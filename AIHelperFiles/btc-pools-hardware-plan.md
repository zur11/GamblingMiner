# BTC Mining Pools & Hardware Shop — Implementation Plan

**Status**: Phase 1 ✅  Phase 2 ✅  Phase 3 ✅  Phase 4 ✅  Phase 5 ✅  Phase 6 ✅ (wiring; Task 6.2 smoke test = manual, in-editor) — **roadmap Step 6 is now active and RE-SCOPED**

> **Phase 3 implementation note (model decision):** the linear model was chosen over the plan's
> literal per-credit loop. **1 bet = 1 nonce attempt** (canonical rule preserved); speed is locked to
> total credits and each bet's single attempt is **round-robin routed** across the node's credit slots
> (first `IndividualPoolCredits` → own chain, rest → casino). Over `TotalCredits` bets this yields
> exactly `IndividualPoolCredits` own + `CasinoPoolCredits` casino attempts — a true reallocation of
> power, not a multiplier (avoids the quadratic `TotalCredits²` attempts/sec of the literal loop).
> Routing lives in `HardwareAllocationRepository.NextNonceTarget(nodeId)`; betting moved to
> `SimulationService` (player + bots) with the manual path in `DiceGame.ProcessBlockchainAttemptForBet`.
> **Rate is read LIVE from hardware** (`SimulationService.HardwareRate(nodeId)` in `_Process`/`TickBots`/
> `GetActiveMiningRates`) — never cached at autobet start — so buying/moving credits mid-run takes effect
> immediately (bet rate, Block Explorer ⛏ readout, and difficulty feed-forward all update at once). The
> DiceGame ApsSelector is display-only and re-locked to hardware via `RefreshHardwareDrivenSpeed()`
> (also from `ApplyAutoBetSpeedSettings`, so a strategy load can't reset the shown value to a stale 1X). (see "Step 6 Scope & Decisions" below). This plan builds on the **per-node candidate block model** (`candidate-block-model-plan.md`, roadmap Step 4) — per-credit nonce routing mines real candidates.
> ⚠️ **Two corrections to this plan since it was written:**
> 1. **Gradual miner spawning is POSTPONED** (needs a per-bot strategy set first), so for now we keep **DEV access to all bettable nodes**; the "player + 4 bots at block 1" assumption is fine for the prototype.
> 2. **The bot/player betting loop moved to `SimulationService`** during the background-simulation work — so Phase 3's nonce-routing/speed-lock now targets `SimulationService.ExecutePlayerBetOnce` / `ExecuteBotBet`, **not** `DiceGame.ExecuteBotBet` / `BotAutoBetRunner` (those no longer exist in DiceGame).

**Architecture summary**:
- **Option 2 (solo / P2P)**: each hardware credit in a node's *individual pool* generates 1 nonce attempt per bet, routed to that node's own blockchain — current behavior, extended with hardware count control.
- **Option 1 (community pool)**: hardware credits assigned to the *casino pool* route each bet's nonce attempt to the casino node's blockchain; the casino distributes block rewards to contributors proportionally minus a dynamic fee.
- **Option 3 (hybrid coordinator)**: designed in a future plan — reserved for post-Basic Mode.

**Starting state** (revised 2026-06-25): player + 4 bots each start with **1 hardware credit** at first-launch bootstrap (1 individual, **0 casino pool**) — everyone begins at a single private-pool credit; casino-pool participation is opt-in by moving credits.  
Betting speed in DiceGame is **locked to total hardware credits** (not freely selectable). 1 credit → 1 bet/second. *(Was `2 credits = 1 individual + 1 casino` before this revision.)*

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
// Each gets: 1 individual credit, 0 casino pool credits  (revised 2026-06-25)
foreach (string nodeId in new[] { "player", "bot_1", "bot_2", "bot_3", "bot_4" })
{
    HardwareAllocationRepository.SetNode(new NodeHardwareState
    {
        NodeId = nodeId,
        IndividualPoolCredits = 1,
        CasinoPoolCredits = 0
    });
}
```

Starting totals: individual = 5 credits, casino pool = 0 (no casino contributors at first launch — players opt in by moving credits to the casino pool). **Revised 2026-06-25**: was `1 individual + 1 casino` each; now `1 individual + 0 casino` so everyone starts at a single private-pool credit.

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

> **Note (2026-06-25)**: the bootstrap was later changed to **1 individual + 0 casino** per node. The figures below were written against the original **1 + 1** bootstrap, so the *starting* numbers differ now (a fresh game shows `CasinoPoolCredits = 0`, casino pool empty, player speed 1/s; move a credit into the casino pool before exercising the casino-payout steps). The relative checks (buy/move/fee-recalc) still hold from the new baseline.

- [ ] Fresh game: 5 nodes each show `IndividualPoolCredits = 1`, `CasinoPoolCredits = 1`
- [ ] DiceGame player autobet rate locked to 2/s; ApsSelector is disabled showing "2X"
- [ ] Bot runners each run at 2 bets/second
- [ ] After a casino block mined: `CasinoPoolRepository` shows a reward event; 5 payout records created
- [ ] After the next block (any miner): casino sends BTC to each contributor; BlockExplorer shows transactions from casino wallet to each node address
- [ ] Casino fee at 30% with starting 1:1 ratio
- [ ] BTCPoolsAndHardwareShop: buy +1 hardware for player → player now shows 3 credits (2 individual, 1 casino); DiceGame speed becomes 3/s
- [ ] Move 1 credit to casino pool → player: 1 individual, 2 casino; speed still 3/s; casino pool total = 6
- [ ] Fee recalculates correctly with the new ratio. NOTE: after that move the player keeps **1**
      individual credit, so individual total = 5 (4 bots + player), casino total = 6 → ratio 1.2 →
      **32%** by the exact Task 2.2 formula (`0.30 + clamp((ratio−1)/2,0,1)×0.20`). The earlier
      "~40%" was loose prose; the implemented formula is the source of truth.

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
| `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.tscn` | ✅ Created | Phase 4 |
| `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.cs` | ✅ Created | Phase 4 |
| `Scripts/Hardware/HardwareAllocationRepository.cs` | ✅ Modified (HardwareChanged event) | Phase 4 |
| `Screens/DiceGame/DiceGame.cs` | ✅ Modified (HardwareChanged subscription) | Phase 4 |
| `Scripts/Services/SceneManager.cs` | ✅ Modified (new enum entry + path) | Phase 5 |
| `Screens/MainMenu/MainMenu.tscn` | ✅ Modified (new button) | Phase 5 |
| `Screens/MainMenu/MainMenu.cs` | ✅ Modified (new button handler) | Phase 5 |
| `Scripts/Services/WalletInitializationService.cs` | ✅ Modified (CasinoPoolRepository.EnsureLoaded) | Phase 6 |

---

## Difficulty Regulator — Power-Step Contingency Plan (2026-06-25)

**Status**: ✅ **CLOSED (2026-06-25) — hybrid regulator fully validated, no fixes needed.** F0 + dev tools + power-accounting audit + steady-state (power 2) + Test 1 (up-step 2→10) + Test 2 (down-step 10→1) all done. Calibration correct at steady state for **power 1, 2 and 10** (difficulty ≈ anchor, ratio ≈ 1). Up-step: mild variance-driven overshoot (1.13×, ~2-block settle) → **F1 not justified**. Down-step: symmetric ~3-block descent → **F2 not justified**. Test Run #1's 1.4× was an un-converged transient (unlucky variance), not a bug; accounting audited correct. **F1/F2/F3/F4 all unnecessary.** Extends the hybrid regulator (OQ-11/OQ-12).

### Context — what triggered this

A ~80-in-game-day test run (121 blocks; chain at `user://blockchain/state.json`) with **all bots + the casino pool active** and an **extra hardware credit bought for the player and assigned to the casino pool**. Goal was to watch the difficulty regulator across a power step and compare per-share payouts of that extra credit in the casino pool.

### Empirical findings

**Baseline (blocks 2–117, power ≈ 1, difficulty pinned at `InitialDifficulty` 585.14):** the regulator is excellent. Feedback-only mode (power 0/1) held difficulty flat and solvetimes hugged target — mean ratio **0.98×**, **sd 0.16**, range 0.70–1.28×, **0%** of blocks slower than 2×. Note the **low sd (0.16)**: this sim's per-block solvetime is *tightly regulated*, not a noisy exponential PoW process.

**Transition (block 118): power stepped ~1 → 11** (bots + casino pool switched on). The feed-forward anchor correctly identified the new equilibrium (`InitialDifficulty × 11 ≈ 6437`) and easing ramped difficulty 585 → 4658 → 5357 → 5396 → 5611 over 4 blocks. But solvetime ratios came out **2.43×, 1.64×, 0.49×, 0.78×**.

Because baseline sd is 0.16, the 2.43× block is a **~9σ event — structural, not variance.** Inverting `realizedPower = 100 · difficulty / solvetimeSec` (calibration verified by baseline ≈ 1.0) gives the realized throughput during the transition:

| Block | Difficulty | Solvetime ratio | Realized power |
|---|---|---|---|
| 118 | 4658 | 2.43× | **~3.3** (stall) |
| 120 | 5396 | 0.49× | **~18.9** (catch-up burst) |
| 121 | 5611 | 0.78× | **~12.3** (settling) |

So during block 118 the network only delivered attempts equivalent to power ~3.3, although **the power reported to the regulator was a clean step to 11** (`HardwareRate` is a constant config value, summed over running sessions — there is *no ramp in the regulator's input*).

### Root cause — two stacked effects at a power step

1. **Deterministic easing ramp (~3 blocks, by design).** `next = current + α·(target − current)` with `DifficultyEaseAlpha = 0.7` ramps difficulty up over a few blocks. During the ramp difficulty sits *below* equilibrium, so this makes early blocks *faster*, not slower — it is **not** the cause of the slow block 118.

2. **Throughput transient (the real culprit for block 118).** `StartBots()` builds all `BotRunner`s in one synchronous loop (they all tick on the same frames), each with `AccumulatorSeconds = 0` (so no synchronized burst at t=0, but execution is concentrated). At the moment of enabling the fleet (buy hardware + assign to pool + pool/UI spin-up) a **frame hitch** produces a large `delta`; `Math.Min(accum + delta, MaxBacklogSeconds = 2.0)` **discards** attempts beyond 2 real-seconds' worth → permanent loss → throughput dips (~3.3). The retained 2 s backlog then flushes → overshoot (~18.9) → settles (~12.3). Bot cold-starts (base bet, fresh bankroll, early stop→recharge→restart cycles) cost extra ticks in that first interval.

> The anchor is fed the *instantaneous configured* power (11) while the *realized* throughput during the first block is ~3.3 → difficulty is briefly priced for more power than is present → the slow block. Lowering `α` only softens effect (1); it does **not** address effect (2), which is what produced block 118.

Code paths: power feed `SimulationService.GetTotalActiveMiningPower()` → `NetworkRoot.SetActiveMiningPower()` (stores static `_activeMiningPower`) → `BlockchainService.GetNextBlockDifficulty(_activeMiningPower)`. Execution caps in `SimulationService` (`MaxBetsPerFrame = 10`, `MaxBacklogSeconds = 2.0`, per-node accumulators in `_Process` / `TickBots`).

### Goal

After a **power step** (enable fleet / add hardware), the first 1–3 blocks should not be mis-priced, **without** distorting the long-run pace or the response to power *drops*. Recommended order by leverage: **F0 → F1 → F3 → F2 → (F4 fallback) → F5.**

### Phase F0 — Instrumentation & baseline *(prerequisite, no logic change)* ✅

Stop inferring realized power; measure it.

- **What**: on each mined block, append a row `utcMs, miner, index, configuredPower, realizedPower (= difficulty·clockSpeed/solveSec), difficulty, anchor, solveSec, solveRatio` → `user://logs/difficulty_trace.csv`. (`feedbackTrim`/`easedNext` deferred — not needed to confirm the issue; can be added for F5 tuning.)
- **Where**: ✅ `NetworkRoot.AppendDifficultyTrace()`, called from `HandleMinedBlock` **inside the `!_bulkMining` guard** — so the historical bootstrap replay is excluded and only **live-mined** blocks are traced. Per-chain (uses the miner's own `Chain[^2]` for solvetime); rows interleave across chains → filter by the `miner` column. `clockSpeed = TargetBlockSeconds / InitialDifficulty`. Build verified green.
- **Acceptance**: reproduce a power step and capture the realized-power curve; confirm or refute the stall→burst→settle pattern before changing logic. ✅ **done — see Test Run #1 below.**
- **Risk**: none (read/log only).

### F0 Test Run #1 (2026-06-25) — findings & plan revision

First live trace (`difficulty_trace.csv`, 17 blocks, indices 113–129, single shared chain). A **power step 2 → 10** at block 117 (anchor jumped 1170 → 5851 = `585 × {2,10}` ✓). Clock confirmed: `CalendarTimeService.SpeedMultiplier = 100`, calendar free-runs on `delta × 100` (not per-bet), so `realizedPower = difficulty × 100 / solveSec` measures the **true attempt-execution rate** with no clock artifact. **This run overturns part of the earlier (state.json-based) diagnosis.**

**Finding 1 — live per-block variance is huge; single blocks are uninformative.** `solveRatio` ranged **0.023× → 3.67×**; `realizedPower` **0.54 → 434**. Live PoW solvetime is ≈ exponential. The earlier "block 118 = structural ~9σ stall" was a **bootstrap artifact** — blocks 2–117 in `state.json` were bulk-mined/semi-synthetic (sd 0.16), not representative of live mining. ⇒ **F3 (startup throughput stall) is DROPPED** — no evidence of a stall; realized throughput sits at/above configured.

**Finding 2 (robust signal) — the feed-forward anchor under-calls equilibrium by ~1.4×.** Don't read single blocks; read where difficulty *converged*. At power 2, difficulty held ~1000–1160 ≈ anchor 1170 (calibration correct; outlier-removed aggregate realized ≈ 2.17 ≈ 2). At power 10, difficulty **climbed via LWMA feedback to ~8400**, and only there did a block hit target (**block 128: dif 8396, ratio 0.94**). So true equilibrium ≈ **8400** vs anchor **5851** → **anchor ≈ 30–45% low** (8400/5851 = 1.44; aggregate realized over the power-10 window ≈ 14 vs configured 10 — same factor). The offset appeared **right after extra hardware was assigned to the casino pool**.

**Finding 3 — the real symptom is the OPPOSITE of the original complaint.** While feedback climbed 5851 → 8400, blocks ran **too fast** (window mean `solveRatio` ≈ **0.73**), not delayed. The "first nodes take too long" impression was a couple of high-variance slow blocks (115: 3.67×, 129: 2.95×). So the issue is mild over-issuance during catch-up, not a delaying shock.

**Plan revision (priorities updated):**

| Item | Was | Now (post steady-state verdict) |
|---|---|---|
| Power accounting (casino-pool credits) | 🔴 PRIMARY (suspected bug) | ✅ **audited — correct, no bug** |
| Anchor calibration | 🔴 PRIMARY | ✅ **verified correct at steady state — power 2 AND power 10** — no fix |
| F1 — EMA on power | highest | ⚪ **not justified** (Test 1: overshoot 1.13×, ~2-block settle, variance-driven) |
| F2 — asymmetric easing | medium | ⚪ **not justified** (Test 2: symmetric ~3-block descent, no prolonged stall) |
| F3 — startup stall | medium | ⚪ **dropped** (no evidence) |
| F4 — lower α | fallback | fallback (unneeded; not the cause) |
| Confirm calibration at power ≈10 | open | ✅ **closed by Test 1** |

**Lead hypothesis (casino-pool undercount): ❌ REFUTED by code audit (2026-06-25).** Traced the full path: `RouteNonceAttempt` (and DiceGame's manual mirror) makes **exactly one** attempt per bet, round-robin to the casino chain *or* the node's own chain (`NextNonceTarget`) — no doubling. `GetTotalActiveMiningPower = Σ HardwareRate` over {player + running bots} = Σ `TotalCredits` (individual **+** casino); every casino-routed attempt originates from one of those counted credits. The `casino` node is **not** a runner (`BuildBotConfigs` excludes `player` and requires a valid strategy; casino is a wallet/pool node) → no uncounted attempts, no double-count. One logical chain (consensus via `BroadcastBlock`; globally sequential indices confirm it) → total attempt rate = power, so `anchor = InitialDifficulty × power` **is** the correct equilibrium level. **Accounting is correct.**

**Revised likely cause: feed-forward + feedback overshoot during the un-converged transient (not a calibration bug).** At the step the anchor sets the level (~5851), but easing keeps difficulty *below* equilibrium for a few blocks → those blocks run fast → `LWMA(solvetimes)` small → `feedbackTrim = Target/LWMA > 1` → `target = anchor × trim` overshoots above the anchor → difficulty climbs past it (to 8418, still rising at block 129 — peak not yet reached). Test Run #1 captured only the climb, not the settle-back. The feed-forward already corrects the level; the feedback then piles on, reacting to the easing-induced fast blocks.

**Decider — the ≥30-block steady-state run (in progress, using the new 100X→9000X dev tool):**
- difficulty settles **≈ anchor** (5851 at power 10), mean ratio → 1.0 ⇒ **no calibration error**; the 1.4× was pure transient overshoot → refinement = *tame overshoot on large steps* (F1 EMA on power smooths the step; or damp feedback while the anchor makes a big jump).
- difficulty stabilizes **~40% above anchor**, ratio → 1.0 ⇒ a **real factor** exists → investigate consensus/orphan losses or a constant.

Caveat: aggregate realized power is time-weighted, so a few long-tail solvetimes can bias a small sample — judge on ≥30 blocks.

### F5 Steady-State Run — VERDICT (2026-06-25) ✅ regulator is correctly calibrated

Second trace: **constant power = 2.0** for 32 blocks (no step), run fast via the dev time-accel tool. Aggregates (dropping the 2 leading warmup blocks):

| Metric | Observed | Expected if calibrated |
|---|---|---|
| aggregate realized power (`100·Σdif/Σsolve`) | **2.05** | ≈ 2.0 ✓ |
| mean `solveRatio` | **0.94** | ≈ 1.0 ✓ |
| mean difficulty | **1121** | ≈ anchor 1170 ✓ |

Difficulty orbited the anchor (oscillating 836–1436 under the heavy per-block variance, but **centered on 1170**), not ~40% above it. **No calibration error.** This **confirms the transient-overshoot explanation**: Test Run #1's ~1.4× was the un-converged feed-forward+feedback overshoot after the 2→10 step (only the climb was captured), **not** a bad anchor. Per-block variance reconfirmed (ratios 0.097→3.36 at constant power) — always judge by aggregates.

**Conclusion:** the hybrid regulator is **fundamentally sound** — no anchor factor, no accounting fix. Remaining work is optional polish for the *transient* on large power steps: **F1 (EMA on power)** to soften the step (and thus the overshoot), **F2 (asymmetric easing)** for quality. F3/F4 stay dropped/fallback.

> **Caveat — confirmed at power 2, not yet at power 10.** The `anchor = InitialDifficulty × power` relation is linear and the accounting audit holds regardless of pool split, so a power-10 calibration error is unlikely — but a single steady-state run at power ≈10 (let it settle ≥20 blocks past the step) would fully close it. Until then, treat F1/F2 as optional polish rather than fixes.

### Dev tooling — time acceleration 100X→9000X (2026-06-25) ✅

To run validation samples (e.g. the ≥30-block steady-state runs F5 needs) in a fraction of the wall-clock time **without altering the dynamics under measurement**.

- **Key correctness point**: bumping `SpeedMultiplier` alone is wrong — it speeds the clock but not bet execution, so in-game solvetime per block inflates with the factor, the regulator reads "blocks too slow", and `feedbackTrim` (clamped [0.5, 2]) can't compensate → difficulty collapses. The dynamics we measure would be destroyed.
- **Correct design**: an orthogonal `CalendarTimeService.DevTimeScale` (int) scales **both** by the same factor `k`: the calendar clock (`delta × SpeedMultiplier × k`) **and** bet execution (`SimulationService._Process`: `simDelta = delta × k` for player + bots). The power fed to the regulator (`HardwareRate`/`GetTotalActiveMiningPower`) is **deliberately not scaled**. ⇒ `attempts/in-game-second = (rate·k)/(100·k) = rate/100` is invariant → difficulty, power, in-game solvetimes and ratios are identical; only wall-clock compresses (`real_time/block = TargetBlockSeconds/(100·k)`).
- **UI**: `UI/DevTimeScaleSelector/DevTimeScaleSelector.cs` (programmatic, like StatusBar) — selector with **10 options: 100X, then 1000X..9000X in 1000X steps** (`DevTimeScale` multipliers 1, 10, 20 … 90), in DiceGame (next to the APS selector) and BlockExplorer (under the StatusBar). Live (next frame). **Not persisted** — resets to 100X on restart.
- **Caveat**: `MaxBetsPerFrame = 10`/node/frame caps throughput at `~600 bets/s/node`, so at very high scale × high single-node hardware the acceleration stops being linear (blocks no longer compress proportionally; the measured dynamics stay intact). The 10000X option was **removed** because it hit this ceiling and lagged; **9000X is the tested-smooth ceiling**. Irrelevant for the measurement regime (power split across nodes, each low-rate).
- **Files**: `CalendarTimeService.cs`, `SimulationService.cs`, `UI/DevTimeScaleSelector/DevTimeScaleSelector.cs`, `DiceGame.cs`, `BlockExplorer.cs`.

**Companion: "Discard Hardware (−1)" button** (Pools & Hardware screen, 2026-06-25 ✅) — the counterpart to the DEV "Buy Hardware". `HardwareAllocationRepository.RemoveCredits` drops a credit (casino pool first, then individual; floored at **1 total** so reported power stays consistent with `TotalCredits`). Enables dropping a node to a single private-pool credit and **power-decrease test runs** (needed to validate F2 asymmetric easing — the fast-relief-on-drop path — and the open power-≈10 calibration check). Built programmatically next to Buy Hardware (no .tscn edit), disabled at the 1-credit floor. Files: `HardwareAllocationRepository.cs`, `BTCPoolsAndHardwareShop.cs`.

### Required Test Runs (priority order)

What's left to measure before deciding whether F1/F2 are worth implementing. The regulator is already verified sound at steady-state power 2; these close the remaining gaps: the **up-step overshoot** (F1 decision), the **down-step relief** (F2 decision, never measured), and the **power-10 calibration** sanity (open caveat).

**Universal protocol (applies to every run):**
- **Clean the CSV** first — delete `user://logs/difficulty_trace.csv` (`…\app_userdata\GamblingMiner\logs\`) so the file starts fresh; the header is rewritten on the first live block.
- **One session, no restart** mid-run — an app restart rolls the world back to the last block but the CSV log does **not** revert (rows would desync from the chain).
- **Use the DEV time selector at 1000X or higher (up to 9000X)** so 25–30 blocks take a fraction of the wall-clock.
- **Judge by aggregates, never single blocks** — per-block variance is ≈ exponential (ratios seen 0.02→3.7 at constant power). Use ≥20–30 blocks per regime; `aggregate realized power = 100 · Σdifficulty / ΣsolveSec`, plus mean `solveRatio` and mean `difficulty` vs `anchor`.
- **Power** = Σ `TotalCredits` of player + running bots (shown as `configuredPower` per block). Raise it with **Buy Hardware**, lower it with **Discard Hardware (−1)** or by stopping bots. Keep the **player autobet running** the whole time (it drives the clock).

---

**Test 1 — Full up-step: climb → overshoot peak → settle (PRIORITY 1). ✅ DONE (2026-06-25) — power-10 calibration CONFIRMED; F1 NOT justified.**
- **Why**: Test Run #1 captured only the climb after a 2→10 step (difficulty was still rising at the last block). Quantify the **overshoot magnitude** and **settle time** to decide if F1 (EMA on power) is worth it — and, once settled, read the **steady-state-at-power-10** aggregate to confirm `anchor = InitialDifficulty × power` at the higher power.
- **Setup**: clean CSV → 1000X → start at LOW power (player only, ~1–2) and run ~5–8 blocks (baseline) → **step up**: enable all bots + casino pool (power ~10–15) → run **≥30 more blocks** (long enough to see difficulty peak and settle).
- **Result** (8 blocks at power 2, step at block 124, 38 blocks at power 10):
  - **Power-10 calibration CONFIRMED ✅** — settle tail (last 20 blocks): aggregate realized **9.60** ≈ 10, mean ratio **1.03** ≈ 1.0, mean difficulty **5793** ≈ anchor 5851 (99%). `anchor = InitialDifficulty × power` holds at power 10 → **open item closed**.
  - **Overshoot mild & variance-driven → F1 NOT justified.** Peak difficulty 6591 = **1.13×** anchor (not the 1.4×/8400 of Test Run #1), reached the anchor band in ~2 blocks. The difference vs Test Run #1 was an early long-tail **slow** block here (idx 126, ratio 7.52×) that damped `feedbackTrim`, where Test Run #1 chained fast blocks that inflated it — i.e. the overshoot is **transient noise, not systematic**. Against the criterion below (>1.3× **and** >10 blocks), this is **1.13× / ~2 blocks** ⇒ leave the regulator as-is; **F1 is cosmetic at best**. Revisit only if Test 2 (down-step) shows a problem.

**Test 2 — Power decrease (down-step) (PRIORITY 2). ✅ DONE (2026-06-25) — F2 NOT justified; regulator fully validated.**
- **Why**: validate F2 (asymmetric easing — fast relief on drops). On a power drop the anchor falls instantly but easing (α=0.7) and the `feedbackTrim` clamp `[0.5×,2×]` cap how fast difficulty can come down per block; meanwhile blocks run **slow** (difficulty too high for the new low power). Measure how many blocks the slow stall lasts.
- **Setup**: continued in the same session from Test 1's settled power-10 state; dropped **power 10 → 1** at block 162 (anchor 585); ran ~33 blocks at power 1.
- **Result**:
  - **Descent is fast & symmetric**: difficulty fell 5622 → 2002 → 899 → **565 ≈ anchor in 3 blocks** (163–165). Slow-stall (ratio > 1.3) lasted only ~3–4 blocks, then normal variance. Same speed as the up-step (symmetric α=0.7) — **no prolonged stall** ⇒ **F2 not justified.** The one genuinely slow block (162, ratio 3.87) is the 1-frame power-read-lag transition artifact (difficulty computed at power 10, mined at power 1) — a single block, unavoidable, and F1/EMA wouldn't fix it (would slow the anchor more).
  - **Power-1 calibration confirmed**: settled (skip 3 descent blocks, 30 blocks) realized **1.05–1.16** ≈ 1, mean ratio **0.87**, mean difficulty **~535–592** ≈ anchor 585.
- **Minor note**: at power 1 and 2 difficulty settles ~10% *below* anchor (ratio ~0.87); at power 10 it sits right on anchor. Small, within the noise floor, harmless direction — not actionable.

**Test 3 — Minimum-power sanity (PRIORITY 3, optional).**
- **Why**: confirm the floor regime is sane (single private-pool credit).
- **Setup**: Discard down to **1 credit, individual pool only** (0 casino) on the player; stop bots; 1000X; run ≥20 blocks.
- **Capture & decide**: aggregate realized ≈ 1, mean ratio ≈ 1.0, difficulty ≈ anchor (≈585). Confirms the low-power end and the `TotalCredits` floor behave.

> After each run, hand over the CSV — the aggregates get computed and the verdict (and any F1/F2 go/no-go) recorded here.

### Phase F1 — EMA on the power signal *(secondary; smooths the anchor on a step)*

- **What**: smooth power before it feeds the anchor, so it tracks *realized* throughput, not the instantaneous configured step; also damps the noisy 18.9/12.3 swings.
- **Where**: `NetworkRoot.SetActiveMiningPower()` (single chokepoint):
  `_activeMiningPower = _activeMiningPower <= 0 ? raw : _activeMiningPower + PowerEmaAlpha·(raw − _activeMiningPower);`
  with `PowerEmaAlpha ≈ 0.2–0.35`; bypass-to-`raw` on the first non-zero sample so it doesn't crawl up from 0.
- **Acceptance**: in the 1→11 step the anchor rises over 3–5 samples without leading throughput; the 2.43× block disappears.
- **Risk**: low. Trade-off: slightly slower reaction to *legitimate* power changes (mitigated by F2's asymmetry).

### Phase F2 — Asymmetric easing

- **What**: split `DifficultyEaseAlpha` into `EaseAlphaUp` (gentle, ≈0.4–0.5: a new miner must not punish newcomers) and `EaseAlphaDown` (fast, ≈0.8: relieve a stuck-too-hard chain quickly).
- **Where**: `BlockchainService.GetNextBlockDifficulty()` — `double alpha = target >= current ? EaseAlphaUp : EaseAlphaDown;`
- **Acceptance**: increases ramp over ~3–4 blocks; decreases resolve in ~1–2 (validate by simulating a miner leaving).
- **Risk**: low. Directly matches the design goal "don't delay the first nodes' mining."

### Phase F3 — Tame the startup throughput transient ⚪ DROPPED

> **Dropped after Test Run #1** — the "startup stall" was a bootstrap-data artifact; live realized throughput sits at/above configured (no stall). Kept for the record; revisit only if a real stall is ever measured. Sub-options below were never implemented.

Sub-options, least→most invasive; pick based on what F0 shows:

- **F3a — Desynchronize cadence**: seed each runner's `AccumulatorSeconds` with a small jitter (`Random·interval`) in `BuildBotRunner` so they don't all tick on the same frames.
- **F3b — Protect the backlog from a hitch**: raise `MaxBacklogSeconds` (e.g. 2→5) and/or clamp per-frame `delta`, so a single hitch doesn't discard attempts (fixes the permanent loss behind the ~3.3).
- **F3c — Soften bot cold-start**: avoid several bots cycling stop/recharge in the first interval (e.g. brief grace before applying stops right after start).
- **Acceptance**: realized power reaches configured within ≤1 block, no stall/overshoot (measured via F0).
- **Risk**: medium (touches the shared player+bot executor). Apply changes one at a time, each measured.

### Phase F4 — Palliative knob *(fallback / one-liner)*

- **What**: lower the default `DifficultyEaseAlpha` 0.7 → 0.5–0.6.
- **When**: only if F1–F3 are deferred and immediate relief is wanted. **Softens the easing ramp, not the throughput transient** (the real cause of block 118). Subsumed by `EaseAlphaUp` once F2 lands.
- **Risk**: minimal. Trade-off: slower convergence + a longer window of too-fast (under-difficulty) blocks during the transition.

### Phase F5 — Validation

- **What**: re-run a power step with the F0 trace; judge by **aggregates**, not single blocks (live per-block variance is ≈ exponential — see Test Run #1).
- **Acceptance**:
  - steady state (**≥30 blocks** at constant power): aggregate realized power (`100·Σdif / ΣsolveSec`) ≈ configured power within ~10%, and difficulty converges near the anchor (not ~40% above it as in Test Run #1);
  - window mean `solveRatio` ≈ 1.0 (vs ≈ 0.73 during the un-calibrated catch-up);
  - power drop: difficulty cedes in ≤2 blocks (verifies F2).
- Use ≥30 blocks because the aggregate is time-weighted and a few long-tail solvetimes can bias a small sample.

### Recommendation — FINAL (all tests done, 2026-06-25)

> ⚠️ **Superseded in scope by Round 2 (2026-07-27, below).** This verdict remains correct **for what it tested** — steady-state powers 1/2/10 and clean up/down steps, where requested power is always executable. Round 2 documents a failure mode outside that envelope: the regulator being handed a power figure the machine cannot hash, which no amount of correct regulation can fix. Read both before touching the regulator.

**No code changes to the regulator. Close this section.** Three steady-state regimes (power 1, 2, 10) confirmed difficulty settles at `anchor = InitialDifficulty × power` with ratio ≈ 1; the up-step (2→10) and down-step (10→1) both transition cleanly in ~2–3 blocks with only mild, variance-driven, **symmetric** overshoot/undershoot. The power-accounting audit found the count correct. Test Run #1's apparent ~1.4× anchor offset was an un-converged transient (unlucky variance), **not** a calibration error or accounting bug.

- **F1 (EMA on power)** — ⚪ not implemented; overshoot is mild (1.13×) and short (~2 blocks). Would only cosmetically smooth a noisy transient and would *slow* the anchor's legitimate response.
- **F2 (asymmetric easing)** — ⚪ not implemented; the down-step cedes in ~3 blocks (symmetric to the up-step), no prolonged slow stall.
- **F3 (startup stall)** — ⚪ dropped (artifact of bootstrap data).
- **F4 (lower α)** — ⚪ fallback only; not needed.

**Kept as permanent assets**: the F0 difficulty trace (`difficulty_trace.csv`) and the dev tools (time-accel 100X→9000X, Discard Hardware) — reuse them to re-validate if the regulator, calibration constants, or pool/hardware model change later. Always judge by aggregates over ≥20–30 blocks (per-block solvetime is ≈ exponential).

### File Checklist (this section)

| File | Change | Phase |
|---|---|---|
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | ✅ `AppendDifficultyTrace` (F0); anchor calibration factor + EMA in `SetActiveMiningPower` | F0 done · PRIMARY/F1 |
| `Scripts/Services/SimulationService.cs` | audit `GetTotalActiveMiningPower`/`GetActiveMiningRates` for casino-pool credit counting | PRIMARY |
| `Scripts/BlockchainPort/Blockchain/BlockchainService.cs` | `EaseAlphaUp`/`EaseAlphaDown` (F2); default `α` (F4, fallback) | F2/F4 |
| ~~`SimulationService` accumulator jitter / backlog clamp / cold-start grace~~ | ⚪ dropped (F3) | — |
| Block Explorer / log writer | difficulty trace surface | F0/F5 |

---

## Difficulty Regulator — Round 2: the un-executable power ceiling (2026-07-27) 📋 SPEC — awaiting the §R2.5 decisions

> **Why this section reopens a "closed" one.** The 2026-06-25 verdict — *"no code changes to the regulator, close this section"* — **still stands for what it tested**: three steady-state regimes (power 1, 2, 10) and clean up/down steps. Round 2 is a different failure, invisible at those powers: the regulator is *correct* and still produces 1.5-day blocks, because it is handed a power figure the simulation **cannot execute**. Everything below was diagnosed from the permanent assets that verdict kept — `difficulty_trace.csv`, `founders_trace.csv`, `network_population_trace.csv` — which is exactly what they were kept for.

### R2.0 — The report

Developer, mid-P15.8 playtest (in-game ~2011-04/05, chain blocks 947-964), mining with the **casino pool** and **2 hardware pieces on the player and on each of the four miner-bots**: *"the difficulty regulator isn't calibrating — blocks are averaging almost 2 days"*. Target is `TargetBlockSeconds = 58,500` s ≈ **0.68 in-game days**.

### R2.1 — It is NOT the casino pool

`network_population_trace.csv` carries `playerBotsPower = 10.000` on **every row** of the window — 5 nodes × 2 hardware credits, flat. The pool routes *where* attempts are credited, never *how many*, and `GetTotalActiveMiningPower` counts each node's `HardwareRate` exactly once (the comment there already says pool attempts are inside each node's rate; the 2026-06-25 power-accounting audit confirmed it). The correlation with "first run in weeks using the pool" is coincidental. **Ruled out by data, not by argument.**

### R2.2 — The trigger: Satoshi's end-game catch-up ramp saturating `MaxShare`

`founders_trace.csv`:

| block | satoshiPower | satoshiShare | satoshiBtc |
|---|---|---|---|
| 953 | 50.0 | 0.41 | 10,607 |
| 957 | 154.2 | 0.69 | 10,657 |
| 958 | 363.6 | 0.84 | 10,707 |
| **959** | **7,037.7** | **0.9900** | 10,757 |
| 962 | 144.6 | 0.67 | 10,907 |
| **964** | **0.0** | — | **11,007 → `satoshiRetired = 1`** |

He entered the **2011-04-26** floor date (`SatoshiEarliestDisappearance`) ~400 BTC short of `SatoshiTargetBtc = 11,000`, so the exponential ramp (`Growth = 1.15`/block) fired and pinned at **`MaxShare = 0.99`**. That constant is the share fed to `shareToWeight`, and `w = s/(1−s)` ⇒ **w = 99**: Satoshi alone at *99× the entire rest of the network*. The arithmetic checks out — `99 × (playerBots 10 + cast 62) ≈ 7,128` against the logged 7,037.7. The cap did exactly what it says; the cap is simply enormous.

**Note the positive feedback:** slow blocks ⇒ fewer blocks mined by the deadline ⇒ Satoshi further short ⇒ steeper ramp ⇒ slower blocks. The system was pushing itself further from the target it was chasing.

### R2.3 — The mechanism: the anchor prices a hashrate nobody can execute

`difficulty_trace.csv` (`configuredPower` = what the regulator was told, `realizedPower = difficulty × 100 / solveSec` = what the machine actually delivered):

| block | configuredPower | realizedPower | difficulty | solveRatio |
|---|---|---|---|---|
| 958 | 225.7 | 36.9 | 57,189 | 2.65× |
| 960 | 7,110.1 | 1,142.8 | 109,362 | 0.16× |
| 961 | 1,917.5 | 2,545.5 | **1,651,122** | 1.11× |
| 962 | 1,428.2 | 247.3 | 952,435 | **6.58×** |
| 963 | 216.6 | 143.2 | 578,218 | **6.90×** |
| 964 | 460.3 | 180.8 | 217,833 | 2.06× |

Window aggregates (18 blocks, the protocol's own yardstick): mean **solveRatio 2.23×** (≈1.5 in-game days, worst 4.7), aggregate realized power `100 × Σdifficulty / ΣsolveSec` = **173** against a mean configured of ~365. Three stacked facts:

1. **The anchor trusts configured power unconditionally.** `SetActiveMiningPower(playerBots + founders + scheduled)` flows straight into `anchor = InitialDifficulty × power`. Nothing asks whether those attempts can be performed.
2. **The attempt engine saturates; the clock does not.** `FoundersMiningService.AddDrained` is *uncapped* — `accumulator += nonFounderAttempts × (Power / otherMinersPower)`, i.e. **703 hashes per player bet** at the peak — while `CalendarTimeService._Process` advances game time every frame regardless of how much mining work actually got done. At block 960 the engine delivered **16%** of the demanded attempts, so game time ran ~6× ahead of the mining, and *that ratio is the inflated block interval*. `DevTimeScale` amplifies it: it multiplies demanded hashes per real second while the clock keeps its full speed. (Note `realizedPower` divides out `DevTimeScale` on both sides, so the configured-vs-realized comparison is valid at any time scale — the saturation is real, not a measurement artifact.)
3. **The overhang unwinds slowly, and that is what the developer is watching now.** `feedbackTrim` is clamped to `[0.5×, 2×]` with `DifficultyEaseAlpha = 0.7`, so from difficulty 217,833 against a post-retirement anchor of ~51,900 (`585 × 88.8`) it needs ~4-5 blocks to converge — each itself slow. **Satoshi retired at block 964: the trigger is already gone and only the tail remains.**

**The generalizable statement:** the regulator's feed-forward is an open-loop bet that requested power equals delivered power. That held for every regime F0-F5 tested (powers 1-10, all executable). It fails silently the moment any participant's power exceeds what the frame budget can hash — and nothing in the system notices or reports it.

### R2.4 — The four options

- **R2-A — Bound the founders' power to something executable.** Lower `MaxShare` (0.99 ⇒ w=99; **0.90 ⇒ w=9**, 0.75 ⇒ w=3) and/or add an absolute "no single miner exceeds N× the rest of the network" clamp. Smallest change, directly removes the 99× spike. Risk: a lower ceiling makes Satoshi's historical 11,000-BTC target harder to reach if the chain is behind schedule — he may retire late or short, which is a **canon** question (`SatoshiTargetBtc` is called "a HISTORICAL requirement, not a tunable"), so the acceptable failure mode needs stating.
- **R2-B — Anchor on delivered power, not requested power.** Feed the regulator `min(configured, k × recentRealized)` (or clamp per-block anchor growth). This is the **general** cure: it makes the whole class of "a power figure nobody can execute" harmless, whatever produces it — founders, a future hardware tier, a pool, ND.2's invisible mass. Risk: realized power is a lagging, noisy signal; a naive `min` could suppress a *legitimate* power step (exactly what F1's EMA was rejected for). Needs an asymmetry — trust configured on the way up only as fast as realization confirms it.
- **R2-C — Let the clock know mining is behind.** If the attempt engine saturates, game time should not keep running. This is the deepest fix and the only one that makes the simulation honest under CPU pressure; it converts a *performance* limit into an honest slowdown rather than a *simulation* artifact. Risk: touches `CalendarTimeService`, the one service everything else derives from; a feedback path from mining into the clock could interact with `DevTimeScale`, the pause-for-board-vote path, and checkpoint timestamps. Highest reward, highest blast radius.
- **R2-D — Asymmetric feedback (F2, revived).** Allow the trim to fall faster than it rises (`EaseAlphaDown > EaseAlphaUp`, and/or widen the clamp's lower bound below 0.5×). F2 was dropped in 2026-06 because down-steps ceded in ~3 blocks — but that was a 10→1 step, not a 30× overhang. This does not prevent the spike; it shortens the tail. **Cheap, additive, and useful regardless of which of A/B/C ships.**

**Recommended order: R2-A + R2-D now (bound the cause, shorten the tail), R2-B next (the general guard), R2-C only if it recurs from a source A doesn't cover.** A and D are small, independently testable, and together would have reduced this incident to a couple of mildly slow blocks.

### R2.5 — Questions & suggestions

1. **[Decision needed] Which options, in which order?** Recommending **A + D now, B next, C deferred**. A alone leaves the general hole open; B alone leaves a 99× founder spike legal (just re-priced); C alone fixes everything but is the riskiest single edit in the codebase.
2. **[Canon question, blocks R2-A] What is allowed to give when Satoshi cannot reach 11,000 BTC by 2011-04-26 at a bounded power?** Three honest answers: (a) he retires **late** — keep ramping at a bounded rate until the target is met, date slips; (b) he retires **short** — the date is canon, the number is not; (c) the target scales with how many blocks the chain actually produced. Today the code implicitly chooses "whatever power it takes", which is why this happened. The step7 plan calls the ~10% share "a HISTORICAL requirement, not a tunable" — so this needs the developer's call, not mine.
3. **Suggestion — a saturation telemetry column, before any fix.** Add `demandedAttempts` / `deliveredAttempts` per block to `difficulty_trace.csv` (or a plain `saturated` flag). Right now saturation is *inferred* from `configured` vs `realized`; measuring it directly makes R2-B's signal available and would let the next occurrence be diagnosed in one glance. This is the F0 move again — **instrument before changing logic** — and it is the honest prerequisite for B and C.
4. **Suggestion — assert the executable-power invariant.** Same reflex as ND.10i's slope assertion and P15.9's clamp tripwire: `GD.PrintErr` once when `configuredPower` exceeds delivered throughput by more than ~2× for N consecutive blocks. The regulator has now been "verified correct" twice while producing wrong block times for reasons outside itself; a standing alarm is what turns that into a five-minute diagnosis.
5. **Observation — `Growth = 1.15`/block compounds 50 → 7,037 in six blocks.** Even under a lower `MaxShare`, that slope alone can disturb pacing for a few blocks. Worth considering a per-block ceiling on the *ramp* (not just the terminal share) if A proves insufficient.
6. **Observation — the ND.2 scheduler was NOT a contributor here** (`invisiblePower = 0.000`, cast 11 × 5.6 ≈ 62 of a 7,110 total), but it carries the same latent shape: `MaxScheduledAttemptsPerFrame = 5000` is a per-FRAME cap that does not scale with `DevTimeScale`, and its own comment already concedes "a sustained shortfall just slows blocks slightly, which the LWMA feedback then trims". With the trim clamped at 0.5×, that self-correction has a hard floor. R2-B would cover this case too — a second reason to prefer the general guard.

### R2.3a — REFINEMENT (2026-07-27, found while specifying the build): the saturation has an exact location

R2.3 said "the attempt engine saturates" and left the ceiling as a vague CPU limit. It is not vague. `SimulationService.Tick`:

```csharp
double simDelta = Math.max(0, delta) * max(1, DevTimeScale);              // sim-seconds this frame
_accumulatorSeconds = Math.Min(_accumulatorSeconds + simDelta, MaxBacklogSeconds);   // ← 2.0
while (_accumulatorSeconds >= interval && executed < MaxBetsPerFrame …)   // ← 10
```

With `MaxBacklogSeconds = 2.0`, `MaxBetsPerFrame = 10` and the player's `interval = 1/HardwareRate = 0.5 s` (2 credits), **the bet engine can consume at most `min(2.0, 10 × 0.5) = 2.0 sim-seconds per frame — ever.** Everything beyond that is silently discarded by the `Math.Min`. Meanwhile `CalendarTimeService._Process` advances the clock by the **full** `delta × SpeedMultiplier × DevTimeScale`, unthrottled.

So the drop is a pure function of frame time. At `DevTimeScale = 90`:

| fps | simDelta/frame | consumed | sim-time DISCARDED |
|---|---|---|---|
| 60 | 1.5 s | 1.5 s | 0% |
| 45 | 2.0 s | 2.0 s | 0% (the knee) |
| 30 | 3.0 s | 2.0 s | 33% |
| 10 | 9.0 s | 2.0 s | 78% |
| 3 | 30 s | 2.0 s | **93%** |

And it is **self-reinforcing**: Satoshi at 703 hashes per player bet tanks the frame rate, which enlarges `simDelta`, which discards a larger fraction, while the clock keeps its full stride. Because founder/scheduled attempts are drained *per executed bet*, every discarded bet removes its whole entourage of attempts too — so total delivered power falls in exact proportion, which is precisely the `configured / realized` ratio the trace shows. The block-962 figure (5.8×) corresponds to ~29 sim-seconds offered against 2.0 consumed, i.e. roughly 3 fps — consistent with a frame hashing hundreds of thousands of times.

**Two consequences for the plan.** (1) The `SimulationService` comment claiming *"attempts-per-IN-GAME-second stay invariant; only wall-clock time compresses"* is true **only below the knee** — it silently stops holding the moment `simDelta > 2.0 s`, i.e. below ~45 fps at 90×. That invariant is load-bearing for every measurement in this document. (2) It promotes **R2-C** from "vaguest and riskiest" to *the actual root fix*, and shrinks it: the cure is not a new feedback path from mining into the clock, it is **advancing the clock by the sim-time the engine actually consumed** instead of the raw delta. That is a small, local change — but it changes core time semantics, so it is specced below as **R2-C1** and left for the developer's explicit decision rather than folded into the agreed scope.

### R2.6 — Verification protocol

Reuse the existing universal protocol (clean the CSV, one session, no restart, judge by ≥20-30-block aggregates — per-block solvetime is ≈ exponential). The specific run this needs: **play through a Satoshi-ramp window** (or force one by putting him behind target) and confirm (1) `configuredPower` never exceeds a few × `realizedPower`, (2) mean `solveRatio` over the window stays within ~1.0 ± 0.3, (3) Satoshi still reaches the canon outcome chosen in question 2, and (4) the post-ramp overhang clears in ≤2 blocks with R2-D in.

### R2.7 — IMPLEMENTATION PHASE (✅ BUILT 2026-07-27 — in-game verification pending)

> **Build log.** `dotnet build` clean, 0 warnings. **All four pieces shipped, including R2-C1** — the
> developer resolved D-R2.5 with "ship it now". Files: `SimulationService.cs` (retention measurement + the
> throttle + the R2-T push), `CalendarTimeService.cs` (`SimulationThrottle` applied to the clock),
> `FoundersMiningService.cs` (`MaxShare` 0.99 → 0.90), `BlockchainService.cs` (`MinFeedbackTrim` 0.25 +
> `DifficultyEaseAlphaDown` 0.9), `NetworkRoot.cs` (two trace columns, `AccumulateSimSaturation`, the alarm).
> No persisted state, no `WorldFormatVersion` bump, no UI.
>
> **One implementation refinement worth recording: the retained fraction is measured, not estimated.** The
> spec said "consumed = executed × interval", which would have under-counted — the accumulator's leftover
> remainder is *carried*, not lost, and executes next frame. Only the `Math.Min(…, MaxBacklogSeconds)` clamp
> destroys simulated time. So the code measures exactly that (`offered − dropped`), power-weighted across the
> player and every running bot since each keeps its own accumulator. Consequence: below the saturation knee
> the throttle is **exactly 1.0** and the clock behaves byte-for-byte as before — the change is inert until
> the engine actually drops work.
>
> **`MaxStepDown` (0.5) is now unread** — `MinFeedbackTrim` (0.25) replaced it in the clamp. Kept as the
> documented historical value with a comment pointing at this section, rather than deleted, since it is the
> symmetric partner of `MaxStepUp` in the anti-oscillation vocabulary.
>
> **Left for the developer's verification run:** §R2.6's protocol, plus the two R2-C1-specific checks in
> "Build order & verification" below.

#### Decisions log

- **D-R2.1 (canon, answers §R2.5 q2) — Satoshi retires SHORT.** If he cannot reach `SatoshiTargetBtc = 11,000`
  by `SatoshiEarliestDisappearance` (2011-04-26) **at a bounded power**, the **date is canon and the number is
  a target**: he retires on schedule with whatever he actually accumulated. This is what makes R2-A safe to
  ship — under the old "whatever power it takes" reading, any ceiling would have been a contradiction. It also
  removes the positive feedback loop at its source: a Satoshi who is allowed to fall short has no reason to
  escalate without limit, so slow blocks can no longer beget slower ones.
- **D-R2.2 (scope, q1) — ship R2-A + R2-D now; R2-B next; R2-C deferred pending D-R2.5.**
- **D-R2.3 (q3) — the saturation telemetry ships FIRST**, before any logic change. F0 discipline: this
  section's whole history is "measure, then decide", and the one number nobody has ever logged is the one that
  would have made this a five-minute diagnosis.
- **D-R2.4 (q4) — the executable-power alarm ships with it.** The regulator has now been declared correct
  twice while producing wrong block times for reasons outside itself.
- **D-R2.5 (RESOLVED 2026-07-27) — R2-C1 SHIPS NOW.** The developer took the recommendation immediately. So
  the root fix lands with the palliatives rather than after them, and the discard mechanism is closed for
  every future power source, not just for the founder ramp that exposed it.

#### R2-T — Saturation telemetry (build FIRST, no behaviour change)

- **`SimulationService.Tick`**: alongside the existing loop compute `simTimeConsumed = executed × interval`,
  and accumulate two counters since the last block: `_simSecondsOffered += simDelta`,
  `_simSecondsConsumed += simTimeConsumed`. Bots carry their own intervals — sum them the same way in
  `TickBots` so the figure covers the whole bet engine, not only the player.
- **Expose** `LastBlockSimTimeOfferedSeconds` / `…ConsumedSeconds`, reset by the same block-boundary hook that
  already drives `RecomputeFoundersOnNewBlock`.
- **`NetworkRoot.AppendDifficultyTrace`**: two new columns, `simSecOffered,simSecConsumed`. A header change is
  free here — the protocol already says to delete `difficulty_trace.csv` before a run.
- **Acceptance**: at 100X on a quiet network the two columns match within a few percent; force a heavy frame
  (high `DevTimeScale` + a founder ramp) and `consumed/offered` must fall visibly below 1 **and track
  `realizedPower / configuredPower`**. If it does, §R2.3a is confirmed by measurement rather than derivation,
  and R2-B has its input signal.

#### R2-A — Bound the founders' power (the trigger)

- **`FoundersMiningService`**: `MaxShare` **0.99 → 0.90**. It feeds `shareToWeight` where `w = s/(1−s)`, so
  this is the difference between a **99×** and a **9×** multiplier over the rest of the network — the single
  constant that produced the 7,037 spike.
- Keep the exponential ramp (`Growth = 1.15`) as-is: against a bounded ceiling it now *approaches* the cap
  instead of running away, and under D-R2.1 falling short is a legal outcome. §R2.5 q5's per-block ramp
  ceiling stays a **contingency**, for use only if the playtest shows 0.90 is still too steep.
- **Acceptance**: across a full ramp window `satoshiShare` tops out at 0.90, `satoshiPower` never exceeds
  `9 × (playerBots + scheduled)`, and `configuredPower` peaks near a tenth of the 7,110 seen here.
- **Watch — D-R2.1's visible consequence**: Satoshi may now retire with **less than 11,000 BTC**. Record the
  final figure from `founders_trace.csv`; it is the number that says whether 0.90 is the right ceiling or the
  historical shape needs a different lever.

#### R2-D — Asymmetric feedback (shortens the tail; F2 revived)

- **`BlockchainService.GetNextBlockDifficulty`**: split the trim clamp and the easing by direction.
  `MinFeedbackTrim` **0.5 → 0.25** (a 4× down-trim per block); `MaxFeedbackTrim` stays **2.0**;
  `DifficultyEaseAlphaDown = 0.9` when `target < current`, `DifficultyEaseAlpha = 0.7` unchanged upward.
- **Why asymmetry is the safe direction**: an overhang makes blocks *slow* (annoying, self-correcting); an
  under-shoot makes blocks *flood* (breaks pacing, mints coins early). Ceding fast and rising slow errs the
  right way. F2 was dropped in 2026-06 because a 10→1 step cleared in ~3 blocks — that was a 10× step, not the
  **30×** overhang measured here.
- **Acceptance (arithmetic, checkable on paper before the run)**: from the real post-retirement state
  (difficulty 217,833, anchor 51,946) the unwind becomes `217,833 → 33,471 → 15,034` — **≤2 blocks**, against
  4-5 today.

#### R2-ASSERT — The executable-power alarm (D-R2.4)

- One `GD.PrintErr` when `configuredPower > 2 × realizedPower` for **3 consecutive** blocks — evaluated in
  `AppendDifficultyTrace`, which already holds both figures — naming both, the ratio and the block index.
- The consecutive gate matters: single-block solvetimes are ≈exponentially distributed, so a one-block ratio
  means nothing. This document's own protocol says judge by aggregates; the alarm must obey its own rule.
- Third instance of the same reflex in one week (ND.10i's slope assertion, P15.9's clamp tripwire):
  **an invariant that lives only in prose is an invariant nobody checks.**

#### R2-C1 — Clock/engine coupling (✅ SHIPPED — D-R2.5 resolved "now")

- **The change**: advance the calendar by the sim-time the bet engine actually **consumed**, not by the raw
  `delta × SpeedMultiplier × DevTimeScale`. The discard site is a single `Math.Min` (§R2.3a); the cure is to
  stop the clock spending time the engine could not.
- **Why it is the real fix**: R2-A bounds *one* source of excess demand and R2-D shortens the tail, but the
  discard mechanism survives both — any future power source (a hardware tier, a pool, ND.2's invisible mass at
  scale) reopens it, and the failure is silent by construction. It also restores the `SimulationService`
  invariant that every measurement in this document depends on.
- **Why it is not in scope by default**: it changes core time semantics. `CalendarTimeService` is the service
  everything else derives from — checkpoint timestamps, the game-time rule (Pattern 2), `DevTimeScale`, the
  board-vote pause. A bug here is a bug in *everything*, and it is the one change in this section that cannot
  be validated by the difficulty trace alone.
- **If it ships**: gate it behind R2-T (build the telemetry, confirm `consumed/offered` reproduces the ratio,
  *then* touch the clock), and verify the §24.9 rule — "the clock always exactly equals the timestamp of the
  block that most recently defines the checkpointed world" — still holds bit-for-bit across a restart.
- **The honest alternative if it does not ship**: raise `MaxBacklogSeconds` / `MaxBetsPerFrame` so the knee
  sits far above any realistic frame time. A palliative — it moves the cliff instead of removing it — but a
  two-constant change with no semantic risk; at `DevTimeScale = 90` a ~30 s backlog would cover down to 3 fps.

#### Build order & verification

1. ✅ **R2-T** (telemetry) — `simSecOffered,simSecConsumed` in `difficulty_trace.csv`.
2. ✅ **R2-A** + **R2-D** + **R2-ASSERT** + **R2-C1**.
3. ⏳ Full §R2.6 protocol run: clean CSV, one session, ≥20-30 blocks through a Satoshi-ramp window.
4. ⚪ **R2-B** afterwards *if still needed* — with R2-C1 in, the clock can no longer outrun the engine, so the
   "anchor on delivered power" guard may now be redundant. **Decide it from the run's data, not in advance:**
   if `configuredPower / realizedPower` stays near 1 with `simSecConsumed / simSecOffered` near 1, R2-B has
   nothing left to fix and should be dropped rather than built for symmetry.

**R2-C1-specific checks for the run** (beyond §R2.6):

- **The clock must be inert below the knee.** At 100X on a quiet network, `simSecConsumed ≈ simSecOffered`
  and the calendar advances exactly as before. If in-game time is now visibly slower at low `DevTimeScale`,
  the throttle is firing when it should not.
- **Above the knee, wall-clock slows instead of block-time stretching.** At a high `DevTimeScale` with a heavy
  network, expect the game to advance *more slowly in real time* than it used to — that is the fix working.
  `solveRatio` should stay near 1 while `simSecConsumed/simSecOffered` dips below it.
- **§24.9's exact-clock rule still holds.** Mine a block, restart, and confirm the calendar lands exactly on
  the last block's timestamp (`BlockSessionCheckpointService`). The throttle scales the clock's *rate*, never
  its checkpointed value, so this should be untouched — but it is the one invariant a clock change could
  break silently, and it is cheap to check.

**Files**: `SimulationService.cs` (R2-T counters), `FoundersMiningService.cs` (`MaxShare`),
`BlockchainService.cs` (trim clamp + directional easing), `NetworkRoot.cs` (two trace columns + the alarm).
No persisted state, no `WorldFormatVersion` bump, no UI. `difficulty_trace.csv` gains two columns — delete it
before the run.
