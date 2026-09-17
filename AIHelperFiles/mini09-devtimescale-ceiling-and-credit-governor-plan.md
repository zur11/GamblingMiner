# Mini-Plan 09 — The absolute DevTimeScale ceiling, and a governor that yields to hardware credits

**Series note:** ninth entry of the *mini-plan* series, following
`mini08-timestamp-fidelity-and-throughput-limits-plan.md`, whose close-out (§4.9) left this as its first open
item.

**Status:** 🔧 **IN PROGRESS** on branch `mini09-devtimescale-governor` (specified 2026-09-16). P1 built and
run (2026-09-17); its result redirects P3 — see "P1 — RESULT".

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
