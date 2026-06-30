# P10 — Network Fee Activation — Implementation Plan

**Status**: ✅ **PHASES 10.1–10.6 DONE** — branch `network-fee-activation`. Phase 10.7 (docs) completed 2026-06-30. See §5 for the phase checklist.

**Scope**: Implements the "whole-network fee-free before ~2009-04-26, all participants pay after" rule
established in `step8-utxo-realism-plan.md` OQ-8.7 and fully specified in `Documentation/PRIVATE_ROADMAP.md` §P10.
Covers:
- **(a)** A shared `NetworkFeePolicy` utility class (the single source of truth for all fee constants and the activation gate).
- **(b)** Fee input UI in all four BTC wallet send panels — hidden before activation, default 0.1 / clamp 0.1–1.0 after.
- **(c)** Sender-balance label on the send panel so the player never has to navigate back to the base wallet to verify funds.
- **(d)** Rename the "Cancel" button to "Go Back" across all send panels (semantically correct — it returns to the base view; actual transaction cancellation is a deferred feature).
- **(e)** Backend gate: bot automated fees and casino pool-payout fees are forced to `0m` before the activation date.

**Created**: 2026-06-29.

**Dependencies**: Requires **Step 8** (UTXO realism — ✅ done). No other open dependencies. Does **not** block any other Basic Mode work (own branch).

**Companion docs**: `step8-utxo-realism-plan.md` OQ-8.7 · `IMPLEMENTATION_ROADMAP.md` §6 · `PRIVATE_ROADMAP.md` §P10.

---

## 0. Decisions locked

| # | Question | Decision |
|---|---|---|
| **D1** | Pre-activation fee field: visible-but-disabled, or hidden entirely? | **Hidden entirely** (`_feeRow.Visible = false`). No fee concept before 2009-04-26; a disabled field implies a "coming soon" hint we don't need. |
| **D2** | Auto-clamp timing? | **Both**: on `FocusExited` (immediate feedback) AND re-clamped inside `OnSendConfirmed()` before the value is used (handles Enter-key submit without a prior focus-exit). |
| **D3** | Backend bot/casino automated fees — gate them too? | **Yes, same branch.** CLAUDE.md canonical: "whole network fee-free before". Bots currently attach 0.1–1.0 BTC from block 1 — a dev contradiction fixed here. After activation, existing random 0.1–1.0 BTC range is unchanged. |
| **D4** | HistoricalEventScheduler fees (Hearn round-trip, 2009-04-18)? | **Leave as-is.** All scripted events use `fee = 0m` by default. 2009-04-18 is 8 days before the activation date — already historically correct. No code changes. |
| **D5** | Fee UI depth for FoundersWallets / CasinoFinances / BotsBtcWallets? | **Simplified but behaviour-identical**: a fee row with `_feeInput` only (no extra hint label), same hidden-before / default-0.1-after / clamp-on-exit logic as BTCWallet. DEV screens don't need the explanatory label. |
| **D6** | "Cancel" → "Go Back" label change? | **All four wallets.** The button returns to the base wallet view, not cancels the transaction. Rename globally. |
| **D7** | Sender balance on the send panel? | **Add `_sendBalanceLabel`** to every send panel. Shows the confirmed spendable balance of the active sending address. Updated in `EnterSendMode()`. Read-only display. |
| **D8** | Activation gate: game clock or block timestamp? | **Game clock (`CalendarTimeService.CurrentLocalDateTime`)** for the UI show/hide decision (called when `EnterSendMode()` is invoked). **Block timestamp (`block.Timestamp`)** for the backend bot/casino gates in `NetworkRoot` (already available there). Both compare to `NetworkFeePolicy.ActivationDateLocal = 2009-04-26`. |

---

## 1. Current state (verified in code)

### Fee infrastructure — complete, no work needed

The pipeline from UI to coinbase already fully supports non-zero fees:

| Component | File | Status |
|---|---|---|
| `BlockTransaction.Fee` field | `Scripts/BlockchainPort/Blockchain/Models.cs:48` | `decimal Fee = 0m` — exists |
| `CreateUnsignedTransaction(inputs, outputs, fee, salt)` | `BlockchainService.cs` | validates `Fee == Σin − Σout` |
| `BuildAndBroadcastUtxoSpend(sender, recipient, amount, fee, salt)` | `NetworkRoot.cs:325` | `need = amount + fee` drives coin selection |
| `BlockTemplateBuilder.Build()` | `BlockTemplateBuilder.cs:20` | orders by descending Fee; coinbase = `reward + ΣFee` |
| `CreateAndBroadcastTransactionToAddress(fromNodeId, toAddr, amount, decimal fee = 0m)` | `NetworkRoot.cs` | public API; fee param exists, defaults to 0 |

Nothing in this pipeline changes. P10 only gates *whether a fee is attached*, not how fees flow.

### Fee UI — inconsistent today

| Screen | Has `_feeInput`? | Passes fee to API? | Problem |
|---|---|---|---|
| `Screens/BTCWallet/BTCWallet.cs` | ✅ already built | ✅ yes | No min/max; fee optional (blank = 0); no activation gate |
| `Screens/FoundersWallets/FoundersWallets.cs` | ❌ missing | ❌ always 0 | `CreateAndBroadcastTransactionToAddress(id, addr, amount)` — no fee arg |
| `Screens/CasinoFinances/CasinoFinances.cs` | ❌ missing | ❌ always 0 | Same pattern |
| `Screens/BotsBtcWallets/BotsBtcWallets.cs` | ❌ missing | ❌ always 0 | Miners-only send section; same pattern |

### Backend automated fees — dev contradiction today

- **Bots** (`NetworkRoot.cs:810`): `decimal fee = Math.Round(MinBotFeeBtc + … * (MaxBotFeeBtc - MinBotFeeBtc), 8)` — always applied from block 1, no date gate. (`MinBotFeeBtc = 0.1m`, `MaxBotFeeBtc = 1.0m`)
- **Casino pool payouts** (`NetworkRoot.cs:47`): `private const decimal CasinoTxFee = 0.1m` — passed by `SendFromCasino()` on every pool payout, no date gate.
- **Contradiction**: the Hearn scripted events (2009-04-18) are fee-free (correct), but bots/casino have been paying fees since the bootstrap (wrong). P10 resolves this.

### No activation date logic exists anywhere

No reference to `FeeActivation`, `OQ-8.7`, or any game-date fee gate exists in the codebase. Everything is built from scratch.

---

## 2. Target architecture — `NetworkFeePolicy`

A new static class is the single source of truth for fee constants and the activation check. Every wallet screen and every backend fee decision reads from here.

```csharp
// Scripts/BlockchainPort/Blockchain/NetworkFeePolicy.cs  (NEW)
using System;

namespace GodotBlockchainPort.Blockchain
{
    // P10: whole-network fee-free before 2009-04-26; all participants pay after.
    // Chosen strictly after the Hearn round-trip (2009-04-18) so scripted historical txs
    // remain fee-free as historically accurate.
    public static class NetworkFeePolicy
    {
        public static readonly DateTime ActivationDateLocal = new DateTime(2009, 4, 26);

        // Basic Mode v1 limits
        public const decimal DefaultFee = 0.1m;
        public const decimal MinFee     = 0.1m;
        public const decimal MaxFee     = 1.0m;

        // Game-clock check (UI layer)
        public static bool IsActive(DateTime gameLocalDateTime)
            => gameLocalDateTime.Date >= ActivationDateLocal;

        // Block-timestamp check (backend layer)
        // Stored as UTC Unix ms; ActivationDateLocal interpreted as midnight UTC for the gate.
        public static readonly long ActivationDateMs =
            new DateTimeOffset(ActivationDateLocal, TimeSpan.Zero).ToUnixTimeMilliseconds();

        public static bool IsActiveByTimestamp(long blockTimestampMs)
            => blockTimestampMs >= ActivationDateMs;

        // Any value outside [MinFee, MaxFee] → DefaultFee. Never throws.
        public static decimal ClampOrDefault(decimal fee)
            => (fee >= MinFee && fee <= MaxFee) ? fee : DefaultFee;
    }
}
```

---

## 3. Send panel UX (all wallets after this step)

### Uniform layout after P10

```
Send BTC
────────────────────────────────────────────────────────────
From: gm1q...XXXXXXXX...       Balance: X.XXXXXXXX BTC   ← _sendBalanceLabel (NEW)
To: [── dropdown ──────────────────────────▾]
    [gm1q... address input  — only when "BTC Address" is selected]
Amount (BTC):  [_______________]
Fee (BTC):     [_______________]  ← _feeRow hidden before 2009-04-26
                                     default 0.1 after; clamp 0.1–1.0 on exit
[Send]  [Go Back]  [feedback label …]
────────────────────────────────────────────────────────────
```

### Fee row lifecycle

| Game date | `_feeRow.Visible` | `_feeInput.Text` on open | Editable |
|---|---|---|---|
| < 2009-04-26 | `false` — hidden | — | — |
| ≥ 2009-04-26 | `true` | `"0.10000000"` | yes |

### Auto-clamp rule (post-activation only)

1. **On `FocusExited`**: if the field is blank or the parsed value is outside `[0.1, 1.0]` → reset to `"0.10000000"`.
2. **On Send button pressed** (`OnSendConfirmed`): re-clamp before consuming the value, then update the field text so the user sees the effective value used. This handles Enter-key submission that skips the `FocusExited` event.

### "Go Back" button

Renames "Cancel" → "Go Back" in every send panel across all four wallets. The button was already wired to `OnSendCancelled()`/`OnBackToBaseWalletPressed()` — only the `Text` property changes.

---

## 4. Phases

### Phase 10.1 — `NetworkFeePolicy` utility class

**New file**: `Scripts/BlockchainPort/Blockchain/NetworkFeePolicy.cs`

Content: exactly as §2 above. No other files change in this phase. The class is `static` with no Godot dependencies — compiles and is testable in pure C#.

---

### Phase 10.2 — BTCWallet (Player)

**File**: `Screens/BTCWallet/BTCWallet.cs`

The `_feeInput` field already exists (built in `BuildSendPanel()`). Changes are additive.

#### New fields

```csharp
private CalendarTimeService? _calendarTimeService;
private HBoxContainer _feeRow = null!;      // wraps the label + input; toggled for hide/show
private Label _sendBalanceLabel = null!;
```

#### `_Ready()` — add service lookup

```csharp
_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
```

#### `BuildSendPanel()` — three additions

1. **After `_sendFromLabel`**, insert `_sendBalanceLabel`:
   ```csharp
   _sendBalanceLabel = new Label();
   _sendBalanceLabel.AddThemeFontSizeOverride("font_size", 20);
   _sendPanel.AddChild(_sendBalanceLabel);
   ```

2. **Existing fee row**: capture it as `_feeRow` so we can toggle `Visible`:
   ```csharp
   _feeRow = feeRow;   // the local HBoxContainer already built — just name it
   _feeInput.FocusExited += OnFeeInputFocusExited;
   ```

3. **"Cancel" button**: change `Text` from `"Cancel"` to `"Go Back"`.

#### New helpers

```csharp
private void ApplyFeeState()
{
    DateTime gameTime = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.MinValue;
    bool active = GodotBlockchainPort.Blockchain.NetworkFeePolicy.IsActive(gameTime);
    _feeRow.Visible = active;
    if (active) _feeInput.Text = GodotBlockchainPort.Blockchain.NetworkFeePolicy.DefaultFee.ToString("F8");
}

private void OnFeeInputFocusExited()
{
    DateTime gameTime = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.MinValue;
    if (!GodotBlockchainPort.Blockchain.NetworkFeePolicy.IsActive(gameTime)) return;
    _feeInput.Text = GodotBlockchainPort.Blockchain.NetworkFeePolicy
        .ClampOrDefault(TryParseFee(_feeInput.Text)).ToString("F8");
}

private static decimal TryParseFee(string text)
    => decimal.TryParse(text.Trim(),
           System.Globalization.NumberStyles.Number,
           System.Globalization.CultureInfo.InvariantCulture,
           out decimal v) ? v : -1m;
```

#### `EnterSendMode(string senderNodeId, string senderAddress, WalletMode returnTo)` — replace fee setup

Remove `_feeInput.Text = string.Empty`. Add:
```csharp
// Show sender balance
decimal balance = GetSenderDisplayBalance(senderNodeId);
_sendBalanceLabel.Text = $"Balance: {balance:F8} BTC";
// Apply fee activation state (hides row before 2009-04-26; fills default after)
ApplyFeeState();
```

`GetSenderDisplayBalance(string nodeId)` is a small private helper that reads the confirmed
spendable balance from `_networkRoot` for the given node (reuses `GetNodeSpendableBalance` or
equivalent — already available in `NetworkRoot`).

#### `OnSendConfirmed()` — replace fee parsing

Replace the existing "blank = 0" fee logic block:
```csharp
DateTime gameTime = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.MinValue;
bool feesActive = GodotBlockchainPort.Blockchain.NetworkFeePolicy.IsActive(gameTime);
decimal fee = 0m;
if (feesActive)
{
    decimal parsed = TryParseFee(_feeInput.Text);
    fee = GodotBlockchainPort.Blockchain.NetworkFeePolicy.ClampOrDefault(parsed);
    _feeInput.Text = fee.ToString("F8"); // reflect the effective clamped value
}
```

---

### Phase 10.3 — FoundersWallets

**File**: `Screens/FoundersWallets/FoundersWallets.cs`

`CalendarTimeService` is already initialized in `_Ready()`. Changes are parallel to Phase 10.2.

#### New fields

```csharp
private HBoxContainer _feeRow = null!;
private LineEdit _feeInput = null!;
private Label _sendBalanceLabel = null!;
```

#### `BuildSendPanel()` — three additions

1. **After `_sendFromLabel`**, insert `_sendBalanceLabel` (font size 18 to match this screen).

2. **After the `_amountInput` row**, add the fee row:
   ```csharp
   _feeRow = new HBoxContainer();
   _feeRow.AddThemeConstantOverride("separation", 10);
   var feeLabel = new Label { Text = "Fee (BTC):" };
   feeLabel.AddThemeFontSizeOverride("font_size", 20);
   _feeInput = new LineEdit
   {
       PlaceholderText = "0.10000000",
       CustomMinimumSize = new Vector2(200, 0)
   };
   _feeInput.AddThemeFontSizeOverride("font_size", 20);
   _feeInput.FocusExited += OnFeeInputFocusExited;
   _feeRow.AddChild(feeLabel);
   _feeRow.AddChild(_feeInput);
   _sendPanel.AddChild(_feeRow);
   ```

3. **"Cancel" button**: rename to `"Go Back"`.

#### New helpers

`ApplyFeeState()`, `OnFeeInputFocusExited()`, `TryParseFee()` — identical logic to Phase 10.2.

#### `EnterSendMode()` — add balance + fee state

```csharp
decimal balance = _networkRoot.GetNodeSpendableBalance(senderNodeId);
_sendBalanceLabel.Text = $"Balance: {balance:F8} BTC";
ApplyFeeState();
```

#### `OnSendConfirmed()` — add fee resolution + pass to API

Resolve fee exactly as in Phase 10.2, then replace:
```csharp
// before:
Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_sendFromNodeId, recipientAddress, amount);
// after:
Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_sendFromNodeId, recipientAddress, amount, fee);
```

---

### Phase 10.4 — CasinoFinances

**File**: `Screens/CasinoFinances/CasinoFinances.cs`

`CalendarTimeService` is **not** currently referenced in this screen — add it.

#### New fields + `_Ready()` addition

```csharp
private CalendarTimeService? _calendarTimeService;
private HBoxContainer _feeRow = null!;
private LineEdit _feeInput = null!;
private Label _sendBalanceLabel = null!;

// in _Ready():
_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
```

All other changes (send panel, helpers, `EnterSendMode`, `OnSendConfirmed`) are identical to Phase 10.3.

For the balance display: the casino's spendable balance spans base address + all derived change addresses. Use `_networkRoot.GetNodeSpendableBalance("casino")` (or equivalent; mirrors how `FoundersWallets` reads founder balances).

---

### Phase 10.5 — BotsBtcWallets

**File**: `Screens/BotsBtcWallets/BotsBtcWallets.cs`

The send section exists for miner bots only (built alongside the detail panel; non-miners see a read-only wallet status). `CalendarTimeService` is not currently referenced — add it.

#### New fields + `_Ready()` addition

```csharp
private CalendarTimeService? _calendarTimeService;
private HBoxContainer _feeRow = null!;
private LineEdit _feeInput = null!;
private Label _sendBalanceLabel = null!;

// in _Ready():
_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
```

#### Send section builder — additions

In the send-section-building method (where `_amountInput` and `_toDropdown` are built):
1. Add `_sendBalanceLabel` before the amount row.
2. Add `_feeRow` after the amount row (same construction as Phase 10.3).
3. Rename the button from "Cancel" to "Go Back".
4. Connect `_feeInput.FocusExited += OnFeeInputFocusExited`.

#### `RefreshDetailPanel(BotWalletRecord bot)` — add balance + fee state refresh

When a bot is selected, the send section is populated. Also update the sender balance and re-apply fee state:
```csharp
// after existing balance/stats refresh:
decimal btcBalance = _networkRoot.GetNodeSpendableBalance(bot.NodeId);
_sendBalanceLabel.Text = $"Balance: {btcBalance:F8} BTC";
ApplyFeeState();
```

This means opening a bot's wallet when the game date has already passed 2009-04-26 correctly shows the fee row.

#### `OnSendConfirmed()` — add fee + pass to API

Resolve fee as in Phase 10.2, then replace:
```csharp
// before:
Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_selectedBot.NodeId, recipientAddress, amount);
// after:
Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_selectedBot.NodeId, recipientAddress, amount, fee);
```

---

### Phase 10.6 — Backend: gate bot automated fees + casino pool-payout fee

**File**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`

#### 6a — Bot automated transaction fees (`ScheduleBotTransactionsAfterBlock`)

Current code (line ~810):
```csharp
decimal fee = Math.Round(MinBotFeeBtc + (decimal)Random.Shared.NextDouble() * (MaxBotFeeBtc - MinBotFeeBtc), 8);
```

Replace with:
```csharp
decimal fee = GodotBlockchainPort.Blockchain.NetworkFeePolicy.IsActiveByTimestamp(block.Timestamp)
    ? Math.Round(MinBotFeeBtc + (decimal)Random.Shared.NextDouble() * (MaxBotFeeBtc - MinBotFeeBtc), 8)
    : 0m;
```

`block` is already a parameter of `ScheduleBotTransactionsAfterBlock` — no signature change needed.

#### 6b — Casino pool payout fee (`SendFromCasino`)

Current signature:
```csharp
private static Transaction? SendFromCasino(NodeAgent casino, string recipientAddress, decimal amount)
{
    ...
    return BuildAndBroadcastUtxoSpend(casino, recipientAddress, amount, CasinoTxFee, null);
}
```

The call site in `TryDistributePendingCasinoRewards` has access to the `Block` that triggered the distribution. Thread `block` down:

```csharp
private static Transaction? SendFromCasino(NodeAgent casino, string recipientAddress, decimal amount, Block block)
{
    if (casino.WalletAddress == recipientAddress) return null;
    decimal fee = GodotBlockchainPort.Blockchain.NetworkFeePolicy.IsActiveByTimestamp(block.Timestamp)
        ? CasinoTxFee
        : 0m;
    return BuildAndBroadcastUtxoSpend(casino, recipientAddress, amount, fee, null);
}
```

Update the call in `TryDistributePendingCasinoRewards` to pass `block`. The `Block` is already available in `HandleMinedBlock(Block block)` which calls `TryDistributePendingCasinoRewards` — trace the call chain and thread `block` through.

#### 6c — Verify no other automated send paths are missed

Before merging Phase 10.6, grep all call sites of `BuildAndBroadcastUtxoSpend` in `NetworkRoot.cs`:
- `CreateAndBroadcastTransaction` (line ~231) — player/founder manual sends, fee defaulted to 0m → ✅ now player-supplied
- `InjectHistoricalSignedTxStatic` (line ~310) — scripted historical events, `fee = 0m` → ✅ pre-activation, correct
- `SendFromCasino` — gated in 6b above
- `ScheduleBotTransactionsAfterBlock` bot loop — gated in 6a above

Any unlisted call site must be reviewed and gated if it predates 2009-04-26.

---

### Phase 10.7 — Documentation

**Files**: `CLAUDE.md`, `AIHelperFiles/IMPLEMENTATION_ROADMAP.md`, `Documentation/PRIVATE_ROADMAP.md`

1. **`CLAUDE.md`**:
   - Canonical decisions table: update `Network fee activation` row → `~2009-04-26 nearest block ✅ Implemented`
   - Implementation Status → "Implemented" section: add `Network fee activation (P10): fee field in all BTC wallet send panels (hidden before, default 0.1 after, clamp 0.1–1.0); backend bot/casino fees gated on the same date`
   - Remove the OQ-8.7 deferred note from the "Prototype" or notes sections

2. **`AIHelperFiles/IMPLEMENTATION_ROADMAP.md`**:
   - §3: Add a `Step 10` entry below Step 9, status `✅ DONE`
   - §6 "What's next": remove the `network-fee-activation` deferred note (it is now done)

3. **`Documentation/PRIVATE_ROADMAP.md`**:
   - P10 entry: change status/goal line to `✅ Done` — fee-free before 2009-04-26, fees enforced after

---

## 5. File impact summary & phase checklist

| File | Change type | Phase | Done? |
|---|---|---|---|
| `Scripts/BlockchainPort/Blockchain/NetworkFeePolicy.cs` | NEW | 10.1 | ✅ |
| `Screens/BTCWallet/BTCWallet.cs` | MOD | 10.2 | ✅ |
| `Screens/FoundersWallets/FoundersWallets.cs` | MOD | 10.3 | ✅ |
| `Screens/CasinoFinances/CasinoFinances.cs` | MOD | 10.4 | ✅ |
| `Screens/BotsBtcWallets/BotsBtcWallets.cs` | MOD | 10.5 | ✅ |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | MOD | 10.6 | ✅ |
| `CLAUDE.md` | MOD | 10.7 | ✅ |
| `AIHelperFiles/IMPLEMENTATION_ROADMAP.md` | MOD | 10.7 | ✅ |
| `Documentation/PRIVATE_ROADMAP.md` | MOD | 10.7 | ✅ |

### Phase 10.6 — additional work done beyond the original scope

The following were discovered and fixed during Phase 10.6 validation:

**Casino pool distribution atomicity fix** (`TryDistributePendingCasinoRewards` / `DistributePoolEventAsSingleTx`): the original `SendFromCasino` loop fired one tx per recipient. Send 1 spent the only large confirmed UTXO (fresh coinbase); sends 2–5 found nothing spendable (change from send 1 is still *pending*, excluded by `GetSpendableUtxos`). Result: `allSent=false`, `MarkDistributed` never called, event retried on every subsequent block → partial double-payments. Fix: one atomic multi-output tx per pool event — one coin selection covering `Σrecipients + totalFee`, all N recipients as outputs, one change output, one `AddTransactionToPendingTransactions`. `MarkDistributed` is called only on tx success; failure leaves no partial state, so retry is safe.

**Block Explorer multi-output display** (`BlockExplorer.cs`): block lookup and right-column preview were using the `[JsonIgnore]` shims `tx.Sender`/`tx.Recipient`/`tx.Amount` (expose only `Inputs[0]`/`Outputs[0]`). Replaced with full `tx.Inputs[]` / `tx.Outputs[]` iteration in all three display locations (block lookup loop, `BuildTransactionDetails`/`FormatTxDetail`, `BuildLatestTransactionPreview`). `BuildLatestTransactionPreview` now shows ALL transactions in the block (was only `block.Transactions[0]`). Fee LINQ fixed: `.Where(t => !t.IsCoinbase).Sum(t => t.Fee)`.

**OQ-8.2 cosmetic filter** (`BlockExplorer.cs`): bots are single-address (no `ReceiveWallet` / no persistent seed), so every bot spend produces a change output back to the bot's own input address. Two helpers gate this cosmetically in the Block Explorer until OQ-8.2 (simplified seeds + `DerivedAddressWallet` for bots) is resolved — remove both helpers and all callers at that point (before referral/rank systems ship):
- `IsSelfChangeTransaction(tx)` — hides the entire tx when ALL outputs go to input addresses (pure self-loop, no external recipient).
- `ExternalOutputs(tx)` — for txs that DO have external recipients, strips only the change-to-self output from the displayed output list, so the tx remains visible but the self-directed change is invisible. Applied in block lookup and right-column preview.

**Done** = all phases complete and verified in-engine across the April 2009 boundary.

---

## 6. In-engine verification checklist

After all phases are implemented, run the following checks before closing the branch:

- [ ] **Pre-activation (game date < 2009-04-26)**: open each send panel (player, founders, casino, one miner bot) — fee row is invisible in all four.
- [ ] **Post-activation (game date ≥ 2009-04-26)**: open each send panel — fee row appears, pre-filled with `0.10000000`.
- [ ] **Clamp on focus-exit**: in BTCWallet, enter `0.05` → click away → field resets to `0.10000000`. Enter `5.0` → click away → resets to `0.10000000`. Enter `0.5` → click away → stays `0.50000000`.
- [ ] **Clamp on send**: enter `0.05` → press Send → value is clamped to `0.1` before the tx fires (check tx fee in Block Explorer).
- [ ] **"Go Back" button**: present and functional in all four wallets.
- [ ] **Sender balance visible**: the send panel shows the sender's BTC balance without requiring a return to the base wallet.
- [ ] **Bot automated fees before 2009-04-26**: in Block Explorer, confirm transactions from bots mined before the activation block carry `Fee: 0.00000000`.
- [ ] **Bot automated fees after 2009-04-26**: confirm bot transactions mined after the activation block carry `Fee: 0.1x…`.
- [ ] **Casino pool payouts before 2009-04-26**: confirm pool-payout transactions carry `Fee: 0.00000000`.
- [ ] **Casino pool payouts after 2009-04-26**: confirm pool-payout transactions carry `Fee: 0.10000000`.
- [ ] **Hearn round-trip (2009-04-18)**: still fee-free — no regression.
- [ ] **Chain validity**: `BlockchainService.ChainIsValid()` passes on a chain that crosses the fee-activation boundary.

---

## 7. Open questions (carry-forward)

| ID | Question | Status |
|---|---|---|
| OQ-10.1 | Should the fee field auto-show/hide if the game clock crosses 2009-04-26 while the send panel is open? | **Deferred.** Current design reads game time once on `EnterSendMode()`. Player must close/reopen the panel. Acceptable for v1 — the date change happens during active betting, not while idle on a send panel. |
| OQ-10.2 | Should the minimum fee also be enforced server-side (in `BlockchainService.AddTransactionToPendingTransactions`)? | **Deferred.** UI-only for now. A server-side min-fee check is a later hardening step. |
| OQ-10.3 | The `CreateAndBroadcastTransaction(fromNodeId, recipientNodeId, amount, decimal fee = 0m)` overload (node-ID version) is used for founder ↔ founder sends. Should it also default to `NetworkFeePolicy.DefaultFee` post-activation? | **Deferred.** Founder-to-founder sends are DEV/scripted — leaving the default 0m is safe for now. |
