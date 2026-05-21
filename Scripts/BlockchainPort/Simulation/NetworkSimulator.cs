using System.Collections.Generic;
using System.Linq;
using GodotBlockchainPort.Blockchain;
#nullable enable

namespace GodotBlockchainPort.Simulation;

public sealed class NetworkSimulator
{
    private readonly Dictionary<string, NodeAgent> _nodes = new();

    /*
     * FUTURO: INodeTransport
     * Cuando quieras dar el salto a multijugador real, extrae la comunicacion de esta clase
     * a una interfaz como:
     *
     * public interface INodeTransport
     * {
     *     void BroadcastTransaction(string fromNodeId, Transaction tx);
     *     void BroadcastBlock(string fromNodeId, Block block);
     *     (List<Block> chain, List<Transaction> pending) RequestChainSnapshot(string targetNodeId);
     * }
     *
     * Implementaciones futuras:
     * - InMemoryNodeTransport: la actual simulacion local (single-player con bots).
     * - EnetNodeTransport o WebSocketNodeTransport: nodos en procesos/clientes diferentes.
     */

    public IReadOnlyCollection<NodeAgent> Nodes => _nodes.Values;

    public void RegisterNode(NodeAgent node)
    {
        _nodes[node.NodeId] = node;
    }

    public bool BroadcastTransaction(string fromNodeId, Transaction tx)
    {
        bool acceptedByAtLeastOne = false;
        foreach ((string nodeId, NodeAgent node) in _nodes)
        {
            if (nodeId == fromNodeId) continue;
            if (node.Blockchain.AddTransactionToPendingTransactions(tx))
            {
                acceptedByAtLeastOne = true;
            }
        }

        return acceptedByAtLeastOne;
    }

    public void BroadcastBlock(string fromNodeId, Block block)
    {
        foreach ((string nodeId, NodeAgent node) in _nodes)
        {
            if (nodeId == fromNodeId) continue;
            node.Blockchain.TryAcceptMinedBlock(block);
        }
    }

    public void RunConsensusRound()
    {
        foreach (NodeAgent node in _nodes.Values)
        {
            List<NodeAgent> candidates = _nodes.Values
                .Where(other => other.NodeId != node.NodeId)
                .ToList();

            int maxChainLength = node.Blockchain.Chain.Count;
            List<Block>? newLongestChain = null;
            List<Transaction>? newPendingTransactions = null;

            foreach (NodeAgent candidate in candidates)
            {
                if (candidate.Blockchain.Chain.Count > maxChainLength)
                {
                    maxChainLength = candidate.Blockchain.Chain.Count;
                    newLongestChain = candidate.Blockchain.Chain.ToList();
                    newPendingTransactions = candidate.Blockchain.PendingTransactions.ToList();
                }
            }

            if (newLongestChain is not null && newPendingTransactions is not null)
            {
                node.Blockchain.TryReplaceChain(newLongestChain, newPendingTransactions);
            }
        }
    }
}
