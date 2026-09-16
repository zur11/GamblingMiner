# Mini-Plan 09 — The absolute DevTimeScale ceiling, and a governor that yields to hardware credits

**Series note:** ninth entry of the *mini-plan* series, following
`mini08-timestamp-fidelity-and-throughput-limits-plan.md`, whose close-out (§4.9) left this as its first open
item.

**Status:** 📋 **SPECIFIED, NOT STARTED** (2026-09-16). To be built on its own branch off `main`.

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
- **The real frame time**, from `delta`.
- **`SimulationService._Process`, split into:** player bet loop · bot loop · founder drive · scheduled drive
  · everything else in the method.
- **Counts:** player and bot bets executed; founder and scheduled **PoW attempts**; checkpoints captured, by
  source.
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
