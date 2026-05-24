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
    private const decimal GenesisRewardBtc = 50m;
    private const int HalvingIntervalBlocks = 210000;
    private const string BlockchainDir = "user://blockchain";
    private const string StatePath = "user://blockchain/state.json";

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

        SharedNodesById.Clear();
        SharedNetwork.RegisterNode(CreateAndRegisterNode(PlayerNodeId));
        for (int i = 1; i <= 4; i++)
        {
            SharedNetwork.RegisterNode(CreateAndRegisterNode($"bot_{i}"));
        }

        LoadStateFromDisk();
        _isInitialized = true;
    }

    private static NodeAgent CreateAndRegisterNode(string nodeId)
    {
        NodeAgent node = new(nodeId);
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

        PersistStateToDisk();
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

    private static void LoadStateFromDisk()
    {
        if (!FileAccess.FileExists(StatePath))
        {
            return;
        }

        using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
        string json = file.GetAsText();
        BlockchainStateSnapshot? snapshot = JsonSerializer.Deserialize<BlockchainStateSnapshot>(json);
        if (snapshot is null || snapshot.PlayerChain.Count == 0)
        {
            return;
        }

        foreach (NodeAgent node in SharedNodesById.Values)
        {
            node.Blockchain.TryReplaceChain(snapshot.PlayerChain, snapshot.PlayerPendingTransactions);
        }

        _lastMinedByNodeId = snapshot.LastMinedByNodeId;
        _currentMinerStreak = snapshot.CurrentMinerStreak;
        _bestMinerStreak = snapshot.BestMinerStreak;
        _lastMinedBlock = snapshot.PlayerChain.LastOrDefault();
    }

    private static void EnsureDirectory(string path)
    {
        if (DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(path)))
        {
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    private sealed class BlockchainStateSnapshot
    {
        public List<Block> PlayerChain { get; set; } = new();
        public List<Transaction> PlayerPendingTransactions { get; set; } = new();
        public string LastMinedByNodeId { get; set; } = string.Empty;
        public int CurrentMinerStreak { get; set; }
        public int BestMinerStreak { get; set; }
    }
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
