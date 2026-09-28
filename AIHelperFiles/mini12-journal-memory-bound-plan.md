# Mini-Plan 12 — The journal's memory: bounding a store that is capped only on disk

**Series note:** twelfth entry of the *mini-plan* series, following
`mini11-frame-capacity-bots-and-later-eras-plan.md`, whose C2 run found this fault while measuring something
else. Evidence and the general lessons: `ProjectDesignManual.md` **§40.11**.

**Status:** ✅ **DONE 2026-09-28**, merged to `main` from `mini12-journal-memory-bound`. **Read §9 first** — it
is the close-out. The trail below is the record of how it ran. *(Was: steps 1–4 done* — the live cap and the stall
counters (§5.1), the long run (§7 step 2: memory 6.77 GB → 1.51 GB, zero stalls, cost per bet flat over 41 M
bets), the `JournalAdd` split (§7 step 3: 5.09 µs, 29.7% of a bet) and **D-12.1, the slim journal line**, built
(§8), then the delta run measured it.*)

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

---

## 7. Results

### Step 2 — the repeat of C2 (2026-09-24, 83 real minutes, fresh world)

Player alone, flat 0.001, defaults, 9000X, on a world reset to the bootstrap date first. **2009-03-21 →
2010-07-18, chain height 0 → 807, 41.1 M bets, 429 reports.**

| | C2 (before the bound) | this run |
|---|---|---|
| bets | 26.4 M | **41.1 M** |
| process memory | 6.77 GB | **1.51 GB** |
| frozen time | 791 s in 19 stalls | **none — 0 stalls in 429 reports** |
| cost per bet, start → end | 16 → 145 µs | **16.9 µs → 16.7 µs at 29 M** |
| game-days per real minute | 4.78 | **5.82** |

- **P1 memory under 1.5 GB — met in substance, formally missed:** 1.51 GB, on a run with 55% more bets.
- **P3 zero stalls — held.** The counters built in step 1 are what made that a measurement rather than an
  impression.
- **P5 trimming is free — held.** A bet cost 15.38 µs in the same session's Bet cost leg, against 15.68 µs in
  mini-plan 11's verification *before* the cap existed.
- **P2 cost per bet within ±20% — refuted as stated, but not by growth.** It is flat against cumulative bets
  (16.9 µs at 0.1 M, 16.7 µs at 29 M) and noisy within the run (16–70 µs by window). The noise is machine-level:
  in those windows the time OUTSIDE the simulation doubles or triples too, it happens in windows where no block
  was mined, and it comes in episodes with no drift across buckets. The session as a whole ran ~50% slower
  outside the sim than mini-plan 11's, with 1 GB of free RAM on the machine.
- **P4 "1.5× further" — refuted, and it was never reachable.** At 9000X the clock advances at most 6.25
  game-days a real minute, so the ceiling against C2 was 1.31×. The run made 5.82, **93% of the clock's
  ceiling**, against C2's 76%. *The prediction was registered without checking its own arithmetic — the same
  mistake the plan catches elsewhere, made while writing the plan that catches it.*

**The restore hazard, verified rather than argued.** After a restart from DiceGame: the journal was rewritten
from the live set to **15 segments / 147,370 records**, and the checkpoint boundary lands on the newest
surviving record to the tick (2010-08-13T00:45:34.435). Nothing later survived. Worth stating: without the cap
that same restore would have rewritten **41 million** records to disk. The lifetime rollup read 43.3 M bets and
`IsComplete: true` throughout, so the two-layer design held exactly as intended — the pruned journal lost
history, the rollup did not.

### Step 3 — the split (2026-09-28, 36 reports, 179,716 bets)

| segment | µs per bet | share |
|---|---|---|
| **JournalAdd** (`BetHistory.Add` + amortised flush) | **5.09** | **29.7%** |
| NonceAttempt (the real PoW hash) | 6.28 | 36.7% |
| RegisterBet (rollup + session stats) | 1.42 | 8.3% |
| ExecuteNext | 1.44 | 8.4% |
| everything else | 2.87 | 16.9% |
| **total** | **17.10** | |

**P6 refuted:** the journal is **78%** of the old combined step, not under half. §4 C's rule — 3 µs or more
ceases to be noise — therefore fires on 5.09 µs, and it chose the slim JSON form.

*Protocol note, recorded because it cost a run:* the first attempt measured the pre-split build. The app had
been open since before that commit, and Godot only picks up a new assembly on a fresh launch. **The trace file
settles it without anyone watching a panel — the header carries the column set the running build writes.**

## 8. D-12.1 — the slim journal line (2026-09-28)

**Decided by §4 C's registered rule, and built the same day.** One flat JSON line per entry: short keys, no
wrapper and no null sibling, money as **whole satoshis**, the multiplier ×10,000, and every field at its
default omitted. `JournalLine` in `BetHistoryRepository` holds the canonical example and the units; CLAUDE.md's
JSON rule and `SERVICES.md` both carry the exception now, because an audit that reads `"a":230000` as SC is off
by 10⁸.

**It stays JSON deliberately.** Binary would save perhaps another 2× and would end the `node`/`awk` audits that
every playtest in this project has relied on. §4 C keeps it available if the slim form's remaining cost is still
≥ 3 µs.

**World format 7 → 8**, because the loader cannot read the old shape and, per project policy, is not taught to.

**What the measurement must answer, registered before the run:**
- the before-figure is **`JournalAdd` 5.09 µs of a 17.10 µs bet (29.7%)**, in the same session shape;
- **a bet line falls from ~285 bytes to ~150**, which halves the journal's write volume and doubles the bets a
  segment file holds in bytes terms;
- **the part of `JournalAdd` that is NOT serialization** — the duplicate-guard hash of a 32-character id and two
  list appends — is untouched by this change and sets the floor the new figure cannot go below. Nothing measured
  it separately, so the delta is what will say how large it was.

### The delta, and D-12.2 — binary is rejected on the measurement (2026-09-28)

| | before (fat line) | after (slim line) |
|---|---|---|
| `JournalAdd` | 5.09 µs | **4.20 µs** (−17.5%) |
| whole bet | 17.10 µs | **16.07 µs** (−6%) |
| bytes per journal line | 285.3 | **162.7** (−43%) |

**Correctness, checked over the whole journal rather than a sample:** 183,055 records, **zero**
balance-continuity breaks — `previous balance + net == balance after` holds exactly, in integer satoshis, for
every consecutive pair. The decimal→satoshi round-trip loses nothing. The multiplier decodes to 1.9804, and
defaults really are absent: a winning LOW bet carries neither `"o"` nor `"h"`.

**D-12.2 — the binary format is rejected, and the rule that would have allowed it is superseded by its own
measurement.** §4 C said binary could be considered if the slim form still cost ≥ 3 µs; it costs 4.20, so by the
letter it qualifies. But the delta measured the thing the threshold was only a proxy for: **43% fewer bytes
bought 0.89 µs.** Binary's further ~60% therefore extrapolates to **~1 µs on a 16 µs bet**, in exchange for
ending the `node`/`awk` audits — the same audits that just verified 183,055 records in one command.

> **The lesson to carry, because the rule was mine and it was the wrong shape:** a threshold on a total is a
> proxy for a *derivative*. "Is the step big?" is not the question; "how much does the step move when the input
> moves?" is. Once one build can measure the derivative, the threshold stops being evidence.

**What is left, and where it goes.** The remaining 4.20 µs is the duplicate guard's hash of a **32-character
GUID string**, two list appends, and the write itself — none of which a line format can touch. Replacing that id
with a compact counter shrinks the line *and* the hashing, which is a bigger lever than the format had. It
changes `BetRecord`, so it is **mini-plan 13**, not a late addition here.

---

## 9. Close-out (2026-09-28)

**The fault is fixed, and the plan's own instrument now sees the failure mode it was blind to.**

- **Memory:** 6.77 GB → **1.51 GB**, on a run with 55% more bets (41.1 M). The live set is bounded by the same
  window the files keep.
- **Freezes:** 791 s in 19 stalls → **none**, and the profiler counts stalls now instead of discarding them, so
  "none" is a measurement rather than an impression.
- **Cost per bet:** flat across 41 M bets (16.9 → 16.7 µs), where C2 had reached 145 µs by 26 M. With the slim
  line it is **16.07 µs**, of which the biggest single part is now the real proof-of-work hash (6.13 µs, 38%).
- **Journal on disk:** 285 → 163 bytes a line, ~43% less written at bet rate, with the audits intact.
- **The restore hazard was verified, not argued:** after a restart the journal was rewritten from the live set
  (15 segments, 147,370 records) with the checkpoint boundary on the newest surviving record, to the tick.

**Predictions: 2 held, 4 refuted.** P3 (zero stalls) and P5 (trimming is free) held. P1 missed by 1% (1.51 GB
against "under 1.5"). P2, P4 and P6 were refuted — and two of them were badly formed rather than merely wrong:

1. **P4 asked for something arithmetically impossible.** At 9000X the clock cannot advance more than 6.25
   game-days a real minute, so "1.5× further than C2" was unreachable before the run started. *Check a
   prediction against its own ceiling while registering it.*
2. **P2 measured the machine as much as the code.** Cost per bet was flat against cumulative bets — which is
   what the plan was actually testing — but noisy within the run, because the session itself ran ~50% slower
   outside the simulation than mini-plan 11's did. *A per-bet figure is only comparable inside one session;
   across sessions, compare shares.*
3. **P6 was simply wrong**, and usefully so: the journal was 78% of the step it shared with the statistics, not
   under half, which is what sent the format change ahead.

**One protocol lesson, which cost a run.** The first split attempt measured the pre-split build: the app had
stayed open since before that commit, and Godot only loads a new assembly on a fresh launch. **The check is the
trace header** — it carries the column set the running build writes, so the file settles the question without
anyone reading a panel mid-run. Asking the developer to spot a line in the Output panel while reports scroll at
9000X is not a check.
