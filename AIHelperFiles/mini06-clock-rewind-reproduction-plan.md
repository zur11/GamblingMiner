# Mini-Plan 06 — Proving INC-003's root cause, deliberately

**Series note:** sixth entry of the *mini-plan* series, following
`mini05-bet-journal-single-actor-plan.md`, which dated the contamination and named a leading root cause it
could not observe.

**Status:** 📋 **SPECIFIED, NOT STARTED.**

**Objective.** INC-003's root fault is *supported* — by dating and by mechanism-fit — and **not observed**.
This plan observes it: deliberately re-create the retired mechanism on a disposable world, with mini-plan
05's sentinel armed, and see whether the journal contaminates in the same shape. Success upgrades INC-003's
root fault from leading hypothesis to fact; failure sends it back to open and is worth just as much.

---

## 1. What is being reproduced

Before `95860f4` (2026-08-16) `BetsHistoryExplorer` **rewound `CalendarTimeService`** to browse history.
There is one clock, and `SimulationService` stamps every settled bet with it — so bets settled while the
player browsed with the replay running were journaled with **timestamps in the past**, and one session's
records land inside an earlier window reading as a second bettor.

The prediction is specific enough to be falsifiable. Contaminating a world this way must produce **all** of:

| # | Prediction |
|---|---|
| P1 | Two balance lines in the same timestamp window, **each internally continuous to the satoshi** |
| P2 | Each at the correct cadence for the hardware credits in force |
| P3 | The intruding line **born mid-progression**, not at base bet |
| P4 | A **bounded** band — starting when the browse starts, ending when it ends |
| P5 | `[BetJournal] UNDECLARED balance discontinuity` firing, naming `SimulationService` on both sides |

**P5 is the discriminator.** If the contamination appears and the writer on both sides is the *same*
source, that is the clock hypothesis. If the two sides carry different sources, it is not — and mini-plan
05's whole hypothesis set reopens with a new lead.

---

## 2. The git question: **neither a checkout nor a revert. Branch from `main` and re-add the mechanism.**

The instinct is to check out the pre-fix commit. **Do not**, for two reasons that compound:

1. **The diagnostics do not exist there.** D1/D3/D2 were built on 2026-08-19 (`66ff873`). At `95860f4^`
   the journal has no sentinel and no session trace, so a reproduction would have to be diagnosed the same
   slow post-hoc way the original was — losing the entire point of having built them.
2. **A `git revert 95860f4` will not apply.** Mini-plan 04 rewrote `BetsHistoryExplorer.cs` heavily
   (820 changed lines); the revert would conflict throughout, and resolving it by hand produces a file that
   is neither build.

So:

```bash
git checkout main
git pull                              # if anything landed since
git checkout -b repro/explorer-clock-rewind
```

**A branch, not a detached HEAD** — because this needs a commit (the harness below), and work committed on
a detached HEAD is orphaned the moment you leave it.

**It branches from `main`, not from `bet-journal-single-actor`** — provided mini-plan 05 is merged first.
If it is not merged yet, branch from `bet-journal-single-actor` instead, since the diagnostics are the
entire reason this branch can conclude anything. **Check with `git log --oneline -1 main` before branching.**

### 2.1 — The harness: re-add the mechanism, do not restore the old code

One DEV-only switch, default **OFF**, that makes the replay cursor also write the world clock — the single
behaviour `95860f4` removed, on top of the current build with every diagnostic intact:

- a `const bool DevRewindWorldClock = false;` in `BetsHistoryExplorer`, in the `TimelineConfig.DevAltTimeline`
  spirit — **`false` on any branch that could be merged, forever**;
- when true, the per-frame cursor advance also calls `_calendarTimeService.SetLocalDateTime(_selectedLocal)`;
- a `GD.PrintErr` on `_Ready` while it is true, so a build carrying it can never be mistaken for a normal
  one.

> **Re-creating a retired mechanism is not the same as reverting to the build that had it.** The revert
> gives you an old world you cannot instrument; the harness gives you today's world with one behaviour
> restored and every sentinel watching. **Reproduce the mechanism, not the commit.**

⚠ **This branch must never merge to `main`.** Its purpose is to write a corrupt journal on purpose.
Delete it once INC-003 is settled.

---

## 3. The world: disposable, and archived first

The harness **deliberately corrupts the bet journal**. It must not touch anything the developer wants.

1. **Archive `user://` whole**, to a dated folder outside it — the P15.8 precedent
   (`%APPDATA%\Godot\GamblingMiner_INC003_pre-repro_<date>\`). This is also the archive INC-003's evidence
   depends on and mini-plan 05 §6.1 already requires before the wipe; doing it here serves both.
2. Run the harness on the **live** `user://` afterwards, accepting that it will be contaminated — the wipe
   is already decided (mini-plan 05 §6, option b), so the world is disposable by prior decision.

*Restoring the archive afterwards is optional and only matters if the reproduction fails and the world is
wanted back.*

---

## 4. Procedure

With the harness ON, 5 hardware credits (the regime the original bands occurred in), bots off:

| | Step |
|---|---|
| 1 | Start the autobet in DiceGame. Let it run **2 minutes** untouched — this establishes the clean line |
| 2 | Navigate DiceGame → Calendar → **BetsHistoryExplorer** |
| 3 | Scrub the cursor **back several in-game hours** — the further back, the more unmistakable the band |
| 4 | Press **Play** and leave it replaying for **2–3 minutes** while the autobet keeps running |
| 5 | Return to DiceGame. Let it run **2 more minutes** |
| 6 | **Stop** |

Steps 3–4 are the mechanism: the cursor moves, and with the harness on it drags the world clock with it,
while `SimulationService` keeps stamping bets from that clock.

**Note the in-game clock at steps 1, 2, 4, 5 and 6** — as always, it is the only thing that lines the
console up against the trace and the journal.

---

## 5. Reading the result

The measurements are mini-plan 05's, unchanged — they are already written and already validated against
seven thousand clean records:

- bets/hour against `span ÷ 20`, looking for the **doubling**;
- the two-line separation over the band, checking each line's satoshi continuity;
- the birth record: is the intruder **already mid-ladder**?
- and the console line, for **P5**.

**A clean result is not a failure of the plan.** If the harness cannot produce the shape, the clock
hypothesis is wrong despite fitting, INC-003's root fault returns to **open**, and the investigation has
learned something no amount of further reasoning could have told it.

---

## 6. Afterwards

- Update **INC-003** — root fault confirmed, or returned to open with this ruled out.
- **Delete the branch.** It exists to hold a deliberate defect.
- Then the wipe that mini-plan 05 §6 already decided: `WorldFormatVersion` bump, clean reset, archive
  already taken at §3.

---

## 7. Out of scope

- Fixing anything. The mechanism has been gone since 2026-08-16; this plan only establishes that it was
  the one.
- The `DevTimeScale 9000X` stress question. Worth doing, unrelated to INC-003, and it belongs with the
  T4 simulation-scale work in `PRIVATE_ROADMAP.md` §8 rather than here.
- A direct DiceGame → BetsHistoryExplorer button (mini-plan 05 §9). Still wanted; still not while an
  investigation into navigation is open.
