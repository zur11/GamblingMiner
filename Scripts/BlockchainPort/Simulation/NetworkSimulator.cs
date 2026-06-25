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

    // RunConsensusRound() (longest-chain reconciliation) was removed in T2: it was a no-op while every node
    // shares one canonical chain (BroadcastBlock keeps them identical). Reintroduce it together with divergent
    // chains (forks / orphan blocks / P2P propagation) — deferred to after Basic Mode (see PRIVATE_ROADMAP
    // "Post-Basic Mode — Divergent Chains / Fork Simulation").
}
