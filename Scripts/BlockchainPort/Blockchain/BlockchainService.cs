using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
#nullable enable

namespace GodotBlockchainPort.Blockchain;

public sealed class BlockchainService
{
    public const string CoinbaseSender = "00";

    // ── Difficulty (Difficulty Regulator, D.1) ────────────────────────────────────────────────────
    // Difficulty is a CONTINUOUS value = expected nonce attempts per block (probability 1/Difficulty that
    // a block-header hash meets the target). A hash meets target when, read as a 256-bit integer H,
    // H ≤ 2²⁵⁶ / Difficulty. The difficulty in effect for each block is stored on the block (Block.Difficulty)
    // so the chain validates without replaying retargets from genesis (D.1 is representation-only; the LWMA
    // retarget arrives in D.2).
    //
    // InitialDifficulty = the legacy effective difficulty (the old "00" prefix + next-hex ≤ '6' rule had
    // success probability (1/16²)·(7/16) = 7/4096, i.e. 4096/7 ≈ 585.14 expected attempts) — seeding this
    // keeps the block pace unchanged until the regulator runs. Target block pace: 58,500 in-game sec/block.
    public const double InitialDifficulty = 4096d / 7d; // ≈ 585.14

    // ── LWMA retarget (D.2) ───────────────────────────────────────────────────────────────────────
    // The difficulty is nudged every block so the average *in-game* time between blocks stays near
    // TargetBlockSeconds. We use an LWMA (Linear Weighted Moving Average) of the last LwmaWindow block
    // solvetimes — recent blocks weighted more — then nextDifficulty = current × (Target / lwmaSolvetime),
    // clamped per step. Because in-game time is bet-driven and a block needs ≈Difficulty attempts, more
    // total mining (more bots / faster hardware) makes blocks arrive in fewer in-game seconds → solvetime
    // dips below target → difficulty rises (and vice-versa). Block time is the only signal (no hashrate term).
    public const double TargetBlockSeconds = 58_500d; // matches the 100X bootstrap pace (≈16h40m/block)
    public const int LwmaWindow = 20;
    public const double MaxStepUp = 2.0d;             // difficulty can at most double per block…
    public const double MaxStepDown = 0.5d;           // …or halve per block (anti-oscillation clamp)
    public const double MinDifficulty = 1.0d;         // floor (1 expected attempt = every hash passes)
    // Easing (D.2, Option A): fraction of the gap to the target closed each block, so a power change ramps
    // in over a few blocks instead of snapping in one. 1.0 = instant; 0.7 ≈ ~97% closed in 3 blocks (tuned by test).
    public const double DifficultyEaseAlpha = 0.7d;

    // 2²⁵⁶ — the space of a 256-bit (64-hex) double-SHA256 hash. Acceptance threshold = MaxHash256 / Difficulty.
    private static readonly BigInteger MaxHash256 = BigInteger.Pow(2, 256);
    // Historical reference only — Satoshi's real base58 genesis address. NOT used for payouts.
    // It is the initial placeholder recipient on the genesis/bootstrap coinbase; NetworkRoot
    // rewrites that recipient to Satoshi's derived gm1q… address once the founder wallet exists.
    public const string SatoshiAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
    public const string GenesisHeadline = "The Times 03/Jan/2009 Chancellor on brink of second bailout for banks.";
    public const string BootstrapSecondBlockTxId = "bootstrap-satoshi-second-block-50btc";
    public static readonly long GenesisTimestampUnixMs =
        new DateTimeOffset(2009, 1, 3, 18, 15, 5, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public List<Block> Chain { get; } = new();
    public List<Transaction> PendingTransactions { get; } = new();

    // ── UTXO set (Step 8 full model, plan Appendix A) ──────────────────────────────────────────────
    // The set of unspent transaction outputs, rebuilt by replaying the chain (because "a block is the only
    // commit to disk"). Cached and invalidated whenever the chain mutates (_chainVersion). Key = "txid:vout".
    private sealed class UtxoEntry
    {
        public required string TxId;
        public required int Vout;
        public required TxOutput Output;
        public required int BlockIndex;
        public required bool IsCoinbase;
        public required bool IsSpendable;
    }
    private Dictionary<string, UtxoEntry>? _utxoCache;
    private int _utxoCacheVersion = -1;
    private int _chainVersion;
    private static string OutPointKey(string txId, int vout) => $"{txId}:{vout}";

    public BlockchainService()
    {
        Block genesis = CreateNewBlock(100, "0", "0", GenesisTimestampUnixMs, "0");
        genesis.Transactions = new List<Transaction> { CreateGenesisCoinbase() };
        genesis.MerkleRoot = MerkleTree.ComputeRoot(genesis.Transactions);
        genesis.Difficulty = InitialDifficulty; // genesis is exempt from PoW validation; set for consistency
    }

    public static string TextToHex(string text)
    {
        return Convert.ToHexString(Encoding.ASCII.GetBytes(text)).ToLowerInvariant();
    }

    public static Transaction CreateGenesisCoinbase()
    {
        return new Transaction
        {
            // Input-less coinbase: one 50-BTC output to Satoshi (rewritten to his derived gm1q… address by
            // NetworkRoot.NormalizeGenesisAcrossNodes). IsSpendable = false → unspendable forever.
            Inputs = new List<TxInput>(),
            Outputs = new List<TxOutput> { new() { Address = SatoshiAddress, Amount = 50m } },
            TransactionId = "genesis-coinbase",
            InputDataText = GenesisHeadline,
            InputDataHex = TextToHex(GenesisHeadline),
            IsSpendable = false
        };
    }

    // Builds an input-less coinbase output (reward + Σfees) to the miner. BIP34-style height in the Salt
    // keeps each coinbase txid unique even at equal reward (Step 4b.3).
    public static Transaction CreateCoinbase(string minerAddress, decimal amount, int blockIndex)
    {
        var coinbase = new Transaction
        {
            Inputs = new List<TxInput>(),
            Outputs = new List<TxOutput> { new() { Address = minerAddress, Amount = amount } },
            Salt = $"coinbase:{blockIndex}",
            IsSpendable = true
        };
        coinbase.TransactionId = ComputeTransactionId(coinbase);
        return coinbase;
    }

    public Block CreateNewBlock(long nonce, string previousBlockHash, string hash, long timestamp, string merkleRoot)
    {
        Block newBlock = new()
        {
            Index = Chain.Count + 1,
            Timestamp = timestamp,
            Transactions = PendingTransactions.ToList(),
            Nonce = nonce,
            Hash = hash,
            PreviousBlockHash = previousBlockHash,
            MerkleRoot = merkleRoot
        };

        PendingTransactions.Clear();
        Chain.Add(newBlock);
        _chainVersion++;
        return newBlock;
    }

    // Step 4b: commit a mined block built from a BlockTemplate. Unlike CreateNewBlock (used only for
    // genesis), the coinbase is already inside template.BlockTransactions, and only the transactions
    // actually included are removed from the mempool (unselected ones remain pending).
    public Block CommitBlock(long nonce, string previousBlockHash, string hash, long timestamp, BlockTemplate template, double difficulty)
    {
        Block newBlock = new()
        {
            Index = Chain.Count + 1,
            Timestamp = timestamp,
            Transactions = template.BlockTransactions,
            Nonce = nonce,
            Hash = hash,
            PreviousBlockHash = previousBlockHash,
            MerkleRoot = template.MerkleRoot,
            Difficulty = difficulty
        };

        foreach (Transaction included in template.SelectedMempoolTxs)
        {
            PendingTransactions.Remove(included);
        }

        Chain.Add(newBlock);
        _chainVersion++;
        return newBlock;
    }

    public Block GetLastBlock() => Chain[^1];

    // Builds an unsigned transaction from explicit inputs + outputs (Step 8 full UTXO model). The caller
    // (NodeAgent) fills in each input's signature/keys and sets the txid. A random Salt keeps the content
    // hash unique; pass a deterministic salt for scripted historical events.
    public static Transaction CreateUnsignedTransaction(List<TxInput> inputs, List<TxOutput> outputs, decimal fee, string? salt = null)
    {
        return new Transaction
        {
            Inputs = inputs,
            Outputs = outputs,
            Fee = fee,
            Salt = salt ?? Guid.NewGuid().ToString("N"),
            TransactionId = string.Empty
        };
    }

    // Step 4b.3 (OQ-C6) / Step 8 (A.6): a transaction's id is the double-SHA256 of its canonical content —
    // the input outpoints, the output (address, amount) pairs, the fee, input data, spendability and the
    // uniqueness Salt. Excludes the signatures, so it is a true fingerprint of *what* the tx does. Also the
    // Merkle leaf, so any reshuffle of inputs/outputs changes the root and invalidates the block.
    public static string ComputeTransactionId(Transaction tx)
    {
        string inputs = string.Join(",", tx.Inputs.Select(i => $"{i.Source.PrevTxId}:{i.Source.Vout}"));
        string outputs = string.Join(",", tx.Outputs.Select(o => $"{o.Address}:{o.Amount.ToString(CultureInfo.InvariantCulture)}"));
        string content = string.Join("|", new[]
        {
            "in:" + inputs,
            "out:" + outputs,
            tx.Fee.ToString(CultureInfo.InvariantCulture),
            tx.InputDataHex,
            tx.IsSpendable ? "1" : "0",
            tx.Salt
        });
        return CryptoUtils.Sha256Hex(CryptoUtils.Sha256Hex(content));
    }

    // The per-input signed message (sighash). The txid already commits to every input/output/fee/salt
    // (ComputeTransactionId), so signing it makes the whole tx tamper-evident and binds each input to this
    // exact set of inputs/outputs — they cannot be reshuffled without breaking every signature.
    public static string BuildTransactionPayload(Transaction tx) => tx.TransactionId;

    // Step 8 (A.4) — per-input validation: txid integrity + for EVERY input an ownership proof (the input's
    // secp256k1 key derives the address recorded on it) and a P-256 signature over the tx sighash. A coinbase
    // (no inputs) is signature-valid by definition. Enables inputs across several owned addresses.
    public bool ValidateTransactionSignature(Transaction tx)
    {
        if (tx.IsCoinbase)
        {
            return true;
        }

        // Integrity: the txid must be the content hash of the transaction (Step 4b.3 / A.6).
        if (!string.Equals(tx.TransactionId, ComputeTransactionId(tx), StringComparison.Ordinal))
        {
            return false;
        }

        string payload = BuildTransactionPayload(tx);
        foreach (TxInput input in tx.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.PublicKeyBase64) ||
                string.IsNullOrWhiteSpace(input.SignatureBase64) ||
                string.IsNullOrWhiteSpace(input.Secp256k1PublicKeyBase64))
            {
                return false;
            }

            // Ownership: secp256k1 public key → Hash160 → Bech32 must match the input's recorded address.
            if (!string.Equals(input.Address, CryptoUtils.DeriveAddressFromPublicKey(input.Secp256k1PublicKeyBase64), StringComparison.Ordinal))
            {
                return false;
            }

            // Signature: P-256 signing key (game-internal, not visible on the address) over the sighash.
            if (!CryptoUtils.Verify(payload, input.SignatureBase64, input.PublicKeyBase64))
            {
                return false;
            }
        }

        return true;
    }

    // Step 8 (A.4) — admit a spend to the mempool under UTXO rules: every input references a confirmed,
    // unspent, mature (if coinbase), non-double-spent output it owns; Σinputs ≥ Σoutputs; and the declared
    // Fee equals Σinputs − Σoutputs. Coinbases never enter here (they are created inside the block template).
    public bool AddTransactionToPendingTransactions(Transaction transaction)
    {
        if (transaction.IsCoinbase || transaction.Inputs.Count == 0)
        {
            return false;
        }
        if (transaction.Outputs.Count == 0 || transaction.Outputs.Any(o => o.Amount <= 0m))
        {
            return false;
        }
        if (ContainsTransactionId(transaction.TransactionId))
        {
            return false;
        }
        if (!ValidateTransactionSignature(transaction))
        {
            return false;
        }

        Dictionary<string, UtxoEntry> utxos = GetUtxoSet();
        HashSet<string> spentByPending = CollectPendingSpentOutpoints();
        int tipIndex = Chain.Count > 0 ? Chain[^1].Index : 0;

        decimal inputSum = 0m;
        var seenInThisTx = new HashSet<string>();
        foreach (TxInput input in transaction.Inputs)
        {
            string key = OutPointKey(input.Source.PrevTxId, input.Source.Vout);
            if (!seenInThisTx.Add(key)) return false;                       // duplicate input inside the tx
            if (!utxos.TryGetValue(key, out UtxoEntry? utxo)) return false; // unknown or already-spent output
            if (!utxo.IsSpendable) return false;                           // genesis (unspendable)
            if (utxo.IsCoinbase && (tipIndex - utxo.BlockIndex) < CoinbaseMaturity) return false; // immature
            if (spentByPending.Contains(key)) return false;                // double-spend vs the mempool
            if (!string.Equals(utxo.Output.Address, input.Address, StringComparison.Ordinal)) return false;
            inputSum += utxo.Output.Amount;
        }

        decimal outputSum = transaction.TotalOutput;
        if (inputSum < outputSum) return false;
        if (transaction.Fee != inputSum - outputSum) return false; // fee must equal the in/out delta exactly

        PendingTransactions.Add(transaction);
        return true;
    }

    // Every outpoint consumed by a pending mempool transaction (for the double-spend guard).
    private HashSet<string> CollectPendingSpentOutpoints()
    {
        var spent = new HashSet<string>();
        foreach (Transaction pending in PendingTransactions)
            foreach (TxInput input in pending.Inputs)
                spent.Add(OutPointKey(input.Source.PrevTxId, input.Source.Vout));
        return spent;
    }

    // The confirmed UTXO set, rebuilt by replaying the chain oldest→newest and cached until the chain
    // mutates (Step 8 / A.3). Key = "txid:vout". Coinbase maturity and spendability are read from each entry.
    private Dictionary<string, UtxoEntry> GetUtxoSet()
    {
        if (_utxoCache != null && _utxoCacheVersion == _chainVersion)
        {
            return _utxoCache;
        }

        var utxos = new Dictionary<string, UtxoEntry>();
        foreach (Block block in Chain)
        {
            foreach (Transaction tx in block.Transactions)
            {
                foreach (TxInput input in tx.Inputs)
                    utxos.Remove(OutPointKey(input.Source.PrevTxId, input.Source.Vout));

                for (int v = 0; v < tx.Outputs.Count; v++)
                {
                    string key = OutPointKey(tx.TransactionId, v);
                    utxos[key] = new UtxoEntry
                    {
                        TxId = tx.TransactionId,
                        Vout = v,
                        Output = tx.Outputs[v],
                        BlockIndex = block.Index,
                        IsCoinbase = tx.IsCoinbase,
                        IsSpendable = tx.IsSpendable
                    };
                }
            }
        }

        _utxoCache = utxos;
        _utxoCacheVersion = _chainVersion;
        return utxos;
    }

    public bool ContainsTransactionId(string transactionId)
    {
        if (PendingTransactions.Any(t => t.TransactionId == transactionId))
        {
            return true;
        }

        return Chain.Any(b => b.Transactions.Any(t => t.TransactionId == transactionId));
    }

    // Step 4: hash a compact block header (prevHash + merkleRoot + timestamp + nonce) via
    // double-SHA256, like Bitcoin — instead of re-serialising the whole transaction list per nonce.
    // The Merkle root commits to every transaction, so this still binds the block contents.
    public string HashHeader(string previousBlockHash, string merkleRoot, long timestamp, long nonce)
    {
        string header = $"{previousBlockHash}|{merkleRoot}|{timestamp}|{nonce}";
        return CryptoUtils.Sha256Hex(CryptoUtils.Sha256Hex(header));
    }

    public long ProofOfWork(string previousBlockHash, string merkleRoot, long timestamp, double difficulty)
    {
        long nonce = 0;
        string hash = HashHeader(previousBlockHash, merkleRoot, timestamp, nonce);
        while (!IsHashAtTargetDifficulty(hash, difficulty))
        {
            nonce++;
            hash = HashHeader(previousBlockHash, merkleRoot, timestamp, nonce);
        }

        return nonce;
    }

    // Difficulty to mine the NEXT block (D.2: HYBRID regulator). Computed from the EXISTING chain only (the
    // block being mined isn't timestamped yet), so it's stable across a candidate's nonce rolls. O(LwmaWindow).
    //
    //   target = anchor × feedbackTrim ;  nextDifficulty = current + DifficultyEaseAlpha × (target − current)
    //
    // • anchor — feed-forward from the KNOWN total mining power. In-game time runs at clock-speed × real time
    //   and a block needs ≈Difficulty attempts, so equilibrium difficulty = (TargetBlockSeconds / clockSpeed) ×
    //   power = InitialDifficulty × power (baseline: 1 bet/sec ↔ InitialDifficulty). This is the *correct level*
    //   for the current power; when a miner joins/leaves or hardware changes the target updates at once. When
    //   power is unknown (0: historical bootstrap / idle), hold at the current difficulty (feedback-only).
    // • feedbackTrim = LWMA(TargetBlockSeconds / recent solvetimes) — the "real-process" block-time signal that
    //   trims calibration drift + PoW variance. CLAMPED to [MaxStepDown, MaxStepUp] per block (anti-oscillation).
    // • easing (Option A) — instead of snapping to target, close a fraction DifficultyEaseAlpha of the gap each
    //   block, so the change ramps in over a few blocks rather than instantly.
    public double GetNextBlockDifficulty(double networkPower)
    {
        if (Chain.Count == 0)
        {
            return InitialDifficulty;
        }

        double current = EffectiveDifficulty(Chain[^1]);

        // Feed-forward anchor (instant, NOT clamped — a known power level should land in one block).
        double anchor = networkPower > 0d ? InitialDifficulty * networkPower : current;

        // Feedback: LWMA over the last up-to-W solvetimes (most recent weighted highest). Clamped.
        double feedbackTrim = 1d;
        if (Chain.Count >= 2)
        {
            int deltas = Math.Min(LwmaWindow, Chain.Count - 1);
            double weightedSum = 0d;
            double weightTotal = 0d;
            for (int k = 0; k < deltas; k++)
            {
                double solveSec = (Chain[Chain.Count - 1 - k].Timestamp - Chain[Chain.Count - 2 - k].Timestamp) / 1000d;
                if (solveSec < 1d)
                {
                    solveSec = 1d;
                }

                double weight = deltas - k; // k=0 is the most recent delta → highest weight
                weightedSum += weight * solveSec;
                weightTotal += weight;
            }

            feedbackTrim = TargetBlockSeconds / (weightedSum / weightTotal);
        }
        feedbackTrim = Math.Clamp(feedbackTrim, MaxStepDown, MaxStepUp);

        // Ease toward the target rather than snapping, so a power change ramps in over a few blocks.
        double target = anchor * feedbackTrim;
        double next = current + DifficultyEaseAlpha * (target - current);
        return Math.Max(next, MinDifficulty);
    }

    // A block with no stored difficulty (pre-D.1 save) is treated as InitialDifficulty — the value it was
    // actually mined against — so old chains still validate and retarget cleanly.
    private static double EffectiveDifficulty(Block block)
    {
        return block.Difficulty > 0d ? block.Difficulty : InitialDifficulty;
    }

    public bool ChainIsValid(IReadOnlyList<Block> blockchain)
    {
        if (blockchain.Count == 0)
        {
            return false;
        }

        bool validChain = true;
        for (int i = 1; i < blockchain.Count; i++)
        {
            Block currentBlock = blockchain[i];
            Block prevBlock = blockchain[i - 1];

            // Merkle root must match the block's transactions (tamper check)…
            if (currentBlock.MerkleRoot != MerkleTree.ComputeRoot(currentBlock.Transactions))
            {
                validChain = false;
            }

            // …and the header must hash to a value meeting the difficulty target.
            string blockHash = HashHeader(
                prevBlock.Hash,
                currentBlock.MerkleRoot,
                currentBlock.Timestamp,
                currentBlock.Nonce
            );

            if (!IsHashAtTargetDifficulty(blockHash, EffectiveDifficulty(currentBlock)))
            {
                validChain = false;
            }

            if (currentBlock.PreviousBlockHash != prevBlock.Hash)
            {
                validChain = false;
            }
        }

        Block genesis = blockchain[0];
        bool correctGenesis = genesis.Nonce == 100
            && genesis.PreviousBlockHash == "0"
            && genesis.Hash == "0"
            && genesis.Transactions.Count >= 1
            && genesis.Timestamp == GenesisTimestampUnixMs;

        return validChain && correctGenesis;
    }

    public bool TryAcceptMinedBlock(Block newBlock)
    {
        Block lastBlock = GetLastBlock();
        bool correctHash = lastBlock.Hash == newBlock.PreviousBlockHash;
        bool correctIndex = lastBlock.Index + 1 == newBlock.Index;
        if (!correctHash || !correctIndex)
        {
            return false;
        }

        if (!IsHashAtTargetDifficulty(newBlock.Hash, EffectiveDifficulty(newBlock)))
        {
            return false;
        }

        Chain.Add(newBlock);
        PendingTransactions.Clear();
        _chainVersion++;
        return true;
    }

    public Block? GetBlock(string blockHash) => Chain.FirstOrDefault(x => x.Hash == blockHash);

    public (Transaction? transaction, Block? block) GetTransaction(string transactionId)
    {
        foreach (Block block in Chain)
        {
            Transaction? tx = block.Transactions.FirstOrDefault(t => t.TransactionId == transactionId);
            if (tx is not null)
            {
                return (tx, block);
            }
        }
        return (null, null);
    }

    public Transaction? GetPendingTransaction(string transactionId)
    {
        return PendingTransactions.FirstOrDefault(t => t.TransactionId == transactionId);
    }

    // Step 4b: a coinbase output needs CoinbaseMaturity confirmations (blocks mined on top) before
    // it is spendable — the fractal equivalent of Bitcoin's 100-confirmation rule (≈16h ≈ 1 block
    // here). Immature coinbase is excluded from the balance until it matures.
    public const int CoinbaseMaturity = 1;

    // Step 8 — an address's confirmed balance = Σ of its mature, spendable UTXOs (unspendable genesis and
    // immature coinbase excluded). AddressTransactions = every confirmed tx that references the address as an
    // input owner or an output recipient (for the explorer / history readers).
    public AddressData GetAddressData(string address)
    {
        int tipIndex = Chain.Count > 0 ? Chain[^1].Index : 0;
        Dictionary<string, UtxoEntry> utxos = GetUtxoSet();

        decimal balance = 0m;
        foreach (UtxoEntry utxo in utxos.Values)
        {
            if (utxo.Output.Address != address || !utxo.IsSpendable) continue;
            if (utxo.IsCoinbase && (tipIndex - utxo.BlockIndex) < CoinbaseMaturity) continue;
            balance += utxo.Output.Amount;
        }

        List<Transaction> addressTransactions = new();
        foreach (Block block in Chain)
            foreach (Transaction tx in block.Transactions)
                if (tx.Inputs.Any(i => i.Address == address) || tx.Outputs.Any(o => o.Address == address))
                    addressTransactions.Add(tx);

        return new AddressData
        {
            AddressTransactions = addressTransactions,
            AddressBalance = balance
        };
    }

    // Confirmed spendable balance MINUS the value of UTXOs already reserved by a pending outgoing tx
    // (pending outputs are not spendable until mined). The scalar form of GetSpendableUtxos.
    public decimal GetAddressSpendableBalance(string address)
    {
        return GetSpendableUtxos(new[] { address }).Sum(u => u.amount);
    }

    // Step 8 (A.3 / coin selection) — the list of confirmed, mature, spendable, NOT-pending-spent outputs
    // owned by any of `addresses`. This is the wallet's selectable "coins"; combining several funds a payment
    // no single one covers (the multi-input case the player hit). Each entry carries the outpoint to spend.
    public IReadOnlyList<(OutPoint outpoint, string address, decimal amount)> GetSpendableUtxos(IEnumerable<string> addresses)
    {
        var owned = new HashSet<string>(addresses);
        int tipIndex = Chain.Count > 0 ? Chain[^1].Index : 0;
        Dictionary<string, UtxoEntry> utxos = GetUtxoSet();
        HashSet<string> spentByPending = CollectPendingSpentOutpoints();

        var result = new List<(OutPoint, string, decimal)>();
        foreach (UtxoEntry utxo in utxos.Values)
        {
            if (!owned.Contains(utxo.Output.Address) || !utxo.IsSpendable) continue;
            if (utxo.IsCoinbase && (tipIndex - utxo.BlockIndex) < CoinbaseMaturity) continue;
            if (spentByPending.Contains(OutPointKey(utxo.TxId, utxo.Vout))) continue;
            result.Add((new OutPoint { PrevTxId = utxo.TxId, Vout = utxo.Vout }, utxo.Output.Address, utxo.Output.Amount));
        }
        return result;
    }

    public bool TryReplaceChain(List<Block> newChain, List<Transaction> newPendingTransactions)
    {
        if (newChain.Count <= Chain.Count || !ChainIsValid(newChain))
        {
            return false;
        }

        Chain.Clear();
        Chain.AddRange(newChain);
        PendingTransactions.Clear();
        PendingTransactions.AddRange(newPendingTransactions);
        _chainVersion++;
        return true;
    }

    // A 64-hex double-SHA256 hash meets the target when, read as a 256-bit integer, it is ≤ 2²⁵⁶ / Difficulty
    // (so the chance a random hash passes is 1/Difficulty). Continuous → any positive Difficulty is valid.
    public static bool IsHashAtTargetDifficulty(string hash, double difficulty)
    {
        if (difficulty <= 0d || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        BigInteger target = (BigInteger)(MaxHash256dbl / difficulty);
        return HexToBigInteger(hash) <= target;
    }

    // 2²⁵⁶ as a double (≈1.16e77). Used only to derive the acceptance threshold; double's ~15–16 significant
    // digits set the probability precisely enough (the discarded low bits are far below any meaningful target).
    private static readonly double MaxHash256dbl = Math.Pow(2d, 256d);

    private static BigInteger HexToBigInteger(string hash)
    {
        // Prefix "0" so the leading hex nibble is never read as a sign bit → always a non-negative value.
        return BigInteger.Parse("0" + hash, NumberStyles.HexNumber);
    }

    // Expected nonce attempts per block at the chain's CURRENT difficulty (the tip's stored Difficulty).
    public double GetExpectedAttemptsForCurrentDifficulty()
    {
        return Chain.Count > 0 ? EffectiveDifficulty(Chain[^1]) : InitialDifficulty;
    }
}
