using GodotBlockchainPort.Blockchain;
using System.Collections.Generic;
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
    // Step 8.4 — the PLAYER also owns a ReceiveWallet (for change outputs + signing any owned address) but
    // keeps RotateCoinbaseAddress = false, so its coinbases stay on the base address (spread is Satoshi-only).
    public DerivedAddressWallet? ReceiveWallet { get; set; }

    // Step 8.4 — gates whether ReceiveWallet rotates the COINBASE address. True (default) for mining founders
    // that spread every reward across fresh addresses (Satoshi); false for the player, which owns a
    // ReceiveWallet only for change/signing and keeps every coinbase on its single base address. Ignored when
    // ReceiveWallet is null.
    public bool RotateCoinbaseAddress { get; set; } = true;

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

    // Step 8 (full UTXO model, A.5) — build a signed spend from N inputs to M outputs. Each input carries the
    // outpoint it consumes + the keys of the address that owns that output; signing is PER INPUT over the tx
    // sighash (the txid), so inputs across several owned derived addresses can be combined (the consolidation
    // case). deterministicSalt makes the txid reproducible for scripted historical events (idempotency);
    // null keeps a random per-tx salt for normal traffic.
    public Transaction BuildSignedSpend(
        IReadOnlyList<(OutPoint outpoint, string address, string signingPublicKey, string signingPrivateKey, string secp256k1PublicKey)> inputs,
        List<TxOutput> outputs, decimal fee, string? deterministicSalt = null)
    {
        var txInputs = inputs.Select(i => new TxInput
        {
            Source = i.outpoint,
            Address = i.address,
            PublicKeyBase64 = i.signingPublicKey,
            Secp256k1PublicKeyBase64 = i.secp256k1PublicKey
        }).ToList();

        Transaction tx = BlockchainService.CreateUnsignedTransaction(txInputs, outputs, fee, deterministicSalt);
        tx.TransactionId = BlockchainService.ComputeTransactionId(tx);   // content-hash txid (OQ-C6 / A.6)
        string payload = BlockchainService.BuildTransactionPayload(tx);  // sighash = the txid (A.5)
        for (int k = 0; k < txInputs.Count; k++)
            txInputs[k].SignatureBase64 = CryptoUtils.Sign(payload, inputs[k].signingPrivateKey);
        return tx;
    }

    // The address a freshly mined coinbase pays. Founders that rotate (Satoshi) draw a fresh derived address
    // per block (address non-reuse); the player and single-address miners keep their base address (Step 8.4).
    private string CoinbaseRecipient =>
        (ReceiveWallet != null && RotateCoinbaseAddress) ? ReceiveWallet.NextReceiveAddress() : WalletAddress;

    // Advances the rotating coinbase frontier after a block commits — only for nodes whose coinbase rotates
    // (Satoshi). The player keeps its base coinbase and advances its frontier on change outputs only (Step 8.4).
    private void OnCoinbaseCommitted()
    {
        if (RotateCoinbaseAddress) ReceiveWallet?.MarkReceiveConsumed();
    }

    public Block MinePendingTransactions(decimal rewardAmount, long timestampUnixMs, double networkPower = 0d, double? forcedDifficulty = null)
    {
        // Build the candidate (coinbase-in-block + selected mempool txs), then full PoW. The
        // timestamp is fixed before mining so it is part of the hashed header (Step 4).
        Block lastBlock = Blockchain.GetLastBlock();
        string coinbaseRecipient = CoinbaseRecipient;
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
        OnCoinbaseCommitted(); // advance to the next fresh coinbase address (Step 8.2; no-op for the player)
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
            // so rebuilds within the same block reuse the same address (Step 8.2). The player keeps its base.
            string coinbaseRecipient = CoinbaseRecipient;
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

        OnCoinbaseCommitted(); // advance to the next fresh coinbase address (Step 8.2; no-op for the player)
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
