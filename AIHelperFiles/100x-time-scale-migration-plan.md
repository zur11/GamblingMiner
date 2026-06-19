# 100X Time Scale Migration Plan

**Status**: Phase 0 ✓ — Next: Phase 1 (Time Scale Constants)  
**Goal**: Migrate the game from 48X to 100X time scale, align block economics to a **210,000 BTC total supply** converging to year ~2140 with halvings every ~4 in-game years, keeping the familiar 50 BTC initial block reward.

---

## Conceptual Model

| Term | Meaning |
|---|---|
| **48X** | 1 real second = 48 in-game seconds |
| **100X** | 1 real second = 100 in-game seconds |
| **Bet tick** | One in-game time unit = 1 bet = 1 nonce attempt |

The multiplier describes how fast in-game time flows relative to real time during autobet. A higher multiplier means the player experiences more Bitcoin history per real-world hour of play.

---

## Pre-Migration Audit — Current State (48X)

### Code constants found

| File | Constant / Value | Current | Notes |
|---|---|---|---|
| `Screens/DiceGame/DiceGame.cs:68` | `GameSecondsPerRealSecond` | `48.0d` | Autobet SpeedMultiplier |
| `Screens/DiceGame/DiceGame.cs:69` | `GameSecondsPerManualBet` | `48.0d` | Manual bet AdvanceSeconds call |
| `Screens/BetsHistoryExplorer/BetsHistoryExplorer.cs:334` | `GameBaseSpeed` | `48.0` | Speed label divisor |
| `Screens/BetsHistoryExplorer/BetsHistoryExplorer.cs:32` | `_speedSteps` | `{ 48d, 96d, 192d, 480d }` | 1x / 2x / 4x / 10x playback |
| `Screens/CalendarsNavigator/CalendarsNavigator.cs:219` | `AddSpeedOption("x1", ...)` | `48.0` | First option in speed dropdown |
| `Screens/CalendarsNavigator/CalendarsNavigator.cs:96` | Fallback speed | `48d` | Used if selector is null |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs:24` | `GenesisRewardBtc` | `50m` | Initial coinbase reward |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs:25` | `HalvingIntervalBlocks` | `210000` | Real Bitcoin value (see inconsistency below) |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs:257` | Halving cap | `>= 32` | Return 0 after 32 completed halvings |

### Documentation references found

| File | Content |
|---|---|
| `CLAUDE.md` | "1 bet tick = 48 in-game seconds; autobet target: 10 real minutes = 8 in-game hours" |
| `CLAUDE.md` | "Coinbase reward: starts at 50 BTC, halves every **4,381 blocks**" |
| `CLAUDE.md` | Canonical table: "Basic Mode halving: `4,381 blocks`" |
| `README.md:15` | "Current scale: 1 bet tick = 48 in-game seconds" |
| `README.md:53` | "Current time scale: 1 bet tick = 48 in-game seconds" |
| `README.md:55` | "Basic Mode halving interval: `4,381 blocks`" |
| `Documentation/DESIGN_OVERVIEW.md:18-19` | "48 in-game seconds", "10 real minutes = 8 in-game hours" |
| `Documentation/DESIGN_OVERVIEW.md:89` | "4,381 blocks" |
| `Documentation/PLAYER_GUIDE.md:44-45` | "10 real minutes = 8 in-game hours", "48 in-game seconds" |
| `Documentation/PLAYER_GUIDE.md:77` | "4,381 blocks" |
| `Documentation/PRIVATE_ROADMAP.md:22` | "4,381 blocks, ~three blocks per in-game day" |
| `Documentation/PRIVATE_ROADMAP.md:70` | "4,381 blocks" (canonical decisions) |
| `Documentation/PRIVATE_ROADMAP.md:261` | Completed checklist: "4,381 blocks" |
| `Documentation/GLOSSARY.md:14` | "Halving: Basic Mode uses a scaled interval of 4,381 blocks" |

### Critical inconsistency detected (must fix in Phase 2)

The design docs say `4,381 blocks` per halving. The code (`NetworkRoot.cs`) uses `210,000` blocks (real Bitcoin). These were never reconciled. As a side-effect:

- Docs imply total supply: `50 × 4,381 × 2 = 438,100 BTC` — wrong halving interval, no intended total
- Code actually computes: `50 × 210,000 × 2 = 21,000,000 BTC` — real Bitcoin total, but wrong halving interval for the game

This migration is the opportunity to fix both: set a halving interval derived from the 100X scale that (a) keeps halvings every ~4 in-game years and (b) produces exactly **210,000 BTC** total supply — keeping the familiar 50 BTC initial reward, no code change to `GenesisRewardBtc` required.

---

## Target State (100X)

### Time scale math

```
1 real second  = 100 in-game seconds
1 real minute  = 100 in-game minutes
10 real minutes = 1,000 in-game minutes = 16 in-game hours 40 in-game minutes
```

### Block difficulty (unchanged)

The difficulty target (`DifficultyPrefix = "00"`, `DifficultyNextHexMaxInclusive = '6'`) is not changing.

```
P(success per attempt) = P("00" prefix) × P(next char ≤ '6')
                       = (1/256) × (7/16)
                       = 7/4096

Expected attempts per block = 4096 / 7 ≈ 585 nonce attempts
```

### Derived block rhythm at 100X

```
In-game seconds per bet      = 100
In-game seconds per block    = 585 × 100 = 58,500
In-game hours per block      = 58,500 / 3,600 ≈ 16.25 in-game hours
In-game days per block       = 58,500 / 86,400 ≈ 0.677
Blocks per in-game day       ≈ 1.477
```

### Block economics — target values

| Parameter | 48X (current code) | 100X (target) | Rationale |
|---|---|---|---|
| `GenesisRewardBtc` | 50m | **50m** *(unchanged)* | 50 BTC × 2,100 × 2 = 210,000 BTC total |
| `HalvingIntervalBlocks` | 210,000 | **2,100** | ~4 in-game years at 100X (see math below) |
| Halving cap | `>= 32` | **`>= 34`** | Rewards reach ~0 at in-game year ~2141 |
| Total BTC supply | 21,000,000 | **210,000** | Intentional game-world supply (1% of real BTC) |

### Halving interval math

```
4 in-game years     = 4 × 365.25 days = 1,461 in-game days
Expected blocks     = 1,461 × 1.477  ≈ 2,158 blocks

→ Round to clean number: 2,100 blocks per halving

Actual halving period = 2,100 / 1.477 = 1,422 days = 3.89 in-game years ≈ 4 years ✓
```

### Total supply verification

```
Total BTC = GenesisRewardBtc × HalvingIntervalBlocks × 2
          = 50 × 2,100 × 2
          = 210,000 BTC ✓
```

### Halving cap and year ~2140

```
With cap = 34: rewards active for halvings 0–33
Last reward block = 34 × 2,100 = 71,400 blocks
Duration = 71,400 / 1.477 blocks/day / 365.25 days/year ≈ 132 in-game years
In-game year at cutoff: 2009 + 132 = 2141 ≈ 2140 ✓
```

### Reward sequence (first 6 halvings)

| Era | Blocks | Reward | In-game year |
|---|---|---|---|
| 0 | 1 – 2,100 | 50 BTC | 2009 – 2013 |
| 1 | 2,101 – 4,200 | 25 BTC | 2013 – 2017 |
| 2 | 4,201 – 6,300 | 12.5 BTC | 2017 – 2021 |
| 3 | 6,301 – 8,400 | 6.25 BTC | 2021 – 2025 |
| 4 | 8,401 – 10,500 | 3.125 BTC | 2025 – 2029 |
| 5 | 10,501 – 12,600 | 1.5625 BTC | 2029 – 2033 |

The curve is identical to real Bitcoin — only the halving interval is compressed.

### Autobet speed comparison

| | 48X (current) | 100X (target) |
|---|---|---|
| In-game time per manual bet | 48 seconds | **100 seconds** |
| Autobet SpeedMultiplier | 48 | **100** |
| 10 real minutes of autobet | 8 in-game hours | **16 in-game hours 40 min** |
| In-game days per real hour of autobet | 2 | **4.17** |
| In-game years per real day of autobet | ~0.13 | **~0.27** |
| Real days per in-game year | ~7.6 | **~3.65** |
| Expected blocks per real hour of autobet | ~6 | **~12.5** |

### Time playback speed steps (BetsHistoryExplorer)

These are the `SpeedMultiplier` values used when the player replays history. They represent in-game seconds per real second. The `d` suffix in the C# source (`48d`, `100d`) is just the `double` type annotation — it has no unit meaning.

| Playback label | 48X SpeedMultiplier | 100X SpeedMultiplier |
|---|---|---|
| x1 (normal) | 48 | **100** |
| x2 | 96 | **200** |
| x4 | 192 | **400** |
| x10 | 480 | **1000** |

At x1 the history replay runs at the same pace as live autobet. At x10 the clock moves 10× faster than autobet.

---

## Save Data Compatibility

Because `GenesisRewardBtc` stays at 50m, **existing blocks already have the correct coinbase reward value**. The only save-breaking change is `HalvingIntervalBlocks`: the threshold shifts from 210,000 to 2,100.

- Players with fewer than 2,100 blocks mined: **no impact** — they are still in era 0 under both thresholds
- Players with more than 2,100 blocks: their chain would immediately show a halving that wasn't there before

In practice, reaching 2,100 blocks requires ~2,100 × 585 ≈ 1.2 million bets. This is extremely unlikely in any existing save during early development, so the break is mostly theoretical. Still document it as a known break and recommend a fresh save if the player encounters reward inconsistencies.

---

## Phases

### Phase 0 — Audit ✓ DONE

This document is the audit. All 48X constants and documentation references are catalogued above.

---

### Phase 1 — Time Scale Code Constants

**Touch 3 files. Zero logic changes — only constant values.**

#### Task 1.1 — DiceGame.cs

**File**: `Screens/DiceGame/DiceGame.cs`

Change both constants at lines 68–69:

```csharp
// Before
private const double GameSecondsPerRealSecond = 48.0d; // 10 real min -> 8 game hours
private const double GameSecondsPerManualBet = 48.0d;  // 1 manual bet tick

// After
private const double GameSecondsPerRealSecond = 100.0d; // 10 real min -> 16h 40m game time
private const double GameSecondsPerManualBet = 100.0d;  // 1 manual bet tick
```

The formula at line 739 (`timePerBet = GameSecondsPerManualBet / Math.Max(1, attempts)`) automatically inherits the new value. No other logic change needed.

#### Task 1.2 — BetsHistoryExplorer.cs

**File**: `Screens/BetsHistoryExplorer/BetsHistoryExplorer.cs`

Change `GameBaseSpeed` constant and the `_speedSteps` array:

```csharp
// Before
private readonly double[] _speedSteps = { 48d, 96d, 192d, 480d };
private const double GameBaseSpeed = 48.0;

// After
private readonly double[] _speedSteps = { 100d, 200d, 400d, 1000d };
private const double GameBaseSpeed = 100.0;
```

#### Task 1.3 — CalendarsNavigator.cs

**File**: `Screens/CalendarsNavigator/CalendarsNavigator.cs`

Change the x1 speed option value and the hardcoded fallback:

```csharp
// Line ~219: speed option
AddSpeedOption("x1", 100.0);   // was 48.0

// Line ~96: fallback when selector is null
? _timeSpeedSelector.GetItemMetadata(0).AsDouble() : 100d;   // was 48d
```

---

### Phase 2 — Block Economics Code

**Touch 1 file. Two constant changes only — `GenesisRewardBtc` stays at 50m.**

#### Task 2.1 — NetworkRoot.cs

**File**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`

`GenesisRewardBtc` does **not** change — 50 BTC is already correct. Only `HalvingIntervalBlocks` and the halving cap change:

```csharp
// Before
private const int HalvingIntervalBlocks = 210000;

// After
private const int HalvingIntervalBlocks = 2100;
```

And the halving cap in `GetBlockRewardForNextCandidate()` at line ~257:

```csharp
// Before
if (completedHalvings >= 32)

// After
if (completedHalvings >= 34)
```

**Validation after change**: Confirm `GetExpectedAttemptsForCurrentDifficulty()` still returns ~585 (no change there). Confirm the reward table for blocks 1, 2101, 4201 matches the target table above (50 BTC, 25 BTC, 12.5 BTC).

---

### Phase 3 — CLAUDE.md Canonical Values

**File**: `CLAUDE.md`

Update the following sections:

**Time section** (under `### Time`):
```
Game-time scale: 1 bet tick = 100 in-game seconds; autobet target: 10 real minutes = 16h 40m in-game
```

**Core game systems section** (under `### Blockchain / Mining System`):
```
Coinbase reward: starts at 50 BTC, halves every 2,100 blocks (≈ 4 in-game years); total supply 210,000 BTC
```

**Canonical Decisions table**:

| Decision | Old Value | New Value |
|---|---|---|
| Current mining rule | `1 bet = 1 nonce attempt` | unchanged |
| Basic Mode halving | `4,381 blocks` | **`2,100 blocks`** |
| Initial block reward | `50 BTC` | **`50 BTC`** *(unchanged)* |
| Halving period (in-game) | `≈ 4 in-game years` | unchanged |
| Total BTC supply | (not stated) | **`210,000 BTC`** |

Update the inline comment on the `CalendarTimeService` description:
```
Game-time scale: 1 bet tick = 100 in-game seconds; autobet target: 10 real minutes = 16h 40m in-game
```

---

### Phase 4 — Documentation Files

Update all player-facing and design documentation.

#### Task 4.1 — README.md

Lines 15, 53, 55:
```markdown
| Time progression | Implemented | Bets advance game time. Current scale: 1 bet tick = 100 in-game seconds. |
```
```markdown
- Current time scale: 1 bet tick = 100 in-game seconds.
- Autobet target: 10 real minutes = 16 in-game hours 40 in-game minutes.
- Basic Mode halving interval: `2,100 blocks` (≈ 4 in-game years; ~1.47 blocks per in-game day).
```

Remove or update the paragraph explaining `4,381`:
```markdown
The `2,100` block halving interval is intentional. At roughly 1.5 blocks per in-game day, it represents about four in-game years. The game uses 100X time scaling: 1 real second = 100 in-game seconds. This is not Bitcoin's real `210,000` block interval, which is used only as a reference.
```

#### Task 4.2 — Documentation/DESIGN_OVERVIEW.md

Section on time (lines 18–19):
```markdown
- Current tick scale: `100 in-game seconds`.
- Autobet target: `10 real minutes = 16 in-game hours 40 minutes`.
```

Section on halving (line 89):
```markdown
- `2,100 blocks` (≈ 4 in-game years at 100X scale).
```

#### Task 4.3 — Documentation/PLAYER_GUIDE.md

Lines 44–45:
```markdown
- 10 real minutes = 16 in-game hours 40 in-game minutes.
- 1 auto-bet tick = 100 in-game seconds.
```

Line 77:
```markdown
Basic Mode uses a scaled halving interval of `2,100 blocks` (≈ 4 in-game years), not Bitcoin's real `210,000` blocks.
```

Initial reward description (add/update near the halving line):
```markdown
The initial block reward is 50 BTC, halving every 2,100 blocks. Total supply converges to 210,000 BTC by approximately in-game year 2141.
```

#### Task 4.4 — Documentation/PRIVATE_ROADMAP.md

Line 22:
```markdown
Basic Mode halving interval: `2,100 blocks`, intentionally scaled to about four in-game years at roughly 1.5 blocks per in-game day (100X time scale).
```

Line 70 (canonical decisions):
```markdown
- Basic Mode halving: `2,100 blocks`, not Bitcoin's real `210,000` blocks.
- Initial block reward: `50 BTC`. Total supply: `210,000 BTC`.
```

Line 261 (completed checklist — mark old and add new):
```markdown
- [x] Basic halving scale defined: `2,100 blocks` (updated from 4,381 in 100X migration).
```

#### Task 4.5 — Documentation/GLOSSARY.md

Line 14:
```markdown
- **Halving**: Reward reduction event. Basic Mode uses a scaled interval of 2,100 blocks (≈ 4 in-game years at 100X scale, initial reward 50 BTC, converging to 210,000 BTC total).
```

---

### Phase 5 — Validation Checklist

Run these checks after completing all phases:

#### Math validation

```
Total BTC supply:   50 × 2,100 × 2 = 210,000 ✓
Halving period:     2,100 / 1.477 blocks/day / 365.25 = 3.89 years ≈ 4 years ✓
Year 2140 cutoff:   34 × 2,100 / 1.477 / 365.25 = 131.8 years → 2009 + 132 ≈ 2141 ✓
Block time:         585 × 100 in-game seconds = 58,500 = 16.25 in-game hours ✓
Autobet 10 min:     600 real seconds × 100 = 60,000 in-game seconds = 1,000 in-game minutes ✓
```

#### In-game checks

- [ ] Place 1 manual bet → game clock advances exactly 1 min 40 sec (100 seconds)
- [ ] Start autobet → `SpeedMultiplier` is 100 (verify in debugger or GD.Print)
- [ ] After 1 block is mined → coinbase reward is 50 BTC (check BlockExplorer)
- [ ] After block 2,100 → next mined block reward is 25 BTC
- [ ] BetsHistoryExplorer x1 playback runs at 100 in-game seconds per real second
- [ ] CalendarsNavigator x1 speed dropdown value is 100.0

#### Grep checks (verify no stale 48X references remain in code)

```
grep -r "48\.0d\|48\.0\b" Screens/ Scripts/   → should return 0 code matches
grep -r "48d\b" Screens/ Scripts/             → should return 0 code matches
grep -r "210000\|210_000" Scripts/            → should return 0 matches
```

---

## Decision Log

| # | Decision | Rationale |
|---|---|---|
| D-1 | `HalvingIntervalBlocks = 2100` (not 2158) | Clean round number; 3.89 in-game years is close enough to 4. |
| D-2 | `GenesisRewardBtc` stays at `50m` | 50 × 2,100 × 2 = 210,000 BTC — clean total, no code change needed, familiar reward curve identical to real Bitcoin. |
| D-3 | Halving cap changed from 32 to 34 halvings | Targets in-game year ~2141 instead of ~2134 — closer to the canonical "2140" milestone. |
| D-4 | Difficulty unchanged | The `"00" + char ≤ '6'` target stays. ~585 attempts/block is a good game feel. Changing difficulty would require separate difficulty calibration work. |
| D-5 | Speed steps stay at 1x/2x/4x/10x ratios | Relative playback multipliers feel the same; only the base changes from 48 to 100. |
| D-6 | Existing saves are a known break | Retroactive reward recalculation would require a blockchain replay. Recommended: document a fresh save as required post-migration. |

---

## Open Questions

- Should the game display "~16h 40m per block" somewhere visible (e.g. mining stats panel)?
- Is the `48-transaction block cap` intentionally set to 48 for thematic reasons (linked to the old 48X tick), or is it a coincidence? If thematic, should it change to something at 100X?
- Should the `_speedSteps` label strings in `BetsHistoryExplorer` be updated to reflect "100X base" semantics in the UI, or left as relative x1/x2/x4/x10?
- Does the `CalendarsNavigator` speed dropdown need a UI label update beyond changing the numeric value?
