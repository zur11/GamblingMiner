# Step 7 — Historical-Character Economics — Implementation Plan

**Status**: Planning. Re-activates the parked **Phases 4, 6, 7** of `historical-founders-and-bootstrap-plan.md` (Satoshi 11,000-BTC ramp + disappearance, the 12 Jan 10 BTC Satoshi→Hal tx, the April 2009 Mike Hearn transfers), built **on the real per-node candidate engine** (Step 4) — not the simplified path.

**Roadmap**: `IMPLEMENTATION_ROADMAP.md` → Step 7. **Depends on** Steps 4–5 (candidate-block model — ✅ complete) and Step 6 (difficulty regulator + hardware pools — ✅ complete). **Companion data**: `historical-blockchain-events-research.md`.

**Created**: 2026-06-25.

---

## 0. Decisions locked for this step (from the design conversation)

These four answers shape every phase below; they resolve the conflicts the parked design left open.

| # | Question | Decision |
|---|---|---|
| D1 | **How do founders accrue BTC in the player era**, given the engine is bet-driven (only player + bots mine, 1 bet = 1 nonce attempt) and OQ-2 said "time always follows the player's bets" after bootstrap? | **Founders are real concurrent miners.** After the bootstrap they mine **in parallel** with player + bots, with their own mining power and **no casino betting**. They **never advance the clock on their own** — they only perform nonce attempts *while the player advances time by betting* (lockstep), so OQ-2's "no autonomous **time** advancement" still holds. Satoshi's power is **dynamically regulated** (like the difficulty regulator) toward **11,000 BTC by 2011-04-26**. |
| D2 | **Patoshi multi-address** (fresh derived address per reward) now, or later? | **Single base address per founder in Step 7** (the existing testing-stage shortcut). The Patoshi per-receive multi-address pattern + real change outputs are **deferred to Step 8** (UTXO realism). |
| D3 | **v1 roster?** | **`satoshi`, `hal`, `mike_hearn` only.** Gavin / Laszlo / Martti and their flavour events are a later iteration. |
| D4 | **How are player-era scripted txs (Hearn E6/E7/E8) triggered**, since their date (~18 Apr 2009) falls after the 21 Mar player start? | A lightweight **`HistoricalEventScheduler`** injects the scripted signed txs when the game clock crosses their date during normal play (hooked where `ScheduleBotTransactionsAfterBlock` already runs). |

### D1 refines OQ-2 (record this)

The parked plan's **OQ-2** read: *"autonomous (no-bet) mining happens **only** during the bootstrap; after that in-game time always follows the player's bets."* D1 **extends** it: founders **do** mine in the player era, but **only on the player's time-advancing ticks** — they add hashrate, not clock motion. The clock is still moved exclusively by player bets.

The three founders are **not symmetric** — each is grounded in its real history (see §2.3, derived from `Resumen Hal Finney y Mike Hearn.txt`):

- **Satoshi** — regulated miner, share dictated by the **11,000 BTC by 2011-04-26** requirement (not a tunable preference — Q-A1).
- **Hal** — keeps **one participant's worth of power** (`P=1.0`, kept as-is) and falls behind **relatively** as the network grows (gradual miners up to **~9 Aug 2009**, his ALS turning point), then dormant. v1 stand-in = a linear `1.0→0` fade.
- **Hearn** — **never mines** (real history: *"sin minería documentada — enfocado en software, no hardware"*). Holder entering ~April 2009 who does the famous **32.51 BTC round-trip** with Satoshi (he *does* send once — Q-N1), netting the +50 gift.

---

## 1. What exists today (verified in code)

- **Bootstrap** (`HistoricalBootstrapService`): first-launch-only, mines genesis → random time on 21 Mar 2009. Satoshi mines every non-Hal block; Hal mines exactly 3 (`HalBlockDatesLocal`). Drives the engine through the **static** surface `NetworkRoot.MineNodeStatic` inside `BeginBulkMining`/`EndBulkMiningAndPersist`. Difficulty is pinned to `InitialDifficulty` during bulk mining. ⇒ Satoshi ≈ 5,550 BTC, Hal ≈ 150 BTC at player start. **No 10 BTC tx yet.**
- **Founders as nodes** (`NetworkRoot.RegisterFounderNode`): `satoshi` and `hal` are registered `NodeAgent`s (keys derived from their seeds via `WalletInitializationService.SatoshiWallet`/`HalWallet`). Genesis + block-2 coinbase already point at Satoshi's derived `gm1q…` (`NormalizeGenesisAcrossNodes`, `EnsureSecondBlockBootstrapPendingTx`). **No `mike_hearn` node yet.**
- **Player-era mining** (`SimulationService`): bet-driven. `ExecutePlayerBetOnce` / `ExecuteBotBet` → `RouteNonceAttempt(nodeId)` → `NetworkRoot.TryMineSingleNonceAttempt(nodeId)` = one nonce attempt on that node's own candidate. `_activeMiningPower` (Σ player+bot bets/sec) is pushed via `SetActiveMiningPower` and feeds the difficulty regulator. Founders **do not** participate.
- **The lottery knob** (`NetworkRoot.RunWeightedBlockLottery`, `NodeAgent.HashrateWeight`): exists from Step 2, used by the bootstrap only. `HashrateWeight` defaults to 1.0 and is otherwise unused in the player era.
- **Per-block hook** (`NetworkRoot.HandleMinedBlock`): the single choke-point after any block — broadcasts, updates streaks, and (live blocks only, `!_bulkMining`) calls `AppendDifficultyTrace`, `ScheduleBotTransactionsAfterBlock`, `PersistStateToDisk`, `TryDistributePendingCasinoRewards`. **This is where founder-block bookkeeping and the historical-event scheduler attach.**
- **External-block handling** (`SimulationService`): when a *bot* mines, `CaptureCheckpoint()` + `StopPlayerOnExternalBlockMined()` already run. **Founder blocks reuse this exact path.**
- **"Block = the only commit to disk"** (`PersistStateToDisk`): nothing between blocks persists; an app restart reverts the world to the last mined block. ⇒ **the event scheduler must derive its "already fired" state from the chain**, not a side flag file (mirror `EnsureSecondBlockBootstrapPendingTx`'s `ContainsTransactionId` check).
- `FoundersMiningService` and `HistoricalEventScheduler` **do not exist yet**.

---

## 2. Target mechanism — founders as regulated concurrent miners

### 2.1 The power model (player era)

Founders mine **in lockstep** with the player's time advancement:

1. Each founder has a **mining power** `P_f` expressed in the *same unit as `_activeMiningPower`* (bets/sec-equivalent attempts).
2. Founder power is **added to the network total** via `SetActiveMiningPower(playerBotsPower + Σ P_f)`, so the difficulty regulator raises difficulty to keep block pacing at `TargetBlockSeconds`. (Net effect: the player finds fewer blocks — the share now goes to the founders. Thematically correct: "Satoshi mined most early blocks.")
3. For every **non-founder** nonce attempt executed in a frame (player + bots), each founder accrues `P_f / P_nonFounderTotal` attempts into a **fractional accumulator**. While the accumulator ≥ 1, the founder performs one real nonce attempt on **its own candidate** (own coinbase) at the current in-game timestamp, via `NetworkRoot.TryMineSingleNonceAttempt(founderId)`.
4. Because founders only attempt *when the player advances time*, they **never move the clock**. Idle player ⇒ no founder mining.

Share check: with `P_f = s/(1-s) · P_nonFounderTotal`, a founder wins a fraction `s` of all blocks on average — independent of how many bots are online.

### 2.2 Satoshi's regulated power (the "founder regulator")

Recomputed **every block** while Satoshi is active (mirrors the parked Phase 4 maths, now feeding real power not just a lottery weight):

```
floorDate        = 2011-04-26                          // never retire before this (OQ-3)
targetBtc        = 11000                                // 1% of his real ≈1.1M (excludes unspendable genesis 50)
btcRemaining     = max(0, targetBtc - satoshiConfirmedBtc)
reward           = current-era block reward (50 BTC in this whole window)
blocksUntilFloor = ceil( inGameDaysUntil(floorDate) * 1.477 )   // 0 once past floor

if clock < floorDate:
    targetShare = clamp01( btcRemaining / max(1, blocksUntilFloor) / reward )
    P_satoshi   = shareToWeight(targetShare, P_nonFounderTotal)      // s/(1-s) · W_others
else:                                                  // past floor but still short → finish ASAP
    P_satoshi   = P_nonFounderTotal * pow(GROWTH, blocksPastFloor)   // exponential ramp

retire when  clock >= floorDate AND satoshiConfirmedBtc >= targetBtc:
    P_satoshi = 0; mark satoshi retired; stop recomputing.  Coins stay frozen forever (Q-S1 → frozen).
```

`satoshiConfirmedBtc` = his **spendable** confirmed balance summed across his address(es), **excluding** the unspendable genesis 50 (OQ-8). `P_nonFounderTotal` (a.k.a. `W_others`) = the summed power of **every other active miner** (player + running bots + Hal while he is still mining); Satoshi is excluded from his own denominator.

**The ~10% share is a historical *requirement*, not a tunable (Q-A1).** It is the output of the regulator, not a knob: ~5,550 BTC at player start (bootstrap) leaves ~5,450 to mine over the ~1,121 player-era blocks to the floor date ⇒ ~109 blocks ⇒ ~10% of all blocks. If the player under-mines, time is "sacrificed" and the exponential ramp closes the gap after the floor, then he retires. The number is whatever it must be to land **11,000 BTC by 2011-04-26** — never capped or hand-set.

### 2.3 The three founders at fractal scale (history-grounded)

**Fractal-scaling principle (state once, reuse everywhere).** BTC amounts are **not** individually divided by 100. The fractal compression comes from **block density**: our chain produces ~1.477 blocks/in-game-day vs real Bitcoin's ~144/day, so an actor who mined over a real calendar span accrues **~1% of the BTC** they did historically — *same* 50 BTC/block reward, ~1% as many blocks exist in that window. That is exactly why Satoshi's real ≈1.1M → **11,000** (1%). **Iconic fixed transaction amounts are kept exact** (10, 32.51, 50.00, 17.49 — Q-E2).

| Founder | Mines in player era? | Power model (v1) | Real anchor (from the source file) |
|---|---|---|---|
| **Satoshi** | Yes — regulated | Power recomputed per block to hit **11,000 BTC by 2011-04-26** (§2.2). Retires (power→0, frozen forever) once both conditions hold. | ≈1.1M BTC real → 1% target; "moved on… in good hands with Gavin" ~2011. |
| **Hal** | Yes — relative drip | Keeps **one participant's worth of power** (`P = 1.0`, kept as-is — never lowered). As the network grows (more miners join up to **~9 Aug 2009**, by design) he falls behind **relatively** on his own. v1 stand-in: a linear `P = 1.0 → 0` fade to 9 Aug; **no BTC target**. Dormant afterward. | Mined "hundreds of BTC" Jan–2009; 2nd miner after Satoshi; ALS turning point Aug 2009 → tapers; coins spent on medical care (2013, out of scope). |
| **Hearn** | **No — never mines** | `P = 0` permanently. Enters ~12 Apr 2009 (first Satoshi contact); does the **32.51 round-trip** with Satoshi (sends once — Q-N1), ending with the +82.51 gift, then **dormant**. | "Sin minería documentada — enfocado en software, no hardware." The deal: *"si Hearn le enviaba 32.51, Satoshi le devolvería la moneda más 50."* |

Notes:
- Hal's bootstrap holdings (3 blocks = 150 BTC + 10 received) are a known **overshoot** vs strict fractal scaling, but the bootstrap baseline is **locked** (Q-bootstrap) — accepted as-is; only his **player-era** curve is shaped here.
- **Why keep Hal at `P = 1.0` instead of lowering it (decided after the 7.2 test — Q-N2):** at a bare 1-credit player Hal mines ~50% of blocks, which *looks* huge — but that is correct "equal to one participant" behaviour. The intended dynamic is that the player + a growing miner field (gradual spawning up to ~9 Aug 2009) outgrow Hal so he shrinks **relatively**, exactly as a fixed-power miner should. The network-coupled fade ("his power keeps up with the network's general rise less and less as August nears") is the **target**; the current linear `1.0→0` decay is its **v1 stand-in** until the postponed gradual-miner-spawning feature lands (Step 6 note).
- Hal's decay endpoint (9 Aug 2009) and Hearn's no-mining are the v1 grounding; the richer long-term behaviour (Hal's 2013 medical sell-off, the 2014 dormancy flag, Hearn's 2016 exit) stays **deferred to late Basic-Mode tuning** — keep the hooks, keep them cheap.

---

## 3. Phases

### Phase 7.1 — `FoundersMiningService` (the founder hashrate controller)

**New file**: `Scripts/Services/FoundersMiningService.cs` (autoload `Node`, registered in `project.godot`). **Touches**: `NetworkRoot.cs` (read-only query helpers).

Owns, with **no persisted state** (recomputed from the chain each launch, like the rest of the between-block world):

1. Per-founder **power** `P_f`, **active/retired** flags, and **fractional attempt accumulators** (§2.1).
2. **Constants**: `SatoshiTargetBtc = 11000m`, `SatoshiEarliestDisappearance = 2011-04-26`, `BlocksPerInGameDay = 1.477`, `Growth` (exponential ramp base, e.g. `1.15`); `HalBaselinePower = 1.0` (kept as-is), `HalDecayStart = 2009-03-21`, `HalDecayEnd = 2009-08-09` (linear `P=1→0` between — his ALS turning point); `HearnPower = 0` (never mines).
3. **Satoshi power recompute** `RecomputeSatoshiPower(double nonFounderTotalPower, DateTime nowLocal)` implementing §2.2; `shareToWeight(s, w)`; retirement transition. **Hal power** = `HalBaselinePower · clamp01((HalDecayEndDate − now)/(HalDecayEndDate − playerStart))`, → 0 after Aug 2009. **Hearn** is never added to the miner set.
4. **Confirmed-BTC query**: add `NetworkRoot.GetFounderConfirmedSpendableBtc(founderId)` (sum spendable, exclude genesis) — Satoshi reads this each block.
5. **Drive API** (used by Phase 7.2): `DriveFounderAttempts(int nonFounderAttemptsThisFrame, double nonFounderTotalPower, long tsMs)` — runs the accumulator math and mines founder attempts; `TotalFounderPower` (added to `SetActiveMiningPower`); `OnBlockMined(Block, …)` to recompute Satoshi after every block + apply Hal/Hearn decay.

**Verification**: a DEV readout in `FoundersWallets` (Phase 7.5) shows live power, share, confirmed BTC vs 11,000, blocks-to-disappearance — testable before wiring into the live loop.

---

### Phase 7.2 — Player-era concurrent founder mining (integration)

**Touches**: `SimulationService.cs`, `NetworkRoot.cs`.

1. **Feed founder power into difficulty**: change the power pushed each frame from `GetTotalActiveMiningPower()` to `GetTotalActiveMiningPower() + FoundersMiningService.TotalFounderPower`, so the regulator accounts for founder hashrate and pacing stays at `TargetBlockSeconds`.
2. **Drive founder attempts**: after the player + bot bet loops in `SimulationService._Process`, call `FoundersMiningService.DriveFounderAttempts(nonFounderAttemptsThisFrame, nonFounderTotalPower, tsMs)`. Founders mine their own candidates via `NetworkRoot.TryMineSingleNonceAttempt(founderId)` at the current in-game timestamp. (Founder nodes already hold the synced chain and the shared mempool, so their candidates are valid and collect pending fees.)
3. **Founder block bookkeeping = the external-block path**: when a founder mines, run the same `CaptureCheckpoint()` + `StopPlayerOnExternalBlockMined()` the bots use. `HandleMinedBlock` already persists + schedules.
4. **Recompute founders each block**: `FoundersMiningService.OnBlockMined(...)` from `HandleMinedBlock` (or right after a block is detected in `SimulationService`) recomputes Satoshi's power toward the target and applies Hal's date-based decay. **Player era only** — the bootstrap stays flat/unregulated (Q-bootstrap).
5. **Lockstep guarantee**: founders only attempt inside this player-driven frame logic — **no autonomous timer**. Idle ⇒ no founder mining ⇒ no clock motion (OQ-2 refinement, §0).

**Gameplay note**: with Satoshi at ~10% share for the whole 21 Mar 2009 → 26 Apr 2011 window, the player effectively loses ~10% of mined blocks (reward + fees) to Satoshi. This is **intended and historically mandated** (Q-A1) — Satoshi *did* mine most early blocks — not a balance knob. It is a real, thematic "Satoshi dominance" tax on the player's early BTC mining and must not be capped to make the player richer.

---

### Phase 7.3 — The 12 Jan 2009 Satoshi→Hal 10 BTC tx (E4, bootstrap era)

**Touches**: `HistoricalBootstrapService.cs` (+ optionally a small `HistoricalEvents` helper shared with Phase 7.4).

- In the bootstrap loop, when the running timestamp first crosses **12 Jan 2009**, build a **real signed** tx: Satoshi → Hal base address, **10 BTC**, signed by Satoshi's key (`NodeAgent.CreateSignedTransaction`), broadcast so it confirms in the next bootstrap block. Deterministic `TransactionId` (e.g. `hist_E4_satoshi_hal_10`) so re-runs/`ContainsTransactionId` checks are idempotent.
- Requires Satoshi ≥ 10 spendable BTC by then — true (genesis unspendable, but block-2's 50 matures one block later and he mines steadily through January). Place it **after** he has a matured coinbase.
- Optional `InputData` note: `"First person-to-person Bitcoin transaction — Satoshi → Hal, 12 Jan 2009"` (respect the **100-byte** cap, OQ-10). Real-history footnote: confirmed in real block 170; in our fractal it lands ~block 13 (dates are the source of truth, not heights — Q-E1).

---

### Phase 7.4 — `HistoricalEventScheduler` + Mike Hearn (E6/E7/E8, player era)

**New file**: `Scripts/Services/HistoricalEventScheduler.cs`. **Touches**: `WalletInitializationService.cs` + `NetworkRoot.cs` (register `mike_hearn`), `HandleMinedBlock` (hook), `WalletModels.cs` (Hearn `FounderWalletState`).

1. **Register `mike_hearn`** as a founder node (seed → derived `gm1q…`), same pattern as Satoshi/Hal. No mining power; he **does** sign one outgoing tx (the round-trip return), so his keypair must be live.
2. **Scheduler**: a small **ordered** ledger of scripted events `{ date, build(txs) }`, fired **in sequence**. After each mined block (in/near `HandleMinedBlock`, beside `ScheduleBotTransactionsAfterBlock`), for every event whose `date` ≤ current block timestamp **and whose prerequisite tx is already confirmed** and that is **not already on-chain or pending**, inject its signed tx(s).
   - **Fired-state + ordering derived from the chain** (critical, because of revert-to-last-block): check `ContainsTransactionId(eventTxId)` / pending set before injecting, and gate each step on the previous step's txid being confirmed — never a side flag file. Deterministic ids: `hist_E6_…`, `hist_E6b_…`, `hist_E7_…`, `hist_E8_…`.
3. **The 32.51 round-trip (Q-N1 — literal: Hearn sends back first)** (exact iconic amounts kept — Q-E2). Source: *"Satoshi movió una recompensa de minería a Mike Hearn… le propuso que si Hearn le enviaba 32.51, Satoshi le devolvería la moneda más 50."* Modelled as a 3-step sequence (Hearn must be funded before he can "send 32.51 first", so Satoshi seeds it — the real first on-chain move is Satoshi's test send, then Hearn returns it):
   - **E6** — Satoshi → Hearn **32.51 BTC** (test/seed). Spends one matured 50-BTC coinbase ⇒ **E8** 17.49 BTC change → a Satoshi address (`50 − 32.51`).
   - **E6b** — Hearn → Satoshi **32.51 BTC** (Hearn returns the coin — the round-trip send; Hearn is now a real spender, "coins never moved" dropped).
   - **E7** — Satoshi → Hearn **82.51 BTC** (returns the coin `32.51` **plus the 50 gift**), funded by the returned 32.51 + a **second** matured 50-BTC coinbase.
   - Net: Hearn ends with **82.51 BTC** and has sent exactly one tx (E6b), then goes dormant. In Step 7's single-address model E8 is a Satoshi→Satoshi-base self-send placeholder; it becomes a **real change output to a fresh address** in Step 8.
   - Requires Satoshi to hold **two** matured 50-BTC coinbases by ~18 Apr — true (he holds thousands of BTC by then).
   - Timeline: Hearn's node **enters ~12 Apr 2009** (registered earlier, idle until then); the round-trip plays out across sequential blocks ~18 Apr 2009.
4. **Reuse for E4?** Optionally fold Phase 7.3's bootstrap insert behind the same `HistoricalEvents` builders so bootstrap and player-era events share one definition list (bootstrap injects synchronously; the scheduler injects on clock-cross).

---

### Phase 7.5 — Satoshi disappearance + `FoundersWallets` readout

**Touches**: `Screens/FoundersWallets/FoundersWallets.{tscn,cs}`, `FoundersMiningService.cs`.

- **Disappearance** already lives in §2.2 / Phase 7.1 (retire when `clock ≥ floorDate ∧ confirmed ≥ 11,000`). Surface it.
- **Satoshi panel**: confirmed BTC vs **11,000** target, current power + network share, estimated blocks/date until disappearance, and post-retirement status `"Retired — last active <date>"`.
- **Hal panel**: tiny-power drip status (no target). **Hearn panel**: holdings from E6/E7, "dormant — funds never moved".
- Keep it **DEV-only** (gated like the existing `[DEV]` entries). No new persisted state — all derived from the chain + the live service.

---

### Phase 7.6 — Documentation alignment

**Touches**: `CLAUDE.md`, `Documentation/{ProjectDesignManual,DESIGN_OVERVIEW,GLOSSARY,PRIVATE_ROADMAP,PLAYER_GUIDE}.md`, `AIHelperFiles/{IMPLEMENTATION_ROADMAP,historical-founders-and-bootstrap-plan,historical-blockchain-events-research}.md`.

- Record the **founders-as-concurrent-miners** model + the **OQ-2 refinement** (§0). Add the founder-regulator (11,000 BTC by 2011-04-26) to ProjectDesignManual as its own chapter, cross-referencing the difficulty regulator (Ch. 26) it parallels.
- Add a **Historical Timeline** mapping real dates → in-game block-by-timestamp (genesis, 11 Jan Hal, 12 Jan 10 BTC, 21 Mar player start, 18 Apr Hearn, ≥ Apr-2011 disappearance).
- Promote `historical-blockchain-events-research.md` §2 (roster) and §4 (events ledger) to **canonical** for the locked roster (D3) and amounts (Q-E2).
- Mark roadmap Step 7 phases done; update the founders plan's Phase 4/6/7 from "PARKED" to "implemented in Step 7".

---

## 3b. Implementation status & test log

| Phase | Status | Notes |
|---|---|---|
| 7.1 FoundersMiningService | ✅ **Done** | `Scripts/Services/FoundersMiningService.cs` (pure controller, autoload). Satoshi regulator + Hal decay + lockstep accumulators + readout getters. No persisted state. |
| 7.2 Player-era concurrent mining | ✅ **Done & verified** | `SimulationService` feeds player+bots+founder power to `SetActiveMiningPower`; recomputes founder powers once per new block (`_lastFounderChainLen` guard around Satoshi's chain-scan); drives `DrainFounderAttempts` after the bet loops; founder blocks reuse the external-block path (`CaptureCheckpoint` + `StopPlayerOnExternalBlockMined`). `TickBots` now returns its bet count. |
| 7.3 E4 10-BTC Satoshi→Hal tx | ✅ **Done & verified in-engine** | `NodeAgent.CreateSignedTransaction(..., deterministicSalt)`; `NetworkRoot.InjectHistoricalSignedTxStatic` (idempotent via deterministic-salt txid); `HistoricalBootstrapService` injects it when the bootstrap clock crosses 12 Jan. No `InputData` note (Q-X4). |
| Block Explorer: mined-per-node | ✅ **Done** | `NetworkRoot.GetMinedBlockCountsByNode()`; `GetNodeStatusLines()` now shows `mined: N` per node so founder/player share is visible. |
| 7.4 Scheduler + Mike Hearn | ✅ **Done** | `mike_hearn` founder node (wallet + registration). `HistoricalEventScheduler` (static, hooked in `HandleMinedBlock`) injects the ~18 Apr 2009 round-trip **E6** (Satoshi→Hearn 32.51) → **E6b** (Hearn→Satoshi 32.51) → **E7** (Satoshi→Hearn 82.51), strictly sequenced via chain-confirmed state (`NetworkRoot.IsHistoricalTxConfirmedStatic`), idempotent. E8 (17.49 change) omitted — implicit change in the account model / self-send is rejected; returns in Step 8. Hearn nets +82.51, never mines. |
| 7.5 FoundersWallets readout + telemetry | ✅ **Done** | "Founder Economics [DEV]" panel (live Satoshi target/power/share/retirement, Hal decay, Hearn holdings) + Mike Hearn selector. `FoundersMiningService.AppendTelemetry` → `user://logs/founders_trace.csv` (one row/block: powers, Satoshi share + BTC, Hal/Hearn BTC, retired flag), driven from `SimulationService`. Satoshi disappearance surfaced (retirement logic in 7.1; full retire date 2011-04-26 not reachable in a short test). |
| Block Explorer: mining ⛏ for casino/founders | ✅ **Done** | `GetActiveMiningRates()` now also lists the casino (Σ active casino-pool credits) + Satoshi/Hal (regulated power) so the ⛏ indicator shows for them; Hearn correctly absent (never mines). `GetTotalActiveMiningPower()` was **decoupled** from this method (it must stay player+bots only — see the test-log bug). |
| 7.6 Docs | ⏳ Pending | CLAUDE.md / ProjectDesignManual / roadmap + plan status flips. |

**Test — 7.3 (E4):** ✅ confirmed on app open with a clean save — the 10 BTC Satoshi→Hal tx is on-chain near 12 Jan.

**Test — 7.2 (51 player-era blocks, 1-credit player, no bots), audited via `user://logs/difficulty_trace.csv`:**
- Miner split over blocks 114→164: **player 26, hal 21, satoshi 4**.
- **Satoshi ≈ 7.8%** (4/51) — on his ~10% target within small-sample variance; his regulated power solved to ~0.21 ⇒ share ~10%. ✅
- Chain **mechanically sound**: indices sequential & gap-free; timestamps monotonic; `anchor = 585.14 × configuredPower`; `realizedPower = difficulty × 99.98 / solveSec` self-consistent on every row; `configuredPower` falls 2.2157 → 1.9350 across the run = **Hal's decay working**. ✅
- **Hal ≈ 41%** — correct for `P = 1.0` against a lone 1-credit player; **kept on purpose** (Q-N2): he shrinks relatively once the network grows. Not a bug.

**Test — 7.4 + 7.5 (durability run, blocks 114→281 = 168 blocks, 1-credit solo player), audited via `user://logs/founders_trace.csv`:**
- **Hal disappearance — exact.** `halPower` decays `1.0 → 0`, hitting **0.0000 at block 275, timestamp = 9 Aug 2009** (day 221) — precisely the configured `HalDecayEnd`. Then DORMANT. ✅
- **7.4 Hearn round-trip — exact.** Block 145 (timestamp **18 Apr 2009**): `satoshiBtc` −32.51 (E6). `hearnBtc` reaches **82.51** by block 148 and holds for the rest of the run; `hearnBtc = 0` during 145–147 is correct (E6b pending reserves the funds). ✅ Net Hearn +82.51.
- **Durability — passed.** 168 blocks, indices sequential & gap-free, timestamps monotonic, no crash, `satoshiRetired = 0` throughout. ✅
- **Hal ended at 1810 BTC** — expected for the 1-credit solo setup (Hal ~45% early); the accepted Q-N2 tradeoff, not an error.
- 🐞 **Bug found & fixed — Satoshi's real share was ~15%, not ~10%** (25/168 blocks mined by Satoshi). Root cause: the 7.5 mining-indicator change added founders/casino to `GetActiveMiningRates()`, and `GetTotalActiveMiningPower()` summed that method — so `otherMinersPower` (must be player+bots only) double-counted the founders into Satoshi's own `W_others`, inflating his power (`0.34` vs the correct `0.21`). **Fix:** `GetTotalActiveMiningPower()` now computes player+bots directly, decoupled from the display method. **Confirmed by a short re-run** (blocks 113→165): Satoshi mined **5/53 = 9.4%** (back on his ~10% target); `satoshiPower ≈ 0.21`; the displayed `satoshiShare` now matches the real share exactly (`0.2175 / 0.0983 → total 2.213 = player + hal + satoshi`).

---

## 4. Suggested build order & dependencies

```
7.1 FoundersMiningService (controller, DEV-readable)
      └─> 7.2 player-era concurrent mining (the keystone integration)
7.3 E4 10-BTC tx (bootstrap)         ── independent of 7.1/7.2, can land early
7.4 HistoricalEventScheduler + Hearn ── needs the HandleMinedBlock hook; reuses 7.3's builders
7.5 disappearance + FoundersWallets readout  ── needs 7.1/7.2
7.6 docs                              ── last
```

7.1 + 7.2 are the heart (the regulated-concurrent-mining engine). 7.3 is small and standalone. 7.4 is the second new service. 7.5/7.6 are surfacing + docs.

---

## 5. File checklist

| File | Phase | Action |
|---|---|---|
| `Scripts/Services/FoundersMiningService.cs` | 7.1 | **new** — power model, Satoshi regulator, accumulators, retirement |
| `project.godot` | 7.1 | register `FoundersMiningService` (+ `HistoricalEventScheduler`) autoloads |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | 7.1,7.2,7.4 | `GetFounderConfirmedSpendableBtc`; founder-power into `SetActiveMiningPower`; founder-block bookkeeping; scheduler hook in `HandleMinedBlock`; register `mike_hearn` |
| `Scripts/Services/SimulationService.cs` | 7.2 | drive `DriveFounderAttempts` after bet loops; founder blocks → `CaptureCheckpoint` + `StopPlayerOnExternalBlockMined` |
| `Scripts/Services/HistoricalBootstrapService.cs` | 7.3 | inject the 12 Jan 10 BTC Satoshi→Hal tx (flat bootstrap kept; **not** regulated — Q-bootstrap) |
| `Scripts/Services/HistoricalEventScheduler.cs` | 7.4 | **new** — clock-cross scripted-tx injector, chain-derived fired-state |
| `Scripts/Services/WalletInitializationService.cs` | 7.4 | create `mike_hearn` wallet |
| `Scripts/BlockchainPort/Blockchain/WalletModels.cs` | 7.4 | Hearn `FounderWalletState` (if not generic already) |
| `Screens/FoundersWallets/FoundersWallets.{tscn,cs}` | 7.5 | Satoshi target/power/retirement + Hal/Hearn panels |
| `CLAUDE.md`, `Documentation/*`, `AIHelperFiles/*` | 7.6 | model, timeline, status flips |

---

## 6. Open questions

### 6.1 Resolved this round

- **Q-A1 — Satoshi's share.** ✅ **RESOLVED — the ~10% is a historical requirement, not a tunable.** The regulator outputs whatever share lands 11,000 BTC by 2011-04-26; never capped or hand-set to make the player richer (§2.2, Phase 7.2 gameplay note).
- **Q-A2 / Q-H1-3 — Hal's curve.** ✅ **RESOLVED from the source file.** Hal mines in the player era at baseline `P=1.0` **decaying linearly to 0 by ~Aug 2009** (his ALS turning point), then dormant/receive-only; no target. His 2013 medical sell-off + 2014 dormancy flag stay out of Basic-Mode scope (§2.3).
- **Q-M1/M2 — Hearn.** ✅ **RESOLVED — never mines** ("sin minería documentada"); enters ~12 Apr 2009, does the 32.51 round-trip with Satoshi (sends **one** tx — Q-N1), ends holding +82.51 BTC, then dormant (§2.3, Phase 7.4).
- **Q-S1 — Satoshi after retirement.** ✅ **RESOLVED — frozen forever in Basic Mode.** A "Satoshi returns / move the coins" event is left as an explicit open door for the **full version / DLCs**, not Basic Mode.
- **Q-bootstrap — regulate the bootstrap?** ✅ **RESOLVED — no.** Keep the flat bootstrap baseline (≈5,550 BTC on-ramp) untouched in Basic Mode; only the player era is regulated. Noted as a *possible* future difficulty-modulation lever, but the baseline is not touched now.

### 6.2 New questions — all resolved this round

- **Q-N1 — Hearn tx structure.** ✅ **RESOLVED — literal round-trip.** Hearn sends 32.51 back to Satoshi (seeded by Satoshi's test send) and Satoshi returns the coin + 50 = 82.51. "Coins never moved" is **dropped** — Hearn signs one outgoing tx. Modelled as E6 (Satoshi→Hearn 32.51, + E8 17.49 change) → E6b (Hearn→Satoshi 32.51) → E7 (Satoshi→Hearn 82.51). See Phase 7.4.
- **Q-N2 — Hal's player-era decay shape.** ✅ **RESOLVED (refined after the 7.2 test).** Keep `HalBaselinePower = 1.0` as-is — do **not** lower it. Endpoint moved to **9 Aug 2009**. Hal shrinks **relatively** as the network grows (gradual miners join up to 9 Aug); the linear `P=1→0` fade is the v1 stand-in for the network-coupled fade. See §2.3 note.
- **Q-N3 — Hal's node-as-Satoshi-backup detail.** ✅ **RESOLVED — ignored for Basic Mode**, *not discarded* — kept on the table for later versions (no mechanical effect now).
- **Q-N4 — Hal in Satoshi's `W_others`.** ✅ **RESOLVED — confirmed.** While Hal mines (Mar–Aug 2009) he counts as a competing miner in Satoshi's share denominator, so Satoshi's ~10% target self-adjusts and stays exact (§2.2).

---

*Created: 2026-06-25 — re-activates `historical-founders-and-bootstrap-plan.md` Phases 4/6/7 on the Step-4 candidate engine. Pairs with `historical-blockchain-events-research.md`.*
