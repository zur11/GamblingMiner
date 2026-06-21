using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GodotBlockchainPort.Blockchain;

// Step 4 — Bitcoin-style Merkle tree over a block's transactions.
// Leaves are a double-SHA256 content hash of each transaction; the tree pairs hashes and
// duplicates the last one on an odd count, until a single root remains. Because the root is part
// of the hashed block header (BlockchainService.HashHeader), any change to a transaction's
// content changes the root and therefore the block hash — tamper-evidence.
//
// Note (OQ-C6): the leaf hash is the realistic "content txid". Replacing the GUID
// Transaction.TransactionId field with this hash is a focused follow-up; for now the leaf is
// computed independently and the field stays a GUID.
public static class MerkleTree
{
    // Canonical, signature-independent content of a transaction (the "what", not the "who signed").
    public static string LeafHash(Transaction tx)
    {
        string content = string.Join("|", new[]
        {
            tx.Amount.ToString(CultureInfo.InvariantCulture),
            tx.Sender,
            tx.Recipient,
            tx.Fee.ToString(CultureInfo.InvariantCulture),
            tx.TransactionId,
            tx.InputDataHex,
            tx.IsSpendable ? "1" : "0"
        });
        return DoubleSha256(content);
    }

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
