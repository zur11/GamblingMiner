# Scheduled Bot Transactions — Implementation Plan

Goal: make miner bots automatically send BTC to the non-miner holder bot pool after each mined block, creating organic BTC circulation observable in BlockExplorer. The recipient pool is exclusively non-miner bot addresses — this keeps early circulation contained, makes non-miner bot addresses publicly interesting, and sets the foundation for the bot referral system documented in the Future section below.

This is the next item after the BTC wallet system (Phase 1–9 of `btc-wallet-system-plan.md`).

---

## ✅ Branch Status for Merge (2026-06-21)

Everything below is either **done on this branch** or **recorded as future-gated work**. The branch is a stable, mergeable increment.

**Done & tested on `scheduled-bot-transactions`:**
- Miner-bot → non-miner recirculation (scheduler core) + **per-bot warmup** + **no-self-send** guard.
- **Fees** end-to-end (built via the candidate-block model: sender pays amount+fee, miner collects via coinbase).
- **Donation ledger** (derived from chain) + BlockExplorer **Enroll Mode** toggle.
- **Referral auction — starter (option b, gradual introduction):** staggered per-bot windows, winner = top donor at close, permanent enrollment; observable + resolved winners shown.

**Future-gated (recorded here; build in a *new* branch when the dependency exists):**
- **Winning Referral Commission payout (1%→5% SC)** → needs the **Casino Rank System** (the %), **P6 casino finances** (the payer), and the **bot betting simulation** (the SC-winnings source).
- **`Referrals` scene + Miner Referrals sub-scene**, persisted enrollment records.
- **Miner Referral conversion** (donate 2 hardware) → needs the **hardware system** (`btc-pools-hardware-plan.md`).
- Pre-Oct-2009 non-miner send programming; window/stagger tuning.

> **How to continue these:** keep them as the "Future —" + "Resolved Decisions" sections below (design is locked, just unbuilt). Don't try to build them on this branch — each waits on a system that lives on its own future branch. When that system lands, branch fresh from `main` and pull the relevant design from here.

---

## ⚠️ Status & Sequencing Note (2026-06-19)

**This plan is functionally COMPLETE for now — do not expand it next.** Phase 1 (the scheduler) is live in `NetworkRoot`; Phases 2–3 are documentation-only and done; Phase 4 (fees) is intentionally chained to the block-template-builder; Phase 5 is observation only. There is essentially nothing left to *build* here today.

**Impact from the historical migration** (`historical-founders-and-bootstrap-plan.md`): this plan assumed *"player + 4 miner bots + non-miners all exist from block 1."* The network now **grows over time** — Satoshi → Hal (bootstrap, 3 Jan–21 Mar 2009) → player (21 Mar) → miner bots. The circulation trigger was **re-aligned during Step 4 cleanup (2026-06-21):**

- The old absolute `TransactionCirculationStartBlock = 5` was dead after the bootstrap (the chain already starts at ~111). Replaced with a **per-bot warmup** (`CirculationWarmupBlocks = 5`): a miner bot only begins donating once **5 blocks have passed since its own first mined block** (`FirstBlockHeightMinedBy`). This works for bots introduced gradually later (Step 6) — each gets its own warmup.
- The scheduler already no-ops during the bootstrap (it runs only when `!_bulkMining`) and skips bots that haven't mined yet (`firstMinedHeight is null`), so only live, accumulating miner bots circulate.
- Recipients are non-miner bots only; a defensive **no-self-send** guard was added (and `AddTransactionToPendingTransactions` now rejects any `Sender == Recipient`).

See `AIHelperFiles/IMPLEMENTATION_ROADMAP.md` for where this sits in the unified order. **Fees (Phase 4) are implemented as part of the candidate block model** (`candidate-block-model-plan.md`, roadmap Step 4).

---

## Implementation Status

| Phase | Description | Status |
|---|---|---|
| 1 — Scheduler Core | Constants + `ScheduleBotTransactionsAfterBlock` + wire into `HandleMinedBlock` | ✓ DONE |
| 2 — Circulation Pattern | Expected block-by-block distribution (documentation only) | ✓ DONE |
| 3 — Persistence | Confirms no new code needed (documentation only) | ✓ DONE |
| 4 — Fee Model | `Transaction.Fee`, fee selection, coinbase augmentation | ✓ DONE in candidate model 4b.2 — bots attach 0.1–1.0 BTC fees; miner collects ΣFee via coinbase |
| 5 — Dev Visibility | Verify via BlockExplorer + BotsBtcWallets | ✓ DONE — fees + sends visible in BlockExplorer (Step 4c) |
| ↺ Re-alignment | Circulation trigger → per-bot warmup; no-self-send guard | ✓ DONE (Step 4 cleanup, 2026-06-21) |

---

## Current State

### What already exists

**`Scripts/BlockchainPort/Blockchain/Models.cs`**
- `Transaction` — has `TransactionId`, `Sender`, `Recipient`, `Amount`, `SignatureBase64`, `PublicKeyBase64`, `Secp256k1PublicKeyBase64`, `IsSpendable`
- `Block` — has `Transactions` list, `Index`, `Timestamp`, `MinedByNodeId`, `MinedByAddress`
- No `Fee` field yet (deferred to Phase 4)

**`Scripts/BlockchainPort/Blockchain/BlockchainService.cs`**
- `GetAddressSpendableBalance(address)` — confirmed balance minus pending outgoing; prevents double-spend
- `AddTransactionToPendingTransactions(tx)` — validates signature + balance before accepting

**`Scripts/BlockchainPort/Simulation/NodeAgent.cs`**
- `CreateSignedTransaction(amount, recipientAddress)` — calls `BlockchainService.CreateUnsignedTransaction`, signs with P-256 key

**`Scripts/BlockchainPort/Simulation/NetworkRoot.cs`** ← MODIFIED
- Phase 1 scheduler constants and `ScheduleBotTransactionsAfterBlock` now live here
- `HandleMinedBlock` now calls the scheduler before `PersistStateToDisk`

**`Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs`**
- `BotWalletRegistry.MinerBots` — `bot_1`..`bot_4`
- `BotWalletRegistry.NonMinerBots` — `non_miner_1`..`non_miner_10`

### How block rewards flow (timing)

| Event | Chain state after event |
|---|---|
| Block N mined | Miner's coinbase reward added to **pending** (not yet confirmed) |
| Block N+1 mined | Miner's reward from block N now **confirmed** |

The miner of block N can only spend their reward from block N+1 onward. `GetAddressSpendableBalance` handles this correctly.

---

## Roles and Participation Model

### Automatic Recirculation — Basic Mode

| Node type | Sends (auto) | Receives (auto) | Reason |
|---|---|---|---|
| `bot_1`..`bot_4` (miners) | **Yes** | **No** | Main source of circulated BTC; accumulates via coinbase |
| `non_miner_1`..`non_miner_10` | **No** | **Yes — only** | The entire recipient pool |
| `casino` | **No** | **No** | No BTC until P7; excluded from both roles |
| `player` | **No** | **No** | Manages wallet manually; not part of automatic recirculation |
| `pass_*` | **No** | **No** | Session-scoped; excluded in Basic Mode |

**Recipient pool** = `BotWalletRegistry.NonMinerBots.Select(b => b.Address).ToList()`

Derived from the registry each call. No hardcoded addresses or ID-prefix string filters.

### Infrastructure Readiness for Future Scenarios

| Scenario | Direction | When |
|---|---|---|
| Player manually programs automatic payment to any address | player → any | Needs UI design (post-Basic Mode) |
| Casino sends BTC once P7 BTC/SC trading is active | casino → any | P7 |
| Non-miner bots begin spending | non-miner → any | Post-Basic Mode |
| Referral reward transactions | any → non-miner | Post-Basic Mode (see Future section) |
| `pass_*` wallet automatic sends | pass_* → any | Post-Basic Mode |

---

## Phase 1 — Scheduler Core in NetworkRoot ✓ IMPLEMENTED

**File modified**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`

### Constants added (lines 26–32)

```csharp
private const int TransactionCirculationStartBlock = 5;
private const decimal MinBotSpendableBalanceBtc = 1.0m;
private const double BotSendProbabilityPerBlock = 0.5;
private const decimal MinSendFractionDecimal = 0.10m;
private const decimal MaxSendFractionDecimal = 0.40m;
```

- `TransactionCirculationStartBlock = 5` — lets early mining rewards accumulate before circulation begins
- `MinBotSpendableBalanceBtc = 1.0m` — with 50 BTC coinbase per block, allows sends after just one confirmation
- `BotSendProbabilityPerBlock = 0.5` — ~2 of 4 eligible miners send per block
- `MinSendFractionDecimal / MaxSendFractionDecimal` — bots send 10–40% of spendable balance per send

### ScheduleBotTransactionsAfterBlock (added after HandleMinedBlock)

```csharp
private static void ScheduleBotTransactionsAfterBlock(Block block)
{
    if (block.Index < TransactionCirculationStartBlock) return;

    List<string> recipientPool = BotWalletRegistry.NonMinerBots
        .Select(b => b.Address)
        .ToList();

    if (recipientPool.Count == 0) return;

    foreach (BotWalletRecord record in BotWalletRegistry.MinerBots)
    {
        if (!SharedNodesById.TryGetValue(record.NodeId, out NodeAgent? node)) continue;

        decimal spendable = node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
        if (spendable < MinBotSpendableBalanceBtc) continue;
        if (Random.Shared.NextDouble() >= BotSendProbabilityPerBlock) continue;

        decimal fraction = MinSendFractionDecimal
            + (decimal)Random.Shared.NextDouble() * (MaxSendFractionDecimal - MinSendFractionDecimal);
        decimal sendAmount = Math.Round(spendable * fraction, 8);
        if (sendAmount <= 0m) continue;

        string recipientAddress = recipientPool[Random.Shared.Next(recipientPool.Count)];
        Transaction tx = node.CreateSignedTransaction(sendAmount, recipientAddress);
        if (node.Blockchain.AddTransactionToPendingTransactions(tx))
            SharedNetwork.BroadcastTransaction(node.NodeId, tx);
    }
}
```

### Wire-in (HandleMinedBlock, before PersistStateToDisk)

```csharp
ScheduleBotTransactionsAfterBlock(block);
PersistStateToDisk();
```

---

## Phase 2 — Expected Circulation Pattern

With recipient pool = 10 non-miner bots, sender pool = 4 miner bots, 50% probability per miner per block:

| Block range | Expected state |
|---|---|
| 1–4 | Only coinbase rewards flow; miners accumulate |
| 5 | First scheduler run; ~2 of 4 miners send to random non-miner addresses |
| 6–7 | ~4 non-miner bots have received at least one send |
| 10 | Statistical expectation: all 10 non-miner bots have received BTC at least once |
| 15+ | BTC distributed across full non-miner pool; some non-miners have received multiple sends |

Non-miner bots do not send in Basic Mode, so their balances only grow. This makes them observable accumulation targets for the referral mechanic.

---

## Phase 3 — Persistence

No new persistence code needed. Scheduled transactions flow through the existing pipeline:

1. `AddTransactionToPendingTransactions(tx)` — adds to the bot's local pending list
2. `SharedNetwork.BroadcastTransaction(node.NodeId, tx)` — propagates to all nodes
3. Next block mine: `CreateNewBlock()` moves all pending txs into the block
4. `PersistStateToDisk()` — serializes chain + pending txs to `user://blockchain/state.json`

---

## Phase 4 — Fee Model (Deferred)

`Transaction` currently has no `Fee` field. When the block template builder (P4) is implemented:

1. Add `public decimal Fee { get; set; } = 0m;` to `Transaction` (default `0m` for backward compatibility)
2. Update `CreateSignedTransaction` on `NodeAgent` to accept `decimal fee = 0m` — include fee in signed data hash
3. Add `private static readonly decimal[] BotFeeOptions = { 0.1m, 0.2m, ..., 1.0m };` to `NetworkRoot`
4. Update scheduler: pick random fee, ensure `sendAmount - fee > 0`, pass fee to `CreateSignedTransaction`
5. Update `CreateNewBlock()` in `BlockchainService`: sum all tx fees and add to miner's coinbase

**Minimum send rule** (once fees are live): send amount must be ≥ fee amount. At 0.1 BTC minimum fee, minimum send is 0.1 BTC. With `MinSendFractionDecimal = 0.10m` and `MinBotSpendableBalanceBtc = 1.0m`, the minimum scheduled send is already 0.10 BTC — these align naturally.

---

## Phase 5 — Development Visibility

Verify circulation through existing screens after reaching block 6:

**BlockExplorer** (`Screens/BlockExplorer/BlockExplorer.tscn`)
- Pending transactions panel: bot-scheduled txs appear after block 5 (sender = `bot_*` address, recipient = `non_miner_*` address)
- Confirmed blocks from block 6 onward: non-coinbase transactions visible
- Address lookup on any non-miner bot address: shows incoming sends

**BotsBtcWallets** (`Screens/BotsBtcWallets/BotsBtcWallets.tscn`)
- `non_miner_1`..`non_miner_10` show increasing confirmed balances
- Refreshes every 3 seconds

---

## Design Decisions (OQ Resolutions)

All open questions from the original design have been resolved:

**Referral window (OQ-1)**: Winner determined by date, not block count. The window is **7 in-game days** measured from each non-miner bot's creation timestamp. For the initial 10 bots, the window starts from the genesis block timestamp. New non-miner bots that appear later each start their own 7-day window from the block in which they were registered. The player with the highest total donation when the window closes wins the referral.

**Donation ledger timing (OQ-2)**: Only at **block confirmation** — never at broadcast. A transaction in the pending pool has not yet landed in a block and could be dropped. Future note: may eventually require 2+ block confirmations for deeper chain safety.

**Referral perk name and design (OQ-3)**: Called **"Winning Referral Commission"** — explicitly NOT cashback (different system, different scope). Commission = **1% of the referred bot's SC winnings, scaling up to a maximum of 5%** as the referral climbs the Casino Rank System (top rank = 5%; see OQ-A in Resolved Decisions). **Always paid by the casino, never deducted from the referral.** The `Referrals` scene (from MainMenu) lists referrals with a claim button enabled when claimable commission > 0 (real-time claims, OQ-B). Bot betting simulation (the source of commission) uses MartingaleCalculator-derived logic — designed after the referral earning mechanic is confirmed working.

**Miner bot referral eligibility (OQ-4)**: Miner bots (`bot_1`..`bot_4`) are always competitors and mining allies — they do NOT become player referrals. However, each miner bot has the potential to **earn their own referrals** over time. The exception is the Miner Referral conversion mechanic described in OQ-6 (non-miner referrals can be promoted to miner nodes by the player — this is a fundamentally different role).

**Minimum donation amount (OQ-5)**: Send amount must be ≥ the fee. At the planned 0.1 BTC minimum fee, the minimum BTC donation to any non-miner bot address is 0.1 BTC. No spam sends below fee level.

**Non-miner bot graduation (OQ-6)**: See Future — Miner Referral System section below.

---

## Future — Non-Miner Bot Referral System

### Core Concept

Non-miner bot addresses (`non_miner_1`..`non_miner_10`) are visible in BlockExplorer. Any participant can send BTC to them:
- Miner bots send automatically via the scheduler
- The player can send manually from BTCWallet at any time

Each non-miner bot tracks a **donation ledger**: total BTC sent to it by each unique sender address, updated only at block confirmation. The participant (player or miner bot) with the highest cumulative donation when the bot's 7-day window closes **permanently** becomes that bot's **casino referral** (it leaves the auction forever — OQ-E), earning a **Winning Referral Commission** of 1% → up to 5% of the bot's SC winnings (scales with the referral's Casino Rank — OQ-A), **always paid by the casino**.

### Donation Ledger Model (future data structure)

```
NonMinerBotDonorRecord:
  botNodeId: string
  senderAddress: string
  totalDonatedBtc: decimal
  confirmedAtBlockIndex: int          // block index of most recent confirmed donation
  isTopDonorSince: int?               // null if not currently top donor
  referralAwardedAtBlockIndex: int?   // set when 7-day window closes and this donor wins
```

Persisted alongside or inside the bot wallet registry. Updated only when a transaction to a non-miner address is **confirmed in a block**.

### Auction Window Rule (implemented — gradual introduction, option b)

> ✅ Implemented & tested on this branch. **Each non-miner has its own staggered window** — *not* a single genesis-based window (that earlier wording was wrong, since the player only arrives on 21 Mar after the bootstrap). See `ProjectDesignManual.md` Chapter 22 for the full explanation.

- Non-miner bots are **introduced gradually** after live mining begins — **not** all at genesis. The anchor is the **first live block** (first block mined by a non-founder ≈ the player's first mined block on/after 21 Mar 2009).
- **Non-miner `i`** (its order in the registry) enters the auction at `firstLiveTimestamp + i × 2 in-game days`, so **every bot has its own window opening at a different time** (what you see in-engine).
- Each bot's auction then runs **7 in-game days** from its own introduction.
- When a bot's window closes, the donor with the highest cumulative donation **confirmed by the close timestamp** wins the referral.
- **The win is permanent (OQ-E).** Once won, the non-miner **leaves the auction forever** and stays the referral of the winner (player or miner bot). There is **no renewal / continuous auction** — the window determines the winner exactly once.
- **Fully derived from the chain** (no persisted auction state). `NetworkRoot.ComputeAuctionLedger` / `GetNonMinerAuctionLedger`; constants `NonMinerIntroIntervalMs` (~2 in-game days), `AuctionWindowMs` (7 in-game days). The recirculation scheduler donates only to **in-auction** non-miners; BlockExplorer **Enroll Mode** shows the race + resolved winners.

### Referrals Scene (planned)

A `Referrals` scene — accessible from **MainMenu** (OQ-D) — shows:
- List of player's referrals with bot IDs and addresses
- Per-referral claimable commission amount (1%→5% of bot's SC winnings since last claim, casino-paid)
- Claim button: enabled when claimable amount > 0 (real-time, OQ-B)
- Bot win/loss history (from simulated betting — designed in a later phase)
- Entry point to the **Miner Referrals** sub-scene (hardware donation / conversion / pool & strategy control)

### No New Scene for the Public Pool

Non-miner bot addresses need no dedicated UI scene. All addresses are already visible and searchable in BlockExplorer. Discovery of which bots are worth donating to happens through observation and experimentation.

---

## Future — Miner Referral System

Every **10 referrals** earned, the player gets the option to convert one of their non-miner referrals into a **Miner Referral Node**. This is a distinct class from regular miner bots.

### What Makes a Miner Referral Different

| Property | Regular miner bot | Miner Referral |
|---|---|---|
| Mining pool shares | Decides independently | **Player controls** |
| Autobet sessions | Independent | **Player controls**, assigns strategies |
| SC balance usage | Independent | Player controls for **hardware purchases only** (no sends) |
| BTC → SC conversion | Independent | **Player can trigger** at any time → goes to Miner Referral's MainBalance |
| BTC wallet sends | Independent | **Locked** — Miner Referral cannot send BTC to external wallets |
| SC → BTC conversion | Independent | Available (see trading note below) |

### Conversion Requirements

To convert a non-miner referral to a Miner Referral Node, the player must donate:
- Mining equipment including at minimum **2 hardware pieces** (the same hardware items used for player mining)
- Hardware donations come from the player's inventory

### Chain Sync Simulation

When a Miner Referral node is created, it must simulate downloading the full blockchain before it can participate in mining. Proposed timing model:
- Sync delay = `blockCount * 0.5 in-game seconds` per block in the current chain
- Example: 200-block chain → 100 in-game seconds (~2 real seconds at 48s/tick)
- Displayed as a progress bar; mining begins only after sync completes
- This simulates real network behavior and makes late-game conversions feel meaningful (longer chains = more time to onboard)

### SC and BTC Controls for Miner Referrals

- Player can buy hardware for any Miner Referral from the BTCPoolsAndHardwareShop scene (selector needed in that scene)
- Player can convert Miner Referral's BTC → SC via the BTC/SC trade scene (planned, selector must include referral wallets)
- Converted SC goes directly to the Miner Referral's MainBalance
- That SC can only be used to purchase hardware — no balance sends to other wallets
- Pool share assignments are controlled by the player in the hardware/pool scene

### BTC/SC Trading Through Referral Wallets

The planned BTC/SC trade scene must include a wallet selector that lists:
1. Player's own wallets (base address + passphrase wallets)
2. All active Miner Referral wallets

This allows the player to manage referral BTC conversions in the same flow as personal conversions. No new dedicated scene needed for referral trading — it's a selector addition to the existing trade scene.

**SC → BTC option for Miner Referrals**: The door is not closed. A possible use case: player converts SC to BTC in a Miner Referral's wallet to speculate on BTC price, then converts back at a profit. This is low-priority but the trade scene selector architecture should support it from the start.

### Miner Bot Referrals (separate concept)

Regular miner bots (`bot_1`..`bot_4` and future spawned miners) can also earn their own referrals independently, following the same 7-day auction mechanic. Their referrals are bot-managed, not player-controlled. This distinction is important: the player competes with miner bots for non-miner bot referrals in the same pool.

---

## Resolved Decisions — Referral System (2026-06-21)

**OQ-A — Secondary perks → none; scale the commission instead.** ✅ No extra perk types. The single perk is the **Winning Referral Commission**, which **scales from 1% up to a maximum of 5%** of the referred bot's SC winnings as that referral climbs the **Casino Rank System** (not yet designed — added to `PRIVATE_ROADMAP.md`), hitting 5% at its top rank. **The commission is ALWAYS paid by the casino — never subtracted from the referral's earnings.**

**OQ-B — Claim frequency → real-time.** ✅ Claimable at any moment; finer pacing may be defined later.

**OQ-C — Miner Referral chain-sync time → decreasing but length-sensitive.** ✅ The sync wait shrinks over the in-game years (simulating hardware/bandwidth progress), but a longer chain always costs more even as tech improves. Exact curve TBD alongside the historical-events schedule. *(This OQ also surfaced **hardware obsolescence** — a separate hardware topic, logged for `btc-pools-hardware-plan.md`: default Basic-Mode mining-set lifespan ≈ **12 in-game months** for the 2009–2012 window, shown live in the hardware/pools scene.)*

**OQ-D — Hardware-donation UI → dedicated Miner Referrals scene.** ✅ Converting a non-miner referral into a Miner Referral by donating hardware happens in a **new scene opened from the Referrals scene** (not in BTCWallet).

**OQ-E — Window renewal → none; referral is permanent.** ✅ When a non-miner referral is won (by the player OR a miner bot), it **leaves the auction forever** and remains the permanent referral of whoever won it. **This replaces the earlier "renewing 7-day window" wording** — the 7-day auction picks the winner once, then the bot is enrolled permanently.

**OQ-F — Referral cap → none.** ✅ No maximum number of non-miner referrals per miner node.

### Scenes

- **`Referrals`** — accessible from **MainMenu**; lists the player's referrals + claimable commission.
- **Miner Referrals** sub-scene — opened from `Referrals`; dedicated to miner-node referrals (hardware donation / conversion, pool & strategy control).

### BlockExplorer — "Enroll Mode" toggle

> ✅ **Foundation implemented (way 2, observe-only) — `scheduled-bot-transactions` branch, 2026-06-21.** `NetworkRoot.GetNonMinerDonationLedger()` computes, on demand from the canonical chain, each non-miner's total received + per-donor totals + leading donor (coinbase excluded; no persisted state yet). BlockExplorer has an **"Enroll Mode" CheckBox** (default off, built programmatically) that reveals a **donation-race panel**: per non-miner — total received, donor count, and leading donor (via `NetworkRoot.DescribeAddress`). **Still deferred (gated):** the *enrolled/permanent* filtering of tx lists + the central list, which needs auction resolution (window-timing decision) + the economy. Today nothing is enrolled, so the counter shows N/N recruitable.

A toggleable **"Enroll Mode" (on/off, default off)** that focuses the explorer on the still-running auction:

- **ON** shows only transactions involving **non-miner bots not yet enrolled** as anyone's referral (still recruitable); a tx to an already-enrolled non-miner is hidden — even a historical tx to a bot that has *since* been enrolled. A block mixing a tx to a now-enrolled bot with a tx to one still in the auction will show only the latter.
- **ON** also filters the **central non-miner list** to only still-recruitable addresses.
- This is partly natural today (enrolled non-miners stop transacting), but must be explicit because we may program sends to/between non-miners **before BTC has value (3 Oct 2009)**.
- Proposed extras: per recruitable bot, the **current leading donor + total**; a **"X of N still recruitable"** counter; **color** (green = player leading, red = a bot leading); optionally hide coinbase rows in ON mode.
