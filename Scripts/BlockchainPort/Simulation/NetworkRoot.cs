using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using GodotBlockchainPort.Blockchain;
#nullable enable

namespace GodotBlockchainPort.Simulation;

public partial class NetworkRoot : Node
{
    private static readonly NetworkSimulator SharedNetwork = new();
    private static readonly Dictionary<string, NodeAgent> SharedNodesById = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool _isInitialized;
    private static Block? _lastMinedBlock;
    private static string _lastMinedByNodeId = string.Empty;
    private static int _currentMinerStreak;
    private static int _bestMinerStreak;

    private const string PlayerNodeId = "player";
    private const string CasinoNodeId = "casino";
    private const decimal GenesisRewardBtc = 50m;
    private const int HalvingIntervalBlocks = 210000;
    private const string BlockchainDir = "user://blockchain";
    private const string StatePath = "user://blockchain/state.json";

    private const int TransactionCirculationStartBlock = 5;
    private const decimal MinBotSpendableBalanceBtc = 1.0m;
    private const double BotSendProbabilityPerBlock = 0.5;
    private const decimal MinSendFractionDecimal = 0.10m;
    private const decimal MaxSendFractionDecimal = 0.40m;

    public override void _Ready()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        // Load saved state first so wallets can be restored before nodes are created.
        BlockchainStateSnapshot? savedState = TryLoadSnapshot();

        SharedNodesById.Clear();
        SharedNetwork.RegisterNode(CreateAndRegisterNode(PlayerNodeId, savedState));
        for (int i = 1; i <= 4; i++)
        {
            SharedNetwork.RegisterNode(CreateAndRegisterNode($"bot_{i}", savedState));
        }

        // Non-miner bots: register as NodeAgents so they can sign and broadcast
        // transactions once they hold a balance. Conditional on HasFullWallet so
        // old registry files (without non-miner keys) skip registration gracefully.
        foreach (BotWalletRecord nonMiner in BotWalletRegistry.NonMinerBots)
        {
            if (nonMiner.HasFullWallet)
                SharedNetwork.RegisterNode(CreateAndRegisterNode(nonMiner.NodeId, savedState));
        }

        // Casino wallet node — keys derived deterministically from seed phrase each launch.
        // Registered here so CasinoFinances can call CreateAndBroadcastTransactionToAddress("casino", ...).
        CasinoWalletState? casinoWalletState = WalletInitializationService.CasinoWallet;
        if (casinoWalletState != null)
        {
            string casinoSeed = string.Join(" ", casinoWalletState.SeedWords);
            (string casinoSignPub, string casinoSignPriv) = CryptoUtils.DeriveSigningKeypair(casinoSeed);
            string casinoSecp256k1 = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(casinoSeed);
            var casinoNode = new NodeAgent(CasinoNodeId, casinoWalletState.BaseAddress,
                                          casinoSignPub, casinoSignPriv, casinoSecp256k1);
            SharedNetwork.RegisterNode(casinoNode);
            SharedNodesById[CasinoNodeId] = casinoNode;
        }

        ApplyStateFromSnapshot(savedState);
        NormalizeGenesisTimestampAcrossNodes();
        EnsureSecondBlockBootstrapPendingTx();
        PersistStateToDisk();
        _isInitialized = true;
    }

    private static NodeAgent CreateAndRegisterNode(string nodeId, BlockchainStateSnapshot? savedState = null)
    {
        NodeAgent node;

        if (nodeId == PlayerNodeId)
        {
            // Player node always uses the seed-phrase wallet so mining coinbase rewards
            // go to the same address shown in BTCWallet. The persisted random wallet is ignored.
            var playerWallet = WalletInitializationService.PlayerWallet;
            if (playerWallet != null)
            {
                string seedPhrase = string.Join(" ", playerWallet.SeedWords);
                var (sigPub, sigPriv) = CryptoUtils.DeriveSigningKeypair(seedPhrase);
                string secp256k1Pub  = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase);
                node = new(nodeId, playerWallet.BaseAddress, sigPub, sigPriv, secp256k1Pub);
            }
            else if (savedState?.NodeWallets?.TryGetValue(nodeId, out NodeWalletSnapshot? pw) == true && pw?.IsComplete() == true)
                node = new(nodeId, pw.Address, pw.SigningPublicKeyBase64, pw.SigningPrivateKeyBase64, pw.Secp256k1PublicKeyBase64);
            else
                node = new(nodeId);
        }
        else
        {
            // Bot nodes: registry (authoritative) → saved snapshot (migration fallback) → fresh random wallet.
            BotWalletRecord? botRecord = BotWalletRegistry.GetBot(nodeId);
            if (botRecord?.HasFullWallet == true)
                node = new(nodeId, botRecord.Address, botRecord.SigningPublicKeyBase64!, botRecord.SigningPrivateKeyBase64!, botRecord.Secp256k1PublicKeyBase64!);
            else if (savedState?.NodeWallets?.TryGetValue(nodeId, out NodeWalletSnapshot? wallet) == true && wallet?.IsComplete() == true)
                node = new(nodeId, wallet.Address, wallet.SigningPublicKeyBase64, wallet.SigningPrivateKeyBase64, wallet.Secp256k1PublicKeyBase64);
            else
                node = new(nodeId);
        }

        SharedNodesById[nodeId] = node;
        return node;
    }

    public Transaction? CreateAndBroadcastTransaction(string fromNodeId, string recipientNodeId, decimal amount)
    {
        EnsureInitialized();
        if (amount <= 0m)
        {
            return null;
        }

        if (!SharedNodesById.TryGetValue(fromNodeId, out NodeAgent? sender) || !SharedNodesById.TryGetValue(recipientNodeId, out NodeAgent? recipient))
        {
            GD.PrintErr($"Invalid route: {fromNodeId} -> {recipientNodeId}");
            return null;
        }

        if (sender.NodeId == recipient.NodeId)
        {
            return null;
        }

        Transaction tx = sender.CreateSignedTransaction(amount, recipient.WalletAddress);
        if (!sender.Blockchain.AddTransactionToPendingTransactions(tx))
        {
            return null;
        }

        SharedNetwork.BroadcastTransaction(sender.NodeId, tx);
        PersistStateToDisk();
        return tx;
    }

    public bool MineAndBroadcastBlock(string minerNodeId, long? minedAtUnixMs = null)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(minerNodeId, out NodeAgent? miner))
        {
            return false;
        }

        decimal reward = GetBlockRewardForNextCandidate(miner);
        Block block = miner.MinePendingTransactions(reward);
        HandleMinedBlock(miner, block, minedAtUnixMs);
        return true;
    }

    public bool TryMineSingleNonceAttempt(string minerNodeId, out Block? minedBlock, long? minedAtUnixMs = null)
    {
        EnsureInitialized();
        minedBlock = null;
        if (!SharedNodesById.TryGetValue(minerNodeId, out NodeAgent? miner))
        {
            return false;
        }

        decimal reward = GetBlockRewardForNextCandidate(miner);
        minedBlock = miner.TryMineSingleNonceAttempt(reward);
        if (minedBlock is null)
        {
            return false;
        }

        HandleMinedBlock(miner, minedBlock, minedAtUnixMs);
        return true;
    }

    private static void HandleMinedBlock(NodeAgent miner, Block block, long? minedAtUnixMs)
    {
        if (minedAtUnixMs.HasValue)
        {
            block.Timestamp = minedAtUnixMs.Value;
        }

        SharedNetwork.BroadcastBlock(miner.NodeId, block);
        Transaction? rewardTx = miner.Blockchain.PendingTransactions
            .LastOrDefault(t => t.Sender == BlockchainService.CoinbaseSender && t.Recipient == miner.WalletAddress);
        if (rewardTx is not null)
        {
            SharedNetwork.BroadcastTransaction(miner.NodeId, rewardTx);
        }

        _lastMinedBlock = block;
        if (string.Equals(_lastMinedByNodeId, miner.NodeId, StringComparison.Ordinal))
        {
            _currentMinerStreak++;
        }
        else
        {
            _lastMinedByNodeId = miner.NodeId;
            _currentMinerStreak = 1;
        }

        if (_currentMinerStreak > _bestMinerStreak)
        {
            _bestMinerStreak = _currentMinerStreak;
        }

        ScheduleBotTransactionsAfterBlock(block);
        PersistStateToDisk();
    }

    private static void ScheduleBotTransactionsAfterBlock(Block block)
    {
        if (block.Index < TransactionCirculationStartBlock) return;

        List<string> recipientPool = BotWalletRegistry.NonMinerBots
            .Select(b => b.Address)
            .ToList();

        if (recipientPool.Count == 0) return;

        foreach (BotWalletRecord record in BotWalletRegistry.MinerBots)
        {
            if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? node)) continue;

            decimal spendable = node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
            if (spendable < MinBotSpendableBalanceBtc) continue;
            if (Random.Shared.NextDouble() >= BotSendProbabilityPerBlock) continue;

            decimal fraction = MinSendFractionDecimal
                + (decimal)Random.Shared.NextDouble() * (MaxSendFractionDecimal - MinSendFractionDecimal);
            decimal sendAmount = Math.Round(spendable * fraction, 8);
            if (sendAmount <= 0m) continue;

            string recipientAddress = recipientPool[Random.Shared.Next(recipientPool.Count)];
            Transaction tx = node.CreateSignedTransaction(sendAmount, recipientAddress);
            if (node.Blockchain.AddTransactionToPendingTransactions(tx))
                SharedNetwork.BroadcastTransaction(node.NodeId, tx);
        }
    }

    private static decimal GetBlockRewardForNextCandidate(NodeAgent miner)
    {
        int nextBlockIndex = miner.Blockchain.GetLastBlock().Index + 1;
        int completedHalvings = Math.Max(0, (nextBlockIndex - 1) / HalvingIntervalBlocks);
        if (completedHalvings >= 32)
        {
            return 0m;
        }

        decimal reward = GenesisRewardBtc;
        for (int i = 0; i < completedHalvings; i++)
        {
            reward /= 2m;
        }

        return reward;
    }

    public void RunConsensus()
    {
        EnsureInitialized();
        SharedNetwork.RunConsensusRound();
        PersistStateToDisk();
    }

    public IReadOnlyList<string> GetNodeIds()
    {
        EnsureInitialized();
        return SharedNodesById.Keys.OrderBy(x => x).ToList();
    }

    public int GetPlayerChainLength()
    {
        EnsureInitialized();
        return SharedNodesById[PlayerNodeId].Blockchain.Chain.Count;
    }

    public int GetPlayerPendingTransactionCount()
    {
        EnsureInitialized();
        return SharedNodesById[PlayerNodeId].Blockchain.PendingTransactions.Count;
    }

    public Block GetPlayerLatestBlock()
    {
        EnsureInitialized();
        return SharedNodesById[PlayerNodeId].Blockchain.GetLastBlock();
    }

    public IReadOnlyList<string> GetNodeStatusLines()
    {
        EnsureInitialized();
        return SharedNetwork.Nodes
            .OrderBy(n => n.NodeId)
            .Select(n => $"{n.NodeId} | block: {n.Blockchain.Chain.Count} | pending: {n.Blockchain.PendingTransactions.Count} | balance: {n.Blockchain.GetAddressSpendableBalance(n.WalletAddress):F8}")
            .ToList();
    }

    public IReadOnlyList<string> GetNodeAddressLines()
    {
        EnsureInitialized();
        return SharedNodesById.Values
            .OrderBy(n => n.NodeId)
            .Select(n => $"{n.NodeId}: {n.WalletAddress}")
            .ToList();
    }

    public Block? GetBlockByIndexForNode(string nodeId, int blockIndex)
    {
        EnsureInitialized();
        if (blockIndex <= 0 || !SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return null;
        }

        return node.Blockchain.Chain.FirstOrDefault(b => b.Index == blockIndex);
    }

    public string BuildTransactionDetails(string nodeId, string transactionId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return "Node not found.";
        }

        (Transaction? tx, Block? block) = node.Blockchain.GetTransaction(transactionId);
        if (tx is null || block is null)
        {
            Transaction? pending = node.Blockchain.GetPendingTransaction(transactionId);
            if (pending is null)
            {
                return "Transaction not found in confirmed or pending sets.";
            }

            return
                $"TxId: {pending.TransactionId}\n" +
                "Status: pending\n" +
                $"Amount: {pending.Amount:F8}\n" +
                $"Sender: {pending.Sender}\n" +
                $"Recipient: {pending.Recipient}";
        }

        return
            $"TxId: {tx.TransactionId}\n" +
            "Status: confirmed\n" +
            $"Block: {block.Index}\n" +
            $"Amount: {tx.Amount:F8}\n" +
            $"Sender: {tx.Sender}\n" +
            $"Recipient: {tx.Recipient}";
    }

    public string BuildAddressDetailsForNode(string nodeId, string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return "Node not found.";
        }

        AddressData addressData = node.Blockchain.GetAddressData(address);
        decimal spendable = node.Blockchain.GetAddressSpendableBalance(address);
        return
            $"Node: {node.NodeId}\n" +
            $"Address: {address}\n" +
            $"Confirmed balance: {addressData.AddressBalance:F8}\n" +
            $"Spendable balance: {spendable:F8}\n" +
            $"Confirmed transactions: {addressData.AddressTransactions.Count}";
    }

    public decimal GetNodeSpendableBalance(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return 0m;
        }

        return node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
    }

    // Returns all confirmed transactions involving address (as sender or recipient),
    // ordered by block index descending. Scans the full player chain.
    public IReadOnlyList<(Transaction tx, int blockIndex)> GetAddressConfirmedTransactions(string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? node))
            return [];
        var result = new List<(Transaction tx, int blockIndex)>();
        foreach (Block block in node.Blockchain.Chain)
        {
            foreach (Transaction tx in block.Transactions)
            {
                if (tx.Sender == address || tx.Recipient == address)
                    result.Add((tx, block.Index));
            }
        }
        result.Sort((a, b) => b.blockIndex.CompareTo(a.blockIndex));
        return result;
    }

    // Creates a signed transaction from a registered node (by nodeId) to any gm1q... address.
    // Used by BotsBtcWallets where the recipient may not be a registered NodeAgent.
    public Transaction? CreateAndBroadcastTransactionToAddress(string fromNodeId, string recipientAddress, decimal amount)
    {
        EnsureInitialized();
        if (amount <= 0m || string.IsNullOrEmpty(recipientAddress))
            return null;
        if (!SharedNodesById.TryGetValue(fromNodeId, out NodeAgent? sender))
        {
            GD.PrintErr($"[NetworkRoot] Unknown sender nodeId: {fromNodeId}");
            return null;
        }
        if (sender.WalletAddress == recipientAddress)
            return null;
        Transaction tx = sender.CreateSignedTransaction(amount, recipientAddress);
        if (!sender.Blockchain.AddTransactionToPendingTransactions(tx))
            return null;
        SharedNetwork.BroadcastTransaction(sender.NodeId, tx);
        PersistStateToDisk();
        return tx;
    }

    // Derives a NodeAgent for a passphrase wallet on demand and registers it in SharedNetwork
    // for the session so it can sign and broadcast transactions. Syncs the player chain so UTXO
    // checks see existing confirmed balance. Returns the nodeId for CreateAndBroadcastTransactionToAddress.
    public string RegisterPassphraseWallet(string seedPhrase, string walletAddress)
    {
        EnsureInitialized();
        string nodeId = $"pass_{walletAddress[4..12]}";
        if (!SharedNodesById.ContainsKey(nodeId))
        {
            (string signPub, string signPriv) = CryptoUtils.DeriveSigningKeypair(seedPhrase);
            string secp256k1Pub = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase);
            var node = new NodeAgent(nodeId, walletAddress, signPub, signPriv, secp256k1Pub);
            if (SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
                node.Blockchain.TryReplaceChain(player.Blockchain.Chain, player.Blockchain.PendingTransactions);
            SharedNetwork.RegisterNode(node);
            SharedNodesById[nodeId] = node;
        }
        return nodeId;
    }

    // Returns confirmed balance and total pending-outgoing for any gm1q... address,
    // queried against the player node's blockchain (the authoritative chain after consensus).
    public (decimal confirmedBalance, decimal pendingOutgoing) GetAddressBalanceDetails(string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? node))
            return (0m, 0m);
        AddressData data = node.Blockchain.GetAddressData(address);
        decimal pendingOut = node.Blockchain.PendingTransactions
            .Where(t => t.Sender == address)
            .Sum(t => t.Amount);
        return (data.AddressBalance, pendingOut);
    }

    public NodeFinancialState GetOrCreateNodeFinancialState(string nodeId, decimal defaultPrincipalBalance, decimal defaultBankrollBalance)
    {
        EnsureInitialized();
        if (!SharedNodesById.ContainsKey(nodeId))
        {
            return new NodeFinancialState();
        }

        NodeAgent node = SharedNodesById[nodeId];
        if (node.FinancialState is null)
        {
            node.FinancialState = new NodeFinancialState
            {
                PrincipalBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, defaultPrincipalBalance)),
                BankrollBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, defaultBankrollBalance)),
                UpdatedAtUtc = DateTime.UtcNow
            };
            PersistStateToDisk();
        }

        return node.FinancialState.Clone();
    }

    public bool HasNodeFinancialState(string nodeId)
    {
        EnsureInitialized();
        return SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) && node.FinancialState is not null;
    }

    public bool HasAnyNodeFinancialState()
    {
        EnsureInitialized();
        return SharedNodesById.Values.Any(node => node.FinancialState is not null);
    }

    public void EnsureMissingNodeFinancialStates(NodeFinancialState template, bool persist = false)
    {
        EnsureInitialized();
        if (template is null)
        {
            return;
        }

        bool changed = false;
        foreach (NodeAgent node in SharedNodesById.Values)
        {
            if (node.FinancialState is not null)
            {
                continue;
            }

            node.FinancialState = template.CloneNormalized();
            node.FinancialState.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        if (changed && persist)
        {
            PersistStateToDisk();
        }
    }

    public void SetNodeFinancialState(string nodeId, NodeFinancialState state, bool persist = false)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) || state is null)
        {
            return;
        }

        node.FinancialState = state.CloneNormalized();
        node.FinancialState.UpdatedAtUtc = DateTime.UtcNow;
        if (persist)
        {
            PersistStateToDisk();
        }
    }

    public string BuildMiningStatusLine(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return "Node not found.";
        }

        int nextBlock = node.Blockchain.GetLastBlock().Index + 1;
        int pending = node.Blockchain.PendingTransactions.Count;
        long nonce = node.GetCurrentCandidateNonce();
        double expected = BlockchainService.GetExpectedAttemptsForCurrentDifficulty();
        decimal reward = GetBlockRewardForNextCandidate(node);

        string lastInfo = _lastMinedBlock is null
            ? "Last mined block: none"
            : $"Last mined block: #{_lastMinedBlock.Index} by {_lastMinedByNodeId}";

        return
            $"Miner: {nodeId}\n" +
            $"Next block target: #{nextBlock}\n" +
            $"Current nonce attempt: {nonce}\n" +
            $"Pending tx in candidate: {pending}\n" +
            $"Next reward: {reward:F8} BTC\n" +
            $"Expected attempts avg: {expected:0}\n" +
            $"Miner streak current/best: {_currentMinerStreak}/{_bestMinerStreak}\n" +
            $"{lastInfo}\n" +
            "Attempts per bet: 1";
    }

    public BlockchainMiningAnnouncement GetLatestMiningAnnouncement()
    {
        EnsureInitialized();
        if (_lastMinedBlock is null)
        {
            return BlockchainMiningAnnouncement.Empty;
        }

        return new BlockchainMiningAnnouncement
        {
            BlockIndex = _lastMinedBlock.Index,
            BlockHash = _lastMinedBlock.Hash,
            Nonce = _lastMinedBlock.Nonce,
            MinerNodeId = _lastMinedByNodeId,
            MinerAddress = _lastMinedBlock.MinedByAddress,
            CurrentMinerStreak = _currentMinerStreak,
            BestMinerStreak = _bestMinerStreak,
            WasPlayer = string.Equals(_lastMinedByNodeId, PlayerNodeId, StringComparison.Ordinal)
        };
    }

    private static void PersistStateToDisk()
    {
        EnsureDirectory(BlockchainDir);
        NodeAgent player = SharedNodesById[PlayerNodeId];

        BlockchainStateSnapshot snapshot = new()
        {
            PlayerChain = player.Blockchain.Chain,
            PlayerPendingTransactions = player.Blockchain.PendingTransactions,
            NodeFinancialStates = SharedNodesById
                .Where(pair => pair.Value.FinancialState is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.FinancialState!.CloneNormalized()),
            NodeWallets = SharedNodesById.ToDictionary(
                pair => pair.Key,
                pair => new NodeWalletSnapshot
                {
                    Address = pair.Value.WalletAddress,
                    SigningPublicKeyBase64 = pair.Value.WalletPublicKey,
                    SigningPrivateKeyBase64 = pair.Value.WalletPrivateKey,
                    Secp256k1PublicKeyBase64 = pair.Value.WalletSecp256k1PublicKey
                }),
            LastMinedByNodeId = _lastMinedByNodeId,
            CurrentMinerStreak = _currentMinerStreak,
            BestMinerStreak = _bestMinerStreak
        };

        using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));

        WriteMonthlyChunks(player.Blockchain.Chain);
    }

    private static void WriteMonthlyChunks(List<Block> chain)
    {
        string absoluteDir = ProjectSettings.GlobalizePath(BlockchainDir);
        if (System.IO.Directory.Exists(absoluteDir))
        {
            foreach (string staleFile in System.IO.Directory.GetFiles(absoluteDir, "blocks-*.json"))
            {
                System.IO.File.Delete(staleFile);
            }
        }

        Dictionary<string, List<Block>> byMonth = chain
            .Where(b => b.Index > 0)
            .GroupBy(b => DateTimeOffset.FromUnixTimeMilliseconds(b.Timestamp).UtcDateTime.ToString("yyyy-MM"))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach ((string month, List<Block> blocks) in byMonth)
        {
            string path = $"{BlockchainDir}/blocks-{month}.json";
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(blocks, JsonOptions));
        }
    }

    private static BlockchainStateSnapshot? TryLoadSnapshot()
    {
        if (!FileAccess.FileExists(StatePath)) return null;
        using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
        string json = file.GetAsText();
        return JsonSerializer.Deserialize<BlockchainStateSnapshot>(json);
    }

    private static void ApplyStateFromSnapshot(BlockchainStateSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.PlayerChain.Count == 0) return;

        foreach (NodeAgent node in SharedNodesById.Values)
            node.Blockchain.TryReplaceChain(snapshot.PlayerChain, snapshot.PlayerPendingTransactions);

        _lastMinedByNodeId = snapshot.LastMinedByNodeId;
        _currentMinerStreak = snapshot.CurrentMinerStreak;
        _bestMinerStreak = snapshot.BestMinerStreak;
        _lastMinedBlock = snapshot.PlayerChain.LastOrDefault();

        foreach ((string nodeId, NodeFinancialState state) in snapshot.NodeFinancialStates ?? new Dictionary<string, NodeFinancialState>())
        {
            if (SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
                node.FinancialState = state.CloneNormalized();
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(path)))
        {
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    private static void NormalizeGenesisTimestampAcrossNodes()
    {
        foreach (NodeAgent node in SharedNodesById.Values)
        {
            if (node.Blockchain.Chain.Count <= 0)
            {
                continue;
            }

            node.Blockchain.Chain[0].Timestamp = BlockchainService.GenesisTimestampUnixMs;
            if (node.Blockchain.Chain[0].Transactions.Count == 0)
            {
                node.Blockchain.Chain[0].Transactions.Add(BlockchainService.CreateGenesisCoinbase());
            }
        }
    }

    private static void EnsureSecondBlockBootstrapPendingTx()
    {
        NodeAgent player = SharedNodesById[PlayerNodeId];
        bool alreadyExists =
            player.Blockchain.ContainsTransactionId(BlockchainService.BootstrapSecondBlockTxId);
        if (alreadyExists || player.Blockchain.Chain.Count != 1)
        {
            return;
        }

        Transaction bootstrapTx = new()
        {
            Amount = 50m,
            Sender = BlockchainService.CoinbaseSender,
            Recipient = BlockchainService.SatoshiAddress,
            TransactionId = BlockchainService.BootstrapSecondBlockTxId,
            InputDataText = "Bootstrap payout to Satoshi address in block 2",
            InputDataHex = BlockchainService.TextToHex("Bootstrap payout to Satoshi address in block 2"),
            IsSpendable = true
        };

        if (!player.Blockchain.AddTransactionToPendingTransactions(bootstrapTx))
        {
            return;
        }

        SharedNetwork.BroadcastTransaction(player.NodeId, bootstrapTx);
    }

    private sealed class BlockchainStateSnapshot
    {
        public List<Block> PlayerChain { get; set; } = new();
        public List<Transaction> PlayerPendingTransactions { get; set; } = new();
        public Dictionary<string, NodeFinancialState> NodeFinancialStates { get; set; } = new();
        public Dictionary<string, NodeWalletSnapshot> NodeWallets { get; set; } = new();
        public string LastMinedByNodeId { get; set; } = string.Empty;
        public int CurrentMinerStreak { get; set; }
        public int BestMinerStreak { get; set; }
    }

    private sealed class NodeWalletSnapshot
    {
        public string Address { get; set; } = string.Empty;
        public string SigningPublicKeyBase64 { get; set; } = string.Empty;
        public string SigningPrivateKeyBase64 { get; set; } = string.Empty;
        public string Secp256k1PublicKeyBase64 { get; set; } = string.Empty;

        public bool IsComplete() =>
            !string.IsNullOrWhiteSpace(Address) &&
            !string.IsNullOrWhiteSpace(SigningPublicKeyBase64) &&
            !string.IsNullOrWhiteSpace(SigningPrivateKeyBase64) &&
            !string.IsNullOrWhiteSpace(Secp256k1PublicKeyBase64);
    }
}

public sealed class NodeFinancialState
{
    public decimal PrincipalBalance { get; set; }
    public decimal BankrollBalance { get; set; }
    public decimal AutoRechargeAmount { get; set; } = BankrollProgramService.DefaultAutoRechargeAmount;
    public List<BankrollProgramService.TransferRecord> TransferRecords { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public NodeFinancialState Clone() => new()
    {
        PrincipalBalance = PrincipalBalance,
        BankrollBalance = BankrollBalance,
        AutoRechargeAmount = AutoRechargeAmount,
        TransferRecords = TransferRecords?.Select(CloneTransferRecord).ToList() ?? new List<BankrollProgramService.TransferRecord>(),
        UpdatedAtUtc = UpdatedAtUtc
    };

    public NodeFinancialState CloneNormalized()
    {
        NodeFinancialState clone = Clone();
        clone.PrincipalBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, clone.PrincipalBalance));
        clone.BankrollBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, clone.BankrollBalance));
        clone.AutoRechargeAmount = clone.AutoRechargeAmount > 0m
            ? Scripts.Finance.Money.Normalize(clone.AutoRechargeAmount)
            : BankrollProgramService.DefaultAutoRechargeAmount;
        clone.UpdatedAtUtc = clone.UpdatedAtUtc == default ? DateTime.UtcNow : clone.UpdatedAtUtc;
        return clone;
    }

    private static BankrollProgramService.TransferRecord CloneTransferRecord(BankrollProgramService.TransferRecord record) => new()
    {
        UtcTimestamp = DateTime.SpecifyKind(record.UtcTimestamp, DateTimeKind.Utc),
        Amount = Scripts.Finance.Money.Normalize(Math.Max(0m, record.Amount)),
        Direction = record.Direction ?? string.Empty,
        Reason = record.Reason ?? string.Empty
    };
}

public sealed class BlockchainMiningAnnouncement
{
    public static BlockchainMiningAnnouncement Empty { get; } = new();
    public int BlockIndex { get; set; }
    public string BlockHash { get; set; } = string.Empty;
    public long Nonce { get; set; }
    public string MinerNodeId { get; set; } = string.Empty;
    public string MinerAddress { get; set; } = string.Empty;
    public int CurrentMinerStreak { get; set; }
    public int BestMinerStreak { get; set; }
    public bool WasPlayer { get; set; }
}
