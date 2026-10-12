# Mini-Plan 23 — Mini-plan 20's carried UI parts: the Exit button first

**Series note:** twenty-third of the *mini-plan* series. **UI work, small code changes, short visual checks.** It
takes the parts mini-plan 20 closed without building (its close-out §5, "Carried"). **Each part's full
specification is still mini-plan 20 §2 and is not repeated here.** This plan restates what changed since, the order,
and the predictions.

**Status:** 🔨 **IN PROGRESS** on branch `mini23-carried-ui` (specified 2026-10-11). **A, C, B, D and E done and verified**: E's measurement 2 (2026-10-12) confirmed P-E2/P-E3 with the explorer showing the 10-number row. A second view, latest number only, has been built behind a toggle. **Next:** the developer's run of P-E4, then close-out, including the Manual's "tick" note left over from C2b.

---

## 1. The parts, and what changed since mini-plan 20 specified them

Code facts re-verified on 2026-10-11, after mini-plans 21 and 22 touched nearby files:

| part | what | spec | re-verified |
|---|---|---|---|
| **A** | **Exit button on MainMenu**, with the approved confirmation (D-20.1) | mini-20 §2 A | no exit exists; `RootMargin` `margin_bottom = 30`; `BankrollStateService` and `CasinoScBalanceService` flush on `NotificationWMCloseRequest` |
| **B** | Calendar snapshot on arrival, except from the explorer | mini-20 §2 B | `CalendarsNavigator` does not read `PreviousScene` yet |
| **C** | Month names: five culture-less `MMM` renders, and locale pass 4 widened to see them first | mini-20 §2 D | the same five sites (`ClientsBetsHistory` ×3, `ClientsTransactions` ×2); `StatusBar`'s `MMM` goes through `FormatGameTime`, which passes `InvariantCulture` |
| **D** | DiceGame's disabled APS dropdown becomes a label of the credits in use | mini-20 §2 E | `_apsSelector`: 12 references in `DiceGame.cs`. The DEV credits ladder is positioned against its right edge |
| **E** | The explorer's frame cost while rows stream (59 → ~28 fps) | mini-20 F round 4 | the per-stage `[ExplorerPerf] ms/frame` line is built (mini-20 F) and has **never been run** |

**Folded in, no part of their own:**
- **Mini-20 C3** (`ClientsBetsHistory`'s per-bet seconds) is resolved **by reason, not by code.** Mini-20 C settled that a
  value repeating down a still list is not a strobe, and that only motion makes one. This DEV feed has no motion
  rule to follow. Its timestamps keep their seconds, and C fixes their month names.
- **Mini-20 C2b's remainder:** CLAUDE.md's "tick" clarification landed in mini-21. `ProjectDesignManual.md` ~l.5057
  ("1 bet = 1 nonce attempt = 100 in-game seconds") still needs the same note. It is done at close-out.

## 2. Per-part notes and predictions

**A — Exit.** As specified: a fixed footer outside `ButtonsScroll`, `margin_bottom ≥ 50` (Ch. 29 §29.10–29.11 read
before the `.tscn` edit), and quit through `PropagateNotification(WMCloseRequest)` then `Quit()`, so it is no weaker
than the window's X. The confirmation names the block the world resumes from, read from the checkpoint's own
`CalendarLocalTicks`. Before the first block, it says the world restarts at the player-start date.
- **Since mini-22, also true and worth saying in the dialog's logic:** a restart now reverts the journal at boot as
  well, so "bets since then will be undone" is literally true of every screen.
- **P-A1:** Exit closes the app, and the Output panel shows `[Exit] close request propagated`. The next launch restores
  exactly the block the dialog named.

**A — built (2026-10-11).** `MainMenu.tscn`: an `HSeparator` and a `Footer` (`HBoxContainer`) holding `%ExitBtn`
("Exit Game", the menu buttons' size and font), **siblings of `ButtonsScroll`**, and `RootMargin`'s `margin_bottom`
30 → **50** (§29.10–29.11, read before the edit). `MainMenu.cs`: a `ConfirmationDialog` built on first press, its text
rebuilt each time from `BlockSessionCheckpointService.CurrentSnapshot.CalendarLocalTicks`. On confirm it prints
`[Exit] close request propagated`, propagates `NotificationWMCloseRequest` to the whole tree, then calls `Quit()`.
Debug and ExportRelease builds clean; detector 17 / 0 / 0 / 0.

**A — verified by the developer (2026-10-11):** the footer is visible and does not scroll, Cancel does nothing, Exit
closes the app, and the next launch restores the date the dialog named.

**C — built (2026-10-11). P-C1 holds.** The widened pass 4,
`(\.ToString\("[^"]*(ddd|MMM)[^"]*"|\{[^{}"]*:[^{}"]*(ddd|MMM)[^{}"]*\})`, minus lines with `InvariantCulture`, read **5
before the fix** (exactly the five sites, no false positive; the old pass read 0) and **0 after**. Four `.ToString`
calls gained `CultureInfo.InvariantCulture`, and the interpolated one became `string.Create(CultureInfo.InvariantCulture,
$"…")`. The stored command changed in CLAUDE.md (Money Handling) and Manual §29.12.1, which also records why. Both
edits used the file tool, and **the command extracted from each document was run**: 0 on the fixed code and 5 on the
pre-fix code (stashed), so the documents store the command that works (Convention 14). Passes 1–3 are unchanged (17 / 0 /
0). CLAUDE.md grew to ~86,200 characters (target 85,000, warning 95,000), because the stored tripwire changed.

**B — Calendar.** As specified: in `_Ready`, if `SceneManager.PreviousScene != BetsHistoryExplorer`, set the selection
to `GamePresentLocalDateTime` (the line "Set Now" already runs). No subscription; the selection does not move while
the player is choosing.
- **P-B1:** entering from Main Menu or DiceGame shows the present. Returning from the explorer keeps the explorer's date.
  The explorer opened from the Calendar shows the newest bets, not the oldest.

**C — month names.** Widen pass 4 first and **record that it returns 5**, then fix the five sites and record 0. The
stored detector command changes in CLAUDE.md and §29.12, so the edit uses the file tool and is verified by extracting
the command from the document and running it (Convention 14).
- **P-C1:** widened pass 4 = 5 before, 0 after. Passes 1–3 unchanged (17 / 0 / 0).

**B — built (2026-10-11), with two findings the specification did not have.**

1. **"Set Now"'s present was stale during a run.** Mini-plan 20 specified reusing "Set Now"'s line,
   `SetExplorerSelectedLocalDateTime(GamePresentLocalDateTime)`. But `_gamePresent` is only written by explicit calls,
   and since mini-plan 21 a run moves the clock through `AdvanceGameSeconds`, which does not touch it. During a run the
   frontier lags, and "Set Now" itself landed on a stale present. The explorer had already solved the same problem
   (`PresentLocal`: the later of the live clock and the frontier). `CalendarsNavigator` now has the same `PresentLocal`,
   used by the entry snapshot, by "Set Now", and by the apply clamp. Convention 13: the retired premise was "the
   frontier is the present", and three readers held it.
2. **"Adopts the date it arrived from" had no writer.** The explorer never handed its cursor back ("navigation does not
   touch the clock"), so returning to the Calendar showed the Calendar's own earlier selection. `OnBackToCalendarPressed`
   now writes `_selectedLocal` into `ExplorerSelectedLocalDateTime` **only when the destination is the Calendar**. That
   is the shared selection, never the world clock, so the explorer's rule still holds. *Interpretation to confirm with
   the developer: "the date it arrived from" = the date the explorer was showing.*

The entry snapshot itself is as specified: in `_Ready`, if `PreviousScene != BetsHistoryExplorer`, the selection becomes
`PresentLocal()`. No subscription, so the date does not move while the player chooses (§9.7e). It is documented in the
code for whoever works the Ch. 38 backlog. Debug and ExportRelease builds clean; detector 17 / 0 / 0 / 0.

**D — APS label.** As specified: enumerate the 12 references first. If any path takes the bet rate from the widget
instead of `HardwareAllocationRepository`, it moves to the repository. The label displays the credits, and only
displays them. Wording: "credits", not "bets per second" (true at every DevTimeScale). The DEV ladder's position is
re-checked after the swap.
- **P-D1:** no code path reads the dropdown's selected item as a rate (to be confirmed by the enumeration). The label
  follows a credit change made in the shop or the DEV ladder without leaving DiceGame.

**B — verified by the developer (2026-10-11):** every prediction held, and the interpretation was confirmed: "the date
it arrived from" is the date the explorer was showing.

**D — built (2026-10-11). P-D1 holds by enumeration.** Of the 12 references: the field and its lookup; the 1X–99X fill
(`InitializeApsSelector`); `Select` + `Disabled` in `RefreshHardwareDrivenSpeed`; two that only used it as the DEV time
selector's anchor; and `ItemSelected → OnBetsPerSecondChanged`, unreachable on a disabled control. **None reads the
selected item as a rate.** The rate has come from `GetAutoBetBaseAps()` → `HardwareAllocationRepository` since Phase 3.
- `.tscn`: `ApsSelector` (`OptionButton`) → `CreditsInUseValue` (`Label`, font 22, same right edge x 1741, so the DEV
  ladder at x 1752 does not move); the caption "Auto bets per second:" → **"Hardware credits in use:"**.
- Code: `RefreshHardwareDrivenSpeed` writes the value `GetAutoBetBaseAps()` returns, the same number the rate uses
  (Convention 6), on entry, on a node switch and on every `HardwareChanged`. `InitializeApsSelector` and
  `OnBetsPerSecondChanged` are **deleted** (Convention 3). The four comments that named the "APS selector" are rewritten.
- Debug and ExportRelease builds clean; detector 17 / 0 / 0 / 0.

**E — explorer frame cost.** Measure first: one run in Per Bet at 99 cr × 1x, copying the `[ExplorerPerf] ms/frame`
line. Fix only the stage that dominates. If the frame is outside script time (`frame − engineProcess`), the next
step is reducing what is laid out per row, not code.
- **P-E1:** one stage dominates, and it is the bet list's flush or layout, not the summary walk.

**E — measurement 1 (developer's run, 2026-10-12; Per Bet 99 cr then 10 cr, 1x, then paused).** Averages per frame:

| | paused | 10 bets/s | 99 bets/s |
|---|---|---|---|
| **frame** | **17 ms** (59 fps) | **28–37 ms** (~31 fps) | **37–50 ms** (~23 fps) |
| explorer (append + summary) | 0.02 | 0.03–0.05 | 0.07–0.19 |
| bet list (`rowsFlush`) | 0 | 0.1–0.2 | 0.2–0.3 |
| **winning-numbers grid** | 0 | **2.0–2.8** | **3.1–4.4** |

- **P-E1 refuted on its candidate.** Among the scripts the **grid** dominates, not the bet list (0.2 ms) or the summary
  (0.005 ms).
- **Most of the cost is outside every timed script:** the stages sum to ~4.5 ms against 20–33 ms of extra frame. The
  cost also comes in **lumps**. At 10 bets/s only one frame in three shows a bet, yet the average frame rises 13 ms, so
  each frame that shows a bet costs ~40 ms.
- **The suspect, read from the code:** every bet shifts all 100 grid cells, so all 100 labels are rewritten,
  re-shaped and redrawn. In DiceGame that grid is hidden (the "Numbers" view deferred at mini-plan 10), which is why
  DiceGame held 60 fps at the same rate.
- **Instrument note (Convention 16):** the `engineProcess` column (`Performance.Monitor.TimeProcess`) read 28 ms while
  PAUSED against a 17 ms frame. It is not the per-frame script time it was taken for, and it is **removed** from the line.

**E — the developer's decision (2026-10-12): the explorer shows a ROW of the last 10 winning numbers, and the grid
becomes an inactive DEV node.** Built:
- `Screens/DiceGame/PreviousWinnerNumbersRow/`: an `HBoxContainer` of **10** `WinnerNumberPresenter` cells, newest on the
  left, with the grid's interface and ring-buffer design (cells never move, at most one rewrite per frame).
- `BetsHistoryExplorer.tscn`: the row under the title (now "Last 10 Winner Numbers"). The grid's `WinnersScroll` keeps
  its place with `visible = false` and `process_mode = disabled`. The field is renamed `_previousWinnerNumbersRow`,
  and its comment says how to point it back at the grid for a later experiment.
- The probe's `gridFlush` becomes **`winnersFlush`** (`AddWinnersFlush`): whichever winning-numbers view is live
  reports there. DiceGame is untouched.
- Debug and ExportRelease builds clean; detector 17 / 0 / 0 / 0.

**E — measurement 2, with the row only (registered before the run):**
- **P-E2:** at 99 bets/s the frame falls from 37–50 ms to **~17–22 ms** (near the paused frame). `winnersFlush` falls from
  3.1–4.4 ms to **< 0.5 ms** (10 cells instead of 100). The residual outside the scripts largely disappears.
- **P-E3:** at 10 bets/s the frame is back at ~17 ms (it was 28–37).
- **If P-E2 is refuted** (frame still > 30 ms), the grid was not the main cost and the next suspect is the bet list's own
  layout. That would be measured before anything else is changed.

**E — measurement 2 results (developer's run 2026-10-12, row only). P-E2 and P-E3 hold:**

| | grid (measurement 1) | **row** (measurement 2) |
|---|---|---|
| frame at 99 bets/s | 37–50 ms (~23 fps) | **16.7–18.3 ms (~59 fps)** |
| frame at 10 bets/s | 28–37 ms (~31 fps) | **16.7–17.4 ms (~59 fps)** |
| winning-numbers flush at 99/s | 3.1–4.4 ms | **0.39–0.51 ms** |
| bet list flush at 99/s | 0.2–0.3 ms | 0.16–0.21 ms |
| paused (reference) | 17 ms | 16.9 ms |

The ~20–30 ms outside every timed script disappeared with the grid, which confirms what it was: re-shaping and
redrawing 100 labels on every frame that showed a bet. At 99 bets/s the explorer now runs at the paused frame rate.
*Precision note:* "winnersFlush < 0.5 ms" held in 12 of 14 windows; two read 0.508 and 0.514. That is a marginal miss
of the stated bound, recorded as such, and it does not change the verdict.

**E — the developer's reading, and a second view (2026-10-12).** The developer asked whether the row is still
expensive. The answer was *partly*:
- **On the frame, no.** 0.4–0.5 ms is ~3% of a 16.7 ms frame that stays on the vsync floor.
- **Relative to the bet list, yes.** The row costs 2–3× the whole bet list (0.16–0.21 ms). Every bet shifts all ten
  cells, so a frame that shows a bet rewrites ten label texts plus ten stylebox colours, and each colour emits
  `changed` and a redraw.

The developer asked for a toggle between two views:
- the row as it is (**default**);
- only the latest number, like DiceGame's `ResultValue`.

**Built.**
- **`LatestWinnerNumber`** (`Screens/BetsHistoryExplorer/`): a `Label` at font 50, tinted through `Modulate` with
  ResultValue's colours. It has the row's interface, paints at most once per frame, and reports into the same
  `winnersFlush` probe.
- **Toggle:** `%WinnersViewToggle` sits in a new `WinnersHeader` beside the title, which follows the mode. The choice
  is a static, so it survives scene changes within one launch, and each launch starts on the row.
- **Both views get every roll;** only the visible one paints. The row gained the same gate: a hidden row keeps
  writing its ring but does not flush, and becoming visible paints it on that frame. Switching views therefore never
  shows stale numbers and never re-reads the journal.

**Prediction P-E4, registered before the run.**
- **Latest-only view at 99 bets/s:**
  - `winnersFlush` < 0.1 ms: one text and one `Modulate` write per frame, against twenty writes for the row.
  - Frame unchanged at ~16.7–17.5 ms, because the frame is already on the vsync floor.
  - So the saving shows in the probe, not in fps.
- **Toggling back to the row** shows the same ten numbers the latest-only view would have produced, with no gap.
- **Refuted if:** the latest-only `winnersFlush` stays ≥ 0.2 ms, or the row is wrong or blank after toggling back.

**E — measurement 3 results (developer's run, 2026-10-12; Per Bet, 99 cr, 1x, ~99 bets/s). P-E4's cost half holds:**

| windows | view | `winnersFlush` | frame | maxFrame |
|---|---|---|---|---|
| 9 | row | 0.39–0.62 ms | 16.67–17.23 ms | 16.7–25.0 ms |
| 1 | row → latest (switch window) | 0.100 ms | 17.38 ms | **50.0 ms** |
| 3 | **latest only** | **0.014–0.022 ms** | **16.666 ms** | 16.666 ms |
| 1 | latest → row (switch window) | 0.126 ms | 16.67 ms | 16.67 ms |
| 1 | row | 0.428 ms | 16.67 ms | 16.67 ms |

- **Latest only costs ~0.02 ms**, against the predicted < 0.1 ms. That is about 1/25 of the row.
- **As predicted, it does not show in the frame.** The frame stays on the vsync floor in both views. The latest-only
  windows show no dropped frames, but there are only three of them, and some row windows were flat too. So that is
  not evidence of a frame-level gain.
- **The row read 0.39–0.62 ms this run, against 0.39–0.51 in measurement 2.** The row code did not change apart from
  its visibility gate, which is inactive while the row is visible. This is the cross-session spread that Pattern 6
  rule 3 describes, not a regression.
- **One 50 ms frame in the row → latest switch window.** It is a single frame at the moment of the click, plausibly
  the panel re-laying out for the new view. It does not recur, and a once-per-click stall is not worth chasing.
- **Not determinable from the trace:** whether the row showed the correct ten numbers immediately after switching
  back. The window's 0.126 ms is consistent with the on-visible flush running, but only the screen can say whether
  it painted the right numbers. Asked of the developer, who reported that **the row repainted instantly**. **P-E4
  holds in full.**
- *Protocol note:* the run asked for a date with bets placed at 99 credits, which was **unnecessary**. Per Bet sets
  the rate from its own credits selector, whatever hardware recorded the bets. The condition was carried over from
  measurement 1's As Played protocol, where the recorded credits do set the rate. The developer asked why it was
  there.

## 3. Order

**A → C → B → D → E.** A is the one the developer called important. C changes a stored detector, so it lands before
later commits rely on the baseline. B and D are small and independent. E needs a measurement run before anything is
built. Each part is one stage → ask → commit unit.

## 4. Invariants (every commit)

- **I1** `dotnet build` clean (Debug and ExportRelease); locale detector at its baseline, re-run (and from C on, the
  widened pass 4).
- **I2** Ch. 29 read before any `.tscn` with a `ScrollContainer` is edited (A: `MainMenu`).
- **I3** No headless launch; the developer runs; Claude reads `user://` itself and names the panel.
- **I4** No new poll-shaped `_Process` (Pattern 6); B adds no subscription by design.

## 5. Close-out

*(Written at close-out.)*
