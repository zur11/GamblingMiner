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
    public string WalletSecp256k1PublicKey { get; }
    public BlockchainService Blockchain { get; } = new();
    public NodeFinancialState? FinancialState { get; set; }

    // Relative mining power for the weighted block lottery (Step 2). Default 1.0 for the player
    // and bots; the founders controller drives Satoshi/Hal weights. Bet-driven player mining
    // does not use this — it only governs RunWeightedBlockLottery (bootstrap / founder mining).
    public double HashrateWeight { get; set; } = 1.0;
    private long _candidateNonce;
    private string _candidateKey = string.Empty;
    private string _candidateMerkleRoot = string.Empty;

    public NodeAgent(string nodeId)
    {
        NodeId = nodeId;
        (WalletAddress, WalletPublicKey, WalletPrivateKey, WalletSecp256k1PublicKey) = CryptoUtils.GenerateWallet();
    }

    public NodeAgent(string nodeId, string address, string signingPublicKey, string signingPrivateKey, string secp256k1PublicKey)
    {
        NodeId = nodeId;
        WalletAddress = address;
        WalletPublicKey = signingPublicKey;
        WalletPrivateKey = signingPrivateKey;
        WalletSecp256k1PublicKey = secp256k1PublicKey;
    }

    public Transaction CreateSignedTransaction(decimal amount, string recipientAddress)
    {
        Transaction tx = Blockchain.CreateUnsignedTransaction(amount, WalletAddress, recipientAddress);
        string payload = BlockchainService.BuildTransactionPayload(tx);
        tx.SignatureBase64 = CryptoUtils.Sign(payload, WalletPrivateKey);
        tx.PublicKeyBase64 = WalletPublicKey;
        tx.Secp256k1PublicKeyBase64 = WalletSecp256k1PublicKey;
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

    public Block MinePendingTransactions(decimal rewardAmount, long timestampUnixMs)
    {
        // Mine current pending transactions first. The timestamp is fixed before mining so it is
        // part of the hashed header (Step 4).
        Block lastBlock = Blockchain.GetLastBlock();
        string merkleRoot = MerkleTree.ComputeRoot(Blockchain.PendingTransactions);

        long nonce = Blockchain.ProofOfWork(lastBlock.Hash, merkleRoot, timestampUnixMs);
        string hash = Blockchain.HashHeader(lastBlock.Hash, merkleRoot, timestampUnixMs, nonce);
        Block minedBlock = Blockchain.CreateNewBlock(nonce, lastBlock.Hash, hash, timestampUnixMs, merkleRoot);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;

        // Reward becomes pending for the next block, matching the current flow (coinbase-in-block is Step 4b).
        Blockchain.AddTransactionToPendingTransactions(CreateCoinbaseReward(rewardAmount));
        return minedBlock;
    }

    public Block? TryMineSingleNonceAttempt(decimal rewardAmount, long timestampUnixMs)
    {
        Block lastBlock = Blockchain.GetLastBlock();
        int nextIndex = lastBlock.Index + 1;
        string pendingFingerprint = string.Join("|", Blockchain.PendingTransactions.Select(t => t.TransactionId));
        string candidateKey = $"{lastBlock.Hash}:{nextIndex}:{pendingFingerprint}";
        if (!string.Equals(candidateKey, _candidateKey, System.StringComparison.Ordinal))
        {
            _candidateKey = candidateKey;
            _candidateNonce = 0;
            _candidateMerkleRoot = MerkleTree.ComputeRoot(Blockchain.PendingTransactions);
        }

        // One attempt this bet: current candidate's Merkle root + the caller's timestamp + the rolling nonce.
        string hash = Blockchain.HashHeader(lastBlock.Hash, _candidateMerkleRoot, timestampUnixMs, _candidateNonce);
        if (!BlockchainService.IsHashAtTargetDifficulty(hash))
        {
            _candidateNonce++;
            return null;
        }

        Block minedBlock = Blockchain.CreateNewBlock(_candidateNonce, lastBlock.Hash, hash, timestampUnixMs, _candidateMerkleRoot);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;
        Blockchain.AddTransactionToPendingTransactions(CreateCoinbaseReward(rewardAmount));

        _candidateNonce = 0;
        _candidateKey = string.Empty;
        _candidateMerkleRoot = string.Empty;
        return minedBlock;
    }

    public long GetCurrentCandidateNonce()
    {
        return _candidateNonce;
    }
}
