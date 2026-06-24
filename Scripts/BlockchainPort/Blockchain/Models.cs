using System;
using System.Collections.Generic;

namespace GodotBlockchainPort.Blockchain;

public sealed class Transaction
{
    public decimal Amount { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
    public string SignatureBase64 { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
    public string Secp256k1PublicKeyBase64 { get; set; } = string.Empty;
    public string InputDataHex { get; set; } = string.Empty;
    public string InputDataText { get; set; } = string.Empty;
    public bool IsSpendable { get; set; } = true;
    // Step 4: miner fee, paid by the sender on top of Amount and collected into the block coinbase.
    // Default 0 keeps existing/coinbase transactions free. Fee collection is wired in Step 4b.
    public decimal Fee { get; set; } = 0m;
    // Reserved for feerate ordering once transactions have a real size model (Step 4 OQ-C2).
    // Uniform size for now → feerate ordering == fee ordering.
    public int SizeVBytes { get; set; } = 1;
    // Step 4b.3: uniqueness nonce folded into the content-hash txid — the account-model analog of a
    // UTXO input reference (and BIP34 block height for coinbases). Without it, two identical payments
    // (or two equal coinbases) would hash to the same id. Random for normal txs; block height for coinbases.
    public string Salt { get; set; } = string.Empty;
}

public sealed class Block
{
    public int Index { get; set; }
    public long Timestamp { get; set; }
    public List<Transaction> Transactions { get; set; } = new();
    public long Nonce { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string PreviousBlockHash { get; set; } = string.Empty;
    // Step 4: Merkle root over this block's transactions. Part of the hashed header, so any change
    // to a transaction's content invalidates the block hash (tamper-evidence).
    public string MerkleRoot { get; set; } = string.Empty;
    public string MinedByNodeId { get; set; } = string.Empty;
    public string MinedByAddress { get; set; } = string.Empty;
    // Step 6 / Difficulty Regulator (D.1): the difficulty this block was mined against = expected nonce
    // attempts (probability 1/Difficulty of a hash meeting target). Persisted per block so the chain can be
    // validated against the difficulty in effect when each block was mined, with no genesis replay on load.
    public double Difficulty { get; set; }
    // D.2 (hybrid regulator): total active mining power (Σ active miners' bets/sec) at the moment this block
    // was mined. Informational/diagnostic — the difficulty feed-forward anchors off the *current* power
    // (InitialDifficulty × power), not a per-block ratio. 0 = unknown (e.g. the historical bootstrap / idle).
    public double MiningPower { get; set; }
}

public sealed class AddressData
{
    public List<Transaction> AddressTransactions { get; set; } = new();
    public decimal AddressBalance { get; set; }
}
