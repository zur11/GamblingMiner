# Implementation Roadmap — Unified Order of Work

**Purpose**: single source of truth for *what to build next and why*, across all the plan files in `AIHelperFiles/`. The individual plans hold the detail; this file holds the **order and the dependencies** so overlapping themes (block-candidate model, gradual network growth, UTXO realism) don't collide.

**Last updated**: 2026-06-25.

---

## 1. Plan inventory & current status

| Plan file | Scope | Status |
|---|---|---|
| `btc-wallet-system-plan.md` | Addresses, seeds, passphrase wallets, dev scenes, notepad | ✅ Done (Phases 0–9) |
| `100x-time-scale-migration-plan.md` | 100X time scale, 210k supply, 2,100-block halving | ✅ Done (Phase 5 validation is a checklist, not code) |
| `scheduled-bot-transactions-plan.md` | Miner-bot → holder-bot BTC recirculation | ⏸ Core done (Phase 1); re-aligns onto candidate engine in Step 6 |
| `historical-founders-and-bootstrap-plan.md` | Satoshi/Hal/Hearn nodes, genesis fix, bootstrap to 21 Mar, Satoshi 11k target | ✅ Phases 1–3 + bootstrap (3a) done; Phases 4/6/7 **implemented in Step 7** |
| `step7-historical-character-economics-plan.md` | **Founders as regulated concurrent miners: Satoshi 11k ramp, Hal drip, Hearn round-trip, E4 tx, event scheduler** | ✅ **Core (7.1–7.5) COMPLETE & verified** — 7.6 docs |
| `candidate-block-model-plan.md` | **Per-node candidate blocks, mempool, tx selection, Merkle, fees, content-hash txid — the real competition engine** | ✅ **Step 4 COMPLETE** (4a/4b.1/4b.2/4b.3/4c) |
| `historical-blockchain-events-research.md` | Character/event data + UTXO-realism direction | ✅ Roster + events (E1/E4/E6–E8) canonical for Step 7; §6 address research still open (Step 8) |
| `btc-pools-hardware-plan.md` | Hardware credits, casino community pool, fees, **+ Network Difficulty Regulator** | ✅ **Done & validated** — difficulty regulator (D.1–D.4, power-step validation closed) + hardware credits/pools/shop. ProjectDesignManual Ch. 26–27 |
| `bot-play-history-plan.md` | Bot Play-History scene (last 260 plays/bot + Notepad) | ✅ Done — `Screens/BotPlayHistory/`, in use |

---

## 2. Direction reset (2026-06-20)

The historical opening is now an accepted, verified baseline: **3a works in-engine — the game starts on 21 Mar 2009 on a Satoshi/Hal-mined chain.** From here the priority flips.

**Decision:** stop layering historical-character economics next. Instead, build the **real per-node candidate-block competition engine** generically — Satoshi/Hal are treated as plain nodes and their special economics are *ignored* for now — and only afterward (a) **refit the lottery bootstrap** to run through that engine and (b) **re-add the historical-character economics** on top of it.

Why: the candidate-block model is the true keystone — it's depended on by hardware pools, fees, bot competition, and (eventually) the founders' own mining. The minimal `HashrateWeight` + lottery (Step 2) and the bootstrap (Step 3) were enough to reach the baseline; everything past the baseline should sit on the *real* engine, not the simplified one.

**Cancelled as next steps (parked, not deleted):** the old 3b (Satoshi 11,000-BTC ramp + disappearance) and 3c (12 Jan 10 BTC tx). Their design is preserved in `historical-founders-and-bootstrap-plan.md` and re-activates as **Step 7** once the candidate engine exists.

Two themes still hold:
- **Block-candidate model = keystone.** Minimal weight + lottery done (Step 2); the **full per-node candidate engine is now the lead (Step 4)**.
- **Network grows over time** (Satoshi → Hal → player @ 21 Mar → bots gradually) — still the init ground truth; the baseline (3a) established it.

---

## 3. Recommended order

> Rule of thumb: do the things that **redefine the foundation** before the things that **build on it**. Identity + init model first; economy systems after.

### Step 1 — Founders identity foundation  *(founders P1–P3)*  ✅ IMPLEMENTED (compiles; pending in-engine verification)
- Satoshi & Hal as nodes with seed phrases; `FoundersWallets` dev scene; genesis & early coinbase recipients → derived `gm1q…`.
- Done: `FounderWalletState`; `WalletInitializationService` creates/persists `satoshi`/`hal` wallets; `NetworkRoot` registers both founder nodes + rewrites genesis & block-2 coinbase recipient to Satoshi's `gm1q…`; `FoundersWallets` dev scene + MainMenu/SceneManager wiring. Deferred to a later step: the 100-byte `InputData` cap.
- **No dependencies.** Fixes the base58/`gm1q…` inconsistency that every other system references. Low risk, high foundation value. Founders exist as registered nodes but don't mine yet (mining arrives in Step 2/3); game still starts at genesis until Step 3.
- *(The 12 Jan 10 BTC Satoshi→Hal tx is NOT here — it needs a Jan-12 block and spendable Satoshi coins, so it lands in Step 3 with the bootstrap.)*

### Step 2 — Block Candidate + Hashrate model (minimal)  *(founders P0)*  ✅ IMPLEMENTED (compiles; DEV-verifiable in FoundersWallets)
- `HashrateWeight` per node + `RunWeightedBlockLottery`. Refactor of existing single-nonce mining; player bet-driven path unchanged.
- **The keystone.** Unblocks Steps 3, 4 (hardware), and 6 (template builder).
- Done: `NodeAgent.HashrateWeight` (default 1.0); `NetworkRoot.RunWeightedBlockLottery(minerNodeIds, minedAtUnixMs?, rng?)` (weighted winner → mines one real PoW block via `MineAndBroadcastBlock`) + `SetHashrateWeight`/`GetHashrateWeight`; injectable RNG for deterministic bootstrap. A "Mining Lottery [DEV]" panel in FoundersWallets lets you set weights + mine N blocks and observe the Satoshi/Hal split. Per-node candidate *template* refactor deferred to P4 (OQ-1).

### Step 3 — Historical bootstrap baseline (3a only)  *(founders P5)*  ✅ IMPLEMENTED + VERIFIED IN-ENGINE
- First-launch-only pre-mine genesis→21 Mar 2009: Satoshi mines every block, Hal exactly 3 (near 12 Jan / 5 Feb / 5 Mar), timestamps march from genesis with ±30% jitter, player clock lands at a random time on 21 Mar. `NetworkRoot` bulk-mining + static API; `HistoricalBootstrapService`; wired into `CalendarTimeService._Ready()`.
- **This is the accepted baseline.** Old 3b/3c are **cancelled as next steps** and re-sequenced to Step 7.

### Step 4 — Per-node Candidate Block Model  ✅ COMPLETE  *(was old Step 6 / PRIVATE_ROADMAP P4 + scheduled-bot-tx Phase 4)*
- The **real blockchain competition engine**: each node builds its own candidate block from its mempool view — tx selection (24-tx cap, fee ordering, age tie-break), Merkle root, coinbase = reward + collected fees; proper block header hashing; content-hash txids.
- **4a ✅ (verified):** `Block.MerkleRoot`, `Transaction.Fee`/`SizeVBytes`, `MerkleTree`, header hashing (`HashHeader`), Merkle tamper check in `ChainIsValid`, pre-mine timestamp.
- **4b.1 ✅ (verified):** `BlockTemplateBuilder`, coinbase-in-block (`CommitBlock`), 24-tx cap, coinbase maturity N=1 (`GetAddressData`), candidate-template caching.
- **4b.2 ✅:** `Transaction.Fee` in the signed payload; sender pays `Amount+Fee`; miner collects `ΣFee` via coinbase; bots attach 0.1–1.0 BTC fees.
- **4b.3 ✅:** content-hash txid (`ComputeTransactionId` + `Transaction.Salt`; Merkle leaf = txid; txid-integrity check; coinbase BIP34-height salt).
- **4c ✅:** BlockExplorer surfaces Merkle root / time / fees / `[COINBASE]`; player fee selector added to BTCWallet's send form.
- Built **generically** — historical characters are plain nodes here; their economics return in Step 7.

### Step 5 — Refit the lottery bootstrap to the candidate model  ✅ ABSORBED into Step 4 (verify-only)
- 4b.1 replaced the mining core **in place** (`MinePendingTransactions`/`TryMineSingleNonceAttempt` build via `BlockTemplateBuilder`), so the historical bootstrap and weighted lottery already mine real candidate blocks through the new engine — no separate simplified path remained to refit. Just confirm bootstrap blocks carry proper coinbase/Merkle (they pass `ChainIsValid` on reload).

### Step 6 — Difficulty regulator + bot play-history + hardware pools  ✅ COMPLETE  *(RE-SCOPED 2026-06-23; finished 2026-06-25)*
- **Re-scoped order:** (1) ✅ **Network Difficulty Regulator — DONE, user-tested & validation-closed** (D.1–D.4: continuous difficulty, hybrid feed-forward + LWMA retarget with easing, Block Explorer readout, calibrated; power-step validation campaign confirmed it sound, contingency fixes closed unimplemented; `btc-pools-hardware-plan.md` + ProjectDesignManual Ch. 26); (2) ✅ **Bot Play-History scene — DONE** (last 260 plays per active miner bot + Notepad; `Screens/BotPlayHistory/`, in use; `bot-play-history-plan.md`); (3) ✅ **Hardware credits + casino community pool — DONE** (credit model, individual↔casino split + round-robin routing, dynamic fee + proportional distribution, `BTCPoolsAndHardwareShop` with Buy/Discard, hardware-locked speed; bootstrap = 1 individual + 0 casino; routed through `SimulationService`; ProjectDesignManual Ch. 27). Also added: a DEV time-acceleration tool (100X→9000X) for validation runs.
- **Gradual miner spawning is POSTPONED** to a later step — it needs a curated per-bot strategy set first; for now keep **DEV access to all bettable nodes**. Era-based hashrate + obsolescence and credit-at-introduction are deferred too.
- ✅ **Already done (Step 4 cleanup):** the `scheduled-bot-transactions` circulation trigger was re-aligned to a **per-bot warmup** (`CirculationWarmupBlocks` = 5 blocks since a bot's *own* first mined block, via `FirstBlockHeightMinedBy`) + a no-self-send guard — ready for gradual bots when they return.
- **Depends on Step 4** (built once, on the real engine).

### Step 7 — Historical-character economics  ✅ CORE COMPLETE (7.1–7.5) *(re-activated 3b/3c + Hearn — founders P4/P6/P7)*
- Satoshi 11,000-BTC dynamic ramp + disappearance (≥ 2011-04-26); 12 Jan 10 BTC Satoshi→Hal tx; April 2009 Mike Hearn round-trip. All built **on the real candidate engine**, not the simplified path.
- **Done & verified:** founders are **regulated concurrent miners** (`FoundersMiningService`) — they mine in lockstep with the player's time advancement (no autonomous clock), Satoshi power-regulated to ~10% toward 11,000 BTC by 2011-04-26, Hal a `P=1.0` drip fading to 0 by 9 Aug 2009, Hearn a receive-only holder doing the 32.51 round-trip (+82.51). E4 10-BTC tx in the bootstrap; `HistoricalEventScheduler` for player-era scripted txs; FoundersWallets DEV readout + `founders_trace.csv` telemetry. In-engine tests: Satoshi 9.4% share, Hal disappears exactly 9 Aug, Hearn round-trip on 18 Apr, 168-block durability run clean. **Only 7.6 (docs) remained.**
- Full detail: **`step7-historical-character-economics-plan.md`**. Original design preserved in `historical-founders-and-bootstrap-plan.md` (Phases 4, 6, 7).

### Step 8 — UTXO realism / Patoshi per-receive addresses
- Fresh derived address per coinbase/deposit; real change outputs; surfaced via passphrase wallets. Founders first, then player wallet.
- **Depends on Step 4** (the candidate/coinbase machinery). Pending §6 address-reuse research in the research doc.

### Step 9 — Economy & meta  *(PRIVATE_ROADMAP P6–P8)*
- P6 casino finances → P7 BTC/SC trading → P8 achievements.

### Step 10 — (POST-BASIC-MODE) Divergent Chains / Fork Simulation  *(deferred — revisit only after Basic Mode is complete)*
- **Deferred, not discarded.** Wanted feature; lower priority until Basic Mode ships. Today all nodes share one canonical chain (`BroadcastBlock` → `TryAcceptMinedBlock`), so there are no forks and chain "consensus" was a no-op — the old `RunConsensus`/`RunConsensusRound` were removed in cleanup task T2.
- Goal: a realistic P2P layer where chains can **diverge** — propagation delay, near-simultaneous blocks, **forks**, **orphan/stale blocks**, **reorgs** — resolved by a real **most-work longest-chain consensus** pass (reinstating a `RunConsensusRound`-style step, keyed on accumulated work, plus Block-Explorer fork/orphan visualization). Layers on top of the per-node candidate-block model (Step 4).
- **Gate:** do not start until Basic Mode is complete and stable. Detail mirrored in `Documentation/PRIVATE_ROADMAP.md` → "Post-Basic Mode — Divergent Chains / Fork Simulation".

---

## 4. Dependency graph (compact)

```
✅ Step1 (founder identity) ─┐
✅ Step2 (weight + lottery) ─┼─> ✅ Step3 (bootstrap baseline, 21 Mar)
                             │
                             └─> ✅ Step4 (CANDIDATE BLOCK MODEL) ─> ✅ Step5 (absorbed)
                                      ├─> ✅ Step6 (regulator + hardware pools + bot play-history)
                                      ├─> ✅ Step7 (historical-char economics: ex-3b/3c + Hearn) — core 7.1–7.5 done
                                      └─> Step8 (UTXO / Patoshi)  ─> Step9 (economy/meta)
```

---

## 5. Mapping to `Documentation/PRIVATE_ROADMAP.md` (P0–P8)

| Roadmap step here | PRIVATE_ROADMAP priority |
|---|---|
| Steps 1–3 (founder identity + bootstrap baseline) | New — predates/extends P3 (bots, transactions, mempool) |
| **Step 4 (candidate block model — lead)** | **P4 (block template builder)** + P3 mempool + scheduled-bot-tx Phase 4 (fees) |
| Step 5 (refit bootstrap to engine) | New — corrects the Step 3 baseline |
| Step 6 (miner bots + hardware pools) | P3 + P5 (hardware progression) |
| Step 7 (historical-char economics) | New — the parked ex-3b/3c |
| Step 8 (UTXO / Patoshi) | New — refines P3 address model |
| Step 9 | P6 / P7 / P8 |

---

## 6. What's next
- ✅ Steps 1–7 core done. **Next: Step 8 (UTXO realism / Patoshi per-receive addresses)** — fresh derived address per coinbase/deposit, real change outputs (incl. the deferred E8 17.49 Hearn change), founders first then the player wallet. Pending §6 address-reuse research in `historical-blockchain-events-research.md`.
- Step 7 leftover refinements (deferred to late Basic-Mode tuning): Hal's network-coupled fade (currently a linear `1.0→0` stand-in pending gradual miner spawning), the founders' full long-term timelines (Hal 2013 sell-off / 2014, Hearn 2016).
- ❌ Still don't start Step 9 (economy/meta P6–P8) before Step 8 — the UTXO model underpins BTC/SC trading.
