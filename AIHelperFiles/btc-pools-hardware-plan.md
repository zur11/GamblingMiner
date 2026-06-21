# BTC Mining Pools & Hardware Shop — Implementation Plan

**Status**: Phase 1 ○  Phase 2 ○  Phase 3 ○  Phase 4 ○  Phase 5 ○  Phase 6 ○ — **GATED: do not start until roadmap Step 6** (see `IMPLEMENTATION_ROADMAP.md`). This plan builds on the **per-node candidate block model** (`candidate-block-model-plan.md`, roadmap Step 4) — per-credit nonce routing mines real candidates. It also assumes "player + 4 bots at block 1", whereas the network now introduces miner bots *gradually after the 21 Mar player start*; the initial allocation and routing are re-based on that model in Step 6.

**Architecture summary**:
- **Option 2 (solo / P2P)**: each hardware credit in a node's *individual pool* generates 1 nonce attempt per bet, routed to that node's own blockchain — current behavior, extended with hardware count control.
- **Option 1 (community pool)**: hardware credits assigned to the *casino pool* route each bet's nonce attempt to the casino node's blockchain; the casino distributes block rewards to contributors proportionally minus a dynamic fee.
- **Option 3 (hybrid coordinator)**: designed in a future plan — reserved for post-Basic Mode.

**Starting state**: player + 4 bots each receive **2 hardware credits** at bootstrap (1 individual, 1 casino pool).  
Betting speed in DiceGame is **locked to total hardware credits** (not freely selectable). 2 credits → 2 bets/second.

---

## Current State

### What already exists

**`Scripts/BlockchainPort/Simulation/NetworkRoot.cs`**
- `TryMineSingleNonceAttempt(nodeId, out Block? minedBlock, long? minedAtUnixMs)` — the nonce-attempt call; accepts any registered node ID.
- `HandleMinedBlock(miner, block, minedAtUnixMs)` — fires when any node mines a block; triggers reward coinbase, broadcasts.
- `GetBlockRewardForNextCandidate(miner)` — current halving-aware reward calculation.
- `CasinoNodeId = "casino"` — casino already exists as a registered `NodeAgent` with full keys.
- `SharedNodesById` — dictionary of all registered nodes (player, bot_1..4, non-miners, casino).

**`Scripts/BlockchainPort/Simulation/NodeAgent.cs`**
- `TryMineSingleNonceAttempt(rewardAmount)` — single-nonce method; independent of DiceGame bet count.
- `CreateCoinbaseReward(amount)` — creates the coinbase TX for reward distribution.

**`Screens/DiceGame/DiceGame.cs`**
- `NodeStrategyState.BetsPerSecond` — per-node integer that currently drives the `ApsSelector` (1–99, free selection).
- `BotAutoBetRunner` — per-bot accumulator that fires `ExecuteBotBet` at the configured rate.
- `GetRunnerEffectiveBetsPerSecond(runner)` — currently returns `runner.Strategy.BetsPerSecond`.
- `GetAutoBetBaseAps()` — reads `_apsSelector.Selected + 1`.
- All node-nonce calls happen in: `_blockchainNetworkRoot.TryMineSingleNonceAttempt(nodeId, ...)` inside the player's `_Process` loop and inside `ExecuteBotBet`.

**`Screens/MainMenu/`**, **`Scripts/Services/SceneManager.cs`** — navigation infrastructure ready for new scene entry.

### What does not yet exist

- Hardware credit concept — no model, no persistence, no service.
- Casino community pool — no pool membership, no reward queue, no payout pipeline.
- BTCPoolsAndHardwareShop scene.
- Hardware-driven speed lock in DiceGame (ApsSelector still free).

---

## New Concepts

### Hardware Credit

A **hardware credit** is an abstract unit representing one dedicated mining pipeline.

| Property | Value |
|---|---|
| 1 credit → | 1 nonce attempt per bet executed by its owning node |
| Pool assignment | Either **individual** (node's own chain) or **casino** (casino community pool) |
| Stacking | Owning node bets `total credits` times per second (all pools combined) |
| Default on purchase | Assigned to individual pool until manually moved |

Each node's total betting speed in DiceGame = `IndividualPoolCredits + CasinoPoolCredits`.

### Individual Mining Pool (Option 2 — unchanged behavior)

Each credit assigned here makes the node's `TryMineSingleNonceAttempt` fire once per bet — same as the current system. If the node mines a block it keeps 100% of the reward. No change to existing nonce or reward logic needed.

### Casino Community Pool (Option 1 — new)

| Concept | Detail |
|---|---|
| Who runs it | The casino `NodeAgent` (already registered) |
| Mining power source | Sum of all casino-pool credits from all contributors |
| Nonce call | `TryMineSingleNonceAttempt("casino", ...)` — one call per contributed credit, per bet cycle |
| Block reward | Casino node's coinbase reward goes to a **pending reward queue** instead of the casino wallet directly |
| Fee calculation | Dynamic; based on casino pool power vs. total individual pool power (see below) |
| Payout | Distributed proportionally to contributors after fee deduction; each payout is a real `CreateAndBroadcastTransactionToAddress` call from casino wallet |
| Transaction fee | 0.1 BTC deducted from each payout (lowest fee available to casino) |

#### Casino Fee Formula

```
casinoTotalCredits   = sum of CasinoPoolCredits across all participating nodes
individualTotalCredits = sum of IndividualPoolCredits across all non-casino nodes

ratio = casinoTotalCredits / max(1, individualTotalCredits)

if ratio == 1.0  →  fee = 30%   (balanced)
if ratio > 1.0   →  fee = lerp(30%, 50%, clamp01((ratio - 1.0) / 2.0))
if ratio < 1.0   →  fee = lerp(10%, 30%, ratio)
```

Starting state: each of 5 nodes contributes 1 credit → casino total = 5, individual total = 5 → ratio = 1.0 → **fee = 30%** ✓

---

## Architecture

### New Data Models

**`Scripts/Hardware/HardwareModels.cs`** (new file)

```csharp
namespace Scripts.Hardware;

public record NodeHardwareState
{
    public string NodeId { get; init; } = string.Empty;
    public int IndividualPoolCredits { get; init; } = 0;
    public int CasinoPoolCredits { get; init; } = 0;
    public int TotalCredits => IndividualPoolCredits + CasinoPoolCredits;
}

public record HardwareAllocationSnapshot
{
    public List<NodeHardwareState> Nodes { get; init; } = new();
}

public record CasinoPoolPendingPayout
{
    public string RecipientNodeId { get; init; } = string.Empty;
    public string RecipientAddress { get; init; } = string.Empty;
    public decimal GrossAmount { get; init; }     // before tx fee
    public decimal NetAmount { get; init; }        // after 0.1 BTC tx fee
    public int FromBlockIndex { get; init; }
}

public record CasinoPoolRewardEvent
{
    public int BlockIndex { get; init; }
    public decimal TotalReward { get; init; }
    public decimal CasinoFeePercent { get; init; }
    public decimal CasinoFeeAmount { get; init; }
    public List<CasinoPoolPendingPayout> Payouts { get; init; } = new();
    public bool Distributed { get; init; } = false;
}

public record CasinoPoolState
{
    public List<CasinoPoolRewardEvent> RewardHistory { get; init; } = new();
}
```

### New Repository: `HardwareAllocationRepository`

**`Scripts/Hardware/HardwareAllocationRepository.cs`** (new file)  
Persists to `user://hardware_allocation.json`.

```csharp
// Key methods:
NodeHardwareState GetNode(string nodeId);
void SetNode(NodeHardwareState state);
void MoveCreditsToIndividual(string nodeId, int count);
void MoveCreditsToCasinoPool(string nodeId, int count);
void AddCredits(string nodeId, int count);   // new hardware purchased; goes to individual
int TotalCasinoPoolCredits();               // sum across all nodes
int TotalIndividualCredits();               // sum across all nodes
```

### Integration into `NetworkRoot`

`NetworkRoot` already owns the casino `NodeAgent` and `HandleMinedBlock`. Casino pool logic is added here:

```csharp
// New members in NetworkRoot:
private static CasinoPoolState _casinoPoolState = new();
private const string CasinoPoolStatePath = "user://casino_pool_state.json";
private const decimal CasinoTxFee = 0.1m;

// New public methods:
public void TryCasinoNonceAttempt(out Block? minedBlock, long? minedAtUnixMs = null);
public void DistributeCasinoReward(int blockIndex, decimal totalReward, 
                                    Dictionary<string, int> contributorCredits);
public static decimal CalculateCasinoFeePercent(int casinoTotal, int individualTotal);
public List<CasinoPoolRewardEvent> GetCasinoPoolHistory();
```

---

## Phase 1 — Hardware Credit Data Model & Persistence

**Files to create**: `Scripts/Hardware/HardwareModels.cs`, `Scripts/Hardware/HardwareAllocationRepository.cs`

### Task 1.1 — HardwareModels.cs

Create the records defined in the Architecture section above.  
Namespace: `Scripts.Hardware`.  
No Godot dependencies — pure C# data.

### Task 1.2 — HardwareAllocationRepository

**File**: `Scripts/Hardware/HardwareAllocationRepository.cs`

Persistence path: `user://hardware_allocation.json` (CamelCase JSON, `FileAccess`).

```csharp
public static class HardwareAllocationRepository
{
    private const string SavePath = "user://hardware_allocation.json";
    private static HardwareAllocationSnapshot _snapshot = new();

    public static void EnsureLoaded();
    public static NodeHardwareState GetNode(string nodeId);
    public static void SetNode(NodeHardwareState updated);
    public static void AddCredits(string nodeId, int count);      // to individual pool
    public static void MoveToIndividual(string nodeId, int count); // from casino pool
    public static void MoveToCasinoPool(string nodeId, int count); // from individual pool
    public static int TotalCasinoPoolCredits();
    public static int TotalIndividualCredits();
    public static IReadOnlyList<NodeHardwareState> AllNodes();
    private static void Save();
}
```

**Guard**: `MoveToIndividual` / `MoveToCasinoPool` must not reduce either pool below 0.

### Task 1.3 — Bootstrap Initial Allocation

**Where**: `WalletInitializationService.EnsureAll()` — after `BotWalletRegistry.EnsureAll()`.

If `user://hardware_allocation.json` does not exist, bootstrap:

```csharp
// 5 nodes: "player", "bot_1", "bot_2", "bot_3", "bot_4"
// Each gets: 1 individual credit, 1 casino pool credit
foreach (string nodeId in new[] { "player", "bot_1", "bot_2", "bot_3", "bot_4" })
{
    HardwareAllocationRepository.SetNode(new NodeHardwareState
    {
        NodeId = nodeId,
        IndividualPoolCredits = 1,
        CasinoPoolCredits = 1
    });
}
```

Starting totals: casino pool = 5 credits, individual = 5 credits → ratio = 1.0 → fee = 30%.

---

## Phase 2 — Casino Mining Pool Service (NetworkRoot Integration)

**Files to modify**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`  
**Files to create**: `Scripts/Hardware/CasinoPoolRepository.cs`

### Task 2.1 — CasinoPoolRepository

**File**: `Scripts/Hardware/CasinoPoolRepository.cs`  
Persists `CasinoPoolState` to `user://casino_pool_state.json`.

```csharp
public static class CasinoPoolRepository
{
    public static void EnsureLoaded();
    public static CasinoPoolState Current { get; }
    public static void AddRewardEvent(CasinoPoolRewardEvent evt);
    public static void MarkDistributed(int blockIndex);
    public static List<CasinoPoolRewardEvent> GetUndistributed();
    private static void Save();
}
```

### Task 2.2 — Casino Fee Calculator in NetworkRoot

```csharp
public static decimal CalculateCasinoFeePercent(int casinoTotal, int individualTotal)
{
    if (individualTotal <= 0) return 0.50m;
    double ratio = (double)casinoTotal / individualTotal;
    if (ratio >= 1.0)
    {
        double t = Math.Clamp((ratio - 1.0) / 2.0, 0.0, 1.0);
        return (decimal)(0.30 + t * 0.20); // 30% → 50%
    }
    else
    {
        return (decimal)(0.10 + ratio * 0.20); // 10% → 30%
    }
}
```

### Task 2.3 — TryCasinoNonceAttempt in NetworkRoot

```csharp
public void TryCasinoNonceAttempt(out Block? minedBlock, long? minedAtUnixMs = null)
{
    EnsureInitialized();
    minedBlock = null;
    if (!SharedNodesById.TryGetValue(CasinoNodeId, out NodeAgent? casino))
        return;

    decimal reward = GetBlockRewardForNextCandidate(casino);
    minedBlock = casino.TryMineSingleNonceAttempt(reward);
    if (minedBlock is null)
        return;

    HandleMinedBlock(casino, minedBlock, minedAtUnixMs);
    // Intercept: do NOT send coinbase to casino wallet directly — queue for distribution.
    QueueCasinoRewardForDistribution(minedBlock, reward);
}
```

### Task 2.4 — Reward Distribution Pipeline

After a casino block is mined, `QueueCasinoRewardForDistribution` is called:

```csharp
private static void QueueCasinoRewardForDistribution(Block block, decimal reward)
{
    // Snapshot current contributor credits at the time of mining.
    var allNodes = HardwareAllocationRepository.AllNodes();
    int casinoTotal  = HardwareAllocationRepository.TotalCasinoPoolCredits();
    int indivTotal   = HardwareAllocationRepository.TotalIndividualCredits();

    decimal feePercent = CalculateCasinoFeePercent(casinoTotal, indivTotal);
    decimal feeAmount  = Money.Normalize(reward * feePercent);
    decimal poolAmount = reward - feeAmount;

    var payouts = new List<CasinoPoolPendingPayout>();
    foreach (NodeHardwareState n in allNodes.Where(n => n.CasinoPoolCredits > 0))
    {
        decimal share = Money.Normalize(poolAmount * n.CasinoPoolCredits / casinoTotal);
        decimal net   = share - CasinoTxFee;
        if (net <= 0m) continue;

        string address = GetNodeAddress(n.NodeId); // lookup from NodeAgent or registry
        payouts.Add(new CasinoPoolPendingPayout
        {
            RecipientNodeId = n.NodeId,
            RecipientAddress = address,
            GrossAmount = share,
            NetAmount = net,
            FromBlockIndex = block.Index
        });
    }

    var rewardEvent = new CasinoPoolRewardEvent
    {
        BlockIndex    = block.Index,
        TotalReward   = reward,
        CasinoFeePercent = feePercent,
        CasinoFeeAmount  = feeAmount,
        Payouts = payouts,
        Distributed = false
    };

    CasinoPoolRepository.AddRewardEvent(rewardEvent);
    // Attempt distribution immediately (casino wallet might have enough confirming balance).
    TryDistributePendingCasinoRewards();
}
```

`TryDistributePendingCasinoRewards` iterates `CasinoPoolRepository.GetUndistributed()`, calls `CreateAndBroadcastTransactionToAddress("casino", payout.RecipientAddress, payout.NetAmount)` for each payout, marks event as distributed once all succeed. Distribution can fail if the casino block reward is not yet confirmed (needs to wait for block N+1). Retry on next block.

### Task 2.5 — Hook Distribution Retry into HandleMinedBlock

```csharp
// At the end of HandleMinedBlock (after existing logic):
TryDistributePendingCasinoRewards();
```

This ensures that after every new block (by any node), the casino checks if pending rewards are now spendable and distributes them.

---

## Phase 3 — DiceGame Hardware-Locked Speed & Nonce Routing

**File to modify**: `Screens/DiceGame/DiceGame.cs`

### Task 3.1 — Lock ApsSelector to Hardware Total

Replace `InitializeApsSelector()` behavior for hardware-driven nodes:

```csharp
private void RefreshHardwareDrivenSpeed()
{
    HardwareAllocationRepository.EnsureLoaded();
    NodeHardwareState hw = HardwareAllocationRepository.GetNode(_activeNodeId);
    int total = Math.Max(1, hw.TotalCredits);

    // Lock ApsSelector to hardware total; hide or disable it.
    if (_apsSelector != null)
    {
        _apsSelector.Select(Math.Clamp(total, 1, MaxAutoBetBaseAps) - 1);
        _apsSelector.Disabled = true;
    }
}
```

Call `RefreshHardwareDrivenSpeed()` from:
- `_Ready()` after `InitializeApsSelector()`
- `OnActiveNodeSelected()`
- Any time hardware credits change (event from `HardwareAllocationRepository`)

`NodeStrategyState.BetsPerSecond` is still set from hardware total via `RefreshHardwareDrivenSpeed` — all existing downstream code continues to work unchanged.

### Task 3.2 — Per-Bet Nonce Routing (Player Loop)

In the player's `_Process` autobet execution, each bet currently calls:
```csharp
_blockchainNetworkRoot.TryMineSingleNonceAttempt(PlayerNodeId, out Block? minedBlock, ...);
```

Replace with a routing loop that mirrors hardware allocation:

```csharp
private void ExecutePlayerNonceAttempts(DateTime utcTimestamp)
{
    NodeHardwareState hw = HardwareAllocationRepository.GetNode(PlayerNodeId);

    // Individual pool: N nonce attempts for player's own chain.
    for (int i = 0; i < hw.IndividualPoolCredits; i++)
    {
        if (_blockchainNetworkRoot.TryMineSingleNonceAttempt(PlayerNodeId, out Block? b, ...))
            HandlePlayerMinedBlock(b!);
    }

    // Casino pool: M nonce attempts for casino chain.
    for (int i = 0; i < hw.CasinoPoolCredits; i++)
    {
        _blockchainNetworkRoot.TryCasinoNonceAttempt(out Block? _, ...);
        // Casino reward is handled internally by NetworkRoot — no player action needed.
    }
}
```

This replaces the existing single `TryMineSingleNonceAttempt` call. The total call count = `hw.TotalCredits`, matching the hardware-locked APS.

### Task 3.3 — Per-Bet Nonce Routing (Bot Runner Loop)

In `ExecuteBotBet(BotAutoBetRunner runner)`, apply the same routing:

```csharp
NodeHardwareState hw = HardwareAllocationRepository.GetNode(runner.NodeId);

for (int i = 0; i < hw.IndividualPoolCredits; i++)
{
    if (_blockchainNetworkRoot.TryMineSingleNonceAttempt(runner.NodeId, out Block? b, ...))
        OnBotMinedBlock(runner.NodeId, b!);
}

for (int i = 0; i < hw.CasinoPoolCredits; i++)
{
    _blockchainNetworkRoot.TryCasinoNonceAttempt(out Block? _, ...);
}
```

`GetRunnerEffectiveBetsPerSecond(runner)` returns hardware total instead of `runner.Strategy.BetsPerSecond`:
```csharp
private double GetRunnerEffectiveBetsPerSecond(BotAutoBetRunner runner)
{
    NodeHardwareState hw = HardwareAllocationRepository.GetNode(runner.NodeId);
    return Math.Clamp(hw.TotalCredits, 1, MaxAutoBetBaseAps);
}
```

---

## Phase 4 — BTCPoolsAndHardwareShop Scene (Unified)

One scene for now; shop can be split into its own scene later once behavior is stable.

**Files to create**:
- `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.tscn`
- `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.cs`

### Task 4.1 — Scene Structure

```
BTCPoolsAndHardwareShop (Control)
├── StatusBarPlaceholder (HBoxContainer) — StatusBar injected here
├── BackBtn (Button) → MainMenu
└── MainSplit (HSplitContainer)
    ├── NodeListPanel (VBoxContainer) — left column ~260px
    │   ├── Title: "Mining Nodes"
    │   ├── NodeList (VBoxContainer) — one button per node
    │   │   ├── NodeBtn_player
    │   │   ├── NodeBtn_bot_1  ... NodeBtn_bot_4
    │   │   └── NodeBtn_casino
    │   └── BuyHardwareBtn (Button) — "Buy Hardware [DEV +1]"
    └── DetailPanel (ScrollContainer) — right column
        └── DetailVBox (VBoxContainer)
            ├── NodeTitleLabel
            ├── HardwareSummaryLabel
            ├── PoolsSection (VBoxContainer)
            │   ├── IndividualPoolRow (HBoxContainer)
            │   │   ├── Label "Individual Pool"
            │   │   ├── CreditsLabel
            │   │   ├── MoveToPoolBtn  "→ Casino Pool"
            │   │   └── MoveToIndivBtn "← Individual"
            │   └── CasinoPoolRow (HBoxContainer)
            │       ├── Label "Casino Pool"
            │       ├── CreditsLabel
            │       └── (mirror buttons)
            └── CasinoPoolStatsPanel (VBoxContainer) — shown only when casino is selected
                ├── TotalContributedLabel
                ├── CurrentFeeLabel
                ├── ParticipantsLabel
                └── RewardHistoryList (VBoxContainer)
```

### Task 4.2 — Controller Logic

**Node list**: build one `Button` per node (player, bot_1..4, casino). Clicking a button calls `SelectNode(nodeId)` which populates the detail panel.

**Detail panel for non-casino nodes**:
- Show `NodeId` title + `TotalCredits` summary
- Individual pool row: credits label + `[→ Casino Pool]` button (moves 1 credit; disabled if IndividualPoolCredits == 0)
- Casino pool row: credits label + `[← Individual]` button (moves 1 credit; disabled if CasinoPoolCredits == 0)
- After any move: call `HardwareAllocationRepository.MoveToIndividual/MoveToCasinoPool`, refresh UI, emit event to DiceGame

**Detail panel for casino**:
- Show pool statistics:
  - "Total casino pool credits: N"
  - "Current fee: X.X%"
  - "Individual total credits: M" (for fee context)
  - Participant list with credits per node
  - Last 10 reward events (block index, total reward, fee amount, net distributed, status)

**Buy Hardware button** (DEV only, shown for selected non-casino node):
- Calls `HardwareAllocationRepository.AddCredits(selectedNodeId, 1)` (to individual pool)
- Refreshes detail panel
- Refreshes DiceGame speed if selectedNodeId is player (via event or service call)

**Hardware change event**:
```csharp
// Static event — DiceGame subscribes in _Ready()
public static event Action<string>? HardwareChanged; // string = affected nodeId
```
`HardwareAllocationRepository` raises this after every credit modification. DiceGame calls `RefreshHardwareDrivenSpeed()` when the active node is affected.

### Task 4.3 — StatusBar Integration

```csharp
public override void _Ready()
{
    GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
    // ... rest of setup
}
```

---

## Phase 5 — SceneManager Registration & Navigation

**File to modify**: `Scripts/Services/SceneManager.cs`

### Task 5.1 — Add Scene Entry

```csharp
public enum SceneId
{
    // ... existing entries ...
    BTCPoolsAndHardwareShop,  // new
}

private static readonly Dictionary<SceneId, string> Paths = new()
{
    // ... existing paths ...
    [SceneId.BTCPoolsAndHardwareShop] = "res://Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.tscn",
};
```

### Task 5.2 — MainMenu Navigation Button

**Files to modify**: `Screens/MainMenu/MainMenu.tscn`, `Screens/MainMenu/MainMenu.cs`

Add `BTCPoolsAndHardwareShopBtn` button wired to:
```csharp
_sceneManager?.Go(SceneManager.SceneId.BTCPoolsAndHardwareShop);
```

Label text: `"Mining Pools & Hardware"` (or `"Pools & Shop"` if space is tight).

---

## Phase 6 — Bootstrap Wiring & Smoke Test

### Task 6.1 — EnsureLoaded on Startup

`HardwareAllocationRepository.EnsureLoaded()` must be called before any DiceGame session starts.  
Best hook: `WalletInitializationService.EnsureAll()`, after `BotWalletRegistry.EnsureAll()`.

```csharp
// In WalletInitializationService.EnsureAll():
HardwareAllocationRepository.EnsureLoaded();    // new line
CasinoPoolRepository.EnsureLoaded();            // new line
```

### Task 6.2 — Smoke Test Checklist

Before marking all phases done:

- [ ] Fresh game: 5 nodes each show `IndividualPoolCredits = 1`, `CasinoPoolCredits = 1`
- [ ] DiceGame player autobet rate locked to 2/s; ApsSelector is disabled showing "2X"
- [ ] Bot runners each run at 2 bets/second
- [ ] After a casino block mined: `CasinoPoolRepository` shows a reward event; 5 payout records created
- [ ] After the next block (any miner): casino sends BTC to each contributor; BlockExplorer shows transactions from casino wallet to each node address
- [ ] Casino fee at 30% with starting 1:1 ratio
- [ ] BTCPoolsAndHardwareShop: buy +1 hardware for player → player now shows 3 credits (2 individual, 1 casino); DiceGame speed becomes 3/s
- [ ] Move 1 credit to casino pool → player: 1 individual, 2 casino; speed still 3/s; casino pool total = 6
- [ ] Fee recalculates correctly with new ratio (casino 6 / individual 4 = 1.5 → ~40%)

---

## Open Questions

| ID | Question | Impact |
|---|---|---|
| OQ-1 | Should the casino fee formula use *total* individual credits or *average per node*? Current design uses total vs total. | Fee at starting state = 30% (balanced) either way if symmetric; diverges as players buy different amounts of hardware. Total vs total is simpler. |
| OQ-2 | What happens if a contributor's casino-pool payout net amount ≤ 0 (reward too small to cover 0.1 BTC fee)? | Current plan: skip that payout silently. Could accumulate across events. TBD. |
| OQ-3 | Should `BuyHardwareBtn` have a cost, or remain free for the entire Basic Mode? | User specified free for now (testing). Pricing TBD with hardware variety later. |
| OQ-4 | Should casino pool credits generate any SC income for the contributor, or only BTC mining rewards? | Not specified. Current design: only BTC. SC betting results are unaffected by pool assignment. |
| OQ-5 | Should the player be able to set 0 individual credits (all credits in casino pool)? | Mechanically valid. Means all player bets contribute to casino chain only. Needs UI warning since player won't mine their own blocks at all. |
| OQ-6 | Should moving credits between pools be instant or require a "next block" delay (simulating hardware migration latency)? | Instant for Basic Mode. Real-world delay could be a future detail. |
| OQ-7 | Should `CasinoPoolStatsPanel` show each bot's contributed credits to the player, or only the player's own share? | Currently both are shown. May be too much information. TBD UX pass. |

---

## Future: Option 3 — Hybrid Coordinator (Post-Basic Mode)

This is a design sketch only. No implementation is planned until after Basic Mode is complete.

### Concept

In the hybrid model, the player (and potentially the casino) can act as a **coordinator** that runs multiple sub-pools simultaneously. Each sub-pool behaves like the casino community pool, but the player is the one collecting fees and distributing rewards.

```
Player Coordinator
├── Sub-pool Alpha  ← bot_1, bot_2 contribute credits
│     Stratum server analogue: player's node collects their nonce attempts
└── Sub-pool Beta   ← bot_3, bot_4, non-miner bots contribute
      Different fee schedule, different hardware capacity
```

### Game Design Implications

| Mechanic | Detail |
|---|---|
| Player becomes "pool operator" | Sets their own fee percentage (within game-defined limits) |
| Competing pools | Player pool vs casino pool; bots evaluate fee competitiveness |
| Pool reputation | Bots prefer pools with consistent payouts and fair fees |
| Multiple casino pools | Casino could run geographically distinct pools (game narrative: different mining farms) |
| Entry unlock condition | Suggested: player mines first block independently in Basic Mode |
| Revenue stream | Pool fees become a parallel SC/BTC income stream alongside casino operations |

### Open Design Questions for Option 3

- Should bots dynamically reallocate hardware between pools based on fee competitiveness?
- Can the player poach contributors from the casino pool with lower fees?
- Is there a maximum number of sub-pools a coordinator can manage?
- How is this surfaced in the UI? A second screen (PoolCoordinator) or an extension of BTCPoolsAndHardwareShop?
- Does the block template builder (P4 roadmap) interact with pool selection?

---

## File Checklist

| File | Status | Phase |
|---|---|---|
| `Scripts/Hardware/HardwareModels.cs` | ○ To create | Phase 1 |
| `Scripts/Hardware/HardwareAllocationRepository.cs` | ○ To create | Phase 1 |
| `Scripts/Services/WalletInitializationService.cs` | ○ To modify (bootstrap call) | Phase 1 |
| `Scripts/Hardware/CasinoPoolRepository.cs` | ○ To create | Phase 2 |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | ○ To modify (casino nonce, fee, distribution) | Phase 2 |
| `Screens/DiceGame/DiceGame.cs` | ○ To modify (hardware lock, nonce routing) | Phase 3 |
| `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.tscn` | ○ To create | Phase 4 |
| `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.cs` | ○ To create | Phase 4 |
| `Scripts/Services/SceneManager.cs` | ○ To modify (new enum entry + path) | Phase 5 |
| `Screens/MainMenu/MainMenu.tscn` | ○ To modify (new button) | Phase 5 |
| `Screens/MainMenu/MainMenu.cs` | ○ To modify (new button handler) | Phase 5 |
