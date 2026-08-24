# Referral Auction — Specification

Extracted from `CLAUDE.md`'s Canonical Decisions table (2026-08-20), where it had grown into a single
32,007-character row mixing the live rule with fourteen amendments' worth of archaeology.

**Section 1 is the rule. Section 2 is history — superseded, kept for the reasoning only.** Implement
against section 1; read section 2 only to understand why a rule is shaped the way it is.

Every figure in section 1 was verified against `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` at
extraction time, and is cited **by symbol** — grep the name.

> **Cited by symbol since 2026-08-23; it was line numbers until then, and all nine had drifted.** Every
> anchor was off by exactly **+10** — a single insertion earlier in the file moved the whole set at once,
> and because the drift was uniform and small, each broken link still landed on plausible-looking auction
> code. Nothing failed; the citations just quietly stopped pointing at what they named. This is Standing
> Convention 15: **cite the symbol, not the line.** Do not reintroduce line numbers here — the symbols
> below were each re-verified by grep at the time of the rewrite.

---

## 1. Canonical rule (current)

- **Scope** — 40 non-miner companies, introduced from Market Birth (2010-07-18) along the historical active-address curve (all 40 by 2017-12-13). Only the player and `bot_1..4` may bid; the Step-14 cast never qualifies (D-EB.7).
- **Opening** — the first qualifying bid into an unbid pool must be worth **$0.10, capped at 1 BTC**, floored at one satoshi, priced at that bid's own block timestamp so a chain replay is deterministic; no market data falls back to the 1 BTC cap (`OpeningBidFloorBtcAt`). It opens a rolling **20 in-game-day** window (`AuctionWindowMs`).
- **Raise floor** — bots must clear `leadingBid + max(openingFloor, 5% × leadingBid)`, coin-flipping between that and `leadingBid + max(2 × openingFloor, 10% × leadingBid)`, plus a random additive tail so bids never repeat as round numbers; the player needs **+1 satoshi**, regardless of size (ND.4d). The absolute floor term is the *live* opening floor, not a constant — `RaiseMin` / `RaiseMax`. Every accepted bid takes the lead and resets the window to a fresh 20 days.
- **One bid per donor per block** — the highest counts, the rest are ignored (no lead, no window reset, no tracked slot, no stock, **no refund**); ties keep the earliest `seq`. A send from the current leader never counts as a bid.
- **Late bids** — still-pending qualifying bids to a resolved company are dropped, so the coins simply stay in the wallet; post-close *confirmed* ones are refunded from the founded company's treasury, fee-deducted (memo `· AUCTION REFUND`).
- **Tracked Donation Pool** — top 10 by BTC amount (`MaxTrackedDonations`), value-ordered and never chronological; ties never evict. The top 3 tiers are the NST band (`NstTopTierCount`).
- **Resolution FOUNDS the company** — the tracked pool mints the NST/PST distribution. No SC cashback, no BTC sweep. **A win is permanent** (D-ND4b.12), never reopened by a later ratchet rework.
- **Bot cadence** — 0/1/2 weighted bid attempts per mined block (15% / 70% / 15%), drawn only among *eligible* bots: those holding ≥1 qualifying, affordable pool at a nonzero probability.
- **Parallel rolls** — every biddable pool rolls its own ladder probability each slot; the hits compete in a **weighted** tie-break, an unparticipated pool carrying an explicit `FreshPoolSeedingWeight` (34) rather than its sentinel probability of 100 — applied in `TryBuildCasinoBotBid`.
- **Exclusions, first match wins** (the `exclusion` chain in `BuildBotPoolOpportunities`: `ExclusionReserve` / `ExclusionSatisfied` / `ExclusionGuard` / `ExclusionPricedOut`) — `reserve` (bot rests at ≤ `BotBidReserveStopBtc` spendable, resumes only at ≥ `BotBidReserveResumeBtc`; hysteresis, chain-seeded at first read) → `satisfied` (tier 1 always; tier 3 at ≥2 own bids; tier 2 never — `IsBidderSatisfied`) → `guard` (the pool is **full** at `MaxTrackedDonations` *and* the bot holds its **smallest** slot, i.e. its own bid would evict its own donation) → `priced out` (`required + fee > spendable × MaxBidBalanceFraction`). A surviving pool computing to ≤0% is defensively re-labelled `satisfied`.
- **Ladder, chosen per pool by occupied slot count** — three modes, selected in `ReBidProbabilityPercentFor`. Tiers 4+ come from a dictionary per mode: `EarlyRushReBidProbabilityPercentByTier` (pool below `EarlyRushSlotThreshold` occupied slots) · `ReBidProbabilityPercentByTier` (NORMAL, at or above it) · `UrgentReBidProbabilityPercentByTier` (window inside its final `AuctionUrgencyWindowMs`, NORMAL pools only — one Fibonacci step up). Tiers 2–3 are bid-count-aware and live in named constants instead, read through `ShallowTierProbabilityPercent`: `Tier2EarlyRushPercent`, `Tier2NormalOneBidPercent` / `Tier2NormalManyBidPercent`, `Tier2UrgencyOneBidPercent` / `Tier2UrgencyManyBidPercent`, `Tier3EarlyRushOneBidPercent`, `Tier3NormalOneBidPercent`, `Tier3UrgencyOneBidPercent`. A multi-slot bot rolls the **sum of its two lowest** slot probabilities. **The tables are mirrored below for reading; the declarations win.**
- **Stuck escalation** — a single-slot bot rolls `max(mode rate, slope × (blocks stuck + 1))` (`EscalatedStuckDetail`). The slope per tier is `StuckEscalationBasePercent`, which is **not a table of its own**: tier 2 reads `Tier2EscalationBasePercent`, tier 3 reads its NORMAL one-bid shallow rate, and tiers 4+ read the NORMAL ladder directly — so re-tuning NORMAL silently re-tunes the escalation with it. The ceiling is `MaxTopTierEscalationPercent` inside the NST band (tier ≤ `NstTopTierCount`) and 100% below it; it bounds the **escalation only**, so a mode rate above it still wins. Reset when the bot re-bids or anyone else's bid re-ranks it. Slope ordering is DEBUG-asserted (`AssertEscalationSlopesAreOrdered`).
- **Display parity** — the panel reports the true per-block probability (weighted knapsack DP, 2 decimals, `<0.01%` floor) plus the exclusion reason; player-held slots show no % (the player never rolls the ladder). Position vocabulary is **"tier", never "rank"** — "rank" is reserved for the future casino ranking system.

### The ladder tables, mirrored — a SNAPSHOT, not the source

**Verified against the declarations 2026-08-23; every figure matched.** This mirror exists because a
ladder is unreadable as a list of symbol names, and unreadable is its own failure mode. But it is a
second copy with no compiler watching it, so treat it exactly as Standing Convention 15 says: **if this
disagrees with the code, the code is right and this is stale.** Re-verify before tuning.

| Tier | EARLY RUSH | NORMAL | URGENCY |
|---|---|---|---|
| t2 (1 bid / ≥2) | 21 / 21 | 5 / 3 | 5 / 5 |
| t3 (1 bid; ≥2 ⇒ satisfied) | 13 | 2 | 3 |
| t4 | 34 | 5 | 8 |
| t5 | 55 | 8 | 13 |
| t6 | 89 | 13 | 21 |
| t7 | — | 21 | 34 |
| t8 | — | 34 | 55 |
| t9 | — | 55 | 89 |

Mode thresholds: `EarlyRushSlotThreshold` = 7 occupied slots · `AuctionUrgencyWindowMs` = 7 in-game days ·
`NstTopTierCount` = 3 · `MaxTopTierEscalationPercent` = 34 · `Tier2EscalationBasePercent` = 3 ·
`MaxBidBalanceFraction` = 0.5 · `BotBidReserveStopBtc` = 200 / `BotBidReserveResumeBtc` = 300.

*Escalation slopes are not listed separately because they are not separate: `t3 = Tier3NormalOneBidPercent`
(2), `t2 = Tier2EscalationBasePercent` (3), `t4+` = the NORMAL column above (5, 8, 13, 21, 34, 55). That
is what produces the documented `t3 < t2 < t4 < t5 …` ordering the DEBUG assertion checks.*

### Note on the affordability cap

The per-pool filter tests `required + fee > spendable × MaxBidBalanceFraction` — **the tail is not in the
filter** (the `ExclusionPricedOut` test in `BuildBotPoolOpportunities`, which reads
`requiredAmount + fee > bidBudgetCap`, with no tail term). The familiar
`required + tail + fee ≤ spendable × MaxBidBalanceFraction` form is the *outgoing-amount invariant*,
enforced afterwards by clamping the principal to `cap − fee` and sizing the tail from whatever headroom
is left (the `targetPrincipal` clamp and `headroom` / `tail` sizing in `TryBuildCasinoBotBid`). Both are
true; they are enforced in different places, and only the first one decides whether a pool is biddable at
all.

### Placeholders

The five treasury thresholds (reserve stop/resume, the raise band ends, the half-spendable cap) and
`FreshPoolSeedingWeight` are hardcoded placeholders. Re-tune only after hardware progression (P5) and
maturing dividend inflow change the arithmetic — and, for the escalation slopes, only once the block
pace is verified: the escalation is denominated per *block*, so calibrating against a broken pace would
bake it into the table. See `Documentation/PRIVATE_ROADMAP.md` → "Casino-Bot Treasury Policy".

---

## 2. Amendment history

All entries below are **superseded or explanatory**. The rule above is the only thing to implement against.

- **EB.2 (2026-07-09)** — cumulative-donation-total leaderboard, 100-day first-bid-only window, pool of 10, `NonMinerIntroIntervalMs` 1-per-~2-days intro. *All four superseded* (D-EB.8 raised the pool to 40; ND.4b replaced the model; the intro curve replaced the interval).
- **ND.4b/ND.4c (2026-07-10)** — introduced the real ascending auction: flat `MinBidBtc = 0.1` opening floor, `leadingBid + max(0.1, 10%)` raise band, 20-day rolling window, soonest-to-expire targeting, random additive tail (D-ND4b.11), same-block collisions by amount-then-chain-order. *Opening floor superseded by ND.10e(1); band by ND.10e(2) — note the `max(…)` **shape** survived, only the constants moved; targeting by ND.6 then ND.10c.*
- **ND.4d (2026-07-10)** — made the raise floor asymmetric: player +1 satoshi, bots the band. **Still current.**
- **ND.5 (2026-07-10)** — Auction Settlement: SC cashback to every tracked donor at the closing date's price + BTC sweep to the casino. *Superseded at ND.8b.2 (D-ND8.14) — resolution founds the company instead. Only the tracked-pool mechanics and the once-per-resolution trigger survive.*
- **2026-07-11 (interim)** — top-5 hard filter on bot targets. *Structurally stalled all bot bidding; deleted at ND.6.*
- **ND.6 (2026-07-12)** — Saturation Ladder: spread-wide-first ordering (ascending own-slot count, ties soonest-to-expire), first-affordable-is-the-target walk, half-spendable cap, one roll at the best tier, flat "top-3 = satisfied". *Ordering and walk superseded by ND.10c(2); satisfied band by ND.8d. Cap and single-roll shape survive.*
- **ND.6d (2026-07-14)** — split the ladder into NORMAL / EARLY RUSH by slot count; removed the unreachable tier-10 89% entry. **Still current.**
- **ND.6e (2026-07-15)** — URGENCY mode: final 7 in-game days shift every tier one Fibonacci level up. Early-rush pools ignore it (their table is already steeper). **Still current.**
- **ND.8d (2026-07-20)** — bid-count-aware satisfaction (tier 1 always / tier 2 never / tier 3 at ≥2), two-lowest-probability sum, retired `SatisfiedTopTierCount`, last-bid preservation (D-ND8d.6), stale-bid cancellation + cash-back (D-ND8d.7), player bid-safety warnings. **Still current.**
- **ND.8d round 3 (2026-07-21)** — stuck-single-bidder escalation for tiers 4–9; the first cut REPLACED the mode rate and was corrected the same day to `max()` (the DeepBit / `non_miner_7` regression); label parity via `PeekStuckEscalationProbabilityPercent`; `_stuckBidderSignatures` edge-triggered, in-memory. *Tier gate widened at ND.10c(3); `ComputeStuckEscalationProbabilityPercent` retired there.*
- **ND.10a (2026-07-22)** — Fix B: `SweepStuckBidderSignatures` refreshes every (pool × bot) each block, independent of selection. **Still current.** Fix A: escalation queue-jump. *Deleted at ND.10c(4).*
- **ND.10c (2026-07-23)** — the bid-opportunity rework: eligible-bots-only draw (supersedes D-ND6.1), parallel per-pool rolls with a uniform tie-break (supersedes D-ND6.6/6.8), escalation extended to tiers 2–3, shared `BuildBotPoolOpportunities`, panel switched to a true per-block probability at 2 decimals. *Uniform tie-break superseded by ND.10l; the rest current.*
- **ND.10d (2026-07-23)** — zero-truth audit: one shared exclusion vocabulary (`satisfied`/`guard`/`priced out`) across roll, label and panel; `<0.01%` floor so a real-but-tiny chance never rounds back to a bare zero. **Still current.**
- **ND.10e (2026-07-23)** — bot treasury sustainability: (1) price-anchored opening bid, (2) raise band 10–20% → 5–10%, (3) BTC reserve guard 200/300 as a fourth exclusion outranking every per-pool rule, (4) dividend auto-claims batched at 10× the network fee. **(1)–(3) still current**; (4) was *retired at Step 16 P16.1a* — correct but insufficient, replaced by quarter-close batching.
- **ND.10i (2026-07-27)** — escalation slope collision: `Tier2EscalationBasePercent = 3` breaks the accidental tier-2/tier-4 tie; `MaxTopTierEscalationPercent = 34` caps the NST band (anti-leapfrog); `AssertEscalationSlopesAreOrdered` + the escalation's composition shown in the label. **Still current.**
- **ND.10j (2026-07-28)** — cold-start fixes: escalation seeded from the chain (`SeedStuckSinceBlockIndex`), reserve guard moved into the pure `IsBotRestingOnReserve` predicate and hoisted out of the pool loop, `poolRolls` trace column + stale-schema `.csv.old` rotation. **Still current.** Slopes explicitly NOT retuned — the block pace was broken at the time.
- **ND.10k (2026-07-28)** — one bid per donor per block (highest wins, `ignoredBidSeqs`, no refund); a fourth non-blocking wallet warning reading `PendingTransactions` directly, because the mempool is a state the player cannot see. Applied retroactively. **Still current.**
- **ND.10l (2026-07-28)** — weighted tie-break replacing ND.10c's uniform draw; `FreshPoolSeedingWeight = 34` (a sentinel's replacement, not a probability); panel DP switched to a 0/1-knapsack convolution, verified against brute-force subset enumeration. **Still current.**
- **Step 16 P16.6 (2026-07-31)** — the reserve guard is a hysteresis and was resolving as "not resting" after every restart (`bot_4` led six auctions it should never have entered); now chain-seeded, announced per launch, DEBUG-tripwired at the bid broadcast. Emergent consequence, recorded not changed: bots are born at 0 BTC ⇒ born resting, so only the 300 gates a fresh bot (~6 mined blocks before its first bid). **Still current.**

### Standing invariants across the history

Automated non-qualifying transfers (cast sell-flow, non-miner exchanges, entry-bootstrap seed funding)
are economy that funds wallets without starting, leading or winning auctions · never-bid-on companies
stay recruitable indefinitely · every resolved auction has a winner · **a win is permanent**
(D-ND4b.12), even where replaying old history under the current rule would legitimately pick a
different leader for a *still-open* auction · promoting cast miners to casino-player status is
deferred, not scheduled.

### References

`AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §5.2–5.3, §6, §7, §8, §12.5.5,
§14.2–14.13 · `Documentation/ProjectDesignManual.md` Ch. 22 §22.6–22.10, §22.14–22.20 · telemetry
`user://logs/casino_bot_bid_trace.csv`.
