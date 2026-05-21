using GodotBlockchainPort.Blockchain;

namespace GodotBlockchainPort.Simulation;

public sealed class NodeAgent
{
    public string NodeId { get; }
    public string WalletPublicKey { get; }
    public string WalletPrivateKey { get; }
    public BlockchainService Blockchain { get; } = new();

    public NodeAgent(string nodeId)
    {
        NodeId = nodeId;
        (WalletPublicKey, WalletPrivateKey) = CryptoUtils.GenerateWallet();
    }

    public Transaction CreateSignedTransaction(decimal amount, string recipientPublicKey)
    {
        Transaction tx = Blockchain.CreateUnsignedTransaction(amount, WalletPublicKey, recipientPublicKey);
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
            Recipient = WalletPublicKey,
            TransactionId = System.Guid.NewGuid().ToString("N")
        };
    }

    public Block MinePendingTransactions(decimal rewardAmount = 12.5m)
    {
        Blockchain.AddTransactionToPendingTransactions(CreateCoinbaseReward(rewardAmount));
        Block lastBlock = Blockchain.GetLastBlock();
        var currentBlockData = new
        {
            transactions = Blockchain.PendingTransactions,
            index = lastBlock.Index + 1
        };

        long nonce = Blockchain.ProofOfWork(lastBlock.Hash, currentBlockData);
        string hash = Blockchain.HashBlock(lastBlock.Hash, currentBlockData, nonce);
        return Blockchain.CreateNewBlock(nonce, lastBlock.Hash, hash);
    }
}
