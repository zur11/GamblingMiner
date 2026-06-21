using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
#nullable enable

namespace GodotBlockchainPort.Blockchain;

public sealed class BlockchainService
{
    public const string CoinbaseSender = "00";
    // DIFFICULTY CALIBRATION — these two constants are the only values to change when adjusting block time.
    // Current target: ~585 attempts/block → ~16h 40m in-game at 100X (1 bet = 100 game-sec, target = 58,500 game-sec/block).
    // To increase difficulty: lengthen DifficultyPrefix (e.g. "000") or lower DifficultyNextHexMaxInclusive (e.g. '3').
    // Verify with GetExpectedAttemptsForCurrentDifficulty(). Recalibrate whenever nonces-per-bet or participant count changes significantly.
    public const string DifficultyPrefix = "00";
    public const char DifficultyNextHexMaxInclusive = '6';
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
    public Block CommitBlock(long nonce, string previousBlockHash, string hash, long timestamp, BlockTemplate template)
    {
        Block newBlock = new()
        {
            Index = Chain.Count + 1,
            Timestamp = timestamp,
            Transactions = template.BlockTransactions,
            Nonce = nonce,
            Hash = hash,
            PreviousBlockHash = previousBlockHash,
            MerkleRoot = template.MerkleRoot
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

    public long ProofOfWork(string previousBlockHash, string merkleRoot, long timestamp)
    {
        long nonce = 0;
        string hash = HashHeader(previousBlockHash, merkleRoot, timestamp, nonce);
        while (!IsHashAtTargetDifficulty(hash))
        {
            nonce++;
            hash = HashHeader(previousBlockHash, merkleRoot, timestamp, nonce);
        }

        return nonce;
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

            if (!IsHashAtTargetDifficulty(blockHash))
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

        if (!IsHashAtTargetDifficulty(newBlock.Hash))
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

    public static bool IsHashAtTargetDifficulty(string hash)
    {
        if (!hash.StartsWith(DifficultyPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (hash.Length <= DifficultyPrefix.Length)
        {
            return false;
        }

        char nextHex = hash[DifficultyPrefix.Length];
        return nextHex is >= '0' and <= DifficultyNextHexMaxInclusive;
    }

    public static double GetExpectedAttemptsForCurrentDifficulty()
    {
        double prefixProbability = Math.Pow(16d, -DifficultyPrefix.Length);
        int acceptedNextHexValues = (DifficultyNextHexMaxInclusive - '0') + 1;
        double nextNibbleProbability = acceptedNextHexValues / 16d;
        double successProbability = prefixProbability * nextNibbleProbability;
        return 1d / successProbability;
    }
}
