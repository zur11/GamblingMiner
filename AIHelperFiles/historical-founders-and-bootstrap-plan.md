# Historical Founders & Genesis Bootstrap — Implementation Plan

**Status**: Design — not yet implemented. Next: resolve OQ-1 (candidate-blocks-first) and OQ-3 (disappearance date), then Phase 1.
**Goal**: Make the early-Bitcoin opening of GamblingMiner historically faithful. Introduce **Satoshi Nakamoto** and **Hal Finney** as special mining nodes (no SC/BTC needed to mine, casino-like), pre-mine a real-but-fast blockchain from the genesis instant up to **21 March 2009** so the player always starts on that day, record the famous **12 Jan 2009 Satoshi→Hal 10 BTC** transaction, retire Satoshi in **early 2011** at a fractal target of **~10,000 BTC**, and review/generalise how extra data (the genesis bank-bailout headline) is stored in blocks.

This plan is the historical-accuracy counterpart to `100x-time-scale-migration-plan.md` and builds on the wallet/identity stack from `btc-wallet-system-plan.md`.

---

## 1. Source Material (attached `Los Primeros Mineros de Bitcoin.txt`)

| Real date | Event | Relevance to our sim |
|---|---|---|
| 2009-01-03 | Satoshi mines the genesis block, 50 BTC | Already modelled (genesis coinbase). Must point at Satoshi's derived address. |
| 2009-01-08 | Bitcoin v0.1 released to the cypherpunk mailing list | Flavour only; optional in-game note. |
| 2009-01-11 | Hal Finney: *"Running bitcoin"* — first external miner | Hal node joins mining from this date. |
| 2009-01-12 | First person-to-person tx: Satoshi → Hal **10 BTC** (real block 170) | Must be reproduced as a real signed tx in the block whose **timestamp** ≈ 12 Jan. |
| Jan–Mar 2009 | Satoshi dominant miner; Hal the only documented external miner; community ~handful | Bootstrap: Satoshi mines almost everything, Hal a handful, no one else. |
| Late 2010 → early 2011 | Satoshi gradually withdraws, then disappears | Satoshi node retires at a configurable "early 2011" date. |
| (historical estimate) | Satoshi's addresses hold ≈ **1,000,000 BTC** | **Fractal target = 1% = 10,000 BTC** at disappearance (mirrors the project's existing 1%-of-real-BTC supply convention). |

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

| Node id | Role | Mines? | Needs SC/BTC to mine? | Seed shown in dev scene? | BTC target |
|---|---|---|---|---|---|
| `satoshi` | Founder, dominant early miner | Yes (weighted) | No | Yes | **10,000 BTC by disappearance** |
| `hal` | Founder, first external miner | Yes (weighted) | No | Yes | None (realism only) |

Both behave like the casino node in that they are casino-class entities (no betting required), but unlike the casino they **do** mine from genesis. They are created in `WalletInitializationService.EnsureAll()` (seed → base `gm1q…`) and registered as `NodeAgent`s in `NetworkRoot` like the casino node.

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

### Phase 0 — Per-node candidate blocks + relative hashrate  *(prerequisite for Phases 4–5)*

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

1. Replace the base58 `SatoshiAddress` constant usage in coinbase recipients with **Satoshi's derived `gm1q…` base address**. Because the genesis is built in the `BlockchainService` constructor (before wallets are guaranteed), do the rewrite in `NetworkRoot.NormalizeGenesisTimestampAcrossNodes()` (rename → `NormalizeGenesisAcrossNodes()`): set genesis coinbase recipient = Satoshi address, keep `IsSpendable = false`, keep the headline `InputData`.
2. Update `EnsureSecondBlockBootstrapPendingTx()` recipient to Satoshi's derived address (or fold it into the Phase 5 bootstrap so block 2 is just Satoshi's first mined block).
3. Keep `SatoshiAddress` base58 constant only as documentation/reference (clearly commented as "historical real address, not used for payouts").
4. **Inscription mechanism review** — the current `Transaction.InputDataText` + `InputDataHex` pair is a good, realistic model (hex is canonical, text is a decode convenience; the genesis headline lives on the coinbase exactly like real Bitcoin). Recommendation: keep it, and standardise two uses going forward:
   - **Coinbase message** (miner-inserted data, e.g. the headline) — realistic; any miner may stamp its coinbase `InputData`.
   - **OP_RETURN-style note** (future) — a zero-value, unspendable tx carrying only `InputData`, for player/bot on-chain messages. No code needed now; document the convention so we do not invent a second mechanism later.
   - Add a guard/length cap on `InputData` to mirror real coinbase scriptSig limits (realism + save-size safety).

---

### Phase 3 — `FoundersWallets` dev scene

**Files**: new `Screens/FoundersWallets/FoundersWallets.{tscn,cs}`, `SceneManager.cs`, `MainMenu.{tscn,cs}`.

- Clone the `CasinoFinances` scene pattern (seed words always viewable, base address + Copy, balance refresh, passphrase sub-wallet, send). 
- Two wallet panels side by side: **Satoshi** and **Hal**, switched by a tab or segmented control.
- **Dev-only** entry on MainMenu (label `"Founders Wallets [DEV]"`), gated like `BotsBtcWallets [DEV]` / `Casino Finances [DEV]`.
- Satoshi panel additionally shows: current BTC vs 10,000 target, current hashrate weight, estimated blocks until disappearance, disappearance date — a live readout of the Phase 4 controller.
- Passphrase wallets supported (same mechanism as casino) — "we may find a use later; not a priority" → keep the capability, no bespoke logic.

---

### Phase 4 — Satoshi dynamic targeting (10,000 BTC by disappearance)  *(needs Phase 0)*

**Files**: `FoundersMiningService.cs`, `NetworkRoot.cs`.

Per-block recalculation (run whenever a block is mined while Satoshi is active):

```
disappearDate          = configurable, default "early 2011" (see OQ-3)
blocksUntilDisappear   = ceil( inGameDaysUntil(disappearDate) * 1.477 )   // from current clock
btcRemaining           = max(0, 10000 - satoshiConfirmedBtc)
reward                 = current era reward (50 BTC in this window)
targetSatoshiShare     = clamp01( btcRemaining / max(1, blocksUntilDisappear) / reward )
satoshi.HashrateWeight = shareToWeight(targetSatoshiShare, otherActiveMinersTotalWeight)
```

`shareToWeight` converts a desired win-share `s` against the rest of the field `W_others` into a weight: `w = s/(1-s) * W_others` (clamped). When few miners are online (bootstrap), `s→1` ⇒ Satoshi mines ~everything; as players/bots join, the same target naturally lowers his share. This is the "power rises as players join, but accumulation is steered to 10,000" behaviour the brief asks for.

**Sanity numbers** (default disappearance ≈ 2011, ~1,080 blocks from genesis; 10,000 BTC = 200 blocks @ 50):
- Bootstrap (genesis→21 Mar, ~114 blocks): Satoshi ~111, Hal ~3 ⇒ Satoshi ≈ 5,550 BTC at player start.
- Player start → disappearance (~963 blocks): Satoshi needs ~89 more ⇒ ~9% share, auto-tuned each block.

---

### Phase 5 — Historical bootstrap simulation (genesis → 21 March 2009)  *(needs Phase 0)*

**Files**: new `Scripts/Services/HistoricalBootstrapService.cs`, hooked from `CalendarTimeService._Ready()` after `WalletInitializationService.EnsureAll()` and before `EnsureGameEpochInitialized()`; first-launch only (guard on a `user://bootstrap_done.flag` or chain length).

Loop (founders only, via `RunWeightedBlockLottery`):

1. Start clock at genesis instant. Active miners: `satoshi` only until Hal's join timestamp (11 Jan), then `satoshi` + `hal`.
2. Hal weight tuned to yield **2–3 blocks total**, spaced (e.g. a low fixed weight active only in a few date windows). Satoshi takes the rest.
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

### Phase 7 — Satoshi disappearance (early 2011)

**Files**: `FoundersMiningService.cs`.

- When the clock crosses the disappearance date: set Satoshi `HashrateWeight = 0`, flag `satoshi` retired, stop targeting. His BTC stays untouched on-chain forever (a permanent "lost/dormant whale", consistent with the `non_miner` dormant-wallet sim in `btc-wallet-system-plan.md` Task 5.3).
- Hal keeps mining at his small weight (no target). His later real-life timeline (active to ~2014) can be a future refinement.
- Surface the event in the FoundersWallets dev scene (status: "Retired — last active <date>").

---

### Phase 8 — Documentation alignment

**Files**: `CLAUDE.md`, `Documentation/{DESIGN_OVERVIEW,GLOSSARY,PRIVATE_ROADMAP,PLAYER_GUIDE,ProjectDesignManual}.md`.

- Replace the "Genesis block … reward to SatoshiAddress (base58)" notes with the founder-node model and the `gm1q…` derived address.
- Add a **Canonical Decisions** row: *Founders = Satoshi (target 10,000 BTC, retires early 2011) + Hal (joins 11 Jan, no target); player starts 21 Mar 2009 after bootstrap.*
- Add a **Historical Timeline** section mapping real dates → in-game block-by-timestamp (genesis, 11 Jan Hal, 12 Jan 10 BTC tx, 21 Mar player start, early-2011 disappearance).
- Document the inscription/`InputData` convention (coinbase message vs future OP_RETURN note) in `ProjectDesignManual.md`.

---

## 6. Open Questions

**OQ-1 — Candidate/hashrate model scope.** Do we implement the full per-node candidate template (Phase 0 task 1) now, or start with just a `HashrateWeight` on the existing implicit candidate and defer the template refactor? Minimum needed for founders is the weight + weighted lottery; the template refactor mainly benefits future fee/tx-selection work. **Recommendation: weight + lottery now, template refactor folded into the P4 block-template-builder later.**

**OQ-2 — Idle mining.** Should the network keep mining (founders + bots) while the player is idle / not betting, or only during bootstrap and player bets? Real history kept advancing regardless. This affects whether bots/Satoshi accrue blocks during downtime. **Recommendation: bootstrap-only for now; full idle-mining is a separate feature.**

**OQ-3 — Satoshi's disappearance date.** "Early 2011" — pick a canonical: last forum post (2010-12-13), last known private email to Gavin (≈2011-04-26), or a round 2011-01-01? This sets `blocksUntilDisappear` and therefore his whole ramp. **Recommendation: make it a single constant; default 2011-04-26.**

**OQ-4 — 10,000 BTC: hard cap or soft target?** If players/bots mine slower than expected, Satoshi could overshoot or undershoot. Do we hard-clamp at exactly 10,000 (stop rewarding him once reached) or accept ±a few hundred from the probabilistic ramp? **Recommendation: soft target with weight→0 once ≥10,000, accepting small overshoot from the final block.**

**OQ-5 — Hal's block count & timing.** "2 or 3 blocks spaced in time" — exact count and which date windows? And should Hal mine anything *after* 21 March / into the player era? **Recommendation: exactly 3 during bootstrap, spaced (~12 Jan, ~early Feb, ~early Mar); tiny non-zero weight afterwards.**

**OQ-6 — Bootstrap cost / first-launch time.** ~110 blocks × ~585 attempts is ~64k hashes — trivial CPU, but should it be instant (synchronous) or shown as a themed "syncing early blockchain…" progress screen? **Recommendation: synchronous but behind a brief loading panel.**

**OQ-7 — Scene name.** Confirm `FoundersWallets` (chosen here over `SatoshiWallet`) since it now hosts two wallets.

**OQ-8 — Genesis recipient & spendability.** Confirm genesis stays `IsSpendable = false` (real Bitcoin genesis 50 BTC is unspendable) and only block-2-onward Satoshi rewards are spendable — i.e. Satoshi's effective spendable target is 10,000 BTC *excluding* the genesis 50.

**OQ-9 — Save migration.** This rewrites genesis recipient and adds founder nodes. Existing `user://blockchain/` saves predate founders. Confirm we treat this as a clean-save break (delete `user://blockchain/`), consistent with prior wallet-system migrations.

**OQ-10 — InputData size cap.** What max length for coinbase/`InputData` messages (realism + JSON save size)? Real coinbase scriptSig is ≤100 bytes. **Recommendation: cap at 100 bytes (hex ≤200 chars).**

---

## 7. File Checklist

| File | Phase | Action |
|---|---|---|
| `Scripts/BlockchainPort/Simulation/NodeAgent.cs` | 0 | `HashrateWeight`; candidate template (optional) |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | 0,1,2 | weighted lottery; register `satoshi`/`hal`; genesis recipient fix |
| `Scripts/BlockchainPort/Blockchain/BlockchainService.cs` | 2 | genesis/coinbase recipient via founder address; `InputData` cap |
| `Scripts/BlockchainPort/Blockchain/WalletModels.cs` | 1 | `FounderWalletState` record |
| `Scripts/Services/WalletInitializationService.cs` | 1 | create Satoshi + Hal wallets |
| `Scripts/Services/FoundersMiningService.cs` | 4,7 | hashrate targeting + disappearance | 
| `Scripts/Services/HistoricalBootstrapService.cs` | 5,6 | first-launch pre-mine + 10 BTC tx |
| `Scripts/Services/CalendarTimeService.cs` | 5 | call bootstrap; land clock on 21 Mar |
| `Screens/FoundersWallets/FoundersWallets.{tscn,cs}` | 3 | dev scene (Satoshi + Hal) |
| `Scripts/Services/SceneManager.cs` | 3 | `FoundersWallets` enum + path |
| `Screens/MainMenu/MainMenu.{tscn,cs}` | 3 | `Founders Wallets [DEV]` button |
| `CLAUDE.md` + `Documentation/*` | 8 | founders model, timeline, inscription convention |

---

*Created: 2026-06-19 — pairs with `100x-time-scale-migration-plan.md` and `btc-wallet-system-plan.md`.*
