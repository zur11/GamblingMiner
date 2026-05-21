using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
#nullable enable

namespace GodotBlockchainPort.Blockchain;

public sealed class BlockchainService
{
    public const string CoinbaseSender = "00";
    public const string DifficultyPrefix = "0000";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public List<Block> Chain { get; } = new();
    public List<Transaction> PendingTransactions { get; } = new();

    public BlockchainService()
    {
        CreateNewBlock(100, "0", "0");
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

        return CryptoUtils.Verify(BuildTransactionPayload(tx), tx.SignatureBase64, tx.PublicKeyBase64);
    }

    public bool AddTransactionToPendingTransactions(Transaction transaction)
    {
        if (!ValidateTransactionSignature(transaction))
        {
            return false;
        }

        PendingTransactions.Add(transaction);
        return true;
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
        while (!hash.StartsWith(DifficultyPrefix, StringComparison.Ordinal))
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

            if (!blockHash.StartsWith(DifficultyPrefix, StringComparison.Ordinal))
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

        if (!newBlock.Hash.StartsWith(DifficultyPrefix, StringComparison.Ordinal))
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
}
