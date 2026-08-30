# Mini-Plan 06 — Proving INC-003's root cause, deliberately

**Series note:** sixth entry of the *mini-plan* series, following
`mini05-bet-journal-single-actor-plan.md`, which dated the contamination and named a leading root cause it
could not observe.

**Status:** ✅ **PRIMARY OBJECTIVE MET, 2026-08-26 — the mechanism is OBSERVED (§9.8), and INC-003's root
fault is updated to match.** T0 (control) and T1 (the reproduction) both ran; **T2 and T4 have not**, and
T2's instrumentation is known-invalid (§9.8c). Seven predictions observed, **P3 refuted and withdrawn from
INC-003** (§9.8a), one new symptom recorded (§9.8b).

*History: specified 2026-08-23 and shelved by mini-plan 07's D2 (gate G5, deferred not cancelled). §9
(2026-08-24) prepared the run — preflight, four runs, the capture rule that keeps the decisive evidence
alive, and a correction to P5 that changed what the run was looking for.*

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
>
> ✅ **Half-answered, 2026-08-24 (§9.7d).** D5 itself is still unimplemented, but the *trap* is closed: the
> build now announces itself at boot, so a Release run can no longer be mistaken for a silent-and-healthy
> one. **Read the first console line before T1.** §9.1 also revises what P5 means, and the revision makes
> this blocker sharper rather than milder — under the clock hypothesis silence is the EXPECTED result, and
> a Release build counterfeits it exactly.

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

> ⚠ **P5's POLARITY IS INVERTED, and §9.1 corrects it (2026-08-24).** The sentinel runs in **registration**
> order, where a rewound clock leaves the balance chain untouched — so under the clock hypothesis it should
> be **silent**, not firing. Read §9.1 before calibrating anything against the table above; §9.2 supplies
> the discriminator P5 was meant to be, and it is a stronger one.

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
- and the console line, for **P5** — **read §9.1 first: its polarity is inverted**, and §9.2's P7/P8 are
  the observations that actually discriminate.

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

---

# 9. The test set, ready to run (2026-08-24)

**Why this section exists.** §4 and §7 describe *manoeuvres*; they do not say what to capture, when the
evidence is destroyed, or which observation decides anything. Preparing the run turned up three things that
change the design — one of them fatal to P5 as written — so they are recorded here rather than discovered
mid-session. Everything below is verified against the code or measured against the 2026-08-20 archive; the
legend follows §0 of mini-plan 07 (**[V]** verified in code, **[M]** measured).

## 9.1 — P5 cannot fire under the mechanism it was written to detect

`AssertSingleActorJournal` compares `_lastRegisteredBalanceAfter + CreditedProfit` against the incoming
`BalanceAfter`, and it is called from `RegisterBet` — i.e. in **registration order**, which is the order
bets settle [V: `Scripts/Services/UserStatsService.cs`, `AssertSingleActorJournal` and its call site in
`RegisterBet`].

A rewound clock changes **timestamps**. It does not change registration order, and it does not touch the
wallet: `SimulationService` settles bets sequentially and each `BalanceAfter` chains off the last one
regardless of what the calendar reads [V: `SimulationService`, settle path — the bet's timestamp is read
from `_calendar.CurrentUtcDateTime` at settle time and is not an input to the balance]. Its pacing is
driven by **real** `delta`, never by the calendar, so rewinding the clock does not stall or reorder the
engine either [V: `SimulationService._Process`, `simDelta = Math.Max(0d, delta) * DevTimeScale`].

**So the clock hypothesis predicts a SILENT sentinel.** The two balance lines appear only once the journal
is read back in **timestamp** order. The polarity is therefore the opposite of what §1 and mini-plan 07
§B.3 assert:

| Mechanism | Sentinel during the run | Journal file in write order |
|---|---|---|
| **Clock rewind** (one writer, timestamps moved) | **silent** | balances continuous · **timestamps go backwards** |
| **A second writer** (two live chains) | **fires**, every alternation | balances break · timestamps monotonic |

This does not weaken the plan — it sharpens it. P5 was never the clock's fingerprint; it was the *rival
hypothesis's* fingerprint, and it is still worth recording for exactly that reason. It is restated as
**P5′** in §9.5.

## 9.2 — The real discriminator, and the capture rule that keeps it alive

**P7 — the journal file's own order.** During a session the journal is **appended** in write order. It is
re-sorted by `TimestampUtc` only by `RebuildJournalFromCurrentState`, whose three callers are the legacy
migration, `ClearAll`, and **`RollbackHistoryToUtc`** — the checkpoint rollback that runs at boot [V:
`Scripts/History/BetHistoryRepository.cs`]. So:

> **The first restart after the run destroys the decisive evidence.** The rollback sorts the journal by
> timestamp, and the backwards jump vanishes into a clean ascending file.

That is measured, not argued: the 2026-08-20 archive holds **193,660 bet records with exactly 0 timestamp
regressions in file order** [M], while the same file shows the two interleaved lines across the band — it
had been through many restarts, so it is a *sorted* artefact and cannot discriminate. This is the honest
reason the archive could never settle INC-003, and it is a better statement of that limit than "the
mechanism is retired".

**⇒ Capture rule, and it is the single most important line in this section:**

> **Copy the whole `user://` directory the moment the run ends, BEFORE closing the game — and never
> restart the editor before that copy exists.**
> `%APPDATA%\Godot\app_userdata\GamblingMiner` → `…\GamblingMiner_mini06_run_<date>`.

**P8 — the two lines' timestamp cadence differs, measurably.** Separating the archive's band
(`2009-05-23 19:09 → 21:59`) by balance level gives two lines of **513 records each, zero internal
continuity breaks each**, over the same ~10,240 game-second span [M]:

| Line | median gap | gap distribution | effective rate |
|---|---|---|---|
| **A** (`bal ≈ 837`) | `20.0000 s` | continuous, incl. `18.987`, `19.184`, `33.107` — **not** frame-quantized | 5 bets / 100 game-s |
| **B** (`bal ≈ 771`) | `16.6670 s` | **only** `16.666`/`16.667`/`33.333` — strictly quantized to `100/60` game-second frames | 5 bets / 100 game-s |

Both run at the rate 5 credits produces, so the doubled hourly rate is the two of them summed — P2 holds.
But they were **stamped by two different processes**: B's gaps are exact multiples of one frame at a steady
60 fps, A's are not quantized at all. Two live sessions sharing one calendar in one process would share the
same frame-time distribution. **These do not.** That is a positive observation pointing at a cursor-driven
clock, available today, and the run should reproduce it.

**P9 — the injected line's size is predicted by the browse, in advance.** Line A spans `10,239 s` of game
time and holds 513 records; at replay speed `1x` (`_cursorSpeed = 100` game-s per real second [V:
`BetsHistoryExplorer._speedSteps = { 100, 200, 400, 1000 }`]) that is **~102 real seconds of browsing**, and
at 5 credits `102 s × 5 bets/s ≈ 510` bets [M]. It matches §4's "leave it replaying 2–3 minutes" to within
the noise. So before the run: **write down the intended browse duration and speed, and predict the injected
count.** A reproduction that lands on it is worth far more than one that merely looks similar.

## 9.3 — Preflight, verified 2026-08-24

| | Item | State |
|---|---|---|
| ✅ | World is virgin | `world_format_version.txt = 6`, no `bet_history_*.jsonl`, `bet_stats_rollup.json` all-zero [M] |
| ✅ | Evidence archived | `GamblingMiner_INC003_evidence_2026-08-20`, 88 files [M, §3.1] |
| ✅ | mini-plan 05 merged into `main` | so §2 branches from `main` [V: `git branch --merged main`] |
| ✅ | `DevRewindWorldClock` absent from `main` | `grep` returns nothing — §B.2's standing check passes [V] |
| ⚠ | **Two `.prerepair` files still sit in `user://`** | `bet_stats_rollup.json.prerepair`, `block_session_checkpoint.json.prerepair`, both 2026-08-14. They survived the 5→6 wipe; the suffix sweep (`72a1366`) will destroy them at the **next** one. They are INC-003 evidence — **move them out of `user://` before the run**, per CLAUDE.md's "archive it OUT of `user://` first" |
| ⚠ | **The build must be DEBUG** | §B1.1. Running from the Godot editor gives it; an **exported build is Release** and produces no sentinel. **And this now applies to §7 as well** — `ReportEmitBudgetBound` is also `[Conditional("DEBUG")]` [V: `BetsHistoryExplorer.cs`], which mini-plan 06 §7 never stated. D5 (the release-safe sentinel) is still **decided and not implemented** [V: `AssertSingleActorJournal` remains `Conditional("DEBUG")`], so the editor run is the cheap way past the blocker |
| — | Hardware: 5 credits | The world starts every node at **1 individual credit** [M: `hardware_allocation.json`]. Use the DEV **"Buy Hardware"** button in `BTCPoolsAndHardwareShop` on the `player` node **4 times** — it is free and saves eagerly [V: `HardwareAllocationRepository.AddCredits` → `SetNode` → `Save`]. No mining or BTC needed |

## 9.4 — The four runs

Each run's duration is recorded with its outcome — §4.6's rule: a negative result only counts if the window
was long enough to have caught a positive.

| | Run | Harness | Procedure | Purpose |
|---|---|---|---|---|
| **T0** ✅ | Control — **RUN, PASSED (§9.7)** | `DevRewindWorldClock = false` | 5 credits **in the private/individual pool**, bots off. Autobet **4 minutes**, one DiceGame → Calendar → BetsHistoryExplorer → back round trip in the middle, **without** touching the replay cursor. Copy `user://`. | Proves this build, at 5 credits, is clean — so anything T1 shows belongs to the harness. Mini-plan 05 never ran a round-trip *at 5 credits*; this closes that gap at the cost of four minutes |
| **T1** | The reproduction | `true` | §4 steps 1–6 verbatim. Speed **1x**. Note the in-game clock at every step, and the wall-clock duration of the replay. Copy `user://` **before closing** | P1–P4, P5′, P6–P9 |
| **T2** | Emit budget | `true`, plus `MaxAppendRowsPerFrame = 1` | §7: replay the densest stretch T1 produced at **10x** (the 4th press of the Speed button, `1000` game-s/s) | The §6.2 promise that has never executed |
| **T4** | **DEV time scale × the explorer** | `true` | §9.6 — the four combinations nobody has ever run | The ceiling, and whether the explorer survives 9000X |
| **T3** | Disposal | — | Delete the branch **before** writing the results up (§B.2's residual risk). `grep -rn "DevRewindWorldClock"` on `main` must return nothing | Containment |

**T2 needs one line of instrumentation that does not exist.** §7 step 3's third clause — *not one bet of
the retained range is skipped* — has nothing measuring it today. `_renderedEndExclusive` **is** the count of
rows emitted, so the check is a single print at the end of the replay comparing it against
`UpperBound(_sortedRecords, demand)`; equality is the promise. Add it to the harness branch with the
constant. Without it, T2 can only confirm the print and the label, which are the conveniences, not the test.

## 9.5 — The result table, to be filled in

One verdict per prediction — `observed` / `not observed` / `not measurable` — never a single overall verdict
(mini-plan 07's Phase B closure criterion 1).

| # | Prediction | How it is checked | Verdict |
|---|---|---|---|
| P1 | Two balance lines in one timestamp window, each continuous to the satoshi | separate by balance level over the band; chain each | |
| P2 | Each at the cadence 5 credits produces | ~20 game-s mean spacing on each line | |
| P3 | The intruding line born mid-progression | first record of the injected line is not the base bet | |
| P4 | The band is bounded — it starts and ends with the browse | first/last injected record vs. the noted clock at steps 3 and 5 | |
| **P5′** | The sentinel is **SILENT** (§9.1) | no `[BetJournal] UNDECLARED balance discontinuity` in the console. **If it fires, the clock is not the whole mechanism** and that is the more valuable result | |
| P6 | `Rollup.TotalBets` delta = counted records (B1.2) | copy `bet_stats_rollup.json` before and after | |
| P7 | **Timestamp regression in the journal's WRITE order** (§9.2) | scan the pre-restart copy in file order for `ts[i] < ts[i-1]`; expect **one large backwards jump** at the scrub | |
| P8 | The injected line's gaps are **not** frame-quantized; the incumbent's are | gap histogram per line (§9.2) | |
| P9 | Injected count ≈ browse seconds × 5 | predicted before the run, compared after | |
| B1.3 | `MaxBetAmount` reproduces the legible symptom | field in the rollup copy | |
| §7 | Budget binds · label appears · **zero rows skipped** | the three clauses, separately | |

**And the negative case stands unchanged** (§5): if the harness cannot produce the shape, the clock
hypothesis is wrong despite fitting, and INC-003's root fault returns to **open**. §9.1 raises the odds that
some of P1–P9 come back mixed rather than uniform — that is a result too, and it must be written as one.

## 9.6 — The DEV time scale, the 9000X ceiling, and T4 (developer, 2026-08-24)

**The question, as raised:** the DEV time-scale selector affects *every* scene, `BetsHistoryExplorer`
included, and has never been tested above the base there. Could the explorer's own 10x replay ladder
multiply the DEV ceiling to 90000X?

**Measured answer: not through the explorer — through `CalendarsNavigator`, and the arithmetic ceiling is
exactly the 90000X predicted.** The clock's rate is a product of two independently-set factors [V:
`CalendarTimeService._Process`]:

```
rate = SpeedMultiplier × max(1, DevTimeScale)
```

`DevTimeScale` tops out at `90` [V: `DevTimeScaleSelector.Multipliers`], but `SpeedMultiplier` is offered up
to **1000** by the Calendar Navigator's own x1/x2/x4/x10 ladder [V: `CalendarsNavigator.InitializeTimeSpeedSelector`,
`AddSpeedOption("x10", 1000.0)`]. `1000 × 90 = 90,000` game-seconds per real second. **Nothing computed that
product and nothing forbade it.**

**The explorer is NOT the path.** Its replay cursor advances on `delta * _cursorSpeed`, capped at `1000`
game-s/real-s and **never consulting `DevTimeScale`** [V: `BetsHistoryExplorer.ComputeCursorDemand`, `_speedSteps`].
Its "10x" is 10× the 100X base, not 10× the DEV scale. When it live-follows it reads the world clock, which
is where the ceiling now applies.

**Why 90000X had not happened anyway — three facts in three files, none of them written down as a rule:**

1. `CalendarsNavigator` refuses anything above x1 while `IsAutobetActive` [V: `OnTimeSpeedSelected`] — a
   flag whose *name* is about betting, doing duty as a clock guard.
2. Every site that sets `IsRunning = true` also sets `SpeedMultiplier = 100` in the same block [V:
   `SimulationService.Start`, `DiceGame` ×2], and each is followed by `_autobetDelegated = true`, so
   `_ExitTree`'s `IsAutobetActive = false` cannot run while the clock is live.
3. The explorer never writes the world clock — **which is the leg this plan's harness deliberately breaks.**

> **A safeguard held up by three coincidences is not a safeguard; it is a run of luck with good
> documentation.** Leg 1 keys on the wrong flag, leg 2 is an accident of statement order, and this very plan
> removes leg 3 on purpose.

**✅ FIXED, and the fix is on `main`, not on the harness branch** — it is a real defence, not test
scaffolding. `CalendarTimeService.MaxGameSecondsPerRealSecond = 9000.0` clamps the product at the one line
that spends it, so the limit binds every writer including ones not yet written; a `Conditional("DEBUG")`
one-shot `GD.PrintErr` names **both factors** when it clamps, because a clamp that silently rescues its
caller hides the caller. `DevTimeScaleSelector` gains a DEBUG assert that its top step still equals the
ceiling — the two live in different files and the failure mode is *a control offering a speed the clock
refuses*, which is worse than a missing control.

**A lateral finding, deliberately NOT fixed here:** by leg 2, the Navigator's **x2 / x4 / x10 options can
only be selected while the clock is frozen**, and every path that starts the clock resets `SpeedMultiplier`
to 100 — so those three options do nothing whenever they would matter. That is a UI defect, it is not this
plan's, and it should not be repaired inside an investigation into clock writers. Filed here so it is not
lost.

### T4 — the four combinations, none of which has ever been run

Bots off, 5 credits, DEBUG build. **Each step is watched for the `[Clock]` clamp line as well as its own
result** — the clamp firing anywhere in T4 means a path reached the ceiling, which is information whichever
way it goes.

| | Step | Expected |
|---|---|---|
| 1 | DiceGame, autobet running, DEV scale → **9000X**. Two minutes | Sim% readout drops but the clock stays coherent; no clamp line (`100 × 90` **is** the ceiling, not above it) |
| 2 | With that still running, navigate to **BetsHistoryExplorer** and let it **live-follow** for two minutes | The explorer keeps up at 9000X, or it does not — **this is the untested case, and it is the one the question was really about.** Watch for `ReportEmitBudgetBound` (at 9000X and 5 credits the budget *can* bind: ~450 bets/s ≈ 7.5 per frame against 25, so it should not — measure, do not assume) |
| 3 | Scrub back and **replay at 10x** while the DEV scale stays at 9000X | Cursor rate stays 1000 game-s/s — replay speed and DEV scale must NOT compound. **With the harness ON this is also the worst case for the world clock**, so confirm it never exceeds the ceiling |
| 4 | Return to Calendar Navigator with the autobet **stopped**, select **x10**, then try to resume | The clamp line fires *or* the guard refuses — record which. This is the 90000X path; after the fix the clock must never exceed 9000X regardless |

**The harness must respect the ceiling too.** When `DevRewindWorldClock` writes the world clock from the
cursor it bypasses `SpeedMultiplier` entirely, so it also bypasses the `_Process` clamp — write it to
advance the clock by at most `MaxGameSecondsPerRealSecond × delta` per frame, and say so in the harness
commit. Today's cursor maximum (1000) is already an order of magnitude under the ceiling, so this costs
nothing and closes the hole rather than relying on the ladder staying short.

---

## 9.7 — ✅ T0 RESULT: PASS, and it found two things anyway (2026-08-24)

**Run as specified**, 5 credits **all in the private/individual pool** (the developer's call, and the right
one — it keeps the casino pool out of a control run; §9.3 should have said so and now does). One DiceGame →
Calendar → BetsHistoryExplorer → back round trip, no Play, no Speed. World captured before closing, to
`…\Proof of Fun\Tests\Mini-Plan 05\Backup\GamblingMiner_T0_2026-08-24\`.

| Check | Result |
|---|---|
| Bet records | **1,375** against **1,380** predicted from `span ÷ 20` [M] |
| Game span | 7.67 game-hours, `09:57:29Z → 17:37:31Z` [M] |
| Timestamp regressions in write order | **0** [M] |
| Undeclared balance discontinuities | **0** — console confirmed clean by the developer |
| Same-timestamp groups | **none**; all 1,375 timestamps unique [M] |

**The one balance break is DECLARED**, `+0.00412164` at record #613, which is exactly where the scene round
trip happened: `ReseedWalletFromBankrollSource` calls `NoteBalanceDiscontinuity("wallet_reseed")` [V:
`DiceGame.cs`, in `ReseedWalletFromBankrollSource`]. An offline scan of the journal cannot see declarations
and will always flag it — **the sentinel is the instrument for this question, a file scan is not.** Worth
stating because the same false positive will appear in every T1–T4 scan.

**⇒ This build, at 5 credits, writes a clean single-actor journal across a scene round trip.** Mini-plan 05
had only established that at 1 credit (§4.8's runs A/B). Anything T1 produces now belongs to the harness.

### 9.7a — Finding 1: 5 credits does NOT bet in bursts

Recorded because the developer expected bursts of 5 and the run refutes it: bets land **one every 20 game-
seconds, evenly spaced** — `04:57:29 · 04:57:50 · 04:58:10 · 04:58:30 …` [M] — i.e. `100 game-s ÷ 5
credits`, confirming §1.5's model at a third point. **No same-timestamp group appeared anywhere in 1,375
records.** This matters beyond trivia: P8's cadence separation and §7's emit-budget test both assume the
grid, and `MaxAppendRowsPerFrame`'s calibration note reasons from same-timestamp groups capped at 10 — on
this world they are capped at **1**.

### 9.7b — Finding 2: the explorer's "up to selected date" count is inflated by a sampling skew

**The defect is on `main`, it is a DISPLAY defect, and no stored data is affected.** The screenshot at
step 3 read `All bets — up to selected date: 7` above a panel showing **one** row, with the cursor parked on
the first record's timestamp. **The row is right and the summary is wrong.**

```csharp
Bets      = Math.Max(0, rollup.TotalBets - heldAll.Bets)   // labelled the PRUNED prefix
shownBets = prefix.Bets + _summaryTotalBets                 // 6 + 1 = 7
```

That subtraction exists to recover bets **deleted by retention**. This world has pruned nothing — one
journal file, 1,375 records, against a cap of 20 segments of 10,000 — so the prefix must be **0**. It was 6,
because the two operands are read at different instants: the in-memory rollup had counted six bets the
explorer's loaded record list had not yet picked up. At 5 bets/second that is ~1.2 seconds of skew, and it
**drifts continuously while an autobet runs**.

Corroborated on the same screenshot rather than assumed: `Max bet: 0.00100000` is the first record's bet, so
the prefix contributed **no** maximum — which is what the same code requires when `rollup.MaxBetAmount ≤
heldAll.MaxBetAmount`. The count inflated and the maxima did not. That asymmetry is this mechanism's
signature and not what real pruned data would produce.

> **The family is INC-002's and INC-003's, not the clock's.** A displayed figure derived from a subtraction
> whose two operands are sampled at different moments, wearing a label — *pruned* — that asserts a cause the
> arithmetic does not establish. CLAUDE.md's *"a label is a claim about semantics, and it gets audited far
> less often than the arithmetic under it"*, found by a control run that was looking for something else.

**Not fixed here, and deliberately so:** it is unrelated to INC-003, and repairing the explorer in the
middle of an investigation that uses the explorer as its instrument is the same mistake §9 already refuses
elsewhere. It needs its own change. The honest fix is not a bigger `Math.Max(0, …)` — it is that a *pruned*
count must come from what retention actually deleted, not from a difference that also moves for other
reasons.

### 9.7c — Finding 3: the rollup FILE lags the journal, and looks exactly like INC-004 while doing it

`bet_stats_rollup.json` read `TotalBets: 612` against 1,375 journal records. **Not damage — an exact
snapshot**: wins 320, wagered 4.30623783, net 0.78581756, maxBet 0.78310979, all matching the first 612
records to the satoshi [M]. The journal auto-flushes on its own mutation/time thresholds [V:
`BetHistoryRepository.MarkDirtyAndSaveIfNeeded`] while `SaveRollupIfDirty` runs only from `FlushHistory()`,
called from **two** places, both in `DiceGame` [V]. So between blocks the file trails the journal by
however long since the last one.

Harmless in both regimes — pre-genesis a restart resets everything, post-genesis the checkpoint is the
authority — **but its on-disk state is `IsComplete: true` + `SeededAtUtc: null`, which is byte-for-byte the
signature INC-004's damage produces** [V: the legitimate writer is the pre-genesis reset in
`UserStatsService`, beside `Rollup.Reset()`]. A healthy lagging file and a destroyed one are
**indistinguishable by inspection**. That is worth carrying into any future rollup investigation, and it is
the sort of thing only a capture of a known-good world could have shown.

### 9.7d — ✅ The DEBUG canary is built (developer, 2026-08-24)

`UserStatsService` now prints one unconditional line at boot naming the build and what it implies for the
sentinel [V: `AnnounceSentinelArming`]. It is deliberately **not** `Conditional` — its whole job in a
Release build is to say so. This closes B1.1's trap: P5′ expects **silence**, and a Release build is
silent for the wrong reason. Before T1, read the first console line.

### 9.7e — Finding 4: why the explorer opened on the OLDEST bet, and what it should do instead

**The observation.** At step 3 the panel showed one row and the developer expected "fewer than 100, but all
the rest". The cap was never the constraint: the panel renders *the last ≤100 bets **up to the cursor***,
and the cursor was parked on the **first bet of the world**, so one row is the correct answer to the
question the panel was actually asked.

The cursor opens at `CalendarTimeService.ExplorerSelectedLocalDateTime` [V: `BetsHistoryExplorer._Ready`],
and **that seed is only ever moved by someone deliberately moving it** — a calendar date applied, "Set Now",
a checkpoint restore, `DiceGame`'s two checkpoint paths [V: every caller of
`SetExplorerSelectedLocalDateTime`]. **Betting never advances it.** So:

1. Boot leaves the seed at the player-start instant, with the clock frozen (it runs only while betting) —
   the same instant the 100 SC dose is booked.
2. The autobet runs. Six hundred bets accumulate. The seed does not move.
3. Passing through `CalendarsNavigator` raises it to the replayable floor, because it sits below it [V:
   `CalendarsNavigator`, the `GetCurrentLocalDateTime() < floor` branch] — the floor being the oldest stored
   bet, i.e. the first one.
4. The explorer opens there.

**Two defects fall out of this, and neither is a data defect.**

- **The explanation exists and cannot be seen.** The explorer has a dedicated suffix for exactly this case —
  `⟵ snapped to the oldest stored bet` versus the neutral `History stored from:` [V: `BuildWindowSuffix`].
  The screenshot shows the neutral branch, because **`CalendarsNavigator` had already done the snapping, one
  scene earlier and silently**; by the time the explorer ran its own clamp the selection was already legal,
  so it had nothing to report. **Two components perform the same clamp and only the one that did not do it
  has the words for it.** The header did say `Selected timeline` and `History stored from` with the *same*
  value twice, which means "you are standing on the oldest bet you have" — true, and not legible as that.
- **The default position is the least useful one.** On any world where the player has never picked a date,
  the seed is below the floor, so the explorer always opens on the **oldest** bet in the journal. To a
  first-time visitor that reads as *"there is no history here"*, which is the opposite of true.

#### The developer's intent, recorded for the implementation plan that takes this on (2026-08-24)

**Stated as the requirement, not as one option among several. NOT IMPLEMENTED — nothing below describes
today's behaviour.**

> **Arriving at `CalendarsNavigator` from any scene that is NOT `BetsHistoryExplorer`:** the date/time
> **updates to the moment the Calendar scene was entered** — a snapshot taken on entry. It then **does not
> auto-update**, even while an autobet session keeps running in the background.
>
> **Arriving from `BetsHistoryExplorer`:** it simply **adopts the date it arrived from** — no snapshot, no
> jump.

Three notes for whoever builds it:

- **The primitive already exists.** `SceneManager.PreviousScene` is the one-deep origin already used for
  origin-aware back navigation [V: `SceneManager`]; this is the same question asked at entry rather than at
  exit, so it needs no new state.
- **"Does not auto-update" is a deliberate refusal of the event-driven default**, not an oversight to be
  tidied later. Important Patterns §6 would push a live subscription here; a date the player is in the
  middle of *choosing* must not move underneath them. Whoever revisits Ch. 38's poll/event backlog should
  read this rule before "fixing" the calendar.
- **It also settles the two defects above**, and settles them at the cause: entry-from-elsewhere becomes an
  implicit "Set Now", so the seed is never below the floor, so the silent snap in `CalendarsNavigator` stops
  firing at all and the explorer opens somewhere worth looking. **Do not fix the snap notice first** — that
  would preserve the wrong default and merely announce it.

---

## 9.8 — ✅ T1 RESULT: the mechanism is OBSERVED (2026-08-26)

**Run on `repro/explorer-clock-rewind`, harness armed, DEBUG build, 5 credits in the private pool, bots
off, replay speed 1x, on a virgin post-restart world.** Both boot banners confirmed in the **Godot editor's
Output panel**. World captured before closing to
`…\Proof of Fun\Tests\Mini-Plan 05\Backup\GamblingMiner_T1_2026-08-26\` — 2,582 bet records,
`09:57:30Z → 18:11:08Z`.

**The run deviated from §4 in one way, and it made the result stronger.** The rewind landed on the
journal's floor rather than two hours back, so the band covers 6.22 game-hours — nearly the whole world —
instead of the ~2 planned.

### The verdicts, one per prediction

| # | Prediction | Verdict | Measurement |
|---|---|---|---|
| **P7** | timestamp regression in WRITE order | **OBSERVED** | exactly **one**, at write index #1102, back **6.22 game-hours** |
| **P1** | two lines, each continuous to the satoshi | **OBSERVED** | in-band: 1,100 incumbent vs **1,121 injected**; injected has **0** internal breaks |
| **P2** | cadence correct for 5 credits | **OBSERVED** | median gap **20.000 s** on both lines |
| **P4** | the band is bounded | **OBSERVED** | `09:57:48Z → 16:11:10Z`, ending exactly where the first pass ended |
| **P5′** | the sentinel is **SILENT** | **OBSERVED** | nothing in Output; and the seam is arithmetically continuous (below) |
| **P8** | injected line less frame-quantized | **OBSERVED** | incumbent **92.4%** frame-quantized, injected **50.4%** |
| **P9** | injected count predicts the browse | **OBSERVED** | 1,121 ÷ 5 = **224 s**; band-span ÷ 100 = **224 s** — two independent derivations, identical |
| **P10** | *(not predicted — see §9.8b)* | **OBSERVED** | the explorer's per-row append path collapses into a 1 Hz wholesale repaint |
| **P3** | intruder born mid-progression | **NOT OBSERVED** | born at **base bet, ladder level 0** — see §9.8a |
| P6 / B1.3 | rollup delta | **not measurable as specified** | rollup 903 vs 2,582 journal — the §9.7c stale-file lag, not a delta |
| §7 | emit budget | **not exercised** | the budget cannot bind at 1x; and the counter is invalid — §9.8c |

### §9.1 confirmed by direct arithmetic, which is the point of the whole run

At the seam between the last pre-rewind record and the first post-rewind one:

```
prev bal 101.49116647  +  net 0.00098040  =  101.49214687
this bal 101.49214687        →  CONTINUOUS across a 6.22-hour timestamp jump
```

**The timestamp moved six hours and the balance chain did not notice.** The sentinel runs in registration
order, so it is *structurally* blind to this mechanism — its silence is a result, not a gap. P5 as
originally written expected the opposite and would have been read as a refutation of the very hypothesis
the run confirmed.

### 9.8a — P3 failed, and that is the most valuable single result

Mini-plan 05 §1.3 found the intruding line born at `0.001 × 2.3⁷` and concluded the trigger *"is not 'a
session started', it is 'a session started WRITING'"* — a session already running that only then began
being registered. **T1's injected line was born at base bet, level 0.** The rewind lands wherever the
ladder happens to be; the level at birth is a coincidence and carries no information about how the line
came to exist.

> **An inference that survives because nothing depends on it is still an inference nobody checked.** P3 was
> carried through mini-plan 05, into INC-003's fault 1, and into this plan's own prediction table, and one
> reproduction retired it. It is withdrawn in INC-003.

### 9.8b — P10, the symptom nobody predicted: the history panel paints in blocks

The developer noticed that during the replay, rows appeared **five at a time** instead of arriving one by
one as they do in DiceGame — and correctly remembered that mini-plan 04's playtest-1 fix had made arrival
per-frame. **That fix is intact. Its precondition is what the defect destroys.**

The fast append path refuses out-of-order input by design [V: `TryAppendNewRecords`, the
`record.TimestampUtc < last` guard — *"not append-ordered after all — fall back rather than render a wrong
order"*]. Under the rewind every newly settled bet carries a timestamp hours **earlier** than the last one
held, so the guard fails on every frame a bet arrives and `SyncRecordsFromHistory` takes the fallback: a
full `OrderBy` over the journal **plus** `ApplyChanceFilter`, which invalidates every cached index. The
per-row path is dead; the only thing still painting is the wholesale repaint, and that sits behind the 1 Hz
floor [V: `ViewRefreshIntervalSeconds` guard]. One repaint per real second × 5 bets/second = **blocks of
five**.

Two reasons to keep this:

- **It is a live symptom requiring no forensics.** Not a balance chain, not a file scan — the panel visibly
  changes how it paints. A player on the pre-`95860f4` build could have seen it and had no way to know what
  it meant.
- **It has a cost, and the cost scales with the journal.** An O(n log n) re-sort plus a full filter rebuild
  **per frame**. Invisible over 2,582 records; INC-003's world held **193,660** and was running this defect
  for three in-game days. That is a candidate explanation for slowdowns in that era, testable against the
  archive, and **not** something this plan measured — stated as a hypothesis, not a finding.

### 9.8c — Two things this run did NOT establish, and one retraction

- **§7's emit-budget check did not run, and its instrumentation is invalid.** The harness printed
  `Replay ended: -2 rows emitted, 1254 records crossed. MISMATCH`. **Negative rows are impossible; §6.2 is
  not broken, the counter is.** `HarnessReportRowsEmitted` differences `_renderedEndExclusive` across a
  window in which `JumpCursorTo` resets it to `-1` and every fallback rebuild resets it again — so it
  measures nothing. **Do not record that line as a result.** T2 needs a counter incremented inside the emit
  loop itself, immune to rebuilds, before it can test anything.
- **P6 could not be measured as B1.2 specified.** The rollup's on-disk copy lags the journal (§9.7c), so
  "the rollup's delta across the run" is a delta of the lag, not of the count. Measuring it needs the
  in-memory figure at two instants, which nothing exposes.
- **A correction to §9.7's T0 write-up.** It cites *"console confirmed clean by the developer"* as evidence
  of zero undeclared discontinuities. At the time the sentinel emitted **only** via `GD.PrintErr`, which
  lands in the Godot editor's **Debugger → Errors** tab, while the developer reads **Output** — so that leg
  confirmed a channel that could not have spoken. **T0's verdict stands** on the offline journal scan, where
  the single break sits exactly at the scene change and matches `wallet_reseed`. But the console leg was
  not evidence, and it read exactly like evidence. The rule that came out of it is now in CLAUDE.md.

---

## 9.9 — Priority change: the non-fluid replay, and what is DEFERRED for it (developer, 2026-08-26)

**The developer has re-prioritised.** The open work is now **the non-fluid replay as hardware credits
increase** (§9.10). Everything below is **deferred, optional, and explicitly not cancelled** — recorded
here so a later reader does not mistake an unfinished item for an overlooked one (§39.16 rule 10: a phase
suspended with its missing precondition named is a close, not a gap).

| Deferred | State when set down | What it still needs |
|---|---|---|
| **T2 — the emit budget** (§7, §9.8c) | never run; counter redesigned but unexercised | the `DevEmitBudgetProbe` flip, a 10x replay over a dense stretch, and the three clauses of §7 step 3 |
| **T4 leftovers** (§9.6) | steps 1, 2 and 4 done; step 3 partly | two observations only: whether **Speed is disabled during live-follow**, and the **`Sim:` %** at 9000X |
| ~~The DEV-scale fluidity sweep~~ | **NOT deferred — it IS §9.10b.** The credits framing was a same-day mis-statement, retracted; DevTimeScale was the variable all along | — |
| **T3 — delete the branch** | pending | blocked until the above are done or abandoned |

**Commits on this branch that belong on `main` and must be cherry-picked before it is deleted** — none has
anything to do with the harness: the **balance-trace real-time throttle** (`[CasinoSC]` at 9000X drowned
every other diagnostic), the **explorer perf probe** of §9.10 if it earns its place, and whatever §9.10's
fix turns out to be.

*(The two speed-ceiling gates were reverted at the developer's instruction and are NOT in that list: the
replay cursor never multiplies `DevTimeScale`, the cursor never reaches the present, and
`CalendarTimeService.MaxGameSecondsPerRealSecond` still bounds the world clock regardless of what either
control offers. The gates were belt over braces and cost usable speed.)*

## 9.10 — ✅ CLOSED: the replay stops flowing once `DevTimeScale` has been raised — the DATA is clumped, not the panel

> **Corrected 2026-08-26, same day.** This section first named **hardware credits** as the variable, on a
> mis-statement the developer retracted within the hour. **The variable is `DevTimeScale`** — raising it
> from 100X to 9000X is what costs the fluidity. The correction is left visible rather than overwritten
> because the credits framing produced a real clue that survives it (the clump-size arithmetic below), and
> because a test aimed at the wrong variable is the most expensive kind of mistake to make silently.

**The symptom, in the developer's terms.** The replay does not flow. Rows arrive **in one clump per real
second** instead of streaming, after `DevTimeScale` has been raised from 100X to 9000X.

**The observation that constrains it hardest, and it was made before the variable was identified:** at DEV
9000X the panel clumped, and **lowering the scale back to 100X in the same session did not fix it**, while a
**fresh** 100X run was smooth. A symptom that survives the removal of its supposed cause is not a rate
problem. Two candidates remain and the probe separates them: the **journal size**, which grows and never
shrinks within a session, or the **bet rate**, which credits and the DEV scale both raise.

**A clue worth stating rather than leaving implicit: the clump was five rows, at five credits.** At 1x the
cursor covers 100 game-seconds per real second and bets sit `100 / credits` game-seconds apart, so a
clump-per-second of exactly `credits` rows is what a **1 Hz wholesale repaint** looks like when the
per-frame append path is not running. That is a hypothesis with a number attached, and the probe tests it.

**An arithmetic fact the test must not lose sight of: `DevTimeScale` does not change the replay's rendering
rate at all.** Bets sit `100 / credits` game-seconds apart at *every* DEV scale — the scale multiplies the
clock and the bet engine by the same factor, which is the invariance the design exists to preserve — and a
1x cursor covers 100 game-seconds per real second regardless. So at 5 credits a 1x replay crosses **5
records per second at 100X and at 9000X alike**. What the scale changes is the **live arrival rate into the
journal behind the cursor**: 5 records/second at 100X, **450 at 9000X**.

> **So the cost is not in rendering the replay. It is in whatever the scene does about records arriving
> while it replays.** That is the sharpest thing known about this defect and it should drive the reading as
> much as the test.

**Five candidates eliminated by reading, so the test need not re-cover them** — recorded because a failed
search is evidence too, and the next person should not repeat it:

| Candidate | Why it is out |
|---|---|
| the clock harness | off — no `[HARNESS]` line in the T4 log |
| `BetHistoryRepository.Records` | `=> _records`, a free property; the per-frame count check is O(1) |
| the append path | `TryAppendNewRecords` is O(new records), ~7.5/frame even at 9000X |
| `ApplyChanceFilter` under "All Bets" | assigns `_sortedRecords = _allRecords` by REFERENCE, not a copy |
| the journal writer | `Flush` is `FileMode.Append` over `_pendingJournalEntries` only — O(pending), never O(journal) |

**Reading did not find it.** That is the honest state, and it is precisely why the probe exists rather than
another round of hypotheses.

### 9.10a — The instrument

`[ExplorerPerf]`, DEBUG-only, one line per real second, printing **only while the cursor is running** — no
flag to arm and none to forget:

```
[ExplorerPerf] records=N sorted=N fallbacks/s=F appends/s=A rebuilds/s=R emitted/s=E
               fps=X speed=Qx requested / Ax actual behind=B game-s
```

| Field | What it decides |
|---|---|
| **`rebuilds/s`** | the clumping itself. `~1.0` is the 1 Hz wholesale repaint — **the hypothesis above, confirmed**. `0` means the panel is appending and the clumping is somewhere else entirely |
| **`fallbacks/s`** | `SyncRecordsFromHistory` taking the full re-sort + `ApplyChanceFilter`, which invalidates the frontier and *causes* a rebuild. Non-zero names the cause; `records` prices it |
| `appends/s` | the fast path working — mutually exclusive with a fallback on the same sync |
| `emitted/s` | rows actually rendered. Should track `credits`; if it does while rebuilds also run, the rows are all arriving in one frame |
| `speed` Q vs A | whether the speed control does what it advertises — a second, independent suspicion |

### 9.10b — The test: `DevTimeScale`, and whether the damage is CONCURRENT or PERSISTENT

**Credits stay at 5 throughout. `DevTimeScale` is the only thing that moves**, and the test is built around
the one observation reasoning could not explain: *lowering it back does not help, but a restart does.*

| | Step | DEV scale while replaying | Record |
|---|---|---|---|
| 1 | Fresh run (a restart wipes the journal). Autobet 45 s → explorer → Step back → **Play at 1x**, 15 s | **100X** | the line, and by eye: flowing or clumping? |
| 2 | Back to Dice, raise to **9000X**, autobet 60 s → explorer → Play at 1x, 15 s | **9000X** | same |
| 3 | Back to Dice, lower to **100X**, autobet 10 s → explorer → Play at 1x, 15 s | **100X** | same — **this is the one that puzzled us** |
| 4 | **Restart the app.** DEV **100X**, autobet 45 s → explorer → Play at 1x, 15 s | **100X** | same — **the control** |

**Steps 3 and 4 are the entire experiment; 1 and 2 exist to establish the baseline and the damage.** Step 3
has a big journal and a low scale. Step 4 has a small journal and a low scale. **They differ in exactly one
thing, and it is the thing the developer could not remove by lowering the dial.**

| Result | Reading |
|---|---|
| **3 clumps, 4 is smooth** | the damage is **PERSISTENT and tied to journal size**. Expect `records` to be large in 3 and small in 4; whichever of `fallbacks/s` or `rebuilds/s` differs between them names the path |
| **3 is smooth** | the damage is **CONCURRENT** — it needs 450 records/second arriving *now*, not a big journal. Then the cost is in the sync path under load, and step 2's line prices it |
| **both clump** | something a restart also fixes but the scale does not — session-lifetime state outside the journal. Widen to the autoloads |
| `rebuilds/s ~ 1.0` wherever it clumps | the 1 Hz wholesale repaint is the clumping, as predicted. `fallbacks/s` then says whether a sync fallback is forcing it |
| `rebuilds/s = 0` everywhere | the hypothesis is wrong. The cost is per-row rendering, and `emitted/s` against `fps` locates it |

**The clump-size arithmetic is the cross-check, and it survives the corrected variable.** At 5 credits a 1x
replay must deliver **5 rows per second at every DEV scale**. If `emitted/s ≈ 5` in *both* the smooth and
the clumping steps, then the same rows are being delivered either spread across frames or bunched into one
— which is a rendering-cadence fault, not a throughput one, and `rebuilds/s` distinguishes them outright.

**One `[ExplorerPerf]` line from the Godot editor's Output panel per step, plus one word on whether it
looked fluid.** Four observations, under ten minutes.

### 9.10c — ✅ RESOLVED: it was never a rendering defect. The DATA is clumped.

**Measured on the developer's live journal, 2026-08-26** — `bet_history_000023.jsonl`, 7,926 bets sharing
only **949 distinct timestamps** [M]:

```
same-timestamp group sizes:  7 → 244   8 → 361   9 → 83   10 → 255
median gap between distinct timestamps: 150.00 s
```

**Every bet arrives in a clump of 7–10 sharing one instant, and the clumps sit 150 game-seconds apart.**
That is arithmetic, not coincidence:

- `SimulationService.MaxBetsPerFrame = 10` bets settle per frame;
- `CalendarTimeService` advances the clock **once per frame**, so every bet in a frame reads the **same**
  timestamp;
- at 9000X and 60 fps the clock covers `9000 / 60 =` **150 game-seconds per frame**.

A journal written at 9000X therefore *physically contains* "ten bets, then a 150-second void, ten bets". The
explorer replays it faithfully. **The panel was right and the recording was wrong.**

**The probe is what settled it, and it did so by refuting the hypothesis it was built to test.** Predicted:
`rebuilds/s ≈ 1.0` from a 1 Hz wholesale repaint. Measured: `rebuilds/s=0.0`, `fallbacks/s=0.0`, `fps=60.0`,
and `emitted/s` alternating **`0.0 / 9.8`** — one clump crossed every ~1.5 real seconds, average throughput
exactly right. No rebuild, no fallback, no dropped frame.

| Observation that had puzzled us | Explanation |
|---|---|
| lowering `DevTimeScale` back to 100X did not help | the **existing records are already clumped**; the dial does not rewrite history |
| a fresh 100X run was fluid | at 100X the clock moves 1.67 game-s/frame, so bets land one per timestamp — T0 measured exactly this: **zero same-timestamp groups in 1,375 records** |
| step 1 clumped although it was called "fresh" | **it was not.** `records=199367` at the first probe line, and the boot log reads `RESTORED from checkpoint`, not a pre-genesis reset. **Once a block has been mined a restart no longer wipes the journal**, so every record ever written at 9000X was still present |

> **Three sessions were spent looking for a defect in the reader.** The reader was correct throughout. What
> made it findable in the end was one measurement that contradicted the hypothesis — and the lesson is the
> project's own: *diagnose with numbers, never guess.* Five candidates had already been eliminated by
> reading (§9.10) and the sixth would have been eliminated the same way, one session later.

**The real defect this exposed is in the WRITER, and it outlives the setting that caused it.** At a high DEV
scale the simulation stamps up to ten bets with a single instant, so the journal asserts that ten bets
happened simultaneously and that nothing happened for the next 150 game-seconds. Those records are
permanent, and every consumer that reads timestamps inherits the distortion: mini-plan 05's two-line
separation, INC-002's streak metrics, P8's cadence signature, and `MaxAppendRowsPerFrame`'s own calibration
note — which already assumes groups cap at 10, tolerable at 100X where they essentially never occur, and the
norm at 9000X.

**Carried to `mini08-timestamp-fidelity-and-throughput-limits-plan.md`** (developer, 2026-08-26): fix the
writer, then find out how far the engine can actually be pushed.
