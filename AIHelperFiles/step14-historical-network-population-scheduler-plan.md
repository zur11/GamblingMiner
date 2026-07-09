# Historical Network Population Scheduler — Miners & Transaction Rates (Step 14) — Design Plan

**Status**: v2 (2026-07-07) — **rounds 1–2 LOCKED** (§0.5, D-14.1 … D-14.12); design fully specified (§3), no open questions remain. Implementation not started; ready for the feature branch (developer cuts it and makes the first commit).

**Scope (intent statement)**: a scheduler that **populates the blockchain with miners over time** and **defines the rate of automated transactions**, so the simulated network reproduces the historical behavior of the real Bitcoin network as far as the data allows. This is the systematization of what today is hardcoded: 4 fixed miner bots (`bot_1..4`), the referral-auction drip for non-miners (`NonMinerIntroIntervalMs` = one every ~2 in-game days), and per-block coin-recirculation probability (`BotSendProbabilityPerBlock = 0.5`). Instead of constants, these should follow historical curves.

**Companion docs**: `AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md` §1 (the price-dataset acquisition — the template this round follows) · `AIHelperFiles/scheduled-bot-transactions-plan.md` (current bot tx behavior) · `AIHelperFiles/btc-pools-hardware-plan.md` + `Documentation/ProjectDesignManual.md` Ch. 26 (difficulty regulator — the consumer of "network power") · Ch. 27 (hardware credits/pools) · CLAUDE.md "Open Design Questions" (the Network Fee Market Simulation research note — §2.1's fee metrics are its Option A input, a free synergy of this step).

---

## 0.5 Decisions locked (rounds 1–2 — 2026-07-07)

| # | Decision |
|---|---|
| **D-14.1** | **Hybrid fidelity (OQ-14.1-c)**: a visible bot-miner cast that grows over time (population replay) + an invisible aggregate mass carrying the rest of the network's power. The population scale is a **new, dedicated fractal factor** — it may be (and is, see §3.1) even more reduced than /100, but **once decided it is frozen** and applied consistently. Anchoring principle (user): fix the scale at the historical maximum and derive every other point relative to that maximum — realized as the log-decade model in §3.1 (P-14.A). |
| **D-14.2** | **Transaction realization is split by role, real sends only, NO synthetic filler.** Miners: their natural BTC sell-flow — re-pace the existing miner→non-miner donation system to the historical curve (miners selling BTC is the natural outflow). Non-miners: a NEW exchange scheduler moving BTC among active non-miner holders, paced from the dataset as far as real balances allow. Under-shooting the historical rate when balances can't sustain it is accepted; fabricating economy is not. |
| **D-14.3** | Cutoff **2025-12-31** with the same freeze semantics as the price dataset (D-13.5); extending the series is **deferred until after Basic Mode v0.1**. |
| **D-14.4** | Hashrate → power conversion is **normalized, era-agnostic**: "total power at player start = the bootstrap's ~6 participants," relative growth from there (log-domain compression, §3.1). The per-era hardware table (CPU/GPU/FPGA/ASIC) is recorded as a **pending proposal for after Basic Mode v0.1** (§9.1). |
| **D-14.5** | **Pool history is out of scope** — the casino pool keeps its current always-available behavior; a future player-created pool will also ignore pool history. Other/competing pools may later follow the historical rhythm on the fractal base — future plan (§9.2). |
| **D-14.6** | **Coin Metrics (Option A of §2) locked as the primary source; fee columns fetched NOW** (zero marginal cost). **Fees join `price_usd` in the fractal exemption** (user decree): fee values are never /100-scaled — they replay at face value, which is what makes the future fee-replay trivial. **The Network Fee Market question (CLAUDE.md / PRIVATE_ROADMAP §5) is direction-locked: Option A (historical fee replay) is the chosen mechanism; Option B (reactive fee market from our own simulated congestion) is retained as a FUTURE validation experiment** — if a reactive market later reproduces a curve similar to the replayed one, that confirms the population/volume simulation of this very plan was built right. (Supersedes the roadmap's pre-decision working hypothesis, which leaned B-first.) |
| **D-14.7** | **Time-shiftable from day one**: every lookup keys off the game clock's local date (which any `TimelineConfig` offset already shifts — the `BtcMarketDataService` precedent); every calendar anchor the scheduler needs routes through `TimelineConfig.Shift()`; scheduler targets are pure `date → target` functions so a future alt-timeline bootstrap can fast-build population history by calling the same functions (ProjectDesignManual Ch. 35 interplay). |
| **D-14.8** | *(round 2)* **The player-share anchor — replaces the free `GrowthFactor` knob**: total network power is sized so that **a player wielding the era-standard resources always holds one visible-cast member's share** — at the historical maximum, `1/28 ≈ 3.6%` of the base block distribution, in every era. Maximal, intelligent use of resources beats the baseline somewhat, never by much; careless resource use falls below it easily. "Pools become mandatory late-game" confirmed as the intended pressure — pool adaptation/competition and any mining-power rebalancing are **deferred, to be designed ON TOP of this step** (§9.6). Realization: §3.1's derived `TotalNetworkUnits` (era-standard power × cast size); the ~28-at-max cast (`CastPerDecade = 2`) is locked by this same arithmetic. |
| **D-14.9** | *(round 2)* Invisible-mass blocks are attributed to **rotating ghost miner names** (small curated pseudonym pool, one-off addresses) — assistant recommendation accepted. |
| **D-14.10** | *(round 2)* **The historical early-era quietness is accepted** — no gameplay floor for automated txs; the founders' scripted events are the early flavor. |
| **D-14.11** | *(round 2)* Invisible-mass BTC is **frozen forever in v1** (retired-Satoshi precedent); recirculation reconsidered only if ND.4 calibration shows the visible economy BTC-starved. |
| **D-14.12** | *(round 2)* Visible cast is **spawn-only in v1**; churn/retirement (the GPU-extinction realism) deferred (§9.5). |

---

## 1. What data the scheduler actually needs (metric → design purpose)

The sim is a **1:100 fractal replica** (D-13.10 precedent: raw real-world data in the asset, `/100` scaling at the service boundary, prices exempt). Whatever source we pick must supply, per **day**, from **2009-01-03** onward:

| # | Metric (daily) | What it drives in-game | Notes |
|---|---|---|---|
| M1 | **Transaction count** (`TxCnt`) | The automated-transaction rate: how many bot/filler txs per day the network should produce (÷100) | The single most important series. Real blocks carry ~2,000–3,000 txs today; our cap is 24/block — the /100 rule already fits (step13 plan §1.6) |
| M2 | **Hashrate** (or difficulty) | Total network mining power over time → how many "participant power units" the bot population must sum to (the difficulty regulator's `anchor = InitialDifficulty × power` already consumes exactly this shape via `SimulationService.SetActiveMiningPower`) | Hashrate must be translated into *participants' worth of power* (like `FoundersMiningService`'s `P = 1.0` unit) using era-typical hardware — that conversion is round-1 design work, but the raw series is the input |
| M3 | **Miner / pool attribution** | How many *distinct* miners exist and how concentrated they are → bot-miner population count, pool era onset (our casino pool ≈ Slush pool, born Dec 2010) | No API gives "number of miners" directly; per-block attribution (coinbase tags) is the measurable proxy, only meaningful from ~2011 (pool era). The 2009–2010 era needs research-based estimates (§2.5) |
| M4 | **Active addresses** (`AdrActCnt`) | Non-miner user population → pace of the referral-auction drip / holder bots | Optional but cheap if the source bundles it |
| M5 | **Block count per day** | Cross-check series (our block pace is fixed by the difficulty regulator; useful for validation only) | Free from any of the sources |
| M6 | **Fees (mean/median per tx, native units)** | NOT this plan's deliverable — but fetching it in the same pipeline feeds the flagged "Network Fee Market Simulation" research (its Option A = historical fee replay) for free | Decide in round 1 whether to include the column now (§4 OQ-14.6) |

---

## 2. Options for obtaining the historical data

All four candidates below were **empirically verified alive on 2026-07-07** (actual API responses inspected, not assumed) — the same discipline as the Step-13 round, where CoinGecko turned out to be paywalled and bitcoincharts dead (§2.6).

### 2.1 Option A — Coin Metrics Community API ⭐ (recommended primary)

- **What**: free, no-key REST API with curated daily metrics for BTC from genesis. One endpoint covers M1, M2, M4, M5, M6 in a single request family:
  `https://community-api.coinmetrics.io/v4/timeseries/asset-metrics?assets=btc&metrics=TxCnt,HashRate,DiffMean,AdrActCnt,BlkCnt,FeeMeanNtv,FeeMedNtv&frequency=1d&start_time=2009-01-03`
- **Verified (2026-07-07)**: returns valid JSON from `2009-01-03T00:00:00Z`; early days show `HashRate: null → 0.00000094…` and `TxCnt: 0`, exactly the expected newborn-network shape.
- **Pros**: everything daily in one place, genesis-complete, clean nulls (no fake zeros — matches our "blank ≠ 0" parsing rule), JSON/CSV output, documented metric methodology (it's the de-facto academic standard), trivially reproducible pipeline (one script, no scraping).
- **Cons / caveats**: response is **paged** (`page_size` max 10,000; ~6,400 days fits in 1–2 pages, follow `next_page_url`); community tier is rate-limited (fine for a one-shot build); **no miner attribution** (M3 must come from elsewhere); community metrics list should be re-checked at implementation time (paid-tier metrics occasionally rotate).

### 2.2 Option B — blockchain.com Charts API (cross-check / fallback)

- **What**: the veteran free charts API: `https://api.blockchain.info/charts/{chart}?timespan=all&sampled=false&format=json` with charts `n-transactions`, `hash-rate`, `difficulty`, `n-unique-addresses`, `transaction-fees`, `miners-revenue`, `avg-block-size`.
- **Verified (2026-07-07)**: `n-transactions?timespan=4weeks` returns valid `{x: unixTs, y: value}` series (status "ok").
- **Pros**: no key, dead-simple schema, series reach back to 2009; independent methodology → ideal **cross-check** against Option A (the step13 pipeline's "assert the join" habit, applied across providers).
- **Cons / caveats**: `timespan=all&sampled=false` must be confirmed to really return every day, unsampled (the default response is downsampled — flag for implementation); methodology is opaque (no docs on how e.g. hash-rate is derived); no miner attribution; the API has historically changed shape without notice.

### 2.3 Option C — mempool.space mining APIs (pool-era miner attribution)

- **What**: free REST APIs from the mempool.space open-source project: `https://mempool.space/api/v1/mining/hashrate/all` (network hashrate + difficulty history) and the `mining/pools/…` family (pool dominance / blocks-per-pool over time windows).
- **Verified (2026-07-07)**: `hashrate/all` returns `{timestamp, avgHashrate}` pairs starting **1231006505 = 2009-01-03** (genesis day) — full-history, genesis-complete.
- **Pros**: the only *API* candidate with **pool attribution** (M3) built in — pool share history is exactly "how many distinct big miners and how concentrated" for the pool era (2011+); hashrate series doubles as an Option-A cross-check; open-source (methodology inspectable).
- **Cons / caveats**: pool endpoints are organized around time windows (24h/3d/1w/…/all) rather than "per historical day" — extracting a *daily pool-count time series* may require the per-pool blocks endpoints or accepting coarser granularity (verify at implementation); pre-pool era (2009–2010) attribution is structurally impossible here (nobody tagged coinbases yet).

### 2.4 Option D — Blockchair daily block dumps (per-block granularity, heavyweight)

- **What**: free daily TSV dumps, one file per calendar day, every block that day with per-block fields **including `guessed_miner`** and `transaction_count`: `https://gz.blockchair.com/bitcoin/blocks/blockchair_bitcoin_blocks_YYYYMMDD.tsv.gz`.
- **Verified (2026-07-07)**: directory listing is live and free, files from **20090103** onward (467 bytes early, growing later).
- **Pros**: the *richest* option — per-block miner attribution means we can compute "distinct miners per day/week" ourselves (M3) for the whole chain, plus exact per-block tx counts; TSV is trivially aggregated to our daily CSV; this is the same "download the primary dump, aggregate locally" shape as the Mt. Gox leg of the price pipeline.
- **Cons / caveats**: ~6,400 file downloads (one per day) — a long, throttle-sensitive crawl (Blockchair rate-limits bulk access aggressively and asks bulk users for sponsorship; a polite, cached, resumable script is mandatory — same `%TEMP%` cache pattern as the Binance kline fetcher); `guessed_miner` is only as good as coinbase-tag heuristics (≈"Unknown" for most of 2009–2010, decent from 2011).

### 2.5 The 2009–2010 miner-count gap — research sources, not APIs (any option needs this)

No API can say how many *people* mined in 2009 — coinbases weren't tagged. If round 1 decides the scheduler needs an early-era miner-count curve (not just hashrate), it will be a small **hand-curated table** (the "13 halt days" precedent — deliberately curated, documented, never interpolated silently), built from published research: Sergio Demián Lerner's Patoshi work (attributes ~22k early blocks to one miner — we already model that as Satoshi's regulated ~10% share), the known early-miner lore (Hal's block 78, etc. — already embodied by our founders), and hashrate÷(era-typical hardware) estimates (CPU ~2–10 MH/s in 2009, GPU ~100–600 MH/s from mid-2010). Note the sim **already covers 2009 structurally**: the historical bootstrap + founders + 4 bots ARE the early network; the scheduler's real work starts where growth takes off (2010+), which is exactly where data quality improves.

### 2.6 Dead / rejected sources (carried over from Step 13 — do not re-litigate)

- **bitcoincharts.com** — dead (verified 2026-07-04; DNS resolves, nothing listens). Its Mt. Gox dump was recovered via the Wayback Machine for the price dataset; it has nothing for network metrics anyway.
- **CoinGecko** — free API returns error `10012` beyond the last 365 days (verified 2026-07-05); paid tiers still lack the network metrics we need. Rejected.

### 2.7 Recommendation — ✅ CONFIRMED as D-14.6 (round 1)

**Primary: Option A (Coin Metrics)** for the daily M1/M2/M4/M5/M6 backbone — one reproducible script, genesis-complete, clean nulls, **fee columns included**. **Option B (blockchain.com)** as the independent cross-check asserted during the build step. **M3 (miner attribution) turned out NOT to be needed**: D-14.1's hybrid drives the visible cast from the hashrate curve (§3.1), so Options C/D stay documented as fallbacks only — no pool/attribution fetch in this step (consistent with D-14.5 pool-history-out-of-scope). Early era per §2.5 only if round-2 calibration proves the hashrate-derived cast curve inadequate for 2009–2010 (not expected — the bootstrap already covers that era).

Proposed asset shape (mirroring `Data/HistoricalPrices/`): **`Data/HistoricalNetwork/btc_network_daily_2009_2025.csv`**, schema ≈ `date,tx_count,hashrate,difficulty,active_addresses,block_count,fee_mean_btc,source` — raw real-world values (CSV stays raw; `/100` and any hashrate→power-units conversion happen at the service boundary, D-13.10 precedent), `InvariantCulture`, blank = null never 0, construction scripts kept out of the repo (`C:\Users\PERSONAL\Desktop\Proof of Fun\Project out of repo data\`) with the same "assert the join / assert continuity, refuse to write otherwise" discipline as `Merge-BtcDailyHistory.ps1`.

---

## 3. The design

### 3.0 The engine insight everything rests on: shares matter, magnitude doesn't

Raw hashrate replay is neither executable nor meaningful in our engine. Real hashrate grows **~12 orders of magnitude** between player start (Mar 2009) and 2025 — bots physically perform nonce attempts (1 bet = 1 attempt), so nobody can "be" 10¹² participants; and the difficulty regulator (Ch. 26) **holds block pace at ~58,500 in-game s regardless of absolute power** (the LWMA feedback trims whatever the feed-forward anchor says). In-engine, absolute network power only manifests as (i) the difficulty number itself and (ii) **each miner's SHARE of blocks**. So the scheduler replays the network's **shape** — who exists, and what fraction of blocks each stratum wins — never its raw magnitude. This is also why D-14.4's "normalize to the bootstrap's ~6 participants" is the only coherent reading, and why the log-domain compression below is a legitimate *second* fractal reduction (explicitly allowed by D-14.1: a scale "even more reduced than /100," frozen once chosen).

### 3.1 P-14.A — the log-decade growth model (population + power, one curve)

Both hybrid layers are driven by one era-agnostic quantity derived from the dataset's hashrate series:

```
decades(date) = log10( H(date) / H(playerStartDate) )        // 0 at player start, ≈12 by 2025
```

- **Visible cast (population layer)** — real bot miners with real wallets:
  `targetVisibleMiners(date) = BaseCast + round(CastPerDecade × decades(date))`, `BaseCast = 4` (today's `bot_1..4`).
  New miners are introduced the way the referral auction already introduces non-miners (block-relative drip, `NetworkRoot` node registration + `BotWalletRegistry` identity), whenever the live count is below target; **spawn-only in v1** (D-14.12). **Choosing `CastPerDecade` IS choosing the user's "scale anchored at the historical maximum"**: max cast = `BaseCast + CastPerDecade × decades(2025) ≈ 4 + 12×k`. **`CastPerDecade = 2` ⇒ ~28 visible miners by 2025 — LOCKED by D-14.8's own arithmetic** (the 1/28 anchor presumes it; Block Explorer stays a readable cast, not a crowd).
- **Invisible mass (power layer)** — ONE aggregate pseudo-miner ("rest of the network"), built exactly like the founders (`FoundersMiningService` pattern: a pure power number fed to the difficulty regulator + nonce attempts drained in lockstep with player betting — a **concurrent miner, never a clock mover**). Its size is **derived from the D-14.8 player-share anchor, not free-knobbed** (the earlier draft's `GrowthFactor` is gone):

  ```
  EraStandardPower(date)  = MaxHardwareCredits ^ ( decades(date) / decades(datasetEnd) )
                            // 1 credit at player start → 100 (the Ch. 27 hardware cap) at the historical max
  TotalNetworkUnits(date) = EraStandardPower(date) × targetVisibleMiners(date)
  InvisibleUnits(date)    = TotalNetworkUnits − Σ(actual live powers: player + founders + visible cast + casino pool)
  ```

  Anchors check out at both ends: player start ⇒ `1 × ~6 ≈ 6` units (D-14.4 ✓); historical max ⇒ `100 × 28 = 2,800` units, so a player wielding the full 100-credit cap holds exactly `1/28 ≈ 3.6%` (D-14.8 ✓). Mid-era the same holds relatively: a player at the *era-standard* power holds one cast share; a player ahead of era hardware beats the baseline boundedly; under-used credits fall below it fast — exactly the intended pressure gradient.
  **Honest interplay note (P5)**: the "not much more than baseline" ceiling is enforced from the player's side by *era-gated hardware availability*, which is P5's job, not this scheduler's — until P5 ships, a player who hoards the full 100 credits in an early era can exceed the era baseline substantially. Accepted interim behavior; the network side (this plan) stays correct either way, and D-14.8 explicitly defers rebalancing to the later pool/power design (§9.6).
- Its mined blocks are handled as external blocks (founder precedent); coinbases go to **rotating ghost miner names** — a small curated pseudonym pool with one-off addresses, BTC frozen forever (D-14.9 + D-14.11). DEV surfacing: `user://logs/network_population_trace.csv` (one row per day-change/spawn/knob change — `founders_trace.csv` precedent); `CastPerDecade` stays dev-inspectable but is design-locked.

### 3.2 P-14.B — transaction pacing by FULLNESS PARITY (the /100 × /100 trap)

D-13.10's `/100` **must not** be applied to daily tx counts directly: our blocks-per-day is *already* /100 of real (2,100-block halvings ⇒ ~1.477 blocks/in-game-day vs. real ~144), so /100 on the daily count would overflow chain capacity by exactly 100×. The correct fractal target collapses both factors into one clean rule:

```
targetTxPerBlock(date) = ( realDailyTx(date) / realDailyBlocks(date) ) / 100     // real txs-per-block, /100
                          clamped to [0, 23]                                     // block cap minus coinbase
```

Sanity checks: 2009–2010 (real ~1–2 txs/block) ⇒ ~0 automated txs — historically faithful near-empty blocks; 2017+ (real ~2,000–4,000/block) ⇒ 20–40 ⇒ **clamps at 23 = the real "full blocks" era reproduced by the cap itself**. The early-era quietness (which *reduces* today's bot liveliness) is **accepted — D-14.10**: no gameplay floor; the founders' scripted events carry the early flavor.

**Realization (D-14.2, real sends only):** each mined block, the scheduler computes the target and tops up **automated** traffic toward it, never competing with organic traffic — player/casino/founder/swap txs count toward fullness first, automated senders fill only remaining capacity:
1. **Miner sell-flow** (existing system, re-paced): the miner→non-miner donation engine (`scheduled-bot-transactions`, warmup + 10–40% sends) replaces its flat `BotSendProbabilityPerBlock = 0.5` with a probability derived from `targetTxPerBlock`.
2. **Non-miner exchange scheduler** (new): fills the remainder by scheduling real UTXO sends **between active non-miner holders** (recipient/amount rules reuse the existing Min/MaxSendFraction shape; fees attach post-activation as everywhere). If balances can't sustain the target, it under-shoots — accepted (no filler); recirculation makes later targets more sustainable.

### 3.3 Dataset + services + scheduler (the concrete pieces)

- **Asset**: `Data/HistoricalNetwork/btc_network_daily_2009_2025.csv` — schema `date,tx_count,hashrate,difficulty,active_addresses,block_count,fee_mean_btc,fee_median_btc,source` (`source = coinmetrics`), raw real-world values, `InvariantCulture`, blank = null never 0, continuous-dates assertion. Built by one out-of-repo PS script (Coin Metrics, paged) + a second script cross-checking tx_count/hashrate against blockchain.com within tolerance (§2.2) — the "assert or refuse to write" discipline of `Merge-BtcDailyHistory.ps1`.
- **Loader**: new autoload **`BtcNetworkDataService`** (#17), mirroring `BtcMarketDataService` exactly (load once in `_Ready`, day-indexed array, O(1) lookups, day-change event; read-only over a static asset ⇒ no persistence, no checkpoint coverage, **no reset-list entry needed** — but re-ask CLAUDE.md Pattern 2's three questions at implementation for anything else this step persists). Raw fields DEV-only; gameplay consumes derived accessors: `GetDecades(date)`, `GetTargetVisibleMiners(date)`, `GetTotalNetworkUnits(date)`, `GetTargetTxPerBlock(date)` — all scaling at the service boundary (D-13.10 precedent), all pure `date → value` (D-14.7).
- **Scheduler**: **`NetworkPopulationScheduler`**, a pure static controller (`FoundersMiningService`/`HistoricalEventScheduler` pattern — no Godot/chain state of its own; chain-derived idempotent decisions), driven by `SimulationService`: per-frame → drain invisible-mass attempts; per-block → tx top-up scheduling; on day-change → population target check/spawn. Nothing persisted (block = the only commit; spawned bots' *identities* land in `BotWalletRegistry`, which is deliberately reset-spared like all identity files).
- **Fees**: columns ship in this dataset (D-14.6); making `NetworkFeePolicy` replay them is **its own future step** — this plan only guarantees the data is on disk and fractal-exempt when that step comes.

### 3.4 Phase checklist (proposed)

- **ND.0 — dataset build + cross-check + commit** ✅ BUILT (2026-07-07) — developer verification + commit pending:
  - [x] `Get-BtcNetworkDaily.ps1` (out-of-repo, beside the step13 scripts): Coin Metrics community v4, one paged request, **6,207 rows 2009-01-03 → 2025-12-31, zero date gaps** (hard continuity assertion passed; only 6 empty `hashrate` cells — the genesis-week days before hashrate is definable, correct nulls). Values stored as the API's own decimal strings **verbatim** (no reparse — invariant by construction).
  - [x] **Schema adapted to community-tier reality** (the plan's own "re-check the metrics list at implementation" caveat fired): `DiffMean`, `FeeMeanNtv`, `FeeMedNtv` are paid-tier. Final schema: `date,tx_count,hashrate,active_addresses,block_count,fee_total_btc,source` — the **difficulty column is dropped** (nothing in the design consumes it; hashrate is the driver) and fees ship as the **raw daily TOTAL** (`FeeTotNtv`) — strictly more raw than a mean; mean-per-tx = `fee_total_btc / tx_count` derived at the service boundary; median unavailable on free tiers, noted for the fee-replay step (§9.4).
  - [x] `Compare-BtcNetworkDaily.ps1` cross-check vs blockchain.com (`timespan=all&sampled=false` confirmed to return full unsampled history, 2009 → today): **PASS** — tx_count median rel-diff **0.81%** / p95 3.1% under the coinbase-included convention (n=6,190 overlapping days), hashrate shape (ratio-normalized @ 2016-01-01) median **0.00%** / p95 9.4% (daily-estimate noise). Side-finding proven empirically: **CM `TxCnt` EXCLUDES coinbase txs** (its raw comparison vs blockchain.com shows 61% p95 concentrated in the early coinbase-dominated era; adding `BlkCnt` collapses it to 0.81%) — exactly the non-coinbase numerator P-14.B's fullness parity wants; ND.1's `GetTargetTxPerBlock` must NOT subtract coinbase again.
  - [x] Asset placed at `Data/HistoricalNetwork/btc_network_daily_2009_2025.csv` (~477 KB) with `.csv.import` pinned to `importer="keep"` from day one (the MD.1 lesson; Godot may append a `uid` line on the next editor scan — commit whatever it writes).
  - [ ] Developer: verify + commit (CSV + `.csv.import` + the plan update). Scripts stay out-of-repo per policy.
- **ND.1 — `BtcNetworkDataService`** (canon-safe, zero behavior change): autoload #17, parsing rules, accessors, DEV load-summary print.
- **ND.2 — population/power layer**: invisible-mass aggregate into the regulator + drained external blocks; visible-cast spawner; both knobs + `network_population_trace.csv`; verify the §24.9 clock rule and pre-genesis reset stay untouched (they're chain-derived — nothing here changes them).
- **ND.3 — transaction layer**: donation re-pace + the non-miner exchange scheduler, top-up-toward-target per block, capacity-yield to organic traffic.
- **ND.4 — calibration playtest + docs pass**: verify the D-14.8 share anchors empirically across eras (DevTimeScale fast-forward: player at era-standard power ≈ one cast share; ~3.6% at the max era; blocks pace unchanged), check the visible economy isn't BTC-starved (the D-14.11 revisit trigger), then CLAUDE.md (autoload #17, scheduler section, canonical decisions), ProjectDesignManual chapter, GLOSSARY, this plan → COMPLETE.

---

## 4. Open questions

### 4.1 Round 1 — RESOLVED (2026-07-07)

All seven round-1 OQs were answered by the user and locked as **D-14.1 … D-14.7** in §0.5. Notable answer detail preserved verbatim in spirit: OQ-14.1 → hybrid, with the population scale anchored at the historical maximum and every other point derived relative to it (realized as P-14.A's log-decade curve — the assistant's proposed refinement of the same principle, since linear max-normalization of a 10¹²-range series would zero out everything before ~2023); OQ-14.2 → miners' flow = their natural sell-side donations, non-miners get a dedicated exchange scheduler, no synthetic txs; OQ-14.6 → fees fetched now, fee replay (Option A) locked as the mechanism with the reactive market (Option B) kept as a future *validation* experiment.

### 4.2 Round 2 — RESOLVED (2026-07-07)

All five round-2 OQs answered and locked as **D-14.8 … D-14.12** (§0.5). The pivotal answer, OQ-14.8, did more than calibrate: it **replaced the free `GrowthFactor` knob with a principled anchor** — "the player at era-standard resources always holds one visible-cast member's share (`1/28 ≈ 3.6%` at the historical max), can beat it somewhat with maximal smart play, and falls below it easily with careless play" — from which §3.1's `TotalNetworkUnits` derivation follows with no free parameter left. (Arithmetic note for the record: `1/28 = 0.0357 ≈ 3.6%`, i.e. the fraction 0.0357 — not 0.035%.) OQ-14.9–14.12 → assistant recommendations accepted verbatim: ghost-name pool with frozen BTC, historical quiet accepted, frozen invisible-mass BTC in v1, spawn-only cast in v1.

---

## 9. Future / deferred (documented only — NOT in this plan's scope)

- **9.1 Per-era hardware table** (D-14.4 tail): CPU/GPU/FPGA/ASIC-era conversion of hashrate → participant power, replacing the era-agnostic normalization — pending proposal for after Basic Mode v0.1.
- **9.2 Pool history** (D-14.5 tail): competing pools following the historical rhythm on the fractal base; the casino pool and the future player-created pool stay history-exempt by decision.
- **9.3 Post-2025 series extension** (D-14.3 tail): after Basic Mode v0.1, same regeneration path as the price dataset (§1.3 of the step13 plan: re-run with a later end date).
- **9.4 Fee replay implementation**: making `NetworkFeePolicy` consume `fee_mean_btc`/`fee_median_btc` — its own step; direction locked here (D-14.6), data delivered by ND.0.
- **9.5 Visible-cast churn/retirement** (D-14.12 tail — v1 ships spawn-only).
- **9.6 Pools & mining-power rebalancing on top of this step** (D-14.8 tail): the pool layer that resolves the late-game "pools become mandatory" pressure — pool adaptation, competition between pools, and any rebalancing of mining powers — is designed AFTER this scheduler exists, on top of it. Includes P5's era-gated hardware availability (the player-side enforcement of "not much more than baseline", §3.1's interplay note).

---

*Rounds 1–2 locked (2026-07-07) — no open questions remain; the design is fully specified up to implementation detail. Next: the developer cuts the feature branch and makes the first commit (this plan); then ND.0 (dataset) → ND.1 (loader) land canon-safe, ND.2–ND.3 build the two layers, ND.4 verifies the D-14.8 anchors empirically.*
