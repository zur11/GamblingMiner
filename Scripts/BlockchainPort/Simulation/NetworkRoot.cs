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
    // INC-001 / D-15.26 — the staging file for the atomic snapshot write (write here, close, rename over
    // StatePath). Never read as world state except as the corrupt-main fallback in TryLoadSnapshot.
    private const string StateTempPath = "user://blockchain/state.json.tmp";
    // Step 8 (full UTXO model) — bumped when the on-disk chain format changes incompatibly. The old
    // account/balance chain has no input→output (UTXO) linkage, so it cannot be replayed into a UTXO set;
    // on a version change we wipe the chain + clock + financial state and re-bootstrap a fresh world (the
    // "clean reset" decision). Increment this whenever the persisted Transaction/Block shape changes.
    // v3 (ND.7, D-ND7.6): historical fee replay — fee SEMANTICS are world-defining (an existing chain
    // carries flat-0.1 fees from 2009-04-26 that the Market-Birth median/mean policy could never
    // produce, and the bootstrap regenerates differently), so the fee-era switch rides the same
    // clean-reset mechanism even though the serialized shape itself is unchanged.
    // v4 (Step 15 P15.1d, D-15.10): the Central Bank (FED) becomes the explicit, persisted owner of all
    // loan bookkeeping — the casino's LoanCount/TotalLoaned/LoanHistory move OFF casino_sc_balance_state.json
    // and off the checkpoint DTO onto a FED account (D-15.3/D-15.23 Fork A). Rather than write a migration
    // for a DEV-era save, world-defining banking semantics ride the same clean-reset mechanism. Every LATER
    // plan15 file just joins the delete list below — no further bump for the rest of the plan.
    private const int WorldFormatVersion = 4;
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
    // ND.10e (2026-07-23, D-ND10e.1 — supersedes D-ND4b.5's flat 0.1 BTC `MinBidBtc`): the OPENING bid of
    // a pool that has no bid yet is **price-anchored**, re-evaluated live on whatever day the first bid
    // lands. It is always worth `OpeningBidUsdValue` ($0.10), CAPPED at `MaxOpeningBidBtc` (1 BTC — the
    // historical high-water mark for a company's minimum bid, which binds while BTC trades under $0.10).
    // Worked: BTC ≤ $0.10 ⇒ 1 BTC · BTC $1 ⇒ 0.1 BTC · BTC $10 ⇒ 0.01 BTC · BTC $30 ⇒ 0.00333 BTC.
    // Rationale (developer, 2026-07-23): a flat BTC floor is simultaneously too CHEAP in the sub-$0.10
    // era (auctions opened for pocket change) and ruinously EXPENSIVE later, which — compounded by the
    // raise band — was de-financing the casino bots faster than their mining income could refill them.
    // Anchoring the opening bid to a constant fiat value fixes both ends with one rule. Once a pool HAS a
    // leading bid nothing changes: the player still needs +1 satoshi (ND.4d), the bots the raise band.
    // Evaluated at the BID's OWN block timestamp, so the chain-replayed ledger stays deterministic.
    private const decimal OpeningBidUsdValue = 0.10m;
    private const decimal MaxOpeningBidBtc = 1m;
    // ND.4d (2026-07-10) — the PLAYER's own minimum raise is a flat 1 satoshi above the leading bid, NOT
    // the RaiseMin/RaiseMax band (that stays exactly as-is for the casino-bots' own bidding). The player
    // can therefore always retake the lead as cheaply as possible — but a casino-bot's NEXT raise still
    // jumps the full band over whatever the player just bid, so a minimal player raise is an easy target
    // to overtake; the risk is left for the player to learn empirically, not blocked in code.
    private const decimal OneSatoshi = 0.00000001m;
    // ND.10e (D-ND10e.2) — the casino-bots' raise band, cut from 10-20% to **5-10%** of the leading bid.
    // The geometric ladder is what ultimately prices every bot out of a mature auction (§22.10, by
    // design), but at 10-20% per accepted raise it arrived far too fast relative to mining income.
    private const decimal RaiseMinFraction = 0.05m;
    private const decimal RaiseMaxFraction = 0.10m;
    // ND.10e (D-ND10e.3) — the casino-bots' BTC RESERVE GUARD, with hysteresis: a bot that falls to
    // `BotBidReserveStopBtc` spendable withdraws from EVERY auction until it has rebuilt to
    // `BotBidReserveResumeBtc`. Deliberately hardcoded for now — the real design (reserve as a function of
    // the live BTC price, the bot's SC position, hardware income, dividend inflow…) is deferred and
    // recorded in `Documentation/PRIVATE_ROADMAP.md`.
    private const decimal BotBidReserveStopBtc = 200m;
    private const decimal BotBidReserveResumeBtc = 300m;
    // ND.10e (D-ND10e.4) — a bot auto-claims its accrued BTC dividend only once it is worth at least this
    // multiple of the network fee, so the fee (which is paid OUT OF the dividend) can never eat most of
    // the payment. See TryAutoClaimBotDividends for the audit that produced the number.
    private const decimal BotDividendClaimFeeMultiple = 10m;
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
    // ND.8d (D-ND8d.1/2/3/4, 2026-07-20 — §12.5.5) — the bid-count-aware shallow-tier ladder, REPLACING the
    // flat "top-3 = satisfied" (SatisfiedTopTierCount, retired). Diagnosis: with 4 bidders and a 3-tier
    // satisfied band, once 3 distinct bots each held a distinct top-3 slot every pool had at most ONE
    // eligible challenger, permanently — the bots-only stall. Fix: tiers 2 & 3 re-bid on probabilities keyed
    // by the pool mode (early-rush / normal / urgency) AND the bot's OWN tracked-slot count in that pool
    // (1 bid vs ≥2). Tier 1 stays ALWAYS satisfied (the leader never re-bids — the bots' last-bid
    // preservation, D-ND8d.1); tier 2 is NEVER satisfied (D-ND8d.2); tier 3 is satisfied only at ≥2 own bids
    // (D-ND8d.3). Tiers 4-9 keep the bid-count-INDEPENDENT ladder above; tier 10's self-eviction guard
    // (D-ND6.7b) is unchanged. Fibonacci-family literals per D-ND6.4 — never formula-derived; full matrix +
    // the early-rush 1-bid crossover justification (tier 2 21% > tier 3 13%) in §12.5.5.
    // ND.8d (2026-07-20 round-2 revision) — the shallow-tier matrix, retuned so tier 2 out-probabilities
    // tier 3 at EQUAL bid-count in every mode, all Fibonacci, and urgency ≥ normal:
    //              tier 2 (1 bid / ≥2 bids)   tier 3 (1 bid / ≥2 bids)
    //   EARLY RUSH   21% / 21%                 13% / satisfied
    //   NORMAL        5% /  3%                  2% / satisfied
    //   URGENCY       5% /  5%                  3% / satisfied
    private const int Tier2EarlyRushPercent = 21;            // tier 2, early-rush — 1 bid & ≥2 bids alike
    private const int Tier2NormalOneBidPercent = 5;
    private const int Tier2NormalManyBidPercent = 3;
    private const int Tier2UrgencyOneBidPercent = 5;
    private const int Tier2UrgencyManyBidPercent = 5;
    private const int Tier3EarlyRushOneBidPercent = 13;      // tier 3, early-rush, 1 bid
    private const int Tier3NormalOneBidPercent = 2;          // tier 3, normal, 1 bid (≥2 bids ⇒ satisfied)
    private const int Tier3UrgencyOneBidPercent = 3;         // tier 3, urgency, 1 bid (≥2 bids ⇒ satisfied)

    // D-ND8d.1/2/3 — the satisfied test (replaces the flat ownTiers[0] <= SatisfiedTopTierCount): tier 1
    // always; tier 3 only once the bot holds ≥2 of its own tracked slots; tier 2 never; everything else
    // (tiers 4+) rolls. bestTier = ownTiers[0] (ascending), ownBidCount = ownTiers.Count. The tier-10
    // self-eviction guard (D-ND6.7b) is separate and still applied at the call site / label helper.
    private static bool IsBidderSatisfied(int bestTier, int ownBidCount)
        => bestTier == 1 || (bestTier == 3 && ownBidCount >= 2);

    // ND.8d — the tier-2/tier-3 probability, keyed by mode × the bot's own bid-count. Returns 0 for tier 3
    // at ≥2 own bids (satisfied). Early-rush ignores urgency (its curve is already the steepest), matching
    // the tiers-4-9 table selection in ReBidProbabilityPercentFor.
    private static int ShallowTierProbabilityPercent(int tier, bool earlyRush, bool urgent, int ownBidCount)
    {
        bool single = ownBidCount <= 1;
        if (tier == 2) // never satisfied
        {
            if (earlyRush) return Tier2EarlyRushPercent;
            if (urgent) return single ? Tier2UrgencyOneBidPercent : Tier2UrgencyManyBidPercent;
            return single ? Tier2NormalOneBidPercent : Tier2NormalManyBidPercent;
        }
        // tier == 3 — satisfied at ≥2 own bids
        if (!single) return 0;
        if (earlyRush) return Tier3EarlyRushOneBidPercent;
        return urgent ? Tier3UrgencyOneBidPercent : Tier3NormalOneBidPercent;
    }

    // ND.6d/ND.8d — the single source of truth for a slot's re-bid probability, shared by the roll in
    // TryBuildCasinoBotBid and the AuctioningCompanyDetails UI label (via ReBidProbabilityLabelForSlot below).
    // occupiedSlots (the pool's current tracked-slot count) selects early-rush (<7) vs normal (≥7); urgent
    // (ND.6e — final 7 window days) shifts a NORMAL pool up. ownBidCount (ND.8d) is the occupant's own
    // tracked-slot count, consumed only by the tiers-2/3 shallow ladder. 0 for tier 1 (always satisfied),
    // tier 3 at ≥2 bids, tier 10 (self-eviction), or an out-of-range tier.
    private static int ReBidProbabilityPercentFor(int tier, int occupiedSlots, bool urgent, int ownBidCount)
    {
        bool earlyRush = occupiedSlots < EarlyRushSlotThreshold;
        if (tier == 2 || tier == 3) return ShallowTierProbabilityPercent(tier, earlyRush, urgent, ownBidCount);
        IReadOnlyDictionary<int, int> table = earlyRush
            ? EarlyRushReBidProbabilityPercentByTier
            : (urgent ? UrgentReBidProbabilityPercentByTier : ReBidProbabilityPercentByTier);
        return table.TryGetValue(tier, out int pct) ? pct : 0;
    }

    // ND.6d/ND.8d — the display string shown next to each tracked-pool slot in AuctioningCompanyDetails,
    // now taking the occupant's own bid-count (ND.8d.2) so tiers 2/3 read their true live odds. "satisfied"
    // for tier 1 (always) and tier 3 at ≥2 own bids, "NN%" for a ladder tier, "0%" for the self-eviction-
    // guarded tier 10 of a full pool, "" where a percentage is meaningless. (Player-held slots are blanked
    // by the caller — the player never rolls the ladder.)
    //
    // ND.8d round-3 label parity (2026-07-21, §12.5.5): for a single-slot occupant the shown % must MATCH
    // the roll's `max(mode rate, escalation)` (since ND.10c composed in BuildBotPoolOpportunities) — the base
    // ReBidProbabilityPercentFor alone shows only the static mode rate and never the growing escalation, so
    // the label sat frozen (e.g. bot_1's tier-5 BitInstant slot displayed a fixed 8% while its actual roll
    // climbed 8→16→24). This is an INSTANCE method so it can resolve the occupant bot from the donor address
    // (bots are single-address, OQ-8.2) and read the current chain tip; it PEEKS `_stuckBidderSignatures`
    // via the pure PeekStuckEscalationProbabilityPercent (no mutation — a 1 s UI refresh must never stamp it).
    public string ReBidProbabilityLabelForSlot(NonMinerDonationSummary summary, string donorAddress, int tier, int occupiedSlots, bool urgent, int ownBidCount)
    {
        // ND.10b (2026-07-22) — the self-eviction guard (D-ND6.7b): a donor holding the SMALLEST slot of a
        // FULL pool won't re-bid the pool at all, so ALL of that donor's slots read "guard" (was a bare
        // "0%" only on the tier-10 slot; a guarded donor's tier-3 slot now reads "guard" too, overriding
        // "satisfied"). Tier 1 (the leader) stays "satisfied" — never relabelled. Pure-address detection
        // (bots are single-address, OQ-8.2); matches the pipeline's `ownTiers.Contains(slotsByValue.Count)`.
        if (tier == 1) return ExclusionSatisfied;
        bool guarded = occupiedSlots >= MaxTrackedDonations
            && summary.TrackedDonations.Count > 0
            && summary.TrackedDonations.OrderByDescending(d => d.AmountBtc).Last().DonorAddress == donorAddress;
        if (guarded) return ExclusionGuard;

        NodeAgent? bot = SharedNodesById.Values.FirstOrDefault(n =>
            n.WalletAddress == donorAddress || (n.ReceiveWallet?.OwnedAddresses.Contains(donorAddress) ?? false));

        // ND.10d (2026-07-23) — THE affordability check this label never had. The roll has always been
        // gated by the half-spendable cap (D-ND6.8), but the label only ever computed the ladder rate, so
        // a bot that can no longer afford the raise still advertised a percentage — and since the stuck
        // escalation keeps ratcheting regardless, a priced-out lone occupant eventually displayed a
        // permanent "100%" beside a 0% real chance in the panel (the Silk Market finding: bot_1 stuck at
        // tier 6 with a 264 BTC cap against a ~371 BTC required raise). A bot priced out of a mature
        // auction is the DESIGNED economic terminator (§22.10) — so say so instead of quoting odds.
        // ND.10e — the reserve guard outranks the per-pool rules (same order as BuildBotPoolOpportunities).
        // ND.10j — through the shared predicate, so the label is right before the first mined block too.
        if (bot != null && IsBotRestingOnReserve(bot.NodeId)) return ExclusionReserve;
        if (bot != null && !CanAffordNextBid(summary, bot)) return ExclusionPricedOut;

        if (IsBidderSatisfied(tier, ownBidCount)) return ExclusionSatisfied;
        int pct = ReBidProbabilityPercentFor(tier, occupiedSlots, urgent, ownBidCount);
        // D-ND10c.3 — tiers 2 and 3 escalate too now (was `tier > 3`), so a lone tier-3 occupant's label
        // climbs 2 → 4 → 6 … instead of sitting at a flat 2% forever.
        if (ownBidCount == 1 && tier >= 2 && bot != null)
        {
            int currentBlockIndex = GetPlayerLatestBlock().Index;
            (int percent, int basePercent, int multiplier, bool capped) escalation =
                PeekStuckEscalationDetail(summary, bot.NodeId, donorAddress, tier, currentBlockIndex);
            // ND.10i suggestion 4 — when the ESCALATION is the binding term, show what it is made of. A bare
            // "40%" cannot be told apart from another tier's "40%" (and after a re-rank stamps every occupant
            // on the same block, equal readings are normal — only the slopes differ), which is exactly how
            // the tier-2/tier-4 collision hid. Where the static mode rate still wins, nothing is appended:
            // there is no escalation story to tell.
            if (escalation.percent > pct)
            {
                return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{escalation.percent}% (base {escalation.basePercent} ×{escalation.multiplier} blocks stuck{(escalation.capped ? ", capped" : string.Empty)})");
            }
        }
        return pct > 0 ? pct + "%" : string.Empty; // integer percent — culture-invariant by construction
    }

    // ND.10d — the half-spendable affordability gate (D-ND6.8) as a standalone question: can this node
    // still afford the raise this pool now demands? Same arithmetic the pipeline uses, so the per-slot
    // label and the roll agree by construction.
    private bool CanAffordNextBid(NonMinerDonationSummary summary, NodeAgent bidder)
    {
        long tipMs = GetPlayerLatestBlock().Timestamp;
        decimal leadingAmount = summary.LeadingBidUnixMs == 0 ? 0m : summary.LeadingDonorTotal;
        decimal requiredAmount = summary.LeadingBidUnixMs == 0
            ? OpeningBidFloorBtcAt(tipMs)
            : leadingAmount + RaiseMin(leadingAmount, tipMs);
        decimal fee = NetworkFeePolicy.MedianFeeAt(tipMs);
        decimal cap = Math.Round(bidder.Blockchain.GetAddressSpendableBalance(bidder.WalletAddress) * MaxBidBalanceFraction, 8);
        return requiredAmount + fee <= cap;
    }

    // ND.10c (2026-07-23, D-ND10c.2) — ONE qualifying pool for ONE bot, carrying the ladder probability
    // it will actually roll this block. THE shared source of truth: the live pipeline
    // (TryBuildCasinoBotBid), the eligibility test (D-ND10c.1) and the AuctioningCompanyDetails panel
    // (RealLeadingBidRoll) all build their view of the world from BuildBotPoolOpportunities below, so a
    // displayed number can never drift from the roll that produces it.
    private sealed class BotPoolOpportunity
    {
        public NonMinerDonationSummary Target = null!;
        public int OwnSlotCount;
        public int BestTier;
        public int OccupiedSlots;
        public List<int> OwnTiers = [];
        public decimal LeadingAmount;
        public decimal RequiredAmount;
        public int ProbabilityPercent; // r_k — always 100 for an unparticipated pool (a first bid never rolls)
        // ND.10d (2026-07-23) — null = the bot MAY bid this pool this slot; otherwise WHY it cannot, as a
        // short display string shared by the per-slot label and the per-bot panel (see ExclusionSatisfied
        // and friends). Every consumer filters on `Exclusion == null`; the reason exists so a 0% can say
        // what kind of zero it is instead of being indistinguishable from "unlikely".
        public string? Exclusion;
    }

    // ND.10d — the exclusion vocabulary. Kept as constants so the roll, the per-slot label and the panel
    // can never disagree on wording (they all render these same strings).
    private const string ExclusionSatisfied = "satisfied";
    private const string ExclusionGuard = "guard";
    private const string ExclusionPricedOut = "priced out";
    private const string ExclusionReserve = "reserve"; // ND.10e — the bot is rebuilding its BTC reserve

    // ND.10e (D-ND10e.3) — the bots currently RESTING on their reserve guard: entered at ≤ 200 BTC
    // spendable, released only once back at ≥ 300 BTC. In-memory only, exactly like
    // `_stuckBidderSignatures` (and `_lastMinedByNodeId`/`_currentMinerStreak` before it): pure bidding
    // behavior, not economically meaningful world state, so no checkpoint / pre-genesis / delete-list
    // work. An app restart re-derives it from the live balances on the next block — the only drift is a
    // bot sitting between the two thresholds, which restarts un-rested; harmless and self-correcting.
    private static readonly HashSet<string> _botsRestingOnReserve = [];

    // ND.10j (2026-07-28, §14.11) — THE reserve-guard question, as one pure predicate every consumer
    // shares: is this bot out of all auctions right now? It applies the hysteresis to the LIVE spendable
    // balance rather than merely reading the set, so it answers correctly even before the set has ever
    // been written this process — the cold-start half of the ND.10j defect. `_botsRestingOnReserve` is
    // in-memory (D-ND10e.3) and its only writer is the per-block sweep below, so between process start
    // and the first mined block the set is EMPTY: the label and the panel would show a percentage for a
    // bot the very next block excludes, which is precisely the §39.16 rule-6 violation ND.10d closed for
    // "priced out". Predicting the next sweep costs one balance read and cannot disagree with it.
    private static bool IsBotRestingOnReserve(string botNodeId)
    {
        bool resting = _botsRestingOnReserve.Contains(botNodeId);
        if (!SharedNodesById.TryGetValue(botNodeId, out NodeAgent? bot)) return resting;
        decimal spendable = bot.Blockchain.GetAddressSpendableBalance(bot.WalletAddress);
        // Hysteresis, unchanged: resting until rebuilt to Resume; entering only at or below Stop. A bot
        // sitting BETWEEN the two thresholds keeps whatever state it already had — which on a cold start
        // is "not resting", the drift D-ND10e.3 documents as harmless and self-correcting.
        return resting ? spendable < BotBidReserveResumeBtc : spendable <= BotBidReserveStopBtc;
    }

    // ND.10e — the reserve guard's edge-triggered sweep, run once per block beside
    // SweepStuckBidderSignatures (single writer, never from a UI refresh — the ND.10c discipline).
    // ND.10j — the hysteresis itself moved into IsBotRestingOnReserve so the sweep and every reader
    // apply literally the same rule; this is now just the write-back of that predicate.
    private static void SweepBotReserveGuard()
    {
        foreach (BotWalletRecord record in BotWalletRegistry.MinerBots)
        {
            if (!SharedNodesById.ContainsKey(record.NodeId)) continue;
            if (IsBotRestingOnReserve(record.NodeId)) _botsRestingOnReserve.Add(record.NodeId);
            else _botsRestingOnReserve.Remove(record.NodeId);
        }
    }

    // ND.10c (D-ND10c.2) — every pool this bot MAY bid this block, with its own roll probability. Applies
    // the unchanged qualifying rules (D-ND8d.1/3 satisfied; D-ND6.7b self-eviction guard) and the
    // unchanged half-spendable affordability cap (D-ND6.8) — what ND.10c changes is that affordability is
    // now a per-pool FILTER rather than a walk terminator, and every surviving pool rolls, instead of only
    // the spread-wide-first one (D-ND6.6, retired: a pool the walk never reached had a structurally
    // unreachable probability — the ND.10b BitInstant/BitPaid finding, step14 plan §14.4).
    private static List<BotPoolOpportunity> BuildBotPoolOpportunities(
        IEnumerable<NonMinerDonationSummary> pools, string botAddress, string botNodeId,
        decimal fee, decimal bidBudgetCap, long nowMs, int currentBlockIndex)
    {
        var opportunities = new List<BotPoolOpportunity>();
        // ND.10j — hoisted out of the pool loop: one live balance read per call instead of one per pool,
        // and (the point) the guard now answers correctly before the first mined block of a session.
        bool restingOnReserve = IsBotRestingOnReserve(botNodeId);
        foreach (NonMinerDonationSummary target in pools)
        {
            List<TrackedDonation> slotsByValue = target.TrackedDonations.OrderByDescending(d => d.AmountBtc).ToList();
            var ownTiers = new List<int>();
            for (int i = 0; i < slotsByValue.Count; i++)
            {
                if (slotsByValue[i].DonorAddress == botAddress) ownTiers.Add(i + 1);
            }

            int ownSlotCount = ownTiers.Count;
            int bestTier = ownSlotCount == 0 ? 0 : ownTiers[0];
            decimal leadingAmount = target.LeadingBidUnixMs == 0 ? 0m : target.LeadingDonorTotal;
            decimal requiredAmount = target.LeadingBidUnixMs == 0
                ? OpeningBidFloorBtcAt(nowMs)
                : leadingAmount + RaiseMin(leadingAmount, nowMs);

            // ND.10d — every in-auction pool now produces an entry; an excluded one carries WHY. The rules
            // and their order are unchanged (satisfied D-ND8d.1/3, self-eviction guard D-ND6.7b, then the
            // half-spendable cap D-ND6.8) — only the bookkeeping changed, so a caller can report the reason.
            string? exclusion = null;
            // ND.10e (D-ND10e.3) — the reserve guard outranks every per-pool rule: a bot rebuilding its
            // BTC reserve is out of ALL auctions until it clears the resume threshold.
            if (restingOnReserve) exclusion = ExclusionReserve;
            else if (ownSlotCount > 0 && IsBidderSatisfied(bestTier, ownSlotCount)) exclusion = ExclusionSatisfied;
            else if (slotsByValue.Count >= MaxTrackedDonations && ownTiers.Contains(slotsByValue.Count)) exclusion = ExclusionGuard;
            else if (requiredAmount + fee > bidBudgetCap) exclusion = ExclusionPricedOut;

            int probabilityPercent = 0;
            if (exclusion == null)
            {
                if (ownSlotCount == 0)
                {
                    probabilityPercent = 100; // unparticipated — a first bid is deterministic (D-ND6.5)
                }
                else
                {
                    // The pool's mode-appropriate rate (early-rush / urgency / normal), summed over the bot's
                    // two lowest slots (ND.8d round 2), with the stuck escalation as a FLOOR for a lone slot
                    // (§12.5.5 round-3 max() rule; tiers 2-3 included since D-ND10c.3).
                    int mode = SumTwoLowestReBidProbabilities(
                        ownTiers, slotsByValue.Count, IsAuctionInUrgencyWindow(target.WindowCloseUnixMs, nowMs), ownSlotCount);
                    probabilityPercent = ownSlotCount == 1
                        ? Math.Max(mode, PeekStuckEscalationProbabilityPercent(target, botNodeId, botAddress, bestTier, currentBlockIndex))
                        : mode;
                }
                if (probabilityPercent <= 0) exclusion = ExclusionSatisfied; // defensive — the rules above already cover every all-0 case
            }

            opportunities.Add(new BotPoolOpportunity
            {
                Target = target,
                OwnSlotCount = ownSlotCount,
                BestTier = bestTier,
                OccupiedSlots = slotsByValue.Count,
                OwnTiers = ownTiers,
                LeadingAmount = leadingAmount,
                RequiredAmount = requiredAmount,
                ProbabilityPercent = probabilityPercent,
                Exclusion = exclusion,
            });
        }
        return opportunities;
    }

    // ND.10c (D-ND10c.5) — the REAL per-bot "chance to place the leading bid in THIS pool", as a TRUE
    // PER-BLOCK probability: it now folds in BOTH the eligible-bot draw (roll 1) and the 0/1/2 count draw,
    // where ND.10b's version reported only the in-pool layer conditional on the bot running its pipeline.
    // A pool therefore reads 0 ONLY when the bot genuinely cannot bid it (satisfied / self-eviction guard /
    // unaffordable) — never merely because a deterministic walk would not have reached it.
    //
    //   r_k = the pool's ladder probability for this bot (BuildBotPoolOpportunities — mode-aware,
    //         max(mode, escalation) for a lone slot, 1.0 unparticipated)
    //   q_k = r_k · Σ_W P(W₋ₖ = W)·w_k/(w_k+W)  — all pools roll in parallel; if several hit, one wins the
    //         WEIGHTED tie-break (ND.10l, superseding the uniform Σ_m P(H₋ₖ=m)/(m+1) count DP). W₋ₖ = the
    //         total tie-break weight of the bot's OTHER pools that hit; w_k = this pool's own weight
    //         (its ladder probability, or FreshPoolSeedingWeight when unparticipated).
    //   p_k = q_k / B                      — B = number of ELIGIBLE bots (D-ND10c.1)
    //   P_k = w1·p + w2·(2p − p²)          — the count draw; slightly below p·E[count] because a bot drawn
    //         for both slots can only take the lead once. Weights read from the live draw constants.
    //
    // Identity (asserted below): Σ_k q_k = 1 − ∏_k (1−r_k). Full derivation + worked example:
    // Documentation/ProjectDesignManual.md §22.14.
    public Dictionary<string, List<(string poolNodeId, string poolName, double percent)>> RealLeadingBidRoll(
        IReadOnlyList<NonMinerDonationSummary> ledger)
    {
        var result = new Dictionary<string, List<(string, string, double)>>();
        List<NonMinerDonationSummary> recruitable = ledger.Where(s => s.Status == NonMinerAuctionStatus.InAuction).ToList();

        Block tip = GetPlayerLatestBlock();
        long tipMs = tip.Timestamp;
        int blockIndex = tip.Index;
        decimal fee = NetworkFeePolicy.MedianFeeAt(tipMs);

        // Pass 1 — each bot's AFFORDABLE opportunities. D-ND10c.1's eligibility test is exactly "this list
        // is non-empty", so B (the eligible-bot count) falls out of the same pass.
        var byBot = new Dictionary<string, List<BotPoolOpportunity>>();
        foreach (BotWalletRecord record in BotWalletRegistry.MinerBots)
        {
            result[record.NodeId] = [];
            if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? bot)) continue;
            decimal spendable = bot.Blockchain.GetAddressSpendableBalance(bot.WalletAddress);
            decimal cap = Math.Round(spendable * MaxBidBalanceFraction, 8);
            List<BotPoolOpportunity> biddable =
                BuildBotPoolOpportunities(recruitable, bot.WalletAddress, record.NodeId, fee, cap, tipMs, blockIndex)
                    .Where(o => o.Exclusion == null)
                    .ToList();
            if (biddable.Count > 0) byBot[record.NodeId] = biddable;
        }

        int eligibleBots = byBot.Count;
        if (eligibleBots == 0) return result; // nobody can bid anywhere this block — every pool reads 0

        double weightOne = CasinoBotDonationWeightOnePercent / 100.0;
        double weightTwo = (100 - CasinoBotDonationWeightZeroPercent - CasinoBotDonationWeightOnePercent) / 100.0;

        foreach ((string botNodeId, List<BotPoolOpportunity> opportunities) in byBot)
        {
            double[] r = opportunities.Select(o => o.ProbabilityPercent / 100.0).ToArray();
            // ND.10l — the tie-break is weighted now, so the share DP needs each pool's WEIGHT as well as
            // its hit probability (a fresh pool's r is the deterministic sentinel 1.0, its weight is not).
            int[] w = opportunities.Select(TieBreakWeight).ToArray();
            double qSum = 0d;
            List<(string, string, double)> perPool = result[botNodeId];

            for (int k = 0; k < r.Length; k++)
            {
                double q = r[k] * ExpectedWeightedTieBreakShare(r, w, k);
                qSum += q;
                double p = q / eligibleBots;                        // roll 1 — the eligible-bot draw
                double perBlock = weightOne * p + weightTwo * (2 * p - p * p); // the count draw
                if (perBlock <= 0) continue;
                perPool.Add((opportunities[k].Target.NonMinerNodeId, DescribeCompany(opportunities[k].Target), perBlock * 100.0));
            }

            // Free self-check on the DP (never fires in practice — a violation means the tie-break model
            // and the parallel-roll model have diverged, which would also mean the panel lies).
            double anyHit = 1d;
            foreach (double rk in r) anyHit *= 1 - rk;
            anyHit = 1 - anyHit;
            if (Math.Abs(qSum - anyHit) > 1e-6)
                GD.PushWarning($"[ND.10c] RealLeadingBidRoll identity broken for {botNodeId}: Σq={qSum:F8} vs 1−∏(1−r)={anyHit:F8}");

            perPool.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        }

        return result;
    }

    // ND.10c (D-ND10c.5), reworked at ND.10l (D-ND10l.3) — the share pool `excludeIndex` keeps when
    // several pools hit in the same slot. Under the retired UNIFORM draw this was Σ_m P(H₋ₖ=m)/(m+1),
    // a plain count DP: only HOW MANY others hit mattered. Under the weighted draw it matters WHICH
    // others hit, so the DP now runs over their total tie-break WEIGHT:
    //
    //     share_k = Σ_W P(other hits weigh W) · w_k / (w_k + W)
    //
    // The panel MUST track the roll here — a per-block probability that models a tie-break the pipeline
    // no longer performs would be exactly the ND.10d class of lie, in the one number the whole scene
    // exists to show (§39.16 rule 6).
    //
    // Two collapses keep it cheap. A pool with r ≥ 1 (every unparticipated pool, and any escalation that
    // has reached 100%) ALWAYS hits, so it is not a random variable at all — its weight folds into a
    // constant offset. A pool with r ≤ 0 never hits and drops out. Only genuinely stochastic pools enter
    // the DP, and a bot holds slots in a handful of pools at most, so the weight axis stays small even
    // when 40 companies are live. The Σq identity asserted by the caller still holds unchanged: any rule
    // that picks exactly one winner from a non-empty hit set satisfies Σ_k q_k = 1 − ∏_k (1−r_k), so it
    // now doubles as a check on this DP too.
    private static double ExpectedWeightedTieBreakShare(double[] probabilities, int[] weights, int excludeIndex)
    {
        int ownWeight = Math.Max(1, weights[excludeIndex]);

        int certainWeight = 0;
        var stochastic = new List<(double p, int w)>();
        for (int j = 0; j < probabilities.Length; j++)
        {
            if (j == excludeIndex) continue;
            if (probabilities[j] >= 1d) certainWeight += Math.Max(0, weights[j]); // always hits
            else if (probabilities[j] > 0d) stochastic.Add((probabilities[j], Math.Max(0, weights[j])));
            // r ≤ 0 — never hits, contributes nothing
        }

        int maxWeight = 0;
        foreach ((double _, int w) in stochastic) maxWeight += w;

        var dist = new double[maxWeight + 1]; // dist[W] = P(the stochastic others' hit weights sum to W)
        dist[0] = 1d;
        int filled = 0;
        foreach ((double p, int w) in stochastic)
        {
            // 0/1-knapsack convolution, walked DOWNWARD so dist[t − w] is still the pre-update value.
            for (int t = filled + w; t >= 0; t--)
            {
                dist[t] = dist[t] * (1 - p) + (t >= w ? dist[t - w] * p : 0d);
            }
            filled += w;
        }

        double share = 0d;
        for (int t = 0; t <= maxWeight; t++)
        {
            if (dist[t] > 0d) share += dist[t] * ownWeight / (ownWeight + certainWeight + (double)t);
        }
        return share;
    }

    // ND.8d (2026-07-20 round-2 refinement, §12.5.5) — a participated pool's re-bid roll is the SUM of the
    // bot's TWO LOWEST slot re-bid probabilities, not the single best-tier probability. With the tier2/tier3
    // inversion the two lowest PROBABILITIES need not be the two best TIERS, so we rank by probability. A
    // satisfied slot contributes 0 (tier 1, or tier 3 at ≥2 bids), so a bot that still holds a satisfied slot
    // only ever sums {0, its single active prob} = that single prob — the boost therefore applies SOLELY to a
    // bot with no satisfied slot (all-positive), exactly the developer's rule. A single-slot bot is unchanged
    // (its "two lowest" is just its one value). Clamped to 100 (early-rush sums can exceed it).
    private static int SumTwoLowestReBidProbabilities(List<int> ownTiers, int occupiedSlots, bool urgent, int ownBidCount)
    {
        int sum = ownTiers
            .Select(t => ReBidProbabilityPercentFor(t, occupiedSlots, urgent, ownBidCount))
            .OrderBy(p => p)
            .Take(2)
            .Sum();
        return Math.Min(100, sum);
    }

    // ND.8d round 3 (2026-07-21, §12.5.5) — the stuck-single-bidder escalation. A bot holding EXACTLY ONE
    // tracked donation in a pool, at a non-top-3 tier (4-9), rolls a probability that grows LINEARLY by the
    // tier's plain NORMAL-mode base each block it has remained stuck at that same tier — reset the instant
    // its own bid (now ≥2 slots, out of this path entirely) or ANY OTHER party's bid changes its rank.
    // Diagnosed off The Silk Market (non_miner_6): bot_4 alone at tier 4, rolling a flat unchanging
    // urgency-8% indefinitely while bot_3 (2 slots elsewhere) never revisited — the round-1/2 fixes solved
    // the CHALLENGER-COUNT problem, not this lone, flat, never-escalating-probability shape.
    //
    // Non-regression floor (2026-07-21 audit fix, §12.5.5 round 3): the CALLER composes this escalation as
    // `max(mode-appropriate rate, escalation)` — it is NEVER used to REPLACE the pool's mode rate. The
    // first cut ignored early-rush/urgency and returned the flat NORMAL base outright, which SUPPRESSED
    // bidding in EARLY-RUSH pools: a single-slot below-top-3 bot there would otherwise roll the steep
    // early-rush rate (tier 4/5/6 = 34/55/89%), but the raw escalation handed it the NORMAL base (5/8/13%)
    // on block 1, only climbing back over ~7 blocks — and pool churn kept resetting it to base. That was
    // the DeepBit (non_miner_7, early-rush) stagnation. With the max() floor the escalation can only ever
    // ADD aggression on top of the mode rate, never remove it, so young pools stay contested AND a lone
    // stuck bot still escalates toward 100% the longer it waits.
    //
    // Revision (2026-07-21, same day): the first cut derived "since" purely from `TrackedDonations`'
    // CURRENT snapshot, watching only for a GREATER-valued donation landing after this bot's own — it
    // missed the case where the bot's OWN OTHER slot gets evicted (dropping it from 2 bids to 1 without
    // any disturbance ABOVE its surviving slot), which should ALSO restart the escalation from base (the
    // bot was governed by the round-2 multi-slot roll during the 2-bid period, never "stuck at 1 bid," so
    // the escalation must start fresh the moment it actually becomes single-slot — not reach back to that
    // surviving slot's own, possibly old, donation timestamp).
    //
    // Fix: an in-memory-only SIGNAL, not derived from a history replay — "does this bot currently hold
    // ≥2 of the pool's tracked slots?" (true/false), keyed per (company, bot). Every time this signal's
    // value (folded together with the bot's own best tier, so a same-multiplicity tier change ALSO counts)
    // CHANGES, the current block index is stamped as the new "since" point — an edge-triggered update, not
    // a per-frame poll (`_Process`): it happens exactly once per this bot's per-block evaluation inside the
    // SAME block-mined event that already drives the whole bidding cascade (HandleMinedBlock →
    // ScheduleBotTransactionsAfterBlock → TryCasinoBotDonation), never oftener.
    //
    // Deliberately NOT part of BlockchainStateSnapshot (no checkpoint/pre-genesis/delete-list work) — like
    // `_lastMinedByNodeId`/`_currentMinerStreak` elsewhere in this file, this is pure bidding-behavior
    // bookkeeping, not economically meaningful world state; an app restart just resets it, and the
    // escalation harmlessly restarts from base (exactly how the streak counters already behave).
    private static readonly Dictionary<(string nonMinerNodeId, string botNodeId), (string signature, int sinceBlockIndex)> _stuckBidderSignatures = new();

    // ND.8d round 3 — the escalation math itself: the tier's plain NORMAL base grown linearly by one step
    // per block stuck, clamped to 100%. Since D-ND10c.4 it has exactly ONE reader,
    // PeekStuckEscalationProbabilityPercent (pure), consumed by both the roll and the UI label; the
    // signature writes belong solely to the per-block SweepStuckBidderSignatures.
    // ND.10i (D-ND10i.2, 2026-07-27) — the CEILING on a top-3 (NST-band) slot's escalation. An accepted bid
    // always takes the lead (it must clear the leader's floor), which RESETS the 20-day rolling window
    // (D-ND4b.1) — so an escalation running to 100% at tier 2 is a leapfrog engine: the runner-up bids every
    // block, becomes leader (tier 1 ⇒ always satisfied, stops), the displaced leader becomes tier 2 and
    // starts escalating, and the countdown never expires while two solvent bots contest the pool. Capping
    // the NST band keeps ND.10c's fix (no lone tier-2/3 occupant frozen at a flat 5%/2% forever) without
    // turning the runner-up into a metronome; below the band the escalation still runs to 100%, because a
    // bot outside the stock-minting tiers genuinely has nothing to lose by pressing. Fibonacci per D-ND6.4.
    // Resolution of such a pool then rests where §22.10 always intended it: price-out, the economic
    // terminator. The ceiling bounds the ESCALATION only — callers compose max(mode rate, escalation), so a
    // calibrated mode rate above it (none today: tier 2 tops out at 21 in early rush) would still win.
    private const int MaxTopTierEscalationPercent = 34;

    // ND.10i suggestion 4 — the escalation's COMPOSITION, not just its result: the tier's slope, how many
    // blocks it has been compounding, and whether the D-ND10i.2 ceiling is binding. The per-slot label shows
    // these so a reader can see WHY two slots read what they do — the developer diagnosed the slope collision
    // by noticing two numbers were equal that shouldn't be, which a visible `base × blocks` would have made
    // obvious on sight instead of requiring a trace audit.
    private static (int percent, int basePercent, int multiplier, bool capped) EscalatedStuckDetail(int bestTier, int blocksElapsed)
    {
        int basePercent = StuckEscalationBasePercent(bestTier);
        if (basePercent <= 0) return (0, 0, 0, false);
        int multiplier = Math.Max(0, blocksElapsed) + 1; // the block it got stuck on counts as block 1
        decimal scaled = basePercent * (decimal)multiplier;
        decimal ceiling = bestTier <= NstTopTierCount ? MaxTopTierEscalationPercent : 100m;
        return ((int)Math.Min(ceiling, scaled), basePercent, multiplier, scaled > ceiling);
    }

    private static int EscalatedStuckPercent(int bestTier, int blocksElapsed)
        => EscalatedStuckDetail(bestTier, blocksElapsed).percent;

    // ND.10i suggestion 3 — the DEV-only ordering assertion. The escalation slopes must ASCEND with tier
    // depth (deeper = further from the NST band = more desperate), with EXACTLY ONE deliberate exception:
    // tier 3 sits below tier 2, because that pair is ordered by SATISFACTION, not desperation (tier 3 can be
    // satisfied at ≥2 own bids, tier 2 never is — D-ND8d.2/3). So the sequence checked is t3 < t2 < t4 < …
    // < t9. Stripped from release builds by [Conditional] — a broken ladder in an exported build is
    // undetectable anyway; the point is to catch it the moment a developer edits a table.
    [System.Diagnostics.Conditional("DEBUG")]
    private static void AssertEscalationSlopesAreOrdered()
    {
        int[] tiersInExpectedOrder = [3, 2, 4, 5, 6, 7, 8, 9];
        for (int i = 1; i < tiersInExpectedOrder.Length; i++)
        {
            int previousTier = tiersInExpectedOrder[i - 1], tier = tiersInExpectedOrder[i];
            int previous = StuckEscalationBasePercent(previousTier), current = StuckEscalationBasePercent(tier);
            if (current > previous) continue;

            GD.PrintErr(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "[ND.10i] Escalation slope ordering VIOLATED — tier {0} ({1}%/block) must escalate strictly "
                + "faster than tier {2} ({3}%/block). Expected ascending order t3 < t2 < t4 … < t9 (the t3/t2 "
                + "swap is the only intended one, D-ND8d.2/3). A shallower slot will now reach certainty as "
                + "fast as, or faster than, a deeper one — the ND.10i DeepBit defect. Check "
                + "StuckEscalationBasePercent / the ladder tables it reads.",
                tier, current, previousTier, previous));
        }
    }

    // D-ND10c.3 (2026-07-23) — the escalation's base is the tier's DESPERATION slope: how fast a lone
    // occupant stuck at that tier grows toward acting. Tiers 4-9 read the plain NORMAL ladder table; tiers
    // 2-3 escalate too since ND.10c (before it the whole escalation was gated at `tier > 3`, so a bot
    // parked at tier 2/3 sat at a flat, never-moving 5%/2% forever — the BitPaid finding, §14.4). Tier 1 is
    // always satisfied and never escalates; tier 10 is excluded by the self-eviction guard before any roll.
    //
    // ND.10i (D-ND10i.1, 2026-07-27) — tier 2 has its OWN base (3), no longer the shallow table's NORMAL
    // one-bid cell (5). ND.10c's rule "the base is the tier's plain NORMAL-mode value" is internally
    // consistent but landed tier 2 on exactly tier 4's slope (both 5), because the two tables were
    // calibrated for different questions and had never been read side by side: a bot in 2nd place — INSIDE
    // the NST band — escalated to certainty as fast as one in 4th and 2.5× faster than one in 3rd, and in a
    // NORMAL-mode pool tiers 2 and 4 were numerically identical at every block forever (the DeepBit audit).
    // The shallow table's tier2 > tier3 crossover is about SATISFACTION (tier 3 can be satisfied at ≥2 own
    // bids, tier 2 never is) — a different question from desperation, and it must not leak into the slope.
    // So the slopes now read 2 (t3) < 3 (t2) < 5 (t4) < 8 … : the deliberate 2/3 swap survives, the
    // accidental 2/4 collision does not. 3 is the only Fibonacci-family value between them (D-ND6.4).
    private const int Tier2EscalationBasePercent = 3;

    private static int StuckEscalationBasePercent(int bestTier)
    {
        if (bestTier == 2) return Tier2EscalationBasePercent;
        if (bestTier == 3)
            return ShallowTierProbabilityPercent(bestTier, earlyRush: false, urgent: false, ownBidCount: 1);
        return ReBidProbabilityPercentByTier.TryGetValue(bestTier, out int p) ? p : 0;
    }

    // ND.8d round-3 label parity (2026-07-21, §12.5.5) — the SIDE-EFFECT-FREE read of the stuck-bidder
    // signal, shared by the roll and the AuctioningCompanyDetails per-slot label. It only READS: it uses
    // the recorded "since" when the signature still matches, else predicts what the next roll would
    // compute (a fresh reset ⇒ blocksElapsed 0 ⇒ base × 1), so label and roll always agree.
    //
    // D-ND10c.4 (2026-07-23) — this is now the ONLY escalation reader anywhere: the side-effecting twin
    // (`ComputeStuckEscalationProbabilityPercent`) is retired, and `SweepStuckBidderSignatures` — which
    // already refreshes EVERY (recruitable pool × casino bot) pair once per block, before any bid runs —
    // is the single writer of `_stuckBidderSignatures`. Single-writer/single-reader; no path can stamp
    // the dictionary from a UI refresh or from a partial pipeline pass.
    //
    // Single-slot occupants only (callers gate on `ownSlotCount == 1`); tier 1 never escalates.
    //
    // ND.10j (2026-07-28, §14.11) — an ABSENT key no longer means "stuck as of this block". It means
    // "this process has never observed this pair", which is true of every pair right after a restart and
    // of EVERY pair before the session's first mined block (the sweep is the only writer and it runs per
    // block). Defaulting to `currentBlockIndex` there silently reset every stuck bidder's accumulated
    // pressure to base×1 — diagnosed off BitInstant (non_miner_8) four in-game days from close: bot_1,
    // a lone tier-5 occupant stuck since block 957, displayed and rolled the flat urgency rate 13%
    // instead of its true 8×8 = 64%, because the app had been restarted and no block had been mined yet.
    // In a closing window there are too few blocks left to rebuild the escalation, so the auction
    // resolves on its leader by default — the exact stagnation ND.8d round 3 introduced the escalation
    // to prevent. Now an absent key is SEEDED from the chain (SeedStuckSinceBlockIndex); a key that is
    // present but carries a DIFFERENT signature still stamps `currentBlockIndex`, because that is an
    // observed, exact edge this process actually saw and must not be overridden by an estimate.
    private static (int percent, int basePercent, int multiplier, bool capped) PeekStuckEscalationDetail(
        NonMinerDonationSummary target, string botNodeId, string botAddress, int bestTier, int currentBlockIndex)
    {
        if (bestTier <= 1) return (0, 0, 0, false);
        int sinceBlockIndex;
        if (_stuckBidderSignatures.TryGetValue((target.NonMinerNodeId, botNodeId), out (string signature, int sinceBlockIndex) recorded))
        {
            // Known pair: the recorded stamp when the signature still matches, else what the next sweep
            // will stamp for the change it has not processed yet (label/roll parity, unchanged).
            sinceBlockIndex = recorded.signature == $"single:{bestTier}" ? recorded.sinceBlockIndex : currentBlockIndex;
        }
        else
        {
            sinceBlockIndex = SeedStuckSinceBlockIndex(target, botAddress, bestTier, currentBlockIndex);
        }
        return EscalatedStuckDetail(bestTier, currentBlockIndex - sinceBlockIndex);
    }

    // ND.10j — the chain-derived answer to "since which block has this bot occupied THIS tier?", used
    // only to seed a pair the in-memory signal has never seen. Deliberately DERIVED rather than
    // persisted: the ND.10c/D-ND10e.3 line that `_stuckBidderSignatures` is bidding bookkeeping and not
    // world state still holds, so this needs no BlockchainStateSnapshot field, no checkpoint work, no
    // delete-list entry and no WorldFormatVersion bump — the developer keeps their running playtest.
    //
    // The derivation: a tracked donation ranked BELOW the bot can never have changed the bot's tier, so
    // the bot has held its current tier since the most recent donation ranked AT OR ABOVE it — which
    // includes its own slot (the block it took the tier) and every later raise that pushed it down.
    //
    // KNOWN IMPRECISION (the one thing a chain read cannot recover): an EVICTION of this bot's other
    // slot, which drops it 2 bids → 1 without disturbing anything above it. The evicted donation is gone
    // from the pool, so a seed cannot see that the bot only became single-slot at that later block, and
    // will over-estimate how long it has been stuck. The live sweep still detects it exactly (that is
    // why the in-memory signal exists at all — the ND.10a revision) and this only ever applies to
    // history from BEFORE the process started. Over-estimating a long-standing lone occupant's pressure
    // is strictly closer to the truth than the reset it replaces, which under-estimated it to zero.
    private static int SeedStuckSinceBlockIndex(NonMinerDonationSummary target, string botAddress, int bestTier, int currentBlockIndex)
    {
        List<TrackedDonation> slotsByValue = target.TrackedDonations.OrderByDescending(d => d.AmountBtc).ToList();
        // Both callers derive `bestTier` from this same value-descending order, so the slot at that tier
        // must belong to this bot. Checking it makes the seed refuse to guess from a stale or mismatched
        // view (it would otherwise silently date the escalation from another donor's slot).
        if (bestTier > slotsByValue.Count || slotsByValue[bestTier - 1].DonorAddress != botAddress)
        {
            return currentBlockIndex; // no escalation, rather than one built on the wrong slot
        }

        long sinceMs = 0;
        for (int i = 0; i < bestTier; i++)
        {
            if (slotsByValue[i].TimestampMs > sinceMs) sinceMs = slotsByValue[i].TimestampMs;
        }
        if (sinceMs <= 0) return currentBlockIndex;

        int seeded = BlockIndexAtOrBeforeTimestamp(sinceMs, currentBlockIndex);
        return Math.Clamp(seeded, 0, currentBlockIndex);
    }

    // ND.10j — a tracked donation carries its CONFIRMING BLOCK's timestamp (ComputeTrackedDonationPool
    // takes `ts` straight from the bid's block), so this maps one back to a block index: the last block
    // at or before that instant. A reverse scan rather than a binary search — block timestamps are
    // non-decreasing today but nothing enforces it, the pool's donations are typically recent so the
    // loop exits in a handful of steps, and the codebase already scans the chain linearly throughout.
    private static int BlockIndexAtOrBeforeTimestamp(long timestampMs, int fallbackBlockIndex)
    {
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)) return fallbackBlockIndex;
        List<Block> chain = player.Blockchain.Chain;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (chain[i].Timestamp <= timestampMs) return chain[i].Index;
        }
        return fallbackBlockIndex;
    }

    private static int PeekStuckEscalationProbabilityPercent(NonMinerDonationSummary target, string botNodeId, string botAddress, int bestTier, int currentBlockIndex)
        => PeekStuckEscalationDetail(target, botNodeId, botAddress, bestTier, currentBlockIndex).percent;

    // Fix B (ND.10a, 2026-07-22) — the per-block signature sweep. The escalation was previously refreshed
    // ONLY when a bot's pipeline SELECTED a pool, so a bot busy seeding fresh pools never updated the signal
    // for a pool it was already stuck in — its signature went stale and neither the roll nor the label
    // escalated (the bot_4/BitInstant tier 2→5 finding, §13 audit). This refreshes EVERY (recruitable pool ×
    // casino bot) pair each block, edge-triggered (a stamp whenever the "multi" / "single:{tier}" signature
    // changes), so a rank-push by ANOTHER bot is recorded at the block it happens and `blocksElapsed` then
    // grows correctly. It also REMOVES the entry when the bot no longer holds any slot in that pool, so a
    // future re-entry starts fresh (closes the round-3 "accepted residual imprecision").
    //
    // D-ND10c.4 (2026-07-23) — this is now the SINGLE WRITER of `_stuckBidderSignatures`: the side-effecting
    // ComputeStuckEscalationProbabilityPercent is retired, and every consumer (the bid roll via
    // BuildBotPoolOpportunities, the panel, the per-slot label) reads through the pure Peek variant. It runs
    // once per block in TryCasinoBotDonation, BEFORE the count==0 early-return, so the escalation advances
    // on donation-less blocks too.
    private static void SweepStuckBidderSignatures(List<NonMinerDonationSummary> recruitable, int currentBlockIndex)
    {
        foreach (NonMinerDonationSummary target in recruitable)
        {
            List<TrackedDonation> slotsByValue = target.TrackedDonations.OrderByDescending(d => d.AmountBtc).ToList();
            foreach (BotWalletRecord record in BotWalletRegistry.MinerBots)
            {
                if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? bot)) continue;
                string botAddress = bot.WalletAddress;
                int ownSlotCount = 0, bestTier = 0;
                for (int i = 0; i < slotsByValue.Count; i++)
                {
                    if (slotsByValue[i].DonorAddress != botAddress) continue;
                    ownSlotCount++;
                    if (bestTier == 0) bestTier = i + 1;
                }

                var key = (target.NonMinerNodeId, record.NodeId);
                if (ownSlotCount == 0)
                {
                    _stuckBidderSignatures.Remove(key); // fully evicted — a future re-entry starts fresh
                    continue;
                }

                string signature = ownSlotCount >= 2 ? "multi" : $"single:{bestTier}";
                if (!_stuckBidderSignatures.TryGetValue(key, out (string signature, int sinceBlockIndex) recorded))
                {
                    // ND.10j — FIRST SIGHT of this pair (a fresh process, or a pool/bot this session has
                    // not swept yet). "Never observed" is not "just became stuck": seed from the chain so
                    // a restart does not zero an escalation that has genuinely been building for blocks.
                    // A "multi" bot does not escalate, so it costs nothing to stamp it at the current
                    // block — only the single-slot case needs a real seed.
                    _stuckBidderSignatures[key] = ownSlotCount >= 2
                        ? (signature, currentBlockIndex)
                        : (signature, SeedStuckSinceBlockIndex(target, botAddress, bestTier, currentBlockIndex));
                }
                else if (recorded.signature != signature)
                {
                    // An OBSERVED change — exact, and never overridden by the estimate above.
                    _stuckBidderSignatures[key] = (signature, currentBlockIndex);
                }
            }
        }
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
    // Step 15 P15.3a — the FED a financing bank draws its provisioning SC from (D-15.3). Same plain
    // autoload-reference pattern; null-guarded, and a null simply keeps the casino fallback in play.
    private static CentralBankService? _centralBank;
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
    // Step 15 P15.2c (D-15.4/D-15.5) — the four CB1 banks' layer-1 balance sheets (quarantined
    // CollateralBtc + the per-client provisioning book), keyed by the bank's NonMinerNodeId. Same
    // BlockchainStateSnapshot inheritance as the two dictionaries above: a bank can only have a balance
    // sheet once it is FOUNDED (2012-09 at the earliest), so no checkpoint/pre-genesis path of its own.
    private static readonly Dictionary<string, BankBalanceSheet> _bankState = new();
    // Step 15 P15.5a — dissolved companies, keyed by NonMinerNodeId. An entry here is the authoritative
    // "this company is dead": it is removed from _companyFoundings/_companyGovernance at closure, so every
    // live loop skips it for free, and the record is what the Closed-Companies readouts render.
    private static readonly Dictionary<string, CompanyClosure> _closedCompanies = new();
    // Provisions are far more frequent than FED loan draws (one per company conversion), so each bank's
    // per-client history is capped — oldest trimmed, totals stay exact (the CentralBankService /
    // ScMonetaryLedgerService precedent).
    private const int MaxBankClientHistory = 200;

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
        _centralBank = GetNodeOrNull<CentralBankService>("/root/CentralBankService"); // P15.3a
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        // ND.10i suggestion 3 (2026-07-27) — DEV-only ladder self-check, stripped from release builds. The
        // tier-2/tier-4 slope collision existed because two tables are read by ONE consumer and nobody had
        // ever printed them side by side; this makes the next such collision announce itself at launch
        // instead of surviving until someone notices two labels behaving alike in a playtest. Same reflex as
        // P15.9's clamp tripwire: an invariant that only lives in prose is an invariant nobody checks.
        AssertEscalationSlopesAreOrdered();

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
        // The four casino miner-bots' governance identities (band / market category / greed) are drawn HERE,
        // at world creation, rather than lazily at the first company vote — so the stances are printed and
        // committed from the world's very first launch (developer request ahead of the P15.8 run) instead of
        // appearing only once the first company happens to found, years of game time later. The draw still
        // lands in a snapshot write (the PersistStateToDisk immediately below), which is the property the
        // original lazy call site was protecting; the vote path keeps calling it as an idempotent safety net.
        EnsureBotGovernancePreferences();
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
            CancelAndRefundStaleAuctionBids(block); // Step 14 (ND.8d, D-ND8d.7): cancel pending / refund confirmed bids to auctions that already closed
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

    // ND.8d.7 (D-ND8d.7, 2026-07-20 — §12.5.5) — the closing-race cleanup. A qualifying bid counts only once
    // MINED into a block at or before its target's WindowCloseUnixMs; a bid that arrives late is worthless
    // (D-ND4b.12 bars post-close bids, and ComputeAuctionLedger now excludes them from the tracked pool too).
    // Two parts, applied to the player and bot_1..4 alike:
    //   (1) MEMPOOL SWEEP — the "cash-back by not spending": drop any still-PENDING qualifying bid tx to a
    //       company whose auction has already resolved. It never confirms, so its UTXOs stay unspent and the
    //       sender keeps the BTC automatically — no refund tx needed. (In our model a mempool tx has not
    //       spent anything yet — the UTXO set is chain-derived; CLAUDE.md Pattern 2.)
    //   (2) EXPLICIT REFUND — the edge net: a qualifying bid CONFIRMED in THIS very block but stale (target
    //       resolved strictly BEFORE this block's timestamp) has already moved BTC to the company. The
    //       founded company's treasury sends it back, memo-tagged, network fee deducted (the ND.5-sweep
    //       precedent). With (1) in place this essentially never fires, but it guarantees no BTC is stranded.
    // Only QUALIFYING-bidder sends (player + bot_1..4) are touched — cast-miner sell-flow to a company is
    // economy, not a bid, and must pass through untouched.
    private static void CancelAndRefundStaleAuctionBids(Block block)
    {
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)) return;

        Dictionary<string, long> resolvedCloseByAddress = ComputeAuctionLedger(block.Timestamp)
            .Where(s => s.Status == NonMinerAuctionStatus.Resolved)
            .ToDictionary(s => s.NonMinerAddress, s => s.WindowCloseUnixMs);
        if (resolvedCloseByAddress.Count == 0) return;

        (HashSet<string> playerAddresses, Dictionary<string, string> botNodeIdByAddress) = BuildAuctionBidderIdentity(player);
        bool IsQualifyingBidder(string addr) => playerAddresses.Contains(addr) || botNodeIdByAddress.ContainsKey(addr);

        // (1) mempool sweep — drop pending qualifying bids to any resolved company from EVERY node's mempool.
        var staleTxIds = new HashSet<string>();
        foreach (Transaction tx in player.Blockchain.PendingTransactions)
        {
            if (IsQualifyingBidder(tx.Sender) && tx.Outputs.Any(o => resolvedCloseByAddress.ContainsKey(o.Address)))
                staleTxIds.Add(tx.TransactionId);
        }
        if (staleTxIds.Count > 0)
        {
            foreach (NodeAgent node in SharedNodesById.Values)
                node.Blockchain.PendingTransactions.RemoveAll(t => staleTxIds.Contains(t.TransactionId));
            GD.Print($"[ND.8d] Cancelled {staleTxIds.Count} pending stale auction bid(s) to resolved companies — BTC stays in the senders' wallets.");
        }

        // (2) explicit refund — a qualifying bid CONFIRMED in THIS block whose target resolved strictly
        // before this block's timestamp (boundary bids at == close still count, so are not stale).
        foreach (Transaction tx in block.Transactions)
        {
            if (tx.Sender == BlockchainService.CoinbaseSender || !IsQualifyingBidder(tx.Sender)) continue;
            foreach (TxOutput output in tx.Outputs)
            {
                if (!resolvedCloseByAddress.TryGetValue(output.Address, out long closeMs)) continue;
                if (block.Timestamp <= closeMs) continue; // still counted (mined at/before close) — not stale
                RefundStaleBid(block, output.Address, tx.Sender, output.Amount);
            }
        }
    }

    // ND.8d.7 — refund one confirmed-stale bid: the company that received it sends the amount back to the
    // original bidder, network fee deducted from the refund so the company's net outflow equals exactly the
    // stale amount it received (the ND.5-sweep "fee deducted from the total" precedent).
    private static void RefundStaleBid(Block block, string companyAddress, string bidderAddress, decimal amount)
    {
        if (amount <= 0m) return;
        NodeAgent? company = SharedNodesById.Values.FirstOrDefault(n =>
            n.WalletAddress == companyAddress || (n.ReceiveWallet?.OwnedAddresses.Contains(companyAddress) ?? false));
        if (company is null) return;

        decimal fee = NetworkFeePolicy.MedianFeeAt(block.Timestamp);
        decimal refund = Scripts.Finance.Money.Normalize(amount - fee);
        if (refund <= 0m) return; // the stale amount can't even cover the fee — nothing worth sending back

        if (BuildAndBroadcastUtxoSpend(company, bidderAddress, refund, fee, null, "AUCTION REFUND") != null)
            GD.Print($"[ND.8d] Refunded confirmed-stale bid: {company.NodeId} → {bidderAddress[..Math.Min(10, bidderAddress.Length)]}… {refund:F8} BTC (bid landed after auction close).");
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
        var breakdown = new List<CompanyFoundingBreakdown>(); // ND.9c — the frozen "how it was founded" record
        foreach (KeyValuePair<string, (decimal liveBtc, List<int> tiers)> kv in perDonor)
        {
            string donorAddress = kv.Key;
            decimal liveBtc = kv.Value.liveBtc;
            List<int> tiers = kv.Value.tiers;

            decimal participationShare = liveBtc / poolTotal;
            decimal baseTokens = Scripts.Finance.Money.Normalize(participationShare * StockBaseTokenPool);
            decimal bonusFraction = tiers.Sum(SlotBonusPercent) / 100m;
            decimal finalTokens = Scripts.Finance.Money.Normalize(baseTokens * (1m + bonusFraction));
            bool holdsTopThreeTier = tiers.Any(t => t <= NstTopTierCount);

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

            // ND.9c — persist the same values we just computed (no second copy of the math, no helper).
            breakdown.Add(new CompanyFoundingBreakdown
            {
                HolderId = holderId,
                Tiers = new List<int>(tiers),
                AmountBtcAtClose = liveBtc,
                ParticipationShare = participationShare,
                BaseTokens = baseTokens,
                BonusFraction = bonusFraction,
                FinalTokens = finalTokens,
                IsNst = holdsTopThreeTier
            });
        }

        var founding = new CompanyFounding
        {
            NonMinerNodeId = summary.NonMinerNodeId,
            NonMinerAddress = summary.NonMinerAddress,
            CompanyId = summary.CompanyId ?? string.Empty,
            FoundedAtUnixMs = block.Timestamp,
            Holdings = holdings,
            FoundingBreakdown = breakdown
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

    // D-ND8.15 step 4 — the NST band: a bidder occupying ANY tier at or above this mints NST (votes +
    // dividend rights) at founding; every other tracked donor mints PST (dividend rights only). Hoisted
    // from FoundCompany's literal 3 at ND.10f so GetPlayerProjectedStake below reads the SAME threshold —
    // the auction-time border colours can then never drift from what founding would actually mint.
    public const int NstTopTierCount = 3;

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
    // Step 15 P15.4e (D-15.15) — banks only: opened when selling ALL of a bank's collateral still can't
    // raise its quarterly FED installment. The ballot is a single dial: what share of the gap shareholders
    // absorb (dividends cut) vs. what the company's own SC reserve absorbs.
    public const string CompanyVoteKindShortfall = "shortfall";

    // ---- Step 15 P15.4b (D-15.13) — the bot GREED axis --------------------------------------------------

    public const string GreedNotSoGreedy = "not_so_greedy";
    public const string GreedAlmostGreedy = "almost_greedy";
    public const string GreedGreedy = "greedy";
    public const string GreedExtremelyGreedy = "extremely_greedy";
    private static readonly string[] GreedOrder = [GreedNotSoGreedy, GreedAlmostGreedy, GreedGreedy, GreedExtremelyGreedy];

    // P15.4c — greed as a multiplier on the category's DEFAULT quarterly payout rate. The ballot clamp is
    // [0, 2× default], so the ladder is written to span exactly that legal range, with `almost_greedy`
    // sitting at 1.0 — i.e. the pre-greed behaviour ("bots vote the standard") is now one of four stances
    // rather than the only one. *P15.8 calibration knobs.*
    private static decimal GreedPayoutMultiplier(string greed) => greed switch
    {
        GreedNotSoGreedy => 0.5m,
        GreedGreedy => 1.5m,
        GreedExtremelyGreedy => 2.0m,
        _ => 1.0m, // almost_greedy — the neutral stance
    };

    // P15.4e (§3.3's table) — what share of a bank's shortfall this bot votes to take out of SHAREHOLDERS'
    // dividends; the complement comes out of the company's own reserves. A greedy holder protects its own
    // dividend and makes the company pay. Default split with no/tied vote is 50/50. *P15.8 knobs.*
    private static decimal GreedDividendsCutPercent(string greed) => greed switch
    {
        GreedNotSoGreedy => 90m,
        GreedAlmostGreedy => 70m,
        GreedGreedy => 30m,
        GreedExtremelyGreedy => 10m,
        _ => DefaultShortfallDividendsCutPercent,
    };

    public const decimal DefaultShortfallDividendsCutPercent = 50m;

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
    // ND.8g — defensive cap on a company's PlayerClaimHistory (the BankTransferRecord=500 precedent).
    private const int MaxPlayerClaimHistoryPerCompany = 500;

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

    // P15.9a (2026-07-27) — project a BOT's global band stance onto the band of the company it is voting
    // at. A bot's CurrencyBandPreference is a position on a global "SC-ness" axis (CB1 100% … CB5 0%), NOT
    // a literal target. Casting it raw put ballots OUTSIDE the company's charter — at a CB1 company
    // (range [75,100]) a CB5 bot cast a literal 0, while the player's own dial is bounded to [75,100] at
    // both ends. And because CloseCompanyVote clamps only the FINAL weighted average, two or three
    // sub-floor ballots (the four bots are drawn as a permutation of the five bands) dragged that average
    // below the floor every quarter, pinning the result to exactly 75 forever — so the one ballot the game
    // PAUSES to collect could never move the outcome.
    //
    // PROJECT, never clamp (P15.9.1): Math.Clamp would collapse the CB5/CB4/CB3 bots onto the same floor —
    // three identical ballots and a result still pinned near it. Projection keeps all five stances distinct
    // inside every band, which is what makes the player's vote matter.
    //
    // Option C, "default-anchored" (D-15.24): the bot's OWN band default maps to the COMPANY's band
    // default, interpolating linearly on each side. That makes this the IDENTITY when the two bands agree
    // (a CB2 bot at a CB2 company votes CB2's own 75) — which plain [0,100]→[min,max] interpolation does
    // NOT give for the asymmetric bands CB2/CB4, whose default does not sit at the centre of their range.
    // Rounded to a whole percent (the player's SpinBox uses Step = 1, so whole percents are the shared
    // vocabulary), .5 away from zero. The final Clamp is a guard for the day a bound stops being an
    // integer, not a redundancy.
    public static decimal ProjectStanceIntoBand(decimal stanceScPercent, string companyBand)
    {
        (decimal min, decimal max) = BandScPercentBounds(companyBand);
        decimal anchor = BandDefaultScPercent(companyBand);
        decimal stance = Math.Clamp(stanceScPercent, 0m, 100m);

        // The two anchors that sit ON a bound (CB1's 100, CB5's 0) leave one side of the piecewise map
        // degenerate — the guards route the whole stance range through the side that exists.
        decimal projected;
        if (stance <= anchor && anchor > 0m)
        {
            projected = min + stance / anchor * (anchor - min);
        }
        else if (anchor < 100m)
        {
            projected = anchor + (stance - anchor) / (100m - anchor) * (max - anchor);
        }
        else
        {
            projected = anchor;
        }

        return Math.Clamp(Math.Round(projected, 0, MidpointRounding.AwayFromZero), min, max);
    }

    // §12.4.3 — light → dark, the axis a quarterly vote may shift by at most ±1 category, clamped within
    // ±1 of the roster DEFAULT (D-ND8.7 — a company never drifts more than one step from its nature).
    private static readonly string[] MarketCategoryOrder = ["official", "light_grey", "dark_grey", "black"];

    private static int MarketCategoryIndex(string category)
    {
        int index = Array.IndexOf(MarketCategoryOrder, category);
        return index >= 0 ? index : 0;
    }

    // ---- Step 15 P15.2 — the four CB1 bank companies -----------------------------------------------------

    // Is this founded company one of the four SC-dealer banks (D-15.6)? Resolved through the founding's
    // roster CompanyId, so it is true only ONCE THE COMPANY IS FOUNDED — which is exactly the gate the
    // selection framework wants (an unfounded bank can finance nothing).
    public static bool IsBankCompany(string nonMinerNodeId) =>
        _companyFoundings.TryGetValue(nonMinerNodeId, out CompanyFounding? founding)
        && CompanyRoster.IsBank(founding.CompanyId);

    // A founded bank's market category — the §5.1 selection distance axis. LOCKED at its roster default
    // (D-15.12, enforced in CloseCompanyVote), so live and default always agree for a bank; reading the
    // LIVE value keeps this honest if that guard is ever loosened. Null for anything that isn't a bank.
    public static string? BankCompanyCategory(string nonMinerNodeId) =>
        IsBankCompany(nonMinerNodeId) && _companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov)
            ? gov.MarketCategory
            : null;

    // Every founded bank, in founding order. Small (≤ 4) and only walked once per conversion.
    private static IEnumerable<CompanyGovernanceState> FoundedBanks() =>
        _companyGovernance.Values
            .Where(g => IsBankCompany(g.NonMinerNodeId))
            .OrderBy(g => _companyFoundings[g.NonMinerNodeId].FoundedAtUnixMs);

    // P15.2c — a founded bank's layer-1 balance sheet, created on first touch. Only ever called for a
    // node that IsBankCompany already accepted, so a sheet can never appear on a non-bank.
    private static BankBalanceSheet BankSheet(string bankNodeId)
    {
        if (!_bankState.TryGetValue(bankNodeId, out BankBalanceSheet? sheet))
        {
            sheet = new BankBalanceSheet();
            _bankState[bankNodeId] = sheet;
        }
        return sheet;
    }

    // Read-only view for the DEV/player readouts (P15.7). Null when the bank has never financed anything.
    public static BankBalanceSheet? GetBankBalanceSheet(string bankNodeId) =>
        _bankState.TryGetValue(bankNodeId, out BankBalanceSheet? sheet) ? sheet : null;

    public static decimal BankCollateralBtc(string bankNodeId) =>
        _bankState.TryGetValue(bankNodeId, out BankBalanceSheet? sheet) ? sheet.CollateralBtc : 0m;

    // P15.2c — record one provisioning event on the bank's own client book. Called by P15.3a's bank
    // provisioning path AFTER both legs have succeeded, so the book never records a half-executed swap.
    private static void RecordBankProvision(string bankNodeId, string companyNodeId, decimal btcBought, decimal scPaid, decimal priceUsd, Block block)
    {
        BankBalanceSheet sheet = BankSheet(bankNodeId);
        sheet.CollateralBtc = Scripts.Finance.Money.Normalize(sheet.CollateralBtc + btcBought);

        if (!sheet.Clients.TryGetValue(companyNodeId, out BankClientAccount? account))
        {
            account = new BankClientAccount();
            sheet.Clients[companyNodeId] = account;
        }
        account.BtcBought = Scripts.Finance.Money.Normalize(account.BtcBought + btcBought);
        account.ScPaid = Scripts.Finance.Money.Normalize(account.ScPaid + scPaid);
        account.ProvisionCount++;
        account.History.Add(new BankClientEntry
        {
            AtUnixMs = block.Timestamp,
            BlockIndex = block.Index,
            BtcBought = btcBought,
            ScPaid = scPaid,
            PriceUsd = priceUsd
        });
        if (account.History.Count > MaxBankClientHistory)
        {
            account.History.RemoveRange(0, account.History.Count - MaxBankClientHistory);
        }
    }

    // ---- §5.1 bank selection (P15.2d / D-15.20: A1 + B1 + casino fallback) --------------------------------

    // One candidate financier for a company's BTC→SC conversion. A null BankNodeId means THE CASINO — the
    // pre-first-bank fallback (D-15.20 (c)): before 2012-09, and for any category with no founded bank, the
    // provisional D-ND8.34 path stays exactly as it is today.
    public readonly record struct FinancierChoice(string? BankNodeId, decimal AmountSc, string Tier);

    public const string FinancierTierNearest = "nearest";   // tier 1 — the nearest-category founded bank
    public const string FinancierTierFullFunder = "funder"; // tier 2 — any single bank that can fund it all
    public const string FinancierTierSplit = "split";       // tier 3 — spread across banks, biggest capacity first
    public const string FinancierTierCasino = "casino";     // the fallback — no founded bank to route to

    // How much SC a founded bank can put up for one provision. In plan15 this is INFINITE: a bank funds
    // every provision with a FED auto-loan, and D-15.1 defers all credit-capacity limits to ND.8e. The
    // method exists so tiers 2/3 below are real, exercised code paths the day limits ship — at which point
    // this becomes the ONE place that has to change (B1: "the eventual limits are a data change, not a
    // rewrite"). Kept private: nothing outside selection should read a capacity that is deliberately fake.
    private static decimal BankFundingCapacitySc(string bankNodeId) => decimal.MaxValue;

    // Returns the ordered financiers for `amountSc`, summing to exactly that amount.
    //
    // Tier 1 — the founded bank nearest the company's CURRENT market category on the MarketCategoryOrder
    //          axis (|catCompany − catBank|), ties broken TOWARD OFFICIAL (D-15.20 A1: a business reaches
    //          for the cleaner bank first), then toward the earlier-founded bank so the result is total.
    // Tier 2 — no single nearest bank can cover it: the nearest bank that CAN fund the whole amount.
    // Tier 3 — nobody can alone: split across banks, nearest-category first then most-capacity.
    // Fallback — no founded bank at all: the casino (a single choice with a null BankNodeId).
    //
    // Selection is evaluated FRESH at each conversion, because a company's category can shift ±1 by vote
    // (§12.4.3) — a bank's cannot (D-15.12, P15.2b), which is what keeps the axis stable underneath.
    public static List<FinancierChoice> SelectFinanciers(string companyNodeId, decimal amountSc)
    {
        var result = new List<FinancierChoice>();
        amountSc = Scripts.Finance.Money.Normalize(amountSc);
        if (amountSc <= 0m)
        {
            return result;
        }

        // A bank never finances itself — its own CB1 inflows convert through the normal path (P15.4a).
        List<CompanyGovernanceState> banks = FoundedBanks()
            .Where(b => b.NonMinerNodeId != companyNodeId)
            .ToList();
        if (banks.Count == 0)
        {
            result.Add(new FinancierChoice(null, amountSc, FinancierTierCasino));
            return result;
        }

        int companyIndex = _companyGovernance.TryGetValue(companyNodeId, out CompanyGovernanceState? companyGov)
            ? MarketCategoryIndex(companyGov.MarketCategory)
            : 0;

        // Distance first; then A1's tie-break toward Official (lower category index); then founding order.
        List<CompanyGovernanceState> byPreference = banks
            .OrderBy(b => Math.Abs(MarketCategoryIndex(b.MarketCategory) - companyIndex))
            .ThenBy(b => MarketCategoryIndex(b.MarketCategory))
            .ThenBy(b => _companyFoundings[b.NonMinerNodeId].FoundedAtUnixMs)
            .ToList();

        // Tier 1 — today's ONLY outcome: capacity is infinite, so the nearest bank always takes it whole.
        CompanyGovernanceState nearest = byPreference[0];
        if (BankFundingCapacitySc(nearest.NonMinerNodeId) >= amountSc)
        {
            result.Add(new FinancierChoice(nearest.NonMinerNodeId, amountSc, FinancierTierNearest));
            return result;
        }

        // Tier 2 — the nearest bank that can still fund the WHOLE amount alone (dormant until limits ship).
        foreach (CompanyGovernanceState bank in byPreference)
        {
            if (BankFundingCapacitySc(bank.NonMinerNodeId) >= amountSc)
            {
                result.Add(new FinancierChoice(bank.NonMinerNodeId, amountSc, FinancierTierFullFunder));
                return result;
            }
        }

        // Tier 3 — split. Nearest-category first (the preference order above), and within an equal
        // preference the most free capacity first, so the fewest banks are involved (dormant until limits).
        decimal remaining = amountSc;
        foreach (CompanyGovernanceState bank in byPreference.OrderByDescending(b => BankFundingCapacitySc(b.NonMinerNodeId)))
        {
            decimal capacity = BankFundingCapacitySc(bank.NonMinerNodeId);
            if (capacity <= 0m) continue;

            decimal slice = Scripts.Finance.Money.Normalize(Math.Min(capacity, remaining));
            if (slice <= 0m) continue;

            result.Add(new FinancierChoice(bank.NonMinerNodeId, slice, FinancierTierSplit));
            remaining = Scripts.Finance.Money.Normalize(remaining - slice);
            if (remaining <= 0m) break;
        }

        // The banking layer couldn't raise all of it — the casino covers the remainder rather than letting
        // a company's conversion silently under-fill (unreachable while capacity is infinite).
        if (remaining > 0m)
        {
            result.Add(new FinancierChoice(null, remaining, FinancierTierCasino));
        }
        return result;
    }

    // ---- P15.2 DEV readouts (consumed by the CentralBank scene; an early slice of P15.7a) -----------------

    public readonly record struct BankLayerRow(
        string BankNodeId,
        string DisplayName,
        string MarketCategory,
        decimal CollateralBtc,
        int ClientCount,
        long FoundedAtUnixMs,
        decimal PendingShortfallSc,        // P15.4d — awaiting its shortfall vote
        decimal UnrecoverableShortfallSc); // P15.4e — insolvent; P15.5a will dissolve on this

    // The founded banks and their layer-1 books, in founding order. Empty before 2012-09.
    public static List<BankLayerRow> GetFoundedBankRows()
    {
        EnsureReady();
        return FoundedBanks()
            .Select(b =>
            {
                BankBalanceSheet? sheet = GetBankBalanceSheet(b.NonMinerNodeId);
                return new BankLayerRow(
                    b.NonMinerNodeId,
                    DescribeNodeForDev(b.NonMinerNodeId),
                    b.MarketCategory,
                    sheet?.CollateralBtc ?? 0m,
                    sheet?.Clients.Count ?? 0,
                    _companyFoundings[b.NonMinerNodeId].FoundedAtUnixMs,
                    b.PendingShortfallSc,
                    b.UnrecoverableShortfallSc);
            })
            .ToList();
    }

    public readonly record struct FinancierPreviewRow(
        string CompanyDisplay,
        string CompanyCategory,
        string FinancierDisplay,
        string Tier);

    // What SelectFinanciers WOULD pick right now for each founded non-bank company. A read-only preview
    // for verifying P15.2d before P15.3 wires the real reroute — it probes with a nominal 1 SC because
    // capacity is currently infinite (D-15.1), so the amount cannot change the answer. Revisit this probe
    // when ND.8e lands real credit limits and the amount starts to matter.
    public static List<FinancierPreviewRow> PreviewCompanyFinanciers()
    {
        EnsureReady();
        var rows = new List<FinancierPreviewRow>();
        foreach (CompanyGovernanceState gov in _companyGovernance.Values.OrderBy(g => g.NonMinerNodeId, StringComparer.Ordinal))
        {
            if (IsBankCompany(gov.NonMinerNodeId)) continue; // a bank converts its own inflows normally (P15.4a)

            List<FinancierChoice> choices = SelectFinanciers(gov.NonMinerNodeId, 1m);
            if (choices.Count == 0) continue;

            FinancierChoice first = choices[0];
            string financier = first.BankNodeId == null
                ? "The Casino (fallback)"
                : DescribeNodeForDev(first.BankNodeId);
            if (choices.Count > 1) financier += $" +{choices.Count - 1} more";

            rows.Add(new FinancierPreviewRow(
                DescribeNodeForDev(gov.NonMinerNodeId),
                gov.MarketCategory,
                financier,
                first.Tier));
        }
        return rows;
    }

    // Quarterly dates are calendar-anchored (founding date + 3 in-game months per quarter), not a flat
    // day count — matches how the roster/timeline anchors every other historical date.
    private static long AddMonthsMs(long baseUnixMs, int months) =>
        new DateTimeOffset(DateTimeOffset.FromUnixTimeMilliseconds(baseUnixMs).ToLocalTime().LocalDateTime.AddMonths(months))
            .ToUnixTimeMilliseconds();

    // The company's RAW on-chain spendable BTC. For a bank this includes its quarantined CollateralBtc —
    // use CompanyOwnBtc below anywhere governance means "the company's own money".
    private static decimal CompanyTreasuryBtc(string nonMinerNodeId) =>
        SharedNodesById.TryGetValue(nonMinerNodeId, out NodeAgent? node) ? AggregateSpendable(node) : 0m;

    // Step 15 P15.3a (D-15.4) — THE QUARANTINE. A bank holds two BTC streams in one wallet: its own CB1
    // business inflows (ordinary company money) and the CollateralBtc it bought while financing other
    // companies, which backs its FED debt and is sold only on a payment day (P15.4d). Every governance
    // computation that treats the treasury as the company's own money must therefore net the collateral
    // out — the reserve-mix conversion base, the quarterly dividend base (dividends on collateral would
    // pay away the very asset backing the debt) and the >30%-inflow vote baseline. Returns the plain
    // treasury for every non-bank, since BankCollateralBtc is 0 for them.
    private static decimal CompanyOwnBtc(string nonMinerNodeId) => Scripts.Finance.Money.Normalize(
        Math.Max(0m, CompanyTreasuryBtc(nonMinerNodeId) - BankCollateralBtc(nonMinerNodeId)));

    // D-ND8.13/D-ND8.26 — the four casino-miner-bots draw, once per world: a distinct 4-of-5 Currency
    // Band preference set (one band always unrepresented), a distinct full permutation of the 4 market
    // categories, and (P15.4b) a distinct permutation of the 4 greed stances — so all four stances of each
    // axis are always represented exactly once, and which bot holds which changes per world.
    //
    // Called at world creation (EnsureInitialized, so the stances exist and are printed from launch #1) and
    // again on every vote open as an idempotent safety net. Both call sites land inside a snapshot write,
    // which is what keeps the draw stable for the rest of the world's life.
    private static void EnsureBotGovernancePreferences()
    {
        IReadOnlyList<BotWalletRecord> minerBots = BotWalletRegistry.MinerBots;
        if (minerBots.Count == 0)
        {
            return;
        }

        if (_botGovernancePreferences.Count >= minerBots.Count)
        {
            BackfillGreedPreferences();
            return;
        }

        string[] bands = ["CB1", "CB2", "CB3", "CB4", "CB5"];
        string[] markets = (string[])MarketCategoryOrder.Clone();
        // P15.4b — greed is drawn as a distinct permutation too, so with four bots all four stances are
        // always represented exactly once: every world has one of each, and which bot holds which changes.
        string[] greeds = (string[])GreedOrder.Clone();
        ShuffleInPlace(bands);
        ShuffleInPlace(markets);
        ShuffleInPlace(greeds);
        for (int i = 0; i < minerBots.Count; i++)
        {
            _botGovernancePreferences[minerBots[i].NodeId] = new BotGovernancePreference
            {
                CurrencyBandPreference = bands[i % bands.Length],
                MarketCategoryPreference = markets[i % markets.Length],
                GreedPreference = greeds[i % greeds.Length]
            };
        }

        PrintBotGovernanceStances("drawn for this world");
    }

    // DEV observability (2026-07-26, developer request ahead of the P15.8 run) — print the four CASINO
    // MINER-BOTS' governance stances whenever they are decided or reloaded, so their behaviour over a long
    // session can be read against who they actually are. Each line carries the DERIVED effect of the greed
    // stance too (its payout multiplier and its shortfall dividends-cut %), because those are the numbers
    // that actually show up in the votes you will be watching — reading them off the same helpers the
    // ballots use, so the printout cannot drift from behaviour (§39.16 rule 6).
    //
    // Scope: bot_1..4 only. The Step-14 CAST miners (artforz, foundry_usa, …) have no governance identity —
    // they never bet, never bid and never hold stock — so there is nothing to print for them.
    private static void PrintBotGovernanceStances(string reason)
    {
        if (_botGovernancePreferences.Count == 0)
        {
            return;
        }

        GD.Print($"[Governance] Casino miner-bot stances ({reason}):");
        foreach (BotWalletRecord bot in BotWalletRegistry.MinerBots)
        {
            if (!_botGovernancePreferences.TryGetValue(bot.NodeId, out BotGovernancePreference? pref))
            {
                continue;
            }

            string greed = string.IsNullOrEmpty(pref.GreedPreference) ? GreedAlmostGreedy : pref.GreedPreference;
            // P15.9b — the band column is a GLOBAL stance, not a ballot: since P15.9a it is projected into
            // each company's own band before it is cast, so printing it bare would name a number that never
            // appears in any vote (§39.16 rule 6 — this printout exists to be read against observed
            // ballots). The per-band projections are spelled out so a stance can be matched to what it
            // actually votes, wherever it votes, off the SAME helper the ballot uses.
            GD.Print(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "  {0,-6} · band {1,-3} (global SC stance {2,3:F0}% → votes CB1 {7:F0} · CB2 {8:F0} · CB3 {9:F0} · CB4 {10:F0} · CB5 {11:F0}) · market {3,-10} · greed {4,-17} (payout ×{5:0.0} of category default, shortfall {6:F0}% from dividends)",
                bot.NodeId,
                pref.CurrencyBandPreference,
                BandDefaultScPercent(pref.CurrencyBandPreference),
                pref.MarketCategoryPreference,
                greed,
                GreedPayoutMultiplier(greed),
                GreedDividendsCutPercent(greed),
                ProjectStanceIntoBand(BandDefaultScPercent(pref.CurrencyBandPreference), "CB1"),
                ProjectStanceIntoBand(BandDefaultScPercent(pref.CurrencyBandPreference), "CB2"),
                ProjectStanceIntoBand(BandDefaultScPercent(pref.CurrencyBandPreference), "CB3"),
                ProjectStanceIntoBand(BandDefaultScPercent(pref.CurrencyBandPreference), "CB4"),
                ProjectStanceIntoBand(BandDefaultScPercent(pref.CurrencyBandPreference), "CB5")));
        }
    }

    // P15.4b — greed arrived AFTER the band/market axes, so a world whose preferences were already drawn
    // carries an empty value for it and would otherwise leave every bot on the neutral stance forever
    // (the Count >= minerBots.Count early-return above never re-draws). Backfilling only the empty slots
    // keeps the already-meaningful band/market choices untouched. This is why the field's default is ""
    // rather than "almost_greedy": a real drawn value has to be distinguishable from an absent one.
    private static void BackfillGreedPreferences()
    {
        var missing = _botGovernancePreferences.Values
            .Where(p => string.IsNullOrEmpty(p.GreedPreference))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        string[] greeds = (string[])GreedOrder.Clone();
        ShuffleInPlace(greeds);
        for (int i = 0; i < missing.Count; i++)
        {
            missing[i].GreedPreference = greeds[i % greeds.Length];
        }

        PrintBotGovernanceStances($"greed backfilled for {missing.Count} bot(s)");
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
            BaselineReserveBtc = CompanyOwnBtc(summary.NonMinerNodeId),
            InflowSinceBaselineBtc = 0m
        };
        _companyGovernance[summary.NonMinerNodeId] = gov;

        OpenCompanyVote(gov, founding, CompanyVoteKindFounding, block);
    }

    private static void TickCompanyGovernance(Block block)
    {
        // P15.5: the closed-company sweeps at the bottom must still run once every company has died, so
        // the early-out has to consider both books.
        if (_companyGovernance.Count == 0 && _closedCompanies.Count == 0)
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
                    // P15.6a — roll the SC throughput window. NOTE (R3, 2026-07-28): this is no longer the
                    // FBI's tolerance basis — that moved to the charter reserve, see FbiToleranceScFor. The
                    // per-quarter inflow is kept because it is an accurate, cheap business-activity metric
                    // (and a P15.8 calibration input); it is simply not what the meter reads any more.
                    gov.ScInflowLastQuarterSc = gov.ScInflowCurrentQuarterSc;
                    gov.ScInflowCurrentQuarterSc = 0m;
                    // P15.4d — THE PAYMENT DAY. A bank's whole "extra-lazy" carry ends here: it sells just
                    // enough collateral to cover this quarter's FED installment. Runs after the dividend
                    // settlement (the closing quarter's obligations are already met) and before the new
                    // quarterly vote, which is what a shortfall will later be measured against.
                    TryBankQuarterlyRepayment(gov, block);
                    gov.QuarterIndex++;
                    gov.NextQuarterlyDueMs = AddMonthsMs(founding.FoundedAtUnixMs, QuarterMonths * (gov.QuarterIndex + 1));
                    OpenCompanyVote(gov, founding, CompanyVoteKindQuarterly, block);
                }
                else if (gov.PendingShortfallSc > 0m)
                {
                    // P15.4e — takes precedence over the >30% special vote: an unpaid FED installment is
                    // the more urgent question, and it is opened only once the quarterly has closed, so the
                    // dividend it may cut has actually been finalized.
                    OpenCompanyVote(gov, founding, CompanyVoteKindShortfall, block);
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

        // Step 15 P15.5 — after the live companies have been processed, because dissolving mutates the
        // dictionary the loop above iterates. Order matters: kill the insolvent first, then (re)assign
        // custodied wallets — a bank that just died releases whatever it was holding — then forward the
        // dead companies' accumulated inflows to whoever now holds them.
        // P15.6 — the FBI runs BEFORE the dissolution sweep so a raid landing this block flows straight
        // into the same custody/inheritance chain a debt default does.
        TickFbiInvestigations(block);
        TryDissolveInsolventBanks(block);
        TryAssignSeizedWallets(block);
        SweepClosedCompanyInflows(block);
    }

    // ── ND.8b.6 (D-ND8.24/D-ND8.34) — automatic BTC→SC reserve conversion. Since Step 15 P15.3 the
    //    counterparty is the SELECTED BANK (§5.1), with the casino surviving only as the pre-first-bank
    //    fallback. ──

    // Calibration floors (v1): convert only when the SC-side deficit is ≥ 5% of total reserve value AND
    // the BTC to sell clears a dust/value floor — conversions stay chunky instead of one tiny tx per
    // inflow, and each conversion is an ORGANIC mempool tx that the fullness-parity budget counts.
    private const decimal ConversionDeficitTriggerFraction = 0.05m;
    private const decimal MinConversionBtc = 0.01m;

    // On-chain display memos for the two conversion counterparties. The bank leg gets its OWN tag because
    // it is load-bearing, not cosmetic: AccumulateCompanyInflows reads it to keep collateral out of the
    // receiving bank's business-inflow measure (D-15.4).
    private const string CompanyConversionMemo = "CONVERSION";
    private const string BankCollateralMemo = "COLLATERAL";
    private const string BankRepaymentMemo = "DEBT SERVICE"; // P15.4d — collateral sold to raise an installment
    private const string SeizedInflowMemo = "SEIZED";        // P15.5b — a dead company's inflow, forwarded to its absorber

    // Moves a founded company's reserves toward its voted ReserveScPercent target: an on-chain BTC send to
    // the financing counterparty (network median fee — the network's cost, never a desk fee) paired with an
    // SC credit into the company's ScReserve at the CLEAN market reference rate (the day's price,
    // D-ND8.24). Gated on the founding-day vote having closed ("per preferences + the founding vote"); v1
    // converts BTC→SC only — the reverse direction (a bank BUYING SC back with BTC) arrives with the
    // deferred SC→BTC rebalancing work.
    //
    // P15.3b: WHO pays the SC is now SelectFinanciers' answer — the nearest-category founded bank, funding
    // itself with a FED auto-loan (D-15.20), or the casino when no bank has founded yet.
    private static void TryConvertCompanyReserves(CompanyGovernanceState gov, Block block)
    {
        if (gov.VoteHistory.Count == 0 || gov.ReserveScPercent <= 0m)
        {
            return;
        }

        decimal? priceUsd = _marketData?.GetEffectivePriceUsd(
            DateTimeOffset.FromUnixTimeMilliseconds(block.Timestamp).LocalDateTime);
        if (priceUsd is not decimal price || price <= 0m)
        {
            return; // no market yet (structurally unreachable post-founding — auctions start at Market Birth)
        }

        // P15.3a — a bank's quarantined CollateralBtc is NOT part of its convertible reserves (D-15.4).
        decimal treasuryBtc = CompanyOwnBtc(gov.NonMinerNodeId);
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

        if (!SharedNodesById.TryGetValue(gov.NonMinerNodeId, out NodeAgent? company))
        {
            return;
        }

        decimal scAmount = Scripts.Finance.Money.Normalize(btcToSell * price);
        if (scAmount <= 0m)
        {
            return;
        }

        // §5.1 — evaluated fresh at every conversion (a company's category can shift by vote; a bank's
        // cannot, P15.2b). Today this always resolves to exactly ONE financier: capacity is infinite under
        // FED auto-loans (D-15.1), so tier 1 always wins.
        List<FinancierChoice> financiers = SelectFinanciers(gov.NonMinerNodeId, scAmount);
        if (financiers.Count == 0)
        {
            return;
        }

        // Tier 3 (a SPLIT across banks) would need the BTC leg split into several sends with their own
        // fees, which is not built — it is unreachable while capacity is infinite. If credit limits ever
        // make it reachable, fund the whole conversion from the casino rather than executing a half-split,
        // and build the multi-leg path THEN (the second of the two places ND.8e must touch — the first is
        // BankFundingCapacitySc).
        FinancierChoice choice = financiers[0];
        if (financiers.Count > 1)
        {
            GD.PushWarning($"[NetworkRoot] SelectFinanciers split {scAmount:F8} SC across {financiers.Count} financiers for {gov.NonMinerNodeId}; the multi-leg BTC path is unbuilt — falling back to the casino (P15.3b).");
            choice = new FinancierChoice(null, scAmount, FinancierTierCasino);
        }

        bool ok = choice.BankNodeId == null
            ? TryConvertViaCasino(gov, company, btcToSell, scAmount, fee, price, block)
            : TryConvertViaBank(gov, company, choice.BankNodeId, btcToSell, scAmount, fee, price, block);
        if (!ok)
        {
            return;
        }

        gov.ScReserve = Scripts.Finance.Money.Normalize(gov.ScReserve + scAmount);
        // P15.6a — this IS the company's SC throughput: every SC it takes in arrives through a conversion.
        gov.ScInflowCurrentQuarterSc = Scripts.Finance.Money.Normalize(gov.ScInflowCurrentQuarterSc + scAmount);
        AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "conversion", "btc_to_sc",
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "btc={0:F8};sc={1:F8};price={2:F2};via={3};tier={4}",
                btcToSell, scAmount, price, choice.BankNodeId ?? CasinoNodeId, choice.Tier));
    }

    // ── Step 15 P15.4d/e — extra-lazy FED repayment, and the shortfall it can produce ──────────────────

    // D-15.4 (Hybrid, full-quarter fraction): the share of its OUTSTANDING FED principal a bank owes each
    // quarter. Nothing is sold between payment days — that is the "lazy", and it is exactly what leaves the
    // bank long BTC in the interim (§1). *P15.8 calibration knob* (0.10 ⇒ a ~7-quarter half-life).
    private const decimal BankQuarterlyRepaymentFraction = 0.10m;

    // The bank sells collateral to the CASINO at the day's clean rate (no desk fee — the D-ND8.24 model the
    // company conversions already use). The casino is the designated SC liquidity backstop: it is the only
    // counterparty with an unlimited credit line (D-15.17), and it is already the SC side of the swap desk
    // and of the pre-first-bank conversion path.
    //
    // Worth being explicit about the monetary effect, because it is not always a net burn: if the casino
    // pays out of SC it already holds, circulation genuinely FALLS by the repayment. If the casino has to
    // auto-loan to buy, the same SC is minted as casino debt and immediately burned as bank debt — a debt
    // TRANSFER from the bank to the casino (which also ends up holding the BTC). Both are coherent; which
    // one dominates is a P15.8 observation, and a candidate input for ND.8e's credit-capacity work.
    private static void TryBankQuarterlyRepayment(CompanyGovernanceState gov, Block block)
    {
        if (_centralBank == null || !IsBankCompany(gov.NonMinerNodeId))
        {
            return;
        }

        string fedClientId = CentralBankService.BankClientId(gov.NonMinerNodeId);
        decimal outstanding = _centralBank.OutstandingDebt(fedClientId);
        if (outstanding <= 0m)
        {
            return;
        }

        decimal installmentSc = Scripts.Finance.Money.Normalize(outstanding * BankQuarterlyRepaymentFraction);
        if (installmentSc <= 0m)
        {
            return;
        }

        decimal? priceUsd = _marketData?.GetEffectivePriceUsd(
            DateTimeOffset.FromUnixTimeMilliseconds(block.Timestamp).LocalDateTime);
        if (priceUsd is not decimal price || price <= 0m)
        {
            return; // unpriceable day — try again next quarter rather than guess at a rate
        }

        decimal raisedSc = TrySellCollateralForSc(gov.NonMinerNodeId, installmentSc, price, block);
        if (raisedSc > 0m)
        {
            _centralBank.Repay(fedClientId, raisedSc, "quarterly"); // BURN — SC leaves existence
            AppendBankCreditTrace(block, "repay", gov.NonMinerNodeId, string.Empty, raisedSc,
                Scripts.Finance.Money.Normalize(raisedSc / price), price);
        }

        decimal gap = Scripts.Finance.Money.Normalize(installmentSc - raisedSc);
        if (gap > 0m)
        {
            // §3.3 — collateral alone couldn't cover it (BTC fell since purchase, or there was never
            // enough). The shortfall vote opens on a later tick, once the quarterly vote has closed.
            gov.PendingShortfallSc = gap;
            AppendBankCreditTrace(block, "shortfall_pending", gov.NonMinerNodeId, string.Empty, gap, 0m, price);
        }
    }

    // Sells at most `wantedSc`-worth of the bank's QUARANTINED collateral to the casino, on-chain, at the
    // clean rate. Returns the SC actually raised (0 if nothing could be sold). The network fee comes out of
    // the collateral pool too, so the book stays conservative — CollateralBtc never claims BTC the wallet
    // has already spent (the §39.9.1 rule that put the quarantine in P15.3 in the first place).
    private static decimal TrySellCollateralForSc(string bankNodeId, decimal wantedSc, decimal price, Block block)
    {
        if (_casinoSc == null
            || !SharedNodesById.TryGetValue(bankNodeId, out NodeAgent? bank)
            || !SharedNodesById.TryGetValue(CasinoNodeId, out NodeAgent? casino))
        {
            return 0m;
        }

        BankBalanceSheet? sheet = GetBankBalanceSheet(bankNodeId);
        if (sheet == null || sheet.CollateralBtc <= 0m)
        {
            return 0m;
        }

        decimal fee = NetworkFeePolicy.MedianFeeAt(block.Timestamp);
        decimal btcWanted = Scripts.Finance.Money.Normalize(wantedSc / price);
        decimal btcSellable = Scripts.Finance.Money.Normalize(Math.Max(0m, sheet.CollateralBtc - fee));
        decimal btcToSell = Math.Min(btcWanted, btcSellable);

        // Same dust floor the conversions use: below it the fee would eat most of the sale, so it is
        // honestly better to raise nothing and let the whole installment become a shortfall.
        if (btcToSell < Math.Max(MinConversionBtc, fee * 2m))
        {
            return 0m;
        }

        decimal scRaised = Scripts.Finance.Money.Normalize(btcToSell * price);
        if (scRaised <= 0m || !_casinoSc.TryPayCompanyProvisionSc(scRaised, "bank_repayment"))
        {
            return 0m;
        }

        if (BuildAndBroadcastUtxoSpend(bank, casino.WalletAddress, btcToSell, fee, null, BankRepaymentMemo) == null)
        {
            _casinoSc.ReceiveSwapSc(scRaised); // unwind the casino's SC leg (the SW.4 pattern)
            return 0m;
        }

        sheet.CollateralBtc = Scripts.Finance.Money.Normalize(Math.Max(0m, sheet.CollateralBtc - btcToSell - fee));
        return scRaised;
    }

    // P15.4e (D-15.7/D-15.15) — apply a closed shortfall vote's split and repay what it raised.
    //
    // Both cuts draw the SC out of the SAME place — the company's `ScReserve`, which is the only SC the
    // company actually holds. What the vote decides is WHO BEARS IT: a dividends cut also shrinks this
    // quarter's finalized SC dividend by the same amount (shareholders forgo it), while a reserves cut
    // leaves the dividend whole and lets the company's working capital take the hit. Already-dripped SC is
    // never clawed back — a cut is forward-looking.
    private static void ApplyShortfallVote(CompanyGovernanceState gov, decimal dividendsCutPercent, Block block)
    {
        decimal gap = gov.PendingShortfallSc;
        if (gap <= 0m || _centralBank == null)
        {
            return; // deliberately does NOT clear the pending gap — an unreachable FED must not erase a debt
        }
        gov.PendingShortfallSc = 0m;

        decimal dividendsShare = Scripts.Finance.Money.Normalize(gap * Math.Clamp(dividendsCutPercent, 0m, 100m) / 100m);

        // Whatever the reserve can actually cover, capped at the gap.
        decimal covered = Scripts.Finance.Money.Normalize(Math.Min(gap, Math.Max(0m, gov.ScReserve)));
        gov.ScReserve = Scripts.Finance.Money.Normalize(gov.ScReserve - covered);

        // The dividend is cut by its voted share of what was ACTUALLY raised (a cut can't exceed the
        // dividend that exists, so any overflow silently falls on the reserve side — which has already
        // paid it above).
        decimal dividendCut = Scripts.Finance.Money.Normalize(Math.Min(gov.QuarterDividendSc, Math.Min(dividendsShare, covered)));
        gov.QuarterDividendSc = Scripts.Finance.Money.Normalize(gov.QuarterDividendSc - dividendCut);

        if (covered > 0m)
        {
            _centralBank.Repay(CentralBankService.BankClientId(gov.NonMinerNodeId), covered, "shortfall");
        }

        decimal unrecoverable = Scripts.Finance.Money.Normalize(gap - covered);
        if (unrecoverable > 0m)
        {
            // Neither a full dividends cut nor the company's reserves could close it: the bank is
            // insolvent. P15.5a reads this and dissolves it (D-15.8); until that ships the flag simply
            // accumulates and is visible in the trace + the FED scene.
            gov.UnrecoverableShortfallSc = Scripts.Finance.Money.Normalize(gov.UnrecoverableShortfallSc + unrecoverable);
        }

        AppendBankCreditTrace(block, unrecoverable > 0m ? "shortfall_unrecoverable" : "shortfall_closed",
            gov.NonMinerNodeId, string.Empty, covered, 0m, 0m);
        AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "shortfall_apply", CompanyVoteKindShortfall,
            string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "gap={0:F8};dividendsCutPct={1:F2};dividendCut={2:F8};covered={3:F8};unrecoverable={4:F8}",
                gap, dividendsCutPercent, dividendCut, covered, unrecoverable));
    }

    // ── Step 15 P15.5 — dissolution, the Closed-Companies list, and seized-wallet custody ──────────────

    public const string ClosureReasonDebtDefault = "debt_default";
    public const string ClosureReasonFbiSeizure = "fbi_seizure";

    public static bool IsCompanyClosed(string nonMinerNodeId) => _closedCompanies.ContainsKey(nonMinerNodeId);

    public static CompanyClosure? GetCompanyClosure(string nonMinerNodeId) =>
        _closedCompanies.TryGetValue(nonMinerNodeId, out CompanyClosure? closure) ? closure : null;

    // Newest closure first — the order both the Closed-Companies readouts want.
    public static List<CompanyClosure> GetClosedCompanies() =>
        _closedCompanies.Values.OrderByDescending(c => c.ClosedAtUnixMs).ToList();

    // P15.5a (D-15.15/D-15.17) — kill a company. The record it leaves behind is the ONLY thing that
    // survives: the founding (and with it every holder's stock) and the governance state are both removed,
    // which is what makes "NST/PST holders lose their tokens and the company's future payments" literal
    // rather than a rule some later loop has to remember to honour (D-15.15, P15.5d).
    //
    // Anything the holders had ALREADY CLAIMED is theirs and untouched — it is in their own wallet. What
    // dies with the company is unclaimed claimables plus every future dividend.
    //
    // The company's BTC is deliberately NOT moved here: see the CompanyClosure doc comment for the custody
    // model. Its remaining SC reserve, by contrast, is real money the FED can be repaid with, so it is
    // applied against the debt on the way out (burning it — the same Option-A rule as any repayment).
    private static void DissolveCompany(CompanyGovernanceState gov, CompanyFounding founding, string reason, Block block)
    {
        string nodeId = gov.NonMinerNodeId;
        bool isBank = IsBankCompany(nodeId);
        string fedClientId = CentralBankService.BankClientId(nodeId);

        decimal scAtClosure = Math.Max(0m, gov.ScReserve);
        decimal repaid = 0m;
        if (reason == ClosureReasonFbiSeizure)
        {
            // P15.6c — the FBI takes the SC and self-funds on it. This is a TRANSFER of existing SC, so it
            // touches neither side of `circulation = grants + debt`; a debt default, by contrast, burns it
            // against the loan below. The BTC is not moved either way — see the custody model (§39.12.2).
            _fbiScFunds = Scripts.Finance.Money.Normalize(_fbiScFunds + scAtClosure);
        }
        else if (isBank && scAtClosure > 0m && _centralBank != null)
        {
            repaid = _centralBank.Repay(fedClientId, scAtClosure, "dissolution");
        }

        CompanyShareHolding? playerHolding = founding.Holdings.FirstOrDefault(h => h.HolderId == PlayerNodeId);
        gov.ClaimableByHolder.TryGetValue(PlayerNodeId, out CompanyClaimable? playerClaim);

        var closure = new CompanyClosure
        {
            NonMinerNodeId = nodeId,
            CompanyId = gov.CompanyId,
            ClosedAtUnixMs = block.Timestamp,
            Reason = reason,
            MarketCategory = gov.MarketCategory,
            WasBank = isBank,
            DebtAtClosureSc = isBank ? (_centralBank?.OutstandingDebt(fedClientId) ?? 0m) : 0m,
            ScAtClosure = scAtClosure,
            BtcAtClosure = CompanyTreasuryBtc(nodeId),
            PlayerNstAtClosure = playerHolding?.Nst ?? 0m,
            PlayerPstAtClosure = playerHolding?.Pst ?? 0m,
            PlayerUnclaimedBtcAtClosure = playerClaim?.Btc ?? 0m,
            PlayerUnclaimedScAtClosure = playerClaim?.Sc ?? 0m
        };
        _closedCompanies[nodeId] = closure;

        _companyGovernance.Remove(nodeId);
        _companyFoundings.Remove(nodeId);

        AppendBankCreditTrace(block, "dissolution", nodeId, string.Empty, closure.DebtAtClosureSc, closure.BtcAtClosure, 0m);
        GD.Print($"[NetworkRoot] Company DISSOLVED — {DescribeNodeForDev(nodeId)} ({reason}); FED loss {closure.DebtAtClosureSc:F8} SC (repaid {repaid:F8} from its reserve), {closure.BtcAtClosure:F8} BTC left in custody.");
    }

    // P15.5a — the debt-default trigger. A bank carrying an unrecoverable shortfall (P15.4e: neither a full
    // dividends cut nor its own reserves could close the gap) is insolvent and dies. Collected and applied
    // OUTSIDE the governance loop, since dissolving mutates the very dictionary that loop iterates.
    private static void TryDissolveInsolventBanks(Block block)
    {
        List<CompanyGovernanceState> doomed = _companyGovernance.Values
            .Where(g => g.UnrecoverableShortfallSc > 0m && IsBankCompany(g.NonMinerNodeId))
            .ToList();

        foreach (CompanyGovernanceState gov in doomed)
        {
            if (_companyFoundings.TryGetValue(gov.NonMinerNodeId, out CompanyFounding? founding))
            {
                DissolveCompany(gov, founding, ClosureReasonDebtDefault, block);
            }
        }
    }

    // P15.5c (D-15.18, "O18-A") — the FED assigns each custodied wallet to a SOLVENT bank of the MATCHING
    // market category, which then processes its inflows through its own band/level and normal governance
    // votes (D-15.12 — never force-converted, never a bespoke per-deposit vote). Until such a bank exists
    // — every 2011–2012 seizure predates the first bank founding, and a category may simply have none —
    // the wallet stays with the FED, held 100% as BTC.
    //
    // "Solvent" is the meaningful qualifier: a bank carrying its own shortfall cannot be handed more to
    // manage, and one that dissolves later releases what it holds back to FED custody (its own closure
    // clears the assignment below).
    private static void TryAssignSeizedWallets(Block block)
    {
        if (_closedCompanies.Count == 0) return;

        foreach (CompanyClosure closure in _closedCompanies.Values)
        {
            // Release an assignment whose holder has since died — back to FED custody, eligible again.
            if (!string.IsNullOrEmpty(closure.InheritingBankNodeId)
                && !_companyGovernance.ContainsKey(closure.InheritingBankNodeId))
            {
                closure.InheritingBankNodeId = string.Empty;
                closure.InheritedAtUnixMs = 0;
            }

            if (!string.IsNullOrEmpty(closure.InheritingBankNodeId)) continue;

            CompanyGovernanceState? heir = FoundedBanks().FirstOrDefault(b =>
                b.MarketCategory == closure.MarketCategory
                && b.NonMinerNodeId != closure.NonMinerNodeId
                && b.PendingShortfallSc <= 0m
                && b.UnrecoverableShortfallSc <= 0m);
            if (heir == null) continue; // no matching solvent bank yet — the FED keeps holding it as BTC

            closure.InheritingBankNodeId = heir.NonMinerNodeId;
            closure.InheritedAtUnixMs = block.Timestamp;
            AppendBankCreditTrace(block, "wallet_inherited", heir.NonMinerNodeId, closure.NonMinerNodeId, 0m, 0m, 0m);
            GD.Print($"[NetworkRoot] Seized wallet {DescribeNodeForDev(closure.NonMinerNodeId)} ({closure.MarketCategory}) inherited by {DescribeNodeForDev(heir.NonMinerNodeId)}.");
        }
    }

    // P15.5b (D-15.8) — the off-UI income redirection. A dead company's address keeps receiving whatever
    // automatic inflows were already scheduled to it (the cast sell-flow still picks it, the network does
    // not know it died). Each block, everything sitting in a closed company's wallet is forwarded to its
    // absorber — which exists only once P15.5c has assigned a bank. While the FED holds the wallet, the
    // coins simply accumulate in place, which IS the custody model.
    //
    // The forwarded BTC lands as the heir's ordinary business inflow, not as collateral: it is a windfall
    // it now owns, and its own band/level governance decides what to do with it (D-15.12). Recovery is
    // tracked in BTC and valued live by the DEV readout — never frozen at a historical price.
    private static void SweepClosedCompanyInflows(Block block)
    {
        if (_closedCompanies.Count == 0) return;

        decimal fee = NetworkFeePolicy.MedianFeeAt(block.Timestamp);
        foreach (CompanyClosure closure in _closedCompanies.Values)
        {
            if (string.IsNullOrEmpty(closure.InheritingBankNodeId)) continue;
            if (!SharedNodesById.TryGetValue(closure.NonMinerNodeId, out NodeAgent? dead)
                || !SharedNodesById.TryGetValue(closure.InheritingBankNodeId, out NodeAgent? heir))
            {
                continue;
            }

            decimal balance = AggregateSpendable(dead);
            decimal amount = Scripts.Finance.Money.Normalize(balance - fee);
            // Same dust floor as every other automated send: below it the fee eats the transfer, so let it
            // keep accumulating until it is worth moving.
            if (amount < Math.Max(MinConversionBtc, fee * 2m)) continue;

            if (BuildAndBroadcastUtxoSpend(dead, heir.WalletAddress, amount, fee, null, SeizedInflowMemo) == null)
            {
                continue;
            }

            closure.RecoveredBtc = Scripts.Finance.Money.Normalize(closure.RecoveredBtc + amount);
            AppendBankCreditTrace(block, "seized_inflow", closure.InheritingBankNodeId, closure.NonMinerNodeId, 0m, amount, 0m);
        }
    }

    // ── Step 15 P15.6 — the FBI investigation / seizure thread (D-15.14/D-15.19/D-15.21) ───────────────
    //
    // THE HYBRID (D-15.21): F1's investigation meter decides *who is a target* — deterministic and
    // player-legible, so keeping a company's SC lean is a real lever — and a capped F2-style roll on top
    // decides *which block the raid actually lands*, so there is suspense without pure randomness punishing
    // good play.
    //
    // Timeline gate (D-15.14): the FBI does not exist in-game before **14 Jun 2011**, the date Gavin
    // Andresen presented Bitcoin to the CIA via In-Q-Tel. (He did not meet the FBI; the CIA connection is
    // flavour only and never mechanically involved — the date is simply when "law enforcement noticed
    // Bitcoin" becomes historically honest.) Routed through TimelineConfig.Shift like every other anchor.
    private static readonly DateTime FbiActivationLocal = TimelineConfig.Shift(new DateTime(2011, 6, 14));

    // Per-category tolerated SC balance, as a multiple of the company's own recent SC throughput (D-15.21).
    // Darker ⇒ lower: a licensed exchange sitting on a float is normal, a black-market stall sitting on the
    // same fortune is a flag. Official is EXEMPT — never flagged on SC alone. *All P15.8 calibration knobs.*
    private static decimal FbiToleranceMultiplier(string marketCategory) => marketCategory switch
    {
        "light_grey" => 8m,
        "dark_grey" => 3m,
        "black" => 1m,
        _ => -1m, // official — no ceiling
    };

    // Meter tuning. A block is ~16h40m of game time (≈1.5 blocks/in-game-day, ≈135 per quarter), so these
    // are sized against quarters, not seconds: at pressure 1 a company flags in ~200 blocks (~1.5 quarters);
    // a badly-over black company flags in well under one. *P15.8 knobs.*
    public const decimal InvestigationFlagThreshold = 100m;
    private const decimal InvestigationGainPerBlock = 0.5m;
    private const decimal InvestigationDecayPerBlock = 1.0m;
    private const decimal InvestigationOverageCap = 4m;   // an overage ratio past this adds no more pressure
    // The raid roll, applied ONLY to the single highest-priority flagged target each block (see below).
    private const decimal SeizureRollBasePercent = 0.5m;  // × darkness × (score ÷ threshold)
    private const decimal SeizureRollCapPercent = 2.0m;   // the "capped" half of the hybrid

    // R3 (2026-07-28) — THE SCORE ITSELF NEEDS A CEILING, not just its gain rate. InvestigationOverageCap
    // bounds how fast the meter climbs; nothing bounded how HIGH it climbed, and gain (up to
    // 0.5 × 4 overage × 4 darkness = 8/block for a black company) outruns decay (1.0/block) by 8×. So a
    // company held over tolerance for 200 blocks accrued ~1,600 and then needed ~1,600 blocks — well over
    // two in-game years — to cool back to zero, staying red on the FED board the whole time. The decay is
    // documented as "the player's lever"; unbounded accumulation quietly took the lever away.
    //
    // The value is not arbitrary: the raid roll is min(2%, 0.5% × darkness × score/threshold), which
    // SATURATES at its 2% cap by score = 2 × threshold for the LIGHTEST non-exempt category (darkness 2)
    // and earlier for every darker one. Past 2× threshold, extra score therefore changes nothing about the
    // risk — it is pure dead weight that only lengthens the cooldown. Capping there bounds the worst-case
    // cool-off at 200 blocks (~4.5 in-game months) while leaving time-to-flag completely unchanged.
    //
    // Applied on every accrual, so it also self-corrects the inflated scores that companies accumulated
    // under the retired throughput basis (see FbiToleranceScFor) — they clamp to the ceiling on the next
    // block rather than needing a one-off amnesty pass. *P15.8 knob like the rest of the meter.*
    private const decimal InvestigationScoreCap = 2m * InvestigationFlagThreshold;

    // The FBI's own budget: an initial FED grant at activation, then self-funding from what it seizes
    // (D-15.21). The grant is booked as a FED loan on client "fbi" rather than conjured, so
    // `circulation = grants + debt` still holds — it is simply never repaid, like the casino's (D-15.17).
    // Seized SC is a TRANSFER of existing SC and touches neither side of the invariant.
    public const string FbiClientId = "fbi";
    private const decimal FbiInitialGrantSc = 100_000m; // *P15.8 knob*
    private static decimal _fbiScFunds;
    private static bool _fbiActivated;

    public static decimal FbiScFunds => _fbiScFunds;
    public static bool FbiActivated => _fbiActivated;
    public static DateTime FbiActivationDateLocal => FbiActivationLocal;

    // A company's tolerated SC balance right now. Negative = exempt (Official, or unpriceable — see below).
    //
    // R3 (2026-07-28) — THE BASIS IS THE CHARTER, NOT RECENT THROUGHPUT. The original P15.6a rule measured
    // the tolerance off `max(ScInflowLastQuarterSc, ScInflowCurrentQuarterSc)`, i.e. a FLOW over ≤2 quarters
    // judged against a STOCK that is meant to be HELD. The two disagree structurally: TryConvertCompanyReserves
    // stops converting the moment a company reaches its voted reserve target, so a perfectly healthy company
    // reports zero throughput two quarters later, `tolerance` collapses to 0.00, FbiOverageRatio pins it at the
    // overage cap, and it is flagged within ~13–25 blocks depending on darkness. Every non-Official company
    // that ever reaches its target was therefore guaranteed a federal file — which is what a 2011 playtest hit
    // (three companies under investigation, all reading "0.00 SC tolerated"). The wealth was not unexplained;
    // it was explained by a conversion the window had simply forgotten.
    //
    // "Explained wealth" is now what the company's OWN shareholders voted to hold: the charter reserve
    // (`ReserveScPercent` of total company value), times the category's tolerance multiple. A company sitting
    // exactly at its target has overage 0 and cools off; one hoarding SC well beyond its charter still heats
    // up. The player's levers stay legible and get sharper — vote the reserve % down, or hold a lighter
    // category — and neither is a stale accident of when the last conversion happened.
    //
    // Valuation basis is deliberately IDENTICAL to TryConvertCompanyReserves' own (CompanyOwnBtc at the
    // chain-tip day's price + ScReserve), so the figure the FBI judges can never drift from the figure the
    // conversion targets (§39.16 rule 6). No market price ⇒ exempt this block rather than judged against a
    // BTC treasury we cannot value; structurally unreachable post-founding (auctions start at Market Birth).
    //
    // A 0% charter is a REAL zero, not the accidental one this replaced: it means "our charter says hold no
    // SC" while the company holds some, which is exactly what the meter is for — and it is escapable, since
    // dividends drain SC and the reserve % is itself votable. *The multiples remain P15.8 knobs.*
    public static decimal FbiToleranceScFor(CompanyGovernanceState gov)
    {
        decimal multiplier = FbiToleranceMultiplier(gov.MarketCategory);
        if (multiplier < 0m) return -1m;

        long nowMs = _lastMinedBlock?.Timestamp ?? 0L;
        decimal? priceUsd = nowMs > 0L
            ? _marketData?.GetEffectivePriceUsd(DateTimeOffset.FromUnixTimeMilliseconds(nowMs).LocalDateTime)
            : null;
        if (priceUsd is not decimal price || price <= 0m) return -1m;

        decimal totalValueSc = CompanyOwnBtc(gov.NonMinerNodeId) * price + gov.ScReserve;
        decimal charterSc = totalValueSc * gov.ReserveScPercent / 100m;
        return Scripts.Finance.Money.Normalize(multiplier * charterSc);
    }

    // How far over the line, as a ratio, capped. A company holding SC with NO throughput to explain it sits
    // at the cap by construction — which is the intended reading of "unexplained wealth", not an edge case.
    private static decimal FbiOverageRatio(CompanyGovernanceState gov, decimal tolerance)
    {
        if (tolerance < 0m) return 0m; // exempt
        if (tolerance == 0m) return gov.ScReserve > 0m ? InvestigationOverageCap : 0m;
        return Math.Max(0m, Math.Min(InvestigationOverageCap, gov.ScReserve / tolerance - 1m));
    }

    // Darkness weight: light_grey 2 … black 4 (official never reaches here — it is exempt above).
    private static decimal FbiDarkness(string marketCategory) => MarketCategoryIndex(marketCategory) + 1m;

    // R3 — how many blocks a file still needs to close, at the decay rate, if it stays under tolerance.
    // Shares InvestigationDecayPerBlock with the tick itself, so the estimate cannot drift from the
    // mechanism (§39.16 rule 6). Exists because "will this red row ever go away?" was a question the board
    // could not answer, which is exactly the ambiguity rule 6 exists to prevent.
    public static int FbiBlocksToClear(decimal score) =>
        score <= 0m || InvestigationDecayPerBlock <= 0m
            ? 0
            : (int)Math.Ceiling(score / InvestigationDecayPerBlock);

    // P15.6a/b/c — one pass per block. Accrues/decays every company's meter, then rolls the raid for the
    // SINGLE highest-priority flagged target: **non-banks first, ranked by overage; banks LAST** (D-15.19 —
    // the FBI builds evidence on the small anomalies before striking a big fish). One raid per block at
    // most, which also keeps the thread from clearing the board in a burst.
    private static void TickFbiInvestigations(Block block)
    {
        if (_companyGovernance.Count == 0) return;

        DateTime nowLocal = DateTimeOffset.FromUnixTimeMilliseconds(block.Timestamp).LocalDateTime;
        if (nowLocal < FbiActivationLocal) return;

        if (!_fbiActivated)
        {
            _fbiActivated = true;
            _centralBank?.DrawLoan(FbiClientId, FbiInitialGrantSc, "fbi_activation");
            _fbiScFunds = Scripts.Finance.Money.Normalize(_fbiScFunds + FbiInitialGrantSc);
            GD.Print($"[NetworkRoot] FBI ACTIVATED ({nowLocal:yyyy-MM-dd}) — initial federal grant {FbiInitialGrantSc:F2} SC (D-15.14).");
            AppendBankCreditTrace(block, "fbi_activated", FbiClientId, string.Empty, FbiInitialGrantSc, 0m, 0m);
        }

        var flagged = new List<(CompanyGovernanceState gov, decimal overage, bool isBank)>();
        foreach (CompanyGovernanceState gov in _companyGovernance.Values)
        {
            decimal tolerance = FbiToleranceScFor(gov);
            decimal overage = FbiOverageRatio(gov, tolerance);

            if (tolerance >= 0m && overage > 0m)
            {
                // Clamped to InvestigationScoreCap: past 2× threshold the roll has saturated, so further
                // accrual would only buy an unpayable cooldown later (R3 — see the constant).
                gov.InvestigationScore = Scripts.Finance.Money.Normalize(Math.Min(InvestigationScoreCap,
                    gov.InvestigationScore + InvestigationGainPerBlock * overage * FbiDarkness(gov.MarketCategory)));
            }
            else
            {
                // Back under tolerance: the heat comes off. This is the player's lever — a company kept lean
                // (or voted lighter) genuinely stops being a target. The ceiling is applied on the way DOWN
                // too, so a file inflated under the retired throughput basis drops to it on its first cooling
                // block instead of serving out a sentence for a defect.
                gov.InvestigationScore = Scripts.Finance.Money.Normalize(
                    Math.Max(0m, Math.Min(InvestigationScoreCap, gov.InvestigationScore) - InvestigationDecayPerBlock));
            }

            // A raid needs a LIVE case, not just a thick file (R3, 2026-07-28). The score decays at
            // InvestigationDecayPerBlock, so a company that gets back under its tolerance still carries a
            // ≥threshold score for ~100 blocks — during which it could be seized for a condition that no
            // longer holds. Requiring overage > 0 to be raid-eligible makes the decay mean what it says
            // ("cooling off"), turns the player's lever into an IMMEDIATE effect rather than a 100-block
            // wait, and keeps the file itself intact so a relapse re-arms at once instead of from zero.
            if (gov.InvestigationScore >= InvestigationFlagThreshold && overage > 0m)
            {
                flagged.Add((gov, overage, IsBankCompany(gov.NonMinerNodeId)));
            }
        }

        if (flagged.Count == 0) return;

        // D-15.19's priority, as a single ordering: banks sort last, everything else by how far over it is.
        (CompanyGovernanceState gov, decimal overage, bool isBank) target = flagged
            .OrderBy(f => f.isBank)
            .ThenByDescending(f => f.overage)
            .First();

        decimal chance = Math.Min(SeizureRollCapPercent,
            SeizureRollBasePercent * FbiDarkness(target.gov.MarketCategory)
                * (target.gov.InvestigationScore / InvestigationFlagThreshold));

        if ((decimal)Random.Shared.NextDouble() * 100m >= chance) return;

        if (_companyFoundings.TryGetValue(target.gov.NonMinerNodeId, out CompanyFounding? founding))
        {
            GD.Print($"[NetworkRoot] FBI RAID — {DescribeNodeForDev(target.gov.NonMinerNodeId)} seized (score {target.gov.InvestigationScore:F1}, roll chance {chance:F2}%).");
            DissolveCompany(target.gov, founding, ClosureReasonFbiSeizure, block);
        }
    }

    // Step 15 P15.7c (D-15.9) — everything a shareholder needs to judge a BANK they hold stock in. Computed
    // from the same constants and helpers the mechanisms themselves use (BankQuarterlyRepaymentFraction,
    // the FED account, BankCollateralBtc), so a displayed installment can never disagree with the one that
    // will actually be charged — §39.16 rule 6. Collateral is valued LIVE, never at a frozen day.
    public readonly record struct BankLendingSummary(
        decimal FedDebtSc,
        decimal TotalDrawnSc,
        decimal TotalRepaidSc,
        decimal CollateralBtc,
        decimal CollateralValueSc,
        decimal NextInstallmentSc,
        long NextPaymentDueMs,
        int ClientCount,
        decimal PendingShortfallSc,
        decimal UnrecoverableShortfallSc);

    // Null for anything that is not a founded bank.
    public static BankLendingSummary? GetBankLendingSummary(string nonMinerNodeId)
    {
        if (!IsBankCompany(nonMinerNodeId)
            || !_companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov))
        {
            return null;
        }

        string fedClientId = CentralBankService.BankClientId(nonMinerNodeId);
        decimal debt = _centralBank?.OutstandingDebt(fedClientId) ?? 0m;
        decimal collateral = BankCollateralBtc(nonMinerNodeId);

        // Valued at the chain tip's day — the world's "now" (the ND.8g "always live, never a frozen
        // historical day" rule). 0 when the market can't price that day, which the caller renders as "n/a"
        // rather than as a real zero.
        long nowMs = _lastMinedBlock?.Timestamp ?? 0L;
        decimal? priceUsd = nowMs > 0L
            ? _marketData?.GetEffectivePriceUsd(DateTimeOffset.FromUnixTimeMilliseconds(nowMs).LocalDateTime)
            : null;
        decimal collateralValue = priceUsd is decimal p && p > 0m
            ? Scripts.Finance.Money.Normalize(collateral * p)
            : 0m;

        return new BankLendingSummary(
            debt,
            _centralBank?.TotalDrawn(fedClientId) ?? 0m,
            _centralBank?.TotalRepaid(fedClientId) ?? 0m,
            collateral,
            collateralValue,
            Scripts.Finance.Money.Normalize(debt * BankQuarterlyRepaymentFraction),
            gov.NextQuarterlyDueMs,
            GetBankBalanceSheet(nonMinerNodeId)?.Clients.Count ?? 0,
            gov.PendingShortfallSc,
            gov.UnrecoverableShortfallSc);
    }

    public readonly record struct FbiInvestigationFile(
        string NonMinerNodeId,
        string DisplayName,
        string MarketCategory,
        bool IsBank,
        decimal Score,
        decimal ScReserve,
        decimal ToleranceSc,
        decimal Overage);

    // P15.6 DEV readout — every company carrying a file, in the SAME order the raid roll picks its target
    // (non-banks first by overage, banks last). Shares FbiToleranceScFor/FbiOverageRatio with the roll, so
    // a displayed figure cannot drift from the mechanism.
    public static List<FbiInvestigationFile> GetFbiInvestigationFiles()
    {
        EnsureReady();
        var files = new List<FbiInvestigationFile>();
        foreach (CompanyGovernanceState gov in _companyGovernance.Values)
        {
            decimal tolerance = FbiToleranceScFor(gov);
            if (tolerance < 0m) continue; // Official — exempt
            decimal overage = FbiOverageRatio(gov, tolerance);
            if (overage <= 0m && gov.InvestigationScore <= 0m) continue;

            files.Add(new FbiInvestigationFile(
                gov.NonMinerNodeId,
                DescribeNodeForDev(gov.NonMinerNodeId),
                gov.MarketCategory,
                IsBankCompany(gov.NonMinerNodeId),
                gov.InvestigationScore,
                gov.ScReserve,
                tolerance,
                overage));
        }

        return files.OrderBy(f => f.IsBank).ThenByDescending(f => f.Overage).ToList();
    }

    // P15.6d — the player-facing risk line for a company they hold stock in: how close it is to a raid, and
    // what to do about it. Returns null when there is nothing to warn about (FBI not active yet, category
    // exempt, or the company comfortably under its tolerance).
    public static string? GetFbiInvestigationWarning(string nonMinerNodeId)
    {
        if (!_fbiActivated || !_companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov))
        {
            return null;
        }

        decimal tolerance = FbiToleranceScFor(gov);
        if (tolerance < 0m) return null; // Official — never flagged on SC alone

        decimal overage = FbiOverageRatio(gov, tolerance);
        if (overage <= 0m && gov.InvestigationScore <= 0m) return null;

        decimal progress = Math.Min(100m, gov.InvestigationScore / InvestigationFlagThreshold * 100m);
        // Rule 6 — this line must state exactly what TickFbiInvestigations will do, and since R3 a raid needs
        // BOTH a ≥threshold file and a live overage. A thick file with no current overage is "open, not
        // active": no raid can land while it stays that way, but the file has not been closed either.
        bool raidEligible = gov.InvestigationScore >= InvestigationFlagThreshold && overage > 0m;
        string state = raidEligible
            ? "FLAGGED — a raid can land on any block"
            : overage > 0m
                ? "under investigation — the file is growing"
                : gov.InvestigationScore >= InvestigationFlagThreshold
                    ? "file open but INACTIVE — back under tolerance, no raid while it stays there"
                    : "cooling off — back under tolerance";

        // R3: the advice names the ACTUAL lever now that the basis is the charter — the reserve % is voted,
        // so the board can bring the heat down itself; the old text told the player to "convert less SC",
        // which was never a control any shareholder held.
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"⚖ Federal investigation: {state} ({progress:F0}% of the threshold). SC reserve {gov.ScReserve:N2} vs a tolerated {tolerance:N2} for a '{gov.MarketCategory}' business holding a {gov.ReserveScPercent:F0}% SC charter. Voting the SC reserve % up to cover what it actually holds — or holding a lighter market category — brings the heat down.");
    }

    // The pre-first-bank fallback (D-15.20 (c)) — unchanged ND.8b.6 behaviour: SC out of the casino's Main
    // Balance (auto-loan when short, so the draw still lands on the casino's FED account), BTC in.
    private static bool TryConvertViaCasino(CompanyGovernanceState gov, NodeAgent company, decimal btcToSell, decimal scAmount, decimal fee, decimal price, Block block)
    {
        if (_casinoSc == null || !SharedNodesById.TryGetValue(CasinoNodeId, out NodeAgent? casino))
        {
            return false;
        }

        if (!_casinoSc.TryPayCompanyProvisionSc(scAmount, "company_conversion"))
        {
            return false;
        }

        if (BuildAndBroadcastUtxoSpend(company, casino.WalletAddress, btcToSell, fee, null, CompanyConversionMemo) == null)
        {
            _casinoSc.ReceiveSwapSc(scAmount); // unwind the SC leg on a failed broadcast (the SW.4 pattern)
            return false;
        }
        return true;
    }

    // P15.3a — THE BANK PROVISIONING PATH (§3.2). The bank borrows the SC from the FED (minting it as
    // "bank:<id>" debt), the company receives it, and the BTC the company sells lands in the bank's wallet
    // as QUARANTINED CollateralBtc. Note what the bank does NOT do: it never touches its own ScReserve —
    // the borrowed SC passes straight through to the company, leaving the bank with a FED debt on one side
    // and collateral on the other. That spread, carried until the quarterly repayment (P15.4d), is the
    // whole economic point of the reform (§1).
    //
    // Order mirrors the casino path: SC leg first, then the on-chain send, with the SC leg unwound on a
    // failed broadcast — here by REPAYING the just-drawn loan, which burns the SC back out of existence
    // and leaves `circulation = grants + debt` exactly as it was.
    private static bool TryConvertViaBank(CompanyGovernanceState gov, NodeAgent company, string bankNodeId, decimal btcToSell, decimal scAmount, decimal fee, decimal price, Block block)
    {
        if (_centralBank == null || !SharedNodesById.TryGetValue(bankNodeId, out NodeAgent? bank))
        {
            return false;
        }

        string fedClientId = CentralBankService.BankClientId(bankNodeId);
        _centralBank.DrawLoan(fedClientId, scAmount, "provision");

        if (BuildAndBroadcastUtxoSpend(company, bank.WalletAddress, btcToSell, fee, null, BankCollateralMemo) == null)
        {
            _centralBank.Repay(fedClientId, scAmount, "provision_unwind");
            return false;
        }

        // Both legs succeeded — only now does the bank's own client book record the provision (so it can
        // never hold a half-executed swap).
        RecordBankProvision(bankNodeId, gov.NonMinerNodeId, btcToSell, scAmount, price, block);
        AppendBankCreditTrace(block, "provision", bankNodeId, gov.NonMinerNodeId, scAmount, btcToSell, price);
        return true;
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

                // P15.3a (D-15.4) — a bank's incoming COLLATERAL is not business inflow: it is the asset
                // leg of a loan it just took, quarantined from its own reserves. Counting it here would
                // fire spurious >30%-inflow special votes — and where the player holds NST in that bank,
                // every one of those PAUSES THE GAME (D-ND8.18). The send is tagged at broadcast
                // (BankCollateralMemo), the same display-memo channel the swap desk already uses.
                if (tx.InputDataText == BankCollateralMemo && IsBankCompany(gov.NonMinerNodeId))
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
            ClosesAtMs = block.Timestamp + VoteDurationMs,
            // P15.4e — the gap the shortfall ballot is deciding how to split (0 for every other kind).
            ShortfallScTarget = kind == CompanyVoteKindShortfall ? gov.PendingShortfallSc : 0m
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
    // Currency — its band stance PROJECTED into this company's band (P15.9a / D-15.24: the stance is a
    // position on a global SC-ness axis, and a ballot outside the company's charter is not a legal vote);
    // Market — one step toward its preferred category;
    // Payout — the category's default rate scaled by its GREED (P15.4c / D-15.13; the clamp is
    // [0, 2× default] and the ladder spans exactly that); Shortfall — §3.3's greed table (P15.4e).
    private static CompanyBallot BuildBotBallot(string botNodeId, CompanyGovernanceState gov)
    {
        _botGovernancePreferences.TryGetValue(botNodeId, out BotGovernancePreference? pref);
        // Empty = drawn before greed existed and not yet backfilled; the pre-greed behaviour is the
        // neutral stance, so normalizing to it is a no-op rather than a silent bias.
        string greed = string.IsNullOrEmpty(pref?.GreedPreference) ? GreedAlmostGreedy : pref.GreedPreference;
        return new CompanyBallot
        {
            ReserveScPercentTarget = ProjectStanceIntoBand(
                BandDefaultScPercent(pref?.CurrencyBandPreference ?? gov.CurrencyBand), gov.CurrencyBand),
            MarketShift = pref == null
                ? 0
                : Math.Sign(MarketCategoryIndex(pref.MarketCategoryPreference) - MarketCategoryIndex(gov.MarketCategory)),
            PayoutRatePercent = Scripts.Finance.Money.Normalize(
                DefaultQuarterlyPayoutRatePercent(gov.MarketCategory) * GreedPayoutMultiplier(greed)),
            DividendsCutPercent = GreedDividendsCutPercent(greed)
        };
    }

    // P15.9f (2026-07-27) — the reserve outcome of a set of ballots, extracted so that CompanyDetails'
    // "if the vote closed now" preview and CloseCompanyVote's REAL resolution cannot diverge (§39.16
    // rule 6). A preview is a promise about what the resolver will do, which is the sharpest form of the
    // case that rule exists for: two implementations of the same weighted average would drift the first
    // time either side changed, and the player would be making a decision on the stale one.
    //
    // The math is D-ND8.19b's: weight = holder's NST ÷ total NST (holders with no NST are ignored, and a
    // ballot from one carries no weight), averaged over the ballots ACTUALLY CAST — so a half-voted ballot
    // set previews the outcome among those who have voted so far — then clamped to the band.
    public sealed class ReserveVoteOutcome
    {
        public decimal TotalNst { get; init; }
        public decimal VotedWeight { get; init; }  // 0..1 — the share of all NST that has cast a ballot
        public decimal RawAverage { get; init; }   // the weighted average before the band clamp
        public decimal Outcome { get; init; }      // RawAverage clamped into the band (or the fallback)
        public decimal BandMin { get; init; }
        public decimal BandMax { get; init; }
        public bool WasClamped { get; init; }
        public bool HasVotes => VotedWeight > 0m;
    }

    public static ReserveVoteOutcome ComputeReserveVoteOutcome(CompanyFounding founding, string band,
        IReadOnlyDictionary<string, CompanyBallot> ballots, decimal fallbackOutcome)
    {
        (decimal min, decimal max) = BandScPercentBounds(band);
        decimal totalNst = founding.Holdings.Where(h => h.Nst > 0m).Sum(h => h.Nst);
        if (totalNst <= 0m || ballots.Count == 0)
        {
            return new ReserveVoteOutcome { BandMin = min, BandMax = max, Outcome = fallbackOutcome };
        }

        Dictionary<string, decimal> nstByHolder = founding.Holdings
            .Where(h => h.Nst > 0m)
            .ToDictionary(h => h.HolderId, h => h.Nst);

        decimal votedWeight = 0m, weightedReserve = 0m;
        foreach ((string holderId, CompanyBallot ballot) in ballots)
        {
            if (!nstByHolder.TryGetValue(holderId, out decimal nst) || nst <= 0m)
            {
                continue;
            }

            decimal weight = nst / totalNst;
            votedWeight += weight;
            weightedReserve += weight * ballot.ReserveScPercentTarget;
        }

        if (votedWeight <= 0m)
        {
            return new ReserveVoteOutcome { TotalNst = totalNst, BandMin = min, BandMax = max, Outcome = fallbackOutcome };
        }

        decimal raw = Scripts.Finance.Money.Normalize(weightedReserve / votedWeight);
        decimal clamped = Math.Clamp(raw, min, max);
        return new ReserveVoteOutcome
        {
            TotalNst = totalNst,
            VotedWeight = votedWeight,
            RawAverage = raw,
            Outcome = clamped,
            BandMin = min,
            BandMax = max,
            WasClamped = raw != clamped
        };
    }

    private static void CloseCompanyVote(CompanyGovernanceState gov, CompanyFounding founding, Block block)
    {
        CompanyVote vote = gov.OpenVote!;
        gov.OpenVote = null;

        // ND.9g — snapshot the three policy dials BEFORE this vote's result is applied (before→after).
        decimal beforeReserve = gov.ReserveScPercent;
        string beforeMarket = gov.MarketCategory;
        decimal beforePayout = gov.QuarterPayoutRatePercent;
        var ballotRecords = new List<VoteBallotRecord>(); // ND.9g — every cast ballot, with its weight

        decimal totalNst = founding.Holdings.Where(h => h.Nst > 0m).Sum(h => h.Nst);
        decimal reserveResult = gov.ReserveScPercent;
        decimal payoutResult = 0m;
        int shiftResult = 0;          // what the NST holders voted (traced even when it can't be applied)
        bool categoryLocked = false;  // P15.2b / D-15.12 — true for a bank: the shift is voted but refused
        // P15.4e — a shortfall vote's single dial. Stays at the 50/50 default when nobody voted (D-15.7).
        decimal dividendsCutResult = DefaultShortfallDividendsCutPercent;

        if (totalNst > 0m && vote.Ballots.Count > 0)
        {
            Dictionary<string, decimal> nstByHolder = founding.Holdings
                .Where(h => h.Nst > 0m)
                .ToDictionary(h => h.HolderId, h => h.Nst);

            decimal votedWeight = 0m, weightedReserve = 0m, weightedPayout = 0m, lighterWeight = 0m, darkerWeight = 0m;
            decimal weightedDividendsCut = 0m; // P15.4e
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
                weightedDividendsCut += weight * ballot.DividendsCutPercent;
                if (ballot.MarketShift > 0) darkerWeight += weight;
                else if (ballot.MarketShift < 0) lighterWeight += weight;

                ballotRecords.Add(new VoteBallotRecord
                {
                    HolderId = holderId,
                    Nst = nst,
                    Weight = weight,
                    ReserveScPercentTarget = ballot.ReserveScPercentTarget,
                    MarketShift = ballot.MarketShift,
                    PayoutRatePercent = ballot.PayoutRatePercent
                });
            }

            if (votedWeight > 0m && vote.Kind == CompanyVoteKindShortfall)
            {
                // P15.4e — a shortfall ballot decides ONE thing. It must not move the reserve mix, the
                // market category or the payout rate as a side effect, so it takes its own exit here.
                dividendsCutResult = Math.Clamp(
                    Scripts.Finance.Money.Normalize(weightedDividendsCut / votedWeight), 0m, 100m);
            }
            else if (votedWeight > 0m)
            {
                // D-ND8.19b — reserve %: simple weighted average of the cast targets, clamped to the
                // band's ±25% range. P15.9f — computed by the SAME helper the CompanyDetails preview calls,
                // so "if the vote closed now" and what actually happens here can never be two different
                // numbers (§39.16 rule 6). The local weightedReserve/votedWeight accumulated above still
                // serve the payout / market / shortfall dials.
                ReserveVoteOutcome outcome = ComputeReserveVoteOutcome(
                    founding, gov.CurrencyBand, vote.Ballots, gov.ReserveScPercent);
                decimal min = outcome.BandMin, max = outcome.BandMax, rawReserve = outcome.RawAverage;
                reserveResult = outcome.Outcome;

                // P15.9.5 (2026-07-27) — the tripwire. Since P15.9a every ballot is band-legal at the point
                // it is CAST (bots projected here, the player clamped in TryRegisterPlayerVote), so the
                // average is in-band by construction and this clamp is a no-op. It stays as the guarantee,
                // not as a redundancy — and if it ever bites again, some new ballot source is bypassing the
                // projection and silently pinning the result to a bound, exactly the bug P15.9 fixed. That
                // failure hid for a whole plan because nothing announced it.
                if (rawReserve != reserveResult)
                {
                    GD.PrintErr(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "[Governance] P15.9 tripwire — {0} ({1}) cast an OUT-OF-BAND reserve average {2:F2}%, clamped to {3:F0}% (band {4}: {5:F0}–{6:F0}). A ballot source is bypassing ProjectStanceIntoBand.",
                        DescribeNodeForDev(gov.NonMinerNodeId), vote.Kind, rawReserve, reserveResult,
                        gov.CurrencyBand, min, max));
                }

                if (vote.Kind == CompanyVoteKindQuarterly)
                {
                    // D-ND8.19b — a market shift is discrete and riskier: it needs ≥60% of TOTAL voting
                    // weight in one direction, and lands clamped within ±1 of the roster default.
                    if (darkerWeight >= MarketShiftSupermajorityFraction) shiftResult = 1;
                    else if (lighterWeight >= MarketShiftSupermajorityFraction) shiftResult = -1;
                    // Step 15 P15.2b (D-15.12) — BANKS ARE EXEMPT from the ±1 shift. Their four categories
                    // span the Official→Black gradient the §5.1 selection distance is measured on, so a
                    // drifting bank would silently re-shape which companies bank where. In exchange banks
                    // gain the seized-wallet holding feature (P15.5c). Only the APPLICATION is blocked —
                    // shiftResult keeps what the holders actually voted, so the governance trace still shows
                    // a rejected attempt rather than pretending nobody asked.
                    categoryLocked = IsBankCompany(gov.NonMinerNodeId);
                    if (shiftResult != 0 && !categoryLocked)
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

        // P15.4e — a shortfall vote changes nothing but the shortfall: the reserve mix is left exactly
        // where it was, and the split is applied (which is what actually spends the ScReserve below).
        if (vote.Kind != CompanyVoteKindShortfall)
        {
            gov.ReserveScPercent = reserveResult;
        }

        int quarterDays = 0; // ND.9h — quarter length (in-game days), 0 for non-quarterly votes
        if (vote.Kind == CompanyVoteKindQuarterly)
        {
            // D-ND8.17 — FINALIZE the quarter's dividend as two separately-tracked amounts (never live
            // accrual): each currency side is payoutRate% of the corresponding reserve at finalize time.
            // The SC side is structurally 0 until ND.8b.6 lands the BTC→SC conversions.
            // P15.3a — the company's OWN BTC: a bank's quarantined collateral is excluded, so a quarterly
            // dividend can never pay away the asset backing its FED debt (D-15.4).
            decimal treasuryBtc = CompanyOwnBtc(gov.NonMinerNodeId);
            gov.QuarterPayoutRatePercent = payoutResult;
            gov.QuarterDividendBtc = Scripts.Finance.Money.Normalize(treasuryBtc * payoutResult / 100m);
            gov.QuarterDividendSc = Scripts.Finance.Money.Normalize(gov.ScReserve * payoutResult / 100m);
            gov.QuarterCycleStartMs = block.Timestamp;
            gov.QuarterCycleEndMs = gov.NextQuarterlyDueMs;
            gov.QuarterDrippedDays = 0;
            gov.QuarterLumpCredited = false;
            quarterDays = Math.Max(1, (int)((gov.QuarterCycleEndMs - gov.QuarterCycleStartMs) / GameDayMs));
        }

        // P15.4e — apply the split and repay what it raised. Runs before the vote record + trace below, so
        // both observe the post-cut ScReserve and the possibly-reduced quarter dividend.
        if (vote.Kind == CompanyVoteKindShortfall)
        {
            ApplyShortfallVote(gov, dividendsCutResult, block);
        }

        // Reset the >30% special-vote baseline at EVERY vote close — "new inflow" is measured from the
        // last governance event (D-ND8.18).
        gov.BaselineReserveBtc = CompanyOwnBtc(gov.NonMinerNodeId);
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
            FinalizedDividendSc = vote.Kind == CompanyVoteKindQuarterly ? gov.QuarterDividendSc : 0m,
            // ND.9g / ND.9h
            BeforeReserveScPercent = beforeReserve,
            BeforeMarketCategory = beforeMarket,
            BeforePayoutRatePercent = beforePayout,
            QuarterDaysInCycle = quarterDays,
            Ballots = ballotRecords
        });
        if (gov.VoteHistory.Count > MaxVoteHistoryPerCompany)
        {
            gov.VoteHistory.RemoveAt(0);
        }

        AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "vote_close", vote.Kind,
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "shift={0}{1}",
                shiftResult, categoryLocked && shiftResult != 0 ? ";shift_refused=bank_locked" : string.Empty));
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
            decimal paidSc = 0m;
            if (claim.Sc > 0m && gov.ScReserve > 0m && holder.FinancialState is NodeFinancialState fin)
            {
                paidSc = Math.Min(claim.Sc, gov.ScReserve);
                fin.PrincipalBalance = Scripts.Finance.Money.Normalize(fin.PrincipalBalance + paidSc);
                gov.ScReserve = Scripts.Finance.Money.Normalize(gov.ScReserve - paidSc);
                claim.Sc = Scripts.Finance.Money.Normalize(claim.Sc - paidSc);
            }

            // ND.10e (D-ND10e.4, 2026-07-23) — BATCH the BTC leg instead of sweeping it the instant it
            // clears the fee. The old gate (`claim.Btc <= fee`) meant a PST daily drip was sent every
            // single block at whatever it had accrued: an audit of a live world found claims netting
            // 0.00039093 BTC against a 0.01 median fee — **96% of that dividend burned as fee** — with
            // 555 bot claims having paid ~5.55 BTC of fees in total. Waiting until the accrual is worth
            // `BotDividendClaimFeeMultiple ×` the fee caps the loss at ~1/N of each payment (10%) and,
            // since the fee comes out of the dividend itself, hands the difference straight back to the
            // bots' BTC income — one of the cheapest de-financing fixes available.
            if (claim.Btc < fee * BotDividendClaimFeeMultiple)
            {
                // Not yet worth a transaction — the BTC keeps accruing, nothing is lost. The SC leg is
                // instant and unaffected by fees, so log it on its own when it paid something (it was
                // previously invisible in telemetry, folded into a row that only reported `btc=`).
                if (paidSc > 0m)
                {
                    AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "bot_claim_sc", holderId,
                        string.Format(System.Globalization.CultureInfo.InvariantCulture, "sc={0:F8}", paidSc));
                }
                continue;
            }

            decimal sendAmount = Scripts.Finance.Money.Normalize(claim.Btc - fee);
            if (sendAmount <= 0m)
            {
                continue;
            }

            if (BuildAndBroadcastUtxoSpend(company, holder.WalletAddress, sendAmount, fee, null, "DIVIDEND") == null)
            {
                // ND.10e — a failed broadcast used to vanish silently (the company treasury being short of
                // spendable UTXOs looks identical to "no dividend was due"). Logged so the trace can prove
                // whether every accrued dividend actually reached its holder.
                AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "bot_claim_failed", holderId,
                    string.Format(System.Globalization.CultureInfo.InvariantCulture, "btc={0:F8} fee={1:F8}", sendAmount, fee));
                continue;
            }

            claim.Btc = 0m;
            AppendCompanyGovernanceTrace(block.Timestamp, block.Index, gov, "bot_claim", holderId,
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "btc={0:F8} sc={1:F8}", sendAmount, paidSc));
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
    // {-1,0,1}, payout ∈ [0, 2× default], dividends-cut ∈ [0,100]) rather than rejecting — the UI pre-fills
    // legal values anyway. Registering the ballot lifts the pause; the vote still closes on its own
    // one-day schedule.
    //
    // P15.4e: `dividendsCutPercent` is read ONLY by a shortfall vote and is optional so the existing panel
    // keeps compiling and can never deadlock the pause — an un-wired caller simply votes the 50/50 default
    // (D-15.7). Wiring the real control is P15.7c; use GetOpenVoteKind/GetOpenVoteShortfallTarget to know
    // when to show it.
    public static bool TryRegisterPlayerVote(string nonMinerNodeId, decimal reserveScPercentTarget, int marketShift,
        decimal payoutRatePercent, decimal dividendsCutPercent = DefaultShortfallDividendsCutPercent)
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
                DefaultQuarterlyPayoutRatePercent(gov.MarketCategory) * 2m),
            DividendsCutPercent = Math.Clamp(Scripts.Finance.Money.Normalize(dividendsCutPercent), 0m, 100m)
        };
        vote.AwaitingPlayerVote = false;
        return true;
    }

    // P15.4e — what kind of vote is open at this company ("" if none), and the SC gap a shortfall ballot
    // is deciding how to split. The Board Vote panel (P15.7c) uses these to swap in the shortfall control.
    public static string GetOpenVoteKind(string nonMinerNodeId) =>
        _companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov) && gov.OpenVote is { } v
            ? v.Kind
            : string.Empty;

    public static decimal GetOpenVoteShortfallTarget(string nonMinerNodeId) =>
        _companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov) && gov.OpenVote is { } v
            ? v.ShortfallScTarget
            : 0m;

    public CompanyGovernanceState? GetCompanyGovernanceByNodeId(string nonMinerNodeId)
    {
        EnsureInitialized();
        return _companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov) ? gov : null;
    }

    // ND.10h (D-ND10h.3, 2026-07-23) — does the player have a dividend here that a claim would ACTUALLY
    // PAY? The obvious test (`Btc > 0 || Sc > 0`) is wrong and produces a permanently-lit signal that pays
    // nothing when pressed: TryClaimPlayerCompanyDividends below only sends the BTC leg when
    // `claim.Btc > fee` (the fee comes out of the claim itself), so a sub-fee dust accrual looks claimable
    // and is not. Same "is this payment worth its fee" question ND.10e answered for the bots' auto-claims.
    // The SINGLE source for every surface that advertises a claim — the BlockExplorer row button and
    // CompanyDetails' Claim panel both read this, so a displayed signal cannot drift from the action.
    public bool HasPlayerClaimableDividends(string nonMinerNodeId)
    {
        EnsureInitialized();
        if (!_companyGovernance.TryGetValue(nonMinerNodeId, out CompanyGovernanceState? gov)
            || !gov.ClaimableByHolder.TryGetValue(PlayerNodeId, out CompanyClaimable? claim))
        {
            return false;
        }

        if (claim.Sc > 0m && gov.ScReserve > 0m) return true;
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)) return false;
        return claim.Btc > NetworkFeePolicy.MedianFeeAt(player.Blockchain.GetLastBlock().Timestamp);
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

        // ND.8g (§12.5.6) — log this successful claim to the company's PLAYER-ONLY history (bot auto-claims,
        // via TryAutoClaimBotDividends, never write here). tipMs is already game time (a block's own
        // Timestamp), never wall-clock, per the project's canonical rule.
        gov.PlayerClaimHistory.Add(new CompanyDividendClaimRecord
        {
            ClaimedAtUnixMs = tipMs,
            BtcAmount = paidBtc,
            ScAmount = paidSc,
            BtcPriceUsdAtClaim = _marketData?.GetEffectivePriceUsd(DateTimeOffset.FromUnixTimeMilliseconds(tipMs).LocalDateTime)
        });
        if (gov.PlayerClaimHistory.Count > MaxPlayerClaimHistoryPerCompany)
        {
            gov.PlayerClaimHistory.RemoveAt(0);
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
    private const string BankCreditTracePath = "user://logs/bank_credit_trace.csv"; // Step 15

    // ND.8b.3 telemetry — one row per governance event (vote_open / vote_close / quarter_settled /
    // bot_claim). Daily drip accruals are deliberately NOT logged (row volume); the quarter_settled and
    // claim rows bracket them for playtest verification.
    // Step 15 (P15.7d, pulled forward to P15.3a): one row per banking-layer credit event — provisions now,
    // repayments / shortfalls / dissolutions / seizures as those phases land. This is the ONLY observability
    // the bank credit loop has until the P15.7 readouts, and the P15.8 calibration run reads it, so it
    // ships with the mechanism rather than after it. Delete-listed in ResetWorldIfIncompatible (the TL.3
    // maintenance rule). Join key is the raw nodeId, like every other trace (ND.10g).
    private static void AppendBankCreditTrace(Block block, string eventType, string bankNodeId, string companyNodeId,
        decimal sc, decimal btc, decimal priceUsd)
    {
        try
        {
            if (!DirAccess.DirExistsAbsolute("user://logs"))
            {
                DirAccess.MakeDirRecursiveAbsolute("user://logs");
            }

            bool exists = FileAccess.FileExists(BankCreditTracePath);
            using FileAccess file = exists
                ? FileAccess.Open(BankCreditTracePath, FileAccess.ModeFlags.ReadWrite)
                : FileAccess.Open(BankCreditTracePath, FileAccess.ModeFlags.Write);
            if (file == null)
            {
                return;
            }

            if (exists) file.SeekEnd();
            else file.StoreLine("blockTimestampMs,blockIndex,event,bankNodeId,companyNodeId,sc,btc,priceUsd,bankFedDebt,bankCollateralBtc");

            file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5:F8},{6:F8},{7:F2},{8:F8},{9:F8}",
                block.Timestamp, block.Index, eventType, bankNodeId, companyNodeId, sc, btc, priceUsd,
                _centralBank?.OutstandingDebt(CentralBankService.BankClientId(bankNodeId)) ?? 0m,
                BankCollateralBtc(bankNodeId)));
        }
        catch (Exception e)
        {
            GD.PushWarning($"[BankCreditTrace] failed: {e.Message}");
        }
    }

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
    private const string DifficultyTraceHeader =
        "utcMs,miner,index,configuredPower,realizedPower,difficulty,anchor,solveSec,solveRatio,simSecOffered,simSecConsumed";
    private static bool _difficultyTraceSchemaChecked;

    // R2-T (2026-07-27) — simulated seconds OFFERED to the bet engine vs. those it actually retained,
    // accumulated by SimulationService since the last block and drained by AppendDifficultyTrace. The
    // SetActiveMiningPower precedent: SimulationService pushes the fact, NetworkRoot only records it.
    // Saturation used to be inferable only by comparing configured to realized power AFTER the fact; this
    // measures it at the source, and is the input signal R2-B will consume.
    private static double _simSecondsOffered;
    private static double _simSecondsConsumed;

    public static void AccumulateSimSaturation(double offeredSeconds, double consumedSeconds)
    {
        if (offeredSeconds <= 0d) return;
        _simSecondsOffered += offeredSeconds;
        _simSecondsConsumed += Math.Clamp(consumedSeconds, 0d, offeredSeconds);
    }

    // R2-ASSERT (D-R2.4) — the executable-power alarm. The regulator has twice been audited and declared
    // correct while producing wrong block times, because the fault was upstream: it was handed a power
    // figure nothing could hash. Gated on THREE CONSECUTIVE blocks because single-block solvetimes are
    // ≈exponentially distributed — the plan's own protocol says judge by aggregates, so the alarm obeys it.
    private const double ExecutablePowerAlarmRatio = 2.0;
    private const int ExecutablePowerAlarmBlocks = 3;
    private static int _executablePowerBreachStreak;

    private static void CheckExecutablePowerAlarm(int blockIndex, double configuredPower, double realizedPower)
    {
        if (configuredPower <= 0d || realizedPower <= 0d
            || configuredPower <= ExecutablePowerAlarmRatio * realizedPower)
        {
            _executablePowerBreachStreak = 0;
            return;
        }

        if (++_executablePowerBreachStreak < ExecutablePowerAlarmBlocks) return;
        _executablePowerBreachStreak = 0; // re-arm, so a long saturation reports periodically, not per block

        GD.PrintErr(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[R2] Difficulty is pricing UN-EXECUTABLE power — block {0}: configured {1:F1} vs realized "
            + "{2:F1} ({3:F1}× ) for {4} consecutive blocks. Blocks will run ≈{3:F1}× slow until it clears. "
            + "Check for a founder/scheduled power spike (founders_trace.csv, network_population_trace.csv) "
            + "and the simSecOffered/simSecConsumed columns for engine saturation.",
            blockIndex, configuredPower, realizedPower, configuredPower / realizedPower,
            ExecutablePowerAlarmBlocks));
    }

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

            // ND.10j's rule, applied here too (R3, 2026-07-28): R2-T appended simSecOffered/simSecConsumed
            // without rotating, so every existing world's file carries the 9-column pre-R2 header above
            // 11-column rows — the saturation columns, the very signal R2-C1 exists to expose, could only be
            // read by counting commas. Rotate to `.old` on a header mismatch, checked once per process.
            bool exists = FileAccess.FileExists(DifficultyTracePath);
            if (exists && !_difficultyTraceSchemaChecked)
            {
                _difficultyTraceSchemaChecked = true;
                if (ReadFirstLine(DifficultyTracePath) != DifficultyTraceHeader)
                {
                    DirAccess.RemoveAbsolute(DifficultyTracePath + ".old");
                    if (DirAccess.RenameAbsolute(DifficultyTracePath, DifficultyTracePath + ".old") == Error.Ok)
                    {
                        exists = false; // rotated — fall through and write a fresh, correctly-headed file
                    }
                    else
                    {
                        GD.PushWarning("[DifficultyTrace] stale header and rotation failed — rows appended "
                            + "to this file will not match its header. Delete difficulty_trace.csv.");
                    }
                }
            }

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
                file.StoreLine(DifficultyTraceHeader);
            }

            // R2-T — the saturation figures for the interval that just closed, then reset for the next block.
            double simOffered = _simSecondsOffered, simConsumed = _simSecondsConsumed;
            _simSecondsOffered = 0d;
            _simSecondsConsumed = 0d;

            file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F1},{8:F4},{9:F2},{10:F2}",
                block.Timestamp, miner.NodeId, block.Index, configuredPower, realizedPower,
                block.Difficulty, anchor, solveSec, solveRatio, simOffered, simConsumed));

            CheckExecutablePowerAlarm(block.Index, configuredPower, realizedPower);
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
        TryCasinoBotDonation(block);

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
    private static void TryCasinoBotDonation(Block block)
    {
        // D-ND4b.4: order targets — active auctions soonest-to-expire first, then not-yet-competing ones
        // — computed ONCE against the chain as of the just-mined block, so every donation slot this
        // block is measured against the SAME starting leader (the D-ND4b.11 same-block tie-break relies
        // on this: two independent bids racing the same pre-block leader, not each other in sequence).
        // ND.6a: each bot then re-orders these per D-ND6.6 (own-slot count first) inside its own pipeline.
        List<NonMinerDonationSummary> ledger = ComputeAuctionLedger(block.Timestamp);
        List<NonMinerDonationSummary> recruitable = ledger.Where(s => s.Status == NonMinerAuctionStatus.InAuction).ToList();

        // Fix B (ND.10a) — refresh the stuck-bidder signal for every (recruitable pool × casino bot) this
        // block, BEFORE the count==0 early-return, so the escalation advances every block regardless of who
        // (if anyone) is drawn to bid.
        SweepStuckBidderSignatures(recruitable, block.Index);
        SweepBotReserveGuard(); // ND.10e (D-ND10e.3) — same per-block, single-writer discipline

        int count = DrawCasinoBotDonationCount();
        if (count == 0) return;

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
            // D-ND10c.1 (2026-07-23) — the draw is restricted to ELIGIBLE bots: those holding at least one
            // qualifying, AFFORDABLE pool with a nonzero roll probability. This reverses D-ND6.1 ("a bot
            // keeps its full selection probability even when its own rules will produce no donation") —
            // a slot is no longer burned on a bot that provably cannot act. Recomputed per hop, since a
            // bid placed in slot 0 moves the leader, the tiers and the required amounts slot 1 will see.
            List<BotWalletRecord> candidates = BotWalletRegistry.MinerBots
                .Where(b => !usedBotIds.Contains(b.NodeId) && !visitedThisSlot.Contains(b.NodeId))
                .Where(b => HasEligibleBidOpportunity(b.NodeId, priorityTargets, fee, block.Timestamp, block.Index))
                .ToList();
            if (candidates.Count == 0) return null; // no eligible bot left for this slot — it yields nothing

            BotWalletRecord record = candidates[Random.Shared.Next(candidates.Count)];
            visitedThisSlot.Add(record.NodeId);
            if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? sender)) continue; // registry hole — pass the slot on

            CasinoBotSlotOutcome outcome = TryBuildCasinoBotBid(priorityTargets, sender, fee, block.Timestamp, block.Index, out Transaction? tx, out CasinoBotBidTrace trace);
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

    // ND.10c (2026-07-23, D-ND10c.2/4) — one bot's full bidding pipeline for one donation slot:
    //   1. Qualifying + affordable pools, each with its own roll probability — `BuildBotPoolOpportunities`
    //      (the shared source of truth: same rules, same numbers the panel displays). Unchanged from ND.6:
    //      pools where the bot is satisfied (D-ND8d.1/3) or self-eviction-guarded (D-ND6.7b) are excluded,
    //      and the half-spendable cap (D-ND6.8) still bounds the ENTIRE outgoing amount.
    //   2. PARALLEL ladder rolls (D-ND10c.2): EVERY affordable pool rolls its own probability this slot;
    //      the pools that hit compete in a uniform tie-break, and the winner is bid. No hit ⇒ no donation
    //      this slot, never re-rolled.
    //
    // What ND.10c replaced: the D-ND6.6 spread-wide-first ordering + D-ND6.8's "first affordable IS the
    // target" walk + D-ND6.5's single ladder roll. That shape made a pool's carefully calibrated
    // probability STRUCTURALLY UNREACHABLE whenever the walk stopped earlier (a bot holding slots in a
    // pool always lost priority to any 0-slot pool), which is what the ND.10b panel exposed. It also
    // retires ND.10a's Fix A (escalation-boosted re-selection): with every pool rolling every slot,
    // reachability is structural and the queue-jump has nothing left to fix. The escalation survives as
    // the `max(mode, escalation)` FLOOR inside BuildBotPoolOpportunities, fed by the per-block Fix-B
    // sweep (`SweepStuckBidderSignatures`), which is now the single writer of `_stuckBidderSignatures`.
    // Unparticipated pools still bid deterministically (their probability is 100 by construction).
    private static CasinoBotSlotOutcome TryBuildCasinoBotBid(List<NonMinerDonationSummary> priorityTargets, NodeAgent sender, decimal fee, long nowMs, int currentBlockIndex, out Transaction? tx, out CasinoBotBidTrace trace)
    {
        tx = null;
        string botAddress = sender.WalletAddress;
        decimal spendable = sender.Blockchain.GetAddressSpendableBalance(botAddress);
        decimal bidBudgetCap = Math.Round(spendable * MaxBidBalanceFraction, 8);
        trace = new CasinoBotBidTrace { FeeBtc = fee, SpendableBtc = spendable, BidBudgetCapBtc = bidBudgetCap };

        List<BotPoolOpportunity> all =
            BuildBotPoolOpportunities(priorityTargets, botAddress, sender.NodeId, fee, bidBudgetCap, nowMs, currentBlockIndex);
        List<BotPoolOpportunity> biddable = all.Where(o => o.Exclusion == null).ToList();
        trace.QualifyingPools = biddable.Count;
        if (biddable.Count == 0)
        {
            // Both defensive since D-ND10c.1 pre-filters ineligible bots out of the draw; the distinction is
            // preserved so the trace still says WHICH kind of nothing it was if one ever slips through.
            return all.Any(o => o.Exclusion == ExclusionPricedOut)
                ? CasinoBotSlotOutcome.NothingAffordable
                : CasinoBotSlotOutcome.NoQualifyingTarget;
        }

        // Step 2 — the parallel rolls. Each pool's probability is mode-aware (early-rush / urgency /
        // normal), summed over the bot's two lowest slots (ND.8d round 2), floored by the stuck escalation
        // for a lone slot (now including tiers 2-3, D-ND10c.3), or a flat 100 when unparticipated.
        var hits = new List<BotPoolOpportunity>();
        var rollLog = new List<string>(biddable.Count);
        foreach (BotPoolOpportunity opportunity in biddable)
        {
            bool hit = Random.Shared.Next(100) < opportunity.ProbabilityPercent;
            if (hit) hits.Add(opportunity);
            // ND.10j — log what each pool rolled BEFORE the tie-break discards all but one (see PoolRolls).
            // ND.10l — and its tie-break WEIGHT where that differs from the rolled probability (i.e. a
            // fresh pool's sentinel 100 vs its FreshPoolSeedingWeight), so a row shows not just which
            // pools hit but why the draw between them resolved as it did.
            int weight = TieBreakWeight(opportunity);
            string weightSuffix = weight == opportunity.ProbabilityPercent ? string.Empty : $"w{weight}";
            rollLog.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{opportunity.Target.NonMinerNodeId}:{opportunity.ProbabilityPercent}{(hit ? "*" : string.Empty)}{weightSuffix}"));
        }
        trace.PoolRolls = string.Join("|", rollLog);
        trace.HitPools = hits.Count;
        if (hits.Count == 0) return CasinoBotSlotOutcome.RollDeclined;

        // ND.10l (2026-07-28, §14.13, D-ND10l.1) — PROBABILITY-WEIGHTED tie-break, replacing D-ND10c.2's
        // uniform draw. The uniform draw fixed an ordering problem and quietly created its mirror image:
        // an unparticipated pool ALWAYS hits, so every fresh company introduction halved an escalated
        // re-bid's real chance that block — diluting exactly the re-bids ND.10c set out to make
        // reachable. `poolRolls` (ND.10j) is what made it visible: at block 964 bot_2's BitInstant roll
        // HIT and lost the coin-flip to a 0.03 BTC seed bid on a brand-new pool.
        //
        // Weighted by how much each pool actually wants the slot, so a 64% escalated re-bid is no longer
        // a coin flip against a fresh pool — but still a DRAW, never a priority: an absolute rule
        // ("expiring auctions first") would starve fresh-pool seeding entirely, and with 40 companies
        // arriving along the address curve their first bids are how auctions start at all.
        BotPoolOpportunity pick = WeightedPickAmongHits(hits);
        trace.ChosenAmongHits = hits.Count;

        NonMinerDonationSummary target = pick.Target;
        decimal leadingAmount = pick.LeadingAmount;
        trace.TargetNodeId = target.NonMinerNodeId;
        trace.OwnTiersInTarget = string.Join("|", pick.OwnTiers);
        trace.RequiredBtc = pick.RequiredAmount;
        trace.RolledTier = pick.BestTier;
        trace.RolledProbabilityPercent = pick.ProbabilityPercent;

        // D-ND4b.6: a raise coin-flips between the two ends of the raise band; a first donation is
        // pinned at the fixed floor. ND.6a: the principal is clamped under the half-spendable cap —
        // the RaiseMax end can exceed it even when the required RaiseMin end fits (the clamp can
        // never drop below requiredAmount, which the affordability filter already fitted).
        decimal targetPrincipal = target.LeadingBidUnixMs == 0
            ? OpeningBidFloorBtcAt(nowMs)
            : (Random.Shared.NextDouble() < 0.5
                ? leadingAmount + RaiseMin(leadingAmount, nowMs)
                : leadingAmount + RaiseMax(leadingAmount, nowMs));
        targetPrincipal = Math.Min(targetPrincipal, Math.Round(bidBudgetCap - fee, 8));

        // D-ND4b.11: additive random tail — headroom measured against the D-ND6.8 cap, not the full
        // spendable balance, so `required + tail + fee ≤ spendable × MaxBidBalanceFraction` holds for
        // the ENTIRE outgoing amount (OQ-ND6.6's resolution).
        decimal headroom = Math.Max(0m, bidBudgetCap - fee - targetPrincipal);
        decimal tail = Math.Round((decimal)Random.Shared.NextDouble() * Math.Min(targetPrincipal, headroom), 8);
        decimal amount = Math.Round(targetPrincipal + tail, 8);
        trace.AmountBtc = amount;

        tx = BuildAndBroadcastUtxoSpend(sender, target.NonMinerAddress, amount, fee, null);
        return tx != null ? CasinoBotSlotOutcome.Donated : CasinoBotSlotOutcome.BroadcastFailed;
    }

    // ND.10l (D-ND10l.2) — an unparticipated pool's `ProbabilityPercent` is 100, but that 100 is a
    // SENTINEL ("a first bid is deterministic, it never rolls" — D-ND6.5), not a statement of how much
    // the bot wants that slot. Using it as a tie-break weight is a category error, and an expensive one:
    // it is the largest number in the system, so weighting by the raw probability would hand fresh pools
    // MORE of the slot than the uniform draw did (a 64% escalated re-bid would fall from ½ to 64/164),
    // making the ND.10j dilution worse rather than better. A fresh pool therefore carries this explicit
    // seeding weight instead.
    //
    // 34 (Fibonacci per D-ND6.4) places a first bid on a par with a fairly pressed tier-8 NORMAL slot:
    // clearly beaten by a genuinely stuck escalation, clearly ahead of a calm low-tier re-bid, and still
    // winning outright on every slot where nothing else hits — which is most of them early on, when the
    // pools are few. A CALIBRATION PLACEHOLDER like the ND.10e treasury thresholds: it should be re-read
    // once the R2 block pace is verified, since block frequency changes how often pools contest at all.
    private const int FreshPoolSeedingWeight = 34;

    private static int TieBreakWeight(BotPoolOpportunity opportunity)
        => opportunity.OwnSlotCount == 0 ? FreshPoolSeedingWeight : opportunity.ProbabilityPercent;

    // ND.10l — the weighted draw itself. Falls back to a uniform pick if every weight is somehow 0 (not
    // reachable: an excluded pool never enters `hits`, and a biddable one always carries a positive
    // probability — D-ND10c's `probabilityPercent <= 0` guard turns any all-zero case into an exclusion).
    private static BotPoolOpportunity WeightedPickAmongHits(List<BotPoolOpportunity> hits)
    {
        int total = 0;
        foreach (BotPoolOpportunity h in hits) total += TieBreakWeight(h);
        if (total <= 0) return hits[Random.Shared.Next(hits.Count)];

        int roll = Random.Shared.Next(total);
        foreach (BotPoolOpportunity h in hits)
        {
            roll -= TieBreakWeight(h);
            if (roll < 0) return h;
        }
        return hits[^1]; // unreachable — the walk always consumes the roll
    }

    // D-ND10c.1 — the eligibility test behind the restricted bot draw: does this bot hold ANY qualifying,
    // affordable pool with a nonzero roll probability? Builds the same opportunity list the pipeline and
    // the panel use, so all three agree by construction. Cheap enough to run per candidate per slot
    // (≤ 4 bots × ≤ 2 slots per block, one spendable-balance read each — the UTXO set is chain-cached).
    private static bool HasEligibleBidOpportunity(string botNodeId, List<NonMinerDonationSummary> pools, decimal fee, long nowMs, int currentBlockIndex)
    {
        if (!SharedNodesById.TryGetValue(botNodeId, out NodeAgent? bot)) return false;
        decimal cap = Math.Round(bot.Blockchain.GetAddressSpendableBalance(bot.WalletAddress) * MaxBidBalanceFraction, 8);
        return BuildBotPoolOpportunities(pools, bot.WalletAddress, botNodeId, fee, cap, nowMs, currentBlockIndex)
            .Any(o => o.Exclusion == null);
    }

    // ND.10d (2026-07-23) — WHY a bot's chance in this pool is zero, for the AuctioningCompanyDetails panel:
    // "satisfied" (top-tier / leader), "guard" (self-eviction, D-ND6.7b), "priced out" (the raise no longer
    // fits the half-spendable cap — the DESIGNED economic terminator of a mature auction, §22.10), or "" when
    // the bot genuinely can bid (a zero on screen is then just rounding, and the panel prints "<0.01%").
    // Single pool × single bot, so it costs one spendable-balance read; called only on a 0.00% row.
    public string BotPoolExclusionNote(NonMinerDonationSummary pool, string botNodeId)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(botNodeId, out NodeAgent? bot)) return string.Empty;
        Block tip = GetPlayerLatestBlock();
        decimal fee = NetworkFeePolicy.MedianFeeAt(tip.Timestamp);
        decimal cap = Math.Round(bot.Blockchain.GetAddressSpendableBalance(bot.WalletAddress) * MaxBidBalanceFraction, 8);
        BotPoolOpportunity? opportunity =
            BuildBotPoolOpportunities([pool], bot.WalletAddress, botNodeId, fee, cap, tip.Timestamp, tip.Index).FirstOrDefault();
        return opportunity?.Exclusion ?? string.Empty;
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
        // D-ND10c.6 — the parallel-roll shape: how many pools rolled, how many hit, and how many the
        // uniform tie-break drew from (= HitPools when a pick happened, 0 when nothing hit).
        public int QualifyingPools;
        public int HitPools;
        public int ChosenAmongHits;
        // ND.10j (2026-07-28, §14.11) — EVERY biddable pool's composed probability this slot, and which
        // of them hit: "non_miner_8:40*|non_miner_10:100*" (`*` = hit; the chosen one is targetNodeId).
        // Until now `rolledProbabilityPercent` was written only after a pick, so all 52 roll-declined rows
        // in the BitInstant audit logged a bare 0 — ND.6b's whole premise is that "the declines ARE the
        // calibration signal", and they carried none. This is also the only place the uniform tie-break
        // is observable: a pool can hit and still lose the draw (blk 964: bot_4's BitInstant roll hit and
        // lost the coin-flip to a fresh 0.03 BTC seed pool), which no other column records.
        public string PoolRolls = string.Empty;
    }

    private const string CasinoBotBidTracePath = "user://logs/casino_bot_bid_trace.csv";
    // ND.10j — hoisted to a const so the writer can compare it against an existing file's first line and
    // rotate a stale-schema trace instead of appending misaligned rows to it.
    private const string CasinoBotBidTraceHeader =
        "blockTimestampMs,blockIndex,slot,hop,botNodeId,outcome,targetNodeId,ownTiersInTarget,rolledTier,"
        + "rolledProbabilityPercent,requiredBtc,amountBtc,feeBtc,spendableBtc,bidBudgetCapBtc,"
        + "qualifyingPools,hitPools,chosenAmongHits,poolRolls";

    // ND.10j — one-shot: the schema/rotation check runs on the first append of the process, not per row.
    private static bool _bidTraceSchemaChecked;

    // ND.10j — the header-schema check for the trace rotation above. Opens read-only and reads one line.
    private static string ReadFirstLine(string path)
    {
        try
        {
            using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            return file == null ? string.Empty : file.GetLine();
        }
        catch
        {
            return string.Empty;
        }
    }

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

            // ND.10j — a trace whose columns no longer match its header is worse than no trace (§39.16
            // rule 1: a lying number is invisible). When the schema changes, the old file is rotated to
            // `.old` rather than appended to or destroyed — the developer keeps the previous session's
            // rows AND gets a correctly-headed file, with no manual delete step. Checked ONCE per process
            // (this runs several times per block), since nothing else writes the file while we hold it.
            bool exists = FileAccess.FileExists(CasinoBotBidTracePath);
            if (exists && !_bidTraceSchemaChecked)
            {
                _bidTraceSchemaChecked = true;
                if (ReadFirstLine(CasinoBotBidTracePath) != CasinoBotBidTraceHeader)
                {
                    DirAccess.RemoveAbsolute(CasinoBotBidTracePath + ".old");
                    if (DirAccess.RenameAbsolute(CasinoBotBidTracePath, CasinoBotBidTracePath + ".old") == Error.Ok)
                    {
                        exists = false; // rotated — fall through and write a fresh, correctly-headed file
                    }
                    else
                    {
                        // Practically unreachable (same directory, `.old` just cleared). Say so rather than
                        // append rows that silently disagree with the header above them.
                        GD.PushWarning("[CasinoBotBidTrace] stale header and rotation failed — rows appended "
                            + "to this file will not match its header. Delete casino_bot_bid_trace.csv.");
                    }
                }
            }

            using FileAccess file = exists
                ? FileAccess.Open(CasinoBotBidTracePath, FileAccess.ModeFlags.ReadWrite)
                : FileAccess.Open(CasinoBotBidTracePath, FileAccess.ModeFlags.Write);
            if (file == null) return;

            if (exists) file.SeekEnd();
            else file.StoreLine(CasinoBotBidTraceHeader);

            file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10:F8},{11:F8},{12:F8},{13:F8},{14:F8},{15},{16},{17},{18}",
                block.Timestamp, block.Index, slot, hop, botNodeId, CasinoBotSlotOutcomeLabel(outcome),
                trace.TargetNodeId, trace.OwnTiersInTarget, trace.RolledTier, trace.RolledProbabilityPercent,
                trace.RequiredBtc, trace.AmountBtc, trace.FeeBtc, trace.SpendableBtc, trace.BidBudgetCapBtc,
                trace.QualifyingPools, trace.HitPools, trace.ChosenAmongHits, trace.PoolRolls));
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

    // ND.10e (D-ND10e.1) — the live, price-anchored opening floor for a pool that has NO leading bid yet:
    // always worth $0.10, capped at 1 BTC (which binds while BTC trades under $0.10), never below one
    // satoshi. Takes the timestamp of the bid being judged — the chain-replayed auction ledger evaluates
    // each historical first bid against the floor that applied on ITS day, so a replay is deterministic.
    // No market data (pre-Market-Birth, or the service absent) falls back to the 1 BTC cap.
    private static decimal OpeningBidFloorBtcAt(long unixMs)
    {
        decimal? priceUsd = _marketData?.GetEffectivePriceUsd(DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime);
        if (priceUsd is not { } usd || usd <= 0m) return MaxOpeningBidBtc;
        decimal floorBtc = Scripts.Finance.Money.Normalize(OpeningBidUsdValue / usd);
        return Math.Clamp(floorBtc, OneSatoshi, MaxOpeningBidBtc);
    }

    // D-ND4b.6 formula (worked examples: step14 plan §3.4 ND.4b), band cut to 5-10% at ND.10e (D-ND10e.2).
    // The absolute floor term is the live opening floor, so an early tiny leader still demands a
    // meaningful jump instead of a satoshi-sized one.
    private static decimal RaiseMin(decimal leadingBid, long nowMs) => Math.Max(OpeningBidFloorBtcAt(nowMs), RaiseMinFraction * leadingBid);
    private static decimal RaiseMax(decimal leadingBid, long nowMs) => Math.Max(2m * OpeningBidFloorBtcAt(nowMs), RaiseMaxFraction * leadingBid);

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
        // panel): OneSatoshi over the leader, not the casino-bots' RaiseMin/RaiseMax formula. ND.10e — an
        // unbid pool quotes the live price-anchored opening floor (worth $0.10, capped 1 BTC), so the
        // wallet's "minimum to compete" moves with the market exactly as the ledger's own gate does.
        return entry.LeadingBidUnixMs == 0
            ? OpeningBidFloorBtcAt(GetPlayerLatestBlockTimestampMsStatic())
            : entry.LeadingDonorTotal + OneSatoshi;
    }

    // ND.8d.6 (D-ND8d.6, 2026-07-20) — the wallet's closing-soon threshold: a bid inside the target
    // auction's final AuctionClosingSoonWarningDays in-game days risks not being mined before close.
    public const double AuctionClosingSoonWarningDays = 2d;

    // ND.8d.6 — does the recipient address belong to a company whose auction the PLAYER currently leads?
    // The BTC wallet shows a NON-blocking warning (the send still proceeds): a further raise onto one's own
    // leading bid just raises the player's own price and resets the 20-day window, delaying their own win.
    public bool IsPlayerLeadingCompanyBid(string address)
    {
        EnsureInitialized();
        NonMinerDonationSummary? entry = ComputeAuctionLedger(GetPlayerLatestBlockTimestampMsStatic())
            .FirstOrDefault(s => s.NonMinerAddress == address);
        if (entry is null || entry.Status != NonMinerAuctionStatus.InAuction || entry.LeadingBidUnixMs == 0) return false;
        return IsPlayerBidderAddress(entry.LeadingDonorAddress);
    }

    // ND.10k (D-ND10k.3, 2026-07-28) — how much the player has ALREADY sent to this company that is still
    // sitting UNCONFIRMED in the mempool (null when nothing is pending). This is the warning the BitInstant
    // incident needed: nothing between blocks is persisted or visible in the ledger (Pattern 2), so a bid
    // that has not been mined yet is invisible everywhere — the player sent a second one believing the
    // first had failed, and block 965 confirmed BOTH into the same bid group. Under D-ND10k.1 only the
    // HIGHEST of a donor's same-block bids participates, so the other becomes a plain non-participating
    // send with no refund. Warned, never blocked (the D-ND8d.6 convention: the send always proceeds).
    //
    // Reads `PendingTransactions` directly rather than the ledger, because the ledger is a pure CHAIN
    // replay and by construction cannot see the mempool — which is exactly why this was invisible.
    public decimal? GetPendingAuctionBidBtc(string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)) return null;

        NonMinerDonationSummary? entry = ComputeAuctionLedger(GetPlayerLatestBlockTimestampMsStatic())
            .FirstOrDefault(s => s.NonMinerAddress == address);
        if (entry is null || entry.Status != NonMinerAuctionStatus.InAuction) return null;

        decimal pending = 0m;
        foreach (Transaction tx in player.Blockchain.PendingTransactions)
        {
            // Same identity rule the ratchet uses (§30.9): an address is a key, not an identity — a bid
            // whose coin selection spent a change-address UTXO is still the player's.
            if (!tx.Inputs.Any(i => IsPlayerBidderAddress(i.Address))) continue;
            foreach (TxOutput o in tx.Outputs)
            {
                if (o.Address == address) pending += o.Amount;
            }
        }
        return pending > 0m ? Math.Round(pending, 8) : null;
    }

    // ND.8d.6 — in-game days remaining before the recipient company's auction closes (null unless it's an
    // InAuction company with a live countdown). The wallet warns when this is ≤ AuctionClosingSoonWarningDays:
    // a bid may not be counted if no block is mined before the window closes (D-ND8d.7 refunds it if so).
    public double? GetAuctionDaysUntilClose(string address)
    {
        EnsureInitialized();
        long nowMs = GetPlayerLatestBlockTimestampMsStatic();
        NonMinerDonationSummary? entry = ComputeAuctionLedger(nowMs)
            .FirstOrDefault(s => s.NonMinerAddress == address);
        if (entry is null || entry.Status != NonMinerAuctionStatus.InAuction || entry.LeadingBidUnixMs == 0) return null;
        return Math.Max(0d, (entry.WindowCloseUnixMs - nowMs) / 86_400_000d);
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

    // ND.10g — returns the node id ALONGSIDE its formatted line instead of only the line. BlockExplorer's
    // mining-rate decorator used to recover the id by re-parsing the line's prefix
    // (`line[..line.IndexOf(" | ")]`), which the DEV rename ("Mt. Gox (non_miner_7) | mined: …") would have
    // silently broken — the ⛏ marker would simply have stopped appearing. Handing back the id as data
    // removes the parse entirely.
    public IReadOnlyList<(string nodeId, string line)> GetNodeStatusLines()
    {
        EnsureInitialized();
        Dictionary<string, int> mined = MinedBlockCountsByNode();
        return SharedNetwork.Nodes
            .OrderBy(n => n.NodeId)
            .Select(n => (n.NodeId, $"{DescribeNodeForDev(n.NodeId)} | mined: {(mined.TryGetValue(n.NodeId, out int c) ? c : 0)} | block: {n.Blockchain.Chain.Count} | pending: {n.Blockchain.PendingTransactions.Count} | balance: {AggregateSpendable(n):F8}"))
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
                // ND.10g — DEV form ("Mt. Gox (non_miner_7)"): this directory exists to be read beside a
                // trace/CSV row, so the raw id must stay.
                if (n.ReceiveWallet == null || n.ReceiveWallet.OwnedAddresses.Count <= 1)
                    return $"{DescribeNodeForDev(n.NodeId)}: {n.WalletAddress}";
                // Step 8.4 — the player rotates only CHANGE addresses (coinbase stays on base), founders that
                // rotate spread their REWARDS across fresh addresses (Satoshi). Word it per the node's mode.
                string kind = n.RotateCoinbaseAddress ? "rewards" : "change";
                return $"{DescribeNodeForDev(n.NodeId)}: {n.WalletAddress}  (base/identity; {kind} spread across {n.ReceiveWallet.OwnedAddresses.Count} addresses)";
            })
            .ToList();
    }

    private const string NonMinerNodeIdPrefix = "non_miner_";

    // ND.10g (2026-07-23, D-ND10g.1) — the ONE company-name resolver, and the only place the
    // non_miner_{i} <-> CompanyRoster.Auctionable[i-1] pairing (D-ND8.37) is turned into UI text.
    // Every other node id passes through untouched: `player`, `casino`, `bot_1..4`, the founders, and
    // the CAST miners — whose ids are already human names (`artforz`, `foundry_usa`, … from
    // NetworkPopulationScheduler's chronological pool; only the never-expected `miner_extra_N`
    // fallback is machine-shaped). Falls back to the raw id when the roster has no match (the
    // documented pool-size-mismatch case, NonMinerBots.Count > Auctionable.Count).
    // DISPLAY-ONLY — never a dictionary key, never compared against a node id.
    public static string DescribeNodeForDisplay(string nodeId) =>
        TryGetCompanyDisplayName(nodeId) ?? nodeId;

    // ND.10g — the DEV/diagnostic twin: "Mt. Gox (non_miner_7)". The raw id stays visible in the
    // diagnostic lists because it is the JOIN KEY of every CSV trace (casino_bot_bid_trace,
    // company_founding_trace, company_governance_trace, auction_settlement_trace) and of the
    // _companyFoundings / _companyGovernance / _stuckBidderSignatures dictionaries — dropping it would
    // mean cross-referencing a trace row against the roster CSV by hand during a playtest audit.
    public static string DescribeNodeForDev(string nodeId) =>
        TryGetCompanyDisplayName(nodeId) is string name ? $"{name} ({nodeId})" : nodeId;

    private static string? TryGetCompanyDisplayName(string nodeId)
    {
        if (!nodeId.StartsWith(NonMinerNodeIdPrefix, StringComparison.Ordinal)) return null;
        if (!int.TryParse(nodeId.AsSpan(NonMinerNodeIdPrefix.Length), out int oneBasedIndex)) return null;
        string? name = CompanyRoster.ForNonMinerIndex(oneBasedIndex - 1)?.DisplayName;
        return string.IsNullOrEmpty(name) ? null : name;
    }

    // Maps an address to a registered node id for display, or a shortened address if unknown.
    // ND.10g (D-ND10g.2) — a non-miner resolves to its COMPANY NAME here, which is what carries the
    // rename into the auction rows, the tracked-pool bids list and every wallet history panel at once.
    // Audited at ND.10g: all call sites are display-only (no caller keys a dictionary or compares a
    // node id against this) — keep it that way, per the §30.9 identity rule.
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
                return DescribeNodeForDisplay(node.NodeId);
            }
        }

        return address.Length > 12 ? address[..12] + "…" : address;
    }

    // ND.8d.2 — is this tracked-pool donor address the PLAYER's? (Player bids are canonicalized to the
    // player's base address in ComputeAuctionLedger, but any address the player's wallet owns is accepted
    // too, for robustness.) AuctioningCompanyDetails uses it to BLANK the re-bid % on a player-held slot —
    // the player bids manually from the BTC wallet and never rolls the casino-bot ladder.
    public bool IsPlayerBidderAddress(string address)
    {
        EnsureInitialized();
        if (!SharedNodesById.TryGetValue(PlayerNodeId, out NodeAgent? player)) return false;
        return player.WalletAddress == address
            || (player.ReceiveWallet?.OwnedAddresses.Contains(address) ?? false);
    }

    // ND.10f (2026-07-23) — the PROJECTED stock class the player would mint if this STILL-OPEN auction
    // closed at the current block: NST when the player occupies any tier ≤ NstTopTierCount of the tracked
    // pool, PST when it holds only lower tiers, None when it holds no tracked slot at all. Pure, read-only
    // and side-effect-free — a "what would I get right now" projection for the auction-time border colours
    // (BlockExplorer's Enroll Mode rows + the AuctioningCompanyDetails page frame), mirroring the founded
    // companies' holding-keyed gold/silver/black. Deliberately reuses FoundCompany's own two inputs — the
    // value-descending tracked-pool ranking and NstTopTierCount — so the live projection and the real mint
    // cannot diverge. It is a PROJECTION, not a promise: every later bid can re-order the pool.
    public PlayerAuctionStake GetPlayerProjectedStake(NonMinerDonationSummary summary)
    {
        EnsureInitialized();
        int tier = 1;
        var stake = PlayerAuctionStake.None;
        foreach (TrackedDonation d in summary.TrackedDonations.OrderByDescending(d => d.AmountBtc))
        {
            if (IsPlayerBidderAddress(d.DonorAddress))
            {
                if (tier <= NstTopTierCount) return PlayerAuctionStake.Nst; // best possible — stop early
                stake = PlayerAuctionStake.Pst;
            }
            tier++;
        }
        return stake;
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
    // ND.10g (D-ND10g.3) — the addresses of every non-miner that has ALREADY APPEARED on the historical
    // curve (in auction or already founded), for the BTC send panels' recipient lists. Under the raw
    // `non_miner_#` id, listing all 40 leaked nothing; under real company names it would put Coinbase and
    // Foundry USA in a 2011 dropdown. "Introduced" is the existing ledger concept — no new state, no new
    // date math. Callers build a dropdown on ENTERING send mode, not per frame, so the ledger's chain walk
    // is not a per-frame cost. Mirrors how ScheduleBotTransactionsAfterBlock already restricts its
    // automated recipients to introduced non-miners.
    public HashSet<string> GetIntroducedNonMinerAddresses()
    {
        EnsureInitialized();
        return GetNonMinerAuctionLedger()
            .Where(s => s.Status != NonMinerAuctionStatus.NotIntroduced)
            .Select(s => s.NonMinerAddress)
            .ToHashSet(StringComparer.Ordinal);
    }

    // ND.10g — the ONE recipient list the four BTC send panels (BTCWallet / FoundersWallets /
    // CasinoFinances / BotsBtcWallets) build their bot dropdowns from, so the naming rule and the
    // introduced-only filter live in a single place instead of four. Order matches BotWalletRegistry
    // .AllBots (miner bots, then non-miners, then cast miners); cast miners need no filter — they are only
    // created as the scheduler spawns them, and their ids ARE their names.
    public IReadOnlyList<(string displayName, string address)> GetSendableBotTargets()
    {
        EnsureInitialized();
        HashSet<string> introduced = GetIntroducedNonMinerAddresses();
        var targets = new List<(string, string)>();
        foreach (BotWalletRecord bot in BotWalletRegistry.AllBots)
        {
            // D-ND10g.3 — a company that has not appeared yet on the historical curve is not listed: it
            // does not exist in the world yet, and its real name would be an anachronism on screen.
            // IsMinerNode is false ONLY for the 40 auction non-miners (cast miners register as miners).
            if (!bot.IsMinerNode && !introduced.Contains(bot.Address)) continue;

            targets.Add((DescribeNodeForDisplay(bot.NodeId), bot.Address));
        }

        return targets;
    }

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
            // the current leader's principal + its minimum raise (the ND.10e price-anchored opening
            // floor when there is no leader yet);
            // the first bid in the group to clear a strictly-higher floor than the running best becomes
            // the new leader. A gap of more than AuctionWindowMs between the leader's own bid and the
            // next candidate group means the window ALREADY closed there — permanently (D-ND4b.12): no
            // bid after that point can revive or re-win a resolved auction, however large.
            (string donor, decimal amount, long ts, long seq)? leader = null;
            long? resolvedAtMs = null;
            // The bids this walk decided do NOT participate at all — excluded from the leading-bid ratchet
            // AND from the tracked pool below (so: no lead, no window reset, no slot, no stock). Two rules
            // populate it, and neither refunds the coins (D-ND10k.2 — they reach the company as a plain
            // non-participating transfer):
            //   • ND.8d.6 (D-ND8d.6, 2026-07-20) — LAST-BID PRESERVATION, cross-block: a further bid from
            //     the party that is ALREADY the current leader. (In practice only the player triggers this
            //     — bot_1..4's tier-1 satisfied rule keeps them off their own leader pool.)
            //   • ND.10k (D-ND10k.1, 2026-07-28) — ONE BID PER DONOR PER BLOCK: within a same-block group,
            //     every bid a donor made except its highest. See the pass-1 comment in the loop.
            // Both are warned in the BTC wallet before the send, and both are non-blocking.
            var ignoredBidSeqs = new HashSet<long>();
            foreach (IGrouping<long, (string donor, decimal amount, long ts, long seq)> group in bids.GroupBy(d => d.ts).OrderBy(g => g.Key))
            {
                if (resolvedAtMs.HasValue) break;
                if (leader.HasValue && group.Key > leader.Value.ts + AuctionWindowMs)
                {
                    resolvedAtMs = leader.Value.ts + AuctionWindowMs;
                    break;
                }

                // ND.10k (2026-07-28, §14.12, D-ND10k.1) — PASS 1: ONE PARTICIPATING BID PER DONOR PER
                // BLOCK. ND.8d.6's last-bid preservation is a CROSS-block rule — it compares each bid
                // against the leader as of the START of this group, and `leader` is only advanced after
                // the whole group is scanned (D-ND4b.11: same-block bids race the same starting leader,
                // never each other in sequence). That rule was written for two DIFFERENT bidders racing;
                // it has no same-donor case, so TWO bids from one party confirmed in the SAME block both
                // counted and both entered the tracked pool. Found in a live playtest: the player sent
                // 10 BTC to BitInstant, it sat unconfirmed in the mempool, they sent 10 BTC again, and
                // block 965 confirmed both — leaving one party holding tiers 1 AND 2 of the same pool.
                //
                // That is an EXPLOIT, not a cosmetic issue: splitting one bid in two buys the same total
                // participation but TWO entries in the 5.2%-halving slot-bonus ladder (D-ND8.15), and it
                // denies a third party an NST seat. It is reachable by the bots too — D-ND6.9's
                // affordability cascade deliberately does not mark a declining bot as used, so one bot
                // can be drawn for two slots in the same block.
                //
                // The rule: within a block, a donor participates with its HIGHEST bid only; every other
                // bid it made in that block is ignored exactly like a leader self-raise — no lead, no
                // window reset, no tracked slot, no stock. Highest (not first) keeps this consistent with
                // D-ND4b.11's existing same-block resolution, and means a small accidental send can never
                // knock out a large deliberate one; exact ties keep the earliest seq, also per D-ND4b.11.
                // D-ND10k.2: an ignored bid is NOT refunded — the coins reach the company as a plain
                // non-participating transfer, byte-for-byte the treatment ND.8d.6 already gives a leader
                // self-raise. The wallet warns before the send (GetPendingAuctionBidBtc).
                var groupBids = new List<(string donor, decimal amount, long ts, long seq)>();
                var keptIndexByDonor = new Dictionary<string, int>();
                foreach ((string donor, decimal amount, long ts, long seq) d in group)
                {
                    // ND.8d.6 — the current leader re-bidding on itself is ignored (last-bid preservation):
                    // it neither re-leads nor resets the window, and is dropped from the tracked pool.
                    if (leader.HasValue && d.donor == leader.Value.donor)
                    {
                        ignoredBidSeqs.Add(d.seq);
                        continue;
                    }

                    if (keptIndexByDonor.TryGetValue(d.donor, out int kept))
                    {
                        if (d.amount > groupBids[kept].amount)
                        {
                            ignoredBidSeqs.Add(groupBids[kept].seq); // superseded by this larger one
                            groupBids[kept] = d;
                        }
                        else
                        {
                            ignoredBidSeqs.Add(d.seq); // lower or tied — the earlier/larger one stands
                        }
                        continue;
                    }

                    keptIndexByDonor[d.donor] = groupBids.Count;
                    groupBids.Add(d);
                }

                // PASS 2 — the floor / best-of-group scan, unchanged, over the surviving one-per-donor set.
                (string donor, decimal amount, long ts, long seq)? best = null;
                foreach ((string donor, decimal amount, long ts, long seq) d in groupBids)
                {
                    // ND.4d — the floor a candidate must clear depends on WHO is bidding, not just on
                    // the current leader: the player only needs to clear the leader by one satoshi;
                    // everyone else (the casino-bots) still needs the full RaiseMin jump. Pre-leader,
                    // both start at the same price-anchored opening floor (D-ND10e.1, superseding D-ND4b.5).
                    // ND.10e (D-ND10e.1) — the pre-leader opening floor is the price-anchored one, judged
                    // at THIS bid group's own timestamp (deterministic on replay), not a flat 0.1 BTC.
                    decimal floor = !leader.HasValue
                        ? OpeningBidFloorBtcAt(group.Key)
                        : playerAddresses.Contains(d.donor)
                            ? leader.Value.amount + OneSatoshi
                            : leader.Value.amount + RaiseMin(leader.Value.amount, group.Key);
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
                // No bid ever cleared even the opening floor (the casino-bot cycle always
                // meets it — this covers a player sending less than that floor as their very first send).
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

            // ND.8d.7 (D-ND8d.7) — a bid confirmed AFTER the window closed never participates: exclude
            // post-close bids from the tracked pool (and thus the stock distribution / donor set), so a late
            // bid earns nothing. For an InAuction company the close is in the future ⇒ no-op; it only bites a
            // resolved company's late bids (which CancelAndRefundStaleAuctionBids then refunds). Also excludes
            // the ND.8d.6 leader self-raises — an ignored bid "doesn't participate in the auction" at all.
            summary.TrackedDonations = ComputeTrackedDonationPool(
                bids.Where(bd => bd.ts <= summary.WindowCloseUnixMs && !ignoredBidSeqs.Contains(bd.seq)).ToList(), nowMs);

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

        // ONE pass over the UTXO set for the whole owned set, not one pass PER address (R3, 2026-07-28):
        // GetAddressSpendableBalance walks the entire UTXO set and rebuilds the pending-spent outpoint set
        // on every call, so the per-address loop cost O(addresses × utxos) — and the casino, whose change
        // rotation gives it the largest address book in the world, is exactly the node the swap desk asked
        // for on every settled bet. GetSpendableUtxos already accepts the whole set and applies the same
        // spendable/maturity/pending filters, so the result is identical by construction (an outpoint has
        // exactly one address, so no double counting is possible).
        var addresses = new HashSet<string>(node.ReceiveWallet.OwnedAddresses) { node.WalletAddress };
        decimal total = 0m;
        foreach (var utxo in node.Blockchain.GetSpendableUtxos(addresses))
            total += utxo.amount;
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
        // INC-001 / D-15.26 — a session whose snapshot FAILED to load must never write back over the file it
        // could not read. On 2026-07-29 a truncated state.json made EnsureInitialized throw; the 1,666-block
        // world survived only because that throw happened to land before any writer ran. This makes the
        // guarantee explicit instead of accidental: one catch-and-continue in the wrong place would otherwise
        // replace a real chain with an empty one at the next mined block.
        if (_snapshotLoadFailed)
        {
            GD.PrintErr("[NetworkRoot] Refusing to persist — the world snapshot failed to load this session " +
                        "(see the error above). The on-disk state is left untouched.");
            return;
        }

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
            CompanyInflowMultipliers = new Dictionary<string, decimal>(_companyInflowMultipliers),
            BankState = new Dictionary<string, BankBalanceSheet>(_bankState),
            ClosedCompanies = new Dictionary<string, CompanyClosure>(_closedCompanies),
            FbiActivated = _fbiActivated,
            FbiScFunds = _fbiScFunds
        };

        // INC-001 / D-15.26 — ATOMIC write. This used to truncate StatePath and stream ~9 MB straight into
        // it, so a process death mid-write left a page-aligned, plausible-looking, INVALID file (the crash
        // that ended the P15.8 session left it 7 bytes short of parseable). Now: serialize → write a temp
        // file → CLOSE it → rename over the target. A rename either fully succeeds or leaves the previous
        // file untouched, so a reader can never observe a half-written world.
        //
        // GOTCHA: the `using` must be an explicit BLOCK, not a using-declaration. A declaration lives until
        // the method returns, which would leave the handle open across the rename below.
        string serialized = JsonSerializer.Serialize(snapshot, JsonOptions);
        using (FileAccess file = FileAccess.Open(StateTempPath, FileAccess.ModeFlags.Write))
        {
            if (file is null)
            {
                GD.PrintErr($"[NetworkRoot] Could not open {StateTempPath} for writing " +
                            $"({FileAccess.GetOpenError()}) — world NOT persisted this block.");
                return;
            }

            file.StoreString(serialized);
            file.Flush();
        }

        try
        {
            System.IO.File.Move(ProjectSettings.GlobalizePath(StateTempPath),
                                ProjectSettings.GlobalizePath(StatePath),
                                overwrite: true);
        }
        catch (Exception e)
        {
            // The previous snapshot is intact by construction — the rename is what would have replaced it.
            // Loud, but not fatal: the next mined block retries the whole write.
            GD.PrintErr($"[NetworkRoot] Snapshot rename failed — the previous world state is still on disk " +
                        $"and this block was NOT committed: {e.Message}");
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
        DeleteIfExists(StateTempPath); // INC-001 — a stale staged write must not survive a world wipe
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
        DeleteIfExists("user://central_bank_state.json"); // Step 15 P15.1d — same rule

        // DEV trace telemetry: not player-visible, but rows dated under the other timeline would make the
        // traces unreadable (founders_trace is actively used to verify founder pacing) — start them fresh.
        DeleteIfExists("user://logs/difficulty_trace.csv");
        DeleteIfExists("user://logs/founders_trace.csv");
        DeleteIfExists("user://logs/swap_desk_trace.csv");
        DeleteIfExists("user://logs/network_population_trace.csv");
        DeleteIfExists(CompanyFoundingTracePath); // ND.6b — was missing since ND.5 (same reasoning as the others); ND.8b.2 renamed the file
        DeleteIfExists(CompanyGovernanceTracePath); // ND.8b.3 — added WITH the feature (the TL.3/ND.6b rule)
        DeleteIfExists(CasinoBotBidTracePath);
        DeleteIfExists(BankCreditTracePath); // Step 15 P15.3a — added WITH the feature (the TL.3/ND.6b rule)

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

    // INC-001 / D-15.26 — set when the world snapshot exists but cannot be read. Guards PersistStateToDisk
    // so a session that failed to load can never write over the file it failed to read.
    private static bool _snapshotLoadFailed;

    // The `Try` prefix is a PROMISE that this function handles its own failure — and for two whole steps it
    // did not: a raw Deserialize threw straight out of EnsureInitialized, which aborted before registering a
    // single node, leaving _isInitialized false, every consumer looking at an empty world, and NOTHING in the
    // log. The money services persist to their own files and restored perfectly, so a total world-load
    // failure presented as "some screens are blank" — about the least diagnosable shape available.
    //
    // Now: a corrupt snapshot is reported with its path, size and reason; the atomic writer's temp file is
    // tried as a fallback (it can only be adopted if it fully parses); and if neither can be read the load
    // ABORTS LOUDLY rather than handing back an empty-but-plausible world. See Documentation/INCIDENT_LOG.md
    // INC-001 and ProjectDesignManual.md Ch. 40.
    private static BlockchainStateSnapshot? TryLoadSnapshot()
    {
        // A rename replaces its target, so StatePath being absent means it never existed: a genuine first
        // run or a post-reset world. Any leftover temp file here can only be a partial first write.
        if (!FileAccess.FileExists(StatePath))
        {
            return null;
        }

        (BlockchainStateSnapshot? snapshot, string? error) = ReadSnapshotFile(StatePath);
        if (snapshot is not null)
        {
            return snapshot;
        }

        GD.PrintErr($"[NetworkRoot] CORRUPT world snapshot at {StatePath} " +
                    $"({DescribeFileSize(StatePath)}): {error}");

        if (FileAccess.FileExists(StateTempPath))
        {
            (BlockchainStateSnapshot? staged, string? tempError) = ReadSnapshotFile(StateTempPath);
            if (staged is not null)
            {
                GD.PrintErr($"[NetworkRoot] Recovered the world from {StateTempPath} — a previous run died " +
                            "between the staged write and the rename.");
                return staged;
            }

            GD.PrintErr($"[NetworkRoot] The staged fallback {StateTempPath} is unreadable too: {tempError}");
        }

        _snapshotLoadFailed = true;
        GD.PrintErr("[NetworkRoot] WORLD LOAD ABORTED. Nothing will be persisted this session, so the " +
                    "on-disk state is safe to repair or restore from a backup. " +
                    "See Documentation/INCIDENT_LOG.md (INC-001) for the repair procedure.");
        throw new InvalidOperationException(
            $"World snapshot at {StatePath} could not be loaded: {error}");
    }

    // Returns (snapshot, null) on success, (null, reason) on any failure. Never throws — the caller decides
    // what a failure means, which is the whole point of the split.
    private static (BlockchainStateSnapshot? Snapshot, string? Error) ReadSnapshotFile(string path)
    {
        try
        {
            string json;
            using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
            {
                if (file is null)
                {
                    return (null, $"could not be opened ({FileAccess.GetOpenError()})");
                }

                json = file.GetAsText();
            }

            BlockchainStateSnapshot? snapshot = JsonSerializer.Deserialize<BlockchainStateSnapshot>(json);
            if (snapshot is null)
            {
                return (null, "deserialized to null");
            }

            return (snapshot, null);
        }
        catch (Exception e)
        {
            return (null, e.Message);
        }
    }

    private static string DescribeFileSize(string userPath)
    {
        try
        {
            return $"{new System.IO.FileInfo(ProjectSettings.GlobalizePath(userPath)).Length:N0} bytes";
        }
        catch
        {
            return "size unknown";
        }
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

        // Step 15 P15.2a/b — a BANK's market category is LOCKED to its roster default (D-15.12), which makes
        // it a DERIVED value rather than a voted one: re-deriving it from the roster on restore is therefore
        // always correct, and it keeps the P15.2a gradient reassignment (three banks moved off "official")
        // from stranding a bank that founded under the old roster with a stale category — no world-format
        // bump needed for a data change the lock already guarantees. Runs after BOTH dictionaries are
        // restored, since IsBankCompany resolves through _companyFoundings.
        foreach (CompanyGovernanceState gov in _companyGovernance.Values)
        {
            if (!IsBankCompany(gov.NonMinerNodeId)) continue;

            string? rosterCategory = CompanyRoster.ByCompanyId(gov.CompanyId)?.MarketCategory;
            if (string.IsNullOrEmpty(rosterCategory) || gov.MarketCategory == rosterCategory) continue;

            GD.Print($"[NetworkRoot] Bank {gov.NonMinerNodeId} ({gov.CompanyId}) category re-derived from the roster: '{gov.MarketCategory}' → '{rosterCategory}' (P15.2b lock).");
            gov.DefaultMarketCategory = rosterCategory;
            gov.MarketCategory = rosterCategory;
        }

        _botGovernancePreferences.Clear();
        foreach ((string botNodeId, BotGovernancePreference pref) in snapshot.BotGovernancePreferences ?? new Dictionary<string, BotGovernancePreference>())
        {
            _botGovernancePreferences[botNodeId] = pref;
        }
        // DEV: restate the stances on every world load, so a long session's log always carries them near
        // the top rather than only in the (possibly weeks-old) block where they were first drawn.
        PrintBotGovernanceStances("restored with the world");

        _companyInflowMultipliers.Clear();
        foreach ((string companyId, decimal multiplier) in snapshot.CompanyInflowMultipliers ?? new Dictionary<string, decimal>())
        {
            _companyInflowMultipliers[companyId] = multiplier;
        }

        // Step 15 P15.2c — the banks' layer-1 balance sheets, same additive-field rule (absent/null on a
        // pre-plan15 snapshot ⇒ empty, which is exactly right: no bank has financed anything yet).
        _bankState.Clear();
        foreach ((string bankNodeId, BankBalanceSheet sheet) in snapshot.BankState ?? new Dictionary<string, BankBalanceSheet>())
        {
            if (sheet == null) continue;
            _bankState[bankNodeId] = sheet;
        }

        // Step 15 P15.5a — same additive-field rule (absent/null ⇒ no company has died yet).
        _closedCompanies.Clear();
        foreach ((string nodeId, CompanyClosure closure) in snapshot.ClosedCompanies ?? new Dictionary<string, CompanyClosure>())
        {
            if (closure == null) continue;
            _closedCompanies[nodeId] = closure;
        }

        _fbiActivated = snapshot.FbiActivated; // Step 15 P15.6
        _fbiScFunds = Scripts.Finance.Money.Normalize(Math.Max(0m, snapshot.FbiScFunds));
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
        // Step 15 P15.2c — keyed by the BANK's NonMinerNodeId, mirrors _bankState (same additive-field rule).
        public Dictionary<string, BankBalanceSheet> BankState { get; set; } = new();
        // Step 15 P15.5a — keyed by NonMinerNodeId, mirrors _closedCompanies (same additive-field rule).
        public Dictionary<string, CompanyClosure> ClosedCompanies { get; set; } = new();
        // Step 15 P15.6 — the FBI's activation latch + self-funding budget (false/0 on an older snapshot,
        // which is exactly right: the thread simply activates on its date the way it always would).
        public bool FbiActivated { get; set; }
        public decimal FbiScFunds { get; set; }
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
    // ND.9c (2026-07-22) — the FROZEN founding-mint breakdown, one entry per unique donor, capturing HOW the
    // NST/PST distribution was computed at auction close (tier occupancy, participation %, slot-bonus, base
    // vs final tokens). Populated inside FoundCompany's per-donor loop from the values it already computes —
    // no reconstruction path, no shared helper (developer decision, ND.9c). Rides this same
    // BlockchainStateSnapshot for free. Legacy companies founded before ND.9c have an EMPTY list; the
    // CompanyDetails snapshot section degrades to a one-line "unavailable" notice + the plain token list.
    public List<CompanyFoundingBreakdown> FoundingBreakdown { get; set; } = new();
}

// ND.9c — one donor's founding-mint math, frozen at auction close (see CompanyFounding.FoundingBreakdown).
// HolderId is "player" / a bot nodeId / (fallback) the raw donor address, resolved exactly as the sibling
// CompanyShareHolding. Tiers are 1-based positions in the value-ranked tracked pool (1 = largest bid); a
// donor can hold more than one. IsNst mirrors the holding's class (any tier ≤ 3 ⇒ NST, else PST).
public sealed class CompanyFoundingBreakdown
{
    public string HolderId { get; set; } = string.Empty;
    public List<int> Tiers { get; set; } = new();
    public decimal AmountBtcAtClose { get; set; }
    public decimal ParticipationShare { get; set; }
    public decimal BaseTokens { get; set; }
    public decimal BonusFraction { get; set; }
    public decimal FinalTokens { get; set; }
    public bool IsNst { get; set; }
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

// ND.10f — the stock class the player would mint if a STILL-OPEN auction closed at the current block; the
// auction-time counterpart of a founded company's CompanyShareHolding class. See
// NetworkRoot.GetPlayerProjectedStake. Purely a UI signal — nothing mechanical reads it.
public enum PlayerAuctionStake
{
    None,
    Pst,
    Nst
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
    // ND.8g (2026-07-21, §12.5.6) — the PLAYER's own dividend claim log for this company (bot claims,
    // via TryAutoClaimBotDividends, never write here — this is a player-facing history only). Rides this
    // same BlockchainStateSnapshot for free, no new persisted file/checkpoint/delete-list work (the same
    // inheritance argument ClaimableByHolder/VoteHistory themselves already rely on). Capped defensively
    // (PlayerBankAccountService.BankTransferRecord's 500-cap precedent — a player claiming this many times
    // from ONE company is not expected; the cap is a safety net, not a real constraint).
    public List<CompanyDividendClaimRecord> PlayerClaimHistory { get; set; } = new();
    // Step 15 P15.4d/e — banks only. PendingShortfallSc is the gap a quarterly repayment could not raise
    // from collateral; it opens a shortfall vote as soon as no other vote is running (the quarterly must
    // close first, so the dividend the vote may cut has actually been finalized). UnrecoverableShortfallSc
    // is what remained after the vote applied BOTH cuts — a bank carrying one is insolvent and is the
    // dissolution trigger P15.5a reads (D-15.8).
    public decimal PendingShortfallSc { get; set; }
    public decimal UnrecoverableShortfallSc { get; set; }
    // Step 15 P15.6a — SC THROUGHPUT, the base of the FBI's throughput-relative tolerance (D-15.21): every
    // conversion that credits ScReserve also accrues here, and the quarter close rolls current → last.
    // Absolute SC ceilings would go stale across the 2009–2025 span; a company's own recent inflow does not.
    public decimal ScInflowCurrentQuarterSc { get; set; }
    public decimal ScInflowLastQuarterSc { get; set; }
    // Step 15 P15.6b — the F1 investigation meter: accrues while ScReserve sits above the company's
    // tolerance (∝ overage × category darkness), decays back under it. At/above
    // NetworkRoot.InvestigationFlagThreshold the company is FLAGGED and eligible for the P15.6c raid roll.
    public decimal InvestigationScore { get; set; }
}

// Step 15 P15.2c (D-15.4/D-15.5) — a founded BANK's balance sheet: the LAYER-1 half of the two-layer
// accounting model (layer 0 = the FED's per-client accounts in CentralBankService). Keyed by the bank's
// NonMinerNodeId in NetworkRoot._bankState, only for the four CompanyRoster.Banks, and persisted in the
// same BlockchainStateSnapshot as _companyFoundings/_companyGovernance — so checkpoint coverage,
// world-reset delete-list membership and the pre-genesis path all come for free (the ND.8g inheritance
// argument).
//
// CollateralBtc (D-15.4) is a QUARANTINED account, deliberately separate from the bank's own CB1 business
// inflows: it is the BTC bought from the companies the bank finances, held to service the FED debt and
// sold "extra-lazy" — just enough, only on a quarterly payment day (P15.4). The bank's own inflows keep
// auto-converting to SC exactly like any other CB1 company. Two BTC streams, one wallet, two books.
public sealed class BankBalanceSheet
{
    public decimal CollateralBtc { get; set; }
    // The bank's own client book: which company it financed, how much BTC it bought and SC it paid.
    public Dictionary<string, BankClientAccount> Clients { get; set; } = new();
}

// One company's account at one bank. Totals are exact and cumulative; History is capped (see
// NetworkRoot.MaxBankClientHistory) exactly like the FED's own per-client history.
public sealed class BankClientAccount
{
    public decimal BtcBought { get; set; }
    public decimal ScPaid { get; set; }
    public int ProvisionCount { get; set; }
    public List<BankClientEntry> History { get; set; } = new();
}

// One provisioning event: the bank paid ScPaid to the company and received BtcBought in exchange, priced
// at that day's clean market rate. AtUnixMs is the mining block's timestamp — game time, like every other
// persisted timestamp in this file.
public sealed class BankClientEntry
{
    public long AtUnixMs { get; set; }
    public int BlockIndex { get; set; }
    public decimal BtcBought { get; set; }
    public decimal ScPaid { get; set; }
    public decimal PriceUsd { get; set; }
}

// Step 15 P15.5a (D-15.15/D-15.17) — one DISSOLVED company. Dissolution applies to every company, banks
// included; only the casino is exempt (D-15.17, it is the player's house and keeps its unlimited FED
// credit line forever). Two reasons today: `debt_default` (a bank that could not service its FED
// installment by any means, P15.4e) and `fbi_seizure` (P15.6).
//
// CUSTODY MODEL (D-15.18): a closure does NOT move the dead company's coins. Its wallet stays on-chain,
// unspendable by anything (no code path owns a dissolved company), and keeps receiving whatever automatic
// inflows were already scheduled to it — that IS what "the FED holds it custodially, 100% as BTC" means
// in a world where every satoshi must live at a real address and the FED has none. Only when a solvent
// bank of the matching market category inherits the wallet does the BTC actually move (P15.5c), after
// which new arrivals are forwarded to that bank per block (P15.5b).
//
// Rides BlockchainStateSnapshot like every other company record — checkpoint coverage, delete-list
// membership and the pre-genesis path all inherited (the ND.8g argument).
public sealed class CompanyClosure
{
    public string NonMinerNodeId { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public long ClosedAtUnixMs { get; set; }
    public string Reason { get; set; } = string.Empty;         // "debt_default" | "fbi_seizure"
    public string MarketCategory { get; set; } = string.Empty; // at closure — the P15.5c inheritance key
    public bool WasBank { get; set; }

    // The loss the FED actually ate: what the company still owed after its last SC was applied.
    public decimal DebtAtClosureSc { get; set; }
    // Balances at the moment of closure, for the recovery tracker's "owed vs recovered" readout.
    public decimal ScAtClosure { get; set; }
    public decimal BtcAtClosure { get; set; }

    // P15.5b — cumulative BTC actually delivered to an absorber since closure (the swept opening balance
    // plus every forwarded inflow). Compared against DebtAtClosureSc at live prices by the DEV tracker.
    public decimal RecoveredBtc { get; set; }
    // P15.5c — "" while the FED holds the wallet custodially; the bank's nodeId once inherited.
    public string InheritingBankNodeId { get; set; } = string.Empty;
    public long InheritedAtUnixMs { get; set; }

    // P15.5d — what the player held when the company died, kept ONLY so the closure notice can say what
    // was lost. The live holdings themselves are destroyed at closure (liquidation, D-15.15).
    public decimal PlayerNstAtClosure { get; set; }
    public decimal PlayerPstAtClosure { get; set; }
    public decimal PlayerUnclaimedBtcAtClosure { get; set; }
    public decimal PlayerUnclaimedScAtClosure { get; set; }
}

// ND.8g — one successful player dividend claim from one company (BTC/SC amounts actually paid THIS press,
// plus that day's BTC/SC price for the historical-value calculation). Written only when
// TryClaimPlayerCompanyDividends pays something (paidBtc > 0 || paidSc > 0) — a "nothing to claim"
// attempt writes nothing. BtcPriceUsdAtClaim is SC-denominated too (SC is USD-pegged 1:1); null only in
// the practically-unreachable case a founded company predates Market Birth (it can't — Market Birth
// 2010-07-18 is well before the earliest possible founding).
public sealed class CompanyDividendClaimRecord
{
    public long ClaimedAtUnixMs { get; set; } // game time (CalendarTimeService), never wall-clock
    public decimal BtcAmount { get; set; }
    public decimal ScAmount { get; set; }
    public decimal? BtcPriceUsdAtClaim { get; set; }
}

// One open vote (D-ND8.18): founding-day, quarterly, or >30%-inflow special. Bots' ballots are cast at
// open; the player's arrives via TryRegisterPlayerVote while AwaitingPlayerVote pauses the game.
public sealed class CompanyVote
{
    public string Kind { get; set; } = string.Empty; // "founding" | "quarterly" | "special" | "shortfall"
    public long OpenedAtMs { get; set; }
    public long ClosesAtMs { get; set; }
    public bool AwaitingPlayerVote { get; set; }
    public Dictionary<string, CompanyBallot> Ballots { get; set; } = new(); // holderId → ballot
    // P15.4e — SHORTFALL votes only: the SC gap this vote must close (what the bank still owes the FED
    // after selling every satoshi of collateral it had). 0 for every other kind.
    public decimal ShortfallScTarget { get; set; }
}

// One NST holder's ballot (D-ND8.19b): a continuous reserve target (clamped to the band), a discrete
// market direction (-1 lighter / 0 hold / +1 darker — quarterly votes only), and a quarterly payout-rate
// preference (% of each reserve side, quarterly votes only).
public sealed class CompanyBallot
{
    public decimal ReserveScPercentTarget { get; set; }
    public int MarketShift { get; set; }
    public decimal PayoutRatePercent { get; set; }
    // Step 15 P15.4e (D-15.7/D-15.15) — SHORTFALL votes only: the share of the gap taken out of
    // shareholders' dividends; the complement comes out of the company's own SC reserve. Ignored by every
    // other vote kind. Defaults to the no/tied-vote 50/50 split.
    public decimal DividendsCutPercent { get; set; } = NetworkRoot.DefaultShortfallDividendsCutPercent;
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
    // ND.9g (2026-07-22) — the company's three policy dials RIGHT BEFORE this vote's result was applied,
    // so the CompanyDetails "Last Vote Snapshot" can show before→after. Legacy records (closed before
    // ND.9g) leave these at defaults and the snapshot shows only the result fields above.
    public decimal BeforeReserveScPercent { get; set; }
    public string BeforeMarketCategory { get; set; } = string.Empty;
    public decimal BeforePayoutRatePercent { get; set; }
    // ND.9h — the quarter length (in-game days) of the cycle this quarterly vote opened, so the snapshot
    // can split each PST holder's quarter total into a daily amount. 0 for non-quarterly votes.
    public int QuarterDaysInCycle { get; set; }
    // ND.9g — every ballot cast in this vote (all cast ballots come from NST holders — only they get the
    // vote panel). Rides the same BlockchainStateSnapshot; the list is ≤ the NST-holder count (tiny).
    public List<VoteBallotRecord> Ballots { get; set; } = new();
}

// ND.9g — one participant's cast ballot in a closed vote (for the "Last Vote Snapshot"). Weight is the
// holder's NST ÷ total NST at close (a 0..1 fraction). MarketShift is -1 lighter / 0 hold / +1 darker;
// ReserveScPercentTarget and PayoutRatePercent are the ballot's raw preferences.
public sealed class VoteBallotRecord
{
    public string HolderId { get; set; } = string.Empty;
    public decimal Nst { get; set; }
    public decimal Weight { get; set; }
    public decimal ReserveScPercentTarget { get; set; }
    public int MarketShift { get; set; }
    public decimal PayoutRatePercent { get; set; }
}

// ND.8b.3 (D-ND8.13/D-ND8.26) — one casino-miner-bot's governance identity, re-rolled per world: a
// Currency Band preference (distinct 4-of-5 draw across the four bots) + a market-category preference
// (distinct permutation, all four stances represented).
public sealed class BotGovernancePreference
{
    public string CurrencyBandPreference { get; set; } = "CB3";
    public string MarketCategoryPreference { get; set; } = "official";
    // Step 15 P15.4b (D-15.13) — a THIRD, independent governance axis drawn per world: how hard this bot
    // pushes for money in shareholders' pockets over money kept in the company. It biases every
    // "dividends vs. company money" vote — the quarterly payout rate (all companies) and the P15.4e bank
    // shortfall split — but deliberately NOT the reserve-band (currency-mix) vote, which is a different
    // question entirely.
    //
    // Deliberately defaults to EMPTY, not to a stance: greed arrived after the other two axes, so a
    // snapshot whose preferences were drawn before it must be DISTINGUISHABLE from a bot that genuinely
    // drew the neutral stance — that is what lets NetworkRoot.BackfillGreedPreferences fill only the
    // absent ones instead of leaving the whole axis stuck. Readers normalize empty to `almost_greedy`
    // (exactly what every bot did before greed existed).
    public string GreedPreference { get; set; } = string.Empty;
}
