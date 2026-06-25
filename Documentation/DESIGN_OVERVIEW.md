# Design Overview - GamblingMiner

This document describes the target design for GamblingMiner and marks which parts are implemented, prototyped, or planned.

## 1. Design Pillars

- **Bets move time**: no betting means no time progression.
- **Every bet mines**: current Basic Mode rule is `1 bet = 1 nonce attempt`.
- **Money management matters**: Main Balance and Bankroll should create clear risk decisions.
- **BTC is strategic, not casino money**: BTC is mined and traded, but not directly wagered.
- **Bots are competitors and teachers**: bots should be able to win blocks and provide observable betting behavior.

## 2. Time

Status: Implemented / Prototype.

- Manual bet tick: one bet advances the game clock.
- Current tick scale: `100 in-game seconds`.
- Autobet target: `10 real minutes = 16 in-game hours 40 minutes`.
- Hardware does not directly accelerate time.
- Hardware will increase bets/attempts per real second.

Open question: whether the time scale needs to be accelerated even more later after the Basic Mode economy is stable.

## 3. Economy

Status: Prototype.

Canonical terms:

- `SC`: Stable Coin, simulated USD-pegged currency.
- `Main Balance`: reserve outside active betting.
- `Bankroll`: subaccount used for active betting.
- `BTC`: mined currency, not directly usable in casino games.

Canonical starting funds:

- General docs: `40,000 SC`.
- Specific economy docs: `39,900 SC Main Balance + 100 SC Bankroll`.

Game over:

- Game over occurs when `Main Balance + Bankroll = 0`.
- If Bankroll is zero but Main Balance has funds, the player can continue by recharging Bankroll.

Naming migration:

- User-facing text should use `Main Balance`.
- Some internal code names still use the older principal-balance wording.
- Internal class renames can happen later if they are not worth the immediate risk.

## 4. Dice And Betting

Status: Implemented / Prototype.

Dice uses a 00-99 roll with configurable chance and multiplier.

Strategy parameters include:

- Base bet.
- Chance to win.
- High/Low direction.
- Increase on loss.
- Increase on win.
- Stop on loss.
- Stop on profit.
- Stop on block mined.

The game should not over-punish bad betting systems. Variance, bankroll limits, and house edge already create pressure. Planned weekly/monthly wager requirements should mainly affect conversion fees and possibly cashback when user fails to comply with these requirements, not directly punish individual strategy choices.

## 5. Mining

Status: Implemented / Prototype.

Current rule:

- `1 bet = 1 nonce attempt`.

Current implemented behavior includes:

- Block mining attempts from bets.
- Latest block announcements.
- Block reward visibility.
- Blockchain Explorer.
- Block checkpoints.

**Persistence model — a block is the only commit.** Within a play session, the live clock, all balances, and the mempool advance and survive scene changes (held by the autoloads and the in-memory simulation). Disk persistence happens **only when a block is mined** — navigating between scenes, sending a BTC transaction, or any other between-block action does **not** commit. So closing the app *without* mining a block and reopening it reverts the entire world to the last mined block: the clock, every participant's balance/bankroll (back to its last-block / initial value), **and** any pending transactions not yet in a block (discarded). Mining a block is what makes progress durable. See `Documentation/ProjectDesignManual.md` §24.8.

Basic Mode halving:

- `2,100 blocks`.
- Intentionally scaled for the 100X time model (1 real second = 100 in-game seconds).
- Approximates four in-game years at roughly 1.5 blocks per in-game day.
- Total supply converges to `210,000 BTC` (in-game year ~2141). Same reward curve as real Bitcoin (50 → 25 → 12.5 → ...).

Real Bitcoin's `210,000` block halving interval is not the Basic Mode nor any other mode target.

## 6. Bots, Wallets, And Mempool

Status: Planned / Prototype.

Bots must be able to win blocks in Basic Mode.

Target model:

- Mining bots are nodes.
- Non-mining bots can still own BTC addresses.
- The casino owns BTC addresses.
- Bots and casino can send transactions.
- Public mempool receives pending transactions.
- BTC circulation should begin around block 4 or 5.
- Basic Mode block cap: `24 transactions`.

### Address & UTXO model

- Balances are currently **account/balance-based** (sum of confirmed transactions per address) — a **testing-stage** simplification.
- Target: simulate a **UTXO-style** system as realistically as possible, made tangible through the **passphrase-wallet** system (many addresses from one seed).
- Direction: derive a **fresh address per receive** (coinbase reward or deposit) — the historical **Patoshi pattern** — so spends produce real change outputs and players learn UTXO mechanics hands-on. Founder nodes (Satoshi especially) adopt this first. See `AIHelperFiles/historical-founders-and-bootstrap-plan.md` and `historical-blockchain-events-research.md`.

The player should see recent bot bets, not full bot strategies. The player can infer strategy parameters from visible behavior.

## 7. Block Template Builder

Status: Planned.

The target is a simplified Bitcoin-like block assembly process:

1. Read pending transactions from the public mempool.
2. Score transactions by simplified ancestor feerate.
3. Select transactions greedily until the 24-transaction cap is reached.
4. Tie-break equal fee rates by mempool age.
5. Build coinbase transaction from block reward plus included fees.
6. Compute the Merkle root from the selected transaction order.
7. Mine against the candidate block.

Future extensions:

- Private mempool.
- Fee ranges for withdrawals.
- Higher-fee private transaction routing.
- Manual transaction priority adjustments.
- RBF-like replacement rules.

## 8. Hardware

Status: Planned.

Hardware should increase throughput without changing the core rule.

- Hardware increases maximum bets per real second.
- Each extra bet remains one mining attempt.
- Hardware does not accelerate game time directly.
- For now, Dice can use all available extra attempts.
- Later, the player can allocate attempts across multiple games.
- Current design cap: up to `100 attempts` per 1 real second.

## 9. BTC And Trading

Status: Planned.

- BTC is earned through mining.
- BTC cannot be wagered directly.
- BTC/SC conversion will happen through casino BTC addresses.
- Fees may depend on level, volume, and minimum wager compliance.
- Historical price data is planned but not required for the earliest Basic Mode testing.

## 10. Casino Finances

Status: Planned.

The casino needs internal accounting:

- SC received from player and bot losses.
- SC paid out for player and bot wins.
- Infinite bank credit line at first.
- Debt owed to the bank.
- Later repayment once casino reserves reach a TBD threshold.
- Interest is postponed.

A development-only `CasinoFinances` scene should be accessible from DiceGame while building and testing. It can be hidden from normal players later.

## 11. Achievements

Status: Planned.

Basic achievements should provide goals without forcing a hard win condition:

- First block mined.
- Multiple blocks mined.
- BTC milestones.
- SC milestones.
- Survival milestones.
- Bankroll discipline milestones.

## 12. Deferred

These are intentionally postponed:

- Multiplayer.
- DLC.
- Multiple casino operators.
- Firebase/cloud persistence.
- Full historical hardware data.
- Full historical BTC daily pricing.
- Additional casino games beyond Dice.
