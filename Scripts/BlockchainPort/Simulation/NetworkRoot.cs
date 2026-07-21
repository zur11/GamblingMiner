using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
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
    // v3 (ND.7, D-ND7.6): historical fee replay — fee SEMANTICS are world-defining (an existing chain
    // carries flat-0.1 fees from 2009-04-26 that the Market-Birth median/mean policy could never
    // produce, and the bootstrap regenerates differently), so the fee-era switch rides the same
    // clean-reset mechanism even though the serialized shape itself is unchanged.
    private const int WorldFormatVersion = 3;
    private const string WorldVersionPath = "user://world_format_version.txt";
    // Step 13 (TL.1) — stamps which calendar (TimelineConfig.Tag) the persisted world was built under.
    // A canon save loaded under the alt-timeline flag (or vice versa) is a corrupt hybrid (e.g. a 2009
    // chain tip paired with a 2010 fee-activation date), so a tag mismatch triggers the same clean reset
    // as a format-version bump. See ResetWorldIfIncompatible.
    private const string WorldTimelinePath = "user://world_timeline.stamp";
    // (CasinoTxFee = 0.1 retired at ND.7 — pool payouts now pay the day's replayed MEDIAN fee,
    // NetworkFeePolicy.MedianFeeAt, literally "the lowest available to the casino" — D-ND7.3/7.8.)

    // Blocks a miner bot must wait AFTER its own first mined block before it starts donating BTC —
    // measured per bot, so it works for bots introduced gradually (not an absolute chain index). Governs
    // TryCastSellFlow / TryNonMinerExchanges only — the ND.4b casino-bot cycle (below) has no warmup
    // gate; its own affordability filter already excludes any bot with nothing to donate.
    private const int CirculationWarmupBlocks = 5;
    private const decimal MinBotSpendableBalanceBtc = 1.0m;
    // (BotSendProbabilityPerBlock = 0.5 retired at Step 14 ND.3 — bot sends are now budgeted per block
    // by the historical fullness-parity target; see ScheduleBotTransactionsAfterBlock. The ND.4a
    // CasinoBotSellFlowMeanBlocks geometric draw is retired in turn at ND.4b/ND.4c below.)
    private static readonly HashSet<string> _casinoBotCycleTxIds = new();
    // ND.4b/ND.4c — competitive fast-cycle referral bidding (step14 plan §3.4, D-ND4b.1-13). This block
    // of constants governs ONLY the casino-bot auction cycle (TryCasinoBotDonation below) — it is
    // independent of TryCastSellFlow/TryNonMinerExchanges' historical fullness-parity budget.
    // D-ND4b.3: per-block donation-count draw, weighted — 15% zero, 70% one, 15% two (the remaining
    // percentage after the two named weights). A flat "always exactly 1" reads monotone/synthetic.
    private const int CasinoBotDonationWeightZeroPercent = 15;
    private const int CasinoBotDonationWeightOnePercent = 70;
    private const decimal MinBidBtc = 0.1m; // D-ND4b.5 — the fixed first-donation floor, BTC principal
    // ND.4d (2026-07-10) — the PLAYER's own minimum raise is a flat 1 satoshi above the leading bid, NOT
    // the 10-20% RaiseMin/RaiseMax formula (that stays exactly as-is for the casino-bots' own bidding).
    // The player can therefore always retake the lead as cheaply as possible — but a casino-bot's NEXT
    // raise still jumps 10-20% over whatever the player just bid, so a minimal player raise is an easy
    // target to overtake; the risk is left for the player to learn empirically, not blocked in code.
    private const decimal OneSatoshi = 0.00000001m;
    // Step 14 ND.5 (D-ND5.3) — a non-miner's tracked donation pool holds the 10 largest qualifying
    // donations (hoisted from ComputeTrackedDonationPool at ND.6a — the self-eviction guard reads it too).
    private const int MaxTrackedDonations = 10;
    // ND.6a (D-ND6.3/6.4, 2026-07-12) — the saturation ladder: exact-TIER re-bid probabilities. "Tier" =
    // a tracked slot's position in a pool's value order (tier 1 = largest donation … tier 10 = smallest
    // slot of a full pool); the word "rank" is reserved for the future casino ranking system and must
    // never be used in this feature. Consecutive Fibonacci percentages kept as literal named values per
    // D-ND6.4 — never derive them from a formula. Tiers 1-3 have no entry (the satisfied state,
    // D-ND6.7a). Tier 10 has no entry either (D-ND6.4 amendment, 2026-07-12): a bot whose BEST slot is
    // tier 10 necessarily holds the smallest slot of a full pool, which the self-eviction guard
    // (D-ND6.7b) excludes before any roll ever happens; restore a tier-10 entry (next Fibonacci step)
    // if that guard is ever relaxed.
    // This NORMAL table governs a pool that has reached EarlyRushSlotThreshold (7) occupied tracked slots
    // and whose rolling window is OUTSIDE its final-week urgency phase (ND.6e below).
    private static readonly IReadOnlyDictionary<int, int> ReBidProbabilityPercentByTier = new Dictionary<int, int>
    {
        [4] = 5, [5] = 8, [6] = 13, [7] = 21, [8] = 34, [9] = 55,
    };
    // ND.6e (2026-07-15) — Option B, the URGENCY ladder (D-ND6.10's pre-approved "auctions too quiet/long"
    // lever). Calibration finding from the continuing 2011 playtest: the early rush fixed the young-pool
    // stall, but once pools matured to NORMAL mode the calm 5%/8%/13% shallow tiers throttled re-bids
    // again — with 3 player-led mature pools the trace tail (blocks ~1005-1119) was 79 roll-declined vs
    // 20 donated, 66 of the declines NORMAL tier-4/5/6 rolls, affordability never the constraint. Rather
    // than raising the whole NORMAL table, each tier shifts ONE Fibonacci level up only while the pool's
    // rolling window is inside its FINAL 7 in-game days — challenges cluster into an organic late-window
    // "sniping" phase, and an accepted raise (which resets the 20-day window, D-ND4b.1) drops the pool
    // back to the calm table. EARLY-RUSH pools ignore urgency (their table is steeper than this one at
    // every tier it has).
    private const long AuctionUrgencyWindowMs = 7L * 86_400_000L;
    private static readonly IReadOnlyDictionary<int, int> UrgentReBidProbabilityPercentByTier = new Dictionary<int, int>
    {
        [4] = 8, [5] = 13, [6] = 21, [7] = 34, [8] = 55, [9] = 89,
    };
    // ND.6e — shared urgency test (the roll + the AuctioningCompanyDetails mode label read the same
    // rule). windowCloseUnixMs == 0 = no leading bid yet ⇒ never urgent (first bids are deterministic).
    public static bool IsAuctionInUrgencyWindow(long windowCloseUnixMs, long nowMs)
        => windowCloseUnixMs != 0 && windowCloseUnixMs - nowMs <= AuctionUrgencyWindowMs;
    // ND.6d (2026-07-14) — the EARLY PROBABILITY RUSH table. Calibration finding from the 2011 playtest:
    // once the player's cheap +1-satoshi retakes push a bot's best slot up to tier 4-5, the normal 5%/8%
    // roll left bots declining ~95% of the time and the player winning every referral uncontested (the
    // trace showed pure roll-declined at tier 4/5, spendable ~1000 BTC — affordability was never the
    // constraint). While a pool holds FEWER than EarlyRushSlotThreshold (7) occupied slots the shallow
    // tiers use this much steeper curve so casino-bots contest young pools hard; at 7 slots the pool
    // reverts to the NORMAL table above. A pool in early-rush can hold AT MOST 6 slots (a 7th slot IS the
    // mode switch), so a best-slot roll can only ever land on tier 4/5/6 here — tiers 7+ need no entry.
    private const int EarlyRushSlotThreshold = 7;
    private static readonly IReadOnlyDictionary<int, int> EarlyRushReBidProbabilityPercentByTier = new Dictionary<int, int>
    {
        [4] = 34, [5] = 55, [6] = 89,
    };
    // D-ND6.7a — a bot holding any top-3 tracked slot in a pool is "satisfied" there: never a re-bid target.
    private const int SatisfiedTopTierCount = 3;

    // ND.6d — the single source of truth for a slot's re-bid probability, shared by the roll in
    // TryBuildCasinoBotBid and the AuctioningCompanyDetails UI label (via ReBidProbabilityLabel below).
    // occupiedSlots (the pool's current tracked-slot count) selects early-rush (<7) vs normal (≥7);
    // urgent (ND.6e — final 7 window days) shifts a NORMAL pool one Fibonacci level up. 0 for any tier
    // with no entry (tiers 1-3 satisfied, tier 10, or an out-of-range tier).
    private static int ReBidProbabilityPercentFor(int tier, int occupiedSlots, bool urgent)
    {
        IReadOnlyDictionary<int, int> table = occupiedSlots < EarlyRushSlotThreshold
            ? EarlyRushReBidProbabilityPercentByTier
            : (urgent ? UrgentReBidProbabilityPercentByTier : ReBidProbabilityPercentByTier);
        return table.TryGetValue(tier, out int pct) ? pct : 0;
    }

    // ND.6d — the display string shown next to each tracked-pool slot in AuctioningCompanyDetails.
    // "satisfied" for the top-3 (secure by the D-ND6.7a rule, never a re-bid), "NN%" for a ladder tier,
    // "0%" for the self-eviction-guarded tier 10 of a full pool, and "" where a percentage is meaningless.
    public static string ReBidProbabilityLabel(int tier, int occupiedSlots, bool urgent)
    {
        if (tier <= SatisfiedTopTierCount) return "satisfied";
        if (occupiedSlots >= MaxTrackedDonations && tier == MaxTrackedDonations) return "0%";
        int pct = ReBidProbabilityPercentFor(tier, occupiedSlots, urgent);
        return pct > 0 ? pct + "%" : string.Empty; // integer percent — culture-invariant by construction
    }
    // D-ND6.8 — a bid's ENTIRE outgoing amount (required principal + D-ND4b.11 additive tail + network
    // fee) may never commit more than this fraction of the bot's SPENDABLE balance (mature/confirmed —
    // GetAddressSpendableBalance). Replaces the plain `spendable ≥ required + fee` affordability check.
    private const decimal MaxBidBalanceFraction = 0.5m;
    private const decimal MinSendFractionDecimal = 0.10m;
    private const decimal MaxSendFractionDecimal = 0.40m;
    // (Step 4b.2's randomized MinBotFeeBtc/MaxBotFeeBtc 0.1–1.0 band retired at ND.7 — the cast
    // sell-flow now pays the day's replayed MEAN fee (they ARE the network's average activity) and
    // every other automated participant the day's MEDIAN — D-ND7.3/7.8.)
    // Referral auction — Step 14 EB.2 (D-EB.4/5/6/7), pool retuned at round 3 (D-EB.8, 2026-07-09);
    // window shortened + bidding mechanics fully reworked into an ascending auction at ND.4b (D-ND4b.1,
    // 2026-07-10). Non-miners are introduced along the historical active-address curve from Market Birth
    // (schedule pushed by BtcNetworkDataService at load — SetNonMinerIntroSchedule, pool size 40). Each
    // bot's window is 20 in-game DAYS (D-ND4b.1 — down from the round-3 100-day value) and RESETS on
    // every accepted raise (D-ND4b.8) rather than counting once from the first-ever bid — see
    // ComputeAuctionLedger. Only nodes with a real casino relationship (bet-driven mining) qualify to
    // bid — the player AND the classic casino-miner-bots bot_1..4 (D-EB.7).
    private const long AuctionWindowMs = 20L * 86_400_000L;
    private static long[] _nonMinerIntroScheduleMs = [];
    // ND.4b (D-ND4b.10) — live/current SC valuation for the BlockExplorer Enroll Mode display and the
    // ND.5 Tracked Donation Pool rows (both always priced as of NOW, never a frozen historical day). A
    // plain autoload reference (not a throwaway instance, unlike EB.1's bootstrap accessors) so the
    // auction ledger reuses the SAME loaded CSV/Market-Birth date the rest of live play already reads.
    private static BtcMarketDataService? _marketData;
    // ND.8b.6 — see _Ready: the provisional casino SC-provisioning path + the player's SC dividend claims.
    private static CasinoScBalanceService? _casinoSc;
    private static PrincipalBalanceService? _principalBalance;
    // ND.8b.1 — non_miner_{i+1} (BotWalletRegistry's fixed creation order) <-> CompanyRoster
    // .Auctionable[i]'s founding record, once (and only once) that company's auction resolves.
    // Keyed by NonMinerNodeId (stable across the whole game, unlike the address — non-miners are
    // single-address today anyway). Persisted inside BlockchainStateSnapshot (PersistStateToDisk /
    // ApplyStateFromSnapshot below) — chain-adjacent state, so it rides the SAME "a block is the only
    // commit to disk" rule + world-reset delete-list entry (StatePath) the rest of that snapshot uses;
    // no separate checkpoint/pre-genesis-reset path is needed (a founding can only ever happen well
    // post-genesis — Market Birth alone is ~1.5 in-game years after player start).
    private static readonly Dictionary<string, CompanyFounding> _companyFoundings = new();
    // ND.8b.3 — per-company governance state (reserve mix, market category, open vote, dividend cycle,
    // claimables), keyed by NonMinerNodeId like _companyFoundings and persisted beside it in
    // BlockchainStateSnapshot (same "a block is the only commit" inheritance — governance can only exist
    // for founded companies, i.e. well post-genesis).
    private static readonly Dictionary<string, CompanyGovernanceState> _companyGovernance = new();
    // ND.8b.3 (D-ND8.13/D-ND8.26) — the casino-miner-bots' governance identities (one Currency-Band
    // preference + one market-category preference each), re-rolled per world; drawn lazily on the first
    // vote open (always inside block processing, so the draw lands in that block's snapshot write).
    private static readonly Dictionary<string, BotGovernancePreference> _botGovernancePreferences = new();

    public static void SetNonMinerIntroSchedule(long[] introUnixMs) =>
        _nonMinerIntroScheduleMs = introUnixMs ?? [];

    public override void _Ready()
    {
        _marketData = GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService");
        // ND.8b.6 (D-ND8.24/D-ND8.34) — the provisional SC-provisioning path: company BTC→SC conversions
        // draw SC from the casino's Main Balance (auto-loan when short), and the player's SC dividend
        // claims land on the Main Balance. Plain autoload references, the _marketData pattern.
        _casinoSc = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
        _principalBalance = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        // Step 8 / Step 13 (TL.1) — if the on-disk world predates the UTXO model OR was built under the
        // other timeline (canon vs. the DEV alt-timeline simulacrum), wipe the incompatible chain/clock/
        // financial state so this launch re-bootstraps a fresh, consistent world. Must run before TryLoadSnapshot.
        // Normally a no-op by now: WorldGuardService (autoload #1) already ran the guard BEFORE any other
        // autoload could load state files into memory (the TL.3 ordering fix) — kept here as a safety net.
        RunWorldCompatibilityGuard();

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

        // Step 14 (ND.2) — scheduler-spawned cast miners (registry-backed identities): re-register every
        // launch so their mined BTC stays visible/spendable. Their POWER comes from
        // NetworkPopulationScheduler each block, not hardware credits — no betting runners.
        foreach (BotWalletRecord castMiner in BotWalletRegistry.CastMiners)
        {
            if (castMiner.HasFullWallet)
                SharedNetwork.RegisterNode(CreateAndRegisterNode(castMiner.NodeId, savedState));
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
        // Step 8 (casino/Hal/Hearn extension) — every other founder ALSO gets a derived wallet, but for
        // CHANGE-only rotation like the player (RotateCoinbaseAddress = false): coinbase/receives stay on the
        // single base address (coinbase spread stays Satoshi-only), and they become multi-address only when
        // they SEND (change → fresh address). Hal mines + receives E4; Mike Hearn makes one outgoing tx (E6b
        // Hearn → Satoshi 32.51, an exact-match send → no change, so rotation is inert today but kept for
        // consistency/future-proofing). The frontier is positioned from the chain by RescanFounderReceiveWallets().
        node.ReceiveWallet = new DerivedAddressWallet(seed);
        if (founder.FounderId != "satoshi")
        {
            node.RotateCoinbaseAddress = false;
        }

        SharedNetwork.RegisterNode(node);
        SharedNodesById[founder.FounderId] = node;
    }

    // memo (Step 13 / SW.3): a display-only on-chain label (Transaction.InputDataText — NOT part of the
    // content-hash txid or the sighash, so it never affects validation). The swap desk tags its txs
    // "swap:…" so wallet history panels can color them apart from ordinary sends / pool payouts.
    public Transaction? CreateAndBroadcastTransaction(string fromNodeId, string recipientNodeId, decimal amount, decimal fee = 0m, string? memo = null)
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
        return BuildAndBroadcastUtxoSpend(sender, recipient.WalletAddress, amount, fee, null, memo);
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

    // Timestamp of the player chain's tip block. Before any real (post-bootstrap) block is mined, this IS
    // the last historical-bootstrap block — used by BlockSessionCheckpointService to re-derive the "player
    // start" instant (tip + 1s) on every pre-genesis restart, without needing to persist it separately.
    public static long GetPlayerLatestBlockTimestampMsStatic() =>
        SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)
            ? player.Blockchain.GetLastBlock().Timestamp
            : BlockchainService.GenesisTimestampUnixMs;

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
    private static Transaction? BuildAndBroadcastUtxoSpend(NodeAgent sender, string recipientAddress, decimal amount, decimal fee, string? deterministicSalt, string? memo = null)
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
        if (!string.IsNullOrEmpty(memo))
        {
            tx.InputDataText = memo; // display-only label; excluded from the txid/sighash so safe post-signing
        }
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
                decimal serviceFee = NetworkFeePolicy.MedianFeeAt(block.Timestamp); // 0 pre-birth (ND.7)
                decimal net = Scripts.Finance.Money.Normalize(share - serviceFee);
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
        TryDistributePendingCasinoRewards(block.Timestamp);
    }

    // Sends queued casino-pool payouts whose backing coinbase has matured (CoinbaseMaturity). Each
    // event is distributed as ONE multi-output tx covering all recipients atomically — so a failed
    // coin selection (insufficient confirmed UTXOs) never produces a partial distribution that would
    // double-pay some recipients on the next retry. Called after every mined block.
    private static void TryDistributePendingCasinoRewards(long blockTimestampMs)
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

            if (DistributePoolEventAsSingleTx(casino, evt, blockTimestampMs))
            {
                CasinoPoolRepository.MarkDistributed(evt.BlockIndex);
            }
        }
    }

    // One atomic multi-output tx covers all payout recipients for a single pool event. The root
    // cause of the "some participants not paid" bug was 5 separate SendFromCasino calls: the 1st
    // spend consumed the only large confirmed UTXO (e.g. the fresh coinbase), and sends 2–5 found
    // nothing spendable (change from send 1 is still pending, not confirmed). A single coin
    // selection that accumulates enough UTXOs for the TOTAL need resolves this: all recipients
    // land in one tx whose change is one pending output instead of five. Returns true on success.
    private static bool DistributePoolEventAsSingleTx(NodeAgent casino, CasinoPoolRewardEvent evt, long blockTimestampMs)
    {
        decimal perFee    = NetworkFeePolicy.MedianFeeAt(blockTimestampMs); // 0 pre-birth (ND.7)
        decimal totalAmt  = evt.Payouts.Sum(p => p.NetAmount);
        decimal totalFee  = perFee * evt.Payouts.Count;
        decimal need      = totalAmt + totalFee;

        // Coin-select across ALL casino addresses (base + all registered change addresses).
        HashSet<string> owned = casino.ReceiveWallet != null
            ? new HashSet<string>(casino.ReceiveWallet.OwnedAddresses) { casino.WalletAddress }
            : new HashSet<string> { casino.WalletAddress };

        IReadOnlyList<(OutPoint outpoint, string address, decimal amount)> available =
            casino.Blockchain.GetSpendableUtxos(owned);
        List<(OutPoint outpoint, string address, decimal amount)>? chosen = SelectUtxos(available, need);
        if (chosen is null) return false; // not enough matured funds yet → retry next block

        var inputs = new List<(OutPoint, string, string, string, string)>(chosen.Count);
        decimal gathered = 0m;
        foreach ((OutPoint outpoint, string address, decimal value) in chosen)
        {
            if (!TryResolveInputKeys(casino, address, out var keys)) return false;
            inputs.Add((outpoint, address, keys.pub, keys.priv, keys.secp));
            gathered += value;
        }

        // One output per recipient; any invalid payout aborts the whole tx.
        var outputs = new List<TxOutput>(evt.Payouts.Count + 1);
        foreach (CasinoPoolPendingPayout payout in evt.Payouts)
        {
            if (string.IsNullOrEmpty(payout.RecipientAddress) || payout.NetAmount <= 0m ||
                payout.RecipientAddress == casino.WalletAddress)
                return false;
            outputs.Add(new TxOutput { Address = payout.RecipientAddress, Amount = payout.NetAmount });
        }

        decimal change   = gathered - need;
        bool    hasChange = change > 0m;
        if (hasChange)
        {
            string changeAddr = casino.ReceiveWallet?.NextReceiveAddress() ?? casino.WalletAddress;
            outputs.Add(new TxOutput { Address = changeAddr, Amount = change });
        }

        Transaction tx = casino.BuildSignedSpend(inputs, outputs, totalFee, null);
        if (!casino.Blockchain.AddTransactionToPendingTransactions(tx)) return false;
        SharedNetwork.BroadcastTransaction(casino.NodeId, tx);
        if (hasChange) casino.ReceiveWallet?.MarkReceiveConsumed();
        return true;
    }

    private static string GetNodeAddress(string nodeId) =>
        SharedNodesById.TryGetValue(nodeId, out NodeAgent? node) ? node.WalletAddress : string.Empty;

    // Read-only view of the casino-pool reward ledger (for the BTCPoolsAndHardwareShop stats panel).
    public List<CasinoPoolRewardEvent> GetCasinoPoolHistory()
    {
        EnsureInitialized();
        return CasinoPoolRepository.Current.RewardHistory.ToList();
    }

    // Step 13 (SW.1 hardening) — the casino wallet's pool-settlement picture, for the swap desk. A pool
    // block's coinbase is mostly the CONTRIBUTORS' money (the casino keeps only its fee share), and the
    // payout lifecycle holds the casino's own share hostage for 1–2 blocks: coinbase immature → payout tx
    // pending → fee-share change confirmed. Two figures let CasinoCoinSwapService be both honest and stable:
    //
    //   settling — the casino's OWN BTC that exists economically but is not yet a spendable UTXO:
    //     (a) fee share still inside an event's unspent on-chain backing coinbase (immature or mature):
    //         coinbase value − what the distribution will pay out;
    //     (b) any pending-tx output addressed to a casino-owned address (the payout tx's fee-share change
    //         in flight — and, later, any other incoming BTC awaiting its confirming block).
    //     A backed event that broadcasts its payout moves seamlessly from (a) to (b) at the same value.
    //
    //   unbackedObligation — payouts owed by events whose backing coinbase is GONE from the canonical
    //     chain (e.g. lost a consensus race after queueing). TryDistributePendingCasinoRewards will retry
    //     these every block, coin-selecting from the casino's accumulated fee income — a live liability
    //     against the wallet's spendable balance, so the desk must treat it as already spent.
    // Step 13 (SW.3) — is this tx still awaiting its confirming block? Queried against the player node's
    // mempool (the authoritative chain after consensus). False once mined (or dropped by a chain replace).
    public bool IsTransactionPending(string transactionId)
    {
        EnsureInitialized();
        return !string.IsNullOrEmpty(transactionId)
            && SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? node)
            && node.Blockchain.PendingTransactions.Any(t => t.TransactionId == transactionId);
    }

    // One diagnostic line per surprising pool event per session (the recompute runs per bet — must not spam).
    private static readonly HashSet<int> _poolSettlementDiagPrinted = new();

    public (decimal settling, decimal obligationVsSpendable) GetCasinoBtcSettlement()
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(CasinoNodeId, out NodeAgent? casino))
        {
            return (0m, 0m);
        }

        var chain = casino.Blockchain.Chain;
        if (chain.Count == 0)
        {
            return (0m, 0m);
        }

        int tipIndex = chain[^1].Index;
        decimal perFee = NetworkFeePolicy.MedianFeeAt(chain[^1].Timestamp); // 0 pre-birth (ND.7)

        // Identity test for "this is our pool block's coinbase": it PAYS a casino-owned address (casino
        // coinbases always pay the base address — RotateCoinbaseAddress = false). More robust than
        // MinedByNodeId, which is stamped post-hash and is not guaranteed to survive block replication.
        HashSet<string> owned = casino.ReceiveWallet != null
            ? new HashSet<string>(casino.ReceiveWallet.OwnedAddresses) { casino.WalletAddress }
            : new HashSet<string> { casino.WalletAddress };

        decimal settling = 0m;
        decimal obligation = 0m;
        foreach (CasinoPoolRewardEvent evt in CasinoPoolRepository.GetUndistributed())
        {
            if (evt.Payouts.Count == 0)
            {
                continue; // nothing owed — MarkDistributed'd on the next block pass anyway
            }

            // What the distribution tx will take from the wallet: Σ net payouts + per-payout network fees.
            decimal need = evt.Payouts.Sum(p => p.NetAmount) + perFee * evt.Payouts.Count;

            // Locate the event's backing coinbase on the canonical chain, and whether it is still unspent.
            // List position ≠ Block.Index in every world (playtest DIAG: tip Index 121 with chain.Count 121 —
            // this chain does not start at Index 0), so resolve by the chain's base-index OFFSET and verify.
            Transaction? coinbase = null;
            int pos = evt.BlockIndex - chain[0].Index;
            if (pos >= 0 && pos < chain.Count)
            {
                Block backing = chain[pos];
                if (backing.Index == evt.BlockIndex)
                {
                    Transaction? cb = backing.Transactions.FirstOrDefault(t => t.IsCoinbase);
                    if (cb != null && cb.Outputs.Count > 0
                        && (owned.Contains(cb.Outputs[0].Address) || backing.MinedByNodeId == CasinoNodeId))
                    {
                        coinbase = cb;
                    }
                }
            }
            bool backedUnspent = coinbase != null && casino.Blockchain.IsUnspentOutput(coinbase.TransactionId, 0);

            if (backedUnspent && tipIndex - evt.BlockIndex < BlockchainService.CoinbaseMaturity)
            {
                // Immature backing coinbase: excluded from spendable, so the casino's fee share inside it
                // is pure settling money.
                settling += Math.Max(0m, coinbase!.Outputs[0].Amount - need);
            }
            else if (backedUnspent)
            {
                // Mature-but-undistributed (retry window): the whole coinbase IS in spendable, but `need`
                // of it belongs to the contributors — count the debt so equity nets to the fee share only.
                obligation += need;
            }
            else
            {
                // No unspent backing coinbase on the canonical chain (orphaned / already consumed): the
                // distribution retry will raid the casino's spendable fee income for this — price it in.
                obligation += need;
                if (_poolSettlementDiagPrinted.Add(evt.BlockIndex))
                {
                    string blockInfo = pos >= 0 && pos < chain.Count
                        ? $"blockAtPos(Index={chain[pos].Index}, miner='{chain[pos].MinedByNodeId}', cbFound={chain[pos].Transactions.Any(t => t.IsCoinbase)})"
                        : "position OUT OF CHAIN BOUNDS";
                    GD.Print($"[SwapDesk][DIAG] pool event #{evt.BlockIndex} has no unspent backing coinbase — tip={tipIndex} chainCount={chain.Count} baseIndex={chain[0].Index} pos={pos} {blockInfo} need={need:F8} → counted as obligation");
                }
            }
        }

        // Incoming BTC in flight: pending-tx outputs addressed to a casino-owned address (the payout tx's
        // fee-share change rotating to a fresh derived address is in OwnedAddresses via MarkReceiveConsumed).
        foreach (Transaction pending in casino.Blockchain.PendingTransactions)
        {
            foreach (TxOutput output in pending.Outputs)
            {
                if (owned.Contains(output.Address))
                {
                    settling += output.Amount;
                }
            }
        }

        return (Scripts.Finance.Money.Normalize(settling), Scripts.Finance.Money.Normalize(obligation));
    }

    // Step 13 (SW.1) — fired for every LIVE accepted block (bootstrap bulk-mining excluded), after broadcast
    // and the post-block side effects. Confirmations change every node's spendable set, so event-driven
    // consumers (CasinoCoinSwapService availability) recompute here instead of polling per-frame (§1.1).
    public static event Action<Block>? BlockAccepted;

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
            TrySettleResolvedAuctions(block); // Step 14 (ND.5, D-ND5.10): settle any non-miner that just resolved this block
            TickCompanyGovernance(block); // Step 14 (ND.8b.3): votes + dividends, committed in this block's snapshot write below
            HistoricalEventScheduler.OnBlockMined(block); // Step 7.4: inject scripted player-era txs at their date
            PersistStateToDisk();
            // After every block (any miner), retry casino-pool payouts whose coinbase has now matured.
            TryDistributePendingCasinoRewards(block.Timestamp);
            BlockAccepted?.Invoke(block); // last — subscribers see the post-payout spendable state
        }
    }

    // Step 14 (ND.5a, D-ND5.10) — ComputeAuctionLedger is a PURE, side-effect-free function called freely
    // and repeatedly from UI refreshes; founding a company (minting its stock-token distribution) is a
    // REAL state-changing event that must fire exactly once per non-miner's resolution. Diffs the CURRENT
    // block's ledger against the PREVIOUS block's (recomputed on demand — nothing between blocks
    // persists, so no new state is needed for the diff itself) and founds only the non-miners whose
    // status just flipped InAuction → Resolved on THIS block.
    private static void TrySettleResolvedAuctions(Block block)
    {
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)) return;
        List<Block> chain = player.Blockchain.Chain;
        if (chain.Count == 0) return;

        List<NonMinerDonationSummary> currentLedger = ComputeAuctionLedger(block.Timestamp);

        long previousMs = chain.Count >= 2 ? chain[^2].Timestamp : long.MinValue;
        Dictionary<string, NonMinerAuctionStatus> previousStatusByAddress = ComputeAuctionLedger(previousMs)
            .ToDictionary(s => s.NonMinerAddress, s => s.Status);

        foreach (NonMinerDonationSummary summary in currentLedger)
        {
            if (summary.Status != NonMinerAuctionStatus.Resolved) continue;

            bool alreadyResolved = previousStatusByAddress.TryGetValue(summary.NonMinerAddress, out NonMinerAuctionStatus prevStatus)
                && prevStatus == NonMinerAuctionStatus.Resolved;
            if (alreadyResolved) continue; // founded on an earlier block already — never re-fire

            FoundCompany(block, player, summary);
        }
    }

    // ND.8b.2 (D-ND8.14/D-ND8.15) — supersedes ND.5's "pay every tracked donor back in SC, sweep BTC to
    // casino" settlement: auction close now FOUNDS the company. The company keeps its own on-chain BTC —
    // its balance IS its treasury from here on, no sweep — and its equity is minted instead, as
    // stock-tokens distributed to the tracked-pool bidders per §12.4.5's algorithm. No SC changes hands at
    // all here (BTC→SC provisioning is ND.8b.6's job); this step is pure token bookkeeping.
    private static void FoundCompany(Block block, NodeAgent player, NonMinerDonationSummary summary)
    {
        if (summary.TrackedDonations.Count == 0) return; // structurally unreachable — a resolved auction has ≥1 bid, so ≥1 tracked donation
        if (_companyFoundings.ContainsKey(summary.NonMinerNodeId)) return; // idempotent safety net (the caller already diff-guards this)

        (HashSet<string> playerAddresses, Dictionary<string, string> botNodeIdByAddress) = BuildAuctionBidderIdentity(player);

        // D-ND8.15 step 1 — rank the tracked pool descending by BTC principal (tier 1 = largest) and
        // aggregate each unique donor's LIVE total + every tier it occupies (a donor can hold more than
        // one tier — repeat raises that were never evicted from the top-10).
        List<TrackedDonation> rankedDesc = summary.TrackedDonations.OrderByDescending(d => d.AmountBtc).ToList();
        decimal poolTotal = rankedDesc.Sum(d => d.AmountBtc);
        if (poolTotal <= 0m) return; // structurally unreachable — a resolved auction's leading bid alone is > 0

        var perDonor = new Dictionary<string, (decimal liveBtc, List<int> tiers)>();
        for (int i = 0; i < rankedDesc.Count; i++)
        {
            int tier = i + 1;
            TrackedDonation d = rankedDesc[i];
            if (!perDonor.TryGetValue(d.DonorAddress, out (decimal liveBtc, List<int> tiers) entry))
            {
                entry = (0m, new List<int>());
            }
            entry.liveBtc += d.AmountBtc;
            entry.tiers.Add(tier);
            perDonor[d.DonorAddress] = entry;
        }

        // D-ND8.15 steps 2–4 — participation share × the 10,000-ST base pool, then the halving slot-bonus
        // ladder (5.2% at tier 1, halving each tier down to 10). A top-3-tier holder mints NST (dividend
        // rights + votes); everyone else mints PST (dividend rights only).
        var holdings = new List<CompanyShareHolding>();
        foreach (KeyValuePair<string, (decimal liveBtc, List<int> tiers)> kv in perDonor)
        {
            string donorAddress = kv.Key;
            decimal liveBtc = kv.Value.liveBtc;
            List<int> tiers = kv.Value.tiers;

            decimal participationShare = liveBtc / poolTotal;
            decimal baseTokens = Scripts.Finance.Money.Normalize(participationShare * StockBaseTokenPool);
            decimal bonusFraction = tiers.Sum(SlotBonusPercent) / 100m;
            decimal finalTokens = Scripts.Finance.Money.Normalize(baseTokens * (1m + bonusFraction));
            bool holdsTopThreeTier = tiers.Any(t => t <= 3);

            string holderId = playerAddresses.Contains(donorAddress)
                ? PlayerNodeId
                : botNodeIdByAddress.TryGetValue(donorAddress, out string? botNodeId)
                    ? botNodeId
                    : donorAddress; // should not happen — see BuildAuctionBidderIdentity's own comment

            holdings.Add(new CompanyShareHolding
            {
                HolderId = holderId,
                Nst = holdsTopThreeTier ? finalTokens : 0m,
                Pst = holdsTopThreeTier ? 0m : finalTokens
            });
        }

        var founding = new CompanyFounding
        {
            NonMinerNodeId = summary.NonMinerNodeId,
            NonMinerAddress = summary.NonMinerAddress,
            CompanyId = summary.CompanyId ?? string.Empty,
            FoundedAtUnixMs = block.Timestamp,
            Holdings = holdings
        };
        _companyFoundings[summary.NonMinerNodeId] = founding;

        AppendCompanyFoundingTrace(block, summary, founding);

        // ND.8b.3 (D-ND8.18) — the very first vote fires on auction close: initialize the company's
        // governance state and open the founding-day vote (initial reserve-mix direction).
        InitializeCompanyGovernance(block, summary, founding);
    }

    // D-ND8.15's fixed halving slot-bonus ladder (percentage points), tier 1..10 — max possible bonus
    // (all 10 tiers held by one bidder) ≈ +10.39%.
    private static readonly decimal[] SlotBonusPercentByTier =
    [
        5.2m, 2.6m, 1.3m, 0.65m, 0.325m, 0.1625m, 0.08125m, 0.040625m, 0.0203125m, 0.01015625m
    ];
    private const decimal StockBaseTokenPool = 10_000m;

    private static decimal SlotBonusPercent(int tier) =>
        tier >= 1 && tier <= SlotBonusPercentByTier.Length ? SlotBonusPercentByTier[tier - 1] : 0m;

    private const string CompanyFoundingTracePath = "user://logs/company_founding_trace.csv";

    // ND.8b.2 — one telemetry row per founded company (renamed from ND.5's auction_settlement_trace: there
    // is no more payout/sweep to log — this instead confirms FoundCompany fires exactly once per
    // resolution and records the minted totals for playtest verification).
    private static void AppendCompanyFoundingTrace(Block block, NonMinerDonationSummary summary, CompanyFounding founding)
    {
        try
        {
            if (!DirAccess.DirExistsAbsolute("user://logs"))
            {
                DirAccess.MakeDirRecursiveAbsolute("user://logs");
            }

            bool exists = FileAccess.FileExists(CompanyFoundingTracePath);
            using FileAccess file = exists
                ? FileAccess.Open(CompanyFoundingTracePath, FileAccess.ModeFlags.ReadWrite)
                : FileAccess.Open(CompanyFoundingTracePath, FileAccess.ModeFlags.Write);
            if (file == null) return;

            if (exists) file.SeekEnd();
            else file.StoreLine("blockTimestampMs,blockIndex,nonMinerNodeId,companyId,companyDisplayName,winnerAddress,trackedDonationCount,holderCount,totalNst,totalPst");

            decimal totalNst = founding.Holdings.Sum(h => h.Nst);
            decimal totalPst = founding.Holdings.Sum(h => h.Pst);
            file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8:F8},{9:F8}",
                block.Timestamp, block.Index, summary.NonMinerNodeId, summary.CompanyId, summary.CompanyDisplayName,
                summary.WinnerAddress, summary.TrackedDonations.Count, founding.Holdings.Count, totalNst, totalPst));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[CompanyFoundingTrace] failed: {e.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    // Step 14 (ND.8b.3, D-ND8.17/18/19b) — Company governance: the dividends & votes engine.
    //
    // BLOCK-DRIVEN: TickCompanyGovernance is called from HandleMinedBlock right after
    // TrySettleResolvedAuctions and BEFORE PersistStateToDisk, so every governance mutation commits in
    // the same block-snapshot write — "a block is the only commit to disk" holds by construction.
    // Between-block UI actions (the player's ballot, a manual dividend claim) mutate memory + the
    // mempool only and revert together on an app restart, like every other between-block action.
    // Timing granularity is the block (~0.68 in-game days at target pace): a vote "closes" on the first
    // block whose timestamp crosses its deadline — comfortably inside D-ND8.18's one-day scale.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private const string CompanyVoteKindFounding = "founding";   // D-ND8.18 — fires on auction close
    private const string CompanyVoteKindQuarterly = "quarterly"; // dividend amount + reserve/nature direction
    private const string CompanyVoteKindSpecial = "special";     // >30%-inflow reserve vote

    // D-ND8.18 — every vote runs one in-game day; its result applies from the next day (the moment the
    // window elapses — the first block past OpenedAt + 1 day) and holds until the next quarter.
    private const long VoteDurationMs = 86_400_000L;
    private const long GameDayMs = 86_400_000L;
    private const int QuarterMonths = 3;
    // D-ND8.18 — the special reserve vote fires when cumulative NEW inflow since the last vote closed
    // exceeds 30% of the reserve value measured at that close (SC+BTC combined — the SC side is
    // structurally 0 until ND.8b.6 lands the automatic conversions, so today this is a pure-BTC test).
    // It does NOT reschedule the quarterly cadence.
    private const decimal SpecialVoteInflowFraction = 0.30m;
    // D-ND8.19b — a discrete ±1 market-category shift needs a weighted supermajority (~60% of total
    // voting weight); the reserve % just moves on the simple weighted average. Tie/sub-threshold = no
    // change (status-quo bias).
    private const decimal MarketShiftSupermajorityFraction = 0.60m;
    private const int MaxVoteHistoryPerCompany = 40;

    // §12.4.3's risk/reward dial, quantified (ND.8b.3 calibration constants, Fibonacci like the ladder
    // tables — tune at the ND.8b playtest): the DEFAULT quarterly dividend rate (% of each reserve side,
    // BTC and SC measured separately per D-ND8.17). Payout ballots clamp to [0, 2× the current default].
    public static decimal DefaultQuarterlyPayoutRatePercent(string marketCategory) => marketCategory switch
    {
        "black" => 21m,
        "dark_grey" => 13m,
        "light_grey" => 8m,
        _ => 5m, // official
    };

    // §12.4.2 — Currency Band geometry: the band default (a company's reserve target until its
    // founding-day vote resolves) and the ±25% vote range, expressed as the SC side % of reserves.
    private static decimal BandDefaultScPercent(string band) => band switch
    {
        "CB1" => 100m,
        "CB2" => 75m,
        "CB3" => 50m,
        "CB4" => 25m,
        _ => 0m, // CB5
    };

    public static (decimal min, decimal max) BandScPercentBounds(string band) => band switch
    {
        "CB1" => (75m, 100m),
        "CB2" => (50m, 100m),
        "CB3" => (25m, 75m),
        "CB4" => (0m, 50m),
        _ => (0m, 25m), // CB5
    };

    // §12.4.3 — light → dark, the axis a quarterly vote may shift by at most ±1 category, clamped within
    // ±1 of the roster DEFAULT (D-ND8.7 — a company never drifts more than one step from its nature).
    private static readonly string[] MarketCategoryOrder = ["official", "light_grey", "dark_grey", "black"];

    private static int MarketCategoryIndex(string category)
    {
        int index = Array.IndexOf(MarketCategoryOrder, category);
        return index >= 0 ? index : 0;
    }

    // Quarterly dates are calendar-anchored (founding date + 3 in-game months per quarter), not a flat
    // day count — matches how the roster/timeline anchors every other historical date.
    private static long AddMonthsMs(long baseUnixMs, int months) =>
        new DateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(baseUnixMs).ToLocalTime().LocalDateTime.AddMonths(months))
            .ToUnixTimeMilliseconds();

    private static decimal CompanyTreasuryBtc(string nonMinerNodeId) =>
        SharedNodesById.TryGetValue(nonMinerNodeId, out NodeAgent? node) ? AggregateSpendable(node) : 0m;

    // D-ND8.13/D-ND8.26 — the four casino-miner-bots draw, once per world: a distinct 4-of-5 Currency
    // Band preference set (one band always unrepresented) and a distinct full permutation of the 4
    // market categories (all stances represented, one per bot). Drawn lazily on the first vote open —
    // always inside block processing, so the draw lands in that same block's snapshot write and stays
    // stable for the rest of the world's life.
    private static void EnsureBotGovernancePreferences()
    {
        IReadOnlyList<BotWalletRecord> minerBots = BotWalletRegistry.MinerBots;
        if (minerBots.Count == 0 || _botGovernancePreferences.Count >= minerBots.Count)
        {
            return;
        }

        string[] bands = ["CB1", "CB2", "CB3", "CB4", "CB5"];
        string[] markets = (string[])MarketCategoryOrder.Clone();
        ShuffleInPlace(bands);
        ShuffleInPlace(markets);
        for (int i = 0; i < minerBots.Count; i++)
        {
            _botGovernancePreferences[minerBots[i].NodeId] = new BotGovernancePreference
            {
                CurrencyBandPreference = bands[i % bands.Length],
                MarketCategoryPreference = markets[i % markets.Length]
            };
        }
    }

    private static void ShuffleInPlace(string[] values)
    {
        for (int i = values.Length - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    // Called from FoundCompany — the company's governance state is born WITH the company, and the
    // founding-day vote (initial reserve-mix direction, D-ND8.18) opens on the same block.
    private static void InitializeCompanyGovernance(Block block, NonMinerDonationSummary summary, CompanyFounding founding)
    {
        if (_companyGovernance.ContainsKey(summary.NonMinerNodeId))
        {
            return;
        }

        string band = string.IsNullOrEmpty(summary.CompanyCurrencyBand) ? "CB3" : summary.CompanyCurrencyBand;
        string category = string.IsNullOrEmpty(summary.CompanyMarketCategory) ? "official" : summary.CompanyMarketCategory;

        var gov = new CompanyGovernanceState
        {
            NonMinerNodeId = summary.NonMinerNodeId,
            CompanyId = founding.CompanyId,
            CurrencyBand = band,
            DefaultMarketCategory = category,
            MarketCategory = category,
            ReserveScPercent = BandDefaultScPercent(band),
            QuarterIndex = 0,
            NextQuarterlyDueMs = AddMonthsMs(founding.FoundedAtUnixMs, QuarterMonths),
            BaselineReserveBtc = CompanyTreasuryBtc(summary.NonMinerNodeId),
            InflowSinceBaselineBtc = 0m
        };
        _companyGovernance[summary.NonMinerNodeId] = gov;

        OpenCompanyVote(gov, founding, CompanyVoteKindFounding, block);
    }

    private static void TickCompanyGovernance(Block block)
    {
        if (_companyGovernance.Count == 0)
        {
            return;
        }

        AccumulateCompanyInflows(block);
        long nowMs = block.Timestamp;

        foreach (CompanyGovernanceState gov in _companyGovernance.Values)
        {
            if (!_companyFoundings.TryGetValue(gov.NonMinerNodeId, out CompanyFounding? founding))
            {
                continue;
            }

            // 1) Close a due vote. AwaitingPlayerVote holds it open — time is paused for the player
            //    anyway (IsAwaitingPlayerVote below), so this guard only bites if the pause was bypassed.
            if (gov.OpenVote != null && nowMs >= gov.OpenVote.ClosesAtMs && !gov.OpenVote.AwaitingPlayerVote)
            {
                CloseCompanyVote(gov, founding, block);
            }

            // 2) With no vote running: the quarterly on its scheduled date takes precedence; else the
            //    >30%-inflow special reserve vote (which never reschedules the quarterly, D-ND8.18).
            if (gov.OpenVote == null)
            {
                if (nowMs >= gov.NextQuarterlyDueMs)
                {
                    // Quarter end: credit the closing cycle's NST lumps + residual PST drip BEFORE the
                    // new quarter's vote opens (D-ND8.17 — the lump is paid at quarter end).
                    SettleDividendCycleAtQuarterEnd(gov, founding, block);
                    gov.QuarterIndex++;
                    gov.NextQuarterlyDueMs = AddMonthsMs(founding.FoundedAtUnixMs, QuarterMonths * (gov.QuarterIndex + 1));
                    OpenCompanyVote(gov, founding, CompanyVoteKindQuarterly, block);
                }
                else if (gov.BaselineReserveBtc > 0m
                    && gov.InflowSinceBaselineBtc > gov.BaselineReserveBtc * SpecialVoteInflowFraction)
                {
                    OpenCompanyVote(gov, founding, CompanyVoteKindSpecial, block);
                }
            }

            // 3) Advance the live dividend cycle: PST daily-drip accrual, then the ND.8b.6 reserve
            //    conversion (fills the SC reserve BEFORE claims draw on it), then bot auto-claims.
            AccrueDailyDrip(gov, founding, block);
            TryConvertCompanyReserves(gov, block);
            TryAutoClaimBotDividends(gov, block);
        }
    }

    // ── ND.8b.6 (D-ND8.24/D-ND8.34) — automatic BTC→SC reserve conversion, provisional casino path ──

    // Calibration floors (v1): convert only when the SC-side deficit is ≥ 5% of total reserve value AND
    // the BTC to sell clears a dust/value floor — conversions stay chunky instead of one tiny tx per
    // inflow, and each conversion is an ORGANIC mempool tx that the fullness-parity budget counts.
    private const decimal ConversionDeficitTriggerFraction = 0.05m;
    private const decimal MinConversionBtc = 0.01m;

    // Moves a founded company's reserves toward its voted ReserveScPercent target: an on-chain
    // company→casino BTC send (network median fee — the network's cost, never a desk fee) paired with an
    // SC credit into the company's ScReserve at the CLEAN market reference rate (the day's price,
    // D-ND8.24), funded from the casino's Main Balance with auto-loan chunks when short (the provisional
    // path — banks take this over at ND.8e, D-ND8.34). Gated on the founding-day vote having closed
    // ("per preferences + the founding vote"); v1 converts BTC→SC only — the reverse direction needs the
    // casino to SELL BTC for SC, which is the swap desk/bank's job, deferred with the provisional path.
    private static void TryConvertCompanyReserves(CompanyGovernanceState gov, Block block)
    {
        if (gov.VoteHistory.Count == 0 || gov.ReserveScPercent <= 0m || _casinoSc == null)
        {
            return;
        }

        decimal? priceUsd = _marketData?.GetEffectivePriceUsd(
            DateTimeOffset.FromUnixTimeMilliseconds(block.Timestamp).LocalDateTime);
        if (priceUsd is not decimal price || price <= 0m)
        {
            return; // no market yet (structurally unreachable post-founding — auctions start at Market Birth)
        }

        decimal treasuryBtc = CompanyTreasuryBtc(gov.NonMinerNodeId);
        decimal totalValueSc = treasuryBtc * price + gov.ScReserve;
        if (totalValueSc <= 0m)
        {
            return;
        }

        decimal targetSc = totalValueSc * gov.ReserveScPercent / 100m;
        decimal deficitSc = targetSc - gov.ScReserve;
        if (deficitSc < totalValueSc * ConversionDeficitTriggerFraction)
        {
            return;
        }

        decimal fee = NetworkFeePolicy.MedianFeeAt(block.Timestamp);
        decimal btcToSell = Scripts.Finance.Money.Normalize(deficitSc / price);
        if (btcToSell + fee > treasuryBtc)
        {
            btcToSell = Scripts.Finance.Money.Normalize(treasuryBtc - fee);
        }

        if (btcToSell < Math.Max(MinConversionBtc, fee * 2m))
        {
            return;
        }

        if (!SharedNodesById.TryGetValue(gov.NonMinerNodeId, out NodeAgent? company)
            || !SharedNodesById.TryGetValue(CasinoNodeId, out NodeAgent? casino))
        {
            return;
        }

        decimal scAmount = Scripts.Finance.Money.Normalize(btcToSell * price);
        if (scAmount <= 0m || !_casinoSc.TryPayCompanyProvisionSc(scAmount, "company_conversion"))
        {
            return;
        }

        if (BuildAndBroadcastUtxoSpend(company, casino.WalletAddress, btcToSell, fee, null, "CONVERSION") == null)
        {
            _casinoSc.ReceiveSwapSc(scAmount); // unwind the SC leg on a failed broadcast (the SW.4 pattern)
            return;
        }

        gov.ScReserve = Scripts.Finance.Money.Normalize(gov.ScReserve + scAmount);
        AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "conversion", "btc_to_sc",
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "btc={0:F8};sc={1:F8};price={2:F2}", btcToSell, scAmount, price));
    }

    // New BTC arriving at a founded company's address this block (its own sends' change excluded) feeds
    // the D-ND8.18 >30% special-vote trigger. Companies are single-address today (OQ-8.2).
    private static void AccumulateCompanyInflows(Block block)
    {
        foreach (CompanyGovernanceState gov in _companyGovernance.Values)
        {
            if (!_companyFoundings.TryGetValue(gov.NonMinerNodeId, out CompanyFounding? founding))
            {
                continue;
            }

            string address = founding.NonMinerAddress;
            foreach (Transaction tx in block.Transactions)
            {
                if (tx.IsCoinbase || tx.Inputs.Any(i => i.Address == address))
                {
                    continue;
                }

                foreach (TxOutput output in tx.Outputs)
                {
                    if (output.Address == address)
                    {
                        gov.InflowSinceBaselineBtc = Scripts.Finance.Money.Normalize(gov.InflowSinceBaselineBtc + output.Amount);
                    }
                }
            }
        }
    }

    private static void OpenCompanyVote(CompanyGovernanceState gov, CompanyFounding founding, string kind, Block block)
    {
        EnsureBotGovernancePreferences();

        var vote = new CompanyVote
        {
            Kind = kind,
            OpenedAtMs = block.Timestamp,
            ClosesAtMs = block.Timestamp + VoteDurationMs
        };

        // Bots cast immediately and deterministically from their persisted preferences (D-ND8.4 —
        // weighting/direction, never filtering; a restart before the closing block simply re-runs the
        // same ballots). The player's ballot arrives through TryRegisterPlayerVote (the Board Vote
        // panel); until it does, the game is paused for them (D-ND8.18, IsAwaitingPlayerVote below).
        foreach (CompanyShareHolding holding in founding.Holdings)
        {
            if (holding.Nst <= 0m)
            {
                continue; // PST holders carry zero votes (D-ND8.6)
            }

            if (holding.HolderId == PlayerNodeId)
            {
                vote.AwaitingPlayerVote = true;
                continue;
            }

            vote.Ballots[holding.HolderId] = BuildBotBallot(holding.HolderId, gov);
        }

        gov.OpenVote = vote;
        AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "vote_open", kind,
            vote.AwaitingPlayerVote ? "awaiting_player" : "bots_only");
    }

    // A bot's ballot is a pure function of its persisted preferences + the company's current state:
    // Currency — its continuous target IS its preference point (a CB3 "balancer" targets 50/50 and damps
    // swings, D-ND8.19b needs no special-casing); Market — one step toward its preferred category;
    // Payout — the current category's default rate (bots vote "the standard").
    private static CompanyBallot BuildBotBallot(string botNodeId, CompanyGovernanceState gov)
    {
        _botGovernancePreferences.TryGetValue(botNodeId, out BotGovernancePreference? pref);
        return new CompanyBallot
        {
            ReserveScPercentTarget = BandDefaultScPercent(pref?.CurrencyBandPreference ?? gov.CurrencyBand),
            MarketShift = pref == null
                ? 0
                : Math.Sign(MarketCategoryIndex(pref.MarketCategoryPreference) - MarketCategoryIndex(gov.MarketCategory)),
            PayoutRatePercent = DefaultQuarterlyPayoutRatePercent(gov.MarketCategory)
        };
    }

    private static void CloseCompanyVote(CompanyGovernanceState gov, CompanyFounding founding, Block block)
    {
        CompanyVote vote = gov.OpenVote!;
        gov.OpenVote = null;

        decimal totalNst = founding.Holdings.Where(h => h.Nst > 0m).Sum(h => h.Nst);
        decimal reserveResult = gov.ReserveScPercent;
        decimal payoutResult = 0m;
        int shiftResult = 0;

        if (totalNst > 0m && vote.Ballots.Count > 0)
        {
            Dictionary<string, decimal> nstByHolder = founding.Holdings
                .Where(h => h.Nst > 0m)
                .ToDictionary(h => h.HolderId, h => h.Nst);

            decimal votedWeight = 0m, weightedReserve = 0m, weightedPayout = 0m, lighterWeight = 0m, darkerWeight = 0m;
            foreach ((string holderId, CompanyBallot ballot) in vote.Ballots)
            {
                if (!nstByHolder.TryGetValue(holderId, out decimal nst) || nst <= 0m)
                {
                    continue;
                }

                decimal weight = nst / totalNst; // D-ND8.15 step 6 — voting weight = NST ÷ total NST
                votedWeight += weight;
                weightedReserve += weight * ballot.ReserveScPercentTarget;
                weightedPayout += weight * ballot.PayoutRatePercent;
                if (ballot.MarketShift > 0) darkerWeight += weight;
                else if (ballot.MarketShift < 0) lighterWeight += weight;
            }

            if (votedWeight > 0m)
            {
                // D-ND8.19b — reserve %: simple weighted average of the cast targets, clamped to the
                // band's ±25% range.
                (decimal min, decimal max) = BandScPercentBounds(gov.CurrencyBand);
                reserveResult = Math.Clamp(Scripts.Finance.Money.Normalize(weightedReserve / votedWeight), min, max);

                if (vote.Kind == CompanyVoteKindQuarterly)
                {
                    // D-ND8.19b — a market shift is discrete and riskier: it needs ≥60% of TOTAL voting
                    // weight in one direction, and lands clamped within ±1 of the roster default.
                    if (darkerWeight >= MarketShiftSupermajorityFraction) shiftResult = 1;
                    else if (lighterWeight >= MarketShiftSupermajorityFraction) shiftResult = -1;
                    if (shiftResult != 0)
                    {
                        int defaultIndex = MarketCategoryIndex(gov.DefaultMarketCategory);
                        int newIndex = Math.Clamp(
                            MarketCategoryIndex(gov.MarketCategory) + shiftResult,
                            Math.Max(0, defaultIndex - 1),
                            Math.Min(MarketCategoryOrder.Length - 1, defaultIndex + 1));
                        gov.MarketCategory = MarketCategoryOrder[newIndex];
                    }

                    payoutResult = Math.Clamp(
                        Scripts.Finance.Money.Normalize(weightedPayout / votedWeight),
                        0m,
                        DefaultQuarterlyPayoutRatePercent(gov.MarketCategory) * 2m);
                }
            }
        }

        gov.ReserveScPercent = reserveResult;

        if (vote.Kind == CompanyVoteKindQuarterly)
        {
            // D-ND8.17 — FINALIZE the quarter's dividend as two separately-tracked amounts (never live
            // accrual): each currency side is payoutRate% of the corresponding reserve at finalize time.
            // The SC side is structurally 0 until ND.8b.6 lands the BTC→SC conversions.
            decimal treasuryBtc = CompanyTreasuryBtc(gov.NonMinerNodeId);
            gov.QuarterPayoutRatePercent = payoutResult;
            gov.QuarterDividendBtc = Scripts.Finance.Money.Normalize(treasuryBtc * payoutResult / 100m);
            gov.QuarterDividendSc = Scripts.Finance.Money.Normalize(gov.ScReserve * payoutResult / 100m);
            gov.QuarterCycleStartMs = block.Timestamp;
            gov.QuarterCycleEndMs = gov.NextQuarterlyDueMs;
            gov.QuarterDrippedDays = 0;
            gov.QuarterLumpCredited = false;
        }

        // Reset the >30% special-vote baseline at EVERY vote close — "new inflow" is measured from the
        // last governance event (D-ND8.18).
        gov.BaselineReserveBtc = CompanyTreasuryBtc(gov.NonMinerNodeId);
        gov.InflowSinceBaselineBtc = 0m;

        gov.VoteHistory.Add(new CompanyVoteRecord
        {
            Kind = vote.Kind,
            OpenedAtMs = vote.OpenedAtMs,
            ClosedAtMs = block.Timestamp,
            ResultReserveScPercent = gov.ReserveScPercent,
            ResultMarketCategory = gov.MarketCategory,
            ResultPayoutRatePercent = vote.Kind == CompanyVoteKindQuarterly ? gov.QuarterPayoutRatePercent : 0m,
            FinalizedDividendBtc = vote.Kind == CompanyVoteKindQuarterly ? gov.QuarterDividendBtc : 0m,
            FinalizedDividendSc = vote.Kind == CompanyVoteKindQuarterly ? gov.QuarterDividendSc : 0m
        });
        if (gov.VoteHistory.Count > MaxVoteHistoryPerCompany)
        {
            gov.VoteHistory.RemoveAt(0);
        }

        AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "vote_close", vote.Kind,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "shift={0}", shiftResult));
    }

    // D-ND8.17 — the PST daily drip: each elapsed in-game day of the active cycle accrues
    // (profit% × finalized dividend) ÷ days-in-quarter into the PST holder's claimable balance. Accrual
    // is date-diffed (block granularity), so restarts/slow blocks never lose or double a day.
    private static void AccrueDailyDrip(CompanyGovernanceState gov, CompanyFounding founding, Block block)
    {
        if (gov.QuarterCycleStartMs <= 0 || gov.QuarterLumpCredited
            || (gov.QuarterDividendBtc <= 0m && gov.QuarterDividendSc <= 0m))
        {
            return;
        }

        int daysInQuarter = Math.Max(1, (int)((gov.QuarterCycleEndMs - gov.QuarterCycleStartMs) / GameDayMs));
        int elapsedDays = (int)Math.Clamp((block.Timestamp - gov.QuarterCycleStartMs) / GameDayMs, 0L, daysInQuarter);
        int daysToDrip = elapsedDays - gov.QuarterDrippedDays;
        if (daysToDrip <= 0)
        {
            return;
        }

        decimal totalTokens = founding.Holdings.Sum(h => h.Nst + h.Pst);
        if (totalTokens <= 0m)
        {
            return;
        }

        foreach (CompanyShareHolding holding in founding.Holdings)
        {
            if (holding.Pst <= 0m)
            {
                continue; // the daily drip is the PST payment preference; NST waits for the lump
            }

            decimal share = (holding.Nst + holding.Pst) / totalTokens; // D-ND8.15 step 5 — profit participation
            CompanyClaimable claim = GetOrCreateClaimable(gov, holding.HolderId);
            claim.Btc = Scripts.Finance.Money.Normalize(claim.Btc + share * gov.QuarterDividendBtc / daysInQuarter * daysToDrip);
            claim.Sc = Scripts.Finance.Money.Normalize(claim.Sc + share * gov.QuarterDividendSc / daysInQuarter * daysToDrip);
        }

        gov.QuarterDrippedDays = elapsedDays;
    }

    // Quarter end (D-ND8.17): NST holders' lump = their full profit-participation share of the finalized
    // dividend; PST holders get any residual drip days flushed so the finalized amounts distribute
    // exactly. Runs once per cycle, right before the next quarterly vote opens.
    private static void SettleDividendCycleAtQuarterEnd(CompanyGovernanceState gov, CompanyFounding founding, Block block)
    {
        if (gov.QuarterCycleStartMs <= 0 || gov.QuarterLumpCredited)
        {
            return;
        }

        gov.QuarterLumpCredited = true;
        if (gov.QuarterDividendBtc <= 0m && gov.QuarterDividendSc <= 0m)
        {
            return;
        }

        decimal totalTokens = founding.Holdings.Sum(h => h.Nst + h.Pst);
        if (totalTokens <= 0m)
        {
            return;
        }

        int daysInQuarter = Math.Max(1, (int)((gov.QuarterCycleEndMs - gov.QuarterCycleStartMs) / GameDayMs));
        int residualDays = Math.Max(0, daysInQuarter - gov.QuarterDrippedDays);

        foreach (CompanyShareHolding holding in founding.Holdings)
        {
            decimal share = (holding.Nst + holding.Pst) / totalTokens;
            CompanyClaimable claim = GetOrCreateClaimable(gov, holding.HolderId);
            if (holding.Nst > 0m)
            {
                claim.Btc = Scripts.Finance.Money.Normalize(claim.Btc + share * gov.QuarterDividendBtc);
                claim.Sc = Scripts.Finance.Money.Normalize(claim.Sc + share * gov.QuarterDividendSc);
            }
            else if (holding.Pst > 0m && residualDays > 0)
            {
                claim.Btc = Scripts.Finance.Money.Normalize(claim.Btc + share * gov.QuarterDividendBtc / daysInQuarter * residualDays);
                claim.Sc = Scripts.Finance.Money.Normalize(claim.Sc + share * gov.QuarterDividendSc / daysInQuarter * residualDays);
            }
        }

        gov.QuarterDrippedDays = daysInQuarter;
        AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "quarter_settled", CompanyVoteKindQuarterly, "");
    }

    private static CompanyClaimable GetOrCreateClaimable(CompanyGovernanceState gov, string holderId)
    {
        if (!gov.ClaimableByHolder.TryGetValue(holderId, out CompanyClaimable? claim))
        {
            claim = new CompanyClaimable();
            gov.ClaimableByHolder[holderId] = claim;
        }

        return claim;
    }

    // Bots auto-claim EVERY dividend arrival (developer directive, 2026-07-20 — normal/NST lumps and
    // preferred/PST drips alike, superseding the initial 2×fee value floor): each accrual is swept with
    // a real on-chain company→bot send on the same block it lands, the network fee deducted from the
    // dividend itself (the ND.5 sweep precedent — accepted shortfall). The only remaining gate is the
    // physical one — the claim must NET something (claimable > fee), since a send cannot pay out less
    // than its own fee; a sub-fee accrual just waits for the next drip day to push it over. A failed
    // broadcast (treasury momentarily tied up) retries on a later block — the claimable never disappears.
    private static void TryAutoClaimBotDividends(CompanyGovernanceState gov, Block block)
    {
        if (gov.ClaimableByHolder.Count == 0 || !SharedNodesById.TryGetValue(gov.NonMinerNodeId, out NodeAgent? company))
        {
            return;
        }

        decimal fee = NetworkFeePolicy.MedianFeeAt(block.Timestamp);
        foreach ((string holderId, CompanyClaimable claim) in gov.ClaimableByHolder)
        {
            if (holderId == PlayerNodeId)
            {
                continue; // the player claims manually (CompanyDetails panel)
            }

            if (!SharedNodesById.TryGetValue(holderId, out NodeAgent? holder))
            {
                continue;
            }

            // ND.8b.6 — the SC side pays instantly from the company's SC reserve into the bot's own SC
            // principal (the NodeFinancialState mirror the recharge/settlement paths already use);
            // partial when the reserve is short, remainder stays accrued. Skipped while the bot has no
            // financial state yet (it materializes on its first bet — always long before any dividend).
            if (claim.Sc > 0m && gov.ScReserve > 0m && holder.FinancialState is NodeFinancialState fin)
            {
                decimal paySc = Math.Min(claim.Sc, gov.ScReserve);
                fin.PrincipalBalance = Scripts.Finance.Money.Normalize(fin.PrincipalBalance + paySc);
                gov.ScReserve = Scripts.Finance.Money.Normalize(gov.ScReserve - paySc);
                claim.Sc = Scripts.Finance.Money.Normalize(claim.Sc - paySc);
            }

            if (claim.Btc <= fee)
            {
                continue; // physically unpayable (net ≤ 0) — waits for the next accrual to clear the fee
            }

            decimal sendAmount = Scripts.Finance.Money.Normalize(claim.Btc - fee);
            if (sendAmount <= 0m || BuildAndBroadcastUtxoSpend(company, holder.WalletAddress, sendAmount, fee, null, "DIVIDEND") == null)
            {
                continue;
            }

            claim.Btc = 0m;
            AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "bot_claim", holderId,
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "btc={0:F8}", sendAmount));
        }
    }

    // ── ND.8b.3 public surface (the pause gate + the CompanyDetails scene's read/act API) ─────────────

    // D-ND8.18 — TRUE while any founded company's open vote still lacks the player's ballot (the player
    // holds NST there). SimulationService pauses the autobet tick and DiceGame refuses manual bets while
    // this holds, so game time cannot advance past the vote without the player's say.
    public static bool IsAwaitingPlayerVote
    {
        get
        {
            foreach (CompanyGovernanceState gov in _companyGovernance.Values)
            {
                if (gov.OpenVote is { AwaitingPlayerVote: true })
                {
                    return true;
                }
            }

            return false;
        }
    }

    // The companies currently waiting on the player's ballot — for the DiceGame pause notice and the
    // Board Vote panel's routing.
    public static IReadOnlyList<(string nonMinerNodeId, string companyDisplayName)> GetCompaniesAwaitingPlayerVote()
    {
        var result = new List<(string, string)>();
        foreach (CompanyGovernanceState gov in _companyGovernance.Values)
        {
            if (gov.OpenVote is not { AwaitingPlayerVote: true })
            {
                continue;
            }

            CompanyRecord? record = CompanyRoster.ByCompanyId(gov.CompanyId);
            result.Add((gov.NonMinerNodeId, record?.DisplayName ?? gov.CompanyId));
        }

        return result;
    }

    // The Board Vote panel's submit path. Clamps every field into its legal range (band ±25%, shift ∈
    // {-1,0,1}, payout ∈ [0, 2× default]) rather than rejecting — the UI pre-fills legal values anyway.
    // Registering the ballot lifts the pause; the vote still closes on its own one-day schedule.
    public static bool TryRegisterPlayerVote(string nonMinerNodeId, decimal reserveScPercentTarget, int marketShift, decimal payoutRatePercent)
    {
        if (!_companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov)
            || gov.OpenVote is not { } vote
            || !_companyFoundings.TryGetValue(nonMinerNodeId, out CompanyFounding? founding)
            || !founding.Holdings.Any(h => h.HolderId == PlayerNodeId && h.Nst > 0m))
        {
            return false;
        }

        (decimal min, decimal max) = BandScPercentBounds(gov.CurrencyBand);
        vote.Ballots[PlayerNodeId] = new CompanyBallot
        {
            ReserveScPercentTarget = Math.Clamp(Scripts.Finance.Money.Normalize(reserveScPercentTarget), min, max),
            MarketShift = Math.Clamp(marketShift, -1, 1),
            PayoutRatePercent = Math.Clamp(Scripts.Finance.Money.Normalize(payoutRatePercent), 0m,
                DefaultQuarterlyPayoutRatePercent(gov.MarketCategory) * 2m)
        };
        vote.AwaitingPlayerVote = false;
        return true;
    }

    public CompanyGovernanceState? GetCompanyGovernanceByNodeId(string nonMinerNodeId)
    {
        EnsureInitialized();
        return _companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov) ? gov : null;
    }

    // The player's manual dividend claim (Quarterly/Daily Dividend panels). The BTC side goes on-chain
    // to the player's BASE address (the D-SW.6 precedent), network fee deducted from the claim itself;
    // the SC side (ND.8b.6) pays instantly from the company's SC reserve into the player's Main Balance
    // (partial when the reserve is short — the remainder stays accrued).
    public (bool ok, string message) TryClaimPlayerCompanyDividends(string nonMinerNodeId)
    {
        EnsureInitialized();
        if (!_companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov)
            || !gov.ClaimableByHolder.TryGetValue(PlayerNodeId, out CompanyClaimable? claim)
            || (claim.Btc <= 0m && claim.Sc <= 0m))
        {
            return (false, "Nothing to claim yet.");
        }

        if (!SharedNodesById.TryGetValue(gov.NonMinerNodeId, out NodeAgent? company)
            || !SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
        {
            return (false, "Company node unavailable.");
        }

        // SC leg (ND.8b.6) — instant, off-chain: company SC reserve → player Main Balance.
        decimal paidSc = 0m;
        if (claim.Sc > 0m && gov.ScReserve > 0m && _principalBalance != null)
        {
            paidSc = Math.Min(claim.Sc, gov.ScReserve);
            _principalBalance.Deposit(paidSc);
            gov.ScReserve = Scripts.Finance.Money.Normalize(gov.ScReserve - paidSc);
            claim.Sc = Scripts.Finance.Money.Normalize(claim.Sc - paidSc);
        }

        // BTC leg — on-chain, fee deducted from the claim.
        long tipMs = player.Blockchain.GetLastBlock().Timestamp;
        decimal fee = NetworkFeePolicy.MedianFeeAt(tipMs);
        decimal paidBtc = 0m;
        string btcNote = string.Empty;
        if (claim.Btc > fee)
        {
            decimal sendAmount = Scripts.Finance.Money.Normalize(claim.Btc - fee);
            if (BuildAndBroadcastUtxoSpend(company, player.WalletAddress, sendAmount, fee, null, "DIVIDEND") != null)
            {
                claim.Btc = 0m;
                paidBtc = sendAmount;
            }
            else
            {
                btcNote = " BTC leg failed (treasury tied up) — try again after the next block.";
            }
        }
        else if (claim.Btc > 0m)
        {
            btcNote = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                " {0:F8} BTC stays accrued (does not cover the {1:F8} network fee yet).", claim.Btc, fee);
        }

        if (paidBtc <= 0m && paidSc <= 0m)
        {
            return (false, "Nothing claimable right now." + btcNote);
        }

        string btcPart = paidBtc > 0m
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F8} BTC broadcast (fee {1:F8}, confirms next block)", paidBtc, fee)
            : string.Empty;
        string scPart = paidSc > 0m
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F8} SC credited to Main Balance", paidSc)
            : string.Empty;
        string joined = btcPart.Length > 0 && scPart.Length > 0 ? $"{btcPart} + {scPart}" : btcPart + scPart;
        return (true, $"Dividend claim: {joined}.{btcNote}");
    }

    private const string CompanyGovernanceTracePath = "user://logs/company_governance_trace.csv";

    // ND.8b.3 telemetry — one row per governance event (vote_open / vote_close / quarter_settled /
    // bot_claim). Daily drip accruals are deliberately NOT logged (row volume); the quarter_settled and
    // claim rows bracket them for playtest verification.
    private static void AppendCompanyGovernanceTrace(long timestampMs, int blockIndex, CompanyGovernanceState gov,
        string eventType, string kind, string detail)
    {
        try
        {
            if (!DirAccess.DirExistsAbsolute("user://logs"))
            {
                DirAccess.MakeDirRecursiveAbsolute("user://logs");
            }

            bool exists = FileAccess.FileExists(CompanyGovernanceTracePath);
            using FileAccess file = exists
                ? FileAccess.Open(CompanyGovernanceTracePath, FileAccess.ModeFlags.ReadWrite)
                : FileAccess.Open(CompanyGovernanceTracePath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                return;
            }

            if (exists) file.SeekEnd();
            else file.StoreLine("blockTimestampMs,blockIndex,companyId,event,kind,reserveScPct,marketCategory,payoutRatePct,dividendBtc,dividendSc,detail");

            file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5:F2},{6},{7:F2},{8:F8},{9:F8},{10}",
                timestampMs, blockIndex, gov.CompanyId, eventType, kind, gov.ReserveScPercent,
                gov.MarketCategory, gov.QuarterPayoutRatePercent, gov.QuarterDividendBtc, gov.QuarterDividendSc, detail));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[CompanyGovernanceTrace] failed: {e.Message}");
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

    // Step 14 (ND.3) — P-14.B fullness parity: the historical target for txs per block, pushed once per
    // block by SimulationService from BtcNetworkDataService.GetTargetTxPerBlock (real txs-per-block ÷ 100,
    // capped by block capacity). 0 until an autobet run pushes it — so the historical bootstrap and
    // manual-only play produce no automated traffic (the same limitation founder mining already has).
    private static decimal _scheduledTxTargetPerBlock;
    private static double _scheduledTxCarry;

    public static void SetScheduledTxTargetPerBlock(decimal target) =>
        _scheduledTxTargetPerBlock = target > 0m ? target : 0m;

    private static void ScheduleBotTransactionsAfterBlock(Block block)
    {
        // Canonical chain (the player's synced view) — used to measure each miner's mining warmup.
        List<Block> chain = SharedNodesById[PlayerNodeId].Blockchain.Chain;

        // ND.4a — the casino-bots' own cycle runs first and OUTSIDE the budget (its txids are exempted
        // from the organic-pending count below).
        TryCasinoBotDonation(block, chain);

        // The mempool is primed to hover AT the target level, so the next block template (which takes up
        // to MaxBlockTransactions − 1 pending txs by fee) carries ≈ target non-coinbase txs. ORGANIC
        // traffic counts first — every already-pending tx (player sends, swap legs, pool payouts) cancels
        // accrued demand 1:1 BEFORE flooring (ND.4a fix: the old post-floor `wholeOwed - pending` both
        // erased up to a whole block's accumulated carry on a collision AND ignored organic txs entirely
        // on sub-1-owed blocks); automation only tops up the remainder (D-14.2: real sends only,
        // under-shooting accepted when balances can't sustain the rate). The fractional carry realizes
        // sub-1 targets — e.g. 2010's ≈0.01 tx/block becomes one automated tx every ~100 blocks,
        // historically faithful near-empty blocks (D-14.10). Replaces BotSendProbabilityPerBlock = 0.5.
        List<Transaction> pendingTxs = SharedNodesById[PlayerNodeId].Blockchain.PendingTransactions;
        _casinoBotCycleTxIds.IntersectWith(pendingTxs.Select(t => t.TransactionId)); // prune confirmed
        int pending = pendingTxs.Count - _casinoBotCycleTxIds.Count;

        double owed = Math.Max(0d, (double)_scheduledTxTargetPerBlock + _scheduledTxCarry - pending);
        int budget = (int)Math.Floor(owed);
        _scheduledTxCarry = owed - budget;
        if (budget <= 0) return;

        int created = TryCastSellFlow(block, chain, budget);
        if (created < budget)
            TryNonMinerExchanges(block, budget - created);
    }

    // ND.4b/ND.4c — the casino-miner-bots' (bot_1..4) fast-cycle competitive donation/bid cycle
    // (D-ND4b.2-6/11): replaces ND.4a's geometric ~1-per-100-block draw with a per-block count draw
    // (0/1/2, weighted — D-ND4b.3). Each slot's chosen bot runs the ND.6a SATURATION-LADDER pipeline
    // (D-ND6.1…10, 2026-07-12, superseding the 2026-07-11 self-competition rules 1-3 — see
    // TryCasinoBotDonateOnce/TryBuildCasinoBotBid), sending either the fixed first-donation floor or a
    // coin-flip raise over the current leading bid (D-ND4b.5/6), topped with a random additive tail so
    // amounts never repeat as clean round numbers (D-ND4b.11, cap-bounded at ND.6a). These are the ONLY
    // automated sends that qualify as referral auction bids (D-EB.7 — the eligibility filter itself lives
    // entirely in ComputeAuctionLedger).
    private static void TryCasinoBotDonation(Block block, List<Block> chain)
    {
        int count = DrawCasinoBotDonationCount();
        if (count == 0) return;

        // D-ND4b.4: order targets — active auctions soonest-to-expire first, then not-yet-competing ones
        // — computed ONCE against the chain as of the just-mined block, so every donation slot this
        // block is measured against the SAME starting leader (the D-ND4b.11 same-block tie-break relies
        // on this: two independent bids racing the same pre-block leader, not each other in sequence).
        // ND.6a: each bot then re-orders these per D-ND6.6 (own-slot count first) inside its own pipeline.
        List<NonMinerDonationSummary> ledger = ComputeAuctionLedger(block.Timestamp);
        List<NonMinerDonationSummary> recruitable = ledger.Where(s => s.Status == NonMinerAuctionStatus.InAuction).ToList();
        List<NonMinerDonationSummary> priorityTargets = recruitable
            .Where(s => s.LeadingBidUnixMs != 0)
            .OrderBy(s => s.WindowCloseUnixMs)
            .Concat(recruitable.Where(s => s.LeadingBidUnixMs == 0))
            .ToList();
        if (priorityTargets.Count == 0) return;

        var usedBotIds = new HashSet<string>();
        for (int slot = 0; slot < count; slot++)
        {
            Transaction? tx = TryCasinoBotDonateOnce(block, priorityTargets, usedBotIds, slot);
            if (tx != null)
            {
                _casinoBotCycleTxIds.Add(tx.TransactionId);
            }
        }
    }

    // D-ND4b.3: 15% chance of 0, 70% chance of 1, 15% chance of 2 — a flat "always 1" reads monotone.
    private static int DrawCasinoBotDonationCount()
    {
        double roll = Random.Shared.NextDouble() * 100d;
        if (roll < CasinoBotDonationWeightZeroPercent) return 0;
        if (roll < CasinoBotDonationWeightZeroPercent + CasinoBotDonationWeightOnePercent) return 1;
        return 2;
    }

    // ND.6a (D-ND6.1/6.9, 2026-07-12 — supersedes the 2026-07-11 self-competition rules 1-3): the bot
    // for THIS donation slot is chosen FIRST (fair random pick among not-yet-used-this-block bots,
    // D-ND6.1 — a bot keeps its full selection probability even when its own rules will produce no
    // donation), then runs its own full bidding pipeline (TryBuildCasinoBotBid). A rule-excluded target
    // list or a failed ladder roll consumes the slot — no substitution. The ONE exception (D-ND6.9):
    // when the chosen bot HAS qualifying targets but can afford NONE of them under the half-spendable
    // cap, the slot cascades to another bot (which re-runs its OWN full pipeline — own participation
    // state, own preference order, own roll, own cap), potentially through all four. A bot that
    // cascades away is NOT marked used-this-block (it did nothing); only the bot that actually donates
    // consumes its once-per-block eligibility.
    private static Transaction? TryCasinoBotDonateOnce(Block block, List<NonMinerDonationSummary> priorityTargets, HashSet<string> usedBotIds, int slot)
    {
        // D-ND7.7 — only the network fee ATTACHED to bid txs replays the daily median (it was
        // MinFee-pinned before, D-ND4b.5's rationale carries over); bid AMOUNTS stay untouched.
        decimal fee = NetworkFeePolicy.MedianFeeAt(block.Timestamp);

        var visitedThisSlot = new HashSet<string>();
        int hop = 0; // 0 = the slot's fairly-drawn first bot; >0 = D-ND6.9 affordability-cascade substitutes
        while (true)
        {
            List<BotWalletRecord> candidates = BotWalletRegistry.MinerBots
                .Where(b => !usedBotIds.Contains(b.NodeId) && !visitedThisSlot.Contains(b.NodeId))
                .ToList();
            if (candidates.Count == 0) return null; // the cascade exhausted every bot — the slot yields nothing

            BotWalletRecord record = candidates[Random.Shared.Next(candidates.Count)];
            visitedThisSlot.Add(record.NodeId);
            if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? sender)) continue; // registry hole — pass the slot on

            CasinoBotSlotOutcome outcome = TryBuildCasinoBotBid(priorityTargets, sender, fee, block.Timestamp, out Transaction? tx, out CasinoBotBidTrace trace);
            AppendCasinoBotBidTrace(block, slot, hop, sender.NodeId, outcome, trace);
            hop++;
            if (outcome == CasinoBotSlotOutcome.Donated && tx != null)
            {
                usedBotIds.Add(sender.NodeId);
                return tx;
            }
            if (outcome != CasinoBotSlotOutcome.NothingAffordable)
                return null; // rule-based/roll-based refusal (or a failed broadcast) never cascades — D-ND6.1
            // NothingAffordable → the D-ND6.9 affordability cascade: try another bot for this same slot.
        }
    }

    // ND.6a — per-slot outcome of one bot's bidding pipeline. Only NothingAffordable cascades (D-ND6.9).
    private enum CasinoBotSlotOutcome { Donated, NoQualifyingTarget, NothingAffordable, RollDeclined, BroadcastFailed }

    // ND.6a — one bot's full bidding pipeline for one donation slot (the saturation ladder, D-ND6.5…6.8):
    //   1. Qualifying pools (D-ND6.7): every InAuction target EXCEPT pools where this bot holds a top-3
    //      tracked slot (satisfied) or the smallest slot of a FULL pool (the self-eviction guard — its
    //      own new bid would evict its own smallest donation, forfeiting the settlement refund already
    //      secured as the auction stands, D-ND5.4). Both exclusions apply in every participation state.
    //      (Satisfied subsumes the old rule 1: the leading bid is by construction the pool's tier 1.)
    //   2. Bot-centric preference order (D-ND6.6): ascending count of the bot's OWN tracked slots
    //      (0-participation pools first — the spread-wide priority), ties keeping priorityTargets'
    //      soonest-to-expire-then-awaiting-first-bid order (stable OrderBy).
    //   3. Half-spendable affordability walk (D-ND6.8): the first pool in that order whose required
    //      amount + fee fits within spendable × MaxBidBalanceFraction is THE target — unaffordable pools
    //      are skipped; if none fits, NothingAffordable (the only outcome that cascades).
    //   4. ONE ladder roll (D-ND6.5), only for a participated target: rolled on the tier with the LOWEST
    //      re-bid probability among the bot's own slots there (= its best/shallowest slot — holding
    //      tiers 4 and 7 rolls the 4th tier's 5%, never the 7th's 21%). A failed roll = no donation this
    //      slot, never re-rolled against another slot or pool. Unparticipated targets donate
    //      deterministically (first-time bids need no ladder, as before ND.6).
    private static CasinoBotSlotOutcome TryBuildCasinoBotBid(List<NonMinerDonationSummary> priorityTargets, NodeAgent sender, decimal fee, long nowMs, out Transaction? tx, out CasinoBotBidTrace trace)
    {
        tx = null;
        string botAddress = sender.WalletAddress;
        decimal spendable = sender.Blockchain.GetAddressSpendableBalance(botAddress);
        decimal bidBudgetCap = Math.Round(spendable * MaxBidBalanceFraction, 8);
        trace = new CasinoBotBidTrace { FeeBtc = fee, SpendableBtc = spendable, BidBudgetCapBtc = bidBudgetCap };

        // Steps 1+2 — qualifying pools with this bot's own slot stats (tiers are 1-based positions in
        // the pool's value order; a stable sort keeps ties in arrival order — consistent with ND.5's
        // tie-never-evicts).
        var qualifying = new List<(NonMinerDonationSummary target, int ownSlotCount, int bestTier, int occupiedSlots, string ownTiersJoined)>();
        foreach (NonMinerDonationSummary target in priorityTargets)
        {
            List<TrackedDonation> slotsByValue = target.TrackedDonations.OrderByDescending(d => d.AmountBtc).ToList();
            var ownTiers = new List<int>();
            for (int i = 0; i < slotsByValue.Count; i++)
            {
                if (slotsByValue[i].DonorAddress == botAddress) ownTiers.Add(i + 1);
            }

            if (ownTiers.Count > 0 && ownTiers[0] <= SatisfiedTopTierCount) continue; // D-ND6.7a — satisfied (ownTiers is ascending by construction)
            if (slotsByValue.Count >= MaxTrackedDonations && ownTiers.Contains(slotsByValue.Count)) continue; // D-ND6.7b — self-eviction guard

            // ND.6d — slotsByValue.Count (the pool's current occupied slots) selects early-rush (<7) vs normal (≥7) for the roll below.
            qualifying.Add((target, ownTiers.Count, ownTiers.Count == 0 ? 0 : ownTiers[0], slotsByValue.Count, string.Join("|", ownTiers)));
        }
        if (qualifying.Count == 0) return CasinoBotSlotOutcome.NoQualifyingTarget;

        // Step 3 — the affordability walk over the bot-centric order.
        foreach ((NonMinerDonationSummary target, int ownSlotCount, int bestTier, int occupiedSlots, string ownTiersJoined) in qualifying.OrderBy(q => q.ownSlotCount))
        {
            decimal leadingAmount = target.LeadingBidUnixMs == 0 ? 0m : target.LeadingDonorTotal;
            decimal requiredAmount = target.LeadingBidUnixMs == 0 ? MinBidBtc : leadingAmount + RaiseMin(leadingAmount);
            if (requiredAmount + fee > bidBudgetCap) continue; // D-ND6.8 — unaffordable, keep walking

            // THE target found — from here every outcome (roll fail included) refers to this pool.
            trace.TargetNodeId = target.NonMinerNodeId;
            trace.OwnTiersInTarget = ownTiersJoined;
            trace.RequiredBtc = requiredAmount;

            // Step 4 — the single ladder roll (ND.6d: mode-aware — early-rush <7 slots vs normal ≥7;
            // ND.6e: a NORMAL pool inside its final 7 window days rolls the urgency table instead — a
            // participated pool always has a leading bid, so WindowCloseUnixMs is set here).
            if (ownSlotCount > 0)
            {
                bool urgent = IsAuctionInUrgencyWindow(target.WindowCloseUnixMs, nowMs);
                int probabilityPercent = ReBidProbabilityPercentFor(bestTier, occupiedSlots, urgent);
                if (probabilityPercent <= 0)
                    return CasinoBotSlotOutcome.RollDeclined; // structurally unreachable — tiers 1-3 (satisfied) and tier 10 (best-slot-10 ⇒ holds the 10th ⇒ self-eviction guard) are both excluded above; early-rush caps at tier 6
                trace.RolledTier = bestTier;
                trace.RolledProbabilityPercent = probabilityPercent;
                if (Random.Shared.Next(100) >= probabilityPercent)
                    return CasinoBotSlotOutcome.RollDeclined;
            }

            // D-ND4b.6: a raise coin-flips between the two ends of the raise band; a first donation is
            // pinned at the fixed floor. ND.6a: the principal is clamped under the half-spendable cap —
            // the RaiseMax end can exceed it even when the required RaiseMin end fits (the clamp can
            // never drop below requiredAmount, which the affordability gate above already fitted).
            decimal targetPrincipal = target.LeadingBidUnixMs == 0
                ? MinBidBtc
                : (Random.Shared.NextDouble() < 0.5
                    ? leadingAmount + RaiseMin(leadingAmount)
                    : leadingAmount + RaiseMax(leadingAmount));
            targetPrincipal = Math.Min(targetPrincipal, Math.Round(bidBudgetCap - fee, 8));

            // D-ND4b.11: additive random tail — headroom now measured against the D-ND6.8 cap, not the
            // full spendable balance, so `required + tail + fee ≤ spendable × MaxBidBalanceFraction`
            // holds for the ENTIRE outgoing amount (OQ-ND6.6's resolution).
            decimal headroom = Math.Max(0m, bidBudgetCap - fee - targetPrincipal);
            decimal tail = Math.Round((decimal)Random.Shared.NextDouble() * Math.Min(targetPrincipal, headroom), 8);
            decimal amount = Math.Round(targetPrincipal + tail, 8);
            trace.AmountBtc = amount;

            tx = BuildAndBroadcastUtxoSpend(sender, target.NonMinerAddress, amount, fee, null);
            return tx != null ? CasinoBotSlotOutcome.Donated : CasinoBotSlotOutcome.BroadcastFailed;
        }

        return CasinoBotSlotOutcome.NothingAffordable;
    }

    // ND.6b — per-visit detail for the casino_bot_bid_trace.csv row (filled progressively as
    // TryBuildCasinoBotBid advances; fields past the point it bailed out stay at their defaults).
    private sealed class CasinoBotBidTrace
    {
        public string TargetNodeId = string.Empty;    // empty until an affordable target was selected
        public string OwnTiersInTarget = string.Empty; // "4|7" style; empty = unparticipated (or no target)
        public int RolledTier;                        // 0 = no roll happened (unparticipated target, or none selected)
        public int RolledProbabilityPercent;
        public decimal RequiredBtc;
        public decimal AmountBtc;                     // 0 unless a broadcast was attempted
        public decimal FeeBtc;
        public decimal SpendableBtc;
        public decimal BidBudgetCapBtc;
    }

    private const string CasinoBotBidTracePath = "user://logs/casino_bot_bid_trace.csv";

    // ND.6b (§8.6 of the step14 plan) — probabilistic rules cannot be calibrated from gameplay feel
    // alone: one telemetry row per BOT VISIT within a donation slot (= one row per slot when no cascade
    // fires; a D-ND6.9 affordability cascade adds one row per substitute, sharing the slot index with
    // ascending `hop`). Every outcome is logged, not just successful bids — the whole point is seeing
    // the declines (ladder cadence, guard exclusions, half-balance blocks) at their real frequencies.
    private static void AppendCasinoBotBidTrace(Block block, int slot, int hop, string botNodeId, CasinoBotSlotOutcome outcome, CasinoBotBidTrace trace)
    {
        try
        {
            if (!DirAccess.DirExistsAbsolute("user://logs"))
            {
                DirAccess.MakeDirRecursiveAbsolute("user://logs");
            }

            bool exists = FileAccess.FileExists(CasinoBotBidTracePath);
            using FileAccess file = exists
                ? FileAccess.Open(CasinoBotBidTracePath, FileAccess.ModeFlags.ReadWrite)
                : FileAccess.Open(CasinoBotBidTracePath, FileAccess.ModeFlags.Write);
            if (file == null) return;

            if (exists) file.SeekEnd();
            else file.StoreLine("blockTimestampMs,blockIndex,slot,hop,botNodeId,outcome,targetNodeId,ownTiersInTarget,rolledTier,rolledProbabilityPercent,requiredBtc,amountBtc,feeBtc,spendableBtc,bidBudgetCapBtc");

            file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10:F8},{11:F8},{12:F8},{13:F8},{14:F8}",
                block.Timestamp, block.Index, slot, hop, botNodeId, CasinoBotSlotOutcomeLabel(outcome),
                trace.TargetNodeId, trace.OwnTiersInTarget, trace.RolledTier, trace.RolledProbabilityPercent,
                trace.RequiredBtc, trace.AmountBtc, trace.FeeBtc, trace.SpendableBtc, trace.BidBudgetCapBtc));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[CasinoBotBidTrace] failed: {e.Message}");
        }
    }

    private static string CasinoBotSlotOutcomeLabel(CasinoBotSlotOutcome outcome) => outcome switch
    {
        CasinoBotSlotOutcome.Donated => "donated",
        CasinoBotSlotOutcome.NoQualifyingTarget => "no-qualifying-target",
        CasinoBotSlotOutcome.NothingAffordable => "nothing-affordable",
        CasinoBotSlotOutcome.RollDeclined => "roll-declined",
        CasinoBotSlotOutcome.BroadcastFailed => "broadcast-failed",
        _ => "unknown",
    };

    // D-ND4b.6 formula (reproduces both of the developer's worked examples — step14 plan §3.4 ND.4b).
    private static decimal RaiseMin(decimal leadingBid) => Math.Max(MinBidBtc, 0.10m * leadingBid);
    private static decimal RaiseMax(decimal leadingBid) => Math.Max(2m * MinBidBtc, 0.20m * leadingBid);

    // ND.4b (D-ND4b.9) — the live "minimum to compete" figure for the player's wallet send panel: null
    // unless the address is a currently-recruitable (InAuction) non-miner, in which case it's the exact
    // floor the next donation must clear to become the new leading bid (mirrors TryCasinoBotDonateOnce's
    // own requiredAmount computation, so the wallet and the bot cycle always agree on the threshold).
    public decimal? GetMinimumCompetitiveBidBtc(string address)
    {
        EnsureInitialized();
        NonMinerDonationSummary? entry = ComputeAuctionLedger(GetPlayerLatestBlockTimestampMsStatic())
            .FirstOrDefault(s => s.NonMinerAddress == address);
        if (entry is null || entry.Status != NonMinerAuctionStatus.InAuction) return null;
        // ND.4d — always the PLAYER's own floor (this is only ever called for the player's wallet send
        // panel): OneSatoshi over the leader, not the casino-bots' RaiseMin/RaiseMax formula.
        return entry.LeadingBidUnixMs == 0 ? MinBidBtc : entry.LeadingDonorTotal + OneSatoshi;
    }

    // Cast-miner sell-flow (D-14.2, split out at ND.4a — this WAS TryMinerSellFlow, which iterated
    // MinerBots + CastMiners in fixed order and so handed every budget slot to bot_1 forever): the
    // Step-14 cast circulate mined BTC to non-miners under the historical fullness-parity budget,
    // rotating with a random start offset (TryNonMinerExchanges' fairness pattern). Their sends stay
    // economy-only — never auction bids (D-EB.7: no casino relationship; the classic bot_1..4 donate
    // via TryCasinoBotDonation instead). Recipients are simply the non-miners already introduced by the
    // historical schedule, regardless of auction status. Returns how many sends were actually created.
    private static int TryCastSellFlow(Block block, List<Block> chain, int budget)
    {
        // ND.8b.5 (D-ND8.20/D-ND8.36) — the uniform-random recipient is replaced by a WEIGHT-BIASED draw
        // among introduced companies: weight = inflow_weight × expansion step-up × dev multiplier. The
        // amount still tracks the SENDER's balance (D-ND8.36 — relative weights over the existing
        // curves, no absolute BTC targets), so era scaling stays free and D-14.2 holds.
        List<(string address, decimal weight)> recipientPool = IntroducedWeightedRecipientPool(block.Timestamp);
        if (recipientPool.Count == 0) return 0;

        IReadOnlyList<BotWalletRecord> senders = BotWalletRegistry.CastMiners;
        if (senders.Count == 0) return 0;

        int created = 0;
        int offset = Random.Shared.Next(senders.Count);
        for (int i = 0; i < senders.Count && created < budget; i++)
        {
            if (TrySellFlowSend(senders[(offset + i) % senders.Count], block, chain, recipientPool) != null)
                created++;
        }

        return created;
    }

    // One sell-flow send attempt for a single miner record — the shared gate/spend logic of the two
    // cycles above (TryCasinoBotDonation and TryCastSellFlow). Returns the broadcast transaction, or
    // null if any gate failed.
    private static Transaction? TrySellFlowSend(BotWalletRecord record, Block block, List<Block> chain,
        List<(string address, decimal weight)> recipientPool)
    {
        if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? node)) return null;

        // Warmup measured PER BOT from the block it first mined — so circulation starts a few
        // blocks after a miner actually begins mining (works for miners introduced gradually,
        // not an absolute chain index that the historical bootstrap would have already passed).
        int? firstMinedHeight = FirstBlockHeightMinedBy(record.NodeId, chain);
        if (firstMinedHeight is null) return null; // hasn't mined yet → nothing to circulate
        if (block.Index - firstMinedHeight.Value < CirculationWarmupBlocks) return null;

        decimal spendable = node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
        if (spendable < MinBotSpendableBalanceBtc) return null;

        decimal fraction = MinSendFractionDecimal
            + (decimal)Random.Shared.NextDouble() * (MaxSendFractionDecimal - MinSendFractionDecimal);
        decimal sendAmount = Math.Round(spendable * fraction, 8);
        if (sendAmount <= 0m) return null;

        // D-ND7.3 — the cast sell-flow pays the day's replayed MEAN fee (the cast IS the network's
        // average activity). 0 pre-birth.
        decimal fee = NetworkFeePolicy.MeanFeeAt(block.Timestamp);
        if (sendAmount + fee > spendable) return null; // must cover amount + fee

        string recipientAddress = DrawWeightedRecipient(recipientPool); // ND.8b.5 — ∝ effective inflow weight
        if (recipientAddress == node.WalletAddress) return null; // never send to self

        // Step 8 — UTXO spend (coin-select the miner's base-address UTXOs + change back to its base).
        return BuildAndBroadcastUtxoSpend(node, recipientAddress, sendAmount, fee, null);
    }

    // Non-miner ↔ non-miner exchange scheduler (D-14.2, new at ND.3): fills the remaining automated-tx
    // budget by circulating BTC among ACTIVE non-miner holders — real UTXO sends between real balances.
    // One send max per holder per block, random start offset for fairness. If no holder can afford a
    // send, the rate under-shoots (accepted — no synthetic filler).
    private static void TryNonMinerExchanges(Block block, int budget)
    {
        List<BotWalletRecord> holders = BotWalletRegistry.NonMinerBots.Where(b => b.IsActive).ToList();
        if (holders.Count < 2) return;

        int created = 0;
        int offset = Random.Shared.Next(holders.Count);
        for (int i = 0; i < holders.Count && created < budget; i++)
        {
            BotWalletRecord senderRecord = holders[(offset + i) % holders.Count];
            if (!SharedNodesById.TryGetValue(senderRecord.NodeId, out NodeAgent? sender)) continue;

            decimal spendable = sender.Blockchain.GetAddressSpendableBalance(sender.WalletAddress);
            if (spendable < MinBotSpendableBalanceBtc) continue;

            decimal fraction = MinSendFractionDecimal
                + (decimal)Random.Shared.NextDouble() * (MaxSendFractionDecimal - MinSendFractionDecimal);
            decimal sendAmount = Math.Round(spendable * fraction, 8);
            if (sendAmount <= 0m) continue;

            decimal fee = NetworkFeePolicy.MedianFeeAt(block.Timestamp); // D-ND7.3 — median, 0 pre-birth
            if (sendAmount + fee > spendable) continue;

            BotWalletRecord recipient = holders[Random.Shared.Next(holders.Count)];
            if (recipient.NodeId == senderRecord.NodeId || recipient.Address == sender.WalletAddress) continue;

            if (BuildAndBroadcastUtxoSpend(sender, recipient.Address, sendAmount, fee, null) != null)
                created++;
        }
    }

    private static List<string> ActiveNonMinerAddresses() =>
        BotWalletRegistry.NonMinerBots.Where(b => b.IsActive).Select(b => b.Address).ToList();

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
            // Base address OR any derived address the node's wallet owns (change rotation / Satoshi's
            // coinbase spread) — an address stays owned forever, so naming the node is always right.
            if (node.WalletAddress == address
                || (node.ReceiveWallet?.OwnedAddresses.Contains(address) ?? false))
            {
                return node.NodeId;
            }
        }

        return address.Length > 12 ? address[..12] + "…" : address;
    }

    // ND.8b.1 — one-line company identity for UI display (BlockExplorer's Enroll Mode,
    // AuctioningCompanyDetails): "Display Name (CB#, market_category)". Falls back to the legacy
    // NodeId if this non-miner has no roster match (should never happen — see the summary's own
    // CompanyId comment).
    public static string DescribeCompany(NonMinerDonationSummary summary) =>
        string.IsNullOrEmpty(summary.CompanyId)
            ? summary.NonMinerNodeId
            : $"{summary.CompanyDisplayName} ({summary.CompanyCurrencyBand}, {summary.CompanyMarketCategory})";

    // Referral auction ledger — Step 14 EB.2 rework (D-EB.4/5/6/7). Fully DERIVED from the canonical
    // chain — no persisted state. Non-miners are introduced along the historical active-address curve
    // from Market Birth; each bot's 6-in-game-month window starts counting at its FIRST QUALIFYING BID.
    // ONLY nodes with a real casino relationship (bet-driven mining — the player AND the classic casino-
    // miner-bots bot_1..4) can bid; every other automated transfer (the growing Step-14 CAST miners,
    // ND.2 — they mine via drained attempts, never bet — and any non-miner↔non-miner exchange) is
    // economy that funds the wallet without starting, leading, or winning an auction. A never-bid-on bot
    // stays recruitable indefinitely; every resolved auction has a real winner. Coinbase txs excluded.
    // "now" = latest block timestamp.
    public IReadOnlyList<NonMinerDonationSummary> GetNonMinerAuctionLedger()
    {
        EnsureInitialized();
        long nowMs = SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? p) && p.Blockchain.Chain.Count > 0
            ? p.Blockchain.Chain[^1].Timestamp
            : 0;
        return ComputeAuctionLedger(nowMs);
    }

    // ND.8b.2 (D-ND8.14) — a PURE, read-only lookup of a founded company's stock-token distribution, for
    // the AuctioningCompanyDetails scene's summary view (a stand-in until ND.8b.4's CompanyDetails scene
    // replaces it). The scene never triggers founding itself (FoundCompany already ran it live, exactly
    // once, from HandleMinedBlock) — this just reads the recorded result. Null if this non-miner hasn't
    // resolved/founded yet.
    public CompanyFounding? GetCompanyFounding(string nonMinerAddress)
    {
        EnsureInitialized();
        NonMinerDonationSummary? summary = GetNonMinerAuctionLedger()
            .FirstOrDefault(s => s.NonMinerAddress == nonMinerAddress && s.Status == NonMinerAuctionStatus.Resolved);
        if (summary is null) return null;

        return _companyFoundings.TryGetValue(summary.NonMinerNodeId, out CompanyFounding? founding) ? founding : null;
    }

    // D-EB.7 / ND.5 — the qualifying-bidder identity split, shared by ComputeAuctionLedger (the ratchet
    // walk) and SettleResolvedAuction (D-ND5.7's per-donor payout routing): the PLAYER (base + derived
    // change addresses) is returned separately from a per-address map to the owning CASINO-MINER-BOT's
    // nodeId (bot_1..4 only — BotWalletRegistry.MinerBots), so a caller can tell not just "is this a
    // qualifying bidder" but WHICH participant a given donor address belongs to.
    private static (HashSet<string> playerAddresses, Dictionary<string, string> botNodeIdByAddress) BuildAuctionBidderIdentity(NodeAgent? player)
    {
        var playerAddresses = new HashSet<string>();
        if (player is not null)
        {
            if (player.ReceiveWallet != null)
            {
                playerAddresses.UnionWith(player.ReceiveWallet.OwnedAddresses);
            }
            playerAddresses.Add(player.WalletAddress);
        }

        var botNodeIdByAddress = new Dictionary<string, string>();
        foreach (BotWalletRecord casinoMinerBot in BotWalletRegistry.MinerBots)
        {
            if (SharedNodesById.TryGetValue(casinoMinerBot.NodeId, out NodeAgent? botNode))
            {
                botNodeIdByAddress[botNode.WalletAddress] = casinoMinerBot.NodeId;
            }
        }

        return (playerAddresses, botNodeIdByAddress);
    }

    // Step 14 (ND.5, D-ND5.3/5.4) — the value-ordered top-10 tracked donation pool: processes `bids`
    // (already qualifying + chronological) in order, keeping the 10 largest by BTC principal. A tie with
    // the current smallest never evicts (first-in stays) — implemented by scanning for the FIRST minimum
    // (strict `<` only), so an existing entry is only ever displaced by a strictly smaller `<` comparison.
    private static List<TrackedDonation> ComputeTrackedDonationPool(List<(string donor, decimal amount, long ts, long seq)> bids, long nowMs)
    {
        var tracked = new List<(string donor, decimal amount, long ts, long seq)>();
        foreach ((string donor, decimal amount, long ts, long seq) bid in bids)
        {
            if (tracked.Count < MaxTrackedDonations)
            {
                tracked.Add(bid);
                continue;
            }

            int minIndex = 0;
            for (int i = 1; i < tracked.Count; i++)
            {
                if (tracked[i].amount < tracked[minIndex].amount) minIndex = i;
            }
            if (bid.amount > tracked[minIndex].amount)
            {
                tracked[minIndex] = bid; // strictly larger — evicts the current smallest
            }
            // else: not strictly larger (< or ==) — never tracked; stays the non-miner's own property forever.
        }

        // Corrected 2026-07-11 (developer playtest feedback): each row's displayed SC value is LIVE — the
        // CURRENT (today's) BTC/SC price, recomputed fresh on every auto-refresh as the game clock
        // advances — not frozen at the donation's own day. This matches the Enroll Mode leading-bid
        // display's intent. Still purely informational; settlement (D-ND5.6) is UNCHANGED and always
        // revalues at the closing date instead — never conflate the two.
        DateTime nowLocal = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).LocalDateTime;
        decimal? livePrice = _marketData?.GetEffectivePriceUsd(nowLocal);
        return tracked
            .Select(t => new TrackedDonation
            {
                DonorAddress = t.donor,
                AmountBtc = t.amount,
                TimestampMs = t.ts,
                CurrentValueSc = livePrice is decimal price ? Math.Round(t.amount * price, 8) : null
            })
            .ToList();
    }

    private static List<NonMinerDonationSummary> ComputeAuctionLedger(long nowMs)
    {
        List<BotWalletRecord> nonMiners = BotWalletRegistry.NonMinerBots.ToList();
        var donations = new Dictionary<string, List<(string donor, decimal amount, long ts, long seq)>>();
        foreach (BotWalletRecord b in nonMiners)
        {
            donations[b.Address] = new List<(string, decimal, long, long)>();
        }

        SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player);

        // Identity is resolved BEFORE the chain walk so the player's donations can be canonicalized to
        // the base address as they are recorded (2026-07-14 playtest fix): a bid whose coin selection
        // spent a derived CHANGE-address UTXO carries that change address in Inputs[0] (the tx.Sender
        // shim), and recording it raw made the same player appear as a second, unnamed donor — raw
        // address in Enroll Mode / details rows, split DonorCount, split settlement payout rows. All
        // mechanics already treated those bids as the player (qualification, the ND.4d one-satoshi
        // floor, D-ND5.7 payout routing — each checks the full playerAddresses set), so this only
        // unifies the recorded identity. Single-address participants (bot_1..4) need no equivalent.
        (HashSet<string> playerAddresses, Dictionary<string, string> botNodeIdByAddress) = BuildAuctionBidderIdentity(player);

        long seq = 0;
        if (player is not null)
        {
            foreach (Block block in player.Blockchain.Chain)
            {
                foreach (Transaction tx in block.Transactions)
                {
                    if (tx.Sender == BlockchainService.CoinbaseSender) continue;
                    // D-ND4b.13 — the running append-order counter: two txs sharing the same block
                    // timestamp still get a resolvable "earlier" ordering from this, satisfying the
                    // same-block tie-break without any new persisted/schema field.
                    if (donations.TryGetValue(tx.Recipient, out List<(string, decimal, long, long)>? list))
                    {
                        string donor = playerAddresses.Contains(tx.Sender) ? player.WalletAddress : tx.Sender;
                        list.Add((donor, tx.Amount, block.Timestamp, seq));
                    }
                    seq++;
                }
            }
        }

        // D-EB.7 (corrected 2026-07-09) — the qualifying-bidder set: the PLAYER (base + derived change
        // addresses) PLUS the classic CASINO-MINER-BOTS (bot_1..4, BotWalletRegistry.MinerBots) — every
        // node whose mining REQUIRES casino play (bet-driven, HardwareRate-locked), exactly like the
        // player. This is the real eligibility test, not "is the player": bot_1..4 already have a
        // casino relationship today (StartBots/BuildBotConfigs — the same hardware-credit betting
        // sessions as the player), so their sell-flow donations (TryCasinoBotDonation) count as bids without
        // needing new machinery. The Step-14 CAST miners (BotWalletRegistry.CastMiners, ND.2) do NOT
        // qualify — they mine via drained attempts (founder-style, concurrent with the player's time
        // advancement), never place a bet, and so never form a casino relationship; their sell-flow
        // donations stay economy-only, exactly like the non-miner↔non-miner exchanges. Single-address
        // bots have no ReceiveWallet (OQ-8.2 — no stored seed), so only WalletAddress applies.
        // ND.4d — playerAddresses is split out from qualifyingBidders so the ratchet walk below can tell
        // WHO is bidding: the player's own raises clear at OneSatoshi over the leader, everyone else's
        // (the casino-bots) still need the full RaiseMin/RaiseMax jump. (The identity sets themselves
        // are built above the chain walk, where the player-donor canonicalization needs them.)
        var qualifyingBidders = new HashSet<string>(playerAddresses);
        qualifyingBidders.UnionWith(botNodeIdByAddress.Keys);

        var result = new List<NonMinerDonationSummary>();
        for (int i = 0; i < nonMiners.Count; i++)
        {
            BotWalletRecord b = nonMiners[i];
            List<(string donor, decimal amount, long ts, long seq)> list = donations[b.Address];

            var summary = new NonMinerDonationSummary
            {
                NonMinerNodeId = b.NodeId,
                NonMinerAddress = b.Address,
                TotalReceived = list.Sum(d => d.amount),          // ALL funding (economy + bids) — display
                DonorCount = list.Select(d => d.donor).Distinct().Count()
            };

            // ND.8b.1 (D-ND8.37) — non_miner_{i+1} (BotWalletRegistry's fixed creation order) always
            // pairs with CompanyRoster.Auctionable[i]; see BtcNetworkDataService.ComputeNonMinerIntroSchedule.
            if (CompanyRoster.ForNonMinerIndex(i) is CompanyRecord company)
            {
                summary.CompanyId = company.CompanyId;
                summary.CompanyDisplayName = company.DisplayName;
                summary.CompanyCurrencyBand = company.CurrencyBand;
                summary.CompanyMarketCategory = company.MarketCategory;
                summary.CompanyAppearanceDateLocal = company.AppearanceDateLocal;
                summary.CompanyAnchor = company.Anchor;
            }

            // D-EB.4 — introduction by the pushed historical schedule (empty schedule / index beyond it
            // ⇒ not introduced; the schedule arrives from BtcNetworkDataService before any gameplay).
            long introMs = i < _nonMinerIntroScheduleMs.Length ? _nonMinerIntroScheduleMs[i] : long.MaxValue;
            if (nowMs < introMs)
            {
                summary.Status = NonMinerAuctionStatus.NotIntroduced;
                result.Add(summary);
                continue;
            }
            summary.IntroUnixMs = introMs;

            // D-EB.6/7 — qualifying bids: donations from a QUALIFYING bidder confirmed at/after the
            // introduction (earlier sends were charity to a bot not yet in the referral program).
            List<(string donor, decimal amount, long ts, long seq)> bids =
                list.Where(d => d.ts >= introMs && qualifyingBidders.Contains(d.donor))
                    .OrderBy(d => d.ts).ThenBy(d => d.seq)
                    .ToList();

            // D-ND5.3/5.4 — the tracked pool draws from every qualifying bid regardless of leader status
            // (win-or-lose, OQ-ND5.1) or auction phase; computed unconditionally so it's populated on both
            // the empty-bids early return below and the full ratchet walk.
            summary.TrackedDonations = ComputeTrackedDonationPool(bids, nowMs);

            if (bids.Count == 0)
            {
                // Recruitable indefinitely: the countdown has not started (LeadingBidUnixMs stays 0).
                summary.Status = NonMinerAuctionStatus.InAuction;
                result.Add(summary);
                continue;
            }

            // D-ND4b.6/7/8/12/13 — ascending-auction ratchet, replacing the old cumulative-sum leader
            // (TopDonor). Bids are grouped by their shared block timestamp (bids landing in the SAME
            // block are evaluated against the SAME starting leader, never against each other in
            // sequence — the D-ND4b.11 same-block tie-break: both execute, only the higher wins, exact
            // ties broken by earliest seq via the group's own iteration order). Each group's floor is
            // the current leader's principal + its minimum raise (MinBidBtc if there is no leader yet);
            // the first bid in the group to clear a strictly-higher floor than the running best becomes
            // the new leader. A gap of more than AuctionWindowMs between the leader's own bid and the
            // next candidate group means the window ALREADY closed there — permanently (D-ND4b.12): no
            // bid after that point can revive or re-win a resolved auction, however large.
            (string donor, decimal amount, long ts, long seq)? leader = null;
            long? resolvedAtMs = null;
            foreach (IGrouping<long, (string donor, decimal amount, long ts, long seq)> group in bids.GroupBy(d => d.ts).OrderBy(g => g.Key))
            {
                if (resolvedAtMs.HasValue) break;
                if (leader.HasValue && group.Key > leader.Value.ts + AuctionWindowMs)
                {
                    resolvedAtMs = leader.Value.ts + AuctionWindowMs;
                    break;
                }

                (string donor, decimal amount, long ts, long seq)? best = null;
                foreach ((string donor, decimal amount, long ts, long seq) d in group)
                {
                    // ND.4d — the floor a candidate must clear depends on WHO is bidding, not just on
                    // the current leader: the player only needs to clear the leader by one satoshi;
                    // everyone else (the casino-bots) still needs the full RaiseMin jump. Pre-leader,
                    // both start at the same fixed MinBidBtc opening floor (D-ND4b.5, unaffected).
                    decimal floor = !leader.HasValue
                        ? MinBidBtc
                        : playerAddresses.Contains(d.donor)
                            ? leader.Value.amount + OneSatoshi
                            : leader.Value.amount + RaiseMin(leader.Value.amount);
                    if (d.amount < floor) continue;
                    if (best is null || d.amount > best.Value.amount)
                    {
                        best = d;
                    }
                }
                if (best.HasValue)
                {
                    leader = best;
                }
            }

            if (!leader.HasValue)
            {
                // No bid ever cleared even the initial MinBidBtc floor (the casino-bot cycle always
                // meets it — this covers a player sending less than 0.1 BTC as their very first send).
                summary.Status = NonMinerAuctionStatus.InAuction;
                result.Add(summary);
                continue;
            }

            summary.LeadingDonorAddress = leader.Value.donor;
            summary.LeadingDonorTotal = leader.Value.amount;
            // Corrected 2026-07-11 (developer correction — "day-of-donation" was a writing mistake in the
            // original D-ND4b.10 spec that leaked into the docs; NOTHING in this system displays a frozen
            // historical-day value — it is ALWAYS priced as of NOW): live/current SC value, recomputed
            // fresh on every call as the game clock advances, not frozen at the leading bid's own day.
            summary.LeadingDonorScValue = _marketData?.GetEffectivePriceUsd(
                    DateTimeOffset.FromUnixTimeMilliseconds(nowMs).LocalDateTime) is decimal price
                ? Math.Round(leader.Value.amount * price, 8)
                : null; // null before Market Birth (no price data yet)
            summary.LeadingBidUnixMs = leader.Value.ts;
            summary.WindowCloseUnixMs = resolvedAtMs ?? (leader.Value.ts + AuctionWindowMs);

            if (resolvedAtMs.HasValue || nowMs >= summary.WindowCloseUnixMs)
            {
                // ≥1 qualifying bid necessarily exists here — every resolved auction has a real winner.
                summary.Status = NonMinerAuctionStatus.Resolved;
                summary.WinnerAddress = leader.Value.donor;
            }
            else
            {
                summary.Status = NonMinerAuctionStatus.InAuction;
            }

            result.Add(summary);
        }

        return result;
    }

    // (FirstLiveBlockTimestamp + InAuctionNonMinerAddresses retired at Step 14 EB.2 — introduction is
    // schedule-driven, and the sell-flow's recipients don't depend on auction status (funding flows
    // before, during, and after a window); whether a send COUNTS as a bid is decided entirely by
    // ComputeAuctionLedger's qualifying-bidder set: see IntroducedWeightedRecipientPool / D-EB.4/7.)

    // ── ND.8b.5 (D-ND8.20/D-ND8.36) — per-company, historically-anchored inflow ──────────────────────
    // The recipient pool lists ACTIVE non-miners already introduced by the historical schedule at the
    // given time (auction status irrelevant — funding continues before, during, and after a window;
    // empty before Market Birth, which is historically exact). ND.8b.5 attaches each company's effective
    // inflow weight, retiring the old uniform IntroducedActiveNonMinerAddresses list (this pool's only
    // caller was the sell-flow).

    // Dev-tunable per-company inflow multipliers (D-ND8.20 option 1, default 1.0 — the "nudge the
    // average" lever), keyed by companyId; surfaced as DEV knobs in WorldEconomy (D-ND8.25, ND.8b.6).
    // Persisted in BlockchainStateSnapshot beside the governance state (block-commit rule).
    private static readonly Dictionary<string, decimal> _companyInflowMultipliers = new();

    public static decimal GetCompanyInflowMultiplier(string companyId) =>
        _companyInflowMultipliers.TryGetValue(companyId, out decimal m) ? m : 1m;

    public static void SetCompanyInflowMultiplier(string companyId, decimal multiplier)
    {
        multiplier = Math.Clamp(multiplier, 0m, 100m);
        if (multiplier == 1m) _companyInflowMultipliers.Remove(companyId);
        else _companyInflowMultipliers[companyId] = multiplier;
    }

    // D-ND8.36 — a company's effective inflow weight at a given date:
    // inflow_weight × (expansion_multiplier once now ≥ expansion_date — a PERMANENT step-up, v1
    // semantics) × the dev multiplier. Companies missing from the roster (a non-canon pool-size
    // mismatch) fall back to weight 1 so the flow never starves.
    public static decimal EffectiveInflowWeight(string? companyId, long nowMs)
    {
        CompanyRecord? record = companyId is null ? null : CompanyRoster.ByCompanyId(companyId);
        if (record is not CompanyRecord r)
        {
            return 1m;
        }

        decimal weight = Math.Max(1, r.InflowWeight);
        if (r.ExpansionDateLocal is DateTime expansion && r.ExpansionMultiplier is decimal multiplier
            && nowMs >= new DateTimeOffset(expansion).ToUnixTimeMilliseconds())
        {
            weight *= multiplier;
        }

        return weight * GetCompanyInflowMultiplier(r.CompanyId);
    }

    // The sell-flow recipient pool with each introduced company's effective weight attached (the
    // ND.8b.5 replacement for the retired plain address list).
    private static List<(string address, decimal weight)> IntroducedWeightedRecipientPool(long nowMs)
    {
        var active = new HashSet<string>(ActiveNonMinerAddresses());
        return ComputeAuctionLedger(nowMs)
            .Where(s => s.Status != NonMinerAuctionStatus.NotIntroduced && active.Contains(s.NonMinerAddress))
            .Select(s => (s.NonMinerAddress, EffectiveInflowWeight(s.CompanyId, nowMs)))
            .Where(p => p.Item2 > 0m)
            .ToList();
    }

    private static string DrawWeightedRecipient(List<(string address, decimal weight)> pool)
    {
        decimal total = pool.Sum(p => p.weight);
        if (total <= 0m)
        {
            return pool[Random.Shared.Next(pool.Count)].address;
        }

        decimal roll = (decimal)Random.Shared.NextDouble() * total;
        foreach ((string address, decimal weight) in pool)
        {
            roll -= weight;
            if (roll < 0m)
            {
                return address;
            }
        }

        return pool[^1].address; // rounding tail
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
            return "Node not found.";

        (Transaction? tx, Block? block) = node.Blockchain.GetTransaction(transactionId);
        Transaction? resolved = tx ?? node.Blockchain.GetPendingTransaction(transactionId);
        if (resolved is null)
            return "Transaction not found in confirmed or pending sets.";

        return FormatTxDetail(resolved, block);
    }

    private static string FormatTxDetail(Transaction tx, Block? block)
    {
        bool isCoinbase = tx.IsCoinbase;
        var sb = new StringBuilder();
        sb.AppendLine($"TxId: {tx.TransactionId}{(isCoinbase ? "  [COINBASE]" : "")}");
        if (block != null) { sb.AppendLine($"Block: {block.Index}"); sb.AppendLine("Status: confirmed"); }
        else                  sb.AppendLine("Status: pending");
        if (!isCoinbase)
        {
            sb.AppendLine($"Fee: {tx.Fee:F8} BTC");
            sb.AppendLine($"Inputs ({tx.Inputs.Count}):");
            foreach (TxInput inp in tx.Inputs)
                sb.AppendLine($"  {inp.Address}");
        }
        sb.AppendLine($"Outputs ({tx.Outputs.Count}):");
        foreach (TxOutput txOut in tx.Outputs)
            sb.AppendLine($"  {txOut.Address}  {txOut.Amount:F8} BTC");
        return sb.ToString().TrimEnd();
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
    // Step 8 — a node's address book: base + every derived (change/coinbase) address it owns, each with its
    // confirmed balance and the Unix-ms timestamp of the block where it FIRST appeared (its "creation" date).
    // Ordered base-first, then by creation time. `createdUnixMs` is 0 if the address has no on-chain output yet.
    public IReadOnlyList<(string address, decimal confirmed, bool isBase, long createdUnixMs)> GetNodeAddressBook(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node)
            || !SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            return [];

        Dictionary<string, long> firstSeen = BuildAddressFirstSeenMap(player);
        long Created(string a) => firstSeen.TryGetValue(a, out long ts) ? ts : 0L;

        var result = new List<(string, decimal, bool, long)>
        {
            (node.WalletAddress, player.Blockchain.GetAddressData(node.WalletAddress).AddressBalance, true, Created(node.WalletAddress))
        };
        if (node.ReceiveWallet != null)
        {
            var derived = new List<(string, decimal, bool, long)>();
            foreach (string addr in node.ReceiveWallet.OwnedAddresses)
                if (addr != node.WalletAddress)
                    derived.Add((addr, player.Blockchain.GetAddressData(addr).AddressBalance, false, Created(addr)));
            derived.Sort((a, b) => a.Item4.CompareTo(b.Item4)); // oldest-first by creation time
            result.AddRange(derived);
        }
        return result;
    }

    // address → Unix-ms timestamp of the first block in which it appears as an output (its creation time).
    private static Dictionary<string, long> BuildAddressFirstSeenMap(NodeAgent player)
    {
        var firstSeen = new Dictionary<string, long>();
        foreach (Block block in player.Blockchain.Chain)
            foreach (Transaction tx in block.Transactions)
                foreach (TxOutput output in tx.Outputs)
                    if (!string.IsNullOrEmpty(output.Address) && !firstSeen.ContainsKey(output.Address))
                        firstSeen[output.Address] = block.Timestamp;
        return firstSeen;
    }

    // Step 8 — a node's confirmed transaction history from ITS perspective (for the wallet's "Transactions"
    // panel): each confirmed tx that touches one of its owned addresses, classified mined / received / sent,
    // with the block timestamp, the net amount, and the counterparty. Internal change (an owned output of the
    // node's own send) is netted out, not listed. Newest first.
    // `recipients` (Step 13 / SW.3 display fix) = distinct external payees of a "sent" row, so a multi-output
    // send (a casino pool distribution pays every contributor in ONE tx) renders as "sent X to addr (+N more)"
    // instead of looking like the whole amount went to the first payee. `memo` = the tx's display label
    // (InputDataText — the swap desk tags its txs "swap:…" so wallet panels can color them apart).
    public IReadOnlyList<(long unixMs, string kind, decimal amount, string counterparty, int recipients, string memo)> GetNodeTransactionHistory(string nodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(nodeId, out NodeAgent? node)
            || !SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            return [];

        var owned = new HashSet<string>(node.ReceiveWallet?.OwnedAddresses ?? Enumerable.Empty<string>()) { node.WalletAddress };
        var result = new List<(long, string, decimal, string, int, string)>();

        foreach (Block block in player.Blockchain.Chain)
            foreach (Transaction tx in block.Transactions)
            {
                string memo = tx.InputDataText ?? string.Empty;
                bool isSender = tx.Inputs.Any(i => owned.Contains(i.Address));
                if (isSender)
                {
                    // We spent — the "amount" is what left the wallet (outputs to addresses we DON'T own).
                    var external = tx.Outputs.Where(o => !owned.Contains(o.Address)).ToList();
                    decimal sent = external.Sum(o => o.Amount);
                    if (sent <= 0m) continue; // pure self-consolidation (all outputs owned) → not user-facing
                    string payee = external[0].Address;
                    int recipients = external.Select(o => o.Address).Distinct().Count();
                    result.Add((block.Timestamp, "sent", sent, payee, recipients, memo));
                }
                else
                {
                    decimal received = tx.Outputs.Where(o => owned.Contains(o.Address)).Sum(o => o.Amount);
                    if (received <= 0m) continue; // not involved
                    if (tx.IsCoinbase)
                        result.Add((block.Timestamp, "mined", received, "coinbase", 1, memo));
                    else
                        result.Add((block.Timestamp, "received", received, tx.Inputs.Count > 0 ? tx.Inputs[0].Address : "—", 1, memo));
                }
            }

        result.Sort((a, b) => b.Item1.CompareTo(a.Item1)); // newest first
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

    // Returns all confirmed transactions involving address (spent as an input owner, or received in ANY
    // output incl. change), ordered by block index descending. Scans the full player chain. Iterates the full
    // Inputs/Outputs lists, not the Sender/Recipient shims (which only expose vout/vin 0 — Step 8 bug fix).
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
                if (tx.Inputs.Any(i => i.Address == address) || tx.Outputs.Any(o => o.Address == address))
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

    // Step 14 (ND.2) — registers a freshly-spawned cast miner mid-session (its BotWalletRegistry record
    // must already exist). Same shape as RegisterPassphraseWallet: sync the canonical chain into the new
    // node so its candidate blocks build on the live tip. Idempotent per nodeId.
    public bool RegisterCastMinerNode(string nodeId)
    {
        EnsureInitialized();
        if (SharedNodesById.ContainsKey(nodeId))
        {
            return true;
        }

        BotWalletRecord? record = BotWalletRegistry.GetBot(nodeId);
        if (record?.HasFullWallet != true)
        {
            GD.PushWarning($"[NetworkRoot] Cast miner '{nodeId}' has no registry wallet — not registered.");
            return false;
        }

        var node = new NodeAgent(nodeId, record.Address, record.SigningPublicKeyBase64!, record.SigningPrivateKeyBase64!, record.Secp256k1PublicKeyBase64!);
        if (SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            node.Blockchain.TryReplaceChain(player.Blockchain.Chain, player.Blockchain.PendingTransactions);
        SharedNetwork.RegisterNode(node);
        SharedNodesById[nodeId] = node;
        return true;
    }

    // Step 14 (ND.2) — lazily registers a GHOST miner for the invisible mass's blocks (D-14.9): a
    // session-transient NodeAgent with a random one-off wallet. Deliberately NOT persisted anywhere —
    // the keys die with the process, so ghost-mined coinbases are frozen forever (D-14.11); only the
    // pseudonym survives, stamped into Block.MinedByNodeId.
    public void EnsureGhostNodeRegistered(string ghostId)
    {
        EnsureInitialized();
        if (SharedNodesById.ContainsKey(ghostId))
        {
            return;
        }

        var node = new NodeAgent(ghostId);
        if (SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player))
            node.Blockchain.TryReplaceChain(player.Blockchain.Chain, player.Blockchain.PendingTransactions);
        SharedNetwork.RegisterNode(node);
        SharedNodesById[ghostId] = node;
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

    // Single-pass scan of EVERY address appearing on a node's confirmed chain — every output's recipient
    // (incl. change outputs at vout ≥ 1) and every input's owner. Static + no EnsureInitialized so it is safe
    // to call from inside EnsureInitialized (RescanFounderReceiveWallets) without re-entrancy.
    //
    // CRITICAL (Step 8 bug fix): must iterate the full Inputs/Outputs lists, NOT the legacy Sender/Recipient
    // shims (which expose only Inputs[0]/Outputs[0]). A CHANGE output lives at Outputs[1], so the old shim
    // scan never saw change addresses — after a restart the rescan couldn't mark them owned, and a node's
    // change-held funds vanished from its wallet (the funds stay on-chain, just unattributed). This also reset
    // the receive frontier, causing change-address reuse. Satoshi was masked because his funds sit on coinbase
    // recipients (Outputs[0]); change-rotating nodes (player, Hal, Hearn, casino) were the ones that broke.
    private static HashSet<string> BuildUsedAddressSet(NodeAgent player)
    {
        var used = new HashSet<string>();
        foreach (Block block in player.Blockchain.Chain)
            foreach (Transaction tx in block.Transactions)
            {
                foreach (TxOutput output in tx.Outputs)
                    if (!string.IsNullOrEmpty(output.Address))
                        used.Add(output.Address);
                foreach (TxInput input in tx.Inputs)
                    if (!string.IsNullOrEmpty(input.Address) && input.Address != BlockchainService.CoinbaseSender)
                        used.Add(input.Address);
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
            BestMinerStreak = _bestMinerStreak,
            CompanyFoundings = new Dictionary<string, CompanyFounding>(_companyFoundings),
            CompanyGovernance = new Dictionary<string, CompanyGovernanceState>(_companyGovernance),
            BotGovernancePreferences = new Dictionary<string, BotGovernancePreference>(_botGovernancePreferences),
            CompanyInflowMultipliers = new Dictionary<string, decimal>(_companyInflowMultipliers)
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

    // Step 8 (clean reset) + Step 13 (TL.1, D-13.7) — the persisted world is wiped whenever EITHER the
    // format version OR the timeline tag (TimelineConfig.Tag) no longer matches what's stamped on disk.
    // A stale format version means old account-model data with no UTXO linkage; a stale timeline tag means
    // a save built under the other calendar (canon vs. the DEV alt-timeline simulacrum) — a canon/alt
    // hybrid would be corrupt (e.g. a 2009 chain tip paired with a 2010 fee-activation date). Both triggers
    // share the SAME complete delete list — no divergent partial resets (D-13.7 extends the Step-8 list
    // with the full Step-11/12 state set + bet-history chunks, which are no longer spared as "cosmetic":
    // canon-dated rows would sit before an alt world's genesis and permanently pollute its since-deposit/
    // since-recharge stat scopes). Idempotent: re-stamps both on exit so it runs once per change.
    //
    // The timeline stamp file not existing at all (upgrading from a pre-TL.1 save) is NOT itself treated as
    // a mismatch — the timeline concept didn't exist yet, so an existing save is assumed canon-compatible
    // and the stamp is simply backfilled, rather than surprise-wiping a developer's current playthrough the
    // moment this phase lands.
    //
    // ORDERING (the second TL.3 lesson): this guard MUST run before ANY service/repository loads its
    // user:// file into a static cache — a file deleted AFTER being loaded lives on in memory and gets
    // re-persisted later (exactly how alt-world hardware/pool state survived the first canon relaunch:
    // CalendarTimeService (autoload #2) → WalletInitializationService.EnsureAll() loaded them long before
    // BlockSessionCheckpointService reached NetworkRoot). WorldGuardService — the FIRST autoload in
    // project.godot — calls RunWorldCompatibilityGuard() so the wipe precedes every load, and the
    // EnsureInitialized call site remains as an idempotent safety net.
    private static bool _worldGuardRan;

    public static void RunWorldCompatibilityGuard()
    {
        if (_worldGuardRan)
        {
            return;
        }
        _worldGuardRan = true;
        ResetWorldIfIncompatible();
    }

    // MAINTENANCE RULE (learned at TL.3, 2026-07-07): every NEW persisted world-state file MUST be added
    // to this delete list when it ships — the TL.3 canon-relaunch verification found hardware credits,
    // casino-pool shares, and swap-desk state leaking across the timeline wipe because their files
    // (hardware_allocation / casino_pool_state / casino_coin_swap_state — the last one created AFTER this
    // list was written) were never listed. Deliberately SPARED, by design (identity/personal data, not
    // world state): the wallet seed/address files (wallet_state, casino/satoshi/hal/mike_hearn_wallet_state,
    // bot_wallet_registry — a fresh bootstrap reuses the same identities), saved_betting_strategies,
    // notepad_notes, and wordlist_256. See ProjectDesignManual Ch. 35 (§35.1).
    private static void ResetWorldIfIncompatible()
    {
        int storedVersion = 0;
        if (FileAccess.FileExists(WorldVersionPath))
        {
            using FileAccess vf = FileAccess.Open(WorldVersionPath, FileAccess.ModeFlags.Read);
            int.TryParse(vf.GetAsText().Trim(), out storedVersion);
        }
        bool formatChanged = storedVersion != WorldFormatVersion;

        bool timelineStampExists = FileAccess.FileExists(WorldTimelinePath);
        string storedTimelineTag = TimelineConfig.Tag;
        if (timelineStampExists)
        {
            using FileAccess tf = FileAccess.Open(WorldTimelinePath, FileAccess.ModeFlags.Read);
            storedTimelineTag = tf.GetAsText().Trim();
        }
        bool timelineChanged = timelineStampExists && storedTimelineTag != TimelineConfig.Tag;

        if (!formatChanged && !timelineChanged)
        {
            if (!timelineStampExists)
            {
                using FileAccess backfill = FileAccess.Open(WorldTimelinePath, FileAccess.ModeFlags.Write);
                backfill?.StoreString(TimelineConfig.Tag);
            }
            return;
        }

        GD.Print($"[NetworkRoot] World reset triggered (format {storedVersion} → {WorldFormatVersion}" +
                 (timelineChanged ? $", timeline '{storedTimelineTag}' → '{TimelineConfig.Tag}'" : string.Empty) +
                 "): resetting chain + clock + financial state (clean reset).");

        DeleteIfExists(StatePath);
        DeleteIfExists("user://block_session_checkpoint.json");
        DeleteIfExists("user://calendar_state.json");
        DeleteIfExists("user://bankroll_state.json");
        DeleteIfExists("user://principal_balance_state.json");
        DeleteIfExists("user://bankroll_program_state.json");
        DeleteIfExists("user://casino_sc_balance_state.json");
        DeleteIfExists("user://player_bank_account_state.json");
        DeleteIfExists("user://casino_client_ledger.json");
        DeleteIfExists("user://bet_history.jsonl");

        // TL.3 gap fix (2026-07-07): hardware/pool/swap world state — found leaking across the timeline
        // wipe during the canon-relaunch verification (alt-bought hardware, bot pool shares, and a casino
        // pool ledger referencing blocks of the wiped chain all survived into the fresh canon world).
        DeleteIfExists("user://hardware_allocation.json");
        DeleteIfExists("user://casino_pool_state.json");
        DeleteIfExists("user://casino_coin_swap_state.json");
        DeleteIfExists("user://sc_monetary_ledger.json"); // ND.8c — added WITH the feature (the TL.3 maintenance rule)

        // DEV trace telemetry: not player-visible, but rows dated under the other timeline would make the
        // traces unreadable (founders_trace is actively used to verify founder pacing) — start them fresh.
        DeleteIfExists("user://logs/difficulty_trace.csv");
        DeleteIfExists("user://logs/founders_trace.csv");
        DeleteIfExists("user://logs/swap_desk_trace.csv");
        DeleteIfExists("user://logs/network_population_trace.csv");
        DeleteIfExists(CompanyFoundingTracePath); // ND.6b — was missing since ND.5 (same reasoning as the others); ND.8b.2 renamed the file
        DeleteIfExists(CompanyGovernanceTracePath); // ND.8b.3 — added WITH the feature (the TL.3/ND.6b rule)
        DeleteIfExists(CasinoBotBidTracePath);

        // The monthly block history chunks and the bet-history chunks are likewise wiped so the explorer
        // and the betting stats rebuild from a pristine world.
        string blocksDirAbs = ProjectSettings.GlobalizePath(BlockchainDir);
        if (System.IO.Directory.Exists(blocksDirAbs))
            foreach (string staleFile in System.IO.Directory.GetFiles(blocksDirAbs, "blocks-*.json"))
                try { System.IO.File.Delete(staleFile); } catch { /* best-effort */ }

        string userDirAbs = ProjectSettings.GlobalizePath("user://");
        if (System.IO.Directory.Exists(userDirAbs))
            foreach (string staleFile in System.IO.Directory.GetFiles(userDirAbs, "bet_history_*.jsonl"))
                try { System.IO.File.Delete(staleFile); } catch { /* best-effort */ }

        using FileAccess versionStamp = FileAccess.Open(WorldVersionPath, FileAccess.ModeFlags.Write);
        versionStamp?.StoreString(WorldFormatVersion.ToString());

        using FileAccess timelineStamp = FileAccess.Open(WorldTimelinePath, FileAccess.ModeFlags.Write);
        timelineStamp?.StoreString(TimelineConfig.Tag);
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

        _companyFoundings.Clear();
        foreach ((string nonMinerNodeId, CompanyFounding founding) in snapshot.CompanyFoundings ?? new Dictionary<string, CompanyFounding>())
        {
            _companyFoundings[nonMinerNodeId] = founding;
        }

        // ND.8b.3 — governance state + bot preferences ride the same snapshot (absent/null on an older
        // snapshot ⇒ empty: any already-founded company simply starts its governance on the next
        // founding — none can exist pre-ND.8b.3 in a canon world anyway).
        _companyGovernance.Clear();
        foreach ((string nonMinerNodeId, CompanyGovernanceState gov) in snapshot.CompanyGovernance ?? new Dictionary<string, CompanyGovernanceState>())
        {
            _companyGovernance[nonMinerNodeId] = gov;
        }

        _botGovernancePreferences.Clear();
        foreach ((string botNodeId, BotGovernancePreference pref) in snapshot.BotGovernancePreferences ?? new Dictionary<string, BotGovernancePreference>())
        {
            _botGovernancePreferences[botNodeId] = pref;
        }

        _companyInflowMultipliers.Clear();
        foreach ((string companyId, decimal multiplier) in snapshot.CompanyInflowMultipliers ?? new Dictionary<string, decimal>())
        {
            _companyInflowMultipliers[companyId] = multiplier;
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
        // ND.8b.2 — keyed by NonMinerNodeId, mirrors _companyFoundings. Absent/null on an older snapshot
        // (pre-ND.8b.2) deserializes to an empty dict below — no founding could exist yet under it anyway.
        public Dictionary<string, CompanyFounding> CompanyFoundings { get; set; } = new();
        // ND.8b.3 — keyed by NonMinerNodeId, mirrors _companyGovernance (same additive-field rule).
        public Dictionary<string, CompanyGovernanceState> CompanyGovernance { get; set; } = new();
        // ND.8b.3 — keyed by bot NodeId, mirrors _botGovernancePreferences (D-ND8.13/26 world draws).
        public Dictionary<string, BotGovernancePreference> BotGovernancePreferences { get; set; } = new();
        // ND.8b.5 — keyed by companyId, mirrors _companyInflowMultipliers (only non-1.0 entries stored).
        public Dictionary<string, decimal> CompanyInflowMultipliers { get; set; } = new();
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
    // ND.8b.1 (D-ND8.10/D-ND8.37) — company identity: "non-miner" survives only as the legacy
    // NodeId above. Null only for a pool-size mismatch (NonMinerBots.Count > CompanyRoster
    // .Auctionable.Count), which should never happen in a canon world (both are 40).
    public string? CompanyId { get; set; }
    public string CompanyDisplayName { get; set; } = string.Empty;
    public string CompanyCurrencyBand { get; set; } = string.Empty;
    public string CompanyMarketCategory { get; set; } = string.Empty;
    public DateTime? CompanyAppearanceDateLocal { get; set; }
    public string CompanyAnchor { get; set; } = string.Empty;
    public decimal TotalReceived { get; set; }
    public int DonorCount { get; set; }
    public string LeadingDonorAddress { get; set; } = string.Empty;
    public decimal LeadingDonorTotal { get; set; }
    // ND.4b (D-ND4b.10, corrected 2026-07-11) — the leading bid's LIVE, CURRENT SC value (BTC principal ×
    // TODAY's BtcMarketDataService price, recomputed fresh on every call — never frozen at the bid's own
    // day); null before Market Birth or when there is no leading bid yet.
    public decimal? LeadingDonorScValue { get; set; }
    public NonMinerAuctionStatus Status { get; set; } = NonMinerAuctionStatus.NotIntroduced;
    public long IntroUnixMs { get; set; }
    // ND.4b (D-ND4b.8, renamed from FirstBidUnixMs): 0 until a qualifying bid clears the required
    // minimum; then the timestamp of the CURRENT leading bid — reset on every accepted raise, so this is
    // NOT literally the first-ever bid (that concept no longer applies under the ascending-auction rules
    // introduced at ND.4b/ND.4c).
    public long LeadingBidUnixMs { get; set; }
    public long WindowCloseUnixMs { get; set; }
    public string WinnerAddress { get; set; } = string.Empty; // set when Resolved (never "" — a resolved window has ≥1 bid)

    // Step 14 (ND.5, D-ND5.3/5.4) — the GLOBAL top-10-by-value tracked donation pool: every qualifying
    // donation this non-miner has ever received competes for one of 10 slots purely by BTC principal
    // amount (win-or-lose bids alike, OQ-ND5.1), evicting the current smallest on a strictly larger
    // newcomer (a tie never evicts — first-in stays). Donations that never make/keep a slot become the
    // non-miner's own property forever — excluded from both the eventual SC refund and BTC sweep.
    public List<TrackedDonation> TrackedDonations { get; set; } = new();
}

// One donation still competing for (or holding) a slot in a non-miner's top-10 tracked pool (D-ND5.3).
public sealed class TrackedDonation
{
    public string DonorAddress { get; set; } = string.Empty;
    public decimal AmountBtc { get; set; }
    public long TimestampMs { get; set; }
    // Corrected 2026-07-11 (developer playtest feedback, supersedes the original D-ND5.3 day-of-donation
    // reading): this donation's LIVE, CURRENT SC value — priced as of "now" (the moment last computed),
    // not frozen at its own donation day. Purely informational display only (ND.8b.2: founding no longer
    // revalues anything in SC — the stock-token mint at close is BTC-participation-share-based only).
    public decimal? CurrentValueSc { get; set; }
}

// ND.8b.2 (D-ND8.14/D-ND8.15) — the once-per-resolution founding record: who holds what NST/PST at the
// company's close, for the AuctioningCompanyDetails scene's summary view (a stand-in until ND.8b.4's
// CompanyDetails scene replaces it) and later the dividends/votes engine (ND.8b.3). Persisted inside
// NetworkRoot's own BlockchainStateSnapshot — see the _companyFoundings field comment.
public sealed class CompanyFounding
{
    public string NonMinerNodeId { get; set; } = string.Empty;
    public string NonMinerAddress { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public long FoundedAtUnixMs { get; set; }
    public List<CompanyShareHolding> Holdings { get; set; } = new();
}

// One bidder's minted equity in a founded company (D-ND8.6/D-ND8.15). Exactly one of Nst/Pst is non-zero
// per holder: a top-3-tier holder at close mints NST (dividend rights + voting weight), everyone else
// mints PST (dividend rights only, no votes). HolderId is "player" or a bot nodeId (bot_1..4) — the same
// identity space ComputeAuctionLedger's qualifying bidders already use.
public sealed class CompanyShareHolding
{
    public string HolderId { get; set; } = string.Empty;
    public decimal Nst { get; set; }
    public decimal Pst { get; set; }
}

// ND.8b.3 (D-ND8.17/18/19b) — one founded company's live governance state: applied reserve mix + market
// category, the open vote (if any), the finalized quarter-dividend cycle, per-holder claimables, and the
// >30%-inflow special-vote tracking. Keyed by NonMinerNodeId beside CompanyFounding and persisted in the
// same BlockchainStateSnapshot (same "a block is the only commit" inheritance).
public sealed class CompanyGovernanceState
{
    public string NonMinerNodeId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public string CurrencyBand { get; set; } = "CB3";
    // The roster's category (the ±1 drift anchor, D-ND8.7) vs the currently-applied one.
    public string DefaultMarketCategory { get; set; } = "official";
    public string MarketCategory { get; set; } = "official";
    // The applied reserve-mix target: what % of reserves the company wants held in SC (BTC is the
    // complement). Set by the founding-day vote, moved by later votes within the band's ±25% range;
    // ENFORCED (actual BTC→SC conversion) from ND.8b.6.
    public decimal ReserveScPercent { get; set; }
    // The company's SC reserve — structurally 0 until ND.8b.6 lands the automatic conversions.
    public decimal ScReserve { get; set; }
    public int QuarterIndex { get; set; }
    public long NextQuarterlyDueMs { get; set; }
    public CompanyVote? OpenVote { get; set; }
    // D-ND8.17 — the FINALIZED quarter dividend (never live accrual), BTC and SC tracked separately.
    public decimal QuarterPayoutRatePercent { get; set; }
    public decimal QuarterDividendBtc { get; set; }
    public decimal QuarterDividendSc { get; set; }
    public long QuarterCycleStartMs { get; set; }
    public long QuarterCycleEndMs { get; set; }
    public int QuarterDrippedDays { get; set; }
    public bool QuarterLumpCredited { get; set; } = true; // true = no cycle is currently distributing
    public Dictionary<string, CompanyClaimable> ClaimableByHolder { get; set; } = new();
    // D-ND8.18 — the >30%-inflow special-vote trigger: reserve value at the last vote close + new BTC
    // received since (the SC side joins the measure at ND.8b.6).
    public decimal BaselineReserveBtc { get; set; }
    public decimal InflowSinceBaselineBtc { get; set; }
    public List<CompanyVoteRecord> VoteHistory { get; set; } = new();
}

// One open vote (D-ND8.18): founding-day, quarterly, or >30%-inflow special. Bots' ballots are cast at
// open; the player's arrives via TryRegisterPlayerVote while AwaitingPlayerVote pauses the game.
public sealed class CompanyVote
{
    public string Kind { get; set; } = string.Empty; // "founding" | "quarterly" | "special"
    public long OpenedAtMs { get; set; }
    public long ClosesAtMs { get; set; }
    public bool AwaitingPlayerVote { get; set; }
    public Dictionary<string, CompanyBallot> Ballots { get; set; } = new(); // holderId → ballot
}

// One NST holder's ballot (D-ND8.19b): a continuous reserve target (clamped to the band), a discrete
// market direction (-1 lighter / 0 hold / +1 darker — quarterly votes only), and a quarterly payout-rate
// preference (% of each reserve side, quarterly votes only).
public sealed class CompanyBallot
{
    public decimal ReserveScPercentTarget { get; set; }
    public int MarketShift { get; set; }
    public decimal PayoutRatePercent { get; set; }
}

// One holder's accrued-but-unclaimed dividends in one company (BTC and SC separately, D-ND8.17).
public sealed class CompanyClaimable
{
    public decimal Btc { get; set; }
    public decimal Sc { get; set; }
}

// A closed vote's outcome, kept per company (capped) for the CompanyDetails history readout.
public sealed class CompanyVoteRecord
{
    public string Kind { get; set; } = string.Empty;
    public long OpenedAtMs { get; set; }
    public long ClosedAtMs { get; set; }
    public decimal ResultReserveScPercent { get; set; }
    public string ResultMarketCategory { get; set; } = string.Empty;
    public decimal ResultPayoutRatePercent { get; set; }
    public decimal FinalizedDividendBtc { get; set; }
    public decimal FinalizedDividendSc { get; set; }
}

// ND.8b.3 (D-ND8.13/D-ND8.26) — one casino-miner-bot's governance identity, re-rolled per world: a
// Currency Band preference (distinct 4-of-5 draw across the four bots) + a market-category preference
// (distinct permutation, all four stances represented).
public sealed class BotGovernancePreference
{
    public string CurrencyBandPreference { get; set; } = "CB3";
    public string MarketCategoryPreference { get; set; } = "official";
}
