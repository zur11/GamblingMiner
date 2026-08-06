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

### The founders mine alongside you

You start on **21 March 2009**, on a chain Satoshi and Hal already mined from the genesis block. They keep mining in your era too — but only while *you* advance time by betting (they never run the clock on their own). So you will see some of "your" blocks won by **Satoshi** (he stays the dominant early miner, taking roughly 1 in 10 blocks until he reaches his historical hoard and disappears around 2011) and by **Hal** (a steady early miner who fades out by August 2009). You will also spot famous historical transactions appear on-chain: the **12 Jan 2009 10 BTC Satoshi→Hal** send, and **Mike Hearn's April 2009 exchange** with Satoshi. This is intended — you are mining *inside* early Bitcoin history, not in an empty world.

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

## Company Shares & Board Votes

As you play, historical companies get founded from the referral auctions, and you can end up holding shares in them. Some shares (NST) carry **voting rights**; others (PST) only pay dividends.

**You decide how much attention each company gets.** Every company you hold NST in has a **Vote Policy** panel (in its Company Details page) with three options. All of them are per-company, so you can steer the two businesses you care about and leave the rest to run themselves.

| Setting | What happens at a vote |
|---|---|
| *(default)* — neither box ticked | A **standing policy** is cast for you automatically. The game never stops. Untouched, it votes the company's current values, i.e. "no change" — set the dials and press **Save Policy** to make it vote something else. |
| **Pause the game for this company's votes** | Everything freezes until you cast a ballot by hand. |
| **Abstain from every vote at this company** | No ballot is cast at all, and the game never stops. |

**When you do pause,** the clock stops and **the betting buttons in Dice (both Manual and AUTO) are disabled**. Open the Blockchain Explorer's Enroll Mode: the company needing your vote shows up in **red ("⚠ BOARD VOTE PENDING")**. Click "Vote →" and submit. If you open the wrong company's page, a red line at the top tells you which one is actually holding the game.

**Voting vs. abstaining is a real choice, not a formality.** At the ballot you can tick **Abstain** instead of dialling a number, then press Submit Ballot either way. The difference: a ballot puts your shares into the weighted average, while abstaining takes them out entirely and lets the other shareholders decide between themselves. The panel shows you both outcomes side by side before you commit, plus the range your holding could move the result across — so you can see whether your say is worth spending here.

Careful with the difference between **Abstain** and **Follow Status Quo**: abstaining casts *nothing*, while Follow Status Quo still casts a ballot — one that votes for no change. If the other holders want change and you hold a big stake, "no change" is an active vote against them.

**Important — save your vote by mining a block.** The game only writes progress to disk when a block is mined (the same reason a restart rewinds your clock, balances, and pending transactions to the last mined block). Your ballot works the same way: casting it unpauses the game **immediately**, but it isn't saved until at least one new block is mined afterward. So:

- If you vote and then close the app **before** a new block is mined, your vote is lost — on reopening you'll be asked to vote again.
- Because a pending vote also stops mining, **reopening the app with a vote still pending drops you straight into it**: you'll be asked to vote before you can bet, with no chance to "play up to it" first.
- The same applies to the Vote Policy panel itself: ticking a box or turning a dial changes nothing until you press **Save Policy**, and even then it is only written to disk at the next mined block.

To keep a vote, cast it and then let the game run until **at least one block is mined** (place a bet or two, or leave autobet on for a moment) before you stop. This is expected behavior, not a bug.

## Bidding in a Referral Auction

You bid on a company by sending BTC to it from your BTC wallet. Two things about that are easy to get wrong, and both cost real BTC — so the send panel warns you about them in amber. **The warnings never block the send**; they are there so the choice is yours.

**A bid you just sent is not counted until a block is mined.** Like everything else in the game, a transaction only becomes real when it lands in a block. Until then it sits pending and appears **nowhere** — not in the auction's bid list, not in the company's details. This looks exactly like a failed send, and it isn't.

**Do not send twice while the first one is still pending.** If both land in the same block, **only your highest bid participates in the auction** — the other reaches the company as an ordinary transfer that earns no slot, no shares, and **is not refunded**. The wallet tells you when you already have an unconfirmed bid to that company and how much it was for. If you want to raise, wait for a block, then send the higher amount.

Two related warnings you may also see:

- **You already hold the leading bid.** Sending more won't count — bidding against yourself is ignored, and your existing leading bid (and its countdown) is kept. The BTC still leaves your wallet.
- **The auction closes very soon.** If no block is mined before it closes, your bid may not be counted in time.

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

