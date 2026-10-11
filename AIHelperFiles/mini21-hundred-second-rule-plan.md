# Mini-Plan 21 — The 100-second rule: no hole and no stretched gap between two bets

**Series note:** twenty-first of the *mini-plan* series. **Simulation work, measured with runs.** It takes the
roadmap objective opened by mini-plan 20 F round 5, *"A long frame advances the clock past the bets"*, and the
candidate it absorbed, *"Per-bet timestamp fidelity when a frame holds about one bet"*.

**Status:** 📝 **SPECIFIED 2026-10-10**, branch `mini21-hundred-second-rule` (proposed). Nothing built.
**Part A DONE 2026-10-11** (results below Part A). **Next:** the developer approves the revised B + C direction (stamps anchored on the engine frontier; the clock advances by what was executed) and the proposed Part E.

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
