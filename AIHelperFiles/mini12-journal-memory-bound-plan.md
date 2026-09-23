# Mini-Plan 12 — The journal's memory: bounding a store that is capped only on disk

**Series note:** twelfth entry of the *mini-plan* series, following
`mini11-frame-capacity-bots-and-later-eras-plan.md`, whose C2 run found this fault while measuring something
else. Evidence and the general lessons: `ProjectDesignManual.md` **§40.11**.

**Status:** 🚧 **IN PROGRESS** on `mini12-journal-memory-bound`. **§5 step 1 done** (2026-09-23): the live cap
(A) and the stall counters (B), one build. **Next: §5 step 2** — the repeat of C2, on a world reset to the start
date first (the developer's call, 2026-09-23; done by making `world_format_version.txt` disagree with
`WorldFormatVersion`, which runs the game's own wipe at the next launch — no code change, the sanctioned delete
list, the exempt identity files kept).

**The fault, in one line:** `BetHistoryRepository` caps the bet journal **on disk** (20 segments × 10,000
entries, ~57 MB) and **never trims the same records in memory**, so a session's footprint grows with every bet
until the machine pages — 6.77 GB after 26.4 M bets, 13 of 65 minutes frozen, cost per bet 16 µs → 145 µs.

**Three questions:**

- **A — where is the boundary?** Which readers genuinely need history beyond a live window, and which only
  appeared to because everything happened to be in memory.
- **B — does the bound hold?** A repeat of C2 with the bound in place: flat cost per bet, flat memory, no stalls.
- **C — is a compact record worth building?** Mini-plan 11 left the JSON-versus-binary question open and named
  the measurement that decides it. It is cheap to take here, and the answer changes with A: a bounded store
  needs a smaller record far less than an unbounded one did.

---

## 1. What the code says, before changing anything (read 2026-09-22)

**The store.** `BetHistoryRepository` holds `_records` (`List<BetRecord>`), `_deposits`, `_recordIds`
(`HashSet<string>`, the INC-002 duplicate guard) and `_pendingJournalEntries` (the unflushed tail). `Add`
appends to the first two and to the guard. **Nothing trims them**: only `RollbackToUtc` (checkpoint restore)
and `ClearAll` (pre-genesis) remove anything, and neither runs during play. `EnforceRetentionCap` deletes
**files**.

**Reads are already bounded at boot, writes are not bounded at all in RAM.** Mini-plan 03's rollup made
`UserStatsService` authoritative for lifetime figures without scanning the journal — its own comment says
*"retention bounded what was WRITTEN; this is what finally bounds what is READ"*. The remaining hole is the
LIVE session: every bet a run settles stays in memory until the process exits.

**The loader already knows how to hold less.** `LoadLatestChunkOnly` loads the newest segment and **resets**
in-memory state; `EnsureAllChunksLoaded` loads the retained window. `GetRecentBets` already relies on the first
(a chunk holds 10,000; DiceGame's list wants ~100). So a bounded live window is not a new concept here — it is
the concept the loader already has, applied to the writer.

**Every consumer of the in-memory records, as they stand:**

| Consumer | What it reads | If the live window is bounded |
|---|---|---|
| `GetRecentBets(max)` (DiceGame's list, on entry and node switch) | newest `max` (~100–260) | unaffected — it already loads the newest segment only |
| `BetsHistoryExplorer` | `EnsureAllChunksLoaded` then all records | unaffected — it loads the retained window from disk on demand, which is itself capped at 20 segments |
| `RebuildStatsFromLoadedHistory` | all loaded records + deposits | **first-run seeding only** (mini-plan 08 D1); already says a retained window is a floor, not a lifetime figure |
| `Rollup.IsComplete = Records.Count == 0` | the loaded count, at first seeding | **must be re-read**: with a bound, "empty" and "trimmed" must stay distinguishable (INC-004's rule: coverage defaults pessimistic) |
| `BuildStatsUpToUtc`, `GetBalanceAtOrBeforeUtc`, `BuildSummaries`, `GetBetsForCalendarDay` | loaded records | **the open question** — §2 A settles each one |
| `RollbackToUtc` → `RebuildJournalFromCurrentState` | writes the in-memory set back over the files | **changes what survives a restore** — §2's hazard |
| `TryGetOldestRecordTimestampUtc`, `GetLatestKnownBalance`, `HasPrunedHistory` | ends of the list | need re-checking against a trimmed head |

**The hazard to register now, before any code.** `RollbackToUtc` deletes every journal file and rewrites them
from the in-memory set. With a bounded set, a restore would rewrite **only the window** — the on-disk history
would shrink to it. Retention caps the file count anyway, so the steady state is the same, but the boundary
moves and it must be verified rather than assumed.

---

## 2. The work

### A — the boundary (design, with the choice registered before building)

**Candidates:**

1. **Bound the live set to the disk window** — trim `_records`/`_recordIds` at segment rotation, keeping the
   newest `MaxRetainedJournalChunks × MaxJournalEntriesPerChunkFile` records. Memory then mirrors what disk
   keeps: ~200,000 records, ~51 MB by C2's 257 bytes a record.
2. **Bound it to a smaller live window** (one or two segments) and let every consumer that wants more call
   `EnsureAllChunksLoaded`, exactly as the explorer does. Smallest footprint; more consumers touched.
3. **Make the record cheaper and keep everything** (struct, satoshi integers, no GUID string). Rejected as the
   primary fix, and the reason is the lesson: *a constant factor on an unbounded quantity is still unbounded*.
   It only moves the freeze later. It stays alive as C's separate question.

**Registered choice: (1).** It is the smallest change that makes the footprint constant, it gives memory and
disk the same boundary — which is what §40.11 says was missing — and it leaves every "load more" path already
in the codebase untouched. (2) is the fallback if (1)'s 51 MB proves to matter, which nothing suggests.

**The duplicate guard changes meaning, and that must be stated in the code, not just here.** `_recordIds` can
only refuse a duplicate it still remembers. Bounded to the window, the guard covers re-registration **within
the window** rather than for all time. INC-002's actual shapes (a load/rebuild re-adding what was already
there, a live double-register of the same settled bet) all occur within a few thousand bets, so the guard keeps
its purpose — but the promise narrows and the comment must say so.

### B — the instrument that could not see the freeze

`FrameCostProfiler` drops any period over `DiscontinuityMs` as "not a frame", which is why 791 seconds of
frozen game left its percentiles looking healthy. **Count what is dropped instead of discarding it silently:**
a stall counter, their total and the worst one, in the report line and as trace columns. It is a handful of
fields, and without it B's verification would again be a human watching a window.

### C — the compact-record question, decided by measurement, not by preference

Mini-plan 11 §"format" left this open and named the measurement: **split `RegisterBet` (6.25 µs, 40% of a
player bet at the time) into journal serialization and statistics bookkeeping.** One new segment in
`BetCostProfiler`, one short run.

---

## 3. Predictions, registered before any data

- **P1 — memory is flat.** With A in place, a repeat of C2 holds process memory under **1.5 GB** for the whole
  run, and the curve is flat after the first minutes rather than rising.
- **P2 — cost per bet is flat.** It stays within **±20%** of its opening value (16–20 µs) for the whole run,
  against 145 µs at the end of C2's.
- **P3 — the stalls disappear.** **Zero** periods over one second, against C2's 19 totalling 791 s.
- **P4 — the run gets further.** In the same wall clock, it reaches at least **1.5×** the game time C2 did
  (C2: ~10 game-months in 65 minutes, with 13 of those minutes frozen).
- **P5 — trimming is free.** It runs once per 10,000 bets, so `RegisterBet` is unchanged within noise.
- **P6 — serialization is not the bulk of `RegisterBet`.** Under 50% of it.

## 4. Decision rules, registered before the data

- **A is done when** every consumer in §1's table has been re-read against the bound and either needs no change
  or has one, and when the `RollbackToUtc` hazard has been verified on a real restore rather than argued.
- **B (the bound) holds if P1–P4 all hold.** If memory is flat but cost per bet is not, the cause is elsewhere
  and is named before anything else is changed — the same rule mini-plan 11 followed when the cap, not the
  frame, turned out to be binding.
- **C — the compact record:**
  - **serialization ≥ 3 µs per bet** ⇒ it pays for itself, and the slimmer-JSON form goes first (short keys,
    integer satoshis, no wrapper): most of the byte saving, and it keeps the journal readable by `node`/`awk`,
    which is how every playtest audit in this project has been done;
  - **under 3 µs** ⇒ **deferred, with the number written down.** Bounded memory and capped files leave the
    format as a disk-write-volume question only, which is a development-speed concern, not a player's;
  - **binary** is considered only if the slim form lands and its remaining serialization cost is still ≥ 3 µs.
    It costs an export tool, because it would otherwise end the direct audits.

## 5. Order

1. **Build A + B** (the bound, the guard's narrowed promise in its comment, the stall counters) → one build.
2. **Run the repeat of C2** (~65 minutes unattended, same settings) → P1–P5.
3. **Run the `RegisterBet` split** (~1 minute, Bet cost armed) → P6, and C's decision.
4. **Decide C** by §4, build it only if the rule says so.
5. **Close-out**, and re-read §40.11's rules against what was actually built.

## 6. Out of scope

- **The chain's whole-file rewrite** (`PersistStateToDisk` writing every block at every block). Measured mild
  in C2 — 24 ms at height 376, 33 ms at 747 — and recorded in `PRIVATE_ROADMAP.md` as its own item.
- **A budget that adapts to the machine.** Every figure this project has is from one PC; a Basic Mode item.
- **The bet journal's purpose or contents.** This plan bounds where the records live, and changes nothing about
  which records exist.
