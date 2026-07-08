# BTC Market Data, Canonical Trading Unlock & DEV Alt-Timeline Bootstrap (Step 13) — Design Plan

**Status**: v3 (2026-07-07) — **COMPLETE**. All phases done (TL.0 → TL.1 → MD.0 → MD.1 → MD.2 → TL.2 → SW.\* → TL.3); `DevAltTimeline` reverted to `false` at TL.3 and the docs truth pass executed. Remaining developer-only step: the in-editor canon relaunch (guard wipes the alt world) + the canon swap-desk re-verify (TL.3, bullet 2).

**Scope**: Three tightly-coupled deliverables that together open the road to P7 (BTC/SC trading):

1. **The historical BTC/USD daily price dataset** (`Data/HistoricalPrices/btc_usd_daily_2010_2025.csv`) — full provenance, schema, caveats, and the loader service that exposes it to the game (§1, §4).
2. **A canonical-rule change**: BTC/SC trading unlocks when the game clock reaches the dataset's first date — **18 Jul 2010, Mt. Gox's launch** — not at player start. In the canonical timeline the player waits ~16 in-game months for the first exchange to exist, which is accepted and historically honest (§2).
3. **A DEV-only alternative-timeline bootstrap** (the "**simulacrum world**") that shifts the entire early history forward so the player lands exactly on 2010-07-18 with live prices from day one — so swap tooling can be developed and tested without grinding 16 in-game months. **This world is a temporary development scaffold, never canon, and is discarded when the swaps work merges back** (§3 — read the warning box).

**Companion docs**: `AIHelperFiles/step7-historical-character-economics-plan.md` (founder arcs the alt timeline must replay) · `AIHelperFiles/historical-founders-and-bootstrap-plan.md` (bootstrap mechanics) · `Documentation/ProjectDesignManual.md` §24.8–24.10 (block-is-the-only-commit, game-time rules) · `Documentation/PRIVATE_ROADMAP.md` P7 · CLAUDE.md "Open Design Questions" ("When exactly should BTC trading unlock in Basic Mode?" — this plan answers it).

---

## ⚠️ 0. THE ALT TIMELINE IS A TEMPORARY SIMULACRUM — READ FIRST

This cannot be over-stated, so it goes before everything else:

- The DEV alt-timeline bootstrap produces a blockchain whose dates **contradict the game's canonical history** (genesis 2010-05-02 instead of 2009-01-03). It exists **only** so that swap tooling can be developed against live market data without waiting out the canonical ~16-month gap.
- It lives **only on the swaps feature branch**, behind a single compile-time flag (`TimelineConfig.DevAltTimeline`). The flag is **`false` on `main`, forever**. No alt-timeline world may ever ship, be demoed as canon, or leak screenshots into design docs as if it were real history.
- Worlds are **mutually incompatible**: a save created under one timeline must never be loaded under the other. A timeline stamp + clean-reset guard (Phase TL.1) enforces this automatically — switching branch direction wipes the world, both ways, by design.
- **Exit plan is part of the feature**: when the swaps work is complete, the branch flips `DevAltTimeline` back to `false` *before* merging to `main` (or the flag site is reviewed to confirm it), the dev machine's alt world is wiped by the guard on next launch, and the canonical bootstrap regenerates the true 2009 world. The only permanent survivors of this step on `main` are: the CSV asset, the market-data service, the trading-unlock rule, the `TimelineConfig` refactor (with offset zero), and the timeline guard itself.
- Everything date-anachronistic inside the simulacrum (e.g. the genesis headline "The Times 03/Jan/2009…" on a block stamped 2010-05-02) is **accepted cosmetic noise** — it is not worth patching a throwaway world (D-13.0).
- The replica differs from canon in **dates only, with ONE deliberate functional exception**: network fees activate on the alt player-start day (**2010-07-18**) instead of the uniformly-shifted 2010-08-23 (D-13.9, §3.6) — canon itself is long fee-active by the time trading unlocks, so a fee-active desk is the *more* faithful swap-testing environment.

---

## 0.5 Decisions locked (round 1 — 2026-07-05)

| # | Decision |
|---|---|
| **D-13.0** | Cosmetic anachronisms inside the simulacrum are tolerated, never patched (genesis headline etc. — §0). |
| **D-13.1** | **Exact 484-day replica** (genesis **2010-05-02**, ~113-block bootstrap, identical world shape). The "November 2009 extended pre-history" idea is **discarded**. Spin-off idea recorded for a FUTURE plan (not this one — §9.1): player-selectable entry-point bootstraps, canon start remaining 21 Mar 2009. |
| **D-13.2** | Intraday price = **step function** (the day's VWAP holds all day). Synthetic intraday walk deferred (§9.2). |
| **D-13.3** | MD.2 surfaces: **(a) BTCWallet valuation line + (b) StatusBar ticker + (c) ScFinances dual-mode toggle label** (full spec §4.2-c: toggle ON → total Net Worth *including* BTC, in SC; toggle OFF → the player's BTC value *alone*, in SC). The **existing SC-only metric keeps — and gains — top visual prominence**: it is the game-over determinant (`Bank + Main + Bankroll = 0`, D-SF2.1) and the UI hierarchy must say so. The game-over rule itself stays SC-only, untouched. Chart deferred (§9.2). |
| **D-13.4** | Swap counterparty = **casino-as-dealer**, bounded by its real BTC inventory + real SC. Follow-up inside SW.\*: verify/seed the casino's BTC inventory at simulacrum landing. |
| **D-13.5** | Post-2025-12-31 behavior: **freeze last price + "post-history era" label**. Explicitly the *most deferred* concern of this plan — 16 years of history away at current pace; revisit only if it ever matters. |
| **D-13.6** | Swap UI = **new `BtcSwap` scene** (MainMenu + ScFinances links; Ch. 29 fixed-footer layout from day one). |
| **D-13.7** | **One `ResetWorldIfIncompatible` with one complete delete list** — the format-version path and the timeline path share the full Step-11/12 list + bet-history chunks. No divergent partial resets. |
| **D-13.8** | Per-day liquidity caps: **deferred** (§9.2). If revived, they use the /100-scaled volume (D-13.10), capped per `source` regime. |
| **D-13.9** | **Alt-timeline fee activation = 2010-07-18** (landing/market-open day), NOT the uniformly-shifted 2010-08-23. The plan's only deliberate functional divergence: all BTC wallets have supported automatic fee inputs since canon 2009-04-26, and swaps must be developed in the fee-active environment canon will actually have when trading unlocks. Consequence for the alt-era Hearn round-trip → §3.6 implementation check. |
| **D-13.10** | **The /100 fractal-replica rule reaches the dataset's consumption**: the CSV stays RAW real-world history; `BtcMarketDataService` exposes game-scaled accessors — `volume_btc ÷ 100`, `num_trades ÷ 100` — for ALL gameplay use. **`price_usd` is exempt by decree — the single tolerated contradiction of the fractal replica.** Details §1.6. |
| **D-13.11** | The 13 `none` days = **market-halt days**: swap desk closed, last price shown greyed with the historical reason (§1.4, §4.3). |
| **D-13.12** | Market-day lookups use the game clock's **local date** directly; the ≤ hours UTC-vs-local mismatch is accepted at daily granularity (§1.5-e). |

---

## 1. The dataset — origin, construction, and modification history

### 1.1 File identity

- **Path (repo)**: `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv` — new `Data/` root folder; already created, **untracked** (developer commits manually per project git policy).
- **Size / shape**: ~283 KB, 5,646 data rows + 1 header, one row per calendar day (UTC days), **continuous** from **2010-07-18** to **2025-12-31** with zero missing dates (days without trading are present, marked — see §1.4).
- **Schema**: `date,price_usd,volume_btc,num_trades,source`
  - `date` — `yyyy-MM-dd` (UTC trading day).
  - `price_usd` — the day's representative price. True VWAP where the source allows, proxy otherwise (see `source`). Since SC is USD-pegged 1:1, this **is** the SC price of 1 BTC.
  - `volume_btc` — BTC traded that day **on the source exchange only** (not global volume — see caveat §1.5-b).
  - `num_trades` — trade count where available; **blank** for the Bitfinex regime.
  - `source` — provenance + fidelity marker per row: `mtgox` | `bitfinex` | `binance` | `none`.
- **Number format**: `InvariantCulture` (period decimal, no thousands separators). Empty cell = **no data**, never zero — parsers must treat `""` as null, not `0` (a `decimal.Parse("")` crashes; a silent `0` would poison charts and swaps).

### 1.2 Sources per regime

| `source` | Date range | Rows | `price_usd` meaning | `num_trades` | Origin |
|---|---|---|---|---|---|
| `mtgox` | 2010-07-18 → 2014-02-01 | 1,289 | **True daily VWAP** = Σ(price·amount)/Σ(amount) over every individual trade | real | Trade-level dump `mtgoxUSD.csv.gz` from bitcoincharts.com (8,295,809 raw trades; 7,834,805 in range) |
| `bitfinex` | 2014-02-02 → 2017-08-16 | 1,285 | **Proxy** = typical price (High+Low+Close)/3 of the daily candle — Bitfinex exposes no trade-level data | blank | Bitfinex public REST `candles/trade:1D:tBTCUSD/hist` |
| `binance` | 2017-08-17 → 2025-12-31 | 3,059 | **True daily VWAP** = quote_volume / base_volume from official kline dumps | real | `data.binance.vision` monthly 1d klines, BTCUSDT (101 monthly files) |
| `none` | 13 scattered days | 13 | *(blank)* | 0 | Real historical trading halts — see §1.4 |

**Why not CoinGecko** (was the original suggestion): verified 2026-07-05 — its public API now returns error `10012` for any query beyond the last 365 days (paid plans only), and even paid it provides only a single daily price + volume — **no VWAP, no trade count**. Rejected.

**Why the Wayback Machine for Mt. Gox**: bitcoincharts.com is dead (verified 2026-07-04: DNS resolves to a Hetzner host, nothing listens on ports 80/443). The dataset was retrieved **byte-identical** from the Internet Archive snapshot of the exact original file: `web.archive.org/web/20161229014309id_/http://api.bitcoincharts.com/v1/csv/mtgoxUSD.csv.gz` (57 MB gzip, snapshot 2016-12-29 — Mt. Gox closed Feb 2014, so the file was frozen years before the snapshot; nothing is missing).

### 1.3 Construction pipeline (reproducible)

Three PowerShell 5.1 scripts, kept **out of the repo** (they are one-shot tooling, not game code) in `C:\Users\PERSONAL\Desktop\Proof of Fun\Project out of repo data\`:

1. `Get-MtGoxDailyVwap.ps1` — downloads the Mt. Gox trade dump (original URL first, Wayback fallback), streams ~8.3M trades, aggregates per-UTC-day VWAP/volume/count, keeps empty days.
2. `Get-BtcDailyVwap-2014-2025.ps1` — Bitfinex candles (one request) + Binance monthly kline zips (cached in `%TEMP%\btc_vwap_cache`); Binance wins on overlap.
3. `Merge-BtcDailyHistory.ps1` — concatenates the two intermediates, translates headers to English, assigns `source`, **asserts the join is contiguous** (2014-02-01 → 2014-02-02, no gap/overlap) and refuses to write otherwise.

Regenerating or extending the dataset (e.g. appending 2026) = run 2 with a later `-EndDate`, then 3.

### 1.4 The 13 `none` days — real halts, deliberately preserved

| Days | Event |
|---|---|
| 2011-06-20 … 2011-06-25 (6) | Mt. Gox suspended trading after the June 2011 hack / $0.01 flash crash |
| 2016-08-03 … 2016-08-09 (7) | Bitfinex suspended trading after the August 2016 hack (~120,000 BTC stolen) |

They are **not data errors and must not be interpolated away silently**. Locked (D-13.11): surface them in-game as "market halted" days — the swap desk closes, which is free historical flavor.

### 1.5 Known caveats (documented so nobody "fixes" them later)

- **(a) Price seam at 2014-02-01→02 (−14%)**: Mt. Gox's last VWAP is ~$949 while Bitfinex's next day is ~$817. Not a crash — the famous "Gox premium" right before its collapse. A day-over-day return computed across this seam is an artifact.
- **(b) Volume seam at 2017-08-16→17**: `volume_btc` drops ~35,000 → ~800 because the column switches exchange (Bitfinex, mature) → (Binance, just launched). Volume is only comparable *within* one `source` regime.
- **(c) Bitfinex VWAP is a proxy** — treat 2014–2017 prices as "daily representative price," not a true VWAP.
- **(d) Binance regime is BTC**USDT** — USDT≈USD assumed (standard practice; deviations were brief and small).
- **(e) UTC days vs. game-local clock**: rows are UTC trading days; the game clock is Local. At daily granularity the mismatch is ≤ hours and irrelevant to gameplay — lookups use the game clock's **local date** directly (D-13.12).

### 1.6 The /100 fractal-replica rule applied to this dataset (D-13.10)

The sim is a deliberate **1:100 fractal replica** of Bitcoin's economy: 210,000 BTC total supply (vs 21M), halving every 2,100 blocks (vs 210,000), Satoshi targeting 11,000 BTC (vs his real ~1.1M). The market dataset must obey the same rule wherever its numbers touch the game world:

- **The CSV stays raw.** The asset remains a pure real-world historical record — provenance-stable and regeneration-safe (the §1.3 pipeline never needs to know about game scaling).
- **Scaling happens at the service boundary** (`BtcMarketDataService`): gameplay accessors return `GameVolumeBtc = volume_btc / 100` and `GameNumTrades = max(1, round(num_trades / 100))` — floor-to-1 so a day with *any* real trades never reads as zero market activity (day one's 5 real trades → 1 game trade). Raw values remain available for DEV/provenance readouts only.
- The sanity check that motivates this: 2013-11-29's raw volume (38,295 BTC) would be **~18% of the sim's entire eventual 210,000-BTC supply traded in one day** — absurd in-world. ÷100 → ~383 BTC, exactly proportional to reality.
- **`price_usd` is exempt by decree — the single tolerated contradiction of the fractal replica** (user, round 1). In pure theory a coin 100× scarcer inside an economy 100× smaller has an indeterminate price ("should be worth less — or more!"); we pin it to real history and move on. Sim market cap is therefore 1/100 of real — which is itself fractal-consistent.
- A happy consistency we get for free: halving is /100 in *blocks* but block time ≈ 16.25 in-game hours, so halving cadence in *calendar* time (~3.9 years) ≈ reality (~4 years) — the real price cycles in this dataset stay roughly in phase with the sim's reward eras.
- ✅ **Canonical-value check RESOLVED** (verified in code, 2026-07-05): round-1 feedback recalled "**48** transactions per block", but the implemented value is **24** — `BlockTemplateBuilder.MaxBlockTransactions = 24`, counting the coinbase (coinbase + up to 23 mempool txs, OQ-C4), and CLAUDE.md's canonical table agrees (its "planned" label is stale — the cap is live). Real blocks carry ~2,000–3,000 txs, so /100 ≈ 20–30: 24 fits the fractal perfectly. No change needed; CLAUDE.md's "planned" label should be corrected to implemented during this step's docs pass.

---

## 2. Canonical-rule change — trading unlocks at Mt. Gox launch

**New canonical decision (for CLAUDE.md "Canonical Decisions" once implemented):**

> **BTC/SC trading unlock**: the swap desk exists only from **2010-07-18** (Mt. Gox launch — the first date of `btc_usd_daily_2010_2025.csv`). Before that in-game date there is no market, no price, and no swap UI beyond a locked teaser. The gate is **data-driven** (`BtcMarketDataService.FirstDataDateLocal`), never a second hardcoded date.

Consequences in the canonical timeline (player starts 2009-03-21):

- Gap to market open = **484 in-game days** ≈ **715 blocks** (at 1.477 blocks/in-game-day). Real-time at continuous autobet: ~116 h at base 100X, ~11.6 h at 1000X, ~1.3 h at 9000X DevTimeScale. The player-facing wait is accepted (user decision, 2026-07-05) — BTC accumulates from mining long before it becomes tradable, which is historically exactly right.
- The existing CLAUDE.md open question "When exactly should BTC trading unlock in Basic Mode?" is **answered** by this rule.
- Locked-state UI proposal: the swap surface shows "No exchange exists yet — the first Bitcoin market opens 18 Jul 2010" (+ optionally a countdown), converting the wait into anticipation.

The dev-side consequence is the whole reason §3 exists: nobody can iterate on swap tooling behind a 1.3–116 h grind, and every world wipe (frequent during development) restarts the grind. Hence the simulacrum.

---

## 3. The DEV alt-timeline bootstrap (the simulacrum world)

### 3.1 Intent

Reproduce **exactly today's first-launch experience** — same bootstrap structure, same founder arcs, same balances, same block count — but with every date shifted forward by a constant offset so the bootstrap lands the player on **2010-07-18**, the first day with market data. From the first bet, the swap desk has a live price ($0.07!), and the entire Mt. Gox era (rise to $1,163, crash, halts) is reachable within normal dev play.

### 3.2 The precise math (correcting the "November 2009" estimate)

The current bootstrap is **shorter than intuition suggests**: genesis `2009-01-03 18:15:05` → landing on `2009-03-21` is **76.24 days ≈ 113 blocks** (58,500 in-game s/block ±30% jitter), not "about a year". Preserving that exact arc:

```
Offset = 2010-07-18 − 2009-03-21 = 484 days  (applied uniformly to every anchor)
```

| Anchor | Canonical | Alt timeline (+484 d) |
|---|---|---|
| Genesis block (`BlockchainService.GenesisTimestampUnixMs`) | 2009-01-03 18:15:05 | **2010-05-02 18:15:05** |
| Calendar fallback epoch (`CalendarTimeService.GameStartLocal`) | 2009-01-03 18:15:06 | 2010-05-02 18:15:06 |
| Hal bootstrap block 1 / E4 (10 BTC Satoshi→Hal) | 2009-01-12 | 2010-05-11 |
| Hal bootstrap block 2 | 2009-02-05 | 2010-06-04 |
| Hal bootstrap block 3 | 2009-03-05 | 2010-07-02 |
| **Player start** (`HistoricalBootstrapService.PlayerStartDayLocal`) / Hal decay start | 2009-03-21 | **2010-07-18** ✅ = first CSV date |
| Hearn round-trip E6…E7b (`HistoricalEventScheduler`) | 2009-04-18 | 2010-08-15 |
| Network fee activation (`NetworkFeePolicy.ActivationDateLocal`) | 2009-04-26 | **2010-07-18** ⚠ deliberate divergence — uniform shift would give 2010-08-23; see D-13.9 + §3.6 |
| Hal decay end (ALS turning point) | 2009-08-09 | 2010-12-06 |
| Satoshi earliest disappearance | 2011-04-26 | 2012-08-22 (spans the 2012 leap day) |

So the founders start the alt chain on **2 May 2010** — ~2.5 months before the player, exactly as canon — **not November 2009**. The November estimate implied a ~259-day / ~383-block bootstrap, i.e. a *different* world (Satoshi lands with ~3.4× more blocks/BTC at player start, different difficulty history). **LOCKED (D-13.1): the exact 484-day replica.** The November idea is discarded; its useful kernel — entering the blockchain at other points in history — is preserved as the future player-feature exploration in §9.1.

Everything **not** in the table is timeline-agnostic by construction and needs no change: halving (block-height 2,100), difficulty regulator (solvetimes), pre-genesis reset & player-start instant (derived from the chain tip), checkpoint clock rule (reads the live calendar), bot scheduling (block-relative). This is precisely why the uniform-offset approach is cheap and safe.

### 3.3 Implementation shape — one flag, one offset, seven anchor sites

New tiny static class (proposal — naming per §5): **`TimelineConfig`** (`Scripts/Services/TimelineConfig.cs`):

```csharp
public static class TimelineConfig
{
	// DEV ONLY — true ONLY on the swaps feature branch. NEVER true on main. See step13 plan §0.
	public const bool DevAltTimeline = false;

	public static readonly TimeSpan Offset = DevAltTimeline ? TimeSpan.FromDays(484) : TimeSpan.Zero;
	public static readonly string Tag = DevAltTimeline ? "ALT-2010-07-18" : "CANON-2009-01-03";

	public static DateTime Shift(DateTime canonicalLocal) => canonicalLocal + Offset;
}
```

The seven anchor sites (§3.2 table, left column — files: `BlockchainService`, `CalendarTimeService`, `HistoricalBootstrapService` ×3 constants, `FoundersMiningService` ×3, `HistoricalEventScheduler`, `NetworkFeePolicy`) are refactored to route their `static readonly` date through `TimelineConfig.Shift(...)`. With `Offset == TimeSpan.Zero` this is **bit-for-bit behavior-identical to today** — the refactor itself is safe to land on `main` (Phase TL.0), and the branch toggles a single `const`.

Explicitly **not** shifted: `CalendarTimeService.LegacyStartLocal` and its migration comparisons (they match historical *persisted* values, not world anchors); the genesis headline string (cosmetic, D-13.6); `WorldFormatVersion` (orthogonal).

### 3.4 World-incompatibility guard (both directions)

A canon save loaded under the alt flag (or vice versa) would be a corrupt hybrid — e.g. a 2009 chain tip with a 2010 fee-activation date, or bet-history rows dated before genesis. Guard (Phase TL.1):

- New stamp `user://world_timeline.stamp` containing `TimelineConfig.Tag`, checked at the same point as `NetworkRoot.ResetWorldIfFormatChanged()` (generalize it to `ResetWorldIfIncompatible()` — version **or** timeline mismatch triggers the same clean reset, then re-stamps both).
- **The Step 8 delete-list must be extended** — it predates Steps 11–12. The timeline reset must clear *everything player-visible*: chain + monthly chunks, checkpoint, calendar, bankroll, principal, bankroll-program, **`casino_sc_balance_state.json`, `player_bank_account_state.json`, `casino_client_ledger.json`, and the bet-history month chunks** (Step 8 spared bet history as "cosmetic", but canon-dated 2009 bets would sit *before* the alt world's genesis and permanently pollute the since-deposit/since-recharge stat scopes — wipe them on a timeline switch). The format-version path adopts the same full list (D-13.7).
- Net effect: checking out the branch and launching ⇒ automatic pristine alt world; checking out `main` and launching ⇒ automatic pristine canon world. No manual file surgery, no way to forget.

### 3.5 What must verifiably replay identically (acceptance criteria for TL.2)

- Bootstrap: ~113 blocks; Hal exactly 3 (near 2010-05-11 / 06-04 / 07-02); E4 10 BTC on-chain near 2010-05-11; landing = first block ts ≥ 2010-07-18 00:00 local; calendar == landing block timestamp exactly (the §24.9 rule holds unmodified because it is chain-derived).
- Founder arcs vs. `founders_trace.csv`: Hal fades to 0 by 2010-12-06; Satoshi paces toward 11,000 BTC by 2012-08-22; Satoshi bootstrap holdings at landing ≈ canon's (~110 blocks × 50 − sends).
- Fees: fee-free through the bootstrap era, active for the whole network from the 2010-07-18 landing (D-13.9); the alt-era Hearn round-trip verified per §3.6.
- Pre-genesis restart loop: restart before first player block ⇒ world resets to the 2010-07-18 landing instant (not to any 2009 value).
- `BtcMarketDataService`: on landing day returns Mt. Gox row 1 ($0.0679 VWAP).

### 3.6 The single functional divergence — fees active from the landing day (D-13.9)

A uniform shift would reproduce canon's 36 fee-free player days (alt activation 2010-08-23). Locked instead: **alt activation = 2010-07-18**, the landing/market-open day itself. Rationale: the simulacrum's fidelity target is *canon at the moment trading exists* — and in canon, by 2010-07-18 fees have been active for ~15 months, with every BTC wallet's automatic fee inputs battle-tested since 2009-04-26. Swap tooling must be born fee-aware, not retrofitted.

- The bootstrap era (2010-05-02 → landing) stays **fee-free**, matching canon's fee-free bootstrap — E4 and Hal's blocks are unaffected.
- `TimelineConfig` carries this one special case explicitly: `FeeActivationLocal = DevAltTimeline ? AltPlayerStartDay : new(2009, 4, 26)` — never a silent shift.
- **Implementation check (TL.2)**: the alt-era Hearn round-trip (E6…E7b, alt date 2010-08-15) now falls **inside the fee era**, whereas in canon it is fee-free (2009-04-18 < 2009-04-26). The scripted amounts are exact-match UTXO spends (E7a spends exactly the 32.51 with no change, etc.) — a forced fee would alter their historical amounts/shapes. Verify `InjectHistoricalSignedTxStatic` injections are fee-exempt (scripted history) or that the fee is handled without touching the historical amounts. **Do not silently alter the amounts**; if neither holds, exempt scripted historical txs from the fee policy explicitly.

---

## 4. Using the data in-game — options & proposals

### 4.1 Loader: new autoload #14 — `BtcMarketDataService` (recommended)

**Q: "Do we need another autoload always processing this data while the player advances time?"** — An autoload **yes**; "always processing" **no**. The dataset is 5,646 tiny rows; the only live work is noticing a day boundary. Options considered:

| Option | Shape | Verdict |
|---|---|---|
| **A (recommended)** | New autoload `BtcMarketDataService` (`Scripts/Services/BtcMarketDataService.cs`, autoload #14). Loads the CSV **once** in `_Ready()` from `res://Data/HistoricalPrices/…` into an array indexed by day-number (O(1) lookup). In `_Process`: a single cached-date comparison against `CalendarTimeService.CurrentLocalDateTime.Date`; on change, raise `event Action<MarketDay> MarketDayChanged`. No timers, no I/O, no per-frame math beyond one `!=`. | Fits the 13-autoload service architecture; one owner for the day-change event that every consumer (StatusBar ticker, swap UI, ScFinances) would otherwise reimplement. |
| B | Static class à la `HistoricalBootstrapService` (pure lookup, lazy load, no events) | Cheaper, but every consumer polls and re-derives day changes; no single throttle point. Fine fallback if #14 feels heavy. |
| C | Fold into `SimulationService` | Rejected — SimulationService owns bet/mining ticks; prices must also work while autobet is stopped, and its power/difficulty responsibilities shouldn't grow a market feed. |

Proposed API surface (round-1 sketch):

```csharp
public sealed record MarketDay(DateTime DateLocal, decimal? PriceUsd, decimal? VolumeBtc, long? NumTrades, string Source);

DateTime FirstDataDateLocal { get; }        // 2010-07-18 — THE trading-unlock gate (§2)
DateTime LastDataDateLocal  { get; }        // 2025-12-31
bool  IsMarketBorn(DateTime nowLocal);      // now >= FirstDataDateLocal
bool  TryGetDay(DateTime dateLocal, out MarketDay day);
bool  IsHaltDay(DateTime dateLocal);        // source == "none"
decimal? GetEffectivePriceUsd(DateTime nowLocal); // carry-forward over halt days; null before market birth

// /100 fractal accessors (D-13.10) — ALL gameplay consumption uses these; raw MarketDay fields are DEV/provenance-only
decimal? GetGameVolumeBtc(DateTime dateLocal);   // raw volume_btc / 100
long?    GetGameNumTrades(DateTime dateLocal);   // max(1, round(raw / 100)) when raw > 0

event Action<MarketDay> MarketDayChanged;   // fired when the game clock crosses a day boundary
```

Parsing rules (hard requirements): `CultureInfo.InvariantCulture`; empty `price_usd`/`volume_btc` ⇒ `null` (never `0`); `Money.Normalize()` before storing decimals; Godot `FileAccess` for the `res://` read (survives PCK export).

### 4.2 How the player sees BTC affected — display options

The key conceptual line: **the price never changes the BTC amount, only its SC valuation.** Proposals (combinable; pick in OQ-13.3):

- **(a) BTCWallet valuation line (recommended v1)**: under the BTC balance, `≈ 1,234.56 SC @ 0.07 SC/BTC` via `GetEffectivePriceUsd`. Before 2010-07-18: `— no market price yet (first exchange opens 18 Jul 2010)`.
- **(b) StatusBar ticker (cheap, high-visibility)**: compact `BTC 0.07` cell; refreshes on `MarketDayChanged` only (price is a daily step — zero per-frame cost). Greyed/`HALT` on halt days.
- **(c) ScFinances dual-mode BTC label + toggle (D-13.3, user-specified)**: ONE new label whose content a small toggle switches — **toggle ON** → `Total Net Worth (incl. BTC): X SC` = `NetWorthSc + BTC × price`; **toggle OFF** → `BTC holdings value: Y SC` (the player's BTC valuation *alone*) — always denominated in SC either way. Meanwhile the **existing SC-only metric (`NetWorthSc`/`OverallPl` = `Bank + Main + Bankroll`) keeps — and gains — top visual prominence** (bigger/bolder, explicit "(game-over metric)" tag): it alone determines whether the game stops, and the UI hierarchy must communicate that weight. The game-over rule itself stays SC-only and untouched (D-SF2.1), preserving the §7.4 escape-hatch design (a player with BTC and no SC is *supposed* to be rescuable by a swap, not auto-dead; the check stays interceptable).
- **(d) Price history chart** — defer; the Block Explorer pattern (RichTextLabel) could render a sparkline later.

### 4.3 How swap tooling reacts — design options (the actual swaps are the branch's main work)

- **Intraday price model (D-13.2 — LOCKED: step function)**: the day's VWAP holds all day — honest to the data, trivial, deterministic across restarts. Rejected: linear interpolation (fabricates intraday prices, lookahead smell); deferred: seeded synthetic intraday walk (§9.2).
- **Counterparty & inventory (D-13.4 — LOCKED: casino-as-dealer)**: casino sells only BTC it actually owns (mined/pooled, on its addresses) and buys with its real SC (`CasinoScBalanceService`) — swaps are real UTXO sends (existing `BuildAndBroadcastUtxoSpend` path) + SC ledger entries; liquidity limits become gameplay. The abstract bottomless-exchange alternative is rejected (bypasses the casino and the chain, clashes with P7); revisit only if casino inventory proves unplayably thin in practice.
- **Spread/fee**: casino quotes `price × (1 ± spread)`; spread dev-configurable (e.g. default 2%), lives with the casino's other knobs in `CasinoGamblingFinances`. Sits beside — never replaces — the on-chain network fee (post-activation).
- **Atomicity**: SC leg settles instantly (service mutation), BTC leg enters the mempool and confirms at the next block — historically honest, and the revert-to-last-block model already keeps both legs consistent across a restart (an unconfirmed swap fully unwinds together with its SC leg on restart, because *nothing* between blocks persists). Same-block guarantee is unnecessary.
- **Halt days (D-13.11)**: `IsHaltDay` ⇒ swap desk closed, last price shown greyed with the historical reason — the June 2011 Mt. Gox hack closes the desk *in the simulacrum on the real anniversary*, free drama.
- **Beyond 2025-12-31 (D-13.5 — LOCKED: freeze at last price + "post-history era" label)**; seeded synthetic continuation deferred (§9.2); closing the desk rejected as hostile. Explicitly the plan's most-deferred concern.
- **Where the swap UI lives (D-13.6 — LOCKED: new scene `Screens/BtcSwap/`)** reachable from MainMenu + ScFinances — it will grow (order form, price readout, history); a ScFinances section was rejected to avoid re-bloating the hub Step 12 just organized.

---

## 5. Naming proposals

| Thing | Proposed | Alternatives | Notes |
|---|---|---|---|
| Timeline flag/offset holder | **`TimelineConfig`** (static, `Scripts/Services/`) | `GameTimeline`, `WorldTimeline` | Not a Node — pure constants, like `NetworkFeePolicy`. |
| Timeline stamp file | **`user://world_timeline.stamp`** | fold into `world_format.stamp` content | Separate file keeps the format-version mechanism untouched. |
| Market data autoload | **`BtcMarketDataService`** (autoload #14) | `MarketDataService`, `BtcPriceService` | "Btc" prefix leaves room for other assets later. |
| Day record | **`MarketDay`** | `BtcMarketDay`, `PriceDay` | |
| Swap scene (later phase) | **`BtcSwap`** (`Screens/BtcSwap/`) | `CoinSwap`, `ExchangeDesk` | OQ-13.6 decides if it's a scene at all. |
| CSV asset | `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv` | — | Already in the working tree (untracked). |

---

## 6. Phase checklist (proposed)

Ordering rationale: TL.0/MD.0/MD.1 are canon-safe and land first (they're `main`-mergeable at any time); the flag flip is the **last** switch before swap work begins, and the **first** thing reverted when it ends.

### Phase TL.0 — `TimelineConfig` + anchor refactor (canon-safe, zero behavior change) ✅ COMPLETE (2026-07-05)
- [x] Add `TimelineConfig` (`DevAltTimeline = false`, `Offset = Zero`, `Tag = CANON…`). New file `Scripts/Services/TimelineConfig.cs`.
- [x] Route the 7 anchor sites (§3.2 table) through `TimelineConfig.Shift(...)` / offset-aware ms constants. `LegacyStartLocal` untouched. Sites: `BlockchainService.GenesisTimestampUnixMs`, `CalendarTimeService.GameStartLocal`, `HistoricalBootstrapService.PlayerStartDayLocal`/`HalBlockDatesLocal`/`E4DateLocal`, `FoundersMiningService.SatoshiEarliestDisappearance`/`HalDecayStart`/`HalDecayEnd`, `HistoricalEventScheduler.HearnDealDateMs`. `PlayerStartDayLocal` and `HalDecayStart` both now read `TimelineConfig.PlayerStartDayLocal` (single shared anchor, per §3.2's footnote) instead of two independent literals.
- [x] `NetworkFeePolicy` routes through the D-13.9 special case (`TimelineConfig.FeeActivationLocal` = `DevAltTimeline ? PlayerStartDayLocal : 2009-04-26`) — canon value unchanged while the flag is false. Side-fix: `ActivationDateMs`'s `DateTimeOffset(dt, TimeSpan.Zero)` conversion now strips the dt's Kind to `Unspecified` first — `TimelineConfig`'s dates carry `DateTimeKind.Local` (the original literal was bare `Unspecified`), and `DateTimeOffset(Local dt, TimeSpan.Zero)` throws unless the machine's real UTC offset is zero. Only matters once `DevAltTimeline` flips true (TL.2); inert today.
- [x] Verify canon world is bit-identical: all `Shift()` calls add `TimeSpan.Zero` while `DevAltTimeline == false`, so every shifted constant has identical `Ticks` to its pre-refactor literal (reasoned through by inspection — `DateTime`/`DateTimeOffset` arithmetic, not zone conversion). `dotnet build` succeeds with 0 warnings/errors. Full in-editor bootstrap replay left to the developer's verification pass (no test framework configured yet — see CLAUDE.md "Testing").

### Phase TL.1 — timeline stamp + generalized clean-reset guard (canon-safe) ✅ COMPLETE (2026-07-05)
- [x] `ResetWorldIfFormatChanged` → `ResetWorldIfIncompatible` (format version **or** timeline tag mismatch ⇒ reset ⇒ re-stamp both). New stamp `user://world_timeline.stamp` holding `TimelineConfig.Tag`, read/written beside `world_format_version.txt` in `NetworkRoot.cs`.
- [x] Extend the delete list to the full Step-11/12 state set + bet-history chunks for BOTH trigger paths (§3.4, D-13.7): added `casino_sc_balance_state.json`, `player_bank_account_state.json`, `casino_client_ledger.json`, `bet_history.jsonl` + its `bet_history_*.jsonl` chunks (previously spared as "cosmetic" — no longer, per D-13.7's reasoning about polluting an alt world's stat scopes).
- [x] Refinement beyond the plan's literal wording: a **missing** timeline stamp (upgrading an existing pre-TL.1 save, which by definition predates the alt timeline ever existing) is treated as compatible and silently backfilled, NOT as a mismatch — so landing this phase doesn't itself wipe a developer's in-progress canon playthrough the moment the stamp file is introduced. Only an actual differing tag (i.e. `DevAltTimeline` flipped on the branch, or reverted back) triggers the reset.
- [x] `dotnet build` succeeds, 0 warnings/errors. Test (fake a stale tag ⇒ clean re-bootstrap; matching tag ⇒ untouched) reasoned through by code inspection for all cases (first-ever launch, pre-TL.1 upgrade, canon↔alt switch both directions) — actual in-editor exercise of the reset path left to the developer's verification pass, same caveat as TL.0 (no test framework configured).

### Phase MD.0 — dataset commit + provenance ✅ COMPLETE (already done, confirmed 2026-07-05)
- [x] Developer commits `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv` (manual, per git policy) — was committed in `f962866` (the plan's own commit), before Step 13 implementation began.
- [x] Provenance/caveats documented (this plan §1 is the source of truth; ProjectDesignManual chapter deferred to the Step-13 completion docs pass, TL.3).

### Phase MD.1 — `BtcMarketDataService` (canon-safe) ✅ COMPLETE (2026-07-05)
- [x] Autoload #14 registered in `project.godot`; CSV loaded once (`_Ready`) into an array indexed by day-number (`DayIndex`, O(1)); API of §4.1; `MarketDayChanged` event fired from `_Process` on a single cached-date comparison. New file `Scripts/Services/BtcMarketDataService.cs`.
- [x] Null-safe parsing (blank ≠ 0), InvariantCulture, halt-day carry-forward (precomputed `_effectivePriceUsd[]` array at load time, not a per-call walk), pre-2010 nulls (`GetEffectivePriceUsd` returns `null` before `FirstDataDateLocal`), post-2025 freeze (D-13.5 — clamps to the last index).
- [x] /100 fractal accessors (D-13.10): `GetGameVolumeBtc` / `GetGameNumTrades` (÷100, floor-to-1 via `max(1, round(raw/100))`); raw `MarketDay` fields reserved for DEV/provenance readouts.
- [x] DEV smoke readout: `GD.Print` on every `MarketDayChanged` (date + price + source), plus a one-time load-summary print (`Loaded 5646 market days (2010-07-18 → 2025-12-31)`).
- [x] Two minor, deliberate API deviations from the plan's §4.1 sketch (nullable-safety, not behavior changes): `TryGetDay(DateTime, out MarketDay? day)` (nullable `out`, since C# `#nullable enable` flags a non-nullable `out` that can go unset on the `false` path) and `event Action<MarketDay?> MarketDayChanged` (nullable payload — fires even when the newly-crossed day falls outside the dataset, e.g. still-canon pre-2010-07-18 play, with a `null` payload so subscribers can distinguish "day changed, still no market" from "day changed, here's the data").
- [x] **Side-finding, fixed**: `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv.import` had Godot's default `csv_translation` importer silently treating the dataset as a *localization* Translation resource (any `.csv` in a Godot project is auto-imported that way; the column headers `price_usd`/`volume_btc`/`num_trades`/`source` were being read as bogus "locale codes"). This had already produced 4 committed ~200KB binary `.translation` files (`f962866`) that were pure dead weight and would keep regenerating on every reimport. Fixed the `.import` override to `importer="keep"` (verified via an actual headless Godot reimport pass — `Godot_v4.5.1-stable_mono_win64.exe --headless --editor --quit`, clean, no errors) and deleted the 4 orphaned `.translation` files. The CSV itself was never corrupted — this only affected the derived/committed junk resources, not the source data — but left unfixed it would have kept polluting the repo and, more importantly, is exactly the kind of thing that silently breaks a `res://` data-asset pattern in a Godot project.
- [x] Smoke-verified via `Godot.exe --headless --path . --quit-after 30`: loads exactly 5,646 rows, `FirstDataDateLocal = 2010-07-18`, `LastDataDateLocal = 2025-12-31`; canon clock (still 2009-03-21, `DevAltTimeline = false`) correctly reports "no market data yet". Spot-checked the §7 testing-checklist rows directly against the CSV (2011-06-19/22 halt-day carry-forward, 2013-11-29 /100 volume, 2010-07-18 floor-to-1 trades, 2025-12-31 freeze target) by inspection against the implemented formulas — all match.

### Phase MD.2 — minimal player-visible surfacing ✅ COMPLETE (2026-07-05)
- [x] BTCWallet valuation line (D-13.3-a): new `%BaseValuationLabel` under the balance, `≈ {SC} SC @ {price} SC/BTC` via `GetEffectivePriceUsd`, refreshed alongside the existing 2s balance-refresh timer.
- [x] StatusBar ticker (D-13.3-b): new compact `BTC {price}` cell, refreshed ONLY on `BtcMarketDataService.MarketDayChanged` (not per-frame); greyed `BTC HALT` on halt days; `BTC —` before market birth or with no data. Subscribed/unsubscribed in `_Ready`/`_ExitTree`.
- [x] ScFinances dual-mode BTC label + toggle (D-13.3-c): new `%IncludeBtcToggle` + `%BtcValueLabel` — ON → `Total Net Worth (incl. BTC)`, OFF → `BTC holdings value` alone, always SC-denominated. The existing SC-only `NetWorthLabel`/`OverallPlLabel` (the actual game-over metric, D-SF2.1, untouched) got the visual-weight upgrade: font size 24→32 plus an explicit "— game-over metric" tag in their text, so it's unambiguous which figure the toggle does NOT affect.
- [x] Locked-state message before 2010-07-18 (canon worlds): BTCWallet and ScFinances both show "— no market price yet (first exchange opens 18 Jul 2010)" (ScFinances additionally still shows the raw BTC amount held); StatusBar shows `BTC —`.
- [x] Side-fix while in `StatusBar.cs`: the pre-existing Main Balance/Bankroll/clock labels used a raw `:F2`/format-specifier interpolation with no `CultureInfo.InvariantCulture` — exactly the bug CLAUDE.md's Number-locale rule warns about, and visibly live on this dev machine (comma-decimal output seen in this session's own console logs). Fixed to `string.Create(CultureInfo.InvariantCulture, …)` while touching the file for the new ticker; not otherwise in scope for MD.2 but a one-line-per-label fix directly adjacent to new code in the same file.
- [x] `dotnet build`: 0 warnings, 0 errors.
- [x] **Verification caveat**: did NOT smoke-test by launching the game this time (see incident note below) — verified only by build + code inspection. Needs your in-editor check of BTCWallet, StatusBar (visible on every screen), and ScFinances before commit.
- [x] **Incident, disclosed and resolved**: an initial verification attempt launched Godot headless with an explicit scene override (`res://Screens/BTCWallet/BTCWallet.tscn`), wrongly assumed to run isolated — it actually hit the real `user://` save directory and reset it to a fresh pre-genesis world (every save file overwritten). You re-launched and re-verified yourself; no code was affected, only local playtest state. Noted in assistant memory to never headless-launch the actual game again for smoke-testing.

### Phase TL.2 — the simulacrum flip (BRANCH-ONLY — never merges to `main` as `true`) ✅ MOSTLY COMPLETE (2026-07-05) — see caveats
- [x] On the swaps branch: `DevAltTimeline = true` (`TimelineConfig.cs`).
- [x] Permanent visible DEV watermark added: `StatusBar` now prepends a red `[ALT-TIMELINE DEV]` label (leftmost, font size 24) whenever `TimelineConfig.DevAltTimeline` is true — visible on every screen that embeds StatusBar. User-confirmed visible after relaunch.
- [x] §3.5 acceptance criteria — **verified empirically** by relaunching and reading `godot.log` + save state (not launched by the assistant, per the TL.2 handoff note):
  - `[NetworkRoot] World reset triggered (format 2 → 2, timeline 'CANON-2009-01-03' → 'ALT-2010-07-18')` — confirms the TL.1 guard's timeline-mismatch path fires correctly (format unchanged, timeline changed, single shared delete list).
  - `[HistoricalBootstrap] First launch — mined genesis → 2010-07-18 04:51:03. Satoshi 110 blocks, Hal 3 blocks. E4 (10 BTC Satoshi→Hal): on-chain.` — Hal exactly 3 ✓, total ≈113 blocks ✓ (110+3, vs canon's typical 111+3 — jitter-driven, both land on "~113"), E4 on-chain ✓, landing timestamp 2010-07-18 04:51:03 ≥ 2010-07-18 00:00 local ✓.
  - `calendar_state.json` ticks decode to **2010-07-18 04:51:03** — exactly equal to the bootstrap's own landing timestamp (§24.9 rule holds unmodified under the offset) ✓.
  - `[BtcMarketDataService] Day changed → 2010-07-18 price=0.0678842 source=mtgox` — live price on landing day ✓ (matches the plan's "$0.0679" target exactly).
  - Offset math independently re-verified: `2009-03-21 + 484 days = 2010-07-18` ✓.
- [x] Fees active from landing day (D-13.9) — **verified by code inspection + the timestamp evidence above**: `TimelineConfig.FeeActivationLocal` = `PlayerStartDayLocal` = 2010-07-18 (midnight local) when `DevAltTimeline` is true; the landing instant (04:51:03 same day) is already ≥ that gate, so the player's first live send is fee-active from block one, while the bootstrap era (which only ever creates fee-exempt scripted/coinbase transactions) is unaffected. Not yet observed on an ACTUAL live send (no player-era block mined yet in this smoke run — see caveat).
- [ ] **Caveat — not yet exercised (needs extended in-game play, not just a launch-and-look)**: Hearn round-trip in the fee era (§3.6 check, alt date 2010-08-15) and the founder-arc long-run pacing vs `founders_trace.csv` (Hal→0 by 2010-12-06, Satoshi→11,000 BTC by 2012-08-22) both require real player-era time advancement (weeks/months of in-game time via betting or DevTimeScale) past the landing instant — nothing to check yet since `founders_trace.csv` doesn't exist until the first player-era block is mined. Structurally these ride on the same `TimelineConfig.Shift` already verified for the bootstrap anchors, so no separate bug is expected, but they haven't been empirically observed. Natural to confirm during Phase SW.* once there's real play against the live alt world; flagging so it isn't silently assumed done.

### Phase SW.\* — swap tooling (separate design round) ✅ COMPLETE (2026-07-07)
- [x] Developed on the branch against the simulacrum; framing already locked by D-13.2/13.4/13.6 (§4.3). **Design round opened (2026-07-06): see `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md`** — scene name amended to `CasinoCoinSwaps` (supersedes the `BtcSwap` placeholder in D-13.6/§5), covering the two swap panels, per-asset DEV reserve controls, availability gating, clamps, the 10% rank-ready fee model, and ledger integration. Note: that plan retires the §4.3 spread knob in favor of the single fee (its OQ-SW.9). **Implemented through SW.6 (2026-07-07), each phase individually playtested and confirmed working by the developer**: `CasinoCoinSwapService` (autoload #15) + the `CasinoCoinSwaps` scene, both swap panels executing real SC↔BTC trades, pool-payout-aware BTC availability, the SC auto-floor (R2), and the CLAUDE.md/GLOSSARY/ProjectDesignManual docs pass — see that plan's own phase log for full detail. Only its §11 deferred items (bot swap participation, the auto-reserve-scheduler, rank-based fee tiers, era-aware network fees, R3) remain open, all explicitly out of scope for SW.\*.

### Phase TL.3 — exit the simulacrum (merge-back gate) ✅ COMPLETE (2026-07-07) — canon relaunch verification = developer's in-editor step
- [x] `DevAltTimeline` → `false` (`TimelineConfig.cs`); `Tag` reverts to `CANON-2009-01-03` by construction (it derives from the flag). The wipe itself (guard clears the alt world ⇒ pristine canon bootstrap) fires on the developer's **next in-editor launch** — the assistant never launches the game (MD.2 incident rule). Expect the log line `[NetworkRoot] World reset triggered (… timeline 'ALT-2010-07-18' → 'CANON-2009-01-03')` followed by a fresh canon bootstrap (genesis 2009-01-03, landing 21 Mar 2009).
- [x] Canon relaunch verified by the developer (2026-07-07): guard fired, world wiped, pristine canon bootstrap, balances reset. **Incident found during this verification — the wipe was INCOMPLETE**: hardware credits, bot casino-pool shares, and the casino-pool ledger survived (so the casino kept mining like in the simulacrum), because (a) `hardware_allocation.json` / `casino_pool_state.json` were never on the D-13.7 delete list and `casino_coin_swap_state.json` was created after the list was written, and (b) an **ordering hole**: `CalendarTimeService` (an early autoload) loads hardware/pool state via `WalletInitializationService.EnsureAll()` BEFORE `NetworkRoot` ran the guard, so even listed files would have survived in their static caches (checkpoint-covered services were masked by the pre-genesis in-memory reset). **Fixed same day**: the three files + the three DEV trace CSVs added to the delete list, and new **`WorldGuardService`** (autoload #1 — FIRST in `project.godot`, #16 overall) runs `NetworkRoot.RunWorldCompatibilityGuard()` before any other autoload loads state; the `EnsureInitialized` call site stays as an idempotent safety net. Docs updated (CLAUDE.md 16-autoload count + Important Pattern 2 third question; ProjectDesignManual §35.1). NOTE: the developer's CURRENT canon world still carries the already-leaked hardware/pool state (tags match now, no reset will fire) — one manual `user://` folder delete clears it.
- [x] Re-verify swaps in canon (developer, in-editor): desk locked at player start ("no exchange exists yet" state), unlocks when the clock crosses 2010-07-18 (fast-forward via DevTimeScale). The only TL.3 item left open — everything mechanical is done.
- [x] Docs truth pass (2026-07-07): **CLAUDE.md** — `BtcMarketDataService` autoload #14 subsection added; **BTC/SC trading unlock (2010-07-18, data-driven)** + the **timeline guard** added to the Canonical Decisions table; Step 13 MD/TL entry added to Implemented; block-tx-cap "planned" label corrected to implemented (per §1.6 ✅); the answered open question ("When exactly should BTC trading unlock…") removed; stale "inclusive" swap-fee wording corrected to the additive D-SW.11 model. **ProjectDesignManual** — Ch. 35 (the simulacrum re-mount / new-bootstrap design guide), written while the alt world still existed to verify against. **GLOSSARY** — *Market Birth / Trading Unlock*, *Market Day*, *Market Halt Day*, *Simulacrum (DEV Alt-Timeline)* added (the Swap terms landed with SW.6). This plan → **COMPLETE**.

---

## 7. Testing checklist (beyond per-phase items)

- [ ] Canon regression after TL.0/TL.1: fresh world ⇒ genesis 2009-01-03, landing 21 Mar 2009, E4 on-chain ~12 Jan, fees activate 2009-04-26 — all unchanged.
- [ ] Timeline switch both directions wipes everything listed in §3.4 (esp.: no 2009-dated bet history visible in an alt world's stats scopes).
- [ ] `BtcMarketDataService`: 2010-07-17 ⇒ null price; 2010-07-18 ⇒ 0.0679; halt day 2011-06-22 ⇒ `IsHaltDay` + carry-forward = 2011-06-19's price; 2026-01-01 ⇒ frozen last price (D-13.5); seam days (2014-02-02, 2017-08-17) parse with their `source` intact.
- [ ] /100 accessors (D-13.10): 2013-11-29 raw volume 38,295.19566404 ⇒ game 382.95195664; 2010-07-18 raw 5 trades ⇒ game 1 (floor-to-1); raw values still exposed for DEV readouts only.
- [ ] Day-change event fires exactly once per crossed day at high DevTimeScale (9000X ⇒ a day every ~9.6 real seconds — no missed/duplicate days when multiple days pass between frames… **note**: at 9000X, `_Process` can jump *multiple* days in one frame; the service must emit per crossed day or at least the latest day, decide in implementation).
- [ ] Restart mid-alt-session pre-first-block ⇒ resets to the 2010-07-18 landing instant (pre-genesis rule intact under offset).

---

## 8. Open questions — round 1 RESOLVED (2026-07-05)

All eight round-1 OQs were answered by the user and are locked as **D-13.1 … D-13.8** in §0.5, together with the two new decisions that surfaced in the same round (**D-13.9** fees-from-landing, **D-13.10** /100 fractal scaling). Remaining open items — all implementation-time checks or deferred designs, none blocking the branch:

- ~~Block tx cap 24 vs 48~~ — **RESOLVED** (§1.6 ✅): the implemented value is 24 (`BlockTemplateBuilder.MaxBlockTransactions`, coinbase included); "48" was a misrecollection. Side-finding: CLAUDE.md still labels the cap "planned" — fix in the docs pass.
- **Hearn-in-fee-era mechanics** (§3.6) — verified at TL.2; an implementation check, not a design question.
- **Deferred designs** — §9.

---

## 9. Future / deferred (documented only — NOT in this plan's scope)

### 9.1 Player-selectable entry-point bootstraps (user idea, 2026-07-05 — explore in a FUTURE plan)

The alt-timeline machinery proves that bootstraps are cheap to retarget — which unlocks a genuine *player feature* for later: offering **different entry experiences** at new-game time. The canonical default start remains **21 Mar 2009** forever, but a future version could offer e.g. "Mt. Gox era (Jul 2010)", "the first bubble (2013)", each with its own **reality-faithful bootstrap**. The fundamental difference from this plan's DEV simulacrum — and the reason this section exists so nobody conflates them: those bootstraps would all keep **genesis at 3 Jan 2009** and fast-build the *real intervening history* (chain, founders, difficulty, prices) up to the chosen entry day, producing **canon-compatible worlds**; the §3 simulacrum instead *moves genesis itself* and produces a throwaway world. To explore when its plan comes: bootstrap runtime at thousands of blocks, what "compressed history" means for bots/difficulty/UTXO spread, per-entry starting balances, save identity, achievements interaction. **Not during Step 13** (user, round 1).

### 9.2 Other deferred items

- Per-day swap liquidity caps derived from `GetGameVolumeBtc` (D-13.8) — revisit during/after SW.\*.
- Synthetic intraday price walk (D-13.2 tail); synthetic post-2025 continuation (D-13.5 tail — explicitly the least urgent item in the plan).
- Price history chart surface (D-13.3 tail).

---

*Round-1 locked. Next: the user cuts the feature branch; TL.0 → TL.1 → MD.0 → MD.1 → MD.2 land canon-safe; TL.2 flips the simulacrum on; SW.\* gets its detailed design round against the live alt world; TL.3 exits the simulacrum and merges back.*
