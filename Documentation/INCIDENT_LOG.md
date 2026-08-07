# Incident Log — Significant Design Crashes

This file records **crashes, corruptions and data-loss events whose cause is a design limitation**, not a
typo. It is deliberately a separate document from the plans and the design manual, for one reason: a plan
records what we decided to build, and the manual records how it works — neither of them naturally records
**the day the thing broke and what the breakage taught us**. That evidence has a short half-life. The save
files get repaired, the logs rotate, and six weeks later all that survives is "there was some crash around
the bank phase".

## What qualifies as an incident

An entry belongs here when **all three** hold:

1. **Something was lost or became unusable** — a world, a playtest, a session, or a persisted figure that
   turned out to have been wrong for an unknown length of time.
2. **The proximate cause is not the interesting part.** A null reference that took ten minutes to fix is a
   commit message, not an incident. An incident is one where the *design* permitted the failure — so the
   same failure will return wearing different clothes until the design changes.
3. **A future phase has to act on it.** Every entry names the phase that fixes it, or explicitly records
   that we accepted the risk.

Near-misses qualify. An incident that was caught before it cost anything is the cheapest kind to learn from.

## Entry template

```
## INC-NNN — <short name> (<date>)

**World / context** — which branch, which timeline, which phase was being played or built.
**Symptom** — what the developer actually saw, in their words where possible.
**Timeline** — what happened, in order.
**Faults** — numbered. Separate the PROXIMATE fault from the ROOT one; they are rarely the same.
**Evidence** — the numbers and log lines that prove it, not the reasoning that suggested it.
**Blast radius** — what was lost, what was recoverable, what had been silently wrong.
**Recovery** — what was done to the world.
**Fix** — the phase that addresses it.
**Lesson** — the rule that would have prevented it, phrased so it applies to something else.
```

---

## INC-001 — The 1.13 GB bet journal and the truncated world snapshot (2026-07-29)

**World / context** — Branch `bank-companies-sc-provisioning`, the **P15.8 calibration playtest** on the DEV
entry-year world (`TimelineConfig.DevEntryYear = 2010`, `WorldFormatVersion 4`, timeline stamp
`CANON-2009-01-03+ENTRY-2010`). The player had landed 2010-03-21 and played forward to **2012-09-21**;
1,666 blocks on the chain, tip at 2012-09-22 18:08:27 UTC. Running at ~9000X. The player held the **leading
bid on First Satoshi Savings** — the first bank company (roster date 2012-09-03) — with about **5 in-game
days left before the auction closed**. That is precisely the moment P15.10's trigger and the whole plan15
credit loop were waiting for.

**Symptom** — In the developer's words: opened the auction details, *"al dar click en el boton para volver a
BlockExplorer la app se colgó. la cerré y volví a abrir pero ahora nada funciona ni se muestra correctamente.
en BlockExplorer todo esta vacio, en DiceGame no aparecen las ultimas apuestas."*

**Timeline**

1. The simulation had been running for hours at 9000X across several sessions.
2. The developer opened `AuctioningCompanyDetails` for First Satoshi Savings and pressed Back.
3. The app stopped responding. The developer force-closed it — **while a block's `PersistStateToDisk()` was
   in flight**.
4. On restart every chain-derived surface was empty: BlockExplorer, wallets, the recent-bets list. The
   money services (which persist to their own files) restored normally, which is what made the failure look
   selective and confusing.

**Faults**

- **F1 — proximate: the world snapshot is written non-atomically, and a corrupt one fails silently.**
  `NetworkRoot.PersistStateToDisk` opens `user://blockchain/state.json` in truncating write mode and streams
  9.25 MB into it. A process death mid-write leaves a half file. Then `TryLoadSnapshot` — **which has no
  try/catch despite the `Try` prefix** — throws `JsonException` out of `EnsureInitialized`, which aborts
  before it has registered a single node. `_isInitialized` stays `false`, every consumer sees an empty
  world, and **nothing is printed**. The world did not fail to load loudly; it failed to load invisibly.
- **F2 — root: the bet-history journal's chunking is defeated by its own rebuild path.**
  `BetHistoryRepository.Flush` rotates every 10,000 lines, but `RebuildJournalFromCurrentState()` — called
  by every `RollbackToUtc` / `ClearAll`, i.e. on every checkpoint restore and every DiceGame entry — points
  the write target back at the **base** file, dumps the entire in-memory history into it with
  `FileMode.Create`, applies **no cap and no rotation**, and **does not delete the chunk files it has just
  duplicated**. Since `GetJournalChunkPaths(includeLegacyBaseFile: true)` then loads base **and** chunks,
  every subsequent boot reads the same records twice and the next rollback writes the doubled set back into
  the base file. It compounds per session.
- **F3 — collateral: `WriteMonthlyChunks` deletes all `blocks-*.json` before rewriting them.** It died
  partway, so `blocks-2012-04.json` is 0 bytes and 2012-05…09 are absent. Harmless only by luck: **nothing
  in the codebase reads those files.** They are pure write amplification (~5 MB per mined block on top of
  the 9.25 MB snapshot).
- **F4 — silent, found during the analysis: the duplicated records were being counted.**
  `UserStatsService.RebuildStatsFromLoadedHistory()` runs over whatever `EnsureAllChunksLoaded()` returned.
  Because base and chunks overlap, **lifetime bets / total wagered / net profit have been inflated for an
  unknown number of sessions.** No one noticed, because there is nothing to compare them against. This is
  §39.16 rule 1 — a persisted figure that lies is invisible and compounds — caught in the wild.

**Evidence**

| Fact | Value |
|---|---|
| `blockchain/state.json` size | **9,256,960 bytes = exactly 2260 × 4096** — a page-aligned truncation, the signature of a killed flush |
| Missing from it | **7 characters**: the closing `  }` and `}`. Brace/bracket balance of the repaired copy: 0/0, no open string |
| Survived intact | `PlayerChain` (1,666 blocks, 2009-01-03 → 2012-09-22), `PlayerPendingTransactions`, `NodeFinancialStates`, `NodeWallets`, `CompanyFoundings`, `CompanyGovernance`, `BankState`, `ClosedCompanies`, `FbiActivated/FbiScFunds`, all four `BotGovernancePreferences` |
| Lost from it | `CompanyInflowMultipliers` only — the ND.8b.5 DEV knobs, which deserialize to empty = ×1.0 |
| `bet_history.jsonl` | **1.126 GB**, ~4,251,000 lines, spanning 2010-03-21 → 2012-08-29 — i.e. the *whole* history in the un-rotated base file |
| `bet_history_000001…114.jsonl` | 293 MB, ~1,081,000 lines, spanning 2010-03-28 → 2012-09-21 — the *same* history again |
| Records deserialized at **every** boot | **~5,330,000** |
| Ratio to the world they describe | 1,666 blocks ⇒ **~3,200 bet records per block** |

The decisive log comparison — a healthy boot (`godot2026-07-28T21.57.51.log`) prints, right after the
wallets load:

```
[WalletInitializationService] Hardware allocation loaded.
[Governance] Casino miner-bot stances (restored with the world):   ← EnsureInitialized completed
[CasinoScBalanceService] Ready — ...
```

The post-crash boot (`godot.log`) goes straight from `Hardware allocation loaded.` to
`[CasinoScBalanceService] Ready`. **The `[Governance]` block is absent and no exception is printed.**

**Blast radius**

- **Nothing permanent was lost.** Because `EnsureInitialized` throws *before* touching any static state, it
  never reaches its closing `PersistStateToDisk()` — so the good snapshot was never overwritten by an empty
  one. Confirmed by mtime: `state.json` still carried the crash timestamp after two restarts. This was
  luck, not design: the same corrupt file reached by a slightly more forgiving code path would have been
  overwritten with an empty world on the next block.
- **The bet history is unrecoverable as a truthful record** — not because it was damaged by the crash, but
  because F4 means it has been double-counting for an unknown period. It was already wrong.
- **The playtest state itself survived**: the chain, every balance, the FED, the casino, all company and
  bank state, and the in-flight First Satoshi Savings auction.

**Recovery** — Executed **2026-07-29** (P15.11a):

- Backup at `%APPDATA%\Godot\GamblingMiner_backup_INC001_2026-07-29\` — 1.43 GB. Everything except the
  journal was **copied**; the 115 `bet_history*.jsonl` files were **moved** into it (same volume ⇒ a rename,
  instant and costing no extra disk), so the deleted history is archived rather than destroyed.
- `state.json` repaired by `TrimEnd()` + `"\r\n  }\r\n}\r\n"`: **9,256,960 → 9,256,967 bytes**, brace and
  bracket depth 0, no open string. Then validated with a **real JSON parser**, not just a balance scan:
  **1,666 blocks**, last index 1666, tip `2012-09-22 18:08:27` UTC, **20** `CompanyFoundings` and **20**
  `CompanyGovernance` entries, 4 bot governance stances, `FbiActivated = true`, and `BankState` empty —
  which is the correct reading, since First Satoshi Savings had not founded yet.
- Deleted: the 115 journal files (moved, above) and 40 write-only `blocks-*.json` (5.2 MB).
- **Save directory: 1.43 GB → 9 MB.** The CSV traces in `logs/` were deliberately kept — they are the P15.8
  calibration record, and the rotated `godot2026-07-28*.log` was kept too, since it is the healthy-boot
  reference that made the `[Governance]` diagnosis possible.

**Known artifact, recorded rather than smoothed over:** the checkpoint file was written for block *N* and the
interrupted snapshot contains block *N+1*, so the restored clock (`2012-09-21 10:47` local) sits about a day
behind the chain tip. The crash landed **between two files' writes** — the same atomicity gap, visible across
files instead of inside one. D-15.26 closes it for a single file; a cross-file transaction is not attempted.

**Fix** — **P15.11** (`AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` §8), shipped 2026-07-29:
atomic snapshot write, a loud `TryLoadSnapshot` plus a writer guarded against ever persisting over a failed
load, the journal-rebuild rotation fix, a retention cap, and the removal of the write-only monthly chunks.

**Follow-up (structural, unscheduled)** — `PRIVATE_ROADMAP.md` §8 **T4, "Simulation-Scale Refactor"**. P15.11
closed the durability half; T4 is the scale half, and it also carries the **progressive frame-rate decay**
reported over the last days of this same run, which P15.11 does not address. Two findings from the T4
groundwork are worth recording against this incident: **(a)** the journal is **player-only** — the four bots
already keep aggregate `ClientBetStats` counters, so the pattern that would have prevented the blowup existed
in the codebase and had never been applied to the player; **(b)** the strongest structural explanation for
the decay is that ~62 per-node `BlockchainService` instances have their UTXO caches invalidated on *every*
block and rebuilt by *full chain replay*, making per-block cost grow linearly with chain length — a run that
starts smooth and degrades. That second one is an unprofiled hypothesis; T4.6 (instrument the per-block cost
budget) exists precisely so the next such question is answered by reading a column.

**Lesson** — Four, in order of how much they generalize:

1. **"A block is the only commit" governs commit *frequency*, and says nothing about commit *atomicity*.**
   We had reasoned carefully for two steps about *when* to write and never once about *what a half-written
   file means*. Every rule about persistence timing needs a companion rule about durability.
2. **A `Try` prefix is a promise.** `TryLoadSnapshot` did not try. When a loader can fail on data the
   player owns, its failure must be louder than its success, and it must never be able to hand back an
   empty-but-plausible object to a caller that will later persist over the original.
3. **Anything that rewrites a file "from current state" must obey the same invariants as the incremental
   writer.** `Flush` rotated; `RebuildJournalFromCurrentState` did not, and it silently won because it ran
   last. When two code paths write the same file, the rules belong to the *file*, not to whichever function
   happened to grow them first.
4. **A subsystem sized for hand-play does not survive being handed a simulator.** The full statement of
   this — the scale limitation this world exposed between 2010-03-21 and 2012-09-22 — is
   `Documentation/ProjectDesignManual.md` **Chapter 40**, and it applies to far more than the bet journal.

---

## INC-002 — The martingale level that could not happen (2026-08-06)

**World / context** — Branch `main`, canonical timeline, `WorldFormatVersion 5` (Step 16 complete). Reported
from the developer's own **long playtest runs**, while repeatedly consulting the *Max Martingale Level
reached* figure in `BetsHistoryExplorer`. Every session in question was played at **50% win chance**.

**Symptom** — In the developer's words: *"al comienzo teniendo sentido pero no sé desde qué punto ese máximo
deja de medirse correctamente… sé que un nivel de 30 es muy difícil o quizá hasta imposible de alcanzar, sin
embargo he llegado a ver máximos de más de 100 y sé por lógica básica que esto no puede suceder."*

That last sentence is the whole incident. The developer diagnosed it from the arithmetic alone, before any
code was read: at 50% a run of 100 has probability 2⁻¹⁰⁰. **A figure that cannot happen was being displayed
as a measurement, and had been for an unknown number of sessions.**

**Timeline**

1. The metric had existed, unchanged and unchallenged, since `BetsHistoryExplorer` was written.
2. INC-001 (2026-07-29) established that the journal had been loading every record 3–4× and that this had
   silently inflated the lifetime stats. **The write path was fixed at P15.11; no reader was hardened**, and
   the streak metric — the reader most sensitive to duplication in the entire project — was never revisited.
3. Over the Step 16 long runs the developer kept reading the figure, watched it drift from plausible to
   absurd, and raised it.

**Faults**

- **F1 — the amplifier: duplicated records + tied timestamps + a stable sort put the copies side by side.**
  `BetsHistoryExplorer` builds its working list with `OrderBy(r => r.TimestampUtc)`. LINQ's `OrderBy` is
  **stable**, and bet timestamps collide *heavily* — the calendar advances once per frame while the
  simulator settles many bets in that frame, measured at **~3.1 bets per distinct timestamp**. So duplicate
  copies of a bet do not scatter; they land **adjacent**, and a run of L losses is rendered as k×L for a
  duplication factor k. This is the mechanism that converts INC-001's F4 ("the lifetime totals are
  inflated", a proportional error nobody can see) into a **visibly impossible** number.
- **F2 — root: nothing on the READ side defended against a duplicate.** `BetRecord.Id` is a Guid, written
  on every journal line since the journal existed, and **read by nothing**. The check that makes the entire
  bug class impossible was one `HashSet.Add` away and had been available the whole time.
- **F3 — independent, and would survive a perfectly clean journal: the metric was not measuring what its
  label claimed.** `AdvanceSummaryTo` counts consecutive `Loss` records over the **entire loaded history**,
  with no reset on a change of `GameId`, a change of `Chance`, a session boundary, or — most importantly —
  **a progression reset**. `InsistAfterStopOnLoss` (then named `InsistAfterStop`), the bankroll-limit reset and every auto-recharge put the bet
  back to base while the loss run kept counting straight through. It also added the closing win to the run
  on a win but not on a trailing loss, so the same streak reported two different values depending on where
  the viewed window happened to end.
- **F4 — no bound was ever asserted.** The figure is one of the few in the project with a *closed-form*
  plausible maximum (`≈ log(n)/log(1/p)`). Nothing compared it to that, so the only detector this defect
  ever had was a human being surprised by it.

**Evidence** — measured read-only over the INC-001 archive
(`%APPDATA%\Godot\GamblingMiner_backup_INC001_2026-07-29\`), 114 chunk files:

| Fact | Value |
|---|---|
| Real bets, all at `Chance=50` | **1,081,554** |
| Wins / losses | 540,898 / 540,656 — **win rate 0.5001** |
| **True** max consecutive-loss run | **19** (theory for n≈1.08M: `log₂n ≈ 20`) |
| Timestamp collisions | 10,000 records → **3,180 distinct timestamps** (~3.1 bets each) |
| Duplication in the base file | every record `Id` in the sampled window appears **exactly 3×** — 30,012 rows, 10,004 distinct ids — plus a 4th copy in its chunk |
| Same window, chunk alone | max loss run **12** |
| Same window, merged and stable-sorted **the way the explorer does it** | max loss run **36** |

The dice engine is exonerated by the second and third rows: 0.5001 over a million bets, and a maximum run
landing exactly on the theoretical expectation.

**Blast radius**

- **No world, session or balance was lost.** This is a pure *reporting* fault: nothing downstream consumes
  the streak figure.
- **The figure itself has been untrustworthy for an unknown period** — certainly through every session that
  ran on a duplicated journal (INC-001's whole span), and by F3 it was never a martingale-level measurement
  even on clean data.
- **Not verifiable for the specific runs reported.** The world was reset on 2026-08-05/06 and
  `bet_history*.jsonl` is in the world-reset delete list (`NetworkRoot.cs`), so the journals that produced
  the >100 readings no longer exist. The mechanism is proven on the archive; **which** duplication source
  fed those particular runs is not, and the guard shipped below is what will answer that next time — by
  name and count, at load.

**Recovery** — none needed; nothing was corrupted on this occasion. The archive was read only.

**Fix** — shipped 2026-08-06, three parts, all in this commit:

1. **`BetHistoryRepository` deduplicates by `BetRecord.Id` at every entry point** — the journal loader, the
   legacy-snapshot loader and live `Add`. Skips at load are counted and reported with `GD.PushWarning`; a
   live duplicate `Add` is refused with `GD.PushError`, because at that point the caller is the bug. The id
   index is rebuilt after `RollbackToUtc` so a legitimately-truncated bet can be re-registered.
2. **The metric is segmented and honestly named** — the run resets on any change of `(GameId, Chance)`, the
   closing win is no longer added, and the label reads **"Max consecutive losses: N (at C% chance)"**.
3. **`AssertLossRunIsPlausible`** (`[Conditional("DEBUG")]`) compares the reported run against
   `log(n)/log(1/p) + 12` for the segment that produced it — ~2⁻¹² false-positive rate — and prints the
   expected value, the bound and a pointer to this entry.

**Lesson** — Three, in order of how much they generalize:

1. **Fixing a writer does not fix the readers that trusted it.** INC-001 closed the duplication *source* and
   explicitly recorded that the lifetime stats had been inflated by it — then stopped. The one reader whose
   output amplifies duplication instead of merely scaling with it was left untouched for a week, and it was
   the reader a human could actually catch. **When an incident names a corrupted input, enumerate everything
   that consumes it; the loudest consumer is the one worth hardening first.**
2. **A metric with a closed-form bound must assert it.** This is §39.16 rule 1 ("never let a displayed
   figure diverge from reality") in its easiest possible case: the dice's own probability model tells you,
   in one line, what the number cannot exceed. Where such a bound exists and is cheap, *not* asserting it
   means the metric's only detector is a person noticing — which here took an unknown number of sessions,
   and worked only because the developer knew the domain well enough to be surprised.
3. **A label is a claim about semantics, and it gets audited far less often than the arithmetic under it.**
   "Martingale level reached" was wrong independently of the duplication: the progression resets in three
   documented ways that the counter ignored. The number was inflated *and* mis-named, and the mis-naming is
   what made the inflation hard to reason about — nobody can sanity-check a figure whose definition they
   have to reconstruct from the code. **When a displayed name states a domain concept, verify the code
   computes that concept, not merely something correlated with it.**
