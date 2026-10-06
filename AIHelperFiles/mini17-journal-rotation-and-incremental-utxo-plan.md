# Mini-Plan 17 — The journal's rotation, and a UTXO set that is updated instead of rebuilt

**Series note:** seventeenth of the *mini-plan* series, following `mini16-fewer-writes-per-block-plan.md`, which
measured both of this plan's targets and whose measurement rule this plan inherits in full.

**Status:** ✅ **DONE 2026-10-06** (close-out §11), branch `mini17-journal-rotation-and-utxo`.
**T4.1 and T4.3 shipped:** UTXO rebuilds **8 → 0.313 per block**, `GetSpendableUtxos` walks only owned outpoints,
and the oracle was silent across 195 blocks and 61 nodes with its `ARMED` line proving it ran (**P3, P4, P5**).
**A2 DROPPED by its own rule (P1 refuted):** 2.00 scans and 0.99 deletes per rotation exactly as read from the
code, but metadata is **4.4%** of journal time against ≥50% predicted — a directory scan costs 0.256 ms, 30×
less than an append. **The journal is closed as a performance subject: 95.6% appends, and the appends are the
record.** Run 1 also exposed a misplaced sort in B2 (hottest reader, summing consumer), corrected in the same
commit. Next candidate, unmeasured: `botTransactionsMs`.

**Two parts, and they bundle for a reason beyond convenience.** Mini-plan 16 left the journal as 94.9% of all
remaining write cost, and T4.1 has been queued since mini-plan 13 with its trigger restated in absolute
milliseconds. **T4.3 (the address → outpoint index) is folded in**, because the roadmap already says it is
"maintained alongside T4.1's incremental update" — the hook T4.1 adds is the only place T4.3's index can be kept
correct, so building them apart means building the hook twice.

**The measurement rule this plan inherits, stated up front because it decides how every prediction below is
written.** Mini-plan 16 established that **a per-block millisecond figure does not reproduce across sessions in
this project** — block cost doubled between two runs a day apart while that plan was removing 26 ms/block of
writes. So: **every success criterion here is a COUNT or a complexity claim, verified within a single session.**
Milliseconds are reported as distributions, and no decision hangs on one.

---

## 1. What the code says, before changing anything (read 2026-10-05)

### A — the journal: the cost is in the ROTATION, not the appends

Mini-plan 16 measured the journal at **19.33 writes/block, 99.0 ms/block, 5.12 ms per append, and 1,494 distinct
segment files in 30.6 minutes** — 94.9% of the project's remaining write cost. The appends looked like the obvious
target. Reading `BetHistoryRepository` says otherwise:

- `MaxJournalEntriesPerChunkFile = 10000`, and 1,494 segments in 30.6 min is **~49 rotations per minute** at 9000X.
- **`RotateToNextChunkFile()` calls `GetJournalChunkPaths()`**, which runs **`Directory.GetFiles(folder, pattern)`**
  — a directory enumeration — purely to parse the highest existing index off a filename.
- It then calls **`EnforceRetentionCap()`, which runs `GetJournalChunkPaths()` AGAIN**, and deletes the excess.

So **each rotation performs two directory enumerations of `user://`, one file creation and (in the steady state) one
deletion — roughly 49 times a minute.** With retention at 20 segments the folder holds ~43 entries, so each scan is
small; metadata operations are what they are, and there are **four of them per rotation where there could be one.**

**Both scans are avoidable from state the repository already owns.** It tracks `_activeJournalPath` and
`_activeJournalLineCount`; the next index is `current + 1`, and the retained set is a list it mutates on every
rotation. Nothing else writes to that folder.

### B — the UTXO set: `O(all transactions ever)` on every chain mutation

`BlockchainService.GetUtxoSet()` caches a `Dictionary<string, UtxoEntry>` keyed on `_chainVersion` and, when the
version moves, **replays the entire chain** — every block, every transaction, every input and output. Mini-plan 15
measured it at **~4 ms per rebuild, 8 rebuilds per block**, plateauing from height 500 (so bounded, but the plateau
is measured only to game date 2010-04, before Market Birth).

`TryAcceptMinedBlock` / `MineBlock` already know exactly which block arrived, so the incremental update is a few
lines beside the existing version bump. **Keep the full replay** for `TryReplaceChain` (a genuine chain swap) and as
a DEBUG cross-check.

`GetSpendableUtxos(addresses)` then **iterates `utxos.Values` in full** and filters by an owned-address set — so
every wallet panel, bot affordability check and treasury read is `O(whole UTXO set)` regardless of how little that
node owns. **12 call sites** reach `GetUtxoSet()` / `GetSpendableUtxos()`.

---

## 2. The work

### A1 — instrument the rotation, separately from the append

Split the journal's single `NoteStateWrite` into its real components: **appends**, **rotations**, **directory
scans**, **creates**, **deletes**, each counted and timed. One build, no behaviour change. This is the same move
part A of mini-plan 16 made, and it is the move that turned that plan's premise from wrong to measured.

### A2 — remove both directory scans

The next index comes from in-memory state; the retained-segment list is held and mutated on rotation rather than
re-derived from the filesystem. **Scans per rotation: 2 → 0.** The list is rebuilt from a single scan **once**, at
load, where a scan is already happening.

⚠ **The invariant that must not break:** `GetJournalChunkPaths` returns segments in **chronological order**, and
`EnforceRetentionCap` trims from the front. An in-memory list must preserve that ordering property, including after
`RebuildJournalFromCurrentState` (which deletes everything and re-writes). **A retention cap that trims the wrong
end destroys the newest history instead of the oldest**, and nothing downstream would announce it — so the DEBUG
cross-check in A3 covers this, not just the UTXO work.

### A3 — a DEBUG cross-check for both parts

Every N blocks, assert that the cheap path equals the truthful one: **the in-memory segment list equals a fresh
directory scan**, and **the incrementally-maintained UTXO set equals a full replay**. This is §39.16 rule 1's shape
— the cheap path must be provably identical to the truthful one — and it is the only thing that makes B safe to
ship.

### B1 — incremental UTXO maintenance (T4.1)

Apply a newly-accepted block's transactions to the cached set at the point the chain is extended, bumping
`_utxoCacheVersion` with `_chainVersion` together. The full replay stays for `TryReplaceChain` and for A3's assert.

### B2 — the address → outpoint index (T4.3)

Maintain `address → HashSet<outpointKey>` inside the same incremental update, and have `GetSpendableUtxos` walk
**only the requested addresses' outpoints**. This is the natural follow-on to §38.7's R3 fix, which already took
`AggregateSpendable` from `O(addresses × utxos)` to `O(utxos)`; this takes it to **`O(owned)`**.

---

## 3. Predictions, registered before any data

Counts and complexity, per the rule above.

- **P1 — the journal's rotation metadata work dominates its append cost.** Measured per rotation: **2 directory
  scans, 1 create, ~1 delete**, at ~49 rotations/minute, and **rotations account for ≥50% of the journal's total
  milliseconds** once A1 separates them. *This is the premise; if it fails, A2 is not worth doing.*
- **P2 — A2 takes directory scans per rotation from 2 to 0**, and total journal writes per block are **unchanged**
  (A2 removes metadata work, not appends).
- **P3 — B1 takes UTXO rebuilds per block from ~8 to 0** in steady play, with the full replay running only on
  `TryReplaceChain` and on A3's periodic assert.
- **P4 — A3's assert never fires.** A single firing is a correctness bug in B1/B2 or A2, and it blocks the plan.
- **P5 — B2 makes `GetSpendableUtxos` cost independent of the UTXO set's size.** Verified as a complexity claim,
  not a timing: the index lookup touches only the requested addresses' outpoints, demonstrated by the assert in A3
  returning identical results while the walk is gone.
- **P6 — milliseconds are reported as distributions, within one session, with no threshold attached.** Stated so
  that no part of this plan can be judged by a figure that mini-plan 16 proved does not reproduce.

## 4. Decision rules, registered before the data

- **P1 fails (appends dominate, not rotation) ⇒ A2 is dropped** and the journal's cost is recorded as inherent to
  recording 8,300 bets/second. That is a legitimate outcome: the journal is the record, not waste.
- **P4 fails at any point ⇒ stop.** B1/B2 do not ship on a set that disagrees with the replay, however much faster
  it is. **A wrong UTXO set is a wrong balance**, which is the one class of defect this project treats as
  unrecoverable (INC-001/004).
- **P3 holds but P5 cannot be demonstrated ⇒ ship B1, hold B2.** The incremental update stands on its own; the
  index is an optimisation of readers and can wait for a measurement that shows it.
- **No part of this plan is judged by a per-block millisecond comparison across sessions.** If a run produces a
  tempting figure, it goes in the record as a distribution and decides nothing.
- **Whatever happens, the full replay stays in the code.** It is the oracle; deleting it to "clean up" would remove
  the only thing that can prove the fast path right.

## 5. Order

1. **A1 — instrument the rotation.** One build. Run 1 (~20 min at 9000X, Block cost armed) settles P1.
2. **A3 — the cross-check first**, before either fast path exists, so it is never written to fit a result.
3. **A2** (if P1 held) → **B1** → **B2**, one build each.
4. **Run 2**, matched within one session: P2, P3, P5, and P4 throughout.
5. **Close out.**

## 6. Out of scope

- **T4.2 (one canonical chain instead of ~62 copies).** The largest structural item left, and it needs a decision
  about fork simulation that is explicitly post-Basic-Mode. B1/B2 keep a measured share rather than an estimate.
- **A binary or compressed journal format.** Mini-plan 12 rejected binary on measurement; nothing here revisits it,
  and P1 is the prediction that would have to fail first.
- **Changing the retention cap or the segment size.** Both are policy, both are documented, and neither is this
  plan's subject — A2 changes *how* the policy is enforced, never *what* it is.
- **The bot-mined block one-frame clock offset** and **"a budget that adapts to the machine"** — both open, both
  unrelated. The latter still needs a second machine.

---

## 7. Part A1 as built (2026-10-05)

**No behaviour change. The journal's single write counter is split into the operations it actually performs.**

`BlockCostProfiler` gains `NoteJournalOp(JournalOp, startTicks)` for **Append**, **DirectoryScan** and **Delete**,
plus `NoteJournalRotation()`. Three decisions inside it:

1. **A rotation is COUNTED, never timed.** It is an envelope around the scans and deletes already timed inside it,
   so timing it as well would double-count every millisecond it contains. The count is what P1 predicts; the
   milliseconds belong to the operations within.
2. **There is deliberately no `Create` op.** A new segment is created implicitly by the first append's
   `FileMode.Append`, so its cost is already inside that append — a separate counter would be a line that never
   fires, which is worse than no counter at all.
3. **The append is double-instrumented, on purpose.** It is still counted as a world-state write (keeping
   mini-plan 16's figures comparable) *and* as a journal op (answering this plan's question). **The two must never
   be summed**, and both call sites say so.

**Where the counters sit:** the scan inside `GetJournalChunkPaths` (in a `finally`, so the `catch`'s early return
is still counted), the delete loop in `EnforceRetentionCap`, the rotation at the top of `RotateToNextChunkFile`,
and the append beside the existing `NoteStateWrite`.

**The ranked table now leads with the journal**, and prints the number P1 is about:
`METADATA share of journal time: NN.N% (P1 predicts >= 50%)`, with scans and deletes also shown **per rotation**
(predicted 2.00 and ~1.00). Seven new CSV columns carry the same per-block figures.

**Verified before staging, because this is the error class that bit mini-plan 14:** the trace's **header column
count was checked against its format string's placeholder indices programmatically** — 33 columns, 33 unique
placeholders, max index 32. A trace whose header and row disagree is how a profiler reports the wrong quantity
with total confidence, and reading column 20 as milliseconds once cost a wrong conclusion.

**Locale detector: baseline 12 → 17**, all ten hits now in `BlockCostProfiler.cs` — five in the per-block report
line, five in the new journal block — each a continuation line below its `InvariantCulture` wrapper (at lines 371
and 434). Updated in `CLAUDE.md` in this same commit, which is what that rule requires.

---

## 8. Part A3 as built (2026-10-05) — the oracle, before either fast path

**`BlockchainService.DescribeUtxoCacheMismatch()`** replays the whole chain into a fresh dictionary and compares it
entry by entry — `TxId`, `Vout`, `BlockIndex`, `IsCoinbase`, `IsSpendable`, amount and address — against whatever
`GetUtxoSet()` currently holds. Returns `null` on agreement, or a **diagnosis** rather than an alarm: the two
counts, plus up to three example keys each for *missing from cache*, *extra in cache* and *differing*. "The sets
disagree" tells the next reader nothing about which direction the bug runs.

**Today it passes trivially, and that is the point.** The cache is still produced by a replay, so cache and oracle
are the same arithmetic. Writing the comparison now means it exists and is proven to run **before there is any
result it could be shaped to fit** — which is the whole reason §5 put A3 ahead of A2 and B1.

**The driver is round-robin, one node per 25 blocks** (`NetworkRoot.AssertUtxoIntegrityPeriodically`, called from
`HandleMinedBlock` — the one hook every miner's block passes through). Asserting all ~62 nodes per interval would
mean ~62 full chain replays, and **an instrument that costs more than the thing it watches gets disarmed, which
makes it no instrument at all.** One replay per 25 blocks covers every node over time at a cost nobody notices.

**It announces itself once** (`[UtxoAssert] ARMED …`, to the Output panel). This is the DEBUG-canary rule: a check
whose passing state is silence must prove once that it runs, or "no mismatch reported" and "the assert never
executed" are indistinguishable. A mismatch prints to **both** the Output panel and the Errors tab.

**Not built here, and why:** the segment-list half of A3 **cannot exist before A2**, because there is no in-memory
list to compare against a directory scan yet. It lands in A2's own commit, with its assert written before the list
is first *used* — the same ordering principle, applied to the only shape the dependency allows. Recorded rather
than quietly dropped.

**Four nullable-reference warnings were introduced and fixed before staging.** `BlockchainService.cs` has nullable
annotations enabled; the new method returned `null` from a non-nullable `string` and used `out UtxoEntry` where the
dictionary yields a nullable. Caught only by reading the build output rather than its summary line — **an
incremental `dotnet build` that recompiles nothing prints `0 Warning(s)` and means nothing**, which is how four
warnings nearly shipped past a check that has been clean for the whole plan family. Verified with
`dotnet build --no-incremental`.

---

## 9. Parts B1 and B2 as built (2026-10-05)

**Built without a run, because neither is gated on P1** — only A2 is ("P1 fails ⇒ A2 is dropped"). Their gate is
**P4, the oracle never firing**, which is continuous and already armed.

### B1 — incremental UTXO maintenance (T4.1)

`AdvanceUtxoCacheWithAppendedBlock(block)` applies one appended block's transactions to the cached set, called
immediately after **all three** `Chain.Add` + `_chainVersion++` pairs — genesis (`CreateNewBlock`), the mined
template (`CommitMinedBlock`) and the received block (`TryAcceptMinedBlock`). Verified by listing every
`Chain.Add`/`Chain.Clear` in the file and checking each append has the call: **3 of 3**, and
`TryReplaceChain`'s wholesale swap deliberately has none.

**Fail-safe by construction, not by care.** It advances the cache only when `_utxoCacheVersion == _chainVersion - 1`
— i.e. the cache was *exactly* current before this append. Any other state does nothing, and `GetUtxoSet`'s version
check then rebuilds by replay, which is always correct. **So the only route to a wrong set is an arithmetic bug in
one loop**, and that loop is what the oracle compares. Inputs are removed before outputs are added, transactions in
list order — identical to the replay, which is what makes a transaction spending an output created earlier in the
*same block* come out right.

### B2 — the address → outpoint index (T4.3)

`_addressIndex` is built from the finished set in one extra pass after the replay (not maintained inside the replay
loop: that loop removes spent outputs as it goes, so keeping the index in step there would duplicate remove-logic in
the one place that does not need it, and a single pass over the final set cannot disagree with the set it came
from). The incremental path maintains both, reading a spent entry's address **before** removing it — an index that
keeps a key whose UTXO is gone reports money that has been spent.

`GetSpendableUtxos` now walks **only the requested addresses' outpoints** instead of the whole set. Its
`_addressIndex ??= BuildAddressIndex(utxos)` is self-healing rather than silently slow: a permanent fallback to the
full walk would still return the right answer, which is exactly the kind of regression nothing ever notices.

**The oracle covers the index too**, and only *after* the set agrees — so a reported index fault is never an echo of
a set fault. It reports **missing** and **stale** keys separately, because they mean opposite things: a missing key
hides money the node owns; a stale key reports money it has already spent, and only the second can make a bot bid
coins it does not have.

### ⚠ A behaviour change found by reading the consumers, not by a run

`NetworkRoot.SelectUtxos` is **order-dependent twice**: it returns the **first** exact-amount match, and its
`OrderByDescending` is a *stable* sort, so equal amounts are broken by the order it receives. `GetSpendableUtxos`
used to hand back whatever order the UTXO dictionary happened to enumerate — **an implementation detail nobody
chose** — and walking an index instead changes it.

**Resolution: the order is now specified.** The result is sorted by outpoint key, which makes the tie-break defined
and reproducible rather than swapping one arbitrary order for another, and makes "why was that coin spent?"
answerable. It **cannot** change the amount gathered — the selector keeps taking until `gathered >= need` — only
which equal-valued coins are taken. Sorting a per-address slice is cheap in a way sorting the whole set never was,
which is itself the point of the index.

**This is the kind of thing the plan's discipline is for:** nothing would have failed, no assert would have fired,
and the only symptom would have been different coins in a transaction for reasons no one could reconstruct.

### What still needs a run

- **P4** — the oracle silent across a real session. It announces `[UtxoAssert] ARMED` once to the Output panel.
- **P3** — `utxoRebuildsSincePrev` falling from ~8 per block to ~0, readable from the existing trace column; no new
  instrument was needed for it.
- **P1** — and therefore whether **A2 is built at all**.

---

## 10. Run 1 results (2026-10-06, 195 blocks, heights 1221 → 1415, game dates 2011-04-27 → 2011-08-30)

**Note the era first, because it governs how everything below reads.** This run sits entirely **after** Market
Birth, after the median-fee start and after Satoshi's retirement, with **13 powered cast miners** against ~5 at
height 800. Mini-plan 16's runs ended at game date ~2010-04. **No figure here is comparable to one from those runs**
— which is the rule this plan inherited, met on its first run.

### P1 — ❌ REFUTED. The mechanism was right and the magnitude was wrong.

Read from the code before the run: two `Directory.GetFiles` per rotation, plus a delete. **Measured: exactly
2.00 scans and 0.99 deletes per rotation, at 5.49 rotations/block.** The structural prediction was precisely right.

Then the cost:

| op | count | total ms | ms each | share |
|---|---|---|---|---|
| **appends** | 2,750 | **20,800.7** | **7.56** | **95.6%** |
| scans | 1,646 | 421.9 | 0.256 | 1.9% |
| deletes | 813 | 545.5 | 0.671 | 2.5% |

**Metadata share: 4.4%, against the ≥50% predicted.** A directory enumeration of a ~43-entry folder costs
**0.256 ms** — about **30× cheaper than one append**. The whole premise was that metadata operations are
expensive; they are not, at this folder size.

**⇒ A2 is DROPPED, by the rule registered in §4:** *"P1 fails (appends dominate, not rotation) ⇒ A2 is dropped and
the journal's cost is recorded as inherent to recording 8,300 bets/second."* Removing two scans per rotation would
save ~1.9% of the journal's time. **The journal is the record, and recording it is what it costs.**

### P3 — ✅ CONFIRMED

UTXO rebuilds fell from **~8 per block to 0.313** — only **5 of 195 blocks** triggered any, and the one block with
51 is the cold-start burst where every node's cache is built once. `utxoMsSincePrev` is **0.483 ms/block mean,
0.000 median**, against ~4 ms before. No new instrument was needed: it is the existing trace column.

### P4 — ✅ CONFIRMED, and the silence is evidence

`[UtxoAssert] ARMED — … round-robin over 61 node(s)` appears **once** in the Output panel, and
**`MISMATCH` appears zero times** across 195 blocks. The developer reported no errors; **that only counts because
the ARMED line proves the check ran** — this is the DEBUG-canary rule paying for itself, since "no mismatch" and
"the assert never executed" would otherwise be the same observation.

### P5 — ✅ demonstrated as specified

The index is the only path `GetSpendableUtxos` now walks, and the oracle verifies it against a freshly derived
index on every check — so it returns identical results with the full-set walk gone.

### ⚠ The run's real finding, and it is a mistake of mine

**`botTransactionsMs` is now the dominant per-block term: median 0.957 ms, mean 76.899, max 469.256.** It is not
transaction *volume* — `txTargetPerBlock` is ~0.5 in this era. It is **per-participant decision work**, and that is
exactly where `AggregateSpendable` lives, which is called for every auction bidder, bot affordability check,
company treasury read and dead-node sweep — dozens of times per block with 13 cast miners and 40 companies.

**And `AggregateSpendable` calls `GetSpendableUtxos` only to SUM the amounts.** B2's first draft sorted that
method's result by outpoint key to make coin-selection tie-breaking deterministic. The determinism reasoning was
right; **the placement was wrong — I put a sort in the hottest reader the UTXO set has, for a guarantee only one of
its consumers needs.**

**Fixed by moving the tie-break to where the order matters and a sort already happens.** `NetworkRoot.SelectUtxos`
now tie-breaks its existing `OrderByDescending(amount)` with `ThenBy(outpoint key)` — free — and its exact-match
pass picks the smallest key among all exact matches in one O(n) scan with no allocation. Same guarantee, zero cost
on the summing path.

**Explicitly NOT attributed:** I cannot claim the sort caused the 76.9 ms. There is no pre-B2 measurement in this
era, and **this plan's own rule says a per-block millisecond comparison across sessions is not evidence** — so the
one question I would most like answered is precisely the kind this project has established it cannot answer that
way. Settling it would need a within-session A/B behind a toggle, which is out of scope. **The sort was waste
regardless of whether it was expensive**, and that is sufficient reason to remove it.

### A design flaw in A1, recorded because it is the second instance of the same shape

**The seven per-block journal CSV columns are structurally always zero** — `jRotations`, `jAppends`, `jScans` and
the rest read 0 on every one of the 195 rows, because the journal does its work **between** blocks at bet rate and
those columns only accumulate while a block is open. This is **exactly** mini-plan 16's P1 failure, one plan later:
I built a per-block view of something that is not a per-block event. P1 was answerable only because
`NoteJournalOp` also accumulates **outside** the bracket into the session totals the ranked table prints — a
decision made for a different reason that happened to save the measurement. **The per-block columns should be
dropped or re-scoped; a column that can only ever be zero is worse than no column, because it reads as a result.**

### On `totalMs`

Median **25.7 ms**, mean **95.1 ms** — a distribution, as P6 requires, with no threshold attached and no comparison
drawn to any earlier run. The gap between median and mean is `botTransactionsMs` above, nothing else.

---

## 11. Close-out (2026-10-06)

| part | outcome |
|---|---|
| **A1 — instrument the rotation** | ✅ Shipped, and it answered P1 — though only via its session totals, not its per-block columns (§10). |
| **A3 — the oracle** | ✅ Shipped **before** either fast path. Silent across 195 blocks and 61 nodes, with its `ARMED` line proving it ran. |
| **A2 — remove the scans** | ❌ **DROPPED by its registered rule.** Metadata is 4.4% of journal time, not the ≥50% predicted. |
| **B1 — incremental UTXO (T4.1)** | ✅ Shipped. Rebuilds **8 → 0.313 per block**; `utxoMsSincePrev` 0.000 median. |
| **B2 — address index (T4.3)** | ✅ Shipped, **and corrected** after run 1: the sort moved out of the hottest reader into the one consumer that needs it. |

### What this plan is worth beyond T4.1 and T4.3 landing

1. **Reading the code predicted the mechanism exactly and the cost not at all.** Two scans and one delete per
   rotation, measured to the second decimal. And a scan costs **0.256 ms** — 30× less than an append — so the
   premise that metadata operations are expensive was simply false at this folder size. **A structural reading tells
   you what happens; only a measurement tells you what it costs.** The plan's gate caught it and dropped A2 without
   argument, which is what registering a decision rule before the data is for.
2. **The oracle justified its ordering.** Built before either fast path, so it could not be shaped to fit a result;
   silent through 195 blocks of real play, with the canary line making that silence mean something. **P4 is the only
   prediction here whose value lies in nothing happening**, and it is the one that made B1 safe to ship at all.
3. **The run found a mistake no failure would have.** B2's sort was correct, deterministic, and in the wrong place —
   in the one reader that `AggregateSpendable` calls dozens of times a block purely to sum. **Nothing would ever
   have broken.** It was found by asking why an unrelated phase had become the dominant cost, and following that to
   a method I had just touched.
4. **The same instrument flaw appeared one plan after it was written down.** Mini-plan 16 learned that the journal's
   work happens *between* blocks; mini-plan 17 then built seven per-block journal columns that are structurally
   always zero. **A lesson recorded in a close-out is not the same as a lesson applied** — and the measurement
   survived only because a different decision (counting outside the bracket) happened to cover it.

### What follows

- **The bet journal is closed as a performance subject.** Its cost is **95.6% appends** at 7.56 ms each, and the
  appends are the record. Anything further would be a smaller line or fewer bets recorded, both of which were
  already rejected on measurement (mini-plan 12).
- **`botTransactionsMs` is the next candidate, and it is unmeasured.** Median 0.96 ms, mean 76.9, max 469, in a
  populated era (13 cast miners, 40 companies) that no earlier plan reached. The work is per-participant decision
  logic, not transaction volume. **Whatever measures it must be a within-session instrument** — a phase split inside
  `ScheduleBotTransactionsAfterBlock`, or a counter of `AggregateSpendable` calls per block — because this project
  has established that comparing per-block milliseconds across sessions proves nothing.
- **Drop or re-scope the seven per-block journal columns** before the next plan reads that trace and mistakes seven
  zeroes for a result.
