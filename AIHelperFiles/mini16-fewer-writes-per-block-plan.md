# Mini-Plan 16 — Fewer writes per block, and a success criterion that reproduces

**Series note:** sixteenth of the *mini-plan* series, following `mini15-fail-closed-and-readouts-that-strobe-plan.md`,
whose close-out recommended this over T4.1 and whose refuted predictions dictate how this plan states its own.

**Status:** 🔄 **IN PROGRESS** on branch `mini16-fewer-writes-per-block`. **A built 2026-09-30** (§7).
**Run 1 done 2026-10-01** (§8): **P1 and P3 refuted, P2 confirmed.** Only **2** writes land inside the block
bracket; the real cost is **48.8 writes / 136.8 ms per block = 2.0% of wall-clock time**, almost all of it between
blocks at bet rate. The journal dominates (108 ms/block, not waste); the two balance files are **26.9 writes/block
of files nothing ever reads**. **Next: B1** — commit those two at the block instead of on a 0.5 s timer. Part D is
dropped by its own gate; part C is demoted.

**Why this and not T4.1.** Mini-plan 14's rule says the UTXO replay becomes the next plan at 40% of a block, and
mini-plan 15 measured it crossing (42.7% at height 674–698). **The rule fired on a shrinking denominator, not a
growing numerator:** the replay *plateaus* at ~4 ms from height 500, while the rest of the block swings
**5.9 → 15.8 ms (2.7×) on the same world inside one run**. Applying the rule mechanically would be Standing
Convention 8's error — reproducing a verdict's arithmetic without its premise. The write path is both the **larger**
term (checkpoint alone **9.46 ms** at height 350–449, against the replay's 3.37 ms) and **the reason every other
per-block figure in this project is unmeasurable**. Fixing it buys measurement as well as time.

---

## 1. What the code says, before changing anything (read 2026-09-30)

**The bytes are trivial and the cost is not.** Every world-state file the block path touches, measured on the live
height-697 world:

| file | bytes |
|---|---|
| `calendar_state.json` | **18** |
| `bankroll_state.json` | 88 |
| `principal_balance_state.json` | 91 |
| `casino_sc_balance_state.json` | 344 |
| `player_bank_account_state.json` | 364 |
| `casino_coin_swap_state.json` | 397 |
| `central_bank_state.json` | 456 |
| `hardware_allocation.json` | 554 |
| `bankroll_program_state.json` | 801 |
| `bet_stats_rollup.json` | 974 (atomic: `.tmp` + rename) |
| `sc_monetary_ledger.json` | 1,412 |
| `casino_client_ledger.json` | 2,292 |
| `block_session_checkpoint.json` | 7,469 |
| `blockchain/state.json` | 59,664 |

**~75 KB per block, costing ~14 ms** (checkpoint 9.46 + snapshot write 4.65, medians at height 350–449). That is
**~5 MB/s** — three orders of magnitude below any SSD. **A cost that low in throughput and that high in latency is
per-write overhead, not volume.** An 18-byte file costs essentially the same as a 7 KB one: open, write, close,
metadata flush, and for the atomic writers a rename on top.

**And the writes are not one per file per block.** Every mutation persists immediately:

- `PrincipalBalanceService` calls `SaveState()` from **four** mutation paths, unthrottled — and the checkpoint's
  bot-session swap calls `SetBalance` twice more (once to swap the player's mirror in, once to restore).
- `CalendarTimeService.PersistCurrentTime()` has **20 call sites** for an **18-byte** file.
- `BankrollProgramService.SaveState()` runs on every `AddRecord` *and* every `ReplaceState` — the checkpoint calls
  `ReplaceState` during a bot session.
- `BankrollStateService` is the one service already throttled (`SaveFlushInterval = 0.5`, mini-plan 08 P1, when its
  unthrottled per-bet write was measured at **66% of a bet**). **That fix was applied to one service and never
  generalized** — which is the shape of this whole plan.

**The pattern already exists in the codebase and is used for exactly one thing.** Mini-plan 14 gave the world
`RequestWorldPersist()` / `FlushWorldIfDirty()`: mark dirty, flush **at most once per frame**. Nothing else uses it.

**⚠ The constraint this plan must not break.** "A block is the only commit to disk" (Pattern 2) governs commit
*timing*. Mini-plan 14 already widened the window by milliseconds and said so. **Coalescing within a frame keeps
that property; deferring past the block does not.** Any change here must still leave the world committed by the
end of the frame that mined the block.

---

## 2. The work

### A — the instrument, first and separately

**Count the writes before changing any of them.** `BlockCostProfiler` already has `NoteSnapshotWrite(bytes, ms)`
for `state.json` alone; generalize it to `NoteStateWrite(path, bytes, ms)` called from **every** world-state
writer — the same 15 sites `WorldWriteGuard.RefuseWrite` already marks, which is a ready-made list of exactly the
writers that matter. Report per block: **write count, distinct files, total bytes, total milliseconds**, plus the
per-path breakdown behind a flag.

This is the whole of the measurement, and it ships before any fix, because **the success criterion of this plan is
a count** (see §3).

### B — coalesce, in the order the instrument ranks them

Mini-plan 14's pattern applied to the writers A shows writing more than once per block: mark dirty, flush at most
once per frame, flush before the checkpoint capture and in `_ExitTree`. **No format change, no version bump.**
`BankrollProgramService` already carries a `StateVersion` (mini-plan 11 D-11.3) that a dirty check can reuse.

### C — skip what has not changed

A writer whose state is identical to what is on disk should write nothing. Cheapest possible version: compare the
serialized payload to the last one written, in memory, and skip on equality. Applies per writer, independent of B.

### D — one file for the money services, **only if A and B leave it worth doing**

Merging the eight small money-service files into one `world_money_state.json` removes seven opens per block. It is
a format change ⇒ `WorldFormatVersion` bump + clean wipe, which this project does by default (Standing
Convention 4). **Explicitly gated on the measurement**: if B and C already bring the count near its floor, D is
churn for its own sake and is dropped, not deferred.

---

## 3. Predictions, registered before any data

Stated as **counts and bytes**, deliberately, because mini-plan 15 demonstrated that the millisecond figures here
vary 2.7× between adjacent samples on one world. A count either reproduces or it is a bug.

- **P1 — a block performs ≥ 20 world-state file writes** across ≤ 15 distinct files (i.e. at least five files are
  written more than once per block).
- **P2 — total bytes per block < 100 KB**, so the implied throughput is **< 10 MB/s**. This is the prediction that
  the cost is per-write overhead rather than volume; if bytes turn out to be large, this plan's premise is wrong
  and B/C/D are the wrong fixes.
- **P3 — `calendar_state.json` (18 bytes) is written more than once per block**, and is the single clearest case
  of cost that is entirely overhead.
- **P4 — B + C reduce writes per block by ≥ 50%** from P1's measured figure.
- **P5 — the VARIANCE falls with the count.** Non-UTXO block cost, measured over matched height buckets, spans
  less than **2×** after B + C, against the 2.7× mini-plan 15 measured. *This is the prediction that matters for
  the project* — it is what makes future per-block figures trustworthy.
- **P6 — per-block milliseconds fall, reported as a distribution and never as a single number.** No threshold is
  attached to it, because mini-plan 15 showed a single-number per-block cost does not reproduce across days.

## 4. Decision rules, registered before the data

- **P2 fails (bytes are the cost) ⇒ stop and re-plan.** Coalescing opens does nothing for a volume problem; the
  answer would be a smaller format, which is a different plan (and one mini-plan 12 already rejected for the
  journal on measurement).
- **P4 fails ⇒ the instrument names the writers that did not coalesce, and each is explained individually.** A
  writer that must write twice per block for a stated reason is a finding, not a failure.
- **P5 fails while P4 holds ⇒ the variance is not the write count**, and the honest conclusion is that per-block
  cost in this project is environmental. Say so, and stop attributing block-cost changes to code.
- **D is judged only after A and B land**, on the measured remaining count. Default is **not** to do it.
- **Any change that moves a commit past the end of the frame that mined the block is out of scope**, whatever it
  saves.

## 5. Order

1. **A — the instrument.** One build. No behaviour change.
2. **Run 1** (~20 minutes at 9000X, Block cost armed): settles P1, P2, P3 and ranks the writers.
3. **B, then C**, in the order the ranking gives. One build each.
4. **Run 2**, matched to run 1 (same speed, overlapping height range): P4, P5, P6.
5. **D or its rejection**, on run 2's numbers.
6. **Close out.**

## 6. Out of scope

- **T4.1 (incremental UTXO).** Measured, bounded at ~4 ms, and its trigger is restated in absolute milliseconds in
  `PRIVATE_ROADMAP.md`. It follows this plan, not the other way round.
- **Asynchronous or background writes.** They would break the "committed by the end of the block's frame"
  property above. Stated as rejected rather than forgotten.
- **A binary state format.** Mini-plan 12 rejected binary for the journal *on measurement*; nothing here suggests
  revisiting it, and P2 is the prediction that would have to fail first.
- **The bot-mined block one-frame clock offset** and **"a budget that adapts to the machine"** — both open, both
  unrelated to write cost.

---

## 7. Part A as built (2026-09-30)

**No behaviour change. One instrument, fifteen call sites, and a cross-check that the set is complete.**

`BlockCostProfiler.NoteStateWrite(path, bytes, startTicks)` joins `NoteSnapshotWrite` as a **counter rather than a
phase**, for the reason mini-plan 13 gave when it made that choice: two phase totals cannot tell you whether one
file was written twice or two files once each.

**Three decisions inside it worth stating:**

1. **The timer brackets the CLOSE, not the `StoreString`.** On Windows the close and its metadata flush are most of
   a small file's cost, so every writer's statement-scoped `using FileAccess file = …;` (which disposes at method
   exit, outside any measurement placed after the write) was converted to a **block-scoped** `using`. Timing the
   `StoreString` alone would have measured the cheap part and concluded the writes are free — the exact failure
   mini-plan 15's P3 produced, one layer down.
2. **`UserStatsService`'s timer spans the temp write AND the rename.** The atomic shape is two filesystem
   operations; measuring only the first would make the safest writer in the project look like the cheapest.
   (P15.11b had already noted the atomic write doubles the volume and that it belonged in a budget — this is that
   budget.)
3. **Writes are counted even between blocks**, and only the per-block columns require an open block. A writer
   firing outside a block is itself a finding.

**The set is 15, and it is the same 15 `WorldWriteGuard` marks — verified 1:1, not asserted.** That list exists
because mini-plan 15 A had to enumerate every world-state writer, which makes it a ready-made census of exactly the
writers whose cost this plan is about. Two things it does *not* count, deliberately: the journal's **deletes**
(retention trim, rebuild wipe — they are not writes and part B will not coalesce them), and `blockchain/state.json`,
which keeps its existing `NoteSnapshotWrite` columns so mini-plan 14's figures stay comparable.

**What the run will show, and where.** Four new CSV columns (`stateWrites`, `stateFiles`, `stateBytes`,
`stateWriteMs`) plus a fragment on each block's line in the **Godot editor's Output panel**; and every 50 blocks a
**ranked table** of writes per path, sorted by **writes per block** — the quantity §3 predicts, not milliseconds.
A file above 1.0 writes/block is a coalescing target for part B; a file at exactly 1.0 is already at its floor and
part B must leave it alone. `block_session_checkpoint.json` can never go below 1.0: it *is* the commit.

**Locale detector: baseline moved 10 → 12**, both new hits being continuation lines of the block report's
`+`-chain, whose `InvariantCulture` wrapper now sits seven lines above — the documented benign shape. Updated in
`CLAUDE.md` in this same commit, which is what that rule requires and what it went a week without last time.

---

## 8. Run 1 results (2026-10-01, 28.1 min at 9000X, 247 blocks, heights 699 → 945)

**Two of three predictions refuted, the third confirmed emphatically — and the refutations redirect the plan.**

### P1 — ❌ REFUTED, and the prediction was looking in the wrong window

Inside the block bracket: **exactly 2 writes, on all 247 blocks — min 2, max 2, no variance whatsoever.**
Predicted ≥ 20. The premise was not wrong about *whether* the writers fire, it was wrong about **where**: the
fifteen writers fire on mutation, mutations happen at **bet rate between blocks**, and that is outside the block
bracket entirely. **Part A's third decision — count writes even when no block is open — is the only reason the real
cost is visible at all.** Had the counter required an open block, this run would have reported 2 writes/block,
8.7 KB, "nothing to fix", and the plan would have closed on a measurement that was true and useless.

### P2 — ✅ CONFIRMED, and more sharply than it was stated

The cost is per-write **overhead**, not volume. The cleanest pair in the data:

| file | bytes | ms per write |
|---|---|---|
| `bankroll_state.json` | **88** | **0.994** |
| `block_session_checkpoint.json` | **7,922** | **0.643** |

**A file 90× smaller costs 55% MORE per write.** Inside the bracket, 8.7 KB in 2.70 ms is an implied 3.2 MB/s.

It also confirms part A's second decision: `bet_stats_rollup.json` (974 B, atomic `.tmp` + rename) costs
**2.181 ms** — the most expensive small-file write in the set, **3.4× the checkpoint that is 8× its size**. Two
filesystem operations, exactly as the atomic shape implies, and timing only the temp write would have hidden it.

### P3 — ❌ REFUTED

`calendar_state.json` **does not appear in the table at all — zero writes in 28 minutes.** `PersistCurrentTime()`
has 20 call sites and not one of them is on the autobet path (the manual-bet path calls it; the delegated sim does
not). **The 18-byte file I named the emblematic case is never written during a run** — a count of call sites is not
a measurement of a rate, which is Standing Convention 15's shape one level up: I cited the symbol correctly and
then inferred a frequency from it. The emblematic case is instead `bankroll_state.json` at **88 bytes and
0.994 ms**.

### The ranked table, per block (over 200 blocks)

| path | writes/block | ms each | ms/block |
|---|---|---|---|
| journal segments (**1,234 distinct files**) | 19.96 | 5.41 | **108.0** |
| `bankroll_state.json` (88 B) | 13.43 | 0.994 | 13.4 |
| `casino_sc_balance_state.json` (344 B) | 13.43 | 0.941 | 12.6 |
| `bet_stats_rollup.json` (974 B, atomic) | 1.00 | 2.181 | 2.2 |
| `block_session_checkpoint.json` (7.9 KB) | 1.00 | 0.643 | 0.6 |
| three others | ~0.01 | ~0.7 | ~0.01 |
| **total** | **48.84** | | **136.8** |

### ⚠ The denominator, because this plan would otherwise repeat the error it was written to avoid

**136.8 ms per block against a 13.7 ms block reads as a 10× catastrophe. It is not one.** These writes are driven
by **bet rate**, not by blocks, so "per block" is the wrong denominator — the same mistake that made mini-plan 15's
P4 fire on a collapsing denominator while its numerator sat still. Under wall-clock, the honest frame:

- **27.4 s of writing over 1,685 s = 2.00% of real time**, about **20 ms of every second**, **7.1 writes/second**.

Worth fixing. Not dominant. **Both numbers are true and only one of them answers a question anyone should act on.**

### What is pure waste — the finding worth acting on

CLAUDE.md Pattern 2 states plainly that between-block writes of these files are **discarded at every boot**, being
restored from the checkpoint or reset pre-genesis. So `bankroll_state.json` and `casino_sc_balance_state.json` —
**26.9 writes per block, 5,372 writes in the run, 5.2 s of wall time** — are writing files that **nothing will
ever read**. Both carry an identical 0.5 s dirty-flag throttle (mini-plan 08 / ND.8f) and both are working exactly
as designed at 2.24 writes/second. **The design is what is wrong: the correct cadence for a value whose only
reader is the checkpoint is not twice a second, it is once per block.** That collapses 26.9 writes/block → 2.

**The journal is the opposite case and must not be treated the same way.** At 108 ms/block it is by far the largest
term, and it is *not* waste — it is the record. Two things about it are notable rather than actionable yet:
**1,234 distinct segment files in 28 minutes** (the retention window churning), and **5.41 ms per append** of
10–126 KB.

### Decision-rule outcomes

- **P2 held ⇒ the plan proceeds.** The rule said P2 failing (bytes being the cost) would mean stop and re-plan.
- **P1/P3 refuted ⇒ part B is re-scoped, not cancelled.** It is no longer "coalesce any writer above 1.0/block"
  generically. It is: **(B1)** move the two balance files from a time throttle to a block commit — the dirty/flush
  machinery already exists in both services, so this is small; **(B2)** the journal is a separate decision with its
  own measurement, and nothing in this run justifies touching it yet.
- **Part C (skip unchanged) is now expected to be worthless for the two balance files** and is demoted: during an
  autobet the bankroll changes on every bet, so every one of those 2,686 writes carried a genuinely new value.
- **Part D (one file for the money services) is dropped**, as its gate required: the money services other than
  those two write ~0.01 times per block. There is nothing to merge.

### A gap in part A worth recording

**The ranked table — the single most important output of this run — goes only to the Output panel**, not to the CSV,
so it was recoverable only because `user://logs/godot.log` captures `GD.Print` and that log is kept as evidence
(Pattern 2's exempt set). That was luck, not design. **A measurement whose only channel is a console is a
measurement that depends on someone pasting it.** If part B needs a second ranked comparison, the table goes to its
own CSV first.
