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

**Run protocol.** DiceGame → set credits and DEV scale → confirm `[BetCost] toggle built in this scene` in
Output → tick **⏱ Bet cost** and confirm `[BetCost] ARMED` → **then** start the autobet → let it print at
least three `[BetCost]` breakdowns → stop the autobet → untick. Read the blocks in **the Godot editor's
Output panel** (not the Debugger → Errors tab; these are `GD.Print`). The CSV is the durable copy.

**Arm before starting, not during — and the reason is not cosmetic.** At a high DEV scale the frame is
saturated (§38.7 measured this world pinned near ~133 ms/frame, ~7 fps), so a click on the toggle lands late
or not at all. Nothing disables it and it is **deliberately not being fixed** (developer's call,
2026-08-30): it is a DEV control, and the protocol has no reason to press it mid-run. Arming first also
captures from bet #1 instead of from wherever the click happened to land. The single consequence to carry
forward: **disarming before a P2 sweep means stopping the autobet first** — which P2 does anyway, being its
own run.

*The first attempt at this protocol failed twice over, and both failures were in the INSTRUCTIONS rather
than the instrument: it said "wait for 3 blocks" in a project where a block is a mined block, and it set a
report period of 20,000 bets in a world where a mined block costs ~2,400 — so the three reports it asked for
were ~25 blocks away. A protocol is part of the apparatus and is wrong in the same ways.*

#### P1b's blind spot, PRE-REGISTERED before the run — an O(N) term this world is too young to show

Found by reading the code while instrumenting it, and written down **before** the measurement so it cannot
be retrofitted to whatever the numbers turn out to say.

`PersistFinancialState(false)` runs on **every bet** and deep-copies the bankroll transfer-record list
**twice**:

1. `SimulationService.PersistFinancialState` — `_bankrollProgram.Records.Select(…).ToList()`
2. `NetworkRoot.SetNodeFinancialState` → `state.CloneNormalized()` → `Clone()` →
   `TransferRecords.Select(CloneTransferRecord).ToList()`

So the segment costs **2N object allocations plus 2 list allocations per bet**, where `N` is the world's
accumulated transfer-record count. `BankrollProgramService._records` is **uncapped** — the only operations
on it are `Add` (one per auto-recharge) and `Clear` (on load/restore). N therefore rises monotonically for
the life of a world and never falls.

**Measured on the developer's live world (2026-08-30): `N = 5`.** At that size the term is a handful of
small allocations and P1b will, correctly, report `PersistFinancialState` as cheap.

> **That is the trap, and it is the point of pre-registering this.** A single measurement on a young world
> cannot distinguish a constant from a linear term with a small argument. Reading "PersistFinancialState:
> 0.4 µs, 3%" and concluding the segment is fine would be **exactly the wrong inference** — the same
> reading at `N = 1,000` is 2,000 allocations per bet and would dominate every other segment combined.

**This is a candidate explanation for a symptom already on record and never explained**: `PRIVATE_ROADMAP.md`
§6's note that fluidity at 9000X *"decayed progressively over the last days of the playtest"*. A per-bet cost
proportional to a monotonically growing counter has precisely that signature — gradual, cumulative,
irreversible within a world, and invisible on any fresh one. **Candidate, not conclusion:** nothing has
measured it, and CLAUDE.md's closing rule under Important Pattern 6 applies to this paragraph as much as to
any other.

**How to actually test it**, in ascending order of cost:

1. **Read N off the world before each run** and record it beside the breakdown —
   `user://bankroll_program_state.json`, `Records.length`. A cost note without its N is uninterpretable.
2. **Two-point measurement.** Run P1b, note N; force N upward (lower the bankroll dose so auto-recharge
   fires often, or run long) and re-run. If `PersistFinancialState` µs tracks N linearly, it is confirmed
   with two points and no new instrument.
3. **Only then** decide the fix. The obvious one — don't copy an append-only list on a hot path that never
   reads it back — is cheap, but it is out of scope until measured, and a cap on `_records` would be a
   *persisted-figure* change subject to Standing Convention 1.

**Generalized, because the shape will recur:** *a per-bet cost measured once, on one world, prices that
world's N and nothing else. When a hot path touches a collection, the measurement's unit is µs per bet **at
a stated collection size** — record the size or the number means nothing later.*

#### P1b — RESULT, round 1 (2026-08-30, 5 credits × 9000X, N = 5, three 5,000-bet windows)

Means over the three windows. They agree closely (total spread 1,229–1,388 µs), so this is a stable
reading, not a sample of noise.

| Segment | µs/bet | share |
|---|---:|---:|
| **MoneyServices** (bankroll + casino + ledger) | **872.7** | **66.8%** |
| **EventFanOut** (`ClientBetSettled` + `BetSettled` signal) | **358.9** | **27.5%** |
| NonceAttempt (PoW + block path) | 35.6 | 2.7% |
| RegisterBet (journal + rollup) | 22.0 | 1.7% |
| ExecuteNext (dice + wallet + progression) | 9.2 | 0.7% |
| PersistFinancialState | 8.2 | 0.6% |
| unaccounted | 0.2 | 0.02% |
| **TOTAL** | **1,306.9** | |

**A bet costs 1.31 MILLISECONDS.** Worst single bet 204 ms, 75 ms, 68 ms in the three windows — the
block-mining bets, amortized correctly into the mean.

**Four findings, in order of consequence.**

**1. `MaxBetsPerFrame = 10` is not conservative. It is almost exactly right — and §3 of this plan had the
sign of its error backwards.** §3 supposed the constant might be "two orders of magnitude conservative". At
1.31 ms a 16.67 ms frame fits **12.8 bets if it does nothing else**, so 10 is at ~78% of an *unshareable*
budget the frame must also spend on rendering, four bot runners, the founders, the scheduled network and
every UI subscriber. That is why frames blow out to ~133 ms (§38.7) rather than despite it. **The guess was
right for reasons nobody knew, which is not the same as being justified — and the plan's premise that it was
loose was wrong.**

**2. 94.2% of a bet is two segments, and the dominant one is a synchronous disk write.**
`BankrollStateService.SetBalance` calls `SaveState()` **unconditionally on every call**, which opens
`bankroll_state.json` in `ModeFlags.Write`, serializes, writes and closes. `SimulationService` calls it once
per bet. `CasinoScBalanceService.ApplyBetResult`, by contrast, sets `_saveDirty = true` and does no I/O —
the correct shape, in the same segment, which is why round 2 splits them.

**3. Therefore the headline answer: 99 × 9000X is unreachable today by ~15×, and the reason is now named
rather than guessed.** Demand is 148 bets/frame; supply is 12.8. But the ~15× is not distributed across the
engine — it is concentrated in work that has no business being per-bet. If the per-bet disk write and the
event fan-out were removed entirely, a bet would cost **~75 µs ⇒ ~220 bets/frame**, which puts the target
*inside* reach with margin. **The prize is a 17× throughput improvement, and it is not in the arithmetic.**

**4. The profiler's own overhead is 0.2 µs — 0.016% of a bet.** The `unaccounted` residue was built to
expose exactly this, and it does. **This retracts the caution written into the profiler and this plan that
it must be disarmed before P2.** That caution was reasonable when unmeasured and is now measured: leaving it
armed during a P2 sweep perturbs the frontier by one part in six thousand. *A precaution stated without a
measurement is a guess like any other — this one happened to be three orders of magnitude too timid.*

**The `ExecuteNext` cross-check FAILED, and it does not matter — say both halves.** P1a predicted 1.768 µs;
the engine reads **9.2 µs**, 5.2× higher. The leading cause is that the Godot editor runs the game with a
debugger attached, which P1a's console harness did not. **It changes no decision** — at 0.7% of the bet, the
arithmetic is exonerated more strongly than P1a claimed, not less — but the harness's absolute figure is
**not** transferable to the engine and must not be quoted as if it were. *A cross-check that fails in the
direction that strengthens your conclusion is still a failed cross-check.*

**The pre-registered O(N) prediction stands, unresolved.** `PersistFinancialState` = **8.2 µs at N = 5**,
cheap exactly as predicted, and that still does not absolve it. Two-point measurement is still required.

**Round 2 (built, awaiting a run): the two dominant segments are split into five** — `BankrollSetBalance`,
`CasinoApplyBetResult`, `ClientLedger`, `ClientBetSettled`, `BetSettled` — because a two-call bundle at 67%
cannot say which call to fix.

**Scope caveat for P2, found while reading the bot path.** The profiler instruments **only**
`ExecutePlayerBetOnce`. `ExecuteBotBet` is a parallel path, runs up to `MaxBetsPerFrame` **per bot** for four
bots, and ends with `SaveBotFinancialState(runner)` on every bet — the same per-bet-write shape. So the
frame's real bet load may be ~5× what this measurement covers. **P2 cannot be read as a whole-engine figure
until the bot path is priced too.**

#### P1b — RESULT, round 2 (2026-08-30, same world, 4 full windows + 1 partial)

Means over the four full 5,000-bet windows. **Every one of round 1's five predictions was confirmed**,
which matters as much as the numbers: the diagnosis was written down before the split existed.

| Segment | µs/bet | share | predicted |
|---|---:|---:|---|
| **BankrollSetBalance** (sync disk write) | **933.9** | **66.0%** | ~850 ✓ |
| **BetSettled** (Godot signal → DiceGame) | **382.7** | **27.1%** | ~350 ✓ |
| NonceAttempt | 47.3 | 3.3% | — |
| RegisterBet | 24.5 | 1.7% | — |
| ExecuteNext | 11.2 | 0.8% | — |
| PersistFinancialState | 10.1 | 0.7% | cheap at N=5 ✓ |
| CasinoApplyBetResult | 3.9 | 0.3% | few µs ✓ |
| ClientBetSettled (C# event) | 0.5 | 0.0% | few µs ✓ |
| ClientLedger | 0.2 | 0.0% | ~0 ✓ |
| unaccounted | 0.2 | 0.0% | — |
| **TOTAL** | **1,414.4** | | |

**Two calls are 93.1% of a bet. Everything else together is 97.9 µs.**

**The split earned its keep in both directions.** It confirmed the disk write, and it *exonerated* the C#
event: `ClientBetSettled` is **0.5 µs** while the Godot signal beside it is **382.7 µs** — a 735× gap that
the old combined `EventFanOut` segment would have left as a shared 359 µs suspicion over both. **The
expensive thing is not "events"; it is one subscriber, `DiceGame.OnSimBetSettled`,** which per bet reseeds
the wallet, updates two panel fields, and calls `UpdateBlockchainStatusUI()` → `BuildMiningStatusLine()` —
recomputing live difficulty, reading the chain tip, counting the mempool and rebuilding a string, **up to 10
times per frame, of which only the last is ever seen.** That is CLAUDE.md §38.7's "coalesce at the consumer"
verbatim.

**What the fixes are worth, arithmetically.** Non-dominant work is 97.9 µs. Throttling the bankroll write
takes its per-bet cost to ≈0; coalescing DiceGame's refresh to once per frame amortizes 382.7 µs over the
frame's bets (≈38 µs/bet at 10/frame). **⇒ ~136 µs/bet, ~123 bets/frame — a ~10× improvement.**

**And that settles the developer's actual goal.** 99 credits × 600X demands `99 × 6 ÷ 60 =` **9.9 bets per
frame**. Today that costs 14.0 ms of a 16.67 ms frame on player bets *alone* — which is why it is not fluid.
After the fixes it costs **1.35 ms, ~8% of the frame.** *600X at the hardware cap is not a stretch goal; it
is comfortably inside reach once two calls stop doing per-bet work.* (99 × 9000X would still need ~20 ms
and remains out — but by ~20%, not by 15×.)

**A drift worth naming, not chasing:** the total fell monotonically across the five windows
(1,470 → 1,409 → 1,393 → 1,385 → 1,314), tracking `BankrollSetBalance` (966 → 858). Consistent with the OS
file cache warming to a file being rewritten hundreds of times a second. It does not change any conclusion,
and it is the kind of monotone trend that would be a finding in a different context.

#### ⚠ FOUND WHILE FIXING, NOT FIXED HERE — the continuity sentinel was neutered on this path

Verifying that coalescing `OnSimBetSettled` could not break anything turned up something worse than a
performance problem, and it is recorded here rather than fixed because it is a correctness change and does
not belong bundled into a performance commit.

`UserStatsService.NoteBalanceDiscontinuity` **drops the comparison baseline** — by design, so the next
registered bet re-seeds instead of being compared across a declared jump. `ReseedWalletFromBankrollSource`
calls it with reason `"wallet_reseed"`, and `OnSimBetSettled` called *that* **once per settled bet**.

So the per-bet order was: `OnBetExecutedRegisterBet` sets the baseline → the signal fires → the baseline is
dropped → repeat. **Every bet's baseline was destroyed before the next bet could be compared against it.
For the entire delegated-autobet path, with DiceGame as the active scene, the continuity sentinel was
comparing nothing.**

> **This matters beyond performance.** `[BetJournal] UNDECLARED balance discontinuity` producing silence is
> a load-bearing *result* in mini-plans 05 and 06 and in INC-003 — and CLAUDE.md states outright that its
> silence "is evidence". On this path the silence was structural. **A sentinel that has been disarmed by a
> UI subscriber reads exactly like a sentinel that found nothing** — which is the same failure the T0 boot
> banner was added to prevent, arriving one layer further in: that banner proves the check was *compiled*,
> and nothing proved it was *comparing*.
>
> The declaration is also spurious on this path. DiceGame's `_wallet` is a display copy; the journal's
> writer during a delegated autobet is `SimulationService`'s own wallet. The reseed announces a jump on a
> wallet that is not the one being audited.

**Consequence for the very next run, stated in advance so it is not misread.** Coalescing moves the reseed
from once per bet to once per frame, so roughly nine bets in ten are now genuinely compared. **If
`[BetJournal] UNDECLARED balance discontinuity` appears, that is the sentinel working for the first time on
this path — not a regression introduced by these fixes.** Treat any such line as a finding to investigate on
its own merits.

**Open, for the developer to schedule:** whether the reseed should declare a discontinuity at all while the
autobet is delegated. Removing it unconditionally is not obviously safe — `ReseedWalletFromBankrollSource`
has other callers, and for a manual bet DiceGame's wallet *is* the writer — so this needs its own look.

#### P1c — VERIFICATION after both fixes (2026-08-30, same world, 5 full windows + 1 partial)

| Segment | before | after | factor |
|---|---:|---:|---:|
| BankrollSetBalance | 933.9 | **0.41** | **2,278×** |
| BetSettled (signal → DiceGame) | 382.7 | **189.7** | 2.0× |
| NonceAttempt | 47.3 | 27.5 | 1.7× |
| RegisterBet | 24.5 | 21.8 | 1.1× |
| ExecuteNext | 11.2 | 8.3 | 1.3× |
| PersistFinancialState | 10.1 | 8.0 | 1.3× |
| CasinoApplyBetResult | 3.9 | 1.2 | 3.2× |
| **TOTAL** | **1,414.4** | **257.4** | **5.5×** |
| bets per frame if idle | 12 | **65** | |

**Fix 1 did exactly what it claimed.** 933.9 → 0.41 µs. The dominant cost in the engine is gone.

**Fix 2 delivered half of what was predicted, and the prediction was wrong for a reason worth recording.**
I forecast ~38 µs on the assumption that the whole 382.7 µs was coalescible. It was not: roughly half was
per-FRAME work (the status line, the reseed, the panel fields — now amortized away) and roughly half is
per-BET work I had *deliberately kept* — `EmitSignal` marshalling plus the `BetExecuted` fan-out to
`BetHistoryContainer` and `PreviousWinnerNumbersGrid`. **The commit comment says in as many words that the
bet-history feed stays per bet; the numeric prediction was then made as though it did not.** *A forecast
that contradicts the design note sitting three lines above it is not a modelling error, it is not having
read your own work.*

**Everything else got faster too** — `NonceAttempt` 1.7×, `ExecuteNext` 1.3× — with no change to any of that
code. Consistent with the frame no longer being saturated: less cache pressure and no stalls behind a
synchronous write. A saturated frame makes *everything* in it look expensive.

**The sentinel finding, now with a result.** No `[BetJournal] UNDECLARED balance discontinuity` line
appeared — and this time that silence means something, because the coalesced reseed drops the baseline once
per frame instead of once per bet, so roughly six bets in seven are genuinely compared. **This is the first
run on this path where the sentinel's silence is evidence rather than an artifact.**

**Against the developer's goal.** 99 credits × 600X demands 9.9 bets/frame:

| target | bets/frame | cost/frame | verdict |
|---|---:|---:|---|
| 99 × 600X | 9.9 | 2.54 ms (15%) | **comfortable** — but see the cap below |
| 99 × 900X | 14.9 | 3.83 ms (23%) | needs `MaxBetsPerFrame ≥ 15` |
| 99 × 9000X | 148.5 | 38.2 ms | still out of reach |

> **`MaxBetsPerFrame = 10` is now the binding constraint, and raising it is finally the RIGHT move.** 99 ×
> 600X needs 9.9 of the 10 available — it fits with no margin at all, so any frame that runs slightly long
> drops bets and `Sim%` dips. §38.7 forbids raising this constant *as a response to saturation*; here the
> saturation was found and removed first, and the constant is what remains. That is the order the rule
> prescribes, not an exception to it.

**The next target is named:** the remaining 189.7 µs is 74% of what a bet now costs, and it is
`EmitSignal` plus two UI containers each doing a `Setup()` and a `MoveChild(item, 0)` per bet.

**Retracted, from the previous version of this paragraph: "~78% of those rows are created and evicted
without ever being drawn."** That is false, and reading the containers is what showed it. Both are already
POOLED — nothing is created per bet — and at 450 bets/s a row survives 100 bets ≈ 0.22 s ≈ **13 frames**, so
every row is genuinely drawn. The waste is not un-drawn rows; it is **per-bet layout churn inside one
frame** — up to seven `MoveChild` re-sorts of two containers where one would do. *The claim was invented to
make the number feel wasteful before anyone had looked at the code that produces it.*

**And the fix is not chosen yet, deliberately.** The per-bet work is `Setup()` (label/text writes) and
`MoveChild()` (container re-sort + relayout), and those have different fixes. Reading the two files cannot
say which dominates, so a `BetHistoryFeed` segment was added — marked from inside
`DiceGame.OnSimBetSettled`, which is legal because `EmitSignal` dispatches synchronously — to split the
container work from the signal's own marshalling. **One short run decides which of the two to attack, rather
than optimising one of them blind.** Prior art on these containers (pooling, the 100-entry cap) is
`ProjectDesignManual.md` §38.8–38.9.

#### P1d — the signal path split, and the sentinel's first real result (2026-08-30, 3 full windows)

| | µs/bet | share of the pair |
|---|---:|---:|
| **BetHistoryFeed** (2 pooled UI containers) | **215.8** | **98.7%** |
| BetSettled (Godot signal marshalling + rest) | 2.9 | 1.3% |

**Unambiguous.** The Godot signal itself is ~3 µs; the entire cost is `BetHistoryContainer` and
`PreviousWinnerNumbersGrid` doing a `Setup()` and a `MoveChild()` each, per bet. Whatever gets optimised
next, it is not the event system — which is the second time this plan's segment-splitting has cleared a
suspect that a coarser measurement would have left condemned.

**The continuity sentinel is silent, and it is now genuinely comparing every bet on this path.** Not six in
seven, as in P1c — the spurious `wallet_reseed` declaration is gone entirely from the delegated steady
state. This is the strongest evidence the journal has ever had on this path, and it says the journal is
continuous.

**An unexplained drift, reported rather than smoothed.** Total per-bet cost rose from 257.4 µs (P1c) to
296.4 µs (P1d) on the same world and configuration, and both runs also rose *within* themselves. The added
mark cannot account for it (~0.02 µs). No conclusion changes at this magnitude, and nothing here is being
chased — but **comparisons should be made within a run, not across them**, and if this trend continues past
a few hundred µs it becomes a finding of its own.

#### The goal is already met — stop optimising and go verify it

At **296 µs/bet**, the developer's target costs:

| target | bets/frame | cost/frame | share of 16.67 ms |
|---|---:|---:|---:|
| 99 credits × 600X | 9.9 | 2.93 ms | **17.6%** |

**That is the goal, and it fits.** The remaining 215.8 µs in the UI containers is real and worth taking
eventually, but it is now an OPTIMISATION rather than a blocker — and this plan's own §38.7 discipline cuts
both ways: *measure before optimising* also means *stop when the measurement says you are done.* Continuing
to tune a number that already clears the requirement is how a performance investigation turns into a
performance hobby.

What remains before the target can actually be run:

1. **`MaxBetsPerFrame` must rise.** 99 × 600X needs 9.9 of the 10 available — it fits with zero margin, so
   any frame that runs slightly long drops bets and `Sim%` dips below 100. At 296 µs, 20 bets/frame is
   5.9 ms (35%), which is a defensible cap with real headroom.
2. **The throughput can be validated WITHOUT buying 94 hardware credits.** Demand is `credits ×
   DevTimeScale`, so **7 credits × 9000X = 630 bets/s = 10.5 bets/frame** puts the engine in the same
   regime as 99 × 600X (594 bets/s, 9.9/frame) for a 40% power increase instead of 20×. What 99 credits
   changes that this does not is the *game-time bet spacing* (1.01 vs 14.3 game-seconds) — which matters to
   **P3**, not to throughput — and the mining power, which perturbs the world's difficulty permanently.
   *Buying the 94 credits is a gameplay decision, not a measurement requirement.*

#### ✅ P1e — THE TARGET REGIME RUNS AT Sim 100% (2026-08-30, 7 credits × 9000X, 7 full windows)

**`Sim:` held at a fixed 100% for the whole minute.** 7 credits × ×90 = **630 bets/s = 10.5 bets/frame**,
which is the same engine regime as **99 credits × 600X** (594 bets/s, 9.9/frame) — reached for the price of
2 hardware credits instead of 94, and a 40% power increase instead of 20×.

Per-bet mean **233 µs**; `BetHistoryFeed` ~178 µs (76%); ceiling 68–81 bets/frame against the 20 now
allowed. **The developer's goal is met and demonstrated, not merely projected.**

> **`Sim%` is the result here, and it does not depend on the profiler.** `CalendarTimeService.SimulationThrottle`
> is computed by the bet engine from what it retained, entirely independently of `BetCostProfiler`. So the
> instrument bug below cannot touch this conclusion — which is the only reason the conclusion survives it.

#### ⚠ The profiler was mis-attributing, and its own residue is what caught it

**`unaccounted` went NEGATIVE** — −7.1 µs (−3.0%) and −16.0 µs (−6.5%) in two of the seven windows. That is
arithmetically impossible when the parts are subsets of the whole, so the parts were not subsets.

**Cause.** `SimulationService` emits `BetSettled` from **four** sites and only one is inside a bet; the other
three are the auto-recharge restart and two manual-transfer paths. All four reach
`DiceGame.OnSimBetSettled`, which marks `BetHistoryFeed`. A mark arriving outside a bet attributed
`now − _segmentStart` — an interval reaching back to the *previous* bet — to that segment, with no bet total
to absorb it.

**Fixed** with an `_inBet` flag set by `BeginBet`, cleared by `EndBet`, honoured by `Mark`, and cleared by a
new `AbortBet()` on the insufficient-balance path — which matters precisely because the auto-recharge emit
follows that abort immediately.

**Blast radius, stated rather than waved away.** `BetHistoryFeed` has been over-attributed in every reading
since it was introduced (P1d and P1e), by roughly the size of the residue — order 5–15 µs of ~180. It does
not move any conclusion: the segment still dominates, the goal still clears, and no fix was chosen on the
strength of the contaminated digits. Earlier rounds (P1a–P1c) had no mark inside `DiceGame` and are
unaffected.

> **The residue earned its entire keep here.** It exists because a breakdown that forces its parts to sum to
> its whole cannot reveal its own overhead — and the same property turned an invisible mis-attribution into
> an impossible number on screen. **A profiler that normalised its segments would have reported a plausible,
> wrong attribution, and nothing would ever have said otherwise.** Build the check that can print an
> impossible value; it is the only kind that can tell you it is broken.

*Second-order note: `[BetCost] toggle built in this scene` printed twice this run — DiceGame was entered,
left for the hardware shop, and re-entered. Benign, and a useful confirmation that the announcement tracks
scene lifetime.*

#### P1f — 99 × 900X: steady state passes, and the dips are SPIKES (2026-08-30, 10 credits × 9000X)

10 credits × ×90 = **900 bets/s = 15 bets/frame**, the engine regime of **99 credits × 900X** (891 bets/s,
14.85/frame). `Sim:` sat at 100% almost throughout, **dipping briefly as low as 63%** before recovering.

Ten full windows: mean **334.6 µs/bet**, `BetHistoryFeed` **254.7 µs (76%)**.

**The dips are not saturation, and the arithmetic separates the two cleanly:**

| | |
|---|---|
| steady state, 15 bets × 334.6 µs | **5.02 ms/frame — 30% of 16.67 ms** |
| worst SINGLE bet in the run | **96.7 ms — 5.8 frames of budget inside one bet** |

A frame spending 30% of its budget is not saturated. **One bet costing 5.8 frames is a spike**, the backlog
clamp discards what it cannot simulate, and `SimulationThrottle` reports the discard honestly — which is
`Sim: 63%` doing exactly its job. The high-`NonceAttempt` windows (42.9, 38.8, 47.2 µs against a ~22 µs
baseline) are precisely the ones carrying `[Checkpoint] CAPTURED` lines, which points at the block-commit
path: `CaptureCheckpoint` plus a full `state.json` write (§38.8a already lists it as an unexplained
per-block cost).

> **A mean cannot show a spike, and the two have opposite fixes.** Averaged over thousands of bets the block
> path reads as a few µs and looks free; the `worst` column is the only place it was visible, and only as an
> unattributed total. So `NonceAttempt` is now split into the PoW attempt alone and a **`BlockCommit`**
> segment. *This is the third time in this plan that splitting a segment changed the conclusion — and each
> time the coarse reading was not wrong, merely unable to distinguish two things with different remedies.*

**A cost that ROSE, named rather than smoothed:** per-bet went 233 µs (7 credits) → 334.6 µs (10 credits),
against a prediction that it would fall slightly on better per-frame amortisation. `BetHistoryFeed` carries
it (178 → 255 µs). Two candidates, not separated by this data: more per-frame container churn at 15 appends
instead of 10.5, or the P1c effect in reverse — *a busier frame makes everything inside it measure slower.*

**Verdict on the developer's escalation:** 99 × 900X is **reachable in steady state today**. What stands
between it and a flat 100% is a per-block spike, not throughput. Next run measures `BlockCommit` directly.

#### P1g — the block-commit discrimination, and a finding RETRACTED (2026-08-30, 17 full windows)

**The split did its job.** `BlockCommit` separates cleanly into two populations, and the boundary is exactly
whether the player mined a block in that window:

| windows | `BlockCommit` |
|---|---|
| 8 with **no** `[Checkpoint] CAPTURED` | **0.044 – 0.051 µs** (i.e. the branch was not taken) |
| 6 with a player-mined block | **1.6 – 5.6 µs**, rising with the number of blocks |

`NonceAttempt`, now measuring the PoW attempt alone, fell from a bundled **29.5 µs with spikes to 42.9 /
38.8 / 47.2** to **16.9 µs, range 14.6 – 20.9**. The variance moved out of it exactly as predicted. Mean
`BlockCommit` is **0.97 µs** — the block path is a pure spike, invisible in any average, which is why it
needed a column rather than a footnote.

**The asymmetry is explained, and verified in code rather than assumed.** Three windows carry a
`[Checkpoint] CAPTURED` line while reading `BlockCommit ≈ 0.05 µs`. `SimulationService` calls
`CaptureCheckpoint()` from **four** sites: the player's bet, a bot's bet, `DriveFounderMining`, and
`DriveScheduledMining`. The last two run **outside `ExecutePlayerBetOnce`** — so when Satoshi, Hal or the
scheduled network solves a block, the checkpoint costs the same tens of milliseconds **in the same frame**
while being structurally invisible to a profiler scoped to the player's bet. *Those dips are real, are
caused by the same work, and cannot be attributed by this instrument at all.*

> ### ⛔ RETRACTED — "the cost ROSE from 233 µs at 7 credits to 334.6 at 10"
>
> Recorded in P1f as a finding against prediction, and **it was noise.** This run is the same configuration
> — 10 credits × 9000X, same world, no code change but one added mark — and reads **221.3 µs**, *below* the
> 7-credit figure it was supposed to have risen from. `BetHistoryFeed` moved with it: 254.7 → **167.9 µs**.
>
> | | within one run | between runs, same config |
> |---|---|---|
> | spread | 213.9 – 245.3 µs (**±7%**) | 221.3 vs 334.6 (**34%**) |
>
> **The instrument is precise and not accurate across sessions.** Window-to-window it is tight enough to
> trust; run-to-run it moves by more than most of the effects this plan has been discussing. The cause is
> not identified — machine state, editor state, thermal, background load are all candidates and none is
> established.
>
> **The methodological consequence, which now binds everything above:** *compare only within a run.* Any
> cross-run claim needs an **A–B–A crossover** — the design §38.8a already used on this exact codebase for
> exactly this reason — and P1f's comparison was a bare A-vs-B. **A number that stays stable across 17
> windows looks authoritative and says nothing about the next session.**

**What is still unattributed.** Several windows show a worst bet of **15–21 ms with `BlockCommit ≈ 0`** —
W2 (17.2 ms), W13 (20.8 ms), W16 (15.5 ms). No player block, and an external checkpoint cannot inflate a
*bet's own* timing since it runs after the bet loop. **Leading candidate: a garbage collection.** This world
holds ~100k bet records in memory and a gen2 pass over that heap is comfortably tens of ms.

**Now instrumented, cheaply enough to be free.** Each report adds a GC line: gen0/gen1/gen2 collections in
the window, how many bets overlapped a collection, and — the question that actually matters — **whether the
WORST bet overlapped one**. A window's totals cannot answer that; they are spread over 5,000 bets, so the
worst bet carries its own flag. `GC.CollectionCount` is a field read, two per bet against a 220 µs bet.

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

#### P3 — the scanner, and the regression it found on its first run (2026-09-10)

`Tools/verify-bet-journal.js`. Runs the four assertions over the retained journal, per segment and over a
scoped tail (`--last N`, `--credits N`).

**A4 turned out to be checkable, which it did not look like.** The journal never records which bet mined
which block, so "no bet after the clock" reads undecidable after the fact. But `ExecutePlayerBetOnce`
derives the block's timestamp from **the same `tsUtc` it gave the bet** — so a player-mined block's
timestamp equals its mining bet's to the millisecond, and that join recovers every commit point. **42 of 42
player-mined blocks in the retained journal matched exactly.** The timestamp pipeline is sound end to end.

**A1 and A2 pass, and A2 is the more interesting of the two.** The per-segment table reads
`20.000 s → 14.286 s → 10.000 s` implied spacing — i.e. **exactly `SpeedMultiplier / credits` at 5, 7 and
10 credits**, tracking the hardware across three playtests with no tuning. That is the writer fix working.
A1 shows 9 same-timestamp groups of size **2** in 205,180 records (0.009%), against the pre-fix shape of
groups of 7–10 spaced 150 game-seconds.

> ### 🔴 A3 FAILS — 114 timestamp regressions, and we caused them
>
> Timestamps run **backwards** in write order: median jump **−20.3 game-seconds**, worst **−102.7**. They
> appear in segments 20–28 and in **none** of segments 8–19.
>
> **The mechanism.** Back-dating spans `(planned − 1) × interval × SpeedMultiplier` game-seconds, but the
> clock only advances `simDelta × SpeedMultiplier × ` **`SimulationThrottle`**. When the throttle is below
> 1 — every one of the `Sim:` dips already recorded in P1f/P1g — the back-dating reaches **further back
> than the clock moved forward**, and the frame's first bet lands before the previous frame's last.
>
> | | regression threshold | at 10 credits × 9000X |
> |---|---|---|
> | `MaxBetsPerFrame = 10` | throttle < 0.60 | span 90 s vs 150 s advance — dips to 63% stayed just clear |
> | `MaxBetsPerFrame = 20` | **throttle < 0.93** | span 140 s vs 150 s — **almost any dip regresses** |
>
> **Raising `MaxBetsPerFrame` to 20 converted a latent bug into a frequent one**, which is exactly where
> the segment boundary sits. Predicted magnitudes for throttle 0.9 → 0.63 are −5 s → −45 s; the observed
> median is −20.3 s. The mechanism is confirmed, not merely plausible.
>
> **§2.2 of this plan reasoned about the risk of USING the throttle and never about the risk of not using
> it.** It rejected the exact-phase variant because recomputing the clock's advance "risks disagreeing with
> what the calendar did, and a disagreement in the wrong direction **future-dates a bet**". True — and the
> disagreement in the *other* direction past-dates one, which is what shipped. *A hazard analysis that
> considers only the failure mode of the option it is rejecting has not compared anything.*
>
> **Proposed fix — clamp, do not predict.** Remember the clock value at the end of the previous frame and
> never back-date a bet before it. This needs no throttle forecast (the aggregate is power-weighted across
> bots and is not known until after `TickBots`), it is exact rather than approximate, it enforces
> monotonicity directly instead of inferring it, and at throttle 1 it changes nothing.

#### P3 — THE FIX (implemented 2026-09-10, awaiting its verification run)

`SimulationService.ClampedStepGameSeconds(nominalStep, planned, clockNowUtc)` — the batch's span may not
exceed the game-time the clock actually moved this frame. `_previousFrameClockUtc` is the anchor, advanced
**after** both settle paths have read it (the bots settle in the same frame off the same clock; advancing it
beside the player's loop would hand them a zero-width window and collapse their spacing to nothing).

Reset to `MinValue` on run start and stop, meaning *no anchor yet* → nominal spacing. Without that a run
would clamp its first frame against an anchor hours of game time stale.

**Verified arithmetically against the measured failure before asking for a playtest** — 10 credits × 9000X,
`planned = 15`, nominal step 10 game-seconds:

| throttle | clock advance | span before | span after | regression |
|---:|---:|---:|---:|---|
| 1.00 | 150.0 | 140.0 | 140.0 | none — **the clamp never binds at full retention** |
| 0.80 | 120.0 | 140.0 | 120.0 | was −20.0 s, now 0 |
| 0.63 | 94.5 | 140.0 | 94.5 | was −45.5 s, now 0 |
| 0.25 | 37.5 | 140.0 | 37.5 | was −102.5 s, now 0 |

The 0.25 row reproduces the journal's observed **worst** regression of −102.7 s to within 0.2 s. *The
mechanism is not merely consistent with the data — it predicts its extreme.*

**One consequence to expect in A2, so it is not misread as a new fault.** In a throttled frame the spacing
is now *compressed* below nominal, because the bets genuinely occupy less game-time than nominal — the clock
did not advance that far. So the scanner's median may sit slightly under `SpeedMultiplier / credits` on a
run with many dips. **That is the fix working, not A2 failing.** §2.2 already refuses to claim uniformity;
this widens the tail on the low side as well as the high.

**Standing use.** `node Tools/verify-bet-journal.js --last 20000 --credits N` after any run that matters.
A1/A3 are absolute; A2 is a median and must carry a one-to-two-interval frame-boundary tail (§2.2) — a
distribution uniform to the tick would mean the scanner is measuring something the engine does not do.

#### ✅ CLOSING RUN — P3 verified and P1's last spike attributed (2026-09-10, 27 windows, ~137k bets)

**A3 PASSES: zero regressions over 100,000 bets**, against 114 before the clamp. `A1` and `A4` hold, and
**A2 still reads exactly 10.0000 s with 95,556 of 99,979 gaps exact to the millisecond** — the clamp binds
in the ~4% of frames that were throttled and nowhere else, which is precisely the design.

**The spike sources separate perfectly, with no overlap at all:**

| | windows | worst bet | GC overlapped the worst bet |
|---|---:|---|---:|
| with a player-mined block | 12 | **20.1 – 73.3 ms** | 1 of 12 |
| without one | 15 | **7.1 – 13.5 ms** | **7 of 15** |

Every block window is ≥ 20.1 ms; every non-block window is ≤ 13.5 ms. **The block commit is the primary
spike and it is now proven rather than inferred** — the previous run could only show that high `BlockCommit`
and checkpoints coincided; this one shows the worst-bet *distributions* do not intersect.

**The GC hypothesis was right in kind and wrong in magnitude, which is worth separating.** GC lands on the
worst bet in **30% of windows against ~0.1% expected by chance** — so it is unambiguously a real spike
source, not noise. But it is a **secondary** one at 8–13 ms, and P1g predicted it would account for the
15–21 ms spikes seen in no-block windows. In this run no such spikes exist: the no-block ceiling is 13.5 ms.
*The prediction identified the mechanism and misjudged the size, and those are different kinds of being
right.* The earlier 15–21 ms no-block readings are best explained by the same 34% cross-run variance P1g
documented — which is exactly why that entry made within-run comparison the rule.

**Net picture of a bet, closing P1:**
- steady state ~265 µs, of which `BetHistoryFeed` is ~76% (two pooled UI containers) — the one named,
  measured, unfixed cost, and an optimisation rather than a blocker;
- a **block-commit spike of 20–73 ms**, roughly 1–4 frames, on the ~1 bet in thousands that solves a block;
- a **GC spike of 8–13 ms**, a few times per 5,000 bets.

Both spikes are what the brief `Sim%` dips are. Neither corrupts anything: `SimulationThrottle` converts a
frame the engine could not fill into an honest wall-clock slowdown.

*(The `WASAPI: Current output_device invalidated` line in this run is Godot's audio driver reacting to a
device change on the machine. Unrelated to any of the above, recorded so it is not read as a finding.)*

### P4 — Clock synchrony at 99 credits

The developer's specific worry. With the fix in place, confirm that in-game **block intervals** and the
difficulty regulator's feed are unchanged between `(99, 100X)` and `(99, highest sustainable)`. That is the
invariance `DevTimeScale` claims, and it is now testable at a resolution that did not exist before —
because every bet finally has an instant of its own.

#### ✅ P4 — RESULT (2026-09-12, 99 real hardware credits, A–B–A′, 64 blocks)

**300X was substituted for 100X, and the reason is a cost the plan never priced.** A block takes
`TargetBlockSeconds ≈ 58,500` game-seconds (~16.3 in-game hours), so 20 blocks at 100X is **115 minutes for
one leg**. The invariance under test needs two scales with a large ratio, not one specific pair, so the legs
are **300X / 900X / 300X** — a 3× ratio, both legs unsaturated, ~95 minutes total. *(Even that under-ran:
the legs took about double the estimate, because the estimate used the historical median solve time from a
much lower-power era rather than the regulator's actual target.)*

Structure, recovered entirely from the new `devTimeScale` column — 10 blocks settle (×9), 20 A (×3), 13 B
(×9), 21 A′ (×3). Configured power was **110.1–110.5 in every leg**, so nothing drifted underneath.

**The settle leg earned its place:** difficulty **89,713** there against **61,809 / 61,859 / 63,865** in the
three measurement legs. The 10× power jump's transient was real and was correctly discarded.

| | pooled 300X (A+A′) | 900X (B) |
|---|---:|---:|
| blocks | 41 | 13 |
| aggregate realized power | 95.67 | 121.64 |
| configured power | 110.16 | 110.52 |
| **realized / configured** | **0.869** | **1.101** |
| simulated-time retention | 1.000 | **0.943** |

**900X / 300X = 1.267, 95% CI [0.68, 2.36] — the bracket contains 1.00, so no violation is detectable.**
Across all 54 measurement blocks realized power is **100.79 against 110.24 configured (0.914 ± 13.6%)**: the
regulator delivers what it prices.

> **The sharpest form of the result is the retention row.** Leg B ran at **94.3% retention** — the engine
> genuinely could not simulate 5.7% of the time offered — **and its in-game block interval still matched the
> unthrottled 300X legs.** That is R2-C1's entire claim demonstrated rather than asserted: the clock slowed
> in wall-clock terms instead of the in-game dynamics distorting.

**Stated plainly: this test has LOW POWER and passing is not proof.** The 95% bracket only excludes effects
below ~0.68× or above ~2.36×; a 20% distortion would pass unnoticed. The binding constraint is the
≈exponential solve-time distribution — the same-scale legs A and A′ differ from each other by as much as
either differs from B (0.797 vs 0.947 realized/configured). **Tightening it to ±10% needs ~100 blocks per
leg, which at 300X is over six hours.** The honest verdict is *consistent with invariance at a resolution of
roughly ±35%*, not *invariance confirmed*.

**A statistic that had to be thrown away, recorded because the reasoning recurs.** The first pass reported
mean `realizedPower` per leg — 484 / 253 / **1,228** against ~110 configured, which reads as a spectacular
violation. It is an artifact: `realizedPower = difficulty × clockSpeed / solveSec` is **inversely**
proportional to an ≈exponential variable, and `E[1/X]` diverges for an exponential, so the mean is whatever
the single fastest block was. Leg A′'s maximum was **19,184**. The aggregate estimator
`Σdifficulty × clockSpeed / Σ solveSec` pools every block and is well-behaved; it gives 1.101 where the mean
gave 11.1×. *Averaging a per-item rate is not the same as computing the rate over the pooled total, and the
difference is largest exactly where the denominator is heavy-tailed.*

#### P3 at the spacing only 99 credits can produce

The case §2.1 singled out and no proxy could reach: **`A2 = 1.0100 s measured against 1.0101 s expected`**,
with 119,803 of 149,998 gaps exact to the millisecond. A3 passes with **0 regressions** over 150,000 bets
and A4 joins every player-mined block. **Per-bet cost is 276.5 µs at 99 credits against ~265 µs at 10** —
the cost is per BET, not per credit, as the model assumed but had never checked at the cap.

**A1 now fails at 1 group of 2 bets in 150,000 (0.001%), and the clamp caused it.** When a frame is throttled
hard the clamp compresses `stepGameSeconds` toward zero, and two adjacent bets can round onto the same tick.
**This is the trade the clamp makes and it is the right one** — an unbounded ordering corruption exchanged
for a resolution artifact three orders of magnitude rarer than the defect §1 set out to fix. *⛔ The mechanism
named here was wrong — see the escalation entry below. The pairs are not sub-tick collapse inside a batch,
and the one-tick floor built on this sentence did not close them.*

#### Escalation — 99 credits × 900X → 3000X, and the A1 prediction FALSIFIED (2026-09-13)

One continuous autobet, 900X → 1000X → 2000X → 3000X, profiler armed throughout. Legs recovered by the
`devTimeScale` column in the block trace and by a rate changepoint in the profiler CSV (the 2000X → 3000X
boundary falls at 02:27:53 UTC).

| leg | bets/s measured | demanded | delivered | block-trace retention | µs/bet |
|---|---:|---:|---:|---:|---:|
| 900X | 877 | 891 | — | 1.000 (1 block) | 221.0 |
| 1000X | 996 | 990 | 100% | 1.000 | 226.8 |
| 2000X | 1,955 | 1,980 | **98.7%** | **0.988** | 173.5 |
| 3000X | 2,053 | 2,970 | **69.1%** | **0.691** | 168.9 |

**Two independent instruments agree to the third digit** — the profiler's delivered-rate ratio and the
block trace's simulated-time retention were computed by different code from different data. The developer's
by-eye readings match too: `Sim:` pinned at 100% through 1000X, occasional dips at 2000X, ~72% at 3000X.

**Verdict on the escalation: the practical ceiling at 99 credits is about 2,000 bets per second, i.e.
2000X.** Past it throughput barely moves — 3000X demands 52% more than 2000X and delivers 5% more. The extra
demand becomes wall-clock slowdown, not bets, which is R2-C1 doing exactly its job.

**The 3000X prediction (~80%) missed by 11 points, and the miss is informative.** 80.8% was `40 ÷ 49.5` —
the cap binding at 60 fps. Both observations at 3000X, the delivered rate AND the retention, are fitted
exactly by one frame rate: **≈51 fps**, with the cap binding every frame (`40 × 51.3 = 2,053`;
`0.404 ÷ 0.585 = 0.691`). *Derived, not measured* — nothing times the whole frame. At that rate a frame is
~19.5 ms of which the bets are ~6.8 ms; the rest is outside every instrument this plan has built. 2000X is
consistent with any frame rate ≥ 49.5 fps, so these data **cannot say whether the frame slowed because of
3000X or was already near 51 fps throughout.** Consequence: *do not raise `MaxBetsPerFrame` again on this
evidence.* Cap and frame rate bind jointly, and which to move needs a whole-frame timing of
`SimulationService._Process`, which does not exist yet.

**Within the run, per-bet cost FALLS ~22% from 1000X to 2000X — and uniformly.** `BetHistoryFeed` ×0.78,
`RegisterBet` ×0.78, `NonceAttempt` ×0.74, total ×0.785. The tempting reading — the UI containers' deferred
relayout amortising over more appends per frame — is refuted by that uniformity: a UI-specific effect would
not speed a proof-of-work attempt by the same factor. A whole-loop effect (cache warmth, CPU power state under
sustained load) fits; nothing here separates those. The direction is trustworthy because it is within one
run, and it is the opposite of P1f's retracted cross-run "rise".

**The journal regime flagged as never exercised is benign.** At ~2,000 bets/s the ~200k retained records
cycle roughly every 100 seconds. After the run: **202,057 records in 21 segments** (inside the documented
190k–210k band), 54.8 MB, lifetime rollup `IsComplete = true` and updated during the run, and `RegisterBet`
never exceeded **21.8 µs** in any window — pruning under continuous load adds no visible per-bet spike.

> ### 🔴 A1 PREDICTION FALSIFIED — the one-tick floor fixed a mechanism that never occurred
>
> Predicted: A1 returns to 0 under the escalation. Observed: **864 same-millisecond pairs** in 202,057 bets.
>
> **The tick-level audit settles the mechanism completely:**
>
> | consecutive-bet delta | count |
> |---|---:|
> | negative (any size) | **0** |
> | exactly 0 ticks | **448** |
> | exactly +1 tick | **416** |
> | +2 ticks up to 1 ms | **0** |
>
> Every pair has size exactly 2, and **857 of 864 are followed by compressed spacing while the gap before
> them is nominal**. Each pair is the entry into a clamped frame. `step = available ÷ (planned − 1)` maps the
> batch onto the **closed** interval `[previousFrameClock, clockNow]` — and the left endpoint is already
> occupied, because the previous frame's last bet was stamped at backdate 0, which *is* that clock. The
> clamped frame's first bet lands on it. The +1 tick variant is the same collision with `AddSeconds`
> truncating the double back-date's fractional tick.
>
> The one-tick floor addressed sub-tick collapse *inside* a batch. That would produce groups of up to
> `planned` bets; the data contains none larger than 2. **The floor was built for a mechanism reasoned rather
> than observed, and the observation that would have ruled it out — every group is exactly size 2 — was
> already in P4's output.**
>
> **And a fragility found along the way:** A3 has never failed at tick resolution only because truncation
> errs *forward*. Had the back-date rounded to nearest, roughly half of these 416 would be one-tick
> **regressions**. *Monotonicity has been held by a rounding direction nobody chose.*
>
> **Fix, proposed and not yet built:** map the batch onto the **half-open** interval — `step = available ÷
> planned`, so the first bet lands one full step after the previous frame's clock (~0.85 game-seconds at
> 3000X, not 0–1 tick). The unclamped branch needs a strict `<` for the same reason, and the floor's
> condition becomes `planned` ticks rather than `planned − 1`. With a full step of separation the rounding
> direction stops mattering at all.

**A scanner gap found by the same audit.** `verify-bet-journal.js` compares timestamps at **millisecond**
resolution for A1 and A3, because it truncates for the A4 block join. A sub-millisecond regression would be
invisible to A3, and A1 is stricter than the property it names (+1 tick is technically distinct). Nothing was
hidden this time — the tick audit found 0 regressions — but it could be. **Proposed:** A1 and A3 at tick
resolution, milliseconds kept only for the A4 join, and a separate near-collision diagnostic (consecutive
bets under 1% of nominal spacing) so this defect is caught whichever way the rounding falls.

#### Half-open clamp + tick-resolution scanner — BUILT (2026-09-13), verified by model, awaiting a run

**The clamp.** `ClampedStepGameSeconds` now spreads a clamped batch over `(previousFrameClock, clockNow]` —
`step = available ÷ planned` — so the first bet lands one full step after the previous frame's clock. The
unclamped branch requires `(planned − 1) × nominal` to be strictly *less* than the advance, for the same
reason. **The conditional one-tick floor is removed, not adjusted** (the escalation entry proposed moving its
threshold to `planned` ticks; that was wrong too): under the half-open interval, whenever a frame can hold
`planned` distinct ticks, `available ÷ planned` already is at least one tick, so the floor could never change
the result. At the true limit — a frame advancing under `planned` ticks, 4 µs of game time at 40 bets — bets
may still share an instant, but the span is strictly shorter than the advance and truncation only moves a
timestamp later, so ordering and the clock bound hold even there.

**The scanner.** A1 counts exact duplicates at tick resolution; **A1b** counts consecutive bets 1–10 ticks apart
(`NEAR_COLLISION_TICKS`); A3 compares at tick resolution; milliseconds survive only for the A4 block join.
Run over the escalation journal it reproduces the hand audit exactly — **448 A1 groups, 416 A1b pairs, 0 A3
regressions** — and A2 now reads 1.0101 s rather than 1.0100, because the median is no longer truncated.

**Verified by a model before any playtest — and the model had to earn that first.** A scratchpad script
reimplements the player loop, the calendar advance, the backlog clamp, the throttle's one-frame lag and .NET 8
`DateTime.AddSeconds` truncation exactly, and was required to reproduce the *measured* defect with the
committed formula before being allowed to judge the new one.

- **Round 1 did not pass.** With frame deltas of exactly 1/60 or 1/30 s it matched rate (2,064/s), retention
  (0.696), zero regressions and the pair count's magnitude (686) — but put **every** pair at +1 tick, against
  the journal's 448 exact / 416 +1. Recorded as a failure, not rounded into agreement.
- **Round 2 tested the explanation.** A 2% multiplicative jitter on the frame delta — Godot's real delta is not
  a constant — gives **480 exact / 477 +1 tick** (a second seed: 468 / 429), **0 regressions, 2,051 bets/s,
  0.691 retention**. The split appears exactly as predicted, so the truncation mechanism is confirmed rather
  than assumed: with too-regular inputs `available` takes so few binary values that all of them round the
  same way.

Frame-time variability in the model is a 16.67 / 33.33 ms vsync alternation, long with probability 0.169 —
fitted because it reproduces BOTH the 3000X delivered rate and its retention. *A hypothesis about the machine,
not a measurement of it,* though it also predicts the observed pair frequency (~14% of frames), which it was
not fitted to.

| scenario (99 credits) | formula | regressions | exact dup | +1 tick | 2–10 ticks | A2 median |
|---|---|---:|---:|---:|---:|---:|
| 3000X, jitter 2% | closed + floor (committed) | 0 | 480 | 477 | 0 | 1.0101 |
| 3000X, jitter 2% | **half-open** | **0** | **0** | **0** | **0** | 1.0101 |
| 2000X, jitter 2% | closed + floor | 0 | 2,009 | 1,922 | 0 | 0.8650 |
| 2000X, jitter 2% | **half-open** | **0** | **0** | **0** | **0** | 0.8434 |
| 900X / 300X, jitter 2% | both | 0 | 0 | 0 | 0 | 1.0101 |

Half-open is clean in all 14 scenario × formula runs. Its only cost is that a clamped frame's spacing is
`(planned − 1) ÷ planned` of what the closed interval gave — 2.5% at 40 bets — visible in the 2000X row. **That
row's sub-nominal A2 is the model's heavy-clamping assumption at 2000X, not an observation**: no real 2000X
journal exists, because the escalation's retained ~200k records were written entirely during the 3000X leg.

#### ✅ Half-open clamp VERIFIED (2026-09-13, 99 credits, 2000X → 3000X)

The retained journal — 202,684 bets — was written **entirely during the 3000X leg** (80 profiler windows ×
5,000 bets exceed the retention cap), the exact regime in which the closed interval produced 864 pairs.

| assertion (tick resolution) | before the fix | after |
|---|---:|---:|
| A1 duplicate instants | 448 | **0** |
| A1b near-collisions (1–10 ticks) | 416 | **0** |
| A3 regressions | 0 | **0** |
| A2 median spacing | 1.0101 s | **1.0101 s** |
| A4 block/bet join | 2 of 2 | **1 of 1** |

**A2's secondary figure matched the model before the run existed:** 160,054 of 202,544 gaps within 1 ms of
nominal = **79.0%**, against **79.2%** from the half-open model at 2% frame jitter. A number the model was not
fitted to, landing within 0.2 points.

Throughput: 2000X delivered 1,971 of 1,980 bets/s with block-trace retention **0.996**; 3000X delivered 2,130/s
at **0.743** (the developer read ~75% by eye). That is above the escalation run's 0.691 — **a cross-run
difference, not attributable to anything**: the fix touches timestamps, not throughput, and P1g measured 34%
between sessions. The profiler's `unaccounted` residue never went negative (minimum 0.061 µs).

#### Found while verifying — the scanner's "session breaks" are not breaks, and they expose a 0.62% clock overspend

The scanner excluded **139 gaps larger than 10× nominal "as session breaks"** from a single continuous
autobet — there were no session breaks. The gaps are frame-aligned (never fewer than 40 bets apart, one capped
frame), range 10–69 game-seconds (median 19.7), and hold **1.49% of the journal's game time**. The label was
a claim, and it was false.

Three tests, recorded with their outcomes because two of them failed:

1. **Throttle overspend followed by an under-advancing frame — REFUTED.** That predicts compressed spacing
   immediately after each gap. The frame after a gap is at exact nominal in **139 of 139**.
2. **Holes versus clamped frames, counted — DISCARDED as badly posed.** 1,022 clamped frames against 139 holes,
   but the holes were counted only above 10× nominal and the clamps at any size. Asymmetric thresholds; the
   comparison meant nothing.
3. **The lagged-ratio mechanism, tested through the mean of r — CONFIRMED.** With the backlog saturated the
   cap executes 40 bets, 40.40 game-seconds, every frame, while `CalendarTimeService` advances `40.40 × r`
   with `r = delta_N ÷ delta_(N−1)` — it multiplies THIS frame's delta by a retention RATIO measured on the
   PREVIOUS frame. Each frame's `r` is recoverable from the journal's own shapes: a boundary gap `G` gives
   `r = (G + 39.39) ÷ 40.40`, a clamped frame with step `s` gives `r = 40·s ÷ 40.40`, a nominal frame `r = 1`.

| over ~5,067 frames (1,286 up-steps, 1,022 clamped, 2,759 nominal) | |
|---|---:|
| Σ ln r — up-steps / clamped frames | +120.56 / −114.66 |
| **geometric** mean of r | 1.00117 |
| **arithmetic** mean of r | 1.00738 |
| arithmetic ÷ geometric | **1.00620** |
| net overspend from the total span, computed independently | **0.625%** |

The geometric mean of the TRUE `r` must be 1: `Σ ln r` telescopes to `ln(delta_last ÷ delta_first)`, and the
frame time did not grow 365-fold over five minutes. So the reconstruction's 1.00117 is its own bias, ~0.12% —
and dividing it out leaves **0.620% against 0.625%** measured by a different route. The up-step half of each
fluctuation is a hole; the down-step half is a clamped frame.

**Why it drifts at all: Jensen's inequality.** The arithmetic mean of a ratio of fluctuating positive
quantities exceeds 1 even when nothing trends. A one-frame-lagged **multiplicative** correction therefore
overspends systematically; an **additive** one could not.

**Consequence: R2-C1's invariance leaks by ~0.6% when the backlog is saturated.** In-game time passes ~0.6%
faster than the mining attempts justify, so in-game block intervals read ~0.6% long. Far below P4's ±35%
resolution, and **zero whenever retention is 1** — at 99 credits, everything up to 1000X, which is where
normal play lives. It exists only in the regime this plan went looking for.

**Fix, not built — a change to R2-C1's contract, which deserves its own decision:** carry the lagged quantity
additively, advancing the calendar by the previous frame's *retained simulated seconds × SpeedMultiplier*
rather than *this frame's delta × last frame's ratio*. The one-frame lag stays; the bias goes, because
`Σ advance = Σ retained` exactly.

**The scanner's label is corrected** to say what an excluded gap may be, instead of what it was assumed to be.

*Two rules. A lagged multiplicative correction applied to a fluctuating base drifts, and the drift is
Jensen's, not noise. And a label on an exclusion is a claim about the excluded data — this one quietly
filed 1.49% of game time under the wrong heading for two runs.*

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
