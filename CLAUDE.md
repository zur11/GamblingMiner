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

**Before creating OR editing ANY scene that contains a `ScrollContainer`, READ `Documentation/ProjectDesignManual.md` Chapter 29 ("UI Design & Godot Layout") first.** This is not optional boilerplate — the same layout bugs (won't-scroll, sideways-clip, footer-overflow) have recurred *specifically because that chapter was not consulted before building the scene*. Do not mirror another scene's layout blindly; the mirror may itself carry an anti-pattern (e.g. `CasinoGamblingFinances`'s footer-inside-scroll, which propagated the overflow bug into `ScFinances`).

Hard-won rules (a scroll bug once cost a full session — full write-up + diagnostics in `Documentation/ProjectDesignManual.md` Chapter 29):

- **A panel scrolls only if it has a BOUNDED height smaller than its content.** The reliable bounding chain is `MarginContainer` (fills the screen via `anchors_preset = 15`) → `VBoxContainer` → the scroll element with `size_flags_vertical = Fill+Expand (3)`. A container that isn't itself height-bounded can't bound its children.
- **Pick ONE of two scroll patterns deliberately — never mix them:**
  1. **`ScrollContainer` wrapping the content** — for a column of many controls (Labels/Buttons/inputs, or `RichTextLabel`s **with an explicit `custom_minimum_size`**). Used by `FoundersWallets`, `BotsBtcWallets`.
  2. **A single `RichTextLabel` with `scroll_active = true` + `fit_content = false`** (bounded height) — for one big block of dynamic BBCode text. Used by `BlockExplorer`'s right column.
- **NEVER put a `fit_content = true` `RichTextLabel` inside a `ScrollContainer` expecting it to scroll.** `fit_content`'s reported minimum height is unreliable inside containers, so the `ScrollContainer` never learns the content overflows. This is the #1 time-sink.
- **`HSplitContainer` does not reliably bound/report content height inside a scroll — use `HBoxContainer`** for two columns that must scroll.
- **Mouse wheel + `mouse_filter`:** with pattern (1), the wheel reaches the `ScrollContainer` only if every control in the chain from the hovered node up to it has `mouse_filter = PASS (1)` (default is `STOP`, which eats the wheel). A big label filling the panel will swallow the wheel — set it to `PASS`, or use pattern (2) where the label scrolls its own wheel.
- **The last line sits flush against the scroll's bottom edge** (`scroll_active` max = content height). Append a few trailing blank lines (`"\n\n\n"`) so the final real line clears the edge and isn't half-clipped.
- **Persistent nav / Back buttons go in a FIXED FOOTER, OUTSIDE the scroll** — never as the last child inside the `ScrollContainer` (there they overflow/clip at the bottom, clickable but unreadable). Structure: `MarginContainer → VBoxContainer (bounded) → { ScrollContainer(size_flags_vertical=3) for the content, THEN the nav/Back row as a sibling footer }`. `ScTransactions` is the reference; `ScFinances` was fixed to match (§29.10). Reparenting nodes between the scroll and the footer is safe — controllers resolve widgets by `%UniqueName`, which is path-independent.
- **The bottom ~50 px of the 1080 canvas can be OFF-SCREEN** in a plain/embedded window (the editor's Game view runs Windowed) — this, not the scroll, was the real cause of the Step 12 "Back button overflows" bug (it happened even with no active scroll). Keep must-read/must-click controls out of that band: give the page's `MarginContainer` a `margin_bottom ≥ ~50`, and remember an expanding child (`size_flags_vertical = 3`) **pins the following footer to the very bottom edge**, straight into the danger band. The exported build starts Maximized (`project.godot window/size/mode=2`, with `window/size/mode.editor=0` so editor embedding still works). Full write-up: §29.11.
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

Seven core service singletons registered in `project.godot` (plus `SceneManager`, `NotepadService`, `FoundersMiningService`, `CasinoScBalanceService`, `CasinoClientLedgerService`, `PlayerBankAccountService`, `BtcMarketDataService`, `CasinoCoinSwapService`, and `WorldGuardService`, documented in their own sections — **sixteen autoloads total**). They persist across all scenes and are accessible globally by class name.

**`WorldGuardService`** (`Scripts/Services/WorldGuardService.cs`) is deliberately the **FIRST** autoload: its only job is running `NetworkRoot.RunWorldCompatibilityGuard()` (format-version OR timeline-tag mismatch ⇒ full clean world reset, D-13.7) **before any other autoload can load a `user://` state file into a static cache** — a file deleted after being loaded survives in memory and re-persists, which is how alt-timeline hardware/pool state once leaked across a timeline wipe (TL.3 incident). Keep it first; see `Documentation/ProjectDesignManual.md` Ch. 35 §35.1.

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
- Calculates performance metrics vs. initial `40,000 SC` baseline (this is **Main Balance alone**, not net worth — relabeled in Step 12; the all-accounts figure is `OverallPl` in `ScFinances`)
- Provides daily / weekly / monthly auto-recharge counters
- **`AutoRechargeEnabled`** (Step 12 / D-SF.4, default **ON**) — the off-switch for the (formerly always-on) Bankroll dose recharge; persisted + checkpoint-covered (reverts to ON pre-genesis). Respected by `SimulationService.TryPlayerAutoRechargeAndRestart` and the manual-bet recharge path. Two UI access points to the **same** flag: the `BankrollProgrammer` toggle (canonical) and the DiceGame `StrategyControlPanel` toggle (now a proxy — see ProjectDesignManual §25.8)
- **Session-start invariant** (D-SF2.6): no user (bot or human) may begin an auto/manual bet session while the Bankroll is below the required bet. With `AutoRechargeEnabled` ON a running session refills from Main so play continues; with it OFF, betting stops on `InsufficientBalance` and waits for a manual recharge. Bots follow the identical rule against `NodeFinancialState.PrincipalBalance`
- Persists to `user://bankroll_program_state.json`

### `BlockSessionCheckpointService`
**Location**: `Scripts/Services/BlockSessionCheckpointService.cs`

Saves the full financial state at each block mining event.

- Captures: Principal Balance, Bankroll, Auto-Recharge amount + Transfer records (`BankrollProgramService`), Transfer records
- Stores calendar local time + history checkpoint UTC time independently
- Enables rollback to pre-mined-block state
- Persists to `user://block_session_checkpoint.json`
- **On startup**, `ApplyCheckpointToServices()` restores (Step 12: **six services**) — `BankrollStateService`, `PrincipalBalanceService`, `BankrollProgramService` (dose + `AutoRechargeEnabled` toggle + transfer records), `CasinoScBalanceService`, `PlayerBankAccountService` (bank balance + settings + transfer history), `CasinoClientLedgerService` (entries) — **and** the game clock (+ the `_gamePresent` frontier) to the last block. This is the only place the clock reverts on app restart, so it applies before any scene loads. A block is the only commit to disk (see Important Pattern 2)
- **Pre-genesis (no checkpoint exists yet — no player/bot/founder block has ever been mined)**: `ResetToPreGenesisDefaults()` runs instead, on **every** boot — Main Balance → `40,000.00`, Bankroll → `0.00`, dose → `BankrollProgramService.DefaultAutoRechargeAmount` (+ `AutoRechargeEnabled` → ON), transfer records → cleared, **`PlayerBankAccountService` → bank `0` / settings default / history cleared, `CasinoClientLedgerService` player entries cleared + `initial` re-established** (Step 12), calendar → exactly the historical bootstrap's last mined block's timestamp (`NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()`, no offset — see Canonical Decisions), bet history → rolled back to that same instant. A checkpoint is captured **only** by a real block-mined event (`DiceGame.CaptureBlockCheckpoint()` / `SimulationService.CaptureCheckpoint()`) — never merely by opening the app — so the world genuinely resets to a first-launch state every restart until the player's first real block. See `Documentation/ProjectDesignManual.md` §24.9

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

- **Extra-lazy funding (`extra-lazy` model, §31.1.1)**: the casino starts **all-zero** — `MainBalance = 0`, `Bankroll = 0`, `LoanCount = 0`, `TotalLoaned = 0` (only `BankrollTarget` keeps its `100 SC` dose default). It draws its foundational `40,000 SC` loan **on demand**, never at boot: on a losing streak it just accumulates player losses in its Bankroll with no loan; the loan is drawn only when a player win empties the Bankroll. `InitialLoanAmount` (`40,000`) is the on-demand loan-draw chunk and the `AutoLoanAmount` default. **Casino = exact mirror of an average player (canonical, CG.3.D):** loan `40,000` (a player's total start) + dose `100` (a player's Bankroll) ⇒ first funding lands the casino at `39,900` Main / `100` Bankroll, the player's own split. This also mirrors the player's pre-genesis lifecycle (all balances reset to canonical defaults on every restart until a real block commits them — see `BlockSessionCheckpointService`). `AutoLoanAmount` (the loan chunk) and `BankrollTarget` (the dose) are dev-configurable in `CasinoGamblingFinances` and revert to these defaults pre-genesis, sticking only at a block.
- `ApplyBetResult(casinoDelta)` is the single write path: called after every settled **player** bet with `casinoDelta = -betEvent.CreditedProfit` (player loss → casino gains; player win → casino pays) — by **`SimulationService`** for autobet **and** by **`DiceGame.ExecuteBet`** for manual bets (manual bets don't flow through `SimulationService`). Bot bets do not route through it yet (OQ-11.1, deferred).
- **On-demand fixed-dose auto-recharge**: the Bankroll fluctuates freely with each bet result; only when it reaches ≤ 0 does `TryAutoRecharge()` fire, injecting exactly **one `BankrollTarget` dose** from Main Balance (looping only if a single win exceeds a whole dose). The winning payout that drove the Bankroll negative is absorbed by the recharged Bankroll, **not** by Main — Main only ever loses one dose per injection (NOT "target-to-fill", which wrongly made Main pay dose + payout).
- **Bankruptcy flavor event**: if Main Balance can't cover a dose, the bank injects an `AutoLoanAmount` (default `40,000 SC`) loan directly (`LoanCount++`, `TotalLoaned += AutoLoanAmount`) before completing the recharge — the game never blocks a bet on casino insolvency.
- `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` is the casino's P/L metric — positive when the casino is ahead of all loans taken so far. Pre-loan it reads `0` naturally (all-zero balances); after a pure loss streak it correctly reads the player's net loss as casino profit.
- Persists to `user://casino_sc_balance_state.json`. Extends `BlockSessionCheckpointService` (casino SC — `MainBalance`/`Bankroll`/`BankrollTarget`/`LoanCount`/`TotalLoaned` — is snapshotted/restored at each block, consistent with "a block is the only commit to disk"), and resets to the all-zero defaults on every pre-genesis restart via `ResetToPreGenesisDefaults()`.
- DEV-only — never surfaced in player-facing UI. See `Screens/CasinoGamblingFinances/CasinoGamblingFinances.cs` and `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md`.

### `CasinoClientLedgerService`
**Location**: `Scripts/Services/CasinoClientLedgerService.cs`

Tracks each casino client's SC deposit/withdrawal history from the casino's operational perspective (Step 11). Forward-compatible for multiple clients (currently just `"player"`); prerequisite for the since-last-deposit metrics in `ClientsBetsHistory` and the full transaction list in `ClientsTransactions`.

- `LedgerEntry.Kind` ∈ `"initial"` (the player's starting `40,000` stake, recorded once on first launch / re-established on each pre-genesis reset), `"deposit"` (SC entering Main Balance from outside — since Step 12 this is a **Bank → Main deposit** made in `ScFinances`, replacing the retired `DepositPopup`), `"auto_recharge"` (internal Bankroll Auto-Recharge — **not** a real deposit), `"withdrawal"` (**Main → Private Bank Account**, SC leaving the casino — Step 12), and `"bankroll_withdrawal"` (the **internal** Bankroll → Main movement — re-kinded in Step 12 so the plain `"withdrawal"` kind now means only real client↔casino outflows). Each entry also carries **`Method`** (`"manual"` | `"auto"`, Step 12 / D-SF2.3) so automatic and player-initiated flows are distinguishable without new kinds.
- Only `"initial"`/`"deposit"` entries reset the since-last-deposit baseline (`GetLastDeposit`) and count toward "Total SC deposited" in `ClientsTransactions` (auto-deposits reset it too — they are real SC re-entering play, D-SF2.2); `"auto_recharge"` and `"bankroll_withdrawal"` are internal movements — recorded for operator visibility (DEV scenes) but excluded from the deposited/withdrawn totals.
- Persists to `user://casino_client_ledger.json`; **checkpoint-covered** since Step 12 (snapshotted at each block, player entries cleared + `initial` re-established on each pre-genesis reset — D-SF2.4). See `Documentation/GLOSSARY.md` for the SC Deposit / SC Withdrawal / Bankroll Auto-Recharge / Bankroll Manual Recharge distinctions, and `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md` OQ-11.6.

### `PlayerBankAccountService`
**Location**: `Scripts/Services/PlayerBankAccountService.cs`

Owns the player's **Private Bank Account** — an **optional SC reserve outside the casino** (Step 12). Unlike the casino's credit relationship (`CasinoScBalanceService` draws loans), the player **owns** this money; the bank account is a savings/reserve they opt into. **Starts EMPTY (`0`)** — the canonical `40,000` stays in Main Balance, funded exactly as today (D-SF3.1) — and **all its automation defaults OFF**, so a new player can ignore the bank entirely for the first in-game months/years and play pure Main↔Bankroll.

- **Four transfer flows**, all built and functional now: manual/auto **deposit** (`TriggerManualDeposit` / `TryAutoDeposit`, Bank → Main, bring the reserve back into play) and manual/auto **withdrawal** (`TriggerManualWithdrawal` / `TryAutoWithdraw`, Main → Bank, park a reserve safe from the casino). Mutates `PrincipalBalanceService` for the Main side; **never** touches the Bankroll (that stays `BankrollProgramService`'s job).
- **Settings** (all revert pre-genesis, stick only at a block): `BankAccountBalance` (starts `0`), `AutoDepositEnabled` (default **OFF** — banked SC is a *safe reserve*, D-SF3.2), `AutoDepositAmount` (`1,000` refill chunk), `AutoWithdrawEnabled` (OFF), `AutoWithdrawThreshold` (`1,000` Main floor), `AutoWithdrawAmount` (`100` installment). Enabling Auto-Deposit / setting its amount is validated against the live bank balance (`0 < amount ≤ bank`).
- **`TryAutoDeposit` is a fallback, not the primary funding path** (D-SF3.3): normal early play funds Main→Bankroll exactly as today; the bank→Main auto-deposit only fires when Main can't cover a recharge **and** Auto-Deposit is ON **and** the bank holds SC — essentially never in early game. With the player opting in (banked reserve + Auto-Deposit ON at a valid amount), this fallback *is* the opt-in "extra-lazy" streaming.
- **`TryAutoWithdraw`** uses a threshold/surplus model with an anti-ping-pong floor (`max(AutoWithdrawThreshold, live recharge dose)`), moving one installment per trigger event — the shape `CasinoScBalanceService` can adopt for P6 repayments.
- **History**: one `BankTransferRecord` list (both directions, `Method` manual/auto, game-time `GameDateLocal`), capped at 500; player metrics `NetWorthSc` (`= Bank + Main + Bankroll`) and `OverallPl` (`= NetWorthSc − 40,000`) are computed in the **`ScFinances` controller** from the three balance sources (the service stays pure — D-SF2.7). Bank→Main deposits also register a `"deposit"` ledger entry (`CasinoClientLedgerService`); Main→Bank withdrawals register `"withdrawal"`.
- Persists to `user://player_bank_account_state.json`; **checkpoint-covered** (a `CheckpointState` DTO snapshotted at each block; `ResetToPreGenesisDefaults()` → bank `0` / settings default / history cleared on every pre-genesis boot — mirrors the casino's pre-genesis rule). Player-facing (managed in `ScFinances`). See `AIHelperFiles/step12-player-sc-finances-plan.md` and `Documentation/ProjectDesignManual.md` Ch. 32.

### `BtcMarketDataService`
**Location**: `Scripts/Services/BtcMarketDataService.cs`

Owns the **historical BTC/USD daily market data** (Step 13 MD.1, autoload #14) — loader + O(1) lookup service for `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv` (5,646 rows, one per UTC day, **2010-07-18 → 2025-12-31**; full provenance/caveats in `AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md` §1). Since SC is USD-pegged 1:1, `price_usd` **is** the SC price of 1 BTC.

- CSV loaded **once** in `_Ready()` into a day-number-indexed array; `_Process` does a single cached-date comparison against the game clock's local date and fires `event Action<MarketDay?> MarketDayChanged` when a day boundary is crossed (`null` payload = the new day falls outside the dataset). No timers, no per-frame parsing, no I/O after load.
- **`FirstDataDateLocal` (2010-07-18, Mt. Gox launch) is THE trading-unlock gate** — `IsMarketBorn(nowLocal)`; data-driven, never a second hardcoded date. Before it: no market, no price, swap desk locked (teaser only).
- Price model: **step function** (the day's VWAP holds all day, D-13.2). `GetEffectivePriceUsd` carries forward over the **13 real historical halt days** (`IsHaltDay`, `source == "none"` — the June 2011 Mt. Gox and Aug 2016 Bitfinex hacks; swap desk closes, last price shown greyed, D-13.11), freezes at the last price after 2025-12-31 (D-13.5), and returns `null` before market birth.
- **/100 fractal accessors (D-13.10)**: ALL gameplay consumption uses `GetGameVolumeBtc` (raw ÷ 100) and `GetGameNumTrades` (`max(1, round(raw ÷ 100))`); the raw `MarketDay` fields are DEV/provenance-only. `price_usd` is exempt from the /100 rule by decree — the single tolerated contradiction of the 1:100 fractal replica.
- Parsing rules: `CultureInfo.InvariantCulture`; blank cells ⇒ `null`, **never** `0`; Godot `FileAccess` on the `res://` path (the `.csv.import` is pinned to `importer="keep"` so Godot never re-imports it as a Translation resource). Consumers: BTCWallet valuation line, StatusBar BTC ticker (refreshes only on `MarketDayChanged`), ScFinances dual-mode BTC label, `CasinoCoinSwapService`.
- Read-only over a static asset — no persisted state, no checkpoint coverage needed.

### `CasinoCoinSwapService`
**Location**: `Scripts/Services/CasinoCoinSwapService.cs`

Owns the casino's **swap desk** (Step 13) — the casino-as-dealer exchange where the player trades SC for BTC and back, at the `CasinoCoinSwaps` scene. Autoload registered **before** `BlockSessionCheckpointService` in `project.godot` (same ordering reason as `PlayerBankAccountService` — it must already be in the tree when the checkpoint restore / pre-genesis reset runs at boot).

- **Two panels, one availability rule**: Panel A ("Buy BTC", SC→BTC) and Panel B ("Sell BTC", BTC→SC). Both obey `OfferedForSwap(asset) = max(0, CasinoBalance(asset) − Reserve(asset))` — the casino only ever offers BTC/SC it actually owns; see `OfferedBtc` / `OfferedSc`. **BTC's offered figure is computed from `CasinoBtcEquity`, never the raw wallet balance** — a casino-pool-mined block's coinbase is mostly the pool *contributors'* money (the casino keeps only its fee share), so `GetCasinoBtcSettlement()` (in `NetworkRoot`) separates the casino's own settling fee share (`CasinoBtcSettling`, shown but not offered) from any unbacked pool-payout obligation (`CasinoBtcPoolObligation`, subtracted from equity) — this is what stops the desk from ever offering another party's BTC.
- **Per-asset `ReserveSetting`** (`BtcReserve`/`ScReserve`, %-of-balance or flat amount, default 0 ⇒ 100% offered) — mutated ONLY through `SetBtcReserve`/`SetScReserve` so a future auto-swaps-scheduler can call the identical API the DEV knobs use. The **BTC** reserve knob lives in `CasinoFinances` (a wallet-level property, %-of-**whole-wallet** including change addresses); the **SC** reserve knob lives in `CasinoGamblingFinances`.
- **SC auto-floor (R2, "recharge pace")**: `ScAutoFloor = SafetyFactor × dosesConsumedInWindow(WindowDays, game time) × BankrollTarget`, reading `CasinoScBalanceService.RechargeHistory` (zero new telemetry). `EffectiveScReserve = max(manual reserve, auto floor when enabled)` — same anti-ping-pong `max()` shape as `PlayerBankAccountService.TryAutoWithdraw`. Toggle + tunables (`ScFloorEnabled`/`ScAutoFloorSafetyFactor`/`ScAutoFloorWindowDays`, defaults OFF / 1.5 / 1 day) live in `CasinoGamblingFinances` beside the SC reserve knob, with a live breakdown readout (`GetScAutoFloorDosesConsumedFor(windowDays)`) showing the formula's three factors before Apply, and the swap-desk info line names which side of `max(manual, auto)` currently binds. Full rationale + the documented (unbuilt) R3 drawdown-based alternative: `Documentation/ProjectDesignManual.md` Ch. 33.
- **Rank-ready swap fee**: one percent (`SwapFeePercent`, default `10%`, clamped `1%–10%`) governs BOTH directions. **Additive model (2026-07-08, D-SW.11, supersedes the original inclusive D-SW.1)**: the casino's own cut is `casinoFee = fee×(base+MinFee)` (uncapped) — a cut ON TOP of the flat 0.1 BTC network fee, summed, never `max()`'d; `totalFee = networkFee + casinoFee`. The casino's real margin is therefore always at or above the nominal %, not below it, UNLESS capped (see D-SW.12 below). Linear inversion `BaseFromNet(targetNet, fee)` replaces the old piecewise `MaxGrossForNet` everywhere (Max-clamp math, reverse "receive X" quotes, the minimum swap size estimate). Every quote/execution reads the fee through `GetSwapFeePercentFor(clientId)` — the single hook a future rank system overrides. DEV knob in `CasinoGamblingFinances`.
- **Max fee deviation cap (D-SW.12, same day)**: dev-configurable `MaxFeeDeviationPoints` (default `2.0`, clamped `[0,20]` points, `SetMaxFeeDeviationPoints`) caps the CASINO'S OWN CUT (never the flat network fee, which is always charged in full) at `[0, (fee+points/100)×gross]`: `casinoFee = max(0, min(fee×(gross+MinFee), (fee+points/100)×gross))`. Capping the combined total instead (an earlier, rejected design) creates an unavoidable floor/ceiling conflict for small-enough swaps (the flat network fee alone can exceed nominal+points% of a tiny gross) — capping only the casino's cut has no such conflict, since `[0, ceiling≥0]` can never be unsatisfiable. Below the crossover (`gross = fee×MinFee/maxDeviationFraction`, ≈0.5 BTC at the 10%/2pt defaults) effective margin holds flat at exactly `nominal+points`; above it, the uncapped additive formula governs and margin decays toward nominal as gross grows. DEV knob beside `SwapFeePercent` in `CasinoGamblingFinances`.
- **Quotes are pure**: `QuoteScToBtc`/`QuoteBtcToSc` (the UI calls these per keystroke) return a `SwapQuote` with gross/fee/net figures, the binding `MaxInput` + which side's balance limits it (`MaxLimitedBy`), and the §3.1a minimum swap size. The minimum was recalibrated twice under the additive model, same day: first to the smallest base whose net delivery is a single satoshi (`BaseFromNet(OneSatoshi, fee)`, ≈0.1222 BTC at 10%) — then, since that let a swap through paying almost 100% in fees for a few satoshi, redefined to a **VALUE floor**: `net(base) ≥ totalFee(base)` (the player must net back at least as much as they pay in fees), `MinSwapGrossBtcFor(fee) = 2×NetworkFeePolicy.MinFee×(1+fee)/(1−2×fee)` — **≈0.275 BTC at the 10% default**. `MinDeliverableBtc`/`MinScPayoutAt` (the panels' enable thresholds) are fee-dependent live computations under this floor (not fixed constants — they move with `SwapFeePercent`). The Max-clamp (`casinoMaxSc`/`casinoMaxBtc`) is floored at the (truncation-safe) `MinInput` whenever the panel is enabled, since two independently-truncated formulas (the panel-enable gate vs. the Max-clamp inversion) can otherwise disagree by a few satoshi right at the boundary.
- **Execution**: `TryExecuteScToBtc`/`TryExecuteBtcToSc` re-gate on fresh state and `Math.Clamp` the input into `[MinInput, MaxInput]` in BOTH directions (an over-max or under-min positive amount silently executes at the clamped bound, never rejected). Panel A's SC legs are instant, then the casino→player on-chain BTC send goes out (to the player's **base address** — no fresh-address-per-swap); a failed broadcast unwinds both SC legs. Panel B runs the OPPOSITE order — the player's own on-chain send goes first (so a failed broadcast needs no rollback), then the SC credit fires instantly without waiting for confirmation (an app restart before that block reverts the mempool send and the SC credit together, so this carries no real risk). In-flight BTC legs are tracked in an in-memory-only `PendingBtcDeliveries` list (deliberately not persisted — restart unwinds both legs) and shown as a ⏳ row until the confirming block lands.
- Ledger entries `"swap_sc_out"` (Panel A) / `"swap_sc_in"` (Panel B) on `CasinoClientLedgerService` — excluded from deposited/withdrawn totals and the since-last-deposit baseline by construction. Swap-desk on-chain sends carry a display memo (`Transaction.InputDataText`, excluded from the txid/sighash) so wallet history panels (CasinoFinances/BTCWallet/FoundersWallets) render them distinctly (aqua, "· SWAP").
- Persists to `user://casino_coin_swap_state.json` + a `swap_desk_trace.csv` telemetry log (one row per swap/knob change, mirrors `founders_trace.csv`); **checkpoint-covered** (`CheckpointState` DTO; `ResetToPreGenesisDefaults()` → reserves 0 / fee 10% / auto-floor OFF / history cleared on every pre-genesis boot). See `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md`.

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
- **Block cap**: 24 transactions per block (`BlockTemplateBuilder.MaxBlockTransactions`, counting the coinbase — implemented)
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
│   ├── ScFinances/             # Player SC-flows hub + ScTransactions (Step 12)
│   ├── CasinoGamblingFinances/ # Casino SC finances [DEV] + ClientsBetsHistory/ClientsTransactions
│   ├── CasinoCoinSwaps/        # Casino swap desk — SC↔BTC (Step 13)
│   └── Shared/                 # Reusable UI components
│
├── Scripts/                    # Core logic (~50 C# files)
│   ├── Services/               # Autoload singletons (16 services)
│   ├── Betting/                # Strategy config, interface, progression logic
│   ├── Sessions/               # Bet loop controllers (Base, Auto, Manual)
│   ├── Dice/                   # DiceEngine, DiceResult
│   ├── Finance/                # Wallet, Money, Transaction, BetTransactionEvent
│   ├── Game/                   # BetService, IBetEventSource
│   ├── History/                # BetHistoryRepository, BetRecord, stats, PlayerFinancialStatsCalculator
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
│   ├── FinancialBettingStats/  # Compact 3-scope betting stats (redesigned Step 12); reused in DiceGame + ScFinances
│   └── StatusBar/              # (DepositPopup/ retired in Step 12 — deposits now flow through ScFinances)
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
| Specific split | `39,900 SC Main Balance + 100 SC Bankroll` (unchanged by Step 12 — the `40,000` stays in the casino accounts, funded as today) |
| Private Bank Account (Step 12) | Starts at `0` — an **optional SC reserve outside the casino**, all automation OFF by default. The player *owns* it (no debt); withdraw Main→Bank to park SC safe, deposit Bank→Main to bring it back. Managed in `ScFinances`. See `PlayerBankAccountService` |
| Player-facing term | `Main Balance` (not "Principal Balance") |
| Game over condition | `Private Bank Account + Main Balance + Bankroll = 0` (Step 12 / D-SF2.1 — total ruin across all three SC accounts; while the bank holds anything it is **not** game over, since the player can always deposit it back). Written to leave room for a future **BTC→SC coin-swap escape hatch** (§7.4) — the check must be interceptable by a later exchange layer, not an irreversible terminal |
| Current mining rule | `1 bet = 1 nonce attempt` |
| Basic Mode halving | `2,100 blocks` (≈ 4 in-game years at 100X scale) |
| Total BTC supply | `210,000 BTC` — converges to in-game year ~2141 |
| Real Bitcoin halving | `210,000 blocks` — NOT used in Basic Mode |
| Block transaction cap | `24 transactions` ✅ **Implemented** — `BlockTemplateBuilder.MaxBlockTransactions = 24`, counting the coinbase (coinbase + up to 23 mempool txs). Fits the 1:100 fractal replica (real blocks carry ~2,000–3,000 txs) |
| BTC/SC trading unlock (Step 13) | **2010-07-18** (Mt. Gox launch — the first date of `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv`). Before that in-game date there is no market, no price, and no swap UI beyond a locked teaser. The gate is **data-driven** (`BtcMarketDataService.FirstDataDateLocal`), never a second hardcoded date. In the canonical timeline the player waits ~484 in-game days (~715 blocks) from the 21 Mar 2009 start — accepted and historically honest (BTC accumulates from mining long before it becomes tradable) |
| Timeline (DEV alt-timeline guard, Step 13) | The canonical timeline (genesis `2009-01-03`) is the ONLY shipping timeline: `TimelineConfig.DevAltTimeline` is **`false` on `main`, forever**. All historical date anchors route through `TimelineConfig.Shift()`; `user://world_timeline.stamp` + `NetworkRoot.ResetWorldIfIncompatible()` auto-wipe the world (full delete list, D-13.7) on any timeline switch, both directions — run by `WorldGuardService`, the **first** autoload, so the wipe precedes every state-file load (ordering is load-bearing; new persisted world-state files MUST be added to the delete list). The DEV simulacrum (+484 days, landing 2010-07-18) is a throwaway dev scaffold — re-mount guide: `Documentation/ProjectDesignManual.md` Ch. 35 |
| Hardware cap | `100 nonce attempts` per time cycle (planned) |
| Network fee activation | `~2009-04-26` nearest block ✅ **Implemented** — whole network **fee-free before**, all participants (bots/casino/player) pay fees **after**; `NetworkFeePolicy` is the single source of truth. See `AIHelperFiles/step10-network-fee-activation-plan.md` |
| RTP | `99.02%` |
| Number format | `1,000,000.00000000` — comma=thousands, period=decimal (`CultureInfo.InvariantCulture`); never use raw `:N8`/`:F2` in string interpolations |
| Currency for betting | SC only — BTC cannot be wagered directly |
| Casino SC defaults (mirror of an average player) | Casino auto-loan chunk `40,000 SC` (`= InitialLoanAmount`, a player's total start) + bankroll dose `100 SC` (`= DefaultBankroll`, a player's Bankroll). Extra-lazy first funding then reproduces the player's `39,900 Main / 100 Bankroll` split. Dev-configurable (`AutoLoanAmount` / `BankrollTarget`), reverts to these defaults pre-genesis. (CG.3.D) |
| Founders | Satoshi (target `11,000 BTC`, retires ≥ `2011-04-26`, then frozen) + Hal (`P=1.0` drip, fades to 0 by `2009-08-09`) + Mike Hearn (joins ~Apr 2009, never mines, +82.51 BTC round-trip) |
| Player start | `21 Mar 2009` after the first-launch bootstrap, at the **exact same timestamp** as the bootstrap's last mined block (`HistoricalBootstrapService`) — no dead/idle time, no offset. This is a specific case of a general rule: **the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world state.** Every checkpoint capture (`BlockSessionCheckpointService.CaptureCheckpoint`) reads the clock synchronously right after mining, so this holds automatically post-first-block; pre-genesis, `BlockSessionCheckpointService.ResetToPreGenesisDefaults()` re-derives it the same way from the chain tip. See `Documentation/ProjectDesignManual.md` §24.9 |
| Swap desk fee (Step 13) | `10%` default, dev-clamped `1%–10%` (`CasinoGamblingFinances`), governing **both** swap directions. **Additive** (D-SW.11, 2026-07-08): `totalFee = networkFee + casinoFee` where `casinoFee = fee×(base+0.1 BTC)` — the network fee is a SEPARATE charge summed with the casino's cut, not absorbed inside it. **Max fee deviation cap** (D-SW.12, same day): `MaxFeeDeviationPoints` (default `2.0`, clamped `[0,20]` points) caps the casino's cut alone at `nominal+points`% effective margin — the network fee is never capped, always charged in full. Bought BTC always lands on the player's **base address** (no fresh-address-per-swap, D-SW.6). See `CasinoCoinSwapService` and `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` |

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
- Player SC Finances hub + Private Bank Account (Step 12): new `PlayerBankAccountService` autoload (#13) — an optional, initially-empty SC reserve outside the casino with four transfer flows (manual/auto deposit Bank→Main, manual/auto withdrawal Main→Bank), all automation OFF by default; checkpoint-covered + pre-genesis reset. New player-facing `ScFinances` hub (balances, Net Worth / Overall P/L, deposit/withdraw sections, 3-scope betting stats, transfer history) + `ScTransactions` (own bank↔Main ledger view). `BankrollProgramService.AutoRechargeEnabled` off-switch (BankrollProgrammer toggle + DiceGame StrategyControlPanel toggle now a proxy to it). `CasinoClientLedgerService` gains `Method` (manual/auto) + `bankroll_withdrawal` taxonomy fix + checkpoint coverage. `DepositPopup` retired (Deposit button → ScFinances). `FinancialBettingStats` redesigned compact (3 scopes: general / since deposit / since recharge) via shared `PlayerFinancialStatsCalculator`, reused in DiceGame + ScFinances; DiceGame bet-history list now seeds from the persistent store on entry. Window starts Maximized (`window/size/mode=2`, `mode.editor=0` for editor embedding). See `AIHelperFiles/step12-player-sc-finances-plan.md` + `Documentation/ProjectDesignManual.md` Ch. 32
- Game-over redefined to total ruin across all three SC accounts (`Private Bank Account + Main Balance + Bankroll = 0`, D-SF2.1)
- Casino swap desk (Step 13, `CasinoCoinSwapService` autoload #15 + `CasinoCoinSwaps` scene): casino-as-dealer SC↔BTC trading — Panel A (Buy BTC, SC→BTC) and Panel B (Sell BTC, BTC→SC), both executing real on-chain BTC sends + instant SC legs, gated by live availability (market-birth/halt-day/settling/no-funds states) and never offering more BTC/SC than the casino truly owns (pool-payout-aware BTC equity vs. settling separation). Rank-ready 10% swap fee (1–10% dev range), **additive** to the flat network fee (D-SW.11) with the D-SW.12 max-deviation cap on the casino's own cut; per-asset strategic reserves (manual %/amount + an SC auto-floor, "R2 recharge pace") composed as `max(manual, auto)`; swap ledger kinds `swap_sc_out`/`swap_sc_in`; on-chain swap txs tagged with a display memo and rendered distinctly in wallet history panels. See `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` and `Documentation/ProjectDesignManual.md` Ch. 33–34
- BTC market data, canonical trading unlock & DEV alt-timeline machinery (Step 13 MD/TL): `BtcMarketDataService` autoload (#14) loading `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv` (5,646 days, Mt. Gox/Bitfinex/Binance provenance, 13 preserved halt days, step-function daily price, /100 fractal volume/trade accessors); player-visible surfacing (BTCWallet valuation line, StatusBar BTC ticker, ScFinances dual-mode BTC label + toggle — with the SC-only game-over metric keeping top visual prominence, D-13.3); **trading unlocks data-driven at 2010-07-18**; `TimelineConfig` (every historical date anchor routed through `Shift()`; `DevAltTimeline` false on `main` forever) + the generalized world-incompatibility guard (`NetworkRoot.ResetWorldIfIncompatible`, `user://world_timeline.stamp`, full delete list D-13.7) that auto-wipes the world on any timeline or format switch. The DEV simulacrum (+484 days, landing on 2010-07-18) hosted the swap-desk development and was exited at TL.3. See `AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md` + `Documentation/ProjectDesignManual.md` Ch. 35 (the simulacrum re-mount / new-bootstrap design guide)

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

**Within a session**, the live clock, balances, and mempool advance and survive scene changes — the autoloads and the **static** `NetworkRoot` hold them in memory. **Nothing between blocks is persisted to disk** — not SC balances, not the chain, not the mempool. Between-block navigation / node-switch saves use `SaveActiveNodeFinancialState(false)` (in-memory only), and BTC transactions / consensus do not persist either (`NetworkRoot.CreateAndBroadcastTransaction`/`CreateAndBroadcastTransactionToAddress` only mutate the in-memory mempool). `PersistStateToDisk()` runs **only** at block-mining (`HandleMinedBlock`), baseline node creation, and startup; the player's block-commit financial write goes through `SimulationService.CaptureCheckpoint` / `DiceGame.CaptureBlockCheckpoint`. Consequently an **app restart reverts the whole world to the last mined block** — clock, every participant's balances, **and** un-mined pending transactions — performed at startup by `BlockSessionCheckpointService.ApplyCheckpointToServices()`. Within-session re-entry must never rewind the clock: `DiceGame` skips `EnsureGameEpochInitialized()` while `SimulationService.IsRunning`, and the checkpoint clock/history restore is a once-per-process operation guarded by the static `_checkpointRestoreSpentThisSession`.

**This principle applies to EVERY player-facing persisted value, not just the four services `ApplyCheckpointToServices()` lists** — `BankrollProgramService` (dose + transfer records), the game clock, and the bet-history log (`UserStatsService`) all self-persist eagerly (on every dose change / bet / recharge) and MUST be explicitly included in both the post-first-block checkpoint restore and the pre-genesis reset below, or they silently leak uncommitted state across a restart. When adding a new player-facing autoload or persisted list, ask: "does this need a `BlockSessionCheckpointService` restore path (post-block) AND a `ResetToPreGenesisDefaults()` path (pre-block)?" — if it holds player state that changes outside of a mined block, the answer is yes. **And a third question (TL.3 lesson): "is its `user://` file in the `NetworkRoot.ResetWorldIfIncompatible()` delete list?"** — every persisted **world-state** file must be, or it leaks across a format/timeline clean reset (`casino_coin_swap_state.json` missed this and alt-world hardware/pool state survived a timeline wipe). Identity/personal files (wallet seeds, bot registry, notepad, saved strategies) are deliberately exempt.

**Pre-genesis (no block has EVER been mined — only the historical bootstrap has run)**: a checkpoint is captured **only** by a real block-mined event now (`DiceGame.CaptureBlockCheckpoint()` / `SimulationService.CaptureCheckpoint()`) — never merely by opening the app (there is no more "baseline" auto-capture). Whenever `BlockSessionCheckpointService.HasCheckpoint()` is false, `ResetToPreGenesisDefaults()` runs on every boot instead of `ApplyCheckpointToServices()`, forcing Main Balance/Bankroll/dose/transfer records back to their true canonical defaults, and resetting the calendar + bet history to the historical bootstrap's landing instant (re-derived from the chain tip via `NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()` — before any real block, the tip *is* the bootstrap's last block, so nothing extra needs to be persisted for this). **Canonical rule**: the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world — never offset, not even by one second (every checkpoint capture reads the clock synchronously right after mining, so this is naturally true post-first-block; the pre-genesis reset and the historical bootstrap's player-start instant both follow the same rule deliberately). See the Canonical Decisions table above ("Player start") and `Documentation/ProjectDesignManual.md` §24.9.

**Canonical rule — game time, never wall-clock, for anything the player can see or that gets persisted.** Every event timestamp that is displayed, stored in a `TransferRecord`/`LoanRecord`/`BetRecord`/ledger entry, or compared against a checkpoint boundary **must** come from `CalendarTimeService` (`.CurrentUtcDateTime` / `.CurrentLocalDateTime`) — **never** `DateTime.Now`/`DateTime.UtcNow` directly. An audit (2026-07-01, OQ-BP.10 in `AIHelperFiles/player-and-casino-bankroll-programmer-plan.md`) found this violated in several places already shipped earlier in the same plan — most seriously, `DiceGame`'s `BetService` timestamp provider used `DateTime.UtcNow` for **every manual bet**, which (since `RollbackHistoryToUtc`/`GetLoadedHistoryStats` compare bet timestamps against the game-time checkpoint boundary) would have silently corrupted the pre-genesis history-rollback fixes above for manual play. All such call sites were fixed to read `CalendarTimeService` (with a `?? DateTime.UtcNow` null-safety fallback only, never as the primary source). **The only legitimate use of real wall-clock time** is pure internal DEV/file bookkeeping metadata the player never sees (e.g. `BlockSessionCheckpointService.CapturedAtUtc`, each service's own `UpdatedAtUtc` snapshot field) or genuine real-time concerns unrelated to game-world state (`UserStatsService`'s 250ms UI-throttle timer, `DiceGame`'s real-bets-per-second rate-measurement fields). When adding any new timestamped record, ask: "is this game-world state, or pure DEV telemetry?" — if the player could ever see it, it's game time.

Full rationale and the bugs this resolved: `Documentation/ProjectDesignManual.md` §24.8 (post-first-block), §24.9 (pre-genesis + the exact-timestamp rule), and §24.10 (the wall-clock-vs-game-time audit).

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
- Should private mempool fees be available in Basic Mode or postponed?
- **Network fee market simulation (research priority, flagged 2026-07-08)**: `NetworkFeePolicy.MinFee` is a single hardcoded `0.1 BTC` constant for the whole post-activation timeline — historically naive, and every fee-dependent system (the swap desk, wallet sends) inherits that. Two candidate approaches to research: **Option A** (historical fee replay, mirroring `BtcMarketDataService`'s price-history architecture) vs. **Option B** (a reactive fee market derived from our own simulated mempool congestion + miner/tx-volume growth — which would track history indirectly for free, since that population growth is already planned historically). See `Documentation/PRIVATE_ROADMAP.md` §5 "Network Fee Market Simulation" and `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` §3.4 for the full comparison. Not scheduled — a dedicated research round, not blocking current work.

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

The example above omits several DEV-only scenes for brevity (e.g. `CasinoFinances`, `FoundersWallets`, `BotPlayHistory`). Step 11 added three more, all DEV-only: `CasinoGamblingFinances` (Main Menu → casino SC balances/loans/transfers), `ClientsBetsHistory` (→ from `CasinoGamblingFinances`, per-client P/L + live bet feed), and `ClientsTransactions` (→ from `CasinoGamblingFinances`, per-client SC deposit/withdrawal ledger) — see `Screens/CasinoGamblingFinances/`. Step 12 added two **player-facing** scenes: `ScFinances` (MainMenu → the player's SC-flows hub: Private Bank Account balances, deposit/withdraw, betting stats) and `ScTransactions` (→ from `ScFinances`, the player's own Bank↔Main transfer history) — see `Screens/ScFinances/`. Step 13 added the **player-facing** `CasinoCoinSwaps` (MainMenu + ScFinances → the casino's SC↔BTC swap desk; carries no DEV controls itself — its reserve/fee/auto-floor knobs live in the existing `CasinoFinances`/`CasinoGamblingFinances` DEV scenes) — see `Screens/CasinoCoinSwaps/`. `SceneManager.Go()` also records a one-deep `PreviousScene` for origin-aware back navigation.

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
│   ├── ScFinances          → Main Menu   (DiceGame's "Deposit Balance" button opens ScFinances; DepositPopup retired in Step 12)
│   ├── BankrollProgrammer  → Main Menu
│   ├── BlockExplorer       → Main Menu
│   └── CalendarsNavigator  → Main Menu / BetsHistoryExplorer
│       └── BetsHistoryExplorer → origin-aware back (Main Menu / CalendarsNavigator / ScFinances)
├── ScFinances [player-facing]  → Main Menu   (Step 12 — the player's SC-flows hub)
│   ├── ScTransactions              → ScFinances
│   ├── BetsHistoryExplorer         → (origin-aware back to its launcher)
│   ├── BankrollProgrammer          → ScFinances / (its own Main Menu back)
│   └── CasinoCoinSwaps             → origin-aware back (Main Menu / ScFinances)
├── CasinoCoinSwaps [player-facing]  → Main Menu   (Step 13 — the casino's SC↔BTC swap desk)
├── MartingaleCalculator (standalone, full-screen) → Main Menu
└── CasinoGamblingFinances [DEV]  → Main Menu
    ├── ClientsBetsHistory [DEV]    → Casino Gambling Finances
    └── ClientsTransactions [DEV]   → Casino Gambling Finances
```

`BetsHistoryExplorer`'s back button is **origin-aware** (Step 12 / SF.4.2): it returns to `SceneManager.PreviousScene ?? MainMenu`, so it goes back to whichever hub launched it (`CalendarsNavigator` or `ScFinances`).

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
- **Keep docs current on the branch where the work happens — including CLAUDE.md.** When a change alters the architecture, update CLAUDE.md (and the other docs) in the same branch/commits as the change, not deferred to merge. CLAUDE.md stays tracked — do not untrack it (its history matters and Claude Code reads it every session).
