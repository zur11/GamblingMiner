# Mini-Plan 22 — The journal rollback: at boot, for every scene, and only as large as the tail it removes

**Series note:** twenty-second of the *mini-plan* series. **Persistence work, measured with runs.** It takes the
roadmap objective opened by mini-plan 21 E, *"The first DiceGame entry after launch spends ~1.3 s rolling the
journal back"*, and a correctness question that reading the code raised while specifying it.

**Status:** 📝 **SPECIFIED 2026-10-11**, branch `mini22-journal-rollback` (proposed). Nothing built.
**Next:** Part A, measure inside the rollback on today's code.

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

## 6. Close-out

*(Written at close-out.)*
