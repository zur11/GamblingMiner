# Mini-Plan 17 — The journal's rotation, and a UTXO set that is updated instead of rebuilt

**Series note:** seventeenth of the *mini-plan* series, following `mini16-fewer-writes-per-block-plan.md`, which
measured both of this plan's targets and whose measurement rule this plan inherits in full.

**Status:** 📋 **SPECIFIED 2026-10-05**, not started. Proposed branch `mini17-journal-rotation-and-utxo`.

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
