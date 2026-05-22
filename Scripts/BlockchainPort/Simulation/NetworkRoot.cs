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
    private readonly List<string> _botNodeIds = new();
    private readonly Dictionary<string, NodeAgent> _nodesById = new();

    public override void _Ready()
    {
        _playerNode = new NodeAgent("player");
        _network.RegisterNode(_playerNode);
        _nodesById[_playerNode.NodeId] = _playerNode;

        for (int i = 1; i <= 4; i++)
        {
            string botId = $"bot_{i}";
            _botNodeIds.Add(botId);
            NodeAgent bot = new NodeAgent(botId);
            _network.RegisterNode(bot);
            _nodesById[botId] = bot;
        }

        GD.Print($"Network ready. Nodes: {_network.Nodes.Count}");
    }

    public Transaction? PlayerCreateAndBroadcastTransaction(decimal amount, string recipientNodeId)
    {
        NodeAgent? recipient = _network.Nodes.FirstOrDefault(n => n.NodeId == recipientNodeId);
        if (recipient is null)
        {
            GD.PrintErr($"Recipient not found: {recipientNodeId}");
            return null;
        }

        Transaction tx = _playerNode.CreateSignedTransaction(amount, recipient.WalletAddress);
        _playerNode.Blockchain.AddTransactionToPendingTransactions(tx);
        _network.BroadcastTransaction(_playerNode.NodeId, tx);
        return tx;
    }

    public void PlayerMineAndBroadcastBlock()
    {
        Block block = _playerNode.MinePendingTransactions();
        _network.BroadcastBlock(_playerNode.NodeId, block);
        Transaction? rewardTx = _playerNode.Blockchain.PendingTransactions
            .LastOrDefault(t => t.Sender == BlockchainService.CoinbaseSender && t.Recipient == _playerNode.WalletAddress);
        if (rewardTx is not null)
        {
            _network.BroadcastTransaction(_playerNode.NodeId, rewardTx);
        }
    }

    public void RunConsensus()
    {
        _network.RunConsensusRound();
    }

    public IReadOnlyList<string> GetRecipientNodeIds()
    {
        return _botNodeIds;
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
            .Select(n => $"{n.NodeId} | block: {n.Blockchain.Chain.Count} | pending: {n.Blockchain.PendingTransactions.Count}")
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

    public string BuildTransactionDetails(string transactionId)
    {
        (Transaction? tx, Block? block) = _playerNode.Blockchain.GetTransaction(transactionId);
        if (tx is null || block is null)
        {
            Transaction? pending = _playerNode.Blockchain.GetPendingTransaction(transactionId);
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
}
