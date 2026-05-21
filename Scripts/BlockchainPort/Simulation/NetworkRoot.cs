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

    public override void _Ready()
    {
        _playerNode = new NodeAgent("player");
        _network.RegisterNode(_playerNode);

        for (int i = 1; i <= 4; i++)
        {
            string botId = $"bot_{i}";
            _botNodeIds.Add(botId);
            _network.RegisterNode(new NodeAgent(botId));
        }

        GD.Print($"Network ready. Nodes: {_network.Nodes.Count}");
    }

    public void PlayerCreateAndBroadcastTransaction(decimal amount, string recipientNodeId)
    {
        NodeAgent? recipient = _network.Nodes.FirstOrDefault(n => n.NodeId == recipientNodeId);
        if (recipient is null)
        {
            GD.PrintErr($"Recipient not found: {recipientNodeId}");
            return;
        }

        Transaction tx = _playerNode.CreateSignedTransaction(amount, recipient.WalletPublicKey);
        _playerNode.Blockchain.AddTransactionToPendingTransactions(tx);
        _network.BroadcastTransaction(_playerNode.NodeId, tx);
    }

    public void PlayerMineAndBroadcastBlock()
    {
        Block block = _playerNode.MinePendingTransactions();
        _network.BroadcastBlock(_playerNode.NodeId, block);
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
}
