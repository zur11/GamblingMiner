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
- Founder entities: `Satoshi` (dominant early miner; target `11,000 BTC` ≈ 1% of his real ≈1.1 M; retires no earlier than `2011-04-26`), `Hal` (joins `2009-01-11`, mines exactly 3 bootstrap blocks), `Mike Hearn` (joins ~April 2009, after the player). Founders mine without needing SC/BTC, like the casino. Detail: `AIHelperFiles/historical-founders-and-bootstrap-plan.md`.
- Game start: a first-launch bootstrap pre-mines the chain from genesis (`2009-01-03`) to `2009-03-21` (Satoshi + Hal only), so the player always begins on `21 March 2009`. From player start onward, in-game time always follows player bets.
- Network-growth model: participants appear over time (`Satoshi → Hal → player → miner bots gradually`), not all at block 1. Autonomous (no-bet) mining happens only during the bootstrap window; reserved otherwise for future expansions/DLC/multiplayer.
- Coinbase recipients use derived `gm1q…` addresses (real base58 kept only as commented reference; genesis coinbase stays unspendable).
- Balance model: account/balance-based is a **testing-stage** simplification; the target is a realistic **UTXO** simulation surfaced via passphrase wallets (a fresh address per receive — the "Patoshi pattern").
- Block-candidate + hashrate model: the **keystone** shared by founder mining, hardware pools, and the block template builder. Minimal weighted-lottery first; full per-node template deferred to P4.

## 5. Implementation Priorities

> **Authoritative implementation order**: `AIHelperFiles/IMPLEMENTATION_ROADMAP.md`. The priorities below are the *feature* breakdown; the roadmap file holds the *sequencing and dependencies*.
>
> **State (2026-06-20):** P0–P2 done. PH historical foundation reached its **accepted baseline** — the game starts on **21 Mar 2009** on a Satoshi/Hal-mined chain (founder identity + genesis fix + weighted lottery + first-launch bootstrap all verified in-engine). **The active next work is now P4 — the per-node candidate block model (the real competition engine)** — built generically with historical characters treated as plain nodes (`AIHelperFiles/candidate-block-model-plan.md`). The remaining historical-character economics (Satoshi 11,000-BTC ramp + disappearance, the 12 Jan 10 BTC tx) are **parked** and re-activate *after* the candidate engine, built on top of it.

### PH - Historical Foundation — ✅ BASELINE REACHED

Goal: establish the historically faithful opening and the network-growth init model **before** economy systems expand on top of it.

- [x] Founders Satoshi & Hal as mining nodes with seed phrases; `FoundersWallets` dev scene (room for Mike Hearn).
- [x] Fix genesis & early coinbase recipients to derived `gm1q…` addresses. *(InputData 100-byte cap still deferred.)*
- [x] Block-candidate + hashrate model **(minimal weighted lottery)** — the keystone seed for P4.
- [x] First-launch bootstrap pre-mine to `21 March 2009` (Satoshi dominant, Hal exactly 3). **Verified in-engine.**
- [ ] **Parked → after P4:** Satoshi `11,000`-BTC dynamic ramp (retire ≥ `2011-04-26`); the `12 Jan` 10 BTC Satoshi→Hal tx; April Mike Hearn transfers. Re-activated once the candidate engine exists, built on top of it.
- Companion research: `historical-blockchain-events-research.md` (UTXO/Patoshi direction; remaining address-reuse research).

**Baseline reached:** a new game starts on `21 March 2009` with a Satoshi/Hal-mined chain and the player bets from there. ➡ **Next: P4 (candidate block model).**

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

> Implemented as `AIHelperFiles/candidate-block-model-plan.md` (roadmap Step 4 — slices 4a/4b.1/4b.2/4b.3/4c). Each node builds its own candidate block from its mempool view: fee-ordered tx selection (24-tx cap incl. coinbase), Merkle root, coinbase = reward + collected fees, real block-header hashing, content-hash txids, and coinbase maturity N=1. BlockExplorer surfaces it; BTCWallet has a player fee selector. The bootstrap/lottery already mine through this engine (the refactor was in-place), so the founder economics (parked) layer straight on top in Step 7. **Next lead: miner-bot gradual introduction + hardware pools (roadmap Step 6).**

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

### Post-Basic Mode — Casino Referral System

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

### Post-Basic Mode — Casino Rank System

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

---

## 6. Design Questions Still Open

- What exact threshold lets the casino start repaying bank debt?
- Should minimum wager requirements be weekly, monthly, or both from the start?
- How harsh should fee penalties be for missing minimum wager requirements?
- Should cashback decrease as a penalty, or should penalty design stay focused on conversion fees first?
- How much bot betting history should the player see for free?
- Should deeper bot history be a paid service, a level unlock, or a later feature?
- When exactly should BTC trading unlock in Basic Mode?
- Should private mempool fees be available in Basic Mode or postponed?

## 7. Basic Mode v0.1 Checklist

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
- [x] Add `CasinoFinances` development scene.
- [x] Add scheduled bot transactions (core scheduler; circulation trigger to be re-aligned for gradual bot introduction).
- [x] **PH**: Founders Satoshi & Hal as nodes + `FoundersWallets` dev scene (implemented; pending in-engine verification). *Note: they do not mine yet — that arrives with the weighted lottery below.*
- [x] **PH**: Fix genesis/early coinbase to derived `gm1q…` addresses (genesis stays unspendable).
- [x] **PH**: Block-candidate + hashrate model (minimal weighted lottery) — the keystone seed (verified). Full per-node candidate engine = **P4 ✅ DONE**.
- [x] **PH**: First-launch bootstrap to 21 Mar 2009 (Satoshi dominant, Hal exactly 3) — verified in-engine.
- [ ] **Parked → Step 7 (after the candidate engine)**: Satoshi 11,000-BTC ramp + disappearance, 12 Jan 10 BTC tx, April Mike Hearn transfers (built on the candidate engine).
- [ ] Add non-miner bot donation tracking (donor-per-bot ledger; groundwork for casino referral system).
- [ ] Add Winning Referral Commission scene (list referrals, claimable 1% SC commission per bot, claim button).
- [ ] Add hardware credit system with casino community mining pool, per-node pool assignment, and BTCPoolsAndHardwareShop scene (`AIHelperFiles/btc-pools-hardware-plan.md`).
- [x] Add mempool with 24-transaction block cap (`BlockTemplateBuilder`, cap incl. coinbase).
- [x] Add block template builder (P4 / candidate-block model).
- [x] Add bot mining that can compete with the player (per-node candidates; verified a player can beat a faster bot to a block).
- [x] Update README so future features are not presented as current.
- [x] Update Player Guide so it describes the actual playable state.
- [ ] Run longer Basic Mode manual/autobet tests after transaction circulation exists.

## 8. Tech-Debt & Cleanup Tasks (annotated 2026-06-24 — not yet implemented)

Three concrete tasks identified while fixing the clock/persistence bugs (see `Documentation/ProjectDesignManual.md` §24.8). **Annotations only — no implementation yet.**

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

### T3 — DiceGame docked mining display shows stale difficulty

The mining readout embedded in DiceGame does not track the live (retargeted) difficulty the way BlockExplorer does.

- **Root cause**: DiceGame's `BuildMiningStatusLine` uses `Blockchain.GetExpectedAttemptsForCurrentDifficulty()`, which returns `EffectiveDifficulty(Chain[^1])` — the **last already-mined block's** difficulty, ignoring the live `_activeMiningPower` feed-forward. BlockExplorer instead uses `NetworkRoot.GetPlayerNextBlockDifficulty()` (the locked candidate difficulty, else the prospective next-block difficulty at current power), so only it reflects the retarget.
- **Fix direction**: have the DiceGame readout draw from the same next-block/candidate-difficulty source as BlockExplorer (e.g. expose/consume `GetPlayerNextBlockDifficulty()`), so the docked "expected attempts / difficulty" line matches the explorer.
- **Done when**: the DiceGame mining display and BlockExplorer agree on the in-progress block's difficulty and both update as power/difficulty change.
