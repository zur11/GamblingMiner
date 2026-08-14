# Mini-Plan 03 — Bet journal: a chunk index and a persisted rollup

**Series note:** third entry of the *mini-plan* series, following
`mini02-panel-state-and-100k-audit-plan.md` (whose **Part D** this is, promoted to its own plan
exactly as that plan said it should be).

**Status:** 📋 **DRAFT — decisions taken (§6), ready to implement.** Branch created, no code touched.
⚠️ **§6.2 is the headline: lifetime statistics are already silently wrong in any world past 200,000
bets, including the developer's.** ·
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
settles**, instead of derived by scanning — the exact list is §6.4.

They are separable, but **the priority inverted once §6.2 was found**: D1 closes INC-001's *cost*,
D2 closes a *correctness* defect that is already producing wrong lifetime numbers. **If only one
ships, ship D2.**

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

## 6. Decisions (developer, 2026-08-13)

### 6.1 — The rollup lives inside `UserStatsService`

It already owns `Stats`, already self-persists per bet, and is already the single service every
consumer asks. Less surface than a parallel file, and it keeps "the stats" in one place.

### 6.2 — ⚠️ Lifetime stats are ALREADY wrong, and this is what fixes it

**`Stats` is not persisted at all.** `UserStatsService._Ready()` calls `EnsureAllChunksLoaded()` and
then `RebuildStatsFromLoadedHistory()` — so every boot recomputes "lifetime" figures **from the
retained window only**. The instant the first chunk is pruned, every pruned bet disappears from the
totals, permanently, on the next restart.

**This is live, not hypothetical.** The developer's world reached 21 chunks (~210k bets) and had
already pruned `bet_history.jsonl` + `000001–000003` by 2026-08-12. Its lifetime P/L, total wagered
and bet count are understated today by whatever those four chunks held.

**So the rebuild-vs-persist boundary is a CORRECTNESS boundary, not a performance one — and the
system chooses it, not us.** It sits exactly at *"has anything been pruned yet?"*, which with current
settings is **200,000 bets** (`MaxRetainedJournalChunks 20 × MaxJournalEntriesPerChunkFile 10000`),
not at a number we pick. The developer's instinct ("below X rebuild, above X persist") is right; only
X is not free to choose:

| Mode | Condition | Behaviour |
|---|---|---|
| **A — rebuild** | nothing pruned yet (disk holds the whole history) | boot rebuilds `Stats` by scanning, exactly as today. Correct *because* the disk is complete |
| **B — persist** | the first chunk has been pruned | the persisted rollup is **authoritative**; boot no longer derives lifetime figures from disk |

Detecting the switch is exact and cheap: **the oldest retained chunk's index > 0** means pruning has
happened. No heuristic, no counter to keep in sync.

*(1,000,000 could only be the boundary if retention were raised to ~100 chunks, which at ~2.8 MB per
chunk is ~280 MB of journal. Not proposed.)*

**Mode B must be entered before the boundary is crossed, not after.** A rollup that starts counting
only once pruning begins has already lost the pruned bets. So the rollup is maintained **from the
moment this ships**, and its starting values are seeded from whatever the retained window can still
prove — with the shortfall recorded honestly rather than silently absorbed (§6.5).

### 6.3 — Vocabulary: "max martingale level" is dropped

**Retired from the design and from the UI vocabulary.** In its place, two symmetric outcome metrics:

- **Max consecutive losses** (already exists, already correct)
- **Max consecutive wins** (new, its mirror)

Both are pure outcome runs, both need INC-002/§40.8's `(GameId, Chance)` segmentation for the same
reason — a run only means something at a fixed win chance — and neither needs the progression to be
reasoned about. *This supersedes D-M2.10*: `ProgressionTriggerStreak` is no longer harvested for
statistics, and the ladder-depth metric it would have provided is not built.

**Why this is the better call**, recorded because D-M2.10 argued the opposite: ladder depth and
outcome run are *different quantities* that coincide only when nothing resets the progression — and
insist resets, the §25.5 bankroll-limit reset and every auto-recharge break exactly that. INC-002's
whole lesson was that a label which requires reconstructing its definition from the code cannot be
sanity-checked by anyone. Two symmetric, self-describing metrics beat one clever one.

### 6.4 — The persisted figures (v1)

| Figure | Segmented? | Note |
|---|---|---|
| Total bets, wins, losses | no | |
| Total wagered, net P/L | no | |
| Max bet amount | no | |
| Max loss amount | no | largest single loss |
| **Max won amount** | no | **new** — the mirror of max loss, currently not tracked anywhere |
| Max consecutive losses | **per `(GameId, Chance)`** | §40.8's rule |
| **Max consecutive wins** | **per `(GameId, Chance)`** | **new**, same rule |

### 6.4a — Surfacing them (added 2026-08-13, developer spotted the gap)

§6.4 said which figures to **persist** and never said which to **display** — and the two are not the
same list. The `BetsHistoryExplorer` summary was still showing only the loss side, so a figure the
plan had decided to track had no way to be seen. *A metric that is stored but never rendered is
indistinguishable from one that was never built.*

Now stated: the summary line shows **losses and wins as PAIRS** —
`Max loss / won` and `Max consecutive losses / wins`. Beside their mirrors the loss numbers read as
what they are, the two tails of one distribution; alone they read as a verdict on the engine. Both
runs keep the `(at N%)` qualifier when unfiltered and drop it when the chance selector is active
(§40.9), and both obey the same `(GameId, Chance)` segmentation for the same reason.

**Note the two are different quantities and must not be confused:** the summary line is scan-based
and scoped to *"up to the selected date"*; the **rollup** is the lifetime running total. Surfacing
the rollup's own lifetime figures is a *separate* item — the natural home is the **Betting Statistics
scene** (roadmap), not this line, which would otherwise state two different numbers under one label.

### 6.5 — Where `EnsureFullHistoryLoaded` is actually used (answer to Q3)

The full-load call has **one** external consumer, but `BetHistory.EnsureAllChunksLoaded()` has five,
and they are what really has to be replaced:

| Call site | Why it loads everything | Replaced by |
|---|---|---|
| `UserStatsService._Ready()` | rebuild lifetime `Stats` | **the rollup** (§6.2) — this is the boot cost INC-001 named |
| `RollbackHistoryToUtc` | trim history to the checkpoint | **the index** (locate the boundary chunk) + a rollup adjustment |
| `ClearAllHistory` | — | **nothing**: it loads every chunk and then clears them. Free win, delete the load |
| `EnsureFullHistoryLoaded` → `BetsHistoryExplorer` | replay window | **the index** |
| `GetRecentBets(max)` → DiceGame's list seed | newest N records | **the index** — only the newest chunk(s) are ever needed |

**The replacement system** is therefore: the rollup answers every *aggregate* question, and the index
answers every *window* question. Once both exist, no caller needs the whole journal in memory, and
`EnsureFullHistoryLoaded` can go — which is the state Mode B requires anyway.

### 6.6 — The replay window in in-game time, with an automatic clamp (new requirement)

The retained window is currently invisible: the player can pick any calendar date and silently get an
empty or truncated replay, with nothing saying why.

- **Express the window as in-game dates.** The oldest retained bet's `TimestampUtc` is the window's
  floor; surface it as a game-local date so "how far back can I go?" has a visible answer. The index
  supplies this for free — it is the first chunk's first timestamp.
- **Clamp out-of-window picks.** Choosing an earlier date snaps to the oldest stored bet's date rather
  than opening an empty replay, and says so.
- Consumers: `CalendarsNavigator` (the picker) and `BetsHistoryExplorer` (`ExplorerSelectedLocalDateTime`).

*Rationale worth keeping: retention is a storage decision the player never made and cannot see, and
its only user-visible consequence is history that isn't there. A limit that shapes what the player
can do must be shown in the units the player thinks in — in-game dates, not chunk counts.*

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
