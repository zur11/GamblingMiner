# Mini-Plan 20 — Pending UI objectives, an Exit button, and a locale pass that could not see month names

**Series note:** twentieth of the *mini-plan* series. **UI work, small code changes, no performance runs.** It takes
the two open UI objectives most recently written into `PRIVATE_ROADMAP.md` §5, adds the developer's request for an
**Exit button on `MainMenu`**, and adds one defect found **while specifying this plan**: five culture-sensitive
month-name renders that the locale detector's pass 4 cannot see.

**Status:** 📝 **SPECIFIED 2026-10-08**, branch `mini20-ui-pending-and-exit-button` (proposed). Nothing built.
**Next:** A (Exit button).

---

## 1. Scope

| Part | What | Source |
|---|---|---|
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

**D-20.1 — should Exit ask for confirmation? Recommended: yes, and the dialog states what will be reverted.**
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
through **three residues**. The roadmap says: *verify before building.*

**C1 — measure first, from the journal, read by Claude (step 3 of the plan lifecycle).** Over the player's
consecutive bets in the current `bet_history_*.jsonl` (read `JournalLine` in `BetHistoryRepository` for the field
map first), compute `Δt` between consecutive timestamps and the distribution of `seconds mod 60`. Mini-plan 08
changed how bets are stamped, so the deltas may not be a flat 100 s.

**Prediction P-C1:** within one autobet session, `Δt` is exactly 100 s, and `seconds mod 60` takes exactly 3
values. **Decision rule:** if P-C1 holds, go to C2. If the deltas vary, the 3-cycle diagnosis is wrong. Stop and
re-diagnose before choosing a format, and record the refutation.

**C2 — D-20.2, the format.** The seconds are true recorded values, so they are not false precision. They are
spatial aliasing down the list. **Recommended: `HH:mm` in the row, and the full `yyyy-MM-dd HH:mm:ss` in the row's
`TooltipText`.** At 100 s per bet, adjacent rows always differ by 1 or 2 minutes, which reads as time passing. No
information is lost, because the tooltip still carries the exact instant. Alternatives: a relative offset
(`+1m40s`), which is noisy at bet rate, or keeping the seconds, which is the status quo.

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

1. **A** — independent and the developer's explicit ask.
2. **D** — small and mechanical, and it changes a stored detector, so it is better done before any other UI
   commit relies on the baseline.
3. **B** — one code path, verified by the developer opening the explorer from the Calendar.
4. **C1** (measurement) → **C2/C3**. C1 needs a journal with an autobet session in it. **Audit before the developer
   restarts the app** if the world is still pre-genesis.

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
