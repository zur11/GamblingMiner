# Autoload Services — the reference

The long-form record of every service singleton in the project. Extracted from `CLAUDE.md`
(2026-08-20) at 48,517 characters, where it was the last remaining oversized block.
`CLAUDE.md`'s **Key Architecture — Autoload Services** keeps a one-line index of every entry
below; this file holds the detail, read on demand.

**How to access an autoload** is *not* documented here — that is `CLAUDE.md` **Important Patterns
§5**, which also owns the registration-order rule the entries below repeatedly depend on.

---

## Registration order

The list below is `project.godot`'s `[autoload]` block, verbatim and in order (**19 entries**).
Order is load-bearing — see `CLAUDE.md` Important Patterns §5.

| # | Autoload | Documented in |
|---|---|---|
| 1 | `WorldGuardService` | this file |
| 2 | `UserStatsService` | this file |
| 3 | `CalendarTimeService` | this file |
| 4 | `BankrollStateService` | this file |
| 5 | `PrincipalBalanceService` | this file |
| 6 | `BankrollProgramService` | this file |
| 7 | `CasinoScBalanceService` | this file |
| 8 | `CasinoClientLedgerService` | this file |
| 9 | `PlayerBankAccountService` | this file |
| 10 | `CasinoCoinSwapService` | this file |
| 11 | `ScMonetaryLedgerService` | this file |
| 12 | `CentralBankService` | this file |
| 13 | `BlockSessionCheckpointService` | this file |
| 14 | `SceneManager` | `CLAUDE.md` → **Scene Management** |
| 15 | `NotepadService` | `ProjectDesignManual.md` §20.1 |
| 16 | `FoundersMiningService` | this file |
| 17 | `SimulationService` | this file |
| 18 | `BtcMarketDataService` | this file |
| 19 | `BtcNetworkDataService` | this file |

`NetworkPopulationScheduler` (last section below) is **not** in that list — it is a pure `static
class` driven per-frame by `SimulationService`, documented here because it belongs to the same
layer. `HistoricalBootstrapService`, `HistoricalEventScheduler`, `WalletInitializationService`,
`WordlistBootstrapper` and `TimelineConfig` also live in `Scripts/Services/` without being
autoloads; they are documented where the systems they serve are.

The sections below are in the order they were written (roughly the order they shipped), not
registration order.

---

### `WorldGuardService`
**Location**: `Scripts/Services/WorldGuardService.cs`

Deliberately the **FIRST** autoload: its only job is running `NetworkRoot.RunWorldCompatibilityGuard()` (format-version OR timeline-tag mismatch ⇒ full clean world reset, D-13.7) **before any other autoload can load a `user://` state file into a static cache** — a file deleted after being loaded survives in memory and re-persists, which is how alt-timeline hardware/pool state once leaked across a timeline wipe (TL.3 incident). Keep it first; see `Documentation/ProjectDesignManual.md` Ch. 35 §35.1.

### `CalendarTimeService`
**Location**: `Scripts/Services/CalendarTimeService.cs`

Manages game-time progression.

- Game start: `2009-01-03 18:15:06 Local`
- Advances via `_Process(delta)` when `IsRunning = true`
- `SpeedMultiplier` allows adjustable time scaling
- **`SimulationThrottle` (R2-C1, 2026-07-27)** — the fraction of last frame's simulated time the bet engine actually retained; the clock advances `delta × SpeedMultiplier × DevTimeScale × throttle`. `1.0` (inert) whenever nothing is dropped, which is every frame that keeps up; below 1 only when `SimulationService`'s backlog clamp discarded simulated work, so **game time can never outrun the mining it represents**. Written by `SimulationService` each frame, reset to `1.0` when the sim stops. See the Round 2 entry under Blockchain / Mining below
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

