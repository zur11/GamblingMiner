# Mini-Plan 06 — Proving INC-003's root cause, deliberately

**Series note:** sixth entry of the *mini-plan* series, following
`mini05-bet-journal-single-actor-plan.md`, which dated the contamination and named a leading root cause it
could not observe.

**Status:** 📋 **SPECIFIED, NOT STARTED.** On the shelf by mini-plan 07's **D2** (gate G5, fired as
*deferred*, not cancelled, 2026-08-22). Its world precondition is now **satisfied** — see §3.

> ⚠ **BLOCKER — read before starting, not halfway through. The sentinel is DEBUG-only.**
> `AssertSingleActorJournal` is `[Conditional("DEBUG")]` [V: `Scripts/Services/UserStatsService.cs:152`],
> and **P5 is the discriminator** (§1). A **Release** run therefore produces **four of the five predictions
> and no verdict** — precisely the ambiguity mini-plan 05 spent a week in, reproduced deliberately and at
> full cost.
>
> Mini-plan 07 **D5** decided the sentinel must exist in **Release**, using the project's two-half pattern
> (a release-safe quantity written to a trace in every build, plus a `Conditional("DEBUG")` assertion over
> the same quantity — reference `AssertBotBallotsVary` in `NetworkRoot.cs`, beside its unconditional
> `spread=` trace emission). **That decision is DECIDED BUT NOT IMPLEMENTED** — verified against `main`,
> 2026-08-23.
>
> *Cited by symbol, not by line. Mini-plan 07 D5 originally gave `NetworkRoot.cs:4172` / `:4279` /
> `:4173-4178` for these; all three had drifted +10 within a day. D5 has since been re-cited by symbol
> (2026-08-23) — **grep the name, and do not reintroduce a line number here.***
>
> **So, before step 1 of §4, do one of two things:** implement D5, or run a **DEBUG** build deliberately
> and record that you did. This is mini-plan 07's **B1.1**, and mini-plan 06 never stated it — which is
> why it is stated here, at the top, rather than left to be discovered mid-run.

**Objective.** INC-003's root fault is *supported* — by dating and by mechanism-fit — and **not observed**.
This plan observes it: deliberately re-create the retired mechanism on a disposable world, with mini-plan
05's sentinel armed, and see whether the journal contaminates in the same shape. Success upgrades INC-003's
root fault from leading hypothesis to fact; failure sends it back to open and is worth just as much.

**Second objective, carried here deliberately (§7).** Mini-plan 04's clock-pays backpressure shipped to
`main` **without ever having executed** — `ReportEmitBudgetBound` did not fire once across six runs and
7,578 records. This plan is already building a harness on a disposable world, which is the one context
where exercising it costs nothing.

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

## 3. The world: archive → wipe → reproduce on a CLEAN one

The harness **deliberately corrupts the bet journal**, so it must not touch anything wanted — and, less
obviously, it must not run on a world that is *already* contaminated.

### 3.1 — ✅ Step 1 done: the evidence is archived (2026-08-20)

```
%APPDATA%\Godot\app_userdata\GamblingMiner_INC003_evidence_2026-08-20\
```

**88 files, 55 MB, verified**: 1,053 bet records inside the `2009-05-23 T19–T21` band, 193,660 bet records
total, **`Rollup.TotalBets = 223,137` as of the archive's capture, 2026-08-20**. This is the archive
INC-003 cites as its evidence, and the one mini-plan 05 §6.1 requires before the wipe — one copy serves
both.

> **Both totals in circulation are correct; each is correct for its own date, and neither used to say so.**
> INC-003 quotes **`215,723`** — the figure as it stood when the incident was written. This section quotes
> **`223,137`** — the figure at the 2026-08-20 archive capture, after further play. **The rollup kept
> counting between the two dates**; the difference is not a discrepancy and neither figure supersedes the
> other. Quote a total with its date attached or not at all. (Mini-plan 07 **B1.4** raised this;
> this note is the fix.)

*Taken when it was, because the journal was still living in `user://` and every pre-block restart could
have rolled it away. It had already survived several by luck.*

### 3.2 — Then wipe, and reproduce on a virgin world

**This reverses the first draft of this section**, which had the harness run on the live world "since it is
disposable by prior decision". That is true and it is still the wrong order:

> **The world already contains two balance lines.** Introducing a new band on top of them makes the result
> ambiguous exactly where P1–P5 need to be unambiguous — a second line would have to be told apart from
> the ones already there, by the same reasoning that took mini-plan 05 a week. **A band that appears in a
> virgin journal admits no argument.**

So: **archive (done) → `WorldFormatVersion` bump + clean reset (mini-plan 05 §6) → run the harness.**

The wipe is not a cost here, it is the instrument: a fresh world means every record in the journal was
written during this experiment, and the two-line separation becomes trivial rather than forensic.

> ### ✅ Both preconditions are now SATISFIED — this plan performs neither (2026-08-23)
>
> - **The archive** was taken 2026-08-20 (§3.1), and a second byte-identical copy,
>   `GamblingMiner_prewipe_2026-08-23`, was taken at the wipe. Both verified against mini-plan 07 §A.0.2's
>   recorded checksums.
> - **The wipe was executed by mini-plan 07, step 5** — `a0c27ea`, `WorldFormatVersion` 5 → 6. It is
>   **not** this plan's to perform, in either direction.
>
> **The world is already virgin.** `WorldFormatVersion = 6`, the journal is empty, and the game clock reads
> the canonical player start. This plan may branch and run the harness directly; §4 step 1 is its first
> action. **The wipe was never an ordering question this plan had to settle — it was a state this plan
> needed, and the state now holds.** (§6 previously listed it a second time as an *afterward*, contradicting
> this section. That bullet is removed.)

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
- **Dispose of the world the run corrupts.** The harness writes a bad journal on purpose, so the world it
  leaves behind is spent. Archive it if the result is worth keeping, then reset it — but note this is
  *disposal of this run's output*, not the mini-plan 05 wipe, which is a precondition and is already done
  (§3.2).

> **Removed here, 2026-08-23:** a third bullet reading *"Then the wipe that mini-plan 05 §6 already
> decided: `WorldFormatVersion` bump, clean reset."* It was **obsolete and self-contradictory** — obsolete
> because mini-plan 07 executed that wipe at `a0c27ea`, and contradictory because §3.2 requires the very
> same wipe **before** the run, as the precondition that makes the result unambiguous. One wipe, needed
> beforehand, listed twice and placed on both sides of the experiment.

---

## 7. Deferred here from mini-plan 04: validate the emit budget, which has never run

`MaxAppendRowsPerFrame = 25` and the whole §6.2 *"the clock pays, not the content"* path shipped to `main`
in an **unexercised** state. Across mini-plan 05's six runs — 7,578 records, 1 and 5 credits, bots on and
off — `ReportEmitBudgetBound` never fired once, because at those rates the budget cannot bind (mini-plan 04
§11.2 measured why: 0.78 bets/frame at 10x against a budget of 25, and same-timestamp groups capped at
`SimulationService.MaxBetsPerFrame = 10`).

**That is not a defect. It is a shipped promise with no execution behind it**, and the honest place to
record it was mini-plan 05's merge review rather than a future surprise.

Validating it needs a régime the game cannot reach by playing, which is exactly what this plan already
builds: a disposable world plus a harness branch that never merges.

| | Step |
|---|---|
| 1 | On `repro/explorer-clock-rewind`, temporarily set `MaxAppendRowsPerFrame = 1` |
| 2 | Replay a dense stretch at **10x** in `BetsHistoryExplorer` |
| 3 | Confirm **all three**: `ReportEmitBudgetBound` prints · the `Speed: 10x requested / N x actual` label appears · **and the cursor falls behind wall-clock without dropping a single row** |

**Step 3's third clause is the actual test.** The label and the print are conveniences; §6.2's promise is
that *not one bet of the retained range is ever skipped*, and the way to check it is to count the emitted
rows against `UpperBound` over the same window and require equality.

Revert the constant before leaving the branch — and since the branch never merges, the revert is belt and
braces rather than the safeguard.

*If it turns out the mechanism does not work, that is a mini-plan 04 defect found before a player ever met
it — which is the whole value of noticing that a shipped path had never run.*

---

## 8. Out of scope

- Fixing anything. The mechanism has been gone since 2026-08-16; this plan only establishes that it was
  the one.
- The `DevTimeScale 9000X` stress question. Worth doing, unrelated to INC-003, and it belongs with the
  T4 simulation-scale work in `PRIVATE_ROADMAP.md` §8 rather than here.
- A direct DiceGame → BetsHistoryExplorer button (mini-plan 05 §9). Still wanted; still not while an
  investigation into navigation is open.
