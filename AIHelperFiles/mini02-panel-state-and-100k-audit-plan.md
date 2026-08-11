# Mini-Plan 02 — Strategy panel state, the 100,000-bet audit, and the BetsHistoryExplorer time collapse

**Series note:** second entry of the *mini-plan* series (`miniNN-…-plan.md`), following
`mini01-split-stop-conditions-plan.md`.

**Status:** 📋 **DRAFT — Part A decided, Parts B/C awaiting review.** No branch created, no code
touched. The run archive (§B.0) is **done**. ·
**Proposed branch:** `panel-state-and-100k-audit` · **World format bump:** none expected ·
**Design record (proposed):** `Documentation/ProjectDesignManual.md` new §24.11 (Part A) and a new
§38.8 (Part C — it is another instance of §38.7's inverse failure); Part B's findings land in this
plan's §B.6, and in `Documentation/INCIDENT_LOG.md` **only** if it turns up a real corruption.

Three pieces of work share one branch: Part B may well produce Part-A- and Part-C-shaped findings,
Part C is measured with the same telemetry Part B reads, and none is large enough to justify its
own branch.

---

# Part A — the StrategyControlPanel empties on a scene round-trip

## A.1 What was reported

Configuration fields in DiceGame's `StrategyControlPanel` are blank after leaving the scene and
coming back. `Increase on loss` was the one noticed; a **general review** of every field was
requested rather than a spot fix.

## A.2 Diagnosis — read from the code, to be confirmed in play

The root cause is one line:

> `Screens/DiceGame/DiceGame.cs:87` — `private readonly Dictionary<string, NodeStrategyState> _nodeStrategies = new();`

It is an **instance** field on a scene that Godot frees on every navigation, so it is empty on
re-entry. `_Ready()` calls `LoadActiveNodeStrategySnapshot()` (line 304), which finds no entry for
`_activeNodeId` and takes the `ClearStrategySettings()` branch (line 821) — blanking **every**
field in the panel, not only the percents.

**Why only *some* fields look lost** (this is what makes the report precise rather than vague):
two other paths write panel fields on re-entry and accidentally mask the clear.

| Path | Restores | Line |
|---|---|---|
| `OnSimBetSettled` — fires on every settled background bet | `Number of bets`, `Amount to bet` | 1145–1146 |
| `BindToRunningBackgroundAutobet` — on re-entry with a live autobet | chance slider, HIGH/LOW | 1182–1184 |

Nothing restores `Increase on loss %`, `Increase on win %`, `Stop on profit`, `Stop on loss`,
`Stop Block`, `Insist On Profit`, `Insist On Loss`. Those are exactly the fields that read as
empty — the report matches the code.

**Blast radius — this is not only cosmetic.** Five consequences, in rough order of severity:

1. **`BuildConfig()` reads the live fields.** Pressing AUTO or Bet Once after a scene round-trip
   builds a config from the blanked panel: **flat betting, both stops disarmed, both insists off**,
   at whatever bet amount happens to be showing. A martingale silently stops being one — the same
   failure shape mini-plan 01 D-M1.9 accepted *once*, at a saved-strategy format change, is here
   happening on ordinary navigation.
2. **`SaveActiveNodeStrategySnapshot()` writes the blank config back.** It fires from many UI
   handlers (lines 631, 637, 682, 708, 744, 948, 1197, 1293, 1408, 1424), so the empty state
   becomes the stored per-node snapshot as soon as the player touches anything.
3. **`BuildBotConfigs()` (line 1364) reads the same dictionary** and skips any node without a valid
   entry — after a re-entry it returns an **empty list**, so bot runners started from that point
   have nothing configured.
4. **`RefreshNodeSelectorReadyDots()` (line 375) reads it** — every node reads as not-ready.
5. **`_activeNodeId` (line 72) is re-initialised to `player`** on entry regardless of which node was
   selected, so the selector silently jumps back too.

**The rule this is an instance of:** *a scene is a view; state that outlives the view must not live
in it.* The codebase already knows this — `_checkpointRestoreSpentThisSession` (2085) and
`_bootstrapAppliedThisSession` (2091) are both `static` with the comment *"Static so it survives
DiceGame being freed and rebuilt on each scene change"*. `_nodeStrategies` is the same kind of
state and did not get the same treatment.

## A.3 The general review (the actual deliverable of Part A)

Before fixing anything, enumerate **every** piece of DiceGame + panel UI state and classify it —
this is the part that was asked for, and the fix falls out of it. The table below is the skeleton
to fill in during implementation, with the expected verdict from the code read above; each row must
be confirmed in play, because the masking paths make guessing unreliable.

| State | Owner | Expected on re-entry | Verdict |
|---|---|---|---|
| Amount to bet | panel | restored *by accident* while an autobet runs; blank when idle | |
| Increase on loss % | panel | **lost** | |
| Increase on win % | panel | **lost** | |
| Number of bets | panel | restored *by accident* while an autobet runs; blank when idle | |
| Stop on profit | panel | **lost** | |
| Stop on loss | panel | **lost** | |
| Stop Block toggle | panel | **lost** (resets OFF) | |
| Insist On Profit | panel | **lost** (resets OFF) | |
| Insist On Loss | panel | **lost** (resets OFF) | |
| Auto Recharge toggle | `BankrollProgramService` | **survives by design** (§25.8 proxy) | |
| Winning chance slider | DiceGame | restored only while an autobet runs | |
| HIGH / LOW | DiceGame | restored only while an autobet runs | |
| APS / hardware rate | `HardwareAllocationRepository` | survives (read fresh) — confirm | |
| Active node selection | DiceGame | **lost** (resets to `player`) | |
| Per-bot strategy snapshots | DiceGame | **lost** (whole dictionary) | |
| Saved named strategies | `user://` repository | survives (personal file) | |

Anything found that is genuinely lost and *matters* gets fixed in the same phase; anything lost and
harmless gets recorded as deliberate, not silently left ambiguous.

## A.4 The fix — decided (2026-08-07)

- **D-M2.1 — `_nodeStrategies` and `_activeNodeId` become `static`** (process-lifetime, not
  scene-lifetime). In-memory, survives scene changes, dies with the process. Exactly the existing
  idiom in this same file (`_checkpointRestoreSpentThisSession` / `_bootstrapAppliedThisSession`,
  both static *for this reason*; `NetworkRoot` holding the whole world in statics; "between-block
  navigation saves are in-memory only", Pattern 2). It does **not** survive an app restart — which
  is correct, not a shortfall: a restart reverts the world to the last mined block anyway, so a
  panel config outliving that would describe a run that no longer exists.
- **D-M2.2 — the panel hydrates from the LIVE SESSION when one is running.** In
  `BindToRunningBackgroundAutobet`, refill from `SimulationService.CurrentConfig.Strategy` (+
  `NumberOfBets`, and the chance/HIGH-LOW it already restores). While a session runs, the session is
  the truthful source: the snapshot is what was **configured**, the session is what is
  **executing**, and today they can already disagree — the static dictionary alone would show the
  configured one and quietly call it current. **Where both are available, the executing config
  wins.**
- **D-M2.3 — `user://` persistence is DEFERRED, not rejected.** Persisting the last-used panel
  config as a personal file (the saved-strategy repository's precedent, exempt from the world-reset
  delete list) would survive an app restart too. Not built here: it needs a real decision on whether
  "last used config" is *personal* state or *run* state, and D-M2.1 + D-M2.2 close the reported
  defect without it. Recorded so the next person doesn't rediscover the option as if it were new.

**One guard rail, independent of the above:** a blank or invalid panel must never silently build a
degraded config. Either the fields are restored, or `BuildConfig()`'s consumers refuse to start —
a run that quietly turns into flat betting with both stops disarmed is the worst of the three
outcomes, and it is what happens today.

## A.5 Verification (developer, in-editor — no headless launch)

- Configure the full panel (both percents, both stops, both insists, Stop Block, chance, HIGH/LOW),
  leave to BlockExplorer, come back **while idle** ⇒ every field is exactly as left.
- Same, but leave and return **with a background autobet running** ⇒ same, and `Amount to bet`
  tracks the live session's current bet as it does today.
- Configure a bot node, switch to the player, leave the scene, return ⇒ the bot's snapshot survives,
  its ready dot is still lit, and starting the runners actually starts that bot.
- Return to DiceGame and immediately press AUTO without touching anything ⇒ the run uses the
  configured progression, not flat betting.
- The active node selector is still on the node it was left on.
- Auto Recharge still mirrors `BankrollProgramService` (the §25.8 proxy must not regress).

---

# Part B — auditing the 100,000-bet run

## B.0 Archive the run FIRST — ✅ DONE 2026-08-07

**Archived to `%APPDATA%\Godot\GamblingMiner_100k_run_2026-08-07\` — 62 files, 29.38 MB.** The live
directory was read only, never written. Everything below runs against the archive.

What was captured, from `%APPDATA%\Godot\app_userdata\GamblingMiner\` (last written 08:47) —

- `bet_history.jsonl` + `bet_history_000001.jsonl` … `bet_history_000010.jsonl` (11 chunks,
  ~2.83 MB each, **~30 MB total**)
- `logs/difficulty_trace.csv`, `logs/network_population_trace.csv`, `logs/founders_trace.csv`,
  `logs/godot.log`
- `block_session_checkpoint.json`, `hardware_allocation.json`, `blockchain/`

Continued play extends these, a restart rolls the journal back to the last mined block, and the
traces are in the world-reset delete list — which is why this was done before anything else, on the
P15.8 precedent (§10 of the step15 plan) and CLAUDE.md's *"Auditing a playtest run"* rule.

## B.1 The question actually being asked

Earlier long runs reported an impossible max-loss streak (>100 consecutive losses at 50% chance,
probability 2⁻¹⁰⁰) — **INC-002**. Its cause was *not* the dice engine: duplicated journal records,
landing adjacent because bet timestamps collide heavily and `OrderBy` is stable, **multiplied** the
streak (measured 12 → 36 on the archived journal). The fix — dedup by `BetRecord.Id`, per-
`(GameId, Chance)` segmentation, the renamed metric and a DEBUG tripwire — shipped at `99582bb`.

This run is **the first long one after that fix**, and it was deliberately run at high frequency
(**5 hardware credits ⇒ 5 bets per game-second**, `SimulationService.HardwareRate`) precisely to
stress the timestamp-collision mechanism that did the amplifying. It reports **max streak 18 over
100,000 bets**.

So the audit has one primary question and one secondary one:

1. **Is 18 real?** — i.e. is the metric now measuring what it claims, on data that is now clean.
2. **Did the high frequency leave any residual distortion?** — the collisions still happen; only
   the amplification was supposed to die.

## B.2 Checks

1. **Integrity.** Duplicate `BetRecord.Id` count (must be **0** — this is the direct test of the
   `99582bb` fix on data produced after it); total record count vs. the reported 100,000; no records
   lost or doubled at chunk boundaries; balance continuity
   `BalanceAfter[i-1] + NetAmount[i] == BalanceAfter[i]` to the satoshi across the whole run,
   including across chunk boundaries.
2. **The streak claim, recomputed independently.** Max consecutive losses per `(GameId, Chance)`
   segment, computed from the archive rather than read off the panel; compare against the reported
   18 and against the bound `log(n)/log(1/p)` ⇒ at n≈100,000, p=0.5 that is **≈16.6**, so 18 is
   entirely ordinary. Report the **whole run-length distribution** against the geometric
   expectation, not just the maximum — a single number can be right by luck, and the distribution
   is what actually proves the sequence is clean.
3. **The frequency hypothesis — the developer's real question.** Measure **timestamp collision
   density** (bets per distinct `TimestampUtc`; INC-002 measured ~3.1 at the old rate, and 5 credits
   should push it higher). Then show the streak is **invariant to sort order**: recompute it (a) in
   file order, (b) in `OrderBy(Timestamp)` order, (c) with records shuffled *within* each timestamp
   group. Three equal answers ⇒ the collision amplification is genuinely dead, and the density
   number quantifies how hard the test was.
4. **Win rate in σ.** Observed wins vs. `chance/100`, deviation expressed in standard deviations
   (INC-002's control measurement got 0.5001 over 1,081,554 bets).
5. **Engine arithmetic parity on a slice.** Replay ~10,000 bets with the engine's *exact* arithmetic
   — BigInt satoshis, `Money.Normalize` = truncation (`MidpointRounding.ToZero`),
   `CalculateMultiplier = Round(100×RTP/chance, 4)`, `BetService`'s `_pendingFractionalProfit` carry
   — and require bet-for-bet reproduction, as mini-plan 01 round 3 did over 684 bets. **Blocked on
   §B.5's parameters**: the strategy config cannot be inferred from the journal, and mini01 round 4
   is the standing lesson about guessing it.
6. **Strategy behaviour at scale.** Count profit-stop firings, loss-stop firings, insist resets,
   §25.5 bankroll-limit resets and auto-recharges; report max bet reached vs. base bet. Round 4's
   segment rule (§25.13) predicts the max bet stays near the ladder depth the loss threshold
   implies — over 100,000 bets that is a far stronger test than the 684-bet run it was derived from.
7. **Scale and limits — INC-001 follow-through.** ~30 MB / 11 chunks at 100,000 bets is ~2.8 MB and
   ~9,000 records per chunk. Report boot-time journal load cost and, specifically, whether the
   rotation bounds **what is loaded per boot** or only what is written — INC-001's 1.13 GB failure
   was a *load* cost, and a rotation that keeps loading every chunk has not fixed it.
8. **Block pace / R2 regulator.** From `difficulty_trace.csv`: mean solvetime vs. the 58,500 s
   target, the `simSecOffered`/`simSecConsumed` retention ratio (§38.7's diagnostic — below 1 means
   "find what is eating the frame", never "raise the caps"), and any `configuredPower > 2×
   realizedPower` tripwire hits. This is the first long run at 5 credits, so it is also the first
   real read on R2 since P15.8 measured 0.713 retention.
9. **`godot.log`.** Every `GD.PrintErr`, assertion and tripwire over the run — including
   `AssertLossRunIsPlausible`, which should have stayed silent, and P15.9's clamp warning. **A clean
   log is a result worth stating explicitly**, not an absence of findings.

## B.3 Tools

`awk` over the `.jsonl` / `.csv` (no Python — see CLAUDE.md's scripting-tools section); `node -e`
for the JSON state files; a throwaway `dotnet run` console project in the scratchpad for anything
that must match the engine's `decimal` arithmetic. Reproducing the engine *approximately* is what
turns real single-satoshi evidence into ~120 phantom mismatches — the mini01 round-3 lesson.

## B.4 What this audit is NOT

Not a tuning pass. Findings are recorded and, if any is a genuine defect, fixed on this branch;
parameter changes to the strategy system, the regulator or the retention policy are a **separate
decision**, on D-15.33's precedent (don't price a placeholder before the mechanism has been seen
firing).

## B.5 Questions for the developer

Blocking for check 5 only — everything else can proceed without answers.

1. **The exact strategy parameters that produced the run**: base bet, `Increase on loss %`,
   `Increase on win %`, `Stop on profit`, `Stop on loss`, both Insist toggles, winning chance,
   HIGH/LOW, number of bets. (Un-inferable ⇒ worth one question, per the mini01 round-4 lesson.)
2. Was it **one continuous autobet**, or restarted / re-entered along the way? Any manual bets mixed
   in? (Part A means a scene round-trip could itself have silently changed the config — so this
   answer may also be a Part A data point.)
3. The **18** — read from `BetsHistoryExplorer`, and over what scope: the whole loaded history, or a
   date/game filter?
4. Did any **bot** bet during the run? (The journal is player-only; bot stats live in
   `CasinoClientLedgerService.ClientBetStats`.)
5. Were all **5 hardware credits on the player node** for the whole run, or moved around?

## B.6 Output

A findings section appended to this plan — one entry per check, each stating the measurement, not
just a verdict. Explicitly including the checks that came back clean.

---

# Part C — time collapses to ~100X inside BetsHistoryExplorer

## C.1 What was reported

With the DEV time scale at **9000X**, entering `BetsHistoryExplorer` drops the apparent rate to
**~100X** for as long as that scene is open, and it returns to 9000X on going back to Main Menu,
DiceGame or BlockExplorer.

## C.2 Diagnosis — the obvious suspect is NOT the cause

The natural first guess is that the scene writes the clock: `BetsHistoryExplorer` does set
`_calendarTimeService.SpeedMultiplier` in three places (lines 85, 370, 378), and CLAUDE.md is
explicit that while a session is delegated `SimulationService` is the **sole owner** of
`SpeedMultiplier`/`IsRunning`/`IsAutobetActive`. But the numbers rule it out:

- `_speedSteps = { 100, 200, 400, 1000 }` (line 32), so `_speedSteps[0]` is **100** — byte-identical
  to the `GameSecondsPerRealSecond = 100` that DiceGame and `SimulationService` set. Writing it is a
  no-op.
- The `_Ready` write (line 85) is inside `if (!_liveMode)`, and `_liveMode = IsAutobetActive` — with
  an autobet running it is `true`, so that branch never executes.
- `DevTimeScale` lives on the `CalendarTimeService` **autoload** and no scene resets it.

**So nothing is changing the requested rate — which means the game is not *keeping up* with it.**
That is `SimulationThrottle` (R2-C1) doing exactly its job: the clock advances by the sim-time the
bet engine actually **retained**, so a frame starved by UI work slows game time in wall-clock terms
rather than letting time outrun the mining it represents. **The reported "100X" is a measurement of
frame starvation, not a speed setting** — §38.7's rule, third bullet: *a displayed throttle is a
MEASUREMENT, not a diagnosis.*

Two candidate frame-eaters, both in this scene, both scaling with exactly the things that are now
large (a 100,000-record history and a 9000X clock):

1. **`OnLiveStatsChanged` re-sorts the ENTIRE history, up to 4×/second** (lines 110–117). It is
   subscribed to `UserStatsService.StatsChanged`, which is throttled to 250 ms — a throttle sized
   for a cheap UI refresh, not for
   `BetHistory.Records.OrderBy(r => r.TimestampUtc).ToList()` over 100k+ records, which is an
   O(n log n) sort **plus** a fresh 100k-element list allocation, four times a second, on the main
   thread. This is the `CasinoCoinSwapService` shape verbatim: a correct event, a rate nobody
   re-checked, expensive work behind it.
2. **`_Process` re-renders on every changed GAME second** (lines 141–148), which at 9000X is
   **every frame**. Each render rebuilds a 260-entry preview and repopulates **two** UI containers
   (`PreviousWinnerNumbersGrid` + `BetHistoryContainer`) — up to ~520 entry updates per frame — and
   runs `AdvanceSummaryTo`. The cadence is denominated in game time, so raising `DevTimeScale`
   *directly multiplies the UI cost*.

Both are consistent with the report's shape: the collapse appears only in this scene, only when the
history is big, and it scales with the DEV time scale.

## C.3 Decisions

- **D-M2.4 — measure before fixing.** The two suspects above are read from the code, not from a
  profile. Confirm with numbers first: `CalendarTimeService.SimulationThrottle` while the scene is
  open vs. in DiceGame, and `difficulty_trace.csv`'s `simSecOffered`/`simSecConsumed` columns over
  the same window (§38.7's own diagnostic). *If the throttle is ~1.0 in that scene, this whole
  diagnosis is wrong and the cause is elsewhere* — say so and start over rather than fixing a
  suspect that measured innocent. **Do not "fix" it by raising `MaxBacklogSeconds` /
  `MaxBetsPerFrame`** — §38.7's second rule: that hands a saturated frame more work.
- **D-M2.5 — a UI refresh cadence must be denominated in REAL time, never game time.** Any
  per-frame or per-game-second rebuild becomes unbounded the moment the DEV time scale moves. The
  fix for (2) is a real-time throttle (the `UserStatsService.EmitStatsChangedIfNeeded` 250 ms
  reference pattern), *plus* the existing "has the rendered window actually changed" guard — not one
  or the other.
- **D-M2.6 — the live-mode subscription must not re-sort.** `Records` is already appended in
  chronological order in practice; the fix is to stop rebuilding a sorted copy per event (append
  incrementally, or re-sort only when the *rendered window* changes, or drop the subscription in
  live mode and let the real-time-throttled `_Process` drive). Which one is chosen depends on C.4's
  measurement of where the time actually goes.
- **D-M2.7 — the three `SpeedMultiplier` writes get cleaned up anyway.** They are no-ops today only
  because `_speedSteps[0]` happens to equal the base rate — a latent violation of the sole-owner
  rule that will bite the day either constant moves. The live-mode branch of `OnSpeedButtonPressed`
  (line 370) writing the clock at all, while advertising `"1x (Live)"`, is the clearest case.
  *Being harmless by numeric coincidence is not the same as being correct.*

## C.4 Work

1. Instrument: read `SimulationThrottle` in both scenes and the trace's retention ratio. Record the
   numbers in §C.6 whether or not they confirm the hypothesis.
2. Fix whichever of the two eaters the measurement indicts (likely both).
3. Re-measure. The success criterion is the **throttle**, not the eye: retention in
   `BetsHistoryExplorer` should approach what DiceGame shows at the same `DevTimeScale`.
4. D-M2.7's cleanup regardless of the outcome.

## C.5 Scope note

`BetsHistoryExplorer` is already named on **Chapter 38's poll-migration backlog**. This is not that
migration — it is the §38.7 *inverse* failure, and it is being fixed now because it is actively
distorting playtest pacing. If the fix happens to make the Ch. 38 migration trivial for this scene,
take it; do not widen to the other ~18 scenes on that list.

## C.6 Measurements (to fill in)

---

## Order of work

1. ~~**B.0 — archive the run.**~~ ✅ done 2026-08-07, before the branch exists.
2. **A.3 — the general review**, in play, filling the table's Verdict column.
3. **A.4** — implement D-M2.1 + D-M2.2 (+ the guard rail).
4. **C.4 step 1** — instrument the throttle *before* touching `BetsHistoryExplorer` (D-M2.4).
5. **C.4 steps 2–4** — fix what the measurement indicts, re-measure, then D-M2.7's cleanup.
6. `dotnet build` clean; developer runs A.5 and C.4's re-measurement.
7. **B.1–B.3** — the audit, against the archive. Last, because it needs no build and its §B.5
   answers may arrive at any point.
8. Docs in the same branch: ProjectDesignManual §24.11 (Part A) + §38.8 (Part C), B.6/C.6 findings
   here, CLAUDE.md only where an architectural rule actually changes.

## Out of scope

- The wider `_Process` poll-migration backlog (Ch. 38) beyond `BetsHistoryExplorer` — see §C.5.
  `StrategyControlPanel` is not on that list at all.
- Any change to hardware progression (P5), the difficulty regulator, `MaxBacklogSeconds` /
  `MaxBetsPerFrame`, or the journal retention policy — B.7/B.8 and C.4 may *measure* them; changing
  them is a separate decision (D-M2.4).
- Panel state in scenes other than DiceGame; if the A.3 review finds the same shape elsewhere,
  record it, don't fix it here.
