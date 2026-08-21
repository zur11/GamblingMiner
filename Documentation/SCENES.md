# Scenes & Navigation

> Extracted from `CLAUDE.md` on 2026-08-21 (Dep-01 D2.3), which had carried it as `## Scene Management`.
> Per the Document Policy, a system's specification lives here; `CLAUDE.md` keeps the index and the rules.
>
> **This document was REBUILT, not moved.** The section it replaces had three claims that the code
> contradicts, all recorded in §4 — including a subsection declaring the SceneManager migration still
> pending, when it has been complete for some time. The inventory below was generated from
> `SceneManager.SceneId` and every path verified to resolve to a file on disk.

---

## 1. The scene inventory — 25 ids, all verified (2026-08-21)

Source of truth: `Scripts/Services/SceneManager.cs`. Every row was checked to resolve to an existing
`.tscn`.

| `SceneId` | File |
|---|---|
| `MainMenu` | `Screens/MainMenu/MainMenu.tscn` |
| `DiceGame` | `Screens/DiceGame/DiceGame.tscn` |
| `BlockExplorer` | `Screens/BlockExplorer/BlockExplorer.tscn` |
| `BankrollProgrammer` | `Screens/BankrollProgrammer/BankrollProgrammer.tscn` |
| `BetsHistoryExplorer` | `Screens/BetsHistoryExplorer/BetsHistoryExplorer.tscn` |
| `CalendarsNavigator` | `Screens/CalendarsNavigator/CalendarsNavigator.tscn` |
| `MartingaleCalculator` | `Screens/MartingaleCalculatorStandalone/MartingaleCalculatorStandalone.tscn` |
| `BTCWallet` | `Screens/BTCWallet/BTCWallet.tscn` |
| `BotsBtcWallets` | `Screens/BotsBtcWallets/BotsBtcWallets.tscn` |
| `CompaniesWallets` | `Screens/CompaniesWallets/CompaniesWallets.tscn` |
| `CastMinerWallets` | `Screens/CastMinerWallets/CastMinerWallets.tscn` |
| `CasinoFinances` | `Screens/CasinoFinances/CasinoFinances.tscn` |
| `FoundersWallets` | `Screens/FoundersWallets/FoundersWallets.tscn` |
| `BotPlayHistory` | `Screens/BotPlayHistory/BotPlayHistory.tscn` |
| `BTCPoolsAndHardwareShop` | `Screens/BTCPoolsAndHardwareShop/BTCPoolsAndHardwareShop.tscn` |
| `CasinoGamblingFinances` | `Screens/CasinoGamblingFinances/CasinoGamblingFinances.tscn` |
| `ClientsBetsHistory` | `Screens/CasinoGamblingFinances/ClientsBetsHistory.tscn` |
| `ClientsTransactions` | `Screens/CasinoGamblingFinances/ClientsTransactions.tscn` |
| `ScFinances` | `Screens/ScFinances/ScFinances.tscn` |
| `ScTransactions` | `Screens/ScFinances/ScTransactions.tscn` |
| `CasinoCoinSwaps` | `Screens/CasinoCoinSwaps/CasinoCoinSwaps.tscn` |
| `AuctioningCompanyDetails` | `Screens/AuctioningCompanyDetails/AuctioningCompanyDetails.tscn` |
| `CompanyDetails` | `Screens/CompanyDetails/CompanyDetails.tscn` |
| `WorldEconomy` | `Screens/WorldEconomy/WorldEconomy.tscn` |
| `CentralBank` | `Screens/CentralBank/CentralBank.tscn` |

**Two names that do not match their folder, and both are deliberate:**

- **`SceneId.MartingaleCalculator` resolves to `Screens/MartingaleCalculatorStandalone/`.** There are two
  calculators: the **popup**, instantiated inline by DiceGame's own button and owning no `SceneId` because
  it never navigates; and the **standalone full screen**, which is what this id routes to. A scene
  reachable only by instantiation is invisible to `SceneManager` by design.
- **`ClientsBetsHistory`, `ClientsTransactions` and `ScTransactions` live inside another screen's folder**
  (`CasinoGamblingFinances/` and `ScFinances/` respectively). They are sub-screens of their hub, and the
  folder says so.

## 2. Adding a scene

Three steps, in `Scripts/Services/SceneManager.cs`:

1. add the entry to the `SceneId` enum;
2. add its path to the `Paths` dictionary;
3. call `_sceneManager?.Go(SceneManager.SceneId.X)` at the call site.

`Go()` also records a one-deep `PreviousScene`, which is what makes origin-aware back navigation possible
(`BetsHistoryExplorer` and `CasinoCoinSwaps` both use it, since each is reachable from more than one hub).

## 3. The `StatusBar` component

`UI/StatusBar/StatusBar.cs` — a pure C# `HBoxContainer`, no `.tscn`, instantiated programmatically in each
screen's `_Ready()`. Shows Main Balance, Bankroll, the player's BTC wallet, the game clock and the BTC
price ticker.

```csharp
// In _Ready() of any screen — insert at the top of a VBoxContainer:
var vbox = GetNode<VBoxContainer>("ContainerPath");
var statusBar = new StatusBar();
vbox.AddChild(statusBar);
vbox.MoveChild(statusBar, 0);

// Or, for scenes with a placeholder slot (MainMenu, MartingaleCalculatorStandalone):
GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
```

⚠ **The two BTC cells are different KINDS of figure and must stay visually distinguishable** (2026-08-06).
`BTC Wallet: 12.50000000` is money the player *owns* — **bitcoin orange** (`#F7931A`), beside the SC
balances it belongs with. `BTC Price: 1,234.56 SC` is a market quote they do not own — default colour, at
the far end, `BTC Price: —` before Market Birth (2010-07-18) and `BTC Price: HALT` on the 13 halt days.
**Do not add a third bare number to this bar.** *(This rule is also kept in `CLAUDE.md`.)*

**Refresh cadence differs per cell, deliberately:**

| Cell | Cadence | Why |
|---|---|---|
| SC balances + clock | `_Process`, every frame | Cheap field reads; the clock genuinely needs real delta |
| BTC price | Event only — `BtcMarketDataService.MarketDayChanged` | A daily step function |
| BTC wallet | `NetworkRoot.BlockAccepted` (dirty flag drained next frame) **+ a 2 s fallback tick** | It runs one `AggregateSpendable` pass over the whole UTXO set — cheap at this cadence, ruinous per frame (§38.7). The block is the real edge; the fallback exists only because the player's own send drops spendable instantly, with no block to announce it. The static event is unsubscribed in `_ExitTree` |

`GetPlayerSpendableBalanceStatic()` is a **static** twin of the instance `GetNodeSpendableBalance`, because
the StatusBar is instantiated in every screen and owns no `NetworkRoot` node (the
`GetPlayerChainLengthStatic` precedent). ⚠ **It reads the owned ADDRESS SET, never `WalletAddress` alone** —
the P16.6 trap: base-only reads went to zero once change rotation landed.

## 4. Navigation map

Rebuilt 2026-08-21 from `SceneManager.SceneId` rather than copied from the previous version, which was
missing nine scenes. `[DEV]` marks screens not intended for players.

```
MainMenu
├── DiceGame                        (also reachable directly; has its own "Main Menu" button)
│   ├── ScFinances                  → Main Menu      ("Deposit Balance" opens it; DepositPopup retired in Step 12)
│   ├── BankrollProgrammer          → Main Menu
│   ├── BlockExplorer               → Main Menu
│   │   ├── AuctioningCompanyDetails    → BlockExplorer   (Step 14 ND.5 — live tracked-donation pool while InAuction; forwards to CompanyDetails on resolution)
│   │   └── CompanyDetails              → BlockExplorer   (Step 14 ND.8b.4 — Board Vote / dividend claims; Step 16 P16.5/P16.8 vote policy + abstention)
│   ├── CalendarsNavigator          → Main Menu / BetsHistoryExplorer
│   │   └── BetsHistoryExplorer         → origin-aware back
│   ├── BTCWallet                       (the player's BTC wallet + send)
│   ├── BTCPoolsAndHardwareShop         (hardware credits + individual vs casino pool)
│   └── MartingaleCalculator            (POPUP, inline — no SceneId; the standalone is a separate screen)
├── ScFinances  [player-facing]     → Main Menu      (Step 12 — the player's SC-flows hub)
│   ├── ScTransactions              → ScFinances
│   ├── BetsHistoryExplorer         → origin-aware back
│   ├── BankrollProgrammer          → ScFinances
│   └── CasinoCoinSwaps             → origin-aware back
├── CasinoCoinSwaps  [player-facing] → Main Menu     (Step 13 — the casino's SC↔BTC swap desk)
├── MartingaleCalculator (standalone, full screen) → Main Menu
├── WorldEconomy      [DEV]  → Main Menu            (Step 14 ND.8c — SC Monetary Ledger; ND.8b.6 company knobs)
├── CentralBank       [DEV]  → Main Menu            (Step 15 P15.1e — the FED's per-client loan accounts)
├── CasinoFinances    [DEV]  → Main Menu            (casino BTC wallet / mining view)
├── FoundersWallets   [DEV]  → Main Menu            (Satoshi, Hal, Mike Hearn)
├── BotsBtcWallets    [DEV]  → Main Menu            (the four miner bots' BTC wallets)
├── CompaniesWallets  [DEV]  → Main Menu            (Step 16 P16.3b — the 40 companies; split out of BotsBtcWallets)
├── CastMinerWallets  [DEV]  → Main Menu            (Step 16 P16.3c — the Step-14 historical cast)
├── BotPlayHistory    [DEV]  → Main Menu            (per-bot rolling play history)
└── CasinoGamblingFinances [DEV] → Main Menu
    ├── ClientsBetsHistory  [DEV]   → CasinoGamblingFinances
    └── ClientsTransactions [DEV]   → CasinoGamblingFinances
```

**Origin-aware back** (`SceneManager.PreviousScene ?? MainMenu`): `BetsHistoryExplorer` returns to whichever
hub launched it — `CalendarsNavigator` or `ScFinances` — and `CasinoCoinSwaps` likewise, from `MainMenu` or
`ScFinances`.

## 5. What the previous version claimed, and what the code says

Three claims were carried in `CLAUDE.md` and are false. They are recorded rather than silently dropped,
because each shows a different way a document rots.

| Claim | Reality | The rot |
|---|---|---|
| *"Scene transitions are currently done inline with hardcoded paths… fragile and should be replaced"* — a whole subsection headed **"Current State (to be migrated)"** | **Zero** `ChangeSceneToFile` call sites outside `SceneManager`; **62** `Go()` call sites | A **completed migration whose to-do note was never deleted**. It sat directly above a paragraph saying the migration was done — the file contradicted itself on adjacent lines |
| A `SceneId` enum listing **7** entries, with `MainMenu, // planned` | **25** entries; `MainMenu` has existed for a long time | An **illustrative code sample frozen at the moment it was written**. A wrong example is worse than none: it is copied |
| `[SceneId.MartingaleCalculator] = "…/MartingaleCalculator.tscn"` | It resolves to `MartingaleCalculatorStandalone.tscn` | The same sample, wrong about a path a reader would trust |

> **A file tree goes stale invisibly (Dep-01 §4.1); a code sample goes stale *and gets copied*.** That is
> why the Document Policy sends long examples to the system's doc and keeps only a minimal one — a short
> example is cheap to check against the code, and a long one never is.
