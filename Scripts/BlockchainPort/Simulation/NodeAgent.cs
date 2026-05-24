using GodotBlockchainPort.Blockchain;
using System.Linq;
#nullable enable

namespace GodotBlockchainPort.Simulation;

public sealed class NodeAgent
{
    public string NodeId { get; }
    public string WalletAddress { get; }
    public string WalletPublicKey { get; }
    public string WalletPrivateKey { get; }
    public BlockchainService Blockchain { get; } = new();
    private long _candidateNonce;
    private string _candidateKey = string.Empty;

    public NodeAgent(string nodeId)
    {
        NodeId = nodeId;
        (WalletAddress, WalletPublicKey, WalletPrivateKey) = CryptoUtils.GenerateWallet();
    }

    public Transaction CreateSignedTransaction(decimal amount, string recipientAddress)
    {
        Transaction tx = Blockchain.CreateUnsignedTransaction(amount, WalletAddress, recipientAddress);
        string payload = BlockchainService.BuildTransactionPayload(tx);
        tx.SignatureBase64 = CryptoUtils.Sign(payload, WalletPrivateKey);
        tx.PublicKeyBase64 = WalletPublicKey;
        return tx;
    }

    public Transaction CreateCoinbaseReward(decimal amount)
    {
        return new Transaction
        {
            Amount = amount,
            Sender = BlockchainService.CoinbaseSender,
            Recipient = WalletAddress,
            TransactionId = System.Guid.NewGuid().ToString("N")
        };
    }

    public Block MinePendingTransactions(decimal rewardAmount = 12.5m)
    {
        // Mine current pending transactions first.
        Block lastBlock = Blockchain.GetLastBlock();
        var currentBlockData = new
        {
            transactions = Blockchain.PendingTransactions,
            index = lastBlock.Index + 1
        };

        long nonce = Blockchain.ProofOfWork(lastBlock.Hash, currentBlockData);
        string hash = Blockchain.HashBlock(lastBlock.Hash, currentBlockData, nonce);
        Block minedBlock = Blockchain.CreateNewBlock(nonce, lastBlock.Hash, hash);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;

        // Reward becomes pending for the next block, matching your expected flow.
        Blockchain.AddTransactionToPendingTransactions(CreateCoinbaseReward(rewardAmount));
        return minedBlock;
    }

    public Block? TryMineSingleNonceAttempt(decimal rewardAmount = 12.5m)
    {
        Block lastBlock = Blockchain.GetLastBlock();
        int nextIndex = lastBlock.Index + 1;
        string pendingFingerprint = string.Join("|", Blockchain.PendingTransactions.Select(t => t.TransactionId));
        string candidateKey = $"{lastBlock.Hash}:{nextIndex}:{pendingFingerprint}";
        if (!string.Equals(candidateKey, _candidateKey, System.StringComparison.Ordinal))
        {
            _candidateKey = candidateKey;
            _candidateNonce = 0;
        }

        var currentBlockData = new
        {
            transactions = Blockchain.PendingTransactions,
            index = nextIndex
        };

        string hash = Blockchain.HashBlock(lastBlock.Hash, currentBlockData, _candidateNonce);
        if (!hash.StartsWith(BlockchainService.DifficultyPrefix, System.StringComparison.Ordinal) ||
            !BlockchainService.IsHashAtTargetDifficulty(hash))
        {
            _candidateNonce++;
            return null;
        }

        Block minedBlock = Blockchain.CreateNewBlock(_candidateNonce, lastBlock.Hash, hash);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;
        Blockchain.AddTransactionToPendingTransactions(CreateCoinbaseReward(rewardAmount));

        _candidateNonce = 0;
        _candidateKey = string.Empty;
        return minedBlock;
    }

    public long GetCurrentCandidateNonce()
    {
        return _candidateNonce;
    }
}
