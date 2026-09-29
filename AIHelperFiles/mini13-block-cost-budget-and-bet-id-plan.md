# Mini-Plan 13 — What a block costs, and the last field in a bet

**Series note:** thirteenth of the *mini-plan* series, following `mini12-journal-memory-bound-plan.md`. Mini-plan
12 left two things behind: a per-bet cost whose remainder is one field, and a per-block cost that mini-plan 11
measured only as a single number. This plan takes both, and the second is the one that matters.

**Status:** 📋 **SPECIFIED 2026-09-28**, not started. Proposed branch `mini13-block-cost-budget-and-bet-id`.

**Why these two together.** They share one instrument session and nothing else. **A** is the roadmap's **T4.6**,
which T4 itself says to do first because four other items (T4.1, T4.2, T4.3, T4.5) are gated on it. **B** is the
small remainder of mini-plan 12, cheap to build and cheap to measure, and it rides along rather than waiting.

**The asymmetry to keep in view while working:** a bet's cost is **constant and already small** — the frame
delivers everything the clock can ask for, even with five engines at the hardware cap (mini-plan 11 A). A
block's cost **grows with the world**. B is a finishing touch; A is the next real scaling question.

---

## 1. What the code says, before measuring (read 2026-09-28)

**`NetworkRoot.HandleMinedBlock` runs, in order:** `SharedNetwork.BroadcastBlock` (to every node) ·
`AppendDifficultyTrace` · `ScheduleBotTransactionsAfterBlock` · `TrySettleResolvedAuctions` ·
`CancelAndRefundStaleAuctionBids` (a mempool sweep across every node) · `TickCompanyGovernance` (votes,
dividends, its own trace) · `HistoricalEventScheduler.OnBlockMined` · **`PersistStateToDisk`** (the whole chain
serialized and atomically rewritten) · `TryDistributePendingCasinoRewards`. Then, outside it,
`SimulationService.CaptureCheckpoint` writes the checkpoint, and the next frame recomputes the founders and the
population.

**The leading suspect, and it is structural.** `BlockchainService.GetUtxoSet()` **rebuilds the UTXO set by
replaying the chain oldest→newest**, cached until the chain changes. A block invalidates it, so the next query
replays **every transaction ever**. Each node keeps its **own** `BlockchainService` — the roadmap counts ~62 of
them (player + 4 bots + 40 non-miner companies + cast) — so the same replay can happen many times per block,
and the work grows with chain length on both axes. That is T4.1 (incremental UTXO) and T4.2 (one chain instead
of 62), and neither is built here.

**What is already measured**, and it is only the total: per-block work was **24 ms at height 376 and 33 ms at
747** (mini-plan 11 C2, `blockWorkMsPerBlock`), and `state.json` went **0.46 MB at 362 blocks → 0.97 MB at 841**.
Roughly linear in height on both, at heights far below the 2,699-block world T4.0 was written from.

**The id (B).** `BetRecord.Id` is a GUID as 32 hex characters, and **the journal's duplicate guard is its only
reader** — verified by grep, every use is inside `BetHistoryRepository`. It costs, per bet: a GUID generation, a
32-character string allocation (~88 bytes), a 32-character hash on insert *and* another on trim, and ~40 of a
line's 163 bytes. INC-002 is why it exists: duplicated records inflated a streak statistic.

---

## 2. The work

### A — the per-block budget (T4.6)

A `BlockCostProfiler` in the shape the other two already have: DEBUG-only, off by default, armed from a toggle
beside the DEV time selector, printing to the Godot editor's **Output** panel and appending a CSV row. **One row
per block**, carrying `chainHeight` and the game date, so cost is read against the world's size rather than
against the run.

Phases, each timed where it happens:
- `Broadcast` (acceptance across every node), `BotTransactions`, `Auctions` (settle + stale-bid sweep),
  `Governance`, `HistoricalEvents`, `Snapshot` (`PersistStateToDisk`, serialize + atomic rename separately if
  they separate cleanly), `CasinoRewards`, `Checkpoint` (the capture that follows), and `Traces` (the five CSV
  appends).
- **`UtxoRebuild` is counted, not just timed**: rebuilds per block, total ms, and **how many distinct nodes
  rebuilt**. It is lazy, so it is attributed to the block that invalidated the cache, wherever the query lands.

### B — the bet's last field

Replace the GUID with a **per-world counter**. Uniqueness stops depending on randomness; hashing an 8-byte
number replaces hashing 32 characters; the line loses ~30 bytes; the per-bet string allocation disappears.

**The part that needs care, and it is the whole risk:** the counter is world state. A restart rewinds the world
to the last block, so the counter must rewind with it, or a re-issued number could collide with a record the
journal still holds. The rule to implement and then verify on a real restart: **the counter resumes from the
highest id the journal actually holds after the rollback**, which needs no new persisted field.

World format bumps (**8 → 9**): the loader cannot read a GUID id as a counter, and per project policy is not
taught to.

---

## 3. Predictions, registered before any data

**A:**
- **P1** — `UtxoRebuild` is the largest phase, at **≥ 40%** of per-block cost, and more than one node rebuilds
  per block.
- **P2** — `Snapshot` is second, and grows linearly with chain bytes: **≥ 20%**.
- **P3** — the five CSV traces together are **under 5%**.
- **P4** — per-block total grows roughly linearly with height, and at height ~1,500 it exceeds **60 ms**.

**B:**
- **P5** — `JournalAdd` falls by **≥ 1 µs** from 4.20, and a line loses **≥ 25 bytes** from 162.7.
- **P6** — correctness holds: zero balance-continuity breaks across the journal, and after a restart no id is
  issued that the journal already holds.

## 4. Decision rules, registered before the data

**A — this plan measures and names; it builds none of the follow-ups.**
- **`UtxoRebuild` ≥ 50%** ⇒ the next plan is **T4.1** (incremental UTXO), with **T4.2** evaluated beside it,
  because 62 copies multiply the same work and fixing one without the other leaves the multiplier in place.
- **`Snapshot` ≥ 50%** ⇒ the next plan is **T4.5** (append the chain instead of rewriting it).
- **Neither reaches 50%** ⇒ name the largest phase with its share and its growth in height, and let that choose.
- **Any phase under 5%** is recorded as **not worth optimizing**, so the next plan does not re-litigate it.

**B:**
- **P5 holds** ⇒ keep the counter.
- **P5 fails** ⇒ the saving does not pay for a persisted-format change. Revert it in the same plan rather than
  leaving a half-justified format in place, and record the measured number so nobody proposes it again.
- **P6 fails at any point** ⇒ revert, regardless of P5. A duplicate id is INC-002's shape, and this field exists
  only to prevent it.

## 5. Order

1. **Build A's instrument** (no behaviour change) → one build.
2. **Run A** (~20–30 minutes, player alone at 9000X from the current world) → cost against height, P1–P4.
3. **Build B** (the counter + world bump 8 → 9).
4. **Run B** (a short Bet cost leg, then a restart) → P5, P6.
5. **Decide A's follow-up** by §4, **close out**.

## 6. Out of scope

- **Building T4.1, T4.2, T4.3 or T4.5.** This plan is the measurement that chooses between them.
- **The Betting Statistics scene, the adaptive budget, PowerShell 7** — unrelated roadmap items.
- **T4.4** is already substantially done: mini-plan 12 bounded the journal in memory, and the lifetime rollup has
  been the counter source since mini-plan 03.

---

## 7. Results — A, first pass (2026-09-28, 30 minutes, 275 blocks, height 121 → 395)

Player alone at 9000X, Block cost armed, the other two profilers off. The world is early-era, so **no company
exists yet** and the governance and auction phases are near zero by construction — a fact about the era, not a
gap in the instrument, and it means this run cannot price the mature-world per-block work T4.0b describes.

| phase | ms per block | share |
|---|---|---|
| **Checkpoint** | **9.87** | **45.7%** |
| **Snapshot** (`PersistStateToDisk`) | **7.80** | **36.1%** |
| UTXO replay (since previous block) | 1.60 | 7.4% |
| Traces (the difficulty CSV) | 1.41 | 6.5% |
| Bot transactions | 1.07 | 5.0% |
| Historical events | 0.96 | 4.5% |
| Subscribers · Auctions · Broadcast · Governance · Casino | 0.23 · 0.21 · 0.04 · 0.002 · 0.008 | under 2% |
| **total** | **21.61** | |

**Growth against height**, five buckets of 55 blocks:

| mean height | total | snapshot | checkpoint | UTXO ms | rebuilds | per rebuild | snapshot KB |
|---|---|---|---|---|---|---|---|
| 148 | 23.15 | 6.76 | 9.01 | 0.81 | 11.4 | 0.071 | 213 |
| 203 | 19.62 | 7.47 | 9.54 | 0.99 | 8.0 | 0.124 | 274 |
| 258 | 19.34 | 7.26 | 9.13 | 1.64 | 8.0 | 0.206 | 332 |
| 313 | 22.36 | 8.09 | 11.06 | 2.10 | 8.0 | 0.262 | 390 |
| 368 | 23.55 | 9.39 | 10.62 | 2.46 | 8.0 | 0.310 | 447 |

**Verdicts.**
- **P1 refuted on its share, confirmed on its shape.** The UTXO replay is **7.4%**, not ≥ 40%. But **8.7
  distinct nodes rebuild per block**, exactly as suspected, and the cost *per rebuild* grew **4.4× while the
  chain grew 2.5×** (0.071 → 0.310 ms). It is the fastest-growing line in the table and the cheapest today:
  T4.1/T4.2 are real, and they are not yet the priority.
- **P2 held:** the snapshot is **36.1%**, and its growth tracks the bytes it writes (213 → 447 KB).
- **P3 refuted, narrowly:** the trace append is **6.5%**, not under 5% — one CSV append per block, flat in height.
- **P4 not measurable in range:** the run reached height 395. Extrapolating the fitted growth to 1,500 gives
  roughly 75 ms a block, which is **an extrapolation and is labelled as one.**

### The finding: the world snapshot appears to be written TWICE per block

The checkpoint phase costs *more* than the snapshot phase, and the code path says why:
`CaptureCheckpoint → PersistFinancialState(true) → SetNodeFinancialState(…, persist: true) → PersistStateToDisk`.
The block has already persisted once inside `HandleMinedBlock`; committing the bet's balances then serializes
and atomically rewrites **the entire chain again**.

**It is not redundant in content** — the second write is the only path that commits the post-bet financial
mirrors, including the bots' — **but the chain half of it is identical both times.** That is precisely the
condition T4.5 describes: the mutable state cannot be committed without rewriting an immutable, ever-growing
chain beside it.

**Being measured rather than inferred, per this plan's own standard.** `PersistStateToDisk` now times itself and
reports each completed write, so the next run's trace carries `snapshotWrites` and `snapshotWriteMs` per block.
If it reads 2 writes and ~16 ms, then **snapshot work is ~73% of a block** and §4's rule selects T4.5 — with the
duplicate itself as a cheaper first move that no rule anticipated.

### The double write, confirmed — and D-13.1

**Measured (2026-09-29, 51 blocks, height ~395–446):** every block reports **exactly 2 snapshot writes — 51 of
51, no exceptions** — costing **18.39 ms of a 27.55 ms block: 66.7%**, at 503 KB a write. The phases split it
10.72 ms (Snapshot) and 12.33 ms (Checkpoint), which is why neither phase alone looked like the answer.

The prediction stated before the run — 2 writes, ~16 ms, ~73% — **held**.

**D-13.1 — §4 A's rule fires on `Snapshot ≥ 50%`, so the follow-up is T4.5** (append the chain instead of
rewriting it), not T4.1 or T4.2. The UTXO replay, the plan's own leading suspect, is 7.4% and stays open as a
growth risk rather than a target. **And the rule's own branch understates what was found:** two thirds of a
block is spent writing the same immutable chain **twice**, which makes "write it once" a cheaper first move
than anything T4.5 proposes, and neither the plan nor the roadmap had anticipated a duplicate.

**What the next plan must not assume.** The second write is not removable on its own: it is the only path that
commits the post-bet financial mirrors, the bots' included. The chain half is what is redundant, so the fix is
to separate the two — which is exactly T4.5's shape, now with a second, independent reason to build it.

### Aside — the restart guard (developer's request, 2026-09-29)

A double-click on STOP restarted the run immediately, which during a measurement starts a session the protocol
did not ask for. A start is now ignored for `AutobetRestartGuardMsec` after any stop, manual or self-inflicted,
with the button reverted so it never shows a run that is not running. **STOP is never guarded, in either
direction** — a guard that could swallow a stop would be worse than the accident it prevents.
