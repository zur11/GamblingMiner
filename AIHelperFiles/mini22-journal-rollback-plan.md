# Mini-Plan 22 — The journal rollback: at boot, for every scene, and only as large as the tail it removes

**Series note:** twenty-second of the *mini-plan* series. **Persistence work, measured with runs.** It takes the
roadmap objective opened by mini-plan 21 E, *"The first DiceGame entry after launch spends ~1.3 s rolling the
journal back"*, and a correctness question that reading the code raised while specifying it.

**Status:** ✅ **COMPLETE 2026-10-11** (close-out §6), merged from `mini22-journal-rollback`. The journal is rolled
back **at boot** (no scene can read discarded bets any more) and only as far as its tail: **51.9 ms**, against
1,319–1,829 ms, with a cross-check against the old full path reading **0 / 0**. *(Specified 2026-10-11.)*

---

## 1. What the code does today

**Pattern 2's promise:** a restart reverts the whole world to the last mined block: clock, balances, **and the bet
journal**. Three facts about how the journal half is kept today, read from the code:

1. **Boot does not roll the journal back.** `BlockSessionCheckpointService.ApplyCheckpointToServices` restores the
   clock and the stats rollup (`ApplyRollupSnapshot`), and `UserStatsService._Ready` reads **nothing** from the
   journal when a rollup file exists (mini-plan 03 D1, stage 1). The journal's rollback happens **only on the first
   DiceGame entry of a process**: `RestoreLegacyCheckpointIfNeeded` → `RestoreCheckpointClockAndHistoryOnly` →
   `UserStatsService.RollbackHistoryToUtc`, guarded once per process by `_checkpointRestoreSpentThisSession`.
2. **So between boot and that entry, the journal holds bets the world discarded.** Anything that reads the journal
   before DiceGame sees them: `BetsHistoryExplorer` loads it in full (`EnsureFullHistoryLoaded`). **This may be the
   10,000-bet mismatch of mini-plan 20 round 4.** The explorer read `records=200100` on one entry and `136958` on the
   next, over the same date range, and a DiceGame visit between the two would have rolled ~63,000 uncommitted bets
   back. It is a hypothesis until Part A checks it.
3. **The rollback costs ~1.3–1.8 s because of its shape.** `EnsureAllChunksLoaded` parses every retained segment
   (~200,000 records), `RollbackToUtc` removes the records after the checkpoint in memory, and
   `RebuildJournalFromCurrentState` **rewrites the whole journal from memory** (~20 segments, ~26 MB). That holds even
   when it removes a handful of bets, or none. The code says why it must load everything: it rewrites from memory,
   so a partial load would erase the older history. **That constraint belongs to the rewrite-everything design, not
   to the rollback.**

## 2. The work

### Part A — measure, on today's code (an instrument, no behaviour change)

A DEBUG-only `[JournalRollback]` line from `RollbackHistoryToUtc`, printed with `GD.Print` to the **Godot editor's
Output panel**. It gives:
- load ms and records loaded;
- records and deposits removed, and in which segments they sit (first and last segment touched);
- rewrite ms, files written and bytes;
- the journal's last timestamp against the checkpoint.

A second DEBUG line at **boot**, from `UserStatsService` after the checkpoint restore, counts nothing (boot must stay
read-free). It states whether a checkpoint exists and its instant, so the run can be matched against the journal.

**Protocol (the developer runs it; Claude reads the files):**
1. With the world as it is, start the autobet for ~30 s at 9000X so bets accumulate **after** the last mined block,
   then stop it. Note the game time (StatusBar).
2. **Close the game. Before reopening, Claude counts** from the journal on disk how many bets lie after the
   checkpoint's instant (`user://block_session_checkpoint.json`, read by Claude).
3. Reopen. Go **Main Menu → Calendar → Bets History Explorer first**, without entering DiceGame. Note the explorer's
   total and copy its `[ExplorerPerf]` line if it plays.
4. Then enter DiceGame (the rollback runs) and copy the `[JournalRollback]` and `[SceneEntry]` lines.
5. Back to the explorer: note its total again.

**Predictions (registered before the run):**
- **P-A1:** the rollback's time splits roughly two-thirds load and one-third rewrite. The in-memory removal is
  negligible.
- **P-A2:** the records removed are **all in the last one or two segments**, a small share of the journal
  (thousands, not tens of thousands, after a ~30 s run).
- **P-A3 (the correctness question):** in step 3 the explorer counts the uncommitted bets; in step 5 it does not. The
  difference equals Claude's count from step 2. **If it holds, mini-plan 20's mismatch is explained, and the
  rollback's place (DiceGame, not boot) is a defect, not only a cost.**

#### Part A — results (developer's run 2026-10-11; disk read by Claude between steps 3 and 5)

The autobet ran at 9000X and stopped at game 2012-02-06 20:38:51 local (01:38:51 UTC). The checkpoint's journal
boundary is **2012-02-06 23:14:35.7365347 UTC** (`[BootCheckpoint]`, and the checkpoint file read directly).

| | disk (Claude, before the rollback) | `[ExplorerLoad]` before DiceGame | after DiceGame |
|---|---|---|---|
| records | 209,160 in 21 files | **209,160** | **200,000** |
| after the checkpoint / after the present | **8,570**, all in the last file | **8,570** | **0** |

`[JournalRollback]`: removed **8,570 records + 0 deposits** from index 200,590 (**a contiguous tail**), in 13.7 ms in
memory; **rewrote the journal in 829.9 ms: 20 files, 25,449 KB**. Its load read 0.0 ms only because the explorer had
already loaded every segment. Mini-plan 21's runs, which entered DiceGame first, put the whole rollback at
1,319–1,829 ms, so the load is ~490 ms. `[SceneEntry]`: `restore:rollbackHistory` **852 ms** of a 1,233 ms entry.

| prediction | verdict |
|---|---|
| **P-A1** ⅔ load, ⅓ rewrite | ❌ **refuted, the reverse:** the rewrite is ~830 ms (~⅔), the load ~490 ms (~⅓), the removal 14 ms |
| **P-A2** the removed records are a small tail in the last segments | ✅ 8,570, one contiguous suffix, entirely in the last segment |
| **P-A3** the explorer counts the uncommitted bets before DiceGame, not after | ✅ **8,570 → 0**, matching the disk count exactly. **The rollback's place is a defect, not only a cost**, and it explains mini-plan 20's mismatch: an explorer visit before the first DiceGame entry counts the discarded tail |

**Found too:** after the rollback the explorer holds **200,000**, not 200,590. The full rewrite also enforces the
20-segment retention cap and pruned the oldest 590 records. That is retention working as designed (the journal
oscillates between ~190,000 and ~210,000), not a loss caused by the rollback. **A targeted truncation will not prune
there**, so the journal can sit one partial segment over the cap until the next rotation enforces it. That is the
same state the journal is in between every rotation.

**Consequence for C:** the tail is contiguous, so rewriting only the boundary segment is valid on this journal. The
expected cost is about one segment's read and write (~1/20 of today's), **~40–50 ms**, against ~1.3 s.

#### Parts C + B — built (2026-10-11)

**C, `BetHistoryRepository.RollbackToUtc`, new body.** It flushes what the session owes, then walks the segments
newest first: a segment entirely after the checkpoint is deleted, a mixed one is rewritten atomically
(`WriteSegmentAtomically`: `.tmp`, flush to the device, rename), and the first segment with nothing to drop stops the
walk, so it and every older segment are neither read nor written. Unparsable lines are kept and counted. Whatever is in
memory is trimmed by the same rule, without a second write. `WorldWriteGuard.RefuseWrite` comes first, and a failure is
loud on both panels, leaving newer segments cut and older ones intact. **No load is needed any more**, so the old
"this MUST load everything" comment in `UserStatsService` is rewritten to say why that rule belonged to the old design.

**The cross-check, DEBUG-only** (`VerifyRollbackAgainstFullPath`). Before truncating, it reads every (type, id) at or
before the checkpoint, which is the old full path's rule, computed independently. Afterwards it reads every (type, id)
left, and reports missing and extra, timed apart from the truncation. Both must be 0. The release build compiles
without it (checked with `-c ExportRelease`).

**B, `BlockSessionCheckpointService.ApplyCheckpointToServices`** calls `RollbackHistoryToUtc` right after the rollup
restore, at boot, like the clock. `UserStatsService` stays #2, so INC-004's recovery order is untouched. The rollback
runs once per process (`_historyRolledBackThisProcess`), so DiceGame's first-entry call becomes a no-op and prints
that it was one. It is left in place until a run shows that.

**The guard is meaningful at boot, checked rather than assumed:** the world is loaded before #13 restores the
checkpoint. The Output panel prints `[Governance] … (restored with the world)` before `[BootCheckpoint]`, so a failed
load has already set `WorldLoadFailed` when the rollback asks `RefuseWrite`.

**Predictions for the run:**
- **P-C1:** the boot rollback reads **2 segments**, rewrites 1, deletes 0 (for a tail inside the last segment), in
  **< 150 ms**. The first DiceGame entry falls to an ordinary entry's cost, with `restore:rollbackHistory` ≈ 0 and the
  no-op line printed.
- **P-C2:** cross-check **missing 0, extra 0**.
- **P-C3:** the explorer opened first after the restart reports `afterPresent=0`.
- The bets dropped equal Claude's count from the disk, taken before the game is reopened.

### Part B — the rollback moves to boot, where Pattern 2 says it happens

The checkpoint restore already runs at boot. The journal's rollback joins it, so **no scene can ever read
uncommitted bets.** DiceGame's once-per-process call becomes a no-op guard. It is not deleted until Part B's run
shows nothing reaches it any more (Convention 12: re-derive the cases from the code, not from the list written when
it was added).

**The ordering constraint, named before the change:** `UserStatsService` (#2) loads before
`BlockSessionCheckpointService` (#13), and **INC-004's recovery path depends on that order** (Pattern 5). So the
rollback is called **by** the checkpoint service during its restore, not by `UserStatsService` at its own `_Ready`,
and the autoload order is untouched.

### Part C — the rollback is only as large as the tail it removes

**Moving a 1.3 s cost to boot is only acceptable if the cost goes.** A targeted truncation:

1. Read the segments **from the newest backwards** until one whose first record is at or before the checkpoint.
2. Rewrite **that** segment without its post-checkpoint records, and delete the newer ones.
3. **Never load the older segments**, since they are not rewritten.
4. If the journal's last record is already at or before the checkpoint, **do nothing at all**. That is the case after
   every stop-on-block, and after any restart with no bet since the last block.

**The constraint the old comment protects, kept:** the rewrite must not erase history it did not load. Here it only
rewrites what it read. **Written to INC-001's and Ch. 40's rules:** atomic writes (`.tmp` → rename), loud failure,
never persisting over a failed load, `WorldWriteGuard.RefuseWrite` first, and the rotation and segment-numbering
invariants of mini-plans 12 and 17 respected. Those must be re-read from `BetHistoryRepository` before the change,
not assumed.

**Older journals hold slightly out-of-order stamps** (mini-plan 08 P3 measured 114, before mini-plan 21 made stamps
exact), so "the tail" in **write order** and "records after the checkpoint" can differ by a few records near a
segment boundary. **Verification, built before the fast path** (mini-plan 17's discipline): a DEBUG cross-check runs
the old full path on a copy of the in-memory state and compares the surviving record ids. **Mismatches must be 0.**

**Predictions:**
- **P-C1:** the first DiceGame entry falls from ~1.5–2.3 s to the cost of an ordinary entry (~150–200 ms). Boot grows
  by the targeted rollback only: **< 150 ms** after a ~30 s run, **~0** when nothing is after the checkpoint.
- **P-C2:** the cross-check reports **0 mismatches**.
- **P-C3:** P-A3's experiment repeated: the explorer opened first after a restart shows **no** uncommitted bet.

### Part D — close

Roadmap and IMPLEMENTATION_STATUS updated. The "explorer held 10,000 more bets" objective is closed if P-A3 and P-C3
hold, and re-scoped if not. Pattern 2's sentence in CLAUDE.md is checked against the new boot order, and edited
only if it is now false.

## 3. What this plan does not do

- **It does not change what the journal records, or its format.** No wipe.
- **It does not touch the rollup.** The checkpoint already restores it, and INC-004's rules govern it.
- **Not the remaining ~150 ms of an ordinary entry** (`recentBets` ~60 ms). That is a separate, smaller question.

## 4. Order

1. **A** — measure. P-A3 decides whether B is a fix or only an optimisation.
2. **C before B in the code, B before C in the run.** The targeted truncation and its cross-check are built first,
   because moving the full rewrite to boot would make every launch slower. Then the call moves to boot.
3. **D** — close.

## 5. Invariants (every commit)

- **I1** `dotnet build` clean; locale detector at its baseline, re-run.
- **I2** No headless launch; the developer runs; Claude reads `user://` itself and names the panel.
- **I3** Every journal write is atomic, guarded, and loud on failure (Ch. 40, INC-001, INC-004).
- **I4** The autoload order is unchanged (Pattern 5).
- **I5** Times in protocols are given in local time, and say so.

## 6. Close-out (2026-10-11)

**Final run** (developer's run; disk counted by Claude after the game was closed and before it was reopened). The
journal boundary was 2012-02-10 06:03:50.4984384 UTC. On disk: 201,686 bets in 21 files, **179 after the boundary**,
all in the last file as a contiguous tail, 0 deposits after it, no stray `.tmp`.

| | prediction | result |
|---|---|---|
| bets dropped at boot | 179 (the disk count) | **179** + 0 deposits |
| segments | read 2, rewrite 1, delete 0 | **read 2, rewrote 1, deleted 0** |
| rollback time | < 150 ms | **51.9 ms** (was 1,319–1,829 ms) |
| **P-C2** cross-check vs the full path | missing 0, extra 0 | **missing 0, extra 0 (OK)** |
| **P-C3** explorer opened first after the restart | `afterPresent=0` | **`records=201507 afterPresent=0`** (201,686 − 179) |
| DiceGame's first-entry call | a no-op | **"already rolled back in this process; this call is a no-op"** |
| `restore:rollbackHistory` in `[SceneEntry]` | ≈ 0 | **0.1 ms** |
| **P-C1** first DiceGame entry | an ordinary entry's cost | **419 ms** (was 1,700–2,300). The rest is the process's cold first entry (load 38 ms, children 108 ms, first frame 102 ms), not the rollback |

**What the plan found and fixed.**

1. **A correctness defect, not only a cost.** Boot restored the clock and every balance to the last block but left
   the journal alone until the first DiceGame entry. Every reader before that saw bets the world had discarded:
   8,570 in Part A's run, visible in `BetsHistoryExplorer`. **That was mini-plan 20's "10,000 more bets than disk"**,
   an explorer visit before the first DiceGame entry of the process. The journal is now rolled back at boot, called
   by the checkpoint service right after the rollup, so Pattern 2's sentence, *"an app restart reverts the whole
   world … performed at startup by `ApplyCheckpointToServices()`"*, is now true of the journal as well. CLAUDE.md is
   unchanged, because the sentence was already right about intent and is now right about fact.
2. **The cost was the design, not the work.** The rollback rewrote ~25 MB to drop bets from one file, and a rule
   written to protect that design ("this MUST load everything") read like a rule of the rollback. The new one rewrites
   only the segments it reads, newest first, and stops at the first segment with nothing to drop.

**Close-out decisions.**
- **The DEBUG cross-check is switched OFF** (`VerifyRollbackAgainstFullPath = false`, declared under `#if DEBUG`). It
  did its job (0 / 0), and it cost ~1.8 s at every editor boot. The code stays, with a note to switch it back on
  whenever this method or the segment layout changes.
- **DiceGame's call is kept as a guarded no-op**, not deleted. The run shows it reached and harmless. It also covers
  the legacy branch where no node state exists, and removing it buys nothing measurable.
- **The instruments stay** (`[BootCheckpoint]`, `[ExplorerLoad]`, `[JournalRollback]`, DEBUG-only).

**Lessons.**
- **A rule written to protect a design reads like a rule of the problem.** "Must load everything" was true of a
  rewrite-from-memory and false of a rollback. Ask what a constraint protects before inheriting it.
- **Where a restore happens is part of its correctness.** The clock had been moved to boot "so it applies regardless
  of which scene the app opens into". Its comment stated the rule, and the journal, restored beside it, never got the
  same treatment.
- **Counting the disk between two steps of the protocol** gave an independent number to compare three instruments
  against, and all three agreed to the record.
