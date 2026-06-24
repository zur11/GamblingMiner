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
    // When true (during the historical bootstrap), per-block persistence and bot recirculation are
    // suppressed so ~114 blocks can be mined in one pass; the bootstrap persists once at the end.
    private static bool _bulkMining;
    private static Block? _lastMinedBlock;
    private static string _lastMinedByNodeId = string.Empty;
    private static int _currentMinerStreak;
    private static int _bestMinerStreak;
    // D.2 hybrid difficulty: total active mining power (Σ active miners' bets/sec), pushed in by
    // SimulationService. The feed-forward term in GetNextBlockDifficulty reads this. 0 = unknown (bootstrap/idle).
    private static double _activeMiningPower;

    private const string PlayerNodeId = "player";
    private const string CasinoNodeId = "casino";
    private const string SatoshiNodeId = "satoshi";
    private const string HalNodeId = "hal";
    private const decimal GenesisRewardBtc = 50m;
    // 50 × 2100 × 2 = 210,000 BTC total supply; ~4 in-game years per halving at 100X scale.
    // If this value changes, recalculate the emission cap in GetBlockRewardForNextCandidate() to preserve the ~2140 end-of-supply year.
    private const int HalvingIntervalBlocks = 2100;
    private const string BlockchainDir = "user://blockchain";
    private const string StatePath = "user://blockchain/state.json";

    // Blocks a miner bot must wait AFTER its own first mined block before it starts donating BTC —
    // measured per bot, so it works for bots introduced gradually (not an absolute chain index).
    private const int CirculationWarmupBlocks = 5;
    private const decimal MinBotSpendableBalanceBtc = 1.0m;
    private const double BotSendProbabilityPerBlock = 0.5;
    private const decimal MinSendFractionDecimal = 0.10m;
    private const decimal MaxSendFractionDecimal = 0.40m;
    // Step 4b.2: bot-chosen fee range (BTC), collected into the winning miner's coinbase.
    private const decimal MinBotFeeBtc = 0.1m;
    private const decimal MaxBotFeeBtc = 1.0m;
    // Referral auction (starter, option-b gradual introduction): a new non-miner enters the auction
    // every ~2 in-game days after live mining begins; each runs a 7-in-game-day donation window.
    private const long NonMinerIntroIntervalMs = 2L * 86_400_000L;
    private const long AuctionWindowMs = 7L * 86_400_000L;

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

        // Founder nodes — Satoshi & Hal. Keys derived from their seed phrases each launch,
        // same pattern as the casino. Registered before ApplyStateFromSnapshot so they receive
        // the synced chain. They mine via the weighted lottery introduced in a later step; here
        // they exist as nodes whose addresses receive the genesis / early coinbase rewards.
        RegisterFounderNode(WalletInitializationService.SatoshiWallet);
        RegisterFounderNode(WalletInitializationService.HalWallet);

        ApplyStateFromSnapshot(savedState);
        NormalizeGenesisAcrossNodes();
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

    private static void RegisterFounderNode(FounderWalletState? founder)
    {
        if (founder is null)
        {
            return;
        }

        string seed = string.Join(" ", founder.SeedWords);
        (string signPub, string signPriv) = CryptoUtils.DeriveSigningKeypair(seed);
        string secp256k1Pub = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seed);
        var node = new NodeAgent(founder.FounderId, founder.BaseAddress, signPub, signPriv, secp256k1Pub);
        SharedNetwork.RegisterNode(node);
        SharedNodesById[founder.FounderId] = node;
    }

    public Transaction? CreateAndBroadcastTransaction(string fromNodeId, string recipientNodeId, decimal amount, decimal fee = 0m)
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

        Transaction tx = sender.CreateSignedTransaction(amount, recipient.WalletAddress, fee);
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

        MineForNode(miner, minedAtUnixMs);
        return true;
    }

    // Shared mining core: full PoW for one block by the given node, then broadcast + bookkeeping.
    // The timestamp is fixed before mining (it is part of the hashed header — Step 4).
    private static void MineForNode(NodeAgent miner, long? minedAtUnixMs)
    {
        long timestamp = minedAtUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        decimal reward = GetBlockRewardForNextCandidate(miner);
        // During the scripted historical bootstrap, pin difficulty to InitialDifficulty (the regulator can't
        // run meaningfully on pre-scripted timestamps). Live mining uses the regulator via TryMineSingleNonceAttempt.
        double? forcedDifficulty = _bulkMining ? BlockchainService.InitialDifficulty : (double?)null;
        Block block = miner.MinePendingTransactions(reward, timestamp, _activeMiningPower, forcedDifficulty);
        HandleMinedBlock(miner, block);
    }

    // ── Step 3a: static surface for the historical bootstrap ───────────────────
    // These let HistoricalBootstrapService drive the engine from CalendarTimeService._Ready()
    // before any scene (and thus any NetworkRoot Node instance) exists.

    public static void EnsureReady() => EnsureInitialized();

    public static int GetPlayerChainLengthStatic() =>
        SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player) ? player.Blockchain.Chain.Count : 0;

    public static bool MineNodeStatic(string nodeId, long minedAtUnixMs)
    {
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? miner))
        {
            return false;
        }

        MineForNode(miner, minedAtUnixMs);
        return true;
    }

    public static void BeginBulkMining() => _bulkMining = true;

    public static void EndBulkMiningAndPersist()
    {
        _bulkMining = false;
        PersistStateToDisk();
    }

    // ── Step 2: weighted block lottery ─────────────────────────────────────────
    // Picks ONE winner among the given miner node ids with probability proportional to each
    // node's HashrateWeight, then mines exactly one valid block for that winner (full PoW nonce
    // search via the existing MineAndBroadcastBlock path) and broadcasts it. Returns the winning
    // node id, or null if no eligible (registered, weight > 0) miner was supplied.
    //
    // This is the mechanism the historical bootstrap (Step 3) uses to let Satoshi + Hal mine the
    // chain to 21 Mar 2009 without the player betting. Bet-driven player mining is unaffected.
    // rng is injectable so the bootstrap / tests can be made deterministic; defaults to Random.Shared.
    public string? RunWeightedBlockLottery(IReadOnlyList<string> minerNodeIds, long? minedAtUnixMs = null, Random? rng = null)
    {
        EnsureInitialized();
        rng ??= Random.Shared;

        double totalWeight = 0d;
        var eligible = new List<(NodeAgent node, double weight)>();
        foreach (string id in minerNodeIds)
        {
            if (!SharedNodesById.TryGetValue(id, out NodeAgent? node) || node.HashrateWeight <= 0d)
            {
                continue;
            }

            eligible.Add((node, node.HashrateWeight));
            totalWeight += node.HashrateWeight;
        }

        if (eligible.Count == 0 || totalWeight <= 0d)
        {
            return null;
        }

        double roll = rng.NextDouble() * totalWeight;
        NodeAgent winner = eligible[^1].node;
        double cumulative = 0d;
        foreach ((NodeAgent node, double weight) in eligible)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                winner = node;
                break;
            }
        }

        return MineAndBroadcastBlock(winner.NodeId, minedAtUnixMs) ? winner.NodeId : null;
    }

    public void SetHashrateWeight(string nodeId, double weight)
    {
        EnsureInitialized();
        if (SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            node.HashrateWeight = Math.Max(0d, weight);
        }
    }

    public double GetHashrateWeight(string nodeId)
    {
        EnsureInitialized();
        return SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) ? node.HashrateWeight : 0d;
    }

    public bool TryMineSingleNonceAttempt(string minerNodeId, out Block? minedBlock, long? minedAtUnixMs = null)
    {
        EnsureInitialized();
        minedBlock = null;
        if (!SharedNodesById.TryGetValue(minerNodeId, out NodeAgent? miner))
        {
            return false;
        }

        long timestamp = minedAtUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        decimal reward = GetBlockRewardForNextCandidate(miner);
        minedBlock = miner.TryMineSingleNonceAttempt(reward, timestamp, _activeMiningPower);
        if (minedBlock is null)
        {
            return false;
        }

        HandleMinedBlock(miner, minedBlock);
        return true;
    }

    // Total active mining power (Σ active miners' bets/sec) for the difficulty feed-forward. Set by
    // SimulationService while a player autobet runs; 0 when idle/bootstrapping (feed-forward then no-ops).
    public void SetActiveMiningPower(double power)
    {
        _activeMiningPower = power > 0d ? power : 0d;
    }

    private static void HandleMinedBlock(NodeAgent miner, Block block)
    {
        // Step 4b: the coinbase now lives inside the block (BlockTemplateBuilder), so it propagates
        // with BroadcastBlock — no separate coinbase-transaction broadcast is needed.
        SharedNetwork.BroadcastBlock(miner.NodeId, block);

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

        if (!_bulkMining)
        {
            ScheduleBotTransactionsAfterBlock(block);
            PersistStateToDisk();
        }
    }

    private static void ScheduleBotTransactionsAfterBlock(Block block)
    {
        // Donations target only non-miners currently in their open auction window (recruitable),
        // at this block's time. Empty until the first non-miner is introduced (after live mining begins).
        List<string> recipientPool = InAuctionNonMinerAddresses(block.Timestamp);

        if (recipientPool.Count == 0) return;

        // Canonical chain (the player's synced view) — used to measure each bot's mining warmup.
        List<Block> chain = SharedNodesById[PlayerNodeId].Blockchain.Chain;

        foreach (BotWalletRecord record in BotWalletRegistry.MinerBots)
        {
            if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? node)) continue;

            // Warmup measured PER BOT from the block it first mined — so circulation starts a few
            // blocks after a miner bot actually begins mining (works for bots introduced gradually,
            // not an absolute chain index that the historical bootstrap would have already passed).
            int? firstMinedHeight = FirstBlockHeightMinedBy(record.NodeId, chain);
            if (firstMinedHeight is null) continue; // hasn't mined yet → nothing to circulate
            if (block.Index - firstMinedHeight.Value < CirculationWarmupBlocks) continue;

            decimal spendable = node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
            if (spendable < MinBotSpendableBalanceBtc) continue;
            if (Random.Shared.NextDouble() >= BotSendProbabilityPerBlock) continue;

            decimal fraction = MinSendFractionDecimal
                + (decimal)Random.Shared.NextDouble() * (MaxSendFractionDecimal - MinSendFractionDecimal);
            decimal sendAmount = Math.Round(spendable * fraction, 8);
            if (sendAmount <= 0m) continue;

            // Step 4b.2: attach a sender-chosen fee (collected into the miner's coinbase).
            decimal fee = Math.Round(MinBotFeeBtc + (decimal)Random.Shared.NextDouble() * (MaxBotFeeBtc - MinBotFeeBtc), 8);
            if (sendAmount + fee > spendable) continue; // must cover amount + fee

            string recipientAddress = recipientPool[Random.Shared.Next(recipientPool.Count)];
            if (recipientAddress == node.WalletAddress) continue; // never send to self (recipients are non-miners, but be safe)

            Transaction tx = node.CreateSignedTransaction(sendAmount, recipientAddress, fee);
            if (node.Blockchain.AddTransactionToPendingTransactions(tx))
                SharedNetwork.BroadcastTransaction(node.NodeId, tx);
        }
    }

    // Index of the first block in the chain mined by nodeId, or null if it has never mined.
    private static int? FirstBlockHeightMinedBy(string nodeId, List<Block> chain)
    {
        foreach (Block b in chain)
        {
            if (string.Equals(b.MinedByNodeId, nodeId, StringComparison.Ordinal))
            {
                return b.Index;
            }
        }

        return null;
    }

    private static decimal GetBlockRewardForNextCandidate(NodeAgent miner)
    {
        int nextBlockIndex = miner.Blockchain.GetLastBlock().Index + 1;
        int completedHalvings = Math.Max(0, (nextBlockIndex - 1) / HalvingIntervalBlocks);
        // Cap derived from HalvingIntervalBlocks: 34 × 2100 = 71,400 blocks ≈ in-game year 2141.
        if (completedHalvings >= 34)
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

    // Nodes that legitimately participate in DiceGame betting: the player and the miner bots.
    // Excludes the casino, founders (satoshi/hal), and non-miner holder bots — none of those bet.
    // Founder mining is driven by the weighted lottery / historical bootstrap, never by DiceGame.
    public IReadOnlyList<string> GetBettableNodeIds()
    {
        EnsureInitialized();
        var ids = new List<string> { PlayerNodeId };
        foreach (BotWalletRecord miner in BotWalletRegistry.MinerBots)
        {
            if (SharedNodesById.ContainsKey(miner.NodeId))
            {
                ids.Add(miner.NodeId);
            }
        }

        return ids;
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

    // Average in-game seconds between the last `window` player blocks (the signal the difficulty regulator
    // targets). 0 if there aren't enough blocks yet. (D.3 — Block Explorer difficulty readout.)
    public double GetPlayerRecentAverageBlockSeconds(int window)
    {
        EnsureInitialized();
        List<Block> chain = SharedNodesById[PlayerNodeId].Blockchain.Chain;
        if (chain.Count < 2) return 0d;

        int deltas = Math.Min(window, chain.Count - 1);
        double sum = 0d;
        for (int k = 0; k < deltas; k++)
        {
            sum += (chain[chain.Count - 1 - k].Timestamp - chain[chain.Count - 2 - k].Timestamp) / 1000d;
        }
        return sum / deltas;
    }

    // Difficulty the block CURRENTLY being mined will use (next-block difficulty at the current network
    // power), for the Block Explorer's main "mining" readout — distinct from any already-mined block's value.
    public double GetPlayerNextBlockDifficulty()
    {
        EnsureInitialized();
        NodeAgent player = SharedNodesById[PlayerNodeId];
        // Prefer the LOCKED difficulty of the candidate currently being mined (fixed until that block is found,
        // so a power change shows up only from the next block). Fall back to the prospective value when idle.
        double candidate = player.GetCurrentCandidateDifficulty();
        return candidate > 0d ? candidate : player.Blockchain.GetNextBlockDifficulty(_activeMiningPower);
    }

    // The difficulty stamped on the player block `blocksAgo` back from the tip (clamped to the genesis end),
    // for showing a rising/falling difficulty trend. (D.3.)
    public double GetPlayerDifficultyBlocksAgo(int blocksAgo)
    {
        EnsureInitialized();
        List<Block> chain = SharedNodesById[PlayerNodeId].Blockchain.Chain;
        if (chain.Count == 0) return 0d;
        int index = Math.Max(0, chain.Count - 1 - Math.Max(0, blocksAgo));
        return chain[index].Difficulty;
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

    // Maps an address to a registered node id for display, or a shortened address if unknown.
    public string DescribeAddress(string address)
    {
        EnsureInitialized();
        foreach (NodeAgent node in SharedNodesById.Values)
        {
            if (node.WalletAddress == address)
            {
                return node.NodeId;
            }
        }

        return address.Length > 12 ? address[..12] + "…" : address;
    }

    // Referral auction ledger (starter, option-(b) gradual introduction). Fully DERIVED from the
    // canonical chain — no persisted state. Non-miner holder bots enter the auction one at a time
    // after live mining begins; each runs a 7-in-game-day donation window; the top donor at window
    // close wins the referral permanently. Coinbase txs excluded. "now" = latest block timestamp.
    public IReadOnlyList<NonMinerDonationSummary> GetNonMinerAuctionLedger()
    {
        EnsureInitialized();
        long nowMs = SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? p) && p.Blockchain.Chain.Count > 0
            ? p.Blockchain.Chain[^1].Timestamp
            : 0;
        return ComputeAuctionLedger(nowMs);
    }

    private static List<NonMinerDonationSummary> ComputeAuctionLedger(long nowMs)
    {
        List<BotWalletRecord> nonMiners = BotWalletRegistry.NonMinerBots.ToList();
        var donations = new Dictionary<string, List<(string donor, decimal amount, long ts)>>();
        foreach (BotWalletRecord b in nonMiners)
        {
            donations[b.Address] = new List<(string, decimal, long)>();
        }

        SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player);
        if (player is not null)
        {
            foreach (Block block in player.Blockchain.Chain)
            {
                foreach (Transaction tx in block.Transactions)
                {
                    if (tx.Sender == BlockchainService.CoinbaseSender) continue;
                    if (donations.TryGetValue(tx.Recipient, out List<(string, decimal, long)>? list))
                    {
                        list.Add((tx.Sender, tx.Amount, block.Timestamp));
                    }
                }
            }
        }

        long? firstLiveTs = FirstLiveBlockTimestamp(player);

        var result = new List<NonMinerDonationSummary>();
        for (int i = 0; i < nonMiners.Count; i++)
        {
            BotWalletRecord b = nonMiners[i];
            List<(string donor, decimal amount, long ts)> list = donations[b.Address];

            var summary = new NonMinerDonationSummary
            {
                NonMinerNodeId = b.NodeId,
                NonMinerAddress = b.Address,
                TotalReceived = list.Sum(d => d.amount),
                DonorCount = list.Select(d => d.donor).Distinct().Count()
            };

            (string addr, decimal total) leader = TopDonor(list, long.MaxValue);
            summary.LeadingDonorAddress = leader.addr;
            summary.LeadingDonorTotal = leader.total;

            if (firstLiveTs is null)
            {
                summary.Status = NonMinerAuctionStatus.NotIntroduced;
                result.Add(summary);
                continue;
            }

            summary.IntroUnixMs = firstLiveTs.Value + i * NonMinerIntroIntervalMs;
            summary.WindowCloseUnixMs = summary.IntroUnixMs + AuctionWindowMs;

            if (nowMs < summary.IntroUnixMs) summary.Status = NonMinerAuctionStatus.NotIntroduced;
            else if (nowMs < summary.WindowCloseUnixMs) summary.Status = NonMinerAuctionStatus.InAuction;
            else
            {
                summary.Status = NonMinerAuctionStatus.Resolved;
                summary.WinnerAddress = TopDonor(list, summary.WindowCloseUnixMs).addr; // donors confirmed by close
            }

            result.Add(summary);
        }

        return result;
    }

    private static (string addr, decimal total) TopDonor(List<(string donor, decimal amount, long ts)> list, long maxTsInclusive)
    {
        var totals = new Dictionary<string, decimal>();
        foreach ((string donor, decimal amount, long ts) in list)
        {
            if (ts > maxTsInclusive) continue;
            totals.TryGetValue(donor, out decimal cur);
            totals[donor] = cur + amount;
        }

        if (totals.Count == 0) return (string.Empty, 0m);
        KeyValuePair<string, decimal> top = totals.OrderByDescending(kv => kv.Value).First();
        return (top.Key, top.Value);
    }

    // Timestamp of the first block mined by a non-founder (live era ≈ 21 Mar player start); null if none yet.
    private static long? FirstLiveBlockTimestamp(NodeAgent? player)
    {
        if (player is null) return null;
        foreach (Block b in player.Blockchain.Chain)
        {
            if (b.Index > 1 && !string.IsNullOrEmpty(b.MinedByNodeId)
                && b.MinedByNodeId != SatoshiNodeId && b.MinedByNodeId != HalNodeId)
            {
                return b.Timestamp;
            }
        }

        return null;
    }

    // Addresses of non-miners currently in their open auction window (recruitable) at the given time.
    private static List<string> InAuctionNonMinerAddresses(long nowMs)
    {
        return ComputeAuctionLedger(nowMs)
            .Where(s => s.Status == NonMinerAuctionStatus.InAuction)
            .Select(s => s.NonMinerAddress)
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
                $"Fee: {pending.Fee:F8}\n" +
                $"Sender: {pending.Sender}\n" +
                $"Recipient: {pending.Recipient}";
        }

        return
            $"TxId: {tx.TransactionId}\n" +
            "Status: confirmed\n" +
            $"Block: {block.Index}\n" +
            $"Amount: {tx.Amount:F8}\n" +
            $"Fee: {tx.Fee:F8}\n" +
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
    public Transaction? CreateAndBroadcastTransactionToAddress(string fromNodeId, string recipientAddress, decimal amount, decimal fee = 0m)
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
        Transaction tx = sender.CreateSignedTransaction(amount, recipientAddress, fee);
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
        double expected = node.Blockchain.GetExpectedAttemptsForCurrentDifficulty();
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

    private static void NormalizeGenesisAcrossNodes()
    {
        // Genesis coinbase is created in the BlockchainService ctor with the historical base58
        // placeholder (BlockchainService.SatoshiAddress). Once Satoshi's wallet exists we rewrite
        // the recipient to his derived gm1q… address so the genesis reward belongs to the founder
        // node. Genesis stays IsSpendable = false. ChainIsValid does not check the recipient, so
        // this rewrite does not invalidate the chain.
        string? satoshiAddress = WalletInitializationService.SatoshiWallet?.BaseAddress;

        foreach (NodeAgent node in SharedNodesById.Values)
        {
            if (node.Blockchain.Chain.Count <= 0)
            {
                continue;
            }

            Block genesis = node.Blockchain.Chain[0];
            genesis.Timestamp = BlockchainService.GenesisTimestampUnixMs;
            if (genesis.Transactions.Count == 0)
            {
                genesis.Transactions.Add(BlockchainService.CreateGenesisCoinbase());
            }

            if (satoshiAddress is not null)
            {
                foreach (Transaction tx in genesis.Transactions)
                {
                    if (tx.Sender == BlockchainService.CoinbaseSender && tx.Recipient == BlockchainService.SatoshiAddress)
                    {
                        tx.Recipient = satoshiAddress;
                    }
                }
            }

            // Keep the genesis Merkle root consistent with its (possibly rewritten) coinbase.
            genesis.MerkleRoot = MerkleTree.ComputeRoot(genesis.Transactions);
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

        // Block-2 payout goes to Satoshi's derived gm1q… address (falls back to the historical
        // base58 placeholder only if the founder wallet is somehow unavailable).
        string satoshiAddress = WalletInitializationService.SatoshiWallet?.BaseAddress ?? BlockchainService.SatoshiAddress;

        Transaction bootstrapTx = new()
        {
            Amount = 50m,
            Sender = BlockchainService.CoinbaseSender,
            Recipient = satoshiAddress,
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

public enum NonMinerAuctionStatus { NotIntroduced, InAuction, Resolved }

// Donation-race + auction summary for one non-miner holder bot (referral-system starter).
public sealed class NonMinerDonationSummary
{
    public string NonMinerNodeId { get; set; } = string.Empty;
    public string NonMinerAddress { get; set; } = string.Empty;
    public decimal TotalReceived { get; set; }
    public int DonorCount { get; set; }
    public string LeadingDonorAddress { get; set; } = string.Empty;
    public decimal LeadingDonorTotal { get; set; }
    public NonMinerAuctionStatus Status { get; set; } = NonMinerAuctionStatus.NotIntroduced;
    public long IntroUnixMs { get; set; }
    public long WindowCloseUnixMs { get; set; }
    public string WinnerAddress { get; set; } = string.Empty; // set when Resolved ("" if no donors)
}
