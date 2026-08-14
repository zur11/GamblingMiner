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

## 5. World format bump — **NOT needed** (settled 2026-08-13)

Expected "yes" while drafting; the implementation showed otherwise, and it is worth being precise
about why, because the reflex here is to bump.

- **The rollup is a NEW file** (`user://bet_stats_rollup.json`). Absent ⇒ it is seeded on first run.
  Nothing existing changes shape.
- **The checkpoint DTO gained one NULLABLE field.** A pre-mini03 checkpoint deserialises it as `null`,
  which means "keep loaded state" — the same legacy pattern `CentralBankState`, `PlayerBankState`,
  `CasinoCoinSwapState` and `ScMonetaryLedgerState` already use.
- It **is** in `ResetWorldIfIncompatible`'s delete list, so any *future* bump takes it with the world.

**A bump is for data whose MEANING changed, not for data that appeared.** Nothing here reinterprets
an existing byte.

⚠️ Recorded for whoever does bump next: **a bump wipes `hardware_allocation.json`** and resets the
player to 1 credit. Any performance comparison across a bump must re-establish credits first —
mini-plan 02 lost a 100k run to exactly this (§B.7a). Hardware is free to re-add (P5 is unbuilt).

### 5.1 — Restore ordering is load-bearing (and already correct)

`UserStatsService` is autoload **#2**; `BlockSessionCheckpointService` is **#15**. So the rollup file
is loaded and the Mode A/B decision made *first*, and the checkpoint's block-committed snapshot
overwrites it *after* — which is the required direction, since a block is the only commit. The
reverse order would let a stale file silently win over the committed value. No change was needed, but
**it is an ordering dependency, not a coincidence**, and belongs with the `CentralBankService` note in
`ApplyCheckpointToServices`.

The bet-history rollback runs later still (DiceGame, on scene entry). With nothing pruned it re-seeds
the rollup from the rolled-back journal and reaches the same value; once pruning has begun it
deliberately leaves it alone, so the checkpoint remains the only source. Both paths end at a rollup
describing exactly the world the checkpoint describes.

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

### 6.8 — Pruning must be invisible in the NUMBERS (developer, 2026-08-14)

Demonstrated rather than argued: after the first chunk was trimmed, `BetsHistoryExplorer` showed
**195,562** bets where the true lifetime figure was **205,562**, and the developer read the smaller
number as the total — exactly the confusion §6.4a predicted.

**The rule:** deletion is a storage detail and must not be visible in any statistic. The *only* thing
the player should notice is that the replay cannot go back past the window floor (§6.6), stated in
the calendar as "history stored from ⟨date⟩".

**The correction is exact, not an estimate**, and it rests on two facts holding together:

1. every pruned bet is older than the window floor, and
2. the selection is **clamped** to that floor.

So for any date the player can actually select, the *entire* pruned contribution belongs in the
total — there is no partial case to get wrong. **Displayed = pruned prefix + scan up to the selected
date**, which also preserves the replay: figures still grow as the timeline is scrubbed forward, and
land on the true lifetime value at the present.

`prefix = rollup lifetime − the retained window's own totals`, computed once per load while both are
in hand.

**Two subtleties worth keeping:**

- **The ALL-BETS prefix is taken from the rollup's TOP-LEVEL totals, never by summing segments.**
  Per-segment aggregates were added after the rollup shipped, so a file written by the first version
  carries run maxima with zeroed counts; summing those would report a prefix of zero and put the grand
  total straight back where the pruning left it. The top-level figures have existed since the first
  version, so that path is correct for every file that can exist. *When a record gains fields, the
  reader must still be correct for the records written before them.*
- **A maximum is carried only when the lifetime value EXCEEDS anything still on disk.** That can only
  have come from a pruned bet. When they are equal the record is still retained and the scan will find
  it at the right point in the timeline — claiming it as pruned would let a rewound view display a
  peak that had not happened yet.

Per-chance prefixes come from the per-segment aggregates, so the chance filter is truthful too. A
legacy rollup (no segment counts) degrades to a zero prefix *for filtered views only* — the default
All Bets view stays correct.

### 6.9 — The rollup file is flushed at the block, not on an arbitrary event

`SaveRollupIfDirty` ran only from `FlushHistory()`, which the **delegated** autobet never calls — the
same shape as mini-plan 02's dead PAUSE button. The in-memory rollup and the checkpoint were always
right; the standalone file simply lagged, and the checkpoint restore corrected it on the next launch.
Nothing broke, but two persisted copies where one is routinely wrong is the §39.16 rule-1 trap. It is
now flushed inside `CaptureRollupSnapshot`, i.e. at every block — which is the right moment on the
project's own terms, since a block is the only commit.

### 6.10 — A store cannot report its own completeness (2026-08-14)

§6.2's Mode A/B rested on `HasPrunedHistory()` — "has retention deleted anything?", answered from the
surviving chunk indices. **It cannot be answered that way, and no variant of it can.**

`BetHistoryRepository.RollbackToUtc` **rewrites the journal from scratch**: it recreates the base file
and renumbers chunks from 1. After any rollback, a journal that has lost 10,000 pruned bets is
byte-for-byte indistinguishable from one that never lost a record. The evidence the test needs was
destroyed by the rewrite, so every structural variant has the same hole.

Measured on the developer's world: the rollup reported `IsComplete: true` while the union of the live
journal and an archive (deduped by `BetRecord.Id`) proved **215,550** canonical bets against a rollup
claiming **205,562** — understating by **9,988**. Records *older than the live window floor* cannot
have been rolled back, since a rollback removes the newest, so they are canonical by construction and
the shortfall is certain rather than inferred.

**The fix is to stop asking the question.** The rollup is **seeded once, on creation, and thereafter
adjusted only by the checkpoint** — the thing that actually owns the world's timeline (a block is the
only commit). Nothing re-derives it, ever. Mode A/B is gone; `HasPrunedHistory` survives only as an
`[Obsolete]` marker carrying this reasoning, so it is not re-invented.

**The general rule, which is the durable part:**

> **A store that anything is allowed to REWRITE cannot report its own completeness.** Completeness
> must be tracked by whoever owns the history, not inferred from what the storage happens to hold.

A corollary worth stating separately, because it is what made the earlier design *feel* safe: a
"self-healing" re-derivation is only self-healing while its source is authoritative. The moment the
source can be shorter than the truth, the same code silently *destroys* the truth instead — and it
does so quietly, at boot, with no failure for anyone to notice.

**On the seed's honesty:** a rollup created against a journal that already contains bets is a FLOOR,
not a lifetime figure, and is marked `IsComplete = false` with a `SeededAtUtc`. Only a rollup created
in a world with zero recorded bets can honestly claim completeness.

### 6.11 — Replay Mode: the window becomes a control, not just a limit (2026-08-14)

The window floor was surfaced in the *explorer* but not in the **calendar**, which is where the date
is actually chosen — so the clamp arrived as a surprise on the next screen. It is now stated and
configurable at the point of choice.

**`Replay Mode` (CheckButton, default ON)** in `CalendarsNavigator`, with a label that always names
the replay floor and what the current mode does with it:

| Mode | Calendar floor | Meaning |
|---|---|---|
| **ON** (default) | oldest stored bet | every date the calendar accepts is one the explorer can actually replay |
| **OFF** | `TimelineConfig.PlayerStartDayLocal` | the player has said they are travelling for some reason other than bet history; the world still has nothing before its own start, so it clamps there — instantly |

Flipping the toggle re-applies the floor immediately, so it can never leave the calendar sitting on
a date the new mode forbids.

**The explorer keeps clamping to the replay floor regardless of the toggle** — it cannot show bets
that are not on disk. So with Replay Mode OFF a player may legitimately land on an earlier date and
watch the explorer snap forward. *That is the mode working, and the label says so in advance* rather
than letting it read as a malfunction.

**A second hardcoded genesis date died here.** The calendar clamped at a literal
`new DateTime(2009, 1, 3, 18, 15, 6)` — genesis, months before the player's world exists, so the
clock could be set into the founders' era. It is replaced by `TimelineConfig.PlayerStartDayLocal`,
which is the canonical anchor **and** timeline-shiftable, as every historical date in this project is
required to be. *A duplicated constant is not merely redundant: it is the copy that will not move
when the original does.*

### 6.12 — Stage 2 of D1 was NOT built, and why

Stage 1 removed the boot scan. Stage 2 — "index the chunks so a consumer loads only the one covering
a date" — was then examined against its remaining consumers and **has none that benefit**:

- `UserStatsService._Ready` no longer reads the journal at all (stage 1).
- `GetRecentBets` needs only the newest chunk, which it now loads directly — no index required.
- `ClearAllHistory`'s load was removed outright.
- The **checkpoint rollback** must load everything, permanently: `RollbackToUtc` trims in memory and
  then **rebuilds the journal from memory**, so loading only the tail would rewrite the journal from
  that tail and delete every older chunk. This is now stated in the code so it is not "optimised".
- **`BetsHistoryExplorer`** is the only caller left, and its summary counts bets *up to the selected
  date* — at the present that is the whole retained window, so it must read it. Seeking to one chunk
  saves nothing; only **per-chunk aggregates** (stage 3, which needs run-merging across chunk
  boundaries) would, and the developer measured the explorer as *notably faster* after stage 1.

#### 6.12a — Stage 1 broke the window floor, and one line of index fixed it

Found immediately on test: the calendar reported **"no bets recorded yet"** for a world holding 215,550 of them.  read  — and since stage 1, boot loads nothing, so that list is empty. *Removing a load breaks every reader that was silently relying on it having happened.*

The fix is the one piece of the index that has a real consumer:  opens the oldest segment, takes its first line, and closes — one short read of one file, no format, no persistence. It prefers memory when the journal does happen to be loaded, since that copy is already trimmed by any rollback.

**Note what this does NOT justify.** Seeking to an arbitrary date still has no beneficiary; only the FIRST record did. The distinction is the whole point of §6.12 — build the piece with a consumer, not the machinery that piece belongs to.

**So the rest of the index is deferred with its trigger named:** build it if and only if opening the explorer
becomes slow again, and build it as stage 3 (aggregates) rather than stage 2 (seek), because seeking
alone cannot help the one consumer that remains. *Machinery whose beneficiaries have all been fixed
by something simpler is not "groundwork" — it is inventory.*
