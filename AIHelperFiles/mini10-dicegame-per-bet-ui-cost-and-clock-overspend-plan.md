# Mini-Plan 10 — DiceGame's per-bet UI cost, and R2-C1's overspend under the governor

**Series note:** tenth entry of the *mini-plan* series, following
`mini09-devtimescale-ceiling-and-credit-governor-plan.md`, whose close-out (§8) left both subjects open.

**Status:** 📋 **SPECIFIED, NOT STARTED** (2026-09-18). To be built on its own branch off `main`.

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

### A2 — The fix: update the lists once per frame, not once per bet

**Design (the default; A1 can overturn it):**
- **Rows never move.** Each list keeps its 100 pooled rows in a fixed order: row 0 is the newest bet, row 99
  the oldest.
- **A bet only records its data** in a 100-entry ring buffer, which is O(1): no node work, no layout.
- **Once per frame, if dirty,** each list rewrites its rows from the ring, newest first. The cost becomes one
  rewrite per frame, whatever the number of bets in it, instead of `Setup + MoveChild` per bet.
- **Rewrite only what changed.** A frame with *k* new bets shifts every row's content by *k*, so all visible
  rows change; rows below the ScrollContainer's visible window can be rewritten lazily when the player
  scrolls. Whether that virtualisation is needed is decided by measurement, not in advance.
- **Where the flush runs:** DiceGame already coalesces its settled-bet UI once per frame
  (`FlushSettledBetUiIfDirty`, after `SimulationService` has settled). The lists flush at the same point.

**Contract that must not change:**
- newest first; the same 100-row scrollback; the same colours and texts;
- `LoadFromHistoricalRecords`, `ClearEntries`, and `AppendHistoricalRecord`, which **BetsHistoryExplorer**
  drives at its own replay pace (mini-plan 04 §2.3). The explorer must render identically; it gets its own
  check in A3;
- the manual burst stays fluid (mini-plan 08): at most one flush per frame is exactly what the paced burst
  already assumes.

**Fallback, if A1 shows the row writes themselves dominate:** virtualise first — rewrite only the ~12 visible
rows per frame, and refresh the rest on scroll.

### A3 — Verify, then re-derive the cap and the budget

1. **Behaviour.**
   - DiceGame's list and the winner grid match the journal's last 100 bets exactly: order, values, colours.
   - After a scene round-trip and after a restart, both show the checkpoint's last bets, as today.
   - BetsHistoryExplorer's replay renders row by row, as before.
2. **The cap sweep again** (mini-plan 09 P3a's protocol): 99 credits × 9000X, Cap/frame 40 → 60 → 80 → 120 →
   40 in one run.
   - Refit `frame ≈ a + b × bets per frame`.
   - **Prediction:** `b` falls from 0.283 ms towards ~0.05 ms, and `a` rises by the per-frame flush cost.
   - Pick the cap that holds D-09.1's 50 fps.
3. **The budget, sized for the slower session** (D-09.6's lesson): repeat the governed 99-credit leg in a
   **second session** and size `BetBudgetPerSecond` for the slower of the two, not the faster.
   **Prediction** (from the refit, to be replaced by measurement): several thousand bets/s, so 99 credits would
   govern well above today's 1700X.
4. **Timestamps:** `verify-bet-journal.js` passes A1–A4 on the sweep's journal. A higher cap means more bets per
   frame, which is exactly where the half-open clamp works hardest.

## 3. Part B — R2-C1's overspend under the governor

### B1 — Measure it directly (ships with A1)

`FrameCostProfiler` gains two per-frame sums:
- **calendar game-seconds advanced**, read as the clock's movement between consecutive frames;
- **retained game-seconds**, the engines' retained simulated seconds × `SpeedMultiplier`, which is what the
  frame actually simulated.

`overspend = Σ calendar advance ÷ Σ retained − 1`, per report and in the CSV. No journal reconstruction
needed. Mini-plan 08's 0.620% was an inference from gaps; this is the quantity itself.

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

1. **Build A1's picker and B1's two sums** (one build) → **run A1** (~2 minutes of play).
2. **Build A2** → **run A3.1–A3.4** (two sessions for the budget).
3. **B3 decides from the data already collected**, plus the one forced-saturation leg.

## 5. Out of scope

- **An adaptive budget** — a Basic Mode refinement option (`PRIVATE_ROADMAP.md`).
- **Other scenes' per-bet UI** (ClientsBetsHistory's live feed, BetsHistoryExplorer's own panels), beyond
  keeping the explorer's rendering unchanged. DiceGame is the scene the budget is sized for.
- **The four unattributed in-sim spikes** (mini-plan 09 P1, H4) — a separate investigation if they matter.
