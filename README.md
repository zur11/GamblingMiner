# GamblingMiner

GamblingMiner is an experimental Godot/C# prototype about Bitcoin mining, casino betting, and long-term bankroll management.

The core idea is simple: **time advances only when bets are placed, and each bet is also one mining nonce attempt**. The player starts in early Bitcoin history with `40,000 SC` total funds and tries to survive, mine BTC, and grow their economy over time.

## Project Status

This project is in active prototyping. The current goal is to stabilize **Basic Mode** before expanding into full historical data, deeper bot economies, multiplayer, or cloud persistence.

| Feature | Status | Notes |
| --- | --- | --- |
| Dice game | Implemented | Manual betting and autobet exist. |
| Betting strategies | Implemented | Strategies can be saved and loaded during development. |
| Time progression | Implemented | Bets advance game time. Current scale: 1 bet tick = 100 in-game seconds. |
| Mining attempt per bet | Implemented | Current rule: 1 bet = 1 nonce attempt. |
| Blockchain explorer | Implemented | Blocks, transactions, addresses, and latest block data can be inspected. |
| Block checkpoints | Implemented | Financial state is checkpointed around mined blocks. |
| Main Balance / Bankroll split | Prototype | User-facing naming still needs to be aligned around `Main Balance`. |
| Bot mining | Prototype / Planned | Basic nodes exist, but the fuller bot wallet, transaction, and mempool model is still being designed. |
| Hardware progression | Planned | Hardware will increase bets/attempts per real second, not game-time speed. |
| BTC/SC trading | Planned | BTC cannot be wagered directly. Trading will use casino BTC addresses later. |
| Casino finances | Planned | A development-only `CasinoFinances` scene is planned. |
| Multiplayer / cloud saves | Deferred | Not needed until the core loop is stable and data volume requires it. |

## Basic Mode Direction

Basic Mode should be the smallest complete version of GamblingMiner:

1. Start with `40,000 SC` total funds.
2. Bet manually or automatically in Dice.
3. Each bet advances time and performs one mining attempt.
4. Mine blocks, receive BTC rewards, and persist checkpoints.
5. Compete against bots that can eventually win blocks before the player.
6. Manage Main Balance, Bankroll, strategies, and risk.
7. Continue until bankruptcy or until the player chooses a self-directed goal.

There is no hard victory condition yet. Survival, BTC accumulation, SC growth, and achievements will all be valid goals.

## Current Economy Terms

- **SC**: Stable Coin, a simulated USD-pegged currency.
- **Main Balance**: the player's total reserve outside active betting.
- **Bankroll**: the subaccount used for active bets.
- **BTC**: earned by mining blocks. It cannot be used directly in casino games.

General documentation should describe the start as `40,000 SC`. More specific economy documentation may describe the intended split as `39,900 SC Main Balance + 100 SC Bankroll`.

## Mining Scale

GamblingMiner intentionally uses a compressed mining scale for gameplay.

- Current time scale: 1 bet tick = 100 in-game seconds.
- Autobet target scale: 10 real minutes = 16 in-game hours 40 in-game minutes.
- Basic Mode halving interval: `2,100 blocks` (≈ 4 in-game years; ~1.5 blocks per in-game day).
- Total BTC supply: `210,000 BTC` (converges to in-game year ~2141).

The `2,100` block interval is intentional. At roughly 1.5 blocks per in-game day, it represents about four in-game years. The game runs at 100X time scale (1 real second = 100 in-game seconds) and targets a total supply of 210,000 BTC — the same reward curve as real Bitcoin (50 → 25 → 12.5 → ...) compressed to a game-world scale. Bitcoin's real `210,000` block halving interval is not used in any game mode.

## Development Notes

- Engine: Godot
- Language: C#
- Primary platform: Windows first
- Save format: local Godot/user data for now
- Main scene: `DiceGame.tscn`

## Documentation Map

- `PLAYER_GUIDE.md`: current player-facing guide.
- `DESIGN_OVERVIEW.md`: living design and architecture overview.
- `GLOSSARY.md`: canonical terminology.
- `PRIVATE_ROADMAP.md`: internal roadmap and design priorities.

