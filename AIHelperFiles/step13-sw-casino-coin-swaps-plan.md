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
| **D-SW.1** | ⚠️ **SUPERSEDED by D-SW.11 (2026-07-08)** — kept for history. Fee allocation exactly as the §3.3 (v1) worked examples: Panel A margin retained in BTC (deliver less BTC, keep the full SC), Panel B player debited exactly B total with the 0.1 network fee inside it, margin retained in SC; floor `max(fee%, 0.1 BTC)` + 1-BTC-gross minimum swap size surfaced in the UI. |
| **D-SW.2** | Player side: swaps touch the player's **Main Balance only** (never the Bankroll). |
| **D-SW.3** | Casino side: swaps touch the **casino's Main Balance only** (the Bankroll stays `ApplyBetResult`'s bet float). Both parties' swap legs are Main↔Main. |
| **D-SW.4** | Ledger: two new `LedgerEntry.Kind`s — `"swap_sc_out"` / `"swap_sc_in"` — excluded from deposited/withdrawn totals AND from the since-last-deposit baseline. GLOSSARY entries in SW.6. |
| **D-SW.5** | Swap SC flows do **not** touch the betting-stats scopes (`PlayerFinancialStatsCalculator` stays bet/deposit/recharge-driven). |
| **D-SW.6** | Bought BTC is delivered to the player's **base address**; no fresh-address-per-swap (address non-reuse stays Satoshi-only, Step 8). |
| **D-SW.7** | `[DEV] Seed casino BTC` button: **kept in the plan as a testing-convenience alternative, NOT a priority** — build it only if SW.1 finds natural pool-mining accrual too slow for iteration. |
| **D-SW.8** | Gating DEV components out of public builds: **deferred to the pre-release pass**. v1 ships them inline, tagged `[DEV]`. |
| **D-SW.9** | **All swap-desk DEV knobs live in the casino's existing DEV scenes; `CasinoCoinSwaps` itself carries NO DEV controls** *(amended 2026-07-06 — the BTC selector was briefly slated for `CasinoCoinSwaps`, then moved)*. The old §4.3 spread panel adapts into the swap-fee control in **`CasinoGamblingFinances`**: one percent, default **10%**, clamped to **1% (min) – 10% (max)**, governing BOTH swap directions. Beside it, `CasinoGamblingFinances` also hosts the casino's **SC swap-reserve selector** (on its Main Balance, default 0) with the %/amount toggleable mode. The **BTC reserve selector lives in `CasinoFinances`** — literally the casino's BTC wallet scene, the natural home — in a new panel there (toggleable fixed-amount or % of the casino's BTC wallet total, default 0). All of these are DEV controls, needed for testing, to be superseded by a future **auto-swaps-scheduler** fed by the data gathered during testing (§2.4). |
| **D-SW.10** | Swap history surfaces **only inside `CasinoCoinSwaps`** for now (ledger entries from D-SW.4 still appear in the `ClientsTransactions` DEV scene as a side effect). |
| **D-SW.11** | **(2026-07-08) SUPERSEDES D-SW.1 — additive fee model.** The casino's percentage fee and the flat 0.1 BTC network fee are `totalFee(base) = NetworkFeePolicy.MinFee×(1+fee) + fee×base` — **summed, never `max()`'d** (D-SW.1's inclusive model made the casino's *real* margin collapse toward 0% near the minimum swap size, which dev testing found counter-intuitive: "en 10% debería sumar 0.2 al menos"). Linear, not piecewise (`CasinoCoinSwapService.BaseFromNet` is the single exact inverse used everywhere, replacing `MaxGrossForNet`). The minimum swap size was recalibrated **twice**, same day: first to the smallest base whose net delivery is exactly one satoshi (`BaseFromNet(OneSatoshi, fee)`, ≈0.1222 BTC at 10%) — then, per Follow-up 10 below, redefined again to a **VALUE floor** (`net(base) ≥ totalFee(base)`, ≈0.275 BTC at 10%) once dev testing found the "net>0" floor let a swap through paying almost 100% in fees for a handful of satoshi. See §3.1a for the rule + recomputed worked examples, and `Documentation/ProjectDesignManual.md` Ch. 34 §34.4 for the full effective-margin analysis. |
| **D-SW.12** | **(2026-07-08) Max fee deviation cap — the casino's own margin, never the network cost.** Dev feedback: the additive model's effective margin (the casino's cut, excluding the flat network fee) can run considerably above the nominal `SwapFeePercent` on swaps near the minimum size (e.g. ~13.6% at the §3.2 minimum when nominal is 10%) — the dev wants a dev-configurable ceiling on how far it may stray. New knob **`MaxFeeDeviationPoints`** (default `2.0`, dev-clamped `0–20` points, `CasinoGamblingFinances`): the CASINO'S OWN cut is clamped to `[0, (fee+maxDeviationFraction)×gross]` — `casinoFee = max(0, min(fee×(gross+MinFee), (fee+maxDeviationFraction)×gross))` — and the flat network fee is always charged in FULL on top, unconditionally (`totalFee = networkFee + casinoFee`). Deliberately capping the CASINO'S CUT rather than the combined total: capping the total instead would force an unavoidable conflict between "never charge less than the real network cost" and "never charge more than nominal+points%" for any swap small enough that the network fee alone already exceeds that percentage of the base (this happens well inside the legal range — e.g. 0.5 BTC at 10%/+2pts) — capping only the casino's own cut has no such conflict, since a `[0, ceiling]` clamp with `ceiling ≥ 0` can never be unsatisfiable. See §3.1b for the worked crossover math and `Documentation/ProjectDesignManual.md` Ch. 34 §34.4 (rewritten) for the full analysis. |

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

## 3. Fee model — 10% flat + network fee (additive), rank-ready

> **⚠️ SUPERSEDED (2026-07-08, D-SW.11) — see §3.1a.** This section originally locked an INCLUSIVE model (`max(fee%×gross, NetworkFeePolicy.MinFee)`, D-SW.1): the casino's percentage fee absorbed the network cost rather than adding to it. Playtesting found this counter-intuitive — near the minimum swap size, the casino's *real* margin was far below the nominal %, since the flat network floor ate most of the "fee." **D-SW.11 replaces this with an ADDITIVE model**: the network fee and the casino's percentage cut are both charged, summed. §3.1/§3.2/§3.3 below are kept as historical record of the original (now superseded) design; §3.1a has the current rule + recomputed worked examples.

### 3.1 The rule (v1, SUPERSEDED by D-SW.11 — see §3.1a)

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

### 3.2 Fee floor (v1, SUPERSEDED by D-SW.11 — see §3.1a)

If 10% of the gross BTC value is smaller than the 0.1 BTC network fee, the casino would swap at a loss. Floor:

```
feeBtcEquivalent = max(SwapFeePercent% × grossBtc, NetworkFeePolicy.MinFee /* 0.1 BTC */)
```

Equivalently the UI enforces a **minimum swap size of 1 BTC gross** (at 10%, fee ≥ 0.1 exactly at 1 BTC) — proposal: implement the floor in the math AND surface the min-size clamp in the UI, so tiny inputs get an honest "minimum swap is X" message instead of a silently worse rate (confirmed, D-SW.1).

### 3.3 Worked examples (v1, SUPERSEDED by D-SW.11 — see §3.1a; landing-day price 0.0679 SC/BTC)

**Panel A — player buys BTC with 100 SC:**
- Gross BTC = 100 / 0.0679 = 1,472.75 BTC
- Fee 10% → net delivered = 1,325.48 BTC (on-chain, casino → player base address)
- Casino: Main Balance +100 SC; BTC out = 1,325.48 + 0.1 network fee; margin retained = 147.28 − 0.1 = 147.18 BTC
- Player: Main Balance −100 SC; +1,325.48 BTC once the tx confirms at the next block

**Panel B — player sells 1,000 BTC:**
- Gross value = 1,000 × 0.0679 = 67.90 SC; fee 10% = 6.79 SC → **player credited 61.11 SC** (instant, into Main Balance)
- On-chain leg: player broadcasts 999.9 BTC to the casino's address + 0.1 network fee = exactly 1,000 BTC debited from the player. The 0.1 comes out of the *swap*, not on top — the casino absorbs it from its 6.79 SC margin (≈ 0.007 SC at this price)
- Casino: Main Balance −61.11 SC; +999.9 BTC once confirmed

*(These allocations — who is debited what, in which asset the margin lands — were **confirmed with these exact numbers under the v1 inclusive model** (D-SW.1); see §3.1a for the current additive-model numbers.)*

### 3.1a — CURRENT rule (D-SW.11, additive model, 2026-07-08)

> **Swap fee = 10% of the gross swap value, PLUS the flat 0.1 BTC network fee — the two are ADDED, never `max()`'d.** The network fee is charged as its own separate line item on top of the percentage, in both panels.

```
totalFee(base) = NetworkFeePolicy.MinFee × (1 + fee) + fee × base     // additive — replaces D-SW.1's max()
net(base)      = base − totalFee(base)
               = base × (1 − fee) − NetworkFeePolicy.MinFee × (1 + fee)
```

where `base` = the pre-fee currency-converted amount (`grossBtc` in Panel A, the player's total BTC send `B` in Panel B). This is **linear** — no more piecewise floor-region/percentage-region split (there is no more `max()` to create a kink in the curve), which simplified the implementation: `CasinoCoinSwapService.BaseFromNet(targetNet, fee)` is now the single exact inverse used everywhere (the Max-clamp math, both reverse "receive X" quotes, and the minimum-swap-size derivation), replacing the old piecewise `MaxGrossForNet`.

**Minimum swap size** (D-SW.11, **revised same day** — see Follow-up 10): the old floor existed to prevent the casino from *losing* money; under the additive model the casino's cut is `fee × (base + MinFee)`, always **positive** for any `base ≥ 0` — the casino can never lose money at any swap size, so that reason for a floor is gone. The FIRST replacement floor (`net > 0`, "pure mathematical minimum") turned out to be economically absurd in dev testing — it let a swap through paying almost 100% of its value in fees to net back a handful of satoshi. The floor was redefined again, same day, to a **VALUE floor**: the player must net back AT LEAST as much as they pay in total fees, i.e. `net(base) ≥ totalFee(base)`, equivalently `net ≥ base/2` (since `base = net + totalFee`, `net ≥ totalFee ⟺ 2×net ≥ base`). Solving `net(base) = totalFee(base)` for `base`:
```
base = 2×totalFee(base) = 2×[MinFee×(1+fee) + fee×base]
base×(1 − 2×fee) = 2×MinFee×(1+fee)
base = 2×MinFee×(1+fee) / (1 − 2×fee)                    // MinSwapGrossBtcFor(fee)
```
At the 10% default this is **≈0.275 BTC** (up from the "net>0" floor's ≈0.1222 BTC, still well down from the original D-SW.1 flat 1.0 BTC). Unlike the "net>0" floor, this one is genuinely **fee-dependent** — `MinDeliverableBtc`/`MinScPayoutAt` (the panels' enable thresholds) no longer collapse to fee-independent constants; they read the live fee%: `MinDeliverableBtc(fee) = MinFee×(2−fee)/(1−2×fee)` (≈0.2375 BTC at 10%), `MinScPayoutAt(priceSc, fee) = priceSc × MinFee×(1+fee)/(1−2×fee)`.

**Worked examples, recomputed (same landing-day price 0.0679 SC/BTC, same 10% nominal fee, additive model):**

**Panel A — player buys BTC with 100 SC** (precise values, verified by direct calculation):
- Gross BTC = 100 / 0.0679 = 1,472.75 BTC (unchanged — the currency conversion itself doesn't move)
- Total fee = `NetworkFeePolicy.MinFee×(1+fee) + fee×gross` = `0.1×1.10 + 0.10×1,472.75` = `0.11 + 147.275` = **147.385 BTC** — displayed as network **0.1** + casino **147.285** (bigger than the old model's 147.18, since the network cost is no longer carved out of the 10%)
- Net delivered = 1,472.75 − 147.385 = **1,325.365 BTC** (slightly less than the old 1,325.48, since the total fee is now bigger)
- Casino: Main Balance +100 SC; BTC out = 1,325.365 + 0.1 network fee = 1,325.465; margin retained = 147.385 − 0.1 = **147.285 BTC**
- Effective casino margin on this swap: 147.285 / 1,472.75 ≈ **10.0007%** (barely above the nominal 10% — this swap is large relative to the ~0.275 BTC minimum, so the flat network fee barely moves the effective rate; see §34.4's note on smaller swaps, where the effective rate runs noticeably ABOVE nominal, e.g. 11% at gross = 1.0 BTC)

**Panel B — player sells 1,000 BTC** (precise values, verified by direct calculation):
- Gross value = 1,000 × 0.0679 = 67.90 SC
- Network fee's SC-equivalent = `NetworkFeePolicy.MinFee × price` = `0.1 × 0.0679` = **0.00679 SC** (the displayed network portion — flat, no `×(1+fee)` factor, same convention as Panel A)
- Total fee = `0.00679×(1+fee) + fee×gross` = `0.1 × 0.0679 × 1.10 + 0.10 × 67.90` = `0.007469 + 6.79` = **6.797469 SC** — displayed as network **0.00679** + casino **6.790679** (`6.797469 − 0.00679`)
- Player credited (instant, Main Balance): 67.90 − 6.797469 = **61.102531 SC** (slightly less than the old 61.11)
- On-chain leg unchanged: player broadcasts 999.9 BTC to the casino's address + 0.1 network fee = exactly 1,000 BTC debited from the player (D-SW.6's delivery mechanics don't change — only the SC-side fee CALCULATION changed)
- Casino: Main Balance −61.102531 SC; +999.9 BTC once confirmed
- Effective casino margin: 6.790679 / 67.90 ≈ **10.001%**

*(D-SW.11 supersedes D-SW.1's exact numbers above; these are the current canonical worked examples. Full rationale + the effective-margin-vs-swap-size analysis: `Documentation/ProjectDesignManual.md` Ch. 34 §34.4.)*

### 3.1b — Max fee deviation cap (D-SW.12)

The additive model's effective margin (`casinoFee/gross`, the casino's cut alone — excludes the flat network fee by definition) is `fee×(1+MinFee/gross)`, always **above** nominal, more so the smaller the swap. At the §3.2 minimum (0.275 BTC, 10% nominal) this is ~13.6% — enough of a stretch above nominal that the dev asked for a configurable ceiling.

**The cap applies to the casino's OWN cut, never to the combined total.** Given `maxDeviationFraction = MaxFeeDeviationPoints/100`:

```
casinoFeeUncapped = fee × (gross + MinFee)                          // the uncapped additive cut (§3.1a)
casinoFeeCeiling  = (fee + maxDeviationFraction) × gross             // never let the cut exceed nominal+points
casinoFee         = max(0, min(casinoFeeUncapped, casinoFeeCeiling)) // clamp — floor and ceiling can NEVER conflict
totalFee          = networkFee + casinoFee                          // network cost is a flat pass-through, always full
```

**Why the cap targets the casino's cut and not the total.** An earlier draft of this fix capped `totalFee` directly (with `totalFee ≥ MinFee` as the floor, `totalFee ≤ (fee+maxDeviationFraction)×gross` as the ceiling) — but for any `gross` small enough that `MinFee` alone already exceeds `(fee+maxDeviationFraction)×gross` (e.g. below ~0.5 BTC at 10%/+2pts), those two constraints are **mutually impossible**: enforcing the floor necessarily breaks the ceiling. Verified this numerically (PowerShell) before committing to the corrected design — at gross=0.5 BTC the total-capped draft produced an effective **total** cost of 20% (worse than the uncapped model's own worst case), while the corrected (casino-cut-only) design holds the casino's margin at exactly the intended 12% ceiling with no conflict, since the floor (`casinoFee ≥ 0`) and the ceiling (`casinoFee ≤ ceiling ≥ 0`) never contradict each other.

**Worked example (10% nominal, +2 points, at several gross sizes):**

| Gross (BTC) | Uncapped casino cut | Capped casino cut | Effective margin % | Total fee |
|---|---|---|---|---|
| 0.275 (min) | 0.1375 | 0.033 | **12.00%** (capped) | 0.133 |
| 0.5 (crossover) | 0.06 | 0.06 | **12.00%** (boundary) | 0.16 |
| 1.0 | 0.11 | 0.12 → uncapped wins | 11.00% | 0.21 |
| 5.5 | 0.56 | 0.66 → uncapped wins | 10.18% | 0.66 |
| 1,472.75 (§3.1a example) | 147.285 | 176.73 → uncapped wins | 10.0007% | 147.385 |

The crossover (`casinoFeeUncapped = casinoFeeCeiling`) solves to `gross = fee×MinFee/maxDeviationFraction` — **0.5 BTC** at the 10%/2pt defaults. Below it, the cap binds and effective margin is a flat `SwapFeePercent + MaxFeeDeviationPoints`; above it, the uncapped additive formula governs and margin decreases toward nominal as gross grows, exactly as in §3.1a.

**Interaction with the §3.2 minimum swap size.** `MinSwapGrossBtcFor` (the value floor, `net(gross) ≥ totalFee(gross)`) was derived from the UNCAPPED additive formula. Since the cap only ever REDUCES `casinoFee` relative to the uncapped value (never increases it), `totalFee` under the cap is ≤ the uncapped `totalFee` at the same gross — so `net` is ≥ the uncapped `net` — so the value-floor inequality still holds (with MORE slack than before) at the previously-derived minimum. The minimum was NOT re-derived for the capped case (that would need its own piecewise solve); the existing ≈0.275 BTC figure remains a safe, if slightly conservative, floor under the cap too.

**Guarantee: the casino never breaks even or loses on a legal swap, at any input the UI can produce (minimum, maximum, or Max-clamped).** `casinoFee = max(0, min(casinoFeeRaw, casinoFeeCap))` where `casinoFeeRaw = fee×(gross+MinFee)` and `casinoFeeCap = (fee+maxDeviationFraction)×gross`. Both inner terms are **strictly positive** for any `gross > 0` and `fee > 0` — and `fee` (`SwapFeePercent/100`) can never be 0 today, since `SwapFeePercent` is hard-clamped to `[1%, 10%]` (D-SW.9). So `min(casinoFeeRaw, casinoFeeCap) > 0` always, meaning the `max(0, …)` floor is never actually the binding term under any reachable setting — `casinoFee` is strictly positive, not merely non-negative, for every legal swap. This holds regardless of `MaxFeeDeviationPoints` (even at its minimum, `0`): `casinoFeeCap = fee×gross > 0` still. Truncation (`Money.Normalize`, 8 decimals) cannot zero this out either, because the enforced minimum swap size (~0.2–0.28 BTC, dominated by the fixed `MinFee=0.1`) keeps `gross` many orders of magnitude above the 1-satoshi (`0.00000001`) truncation floor — `casinoFeeCap` at the smallest legal swap and lowest fee (1%) is still ≈0.002 BTC, far from rounding to zero. Max-clamping only ever moves a swap to a LARGER `gross` (never below the minimum, per the Follow-up 11 floor), and both `casinoFeeRaw`/`casinoFeeCap` are monotonically increasing in `gross`, so clamping can never reduce margin below what it already is at the minimum.

**Where this guarantee would stop holding (none reachable via the current UI/execution path, listed for future awareness):**
- **`SwapFeePercent = 0%`** — not possible today (`MinSwapFeePercent = 1`), but if a future rank system's per-client override (`GetSwapFeePercentFor(clientId)`, the one documented extension hook) ever allowed a VIP client a 0% rate, `casinoFeeRaw = casinoFeeCap = 0` and the casino would break even exactly (not lose — `totalFee` still covers `networkFee` in full — but earn nothing).
- **`NetworkFeePolicy.MinFee` changing significantly** (the pending §3.4 research below) — the algebra above still holds for ANY positive `MinFee` (the guarantee doesn't depend on its specific value), but the ENFORCED MINIMUM SWAP SIZE scales with `MinFee` (§3.2's formula), so a much smaller future `MinFee` would shrink the minimum swap size too, and it would need re-verification that the smaller resulting `gross` still clears the 8-decimal truncation floor comfortably (almost certainly still true at any realistic `MinFee`, but worth a numeric re-check whenever that value changes, following this session's established discipline of never trusting algebra across `Money.Normalize` without verifying).
- **A direct/bypassing execution call** — not reachable from the UI (`TryExecuteScToBtc`/`TryExecuteBtcToSc` both `Math.Clamp` the input into `[MinInput, MaxInput]` before computing the real quote, so even a maliciously tiny or negative input gets clamped up to the safe minimum first).

### 3.4 Network fee: hardcoded today, needs a real model — research priority (see `Documentation/PRIVATE_ROADMAP.md` §5 "Network Fee Market Simulation")

The 0.1 BTC network fee is **fully hardcoded** (`NetworkFeePolicy.MinFee`, a single flat constant since the `2009-04-26` activation date) and historically naive: real Bitcoin fees were near-zero for years, then rose through several distinct regimes (priority/coin-age free relay, the 0.01 BTC default-fee era, fee-per-kB rules, and eventually full fee-market auctions during congestion events). Every system built on top of this constant — the swap desk's whole fee model (D-SW.11/D-SW.12 above), the minimum swap size (§3.2), `MinDeliverableBtc`/`MinScPayoutAt`, every BTC wallet send panel's fee field — inherits its historical naivety and its "one number forever" fragility: nothing in the current design would need to change CODE-wise if `NetworkFeePolicy.MinFee` became a function of game-time/network-state instead of a constant (every formula in this plan already treats it as "the current network fee," read fresh per quote), but the VALUE itself never moves, which is the actual gap.

**This needs a dedicated research round before deciding how to model it** — logged here so it isn't lost, and tracked as a priority research item in `Documentation/PRIVATE_ROADMAP.md` §5 ("Network Fee Market Simulation research"). Two candidate approaches, not mutually exclusive:

- **Option A — historical fee replay.** Model the network fee the same way `BtcMarketDataService` already models the BTC/USD price: source/curate a historical dataset of real average Bitcoin transaction fees across the simulated years and replay it as a step function keyed to game-time, mirroring the existing price-history architecture almost exactly (same "day-change event" pattern). **Pro**: historically faithful, reuses a proven architecture, straightforward to reason about and to calibrate against a citable real-world source. **Con**: real historical fee data is far less standardized/available than price data (especially pre-2012, where fees were often literally zero or negligible, and metrics like "average fee" vary by source/methodology); it is a **pure replay**, disconnected from anything happening inside OUR simulated chain — a player's own transaction volume, or the simulated bot population's growth, has zero influence on the fee they pay, which can feel arbitrary ("why does my fee change when nothing in MY world changed?").
- **Option B — reactive fee market from the simulation's own state.** Derive the network fee dynamically from OUR simulated blockchain's own congestion signals: mempool depth (pending tx count vs. the `24 transactions` per-block cap), the growing miner/bot population (already gradually introduced per PH/P3), and total transaction volume — a basic fee-per-byte auction model, similar in spirit to real Bitcoin's actual fee market mechanics. **Pro, and the deciding one**: the miner population and transaction volume are **already planned to grow following real Bitcoin history** (gradual bot introduction Post-PH, the referral system, mining pools scaling over time) — so a reactive fee model keyed to OUR simulation's own congestion would **automatically and indirectly track real historical fee trends** as a side effect of infrastructure that is already planned/built, with zero separate historical-fee dataset to source or maintain. It also gives the player genuine agency (their own transaction/betting volume can influence the fee market they experience) and slots directly into the placeholder P4 already left for it (`AIHelperFiles/candidate-block-model-plan.md` / "Block Template Builder": *"Keep room for future private mempool/fee-market behavior"*, and the ancestor-feerate tx-ordering model already built there). **Con**: needs real design work that doesn't exist yet (a congestion→fee formula, its elasticity/tuning, an actual fee-market auction inside the mempool/candidate-block engine) — more moving parts than a lookup table, and harder to validate against a real-world citation (though for an emergent-economics goal, that may be a feature rather than a bug).

**Recommended framing for the research round (not a decision — to be made when this round is actually run):** Option B appears the better structural fit given the "gradual, historically-paced population growth" design already committed to elsewhere in this project (PH/P3/P5) — it would inherit historical shape for free instead of requiring a second, independently-sourced historical dataset alongside `BtcMarketDataService`'s price history. Option A remains valuable as a **calibration/validation reference** for Option B (i.e., tune B's auction parameters so its emergent fee curve roughly tracks A's real historical shape) rather than as a replacement replay mechanism — a possible hybrid worth evaluating explicitly in that round. **Do NOT block SW.\* or any current work on this** — it is a dedicated future research/design pass.

---

## 4. Swap execution pipelines

*(§4.1/§4.2's quote formulas below are updated for D-SW.11's additive model, 2026-07-08 — the clamp/execute STRUCTURE is unchanged from v1, only the `fee`/`feeSc` line and the minimum swap size moved.)*

### 4.1 Panel A — SC → BTC (casino sells)

```
Input: S (SC, one field; live quote readout below it)
Quote: grossBtc = S / price;  fee = 0.1×(1+fee%) + fee%×grossBtc;  netBtc = grossBtc − fee   [D-SW.11, additive]
Clamps (live "Max" label + input hard-clamp):
  S ≤ player Main Balance                                  (player-side limit)
  netBtc + 0.1 ≤ OfferedBtc                                (casino-side limit)
    ⇒ S_max = min(playerMain, BaseFromNet(OfferedBtc − 0.1, fee%) × price)
  S ≥ min swap size (§3.1a — ≈0.275 BTC gross at 10%, the VALUE floor, NOT the old flat 1 BTC nor the brief "net>0" ≈0.1222 BTC)
Execute:
  1. Player Main Balance −S            (PrincipalBalanceService, instant)
  2. Casino Main Balance +S            (CasinoScBalanceService, instant — D-SW.3)
  3. Casino → player on-chain send of netBtc, fee 0.1       (existing NetworkRoot
     CreateAndBroadcastTransactionToAddress("casino", …) / BuildAndBroadcastUtxoSpend path —
     multi-input coin selection + change rotation already work)
  4. Ledger entry (D-SW.4) + SwapRecord + trace row
```

`MinDeliverableBtc` (Panel A's enable threshold, §1.1) — under the value floor this is **fee-DEPENDENT** (Follow-up 10): `MinFee×(2−fee)/(1−2×fee) ≈ 0.2375 BTC` at the 10% default (the minimum swap's net delivery is `minGross/2`, not a fixed `OneSatoshi`, so it moves with the live fee%).

### 4.2 Panel B — BTC → SC (casino buys)

```
Input: B (BTC — the TOTAL the player will part with, network fee included)
Quote: grossSc = B × price;  feeSc = 0.1×price×(1+fee%) + fee%×grossSc;  netSc = grossSc − feeSc   [D-SW.11, additive]
Clamps:
  B ≤ player spendable BTC (confirmed UTXOs − pending outgoing)
  netSc ≤ OfferedSc        ⇒ B_max = min(playerSpendable, BaseFromNet(OfferedSc / price, fee%))
  B ≥ min swap size (§3.1a — ≈0.275 BTC at 10%, the VALUE floor, NOT the old flat 1 BTC nor the brief "net>0" ≈0.1222 BTC)
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

### SW.3 — Panel A execution (SC → BTC) — ✅ implemented (2026-07-07, pending in-editor verification)
- [x] `TryExecuteScToBtc` full pipeline (§4.1): re-gates on fresh availability, hard-clamps the input to the binding max (§4.3) and enforces the 1-BTC-gross minimum (§3.2), then: player Main −S (`PrincipalBalanceService.TryWithdraw`) → casino Main +S (new `CasinoScBalanceService.ReceiveSwapSc`/`TryPaySwapSc`, D-SW.3) → on-chain send casino → client node's **base address** via `CreateAndBroadcastTransaction(CasinoNodeId, clientId, netBtc, 0.1)` (D-SW.6; the 0.1 network fee is paid by the casino out of its margin, D-SW.1). A failed broadcast unwinds both SC legs — no partial swap. Ledger entry kind **`swap_sc_out`** (new `RegisterSwapScOut`/`RegisterSwapScIn` on `CasinoClientLedgerService`, D-SW.4 — excluded from deposited/withdrawn totals and the since-last-deposit baseline by construction) + `SwapRecord` + trace row + availability re-gate + `SwapDeskChanged` (via `RegisterSwap`). Pending display: in-memory `PendingBtcDeliveries` (deliberately NOT persisted — a restart unwinds both legs, §4.4) pruned on each recompute via new `NetworkRoot.IsTransactionPending`; Panel A shows the ⏳ row with the restart-honesty copy, and the SWAP button is live (Panel B's stays hard-disabled until SW.4). `clientId`-ready end to end (bots can call the same API later). `dotnet build` clean.

**SW.3 addendum (playtest follow-up, 2026-07-07) — tx-history legibility.** The tester read two "sent 34.50 BTC → player" lines as duplicate swaps; they were **pool distributions** (one multi-output tx per pool block: 35 pool − 5×0.1 payout fees = 34.50 total, and `GetNodeTransactionHistory` labeled the whole send with only the FIRST external payee — the player). Fixes: (1) `GetNodeTransactionHistory` now returns `recipients` (distinct external payees) + `memo` (`Transaction.InputDataText`), and all three wallet history panels (CasinoFinances / BTCWallet / FoundersWallets) render multi-output sends as "sent X to addr **(+N more)**"; (2) swap-desk txs carry an on-chain display memo (`swap: casino desk SC→BTC` / `…BTC→SC` via a new optional `memo` param on `CreateAndBroadcastTransaction` → `BuildAndBroadcastUtxoSpend`; `InputDataText` is excluded from the content-hash txid and the sighash, so tagging post-signing is validation-safe) and render **aqua + "· SWAP"** in those panels. Side discovery while verifying: `CommitBlock` assigns `Index = Chain.Count + 1`, i.e. **list position = Index − 1 by construction** — confirming the v4 offset fix is the permanent correct form, not a world quirk.

### SW.4 — Panel B execution (BTC → SC) — ✅ implemented (2026-07-07, pending in-editor verification)
- [x] `TryExecuteBtcToSc` full pipeline (§4.2), same standards as SW.3 — but legs run in the OPPOSITE order: the on-chain send is the CLIENT's own broadcast (client → casino base address of `B − 0.1` + 0.1 network fee, via `CreateAndBroadcastTransaction(clientId, CasinoNodeId, …)`, tagged with the `SwapTxMemoBtcToSc` display memo), so it goes FIRST — a failed broadcast then needs no rollback since nothing has moved yet. The SC leg (`CasinoScBalanceService.TryPaySwapSc` + `PrincipalBalanceService.Deposit`) fires INSTANTLY after, without waiting for confirmation (§4.4 — a restart before the block reverts the mempool send and the SC credit together, so no real risk). Includes the player-side spendable-BTC clamp (`PlayerSpendableBtc` = confirmed − pending, from SW.2) and the casino-side `OfferedSc` clamp, both hard-clamped to the quote's `MaxInput`; the §3.2 1-BTC minimum enforced. Ledger entry kind **`swap_sc_in`** (already added in SW.3) + `SwapRecord` + trace + re-gate.
- [x] Scene: Panel B's SWAP button now live (enabled with the panel); pending-row logic extended to both directions — Panel A shows "BTC incoming", Panel B shows "BTC sent … (a restart … incl. your SC credit)" for restart-honesty (§4.4). `dotnet build` clean.

**SW.3/SW.4 addendum (playtest follow-up, 2026-07-07) — clamp symmetry.** Both `TryExecuteScToBtc`/`TryExecuteBtcToSc` only clamped DOWN to the max (an over-max input executed at the max — confirmed working); an under-min *positive* input was rejected outright instead of clamping UP to the minimum and executing, breaking the symmetry the user expected from the max side. Fixed: both methods now `Math.Clamp(amount, probe.MinInput, probe.MaxInput)` (guarding the pathological case `MaxInput < MinInput` — no legal swap exists right now — with its own clear error). A positive amount below the minimum now silently swaps at the minimum, exactly mirroring the above-max behavior.

### SW.5 — SC equilibrium auto-floor (the pending calculation task, §2.3) — ✅ implemented (2026-07-07, pending in-editor verification)
- [x] Implemented R2 (recharge-pace floor) behind a DEV toggle: `CasinoCoinSwapService.ScAutoFloor = SafetyFactor × dosesConsumedInWindow(WindowHours, game time) × BankrollTarget`, reading `CasinoScBalanceService.RechargeHistory` (`Reason == "auto"`, already checkpoint-covered, zero new state). Superseded the R1 static-multiple placeholder from SW.0 (`ScFloorMultiplier` renamed/replaced by `ScAutoFloorSafetyFactor` default **1.5** + `ScAutoFloorWindowHours` default **24** in-game hours; DTOs/persistence/pre-genesis reset all updated — System.Text.Json's default unknown-property tolerance means no explicit migration code was needed for the renamed field). `EffectiveScReserve = max(manual reserve, auto floor)` — same composition as `TryAutoWithdraw`'s anti-ping-pong guard. DEV toggle ("Auto floor (R2, recharge pace)" + Safety × / Window-hours SpinBoxes + Apply) added to `CasinoGamblingFinances` directly beside the SC reserve selector (D-SW.9); the info line now also shows the live auto-floor value.
- [x] **Documentation (user request, 2026-07-07)**: `Documentation/ProjectDesignManual.md` Chapter 33 — a detailed, human-readable writeup of both R2 (as implemented: the formula, the worked example, why it was the natural first choice, its known coarseness) and R3 (drawdown-based, as a fully-explained but NOT-yet-planned alternative: the formula, a worked example, the new telemetry it would require, a comparison table, and explicit "when to actually build R3" criteria) — written so a future implementation plan for R3 can start directly from "why" without re-deriving the reasoning. `dotnet build` clean.

### SW.6 — polish + docs truth pass — ✅ implemented (2026-07-07)
- [x] Min-swap-size messaging (already shipped in SW.2's live quote preview — "Min: X" + inline "✖ minimum swap is X" — and SW.3/SW.4's execute-time error), post-2025 freeze label (D-13.5, already shipped in SW.2 — "Desk open (post-history era — price frozen)"), pending-restart honesty copy (already shipped in SW.3/SW.4's ⏳ rows). Verified all three were already in place before adding anything redundant. Trailing-blank-lines guard: `CasinoCoinSwaps` uses Ch. 29 Pattern A (`ScrollContainer` + plain `Label`/`PanelContainer` children), which does not carry Pattern B's `RichTextLabel` content-height bug the guard exists for — added a small 24px `BottomSpacer` control after the recent-swaps list anyway, in the same spirit, so the last swap row never sits flush against the scroll's bottom edge.
- [x] Docs: **CLAUDE.md** — autoload count corrected to fifteen (also surfaced the pre-existing `BtcMarketDataService`/#14 gap, left to the sibling plan's TL.3 as that plan's own docs task), new `CasinoCoinSwapService` subsection (availability/reserves/auto-floor/fee/quotes/execution/persistence, mirroring the other financial-service subsections' depth), File Organization tree (`Screens/CasinoCoinSwaps/`, service count 13→15), Canonical Decisions table (swap fee row), Implementation Status bullet, SceneManager section paragraph + Navigation Map (`CasinoCoinSwaps` under both MainMenu and ScFinances, origin-aware back). **GLOSSARY.md** — `Offered Balance`, `Strategic Reserve`, `Swap`, `Swap Fee` entries, alphabetically placed. **ProjectDesignManual.md Ch. 33** — written in SW.5 (R2 implemented + R3 documented alternative, per user request for human-readable detail). **Sibling plan** (`step13-btc-market-data-and-dev-alt-timeline-plan.md`)'s `Phase SW.*` line updated to `[x]` with a summary of what shipped and a pointer to this plan's phase log.
- [x] Feeds into TL.3 (exit the simulacrum) unchanged — nothing here depends on the alt timeline except test convenience. `dotnet build` clean (0 warnings, 0 errors) after every change in this phase.

---

**Round complete (2026-07-07).** SW.0 through SW.6 are all implemented and individually playtested/confirmed by the developer, with three bugs found and fixed along the way (the BTC pool-payout obligation/settling misclassification across two hardening passes, the chain list-position-≠-Index off-by-one, and the missing symmetric min-side clamp) — see each phase's addendum above for full detail. Remaining work is explicitly deferred (§11): bot swap participation, the auto-reserve-scheduler, rank-based fee tiers, era-aware network fees, and R3 (drawdown-based auto-floor, now fully documented in ProjectDesignManual Ch. 33 for whenever it's picked up).

**SW.5 post-round follow-up (2026-07-07) — R2 UI clarity, pre-commit.** After using the SC auto-floor selector, the dev flagged two genuine usability gaps (not bugs): (1) `SafetyFactor` alone is unreadable — the same value produces very different SC amounts depending on `dosesConsumed` (itself moved by `WindowHours` + play pace) and `BankrollTarget`, and the SpinBox's `max_value = 20.0` was an arbitrary UI choice with no real backing (the service itself has no upper clamp); (2) running the manual reserve and the R2 auto floor at the same time was confusing because the UI never showed which of the two `max(manual, auto)` actually binds. Discussed both against a documented R3 migration; user chose the cheaper, non-behavior-changing fix for both (confirmed via question, not assumed):
- New `CasinoCoinSwapService.ScAutoFloorDosesConsumed`/`GetScAutoFloorDosesConsumedFor(windowHours)` expose the intermediate doses-count so the UI can show the full multiplication, not just the final number.
- `CasinoGamblingFinances` gained a **live-preview breakdown label** ("Preview: 1.5 safety × 5 dose(s) consumed in last 24h × 100.00000000 SC (BankrollTarget) = 750.00000000 SC") that updates on every SpinBox/toggle change — BEFORE Apply — via the new parameterized doses lookup (so the Window spinner's un-applied value previews correctly too). The SafetyFactor SpinBox's arbitrary ceiling was raised from 20.0 to 1000.0 (still bounded, but no longer confusingly low) since the live breakdown is what actually informs the dev now, not the spinner's cap.
- The swap-desk info line now appends **`[auto floor binds]`** or **`[manual reserve binds]`** next to the effective reserve — the `max()` composition itself is UNCHANGED (still exactly `TryAutoWithdraw`'s anti-ping-pong shape), only made visible.
- R3 migration remains fully documented (Ch. 33) but explicitly not started — the dev's own read was that R2's coarseness (count-based, not SC-denominated) is real but the UI transparency fix resolves the immediate usability problem without new telemetry.

**Follow-up 2 (same session) — window unit changed from hours to days.** After using the breakdown preview, the dev asked to pick the R2 window in whole game-DAYS instead of raw hours ("de una vez" — a cleaner, more natural unit than typing large hour counts). Renamed throughout (not just relabeled): `ScAutoFloorWindowHours` → `ScAutoFloorWindowDays` (const default `DefaultScAutoFloorWindowHours = 24m` → `DefaultScAutoFloorWindowDays = 1m`, the same duration), `GetScAutoFloorDosesConsumedFor(windowHours)` → `(windowDays)` using `AddDays` instead of `AddHours`, and every persistence/checkpoint field, print line, and DEV-UI/doc reference updated to match (CLAUDE.md, ProjectDesignManual Ch. 33). Old saved `casino_coin_swap_state.json`/checkpoint files with the old field name fall back to the new default (1 day) via the existing `> 0m` fallback pattern — same acceptable no-migration precedent as the R1→R2 rename in SW.5. `CasinoGamblingFinances`'s Window SpinBox now reads `min=1, max=30, step=1, default=1` (whole days; the old 720-hour max was exactly 30 days, so the ceiling is unchanged, just re-denominated).

**Follow-up 3 (same session) — colored binding indicators.** Text alone (`[auto floor binds]`/`[manual reserve binds]` in the shared info line) still required reading; the dev asked for an at-a-glance dot. Added a `●` `Label` at the end of each row (`ManualReserveIndicator` on `ScReserveRow`, `AutoFloorIndicator` on `ScAutoFloorRow`), colored in `RefreshSwapDeskInfo()` (the same place that already computes `manualAbs`/`autoAbs`): green = this side is the effective reserve right now, red = the other side overrides it, and grey (auto only) = the toggle is off entirely, not a candidate. No new logic — purely a visual read of the existing `max(manual, auto)` composition.

**Follow-up 4 (same session) — REAL BUG: lowering the swap fee % didn't lower the effective fee.** Dev report: setting `SwapFeePercent` to 1% in `CasinoGamblingFinances` still showed ~10% being charged in both `CasinoCoinSwaps` panels. Root cause was NOT stale UI (the `SwapDeskChanged` event/re-quote path was already correct) — it was the §3.2 fee-floor math: `MinSwapGrossBtc` was a **hardcoded `const decimal = 1m`**, sized so that at the ORIGINAL 10% default, `10% × 1 BTC = 0.1 BTC` exactly matches the flat `NetworkFeePolicy.MinFee` network-fee floor (the crossover point where the percentage fee first exceeds the flat floor). Lowering `SwapFeePercent` to 1% without moving this constant meant `1% × 1 BTC = 0.01 BTC`, far below the 0.1 BTC floor — so `Math.Max(feeFraction × gross, NetworkFeePolicy.MinFee)` kept resolving to the flat 0.1 BTC floor for any swap anywhere near the advertised "1 BTC minimum," making the EFFECTIVE fee rate ~10% regardless of the nominal percent. Fixed: `MinSwapGrossBtc` (`const decimal = 1m`) → `MinSwapGrossBtcFor(decimal feeFraction) => NetworkFeePolicy.MinFee / feeFraction` (a private static method), recomputed at every call site (`MinDeliverableBtc`, `MinScPayoutAt`, both `Quote*` methods — each already had the correct per-call `fee`/`feeFraction` in scope, so no client-facing behavior changed except the number itself). At the 10% default this still resolves to exactly 1 BTC (no regression); at 1% it now correctly resolves to 10 BTC, so a 1 BTC swap at 1% fee genuinely costs ~1%, not ~10%. Also fixed a UI label in `CasinoCoinSwaps.cs` (Panel A's Min readout) that hardcoded the literal text "(1 BTC gross)" — now computed live as `probe.MinInput / probe.PriceUsed`.

**Follow-up 5 (same session) — reactive "amount to receive" input, both panels.** Dev request: add a second input per panel for the amount the player wants to RECEIVE, reactive with the existing "pay"/"send" input in both directions. Implementation:
- **Service**: two new reverse-quote methods, `QuoteScToBtcForReceivedBtc(clientId, desiredNetBtc)` (Panel A) and `QuoteBtcToScForReceivedSc(clientId, desiredNetSc)` (Panel B). Both invert the forward quote's net(gross) curve via the ALREADY-EXISTING `MaxGrossForNet` helper (built for the Max-clamp math, and — discovered while reusing it — mathematically exact, not just an upper bound: for a given fee it returns the one gross whose net equals the target precisely, in both the flat-floor and percentage regions), then replay the result through the ordinary forward `Quote*` method. This means the reverse quote can never disagree with the forward one and every clamp/`IsValid`/`MaxLimitedBy` rule is evaluated in exactly one place, not duplicated.
- **Scene**: new `PanelAReceiveInput` / `PanelBReceiveInput` `LineEdit`s (with labels, under each pay/send row). `ApplyPanelState` now disables/enables both inputs together.
- **Bidirectional sync, no reentrancy guard needed**: confirmed via the codebase's own existing `FillMax` (which already needed a manual `EmitSignal` to "replay" a MAX fill) that setting `LineEdit.Text` programmatically does NOT raise `TextChanged` in this Godot binding — so syncing the other field via a plain `.Text =` assignment is inert and cannot recurse.
- **Clobber-safety (caught before shipping, not by the user)**: naively always refreshing from the pay field on the periodic 2s timer / event ticks would overwrite the receive field while the user is mid-typing there. Fixed with a per-panel `_panelALastEditedReceive`/`_panelBLastEditedReceive` flag, set by whichever field's `TextChanged` last fired; `RefreshAll()` now recomputes from whichever field is the current SOURCE (never overwriting it) — the field the user is actively typing into is therefore never at risk from any refresh path, periodic or event-driven.
- Both reverse-path methods duplicate the disabled-state label reset (`"Max: —"`/`"Quote: —"`) rather than relying on the forward path having already run, since `RefreshAll()` may now call ONLY the reverse path for a given panel.

**Follow-up 6 (same session) — MAX/MIN for all four inputs + exact-match rounding fix + fee breakdown.** Two more requests after using the new receive inputs:
1. **MAX/MIN buttons on all four fields** (pay ×2, receive ×2 across both panels — 8 buttons total). Refactored the old single-purpose `FillMax` into two generic helpers, `FillPayExtreme`/`FillReceiveExtreme` (parameterized by `Func<string, decimal, SwapQuote>` so one pair covers both panels' forward quote methods) + a shared `FillAmount` (replays through the reactive `TextChanged` path via `EmitSignal`, same as before). `ApplyPanelState` now takes a `params Button[]` so all four buttons per panel enable/disable together.
2. **Rounding gap analysis + fix.** Dev report: typing "10.00" in Panel A's receive field returned a quote for `9.99999996` BTC — a 4-satoshi shortfall. Root cause: the reverse solve computes an exact gross via `MaxGrossForNet`, converts to SC via one `Money.Normalize` (8-decimal rounding), then replays through the forward quote (which independently re-derives gross via a SECOND rounding) — two independent 8-decimal roundings straddling a division by `price` do not perfectly cancel. Analysis (given to the dev before implementing, confirmed via question): the gap scales as `error_SC ÷ price`, so it is proportionally worse at LOW prices (the earliest/cheapest years of the simulated market) and shrinks to a negligible fraction of a satoshi as price rises — economically immaterial at any legal swap size, but a real deterministic shortfall against the typed number worth fixing on principle. **Fix (both reverse-quote options combined, per dev's confirmed choice)**: (a) both `QuoteScToBtcForReceivedBtc`/`QuoteBtcToScForReceivedSc` now follow their one-shot estimate with a small bounded nudge loop (`MaxExactMatchIterations = 10`, converges in 1–3 iterations observed) that bumps the pay-side input up until the delivered net is AT LEAST the desired amount — the negligible surplus (usually 0, occasionally a few satoshi) folds silently into the pay amount, never shown as its own UI line (per dev's explicit confirmation: "el monto indefinido no se va a mostrar... se sobreentendera con la explicacion respectiva en el manual"); (b) the quote label in both panels now breaks `FeeCharged` down into `network fee` (always exactly `NetworkFeePolicy.MinFee`, converted to SC at `PriceUsed` for Panel B) + `casino fee` (the remainder, legitimately **0** in the fee-floor regime near the minimum swap size — shown as 0, not hidden, since that's the clearest illustration of why the minimum-size floor exists).
- **Documentation**: `Documentation/ProjectDesignManual.md` Chapter 34 — the "desk itself" writeup Ch. 33's intro had promised, covering the reactive dual-input design, the clobber-bug caught and fixed before shipping (a per-panel "last edited field" tracker), the exact-match rounding fold-in (full cause analysis + the fix + why there is deliberately no new UI line for it), and the fee breakdown math. Ch. 33's forward-reference updated to point at it.

**Follow-up 7 (same session) — NOT a bug: effective casino margin near the minimum swap size.** Dev report: at the 10% default fee, a swap just above the minimum size showed `network 0.1 BTC + casino 0.01 BTC` (not ~0.1+0.1) — "el casino termina cobrando 1%". Confirmed this is correct, matching the plan's own §3.3 worked example exactly: `feeCharged = max(fee% × gross, NetworkFeePolicy.MinFee)` is the WHOLE fee (the network fee is INCLUDED in the 10%, never additive on top, per D-SW.1) — so `effectiveMarginPercent = SwapFeePercent − (NetworkFeePolicy.MinFee ÷ gross) × 100` is 0% exactly at the minimum gross and only approaches the nominal rate for swaps many times larger than the minimum (10× → within ~1 point; 100× → within ~0.01 point). Added an **effective margin %** readout to both quote labels (next to the network/casino BTC-or-SC split already shown) so this never needs re-deriving by hand mid-playtest. Documented in ProjectDesignManual Ch. 34 (new paragraph after the fee-breakdown section).

**Follow-up 8 (same session) — REDESIGN: fee model changed from inclusive to additive (D-SW.11, supersedes locked D-SW.1).** After Follow-up 7's explanation, the dev decided the INCLUSIVE model itself (not just its near-minimum behavior) was the wrong design: "quiero que el casino calcule el 10% de todo el monto a cambiar incluyendo network fees y lo SUME al total." This reverses a previously **locked** decision (D-SW.1) with worked examples baked into multiple docs — handled via a formal plan (`EnterPlanMode`/`ExitPlanMode`), not an inline edit, given the scope. Derived and confirmed (via `AskUserQuestion`) the new formula and the resulting minimum-swap-size choice before implementing:
- **New formula**: `totalFee(base) = NetworkFeePolicy.MinFee×(1+fee) + fee×base` (additive, replaces `max()`) — **linear**, so the old piecewise `MaxGrossForNet` (floor-region/percentage-region split) collapsed into one exact inverse, `BaseFromNet(targetNet, fee) = (targetNet + MinFee×(1+fee))/(1−fee)`, reused everywhere: both forward quotes' fee term, both Max-clamp calcs, both reverse "receive X" quotes, and the minimum-swap-size derivation.
- **Minimum swap size recalibrated** (dev-confirmed: pure mathematical minimum, no added UX floor): the old floor prevented a casino LOSS (now impossible — margin is always `fee×(base+MinFee) > 0`); the new floor prevents a degenerate (≤0) delivery, calibrated to `BaseFromNet(OneSatoshi, fee)` ≈ **0.1222 BTC at 10%** (down from the flat 1.0 BTC). Side effect: `MinDeliverableBtc`/`MinScPayoutAt` collapse to **fee-independent constants** (`0.10000001 BTC` / `price×OneSatoshi`), since `net(minBase) = OneSatoshi` by construction for any fee%.
- **`MinDeliverableBtc`/`MinScPayoutAt` simplified accordingly**; the exact-match nudge loops (Follow-up 6) were left untouched — they compensate for `Money.Normalize` rounding in the SC↔BTC round-trip, orthogonal to whether the fee curve is linear or piecewise.
- **UI**: only the header's fee label needed rewording ("incl." → "+", additive phrasing); the quote labels' network/casino/effective-margin breakdown (Follow-up 6/7) was verified algebraically to already compute correctly against the new formula with zero code changes, since it derives network vs. casino portions generically from `FeeCharged`.
- **Effective margin now runs the OPPOSITE direction from Follow-up 7's finding**: it starts ABOVE the nominal % near the minimum (e.g. 11% at `gross = 1.0` BTC, the old minimum) and settles DOWN toward nominal as swaps get larger (≈10.0007% at the §3.3 100-SC example) — the reverse of the old inclusive model's "starts at 0%, ramps up" behavior.
- **Documentation**: `D-SW.11` added to §0.5 (supersedes `D-SW.1`, which is marked superseded but kept for history); §3.1/§3.2/§3.3 kept as historical v1 record, with a new §3.1a holding the current rule + recomputed worked examples (verified by direct calculation, not hand arithmetic); §4.1/§4.2's pipelines updated to the new formula/minimum. `CLAUDE.md`, `Documentation/GLOSSARY.md`, and `ProjectDesignManual.md` Ch. 34 §34.4 updated to match (the "margin ramps up from 0%" paragraph rewritten to "starts above nominal, settles down").

**Follow-up 9 (same session) — REAL BUG: MIN buttons off-by-one-satoshi + receive-field MIN buttons dead entirely.** Dev report: the pay-input MIN button filled the analytical minimum exactly, but that exact value showed INVALID (orange) — one satoshi more turned it valid (green); both panels' receive-field MIN buttons did nothing at all. Root cause, confirmed with precise decimal simulation (PowerShell, matching `Money.Normalize`'s actual `MidpointRounding.ToZero` — i.e. **truncation**, not round-to-nearest, which the first hand-derivation had wrongly assumed): `MinSwapGrossBtcFor`'s analytical estimate (`BaseFromNet(OneSatoshi, fee)`) is exact only under infinite-precision arithmetic. The real pipeline truncates THREE times in a row (`grossBtc`, `feeBtc`, `netBtc`, each its own `Money.Normalize`), and those compounding truncations shaved the intended "net = +1 satoshi" down to `netBtc = -0.00000011` (Panel A) or `netSc = 0` exactly (Panel B) at the displayed minimum — both fail the strict `net > 0m` validity check. The receive-MIN buttons' silent no-op was the SAME root cause one layer further: `FillReceiveExtreme` re-quotes at `probe.MinInput` and fills the receive field with the result's `NetOut` — which was ≤0, so `FillAmount`'s `if (amount <= 0m) return;` guard silently swallowed the fill.
  - **Fix**: extracted the forward quotes' core math (no clamps/validity) into pure, non-recursive helpers `ComputeScToBtcCore`/`ComputeBtcToScCore`, then added `FindMinScInput`/`FindMinBtcInput` — the SAME exact-match nudge pattern the reverse "receive X" quotes already use (Follow-up 6), but targeting "any positive net" instead of a caller-specified target, and built on the pure core helpers specifically so it cannot recurse into `QuoteScToBtc`/`QuoteBtcToSc` (which now call `FindMin*Input` for their own `MinInput` field — a direct call from the reverse-quote-style logic back into the forward quote would have been circular). `QuoteScToBtc`/`QuoteBtcToSc`'s `MinInput` now use these finders instead of the raw analytical `MinSwapGrossBtcFor(fee) * price`.
  - **Verified with precise decimal simulation** (not hand arithmetic): both panels' true minimum now lands exactly 1 satoshi above the old (broken) analytical estimate, converging in 1 nudge iteration, with `net = +0.00000001` at the corrected minimum — confirmed green/valid, and the receive-MIN buttons now fill a positive `NetOut`.
  - **Lesson for future money-math in this codebase**: `Money.Normalize` **truncates** (`MidpointRounding.ToZero`), it does not round to nearest — any analytical derivation assuming exact arithmetic across a MULTI-STEP `Money.Normalize` pipeline (3+ chained roundings) should be verified against the real truncating pipeline before trusting it as a boundary/validity threshold, not just algebraically. `MaxExactMatchIterations`/`OneSatoshi` (already defined for the reverse-quote nudges) were reused rather than duplicated.

**Follow-up 10 (same session) — DESIGN CHANGE: minimum swap size redefined from "net > 0" to a VALUE floor.** Dev report: with the freshly-fixed "net>0" floor in place, buying BTC with the (Panel A) minimum let a player pay an amount almost entirely in fees to net back a tiny fraction of a BTC (observed: netting `0.00000008 BTC` on a swap costing far more in fees) — "es absurdo pagar tantos fees para cambiar una fraccion." The dev's explicit new requirement: the minimum swap must guarantee the player nets back **at least as much as they pay in total fees** (`net(base) ≥ totalFee(base)`), not merely a positive amount.
- **Derivation**: since `base = net + totalFee`, the condition `net ≥ totalFee` is equivalent to `net ≥ base − net ⟺ 2×net ≥ base`, i.e. the minimum is where `net` is exactly half of `base`. Setting `net(base) = totalFee(base)` and solving:
  ```
  base = 2×[MinFee×(1+fee) + fee×base]
  base×(1 − 2×fee) = 2×MinFee×(1+fee)
  base = 2×MinFee×(1+fee) / (1 − 2×fee)
  ```
  At the 10% default this is **≈0.275 BTC** (up from the "net>0" floor's ≈0.1222 BTC from Follow-up 8/9, still well down from the original D-SW.1 flat 1.0 BTC). `MinSwapGrossBtcFor(fee)` was updated to this formula (the `fee ≥ 0.5m` defensive guard is unreachable in practice since `SwapFeePercent` is clamped to `[1%, 10%]` per D-SW.9).
- **`FindMinScInput`/`FindMinBtcInput` (Follow-up 9's truncation-safe nudge finders) updated to match**: the nudge condition changed from `netBtc > 0m` to `netBtc > 0m && netBtc >= feeBtc` (same pattern, new target). Verified with the same precise-decimal-truncation simulation used in Follow-up 9: at the 10% default and an arbitrary test price, the analytical `MinSwapGrossBtcFor` estimate (`0.275` BTC / `0.20612244` BTC at 1% fee) converges in the very FIRST iteration with `net` exactly equal to `fee` (no truncation shortfall this time — the value-floor equation divides evenly where the old "net = 1 satoshi" target did not), so no nudge was actually needed in the cases tested, but the loop is retained for safety against less clean prices.
- **`MinDeliverableBtc`/`MinScPayoutAt` (the panels' enable thresholds) UN-simplified back to live, fee-dependent computations** — Follow-up 8's "fee-independent constant" simplification was a side effect specific to the "net = OneSatoshi always" floor, and no longer holds under the value floor (net-at-minimum is now `minGross/2`, which moves with fee%): `MinDeliverableBtc(fee) = MinFee×(2−fee)/(1−2×fee)` (≈0.2375 BTC at 10%, ≈0.20306122 BTC at 1%), `MinScPayoutAt(priceSc, fee) = priceSc × MinFee×(1+fee)/(1−2×fee)`. Both now read the live fee via `GetSwapFeePercentFor(PlayerNodeId)` (the player is the only client today; a future rank system would need to decide which client's fee sizes a shared enable-threshold, out of scope here).
- **Effect on panel unlock timing**: Panel A/B now need slightly more casino BTC/SC on hand to unlock than under the brief "net>0" floor (≈0.275 BTC vs ≈0.1222 BTC at 10% default) — still far below the original 1.0 BTC flat floor.
- **Documentation**: `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` §0.5 (D-SW.11 entry), §3.1a, §4.1, §4.2 updated to the new ≈0.275 BTC figure and the fee-dependent `MinDeliverableBtc`/`MinScPayoutAt` formulas (this Follow-up). `CLAUDE.md`'s `CasinoCoinSwapService` subsection and `Documentation/ProjectDesignManual.md` Ch. 34 §34.3/§34.4 updated to match in Follow-up 11.

**Follow-up 11 (same session) — REAL BUG: Panel B's MIN button still showed invalid after the Follow-up 10 fix.** Dev report: Panel A's MIN button now worked, but Panel B's still turned the display orange. Diagnosed via a fresh PowerShell sweep across fee/price combinations — a DIFFERENT truncation bug from Follow-up 9's, this time in the **Max clamp**, not the Min: `casinoMaxBtc = BaseFromNet(OfferedSc/price, fee)` (Panel B) and `casinoMaxSc = BaseFromNet(OfferedBtc−MinFee, fee)×price` (Panel A) are each an INDEPENDENT derivation of "how much can the casino afford," separately truncated from the panel-enable gate's own threshold formula (`MinScPayoutAt`/`MinDeliverableBtc`). Algebra proves the two should agree exactly, but `Money.Normalize`'s truncation at each step can make the Max-clamp land a few satoshi BELOW the Min at the boundary — exactly when the casino's offered balance is razor-thin above the minimum (the scenario a MIN-button press specifically exercises). Verified numerically: at `fee=10%, price=1.23456789`, `casinoMaxBtc` computed to `0.27499998` against `minGross=0.275` — a 2-satoshi shortfall, enough to fail `IsValid`'s `btcAmount <= MaxInput` check.
- **Fix**: since `PanelAReason`/`PanelBReason == None` at this point in the code already proves (via the panel's own enable gate) that the casino CAN afford at least the minimum swap, floor the casino-side Max estimate at the (already truncation-safe, from Follow-up 9) `MinInput`: `casinoMaxSc = Math.Max(BaseFromNet(...)×price, minSc)` and `casinoMaxBtc = Math.Max(BaseFromNet(...), minGross)`. The PLAYER-side cap (`playerMaxSc`/`playerMaxBtc`, a plain balance read, no derived formula) is left untouched — if the player genuinely can't afford the minimum, `Math.Min(playerMax, casinoMax)` correctly stays below `MinInput`, which is a real "insufficient funds" state, not a bug.
- **A more aggressive "adaptive proportional-jump" fix was prototyped and rejected**: initial attempts to make the Max-clamp itself truncation-exact (mirroring Follow-up 9's nudge, but climbing instead of descending) failed to converge at very low BTC prices (where a single BTC-satoshi moves the SC-side net by less than one SC-satoshi, requiring thousands of nudge iterations) — verified this failure mode numerically before abandoning it in favor of the much simpler floor-at-MinInput fix above, which sidesteps the convergence problem entirely (it doesn't try to find the EXACT true max, just guarantees the max is never inconsistently below the already-correct min).
- **Documentation**: this Follow-up; `CLAUDE.md`'s `CasinoCoinSwapService` subsection and `ProjectDesignManual.md` Ch. 34 §34.3 updated with the ≈0.275 BTC minimum figure (superseding the stale ≈0.1222 BTC references) and a note on this second truncation-boundary bug class.

**Follow-up 12 (same session) — NEW FEATURE: max fee deviation cap (D-SW.12).** Dev question + request: what does "effective X.0X%" mean in the fee breakdown (answered: the casino's real % cut on that specific swap, always ≥ nominal under the additive model, converging to nominal only for large swaps) — followed by "quisiera poder controlar que el casino no se desvíe tanto del porcentaje fijado." Presented three implementation options (points-above-nominal, multiplier-of-nominal, absolute independent ceiling); dev chose **points above nominal**.
- **First design attempt (rejected after numerical verification)**: capping the COMBINED total fee (`max(MinFee, min(additiveFee, cappedFee))`) was tried first, but proved mathematically broken — for any gross small enough that the flat network fee alone exceeds `(nominal+points)%` of it, the "never below network cost" floor and the "never above nominal+points" ceiling become mutually unsatisfiable, and the floor wins, producing an effective **total** cost WORSE than the uncapped model at some sizes (verified: 20% at gross=0.5 BTC, vs. the uncapped model's 12% at the same size — the opposite of the intended fix).
- **Corrected design**: cap the CASINO'S OWN CUT only (`casinoFee = max(0, min(fee×(gross+MinFee), (fee+maxDeviationFraction)×gross))`), with the flat network fee always charged in full, separately, on top (`totalFee = networkFee + casinoFee`). This has no floor/ceiling conflict (0 and a non-negative ceiling never contradict), verified numerically across gross sizes from the minimum (0.275 BTC) up to 1,472 BTC: effective margin holds flat at exactly `nominal+points` below the crossover point (`gross = fee×MinFee/maxDeviationFraction`, 0.5 BTC at the 10%/2pt defaults) and smoothly decreases toward nominal above it, matching §3.1a's original (uncapped) shape exactly once the cap stops binding.
- **New knob**: `MaxFeeDeviationPoints` (default `2.0`, clamped `[0,20]` points, `SetMaxFeeDeviationPoints`), in `CasinoGamblingFinances` beside the existing swap fee %. Checkpoint-covered + pre-genesis reset to default, same pattern as `SwapFeePercent`.
- **Interaction with the §3.2 minimum swap size**: the cap only ever REDUCES `casinoFee` (hence `totalFee`) relative to the uncapped value, which only INCREASES `net` relative to uncapped — so the existing value-floor minimum (`net ≥ totalFee`, derived from the uncapped formula) remains valid (with more slack) under the cap; it was not re-derived.
- **Documentation**: this Follow-up; new §3.1b in this plan; `CLAUDE.md`'s `CasinoCoinSwapService` subsection + Canonical Decisions row; `ProjectDesignManual.md` Ch. 34 §34.4 rewritten with the corrected two-region (capped/uncapped) margin shape.

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
