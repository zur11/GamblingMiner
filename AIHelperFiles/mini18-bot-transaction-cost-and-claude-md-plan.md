# Mini-Plan 18 — What a bot transaction costs, the zero columns retired, and CLAUDE.md back under budget

**Series note:** eighteenth of the *mini-plan* series, following `mini17-journal-rotation-and-incremental-utxo-plan.md`,
whose close-out named both technical items here as the work it left behind.

**Status:** 📋 **SPECIFIED 2026-10-06**, not started. Proposed branch `mini18-bot-transaction-cost`.

**Three parts, one of them documentation.**

- **A — retire the seven zero columns.** Mini-plan 17's per-block journal trace columns are structurally always
  zero. Debt, named in its own close-out, paid before the next reader mistakes seven zeroes for a result.
- **B — measure the bot-transaction phase, within one session.** It is now the dominant per-block term and nothing
  has ever looked inside it.
- **C — CLAUDE.md back toward budget**, by extraction to documents that already own the material.

---

## 1. What the code says, before changing anything (read 2026-10-06)

### B — the phase that became dominant while nobody was measuring it

Mini-plan 17's run (195 blocks, heights 1221→1415, **game dates 2011-04-27 → 2011-08-30**) measured
`botTransactionsMs` at **median 0.957 ms, mean 76.899, max 469.256** — the largest per-block term by mean, in an era
no earlier plan reached. The whole phase is **one call**: `ScheduleBotTransactionsAfterBlock(block)`.

**It is not transaction volume.** `network_population_trace.csv` gives `txTargetPerBlock` ≈ **0.46–0.52** in that
era — about one transaction every two blocks. So the cost is **decision work**: deciding who transacts, and whether
they can afford to.

**And that work runs through the UTXO set.** `AggregateSpendable(node)` → `GetSpendableUtxos(ownedAddressSet)` is
called for every auction bid cap, every bot affordability check, every company treasury read and every dead-node
sweep — with **13 powered cast miners and 40 companies** live in that era. Nothing counts those calls today.

**The population is what changed, and it is still growing:** `castPowered` was ~5 at height 800 and is **13** at
1415, against a `castTarget` of **17**. Whatever this phase costs per participant, the participant count is not
done rising.

### A — seven columns that can only ever be zero

`jRotations`, `jAppends`, `jScans`, `jDeletes`, `jAppendMs`, `jScanMs`, `jDeleteMs` accumulate only while
`_inBlock`; the journal flushes at **bet rate, between blocks**. All 195 rows read 0. Mini-plan 17's P1 was
answerable only because `NoteJournalOp` *also* accumulates into session totals, printed in the ranked table.

### C — CLAUDE.md, measured rather than estimated

**96,707 characters** (97,550 bytes — the file carries multi-byte `—`, `§`, `⚠`, `✅`, so the two figures differ and
the budget is stated in **characters**). Target 60,000; warning 100,000. Largest blocks:

| block | chars |
|---|---|
| Pattern 2 (checkpoint/durability, four instalments) | 12,871 |
| Pattern 6 (event-driven + the cost-note family) | 11,794 |
| Money Handling → the locale-sweep block | 6,652 |
| Pattern 7 (fifteen standing conventions) | 6,354 |
| UI Layout & Scrolling | 6,105 |
| Scripting tools (no-Python + allowlist) | 5,522 |

---

## 2. The work

### A — delete the seven columns

Remove them from `BlockCostProfiler`'s header, format string and argument list, and **re-verify the header/placeholder
alignment programmatically** (33 → 26 columns) — the check that caught this class of error in mini-plan 17 A1. The
session-total journal breakdown in the ranked table **stays**: it is what answered P1.

### B — the instrument, and nothing else

Split `ScheduleBotTransactionsAfterBlock` into sub-phases and count the UTXO reads it performs:

- **`AggregateSpendable` calls per block**, and **`GetSpendableUtxos` calls per block** (the latter is the lower
  layer and also reachable from elsewhere — counting both says how much of it is this phase).
- **outpoints walked per call** (summed and max), which is what B2's index made proportional to ownership.
- a **sub-phase split** of the bot-transaction phase itself, so "decision work" stops being one opaque number.

**Counts first, milliseconds second and never as a criterion** — mini-plan 16's rule, which mini-plan 17 then
confirmed by finding its own premise refuted on cost while exact on mechanism.

### C — extract, never delete

Two moves, each into a section that already exists and already holds the long form:

1. **The locale-sweep block → `ProjectDesignManual.md` §29.12.** CLAUDE.md keeps: the rule, the detector command,
   the current baseline, and the pointer. The history (5 → 7 → 10 → 12 → 17, the week the tripwire read "regression"
   in silence, the five bug shapes, the bulk-regex incident) goes to §29.12.
2. **Pattern 6's cost-note narratives → `ProjectDesignManual.md` Ch. 38/40 and §29.13**, which already carry the
   cases. CLAUDE.md keeps each as its one-line rule plus the pointer.

**The invariant:** every extracted sentence lands in a named section of an existing document. **Nothing is deleted
outright** — the file is a index of rules, and a rule whose case has vanished stops being persuasive and starts
being folklore.

---

## 3. Predictions, registered before any data

- **P1 — `AggregateSpendable` is called ≥ 20 times per block** in the 2011 era, and `GetSpendableUtxos` at least as
  often. *This is the premise: if the call count is small, the cost is elsewhere in the phase and B's sub-phase
  split is what finds it.*
- **P2 — the bot-transaction phase's cost tracks the PARTICIPANT count, not the transaction count.** Measured as a
  correlation across the run's blocks: calls per block should move with `castPowered`, not with whether a
  transaction was actually built.
- **P3 — outpoints walked per `GetSpendableUtxos` call is far below the UTXO set size**, confirming B2's index is
  doing its job as a complexity claim rather than as a timing.
- **P4 — A changes no measurement.** The remaining 26 columns carry identical values for the same blocks; only the
  seven zeroes are gone.
- **P5 — CLAUDE.md ends at ≤ 88,000 characters** with every extracted passage present in a named `Documentation/`
  section, and the locale detector still at its baseline afterwards (the detector text itself is being moved).

## 4. Decision rules, registered before the data

- **P1 fails (few calls) ⇒ the UTXO reads are not the story**, and B's sub-phase split names what is. Do not fix
  anything on the strength of a plausible mechanism — that is exactly the error mini-plan 17 made and caught.
- **No part of this plan is judged by a per-block millisecond figure, and none is compared across sessions.** If a
  tempting number appears it is recorded as a distribution and decides nothing.
- **B builds no fix.** It measures. Any optimisation it points at is the *next* plan's subject, specified against
  this plan's numbers.
- **C is reverted wholesale if any extracted passage cannot be placed in an existing section.** A half-extraction
  that leaves a rule without its case is worse than the character count it saves.

## 5. Order

1. **A** — delete the columns, re-verify the alignment. One build.
2. **B** — the instrument. One build.
3. **Run** — ~20 minutes at 9000X from the developer's current world, which is **already at game date 2011-08 with
   13 cast miners**. No special setup; Block cost armed.
4. **C** — the extraction, verified by character count and by grepping each moved passage at its destination.
5. **Close out.**

## 6. Out of scope

- **Any fix to the bot-transaction phase.** B measures it; the fix is specified afterwards against real numbers.
- **`TimelineConfig.DevEntryYear`** — the entry-year bootstrap that would build a populated world in one pass.
  **Not needed here** (the developer's world is already in the era) and it costs a full world wipe, since the
  timeline stamp changes. Recorded in `PRIVATE_ROADMAP.md` as the tool for a *reproducible* populated-era world when
  one is wanted.
- **The `BetHistoryItem` timestamp strobe** — a separate roadmap objective, diagnosed but deliberately not bundled
  (the developer's call).
- **T4.2** (one canonical chain instead of ~62 copies), still gated on a post-Basic-Mode fork decision.

---

## 7. Parts A and B as built (2026-10-06)

### A — the seven zero columns are gone

Removed from the header, the format string and the argument list, and the dead per-block accumulators
(`_journalOps`, `_journalOpMs`, `_journalRotations`) deleted with them. `NoteJournalOp` and
`NoteJournalRotation` no longer branch on `_inBlock` at all — the journal's work happens between blocks, so the
branch could only ever take the useless side.

**Verified programmatically: 33 → 26 columns, 26 unique placeholders, max index 25 — aligned**, and no `j*` column
or dead field remains. That is the check that caught this error class in mini-plan 17 A1, re-run because removing
seven columns is exactly when an off-by-one lands.

The field comment now says **do not add per-block journal columns back**, with the reason, because the next reader
will otherwise notice the session totals and wonder why there is no per-block view.

### B — four sub-phases and two counters

**The split follows the method's real structure, and its asymmetry is the hypothesis.**
`TryCasinoBotDonation` runs on **every** block; `TryCastSellFlow` and `TryNonMinerExchanges` run **only when the
budget is positive** — about half the blocks at a ≈0.5 tx/block target, since `if (budget <= 0) return;` sits
between them. **A median of 0.957 ms against a mean of 76.899 has to come from a path that does not run every
time**, and this says which one.

- **`botDonationMs`** — the every-block path.
- **`botBudgetMs`** — closed **whether or not the method returns**, so a zero-budget block still reports what the
  decision itself cost. Timing it only on the expensive path would have made the cheap path look like no path.
- **`botSellFlowMs`**, **`botExchangeMs`** — the two conditional paths.
- **`aggSpendableCalls`** / **`spendableReads`** — `AggregateSpendable` is the wrapper every bid cap, affordability
  check and treasury read goes through; `GetSpendableUtxos` is the layer beneath and is reachable from elsewhere
  too, so **counting both says how much of the lower layer belongs to this phase.**
- **`outpointsWalked`** / **`outpointsMax`** — the quantity mini-plan 17's address index was built to shrink,
  verified as a **count**, which is what P3 asks for.

`BotPhase` is deliberately **separate from `Phase`**: that enum's phases are contiguous (each `Enter` closes the
previous), and slotting sub-phases into it would break that contract for the outer phases. These are timed
independently and may be entered zero or one time per block.

**Verified: 26 → 34 columns, 34 unique placeholders, max index 33 — aligned.** All four sub-phases instrumented
(4 of 4). Build clean on `--no-incremental`; locale detector at baseline (17).

**Still measurement-only.** No behaviour changed in either part. B builds no fix, by §4.

---

## 8. Run 1 results (2026-10-07, 192 blocks, heights ~1416 → 1607)

Fresh build confirmed from the artefact: the trace carries all 34 columns including the eight new ones.
`[UtxoAssert] ARMED` present, **`MISMATCH` zero** — mini-plan 17's oracle still silent over another 192 blocks.

### The split names it in one line: `TryCastSellFlow` is 98.2% of the phase

| sub-phase | total over 192 blocks | share | blocks it ran on |
|---|---|---|---|
| **`TryCastSellFlow`** | **9,990.2 ms** | **98.2%** | **79 (41%)** |
| `TryCasinoBotDonation` | 185.8 ms | 1.8% | 192 (100%) |
| budget arithmetic | 0.5 ms | 0.0% | 192 (100%) |
| `TryNonMinerExchanges` | 0.0 ms | 0.0% | **0 (0%)** |
| unaccounted | 1.3 ms | 0.0% | — |

**The asymmetry hypothesis was exactly right.** `botTransactionsMs` has median **1.067** and mean **53.009** because
the every-block path (donation, median 0.677 ms) is cheap and the conditional path costs **126.5 ms per invocation**
on 41% of blocks. `TryNonMinerExchanges` never ran at all — `TryCastSellFlow` always filled the budget first.

### P1 — ❌ refuted on its threshold, right in magnitude

`aggSpendableCalls`: median **13**, mean **13.786**, max 22. Predicted **≥ 20**. The second clause holds:
`spendableReads` (mean 14.198) ≥ `aggSpendableCalls` (13.786), and that ~0.4/block gap is exactly what counting
both layers was for — a few calls reach `GetSpendableUtxos` from outside `AggregateSpendable`.

### P3 — ✅ confirmed, and it REFUTES this plan's own premise for this phase

`outpointsWalked`: median **0**, mean 7.3, max **42**. Mini-plan 17's address index means a typical spendable read
walks **nothing**, and the worst case touches 42 outpoints. **So the UTXO reads are not where the cost is** — which
is the reasoning that put them in part B's scope. Good: that is what measuring first is for.

### P2 — ⚠ UNTESTABLE in this window, and that is a flaw in the prediction, not a result

`castPowered` is **constant at 13 across all 192 blocks**, so the correlation with it is undefined (NaN).
**A correlation needs variance in its predictor, and this one had none.** `corr(sellFlowMs, txTargetPerBlock)` =
**−0.135**, but that predictor barely moves either, so it is weak support at best. **P2 was written without checking
that the quantity it correlates against actually varies inside a 192-block window** — the next version needs either
a run spanning a cast-size change (the entry-year bootstrap would give one) or a direct per-participant count.

### ⚠ The cause, READ FROM THE CODE and therefore not yet measured

`TrySellFlowSend` — called once per cast miner per invocation — begins with
`FirstBlockHeightMinedBy(record.NodeId, chain)`, which is **a linear scan of the entire chain**:

```
foreach (Block b in chain) if (b.MinedByNodeId == nodeId) return b.Index;
```

With **13 cast miners** and the chain at **1,607 blocks**, that is up to **~20,900 block comparisons per
invocation** — and a miner that has **never mined** scans the whole chain to return `null`. 126.5 ms ÷ 13 ≈ **9.7 ms
per miner**, which is the right order for a 1,600-element scan with a string compare each.

**It grows linearly with chain height**, which is precisely why this phase was invisible at height 400 and dominant
at 1,600 — and why mini-plan 16's and 17's runs never saw it.

**Stated as a code reading, not a measurement**, because mini-plan 17's lesson applies verbatim: *a structural
reading tells you what happens; only a measurement tells you what it costs.* What **is** measured is that
`TryCastSellFlow` is 98.2% of the phase. The attribution *inside* it needs its own instrument to confirm.

**And it is the same shape mini-plan 17 just removed from the UTXO path** — an `O(chain)` scan on per-block code.
That is now twice in two plans, in two different places, which suggests looking for the pattern rather than the
instance: **`grep` for anything that iterates `Chain` inside per-block or per-participant code.**

### Decision-rule outcome

Per §4, **B builds no fix** and this run's numbers specify the next plan rather than authorising a change here. The
fix shape is obvious and cheap (memoise first-mined height per node, or maintain it in the block hook exactly as
mini-plan 17 B1 maintains the UTXO set) — which is an argument for doing it deliberately, with its own prediction
and its own verification, not as an aside.
