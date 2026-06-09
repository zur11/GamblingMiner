# GamblingMiner — CLAUDE.md

## Project Overview

**GamblingMiner** is an experimental Godot 4.5.1 / C# prototype that simulates early Bitcoin history combined with a casino betting system. The core mechanic: **time only advances when bets are placed, and each bet simultaneously performs one mining nonce attempt**.

- **Engine**: Godot 4.5.1 (.NET / C#)
- **Target framework**: .NET 8.0
- **Primary platform**: Windows
- **Save format**: Local Godot `user://` data (JSON)
- **Starting condition**: Player begins on **January 3, 2009** with **40,000 SC** total funds
- **Public status**: Experimental prototype with a serious game design direction

### Language Policy

All project files, source code, UI text, code-facing names, and documentation inside the repository **must be in English**. Spanish is reserved exclusively for AI chat and planning conversations outside the repository.

---

## Core Gameplay Loop

```
Place Bet → Dice Roll Resolves + 1 Nonce Attempt → Time Advances →
Block Mined? → BTC Reward + Checkpoint → Manage Bankroll / Strategies → Repeat
```

**The three-layer loop:**
1. **Casino layer** — bet, win or lose SC, manage bankroll discipline
2. **Mining layer** — every bet is one nonce attempt; bots compete for blocks
3. **Historical layer** — time progresses through real early Bitcoin history (2009+)

**Game over**: `Main Balance + Bankroll = 0`

---

## Code Conventions

### Language and Style

- **Language**: C# only. No GDScript for logic files.
- **Files**: `PascalCase.cs` (e.g., `BetHistoryRepository.cs`)
- **Classes**: `PascalCase` (e.g., `class ProgressiveBettingStrategy`)
- **Interfaces**: `IPascalCase` (e.g., `IBettingStrategy`)
- **Methods**: `PascalCase` (e.g., `ExecuteNext()`)
- **Fields / locals**: `camelCase` (e.g., `currentBet`)
- **Private fields**: `_camelCase` (e.g., `_sessionId`)
- **Constants**: `PascalCase` or `UPPER_SNAKE_CASE` — follow existing pattern in the file
- **Scene files**: `.tscn`
- **Resource files**: `.tres`
- **Indentation**: **Tabs** — Godot auto-formats with tabs; never use spaces in `.cs` files opened by the editor

### Godot / C# Integration

- All service singletons extend `Godot.Node` as `partial class`
- Override `_Ready()` for initialization, `_Process(double delta)` for per-frame logic
- Autoloads are registered in `project.godot` and accessed globally by class name (no `GetNode` needed)
- Signals: prefer typed C# `event Action<T>` for service-to-service communication; use Godot signals for scene-to-UI connections where needed
- Node references: `GetNode<T>("%UniqueNodeName")` or `GetNode<T>("ChildName")` — never use `%` or `$` on another object's reference

### Money Handling

- All monetary values: **8 decimal places** (BTC satoshi-model precision)
- Always use `Money.Normalize()` before storing any decimal result
- Use `Money.FormatSignedAdaptive()` for display strings
- Never accumulate fractional profit without using `BetService`'s built-in remainder accumulation

### Time

- `DateTime.Utc` for storage, persistence, and internal comparisons
- `DateTime.Local` for player-facing display (game time starts `2009-01-03 18:15:06 Local`)
- Unix **milliseconds** for blockchain timestamps
- Game-time scale: **1 bet tick = 48 in-game seconds**; autobet target: **10 real minutes = 8 in-game hours**

### JSON Persistence

- All `user://` files use JSON with **CamelCase** naming policy
- History files are chunked by month to keep file sizes manageable
- Always use `FileAccess` (Godot API) for `user://` paths

---

## Key Architecture — Autoload Services

Six singletons registered in `project.godot`. They persist across all scenes and are accessible globally by class name.

### `CalendarTimeService`
**Location**: `Scripts/Services/CalendarTimeService.cs`

Manages game-time progression.

- Game start: `2009-01-03 18:15:06 Local`
- Advances via `_Process(delta)` when `IsRunning = true`
- `SpeedMultiplier` allows adjustable time scaling
- Persists to `user://calendar_state.json`
- Key properties: `CurrentLocalDateTime`, `CurrentUtcDateTime`, `ExplorerSelectedLocalDateTime`

### `UserStatsService`
**Location**: `Scripts/Services/UserStatsService.cs`

Tracks betting statistics and history with persistence.

- Maintains persistent bet history (JSON, chunked by month)
- Emits `StatsChanged` event throttled at 250 ms to avoid UI overload
- Supports time-travel balance reconstruction and historical stats queries
- Key method: `OnBetExecutedRegisterBet()`

### `BankrollStateService`
**Location**: `Scripts/Services/BankrollStateService.cs`

Manages the **Bankroll** (active betting subaccount).

- Bankroll is separate from Main Balance
- Persists to `user://bankroll_state.json`
- Auto-initialized on first run

### `PrincipalBalanceService`
**Location**: `Scripts/Services/PrincipalBalanceService.cs`

Manages the **Main Balance** (player reserve outside active betting).

- Default initial value: `39,900 SC` (with `100 SC` to Bankroll = `40,000 SC` total)
- Persists to `user://principal_balance_state.json`
- **Legacy naming note**: internal class still uses `PrincipalBalance`; user-facing labels must say `Main Balance`

### `BankrollProgramService`
**Location**: `Scripts/Services/BankrollProgramService.cs`

Manages transfers between Main Balance and Bankroll.

- Tracks auto-recharge events and transfer history
- Records direction and reason for each transfer
- Calculates performance metrics vs. initial `40,000 SC` baseline
- Provides daily / weekly / monthly auto-recharge counters
- Persists to `user://bankroll_program_state.json`

### `BlockSessionCheckpointService`
**Location**: `Scripts/Services/BlockSessionCheckpointService.cs`

Saves the full financial state at each block mining event.

- Captures: Principal Balance, Bankroll, Auto-Recharge amount, Transfer records
- Stores calendar local time + history checkpoint UTC time independently
- Enables rollback to pre-mined-block state
- Persists to `user://block_session_checkpoint.json`

---

## Core Game Systems

### Dice Engine
**Location**: `Scripts/Dice/DiceEngine.cs`

- 00–99 roll system with configurable chance and multiplier
- **RTP**: 99.02% (house-favorable)
- Multiplier formula: `(100 * RTP) / chance%`
- Profit: `win ? (bet * multiplier - bet) : -bet`

### Betting Strategy System
**Locations**: `Scripts/Betting/`

- `IBettingStrategy` — strategy interface
- `ProgressiveBettingStrategy` — multiplies bet by `1 + (IncreasePercent / 100)` on configured trigger; resets to base bet otherwise
- `BettingStrategyConfig` — data model with all parameters:
  - `BaseBet`, `IncreasePercent`, `IncreaseOnLoss`, `IncreaseOnWin`
  - `StopOnProfit`, `StopOnLoss` (optional thresholds)
  - `StopOnBlockMined` — halts session when a block is mined
  - `UseProgressionAnchorStops` — stop conditions relative to streak start
  - `InsistAfterStop` — retry after hitting a stop condition (except for StopOnBlockMined)
- `SavedBettingStrategy` / `SavedBettingStrategyRepository` — persistence of named strategies

### Bet Sessions
**Locations**: `Scripts/Sessions/`

- `BaseBetSession` — abstract; handles run state, remaining bets, current bet, progression streaks, stop conditions; calls `BetService.ExecuteBet()`
- `AutoBetSession` — extends `BaseBetSession`; adds session ID tracking
- `ManualBetSession` — single-bet handler

### Bet Execution Pipeline

```
User/Session calls ExecuteNext()
  → BetService.ExecuteBet()
      → Wallet.ApplyTransaction(withdrawal)
      → DiceEngine.Play()
      → If win: Wallet.ApplyTransaction(payout)
      → Accumulate fractional profit remainder
      → Emit BetTransactionEvent
  → ProgressiveBettingStrategy.CalculateNextBet()
  → BaseBetSession.ApplyStopConditions()
  → UserStatsService.OnBetExecutedRegisterBet()
  → BankrollProgramService.TryTransferBalanceToBankroll() (auto-recharge if configured)
```

### Blockchain / Mining System
**Locations**: `Scripts/BlockchainPort/`

- `BlockchainService` — difficulty model: hash must start with `"00"` and next hex ≤ `'6'`; expected attempts ~107 per block
- `NodeAgent` — generates ECDSA wallet keypair; `TryMineSingleNonceAttempt()` = one attempt per call (enforces `1 bet = 1 attempt` rule); caches candidate block to avoid recomputing on each attempt
- `CryptoUtils` — ECDSA signing/verification, SHA256 hashing, address derivation
- **Genesis block**: nonce=100, hash=`"0"`, previous=`"0"`, timestamp `2009-01-03 18:15:05 Unix ms`
- **Coinbase reward**: starts at 50 BTC, halves every **4,381 blocks** (intentionally scaled ≈ 4 in-game years)
- **Block cap** (planned): 48 transactions per block

---

## Data Models

### Finance

| Class | Purpose |
|---|---|
| `Wallet` | Simple balance ledger; `ApplyTransaction()`, `SetBalanceForTimeTravel()` |
| `Money` | Static utility; 8-decimal precision; `Normalize()`, `FormatSignedAdaptive()` |
| `Transaction` | Enum types: `Deposit`/`Withdrawal`; source types: `Bet`/`External`/`OtherGame` |
| `BetTransactionEvent` | Record capturing full roll metadata (bet, profit, roll, chance, multiplier, direction, timestamp) |
| `BetRecord` | Persistent history entry (game ID, outcome, amounts, roll details) |

### Blockchain

| Class | Purpose |
|---|---|
| `Block` | Index, Timestamp (Unix ms), Transactions[], Nonce, Hash, PreviousBlockHash, MinedByNodeId |
| `BlockTransaction` | Sender/Recipient (BTC addresses), amount, fee, signature (Base64 ECDSA), IsSpendable |
| `NodeAgent` | Mining node with ECDSA keypair; mines nonces, creates signed transactions |

### History

| Class | Purpose |
|---|---|
| `BetHistoryRepository` | Loads/saves JSON chunked by month; rollback to UTC timestamp; time-bucket summaries |
| `UserBettingStats` | Aggregates wins/losses, total wagered, net profit; per-game stats |
| `TimeBasedBetStats` | Pre-calculated summaries for fast performance queries |

---

## File Organization

```
GamblingMiner/
├── Documentation/              # Design docs (English only)
│   ├── DESIGN_OVERVIEW.md      # Target design with implementation status labels
│   ├── GLOSSARY.md             # Canonical terminology
│   ├── PLAYER_GUIDE.md         # What is actually playable now
│   └── PRIVATE_ROADMAP.md      # Internal priorities P0–P8
│
├── Screens/                    # UI scenes + screen controllers
│   ├── DiceGame/               # Main game loop (ManualBet, AutoBet, strategy selector)
│   ├── BlockExplorer/          # Blockchain inspector
│   ├── BankrollProgrammer/     # Main Balance ↔ Bankroll UI
│   ├── BetsHistoryExplorer/    # Historical stats browser
│   ├── CalendarsNavigator/     # Time-based history browsing
│   ├── MartingaleCalculator/   # Strategy planner
│   └── Shared/                 # Reusable UI components
│
├── Scripts/                    # Core logic (~50 C# files)
│   ├── Services/               # Autoload singletons (6 services)
│   ├── Betting/                # Strategy config, interface, progression logic
│   ├── Sessions/               # Bet loop controllers (Base, Auto, Manual)
│   ├── Dice/                   # DiceEngine, DiceResult
│   ├── Finance/                # Wallet, Money, Transaction, BetTransactionEvent
│   ├── Game/                   # BetService, IBetEventSource
│   ├── History/                # BetHistoryRepository, BetRecord, stats
│   ├── BlockchainPort/
│   │   ├── Blockchain/         # BlockchainService, Models, CryptoUtils
│   │   └── Simulation/         # NodeAgent, NetworkSimulator
│   ├── Calendars/              # CalendarModel, GregorianCalendarModel
│   ├── StateMachines/          # AutoBetSessionStateMachine
│   ├── Controllers/            # WalletController
│   └── User/                   # UserBettingStats, UserBetRecord
│
├── UI/                         # Reusable UI component scripts
│   ├── StrategyControlPanel/
│   ├── FinancialBettingStats/
│   └── DepositPopup/
│
├── GamblingMiner.csproj        # .NET 8.0, Godot.NET.Sdk 4.5.1
├── GamblingMiner.sln
├── Main.cs / Main.tscn
├── project.godot
└── CLAUDE.md
```

---

## Canonical Decisions

These values are fixed and must be consistent across all docs, UI, and code:

| Decision | Canonical Value |
|---|---|
| General initial balance | `40,000 SC` |
| Specific split | `39,900 SC Main Balance + 100 SC Bankroll` |
| Player-facing term | `Main Balance` (not "Principal Balance") |
| Game over condition | `Main Balance + Bankroll = 0` |
| Current mining rule | `1 bet = 1 nonce attempt` |
| Basic Mode halving | `4,381 blocks` (≈ 4 in-game years) |
| Real Bitcoin halving | `210,000 blocks` — NOT used in Basic Mode |
| Block transaction cap | `48 transactions` (planned) |
| Hardware cap | `100 nonce attempts` per time cycle (planned) |
| RTP | `99.02%` |
| Currency for betting | SC only — BTC cannot be wagered directly |

---

## Implementation Status

### Implemented

- Manual and autobet in Dice game
- Progressive betting strategies with save/load
- Time progression (1 bet = 48 in-game seconds)
- 1 bet = 1 nonce mining attempt
- Block mining with SHA256 difficulty target
- Block reward system (50 BTC, halving at 4,381 blocks)
- Blockchain Explorer (blocks, transactions, addresses, node balances)
- Financial checkpoints at block mining events
- Main Balance / Bankroll separation
- Auto-recharge system with transfer tracking
- User betting statistics and history persistence (JSON, monthly chunks)
- Calendar-based history browsing

### Prototype (Partially Implemented)

- Bot mining nodes (can mine blocks; no wallet transactions yet)
- Transaction model with ECDSA signatures
- Mempool data structure (pending transactions)

### Planned (P0–P8 Roadmap)

| Priority | Feature |
|---|---|
| P0 | Documentation truth pass — status labels everywhere |
| P1 | Main Balance naming alignment across all UI and docs |
| P2 | Bankroll auto-recharge rules UX and warning labels |
| P3 | Bot wallets, transactions, casino BTC addresses, public mempool |
| P4 | Block template builder (ancestor-feerate ordering, Merkle root, coinbase fees) |
| P5 | Hardware progression (bets per real second, not time acceleration) |
| P6 | Casino finances tracking (SC income/expense, bank credit line) |
| P7 | BTC/SC trading via casino BTC addresses |
| P8 | Achievements system (survival, mining, SC/BTC milestones) |

---

## Important Patterns

### 1. Event-Driven Services

Services communicate via typed C# events, not Godot signals:

```csharp
// Emitting
event Action<UserBettingStats> StatsChanged;

// High-frequency throttle pattern
private void EmitStatsChangedIfNeeded()  // 250 ms throttle
```

### 2. Checkpoint / Rollback

`BlockSessionCheckpointService` captures the full financial state at each block mining event. This is the only rollback mechanism. Do not add ad-hoc save points elsewhere.

### 3. Fractional Profit Accumulation

`BetService` accumulates sub-satoshi remainders internally. Never round individual bet payouts at the call site — let `BetService` handle precision.

### 4. Legacy Naming Migration

Internal service classes still use `PrincipalBalance` names. User-facing labels **must** use `Main Balance`. Internal class renames are deferred. Do not introduce new code that uses `PrincipalBalance` as a user-facing string.

### 5. Autoload Access Pattern

In Godot 4 C#, autoloads are nodes attached under `/root/`. The correct access pattern is `GetNodeOrNull<T>("/root/ServiceName")` called in `_Ready()`, stored in a private field:

```csharp
private CalendarTimeService _calendarTimeService;
private BankrollStateService _bankrollStateService;

public override void _Ready()
{
    _calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
    _bankrollStateService = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
}
```

Use `GetNodeOrNull` (not `GetNode`) so the app does not crash if the autoload is temporarily absent during development. Always null-check before use: `_calendarTimeService?.CurrentLocalDateTime`.

**Do not** access autoloads by bare class name or via a static `Instance` property — Godot C# autoloads do not work that way.

---

## Glossary Reference

See `Documentation/GLOSSARY.md` for the full canonical terminology list. Key terms:

- **SC** — Stable Coin, simulated USD-pegged currency
- **Main Balance** — player reserve outside active betting
- **Bankroll** — subaccount of Main Balance used for active bets
- **Autobet** — automated repeated betting using the current strategy
- **Nonce** — value miners vary while searching for a valid block hash
- **RTP** — Return to Player (Dice targets 99.02%)
- **Halving** — reward reduction event; Basic Mode = 4,381 blocks
- **Stop on block mined** — strategy condition that halts betting after a block is found

---

## Development Best Practices

- Always prefer editing existing files to creating new ones
- Never create documentation files unless explicitly requested
- Verify canonical values (balances, intervals, RTP) against this file and `GLOSSARY.md` before hardcoding
- Use `Grep`/`Glob` for exploration; do not use `bash find` or `bash grep`
- Check git status before committing
- Follow existing naming patterns: `PascalCase` for classes and files, `_camelCase` for private fields
- Always call `Money.Normalize()` before storing any decimal result
- Use `DateTime.Utc` for storage; `DateTime.Local` for display
- High-frequency service events must be throttled — see `UserStatsService.EmitStatsChangedIfNeeded()` as the reference pattern

---

## Open Design Questions

- What threshold lets the casino start repaying bank debt (P6)?
- Should minimum wager requirements be weekly, monthly, or both?
- How harsh should fee penalties be for missing minimum wager requirements?
- How much bot betting history should the player see by default?
- When exactly should BTC trading unlock in Basic Mode?
- Should private mempool fees be available in Basic Mode or postponed?

---

## Scene Management

### Current State (to be migrated)

Scene transitions are currently done inline with hardcoded paths:
```csharp
GetTree().ChangeSceneToFile("res://Screens/DiceGame/DiceGame.tscn");
```
This pattern is scattered across multiple screen files. It is fragile and should be replaced.

### `SceneManager` Autoload

A `SceneManager` autoload centralizes all scene transitions. All paths live in one place; call sites use a compile-time-safe enum.

**`Scripts/Services/SceneManager.cs`** (registered in `project.godot`):

```csharp
public partial class SceneManager : Node
{
    public enum SceneId
    {
        DiceGame,
        BlockExplorer,
        BankrollProgrammer,
        BetsHistoryExplorer,
        CalendarsNavigator,
        MartingaleCalculator,
        MainMenu,           // planned
        // Add new scenes here only
    }

    private static readonly Dictionary<SceneId, string> Paths = new()
    {
        [SceneId.DiceGame]              = "res://Screens/DiceGame/DiceGame.tscn",
        [SceneId.BlockExplorer]         = "res://Screens/BlockExplorer/BlockExplorer.tscn",
        [SceneId.BankrollProgrammer]    = "res://Screens/BankrollProgrammer/BankrollProgrammer.tscn",
        [SceneId.BetsHistoryExplorer]   = "res://Screens/BetsHistoryExplorer/BetsHistoryExplorer.tscn",
        [SceneId.CalendarsNavigator]    = "res://Screens/CalendarsNavigator/CalendarsNavigator.tscn",
        [SceneId.MartingaleCalculator]  = "res://Screens/MartingaleCalculator/MartingaleCalculator.tscn",
        [SceneId.MainMenu]              = "res://Screens/MainMenu/MainMenu.tscn",
    };

    public void Go(SceneId scene) => GetTree().ChangeSceneToFile(Paths[scene]);
}
```

**Usage in any screen after migration:**
```csharp
private SceneManager _sceneManager;

public override void _Ready()
{
    _sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
}

private void OnBackButtonPressed()
{
    _sceneManager?.Go(SceneManager.SceneId.DiceGame);
}
```

All existing main screens have been migrated. Adding a new scene: (1) add entry to `SceneId` enum, (2) add path to `Paths` dictionary, (3) call `_sceneManager?.Go(SceneId.X)` at the call site.

### `StatusBar` Component

**`UI/StatusBar/StatusBar.cs`** — pure C# `HBoxContainer` (no .tscn needed). Instantiated programmatically in each screen's `_Ready()`.

Shows Main Balance, Bankroll, and game clock — updates every frame via `_Process`.

```csharp
// In _Ready() of any screen — insert at top of a VBoxContainer:
var vbox = GetNode<VBoxContainer>("ContainerPath");
var statusBar = new StatusBar();
vbox.AddChild(statusBar);
vbox.MoveChild(statusBar, 0);

// Or for scenes that use a placeholder slot (MainMenu, MartingaleCalculatorStandalone):
GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
```

### Navigation Map

```
MainMenu
├── DiceGame          (also reachable directly; DiceGame has its own "Main Menu" button)
│   ├── BankrollProgrammer  → Main Menu
│   ├── BlockExplorer       → Main Menu
│   └── CalendarsNavigator  → Main Menu / BetsHistoryExplorer
│       └── BetsHistoryExplorer → Main Menu or CalendarsNavigator
└── MartingaleCalculator (standalone, full-screen) → Main Menu
```

DiceGame's MartingaleCalc button opens the **popup version** (`Screens/MartingaleCalculator/`) inline — it does not navigate away. The standalone version (`Screens/MartingaleCalculatorStandalone/`) is a full screen reachable only from MainMenu.

---

## Testing

**Status**: _[Pending — no test framework configured yet. Document test approach here once established.]_

---

## Architecture Documentation

Detailed design documents are in `Documentation/`:

| File | Contents |
|---|---|
| `DESIGN_OVERVIEW.md` | Target design per system with implementation status labels |
| `GLOSSARY.md` | Canonical terminology (source of truth for naming) |
| `PLAYER_GUIDE.md` | What is playable now (updated for each release) |
| `PRIVATE_ROADMAP.md` | Internal priorities P0–P8, canonical decisions, open questions |
