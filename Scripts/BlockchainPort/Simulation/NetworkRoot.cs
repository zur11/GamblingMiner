using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using GodotBlockchainPort.Blockchain;
using Scripts.Hardware;
#nullable enable

namespace GodotBlockchainPort.Simulation;

public partial class NetworkRoot : Node
{
    private static readonly NetworkSimulator SharedNetwork = new();
    private static readonly Dictionary<string, NodeAgent> SharedNodesById = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool _isInitialized;
    // When true (during the historical bootstrap), per-block persistence and bot recirculation are
    // suppressed so ~114 blocks can be mined in one pass; the bootstrap persists once at the end.
    private static bool _bulkMining;
    private static Block? _lastMinedBlock;
    private static string _lastMinedByNodeId = string.Empty;
    private static int _currentMinerStreak;
    private static int _bestMinerStreak;
    // D.2 hybrid difficulty: total active mining power (Σ active miners' bets/sec), pushed in by
    // SimulationService. The feed-forward term in GetNextBlockDifficulty reads this. 0 = unknown (bootstrap/idle).
    private static double _activeMiningPower;

    private const string PlayerNodeId = "player";
    private const string CasinoNodeId = "casino";
    private const string SatoshiNodeId = "satoshi";
    private const string HalNodeId = "hal";
    private const decimal GenesisRewardBtc = 50m;
    // 50 × 2100 × 2 = 210,000 BTC total supply; ~4 in-game years per halving at 100X scale.
    // If this value changes, recalculate the emission cap in GetBlockRewardForNextCandidate() to preserve the ~2140 end-of-supply year.
    private const int HalvingIntervalBlocks = 2100;
    private const string BlockchainDir = "user://blockchain";
    private const string StatePath = "user://blockchain/state.json";
    // Step 8 (full UTXO model) — bumped when the on-disk chain format changes incompatibly. The old
    // account/balance chain has no input→output (UTXO) linkage, so it cannot be replayed into a UTXO set;
    // on a version change we wipe the chain + clock + financial state and re-bootstrap a fresh world (the
    // "clean reset" decision). Increment this whenever the persisted Transaction/Block shape changes.
    private const int WorldFormatVersion = 2;
    private const string WorldVersionPath = "user://world_format_version.txt";
    // Casino community pool: fixed per-payout transaction fee (lowest available to the casino),
    // deducted from each contributor's gross share before it is sent (Phase 2).
    private const decimal CasinoTxFee = 0.1m;

    // Blocks a miner bot must wait AFTER its own first mined block before it starts donating BTC —
    // measured per bot, so it works for bots introduced gradually (not an absolute chain index).
    private const int CirculationWarmupBlocks = 5;
    private const decimal MinBotSpendableBalanceBtc = 1.0m;
    private const double BotSendProbabilityPerBlock = 0.5;
    private const decimal MinSendFractionDecimal = 0.10m;
    private const decimal MaxSendFractionDecimal = 0.40m;
    // Step 4b.2: bot-chosen fee range (BTC), collected into the winning miner's coinbase.
    private const decimal MinBotFeeBtc = 0.1m;
    private const decimal MaxBotFeeBtc = 1.0m;
    // Referral auction (starter, option-b gradual introduction): a new non-miner enters the auction
    // every ~2 in-game days after live mining begins; each runs a 7-in-game-day donation window.
    private const long NonMinerIntroIntervalMs = 2L * 86_400_000L;
    private const long AuctionWindowMs = 7L * 86_400_000L;

    public override void _Ready()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        // Step 8 — if the on-disk world predates the UTXO model, wipe the incompatible chain/clock/financial
        // state so this launch re-bootstraps a fresh UTXO world (clean reset). Must run before TryLoadSnapshot.
        ResetWorldIfFormatChanged();

        // Load saved state first so wallets can be restored before nodes are created.
        BlockchainStateSnapshot? savedState = TryLoadSnapshot();

        SharedNodesById.Clear();
        SharedNetwork.RegisterNode(CreateAndRegisterNode(PlayerNodeId, savedState));
        for (int i = 1; i <= 4; i++)
        {
            SharedNetwork.RegisterNode(CreateAndRegisterNode($"bot_{i}", savedState));
        }

        // Non-miner bots: register as NodeAgents so they can sign and broadcast
        // transactions once they hold a balance. Conditional on HasFullWallet so
        // old registry files (without non-miner keys) skip registration gracefully.
        foreach (BotWalletRecord nonMiner in BotWalletRegistry.NonMinerBots)
        {
            if (nonMiner.HasFullWallet)
                SharedNetwork.RegisterNode(CreateAndRegisterNode(nonMiner.NodeId, savedState));
        }

        // Casino wallet node — keys derived deterministically from seed phrase each launch.
        // Registered here so CasinoFinances can call CreateAndBroadcastTransactionToAddress("casino", ...).
        CasinoWalletState? casinoWalletState = WalletInitializationService.CasinoWallet;
        if (casinoWalletState != null)
        {
            string casinoSeed = string.Join(" ", casinoWalletState.SeedWords);
            (string casinoSignPub, string casinoSignPriv) = CryptoUtils.DeriveSigningKeypair(casinoSeed);
            string casinoSecp256k1 = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(casinoSeed);
            var casinoNode = new NodeAgent(CasinoNodeId, casinoWalletState.BaseAddress,
                                          casinoSignPub, casinoSignPriv, casinoSecp256k1);
            // Step 8 (casino/Hal extension) — the casino carries a derived wallet for CHANGE-only rotation
            // (RotateCoinbaseAddress = false; it does not mine). Receives (pool fees) land on the base; each
            // send returns change to a fresh derived address, like the player. Rescanned from the chain at init.
            casinoNode.ReceiveWallet = new DerivedAddressWallet(casinoSeed);
            casinoNode.RotateCoinbaseAddress = false;
            SharedNetwork.RegisterNode(casinoNode);
            SharedNodesById[CasinoNodeId] = casinoNode;
        }

        // Founder nodes — Satoshi & Hal. Keys derived from their seed phrases each launch,
        // same pattern as the casino. Registered before ApplyStateFromSnapshot so they receive
        // the synced chain. They mine via the weighted lottery introduced in a later step; here
        // they exist as nodes whose addresses receive the genesis / early coinbase rewards.
        RegisterFounderNode(WalletInitializationService.SatoshiWallet);
        RegisterFounderNode(WalletInitializationService.HalWallet);
        RegisterFounderNode(WalletInitializationService.MikeHearnWallet);

        ApplyStateFromSnapshot(savedState);
        NormalizeGenesisAcrossNodes();
        EnsureSecondBlockBootstrapPendingTx();
        RescanFounderReceiveWallets(); // Step 8.2 — position founders' fresh-coinbase frontier from the chain
        PersistStateToDisk();
        _isInitialized = true;
    }

    private static NodeAgent CreateAndRegisterNode(string nodeId, BlockchainStateSnapshot? savedState = null)
    {
        NodeAgent node;

        if (nodeId == PlayerNodeId)
        {
            // Player node always uses the seed-phrase wallet so mining coinbase rewards
            // go to the same address shown in BTCWallet. The persisted random wallet is ignored.
            var playerWallet = WalletInitializationService.PlayerWallet;
            if (playerWallet != null)
            {
                string seedPhrase = string.Join(" ", playerWallet.SeedWords);
                var (sigPub, sigPriv) = CryptoUtils.DeriveSigningKeypair(seedPhrase);
                string secp256k1Pub  = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase);
                node = new(nodeId, playerWallet.BaseAddress, sigPub, sigPriv, secp256k1Pub);
                // Step 8.4 — the player carries a derived-address wallet for CHANGE outputs + signing any owned
                // address, but RotateCoinbaseAddress = false keeps every mined reward on the base address (coinbase
                // spread is a Satoshi-only trait). The player's wallet becomes multi-address only by spending: each
                // send's change lands on a fresh derived address. addr(0) == BaseAddress, so existing balances and
                // the chain rescan (RescanFounderReceiveWallets) are untouched.
                node.ReceiveWallet = new DerivedAddressWallet(seedPhrase);
                node.RotateCoinbaseAddress = false;
            }
            else if (savedState?.NodeWallets?.TryGetValue(nodeId, out NodeWalletSnapshot? pw) == true && pw?.IsComplete() == true)
                node = new(nodeId, pw.Address, pw.SigningPublicKeyBase64, pw.SigningPrivateKeyBase64, pw.Secp256k1PublicKeyBase64);
            else
                node = new(nodeId);
        }
        else
        {
            // Bot nodes: registry (authoritative) → saved snapshot (migration fallback) → fresh random wallet.
            BotWalletRecord? botRecord = BotWalletRegistry.GetBot(nodeId);
            if (botRecord?.HasFullWallet == true)
                node = new(nodeId, botRecord.Address, botRecord.SigningPublicKeyBase64!, botRecord.SigningPrivateKeyBase64!, botRecord.Secp256k1PublicKeyBase64!);
            else if (savedState?.NodeWallets?.TryGetValue(nodeId, out NodeWalletSnapshot? wallet) == true && wallet?.IsComplete() == true)
                node = new(nodeId, wallet.Address, wallet.SigningPublicKeyBase64, wallet.SigningPrivateKeyBase64, wallet.Secp256k1PublicKeyBase64);
            else
                node = new(nodeId);
        }

        SharedNodesById[nodeId] = node;
        return node;
    }

    private static void RegisterFounderNode(FounderWalletState? founder)
    {
        if (founder is null)
        {
            return;
        }

        string seed = string.Join(" ", founder.SeedWords);
        (string signPub, string signPriv) = CryptoUtils.DeriveSigningKeypair(seed);
        string secp256k1Pub = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seed);
        var node = new NodeAgent(founder.FounderId, founder.BaseAddress, signPub, signPriv, secp256k1Pub);

        // Step 8.2 — coinbase address spread (a fresh coinbase address per block) is a SATOSHI-ONLY trait
        // ("Patoshi"/one-address-per-reward → ~220 addresses at the 11,000-BTC floor): Satoshi keeps the
        // default RotateCoinbaseAddress = true.
        // Step 8 (casino/Hal extension) — Hal ALSO gets a derived wallet, but for CHANGE-only rotation like
        // the player (RotateCoinbaseAddress = false): his coinbase stays on his single base address (other
        // early miners reused addresses; coinbase spread stays Satoshi-only), and he only becomes multi-address
        // when he SENDS (change → fresh address). Hearn stays single-address (no ReceiveWallet). The frontier
        // is positioned from the chain by RescanFounderReceiveWallets() once the chain is loaded.
        if (founder.FounderId == "satoshi")
        {
            node.ReceiveWallet = new DerivedAddressWallet(seed);
        }
        else if (founder.FounderId == "hal")
        {
            node.ReceiveWallet = new DerivedAddressWallet(seed);
            node.RotateCoinbaseAddress = false;
        }

        SharedNetwork.RegisterNode(node);
        SharedNodesById[founder.FounderId] = node;
    }

    public Transaction? CreateAndBroadcastTransaction(string fromNodeId, string recipientNodeId, decimal amount, decimal fee = 0m)
    {
        EnsureInitialized();
        if (amount <= 0m)
        {
            return null;
        }

        if (!SharedNodesById.TryGetValue(fromNodeId, out NodeAgent? sender) || !SharedNodesById.TryGetValue(recipientNodeId, out NodeAgent? recipient))
        {
            GD.PrintErr($"Invalid route: {fromNodeId} -> {recipientNodeId}");
            return null;
        }

        if (sender.NodeId == recipient.NodeId)
        {
            return null;
        }

        // Step 8 — UTXO spend: coin-select the sender's owned UTXOs (combining several if needed) + change.
        // No disk write: a block is the only commit. The tx lives in the in-memory mempool and becomes durable
        // when the next block is mined; if the app closes before that, it is discarded on restart (revert to block).
        return BuildAndBroadcastUtxoSpend(sender, recipient.WalletAddress, amount, fee, null);
    }

    public bool MineAndBroadcastBlock(string minerNodeId, long? minedAtUnixMs = null)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(minerNodeId, out NodeAgent? miner))
        {
            return false;
        }

        MineForNode(miner, minedAtUnixMs);
        return true;
    }

    // Shared mining core: full PoW for one block by the given node, then broadcast + bookkeeping.
    // The timestamp is fixed before mining (it is part of the hashed header — Step 4).
    private static void MineForNode(NodeAgent miner, long? minedAtUnixMs)
    {
        long timestamp = minedAtUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        decimal reward = GetBlockRewardForNextCandidate(miner);
        // During the scripted historical bootstrap, pin difficulty to InitialDifficulty (the regulator can't
        // run meaningfully on pre-scripted timestamps). Live mining uses the regulator via TryMineSingleNonceAttempt.
        double? forcedDifficulty = _bulkMining ? BlockchainService.InitialDifficulty : (double?)null;
        Block block = miner.MinePendingTransactions(reward, timestamp, _activeMiningPower, forcedDifficulty);
        HandleMinedBlock(miner, block);
    }

    // ── Step 3a: static surface for the historical bootstrap ───────────────────
    // These let HistoricalBootstrapService drive the engine from CalendarTimeService._Ready()
    // before any scene (and thus any NetworkRoot Node instance) exists.

    public static void EnsureReady() => EnsureInitialized();

    public static int GetPlayerChainLengthStatic() =>
        SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player) ? player.Blockchain.Chain.Count : 0;

    public static bool MineNodeStatic(string nodeId, long minedAtUnixMs)
    {
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? miner))
        {
            return false;
        }

        MineForNode(miner, minedAtUnixMs);
        return true;
    }

    // Step 7.3/7.4 + Step 8: inject a scripted historical signed transaction between two registered nodes
    // (the 12 Jan 2009 Satoshi→Hal 10 BTC tx, or the April 2009 Satoshi↔Hearn round-trip). Full UTXO model:
    //   • SOURCE   — coin-selects owned UTXOs (exact single match → no change; else largest-first combine).
    //   • RECIPIENT — a FRESH derived address when the recipient is a multi-address founder (address non-reuse
    //     — Satoshi receives E6b at a new address), else the recipient's base.
    //   • CHANGE   — the remainder is a real change OUTPUT (vout 1) to a FRESH sender address (E6's 17.49 = E8),
    //     now part of the SAME transaction rather than a separate change tx.
    // Idempotency is by SALT (unique per event). Chain-derived, surviving the revert-to-last-block model.
    public static bool InjectHistoricalSignedTxStatic(string fromNodeId, string toNodeId, decimal amount, string deterministicSalt, decimal fee = 0m)
    {
        EnsureInitialized();
        if (amount <= 0m
            || !SharedNodesById.TryGetValue(fromNodeId, out NodeAgent? sender)
            || !SharedNodesById.TryGetValue(toNodeId, out NodeAgent? recipient))
        {
            return false;
        }

        // Idempotent no-op if this event is already pending or confirmed (by salt).
        if (IsHistoricalSaltPresent(sender, deterministicSalt))
        {
            return true;
        }

        // Recipient lands on a FRESH derived address only when it is a full address-non-reuse founder
        // (RotateCoinbaseAddress = Satoshi) — e.g. Satoshi receives E6b at a new address (historically
        // confirmed). Change-only-rotation nodes (Hal, casino) and single-address nodes (Hearn) receive on
        // their BASE address — incoming deposit rotation is deferred (OQ-8.3); they only rotate CHANGE on send.
        bool rotateRecipient = recipient.ReceiveWallet != null && recipient.RotateCoinbaseAddress;
        string recipientAddr = rotateRecipient ? recipient.ReceiveWallet!.NextReceiveAddress() : recipient.WalletAddress;

        Transaction? tx = BuildAndBroadcastUtxoSpend(sender, recipientAddr, amount, fee, deterministicSalt);
        if (tx is null)
        {
            return false; // not funded yet → caller retries on a later block
        }

        if (rotateRecipient) recipient.ReceiveWallet!.MarkReceiveConsumed(); // the fresh receive address is now used
        return true;
    }

    // Step 8 (full UTXO model) — THE shared spend path for every node (player, founders, bots, casino). Coin-
    // selects owned UTXOs to cover amount+fee, builds ONE signed transaction with the recipient output plus an
    // optional change output to a fresh owned address, and broadcasts it. Returns the tx, or null if the
    // node's total spendable across ALL its addresses can't cover amount+fee. A node with a ReceiveWallet
    // (player, Satoshi) returns change to a fresh derived address; others return change to their base address.
    private static Transaction? BuildAndBroadcastUtxoSpend(NodeAgent sender, string recipientAddress, decimal amount, decimal fee, string? deterministicSalt)
    {
        decimal need = amount + fee;
        HashSet<string> owned = sender.ReceiveWallet != null
            ? new HashSet<string>(sender.ReceiveWallet.OwnedAddresses) { sender.WalletAddress }
            : new HashSet<string> { sender.WalletAddress };

        IReadOnlyList<(OutPoint outpoint, string address, decimal amount)> available = sender.Blockchain.GetSpendableUtxos(owned);
        List<(OutPoint outpoint, string address, decimal amount)>? chosen = SelectUtxos(available, need);
        if (chosen is null)
        {
            return null; // insufficient total funds, even combining every UTXO
        }

        var inputs = new List<(OutPoint, string, string, string, string)>(chosen.Count);
        decimal gathered = 0m;
        foreach ((OutPoint outpoint, string address, decimal value) in chosen)
        {
            if (!TryResolveInputKeys(sender, address, out (string pub, string priv, string secp) keys))
                return null; // an owned address whose keys we can't derive (should not happen)
            inputs.Add((outpoint, address, keys.pub, keys.priv, keys.secp));
            gathered += value;
        }

        var outputs = new List<TxOutput> { new() { Address = recipientAddress, Amount = amount } };
        decimal change = gathered - need;
        bool hasChange = change > 0m;
        if (hasChange)
        {
            string changeAddr = sender.ReceiveWallet?.NextReceiveAddress() ?? sender.WalletAddress;
            if (changeAddr == recipientAddress) changeAddr = sender.WalletAddress; // never merge change into the payee
            outputs.Add(new TxOutput { Address = changeAddr, Amount = change });
        }

        Transaction tx = sender.BuildSignedSpend(inputs, outputs, fee, deterministicSalt);
        if (!sender.Blockchain.AddTransactionToPendingTransactions(tx))
        {
            return null;
        }
        SharedNetwork.BroadcastTransaction(sender.NodeId, tx);
        if (hasChange) sender.ReceiveWallet?.MarkReceiveConsumed(); // a fresh change address was used
        return tx;
    }

    // Coin selection: prefer an EXACT single-UTXO match (amount+fee → no change; preserves scripted exact-
    // amount events like E7a's 32.51 and E7b's whole 50-coinbase); otherwise accumulate LARGEST-first until
    // covered — combining several UTXOs into one transaction (the multi-input consolidation case). Returns
    // null when even every available UTXO together can't cover `need`.
    private static List<(OutPoint outpoint, string address, decimal amount)>? SelectUtxos(
        IReadOnlyList<(OutPoint outpoint, string address, decimal amount)> available, decimal need)
    {
        foreach ((OutPoint outpoint, string address, decimal amount) u in available)
            if (u.amount == need)
                return new List<(OutPoint, string, decimal)> { u };

        var chosen = new List<(OutPoint, string, decimal)>();
        decimal gathered = 0m;
        foreach ((OutPoint outpoint, string address, decimal amount) u in available.OrderByDescending(x => x.amount))
        {
            chosen.Add(u);
            gathered += u.amount;
            if (gathered >= need) return chosen;
        }
        return null;
    }

    // The signing keys for an owned address: the node's base keypair for WalletAddress, else the per-address
    // derived context from the ReceiveWallet (Step 8.1 TryFindSpendingContext). Lets one spend pull keys for
    // several of the sender's own derived addresses (the consolidation case).
    private static bool TryResolveInputKeys(NodeAgent sender, string address, out (string pub, string priv, string secp) keys)
    {
        if (address == sender.WalletAddress)
        {
            keys = (sender.WalletPublicKey, sender.WalletPrivateKey, sender.WalletSecp256k1PublicKey);
            return true;
        }
        if (sender.ReceiveWallet != null && sender.ReceiveWallet.TryFindSpendingContext(address, out var ctx))
        {
            keys = (ctx.signingPublicKeyBase64, ctx.signingPrivateKeyBase64, ctx.secp256k1PublicKeyBase64);
            return true;
        }
        keys = default;
        return false;
    }

    private static bool IsHistoricalSaltPresent(NodeAgent node, string salt) =>
        node.Blockchain.PendingTransactions.Any(t => t.Salt == salt)
        || node.Blockchain.Chain.Any(b => b.Transactions.Any(t => t.Salt == salt));

    // Whether a scripted historical tx (identified by its unique event SALT) is already CONFIRMED on the
    // canonical chain (not merely pending). Lets HistoricalEventScheduler sequence a multi-step exchange —
    // each step waits for the previous to be mined (Step 7.4). Salt-based (Step 8.3) so it is independent of
    // the now-variable source/recipient addresses. Chain-derived, surviving the revert-to-last-block model.
    public static bool IsHistoricalTxConfirmedStatic(string fromNodeId, string toNodeId, decimal amount, string deterministicSalt, decimal fee = 0m)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
        {
            return false;
        }
        return player.Blockchain.Chain.Any(b => b.Transactions.Any(t => t.Salt == deterministicSalt));
    }

    public static void BeginBulkMining() => _bulkMining = true;

    public static void EndBulkMiningAndPersist()
    {
        _bulkMining = false;
        PersistStateToDisk();
    }

    // ── Step 2: weighted block lottery ─────────────────────────────────────────
    // Picks ONE winner among the given miner node ids with probability proportional to each
    // node's HashrateWeight, then mines exactly one valid block for that winner (full PoW nonce
    // search via the existing MineAndBroadcastBlock path) and broadcasts it. Returns the winning
    // node id, or null if no eligible (registered, weight > 0) miner was supplied.
    //
    // This is the mechanism the historical bootstrap (Step 3) uses to let Satoshi + Hal mine the
    // chain to 21 Mar 2009 without the player betting. Bet-driven player mining is unaffected.
    // rng is injectable so the bootstrap / tests can be made deterministic; defaults to Random.Shared.
    public string? RunWeightedBlockLottery(IReadOnlyList<string> minerNodeIds, long? minedAtUnixMs = null, Random? rng = null)
    {
        EnsureInitialized();
        rng ??= Random.Shared;

        double totalWeight = 0d;
        var eligible = new List<(NodeAgent node, double weight)>();
        foreach (string id in minerNodeIds)
        {
            if (!SharedNodesById.TryGetValue(id, out NodeAgent? node) || node.HashrateWeight <= 0d)
            {
                continue;
            }

            eligible.Add((node, node.HashrateWeight));
            totalWeight += node.HashrateWeight;
        }

        if (eligible.Count == 0 || totalWeight <= 0d)
        {
            return null;
        }

        double roll = rng.NextDouble() * totalWeight;
        NodeAgent winner = eligible[^1].node;
        double cumulative = 0d;
        foreach ((NodeAgent node, double weight) in eligible)
        {
            cumulative += weight;
            if (roll < cumulative)
            {
                winner = node;
                break;
            }
        }

        return MineAndBroadcastBlock(winner.NodeId, minedAtUnixMs) ? winner.NodeId : null;
    }

    public void SetHashrateWeight(string nodeId, double weight)
    {
        EnsureInitialized();
        if (SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            node.HashrateWeight = Math.Max(0d, weight);
        }
    }

    public double GetHashrateWeight(string nodeId)
    {
        EnsureInitialized();
        return SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) ? node.HashrateWeight : 0d;
    }

    public bool TryMineSingleNonceAttempt(string minerNodeId, out Block? minedBlock, long? minedAtUnixMs = null)
    {
        EnsureInitialized();
        minedBlock = null;
        if (!SharedNodesById.TryGetValue(minerNodeId, out NodeAgent? miner))
        {
            return false;
        }

        long timestamp = minedAtUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        decimal reward = GetBlockRewardForNextCandidate(miner);
        minedBlock = miner.TryMineSingleNonceAttempt(reward, timestamp, _activeMiningPower);
        if (minedBlock is null)
        {
            return false;
        }

        HandleMinedBlock(miner, minedBlock);
        return true;
    }

    // Total active mining power (Σ active miners' bets/sec) for the difficulty feed-forward. Set by
    // SimulationService while a player autobet runs; 0 when idle/bootstrapping (feed-forward then no-ops).
    public void SetActiveMiningPower(double power)
    {
        _activeMiningPower = power > 0d ? power : 0d;
    }

    // ── Phase 2: Casino community mining pool ──────────────────────────────────
    // Credits assigned to the casino pool route their nonce attempts to the casino node's chain.
    // When the casino mines a block, its coinbase reward is queued and later distributed to the
    // pool's contributors (proportional to their casino-pool credits) minus a dynamic casino fee.

    // Dynamic casino fee as a function of casino-pool vs. individual mining power (credit totals).
    // ratio = casinoTotal / individualTotal: 1.0 → 30% (balanced); >1 → up to 50%; <1 → down to 10%.
    public static decimal CalculateCasinoFeePercent(int casinoTotal, int individualTotal)
    {
        if (individualTotal <= 0) return 0.50m;
        double ratio = (double)casinoTotal / individualTotal;
        if (ratio >= 1.0)
        {
            double t = Math.Clamp((ratio - 1.0) / 2.0, 0.0, 1.0);
            return (decimal)(0.30 + t * 0.20); // 30% → 50%
        }

        return (decimal)(0.10 + ratio * 0.20); // 10% → 30%
    }

    // One casino-pool nonce attempt: mines on the casino node's behalf. On a hit, the block goes
    // through the normal broadcast/bookkeeping path and its reward is queued for distribution.
    public void TryCasinoNonceAttempt(out Block? minedBlock, long? minedAtUnixMs = null)
    {
        EnsureInitialized();
        minedBlock = null;
        if (!SharedNodesById.TryGetValue(CasinoNodeId, out NodeAgent? casino))
        {
            return;
        }

        long timestamp = minedAtUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        decimal reward = GetBlockRewardForNextCandidate(casino);
        minedBlock = casino.TryMineSingleNonceAttempt(reward, timestamp, _activeMiningPower);
        if (minedBlock is null)
        {
            return;
        }

        HandleMinedBlock(casino, minedBlock);
        QueueCasinoRewardForDistribution(minedBlock, reward);
    }

    // Snapshots contributor credits at mining time, computes per-contributor net payouts (gross share
    // minus the casino tx fee), records the reward event, and attempts distribution immediately.
    private static void QueueCasinoRewardForDistribution(Block block, decimal reward)
    {
        IReadOnlyList<NodeHardwareState> allNodes = HardwareAllocationRepository.AllNodes();
        int casinoTotal = HardwareAllocationRepository.TotalCasinoPoolCredits();
        int individualTotal = HardwareAllocationRepository.TotalIndividualCredits();

        decimal feePercent = CalculateCasinoFeePercent(casinoTotal, individualTotal);
        decimal feeAmount = Scripts.Finance.Money.Normalize(reward * feePercent);
        decimal poolAmount = reward - feeAmount;

        var payouts = new List<CasinoPoolPendingPayout>();
        if (casinoTotal > 0)
        {
            foreach (NodeHardwareState n in allNodes.Where(n => n.CasinoPoolCredits > 0))
            {
                decimal share = Scripts.Finance.Money.Normalize(poolAmount * n.CasinoPoolCredits / casinoTotal);
                decimal net = Scripts.Finance.Money.Normalize(share - CasinoTxFee);
                if (net <= 0m) continue; // reward too small to cover the tx fee → skip (OQ-2)

                string address = GetNodeAddress(n.NodeId);
                if (string.IsNullOrEmpty(address)) continue;

                payouts.Add(new CasinoPoolPendingPayout
                {
                    RecipientNodeId = n.NodeId,
                    RecipientAddress = address,
                    GrossAmount = share,
                    NetAmount = net,
                    FromBlockIndex = block.Index
                });
            }
        }

        var rewardEvent = new CasinoPoolRewardEvent
        {
            BlockIndex = block.Index,
            TotalReward = reward,
            CasinoFeePercent = feePercent,
            CasinoFeeAmount = feeAmount,
            Payouts = payouts,
            Distributed = false
        };

        CasinoPoolRepository.AddRewardEvent(rewardEvent);
        TryDistributePendingCasinoRewards();
    }

    // Sends queued casino-pool payouts whose backing coinbase has matured (CoinbaseMaturity). Each
    // event is all-or-nothing: only attempted once the casino can cover every payout in it, so a
    // partial send can never double-pay on a later retry. Called after every mined block.
    private static void TryDistributePendingCasinoRewards()
    {
        if (!SharedNodesById.TryGetValue(CasinoNodeId, out NodeAgent? casino))
        {
            return;
        }

        foreach (CasinoPoolRewardEvent evt in CasinoPoolRepository.GetUndistributed())
        {
            if (evt.Payouts.Count == 0)
            {
                CasinoPoolRepository.MarkDistributed(evt.BlockIndex); // nothing owed (e.g. no contributors)
                continue;
            }

            decimal required = evt.Payouts.Sum(p => p.NetAmount + CasinoTxFee);
            decimal spendable = casino.Blockchain.GetAddressSpendableBalance(casino.WalletAddress);
            if (spendable < required) continue; // coinbase not matured / not enough yet → retry next block

            bool allSent = true;
            foreach (CasinoPoolPendingPayout payout in evt.Payouts)
            {
                if (SendFromCasino(casino, payout.RecipientAddress, payout.NetAmount) is null)
                {
                    allSent = false;
                }
            }

            if (allSent)
            {
                CasinoPoolRepository.MarkDistributed(evt.BlockIndex);
            }
        }
    }

    // Broadcasts a single payout from the casino wallet at the fixed casino tx fee. Static mirror of
    // CreateAndBroadcastTransactionToAddress for the static distribution path. No disk write — the tx
    // becomes durable when the next block is mined.
    private static Transaction? SendFromCasino(NodeAgent casino, string recipientAddress, decimal amount)
    {
        if (amount <= 0m || string.IsNullOrEmpty(recipientAddress)) return null;
        if (casino.WalletAddress == recipientAddress) return null;

        // Step 8 — UTXO spend (coin-select casino UTXOs + change to its base address).
        return BuildAndBroadcastUtxoSpend(casino, recipientAddress, amount, CasinoTxFee, null);
    }

    private static string GetNodeAddress(string nodeId) =>
        SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) ? node.WalletAddress : string.Empty;

    // Read-only view of the casino-pool reward ledger (for the BTCPoolsAndHardwareShop stats panel).
    public List<CasinoPoolRewardEvent> GetCasinoPoolHistory()
    {
        EnsureInitialized();
        return CasinoPoolRepository.Current.RewardHistory.ToList();
    }

    private static void HandleMinedBlock(NodeAgent miner, Block block)
    {
        // Step 4b: the coinbase now lives inside the block (BlockTemplateBuilder), so it propagates
        // with BroadcastBlock — no separate coinbase-transaction broadcast is needed.
        SharedNetwork.BroadcastBlock(miner.NodeId, block);

        _lastMinedBlock = block;
        if (string.Equals(_lastMinedByNodeId, miner.NodeId, StringComparison.Ordinal))
        {
            _currentMinerStreak++;
        }
        else
        {
            _lastMinedByNodeId = miner.NodeId;
            _currentMinerStreak = 1;
        }

        if (_currentMinerStreak > _bestMinerStreak)
        {
            _bestMinerStreak = _currentMinerStreak;
        }

        if (!_bulkMining)
        {
            AppendDifficultyTrace(miner, block); // F0: per-block difficulty/throughput telemetry (live blocks only)
            ScheduleBotTransactionsAfterBlock(block);
            HistoricalEventScheduler.OnBlockMined(block); // Step 7.4: inject scripted player-era txs at their date
            PersistStateToDisk();
            // After every block (any miner), retry casino-pool payouts whose coinbase has now matured.
            TryDistributePendingCasinoRewards();
        }
    }

    // F0 (difficulty-regulator contingency plan): append one telemetry row per LIVE-mined block so the
    // realized-vs-configured power curve across a power step can be measured instead of inferred. Excludes
    // the historical bootstrap (called inside the !_bulkMining guard). One CSV per chain-miner is interleaved;
    // filter by the `miner` column. realizedPower inverts the equilibrium calibration solvetime = difficulty ×
    // (TargetBlockSeconds / InitialDifficulty) / power, so realizedPower = difficulty × clockSpeed / solveSec.
    private const string DifficultyTracePath = "user://logs/difficulty_trace.csv";

    private static void AppendDifficultyTrace(NodeAgent miner, Block block)
    {
        try
        {
            var chain = miner.Blockchain.Chain;
            if (chain.Count < 2)
            {
                return; // need a previous block to derive a solvetime
            }

            Block prev = chain[chain.Count - 2];
            double solveSec = (block.Timestamp - prev.Timestamp) / 1000d;
            if (solveSec <= 0d)
            {
                return; // non-monotonic timestamp (e.g. bootstrap remnant) — skip rather than divide-by-zero
            }

            double configuredPower = block.MiningPower;
            double clockSpeed = BlockchainService.TargetBlockSeconds / BlockchainService.InitialDifficulty;
            double realizedPower = block.Difficulty * clockSpeed / solveSec;
            double anchor = configuredPower > 0d
                ? BlockchainService.InitialDifficulty * configuredPower
                : prev.Difficulty;
            double solveRatio = solveSec / BlockchainService.TargetBlockSeconds;

            if (!DirAccess.DirExistsAbsolute("user://logs"))
            {
                DirAccess.MakeDirRecursiveAbsolute("user://logs");
            }

            bool exists = FileAccess.FileExists(DifficultyTracePath);
            using FileAccess file = exists
                ? FileAccess.Open(DifficultyTracePath, FileAccess.ModeFlags.ReadWrite)
                : FileAccess.Open(DifficultyTracePath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                return;
            }

            if (exists)
            {
                file.SeekEnd();
            }
            else
            {
                file.StoreLine("utcMs,miner,index,configuredPower,realizedPower,difficulty,anchor,solveSec,solveRatio");
            }

            file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F1},{8:F4}",
                block.Timestamp, miner.NodeId, block.Index, configuredPower, realizedPower,
                block.Difficulty, anchor, solveSec, solveRatio));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[DifficultyTrace] failed: {e.Message}");
        }
    }

    private static void ScheduleBotTransactionsAfterBlock(Block block)
    {
        // Donations target only non-miners currently in their open auction window (recruitable),
        // at this block's time. Empty until the first non-miner is introduced (after live mining begins).
        List<string> recipientPool = InAuctionNonMinerAddresses(block.Timestamp);

        if (recipientPool.Count == 0) return;

        // Canonical chain (the player's synced view) — used to measure each bot's mining warmup.
        List<Block> chain = SharedNodesById[PlayerNodeId].Blockchain.Chain;

        foreach (BotWalletRecord record in BotWalletRegistry.MinerBots)
        {
            if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? node)) continue;

            // Warmup measured PER BOT from the block it first mined — so circulation starts a few
            // blocks after a miner bot actually begins mining (works for bots introduced gradually,
            // not an absolute chain index that the historical bootstrap would have already passed).
            int? firstMinedHeight = FirstBlockHeightMinedBy(record.NodeId, chain);
            if (firstMinedHeight is null) continue; // hasn't mined yet → nothing to circulate
            if (block.Index - firstMinedHeight.Value < CirculationWarmupBlocks) continue;

            decimal spendable = node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
            if (spendable < MinBotSpendableBalanceBtc) continue;
            if (Random.Shared.NextDouble() >= BotSendProbabilityPerBlock) continue;

            decimal fraction = MinSendFractionDecimal
                + (decimal)Random.Shared.NextDouble() * (MaxSendFractionDecimal - MinSendFractionDecimal);
            decimal sendAmount = Math.Round(spendable * fraction, 8);
            if (sendAmount <= 0m) continue;

            // Step 4b.2: attach a sender-chosen fee (collected into the miner's coinbase).
            decimal fee = Math.Round(MinBotFeeBtc + (decimal)Random.Shared.NextDouble() * (MaxBotFeeBtc - MinBotFeeBtc), 8);
            if (sendAmount + fee > spendable) continue; // must cover amount + fee

            string recipientAddress = recipientPool[Random.Shared.Next(recipientPool.Count)];
            if (recipientAddress == node.WalletAddress) continue; // never send to self (recipients are non-miners, but be safe)

            // Step 8 — UTXO spend (coin-select the bot's base-address UTXOs + change back to its base).
            BuildAndBroadcastUtxoSpend(node, recipientAddress, sendAmount, fee, null);
        }
    }

    // Index of the first block in the chain mined by nodeId, or null if it has never mined.
    private static int? FirstBlockHeightMinedBy(string nodeId, List<Block> chain)
    {
        foreach (Block b in chain)
        {
            if (string.Equals(b.MinedByNodeId, nodeId, StringComparison.Ordinal))
            {
                return b.Index;
            }
        }

        return null;
    }

    private static decimal GetBlockRewardForNextCandidate(NodeAgent miner)
    {
        int nextBlockIndex = miner.Blockchain.GetLastBlock().Index + 1;
        int completedHalvings = Math.Max(0, (nextBlockIndex - 1) / HalvingIntervalBlocks);
        // Cap derived from HalvingIntervalBlocks: 34 × 2100 = 71,400 blocks ≈ in-game year 2141.
        if (completedHalvings >= 34)
        {
            return 0m;
        }

        decimal reward = GenesisRewardBtc;
        for (int i = 0; i < completedHalvings; i++)
        {
            reward /= 2m;
        }

        return reward;
    }

    // NOTE: chain "consensus" (longest-chain reconciliation) was removed in T2 — it was a no-op in this
    // single-shared-chain design (every node already holds the same canonical chain via BroadcastBlock). It
    // becomes meaningful only with divergent chains (forks / orphan blocks / P2P propagation), a feature
    // deliberately deferred to **after Basic Mode** — see PRIVATE_ROADMAP "Post-Basic Mode — Divergent
    // Chains / Fork Simulation" and AIHelperFiles/IMPLEMENTATION_ROADMAP.md.

    public IReadOnlyList<string> GetNodeIds()
    {
        EnsureInitialized();
        return SharedNodesById.Keys.OrderBy(x => x).ToList();
    }

    // Nodes that legitimately participate in DiceGame betting: the player and the miner bots.
    // Excludes the casino, founders (satoshi/hal), and non-miner holder bots — none of those bet.
    // Founder mining is driven by the weighted lottery / historical bootstrap, never by DiceGame.
    public IReadOnlyList<string> GetBettableNodeIds()
    {
        EnsureInitialized();
        var ids = new List<string> { PlayerNodeId };
        foreach (BotWalletRecord miner in BotWalletRegistry.MinerBots)
        {
            if (SharedNodesById.ContainsKey(miner.NodeId))
            {
                ids.Add(miner.NodeId);
            }
        }

        return ids;
    }

    public int GetPlayerChainLength()
    {
        EnsureInitialized();
        return SharedNodesById[PlayerNodeId].Blockchain.Chain.Count;
    }

    public int GetPlayerPendingTransactionCount()
    {
        EnsureInitialized();
        return SharedNodesById[PlayerNodeId].Blockchain.PendingTransactions.Count;
    }

    public Block GetPlayerLatestBlock()
    {
        EnsureInitialized();
        return SharedNodesById[PlayerNodeId].Blockchain.GetLastBlock();
    }

    // Average in-game seconds between the last `window` player blocks (the signal the difficulty regulator
    // targets). 0 if there aren't enough blocks yet. (D.3 — Block Explorer difficulty readout.)
    public double GetPlayerRecentAverageBlockSeconds(int window)
    {
        EnsureInitialized();
        List<Block> chain = SharedNodesById[PlayerNodeId].Blockchain.Chain;
        if (chain.Count < 2) return 0d;

        int deltas = Math.Min(window, chain.Count - 1);
        double sum = 0d;
        for (int k = 0; k < deltas; k++)
        {
            sum += (chain[chain.Count - 1 - k].Timestamp - chain[chain.Count - 2 - k].Timestamp) / 1000d;
        }
        return sum / deltas;
    }

    // Difficulty of the block a node is CURRENTLY mining: the LOCKED candidate difficulty (fixed until that
    // block is found, so a power change only shows from the next block) or, when idle, the prospective
    // next-block difficulty at the live network power. In this model Difficulty == the expected nonce attempts
    // for that block. Shared by the Block Explorer AND the DiceGame mining readout so both track the same live
    // value — NOT the last already-mined block's stamped difficulty (which is what made DiceGame look stale).
    private static double GetNextOrCandidateDifficulty(NodeAgent node)
    {
        double candidate = node.GetCurrentCandidateDifficulty();
        return candidate > 0d ? candidate : node.Blockchain.GetNextBlockDifficulty(_activeMiningPower);
    }

    // Difficulty the block CURRENTLY being mined will use, for the Block Explorer's main "mining" readout —
    // distinct from any already-mined block's value.
    public double GetPlayerNextBlockDifficulty()
    {
        EnsureInitialized();
        return GetNextOrCandidateDifficulty(SharedNodesById[PlayerNodeId]);
    }

    // The difficulty stamped on the player block `blocksAgo` back from the tip (clamped to the genesis end),
    // for showing a rising/falling difficulty trend. (D.3.)
    public double GetPlayerDifficultyBlocksAgo(int blocksAgo)
    {
        EnsureInitialized();
        List<Block> chain = SharedNodesById[PlayerNodeId].Blockchain.Chain;
        if (chain.Count == 0) return 0d;
        int index = Math.Max(0, chain.Count - 1 - Math.Max(0, blocksAgo));
        return chain[index].Difficulty;
    }

    public IReadOnlyList<string> GetNodeStatusLines()
    {
        EnsureInitialized();
        Dictionary<string, int> mined = MinedBlockCountsByNode();
        return SharedNetwork.Nodes
            .OrderBy(n => n.NodeId)
            .Select(n => $"{n.NodeId} | mined: {(mined.TryGetValue(n.NodeId, out int c) ? c : 0)} | block: {n.Blockchain.Chain.Count} | pending: {n.Blockchain.PendingTransactions.Count} | balance: {AggregateSpendable(n):F8}")
            .ToList();
    }

    // Blocks each node has mined on the canonical (player) chain, keyed by node id. Genesis (index 0,
    // unattributed) is excluded. Lets the Block Explorer show who mined how much — e.g. Satoshi's ~10%
    // founder share accruing during play (Step 7.2).
    public IReadOnlyDictionary<string, int> GetMinedBlockCountsByNode()
    {
        EnsureInitialized();
        return MinedBlockCountsByNode();
    }

    private static Dictionary<string, int> MinedBlockCountsByNode()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
        {
            return counts;
        }

        foreach (Block b in player.Blockchain.Chain)
        {
            if (b.Index == 0 || string.IsNullOrEmpty(b.MinedByNodeId))
            {
                continue;
            }

            counts.TryGetValue(b.MinedByNodeId, out int c);
            counts[b.MinedByNodeId] = c + 1;
        }

        return counts;
    }

    public IReadOnlyList<string> GetNodeAddressLines()
    {
        EnsureInitialized();
        return SharedNodesById.Values
            .OrderBy(n => n.NodeId)
            .Select(n =>
            {
                if (n.ReceiveWallet == null || n.ReceiveWallet.OwnedAddresses.Count <= 1)
                    return $"{n.NodeId}: {n.WalletAddress}";
                // Step 8.4 — the player rotates only CHANGE addresses (coinbase stays on base), founders that
                // rotate spread their REWARDS across fresh addresses (Satoshi). Word it per the node's mode.
                string kind = n.RotateCoinbaseAddress ? "rewards" : "change";
                return $"{n.NodeId}: {n.WalletAddress}  (base/identity; {kind} spread across {n.ReceiveWallet.OwnedAddresses.Count} addresses)";
            })
            .ToList();
    }

    // Maps an address to a registered node id for display, or a shortened address if unknown.
    public string DescribeAddress(string address)
    {
        EnsureInitialized();
        foreach (NodeAgent node in SharedNodesById.Values)
        {
            if (node.WalletAddress == address)
            {
                return node.NodeId;
            }
        }

        return address.Length > 12 ? address[..12] + "…" : address;
    }

    // Referral auction ledger (starter, option-(b) gradual introduction). Fully DERIVED from the
    // canonical chain — no persisted state. Non-miner holder bots enter the auction one at a time
    // after live mining begins; each runs a 7-in-game-day donation window; the top donor at window
    // close wins the referral permanently. Coinbase txs excluded. "now" = latest block timestamp.
    public IReadOnlyList<NonMinerDonationSummary> GetNonMinerAuctionLedger()
    {
        EnsureInitialized();
        long nowMs = SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? p) && p.Blockchain.Chain.Count > 0
            ? p.Blockchain.Chain[^1].Timestamp
            : 0;
        return ComputeAuctionLedger(nowMs);
    }

    private static List<NonMinerDonationSummary> ComputeAuctionLedger(long nowMs)
    {
        List<BotWalletRecord> nonMiners = BotWalletRegistry.NonMinerBots.ToList();
        var donations = new Dictionary<string, List<(string donor, decimal amount, long ts)>>();
        foreach (BotWalletRecord b in nonMiners)
        {
            donations[b.Address] = new List<(string, decimal, long)>();
        }

        SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player);
        if (player is not null)
        {
            foreach (Block block in player.Blockchain.Chain)
            {
                foreach (Transaction tx in block.Transactions)
                {
                    if (tx.Sender == BlockchainService.CoinbaseSender) continue;
                    if (donations.TryGetValue(tx.Recipient, out List<(string, decimal, long)>? list))
                    {
                        list.Add((tx.Sender, tx.Amount, block.Timestamp));
                    }
                }
            }
        }

        long? firstLiveTs = FirstLiveBlockTimestamp(player);

        var result = new List<NonMinerDonationSummary>();
        for (int i = 0; i < nonMiners.Count; i++)
        {
            BotWalletRecord b = nonMiners[i];
            List<(string donor, decimal amount, long ts)> list = donations[b.Address];

            var summary = new NonMinerDonationSummary
            {
                NonMinerNodeId = b.NodeId,
                NonMinerAddress = b.Address,
                TotalReceived = list.Sum(d => d.amount),
                DonorCount = list.Select(d => d.donor).Distinct().Count()
            };

            (string addr, decimal total) leader = TopDonor(list, long.MaxValue);
            summary.LeadingDonorAddress = leader.addr;
            summary.LeadingDonorTotal = leader.total;

            if (firstLiveTs is null)
            {
                summary.Status = NonMinerAuctionStatus.NotIntroduced;
                result.Add(summary);
                continue;
            }

            summary.IntroUnixMs = firstLiveTs.Value + i * NonMinerIntroIntervalMs;
            summary.WindowCloseUnixMs = summary.IntroUnixMs + AuctionWindowMs;

            if (nowMs < summary.IntroUnixMs) summary.Status = NonMinerAuctionStatus.NotIntroduced;
            else if (nowMs < summary.WindowCloseUnixMs) summary.Status = NonMinerAuctionStatus.InAuction;
            else
            {
                summary.Status = NonMinerAuctionStatus.Resolved;
                summary.WinnerAddress = TopDonor(list, summary.WindowCloseUnixMs).addr; // donors confirmed by close
            }

            result.Add(summary);
        }

        return result;
    }

    private static (string addr, decimal total) TopDonor(List<(string donor, decimal amount, long ts)> list, long maxTsInclusive)
    {
        var totals = new Dictionary<string, decimal>();
        foreach ((string donor, decimal amount, long ts) in list)
        {
            if (ts > maxTsInclusive) continue;
            totals.TryGetValue(donor, out decimal cur);
            totals[donor] = cur + amount;
        }

        if (totals.Count == 0) return (string.Empty, 0m);
        KeyValuePair<string, decimal> top = totals.OrderByDescending(kv => kv.Value).First();
        return (top.Key, top.Value);
    }

    // Timestamp of the first block mined by a non-founder (live era ≈ 21 Mar player start); null if none yet.
    private static long? FirstLiveBlockTimestamp(NodeAgent? player)
    {
        if (player is null) return null;
        foreach (Block b in player.Blockchain.Chain)
        {
            if (b.Index > 1 && !string.IsNullOrEmpty(b.MinedByNodeId)
                && b.MinedByNodeId != SatoshiNodeId && b.MinedByNodeId != HalNodeId)
            {
                return b.Timestamp;
            }
        }

        return null;
    }

    // Addresses of non-miners currently in their open auction window (recruitable) at the given time.
    private static List<string> InAuctionNonMinerAddresses(long nowMs)
    {
        return ComputeAuctionLedger(nowMs)
            .Where(s => s.Status == NonMinerAuctionStatus.InAuction)
            .Select(s => s.NonMinerAddress)
            .ToList();
    }

    public Block? GetBlockByIndexForNode(string nodeId, int blockIndex)
    {
        EnsureInitialized();
        if (blockIndex <= 0 || !SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return null;
        }

        return node.Blockchain.Chain.FirstOrDefault(b => b.Index == blockIndex);
    }

    public string BuildTransactionDetails(string nodeId, string transactionId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return "Node not found.";
        }

        (Transaction? tx, Block? block) = node.Blockchain.GetTransaction(transactionId);
        if (tx is null || block is null)
        {
            Transaction? pending = node.Blockchain.GetPendingTransaction(transactionId);
            if (pending is null)
            {
                return "Transaction not found in confirmed or pending sets.";
            }

            return
                $"TxId: {pending.TransactionId}\n" +
                "Status: pending\n" +
                $"Amount: {pending.Amount:F8}\n" +
                $"Fee: {pending.Fee:F8}\n" +
                $"Sender: {pending.Sender}\n" +
                $"Recipient: {pending.Recipient}";
        }

        return
            $"TxId: {tx.TransactionId}\n" +
            "Status: confirmed\n" +
            $"Block: {block.Index}\n" +
            $"Amount: {tx.Amount:F8}\n" +
            $"Fee: {tx.Fee:F8}\n" +
            $"Sender: {tx.Sender}\n" +
            $"Recipient: {tx.Recipient}";
    }

    public string BuildAddressDetailsForNode(string nodeId, string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return "Node not found.";
        }

        AddressData addressData = node.Blockchain.GetAddressData(address);
        decimal spendable = node.Blockchain.GetAddressSpendableBalance(address);
        return
            $"Node: {node.NodeId}\n" +
            $"Address: {address}\n" +
            $"Confirmed balance: {addressData.AddressBalance:F8}\n" +
            $"Spendable balance: {spendable:F8}\n" +
            $"Confirmed transactions: {addressData.AddressTransactions.Count}";
    }

    public decimal GetNodeSpendableBalance(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return 0m;
        }

        return AggregateSpendable(node);
    }

    // Step 8.4 — a node's wallet as the collection of addresses it owns (base + any derived change/receive
    // addresses), each with its confirmed balance and a flag marking the base/identity address. Lets BTCWallet
    // show that "a wallet = a set of addresses/UTXOs" (OQ-2 educational core). The base is always first; for a
    // single-address node (no ReceiveWallet) the list is just the base. Ordered base-first, then by index.
    public IReadOnlyList<(string address, decimal confirmed, bool isBase)> GetNodeAddressBook(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node)
            || !SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            return [];

        var result = new List<(string, decimal, bool)>
        {
            (node.WalletAddress, player.Blockchain.GetAddressData(node.WalletAddress).AddressBalance, true)
        };
        if (node.ReceiveWallet != null)
            foreach (string addr in node.ReceiveWallet.OwnedAddresses)
                if (addr != node.WalletAddress)
                    result.Add((addr, player.Blockchain.GetAddressData(addr).AddressBalance, false));
        return result;
    }

    // Step 8.2 — the founder's SCRIPTED historical activity (the automatic, system-driven events: the Hearn
    // round-trip, the 10-BTC Satoshi→Hal tx, …), so the wallet can show these in a panel SEPARATE from the
    // main balance — they are not manual withdrawals the founder ordered. Lists each `hist_*`-salted tx that
    // involves one of the node's addresses (excluding internal self-change), with direction + counterparty +
    // pending/confirmed status. Drives the "Automatic Activity" panel in FoundersWallets.
    public IReadOnlyList<(string label, bool outgoing, decimal amount, string counterparty, bool confirmed)> GetNodeScriptedActivity(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node)
            || !SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            return [];

        HashSet<string> addresses = node.ReceiveWallet != null
            ? new HashSet<string>(node.ReceiveWallet.OwnedAddresses) { node.WalletAddress }
            : new HashSet<string> { node.WalletAddress };

        var result = new List<(string, bool, decimal, string, bool)>();

        void Consider(Transaction t, bool confirmed)
        {
            if (string.IsNullOrEmpty(t.Salt) || !t.Salt.StartsWith("hist_")) return;
            bool isSender = addresses.Contains(t.Sender);
            bool isRecipient = addresses.Contains(t.Recipient);
            if (isSender == isRecipient) return; // not involved, or an internal self-change → skip
            string counterparty = isSender ? t.Recipient : t.Sender;
            result.Add((ScriptedEventLabel(t.Salt), isSender, t.Amount, counterparty, confirmed));
        }

        foreach (Block block in player.Blockchain.Chain)
            foreach (Transaction t in block.Transactions)
                Consider(t, true);
        foreach (Transaction t in player.Blockchain.PendingTransactions)
            Consider(t, false);

        return result;
    }

    // "hist_E6_satoshi_hearn_3251" → "E6"; "..._change" → "E6 change".
    private static string ScriptedEventLabel(string salt)
    {
        string[] parts = salt.Split('_');
        string code = parts.Length > 1 ? parts[1] : salt;
        return salt.EndsWith("_change") ? code + " change" : code;
    }

    // Step 8.2 — a node's full spendable balance. A multi-address node (Satoshi) spreads its coinbases across
    // many derived addresses (address non-reuse), so its balance is the sum across the owned set plus the
    // base/identity address (which holds p2p receives like E4). Single-address nodes use the base only. The
    // unspendable genesis 50 is already excluded by GetAddressData (IsSpendable = false).
    private static decimal AggregateSpendable(NodeAgent node)
    {
        if (node.ReceiveWallet == null)
            return node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);

        var addresses = new HashSet<string>(node.ReceiveWallet.OwnedAddresses) { node.WalletAddress };
        decimal total = 0m;
        foreach (string address in addresses)
            total += node.Blockchain.GetAddressSpendableBalance(address);
        return total;
    }

    // Returns all confirmed transactions involving address (as sender or recipient),
    // ordered by block index descending. Scans the full player chain.
    public IReadOnlyList<(Transaction tx, int blockIndex)> GetAddressConfirmedTransactions(string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? node))
            return [];
        var result = new List<(Transaction tx, int blockIndex)>();
        foreach (Block block in node.Blockchain.Chain)
        {
            foreach (Transaction tx in block.Transactions)
            {
                if (tx.Sender == address || tx.Recipient == address)
                    result.Add((tx, block.Index));
            }
        }
        result.Sort((a, b) => b.blockIndex.CompareTo(a.blockIndex));
        return result;
    }

    // Creates a signed transaction from a registered node (by nodeId) to any gm1q... address.
    // Used by BotsBtcWallets where the recipient may not be a registered NodeAgent.
    public Transaction? CreateAndBroadcastTransactionToAddress(string fromNodeId, string recipientAddress, decimal amount, decimal fee = 0m)
    {
        EnsureInitialized();
        if (amount <= 0m || string.IsNullOrEmpty(recipientAddress))
            return null;
        if (!SharedNodesById.TryGetValue(fromNodeId, out NodeAgent? sender))
        {
            GD.PrintErr($"[NetworkRoot] Unknown sender nodeId: {fromNodeId}");
            return null;
        }
        if (sender.WalletAddress == recipientAddress)
            return null;

        // Step 8 (full UTXO model) — coin-select the sender's owned UTXOs (combining several when no single
        // one covers the amount — the player's multi-input case) and pay the recipient, returning change to a
        // fresh derived address (player/Satoshi) or the base address (bots/casino/passphrase). One shared path.
        // No disk write: a block is the only commit (see CreateAndBroadcastTransaction / PersistStateToDisk).
        return BuildAndBroadcastUtxoSpend(sender, recipientAddress, amount, fee, null);
    }

    // Derives a NodeAgent for a passphrase wallet on demand and registers it in SharedNetwork
    // for the session so it can sign and broadcast transactions. Syncs the player chain so UTXO
    // checks see existing confirmed balance. Returns the nodeId for CreateAndBroadcastTransactionToAddress.
    public string RegisterPassphraseWallet(string seedPhrase, string walletAddress)
    {
        EnsureInitialized();
        string nodeId = $"pass_{walletAddress[4..12]}";
        if (!SharedNodesById.ContainsKey(nodeId))
        {
            (string signPub, string signPriv) = CryptoUtils.DeriveSigningKeypair(seedPhrase);
            string secp256k1Pub = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase);
            var node = new NodeAgent(nodeId, walletAddress, signPub, signPriv, secp256k1Pub);
            if (SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
                node.Blockchain.TryReplaceChain(player.Blockchain.Chain, player.Blockchain.PendingTransactions);
            SharedNetwork.RegisterNode(node);
            SharedNodesById[nodeId] = node;
        }
        return nodeId;
    }

    // Returns confirmed balance and total pending-outgoing for any gm1q... address,
    // queried against the player node's blockchain (the authoritative chain after consensus).
    public (decimal confirmedBalance, decimal pendingOutgoing) GetAddressBalanceDetails(string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? node))
            return (0m, 0m);
        AddressData data = node.Blockchain.GetAddressData(address);
        decimal pendingOut = node.Blockchain.PendingTransactions
            .Where(t => t.Sender == address)
            .Sum(t => t.Amount);
        return (data.AddressBalance, pendingOut);
    }

    // Phase 8.1 (Step 8) — every address that appears anywhere on the confirmed player chain (coinbase
    // recipient, tx recipient, or real tx sender), collected in a single pass so a DerivedAddressWallet
    // rescan can probe membership in O(1) (OQ-8.4) instead of scanning the chain once per derived address.
    public HashSet<string> CollectUsedAddressSet()
    {
        EnsureInitialized();
        return SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)
            ? BuildUsedAddressSet(player)
            : new HashSet<string>();
    }

    // Single-pass scan of every address appearing on a node's confirmed chain (coinbase recipient, tx
    // recipient, or real tx sender). Static + no EnsureInitialized so it is safe to call from inside
    // EnsureInitialized (RescanFounderReceiveWallets) without re-entrancy.
    private static HashSet<string> BuildUsedAddressSet(NodeAgent player)
    {
        var used = new HashSet<string>();
        foreach (Block block in player.Blockchain.Chain)
            foreach (Transaction tx in block.Transactions)
            {
                if (!string.IsNullOrEmpty(tx.Recipient))
                    used.Add(tx.Recipient);
                // The coinbase sentinel "00" is never a real address.
                if (!string.IsNullOrEmpty(tx.Sender) && tx.Sender != BlockchainService.CoinbaseSender)
                    used.Add(tx.Sender);
            }
        return used;
    }

    // Step 8.2/8.4 — position every derived-address wallet's frontier from the chain (Decision D3): the
    // rotating founders (Satoshi's coinbases) and the player (whose frontier advances on change outputs).
    // Called at init after the chain is loaded/normalized; in-session the frontier then advances incrementally
    // via NodeAgent.ReceiveWallet.MarkReceiveConsumed as each rotated receive (coinbase / change) is committed.
    private static void RescanFounderReceiveWallets()
    {
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            return;
        HashSet<string> used = BuildUsedAddressSet(player);
        foreach (NodeAgent node in SharedNodesById.Values)
            node.ReceiveWallet?.Rescan(used.Contains);
    }

    // Phase 8.1 (Step 8) — confirmed-balance aggregate across a derived-address set (a node's many
    // receive addresses). Sums each address's confirmed (mature) balance on the player chain; used by the
    // founder-economics aggregation in Phase 8.2 and the wallet total in Phase 8.4.
    public decimal GetWalletTotalConfirmed(IEnumerable<string> addresses)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            return 0m;

        decimal total = 0m;
        foreach (string address in addresses)
            total += player.Blockchain.GetAddressData(address).AddressBalance;
        return Scripts.Finance.Money.Normalize(total);
    }

    public NodeFinancialState GetOrCreateNodeFinancialState(string nodeId, decimal defaultPrincipalBalance, decimal defaultBankrollBalance)
    {
        EnsureInitialized();
        if (!SharedNodesById.ContainsKey(nodeId))
        {
            return new NodeFinancialState();
        }

        NodeAgent node = SharedNodesById[nodeId];
        if (node.FinancialState is null)
        {
            node.FinancialState = new NodeFinancialState
            {
                PrincipalBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, defaultPrincipalBalance)),
                BankrollBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, defaultBankrollBalance)),
                UpdatedAtUtc = DateTime.UtcNow
            };
            PersistStateToDisk();
        }

        return node.FinancialState.Clone();
    }

    public bool HasNodeFinancialState(string nodeId)
    {
        EnsureInitialized();
        return SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) && node.FinancialState is not null;
    }

    public bool HasAnyNodeFinancialState()
    {
        EnsureInitialized();
        return SharedNodesById.Values.Any(node => node.FinancialState is not null);
    }

    public void EnsureMissingNodeFinancialStates(NodeFinancialState template, bool persist = false)
    {
        EnsureInitialized();
        if (template is null)
        {
            return;
        }

        bool changed = false;
        foreach (NodeAgent node in SharedNodesById.Values)
        {
            if (node.FinancialState is not null)
            {
                continue;
            }

            node.FinancialState = template.CloneNormalized();
            node.FinancialState.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        if (changed && persist)
        {
            PersistStateToDisk();
        }
    }

    public void SetNodeFinancialState(string nodeId, NodeFinancialState state, bool persist = false)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) || state is null)
        {
            return;
        }

        node.FinancialState = state.CloneNormalized();
        node.FinancialState.UpdatedAtUtc = DateTime.UtcNow;
        if (persist)
        {
            PersistStateToDisk();
        }
    }

    public string BuildMiningStatusLine(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
        {
            return "Node not found.";
        }

        int nextBlock = node.Blockchain.GetLastBlock().Index + 1;
        int pending = node.Blockchain.PendingTransactions.Count;
        long nonce = node.GetCurrentCandidateNonce();
        // The difficulty of the block being mined NOW (live, power-aware) — matches the Block Explorer readout.
        double difficulty = GetNextOrCandidateDifficulty(node);
        decimal reward = GetBlockRewardForNextCandidate(node);

        string lastInfo = _lastMinedBlock is null
            ? "Last mined block: none"
            : $"Last mined block: #{_lastMinedBlock.Index} by {_lastMinedByNodeId}";

        return
            $"Miner: {nodeId}\n" +
            $"Next block target: #{nextBlock}\n" +
            $"Current nonce attempt: {nonce}\n" +
            $"Pending tx in candidate: {pending}\n" +
            $"Next reward: {reward:F8} BTC\n" +
            $"Mining difficulty: {difficulty:F2}  (~{difficulty:F0} attempts/block)\n" +
            $"Miner streak current/best: {_currentMinerStreak}/{_bestMinerStreak}\n" +
            $"{lastInfo}\n" +
            "Attempts per bet: 1";
    }

    public BlockchainMiningAnnouncement GetLatestMiningAnnouncement()
    {
        EnsureInitialized();
        if (_lastMinedBlock is null)
        {
            return BlockchainMiningAnnouncement.Empty;
        }

        return new BlockchainMiningAnnouncement
        {
            BlockIndex = _lastMinedBlock.Index,
            BlockHash = _lastMinedBlock.Hash,
            Nonce = _lastMinedBlock.Nonce,
            MinerNodeId = _lastMinedByNodeId,
            MinerAddress = _lastMinedBlock.MinedByAddress,
            CurrentMinerStreak = _currentMinerStreak,
            BestMinerStreak = _bestMinerStreak,
            WasPlayer = string.Equals(_lastMinedByNodeId, PlayerNodeId, StringComparison.Ordinal)
        };
    }

    // "Block = the only commit to disk" (ProjectDesignManual §24.8 / PRIVATE_ROADMAP T1): this is only ever
    // called at block-mining, baseline node creation, and startup. NOTHING between blocks persists — not the
    // chain, not the mempool, not financial state — so an app restart reverts the whole world (clock, balances
    // AND pending transactions) to the last mined block. A tx broadcast or consensus round only mutates the
    // in-memory state; it becomes durable when the next block is mined.
    private static void PersistStateToDisk()
    {
        EnsureDirectory(BlockchainDir);
        NodeAgent player = SharedNodesById[PlayerNodeId];

        BlockchainStateSnapshot snapshot = new()
        {
            PlayerChain = player.Blockchain.Chain,
            PlayerPendingTransactions = player.Blockchain.PendingTransactions,
            NodeFinancialStates = SharedNodesById
                .Where(pair => pair.Value.FinancialState is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value.FinancialState!.CloneNormalized()),
            NodeWallets = SharedNodesById.ToDictionary(
                pair => pair.Key,
                pair => new NodeWalletSnapshot
                {
                    Address = pair.Value.WalletAddress,
                    SigningPublicKeyBase64 = pair.Value.WalletPublicKey,
                    SigningPrivateKeyBase64 = pair.Value.WalletPrivateKey,
                    Secp256k1PublicKeyBase64 = pair.Value.WalletSecp256k1PublicKey
                }),
            LastMinedByNodeId = _lastMinedByNodeId,
            CurrentMinerStreak = _currentMinerStreak,
            BestMinerStreak = _bestMinerStreak
        };

        using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));

        WriteMonthlyChunks(player.Blockchain.Chain);
    }

    private static void WriteMonthlyChunks(List<Block> chain)
    {
        string absoluteDir = ProjectSettings.GlobalizePath(BlockchainDir);
        if (System.IO.Directory.Exists(absoluteDir))
        {
            foreach (string staleFile in System.IO.Directory.GetFiles(absoluteDir, "blocks-*.json"))
            {
                System.IO.File.Delete(staleFile);
            }
        }

        Dictionary<string, List<Block>> byMonth = chain
            .Where(b => b.Index > 0)
            .GroupBy(b => DateTimeOffset.FromUnixTimeMilliseconds(b.Timestamp).UtcDateTime.ToString("yyyy-MM"))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach ((string month, List<Block> blocks) in byMonth)
        {
            string path = $"{BlockchainDir}/blocks-{month}.json";
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(blocks, JsonOptions));
        }
    }

    // Step 8 (clean reset) — when WorldFormatVersion changes, the persisted world is incompatible (the old
    // account-model chain has no UTXO linkage). Delete the chain, the per-block checkpoint, the game clock,
    // and the SC balance state so the next steps re-bootstrap a pristine UTXO world from genesis. SC betting
    // history (cosmetic) is left untouched. Idempotent: writes the new version stamp so it runs once.
    private static void ResetWorldIfFormatChanged()
    {
        int storedVersion = 0;
        if (FileAccess.FileExists(WorldVersionPath))
        {
            using FileAccess vf = FileAccess.Open(WorldVersionPath, FileAccess.ModeFlags.Read);
            int.TryParse(vf.GetAsText().Trim(), out storedVersion);
        }
        if (storedVersion == WorldFormatVersion)
        {
            return;
        }

        GD.Print($"[NetworkRoot] World format {storedVersion} → {WorldFormatVersion}: resetting chain + clock + financial state for the UTXO model (clean reset).");

        DeleteIfExists(StatePath);
        DeleteIfExists("user://block_session_checkpoint.json");
        DeleteIfExists("user://calendar_state.json");
        DeleteIfExists("user://bankroll_state.json");
        DeleteIfExists("user://principal_balance_state.json");
        DeleteIfExists("user://bankroll_program_state.json");

        // The monthly block history chunks are likewise old-format — remove them so the explorer rebuilds.
        string blocksDirAbs = ProjectSettings.GlobalizePath(BlockchainDir);
        if (System.IO.Directory.Exists(blocksDirAbs))
            foreach (string staleFile in System.IO.Directory.GetFiles(blocksDirAbs, "blocks-*.json"))
                try { System.IO.File.Delete(staleFile); } catch { /* best-effort */ }

        using FileAccess stamp = FileAccess.Open(WorldVersionPath, FileAccess.ModeFlags.Write);
        stamp?.StoreString(WorldFormatVersion.ToString());
    }

    private static void DeleteIfExists(string userPath)
    {
        if (FileAccess.FileExists(userPath))
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(userPath));
    }

    private static BlockchainStateSnapshot? TryLoadSnapshot()
    {
        if (!FileAccess.FileExists(StatePath)) return null;
        using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
        string json = file.GetAsText();
        return JsonSerializer.Deserialize<BlockchainStateSnapshot>(json);
    }

    private static void ApplyStateFromSnapshot(BlockchainStateSnapshot? snapshot)
    {
        if (snapshot is null || snapshot.PlayerChain.Count == 0) return;

        foreach (NodeAgent node in SharedNodesById.Values)
            node.Blockchain.TryReplaceChain(snapshot.PlayerChain, snapshot.PlayerPendingTransactions);

        _lastMinedByNodeId = snapshot.LastMinedByNodeId;
        _currentMinerStreak = snapshot.CurrentMinerStreak;
        _bestMinerStreak = snapshot.BestMinerStreak;
        _lastMinedBlock = snapshot.PlayerChain.LastOrDefault();

        foreach ((string nodeId, NodeFinancialState state) in snapshot.NodeFinancialStates ?? new Dictionary<string, NodeFinancialState>())
        {
            if (SharedNodesById.TryGetValue(nodeId, out NodeAgent? node))
                node.FinancialState = state.CloneNormalized();
        }
    }

    private static void EnsureDirectory(string path)
    {
        if (DirAccess.DirExistsAbsolute(ProjectSettings.GlobalizePath(path)))
        {
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    private static void NormalizeGenesisAcrossNodes()
    {
        // Genesis coinbase is created in the BlockchainService ctor with the historical base58
        // placeholder (BlockchainService.SatoshiAddress). Once Satoshi's wallet exists we rewrite
        // the recipient to his derived gm1q… address so the genesis reward belongs to the founder
        // node. Genesis stays IsSpendable = false. ChainIsValid does not check the recipient, so
        // this rewrite does not invalidate the chain.
        string? satoshiAddress = WalletInitializationService.SatoshiWallet?.BaseAddress;

        foreach (NodeAgent node in SharedNodesById.Values)
        {
            if (node.Blockchain.Chain.Count <= 0)
            {
                continue;
            }

            Block genesis = node.Blockchain.Chain[0];
            genesis.Timestamp = BlockchainService.GenesisTimestampUnixMs;
            if (genesis.Transactions.Count == 0)
            {
                genesis.Transactions.Add(BlockchainService.CreateGenesisCoinbase());
            }

            if (satoshiAddress is not null)
            {
                foreach (Transaction tx in genesis.Transactions)
                {
                    // Rewrite the genesis coinbase output's recipient (base58 placeholder → Satoshi's derived
                    // gm1q… address). The output list is the source of truth in the Step 8 UTXO model.
                    if (tx.IsCoinbase && tx.Outputs.Count > 0 && tx.Outputs[0].Address == BlockchainService.SatoshiAddress)
                    {
                        tx.Outputs[0].Address = satoshiAddress;
                    }
                }
            }

            // Keep the genesis Merkle root consistent with its (possibly rewritten) coinbase.
            genesis.MerkleRoot = MerkleTree.ComputeRoot(genesis.Transactions);
        }
    }

    private static void EnsureSecondBlockBootstrapPendingTx()
    {
        NodeAgent player = SharedNodesById[PlayerNodeId];
        bool alreadyExists =
            player.Blockchain.ContainsTransactionId(BlockchainService.BootstrapSecondBlockTxId);
        if (alreadyExists || player.Blockchain.Chain.Count != 1)
        {
            return;
        }

        // Block-2 payout goes to Satoshi's derived gm1q… address (falls back to the historical
        // base58 placeholder only if the founder wallet is somehow unavailable).
        string satoshiAddress = WalletInitializationService.SatoshiWallet?.BaseAddress ?? BlockchainService.SatoshiAddress;

        Transaction bootstrapTx = new()
        {
            // An input-less, coinbase-style bootstrap payout (Step 8 UTXO model): one 50-BTC output to Satoshi.
            Inputs = new List<TxInput>(),
            Outputs = new List<TxOutput> { new() { Address = satoshiAddress, Amount = 50m } },
            TransactionId = BlockchainService.BootstrapSecondBlockTxId,
            Salt = "bootstrap-block2",
            InputDataText = "Bootstrap payout to Satoshi address in block 2",
            InputDataHex = BlockchainService.TextToHex("Bootstrap payout to Satoshi address in block 2"),
            IsSpendable = true
        };

        // System injection: the normal mempool admission path rejects input-less (coinbase-style) txs, so add
        // it directly to EVERY node's mempool — whichever node mines block 2 then includes it in the template.
        foreach (NodeAgent node in SharedNodesById.Values)
            if (!node.Blockchain.ContainsTransactionId(BlockchainService.BootstrapSecondBlockTxId))
                node.Blockchain.PendingTransactions.Add(bootstrapTx);
    }

    private sealed class BlockchainStateSnapshot
    {
        public List<Block> PlayerChain { get; set; } = new();
        public List<Transaction> PlayerPendingTransactions { get; set; } = new();
        public Dictionary<string, NodeFinancialState> NodeFinancialStates { get; set; } = new();
        public Dictionary<string, NodeWalletSnapshot> NodeWallets { get; set; } = new();
        public string LastMinedByNodeId { get; set; } = string.Empty;
        public int CurrentMinerStreak { get; set; }
        public int BestMinerStreak { get; set; }
    }

    private sealed class NodeWalletSnapshot
    {
        public string Address { get; set; } = string.Empty;
        public string SigningPublicKeyBase64 { get; set; } = string.Empty;
        public string SigningPrivateKeyBase64 { get; set; } = string.Empty;
        public string Secp256k1PublicKeyBase64 { get; set; } = string.Empty;

        public bool IsComplete() =>
            !string.IsNullOrWhiteSpace(Address) &&
            !string.IsNullOrWhiteSpace(SigningPublicKeyBase64) &&
            !string.IsNullOrWhiteSpace(SigningPrivateKeyBase64) &&
            !string.IsNullOrWhiteSpace(Secp256k1PublicKeyBase64);
    }
}

public sealed class NodeFinancialState
{
    public decimal PrincipalBalance { get; set; }
    public decimal BankrollBalance { get; set; }
    public decimal AutoRechargeAmount { get; set; } = BankrollProgramService.DefaultAutoRechargeAmount;
    public List<BankrollProgramService.TransferRecord> TransferRecords { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public NodeFinancialState Clone() => new()
    {
        PrincipalBalance = PrincipalBalance,
        BankrollBalance = BankrollBalance,
        AutoRechargeAmount = AutoRechargeAmount,
        TransferRecords = TransferRecords?.Select(CloneTransferRecord).ToList() ?? new List<BankrollProgramService.TransferRecord>(),
        UpdatedAtUtc = UpdatedAtUtc
    };

    public NodeFinancialState CloneNormalized()
    {
        NodeFinancialState clone = Clone();
        clone.PrincipalBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, clone.PrincipalBalance));
        clone.BankrollBalance = Scripts.Finance.Money.Normalize(Math.Max(0m, clone.BankrollBalance));
        clone.AutoRechargeAmount = clone.AutoRechargeAmount > 0m
            ? Scripts.Finance.Money.Normalize(clone.AutoRechargeAmount)
            : BankrollProgramService.DefaultAutoRechargeAmount;
        clone.UpdatedAtUtc = clone.UpdatedAtUtc == default ? DateTime.UtcNow : clone.UpdatedAtUtc;
        return clone;
    }

    private static BankrollProgramService.TransferRecord CloneTransferRecord(BankrollProgramService.TransferRecord record) => new()
    {
        UtcTimestamp = DateTime.SpecifyKind(record.UtcTimestamp, DateTimeKind.Utc),
        Amount = Scripts.Finance.Money.Normalize(Math.Max(0m, record.Amount)),
        Direction = record.Direction ?? string.Empty,
        Reason = record.Reason ?? string.Empty
    };
}

public sealed class BlockchainMiningAnnouncement
{
    public static BlockchainMiningAnnouncement Empty { get; } = new();
    public int BlockIndex { get; set; }
    public string BlockHash { get; set; } = string.Empty;
    public long Nonce { get; set; }
    public string MinerNodeId { get; set; } = string.Empty;
    public string MinerAddress { get; set; } = string.Empty;
    public int CurrentMinerStreak { get; set; }
    public int BestMinerStreak { get; set; }
    public bool WasPlayer { get; set; }
}

public enum NonMinerAuctionStatus { NotIntroduced, InAuction, Resolved }

// Donation-race + auction summary for one non-miner holder bot (referral-system starter).
public sealed class NonMinerDonationSummary
{
    public string NonMinerNodeId { get; set; } = string.Empty;
    public string NonMinerAddress { get; set; } = string.Empty;
    public decimal TotalReceived { get; set; }
    public int DonorCount { get; set; }
    public string LeadingDonorAddress { get; set; } = string.Empty;
    public decimal LeadingDonorTotal { get; set; }
    public NonMinerAuctionStatus Status { get; set; } = NonMinerAuctionStatus.NotIntroduced;
    public long IntroUnixMs { get; set; }
    public long WindowCloseUnixMs { get; set; }
    public string WinnerAddress { get; set; } = string.Empty; // set when Resolved ("" if no donors)
}
