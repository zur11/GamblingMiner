using GodotBlockchainPort.Blockchain;

namespace GodotBlockchainPort.Simulation;

public sealed class NodeAgent
{
    public string NodeId { get; }
    public string WalletAddress { get; }
    public string WalletPublicKey { get; }
    public string WalletPrivateKey { get; }
    public BlockchainService Blockchain { get; } = new();

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

        // Reward becomes pending for the next block, matching your expected flow.
        Blockchain.AddTransactionToPendingTransactions(CreateCoinbaseReward(rewardAmount));
        return minedBlock;
    }
}
