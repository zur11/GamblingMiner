# Scheduled Bot Transactions — Implementation Plan

Goal: make miner bots automatically send BTC to the non-miner holder bot pool after each mined block, creating organic BTC circulation that the player can observe in BlockExplorer. The recipient pool is exclusively non-miner bot addresses — this keeps early circulation contained, makes non-miner bot addresses publicly interesting, and sets the foundation for the bot referral system designed in the future section below.

This is the next item after the BTC wallet system (Phase 1–9 of `btc-wallet-system-plan.md`).

---

## Current State

### What already exists

**`Scripts/BlockchainPort/Blockchain/Models.cs`**
- `Transaction` — has `TransactionId`, `Sender`, `Recipient`, `Amount`, `SignatureBase64`, `PublicKeyBase64`, `Secp256k1PublicKeyBase64`, `IsSpendable`
- `Block` — has `Transactions` list, `Index`, `Timestamp`, `MinedByNodeId`, `MinedByAddress`
- No `Fee` field yet on `Transaction` (see Phase 4 — implementable now or deferred)

**`Scripts/BlockchainPort/Blockchain/BlockchainService.cs`**
- `GetAddressSpendableBalance(address)` — confirmed balance minus pending outgoing; prevents double-spend
- `AddTransactionToPendingTransactions(tx)` — validates signature + balance before accepting; rejects if spendable balance is insufficient
- `PendingTransactions` — the in-memory list of transactions waiting to be included in the next block

**`Scripts/BlockchainPort/Simulation/NodeAgent.cs`**
- `CreateSignedTransaction(amount, recipientAddress)` — calls `BlockchainService.CreateUnsignedTransaction`, signs with P-256 key, attaches public keys
- Each `NodeAgent` holds its own `BlockchainService` (chain copy) and ECDSA keypair
- `WalletAddress` is the node's receiving `gm1q...` address

**`Scripts/BlockchainPort/Simulation/NetworkRoot.cs`**
- `SharedNodesById: Dictionary<string, NodeAgent>` — all registered nodes
- `SharedNetwork: NetworkSimulator` — propagates transactions and blocks to all registered nodes
- `HandleMinedBlock(miner, block, minedAtUnixMs)` — fires every time any block is mined; **this is the correct hook for the scheduler**
- `CreateAndBroadcastTransactionToAddress(fromNodeId, recipientAddress, amount)` — full pipeline: sign → validate → add to pending → broadcast
- `PersistStateToDisk()` — serializes chain + pending txs + node wallets

**`Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs`**
- Miner bots: `bot_1`..`bot_4` — `BotWalletRegistry.MinerBots` IReadOnlyList
- Non-miner holder bots: `non_miner_1`..`non_miner_10` — `BotWalletRegistry.NonMinerBots` IReadOnlyList
- Both lists expose `NodeId`, `Address`, `IsMinerNode` per record
- `AllBots` property combines both lists

### What is missing

- No automatic transaction scheduling — all bot-to-bot sends are currently manual (BotsBtcWallets screen)
- No circulation start rule
- No per-block hook that evaluates bot balances and queues outgoing transactions
- `Transaction` has no `Fee` field (optionally added in Phase 4)

### How block rewards flow (timing)

| Event | Chain state after event |
|---|---|
| Block N mined | Miner's coinbase reward is added to **pending** (not yet confirmed) |
| Block N+1 mined | Miner's reward from block N is now **confirmed** in block N+1 |

So the miner of block N can only spend their reward starting from block N+1. `GetAddressSpendableBalance` already handles this correctly — pending incoming transactions do not count as spendable.

---

## Roles and Participation Model

### Automatic Recirculation — Basic Mode

| Node type | Sends (auto) | Receives (auto) | Reason |
|---|---|---|---|
| `bot_1`..`bot_4` (miners) | **Yes** | **No** | Main source of circulated BTC; already accumulates via coinbase |
| `non_miner_1`..`non_miner_10` | **No** | **Yes — only** | The entire recipient pool; receive from miners |
| `casino` | **No** | **No** | No BTC until P7 BTC/SC trading; excluded from both roles in Basic Mode |
| `player` | **No** | **No** | Player manages their own wallet manually; not part of automatic recirculation |
| `pass_*` | **No** | **No** | Session-scoped passphrase wallets excluded in Basic Mode |

**Recipient pool** = `BotWalletRegistry.NonMinerBots.Select(b => b.Address).ToList()`

Derived from the registry each call. No hardcoded addresses or ID-prefix string filters needed.

### Infrastructure Readiness for Future Scenarios

The scheduler and transaction pipeline must be designed so the following future patterns require only lifted exclusions or new call sites — no architectural changes:

| Scenario | Direction | When |
|---|---|---|
| Player manually programs automatic payment to any address | player → any | Needs UI design (post-Basic Mode) |
| Casino sends BTC once P7 BTC/SC trading is active | casino → any | P7 |
| Non-miner bots begin spending (post-casino player simulation) | non-miner → any | Post-Basic Mode |
| `pass_*` wallet automatic sends | pass_* → any | Post-Basic Mode |
| Referral reward transactions | any → non-miner | Post-Basic Mode (see Future section below) |

---

## Phase 1 — Scheduler Core in NetworkRoot

**File to modify**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`

### 1.1 — Constants

Add alongside existing `PlayerNodeId` / `CasinoNodeId` constants:

```csharp
private const int TransactionCirculationStartBlock = 5;
private const decimal MinBotSpendableBalanceBtc = 1.0m;
private const double BotSendProbabilityPerBlock = 0.5;
private const decimal MinSendFractionDecimal = 0.10m;
private const decimal MaxSendFractionDecimal = 0.40m;
```

- `TransactionCirculationStartBlock = 5` — lets early mining rewards accumulate before circulation begins
- `MinBotSpendableBalanceBtc = 1.0m` — with 50 BTC coinbase per block, allows sends after just one confirmation; revisit if too frequent
- `BotSendProbabilityPerBlock = 0.5` — ~2 of 4 eligible miners send per block; produces 2–4 non-coinbase txs per block during active circulation
- `MinSendFractionDecimal / MaxSendFractionDecimal` — bots send 10–40% of spendable balance per send; prevents rapid depletion

### 1.2 — Scheduler Method

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

Notes:
- Iterates `BotWalletRegistry.MinerBots` directly — no ID-prefix checks, no leaking through the full `SharedNodesById` dictionary
- Recipient pool built from `BotWalletRegistry.NonMinerBots` each call — picks up any future registry additions automatically
- Uses `Random.Shared` (.NET 8 thread-safe shared instance) — never `new Random()` which risks identical seeds under rapid calls
- `AddTransactionToPendingTransactions` re-validates signature and balance internally; intentional double-validation (defense in depth)
- If Phase 4 fee model is implemented, `CreateSignedTransaction` receives an additional fee argument (see Phase 4)

### 1.3 — Wire Into HandleMinedBlock

```csharp
private static void HandleMinedBlock(NodeAgent miner, Block block, long? minedAtUnixMs)
{
    // ... existing broadcast and streak logic (unchanged) ...

    ScheduleBotTransactionsAfterBlock(block);  // ← add before PersistStateToDisk
    PersistStateToDisk();
}
```

The scheduler fires after the block is broadcast to all nodes. `GetAddressSpendableBalance` sees the finalized chain state for block N, so scheduled transactions correctly land in block N+1.

---

## Phase 2 — Expected Circulation Pattern

With recipient pool = 10 non-miner bots, sender pool = 4 miner bots, 50% probability per miner per block:

| Block range | Expected state |
|---|---|
| 1–4 | Only coinbase rewards flow; miners accumulate |
| 5 | First scheduler run; ~2 of 4 miners send to random non-miner addresses |
| 6–7 | ~4 non-miner bots have received at least one send (uniform random over 10 addresses) |
| 10 | Statistical expectation: all 10 non-miner bots have received BTC at least once |
| 15+ | BTC distributed across full non-miner pool; some non-miners have received multiple sends |

Non-miner bots do not send in Basic Mode, so their balances only grow. This makes them observable accumulation targets and the basis for the referral mechanic described below.

---

## Phase 3 — Persistence

No new persistence code is needed. The scheduled transactions flow through the existing pipeline:

1. `node.Blockchain.AddTransactionToPendingTransactions(tx)` — adds to the bot's local pending list
2. `SharedNetwork.BroadcastTransaction(node.NodeId, tx)` — propagates to all nodes
3. Next block mine: `CreateNewBlock()` in `BlockchainService` moves all pending txs into the block
4. `PersistStateToDisk()` — serializes chain + pending txs to `user://blockchain/state.json`

Scheduled transactions are visible in `BlockExplorer` immediately (in pending) and confirmed in the next mined block. They survive save/reload as part of the standard chain state snapshot.

---

## Phase 4 — Fee Model

**Decision**: implement now (preferred) or defer to block template builder phase (safe fallback).

The simple fee model: bots pick a fee from a fixed list `[0.1, 0.2, ..., 1.0 BTC]` randomly. The fee is subtracted from `sendAmount` before the transaction is created; the block assembler adds all included fees to the miner's coinbase reward.

### 4.1 — Changes Required

**`Scripts/BlockchainPort/Blockchain/Models.cs`** — add to `Transaction`:
```csharp
public decimal Fee { get; set; } = 0m;
```
Default `0m` ensures backward compatibility with existing persisted blocks.

**`Scripts/BlockchainPort/Simulation/NodeAgent.cs`** — update signature:
```csharp
public Transaction CreateSignedTransaction(decimal amount, string recipientAddress, decimal fee = 0m)
```
Set `tx.Fee = fee` inside the method before signing. Include the fee value in the signed data hash (alongside Amount and Recipient) to prevent tampering.

**`Scripts/BlockchainPort/Simulation/NetworkRoot.cs`** — add fee list constant and update scheduler:
```csharp
private static readonly decimal[] BotFeeOptions =
    { 0.1m, 0.2m, 0.3m, 0.4m, 0.5m, 0.6m, 0.7m, 0.8m, 0.9m, 1.0m };
```

Inside `ScheduleBotTransactionsAfterBlock`, before creating the transaction:
```csharp
decimal fee = BotFeeOptions[Random.Shared.Next(BotFeeOptions.Length)];
decimal netSend = Math.Round(spendable * fraction, 8);
if (netSend - fee <= 0m) continue;  // fee would consume the entire send
Transaction tx = node.CreateSignedTransaction(netSend - fee, recipientAddress, fee);
```

**`Scripts/BlockchainPort/Blockchain/BlockchainService.cs`** — update `CreateNewBlock()` coinbase:
```csharp
decimal totalFees = pendingTxsInBlock.Sum(tx => tx.Fee);
// add totalFees to the coinbase Amount alongside the block reward
```

### 4.2 — If Deferring

Leave `Transaction.Fee = 0m` as a planned field (add default property now for forward compatibility), mark the `CreateSignedTransaction`, scheduler, and `CreateNewBlock` change sites as `// TODO: Phase 4 fee model` comments, and implement during the block template builder phase. Until then, all scheduled transactions carry zero fee and the full amount goes to the recipient.

---

## Phase 5 — Development Visibility

After implementation, verify circulation through existing screens:

**BlockExplorer** (`Screens/BlockExplorer/BlockExplorer.tscn`)
- Pending transactions panel: bot-scheduled txs appear after block 5
- Confirmed blocks (from block 6 onward): non-coinbase transactions visible
- Address lookup on any non-miner bot address: shows incoming sends from miner bot addresses

**BotsBtcWallets** (`Screens/BotsBtcWallets/BotsBtcWallets.tscn`)
- Non-miner bots (`non_miner_1`..`non_miner_10`) show increasing confirmed balances
- Balance label refreshes every 3 seconds (screen's existing refresh interval)

No new dev scenes needed.

---

## Implementation Order

```
1. Add constants (TransactionCirculationStartBlock, MinBotSpendableBalanceBtc, etc.) to NetworkRoot
2. (Optional) Phase 4: add Fee field to Transaction, update CreateSignedTransaction, add BotFeeOptions, update CreateNewBlock
3. Add ScheduleBotTransactionsAfterBlock() to NetworkRoot
4. Wire call into HandleMinedBlock() before PersistStateToDisk()
5. Run game: autobet until block 5 — verify no scheduled txs appear in BlockExplorer pending panel
6. Continue autobet to block 6+ — verify pending transactions appear (sender = miner bot address, recipient = non_miner_* address)
7. Verify miner bot balances decrease in BotsBtcWallets
8. Verify non-miner bot balances accumulate in BotsBtcWallets
9. Verify BlockExplorer confirmed blocks from block 6+ contain non-coinbase transactions
10. If Phase 4 implemented: verify fee amounts appear in tx detail and miner coinbase reflects the fee bonus
```

---

## Future — Non-Miner Bot Referral System

This section captures design intent for a post-Basic-Mode feature. No implementation is planned here — it informs what data the Phase 1 scheduler should eventually track and how the non-miner bot pool grows in meaning over time.

### Core Concept

Non-miner bot addresses (`non_miner_1`..`non_miner_10`) are visible in BlockExplorer. Any participant can send BTC to them:
- Miner bots send automatically via the scheduler (see Phase 1)
- The player can send manually from BTCWallet at any time

Each non-miner bot tracks a **donation ledger**: total BTC sent to it by each unique sender address. If the player's address is the top donor to a given non-miner bot for a defined number of consecutive blocks (threshold TBD), that bot becomes the player's **casino referral** — providing small but persistent advantages in the casino.

This creates a natural incentive for players to donate BTC to non-miner bots, competing with the miner bot automatic scheduler for "top donor" status on each bot.

### Donation Ledger Model (future data structure)

```
NonMinerBotDonorRecord:
  botNodeId: string
  senderAddress: string
  totalDonatedBtc: decimal
  lastDonationBlock: int
  isTopDonorSince: int?   // block index when this sender became top donor; null if not top donor
```

Persisted alongside or inside the bot wallet registry. Updated whenever a transaction to a non-miner address is confirmed in a block (not at broadcast — only at confirmation to avoid counting dropped transactions).

### Referral Auction Mechanic (to be designed)

- Player inspects non-miner bot addresses in BlockExplorer to see donation rankings
- If player becomes and remains top donor for X consecutive blocks → bot becomes their casino referral
- Referral tier depends on total donated amount (bots with higher received totals = better referrals with more valuable perks)
- Future: non-miner bots simulate casino players (background betting, no gameplay required), giving them income streams that determine their referral quality
- Future: referral system supports human referrals, tiered rewards, and revenue sharing — non-miner bots are the entry point

### Referral Perk Design Space (all TBD)

- SC cashback percentage on wins
- Reduced BTC/SC conversion fee (P7)
- Tournament entry tickets
- Extended bankroll auto-recharge grace periods
- Priority informational notifications (next block ETA, etc.)

### No New Scene Required

The "public pool" of non-miner bot addresses needs no new UI scene. All addresses are already visible and searchable in BlockExplorer. The player discovers which bots are interesting by browsing the existing address list — the referral potential emerges from observation and experimentation, not from a dedicated screen.

---

## Open Questions

**OQ-1 — Referral top-donor threshold**: How many consecutive blocks must the player be top donor to earn a referral? Should the threshold scale with the bot's referral quality (better bots require longer commitment)?

**OQ-2 — Donation ledger persistence timing**: Confirmed at broadcast (pending) or only at block confirmation? Confirmation is safer — avoids counting transactions that never make it into a block.

**OQ-3 — Referral perk priority**: Which perk type should be implemented first to have the most impact on Basic Mode survival decisions?

**OQ-4 — Miner bot referral eligibility**: Can miner bots also become referrals, or only non-miner bots? Mixing roles may dilute the mechanic but miner bots accumulate more BTC and could be more valuable referrals.

**OQ-5 — Minimum donation threshold**: Should any BTC transfer of any size count toward donor ranking, or should there be a minimum per-tx floor to prevent ranking manipulation via many tiny sends?

**OQ-6 — Non-miner bot graduation timing**: When do non-miner bots graduate to simulated casino players? Tied to P8 Achievements, a block milestone, or a separate post-Basic-Mode phase?
