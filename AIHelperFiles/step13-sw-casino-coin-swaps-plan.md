# CasinoCoinSwaps — Swap Desk Design Plan (Step 13 / Phase SW.\*)

**Status**: v2 (2026-07-06) — **round-1 decisions LOCKED** (§0.5, D-SW.1…D-SW.10). Ready for implementation (SW.0).

**Scope**: The detailed design round that `step13-btc-market-data-and-dev-alt-timeline-plan.md` Phase SW.\* reserved for itself. Developed **on this same branch** (`btc-market-data-and-dev-alt-timeline`) against the live simulacrum world (TL.2 already flipped) — **no new branches**; work proceeds through this plan's SW phases.

**Deliverable**: the casino's swap desk — a new scene **`CasinoCoinSwaps`** with two swap panels (SC→BTC and BTC→SC, casino-as-dealer), per-asset DEV reserve controls, availability gating, clamps, and a rank-ready fee model.

**Companion docs**: `AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md` (§4.3 framing, D-13.2/13.4/13.5/13.6/13.11) · `Scripts/Services/PlayerBankAccountService.cs` (`TryAutoWithdraw` — the threshold/surplus reserve pattern this plan generalizes) · `Scripts/Services/CasinoScBalanceService.cs` (casino SC sheet + recharge telemetry) · `Documentation/ProjectDesignManual.md` Ch. 29 (MANDATORY before building the scene) · CLAUDE.md Important Pattern 2 (block = the only commit).

---

## 0. Inherited decisions (locked upstream — not re-litigated here)

| From | Decision | Effect here |
|---|---|---|
| D-13.2 | Intraday price = step function | One quote per in-game day, `BtcMarketDataService.GetEffectivePriceUsd()`; deterministic across restarts |
| D-13.4 | Casino-as-dealer | The casino sells only BTC it actually owns and buys with its real SC. Swaps are real UTXO sends + SC service mutations. Liquidity limits ARE gameplay |
| D-13.6 | Swap UI = its own scene, MainMenu + ScFinances links, Ch. 29 fixed-footer layout from day one | **Naming AMENDED (user, 2026-07-06): the scene is `CasinoCoinSwaps`** (`Screens/CasinoCoinSwaps/`), superseding the `BtcSwap` placeholder name. Everything else in D-13.6 stands |
| D-13.11 | Halt days close the desk | Both panels disabled on `IsHaltDay`, greyed last price + historical reason |
| D-13.5 | Post-2025-12-31 → freeze last price + "post-history era" label | Desk stays open at the frozen price |
| §4.3 atomicity | SC leg instant, BTC leg via mempool → next block; a restart unwinds both legs together (nothing between blocks persists) | No same-block guarantee needed; the pending-BTC state must be *visible* in the panel (§4.4) |

**Superseded**: §4.3's "spread dev-configurable (e.g. default 2%)" sketch is **absorbed** into the swap fee (D-SW.9): one price, one fee — the old spread knob *becomes* the fee knob, still living in `CasinoGamblingFinances` as §4.3 intended, now default 10% with a 1%–10% range, applied to both swap directions. No separate bid/ask spread exists.

---

## 0.5 Decisions locked (round 1 — 2026-07-06)

| # | Decision |
|---|---|
| **D-SW.1** | Fee allocation exactly as the §3.3 worked examples: Panel A margin retained in BTC (deliver less BTC, keep the full SC), Panel B player debited exactly B total with the 0.1 network fee inside it, margin retained in SC; floor `max(fee%, 0.1 BTC)` + 1-BTC-gross minimum swap size surfaced in the UI. |
| **D-SW.2** | Player side: swaps touch the player's **Main Balance only** (never the Bankroll). |
| **D-SW.3** | Casino side: swaps touch the **casino's Main Balance only** (the Bankroll stays `ApplyBetResult`'s bet float). Both parties' swap legs are Main↔Main. |
| **D-SW.4** | Ledger: two new `LedgerEntry.Kind`s — `"swap_sc_out"` / `"swap_sc_in"` — excluded from deposited/withdrawn totals AND from the since-last-deposit baseline. GLOSSARY entries in SW.6. |
| **D-SW.5** | Swap SC flows do **not** touch the betting-stats scopes (`PlayerFinancialStatsCalculator` stays bet/deposit/recharge-driven). |
| **D-SW.6** | Bought BTC is delivered to the player's **base address**; no fresh-address-per-swap (address non-reuse stays Satoshi-only, Step 8). |
| **D-SW.7** | `[DEV] Seed casino BTC` button: **kept in the plan as a testing-convenience alternative, NOT a priority** — build it only if SW.1 finds natural pool-mining accrual too slow for iteration. |
| **D-SW.8** | Gating DEV components out of public builds: **deferred to the pre-release pass**. v1 ships them inline, tagged `[DEV]`. |
| **D-SW.9** | **All swap-desk DEV knobs live in the casino's existing DEV scenes; `CasinoCoinSwaps` itself carries NO DEV controls** *(amended 2026-07-06 — the BTC selector was briefly slated for `CasinoCoinSwaps`, then moved)*. The old §4.3 spread panel adapts into the swap-fee control in **`CasinoGamblingFinances`**: one percent, default **10%**, clamped to **1% (min) – 10% (max)**, governing BOTH swap directions. Beside it, `CasinoGamblingFinances` also hosts the casino's **SC swap-reserve selector** (on its Main Balance, default 0) with the %/amount toggleable mode. The **BTC reserve selector lives in `CasinoFinances`** — literally the casino's BTC wallet scene, the natural home — in a new panel there (toggleable fixed-amount or % of the casino's BTC wallet total, default 0). All of these are DEV controls, needed for testing, to be superseded by a future **auto-swaps-scheduler** fed by the data gathered during testing (§2.4). |
| **D-SW.10** | Swap history surfaces **only inside `CasinoCoinSwaps`** for now (ledger entries from D-SW.4 still appear in the `ClientsTransactions` DEV scene as a side effect). |

*Interpretation note on D-SW.9's "% of the base address total" — **CONFIRMED (user, round 2)**: it means the casino's **whole BTC wallet** (base + change addresses); change rotation moves funds off the base address on every send, so a base-address-only percent would silently shrink the reserve.*

---

## 1. The desk model — two panels, one availability rule

```
CasinoCoinSwaps
├── Panel A — "Buy BTC"  : player gives SC  → casino delivers BTC (on-chain)
└── Panel B — "Sell BTC" : player gives BTC (on-chain) → casino credits SC
```

Both panels obey the same core rule:

```
OfferedForSwap(asset) = max(0, CasinoBalance(asset) − Reserve(asset))
```

- **Panel A** consumes the casino's **offered BTC** (from its real on-chain wallet, confirmed UTXOs minus pending outgoing).
- **Panel B** consumes the casino's **offered SC** (from `CasinoScBalanceService.MainBalance` — the Bankroll is the betting float and is never touched by swaps; D-SW.3).
- The **non-offered remainder is the casino's strategic reserve** — BTC held back to sell later when the casino's SC gets low and it needs liquidity urgently; SC held back to sustain the betting pace it is currently servicing (§2.3).

### 1.1 First-funds gating (the first goal)

The scene is always *reachable*, but its tools enable/disable live:

| Condition | Effect |
|---|---|
| Game clock < `BtcMarketDataService.FirstDataDateLocal` (canon worlds) | Whole desk locked: "No exchange exists yet — the first Bitcoin market opens 18 Jul 2010" |
| `IsHaltDay` (D-13.11) | Whole desk closed: greyed last price + the historical halt reason (June 2011 Mt. Gox hack / Aug 2016 Bitfinex hack) |
| Casino offered BTC ≤ `MinDeliverableBtc` (§4.1) | **Panel A disabled**, reason label: "Casino has no BTC available for swaps" (reserve % shown to DEV) |
| Casino offered SC ≤ minimum SC payout | **Panel B disabled**, reason label: "Casino has no SC available for swaps" |
| Both funded | Both panels enabled |

Enablement is **event-driven, not per-frame**: recompute on `CasinoScBalanceService.BalanceChanged`, on block-mined (BTC confirmations change the casino's spendable set), on `BtcMarketDataService.MarketDayChanged`, and after every swap/reserve-setting change. A panel that runs dry mid-session disables itself on the next event.

Note the natural progression this produces in a fresh (simulacrum) world: the casino starts with **zero BTC and zero SC** (extra-lazy). Its first SC arrives with the player's first lost bets → Panel B wakes first. Its first BTC arrives via casino pool mining → Panel A wakes later. That ordering is emergent and correct; SW.1 verifies it is *fast enough* for dev iteration and adds a DEV seed fallback if not (D-SW.7 — documented alternative, not a priority).

---

## 2. Reserve controls — the generalized threshold model (DEV-only, scheduler-ready)

### 2.1 The pattern being generalized

`PlayerBankAccountService.TryAutoWithdraw` already implements the shape the user asked to mirror: a **floor** (`max(AutoWithdrawThreshold, live dose)`) below which the source account is never drained, and movement only of the **surplus** above it. The swap desk applies the same idea *statically*: the reserve is the floor; only the surplus is offered.

### 2.2 Per-asset reserve setting — percent OR amount (toggleable)

One `ReserveSetting` per asset (BTC, SC), owned by the new `CasinoCoinSwapService` (§5):

```csharp
public sealed class ReserveSetting
{
	public bool    UsePercent { get; set; } = true;   // toggle: percent-of-balance vs absolute amount
	public decimal Percent    { get; set; } = 0m;     // 0–100; START 0 (= 100% offered, for testing)
	public decimal Amount     { get; set; } = 0m;     // absolute floor in the asset's own unit

	public decimal ReserveFor(decimal balance) =>
		Money.Normalize(UsePercent ? balance * (Percent / 100m) : Math.Min(Amount, balance));
}
```

- **Defaults: reserve 0 ⇒ 100% offered** — the user-specified testing starting point, trivially changeable live.
- **DEV UI placement (D-SW.9, as amended)**: **`CasinoCoinSwaps` hosts NO DEV controls** — it only *displays* offered/reserve readouts. The **BTC reserve selector lives in `CasinoFinances`** (the casino's BTC wallet scene — the reserve is a wallet-level property, so it sits with the wallet): a new panel with a `[%|BTC]` mode toggle + SpinBox + Apply, tagged `[DEV]`; percent is of the casino's whole BTC wallet (confirmed, §0.5 note). The **SC reserve selector lives in `CasinoGamblingFinances`** (the casino's SC knob hub), same toggleable %/amount component, percent of its Main Balance, default 0 — beside the swap-fee knob (§3.1). Both stay DEV forever — they are the manual console we learn from before automating.
- Setters go through the service (`SetBtcReserve(...)` / `SetScReserve(...)`), never straight from UI to fields — so the future **auto-swaps-scheduler** calls the *exact same* setters regardless of which scene hosts the knob. That is the whole groundwork requirement: manual DEV knobs and the future scheduler share one API and one trace log.

### 2.3 The SC-side equilibrium — PENDING CALCULATION TASK (SW.5)

The casino's SC reserve must sustain **the betting pace it is currently receiving** — swaps must not drain the SC that tomorrow's player wins will claim. Finding that floor is an explicit pending task; options to evaluate during testing (data sources already exist):

| Option | Floor formula | Data source | Cost |
|---|---|---|---|
| **R1 — static multiple** (SW.0 placeholder) | `N × BankrollTarget` (N dev-tunable, default e.g. 10) | none | zero — ship day one |
| **R2 — recharge pace** (recommended for SW.5) | `SafetyFactor × dosesConsumedInWindow × BankrollTarget`, window = last W in-game hours | `CasinoScBalanceService.RechargeHistory` (game-dated, already capped at 500) | small — pure read |
| R3 — drawdown-based | `k × maxBankrollDrawdown(last M bets)` | new ring buffer in the swap service subscribed to `BalanceChanged` | medium — new telemetry |

SW.5 implements R2 behind a DEV toggle ("auto floor" vs the manual §2.2 setting; the **effective** SC floor = `max(manual reserve, auto floor)` when enabled — same `max()` composition as `TryAutoWithdraw`'s anti-ping-pong guard). The toggle sits in `CasinoGamblingFinances` next to the SC reserve selector it composes with (D-SW.9). R3 is kept as the comparison candidate if R2 proves too coarse.

### 2.4 The BTC-side strategic reserve + the future scheduler (sketch only, NOT built now)

The BTC reserve's purpose is asymmetric: it is a **war chest**, not an operating float. The future `automatic-reserve-scheduler` (post-testing) would adjust the §2.2 knobs by rules like:

- `if CasinoScBalanceService.TotalSc < UrgencyThreshold` → lower the BTC reserve % (release the war chest — sell more BTC for SC exactly when SC is scarce, which is the user's stated intent for the reserve).
- `if CumulativeProfitSinceLoan > ComfortThreshold` → raise the BTC reserve % (accumulate).

**Built now (groundwork only)**: the shared setter API (§2.2), and a trace log `user://logs/swap_desk_trace.csv` (mirroring `founders_trace.csv`) — one row per swap and per reserve change: game date, panel, amounts, fee, both casino balances, both reserves, both offered figures. This is the dataset the scheduler's rules will be tuned from.

---

## 3. Fee model — 10% flat, network-fee-inclusive, rank-ready

### 3.1 The rule (v1)

> **Swap fee = 10% of the gross swap value, in both panels — and it INCLUDES the BTC network fee** (min 0.1 BTC, hardcoded). The player is never charged the network fee *on top of* the 10%; the casino pays the on-chain fee out of its collected margin.

```csharp
// CasinoCoinSwapService — rank-ready from day one:
public const decimal DefaultSwapFeePercent = 10m;
public const decimal MinSwapFeePercent     = 1m;    // D-SW.9 clamp range
public const decimal MaxSwapFeePercent     = 10m;
public decimal SwapFeePercent { get; private set; } = DefaultSwapFeePercent;   // persisted; setter clamps to [1,10]

// THE accessor every quote/execution path uses. Today it ignores clientId and returns the global
// value; the rank system (future step) overrides per client HERE and nowhere else.
public decimal GetSwapFeePercentFor(string clientId) => SwapFeePercent;
```

- All fee math flows through `GetSwapFeePercentFor("player")` — when ranks arrive, only this accessor changes.
- **The DEV fee knob lives in `CasinoGamblingFinances`** (D-SW.9 — the old §4.3 spread panel adapted): one SpinBox, default 10%, clamped 1%–10%, governing **both** swap directions. `CasinoCoinSwaps` only *displays* the current fee in its quotes; it has no fee control.

### 3.2 Fee floor

If 10% of the gross BTC value is smaller than the 0.1 BTC network fee, the casino would swap at a loss. Floor:

```
feeBtcEquivalent = max(SwapFeePercent% × grossBtc, NetworkFeePolicy.MinFee /* 0.1 BTC */)
```

Equivalently the UI enforces a **minimum swap size of 1 BTC gross** (at 10%, fee ≥ 0.1 exactly at 1 BTC) — proposal: implement the floor in the math AND surface the min-size clamp in the UI, so tiny inputs get an honest "minimum swap is X" message instead of a silently worse rate (confirmed, D-SW.1).

### 3.3 Worked examples (landing-day price 0.0679 SC/BTC)

**Panel A — player buys BTC with 100 SC:**
- Gross BTC = 100 / 0.0679 = 1,472.75 BTC
- Fee 10% → net delivered = 1,325.48 BTC (on-chain, casino → player base address)
- Casino: Main Balance +100 SC; BTC out = 1,325.48 + 0.1 network fee; margin retained = 147.28 − 0.1 = 147.18 BTC
- Player: Main Balance −100 SC; +1,325.48 BTC once the tx confirms at the next block

**Panel B — player sells 1,000 BTC:**
- Gross value = 1,000 × 0.0679 = 67.90 SC; fee 10% = 6.79 SC → **player credited 61.11 SC** (instant, into Main Balance)
- On-chain leg: player broadcasts 999.9 BTC to the casino's address + 0.1 network fee = exactly 1,000 BTC debited from the player. The 0.1 comes out of the *swap*, not on top — the casino absorbs it from its 6.79 SC margin (≈ 0.007 SC at this price)
- Casino: Main Balance −61.11 SC; +999.9 BTC once confirmed

*(These allocations — who is debited what, in which asset the margin lands — are **confirmed with these exact numbers** (D-SW.1).)*

### 3.4 Pending study — real-world fee history (NOTED, not in scope)

The 0.1 BTC network fee is **fully hardcoded** (`NetworkFeePolicy.MinFee`) and historically naive: real 2009–2013 Bitcoin had zero-fee relay policies, the 0.01 BTC default fee era, fee-per-kB rules, and priority (coin-age) free transactions. **Pending task (deferred, own research round)**: study the real fee regimes of these eras and decide whether/how the sim's flat 0.1 should become era-aware — logged here so it isn't lost; do NOT block SW.\* on it.

---

## 4. Swap execution pipelines

### 4.1 Panel A — SC → BTC (casino sells)

```
Input: S (SC, one field; live quote readout below it)
Quote: grossBtc = S / price;  fee = max(10% × grossBtc, 0.1);  netBtc = grossBtc − fee
Clamps (live "Max" label + input hard-clamp):
  S ≤ player Main Balance                                  (player-side limit)
  netBtc + 0.1 ≤ OfferedBtc                                (casino-side limit)
    ⇒ S_max = min(playerMain, (OfferedBtc − 0.1) × price / (1 − fee%))
  S ≥ min swap size (§3.2)
Execute:
  1. Player Main Balance −S            (PrincipalBalanceService, instant)
  2. Casino Main Balance +S            (CasinoScBalanceService, instant — D-SW.3)
  3. Casino → player on-chain send of netBtc, fee 0.1       (existing NetworkRoot
     CreateAndBroadcastTransactionToAddress("casino", …) / BuildAndBroadcastUtxoSpend path —
     multi-input coin selection + change rotation already work)
  4. Ledger entry (D-SW.4) + SwapRecord + trace row
```

`MinDeliverableBtc` (Panel A's enable threshold, §1.1) = the smallest `OfferedBtc` for which any legal swap exists ≈ `0.1 + netBtc(minSwapSize)`.

### 4.2 Panel B — BTC → SC (casino buys)

```
Input: B (BTC — the TOTAL the player will part with, network fee included)
Quote: grossSc = B × price;  feeSc = max(10% × grossSc, 0.1 × price);  netSc = grossSc − feeSc
Clamps:
  B ≤ player spendable BTC (confirmed UTXOs − pending outgoing)
  netSc ≤ OfferedSc        ⇒ B_max = min(playerSpendable, OfferedSc / (price × (1 − fee%)))
  B ≥ 1 BTC (fee floor, §3.2)
Execute:
  1. Player → casino on-chain send of (B − 0.1), fee 0.1    (player pays exactly B total)
  2. Casino Main Balance −netSc; player Main Balance +netSc (instant — the SC leg does NOT wait
     for confirmation; see §4.4)
  3. Ledger entry + SwapRecord + trace row
```

### 4.3 Clamp UX (both panels)

- Every input shows a live **`Max: X`** line naming *which* limit binds ("your Main Balance" vs "casino BTC available" vs "casino SC available") — the user's requirement that players understand *whose* balance runs out.
- A `MAX` button fills the binding maximum. Inputs hard-clamp on submit (mirror `TriggerManualDeposit`'s `Math.Min` safety even though the UI validates first).
- Quote readout updates on every keystroke: `You give X → fee Y → you receive Z` with the fee line explicit.

### 4.4 Atomicity, pending state, restarts (restating the locked model)

- The SC leg settles **instantly** in both panels; the BTC leg confirms at the **next mined block**. Between those, the panel shows a `⏳ pending confirmation` row (amount + direction).
- An app restart before the confirming block **unwinds both legs together** — the checkpoint model reverts SC balances and the mempool as one unit (nothing between blocks persists). This is already the locked design; the scene just needs to *display* pending honestly so a dev/player understands what a restart would revert.
- Casino-side availability must count pending: `OfferedBtc` subtracts pending outgoing casino BTC; `OfferedSc` is naturally live (service mutation already happened).

---

## 5. Service design — `CasinoCoinSwapService` (autoload #15)

New autoload `Scripts/Services/CasinoCoinSwapService.cs`. It owns swap-desk **state and policy**; execution legs delegate to the existing owners (`PrincipalBalanceService`, `CasinoScBalanceService`, `NetworkRoot`). It follows every CLAUDE.md service convention: `GetNodeOrNull` wiring in `_Ready()`, typed C# events, `Money.Normalize` everywhere, game-time timestamps only, InvariantCulture.

```csharp
public partial class CasinoCoinSwapService : Node
{
	public const decimal DefaultSwapFeePercent = 10m;

	// §2.2 — both default to "reserve 0 ⇒ 100% offered" for testing
	public ReserveSetting BtcReserve { get; }
	public ReserveSetting ScReserve  { get; }
	public decimal SwapFeePercent { get; private set; }
	public decimal GetSwapFeePercentFor(string clientId);            // rank hook (§3.1)

	// Availability (§1) — event-recomputed, cached
	public decimal CasinoBtcBalance   { get; }   // confirmed − pending outgoing (via NetworkRoot helper, SW.1)
	public decimal OfferedBtc         { get; }
	public decimal OfferedSc          { get; }
	public bool    IsPanelAEnabled    { get; }   // + a DisableReason enum per panel for the UI labels
	public bool    IsPanelBEnabled    { get; }

	// Quotes (pure, UI calls per keystroke) + execution (clamps re-checked inside)
	public SwapQuote QuoteScToBtc(string clientId, decimal scAmount);
	public SwapQuote QuoteBtcToSc(string clientId, decimal btcAmount);
	public bool TryExecuteScToBtc(string clientId, decimal scAmount, out string error);
	public bool TryExecuteBtcToSc(string clientId, decimal btcAmount, out string error);

	// History: SwapRecord { GameDateLocal, ClientId, Direction, GrossIn, FeeCharged, NetOut, PriceUsed, Method }
	// capped at 500 (mirror MaxTransferHistory) + swap_desk_trace.csv appends (§2.4)
	public IReadOnlyList<SwapRecord> SwapHistory { get; }

	public event Action SwapDeskChanged;
}
```

- **`clientId` everywhere from day one** — only `"player"` is wired now; bots join later by calling the same API (user requirement). Bot integration itself is out of SW.\* scope (noted in §11).
- **Persistence**: `user://casino_coin_swap_state.json` (settings + history). Per **Important Pattern 2 the answer to "does this need checkpoint + pre-genesis paths?" is YES**: a `CheckpointState` DTO bundled into `BlockSessionCheckpointService.Snapshot` (the `PlayerBankAccountService` bundling pattern), restored post-block; `ResetToPreGenesisDefaults()` → reserves 0 / fee 10% / history cleared on every pre-genesis boot. Settings stick only at a block, like every other knob.
- **Not** a bet-tick participant: no `_Process` logic beyond nothing — everything is event-driven (§1.1).

---

## 6. Scene design — `Screens/CasinoCoinSwaps/`

**⚠ Ch. 29 first** (per CLAUDE.md and memory): fixed footer OUTSIDE the scroll, `MarginContainer(preset 15, margin_bottom ≥ 50) → VBoxContainer → { ScrollContainer(size_flags_vertical=3) → content, footer row }`, `mouse_filter = PASS` down the chain, no `fit_content` RichTextLabel inside the ScrollContainer. `ScTransactions` is the reference structure.

```
MarginContainer (full-rect, margin_bottom ≥ 50)
└── VBoxContainer
    ├── StatusBar (programmatic, slot 0 — standard pattern)
    ├── Header row:  "Casino Coin Swaps"   |  price cell: "1 BTC = 0.0679 SC (18 Jul 2010)"
    │                                      |  desk-state label (open / HALTED: reason / no market yet)
    │                                      |  fee readout (display only): "Swap fee: 10%" (knob in
    │                                      |    CasinoGamblingFinances — D-SW.9)
    ├── ScrollContainer (size_flags_vertical = 3)
    │   └── HBoxContainer (two columns — HBox, NOT HSplit, per Ch. 29)
    │       ├── PanelContainer — PANEL A "Buy BTC (pay SC)"
    │       │     Casino BTC available: 1,234.56  (offered / reserve readout — BTC reserve is
    │       │       set in CasinoFinances, D-SW.9; this panel only shows the result)
    │       │     You pay:   [SC input] [MAX]     Max: 39,900 SC (your Main Balance)
    │       │     Quote:     fee 10% → you receive ≈ 1,325.48 BTC
    │       │     [ SWAP ]   (+ ⏳ pending row when a leg awaits confirmation)
    │       │     (disabled state: everything greyed + reason label, §1.1)
    │       └── PanelContainer — PANEL B "Sell BTC (receive SC)"
    │             Casino SC available: 5,000.00   (offered / reserve readout — SC reserve is
    │               set in CasinoGamblingFinances, D-SW.9; this panel only shows the result)
    │             You send:  [BTC input] [MAX]    Max: 999.90 BTC (casino SC limit)
    │             Quote:     fee 10% (incl. 0.1 network fee) → you receive ≈ 61.11 SC
    │             [ SWAP ]
    │   (below the two panels, full-width): recent swaps list (last N SwapRecords)
    └── Footer (sibling of the scroll): [ Back ]   ← origin-aware (MainMenu / ScFinances)
```

- **Navigation**: `SceneManager.SceneId.CasinoCoinSwaps` + path entry; links from MainMenu and ScFinances (D-13.6); Back uses the one-deep `PreviousScene` pattern like `BetsHistoryExplorer`.
- All labels through `Money.FormatSignedAdaptive()` / `string.Create(InvariantCulture, …)` — no raw `:N8` (CLAUDE.md number-locale rule).
- Refresh on `SwapDeskChanged` + `MarketDayChanged` + `BalanceChanged`, not per-frame.
- `CasinoCoinSwaps` carries no DEV controls (D-SW.9 as amended) — the knobs live in `CasinoFinances` (BTC reserve) and `CasinoGamblingFinances` (fee + SC reserve), both already-DEV scenes, so the D-SW.8 pre-release gating concern barely touches this scene (only the offered/reserve readout wording, if anything).

---

## 7. Phases

### SW.0 — `CasinoCoinSwapService` skeleton (no UI) — ✅ implemented (2026-07-06, pending in-editor verification)
- [x] Autoload #15 registered (inserted **before** `BlockSessionCheckpointService` in `project.godot`, so it is in the tree when the checkpoint restore / pre-genesis reset runs at boot — same ordering as `PlayerBankAccountService`); `ReserveSetting` ×2 (defaults 0 ⇒ 100% offered); `SwapFeePercent` + `GetSwapFeePercentFor` (§3.1); R1 placeholder SC floor (§2.3): `SetScFloor(enabled, N)`, dev-tunable N default 10, default OFF, composed as `EffectiveScReserve = max(manual, floor)`.
- [x] Persistence (`user://casino_coin_swap_state.json`) + `CheckpointState` DTO bundled into `BlockSessionCheckpointService.Snapshot` (capture/restore, null = legacy → keep loaded state) + `ResetToPreGenesisDefaults()` (Important Pattern 2 — both paths, day one).
- [x] `SwapRecord` history (cap 500, `RegisterSwap(...)` for the SW.3/SW.4 pipelines) + `swap_desk_trace.csv` appender (§2.4 — one row per swap and per knob change; SW.1 fills the `casinoBtc`/`offeredBtc` columns via the `CasinoBtcBalance` stub, currently 0).
- [x] `dotnet build` clean (0 warnings, 0 errors). (No headless game launch — per standing rule, in-editor verification is the developer's.)

### SW.1 — availability plumbing + first-funds gating — ✅ implemented (2026-07-06, pending in-editor verification)
- [x] `NetworkRoot` helper: **already existed** — `GetNodeSpendableBalance("casino")` (the same figure CasinoFinances' send panel shows) aggregates confirmed, coinbase-mature UTXOs across ALL casino addresses (base + change rotation) and **excludes UTXOs reserved by pending outgoing txs** (`GetSpendableUtxos` → `CollectPendingSpentOutpoints`), which is strictly better than a scalar "− pending outgoing" (the change coming back isn't counted until confirmed — exactly §4.4's conservative rule). Pending-out subtraction verified in code. Added `NetworkRoot.BlockAccepted` (static typed event, fired per LIVE accepted block inside the `!_bulkMining` guard, after pool payouts) — the block-mined trigger §1.1 needs.
- [x] `OfferedBtc`/`OfferedSc`/`IsPanelAEnabled`/`IsPanelBEnabled` + `PanelDisableReason` (`MarketNotBornYet`/`HaltDay`/`NoCasinoBtc`/`NoCasinoSc`), recomputed on the §1.1 event set (`CasinoScBalanceService.BalanceChanged`, `NetworkRoot.BlockAccepted`, `BtcMarketDataService.MarketDayChanged`, every knob change/swap). Thresholds: Panel A `OfferedBtc ≥ MinDeliverableBtc` (= net(1 BTC gross) + 0.1 = **1.0 BTC** at 10%); Panel B `OfferedSc ≥ MinScPayoutAt(price)` (= net SC of the 1-BTC minimum swap). First blockchain touch is deferred one frame past autoload boot (`CallDeferred`), so checkpoint-restore ordering is untouched; `SwapDeskChanged` fires only when the enablement snapshot changes (per-bet `OfferedSc` movement stays on `BalanceChanged`).
- [x] **Simulacrum funding timeline analyzed (code-level)**: casino **SC** arrives with the player's first lost bets (`ApplyBetResult` per settled bet) — immediate, no concern. Casino **BTC** accrues ONLY via casino-pool mining, and **every node defaults to `CasinoPoolCredits = 0`** — the dev must assign credits to the casino pool in BTCPoolsAndHardwareShop before the casino ever mines. Once assigned: casino mines ∝ its credit share, `CoinbaseMaturity = 1` (spendable one block later), and the casino nets the pool fee (10–50% of each pool block's reward) after payouts. So Panel A's wake-up is **dev-controllable and fast once credits are assigned** — the D-SW.7 seed button stays unbuilt (documented alternative); revisit only if in-editor testing (developer's step) finds the credit-assignment path too slow in practice.

**SW.1 hardening (post-playtest fix, 2026-07-07) — offer only the casino's BTC *equity*, never pool money.** First playtest (player + 4 miner bots on the casino pool) showed Panel A offering ~30 BTC, then disabling for a block, then re-opening at ~10 — a drop the desk must never produce. Root cause (code-confirmed): a casino pool coinbase is mostly the CONTRIBUTORS' money (the casino keeps only its 10–50% fee share), and the raw `GetNodeSpendableBalance("casino")` counts it all while it sits in the wallet; worse, `TryDistributePendingCasinoRewards` retries undistributed events every block and `DistributePoolEventAsSingleTx` coin-selects from *whatever is spendable* — so an event whose own coinbase became unavailable (e.g. lost a consensus race after queueing) gets paid **out of the casino's accumulated fee income**, pending-spending every casino UTXO at once (→ the one-block disable) and returning only the difference as change (→ the 30 → ~10 collapse). Fix (v2, refined after the second playtest question "why does owned read 0 while 35 is earmarked?"): `NetworkRoot.GetCasinoBtcSettlement()` returns **(settling, unbackedObligation)** — *settling* = the casino's OWN fee share that exists economically but is not yet a spendable UTXO (still inside an unspent backing coinbase, mature or immature, via the new `BlockchainService.IsUnspentOutput` probe; or in a pending payout tx's change output to a casino-owned address), *unbackedObligation* = payouts owed by events whose backing coinbase is gone from the canonical chain (consensus-race orphan — the retry raids the casino's fee income for these). `CasinoCoinSwapService`: `CasinoBtcEquity = max(0, spendable − unbackedObligation)` (**gates/offers/reserve/quotes/trace — only confirmed, truly-spendable BTC is ever offered**, because a Panel A swap must spend a confirmed UTXO now; the engine doesn't chain unconfirmed spends), `CasinoBtcSettling` + `CasinoBtcOwnedTotal = equity + settling` (the honest *display* figure — the fee share reads as the casino's from block one), and a new `PanelDisableReason.BtcSettling` so the panel says "BTC settling — swaps unlock at the next block" instead of a bare "no BTC" during the 1–2-block first-funding window. Result: *owned* never reads 0 while the casino's share is in flight, the offer only ever grows (or shrinks by actual swaps), and no contributor money is ever offered.

*(v4 — the DIAG line found the true root cause of every misclassification across all three playtests: `tip=121 chainCount=121 index OUT OF CHAIN BOUNDS` — **list position ≠ `Block.Index`** (this world's chain does not start at Index 0), so `chain[evt.BlockIndex]` always missed the freshly mined block. Fixed: resolve by base-index offset `chain[evt.BlockIndex − chain[0].Index]` + verify `.Index`. Also, per user feedback, Panel A's readout now shows just `(owned X, reserve Y)` — owned already includes the settling share, and listing settling/earmark beside it read as additive (30+35 = "65"); the settling detail stays in the red status label and the full identity in CasinoFinances' DEV line. **Warning for future code: never index a chain list by `Block.Index` directly.**)*

*(v3 — third playtest still showed "owned 0, earmarked 35", i.e. the backed-coinbase check misclassified a live event.)* Hardened `GetCasinoBtcSettlement()`: the backing coinbase is now identified by **its payout address ∈ the casino's owned set** (casino coinbases always pay the base address — `RotateCoinbaseAddress = false`), with `MinedByNodeId` kept only as a fallback (it is stamped post-hash and not guaranteed to survive block replication/consensus chain replacement — the suspected misclassification cause); fixed a v2 double-count (a **mature**-but-undistributed backed event now counts as obligation-vs-spendable — its coinbase is IN spendable — instead of settling, so equity nets to the fee share in every phase: immature → settling 15; mature-retry → 50 spendable − 35 due = 15; payout pending → change 15 settling; confirmed → equity 15 — **owned reads the fee share continuously through all four phases**); and added a once-per-event `[SwapDesk][DIAG]` console line whenever an event classifies as "no unspent backing coinbase", printing tip/chainCount/block Index/miner/coinbase-found — if the misclassification ever recurs, the console pinpoints the failing guard in one run. Side finding (pre-existing, out of SW scope, logged here): `CasinoPoolRepository.MarkDistributed` persists at *broadcast* time, so an app restart that reverts a pending payout tx leaves the event marked distributed — contributors unpaid, coinbase back to the casino. Revisit with the pool system (P4/P5 era).

### SW.2 — the `CasinoCoinSwaps` scene (display-only) + DEV rows in the two casino scenes — ✅ implemented (2026-07-06, pending in-editor verification)
- [x] **Read ProjectDesignManual Ch. 29, then** built the §6 layout (`Screens/CasinoCoinSwaps/` — fixed footer OUTSIDE the scroll, `ScTransactions` structure, `HBoxContainer` two-column panels, `mouse_filter = PASS` on the container chain, `margin_bottom = 50` safe area); `SceneId.CasinoCoinSwaps` + path; MainMenu button ("Casino Coin Swaps", after SC Finances) + ScFinances NavRow "Coin Swaps →"; origin-aware Back (`PreviousScene ?? MainMenu`).
- [x] Both panels render availability, offered/reserve readouts (each pointing at the DEV scene hosting its knob), live quote previews per keystroke (new **pure `QuoteScToBtc`/`QuoteBtcToSc` + `SwapQuote` DTO** in the service — added here because SW.2's previews need them; §4.1/§4.2 math incl. the piecewise fee-floor-aware `MaxGrossForNet` inversion for the four Max clamps, `MaxLimitedBy` naming whose balance binds, min-swap messaging), enable/disable states with reasons, halt-day (year-mapped Mt. Gox/Bitfinex copy) / no-market (data-driven `FirstDataDateLocal`) / post-history-freeze states. Header shows price + desk state + fee read-only. MAX button fills the binding max. Recent-swaps list (last 20, D-SW.10). No DEV controls in this scene (D-SW.9 as amended).
- [x] `CasinoGamblingFinances` gained the swap-desk DEV row (D-SW.9): swap-fee SpinBox (default 10%, SpinBox clamps 1–10, service re-clamps) + SC swap-reserve selector (%/amount OptionButton toggle + SpinBox + Apply, only the active mode's field changes so the toggle round-trips) + a live info line (fee, reserve, effective reserve, offered SC).
- [x] `CasinoFinances` gained the **BTC swap-reserve panel** (D-SW.9) in the BaseWalletPanel: same %/amount component, percent of the WHOLE wallet (base + change, §0.5 note), default 0, tagged `[DEV]`, wired to `SetBtcReserve`; live info line (held-back BTC, offered BTC, spendable). (Scene's existing layout untouched otherwise.)
- [x] SWAP buttons present but hard-disabled ("execution lands in SW.3/SW.4"); pending-confirmation labels exist hidden, ready for SW.3/SW.4 to fill. `dotnet build` clean (0 warnings, 0 errors).

### SW.3 — Panel A execution (SC → BTC)
- [ ] `TryExecuteScToBtc` full pipeline (§4.1): clamps re-validated service-side, SC legs, on-chain send, fee floor, ledger entry (D-SW.4), SwapRecord + trace, pending-row display, availability re-gate after execution.

### SW.4 — Panel B execution (BTC → SC)
- [ ] `TryExecuteBtcToSc` (§4.2), same standards. Includes the player-side spendable-BTC clamp (confirmed − pending).

### SW.5 — SC equilibrium auto-floor (the pending calculation task, §2.3)
- [ ] Implement R2 (recharge-pace floor) behind a DEV toggle; effective floor = `max(manual, auto)`; tune W/SafetyFactor against real testing sessions using the trace CSV; document findings for the future scheduler.

### SW.6 — polish + docs truth pass
- [ ] Min-swap-size messaging, post-2025 freeze label (D-13.5), trailing-blank-lines scroll guard, pending-restart honesty copy.
- [ ] Docs: CLAUDE.md (autoload #15, scene in the navigation map, fee canonical note), GLOSSARY (Swap / Swap Fee / Strategic Reserve / Offered Balance), ProjectDesignManual section, update the step13 plan's SW.\* line → this plan.
- [ ] Feeds into TL.3 (exit the simulacrum) unchanged — nothing here depends on the alt timeline except test convenience.

---

## 8. Testing checklist

- [ ] Fresh simulacrum world: desk opens with both panels disabled; Panel B enables after first player losses fund casino SC; Panel A enables after the casino's first confirmed BTC.
- [ ] Reserve 0/100%: full balance offered; set BTC reserve 100% (in `CasinoFinances`) ⇒ Panel A disables live; set SC reserve amount > casino SC (in `CasinoGamblingFinances`) ⇒ Panel B disables (amount-mode clamps to balance) — cross-scene: a reserve changed in either casino scene updates `CasinoCoinSwaps`' readouts on next visit/event.
- [ ] Percent↔amount toggle round-trips (both selectors); settings revert pre-genesis, stick after a block (restart tests both ways).
- [ ] Fee: 10% both directions; floor binds below 1 BTC gross (worked examples §3.3 reproduced exactly); changing the fee in `CasinoGamblingFinances` changes `CasinoCoinSwaps` quotes immediately; SpinBox refuses values outside 1–10%.
- [ ] Clamps: each of the four limits (player SC, player BTC, casino BTC, casino SC) individually binding produces the right `Max` label naming the right owner; MAX button + hard-clamp on submit.
- [ ] Panel A: player SC debited, casino SC credited, net BTC arrives at next block, casino paid the 0.1 network fee, conservation holds on-chain (Σin = Σout + fee).
- [ ] Panel B: player debited exactly B total (B − 0.1 to casino + 0.1 fee), SC credited instantly.
- [ ] Restart with a pending swap ⇒ both legs unwind together (SC balances AND mempool revert to the last block).
- [ ] Halt day (drive the clock to 2011-06-20 in the simulacrum): desk closes with the Mt. Gox reason; reopens 2011-06-26 at carry-forward price.
- [ ] Trace CSV rows for every swap and reserve change; number locale correct (InvariantCulture) in UI and CSV.

---

## 9. Open questions — round 1 RESOLVED (2026-07-06)

All ten round-1 OQs were answered by the user and are locked as **D-SW.1 … D-SW.10** in §0.5. Notes beyond the table:

- **OQ-SW.7** resolved as "keep as a documented testing alternative, not a priority" — SW.1's verification step decides whether it ever gets built.
- **OQ-SW.8** resolved as "defer to the pre-release pass" — no gate work in SW.\*.
- **OQ-SW.9** resolved with a refinement rather than a plain yes: the spread knob is not deleted but **adapted** — it becomes the swap-fee control in `CasinoGamblingFinances` (default 10%, range 1–10%, both directions). The DEV control placement was then **amended in round 2 (same day)**: fee + SC reserve → `CasinoGamblingFinances`; **BTC reserve → `CasinoFinances`** (the casino's BTC wallet scene — a wallet-level property belongs with the wallet); `CasinoCoinSwaps` carries no DEV controls at all. §§2.2, 3.1, 6, 7-SW.2, 8 updated accordingly.
- **Round 2 (2026-07-06)** also confirmed the §0.5 interpretation note on D-SW.9: the BTC reserve percent is of the casino's **whole BTC wallet** (base + change addresses), not the literal base address.

---

## 10. Naming

| Thing | Name |
|---|---|
| Scene | `Screens/CasinoCoinSwaps/CasinoCoinSwaps.tscn` + `.cs`, `SceneId.CasinoCoinSwaps` (supersedes D-13.6's `BtcSwap` placeholder) |
| Service | `CasinoCoinSwapService` (autoload #15, `Scripts/Services/CasinoCoinSwapService.cs`) |
| State file | `user://casino_coin_swap_state.json` |
| Trace | `user://logs/swap_desk_trace.csv` |
| Records | `SwapRecord`, `ReserveSetting`, `SwapQuote` |
| Ledger kinds (D-SW.4) | `"swap_sc_out"` / `"swap_sc_in"` |

---

## 11. Deferred (documented so nothing is lost)

- **Bot swap participation** — API is clientId-ready (§5); wiring bots (their own SC↔BTC decisions) is a future step, after ranks/referrals framing.
- **Automatic-reserve-scheduler** (§2.4) — after testing produces trace data; shares the §2.2 setter API.
- **Rank-based fee tiers** — `GetSwapFeePercentFor` is the single hook; rank design is its own future round.
- **Era-aware network fees** (§3.4) — the real-history fee study; 0.1 stays hardcoded until then.
- **R3 drawdown floor** (§2.3) — only if R2 proves too coarse.
- **Per-day liquidity caps from `GetGameVolumeBtc`** (D-13.8, step13 §9.2) — would layer onto `OfferedBtc`/`OfferedSc` as an additional `min()`; revisit after SW.\*.

---

*Round 1 locked (D-SW.1…D-SW.10, §0.5). Next: SW.0 (`CasinoCoinSwapService` skeleton) begins on this branch.*
