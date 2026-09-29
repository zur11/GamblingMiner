# Mini-Plan 14 — Write the chain once, then stop rewriting it

**Series note:** fourteenth of the *mini-plan* series, following `mini13-block-cost-budget-and-bet-id-plan.md`,
whose T4.6 measurement selected this work and named its first move.

**Status:** ✅ **DONE 2026-09-29**, merged to `main` from `mini14-write-the-chain-once`. **Read §10 first** — it
is the close-out, and it names what the next plan inherits.

**This is the roadmap's T4.5**, and it absorbs the separate "Chain persistence" entry, which described the same
thing. What mini-plan 13 measured, at height ~400:

- a block costs **27.55 ms**, of which **18.39 ms (66.7%) is writing the world snapshot — twice** (exactly 2
  writes in 51 of 51 blocks, 503 KB each);
- the snapshot grows with the chain: **213 KB at height 148 → 503 KB at ~400**, and the phase's time grows with it;
- everything else is small by comparison: UTXO replay 7.4%, the difficulty trace 6.5%, bot transactions 5.0%.

**Two moves, in order of size:**

- **A — write once per block instead of twice.** Pure coalescing, no format change. Expected to remove a third of
  a block outright.
- **B — stop rewriting the chain at all.** Blocks are immutable and append-only; only the mutable remainder needs
  rewriting. This is the change that makes per-block cost stop growing with the world.

---

## 1. What the code says, before changing anything (read 2026-09-29)

**Six call sites write the snapshot**, and a block reaches two of them: `HandleMinedBlock`'s own
`PersistStateToDisk`, and then `CaptureCheckpoint → PersistFinancialState(true) → SetNodeFinancialState(persist:
true)`. The others are the vote-path safety net, first creation of a node's financial state, a
`changed && persist` path, and baseline/bootstrap creation.

**The second write is not redundant in content** — it alone commits the post-bet financial mirrors, the bots'
included — **but the chain half of it is identical to the first.** That is the whole finding: 503 KB written
twice, of which the part that changed is a few hundred bytes of balances.

**What the snapshot holds:** `PlayerChain` (the entire chain, and by far the bulk), `PlayerPendingTransactions`,
`NodeFinancialStates`, `NodeWallets`, the miner streaks, `CompanyFoundings`, `CompanyGovernance`, `BankState`,
`ClosedCompanies`, `BotGovernancePreferences`, `CompanyInflowMultipliers`, and two FBI fields.

**The durability guarantees that already exist and must survive both moves** (INC-001, D-15.26):
the write is atomic (`.tmp` → flush → close → rename); a corrupt read falls back to the staged `.tmp` and, if
that fails too, **aborts the load and latches `_snapshotLoadFailed` so nothing is ever written back over the
file it could not read**. Ch. 40's three questions apply to every new writer this plan creates.

**No forks today.** Divergent chains / reorgs are explicitly post-Basic-Mode (`PRIVATE_ROADMAP.md`), so an
append-only chain store is safe now — and the plan states it as the premise it is, so a future fork plan knows
what it has to revisit.

---

## 2. The work

### A — one write per block

`PersistStateToDisk` becomes a **request**, not a write: it marks the world dirty, and a single flush performs
at most one write per frame. The flush runs at the **top of `SimulationService._Process`, before its early
return**, so it happens whether or not a run is active — an idle-time mutation must not sit unwritten because
nothing is betting. Paths outside the frame loop (bootstrap, baseline creation, world reset, app exit) flush
explicitly at their end.

**What this changes about "a block is the only commit":** the commit still happens per block, a few milliseconds
later, inside the same frame. That widening of the durability window is the one behavioural cost, and it is
stated here rather than discovered later.

### B — the chain leaves the snapshot

- `user://blockchain/chain.jsonl` — **one block per line, appended**, never rewritten.
- `state.json` — everything else, still written whole, still atomically. It is small and does not grow with the
  chain.
- **A tip stamp ties them together:** the state file records the chain height and tip hash it was written
  against. On load, a chain whose tip does not match is a **loud failure that refuses to persist**, exactly as a
  corrupt snapshot does today.
- **A torn last line** (a crash mid-append) is detected on load, truncated to the last complete block, and
  reported loudly — then the tip check above decides whether the world is still coherent.

### C — the traces, only if the measurement says so

Mini-plan 13 measured the per-block difficulty trace at 6.5%, which will become a larger share once A and B
land. Buffering it is small work; §4 decides whether it is worth doing at all.

---

## 3. Predictions, registered before any data

- **P1 — A halves the writes:** `snapshotWrites` reads **1 in every block**, and a block falls from 27.55 ms to
  **≤ 19 ms** at a comparable height.
- **P2 — B makes the write small:** the Snapshot phase drops **below 2 ms**, and `snapshotBytes` per block falls
  below **50 KB** (the mutable remainder), against 503 KB today.
- **P3 — and flat.** This is B's real success criterion, and it closes mini-plan 13's P4, which ran out of range:
  **per-block cost stops growing with height.** Measured across a run spanning at least 300 blocks, the Snapshot
  phase's slope against height is **indistinguishable from zero**, where today it rises with the chain.
- **P4 — the next cost surfaces:** with the snapshot gone, the **UTXO replay becomes the largest phase** of a
  block, since it was second at 7.4% and grows 4.4× per 2.5× of chain.
- **P5 — nothing is lost.** After a deliberate mid-append truncation of `chain.jsonl`, the next launch reports
  the damage loudly, refuses to persist, and does not silently produce a shorter world.

## 4. Decision rules, registered before the data

- **A ships if P1 holds** and no path is left that dirties the world without a flush — verified by a run in which
  every block reports exactly one write.
- **B ships if P2 and P3 hold.** If P3 fails — the cost still grows with height — **the chain was not the only
  thing growing**, and the plan names what is before proposing anything further.
- **P5 is a gate, not a measurement.** If the torn-tail test does not fail loudly, B does not ship, whatever P2
  and P3 say. A faster writer that can lose a world silently is the trade INC-001 already paid for once.
- **C ships only if the traces exceed 15% of a block after B**, and is dropped otherwise, with the number
  recorded so it is not re-proposed from intuition.
- **T4.1 (incremental UTXO) is NOT built here.** Each run reports its share; when it exceeds **40% of a block**,
  it becomes the next plan by the same rule that selected this one.

## 5. Order

1. **Build A** (coalescing + explicit flushes) → one build.
2. **Run A** (~10 minutes, Block cost armed) → P1.
3. **Build B** (the split, the tip stamp, the torn-tail handling, world-format bump) → one build.
4. **Run B** (~30 minutes, spanning 300+ blocks) → P2, P3, P4.
5. **The torn-tail test** (game closed, truncate `chain.jsonl` by hand, relaunch) → P5.
6. **Decide C**, close out.

## 6. Out of scope

- **T4.1 / T4.2 / T4.3** — the UTXO replay, the 62 chain copies, the address index. Measured, ranked, not built.
- **Fork / reorg support.** Append-only assumes no reorgs, which is the current design; a future fork plan
  inherits that premise explicitly.
- **The bet journal.** Mini-plans 12 and 13 finished it: 134.4 bytes a line, bounded in memory, 12.63 µs a bet.

---

## 7. Results — A, one write per block (2026-09-29, 91 blocks, height 120 → 210)

**P1 held on the count, exactly: 1 write in every block, 91 of 91, zero exceptions.** Write time halved,
**18.39 → 9.30 ms**.

| phase | ms | share |
|---|---|---|
| **world write** (one, 230 KB) | **9.30** | 48.2% |
| checkpoint JSON | 4.39 | 22.8% |
| historical events | 2.74 | 14.2% |
| the difficulty trace | 1.55 | 8.0% |
| UTXO replay (10.0 rebuilds over 10.0 nodes, 0.095 ms each) | 0.95 | 4.9% |
| bot transactions | 0.78 | 4.0% |
| **total** | **19.29** | |

**P1's millisecond target is NOT held: 19.29 against "≤ 19".** Missed by 1.5%, and stated as missed. It is also
not a like-for-like comparison — the format-9 bump wiped the world between the two measurements, so this run
sits at height ~163 against mini-plan 13's ~400, where the write was 503 KB rather than 230 KB. **The
comparison that is honest is the one the count gives: the same work, done once instead of twice.**

**Two errors in the first reading of this run, recorded because both were mine and both were caught by the
data rather than by care:**
1. **The trace still held the pre-fix run**, and aggregating "the last 100 rows" mixed the two builds, producing
   a nonsense 1.09 writes per block. Runs must be separated by their timestamp gap before anything is averaged.
2. **A column index was off by one**, turning `utxoRebuildsSincePrev` (10 rebuilds) into "10 ms" and briefly
   making the UTXO replay look like half a block. **A trace's header is the schema; read the position, never
   the position you remember.**

**What A leaves for B, and one thing nobody had measured.** The world write is still **48% of a block and grows
with the chain** — 230 KB here, 503 KB at height 400 — which is exactly B's target. But the **checkpoint JSON is
now the second cost at 22.8%**, and the **historical-events phase reads 14.2%**, both of which were invisible
under the double write. Neither is B's subject; both are recorded so the next ranking starts from measurement.

## 8. Results — B, the chain leaves the snapshot (2026-09-29, 297 blocks, height 119 → 415)

| mean height | total ms | write ms | write KB | checkpoint | UTXO ms | rebuilds |
|---|---|---|---|---|---|---|
| 144 | 14.46 | 3.36 | 57.8 | 6.31 | 0.97 | 11.8 |
| 194 | 8.12 | 2.85 | 57.7 | 5.53 | 0.98 | 8.0 |
| 244 | 8.45 | 3.12 | 57.7 | 5.58 | 1.34 | 8.0 |
| 294 | 8.80 | 2.95 | 57.7 | 5.35 | 1.95 | 8.0 |
| 344 | 8.04 | 2.68 | 57.9 | 1.85 → see note | 1.85 | 8.0 |
| 392 | 8.48 | 2.75 | 57.9 | 5.14 | 2.37 | 7.9 |

*(The first bucket's 14.46 ms carries the early-2009 scripted historical events — 4.74 ms — which fall to
0.03 ms once they are past.)*

**P3 held, and it is the result that matters: the curve is flat.** A block costs **~8.4 ms and stops growing
with height**, against 27.55 ms and rising before this plan. The write is **57.8 KB at every height**, where it
was 230 KB at height 163 and 503 KB at 400. **Mini-plan 13's P4 — unmeasurable then — is answered here: per-block
cost no longer tracks the size of the world.**

**P2 refuted on its thresholds, held in intent:** 2.75 ms and 57.8 KB against "under 2 ms and under 50 KB". What
remains in `state.json` is real content — wallets, financial states, governance, pending transactions — not chain.

**P4 not yet:** the UTXO replay is not the largest phase (write 2.75, checkpoint JSON ~2.4, UTXO 2.37), **but it
is the only one still growing** — 0.97 → 2.37 ms across the run at a constant 8 rebuilds a block, so it is the
per-rebuild cost climbing with the chain. At ~28% of a block it is close to the 40% that §4 set as T4.1's trigger.

### The bug this run's own artefact caught, before the world was reopened

`JsonOptions` carries `WriteIndented = true` — correct for `state.json`, and **wrong for a line-delimited file**.
Every block was therefore written across dozens of lines into a file whose entire contract is one block per
line. The append never failed, the run measured fine, and the file looked plausible; **the next launch would have
parsed every line as garbage, loaded an empty chain, and hit the "state claims blocks that do not exist" path.**

Two things are worth keeping from it:

1. **The guard worked.** That path fails loudly and refuses to persist, so the outcome would have been an
   unusable session and an intact world on disk — not a destroyed one. INC-001's rule paid for itself again.
2. **A writer without a reader is untested, and a format change must be round-tripped inside the unit that makes
   it.** This unit shipped a writer, measured it, and only then looked at what it had written. The measurement
   run exercised nothing but the write path.

Fixed with compact options for the chain's lines, and **world format 10 → 11**, because an unreadable v10 chain
file must be removed rather than met by every future load.

## 9. P5 — the durability gate, and what it caught (2026-09-29)

Two damage cases were staged by hand with the game closed, both backed up outside `user://` first.

**Case 1 — the chain one block ahead of its state** (a crash between the append and the state write).
**Passed functionally:** the extra block was truncated, the file rewritten, and the world loaded and played
normally. **But the developer saw nothing** — not in the Output panel, not in the Debugger's Errors tab. A
`GD.PrintErr` describing damage to a saved world is read once, days later, by someone asking what happened to
their world; landing where nobody looks makes it silence. Fixed: world-integrity findings now print to **both**
panels (`ReportWorldIntegrity`). *CLAUDE.md's "NAME THE PANEL" rule, arriving from the other direction — not
"which panel do I tell the developer to read", but "which panel does this message need to reach".*

**Case 2 — a torn last line** (a crash mid-append). **The detection is exactly right:**

> `The last line of user://blockchain/chain.jsonl is incomplete — a crash during an append. Dropping it; the
> 148 blocks before it are intact.`
> `WORLD LOAD ABORTED — the state says height 148 but the chain file only has 147. Blocks are missing, and
> nothing will be persisted this session so the remaining files are left as they are.`

`chain.jsonl` and `state.json` were untouched afterwards — the refusal held.

### And then the gate failed, one line later

```
[HistoricalBootstrap] First launch — mined genesis → 2009-03-21. Satoshi 109 blocks, Hal 3 blocks.
[Governance] Casino miner-bot stances (drawn for this world)
```

**An empty in-memory chain looks exactly like a new player.** The load aborted, refused to persist — and the
game then fabricated a whole world and handed it over as if it were the player's: a fresh genesis, 112 mined
blocks, new bot stances. Nothing could be written for that chain, so playing on would have produced checkpoints
describing a chain that can never reach disk. **That is INC-001's shape arriving through the RECOVERY path
rather than through a writer**, which is why "refuse to persist" was only ever half a guard.

The session's file timestamps show the split exactly: `chain.jsonl` and `state.json` untouched, while
`calendar_state.json`, `bankroll_state.json`, `principal_balance_state.json`, the rollup and two journal
segments were all rewritten. Those carry the REAL checkpoint's values, so they are not corrupt — but they prove
the refusal covered one store and nothing else.

**Fixed, and the fix is the rule:**
- `NetworkRoot.WorldLoadFailed` is public now, because a failed load is a fact the whole session needs.
- **`HistoricalBootstrapService` refuses to run** on a failed load — a broken world is left visibly broken
  rather than replaced by a plausible one.
- **`BlockSessionCheckpointService` refuses to capture** — the checkpoint and the chain must fail together, or
  the next launch meets exactly the mismatch the tip stamp exists to detect.

**Still open, recorded rather than fixed here:** the remaining eager writers (clock, balances, rollup, journal)
do not consult `WorldLoadFailed`. They wrote real values this time, but "fail closed everywhere" is a pass of
its own and belongs to the roadmap, not to the end of this plan.

**P5's verdict: refuted as a gate, then satisfied after the fix** — and it earned its place. The performance
result (§8) would have shipped a world that replaces itself when damaged.

**P5, re-tested after the fix (2026-09-29):** the same torn file now produces the two detection messages and
then **`[HistoricalBootstrap] SKIPPED — the world failed to load this session`**. No genesis is mined, no world
is fabricated, `state.json` and the checkpoint are untouched. **The gate passes.** (`[Governance] … drawn for
this world` still prints, because the init path draws stances before the bootstrap is reached — nothing is
persisted and the world stays empty, so it is noise rather than a second world.)

---

## 10. Close-out (2026-09-29)

**A block costs 8.4 ms instead of 27.55, and — the part that matters — it no longer grows with the world.**

| | before mini-plan 14 | after |
|---|---|---|
| per block | 27.55 ms, rising with height | **~8.4 ms, flat** |
| world writes per block | 2 | **1** |
| bytes written per block | 503 KB at height 400, growing | **57.8 KB at every height** |

**A** collapsed the double write: `PersistStateToDisk` became a request, one flush per frame performs it, and
the flush runs before each checkpoint so the chain on disk is never older than the checkpoint naming it — an
invariant that used to hold only by accident of ordering. **B** moved the chain into an appended `chain.jsonl`
with a tip stamp in `state.json`, which is what made the cost flat and what answers mini-plan 13's P4.

**Predictions: 2 held, 3 refuted, 1 refuted-then-satisfied.** P1's count held exactly (1 write in 91 of 91) and
its millisecond target missed by 1.5%. P2 missed its thresholds (2.75 ms, 57.8 KB against under 2 and under 50)
and held in intent. **P3 held, and it was the real test.** P4 has not happened yet: the UTXO replay is not the
largest phase but is the only one still growing. **P5 failed as a gate, was fixed, and passed on re-test.**

**Three lessons, each paid for in this plan:**

1. **A writer without a reader is untested.** B shipped a writer, measured it, and only afterwards looked at
   what it had written — which was indented JSON in a line-delimited file, unreadable by its own loader. The
   measurement run exercised the write path and nothing else. **A format change must be round-tripped inside
   the unit that makes it.**
2. **"Refuse to persist" is half a guard.** The other half is refusing to *act as though the world were new*.
   An empty in-memory chain is indistinguishable from a first launch, so the recovery path rebuilt the world
   the writer had just protected. INC-001's shape, arriving from the opposite direction.
3. **A diagnostic about a damaged world has to reach the panel a human reads.** Case 1 passed silently in both
   panels; the finding was real and invisible. CLAUDE.md's "NAME THE PANEL" rule, applied to the message rather
   than to the instruction.

**What the next plan inherits:** the UTXO replay is now the growth that remains — 0.97 → 2.37 ms across 300
blocks at a constant 8 rebuilds, ~28% of a block against §4's 40% trigger for **T4.1**. And **fail-closed
everywhere** (the clock, balances, rollup and journal still write on a failed load) is a roadmap item rather
than a loose end here.
