# Scheduled Bot Transactions — Implementation Plan

Goal: make bots automatically send BTC to each other, to the player, and to the casino after each mined block, creating organic BTC circulation that the player can observe in BlockExplorer and their own BTCWallet.

This is the next item after the BTC wallet system (Phase 1–9 of `btc-wallet-system-plan.md`).

---

## Current State

### What already exists

**`Scripts/BlockchainPort/Blockchain/Models.cs`**
- `Transaction` — has `TransactionId`, `Sender`, `Recipient`, `Amount`, `SignatureBase64`, `PublicKeyBase64`, `Secp256k1PublicKeyBase64`, `IsSpendable`
- `Block` — has `Transactions` list, `Index`, `Timestamp`, `MinedByNodeId`, `MinedByAddress`
- No `Fee` field yet on `Transaction` (deferred to Phase 4 block template builder)

**`Scripts/BlockchainPort/Blockchain/BlockchainService.cs`**
- `GetAddressSpendableBalance(address)` — confirmed balance minus pending outgoing; prevents double-spend
- `AddTransactionToPendingTransactions(tx)` — validates signature + balance before accepting; rejects if spendable balance is insufficient
- `PendingTransactions` — the in-memory list of transactions waiting to be included in the next block

**`Scripts/BlockchainPort/Simulation/NodeAgent.cs`**
- `CreateSignedTransaction(amount, recipientAddress)` — calls `BlockchainService.CreateUnsignedTransaction`, signs with P-256 key, attaches public keys
- Each `NodeAgent` holds its own `BlockchainService` (chain copy) and ECDSA keypair
- `WalletAddress` is the node's receiving `gm1q...` address

**`Scripts/BlockchainPort/Simulation/NetworkRoot.cs`**
- `SharedNodesById: Dictionary<string, NodeAgent>` — all registered nodes (player, bot_1..bot_4, non-miner bots, casino, session pass_* wallets)
- `SharedNetwork: NetworkSimulator` — propagates transactions and blocks to all registered nodes
- `HandleMinedBlock(miner, block, minedAtUnixMs)` — fires every time any block is mined; **this is the correct hook for the scheduler**
- `CreateAndBroadcastTransactionToAddress(fromNodeId, recipientAddress, amount)` — full pipeline: sign → validate → add to pending → broadcast
- `PersistStateToDisk()` — serializes chain + pending txs + node wallets; already handles persistence correctly

**`Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs`**
- 4 miner bots (`bot_1`..`bot_4`) and 10 non-miner holder bots — all registered with full ECDSA keypairs
- All are registered as `NodeAgent`s in `EnsureInitialized()` so they can sign transactions

### What is missing

- No automatic transaction scheduling — all bot-to-bot sends are currently manual (BotsBtcWallets screen)
- No circulation start rule
- No per-block hook that evaluates bot balances and queues outgoing transactions
- `Transaction` has no `Fee` field (deferred; block template builder will handle fee logic)

### How block rewards flow (timing)

Understanding this is critical for the scheduler's balance checks:

| Event | Chain state after event |
|---|---|
| Block N mined | Miner's coinbase reward is added to **pending** (not yet confirmed) |
| Block N+1 mined | Miner's reward from block N is now **confirmed** in block N+1 |

So the miner of block N can only spend their reward starting from block N+1. `GetAddressSpendableBalance` already handles this correctly — pending incoming transactions do not count as spendable.

---

## Phase 1 — Scheduler Core in NetworkRoot

**File to modify**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`

### 1.1 — Constants

Add to the top of `NetworkRoot`, alongside the existing `PlayerNodeId` / `CasinoNodeId` constants:

```csharp
private const int TransactionCirculationStartBlock = 5;
private const decimal MinBotSpendableBalanceBtc = 1.0m;
private const double BotSendProbabilityPerBlock = 0.5;
private const decimal MinSendFractionDecimal = 0.10m;
private const decimal MaxSendFractionDecimal = 0.40m;
```

- `TransactionCirculationStartBlock = 5` — no scheduled sends before block 5; lets early mining rewards accumulate first
- `MinBotSpendableBalanceBtc = 1.0m` — bot must have at least 1 BTC confirmed and unencumbered before scheduling
- `BotSendProbabilityPerBlock = 0.5` — each eligible bot has a 50% chance per block of scheduling a send; statistically produces ~1–3 scheduled transactions per block once circulation is active
- `MinSendFractionDecimal / MaxSendFractionDecimal` — amount range: 10% to 40% of spendable balance per scheduled send; prevents bots from depleting their balance instantly

### 1.2 — Scheduler Method

Add as a new private static method in `NetworkRoot`:

```csharp
private static void ScheduleBotTransactionsAfterBlock(Block block)
{
    if (block.Index < TransactionCirculationStartBlock) return;

    List<string> allAddresses = SharedNodesById.Values
        .Select(n => n.WalletAddress)
        .Distinct()
        .ToList();

    foreach ((string nodeId, NodeAgent node) in SharedNodesById)
    {
        // Player manages their own wallet; passphrase wallets are session-scoped
        if (nodeId == PlayerNodeId) continue;
        if (nodeId.StartsWith("pass_", StringComparison.Ordinal)) continue;

        decimal spendable = node.Blockchain.GetAddressSpendableBalance(node.WalletAddress);
        if (spendable < MinBotSpendableBalanceBtc) continue;
        if (Random.Shared.NextDouble() >= BotSendProbabilityPerBlock) continue;

        decimal fraction = MinSendFractionDecimal
            + (decimal)Random.Shared.NextDouble() * (MaxSendFractionDecimal - MinSendFractionDecimal);
        decimal sendAmount = Math.Round(spendable * fraction, 8);
        if (sendAmount <= 0m) continue;

        List<string> eligible = allAddresses
            .Where(a => a != node.WalletAddress)
            .ToList();
        if (eligible.Count == 0) continue;

        string recipientAddress = eligible[Random.Shared.Next(eligible.Count)];
        Transaction tx = node.CreateSignedTransaction(sendAmount, recipientAddress);
        if (node.Blockchain.AddTransactionToPendingTransactions(tx))
            SharedNetwork.BroadcastTransaction(node.NodeId, tx);
    }
}
```

Notes:
- Uses `Random.Shared` (.NET 8 built-in thread-safe shared instance) — never `new Random()` which risks identical seeds under rapid calls
- The balance check is on `node.Blockchain` (the bot's local chain copy), which is kept in sync via `SharedNetwork.BroadcastBlock` — no separate chain query needed
- `AddTransactionToPendingTransactions` re-validates signature and balance internally; calling it here is intentional double-validation (defense in depth)
- Only the tx data is broadcast, not a new block; the scheduled transactions go into the pending pool for the next block

### 1.3 — Wire Into HandleMinedBlock

Modify `HandleMinedBlock` to call the scheduler before persisting:

```csharp
private static void HandleMinedBlock(NodeAgent miner, Block block, long? minedAtUnixMs)
{
    if (minedAtUnixMs.HasValue)
        block.Timestamp = minedAtUnixMs.Value;

    SharedNetwork.BroadcastBlock(miner.NodeId, block);
    Transaction? rewardTx = miner.Blockchain.PendingTransactions
        .LastOrDefault(t => t.Sender == BlockchainService.CoinbaseSender && t.Recipient == miner.WalletAddress);
    if (rewardTx is not null)
        SharedNetwork.BroadcastTransaction(miner.NodeId, rewardTx);

    _lastMinedBlock = block;
    // ... streak tracking (unchanged) ...

    ScheduleBotTransactionsAfterBlock(block);  // ← add here, before PersistStateToDisk
    PersistStateToDisk();
}
```

The scheduler fires after the block is broadcast to all nodes, so `GetAddressSpendableBalance` sees the finalized chain state for block N when scheduling transactions that will land in block N+1.

---

## Phase 2 — Participant Roles

### Who sends

| Node type | Eligible to send | Condition |
|---|---|---|
| `bot_1`..`bot_4` (miners) | Yes | spendable ≥ 1 BTC and block.Index ≥ 5 |
| Non-miner holder bots | Yes | same; starts with 0 BTC so ineligible until they receive from miners |
| `casino` | Yes | ineligible until it receives BTC (currently has 0 from mining); will become active once BTC/SC trading (P7) seeds it |
| `player` | **No** | always excluded — player owns their wallet |
| `pass_*` | **No** | session-scoped passphrase wallets excluded |

### Who receives

Any registered address can receive: `bot_1`..`bot_4`, non-miner bots, `casino`, `player`. All addresses are pulled from `SharedNodesById.Values` each call so new session registrations (passphrase wallets) also get into the pool.

The recipient selection is uniform random among all addresses except the sender. This gives the player and casino equal probability of receiving BTC as any other bot. If the game design later requires the player to receive BTC more reliably, add a weighted selection (e.g., player address appears 3× in the pool).

### Expected circulation pattern

- Blocks 1–4: only coinbase rewards flow (miners accumulate BTC)
- Block 5+: scheduler activates; miner bots start distributing
- By block 10: non-miner bots begin receiving; casino may receive its first BTC
- By block 20: BTC is spread across the full address pool; player's BTCWallet shows incoming transactions

---

## Phase 3 — Persistence

No new persistence code is needed. The scheduled transactions flow through the existing pipeline:

1. `node.Blockchain.AddTransactionToPendingTransactions(tx)` — adds to the bot's local pending list
2. `SharedNetwork.BroadcastTransaction(node.NodeId, tx)` — propagates to all nodes including the player's chain
3. Next block mine: `CreateNewBlock()` in `BlockchainService` moves all pending txs into the block
4. `PersistStateToDisk()` — serializes `PlayerPendingTransactions` and chain to `user://blockchain/state.json`

Scheduled transactions will be visible in `BlockExplorer` immediately (in pending), and confirmed in the next mined block. They survive save/reload because they are part of the standard chain state snapshot.

---

## Phase 4 — Fee Model (Deferred)

`Transaction` currently has no `Fee` field. When Phase 4 (block template builder) is implemented:

1. Add `Fee` property to `Transaction` (default `0m` for backward compatibility)
2. Update `CreateSignedTransaction` on `NodeAgent` to accept an optional fee parameter
3. Update `ScheduleBotTransactionsAfterBlock` to subtract fee from `sendAmount` and set `tx.Fee`
4. Update block assembly to add included fees to the miner's coinbase

Until then, scheduled transactions carry `Fee = 0` implicitly and the full `Amount` goes to the recipient.

---

## Phase 5 — Development Visibility

After implementation, verify circulation is working through existing UI:

**BlockExplorer** (`Screens/BlockExplorer/BlockExplorer.tscn`)
- Pending transactions panel shows bot-scheduled txs appear after block 5
- Confirmed blocks (from block 6 onward) contain non-coinbase transactions
- Address lookup: player's address shows incoming transactions from bot addresses

**BotsBtcWallets** (`Screens/BotsBtcWallets/BotsBtcWallets.tscn`)
- Non-miner bots begin receiving BTC from miner bots once circulation starts
- Their confirmed balance label updates every 3 seconds (the screen's refresh interval)

**BTCWallet** (`Screens/BTCWallet/BTCWallet.tscn`)
- Player's base address balance increases when a bot sends to the player address
- Balance refreshes every 2 seconds

No new dev scenes are needed for this phase.

---

## Implementation Order

```
1. Add constants (TransactionCirculationStartBlock, MinBotSpendableBalanceBtc, etc.) to NetworkRoot
2. Add ScheduleBotTransactionsAfterBlock() static method to NetworkRoot
3. Wire call into HandleMinedBlock() before PersistStateToDisk()
4. Run game: autobet until block 5 — verify no scheduled txs appear in BlockExplorer
5. Continue autobet to block 6+ — verify pending transactions appear after each block
6. Verify at least one miner bot balance decreases and a target address increases
7. Verify player BTCWallet occasionally receives BTC from bot addresses
8. Verify BlockExplorer confirmed blocks from block 6+ contain non-coinbase transactions
```

---

## Open Questions

**OQ-1 — Player as recipient probability**: Should the player's address be more likely as a recipient to ensure early BTC income? Or keep it uniform for simulation realism?

**OQ-2 — Casino eligibility**: Should the casino be excluded from scheduled sends until P7 (BTC/SC trading) seeds it with BTC, or allow it to receive and hold BTC from bots naturally?

**OQ-3 — `MinBotSpendableBalanceBtc` threshold**: Is 1.0 BTC the right minimum? With 50 BTC coinbase rewards per block, this allows sends after the first confirmation. Lower = more frequent tiny sends; higher = larger but less frequent sends.

**OQ-4 — `BotSendProbabilityPerBlock`**: At 0.5, all 4 miner bots are eligible by block 5 and statistically 2 send per block. This means blocks 6–20 will have 2–4 non-coinbase transactions each. Is that realistic enough for the game's early Bitcoin feel?

**OQ-5 — Fee field timing**: Should `Fee` be added to `Transaction` now (with default `0m`) to front-load the migration, or wait until Phase 4?

**OQ-6 — Non-miner bot seeding**: Non-miner bots start with 0 BTC and only receive from miner bots' random sends. Should we seed them with small amounts at circulation start (e.g., one small coinbase-like grant at block 5) or let the natural random sends fill them over time?
