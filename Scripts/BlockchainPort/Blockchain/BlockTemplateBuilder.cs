using System;
using System.Collections.Generic;
using System.Linq;

namespace GodotBlockchainPort.Blockchain;

// Step 4b — builds a miner's candidate block from its mempool view.
// Selects up to (MaxBlockTransactions - 1) mempool transactions by fee (highest first; equal fees
// keep arrival order = age tie-break, since the mempool list is in arrival order and OrderByDescending
// is stable), then prepends a coinbase paying the miner the block reward + the selected fees.
//
// OQ-C4: the cap counts the coinbase (24 = coinbase + up to 23 mempool txs).
// OQ-C2: uniform tx size for now, so fee ordering == feerate ordering.
public static class BlockTemplateBuilder
{
    public const int MaxBlockTransactions = 24;

    public static BlockTemplate Build(string minerAddress, decimal blockReward, IReadOnlyList<Transaction> mempool)
    {
        List<Transaction> selected = mempool
            .OrderByDescending(t => t.Fee)
            .Take(MaxBlockTransactions - 1)
            .ToList();

        decimal feeTotal = selected.Sum(t => t.Fee);

        var coinbase = new Transaction
        {
            Amount = blockReward + feeTotal,
            Sender = BlockchainService.CoinbaseSender,
            Recipient = minerAddress,
            TransactionId = Guid.NewGuid().ToString("N"), // content-hash txid arrives in 4b.3 (OQ-C6)
            IsSpendable = true
        };

        var blockTransactions = new List<Transaction>(selected.Count + 1) { coinbase };
        blockTransactions.AddRange(selected);

        string merkleRoot = MerkleTree.ComputeRoot(blockTransactions);
        return new BlockTemplate(blockTransactions, selected, coinbase, merkleRoot);
    }
}

// BlockTransactions = [coinbase, ...selected]; SelectedMempoolTxs = the mempool txs to remove from
// the miner's pending pool on commit; Coinbase and MerkleRoot are conveniences for the miner.
public sealed record BlockTemplate(
    List<Transaction> BlockTransactions,
    List<Transaction> SelectedMempoolTxs,
    Transaction Coinbase,
    string MerkleRoot
);
