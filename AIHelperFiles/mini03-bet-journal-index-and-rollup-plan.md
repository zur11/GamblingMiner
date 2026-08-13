# Mini-Plan 03 — Bet journal: a chunk index and a persisted rollup

**Series note:** third entry of the *mini-plan* series, following
`mini02-panel-state-and-100k-audit-plan.md` (whose **Part D** this is, promoted to its own plan
exactly as that plan said it should be).

**Status:** 📋 **DRAFT — awaiting developer review.** Branch created, no code touched. ·
**Branch:** `bet-journal-index-and-rollup` · **World format bump:** likely **yes** — see §5 ·
**Design record (proposed):** `Documentation/ProjectDesignManual.md` new §40.10; INC-001's entry in
`Documentation/INCIDENT_LOG.md` gains a "closed by" pointer.

---

## 1. What is already done, and what is not

The single most common misremembering of this work is "we should store bets in chunks". **Chunking
already shipped** (INC-001 / P15.11 / D-15.28) and the mini-plan-02 audit verified it running:

| Already true | Value |
|---|---|
| Journal is chunked | `bet_history_NNNNNN.jsonl`, `MaxJournalEntriesPerChunkFile = 10000` |
| Old chunks are pruned | `MaxRetainedJournalChunks = 20` ⇒ newest **200,000 bets** kept |
| Writes are bounded | observed live: 08-07 archive holds chunks `000001–000010`; by 08-12 the world had pruned to `000004–000024` |

**What is NOT done is the read side.** `UserStatsService.EnsureFullHistoryLoaded()` still loads
**every retained chunk** — 200k records — and every lifetime figure the player sees is derived by
scanning them. Retention bounds what is **written**; nothing bounds what is **read**. That is
INC-001's root cause still standing after INC-001's fix.

## 2. The two deliverables

**D1 — the chunk index.** Per chunk: first/last `TimestampUtc` and record count. A consumer
binary-searches to the chunk covering a date and loads only that one.

**D2 — the persisted rollup.** Lifetime figures kept as **running totals updated as each bet
settles**, instead of derived by scanning: total bets, wins/losses, wagered, net P/L, max bet,
**max martingale level**, and max consecutive losses.

They are separable and D1 is the one that closes INC-001. If only one ships, ship D1.

## 3. Decisions carried over from mini-plan 02 (still binding)

- **D-M2.10 — max martingale level is FREE and more correct than deriving it.**
  `BaseBetSession.ProgressionTriggerStreak` already *is* the live ladder depth; its running maximum
  costs one comparison per settled bet. It is **not** the same quantity as INC-002's "max consecutive
  losses" — insist resets, the §25.5 bankroll-limit reset and every auto-recharge return the bet to
  base while a loss run keeps counting — and the two must never be conflated in code or in a label.
  Captured at settle time in true order, it is also immune to the tie-order sensitivity a
  reconstructed metric suffers (§B.6.4/§B.6.8a of the mini02 plan).
- **D-M2.11 — v1 is GLOBAL.** No per-strategy epochs. The one exception that must survive:
  **max consecutive losses stays segmented by `(GameId, Chance)`** — INC-002/§40.8's correctness
  rule, not a presentation choice. The per-strategy breakdown is the **Betting Statistics scene**
  (`PRIVATE_ROADMAP.md`, Basic Mode objective), and the epoch key gets designed there.
- **D-M2.12 — a rollup is a persisted figure that can diverge from reality** (§39.16 rule 1). It
  ships **checkpoint-covered** — rolled back to the last mined block like every other player-facing
  persisted value — or a crash leaves the summary ahead of the journal it summarises.

## 4. The new constraint pruning creates (found during the mini02 audit)

D-M2.12 originally proposed verifying the rollup by **recomputing it from the chunks**. Pruning makes
that impossible: the old records are *gone*, so a recompute can only ever validate the **retained
window**, never the lifetime totals.

Consequences, and they shape the whole design:

1. **The rollup must be a genuine running total**, never a cache of something re-derivable. Once the
   first chunk is pruned it is the *only* record of those bets' contribution.
2. **The DEBUG verification is scoped, not total** — recompute over the retained window and compare
   against a *windowed* counterpart, or compare deltas rather than absolutes. A verification that
   silently compares a lifetime total against a truncated recomputation would fail forever and be
   switched off, which is worse than not having one.
3. **Losing the rollup loses history permanently.** That raises its durability requirements to
   INC-001's own standard: atomic write (`.tmp` → flush → rename), a loud failure on a corrupt read,
   and never persisting a failed load back over the good copy (§40.5).

## 5. World format bump — expect yes

A new persisted file (or new fields in `user://` state) that the checkpoint restores. Per project
policy — **bump and wipe, never migrate** — `WorldFormatVersion` 5 → 6 and the new file joins
`NetworkRoot.ResetWorldIfIncompatible`'s delete list. Cheap here: the rollup is derived from play, so
a wipe costs nothing but the wiped world itself.

⚠️ **A bump wipes `hardware_allocation.json`**, which resets the player to 1 credit. Any performance
comparison across the bump must re-establish credits first — mini-plan 02 lost a 100k run to exactly
this (§B.7a). Hardware is free to re-add (no BTC cost; P5 is unbuilt).

## 6. Open questions for the developer

1. **Where does the rollup live** — inside `UserStatsService` (already owns lifetime stats and
   self-persists per bet, less surface) or a new file beside the journal (separates "stats I display"
   from "index over storage")?
2. **Does the index get persisted, or rebuilt at boot from chunk headers?** Rebuilding means reading
   the first and last line of each of ≤21 files — likely fast enough to need no persistence at all,
   which would remove the bump. **Worth measuring before designing around it** (§40.7: time it, or
   say plainly that you did not).
3. **Is `EnsureFullHistoryLoaded` allowed to disappear**, or must some screen still be able to demand
   the whole retained window?

## 7. Verification

- Boot time with a 200k-record journal, before and after — **the** headline number, and the only one
  that says INC-001 is closed.
- `BetsHistoryExplorer` still correct: time-travel to a date, and the window shown matches what the
  full-load path showed.
- The chance-to-win selector still lists exactly the chances present, with its time-aware behaviour
  intact (mini02 §40.9) — it consumes the loaded history and is the most likely thing to break.
- Rollup figures agree with a from-scratch scan **over the retained window**.
- Rollup survives a restart and rolls back with the checkpoint; a mid-block crash never leaves it
  ahead of the journal.
- `Sim:` retention unchanged in DiceGame and BetsHistoryExplorer — **measure warm, and prove it by
  returning** (mini02 §38.8b); never compare across worlds.

## 8. Out of scope

- The per-strategy **Betting Statistics scene** (roadmap, Basic Mode).
- Retuning `MaxRetainedJournalChunks` / `MaxJournalEntriesPerChunkFile` — this plan makes the read
  cheap; whether 200k is the right retention is a separate question.
- What DiceGame spends its frame on at 9000X (mini02 §C.6c, open — belongs with roadmap §8 T4).
