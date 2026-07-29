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

**Fix** — **P15.11** (`AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` §8): atomic snapshot
write, a loud `TryLoadSnapshot`, the journal-rebuild rotation fix, a history retention policy, and the
removal of the write-only monthly chunks.

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
