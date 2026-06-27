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

    // Step 8.2 — when set (founders that mine: Satoshi, Hal), every coinbase is paid to a FRESH derived
    // address (address non-reuse) instead of the static WalletAddress. Null for the player/bots/casino,
    // which keep a single coinbase address. The frontier is positioned from the chain at init and advances
    // as each block is mined (MarkReceiveConsumed). MinedByAddress stays = WalletAddress (stable identity).
    public DerivedAddressWallet? ReceiveWallet { get; set; }

    // Relative mining power for the weighted block lottery (Step 2). Default 1.0 for the player
    // and bots; the founders controller drives Satoshi/Hal weights. Bet-driven player mining
    // does not use this — it only governs RunWeightedBlockLottery (bootstrap / founder mining).
    public double HashrateWeight { get; set; } = 1.0;
    private long _candidateNonce;
    private string _candidateKey = string.Empty;
    private BlockTemplate? _candidateTemplate;
    // Difficulty is LOCKED on the first nonce attempt at a given tip (block height) and kept for the whole
    // block — even if the mempool changes (which rebuilds the candidate template) — so a power change only
    // affects the NEXT block. `_difficultyTipHash` is the tip the locked value was computed for.
    private double _candidateDifficulty;
    private string _difficultyTipHash = string.Empty;

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

    // deterministicSalt: pass a fixed string (e.g. a historical-event key) to make the content-hash txid
    // reproducible across runs/frames, so the scripted-history injectors (Step 7.3/7.4) get idempotency for
    // free via ContainsTransactionId. Default null keeps the random per-tx salt for normal traffic.
    public Transaction CreateSignedTransaction(decimal amount, string recipientAddress, decimal fee = 0m, string? deterministicSalt = null)
    {
        Transaction tx = Blockchain.CreateUnsignedTransaction(amount, WalletAddress, recipientAddress);
        if (deterministicSalt != null)
        {
            tx.Salt = deterministicSalt;
        }
        tx.Fee = fee; // set before computing the id so amount + fee are part of the content hash (Step 4b.2/4b.3)
        tx.TransactionId = BlockchainService.ComputeTransactionId(tx); // content-hash txid (OQ-C6)
        string payload = BlockchainService.BuildTransactionPayload(tx);
        tx.SignatureBase64 = CryptoUtils.Sign(payload, WalletPrivateKey);
        tx.PublicKeyBase64 = WalletPublicKey;
        tx.Secp256k1PublicKeyBase64 = WalletSecp256k1PublicKey;
        return tx;
    }

    public Block MinePendingTransactions(decimal rewardAmount, long timestampUnixMs, double networkPower = 0d, double? forcedDifficulty = null)
    {
        // Build the candidate (coinbase-in-block + selected mempool txs), then full PoW. The
        // timestamp is fixed before mining so it is part of the hashed header (Step 4).
        Block lastBlock = Blockchain.GetLastBlock();
        string coinbaseRecipient = ReceiveWallet?.NextReceiveAddress() ?? WalletAddress;
        BlockTemplate template = BlockTemplateBuilder.Build(coinbaseRecipient, rewardAmount, Blockchain.PendingTransactions, lastBlock.Index + 1);

        // forcedDifficulty is used by the historical bootstrap: its block timestamps are scripted (not driven
        // by mining), so the block-time regulator would drift the difficulty meaninglessly — pin it instead.
        double difficulty = forcedDifficulty ?? Blockchain.GetNextBlockDifficulty(networkPower);
        long nonce = Blockchain.ProofOfWork(lastBlock.Hash, template.MerkleRoot, timestampUnixMs, difficulty);
        string hash = Blockchain.HashHeader(lastBlock.Hash, template.MerkleRoot, timestampUnixMs, nonce);
        Block minedBlock = Blockchain.CommitBlock(nonce, lastBlock.Hash, hash, timestampUnixMs, template, difficulty);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;
        minedBlock.MiningPower = networkPower;
        ReceiveWallet?.MarkReceiveConsumed(); // advance to the next fresh coinbase address (Step 8.2)
        return minedBlock;
    }

    public Block? TryMineSingleNonceAttempt(decimal rewardAmount, long timestampUnixMs, double networkPower = 0d)
    {
        Block lastBlock = Blockchain.GetLastBlock();
        int nextIndex = lastBlock.Index + 1;

        // Lock the block's difficulty on the FIRST attempt at this tip (block height) and keep it for the
        // whole block, regardless of later mempool/power changes. It re-locks only when the tip advances
        // (a new block was mined) → a power change takes effect from the NEXT block, never the one in progress.
        if (!string.Equals(lastBlock.Hash, _difficultyTipHash, System.StringComparison.Ordinal))
        {
            _difficultyTipHash = lastBlock.Hash;
            _candidateDifficulty = Blockchain.GetNextBlockDifficulty(networkPower);
        }

        string pendingFingerprint = string.Join("|", Blockchain.PendingTransactions.Select(t => t.TransactionId));
        string candidateKey = $"{lastBlock.Hash}:{nextIndex}:{pendingFingerprint}";
        if (!string.Equals(candidateKey, _candidateKey, System.StringComparison.Ordinal) || _candidateTemplate is null)
        {
            _candidateKey = candidateKey;
            _candidateNonce = 0;
            // Build the candidate once per (tip, mempool) state; only the nonce rolls across bets. (The
            // difficulty is NOT touched here — it's locked per tip above, so a mempool change can't move it.)
            // ReceiveWallet (founders) supplies a fresh coinbase address; the frontier only advances on commit,
            // so rebuilds within the same block reuse the same address (Step 8.2).
            string coinbaseRecipient = ReceiveWallet?.NextReceiveAddress() ?? WalletAddress;
            _candidateTemplate = BlockTemplateBuilder.Build(coinbaseRecipient, rewardAmount, Blockchain.PendingTransactions, nextIndex);
        }

        string hash = Blockchain.HashHeader(lastBlock.Hash, _candidateTemplate.MerkleRoot, timestampUnixMs, _candidateNonce);
        if (!BlockchainService.IsHashAtTargetDifficulty(hash, _candidateDifficulty))
        {
            _candidateNonce++;
            return null;
        }

        Block minedBlock = Blockchain.CommitBlock(_candidateNonce, lastBlock.Hash, hash, timestampUnixMs, _candidateTemplate, _candidateDifficulty);
        minedBlock.MinedByNodeId = NodeId;
        minedBlock.MinedByAddress = WalletAddress;
        minedBlock.MiningPower = networkPower;

        ReceiveWallet?.MarkReceiveConsumed(); // advance to the next fresh coinbase address (Step 8.2)
        _candidateNonce = 0;
        _candidateKey = string.Empty;
        _candidateTemplate = null;
        // Keep `_candidateDifficulty`/`_difficultyTipHash`; they re-lock on the first attempt at the new tip.
        return minedBlock;
    }

    public long GetCurrentCandidateNonce()
    {
        return _candidateNonce;
    }

    // The difficulty locked for the candidate currently being mined (0 if none built yet — e.g. idle/between
    // blocks). Lets the UI show the in-progress block's difficulty, which is fixed until that block is mined.
    public double GetCurrentCandidateDifficulty()
    {
        return _candidateDifficulty;
    }
}
