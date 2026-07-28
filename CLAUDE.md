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

Seven core service singletons registered in `project.godot` (plus `SceneManager`, `NotepadService`, `FoundersMiningService`, `CasinoScBalanceService`, `CasinoClientLedgerService`, `PlayerBankAccountService`, `BtcMarketDataService`, `BtcNetworkDataService`, `CasinoCoinSwapService`, `ScMonetaryLedgerService`, `CentralBankService`, and `WorldGuardService`, documented in their own sections — **nineteen autoloads total**). They persist across all scenes and are accessible globally by class name. (`NetworkPopulationScheduler`, documented directly after `BtcNetworkDataService` below, is a pure static controller in the `FoundersMiningService`/`HistoricalEventScheduler` mold — driven per-frame by `SimulationService`, **not itself a registered autoload**.)

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
- **On startup**, `ApplyCheckpointToServices()` restores — `BankrollStateService`, `PrincipalBalanceService`, `BankrollProgramService` (dose + `AutoRechargeEnabled` toggle + transfer records), **`CentralBankService`** (per-client FED accounts — restored FIRST of the money services, since the casino reads its loan figures through it and the ledger reconciles against it), `CasinoScBalanceService`, `PlayerBankAccountService` (bank balance + settings + transfer history), `CasinoClientLedgerService` (entries), `CasinoCoinSwapService`, `ScMonetaryLedgerService` — **and** the game clock (+ the `_gamePresent` frontier) to the last block. This is the only place the clock reverts on app restart, so it applies before any scene loads. A block is the only commit to disk (see Important Pattern 2)
- **Pre-genesis (no checkpoint exists yet — no player/bot/founder block has ever been mined)**: `ResetToPreGenesisDefaults()` runs instead, on **every** boot — Main Balance → `40,000.00`, Bankroll → `0.00`, dose → `BankrollProgramService.DefaultAutoRechargeAmount` (+ `AutoRechargeEnabled` → ON), transfer records → cleared, **`PlayerBankAccountService` → bank `0` / settings default / history cleared, `CasinoClientLedgerService` player entries cleared + `initial` re-established** (Step 12), calendar → exactly the historical bootstrap's last mined block's timestamp (`NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()`, no offset — see Canonical Decisions), bet history → rolled back to that same instant. A checkpoint is captured **only** by a real block-mined event (`DiceGame.CaptureBlockCheckpoint()` / `SimulationService.CaptureCheckpoint()`) — never merely by opening the app — so the world genuinely resets to a first-launch state every restart until the player's first real block. See `Documentation/ProjectDesignManual.md` §24.9

### `SimulationService`
**Location**: `Scripts/Services/SimulationService.cs`

Owns the running **background simulation** so it survives scene changes. While a player autobet is active, this service ticks the player autobet **and** the bot runners in its own `_Process`, in every scene — bets fire, bots mine, time advances, balances change. DiceGame is a thin view/controller on top of it.

- **Single source of truth = `BankrollStateService`**: the service builds its **own** wallet/session (seeded from the bankroll, written back each settled bet), so its wallet has **no** scene-bound event subscriptions and freeing a scene cannot crash it. **Corollary (2026-07-15 fix): while a session is live, any external Main↔Bankroll mutation MUST go through the session wallet** — `TryManualTransferToBankroll`/`TryManualTransferToBalance` — because a plain `BankrollStateService.SetBalance` is clobbered by the next settled bet's write-back (this silently destroyed manually-recharged SC). `BankrollProgrammer` routes through these when `IsRunning`, falling back to the direct path when idle. Same-day companion fix in DiceGame's Active Node Selector: switching player→bot rewrites the shared services with the bot's balances, so switching **back** (and `_ExitTree` with a bot still active) must re-apply the player's `NodeFinancialState` mirror (`LoadActiveNodeFinancialState(restorePlayerFromMirror: true)`) — the plain player-guard early-return (correct on scene ENTRY, where the services are authoritative) had left the bot's balances masquerading as the player's after a switch-back. Same rule at both checkpoint-capture sites (`DiceGame.CaptureBlockCheckpoint` / `SimulationService.CaptureCheckpoint`): **a checkpoint always captures the PLAYER's financial state**, no matter which node is active/betting — with a bot active, the player mirror is swapped in for the capture and the bot's values re-applied after.
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
- `ApplyBetResult(casinoDelta)` is the single write path: called after **every settled client bet** with `casinoDelta = -betEvent.CreditedProfit` (client loss → casino gains; client win → casino pays) — by **`SimulationService`** for the player autobet **and** the bot runners (`ExecuteBotBet`), and by **`DiceGame.ExecuteBet`** for manual bets, player or bot-active alike (manual bets don't flow through `SimulationService`). **OQ-11.1 RESOLVED (Step 14 ND.8f, 2026-07-19)**: `bot_1..4` are first-class casino clients — the casino Bankroll fluctuates with all five clients' play. Perf note: bet-driven saves flush through a 0.5 s dirty-flag throttle in `_Process` (bots settle many bets/sec in the background; a restart restores from the block checkpoint anyway) — loans/transfers/setters still save immediately.
- **On-demand fixed-dose auto-recharge**: the Bankroll fluctuates freely with each bet result; only when it reaches ≤ 0 does `TryAutoRecharge()` fire, injecting exactly **one `BankrollTarget` dose** from Main Balance (looping only if a single win exceeds a whole dose). The winning payout that drove the Bankroll negative is absorbed by the recharged Bankroll, **not** by Main — Main only ever loses one dose per injection (NOT "target-to-fill", which wrongly made Main pay dose + payout).
- **Bankruptcy flavor event**: if Main Balance can't cover a dose, the bank injects an `AutoLoanAmount` (default `40,000 SC`) loan directly (`LoanCount++`, `TotalLoaned += AutoLoanAmount`) before completing the recharge — the game never blocks a bet on casino insolvency.
- `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` is the casino's P/L metric — positive when the casino is ahead of all loans taken so far. Pre-loan it reads `0` naturally (all-zero balances); after a pure loss streak it correctly reads the player's net loss as casino profit.
- Persists to `user://casino_sc_balance_state.json`. Extends `BlockSessionCheckpointService` (casino SC — `MainBalance`/`Bankroll`/`BankrollTarget`/`LoanCount`/`TotalLoaned` — is snapshotted/restored at each block, consistent with "a block is the only commit to disk"), and resets to the all-zero defaults on every pre-genesis restart via `ResetToPreGenesisDefaults()`.
- DEV-only — never surfaced in player-facing UI. See `Screens/CasinoGamblingFinances/CasinoGamblingFinances.cs` and `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md`.

### `CasinoClientLedgerService`
**Location**: `Scripts/Services/CasinoClientLedgerService.cs`

Tracks each casino client's SC deposit/withdrawal history from the casino's operational perspective (Step 11). **Multi-client for real since Step 14 ND.8f**: the five canonical clients (`CanonicalClients` — `player` + `bot_1..4`, the ND.8c genesis-grant set) each carry one `"initial"` 40,000 entry (`EnsureCanonicalInitialDeposits()`, idempotent — at boot, after a checkpoint restore, and on every pre-genesis reset), and the service also owns the **per-client bet-stats book** (`ClientBetStats`: bets/wins/losses/wagered/net-profit; `RegisterSettledBet`, in-memory with a 1 s dirty-flag flush — no per-bet I/O and no per-bet `LedgerChanged`) — the stats source for the bots' rows in `ClientsBetsHistory` (the player's row keeps reading `UserStatsService`). Bot auto-recharges (`SimulationService.TryAutoRechargeBot`) and bot auction payouts register per-bot entries. Known limitation OQ-ND8f.1: with a bot active in DiceGame's selector, `BankrollProgramService` recharges still ledger as `"player"` (pre-existing DEV-path misattribution). Drives the since-last-deposit metrics in `ClientsBetsHistory` and the full per-client transaction list in `ClientsTransactions` (both now render all five clients).

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
- **Rank-ready swap fee**: one percent (`SwapFeePercent`, default `10%`, clamped `1%–10%`) governs BOTH directions. **Additive model (2026-07-08, D-SW.11, supersedes the original inclusive D-SW.1)**: the casino's own cut is `casinoFee = fee×(base+networkFee)` (uncapped) — a cut ON TOP of the network fee, summed, never `max()`'d; `totalFee = networkFee + casinoFee`. **Since ND.7 (D-ND7.9) `networkFee` is the day's replayed MEDIAN for the current game date** (`CurrentNetworkFeeBtc`, public — the UI fee breakdowns read it too), threaded as a parameter through the pure static formula helpers; it can be 0 in the 2010-07→2011-04 zero-median era. The casino's real margin is therefore always at or above the nominal %, not below it, UNLESS capped (see D-SW.12 below). Linear inversion `BaseFromNet(targetNet, fee)` replaces the old piecewise `MaxGrossForNet` everywhere (Max-clamp math, reverse "receive X" quotes, the minimum swap size estimate). Every quote/execution reads the fee through `GetSwapFeePercentFor(clientId)` — the single hook a future rank system overrides. DEV knob in `CasinoGamblingFinances`.
- **Max fee deviation cap (D-SW.12, same day)**: dev-configurable `MaxFeeDeviationPoints` (default `2.0`, clamped `[0,20]` points, `SetMaxFeeDeviationPoints`) caps the CASINO'S OWN CUT (never the flat network fee, which is always charged in full) at `[0, (fee+points/100)×gross]`: `casinoFee = max(0, min(fee×(gross+MinFee), (fee+points/100)×gross))`. Capping the combined total instead (an earlier, rejected design) creates an unavoidable floor/ceiling conflict for small-enough swaps (the flat network fee alone can exceed nominal+points% of a tiny gross) — capping only the casino's cut has no such conflict, since `[0, ceiling≥0]` can never be unsatisfiable. Below the crossover (`gross = fee×MinFee/maxDeviationFraction`, ≈0.5 BTC at the 10%/2pt defaults) effective margin holds flat at exactly `nominal+points`; above it, the uncapped additive formula governs and margin decays toward nominal as gross grows. DEV knob beside `SwapFeePercent` in `CasinoGamblingFinances`.
- **Quotes are pure**: `QuoteScToBtc`/`QuoteBtcToSc` (the UI calls these per keystroke) return a `SwapQuote` with gross/fee/net figures, the binding `MaxInput` + which side's balance limits it (`MaxLimitedBy`), and the §3.1a minimum swap size. The minimum was recalibrated twice under the additive model, same day: first to the smallest base whose net delivery is a single satoshi (`BaseFromNet(OneSatoshi, fee)`, ≈0.1222 BTC at 10%) — then, since that let a swap through paying almost 100% in fees for a few satoshi, redefined to a **VALUE floor**: `net(base) ≥ totalFee(base)` (the player must net back at least as much as they pay in fees), `MinSwapGrossBtcFor(fee, networkFee) = 2×networkFee×(1+fee)/(1−2×fee)` — was ≈0.275 BTC under the retired 0.1 scaffold; under ND.7's live median it scales with history (≈0.00055 BTC at a 0.0002 median; a single satoshi swaps legally in the zero-median era). `MinDeliverableBtc`/`MinScPayoutAt` (the panels' enable thresholds) are fee-dependent live computations under this floor (not fixed constants — they move with `SwapFeePercent`). The Max-clamp (`casinoMaxSc`/`casinoMaxBtc`) is floored at the (truncation-safe) `MinInput` whenever the panel is enabled, since two independently-truncated formulas (the panel-enable gate vs. the Max-clamp inversion) can otherwise disagree by a few satoshi right at the boundary.
- **Execution**: `TryExecuteScToBtc`/`TryExecuteBtcToSc` re-gate on fresh state and `Math.Clamp` the input into `[MinInput, MaxInput]` in BOTH directions (an over-max or under-min positive amount silently executes at the clamped bound, never rejected). Panel A's SC legs are instant, then the casino→player on-chain BTC send goes out (to the player's **base address** — no fresh-address-per-swap); a failed broadcast unwinds both SC legs. Panel B runs the OPPOSITE order — the player's own on-chain send goes first (so a failed broadcast needs no rollback), then the SC credit fires instantly without waiting for confirmation (an app restart before that block reverts the mempool send and the SC credit together, so this carries no real risk). In-flight BTC legs are tracked in an in-memory-only `PendingBtcDeliveries` list (deliberately not persisted — restart unwinds both legs) and shown as a ⏳ row until the confirming block lands.
- Ledger entries `"swap_sc_out"` (Panel A) / `"swap_sc_in"` (Panel B) on `CasinoClientLedgerService` — excluded from deposited/withdrawn totals and the since-last-deposit baseline by construction. Swap-desk on-chain sends carry a display memo (`Transaction.InputDataText`, excluded from the txid/sighash) so wallet history panels (CasinoFinances/BTCWallet/FoundersWallets) render them distinctly (aqua, "· SWAP").
- Persists to `user://casino_coin_swap_state.json` + a `swap_desk_trace.csv` telemetry log (one row per swap/knob change, mirrors `founders_trace.csv`); **checkpoint-covered** (`CheckpointState` DTO; `ResetToPreGenesisDefaults()` → reserves 0 / fee 10% / auto-floor OFF / history cleared on every pre-genesis boot). See `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md`.

### `ScMonetaryLedgerService`
**Location**: `Scripts/Services/ScMonetaryLedgerService.cs`

Owns the **SC Monetary Ledger** (Step 14 ND.8c, autoload #18) — monetary-system **Option 0** of the fiat-debt ladder (D-ND8.30…35): a pure accounting service recording every event where SC **enters or leaves existence** (mint/burn). Flows between existing holders (bets, transfers, swaps, settlements) are NOT its job — those keep their own ledgers. Registered **before** `BlockSessionCheckpointService` (the `PlayerBankAccountService`/`CasinoCoinSwapService` boot-ordering precedent).

- **Standing invariant (D-ND8.35)**: `TotalCirculation = TotalGenesisGrants + TotalDebtOutstanding` — every SC in existence is a genesis grant or someone's debt.
- **Genesis grants** — the five canonical casino players (`player` + `bot_1..4`), each granted `40,000 SC` (equity: granted once, never repayable, never debt). Registered **declaratively** at the pre-genesis/first-run paths and re-established on every pre-genesis reset (the client-ledger `"initial"` precedent); the bots' balances materialize lazily in code (`GetOrCreateNodeFinancialState`) but the grant records the canon, not the lazy-init timing.
- **Loan draws** — ONE hook in `CasinoScBalanceService.AddLoanRecord` covers all three casino loan-draw sites (bankruptcy dose recharge, `PayFromMainWithAutoLoan`, dev manual loan): each draw mints its amount as `"casino"` debt, keeping the ledger in lockstep with `TotalLoaned` by construction. `RegisterBurn` exists but is caller-less — armed for **ND.8e** (Central Bank Option A: repayment destroys SC).
- **Simulates SC quantity/credit only, NEVER value** — the 1:1 USD peg is canon; monetary tightening is expressed as credit scarcity, never inflation/devaluation (Option C rejected forever).
- Event log capped at 500 (totals exact independently of the cap); event timestamps are game time. Persists to `user://sc_monetary_ledger.json`; **checkpoint-covered** (`CheckpointState` DTO restored AFTER the casino SC restore — a legacy null DTO initializes from live state: canonical grants + the casino's just-restored `TotalLoaned`, marked by one `init_sync` event; pre-genesis reset re-registers the grants at the player-start clock; file in the world-reset delete list). **No `WorldFormatVersion` bump** — accounting-only.
- DEV readout: the **`WorldEconomy`** scene (MainMenu → "World Economy [DEV]", D-ND8.25) — circulation/grants/debt totals, per-party breakdowns, mint/burn event log; the ND.8b.6 company inflow/expansion knobs (D-ND8.25) live in the same scene. See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §12.4.6e/§12.5.1 and `Documentation/ProjectDesignManual.md` §36.9.

### `CentralBankService`
**Location**: `Scripts/Services/CentralBankService.cs`

Owns the **Central Bank (FED)** (Step 15 P15.1, autoload #19) — the explicit in-world entity behind the SC the casino has always borrowed from. Registered **between `ScMonetaryLedgerService` and `BlockSessionCheckpointService`** in `project.godot` (it must be in the tree before the checkpoint restore/reset runs — the `PlayerBankAccountService`/`CasinoCoinSwapService` precedent — and the ledger it syncs into must be in the tree before it loads).

- **Two-layer debt architecture (D-15.23 "Fork A")**: the FED is the **entity/relationship** layer — the authoritative per-client store of `{ OutstandingDebt, TotalDrawn, TotalRepaid, DrawCount, RepayCount, History }` (`FedClientAccount`, history capped at the newest 500 per client, totals exact independently of the cap). `ScMonetaryLedgerService` stays the **macro accounting** layer (mint/burn log + `circulation = grants + debt`), kept in lockstep **for free**: `DrawLoan` calls its `RegisterLoanDraw` (mint), `Repay` calls its `RegisterBurn` (**burn — the ledger's first real caller**, armed and caller-less since ND.8c). Fork B (retiring `_debtByBorrower` so the FED is the single debt store) is a deferred optional cleanup, not scheduled.
- **The casino is now just another FED client** (P15.1c): `CasinoScBalanceService` keeps **no** loan state — `LoanCount`/`TotalLoaned`/`LoanHistory` are **read-through accessors** over its FED account (plus the new `OutstandingFedDebt`), and its three draw sites (bankruptcy dose recharge, dev `TriggerManualLoan`, provisional company provisioning) funnel through one private `DrawFedLoan`. `TotalLoaned` maps to **`TotalDrawn`** (cumulative) so `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` keeps its exact meaning; the ledger's reconcile compares against **`OutstandingDebt`** instead, because that is what "debt" means in the monetary invariant. The casino resolves the FED **lazily** (it registers earlier than the FED) and never in `_Ready`.
- **No interest, no credit limit (D-15.1)** — every `DrawLoan` succeeds, exactly as the casino's auto-loan always has; period-accurate for the ZIRP 2009–2015 window. The fed-funds-rate replay and per-client credit-capacity **limits** stay deferred to ND.8e, one layer *above* the banks. Clients: `"casino"` today; the four CB1 bank companies from P15.2, keyed `BankClientId(nodeId)` = `"bank:<companyNodeId>"`. The casino is the sole entity exempt from dissolution (D-15.17).
- Persists to `user://central_bank_state.json`; **checkpoint-covered** (`CheckpointState` DTO — **ordering is load-bearing**: the FED restores *before* the casino, which reads its loan figures through it, and *before* the ledger, whose reconcile/live-state init read the FED's casino account; `ResetToPreGenesisDefaults()` → no accounts, run before the casino's reset) and in the world-reset delete list. **`WorldFormatVersion` 3 → 4** (D-15.10) — the casino's loan fields left both its own JSON and the checkpoint DTO; every *later* plan15 file just joins the delete list, no further bump.
- DEV readout: the **`CentralBank`** scene (Main Menu → "Central Bank [DEV]", D-15.16) — per-client accounts, movement history, system totals, and an explicit FED/ledger **in-sync** marker on the monetary invariant. Event-driven (`CentralBankChanged` + `LedgerChanged` behind a 0.5 s dirty-flag coalescer, 5 s fallback), deliberately **not** a new Ch. 38 poll-migration candidate. See `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` §3.1/§8 and `Documentation/ProjectDesignManual.md` Ch. 39.

### `BtcNetworkDataService`
**Location**: `Scripts/Services/BtcNetworkDataService.cs`

Owns the **historical BTC network dataset** (Step 14 ND.1, autoload #17) — loader + O(1) lookup service for `Data/HistoricalNetwork/btc_network_daily_2009_2025.csv` (6,207 rows, one per UTC day, **2009-01-03 → 2025-12-31**; Coin Metrics community tier, cross-checked against blockchain.com; the ND.7.0 `fee_median_btc` column comes from Blockchair true medians + BitInfoCharts — provenance/caveats in `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §2/ND.0/§10.5). Mirrors `BtcMarketDataService` exactly: CSV loaded once (`EnsureLoaded()`, idempotent — also callable from a throwaway, never-scene-tree instance, the pattern EB.1's entry-year bootstrap uses), day-indexed array, O(1) `TryGetDay`, a `NetworkDayChanged` day-boundary event, read-only over a static asset (no persistence, no checkpoint coverage, no reset-list entry needed).

- **Derived accessors, all pure `date → value`** (registered after `BtcMarketDataService` in `project.godot` so `FirstDataDateLocal`/Market Birth is already available when these compute):
  - `GetDecades(date)` — `log10(hashrate(date) / hashrate(PlayerStartDayLocal))`, clamped ≥ 0, carrying forward over the genesis-week's null hashrate cells, frozen post-2025. This is the ONE scale anchor everything else below derives from.
  - `GetEraStandardPower(date) = EraMaxHardwareCredits ^ (decades(date) / decadesAtDatasetEnd)` — **the "what one historic miner is worth today" reference** (`EraMaxHardwareCredits = 100.0`). A pure function of the calendar date only — independent of the player's, founders', or any bot's actual live power (§6.1 of the step14 plan, confirmed by inspection).
  - `GetTargetVisibleMiners(date) = BaseCast (4) + CastPerDecade (2.0) × decades(date)` — the historically-scaled visible **cast** size target (spawn-drip target for `NetworkPopulationScheduler`).
  - `GetTotalNetworkUnits(date) = EraStandardPower(date) × GetTargetVisibleMiners(date)` — total network power at that date; `TotalNetworkUnits / TargetVisibleMiners = EraStandardPower` by construction, so the era-standard reference IS the network average at every date.
  - `GetTargetTxPerBlock(date)` — fullness-parity budget (real historical tx/block, non-coinbase, **not** re-subtracted — a Coin Metrics `TxCnt` quirk found at ND.0), `Math.Clamp [0, MaxBlockTransactions − 1]`.
  - `ComputeNonMinerIntroSchedule()` — precomputes the **referral-auction non-miner introduction dates** (D-EB.4/8) from the active-address curve, birth-anchored running-max: `1 + NonMinersPerAddressDecade (12.183693) × log10(runningMax / addressesAtBirth)`, capped at `NonMinerPoolSize (40)`. Pushed once, at load, into `NetworkRoot.SetNonMinerIntroSchedule` — the ONE shared schedule for canonical live play and the EB.1 entry-year fast-builds alike.
  - `ComputeAndPushFeeSchedule()` (ND.7) — builds the **Historical Fee Replay** schedule (per-day effective median/mean/max, D-ND7.4 per-component carry-forward, entries from Market Birth) and pushes it into the pure-static `NetworkFeePolicy.SetFeeSchedule` — same load-time push pattern (and same throwaway-instance compatibility) as the intro schedule.
- Anchors route through `TimelineConfig.PlayerStartDayLocal` (D-14.7 — an alt/entry-year world anchors `decades = 0` at its own landing day, for free).
- Parsing rules mirror `BtcMarketDataService`: `CultureInfo.InvariantCulture`; blank cells ⇒ `null`, never `0`; hashrate parsed as `double` (a physical measure consumed only via log-ratios — the raw strings exceed `decimal` precision and money rules don't apply); fees as `Money.Normalize`d `decimal` (consumed since ND.7 by the fee-replay schedule above).
- Consumers: `NetworkPopulationScheduler` (every accessor above), `NetworkRoot.SetScheduledTxTargetPerBlock` (via `SimulationService`), the EB.1 entry-year bootstrap (a throwaway `new BtcNetworkDataService()` + `EnsureLoaded()`, no autoload/scene-tree dependency needed — `NetworkRoot`/`FoundersMiningService` are equally instantiable this way, all either pure-static or holding zero meaningful instance state).

### `NetworkPopulationScheduler`
**Location**: `Scripts/Services/NetworkPopulationScheduler.cs`

The **historical network population scheduler** (Step 14 ND.2) — a plain `static class`, **not a `Node` and not registered in `project.godot`** (unlike every other entry in this chapter): no Godot/chain state of its own, driven per-block/per-frame by `SimulationService`, which feeds it the live facts (player+bot power, founders' power, the game date) and reads back powers + per-miner attempt counts — the `FoundersMiningService`/`HistoricalEventScheduler` pure-controller pattern, one step further (no `Node` base needed at all since `SimulationService` already owns the per-frame drive loop). Nothing persists — cast **identity** lives in `BotWalletRegistry.CastMiners` (reset-spared like all identity files); everything else re-derives from the game date + `BtcNetworkDataService` (D-14.7 — free time-shiftability, the same reason the EB.1 entry-year tool works with zero extra machinery).

Two hybrid layers make up the historical network (P-14.A):

- **Visible cast** — real, named, registry-backed miner bots (`BotWalletRegistry.CastMiners`, a THIRD list, deliberately never merged into `MinerBots`: cast miners join none of the betting-runner/donation-loop machinery `MinerBots` feeds — no SC finances, no bets, ND.2 v1). Spawned **at most one per block** ("spawn drip") as `GetTargetVisibleMiners(date)` grows; each powered member wields exactly `GetEraStandardPower(date)` — the era-standard reference. They mine **founder-style**: drained nonce attempts in lockstep with the player's own time advancement (`DrainScheduledAttempts`, the same accumulator pattern as `FoundersMiningService.DrainFounderAttempts`), concurrent miners, never clock movers. A chronological name pool (36 early-individual → pool-era handles, e.g. `artforz` → `foundry_usa`) gives spawns a historically-shaped flavor; exhaustion falls back to `miner_extra_N` (never expected — pool size 40 < max cast ~33 + BaseCast 4... margin intentional).
- **Invisible mass** — one aggregate power term, `max(0, TotalNetworkUnits(date) − playerBotsPower − foundersPower − castTotal)`, covering the REST of the historical network the game doesn't model individually. Its mined blocks are attributed to a **rotating ghost pseudonym** (`unknown_miner`, `garage_gpu`, … 12 names) — session-transient, one-off wallets whose BTC is frozen forever once mined (no stored keys survive a restart, D-14.11, the retired-Satoshi precedent for "coins nobody can ever move"). **Attribution is randomized** (ND.4a, 2026-07-10): `AdvanceGhostRotation()` draws the NEXT ghost name uniformly at random (and the initial index is randomized too) rather than a fixed round-robin — the fixed rotation kept all 12 names permanently tied in blocks-mined, which read as synthetic; a random draw spreads the SAME invisible-mass total organically. The invisible mass's power/attempt math itself is untouched by this — only which pseudonym gets credit for a given ghost block changes.
- **Per-block recompute** (`Recompute`, called once per new chain length): caches the era-standard power, the powered cast list, and the invisible mass for that block — read every frame by `SimulationService` until the next new block. `TotalScheduledPower = castTotal + invisiblePower` feeds the difficulty regulator's power input (alongside player+bot+founder power) so block pace stays historically shaped regardless of how the total splits across participants.
- **Per-frame drain budget** (`MaxScheduledAttemptsPerFrame = 5000`, accumulators capped at `10000`): an unbounded late-game drain (the scheduled mass can owe hundreds of attempts per player bet once `decades` is large) could stall a frame at high `DevTimeScale`; a sustained shortfall just slows blocks slightly, which the difficulty regulator's LWMA feedback then trims — self-correcting by design (Ch. 26).
- **Canon-safety at player start**: `decades(2009-03-21) = 0` ⇒ cast target = `BaseCast` (no spawns), `TotalNetworkUnits = 4 < live power` ⇒ invisible mass clamps to 0 ⇒ the scheduler is a complete no-op at the canonical start, identical to pre-ND.2 behavior; it wakes only as the historical curve grows through 2009+.
- **Automated transaction layer** (Step 14 ND.3/ND.4a, `NetworkRoot.ScheduleBotTransactionsAfterBlock`, not part of this service but driven by its data): a **fullness-parity budget** (`owed = max(0, GetTargetTxPerBlock(date) + fractionalCarry − pendingOrganicTxs)`, ND.4a-fixed to cancel organic demand BEFORE flooring) fills the mempool toward the historical tx/block target — organic traffic (player sends, swap legs, pool payouts) always counts first, automation only tops up the remainder, under-shooting accepted rather than synthetic filler (D-14.2). Two INDEPENDENT rotation cycles fill that budget/their own separate cadence (ND.4a, revised at ND.4b — see the Referral Auction canonical decision + ProjectDesignManual Ch. 22 §22.7 for the auction-specific cycle): **cast sell-flow** (`TryCastSellFlow`, historical-budget-governed, fair random rotation among `BotWalletRegistry.CastMiners`) and **non-miner↔non-miner exchanges** (`TryNonMinerExchanges`, real UTXO sends between funded holders, same budget). The **casino-miner-bots'** (`bot_1..4`) own donation/bid cycle (`TryCasinoBotDonation`) is INDEPENDENT of this budget entirely — see the Referral Auction entry below.
- DEV telemetry: `user://logs/network_population_trace.csv` (one row per live block — decades, cast target/powered/power-each, invisible power, player+bot/founder/total power, tx target, pending txs, spawned-this-block node id).
- See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` (the full plan, all rounds) and `Documentation/ProjectDesignManual.md` Ch. 36.

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
- **Balance model**: a **real multi-input/multi-output UTXO model** (Step 8 / Appendix A — implemented & in-engine audited). A `Transaction` holds `Inputs[]` (each an `OutPoint` + per-input signature) and `Outputs[]`; balance = Σ of an address's unspent outputs; fee = Σin − Σout. The **UTXO set** is rebuilt by replaying the chain (cached by `_chainVersion`, never persisted — consistent with "a block is the only commit"). One spend path `NetworkRoot.BuildAndBroadcastUtxoSpend` coin-selects owned UTXOs (exact match else largest-first **multi-input** combine) + change to a fresh derived address. **Address non-reuse** (a fresh derived address per receive/coinbase) is **Satoshi-only** (his ~220-address "one coinbase per address" spread). The **player, casino, Hal, and Mike Hearn** become multi-address only via **change outputs on send** (`ReceiveWallet` + `NodeAgent.RotateCoinbaseAddress = false` → coinbase/receives stay on base, change rotates); **only the bots stay single-address** (no stored seed — OQ-8.2). Hearn's one outgoing tx (E6b → Satoshi 32.51) is an exact-match send (no change), so his rotation is inert today — kept for consistency. E8 (17.49 Hearn change) is now a real change output. Legacy `Sender`/`Recipient`/`Amount` survive as read-only `[JsonIgnore]` shims — they expose only `Inputs[0]`/`Outputs[0]`, so **never use them to scan the chain for address membership** (a change output at `Outputs[1]` would be missed — the bug that made change-held funds vanish from wallets after a restart); iterate the full `Inputs`/`Outputs` lists instead. **And never use `tx.Sender` as a PARTICIPANT identity**: a spend whose coin selection consumed a change-address UTXO carries that derived address in `Inputs[0]` — the 2026-07-14 auction donor incident, where the player's bid displayed as an anonymous address. Resolve ownership through the node's full owned-address set (`BuildAuctionBidderIdentity` pattern; an address is a key, not an identity — `Documentation/ProjectDesignManual.md` §30.9). The account→UTXO switch used a **clean reset** (`WorldFormatVersion`). See `Documentation/ProjectDesignManual.md` Ch. 30 + `AIHelperFiles/step8-utxo-realism-plan.md` (Appendix A). NOTE: "Patoshi pattern" is a **misnomer** for this address mechanic — it is **address non-reuse**; the real Patoshi pattern is a mining-forensic fingerprint (D0).

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
│   ├── AuctioningCompanyDetails/ # Per-non-miner live tracked-donation pool while InAuction (Step 14 ND.5; forwards to CompanyDetails once founded)
│   ├── CompanyDetails/         # Founded company: stock summary + Board Vote / dividend panels (Step 14 ND.8b.4)
│   ├── WorldEconomy/           # SC Monetary Ledger readout + company inflow/expansion knobs [DEV] (Step 14 ND.8c/ND.8b.6)
│   ├── CentralBank/            # Central Bank (FED) per-client accounts + monetary invariant [DEV] (Step 15 P15.1e)
│   └── Shared/                 # Reusable UI components
│
├── Scripts/                    # Core logic (~50 C# files)
│   ├── Services/               # Autoload singletons (19 services)
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
| Network fees — Historical Fee Replay (Step 14 ND.7, 2026-07-13 — **supersedes Step 10's flat 2009-04-26 era, which is retired**) | **The fee era begins at Market Birth (2010-07-18), data-driven** — no fee exists anywhere on the network before it; from it the **real daily historical band** governs, replayed from the network dataset's `fee_median_btc`/`fee_total_btc` (fees are fractal-exempt — face value, never /100). Per day: **median** = every participant's base fee (send panels default-fill it; player clamp band `[median, max]`), **mean** = `fee_total ÷ tx_count`, paid ONLY by the cast miners' sell-flow (they ARE the network's average activity), **max** = `max(median, mean) × MaxFeeMeanMultiplier (10)` — a documented approximation, no real daily-max metric exists. Each component carries forward its last positive value across zero/blank days (D-ND7.4); the median has NO positive value before **2011-04-14 (= 0.01 BTC)**, so the effective median is an honest **0** from Market Birth through 2011-04-13 (most txs genuinely paid no fee then — the swap desk opens into a ~9-month zero-network-fee era, deliberate). `NetworkFeePolicy` stays the single source of truth: a pure static class holding the schedule pushed by `BtcNetworkDataService.EnsureLoaded()` (`SetFeeSchedule` — armed for EB.1 throwaway bootstrap instances too); no schedule ⇒ fee-free fallback + one warning, never the 0.1 scaffold back. The legacy `DefaultFee/MinFee/MaxFee` consts, `NetworkRoot.MinBotFeeBtc/MaxBotFeeBtc/CasinoTxFee`, and `TimelineConfig.FeeActivationLocal` (D-13.9, absorbed) are **deleted**. Fee semantics are world-defining ⇒ `WorldFormatVersion` 2 → 3 (clean reset). Median column provenance: Blockchair true medians (2010-07-18 → 2011-04-13) + BitInfoCharts USD median ÷ price (2011-04-14 →) — Coin Metrics' `FeeMedNtv` is paid-tier. See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10 |
| RTP | `99.02%` |
| Number format | `1,000,000.00000000` — comma=thousands, period=decimal (`CultureInfo.InvariantCulture`); never use raw `:N8`/`:F2` in string interpolations |
| Currency for betting | SC only — BTC cannot be wagered directly |
| Casino SC defaults (mirror of an average player) | Casino auto-loan chunk `40,000 SC` (`= InitialLoanAmount`, a player's total start) + bankroll dose `100 SC` (`= DefaultBankroll`, a player's Bankroll). Extra-lazy first funding then reproduces the player's `39,900 Main / 100 Bankroll` split. Dev-configurable (`AutoLoanAmount` / `BankrollTarget`), reverts to these defaults pre-genesis. (CG.3.D) |
| Founders | Satoshi (target `11,000 BTC`, retires ≥ `2011-04-26`, then frozen) + Hal (`P=1.0` drip, fades to 0 by `2009-08-09`) + Mike Hearn (joins ~Apr 2009, never mines, +82.51 BTC round-trip) |
| Referral auction (amended 2026-07-09 at EB.2, reworked into an ascending auction 2026-07-10 at ND.4b/ND.4c, refined same day at ND.4d, saturation ladder added 2026-07-12 at ND.6, bid-count-aware ladder + last-bid safety 2026-07-20 at ND.8d, stuck-single-bidder escalation 2026-07-21 at ND.8d round 3, escalation-engagement fix 2026-07-22 at ND.10a, bid-opportunity rework 2026-07-23 at ND.10c) | Non-miners (pool capped at **40**, raised from 10 at round 3 — D-EB.8) enter the auction from **Market Birth (2010-07-18)** along the historical active-address curve (`1 + 12.18 per address-decade` since birth; empirically all 40 by **2017-12-13**) — the 1-per-~2-days intro (`NonMinerIntroIntervalMs`) is retired. **Bidding is now a real ascending auction (D-ND4b.5-8, superseding EB.2's cumulative-donation-total model)**: a non-miner's first qualifying donation is pinned at a fixed **0.1 BTC** floor and activates its countdown; every later donation must clear a strictly higher floor to become the new leading bid, which RESETS the countdown to a fresh **20 in-game days** (D-ND4b.1, down from EB.2's 100-day/first-bid-only window) — a rolling window, not a one-shot timer. **The required floor is ASYMMETRIC (ND.4d, 2026-07-10)**: the casino-miner-bots `bot_1..4` still need `leadingBid + max(0.1, 10%×leadingBid)` (D-ND4b.6, unchanged); the **player's own** floor is a flat **1 satoshi (`0.00000001` BTC)** above the leader, regardless of its size — a deliberate one-sided exception (the player can always retake the lead as cheaply as possible, but a bot's very next raise still jumps the full 10–20% over that cheap bid, so a minimal player raise is easy to overtake; the risk is left for the player to learn empirically, never blocked in code). **Only casino players can bid (D-EB.7, unchanged): the player and the classic casino-miner-bots `bot_1..4`** (bet-driven mining = a real casino relationship); the much larger, historically-growing Step-14 cast (up to 29 additional / 33 total by 2025 — mines via drained attempts, never bets) does NOT qualify. `bot_1..4` bid on their own dedicated cadence (~1 donation-attempt per live block, weighted 0/1/2 count, D-ND4b.2/3 — fully independent of the historical fullness-parity budget the cast/non-miner-exchange cycles still use), topping every amount with a random additive tail so bids never repeat as round numbers (D-ND4b.11). **Saturation Ladder (ND.6, 2026-07-12, D-ND6.1…10 — supersedes ND.4b's plain soonest-to-expire targeting AND the interim 2026-07-11 top-5 hard filter, which had structurally stalled all bot bidding)**: each slot's fairly-drawn bot orders recruitable pools by ascending count of its OWN tracked slots (spread-wide first; ties soonest-to-expire), skips pools where it holds a top-3 tracked slot ("satisfied") or the smallest slot of a full pool (self-eviction guard — its own bid would evict its own smallest donation, forfeiting the secured settlement refund), walks them under a **half-spendable cap** (`required + tail + fee ≤ spendable × 0.5` — the ONLY failure that passes the slot to another bot; the decliner is not marked used-this-block), and re-bids a participated pool only on ONE probability roll at its best (lowest-probability) tier. **Two-mode ladder (ND.6d, 2026-07-14):** the applicable table is chosen per pool by its current occupied tracked-slot count — **NORMAL** (pool ≥ 7 slots): tier 4 → 5%, 5 → 8%, 6 → 13%, 7 → 21%, 8 → 34%, 9 → 55% (Fibonacci; tier-10 89% entry REMOVED — the guard makes it unreachable); **URGENCY (ND.6e, 2026-07-15 — Option B, D-ND6.10's pre-approved lever)**: while a NORMAL pool's rolling window is inside its **final 7 in-game days** (`IsAuctionInUrgencyWindow`) every tier shifts one Fibonacci level up — tier 4 → 8%, 5 → 13%, 6 → 21%, 7 → 34%, 8 → 55%, 9 → 89% (`UrgentReBidProbabilityPercentByTier`) — so challenges cluster into an organic late-window "sniping" phase, and an accepted raise (window resets to 20 days) drops the pool back to the calm table (the 2011-playtest round-2 fix: with 3 player-led MATURE pools bid scarcity had returned in NORMAL mode; early-rush pools ignore urgency, their table is steeper at every tier it has); **EARLY RUSH** (pool < 7 slots): tier 4 → 34%, 5 → 55%, 6 → 89% (no tier 7+ entry — a 7th slot IS the switch to NORMAL). The early rush is the 2011-playtest fix: the player's +1-satoshi retakes kept pushing contested bots' best slot up to tier 4/5 where the NORMAL 5%/8% roll left them declining ~95% of the time (trace: pure roll-declined, spendable ~1000 BTC — never an affordability problem) and the player won every referral uncontested; the steep young-pool probabilities restore competition, reverting once a pool matures to 7 slots. The `AuctioningCompanyDetails` pool panel shows each slot's live re-bid % + the pool's mode, sharing `NetworkRoot.ReBidProbabilityLabel`/`ReBidProbabilityPercentFor` with the roll. First-time bids into unparticipated pools stay deterministic. **Bid-count-aware ladder + last-bid safety (ND.8d, 2026-07-20, D-ND8d.1…7 — the bots-only-stagnation fix):** the flat "top-3 = satisfied" (`SatisfiedTopTierCount`, retired) is replaced for tiers 2 & 3 by a ladder keyed on the pool mode AND the bot's OWN tracked-slot count in that pool — **tier 1 stays ALWAYS satisfied** (the leader never re-bids — the bots' last-bid preservation), **tier 2 is NEVER satisfied**, **tier 3 is satisfied only at ≥2 own bids**. Matrix (1 bid / ≥2 bids, retuned 2026-07-20 round 2): tier 2 — early-rush `21%`/`21%`, normal `5%`/`3%`, urgency `5%`/`5%`; tier 3 — early-rush `13%`/satisfied, normal `2%`/satisfied, urgency `3%`/satisfied. Invariants: **tier 2 out-probabilities tier 3 at equal bid-count in every mode** (early 21>13 · normal 5>2 · urgency 5>3), all values Fibonacci, urgency ≥ normal. **The participated re-bid roll sums the bot's TWO LOWEST slot probabilities** (`SumTwoLowestReBidProbabilities`, round-2 — ranked by probability not tier, since tier 2 > tier 3), so a multi-slot bot re-bids harder; a satisfied slot contributes 0, capping the sum to a single value, so the boost applies only to bots with no satisfied slot (a single-slot bot is unchanged). Diagnosis: with 4 bidders and a 3-tier satisfied band every pool converged to a single lone challenger; the bid-count relaxation reopens tiers 2/3. `ReBidProbabilityLabel`/`ReBidProbabilityPercentFor` now take the occupant's bid-count, and the `AuctioningCompanyDetails` per-slot % is live bid-count-aware — a **player-held slot shows no re-bid %** (the player bids manually, never rolls the ladder). **Player bid-safety warnings (BTC wallet, NON-blocking — the send ALWAYS proceeds):** already-leading + closing-soon (within `AuctionClosingSoonWarningDays = 2` in-game days of the window a bid may not be mined before close). **Last-bid preservation (round-2, D-ND8d.6):** a send from the CURRENT leader does NOT count as a bid — the `ComputeAuctionLedger` ratchet skips any bid whose donor equals the current leader (also excluded from the tracked pool), so it never re-leads, never resets the 20-day window, and earns no stock; the transfer still leaves the wallet as a plain non-participating send (the warning states exactly this). In practice only the player triggers it (bots' tier-1 satisfied rule keeps them off their own leader pool). **Stale-bid cancellation + cash-back (D-ND8d.7):** a bid counts only if mined at/before its target's close — `ComputeAuctionLedger` now excludes post-close bids from the tracked pool (no stock for a late bid), a per-block sweep drops still-PENDING qualifying bids to a resolved company (never spent ⇒ implicit cash-back, the coins stay in the sender's wallet), and any qualifying bid CONFIRMED post-close is refunded from the founded company's treasury (`NetworkRoot.CancelAndRefundStaleAuctionBids`, memo "· AUCTION REFUND", fee-deducted) — player + `bot_1..4` alike. **Stuck-single-bidder escalation (round 3, 2026-07-21, `NetworkRoot.ComputeStuckEscalationProbabilityPercent`):** a bot holding EXACTLY ONE tracked slot at a non-top-3 tier (4–9) rolls `max(mode-appropriate rate, escalation)`, where the escalation grows LINEARLY by the tier's plain NORMAL-mode base each block it remains stuck at that tier (`escalation = base × (blocksElapsed + 1)`, clamped 100%) — reset the instant it re-bids (≥2 slots) or ANYONE ELSE's bid changes its rank. Diagnosed off The Silk Market: a lone bot (single bid, tier 4) rolling a flat unchanging urgency-8% forever while another bot with better odds was simply busy contesting other pools — the round-1/2 fixes solved the challenger-COUNT problem, not a lone bidder's flat, never-escalating probability. **`max()` non-regression floor (2026-07-21 audit fix — supersedes the first cut's "ignore the mode and REPLACE it"):** the escalation is a floor ON TOP of the mode rate, never a replacement. The first cut handed a single-slot below-top-3 bot in an **EARLY-RUSH** pool the NORMAL base (5/8/13%) instead of the steep early-rush rate (34/55/89%) it would otherwise roll, only climbing back over ~7 blocks (and churn kept resetting it) — the **DeepBit (non_miner_7) stagnation** (trace block 1166: bot_4 alone at tier 5 rolled 8% where early-rush = 55%). `max()` keeps early-rush's young-pool aggression AND still escalates a genuinely stuck bot toward 100%; the compute helper is unchanged, only the caller's composition (replace → max, in `TryBuildCasinoBotBid`). **Label parity (same day):** the escalation lived only in the roll, so the `AuctioningCompanyDetails` per-slot "re-bid NN%" label sat frozen at the static mode rate (a lone below-top-3 slot showing e.g. `8%` while its roll climbed `8→16→24`). Fixed with a side-effect-free `PeekStuckEscalationProbabilityPercent` (never stamps `_stuckBidderSignatures` — safe for the 1 s UI refresh; shares `EscalatedStuckPercent` with the roll) behind a new instance method `ReBidProbabilityLabelForSlot` composing the same `max(mode rate, escalation)`; the static `ReBidProbabilityLabel` is retired. **Tracking (corrected same day):** an in-memory-only signal `_stuckBidderSignatures` (NOT part of `BlockchainStateSnapshot` — the `_lastMinedByNodeId`/`_currentMinerStreak` precedent, harmlessly resets on restart) keyed per (company, bot): a `"multi"` / `"single:{tier}"` signature, edge-triggered — every time it CHANGES (a rank-push, OR the bot's OTHER slot getting evicted and dropping it from 2 bids to 1, which a pure chain-snapshot read cannot detect on its own) the current block index is stamped as the new "since" point. Updated once per this bot's per-block evaluation inside the same block-mined event that already drives the whole cascade — no `_Process`, no per-frame polling. **ND.10a (2026-07-22, §14.2 — escalation must actually ENGAGE):** round 3 fixed the escalation MATH but it only updated/fired when the bot's pipeline SELECTED that pool, and selection is spread-wide-first (ascending own-slot count) — so a bot busy seeding fresh 0-slot pools never re-selected a pool it was stuck in, leaving its signature stale (the bot_4/BitInstant tier 2→5 finding: label frozen at 8%, escalation never fired). **Fix B** — `SweepStuckBidderSignatures` refreshes the signal for EVERY (recruitable pool × casino bot) each block, edge-triggered, independent of selection (and removes it on full eviction), so a rank-push by ANOTHER bot stamps `single:{newTier}` at the block it happens and the label/roll then climb correctly. **Fix A** — in `TryBuildCasinoBotBid`, each qualifying single-slot-below-top-3 pool rolls its live escalation; the first HIT jumps the spread-wide queue and is contested outright this slot (skipping the ladder re-roll), so the growing escalation actually pulls the bot back to re-bid instead of forever seeding new pools. **ND.10c (2026-07-23, §14.4 — the bid-opportunity rework, D-ND10c.1…7):** ND.10b's panel exposed two structural faults it could not itself fix — a pool's calibrated ladder % was **unreachable** whenever the spread-wide-first walk stopped earlier (a bot holding slots in a pool always lost priority to any 0-slot pool, so `[re-bid 94%]` could sit beside a `0%` real chance), and the escalation's `tier > 3` gate left a bot parked at **tier 2/3 at a flat, never-moving 5%/2% forever** (the BitPaid finding). Four changes: **(1) the bot draw is restricted to ELIGIBLE bots** — those holding ≥1 qualifying, affordable pool with a nonzero probability (**supersedes D-ND6.1**; D-ND6.9's affordability cascade survives only as a defensive path, an occurrence in the trace is now a bug signal); **(2) PARALLEL per-pool rolls** — every affordable qualifying pool rolls its OWN ladder probability each slot and the hits compete in a uniform tie-break (**supersedes D-ND6.6's spread-wide ordering and D-ND6.8's first-affordable-is-the-target walk**; affordability becomes a per-pool filter). Each pool keeps exactly its calibrated rate (a uniform "divide one action across N pools" model was rejected: it cuts a bot's action rate to `mean(r)`, ~4× less activity, and still leaves a 2% pool stuck at 0.1%/block), total activity rises to `1 − ∏(1−r_k)`, and unparticipated pools (`r = 1.0`) still always hit, so fresh-pool seeding survives without an ordering rule; **(3) the stuck escalation extends to tiers 2–3** (base = the shallow table's NORMAL one-bid cell, tier 2 → 5, tier 3 → 2) — *this*, not the selection rework, is what unsticks a lone tier-3 occupant; **(4) ND.10a's Fix A (escalation queue-jump) is deleted** and `ComputeStuckEscalationProbabilityPercent` retired — with every pool rolling every slot, reachability is structural, so `SweepStuckBidderSignatures` becomes the SINGLE writer of `_stuckBidderSignatures` and the pure `Peek` variant the single reader. One shared `BuildBotPoolOpportunities` now feeds the roll, the eligibility test and the panel, so a displayed number cannot drift from the roll. **The `AuctioningCompanyDetails` panel now reports a TRUE PER-BLOCK probability** (`q_k = r_k · Σ_m P(H₋ₖ=m)/(m+1)` over a Poisson-binomial DP → `/B` eligible bots → the 0/1/2 count draw), shown to **2 decimals** — integer percent rounded realistic values to `0%`, the very ambiguity that opened the subphase; a `0%` now means genuinely impossible. Full derivation + worked example: `Documentation/ProjectDesignManual.md` §22.14. **ND.10d (2026-07-23, §14.5, D-ND10d.1…3 — the zero-truth audit):** the per-slot bids-list label never applied the half-spendable affordability cap the roll has always enforced, so a **priced-out** bot still advertised odds — and since the escalation ratchets regardless of affordability, a lone stuck occupant displayed a permanent `100%` beside a truthful `0%` in the panel (The Silk Market: leader `bot_2` at 337.42 BTC ⇒ ≈371 BTC required against bot_1's 264 / bot_4's 119 caps; `bot_3` guard-excluded, `bot_2` leader — all four zeros correct and all four different). Fix: one shared exclusion vocabulary (`BotPoolOpportunity.Exclusion` ∈ `satisfied`/`guard`/`priced out`, `null` = biddable) across the roll, the label (`[priced out]`) and the panel (`0.00% (reason)` via `BotPoolExclusionNote`), plus a `<0.01%` floor so a real-but-tiny chance never rounds back to a bare zero. A bot pricing out of a mature auction is the **designed** economic terminator (§22.10), not a bug; the escalation deliberately keeps ratcheting while priced out (so the bot bids the instant it can afford to) but is no longer displayed while it cannot act. **ND.10e (2026-07-23, §14.6, D-ND10e.1…4 — bot treasury sustainability):** bots were pricing out far too fast for their income, so four economy changes ship together. **(1) The opening bid is PRICE-ANCHORED** (supersedes D-ND4b.5's flat `MinBidBtc = 0.1`): a pool with no bid yet demands an opening bid **worth `$0.10`, capped at `1 BTC`** (the cap binds while BTC < $0.10 — the historical high-water mark), evaluated live on the day the first bid lands and, in the chain-replayed ledger, at each candidate bid's OWN block timestamp so replays stay deterministic (`OpeningBidFloorBtcAt`; no market data ⇒ the 1 BTC cap). Once a pool has a leader nothing changes (player +1 satoshi, bots the band). **(2) The raise band drops 10–20% → 5–10%** (`RaiseMinFraction`/`RaiseMaxFraction`) — the geometric ladder still prices everyone out eventually (§22.10, by design), at half the speed. **(3) A BTC RESERVE GUARD with hysteresis:** at **≤ 200 BTC** spendable a bot withdraws from every auction until rebuilt to **≥ 300 BTC** (`_botsRestingOnReserve` + the per-block `SweepBotReserveGuard`, in-memory/single-writer like `_stuckBidderSignatures`), surfaced as the fourth exclusion **`reserve`**, which outranks every per-pool rule. **(4) Dividend auto-claims are BATCHED at 10× the network fee** (`BotDividendClaimFeeMultiple`): the old "send the moment it clears the fee" gate had PST drips going out every block, with audited payments netting `0.00039093` BTC against a `0.01` median fee (**96% burned**, ~5.55 BTC of fees across 555 claims) — and since the fee comes out of the dividend, that was pure bot income loss. Same audit confirmed auto-claims otherwise work correctly for all four bots, SC leg included; two telemetry blind spots were closed (`bot_claim_failed` for a silent broadcast failure, `sc=` + a standalone `bot_claim_sc` row for the previously invisible SC leg). **All five thresholds are hardcoded placeholders** — the variable-reserve design (BTC price, SC position, mining income, dividend inflow, per-bot personality, DEV knobs) is deferred and recorded in `Documentation/PRIVATE_ROADMAP.md` → "Casino-Bot Treasury Policy", to be re-tuned only after hardware progression (P5) and maturing dividend inflow change the arithmetic. **Future debt (documented, not built): move bidding into internal casino wallets that then broadcast to the chain, so the contest resolves without waiting for the next block.** Position vocabulary is **"tier", never "rank"** (reserved for the future casino ranking system). Per-slot telemetry: `user://logs/casino_bot_bid_trace.csv`. Automated non-qualifying transfers (cast sell-flow, non-miner exchanges, entry-bootstrap seed funding) are economy that funds wallets without starting, leading, or winning auctions. Never-bid-on bots stay recruitable indefinitely; every resolved auction has a winner; **a win is permanent — never reopened by the ratchet rework, even though replaying old cumulative-model history through the new last-bid-wins rule can legitimately pick a different in-progress leader for a still-open auction (D-ND4b.12)**. Promoting cast miners to casino-player status = deferred, not scheduled. **Auction Settlement (ND.5, 2026-07-10, D-ND5.1…10 — ⚠️ SUPERSEDED at ND.8b.2, 2026-07-19, D-ND8.14: resolution now FOUNDS the company instead — no SC cashback, no BTC sweep; the tracked pool mints the NST/PST stock distribution and value returns as dividends. The tracked-pool mechanics and once-per-resolution trigger below survive; the payout/sweep effect does not — see the Business Migration Implemented bullet + ProjectDesignManual §22.12)**: the instant a non-miner resolves, every donor still holding a slot in its **Tracked Donation Pool** (a value-ordered top-10-by-BTC pool, NOT chronological — win-or-lose qualifying donations alike compete on amount only) is paid back in SC at the CLOSING date's price (uniform across the pool, distinct from each donation's own LIVE/current display value shown pre-settlement — corrected 2026-07-11, "day-of-donation" was a wording mistake; nothing in this system displays a value frozen at a historical day outside of settlement), funded from `CasinoScBalanceService.MainBalance` only (on-demand auto-loan if short); the non-miner then sweeps the pool's total BTC to the casino, network fee deducted from the total (accepted shortfall, not a bug). Fires exactly once per resolution via `NetworkRoot.TrySettleResolvedAuctions`, diffed per block, never from a UI refresh. Viewable in the new `AuctioningCompanyDetails` scene (BlockExplorer Enroll Mode → "Details →", shown only for non-miners with a leading bid). See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §5.2–5.3, §6 (D-EB.4…10), ND.4b/ND.4c (D-ND4b.1…13), §7 (D-ND5.1…10), §8 (D-ND6.1…10), §12.5.5 (ND.8d, D-ND8d.1…7) + `Documentation/ProjectDesignManual.md` Ch. 22 §22.6–22.10 |
| Player start | `21 Mar 2009` after the first-launch bootstrap, at the **exact same timestamp** as the bootstrap's last mined block (`HistoricalBootstrapService`) — no dead/idle time, no offset. This is a specific case of a general rule: **the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world state.** Every checkpoint capture (`BlockSessionCheckpointService.CaptureCheckpoint`) reads the clock synchronously right after mining, so this holds automatically post-first-block; pre-genesis, `BlockSessionCheckpointService.ResetToPreGenesisDefaults()` re-derives it the same way from the chain tip. See `Documentation/ProjectDesignManual.md` §24.9 |
| Swap desk fee (Step 13; network-fee component replaced at ND.7) | `10%` default, dev-clamped `1%–10%` (`CasinoGamblingFinances`), governing **both** swap directions. **Additive** (D-SW.11, 2026-07-08): `totalFee = networkFee + casinoFee` where `casinoFee = fee×(base+networkFee)` — the network fee is a SEPARATE charge summed with the casino's cut, not absorbed inside it. Since ND.7 (D-ND7.9) `networkFee` is **the day's replayed median for the current game date** (`CasinoCoinSwapService.CurrentNetworkFeeBtc`), not the retired flat 0.1 — the min-swap size and panel thresholds scale with it live (0 in the 2010-07→2011-04 zero-median era). **Max fee deviation cap** (D-SW.12): `MaxFeeDeviationPoints` (default `2.0`, clamped `[0,20]` points) caps the casino's cut alone at `nominal+points`% effective margin — the network fee is never capped, always charged in full. Bought BTC always lands on the player's **base address** (no fresh-address-per-swap, D-SW.6). See `CasinoCoinSwapService` and `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` |

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
- Network fee activation (P10 — **the flat 2009-04-26/0.1-BTC era it built is RETIRED by Step 14 ND.7's Historical Fee Replay below**; the plumbing it created survives): fee row hidden before the fee era, default-filled and clamp-validated after, in all four BTC wallet send panels (BTCWallet, FoundersWallets, CasinoFinances, BotsBtcWallets); sender balance label on every send panel; backend automated-fee sites gate on `block.Timestamp`
- Casino pool distribution atomicity: one multi-output tx per pool event (`DistributePoolEventAsSingleTx`) — eliminates partial/double-payment bug caused by sequential single sends depleting the only available UTXO before change confirmed
- Block Explorer multi-output display: full `tx.Inputs[]` / `tx.Outputs[]` iteration in block lookup and right-column preview; `tx.IsCoinbase` for coinbase detection; all transactions in a block shown (was only the first); fee LINQ uses `!t.IsCoinbase`
- Block Explorer OQ-8.2 cosmetic filter: `IsSelfChangeTransaction(tx)` hides txs whose every output goes back to an input address; `ExternalOutputs(tx)` strips change-to-self outputs from the displayed output list for txs that DO have external recipients. Remove both helpers once bots have `DerivedAddressWallet` (before referral/rank systems). See `Documentation/ProjectDesignManual.md` §29.9
- Player SC Finances hub + Private Bank Account (Step 12): new `PlayerBankAccountService` autoload (#13) — an optional, initially-empty SC reserve outside the casino with four transfer flows (manual/auto deposit Bank→Main, manual/auto withdrawal Main→Bank), all automation OFF by default; checkpoint-covered + pre-genesis reset. New player-facing `ScFinances` hub (balances, Net Worth / Overall P/L, deposit/withdraw sections, 3-scope betting stats, transfer history) + `ScTransactions` (own bank↔Main ledger view). `BankrollProgramService.AutoRechargeEnabled` off-switch (BankrollProgrammer toggle + DiceGame StrategyControlPanel toggle now a proxy to it). `CasinoClientLedgerService` gains `Method` (manual/auto) + `bankroll_withdrawal` taxonomy fix + checkpoint coverage. `DepositPopup` retired (Deposit button → ScFinances). `FinancialBettingStats` redesigned compact (3 scopes: general / since deposit / since recharge) via shared `PlayerFinancialStatsCalculator`, reused in DiceGame + ScFinances; DiceGame bet-history list now seeds from the persistent store on entry. Window starts Maximized (`window/size/mode=2`, `mode.editor=0` for editor embedding). See `AIHelperFiles/step12-player-sc-finances-plan.md` + `Documentation/ProjectDesignManual.md` Ch. 32
- Game-over redefined to total ruin across all three SC accounts (`Private Bank Account + Main Balance + Bankroll = 0`, D-SF2.1)
- Casino swap desk (Step 13, `CasinoCoinSwapService` autoload #15 + `CasinoCoinSwaps` scene): casino-as-dealer SC↔BTC trading — Panel A (Buy BTC, SC→BTC) and Panel B (Sell BTC, BTC→SC), both executing real on-chain BTC sends + instant SC legs, gated by live availability (market-birth/halt-day/settling/no-funds states) and never offering more BTC/SC than the casino truly owns (pool-payout-aware BTC equity vs. settling separation). Rank-ready 10% swap fee (1–10% dev range), **additive** to the flat network fee (D-SW.11) with the D-SW.12 max-deviation cap on the casino's own cut; per-asset strategic reserves (manual %/amount + an SC auto-floor, "R2 recharge pace") composed as `max(manual, auto)`; swap ledger kinds `swap_sc_out`/`swap_sc_in`; on-chain swap txs tagged with a display memo and rendered distinctly in wallet history panels. See `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` and `Documentation/ProjectDesignManual.md` Ch. 33–34
- BTC market data, canonical trading unlock & DEV alt-timeline machinery (Step 13 MD/TL): `BtcMarketDataService` autoload (#14) loading `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv` (5,646 days, Mt. Gox/Bitfinex/Binance provenance, 13 preserved halt days, step-function daily price, /100 fractal volume/trade accessors); player-visible surfacing (BTCWallet valuation line, StatusBar BTC ticker, ScFinances dual-mode BTC label + toggle — with the SC-only game-over metric keeping top visual prominence, D-13.3); **trading unlocks data-driven at 2010-07-18**; `TimelineConfig` (every historical date anchor routed through `Shift()`; `DevAltTimeline` false on `main` forever) + the generalized world-incompatibility guard (`NetworkRoot.ResetWorldIfIncompatible`, `user://world_timeline.stamp`, full delete list D-13.7) that auto-wipes the world on any timeline or format switch. The DEV simulacrum (+484 days, landing on 2010-07-18) hosted the swap-desk development and was exited at TL.3. See `AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md` + `Documentation/ProjectDesignManual.md` Ch. 35 (the simulacrum re-mount / new-bootstrap design guide)
- Historical network population scheduler (Step 14 ND, `BtcNetworkDataService` autoload #17 + `NetworkPopulationScheduler` pure static controller): a two-layer hybrid model reproduces real Bitcoin network growth (2009–2025) on top of the player/bot/founder economy — a **visible cast** of registry-backed, historically-named miner bots (`BotWalletRegistry.CastMiners`, spawn-drip capped by the era-standard power curve, mines via drained attempts like the founders) plus an **invisible mass** covering the rest of the modeled network, attributed to a randomly-rotating ghost pseudonym pool (ND.4a) so blocks-mined never reads as a synthetic 12-way tie. A fullness-parity budget (`GetTargetTxPerBlock`) drives automated tx circulation (cast sell-flow + non-miner↔non-miner exchanges) toward the historical tx/block target, organic traffic always counted first. The **EB.1 DEV entry-year bootstrap** (`TimelineConfig.DevEntryYear`, `0` on `main` forever) lets a developer fast-build a canon-compatible world landing directly in any chosen year (2010–2025) for spot-checking eras, reusing every piece above as throwaway, non-scene-tree instances. See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` and `Documentation/ProjectDesignManual.md` Ch. 36.
- Referral auction reworked into a real ascending auction (Step 14 ND.4b/ND.4c, `NetworkRoot.ComputeAuctionLedger`/`TryCasinoBotDonation`): supersedes EB.2's cumulative-donation-total leaderboard with an escalating bid ladder — a fixed 0.1 BTC opening floor, each later bid required to clear `leadingBid + max(0.1, 10%×leadingBid)` to take the lead, a rolling 20-day countdown that resets on every accepted raise, same-block bid collisions resolved by amount-then-chain-order (no new persisted timestamp field), and permanence for already-`Resolved` non-miners. `bot_1..4` bid on their own per-block cadence, independent of the historical tx budget; the player's BTCWallet send panel shows a live non-blocking "minimum to compete" warning. BlockExplorer Enroll Mode surfaces the leading bid's LIVE, current SC value alongside BTC (corrected 2026-07-11 — priced as of now, never a frozen historical-day value). See the same step14 plan (§ND.4b/ND.4c) and `Documentation/ProjectDesignManual.md` Ch. 22 §22.7.
- Auction Settlement — SC cashback for tracked auction donors (Step 14 ND.5 — **⚠️ the cashback/sweep effect is SUPERSEDED by ND.8b.2's company founding, see the Business Migration bullet below**; the tracked pool + once-per-resolution trigger survive) (`NetworkRoot.TrySettleResolvedAuctions`/`ComputeTrackedDonationPool`, `AuctioningCompanyDetails` scene — now InAuction-only, forwarding to `CompanyDetails` on resolution): each non-miner maintains a value-ranked top-10 **Tracked Donation Pool** (every qualifying donation it has ever received, win-or-lose, competing purely on BTC amount — smallest evicted by a strictly larger newcomer, ties never evict); the instant its auction resolves, `TrySettleResolvedAuctions` (block-diffed off `HandleMinedBlock`, fires exactly once per resolution, `user://logs/auction_settlement_trace.csv` telemetry) revalues the whole pool at the CLOSING date's price and pays every unique tracked donor in SC — player via `PrincipalBalanceService` + a new `CasinoClientLedgerService` kind (`"auction_payout"`), `bot_1..4` via `NodeFinancialState.PrincipalBalance` — funded from `CasinoScBalanceService.MainBalance` only, drawing an on-demand auto-loan (`PayFromMainWithAutoLoan`) first if short. The non-miner then sweeps the pool's total BTC to the casino, network fee deducted from the total (accepted shortfall, D-ND5's OQ-ND5.3). The new `AuctioningCompanyDetails` scene (BlockExplorer Enroll Mode → a "Details →" button shown only for non-miners with a leading bid) shows the live tracked pool while `InAuction`, or a settlement summary once `Resolved` — a pure, side-effect-free display, never a second settlement trigger. See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §7 (D-ND5.1…10) and `Documentation/ProjectDesignManual.md` Ch. 22 §22.9.
- Historical Fee Replay (Step 14 ND.7, 2026-07-13 — D-ND7.1…10): real daily network fees from Market Birth, retiring Step 10's flat 2009-04-26/0.1-BTC era entirely. New `fee_median_btc` dataset column (ND.7.0: Blockchair true medians 2010-07-18→2011-04-13 + BitInfoCharts USD median ÷ price 2011-04-14→2025-12-31, second-source spot-checked within 0.2% — Coin Metrics `FeeMedNtv` confirmed paid-tier); `BtcNetworkDataService.ComputeAndPushFeeSchedule()` builds the per-day effective median/mean/max band (D-ND7.4 per-component carry-forward; effective median honestly 0 until 2011-04-14) and pushes it into the rewritten pure-static `NetworkFeePolicy` (`MedianFeeFor/MeanFeeFor/MaxFeeFor` + timestamp twins, `ClampOrDefaultFor`, fee-free no-schedule fallback; `DefaultFee/MinFee/MaxFee` + `TimelineConfig.FeeActivationLocal` deleted). Who pays what (D-ND7.3): cast sell-flow = the day's MEAN; everything else (pool payouts, non-miner exchanges, bot bids, settlement sweeps, swap legs, player default) = the day's MEDIAN, player clamp `[median, max]`. Swap desk reads the live median (D-ND7.9 — min-swap size scales with history). `WorldFormatVersion` 2 → 3 (D-ND7.6); `network_population_trace.csv` + `feeMedianBtc,feeMeanBtc` columns. See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10.
- Saturation Ladder — casino-bot re-bidding refinement (Step 14 ND.6, `NetworkRoot.TryCasinoBotDonateOnce`/`TryBuildCasinoBotBid`, replacing the deleted `FilterAndPrioritizeTargetsForBot`): fixes the structural auction stall (a hard top-5 filter had converged all 4 bots into permanent silence, leaving every player bid uncontested) with a probabilistic re-bid ladder — each donation slot's fairly-drawn bot orders recruitable pools by ascending own-tracked-slot count (ties soonest-to-expire), skips satisfied (top-3 slot) and self-eviction-guard (smallest slot of a full pool) pools, walks them under a half-spendable cap (`required + tail + fee ≤ spendable × 0.5`), and re-bids a participated pool only on ONE roll at its best tier (two-mode since ND.6d, urgency-aware since ND.6e: NORMAL `ReBidProbabilityPercentByTier` tier 4 → 5% … 9 → 55% for pools ≥ 7 slots — shifted one Fibonacci level up to 8%…89% (`UrgentReBidProbabilityPercentByTier`) while the window is inside its final 7 in-game days (Option B, 2026-07-15); EARLY-RUSH `EarlyRushReBidProbabilityPercentByTier` tier 4 → 34%, 5 → 55%, 6 → 89% for pools < 7 slots — the 2011-playtest fix so young pools stay contested; both Fibonacci-derived, tier 10 removed — guard-unreachable). Only an all-targets-unaffordable outcome cascades the slot to another bot (the decliner isn't marked used); rule/roll refusals consume the slot. Vocabulary: **"tier", never "rank"**. Per-visit telemetry `user://logs/casino_bot_bid_trace.csv` (slot/hop/outcome/tiers/roll/cap figures — declines logged, they ARE the calibration signal); trace delete-list gap fixed (`auction_settlement_trace.csv` had been missing since ND.5). See the step14 plan §8 (D-ND6.1…10) and `Documentation/ProjectDesignManual.md` Ch. 22 §22.10.
- SC Monetary Ledger — monetary-system Option 0 (Step 14 ND.8c, `ScMonetaryLedgerService` autoload #18 + `WorldEconomy` DEV scene): every SC mint/burn event accounted under the invariant `circulation = genesis grants + outstanding debt` — five canonical 40,000 SC genesis grants (player + `bot_1..4`, equity, re-established pre-genesis) and every casino bank-loan draw mirrored as `"casino"` debt via the single `AddLoanRecord` funnel (all three draw sites); `RegisterBurn` armed for ND.8e (Central Bank / fed-funds policy replay — approved, not yet built; Option B fractional-reserve documented post-Basic-Mode; Option C inflation rejected forever, the 1:1 peg is canon). Checkpoint-covered + pre-genesis reset + delete-list; no `WorldFormatVersion` bump. See the step14 plan §12.4.6e/§12.5.1 and `Documentation/ProjectDesignManual.md` §36.9.
- The Business Migration (Step 14 ND.8b.1–.6, 2026-07-19 — D-ND8.1…39, all stages built; see `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §12.4/§12.5.2 and `Documentation/ProjectDesignManual.md` §22.12): the 40 auction non-miners are now **named historical companies** (`Data/Companies/company_roster.csv` via the pure-static `CompanyRoster`; `non_miner_{i+1}` ↔ `Auctionable[i]`, D-ND8.37 hybrid intro pacing). **Auction close now FOUNDS the company** (`NetworkRoot.FoundCompany`, supersedes ND.5's SC-cashback + BTC-sweep settlement, D-ND8.14): the company keeps its on-chain BTC as its treasury and mints the **NST/PST stock distribution** (participation% × 10,000 ST + the 5.2%-halving slot-bonus ladder; top-3 tiers → NST with votes, rest → PST, D-ND8.15). The **dividends & votes engine** (ND.8b.3, block-driven `TickCompanyGovernance` in `HandleMinedBlock` before `PersistStateToDisk` — every mutation commits in the same block write): founding-day/quarterly/>30%-inflow votes, 1-in-game-day windows, NST-weighted D-ND8.19b resolution (reserve % weighted-average clamped to the band's ±25%; ≥60% supermajority for a ±1 market shift clamped to roster-default ±1), per-world bot preference draws (D-ND8.13/26), quarterly dividends finalized per D-ND8.17 (PST daily drip, NST quarter-end lump; category default rates 5/8/13/21%/quarter, ballots clamp to [0,2×]); **an open vote where the player holds NST PAUSES the game** (`NetworkRoot.IsAwaitingPlayerVote` — SimulationService freezes the calendar, DiceGame refuses manual bets) until the ballot lands via the new **`CompanyDetails` scene** (ND.8b.4, BlockExplorer Enroll Mode Founded rows → "Details →"; holding-gated Board Vote / Quarterly Dividend / Daily Dividend panels; `AuctioningCompanyDetails` stays InAuction-only and forwards on resolution). **Weighted inflow** (ND.8b.5, D-ND8.36): the cast sell-flow recipient is drawn ∝ `inflow_weight × expansion step-up × dev multiplier`. **SC provisioning** (ND.8b.6, provisional casino path D-ND8.24/34): companies auto-convert treasury BTC → an `ScReserve` at the clean day-price rate via on-chain sends to the casino, SC funded by `CasinoScBalanceService.TryPayCompanyProvisionSc` (Main Balance, auto-loans through the `AddLoanRecord` funnel ⇒ SC Monetary Ledger covered); SC dividend claims pay bots' `NodeFinancialState.PrincipalBalance` / the player's Main Balance. All new state (foundings, governance, bot preferences, inflow multipliers) rides `NetworkRoot`'s `BlockchainStateSnapshot` (block-commit + delete-list by inheritance); WorldEconomy gained the D-ND8.25 inflow/expansion DEV knobs; telemetry `company_founding_trace.csv` + `company_governance_trace.csv` (both delete-listed). Deferred: stock trading (D-ND8.21), SC→BTC rebalancing, bank-company credit takeover (ND.8e), seizure/bust rolls.
- Bots as first-class casino clients (Step 14 ND.8f, 2026-07-19 — resolves OQ-11.1): every settled bet routes to the casino's SC balance sheet (`SimulationService.ExecuteBotBet` + `DiceGame.ExecuteBet` bot-active + the delegated-autobet-on-bot path), with bet-driven saves throttled (0.5 s casino / 1 s stats flush — no per-bet disk I/O); `CasinoClientLedgerService` gains the five canonical clients (each with an `"initial"` 40,000 entry mirroring its ND.8c genesis grant) + the per-client `ClientBetStats` book (checkpoint-covered beside the entries list); bot auto-recharges and bot auction payouts now ledger per-bot; `ClientsBetsHistory` renders one row per client and `ClientsTransactions`' selector lists all five; the live bet feed shows **all five clients** via the typed `SimulationService.ClientBetSettled` event (nodeId + gameId + `BetTransactionEvent` — fired per bot bet and per delegated player-autobet bet; 50-row cap unchanged), with a per-client filter dropdown beside the game filter. See the step14 plan §12.5.4.
- Player Dividend Claim History panel (Step 14 ND.8g, 2026-07-21): a per-company log of every dividend the PLAYER has actually claimed (`CompanyDetails`' Quarterly/Daily Dividend panels) — bot auto-claims never write here. New `CompanyDividendClaimRecord` (claimed-at game time, BTC/SC amounts paid that press, that day's BTC/SC price) in a new `CompanyGovernanceState.PlayerClaimHistory` list (capped 500, rides the existing `BlockchainStateSnapshot` for free — no new checkpoint/delete-list work), appended by `TryClaimPlayerCompanyDividends` on every successful claim. `CompanyDetails` shows three lifetime totals — total SC received, total BTC received, and that BTC's value at **2a.** each payment's own historical day-price vs. **2b.** today's live price (`BtcMarketDataService`, the `AuctioningCompanyDetails`/`BTCWallet` "always live" precedent) — plus the most-recent-30 claim log, rebuilt every 1 s refresh (not signature-gated, so a fresh claim shows immediately). Diagnosed from a real playtest gap: ArtForz Cluster had a genuine unclaimed PST balance the player simply hadn't noticed — see the step14 plan §12.5.5 round 3's dividend re-audit + §12.5.6.
- Company **stake border colours** — gold / silver / black (Step 14 ND.9b 2026-07-22, extended to open auctions at ND.10f 2026-07-23): one visual vocabulary answering "what is MY stake in this company?" across a company's whole life. **Founded** (`CompanyDetails` page frame + BlockExplorer founded rows): gold = the player holds NST, silver = PST, black = nothing, read from `CompanyFounding.Holdings`. **Still in auction** (`AuctioningCompanyDetails` page frame + BlockExplorer in-auction rows, ND.10f): the same three colours as a **live projection of the founding mint** — gold = the player currently occupies a top-3 tier of the tracked pool (it would mint NST if the auction closed at this block), silver = only lower tracked tiers (PST), black = no slot in any of the 10 tiers. Computed by the pure `NetworkRoot.GetPlayerProjectedStake` (→ `PlayerAuctionStake` enum), which reuses `FoundCompany`'s OWN value-descending tracked-pool ranking and its NST-band threshold — hoisted at ND.10f into the single `NstTopTierCount` const — so the forecast can never drift from the real mint; player identity resolves through `IsPlayerBidderAddress` (owned-address set, §30.9). It is a forecast, never an entitlement: every later bid re-orders the pool, and the page frames say so in words ("If it closed now you would mint: …"). Coexists with the pending-work tint below (border = your stake, tint = what you must do). Display-only — no persisted state, no `WorldFormatVersion` bump. See `Documentation/ProjectDesignManual.md` §22.15 and the step14 plan §14.7 (D-ND10f.1…3).
- Company **pending-work tint** — red / green / mocha / black (Step 14 ND.10h, 2026-07-23): the second colour axis on a founded company's BlockExplorer row — where the border says "what do I own here?", the row label + button say **"what must I do here?"**. Board vote pending → **red** `Vote →` (the game-pausing case, unchanged from ND.8b.3); dividends claimable → **green** `Claim →`; both → **mocha** `#C08552` `Vote →`; neither → untinted `Details →` with a **black border**. A pure function of current state (voting clears the flag, claiming clears the claimable) — no history, no persisted state, no event. Two rules: **mocha is hand-picked, never an RGB average** (additive mixing gives a yellow-green `#A6C364`; the pigment/subtractive mix in CMY gives `#4D2600`, whose hue is kept and value lifted for legibility on dark) — and **"claimable" means PAYABLE, not non-zero**: `NetworkRoot.HasPlayerClaimableDividends` tests `(Sc > 0 && ScReserve > 0) || Btc > medianFee`, since `TryClaimPlayerCompanyDividends` deducts the fee from the claim itself and pays SC only up to the company reserve — the naive `> 0` test yields a permanently-green button that pays nothing. It is the SINGLE source for both the row button and `CompanyDetails`' "Claimable now:" line (which appends "— below the network fee; still accruing"), the ND.10d "a displayed signal must share its source with the action it advertises" rule. Note `ApplyButtonBorder` overrides all four Button stylebox states (`normal`/`hover`/`pressed`/`focus`) — overriding only `normal` makes the border vanish on hover. See §22.16 and the step14 plan §14.9 (D-ND10h.1…5).
- **Bot ballots must be legal ballots** (Step 15 P15.9, 2026-07-27 — D-15.24; found by the developer at Papa's Pizzeria's first quarterly vote during the P15.8 run): `BuildBotBallot` filled its reserve dial from the bot's OWN `CurrencyBandPreference` with no reference to the company being voted at, so at a **CB1** company (charter range `[75,100]`) a CB5 bot cast a literal **0** while the player's dial is bounded at both ends (SpinBox + `TryRegisterPlayerVote`'s clamp). Because `CloseCompanyVote` clamps only the **final weighted average**, and the four bots are drawn as a permutation of the five bands, two or three sub-floor ballots dragged that average under the floor every quarter and pinned the result to **exactly 75 forever** — so the one ballot the game **pauses the whole simulation** to collect could never move the outcome. Fix: **project, never clamp** — `NetworkRoot.ProjectStanceIntoBand` maps the bot's stance into the company's band (clamping would collapse the CB5/CB4/CB3 bots onto the same bound: three identical ballots and a result still pinned). The projection is **default-anchored** — the bot's own band default maps to the company's band default, linear on each side, so it is the identity when the two bands agree (a CB2 bot at a CB2 company votes CB2's own 75, which plain `[0,100]→[min,max]` interpolation does not give for the asymmetric CB2/CB4) — rounded to a whole percent (matching the player's `Step = 1`), `.5` away from zero. Three companions: `CloseCompanyVote` now **`GD.PrintErr`s when the clamp actually bites** (the clamp silently absorbing illegal ballots is what hid this for a whole plan; post-P15.9 it should never fire); `PrintBotGovernanceStances` prints the **global** stance plus what it votes in all five bands, computed through the same helper; and `CompanyDetails`' ballot list names the band range in its header. Expect CB1 reserve results to come off the 75 floor and start varying — **all four banks are CB1**, so their SC/BTC mix is where it shows first. Display/behavior only: no new persisted field, old out-of-band `VoteBallotRecord` rows kept as honest history, **no `WorldFormatVersion` bump**. Sibling case deferred to **P15.10** (a bank's market-shift dial is voted then refused, D-15.12 — a control its shareholders cannot move). **P15.9f (same day, from the first live verification, §39.15.1):** ballots are cast when a vote **OPENS**, not when it closes, and an open vote's ballots are persisted — so a rebuild mid-playtest leaves any *already-open* vote carrying pre-fix ballots (expected, self-clearing, never a reason to wipe; the tripwire named the exact raw average that proved it). It also exposed a readout gap: the only ballot list in the scene was the Last Vote Snapshot, which shows a **closed** vote — always one quarter too late — so the Board Vote panel now lists **the open vote's** ballots (each holder's resolver weight + cast ballot or *not voted yet*) plus a live **"if the vote closed now"** line under the dial, both resolving through one new pure static `NetworkRoot.ComputeReserveVoteOutcome` that `CloseCompanyVote` also uses (a preview is a promise about what the resolver will do — rule 6's sharpest case). See `Documentation/ProjectDesignManual.md` §39.15/§39.15.1 and `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` §8 (P15.9).
- **Step 15 standing conventions** (`Documentation/ProjectDesignManual.md` **§39.16**, written 2026-07-26 after P15.5, renumbered from §39.15 when P15.9 landed) — six rules that each began as a one-off call and recurred, now defaults for the rest of plan15 and beyond: (1) **never let a persisted figure diverge from reality** — the exclusions that keep a tracked quantity truthful ship in the same phase that creates it (a lying number is invisible and compounds; an absent feature is not); (2) **a phase you cannot observe is a phase you cannot sign off** — pull the minimum readout forward from its nominal subphase and note the borrow (done 4× in plan15); (3) **prefer deletion to a flag** when something is over — but hunt down consumers that read the record from a different source; (4) **version-bump and wipe by default**, re-derive only genuinely derivable values, never contort a design to avoid a bump; (5) **a new field on an existing per-entity record gets a sentinel default + backfill** when its populator is guarded by an "already populated?" check — this is about silent failure modes, not bumps; (6) **a displayed signal must share its source with the action it advertises** (the ND.10d rule). Plus: an on-chain display memo can become **load-bearing** (`COLLATERAL`) — check before treating `InputDataText` as decorative.
- **Surfacing & telemetry** (Step 15 P15.7, 2026-07-26 — D-15.9/22): mostly already shipped early alongside the mechanisms it observes (FED-scene Banking layer + financier preview at P15.2, `bank_credit_trace.csv` at P15.3, the shortfall ballot control at P15.4e, Closed-Companies + recovery tracker at P15.5, the FBI board at P15.6). Added here: the **layer-1 per-client sub-ledger** under each bank in the FED scene (BTC bought / SC paid / provision count — layer 0 = bank↔FED above it, layer 1 = bank↔company, the D-15.5 model visible on one screen); the **banking-layer aggregate** in `WorldEconomy` (`AppendBankingLayer` — per-bank strip with under-collateralized/shortfall/insolvent flags, a system `Σ collateral value vs Σ FED debt` solvency line valued at TODAY's price, and a one-line closures pointer); and the player-facing **bank lending panel** in `CompanyDetails` (`NetworkRoot.GetBankLendingSummary` → FED debt with drawn/repaid, collateral + live value, the **collateral-vs-debt health line** that is the carry itself, next installment + due date, pending-shortfall/insolvent states). **WorldEconomy deliberately got the aggregate only, not copies** of the closures/recovery/FBI panels — those belong to the FED's own page, and duplicating them would mean two places to keep in step. Every lending figure is computed from the same constants the repayment uses, so a displayed installment cannot disagree with the charged one. See `Documentation/ProjectDesignManual.md` §39.14.
- **The FBI investigation / seizure thread** (Step 15 P15.6, 2026-07-26 — D-15.14/19/21): from **14 Jun 2011** (Gavin Andresen's In-Q-Tel presentation — the CIA link is flavour only; routed through `TimelineConfig.Shift`) the FBI investigates SC-hoarding companies. **The hybrid**: F1's deterministic **investigation meter** picks the targets (player-legible, so keeping SC lean is a real lever), a **capped roll** decides which block the raid lands. **Throughput-relative tolerance** (`tolerance = categoryMultiplier × T`, Official **∞** / Light-Grey 8× / Dark-Grey 3× / Black 1×) where `T` = the company's SC inflow, accumulated at the single conversion-credit site and rolled current→last each quarter; effective `T` = max(last, current), and a `T = 0` company sits at the overage cap **by design** ("unexplained wealth"). Meter sized against quarters (≈135 blocks): gain `0.5 × overage × darkness`/block (darkness = category index + 1, overage capped at 4), decay `1.0`/block under tolerance — the decay IS the player's lever. **Priority (D-15.19) is one ordering**: flagged targets sort **banks last**, others by overage; **at most one raid per block** on the top target, rolling `min(2%, 0.5% × darkness × score/threshold)`. **Self-funding**: the initial grant is booked as a **FED loan on client `"fbi"`** (never repaid, like the casino's) so it can't mint SC outside `circulation = grants + debt`; seized SC is a plain **transfer** and touches neither side — hence `DissolveCompany` branches on reason (seizure → FBI, debt default → burn against the loan). Seized **BTC is not moved at all**, flowing straight into P15.5's custody chain. `GetFbiInvestigationWarning` surfaces state/progress/tolerance in `CompanyDetails` for any viewer (null when there's nothing to say), and the FED scene lists open files in the roll's own order via the shared source. **Every number is a P15.8 placeholder.** See `Documentation/ProjectDesignManual.md` §39.13.
- **Dissolution, the Closed-Companies list and seized-wallet custody** (Step 15 P15.5, 2026-07-26 — D-15.8/15/17/18): a bank carrying P15.4e's `UnrecoverableShortfallSc` is insolvent and **dies** (`TryDissolveInsolventBanks`, collected and applied OUTSIDE the governance loop since dissolving mutates the dictionary it iterates; `TickCompanyGovernance`'s early-out widened to "no live **and** no closed companies"). **Liquidation is deletion, not a rule**: closure removes the entry from BOTH `_companyFoundings` (which destroys every holder's NST/PST) and `_companyGovernance` (which destroys unclaimed claimables), so every live loop skips the dead company for free; already-claimed dividends stay in the holder's wallet. Only a `CompanyClosure` record survives (reason `debt_default` | `fbi_seizure`, category, FED loss, balances at closure, and a copy of the player's holding purely so the notice can say what was lost) — riding `BlockchainStateSnapshot` for free. **Custody = seize the wallet, don't move the coins** (the FED is SC-only and has no address): the dead wallet stays on-chain, unspendable, still receiving its scheduled inflows — that IS D-15.18's "held 100% as BTC"; its leftover SC is applied against the debt and burned. `TryAssignSeizedWallets` hands each wallet to a **solvent** founded bank of the **matching category** (releasing the assignment if that heir later dies), after which `SweepClosedCompanyInflows` forwards the balance + every later arrival there (memo `SEIZED`) as ordinary business inflow, never collateral. DEV **recovery tracker** in the FED scene values `RecoveredBtc` at the LIVE price against `DebtAtClosureSc` → RECOVERED/underwater. Display note: the BlockExplorer "Founded" list is chain-derived (a resolved auction stays `Resolved` forever), so a dissolved company gets its own grey `✗` terminal row with no action button, and `CompanyDetails` shows a liquidation notice instead of "not founded yet?"; **re-founding is impossible** because `TrySettleResolvedAuctions` only fires on an `InAuction → Resolved` flip. See `Documentation/ProjectDesignManual.md` §39.12.
- **Extra-lazy FED repayment, greed voting and the shortfall board vote** (Step 15 P15.4b–e, 2026-07-26 — D-15.4/7/13/15; P15.4a shipped at P15.3): **greed** is a third per-bot governance axis (`BotGovernancePreference.GreedPreference` ∈ `not_so_greedy · almost_greedy · greedy · extremely_greedy`, drawn per world as its own shuffled permutation) answering only "shareholders' pockets vs. the company's money" — it biases the **quarterly payout ballot** (multiplier `0.5/1.0/1.5/2.0` on the category default, spanning exactly the existing `[0, 2×]` clamp, with `almost_greedy` = the pre-greed behavior) and the shortfall split, but **never** the reserve-band vote. The field defaults to `""` rather than a stance so `BackfillGreedPreferences` can fill only absent slots — the general shape for adding a field to an existing persisted record without a format bump. **Quarterly repayment** (`TryBankQuarterlyRepayment`, on the payment day after dividends settle, before the new quarterly vote): a bank owes `BankQuarterlyRepaymentFraction` (**10%**, P15.8 knob) of outstanding FED principal, sells **just enough** collateral to the casino at the clean rate, and `CentralBankService.Repay` **burns** it; the network fee comes out of the collateral pool so the book never claims spent BTC. Not always a net burn — if the casino must auto-loan to buy, it is a debt **transfer** bank→casino (a P15.8 observation). **The shortfall vote** (new `CompanyVoteKindShortfall`, opened once the quarterly has closed, ahead of the >30% special vote): one dial splitting the gap between a **dividends cut** and a **reserves cut** (default 50/50, bots per §3.3's greed table) — both draw SC from `gov.ScReserve`; the vote decides *who bears it*, a dividends cut also shrinking `QuarterDividendSc`. It takes its own exit in `CloseCompanyVote` so it can't move the reserve mix/category/payout as a side effect. Whatever neither source closes accumulates on `gov.UnrecoverableShortfallSc` — **the dissolution trigger P15.5a reads**. `TryRegisterPlayerVote` gained an optional `dividendsCutPercent` (so the pause can never deadlock) and `CompanyDetails` swaps its Board Vote body for the shortfall dial. See `Documentation/ProjectDesignManual.md` §39.10–39.11.
- **Company BTC→SC conversions route through the banks** (Step 15 P15.3, 2026-07-26 — D-15.4/11/20; the reform's credit loop closes): `NetworkRoot.TryConvertCompanyReserves` keeps every calibration floor and its clean-rate pricing (D-ND8.24) but now asks **`SelectFinanciers`** who the counterparty is, dispatching to `TryConvertViaBank` (the bank draws the SC from the FED as `bank:<id>` debt, the company's `ScReserve` is credited, and the BTC lands in the bank's wallet as quarantined `CollateralBtc`, memo `COLLATERAL`) or `TryConvertViaCasino` (byte-for-byte the old ND.8b.6 path, surviving **only** as the pre-first-bank fallback, memo `CONVERSION`). **The bank never touches its own `ScReserve`** — the borrowed SC passes straight through, leaving it long BTC on FED debt, which *is* the economic point (§1). Both paths unwind the SC leg on a failed broadcast; the bank's unwind **repays the just-drawn loan** (burning the SC back out, invariant intact), and the client book is written only after BOTH legs succeed. A bank finances peers, never itself. **The `CollateralBtc` quarantine landed here rather than at P15.4a** (a persisted figure would otherwise have diverged from reality): new `CompanyOwnBtc` = treasury − collateral replaces `CompanyTreasuryBtc` at all three governance sites (conversion base, quarterly dividend base, >30%-inflow baseline), and `AccumulateCompanyInflows` skips `COLLATERAL`-memo arrivals at a bank — otherwise spurious special votes would fire, **pausing the game** wherever the player holds NST there. Tier-3 splits are detected, warned and routed to the casino rather than half-executed (the multi-leg BTC path is unbuilt — with `BankFundingCapacitySc`, the complete list of what ND.8e must touch). New telemetry `user://logs/bank_credit_trace.csv` (delete-listed). See `Documentation/ProjectDesignManual.md` §39.9.
- **Bank companies typed, categorised and locked** (Step 15 P15.2, 2026-07-26 — D-15.6/12/20; no behavior change, conversions still route to the casino until P15.3): the four CB1 roster banks get the **Official → Light-Grey → Dark-Grey → Black** gradient (`company_roster.csv` `market_category`: First Satoshi Savings `official` · Digital Reserve Trust `light_grey` · Ledger & Sons `dark_grey` · Harbor Coin Bank `black`) — the **distance axis** §5.1 selection measures on, so a darker bank inherits both the higher §12.4.3 dividend rate and (from P15.6) the higher seizure risk. Banks are identified by a **closed id set** in `CompanyRoster` (`BankCompanyIds`/`IsBank`/`Banks`) rather than a CSV column (plan15 creates no companies, D-15.6). **Categories are LOCKED** (`CloseCompanyVote` refuses the ±1 shift for a bank, still tracing `shift_refused=bank_locked`) — and since a locked category is therefore a **derived** value, it is **re-derived from the roster on every snapshot restore**, which is what let the gradient reassignment ship with **no second `WorldFormatVersion` bump**. New `NetworkRoot._bankState` (`BankBalanceSheet { CollateralBtc, Clients→BankClientAccount }`, history capped 200/client) rides `BlockchainStateSnapshot` for free. **`SelectFinanciers(companyNodeId, amountSc)`** implements the full §5.1 framework — tier 1 nearest-category (ties toward Official, then founding order), tier 2 single full-funder, tier 3 split, casino fallback when no bank has founded — with tiers 2/3 **deliberately dormant** (`BankFundingCapacitySc` is infinite until ND.8e's limits, D-15.1), so credit limits become a one-method change. The FED scene gained a read-only **Banking layer** block + financier-selection preview (an early slice of P15.7a) so a no-behavior-change phase is still verifiable. See `Documentation/ProjectDesignManual.md` §39.7–39.8.
- The **Central Bank (FED)** as an explicit entity (Step 15 P15.1, 2026-07-26 — `CentralBankService` autoload #19 + the `CentralBank` DEV scene; D-15.1/3/5/10/16/17/23): the abstract off-screen "bank" the casino borrowed from becomes a persisted, DEV-visible in-world entity with **per-client accounts** (outstanding debt / total drawn / total repaid / draw+repay counts / capped movement history). **Two-layer debt architecture (Fork A)** — the FED owns the per-client relationship, `ScMonetaryLedgerService` keeps the macro `circulation = grants + debt` invariant, synced for free because `DrawLoan`/`Repay` call its `RegisterLoanDraw`/**`RegisterBurn`** (the burn hook's first real caller since ND.8c armed it). The **casino stops storing its own loan copy** — `LoanCount`/`TotalLoaned`/`LoanHistory` are read-through accessors over its FED account, and all three draw sites funnel through one `DrawFedLoan`. **Zero casino behavior change** (unlimited zero-rate auto-loan, D-15.1/17); checkpoint-covered with load-bearing restore ordering, pre-genesis reset, delete-list entry, and **`WorldFormatVersion` 3 → 4** (D-15.10, clean reset — no further plan15 bump). See `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` §8 (P15.1a–e) and `Documentation/ProjectDesignManual.md` Ch. 39.
- Companies are **named everywhere the player looks** (Step 14 ND.10g, 2026-07-23): the ND.8b.1 company identities now replace the internal `non_miner_#` id across every UI-facing surface — the four BTC send-panel recipient selectors, BlockExplorer's balances/mining list + address directory + node selector, `BotsBtcWallets`, and (via `DescribeAddress`) every wallet history panel and auction row. **Two tiers**: player-facing shows the name alone (`Mt. Gox`), DEV/diagnostic shows both (`Mt. Gox (non_miner_7)`) because the raw id is the **join key of every CSV trace** and of the `_companyFoundings`/`_companyGovernance`/`_stuckBidderSignatures` dictionaries; traces, logs, persisted JSON and dictionary keys are untouched. `NetworkRoot.DescribeNodeForDisplay`/`DescribeNodeForDev` are the only two places the `non_miner_{i}` ↔ `CompanyRoster.Auctionable[i-1]` pairing becomes UI text — `player`/`casino`/`bot_1..4`/founders keep their ids, and the cast miners already carry human names (`artforz`, `foundry_usa`). **Behavior change (D-ND10g.3):** the send panels now list only companies already **introduced** on the historical curve (`GetIntroducedNonMinerAddresses`) — under a raw id listing all 40 leaked nothing, under real names it would put *Coinbase* in a 2011 dropdown; all four panels share one `GetSendableBotTargets()`. Two regressions were anticipated and fixed, both the same mistake — **recovering data from formatted display text**: `GetNodeStatusLines` now returns `(nodeId, line)` pairs (BlockExplorer's ⛏ marker used to re-parse the id out of the line prefix) and BlockExplorer carries `_selectorNodeIds` parallel to its option items (the lookup handlers fed `GetItemText` into id-resolving calls). See §22.17 and the step14 plan §14.8 (D-ND10g.1…3).

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

This is the service-to-service half of a broader project-wide principle — see **Pattern 6** below for the full rule (when `_Process` is and isn't warranted) and the standing project goal to audit every remaining poller before Basic Mode v0.1 ships.

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

### 6. Prefer Event-Driven Design Over `_Process` Polling

**This is a standing project-wide design principle, not a one-off — apply it to every new system, and treat it as a checklist item before ANY code review is considered done.** `_Process(double delta)` runs every rendered frame. Reaching for it by default is the single most common way to smuggle needless per-frame CPU work into a project whose core loop (bet → nonce attempt → time tick) is already discrete and event-shaped from the ground up.

**The rule:** before writing `_Process`, ask *"does this genuinely need to know about the passage of REAL time, every frame?"*

- **Yes** → advancing a real-time clock, an animation, a UI countdown against wall-clock delta. `_Process` is correct and necessary. Examples already in this codebase: `CalendarTimeService` (advances the game clock by real delta × speed multiplier — nothing else could drive it), `SimulationService` (the background sim's per-tick bet/mining loop), `DiceGame.TickAutoBet` (autobet pacing/animation).
- **No, it only re-reads STATE that changes on a discrete event** (a bet settled, a block mined, a transfer completed, a claim was pressed) → **this is the polling anti-pattern.** The state owner (a service) should fire a typed `event Action<T>` at the exact point the state changes (Pattern 1 above); the consumer (usually a UI scene) should subscribe in `_Ready()` and unsubscribe in `_ExitTree()`, and stop polling entirely.

**The hybrid middle case — a cheap edge-trigger inside `_Process`.** Sometimes the STATE only changes on a boundary that isn't itself a discrete game event (a calendar day rolling over). `BtcMarketDataService`/`BtcNetworkDataService` are the reference pattern: `_Process` does the *cheapest possible* single date comparison against the game clock every frame, and fires `MarketDayChanged`/`NetworkDayChanged` **only** when a day boundary is actually crossed — no timers, no per-frame parsing, no I/O most frames. If you must poll something inside `_Process`, this is the shape: the per-frame cost should be one flag/value comparison, and the real work (rebuilding a panel, hitting disk) belongs behind the resulting edge, never inside the poll itself.

**A signal doesn't have to be a Godot/C# event — an in-memory flag with edge-triggered updates is the same idea.** ND.8d round 3's stuck-bidder-escalation fix (`NetworkRoot._stuckBidderSignatures`, 2026-07-21) is the freshest example: rather than replaying bid history every roll (expensive, and still not `_Process`-shaped) or polling anything per-frame, it stores a small `(signature, sinceBlockIndex)` per (company, bot) — updated once, exactly when the signature actually changes, inside the SAME block-mined event that already drives the whole bidding cascade. No new persisted state, no per-frame cost, no history replay. When a "since when has X been true" question comes up, reach for an edge-triggered signal like this before reaching for either a poll or a full replay.

**Already-good examples in this codebase (services firing typed events on real state changes):** `UserStatsService.StatsChanged` (throttled 250ms — the reference pattern for a HIGH-FREQUENCY event, `EmitStatsChangedIfNeeded()`), `SimulationService.ClientBetSettled`, `CasinoClientLedgerService.LedgerChanged` / `ScMonetaryLedgerService.LedgerChanged`, `PrincipalBalanceService.BalanceChanged` / `CasinoScBalanceService.BalanceChanged`, `PlayerBankAccountService.BankStateChanged`, `CasinoCoinSwapService.SwapDeskChanged`, `BtcMarketDataService.MarketDayChanged`, `BtcNetworkDataService.NetworkDayChanged`.

**Known migration candidates (audited 2026-07-21, none fixed yet — this is the backlog, not a mandate to stop and fix them now):** roughly fifteen scenes poll on a `RefreshInterval`/`FallbackInterval` timer purely to rebuild a panel from service state that only actually changes on a settled bet, a mined block, or a transfer — `StatusBar`, `FinancialBettingStats`, `CalendarsNavigator`, `BetsHistoryExplorer`, `BTCWallet`, `AuctioningCompanyDetails`, `CompanyDetails`, `BlockExplorer`, `CasinoFinances`, `BotPlayHistory`, `ScFinances`, `ScTransactions`, `CasinoGamblingFinances`, `ClientsTransactions`, `ClientsBetsHistory`, `FoundersWallets`, `CasinoCoinSwaps`, `BTCPoolsAndHardwareShop`, `BotsBtcWallets`. Each is a candidate to migrate to "rebuild once in `_Ready()`, then only when a subscribed event fires" — full write-up, rationale, and the Basic Mode v0.1 gate: `Documentation/ProjectDesignManual.md` Chapter 38.

**Project goal, tracked in `Documentation/PRIVATE_ROADMAP.md` §6:** before Basic Mode v0.1 is considered complete, audit every `_Process` override in the project against this principle and migrate what's feasible to event-driven design. Not a hard blocker on other work — but do not add a NEW poll-shaped `_Process` to the backlog above without first checking whether an event already exists (or should) for the state you're reading.

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
- **Network fee market simulation — Option A ✅ IMPLEMENTED (Step 14 ND.7, 2026-07-13)**: the historical fee replay is live (`NetworkFeePolicy` consumes the dataset's daily median/mean band from Market Birth — see the Canonical Decisions fee row and the Implemented bullet). **Option B** (a reactive fee market from our own simulated mempool congestion) remains the **future validation experiment**: if it reproduces a curve similar to the replay, it confirms the Step-14 population/volume simulation was built right — and it is where fee CHOICE enters (queue-jumping above the daily base when the 24-tx cap saturates; today no participant has a reason to pay above base, OQ-ND7.1). See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10.6 and `Documentation/PRIVATE_ROADMAP.md` "Network Fee Market Simulation".

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
│   │   ├── AuctioningCompanyDetails (Step 14 ND.5, Enroll Mode "Details →" while InAuction; forwards to CompanyDetails on resolution) → BlockExplorer
│   │   └── CompanyDetails (Step 14 ND.8b.4, Enroll Mode Founded rows' "Details →" — Board Vote / dividend claims) → BlockExplorer
│   └── CalendarsNavigator  → Main Menu / BetsHistoryExplorer
│       └── BetsHistoryExplorer → origin-aware back (Main Menu / CalendarsNavigator / ScFinances)
├── ScFinances [player-facing]  → Main Menu   (Step 12 — the player's SC-flows hub)
│   ├── ScTransactions              → ScFinances
│   ├── BetsHistoryExplorer         → (origin-aware back to its launcher)
│   ├── BankrollProgrammer          → ScFinances / (its own Main Menu back)
│   └── CasinoCoinSwaps             → origin-aware back (Main Menu / ScFinances)
├── CasinoCoinSwaps [player-facing]  → Main Menu   (Step 13 — the casino's SC↔BTC swap desk)
├── MartingaleCalculator (standalone, full-screen) → Main Menu
├── WorldEconomy [DEV]  → Main Menu   (Step 14 ND.8c — SC Monetary Ledger readout; + ND.8b.6 company inflow/expansion knobs)
├── CentralBank [DEV]   → Main Menu   (Step 15 P15.1e — the FED's per-client loan accounts, D-15.16)
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
