# Mini-Plan 08 — Timestamp fidelity, and where the engine's real ceiling is

**Series note:** eighth entry of the *mini-plan* series, following
`mini07-userstats-audit-and-inc003-closure-plan.md`. Its subject was found by mini-plan 06 §9.10c while
looking for something else entirely.

**Status:** 📋 **SPECIFIED, NOT STARTED.** To be built on its own branch off `main`, after mini-plan 06's
keepers are cherry-picked and `repro/explorer-clock-rewind` is deleted.

**Objective, in two halves that must be done in this order.**

1. **Fix the writer** so the bet journal records *when bets actually happened* rather than when the frame
   that settled them ended.
2. **Then measure how far the engine can be driven** — the developer's target is **99 hardware credits
   × 9000X**, and the honest answer today is that nothing has measured it.

---

## 1. The defect, stated exactly

Every bet settled in one frame is stamped with the **same** instant, because `CalendarTimeService` advances
the clock once per frame and `SimulationService` reads it per bet [V: `SimulationService._Process`, the
`while (_accumulatorSeconds >= interval …)` loop; `CalendarTimeService._Process`].

Measured on a real journal (mini-plan 06 §9.10c): 7,926 bets across **949 distinct timestamps**, groups of
7–10, spaced **150.00 game-seconds** — which is `9000 / 60`, the clock's per-frame stride at 9000X.

**Why it matters after the DEV scale is turned back down.** The records are permanent. Every consumer that
reads a timestamp inherits the distortion — mini-plan 05's two-line balance separation, INC-002's streak
metrics, mini-plan 06's P8 cadence signature, and `BetsHistoryExplorer`'s `MaxAppendRowsPerFrame`
calibration, which reasons explicitly from "at most 10 bets can share an instant".

> **A recording is not a performance setting.** `DevTimeScale` is meant to compress wall-clock time while
> leaving the simulation's in-game behaviour invariant — that invariance is stated in
> `CalendarTimeService`'s own comment and is the entire justification for the feature. **Timestamp
> resolution is the one place the invariance silently fails.**

## 2. The fix: back-date each bet by its own interval

The engine already knows the exact spacing. `interval = 1 / HardwareRate` is in *simulated* seconds, and the
calendar advances `SpeedMultiplier` game-seconds per simulated second — so one bet interval is
`interval × SpeedMultiplier` **game-seconds**, and that value is correct at every DEV scale because
`DevTimeScale` multiplies both sides.

**Assign the k-th of `n` bets settled this frame the timestamp
`clockNow − (n − 1 − k) × interval × SpeedMultiplier`.**

Two properties make this the right shape rather than merely a nicer one:

- **The last bet of the frame keeps the clock's exact value**, so CLAUDE.md's canonical rule — *the in-game
  calendar clock always exactly equals the timestamp of the block that most recently defines the
  checkpointed world* — is preserved with no special case. Back-dating forward from a frame start would
  need `frameStart`, which `SimulationService` does not have.
- **The spacing it produces is the spacing the engine actually simulated.** At 5 credits it reproduces the
  20.000 s grid T0 measured; at 99 credits it gives `100 / 99 ≈ 1.01` game-seconds.

**Verification, and it is cheap:** after the fix a journal written at 9000X must show **zero same-timestamp
groups**, and spacing at the nominal `SpeedMultiplier / credits`. That is one `node -e` scan of group sizes,
exactly as mini-plan 06 §9.10c ran.

### 2.2 — What this fix does NOT achieve, stated before anyone measures it

Anchoring the batch to the frame's **end** is exact *within* a frame and leaves a **bounded jitter across
frame boundaries**. The accumulator carries a remainder between frames, so the gap between the last bet of
one frame and the first of the next is not the nominal interval but somewhere between one and roughly two
of them.

Worked at 5 credits / 9000X: `simDelta = 1.5` sim-seconds per frame at 60 fps, `interval = 0.2`, so seven
bets fire and 0.1 sim-seconds carry over. Within the frame the seven sit exactly 20.000 game-seconds apart;
the first bet of the next frame lands ~30 game-seconds after the last of this one rather than 20.

> **That is a reduction from a 150-second void to a ~10-second jitter, not the elimination of error.** P3
> must therefore assert *zero same-timestamp groups* and *median spacing at the nominal value* — **not
> "uniform to the tick"**, which this implementation does not deliver and should not be recorded as
> delivering.

**The exact-phase variant is deliberately not built.** Bet `k` truly fires at
`frameStart + ((k+1) × interval − a₀) × SpeedMultiplier`, where `a₀` is the accumulator before the frame
drains. That is uniform across boundaries, but it needs `frameStart`, i.e. the game-time span the calendar
actually advanced this frame — which is `simDelta × SpeedMultiplier × SimulationThrottle`, a value this
service *writes* and the calendar *applies*. Recomputing it here risks disagreeing with what the calendar
did, and a disagreement in the wrong direction **future-dates a bet**, which is worse than the jitter it
would remove. Build it only if P3 measures the jitter mattering to something.

### 2.1 — Timestamp PRECISION is not the problem, and here is the arithmetic

The developer asked whether this needs a finer timestamp. **It does not, and the reason is worth writing
down because the intuition points the other way.**

Bet spacing in game-seconds is `SpeedMultiplier / credits`, and it is **invariant under `DevTimeScale`** —
the scale multiplies the clock and the bet rate by the same factor. So:

| credits | spacing between bets |
|---|---|
| 5 | 20.0 game-seconds |
| 99 (`MaxAutoBetBaseAps`) | **1.01 game-seconds** |

`DateTime` resolves 100 nanoseconds. At the hardware cap the required resolution is **one second**, seven
orders of magnitude coarser. **Precision was never the constraint; per-frame granularity was.**

## 3. The limit question: 99 credits × 9000X

**Demand** is `credits × DevTimeScale` bets per real second — `99 × 90 =` **8,910/s** at the target.

**Supply** today is `MaxBetsPerFrame × fps = 10 × 60 =` **600/s** [V: `SimulationService.MaxBetsPerFrame`].

So the target demands **14.9× what the engine is currently allowed to deliver**, and `SimulationThrottle`
converts the shortfall into an honest wall-clock slowdown: the clock would run at ~6.7% of 9000X, i.e.
**~600X effective**. That is not a failure — it is the R2-C1 mechanism working exactly as designed — but it
means **99 × 9000X is unreachable today, and the binding constraint is `MaxBetsPerFrame`, not timestamps.**

**The frontier as the code stands is `credits × DevTimeScale ≤ 600`:**

| credits | highest DEV scale at Sim 100% |
|---|---|
| 5 | 9000X *(450/s — 75% of the cap, which is why it worked)* |
| 6 | 9000X *(540/s)* |
| 20 | 3000X |
| 99 | **~600X** |

**But `MaxBetsPerFrame = 10` is a CONSTANT, not a measured capacity.** Nobody has timed a bet. If one costs
20 µs, a 16.6 ms frame could afford several hundred and the constant is two orders of magnitude
conservative; if one costs 1 ms, ten is already generous. **The whole question turns on a number nobody has
measured**, which is CLAUDE.md's own standing rule: *a cost note is a measurement or it is a guess wearing a
measurement's clothes.*

## 4. The test plan

### P1 — Price one bet (desk work, no playtest)

Time `ExecutePlayerBetOnce` end to end and, separately, its parts: the dice roll, `Money.Normalize`, the
wallet mutation, the journal append, `UserStatsService.RegisterBet`, `CasinoScBalanceService.ApplyBetResult`,
`CasinoClientLedgerService`, and the events each fires. **Per CLAUDE.md's scripting table this must be
`dotnet run` on a throwaway console project** — a reimplementation in another numeric model proves nothing
about `decimal` arithmetic.

**Output:** microseconds per bet, and which component dominates. That single number sets the real ceiling on
`MaxBetsPerFrame` and says whether 99 × 9000X is reachable at all or merely a long way off.

#### P1 as specified is only half-buildable — and the half it can build is the cheap half

**The specification above cannot be executed as written, and the reason is worth recording because it is
structural, not an oversight.** `ExecutePlayerBetOnce` splits cleanly in two:

| | Reachable in a console project? | Why |
|---|---|---|
| `_session.ExecuteNext` — dice, `Money.Normalize`, both wallet mutations, the fractional carry, progression, streak, stop conditions | **yes** | plain C# classes; the only `Godot` reference in the whole path is one `GD.Print` in a debug anomaly branch |
| the journal append, `PersistFinancialState`, the SC balance sheet, the client ledger, `RouteNonceAttempt`, the four events each bet fires | **no** | `Godot.Node` autoloads and static chain state; none of it exists outside the engine |

So P1 was split into **P1a (desk, done)** and **P1b (in-engine, built and awaiting a run)**. Note which half
went where: the console project can price the *arithmetic*, which is exactly the half CLAUDE.md's scripting
table insists must not be reimplemented — and it cannot touch the half this plan's own §4 nominates as the
suspect. **The instrument the rule demanded is aimed at the component the hypothesis exonerates.**

#### P1a — RESULT (2026-08-30, throwaway console project, real game source linked verbatim)

2,000,000 measured bets per layer after a 200,000-bet warm-up, on a fresh instance, workstation GC.
Each row adds one ring of the real call stack, so the **difference** between rows is that ring's cost.

| Layer | DEBUG | RELEASE |
|---|---|---|
| `DiceEngine.Play` alone | 0.239 µs | 0.264 µs |
| `+ BetService.ExecuteBet` (2× `Wallet.ApplyTransaction`, carry, event record) | 0.877 µs | 0.522 µs |
| `+ BaseBetSession.ExecuteNext` (progression, streak, stops) — **the full Godot-free core** | **1.768 µs** | **0.703 µs** |

Allocation: **368 B/bet**, ~1 gen0 collection per 11,400 bets. At the 8,910 bets/s target that is ~3.3 MB/s
of churn and under one gen0 GC per second — real, but not a candidate for the bottleneck.

**Read DEBUG, not RELEASE.** The developer measures in the Godot editor, which runs the DEBUG build; the
2.5× gap on the session row is `DebugAssertProgression` plus un-inlined property access. RELEASE is recorded
only so the exported build's figure is not later guessed.

**What it establishes.** At `MaxBetsPerFrame = 10`, the core costs **17.7 µs of a 16,670 µs frame — 0.1%**.
At the full 99 × 9000X demand (8,910 bets/s ⇒ 148.5 bets/frame) it costs **263 µs, 1.6% of the frame.**

> **The decimal arithmetic is not the constraint, and it is not close.** `MaxBetsPerFrame = 10` is roughly
> three orders of magnitude below what the core alone would sustain. Everything that decides this question
> is in the half a console project cannot see — which is what P1b measures, and is precisely §4's own
> prediction and CLAUDE.md §38.7's standing suspicion about per-bet events.

**Do not read the last column of that table as a throughput ceiling.** It is what a frame could do if it did
*nothing else* — no rendering, no bots, no founders, no scheduled network, no UI subscriber. `MaxBetsPerFrame`
belongs well below it. P2 is what finds where.

#### P1b — the in-engine segment profiler (built 2026-08-30, awaiting a run)

`Scripts/Diagnostics/BetCostProfiler.cs` times one bet in six segments — `ExecuteNext`, `RegisterBet`,
`PersistFinancialState`, the three money services, `RouteNonceAttempt`, and the event fan-out — and reports
one breakdown per 20,000 player bets to **the Godot editor's Output panel** and to
`user://logs/bet_cost_trace.csv`.

Four properties are deliberate:

1. **`ExecuteNext` is measured in BOTH halves**, so P1b's first segment is a cross-check on P1a. If the
   in-engine reading is far from **1.768 µs**, the console harness is not modelling what the engine runs and
   every conclusion above is suspect. *That reconciliation is a required output of the run, not a nicety.*
2. **The residue is reported, not absorbed.** `unaccounted = total − Σsegments` holds both the code between
   marks and the profiler's own `Stopwatch.GetTimestamp` calls. A breakdown forced to sum to its whole
   cannot reveal its own overhead.
3. **Off by default, toggled from `DevTimeScaleSelector`** (`⏱ Bet cost`, DEBUG-only). P2 measures the
   frontier, and the profiler's few percent per bet is exactly the quantity P2 is measuring — so arming it
   during P2 would corrupt the result. Arm for P1b, read, disarm.
4. **It announces arming and disarming**, with `GD.Print`. Mini-plan 06 §9.1's rule: a diagnostic whose
   passing state is silence must say out loud whether it is running, or "nothing appeared" is ambiguous
   between "no finding" and "never armed" — and a RELEASE build, where every entry point is stripped by
   `Conditional("DEBUG")`, counterfeits that silence exactly.

**Run protocol.** DiceGame → set credits and DEV scale → start autobet → tick **⏱ Bet cost** → let it print
at least three breakdowns (60,000 bets) → untick. Read the blocks in **the Godot editor's Output panel**
(not the Debugger → Errors tab; these are `GD.Print`). The CSV is the durable copy.

### P2 — Raise `MaxBetsPerFrame` to what P1 permits, and sweep the frontier

For each `(credits, DevTimeScale)` in a coarse grid, run 60 real seconds and record **`Sim:` %**, achieved
bets per second, and fps. The frontier is where Sim% first drops below 100.

**Read it against P1's prediction.** If the measured frontier sits well below what the per-bet cost implies,
something else is the bottleneck — the per-bet **events** are the first suspects, since `StatsChanged`,
`BalanceChanged`, `LedgerChanged` and `ClientBetSettled` all fire per bet and each has subscribers.
CLAUDE.md §38.7 already records one case where a correct event fired far too often cost more than any poll
in the backlog.

### P3 — Timestamp fidelity at the frontier

At the highest `(credits × DevTimeScale)` P2 sustains, run 60 seconds and scan the journal for:

1. **zero same-timestamp groups**;
2. **median** spacing equal to `SpeedMultiplier / credits` game-seconds, with the spread bounded by §2.2's
   frame-boundary jitter — *not* uniform to the tick, which this implementation does not claim;
3. **strictly monotonic** timestamps in write order — the P7 check from mini-plan 06 §9.2, now a standing
   regression test rather than a one-off;
4. **no bet ever timestamped after the clock.** §2.2 explains why this is the property to guard rather
   than uniformity: a future-dated bet would be a worse defect than the jitter.

### P4 — Clock synchrony at 99 credits

The developer's specific worry. With the fix in place, confirm that in-game **block intervals** and the
difficulty regulator's feed are unchanged between `(99, 100X)` and `(99, highest sustainable)`. That is the
invariance `DevTimeScale` claims, and it is now testable at a resolution that did not exist before —
because every bet finally has an instant of its own.

## 5. Out of scope

- **The explorer.** It was correct throughout mini-plan 06 §9.10 and needs no change. Its
  `MaxAppendRowsPerFrame` calibration note will need a factual update once groups no longer reach 10, but
  that is a comment, not a behaviour.
- **Raising `MaxAutoBetBaseAps` above 99.** The cap is a design decision; this plan measures whether the
  existing one is deliverable.
- **Retention and journal size.** A higher sustained bet rate writes records faster and reaches the
  20-segment cap sooner. Worth noting, not worth solving here.
- **Re-recording existing journals.** The clumped records already written are permanent and this plan does
  not rewrite them — mini-plan 05 §6's refusal of heuristic surgery on the journal stands.
