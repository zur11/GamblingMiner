# Mini-Plan 26 — The per-block freeze: what one bot transaction costs, measured inside the send

**Series note:** twenty-sixth of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 1**, inserted by
mini-plan 25's measurement F1 (`step17-ui-access-and-event-driven-audit-plan.md` §5.5 "F1 results", §10.1, §11.4), and
the two 📝 notes under §8 **T4**.

**Status:** 🔨 **IN PROGRESS.** Specified 2026-10-20 (`68d6b9f`, all of §11 approved by the developer). **Unit 1, Part
A's instrument, is built** (§3.5). §7 step 1 is done: F1's two traces are archived. Next: the developer runs **M1**
(§7). Part B, the fix, is specified from M1's data as an amendment to this file (§8), with its own predictions
registered before its code.

**Branch:** `mini26-per-block-freeze` (proposed, D-26.1).

**World treatment: no bump, no wipe.** Part A adds DEBUG instruments and nothing that persists. Every fix shape §8
names is an in-memory memo of a pure function, so none needs a `WorldFormatVersion` bump. **If a fix turns out to
need persisted state, the plan stops and asks.** The runs advance R3's world (v12, ~2015-07, the player at 10
credits), as F1's did. *If a wipe is ever proposed, `user://logs/rank_calibration_trace.csv` is renamed
`rank_calibration_R3.csv` first.*

**Prior art this plan consumes rather than re-derives:** mini-plan 13 (`BlockCostProfiler`, T4.6) · mini-plan 18
(the four `botTx` sub-phases, and a named cause that §2.4 re-examines) · mini-plan 17 (the address index, and B1's
"maintain it on append" shape) · CLAUDE.md Pattern 6's six instrument rules and its closing rule.

---

## 1. Why this plan exists

F1 measured R3's world at h3,451–3,495 (2015-06-06 → 07-08). Every scene freezes at every block: a mean of
**2,684 ms per block**, and **44 of 45 blocks over 1 s**. A frame that long is a stall, a frozen game. A Stop pressed
during such a block waits for it. **83%** of the cost is `botTx`, the phase that runs
`NetworkRoot.ScheduleBotTransactionsAfterBlock`. Governance adds spikes of up to 3.7 s.

The plan starts with measurement, because the one cause ever named for this phase (mini-plan 18, §2.4) was a code
reading and was never measured. And §2 shows that F1's own rows already contradict two of the readings now in the
roadmap.

---

## 2. What F1's own rows already say (read 2026-10-20; existing data, not a new measurement)

F1's 45 rows are still in `user://logs/block_cost_trace.csv`. Since mini-plan 18 that file has carried a four-way
split of `botTx` (`botDonationMs`, `botBudgetMs`, `botSellFlowMs`, `botExchangeMs`), plus three UTXO-read counts.
F1's write-up quoted only the outer phase.

### 2.1 — A correction: the worst block is 8,506 ms, not 6,941

The worst block in F1's window is **h3,483: 8,506 ms**. h3,486 took 8,309 ms. **6,941 ms is h3,495, the last block.**
F1's sentence on "the slow stop" was about that block, and the number then slipped into "worst". The mean (2,684) and
the 83% share check out against the rows. **Corrected in the same commit as this plan, at all three sites:** the
step-17 plan's F1 results, and the roadmap's ▶ NEXT row and T4 note.

### 2.2 — `botTx` is three costs with three shapes

| Sub-phase | Blocks it ran on | Cost | Shape |
|---|---|---|---|
| `TryCastSellFlow` | **45 / 45** | mean **1,435 ms** per block | the floor, on every block |
| `TryNonMinerExchanges` | **8 / 45**, all from h3,481 on (8 of the last 15) | **1,674–6,939 ms** per run, mean ≈ 3.7 s | the spikes |
| `TryCasinoBotDonation` | 45 / 45 | usually 10–20 ms; **5 blocks over 200 ms** (291–1,523) | rare spikes |
| budget arithmetic | 45 / 45 | 0.012 ms | nil |

Outside `botTx`, governance was over 200 ms on **17 of 45 blocks** (max 3,660 ms). Auctions and checkpoint are ~30 ms
each. **Mini-plan 18's P3 still holds, and it clears the UTXO reads again:** ~105 spendable reads per block walk
~15,600 outpoints in all. That is far too little work to cost seconds.

### 2.3 — The "doubling in three game-weeks" is a change of regime, not a curve

Per visit, split by sub-phase (ms per block):

| Visit | Heights | Total | Sell-flow | Exchanges (blocks) | Donation | Governance | Total without exchanges |
|---|---|---|---|---|---|---|---|
| DiceGame | 3,451–3,457 | 1,586 | 1,178 | 0 (0) | 310 | 19 | 1,586 |
| ClientsBetsHistory | 3,458–3,464 | 2,475 | 1,488 | 0 (0) | 437 | 483 | 2,475 |
| BotPlayHistory | 3,467–3,477 | 1,860 | 1,442 | 0 (0) | 13 | 337 | 1,860 |
| DiceGame | 3,478–3,484 | 3,408 | 1,303 | 1,647 (3) | 15 | 375 | **1,762** |
| ClientsBetsHistory | 3,485–3,492 | 4,001 | 1,500 | 1,547 (3) | 15 | 869 | 2,454 |

- **The sell-flow is flat across the window**, at 1,178–1,500 ms. **DiceGame's doubling, 1,586 → 3,408, is entirely
  the arrival of exchanges.** Without them that visit reads 1,762.
- **What changed is the target.** `network_population_trace.csv` puts `txTargetPerBlock` at **5.26** at h3,451,
  **7.80** at h3,472 and **12.15** at h3,494. The exchanges run only when the cast's sell-flow cannot fill the
  budget (`created < budget`). So they begin once the historical tx volume outgrows the cast, and they should run on
  more and more blocks as 2015's volume keeps rising (P6).
- **Growth across eras is still real.** The sell-flow was **281 ms in 2013** (§10.1) and is ~1,435 ms now. The cast
  grew from 13 to 22 powered miners, and the target rose. That growth cannot be tested inside one session (Pattern 6
  instrument rule 5: a 50-block window moves neither predictor). §5 tests the per-send mechanism instead, which
  turns era growth into arithmetic.

### 2.4 — Mini-plan 18's named cause does not survive its own arithmetic

Mini-plan 18 measured `TryCastSellFlow` at 98.2% of the phase in 2011. From the code it read the cause as
`FirstBlockHeightMinedBy`, a linear chain scan per cast miner, "~9.7 ms per miner". **That cause was never measured
and never fixed, and the scan is still in the code.** Two things argue against it:

- **The arithmetic.** At h1,600 a scan is ~1,600 ordinal string compares, which takes microseconds, not ~10 ms. Even
  today's 23 cast miners × 3,495 blocks is about 80,000 compares per block.
- **The figure itself.** Its 126.5 ms per invocation came at a target of ≈0.5 tx/block, i.e. **~1 send** per
  invocation. F1's 1,435 ms per block is the same order **per send**. The cost follows *sends*, not miners × chain.

This is a structural reading, so it is tested, not asserted: **P5** measures the scan directly.

### 2.5 — What one send does (code reading, 2026-10-20)

Every automated spend goes through **one primitive**, `NetworkRoot.BuildAndBroadcastUtxoSpend`: the sell-flow, the
exchanges, the casino-bot bids, and governance's `DIVIDEND`, `COLLATERAL`, `DEBT SERVICE`, conversions and refunds.

| Stage | Code | Runs | Grows with |
|---|---|---|---|
| **gate** (before the send) | `FirstBlockHeightMinedBy`; `AggregateSpendable`, which copies the owned-address set | per **attempt** | chain blocks; addresses owned |
| **select** | `OwnedAddressSet` (another copy) → `GetSpendableUtxos` → `SelectUtxos` (a sort plus a key string per UTXO) | per send | addresses; UTXOs held |
| **keys** | `TryResolveInputKeys` → `DeriveSigningContext`, **uncached**: SHA-256, a P-256 key from the seed and its export, and a secp256k1 public key by **`BigInteger` scalar multiplication** with a Fermat inverse | per **input** (except a base-address input) | inputs |
| **sign** | `ComputeTransactionId`, `BuildTransactionPayload`, and `CryptoUtils.Sign`, which re-imports the PKCS#8 key each time | per input | inputs |
| **local admission** | `AddTransactionToPendingTransactions` at the sender | per send | (as below) |
| **fan-out** | `BroadcastTransaction` → `AddTransactionToPendingTransactions` at **every other node** | per node, per send | **nodes** × (below) |
| — dup scan | `ContainsTransactionId`: a LINQ walk over **every confirmed transaction**. A new txid never exits early | per admission | **chain txs** (12,897 at h3,495) |
| — signature | txid recomputed; per input, the address re-derived (SHA-256 + RIPEMD-160 + Bech32), then a P-256 key import and verify | per admission × input | inputs |
| — UTXO checks | `CollectPendingSpentOutpoints`, rebuilt on every call, plus dictionary lookups | per admission | mempool inputs |

**Sizes, read from the world** (read-only): ≈**72 nodes**, so a fan-out is ~71 admissions. That is 4 casino bots + 40
non-miners + 23 cast in `bot_wallet_registry.json`, plus the player, the casino and the founders, and V1 makes it a
count. Inputs per transaction over F1's 417 non-coinbase transactions: **345 have 1**, 23 have 2, 24 have 3–5,
**10 have 6–10, 12 have 11–30, 3 have 31+** (the largest is a `DEBT SERVICE` with 42). The governance-labelled
spends are fatter: `DIVIDEND` 3.8 inputs on average, `COLLATERAL` 2.95.

`MempoolChanged`, fired once per send, is ruled out. Its only subscribers are scenes that set a dirty flag.

### 2.6 — The arithmetic does not close, which is the reason to measure

My per-unit estimates are **untimed** guesses wearing numbers (the closing rule). They are written down so M1 can
refute them:
- a P-256 import plus verify of ~0.2–0.5 ms per input per admission, so ~15–35 ms per input for a whole fan-out;
- a dup scan of ~0.1–0.2 ms per admission, so ~7–15 ms per send;
- a few ms per input for key derivation.

That sums to **tens of ms per single-input send**. The rows say otherwise. The whole network confirms ~9.3
non-coinbase transactions per block, and the sell-flow alone costs 1,435 ms. Even if every one of those were a
sell-flow send, that is **~150 ms per send, 3–10× the estimate.** So either the unit costs are several times larger
than I think, or the time goes somewhere the stage list does not show: attempts that never become sends, or a stage
I have not seen. **The instrument therefore counts attempts by outcome, not only sends, and times the gate apart
from the send.**

---

## 3. Part A — the instrument (unit 1): extend the two profilers, write no new one

### 3.1 — `BlockCostProfiler`: the send, stage by stage

- **A send context.** Each send records the context it ran in: `SellFlow`, `Exchange`, `Donation`, `Governance`,
  `Auctions`, `Other` (another in-block phase), or `OutsideBlock` (player sends and swap legs between blocks, which
  go into session totals only). The context is the open `Phase`. Inside `BotTransactions` it is the bot sub-phase,
  through a new `MarkBotPhase(BotPhase)` call at the start of each sub-phase. `NoteBotPhase`'s contract is
  unchanged.
- **Send stages, contiguous** like `Enter`: `Select → Keys → Sign → LocalAdmit → FanOut`. They are opened by
  `BeginSend(context)` inside `BuildAndBroadcastUtxoSpend` and closed by `EndSend(outcome, inputs, outputs)`. The
  outcome is `Sent`, `SelectFailed`, `KeysFailed` or `AdmitFailed`.
- **Admission sub-stages**, timed inside `BlockchainService.AddTransactionToPendingTransactions`: `DupScan`,
  `Signature`, `UtxoChecks`. Two **counts** go with them: transactions scanned by `ContainsTransactionId`, and
  signature verifications. They are attributed to the send stage that is open (`LocalAdmit` or `FanOut`). An
  admission with no send open is counted on its own, and a non-zero count is a finding.
- **Attempts and the gate.** `TryCastSellFlow` and `TryNonMinerExchanges` count attempts by the gate that ended them:
  never mined, warmup, low balance, amount + fee, self, or reached the send. Each attempt is timed whole, and the
  gate's time is the attempt's time minus the send nested in it. `FirstBlockHeightMinedBy` gets its own time and a
  count of blocks scanned.
- **Allocation and GC per block:** `GC.GetAllocatedBytesForCurrentThread()` and `GC.GetTotalPauseDuration()`
  (.NET 7+; this project targets net8.0) are read at `BeginBlock` and `EndBlock`, plus the gen-0/1/2 collection
  counts.

**New per-block columns** (the header changes, so the old file rotates, see §7 step 1): `budget`, `sellAttempts`,
`sellSends`, `exchAttempts`, `exchSends`, `gateMs`, `firstMinedMs`, `firstMinedBlocksScanned`, `sends`, `sendFails`,
`sendInputs`, `sendInputsMax`, `sendMs`, `selectMs`, `keysMs`, `signMs`, `localAdmitMs`, `fanOutMs`, `admissions`,
`dupScanMs`, `txsScanned`, `sigCheckMs`, `sigVerifies`, `utxoCheckMs`, `keyDerivations`, `govSends`, `govSendMs`, `donationSends`,
`donationSendMs`, `exchSendInputs`, `sellSendInputs`, `allocMB`, `gcPauseMs`, `gc0`, `gc1`, `gc2`.

### 3.2 — One row per send: `user://logs/send_cost_trace.csv`

A second trace from the same instrument, so P4 (cost against inputs) is read exactly rather than from block
averages. Columns: `reportUtc`, `chainHeight`, `context`, `inputs`, `baseAddressInputs`, `outputs`, `outcome`,
`totalMs`, `selectMs`, `keysMs`, `signMs`, `localAdmitMs`, `fanOutMs`, `fanOutNodes`, `fanOutAccepted`. At ~10–20
sends per block that is a small file. It is a `user://logs/*` trace, so it is exempt from `WorldWriteGuard` by class
and is not world state (Pattern 2).

**A session summary in the Output panel**, every 50 blocks and on disarm: sends per block, inputs, each stage's share
of send time, the admission split, the per-context totals, attempts per send, and `FirstBlockHeightMinedBy`'s share
of the sell-flow. It is printed with `GD.Print`.

### 3.3 — `FrameCostProfiler`: what the gen-0 GC on every frame costs

F1 counted "frames with a GC" at ≈600 of 600 and nothing more. Each report window gains: **GC pause time**
(`GC.GetTotalPauseDuration()` deltas) and its share of frame time, gen-1 and gen-2 counts, **MB allocated per
frame**, and the share of it allocated inside the sim segments. **Pause and allocation are attributed exactly like the
period:** committed frames go into the window's figures, and stall periods go onto the stall line. So neither
filter hides the other (instrument rule 1).

### 3.4 — Constraints on unit 1

- `[Conditional("DEBUG")]`, off by default, armed from the existing **"⏱ Block cost"** and **"⏱ Frame cost"**
  toggles. Everything prints with `GD.Print`, never `GD.PrintErr` alone.
- **The admission path is hot, about 72 calls per send.** Its timestamps live under `#if DEBUG`, or are passed
  inline as arguments of a `[Conditional]` call, whose evaluation is removed with it. So the ExportRelease build
  carries no instrumentation work there.
- **Instrument cost: not timed.** It is estimated at ≤ 1 ms per block (~6 timestamps × ~72 admissions × ~15 sends),
  and that is an estimate, stated as one.
- Both builds at 0 warnings. The locale detector stays at **17/0/0/0**: every new string goes in
  `string.Create(CultureInfo.InvariantCulture, …)` with its `+`-chain within three lines of the wrapper. Otherwise
  the baseline is updated in the same commit.

### 3.5 — As built (unit 1, 2026-10-20)

**Where the marks are.**
- `BuildAndBroadcastUtxoSpend` carries the five send stages. So do the two paths that build their own spend: the
  casino-pool payout (`DistributePoolEventAsSingleTx`, in the CasinoRewards phase, so `OtherInBlock`) and the
  dividend batch (Governance). Every exit closes its send.
- `ScheduleBotTransactionsAfterBlock` marks each bot sub-phase and notes the budget.
- `TrySellFlowSend` and the exchange loop wrap each candidate in `BeginSendAttempt` / `EndSendAttempt`. A local
  `gate` names the check that ends it, and a `finally` reports it on every exit.
- `AddTransactionToPendingTransactions` became a timed wrapper around `TryAdmitToPending`. That method holds the
  rules, unchanged except for two span marks.

**Release cost.** `BlockCostProfiler.Clock()` returns 0 outside DEBUG. The admission wrapper's DEBUG work sits
under `#if DEBUG`, and everything else is a `[Conditional("DEBUG")]` call, whose arguments are not evaluated in a
release build. Both builds have 0 warnings, and the locale detector reads **17/0/0/0**. The 17 hit lines are
identical by content to `HEAD`'s, so the count did not hold by coincidence.

**Where the build differs from §3, stated rather than smoothed over:**
- **V2 is a plausibility check, not an independent count.** `txsScanned` is *derived*: a txid that passes the
  duplicate check was compared against every pending and confirmed transaction, because the scan has no early exit
  on a miss. Counting inside the LINQ walk would change the cost being measured. V2 therefore reads as **ns per
  transaction scanned**, which must be plausible (single to low double digits).
- **Three columns more than §3.1 listed:** `orphanAdmissions` (admissions with no send open; expected 0), and
  `sellSendMs` / `exchSendMs`, which P1 needs per context.
- **Instrument work inside the timed span.** The confirmed-transaction count behind V2 is computed once per node per
  chain version, so ~72 walks of ~3,500 blocks per block, inside the fan-out stage. Estimated (not timed) at
  ~0.2–0.4 ms per block. Each send also appends one struct to a list. The rows are formatted and written only after
  the block's timing has closed.
- **The pool payout's invalid-payout exit** is closed as `SelectFailed`, so the send is not left open. It is not a
  stage failure, and it is the rare path.
- **A report's own allocation and GC are billed to no frame.** FrameCost reads the allocation and pause counters
  after its commit, exactly as it already did for time.

---

## 4. Instrument self-checks: read these before any prediction (Convention 16, the boring check)

| # | Check | Expected |
|---|---|---|
| **V1** | admissions per `Sent` send (local + fan-out) | **= the node count**, printed once on arm (≈72) |
| **V2** | `txsScanned` per admission of a new txid | **= the confirmed transaction count at that height** (12,897 at h3,495, rising) |
| **V3** | `sigVerifies` | **= Σ inputs × admissions that reached the signature step** |
| **V4** | Σ send-stage ms ≤ `sendMs`, and bot-context `sendMs` ≤ `botTransactionsMs` | the accounting closes; the remainder is printed, never hidden |

**If any of them fails, the instrument is fixed before a single prediction is read.**

---

## 5. Predictions, registered before any data

Each prediction reads M1's trace. "Expected" is my leading guess and "rival" is the alternative it is set against.
I will state both as refuted if they fail.

| # | Prediction | Threshold | Stance |
|---|---|---|---|
| **P1** | **Sends, not decisions.** In the bot phase, time inside the send primitive is the cost | send time **≥ 80%** of `botTransactionsMs`, and gate time **< 10%** | expected (rival: attempts that never send, §2.6) |
| **P2** | **The fan-out is the send** | non-sender admissions **≥ 50%** of send time, all contexts | expected (rival: `Keys` ≥ 50%) |
| **P3** | **Inside admission, the signature check dominates** | `Signature` **≥ 50%** of admission time, `DupScan` **≤ 25%** | expected |
| **P4** | **Cost is linear in inputs** | mean ms per input of sends with ≥ 6 inputs within **0.5×–2×** of single-input sends'. **Precondition (rule 5): ≥ 20 sends with ≥ 6 inputs in the run** (F1's window had 25 such transactions). Below that it is untestable, and said so | uncertain |
| **P5** | **Mini-plan 18's cause is not the cause** | `firstMinedMs` **< 1%** of `botSellFlowMs` | expected (refutes mini-plan 18 §8's reading) |
| **P6** | **Exchanges are fat sends, and spreading** | exchange sends carry **≥ 5×** the mean inputs of sell-flow sends, **and** exchanges run on **≥ 20%** of the run's blocks | expected |
| **P7** | **Governance spikes are sends** | on blocks with `governanceMs` > 200, the send primitive holds **≥ 50%** of governance time | genuinely 50/50: dividends are sends, but votes and the 3 MB governance trace are not |
| **P8** | **Donation spikes are sends** | on blocks with donation > 200 ms, sends hold **≥ 50%** of donation time | expected |
| **G1** | **The every-frame GC is cheap** | GC pause **< 2%** of committed frame time | expected |
| **G2** | **It is the bets' allocation** | **≥ 1 MB** allocated per committed frame, **≥ 70%** of it inside the sim segments | expected |

---

## 6. Decision rules, registered before the data

- **R1: the fix target.** The send stage with the **largest share** of send time across the run is Part B's first
  target. Every stage at ≥ 20% is in scope; below 20%, out. **A fix must be exact:** a memo of a pure function, or
  an index maintained on append (§8). If the top stage has no exact fix (for example, the fan-out's *width* is the
  cost, which only T4.2 removes), the plan **stops and brings the choice to the developer**. It does not start T4.2
  inside a mini-plan.
- **R2: P1 refuted.** If the remainder outside the send is ≥ 20%, that remainder gets its own breakdown before any
  fix. **No fix on a guess.**
- **R3: governance and donation.** If P7/P8 are confirmed, the same fix covers them, verified by the same counts. If
  refuted, governance gets a breakdown unit of its own (`TickCompanyGovernance`'s sub-phases) and no fix inside this
  plan.
- **R4: the GC.** If G1 holds, the every-frame GC is recorded as benign in T4 and closed, whatever G2 says. If G1
  fails, the allocation hunt becomes a Part B unit **after** the freeze fix, with its own predictions.
- **R5: P4 untestable.** If fewer than 20 fat sends occur, the per-input question is answered by `sigVerifies` and
  `keyDerivations` as counts instead, and P4 is reported untestable.

---

## 7. Measurement M1: protocol (the developer runs it; I read the files)

**Prediction first:** §5, all of it. **The developer watches nothing while it runs** (developer, 2026-10-20, restating
the lifecycle rule that Claude reads the logs itself). Every check below that used to be "watch the Output panel" is
read afterwards from `user://logs/godot.log`, which receives the same `GD.Print` lines, and from the CSV traces. The
run's length is set by time, not by a print. The block count is read from the trace, and if it falls short of 100
blocks, Claude asks for a second session rather than reading a short run as a full one.

**What Claude checks afterwards, in `godot.log`:** `[Build] DEBUG` (a DEBUG build ran) · `[BlockCost] ARMED` with the
node count (V1) · `[FrameCost] ARMED` · the `[BlockCost] SENDS` summaries · the **absence** of `NEVER REACHED ITS
CHECKPOINT`, `[UtxoAssert] MISMATCH`, `[Governance] EXCEPTION` and `[BetJournal] UNDECLARED`.

1. **Before launching, archive F1's two traces.** Unit 1 changes both headers, and the profilers rotate a mismatched
   file onto `.old`, which already holds older traces (2026-10-06 and 2026-09-22). In
   `%APPDATA%/Godot/app_userdata/GamblingMiner/logs/`, rename `block_cost_trace.csv` → `block_cost_trace_F1.csv` and
   `frame_cost_trace.csv` → `frame_cost_trace_F1.csv`. ✅ **Done 2026-10-20** with the developer's approval; both
   `.old` files are untouched.
2. Launch the game from the Godot editor (Play), as usual.
3. Open **DiceGame** and stay there for the whole run (D-26.2; F1 showed the scene does not move the cost).
4. DEV time selector: **9000X**.
5. Tick the **"⏱ Block cost"** and **"⏱ Frame cost"** checkboxes **before** starting the autobet.
6. Start the autobet for the **player and all four bots**, each with the strategy and credits it already has (the
   player at 10 credits, `bot_1`…`bot_4` at 1). That is F1's configuration: `session_lifecycle_trace.csv` shows all
   five runners stopped together at the end of F1. Change nothing.
7. Let it run for **about 20 real minutes**. F1 took ~7 minutes for 45 blocks, so 20 minutes should give ~120.
8. Stop the autobet, then untick both checkboxes (the disarm writes the final summary to the log). Close the game
   normally and tell me "M1 done". I read `godot.log`, `block_cost_trace.csv`, `send_cost_trace.csv` and
   `frame_cost_trace.csv` myself.

---

## 8. Part B — the fix, specified from M1's data

Written as an amendment to this file after M1, with predictions stated as **counts**, and committed before any fix
code. The candidate shapes below are listed so R1 has something exact to choose from. **None is chosen yet.**

| Fix | What | Why it is exact | Its count |
|---|---|---|---|
| **F-a** | verify each transaction **once**, not once per node: a `txid → signature-valid` memo | the txid is the content hash (Step 4b.3 / A.6), and signature validity depends on content alone | verifies per send: ≈72 × inputs → inputs |
| **F-b** | a confirmed-txid set per `BlockchainService`, maintained on append (mini-plan 17 B1's shape) | the same set the scan computes, kept current | txs scanned per admission: ≈12,900 → 0 |
| **F-c** | a signing-context cache per derived index in `DerivedAddressWallet` | deterministic in (seed, index) | derivations per input → ≤ 1 per address lifetime |
| **F-d** | `CollectPendingSpentOutpoints` maintained incrementally | only if measured at ≥ 20% | rebuilds per admission → 0 |

**Out of reach for an exact fix, and out of scope:** a narrower fan-out (T4.2, Step 17.D), and fewer transactions
(that is design, not cost).

**Verification M2: an interleaved A/B in one session** (D-26.4). A DEBUG-only switch alternates by block parity.
Even heights bypass the fix's *reads* and odd heights use them, while any maintenance runs on both. So both paths are
measured in the same world, era and scene, which is the only comparison that reproduces (instrument rule 3). Every
row carries its path. **The switch is deleted at close-out** (Convention 3).
- **Outcome identity:** every `Sent` send is accepted at all of its admissions on both paths, and `[UtxoAssert]`
  stays silent.
- **Exit criterion, registered now:** on the new path, **blocks over 1,000 ms ≤ 10%** of its blocks (F1: 44 of
  45), **and** mean `botTransactionsMs` new/old **≤ 0.25** in the same session.
- **If M1 shows that exact fixes cannot reach that**, Part B says so **before** building anything. The developer then
  chooses: stop at what the exact fixes give, or take T4.2 forward (Convention 10's mirror; no grinding).

---

## 9. Order

1. **Specify** (this commit), then branch `mini26-per-block-freeze` off `main`.
2. **Unit 1**: Part A's instrument (§3), with V1–V4 runnable from its output.
3. **M1** (§7), run by the developer. The results go into this file, refuted predictions stated as refuted.
4. **Part B amendment** (§8): the chosen fix(es), their count predictions, and M2's design, committed before code.
5. **Fix units**, one per fix, each with its count.
6. **M2**: the interleaved A/B.
7. **Close-out:** this plan's close-out section, the roadmap entry → `DONE`, an `IMPLEMENTATION_STATUS.md` entry, the
   T4 notes brought up to date, and any lesson that generalises into `ProjectDesignManual.md` Ch. 40.

---

## 10. Out of scope

- **T4.2**, one canonical chain (Step 17.D). If R1 lands on the fan-out's width, it comes back to the developer.
- **Changing what the network does:** the historical tx target, the cast's send fractions, the exchange rule, and
  governance behaviour. A cheaper send must produce the **same** transactions.
- **`ComputeAuctionLedger`'s chain walks**, unless M1 puts one at ≥ 20%. Today it sits inside a donation phase that
  usually reads 10–20 ms.
- FrameCost's stall semantics (they stay as they are; §3.3 only attributes GC the same way).
- Any UI.

---

## 11. Decisions at specification (the developer's)

| # | Decision | Proposed |
|---|---|---|
| **D-26.1** | Branch name | `mini26-per-block-freeze` |
| **D-26.2** | M1 runs in DiceGame only | yes: F1 measured the scene's effect as 1.03–1.29×, inside its 1.5× rule |
| **D-26.3** | The GC question is measured here and fixed only under R4 | yes |
| **D-26.4** | M2 is an interleaved A/B by block parity, through a DEBUG-only switch deleted at close-out | yes, confirmed again at Part B |
| **D-26.5** | T4.2 stays out; a fix that needs it comes back to the developer | yes |
