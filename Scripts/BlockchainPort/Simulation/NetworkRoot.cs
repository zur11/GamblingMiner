using System.Linq;
using System.Collections.Generic;
using Godot;
using GodotBlockchainPort.Blockchain;
#nullable enable

namespace GodotBlockchainPort.Simulation;

public partial class NetworkRoot : Node
{
    private readonly NetworkSimulator _network = new();
    private NodeAgent _playerNode = null!;
    private readonly Dictionary<string, NodeAgent> _nodesById = new();

    public override void _Ready()
    {
        _playerNode = new NodeAgent("player");
        _network.RegisterNode(_playerNode);
        _nodesById[_playerNode.NodeId] = _playerNode;

        for (int i = 1; i <= 4; i++)
        {
            string botId = $"bot_{i}";
            NodeAgent bot = new NodeAgent(botId);
            _network.RegisterNode(bot);
            _nodesById[botId] = bot;
        }

        GD.Print($"Network ready. Nodes: {_network.Nodes.Count}");
    }

    public Transaction? CreateAndBroadcastTransaction(string fromNodeId, string recipientNodeId, decimal amount)
    {
        if (amount <= 0m)
        {
            return null;
        }

        if (!_nodesById.TryGetValue(fromNodeId, out NodeAgent? sender) || !_nodesById.TryGetValue(recipientNodeId, out NodeAgent? recipient))
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

        _network.BroadcastTransaction(sender.NodeId, tx);
        return tx;
    }

    public bool MineAndBroadcastBlock(string minerNodeId)
    {
        if (!_nodesById.TryGetValue(minerNodeId, out NodeAgent? miner))
        {
            return false;
        }

        Block block = miner.MinePendingTransactions();
        _network.BroadcastBlock(miner.NodeId, block);
        Transaction? rewardTx = miner.Blockchain.PendingTransactions
            .LastOrDefault(t => t.Sender == BlockchainService.CoinbaseSender && t.Recipient == miner.WalletAddress);
        if (rewardTx is not null)
        {
            _network.BroadcastTransaction(miner.NodeId, rewardTx);
        }

        return true;
    }

    public bool TryMineSingleNonceAttempt(string minerNodeId, out Block? minedBlock)
    {
        minedBlock = null;
        if (!_nodesById.TryGetValue(minerNodeId, out NodeAgent? miner))
        {
            return false;
        }

        minedBlock = miner.TryMineSingleNonceAttempt();
        if (minedBlock is null)
        {
            return false;
        }

        _network.BroadcastBlock(miner.NodeId, minedBlock);
        Transaction? rewardTx = miner.Blockchain.PendingTransactions
            .LastOrDefault(t => t.Sender == BlockchainService.CoinbaseSender && t.Recipient == miner.WalletAddress);
        if (rewardTx is not null)
        {
            _network.BroadcastTransaction(miner.NodeId, rewardTx);
        }

        return true;
    }

    public void RunConsensus()
    {
        _network.RunConsensusRound();
    }

    public IReadOnlyList<string> GetNodeIds()
    {
        return _nodesById.Keys.OrderBy(x => x).ToList();
    }

    public int GetPlayerChainLength()
    {
        return _playerNode.Blockchain.Chain.Count;
    }

    public int GetPlayerPendingTransactionCount()
    {
        return _playerNode.Blockchain.PendingTransactions.Count;
    }

    public Block GetPlayerLatestBlock()
    {
        return _playerNode.Blockchain.GetLastBlock();
    }

    public IReadOnlyList<string> GetNodeStatusLines()
    {
        return _network.Nodes
            .OrderBy(n => n.NodeId)
            .Select(n => $"{n.NodeId} | block: {n.Blockchain.Chain.Count} | pending: {n.Blockchain.PendingTransactions.Count} | balance: {n.Blockchain.GetAddressSpendableBalance(n.WalletAddress):F8}")
            .ToList();
    }

    public IReadOnlyList<string> GetNodeAddressLines()
    {
        return _nodesById.Values
            .OrderBy(n => n.NodeId)
            .Select(n => $"{n.NodeId}: {n.WalletAddress}")
            .ToList();
    }

    public Block? GetPlayerBlockByIndex(int blockIndex)
    {
        if (blockIndex <= 0)
        {
            return null;
        }

        return _playerNode.Blockchain.Chain.FirstOrDefault(b => b.Index == blockIndex);
    }

    public Block? GetBlockByIndexForNode(string nodeId, int blockIndex)
    {
        if (blockIndex <= 0 || !_nodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return null;
        }

        return node.Blockchain.Chain.FirstOrDefault(b => b.Index == blockIndex);
    }

    public string BuildTransactionDetails(string nodeId, string transactionId)
    {
        if (!_nodesById.TryGetValue(nodeId, out NodeAgent? node))
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

    public string BuildAddressDetails(string address)
    {
        AddressData addressData = _playerNode.Blockchain.GetAddressData(address);
        return
            $"Address: {address}\n" +
            $"Balance: {addressData.AddressBalance:F8}\n" +
            $"Transactions: {addressData.AddressTransactions.Count}";
    }

    public string BuildAddressDetailsForNode(string nodeId, string address)
    {
        if (!_nodesById.TryGetValue(nodeId, out NodeAgent? node))
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
        if (!_nodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return 0m;
        }

        return node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
    }

    public string BuildMiningStatusLine(string nodeId)
    {
        if (!_nodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return "Node not found.";
        }

        int nextBlock = node.Blockchain.GetLastBlock().Index + 1;
        int pending = node.Blockchain.PendingTransactions.Count;
        long nonce = node.GetCurrentCandidateNonce();
        double expected = BlockchainService.GetExpectedAttemptsForCurrentDifficulty();

        return
            $"Miner: {nodeId}\n" +
            $"Next block target: #{nextBlock}\n" +
            $"Current nonce attempt: {nonce}\n" +
            $"Pending tx in candidate: {pending}\n" +
            $"Expected attempts avg: {expected:0}\n" +
            "Attempts per bet: 1";
    }
}
