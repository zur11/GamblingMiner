# Mini-Plan 23 — Mini-plan 20's carried UI parts: the Exit button first

**Series note:** twenty-third of the *mini-plan* series. **UI work, small code changes, short visual checks.** It
takes the parts mini-plan 20 closed without building (its close-out §5, "Carried"). **Each part's full
specification is still mini-plan 20 §2 and is not repeated here.** This plan restates what changed since, the order,
and the predictions.

**Status:** 📝 **SPECIFIED 2026-10-11**, branch `mini23-carried-ui` (proposed). Nothing built. **Next:** A.

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

**E — explorer frame cost.** Measure first: one run in Per Bet at 99 cr × 1x, copying the `[ExplorerPerf] ms/frame`
line. Fix only the stage that dominates. If the frame is outside script time (`frame − engineProcess`), the next
step is reducing what is laid out per row, not code.
- **P-E1:** one stage dominates, and it is the bet list's flush or layout, not the summary walk.

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
