# Mini-Plan 20 — Pending UI objectives, an Exit button, and a locale pass that could not see month names

**Series note:** twentieth of the *mini-plan* series. **UI work, small code changes, no performance runs.** It takes
the two open UI objectives most recently written into `PRIVATE_ROADMAP.md` §5, adds the developer's request for an
**Exit button on `MainMenu`**, and adds one defect found **while specifying this plan**: five culture-sensitive
month-name renders that the locale detector's pass 4 cannot see.

**Status:** 📝 **SPECIFIED 2026-10-08**, branch `mini20-ui-pending-and-exit-button`. Nothing built.
**D-20.1 approved** (confirmation dialog). **D-20.2 approved as HH:mm + tooltip, then reopened by Part 0's code
reading**: the column strobes along two axes, down the list (credits) and frame to frame (DevTimeScale), so a
fixed `HH:mm` is wrong above 1 credit (§2 Part 0).
**Next:** Part 0 (visual diagnostic), then A.

---

## 1. Scope

| Part | What | Source |
|---|---|---|
| **0** | A **visual diagnostic**: the clock, the attempts counter and the bet rows while DevTimeScale and hardware credits change | the developer, 2026-10-08 (approval of the specification) |
| **A** | An **Exit** button on `MainMenu` that closes the app | the developer, 2026-10-08 |
| **B** | `CalendarsNavigator` **snapshots the present on arrival**, except when arriving from `BetsHistoryExplorer` | roadmap "Calendar entry date", requirement recorded 2026-08-24 (`mini06-…-plan.md` §9.7e) |
| **C** | The bet-history row's **timestamp strobe** | roadmap "The bet-history row's timestamp strobes", developer's report 2026-10-06 |
| **D** | Five **month-name renders without `InvariantCulture`**, and the pass-4 regex that misses them | found while specifying C |

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
