# Mini-Plan 16 — Fewer writes per block, and a success criterion that reproduces

**Series note:** sixteenth of the *mini-plan* series, following `mini15-fail-closed-and-readouts-that-strobe-plan.md`,
whose close-out recommended this over T4.1 and whose refuted predictions dictate how this plan states its own.

**Status:** 📋 **SPECIFIED 2026-09-30**, not started. Proposed branch `mini16-fewer-writes-per-block`.

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
