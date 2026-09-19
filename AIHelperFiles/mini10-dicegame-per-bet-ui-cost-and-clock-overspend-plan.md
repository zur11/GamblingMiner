# Mini-Plan 10 — DiceGame's per-bet UI cost, and R2-C1's overspend under the governor

**Series note:** tenth entry of the *mini-plan* series, following
`mini09-devtimescale-ceiling-and-credit-governor-plan.md`, whose close-out (§8) left both subjects open.

**Status:** 🚧 **IN PROGRESS** on `mini10-dicegame-ui-cost-and-clock` (2026-09-18). A1 and B1 are **measured**;
their results are in §2 A1 and §3 B1/B2, and they changed A2's design — see **D-10.1**.

**Two independent parts, in this order:**

- **A — make a bet cheap to show.** DiceGame pays ~4× more per bet than a scene without its bet lists, and that
  cost is what caps the DEV scale (mini-plan 09). Cutting it is the real lever for going faster. It ends by
  re-deriving `MaxBetsPerFrame` and `BetBudgetPerSecond` from new measurements.
- **B — settle R2-C1's overspend.** Measure it directly under the governor, then close it or fix it by a
  pre-registered rule.

B's instrument ships in A's first build, so every run of part A measures it for free.

*Out of scope, recorded:* an **adaptive budget** (mini-plan 09's D-09.6 option c) is a Basic Mode refinement
option, not this plan's (`PRIVATE_ROADMAP.md`).

---

## 1. What mini-plan 09 left, measured

| scene | sim cost per bet | outside-sim cost per extra bet | source |
|---|---:|---:|---|
| DiceGame | 0.156 ms (fast session) – 0.19 ms (slow) | ~0.13 ms (outside grew 12.6 → 18.9 ms from 40 → 80 bets/frame) | mini-09 P3a, §6 |
| hardware shop | **0.042 ms** | not measurable (frame sat at the display rate) | mini-09 P1, §6 |

- **The frame fit, DiceGame:** `frame ≈ 7.6 ms + 0.283 ms × bets per frame`. Past the cap, each bet costs frame
  rate, not throughput.
- **Where the difference lives, from the code** (not yet measured):
  - DiceGame has exactly **two** per-bet UI consumers: `BetHistoryContainer` and `PreviousWinnerNumbersGrid`,
    both fed by `DiceGame.BetExecuted`, which `OnSimBetSettled` raises once per settled bet.
  - Both do the same thing per bet: `Setup()` one pooled row, then `MoveChild(item, 0)` among 100 children.
  - A move to index 0 renumbers every sibling and invalidates the container's layout. So one bet reorders
    ~100 nodes, twice, and at 40 bets per frame that happens 80 times in a frame.
  - The share paid **inside** the settle loop is mini-plan 08's `BetHistoryFeed` segment. The share paid **after**
    it (sort, layout, redraw) is what mini-plan 09 saw as outside-sim time growing with bets.
- **R2-C1:** mini-plan 08 measured a **0.620%** overspend of game time over simulated time, *under a saturated
  backlog*, by reconstructing per-frame ratios from a journal. Mini-plan 09's governor exists to keep runs
  **out** of saturation, so the overspend under governed play is expected to be small — but it is unmeasured.
  The fix specified in mini-plan 09 (P2) was flawed (D-09.2), so nothing should be built until the size of the
  problem is known.

## 2. Part A — the per-bet UI cost

### A1 — Attribute the cost, one run (instrument + desk work)

A DEBUG-only **"Bet UI"** picker in DiceGame's diagnostic column, switchable mid-run, with three modes:

| mode | per bet | isolates |
|---|---|---|
| **Full** | `Setup` + `MoveChild` (today) | the baseline |
| **No reorder** | `Setup` only; rows stay in place (the list shows wrong order — acceptable for a DEV measurement) | the cost of `MoveChild` and the layout it invalidates |
| **Off** | nothing: both consumers skip the event | the whole UI cost (should approach the hardware shop's) |

**Run:** 99 credits, 9000X requested (governed to 1700X), `Cap/frame` override 60 so the cap binds and the
bets per frame stay fixed, Frame cost ON, Bet cost ON. Sequence Full → No reorder → Off → Full, 3 reports each.
Bet cost's `BetHistoryFeed` segment reads the in-loop share; Frame cost's outside-sim time reads the rest.

**Pre-registered predictions:**
- **Off** brings sim cost per bet close to the shop's 0.042 ms, and outside-sim time stops growing with bets.
- **No reorder** recovers **most** of the gap. The hypothesis is that `MoveChild` and its layout invalidation
  cost more than `Setup`'s four label writes.
- The two Full legs agree within ±7%, the within-run spread.

If No reorder recovers little, the cost is in the row writes themselves, and A2's design changes (see A2's
fallback).

#### A1 RESULT (run 2026-09-18, 17 reports, `frame_cost_trace.csv`)

Read from `playerLoopMs` — the segment that raises `BetExecuted` — divided by bets per frame. The first report
of each leg is discarded: it straddles the switch and mixes both modes.

| mode | ms per bet, player loop | of which |
|---|---:|---|
| **Full** | 0.198 (0.21 in the second leg) | baseline |
| **No reorder** | 0.175 | — |
| **Off** | 0.040 | matches the shop's 0.042 ms ✅ |

- **The UI is 0.158 ms per bet — 80% of the whole cost of processing a bet.** Everything else is 0.040 ms.
- **`MoveChild` is 0.023 ms of it (15%). The row `Setup` is 0.135 ms (85%).** The pre-registered prediction —
  that the reorder dominates — is **refuted**, and with it A2's original design (**D-10.1**).
- At 1,700 bets/s the lists cost **269 ms of CPU per second**, against 68 ms for the simulation itself.
- **Method caveat, recorded:** in the Off leg the frame hits vsync (p50 16.67 ms, 58 fps), so whole-frame figures
  measure the display, not the work. Only the player-loop segment is comparable across the three legs, which is
  why every figure above is read from it. The Off leg's true ceiling was therefore **not** observed — A3 has to
  go and find it.
- The two Full legs differ by 6% (0.198 → 0.21), inside mini-plan 09's ~17% between-session spread.

### A2 — The fix: the bet display becomes a window with two costs

**D-10.1 (2026-09-18, replaces this section's original design).** A1 refuted its premise. Reordering is 15% of
the cost; **repainting rows is 85%**, and no rearrangement of the list touches it. The cost is not *how* the
rows are written but *how many times*: at 1,700 bets/s the lists repaint ~1,700 rows per second, of which a
human reads none. So the fix attacks the repaint rate, in two states.

**The two states** (the developer's design, 2026-09-18 — it is what makes the second budget legitimate):

| state | per-bet work | budget |
|---|---|---|
| **Hidden** — the bet display is toggled off | **none**, exactly today's `Off` mode: 0.040 ms per bet | the high one |
| **Visible** | paint the **visible rows only**, at most once per frame | the measured one |

- **The toggle is a PLAYER control, not a DEV one.** Hiding the feed is how the player buys speed, and the
  trade is legible: you cannot watch what you are not rendering. Default **visible**; not persisted — there is
  no user-settings persistence yet (`PRIVATE_ROADMAP.md`), and inventing a private one for this is the wrong
  order of work.
- **On SHOW, the list rebuilds from the journal's last 100 records** (the existing `LoadFromHistoricalRecords`
  path). Hiding must never be able to leave a stale row on screen: the panel is a *view*, and a view that was
  not painted has no content of its own to preserve.
- **While visible**, the flush runs where DiceGame already coalesces settled-bet UI once per frame
  (`FlushSettledBetUiIfDirty`, after `SimulationService` has settled), and writes only the rows inside the
  `ScrollContainer`'s visible window. Rows outside it are written when the player scrolls to them.
  *Why this is the whole win:* with a 100-row buffer and 30–50 bets per frame, no row is overwritten twice
  within one frame, so coalescing **alone** saves nothing. The old path wrote **one row per bet**, i.e. 30–50
  rows per frame, growing with bets; the new one writes at most the ~14 rows on screen, **whatever the bets per
  frame**. The prediction is therefore not "cheaper per bet" but **a per-bet slope near zero** for this view.
  *(Corrected 2026-09-19: this bullet first said "~12 visible rows instead of ~100", which misdescribed the
  old path — it never repainted 100 rows per bet, only one.)*
- **The Numbers view does NOT get this treatment, deliberately.** Its 100 cells (10 × 10 at 40 px) all fit in
  the window at once, so there is no scroll and "only what is visible" is all of it: a fixed-order rewrite
  would cost 100 cell writes per frame against the old path's 30–50. It keeps one Setup + MoveChild per bet,
  and A3 measures it on its own — that measurement replaces A3.5's "split the two consumers".
- **Both consumers are in scope** — `BetHistoryContainer` and `PreviousWinnerNumbersGrid`. A1 measured them
  together and did not split them; if the grid turns out to be the cheap one it can stay visible, but that is a
  measurement A3 can make, not an assumption to build on.

**The governor gains a second budget, and it is STATE, not adaptation.** `BetBudgetPerSecond` becomes one
value per cost state, each **measured** (A3), chosen by the state in force. This is not D-09.6's rejected
adaptive budget: there is no feedback loop and no estimator — two discrete states, two numbers, switched by an
explicit player action.
- Showing the display, or entering a scene that paints per bet (**BetsHistoryExplorer**'s replay), re-governs
  the scale **live** and downward. The orange readout already announces a governed scale; it must say which
  state is in force, because a speed that changes when a panel opens is otherwise indistinguishable from a bug.
- **The clock's own ceiling may bind before the hidden budget does** (`CalendarTimeService.MaxGameSecondsPerRealSecond`,
  the governor's existing `Ceiling` limit). If it does, say so plainly rather than quoting a budget nothing can
  reach.

**Contract that must not change:**
- newest first; the same 100-row scrollback; the same colours and texts;
- `LoadFromHistoricalRecords`, `ClearEntries`, and `AppendHistoricalRecord`, which **BetsHistoryExplorer**
  drives at its own replay pace (mini-plan 04 §2.3). The explorer must render identically; it gets its own
  check in A3;
- the manual burst stays fluid (mini-plan 08): at most one flush per frame is exactly what the paced burst
  already assumes. **With the display hidden, a manual burst still has to show its result** — the Roll Result
  label and the balances are not the bet list and keep updating.

### A3 — Verify, then re-derive the cap and the budget

1. **Behaviour.**
   - DiceGame's list and the winner grid match the journal's last 100 bets exactly: order, values, colours.
   - **Hide during a run, show again: the list is correct immediately**, not stale and not empty — the case the
     whole two-state design stands on.
   - After a scene round-trip and after a restart, both show the checkpoint's last bets, as today.
   - BetsHistoryExplorer's replay renders row by row, as before.
2. **The cap sweep again** (mini-plan 09 P3a's protocol): 99 credits × 9000X, Cap/frame 40 → 60 → 80 → 120 →
   40 in one run, **with the display visible** — that is the state the visible budget is sized for.
   - Refit `frame ≈ a + b × bets per frame`.
   - **Prediction:** `b` falls from 0.283 ms towards ~0.05 ms, and `a` rises by the per-frame flush cost.
   - Pick the cap that holds D-09.1's 50 fps.
3. **The hidden state's ceiling, which A1 could not see.** With the display hidden, the frame sat at vsync, so
   the run never revealed what it could deliver. Raise the requested scale until either the 50 fps floor or the
   clock's ceiling binds, and record **which one did**. That figure is the hidden budget; if the ceiling binds
   first, the hidden budget is "the ceiling" and the number is the clock's, not the frame's.
4. **The budget(s), sized for the slower session** (D-09.6's lesson): repeat both governed legs in a **second
   session** and size each `BetBudgetPerSecond` for the slower of the two, not the faster.
5. **One leg per bet view** — Detailed, Numbers, Off — in one run. The trace's `betUiMode` column now records
   the view in force for each report (A1's DEBUG picker was deleted when A2 made its NoReorder mode
   meaningless). Predictions: Detailed's per-bet slope near zero; Numbers close to A1's old half-cost; Off
   unchanged at 0.040 ms per bet.
6. **Timestamps:** `verify-bet-journal.js` passes A1–A4 on the sweep's journal. A higher cap means more bets per
   frame, which is exactly where the half-open clamp works hardest.

## 3. Part B — R2-C1's overspend under the governor

### B1 — Measure it directly (ships with A1)

`FrameCostProfiler` gains two per-frame sums:
- **calendar game-seconds advanced**, read as the clock's movement between consecutive frames;
- **retained game-seconds**, the engines' retained simulated seconds × `SpeedMultiplier`, which is what the
  frame actually simulated.

`overspend = Σ calendar advance ÷ Σ retained − 1`, per report and in the CSV. No journal reconstruction
needed. Mini-plan 08's 0.620% was an inference from gaps; this is the quantity itself.

#### B1 RESULT (same run as A1)

| retention | overspend |
|---:|---:|
| 1.0000 (10 of 17 reports) | **0.000000%** |
| 0.9988 | 0.073% |
| 0.9971 | 0.204% |
| 0.9875 | 0.209% |
| 0.9700 | 0.329% |
| 0.9159 | 1.118% |

**The overspend is not a structural bias of the autoload order — it is a symptom of saturation, and it is
exactly zero when the frame keeps up.** Mini-plan 08's 0.620% was a correct reading *of a saturated run*, and
reading it as a property of the design was the error this measurement corrects.

Every saturated window in the run was a **Bet UI Full** window: the UI cost is what pushes the 1,700 budget
into saturation in the first place. **A fixes B**, and B3's criterion should be evaluated after A2 ships, not
before.

### B2 — Two readings, from runs that happen anyway

- **Governed:** every A1 and A3 leg at the governor's effective scale.
- **Forced saturation:** one leg with `Cap/frame` overridden low (e.g. 20 at 99 credits, which cannot deliver
  1,700 bets/s). **Prediction:** it reproduces ~0.6%, which validates the instrument against mini-plan 08's
  independent reconstruction.

### B3 — Decision rule, registered before the data

- **Governed overspend below 0.05%: close P2 for good.** Record the measurement, note that saturation only
  happens when something outside the governor's control stalls the frame, and delete nothing (R2-C1 stays).
- **At or above 0.05%: build the lag-free version.** The shape, from D-09.2:
  - the calendar must not advance on its own during a sim-driven frame;
  - `SimulationService` evaluates every engine's backlog clamp at the top of the frame, then advances the
    clock by exactly the retained simulated seconds × `SpeedMultiplier`, before planning any bet.

  That keeps `Σ advance = Σ retained` with no lag and no Jensen bias. It reorders the frame, so it gets its
  own verification: scanner A1–A4, and a P4-style block-interval check at low power, stated as such.

## 4. Order, and what each step needs from the developer

1. ~~**Build A1's picker and B1's two sums** (one build) → **run A1**~~ ✅ done 2026-09-18. Results in A1 and B1.
2. **Build A2** — the toggleable display, the visible-row flush, and the two-state budget → **run A3.1–A3.6**
   (two sessions for the budgets).
3. **B3 decides from A3's governed data**, plus the one forced-saturation leg — after A2, per B1's result.

## 5. Out of scope

- **An adaptive budget** — a Basic Mode refinement option (`PRIVATE_ROADMAP.md`).
- **Other scenes' per-bet UI** (ClientsBetsHistory's live feed, BetsHistoryExplorer's own panels), beyond
  keeping the explorer's rendering unchanged. DiceGame is the scene the budget is sized for.
- **The four unattributed in-sim spikes** (mini-plan 09 P1, H4) — a separate investigation if they matter.
