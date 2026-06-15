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
  - Basic Mode halving interval: `4,381 blocks`, intentionally scaled to about four in-game years at roughly three blocks per in-game day.
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
- Basic Mode halving: `4,381 blocks`, not Bitcoin's real `210,000` blocks.
- Hardware: increases bets/nonce attempts per real second; it does not alter game-time speed.
- Mining rule for now: `1 bet = 1 nonce attempt`.
- Bot mining: required in Basic Mode.
- BTC cannot be used directly for betting.
- Multiplayer, DLCs, multiple casinos, and cloud persistence are postponed until the core loop is fun and data volume requires more infrastructure.

## 5. Implementation Priorities

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
- Limit Basic Mode blocks to `48 transactions` for now.
- Include transaction fees in block rewards.
- Let bots mine competing blocks using their own candidate blocks.

Done when blocks contain believable transactions and bots can win blocks before the player.

### P4 - Block Template Builder

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
- [x] Basic halving scale defined: `4,381 blocks`.
- [x] Last block and next reward are visible in DiceGame.
- [x] Block checkpoints restore financial state.
- [x] Saved strategies work as development/player-owned strategies.
- [x] User-facing DiceGame label uses `Main Balance`.
- [ ] Clarify auto-recharge behavior in UI and docs.
- [x] Add player BTC wallet and addresses.
- [x] Add bot/non-node wallet address model.
- [x] Add casino BTC addresses.
- [x] Add `CasinoFinances` development scene.
- [ ] Add scheduled bot transactions.
- [ ] Add public mempool with 48 transaction block cap.
- [ ] Add simplified block template builder.
- [ ] Add bot mining that can beat the player.
- [x] Update README so future features are not presented as current.
- [x] Update Player Guide so it describes the actual playable state.
- [ ] Run longer Basic Mode manual/autobet tests after transaction circulation exists.
