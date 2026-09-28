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
