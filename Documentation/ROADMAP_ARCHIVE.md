# Roadmap Archive - GamblingMiner

**What this is.** The finished and superseded entries of `Documentation/PRIVATE_ROADMAP.md`, moved here
**verbatim, under their original headings,** by mini-plan 24 (2026-10-12), so the roadmap holds only what is still
open. Nothing here is a current instruction. Where an entry and the code disagree, the code wins, then
`IMPLEMENTATION_STATUS.md`, then the plan the entry names. Section numbers inside the text (§5, §6, §8) refer to the
roadmap they were written in.

**Order:** as they stood in the roadmap. They cover:
- §5's old header note and the 2026-08-21 working order;
- PH, P3, P4, P10, the fee-market research record, P15.11;
- every mini-plan 08–23 entry;
- the Calendar entry date and bot seed phrase entries;
- the original §6 checklist in full;
- T1–T3, and T4's finished parts.

---

## From §5 — the header note and the working order of 2026-08-21

> **Authoritative implementation order**: `AIHelperFiles/IMPLEMENTATION_ROADMAP.md`. The priorities below are the *feature* breakdown; the roadmap file holds the *sequencing and dependencies*.
>
> **State (2026-06-30):** P0–P2 + the candidate engine (P4/Step 4), difficulty regulator + hardware pools (Step 6), **founder economics (Step 7)**, **Step 8 — UTXO realism / address non-reuse**, and **P10 — network fee activation** are all done & verified in-engine. The game starts on **21 Mar 2009** on a Satoshi/Hal-mined chain; founders mine concurrently in the player era; the chain runs a real multi-input/multi-output UTXO model; the whole network is fee-free before 2009-04-26 and all participants pay fees after (see `AIHelperFiles/step10-network-fee-activation-plan.md` + P10 in §5). **The active next work is Step 9 — economy/meta (P6–P8)**; carried-forward deferrals: bots multi-address (OQ-8.2), deposit-address rotation (OQ-8.3), the optional Patoshi forensic view (8.5).

### ▶ NEXT — the agreed working order (2026-08-21)

Three plans are specified and unstarted. **This is the sequence and the reason for it**, decided with the
developer after measuring the world rather than assuming it.

| # | Plan | Branch | Carries a wipe? |
|---|---|---|---|
| **1** | **Mini-plan 06** — prove INC-003's root cause deliberately | `repro/explorer-clock-rewind` | **Yes** — and the wipe is the instrument, not a cost |
| **2** | **Step 17** — direct explorer access + the event-driven audit | `explorer-access-and-event-driven-audit` | No |
| **3** | Step 17's suspended halves — Betting Statistics (17.B) and T4 (17.D) | — | 17.B eventually does |

**Why mini-plan 06 goes first, against the intuition that a wipe should be delayed.** The world was
measured before deciding: **210 blocks, game date 2009-05-27, zero companies founded, Market Birth 416
in-game days away.** There is nothing accumulated to lose — a fresh world starts at 2009-03-21, about
twenty real minutes of play behind. **The wipe is cheap, so the argument for delaying it disappears**, and
what it buys is real: INC-003 closed, the contaminated journal disposed of instead of quietly skewing every
lifetime figure, and a virgin world in which a reproduced band admits no argument.

> The first framing of this choice was a false dilemma — *wipe now and lose a rich world, or test Step 17
> on a world you will wipe.* **Both horns rested on "rich", and one measurement removed them both.**
> Check the premise before weighing the trade-off it implies.

**⚠ And Step 17 carries a constraint discovered in the same measurement** (`step17-…-plan.md` §5.1a): four
of the event-driven audit's targets — `AuctioningCompanyDetails`, `CompanyDetails`, `CompaniesWallets`,
`CasinoCoinSwaps` — depend on companies or a market, and **both begin at Market Birth**. They are §39.16
rule 10 **SUSPENDED**, precondition named: *the world reaching 2010-07-18*. Migrating them blind is
allowed; declaring them done is not. Without that split, 17.C would sign off with a quarter of its
migrations never once executed — the same failure this project documented twice in one week (mini-plan
04's emit budget, shipped without ever running; Ch. 38's catalogue, a month stale while reading as
current).

**Also carried, and not blocking:** the `user://` archive holding INC-003's evidence lives at
`%APPDATA%\Godot\app_userdata\GamblingMiner_INC003_evidence_2026-08-20\` — 88 files, 55 MB, verified. The
world reset deletes the live journal, so that archive is the incident's only surviving evidence.


### PH - Historical Foundation — ✅ BASELINE REACHED

Goal: establish the historically faithful opening and the network-growth init model **before** economy systems expand on top of it.

- [x] Founders Satoshi & Hal as mining nodes with seed phrases; `FoundersWallets` dev scene (room for Mike Hearn).
- [x] Fix genesis & early coinbase recipients to derived `gm1q…` addresses. *(InputData 100-byte cap still deferred.)*
- [x] Block-candidate + hashrate model **(minimal weighted lottery)** — the keystone seed for P4.
- [x] First-launch bootstrap pre-mine to `21 March 2009` (Satoshi dominant, Hal exactly 3). **Verified in-engine.**
- [x] **DONE (Step 7, on the candidate engine):** Satoshi `11,000`-BTC dynamic ramp (retire ≥ `2011-04-26`); the `12 Jan` 10 BTC Satoshi→Hal tx; April Mike Hearn 32.51 round-trip (+82.51). Built as **regulated concurrent mining** (`FoundersMiningService` + `HistoricalEventScheduler`); Hal `P=1.0` fades to 0 by `9 Aug 2009`. Verified in-engine. See `AIHelperFiles/step7-historical-character-economics-plan.md`.
- Companion research: `historical-blockchain-events-research.md` (address-reuse research → resolved in Step 8 §6).

**Baseline reached:** a new game starts on `21 March 2009` with a Satoshi/Hal-mined chain and the player bets from there. ➡ **Steps 7 (founder economics) + 8 (UTXO realism / address non-reuse) complete; next: Step 9 (economy/meta, P6–P8).**

### P3 - Bot Wallets, Transactions, And Mempool

Goal: make bot mining and block contents meaningful.

- Create BTC wallet addresses for:
  - mining bots,
  - non-mining bot participants,
  - the casino,
  - the player.
- Generate scheduled transactions between wallets.
- Start BTC circulation around block 4 or 5.
- Build a public mempool shared by mining nodes.
- Limit Basic Mode blocks to `24 transactions` for now.
- Include transaction fees in block rewards.
- Let bots mine competing blocks using their own candidate blocks.

**Re-alignment with PH**: after the historical foundation, miner bots are **introduced gradually after player start**, not present at block 1. The scheduled-transaction circulation trigger (`AIHelperFiles/scheduled-bot-transactions-plan.md`) must key off bot introduction rather than an absolute block index, and no-op while only founders are mining.

Done when blocks contain believable transactions and bots can win blocks before the player.

### P4 - Block Template Builder  ✅ DONE (the per-node candidate block model)

> Implemented as `AIHelperFiles/candidate-block-model-plan.md` (roadmap Step 4 — slices 4a/4b.1/4b.2/4b.3/4c). Each node builds its own candidate block from its mempool view: fee-ordered tx selection (24-tx cap incl. coinbase), Merkle root, coinbase = reward + collected fees, real block-header hashing, content-hash txids, and coinbase maturity N=1. BlockExplorer surfaces it; BTCWallet has a player fee selector. The bootstrap/lottery already mine through this engine (the refactor was in-place), and the founder economics layered straight on top in **Step 7 (✅ done)**. **Next lead: Step 8 — UTXO realism / Patoshi per-receive addresses.**

Goal: simulate Bitcoin-like block assembly without full-node complexity.

- Select transactions from the public mempool.
- Use a simplified ancestor-feerate ordering model.
- Tie-break equal fee rates by mempool age.
- Build a coinbase transaction with block reward plus included fees.
- Compute a Merkle root from the final transaction order.
- Keep room for future private mempool/fee-market behavior.

Done when candidate blocks differ by miner and transaction selection matters.

### P10 - Network Fee Activation (~2009-04-26) ✅ DONE (2026-06-30) — flat era RETIRED by Step 14 ND.7 (2026-07-13)

> **Superseded note (ND.7)**: the flat 2009-04-26 / 0.1-BTC fee era this priority built was replaced by the **Historical Fee Replay** — the fee era now begins at Market Birth (2010-07-18) and replays the real daily median/mean band from the network dataset. P10's *plumbing* (fee rows, per-tx `Fee = Σin − Σout`, coinbase collection, the four send panels) is exactly what the replay feeds. See the "Network Fee Market Simulation" entry below and `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10.

Goal: make the simulated network historically faithful to early Bitcoin's **fee-free era**, then switch the whole network to paying fees on one date — resolving the current dev-time contradiction (scripted historical txs are fee-free while bots/casino attach fees from block 1).

- **The whole network is fee-free until a `FeeActivationDate` ≈ 2009-04-26** (the nearest mined block, just after the 18 Apr Hearn round-trip). On/after that block, **every** participant (player, bots, casino, founders) begins paying fees.
- Gate points to flip to 0 before the date, restore after: the bot fee in `NetworkRoot.ScheduleBotTransactionsAfterBlock` (`MinBotFeeBtc`/`MaxBotFeeBtc`), `CasinoTxFee`, and the player's default/selected fee. The candidate-block fee-collection engine is unchanged (it already collects `ΣFee`); this only gates whether a fee is *attached*.
- Provisional date `2009-04-26`; resolve to the nearest block by timestamp (dates are the source of truth — Q-E1).
- **Own branch** (e.g. `network-fee-activation`); does not block other Basic Mode work.
- Design: `AIHelperFiles/step8-utxo-realism-plan.md` OQ-8.7 + `IMPLEMENTATION_ROADMAP.md` ("What's next"). Tracked in the §6 checklist.

Done when no fee is attached by any participant before the activation block, and all participants attach fees on/after it, validated in-engine across the April 2009 boundary.

**Delivered (2026-06-30):** `NetworkFeePolicy` static class (single source of truth: `ActivationDateLocal = 2009-04-26`, `DefaultFee/MinFee/MaxFee`); fee row hidden before the date, default 0.1 BTC after, clamp 0.1–1.0 on send, across all four BTC wallet send panels (BTCWallet, FoundersWallets, CasinoFinances, BotsBtcWallets); sender balance label on every send panel; "Go Back" button rename; backend gates for bot automated fees and casino pool-payout fees (`NetworkFeePolicy.IsActiveByTimestamp`). Also delivered in the same phase: **casino pool distribution atomicity fix** (one multi-output tx per event — eliminates partial/double-payment bug); **Block Explorer full multi-output tx display** (full `Inputs[]`/`Outputs[]` iteration, all txs in a block shown); **OQ-8.2 cosmetic filter** (`IsSelfChangeTransaction` + `ExternalOutputs` in `BlockExplorer.cs` — hides bot change-to-self from display until bots have `DerivedAddressWallet`). Full detail: `AIHelperFiles/step10-network-fee-activation-plan.md`.

### Network Fee Market Simulation — Research Priority (not yet scheduled)

Goal: replace the single hardcoded `NetworkFeePolicy.MinFee` (flat `0.1 BTC` since the P10 activation date, unchanged for the rest of the simulated timeline) with a real model of how the network fee should move over time. Flagged 2026-07-08 while building the casino swap desk's fee-deviation cap (Step 13, D-SW.12) — every fee-dependent system built on this constant (the swap desk's whole model, the minimum swap size, every BTC wallet send panel) inherits its historical naivety, and none of it would need code changes to consume a non-constant fee, only a decision on HOW to derive one. **This is a dedicated research/design round, not an implementation task yet — do not schedule build work on it until the round below has run.**

Two candidate approaches, framed as research options to investigate, compare, and decide between (not mutually exclusive):

- **Option A — historical fee replay.** Mirror `BtcMarketDataService`'s existing architecture (a curated historical dataset replayed as a step function keyed to game-time) but for real average Bitcoin transaction fees instead of BTC/USD price. Pro: historically faithful, reuses a proven pattern, easy to cite/validate against a real source. Con: real historical fee data is far less standardized than price data (especially the pre-2012 near-zero-fee years), and it's a pure replay with **zero connection to anything happening inside our own simulated chain** — the player's or bots' own transaction volume has no influence on the fee they pay.
- **Option B — reactive fee market from the simulation's own state.** Derive the fee dynamically from OUR simulated chain's own congestion: mempool depth vs. the block's transaction cap, the size of the miner/bot population, total transaction volume — a basic fee-per-byte auction, similar in spirit to real Bitcoin's actual fee-market mechanics. **The deciding point in its favor**: this project's own miner/bot population and transaction volume are **already planned to grow following real Bitcoin history** (gradual bot introduction, the referral system, mining pools scaling over time — see PH/P3/P5 above) — so a congestion-reactive fee model would **automatically and indirectly track real historical fee trends**, as a side effect of infrastructure already committed to elsewhere, with no separate historical-fee dataset to source. It also gives the player genuine agency (their own activity can move the fee market they experience), and slots directly into the placeholder P4 already left for it ("Keep room for future private mempool/fee-market behavior," `candidate-block-model-plan.md`). Con: needs real design work that doesn't exist yet (a congestion→fee formula, its tuning/elasticity, an actual auction inside the mempool/candidate-block engine) and is harder to validate against a citable real-world number.

**Working hypothesis for the eventual research round** (to confirm or overturn when it actually runs, not a decision yet): Option B looks like the better structural fit — it inherits historical shape for free from already-planned population/volume growth instead of requiring an independently-sourced second historical dataset, and it already has a natural home in P4's mempool/fee-market placeholder. Option A may still be valuable as a **calibration reference** for tuning B's emergent curve against real history, rather than as a standalone replay mechanism — a hybrid worth evaluating explicitly. Full context and the swap desk's dependency on this: `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` §3.4.

**✅ DECISION RECORDED (2026-07-07, user — Step 14 round 1, D-14.6, superseding the working hypothesis above): Option A is the chosen mechanism; Option B is retained as a future VALIDATION experiment, not the mechanism.** Rationale: (1) the fee dataset turned out to be zero-marginal-cost — Step 14's network-data pipeline (Coin Metrics, verified alive with genesis-complete daily fee metrics) fetches the fee columns alongside the tx/hashrate series it needs anyway, dissolving Option A's "second dataset to source" con; (2) fees, like `price_usd`, are decreed **fractal-exempt** (never /100-scaled), so the replay applies at face value with no scaling design; (3) the hypothesis's best argument for B — "population/volume growth will track history anyway" — is inverted into B's new role: once the Step-14 scheduler replays historical population/volume, building B later and comparing its emergent fee curve against A's replayed truth becomes a **cross-validation of the whole network simulation** ("if B lands near A, the rest was built right"). Data acquisition: Step 14 ND.0; the `NetworkFeePolicy` replay implementation remains its own future step (step14 plan §9.4). This item stays open only for that implementation + the eventual B experiment.

**✅ OPTION A IMPLEMENTED (2026-07-13, Step 14 ND.7)**: the Historical Fee Replay is live — fee era gated at Market Birth (2010-07-18), daily median/mean/max band replayed via `NetworkFeePolicy`'s pushed schedule, cast sell-flow on the mean and every other participant on the median, `WorldFormatVersion` 3. One dataset caveat recorded at ND.7.0: Coin Metrics' daily median (`FeeMedNtv`) is paid-tier, so the `fee_median_btc` column ships from a developer-approved hybrid (Blockchair true medians for 2010-07 → 2011-04, BitInfoCharts USD median ÷ price after, spot-checked within 0.2%). **This item now stays open ONLY for the Option-B congestion experiment (OQ-ND7.1)** — the layer that will finally give participants a reason to pay ABOVE the daily base (queue-jumping at the 24-tx cap), and the cross-validation of the population/volume simulation.


### Persistence Durability & History Retention (P15.11) — SCHEDULED, blocks the rest of P15.8

**Status: scheduled and blocking (2026-07-29).** Spec: `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` **P15.11**. Forensics: `Documentation/INCIDENT_LOG.md` **INC-001**. Design statement: `ProjectDesignManual.md` **Chapter 40**.

**What ships:** atomic snapshot writes (`.tmp` → rename) with a loader that fails loudly and a writer guarded against ever persisting over a failed load (D-15.26); the journal-rebuild path made to obey the same rotation/cleanup invariants as the incremental writer (D-15.27); a retention cap that bounds the boot load by construction (D-15.28/29); and the removal of the write-only `blocks-*.json` mirror (D-15.30). The crashed world is repaired rather than reset (D-15.31) — the bet history is deleted under explicit authorization, since it is stats-only and was already inflated by duplication.

**Deliberately deferred, recorded so it is a decision and not an oversight:** a **persisted lifetime stats aggregate** (so totals stop meaning "over retained history"), and making the world snapshot **incremental** rather than a full 9.25 MB rewrite whose cost grows linearly with chain length. Both become worth doing when a run goes materially past ~1,666 blocks; revisit alongside P5 (hardware progression), which changes how fast blocks accumulate.

### Bet timestamp fidelity + the engine's real throughput ceiling — ✅ DONE (mini-plan 08, merged 2026-09-15)

> **This entry is history.** Both objectives were met; the figures below are the ones known on 2026-08-30 and
> were superseded — `MaxBetsPerFrame` ended at 40, the measured ceiling is ~2,000 bets/s at 99 credits, and
> the run also produced INC-005. Current record: the plan's close-out **§4.9**.

**Status: `AIHelperFiles/mini08-timestamp-fidelity-and-throughput-limits-plan.md`, own branch off `main`.**
Found by mini-plan 06 §9.10c while investigating something else.

**The defect.** Every bet settled in one frame carries the **same** timestamp, because the clock advances
once per frame and `SimulationService` reads it per bet. Measured on a real journal: 7,926 bets across
**949 distinct timestamps**, groups of 7–10, spaced 150.00 game-seconds — `9000 / 60`, the clock's stride at
9000X. The records are permanent, so every consumer that reads a timestamp inherits it: mini-plan 05's
balance-line separation, INC-002's streak metrics, `MaxAppendRowsPerFrame`'s calibration. **`DevTimeScale`
claims in-game invariance; timestamp resolution is where that claim silently fails.**

**The fix** back-dates the k-th of n bets in a frame by `(n−1−k) × interval × SpeedMultiplier` game-seconds,
which reproduces the spacing the engine actually simulated and leaves the last bet on the clock's exact
value — preserving the canonical clock-equals-last-event rule with no special case.

**The limit question the developer wants answered: 99 credits × 9000X.** Demand is `credits × DevTimeScale`
= **8,910 bets/s**; supply is `MaxBetsPerFrame × fps`. **Timestamp precision is NOT a constraint**: spacing
at 99 credits is 1.01 game-seconds against `DateTime`'s 100 ns.

**✅ ANSWERED, 2026-08-30 (mini-plan 08 P1).** A bet was priced for the first time and cost **1,414 µs** —
so `MaxBetsPerFrame = 10` was not the loose guess this entry assumed, it was at ~78% of what a frame could
physically deliver. **93% of that bet was two calls doing per-bet work that was not per-bet work**:
`BankrollStateService.SetBalance` writing `bankroll_state.json` synchronously on every bet (934 µs, and its
output is discarded at every boot), and `DiceGame.OnSimBetSettled` rebuilding the blockchain status line up
to `MaxBetsPerFrame` times per frame with only the last drawn (383 µs). Both fixed; **a bet now costs
296 µs, and `MaxBetsPerFrame` is 20 — the first time that number has been a measurement.**

Where that leaves the target: **99 credits × 600X is comfortable** (9.9 bets/frame, 2.93 ms, 18% of a
16.67 ms frame). 99 × 9000X remains out of reach — 148 bets/frame would need ~44 ms — but by ~3×, not the
15× this entry recorded. Remaining named cost: the bet-history UI containers at 216 µs/bet, 73% of what a
bet now costs. Full record: `AIHelperFiles/mini08-timestamp-fidelity-and-throughput-limits-plan.md` §4.

### DevTimeScale ceiling + credit-yielding governor — ✅ DONE (mini-plan 09, 2026-09-18)

> Both halves shipped, and 9000X became real down to 2 credits. The budget it set — `BetBudgetPerSecond =
> 1,700`, 99 credits → 1700X — was **superseded by mini-plan 10** (9,000, the clock's demand at the hardware
> cap) once DiceGame's bet list stopped costing 0.198 ms per bet. The plan's close-out is §8; the open items
> are listed there and in `IMPLEMENTATION_STATUS.md`. The text below is the original specification.

**Status: `AIHelperFiles/mini09-devtimescale-ceiling-and-credit-governor-plan.md`, own branch off `main`.**
Picked up from mini-plan 08's close-out, at the developer's request.

**Two halves, in order.** (A) Find the absolute limit: time the whole frame, remove R2-C1's 0.620%
saturated-backlog overspend, and sweep the bets/s capacity and the game-time ceiling as two separate axes.
(B) A governor: the developer requests a scale, the game runs at
`min(requested, BetBudgetPerSecond ÷ Σ running credits, ceiling)`. Buying credits mid-autobet lowers it step
by step, discarding raises it back, and 100X is never transformed. A readout always shows requested vs
effective. The budget constant is phase A's result, so B cannot be built first.

**Basic Mode refinement option (the developer's decision, 2026-09-18): an adaptive budget.** The shipped
governor uses a fixed `BetBudgetPerSecond`, sized for the slowest session measured. That leaves a fast
session's speed unused and is specific to one machine and one era. Following live delivery would recover that
speed, but it oscillates and moves the scale for reasons the player cannot see. The measurements to start from
are in the plan's D-09.6 entry.

### DiceGame's per-bet UI cost + R2-C1's overspend under the governor — ✅ DONE (mini-plan 10, 2026-09-20)

> Both parts closed. **(A)** The bet list was 80% of the cost of a bet and now costs 0.025 ms, the same as
> drawing nothing: rows never move and only the ~14 visible ones are painted, once per frame. 99 credits run
> at the clock's ceiling, 9000X, against 1700X before — `DefaultMaxBetsPerFrame` 40 → 160 and
> `BetBudgetPerSecond` 1,700 → 9,000, the latter **derived** as the clock's demand at the hardware cap rather
> than measured. **Both superseded by mini-plan 11 (2026-09-22): 180 and 44,000.** A player-facing Bet View button (Detailed / OFF) shows or hides the list; once the list got
> cheap it became a view preference rather than a speed setting.
> **(B)** The overspend is **0.000000% at full retention** and rises only with saturation, so P2 closed by its
> pre-registered rule. **Withdrawn, as an option for a later version:** the Numbers view, see "Post-Basic
> Mode v1 — Checklist". The text below is the original specification.

**Status: `AIHelperFiles/mini10-dicegame-per-bet-ui-cost-and-clock-overspend-plan.md`, own branch off `main`.**
Mini-plan 09's two remaining open items, at the developer's request.

- **(A) The per-bet UI cost.** DiceGame pays ~4× more per bet than a scene without its bet lists. Both lists
  (`BetHistoryContainer`, `PreviousWinnerNumbersGrid`) reorder 100 rows on every bet. The plan attributes the
  cost with a mid-run DEBUG switch, then updates the lists once per frame from a ring buffer, and re-derives
  `MaxBetsPerFrame` and `BetBudgetPerSecond` from new measurements, the budget across two sessions.
- **(B) R2-C1's overspend.** Measured directly by `FrameCostProfiler`, under the governor and in one forced-
  saturation leg. Closed for good below 0.05%; otherwise fixed with the lag-free design D-09.2 described.

### The frame's real capacity, bots betting alongside, and later eras — ✅ DONE (mini-plan 11, 2026-09-22)

> **All three answered, and a fourth found.** **(A)** The player and all four bots at the hardware cap —
> 44,545 bets/s, the most the game can demand — ran at 56–57 fps delivering 99.2–99.5%, so the frame's limit was
> never reached. `BetBudgetPerSecond` 9,000 → **44,000**, measured again rather than derived (D-11.1), and
> `DefaultMaxBetsPerFrame` 160 → **180** (D-11.2), because the per-engine cap bound before the frame did.
> **(B)** A bot bet costs **0.0062 ms against the player's 0.0286** — 22%, not the ±30% predicted. The gap was
> player-only work, 41% of it one call copying every transfer record twice per bet; now 0.40 µs, and a player bet
> **15.7 µs** (D-11.3). One budget still covers every engine. **(C)** The historical network costs the frame
> **nothing** through Market Birth: 0.54 ms a frame at its peak, 26 attempts against its 5,000 cap. Neither
> candidate fix is needed or built, and the governor's comment now records the era verified. **The fourth:**
> a session's RAM grows without bound with the number of bets — 6.77 GB after 26.4 M, 13 of 65 minutes frozen —
> which is **mini-plan 12**, below. The text that follows is the original specification.

**Status: `AIHelperFiles/mini11-frame-capacity-bots-and-later-eras-plan.md`, own branch off `main`.** The three
limits mini-plan 10 stated about its own result, at the developer's request.

- **(A) The frame's capacity.** Never observed: the clock's ceiling binds first with the player alone. Bots
  provide demand the clock does not cap, so they are how it gets measured. It also corrects D-10.4's claim that
  the budget "never binds for the hardware that exists": that holds for the player alone. With all four bots
  running, the governor divides the budget across five engines.
- **(B) A bot bet's cost.** Different path from the player's (ledger, per-client book, event), never priced.
- **(C) Later eras.** The historical network hashes alongside the player, capped per frame, and the governor
  leaves it out on the strength of a 2009 measurement. A long unattended run past Market Birth, with the trace
  reading cost against the game date. Two candidate fixes are named, not chosen: a network term in the governor,
  or one probability draw instead of *k* hashes for the invisible mass. It advances the world permanently, which
  the developer accepted (no archive).

### The journal's memory — bounding a store capped only on disk — ✅ DONE (mini-plan 12, 2026-09-28)

> **Fixed, and the instrument that missed it can now see it.** The live set is bounded by the same window the
> files keep, so a 41.1 M-bet run held **1.51 GB instead of 6.77**, with **zero stalls** against 791 s of frozen
> game, and cost per bet flat across the whole run (16.9 → 16.7 µs, against C2's 16 → 145). `FrameCostProfiler`
> counts the periods it refuses to call frames instead of discarding them. The restore path was verified on a
> real restart. **D-12.1** then made a journal line flat, short-keyed and satoshi-integer — 285 → **163 bytes**,
> the append 5.09 → **4.20 µs**, a bet **16.07 µs** — verified by 183,055 records with zero balance-continuity
> breaks. **D-12.2 rejected binary on the measurement:** 43% fewer bytes bought 0.89 µs, so binary's further
> ~60% is worth ~1 µs of a 16 µs bet and would end the `node`/`awk` audits. The remaining cost is the 32-char
> GUID id's hash and the appends, which is **mini-plan 13's** lever, not a format's. Text below is the
> original specification.

**Status: `AIHelperFiles/mini12-journal-memory-bound-plan.md`, own branch off `main`.** Found by mini-plan 11's
C2 run while it was measuring something else; evidence and lessons in `ProjectDesignManual.md` §40.11.

`BetHistoryRepository` caps the journal on disk (20 segments, ~57 MB) and never trims the same records in
memory. A session therefore grows with every bet: **6.77 GB after 26.4 M bets on a 7.9 GB machine**, 13 of a
65-minute run spent frozen, cost per bet 16 µs → 145 µs. The limit is in **events, not hours** — at 99 credits
and normal speed it is ~74 hours of continuous play.

- **(A) The boundary.** Trim the live set to the window the disk already keeps, and re-read every consumer
  against that: the explorer and the stats rebuild already load from disk on demand, the duplicate guard's
  promise narrows to the window, and `RollbackToUtc` rewrites the files from memory, so the restore boundary
  moves and must be verified.
- **(B) The instrument.** `FrameCostProfiler` discards any period over a second as "not a frame", which is how
  791 s of frozen game left healthy percentiles. Count the stalls instead of dropping them.
- **(C) The compact record**, decided by one measurement rather than preference: split `RegisterBet` into
  serialization and bookkeeping. The rule is registered in the plan; a bounded store needs a small record far
  less than an unbounded one did.

### What a block costs, and the last field in a bet — ✅ DONE (mini-plan 13, 2026-09-29)

> **T4.6 is answered, and it selects T4.5.** A block costs **27.55 ms** at height ~400, of which **66.7% writes
> the world snapshot TWICE** — exactly 2 writes in 51 of 51 blocks, 18.39 ms, 503 KB each. Two phases split the
> cost (Snapshot 10.72, Checkpoint 12.33), which is why it only appeared once the writes were *counted*. **The
> UTXO replay is 7.4%** — 8.7 distinct nodes rebuild per block and the per-rebuild cost grew 4.4× while the
> chain grew 2.5×, so T4.1/T4.2 are real, structural and not yet the priority. **The first move inside T4.5 is
> smaller than T4.5 itself: stop writing the chain twice.** The second write is the only path committing the
> post-bet financial mirrors (the bots' included), so it cannot be deleted — separating the immutable chain from
> the mutable state is what makes one write enough.
>
> **B shipped too (D-13.2):** the bet id is a per-world sequence rather than a 32-character Guid — a bet
> **16.07 → 12.63 µs**, a journal line **162.7 → 134.4 bytes**, the rewind exercised by 2,729 ids issued after a
> restart with zero duplicates over 193,108 records. Across mini-plans 12–13 a bet went **28.5 → 12.63 µs** and
> a line **285.3 → 134.4 bytes**, leaving the real proof-of-work hash as the largest single item in a bet.
> Text below is the original specification.

**Status: `AIHelperFiles/mini13-block-cost-budget-and-bet-id-plan.md`, own branch off `main`.** **This is T4.6**,
which T4 itself says to do first because T4.1, T4.2, T4.3 and T4.5 are all gated on it.

- **(A) The per-block budget.** Mini-plan 11 measured the total only (24 ms at height 376, 33 ms at 747, growing
  with the chain). This decomposes it: broadcast, bot transactions, auctions, governance, historical events, the
  snapshot write, the checkpoint, the five trace appends — and **`GetUtxoSet()`'s replay, counted as well as
  timed**, since it rebuilds from genesis on every chain change and ~62 nodes each hold their own. The decision
  rules name which of T4.1 / T4.2 / T4.5 comes next, by share; the plan builds none of them.
- **(B) The bet's last field.** `BetRecord.Id` is a 32-character GUID whose only reader is the journal's INC-002
  duplicate guard. A per-world counter removes a string allocation, replaces a 32-character hash with an 8-byte
  one and drops ~30 bytes a line. Its risk is the rewind: the counter must roll back with the world, and the
  plan verifies that on a real restart rather than arguing it.

### Write the chain once, then stop rewriting it — ✅ DONE (mini-plan 14, 2026-09-29)

> **T4.5 is done, and per-block cost stopped growing with the world.** A block went from **27.55 ms and rising**
> to **~8.4 ms and flat**; the world write went from **two per block to one**, and from **503 KB at height 400
> (growing) to 57.8 KB at every height**. The chain now lives in an appended `chain.jsonl`; `state.json` keeps
> the mutable remainder and a tip stamp naming the chain it belongs to. This also answers mini-plan 13's P4.
>
> **The durability gate found more than the performance work did.** A torn chain file was detected and refused
> correctly — and then the game **fabricated a replacement world**, because an empty in-memory chain is
> indistinguishable from a first launch. Fixed: the bootstrap and the checkpoint both refuse on
> `NetworkRoot.WorldLoadFailed`. Also fixed: an indented-JSON bug that made the first chain file unreadable by
> its own loader, caught by reading the artefact rather than by the run (world format 9 → 10 → 11).
>
> **Next, by §4's own rule:** the UTXO replay is the only cost still growing — 0.97 → 2.37 ms over 300 blocks at
> a constant 8 rebuilds a block, ~28% of a block against the 40% that makes **T4.1** the next plan.
>
> **⚠ SUPERSEDED by mini-plan 15's measurement to height 698, in two places.** (1) The UTXO replay is **not** the
> only cost still growing: it **plateaus at ~4 ms from height 500**. (2) **"~8.4 ms and flat" did not reproduce** —
> every phase measured 1.3–2.3× higher at equal heights on a later day, uniformly, so a per-block cost is a
> distribution rather than a figure. The 40% rule does fire, but on a shrinking denominator. See the mini-plan 15
> entry. Text below is the original specification.

**Status: `AIHelperFiles/mini14-write-the-chain-once-plan.md`, own branch off `main`.** **This is T4.5**,
selected by mini-plan 13's measurement, and it **absorbs the former "Chain persistence" entry**, which described
the same work from a weaker measurement.

Measured at height ~400: a block costs **27.55 ms**, of which **18.39 ms (66.7%) writes the world snapshot
TWICE** — 503 KB each, growing with the chain (213 KB at height 148). Two moves, in order of size:

- **(A) One write per block instead of two.** Pure coalescing, no format change: `PersistStateToDisk` marks the
  world dirty and a single flush runs at most one write per frame, at the top of `SimulationService._Process`
  so it happens whether or not a run is active. The commit still happens per block, milliseconds later — a
  widened durability window, stated rather than discovered.
- **(B) The chain leaves the snapshot.** `chain.jsonl` appended one block per line, never rewritten;
  `state.json` keeps the small mutable remainder; a tip stamp ties them, and a mismatch or a torn last line
  fails **loudly and refuses to persist**, as INC-001 requires. **Its success criterion is that per-block cost
  stops growing with height** — which also closes mini-plan 13's P4, the prediction that ran out of range.
- **(C) The traces**, only if they exceed 15% of a block once A and B land.

**Premise stated on purpose:** append-only assumes no reorgs, which is the current design (forks are
post-Basic-Mode). A future fork plan inherits that premise explicitly rather than discovering it.

### Fail closed everywhere, and readouts that strobe — ✅ DONE (mini-plan 15, 2026-09-30)

> **A session that cannot read its world now writes nothing world-shaped, and the fast readouts stopped
> strobing.** 15 eager writers consult `NetworkRoot.WorldLoadFailed`. Verified on a torn chain while the game
> mined blocks: **zero of 88 world-state files changed**, 12 writers each announced their refusal once, and the
> services restored from the checkpoint *without writing back*. The clock and both nonce counters now repaint on
> a **measured** cadence chosen so the finest displayed field advances ~2 of its own steps — seconds at 100X,
> hours at 9000X — and the developer reports no freeze anywhere.
>
> **Two predictions refuted, and both were unmeasurable rather than merely wrong.** **P3** (a ≥0.5 ms frame
> saving) showed no change: outside-sim is `period − sim`, a **residual containing the vsync wait**, so removing
> CPU work from a vsync-bound frame just makes it wait longer. B's cost benefit is unmeasured, not disproven.
> **The plan's own B.1/B.2 spec was refuted before any code was written** — a fixed 10 Hz sample at 9000X
> advances the clock ~15 minutes per sample and `15 mod 10 = 5`, relocating the freeze to the minutes digit.
>
> **⚠ Two figures from mini-plan 14 are superseded below.** The UTXO replay is **not** "the only cost still
> growing" — it **plateaus at ~4 ms from height 500** (measured to height 698). And "a block costs 8.4 ms and
> flat" **did not reproduce**: at equal heights every phase measured **1.3–2.3× higher** on a later day,
> uniformly across unrelated phases including pure-CPU ones, so the cause is environmental rather than a code
> change — but a per-block cost is a **distribution, not a figure**.
>
> **Next, and it is NOT T4.1 by default.** The 40% rule fires (42.7% at height 674–698) but **on a shrinking
> denominator, not a growing numerator**: non-UTXO block cost swings 5.9 → 15.8 ms on the same world inside one
> run. Restate T4.1's trigger in absolute milliseconds and drop the share. T4.1's prize is a stable ~4 ms.
> **The recommended next plan is the checkpoint + snapshot write path** (checkpoint alone 9.46 ms at height
> 350–449), whose 2.7× variance is what makes every other per-block figure here unmeasurable — fixing it buys
> measurement as well as time. Developer's choice.
>
> This entry **absorbs the former "Fail closed everywhere on a failed world load" open item**, which specified
> part A from mini-plan 14's finding. Text below is the original specification.

**Status: `AIHelperFiles/mini15-fail-closed-and-readouts-that-strobe-plan.md` (close-out §8), merged from branch
`mini15-fail-closed-and-readouts`.** Took the open safety item, plus a UI bug the developer reported
while watching mini-plan 14's runs, plus the measurement that decides whether T4.1 comes next.

- **(A)** Every eager writer consults `NetworkRoot.WorldLoadFailed`, so a session that could not load its world
  writes **nothing world-shaped** — today only three places check it.
- **(B) The clock and the nonce counter freeze a digit at speed, and it is arithmetic.** Both refresh **every
  frame**, and the clock advances `100 × DevTimeScale ÷ fps` game-seconds per frame — at 9000X that is ~150 s,
  and `150 mod 60 = 30`, so the seconds alternate between two values while the units digit sits still. The
  counter gains ~148 per frame, a near-constant step, which does the same to its last digits. The fix is a
  fixed ~10 Hz sample plus a displayed precision that matches it — never an animated counter, which would show
  values the world never had. It also removes a per-frame rebuild of DiceGame's whole mining-status block.
- **(C)** The UTXO replay's share at a taller chain; mini-plan 14's 40% rule then decides whether T4.1 follows.

### Fewer writes per block, and a success criterion that reproduces — ✅ DONE (mini-plan 16, 2026-10-02)

> **The write path lost half its writes, and the project lost a unit of measurement.**
>
> **What shipped.** Every world-state writer is instrumented (`BlockCostProfiler.NoteStateWrite`), and the two
> hottest — `bankroll_state.json` (88 B) and `casino_sc_balance_state.json` (344 B) — moved from a 0.5 s real-time
> flush to the **block commit**: **13.43 → 1.00 writes/block each, 25.99 → 0.93 ms/block, −96%.** Total writes per
> block **48.84 → 23.36 (−52.2%)**, write share of wall-clock **2.01% → 1.56%**. `CasinoScBalanceService` also
> gained the quit flush that `BankrollStateService` got in mini-plan 08 and never received back.
>
> **Only visible because the instrument counted OUTSIDE the block.** Inside the bracket a block performs 2 writes;
> the real figure was 48.8, almost all of it between blocks at bet rate. **P1 (≥20 writes per block) was refuted by
> looking in the wrong window**, and had the counter required an open block the plan would have closed on a
> measurement that was true and useless. **P3 was refuted too:** `calendar_state.json` — 20 call sites, 18 bytes,
> the "emblematic case" — is written **zero** times during a run. A count of call sites is not a rate.
>
> **P2 confirmed, emphatically:** an **88-byte** file costs **0.994 ms** per write while the **7,922-byte**
> checkpoint costs **0.643 ms**. Cost is per-write overhead, not volume. The atomic rollup (974 B, `.tmp` + rename)
> is the dearest small write in the set at **2.18 ms**, exactly as two filesystem operations implies.
>
> **⚠ THE RULE THIS ESTABLISHES, and it governs every T4 item.** Block cost nearly **doubled between the two runs
> (median 10.54 → 19.67 ms) while the plan was removing 26 ms/block of writes** — concentrated in `checkpointMs`
> (5.45 → 13.56), of which only ~2.4 ms is attributable. Within-run spread was **1.19×** (run 1, *before* B1) and
> **1.31×** (run 2), so the **2.7× that justified choosing this plan over T4.1 does not reproduce**: the variance is
> **between sessions, not within them**, and it is large enough to swallow any millisecond-level improvement whole.
> **Per-block millisecond figures do not reproduce across sessions and are not evidence of a code change. State cost
> criteria as COUNTS.** The count predicted 52% and measured 52.2%. Had this plan stated success in milliseconds it
> would have reported a catastrophic regression while removing 96% of what it targeted. This retroactively settles
> mini-plan 14's "a block costs 8.4 ms" and mini-plan 15's 40%-of-a-block T4.1 trigger, both already flagged.
>
> **What remains.** The **bet journal is 94.9% of all remaining write cost** (99.0 of 104.3 ms/block, 19.33 writes,
> 5.12 ms per append) and it is **the record, not waste** — suspended (§6/§11) with its precondition named: a
> measurement of whether that per-append cost is reducible, never an assumption. **C and D dropped by their own
> gates** (every bankroll write carried a new value; the other money services write ~0.01×/block, so there is
> nothing to merge).
>
> Text below is the original specification.

**Status: `AIHelperFiles/mini16-fewer-writes-per-block-plan.md` (close-out §11), merged from branch
`mini16-fewer-writes-per-block`.** Chosen over T4.1 on mini-plan 15's measurement, and the reasoning is recorded
under T4.1 itself: the UTXO replay plateaus at ~4 ms while the write path is both larger (checkpoint **9.46 ms** at
height 350–449) and the reason every other per-block figure here varies 2.7×.

**The premise, measured.** The block path writes ~14 files totalling **~75 KB** and takes **~14 ms** — about
**5 MB/s**, three orders of magnitude below the disk. So the cost is **per-write overhead, not volume**:
`calendar_state.json` is **18 bytes** and `PersistCurrentTime()` has **20 call sites**;
`PrincipalBalanceService.SaveState()` fires from four unthrottled mutation paths. The one service already throttled
is `BankrollStateService` (mini-plan 08 P1, where its unthrottled per-bet write was **66% of a bet**) — **that fix
was applied to one service and never generalized**, which is this whole plan in one sentence. The coalescing
pattern also already exists and is used for exactly one thing: mini-plan 14's `RequestWorldPersist` /
`FlushWorldIfDirty`.

**The plan's own discipline, inherited from mini-plan 15's failures.** Its success criterion is a **write count**,
not a millisecond figure and not a share — a count reproduces or it is a bug, whereas a single per-block cost
demonstrably does not reproduce across days. The instrument (generalizing `BlockCostProfiler.NoteSnapshotWrite`
to every writer `WorldWriteGuard` already marks) ships **before** any fix, and **P5 — that the 2.7× variance falls
with the count — is the prediction that matters for the project**, because it is what would make future per-block
figures trustworthy at all.

**Hard constraint:** coalescing within a frame preserves "a block is the only commit to disk"; deferring past the
block does not. Anything that moves a commit past the end of the frame that mined the block is out of scope
whatever it saves.

### The journal's rotation, and a UTXO set that is updated instead of rebuilt — ✅ DONE (mini-plan 17, 2026-10-06)

> **T4.1 and T4.3 are DONE. The journal is closed as a performance subject. A2 was dropped by its own rule.**
>
> **T4.1 (incremental UTXO):** a newly-appended block's transactions are applied to the cached set instead of
> replaying the chain — called after all three `Chain.Add` + `_chainVersion++` pairs, with `TryReplaceChain`'s
> wholesale swap deliberately left to the replay. **Rebuilds fell from ~8 per block to 0.313**, and
> `utxoMsSincePrev` is **0.000 median** against ~4 ms before. Fail-safe by construction: it advances only when the
> cache was exactly current before the append, so every other state falls back to the replay.
>
> **T4.3 (address → outpoint index):** maintained by that same hook, so `GetSpendableUtxos` walks only the
> requested addresses' outpoints instead of the whole set — `O(owned)`, the follow-on §38.7's R3 fix pointed at.
>
> **The oracle is the reason either shipped.** `DescribeUtxoCacheMismatch` replays the chain and compares entry by
> entry — built **before** either fast path so it could not be shaped to fit a result — driven round-robin at one
> node per 25 blocks. **Silent across 195 blocks and 61 nodes, with an `ARMED` line proving it ran.** The full
> replay is now load-bearing twice (this and `TryReplaceChain`) and must never be deleted.
>
> **⚠ P1 REFUTED, and A2 dropped by the rule registered before the data.** The code reading was exact — **2.00
> `Directory.GetFiles` scans and 0.99 deletes per rotation**, at 5.49 rotations/block. The cost reading was not: a
> scan of that folder costs **0.256 ms, about 30× less than one append**. Journal time is **95.6% appends**
> (2,750 × 7.56 ms) and **4.4% metadata**. Removing both scans would have saved 1.9%. **A structural reading tells
> you what happens; only a measurement tells you what it costs.** The bet journal is therefore **closed as a
> performance subject** — the appends are the record, and a smaller line or fewer recorded bets were both already
> rejected on measurement (mini-plan 12).
>
> **A misplaced sort in T4.3, found by reading the run rather than by any failure.** The first draft sorted
> `GetSpendableUtxos`' result to make coin-selection tie-breaking deterministic. Right reasoning, wrong place:
> `AggregateSpendable` calls that method **only to sum it**, for every auction bidder, bot affordability check,
> company treasury read and dead-node sweep — dozens of times per block. The tie-break now lives in
> `NetworkRoot.SelectUtxos`, where a sort already happens and it is therefore free.
>
> **Next candidate, and it is UNMEASURED: `botTransactionsMs`** — median 0.96 ms, mean **76.9**, max 469, in a
> populated era (13 cast miners, 40 companies, game dates 2011-04→08) that no earlier plan reached. It is
> per-participant decision work, not transaction volume (`txTargetPerBlock` ≈ 0.5). **Whatever measures it must be
> a within-session instrument** — a phase split inside `ScheduleBotTransactionsAfterBlock`, or a count of
> `AggregateSpendable` calls per block — because comparing per-block milliseconds across sessions proves nothing
> here. Also pending: **drop or re-scope mini-plan 17's seven per-block journal trace columns**, which are
> structurally always zero because the journal works between blocks, and would read as a result to the next reader.
>
> Text below is the original specification.

**Status: `AIHelperFiles/mini17-journal-rotation-and-incremental-utxo-plan.md` (close-out §11), merged from branch
`mini17-journal-rotation-and-utxo`.** Takes mini-plan 16's two leftovers — the bet journal (94.9% of remaining write
cost) and **T4.1**, whose trigger mini-plan 15 restated in absolute milliseconds. **T4.3 is folded in**, because
this roadmap already says its index is "maintained alongside T4.1's incremental update": the hook T4.1 adds is the
only place T4.3's index can be kept correct, so building them apart means building the hook twice.

**The journal's premise, read from the code rather than assumed.** Mini-plan 16 measured 19.33 writes/block and
5.12 ms per append, which made the appends look like the target. `BetHistoryRepository` says the cost is more likely
the **rotation**: `MaxJournalEntriesPerChunkFile` is 10,000 and the run rotated **~49 times a minute**, and *each*
rotation runs **`Directory.GetFiles` twice** — once in `RotateToNextChunkFile` to parse the highest index off a
filename, once again inside `EnforceRetentionCap` — plus a create and a delete. **Four metadata operations where
there could be one**, and both scans are derivable from state the repository already owns (`_activeJournalPath`, and
a retained-segment list it mutates on rotation).

**The UTXO side.** `GetUtxoSet()` caches on `_chainVersion` and **replays the entire chain** when it moves —
`O(all transactions ever)`, ~4 ms × 8 rebuilds per block. `GetSpendableUtxos` then **walks the whole set** and
filters by owned address, so every wallet panel and affordability check is `O(whole set)` across **12 call sites**.

**⚠ The rule it inherits, and the reason its predictions look the way they do.** Mini-plan 16 established that a
per-block millisecond figure **does not reproduce across sessions here**. So every criterion in this plan is a
**count or a complexity claim, verified within one session**; milliseconds are recorded as distributions and decide
nothing. **The cross-check is built BEFORE either fast path**, so it cannot be written to fit a result — and it
covers both: the in-memory segment list against a fresh scan, and the incremental UTXO set against a full replay.
**The full replay is never deleted; it is the oracle.** A single assert failure stops the plan, because a wrong UTXO
set is a wrong balance.

### What a bot transaction costs, the zero columns retired, and CLAUDE.md back under budget — ✅ DONE (mini-plan 18, 2026-10-07)

> **The phase has one cause, and it is a full-chain scan.** `TryCastSellFlow` is **98.2%** of the bot-transaction
> phase (9,990 of 10,178 ms over 192 blocks), runs on **41%** of blocks at **126.5 ms per invocation**, and
> `TryNonMinerExchanges` **never ran at all** because sell-flow always filled the budget first. The every-block
> path (`TryCasinoBotDonation`) is 1.8%; the budget arithmetic 0.0%. That asymmetry is why the phase read median
> 1.067 ms against mean 53.009.
>
> **The cause, read from the code and therefore NOT yet measured:** `TrySellFlowSend` calls
> `FirstBlockHeightMinedBy(nodeId, chain)` once per cast miner, and that method **linearly scans the entire
> chain** — 13 cast miners × 1,607 blocks ≈ **20,900 block comparisons per invocation**, with a miner that has
> never mined scanning the whole chain to return `null`. **It grows linearly with height**, which is why the phase
> was invisible at height 400.
>
> **⚠ THE NEXT PLAN IS ONE METHOD, not an audit.** A grep for the *pattern* found **17** full-chain scan sites and
> **cleared fifteen** of them: the oracle (by design), `GetUtxoSet` (cold since mini-plan 17 B1), `GetTransaction`
> / `GetAddressData` (UI reads), `EnsureReserveGuardSeeded` (once per session), `IsHistoricalSaltPresent` (per
> historical event, and the sell-flow path passes a null salt so it is skipped), `RewriteWholeChainFile` (tip
> mismatch only). **Fix shape:** memoise first-mined height per node, or maintain it in the block hook exactly as
> mini-plan 17 B1 maintains the UTXO set. **State its prediction as a COUNT** (chain blocks inspected per
> invocation → ~0); `botSellFlowMs` then verifies it **within one session**.
>
> **P3 confirmed and it refuted this plan's own premise.** Part B was scoped around the UTXO reads;
> `outpointsWalked` measured **median 0, max 42**, because mini-plan 17's address index had already made them
> nearly free. The cost was somewhere nobody had proposed, and the sub-phase split found it because it measured
> the *structure* rather than the suspicion.
>
> **P1 refuted** on its threshold (13.8 `AggregateSpendable` calls/block against ≥20 predicted; its second clause
> held). **P2 UNTESTABLE, not refuted** — `castPowered` was constant at 13 across all 192 blocks, so the
> correlation was undefined. **P5 refuted** — CLAUDE.md went **96,707 → 90,942 characters** against a ≤88,000
> target. **P2 and P5 failed for the same root reason: a number chosen without checking the quantity it
> constrains.**
>
> **CLAUDE.md's remaining bulk is RULES, not cases**, so reaching the stated 60,000 target is now a **structural**
> call rather than more pruning — see the entry below. Extracted this plan: §29.12.1 (the fifth, TEXT-side locale
> shape), §29.12.2 (the baseline's history), **§40.12 — "Measurement discipline: five ways an instrument lies"**.
>
> Text below is the original specification.

**Status: `AIHelperFiles/mini18-bot-transaction-cost-and-claude-md-plan.md` (close-out §10), merged from branch
`mini18-bot-transaction-cost`.** Takes both items mini-plan 17's close-out left behind, plus the CLAUDE.md
extraction the developer approved.

**(A)** Delete mini-plan 17's seven per-block journal trace columns — structurally always zero, because the journal
flushes at bet rate *between* blocks. The session-total breakdown in the ranked table stays; it is what answered
that plan's P1. **(B)** Instrument `ScheduleBotTransactionsAfterBlock`, now the dominant per-block term
(**median 0.957 ms, mean 76.899, max 469.256**) and never once looked inside. It is **not** transaction volume —
`txTargetPerBlock` ≈ 0.5 in that era — so it is decision work, and that work runs through `AggregateSpendable` →
`GetSpendableUtxos` for every auction bid cap, bot affordability check, company treasury read and dead-node sweep,
with **13 powered cast miners and 40 companies** live. Nothing counts those calls. **(C)** Extract the locale-sweep
block to `ProjectDesignManual.md` §29.12 and Pattern 6's cost-note narratives to Ch. 38/40 + §29.13, keeping each
rule and its pointer. Target **≤ 88,000 characters** from **96,707** today.

**The discipline, inherited twice over:** counts first, no cross-session millisecond comparison, and **B builds no
fix** — it measures, and whatever it points at is the next plan's subject. Mini-plan 17 read a mechanism exactly and
got its cost completely wrong; acting on a plausible mechanism is the error this plan is shaped to avoid.

### CLAUDE.md depuration — ✅ DONE (mini-plan 19, 2026-10-07)

> **91,594 → 83,776 characters (−8.5%), and the target is re-set from 60,000 to 84,000 with its reasoning.**
>
> **Most of the saving was DUPLICATION, not relocation.** The locale block was a second copy of §29.12; the fee
> row's detail a second copy of the step-14 plan; the Player-start row restated Pattern 2's own rule. **The file's
> "if the subject already exists, EDIT it — never append a second version" rule had been broken repeatedly, and
> the budget was the symptom.** Every carve was mandated by a rule already in the file — the ~500-character row
> limit, the no-second-version rule, and a section whose own first line says to go read Chapter 29. **None of it
> required taste.**
>
> **Carved:** seven over-long Canonical Decisions rows (the worst **1,916 → 849**) · Pattern 6's instrument
> narratives (already in §40.12) · UI Layout down to its eleven DON'Ts, each grep-verified present in Ch. 29
> before removal · the scripting-tools section → new **`Documentation/DEV_ENVIRONMENT.md`**, CLAUDE.md keeping the
> three facts that are traps *before* you would think to open a doc (no Python, PowerShell 5.1, `node -e` makes
> path permissions advisory).
>
> **Declined, with reasons, so they are not re-litigated:** Pattern 7's fifteen conventions (moved *into* the file
> in August precisely so they would be found), Pattern 2's checkpoint contract, and the Canonical Decisions table
> itself. **60,000 was unreachable without moving architecture rules out, and a target that cannot be met trains
> the reader to ignore it** — the same mechanism the file's own warning-suspension note describes.
>
> **One invariant failed and the failure is the lesson. I1** — a mechanical differ over every bolded statement —
> was abandoned as unworkable: it reported **28 lost rules, every one a false positive**, first because the regex
> captured fragments and then because my fix's word filter produced probes that could never match. **A one-off
> verifier written in the same session as the edit it checks is as likely to be the thing that is wrong** — the
> third instance of that shape in two plans. What worked was the boring check: **one distinctive token per removed
> passage, grepped across the corpus, 13 of 13 found.**
>
> Text below is the original specification.

**Status: `AIHelperFiles/mini19-claude-md-depuration-plan.md` (close-out §7), merged from branch
`mini19-claude-md-depuration`.** Documentation only — no code, no runs. **Scope decided by the developer: Tier 1 +
Tier 2.** Four carves, each either mandated by the file's own policy or reversing no prior decision: the **seven
Canonical Decisions rows over 500 characters** (6,269 chars, every one already naming its full doc, so trimming
removes a *second copy*), **Pattern 6's five instrument rules** (cases now in §40.12), **UI Layout & Scrolling**
down to its trigger and its what-not-to-write bullets (the rest to Ch. 29, which the section already tells the
reader to open), and the **scripting-tools section** to a new `Documentation/DEV_ENVIRONMENT.md` keeping three
lines — no Python, PowerShell is 5.1, `node -e` is the only open-tail rule.

**Its discipline is INVARIANTS, not predictions**, since nothing is being measured. The load-bearing one: **no rule
is lost, verified by diffing every bolded statement before and after** — anything that disappears must be present
at a named destination. Plus: the stored detector must still return 17 when extracted from the document and run
(the check that caught mini-plan 18 C's corruption), and **every edit to text carrying `$`, backticks or quotes
uses the file tool** — Standing Convention 14, violated twice by this plan family.

**And it re-sets the 60,000 target to the landed figure**, because the measurement below says nothing can reach it
without carving architecture rules. *A target nothing can reach trains the reader to ignore it* — the same failure
the 100k-warning note already describes.

### Pending UI objectives, an Exit button, and month names — ✅ DONE (mini-plan 20, 2026-10-10 + mini-plan 23, 2026-10-12)

> **✅ Every carried part is DONE by mini-plan 23 (2026-10-12)**, close-out §5 of
> `AIHelperFiles/mini23-carried-ui-plan.md`: the Exit button, month names (with locale pass 4 widened), the Calendar
> snapshot, the "Hardware credits in use" label, and the explorer's frame cost. The explorer now shows the last 10
> winning numbers, or only the latest one behind a toggle, at the 16.7 ms vsync floor at 99 bets/s. The carried list
> below is kept as the record of what was handed over.
>
> **Done:** Part 0 (clock and counter recover in ≤ 1.05 s after 9000X; DEV credits ladder), **C** (the bet column
> follows motion only, with a full-instant tooltip), **F** (the explorer's As Played / Per Bet paces, and a
> one-second cap on empty stretches). **Closed here by the developer's call to merge and start a new plan.**
>
> **📝 Taken by mini-plan 23 (`AIHelperFiles/mini23-carried-ui-plan.md`, branch `mini23-carried-ui`, SPECIFIED
> 2026-10-11)**, in the order Exit → month names → Calendar → APS label → explorer frame cost. C3 is resolved by reason
> (a still list needs no motion rule). C2b's remainder (the Manual ~l.5057 note) is done at its close-out.
>
> **⚠ CARRIED, still open, each specified in the plan's §2 — this list is their home until a plan takes them:**
> - **A — the Exit button on MainMenu** (D-20.1 approved: a confirmation naming the block the world resumes from).
>   The developer called it small but very important.
> - **B** — the Calendar snapshot on arrival (the objective "Calendar entry date" below).
> - **D** — five culture-less `MMM` renders; widen locale pass 4 and show it fires before fixing them.
> - **E** — DiceGame's disabled APS dropdown becomes a label of the credits in use.
> - **C2b** — clarify CLAUDE.md's "1 bet tick" and Manual ~l.5057 (a tick holds one bet per credit).
> - **C3** — `ClientsBetsHistory`'s per-bet seconds: the same treatment as C, or a written reason.
> - **The explorer's frame cost** — 59 → ~28 fps while rows stream; the per-stage timer is built and not yet run.
>
> Close-out: the plan's §5. Text below is the original specification and its running record.

**Status at specification: `AIHelperFiles/mini20-ui-pending-and-exit-button-plan.md`, branch
`mini20-ui-pending-and-exit-button`. Nothing built; next is Part 0.** **Part 0** (added at approval, the developer's request) is a visual diagnostic
of the clock, the attempts counter and the bet rows while DevTimeScale and hardware credits change. Its code
reading found that the timestamp column can strobe along **two axes**. **Down the list**, the gap between
adjacent rows is `100 ÷ credits` game-seconds and independent of DevTimeScale. **Frame to frame**, the top row
jumps by the clock's per-frame advance, which DevTimeScale drives. CLAUDE.md's "1 bet **tick** = 100 s" is
correct, but "1 **bet** = 100 s" (part C's diagnosis, `ProjectDesignManual.md` ~l.5057) holds only at 1 credit,
so D-20.2's approved fixed `HH:mm` is reopened. Then four parts. **A**: an **Exit** button on `MainMenu`. It is no weaker than the window's
X because it propagates `NotificationWMCloseRequest` before `Quit()`. It sits in a fixed footer with a ≥50 px
bottom margin, and it shows a confirmation that names the block the world will resume from (D-20.1). **B**: the
Calendar snapshot on arrival (the objective below). **C**: the bet-row timestamp strobe (the objective below),
**measured from the journal before any format is chosen** (P-C1: Δt = 100 s and three residues, or the diagnosis
is refuted). **D**: found while specifying. Five `MMM` renders in `ClientsBetsHistory`/`ClientsTransactions` with
no culture, which the locale detector's pass 4 cannot see because it only matches a format that **starts** with a
name token. The stored tripwire read 0 against 5 real hits. It gets widened, **shown to fire**, and then the
sites are fixed. **Added during the plan, at the developer's request:** **E**, DiceGame's disabled APS dropdown
becomes a label; **F**, `BetsHistoryExplorer` gains a pace toggle, **As Played** (default, game time) and **Per
Bet** (a chosen hardware credit count × the same 1x–10x speed, whatever hardware recorded the bets). **Out of scope by design:** the Holdings Hub and the Betting Statistics scene, each a new screen
needing its own plan.

### The bet-history row's timestamp strobes — DIAGNOSED, taken by mini-plan 20 C (developer's report, 2026-10-06)

**Status: SPECIFIED in mini-plan 20 part C (2026-10-08); before that, open and deliberately NOT bundled into
mini-plan 18 (developer's call).** The timestamp column of the bet
rows in `BetHistoryContainer` shows the same kind of frozen-digit behaviour the DiceGame clock and nonce counter had
before mini-plan 15 B fixed them.

**Diagnosed from the code, not yet measured — and it is NOT the same fix.** `BetHistoryItem.cs` renders
`local.ToString("HH:mm:ss", …)` for each row's own bet timestamp. There is **no resampling here**: every row shows a
distinct, real recorded value, so `AdaptiveReadoutSampler` is the wrong tool. The aliasing is **spatial, down the
list, and it is permanent at every speed**: the game clock advances **exactly 100 game-seconds per bet tick**
(`1 bet tick = 100 in-game seconds`), and **100 mod 60 = 40**, so the seconds field of consecutive rows steps
+40 and cycles through only **three residues** forever. It is the identical arithmetic to the clock's
`150 mod 60 = 30` two-value alternation — one axis over.

**Why it only *looks* like a bug at speed:** the pattern is always there; rows only scroll fast enough to see it
when the autobet is running hard.

**So the fix is about displayed PRECISION, not cadence.** At 100 s per bet, `HH:mm` makes adjacent rows differ by
1–2 minutes and reads correctly; `HH:mm:ss` can only ever show a 3-cycle. Whether the right answer is coarser
precision, a relative offset ("+1m40s"), or showing the bet's game date differently is a design question with a
player-facing consequence, which is why it is an objective rather than a one-line change. **Verify the 3-residue
claim against a running list before building anything** — it is arithmetic from the code, not a measurement.

### The explorer held 10,000 more bets than the journal — ✅ EXPLAINED AND FIXED by mini-plan 22 (2026-10-11)

> **Not duplicated records: discarded ones.** Boot restored the clock and every balance to the last block but left
> the journal untouched until the first DiceGame entry of the process, so an explorer visit before that entry
> counted the bets the restart had discarded. Mini-plan 22 A measured it (8,570 discarded bets visible, matching
> Claude's count from the disk exactly, 0 after the rollback), and 22 B moved the rollback to boot. The final run's
> explorer, opened first after a restart, reported `afterPresent=0`. The INC-002 duplicate-id hypothesis below was the
> wrong shape. Text below is the original note.

**Status at noting: open, found by mini-plan 20 F round 4 and deliberately not chased inside a UI plan.**
`BetsHistoryExplorer`'s `[ExplorerPerf]` reported `records=200100` on one entry and `records=136958` on the next,
over the same date range. The journal on disk held **126,958** bets in 13 segments: the second load is exactly
**10,000** above the disk, and before the cursor the first load held ~63,000 more than the second. One segment is
10,000 entries, so the leading suspect is a segment counted twice by the in-memory history (`EnsureFullHistoryLoaded`
plus the active buffer, or a rotated segment kept in memory). That is INC-002's shape, and **§40.8 applies:
any figure computed off `BetHistory` (the explorer's summary, streaks, max level) is suspect until this is
understood.** First step: count duplicate `BetRecord.Id`s in the explorer's `_allRecords` against the journal
files, on the same world, in the same session.

### The 100-second rule — ✅ DONE (mini-plan 21, 2026-10-11)

> **The developer's rule now holds: 981 violations → 0** over 148,407 bets, holes 41 → 0, clock overspend 1.21 % →
> 0. The fix was not the one specified: the baseline showed a hole is mostly bets **stamped late** (each followed by
> exactly 179 gaps at the `MaxBetsPerFrame` cap), so capping the clock alone could not pass the count. **One simulated
> span per frame now moves the clock and every engine, and a bet's stamp is its remaining accumulator.** Mini-plan 08's
> back-date clamp is deleted, and the rule is canonical in CLAUDE.md's Time section. **Part E**, the developer's ~1 s
> DiceGame load: with a run going it was the hidden martingale calculator rebuilding 500 rows three times per entry;
> the table is now built only when opened, and the calculator draws above DiceGame's code-built controls.
> **Still open:** the first entry after launch spends ~1.3 s in the checkpoint's history rollback (objective below).
> Close-out: the plan's §6. Text below is the original specification.

**Status at specification: `AIHelperFiles/mini21-hundred-second-rule-plan.md`, branch `mini21-hundred-second-rule`.
Nothing built; next was Part A, a baseline run on today's code.** It takes the two objectives below as one plan, because the
developer's rule (*at most 100 in-game seconds between one bet and the next*; `100 ÷ credits` at a known credit
count) is broken by both. **A** measures the baseline with instruments that already exist (the journal, and the
Frame cost CSV's overspend columns). **B** caps the clock's per-frame advance at the bet engines' backlog window,
so a stall makes the game slower, never emptier. **C** stamps each bet at the instant its interval expired, not at
its frame's end, and verifies every checkpoint capture still reads the mining bet's own instant. **D** is the count:
zero violations, with the cost in blocks per real minute measured within one session. **No world wipe; journals
already written keep their holes**, and the explorer's one-second trim stays for them.

### The first DiceGame entry after launch spends ~1.3 s rolling the journal back — ✅ DONE (mini-plan 22, 2026-10-11)

> **Done, and it was a correctness defect as well as a cost.** The journal is now rolled back **at boot**, called by
> the checkpoint service after the rollup, so no scene can read bets a restart discarded. It is rolled back **only as
> far as its tail**: newest segment first, a mixed one rewritten atomically, stopping at the first segment with nothing
> to drop. The final run dropped 179 bets in **51.9 ms** (was 1,319–1,829 ms), read 2 segments and rewrote 1; a DEBUG
> cross-check against the old full path read missing 0, extra 0. The first DiceGame entry went from 1.7–2.3 s to 419 ms,
> the rest being the process's cold first entry. Close-out: the plan's §6.
>
> **Original specification note:** taken by `AIHelperFiles/mini22-journal-rollback-plan.md`, branch
> `mini22-journal-rollback`; next was Part A. Reading the code to specify it raised a correctness question as well as the cost: **boot does not roll
> the journal back**, only the first DiceGame entry does, so until then every reader sees bets the world discarded.
> That may be what explained mini-plan 20's 10,000-bet mismatch. **A** measures inside the rollback and tests that
> hypothesis. **C** makes the rollback only as large as the tail it removes, verified against the old full path by a
> DEBUG cross-check that must read 0 mismatches. **B** then moves it to boot, called by the checkpoint service so
> Pattern 5's order (and INC-004's recovery path) is untouched.

**Status at specification: open, measured by mini-plan 21 E and deliberately not changed there.** `[SceneEntry]` put
`restore:rollbackHistory` at **1,319–1,829 ms** on the first DiceGame entry of a process, and 0.0 after:
`RestoreLegacyCheckpointIfNeeded` → `RestoreCheckpointClockAndHistoryOnly` → `UserStatsService.RollbackHistoryToUtc`,
once per process (guarded by `_checkpointRestoreSpentThisSession`). It is Pattern 2's guarantee that a restart
reverts to the last mined block, in the journal area INC-003 and INC-004 were about. **First step, before any
proposal:** time inside `RollbackHistoryToUtc` (full-journal load vs. the rollback itself vs. a write). Then ask
whether the work can move to boot, run once in the background, or skip a journal already at the boundary, without
weakening the guarantee.

### A long frame advances the clock past the bets — the holes in the journal (noted 2026-10-10) — ✅ DONE by mini-plan 21

**Status: taken by mini-plan 21 (M1), above. Found by mini-plan 20 F round 5.** The journal holds stretches of
**6–17 game-minutes with no player bet** inside a single continuous session: bet ids consecutive, balance continuous,
nothing missing. **Game time passed with no mining attempt.** In this game a pause in betting stops the clock, so
these stretches are not history; they are lost simulation.

**Mechanism, from the code** (`SimulationService`, the R2-C1 and mini-plan 10 B1 notes): on a long frame at a high
DevTimeScale, `CalendarTimeService` advances by `delta × rate × the PREVIOUS frame's retained fraction`, while the bet
engine keeps only `BacklogWindowSimSeconds()` and drops the rest. A stall of ~110 ms at 9000X is ~1,000 game-s. Mini-
plan 10 B1 measured the average overspend at 0.62%; the holes are where it concentrates.

**Why it matters beyond the explorer:** every running engine drops the same backlog, so the network loses attempts
while the clock runs. The difficulty regulator then reads a slower network than existed. The intent stated at R2-C1
— *a simulation that runs slower is honest; one that silently drops simulated work is not* — is violated exactly on
the frames that matter.

**Direction (to be measured, not assumed):** let a frame's clock advance be capped by what the engines can retain
THIS frame (the same backlog window), so a stall makes the game slower, never emptier.

**The developer's rule, which is the success criterion (2026-10-10):** *"the most time there should ever be between
one bet and the next is 100 in-game seconds"* — one tick, at 1 credit; in general **`100 ÷ credits` game-seconds**.
Measured as a count: **zero** consecutive player bets inside a continuous session further apart than that (plus a
float tolerance), in a 9000X run with deliberate scene changes and credit changes.

**That rule is violated by TWO mechanisms, so it closes both objectives or neither:** these holes (6–17 minutes),
and the frame quantization recorded just below (100 / **150** / 50 s at 1 credit × 9000X, where 150 already
exceeds one tick). A plan that fixes only the holes would still fail the count. **Plan them together.**

**What it does NOT do: repair journals already written.** Their timestamps are the record of what the
simulation did; this project does not rewrite history in place (mini-plan 08 §5), and the next world wipe clears
them. The explorer's As Played trim (mini-plan 20 F) hides the holes in replay meanwhile; it does not remove them
from the world.

### Per-bet timestamp fidelity when a frame holds about one bet — candidate (noted 2026-10-08) — ✅ DONE by mini-plan 21

**Status: taken by mini-plan 21 (M2), above. From mini-plan 20 Part 0 round 2.** At 1 credit × 9000X the journal's gaps between player
bets average 100.6 s but come as **100 / 50 / 150 s**. Mini-plan 08's rule gives a frame's LAST bet the clock's
exact value, so with ~1.5 bets per 150 s frame a one-bet frame stamps at the frame's end, not where its interval
expired. **Right on average, wrong per bet.** Invisible in DiceGame's column, which shows hours at that speed. Any
fix touches mini-plan 08's load-bearing contract (the calendar equals the timestamp of the event that defines the
world), so it needs its own plan.

### Calendar entry date — snapshot on arrival, except from the Explorer — ✅ DONE (mini-plan 23 B, 2026-10-12)

**✅ Built and verified by mini-plan 23 part B.** It also fixed a stale "present" during a run, and made the explorer
hand its date back to the Calendar. **Original status: the developer's requirement, recorded 2026-08-24. Not built — SPECIFIED in mini-plan 20 part B
(2026-10-08).** Full write-up, with the run that
exposed it and the two defects it settles: `AIHelperFiles/mini06-clock-rewind-reproduction-plan.md` **§9.7e**.

**The requirement.** Arriving at `CalendarsNavigator` **from any scene except `BetsHistoryExplorer`**, the
date/time updates to **the moment the Calendar scene was entered** — a snapshot, which then **does not
auto-update** even while an autobet keeps running behind it. Arriving **from `BetsHistoryExplorer`**, it
adopts the date it arrived from.

**Why it matters beyond convenience.** Today the explorer's cursor opens at
`CalendarTimeService.ExplorerSelectedLocalDateTime`, and that seed moves only when something deliberately
moves it — betting never does. On a world where the player has never picked a date it therefore sits below
the replayable floor, gets silently raised to the floor by `CalendarsNavigator`, and the explorer opens on
the **oldest bet in the journal** — which reads as "there is no history here". Observed in mini-plan 06's T0
run: one row on screen with ~600 bets in the journal, and the panel was right.

**Two notes for the implementation.** `SceneManager.PreviousScene` already answers "where did I arrive
from" — the same one-deep origin that drives origin-aware back navigation, asked at entry instead of exit,
so no new state is needed. And **"does not auto-update" is a deliberate refusal of the event-driven
default** (Important Patterns §6), not an omission: a date the player is in the middle of choosing must not
move underneath them. Anyone working the Ch. 38 poll/event backlog should read §9.7e before "fixing" the
calendar.

### Bot Seed Phrases & Full UTXO Integration (OQ-8.2) — ✅ DONE (Step 16 P16.2, 2026-07-30)

**Status: SHIPPED.** Delivered as block E of Step 16 — every `bot_1..4`, cast miner, non-miner company and passphrase wallet now carries a 3-word seed + `DerivedAddressWallet`, base addresses derived from the seed (D-16.3), both Block Explorer cosmetics deleted, `WorldFormatVersion` 4 → 5 + `RegistryFormatVersion` 1 → 2. **Read `CLAUDE.md`'s Prototype entry before touching this area**: giving ~74 more participants a seed retired premises that eleven call sites depended on, and the three defects that surfaced the next day (bots spending change they could not see, a six-minute launch from affine secp256k1, and a bot bidding against its own reserve guard) are the lesson, not a footnote. Original design: `AIHelperFiles/step8-utxo-realism-plan.md` OQ-8.2; full record `AIHelperFiles/step16-living-governance-and-bot-wallets-plan.md` P16.2 + `Documentation/ProjectDesignManual.md` §30.10, §22.20, §40.7.

*(Historical note, kept because the reasoning generalizes:)*

**Why it was deferred, and why that reason is now gone.** At Step 8 the number of mining bots was an open question, and handing a seed phrase + full `DerivedAddressWallet` integration to an unknown (possibly large) population looked like a poor trade. The game has since settled: the casino-miner population is **exactly four** (`bot_1..4`), fixed by the ND.8c genesis-grant set and the D-EB.7 "only casino players may bid" rule. At four, per-bot seeds are cheap and the migration is straightforward — the original objection no longer applies.

**What it fixes, concretely.** Bots are the last single-address participants in the world. In `BuildAndBroadcastUtxoSpend` the change address is `sender.ReceiveWallet?.NextReceiveAddress() ?? sender.WalletAddress` — with no `ReceiveWallet`, **a bot's change returns to the very address it just spent from**, so receipts and change pile up as dozens of UTXOs on one address. Observed on block 1274 (2011-12-11): `bot_1`'s 193.73 BTC auction bid on `non_miner_18` combined **nine inputs, all printing the same address**, while three other transactions in that same block paid *into* it. It is correct UTXO behaviour and correct coin selection (`SelectUtxos`: exact match, else largest-first) — but the address repetition is an artefact of the missing rotation, not of the model.

**A finding that de-risks the migration** (from the block-1274 audit): `NetworkRoot.TryResolveInputKeys` resolves the **base address** (the node's own keypair) and **derived addresses** (`ReceiveWallet.TryFindSpendingContext`) through two independent branches. So a bot's base address does **not** have to be seed-derived — it can keep the registry address it already carries all over the chain, and take a `DerivedAddressWallet` purely for change rotation. **That suggests no `WorldFormatVersion` bump / chain wipe is required**; confirm during design before relying on it (the player/casino/founders all have base == seed-derived, so this combination has never been exercised).

**The enabling change:** `BotWalletRecord` (`Scripts/BlockchainPort/Blockchain/WalletModels.cs`) stores keys but explicitly no seed — *"gm1q… only; no seed words stored"*. It needs a seed field. Note `bot_wallet_registry.json` is an **identity file, deliberately spared** by `NetworkRoot.ResetWorldIfIncompatible`'s delete list, so existing records survive every reset with the field absent — this is exactly §39.16 rule 5 (sentinel default + backfill), and the backfill must not disturb the existing `Address`.

**Scope decision to make during design — how far the migration reaches:**
1. **`bot_1..4`** (4 nodes) — the ask, and the only participants with real economic agency (bets, bids, dividends, pool payouts).
2. **Cast miners** (`BotWalletRegistry.CastMiners`, up to ~33 by 2025, spawned one per block) — also single-address. Larger and dynamic, but they only mine and run the sell-flow.
3. **Non-miner companies** (40) — also single-address; they receive bids, inflows and conversions.
4. **Ghost miners** — **never.** D-14.11 makes their coins frozen forever by design; they are session-transient with no persisted keys, and giving them wallets would contradict the whole mechanic.

**The scope choice decides a second deliverable:** the two Block Explorer cosmetics `IsSelfChangeTransaction` / `ExternalOutputs` (§29.9) hide change-to-self outputs and exist *solely* because of single-address participants — CLAUDE.md wants them gone before the referral/rank systems ship. They can only be removed once the **last** single-address participant is migrated, i.e. under scope 1+2+3. Under scope 1 alone the cosmetics must stay, and the display stays filtered (block 1274's transaction really has **two** outputs — the 6.05 BTC change back to `bot_1` is hidden, which is what makes its arithmetic look wrong).

**Related, do at the same time if scope allows:** OQ-8.3 (player/casino incoming deposit-address rotation) is the same family — full HD receive behaviour rather than change-on-send only — and is still parked in the Post-Basic-Mode list below.

> **✅ SCHEDULED (2026-07-30) — this is block E of Step 16**, `AIHelperFiles/step16-living-governance-and-bot-wallets-plan.md` **P16.2/P16.3**. Scope taken: **1+2+3 (casino-miner bots + cast miners + companies), ghosts excluded forever** — so the two Block Explorer cosmetics come out in the same phase, gated on one check (D-16.6: a ghost that only ever receives a coinbase produces no change output and therefore does not block the removal; if that check fails, the cosmetics stay and the reason is recorded). Two decisions taken there are worth reading back here: **D-16.3** derives each bot's base address **from its new seed**, making every participant structurally identical to the player rather than leaving 74 nodes on the `base ≠ DeriveAddress(0)` combination this file flagged as untested — which costs a **`WorldFormatVersion` 4 → 5 wipe** (**D-16.4**, authorized, the world was already reset); and **D-16.7** splits the mixed `BotsBtcWallets` scene into `BotsBtcWallets` (the four casino-miner bots) + new `CompaniesWallets` + new `CastMinerWallets`. Note `bot_wallet_registry.json` is an identity file **exempt from the world-reset delete list**, so the regeneration is version-gated rather than left to be remembered.


## From §6 — the original Basic Mode v0.1 Checklist, in full

> The running tick-list for the **Basic Mode** feature breakdown in §5 directly above (PH + P0–P10 + Casino Referral/Rank). Post-Basic-Mode items live in the "Post-Basic Mode v1 — Checklist" under §5, not here. Open design questions are in §7 below.

- [x] Canonical initial total balance defined: `40,000 SC`.
- [x] Specific starting split defined: `39,900 SC Main Balance + 100 SC Bankroll`.
- [x] Game over condition defined: `Main Balance + Bankroll = 0`.
- [x] Current rule defined: `1 bet = 1 nonce attempt`.
- [x] Basic halving scale defined: `2,100 blocks` (updated from 4,381 in 100X migration; total supply 210,000 BTC).
- [x] Last block and next reward are visible in DiceGame.
- [x] Block checkpoints restore financial state.
- [x] Saved strategies work as development/player-owned strategies.
- [ ] **User settings persistence — a real design, which does not exist yet.** Player-owned settings are
  scattered across ad-hoc files with no shared rule for what survives a world wipe. The visible symptom
  (2026-09-15, v6 → v7 reset): `saved_betting_strategies.json` survives every wipe because it sits in the
  exempt set (`NetworkRoot.ResetWorldIfIncompatible`, CLAUDE.md Pattern 2), and the developer expected it to
  be deleted. **Do not fix that file alone.** Decide once which settings belong to the player (survive a
  wipe) and which to the world (wiped with it), then give them one persistence home. Required for Basic Mode.

  **What is waiting for it** — the list is kept here, because each of these is currently a *decision deferred*,
  and a deferral nobody wrote down becomes an omission. Add a line whenever a feature ships a setting with no
  home; that is the cost signal telling us when this is due.

  | setting | today | belongs to |
  |---|---|---|
  | Saved betting strategies | `saved_betting_strategies.json`, exempt from the wipe — survives, which surprised the developer | the player, probably — but the surprise says the rule was never stated, not that the file is wrong |
  | **Bet View: Detailed / OFF** (mini-plan 10) | not persisted; resets to Detailed on every DiceGame entry | the player. A **view preference only**: after mini-plan 10 both states cost the same (~0.025 ms per bet) and both reach the clock's ceiling, so forgetting it changes nothing but what is on screen. *(Until 2026-09-21 this row called it a speed choice with a higher hidden budget. That was the design before the list got cheap, and the two-budget plan was never built.)* |
  | DEV time scale (the requested one) | not persisted | DEV, but the same mechanism |
  | **Bets explorer replay pace, speed and Per Bet credits** (mini-plan 20 F) | not persisted; every entry opens **As Played** at 1x (Per Bet at 1 credit when toggled) | the player. A **view preference**, like Bet View |
  | DEV player credits ladder (mini-plan 20 Part 0) | writes the real hardware state (`hardware_allocation.json`), so it persists as a shop purchase does | **world**, deliberately: it is a shortcut to the shop, not a setting of its own |
  | The strategy panel's last-used values | not persisted; distinct from a *saved* strategy | the player |
  | Anything a future options menu holds | does not exist | the player |

- [x] User-facing DiceGame label uses `Main Balance`.
- [~] Clarify auto-recharge behavior in UI and docs. **Docs done** (ProjectDesignManual Ch.25 + CLAUDE.md: progression resets, Insist After Stop, auto-recharge precedence). UI labels/warnings still pending (P2).
- [x] Add player BTC wallet and addresses.
- [x] Add bot/non-node wallet address model.
- [x] Add casino BTC addresses.
- [x] Add `CasinoFinances` development scene (BTC wallet — addresses, passphrase, UTXO sends).
- [x] **Step 11 — Casino SC Gambling Finances — ✅ DONE (2026-06-30).** `CasinoScBalanceService` autoload (99M SC Main Balance + 1M SC Bankroll, target-to-fill auto-recharge, 100M SC re-loan on exhaustion, checkpoint-persisted); `CasinoClientLedgerService` autoload (per-client SC deposit/withdrawal ledger with stat snapshots; `auto_recharge` excluded from deposit totals/baseline); SC flow wired per settled player bet; `CasinoGamblingFinances` DEV scene from Main Menu (balances, P/L vs total loans, recharge target controls, manual transfers, nav to sub-scenes); `ClientsBetsHistory` DEV scene (global SC wagered all clients live, per-client all-time + since-last-deposit/recharge P/L and wagered, game filter, live bet feed); `ClientsTransactions` DEV scene (deposit/withdrawal-only transaction list per client — auto-recharges recorded but hidden from the visible list — global totals, wager-base annotations). See `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md`.
- [x] Add scheduled bot transactions (core scheduler; circulation trigger to be re-aligned for gradual bot introduction).
- [x] **PH**: Founders Satoshi & Hal (and Mike Hearn) as nodes + `FoundersWallets` dev scene (verified). *They now mine concurrently in the player era — Step 7 below.*
- [x] **PH**: Fix genesis/early coinbase to derived `gm1q…` addresses (genesis stays unspendable).
- [x] **PH**: Block-candidate + hashrate model (minimal weighted lottery) — the keystone seed (verified). Full per-node candidate engine = **P4 ✅ DONE**.
- [x] **PH**: First-launch bootstrap to 21 Mar 2009 (Satoshi dominant, Hal exactly 3) — verified in-engine.
- [x] **Step 7 (founder economics) — DONE & verified**: founders as **regulated concurrent miners** (`FoundersMiningService`) — Satoshi 11,000-BTC ramp + disappearance (~10% share, retire ≥ 2011-04-26), Hal `P=1.0` drip fading to 0 by 9 Aug 2009, Mike Hearn 32.51 round-trip (+82.51, never mines), 12 Jan 10 BTC Satoshi→Hal tx, `HistoricalEventScheduler`, FoundersWallets DEV readout + `founders_trace.csv`. See `AIHelperFiles/step7-historical-character-economics-plan.md`.
- [x] **Step 8 (UTXO realism / address non-reuse) — DONE & in-engine audited.** Replaced the account/balance model with a **real multi-input/multi-output UTXO model** (`Transaction` = `Inputs[]`/`Outputs[]`, chain-replayed UTXO set, per-input signing, `Fee = Σin − Σout`). One unified spend path (`BuildAndBroadcastUtxoSpend`, exact-match else largest-first multi-input coin selection + change). Terminology corrected (D0): the address mechanic is **address non-reuse**, *not* the "Patoshi pattern" (a mining-forensic fingerprint, reserved for the unbuilt Phase 8.5). Plan: `AIHelperFiles/step8-utxo-realism-plan.md`; design: ProjectDesignManual Ch. 30.
  - [x] §6 address research resolved (D4/D5): strict one-address-per-receive holds incl. the receive side (Satoshi received from Hearn at a *new* address); Satoshi↔Hal unidirectional. `historical-blockchain-events-research.md`.
  - [x] Fresh derived **coinbase** address per block — **Satoshi-only** (the address-non-reuse spread, ~109 distinct coinbase addresses audited, tracking to the fractal ~220). Player/casino/Hal/Hearn keep one coinbase/receive address and become multi-address only via **change on send**; bots stay single-address (no seed — OQ-8.2).
  - [x] Real **change outputs** on spends — **E8 reinstated** (17.49 Hearn change → a fresh Satoshi address; audited on-chain in the April round-trip).
  - [x] `FoundersWallets` lists Satoshi's many derived addresses with per-address balances (scrollable address book + "View empty addresses" toggle); BTCWallet + CasinoFinances have the same view.
  - [x] **Clean reset** (`WorldFormatVersion`) instead of an in-place migration (the old chain has no UTXO linkage).
  - [ ] Hal's network-coupled fade (replace the linear `1.0→0` stand-in once gradual miner spawning exists) — *late Basic-Mode tuning, not blocking; unrelated to UTXO.*
- [x] **P10 — Network Fee Activation ≈ 2009-04-26 — ✅ DONE (2026-06-30).** `NetworkFeePolicy`; whole network fee-free before the date, all participants pay after; fee UI in all four wallet send panels; backend bot/casino gates. Also: casino pool atomicity fix; Block Explorer full multi-output display; OQ-8.2 cosmetic filter. Full detail: **§5 → P10** + `AIHelperFiles/step10-network-fee-activation-plan.md`.
- [ ] **Founder long-term timelines** — beyond Hal's fade (above): Hal 2013 sell-off / 2014, Mike Hearn 2016; late Basic-Mode tuning. `step7-historical-character-economics-plan.md`.
- [ ] Add non-miner bot donation tracking (donor-per-bot ledger; groundwork for casino referral system).
- [ ] Add Winning Referral Commission scene (list referrals, claimable 1% SC commission per bot, claim button).
- [ ] **Casino Referral System** + **Casino Rank System** — full systems (design in the "Casino Referral System (Basic Mode)" / "Casino Rank System (Basic Mode)" sections above; referral groundwork is the two items above). `scheduled-bot-transactions-plan.md`.
- [x] Add hardware credit system with casino community mining pool, per-node pool assignment, and BTCPoolsAndHardwareShop scene (`AIHelperFiles/btc-pools-hardware-plan.md`). ✅ 2026-06-25 — credit model, individual↔casino split + round-robin routing, dynamic fee + proportional distribution, Buy/Discard hardware, hardware-locked speed, bootstrap 1 individual + 0 casino. Foundation for **P5** is in place (ProjectDesignManual Ch. 27). Also: continuous difficulty regulator (Ch. 26) validated, + DEV 100X→9000X time tool.
- [x] Add mempool with 24-transaction block cap (`BlockTemplateBuilder`, cap incl. coinbase).
- [x] Add block template builder (P4 / candidate-block model).
- [x] Add bot mining that can compete with the player (per-node candidates; verified a player can beat a faster bot to a block).
- [x] Update README so future features are not presented as current.
- [x] Update Player Guide so it describes the actual playable state.
- [ ] Run longer Basic Mode manual/autobet tests after transaction circulation exists.
- [ ] **Event-driven design audit** (added 2026-07-21, after the ND.8d round-3 stuck-bidder-escalation fix applied this principle directly). Standing project-wide design rule: `_Process` is for genuinely continuous/real-time work only (the game clock, the background sim loop, autobet animation) — everything else that only re-reads state changing on a discrete event (a bet, a mined block, a transfer, a claim) should be event-driven, the state's owner firing a typed event and consumers subscribing instead of polling on a timer. Full principle, the genuine exceptions, the edge-triggered-signal middle case, and the already-good examples already in this codebase: `CLAUDE.md` "Important Patterns" #6 + `Documentation/ProjectDesignManual.md` Chapter 38. **Before Basic Mode v0.1 is considered complete**, run the dedicated audit pass Chapter 38 describes over every currently-running `_Process` override and migrate the poll-based UI refreshes it lists (≈15 scenes, enumerated in Ch. 38 §38.5) to event-driven design where feasible. Not a blocker on other Basic Mode work — a scheduled cleanup pass, same category as the T1–T3 tasks in §8 below.

## From §8 — T1–T3 and T4's finished parts

### T1 — Stop transactions/consensus from committing financial state to disk ✅ DONE (2026-06-24)

Closed the "known edge" left open by the **block = the only commit to disk** model: a mid-session BTC send / consensus round used to flush in-memory SC balances to disk, so an app restart would *not* fully revert to the last block.

- **Was**: `NetworkRoot.PersistStateToDisk()` serialized the *whole* snapshot — chain, pending tx, wallets **and** the live `NodeFinancialStates` — and was called outside block-mining by `CreateAndBroadcastTransaction`, `CreateAndBroadcastTransactionToAddress`, and `RunConsensus`.
- **Fix shipped (stronger than first planned — *nothing* persists between blocks)**: the between-block `PersistStateToDisk()` calls in `CreateAndBroadcastTransaction`, `CreateAndBroadcastTransactionToAddress`, and `RunConsensus` were **removed**. Those actions now only mutate the in-memory chain/mempool; `PersistStateToDisk()` runs only at block-mining (`HandleMinedBlock`), baseline node creation, and startup. (The first attempt — an `includeFinancialState` flag that still persisted the pending tx — was discarded: it would have left a pending tx with its own `Timestamp` on disk while the clock/balances reverted, an inconsistent half-state.)
- **Result**: a BTC tx between blocks lives in the mempool and becomes durable only when the next block is mined; close the app before that and the whole world — clock, balances **and** un-mined pending transactions — reverts to the last mined block. A block is the only commit.

### T2 — Remove dead Block-Mining / maintenance UI from BlockExplorer ✅ DONE (2026-06-24)

These controls predated the background simulation + real-time auto-refresh and had no purpose.

- **Mine button** (`%MineButton` → `OnMinePressed`): removed — manual block minting is obsolete (mining is bet-driven for the player + background-sim for bots). `NetworkRoot.MineAndBroadcastBlock` is **kept** (still used by `RunWeightedBlockLottery`).
- **Consensus button** (`%ConsensusButton` → `OnConsensusPressed`): removed, **and the code path with it** — `NetworkRoot.RunConsensus` and `NetworkSimulator.RunConsensusRound` deleted. They were a no-op: every node already shares one canonical chain (`BroadcastBlock` → `TryAcceptMinedBlock`), so longest-chain reconciliation had nothing to do. (Revisit with fork simulation — see the Post-Basic-Mode item below.)
- **Refresh button** (`%RefreshButton` → `OnRefreshPressed`): removed — redundant with `BlockExplorer._Process`'s 1 s auto-refresh.
- Also removed the now-unused `ActionFeedbackLabel` (only the three deleted handlers wrote to it) and retitled the lone-`%MinerNodeOption` section from "Mining / Consensus" to **"Inspect node"**.
- **Kept**: `%MinerNodeOption` (the node-context selector reused by the tx/address/block lookups).
- **Result**: BlockExplorer shows only live, read-only inspection controls; no orphaned mine/consensus/refresh nodes, handlers, or consensus code remain.

### T3 — DiceGame docked mining display shows stale difficulty ✅ DONE (2026-06-24)

The mining readout embedded in DiceGame did not track the live (retargeted) difficulty the way BlockExplorer does.

- **Was**: DiceGame's `BuildMiningStatusLine` used `Blockchain.GetExpectedAttemptsForCurrentDifficulty()`, which returns `EffectiveDifficulty(Chain[^1])` — the **last already-mined block's** difficulty, ignoring the live `_activeMiningPower` feed-forward. BlockExplorer instead used `NetworkRoot.GetPlayerNextBlockDifficulty()` (the locked candidate difficulty, else the prospective next-block difficulty at current power), so only it reflected the retarget.
- **Fix shipped**: extracted a shared `GetNextOrCandidateDifficulty(node)` helper (locked candidate difficulty, else `GetNextBlockDifficulty(_activeMiningPower)`); `GetPlayerNextBlockDifficulty()` now delegates to it, and `BuildMiningStatusLine` uses it too — so both readouts compute the in-progress block's difficulty from the same live source. The DiceGame line was relabelled `Mining difficulty: {x:F2}  (~{x:F0} attempts/block)` to match the Block Explorer format. `GetExpectedAttemptsForCurrentDifficulty()` is left in place (now only a validation helper referenced by `100x-time-scale-migration-plan.md`).
- **Result**: the DiceGame mining display and BlockExplorer agree on the in-progress block's difficulty and both update live as power/difficulty change.


### T4 — the measurements and the finished items (the open T4.2 stays in the roadmap)

#### T4.0 — What was actually measured (2026-07-29), so the proposals are not guesses

1. **The bet journal is PLAYER-ONLY — the bots cost nothing.** `UserStatsService.OnBetExecutedRegisterBet` has exactly two callers: `DiceGame` (manual bets) and `SimulationService.ExecutePlayerBetOnce`, the latter guarded by `if (_config.IsPlayerActive)`. Bot bets go to `CasinoClientLedgerService.RegisterSettledBet`, which keeps **aggregate `ClientBetStats` counters** (bets/wins/losses/wagered/net) flushed on a 1 s dirty flag. So the four bots — who bet far more than the player — are ~O(1) each, while the player's own bets are the millions of rows. **The correct pattern is already in this codebase; it was simply never applied to the player.** That answers the open question directly: it is the player, not the bots, and not the two together.
2. **Per-block cost grows with chain length, which is the shape of "increasing lag".** Every node keeps its **own** `BlockchainService` (~62 of them: player + 4 bots + 40 non-miners + 14 cast miners + casino + 3 founders). `NetworkSimulator.BroadcastBlock` calls `TryAcceptMinedBlock` on **every** node, each bumping its own `_chainVersion` — which invalidates **every** node's UTXO cache. `GetUtxoSet()` then rebuilds by **replaying the entire chain**, and `AggregateSpendable(node)` reads `node.Blockchain`, so a balance query on N distinct nodes in one block triggers N full replays. At 1,666 blocks × up to 24 txs that is ~40,000 tx-visits per rebuild, and the bot/company/bank sweeps query dozens of nodes per block. **The per-block cost therefore rises linearly as the chain grows** — a run that starts smooth and degrades, exactly as reported.
3. **The world snapshot is 9.25 MB rewritten every block**, also linear in chain length.
4. **Heap pressure:** ~5.3M `BetRecord` objects (est. 1.5–2.5 GB) before the wipe, on an Intel UHD 620 laptop — rising Gen2 GC cost as the list grows.

> **(2) is a strong structural hypothesis, not a profiled fact.** It has the right shape and the right growth curve, but nothing has been timed. See T4.6 — measure before refactoring anything.

#### T4.0b — What the RESUMED run measured (2026-07-30), which partly contradicts T4.0

The P15.8 world was played on to **~Oct 2014 / block 2699** after the P15.11 repair, and `difficulty_trace.csv` now covers 1,472 blocks. Reading it changes two of the assumptions above (full audit: `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` §10, finding F5; decision D-15.37):

1. **The R2 difficulty regulator is confirmed correct** — mean solvetime **62,373 s against the 58,500 s target (+6.6%)**. Block pace is no longer a suspect in anything.
2. **The retention throttle is chronically ~0.71 and does NOT decay with chain length.** By 200-block bucket: `0.752 · 0.693 · 0.801 · 0.806 · 0.653 · 0.599 · 0.706 · 0.688`. A cost that grows linearly with chain length would show a monotone downward trend across 1,500 blocks and does not. **This does not refute T4.0 (2)** — the run is a window inside an already-large chain, so a linear term could be present and swamped — but it does mean the *dominant* term behaves like **chronic saturation plus periodic per-block spikes**, which matches the developer's description ("processes a second, stalls a second") better than a smooth linear decay. **T4.2's urgency is mildly demoted; T4.6 is confirmed as the correct first move.**
3. **Two per-block cost sources the original T4.0 did not name**, both new since it was written:
   - **Dividend-claim transactions: ~8.66 per block** (13,691 `bot_claim` rows over 1,586 blocks). Each is a real on-chain send. Besides the frame cost this saturates the mempool — `pendingTxs` sat at **26–28** against a 24-tx block cap and an ND.4a historical budget of ~5 — so the automated transaction layer has been at `owed = 0` for most of the run. Fixing it is scheduled in **Step 16** (settle bot claims internally, or per company at quarter close, instead of per holder on-chain); it is listed here because it is also a *performance* item.
   - **Telemetry I/O**: five CSV traces appended per block, `company_governance_trace.csv` alone reaching 2 MB. Small individually, but it is per-block file I/O sitting next to the snapshot write.
   - Also note **P15.11b's atomic write doubles the snapshot's write volume** (`.tmp` then rename). That was the correct trade, and it belongs in the budget T4.6 measures rather than being reverted.

**Instrument these five together** in T4.6 — UTXO rebuilds, snapshot serialize+rename, governance tick, the bot/company sweeps, and claim-transaction construction — plus managed heap and block index. That set now covers every hypothesis on the table.

#### T4.1 — Incremental UTXO maintenance — ✅ **DONE (mini-plan 17 B1, 2026-10-06)**

> Shipped. Rebuilds **~8 per block → 0.313**, `utxoMsSincePrev` **0.000 median**. The full replay stays as the
> oracle and for `TryReplaceChain`, and **`DescribeUtxoCacheMismatch` must never be deleted** — it is what proves
> the fast path right, and it was silent across 195 blocks and 61 nodes. **T4.3 shipped with it** (below).
> Text below is the original specification plus mini-plan 15's measurement note.

> **⚠ Measured to height 698 by mini-plan 15, which changes this item's case in two ways.**
>
> 1. **The trigger must be restated in absolute milliseconds, not as a share of a block.** Mini-plan 14's rule
>    ("T4.1 becomes the next plan at 40% of a block") *does* fire — 42.7% at height 674–698 — but the non-UTXO
>    denominator swings **5.9 → 15.8 ms (2.7×) on the same world inside one run**, so the share crossed 40%
>    because the remainder collapsed, not because the replay grew. A threshold on that ratio is a threshold on
>    disk-write noise (Standing Convention 9's shape). The UTXO term itself reproduces cleanly: **1.26 ms at
>    height 150 → ~4.0 ms at height 500.**
> 2. **It plateaus.** 4.66 · 3.92 · 3.88 · 3.99 ms across the last four 50-block buckets to height 698. The prize
>    is a **stable ~4 ms**, not an unbounded curve — still worth taking, and no longer urgent on growth grounds.
>    **Caveat: measured only to game date 2010-04, before Market Birth (2010-07-18)** — real market volume may
>    restart the UTXO set's growth, and nothing has looked past that date.
>
> **Recommended ahead of it: the checkpoint + snapshot write path** (checkpoint alone **9.46 ms** at height
> 350–449, against the UTXO replay's 3.37 ms at the same heights). It is the larger term, and its variance is what
> makes every other per-block figure in this project unmeasurable — so fixing it buys measurement as well as time.

Applying a newly-accepted block's transactions to the cached UTXO set costs **O(txs in that block)**; replaying the chain costs **O(all txs ever)**. `TryAcceptMinedBlock` / `MineBlock` already know exactly which block arrived, so the incremental update is a few lines beside the existing `_chainVersion++`. Keep the full replay for `TryReplaceChain` (a genuine chain swap) and as a DEBUG-only cross-check — *assert the incremental set equals the replayed set* every N blocks, which is the §39.16-rule-1 shape: the cheap path must be provably identical to the truthful one.

#### T4.3 — Address → outpoint index — ✅ **DONE (mini-plan 17 B2, 2026-10-06)**

> Shipped alongside T4.1, maintained by the same incremental hook, and verified by the same oracle (which checks
> the index only **after** the set agrees, so an index fault is never an echo of a set fault). ⚠ **Do not add a
> sort to `GetSpendableUtxos`:** its dominant consumer, `AggregateSpendable`, calls it only to SUM, dozens of times
> per block. Coin selection's deterministic tie-break lives in `NetworkRoot.SelectUtxos`, where a sort already
> happens. Text below is the original specification.

`GetSpendableUtxos(addresses)` walks the whole UTXO set. An `address → outpoints` dictionary maintained alongside T4.1's incremental update makes every wallet panel, bot affordability check and company treasury read **O(that node's outpoints)**. This is the natural follow-on to the R3 fix (§38.7) that already collapsed `AggregateSpendable` from `O(addresses × utxos)` to `O(utxos)` — T4.3 takes it to `O(owned)`.

#### T4.4 — Player stats as aggregate counters; the journal as a bounded recent window

Generalize `ClientBetStats` to **all five clients including the player**, so lifetime totals are maintained incrementally and are exact regardless of retention. The per-bet journal then serves only what genuinely needs rows — `BetsHistoryExplorer`, `CalendarsNavigator`, DiceGame's recent list — and can stay retention-capped (P15.11d) without the "General" scope having to apologize for it. This retires the current situation where *lifetime totals are derived by replaying every record ever written*, which is both the memory cost and the reason F4's double-counting was invisible.

#### T4.5 — Bounded / incremental world persistence

The snapshot rewrites the full chain per block. Options, cheapest first: (a) **split the chain out** of `state.json` and append only new blocks (the mutable governance/financial state stays a small whole-file atomic write); (b) write the chain in **immutable sealed segments** (the deleted `blocks-*.json` had the right *idea* and the wrong *lifecycle* — it rewrote all of them every block and nothing ever read them); (c) leave it and accept the cost, which is defensible below a few thousand blocks. Whatever is chosen must keep P15.11b's atomicity and the "a block is the only commit" contract intact.

> **✅ T4.6 IS DONE (mini-plan 13, 2026-09-29), and it re-orders what follows.** Measured at height ~400, one
> block costs **27.55 ms**: **snapshot writes 18.39 ms (66.7%), and there are exactly TWO of them per block**
> (51 of 51, 503 KB each) · UTXO replay **7.4%** across **8.7 distinct nodes**, its per-rebuild cost growing
> **4.4× while the chain grew 2.5×** · the difficulty trace **6.5%** · bot transactions **5.0%** · governance and
> auctions ~**0** in this era, because no company exists yet. **The suggested order below is superseded: T4.5
> first, and its first move is smaller than T4.5 itself — stop writing the chain twice.** The second write is
> the only path that commits the post-bet financial mirrors (the bots' included), so it cannot be deleted;
> splitting the immutable chain from the mutable state is what makes one write enough. T4.1/T4.2 keep a measured
> share and a measured growth rate instead of an estimate. **T4.4 is also largely done** — mini-plan 12 bounded
> the journal in memory, and the lifetime rollup has been the counter source since mini-plan 03.

#### T4.6 — A per-block cost budget (DO THIS FIRST)

Everything above is a hypothesis until the frame is instrumented. Add per-block timings to the existing `difficulty_trace.csv` (which already carries R2's `simSecOffered,simSecConsumed`, the honest retention signal that caught §38.7's inverse-poll defect): **ms spent in UTXO rebuilds, in snapshot serialization, in the governance tick, in the bot/company sweeps, plus managed heap size and block index.** Then the question "why did it get slower over three days?" is answered by reading a column instead of reasoning about it — and each T4 item above can be accepted or dropped on evidence. §38.7's third rule applies throughout: **a displayed throttle is a measurement, not a diagnosis** — a `SimulationThrottle` below 1 means "find what is eating the frame", never "raise the budget".

**Suggested order:** T4.6 → T4.1 → T4.4 → T4.3 → T4.2 → T4.5. The first two are small and independent; T4.2 is the one that needs a real decision about fork simulation.


## From the Post-Basic Mode checklist — the DiceGame "Numbers" view as written before it moved into Basic Mode (2026-10-12)

- [ ] **A "Numbers" bet view in DiceGame — an option for a later version, not a Basic Mode need** (developer's call, 2026-09-20, D-10.3). A second way to watch bets: only the roll numbers, red or green, in `PreviousWinnerNumbersGrid`. It shipped briefly in mini-plan 10 and was withdrawn because it halved the frame rate at high speed. **The measurements are done; only the design choice is open:** a cell repaint costs **~0.21 ms**, and at 160 bets per frame all 100 cells change every frame (26 fps, against 57 with the same grid visible but not written to). What is written makes no difference, so no cell-level optimisation helps. The two ways out both change what the view promises: **a refresh cadence** (~10 Hz, ~50 fps by arithmetic, unmeasured) or **fewer cells**. Its per-bet cost is already solved (D-10.2: ring buffer, fixed order, 0.025 ms/bet). **Before showing it, fix its scene geometry**, which has never been on screen: it starts at y 534, under the Bet View button, and its 540 minimum height runs past the bottom band (ProjectDesignManual Ch. 29 §29.11). The grid still serves BetsHistoryExplorer, whose replay is slow enough that none of this bites. Full record: mini-plan 10 §A3, D-10.3.


## From §5 — the per-block freeze, closed by mini-plan 26 (2026-10-20)

Moved verbatim at mini-plan 26's close-out. Record of the work: `AIHelperFiles/mini26-per-block-freeze-plan.md`
(close-out §12) and `IMPLEMENTATION_STATUS.md`.

**From ▶ NEXT (2026-10-20):**

| # | Work | Where it is specified | Why here |
|---|---|---|---|
| **1** | **The per-block freeze: T4's after-block traffic** (`ScheduleBotTransactionsAfterBlock`, the cast's sell-flow, and the governance spikes) — a sub-phase breakdown measured first, then the fix | **`AIHelperFiles/mini26-per-block-freeze-plan.md` (SPECIFIED 2026-10-20; IN PROGRESS: M1 measured the cause, 95.6% of a send is its re-admission at every other node; the fix, §8.1, is built and M2 confirmed it: 416 ms per block against 4,858 in the same session; close-out next)**, branch `mini26-per-block-freeze` · §8 T4, its two 📝 notes · mini-plan 25 plan §5.5 "F1 results" | **The most visible defect in the game at 2015:** 2.7 s mean per block, worst 8.5 s, every scene frozen. Inserted by F1 (2026-10-20). Mini-plan 26 §2 reads F1's rows: the sell-flow is flat across that window, and the growth is exchanges arriving as the historical tx target outgrows the cast |

---

## From §5 and §6 — Step 17.A + 17.C, closed by mini-plan 25 (2026-10-20)

Moved verbatim at mini-plan 25's close-out. Record of the work: `AIHelperFiles/step17-ui-access-and-event-driven-audit-plan.md` (close-out §11) and `IMPLEMENTATION_STATUS.md`.

**From ▶ NEXT (2026-10-12):**

| # | Work | Where it is specified | Why here |
|---|---|---|---|
| **1** | **Step 17.A + 17.C** — a direct `DiceGame → BetsHistoryExplorer` button, and the event-driven audit (migrate the `_Process` polling backlog) | `AIHelperFiles/step17-ui-access-and-event-driven-audit-plan.md` (SPECIFIED 2026-08-20, **re-specified 2026-10-12**, not started) | 17.C is a **declared Basic Mode v0.1 gate** (§6). 17.A does not exist yet (verified 2026-10-12: only `CalendarsNavigator` and `ScFinances` reach the explorer) |
| — | **Small item, fold into whichever UI plan comes first:** DiceGame's "Numbers" bet view | §6 | Its cost is solved: mini-plan 23 built the 10-number row and the latest-only view |

**📝 Step 17 re-verified 2026-10-12 — its §0 governs.** The catalogue was re-derived from the code: 25 overrides, 8
not polls, 9 subscribed with a fallback timer, 8 pure polls covering 10 scenes. Ch. 38 §38.5 stays stale until
17.C closes. Of the four Market-Birth suspensions, only `CompanyDetails` remains, and it now waits for **a founded
company**. The live test world, at 2012-02-13 and height 1,670, has none, because no test run has let the bots mine
(not a real-play state). 17.A is kept and arrives at **the present, both ways** (developer, 2026-10-12). Plan §8
records every decision. Runs as **mini-plan 25**, branch `mini25-event-driven-audit`.

**From §6, the Basic Mode v0.1 Checklist** (both ✅ done by mini-plan 25: the audit is 17.C, the Numbers view is 17.A2):

- [ ] **The event-driven audit — Step 17.C, item 1 of ▶ NEXT.** Before Basic Mode v0.1 is complete, every
  `_Process` override is audited against CLAUDE.md Important Patterns §6 / `ProjectDesignManual.md` Ch. 38, and the
  poll-based refreshes are migrated where feasible. Re-derive the catalogue from the code first (Ch. 38 §38.5 was
  audited 2026-07-21).
- [ ] **DiceGame's "Numbers" bet view — a small item, moved into Basic Mode (developer, 2026-10-12).** It shipped in
  mini-plan 10 and was withdrawn because the 100-cell grid halved the frame rate (D-10.3). **Mini-plan 23 solved the
  cost** by showing less:
  - `PreviousWinnerNumbersRow` (the last 10, 0.4–0.6 ms a frame at 99 bets/s);
  - `LatestWinnerNumber` (one number, ~0.02 ms);
  - both are live in `BetsHistoryExplorer` behind a toggle.

  DiceGame can use the same components. **Before showing anything there, fix the old grid's scene geometry**: it has
  never been on screen, starts at y 534 under the Bet View button, and its 540 minimum height runs past the bottom
  band (Ch. 29 §29.11). Fold this into whichever UI plan comes first.
