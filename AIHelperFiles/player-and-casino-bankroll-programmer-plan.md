# BankrollProgrammer + CasinoGamblingFinances UI Improvements + DiceGame Bankroll Display Fix — Implementation Plan

**Status**: ✅ READY TO IMPLEMENT

**Scope**: Six targeted changes across three scenes. `CasinoScBalanceService` gains a loan-history log; no other new services.

- **(a)** DiceGame: bankroll label shows `100.00 SC` on every re-entry when session is stopped — one-line guard.
- **(b)** BankrollProgrammer: add a read-only "current auto-recharge dose" label that updates on apply; dose > Main Balance is blocked at set time (player only — casino has an infinite credit line).
- **(c)** BankrollProgrammer: remove the minimum bankroll reserve that blocks Bankroll → Main Balance transfers below the dose amount.
- **(d)** BankrollProgrammer: add a "Manual Recharge" row (Main Balance → Bankroll) with immediate effect.
- **(e)** CasinoGamblingFinances: surface the Bankroll Auto-Recharge Target as a standalone value label (parallel to player's dose label); pre-populate the target input on scene load.
- **(f)** CasinoGamblingFinances: add loan history (game-dated, auto + manual), a Manual Loan button (100M SC default), and fix all date displays to use game time (CalendarTimeService) rather than wall-clock time.

**Created**: 2026-06-30.

**Dependencies**: `CasinoScBalanceService` gains a `LoanRecord` inner class and `LoanHistory` list + `TriggerManualLoan()` + CalendarTimeService reference. All other changes are UI-only. Touches `BankrollProgrammer.cs`, `BankrollProgrammer.tscn`, `DiceGame.cs`, `CasinoGamblingFinances.cs`, `CasinoGamblingFinances.tscn`, and `CasinoScBalanceService.cs`.

**Companion docs**: `Documentation/GLOSSARY.md` (SC Deposit vs Auto-Recharge distinction) · `AIHelperFiles/background-simulation-plan.md` (SimulationService context) · `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md` (casino SC architecture).

---

## 0. Decisions locked

| # | Question | Decision |
|---|---|---|
| **D1** | Bankroll→Main Balance: remove reserve entirely or just lower it? | **Remove entirely.** The player can drain bankroll to 0; the game-over condition is `Main Balance + Bankroll = 0`, so the natural limit is sufficient. Optionally surface a hint when the result leaves bankroll at 0. |
| **D2** | Manual Recharge (Main Balance→Bankroll): ledger entry type? | **`"manual_recharge"`** reason string passed to `TryTransferBalanceToBankroll()`. This routes to `_ledger?.RegisterDeposit()` in `BankrollProgramService` (same path as other non-internal transfers). If a separate reason string is needed for analytics, change to `"manual_recharge_from_ui"` — but current code only special-cases `"auto_recharge"` and `"startup_default"`, so any other string is fine. |
| **D3** | Dose label position in the VBox? | **Between `BankrollValue` and `PerformanceValue`** so it reads: Main Balance → Bankroll → Current Dose → Performance. The dose is metadata about the bankroll, so it belongs near it. |
| **D4** | DiceGame fix: static flag vs. checking `BankrollStateService._initialized`? | **Static flag** (`_bootstrapAppliedThisSession`), same pattern as `_checkpointRestoreSpentThisSession`. The `_initialized` field on `BankrollStateService` is private. The flag is cleaner and self-documenting. |
| **D5** | UI language for BankrollProgrammer? | **English everywhere.** Fix all Spanish strings (button labels, placeholder text, status messages) in the same pass. `CultureInfo.InvariantCulture` on all format calls. |
| **D6** | Subscribe to `AutoRechargeAmountChanged` event in BankrollProgrammer? | **Yes.** Currently only `TransfersChanged` is subscribed. The dose label must also refresh when the dose changes from elsewhere (e.g. a future API). Subscribe/unsubscribe symmetrically in `_Ready()`/`_ExitTree()`. |
| **D7** | Casino: standalone target label vs. keeping value inside `TargetInfoLabel` text? | **Standalone label.** Split `TargetInfoLabel` into two: a `BankrollTargetValueLabel` (font 24, just the number — mirrors player's `AutoRechargeDoseValue`) and a smaller `TargetHintLabel` (font 18) with only the behavior explanation "auto-fills to this level when bankroll hits 0". The number needs to be scannable at a glance next to Main Balance and Bankroll. |
| **D8** | Casino: pre-populate `_bankrollTargetInput` in `_Ready()`? | **Yes.** `BankrollProgrammer` pre-populates `_autoRechargeAmountInput` with the current dose; casino must match. Without it, the player has to know the current target before typing a new one — a needless friction in a DEV screen. |
| **D9** | Casino: does `TryTransferToMainBalance()` need a reserve guard removed? | **No — already has none.** `CasinoScBalanceService.TryTransferToMainBalance()` allows transfer of any amount `≤ Bankroll`. No code change needed; this is a "already correct" note for the testing checklist. |
| **D10** | Casino: add transfer history list like player's `TransfersList`? | **No — out of scope.** The casino does not have a `BankrollProgramService`-equivalent transfer log; historical casino SC movements are observable through `ClientsTransactions`. Adding a log would require a new `List<TransferRecord>` on `CasinoScBalanceService` and is a separate feature. Deferred as OQ-CG.1. |
| **D11** | Should the Manual Recharge field clear after a successful transfer? | **Yes — clear it.** `_manualRechargeToBankrollInput.Text = "";` after a successful `OnManualRechargeToBankrollPressed()`. (OQ-BP.1 resolved.) |
| **D12** | How should Manual Recharge transfers be recorded in `CasinoClientLedgerService`? | **As `"auto_recharge"` kind, not `"deposit"`.** Per GLOSSARY: "Bankroll Manual Recharge does not count as an SC Deposit — it is an internal movement between the player's own sub-accounts." In `BankrollProgramService.TryTransferBalanceToBankroll()`, add `"manual_recharge"` to the `isInternalRecharge` check so it routes to `_ledger?.RegisterAutoRecharge()`, not `_ledger?.RegisterDeposit()`. (OQ-BP.2 resolved.) |
| **D13** | Should setting a dose larger than the current Main Balance be blocked? | **Yes — blocking at set time, player only.** In `OnApplyAutoRechargeAmountPressed()`, validate `amount ≤ _principalBalanceService.CurrentBalance` before calling `SetAutoRechargeAmount()`. If over, show an error: `"Dose exceeds available Main Balance ({balance} SC). Enter a lower amount."` This makes the constraint visible immediately rather than at the next auto-recharge failure. **Does NOT apply to the casino**: the casino has an infinite bank credit line (`TryAutoRecharge()` auto-injects a 100M SC loan whenever needed) and is never blocked. (OQ-BP.3 resolved.) |
| **D14** | Casino: where does game-date context come from in `CasinoScBalanceService`? | **Injected via `_Ready()` autoload reference.** `CasinoScBalanceService` already extends `Node`; add `private CalendarTimeService _calendarTime;` and wire it in `_Ready()` with `GetNodeOrNull`. All loan records use `_calendarTime?.CurrentLocalDateTime ?? DateTime.Now` as their game date. |
| **D15** | Casino: should the initial 100M SC startup loan appear in `LoanHistory`? | **No — only new loans from this version forward.** The existing `LoanCount = 1` / `TotalLoaned = 100M` totals cover all historical loans including the initial one. `LoanHistory` is a forward-only detail log. If `LoanHistory.Count < LoanCount` the UI notes "N historical loans not in this log" so the gap is transparent. |
| **D16** | Casino: should `TriggerManualLoan()` also trigger a bankroll recharge? | **No — it only tops up Main Balance.** The operator clicks "Request Loan" to add funds to Main Balance, then uses the existing "Main Balance → Bankroll" transfer button to move funds. This keeps the two actions independent and auditable. The auto-loan in `TryAutoRecharge()` does both because it fires during a live bet settlement where the bankroll is already 0 and immediate coverage is required. |
| **D17** | Casino: what amount does `TriggerManualLoan()` accept? | **User-specified, default pre-filled as `InitialLoanAmount` (100M SC).** The input is pre-populated with `100,000,000` on scene load. The user can change it. Invalid or blank input defaults to `InitialLoanAmount`. Post Basic Mode v0.1: configurable default (OQ-CG.3). |
| **D18** | Casino: game date display — where and what format? | **Add a `GameDateLabel` at the top of `CasinoGamblingFinances` (below the title/separator), updated every `_Process` tick via `_calendarTime?.CurrentLocalDateTime`.** Format: `yyyy-MM-dd HH:mm:ss`. Loan history entries show `yyyy-MM-dd` in game time. No wall-clock dates are ever shown in this scene. |

---

## 1. Root cause analysis

### 1.1 DiceGame — bankroll shows `100.00 SC` on re-entry

**Mechanism**: `ApplyRealtimeBootstrapFromLoadedHistory()` runs every time `DiceGame._Ready()` fires. It calls `_userStatsService.GetLatestKnownBalance(current)`, which scans all `BetRecord.BalanceAfter` and `DepositRecord.BalanceAfter` entries in the loaded history chunk and returns the most recent one.

The most recent recorded balance is often exactly the auto-recharge amount (`100.00 SC`) because:
- The session hits `InsufficientBalance`, auto-recharge fires and records a deposit with `BalanceAfter = 100 SC`.
- High-frequency-mode throttles bet recording, so the bets following the recharge may not yet be written to history before the session is stopped.

When the player navigates back to DiceGame, `ApplyRealtimeBootstrapFromLoadedHistory()` calls `_wallet.SetBalanceForTimeTravel(100m)` (which does NOT fire `BalanceDeltaChanged`) and `_bankrollStateService?.SetBalance(100m)`, silently overwriting the correct persisted value. `UpdateAllUI()` then displays `100.00 SC`. The first bet fires `ApplyTransaction()` → `BalanceDeltaChanged` → `UpdateBalanceUI()`, which re-reads the (now-correct-after-the-bet) wallet value, and the display recovers.

**Fix**: guard `ApplyRealtimeBootstrapFromLoadedHistory()` with a static `_bootstrapAppliedThisSession` flag so it runs only on the very first `DiceGame._Ready()` call per app process. The `BankrollStateService` persists its value on every `SetBalance()` call and is the authoritative source of truth for re-entry.

### 1.2 BankrollProgrammer — dose label doesn't update

There is no dedicated read-only label for the current auto-recharge dose. The `AutoRechargeAmountInput` field is populated from `_bankrollProgramService.AutoRechargeAmount` once in `_Ready()`, but `RenderAll()` never updates the input text. `_statusValue` shows a transient "updated" message but is overwritten by subsequent status messages. Adding a dedicated `AutoRechargeDoseValue` label that `RenderAll()` writes to on every call is the clean solution.

### 1.3 BankrollProgrammer — minimum reserve blocks transfer

`OnTransferToBalancePressed()` computes:
```csharp
decimal reserve = Money.Normalize(_bankrollProgramService?.AutoRechargeAmount ?? …);
decimal maxTransferLeavingReserve = Math.Max(0m, currentBankroll - reserve);
decimal effectiveAmount = Math.Min(amount, maxTransferLeavingReserve);
if (effectiveAmount <= 0m) { _statusValue.Text = "No hay saldo …"; return; }
```
This prevents the user from transferring the last `AutoRechargeAmount` worth of bankroll. Per D1, remove this reserve logic and allow transfer up to `currentBankroll`.

### 1.4 BankrollProgrammer — missing Manual Recharge row

The scene has a Bankroll→Main Balance row but no Main Balance→Bankroll row for manual top-ups. Adding it mirrors the existing pattern: a new `HBoxContainer` row in the `.tscn` + a new handler in the `.cs`.

### 1.5 CasinoGamblingFinances — target value buried in explanatory text

**What's already correct**: `CasinoGamblingFinances` is architecturally ahead of the player's scene in several ways — both transfer directions already exist (one shared input, two buttons), `TryTransferToMainBalance()` has no minimum reserve, and the `BalanceChanged` event already drives all label refreshes in real time (no polling lag). No structural changes are needed for transfers.

**What's missing**: `TargetInfoLabel` renders the current `BankrollTarget` value embedded inside a sentence:
```
"Bankroll target: 1,000,000.00000000 SC   (auto-fills to this level on exhaustion)"
```
This makes the current target unreadable at a glance — the number must be parsed out of prose. The player's `BankrollProgrammer` will have a standalone `AutoRechargeDoseValue` label at the same visual weight as `MainBalanceLabel` and `BankrollLabel`; the casino scene must show `BankrollTarget` the same way.

Additionally, `_bankrollTargetInput` is not pre-populated in `_Ready()`. The player must know the current target value before they can decide whether to change it, forcing an unnecessary mental look-up (or a read of `TargetInfoLabel`).

**Fix**: split `TargetInfoLabel` into `BankrollTargetValueLabel` (standalone number, font 24) + `TargetHintLabel` (explanation only, font 18); pre-populate `_bankrollTargetInput` in `_Ready()`.

### 1.6 CasinoGamblingFinances — loan history and game-date context missing

The auto-loan mechanism already exists in `TryAutoRecharge()`. Two sequential conditions determine when it fires:

1. **Auto-recharge trigger** (`ApplyBetResult`): after applying a bet result, if `Bankroll + casinoDelta ≤ 0` — a player win exceeds the casino's remaining bankroll, pushing it negative.
2. **Loan trigger** (inside `TryAutoRecharge()`): `MainBalance < (BankrollTarget − Bankroll)` — the Main Balance cannot cover the amount needed to fill the bankroll back to `BankrollTarget`. Since `Bankroll` is still the raw negative value at this point (the `Math.Max(0m, …)` clamp runs *after* the recharge), `needed` is inflated accordingly. For example: Bankroll = −200,000 → needed = 1,000,000 − (−200,000) = 1,200,000; if MainBalance = 800,000 SC, the loan fires.

Neither trigger has anything to do with absolute zero: the loan fires based on relative magnitudes — the win amount vs. remaining bankroll, and the refill amount vs. available Main Balance. However:

1. **No game-date log**: loans are only counted and summed; there is no per-event record with a game-time timestamp. The operator cannot see when a loan fired relative to the 2009 game timeline.
2. **No manual loan UI**: to manually inject capital, the operator has no button — they can only trigger "Main Balance → Bankroll" transfers (which don't add new money, they only move existing funds).
3. **Dates in the scene are wall-clock dates** (or absent): loan records, if added, must use `CalendarTimeService.CurrentLocalDateTime`, not `DateTime.Now`. A `GameDateLabel` should show the current in-game date so all displayed figures are in the correct temporal context.

**Fix**: add `LoanRecord` + `LoanHistory` to `CasinoScBalanceService`, wire `CalendarTimeService` in `_Ready()`, log auto-loans and manual loans with game date, expose `TriggerManualLoan()`, add `GameDateLabel` + `ManualLoanRow` + `LoanHistoryList` to the scene.

---

## 2. Target architecture — service change summary

| File | Change type |
|---|---|
| `DiceGame.cs` | One guard line (`_bootstrapAppliedThisSession`) |
| `BankrollProgrammer.cs` / `.tscn` | New label, new row, reserve removal, English text, dose validation |
| `Scripts/Services/BankrollProgramService.cs` | One line: add `"manual_recharge"` to the `isInternalRecharge` check |
| `Scripts/Services/CasinoScBalanceService.cs` | New: `LoanRecord` class, `_loanHistory` list, `CalendarTimeService` reference, `TriggerManualLoan()`, loan logging in `TryAutoRecharge()`, persistence of `LoanHistory` in `Snapshot` |
| `CasinoGamblingFinances.cs` / `.tscn` | New: `GameDateLabel`, `BankrollTargetValueLabel`, `ManualLoanRow`, `LoanHistoryList`; pre-populate target input; wire `CalendarTimeService` |

`BankrollProgramService.TryTransferBalanceToBankroll()` is the correct API for the player's Main Balance → Bankroll. `CasinoScBalanceService.TryTransferToBankroll()` is the correct API for the casino's — both are direct atomic operations, no new wrapper needed.

---

## 3. Phase checklist

### Phase BP.1 — DiceGame: bankroll display on re-entry

**Files**: `Screens/DiceGame/DiceGame.cs`

- [x] **BP.1.1** Add static field `private static bool _bootstrapAppliedThisSession;` alongside `_checkpointRestoreSpentThisSession`.
- [x] **BP.1.2** At the top of `ApplyRealtimeBootstrapFromLoadedHistory()`, add the guard:
  ```csharp
  if (_bootstrapAppliedThisSession) return;
  _bootstrapAppliedThisSession = true;
  ```
  This mirrors the `_checkpointRestoreSpentThisSession` pattern exactly. The bootstrap is only meaningful on a cold app start when the services may not have a persisted value yet.

**Verify**: stop autobet, navigate to BankrollProgrammer and back, confirm bankroll label matches the correct persisted value immediately on re-entry without placing a bet.

---

### Phase BP.2 — BankrollProgrammer: current dose label

**Files**: `Screens/BankrollProgrammer/BankrollProgrammer.tscn`, `Screens/BankrollProgrammer/BankrollProgrammer.cs`

- [x] **BP.2.1** In `.tscn`, add a new `Label` node immediately after `BankrollValue` and before `PerformanceValue`:
  ```
  [node name="AutoRechargeDoseValue" type="Label" parent="VBox"]
  unique_name_in_owner = true
  layout_mode = 2
  theme_override_font_sizes/font_size = 24
  text = "0.00000000"
  ```
- [x] **BP.2.2** In `.cs`, add private field `private Label _autoRechargeDoseValue;` alongside `_bankrollValue`.
- [x] **BP.2.3** In `_Ready()`, wire it: `_autoRechargeDoseValue = GetNode<Label>("%AutoRechargeDoseValue");`
- [x] **BP.2.4** In `_Ready()`, subscribe to the dose-change event (add after the `TransfersChanged` subscription):
  ```csharp
  _bankrollProgramService.AutoRechargeAmountChanged += RenderAll;
  ```
- [x] **BP.2.5** In `_ExitTree()`, unsubscribe symmetrically:
  ```csharp
  _bankrollProgramService.AutoRechargeAmountChanged -= RenderAll;
  ```
- [x] **BP.2.6** In `RenderAll()`, add after the `_bankrollValue.Text` line (also add a null guard for `_autoRechargeDoseValue`):
  ```csharp
  _autoRechargeDoseValue.Text = (_bankrollProgramService?.AutoRechargeAmount ?? 0m)
      .ToString("N8", CultureInfo.InvariantCulture);
  ```
- [x] **BP.2.7** Add `_autoRechargeDoseValue` to the `GodotObject.IsInstanceValid` guard at the top of `RenderAll()`.
- [x] **BP.2.8** Fix all English text in the `.tscn` and `.cs`:
  - `placeholder_text`: `"Auto recarga"` → `"Dose amount (SC)"`
  - `"Aplicar auto recarga"` → `"Set Auto-Recharge Dose"`
  - `"Monto BR -> BAL"` → `"Amount (Bankroll → Main Balance)"`
  - `"Transferir a balance"` → `"Transfer to Main Balance"`
  - Status messages in `OnApplyAutoRechargeAmountPressed()`: English, `CultureInfo.InvariantCulture` on all format calls.
  - Status messages in `OnTransferToBalancePressed()`: English, `CultureInfo.InvariantCulture`.
- [x] **BP.2.9** In `OnApplyAutoRechargeAmountPressed()`, add a blocking validation after parsing `amount` but before calling `SetAutoRechargeAmount()`. If `amount > _principalBalanceService?.CurrentBalance`, reject immediately:
  ```csharp
  decimal mainBalance = Money.Normalize(_principalBalanceService?.CurrentBalance ?? 0m);
  if (amount > mainBalance)
  {
      _statusValue.Text = string.Create(CultureInfo.InvariantCulture,
          $"Dose exceeds available Main Balance ({mainBalance:N8} SC). Enter a lower amount.");
      return;
  }
  ```
  This prevents setting an unreachable dose. The constraint is dynamic — if Main Balance later drops below the dose (from bets), a smaller dose must be configured before the next auto-recharge fires. That is intentional and sufficient for Basic Mode.

**Verify**: set dose to a value exceeding Main Balance — blocked with error message. Set dose to a valid value — `AutoRechargeDoseValue` label updates immediately.

---

### Phase BP.3 — BankrollProgrammer: remove minimum bankroll reserve

**Files**: `Screens/BankrollProgrammer/BankrollProgrammer.cs`

- [x] **BP.3.1** In `OnTransferToBalancePressed()`, replace the reserve block:
  ```csharp
  // BEFORE (remove all of this):
  decimal reserve = Money.Normalize(_bankrollProgramService?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount);
  decimal maxTransferLeavingReserve = Money.Normalize(Math.Max(0m, currentBankroll - reserve));
  decimal effectiveAmount = Money.Normalize(Math.Min(amount, maxTransferLeavingReserve));
  if (effectiveAmount <= 0m)
  {
      _statusValue.Text = $"No hay saldo transferible. Reserva minima: {reserve:F8}.";
      return;
  }
  ```
  with the simpler:
  ```csharp
  // AFTER:
  decimal effectiveAmount = Money.Normalize(Math.Min(amount, currentBankroll));
  if (effectiveAmount <= 0m)
  {
      _statusValue.Text = "No transferable balance.";
      return;
  }
  ```
- [x] **BP.3.2** Update the success status message to English and `CultureInfo.InvariantCulture`:
  ```csharp
  _statusValue.Text = string.Create(CultureInfo.InvariantCulture,
      $"Transferred {effectiveAmount:N8} to Main Balance. Bankroll remaining: {_bankrollMirrorWallet.Balance:N8}.");
  ```
- [x] **BP.3.3** Optionally, if `effectiveAmount` results in bankroll = 0, append a hint to the status message: `" Bankroll is now empty — time stops until funds are added."` This is cosmetic and non-blocking.

**Verify**: with bankroll at 87.50 SC and dose at 100 SC, transfer 87.50 SC to Main Balance — should succeed and leave bankroll at 0.00.

---

### Phase BP.4 — BankrollProgrammer: Manual Recharge (Main Balance → Bankroll)

**Files**: `Screens/BankrollProgrammer/BankrollProgrammer.tscn`, `Screens/BankrollProgrammer/BankrollProgrammer.cs`

- [x] **BP.4.1** In `.tscn`, add a new `HBoxContainer` row between `AutoRechargeRow` and `ManualReturnRow`:
  ```
  [node name="ManualRechargeToBankrollRow" type="HBoxContainer" parent="VBox"]
  layout_mode = 2

  [node name="ManualRechargeToBankrollInput" type="LineEdit" parent="VBox/ManualRechargeToBankrollRow"]
  unique_name_in_owner = true
  layout_mode = 2
  placeholder_text = "Amount (Main Balance → Bankroll)"

  [node name="ManualRechargeToBankrollBtn" type="Button" parent="VBox/ManualRechargeToBankrollRow"]
  unique_name_in_owner = true
  layout_mode = 2
  text = "Manual Recharge"
  ```
- [x] **BP.4.2** In `.cs`, add private field `private LineEdit _manualRechargeToBankrollInput;`.
- [x] **BP.4.3** In `_Ready()`:
  ```csharp
  _manualRechargeToBankrollInput = GetNode<LineEdit>("%ManualRechargeToBankrollInput");
  GetNode<Button>("%ManualRechargeToBankrollBtn").Pressed += OnManualRechargeToBankrollPressed;
  ```
- [x] **BP.4.4** Implement the handler:
  ```csharp
  private void OnManualRechargeToBankrollPressed()
  {
      if (!TryParseAmount(_manualRechargeToBankrollInput.Text, out decimal amount))
      {
          _statusValue.Text = "Invalid amount.";
          return;
      }

      decimal available = _principalBalanceService?.CurrentBalance ?? 0m;
      if (amount > available)
      {
          _statusValue.Text = string.Create(CultureInfo.InvariantCulture,
              $"Insufficient Main Balance. Available: {available:N8}.");
          return;
      }

      // Reuse the mirror wallet pattern: sync a local mirror from the real bankroll,
      // use TryTransferBalanceToBankroll to mutate both sides, then write back.
      decimal currentBankroll = Money.Normalize(_bankrollStateService?.CurrentBalance ?? 0m);
      _bankrollMirrorWallet ??= new Wallet(currentBankroll);
      _bankrollMirrorWallet.SetBalanceForTimeTravel(currentBankroll);

      bool ok = _bankrollProgramService != null &&
          _principalBalanceService != null &&
          _bankrollMirrorWallet != null &&
          _bankrollProgramService.TryTransferBalanceToBankroll(
              _principalBalanceService, _bankrollMirrorWallet, amount, "manual_recharge");

      if (!ok)
      {
          _statusValue.Text = "Transfer failed.";
          return;
      }

      _bankrollStateService?.SetBalance(_bankrollMirrorWallet.Balance);
      _statusValue.Text = string.Create(CultureInfo.InvariantCulture,
          $"Recharged {amount:N8} to Bankroll. Bankroll now: {_bankrollMirrorWallet.Balance:N8}.");
      RenderAll();
  }
  ```
- [x] **BP.4.5** Clear the input field after a successful transfer:
  ```csharp
  _manualRechargeToBankrollInput.Text = "";
  ```
  Add this line immediately before the success `_statusValue.Text` assignment.
- [x] **BP.4.6** Fix the ledger routing for `"manual_recharge"` in `Scripts/Services/BankrollProgramService.cs`. The current `isInternalRecharge` check only covers `"auto_recharge"` and `"startup_default"`, so `"manual_recharge"` would incorrectly call `_ledger?.RegisterDeposit()`. Per GLOSSARY, a Bankroll Manual Recharge is **not** an SC Deposit. Fix by adding it to the internal check:
  ```csharp
  // In TryTransferBalanceToBankroll(), update the isInternalRecharge check:
  bool isInternalRecharge = string.Equals(reason, "auto_recharge", StringComparison.Ordinal)
                         || string.Equals(reason, "startup_default", StringComparison.Ordinal)
                         || string.Equals(reason, "manual_recharge", StringComparison.Ordinal); // ← add this line
  ```
  This routes to `_ledger?.RegisterAutoRecharge()`, recording it with `kind = "auto_recharge"` in the ledger — visible in ClientsTransactions as an internal recharge, never resetting the since-last-deposit baseline.

**Verify**: with Main Balance at 39,800 SC, type `500` in the Manual Recharge field, click "Manual Recharge" — Main Balance drops to 39,300 SC, Bankroll increases by 500 SC, field clears, transfer record in the list shows direction `BAL->BR` and reason `manual_recharge`. In ClientsTransactions the entry appears with `kind = auto_recharge`, not `kind = deposit`.

---

### Phase CG.1 — CasinoGamblingFinances: standalone target value label + input pre-population

**Files**: `Screens/CasinoGamblingFinances/CasinoGamblingFinances.tscn`, `Screens/CasinoGamblingFinances/CasinoGamblingFinances.cs`

**Context**: `CasinoScBalanceService` already fires `BalanceChanged` on every `SetBankrollTarget()`, `TryTransferToBankroll()`, and `TryTransferToMainBalance()` call; `RefreshLabels()` is already subscribed to it. The new label just needs to be written from inside `RefreshLabels()` — no new event subscriptions required.

- [ ] **CG.1.1** In `.tscn`, **replace** `TargetInfoLabel` with two nodes. Current:
  ```
  [node name="TargetInfoLabel" type="Label" parent="RootMargin/RootVBox"]
  unique_name_in_owner = true
  layout_mode = 2
  theme_override_font_sizes/font_size = 20
  text = "Bankroll target: 1,000,000.00000000 SC   (auto-fills to this level on exhaustion)"
  ```
  Replace with:
  ```
  [node name="BankrollTargetValueLabel" type="Label" parent="RootMargin/RootVBox"]
  unique_name_in_owner = true
  layout_mode = 2
  theme_override_font_sizes/font_size = 24
  text = "1,000,000.00000000"

  [node name="TargetHintLabel" type="Label" parent="RootMargin/RootVBox"]
  layout_mode = 2
  theme_override_font_sizes/font_size = 18
  text = "(auto-recharge target — fills to this level when bankroll hits 0)"
  ```
  Position `BankrollTargetValueLabel` immediately after `BankrollLabel` so the three live-value labels read top-to-bottom: Main Balance → Bankroll → Bankroll Target. `TargetHintLabel` stays after `LoanInfoLabel` near the separator, as context for the "Bankroll Target" control section.

- [ ] **CG.1.2** In `.cs`, add private field `private Label _bankrollTargetValueLabel;` alongside `_bankrollLabel`.

- [ ] **CG.1.3** In `_Ready()`, wire the new label and remove the old `_targetInfoLabel` reference:
  ```csharp
  // Remove:
  _targetInfoLabel = GetNode<Label>("%TargetInfoLabel");

  // Add:
  _bankrollTargetValueLabel = GetNode<Label>("%BankrollTargetValueLabel");
  ```
  `TargetHintLabel` is static text — no C# reference needed.

- [ ] **CG.1.4** In `_Ready()`, pre-populate the target input:
  ```csharp
  if (_casinoSc != null)
      _bankrollTargetInput.Text = _casinoSc.BankrollTarget.ToString("N8", CultureInfo.InvariantCulture);
  ```
  Place this after the `_casinoSc.BalanceChanged += RefreshLabels;` subscription.

- [ ] **CG.1.5** In `RefreshLabels()`, **replace** the `_targetInfoLabel.Text` line with a write to `_bankrollTargetValueLabel`:
  ```csharp
  // Remove:
  _targetInfoLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Bankroll target: {_casinoSc.BankrollTarget:N8} SC   (auto-fills to this level on exhaustion)");

  // Add:
  _bankrollTargetValueLabel.Text = _casinoSc.BankrollTarget.ToString("N8", CultureInfo.InvariantCulture);
  ```

- [ ] **CG.1.6** Remove the now-unused `_targetInfoLabel` field declaration from the class.

- [ ] **CG.1.7** In `OnSetTargetPressed()`, clear the input after success (already done). No change needed. Confirm the feedback label reads: `$"Bankroll target set to {value:N8} SC."` — already in English with `InvariantCulture`. ✅

**Verify**: open CasinoGamblingFinances — a standalone `1,000,000.00000000` label appears at the same visual weight as the Main Balance and Bankroll labels. Set a new target (e.g. `500000`) — the standalone label updates to `500,000.00000000` immediately. The target input is pre-populated with the current target on scene open.

---

### Phase CG.2 — CasinoGamblingFinances: loan history, manual loan button, game-date display

**Files**: `Scripts/Services/CasinoScBalanceService.cs`, `Screens/CasinoGamblingFinances/CasinoGamblingFinances.cs`, `Screens/CasinoGamblingFinances/CasinoGamblingFinances.tscn`

#### CasinoScBalanceService.cs changes

- [ ] **CG.2.1** Add `CalendarTimeService _calendarTime;` private field. In `_Ready()`, after `LoadState()`:
  ```csharp
  _calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
  ```

- [ ] **CG.2.2** Add the `LoanRecord` inner class (alongside `Snapshot`):
  ```csharp
  public sealed class LoanRecord
  {
      public decimal  Amount      { get; set; }
      public string   Reason      { get; set; } = string.Empty; // "auto" | "manual"
      public DateTime GameDateLocal { get; set; }
  }
  ```

- [ ] **CG.2.3** Add `private readonly List<LoanRecord> _loanHistory = new();` and expose it:
  ```csharp
  public IReadOnlyList<LoanRecord> LoanHistory => _loanHistory;
  ```

- [ ] **CG.2.4** Add a private helper `AddLoanRecord(decimal amount, string reason)`:
  ```csharp
  private void AddLoanRecord(decimal amount, string reason)
  {
      _loanHistory.Add(new LoanRecord
      {
          Amount       = amount,
          Reason       = reason,
          GameDateLocal = _calendarTime?.CurrentLocalDateTime ?? DateTime.Now
      });
  }
  ```

- [ ] **CG.2.5** In `TryAutoRecharge()`, call `AddLoanRecord` immediately after the loan injection (the `LoanCount++` block):
  ```csharp
  // existing code:
  MainBalance = Money.Normalize(MainBalance + InitialLoanAmount);
  LoanCount++;
  TotalLoaned = Money.Normalize(TotalLoaned + InitialLoanAmount);
  // add:
  AddLoanRecord(InitialLoanAmount, "auto");
  ```

- [ ] **CG.2.6** Add public `TriggerManualLoan(decimal amount)` method (adds funds to Main Balance only — does not auto-recharge Bankroll; see D16):
  ```csharp
  public bool TriggerManualLoan(decimal amount)
  {
      amount = Money.Normalize(amount);
      if (amount <= 0m) amount = InitialLoanAmount;

      MainBalance = Money.Normalize(MainBalance + amount);
      LoanCount++;
      TotalLoaned = Money.Normalize(TotalLoaned + amount);
      AddLoanRecord(amount, "manual");
      SaveState();
      BalanceChanged?.Invoke();
      return true;
  }
  ```

- [ ] **CG.2.7** Add `LoanHistory` to `Snapshot` and update `SaveState()`/`LoadState()`:

  In `Snapshot`:
  ```csharp
  public List<LoanRecord> LoanHistory { get; set; } = new();
  ```

  In `SaveState()`, add to the snapshot initializer:
  ```csharp
  LoanHistory = _loanHistory
      .Select(r => new LoanRecord
      {
          Amount        = r.Amount,
          Reason        = r.Reason,
          GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
      })
      .ToList()
  ```

  In `LoadState()`, after loading other fields:
  ```csharp
  _loanHistory.Clear();
  foreach (var r in snapshot.LoanHistory ?? new List<LoanRecord>())
  {
      if (r == null || r.Amount <= 0m) continue;
      _loanHistory.Add(new LoanRecord
      {
          Amount        = Money.Normalize(r.Amount),
          Reason        = r.Reason ?? "auto",
          GameDateLocal = r.GameDateLocal.Kind == DateTimeKind.Unspecified
              ? DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
              : r.GameDateLocal
      });
  }
  ```

  Also add to `InitializeDefaults()`: `_loanHistory.Clear();` (no history entry for the initial bootstrap loan — see D15).

#### CasinoGamblingFinances.tscn changes

- [ ] **CG.2.8** Add `GameDateLabel` immediately after `Sep0` (the first separator, below the title), before `MainBalanceLabel`:
  ```
  [node name="GameDateLabel" type="Label" parent="RootMargin/RootVBox"]
  unique_name_in_owner = true
  layout_mode = 2
  theme_override_font_sizes/font_size = 18
  text = "Game date: 2009-01-03 18:15:06"
  ```

- [ ] **CG.2.9** After `TransferFeedbackLabel` and before `Sep3` (the navigation separator), add a new loan section:
  ```
  [node name="Sep_Loans" type="HSeparator" parent="RootMargin/RootVBox"]
  layout_mode = 2

  [node name="LoanSectionLabel" type="Label" parent="RootMargin/RootVBox"]
  layout_mode = 2
  theme_override_font_sizes/font_size = 20
  text = "Bank Loans"

  [node name="ManualLoanRow" type="HBoxContainer" parent="RootMargin/RootVBox"]
  layout_mode = 2
  theme_override_constants/separation = 10

  [node name="ManualLoanAmountLabel" type="Label" parent="RootMargin/RootVBox/ManualLoanRow"]
  layout_mode = 2
  theme_override_font_sizes/font_size = 20
  text = "Loan amount (SC):"

  [node name="ManualLoanInput" type="LineEdit" parent="RootMargin/RootVBox/ManualLoanRow"]
  unique_name_in_owner = true
  custom_minimum_size = Vector2(280, 0)
  layout_mode = 2
  theme_override_font_sizes/font_size = 20
  placeholder_text = "100000000"

  [node name="ManualLoanBtn" type="Button" parent="RootMargin/RootVBox/ManualLoanRow"]
  unique_name_in_owner = true
  layout_mode = 2
  theme_override_font_sizes/font_size = 20
  text = "Request Loan → Main Balance"

  [node name="LoanFeedbackLabel" type="Label" parent="RootMargin/RootVBox"]
  unique_name_in_owner = true
  layout_mode = 2
  theme_override_font_sizes/font_size = 18
  text = ""

  [node name="LoanHistoryList" type="ItemList" parent="RootMargin/RootVBox"]
  unique_name_in_owner = true
  custom_minimum_size = Vector2(0, 180)
  layout_mode = 2
  ```

#### CasinoGamblingFinances.cs changes

- [ ] **CG.2.10** Add private fields:
  ```csharp
  private CalendarTimeService _calendarTime;
  private Label    _gameDateLabel;
  private LineEdit _manualLoanInput;
  private Label    _loanFeedbackLabel;
  private ItemList _loanHistoryList;
  ```

- [ ] **CG.2.11** In `_Ready()`, wire all new nodes (after existing wires):
  ```csharp
  _calendarTime      = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
  _gameDateLabel     = GetNode<Label>("%GameDateLabel");
  _manualLoanInput   = GetNode<LineEdit>("%ManualLoanInput");
  _loanFeedbackLabel = GetNode<Label>("%LoanFeedbackLabel");
  _loanHistoryList   = GetNode<ItemList>("%LoanHistoryList");

  GetNode<Button>("%ManualLoanBtn").Pressed += OnManualLoanPressed;

  // Pre-populate loan input with default
  _manualLoanInput.Text = CasinoScBalanceService.InitialLoanAmount
      .ToString("N0", CultureInfo.InvariantCulture);
  ```

- [ ] **CG.2.12** Add `_Process` update for the game-date label (replace or extend the existing `_fallbackTimer` block):
  ```csharp
  public override void _Process(double delta)
  {
      // Update game-date label every frame (cheap string format, low frequency is fine too)
      if (_gameDateLabel != null && _calendarTime != null)
      {
          _gameDateLabel.Text = string.Create(CultureInfo.InvariantCulture,
              $"Game date: {_calendarTime.CurrentLocalDateTime:yyyy-MM-dd HH:mm:ss}");
      }

      _fallbackTimer += delta;
      if (_fallbackTimer >= FallbackInterval)
      {
          _fallbackTimer = 0;
          RefreshLabels();
      }
  }
  ```

- [ ] **CG.2.13** Implement `OnManualLoanPressed()`:
  ```csharp
  private void OnManualLoanPressed()
  {
      _loanFeedbackLabel.Text = "";
      decimal amount = CasinoScBalanceService.InitialLoanAmount;
      string raw = (_manualLoanInput.Text ?? string.Empty).Trim().Replace(",", "");
      if (!string.IsNullOrEmpty(raw) &&
          decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) &&
          parsed > 0m)
      {
          amount = Money.Normalize(parsed);
      }

      _casinoSc?.TriggerManualLoan(amount);
      _manualLoanInput.Text = CasinoScBalanceService.InitialLoanAmount
          .ToString("N0", CultureInfo.InvariantCulture);
      _loanFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture,
          $"Loan of {amount:N8} SC added to Main Balance (game: {_calendarTime?.CurrentLocalDateTime:yyyy-MM-dd}).");
      RefreshLabels();
  }
  ```

- [ ] **CG.2.14** In `RefreshLabels()`, update `_loanInfoLabel` to include last loan game date, and populate `_loanHistoryList`:
  ```csharp
  // Update _loanInfoLabel — add last loan date if available
  string lastLoanDate = (_casinoSc.LoanHistory.Count > 0)
      ? _casinoSc.LoanHistory[^1].GameDateLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
      : "n/a";
  int unloggedCount = _casinoSc.LoanCount - _casinoSc.LoanHistory.Count;
  string unloggedNote = unloggedCount > 0 ? $" (+{unloggedCount} pre-log)" : "";
  _loanInfoLabel.Text = string.Create(CultureInfo.InvariantCulture,
      $"Bank loans taken: {_casinoSc.LoanCount}{unloggedNote}   |   Total loaned: {_casinoSc.TotalLoaned:N8} SC   |   Last: {lastLoanDate}");

  // Populate loan history list (newest first)
  _loanHistoryList.Clear();
  var history = _casinoSc.LoanHistory;
  for (int i = history.Count - 1; i >= 0; i--)
  {
      var r = history[i];
      _loanHistoryList.AddItem(string.Create(CultureInfo.InvariantCulture,
          $"{r.GameDateLocal:yyyy-MM-dd} | {r.Amount:N8} SC | {r.Reason}"));
  }
  ```

- [ ] **CG.2.15** Add null guard for `_loanHistoryList` in `RefreshLabels()` (check `GodotObject.IsInstanceValid(_loanHistoryList)` or null-check before `.Clear()`).

**Verify**:
- Scene opens showing `Game date: 2009-...` that ticks forward while autobet runs.
- Request a manual loan of 200M SC — `MainBalanceLabel` increases by 200M, loan appears in history list as `2009-XX-XX | 200,000,000.00000000 SC | manual`.
- Run a session until casino bankroll hits 0 — auto-loan fires, history list gets a new `auto` entry with the current game date.
- Cold-start (restart app) — loan history persists and is shown correctly.
- Pre-existing loans from before this version show `LoanCount` total with `(+N pre-log)` note in `_loanInfoLabel`.

---

## 4. Testing checklist (all phases)

### BankrollProgrammer (player)
- [ ] **DiceGame re-entry**: stop autobet mid-session (not on InsufficientBalance), navigate to BankrollProgrammer and back — bankroll label is correct on re-entry.
- [ ] **DiceGame re-entry v2**: let session stop on InsufficientBalance with auto-recharge off, navigate away and back — bankroll label shows correct (possibly 0) balance.
- [ ] **Dose label**: apply a new dose (e.g. 250 SC) — `AutoRechargeDoseValue` label shows `250.00000000` and persists on scene re-entry.
- [ ] **Dose label cold start**: restart the app and re-enter BankrollProgrammer — dose label shows the persisted dose, not the default.
- [ ] **Bankroll→Main Balance, full drain**: with bankroll at 87.50 SC and dose at 100 SC, transfer 87.50 SC — succeeds, bankroll = 0.00, Main Balance increases. (Was blocked before BP.3.)
- [ ] **Manual Recharge**: transfer 500 SC from Main Balance to Bankroll — both balances update, input field clears, record appears in transfer list with direction `BAL->BR` and reason `manual_recharge`.
- [ ] **Manual Recharge, ledger kind**: confirm the entry in ClientsTransactions shows `kind = auto_recharge`, not `kind = deposit` (it is an internal recharge, not an SC Deposit).
- [ ] **Manual Recharge, insufficient**: attempt to transfer more than Main Balance — blocked with English error message.
- [ ] **Dose validation**: set dose higher than current Main Balance — blocked with error showing the current Main Balance. Set a valid dose — succeeds and label updates.
- [ ] **Auto-recharge still works**: with bankroll at 0 after manual drain, place a bet with auto-recharge enabled — auto-recharge fires correctly (amount = current dose), bankroll refills to the correct dose amount.
- [ ] **StatusBar consistency**: StatusBar on all scenes shows consistent balances with BankrollProgrammer labels throughout.
- [ ] **InvariantCulture**: all SC amounts in status messages use comma as thousands separator, period as decimal (e.g. `1,000.00000000` not `1.000,00000000`).
- [ ] **English only**: no Spanish strings remain in BankrollProgrammer .tscn or .cs.

### CasinoGamblingFinances (casino)
- [ ] **Game date label**: scene shows a live `Game date: 2009-...` label that advances while autobet is running.
- [ ] **Target value label**: open scene — `BankrollTargetValueLabel` shows `1,000,000.00000000` at font size 24.
- [ ] **Target input pre-populated**: `BankrollTargetInput` shows the current target on scene open.
- [ ] **Set new target**: type `500000`, click Set — `BankrollTargetValueLabel` updates to `500,000.00000000` immediately.
- [ ] **Manual loan**: type `200000000`, click "Request Loan" — `MainBalanceLabel` increases by 200M; loan appears in history list as `2009-XX-XX | 200,000,000.00000000 SC | manual`; `LoanCount` increments; `TotalLoaned` increases; loan input resets to 100M default.
- [ ] **Manual loan, blank input**: click "Request Loan" with empty/invalid input — defaults to 100M SC, logs correctly.
- [ ] **Auto-loan trigger**: run autobet until a player win pushes `Bankroll ≤ 0` and the required refill amount (`BankrollTarget − Bankroll`) exceeds `MainBalance` — auto-loan fires, `TryAutoRecharge()` logs an `auto` entry in history with the current game date, bankroll refills to `BankrollTarget`.
- [ ] **Loan history persists**: restart app — loan history list is re-populated from the save file with correct amounts and game dates.
- [ ] **Pre-log note**: if `LoanCount > LoanHistory.Count` (old save), `_loanInfoLabel` shows `(+N pre-log)`.
- [ ] **Casino Main Balance → Bankroll and reverse**: regression check — both transfer directions still work.
- [ ] **Auto-recharge still fires**: casino bankroll depletion triggers a recharge to the configured target.
- [ ] **`TargetInfoLabel` field removed**: no null-reference crash in `RefreshLabels()`.
- [ ] **No wall-clock dates**: no `DateTime.Now` or `DateTime.UtcNow` is used for any displayed text in this scene.

---

## 5. Open questions

| # | Question | Decision |
|---|---|---|
| **OQ-BP.1** | Should the Manual Recharge amount field clear after a successful transfer? | ✅ **Yes — clear it.** See D11. Implemented in BP.4.5. |
| **OQ-BP.2** | Should "Manual Recharge" entries appear in `ClientsTransactions` as deposits? | ✅ **No — internal recharge, not a deposit.** Per GLOSSARY, Bankroll Manual Recharge is explicitly not an SC Deposit. Routes to `RegisterAutoRecharge()` via the `isInternalRecharge` guard in `BankrollProgramService`. See D12, implemented in BP.4.6. |
| **OQ-BP.3** | Should setting a dose larger than Main Balance be blocked? | ✅ **Yes — blocking at set time.** Error message shown, dose not saved. See D13, implemented in BP.2.9. |
| **OQ-CG.1** | Casino: add a transfer history list (like player's `TransfersList`)? | ⏸ **Deferred.** Requires new `List<TransferRecord>` on `CasinoScBalanceService`. Observable via `ClientsTransactions` for now. |
| **OQ-CG.2** | Casino: show auto-recharge event counters (day/week/month)? | ⏸ **Deferred.** Requires tracking recharge timestamps in `CasinoScBalanceService.ApplyBetResult()`. Post-scope. |
| **OQ-CG.3** | Casino: configurable loan amount for auto-loans and a separate default for manual loans? | ⏸ **Deferred — post Basic Mode v0.1.** A future "loan dosificador" would expose: (a) `AutoLoanAmount` (default 100M) — the amount injected per auto-loan when the casino is exhausted during live play; (b) `ManualLoanDefaultAmount` (default 100M) — the pre-filled value in the manual loan input. Both would be configurable from this scene and persisted. Today both are always `InitialLoanAmount = 100M SC` and the manual input is editable ad-hoc without persistence. |
| **OQ-BP.4** (bug, out of original scope) | Manual test after BP.4: a BankrollProgrammer transfer (Manual Recharge, or the pre-existing Bankroll→Main Balance) got silently reverted on returning to `DiceGame`. Root cause? | ✅ **Fixed.** `NetworkRoot.SharedNodesById` is a `static` dict that outlives `DiceGame`'s per-`_Ready()` `NetworkRoot` instance. `DiceGame.LoadActiveNodeFinancialState()` (called every `_Ready()`, `DiceGame.cs`) applied the player's *cached* `NodeFinancialState` snapshot back onto `PrincipalBalanceService`/`BankrollStateService`/`BankrollProgramService` — but that cache was frozen at the moment DiceGame was left (`SaveActiveNodeFinancialState(false)`, e.g. `OnOpenBankrollProgrammerPressed`), *before* the BankrollProgrammer transfer ran. Since those three services already persist themselves and are documented as the single source of truth for the player (see `SimulationService` header comment), the fix makes `LoadActiveNodeFinancialState()` skip applying the cached snapshot when `IsPlayerActive()` — `NodeFinancialState` now only round-trips for bot nodes (the Active Node Selector's actual use case), never for the player. Confirmed via code trace that all other `GetOrCreateNodeFinancialState` call sites (`SimulationService.BuildBotRunner` and two others) already exclude the player node. Pre-existing bug (predates this plan — the original Bankroll→Main Balance transfer had the same defect); surfaced during BP.4 manual testing on 2026-07-01. |
| **OQ-BP.5** (bug, out of original scope) | Follow-up manual test: mined a block via autobet (persisted correctly, confirming OQ-BP.4's fix), then placed a few *manual* (non-auto) bets afterward to move the bankroll further, closed the app, reopened it. Main Menu showed the correct (checkpoint-reverted) bankroll, but entering `DiceGame` immediately reverted it to the un-committed, pre-restart value — *before placing any new bet*. Root cause? | ✅ **Fixed.** Same family of bug as OQ-BP.4, different code path: `DiceGame.ApplyRealtimeBootstrapFromLoadedHistory()` (guarded by the `_bootstrapAppliedThisSession` static flag added in BP.1) still runs once per **process**, so on a genuine app restart the flag resets and it fires again on the very first `DiceGame._Ready()`. It called `_userStatsService.GetLatestKnownBalance(current)`, which scans `bet_history.jsonl` — a log that records *every* bet regardless of whether a block was later mined, so after a restart it can be ahead of the checkpoint (containing the manual bets from the uncommitted period the checkpoint revert correctly discarded) — and overwrote the already-correct, checkpoint-reverted `BankrollStateService`/`_wallet` with that stale value. Fix: `ApplyRealtimeBootstrapFromLoadedHistory()` no longer touches the balance at all (only refreshes `_financialStats` from history) — `BlockSessionCheckpointService.ApplyCheckpointToServices()` (autoload boot, before any scene loads) is the sole source of truth for the balance on a cold start, same principle as OQ-BP.4. Surfaced during OQ-BP.4 regression testing on 2026-07-01. |
| **OQ-BP.6** (bug, out of original scope) | Regression testing after OQ-BP.5's fix surfaced two more "block is the only commit" leaks, both present before mining any block: (a) DiceGame's `FinancialBettingStats` panel ("last deposit P/L", "General P/L") showed a stale non-zero value after a clean restart with no block mined; (b) `BankrollProgrammer`'s transfer list kept showing the `manual_recharge` entry (and dose amount) after the same kind of restart. Root causes? | ✅ **Fixed (both).** (a) `DiceGame._Ready()` called `ApplyRealtimeBootstrapFromLoadedHistory()` (line ~262, which reads `_userStatsService.GetLoadedHistoryStats()`) *before* `RestoreLegacyCheckpointIfNeeded()` (line ~294, which rolls the bet-history log back to the checkpoint's UTC boundary via `RollbackHistoryToUtc`) — so the stats panel was built from history that hadn't been rolled back yet. Fix: moved the `ApplyRealtimeBootstrapFromLoadedHistory()` call to run immediately after `RestoreLegacyCheckpointIfNeeded()`. (b) `BlockSessionCheckpointService.ApplyCheckpointToServices()` (autoload boot, the reliable revert path used for `BankrollStateService`/`PrincipalBalanceService`/`CasinoScBalanceService`) never touched `BankrollProgramService` at all — its `AutoRechargeAmount`/`Records` only got restored via the DiceGame-scoped `RestoreLegacyCheckpointIfNeeded()`'s legacy branch, which is skipped whenever `NetworkRoot.HasAnyNodeFinancialState()` is true (i.e. on essentially every real restart after the first). Fix: `ApplyCheckpointToServices()` now also calls `BankrollProgramService.ReplaceState(CurrentSnapshot.AutoRechargeAmount, CurrentSnapshot.TransferRecords)`, matching the other three services' pattern — reliable regardless of `NetworkRoot`'s state. Surfaced 2026-07-01. |
| **OQ-BP.7** (bug, out of original scope) | Even on a completely fresh install (before any block has ever been mined), `BankrollProgrammer`'s transfer list shows the very first `startup_default` 100 SC entry (from `DiceGame.EnsureInitialBankrollFunded()`), and Main Balance/Bankroll show the post-startup-recharge split (39,900/100) rather than the true pre-genesis 40,000/0 — on restart. The user wants a "block is the only commit" world to feel like a genuine first-ever launch (Main Balance = 40,000.00, Bankroll = 0.00, no records, dose = default 100) until a **real** block is mined — including the configured auto-recharge dose itself: a custom dose only "sticks" across restarts once a block has actually been mined with it in effect; before that, every restart (regardless of what was typed into `BankrollProgrammer`) goes back to `DefaultAutoRechargeAmount`. | ✅ **Fixed.** Root cause: `DiceGame.CaptureBlockCheckpointIfMissing()` captured a **baseline** checkpoint on the very first `_Ready()` (guarded only by "no checkpoint exists yet", not "a real block was mined"), so `startup_default` got folded into a permanent baseline immediately. Fix (three parts): (1) removed `CaptureBlockCheckpointIfMissing()` entirely — `BlockSessionCheckpointService.CaptureCheckpoint()` is now reachable *only* from real block-mined events (`DiceGame.CaptureBlockCheckpoint()` at the manual-bet mining callback, and `SimulationService.CaptureCheckpoint()` during autobet/bots/founders), never from just opening the app; (2) `BlockSessionCheckpointService._Ready()` now calls a new `ResetToPreGenesisDefaults()` whenever `HasCheckpoint()` is false (no block ever mined in this world) — force-resets `PrincipalBalanceService` to 40,000, `BankrollStateService` to 0, `BankrollProgramService.AutoRechargeAmount` to `DefaultAutoRechargeAmount`, and clears `BankrollProgramService.Records`, on every boot, discarding whatever those services' own eagerly-self-persisted files accumulated; (3) `DiceGame.EnsureInitialBankrollFunded()` now uses `_bankrollProgramService.AutoRechargeAmount` (falling back to `DefaultAutoRechargeAmount`) instead of the hardcoded default constant, so once a dose *has* been committed by a real mined block, `ApplyCheckpointToServices()` restores it from the checkpoint and the startup recharge on the next fresh game correctly uses that committed dose — but pre-genesis, every restart resets the dose to default just like the balances. Implemented 2026-07-01 per explicit user request (higher priority than initially scoped; refined same day to also reset the dose, not just balances/records). |
| **OQ-BP.8** (bug, out of original scope) | Regression testing after OQ-BP.7 found three more leaks in the same "pre-genesis" (no block ever mined) window: (a) player start time on 21 Mar 2009 lands hours after the historical bootstrap's last mined block ("dead blockchain time"), not right after it; (b) placing bets, closing the app without mining a block, and reopening left the game clock advanced even though balances correctly reset; (c) `DiceGame`'s "General P/L" stat still reflected bets from the discarded, un-committed previous session. Root causes? | ✅ **Fixed (all three).** (a) `HistoricalBootstrapService.Run()` used to pick an independently-random `landingLocal` within 21 Mar and mine blocks only until the timestamp *would* reach it (the crossing block itself was never mined) — leaving up to a full jittered block interval (~11–21h) of dead time between the last historical block and the player's start. Rewrote it to mine the block that *crosses* into 21 Mar 00:00 too, track that block's timestamp as `lastMinedTs`, and set `LandingLocalDateTime = lastMinedTs + 1 second` — exact, no gap, still always on/after 21 Mar. **Superseded by OQ-BP.9 same day: the `+1 second` offset was removed** — see below. (b, c) `CalendarTimeService` and `UserStatsService`'s bet history self-persist on every bet regardless of block mining, same class of leak as OQ-BP.4–7, but `BlockSessionCheckpointService.ResetToPreGenesisDefaults()` (added in OQ-BP.7) didn't yet touch either. Added `NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()` (mirrors the existing `GetPlayerChainLengthStatic()` static-surface pattern) so `ResetToPreGenesisDefaults()` can re-derive "player start" (chain tip — before any real block, the tip *is* the historical bootstrap's last block, so this is exact and needs no separate persistence) and, on every boot with no checkpoint yet, reset `CalendarTimeService` to that instant and call `UserStatsService.RollbackHistoryToUtc()` to discard any bets from the discarded session — mirroring exactly what `ApplyCheckpointToServices()` already does for the post-first-block case. Implemented 2026-07-01. |
| **OQ-BP.9** (refinement, canonical decision) | OQ-BP.8 landed the player start 1 second *after* the last bootstrap block's timestamp. Should it instead be exactly *equal* to that timestamp? | ✅ **Yes — exact match, no offset.** Verified the invariant the user pointed out: every post-bootstrap checkpoint capture (`DiceGame.CaptureBlockCheckpoint()` → `BlockSessionCheckpointService.CaptureCheckpoint(..., calendarLocalDateTime)`, and `SimulationService.CaptureCheckpoint()`) reads `CalendarTimeService.CurrentLocalDateTime` **synchronously, immediately after** the block is mined with that same instant as its `Timestamp` — no clock advance happens in between (Godot's clock only advances via `_Process(delta)`, and these are plain synchronous method calls within one call chain) — so a checkpoint's saved calendar time is *always* bit-for-bit equal to its triggering block's timestamp. **Canonical rule**: the in-game calendar clock and the timestamp of the block that most recently defines the checkpointed world state are always the exact same instant — never offset. The player's very first instant (right after the historical bootstrap, before any real block) must follow the same rule for consistency, so `HistoricalBootstrapService.Run()`'s `LandingLocalDateTime` and `BlockSessionCheckpointService.ResetToPreGenesisDefaults()`'s recomputed `playerStart` both dropped the `AddSeconds(1)` and now use `DateTimeOffset.FromUnixTimeMilliseconds(tipMs)` directly (no offset). This rule is now recorded in `CLAUDE.md` (Canonical Decisions + `BlockSessionCheckpointService`/Important Pattern 2) as the single source of truth for future work — see there for the general statement, this row is the historical "why". Implemented 2026-07-01. |
