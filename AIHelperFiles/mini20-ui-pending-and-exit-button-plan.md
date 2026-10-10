# Mini-Plan 20 — Pending UI objectives, an Exit button, and a locale pass that could not see month names

**Series note:** twentieth of the *mini-plan* series. **UI work, small code changes, no performance runs.** It takes
the two open UI objectives most recently written into `PRIVATE_ROADMAP.md` §5, adds the developer's request for an
**Exit button on `MainMenu`**, and adds one defect found **while specifying this plan**: five culture-sensitive
month-name renders that the locale detector's pass 4 cannot see.

**Status:** 📝 **SPECIFIED 2026-10-08**, branch `mini20-ui-pending-and-exit-button`. Nothing built.
**D-20.1 approved** (confirmation dialog). **D-20.2 SUPERSEDED**: approved as `HH:mm` + tooltip, then
reworked as a spacing rule, then dropped when the developer asked why 1 credit at 100X should show less than 99
credits at the same speed. **The column follows motion only** (the clock's unit), and the tooltip stays (§2 C2).
**Part 0 DONE 2026-10-08 (two rounds)**: the clock and counter hold, and their slow return to full precision is
fixed (≤ 1.05 s measured). **Round 3 passed** (the bet column follows motion). **F built**: the explorer's two
replay paces. **Next:** the developer tests F in both paces, then A.

---

## 1. Scope

| Part | What | Source |
|---|---|---|
| **0** | A **visual diagnostic**: the clock, the attempts counter and the bet rows while DevTimeScale and hardware credits change | the developer, 2026-10-08 (approval of the specification) |
| **A** | An **Exit** button on `MainMenu` that closes the app | the developer, 2026-10-08 |
| **B** | `CalendarsNavigator` **snapshots the present on arrival**, except when arriving from `BetsHistoryExplorer` | roadmap "Calendar entry date", requirement recorded 2026-08-24 (`mini06-…-plan.md` §9.7e) |
| **C** | The bet-history row's **timestamp strobe** | roadmap "The bet-history row's timestamp strobes", developer's report 2026-10-06 |
| **D** | Five **month-name renders without `InvariantCulture`**, and the pass-4 regex that misses them | found while specifying C |
| **E** | DiceGame's disabled **"Auto bets per second" dropdown → a plain label** stating the credits in use | the developer, 2026-10-08 |
| **F** | `BetsHistoryExplorer` gains a **pace toggle**: As Played (default, game time) and Per Bet (a chosen credit count × the 1x–10x speed, whatever hardware recorded the bets) | the developer, 2026-10-09, after round 3 |

**Out of scope, with the reason.** These are also open UI objectives on the roadmap, but each is a full design and
not a pending fix:

- **Player Holdings Hub** is a new screen with a StatusBar badge. It needs its own plan.
- **Betting Statistics scene** is a new screen, and it carries the rollup's epoch-key decision (mini-plan 02 §D.6).
  It needs its own plan.
- **P2 auto-recharge UI labels/warnings** is older (Basic Mode checklist `[~]`) and is not a recent addition.

---

## 2. The work

### Part 0 — Do the readouts follow DevTimeScale and hardware credits? (visual diagnostic, before any change)

**The developer's question:** when DevTimeScale is changed, or hardware credits are added and removed, do the clock,
the attempts counter and the bet rows adjust? Mini-plan 15 B fixed the clock and counter, but no one has watched
them while these two dials move.

**What the code says, read before predicting** [V]:

- **Clock.** `AdaptiveReadoutSampler` measures the clock's rate **from the clock itself**, not from
  `DevTimeScale` [V: `ReadoutSampling.cs` class comment]. So it follows DevTimeScale, the throttle and a pause.
  Hardware credits do **not** move the clock: the calendar runs at `SpeedMultiplier × DevTimeScale` whatever the
  bet rate is.
- **Attempts counter.** `CounterQuantizer` measures the step between two samples [V: `NoteSample`]. So its
  quantum follows **both** dials, since the step is credits × time-per-sample. It floors in decades and marks
  the value with `~`. Taking hardware away should drop the quantum again, because the loop also divides down.
- **Bet rows. This is the part the plan had wrong.** A bet occupies `interval × SpeedMultiplier` game-seconds,
  with `interval = 1 / HardwareRate`, where `HardwareRate` is the node's credits clamped to 1..99
  [V: `SimulationService` ~l.706 and `HardwareRate`]. **Two different quantities live in the bet rows, and only
  one of them is independent of DevTimeScale** (corrected 2026-10-08 after the developer challenged a first
  draft that said "DevTimeScale cancels" and stopped there):

  - **(a) The game-time gap between ADJACENT rows = `100 ÷ credits` game-seconds, independent of DevTimeScale.**
    The clock advances `100 × DevTimeScale` game-s per real second [V: `CalendarTimeService._Process`]. The node
    places `credits × DevTimeScale` bets per real second, because `simDelta` is scaled by the same factor
    [V: `SimulationService` ~l.704]. The ratio drops DevTimeScale, and that is deliberate: the code comment there
    keeps *attempts per in-game second* invariant so difficulty dynamics do not depend on the dev speed. At 1
    credit the gap is 100 s (the 3-residue strobe), at 2 credits 50 s, at 10 credits 10 s, and at 99 credits ~1 s.
  - **(b) What the player SEES change from one frame to the next DOES depend on DevTimeScale.** DiceGame's list
    repaints once per frame [V: `BetHistoryContainer`, mini-plan 10 A2]. At 9000X the whole list scrolls 90× faster,
    and the top row's timestamp jumps by about the clock's per-frame advance (~150 game-s at 60 fps). That is the
    same `150 mod 60 = 30` that froze the clock before mini-plan 15 B, and it is independent of credits.
    `BetsHistoryExplorer` replays at its own pace, so (b) does not apply there. Its timestamps are the same
    journal values, so (a) does.

  **So the column can strobe along two axes**: down the list (credits) and frame to frame (DevTimeScale). Part
  C's format must answer both. A fixed `HH:mm`, as approved in D-20.2, fails (a) at high credits (~60
  identical-looking rows per minute at 99 credits). A throttled frame (`SimulationThrottle` < 1) compresses (a)
  further (the mini-plan 08 P3 clamp).

  **The premise behind part C, restated precisely.** CLAUDE.md's "1 bet **tick** = 100 in-game seconds" is
  correct at every credit count. A tick is not a bet: a manual click is a burst of `credits` bets spread across
  one 100 s tick (`GameSecondsPerManualBet / attempts` [V: `DiceGame` ~l.1091]), and an autobet second at base
  speed is the same tick. What holds **only at 1 credit** is "1 **bet** = 100 s". That form appears in part C's
  original diagnosis, in the roadmap objective, and literally in `ProjectDesignManual.md` ~l.5057 ("1 bet = 1
  nonce attempt = 100 in-game seconds").

**Predictions, registered before the run:**

| | 1 → many credits | 100X → 9000X |
|---|---|---|
| **P-0.1 Clock** | no change in format or cadence | finest field coarsens (seconds → minutes → hours) and recovers when lowered, with no format flapping |
| **P-0.2 Attempts counter** | quantum rises (`~` appears, low digits drop) and falls back when credits are removed | quantum rises and falls back the same way |
| **P-0.3a Bet rows, adjacent gap** | shrinks to `100/credits` s; the seconds field down the list stops 3-cycling and counts up | **no change** |
| **P-0.3b Bet rows, frame to frame** | no change | the list scrolls ~90× faster; the top row's timestamp jumps ~150 game-s per repaint, and its seconds field alternates between two values (the pre-mini-15 clock shape) |

**Protocol (the developer runs it; Claude reads the journal afterwards):**

1. DiceGame, Bet View **Detailed**, autobet running, **100X**, **1 credit** on the player node. Watch the clock,
   the "own nonce attempt" line and the bet rows for ~20 s.
2. Raise DevTimeScale to **9000X**, watch ~20 s, then lower it back to 100X.
3. In **Mining Pools & Hardware**, raise the player node to **10 credits**, return to DiceGame, watch ~20 s. Then
   **99 credits**, same. Then back to **1**. The autobet keeps running across these scene changes.
4. Say which of P-0.1…P-0.3b looked wrong. Note the approximate order of the steps; exact times are not needed.
   **Do not restart the app before Claude has read the journal** if the world is still pre-genesis.

**Claude then reads the journal itself** (field map: `JournalLine` in `BetHistoryRepository`). Δt between
consecutive player bets should step **100 → 10 → ~1.01 → 100 s** at the credit changes, and stay flat across the
DevTimeScale change (P-0.3a). P-0.3b is a per-frame display effect, so it leaves no trace in the journal and only
the developer's eyes can confirm it. **This measurement replaces C1**: the same data answers P-C1 at 1 credit.

**Decision rule.** If P-0.1 and P-0.2 hold, the clock and counter are signed off with a visual check they never had.
If either fails, it becomes a part of this plan, diagnosed with numbers before anything is changed. P-0.3a and
P-0.3b feed D-20.2 below.

#### Part 0 — results (run by the developer, 2026-10-08)

- **P-0.1 Clock: holds, with one defect.** The format coarsens and recovers as predicted, but **returning from
  9000X to 100X took well over ten seconds** to show seconds again, longer at 99 credits. **Cause, from the
  code:** the rate estimate is smoothed 20% per repaint, and while the readout shows hours or minutes it repaints
  about once a second. The estimate has to fall from 9000 to the finer-unit bar of `120 / 1.15 ≈ 104`, and 100X
  sits only 4% under that bar, so `8,900 × 0.8ⁿ ≤ 4` takes ~35 repaints, plus a 6-repaint confirmation streak.
  **Fixed rather than labelled** (the developer asked for faster recovery first, a "recalibrating" label only if
  it could not be made faster):
  1. a measurement more than **2×** away from the estimate replaces it (`RateSnapRatio`), because that is a dial
     change, not jitter. The confirmation streak still guards the format.
  2. while a **finer** unit waits out its streak, the sampler repaints at the finer unit's cadence. That is safe in
     that direction only: a coarse format painted more often moves less than one of its own steps per repaint.

  Expected recovery: about one repaint in flight (≤ 1 s) plus ~6 × 0.02 s. A DEBUG trace now prints every unit
  change to the **Godot editor's Output panel** (`[Readout] DiceGame clock: Hours -> Seconds …, N s after the last
  rate step`), so the next run measures the recovery instead of us estimating it.
- **P-0.2 Attempts counter: holds, and it is where the 99-credit slowdown was** (developer's clarification). The
  counter has no cadence of its own: `CounterQuantizer.NoteSample` runs only on the clock sampler's repaints
  [V: `DiceGame`, `cadenceSample`], and its quantum is the step **per sample = credits × time between repaints**.
  While the clock was stuck at ~1 repaint/s, 99 credits gained ~100 attempts per sample, forcing `~` and dropped
  digits. At 1 credit it gained ~1 per sample, so it showed every digit even during the slow phase. **Same cause
  as the clock, and the same fix covers it.** A first draft of this entry called the 99-credit effect
  "unexplained" because it looked only at the clock.
- **P-0.3a / P-0.3b Bet rows: not confirmed.** The developer saw no change, as expected before C. **The journal
  read did not find the 1- and 10-credit phases**: the last ~50,000 player bets are all `Δt ≈ 1.00–1.01 s` (99
  credits), with throttled stretches down to ~0.43 s. There are 2,333 isolated 100 s gaps and 11 isolated 10 s
  gaps across the retained journal, but **no run of 20 or more** at either spacing. **Asked rather than inferred,
  and the answer is retention.** The developer did bet at 1 credit first. At 9000X × 99 credits the player places
  ~8,900 bets per real second, and the journal keeps ~190,000–210,000, so **about 22 seconds at that rate prunes
  everything before it.** The protocol ran its fastest phase last, and that phase erased the evidence for the
  earlier ones. **Re-run with the order reversed** (99 credits first, 1 credit last, 9000X kept short at high
  credits); see the re-test protocol below.

  **Lesson for any journal-read protocol here: order the phases slowest-LAST, and size the fastest phase against
  retention**, or the run destroys its own evidence.

**C implemented on the developer's instruction ("apply what you deduce best") ahead of a confirmed P-0.3a**, as
designed above: the column shows the coarser of the spacing unit (minutes at a ≥ 60 s average gap, read from the
ring's newest and oldest rows) and the DiceGame clock sampler's unit (`SetMotionUnit`). The full instant is in
every row's tooltip. `BetsHistoryExplorer` does not pass a motion unit, so it uses the spacing axis only.
*(Superseded before round 3: the spacing axis was removed. See "Part C — the visual check" below.)*

#### Part 0 — round 2 results (run by the developer, 2026-10-08)

**The clock and counter recovery is fixed, confirmed by the trace in the Godot editor's Output panel.** Each
9000X → 100X return reached `Seconds` after **1.05 s** (99 credits), **0.37 s** (10) and **0.25 s** (1). The
prediction was ≤ ~1.2 s at every credit count, and it holds. The developer described the recalibration as
instantaneous. Each 100X → 9000X switch coarsened to `Hours` within 0.12–0.19 s.

**P-0.3a, read from the journal by Claude. The fastest-first order kept every phase:**

| phase | gap between consecutive player bets | verdict |
|---|---|---|
| 99 cr | 6,690 gaps < 2 s (the ~1.01 s phase) | ✅ |
| 10 cr, 100X | **10.000 s** flat | ✅ |
| 10 cr, 9000X | mean **10.0 s**, max 14.2 s | ✅ on average |
| 1 cr, 100X | **99.97 s** | ✅ |
| 1 cr, 9000X | mean **100.6 s**, but per bet **100 / 50 / 150 s** (470 / 405 / 404 of 1,341) | ⚠️ **holds on average, refuted per bet** |

**The refutation is frame quantization, and it is mini-plan 08's rule working as designed.** At 9000X the clock
advances ~150 game-s per frame, and ~1.5 bets fall into each frame. The **last** bet of a frame takes the
clock's exact value (deliberately load-bearing: the calendar equals the timestamp of the event that defines the
world), and the others are back-dated by `interval × SpeedMultiplier` = 100 s. A one-bet frame therefore
stamps at the frame's end, not where its interval expired. Frames alternate 2, 1, 2, 1 bets, which gives the
`100, 150, 50` cycle. **The mean is right and individual gaps are not.** P-0.3a assumed per-bet precision that
the stamping never promised above ~1 bet per frame.

**Not changed by this plan:** it touches mini-plan 08's back-dating contract, which is out of a UI plan's scope.
For the bet column it does not matter: at 1 credit and 9000X the column shows hours (it follows the clock), so
the 50/150 jitter is below the displayed precision. **Recorded as a candidate objective**:
*per-bet timestamp fidelity when there is ≤ ~1 bet per frame*.

#### Part C — the visual check of the bet column (round 3)

Round 2 measured the spacing but not how the column LOOKS. The developer had not understood the column was meant
to change.

**The spacing axis was REMOVED before the check, on the developer's question.** Writing the predictions put
`HH:mm` at 1 credit/100X beside `HH:mm:ss` at 10 and 99 credits at the same speed. The developer asked why the
slowest, most readable case should show the least, and there is no good answer. Those seconds are true values, and
at 100X × 1 credit the list grows one row per real second. **A value that repeats down a still list is not a
strobe; only motion makes one.** The original report already said so ("rows only scroll fast enough to see it
when the autobet is running hard"), and the diagnosis read it as arithmetic down the list. So the column now
follows the clock sampler's unit alone, the tooltip stays, and the spacing code (including a 10-gap window added
an hour earlier to fix its own slow switch-back) is deleted, not flagged (Convention 3).

**Predictions: the column shows the unit the DiceGame clock shows:**

| phase | column shows | e.g. |
|---|---|---|
| 99 cr, 100X | `HH:mm:ss`, consecutive seconds | `02:31:06 02:31:05 02:31:04` |
| 10 cr, 100X | `HH:mm:ss`, stepping by 10 s | `02:31:40 02:31:30 02:31:20` |
| 1 cr, 100X | `HH:mm:ss`, stepping by 1 min 40 s | `02:35:26 02:33:46 02:32:06` |
| any credits, 9000X | `HH'h'`, within ~0.2 s of the switch, same moment as the clock | `02h` |
| back to 100X | `HH:mm:ss` again within ~1 s, same moment as the clock | |
| autobet stopped | `HH:mm:ss` | |
| any row, hovered | tooltip `yyyy-MM-dd HH:mm:ss` | `2012-01-12 02:31:06` |
| `BetsHistoryExplorer` | `HH:mm:ss` always (not motion-driven) | |

**A DEV control for the test, added at the developer's request (2026-10-08).** Round 1 changed credits by
leaving DiceGame for Mining Pools & Hardware at every step. `UI/DevPlayerCreditsSelector` is a ladder (1–5, 10–90,
99) beside DiceGame's APS selector that sets the **player node's** total credits directly. It writes through the
shop's own calls (`AddCredits` → individual pool, `RemoveCredits` → casino pool first, floor 1), so it cannot
produce a state the shop could not. It shows an off-ladder total as its own item, so it never displays a nearest
rung instead of the truth. **Player only, and DEV only**: the shipping control remains Mining Pools & Hardware.

**Re-test protocol (round 2) — fastest phase FIRST, so the journal keeps every phase.** Bet rates per real second:
99 credits = 99 at 100X and ~8,900 at 9000X; 10 credits = 10 / 900; 1 credit = 1 / 90.

1. **99 credits.** 100X ~15 s → **9000X at most ~10 s** (≈ 89,000 bets, under half the retention) → back to 100X
   ~15 s. Note how long the clock and the counter take to show full precision again.
2. **10 credits** (set with the DEV credits control beside DiceGame's APS selector, below). 100X ~20 s → 9000X ~15 s → 100X ~20 s.
3. **1 credit.** 100X ~30 s → 9000X ~15 s → 100X ~30 s. **Ends at 100X × 1 credit**, the slowest phase.
4. Watch the bet rows' time column in each phase, and hover a row for the tooltip.
5. Copy every `[Readout]` line from the **Godot editor's Output panel**. Do not restart before Claude has read
   the journal.

**Predictions for Claude's journal read:** contiguous runs at `Δt ≈ 1.01 s`, then `≈ 10 s`, then `≈ 100 s`,
each flat across its own 9000X stretch (P-0.3a). Each `[Readout]` recovery line after a 9000X → 100X switch
reads **≤ ~1.2 s**, at every credit count.

### A — Exit button on `MainMenu`

**What the code says today** [V = verified while specifying]:

- `MainMenu` has no exit. All 19 navigation buttons are children of `ButtonsScroll/ButtonsVBox` [V: `MainMenu.tscn`].
- `RootMargin` has `margin_bottom = 30` [V]. That is **below** §29.11's ~50 px bottom safe area. So the only
  thing that keeps `MainMenu` correct today is that nothing must-click sits at the bottom. **An Exit button would
  be the first.**
- Closing the window with **X** delivers `NotificationWMCloseRequest`. Two services flush on it:
  `BankrollStateService` and `CasinoScBalanceService` [V: both `_Notification` overrides]. `SimulationService`
  performs the final `NetworkRoot.FlushWorldIfDirty()` in `_ExitTree` [V].
- **`GetTree().Quit()` on its own does NOT deliver `NotificationWMCloseRequest`.** Both services also listen for
  `NotificationPredelete`, so they would probably still flush during teardown. "Probably" is not a quit
  contract, though.

**The rule this part ships with: the Exit button must be no weaker than the window's X.** Implementation:
`GetTree().Root.PropagateNotification((int)NotificationWMCloseRequest)` and then `GetTree().Quit()`. This is
Godot's documented pattern for a custom quit, and it runs every existing close-request handler, plus any future
one, without listing them.

**Layout (read `ProjectDesignManual.md` Ch. 29 before editing the `.tscn`. `MainMenu` has a `ScrollContainer`):**

- Put the button in a **fixed footer that is a sibling of `ButtonsScroll`, never a child of it** (§29.10).
- Raise `RootMargin`'s `margin_bottom` to **≥ 50** (§29.11). `ButtonsScroll` is `size_flags_vertical = 3`, so it
  pins the footer to the very bottom edge. That is exactly the band that can fall off-screen.

**D-20.1 — should Exit ask for confirmation? APPROVED 2026-10-08: yes, and the dialog states what will be
reverted.**
Pattern 2 means a restart reverts the whole world to the last mined block: clock, balances, bets, and the
mempool. The X button does this silently today. A deliberate Exit button is the place to say it once:

- **After the first block:** *"The world will resume from the last mined block (\<game date\>). Bets placed since
  then will be undone."* The date comes from the checkpoint's own `CalendarLocalTicks`
  [V: `BlockSessionCheckpointService.Snapshot`] and is never recomputed. Standing Convention 6: the message shares
  its source with what the restore will actually do.
- **Pre-genesis** (`HasCheckpoint()` false): *"No block has been mined yet. The world will restart at the
  player-start date."*
- Alternative: quit immediately with no dialog. That is simpler, but it hides the one consequence a player
  cannot guess.

**Verification (the developer runs it; no headless launch, per memory):** press Exit. The app closes. The
**Godot editor's Output panel** shows the line `[Exit] close request propagated` (emitted with `GD.Print`, not
`PrintErr`, so it lands where the developer reads). Do this once pre-genesis and once after a block. In both cases
the dialog text must match the state the next launch restores.

### B — Calendar snapshot on arrival

**The requirement** (§9.7e, the developer's words): arriving at `CalendarsNavigator` from any scene **except
`BetsHistoryExplorer`**, the selection becomes **the present at the moment of entry** and then does **not**
auto-update. Arriving **from** `BetsHistoryExplorer`, it adopts the date it arrived from.

**Implementation:** in `_Ready`, before `SyncInputsFromClock()`: if `SceneManager.PreviousScene !=
BetsHistoryExplorer`, call the same line "Set Now" already runs,
`SetExplorerSelectedLocalDateTime(GamePresentLocalDateTime)` [V: `OnSetNowPressed`].

**Facts checked against the code:**

- `PreviousScene` is assigned in `Go()` [V: `SceneManager`]. When the app boots into `MainMenu` without a `Go()`,
  `PreviousScene` is `null` on the first `Go(CalendarsNavigator)`. `null` is not the explorer, so the snapshot is
  taken. That is correct.
- The explorer's Back button returns to `PreviousScene` through `Go()` [V: `BetsHistoryExplorer` ~l.1734]. So
  Calendar → Explorer → Back arrives with `PreviousScene = BetsHistoryExplorer` and adopts the date. That is
  correct.
- **"Does not auto-update" is already true.** The `_Process` presenters read `ExplorerSelectedLocalDateTime`,
  not the live clock [V: `GetCurrentLocalDateTime`]. The snapshot adds no subscription, by design (§9.7e: *a date
  the player is choosing must not move underneath them*).
- `PushScene` overlays do not update `PreviousScene`. No overlay leads to the Calendar today. Say so in a code
  comment so the next overlay route does not break this silently.

**Prediction P-B1.** With the snapshot in place, the silent floor snap (`GetCurrentLocalDateTime() < floor`) no
longer fires on entry, because the present is never below the floor. The explorer opened from the Calendar then
shows **the most recent ≤100 bets**, not the oldest one. Per §9.7e: **do not add a "snapped" notice first.** That
would keep the wrong default and only announce it.

### C — The bet-history row's timestamp strobe

**The diagnosis on file is arithmetic, not a measurement.** The game clock advances 100 game-seconds per bet tick.
`100 mod 60 = 40`, so the seconds field of consecutive rows (`HH:mm:ss`, `BetHistoryItem.cs` [V]) can only cycle
through **three residues**. The roadmap says: *verify before building.* **Part 0 narrows it:** that diagnosis
reads "tick" as "bet", so it holds at 1 credit only. It also sees one axis of two: the frame-to-frame jump of the
top row (P-0.3b) is a second, DevTimeScale-driven strobe that the diagnosis never mentions.

**C1 — measured by Part 0.** The journal read after Part 0's run answers P-C1 at 1 credit:
`Δt` = 100 s and `seconds mod 60` takes exactly 3 values. If it does not, the 3-cycle diagnosis is refuted. Stop
and re-diagnose before choosing a format, and record the refutation.

> **⚠ SUPERSEDED 2026-10-08 (round 3).** The spacing rule below was implemented and then removed on the
> developer's question. The column follows motion only; see §2 Part 0 → "Part C — the visual check". This text is
> kept as the record of how the decision moved.

**C2 — D-20.2, the format.** The seconds are true recorded values, so they are not false precision. They are
spatial aliasing down the list. **Approved (2026-10-08): `HH:mm` in the row, and the full `yyyy-MM-dd HH:mm:ss`
in the row's `TooltipText`.** **Reopened the same day by Part 0's code reading.** Row spacing is `100 ÷ credits`
game-seconds, so a fixed `HH:mm` is right only at 1 credit. At 10+ credits many consecutive rows would show the
same minute. **Revised proposal, to confirm with the developer after Part 0:** the row's finest field follows the
current spacing, the same "precision and step are one decision" rule as §29.13:

- spacing ≥ 60 game-s (1 credit): `HH:mm`
- spacing < 60 game-s (2+ credits): `HH:mm:ss`. At 10 s the seconds count up visibly; at 50 s there is a 6-cycle,
  which Part 0 should show to be acceptable or not.
- the tooltip always carries the full instant.

The spacing is read from the **rows themselves** (the previous row's timestamp), never from the credits value.
A displayed figure shares its source with what it describes (Convention 6), and the throttle can compress
spacing below the nominal value.

**This proposal answers only axis (a).** The frame-to-frame jump of P-0.3b is the clock's own problem, and
mini-plan 15 B already solved that one with `AdaptiveReadoutSampler`, whose rate is measured from the clock.
Whether DiceGame's list should share that sampler's cadence, or coarsen its finest field with it, is decided
**after** the developer has seen P-0.3b. If it is not visibly a problem, it is recorded as observed and left alone.

**C2b — clarify "tick", do not rewrite it.** CLAUDE.md's Time section ("1 bet tick = 100 in-game seconds") is
correct and stays. It gains one clause, *"a tick holds one bet per hardware credit, so a node's bets are
`100 ÷ credits` game-seconds apart"*, so that "tick" is not read as "bet" (which is exactly how part C's
diagnosis and this plan's first draft misread it). `ProjectDesignManual.md` ~l.5057 ("1 bet = 1 nonce attempt =
100 in-game seconds") is the literal credit-1 statement and gets the same note. Both edits go in the same commit
as C2.

**C3 — enumerate the other consumers before closing** (Standing Convention 13). The same arithmetic applies to
any **per-bet** list that renders seconds. The only other one found is `ClientsBetsHistory`'s live feed
(`dd MMM yyyy HH:mm:ss`, DEV scene, ~l.112). It gets the same treatment as C2, or a written reason why not. Event
lists that are not per-bet (transfers, loans, swaps) are **not** in scope: their spacing is not a fixed tick, so
the 3-cycle cannot occur there.

### F — Two replay paces in the explorer, As Played and Per Bet (developer, 2026-10-09)

**Round 3 (the bet column) passed: "everything looked fluid".** Its optional step found this. After a test run
mostly at 99 credits, pressing Play hours or a day back always looked like fast-forward, even at 1X.

**Cause: a deliberate design, not a bug.** Mini-plan 04 paced the replay in GAME time: the cursor walked 100
game-s per real second at 1X and rendered every bet it crossed, so the recorded hardware rate (the spacing
between bets) set the pace. A 99-credit stretch replayed at 99 bets/s, and no speed step could slow it.

**How the decision moved, in one day.** The developer first asked for the pace to be **replaced** by one bet per
second at 1X (built and staged, never committed). On reflection they asked for **both**, behind a toggle, with
the original as the default, because each answers a different question and they want to test in both. A third
step gave Per Bet a **hardware credits selector**, multiplied by the shared 1x–10x speed. The developer's framing:
a session played at 99 credits can be read at 99 × 10x in As Played, and Per Bet must be able to say the same
thing, or anything below it.

| | **As Played** (default) | **Per Bet** |
|---|---|---|
| what it replays | game time, 100 game-s per real second at 1x | `credits × speed` bets per real second |
| a 1-credit stretch at 1x | 1 bet/s | `credits` bets/s (1 at 1 cr) |
| a 99-credit stretch at 1x | 99 bets/s | `credits` bets/s (99 at 99 cr) |
| controls | speed 1x / 2x / 4x / 10x (`_speedSteps`, unchanged) | **Credits** selector (`HardwareCreditLadder`, the same ladder as DiceGame's DEV control, shown only in Per Bet) × the same speed |
| range | as recorded × 1–10 | 1 to 990 bets/s (99 cr × 10x) |
| empty stretches (stopped run, closed app) | walked at the chosen speed | crossed at once |
| the "Selected timeline" label | moves continuously | moves bet by bet, resting on the last bet shown |
| question it answers | *how did it happen?* | *let me read it at the hardware rate I choose* |

**They line up by construction:** As Played over a stretch recorded at N credits and Per Bet at N credits show
the same rate at the same multiplier. Per Bet only removes the gaps and makes N the viewer's choice.

**Neither depends on DevTimeScale.** **Live-follow is identical in both:** at the present, bets appear as the run
settles them. Implemented in `BetsHistoryExplorer`: `ComputeAsPlayedDemand` (the mini-plan 04 demand, restored
unchanged) and `EmitOwedBetsAndSettleCursor` (accrues `PerBetRate()` = credits × multiplier bets per real second
and emits the whole ones). Both go through the same emit step, which still owns rendering and the summary walk.
Reaching the present (`ReachThePresent`) is shared: the multiplier drops to 1x there, the credit count is kept,
and the panel either follows a live run or stops.

**Details, stated so they are not mistaken for bugs:**

- The toggle reads **"Pace: As Played"** / **"Pace: Per Bet"**, naming the pace in effect. In Per Bet the speed
  button shows the resulting rate too, `Speed 2x (198 bets/s)`, so the multiplication is never left to the viewer.
- Switching pace keeps the cursor, the play state, the rows and the multiplier. The credit count is kept while
  hidden.
- The "requested / actual" readout measures in the current pace's unit: game time As Played, bets Per Bet. At the
  top (990 bets/s ≈ 16.5 rows a frame at 60 fps) the 25-row emit budget still holds; a slower frame makes it bind,
  and the readout says so (§6.2).
- The ladder moved to `Scripts/Hardware/HardwareCreditLadder.cs`, read by both selectors, so they cannot offer
  different rungs.
- In Per Bet, a pause banks no bets: Play resumes at the chosen rate, not with a burst. A live run faster than the
  chosen rate is never caught up with; **Go to Now** is the way to the present.
- **Not persisted.** Every entry opens As Played at its base speed. It is listed in the roadmap's
  user-settings-persistence table, which exists so a setting with no home is a recorded decision.

### E — The disabled APS dropdown becomes a label (developer, 2026-10-08)

**The requirement:** in DiceGame, the node that shows the credits in use ("Auto bets per second: [99X ▾]",
`%ApsSelector`) keeps **no dropdown**. The dropdown has been disabled since betting speed was locked to hardware,
so it looks like a control and does nothing. It becomes **a label that states how many credits DiceGame is
using**. Changing credits stays in Mining Pools & Hardware (and, for DEV tests, the Part 0 ladder beside it).

**Before removing it, enumerate its readers** (Standing Convention 13). `_apsSelector` has **12 references** in
`DiceGame.cs` at specify time. At least `InitializeApsSelector`, `RefreshHardwareDrivenSpeed` and the
`ItemSelected → OnBetsPerSecondChanged` hook may read the **selected item** as the bet rate. If any path takes its
rate from the widget rather than from `HardwareAllocationRepository`, the label must not inherit that: **the rate
comes from the hardware, and the label only displays it** (Convention 6, one source). The
`DevPlayerCreditsSelector` is positioned against this node's right edge (x 1741), so check its placement after
the swap.

**Wording to settle at implementation:** "credits" versus "bets per second". At base speed they are the same
number, but at 9000X the node bets 90× faster, so "N credits" is true at any speed and "N bets/s" is not.

### D — Month names in the developer's language, and a detector that could not see them

**Found while specifying C.** Five renders use `MMM` with no culture, so on the developer's Spanish locale they
produce `may.`, `ene.`, and so on, which is exactly the §29.12.1 shape:

| file | line (at specify) | form |
|---|---|---|
| `ClientsBetsHistory.cs` | ~112 | `.ToString("dd MMM yyyy HH:mm:ss")` |
| `ClientsBetsHistory.cs` | ~180 | `.ToString("dd MMM yyyy")` |
| `ClientsBetsHistory.cs` | ~217 | `.ToString("dd MMM yyyy HH:mm")` |
| `ClientsTransactions.cs` | ~124 | `$"…{enrolledLocal:dd MMM yyyy HH:mm:ss}"` |
| `ClientsTransactions.cs` | ~152 | `.ToString("dd MMM yyyy HH:mm:ss")` |

**Why the detector missed them:** pass 4 is
`\.ToString\("(ddd|dddd|MMM|MMMM)[^"]*"\)`. It only matches a format that **starts** with a name token. Every
site above starts with `dd ` and passes. The interpolated form at ~124 is outside pass 4's shape entirely. **The
stored detector reported 0 against 5 real hits. That is a tripwire returning the "clean" answer for the wrong
reason**, the failure Standing Convention 14 warns about.

**D1 — fix the detector first, so it is shown to fire:** widen pass 4 to find a name token anywhere in the
format string, both in `.ToString("…")` and in an interpolation hole `{x:…MMM…}`. **Run it before the code fix and
record that it returns 5.** Then fix the sites (`CultureInfo.InvariantCulture`, `string.Create` for the
interpolated one) and record 0. The new baseline is `17 / 0 / 0 / 0`, unchanged in its numbers but now earned.
The edit to the stored command in CLAUDE.md and §29.12 is made **with the file tool**, and the command is then
**extracted from the document and run** (Convention 14).

---

## 3. Order

0. **Part 0** — the visual diagnostic and the journal read. It changes nothing, so it runs on today's code, and
   its result decides D-20.2.
1. **A** — independent and the developer's explicit ask.
2. **D** — small and mechanical, and it changes a stored detector, so it is better done before any other UI
   commit relies on the baseline.
3. **B** — one code path, verified by the developer opening the explorer from the Calendar.
4. **C2/C3** (C1 is answered by Part 0's journal).
5. **E** — after Part 0's round 2, so the dropdown being replaced is not mid-test.

Each part is one stage → ask → commit unit on the plan branch.

## 4. Invariants (every commit)

- **I1** `dotnet build` clean.
- **I2** Locale detector at its baseline, **re-run, not consulted**. From D onward this means the widened pass 4.
- **I3** Ch. 29 is read before any `.tscn` with a `ScrollContainer` is edited (A touches `MainMenu`).
- **I4** No headless game launch. The developer verifies in the editor and names the panel.
- **I5** No new poll-shaped `_Process`. B deliberately adds no subscription either.
- **I6** Docs move with the code. Roadmap objectives B and C are marked DONE (or refuted) in their own entries. If
  D changes the stored detector, CLAUDE.md's Money Handling block and §29.12/§29.12.1 change in the same commit.

## 5. Close-out

*(Written at close-out.)*
