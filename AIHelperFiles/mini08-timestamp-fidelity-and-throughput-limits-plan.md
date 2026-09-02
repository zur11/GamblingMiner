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

**The next target is named and measured:** the remaining 189.7 µs is 74% of what a bet now costs, and it is
two UI containers appending a row each, per bet, to displays capped at 100 entries — at 450 bets/s, ~78% of
those rows are created and evicted without ever being drawn. Batching a frame's appends into one relayout
would keep every row and remove the per-append overhead. Prior art on these two containers (pooling, the
100-entry cap) is `ProjectDesignManual.md` §38.8–38.9.

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
