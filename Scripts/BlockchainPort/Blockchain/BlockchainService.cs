using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
#nullable enable

namespace GodotBlockchainPort.Blockchain;

public sealed class BlockchainService
{
    public const string CoinbaseSender = "00";
    public const string DifficultyPrefix = "00";
    public const char DifficultyNextHexMaxInclusive = '6';
    public static readonly long GenesisTimestampUnixMs =
        new DateTimeOffset(2009, 10, 3, 4, 59, 59, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public List<Block> Chain { get; } = new();
    public List<Transaction> PendingTransactions { get; } = new();

    public BlockchainService()
    {
        Block genesis = CreateNewBlock(100, "0", "0");
        genesis.Timestamp = GenesisTimestampUnixMs;
    }

    public Block CreateNewBlock(long nonce, string previousBlockHash, string hash)
    {
        Block newBlock = new()
        {
            Index = Chain.Count + 1,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Transactions = PendingTransactions.ToList(),
            Nonce = nonce,
            Hash = hash,
            PreviousBlockHash = previousBlockHash
        };

        PendingTransactions.Clear();
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
            TransactionId = Guid.NewGuid().ToString("N")
        };
    }

    public static string BuildTransactionPayload(Transaction tx)
    {
        return $"{tx.Amount}|{tx.Sender}|{tx.Recipient}|{tx.TransactionId}";
    }

    public bool ValidateTransactionSignature(Transaction tx)
    {
        if (tx.Sender == CoinbaseSender)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(tx.PublicKeyBase64) || string.IsNullOrWhiteSpace(tx.SignatureBase64))
        {
            return false;
        }

        if (!string.Equals(tx.Sender, CryptoUtils.DeriveAddressFromPublicKey(tx.PublicKeyBase64), StringComparison.Ordinal))
        {
            return false;
        }

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

        if (!ValidateTransactionSignature(transaction))
        {
            return false;
        }

        if (transaction.Sender != CoinbaseSender)
        {
            decimal spendableBalance = GetAddressSpendableBalance(transaction.Sender);
            if (spendableBalance < transaction.Amount)
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

    public string HashBlock(string previousBlockHash, object currentBlockData, long nonce)
    {
        string dataAsString = previousBlockHash + nonce + JsonSerializer.Serialize(currentBlockData, JsonOptions);
        return CryptoUtils.Sha256Hex(dataAsString);
    }

    public long ProofOfWork(string previousBlockHash, object currentBlockData)
    {
        long nonce = 0;
        string hash = HashBlock(previousBlockHash, currentBlockData, nonce);
        while (!IsHashAtTargetDifficulty(hash))
        {
            nonce++;
            hash = HashBlock(previousBlockHash, currentBlockData, nonce);
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

            string blockHash = HashBlock(
                prevBlock.Hash,
                new { transactions = currentBlock.Transactions, index = currentBlock.Index },
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
            && genesis.Transactions.Count == 0;

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

    public AddressData GetAddressData(string address)
    {
        List<Transaction> addressTransactions = new();
        foreach (Block block in Chain)
        {
            addressTransactions.AddRange(
                block.Transactions.Where(t => t.Sender == address || t.Recipient == address)
            );
        }

        decimal balance = 0m;
        foreach (Transaction tx in addressTransactions)
        {
            if (tx.Recipient == address) balance += tx.Amount;
            else if (tx.Sender == address) balance -= tx.Amount;
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
                // Pending outgoing transactions reserve funds immediately.
                // Pending incoming transactions are not spendable until mined.
                balance -= pending.Amount;
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
