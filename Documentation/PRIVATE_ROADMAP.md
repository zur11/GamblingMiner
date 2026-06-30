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

## 5. Implementation Priorities

> **Authoritative implementation order**: `AIHelperFiles/IMPLEMENTATION_ROADMAP.md`. The priorities below are the *feature* breakdown; the roadmap file holds the *sequencing and dependencies*.
>
> **State (2026-06-30):** P0–P2 + the candidate engine (P4/Step 4), difficulty regulator + hardware pools (Step 6), **founder economics (Step 7)**, **Step 8 — UTXO realism / address non-reuse**, and **P10 — network fee activation** are all done & verified in-engine. The game starts on **21 Mar 2009** on a Satoshi/Hal-mined chain; founders mine concurrently in the player era; the chain runs a real multi-input/multi-output UTXO model; the whole network is fee-free before 2009-04-26 and all participants pay fees after (see `AIHelperFiles/step10-network-fee-activation-plan.md` + P10 in §5). **The active next work is Step 9 — economy/meta (P6–P8)**; carried-forward deferrals: bots multi-address (OQ-8.2), deposit-address rotation (OQ-8.3), the optional Patoshi forensic view (8.5).

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
- Game over only happens when Main Balance plus Bankroll reaches zero.

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

### P6 - Casino Finances

Goal: track the casino as an economic actor.

- Track casino SC income from player and bot losses.
- Track casino SC expenses from player and bot wins.
- Model an infinite bank credit line at first.
- Track casino debt to the bank.
- Once reserves pass a TBD threshold, the casino can start repaying bank debt.
- Interest is postponed.
- Add a `CasinoFinances` scene accessible from `DiceGame` during development.
- Later, hide this scene from normal players.

Done when casino profit/loss can be audited internally.

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

### P10 - Network Fee Activation (~2009-04-26) ✅ DONE (2026-06-30)

Goal: make the simulated network historically faithful to early Bitcoin's **fee-free era**, then switch the whole network to paying fees on one date — resolving the current dev-time contradiction (scripted historical txs are fee-free while bots/casino attach fees from block 1).

- **The whole network is fee-free until a `FeeActivationDate` ≈ 2009-04-26** (the nearest mined block, just after the 18 Apr Hearn round-trip). On/after that block, **every** participant (player, bots, casino, founders) begins paying fees.
- Gate points to flip to 0 before the date, restore after: the bot fee in `NetworkRoot.ScheduleBotTransactionsAfterBlock` (`MinBotFeeBtc`/`MaxBotFeeBtc`), `CasinoTxFee`, and the player's default/selected fee. The candidate-block fee-collection engine is unchanged (it already collects `ΣFee`); this only gates whether a fee is *attached*.
- Provisional date `2009-04-26`; resolve to the nearest block by timestamp (dates are the source of truth — Q-E1).
- **Own branch** (e.g. `network-fee-activation`); does not block other Basic Mode work.
- Design: `AIHelperFiles/step8-utxo-realism-plan.md` OQ-8.7 + `IMPLEMENTATION_ROADMAP.md` ("What's next"). Tracked in the §6 checklist.

Done when no fee is attached by any participant before the activation block, and all participants attach fees on/after it, validated in-engine across the April 2009 boundary.

**Delivered (2026-06-30):** `NetworkFeePolicy` static class (single source of truth: `ActivationDateLocal = 2009-04-26`, `DefaultFee/MinFee/MaxFee`); fee row hidden before the date, default 0.1 BTC after, clamp 0.1–1.0 on send, across all four BTC wallet send panels (BTCWallet, FoundersWallets, CasinoFinances, BotsBtcWallets); sender balance label on every send panel; "Go Back" button rename; backend gates for bot automated fees and casino pool-payout fees (`NetworkFeePolicy.IsActiveByTimestamp`). Also delivered in the same phase: **casino pool distribution atomicity fix** (one multi-output tx per event — eliminates partial/double-payment bug); **Block Explorer full multi-output tx display** (full `Inputs[]`/`Outputs[]` iteration, all txs in a block shown); **OQ-8.2 cosmetic filter** (`IsSelfChangeTransaction` + `ExternalOutputs` in `BlockExplorer.cs` — hides bot change-to-self from display until bots have `DerivedAddressWallet`). Full detail: `AIHelperFiles/step10-network-fee-activation-plan.md`.

### Casino Referral System (Basic Mode)

Goal: give non-miner holder bots (`non_miner_1`..`non_miner_10`) a social and economic role in the casino ecosystem, and give the player an organic reason to donate BTC to them.

**Referral auction mechanic** (decisions resolved 2026-06-21 — see `scheduled-bot-transactions-plan.md` → Resolved Decisions):
- Each non-miner bot runs a **7-day in-game auction window** (starting from the bot's creation block timestamp; genesis timestamp for the initial 10 bots).
- Non-miner bot addresses are visible in BlockExplorer; a toggleable **"Enroll Mode"** (default off) filters the explorer to only still-recruitable (un-enrolled) non-miners.
- The participant with the highest total confirmed BTC donation when the window closes becomes that bot's **casino referral** — **permanently**: the bot then **leaves the auction forever** (no renewal). Player and miner bots compete in the same auction; **no cap** on referrals per node.

**Winning Referral Commission** (the only perk):
- **1% → up to 5%** of the referred bot's SC winnings — scales with the referral's **Casino Rank** (top rank = 5%; see the Casino Rank System item below).
- **Always paid by the casino, never deducted from the referral's earnings.**
- Claimable in **real time** in a new **`Referrals` scene** (from MainMenu), which also opens a **Miner Referrals** sub-scene.
- Bot SC winnings come from simulated betting (MartingaleCalculator-derived logic, designed in a later phase).

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

### Post-Basic Mode — Divergent Chains / Fork Simulation (revisit AFTER Basic Mode)

**Deferred, not discarded.** The idea is wanted; the system simply has higher priorities until Basic Mode is finished. **Re-plan this only once Basic Mode is complete.**

Today every node shares one canonical chain (a block is mined → `BroadcastBlock` → every node accepts it via `TryAcceptMinedBlock`), so there are never competing chains. That made the old `RunConsensus` / `RunConsensusRound` a no-op, and it was removed in T2.

Goal (post-Basic-Mode): model a more realistic P2P network where chains can **diverge** — block propagation delay, two miners finding a block near-simultaneously, **forks**, **orphan/stale blocks**, and **reorgs** — then resolve them with a real **longest-chain (most-work) consensus** pass. This is a strong educational fit and layers naturally on top of the per-node candidate-block model (P4).

When revisited, this re-introduces a consensus step (reinstate `RunConsensusRound`-style longest-chain adoption, keyed on accumulated work/difficulty rather than raw length) and the UI to observe forks/orphans in the Block Explorer.

Start when: Basic Mode is complete and stable. Until then, leave mining committing to the single shared chain.

### Post-Basic Mode v1 — Checklist (deferred items)

Items intentionally **not** built for Basic Mode v1 — revisit only once v1 is complete and stable. Each links to its design. (Everything else carried forward — fee activation, casino referral/rank, founder long-term timelines — belongs to **Basic Mode**; see the §6 checklist.)

- [ ] **Patoshi pattern — mining-forensic view (Step 8, Phase 8.5).** An *optional, clearly-labelled cosmetic* Block-Explorer view that highlights Satoshi-mined blocks as a contiguous band (echoing Lerner's ExtraNonce-vs-height plot) with a teaching caption. Our engine can't reproduce the real ExtraNonce/decrementing-nonce/timestamp artifacts (random-nonce search, no ExtraNonce field), so it is an honest stand-in. **This is distinct from address non-reuse** (the many-addresses wallet pattern, already implemented) — the D0 terminology correction already shipped; only this forensic view is deferred. Design: `AIHelperFiles/step8-utxo-realism-plan.md` (Phase 8.5 + OQ-8.5).
- [ ] **Bots multi-address (Step 8, OQ-8.2).** Give miner/non-miner bots their own seed so they get change-address rotation like the player/casino/founders. Blocked today because bots use random/registry keypairs (no stored seed). Revisit with gradual-miner-spawning. Design: `step8-utxo-realism-plan.md` OQ-8.2.
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
- [ ] **Step 11 — Casino SC Gambling Finances** — `CasinoScBalanceService` autoload (99M SC Main Balance + 1M SC Bankroll, target-to-fill auto-recharge, 100M SC re-loan on exhaustion, checkpoint-persisted); `CasinoClientLedgerService` autoload (per-client SC deposit/withdrawal ledger with stat snapshots); SC flow wired per settled player bet; `CasinoGamblingFinances` DEV scene from Main Menu (balances, P/L vs total loans, recharge target controls, manual transfers, nav to sub-scenes); `ClientsBetsHistory` DEV scene (global SC wagered all clients live, per-client all-time + since-last-deposit P/L and wagered, game filter, live bet feed); `ClientsTransactions` DEV scene (full SC deposit/withdrawal history per client, wager-base annotations). See `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md`.
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

## 7. Design Questions Still Open

- What exact threshold lets the casino start repaying bank debt?
- Should minimum wager requirements be weekly, monthly, or both from the start?
- How harsh should fee penalties be for missing minimum wager requirements?
- Should cashback decrease as a penalty, or should penalty design stay focused on conversion fees first?
- How much bot betting history should the player see for free?
- Should deeper bot history be a paid service, a level unlock, or a later feature?
- When exactly should BTC trading unlock in Basic Mode?
- Should private mempool fees be available in Basic Mode or postponed?

## 8. Tech-Debt & Cleanup Tasks (2026-06-24 — ✅ all implemented)

Three concrete tasks identified while fixing the clock/persistence bugs (see `Documentation/ProjectDesignManual.md` §24.8). All three are now done — details per task below.

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
