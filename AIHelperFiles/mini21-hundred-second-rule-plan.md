# Mini-Plan 21 — The 100-second rule: no hole and no stretched gap between two bets

**Series note:** twenty-first of the *mini-plan* series. **Simulation work, measured with runs.** It takes the
roadmap objective opened by mini-plan 20 F round 5, *"A long frame advances the clock past the bets"*, and the
candidate it absorbed, *"Per-bet timestamp fidelity when a frame holds about one bet"*.

**Status:** ✅ **COMPLETE 2026-10-11** (close-out §6), merged from `mini21-hundred-second-rule`. R-global
**981 → 0**, holes 41 → 0, overspend 1.21 % → 0. Part E: a run-going DiceGame entry no longer loads visibly.
**Carried:** the first entry's ~1.3 s history rollback (roadmap). *(Specified 2026-10-10.)*

---

## 1. The rule, and why it is one plan

**The developer's rule (2026-10-10):** *"the most time there should ever be between one bet and the next is 100
in-game seconds."* That is one tick at 1 credit. In general a node's bets are **`100 ÷ credits` game-seconds**
apart (mini-plan 20 Part 0), so:

- **R-global:** no two consecutive player bets more than **100 game-seconds** apart, at any credit count.
- **R-phase:** inside a stretch at a known credit count, no gap above **`100 ÷ credits`** game-seconds.

Both get a float tolerance of **1 ms**, because stamps are `DateTime` ticks and a back-date is a `double`.

**In this game time does not pass without bets.** The clock runs only while the player's autobet runs, and it stops
on Pause, a board vote and stop-on-block; a manual click spans one 100 s tick. So every gap above the rule is the
simulation's fault, not a pause. Starting, stopping and changing credits do not create violations either: a run's
first bet comes one interval after the clock starts, and a credit drop from 99 to 1 is a 100 s gap, which R-global
allows.

**Two mechanisms break the rule, so they are planned together.** Fixing one leaves the count above zero.

| | mechanism | measured | where |
|---|---|---|---|
| **M1 — holes** | A long frame advances `CalendarTimeService` by `delta × rate × the PREVIOUS frame's retained fraction` (R2-C1), while each bet engine keeps at most `BacklogWindowSimSeconds()` and drops the rest. The clock passes time no engine simulated. | 8 gaps of **6–17 game-minutes** in one journal, ids consecutive, balance continuous (mini-plan 20 round 5). Mini-plan 10 B1: **0.62 %** average overspend. | `CalendarTimeService._Process`; `SimulationService` (`BacklogWindowSimSeconds`, `RecordSimTimeRetention`, R2-C1) |
| **M2 — quantization** | Mini-plan 08 gives each frame's **last** bet the clock's exact value and back-dates the others by whole steps. A bet is therefore stamped at the **end of the frame** in which its interval expired, not at the instant it expired. | 1 credit × 9000X: gaps **100 / 150 / 50 s** (470 / 405 / 404 of 1,341). 1 credit × 100X: **99.26–100.38 s**. | `SimulationService.SettleTimestampUtc`, `ClampedStepGameSeconds`, the settle loops |

**Where M1 bites, from the code:** the backlog window is `max(2 sim-s, 1/15 real-s × DevTimeScale)`. At 100X a stall
must exceed **~2 real seconds** to open a hole; at 9000X, **~67 ms** is enough. So the holes are mostly a DEV-speed
artifact, but a multi-second stall at shipping speed makes them too: a scene that loads the full journal while an
autobet runs is the candidate.

---

## 2. The work

### Part A — baseline on today's code (no code change)

**Instruments that already exist:**
- the **bet journal**, read by Claude (field map: `JournalLine` in `BetHistoryRepository`; timestamps are **UTC**,
  while every screen is local, UTC−5, the lesson from mini-plan 20 round 4);
- the **`⏱ Frame cost`** toggle beside DiceGame's DEV time selector. Its `user://logs/*.csv` has
  `calendarAdvanceGameSec`, `retainedGameSec` and `overspend` per window (mini-plan 10 B1).

**Protocol (the developer runs it; Claude reads the files).** Fastest phase first, so retention keeps every phase
(mini-plan 20's lesson). Arm `⏱ Frame cost` before step 1.

1. **99 cr, 9000X, ≤ 10 s.** Then change scene twice (DiceGame → Main Menu → DiceGame) while it runs.
2. **10 cr, 9000X, ~20 s**, with one scene change.
3. **1 cr, 9000X, ~30 s**, with one scene change.
4. **1 cr, 100X, ~60 s.** Then open **Bets History Explorer** once while it runs (the multi-second load is the
   stall that could reach shipping speed), and return.
5. Don't restart before Claude has read the journal and the CSV.

Credits are set with the DEV ladder beside DiceGame's APS selector, without leaving the scene.

**Predictions (registered before the run):**

| | prediction |
|---|---|
| **P-A1** | R-global is violated: at least one gap > 100 s, **at a scene change in the 9000X phases**, each hole ≈ (stall − 67 ms) × 9000 game-s. |
| **P-A2** | 1 cr × 9000X: about a third of the gaps at **150 s** (M2), mean ≈ 100. |
| **P-A3** | 1 cr × 100X: every gap within ≈ 100 ± 1.7 s (one frame of quantization). R-phase is violated by up to ~1.7 s, with no holes. |
| **P-A4** | The explorer stall in step 4 (if longer than ~2 real s) opens **one hole at 100X**; if it is shorter, none. Either outcome is information: it says whether M1 reaches shipping speed. |
| **P-A5** | The Frame cost CSV shows overspend concentrated in the windows that contain the scene changes. |

#### Part A — results (developer's run 2026-10-11; journal and CSV read by Claude)

Run start: game 2012-01-23 18:00:08 local = 23:00:08 UTC. **87,100 bets, 0 id breaks, 0 balance breaks, 0 backwards
stamps.** The developer changed scene twice in steps 1 and 3 (once was planned): more stalls, which only helps.

| phase | gaps | R-phase violations | R-global (> 100 s) | min / p50 / p99 / max (s) |
|---|---|---|---|---|
| 99 cr, 9000X | 63,375 | 182 | 13 | 0.085 / 1.010 / 1.011 / **1,134.8** |
| 10 cr, 9000X | 20,725 | 34 | 4 | 2.500 / 10.000 / 10.000 / 756.3 |
| 1 cr, 9000X + 100X | 2,999 | 964 | 964 | 3.334 / 100.000 / 150.342 / 826.2 |
| **all** | **87,099** | | **981** | holes ≥ 150 s: **41** (3 ≥ 1,000 s, 8 at 300–1,000, 30 at 150–300) |

**Frame cost CSV:** overspend **1.21 %** over the run, twice mini-plan 10's 0.62 %. By window: **2.4 % and 3.0 %** in
the 99-credit windows (frame max 369 ms and 98 ms, per-frame cap bound in 13–17 % of frames), 0.8–1.4 % in the
other windows with long frames, and **exactly 0** in every window without one.

| prediction | verdict |
|---|---|
| **P-A1** holes at the scene changes in the 9000X phases | ✅ 41 holes of 150–1,135 s; one more at the **run's start** (1,020 s before the first bet) |
| **P-A2** a third of the 1 cr × 9000X gaps at 150 s | ✅ **964 of 2,999 (32 %)**, in the exact `150 50 100` cycle |
| **P-A3** 1 cr × 100X within 100 ± 1.7 s | ⚠ mostly 99.96–100.8, but **one 106.35 s gap followed by 93.07 s**: a stall's frame stamped late at 100X too |
| **P-A4** does the explorer's load open a hole at 100X? | ✅ **No.** Its stall measured **320 ms** (the CSV window's max frame), far under the ~2 s that 100X needs. Holes stayed a DEV-speed effect here; the stamping error did not |
| **P-A5** overspend concentrated in the windows with stalls | ✅ zero without them; highest where stalls and the per-frame cap coincide |

**The finding that changes Part B: at 99 credits a hole is mostly bets stamped LATE, not bets lost.** Every one of
the ten holes in the 99-credit phase is followed by **exactly 179 gaps of 1.010 s**, which is one full frame at
`MaxBetsPerFrame` = 180, anchored on the clock. After those come **compressed** gaps (mean 0.28–0.95 s), and in
some cases stretched ones (1.5–2.5 s). That is the engine's carried backlog (retention stays 0.99) executing over the
following frames at the cap, **stamped into the frames where it executes instead of into the stretch where its
intervals expired.** So:

- **B1 alone cannot pass the count at high credits.** Capping the clock at the backlog window still lets a stall frame
  advance ~600 game-s while the cap executes 180 bets: a ~420 s hole, then compressed catch-up.
- **The hole and the quantization are the same defect at two sizes:** a bet's stamp is anchored on the CLOCK, and the
  clock is not where the bet's interval expired.

**Revised direction for B + C, to be approved before building:** stamps are anchored on the **engine's own simulated
frontier** (the game instant up to which it has executed intervals), not on the clock. Each bet is stamped at
`frontier + interval × SpeedMultiplier`, so spacing is exact by construction and carried bets land in the stretch where
they belong. **The clock then advances by what the engines EXECUTED this frame, never by more**, so it cannot open a
stretch no bet occupies, and a stall makes the game slower, never emptier. With several engines (player, bots) the
clock follows the executed fraction, the same power-weighted aggregate R2-C1 already computes, but measured this frame
instead of the last. **This is Option B2 from the start, merged with C**, because the data refutes B1's sufficiency
before it is built. The mini-plan 08 contract still holds: the clock ends each frame at or after the player's last
stamp by less than one interval.

**Also observed, by the developer:** a brief freeze when re-entering DiceGame at 9000X. The CSV puts the worst frame of
those windows at **369 ms**, and at 9000X that is ~3,300 game-s, exactly the size of the larger holes. The stall is the
holes' trigger, so it is worth measuring on its own (Part E, proposed below).

#### Parts B + C — built (2026-10-11), approved as the revised direction

**One span per frame, shared by every engine and by the clock** (`SimulationService._Process`):

1. Real time owed accumulates in one shared backlog, `_simBacklogSeconds`, clamped to the same `BacklogWindowSimSeconds()`
   as before. The excess is the only time ever dropped, and it is now dropped **from the game too**.
2. Each frame takes a **span** from it: no more than any running engine can execute under `MaxBetsPerFrame` (player
   and every bot), and no faster than `CalendarTimeService.MaxGameSecondsPerRealSecond`, a ceiling now held at the
   line that spends the rate.
3. **The clock advances by exactly the span, before the bets** (`CalendarTimeService.AdvanceGameSeconds`; the calendar's
   own `_Process` stands down while `DrivenBySimulation` is set, from run start to run stop).
4. Every engine's accumulator receives the same span. **After a bet's interval is subtracted, what remains in the
   accumulator is how long before the frame's end that interval expired**, so the back-date is simply
   `accumulator × SpeedMultiplier`. Spacing is `interval × SpeedMultiplier` exactly, within a frame and across frames.

**Contract checked before the change, as C required.** The player's own block captures its bet's back-date
(`CaptureCheckpoint(_settleBackdateGameSeconds)`, mini-plan 08 D4), the same value its stamp uses. Bot, founder and
scheduled blocks capture at the frame's clock, which is at or after every player stamp of the frame. **Both still
hold**, because the clock is advanced to the frame's end before any bet, exactly as the calendar did before.

**Deleted, not flagged (Convention 3):** `ClampedStepGameSeconds` and `PlannedBetsThisFrame`, mini-plan 08 P3's
clamp. It existed because the clock and the engines moved by different amounts; they no longer do.

**Kept as a check:** the Frame cost CSV's `overspend` column now compares the clock's actual movement with the span.
It must read **zero**; anything else is a second writer moving the clock during a run. R2-C1's retained fraction no
longer moves the clock; it remains the `Sim:` readout and the difficulty trace's saturation figure.

**Predictions for the re-run (Part A's protocol, unchanged):**
- **P-BC1:** R-global **0** and R-phase **0** violations (1 ms tolerance), at every credit count and speed, including
  across the scene changes.
- **P-BC2:** the Frame cost CSV's overspend is **0.000 %** in every window.
- **P-BC3:** no backwards stamps, no id or balance breaks (as in the baseline).
- **P-BC4, the cost:** during a stall the clock advances less, so the game is slower in wall-clock terms around
  scene changes. Game-days per real minute, baseline against this run, compared on the same machine. It is a
  cross-session comparison, so it is stated as an observation and not as a pass condition (§40.12 rule 3).

**Results (developer's run 2026-10-11, start game 2012-01-27 15:58:06 local = 20:58:06 UTC; read by Claude):**

| | baseline (Part A) | B + C |
|---|---|---|
| bets | 87,100 | **148,407** |
| **R-global** (gap > 100 s) | 981 | **0** |
| holes ≥ 150 s | 41 | **0** |
| distinct gap values | thousands | **five:** 1.0100 / 1.0110 (99 cr: 1.0101 s, split by the reading tool's millisecond rounding), 10.0000, 100.0000, and one 99.9990 (the same rounding) |
| R-phase hits | 182 / 34 / 964 | 17 / 17 / 0 — **every one is the first gap after a credit change**, falling inside the previous phase's window. **0 genuine** |
| Frame cost overspend | 1.21 % (2.4–3.0 % at 99 cr) | **−0.000000 in all 13 rows** |
| id breaks / balance breaks / backwards stamps | 0 / 0 / 0 | **0 / 0 / 0** |

- **P-BC1 ✅, P-BC2 ✅, P-BC3 ✅.**
- **P-BC4, the cost, as an observation (cross-session, §40.12 rule 3):** game-seconds per real second from the CSV
  rows. At 99 credits: 8,855–9,156 before, 8,652–8,933 after. At 1 credit × 9000X: 8,875–9,175 before, 8,885–9,019
  after. **The "lost" speed is the overspend itself.** The baseline ran above the 9,000 ceiling in places, because the
  clock was advancing time no engine simulated. That time is now simply not added.
- **The developer's observation:** "the freeze when entering DiceGame was completely corrected, and no freeze in the
  flow of bets at any speed." Remaining: **~1 real second of loading before DiceGame appears**, at any speed. That is
  Part E's subject: a load time, no longer a simulation hole.

**⚠ An incident while building it, recorded under Standing Convention 14.** A `sed -i` insertion whose line-number
variable came back empty lost its address and **appended the new line after every line of `SimulationService.cs`**
(1,746 copies), and reported nothing. Caught on the next read, before any build or stage. Repaired by removing the
exact line (`grep -vxF`), then **verified against git**: line endings unchanged, every changed hunk inside the four
intended regions, no duplicated consecutive lines, the intended line present once. The rest of the plan's edits used
the file tool. **The trap is the one the convention describes: an address-less `sed` is a whole-file edit that looks
like a one-line one.**

### Part B — M1: the clock may not outrun the engines on the frame that matters

> **⚠ Superseded by the revised direction in "Part A — results" above, pending the developer's approval.** B1 is
> kept below as the record of what was refuted, and why.

**Option B1 (preferred): cap the clock's per-frame advance at the backlog window.** `CalendarTimeService` advances
at most `BacklogWindowSimSeconds() × SpeedMultiplier` game-seconds in one frame: the same amount the bet engines
can keep. A stall then makes the game **slower**, never **emptier**. It is local to the line that spends the rate,
which already holds `MaxGameSecondsPerRealSecond` for the same reason ("a guard can only protect against writers not
yet written"). The window's formula moves to one shared place, read by both services: one value, two consumers.

**Option B2 (held in reserve): SimulationService corrects the clock this frame.** After the engines have measured
this frame's retained fraction, pull the calendar back to `previousClock + retained game-seconds`. This removes
R2-C1's one-frame lag entirely, saturation included, but it writes the clock from a second place. **Chosen only if
B1 leaves overspend that is not stall-driven** (P-B2 below refuted).

**Predictions for B1:**
- **P-B1:** with Part A's protocol, **zero holes**: no gap above 150 s (M2 is still present).
- **P-B2:** the Frame cost CSV's overspend falls from Part A's value to **≤ 0.05 %**. Whatever remains is R2-C1's
  saturation lag, which B1 does not touch. If it stays above that, B2 is taken.
- **P-B3, the cost:** during a stall the clock advances less, so `Sim:` readout dips are expected at scene changes.
  Blocks per real hour at 9000X may drop slightly. **Measured, not argued:** blocks per real minute over the 9000X
  phases, Part A against Part B, **within one session** (§40.12 rule 3: a per-block figure does not reproduce across
  sessions here).

### Part C — M2: stamp each bet at the instant its interval expired

Today: within a frame, bet *k* of *n* is stamped `clockNow − (n − 1 − k) × step`, so the last bet sits on the
clock. **Proposed:** stamp it at the frame's starting clock plus the simulated time at which the engine's
accumulator crossed that bet's interval, converted to game time. Spacing then comes out at exactly `interval ×
SpeedMultiplier` (100 ÷ credits), whatever the frame boundaries.

**The contract this touches, named before it is touched.** Mini-plan 08's rule is that the last bet of a frame takes
the clock's exact value, because the canonical rule says *the calendar equals the timestamp of the event that most
recently defines the world*. Under C the last bet sits **at or before** the clock, by at most the accumulator's
leftover (less than one interval). That is already tolerated: CLAUDE.md records "an offset of up to one frame for
bot-mined blocks", and a player block's checkpoint **freezes the clock onto the captured instant** (mini-plan 08 D4,
`FreezeCalendarAtBlockStop`). **C must verify, not assume, that every capture path still reads the mining bet's own
instant.** A path that read "the clock" would now be off by up to one interval: the check is a grep for every
checkpoint capture and every `SettleTimestampUtc` consumer, listed in the plan before any change.

**Predictions for C:**
- **P-C1:** 1 cr × 9000X: every gap **100.000 s ± 1 ms**. 10 cr: 10.000. 99 cr: 1.0101.
- **P-C2:** 1 cr × 100X: every gap 100.000 ± 1 ms (today 99.26–100.38).
- **P-C3:** monotonicity holds: zero bets stamped before their predecessor (mini-plan 08's verifier,
  `Tools/verify-bet-journal.js`, run on the result).
- **P-C4:** the checkpoint's clock still equals the mining bet's timestamp for every player-mined block in the run.

### Part E (proposed after Part A) — the DiceGame re-entry stall

**Developer's observation:** re-entering DiceGame at 9000X shows a brief frozen adjustment. **The CSV:** frames of
up to 369 ms in the windows with scene changes. This is the trigger of the holes. After B + C a stall no longer
creates a hole, but it is still a visible freeze. **First a measurement, not a fix:** time DiceGame's entry
(`_Ready`, the initial bet-list load, the first frames) with the existing DEBUG instruments plus a one-shot entry
timer, and name the stage that costs the most. A fix is decided from that number.

**E1 — the instrument (built 2026-10-11, DEBUG-only).** `Scripts/Diagnostics/SceneEntryProbe.cs` prints one
`[SceneEntry]` line per entry to the Godot editor's **Output** panel:

- `load .tscn`: `SceneManager.Go` now loads, then swaps (`GD.Load` + `ChangeSceneToPacked`, which is what
  `ChangeSceneToFile` did inside), so the load is timed separately;
- `toReady`: the deferred swap, the old scene's exit, instantiation, and **every child's `_Ready`**, which Godot runs
  before the root's;
- `_Ready`, split into DiceGame's stages: services, `networkRoot` (a `new NetworkRoot()` added on every entry),
  nodes + signals + APS, DEV controls, `UpdateAllUI`, node selector, legacy restore, history bootstrap, the recent
  100 bets, node state + calculator, sim binding;
- `toFirstFrame`: from the end of `_Ready` to DiceGame's first `_Process`.

**Prediction P-E1:** the ~1 s splits mostly between `load .tscn` + `toReady` (engine-side) and two `_Ready` stages
(`networkRoot` and `recentBets`), not evenly. **The first entry after launch is cold** (JIT, resource cache) and
is read separately from the repeated ones.

**E1 results (developer's run 2026-10-11, Output panel). P-E1 REFUTED:** the engine side is cheap (`load .tscn`
14–17 ms warm, `toReady` 18–29 ms) and `networkRoot` costs 0.1 ms. There are **three separate costs**, none of
them predicted:

| entry | total | where |
|---|---|---|
| 1st after launch, no run | **2,301 ms** | `legacyRestore` **1,829 ms**, once per process (0.0 after). That is `RestoreLegacyCheckpointIfNeeded` → `RollbackHistoryToUtc`, the checkpoint's history rollback |
| 2nd–4th, no run | **158–178 ms** | `recentBets` 62–68 ms; the developer saw these as instant |
| **with the run going, 9000X × 99 cr** | **1,384–1,491 ms** | `nodeState+calculator` **658–740 ms**, `simBind` **350–375 ms**, `toFirstFrame` **265 ms** |

The developer's observation matches: a load only on the first entry, and about one second on each entry **while the
autobet runs**. Both `nodeState+calculator` and `simBind` mostly set UI values (the strategy panel, the chance
slider), which fire signal handlers, so the cost is probably in a handler, not in the calls themselves. **The code
cannot say which; E2 splits them further:**

- `nodeState+calculator` → `betViewToggle`, `nodeFinancialState`, `strategySnapshot`, `bankrollFunded`,
  `missingNodeStates`, `calculator`;
- `BindToRunningBackgroundAutobet` → `slider+toggle`, `applyStrategy`, `saveSnapshot`, `panelState`, `runLock`,
  `readyDots+reseed`;
- the one-time restore → `rollbackHistory` and `clock`;
- `toFirstFrame` → the simulation's own frame within it, and the real delta it was handed. After a ~1.2 s stall the
  run's first frame catches up, bounded by `MaxBetsPerFrame` per engine.

**A fix is chosen only from E2's numbers.** The one-time `rollbackHistory` touches Pattern 2's restore and the
INC-003 history, so it gets the most caution of the three.

**E2 results (developer's run 2026-10-11):**

| entry | the stages that matter |
|---|---|
| 1st after launch | `restore:rollbackHistory` **1,319 ms**; every other stage is small |
| with the run going | `strategySnapshot` 319–372 · `calculator` 328–331 · `bind:applyStrategy` 336–369 · `toFirstFrame` 261–276, of which the simulation frame is 2–17 ms |
| without the run | the same three stages at **0.1–4 ms** |

**Three different stages each costing the same ~330 ms pointed to one operation, and it was the martingale
calculator.** `MartingaleCalculator.CalculateFromGameContext` frees every row and **instantiates up to 500 row
scenes** (`maxRows = 500`) on each update, **while the calculator is hidden**. The developer's strategy (0.001 SC
base, 0 % increase, ~31 SC bankroll) always reaches the cap. With the run going, DiceGame's entry updates it three
times: `ApplyStrategySettings` twice, through the strategy snapshot and the run binding, plus the explicit refresh.
That is 3 × ~330 ms, ~0.66 ms per row. The 1,500 discarded rows are freed after `_Ready`, which accounts for the extra
~220 ms of `toFirstFrame`. Without a run the panel has no base bet yet, and the calculator stops at "Waiting for valid
strategy values". **The same rebuild also ran on every throttled refresh during a run**, unseen.

**E3 — fixed: the table is built only where someone can see it.** `UpdateFromGameSettings` still records every input
at once. While the calculator is hidden it marks the table pending, and `Open()` builds it. `OnOpenCalculatorPressed`
now refreshes **before** `Open()`, so opening builds the rows exactly once from fresh inputs; the old order would
have built them twice under the lazy rule. `Open()` is the only way the calculator is shown (grep), so a pending
table cannot be seen unbuilt. It is mini-plan 10's rule for the bet list, applied here.

**Prediction P-E3:** with the run going, an entry falls from ~1.4 s to roughly what an entry without a run costs
(~150–200 ms): `strategySnapshot`, `calculator` and `bind:applyStrategy` under 5 ms each, `toFirstFrame` near 45 ms.
Opening the calculator still shows the full table. **The first entry after launch is unchanged**:
`rollbackHistory` is a separate cost, left for a decision of its own.

**E3 results (developer's run 2026-10-11): every prediction held**, and with a run going an entry no longer
loads visibly. The full table appears when the calculator is opened.

**E4 — found by the same test: the calculator did not cover DiceGame.** Two causes, both in the screenshot. (1)
DiceGame builds controls in code AFTER the calculator (the DEV block, the Bet View toggle, the DEV credits ladder),
and Godot draws and routes input to later siblings first, so they sat on top of it. Fix: `Open()` calls
`MoveToFront()`. Every one of those controls shares its parent (the DiceGame root, checked against each `AddChild`),
so it is above all of them, for drawing and input. (2) The panel used the default theme's semi-transparent style, so
the bet list showed through the table. Fix: an opaque `StyleBoxFlat` set in `_Ready`. The full-screen backdrop stays
a deliberate 72 % dim, and the `.tscn` layout is untouched.

**Not fixed here, and why: the one-time `rollbackHistory` (1.3 s, first entry only).** It is the checkpoint
restore's history rollback, Pattern 2's guarantee that a restart reverts to the last mined block. Making it cheaper
means changing where or how the journal is loaded and rolled back, the area INC-003 and INC-004 were about. It is
measured and recorded, with its own proposal to come, rather than changed in a UI-load pass.

### Part D — the count, and close

Part A's protocol again on the final code. **Success = R-global 0 and R-phase 0 violations** (1 ms tolerance),
P-C3 and P-C4 holding, and the blocks-per-minute cost from P-B3 stated. Then: CLAUDE.md's Time section gains the
rule as a canonical statement, the roadmap objectives move to DONE, and **the explorer's As Played one-second trim
(mini-plan 20 F) is kept**, because journals written before this plan still hold holes. Its comment is updated to
say so.

---

## 3. What this plan does not do

- **It does not repair journals already written.** Their stamps are the record of what the simulation did, and this
  project does not rewrite history in place (mini-plan 08 §5). The next world wipe clears them.
- **No world wipe and no format change.** The journal's fields are unchanged; only the values written from now on
  change.
- **Not the explorer's frame cost or the 10,000-bet mismatch.** Both remain their own roadmap entries.

## 4. Order

1. **A** — baseline, no code. Its numbers can refute the premises (P-A1 to P-A5) before anything is built.
2. **B1** — the clock cap. Re-run A's protocol; B2 only if P-B2 fails.
3. **C** — the stamping, after B, so a gap above 100 s can only be M2's.
4. **D** — the count and the close-out.

Each part is one stage → ask → commit unit.

## 5. Invariants (every commit)

- **I1** `dotnet build` clean; locale detector at its baseline, re-run (17 / 0 / 0 / 0).
- **I2** No headless launch; the developer runs, Claude reads `user://` files itself and names the panel for anything
  printed.
- **I3** Compare costs **within one session**, and state a cost criterion as a count (§40.12 rules 3 and 6).
- **I4** Any new eager writer calls `WorldWriteGuard.RefuseWrite` first (Pattern 2). None is expected.
- **I5** Times written into a protocol are converted to the screen's local time and say so.

## 6. Close-out (2026-10-11)

**The developer's rule holds.** Never more than `100 ÷ credits` game-seconds between two consecutive bets of a node,
at every speed and credit count, across scene changes.

| | baseline (A) | final (B + C) |
|---|---|---|
| bets in the run | 87,100 | 148,407 |
| **R-global** (gap > 100 s) | **981** | **0** |
| holes ≥ 150 s | 41 | 0 |
| distinct gap values | thousands | five, the exact interval at each credit count |
| Frame cost overspend | 1.21 % | 0.000 % in every row |
| id / balance breaks, backwards stamps | 0 / 0 / 0 | 0 / 0 / 0 |

Part D's count is the B + C run: Part E touched no simulation code, so that run stands as the final measurement.

**What it took, and how the plan moved.**

1. **The specified fix was refuted by the baseline before it was built.** Every hole at 99 credits was followed by
   exactly 179 gaps at 1.010 s: a `MaxBetsPerFrame` frame anchored on the clock. The holes were bets stamped late,
   not bets lost, so capping the clock (B1) could not pass the count. **The holes and the quantization were one defect
   at two sizes:** stamps were anchored on the clock, and the clock was not where a bet's interval expired.
2. **The fix that replaced it:** one simulated span per frame, shared by every engine and by the clock. The clock
   advances by exactly that span, and a bet's back-date is its engine's remaining accumulator. Spacing is exact by
   construction, and mini-plan 08's clamp had nothing left to do and was deleted.
3. **Part E, the ~1 s DiceGame load**, measured before it was fixed. P-E1 was refuted: the engine side was cheap.
   Three separate costs turned up. The run-only one was the **hidden martingale calculator instantiating 500 rows
   three times per entry**; it is now built only when opened. Its test exposed an overlay defect (E4): code-built
   controls drawn above the calculator, and a translucent panel.

**Carried, with a home in the roadmap:** the first DiceGame entry after launch still spends ~1.3 s in
`RollbackHistoryToUtc`, the checkpoint's history rollback (Pattern 2). Measured, deliberately not changed here.

**Documentation moved with the code:** CLAUDE.md's Time section states the rule canonically, and "tick" now says a
tick holds one bet per credit (mini-plan 20 C2b's clarification, done here); autoload row #3 says who moves the clock.
SERVICES.md marks `SimulationThrottle` as a readout. The two IMPLEMENTATION_STATUS lines that still listed R2-C1's
overspend as open are marked closed. The explorer's As Played trim is kept for journals written before this plan.
**CLAUDE.md is at ~85,900 characters**, past its 85,000 target and under the 95,000 warning; the growth is a rule,
not a case (its own policy's test).

**Lessons.**

- **A baseline can refute the fix, not only the premise.** Part A was specified to count violations. Its most
  valuable output was the 179-gap signature, which retired option B1 before any code existed.
- **Three equal costs in three different places point to one operation.** E2's ~330 ms × 3 was one hidden rebuild.
- **An address-less `sed -i` is a whole-file edit** (Convention 14). It appended a line after every line of a 1,700-line
  file and reported nothing. Caught on the next read, repaired, and verified against git.
- **"Paint what can be seen" applies beyond lists.** The calculator broke mini-plan 10's rule in a place nobody
  thought of as a list.
