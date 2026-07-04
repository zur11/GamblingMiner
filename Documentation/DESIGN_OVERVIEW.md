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
- `Private Bank Account`: **[Implemented — Step 12]** optional SC reserve *outside* the casino; starts at `0`, automation OFF by default; managed in `ScFinances`.
- `BTC`: mined currency, not directly usable in casino games.

Canonical starting funds:

- General docs: `40,000 SC`.
- Specific economy docs: `39,900 SC Main Balance + 100 SC Bankroll` (unchanged by Step 12 — the Private Bank Account starts empty).

Game over:

- **[Updated — Step 12]** Game over occurs when all three SC accounts are empty: `Private Bank Account + Main Balance + Bankroll = 0`.
- If Bankroll is zero but Main Balance (or the bank) has funds, the player can continue by recharging Bankroll / depositing from the bank. Written to allow a future BTC→SC coin-swap rescue.

Naming migration:

- User-facing text should use `Main Balance`.
- Some internal code names still use the older principal-balance wording.
- Internal class renames can happen later if they are not worth the immediate risk.

Player SC Finances hub — ✅ IMPLEMENTED (Step 12):

- The **`ScFinances`** scene is the player's canonical home for SC flows (replacing the retired DiceGame `DepositPopup`): Private Bank Account balance, Main/Bankroll, **Net Worth** (`Bank + Main + Bankroll`) and **Overall P/L** (`Net Worth − 40,000`), deposit/withdraw controls, a 3-scope betting-stats panel, and the bank transfer history. `ScTransactions` shows the player's own Bank↔Main ledger.
- **Private Bank Account** (`PlayerBankAccountService`): an optional reserve outside the casino, starting at `0`, all automation OFF by default — a new player can ignore it for months/years of in-game time. Four flows: manual/auto **deposit** (Bank→Main, bring reserve into play) and manual/auto **withdrawal** (Main→Bank, park winnings safe). Auto-Deposit is a rarely-hit fallback; Auto-Withdraw is the shipped "lock in winnings automatically" mechanism.
- **Bankroll auto-recharge off-switch** (`AutoRechargeEnabled`, default ON): a `BankrollProgrammer` toggle (mirrored by the DiceGame strategy-panel toggle, now a proxy to the same flag).
- See `ProjectDesignManual.md` Ch. 32 and `AIHelperFiles/step12-player-sc-finances-plan.md`.

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
- **Founder economics (Step 7, implemented):** Satoshi & Hal mine concurrently with the player in the player era — in lockstep with the player's bets, never advancing the clock on their own (`FoundersMiningService`). Satoshi is power-regulated to ~10% of blocks toward `11,000 BTC` by `2011-04-26` (then retires); Hal fades to 0 by `9 Aug 2009`. Scripted historical txs appear on-chain: the `12 Jan 2009` 10 BTC Satoshi→Hal send (bootstrap) and the April 2009 Mike Hearn 32.51 round-trip (`HistoricalEventScheduler`). See `ProjectDesignManual.md` Ch. 28.

**Persistence model — a block is the only commit.** Within a play session, the live clock, all balances, and the mempool advance and survive scene changes (held by the autoloads and the in-memory simulation). Disk persistence happens **only when a block is mined** — navigating between scenes, sending a BTC transaction, changing the auto-recharge dose, or any other between-block action does **not** commit. So closing the app *without* mining a block and reopening it reverts the entire world to the last mined block: the clock, every participant's balance/bankroll (back to its last-block / initial value), the auto-recharge dose and transfer history, and any pending transactions not yet in a block (discarded). Mining a block is what makes progress durable. **Before the player's very first block** (only the historical bootstrap has run), every restart resets all the way to a true first-launch state — Main Balance 40,000 SC / Bankroll 0 SC / default dose / no transfer records / clock exactly at the bootstrap's last mined block — rather than baking in an artificial "baseline". See `Documentation/ProjectDesignManual.md` §24.8 (post-first-block) and §24.9 (pre-genesis).

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

### Address & UTXO model — ✅ IMPLEMENTED (Step 8)

- **Implemented**: a **real multi-input/multi-output UTXO model** (Bitcoin's actual transaction model). A `Transaction` holds `Inputs[]` (each referencing a prior output) and `Outputs[]`; balance = Σ of an address's unspent outputs; fee = Σinputs − Σoutputs. The UTXO set is rebuilt by replaying the chain (never persisted). One unified spend path coin-selects owned UTXOs (exact match, else largest-first **multi-input** combine) and returns **change** to a fresh address.
- **Address non-reuse** (a fresh derived address per receive) is **Satoshi-only** (his ~220-address "one coinbase per address" spread — the audited fractal analog of the historical ~20,000). The player, casino, Hal, and Mike Hearn keep one coinbase/receive address and become multi-address only via **change outputs on send**; the bots stay single-address (no stored seed — deferred).
- **Terminology (D0)**: this address mechanic is **address non-reuse**, *not* the "Patoshi pattern" (which is a mining-forensic fingerprint — ExtraNonce/nonce/timestamp artifacts — reserved for an optional, unbuilt forensic view). The deferred Step-7 **E8** (17.49 Hearn change) is now a real change output. See `Documentation/ProjectDesignManual.md` Ch. 30 + `AIHelperFiles/step8-utxo-realism-plan.md`.

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

**P6 note (Step 12):** casino repayment can adopt the player's **Auto-Withdraw threshold/surplus mechanism** verbatim — `TryAutoWithdraw()` runs against a floor + installment; the casino would run the identical shape against its *debt* instead of an account (one mechanism, two semantics: equity vs. repayment). The one blocker is an insolvency policy ("the game never blocks a bet on casino insolvency" would break if the casino's auto-loan could be toggled off) — design that alongside P6. See `ProjectDesignManual.md` Ch. 32 §32.2.

The DEV casino-SC scenes (`CasinoGamblingFinances` / `ClientsBetsHistory` / `ClientsTransactions`, Step 11) already track the casino's SC balance sheet, loans, and per-client ledger. A development-only `CasinoFinances` scene (BTC side) is also accessible while building and testing. These can be hidden from normal players later.

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
