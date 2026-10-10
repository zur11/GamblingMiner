# Mini-Plan 21 — The 100-second rule: no hole and no stretched gap between two bets

**Series note:** twenty-first of the *mini-plan* series. **Simulation work, measured with runs.** It takes the
roadmap objective opened by mini-plan 20 F round 5, *"A long frame advances the clock past the bets"*, and the
candidate it absorbed, *"Per-bet timestamp fidelity when a frame holds about one bet"*.

**Status:** 📝 **SPECIFIED 2026-10-10**, branch `mini21-hundred-second-rule` (proposed). Nothing built.
**Next:** Part A, the baseline run on today's code.

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

### Part B — M1: the clock may not outrun the engines on the frame that matters

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

## 6. Close-out

*(Written at close-out.)*
