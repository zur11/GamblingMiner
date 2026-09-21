# Mini-Plan 11 — The frame's real capacity, bots betting alongside, and what later eras cost

**Series note:** eleventh entry of the *mini-plan* series, following
`mini10-dicegame-per-bet-ui-cost-and-clock-overspend-plan.md`, whose close-out stated three limits of its own
result. This plan measures them.

**Status:** 🚧 **IN PROGRESS** on `mini11-frame-capacity-bots-and-eras` (branch created 2026-09-21, plan
committed). **§5 step 1 done** (2026-09-21): C1's trace columns, two A1 columns §2 needed and did not list,
and the §0 governor-comment correction — one build, no behaviour change (§3 C1 records what was built).
**Next: §5 step 2** — the A1/B1 run; its protocol is given in the chat before the run.

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

### A2 — the second session

Repeat the densest leg in another session, after a restart (D-09.6: size for the slower one).

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
2. **Run A1/B1** (~5 minutes). **Setup first:** give each bot node credits in the hardware shop and save a flat
   strategy on it from DiceGame's Active node selector. The protocol will list both steps screen by screen.
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
