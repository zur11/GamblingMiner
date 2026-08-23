# Mini-Plan 07 — Auditing `UserStatsService`, and closing INC-003 with it

**Series note:** seventh entry of the *mini-plan* series, following
`mini06-clock-rewind-reproduction-plan.md` (specified, not started). Numbering follows the established
`miniNN-<slug>-plan.md` convention of `AIHelperFiles/` — `mini01` … `mini06` exist; this is `mini07`.

**Status:** 📋 **SPECIFIED, NOT STARTED — awaiting review.** Nothing in it has been executed.

**Why one plan and not two.** INC-003's blast radius is stated as *"every lifetime figure inherits it —
`Rollup.TotalBets`, total wagered, net profit, and every max/streak"*. Every one of those figures is
owned by a single service, and that service's entry in `SERVICES.md` is **383 bytes, four bullets, the
shortest in the index** — and at least one of its four bullets is false against the code (§A.2). Closing
the incident and auditing the owner are the same reading of the same file; splitting them would make the
audit re-derive what the incident work already had open, and would let the incident close against a
description of the service nobody has checked.

**Ordering claim, up front:** the audit runs **first**, read-only, and it can invalidate the
reproduction. §Gate says under exactly which finding.

---

## 0. Verification legend

Every factual claim in this plan carries one of three marks. Requirement 6 of the brief.

| Mark | Means |
|---|---|
| **[V]** | Verified by reading the cited file:line in this repository, 2026-08-22 |
| **[M]** | Measured read-only from the live `user://` directory, 2026-08-22 (`%APPDATA%\Godot\app_userdata\GamblingMiner\`) — no app was launched, nothing was written |
| **[A]** | **Assumption.** Not verified. Stated as an assumption deliberately, and Phase A is what tests it |

Nothing here was verified by running the game. **No step of this plan launches the game** except Phase B,
whose whole subject is a run — and Phase B runs on a disposable world by construction (§B.2).

---

# PHASE A — Audit `UserStatsService` (read-only, no code changes)

**Constraint that governs the whole phase:** it is a **reading**. No file in `Scripts/` is edited, no
`user://` file is written, the game is not launched. Its only outputs are documentation (§A.5) and a set of
findings, some of which will propose work that this plan does **not** carry out (§Negative scope).

## A.0 — Preconditions, and the archive inventory that removes the time pressure

> **⚠ Revised 2026-08-22, after the archive inventory below.** The first draft of this section said the
> contaminated world exists only in the live `user://` and that Phase A had to read it before the wipe.
> **That was wrong, and it was checkable.** The evidence lives in a frozen archive that is byte-identical
> to the live world. Phase A is under **no** time pressure, and the wipe destroys nothing. The consequences
> run through §A.4.3, the Gate, and **D3**.

### A.0.1 — Every archived world, measured

Eight worlds exist [M, all figures below]. Six sit in `%APPDATA%\Godot\` itself — outside `app_userdata`,
which is why the first draft found only one. Bands are the four contaminated hours INC-003 names
(`2009-05-22 T19`, `2009-05-23 T19/T20/T21`); `180/h` is the correct 5-credit rate.

| Archive | Taken | Size | Journal | Game-time span | Rollup | Bands | Same lineage as live? |
|---|---|---|---|---|---|---|---|
| `GamblingMiner_backup_INC001_2026-07-29` | 2026-07-29 | 1.5 GB | 115 files, 5,333,202 lines, **1,091,554 unique** | 2010-03-21 → 2012-09-22 | ✗ none | 0 | **no** — 0 shared Ids |
| `GamblingMiner_P15.8_run_2026-07-30` | 2026-07-30 | 33 MB | **no journal** | — | ✗ none | — | n/a |
| `GamblingMiner_100k_run_2026-08-07` | 2026-08-07 | 30 MB | 11 files, 105,050 | 2009-03-21 → 2009-04-15 | ✗ none | 0 | **no** — 0 shared Ids |
| `GamblingMiner_prefix_run_2026-08-12` | 2026-08-12 13:28 | 57 MB | 21 files, 204,243 | 2009-04-04 → 2009-05-24 | ✗ none | 180/167/170/170 = **normal rate** | **no** — 0 shared Ids |
| `GamblingMiner_postfix_100k_2026-08-12` | 2026-08-12 19:52 | 29 MB | 11 files, 100,147 | 2009-03-21 → 2009-07-17 | ✗ none | 36/36/36/36 (1-credit regime) | **no** — 0 shared Ids |
| **`GamblingMiner_fresh5cred_2026-08-12`** | 2026-08-12 22:12 | 58 MB | 21 files (chunks 5–25), 202,817 | 2009-04-02 → **2009-05-21 19:13** | ✗ none | **0 — pre-contamination** | ✅ **YES — 167,900 shared Ids** |
| **`app_userdata\GamblingMiner_INC003_evidence_2026-08-20`** | 2026-08-20 | 55 MB | 20 files, 193,660 | 2009-04-09 → 2009-05-28 | ✅ `TotalBets 223,137` | **270/333/359/361** | ✅ **byte-identical to live** |
| `app_userdata\GamblingMiner` (live) | — | 55 MB | 20 files, 193,660 | 2009-04-09 → 2009-05-28 | ✅ `TotalBets 223,137` | **270/333/359/361** | — |

Three facts fall straight out of that table and none of them were in the first draft:

1. **The band counts in the live world are exactly INC-003's quoted anomaly rates** — 270 / 333 / 359 /
   361 against a correct 180/h [M; INC-003 quotes the same four numbers]. The contamination is measurable
   by counting lines, and it is confirmed present in the archive.
2. **Only two worlds have a rollup at all** — the live one and its archive. Every earlier archive predates
   mini-plan 03. `fresh5cred`'s checkpoint has **no `BetStatsRollup` field** [M: its
   `block_session_checkpoint.json` top-level keys, `CapturedAtUtc 2026-08-13T03:10:03Z`]. **There is no
   pre-contamination rollup baseline anywhere.** That is a hard limit on §A.4.3, not a search that has not
   been done yet.
3. **Lineage is decided by record Id, and it is decisive.** Of the six candidates only `fresh5cred` shares
   Ids with the live world (167,900 of live's 193,660). The other five share **zero** — they are separate
   runs, not ancestors, whatever their game-time spans suggest.

### A.0.2 — The authoritative evidence, and its fidelity, now verified

**`GamblingMiner_INC003_evidence_2026-08-20` is the authoritative evidence for INC-003**, and it is a
*perfect* copy, not an approximate one [M]:

```
md5(bet_stats_rollup.json)        live == archive   399df9708822d12a01c3a91daa98149c
md5(cat bet_history_*.jsonl)      live == archive   9f1f4decd434a5266d5289e68398384a
```

It also holds both `.prerepair` files — `block_session_checkpoint.json.prerepair` (INC-003's dating
evidence) and `bet_stats_rollup.json.prerepair`. **The live world adds nothing the archive lacks.**

**Consequences, and they are the reason this section was rewritten:**

- **The wipe destroys no evidence.** Mini-plan 05 §6.1's archive-then-wipe ordering was satisfied on
  2026-08-20, and satisfied exactly. D1 may be executed at any time without waiting for Phase A.
- **The live world may keep being played and keep pruning.** Anything retention deletes from it is already
  frozen in the archive. There is no race.
- **Phase A reads the ARCHIVE, not the live world** — reversing the first draft. A frozen input is
  reproducible; a live one is not, and a measurement nobody can repeat is a measurement nobody can check.
- **`fresh5cred` is promoted to evidence.** It is a genuine pre-contamination ancestor of the live world,
  and §A.4.3 R2 changes because of it.

3. **`NetworkRoot.WorldFormatVersion` is still `5`** [V:
   `Scripts/BlockchainPort/Simulation/NetworkRoot.cs:64`] — mini-plan 05 §6's wipe has not been executed.
   This is now a statement of fact about D1's status, **not** a countdown.
4. **`main` carries mini-plan 05's diagnostics** [V: `git log` — `6f4b475 Merge mini-plan 05`]. This
   satisfies mini-plan 06 §2's branching precondition: Phase B branches from `main`, not from
   `bet-journal-single-actor`.

## A.1 — What the service actually maintains

Each row is a deliverable of the audit: the audit must confirm or correct it, and the confirmed table is
what goes to `SERVICES.md`. The line references are what makes it checkable rather than assertable.

### A.1.a — Two parallel figure sets, not one

| | `Stats` (`UserBettingStats`) | `Rollup` (`BetStatsRollup`) |
|---|---|---|
| Nature | In-memory only, rebuilt at boot | **Persisted**, `user://bet_stats_rollup.json` [V: `UserStatsService.cs:27`] |
| Holds | totals, since-deposit trio, wins/losses, drawdown pair [V: `Scripts/User/UserBettingStats.cs:8-20`] | all of the above **plus** `MaxBetAmount` / `MaxLossAmount` / `MaxWonAmount` and per-`(GameId, Chance)` segment aggregates + run maxima [V: `Scripts/User/BetStatsRollup.cs:35-82`] |
| Since boot stage 1 | **reconstructed from the rollup, not from the journal** [V: `UserStatsService.cs:56-59`] | loaded from disk [V: `:279-302`] |
| Survives pruning | no — it is a projection of whatever the rollup holds | **yes — this is its entire reason to exist** [V: `BetStatsRollup.cs:8-18`] |

**The consequence worth extracting:** since mini-plan 03 stage 1, **a boot with a rollup file on disk reads
zero bet records** — `Stats = UserBettingStats.FromRollup(Rollup); return;` [V: `UserStatsService.cs:52-59`].
The journal is no longer the source of the player's lifetime figures. It is the *replay window*.

**Note the asymmetry the audit must state plainly:** the maxima and the streaks exist **only** in the
rollup. `UserBettingStats` has no `MaxBetAmount` and no streak fields at all [V:
`UserBettingStats.cs:8-20`] — so any consumer reading `Stats` for those figures cannot be reading them,
and any consumer showing them is reading `Rollup` directly. That is a fact about the API surface which
today is discoverable only by opening both files.

### A.1.b — What is pruned, and what survives it

- The journal is chunked at **10,000 entries per file** [V: `Scripts/History/BetHistoryRepository.cs:16`],
  retained at a cap of **20 SEGMENTS** [V: `:22`], oldest-segment-first deletion [V: `:434-461`].
  **The cap is on segments, not on records, so the retained count OSCILLATES — `SERVICES.md` must declare
  it as "20 segments of 10,000, ~190,000–210,000 records depending on where the active segment sits", and
  never as a flat 200,000** (developer, 2026-08-22). Two real worlds bracket it [M]: live holds
  **193,660** (19 full + a 3,660 partial) and `fresh5cred` holds **202,817** (20 full + a 2,817 partial,
  the documented `cap + 1` case) — a 9,157-record spread between two snapshots of the same lineage.
- **A rewrite re-sorts the journal by timestamp** — `RebuildJournalFromCurrentState` writes deposits then
  `_records.OrderBy(r => r.TimestampUtc)` [V: `:869-878`], while ordinary `Add` merely appends [V:
  `:~330`]. So the journal is *append-ordered between rewrites and timestamp-ordered after one*, and a
  rollback/clear converts one into the other. **This matters to §A.4** and is written down nowhere.
- The rollup survives pruning because it is maintained **on every settled bet** [V:
  `UserStatsService.cs:219-221`], not derived at boot.
- **`IsComplete` is a claim about the rollup's *seeding moment*, not about the journal's current state**
  [V: `UserStatsService.cs:79-80` sets it once; `BetStatsRollup.cs:25-30` documents it]. A rollup seeded
  before the first prune stays `IsComplete = true` forever and correctly so, even with 29,000 bets now
  outside retention. **Completeness is a coverage claim; it says nothing about validity** — which is
  precisely the distinction INC-003 turns on (§A.4).

### A.1.c — How it persists, and the defect chain the reading turned up → **INC-004**

> **Resolved 2026-08-22 (developer, D4).** The first draft filed these as two independent latent findings.
> They are **one defect and one causal chain**, and they are filed together as **INC-004**, not split
> between an incident and a future mini-plan:
>
> **A-F1 (non-atomic write) → A-F2 (failed load silently zeroes) → A-F3 (`IsComplete: true` on a rollup
> that isn't).** A crash inside the write window damages the file; the next boot converts the damage into
> a zeroed rollup that *claims completeness*; the next settled bet writes that claim back over the only
> surviving copy of the pruned history.
>
> **The fix for A-F1/A-F2 lands BEFORE the wipe** (D1). The developer's reason is the governing one:
> *a clean rollup that can lie again is worse than the current one, because the current one is known to
> be wrong.* The wipe is last, and it is not urgent — the evidence is frozen (§A.0.2).

**Finding A-F1 — the rollup writer is not atomic.** `SaveRollupIfDirty` opens the real path in
`FileAccess.ModeFlags.Write` (truncate) and streams the serialized rollup into it [V:
`UserStatsService.cs:304-322`]. There is no `.tmp` → flush → rename. CLAUDE.md's Important Pattern 2
sequel requires exactly that of *"player-owned state"*, and the rollup is, past the pruning boundary, the
**only** copy of the pruned bets' contribution [V: the file's own comment, `:170-172`]. A crash mid-write
truncates the lifetime history irrecoverably. This is INC-001's first of three questions, unanswered on a
file created *by* INC-001's remediation.

**Finding A-F2 — a failed load can be persisted back over the good copy.** If `LoadRollup` throws, it
prints and returns, leaving `Rollup` at its `new()` default [V: `:288-302` — the `catch` does not
rethrow and sets no failure flag]. `_Ready` has already established `hadRollupFile == true`, so it takes
the stage-1 branch: `RollupIsAuthoritative = true; Stats = FromRollup(Rollup); return;` [V: `:45-59`].
The very next settled bet sets `_rollupDirty` [V: `:220`] and the next flush **writes the zeroed rollup
over the file that failed to parse** [V: `:268-276` → `:304-322`]. That is INC-001's third question —
*"can a failed load ever be persisted back over the good copy?"* — answered *yes*, on the
lifetime-history file. The `catch` even says the file is the only record of the pruned bets [V:
`:296-298`], which makes this the §39.16-rule-1 shape: the risk was named and the guard was not written.

**A-F3 — the rollup on disk claims a completeness it does not have. ✅ MECHANISM OBSERVED, 2026-08-22.**
The live rollup reads `IsComplete: true, SeededAtUtc: null` while being short by ≥50,000 records
(§A.4.3). Only one code path can produce that pair on a world with history, and it was **reproduced in
isolation** rather than argued — see §A.6.

**These are durability findings, not INC-003 findings.** They belong to the audit because the audit is
what opened the file. Their disposition is settled: **INC-004, fixed before the wipe.**

### A.1.d — Who reads its figures

The audit's job is to close this list, not to trust it. Current reading [V, all]:

| Consumer | Reads | Line |
|---|---|---|
| `BlockSessionCheckpointService` | `CaptureRollupSnapshot()` at each block; `ApplyRollupSnapshot()` on restore | `Scripts/Services/BlockSessionCheckpointService.cs:238`, `:176` |
| `BetsHistoryExplorer` | `Rollup` directly, to compute the pruned prefix by subtraction | `Screens/BetsHistoryExplorer/BetsHistoryExplorer.cs:709` |
| `FinancialBettingStats` | subscribes `StatsChanged`, renders via `PlayerFinancialStatsCalculator` | `UI/FinancialBettingStats/FinancialBettingStats.cs:47,87` |
| `ClientsBetsHistory` | `Stats.TotalAmountWagered` and `Stats` for the player's row | `Screens/CasinoGamblingFinances/ClientsBetsHistory.cs:143,190` |
| `CalendarsNavigator` | `GetOldestRetainedBetUtc()` — the replay-window floor | `Screens/CalendarsNavigator/CalendarsNavigator.cs:182` |
| `DiceGame` | `GetRecentBets`, `NoteBalanceDiscontinuity`, `RollbackHistoryToUtc`, writes as `SourceDiceGame` | `Screens/DiceGame/DiceGame.cs:312,495,539,1209,1601,2262,2269` |
| `SimulationService` | writes as `SourceSimulation` | `Scripts/Services/SimulationService.cs:599` |
| **`BankrollProgramService`** | **copies `Stats.TotalAmountWagered` / `TotalProfit` into a ledger entry** | `Scripts/Services/BankrollProgramService.cs:100-101` |
| **`PlayerBankAccountService`** | **same copy, same destination** | `Scripts/Services/PlayerBankAccountService.cs:102-103` |
| **`CasinoClientLedgerService`** | **stores them as `TotalWageredSnapshot` / `NetProfitSnapshot`** | `Scripts/Services/CasinoClientLedgerService.cs:289-290` |

**The last three rows are the audit's first real result** and go straight into §A.4: a contaminated
lifetime figure is not only *displayed*, it is **copied into a different persisted, checkpoint-covered
file** at every recharge, deposit and withdrawal. INC-003 currently says the corruption is *"confined to
the player's own bet journal and the three consumers it feeds"* — that sentence is a candidate for
amendment (**D6**).

### A.1.e — The sentinel, and the build it needs

`AssertSingleActorJournal` is `[System.Diagnostics.Conditional("DEBUG")]` [V:
`UserStatsService.cs:137`]. **In a Release build it does not exist** — the call compiles away, and with
it the `[BetJournal] UNDECLARED balance discontinuity` line that mini-plan 06's **P5 is built on**. This
is not a defect (a debug assertion is a legitimate shape) but it is an **unrecorded precondition** of the
reproduction and of the incident's stated protection. Whether it should stay DEBUG-only is **D5**.

## A.2 — What `SERVICES.md`'s entry gets wrong or omits

Current entry, four bullets, 383 bytes [V: `Documentation/SERVICES.md:68-76`]. Audited against the code:

| Bullet | Verdict |
|---|---|
| *"Maintains persistent bet history (JSON, **chunked by month**)"* | ❌ **FALSE.** Chunking is by **10,000 entries per file**, index-numbered [V: `BetHistoryRepository.cs:16,23`]. Nothing in the repository is month-aware. **The same false claim appears in four places** [V]: `SERVICES.md:73`, `CLAUDE.md:163`, `CLAUDE.md:181`, `Documentation/ARCHITECTURE.md:109`. Requirement 5's "verify it does not contradict" applies here first — this is an existing contradiction between the docs and the code, and the Document Policy's rule 2 says correct the false one and say so |
| *"Emits `StatsChanged` … 250 ms"* | ✅ true [V: `:13,504-527`] — but the throttle applies **only in high-frequency mode**; outside it every registration emits immediately [V: `:506-510`] |
| *"Supports time-travel balance reconstruction"* | ✅ true [V: `:549-572`] |
| *"Key method: `OnBetExecutedRegisterBet()`"* | ✅ true, and now takes a **`source`** parameter defaulting to `"unknown"` [V: `:193`] |
| **Absent entirely** | the **rollup** — its file, its purpose, `IsComplete`/`SeededAtUtc`, its checkpoint coverage, the never-re-seed rule, the retention cap it exists to survive, the discontinuity sentinel and its declaration API, the `RegisterSource` dead path, the pre-genesis `ClearAllHistory` path, and the fact that boot no longer reads the journal |

**Deliverable:** a rewritten `SERVICES.md` section covering the table in §A.1, at the density of the
`CasinoClientLedgerService` / `PlayerBankAccountService` entries (which are the house standard for a
service of this weight), plus the four-site correction of the month-chunking claim.

## A.3 — Rules and invariants reachable today only by reading the code

Candidates for **Important Patterns** (permanent, cross-cutting) versus `SERVICES.md` (service detail).
Classifying each is part of the phase; the proposed split is:

**→ Important Patterns (they govern future work anywhere):**

1. **A store cannot report its own completeness once anything is allowed to rewrite it.** Completeness is
   tracked by whoever owns the history, seeded once and thereafter adjusted only by the checkpoint [V:
   `BetHistoryRepository.cs:375-397` — `HasPrunedHistory` is `[Obsolete]` *and kept as a warning*;
   `UserStatsService.cs:60-88`]. This already has a corpse and a comment; it has no home in the docs.
2. **`IsComplete` is a coverage claim, never a validity claim.** A running total that counted every bet
   is "complete" even when some of what it counted was wrong. §A.4 is the whole case. This is the
   `ProjectDesignManual` §40.8 rule — *"a label is a claim about semantics"* — arriving on a new field,
   which is an argument for stating it once, generally, rather than a third time per incident.
3. **A diagnostic that is `Conditional("DEBUG")` is absent from the shipped build**, so an invariant
   defended only by one is undefended in release. (Pairs with **D5**.)

**→ `SERVICES.md` (service-specific, but invisible today):**

4. The rollup is **seeded exactly once and never re-derived**; `RebuildStatsFromLoadedHistory` reseeds it
   only while `RollupIsAuthoritative` is false [V: `UserStatsService.cs:578-583`].
5. **`ClearAllHistory` (pre-genesis) zeroes the rollup explicitly**, because the rebuild deliberately
   won't [V: `:386-394`] — and restores `IsComplete = true`, since clearing un-prunes by definition.
6. **Legitimate discontinuities must declare themselves**; `RegisterDeposit` declares on behalf of every
   funding path so a new one inherits the exemption [V: `:227-232`], and `NoteBalanceDiscontinuity` drops
   the baseline rather than setting a skip-once token [V: `:128-136`].
7. **`RollbackHistoryToUtc` must load every chunk first**, or a rollback of a few bets destroys the whole
   retained history [V: `:355-364`] — a comment that already begs not to be optimised away.
8. **The retention cap's true ceiling is `cap + 1`** — the in-progress segment is not on disk when the cap
   is enforced [V: `BetHistoryRepository.cs:17-21`].
9. `RegisterSource` has no callers and is kept **as a tripwire** that prints on use [V: `:257-266`].

## A.4 — The crossing with INC-003

This is the section the brief exists for, and the one where saying *"not recoverable"* is a result.

### A.4.1 — The figure INC-003 quotes has already moved

| Source | `Rollup.TotalBets` | Note |
|---|---|---|
| INC-003 blast radius [V: `Documentation/INCIDENT_LOG.md`, INC-003 §Blast radius] | **215,723** | the value when the entry was written, 2026-08-19 |
| mini-plan 06 §3.1, archive of 2026-08-20 | **223,137** | |
| **Live `user://` today** [M] | **223,137** | `bet_stats_rollup.json`, `IsComplete: true`, `SeededAtUtc: null` |

**The incident quotes a moving figure as if it were fixed.** The world kept being played between the
entry and the archive. A first Phase A output is therefore trivial and worth doing: **restate INC-003's
blast-radius figures as of a named artefact**, not as bare numbers. (The archive is that artefact.)

### A.4.2 — The measurable facts, today

- Journal: **20 chunks, 193,660 lines** spanning **2009-04-09T10:33:38Z → 2009-05-28T01:27:53Z** [M].
  Lines include deposit entries, so the bet count is ≤ 193,660; the exact split is an A.4 measurement.
- Rollup: **223,137 bets** [M]. **≈ 29,500 bets are outside the retention window** and exist nowhere but
  as their contribution to the rollup's running totals.
- **The contaminated band is still inside the retained window.** INC-003 places it at `2009-05-22 T19`
  and `2009-05-23 T19/T20/T21`; the journal retains from `2009-04-09` [M]. **The contaminating records
  are still on disk and still measurable** — and, per §A.0.2, **frozen in the archive**, so the format
  bump does not end it. Their exact counts are `270 / 333 / 359 / 361` against a correct `180/h` [M],
  reproducing INC-003's four quoted rates line-for-line.
- A second dated artefact exists that INC-003 does not cite: **`bet_stats_rollup.json.prerepair`**,
  `TotalBets: 205,562`, older schema (run maxima only, no per-segment aggregates, no since-deposit trio)
  [M]. Its sibling `block_session_checkpoint.json.prerepair` is INC-003's dating evidence.

### A.4.3 — Is the rollup recoverable? The honest analysis

Recovery would mean: compute the contaminated records' contribution and subtract it from the rollup. That
requires **three** things, and the audit's job is to say which hold.

| | Requirement | Status |
|---|---|---|
| R1 | The contaminated records must be **identifiable** | **Plausible for the retained window.** Mini-plan 04 §13's two-line separation did it by hand for one band; the balance-continuity property is exact per line [V: the arithmetic is `AssertSingleActorJournal`'s, `UserStatsService.cs:146-148`] |
| R2 | The contamination must be **entirely inside** the retained window | ⚠️ **PARTIALLY DECIDABLE — revised by §A.0.1.** `fresh5cred` (2026-08-12 22:12) is a same-lineage ancestor holding **34,917 bet records the live world has since pruned** [M: 202,817 unique Ids, 167,900 shared with live], and its bands are **empty** — it ends at game time `2009-05-21 19:13`, before the first contaminated hour. So a slice of the pruned prefix is recoverable *as records*, and it is clean. **What remains undecidable is the rest**: bets pruned before 2026-08-12 exist in no surviving archive of this lineage, and the journal's timestamp-vs-write ordering flips on any rewrite [V: `BetHistoryRepository.cs:869-878`], so the ordering argument about where late-written records landed still does not survive this world's rollbacks |
| R3 | The subtraction must reconstruct **maxima and streaks**, not just sums | ❌ **IMPOSSIBLE for the maxima.** `MaxBetAmount` / `MaxLossAmount` / `MaxWonAmount` and the per-segment run maxima are **order-dependent, non-invertible reductions** [V: `BetStatsRollup.cs:132-213`]. Removing a record from a max tells you nothing about what the max *would have been*; the runner-up was never stored. The streaks are worse — INC-003 notes both streams are `Dice|50`, so the two sessions' runs were concatenated into one segment [V: the segment key is `(GameId, Chance)` only, `:84`] |

**One arithmetic fact the inventory opened, and it must not be over-read.** Live holds 193,660 bets and
`fresh5cred` holds 34,917 that live no longer has, so the union is **228,577 distinct bet records** —
against a rollup lifetime of **223,137** [M; and the live journal contains **zero** deposit lines, so
every line is a bet]. **The union exceeds the lifetime total by 5,440.** That is not proof of an
undercount: records trimmed by `RollbackHistoryToUtc` were counted when they settled and then un-counted
when the checkpoint restored the rollup alongside the journal [V: `UserStatsService.cs:325-336`,
`:344-367`], so an ancestor journal legitimately holds records the current rollup does not count.
**Explaining that 5,440 is a Phase A measurement, and it is the one that would either confirm or break
assumption [A]4 (`IsComplete: true`).**

**Conclusion, stated as the brief asks it to be stated: the rollup is NOT recoverable** — and the
inventory narrows *why* rather than overturning it. The sums could be corrected for the retained window
(R1) and now partially certified against the recovered slice of the prefix (R2), but **there is no
pre-contamination rollup anywhere** [M: §A.0.1 fact 2 — every earlier archive predates the rollup, and
`fresh5cred`'s checkpoint carries no `BetStatsRollup`], so there is nothing to subtract *from*; and the
maxima and streaks — which are the figures INC-003 flags as the longest-lived, and the ones
visible on screen as the impossible `Max bet: 964.63272326 SC` beside a bankroll of `837.66` — are
**mathematically unrecoverable** (R3). The live rollup still carries that exact max [M:
`MaxBetAmount: 964.63272326`], the same impossible figure, still on file three days later.

**A partial recovery is worse than none**, and for the reason mini-plan 05 §6 already refused option (c):
a correction applied to the sums but not to the maxima produces a rollup that is *internally
inconsistent* and *looks* repaired. That is a heuristic rewrite of history wearing a fix's clothes.

**What Phase A therefore delivers here is a measurement and a statement, not a repair:**
- the exact contaminated-band record count and its contribution to `TotalBets` / `TotalWagered` /
  `TotalNetProfit`, measured over the retained window (`awk` over the journal — the project's tool for
  this [V: CLAUDE.md's scripting table]);
- the explicit statement that the maxima and streaks cannot be recovered, and why;
- the extension of the blast radius to the ledger snapshots (§A.1.d) — a figure copied into
  `casino_client_ledger.json` at every recharge is not corrected by anything that corrects the rollup.

→ **Decision D1** is then the developer's: what happens to a rollup that is contaminated and
unrecoverable.

## A.5 — Phase A outputs, and where each goes (Document Policy)

Before writing a word into `CLAUDE.md`: **search `CLAUDE.md` and `Documentation/` for the subject first,
edit what exists, and where the new contradicts the written, verify against the CODE and correct the false
one** — the Policy's mandatory order. §A.2 has already found one contradiction that will need exactly
that treatment (the month-chunking claim, in four places).

| Output | Destination | Why there |
|---|---|---|
| The full service description (§A.1) | **`Documentation/SERVICES.md`** — rewrite the section | "A system's specification" belongs in its own doc |
| The month-chunking correction | **all four sites** [V: `SERVICES.md:73`, `CLAUDE.md:163`, `CLAUDE.md:181`, `ARCHITECTURE.md:109`] | a false statement repeated is corrected everywhere or nowhere |
| Rules 1–3 of §A.3 | **`CLAUDE.md` → Important Patterns**, as edits to the existing Pattern 2 durability block and the §40.8 label rule, **not as a new section** | they are the same subject as text already there; Policy rule 1 says edit, never append a second version |
| Rules 4–9 of §A.3 | **`SERVICES.md`** | service-specific |
| The `SERVICES.md` index line | **`CLAUDE.md:181`** — one line, corrected | the index says where detail lives; that is all it may do |
| The measurement + the unrecoverability statement (§A.4) | **`INCIDENT_LOG.md` INC-003**, amended in place | it is incident evidence, not a specification |
| Findings A-F1 / A-F2 | **wherever D4 sends them** — an INC entry, a new mini-plan, or a note. Not silently fixed | they are defects found, not defects scoped |

**Budget check:** `CLAUDE.md` is well under target after Dep-01. This plan's net effect on it should be
near zero — one corrected index line, two corrected sentences, and edits *within* existing Important
Patterns blocks. If any Phase A output wants more than ~1,500 new characters in `CLAUDE.md`, that is the
signal it belongs in `SERVICES.md` instead.

## A.6 — G6 resolved: the mechanism is REACHABLE, and the wipe does not fix it

**Question.** Is the path that writes `IsComplete: true` over a short rollup reachable in ordinary play,
or is it an artefact of this world's history? It decides whether A-F1/A-F2 are latent or live.

### A.6.1 — Elimination over every path that can write the signature

| Path | Produces `IsComplete: true` + `SeededAtUtc: null` on a world with history? |
|---|---|
| Seeding [V: `UserStatsService.cs:79-80`] | **No.** Sets `SeededAtUtc = DateTime.UtcNow` unconditionally. A null there proves seeding never ran on this file |
| `ClearAllHistory` [V: `:386-394`] | Yes — but it clears the **journal in the same call**, so rollup and journal stay consistent. It cannot produce a shortfall |
| `ApplyRollupSnapshot(null)` | **Unreachable** — guarded at its only call site [V: `BlockSessionCheckpointService.cs:173-177`] |
| **`LoadRollup` throws, or deserializes to null** [V: `:288-302`] | **Yes, and it is the only one.** `Rollup` stays `new()`; `hadRollupFile` is true so `_Ready` returns early with `RollupIsAuthoritative = true` [V: `:52-59`]; the next settled bet dirties it [V: `:220`] and the next flush writes the zeroed rollup back [V: `:304-322`] |

### A.6.2 — Reproduced, not argued

Run in a throwaway `dotnet` console over the **real** `bet_stats_rollup.json` as seed, replicating
`_Ready` → `LoadRollup` → stage-1 return → one `RegisterBet` → `SaveRollupIfDirty` exactly as written
(the project's own standard for anything that must match the engine — CLAUDE.md's scripting table):

```
CONTROL  intact file       LoadRollup OK, TotalBets=223137  → on disk: TotalBets=223138   (correct)
(a)      truncated to half LoadRollup THREW JsonException   → on disk: IsComplete=True SeededAtUtc=null TotalBets=1
(b)      literal null      deserialized to NULL             → on disk: IsComplete=True SeededAtUtc=null TotalBets=1
(c)      zero-byte file    LoadRollup THREW JsonException   → on disk: IsComplete=True SeededAtUtc=null TotalBets=1
```

**Case (c) is the likeliest crash residue**, and it matters: `FileAccess.Open(…, Write)` **truncates at
open**, so the exposure window is not "mid-write" but *from open until `StoreString` returns* — a kill
anywhere in it leaves a zero-byte or partial file. `SaveRollupIfDirty` runs at every block and every
`FlushHistory`, so the window recurs for the life of every world.

**Verdict: G6 = REACHABLE. A fresh world drifts to the same state.** The wipe cleans the contaminated
data and does nothing about recurrence — which is why the fix precedes it (D1).

**What is observed and what is not, kept apart:**

- **The mechanism: OBSERVED.** Three damage modes, all producing the signature, reproduced above.
- **This world's rollup having arrived that way: NOT OBSERVED.** Compatible and dated (§A.6.3), but no
  wall-clock record survives — Godot retains 5 logs per world, all from 2026-08-19/20, and none carries
  a rollup message [M]. **That is absence of evidence, not evidence of absence**, and it leaves A-F3's
  *history* at exactly INC-003's epistemic status. Naming that symmetry is the point.

### A.6.3 — Dating the boundary

Under the reading that the rollup began counting partway through (§A.4.3), the boundary is computable and
was computed [M]:

```
union of bets                                          228,555
less rolled-back-and-uncounted (fresh-only, 05-20/21)   −4,917
                                                       ────────
countable                                              223,638
rollup TotalBets                                       223,137
                                                       ────────
uncounted at the front                                     501
```

The 502nd countable bet falls inside the same-timestamp group stamped
**`2009-04-02T12:17:57.1445663Z`** — so the rollup's first counted bet is there, in game time.

**It sits BETWEEN blocks**, not on one: the chain holds block 130 at `2009-04-02T04:47:47Z` and block 131
at `2009-04-02T21:35:45Z` [M: `blockchain/state.json`, 210 blocks]. That is a real elimination — a
checkpoint restore would land the boundary **on** a block, because that is when a snapshot is captured.
It does **not** correlate with either `.prerepair` artefact (those carry a game clock of `2009-05-24
13:54:40`, wall clock `2026-08-14`).

**The 501 is DECLARED AS A HOLE, not absorbed.** It is 0.22% of the total and it is not explained. It is
the position of the boundary under one reading and an unexplained residual under the other, and this plan
does not choose between them by rounding. Candidates worth one measurement each: a second, smaller
rollback whose trimmed records survive in neither journal; or a mismatch between the journal's rollback
boundary (`HistoryCheckpointUtcTicks`) and the rollup snapshot captured at the block.

---

# GATE — the stop condition between the phases

**Phase B does not start automatically.** Phase A is run, its findings are reported, and then one of the
following holds. Requirement 2 of the brief.

| Finding in Phase A | Effect on Phase B |
|---|---|
| **G1 — a second writer is found that is not the clock** (a code path that can stamp a bet with a past timestamp, or a third `source` in the journal) | **Phase B is CANCELLED as specified.** Mini-plan 06 reproduces *one* named mechanism; if the audit finds a different live one, reproducing the retired one proves the wrong thing. INC-003's root fault returns to open with a new lead, and the next plan reproduces *that* |
| **G2 — the sentinel cannot fire in the build the reproduction will use** (§A.1.e, unresolved) | **Phase B is POSTPONED** until D5 is answered. P5 is the discriminator; a run that cannot print it produces P1–P4 and no verdict — the exact ambiguity mini-plan 05 spent a week in |
| **G3 — the contaminated band is measured and found NOT to match the clock-rewind shape** (the band is unbounded, or the intruder is *not* born mid-progression under exact measurement) | **Phase B is POSTPONED** pending re-reading. P1–P4 are calibrated against INC-003's description of the band; if that description does not survive exact measurement, the predictions are miscalibrated before the run starts |
| **G4 — D1 chooses "wipe now"** and the wipe is executed before Phase B | **Phase B proceeds unchanged** — it wants a virgin world anyway (mini-plan 06 §3.2). The wipe is a precondition it already asked for. **Revised:** the wipe is also no longer a deadline for Phase A (§A.0.2), so G4 may now fire *before* Phase A without cost |
| **G5 — D2 answers "the reproduction is not worth it"** | ✅ **FIRED as DEFERRED, 2026-08-22** — *not* cancelled. Mini-plan 06 stays on the shelf intact; INC-003 stays `LEADING BUT NOT OBSERVED` and says so explicitly |
| **G6 — the mechanism writing `IsComplete: true` over a short rollup is reachable in normal play** | ✅ **FIRED, 2026-08-22 — REACHABLE, and the mechanism was reproduced (§A.6).** Phase B is **preempted**: a live defect on `main` outranks reproducing a mechanism closed since 2026-08-16. **And the wipe does not fix it** — a fresh world drifts to the same state — which is why the A-F1/A-F2 fix precedes it |
| Everything else | Phase B proceeds as §B |

**The default is not "both phases run."** Four of the six listed outcomes stop or defer it — and in the
event **two fired**: G5 (deferred by decision) and G6 (preempted by a live defect). **Phase B is not
running.**

---

# PHASE B — The deliberate reproduction (mini-plan 06)

**This plan does not re-specify mini-plan 06.** `mini06-clock-rewind-reproduction-plan.md` is the
procedure; it is incorporated by reference and stays the operational document. What follows is only what
changes, what is added, and what the audit exposed about it.

## B.1 — What of mini-plan 06 stands, and what changes

**Stands unchanged, and the audit strengthens it:**

- §2's *"branch from `main` and re-add the mechanism, do not check out the old commit"* — and its
  precondition is now met: mini-plan 05 is merged [V: `git log`, `6f4b475`].
- §3's *archive → wipe → reproduce on a virgin world* ordering, including the argument that a world
  already holding two balance lines makes P1–P5 ambiguous.
- §4's six-step procedure, §5's measurements, §7's emit-budget validation.
- §8's out-of-scope list.

**Changes or additions, all from Phase A:**

| # | Change | Source |
|---|---|---|
| B1.1 | **The run must be a DEBUG build.** `AssertSingleActorJournal` is `Conditional("DEBUG")` [V: `UserStatsService.cs:137`], so P5 exists only there. Mini-plan 06 never states this; a Release run would silently produce four of five predictions and no discriminator | §A.1.e |
| B1.2 | **Add a sixth observation, P6: the rollup's delta across the run.** The audit's subject is the rollup, and the run is the one context where its input is known exactly. Capture `bet_stats_rollup.json` before and after and check `TotalBets` against the counted records. One file copy, and it tests §A.1.b's "maintained on every settled bet" claim under the exact conditions that broke it | §A.1.b |
| B1.3 | **Capture `MaxBetAmount` too.** INC-003's most legible symptom was a max bet exceeding the bankroll. If the harness reproduces the band, it should reproduce *that*, and it is one field | §A.4.3 R3 |
| B1.4 | **Mini-plan 06 §3.1's archive figure needs a note**: it records `Rollup.TotalBets = 223,137` while INC-003 records `215,723`. Both are correct for their moment; neither says so | §A.4.1 |
| B1.5 | **Do not let the harness branch touch the rollup's writer.** If D4 sends A-F1/A-F2 to a fix, that fix lands on its own branch off `main` — never on `repro/explorer-clock-rewind`, which by construction never merges | §B.2 |

## B.2 — The risk of re-adding a retired mechanism, and how it is contained

The harness deliberately re-creates a defect that was fixed on 2026-08-16 [V: INC-003 §Fix, commit
`95860f4`]. Four containment layers, in order of how much they are relied upon:

1. **Branch.** `repro/explorer-clock-rewind`, cut from `main` [V: precondition met]. **A branch, not a
   detached HEAD** — mini-plan 06 §2's reason stands: the harness is a commit.
2. **Default-OFF constant.** `const bool DevRewindWorldClock = false;` in `BetsHistoryExplorer`, in the
   `TimelineConfig.DevAltTimeline` spirit — false on any branch that could merge, forever (mini-plan 06
   §2.1).
3. **Loud on boot.** `GD.PrintErr` in `_Ready` while it is true, so a build carrying it cannot pass for a
   normal one (mini-plan 06 §2.1).
4. **Deletion.** The branch is deleted once INC-003 is settled (mini-plan 06 §6).

**What must not touch `main`, stated as a list rather than as a principle:**

- the `DevRewindWorldClock` constant and every line guarded by it;
- the temporary `MaxAppendRowsPerFrame = 1` of mini-plan 06 §7;
- the corrupted world the run produces — it is `user://` state, not repository state, and the wipe that
  follows is its disposal;
- **any documentation edit.** Phase A's documentation lands on a normal branch off `main`, before or
  independently of Phase B. Docs written on a throwaway branch die with it.

**Reversion is deletion, not revert.** Nothing on this branch is ever merged, so there is no commit on
`main` to undo. If the branch is somehow merged in error, the recovery is `git revert` of the merge plus a
grep for `DevRewindWorldClock` — which should return zero hits on `main` at all times, and is worth
stating as a standing check.

**The residual risk the layers do not cover:** the developer runs the harness build, forgets, and keeps
playing. The world is disposable by then (post-wipe), so the cost is bounded to that world — but the
harness build should not be the one left in the editor. **Mitigation: delete the branch immediately after
the run, before writing the results up**, rather than after.

## B.3 — What P5 proves, and what it does not

P5 is `[BetJournal] UNDECLARED balance discontinuity` firing and **naming the same writer on both sides of
the break** [V: the message carries `source` and `_lastRegisteredSource`, `UserStatsService.cs:158-167`].

**What it proves.** That a single writer — `SimulationService` — produced two records whose balances do
not chain, *without* any declared discontinuity. Since every legitimate break declares itself
(`RegisterDeposit` declares for all funding paths [V: `:227-232`]; rollback and clear declare [V: `:346`,
`:374`]; DiceGame declares on node load, wallet reseed and checkpoint restore [V:
`DiceGame.cs:539,1209,2262`]), an undeclared break under one writer means **the writer's own sequence was
re-ordered underneath it** — which is what a rewound clock does, and what nothing else in the enumerated
set does. Combined with P1–P4, that is the observation INC-003 lacks.

**What it does not prove — four things, and they matter for how the result is written up:**

1. **It does not prove the clock is the *only* mechanism that can do this.** It shows the clock is
   *sufficient*. Any other reordering source would produce the same signature; the audit's enumeration of
   writers (§A.1.d) is what narrows sufficiency toward necessity, and even that is an enumeration of
   *today's* code, not of the code as it stood before 2026-08-16.
2. **It does not prove the historical band was produced this way.** It proves the mechanism produces that
   shape. The historical claim rests on dating [V: INC-003's `.prerepair` checkpoint evidence] plus
   mechanism-fit; the reproduction upgrades *mechanism-fit* from "argued" to "observed" and leaves dating
   exactly as strong as it already was. **This is the single most likely place for the result to be
   overstated in the write-up.**
3. **A same-writer break does not identify *which* records are the intruders.** The sentinel fires at the
   boundary; assigning records to lines is still mini-plan 05's balance-continuity separation. P5 is a
   detector, not a classifier — which is exactly why §A.4.3's R1 is "plausible" rather than "solved".
4. **It says nothing about the rollup.** The rollup counted both lines identically and correctly [V:
   `:219-221` — one call per settled bet, no notion of a writer]. **The rollup has no defence against
   this class of defect and P5 does not give it one**; the sentinel guards the *journal's* legibility, not
   the running total's validity. Closing that gap is separate work and is not in this plan.

**And the negative result:** if the harness cannot produce the shape, mini-plan 06 §5 already says it —
the clock hypothesis is wrong despite fitting, and INC-003's root fault returns to **open**. That outcome
is a success of the method and must be written up with the same weight as a confirmation.

---

# Decision points — the developer's, not Claude's

Requirement 1. Six, of which two were anticipated in the brief. **All six are now DECIDED (developer,
2026-08-22).** The reasoning is kept because the reasoning is what a later reader needs.

**The resulting order of work — this supersedes every ordering statement written earlier in this plan:**

1. **Fix A-F1 + A-F2** (atomic write; a failed load must never be written back). Before anything else.
2. **File INC-004** — the three findings as one causal chain.
3. **Phase A's documentation** (§A.5), on a normal branch.
4. **The wipe** (D1a). Last. No urgency: the evidence is frozen (§A.0.2).
5. **Mini-plan 08** — only whatever is left over that the INC-004 chain does not absorb, if anything.
6. **Phase B / mini-plan 06** — deferred, on the shelf (D2).

| # | Decision | Why it is not Claude's | What it blocks |
|---|---|---|---|
| **D1** | ✅ **DECIDED — (a) WIPE, but LAST, and only after A-F1/A-F2 are fixed.** The rejected options and the reason: (b) keep playing — refused; (c) hand-set the flags — refused as a partial repair. **The developer's governing reason:** *a clean rollup that can lie again is worse than the current one, because the current one is known to be wrong* | It traded a playtest world against figure integrity | Nothing now — the wipe is last and unhurried |
| **D2** | ✅ **DECIDED — DEFERRED, not cancelled.** Mini-plan 06 goes on the shelf intact. **INC-003 stays `LEADING BUT NOT OBSERVED` and must say so explicitly** — not by omission, not softened | How much certainty an incident record is worth | Phase B (gate G5, fired as *deferred*) |
| **D3** | ~~**When does the wipe happen relative to Phase A?**~~ **DISSOLVED, 2026-08-22 — no decision needed.** The premise was that Phase A needs the live world and the wipe would destroy it. The archive is byte-identical to the live world by checksum, and Phase A now reads the archive by preference (§A.0.2). **The wipe may happen whenever D1 says, before or after Phase A.** Recorded rather than deleted, because the reasoning that retired it is the useful part | — | nothing |
| **D4** | ✅ **DECIDED — one defect, one chain, one entry: INC-004.** A-F1 → A-F2 → A-F3 are not split between an incident and a mini-plan. **The A-F1/A-F2 fix precedes the wipe.** Mini-plan 08 keeps only what the chain does not absorb | Scope | Step 1 of the order above |
| **D5** | ✅ **DECIDED — the sentinel must exist in RELEASE**, using the project's existing two-half pattern, **verified 2026-08-22**: a release-safe quantity written to a trace in every build, plus a `Conditional("DEBUG")` assertion over the same quantity. Reference: `AssertBotBallotsVary` [V: `NetworkRoot.cs:4172`, attribute at `:4279`] beside the unconditional `spread={2:F1}` trace emission [V: `:4173-4178`]. **The pattern is exactly as the developer described it** | A shipped-diagnostics-cost decision | Gate G2, now satisfiable |
| **D6** | ✅ **DECIDED — YES, widen the blast radius.** INC-003 gains `CasinoClientLedgerService`'s `TotalWageredSnapshot` / `NetProfitSnapshot` [V: `CasinoClientLedgerService.cs:289-290`], a second persisted, checkpoint-covered file carrying copies of the contaminated lifetime figures | What the incident *was* | The INC-003 edit in §A.5 |

---

# Closure criteria — including the negative case

Requirement 3. The standard is INC-003's own `LEADING BUT NOT OBSERVED`: a conclusion that names its own
epistemic status rather than rounding itself up to a fact.

## Phase A closes when

1. Every row of §A.1 is confirmed or corrected **with a file:line**, and every claim that could not be
   confirmed by reading is marked **[A]** in the written output.
2. `SERVICES.md`'s section is rewritten; the four month-chunking sites are corrected; the `CLAUDE.md`
   index line is one line.
3. §A.4's measurement exists as numbers with a named source artefact, and the recoverability verdict is
   written in one of exactly three forms:
   - **RECOVERABLE** — with the arithmetic, and it must reconstruct maxima, not only sums;
   - **NOT RECOVERABLE** — with *which* of R1/R2/R3 fails and why (the expected outcome, §A.4.3);
   - **NOT DETERMINED** — with the specific missing measurement named, and what would settle it.
4. Findings A-F1/A-F2 are written down wherever D4 chose, **not** left in this plan.

**The negative case, explicitly:** *"could not be determined"* is registered as
**`NOT DETERMINED — <what is missing>`** in the same sentence, in `INCIDENT_LOG.md`. It is never promoted
by omission: a figure with no verdict beside it reads as a verified figure, which is the exact failure
INC-003's own `IsComplete` discussion is about. **A phase whose exit depends on evidence the world can no
longer produce is closed as suspended with its missing precondition named** — §39.16 rule 10, applied to
an audit rather than to a build phase.

## Phase B closes when

1. P1–P6 each carry **observed / not observed / not measurable**, individually. Not a single verdict.
2. INC-003's root fault is updated to exactly one of:
   - **OBSERVED** — P5 fired naming one writer on both sides, and P1–P4 hold. The write-up carries §B.3's
     four limits verbatim, so "observed" is not read as "proven necessary";
   - **RULED OUT** — the harness ran the mechanism and the shape did not appear. Root fault returns to
     **open**, with this mechanism struck off;
   - **NOT REPRODUCED — INCONCLUSIVE** — the run produced no clean signal either way (harness misfired,
     sentinel compiled out, world not virgin). **This is not "ruled out"**, and the distinction is the
     whole reason for writing three outcomes instead of two.
3. §7's emit-budget validation carries its own separate verdict — a different question that happens to
   share a harness.
4. The branch is deleted and `grep -rn "DevRewindWorldClock"` on `main` returns nothing.

---

# Negative scope — what this plan does NOT do

Requirement 4. Each of these is real, some are already agreed, and none of them enters here.

- **It does not fix the clock-rewind mechanism.** Already fixed, 2026-08-16, `95860f4`.
- **It does not repair, filter, or rewrite the contaminated journal or rollup.** Mini-plan 05 §6 refused
  heuristic surgery on principle and that refusal stands. §A.4 measures; D1 decides.
- **It does not fix A-F1 or A-F2** (the rollup's non-atomic write, and its write-back after a failed
  load). Found here, filed by D4, fixed elsewhere.
- **It does not change the retention cap, the chunk size, or the rollup's schema.** Any schema change is a
  `WorldFormatVersion` bump and belongs to whoever decides D1.
- **It does not add the rollup-side defence** §B.3's fourth limit identifies. Naming a gap is not scoping
  it.
- **It does not touch the Referral Auction rename.** Lateral finding, unrelated, carried elsewhere.
- **It does not touch `.claude/settings.local.json`'s 235 permissions.** Same.
- **It does not audit the other eighteen autoloads.** `UserStatsService` is in scope because it owns the
  figures INC-003 damaged; the shortness of its `SERVICES.md` entry is a symptom, not a mandate to sweep
  the file.
- **It does not do the `_Process` / event-driven backlog audit** (Step 17 §5.1, `PRIVATE_ROADMAP` §6),
  even though `UserStatsService.StatsChanged` is that backlog's reference pattern.
- **It does not add the DiceGame → BetsHistoryExplorer button.** Mini-plan 05 §9, still deferred, and
  mini-plan 06 §8 already says not while a navigation investigation is open.
- **It does not run the `DevTimeScale 9000X` stress question.** Mini-plan 06 §8; belongs with T4.

---

# Verification appendix

Every claim of fact in this plan and where it came from. **[A]-marked items are the ones Phase A tests.**

**Verified by reading [V]** — `UserStatsService.cs` lines 13, 27, 40-95, 109-118, 137, 146-148, 158-167,
193-222, 227-247, 257-266, 268-276, 279-302, 304-322, 325-336, 344-367, 372-399, 447-457, 504-527,
574-635 · `BetStatsRollup.cs` 8-18, 25-33, 35-82, 84, 109-213 · `UserBettingStats.cs` 8-20, 65 ·
`BetHistoryRepository.cs` 14-27, 319-367, 375-397, 434-461, 855-883 · `NetworkRoot.cs` 64, 7676, 7680,
7712 · `BlockSessionCheckpointService.cs` 176, 238 · `BetsHistoryExplorer.cs` 709 ·
`BankrollProgramService.cs` 100-101 · `PlayerBankAccountService.cs` 102-103 ·
`CasinoClientLedgerService.cs` 280-290 · `ClientsBetsHistory.cs` 143, 190 ·
`FinancialBettingStats.cs` 47, 87 · `CalendarsNavigator.cs` 182 · `DiceGame.cs` 312, 495, 539, 1209,
1601, 2262, 2269 · `SimulationService.cs` 599 · `SERVICES.md` 68-76 · `CLAUDE.md` 163, 181 ·
`ARCHITECTURE.md` 109 · `INCIDENT_LOG.md` INC-003 · `git log` (mini-plan 05 merged at `6f4b475`).

**Measured read-only [M]** — `bet_stats_rollup.json` (`TotalBets 223137`, `IsComplete true`,
`SeededAtUtc null`, `MaxBetAmount 964.63272326`) · `bet_stats_rollup.json.prerepair`
(`TotalBets 205562`, older schema) · 20 journal chunks, 193,660 lines, **all of type `bet`, zero
deposits** · span `2009-04-09T10:33:38.4811376Z` → `2009-05-28T01:27:53.4331931Z` · **the full
eight-world archive inventory of §A.0.1**, including per-archive band counts, rollup presence, and
record-Id lineage overlap · **the live/archive checksum identity of §A.0.2** · `fresh5cred`'s
`block_session_checkpoint.json` key list (no `BetStatsRollup`, `CapturedAtUtc 2026-08-13T03:10:03Z`).

**Assumptions [A], to be tested in Phase A, asserted nowhere above as fact:**

1. **[A]** That the ≈29,500 pruned bets were pruned *by retention* and not by some other path. Consistent
   with 20 chunks sitting at the cap, but not proven — `RebuildJournalFromCurrentState` also deletes files.
2. **[A]** That the two writers named in the code (`SourceDiceGame`, `SourceSimulation`) plus the dormant
   `RegisterSource` are the **complete** set of paths into the journal. Verified for `main` today by grep;
   **not** verified for the build that wrote the contamination.
3. **[A]** That `bet_stats_rollup.json.prerepair` cannot serve as a clean pre-contamination baseline. The
   reasoning is that its sibling checkpoint holds a game clock of `2009-05-24 13:54:40` [V: INC-003],
   already past both bands — so the rollup beside it has already counted them. Sound, but the rollup file
   itself carries no clock and this has not been measured. **Now partly moot**: §A.0.1 established there is
   no pre-contamination rollup in *any* archive, so this file's status changes nothing either way.
   ~~**[A]** That the 2026-08-20 archive is a faithful copy of the live world.~~ **RESOLVED → [V]**: the
   rollup and the concatenated journal have identical md5 sums (§A.0.2).
4. ~~**[A]** That the live rollup's `IsComplete: true` is *correct*.~~ **FALSIFIED, 2026-08-22 → this is
   now finding A-F3.** The rollup began counting inside the group stamped `2009-04-02T12:17:57.1445663Z`
   and is short by ≥50,000 records while declaring completeness (§A.4.3, §A.6.3). `SeededAtUtc: null`
   independently proves the seeding path never ran on this file [V: `:79-80` sets it unconditionally].
5. **[A]** That the contaminated band's records are the *only* contaminated ones inside the retained
   window. This is R2 restricted to the part of history that still exists, and it is exactly as
   undecidable there as everywhere else until measured.
