# Mini-Plan 11 — The frame's real capacity, bots betting alongside, and what later eras cost

**Series note:** eleventh entry of the *mini-plan* series, following
`mini10-dicegame-per-bet-ui-cost-and-clock-overspend-plan.md`, whose close-out stated three limits of its own
result. This plan measures them.

**Status:** 🚧 **IN PROGRESS** on `mini11-frame-capacity-bots-and-eras` (branch created 2026-09-21, plan
committed). **§5 step 1 done** (2026-09-21): C1's trace columns, two A1 columns §2 needed and did not list,
and the §0 governor-comment correction — one build, no behaviour change (§3 C1 records what was built).
**§5 step 2 done** (A1/B1, session 1, results in §2): the frame delivered the largest demand the game can
produce (44,331 of 44,545 bets/s) at 57 fps without breaking; a bot bet costs 22% of a player bet; the per-engine
cap binds first. **§5 step 3 done** (A2, §2): the slower session delivered 44,195 bets/s at 56 fps; the
player's bet is 72% player-only work, 41% of it one per-bet copy of every transfer record.
**Next: §5 step 4** — the developer decides the budget, the cap, and whether the record copy is fixed before C2
(readings and candidates at the end of §2).

**Three questions, one per part:**

- **A — how far does the frame go?** Mini-plan 10 delivered 8,910 bets/s at 58–60 fps and never saw the frame
  break, because the clock's ceiling binds first. The frame's own capacity is unknown.
- **B — what does a bot bet cost?** Every figure so far was measured with the player betting alone. A bot bet
  takes a different path and has never been priced.
- **C — what do later eras cost?** Everything was measured in May–June 2009, when the historical network does no
  mining work per frame. Later it does, and that work is not in the governor's arithmetic.

A and B share their runs. C is a long unattended run and comes last, because it moves the world forward.

---

## 0. A correction this plan starts from

Mini-plan 10's D-10.4 set `DevTimeScaleGovernor.BetBudgetPerSecond` to the clock's demand at the hardware
cap, and its comment says that *"for the hardware that exists the budget never binds"*. **That holds only with
the player betting alone.** `SimulationService.GetTotalActiveMiningPower` sums the credits of **every** running
engine — the player and each running bot runner — and each is clamped to `MaxAutoBetBaseAps` on its own. With
the player and all four bots at that clamp, the governor divides the budget by five engines' credits and the
scale falls to a fifth of the ceiling.

Whether that is right depends entirely on Part A: if the frame can take far more than the budget, the governor
is throttling bots for no reason. The comment is corrected in this plan's first build, with the qualifier it
lacked, and the budget itself is re-decided by A's rule (§4).

---

## 1. What the code says, before measuring

**Bots (read 2026-09-21, `SimulationService`).**
- A bot runner exists only while the player's autobet runs (`_Process` returns early otherwise). DiceGame starts
  them from each bot node's saved strategy (`BuildBotConfigs`); a node with no valid strategy does not run.
- Each runner has its **own** accumulator and its **own** `MaxBetsPerFrame` loop (`TickBots`). Five engines at the
  cap can therefore settle up to five times the cap in one frame.
- Per bet, a bot does what the player does not: `PushBotPlayEntry` (the study-screen history),
  `CasinoScBalanceService.ApplyBetResult` (throttled save), `CasinoClientLedgerService.RegisterSettledBet`, the
  `ClientBetSettled` event (whatever listens to it), then its nonce attempt, then `SaveBotFinancialState` — which
  is **in-memory** (`persist: false`), not the per-bet disk write mini-plan 08 removed from the player.
- It does **not** write the player's journal and does not feed DiceGame's bet list.

So a bot bet may be cheaper or dearer than the player's; nothing in the code settles which.

**The historical network (`NetworkPopulationScheduler`).**
- For every player+bot nonce attempt, the powered cast and the invisible mass accrue attempts in proportion to
  their share of the network's power (`DrainScheduledAttempts`). **Each is a real candidate-header hash.**
- The file's own note: *"late-game the scheduled mass can owe hundreds of attempts per player bet"*. Drained
  attempts are capped per frame at `MaxScheduledAttemptsPerFrame`; what is not delivered stays in the
  accumulators, and past `AccumulatorCap` it is shed. A sustained shortfall slows blocks, which the difficulty
  regulator is meant to absorb (ProjectDesignManual Ch. 26).
  *Found while building C1:* "stays in the accumulators" is true of the invisible mass only. The cast loop
  **breaks** once the budget is spent, before the remaining members' accumulators are touched, so those members
  are not even accrued that frame. The invisible mass is drained last, so it is the one truncated first, and a
  cast member is skipped only if the cast alone owes the whole budget. `scheduledCapShare` counts both cases.
- Arithmetic worth stating: the network's attempts per real second = **DevTimeScale × scheduled power**, whatever
  the player's credits. The governor never sees them — its comment says so, and says why: *"cost under 0.05 ms
  per frame in 2009 (P1, H3)"*.
- **Measured today, 2009-06:** 0 scheduled attempts per frame; the founders ~18 per frame at ~0.07 ms — about
  **4 µs per attempt**, an estimate from one trace and not a measurement of the hash itself.

**Per-block work.** A block mined by anyone runs the block handling and a checkpoint capture. Its cost grows
with what is persisted (chain, state), which grows with the era. `FrameCostProfiler`'s H4 counts frames over
50 ms that carry a checkpoint, but no instrument times the per-block work itself.

---

## 2. Parts A and B — the frame's capacity, measured with bots as the demand

The clock's ceiling caps what the player alone can ask for, so the player alone can never show where the frame
breaks. **Bots can**: with the budget lifted (the DEBUG `Budget off` override from mini-plan 10), the player plus
bots at the ceiling demand far more than any frame can deliver, so every frame is cap-bound and the frame's
limit becomes visible. This is the natural demand the game will face, not a synthetic one.

### A1/B1 — one run, engines added one at a time

**Setup:** each bot node gets hardware credits and a saved strategy (flat — no progression — so no bot
busts and drops out mid-leg). Detailed view, 9000X requested, `Budget off`, Frame cost ON, Bet cost OFF.

**Legs**, ~3 reports each: player alone → + bot 1 → + bot 2 → + bot 3 → + bot 4, then back to the player
alone as the control against session drift.

**How the legs are made — revised 2026-09-21, before any data.** Adding bots by stopping the autobet cannot
produce the control leg: once a bot node has a valid strategy, nothing in the UI removes it before the app
restarts (`_nodeStrategies` is process-lifetime, and a base bet of 0 is ignored by the snapshot rather than
clearing it). A pause does not help either: it keeps the Active node selector locked, and bots are started
only at autobet start. What does work is that **credits are read fresh every frame** (`HardwareRate`). So all
four bots get their flat strategy before the run and start at the 1-credit floor, and each leg raises one bot
to the cap **mid-run**. The control leg drops them back to 1. The hardware shop gains two DEV buttons for this,
`Set to cap` and `Set to 1`; at one credit per click it was 98 clicks a bot, each way.

Consequence, stated: the two "player alone" legs are **player + four 1-credit bots**, about 360 bets/s of bot
demand, some 6 bets a frame at 60 fps, identical at both ends. The legs are told apart in the CSV by
`demandBetsPerSecond`, since `botEnginesPerFrame` stays at 4 throughout.

**Arithmetic, before data, against the third prediction below.** One engine at the cap asks 8,910 bets/s. At
the per-engine cap that is `8,910 ÷ DefaultMaxBetsPerFrame` ≈ **55.7 fps**, so every engine at the cap is cut
by its own `MaxBetsPerFrame` as soon as the frame drops below ~56 fps. That is after vsync is left and before
the 50 fps floor. The prediction is expected to fail on this alone. It stays registered as written, because
§4's cap rule is what that failure feeds. The frame fit is unaffected: it uses bets actually settled per frame,
and cap-bound frames still supply points, up to 5 × the cap.

**What each leg gives:**
- **Frame fit beyond vsync.** Total bets per frame rises with each engine until frames exceed 16.7 ms. Fitting
  `frame ≈ a + b × total bets per frame` on the legs that leave vsync gives the frame's capacity at D-09.1's
  50 fps floor: the `k` at which the fit reaches 20 ms, times 50.
- **Bot cost vs player cost (Part B).** `BotLoop` ms ÷ bot bets per frame against `PlayerLoop` ms ÷ player
  bets per frame, same frames, same session.
- **Per-engine cap.** Whether five `MaxBetsPerFrame` loops behave like one loop five times as big, or whether the
  per-engine cap starts binding first.

**Pre-registered predictions:**
- A bot bet costs within ±30% of the player's 0.025 ms. It skips the journal and the list, but adds a ledger,
  a book and an event.
- The frame's capacity at 50 fps is **well above** 9,000 bets/s. At ~0.025 ms per bet, 20 ms holds several
  hundred bets beyond the fixed per-frame work.
- The per-engine caps do not bind before the frame does.

### A1/B1 — results, session 1 (2026-09-21, 18:10–18:14 UTC)

One run, six legs, 24 reports: 18 full, plus 6 partial flushes at the leg changes, which are not used. World
2009-08-01 → 08-28, chain height 300 → 338. Budget off, cap 160, Detailed, 9000X. No bot error; the developer
watched the retention readout, which never left 100% on screen.

| Leg | demand bets/s | delivered | % | fps | frame p50 / p95 ms | sim ms | outside sim ms | bets/frame | player loop cut | a bot loop cut | retention |
|---|---|---|---|---|---|---|---|---|---|---|---|
| L0 — player + four 1-credit bots | 9,270 | 9,280 | 100.1 | 59.4 | 16.63 / 22.3 | 4.40 | 12.44 | 156 | 15% | 0% | 0.9997 |
| L1 — + bot_1 at the cap | 18,090 | 18,076 | 99.9 | 59.2 | 16.62 / 22.3 | 5.33 | 11.55 | 305 | 16% | 16% | 0.9994 |
| L2 — + bot_2 | 26,910 | 26,907 | 100.0 | 59.5 | 16.58 / 22.1 | 6.07 | 10.72 | 452 | 10% | 9% | 1.0000 |
| L3 — + bot_3 | 35,730 | 35,706 | 99.9 | 59.0 | 16.52 / 22.9 | 7.22 | 9.73 | 605 | 22% | 21% | 0.9996 |
| L4 — + bot_4 | 44,545 | 44,331 | 99.5 | 57.0 | 16.88 / 24.2 | 8.65 | 8.90 | 778 | 60% | 61% | 0.9966 |
| L5 — control, all bots back to 1 | 9,270 | 9,259 | 99.9 | 59.2 | 16.62 / 22.7 | 4.55 | 12.35 | 157 | 17% | 0% | 0.9993 |

Each figure is the mean of a leg's three full reports; "sim" and "outside sim" are per-frame means.

**The frame did not break, again.** L4 is the largest demand the game can produce with the hardware that exists:
five engines, each clamped to `MaxAutoBetBaseAps`, at the clock's ceiling. The frame delivered 99.5% of it at
57 fps. §2's fit, "the `k` at which the frame reaches 20 ms", therefore lies **outside the measured range and was
not measured.** What was measured is a lower bound: **at least 44,331 bets/s at 57 fps**, in this mix.
- *Extrapolation, labelled as one, and not to be fed to §4.* The sim grows ~0.0068 ms per extra bot bet
  (4.40 → 8.65 ms over +622 bets a frame; L2 and L3 sit 0.3 ms below that line). Outside-sim time falls from
  12.4 to 8.9 ms as the sim grows, which is vsync wait being used up, so the real outside work is at most 8.9 ms.
  If both hold, a 20 ms frame takes ~1,140 bets, ~57,000 bets/s.
- **Control:** L5 repeats L0 within noise (59.2 vs 59.4 fps, sim 4.55 vs 4.40 ms, player bet 0.029 vs 0.028 ms).
  No drift over the four minutes.

**B — a bot bet costs 0.0062 ms against the player's 0.028 ms, 22% of it. The ±30% prediction is refuted: bots
are ~4.5× cheaper.** Bot figure = `BotLoop` ÷ bot bets, over L2–L4, where bot bets dominate. L0 and L5 read
0.011–0.012, which is the four runners' fixed per-frame overhead spread over six bets. Player figure =
`PlayerLoop` ÷ player bets, 0.027–0.029 in every leg. The player's extra lies on its own path: the journal
(`OnBetExecutedRegisterBet`), `PersistFinancialState` (a fresh `NodeFinancialState` per bet, with a LINQ copy of
every transfer record), `BankrollStateService.SetBalance`, and the `BetSettled` signal into DiceGame. The frame
profiler cannot split these. §4 B requires the cause to be named, so A2 ends with a short Bet cost leg, whose
segments are exactly the player's bet. §4 B's "fix a dominant bot-only call first" has nothing to act on: the
bots are the cheap side.

**Per-engine cap — the third prediction is refuted, as the arithmetic above said.** At L4, 57 fps, the player's
loop was cut on 60% of frames and some bot loop on 61%. Cost: 0.5% of demand undelivered, retention 0.9966, so
the clock ran 0.34% slow. Even L0 cuts the player's loop on 15% of frames: its p95 frame, 22 ms, is past the
~18 ms at which 160 bets run out. At retention 0.9997 that costs nothing measurable.

**Recorded on the way:**
- **Founder attempts:** 0.12 per player/bot bet throughout, **3.2 µs each** (`FounderDrive` ÷ attempts, L1–L4).
  §1's ~4 µs estimate was the right order and slightly high.
- **Block work, C1's first reading:** 18–25 ms per block over 23 blocks, one at 59.6 ms (the first after arming).
  **One block costs more than a whole frame.** It is what the frames over 33 ms are: every frame over 50 ms in a
  full report carried a checkpoint or a GC. Heights 300–338 are too narrow a range to show growth; that is C2's job.
- **GC:** gen-0 collections in 31% of frames at L0, 84% at L4. Allocation grows with bot bets. It did not cost
  frame rate here; recorded, not chased.
- **Scheduled network:** 0–1 cast miner powered, scheduled power ≤ 1.1, ~0–2 attempts a frame,
  `scheduledCapShare` 0. August 2009 is still an idle network, as §1 said.

**Against §4, pending A2 — nothing is decided on one session:**
- **A:** ≥ 44,331 bets/s at 50 fps or better, far above 9,000, so the first branch applies: the budget becomes
  the measured figure, rounded down. Consequence to weigh at step 4: rounded to 44,000, the budget would bind at
  L4 itself (495 credits → 88X, not 90X).
- **B:** outside ±30%, on the cheap side. A single budget priced on the player's bet is conservative for bots.
- **Cap:** it binds first, so 160 does not stay. §4 names no replacement. The arithmetic candidate is
  `8,910 ÷ 50` ≈ **179** bets a frame: one engine at the cap, served down to D-09.1's 50 fps floor.

### A2 — the second session

Repeat the densest leg in another session, after a restart (D-09.6: size for the slower one). **Added after
A1:** a short Bet cost leg at the end, Frame cost off, to name the player's extra cost by segment (§4 B).

**Pre-registered, from session 1:** L4 holds 50 fps if this session is no more than **~14% slower** than
session 1, because `(8.65 + 8.90) × 1.14 ≈ 20 ms`. Mini-plan 08 measured sessions up to 34% apart; a session
that slow would not hold it.

### A2 — results, session 2 (2026-09-21, 18:28–18:29 UTC, after a restart)

L4 only: player + four bots at the cap, same settings as A1. Three full reports plus one partial flush. Chain
height 342 → 346. No bot error.

| | session 1 (L4) | session 2 (L4) |
|---|---|---|
| fps | 57.0 | **56.2** |
| frame p50 / p95 ms | 16.88 / 24.2 | 17.09 / 24.3 |
| sim / outside sim, ms per frame | 8.65 / 8.90 | 8.79 / 9.00 |
| delivered of demanded bets/s | 44,331 of 44,545 (99.5%) | **44,195 of 44,540 (99.2%)** |
| retention | 0.9966 | 0.9949 |
| player loop / a bot loop cut by the cap | 60% / 61% | 75% / 75% |
| player bet / bot bet, ms | 0.028 / 0.0062 | 0.0286 / 0.0062 |

**The prediction held.** Session 2 is the slower one, but by ~1.5% (sim +1.6%, frame period +1.4%), far inside
the 14% that 50 fps allows. **The slower session's figure is 44,195 bets/s at 56 fps.** It is still a lower bound
on the frame, not its limit.

**The cap got worse within the leg.** The three reports read 64%, 66% and 93% of frames cut, and the partial
flush after them, 1.4 s long, reads 100% with retention 0.972. At full load one frame of 800 bets takes ~18 ms,
right at the ~18 ms where 160 bets per engine run out. The loop leaves itself almost no headroom to catch up
after a block's 20–50 ms frame, so the backlog fills and then sheds. This is most likely the brief dip the
developer saw on the retention readout near the end. The trace puts it in the last 1.4 s before Frame cost was
switched off, just before Bet cost was switched on. No report averaged below 54 fps.

**Block work:** 8 blocks at 20.5–30.6 ms mean per report, max 50.7 ms; weighted mean 26 ms.

### B — the player's extra cost, named (Bet cost leg, 37 reports, 180,173 player bets)

| Segment | µs per player bet | share | on the bot path too? |
|---|---|---|---|
| `PersistFinancial` | **11.63** | **40.8%** | no |
| `RegisterBet` (the journal) | 6.72 | 23.6% | no |
| `NonceAttempt` | 5.67 | 19.9% | yes |
| `ExecuteNext` | 1.80 | 6.3% | yes |
| `BetHistoryFeed` | 1.68 | 5.9% | no |
| `CasinoApplyBetResult` + `ClientLedger` + `ClientBetSettled` | 0.49 | 1.7% | yes |
| `BetSettledSignal` + `BankrollSetBalance` | 0.46 | 1.6% | no |
| **total** (unaccounted 0.04) | **28.52** | | |

The total matches the frame profiler's 0.0286 ms, from a different instrument. **Player-only work is 20.5 µs,
72% of the bet, and the largest part is one call:** `PersistFinancialState(false)` runs on every bet and builds
the player's `NodeFinancialState` mirror, copying **every transfer record** with a LINQ `Select`. Then
`SetNodeFinancialState` → `CloneNormalized` copies them **again**, normalising each amount. This world holds 64
records, so each bet makes ~130 record allocations plus two lists and two state objects: over a million
allocations a second at 9000X, which is consistent with the gen-0 GC in 84% of frames. The records change only on a
transfer, and **their number only grows**, by one per recharge. The copy is O(records) by construction.
*Its growth rate per record is not measured*: one point, 64 records, 11.6 µs.

The bot's `SaveBotFinancialState` goes through the same `CloneNormalized`, with each bot's own records. Bots
recharge rarely at a 0.01 flat bet, which is why it does not show in a bot's 6.2 µs.

*Loose end, not chased:* the segments a bot shares cost 8.0 µs inside the player's bet, against 6.2 µs for a
whole bot bet. The bet profiler's own marks, or warmer caches in the bots' tight loop, would both do it.

### Against §4 — the readings, for the developer to decide (step 4)

- **A.** The slower session's measured capacity is **≥ 44,195 bets/s**, above 9,000, so the first branch applies:
  `BetBudgetPerSecond` becomes it, rounded down to a clean figure, **44,000**. D-10.4's derivation is retired.
  Consequence: the budget binds only with all five engines at the cap, and holds that one configuration at
  **88X (8,800X)** instead of 90X. At 90X that configuration delivered 99.2–99.5%, with the clock 0.3–0.5% slow.
- **B.** A bot bet is outside ±30%, on the cheap side. **One budget for all engines stays.** A budget priced on
  the player's bet is conservative for bots, and weighting engines would be a design change nothing here needs.
  §4 B's "fix a dominant call first" was written for a bot-only call. **The dominant call turned out to be
  player-only** (`PersistFinancialState`, above), a case the rule did not register. Whether to fix it here is a
  new decision.
- **Cap.** It binds first, in both sessions, so `DefaultMaxBetsPerFrame = 160` does not stay. §4 registered no
  replacement. The arithmetic candidate is **180**: the smallest clean figure ≥ `8,910 ÷ 50` = 178.2, i.e. one
  engine at the cap served down to D-09.1's 50 fps floor. Re-priced first, as the constant's own ⚠ note requires:
  a full frame at 180 is `180 × 0.0286 + 4 × 180 × 0.0062` ≈ **9.6 ms** of bets, plus ~1 ms fixed.

---

## 3. Part C — later eras

### C1 — the instrument

`frame_cost_trace.csv` gains the columns that let cost be read **against the era**, not just against the run:
- `gameDateUtc` and `chainHeight` at the report;
- `castPowered`, `scheduledPower` — the network's size, from `NetworkPopulationScheduler`;
- `scheduledCapShare` — the share of frames in which the drain hit `MaxScheduledAttemptsPerFrame`, i.e. in which
  the network was owed more than it got;
- `blockWorkMs` — time spent in nonce attempts that **produced a block**, plus the checkpoint capture that
  follows. An attempt is timed whether or not it finds a block (a `Stopwatch` read costs tens of nanoseconds
  against ~4 µs per attempt); only the block-producing ones are added to this bucket.

`ScheduledDrive` ms and scheduled attempts per frame already exist, so the cost per network attempt falls out
without anything new.

**As built (2026-09-21, step 1).** Nine columns appended to `frame_cost_trace.csv`, so the header changes and the
first report rotates the old trace to `frame_cost_trace.csv.old` (ND.10j). The Output panel's report gains an
`A1` line and a `C1` line.

| Column | Meaning |
|---|---|
| `gameDateUtc`, `chainHeight` | The game clock and the tip's index (genesis 0) at the window's **last** frame. A window is ~10 real seconds, about one game-day at 9000X |
| `castPowered`, `scheduledPower` | `PoweredCastIds.Count` and `TotalScheduledPower` at the same frame |
| `scheduledCapShare` | Share of frames in which the drain was cut short by `MaxScheduledAttemptsPerFrame`: a miner owed a whole attempt it did not get, or was skipped (§1) |
| `blockWorkMsPerBlock`, `blockWorkMaxMs` | Mean and worst time of a block-producing attempt, from before the hash to after the checkpoint **and any stop-on-block freeze**. All four engine types are timed: player, bots, founders, scheduled. The plan's single `blockWorkMs` is split in two, because a mean cannot show a spike (mini-plan 08's 96.7 ms block inside one bet) |
| `botEnginesPerFrame` | Mean bot settle loops run per frame. **Added for A1:** it names each leg from the CSV, not from a clock |
| `botCapBoundShare` | Share of frames in which any bot loop was cut by `MaxBetsPerFrame`. **Added for A1:** H2's `capBoundShare` sees the player's loop only, so §2's "per-engine cap" question had no instrument |

The per-attempt `Stopwatch` read is paid only while Frame cost is armed, and nothing of it exists in a RELEASE
build (every call is `[Conditional("DEBUG")]`, arguments included).

### C2 — the run

The player alone, flat strategy, Detailed view, 9000X, **default budget and cap**, Frame cost ON. Nothing else
armed. Left running unattended from the current date forward.

**How long:** at 9000X one game-day passes in ~9.6 real seconds, so one game-month in ~5 minutes and one
game-year in about an hour. **Target: past Market Birth (2010-07-18)**, about 13 game-months, a little over an
hour. Continuing to Satoshi's retirement (2011-04-26) is optional, depending on what the first hour shows.

**⚠ This run moves the developer's world forward, permanently.** Every block is a checkpoint, so there is no
way back to June 2009 afterwards. **The developer waived the archive (2026-09-21):** the current world may be
advanced or reset freely, so C2 runs on it as it stands.

**Pre-registered predictions:**
- `ScheduledDrive` stays near zero until the network's scheduled power becomes non-zero, then grows with it.
- The drain reaches `MaxScheduledAttemptsPerFrame` at some date before Market Birth. From then on
  `scheduledCapShare` rises towards 1, and `ScheduledDrive` flattens at the cap times the per-attempt cost —
  several milliseconds per frame, taken from the same 16.7 ms the bets use.
- `blockWorkMs` per block grows slowly with `chainHeight`.

---

## 4. Decision rules, registered before the data

**A — the budget.**
- **If the frame's capacity at 50 fps (the slower session) is ≥ 9,000 bets/s**, `BetBudgetPerSecond` becomes that
  measured capacity (rounded down to a clean figure), and D-10.4's derivation is retired. The budget goes back to
  meaning *"what the frame can take"*. That is the number that matters once bots add demand the clock does not
  cap.
- **If it is below 9,000**, the player-alone result (8,910 bets/s at 59 fps) is contradicted by bot bets costing
  more. The budget becomes the measured figure anyway, and B's result says which engine type is to blame.
- `DefaultMaxBetsPerFrame` stays at 160 unless A shows the per-engine cap binding first.

**B — the bot bet.**
- **Within ±30% of the player's bet:** one budget for all engines, as today.
- **Outside it:** the cause is named from B's run. If one of the bot-only calls dominates, fixing that call comes
  before any governor change, because a governor that weights engines is a design change and needs its own
  decision.

**C — the network.**
- **If `ScheduledDrive` stays under 25% of a 16.7 ms frame and `scheduledCapShare` stays near 0** through the
  measured range: the governor is unchanged, and its comment records the era range actually verified instead of
  "2009".
- **Otherwise** a D-11 decision is opened, with the data, between two candidates this plan names but does not
  choose:
  1. **Make the network a term of the governor**, weighted by its measured cost per attempt. The scale falls as the
     network grows: correct, but visible to the player.
  2. **Stop hashing each network attempt.** For the invisible mass, *k* attempts at success probability *p* are
     equivalent, as far as "was a block found this frame" goes, to one draw at `1 − (1 − p)^k`. That turns O(k)
     hashes into O(1). It changes how the network is simulated, not what it produces, and the project's
     "1 bet = 1 nonce attempt" rule concerns the player's bets, not the network's. Still a design decision, and the
     developer's.

---

## 5. Order, and what each step needs from the developer

1. **Build C1's columns and the §0 correction** (one build, no behaviour change) → staged, commit approved.
2. **Run A1/B1** (~5 minutes). **Setup first:** save a flat strategy on each bot node from DiceGame's Active node
   selector, with the bots left at 1 credit. Each leg then raises one bot to the cap mid-run in the hardware shop
   (§2, revised). The protocol lists every step screen by screen.
3. **Run A2** in another session (~1 minute).
4. **Decide A and B** by §4.
5. **Run C2** (about an hour, unattended). No archive: waived by the developer.
6. **Decide C** by §4.

---

## 6. Out of scope

- **The Numbers view** — a later-version option (`PRIVATE_ROADMAP.md`, Post-Basic Mode).
- **Scenes other than DiceGame.** The budget is sized for the most expensive scene to bet in; lighter scenes
  inherit it.
- **An adaptive budget** — a Basic Mode refinement option, unchanged.
- **Building either C candidate.** This plan measures and decides whether one is needed; building it is its own
  plan.
