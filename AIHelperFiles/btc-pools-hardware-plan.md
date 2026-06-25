# BTC Mining Pools & Hardware Shop — Implementation Plan

**Status**: Phase 1 ✅  Phase 2 ✅  Phase 3 ✅  Phase 4 ○  Phase 5 ○  Phase 6 ○ — **roadmap Step 6 is now active and RE-SCOPED**

> **Phase 3 implementation note (model decision):** the linear model was chosen over the plan's
> literal per-credit loop. **1 bet = 1 nonce attempt** (canonical rule preserved); speed is locked to
> total credits and each bet's single attempt is **round-robin routed** across the node's credit slots
> (first `IndividualPoolCredits` → own chain, rest → casino). Over `TotalCredits` bets this yields
> exactly `IndividualPoolCredits` own + `CasinoPoolCredits` casino attempts — a true reallocation of
> power, not a multiplier (avoids the quadratic `TotalCredits²` attempts/sec of the literal loop).
> Routing lives in `HardwareAllocationRepository.NextNonceTarget(nodeId)`; betting moved to
> `SimulationService` (player + bots) with the manual path in `DiceGame.ProcessBlockchainAttemptForBet`. (see "Step 6 Scope & Decisions" below). This plan builds on the **per-node candidate block model** (`candidate-block-model-plan.md`, roadmap Step 4) — per-credit nonce routing mines real candidates.
> ⚠️ **Two corrections to this plan since it was written:**
> 1. **Gradual miner spawning is POSTPONED** (needs a per-bot strategy set first), so for now we keep **DEV access to all bettable nodes**; the "player + 4 bots at block 1" assumption is fine for the prototype.
> 2. **The bot/player betting loop moved to `SimulationService`** during the background-simulation work — so Phase 3's nonce-routing/speed-lock now targets `SimulationService.ExecutePlayerBetOnce` / `ExecuteBotBet`, **not** `DiceGame.ExecuteBotBet` / `BotAutoBetRunner` (those no longer exist in DiceGame).

**Architecture summary**:
- **Option 2 (solo / P2P)**: each hardware credit in a node's *individual pool* generates 1 nonce attempt per bet, routed to that node's own blockchain — current behavior, extended with hardware count control.
- **Option 1 (community pool)**: hardware credits assigned to the *casino pool* route each bet's nonce attempt to the casino node's blockchain; the casino distributes block rewards to contributors proportionally minus a dynamic fee.
- **Option 3 (hybrid coordinator)**: designed in a future plan — reserved for post-Basic Mode.

**Starting state**: player + 4 bots each receive **2 hardware credits** at bootstrap (1 individual, 1 casino pool).  
Betting speed in DiceGame is **locked to total hardware credits** (not freely selectable). 2 credits → 2 bets/second.

---

## Step 6 Scope & Decisions (2026-06-23)

Answers that re-scope Step 6 (`IMPLEMENTATION_ROADMAP.md` Step 6 = gradual participants + miner bots + hardware pools):

- **Bots never mine without the player** (original decision stands — time only advances while the player participates). **Gradual miner-bot spawning is POSTPONED** until a curated set of per-bot strategies exists; each miner will *later* spawn gradually with its era-appropriate hashrate. **For now: keep DEV access to all bettable nodes** in DiceGame (no intro gating).
- **FIRST — Network Difficulty Regulator** (dedicated section below): foundational; **built first**, independent of gradual spawn and hardware.
- **Bot Play-History scene** — moved to its **own plan** (`bot-play-history-plan.md`); sequenced **after** the regulator, tracked separately.
- **Hardware credits at introduction:** deferred until a hardware **prototype** is working — build the prototype first (flat credits), wire credit-at-introduction afterward.
- **Era-based hashrate + obsolescence:** deferred to the definitive Basic-Mode build.
- **DEV features** (free "Buy Hardware", dev access to all nodes, dev panels): fine **while developing**; **all DEV features are removed for the final Basic Mode.**

### Revised Step 6 order
1. **Network Difficulty Regulator** (foundational — this plan, section below).
2. **Bot Play-History scene + Notepad access** — own plan (`bot-play-history-plan.md`).
3. **Hardware/pools prototype** — credit model + casino pool + hardware-locked speed (routed through `SimulationService`); credit-at-introduction & obsolescence deferred.
4. **Gradual miner spawning** — postponed to a later step (once per-bot strategies exist).

---

## NEW SYSTEM — Network Difficulty Regulator

**Goal**: replace the static difficulty with a regulator that keeps the **average block time near a target** as total network power and participant count change. Foundational for hardware/pools (more power must be pushed back) and gradual spawning (more participants must be pushed back). Buildable **now** against the current model.

### Current state
- **Before D.1:** difficulty was **static & discrete** — `"00"` prefix + next-hex ≤ '6' (≈585 expected attempts/block; the "~107" figure in old docs was wrong).
- **After D.1 (done):** difficulty is a **continuous, persisted-per-block** value (`Block.Difficulty`), seeded at `InitialDifficulty = 4096/7 ≈ 585.14` (same pace). The regulator (D.2) just needs to make `GetNextBlockDifficulty()` dynamic.

### Design principles (grounded in real BTC)
- **Block time is the canonical signal.** Real Bitcoin **never measures hashrate** — it only compares actual vs. expected **block time** and retargets. Block time already captures *total* network power **and** participant count **and** variance. (So measuring power directly is redundant as the *primary* control.)
- **Use TOTAL hashrate, not average.** If/when we add a power term, it must be the **sum** of all active miners' power (Σ credits × bets/sec). The *average* normalizes out participant count — the very variable we want included. `total = avg × count`.
- **Bitcoin classic** retargets every **2016 blocks**: `newDifficulty = oldDifficulty × (expectedTimespan / actualTimespan)`, clamped to **[0.25×, 4×]**. Robust but slow; oscillates on small/spiky networks.
- **Per-block algorithms (DigiShield / LWMA)** retarget **every block** from a weighted moving average of recent solvetimes (recent weighted more). Fast, smooth, oscillation-resistant — **the better fit for this fast, fractal game.**

### Finalized implementation (decisions baked in)

**Constants** (`BlockchainService`): `TargetBlockSeconds = 58_500` (OQ-8), `LwmaWindow = 20` (OQ-10), `MaxStepUp = 2.0`, `MaxStepDown = 0.5` (OQ-10), `MinDifficulty = 1.0` floor, `DifficultyEaseAlpha = 0.7` (smoothing, tuned by test).

> **Final shape = HYBRID + easing** (OQ-11 reversed, 2026-06-23): `target = anchor × feedbackTrim`, then ease: `next = current + DifficultyEaseAlpha × (target − current)`.
> - **anchor** (feed-forward): `InitialDifficulty × networkPower` — the correct difficulty for the current *known* total power (= `(TargetBlockSeconds/clockSpeed) × power`). Instant, unclamped. `0` power (bootstrap/idle) → hold at current (feedback-only).
> - **feedbackTrim** (LWMA over `W` solvetimes, clamped `[0.5×, 2×]`): the real-process block-time correction for calibration drift + variance.
> - **easing** `α=0.7`: ramps a change in over ~3 blocks instead of snapping (user-tuned).

- **0. Continuous difficulty (foundational refactor).** Replace the discrete prefix rule with a numeric `Difficulty = expectedAttemptsPerBlock` (double). Acceptance: interpret the 64-hex block hash as a 256-bit `BigInteger` `H`, accept if `H ≤ 2²⁵⁶ / Difficulty` (probability `1/Difficulty`). `IsHashAtTargetDifficulty(hash, difficulty)` takes the difficulty; `GetExpectedAttemptsForCurrentDifficulty()` returns the **current chain** difficulty. Seed the genesis/initial difficulty at **≈585** (today's effective value) so nothing changes until the regulator runs.
- **1. Persisted per block (OQ-12).** Add **`Block.Difficulty`** (the value the block was mined against). Mining a candidate: difficulty is computed from the previous blocks (below), written onto the block, and the PoW must satisfy it. Validation (`ChainIsValid`) checks each block's hash against **its own** `Difficulty`. On load, the **current** difficulty = the last block's `Difficulty` → O(1), no genesis replay.
- **2. Primary regulator — LWMA block-time feedback (OQ-9).** When building the next candidate, compute the next difficulty from the last `W` blocks' solvetimes (in-game timestamp deltas), recent blocks weighted more (linear weights), then `nextDifficulty = clamp( currentDifficulty × (TargetBlockSeconds / lwmaSolvetime), currentDifficulty×0.5, currentDifficulty×2.0 )`, and not below `MinDifficulty`. (Fewer than `W` blocks early on → use what's available / hold at seed.)
- **3. Anti-oscillation / safety.** Per-step clamp + `MinDifficulty` floor. Timestamps are engine-controlled (no adversary) → we **skip** Bitcoin's median-time-past / timestamp-attack defenses.
- **4. Fractal calibration.** `TargetBlockSeconds` is fixed (OQ-8); `W`/clamps/`α` tunable. Calibrate so **relative** jumps mirror BTC's fractal (~16.5× across 2010), not absolute hashes.
- **Feed-forward — REINSTATED as a hybrid (OQ-11 reversed).** Pure block-time feedback was too slow to converge in a tiny network; the user chose to bring back the power term as the instant *anchor*, with the LWMA as the trim. Total power (Σ active miners' bets/sec) is pushed from `SimulationService` (`GetActiveMiningRates` sum) into `NetworkRoot.SetActiveMiningPower`, read by `GetNextBlockDifficulty(power)`.
- **Where it lives.** `BlockchainService.GetNextBlockDifficulty(double networkPower)`, called from the mining path (`NodeAgent` → `NetworkRoot`). No separate per-frame service.

### Difficulty Regulator — small steps
- **D.1 — Continuous difficulty + persisted target (no behavior change yet).** ✅ **DONE.** `Block.Difficulty` field (persisted via the existing JSON chunks/snapshot); `BigInteger` target math (`MaxHash256`, `HexToBigInteger`); `IsHashAtTargetDifficulty(hash, difficulty)`; `InitialDifficulty = 4096/7 ≈ 585.14` (the *exact* probability of the old `"00"`+next-hex-≤'6' rule → identical pace) seeded on genesis + every new block; `ProofOfWork`/`CommitBlock` take + stamp the difficulty; `GetNextBlockDifficulty()` (D.1: returns the tip's difficulty = constant) and instance `GetExpectedAttemptsForCurrentDifficulty()` read the tip; `ChainIsValid`/`TryAcceptMinedBlock` validate each block against **its own** `Difficulty`; `EffectiveDifficulty` coerces a missing/0 value (pre-D.1 save) to `InitialDifficulty`. **Verified:** chain mines + validates + round-trips across reload. *Files:* `Models.cs`, `BlockchainService.cs`, `NodeAgent.cs`, `NetworkRoot.cs`.
  - **Display added early (a slice of D.3, for verification):** Block Explorer now shows the network difficulty on the chain-info line, the latest-block panel, and the per-block lookup. The richer **avg block time + trend** still belongs to D.3.
- **D.2 — Retarget (HYBRID, not pure LWMA).** ✅ **DONE + user-tested.** `GetNextBlockDifficulty(networkPower)` = `anchor × feedbackTrim`, eased by `α`. Feed-forward anchor = `InitialDifficulty × power`; LWMA feedback trim (clamped); easing `α=0.7`. Power plumbed `SimulationService.SetActiveMiningPower` → `NetworkRoot._activeMiningPower` → `NodeAgent` → `GetNextBlockDifficulty`. `Block.MiningPower` stamped (diagnostic).
  - **Bootstrap pin (fix):** the regulator is **bypassed during `_bulkMining`** — bootstrap blocks are mined at a fixed `InitialDifficulty` (`MineForNode` passes `forcedDifficulty`). The historical pre-mine uses *scripted* timestamps, so running the block-time feedback there is meaningless and was drifting the start difficulty (e.g. down to ~100). Now the game starts at ~585 and the regulator takes over only for live play.
  - **First-attempt / per-tip lock (fix):** difficulty is locked on the **first nonce attempt at a tip** (`NodeAgent._candidateDifficulty` keyed by `_difficultyTipHash`) and kept for the whole block **across mempool changes** (a bot tx rebuilds the template but must not move the difficulty — this was the "current block changes" bug). A power/participant change *before* the first attempt counts for that block; *after*, it applies from the next. `GetPlayerNextBlockDifficulty` reports the locked value.
  - **Manual/auto parity (fix):** manual betting now sets the same network power (player + configured bots) via `DiceGame.SetManualMiningPower` before the bet, so manual mining regulates difficulty identically to autobet (previously manual left power at 0 → stuck at player-only difficulty).
  - *Verified: a bot joining ramps difficulty up over a few blocks and block time settles back near target; removing it ramps down; bootstrap starts at ~585.*
- **D.3 — Block Explorer readout.** ✅ **DONE.** Main chain-info line shows the **mining (next-block) difficulty** (`GetPlayerNextBlockDifficulty`) + trend (vs last block) + **recent avg block time** vs target; each block's own difficulty stays in its panel/lookup. Auto-refresh via the 1 s tick.
- **D.4 — Calibrate & document.** ✅ **DONE.** Tuned `α=0.7` (kept `W=20`, clamp `[0.5×,2×]`) by test; documented in `ProjectDesignManual.md` Ch.26 + `CLAUDE.md`. (Fractal-scale calibration of `TargetBlockSeconds`/jumps remains a future tuning item when later eras land.)

---

## Bot Play-History scene — moved out

The Bot Play-History scene (last 260 plays per active miner bot + Notepad access) now has its **own plan**: **`AIHelperFiles/bot-play-history-plan.md`**. It's part of Step 6 but tracked separately and **sequenced after the Difficulty Regulator**. (Decisions OQ-13/OQ-14 live in that file.)

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

### Two-Piece Hardware Model + Obsolescence (decided 2026-06-21)

> Design decisions captured while on the `scheduled-bot-transactions` branch (referral OQ-C surfaced them). They belong to **this** future hardware/pools branch — route to `main` so they persist.

**Every miner has two kinds of hardware:**

1. **Base (Piece 1) — the computer.** A normal laptop/PC. Always present (one per node), gives the baseline hashrate, and is **not** the thing you keep buying. Era-appropriate baseline: 2009 ≈ a Core 2 Duo laptop (~1–5 MH/s) or an i7 desktop (~20–30 MH/s).
2. **Accelerator (Piece 2) — the timeline-appropriate, buyable piece.** The hardware/software that makes sense in the current in-game era and complements the base. **These are what the player buys to raise mining power** (they map onto the "hardware credits" below — each accelerator ≈ credits / nonce-attempts per bet). The accelerator **changes by era** (see timeline) and **becomes obsolete** over time.

**A minimal viable miner = Base (1) + at least one Accelerator (1).** Buying more / better accelerators increases hashrate.

**Obsolescence (default for Basic Mode):** an accelerator's *competitive* life averaged ~**12 in-game months** in the 2009–2012 window (CPU → GPU → FPGA → ASIC waves each obsoleted the prior tier). Default obsolescence = **12 in-game months**, and it should **shorten in later eras** (ASIC era → a few months). The hardware/pools scene shows each piece's **remaining lifetime live**. (Competitive/economic life is shorter than physical life — model the economic one.)

**Era timeline (historical reference — from `hardware mineria.txt`):**

| In-game era | Accelerator (Piece 2) | Approx hashrate | Notes |
|---|---|---|---|
| 2009 early | CPU baseline (Core 2 Duo laptop) | ~1–5 MH/s | difficulty = 1; CPU sufficient |
| 2009 late | Multi-core CPU (i7) + code optimizations (midstate, SIMD) | ~20–30 MH/s | 2–8× over single-core; ~10–20% from optimizations |
| 2010-07 | **GPU** (NVIDIA GTX 260 ~200–300 MH/s; AMD Radeon 5970 ~600–700 MH/s) | ~200–700 MH/s | ArtForz; 50–100× CPU; the big jump |
| 2010-07 | Mining **pools** (Slushpool) | — | distributes risk, not raw speed (maps to our casino pool) |
| 2011 | Multiple GPUs | ~1–2 GH/s | competition heats up |
| 2012 | FPGA (research → early units); ASIC research begins | ~ tens of GH/s | GPUs still viable |
| 2013-01 | **ASIC** (Avalon, Antminer…) | ≫ GPUs | GPUs become useless; ASIC industry era |

Difficulty grew ~16.5× across 2010 (≈1 → ~12,000 by Dec 2010); our fractal must scale the *relative* jumps, not the absolute numbers.

**How it connects:** Piece 2 (accelerators) are the buyable units modeled by the **hardware credits** below; the Base is the starting credit a node always has. Obsolescence retires accelerator credits over time, pushing the player to keep upgrading.

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

### Resolved decisions (2026-06-23)

| ID | Decision |
|---|---|
| OQ-8 | **`T_target` = 58,500 in-game seconds/block** (≈16h40m at 100X). Fixed — it's what keeps temporal + fractal coherence with the 100X scale. |
| OQ-9 | **LWMA per-block** retarget (recommended). Document it thoroughly once implemented. |
| OQ-10 | **`W` = 20 blocks**, **per-step clamp [0.5×, 2×]** (recommended). Explain clearly in the docs. **Also: show the live network difficulty in the Block Explorer** (only there, for now). |
| OQ-11 | **REVERSED → hybrid feed-forward.** Pure block-time feedback converged too slowly in a tiny (1–5 miner) network. The power term is back as the instant *anchor* (`InitialDifficulty × power`), with the LWMA as the trim, and an easing factor `α` so changes ramp over a few blocks rather than snapping. |
| OQ-12 | **Persisted** difficulty (store the target per block) — not recomputed from genesis (could get slow at large heights). Periodic weekly/monthly/yearly reference snapshots are a *maybe-later* if the chain grows huge. |

*(OQ-13 / OQ-14 concerned the Bot Play-History scene — moved to `bot-play-history-plan.md`.)*

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
| `Scripts/Hardware/HardwareModels.cs` | ✅ Created | Phase 1 |
| `Scripts/Hardware/HardwareAllocationRepository.cs` | ✅ Created | Phase 1 |
| `Scripts/Services/WalletInitializationService.cs` | ✅ Modified (bootstrap call) | Phase 1 |
| `Scripts/Hardware/CasinoPoolRepository.cs` | ✅ Created | Phase 2 |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | ✅ Modified (casino nonce, fee, distribution) | Phase 2 |
| `Screens/DiceGame/DiceGame.cs` | ✅ Modified (hardware lock, manual nonce routing, bot speed) | Phase 3 |
| `Scripts/Services/SimulationService.cs` | ✅ Modified (player + bot nonce routing) | Phase 3 |
| `Scripts/Hardware/HardwareAllocationRepository.cs` | ✅ Modified (NextNonceTarget round-robin router) | Phase 3 |
| `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.tscn` | ○ To create | Phase 4 |
| `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.cs` | ○ To create | Phase 4 |
| `Scripts/Services/SceneManager.cs` | ○ To modify (new enum entry + path) | Phase 5 |
| `Screens/MainMenu/MainMenu.tscn` | ○ To modify (new button) | Phase 5 |
| `Screens/MainMenu/MainMenu.cs` | ○ To modify (new button handler) | Phase 5 |
