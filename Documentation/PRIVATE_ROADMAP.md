# Private Roadmap - GamblingMiner

Internal roadmap for design coherence, implementation order, and Basic Mode priorities.

All project files, public documentation, UI text, code names, and backend terminology should be in English. Spanish is reserved for AI chat and planning conversations outside the repository.

## 1. Current Read

### Strengths

- The core fantasy is strong: bets move time, every bet is a mining attempt, and bankroll management becomes the fuel for reaching Bitcoin history.
- The project already has a real playable foundation: Dice, manual betting, autobet, saved strategies, game time, per-bet nonce attempts, a block explorer, block checkpoints, and a bankroll program.
- Separating `Main Balance` from `Bankroll` is the right direction. It lets the player manage active risk without losing track of total reserves.
- The project has a clear educational angle: players can observe bot betting behavior, infer strategy parameters, and learn crypto-casino math through play.

### Main Risks

- Documentation currently mixes implemented systems, prototypes, and planned features too freely.
- Some numbers must be made canonical everywhere:
  - Initial funds: `40,000 SC` total in general docs.
  - Specific docs may explain this as `39,900 SC Main Balance + 100 SC Bankroll`.
  - Basic Mode halving interval: `2,100 blocks`, intentionally scaled to about four in-game years at roughly 1.5 blocks per in-game day (100X time scale).
- Legacy `Principal Balance` code-facing names should move toward `Main Balance` where reasonable.
- Bots must matter in Basic Mode, but the bot/non-node wallet and transaction system needs a coherent model before long-session testing.
- Casino finances are part of the simulation and need their own internal scene, even if the player should not have access to it later.
- **Persistence was sized for hand-play and is now driven by a simulator** (INC-001, 2026-07-29 — cost a P15.8 playtest session). The commit rule ("a block is the only commit") covers *when* to write and never covered *durability*: writes are not atomic, a corrupt snapshot loaded silently into an empty world, no store has a retention policy, and lifetime stats had been double-counting for an unknown period. Fixed at **P15.11**; the limits deliberately left open (snapshot cost linear in chain length, no persisted lifetime aggregate) are named in `ProjectDesignManual.md` §40.6.

## 2. Product Direction

GamblingMiner should become all three things at once:

- A casino incremental game.
- A Bitcoin mining simulator.
- A historical economic management game.

The minimum objective is survival, because time cannot advance without money. Beyond survival, the player chooses whether to optimize for BTC, SC, total net worth, achievements, or experimentation.

Basic Mode does not need a hard victory condition. It needs a sustainable loop, a few clear achievements, and enough stability for long sessions.

## 3. Basic Mode Definition

Basic Mode is the smallest closed version of the game where the central loop works without relying on full historical data, multiplayer, cloud saves, or multiple casino systems.

### Core Loop

1. The player starts with `40,000 SC` total funds.
2. Specific economy screens explain this as `Main Balance` plus `Bankroll`.
3. The player funds Bankroll manually or through optional auto-recharge.
4. The player bets manually or with autobet in Dice.
5. Each bet or each set of bets(with hardware) advances game time by one tick in the current time scale and performs exactly one mining nonce attempt per each bet.
6. Hardware increases bets/attempts per real second, but never accelerates time, just increases the posibility of mining a block sooner.
7. If a block is mined, BTC reward and block state are persisted through a checkpoint.
8. Bots can mine competing blocks and can win before the player.
9. The player can inspect blocks, BTC balances, recent bet history, and performance stadistics.
10. Game over happens only when `Main Balance + Bankroll` reaches zero.

### Basic Promise

"Bet to move time, mine with every attempt, protect your bankroll, and survive the early Bitcoin era."

## 4. Canonical Decisions

- Project language: English for all files, UI, code-facing names, and documentation.
- Public positioning for now: experimental prototype with a serious game design direction.
- General initial balance: `40,000 SC`.
- Specific initial split: `39,900 SC Main Balance + 100 SC Bankroll`.
- Preferred player-facing term: `Main Balance`.
- Game over: total SC depletion across Main Balance and Bankroll.
- Bankroll: subaccount of Main Balance, used for active betting risk.
- Auto-recharge: optional player automation, required infrastructure for continuously betting bots.
- Basic Mode halving: `2,100 blocks`, not Bitcoin's real `210,000` blocks. Total supply: `210,000 BTC` (50 BTC initial reward; converges to in-game year ~2141).
- Hardware: increases bets/nonce attempts per real second; it does not alter game-time speed.
- Hardware credits: each credit = 1 nonce attempt per bet; betting speed in DiceGame is locked to total credits owned across all pool assignments.
- Mining pools: hardware credits are assigned to either a node's individual pool (solo mining, full reward) or the casino community pool (shared mining, proportional reward minus casino fee).
- Casino community pool fee: dynamic; 30% at balanced power, up to 50% when casino pool dominates, down to 10% when individual pools dominate.
- Mining rule for now: `1 bet = 1 nonce attempt`.
- Bot mining: required in Basic Mode.
- BTC cannot be used directly for betting.
- Multiplayer, DLCs, multiple casinos, and cloud persistence are postponed until the core loop is fun and data volume requires more infrastructure.
- Founder entities (Step 7, implemented): `Satoshi` (dominant early miner; power-regulated to ~10% of blocks toward `11,000 BTC` ≈ 1% of his real ≈1.1 M; retires ≥ `2011-04-26`, then frozen), `Hal` (joins `2009-01-11`, 3 bootstrap blocks, then a `P=1.0` player-era drip fading to 0 by `9 Aug 2009`), `Mike Hearn` (joins ~April 2009, never mines, +82.51 BTC round-trip). Founders mine without needing SC/BTC, like the casino. Detail: `AIHelperFiles/step7-historical-character-economics-plan.md`.
- Game start: a first-launch bootstrap pre-mines the chain from genesis (`2009-01-03`) to `2009-03-21` (Satoshi + Hal only), so the player always begins on `21 March 2009`. From player start onward, in-game time always follows player bets — but the founders **mine concurrently in lockstep** with those bets (they add hashrate, never advance the clock themselves).
- Network-growth model: participants appear over time (`Satoshi → Hal → player → miner bots gradually`), not all at block 1. Autonomous (no-bet) mining happens only during the bootstrap window; reserved otherwise for future expansions/DLC/multiplayer.
- Coinbase recipients use derived `gm1q…` addresses (real base58 kept only as commented reference; genesis coinbase stays unspendable).
- Balance model: a **real multi-input/multi-output UTXO model** (Step 8 — done & audited). Balance = Σ unspent outputs; fee = Σin − Σout; one unified spend path with multi-input coin selection + change. **Address non-reuse** (a fresh address per receive) is **Satoshi-only**; player/casino/Hal/Hearn rotate change-on-send. NOTE: this is **address non-reuse**, *not* the "Patoshi pattern" (a mining-forensic fingerprint — D0).
- Block-candidate + hashrate model: the **keystone** shared by founder mining, hardware pools, and the block template builder. Minimal weighted-lottery first; full per-node template deferred to P4.
- Casino SC gambling finances (Step 11, implemented): the casino is a first-class economic actor with its own explicit SC balance sheet (`CasinoScBalanceService`, 99M Main Balance + 1M Bankroll = 100M total, a hypothetical bank loan), routed per settled **player** bet (`casinoDelta = -CreditedProfit`; bot routing deferred — OQ-11.1). Target-to-fill auto-recharge on Bankroll exhaustion; a 100M SC re-loan is the bankruptcy flavor event (no bet ever blocks). Per-client SC deposit/withdrawal history lives in `CasinoClientLedgerService`, distinguishing real deposits (`initial`/`deposit`) from internal Bankroll Auto-Recharge movements (`auto_recharge`, excluded from deposit totals and the since-last-deposit baseline) and `withdrawal`. Three DEV-only scenes (`CasinoGamblingFinances`, `ClientsBetsHistory`, `ClientsTransactions`) surface it from Main Menu. See `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md`.

## 5. Implementation Priorities

> **Authoritative implementation order**: `AIHelperFiles/IMPLEMENTATION_ROADMAP.md`. The priorities below are the *feature* breakdown; the roadmap file holds the *sequencing and dependencies*.
>
> **State (2026-06-30):** P0–P2 + the candidate engine (P4/Step 4), difficulty regulator + hardware pools (Step 6), **founder economics (Step 7)**, **Step 8 — UTXO realism / address non-reuse**, and **P10 — network fee activation** are all done & verified in-engine. The game starts on **21 Mar 2009** on a Satoshi/Hal-mined chain; founders mine concurrently in the player era; the chain runs a real multi-input/multi-output UTXO model; the whole network is fee-free before 2009-04-26 and all participants pay fees after (see `AIHelperFiles/step10-network-fee-activation-plan.md` + P10 in §5). **The active next work is Step 9 — economy/meta (P6–P8)**; carried-forward deferrals: bots multi-address (OQ-8.2), deposit-address rotation (OQ-8.3), the optional Patoshi forensic view (8.5).

### ▶ NEXT — the agreed working order (2026-08-21)

Three plans are specified and unstarted. **This is the sequence and the reason for it**, decided with the
developer after measuring the world rather than assuming it.

| # | Plan | Branch | Carries a wipe? |
|---|---|---|---|
| **1** | **Mini-plan 06** — prove INC-003's root cause deliberately | `repro/explorer-clock-rewind` | **Yes** — and the wipe is the instrument, not a cost |
| **2** | **Step 17** — direct explorer access + the event-driven audit | `explorer-access-and-event-driven-audit` | No |
| **3** | Step 17's suspended halves — Betting Statistics (17.B) and T4 (17.D) | — | 17.B eventually does |

**Why mini-plan 06 goes first, against the intuition that a wipe should be delayed.** The world was
measured before deciding: **210 blocks, game date 2009-05-27, zero companies founded, Market Birth 416
in-game days away.** There is nothing accumulated to lose — a fresh world starts at 2009-03-21, about
twenty real minutes of play behind. **The wipe is cheap, so the argument for delaying it disappears**, and
what it buys is real: INC-003 closed, the contaminated journal disposed of instead of quietly skewing every
lifetime figure, and a virgin world in which a reproduced band admits no argument.

> The first framing of this choice was a false dilemma — *wipe now and lose a rich world, or test Step 17
> on a world you will wipe.* **Both horns rested on "rich", and one measurement removed them both.**
> Check the premise before weighing the trade-off it implies.

**⚠ And Step 17 carries a constraint discovered in the same measurement** (`step17-…-plan.md` §5.1a): four
of the event-driven audit's targets — `AuctioningCompanyDetails`, `CompanyDetails`, `CompaniesWallets`,
`CasinoCoinSwaps` — depend on companies or a market, and **both begin at Market Birth**. They are §39.16
rule 10 **SUSPENDED**, precondition named: *the world reaching 2010-07-18*. Migrating them blind is
allowed; declaring them done is not. Without that split, 17.C would sign off with a quarter of its
migrations never once executed — the same failure this project documented twice in one week (mini-plan
04's emit budget, shipped without ever running; Ch. 38's catalogue, a month stale while reading as
current).

**Also carried, and not blocking:** the `user://` archive holding INC-003's evidence lives at
`%APPDATA%\Godot\app_userdata\GamblingMiner_INC003_evidence_2026-08-20\` — 88 files, 55 MB, verified. The
world reset deletes the live journal, so that archive is the incident's only surviving evidence.

### PH - Historical Foundation — ✅ BASELINE REACHED

Goal: establish the historically faithful opening and the network-growth init model **before** economy systems expand on top of it.

- [x] Founders Satoshi & Hal as mining nodes with seed phrases; `FoundersWallets` dev scene (room for Mike Hearn).
- [x] Fix genesis & early coinbase recipients to derived `gm1q…` addresses. *(InputData 100-byte cap still deferred.)*
- [x] Block-candidate + hashrate model **(minimal weighted lottery)** — the keystone seed for P4.
- [x] First-launch bootstrap pre-mine to `21 March 2009` (Satoshi dominant, Hal exactly 3). **Verified in-engine.**
- [x] **DONE (Step 7, on the candidate engine):** Satoshi `11,000`-BTC dynamic ramp (retire ≥ `2011-04-26`); the `12 Jan` 10 BTC Satoshi→Hal tx; April Mike Hearn 32.51 round-trip (+82.51). Built as **regulated concurrent mining** (`FoundersMiningService` + `HistoricalEventScheduler`); Hal `P=1.0` fades to 0 by `9 Aug 2009`. Verified in-engine. See `AIHelperFiles/step7-historical-character-economics-plan.md`.
- Companion research: `historical-blockchain-events-research.md` (address-reuse research → resolved in Step 8 §6).

**Baseline reached:** a new game starts on `21 March 2009` with a Satoshi/Hal-mined chain and the player bets from there. ➡ **Steps 7 (founder economics) + 8 (UTXO realism / address non-reuse) complete; next: Step 9 (economy/meta, P6–P8).**

### P0 - Documentation Truth Pass

Goal: make docs trustworthy and easy to maintain.

- Add feature status labels: `Implemented`, `Prototype`, `Planned`, `TBD`.
- Update `README.md` so planned features are not described as current gameplay.
- Update `PLAYER_GUIDE.md` so it describes what can actually be played now.
- Keep `DESIGN_OVERVIEW.md` as the living target design, but mark future systems clearly.
- Keep `GLOSSARY.md` short and canonical.

Done when a new reader can tell in under 30 seconds what exists now and what is planned.

### P1 - Main Balance Naming

Goal: align docs, UI, and code-facing language around `Main Balance`.

- Keep user-facing labels on `Main Balance`.
- Keep internal class renames optional until safe, but document the legacy naming migration.
- Verify bankroll transfers still read naturally:
  - Main Balance -> Bankroll
  - Bankroll -> Main Balance
- Show performance against the `40,000 SC` baseline.

Done when the UI no longer teaches two names for the same concept.

### P2 - Bankroll Rules

Goal: make money flow obvious.

- Bankroll is a subaccount used for active bets.
- If Bankroll reaches zero and Main Balance has funds, the player can continue by recharging.
- If auto-recharge is enabled, the game attempts to recharge automatically.
- If auto-recharge is disabled, `ResultLabel` should warn the player that Main Balance can be moved into Bankroll.
- Game over only happens when Main Balance plus Bankroll reaches zero. **[Updated — Step 12: now `Private Bank Account + Main Balance + Bankroll = 0`.]**
- **[Implemented — Step 12]** `BankrollProgramService.AutoRechargeEnabled` off-switch (default ON), with a real UI toggle in `BankrollProgrammer` and a proxy toggle in DiceGame's strategy panel (single source of truth). The remaining P2 gap is the *warning-label* UX when it's OFF.

Done when a player can explain where money moved after a 15-minute session.

### P3 - Bot Wallets, Transactions, And Mempool

Goal: make bot mining and block contents meaningful.

- Create BTC wallet addresses for:
  - mining bots,
  - non-mining bot participants,
  - the casino,
  - the player.
- Generate scheduled transactions between wallets.
- Start BTC circulation around block 4 or 5.
- Build a public mempool shared by mining nodes.
- Limit Basic Mode blocks to `24 transactions` for now.
- Include transaction fees in block rewards.
- Let bots mine competing blocks using their own candidate blocks.

**Re-alignment with PH**: after the historical foundation, miner bots are **introduced gradually after player start**, not present at block 1. The scheduled-transaction circulation trigger (`AIHelperFiles/scheduled-bot-transactions-plan.md`) must key off bot introduction rather than an absolute block index, and no-op while only founders are mining.

Done when blocks contain believable transactions and bots can win blocks before the player.

### P4 - Block Template Builder  ✅ DONE (the per-node candidate block model)

> Implemented as `AIHelperFiles/candidate-block-model-plan.md` (roadmap Step 4 — slices 4a/4b.1/4b.2/4b.3/4c). Each node builds its own candidate block from its mempool view: fee-ordered tx selection (24-tx cap incl. coinbase), Merkle root, coinbase = reward + collected fees, real block-header hashing, content-hash txids, and coinbase maturity N=1. BlockExplorer surfaces it; BTCWallet has a player fee selector. The bootstrap/lottery already mine through this engine (the refactor was in-place), and the founder economics layered straight on top in **Step 7 (✅ done)**. **Next lead: Step 8 — UTXO realism / Patoshi per-receive addresses.**

Goal: simulate Bitcoin-like block assembly without full-node complexity.

- Select transactions from the public mempool.
- Use a simplified ancestor-feerate ordering model.
- Tie-break equal fee rates by mempool age.
- Build a coinbase transaction with block reward plus included fees.
- Compute a Merkle root from the final transaction order.
- Keep room for future private mempool/fee-market behavior.

Done when candidate blocks differ by miner and transaction selection matters.

### P5 - Hardware Progression

Goal: make speed upgrades meaningful without breaking the learned rule.

- Hardware increases the maximum allowed bets per real second.
- Each extra bet still equals one nonce attempt.
- Game time progression remains based on bet ticks, not hardware directly.
- Basic Mode can expose only Dice allocation until more games exist.
- Cap current cycle throughput at `100 attempts` per time cycle.

**Foundation (precursor to P5)**: the hardware credit model, pool assignment UI, and casino community pool are defined and implemented first in `AIHelperFiles/btc-pools-hardware-plan.md`. P5's economic layer (hardware pricing, variety, upgrade decisions) builds on top of that foundation.

Done when buying hardware feels like an economic decision, not just a UI speed setting.

### P6 - Casino Finances  ✅ MOSTLY DONE (Step 11 + Step 12)

Goal: track the casino as an economic actor.

- Track casino SC income from player and bot losses. ✅ (`CasinoScBalanceService`, Step 11)
- Track casino SC expenses from player and bot wins. ✅
- Model an infinite bank credit line at first. ✅ (on-demand `40,000` loan draw)
- Track casino debt to the bank. ✅ (`TotalLoaned`, `CumulativeProfitSinceLoan`)
- DEV scenes shipped: `CasinoGamblingFinances` / `ClientsBetsHistory` / `ClientsTransactions` (Step 11).
- **Repayment (still open):** once reserves pass a TBD threshold, the casino can start repaying bank debt. **Step 12 gives this a ready mechanism** — the player's **Auto-Withdraw threshold/surplus** model (`PlayerBankAccountService.TryAutoWithdraw`: keep a floor, move one installment per event) applies verbatim to the casino, running against its *debt* instead of an account. Blocker: an **insolvency policy** first (the "never block a bet on casino insolvency" rule breaks if the auto-loan can be toggled off) — design alongside this. See `ProjectDesignManual.md` Ch. 32 §32.2.
- Interest is postponed.

Done when casino profit/loss can be audited internally ✅ — remaining: the repayment/insolvency policy above.

### P7 - BTC Trading Minimum

Goal: make BTC useful without implementing the full historical economy.

- BTC/SC conversion happens through casino BTC addresses.
- BTC cannot be wagered directly.
- Trading unlock timing is TBD.
- Conversion fees will increase if the player fails weekly/monthly minimum wager requirements (TBD).
- Base Conversion fees will decrease with level or volume.

Done when mined BTC can influence survival decisions.

### P8 - Achievements

Goal: give Basic Mode long-session structure without a hard win condition.

- Survive for a time milestone.
- Mine first block.
- Mine multiple blocks.
- Reach BTC milestones.
- Reach SC milestones.
- Maintain bankroll discipline milestones.

Done when players have short-term and medium-term targets.

### P9 - Unit Testing Infrastructure

Goal: establish a test foundation so core logic can be verified without running the full game.

- Identify a C# test framework compatible with Godot 4 projects (e.g., GdUnit4 for in-engine tests, or xUnit/NUnit for pure logic outside Godot).
- Start with pure C# classes that have no Godot dependency: `DiceEngine`, `ProgressiveBettingStrategy`, `Money`, `BetHistoryRepository`.
- Define a minimal test conventions doc (where tests live, how to run them).
- Do not attempt to test autoload services or scene logic in the first pass.

Done when at least the core betting and money logic has automated coverage and a new developer can run tests from the command line.

### P10 - Network Fee Activation (~2009-04-26) ✅ DONE (2026-06-30) — flat era RETIRED by Step 14 ND.7 (2026-07-13)

> **Superseded note (ND.7)**: the flat 2009-04-26 / 0.1-BTC fee era this priority built was replaced by the **Historical Fee Replay** — the fee era now begins at Market Birth (2010-07-18) and replays the real daily median/mean band from the network dataset. P10's *plumbing* (fee rows, per-tx `Fee = Σin − Σout`, coinbase collection, the four send panels) is exactly what the replay feeds. See the "Network Fee Market Simulation" entry below and `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10.

Goal: make the simulated network historically faithful to early Bitcoin's **fee-free era**, then switch the whole network to paying fees on one date — resolving the current dev-time contradiction (scripted historical txs are fee-free while bots/casino attach fees from block 1).

- **The whole network is fee-free until a `FeeActivationDate` ≈ 2009-04-26** (the nearest mined block, just after the 18 Apr Hearn round-trip). On/after that block, **every** participant (player, bots, casino, founders) begins paying fees.
- Gate points to flip to 0 before the date, restore after: the bot fee in `NetworkRoot.ScheduleBotTransactionsAfterBlock` (`MinBotFeeBtc`/`MaxBotFeeBtc`), `CasinoTxFee`, and the player's default/selected fee. The candidate-block fee-collection engine is unchanged (it already collects `ΣFee`); this only gates whether a fee is *attached*.
- Provisional date `2009-04-26`; resolve to the nearest block by timestamp (dates are the source of truth — Q-E1).
- **Own branch** (e.g. `network-fee-activation`); does not block other Basic Mode work.
- Design: `AIHelperFiles/step8-utxo-realism-plan.md` OQ-8.7 + `IMPLEMENTATION_ROADMAP.md` ("What's next"). Tracked in the §6 checklist.

Done when no fee is attached by any participant before the activation block, and all participants attach fees on/after it, validated in-engine across the April 2009 boundary.

**Delivered (2026-06-30):** `NetworkFeePolicy` static class (single source of truth: `ActivationDateLocal = 2009-04-26`, `DefaultFee/MinFee/MaxFee`); fee row hidden before the date, default 0.1 BTC after, clamp 0.1–1.0 on send, across all four BTC wallet send panels (BTCWallet, FoundersWallets, CasinoFinances, BotsBtcWallets); sender balance label on every send panel; "Go Back" button rename; backend gates for bot automated fees and casino pool-payout fees (`NetworkFeePolicy.IsActiveByTimestamp`). Also delivered in the same phase: **casino pool distribution atomicity fix** (one multi-output tx per event — eliminates partial/double-payment bug); **Block Explorer full multi-output tx display** (full `Inputs[]`/`Outputs[]` iteration, all txs in a block shown); **OQ-8.2 cosmetic filter** (`IsSelfChangeTransaction` + `ExternalOutputs` in `BlockExplorer.cs` — hides bot change-to-self from display until bots have `DerivedAddressWallet`). Full detail: `AIHelperFiles/step10-network-fee-activation-plan.md`.

### Network Fee Market Simulation — Research Priority (not yet scheduled)

Goal: replace the single hardcoded `NetworkFeePolicy.MinFee` (flat `0.1 BTC` since the P10 activation date, unchanged for the rest of the simulated timeline) with a real model of how the network fee should move over time. Flagged 2026-07-08 while building the casino swap desk's fee-deviation cap (Step 13, D-SW.12) — every fee-dependent system built on this constant (the swap desk's whole model, the minimum swap size, every BTC wallet send panel) inherits its historical naivety, and none of it would need code changes to consume a non-constant fee, only a decision on HOW to derive one. **This is a dedicated research/design round, not an implementation task yet — do not schedule build work on it until the round below has run.**

Two candidate approaches, framed as research options to investigate, compare, and decide between (not mutually exclusive):

- **Option A — historical fee replay.** Mirror `BtcMarketDataService`'s existing architecture (a curated historical dataset replayed as a step function keyed to game-time) but for real average Bitcoin transaction fees instead of BTC/USD price. Pro: historically faithful, reuses a proven pattern, easy to cite/validate against a real source. Con: real historical fee data is far less standardized than price data (especially the pre-2012 near-zero-fee years), and it's a pure replay with **zero connection to anything happening inside our own simulated chain** — the player's or bots' own transaction volume has no influence on the fee they pay.
- **Option B — reactive fee market from the simulation's own state.** Derive the fee dynamically from OUR simulated chain's own congestion: mempool depth vs. the block's transaction cap, the size of the miner/bot population, total transaction volume — a basic fee-per-byte auction, similar in spirit to real Bitcoin's actual fee-market mechanics. **The deciding point in its favor**: this project's own miner/bot population and transaction volume are **already planned to grow following real Bitcoin history** (gradual bot introduction, the referral system, mining pools scaling over time — see PH/P3/P5 above) — so a congestion-reactive fee model would **automatically and indirectly track real historical fee trends**, as a side effect of infrastructure already committed to elsewhere, with no separate historical-fee dataset to source. It also gives the player genuine agency (their own activity can move the fee market they experience), and slots directly into the placeholder P4 already left for it ("Keep room for future private mempool/fee-market behavior," `candidate-block-model-plan.md`). Con: needs real design work that doesn't exist yet (a congestion→fee formula, its tuning/elasticity, an actual auction inside the mempool/candidate-block engine) and is harder to validate against a citable real-world number.

**Working hypothesis for the eventual research round** (to confirm or overturn when it actually runs, not a decision yet): Option B looks like the better structural fit — it inherits historical shape for free from already-planned population/volume growth instead of requiring an independently-sourced second historical dataset, and it already has a natural home in P4's mempool/fee-market placeholder. Option A may still be valuable as a **calibration reference** for tuning B's emergent curve against real history, rather than as a standalone replay mechanism — a hybrid worth evaluating explicitly. Full context and the swap desk's dependency on this: `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` §3.4.

**✅ DECISION RECORDED (2026-07-07, user — Step 14 round 1, D-14.6, superseding the working hypothesis above): Option A is the chosen mechanism; Option B is retained as a future VALIDATION experiment, not the mechanism.** Rationale: (1) the fee dataset turned out to be zero-marginal-cost — Step 14's network-data pipeline (Coin Metrics, verified alive with genesis-complete daily fee metrics) fetches the fee columns alongside the tx/hashrate series it needs anyway, dissolving Option A's "second dataset to source" con; (2) fees, like `price_usd`, are decreed **fractal-exempt** (never /100-scaled), so the replay applies at face value with no scaling design; (3) the hypothesis's best argument for B — "population/volume growth will track history anyway" — is inverted into B's new role: once the Step-14 scheduler replays historical population/volume, building B later and comparing its emergent fee curve against A's replayed truth becomes a **cross-validation of the whole network simulation** ("if B lands near A, the rest was built right"). Data acquisition: Step 14 ND.0; the `NetworkFeePolicy` replay implementation remains its own future step (step14 plan §9.4). This item stays open only for that implementation + the eventual B experiment.

**✅ OPTION A IMPLEMENTED (2026-07-13, Step 14 ND.7)**: the Historical Fee Replay is live — fee era gated at Market Birth (2010-07-18), daily median/mean/max band replayed via `NetworkFeePolicy`'s pushed schedule, cast sell-flow on the mean and every other participant on the median, `WorldFormatVersion` 3. One dataset caveat recorded at ND.7.0: Coin Metrics' daily median (`FeeMedNtv`) is paid-tier, so the `fee_median_btc` column ships from a developer-approved hybrid (Blockchair true medians for 2010-07 → 2011-04, BitInfoCharts USD median ÷ price after, spot-checked within 0.2%). **This item now stays open ONLY for the Option-B congestion experiment (OQ-ND7.1)** — the layer that will finally give participants a reason to pay ABOVE the daily base (queue-jumping at the 24-tx cap), and the cross-validation of the population/volume simulation.

### Casino Referral System (Basic Mode)

Goal: give non-miner holder bots (`non_miner_1`..`non_miner_40`) a social and economic role in the casino ecosystem, and give the player an organic reason to donate BTC to them.

**Referral auction mechanic** (decisions resolved 2026-06-21, **canonically amended 2026-07-09 — Step 14 EB.2, D-EB.4/5/6/7, then retuned same day at round 3, D-EB.8/9/10**; see `step14-historical-network-population-scheduler-plan.md` §5.2–5.3, §6 and ProjectDesignManual §22.6):
- Non-miners enter the auction pool **from Market Birth (2010-07-18)** along the historical active-address curve (`1 + 12.18 per address-decade` since birth; pool raised **10 → 40** at round 3, D-EB.8; empirically all 40 deployed by **2017-12-13**) — the old 1-per-~2-days introduction from live-mining start is retired. Scaling further (toward 100–220) needs a companion architecture change first — see the deferred decoupling proposal, ND.4.
- Each non-miner bot runs a **100-in-game-day auction window** (D-EB.9, confirmed) whose **countdown starts at the bot's FIRST QUALIFYING BID** — never-bid-on bots stay recruitable indefinitely, and every resolved auction has a real winner. (Supersedes the original 7-day window anchored to the creation block timestamp, and an interim 6-month provisional value.)
- **Only casino players can bid (D-EB.7)** — nodes whose mining REQUIRES betting at the casino: **the player and the classic casino-miner-bots `bot_1..4`**, who already run real hardware-credit-locked betting sessions to mine and so already have a casino relationship. The much larger, historically-growing pool of Step-14 cast miners (up to 29 additional beyond the classic 4, 33 total at the 2025 max) mines via drained attempts, never bets, and does NOT qualify — their transfers (plus all non-miner↔non-miner exchanges, entry-bootstrap seed funding) are **economy, not bids**: they fund wallets but never start, lead, or win an auction; the ledger distinguishes qualifying bidders from mere senders. Promoting cast miners to casino-player status is a distinct, deferred feature — not scheduled.
- Non-miner bot addresses are visible in BlockExplorer; a toggleable **"Auction / Company Mode"** (default off, renamed from "Enroll Mode" at ND.9a, 2026-07-22) filters the explorer to still-recruitable non-miners **and** founded companies. **Deferred, non-priority Basic-Mode objective:** the toggle reverts to **Auction Mode only** in Basic Mode, and founded companies get their **own dedicated scene/list** (separate from the explorer). Not scheduled.
- The qualifying bidder with the highest total confirmed BTC donation when the window closes becomes that bot's **casino referral** — **permanently**: the bot then **leaves the auction forever** (no renewal). **No cap** on referrals per node.

**Winning Referral Commission** (the only perk):
- **1% → up to 5%** of the referred bot's SC winnings — scales with the referral's **Casino Rank** (top rank = 5%; see the Casino Rank System item below).
- **Always paid by the casino, never deducted from the referral's earnings.**
- Claimable in **real time** in a new **`Referrals` scene** (from MainMenu), which also opens a **Miner Referrals** sub-scene.
- Bot SC winnings come from simulated betting (MartingaleCalculator-derived logic, designed in a later phase).

**Deferred — vary the self-eviction guard to close a predictability exploit (ND.10b, 2026-07-22):** the self-eviction guard (a bot won't re-bid a full pool where it holds the smallest slot, D-ND6.7b) plus the deterministic affordability/priority ordering is now surfaced transparently in `AuctioningCompanyDetails` (the per-bot real-leading-bid-roll panel). That transparency makes the behavior **exploitable** by a player who learns the exact rule. A later subphase should introduce **to-be-designed randomized/asymmetric slack** into the guard (and possibly the ordering) to (a) close the exploit and (b) add flavor via natural asymmetry. Not scheduled.

**Minimum donation rule**: Send amount must be ≥ fee amount (at 0.1 BTC fee, minimum donation is 0.1 BTC).

**Donation ledger**: Updated at block confirmation only — never at broadcast. Schema: `botNodeId`, `senderAddress`, `totalDonatedBtc`, `confirmedAtBlockIndex`, referral award block.

**Miner Referral conversion**: Every 10 referrals earned, the player may convert one non-miner referral into a **Miner Referral Node** by donating 2 hardware pieces — done in a **dedicated Miner Referrals scene opened from the `Referrals` scene** (OQ-D). Miner Referrals are player-controlled: the player manages their mining pool shares, autobet strategies, hardware purchases (from Miner Referral's MainBalance), and BTC→SC conversions. Miner Referral BTC cannot be sent to external wallets. SC from conversions goes to Miner Referral's MainBalance and can only be spent on hardware. Chain sync simulation: new nodes simulate downloading the full chain before mining begins — sync time **decreases over the in-game years** (tech progress) but a **longer chain always costs more** (length-sensitive); exact curve TBD (OQ-C).

**BTC/SC trade scene** (planned): wallet selector must include all active Miner Referral wallets alongside player wallets, so the player can manage referral BTC conversions in the same flow as personal conversions. BTCPoolsAndHardwareShop scene must also include Miner Referral selectors for hardware purchases.

See `AIHelperFiles/scheduled-bot-transactions-plan.md` → Future + Resolved Decisions sections for full design notes.

Done when a player can earn a casino referral by donating BTC to a non-miner bot and observe at least one Winning Referral Commission claimable in the `Referrals` scene.

### Casino Rank System (Basic Mode)

Goal: a progression ladder for casino participants (player, and notably **Miner Referrals**) that gates and scales rewards.

- Defines ranks from an entry level up to a top level (exact tiers + advancement criteria TBD — likely wager volume / time survived / BTC or SC milestones).
- **Drives the Winning Referral Commission scale:** a referral's rank sets its commission rate, from **1% at the base rank up to 5% at the top rank** (the commission is paid by the casino).
- Connects to other systems over time (achievements P8, conversion-fee tiers P7, etc.).

Done when a participant's rank is tracked and visibly affects at least the referral commission rate.

### Casino-Bot Treasury Policy — variable reserves & bid affordability (deferred design, hardcoded stopgap shipped at ND.10e)

**Status: a hardcoded stopgap is LIVE** (Step 14 ND.10e, 2026-07-23 — `NetworkRoot`: opening bid worth **$0.10** capped at **1 BTC**, raise band **5–10%**, BTC reserve guard **stop ≤ 200 / resume ≥ 300 BTC**, dividend auto-claim batched at **10× the network fee**). Those five numbers are deliberate placeholders: they were chosen to stop `bot_1..4` de-financing themselves out of the referral auctions, not from any model of what a bot's treasury *should* look like. This entry records the real design so the constants don't quietly become canon.

**Goal:** replace the flat constants with a **reserve policy** each bot evaluates for itself, so reserves look varied and natural across the four bots instead of identical and arbitrary.

Candidate inputs (all already available, none requiring new state):
- **Live BTC price** (`BtcMarketDataService`) — a fiat-anchored reserve (e.g. "keep $X of BTC") behaves very differently in 2011 vs 2017 and is the same trick ND.10e's opening-bid floor already uses.
- **The bot's SC position** (`NodeFinancialState.PrincipalBalance`) — a bot flush with SC can afford to run its BTC down; one near a recharge cannot.
- **Mining income rate** — blocks mined per window, i.e. how fast the reserve actually refills. A reserve should be expressed in *time to recover*, not a flat amount.
- **Dividend inflow** (§22.12/ND.8g) — a bot holding NST/PST in several founded companies has a recurring BTC income the guard should credit it for.
- **Per-bot personality** — the same per-world draw pattern used for the bots' governance preferences (D-ND8.13/26) would give each bot its own risk appetite, permanently and reproducibly.

**A structural defect the policy must fix, not just a number** (found 2026-07-28, ND.10j's BitInstant audit — plan §14.11.3): **a bot can bid itself into the reserve guard.** The half-spendable cap is evaluated per bid, and the guard only runs on the *next* block sweep — never against the balance a bid would leave behind. `bot_1` sent 86.93 BTC out of 285.30 (legal under the cap) and landed at 198.51, under the 200 stop, in one move. Compounding it, the 200→300 hysteresis band is ~345 blocks wide at dividend-only income, so the "rest" is closer to a retirement: `bot_1` was out of every auction from block 947 to 964 and only re-entered because a process restart re-derived the in-memory set while it sat between the thresholds. **Whatever replaces the flat constants must be a pre-commit check** ("would this bid breach my reserve?"), not a post-hoc sweep, and its recovery band must be expressed in *time to refill* rather than a flat BTC gap.

> **⚠ That restart escape hatch is now CLOSED, which makes the above more urgent, not less** (Step 16 P16.6, 2026-07-31 — `ProjectDesignManual.md` §22.20). The sentence above records a bot being rescued from its rest by a restart as an aside; it was in fact a defect, and the mirror image of it let `bot_4` — which peaked at 250 BTC and **never** reached the 300 resume threshold — take the leading bid in six auctions it should never have entered. `_botsRestingOnReserve` is now seeded by chain replay at process start (`EnsureReserveGuardSeeded`), so the hysteresis has real memory across restarts. Consequences for this design work: **(1)** the ~345-block band is now a genuine retirement with no accidental way out, so the *time-to-refill* recovery model is the load-bearing part of the fix, not a refinement; **(2)** a second emergent effect became visible in the same replay — bots are born holding 0 BTC, which is `≤ 200`, so **every bot is born resting and the 200 stop never gates a fresh one; only the 300 does**, i.e. a new bot must mine six blocks before its first bid ever. That may well be desirable (a bot should have a treasury before it plays), but it is emergent rather than chosen and the policy should state it deliberately either way.

**Two pressures already scheduled that will change the arithmetic** (re-tune only after they land, not before):
1. **Hardware progression (P5)** — hardware multiplies every casino-miner's attempts/sec and therefore its BTC income; the whole auction economy is sized against pre-hardware income today.
2. **Dividend inflow** is only now becoming material as companies are founded — with several mature companies paying quarterly, the bots' BTC income stops being mining-only.

**A third constant now sits in the same category:** `FreshPoolSeedingWeight` (34, ND.10l §14.13) — the tie-break weight an unbid pool carries against a contested one. It trades first-bid seeding against escalated re-bids, and block frequency changes how often the two compete at all, so it belongs to the same post-R2/post-P5 calibration pass.

**Also wanted:** surface the knobs (reserve floor/ceiling or the policy's parameters, the raise band, the opening-bid anchor, the claim batching multiple) as **DEV-configurable** in the same spirit as `CasinoGamblingFinances`/`WorldEconomy`'s existing sliders, so a calibration run doesn't need a rebuild. Design + the audit that produced the stopgap: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §14.6.

### Persistence Durability & History Retention (P15.11) — SCHEDULED, blocks the rest of P15.8

**Status: scheduled and blocking (2026-07-29).** Spec: `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` **P15.11**. Forensics: `Documentation/INCIDENT_LOG.md` **INC-001**. Design statement: `ProjectDesignManual.md` **Chapter 40**.

**What ships:** atomic snapshot writes (`.tmp` → rename) with a loader that fails loudly and a writer guarded against ever persisting over a failed load (D-15.26); the journal-rebuild path made to obey the same rotation/cleanup invariants as the incremental writer (D-15.27); a retention cap that bounds the boot load by construction (D-15.28/29); and the removal of the write-only `blocks-*.json` mirror (D-15.30). The crashed world is repaired rather than reset (D-15.31) — the bet history is deleted under explicit authorization, since it is stats-only and was already inflated by duplication.

**Deliberately deferred, recorded so it is a decision and not an oversight:** a **persisted lifetime stats aggregate** (so totals stop meaning "over retained history"), and making the world snapshot **incremental** rather than a full 9.25 MB rewrite whose cost grows linearly with chain length. Both become worth doing when a run goes materially past ~1,666 blocks; revisit alongside P5 (hardware progression), which changes how fast blocks accumulate.

### Player Holdings Hub — one screen to claim, vote and find your companies (design + implement, after Step 16)

**Status: recorded 2026-07-30, not yet scheduled.** Deliberately **not** in Step 16 (developer's call) — that step makes the votes worth casting and the dividends cheap to pay; this one makes them *findable*.

**The problem, in the developer's words:** the only way to reach a company the player holds stock in is to hunt for it among all 40 in `BlockExplorer`'s Enroll Mode. Everything the player owns is scattered across a list dominated by companies they have nothing to do with.

**What it is:** a single player-facing screen listing **only the companies where the player holds NST or PST** — claim dividends, cast/see votes, and open `CompanyDetails` from one place. The signals it should carry already exist and are already computed: the **stake border colours** (gold NST / silver PST / black none, §22.15), the **pending-work tint** (red vote · green claim · mocha both · black nothing, §22.16 — `HasPlayerClaimableDividends` is already the shared "payable, not non-zero" source), the FBI investigation warning (`GetFbiInvestigationWarning`), and the bank lending summary. **Reuse those sources, never re-derive them** (§39.16 rule 6): this screen is an *index*, and an index that disagrees with the page it points at is worse than no index.

**The larger idea it belongs to** is the audit's **D2, "decision inbox"** (`step15-…-plan.md` §10.5 handoff): the game generates far more events than it shows — votes opening, dividends becoming payable, auctions closing, FBI heat on a holding — and they live in five different scenes. A holdings hub with a **badge count in the StatusBar** is the smallest version of that idea and probably the highest engagement-per-line change available. Once Step 16's per-company pause toggle exists (D-16.11), this screen is also the natural place to manage those toggles in bulk.

**Promoted in priority by Step 16's own playtest (2026-08-05).** Two of the three participation settings are now per-company (`PlayerPauseOnVotes`, `PlayerAutoAbstain`, plus three policy dials), and with the player holding NST in ten companies the panel-by-panel management is already the slowest part of the loop — this hub is where a bulk view belongs. The step also produced the exact failure it exists to prevent: with the game frozen by a vote at BTC Guild the developer went to ArtForz Cluster, found a normal page, and had no route onward. P16.8e's pause locator patches that one case; **a holdings hub answers the general question ("where is anything waiting for me?") that the locator only answers for the pause.** Reuse `GetCompaniesAwaitingPlayerVote` as the badge source.

### Betting Statistics scene — per-strategy figures (design open, BASIC MODE objective)

**Status: deferred 2026-08-13 (developer's call), targeted at Basic Mode.** Split out of mini-plan 02's Part D so the storage work there can proceed without waiting on a new screen. Full write-up: `AIHelperFiles/mini02-panel-state-and-100k-audit-plan.md` §D.6.

**What it is:** a player-facing screen that picks a **strategy** and shows that strategy's own figures — max martingale level, max bet, bets, net P/L, streaks — instead of the lifetime totals `BetsHistoryExplorer` shows.

**Why it is the right place to design the epoch key.** Mini-plan 02's rollup (D-M2.11) is deliberately **global** in v1; segmenting it needed a boundary definition, and the honest answer was "design that against the screen that needs it rather than guess now". This is that screen. When it is built, two decisions land with it: (a) the fingerprint must cover everything that changes what a *level means* — base bet, both progression percents, both stop amounts, both Insist switches; and (b) storing that fingerprint **per record** versus only per summary decides whether history can ever be re-segmented after the fact. Cheaper to decide with the screen in front of you than in the abstract.

**Already available to build on:** `BetsHistoryExplorer`'s chance-to-win selector (mini-plan 02) is the same idea one axis smaller — filter the history by a strategy dimension, drive the summary figures from the filtered view, and offer an option only from the moment its first bet exists. Its time-aware option list is the pattern to copy. And **max martingale level** is free at settle time from `BaseBetSession.ProgressionTriggerStreak` (D-M2.10) — it is not the same quantity as INC-002's "max consecutive losses" and must not be conflated with it.

### Ghost Miner Typology — four kinds instead of one (design open, after Step 16)

**Status: recorded 2026-07-30 from the Step 16 Round-2 discussion.** Full note: `AIHelperFiles/step16-living-governance-and-bot-wallets-plan.md` **§6.1** (D-16.17).

Today every ghost is **session-transient with no persisted keys**, which is exactly what makes its coinbase frozen forever (D-14.11). The proposal keeps that as the majority case and adds three biographies: **(1) always a ghost** (~80%, unchanged), **(2) active → ghost**, **(3) active → ghost → active** — the *"max sudden whale"* — and **(4) ghost → active** late in the timeline.

**Why it is not a cosmetic change:** kinds 2–4 need keys that survive the process, which is the precise boundary D-14.11 drew. They become real identities (a fourth `BotWalletRegistry` list, seeded and derived-wallet-backed exactly like everything Step 16's P16.2 touches — after that step it is a handful of lines). Kind 1 stays free and keyless.

**Design notes worth keeping:** transitions should be **schedule-driven from the historical curve** (the `ComputeNonMinerIntroSchedule` / `ComputeAndPushFeeSchedule` pattern — derive once, push into a pure static holder), which makes them time-shiftable for free (D-14.7) and gives an entry-year world the same ghost biographies. **Kind 3 is the most valuable of the four**: dormant 2009–2011 coins suddenly moving is a real, recurring Bitcoin event, and it is exactly the sort of thing a player should be able to *notice* in the Block Explorer.

### Promoting Cast Miners to Casino-Player Status — a lever, not a migration (permanently open)

**Status: deferred at Step 16 (D-16.16), recorded as a standing option.** After Step 16's P16.2 the cast miners hold seeds, derived wallets and change rotation like every other participant, so the *mechanics* are ready; what remains is purely an **auction-balance** question (D-EB.7 currently restricts bidding to the player + `bot_1..4`).

**The framing to build toward** (developer, 2026-07-30): introduce them **gradually, as a lever against stagnation and for company variety** — e.g. admitting one or two cast miners as bidders into a pool that has stalled — rather than promoting the whole cast at once. That makes it a **dial the auction system can reach for**, which is a better shape than a one-time migration. Revisit alongside the §22.10 price-out terminator and the ND.10 escalation ladder, since those are the systems a larger bidder population would perturb.

### Bot Seed Phrases & Full UTXO Integration (OQ-8.2) — ✅ DONE (Step 16 P16.2, 2026-07-30)

**Status: SHIPPED.** Delivered as block E of Step 16 — every `bot_1..4`, cast miner, non-miner company and passphrase wallet now carries a 3-word seed + `DerivedAddressWallet`, base addresses derived from the seed (D-16.3), both Block Explorer cosmetics deleted, `WorldFormatVersion` 4 → 5 + `RegistryFormatVersion` 1 → 2. **Read `CLAUDE.md`'s Prototype entry before touching this area**: giving ~74 more participants a seed retired premises that eleven call sites depended on, and the three defects that surfaced the next day (bots spending change they could not see, a six-minute launch from affine secp256k1, and a bot bidding against its own reserve guard) are the lesson, not a footnote. Original design: `AIHelperFiles/step8-utxo-realism-plan.md` OQ-8.2; full record `AIHelperFiles/step16-living-governance-and-bot-wallets-plan.md` P16.2 + `Documentation/ProjectDesignManual.md` §30.10, §22.20, §40.7.

*(Historical note, kept because the reasoning generalizes:)*

**Why it was deferred, and why that reason is now gone.** At Step 8 the number of mining bots was an open question, and handing a seed phrase + full `DerivedAddressWallet` integration to an unknown (possibly large) population looked like a poor trade. The game has since settled: the casino-miner population is **exactly four** (`bot_1..4`), fixed by the ND.8c genesis-grant set and the D-EB.7 "only casino players may bid" rule. At four, per-bot seeds are cheap and the migration is straightforward — the original objection no longer applies.

**What it fixes, concretely.** Bots are the last single-address participants in the world. In `BuildAndBroadcastUtxoSpend` the change address is `sender.ReceiveWallet?.NextReceiveAddress() ?? sender.WalletAddress` — with no `ReceiveWallet`, **a bot's change returns to the very address it just spent from**, so receipts and change pile up as dozens of UTXOs on one address. Observed on block 1274 (2011-12-11): `bot_1`'s 193.73 BTC auction bid on `non_miner_18` combined **nine inputs, all printing the same address**, while three other transactions in that same block paid *into* it. It is correct UTXO behaviour and correct coin selection (`SelectUtxos`: exact match, else largest-first) — but the address repetition is an artefact of the missing rotation, not of the model.

**A finding that de-risks the migration** (from the block-1274 audit): `NetworkRoot.TryResolveInputKeys` resolves the **base address** (the node's own keypair) and **derived addresses** (`ReceiveWallet.TryFindSpendingContext`) through two independent branches. So a bot's base address does **not** have to be seed-derived — it can keep the registry address it already carries all over the chain, and take a `DerivedAddressWallet` purely for change rotation. **That suggests no `WorldFormatVersion` bump / chain wipe is required**; confirm during design before relying on it (the player/casino/founders all have base == seed-derived, so this combination has never been exercised).

**The enabling change:** `BotWalletRecord` (`Scripts/BlockchainPort/Blockchain/WalletModels.cs`) stores keys but explicitly no seed — *"gm1q… only; no seed words stored"*. It needs a seed field. Note `bot_wallet_registry.json` is an **identity file, deliberately spared** by `NetworkRoot.ResetWorldIfIncompatible`'s delete list, so existing records survive every reset with the field absent — this is exactly §39.16 rule 5 (sentinel default + backfill), and the backfill must not disturb the existing `Address`.

**Scope decision to make during design — how far the migration reaches:**
1. **`bot_1..4`** (4 nodes) — the ask, and the only participants with real economic agency (bets, bids, dividends, pool payouts).
2. **Cast miners** (`BotWalletRegistry.CastMiners`, up to ~33 by 2025, spawned one per block) — also single-address. Larger and dynamic, but they only mine and run the sell-flow.
3. **Non-miner companies** (40) — also single-address; they receive bids, inflows and conversions.
4. **Ghost miners** — **never.** D-14.11 makes their coins frozen forever by design; they are session-transient with no persisted keys, and giving them wallets would contradict the whole mechanic.

**The scope choice decides a second deliverable:** the two Block Explorer cosmetics `IsSelfChangeTransaction` / `ExternalOutputs` (§29.9) hide change-to-self outputs and exist *solely* because of single-address participants — CLAUDE.md wants them gone before the referral/rank systems ship. They can only be removed once the **last** single-address participant is migrated, i.e. under scope 1+2+3. Under scope 1 alone the cosmetics must stay, and the display stays filtered (block 1274's transaction really has **two** outputs — the 6.05 BTC change back to `bot_1` is hidden, which is what makes its arithmetic look wrong).

**Related, do at the same time if scope allows:** OQ-8.3 (player/casino incoming deposit-address rotation) is the same family — full HD receive behaviour rather than change-on-send only — and is still parked in the Post-Basic-Mode list below.

> **✅ SCHEDULED (2026-07-30) — this is block E of Step 16**, `AIHelperFiles/step16-living-governance-and-bot-wallets-plan.md` **P16.2/P16.3**. Scope taken: **1+2+3 (casino-miner bots + cast miners + companies), ghosts excluded forever** — so the two Block Explorer cosmetics come out in the same phase, gated on one check (D-16.6: a ghost that only ever receives a coinbase produces no change output and therefore does not block the removal; if that check fails, the cosmetics stay and the reason is recorded). Two decisions taken there are worth reading back here: **D-16.3** derives each bot's base address **from its new seed**, making every participant structurally identical to the player rather than leaving 74 nodes on the `base ≠ DeriveAddress(0)` combination this file flagged as untested — which costs a **`WorldFormatVersion` 4 → 5 wipe** (**D-16.4**, authorized, the world was already reset); and **D-16.7** splits the mixed `BotsBtcWallets` scene into `BotsBtcWallets` (the four casino-miner bots) + new `CompaniesWallets` + new `CastMinerWallets`. Note `bot_wallet_registry.json` is an identity file **exempt from the world-reset delete list**, so the regeneration is version-gated rather than left to be remembered.

### Post-Basic Mode — Divergent Chains / Fork Simulation (revisit AFTER Basic Mode)

**Deferred, not discarded.** The idea is wanted; the system simply has higher priorities until Basic Mode is finished. **Re-plan this only once Basic Mode is complete.**

Today every node shares one canonical chain (a block is mined → `BroadcastBlock` → every node accepts it via `TryAcceptMinedBlock`), so there are never competing chains. That made the old `RunConsensus` / `RunConsensusRound` a no-op, and it was removed in T2.

Goal (post-Basic-Mode): model a more realistic P2P network where chains can **diverge** — block propagation delay, two miners finding a block near-simultaneously, **forks**, **orphan/stale blocks**, and **reorgs** — then resolve them with a real **longest-chain (most-work) consensus** pass. This is a strong educational fit and layers naturally on top of the per-node candidate-block model (P4).

When revisited, this re-introduces a consensus step (reinstate `RunConsensusRound`-style longest-chain adoption, keyed on accumulated work/difficulty rather than raw length) and the UI to observe forks/orphans in the Block Explorer.

Start when: Basic Mode is complete and stable. Until then, leave mining committing to the single shared chain.

### Post-Basic Mode v1 — Checklist (deferred items)

Items intentionally **not** built for Basic Mode v1 — revisit only once v1 is complete and stable. Each links to its design. (Everything else carried forward — fee activation, casino referral/rank, founder long-term timelines — belongs to **Basic Mode**; see the §6 checklist.)

- [ ] **Patoshi pattern — mining-forensic view (Step 8, Phase 8.5).** An *optional, clearly-labelled cosmetic* Block-Explorer view that highlights Satoshi-mined blocks as a contiguous band (echoing Lerner's ExtraNonce-vs-height plot) with a teaching caption. Our engine can't reproduce the real ExtraNonce/decrementing-nonce/timestamp artifacts (random-nonce search, no ExtraNonce field), so it is an honest stand-in. **This is distinct from address non-reuse** (the many-addresses wallet pattern, already implemented) — the D0 terminology correction already shipped; only this forensic view is deferred. Design: `AIHelperFiles/step8-utxo-realism-plan.md` (Phase 8.5 + OQ-8.5).
- [x] ~~**Bots multi-address (Step 8, OQ-8.2).**~~ **PROMOTED out of this list (2026-07-28)** — the deferral reason (an unknown, possibly large miner-bot population) is gone now that the casino-miner set is fixed at four. Scheduled for the end of Step 15; see **"Bot Seed Phrases & Full UTXO Integration (OQ-8.2)"** in §5 above for the why-now, the scope tiers and the design constraints.
- [ ] **Player/casino deposit-address rotation (Step 8, OQ-8.3).** Rotate the *incoming* receive address after each external deposit (full HD behavior). v1 delivers UTXO realism via change-on-send only. Design: `step8-utxo-realism-plan.md` OQ-8.3.
- [ ] **Divergent Chains / Fork Simulation** — see the "Post-Basic Mode — Divergent Chains / Fork Simulation" section above (`IMPLEMENTATION_ROADMAP.md` Step 10).

---

## 6. Basic Mode v0.1 Checklist

> The running tick-list for the **Basic Mode** feature breakdown in §5 directly above (PH + P0–P10 + Casino Referral/Rank). Post-Basic-Mode items live in the "Post-Basic Mode v1 — Checklist" under §5, not here. Open design questions are in §7 below.

- [x] Canonical initial total balance defined: `40,000 SC`.
- [x] Specific starting split defined: `39,900 SC Main Balance + 100 SC Bankroll`.
- [x] Game over condition defined: `Main Balance + Bankroll = 0`.
- [x] Current rule defined: `1 bet = 1 nonce attempt`.
- [x] Basic halving scale defined: `2,100 blocks` (updated from 4,381 in 100X migration; total supply 210,000 BTC).
- [x] Last block and next reward are visible in DiceGame.
- [x] Block checkpoints restore financial state.
- [x] Saved strategies work as development/player-owned strategies.
- [x] User-facing DiceGame label uses `Main Balance`.
- [~] Clarify auto-recharge behavior in UI and docs. **Docs done** (ProjectDesignManual Ch.25 + CLAUDE.md: progression resets, Insist After Stop, auto-recharge precedence). UI labels/warnings still pending (P2).
- [x] Add player BTC wallet and addresses.
- [x] Add bot/non-node wallet address model.
- [x] Add casino BTC addresses.
- [x] Add `CasinoFinances` development scene (BTC wallet — addresses, passphrase, UTXO sends).
- [x] **Step 11 — Casino SC Gambling Finances — ✅ DONE (2026-06-30).** `CasinoScBalanceService` autoload (99M SC Main Balance + 1M SC Bankroll, target-to-fill auto-recharge, 100M SC re-loan on exhaustion, checkpoint-persisted); `CasinoClientLedgerService` autoload (per-client SC deposit/withdrawal ledger with stat snapshots; `auto_recharge` excluded from deposit totals/baseline); SC flow wired per settled player bet; `CasinoGamblingFinances` DEV scene from Main Menu (balances, P/L vs total loans, recharge target controls, manual transfers, nav to sub-scenes); `ClientsBetsHistory` DEV scene (global SC wagered all clients live, per-client all-time + since-last-deposit/recharge P/L and wagered, game filter, live bet feed); `ClientsTransactions` DEV scene (deposit/withdrawal-only transaction list per client — auto-recharges recorded but hidden from the visible list — global totals, wager-base annotations). See `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md`.
- [x] Add scheduled bot transactions (core scheduler; circulation trigger to be re-aligned for gradual bot introduction).
- [x] **PH**: Founders Satoshi & Hal (and Mike Hearn) as nodes + `FoundersWallets` dev scene (verified). *They now mine concurrently in the player era — Step 7 below.*
- [x] **PH**: Fix genesis/early coinbase to derived `gm1q…` addresses (genesis stays unspendable).
- [x] **PH**: Block-candidate + hashrate model (minimal weighted lottery) — the keystone seed (verified). Full per-node candidate engine = **P4 ✅ DONE**.
- [x] **PH**: First-launch bootstrap to 21 Mar 2009 (Satoshi dominant, Hal exactly 3) — verified in-engine.
- [x] **Step 7 (founder economics) — DONE & verified**: founders as **regulated concurrent miners** (`FoundersMiningService`) — Satoshi 11,000-BTC ramp + disappearance (~10% share, retire ≥ 2011-04-26), Hal `P=1.0` drip fading to 0 by 9 Aug 2009, Mike Hearn 32.51 round-trip (+82.51, never mines), 12 Jan 10 BTC Satoshi→Hal tx, `HistoricalEventScheduler`, FoundersWallets DEV readout + `founders_trace.csv`. See `AIHelperFiles/step7-historical-character-economics-plan.md`.
- [x] **Step 8 (UTXO realism / address non-reuse) — DONE & in-engine audited.** Replaced the account/balance model with a **real multi-input/multi-output UTXO model** (`Transaction` = `Inputs[]`/`Outputs[]`, chain-replayed UTXO set, per-input signing, `Fee = Σin − Σout`). One unified spend path (`BuildAndBroadcastUtxoSpend`, exact-match else largest-first multi-input coin selection + change). Terminology corrected (D0): the address mechanic is **address non-reuse**, *not* the "Patoshi pattern" (a mining-forensic fingerprint, reserved for the unbuilt Phase 8.5). Plan: `AIHelperFiles/step8-utxo-realism-plan.md`; design: ProjectDesignManual Ch. 30.
  - [x] §6 address research resolved (D4/D5): strict one-address-per-receive holds incl. the receive side (Satoshi received from Hearn at a *new* address); Satoshi↔Hal unidirectional. `historical-blockchain-events-research.md`.
  - [x] Fresh derived **coinbase** address per block — **Satoshi-only** (the address-non-reuse spread, ~109 distinct coinbase addresses audited, tracking to the fractal ~220). Player/casino/Hal/Hearn keep one coinbase/receive address and become multi-address only via **change on send**; bots stay single-address (no seed — OQ-8.2).
  - [x] Real **change outputs** on spends — **E8 reinstated** (17.49 Hearn change → a fresh Satoshi address; audited on-chain in the April round-trip).
  - [x] `FoundersWallets` lists Satoshi's many derived addresses with per-address balances (scrollable address book + "View empty addresses" toggle); BTCWallet + CasinoFinances have the same view.
  - [x] **Clean reset** (`WorldFormatVersion`) instead of an in-place migration (the old chain has no UTXO linkage).
  - [ ] Hal's network-coupled fade (replace the linear `1.0→0` stand-in once gradual miner spawning exists) — *late Basic-Mode tuning, not blocking; unrelated to UTXO.*
- [x] **P10 — Network Fee Activation ≈ 2009-04-26 — ✅ DONE (2026-06-30).** `NetworkFeePolicy`; whole network fee-free before the date, all participants pay after; fee UI in all four wallet send panels; backend bot/casino gates. Also: casino pool atomicity fix; Block Explorer full multi-output display; OQ-8.2 cosmetic filter. Full detail: **§5 → P10** + `AIHelperFiles/step10-network-fee-activation-plan.md`.
- [ ] **Founder long-term timelines** — beyond Hal's fade (above): Hal 2013 sell-off / 2014, Mike Hearn 2016; late Basic-Mode tuning. `step7-historical-character-economics-plan.md`.
- [ ] Add non-miner bot donation tracking (donor-per-bot ledger; groundwork for casino referral system).
- [ ] Add Winning Referral Commission scene (list referrals, claimable 1% SC commission per bot, claim button).
- [ ] **Casino Referral System** + **Casino Rank System** — full systems (design in the "Casino Referral System (Basic Mode)" / "Casino Rank System (Basic Mode)" sections above; referral groundwork is the two items above). `scheduled-bot-transactions-plan.md`.
- [x] Add hardware credit system with casino community mining pool, per-node pool assignment, and BTCPoolsAndHardwareShop scene (`AIHelperFiles/btc-pools-hardware-plan.md`). ✅ 2026-06-25 — credit model, individual↔casino split + round-robin routing, dynamic fee + proportional distribution, Buy/Discard hardware, hardware-locked speed, bootstrap 1 individual + 0 casino. Foundation for **P5** is in place (ProjectDesignManual Ch. 27). Also: continuous difficulty regulator (Ch. 26) validated, + DEV 100X→9000X time tool.
- [x] Add mempool with 24-transaction block cap (`BlockTemplateBuilder`, cap incl. coinbase).
- [x] Add block template builder (P4 / candidate-block model).
- [x] Add bot mining that can compete with the player (per-node candidates; verified a player can beat a faster bot to a block).
- [x] Update README so future features are not presented as current.
- [x] Update Player Guide so it describes the actual playable state.
- [ ] Run longer Basic Mode manual/autobet tests after transaction circulation exists.
- [ ] **Event-driven design audit** (added 2026-07-21, after the ND.8d round-3 stuck-bidder-escalation fix applied this principle directly). Standing project-wide design rule: `_Process` is for genuinely continuous/real-time work only (the game clock, the background sim loop, autobet animation) — everything else that only re-reads state changing on a discrete event (a bet, a mined block, a transfer, a claim) should be event-driven, the state's owner firing a typed event and consumers subscribing instead of polling on a timer. Full principle, the genuine exceptions, the edge-triggered-signal middle case, and the already-good examples already in this codebase: `CLAUDE.md` "Important Patterns" #6 + `Documentation/ProjectDesignManual.md` Chapter 38. **Before Basic Mode v0.1 is considered complete**, run the dedicated audit pass Chapter 38 describes over every currently-running `_Process` override and migrate the poll-based UI refreshes it lists (≈15 scenes, enumerated in Ch. 38 §38.5) to event-driven design where feasible. Not a blocker on other Basic Mode work — a scheduled cleanup pass, same category as the T1–T3 tasks in §8 below.

## 7. Design Questions Still Open

- What exact threshold lets the casino start repaying bank debt?
- Should minimum wager requirements be weekly, monthly, or both from the start?
- How harsh should fee penalties be for missing minimum wager requirements?
- Should cashback decrease as a penalty, or should penalty design stay focused on conversion fees first?
- How much bot betting history should the player see for free?
- Should deeper bot history be a paid service, a level unlock, or a later feature?
- When exactly should BTC trading unlock in Basic Mode?
- Should private mempool fees be available in Basic Mode or postponed?

## 8. Tech-Debt & Cleanup Tasks

T1–T3 (2026-06-24, ✅ all implemented) came out of the clock/persistence bug fixes — see `Documentation/ProjectDesignManual.md` §24.8. **T4 (2026-07-29, OPEN) is a standing technical objective**, not a scheduled task: it is the structural answer to INC-001 and to the progressive frame-rate decay observed in the same run. **T5** (2026-08-06) is deferred by decision. **T6** (2026-08-21, ✅ Dep-01 done, pass 2 open) is the newest and the odd one: its subject is not the game but **the files Claude reads**, an artefact class the project had never treated as one — and it took a 228,348-character `CLAUDE.md` going unnoticed to make that visible.

### T1 — Stop transactions/consensus from committing financial state to disk ✅ DONE (2026-06-24)

Closed the "known edge" left open by the **block = the only commit to disk** model: a mid-session BTC send / consensus round used to flush in-memory SC balances to disk, so an app restart would *not* fully revert to the last block.

- **Was**: `NetworkRoot.PersistStateToDisk()` serialized the *whole* snapshot — chain, pending tx, wallets **and** the live `NodeFinancialStates` — and was called outside block-mining by `CreateAndBroadcastTransaction`, `CreateAndBroadcastTransactionToAddress`, and `RunConsensus`.
- **Fix shipped (stronger than first planned — *nothing* persists between blocks)**: the between-block `PersistStateToDisk()` calls in `CreateAndBroadcastTransaction`, `CreateAndBroadcastTransactionToAddress`, and `RunConsensus` were **removed**. Those actions now only mutate the in-memory chain/mempool; `PersistStateToDisk()` runs only at block-mining (`HandleMinedBlock`), baseline node creation, and startup. (The first attempt — an `includeFinancialState` flag that still persisted the pending tx — was discarded: it would have left a pending tx with its own `Timestamp` on disk while the clock/balances reverted, an inconsistent half-state.)
- **Result**: a BTC tx between blocks lives in the mempool and becomes durable only when the next block is mined; close the app before that and the whole world — clock, balances **and** un-mined pending transactions — reverts to the last mined block. A block is the only commit.

### T2 — Remove dead Block-Mining / maintenance UI from BlockExplorer ✅ DONE (2026-06-24)

These controls predated the background simulation + real-time auto-refresh and had no purpose.

- **Mine button** (`%MineButton` → `OnMinePressed`): removed — manual block minting is obsolete (mining is bet-driven for the player + background-sim for bots). `NetworkRoot.MineAndBroadcastBlock` is **kept** (still used by `RunWeightedBlockLottery`).
- **Consensus button** (`%ConsensusButton` → `OnConsensusPressed`): removed, **and the code path with it** — `NetworkRoot.RunConsensus` and `NetworkSimulator.RunConsensusRound` deleted. They were a no-op: every node already shares one canonical chain (`BroadcastBlock` → `TryAcceptMinedBlock`), so longest-chain reconciliation had nothing to do. (Revisit with fork simulation — see the Post-Basic-Mode item below.)
- **Refresh button** (`%RefreshButton` → `OnRefreshPressed`): removed — redundant with `BlockExplorer._Process`'s 1 s auto-refresh.
- Also removed the now-unused `ActionFeedbackLabel` (only the three deleted handlers wrote to it) and retitled the lone-`%MinerNodeOption` section from "Mining / Consensus" to **"Inspect node"**.
- **Kept**: `%MinerNodeOption` (the node-context selector reused by the tx/address/block lookups).
- **Result**: BlockExplorer shows only live, read-only inspection controls; no orphaned mine/consensus/refresh nodes, handlers, or consensus code remain.

### T3 — DiceGame docked mining display shows stale difficulty ✅ DONE (2026-06-24)

The mining readout embedded in DiceGame did not track the live (retargeted) difficulty the way BlockExplorer does.

- **Was**: DiceGame's `BuildMiningStatusLine` used `Blockchain.GetExpectedAttemptsForCurrentDifficulty()`, which returns `EffectiveDifficulty(Chain[^1])` — the **last already-mined block's** difficulty, ignoring the live `_activeMiningPower` feed-forward. BlockExplorer instead used `NetworkRoot.GetPlayerNextBlockDifficulty()` (the locked candidate difficulty, else the prospective next-block difficulty at current power), so only it reflected the retarget.
- **Fix shipped**: extracted a shared `GetNextOrCandidateDifficulty(node)` helper (locked candidate difficulty, else `GetNextBlockDifficulty(_activeMiningPower)`); `GetPlayerNextBlockDifficulty()` now delegates to it, and `BuildMiningStatusLine` uses it too — so both readouts compute the in-progress block's difficulty from the same live source. The DiceGame line was relabelled `Mining difficulty: {x:F2}  (~{x:F0} attempts/block)` to match the Block Explorer format. `GetExpectedAttemptsForCurrentDifficulty()` is left in place (now only a validation helper referenced by `100x-time-scale-migration-plan.md`).
- **Result**: the DiceGame mining display and BlockExplorer agree on the in-progress block's difficulty and both update live as power/difficulty change.

### T4 — Simulation-Scale Refactor: chain state, stats persistence & a cost budget — 🎯 OPEN technical objective (not scheduled)

**Why this exists.** Two signals from the same 2026-07-29 run: **INC-001** (`Documentation/INCIDENT_LOG.md`) — a 1.13 GB bet journal and a world that failed to load silently — and the developer's report that **fluidity at 9000X decayed progressively over the last days of the playtest ("cada vez más lag")**. P15.11 fixed the *durability* half. This entry is the *scale* half: the reason those numbers were allowed to grow at all. Design context: `ProjectDesignManual.md` **Chapter 40**.

**Not a priority.** Nothing here blocks Basic Mode, and none of it should interrupt the bank testing. It is written down now because the run that exposed it is the largest world this project has produced, and that evidence has a short half-life.

#### T4.0 — What was actually measured (2026-07-29), so the proposals are not guesses

1. **The bet journal is PLAYER-ONLY — the bots cost nothing.** `UserStatsService.OnBetExecutedRegisterBet` has exactly two callers: `DiceGame` (manual bets) and `SimulationService.ExecutePlayerBetOnce`, the latter guarded by `if (_config.IsPlayerActive)`. Bot bets go to `CasinoClientLedgerService.RegisterSettledBet`, which keeps **aggregate `ClientBetStats` counters** (bets/wins/losses/wagered/net) flushed on a 1 s dirty flag. So the four bots — who bet far more than the player — are ~O(1) each, while the player's own bets are the millions of rows. **The correct pattern is already in this codebase; it was simply never applied to the player.** That answers the open question directly: it is the player, not the bots, and not the two together.
2. **Per-block cost grows with chain length, which is the shape of "increasing lag".** Every node keeps its **own** `BlockchainService` (~62 of them: player + 4 bots + 40 non-miners + 14 cast miners + casino + 3 founders). `NetworkSimulator.BroadcastBlock` calls `TryAcceptMinedBlock` on **every** node, each bumping its own `_chainVersion` — which invalidates **every** node's UTXO cache. `GetUtxoSet()` then rebuilds by **replaying the entire chain**, and `AggregateSpendable(node)` reads `node.Blockchain`, so a balance query on N distinct nodes in one block triggers N full replays. At 1,666 blocks × up to 24 txs that is ~40,000 tx-visits per rebuild, and the bot/company/bank sweeps query dozens of nodes per block. **The per-block cost therefore rises linearly as the chain grows** — a run that starts smooth and degrades, exactly as reported.
3. **The world snapshot is 9.25 MB rewritten every block**, also linear in chain length.
4. **Heap pressure:** ~5.3M `BetRecord` objects (est. 1.5–2.5 GB) before the wipe, on an Intel UHD 620 laptop — rising Gen2 GC cost as the list grows.

> **(2) is a strong structural hypothesis, not a profiled fact.** It has the right shape and the right growth curve, but nothing has been timed. See T4.6 — measure before refactoring anything.

#### T4.0b — What the RESUMED run measured (2026-07-30), which partly contradicts T4.0

The P15.8 world was played on to **~Oct 2014 / block 2699** after the P15.11 repair, and `difficulty_trace.csv` now covers 1,472 blocks. Reading it changes two of the assumptions above (full audit: `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` §10, finding F5; decision D-15.37):

1. **The R2 difficulty regulator is confirmed correct** — mean solvetime **62,373 s against the 58,500 s target (+6.6%)**. Block pace is no longer a suspect in anything.
2. **The retention throttle is chronically ~0.71 and does NOT decay with chain length.** By 200-block bucket: `0.752 · 0.693 · 0.801 · 0.806 · 0.653 · 0.599 · 0.706 · 0.688`. A cost that grows linearly with chain length would show a monotone downward trend across 1,500 blocks and does not. **This does not refute T4.0 (2)** — the run is a window inside an already-large chain, so a linear term could be present and swamped — but it does mean the *dominant* term behaves like **chronic saturation plus periodic per-block spikes**, which matches the developer's description ("processes a second, stalls a second") better than a smooth linear decay. **T4.2's urgency is mildly demoted; T4.6 is confirmed as the correct first move.**
3. **Two per-block cost sources the original T4.0 did not name**, both new since it was written:
   - **Dividend-claim transactions: ~8.66 per block** (13,691 `bot_claim` rows over 1,586 blocks). Each is a real on-chain send. Besides the frame cost this saturates the mempool — `pendingTxs` sat at **26–28** against a 24-tx block cap and an ND.4a historical budget of ~5 — so the automated transaction layer has been at `owed = 0` for most of the run. Fixing it is scheduled in **Step 16** (settle bot claims internally, or per company at quarter close, instead of per holder on-chain); it is listed here because it is also a *performance* item.
   - **Telemetry I/O**: five CSV traces appended per block, `company_governance_trace.csv` alone reaching 2 MB. Small individually, but it is per-block file I/O sitting next to the snapshot write.
   - Also note **P15.11b's atomic write doubles the snapshot's write volume** (`.tmp` then rename). That was the correct trade, and it belongs in the budget T4.6 measures rather than being reverted.

**Instrument these five together** in T4.6 — UTXO rebuilds, snapshot serialize+rename, governance tick, the bot/company sweeps, and claim-transaction construction — plus managed heap and block index. That set now covers every hypothesis on the table.

#### T4.1 — Incremental UTXO maintenance (highest leverage, lowest risk)

Applying a newly-accepted block's transactions to the cached UTXO set costs **O(txs in that block)**; replaying the chain costs **O(all txs ever)**. `TryAcceptMinedBlock` / `MineBlock` already know exactly which block arrived, so the incremental update is a few lines beside the existing `_chainVersion++`. Keep the full replay for `TryReplaceChain` (a genuine chain swap) and as a DEBUG-only cross-check — *assert the incremental set equals the replayed set* every N blocks, which is the §39.16-rule-1 shape: the cheap path must be provably identical to the truthful one.

#### T4.2 — One canonical chain + one UTXO set, instead of 62 copies

The per-node `BlockchainService` is already acknowledged as vestigial — `NetworkRoot` (~L5209) says *"single-shared-chain design (every node already holds the same canonical chain via BroadcastBlock)"*, and `RunConsensusRound` was deleted at T2 for the same reason. A single shared chain + one shared UTXO index, with `NodeAgent` reduced to identity + owned addresses, removes the 62× invalidation storm at its source and collapses `BroadcastBlock` to a no-op. **Blocked-by-design consideration:** per-node chains are the substrate the deferred **Divergent Chains / Fork Simulation** wants. The honest resolution is to make the shared chain the default and reintroduce per-node chains *deliberately* when forks ship, rather than paying for 62 unused copies for years.

#### T4.3 — Address → outpoint index

`GetSpendableUtxos(addresses)` walks the whole UTXO set. An `address → outpoints` dictionary maintained alongside T4.1's incremental update makes every wallet panel, bot affordability check and company treasury read **O(that node's outpoints)**. This is the natural follow-on to the R3 fix (§38.7) that already collapsed `AggregateSpendable` from `O(addresses × utxos)` to `O(utxos)` — T4.3 takes it to `O(owned)`.

#### T4.4 — Player stats as aggregate counters; the journal as a bounded recent window

Generalize `ClientBetStats` to **all five clients including the player**, so lifetime totals are maintained incrementally and are exact regardless of retention. The per-bet journal then serves only what genuinely needs rows — `BetsHistoryExplorer`, `CalendarsNavigator`, DiceGame's recent list — and can stay retention-capped (P15.11d) without the "General" scope having to apologize for it. This retires the current situation where *lifetime totals are derived by replaying every record ever written*, which is both the memory cost and the reason F4's double-counting was invisible.

#### T4.5 — Bounded / incremental world persistence

The snapshot rewrites the full chain per block. Options, cheapest first: (a) **split the chain out** of `state.json` and append only new blocks (the mutable governance/financial state stays a small whole-file atomic write); (b) write the chain in **immutable sealed segments** (the deleted `blocks-*.json` had the right *idea* and the wrong *lifecycle* — it rewrote all of them every block and nothing ever read them); (c) leave it and accept the cost, which is defensible below a few thousand blocks. Whatever is chosen must keep P15.11b's atomicity and the "a block is the only commit" contract intact.

#### T4.6 — A per-block cost budget (DO THIS FIRST)

Everything above is a hypothesis until the frame is instrumented. Add per-block timings to the existing `difficulty_trace.csv` (which already carries R2's `simSecOffered,simSecConsumed`, the honest retention signal that caught §38.7's inverse-poll defect): **ms spent in UTXO rebuilds, in snapshot serialization, in the governance tick, in the bot/company sweeps, plus managed heap size and block index.** Then the question "why did it get slower over three days?" is answered by reading a column instead of reasoning about it — and each T4 item above can be accepted or dropped on evidence. §38.7's third rule applies throughout: **a displayed throttle is a measurement, not a diagnosis** — a `SimulationThrottle` below 1 means "find what is eating the frame", never "raise the budget".

**Suggested order:** T4.6 → T4.1 → T4.4 → T4.3 → T4.2 → T4.5. The first two are small and independent; T4.2 is the one that needs a real decision about fork simulation.

### T5 — Dev shell: PowerShell 5.1 → 7 migration — ⏸️ DEFERRED by decision (2026-08-06), not by oversight

**Decision: stay on Windows PowerShell 5.1 until a migration is FORCED.** Reviewed 2026-08-06 and deliberately deferred: the gain is developer comfort, the risk is nonzero, and nothing in the project is blocked. Revisit only on one of the triggers below — not on general preference.

**Current state (measured, not assumed).**

- `powershell.exe` **5.1.26100.8875**, edition `Desktop`, at `C:\WINDOWS\System32\WindowsPowerShell\v1.0\`.
- **`pwsh` is not installed** — no `C:\Program Files\PowerShell`. `winget` is available.
- Claude Code **autodetects `pwsh.exe` and falls back to `powershell.exe`**, so this machine is on the fallback path. Installing PowerShell 7 would flip the agent's PowerShell tool to 7.x **automatically, with no setting to change** — which is precisely why the install decision *is* the migration decision, and why it is recorded here rather than treated as an incidental machine tweak.
- Related and settled the same day: **Python is excluded from this project permanently** (it is not installed; `python`/`python3` on PATH are Microsoft Store execution-alias stubs). See the tool-routing table in `CLAUDE.md` → *Development Best Practices* → *Scripting tools on this machine*. That table is what makes T5 cheap to defer — it routes the work 5.1 is worst at away from PowerShell entirely.

**What 5.1 actually costs us — three recurring frictions, each with evidence in `.claude/settings.local.json`.**

1. **Native exit codes are unreliable under redirection.** In 5.1, redirecting a native executable's stderr (`2>&1`) wraps each line in an `ErrorRecord` (`NativeCommandError`) and sets `$?` to `$false` **even when the exe returned 0**. The allowlist carries ~nine hand-tuned `dotnet build … 2>&1 / 2>$null | Select-String …` variants that exist only to work around this. PS7 passes native stderr through as plain text and `$?` tracks the real exit code.
2. **Encoding.** `Set-Content`/`Add-Content` default to the system ANSI codepage in 5.1. This project's docs are dense with `—`, `§`, `⚠`, `✅`, so appending to a plan or design file through the natural cmdlet is unsafe; the workarounds on record are (a) scripts that strip non-ASCII and rewrite with an explicit UTF-8 BOM, and (b) routing every `.md` append through `[System.IO.File]::WriteAllLines`/`AppendAllText`. PS7 defaults to UTF-8 across the writing cmdlets and removes the class.
3. **Syntax.** No `&&` / `||`, no ternary, no `??` / `?.` — constant minor verbosity, no correctness impact.

**Why deferred anyway.** The two headline PS7 wins — `ConvertFrom-Json -AsHashtable` and faster `Import-Csv` — are already neutralized: `CLAUDE.md` routes JSON/state inspection to `node -e` and CSV telemetry aggregation to `awk`, both of which beat PS7 at those jobs. What remains is friction (1)–(3), which is real but bounded and fully worked around today. Against that sits a small but nonzero regression surface: the out-of-repo dataset scripts (`Get-BtcNetworkDaily.ps1`, `Get-MtGoxDailyVwap.ps1`, `Get-BtcFeeMedianDaily.ps1`, `Merge-…`, `Compare-…`) were written and validated against 5.1 and use `[Net.ServicePointManager]::SecurityProtocol`, `-UseBasicParsing` and `[Net.HttpWebRequest]` — all no-ops or compatible under PS7, *probably* fine, none re-verified. They are finished artifacts whose CSVs are built and committed, so the exposure is low; it is not zero, and "low risk for a comfort gain" is not a trade this project takes mid-flight.

**Reactivation triggers — migrate when ANY of these becomes true:**

- A PowerShell script becomes **load-bearing inside the repo** (today they all live out-of-repo and have already produced their output). A script that must run repeatedly, in CI, or as a hook needs trustworthy native exit codes — friction (1) stops being cosmetic.
- **A new dataset-acquisition round** (the Step-13/Step-14 pattern: bulk download, decompress, join, spot-check). That work is where 5.1's slow `Import-Csv` and encoding defaults bite hardest and where the existing scripts would be edited anyway — migrating *with* that work costs almost nothing extra.
- **An encoding corruption reaches a committed file.** The moment friction (2) produces a real defect rather than a workaround, the workaround has failed.
- **Automation gates on a build result** — a hook, a pre-commit check, or any flow whose control path reads `$?` from `dotnet build`.
- **The harness or tooling drops 5.1**, or a needed cmdlet/parameter is 7-only.

**How to do it when the time comes (the trap is already identified — do not rediscover it).**

- Install to the **standard machine path** `C:\Program Files\PowerShell\7\`: `winget install --id Microsoft.PowerShell --source winget --scope machine`.
- **Do NOT install from the Microsoft Store.** The known VSCode-extension failure to pick up `pwsh` ([claude-code issue #54335](https://github.com/anthropics/claude-code/issues/54335), closed) had a Store, user-scoped `pwsh` as its root cause — the autodetection did not find it. This is the *same* packaging trap that produced the Python stub: a Store package putting the binary where the detector does not look.
- Restart the extension so detection re-runs; confirm the tool reports 7.x before trusting anything.
- **Then:** update the `CLAUDE.md` note that currently reads *"Note PowerShell here is 5.1, not 7"*, and re-run the out-of-repo dataset scripts once to confirm (2) above.
- **Reversible:** PS7 installs **side-by-side** and never replaces 5.1. Uninstalling `pwsh` returns the agent's tool to `powershell.exe` with no other change.

### T6 — The Claude context system ✅ Dep-01 DONE (2026-08-21), pass 2 open

A third artefact class the project had never treated as one: **the files Claude reads**, which have their
own failure modes and had no plan, no policy and no measurement.

**What happened.** `CLAUDE.md` reached **228,348 characters** unnoticed, paid for on every message of every
session. A single table cell held 32,104; one section held 57,722 of design record labelled as status. It
was found by accident, not by review.

**Fixed across an emergency pass plus `AIHelperFiles/dep01-claude-context-depuration-plan.md`:**
**228,348 → 70,295, a 69% reduction, with no permanent instruction removed.** A **Document Policy** now
sits near the top of `CLAUDE.md` (what belongs, what does not and where it goes, the mandatory procedure
before writing, a 60k/100k/150k budget); four sections were cut or extracted into `ARCHITECTURE.md` and
`SCENES.md`; and a size guard runs on **both** `PostToolUse` and `SessionStart`.

**The finding worth carrying, because it was the same in all four cuts:** the material was not merely
surplus, **it had stopped being true and nothing could show it.** The file tree listed 16 of 24 entries and
omitted `MainMenu`; Scene Management declared a migration pending directly above the paragraph saying it
was complete; Important Patterns' own premise was false. **A file tree carries its authority in its shape,
a code sample goes stale *and gets copied*, and a dated narrative reads as a live rule — none announces
itself.** Which is why the guard matters more than any cut: what failed was not the size, it was that
nothing measured it.

**Two-event wiring is the part to preserve if this is ever revisited.** No hook observes a manual editor
save, so a `PostToolUse`-only guard would report a clean bill of health while the developer's own edits
doubled the file — which is most likely how it reached 228k. `SessionStart` closes that half.

**Open — Dep-01 D4 pass 2.** The sweep covered `CLAUDE.md` against `GLOSSARY.md` and the code: one framing
collision (fixed), three redundancies (reported, not touched), and four measurable baselines re-run and
still holding. **Not covered: `Documentation/` against itself — where 949,757 of the ~1.02M characters
are.** The three contradictions found during the emergency pass were all spotted by hand while doing
something else, so the density there is probably higher than one finding suggests. Start with the newly
extracted docs: an extraction is the freshest opportunity for a copy to drift.

