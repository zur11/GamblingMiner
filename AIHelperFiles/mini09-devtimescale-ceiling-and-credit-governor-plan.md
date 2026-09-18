# Mini-Plan 09 — The absolute DevTimeScale ceiling, and a governor that yields to hardware credits

**Series note:** ninth entry of the *mini-plan* series, following
`mini08-timestamp-fidelity-and-throughput-limits-plan.md`, whose close-out (§4.9) left this as its first open
item.

**Status:** ✅ **COMPLETE (2026-09-18)** on branch `mini09-devtimescale-governor`, awaiting merge to `main`.
Close-out: **§8**.

**Objective, in two halves that must be done in this order.**

1. **Find the absolute limit** at which the simulation can run fluidly — not "about 2,000 bets/s at 99
   credits", but a measured capacity, with the frame accounted for and the known clock overspend removed.
2. **Build a self-limiting DevTimeScale** that gives priority to hardware credits. The developer picks a
   *requested* scale; the game runs at the highest *effective* scale the current credits allow. Buying credits
   mid-autobet lowers the effective scale step by step; discarding them raises it back towards the request;
   at 100X nothing ever changes.

The second half cannot be designed before the first: its central constant *is* the first half's result.

---

## 1. What mini-plan 08 already established — and what it could not

**Measured** (mini-plan 08, "Escalation" and "Half-open clamp VERIFIED"):

| 99 credits | demanded bets/s | delivered | retention |
|---|---:|---:|---:|
| 1000X | 990 | 100% | 1.000 |
| 2000X | 1,980 | 98.7–99.5% | 0.988–0.996 |
| 3000X | 2,970 | 69–74% | 0.691–0.743 |

- **Demand is `Σ credits × DevTimeScale` bets per real second**, one term per running bet engine. The
  measured rows match it to within 1% (`99 × 20 = 1,980` demanded at 2000X).
- **Per-block spikes are a second, independent limit.** At 10 credits × 9000X the steady state used 30% of
  the frame, yet `Sim:` dipped to 63%: the worst single bet cost 96.7 ms, carried by block commits. Founder
  and scheduled-network checkpoints cost the same and are **invisible to `BetCostProfiler`**, which is scoped
  to the player's bet (P1f, P1g).
- **The run-to-run spread is 34%**, against ±7% within a run (P1g). *Every comparison below is within one
  run, A–B–A, or it is not a comparison.*
- **R2-C1 overspends 0.620% under a saturated backlog** (Jensen's inequality on a lagged multiplicative
  ratio), and produces the frame-edge holes the scanner excludes from A2.

**Derived, NOT measured — the gap this plan closes first:**

- At 3000X both the delivered rate and the retention are fitted by one frame rate, **≈51 fps with
  `MaxBetsPerFrame` binding every frame** (`40 × 51.3 = 2,053`). That puts the bets at ~6.8 ms of a ~19.5 ms
  frame, and **the other ~65% is outside every instrument that exists.**
- So nobody knows whether the ~2,000 bets/s ceiling is **the cap** (`MaxBetsPerFrame × fps`; raising the cap
  would raise it) or **the CPU** (the frame is genuinely full; raising the cap would only lower fps). Mini-plan
  08 closed with *"do not raise `MaxBetsPerFrame` again on this evidence"*, and that stands until §3 P1 exists.

## 2. The model the governor will implement — written before any measurement

**Two constraints, and the effective scale is the tighter one.**

```
effective = min( requested,
                 floor( BetBudgetPerSecond / Σ credits of running engines ),   ← bets/s dimension
                 MaxGameSecondsPerRealSecond / 100 )                            ← game-time dimension
            clamped to ≥ 1
```

- **`BetBudgetPerSecond`** — the bets/s the engine sustains at `Sim: 100%`, **minus a safety margin** for the
  per-block spikes. Measured in §3 P3; its value is not written here (Standing Convention 15).
- **`Σ credits of running engines`** — the player's active node plus every running bot runner, because each
  runs its own loop inside the same frame. Founder and scheduled-network attempts are **not** separate terms:
  they are drained in proportion to those attempts, so they scale the *cost per bet* instead of the demand.
  That makes the budget **era-dependent** (§5).
- **The game-time dimension already exists** as `CalendarTimeService.MaxGameSecondsPerRealSecond`. §3 P3b
  decides whether it is the true ceiling for 1 credit, or whether per-block work saturates earlier.

**What the developer's example looks like under this model**, with a provisional budget of 2,000 bets/s
taken from §1 (illustration only; the real constant comes from P3):

| requested | credits (player only) | bets/s limit ÷ credits | effective |
|---|---:|---:|---|
| 9000X | 2 | ×1000 | **9000X** (ceiling binds) |
| 9000X | 22 | ×90.9 | **9000X** |
| 9000X | 23 | ×86.9 | **8600X** |
| 9000X | 50 | ×40 | **4000X** |
| 9000X | 99 | ×20.2 | **2000X** |
| 100X | 99 | ×20.2 | **100X** — unchanged |

**Why 100X can never be limited:** the most a world can run is five bettable nodes × `MaxAutoBetBaseAps`
credits. Unless the budget fell below that product, `floor(budget ÷ Σ credits) ≥ 1` always holds. §4
**asserts** it rather than trusting it.

## 3. Phase A — find the limit

### P1 — Time the whole frame (instrument, desk work)

**`FrameCostProfiler`**, DEBUG-only and disarmed by default, beside `BetCostProfiler` and armed from a toggle
next to the DEV time selector. **Per frame it records:**
- **The real frame time**, as the period between consecutive frames. *(Specified as "from `delta`"; built
  with `Stopwatch` instead, because `delta` describes the previous frame — see "P1 — BUILT".)*
- **`SimulationService._Process`, split into:** player bet loop · bot loop · founder drive · scheduled drive
  · everything else in the method.
- **Counts:** player and bot bets executed; founder and scheduled **PoW attempts**; checkpoints captured.
  *(Specified "by source"; built as a per-frame total. The segment timing already shows which drive paid for
  a spike, so a per-source count would add a column without adding an answer. Revisit if H4's run leaves a
  spike unattributed.)*
- **Whether `MaxBetsPerFrame` bound** that frame.

**Per report** (every N frames): p50 / p95 / max per segment, frames over 16.67 ms, and — the question that
matters — **the share of frame time spent outside `SimulationService._Process`** (rendering, UI, other
nodes' `_Process`).

**Pre-registered hypotheses, recorded to be falsified:**
- **H1:** at 99 × 3000X the simulation is ~35% of the frame, and fps is limited by work outside it.
- **H2:** the cap binds on nearly every frame there.
- **H3:** founder and scheduled PoW attempts are a small multiple of player bets in the current 2009 world.
- **H4:** every frame over 50 ms carries a checkpoint or a GC.

If H1 holds, raising `MaxBetsPerFrame` buys nothing, and the budget is set by the outside work. If it fails,
P3a has a lever.

#### P1 — BUILT (2026-09-16), awaiting its run

`Scripts/Diagnostics/FrameCostProfiler.cs`, armed by the **⏱ Frame cost** toggle beside the DEV time selector.

- **A frame** is the `Stopwatch` period between two consecutive `BeginFrame` calls: this frame's simulation
  plus everything the engine did before the next one. It is recorded one call late, when that period is known.
  Godot's `delta` is not used, because it describes the previous frame.
- **Six contiguous segments** of `SimulationService._Process`: `Recompute`, `PlayerLoop`, `BotLoop`,
  `FounderDrive`, `ScheduledDrive`, `Tail`. Whatever falls between marks is reported as unaccounted, never
  normalised away (BetCostProfiler's residue rule).
- **Per frame:**
  - player and bot bets, and whether `MaxBetsPerFrame` bound;
  - founder and scheduled PoW attempts;
  - checkpoints captured;
  - whether a GC ran during the period;
  - demand (`GetTotalActiveMiningPower() × DevTimeScale`) and retention.
- **Every `ReportEveryFrames` frames**, one block in the Godot editor's Output panel, with H1–H4 each on its
  own line, and one row in `user://logs/frame_cost_trace.csv` (DEV wall-clock telemetry like
  `bet_cost_trace.csv`, not on the wipe's delete list). A partial window flushes on disarm.
- **Three things it deliberately does not see, recorded so they are not mistaken for findings:**
  - the frame in which an autobet stops itself (discarded);
  - any period over 1 s (a discontinuity, not a frame);
  - the report's own cost, since the next frame starts counting after it.
- **Its own per-frame overhead has not been timed.** It is a handful of `Stopwatch` calls and GC counter reads,
  expected far below a millisecond. That is stated as an expectation, not as a measurement.
- Build clean, 0 warnings. Locale detector unchanged at 7 / 0.

**Run protocol — one continuous run, legs compared only within it.**

- **Setup:**
  - the current world;
  - **99 hardware credits** on the player, all in the private pool;
  - Stop-on-block OFF, auto-recharge ON;
  - **⏱ Bet cost OFF** (its per-bet overhead would contaminate the frame);
  - **⏱ Frame cost ON**, before starting the autobet.
- **Legs**, each held for at least three reports (~30 s):
  1. **1000X** — the unsaturated baseline;
  2. **2000X** — the measured knee;
  3. **3000X** — where H1/H2 were derived;
  4. **9000X** at 99 credits — deep saturation;
  5. **9000X at 1 credit** — the game-time axis alone (discard 98 credits in the hardware shop; the sim keeps
     running across the scene change).
- **End:** disarm the toggle (flushes the partial window), stop the autobet. The CSV carries every leg; the
  Output panel is not needed.

#### ✅ P1 — RESULT (2026-09-17, 99 credits, 2009-03 world, 42 reports, one continuous run)

The five legs ran as specified. **Deviation:** on returning from the hardware shop the ⏱ Frame cost toggle
showed OFF, and the developer took it as disarmed. The profiler was still armed — its state is static, and the
rebuilt toggle did not read it — so every leg was measured. The only loss is the final partial window, which was
never flushed. *Fixed in the same commit as this record: both diagnostic toggles now mirror their profiler's
state when the scene is built.*

Leg boundaries were recovered from the demand column. Reports straddling a change are excluded as transitions.
Figures are means of report means; frames are summed.

| leg | frames | fps | frame ms | sim ms | outside sim ms | bets/frame | cap bound | delivered/s | retention |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| DiceGame 1000X, 99 cr | 9,000 | 59.5 | 16.80 | 3.32 | 13.48 | 16.6 | 0% | 991 | 1.000 |
| DiceGame 2000X, 99 cr | 1,800 | 54.6 | 18.34 | 5.81 | 12.53 | 36.0 | 36% | 1,965 | 0.995 |
| DiceGame 3000X, 99 cr | 3,000 | 51.1 | 19.59 | 6.46 | 13.13 | 40.0 | 100% | 2,043 | 0.702 |
| DiceGame 9000X, 99 cr | 7,200 | 53.3 | 18.79 | 6.27 | 12.52 | 40.0 | 100% | 2,130 | 0.242 |
| **Hardware shop** 9000X, 99 cr | 600 | 58.6 | 17.08 | **1.83** | 15.25 | 40.0 | 100% | **2,342** | 0.266 |
| DiceGame 9000X, **1 cr** | 1,200 | 59.4 | 16.83 | 0.70 | 16.13 | 1.5 | 0% | 89 | **0.987** |

*A frame pinned near 16.7 ms is waiting for the display (the 1000X leg, the shop, 1 credit). There "outside sim"
includes idle time and is not work. Only the saturated DiceGame legs, whose frames run longer than 16.7 ms,
measure the outside work itself.*

**H1 — CONFIRMED, and the consequence recorded beside it was WRONG.** The sim is 33% of a saturated DiceGame
frame (6.3 of 18.8 ms, predicted ~35%). The other ~12.5 ms is outside the sim and does not grow with bets: 13.1 ms
at 3000X, 12.5 ms at 9000X. What this plan wrote in advance was *"if H1 holds, raising `MaxBetsPerFrame` buys
nothing"*. That does not follow. A large fixed outside cost means each extra bet costs **frame rate**, not
throughput. Across the saturated legs the frame fits
`frame ≈ 12.5 ms + 0.157 ms × bets per frame`, which reproduces them (40 bets → 18.8 ms, 2,128/s; 36 bets at
2000X → 18.2 ms predicted, 18.3 measured). **The ceiling is the cap, and moving it trades fps for bets/s.**

| minimum fps held | frame | cap that fits | bets/s — **model extrapolation, not measured** |
|---:|---:|---:|---:|
| 55 | 18.2 ms | ~36 | ~2,000 |
| 50 | 20.0 ms | ~48 | ~2,390 |
| 45 | 22.2 ms | ~62 | ~2,790 |
| 40 | 25.0 ms | ~80 | ~3,190 |

**So the absolute limit is not a hardware fact, it is a choice of how many fps count as fluid.** Past the choice,
the curve flattens towards `1 ÷ 0.157 ms ≈ 6,400 bets/s` at 0 fps.

**H2 — CONFIRMED.** The cap bound on 100% of saturated frames at 3000X and 9000X, and on 36% at 2000X, the knee.

**H3 — FALSIFIED in size, harmlessly.** At 99 credits the founders and the scheduled network make **0.136** PoW
attempts per player bet, a fraction, not the predicted "small multiple". At 1 credit the ratio is ~5, because
their power does not shrink with the player's. Either way both drives cost under 0.05 ms per frame: negligible
in this era. §5's era-dependence warning stands for later eras, where the scheduled network is far larger.

**H4 — FALSIFIED.** In the DiceGame legs, 14 of 19 frames over 50 ms carried **neither** a checkpoint nor a GC.
The Output panel's worst-frame lines (from `godot.log`) say where they happened: in **36 of 42** reports the
worst frame spent 11 ms or less in the sim. It is 35–95 ms spent **outside** it — render, UI, other nodes, the
OS — and this profiler cannot divide that time further. Inside the sim, **checkpoints do cost 30–43 ms** when
they land (4 observed), and **4 more in-sim spikes of 15–67 ms have no checkpoint and no GC**. Those are
unattributed; candidates, not findings, are a journal segment rotation or a throttled service save inside the
settle loop.

**Two findings nobody predicted.**

1. **The scene sets the per-bet cost.** In the hardware shop the same 40 bets per frame cost **1.83 ms**, against
   6.27 ms in DiceGame. That is ~0.11 ms per bet paid by DiceGame's bet-history UI, fed synchronously inside the
   settle loop — mini-plan 08's `BetHistoryFeed`, now seen from the frame side. The shop still delivered only
   2,342/s: its frame sits at the display rate, so there too **the cap is what binds**. A budget measured in
   DiceGame is conservative for every lighter scene.
2. **9000X is not flat even at 1 credit.** Retention was 0.987 with 0.7 ms of sim per frame, so this is not load.
   **Leading hypothesis, not yet tested:** `MaxBacklogSeconds` is a window in *simulated* seconds, so at ×90 any
   frame longer than `MaxBacklogSeconds ÷ 90` (22 ms) loses simulated time, and ordinary frame jitter crosses
   that. The window widens as the scale drops (33 ms at ×60, 67 ms at ×30). *This is jitter against a window, not
   saturation. §38.7's rule against raising `MaxBacklogSeconds` was written for saturation and does not settle
   this case either way.*

#### P3, redirected by P1

- **P3a becomes a cap sweep, and needs a runtime control first.** Changing `MaxBetsPerFrame` today means a
  rebuild, which makes a within-run A–B–A impossible and a cross-run comparison meaningless (34% spread). **Build
  a DEBUG-only runtime override** for the cap, then run DiceGame at 99 credits × 9000X (saturated, so the cap
  binds) through cap 40 → 60 → 80 → 40 in one run. That tests the table above.
- **P3b tests the backlog-window hypothesis:** 1 credit, ×90 → ×60 → ×30 → ×90 in one run. **Prediction:**
  retention rises towards 1.000 as the window widens, and the loss tracks the share of frames longer than
  `MaxBacklogSeconds ÷ scale`.
- **A decision only the developer can make, needed before §4:** the minimum fps that counts as fluid. It picks
  the cap, and the cap sets `BetBudgetPerSecond`.
- **§4's budget is scene-dependent in fact.** The simplest honest governor uses the DiceGame budget everywhere,
  which is conservative elsewhere. A per-scene budget is possible but is a separate decision, not an assumption.

#### Decisions (2026-09-17)

**D-09.1 — Minimum fps: 50.** The developer's choice, on the recommendation above. P1's model puts it at a cap
of ~48 and ~2,390 bets/s; P3a measures it instead of trusting the extrapolation.

**D-09.2 — P2 is deferred until after the governor, and its specification was flawed.** P2 said: advance the
calendar by *last frame's* retained simulated seconds. Reading the frame order to build it:
`CalendarTimeService` (autoload #3) advances **before** `SimulationService` (#17) in every frame. Under the
specified change, frame N's clock would move by frame N−1's retained time while frame N simulates its own
delta.
- **The two would disagree on every frame whose duration changes, saturated or not.** Today, at retention 1,
  they agree exactly. The half-open clamp would therefore compress or gap spacing continuously: a 0.62% bias
  that exists only under saturation, traded for timestamp distortion everywhere.
- **A lag-free version** has to evaluate every engine's backlog clamp *before* the clock advances, which means
  restructuring the frame, not editing one line.
- **Why deferring costs nothing:** the governor's purpose is to keep demand under the budget, so the saturated
  regime where the bias lives mostly stops occurring.

Re-measure the overspend once the governor runs, and build the restructure only if it is still worth its risk.
*A fix specified from the arithmetic alone skipped the question of WHEN, inside a frame, each side runs.*

#### P3 — protocol and predictions (registered before the run)

**Instrument added:** a DEBUG-only runtime override of `MaxBetsPerFrame`, set from a **Cap/frame** picker in
the DEV diagnostic column (40 / 48 / 60 / 80), with the cap in force added as `capPerFrame` to
`frame_cost_trace.csv`. Not persisted; RELEASE builds cannot set it.

**One session, two parts.** Frame cost ON before starting; Bet cost OFF; Stop-on-block OFF; auto-recharge ON.
Each step holds for **3 new `[FrameCost]` blocks** in the Godot editor's Output panel.

*Part 1 — P3b, the game-time window.* 1 credit, as the world stands after P1: DEV scale **9000X → 6000X → 3000X
→ 9000X**, cap 40.

| scale | frame length at which simulated time starts to drop (`MaxBacklogSeconds ÷ scale`) | predicted retention |
|---|---:|---|
| 9000X | 22 ms | ~0.987, repeating P1 |
| 6000X | 33 ms | higher; the loss tracks the share of frames over 33 ms |
| 3000X | 67 ms | ~1.000 |
| 9000X again | 22 ms | back to ~0.987 — the A–B–A check |

*Part 2 — P3a, the cap.* Buy back to 99 credits (the autobet keeps running; those reports are transitions),
DiceGame, **9000X**, so every cap binds: Cap/frame **40 → 48 → 60 → 80 → 40**.

| cap | predicted frame | predicted fps | predicted bets/s |
|---:|---:|---:|---:|
| 40 | 18.8 ms | 53 | 2,130 (P1 measured) |
| 48 | 20.0 ms | 50 | 2,390 |
| 60 | 21.9 ms | 46 | 2,740 |
| 80 | 25.1 ms | 40 | 3,190 |
| 40 again | 18.8 ms | 53 | 2,130 — the A–B–A check |

**Pass:** each row within ~10% (the within-run spread is ±7%), and the two 40 rows agree with each other. A
miss is recorded as the model being wrong at that point, not smoothed into agreement.

#### ✅ P3 — RESULT (2026-09-17, one session, 56 reports)

Both parts ran as specified, except that cap 60 held for 2 reports and cap 80 for 1 instead of 3 each. Leg
boundaries were recovered from demand and `capPerFrame`; reports straddling a change are excluded.
**Deviation:** at the end the ⏱ Frame cost toggle could not be reached while saturated, so it was disarmed after
the autobet stopped. The `Sim:` readout's text changes width and pushes the diagnostic column sideways,
under other controls. The data is unaffected, since disarming only flushes the partial window, which did
flush. The layout is to be fixed with the governor's readout.

**Part 1 — P3b, the backlog window, 1 credit, cap 40:**

| scale | frames | window (`MaxBacklogSeconds ÷ scale`) | retention | frames > 33 ms per 1,000 | checkpoints |
|---|---:|---:|---:|---:|---:|
| 9000X | 1,200 | 22 ms | **0.960** (0.931–0.988) | 1.7 | 2 |
| 6000X | 2,400 | 33 ms | **0.9992** | 1.7 | 3 |
| 3000X | 4,200 | 67 ms | **1.0000** | 2.9 | 4 |
| 9000X again | 1,200 | 22 ms | **0.960** (0.941–0.979) | 7.5 | 5 |

**Confirmed in direction and in the A–B–A** (0.9596 and 0.9602): the loss belongs to the window, not to the
scale's workload. The simulation took under 1 ms per frame in every leg. The 9000X loss was larger than P1's
0.987; within each report it tracks the checkpoints, whose 30–43 ms frames exceed a 22 ms window. The
frames' p95 is ~21–22 ms at every scale, which is why a 22 ms window is so sensitive and a 33 ms one is not.

**Part 2 — P3a, the cap, 99 credits × 9000X:**

| cap | frames | fps | frame | sim | outside sim | delivered/s | predicted | frames > 50 ms per 1,000 | GC frames per 1,000 |
|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|
| 40 | 3,600 | 53.1 | 18.85 ms | 6.24 ms | 12.61 ms | **2,123** | 2,130 ✓ | 0.6 | 44 |
| 48 | 11,400 | 48.3 | 20.71 ms | 7.28 ms | 13.43 ms | **2,317** | 2,390 ✓ (−3%) | 1.6 | 59 |
| 60 | 1,200 | 40.2 | 24.89 ms | 9.01 ms | 15.87 ms | **2,412** | 2,740 ✗ (−12%) | 6.7 | 87 |
| 80 | 600 | 33.1 | 30.25 ms | 11.31 ms | 18.94 ms | **2,627** | 3,190 ✗ (−18%) | 36.7 | 110 |
| 40 again | 2,908 | 51.8 | 19.32 ms | 6.42 ms | 12.90 ms | **2,071** | 2,130 ✓ (A–B–A: −2.4%) | 2.1 | 57 |

**P1's model FAILED past cap 48, and the reason is a variable P1 never varied.** The model assumed the ~12.5 ms
outside the simulation was a fixed cost. It is not: it grows with bets per frame, 12.6 → 13.4 → 15.9 → 18.9 ms.
Per bet, the sim stays flat (0.142–0.160 ms), but DiceGame pays a second cost later in the frame, outside the
profiled method: layout and rendering of what each bet appended. GC frames rise with it (44 → 110 per 1,000).
**Why P1 could not see it:** both of its saturated legs ran exactly 40 bets per frame, and its 1000X leg's
frames waited for the display, so their "outside" time was idle, not work. *The extrapolation rested on a
quantity the data never varied.*

**Fitted over all five legs:** `frame ≈ 7.6 ms + 0.283 ms × bets per frame` (DiceGame, 2009 world). It predicts
**~44 bets per frame at 50 fps, delivering ~2,190/s** — about 3% above cap 40's ~2,100/s at ~52 fps. The
asymptote is ~3,500/s at 0 fps.

**What that means for the limit.**
- **At the chosen 50 fps (D-09.1), moving the cap buys almost nothing:** +3% throughput for a lower frame rate
  and more stutter, since frames over 50 ms rise 3–60× from cap 48 to 80.
- **DiceGame's ceiling at 99 credits is therefore ~2,000–2,100 bets/s, i.e. 2000X**, which mini-plan 08 had
  found empirically, now accounted for rather than observed.
- **The large lever is not the cap but DiceGame's per-bet UI cost:** the same 40 bets cost 1.83 ms of sim in
  the hardware shop against 6.24 ms here, plus the outside share measured above. That is a separate piece of
  work, not this plan's.

**Decisions this result puts to the developer (§4 cannot be built without them):**
- **D-09.3 — the cap.** Recommended: keep `DefaultMaxBetsPerFrame` at 40. Cap 44 would meet 50 fps
  exactly, for ~3%.
- **D-09.4 — the budget.** Recommended: `BetBudgetPerSecond = 2,000`, about 5% under the ~2,100/s measured
  at cap 40. That makes 99 credits govern to 2000X, where retention was 0.995 in P1.
- **D-09.5 — the game-time axis.** Two options:
  - **(a)** lower the effective ceiling to 6000X, where 1 credit retains 0.999;
  - **(b)** keep 9000X and define the backlog window in **real** time, so that it never shrinks below the
    frame spikes a normal frame produces (e.g. ≥ 67 ms at any scale, i.e. 6 simulated seconds at ×90).

  Recommended: **(b)**. The loss at 1 credit is catch-up after a long frame, not work the engine cannot do,
  so widening the window gives up nothing. §38.7's rule forbids raising `MaxBacklogSeconds` to hand a
  *saturated* frame more work, which this is not: a governed run is, by construction, not saturated.
  **Verification:** 1 credit × 9000X must then retain ≥ 0.999.

#### Decisions taken (2026-09-17)

- **D-09.3 — cap stays at 40.** Accepted as recommended.
- **D-09.4 — `BetBudgetPerSecond = 2,000`.** Accepted as recommended.
- **D-09.5 — (b) is built, and 9000X stays only if it earns it.** The developer's criterion: *if, in what the
  player actually experiences, 9000X at 1 credit is no different from 6000X, lower the ceiling to 6000X — offer
  the most honest thing possible.*
  - **Today they already differ:** 9000X at 0.960 retention runs ~8,640X effective, against 6000X's ~6,000X.
  - **With the real-time window,** 9000X is expected to run at its full rate.
  - **So:** build (b), keep 9000X, and apply the developer's criterion to the verification. If 1–2 credits at
    9000X do **not** reach ≥ 0.999, the ceiling drops to 6000X, together with the ladder, whose top is
    asserted equal to the ceiling.

#### §4 — BUILT (2026-09-17), awaiting verification

- **`DevTimeScaleGovernor`** (`Scripts/Services/DevTimeScaleGovernor.cs`), pure static.
  `Govern(requested, runningCredits)` returns `min(requested, ⌊BetBudgetPerSecond ÷ credits⌋, ceiling ÷ 100)`,
  clamped ≥ 1, plus the reason: `None`, `Credits` or `Ceiling`.
- **`CalendarTimeService`:**
  - `RequestedDevTimeScale` is the selector's to write and raises `RequestedDevTimeScaleChanged`.
  - `DevTimeScale` is now the effective scale, with a **private setter**, so only
    `ApplyGovernedDevTimeScale` can change it. The compiler enforces that no other writer exists.
  - `DevTimeScaleLimit` and `DevTimeScaleGovernedCredits` carry the reason and the credits for the readout,
    and `DevTimeScaleChanged` fires on change.
  - Every reader of `DevTimeScale` is unchanged: the clock, the bet engine's `simDelta`, the difficulty
    trace's `devTimeScale` column.
- **`SimulationService`** governs on three inputs:
  - the request changing (event);
  - `HardwareChanged` (event);
  - the running credits changing, which catches bot runners starting, stopping or removing themselves.
    That one is a single comparison per frame, against the power `_Process` already computes; the governor
    runs only on change.
  - **While idle it previews** against the player's own credits, so the readout already shows what the next
    autobet will run at.
  - **DEBUG check at boot:** `BetBudgetPerSecond ≥ 5 bettable nodes × MaxAutoBetBaseAps`. If that ever fails,
    the Output panel prints a warning, because 100X could then be slowed.
- **D-09.5(b):** both backlog clamps use `max(MaxBacklogSeconds, MinBacklogWindowRealSeconds × DevTimeScale)`,
  with the floor at 1/15 s, the window 3000X already had. It never binds below ×30.
- **Readout:** after the diagnostic column, amber, hidden while nothing limits. It shows
  `⇣ 2000X · 99 credits` or `⇣ 6000X · ceiling`, with a tooltip spelling out requested vs running and the
  budget arithmetic. The selector itself always shows the request.
- **`SimRetentionReadout` has a fixed width** sized for `Sim: 100%`, so its changing text no longer pushes the
  diagnostic toggles out of reach (P3's run).

#### §6 — verification protocol and predictions (registered before the run)

One session, starting from the world as P3 left it (99 credits). Frame cost ON before starting, Bet cost OFF,
Stop-on-block OFF, auto-recharge ON, Cap/frame 40. Each measured step holds for **3 new `[FrameCost]` blocks**.

| step | action | predicted readout | predicted retention |
|---|---|---|---|
| 1 | idle, request **100X** | none | — |
| 2 | request **9000X**, still idle | `⇣ 2000X · 99 credits` before the autobet starts | — |
| 3 | start the autobet | unchanged | ~0.995 (P1's 2000X) at ≥ 50 fps |
| 4 | discard to **50** credits | `⇣ 4000X · 50 credits` | ≥ 0.99 |
| 5 | discard to **23** | `⇣ 8600X · 23 credits` | ≥ 0.99 |
| 6 | discard to **22** | none — 22 × 90 = 1,980 ≤ 2,000 | ≥ 0.99 |
| 7 | discard to **2** | none, 9000X | **≥ 0.999** — the D-09.5 criterion |
| 8 | request **100X**, running | none | 1.000 |

**Also from disk afterwards:**
- `verify-bet-journal.js` passes A1–A4 across every scale change;
- the difficulty trace's `devTimeScale` column shows the effective values;
- in the frame trace, delivered bets/s never exceeds ~2,000 while the readout is showing.

#### §6 — RESULT (2026-09-18): the governor works; the budget does not hold across sessions

The developer ran steps 1–8 and saw every predicted readout. Step 8 was cut short (its three reports at 100X
would have taken minutes); the frame trace still holds six reports of it.

| step | effective scale | fps | frame | sim per bet | outside sim | demand | delivered | retention |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| 3 — 99 credits | 2000X (credits) | 45.6 | 21.97 ms | 0.184 ms | 14.62 ms | 1,980 | 1,822 | **0.936** |
| 4 — 50 credits | 4000X (credits) | 46.0 | 21.73 ms | 0.168 ms | 15.15 ms | 2,000 | 1,799 | **0.930** |
| 5 — 23 credits | 8600X (credits) | 45.8 | 21.86 ms | 0.176 ms | 15.05 ms | 1,978 | 1,771 | **0.931** |
| 6 — 22 credits | 9000X | 46.2 | 21.67 ms | 0.179 ms | 14.57 ms | 1,980 | 1,832 | **0.942** |
| 7 — 2 credits | 9000X | 58.3 | 17.16 ms | — | — | 180 | 179 | **0.9993** ✓ |
| 8 — 2 credits | 100X | ~60 | ~16.7 ms | — | — | 2 | 2 | **1.000** ✓ |
| (hardware shop, 33 bets/frame) | 2000X | 59.7 | 16.75 ms | 0.042 ms | — | 1,980 | 1,981 | 1.000 |

**What passed:**
- **The mechanism.** Every readout matched. The difficulty trace's `devTimeScale` column records the effective
  scale per block: 20 → 40 → 86 → 90.
- **Timestamps under repeated scale changes.** `verify-bet-journal.js`: A1, A1b, A3 PASS; A4 13 of 13
  player-mined blocks joined to the millisecond.
- **D-09.5's criterion.** 2 credits at 9000X retained 0.9993 (0.960 before the real-time window), including
  reports holding 3 and 5 checkpoints. **9000X stays.**
- **100X untouched.** Retention 1.000 at the display rate.

**What failed: the 50 fps target at the 2,000 budget.** With demand held at the budget (steps 3–6), DiceGame
delivered ~1,800 bets/s at ~46 fps with retention ~0.93. The same configuration ran at 53–55 fps and 0.995 on
2026-09-17.
- **The whole frame was ~17% slower, and evenly:** sim cost per bet 0.184 ms against 0.156 (P3a), and the work
  outside the sim 14.6 ms against 12.6.
- **A uniform factor across both halves matches mini-plan 08 P1g's between-session spread** (up to 34%), not a
  code change: this build added one comparison per frame to the sim path, and nothing else inside it.
  *Hypothesis, not isolated.* An A–B–A inside one session would settle it, and is not needed for the decision
  below.
- **The budget's 5% margin was smaller than the known between-session spread,** so the failure was
  predictable from P1g. The margin was chosen against within-run noise (±7%), the wrong reference.

**And it matters for honesty, not only for fluidity.** At retention 0.936 the readout said 2000X while game time
actually ran at ~1,870X. The developer's standard, stated for D-09.5, is to offer the player the most honest
figure possible. A budget the engine cannot sustain makes the readout overstate the speed.

**D-09.6 — the budget, to be decided.** The slow session's frame, scaled from P3a's fit
(≈ 1.16 × (7.6 + 0.283 × bets per frame)), holds 50 fps at ~34 bets per frame: **~1,700 bets/s.**
- **(a) Lower `BetBudgetPerSecond` to 1,700 — recommended.** Holds 50 fps and full retention in both sessions
  measured. 99 credits govern to 1700X. The cost: ~15% of speed on a fast session, which could have run
  2000X.
- **(b) Keep 2,000.** Fast sessions run at ~53 fps; slow ones at ~46 fps, with the readout overstating the
  rate by ~7%.
- **(c) An adaptive budget** that follows measured delivery. Rejected in §5 as unpredictable; this result is the
  evidence §5 said would reopen it, but it is a larger build.

**Layout fix, found in this run's screenshots.** With an autobet running, StrategyControlPanel grows (PAUSE
appears), and Auto Recharge moves down to ~495. The diagnostic column, placed from an idle screenshot at y 445,
sat over it. It moves to y 498, with separation 2, which still ends above the chance slider.

#### Decisions taken (2026-09-18)

- **D-09.6 — `BetBudgetPerSecond = 1,700`, option (a).** Sized for the slowest session measured. 99 credits
  govern to 1700X; the readout shows what actually runs.
- **Option (c), an adaptive budget, is a candidate for a refinement stage AFTER this plan, not part of it.**
  - **The case for it:** a fixed constant has to be sized for the worst session, so it leaves a fast session's
    speed unused, and it is machine- and era-specific (§5).
  - **The case against, which any design must answer first:** a budget that follows live delivery oscillates.
    It also moves the effective scale for reasons the player cannot see (a GC, a background process), and it
    makes the readout harder to trust.
  - **A study should start from this plan's data:**
    - the within-run spread (±7%) against the between-session spread (~17% measured here, up to 34% in
      mini-plan 08);
    - FrameCostProfiler's per-report delivery, which already measures what an adaptive rule would feed on.
- **Layout — the interim fix above is superseded by a simpler one, the developer's.** PAUSE no longer sits under
  Bet Once / AUTO, where showing it made the panel's top row taller and pushed every row below it down for the
  length of an autobet.
  - **PAUSE is now its own column to the right of that row,** at Bet Once's font size (30 instead of 44), with
    one fixed width for "PAUSE" and "RESUME".
  - **The panel no longer changes height, so Auto Recharge stays put** and the diagnostic column goes back to
    y 445.
  - **DiceGame's five navigation buttons** are absolutely placed, not in a container shared with the panel, so
    they cannot be pushed by layout. DiceGame shifts them just enough to clear PAUSE's real edge, one frame
    after it appears (after the containers have laid it out), and returns them when it hides. At font 30 the
    shifted column is *estimated* to fit inside the 1920 px viewport, and at 44 to overflow it by ~20 px. Both
    figures come from text widths, not from the screen, and the first is to be confirmed by eye.

#### ✅ D-09.6 — confirmation RESULT (2026-09-18, 99 credits, requested 9000X)

The readout showed `⇣ 1,700X · 99 credits` before and during the run. The frame trace holds 8 full reports at
demand 1,683 bets/s (99 × 17). The final partial window was not flushed because the toggle was left armed,
which loses nothing but that window.

| | measured | target |
|---|---:|---:|
| fps | **53.1** mean, **50.5** worst report | ≥ 50 (D-09.1) |
| delivered | **1,679** bets/s against 1,683 demanded | = demand |
| retention | **0.9984** mean, 0.990 worst report | ~1.000 |
| cap bound | 5–37% of frames | < 100% (not saturated) |

**The honest-readout criterion holds:** what the readout says (1700X) is what ran. Sim cost was ~0.19 ms per
bet, as slow as §6's session and slower than P3a's, so the budget held in the kind of session it was sized
for. The developer watched `Sim:` stay at 100% throughout.

**Layout confirmed by eye** from the developer's screenshots:
- PAUSE appears to the right of STOP;
- the navigation column shifts right and stays inside the viewport, which settles the estimate above;
- RESUME keeps PAUSE's width;
- Auto Recharge and the diagnostic column do not move.

### P2 — R2-C1: carry the lagged quantity additively (build + verify)

The fix mini-plan 08 specified and did not build. Today the calendar advances `delta × rate × throttle`, where
the throttle is **last frame's retained ratio applied to this frame's delta**; the arithmetic mean of that
ratio exceeds 1. **Change:** the calendar advances by **last frame's retained simulated seconds ×
`SpeedMultiplier`**. The one-frame lag remains, but `Σ advance = Σ retained` exactly, so the bias is gone by
construction.

- **Scope, stated before building:** the path where the calendar runs *without* the simulation (manual play,
  no autobet) must keep advancing exactly as today. Only a frame in which `SimulationService` drove the sim
  may use the additive carry.
- **Verify, within one run:** repeat mini-plan 08's `r` reconstruction on a saturated journal.
  **Prediction:** net overspend drops from 0.620% to under 0.05%. The frame-edge holes should mostly
  vanish, since the up-step half of each fluctuation was a hole. Scanner A1–A4 must still pass.
- **Invariance:** no P4-style block-interval test is required. The change only removes a bias that P4's ±35%
  resolution could never see; it is recorded as such, not claimed as verified invariance.

### P3 — The sweep: two axes, and the cap experiment

**Use credits as the fine control, not the ladder.** Demand is `credits × scale`. At a fixed scale, one
credit is one step of `scale` bets/s — far finer than the ladder's 1000X rungs above 900X — and credits can
be added and discarded in DEV at will. *Changing credits perturbs mining power and difficulty*, so the sweep
runs as one continuous autobet, and each measurement leg starts after a settle leg (mini-plan 08 P4's
lesson).

**P3a — bets/s capacity.** At ×20, ×30 and ×90, step credits upward within one run. Starting points follow
the model: ×90 from 15 credits, ×30 from 50, ×20 from 90. **For each scale, record the highest demand
holding `Sim: 100%` p95 over a full report window.**
- *If the three capacities agree within the within-run spread*, `BetBudgetPerSecond` is one constant.
- *If they fall with scale*, game-time work is eating the frame, and the budget becomes a small table by
  scale — which the governor formula already accommodates.

**P3b — the game-time ceiling.** 1 credit, ×90, a full window, with P1's instrument armed. Is `Sim: 100%` flat
there, or do per-block spikes (checkpoint, `state.json`, company governance) already dent it?
- *Flat:* 9000X is the true ceiling and stays.
- *Dented:* the ceiling is wrong for the game-time dimension, and **§4 must lower it or the spikes must be
  fixed**, which becomes a decision recorded here, not an assumption.
- **Optional, only if P3b is flat with margin:** a DEV build raising `MaxGameSecondsPerRealSecond` and the
  ladder together (their parity is asserted in code) to find where 1 credit saturates. The developer asked
  for the *absolute* limit, and 9000X was set by judgement, not measured.

**P3c — does `MaxBetsPerFrame` bind?** Only if P1 rejects H1. At 99 × 3000X, A–B–A within one run: the current
`MaxBetsPerFrame` → a higher value → back. **Accept a raise only if** delivered bets/s rises, fps does not fall
below the rate that made the gain, and P1 shows the frame had room. Otherwise, record the cap as correctly
sized and leave it. **§38.7 still binds: low `Sim%` means find what eats the frame, never raise the cap
reflexively.**

**P3 output:** `BetBudgetPerSecond`, per scale if necessary, with the margin chosen from the measured spike
distribution, and the ceiling confirmed or corrected. Written into §6's record with the run's figures.

## 4. Phase B — the governor

### Where the numbers live

- **`CalendarTimeService.RequestedDevTimeScale`** (new) — what the developer chose. **Only the selector
  writes it.**
- **`CalendarTimeService.DevTimeScale`** (existing) — becomes the **effective** scale. **Only the governor
  writes it.** Every current reader (the clock's `_Process`, `SimulationService`'s `simDelta`, telemetry)
  keeps reading the same property and needs no change. *Keeping the consumed name and adding the new one on
  the writer's side means no reader can be missed* (Standing Convention 13).
- **`DevTimeScaleGovernor`** (new, pure static) — `Effective(requested, totalCredits)`, the §2 formula and
  nothing else, so it can be checked in a throwaway console project exactly as the game computes it.
- **Recompute owner: `SimulationService`**, the only place that knows which bot runners are running. It
  recomputes on:
  - `HardwareAllocationRepository.HardwareChanged` (buy, discard, move between pools);
  - bot runners starting or stopping;
  - player autobet start;
  - the requested scale changing.

  No new autoload, and no polling: every trigger is already an event or a method call (Pattern 6).

### Behaviour

- **Step per change, no smoothing.** Each credit bought is one `HardwareChanged`, hence one recompute,
  hence one step down. That is exactly the "baja según voy comprando" the developer described. A scale
  change mid-run should already be safe, because the half-open clamp spreads each batch over the clock's
  *actual* movement, whatever stride produced it. §6 verifies that on disk instead of assuming it.
- **Resolution 100X** — any integer multiplier, even off the ladder (8600X is valid). The selector keeps
  showing the requested rung; a readout shows the effective one.
- **Readout beside the selector and `Sim%`:** `Requested 9000X · Effective 2000X — limited by 99 credits`,
  or `Effective 9000X` when nothing binds. This is the "campo para señalar el DevTimeScale requerido": the
  request is always visible, so a lowered scale can never be mistaken for a selector that ignored the click.
- **DEBUG assert:** `BetBudgetPerSecond ≥ bettable nodes × MaxAutoBetBaseAps`. That is what guarantees 100X
  is never transformed; if a future budget change breaks it, it prints instead of silently slowing normal
  play.
- **Not persisted**, like `DevTimeScale` today: both reset to 100X on restart.

## 5. Known limits of this plan, stated up front

- **The budget is measured on one machine and in one era.** Founder and scheduled attempts per bet depend on
  the network's power ratio, which changes by orders of magnitude between 2009 and later years. A 2009 budget
  may be too generous in 2013. **Mitigation:** the margin, plus R2-C1 as the safety net, since a budget that
  is too generous produces honest wall-clock slowdown, never distorted game dynamics. **Optional P3d:** repeat
  P3a on an EB.1 DEV entry-year world (`TimelineConfig.DevEntryYear`) in a later era, and turn the constant
  into a curve if it moves.
- **A static budget rather than an adaptive controller, deliberately.** A controller reading live frame time
  would track the actual machine, but it would oscillate, and it would make the effective scale depend on
  things the developer cannot see (a background process, a GC). A measured constant is predictable and
  testable, and R2-C1 already absorbs what it misses. Recorded as a decision to revisit if the constant proves
  too machine-specific.

## 6. Verification protocol for the governor (after §4 is built)

1. **Ramp down under load.** Request 9000X, 2 credits, autobet running. Buy credits one at a time up to 99.
   - Effective readout steps down as §2's table predicts.
   - `Sim:` holds ~100% throughout.
   - The Godot editor's Output panel shows no `[Clock] … exceeds the ceiling` line.
2. **Ramp up.** Discard back to 2 credits: effective rises back to 9000X.
3. **100X is inert.** Request 100X, repeat the ramp both ways: effective never leaves 100X.
4. **Bots count.** With bot runners configured, the same ramp binds earlier, by their credits.
5. **From disk:**
   - `verify-bet-journal.js` passes A1–A4 across the whole ramp (the scale changed mid-run many times).
   - The difficulty trace's `devTimeScale` column shows the effective values, one per block.

## 7. Out of scope

- **Changing what a hardware credit is worth.** The governor lowers the clock; it never touches
  `HardwareRate`, the difficulty regulator or mining power.
- **A non-DEV speed control for players.** `DevTimeScale` stays a development tool; a player-facing speed
  setting is a design question for Basic Mode's UI.
- **Persisting the requested scale.** User settings persistence does not exist yet (see `PRIVATE_ROADMAP.md`
  §6), and this plan will not invent a one-off for it.
- **Interleaving player and bot settle loops** (mini-plan 08's D4 residual). Unrelated to throughput.

## 8. Close-out (2026-09-18)

**Both objectives are met.**

1. **The limit is measured, and it turned out to be a choice, not a wall.**
   - **In DiceGame the ceiling is `MaxBetsPerFrame` itself,** bound on every saturated frame. Past it, each
     extra bet per frame costs frame rate, not throughput: `frame ≈ 7.6 ms + 0.283 ms × bets per frame`,
     fitted in-run over caps 40–80.
   - **At the chosen 50 fps (D-09.1) the cap stays at 40 (D-09.3).** Sessions on the same machine differ by
     ~17%, so the budget is sized for the slower: **1,700 bets/s (D-09.6)**. 99 credits run at 1700X, and 1700X
     is what actually runs (53 fps, retention 0.998, confirmed).
   - **The game-time ceiling of 9000X stays.** It is real down to 2 credits (0.999 retention) since the
     backlog window gained a real-time floor (D-09.5).
2. **The governor gives priority to hardware credits.** Requested vs effective, recomputed on events, 100X
   never touched, a readout that always says what runs. Verified across 99 → 50 → 23 → 22 → 2 credits, with the
   timestamp scanner passing A1–A4 over every scale change.

**What shipped:**
- **Instruments:** `FrameCostProfiler` (whole-frame, H1–H4 per report, `frame_cost_trace.csv`) and a DEBUG
  runtime `MaxBetsPerFrame` override for within-run sweeps.
- **The governor:** `DevTimeScaleGovernor`, `RequestedDevTimeScale`, and `DevTimeScale` with a private setter.
- **The backlog window's real-time floor.**
- **UI:**
  - the governor readout;
  - `Sim:` at a fixed width;
  - the diagnostic toggles mirroring their profilers' static state;
  - the DEV test controls in DiceGame's free block;
  - PAUSE as its own column, with the navigation buttons shifting to clear it.

**Open, each its own decision:**
- **An adaptive budget** (D-09.6 option c). A candidate for a refinement stage; the data to start from is in
  D-09.6.
- **R2-C1's saturated-backlog overspend (P2, D-09.2).** Its specified fix was flawed: it would have
  desynchronised the clock on every frame. The governor now keeps runs out of saturation, which is the only
  regime where the overspend exists, so it should be re-measured before anything is built.
- **DiceGame's per-bet UI cost** — the real lever for going faster. The same 40 bets cost 0.04 ms of sim per bet
  in the hardware shop against ~0.16–0.19 in DiceGame, plus the outside-sim share P3a measured.
- **Four in-sim spikes of 15–67 ms with no checkpoint or GC** (P1, H4) remain unattributed.

*Lessons this plan paid for, recorded once:*
- **An extrapolation is only as good as the variables the data actually varied.** P1's model held the outside
  cost constant because both saturated legs ran 40 bets per frame, and P3a broke it.
- **A margin must be sized against the variance that will actually occur.** Within-run noise was the wrong
  reference; the between-session spread was the right one, and mini-plan 08 had already measured it.
- **A fix specified from arithmetic alone must also ask WHEN each side runs inside a frame (P2).**
