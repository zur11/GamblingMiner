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

### UI Layout & Scrolling (Godot) — read before touching a scrollable panel

Hard-won rules (a scroll bug once cost a full session — full write-up + diagnostics in `Documentation/ProjectDesignManual.md` "UI Design & Godot Layout"):

- **A panel scrolls only if it has a BOUNDED height smaller than its content.** The reliable bounding chain is `MarginContainer` (fills the screen via `anchors_preset = 15`) → `VBoxContainer` → the scroll element with `size_flags_vertical = Fill+Expand (3)`. A container that isn't itself height-bounded can't bound its children.
- **Pick ONE of two scroll patterns deliberately — never mix them:**
  1. **`ScrollContainer` wrapping the content** — for a column of many controls (Labels/Buttons/inputs, or `RichTextLabel`s **with an explicit `custom_minimum_size`**). Used by `FoundersWallets`, `BotsBtcWallets`.
  2. **A single `RichTextLabel` with `scroll_active = true` + `fit_content = false`** (bounded height) — for one big block of dynamic BBCode text. Used by `BlockExplorer`'s right column.
- **NEVER put a `fit_content = true` `RichTextLabel` inside a `ScrollContainer` expecting it to scroll.** `fit_content`'s reported minimum height is unreliable inside containers, so the `ScrollContainer` never learns the content overflows. This is the #1 time-sink.
- **`HSplitContainer` does not reliably bound/report content height inside a scroll — use `HBoxContainer`** for two columns that must scroll.
- **Mouse wheel + `mouse_filter`:** with pattern (1), the wheel reaches the `ScrollContainer` only if every control in the chain from the hovered node up to it has `mouse_filter = PASS (1)` (default is `STOP`, which eats the wheel). A big label filling the panel will swallow the wheel — set it to `PASS`, or use pattern (2) where the label scrolls its own wheel.
- **The last line sits flush against the scroll's bottom edge** (`scroll_active` max = content height). Append a few trailing blank lines (`"\n\n\n"`) so the final real line clears the edge and isn't half-clipped.
- **Setting `RichTextLabel.Text` resets its internal scroll to the top.** On a timer-refreshed panel, save `GetVScrollBar().Value` before setting `Text` and restore it after.
- **Diagnose with numbers, never guess.** If a panel won't scroll, print `GetVScrollBar()` `MaxValue`/`Page`/`Value`, `Size`, `GetContentHeight()`, and whether the data is even present — before restructuring. Add a visible canary (e.g. a title marker) to confirm the scene actually reloaded the edited `.tscn` (C# always rebuilds; external `.tscn` edits need a scene reload in the editor).
- **Block Explorer display filter (OQ-8.2 cosmetic, `BlockExplorer.cs`):** `IsSelfChangeTransaction(tx)` hides a tx entirely when all its outputs go to input addresses (pure self-loop). `ExternalOutputs(tx)` strips only the change-to-self output for txs that have at least one external recipient. Both are temporary cosmetics for bots' single-address change-to-self pattern. Remove them once bots have `DerivedAddressWallet` (before referral / rank systems ship). Detail: `Documentation/ProjectDesignManual.md` §29.9.

### Money Handling

- All monetary values: **8 decimal places** (BTC satoshi-model precision)
- Always use `Money.Normalize()` before storing any decimal result
- Use `Money.FormatSignedAdaptive()` for display strings
- Never accumulate fractional profit without using `BetService`'s built-in remainder accumulation
- **Number locale**: canonical format is `1,000,000.00000000` — comma for thousands separator, period for decimal point. This is `CultureInfo.InvariantCulture`. **Never** use a raw C# interpolated string with a decimal format specifier (`:N8`, `:F2`, `:+0.00000000;-0.00000000`, etc.) — it will invert the separators on Spanish/European locales. Always pass `CultureInfo.InvariantCulture` explicitly: use `string.Create(CultureInfo.InvariantCulture, $"… {value:N8} …")` for compound strings, or `.ToString("N8", CultureInfo.InvariantCulture)` for single values. `Money.FormatSignedAdaptive()` already does this internally.

### Time

- `DateTime.Utc` for storage, persistence, and internal comparisons
- `DateTime.Local` for player-facing display (game time starts `2009-01-03 18:15:06 Local`)
- Unix **milliseconds** for blockchain timestamps
- Game-time scale: **1 bet tick = 100 in-game seconds**; autobet target: **10 real minutes = 16h 40m in-game**

### JSON Persistence

- All `user://` files use JSON with **CamelCase** naming policy
- History files are chunked by month to keep file sizes manageable
- Always use `FileAccess` (Godot API) for `user://` paths

---

## Key Architecture — Autoload Services

Seven core service singletons registered in `project.godot` (plus `SceneManager`, `NotepadService`, `FoundersMiningService`, `CasinoScBalanceService`, and `CasinoClientLedgerService`, documented in their own sections — **twelve autoloads total**). They persist across all scenes and are accessible globally by class name.

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
- **On startup**, `ApplyCheckpointToServices()` restores **both** balances **and** the game clock (+ the `_gamePresent` frontier) to the last block — the only place the clock reverts on app restart, so it applies before any scene loads. A block is the only commit to disk (see Important Pattern 2)

### `SimulationService`
**Location**: `Scripts/Services/SimulationService.cs`

Owns the running **background simulation** so it survives scene changes. While a player autobet is active, this service ticks the player autobet **and** the bot runners in its own `_Process`, in every scene — bets fire, bots mine, time advances, balances change. DiceGame is a thin view/controller on top of it.

- **Single source of truth = `BankrollStateService`**: the service builds its **own** wallet/session (seeded from the bankroll, written back each settled bet), so its wallet has **no** scene-bound event subscriptions and freeing a scene cannot crash it.
- Owns bot runners (`StartBots`/`StopBots`/`TickBots`/`RunBotManualBurst`); DiceGame supplies per-node strategy snapshots via `BuildBotConfigs()`.
- Player **and** bot auto-recharge happen *after* the session self-stops on `InsufficientBalance` (`TryPlayerAutoRechargeAndRestart` / `TryRechargeAndRestartBot`), restarting the progression from base bet.
- Signals: `BetSettled` (per player bet), `AutobetStopped` (run ended). Exposes `GetActiveMiningRates()` for the Block Explorer mining indicator.
- While delegated, it is the **sole owner** of `CalendarTimeService.IsRunning/SpeedMultiplier/IsAutobetActive`. No persisted run state → the app starts with autobet **stopped**.
- Also **drives the founders' concurrent mining** (Step 7): each frame it recomputes founder power once per new block, feeds `player+bots+founders` power to the difficulty regulator, and runs `FoundersMiningService.DrainFounderAttempts` so Satoshi/Hal mine in lockstep with the player's time advancement. `GetTotalActiveMiningPower()` is player+bots **only** (it is the founders' competition denominator — never sum `GetActiveMiningRates()`, which also lists founders/casino for display).
- Not persisted; registered in `project.godot` as an autoload.
- See `Documentation/ProjectDesignManual.md` Chapter 24 and `AIHelperFiles/background-simulation-plan.md`.

### `FoundersMiningService`
**Location**: `Scripts/Services/FoundersMiningService.cs`

Owns the **player-era mining power of the founders** (Satoshi + Hal) and the regulator math (Step 7). A **pure controller** — no chain/Godot state; callers feed it the live facts (other miners' power, the game clock, Satoshi's confirmed BTC) and it returns powers + per-founder nonce-attempt counts. No persisted state (recomputed from the live world each launch).

- **Satoshi** is power-regulated toward **11,000 BTC by 2011-04-26** (`shareToWeight` ramp ⇒ ~10% share; exponential past the floor date if short; retires when both conditions hold, coins frozen forever in Basic Mode).
- **Hal** keeps `P = 1.0` (one participant's worth) and fades linearly to 0 by **9 Aug 2009** (his ALS turning point) — a v1 stand-in for "falls behind as the network grows"; dormant after.
- **Founders are concurrent miners, not clock movers** (OQ-2 refinement): they only attempt nonces while the player advances time by betting. `DrainFounderAttempts` accrues each founder ∝ its power-share of the player+bot attempts that frame; `SimulationService` mines those on the founders' own candidates (own coinbase), handled as external blocks.
- **Mike Hearn never mines** — he's a receive-only holder driven by `HistoricalEventScheduler` (the static class, like `HistoricalBootstrapService`), which injects player-era scripted txs (the April 2009 32.51 round-trip) when the game clock crosses their date, with chain-derived idempotent state.
- DEV readout + `user://logs/founders_trace.csv` telemetry surface it in `FoundersWallets`.
- See `AIHelperFiles/step7-historical-character-economics-plan.md`.

### `CasinoScBalanceService`
**Location**: `Scripts/Services/CasinoScBalanceService.cs`

Owns the casino's own **StableCoin (SC) balance sheet** (Step 11) — the casino's parallel to the player's `PrincipalBalanceService` + `BankrollStateService` + `BankrollProgramService`, combined into one cohesive autoload.

- Initial allocation: **99,000,000 SC Main Balance + 1,000,000 SC Bankroll = 100,000,000 SC total**, representing a hypothetical bank loan (`TotalLoaned` starts at 100M).
- `ApplyBetResult(casinoDelta)` is the single write path: called by `SimulationService` after every settled **player** bet with `casinoDelta = -betEvent.CreditedProfit` (player loss → casino gains; player win → casino pays). Bot bets do not route through it yet (OQ-11.1, deferred).
- **Target-to-fill auto-recharge**: the Bankroll fluctuates freely with each bet result; only when it reaches ≤ 0 does auto-recharge fire, refilling to a configurable `BankrollTarget` (default 1,000,000 SC) from Main Balance.
- **Bankruptcy flavor event**: if Main Balance can't cover the target-to-fill transfer, the bank injects another 100,000,000 SC loan directly (`LoanCount++`, `TotalLoaned += 100M`) before completing the recharge — the game never blocks a bet on casino insolvency.
- `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` is the casino's P/L metric — positive when the casino is ahead of all loans taken so far.
- Persists to `user://casino_sc_balance_state.json`. Extends `BlockSessionCheckpointService` (casino SC is snapshotted/restored at each block, consistent with "a block is the only commit to disk").
- DEV-only — never surfaced in player-facing UI. See `Screens/CasinoGamblingFinances/CasinoGamblingFinances.cs` and `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md`.

### `CasinoClientLedgerService`
**Location**: `Scripts/Services/CasinoClientLedgerService.cs`

Tracks each casino client's SC deposit/withdrawal history from the casino's operational perspective (Step 11). Forward-compatible for multiple clients (currently just `"player"`); prerequisite for the since-last-deposit metrics in `ClientsBetsHistory` and the full transaction list in `ClientsTransactions`.

- `LedgerEntry.Kind` ∈ `"initial"` (first-ever deposit, recorded once on first launch), `"deposit"` (future manual SC deposits via a dedicated SC Wallet scene; today routed from `DiceGame`'s `DepositPopup`), `"auto_recharge"` (internal Bankroll Auto-Recharge — **not** a real deposit), `"withdrawal"` (Bankroll → Main Balance).
- Only `"initial"`/`"deposit"` entries reset the since-last-deposit baseline (`GetLastDeposit`) and count toward "Total SC deposited" in `ClientsTransactions`; `"auto_recharge"` is recorded for operator visibility (DEV scenes) but excluded from both.
- Persists to `user://casino_client_ledger.json`. See `Documentation/GLOSSARY.md` for the SC Deposit / Bankroll Auto-Recharge / Bankroll Manual Recharge distinction, and `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md` OQ-11.6.

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
  - `UseProgressionAnchorStops` — chooses the baseline the `StopOnProfit`/`StopOnLoss` metric (`currentBalance − baseline`) is measured from. **Session mode** (`false`): `SessionStartingBalance` = bankroll at session start (net session P/L). **Anchor mode** (`true`): `ProgressionAnchorBalance` = bankroll at the start of the current progression run (P/L of just that run; a win re-anchors). With `InsistAfterStop`, both baselines re-anchor to the current balance on each reset. See Chapter 25.3.
  - `InsistAfterStop` — on a `StopOnProfit`/`StopOnLoss` hit, **reset the progression to base bet and keep going** instead of stopping. Applies **only** to `StopOnProfit`/`StopOnLoss`, **never** to `StopOnBlockMined` (a mined block always stops if that toggle is on).
- `SavedBettingStrategy` / `SavedBettingStrategyRepository` — persistence of named strategies

**Progression resets vs. auto-recharge (bankroll management).** Implemented in `BaseBetSession.ApplyStopConditions` + `SimulationService`; shared by player **and** bot sessions. Order of preference — *reset cheaply, recharge only as a last resort*:
1. **`StopOnLoss`/`StopOnProfit` + `InsistAfterStop`** (primary): threshold set **below** the bankroll caps a losing run's depth, resetting to base with **no** recharge.
2. **Bankroll-limit reset** (safety net): if the grown bet exceeds the bankroll but the **base** bet still fits and `InsistAfterStop` is on → reset to base, **no** recharge.
3. **Auto-recharge** (last resort): only when even the **base** bet can't be afforded does the session stop with `InsufficientBalance`; then — *after* the stop — `SimulationService.TryPlayerAutoRechargeAndRestart` / `TryRechargeAndRestartBot` moves funds (Main Balance→Bankroll for the player, `NodeFinancialState.PrincipalBalance` for bots) and **restarts the progression from base**. The recharge is decided *after* the stop because `ApplyStopConditions` self-stops on `InsufficientBalance` *inside* `ExecuteNext`. `InsistAfterStop` stays active across recharges. See `Documentation/ProjectDesignManual.md` Chapter 25 (and 24.5).

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

- `BlockchainService` — **continuous, regulated difficulty** (Step 6, D.1–D.4): `Difficulty` = expected nonce attempts per block; a 64-hex hash meets target when, read as a 256-bit `BigInteger`, `H ≤ 2²⁵⁶ / Difficulty`. `InitialDifficulty = 4096/7 ≈ 585.14` (the exact probability of the old `"00"`+next-hex-≤'6' rule, so pace is unchanged). Persisted per block (`Block.Difficulty`); `ChainIsValid` validates each block against its own stored difficulty (no genesis replay). `GetNextBlockDifficulty(networkPower)` is the **HYBRID retarget**: `target = anchor × feedbackTrim`, eased `next = current + DifficultyEaseAlpha·(target − current)`. **anchor** = `InitialDifficulty × power` (feed-forward from total active power = Σ miners' bets/sec, pushed by `SimulationService.SetActiveMiningPower`); **feedbackTrim** = LWMA over the last `LwmaWindow=20` block solvetimes vs `TargetBlockSeconds=58,500`, clamped `[0.5×,2×]`; `DifficultyEaseAlpha=0.7`. Power `0` (bootstrap/idle) → feedback-only. See `AIHelperFiles/btc-pools-hardware-plan.md` + ProjectDesignManual Ch.26.
- `NodeAgent` — generates ECDSA wallet keypair; `TryMineSingleNonceAttempt()` = one attempt per call (enforces `1 bet = 1 attempt` rule); caches candidate block to avoid recomputing on each attempt
- `CryptoUtils` — ECDSA signing/verification, SHA256 hashing, address derivation
- **Genesis block**: nonce=100, hash=`"0"`, previous=`"0"`, timestamp `2009-01-03 18:15:05 Unix ms`
- **Coinbase reward**: starts at 50 BTC, halves every **2,100 blocks** (≈ 4 in-game years at 100X); total supply **210,000 BTC** (converges to in-game year ~2141)
- **Block cap** (planned): 24 transactions per block
- **Founder economics** (Step 7): Satoshi & Hal are **regulated concurrent miners** (`FoundersMiningService`, driven by `SimulationService`) — they mine their own candidates in lockstep with the player's bets (no autonomous clock). Satoshi targets ~10% share toward **11,000 BTC by 2011-04-26**; Hal fades to 0 by **9 Aug 2009**. Scripted historical txs: the **12 Jan 2009 10 BTC Satoshi→Hal** tx (`HistoricalBootstrapService`, in the bootstrap) and the **April 2009 Mike Hearn 32.51 round-trip** (`HistoricalEventScheduler`, player era, → Hearn +82.51, never mines). See `AIHelperFiles/step7-historical-character-economics-plan.md`.
- **Balance model**: a **real multi-input/multi-output UTXO model** (Step 8 / Appendix A — implemented & in-engine audited). A `Transaction` holds `Inputs[]` (each an `OutPoint` + per-input signature) and `Outputs[]`; balance = Σ of an address's unspent outputs; fee = Σin − Σout. The **UTXO set** is rebuilt by replaying the chain (cached by `_chainVersion`, never persisted — consistent with "a block is the only commit"). One spend path `NetworkRoot.BuildAndBroadcastUtxoSpend` coin-selects owned UTXOs (exact match else largest-first **multi-input** combine) + change to a fresh derived address. **Address non-reuse** (a fresh derived address per receive/coinbase) is **Satoshi-only** (his ~220-address "one coinbase per address" spread). The **player, casino, Hal, and Mike Hearn** become multi-address only via **change outputs on send** (`ReceiveWallet` + `NodeAgent.RotateCoinbaseAddress = false` → coinbase/receives stay on base, change rotates); **only the bots stay single-address** (no stored seed — OQ-8.2). Hearn's one outgoing tx (E6b → Satoshi 32.51) is an exact-match send (no change), so his rotation is inert today — kept for consistency. E8 (17.49 Hearn change) is now a real change output. Legacy `Sender`/`Recipient`/`Amount` survive as read-only `[JsonIgnore]` shims — they expose only `Inputs[0]`/`Outputs[0]`, so **never use them to scan the chain for address membership** (a change output at `Outputs[1]` would be missed — the bug that made change-held funds vanish from wallets after a restart); iterate the full `Inputs`/`Outputs` lists instead. The account→UTXO switch used a **clean reset** (`WorldFormatVersion`). See `Documentation/ProjectDesignManual.md` Ch. 30 + `AIHelperFiles/step8-utxo-realism-plan.md` (Appendix A). NOTE: "Patoshi pattern" is a **misnomer** for this address mechanic — it is **address non-reuse**; the real Patoshi pattern is a mining-forensic fingerprint (D0).

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
| Basic Mode halving | `2,100 blocks` (≈ 4 in-game years at 100X scale) |
| Total BTC supply | `210,000 BTC` — converges to in-game year ~2141 |
| Real Bitcoin halving | `210,000 blocks` — NOT used in Basic Mode |
| Block transaction cap | `24 transactions` (planned) |
| Hardware cap | `100 nonce attempts` per time cycle (planned) |
| Network fee activation | `~2009-04-26` nearest block ✅ **Implemented** — whole network **fee-free before**, all participants (bots/casino/player) pay fees **after**; `NetworkFeePolicy` is the single source of truth. See `AIHelperFiles/step10-network-fee-activation-plan.md` |
| RTP | `99.02%` |
| Number format | `1,000,000.00000000` — comma=thousands, period=decimal (`CultureInfo.InvariantCulture`); never use raw `:N8`/`:F2` in string interpolations |
| Currency for betting | SC only — BTC cannot be wagered directly |
| Founders | Satoshi (target `11,000 BTC`, retires ≥ `2011-04-26`, then frozen) + Hal (`P=1.0` drip, fades to 0 by `2009-08-09`) + Mike Hearn (joins ~Apr 2009, never mines, +82.51 BTC round-trip) |
| Player start | `21 Mar 2009` after the first-launch bootstrap |

---

## Implementation Status

### Implemented

- Manual and autobet in Dice game
- Progressive betting strategies with save/load
- Time progression (1 bet = 100 in-game seconds)
- 1 bet = 1 nonce mining attempt
- Block mining with SHA256 difficulty target
- Block reward system (50 BTC, halving at 2,100 blocks, total supply 210,000 BTC)
- Blockchain Explorer (blocks, transactions, addresses, node balances)
- Financial checkpoints at block mining events
- Main Balance / Bankroll separation
- Auto-recharge system with transfer tracking
- User betting statistics and history persistence (JSON, monthly chunks)
- Calendar-based history browsing
- Background simulation: autobet + bots keep running, mining, and recharging across all scenes (SimulationService autoload)
- Historical founders (Step 7): Satoshi/Hal/Hearn nodes; first-launch bootstrap to 21 Mar 2009; founders as regulated concurrent miners (`FoundersMiningService`); Satoshi 11k-BTC ramp + disappearance logic; Hal drip-fade to 9 Aug 2009; 12 Jan 10 BTC Satoshi→Hal tx; April 2009 Hearn 32.51 round-trip (`HistoricalEventScheduler`); FoundersWallets DEV readout + `founders_trace.csv`
- UTXO realism (Step 8): real multi-input/multi-output UTXO model (chain-replayed UTXO set, per-input signing, `Fee = Σin − Σout`, multi-input coin selection + change); Satoshi-only coinbase address non-reuse (~220 addresses); change rotation for player/casino/Hal/Hearn; E8 reinstated; clean reset (`WorldFormatVersion`); address-book UIs (BTCWallet/FoundersWallets/CasinoFinances) + "View empty addresses" toggle. In-engine audited (conservation, 0 double-spends, 100-input consolidation, full April round-trip). See `Documentation/ProjectDesignManual.md` Ch. 30.
- Bot mining + BTC transactions (mine blocks; recirculate BTC via scheduled payouts); ECDSA-signed transactions; mempool (pending transactions)
- Network fee activation (P10): `NetworkFeePolicy` (`ActivationDateLocal = 2009-04-26`, `DefaultFee = 0.1 BTC`, `MinFee/MaxFee`); fee row hidden before activation, default-filled and clamp-validated after, in all four BTC wallet send panels (BTCWallet, FoundersWallets, CasinoFinances, BotsBtcWallets); sender balance label on every send panel; backend bot-automated-fee and casino-pool-payout-fee gates on `block.Timestamp`
- Casino pool distribution atomicity: one multi-output tx per pool event (`DistributePoolEventAsSingleTx`) — eliminates partial/double-payment bug caused by sequential single sends depleting the only available UTXO before change confirmed
- Block Explorer multi-output display: full `tx.Inputs[]` / `tx.Outputs[]` iteration in block lookup and right-column preview; `tx.IsCoinbase` for coinbase detection; all transactions in a block shown (was only the first); fee LINQ uses `!t.IsCoinbase`
- Block Explorer OQ-8.2 cosmetic filter: `IsSelfChangeTransaction(tx)` hides txs whose every output goes back to an input address; `ExternalOutputs(tx)` strips change-to-self outputs from the displayed output list for txs that DO have external recipients. Remove both helpers once bots have `DerivedAddressWallet` (before referral/rank systems). See `Documentation/ProjectDesignManual.md` §29.9

### Prototype (Partially Implemented)

- Bots stay single-address (no per-bot seed → no change rotation yet — OQ-8.2). The Block Explorer hides the resulting change-to-self outputs cosmetically (`IsSelfChangeTransaction` / `ExternalOutputs` — remove both when OQ-8.2 is resolved)

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

### 2. Checkpoint / Rollback — a block is the only commit to disk

`BlockSessionCheckpointService` captures the full financial state at each block mining event. This is the only rollback mechanism. Do not add ad-hoc save points elsewhere.

**Within a session**, the live clock, balances, and mempool advance and survive scene changes — the autoloads and the **static** `NetworkRoot` hold them in memory. **Nothing between blocks is persisted to disk** — not SC balances, not the chain, not the mempool. Between-block navigation / node-switch saves use `SaveActiveNodeFinancialState(false)` (in-memory only), and BTC transactions / consensus do not persist either (`NetworkRoot.CreateAndBroadcastTransaction`/`CreateAndBroadcastTransactionToAddress` only mutate the in-memory mempool). `PersistStateToDisk()` runs **only** at block-mining (`HandleMinedBlock`), baseline node creation, and startup; the player's block-commit financial write goes through `SimulationService.CaptureCheckpoint` / `DiceGame.CaptureBlockCheckpoint`. Consequently an **app restart reverts the whole world to the last mined block** — clock, every participant's balances, **and** un-mined pending transactions — performed at startup by `BlockSessionCheckpointService.ApplyCheckpointToServices()`. Within-session re-entry must never rewind the clock: `DiceGame` skips `EnsureGameEpochInitialized()` while `SimulationService.IsRunning`, and the checkpoint clock/history restore is a once-per-process operation guarded by the static `_checkpointRestoreSpentThisSession`. Full rationale and the bugs this resolved: `Documentation/ProjectDesignManual.md` §24.8.

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
- **Halving** — reward reduction event; Basic Mode = 2,100 blocks (≈ 4 in-game years at 100X)
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

The example above omits several DEV-only scenes for brevity (e.g. `CasinoFinances`, `FoundersWallets`, `BotPlayHistory`). Step 11 added three more, all DEV-only: `CasinoGamblingFinances` (Main Menu → casino SC balances/loans/transfers), `ClientsBetsHistory` (→ from `CasinoGamblingFinances`, per-client P/L + live bet feed), and `ClientsTransactions` (→ from `CasinoGamblingFinances`, per-client SC deposit/withdrawal ledger) — see `Screens/CasinoGamblingFinances/`.

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
├── MartingaleCalculator (standalone, full-screen) → Main Menu
└── CasinoGamblingFinances [DEV]  → Main Menu
    ├── ClientsBetsHistory [DEV]    → Casino Gambling Finances
    └── ClientsTransactions [DEV]   → Casino Gambling Finances
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

---

## Git Workflow

- **`main` is the stable trunk.** It is anchored at known-good points (e.g. a completed roadmap step). Keep it buildable.
- **One branch per category of modifications** (e.g. `scheduled-bot-transactions`, `candidate-block-model`, `historical-founders`). Do feature work on its branch; merge back to `main` when stable.
- **Staging and commits are done manually by the developer.** Claude does **not** run `git add`/`commit`/`push`/branch operations unless explicitly asked — only assists with git when requested. A clean working tree usually means the developer already committed; verify via recent commit history, don't assume there's work to commit.
- **CLAUDE.md is a `main` document.** Maintain and commit it on `main` (it describes stable architecture, so feature branches rarely edit it). When a merged feature changes the architecture, update CLAUDE.md on `main`. It stays tracked — do not untrack it (its history matters and Claude Code reads it every session).
