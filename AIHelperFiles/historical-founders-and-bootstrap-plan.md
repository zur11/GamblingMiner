# Historical Founders & Genesis Bootstrap — Implementation Plan

**Status**: **Phases 1–3 IMPLEMENTED** (Step 1 — compiles clean; pending in-engine verification). All 10 open questions resolved (see §6). **Active lead plan** (roadmap Steps 1–3, 5 — see `IMPLEMENTATION_ROADMAP.md`). Next: Phase 0 (weight + lottery) → Phases 4–5 (bootstrap + Satoshi targeting). The companion research doc `historical-blockchain-events-research.md` must be filled before Phases 5–6 are coded.

> **Step 1 done (founder identity):** `FounderWalletState` (`WalletModels.cs`); `WalletInitializationService` creates/persists `user://satoshi_wallet_state.json` + `user://hal_wallet_state.json`; `NetworkRoot` registers `satoshi`/`hal` nodes and rewrites the genesis + block-2 coinbase recipient to Satoshi's derived `gm1q…` (genesis stays `IsSpendable=false`); `Screens/FoundersWallets/` dev scene + `SceneManager`/`MainMenu` wiring. Founders exist as registered nodes but do **not** mine yet (lottery = Phase 0/Step 2). Deferred: 100-byte `InputData` cap. Requires a clean `user://blockchain/` (OQ-9).
**Goal**: Make the early-Bitcoin opening of GamblingMiner historically faithful. Introduce **Satoshi Nakamoto** and **Hal Finney** (and later **Mike Hearn**) as special founder mining nodes (no SC/BTC needed to mine, casino-like), pre-mine a real-but-fast blockchain from the genesis instant up to **21 March 2009** so the player always starts on that day, record the famous **12 Jan 2009 Satoshi→Hal 10 BTC** transaction (and the April 2009 Mike Hearn transfers), retire Satoshi **no earlier than 26 April 2011** at a fractal target of **11,000 BTC** (1% of his real ≈1.1 M BTC), and review/generalise how extra data (the genesis bank-bailout headline) is stored in blocks.

This plan is the historical-accuracy counterpart to `100x-time-scale-migration-plan.md` and builds on the wallet/identity stack from `btc-wallet-system-plan.md`. The per-character on-chain event data lives in `historical-blockchain-events-research.md`.

---

## 1. Source Material (attached `Los Primeros Mineros de Bitcoin.txt`)

| Real date | Event | Relevance to our sim |
|---|---|---|
| 2009-01-03 | Satoshi mines the genesis block, 50 BTC | Already modelled (genesis coinbase). Must point at Satoshi's derived address. |
| 2009-01-08 | Bitcoin v0.1 released to the cypherpunk mailing list | Flavour only; optional in-game note. |
| 2009-01-11 | Hal Finney: *"Running bitcoin"* — first external miner | Hal node joins mining from this date. |
| 2009-01-12 | First person-to-person tx: Satoshi → Hal **10 BTC** (spent from block-9 coinbase; confirmed real block 170) | Must be reproduced as a real signed tx in the block whose **timestamp** ≈ 12 Jan. |
| Jan–Mar 2009 | Satoshi dominant miner; Hal the only documented external miner; community ~handful | Bootstrap: Satoshi mines almost everything, Hal exactly 3, no one else. |
| 2009-04-18 15:55:19 UTC | Satoshi → Mike Hearn **32.51 + 50.00 BTC** (real block 11408) + automatic **17.49 BTC** change back to Satoshi | After player start; Mike Hearn joins as a third founder node. See research doc. |
| 2010-12-12 / 13 | Satoshi's last public post ("0.3.19 DoS limits"), last login next day | Earliest allowed disappearance reference; our retirement is **no earlier than 2011-04-26**. |
| (historical estimate) | Satoshi's addresses hold ≈ **1,100,000 BTC** | **Fractal target = 1% = 11,000 BTC** at disappearance (mirrors the project's existing 1%-of-real-BTC supply convention). |

### Fractal scale reminder (from the 100X migration)

```
1 bet            = 100 in-game seconds = 1 nonce attempt
Expected block   ≈ 585 attempts → 58,500 in-game seconds ≈ 0.677 in-game days
Blocks / day     ≈ 1.477
Reward era 0     = 50 BTC/block for blocks 1–2,100 (we stay deep inside era 0 for all of this plan)
```

---

## 2. Current State (what exists today)

### Blockchain / mining (`Scripts/BlockchainPort/…`)

- **Genesis** (`BlockchainService` ctor + `CreateGenesisCoinbase`): nonce `100`, hash `"0"`, prev `"0"`, timestamp `2009-01-03 18:15:05Z`. Coinbase **50 BTC**, `IsSpendable = false`, recipient = `SatoshiAddress = "1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa"` — a **real Bitcoin base58 address**, inconsistent with our `gm1q…` bech32 scheme. The genesis headline is stored on the coinbase via `InputDataText` + `InputDataHex` (`GenesisHeadline = "The Times 03/Jan/2009 Chancellor on brink of second bailout for banks."`). This is realistic — real Bitcoin puts the headline in the coinbase scriptSig.
- **Bootstrap block 2** (`EnsureSecondBlockBootstrapPendingTx`): injects a second 50 BTC coinbase to `SatoshiAddress` (`IsSpendable = true`) when the chain length is 1. This is the only "Satoshi gets paid" logic beyond genesis.
- **Satoshi is not a node.** There is no Satoshi `NodeAgent`, no seed phrase, no wallet, no mining power — only a hardcoded recipient string.
- **Nodes today**: `player`, `bot_1..bot_4` (miners), `non_miner_1..10` (holders), `casino`. All registered in `NetworkRoot.EnsureInitialized()`. Each `NodeAgent` owns a `BlockchainService`; on load every node's chain is replaced with the player's chain (`ApplyStateFromSnapshot`). Wallets are `gm1q…` (secp256k1 → Hash160 → bech32).
- **Mining is bet-driven only.** `TryMineSingleNonceAttempt(nodeId, …, minedAtUnixMs)` performs exactly one nonce attempt and stamps the block with an explicit in-game timestamp. The player mines on each manual/auto bet (`ProcessBlockchainAttemptForBet`); bots mine via `BotAutoBetRunner` ticks **only while an autobet session is running**. There is **no autonomous mining when the player is idle** and no concept of relative hashrate between nodes — whoever's tick happens to find a valid hash wins.
- **Candidate block** is implicit: `{ transactions = PendingTransactions, index }` plus a cached `_candidateNonce`. Nodes broadcast txs to each other but there is no explicit, independently-controllable per-node candidate template.
- **Reward / halving** (`NetworkRoot`): 50 BTC, halving every 2,100 blocks, cap 34, 210,000 BTC total. Coinbase always pays the mining node's own address.

### Identity / wallet (`btc-wallet-system-plan.md`, all done)

- `WalletInitializationService.EnsureAll()` creates player + casino wallets (seed words + base `gm1q…`), `BotWalletRegistry` creates bot wallets, all at startup before `NetworkRoot` builds nodes. Dev scenes already exist for casino (`CasinoFinances`) and bots (`BotsBtcWallets`) — these are the exact templates for a founders scene.

### Time (`CalendarTimeService`)

- Game start `2009-01-03 18:15:06` local. `EnsureGameEpochInitialized()` seeds the clock on first launch. There is **no** mechanism to fast-forward the clock to 21 March at first launch, and **no** autonomous block production during that fast-forward.

### Conflicts to resolve

1. `SatoshiAddress` is a base58 mainnet address, not `gm1q…`. Genesis + block-2 payouts must move to Satoshi's **derived** base address.
2. Satoshi/Hal must become real nodes with seed phrases (so the dev scene can show seed + base address like casino), but they must mine **without** consuming SC/BTC and **without** the player betting.
3. There is no relative-hashrate model, so "Satoshi mines ~X% of blocks, recalculated each block" cannot be expressed cleanly today.
4. There is no idle/bootstrap mining loop, so "pre-mine to 21 March as fast as possible" needs a new driver.

---

## 3. Recommendation — should we do per-node candidate blocks first?

**Yes — design and implement an explicit per-node candidate + relative-hashrate model BEFORE (or as Phase 0 of) the founders work, but split the founders work so the identity/wallet half does not wait on it.**

Reasoning:

- The two headline requirements — *"Satoshi's mining power recalculates every block to land on 10,000 BTC"* and *"each run starts at a slightly different moment on 21 March with organic variety"* — are both fundamentally **"who wins the next block, and with what probability"** problems. Today that is an emergent accident of which bet ticks first. There is no knob to turn. A relative-hashrate / weighted-candidate model **is** that knob.
- Without it, the only way to force Satoshi's share is hacks (e.g. "every Nth block is Satoshi's"), which are brittle, hard to retune as players/bots join, and visibly non-random.
- The bootstrap pre-mine and the disappearance ramp are far simpler to express as "set each node's hashrate weight, run the shared lottery N times" than as bespoke loops.
- The candidate model is also a documented roadmap item (P4 block template builder) and a prerequisite for the casino community pool (`btc-pools-hardware-plan.md`) and fees — so it is not throwaway work; it unblocks several lanes at once.

**However**, the *identity* half of this plan has no such dependency:

- Making Satoshi & Hal nodes with seed phrases and `gm1q…` addresses, fixing the genesis/early coinbase recipients, building the `FoundersWallets` dev scene, and recording the 12 Jan 10 BTC tx can all ship **before** the candidate model exists. They only need the wallet stack that is already done.

**Proposed sequencing:** Phase 1–3 (identity, address fix, dev scene, the famous tx as a *scripted* historical insert) can land immediately. Phase 0 (candidate/hashrate model) is the prerequisite for Phases 4–5 (dynamic Satoshi targeting + the randomised bootstrap simulation). Phase 0 is broken out below with enough detail to start, and its open questions are listed.

The variable **start time** on 21 March is actually achievable *today* by stamping the final bootstrap block's timestamp at a random moment within 21 March — the candidate model is what makes the **content** of each run (Hal's blocks, nonces, tx ordering) genuinely vary, not the start clock.

---

## 4. Target Architecture

### New special nodes

| Node id | Role | Enters | Mines? | Needs SC/BTC to mine? | Seed in dev scene? | BTC target |
|---|---|---|---|---|---|---|
| `satoshi` | Founder, dominant early miner | Genesis (3 Jan 2009) | Yes (weighted) | No | Yes | **11,000 BTC by disappearance** |
| `hal` | Founder, first external miner | 11 Jan 2009 | Yes (weighted, exactly 3 in bootstrap) | No | Yes | None (realism only) |
| `mike_hearn` | Founder, early collaborator | After player start (~April 2009) | TBD (see research doc) | No | Yes | None (realism only) |

All three behave like the casino node in that they are casino-class entities (no betting required), but unlike the casino they **do** mine. They are created in `WalletInitializationService.EnsureAll()` (seed → base `gm1q…`) and registered as `NodeAgent`s in `NetworkRoot` like the casino node. **Founder nodes stay available for game-design dynamics for as long as they are historically alive**, even when not mining (e.g. Mike Hearn pre-April, Hal/Satoshi post-bootstrap).

### New service: founders / hashrate controller

`Scripts/Services/FoundersMiningService.cs` (or extend `NetworkRoot`) owns:

- Each node's **hashrate weight** (relative probability of winning the shared block lottery).
- Satoshi's **per-block target recalculation** toward 10,000 BTC by the disappearance date.
- Hal's **join date** (11 Jan) and his small fixed quota (2–3 spaced blocks).
- The **disappearance event** (early 2011): Satoshi's weight → 0, node flagged retired.

### New driver: bootstrap simulation

`Scripts/Services/HistoricalBootstrapService.cs` runs **once on first launch**, after wallets + nodes exist and before the player can act:

- Repeatedly mines blocks (founders only) assigning each an in-game timestamp marching from `2009-01-03 18:15:06` forward at the fractal cadence, until a block lands on **21 March 2009** at a randomised time-of-day.
- Advances `CalendarTimeService` to that final timestamp so the player starts on 21 March.

---

## 5. Phases

### Phase 0 — Per-node candidate blocks + relative hashrate  *(prerequisite for Phases 4–5)*  ✅ IMPLEMENTED (Step 2 — minimal: weight + lottery)

> **Step 2 done:** `NodeAgent.HashrateWeight` (default 1.0); `NetworkRoot.RunWeightedBlockLottery(minerNodeIds, minedAtUnixMs?, rng?)` picks a weighted winner and mines one real PoW block for it via `MineAndBroadcastBlock`; `SetHashrateWeight`/`GetHashrateWeight`; injectable RNG for deterministic bootstrap. Verified via the "Mining Lottery [DEV]" panel in FoundersWallets. **Task 1 (candidate-template refactor) intentionally deferred to the P4 block-template-builder per OQ-1** — only the weight + lottery were needed for the founders.

**Files**: `NodeAgent.cs`, `NetworkRoot.cs`, new `Scripts/BlockchainPort/Simulation/BlockCandidate.cs` (optional).

**Goal**: give every mining node an explicit candidate template and a hashrate weight, and centralise "run the next-block lottery" so that win probability ∝ weight.

Tasks:

1. **Candidate template** — formalise the implicit `{ transactions, index, prevHash, _candidateNonce }` into a small `BlockCandidate` the node rebuilds when its mempool view or tip changes. (Largely a refactor of `TryMineSingleNonceAttempt`.) This lets different nodes legitimately hold different candidates (different tx selection, different coinbase recipient).
2. **Hashrate weight** — add `double HashrateWeight` per node (default 1.0 for player/bots; controller-driven for founders). 
3. **Weighted lottery** — a `NetworkRoot.RunWeightedBlockLottery(activeMiners, minedAtUnixMs)` that, per simulated tick, picks a winner with probability ∝ weight and produces a single valid block for that winner. Used by the bootstrap loop and (optionally) by a future idle-mining loop. Bet-driven player mining stays as-is (the player's own bets are their hashrate).
4. **Determinism / seeding** — accept an injectable RNG so bootstrap runs are reproducible in tests but random in play.

**Why this shape**: it does not disturb the existing bet-driven player path, but gives the founders a controllable share without faking PoW. See OQ-1/OQ-2 for the open design points.

---

### Phase 1 — Satoshi & Hal as mining nodes

**Files**: `WalletInitializationService.cs`, new `Scripts/BlockchainPort/Blockchain/WalletModels.cs` records (`FounderWalletState`), `NetworkRoot.cs`.

1. Add `FounderWalletState(string[] SeedWords, string BaseAddress, string FounderId)` and persist to `user://satoshi_wallet_state.json` and `user://hal_wallet_state.json`.
2. In `EnsureAll()`, after casino, create both founder wallets (3-word seed → `DeriveGmAddress`), same pipeline as casino.
3. In `NetworkRoot.EnsureInitialized()`, register `satoshi` and `hal` `NodeAgent`s with keys derived from their seeds (mirror the casino block at lines 68–80).
4. Founders are **excluded** from bet-driven loops and from the bot recirculation scheduler (`ScheduleBotTransactionsAfterBlock` already iterates only `BotWalletRegistry.MinerBots`, so no change needed there).

**No dependency on Phase 0** — founders exist as nodes regardless of how they win blocks.

---

### Phase 2 — Fix genesis & early coinbase to Satoshi's derived address + inscription review

**Files**: `BlockchainService.cs`, `NetworkRoot.cs`.

1. Replace the base58 `SatoshiAddress` constant usage in coinbase recipients with **Satoshi's derived `gm1q…` address**. Because the genesis is built in the `BlockchainService` constructor (before wallets are guaranteed), do the rewrite in `NetworkRoot.NormalizeGenesisTimestampAcrossNodes()` (rename → `NormalizeGenesisAcrossNodes()`): set genesis coinbase recipient = Satoshi's genesis address, keep `IsSpendable = false`, keep the headline `InputData`.
2. Update `EnsureSecondBlockBootstrapPendingTx()` recipient to a Satoshi address (or fold it into the Phase 5 bootstrap so block 2 is just Satoshi's first mined block).
3. Keep `SatoshiAddress` base58 constant only as documentation/reference (clearly commented as "historical real address, not used for payouts"). Optional educational tooltip may surface it (Q-X3).
4. **⭐ Patoshi multi-address direction (Q-X1 / Q-S2 resolved).** Satoshi is *not* a single-address node. The target is a realistic UTXO-style model where Satoshi **derives a fresh passphrase address per coinbase reward (and per deposit)** — the "Patoshi pattern" — using the existing passphrase-wallet derivation. This is the same mechanic that makes UTXO realism tangible for the player later. The single-base-address form is the **testing-stage shortcut only**; the FoundersWallets scene must be able to list Satoshi's many derived addresses. (Strict one-address-per-receive pending §6 research on whether any real Satoshi address was reused.)
5. **Inscription mechanism review (Q-X4 resolved).** The current `Transaction.InputDataText` + `InputDataHex` pair is a good, realistic model (hex is canonical, text is a decode convenience; the genesis headline lives on the coinbase exactly like real Bitcoin). **For now, only the genesis block carries an inscription.** Keep the system fully wired and ready to attach messages to other events/contexts later. Conventions to preserve for the future (no code now):
   - **Coinbase message** (miner-inserted data, e.g. the headline) — any miner may stamp its coinbase `InputData`.
   - **OP_RETURN-style note** (future) — a zero-value, unspendable tx carrying only `InputData`, for player/bot on-chain messages.
   - Add a guard/length cap on `InputData` = **100 bytes** (hex ≤ 200 chars) to mirror real coinbase scriptSig limits (realism + save-size safety).

---

### Phase 3 — `FoundersWallets` dev scene

**Files**: new `Screens/FoundersWallets/FoundersWallets.{tscn,cs}`, `SceneManager.cs`, `MainMenu.{tscn,cs}`.

- Clone the `CasinoFinances` scene pattern (seed words always viewable, base address + Copy, balance refresh, passphrase sub-wallet, send). 
- Wallet panels for **Satoshi**, **Hal** (and room for **Mike Hearn**), switched by a tab or segmented control.
- **Dev-only** entry on MainMenu (label `"Founders Wallets [DEV]"`), gated like `BotsBtcWallets [DEV]` / `Casino Finances [DEV]`.
- Satoshi panel additionally shows: current BTC vs **11,000** target, current hashrate weight, estimated blocks until disappearance, disappearance date — a live readout of the Phase 4 controller. It must also **list Satoshi's many derived (Patoshi) addresses** with per-address balances, since he uses a fresh address per reward (Q-X1).
- Passphrase wallets supported (same mechanism as casino) — "we may find a use later; not a priority" → keep the capability, no bespoke logic.

---

### Phase 4 — Satoshi dynamic targeting (11,000 BTC, retires no earlier than 26 Apr 2011)  *(needs Phase 0)*

**Files**: `FoundersMiningService.cs`, `NetworkRoot.cs`.

`SatoshiTargetBtc = 11000` (soft target — see OQ-4, excludes the unspendable genesis 50). `SatoshiEarliestDisappearance = 2011-04-26` (hard floor — never retire before this).

Per-block recalculation (run whenever a block is mined while Satoshi is active):

```
floorDate              = 2011-04-26                  // never retire before this
blocksUntilFloor       = ceil( inGameDaysUntil(floorDate) * 1.477 )   // >=0; 0 once past floor
btcRemaining           = max(0, 11000 - satoshiConfirmedBtc)
reward                 = current era reward (50 BTC in this window)

if (clock < floorDate):
    # Pace the ramp so he reaches ~11,000 around the floor date, never sooner.
    targetSatoshiShare     = clamp01( btcRemaining / max(1, blocksUntilFloor) / reward )
    satoshi.HashrateWeight = shareToWeight(targetSatoshiShare, otherActiveMinersTotalWeight)
else:
    # Past floor and still short: ramp power EXPONENTIALLY to finish ASAP, then retire.
    satoshi.HashrateWeight = otherActiveMinersTotalWeight * pow(GROWTH, blocksPastFloor)
```

`shareToWeight` converts a desired win-share `s` against the rest of the field `W_others` into a weight: `w = s/(1-s) * W_others` (clamped). When few miners are online (bootstrap), `s→1` ⇒ Satoshi mines ~everything; as players/bots join, the same target naturally lowers his share. **If the player mines so little that Satoshi is still short at the floor date, time is "sacrificed": he keeps mining past 26 Apr 2011 with exponentially rising power until he hits 11,000, then retires (Phase 7).**

**Sanity numbers** (floor date 2011-04-26 ≈ 1,235 blocks from genesis; 11,000 BTC = 220 blocks @ 50):
- Bootstrap (genesis→21 Mar, ~114 blocks): Satoshi ~111, Hal exactly 3 ⇒ Satoshi ≈ 5,550 BTC at player start.
- Player start → floor date (~1,121 blocks): Satoshi needs ~109 more ⇒ ~10% share, auto-tuned each block.

---

### Phase 5 — Historical bootstrap simulation (genesis → 21 March 2009)  *(needs Phase 0)*

> **Idle-mining policy (OQ-2 resolved):** Autonomous mining (without the player betting) happens **only** during this bootstrap window (3 Jan – 21 Mar 2009), and only `satoshi` + `hal` via the weighted lottery. **From the moment the player starts, in-game time ALWAYS follows the player's bets** — the player appears first, then miner bots are introduced (in that order), all bet-driven. Autonomous time advancement is reserved for possible future expansions/DLC/online-multiplayer only.

**Files**: new `Scripts/Services/HistoricalBootstrapService.cs`, hooked from `CalendarTimeService._Ready()` after `WalletInitializationService.EnsureAll()` and before `EnsureGameEpochInitialized()`; first-launch only (guard on a `user://bootstrap_done.flag` or chain length).

Loop (founders only, via `RunWeightedBlockLottery`):

1. Start clock at genesis instant. Active miners: `satoshi` only until Hal's join timestamp (11 Jan), then `satoshi` + `hal`.
2. Hal weight tuned to yield **exactly 3 blocks**, spaced (~12 Jan, ~early Feb, ~early Mar — a low fixed weight active only in those date windows). Satoshi takes every other block.
3. Each produced block gets `minedAtUnixMs` = running clock advanced by ~`58,500 ± jitter` in-game seconds (jitter from the geometric nature of PoW, drawn from the RNG) so timestamps look organic.
4. Insert the **12 Jan tx** (Phase 6) into the block whose timestamp first crosses 12 Jan.
5. Stop when the next block timestamp would land in **21 March 2009**; place it at a **random time-of-day within 21 March**, then set `CalendarTimeService` to that timestamp and persist.
6. Run Phase 4 recalculation each iteration so Satoshi tracks toward target during bootstrap too.

Result: first launch leaves a ~110-block chain, Satoshi ≈ 5,550 BTC, Hal ≈ 100–150 BTC, the famous tx on-chain, and the player dropped into a random moment of 21 March 2009.

**Note on "always different start"**: the time-of-day randomisation (step 5) already varies the start moment without Phase 0. Phase 0 adds genuine variety to *which* blocks Hal mined, nonce values, and tx placement.

---

### Phase 6 — The 12 Jan 2009 Satoshi → Hal 10 BTC transaction

**Files**: `HistoricalBootstrapService.cs` (insertion), or a small `HistoricalEvents` helper.

- Build a **real signed** tx: sender = Satoshi node, recipient = Hal base address, amount 10 BTC, signed by Satoshi's key (`NodeAgent.CreateSignedTransaction`). Requires Satoshi to already hold ≥10 spendable BTC — true by 12 Jan (he will have mined several blocks; coinbase from block N spendable at N+1, see scheduled-bot-transactions timing).
- Optional `InputData` note: `"First person-to-person Bitcoin transaction — Satoshi → Hal, 12 Jan 2009"`.
- Confirm it in the block whose timestamp ≈ 12 Jan. Document that in real Bitcoin this was **block 170**; in our fractal it lands around **block ~13** (≈ 9 in-game days × 1.477), because heights compress while *dates* are preserved — dates are the source of truth, not heights.

---

### Phase 7 — Satoshi disappearance (≥ 26 Apr 2011, once 11,000 BTC reached)

**Files**: `FoundersMiningService.cs`.

- Retirement fires when **both** conditions hold: clock ≥ `2011-04-26` **and** `satoshiConfirmedBtc ≥ 11,000`. (If the player under-mines, the exponential ramp in Phase 4 closes the gap after the floor date.) Then set Satoshi `HashrateWeight = 0`, flag `satoshi` retired, stop targeting. His BTC stays untouched on-chain forever (a permanent "lost/dormant whale", consistent with the `non_miner` dormant-wallet sim in `btc-wallet-system-plan.md` Task 5.3).
- Hal keeps mining at his small weight (no target). His later real-life timeline (active to ~2014) can be a future refinement.
- Mike Hearn and any other founders remain available for game dynamics while historically alive, independent of Satoshi's retirement.
- Surface the event in the FoundersWallets dev scene (status: "Retired — last active <date>").

---

### Phase 8 — Documentation alignment

**Files**: `CLAUDE.md`, `Documentation/{DESIGN_OVERVIEW,GLOSSARY,PRIVATE_ROADMAP,PLAYER_GUIDE,ProjectDesignManual}.md`.

- Replace the "Genesis block … reward to SatoshiAddress (base58)" notes with the founder-node model and the `gm1q…` derived address.
- Add a **Canonical Decisions** row: *Founders = Satoshi (target 11,000 BTC, retires ≥ 26 Apr 2011) + Hal (joins 11 Jan, 3 bootstrap blocks, no target) + Mike Hearn (joins ~Apr 2009, no target); player starts 21 Mar 2009 after bootstrap.*
- Add a **Historical Timeline** section mapping real dates → in-game block-by-timestamp (genesis, 11 Jan Hal, 12 Jan 10 BTC tx, 21 Mar player start, 18 Apr Mike Hearn transfers, ≥ Apr-2011 disappearance). Source data: `historical-blockchain-events-research.md`.
- Document the inscription/`InputData` convention (coinbase message vs future OP_RETURN note) in `ProjectDesignManual.md`.

---

## 6. Open Questions — ALL RESOLVED (2026-06-19)

**OQ-1 — Candidate/hashrate model scope.** ✅ **RESOLVED:** weight + weighted lottery now; full per-node candidate-template refactor deferred into the P4 block-template-builder.

**OQ-2 — Idle mining.** ✅ **RESOLVED:** autonomous (no-bet) mining happens **only** during the 3 Jan – 21 Mar 2009 bootstrap, Satoshi + Hal via lottery. After bootstrap the player appears first, then miner bots are introduced (in that order), and **in-game time always follows the player's bets**. Autonomous time advancement is reserved for future expansions/DLC/online-multiplayer only.

**OQ-3 — Satoshi's disappearance date.** ✅ **RESOLVED:** single constant `SatoshiEarliestDisappearance = 2011-04-26`; **never retire before** this. If Satoshi is still short of target at that date, sacrifice as much extra time as needed but ramp his mining power **exponentially** to finish ASAP.

**OQ-4 — Target: hard cap or soft target?** ✅ **RESOLVED:** target corrected to **11,000 BTC** (1% of his real ≈1.1 M BTC). Soft target: weight→0 once ≥ 11,000, small overshoot from the final block accepted.

**OQ-5 — Hal's block count & timing.** ✅ **RESOLVED:** exactly **3** during bootstrap (~12 Jan, ~early Feb, ~early Mar). All founder characters stay available for game dynamics while historically alive. Full per-character on-chain in/out data to be gathered in the new research doc (see below).

**OQ-6 — Bootstrap cost / first-launch time.** ✅ **RESOLVED:** synchronous, behind a brief themed loading panel.

**OQ-7 — Scene name.** ✅ **RESOLVED:** `FoundersWallets` confirmed. It will likely also host **Mike Hearn**'s wallet (his blockchain in/out still to be designed; he enters after the player).

**OQ-8 — Genesis recipient & spendability.** ✅ **RESOLVED:** genesis stays `IsSpendable = false`; the 11,000 BTC target is spendable BTC and **excludes** the genesis 50.

**OQ-9 — Save migration.** ✅ **RESOLVED:** clean-save break — `user://blockchain/` is always deleted (user confirmed this is routine).

**OQ-10 — InputData size cap.** ✅ **RESOLVED:** cap at **100 bytes** (hex ≤ 200 chars), mirroring real coinbase scriptSig.

### New work item from OQ-5/OQ-7

Create and fill **`historical-blockchain-events-research.md`** — a structured catalogue of every founder/early-character on-chain in/out event, grounded in historical data, organised first as a **questionnaire** (the data I need in order to build a definitive character table with dates and characteristics). Draft created alongside this update.

---

## 7. File Checklist

| File | Phase | Action |
|---|---|---|
| `Scripts/BlockchainPort/Simulation/NodeAgent.cs` | 0 | `HashrateWeight`; candidate template (optional) |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | 0,1,2 | weighted lottery; register `satoshi`/`hal`; genesis recipient fix |
| `Scripts/BlockchainPort/Blockchain/BlockchainService.cs` | 2 | genesis/coinbase recipient via founder address; `InputData` cap |
| `Scripts/BlockchainPort/Blockchain/WalletModels.cs` | 1 | `FounderWalletState` record |
| `Scripts/Services/WalletInitializationService.cs` | 1 | create Satoshi + Hal (+ Mike Hearn) wallets |
| `Scripts/Services/FoundersMiningService.cs` | 4,7 | hashrate targeting (11,000) + disappearance (≥ 2011-04-26, exp. ramp) | 
| `Scripts/Services/HistoricalBootstrapService.cs` | 5,6 | first-launch pre-mine + 10 BTC tx |
| `Scripts/Services/CalendarTimeService.cs` | 5 | call bootstrap; land clock on 21 Mar |
| `Screens/FoundersWallets/FoundersWallets.{tscn,cs}` | 3 | dev scene (Satoshi + Hal, room for Mike Hearn) |
| `Scripts/Services/SceneManager.cs` | 3 | `FoundersWallets` enum + path |
| `Screens/MainMenu/MainMenu.{tscn,cs}` | 3 | `Founders Wallets [DEV]` button |
| `AIHelperFiles/historical-blockchain-events-research.md` | — | research questionnaire → character/event table |
| `CLAUDE.md` + `Documentation/*` | 8 | founders model, timeline, inscription convention |

---

*Created: 2026-06-19 — pairs with `100x-time-scale-migration-plan.md` and `btc-wallet-system-plan.md`.*
