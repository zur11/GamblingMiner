using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace GodotBlockchainPort.Blockchain;

// Step 8 (full UTXO model, plan Appendix A) — references a specific prior transaction output (the "coin"
// being spent): the txid that created it + that output's position (vout) in the creating tx.
public sealed class OutPoint
{
    public string PrevTxId { get; set; } = string.Empty;
    public int Vout { get; set; }
}

// One spent input: the prior output it consumes (Source) + the keys proving ownership of that output's
// address. Address is denormalized (= the consumed output's address) so balance/display read it without a
// UTXO-set lookup. The signature commits to the whole tx (sighash) so inputs/outputs can't be reshuffled.
public sealed class TxInput
{
    public OutPoint Source { get; set; } = new();
    public string Address { get; set; } = string.Empty;             // the consumed output's address (owner)
    public string SignatureBase64 { get; set; } = string.Empty;     // P-256 signature over the tx sighash
    public string PublicKeyBase64 { get; set; } = string.Empty;     // P-256 signing key (game-internal)
    public string Secp256k1PublicKeyBase64 { get; set; } = string.Empty; // address-ownership key
}

// One created output: who can spend it (Address) and how much. Its position in Transaction.Outputs is its vout.
public sealed class TxOutput
{
    public string Address { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class Transaction
{
    // Source of truth (Step 8 full UTXO model). A coinbase has NO inputs (Inputs.Count == 0). A normal
    // payment has one output to the payee + an optional change output back to a fresh owned address.
    public List<TxInput> Inputs { get; set; } = new();
    public List<TxOutput> Outputs { get; set; } = new();

    public string TransactionId { get; set; } = Guid.NewGuid().ToString("N");
    public string InputDataHex { get; set; } = string.Empty;
    public string InputDataText { get; set; } = string.Empty;
    public bool IsSpendable { get; set; } = true;
    // Step 4: miner fee = Σinputs − Σoutputs, collected into the block coinbase. Kept as a field for display
    // and for fee-ordered block building; validated against the input/output sums.
    public decimal Fee { get; set; } = 0m;
    // Reserved for feerate ordering once transactions have a real size model (Step 4 OQ-C2).
    public int SizeVBytes { get; set; } = 1;
    // Step 4b.3: uniqueness nonce folded into the content-hash txid. Without it two identical coinbases
    // (or identical payments) would hash to the same id. Random for normal txs; block height for coinbases.
    public string Salt { get; set; } = string.Empty;

    // ── Migration shims (plan Appendix A) ─────────────────────────────────────────────────────────────
    // Read-only views onto the input/output lists so legacy read-only call sites (Block Explorer, stats,
    // scripted-activity scans) keep working without a full port. Signing/validation/balance use the lists
    // directly. JsonIgnore'd so they are not written to the save file (Inputs/Outputs are authoritative).
    [JsonIgnore]
    public string Sender => Inputs.Count > 0 ? Inputs[0].Address : BlockchainService.CoinbaseSender;
    [JsonIgnore]
    public string Recipient => Outputs.Count > 0 ? Outputs[0].Address : string.Empty;
    [JsonIgnore]
    public decimal Amount => Outputs.Count > 0 ? Outputs[0].Amount : 0m;
    // The first input's keys (legacy single-signature reads). Empty for a coinbase.
    [JsonIgnore]
    public string SignatureBase64 => Inputs.Count > 0 ? Inputs[0].SignatureBase64 : string.Empty;
    [JsonIgnore]
    public string PublicKeyBase64 => Inputs.Count > 0 ? Inputs[0].PublicKeyBase64 : string.Empty;
    [JsonIgnore]
    public string Secp256k1PublicKeyBase64 => Inputs.Count > 0 ? Inputs[0].Secp256k1PublicKeyBase64 : string.Empty;

    [JsonIgnore]
    public bool IsCoinbase => Inputs.Count == 0;
    [JsonIgnore]
    public decimal TotalOutput => Outputs.Sum(o => o.Amount);
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
