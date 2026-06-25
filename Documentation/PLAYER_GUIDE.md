# Player Guide - GamblingMiner

This guide describes the current Basic Mode direction and the parts of the prototype that are already playable.

You begin in early Bitcoin history with a total economy of `40,000 SC`. In specific economy screens, this may be represented as `Main Balance` plus `Bankroll`.

## Core Rule

Every bet has two effects:

1. It resolves a casino Dice roll.
2. It performs one mining nonce attempt.

Current rule: **1 bet = 1 nonce attempt**.

Time only advances when bets happen. If you cannot bet, time stops.

## Your Balances

- **Main Balance**: your reserve outside active betting.
- **Bankroll**: the subaccount used for active Dice bets.
- **BTC Wallet**: receives BTC from mined blocks.

Game over happens only when `Main Balance + Bankroll` reaches zero. If Bankroll reaches zero while Main Balance still has funds, the player should be able to recharge Bankroll and continue. If auto-recharge is disabled, the Dice result area should warn that funds can be moved from Main Balance to Bankroll.

## Playing Dice

### Manual Betting

Click the roll button to place one bet.

- One click places one Dice bet.
- One click advances the game clock by the current bet tick.
- One click performs one mining attempt.

### Autobet

Autobet repeats bets using the current strategy and speed settings.

Use autobet when you want time, betting, and mining attempts to continue without clicking manually.

Current time scale target:

- 10 real minutes = 16 in-game hours 40 in-game minutes.
- 1 auto-bet tick = 100 in-game seconds.

Hardware will later increase the number of bets/attempts per real second, but hardware will not directly accelerate game time.

## Betting Strategies

Strategies can be saved and loaded during development.

Common parameters include:

- Base bet.
- Chance to win.
- High/Low direction.
- Increase on loss.
- Increase on win.
- Stop on loss.
- Stop on profit.
- Stop on block mined.

Martingale-style strategies can work for short periods, but the casino edge and limited bankroll make them risky over time. The game does not need to punish bad strategies directly; variance and house edge already do that.

## Mining

Each bet attempts to mine the next block.

When a block is mined:

- The winning miner receives the block reward.
- The latest block data is updated.
- A checkpoint saves your full state (time, balances, and the blockchain).
- The block can be inspected in the Blockchain Explorer.

**Your progress is saved only when a block is mined.** As you play, time, balances, and pending transactions advance freely — but they become permanent only at a mined block. If you close the game *without* mining a block, on reopen everything rewinds to the last mined block: the clock, all balances, and any transactions not yet included in a block. Mining a block is what locks in your progress.

Basic Mode uses a scaled halving interval of `2,100 blocks`, not Bitcoin's real `210,000` blocks. The initial block reward is 50 BTC and the total supply converges to 210,000 BTC by approximately in-game year 2141.

## Bots

Miner Bots are intended to be real competitors in Basic Mode. They should be able to mine blocks before the player.

The fuller bot system is still being designed. The target design includes:

- Mining bots.
- Non-mining bot wallet participants.
- Casino BTC addresses.
- Scheduled transactions between wallets.
- A shared public mempool.
- A simplified 24-transaction block cap.

The player should eventually be able to inspect recent bot bets and infer their strategy parameters. Full strategy visibility is not planned; the player learns by observing recent behavior.

## Blockchain Explorer

The Blockchain Explorer is used to inspect:

- Latest block data.
- Blocks by height or hash.
- Transactions.
- Addresses.
- Node balances and pending transactions.

It is currently one of the best ways to understand what the mining prototype is doing.

## Planned Systems

These systems are part of the design direction but should not be treated as finished gameplay yet:

- Historical hardware progression.
- BTC/SC trading.
- Casino BTC reserves and debt tracking.
- CasinoFinances development scene.
- More complete bot economy.
- Private mempool and fee market.
- Achievements.
- Additional casino games.
- Multiplayer.

## Basic Survival Tips

- Keep enough Bankroll to keep betting.
- Keep enough Main Balance to recover from a depleted Bankroll.
- Use stop conditions when testing aggressive strategies.
- Watch mined blocks and reward state.
- Treat autobet as a tool for long sessions, not as a guarantee of profit.

