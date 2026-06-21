using System.Collections.Generic;
using System.Linq;

namespace GodotBlockchainPort.Blockchain;

// Step 4 — Bitcoin-style Merkle tree over a block's transactions.
// Leaves are a double-SHA256 content hash of each transaction; the tree pairs hashes and
// duplicates the last one on an odd count, until a single root remains. Because the root is part
// of the hashed block header (BlockchainService.HashHeader), any change to a transaction's
// content changes the root and therefore the block hash — tamper-evidence.
//
// OQ-C6 (4b.3): the leaf hash IS the transaction's content-hash txid
// (BlockchainService.ComputeTransactionId) — id and Merkle leaf are now the same value.
public static class MerkleTree
{
    // The Merkle leaf is the transaction's content hash — the same value used as its txid (Step 4b.3).
    // Recomputed from content (never read from the stored id), so tampering with any field changes the
    // root and invalidates the block.
    public static string LeafHash(Transaction tx) => BlockchainService.ComputeTransactionId(tx);

    public static string ComputeRoot(IReadOnlyList<Transaction> transactions)
    {
        if (transactions is null || transactions.Count == 0)
        {
            // Empty tree → hash of the empty string (a stable sentinel; coinbase-only blocks
            // always have ≥1 tx, so this is mostly defensive).
            return DoubleSha256(string.Empty);
        }

        List<string> level = transactions.Select(LeafHash).ToList();

        while (level.Count > 1)
        {
            var next = new List<string>((level.Count + 1) / 2);
            for (int i = 0; i < level.Count; i += 2)
            {
                string left = level[i];
                string right = (i + 1 < level.Count) ? level[i + 1] : left; // duplicate last if odd
                next.Add(DoubleSha256(left + right));
            }

            level = next;
        }

        return level[0];
    }

    private static string DoubleSha256(string input) => CryptoUtils.Sha256Hex(CryptoUtils.Sha256Hex(input));
}
