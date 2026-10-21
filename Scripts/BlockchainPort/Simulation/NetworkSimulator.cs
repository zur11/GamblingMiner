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
		// Mini-plan 26 B (F-a) — the signature is verified ONCE here, and every node is handed the verdict. It reads
		// only the transaction (BlockchainService.ValidateTransactionSignature is static), so it cannot differ between
		// nodes; each node still runs every other admission rule itself. M1 measured the per-node verification at
		// 1.84 ms per input, ~78 times per send (plan §7.1). It is computed here rather than trusted from the
		// sender's own admission (D-26.6), so it does not depend on every caller admitting locally first.
		long verifyBegin = Scripts.Diagnostics.BlockCostProfiler.Clock();
		bool signatureVerdict = BlockchainService.ValidateTransactionSignature(tx);
		Scripts.Diagnostics.BlockCostProfiler.NoteBroadcastVerify(verifyBegin, tx.Inputs.Count);

		bool acceptedByAtLeastOne = false;
		foreach ((string nodeId, NodeAgent node) in _nodes)
		{
			if (nodeId == fromNodeId) continue;
			if (node.Blockchain.AddTransactionToPendingTransactions(tx, signatureVerdict))
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
