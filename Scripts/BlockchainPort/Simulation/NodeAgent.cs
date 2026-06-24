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
    private BlockTemplate? _candidateTemplate;

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

    public Transaction CreateSignedTransaction(decimal amount, string recipientAddress, decimal fee = 0m)
    {
        Transaction tx = Blockchain.CreateUnsignedTransaction(amount, WalletAddress, recipientAddress);
        tx.Fee = fee; // set before computing the id so amount + fee are part of the content hash (Step 4b.2/4b.3)
        tx.TransactionId = BlockchainService.ComputeTransactionId(tx); // content-hash txid (OQ-C6)
        string payload = BlockchainService.BuildTransactionPayload(tx);
        tx.SignatureBase64 = CryptoUtils.Sign(payload, WalletPrivateKey);
        tx.PublicKeyBase64 = WalletPublicKey;
        tx.Secp256k1PublicKeyBase64 = WalletSecp256k1PublicKey;
        return tx;
    }

    public Block MinePendingTransactions(decimal rewardAmount, long timestampUnixMs)
    {
        // Build the candidate (coinbase-in-block + selected mempool txs), then full PoW. The
        // timestamp is fixed before mining so it is part of the hashed header (Step 4).
        Block lastBlock = Blockchain.GetLastBlock();
        BlockTemplate template = BlockTemplateBuilder.Build(WalletAddress, rewardAmount, Blockchain.PendingTransactions, lastBlock.Index + 1);

        double difficulty = Blockchain.GetNextBlockDifficulty();
        long nonce = Blockchain.ProofOfWork(lastBlock.Hash, template.MerkleRoot, timestampUnixMs, difficulty);
        string hash = Blockchain.HashHeader(lastBlock.Hash, template.MerkleRoot, timestampUnixMs, nonce);
        Block minedBlock = Blockchain.CommitBlock(nonce, lastBlock.Hash, hash, timestampUnixMs, template, difficulty);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;
        return minedBlock;
    }

    public Block? TryMineSingleNonceAttempt(decimal rewardAmount, long timestampUnixMs)
    {
        Block lastBlock = Blockchain.GetLastBlock();
        int nextIndex = lastBlock.Index + 1;
        string pendingFingerprint = string.Join("|", Blockchain.PendingTransactions.Select(t => t.TransactionId));
        string candidateKey = $"{lastBlock.Hash}:{nextIndex}:{pendingFingerprint}";
        if (!string.Equals(candidateKey, _candidateKey, System.StringComparison.Ordinal) || _candidateTemplate is null)
        {
            _candidateKey = candidateKey;
            _candidateNonce = 0;
            // Build the candidate once per (tip, mempool) state; only the nonce rolls across bets.
            _candidateTemplate = BlockTemplateBuilder.Build(WalletAddress, rewardAmount, Blockchain.PendingTransactions, nextIndex);
        }

        double difficulty = Blockchain.GetNextBlockDifficulty();
        string hash = Blockchain.HashHeader(lastBlock.Hash, _candidateTemplate.MerkleRoot, timestampUnixMs, _candidateNonce);
        if (!BlockchainService.IsHashAtTargetDifficulty(hash, difficulty))
        {
            _candidateNonce++;
            return null;
        }

        Block minedBlock = Blockchain.CommitBlock(_candidateNonce, lastBlock.Hash, hash, timestampUnixMs, _candidateTemplate, difficulty);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;

        _candidateNonce = 0;
        _candidateKey = string.Empty;
        _candidateTemplate = null;
        return minedBlock;
    }

    public long GetCurrentCandidateNonce()
    {
        return _candidateNonce;
    }
}
