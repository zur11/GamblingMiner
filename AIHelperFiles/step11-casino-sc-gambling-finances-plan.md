# Step 11 — Casino SC Gambling Finances — Implementation Plan

**Status**: ✅ DONE (2026-06-30) — branch `casino-sc-gambling-finances`

**Scope**: Implements the casino's StableCoin (SC) financial layer as an explicit, auditable system. Introduces:
- **(a)** `CasinoScBalanceService` autoload: SC Main Balance (99M SC) + Bankroll (1M SC from a hypothetical bank loan), persisted and auto-recharged; the casino's balance sheet parallel to the player's `PrincipalBalanceService` + `BankrollStateService`.
- **(b)** SC flow wiring in `SimulationService`: every settled player bet routes the inverse of the player's profit to/from the casino bankroll (player loss → casino gains; player win → casino pays profit).
- **(c)** `CasinoGamblingFinances` DEV scene (accessible from Main Menu only): shows casino SC Main Balance, Bankroll, P/L vs total loans, auto-recharge/transfer controls, and navigation buttons to `ClientsBetsHistory` and `ClientsTransactions`.
- **(d)** `ClientsBetsHistory` DEV scene: per-client real-time bet monitoring for Dice (multi-game ready); all-time and since-last-deposit casino P/L per client; global total SC wagered by all clients (live); overall casino P/L since 21 March 2009.
- **(e)** `CasinoClientLedgerService` autoload: per-client SC deposit/withdrawal ledger with stat snapshots at each deposit; prerequisite for since-last-deposit metrics in both sub-scenes.
- **(f)** `ClientsTransactions` DEV scene: full SC transaction ledger per client (deposits, withdrawals, auto-recharges); global totals (all clients deposited / withdrawn); wager-base annotations on deposit rows.

**Created**: 2026-06-30.

**Dependencies**: Requires `SimulationService` (BetSettled event), `UserStatsService` (player bet history), `BlockSessionCheckpointService` (checkpoint extension) — all already implemented. Does **not** block other Basic Mode work (own branch).

**Companion docs**:
- `Documentation/PRIVATE_ROADMAP.md` §P6 + §6 checklist
- `AIHelperFiles/background-simulation-plan.md` (SimulationService architecture)
- `AIHelperFiles/scheduled-bot-transactions-plan.md` (OQ-11.1 bot-bet routing, deferred)
- `Documentation/ProjectDesignManual.md` Ch. 24–25 (checkpoint / auto-recharge patterns)

---

## 0. Decisions locked

| # | Question | Decision |
|---|---|---|
| **D1** | Casino SC as explicit tracked service or implicit math? | **Explicit `CasinoScBalanceService`** autoload. The casino is a first-class economic actor; its SC balance must be auditable from day one, not derived post-hoc. |
| **D2** | Initial casino SC allocation? | **99,000,000.00 SC Main Balance + 1,000,000.00 SC Bankroll = 100,000,000.00 SC total**. This represents a hypothetical bank loan. The bank mechanics (interest, repayment threshold) are a post–Basic Mode v0.1 design (see §7 OQ-11.2). |
| **D3** | Auto-recharge model? | **Target-to-fill on exhaustion**: the Bankroll fluctuates freely with each bet result (up when player loses, down when player wins). Auto-recharge fires only when the Bankroll reaches ≤ 0, then fills back to `BankrollTarget` (default **1,000,000.00 SC**) from Main Balance. `BankrollTarget` is configurable from the `CasinoGamblingFinances` scene. **Correction from original plan**: triggering recharge on every dip below target caused MainBalance to absorb all bet P/L, making the Bankroll always display 1M SC with no meaningful fluctuation. |
| **D4** | Casino auto-recharge: always-on or configurable toggle? | **Always automatic, no toggle**. The casino is an institution, not a player. Auto-recharge fires when the Bankroll is exhausted (≤ 0), transfers `BankrollTarget − Bankroll` from Main Balance. A toggle would let the casino "close" which is outside Basic Mode scope. |
| **D5** | Casino SC persistence strategy (checkpoint rule)? | **Extends `BlockSessionCheckpointService`**: casino SC state is snapshotted and restored at each block mining event — consistent with "a block is the only commit to disk". No mid-session saves. |
| **D6** | Bot bets in casino SC flow? | **Open question (OQ-11.1) — player bets only in Phase 11.2**. Bot SC is a simulation artifact today. The question is tracked and deferred. |
| **D7** | Casino "bankruptcy" condition? | **Flavor event + automatic 100M SC re-loan**. If Main Balance cannot cover the target-to-fill transfer (i.e., `MainBalance < BankrollTarget − Bankroll`), the bank injects another **100,000,000.00 SC** directly into Main Balance, logs a new debt entry (`LoanCount++`, `TotalLoaned += 100M`), then completes the recharge. Game continues uninterrupted. Full debt mechanics (repayment threshold, interest, bet-coverage caps) are post–Basic Mode v0.1 design (OQ-11.2 below). |
| **D8** | Casino-open date for P/L baseline? | **21 March 2009 00:00:00 Local** — the game start date, hardcoded. P/L = `TotalSc − TotalLoaned` (grows with each 100M re-loan — see D7). Positive = casino net profit; negative = casino net loss. |
| **D9** | ClientsBetsHistory: full bet scroll or aggregate summary? | **Aggregate per player + rolling live feed** (last 50 bets, newest first). The full historical scroll lives in `BetsHistoryExplorer` (player-facing). `ClientsBetsHistory` is a DEV operations dashboard. |
| **D10** | `ClientsBetsHistory` and `ClientsTransactions`: full scenes or popups? | **Both are full scenes**, navigated via `SceneManager`, each with a "← Back to Casino Finances" button. Consistent with all other DEV scenes in this project. |
| **D11** | Multi-game readiness in ClientsBetsHistory? | **Include a `Game` filter dropdown from day 1** (default "All Games", only "Dice" populated). Aggregate rows are keyed by `(playerId, gameId)` so future games slot in without schema changes. |
| **D12** | Per-player enrollment date? | Derived from each client's **first recorded bet timestamp** in `BetHistoryRepository` / `UserStatsService`. For the current single player, this is the first bet after the 21 Mar 2009 bootstrap. For future bot clients, their first bet timestamp. |
| **D13** | Should casino SC Bankroll appear in the DiceGame StatusBar or any player-facing UI? | **No — DEV scenes only**. The casino's SC reserves are internal. |

---

## 1. Current state (verified in code)

### Casino SC — no explicit tracking today

When a player bets in `BetService.ExecuteBet()`:
1. `bet` is withdrawn from the **player's bankroll wallet**.
2. If win: `payout = bet + creditedProfit` is deposited back to the player's wallet.
3. **The casino receives and pays nothing** — the house edge is implicit. No SC flows to or from any casino-side ledger.

This means: there is currently no source of truth for how much SC the casino has earned or owed since launch.

### Existing `CasinoFinances` scene — BTC only, unrelated

`Screens/CasinoFinances/CasinoFinances.cs` manages the casino's **BTC** wallet (UTXO-based, addresses, passphrase, BTC sends). It has zero SC visibility. `CasinoGamblingFinances` is a **new, separate scene** for SC finances; these two never merge.

### SimulationService.BetSettled — already the right hook

`SimulationService` emits `Action<BetTransactionEvent, string> BetSettled` after every settled player bet. `CreditedProfit` in `BetTransactionEvent` carries the signed net gain/loss from the player's perspective. This is the exact hook needed for casino SC routing (casino delta = `−CreditedProfit` on a win; `+BetAmount` on a loss, but equivalently `−player_profit` since on loss `player_profit = −BetAmount`).

Net formula: **`casinoDelta = −event.CreditedProfit`** (covers both win and loss without branching).

### UserStatsService — per-player aggregates already tracked

`UserStatsService` tracks lifetime bets, wins, losses, net profit for the player. Per-player P/L from the casino's perspective = `−(player net profit)`. No new storage design is needed for per-player aggregates; `ClientsBetsHistory` reads from `UserStatsService`.

### BlockSessionCheckpointService — ready for extension

The existing checkpoint already captures `PrincipalBalance`, `BankrollBalance`, `AutoRechargeAmount`, and `TransferRecords`. Extending it with casino SC state requires only adding two new fields to the snapshot model.

---

## 2. Target architecture

### 2.1 `CasinoScBalanceService` (new autoload)

Mirrors the player's `PrincipalBalanceService` + `BankrollStateService` + `BankrollProgramService` but as a single cohesive service for the casino SC layer.

```csharp
// Scripts/Services/CasinoScBalanceService.cs
public partial class CasinoScBalanceService : Node
{
    public const decimal InitialLoanAmount     = 100_000_000.00000000m;
    public const decimal DefaultBankroll       =   1_000_000.00000000m;
    public const decimal DefaultMainBalance    =  99_000_000.00000000m;
    public const decimal DefaultRechargeAmount =   1_000_000.00000000m;

    public decimal MainBalance      { get; private set; }
    public decimal Bankroll         { get; private set; }
    public decimal TotalSc          => Money.Normalize(MainBalance + Bankroll);

    // Positive = casino is ahead of all loans received so far; negative = casino owes.
    public decimal CumulativeProfitSinceLoan => Money.Normalize(TotalSc - TotalLoaned);

    public decimal BankrollTarget   { get; private set; } = DefaultBankroll;   // target-to-fill level
    public int     LoanCount        { get; private set; }                        // how many 100M re-loans taken
    public decimal TotalLoaned      { get; private set; } = InitialLoanAmount;   // initial + re-loans

    public event Action BalanceChanged;

    // Called by SimulationService after each settled player bet.
    // casinoDelta = -(player's creditedProfit) — positive when player loses, negative when player wins.
    // Internally: applies delta to Bankroll; if Bankroll < BankrollTarget, triggers TryAutoRecharge().
    public void ApplyBetResult(decimal casinoDelta);

    // Auto-recharge (target-to-fill model 2): transfers (BankrollTarget − Bankroll) from MainBalance.
    // If MainBalance is insufficient, injects a new 100M SC bank loan into MainBalance first (logs debt).
    // Always succeeds (infinite credit line in Basic Mode).
    public void TryAutoRecharge();

    // Manual transfers (CasinoGamblingFinances scene)
    public bool TryTransferToBankroll(decimal amount);
    public bool TryTransferToMainBalance(decimal amount);
    public void SetBankrollTarget(decimal target);
}
```

`ApplyBetResult` is the single write path from the simulation. Internally:
```
Bankroll += casinoDelta;
if (Bankroll <= 0m) TryAutoRecharge();   // only on exhaustion — Bankroll fluctuates freely otherwise
Bankroll = Math.Max(0m, Bankroll);
BalanceChanged?.Invoke();
```

`TryAutoRecharge` (target-to-fill + re-loan):
```
decimal needed = BankrollTarget - Bankroll;
if (needed <= 0m) return;
if (MainBalance < needed)
{
    // Bank injects a new 100M SC loan
    MainBalance += InitialLoanAmount;
    LoanCount++;
    TotalLoaned += InitialLoanAmount;
}
decimal transfer = Math.Min(needed, MainBalance);
MainBalance -= transfer;
Bankroll    += transfer;
```

Persisted to `user://casino_sc_balance_state.json` via the same `Snapshot` + `FileAccess` pattern as `BankrollStateService`.

### 2.2 SC flow wiring in `SimulationService`

After the existing `BetSettled` emission for a **player** bet, add:

```csharp
// casinoDelta is the inverse of the player's credited gain/loss
decimal casinoDelta = -betEvent.CreditedProfit;
_casinoSc?.ApplyBetResult(casinoDelta);
```

`CasinoScBalanceService` is retrieved in `_Ready()` as `GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService")` and stored in `_casinoSc`. Bot bets are excluded (OQ-11.1 deferred).

### 2.3 Checkpoint extension in `BlockSessionCheckpointService`

Add two fields to the existing checkpoint snapshot:

```csharp
public decimal CasinoScMainBalance { get; set; }
public decimal CasinoScBankroll    { get; set; }
```

On `CaptureCheckpoint()`, read from `CasinoScBalanceService`. On `ApplyCheckpointToServices()`, call new `RestoreCasinoScState(decimal main, decimal bankroll)` on the service. This is fully backwards-compatible: a missing field in the JSON defaults to `0`, and the service's `EnsureInitialized()` guard fills in the defaults on first run.

### 2.4 `CasinoGamblingFinances` scene

DEV scene, full-screen, accessible from Main Menu only. Pure C# with a simple `.tscn` scaffold (anchor = full screen, `MarginContainer` → `VBoxContainer`).

```
[StatusBar]
Label  "Casino SC Gambling Finances [DEV]"

── BALANCE PANEL ──────────────────────────────────────
Label  "Main Balance:    99,000,000.00000000 SC"   _mainBalanceLabel
Label  "Bankroll:         1,000,000.00000000 SC"   _bankrollLabel
Label  "Total SC:       100,000,000.00000000 SC"   _totalLabel
Label  "P/L vs loan:             0.00000000 SC"   _plLabel   (green if ≥ 0, red if < 0)
Label  "Bank loans taken: 1   Total loaned: 100,000,000.00 SC"   _loanInfoLabel
Label  "Bankroll target:  1,000,000.00000000 SC   (auto-fills to this level)"   _targetInfoLabel

── BANKROLL TARGET SETTINGS ───────────────────────────
HBoxContainer
  Label  "Bankroll target (SC):"
  LineEdit  _bankrollTargetInput
  Button  "Set"
Label  _targetFeedback

── MANUAL TRANSFERS ───────────────────────────────────
HBoxContainer
  Label  "Amount:"
  LineEdit  _transferInput
HBoxContainer
  Button  "Main Balance → Bankroll"   _toBankrollBtn
  Button  "Bankroll → Main Balance"   _toMainBtn
Label  _transferFeedback

HSeparator
Button  "View Clients Bets History →"
Button  "View Clients Transactions →"
Button  "← Back to Main Menu"
```

Refreshes all labels on `BalanceChanged` event + a 2 s `_Process` fallback (same pattern as `BankrollProgrammer`).

### 2.5 `ClientsBetsHistory` scene

DEV scene, full-screen, accessible only from `CasinoGamblingFinances`. Requires `CasinoClientLedgerService` (§2.6).

```
[StatusBar]
Label  "Clients Bets History [DEV]"

── CASINO GLOBAL SUMMARY ──────────────────────────────
Label  "Casino since 21 Mar 2009:"
Label  "Total SC:       100,000,000.00 SC"          _overallTotalLabel
Label  "P/L vs loans:           +0.00 SC  ▲"        _overallPlLabel  (green/red + arrow)
Label  "Total SC wagered (all clients, all time):  0.00 SC"   _totalWageredAllLabel  (live, updates per bet)

── GAME FILTER ────────────────────────────────────────
HBoxContainer
  Label  "Game:"
  OptionButton  _gameFilter   [All Games | Dice | …]

── PER-CLIENT TABLE ───────────────────────────────────
ScrollContainer
  VBoxContainer  _clientRows
    [per client row — built programmatically:]
      HSeparator
      Label  "Player — gm1qXXXX...  (enrolled: 21 Mar 2009)"
      Label  "Bets: 0   Won: 0   Lost: 0   Win rate: 0.00%"
      Label  "Cumulative SC wagered (all time):    0.00000000 SC"      _wagerLifetimeLabel
      Label  "SC wagered since last deposit:       0.00000000 SC"      _wagerSinceDepositLabel
      Label  "Casino P/L with this client (all time):   +0.00 SC"     (green/red)   _plLifetimeLabel
      Label  "Casino P/L since last client deposit:     +0.00 SC"     (green/red)   _plSinceDepositLabel

── LIVE BET FEED ──────────────────────────────────────
Label  "Recent bets (live — last 50)"
ScrollContainer
  VBoxContainer  _liveFeedVBox   [Label per entry, newest first, max 50]
    "21 Mar 2009 18:15:10  Player  Dice  Bet 1.00 SC  WIN  +0.97 SC  → casino: −0.97 SC"

Button  "← Back to Casino Finances"
```

**Per-client metric derivations** (all from casino perspective — opposite sign to player):

| Metric | Source |
|---|---|
| Cumulative SC wagered (all time) | `UserStatsService.TotalWagered` (filtered by game if needed) |
| SC wagered since last deposit | `UserStatsService.TotalWagered − lastDeposit.TotalWageredSnapshot` |
| Casino P/L with client (all time) | `−UserStatsService.NetProfit` |
| Casino P/L since last client deposit | `−(UserStatsService.NetProfit − lastDeposit.NetProfitSnapshot)` |
| Total SC wagered all clients | Running counter incremented by `BetAmount` on every `BetSettled` event |

**Live feed**: subscribes to `SimulationService.BetSettled` in `_Ready()`; unsubscribes in `_ExitTree()`. Each event prepends one line to `_liveFeedVBox` (trim to 50 children) and increments `_totalWageredAllLabel`. Per-client aggregate rows refresh every 2 s from `UserStatsService` + `CasinoClientLedgerService`.

---

### 2.6 `CasinoClientLedgerService` (new autoload)

Tracks each casino client's SC deposit and withdrawal events (from the casino's operational perspective). A **deposit** is any SC transfer where the client commits funds to play; a **withdrawal** is any SC transfer back to the client's Main Balance reserve.

```csharp
// Scripts/Services/CasinoClientLedgerService.cs
public partial class CasinoClientLedgerService : Node
{
    public sealed class LedgerEntry
    {
        public string   ClientId            { get; set; }   // "player", future: referral ids
        public DateTime UtcTimestamp        { get; set; }
        public decimal  Amount              { get; set; }
        public string   Kind                { get; set; }   // "initial" | "deposit" | "withdrawal"
        // Snapshot of client betting stats at the moment of this deposit (for since-last-deposit metrics)
        public decimal  TotalWageredSnapshot { get; set; }  // 0 on withdrawals
        public decimal  NetProfitSnapshot   { get; set; }   // 0 on withdrawals
    }

    // All ledger entries (all clients, chronological).
    public IReadOnlyList<LedgerEntry> Entries { get; }

    // Registers the initial deposit on first game run (called by WalletInitializationService or a startup hook).
    public void RegisterInitialDeposit(string clientId, decimal amount, DateTime utc,
                                       decimal totalWageredSnapshot, decimal netProfitSnapshot);

    // Called whenever BankrollProgramService.TransfersChanged fires for a balance_to_bankroll transfer.
    public void RegisterDeposit(string clientId, decimal amount, DateTime utc,
                                decimal totalWageredSnapshot, decimal netProfitSnapshot);

    // Called whenever BankrollProgramService.TransfersChanged fires for a bankroll_to_balance transfer.
    public void RegisterWithdrawal(string clientId, decimal amount, DateTime utc);

    // Returns the most recent deposit entry for the given client (null if never deposited).
    public LedgerEntry? GetLastDeposit(string clientId);

    // Returns all entries for a given client, chronological.
    public IReadOnlyList<LedgerEntry> GetEntriesForClient(string clientId);

    public event Action LedgerChanged;
}
```

Persisted to `user://casino_client_ledger.json`.

**Wiring** in `BankrollProgramService._Ready()` (add `_ledger` + `_userStats` via `GetNodeOrNull`), then after each successful `TryTransferBalanceToBankroll`:
- reason `"auto_recharge"` or `"startup_default"` → `RegisterAutoRecharge` (internal recharge, NOT a player deposit; captures stats snapshots so `ClientsBetsHistory` can display "P/L since last Bankroll Recharge")
- any other reason → `RegisterDeposit` (reserved for future explicit player transfers via the SC wallet screen — no such path exists in Basic Mode v0.1 yet)

After any `TryTransferBankrollToBalance` → `RegisterWithdrawal`. On first launch, `RegisterInitialDeposit("player", 40000m, …)` is called in `_Ready()` with both snapshot fields at 0. See OQ-11.6 for the auto_recharge / baseline-reset decision.

**Note**: "deposit" in this ledger means exclusively a player-initiated, intentional SC commitment — NOT an internal bankroll top-up. The player currently deposits SC via the popup in DiceGame (adds to Main Balance). A dedicated SC wallet scene (planned, post-Basic Mode) will be the canonical deposit UI. Until then, the `"deposit"` kind is a forward-compatible stub for that future path.

---

### 2.7 `ClientsTransactions` scene

DEV scene, full-screen, accessible from `CasinoGamblingFinances`. Shows the complete SC deposit/withdrawal ledger for any registered casino client. Requires `CasinoClientLedgerService` (§2.6).

```
[StatusBar]
Label  "Clients Transactions [DEV]"

── GLOBAL TOTALS (all clients, all time) ──────────────
Label  "Total SC deposited by all clients:   40,000.00000000 SC"   _globalDepositedLabel   (live)
Label  "Total SC withdrawn by all clients:       0.00000000 SC"   _globalWithdrawnLabel   (live)

── CLIENT SELECTOR ────────────────────────────────────
HBoxContainer
  Label  "Client:"
  OptionButton  _clientSelector   [Player | …future clients]

── PER-CLIENT SUMMARY ─────────────────────────────────
Label  "Enrolled:         21 Mar 2009 18:15:06"   _enrolledLabel
Label  "Total deposited:  40,000.00000000 SC"     _totalDepositedLabel
Label  "Total withdrawn:       0.00000000 SC"     _totalWithdrawnLabel
Label  "Net commitment:   40,000.00000000 SC"     _netCommitmentLabel  (deposited − withdrawn)

── TRANSACTION LIST ───────────────────────────────────
ScrollContainer
  VBoxContainer  _txListVBox
    [per entry — newest first — built from CasinoClientLedgerService.GetEntriesForClient()]
      "21 Mar 2009 18:15:06  [INITIAL DEPOSIT]  40,000.00000000 SC  │ wager base: 0.00 SC"
      "21 Mar 2009 18:30:00  [DEPOSIT        ]     100.00000000 SC  │ wager base: 532.00 SC"
      "21 Mar 2009 18:45:00  [AUTO-RECHARGE  ]     100.00000000 SC"
      "21 Mar 2009 19:00:00  [WITHDRAWAL     ]     200.00000000 SC"

Button  "← Back to Casino Finances"
```

**Display logic**:
- **Global totals** are computed across all entries in `CasinoClientLedgerService.Entries` (all clients): `TotalDeposited = Σ Amount where Kind ∈ {initial, deposit}` (auto_recharge is excluded — it is an internal movement, not a real deposit); `TotalWithdrawn = Σ Amount where Kind == withdrawal`. Updated on `LedgerChanged`.
- Entries rendered in reverse chronological order (newest at top) for the selected client.
- Deposit rows show `TotalWageredSnapshot` as "wager base" (hidden / blank on `auto_recharge` entries since snapshot is not updated).
- Colour coding: `initial` = cyan, `deposit` = green, `auto_recharge` = gray, `withdrawal` = orange.
- `_clientSelector` is populated from registered `ICasinoClient` identifiers; switching client rebuilds per-client summary + `_txListVBox` while global totals remain the same.
- Refreshes on `CasinoClientLedgerService.LedgerChanged` event + a 2 s `_Process` fallback.

---

## 3. Files changed

| Phase | File | Action |
|---|---|---|
| 11.1 | `Scripts/Services/CasinoScBalanceService.cs` | **NEW** |
| 11.1 | `project.godot` | Register `CasinoScBalanceService` autoload after `BankrollProgramService` |
| 11.2 | `Scripts/Services/SimulationService.cs` | MOD — retrieve `_casinoSc` in `_Ready()`; route `ApplyBetResult` after player `BetSettled` |
| 11.2 | `Scripts/Services/BlockSessionCheckpointService.cs` | MOD — add two casino SC fields to snapshot; extend `CaptureCheckpoint` + `ApplyCheckpointToServices` |
| 11.3 | `Screens/CasinoGamblingFinances/CasinoGamblingFinances.tscn` | **NEW** (minimal anchor scaffold) |
| 11.3 | `Screens/CasinoGamblingFinances/CasinoGamblingFinances.cs` | **NEW** |
| 11.3 | `Scripts/Services/SceneManager.cs` | Add `CasinoGamblingFinances`, `ClientsBetsHistory`, `ClientsTransactions` to `SceneId` + `Paths` |
| 11.3 | `Screens/MainMenu/MainMenu.tscn` + `MainMenu.cs` | Add DEV button "Casino SC Finances" |
| 11.4 | `Screens/CasinoGamblingFinances/ClientsBetsHistory.tscn` | **NEW** |
| 11.4 | `Screens/CasinoGamblingFinances/ClientsBetsHistory.cs` | **NEW** (depends on 11.6) |
| 11.4 | `Screens/CasinoGamblingFinances/CasinoGamblingFinances.cs` | Add nav buttons to `ClientsBetsHistory` + `ClientsTransactions` |
| 11.6 | `Scripts/Services/CasinoClientLedgerService.cs` | **NEW** |
| 11.6 | `project.godot` | Register `CasinoClientLedgerService` autoload after `CasinoScBalanceService` |
| 11.6 | `Scripts/Services/BankrollProgramService.cs` | MOD — call `CasinoClientLedgerService.RegisterDeposit/RegisterWithdrawal` after each transfer |
| 11.7 | `Screens/CasinoGamblingFinances/ClientsTransactions.tscn` | **NEW** |
| 11.7 | `Screens/CasinoGamblingFinances/ClientsTransactions.cs` | **NEW** |
| 11.8 | `Documentation/PRIVATE_ROADMAP.md` | Mark checklist item done; add canonical decisions to §4 |
| 11.8 | `CLAUDE.md` | Add `CasinoScBalanceService` + `CasinoClientLedgerService` to Key Architecture; note DEV scenes |
| 11.8 | `AIHelperFiles/IMPLEMENTATION_ROADMAP.md` | Add Step 11 to plan inventory |

---

## 4. Phases

### Phase 11.1 — `CasinoScBalanceService` autoload

**Goal**: New autoload with correct initial balances, persist/load, auto-recharge, and manual transfer methods. No SC flow wiring yet.

**Steps**:
1. Create `Scripts/Services/CasinoScBalanceService.cs` per §2.1.
2. Register it in `project.godot` as `CasinoScBalanceService` immediately after `BankrollProgramService`.
3. Verify: on first launch (no save file), `MainBalance = 99,000,000 SC`, `Bankroll = 1,000,000 SC`; values survive a restart.

**Acceptance**:
- No existing test breaks (the service has no wiring yet).
- `GD.Print` in `_Ready()` confirms correct values on the first run: Main Balance = 99M, Bankroll = 1M, BankrollTarget = 1M, LoanCount = 1, TotalLoaned = 100M.
- Delete `user://casino_sc_balance_state.json` → fresh run shows defaults; re-run → values restored.
- Call `TryAutoRecharge()` with Main Balance = 0 → `LoanCount` increments to 2, `TotalLoaned = 200M`, Bankroll refills to `BankrollTarget`.

---

### Phase 11.2 — SC flow wiring

**Goal**: Every settled **player** bet routes the inverse of the player's profit to/from `CasinoScBalanceService.Bankroll`. The casino SC state is snapshotted at each block checkpoint.

**Steps**:
1. In `SimulationService._Ready()`, retrieve `_casinoSc = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService")`.
2. After the existing player `BetSettled` emit, add:
   ```csharp
   _casinoSc?.ApplyBetResult(-betEvent.CreditedProfit);
   ```
3. Extend `BlockSessionCheckpointService` snapshot with `CasinoScMainBalance` and `CasinoScBankroll`. On `CaptureCheckpoint`, populate from `CasinoScBalanceService`. On `ApplyCheckpointToServices`, call `RestoreCasinoScState(snapshot.CasinoScMainBalance, snapshot.CasinoScBankroll)`.
4. Add `RestoreCasinoScState(decimal main, decimal bankroll)` to `CasinoScBalanceService` (sets both fields directly, bypasses auto-recharge, does not persist — mirrors the existing pattern for the player's checkpoint restore).

**Acceptance** (manual in-engine test):
1. Start autobet → watch `CasinoScBalanceService.Bankroll` change each bet (opposite sign to player profit). Verify by reading `GD.Print` in `ApplyBetResult`.
2. Mine a block → restart the app → casino SC restores to checkpoint values (not current values).
3. Set casino Bankroll to 0 manually via a temporary dev shortcut → auto-recharge fills bankroll back up to `BankrollTarget`.
4. Drain both Bankroll and Main Balance to 0 → a re-loan fires automatically, `LoanCount` increments, game continues.

---

### Phase 11.3 — `CasinoGamblingFinances` DEV scene

**Goal**: Navigable DEV scene from Main Menu displaying all casino SC financials plus auto-recharge and manual transfer controls.

**Steps**:
1. Create `Screens/CasinoGamblingFinances/CasinoGamblingFinances.tscn` — minimal scaffold: `Control` (anchor = full screen) → `MarginContainer` → `VBoxContainer` with `%StatusBarPlaceholder HBoxContainer`.
2. Create `CasinoGamblingFinances.cs` per §2.4. Subscribe to `CasinoScBalanceService.BalanceChanged`; unsubscribe in `_ExitTree()`.
3. Add `CasinoGamblingFinances`, `ClientsBetsHistory`, and `ClientsTransactions` to `SceneManager.SceneId` and `Paths`.
4. Add a "Casino SC Finances [DEV]" button to `MainMenu.tscn` / `MainMenu.cs`. Wire to `_sceneManager.Go(SceneManager.SceneId.CasinoGamblingFinances)`.

**Acceptance**:
- Main Menu shows the new DEV button.
- Scene shows correct live values (updated on every `BalanceChanged` event).
- "Main Balance → Bankroll" and "Bankroll → Main Balance" transfer buttons function correctly; feedback label shows success or rejection reason.
- "Set" `BankrollTarget` button validates input (positive decimal) and reflects in `_targetInfoLabel`; subsequent auto-recharges use the new target.
- Loan info label increments correctly after a forced re-loan test.
- "View Clients Bets History →" button is present (wired in Phase 11.4); "View Clients Transactions →" button is present (wired in Phase 11.7). Both may be non-functional stubs at this phase checkpoint.

---

### Phase 11.4 — `ClientsBetsHistory` DEV scene

**Goal**: Per-client P/L aggregates (all-time + since-last-deposit), wagering totals, and real-time live bet feed. Accessible from `CasinoGamblingFinances`. **Depends on Phase 11.6** (`CasinoClientLedgerService`) for the since-last-deposit metrics.

> Build this phase after Phase 11.6, even though it is numbered earlier. Alternatively, implement in two passes: Phase 11.4a (basic aggregates, no ledger dependency) before 11.6, then Phase 11.4b (since-last-deposit metrics) after 11.6.

**Steps**:
1. Create `Screens/CasinoGamblingFinances/ClientsBetsHistory.tscn` (same anchor pattern as Phase 11.3).
2. Create `ClientsBetsHistory.cs` per §2.5:
   - In `_Ready()`: retrieve `_casinoSc`, `_userStats`, `_ledger` autoloads; build the global summary banner; build per-client rows; subscribe to `SimulationService.BetSettled`.
   - On each `BetSettled`: prepend to live feed (trim to 50), increment `_totalWageredAll`, schedule a row refresh.
   - Per-client row data: all-time metrics from `UserStatsService`; since-last-deposit metrics from `CasinoClientLedgerService.GetLastDeposit(clientId)`.
   - Game filter dropdown: `SelectedIndex == 0` = All Games; filters both live feed entries and per-row stats.
   - In `_ExitTree()`: unsubscribe from `BetSettled`.
3. Add "View Clients Bets History →" nav button to `CasinoGamblingFinances.cs` and wire it to `SceneManager.SceneId.ClientsBetsHistory`. (The "View Clients Transactions →" button is added and wired in Phase 11.7 when that scene exists.)

**Acceptance**:
- Global "Total SC wagered (all clients)" updates on every bet during autobet.
- Casino overall P/L matches `CasinoScBalanceService.CumulativeProfitSinceLoan` (green/red + arrow).
- Per-client row: lifetime wagered and P/L match `UserStatsService`; since-last-deposit wagered resets correctly when a new deposit fires.
- "Casino P/L since last deposit" is `−(currentNetProfit − lastDeposit.NetProfitSnapshot)` — verified by making a manual deposit and then placing a known set of bets.
- Live feed prepends an entry within 1 second of each settle during autobet.
- Switching game scenes and returning: live feed has new entries (subscription survives navigation).

---

### Phase 11.5 — *(renumbered to 11.8 — see below)*

---

### Phase 11.6 — `CasinoClientLedgerService` autoload

**Goal**: New autoload that records every casino-client SC deposit and withdrawal with stat snapshots. Prerequisite for since-last-deposit metrics in both `ClientsBetsHistory` and `ClientsTransactions`.

**Steps**:
1. Create `Scripts/Services/CasinoClientLedgerService.cs` per §2.6.
2. Register in `project.godot` immediately after `CasinoScBalanceService`.
3. **Initial deposit wiring**: in `CasinoClientLedgerService._Ready()`, if no entries exist for `"player"`, call `RegisterInitialDeposit("player", 40000m, DateTime.UtcNow, 0m, 0m)` — records the player's total starting capital as the first ledger event.
4. **Ongoing transfer wiring**: in `BankrollProgramService.TryTransferBalanceToBankroll()`, after a successful transfer:
   - reason `"auto_recharge"` or `"startup_default"` → `RegisterAutoRecharge("player", amount, utcNow, wageredSnapshot, profitSnapshot)` — internal recharge, does NOT reset the since-last-deposit baseline; snapshots captured so "P/L since last Bankroll Recharge" can be computed in `ClientsBetsHistory`.
   - any other reason → `RegisterDeposit(...)` — reserved for future explicit player transfers (SC wallet screen, planned post-Basic Mode).
   - In `TryTransferBankrollToBalance()` → `RegisterWithdrawal("player", amount, utcNow)`.
   
   **Key distinction**: "deposit" = player consciously committing funds to play (manual, future UI). Auto-recharges and startup init are internal mechanics invisible to the player as "deposits".
   - Access `CasinoClientLedgerService` from `BankrollProgramService` via `GetNodeOrNull` in `_Ready()`, same pattern as all other autoloads.
5. Persist to `user://casino_client_ledger.json`.

**Acceptance**:
- Fresh game start → ledger has exactly one entry: kind = `"initial"`, amount = 40,000 SC, both snapshots = 0.
- Make a manual deposit of 100 SC → new `"deposit"` entry with the current `TotalWagered` snapshot.
- Make a manual withdrawal → new `"withdrawal"` entry (no snapshots).
- Restart app → entries persist correctly.
- `GetLastDeposit("player")` returns the most recent deposit/initial entry; `GetEntriesForClient("player")` returns all in chronological order.

---

### Phase 11.7 — `ClientsTransactions` DEV scene

**Goal**: Full SC transaction ledger per casino client, accessible from `CasinoGamblingFinances`. Requires Phase 11.6.

**Steps**:
1. Create `Screens/CasinoGamblingFinances/ClientsTransactions.tscn` (same anchor scaffold as Phase 11.3).
2. Create `ClientsTransactions.cs` per §2.7:
   - In `_Ready()`: retrieve `_ledger` autoload; populate `_clientSelector` from `ICasinoClient` list (currently just player); subscribe to `LedgerChanged`.
   - `OnClientSelected(idx)`: rebuilds summary labels and `_txListVBox` for the selected client.
   - `BuildTxList(clientId)`: iterates `_ledger.GetEntriesForClient(clientId)` in reverse order; renders each entry with colour-coded kind label and wager-base annotation.
   - In `_ExitTree()`: unsubscribe from `LedgerChanged`.
3. Add "View Clients Transactions →" nav button to `CasinoGamblingFinances.cs` and wire it to `SceneManager.SceneId.ClientsTransactions`.

**Acceptance**:
- Global "Total SC deposited by all clients" = 40,000 SC on first launch (initial deposit only).
- Global "Total SC withdrawn by all clients" = 0 SC on first launch.
- After a manual deposit: global deposited total increments; new deposit entry appears for the selected client.
- After an auto-recharge: gray `[AUTO-RECHARGE]` entry appears; global deposited total increments; per-client `Net commitment` increments; no wager-base annotation shown.
- After a manual withdrawal: global withdrawn total increments; orange `[WITHDRAWAL]` entry appears (no wager-base annotation).
- Per-client summary (`Total deposited`, `Total withdrawn`, `Net commitment`) matches the sum of that client's entries.
- Switching clients rebuilds the per-client panel; global totals do not change.

---

### Phase 11.8 — Documentation *(was 11.5)*

**Goal**: Update all relevant docs to reflect the new services and scenes.

**Steps**:
1. **`Documentation/PRIVATE_ROADMAP.md`**: mark Step 11 checklist item as done in §6; add canonical decisions to §4.
2. **`CLAUDE.md`** Key Architecture section: add `CasinoScBalanceService` + `CasinoClientLedgerService` rows to the autoload table with persisted paths; note all three new DEV scenes under Scene Management.
3. **`AIHelperFiles/IMPLEMENTATION_ROADMAP.md`**: add Step 11 to the plan inventory table with status ✅ Done.

---

## 5. Phase checklist

**Recommended execution order**: 11.1 → 11.2 → 11.3 → 11.6 → 11.4 → 11.7 → 11.8

- [x] **Phase 11.1** — `CasinoScBalanceService` autoload: new file, project.godot registration, defaults, persist/load, target-to-fill auto-recharge + 100M re-loan, manual transfers, `BalanceChanged` event
- [x] **Phase 11.2** — SC flow wiring: `SimulationService` routes `ApplyBetResult` per player bet; `BlockSessionCheckpointService` snapshots/restores casino SC at each block
- [x] **Phase 11.3** — `CasinoGamblingFinances` DEV scene: Main Menu navigation, balance/loan panel, `BankrollTarget` controls, manual transfer panel, stub nav buttons for both sub-scenes; `SceneManager` updated for all three scene IDs
- [x] **Phase 11.6** — `CasinoClientLedgerService` autoload: new file, initial deposit recording, deposit/withdrawal wiring in `BankrollProgramService`, persist/load, `LedgerChanged` event
- [x] **Phase 11.4** — `ClientsBetsHistory` DEV scene: global wager total (live), per-client aggregate rows (all-time + since-last-deposit metrics), game filter, live bet feed, back navigation
- [x] **Phase 11.7** — `ClientsTransactions` DEV scene: global deposited/withdrawn totals, per-client ledger view (deposits/withdrawals only — auto-recharges recorded but hidden from the visible list), colour-coded entries with wager-base annotations, client selector; wires "View Clients Transactions →" button in `CasinoGamblingFinances`
- [x] **Phase 11.8** — Documentation: PRIVATE_ROADMAP.md, CLAUDE.md (both new autoloads + all three scenes), IMPLEMENTATION_ROADMAP.md

---

## 6. Open design questions

### OQ-11.1 — Should bot bets also route through the casino SC bankroll?

Bots bet against the same house, so by the same logic, bot losses should go to the casino bankroll and bot wins should be paid from it.

**Arguments for**: The casino SC becomes a true closed-economy ledger. If bots collectively run hot, the casino bankroll reflects the pressure — which is more realistic and makes the internal simulation coherent.

**Arguments against**: Bot SC is a simulation artifact (no real player is earning/losing it). Adding bot routing increases the rate of casino SC change dramatically (bots bet continuously), which may dwarf the player's effect and make the casino bankroll meaningless as a "player interaction" measure. The user description focused on player bets.

**Current plan**: player bets only (Phase 11.2). Revisit when bot P/L reporting becomes part of a larger economy simulation milestone or when `bot-play-history-plan.md` expands scope.

---

### OQ-11.2 — Casino bankruptcy / bank debt mechanic — partially resolved

**Basic Mode v0.1 behavior (resolved, implemented in D7 + Phase 11.1)**:
When Main Balance cannot cover the target-to-fill transfer, the bank injects a new **100M SC loan** into Main Balance automatically. `LoanCount` increments; `TotalLoaned` grows by 100M. Game continues without interruption. This is a flavour event — no bet is ever blocked.

**Post–Basic Mode v0.1 design questions (deferred to P6)**:
- What is the debt repayment threshold? (e.g., TotalSc ≥ TotalLoaned? Some fraction of TotalLoaned?)
- Is there an interest rate, and if so, does it compound per in-game time unit or per real-time unit?
- Once deep in debt, does the casino's bet-coverage ability degrade (e.g., max player payout capped)?
- Does accumulated debt unlock a "renegotiation" event or a new credit-line tier?

None of the post-Basic questions block this step. `LoanCount` and `TotalLoaned` are tracked now so P6 can build the repayment mechanic on top without a schema migration.

---

### OQ-11.3 — Player "enrollment" for multi-player future

Today there is one player. `ClientsBetsHistory` derives enrollment from `UserStatsService` first-bet timestamp. When Miner Referral nodes (Casino Referral System) begin betting as casino clients:
- Each Miner Referral needs its own first-bet record (either from `BetHistoryRepository` or from a separate `CasinoClientRegistry`).
- The `ClientsBetsHistory` client list must expand beyond a single hardcoded player row.

**Current plan (Phase 11.4)**: build the client table structure to be keyed by `playerId` with a list of `ICasinoClient` identifiers. For now the list has one entry (the human player). The interface is `{ string PlayerId, string DisplayName, DateTime EnrollmentDateLocal }` — a forward-compatible stub. Actual multi-client population waits until the Casino Referral System ships.

---

### OQ-11.4 — Casino P/L display: game-time or real-time reference? — resolved

**Decision**: show cumulative number + casino-open date label only. The live date range ("21 Mar 2009 → current game date") can be added as a cosmetic label later without any service changes.

**Why the P/L is coherent as-is**: `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` is computed from the live casino SC balance, which is snapshotted and restored at every block checkpoint. This means:
- P/L correctly resets to 0 when the game world is reset (`WorldFormatVersion` bump re-initialises the service from defaults). That is the right behaviour — a world reset is a fresh casino.
- Between blocks, P/L is live (in-memory, not on disk). Exactly the same model as the player's SC balance.
- Because blocks carry timestamps, the Block Explorer already provides a block-indexed audit trail of when SC changed hands. The casino P/L does not need its own time-series; it rides the same checkpoint model.

**"Does not age" clarification**: P/L is always the delta from `TotalLoaned` (not from a fixed wall-clock date). As more loans are taken, `TotalLoaned` grows and the P/L threshold shifts — the figure always shows "how much ahead/behind the casino is vs its cumulative borrowing", which is the correct economic meaning. No temporal windowing is needed.

---

### OQ-11.5 — Auto-recharge model — resolved ✅

**Decision: target-to-fill (model 2)**, implemented in Phase 11.1.

Each auto-recharge transfers exactly `BankrollTarget − Bankroll` from Main Balance. Default `BankrollTarget = 1,000,000.00 SC`. Configurable at runtime from `CasinoGamblingFinances`. If Main Balance cannot cover the transfer, the 100M SC re-loan fires first (D7). The field is persisted as `BankrollTarget` in `casino_sc_balance_state.json`.

---

### OQ-11.6 — Do auto-recharges count as separate deposit events in the client ledger?

When the player's `BankrollProgramService` auto-recharges the bankroll (reason = `"auto_recharge"`, direction = `balance_to_bankroll`), `CasinoClientLedgerService` will record it as a deposit entry — the same path as a manual deposit.

**Why this matters for since-last-deposit metrics**: if the casino logs every auto-recharge as a deposit, then `SC wagered since last deposit` and `Casino P/L since last deposit` will reset very frequently (every time the bankroll exhausts), making those metrics close to zero on most rows. If auto-recharges are excluded, only intentional manual deposits (or the initial one) set the reference baseline — giving the "since-last-deposit" metrics a longer and more meaningful window.

**Options**:
1. **Log all transfers as deposits** (uniform, simple): every `balance_to_bankroll` is a deposit entry regardless of reason. `Since-last-deposit` metrics reset often.
2. **Log only manual deposits + initial** (meaningful window): filter to `reason != "auto_recharge"`. Auto-recharges are still recorded in `BankrollProgramService.Records` but not in the casino ledger.
3. **Log auto-recharges as a separate entry kind** (`"auto_recharge"`) without updating the `TotalWageredSnapshot`/`NetProfitSnapshot` fields: they appear in `ClientsTransactions` for visibility but don't reset the since-last-deposit baseline.

**Decision: option 3 ✅** — auto-recharges are recorded with `Kind = "auto_recharge"` and appear in `ClientsTransactions` for full operator visibility, but `TotalWageredSnapshot` and `NetProfitSnapshot` are not updated (they carry the values from the prior intentional deposit). `GetLastDeposit(clientId)` filters to `Kind == "initial" || Kind == "deposit"` only, so the since-last-deposit baseline is never shifted by an auto-recharge.
