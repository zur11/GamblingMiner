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
            Amount = 50m,
            Sender = CoinbaseSender,
            Recipient = SatoshiAddress,
            TransactionId = "genesis-coinbase",
            InputDataText = GenesisHeadline,
            InputDataHex = TextToHex(GenesisHeadline),
            IsSpendable = false
        };
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
        return newBlock;
    }

    public Block GetLastBlock() => Chain[^1];

    public Transaction CreateUnsignedTransaction(decimal amount, string sender, string recipient)
    {
        return new Transaction
        {
            Amount = amount,
            Sender = sender,
            Recipient = recipient,
            Salt = Guid.NewGuid().ToString("N"), // uniqueness nonce; the content-hash txid is set by the signer
            TransactionId = string.Empty
        };
    }

    // Step 4b.3 (OQ-C6): a transaction's id is the double-SHA256 of its canonical content — amount,
    // parties, fee, input data, spendability and the uniqueness Salt. Excludes the id itself and the
    // signature, so it is a true fingerprint of *what* the transaction does. Also used as the Merkle leaf.
    public static string ComputeTransactionId(Transaction tx)
    {
        string content = string.Join("|", new[]
        {
            tx.Amount.ToString(CultureInfo.InvariantCulture),
            tx.Sender,
            tx.Recipient,
            tx.Fee.ToString(CultureInfo.InvariantCulture),
            tx.InputDataHex,
            tx.IsSpendable ? "1" : "0",
            tx.Salt
        });
        return CryptoUtils.Sha256Hex(CryptoUtils.Sha256Hex(content));
    }

    public static string BuildTransactionPayload(Transaction tx)
    {
        // The txid already commits to amount/parties/fee/data/salt (ComputeTransactionId); signing a
        // payload that includes it makes the whole transaction tamper-evident under the signature.
        return $"{tx.Amount}|{tx.Sender}|{tx.Recipient}|{tx.TransactionId}|{tx.Fee}";
    }

    public bool ValidateTransactionSignature(Transaction tx)
    {
        if (tx.Sender == CoinbaseSender)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(tx.PublicKeyBase64) ||
            string.IsNullOrWhiteSpace(tx.SignatureBase64) ||
            string.IsNullOrWhiteSpace(tx.Secp256k1PublicKeyBase64))
        {
            return false;
        }

        // Integrity: the txid must be the content hash of the transaction (Step 4b.3).
        if (!string.Equals(tx.TransactionId, ComputeTransactionId(tx), StringComparison.Ordinal))
        {
            return false;
        }

        // Address ownership check: secp256k1 public key → Hash160 → Bech32 must match Sender
        if (!string.Equals(tx.Sender, CryptoUtils.DeriveAddressFromPublicKey(tx.Secp256k1PublicKeyBase64), StringComparison.Ordinal))
        {
            return false;
        }

        // Signature check: P-256 signing key (game-internal, not visible on the address)
        return CryptoUtils.Verify(BuildTransactionPayload(tx), tx.SignatureBase64, tx.PublicKeyBase64);
    }

    public bool AddTransactionToPendingTransactions(Transaction transaction)
    {
        if (transaction.Amount <= 0m)
        {
            return false;
        }

        if (ContainsTransactionId(transaction.TransactionId))
        {
            return false;
        }

        // A transaction may never pay its own sender (coinbase sender "00" is never a real address).
        if (string.Equals(transaction.Sender, transaction.Recipient, StringComparison.Ordinal))
        {
            return false;
        }

        if (!ValidateTransactionSignature(transaction))
        {
            return false;
        }

        if (transaction.Sender != CoinbaseSender)
        {
            // Sender must be able to cover amount + fee (the fee is paid to the miner via the coinbase).
            decimal spendableBalance = GetAddressSpendableBalance(transaction.Sender);
            if (spendableBalance < transaction.Amount + transaction.Fee)
            {
                return false;
            }
        }

        PendingTransactions.Add(transaction);
        return true;
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
    //   nextDifficulty = anchor × feedbackTrim
    //
    // • anchor — feed-forward from the KNOWN total mining power. In-game time runs at clock-speed × real time
    //   and a block needs ≈Difficulty attempts, so equilibrium difficulty = (TargetBlockSeconds / clockSpeed) ×
    //   power = InitialDifficulty × power (baseline: 1 bet/sec ↔ InitialDifficulty). This lands the *correct
    //   level* instantly — when a miner joins/leaves or hardware changes, the next block already reflects it,
    //   with no waiting for feedback. When power is unknown (0: historical bootstrap / idle), hold at the
    //   current difficulty (feedback-only).
    // • feedbackTrim = LWMA(TargetBlockSeconds / recent solvetimes) — the "real-process" block-time signal that
    //   trims calibration drift + PoW variance. CLAMPED to [MaxStepDown, MaxStepUp] per block (anti-oscillation).
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

        return Math.Max(anchor * feedbackTrim, MinDifficulty);
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

    public AddressData GetAddressData(string address)
    {
        int tipIndex = Chain.Count > 0 ? Chain[^1].Index : 0;

        List<Transaction> addressTransactions = new();
        decimal balance = 0m;

        foreach (Block block in Chain)
        {
            foreach (Transaction tx in block.Transactions)
            {
                if (tx.Sender != address && tx.Recipient != address)
                {
                    continue;
                }

                addressTransactions.Add(tx);

                if (!tx.IsSpendable)
                {
                    continue;
                }

                // Immature coinbase (not enough confirmations) does not count toward the balance yet.
                bool isCoinbase = tx.Sender == CoinbaseSender;
                if (isCoinbase && (tipIndex - block.Index) < CoinbaseMaturity)
                {
                    continue;
                }

                if (tx.Recipient == address) balance += tx.Amount;
                else if (tx.Sender == address) balance -= tx.Amount + tx.Fee; // sender pays amount + fee
            }
        }

        return new AddressData
        {
            AddressTransactions = addressTransactions,
            AddressBalance = balance
        };
    }

    public decimal GetAddressSpendableBalance(string address)
    {
        decimal balance = GetAddressData(address).AddressBalance;
        foreach (Transaction pending in PendingTransactions)
        {
            if (pending.Sender == address)
            {
                // Pending outgoing transactions reserve funds immediately (amount + fee).
                // Pending incoming transactions are not spendable until mined.
                balance -= pending.Amount + pending.Fee;
            }
        }

        return balance;
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
