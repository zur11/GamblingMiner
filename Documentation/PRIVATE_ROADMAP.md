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

> **Re-read 2026-10-12 (mini-plan 24):**
> - **Resolved:** the bullets below on the bot wallet/transaction model (Steps 8 and 16), the casino finances scene
>   (Step 11) and persistence durability (P15.11, mini-plans 12–17).
> - **Still live:** documentation drift (T6 pass 2) and the remaining `Principal Balance` internal names.
>
> They are kept as the risk read of their time.

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
10. Game over happens only when `Private Bank Account + Main Balance + Bankroll` reaches zero (Step 12).

### Basic Promise

"Bet to move time, mine with every attempt, protect your bankroll, and survive the early Bitcoin era."

## 4. Canonical Decisions

- Project language: English for all files, UI, code-facing names, and documentation.
- Public positioning for now: experimental prototype with a serious game design direction.
- General initial balance: `40,000 SC`.
- Specific initial split: `39,900 SC Main Balance + 100 SC Bankroll`.
- Preferred player-facing term: `Main Balance`.
- Game over: total SC depletion across the Private Bank Account, Main Balance and Bankroll (Step 12).
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

> **This file holds only what is still open (depurated by mini-plan 24, 2026-10-12).** Every finished entry — the
> P-items that shipped, P15.11, every mini-plan 08–23 entry, T1–T3, the done parts of T4, and the superseded
> 2026-08-21 working order — moved **verbatim, under its original heading,** to
> **`Documentation/ROADMAP_ARCHIVE.md`**. A reference elsewhere to a roadmap entry that is not here is in the archive.
> What shipped, and when, is `IMPLEMENTATION_STATUS.md`. `AIHelperFiles/IMPLEMENTATION_ROADMAP.md` is history (last
> updated 2026-06-30, ends at Step 10); **the order below is the authoritative one.**

### ▶ NEXT — the agreed working order (2026-10-12)

Decided with the developer at mini-plan 24. Each item is its own plan, under the usual lifecycle (CLAUDE.md, Git
Workflow).

| # | Work | Where it is specified | Why here |
|---|---|---|---|
| **1** | **Step 17.A + 17.C** — a direct `DiceGame → BetsHistoryExplorer` button, and the event-driven audit (migrate the `_Process` polling backlog) | `AIHelperFiles/step17-ui-access-and-event-driven-audit-plan.md` (SPECIFIED 2026-08-20, not started) | 17.C is a **declared Basic Mode v0.1 gate** (§6). 17.A does not exist yet (verified 2026-10-12: only `CalendarsNavigator` and `ScFinances` reach the explorer) |
| **2** | **User settings persistence** | §6, its first open item and its table | "Required for Basic Mode", and mini-plans 20 and 23 added view preferences with no home |
| **3** | **Player Holdings Hub** | below in this section | The highest engagement-per-line change available, promoted by Step 16's playtest |
| **4** | **Betting Statistics (17.B)**, then the **Basic Mode economy** — P2's warning, P5, P6 repayment, P7, P8 and the **Casino Rank System** — each with a design round first (the Rank's as a **design plan of its own** before its implementation plan) | Step 17 plan §17.B · the P-items below | Step 17 suspends 17.B until A and C are built **and tested** (developer's gate) |
| — | **Small item, fold into whichever UI plan comes first:** DiceGame's "Numbers" bet view | §6 | Its cost is solved: mini-plan 23 built the 10-number row and the latest-only view |

**⚠ Re-verify before relying on Step 17's text — it was written 2026-08-20 and twenty mini-plans have run since:**
- **17.C's suspension has likely EXPIRED.** Four targets (`AuctioningCompanyDetails`, `CompanyDetails`,
  `CompaniesWallets`, `CasinoCoinSwaps`) were suspended until the world reaches Market Birth (2010-07-18). The
  developer's world was past 2012 in October 2026. Confirm on the current world, then treat them as active.
- **Ch. 38's poll catalogue is stale** (audited 2026-07-21). Re-derive it from the code first: 25 files under
  `Screens/` and `UI/` override `_Process` (count of 2026-10-12).
- **Scenes changed since the plan:** mini-plans 20–23 touched `BetsHistoryExplorer`, `DiceGame`, `CalendarsNavigator`
  and `MainMenu`. In particular, `CalendarsNavigator`'s refusal to auto-update is deliberate (its comment, and
  `mini06-…-plan.md` §9.7e). It is not a poll to migrate.

**Not scheduled (open, see their entries):** T4.2 (Step 17.D) · P9 unit tests · the bot treasury policy (waits on P5)
· the machine-adaptive budget (needs a second machine) · T6 pass 2 · the Network Fee Option-B experiment.

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

> **Status 2026-10-12 — partly built.** The swap desk shipped in Step 13 (`CasinoCoinSwapService`, `CasinoCoinSwaps`),
> and the unlock is decided: Market Birth, 2010-07-18, data-driven (CLAUDE.md, Canonical Decisions). **Still open:**
> the minimum-wager requirements, their fee penalties, and the level/volume fee discounts (§7's questions).

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

### Casino Rank System (Basic Mode — a DESIGN plan first, then an implementation plan)

> **Status 2026-10-12 — Basic Mode, design open (developer's decision at mini-plan 24).** It needs **a design plan of
> its own before any implementation plan**. The text below was written when the rank drove the referral commission.
> That commission is now **Post-Basic Mode**, so the design round must first answer what the rank drives *in Basic
> Mode*. Candidates already named below: P7's conversion-fee tiers and P8's achievements. It must also settle:
> - who holds a rank (the player only, or `bot_1..4` too);
> - the tiers and the advancement criteria;
> - where the rank is shown.

Goal: a progression ladder for casino participants (player, and notably **Miner Referrals**) that gates and scales rewards.

- Defines ranks from an entry level up to a top level (exact tiers + advancement criteria TBD — likely wager volume / time survived / BTC or SC milestones).
- **Drives the Winning Referral Commission scale:** a referral's rank sets its commission rate, from **1% at the base rank up to 5% at the top rank** (the commission is paid by the casino).
- Connects to other systems over time (achievements P8, conversion-fee tiers P7, etc.).

Done when a participant's rank is tracked and visibly affects at least the referral commission rate.

### P9 - Unit Testing Infrastructure

Goal: establish a test foundation so core logic can be verified without running the full game.

- Identify a C# test framework compatible with Godot 4 projects (e.g., GdUnit4 for in-engine tests, or xUnit/NUnit for pure logic outside Godot).
- Start with pure C# classes that have no Godot dependency: `DiceEngine`, `ProgressiveBettingStrategy`, `Money`, `BetHistoryRepository`.
- Define a minimal test conventions doc (where tests live, how to run them).
- Do not attempt to test autoload services or scene logic in the first pass.

Done when at least the core betting and money logic has automated coverage and a new developer can run tests from the command line.

### Network Fee Market — Option B, the congestion experiment (not scheduled)

**Option A (the Historical Fee Replay) is live since Step 14 ND.7 (2026-07-13).** It is the canonical mechanism
(CLAUDE.md, Canonical Decisions, fee row). The research history and the A-vs-B decision record are in the archive.
**What stays open is only Option B (OQ-ND7.1):** a fee market reacting to our own mempool congestion at the 24-tx cap.
It has two roles. It gives participants a reason to pay *above* the daily base (queue-jumping), and it is a
**cross-validation** of the population/volume simulation: if B's emergent curve lands near A's replayed truth, the
rest was built right. Design: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10.6.

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

### Player Holdings Hub — one screen to claim, vote and find your companies (design + implement, after Step 16)

**Status: recorded 2026-07-30, not yet scheduled.** Deliberately **not** in Step 16 (developer's call) — that step makes the votes worth casting and the dividends cheap to pay; this one makes them *findable*.

**The problem, in the developer's words:** the only way to reach a company the player holds stock in is to hunt for it among all 40 in `BlockExplorer`'s Enroll Mode. Everything the player owns is scattered across a list dominated by companies they have nothing to do with.

**What it is:** a single player-facing screen listing **only the companies where the player holds NST or PST** — claim dividends, cast/see votes, and open `CompanyDetails` from one place. The signals it should carry already exist and are already computed: the **stake border colours** (gold NST / silver PST / black none, §22.15), the **pending-work tint** (red vote · green claim · mocha both · black nothing, §22.16 — `HasPlayerClaimableDividends` is already the shared "payable, not non-zero" source), the FBI investigation warning (`GetFbiInvestigationWarning`), and the bank lending summary. **Reuse those sources, never re-derive them** (§39.16 rule 6): this screen is an *index*, and an index that disagrees with the page it points at is worse than no index.

**The larger idea it belongs to** is the audit's **D2, "decision inbox"** (`step15-…-plan.md` §10.5 handoff): the game generates far more events than it shows — votes opening, dividends becoming payable, auctions closing, FBI heat on a holding — and they live in five different scenes. A holdings hub with a **badge count in the StatusBar** is the smallest version of that idea and probably the highest engagement-per-line change available. Once Step 16's per-company pause toggle exists (D-16.11), this screen is also the natural place to manage those toggles in bulk.

**Promoted in priority by Step 16's own playtest (2026-08-05).** Two of the three participation settings are now per-company (`PlayerPauseOnVotes`, `PlayerAutoAbstain`, plus three policy dials), and with the player holding NST in ten companies the panel-by-panel management is already the slowest part of the loop — this hub is where a bulk view belongs. The step also produced the exact failure it exists to prevent: with the game frozen by a vote at BTC Guild the developer went to ArtForz Cluster, found a normal page, and had no route onward. P16.8e's pause locator patches that one case; **a holdings hub answers the general question ("where is anything waiting for me?") that the locator only answers for the pause.** Reuse `GetCompaniesAwaitingPlayerVote` as the badge source.

### Why 60,000 was unreachable, and what was declined (measured 2026-10-07)

**Status: the measurement behind mini-plan 19, kept because the next reader will otherwise re-litigate it.** Mini-plan 18 C extracted every *case narrative* it could place in an existing
section and landed at **90,942 characters** against the stated **60,000** target (warning 100,000, hard limit
150,000). **What remains is rules**, in order of size: Pattern 2's canonical checkpoint/commit rule (12.9k),
Pattern 6 after compression (9.6k), **Pattern 7's fifteen standing conventions (6.4k)**, the UI-layout rules
(6.1k), the scripting-tools section (5.5k). Every one is "permanent instructions that govern future work", which
the Document Policy says belongs in the file.

**So the remaining move is a carve, with precedent.** `SERVICES.md` (48.5k), `ARCHITECTURE.md` (16.7k) and
`SCENES.md` (7.5k) were each extracted wholesale from this file and replaced by a one-line index. **Pattern 7's
fifteen conventions are the obvious next candidate** — they are already numbered, already self-contained, and
already say where each case lives. A `Documentation/STANDING_CONVENTIONS.md` with a one-line index here would cut
~6k without losing a rule.

**Why it is not done unasked:** an index is only as good as the habit of following it, and Pattern 7's whole
premise is that *"a rule nobody can find is a rule nobody applies"* — it was moved INTO this file for exactly that
reason (2026-08-20). Moving it back out reverses a deliberate decision, so it is the developer's to make.

### A reproducible populated-era world — the entry-year bootstrap, and what it costs (noted 2026-10-06)

**Status: available, unused, and not needed by mini-plan 18.** `TimelineConfig.DevEntryYear` (Step 14 EB.1) builds
the chain from the canonical 21 Mar 2009 player start onward to 21 Mar of a chosen year, using **the same
weighted-power model live play uses** — so an entry-year world is canon-*compatible*: genesis and the founders keep
their true dates and the intervening history is really built, not faked. It also seeds the non-miner companies
(`eb1_seed_*`). `DevEntryYear = 0` on `main` forever; the body is deliberate dead code under `#pragma warning
disable CS0162`, kept for exactly this use (Ch. 35 precedent).

**When it is the right tool:** when a measurement needs a **reproducible** starting era rather than whatever height
the developer's world happens to have reached. Roughly **1,100 blocks to reach 2011** and **~1,600 to reach 2012**,
at the regulated ~58,500 s target solvetime, built in one bootstrap instead of hours of play.

**What it costs, stated plainly:** the timeline stamp gains an `+ENTRY-<year>` suffix, so
`NetworkRoot.ResetWorldIfIncompatible()` **wipes the world** — in both directions, including on the way back to
`main`. Any world worth keeping must be **archived out of `user://` first**. It also must be reverted to `0` before
merging (the precedent exists: Step 14 and Step 15 each closed with exactly that commit).

**Not needed for mini-plan 18** because the developer's current world already sits at game date **2011-08 with 13
powered cast miners** — the era whose cost is in question.

### A budget that adapts to the machine (BASIC MODE refinement)

**Status: open, named in mini-plan 09 (D-09.6 option (c)) and unchanged since.** Every performance figure this
project holds — `BetBudgetPerSecond` 44,000, `DefaultMaxBetsPerFrame` 180, a player bet at 15.7 µs — was
measured on **one** PC (7.9 GB RAM, ~59 fps at vsync in DiceGame). A slower machine would be governed to a
speed its frame cannot hold, and the readout would again promise what does not run. A budget that follows
measured delivery instead of a constant is the known answer; it needs a second machine, or a deliberate
throttle to stand in for one.

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

### Post-Basic Mode — Divergent Chains / Fork Simulation (revisit AFTER Basic Mode)

**Deferred, not discarded.** The idea is wanted; the system simply has higher priorities until Basic Mode is finished. **Re-plan this only once Basic Mode is complete.**

Today every node shares one canonical chain (a block is mined → `BroadcastBlock` → every node accepts it via `TryAcceptMinedBlock`), so there are never competing chains. That made the old `RunConsensus` / `RunConsensusRound` a no-op, and it was removed in T2.

Goal (post-Basic-Mode): model a more realistic P2P network where chains can **diverge** — block propagation delay, two miners finding a block near-simultaneously, **forks**, **orphan/stale blocks**, and **reorgs** — then resolve them with a real **longest-chain (most-work) consensus** pass. This is a strong educational fit and layers naturally on top of the per-node candidate-block model (P4).

When revisited, this re-introduces a consensus step (reinstate `RunConsensusRound`-style longest-chain adoption, keyed on accumulated work/difficulty rather than raw length) and the UI to observe forks/orphans in the Block Explorer.

Start when: Basic Mode is complete and stable. Until then, leave mining committing to the single shared chain.

### Post-Basic Mode — the Casino Referral System (moved here 2026-10-12)

**Moved out of Basic Mode by the developer at mini-plan 24.** The referral *auction* shipped, but it ended up founding
**companies** (Step 14–16): NST/PST stakes, dividends, governance. Basic Mode keeps that as the auction's reward. What
the two sections below describe and **nothing in the code implements** is post-Basic-Mode:
- the 1–5% Winning Referral Commission;
- the `Referrals` scene;
- converting referrals into Miner Referral nodes.

**The Casino Rank System stays in Basic Mode** (developer's correction, same day). It is in §5 after P8, and needs a
design plan before implementation. Its section still mentions the commission, which this move makes a Post-Basic
consumer of the rank.

Their text is kept as written. Two lines in it are stale and are corrected here, not inside it:
- **"Minimum donation rule … 0.1 BTC"** dates from the retired flat fee. The fee is now the day's replayed median.
- **The auction mechanics** are described as amended through EB.2. The current rule is
  **`Documentation/REFERRAL_AUCTION.md` §1**.

### Casino Referral System (Post-Basic Mode since 2026-10-12; written as Basic Mode)

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

### Post-Basic Mode v1 — Checklist (deferred items)

Items intentionally **not** built for Basic Mode v1 — revisit only once v1 is complete and stable. Each links to its design. (Everything else carried forward — fee activation, casino referral/rank, founder long-term timelines — belongs to **Basic Mode**; see the §6 checklist.)

- [ ] **Patoshi pattern — mining-forensic view (Step 8, Phase 8.5).** An *optional, clearly-labelled cosmetic* Block-Explorer view that highlights Satoshi-mined blocks as a contiguous band (echoing Lerner's ExtraNonce-vs-height plot) with a teaching caption. Our engine can't reproduce the real ExtraNonce/decrementing-nonce/timestamp artifacts (random-nonce search, no ExtraNonce field), so it is an honest stand-in. **This is distinct from address non-reuse** (the many-addresses wallet pattern, already implemented) — the D0 terminology correction already shipped; only this forensic view is deferred. Design: `AIHelperFiles/step8-utxo-realism-plan.md` (Phase 8.5 + OQ-8.5).
- [x] ~~**Bots multi-address (Step 8, OQ-8.2).**~~ **PROMOTED out of this list (2026-07-28)** — the deferral reason (an unknown, possibly large miner-bot population) is gone now that the casino-miner set is fixed at four. Scheduled for the end of Step 15; see **"Bot Seed Phrases & Full UTXO Integration (OQ-8.2)"** in §5 above for the why-now, the scope tiers and the design constraints.
- [ ] **Player/casino deposit-address rotation (Step 8, OQ-8.3).** Rotate the *incoming* receive address after each external deposit (full HD behavior). v1 delivers UTXO realism via change-on-send only. Design: `step8-utxo-realism-plan.md` OQ-8.3.
- [ ] **Casino Referral System** (moved here 2026-10-12): the commission, the `Referrals` scene and Miner Referral
  conversion. See the section above. (The Casino Rank System stays in Basic Mode, §5.)
- [ ] **Network Fee Market Option B**: the congestion experiment, listed in §5 as not scheduled.
- [ ] **Divergent Chains / Fork Simulation** — see the "Post-Basic Mode — Divergent Chains / Fork Simulation" section above (`IMPLEMENTATION_ROADMAP.md` Step 10).

---

## 6. Basic Mode v0.1 Checklist

> The **open** items of the Basic Mode tick-list. The ticked ones (balances, halving, checkpoints, wallets, the casino
> finances, Steps 7–8, P10, the hardware pools, the mempool and template builder, the README/Player Guide pass) moved
> to `ROADMAP_ARCHIVE.md` with the rest of the original list. Post-Basic-Mode items live under §5. Open design
> questions are in §7. Game over is `Private Bank Account + Main Balance + Bankroll = 0` (CLAUDE.md, Canonical
> Decisions).

- [ ] **User settings persistence — a real design, which does not exist yet.** Player-owned settings are
  scattered across ad-hoc files with no shared rule for what survives a world wipe. The visible symptom
  (2026-09-15, v6 → v7 reset): `saved_betting_strategies.json` survives every wipe because it sits in the
  exempt set (`NetworkRoot.ResetWorldIfIncompatible`, CLAUDE.md Pattern 2), and the developer expected it to
  be deleted. **Do not fix that file alone.** Decide once which settings belong to the player (survive a
  wipe) and which to the world (wiped with it), then give them one persistence home. Required for Basic Mode.

  **What is waiting for it** — the list is kept here, because each of these is currently a *decision deferred*,
  and a deferral nobody wrote down becomes an omission. Add a line whenever a feature ships a setting with no
  home; that is the cost signal telling us when this is due.

  | setting | today | belongs to |
  |---|---|---|
  | Saved betting strategies | `saved_betting_strategies.json`, exempt from the wipe — survives, which surprised the developer | the player, probably — but the surprise says the rule was never stated, not that the file is wrong |
  | **Bet View: Detailed / OFF** (mini-plan 10) | not persisted; resets to Detailed on every DiceGame entry | the player. A **view preference only**: after mini-plan 10 both states cost the same (~0.025 ms per bet) and both reach the clock's ceiling, so forgetting it changes nothing but what is on screen. *(Until 2026-09-21 this row called it a speed choice with a higher hidden budget. That was the design before the list got cheap, and the two-budget plan was never built.)* |
  | DEV time scale (the requested one) | not persisted | DEV, but the same mechanism |
  | **Bets explorer replay pace, speed and Per Bet credits** (mini-plan 20 F) | not persisted; every entry opens **As Played** at 1x (Per Bet at 1 credit when toggled) | the player. A **view preference**, like Bet View |
| **Bets explorer winning-numbers view: Last 10 / Latest only** (mini-plan 23 E) | a `static` in `BetsHistoryExplorer`: survives scene changes, resets to Last 10 at every launch | the player. A **view preference** |
  | DEV player credits ladder (mini-plan 20 Part 0) | writes the real hardware state (`hardware_allocation.json`), so it persists as a shop purchase does | **world**, deliberately: it is a shortcut to the shop, not a setting of its own |
  | The strategy panel's last-used values | not persisted; distinct from a *saved* strategy | the player |
  | Anything a future options menu holds | does not exist | the player |

- [ ] **The event-driven audit — Step 17.C, item 1 of ▶ NEXT.** Before Basic Mode v0.1 is complete, every
  `_Process` override is audited against CLAUDE.md Important Patterns §6 / `ProjectDesignManual.md` Ch. 38, and the
  poll-based refreshes are migrated where feasible. Re-derive the catalogue from the code first (Ch. 38 §38.5 was
  audited 2026-07-21).
- [ ] **DiceGame's "Numbers" bet view — a small item, moved into Basic Mode (developer, 2026-10-12).** It shipped in
  mini-plan 10 and was withdrawn because the 100-cell grid halved the frame rate (D-10.3). **Mini-plan 23 solved the
  cost** by showing less:
  - `PreviousWinnerNumbersRow` (the last 10, 0.4–0.6 ms a frame at 99 bets/s);
  - `LatestWinnerNumber` (one number, ~0.02 ms);
  - both are live in `BetsHistoryExplorer` behind a toggle.

  DiceGame can use the same components. **Before showing anything there, fix the old grid's scene geometry**: it has
  never been on screen, starts at y 534 under the Bet View button, and its 540 minimum height runs past the bottom
  band (Ch. 29 §29.11). Fold this into whichever UI plan comes first.
- [~] **Clarify auto-recharge behavior in UI and docs.** Docs done (ProjectDesignManual Ch. 25 + CLAUDE.md). The UI
  warning when auto-recharge is OFF is still pending (P2).
- [ ] **Hal's network-coupled fade:** replace the linear `1.0 → 0` stand-in once gradual miner spawning exists.
  Late Basic-Mode tuning, not blocking.
- [ ] **Founder long-term timelines.** Beyond Hal's fade: Hal 2013 sell-off / 2014, Mike Hearn 2016. Late
  Basic-Mode tuning. `step7-historical-character-economics-plan.md`.
- [ ] **Run longer Basic Mode manual/autobet tests.** Transaction circulation exists now, so this is unblocked.

## 7. Design Questions Still Open

- What exact threshold lets the casino start repaying bank debt?
- Should minimum wager requirements be weekly, monthly, or both from the start?
- How harsh should fee penalties be for missing minimum wager requirements?
- Should cashback decrease as a penalty, or should penalty design stay focused on conversion fees first?
- How much bot betting history should the player see for free?
- Should deeper bot history be a paid service, a level unlock, or a later feature?
- Should private mempool fees be available in Basic Mode or postponed?

## 8. Tech-Debt & Cleanup Tasks

T1–T3 (2026-06-24, ✅ all implemented) came out of the clock/persistence bug fixes — see `Documentation/ProjectDesignManual.md` §24.8. **T4 (2026-07-29, OPEN) is a standing technical objective**, not a scheduled task: it is the structural answer to INC-001 and to the progressive frame-rate decay observed in the same run. **T5** (2026-08-06) is deferred by decision. **T6** (2026-08-21, ✅ Dep-01 done, pass 2 open) is the newest and the odd one: its subject is not the game but **the files Claude reads**, an artefact class the project had never treated as one — and it took a 228,348-character `CLAUDE.md` going unnoticed to make that visible.

### T1–T3 — ✅ DONE (2026-06-24)

Nothing between blocks commits to disk (T1) · the dead Mine/Consensus/Refresh UI removed from BlockExplorer (T2) ·
DiceGame's mining readout reads the live difficulty (T3). Full entries: `ROADMAP_ARCHIVE.md`.

### T4 — Simulation-Scale Refactor: chain state, stats persistence & a cost budget — 🎯 OPEN technical objective (not scheduled)

**Why this exists.** Two signals from the same 2026-07-29 run: **INC-001** (`Documentation/INCIDENT_LOG.md`) — a 1.13 GB bet journal and a world that failed to load silently — and the developer's report that **fluidity at 9000X decayed progressively over the last days of the playtest ("cada vez más lag")**. P15.11 fixed the *durability* half. This entry is the *scale* half: the reason those numbers were allowed to grow at all. Design context: `ProjectDesignManual.md` **Chapter 40**.

**Not a priority.** Nothing here blocks Basic Mode, and none of it should interrupt the bank testing. It is written down now because the run that exposed it is the largest world this project has produced, and that evidence has a short half-life.

**Status at 2026-10-12 — only T4.2 is open.** The rest shipped, and their specifications and measurements
(T4.0, T4.0b, T4.1, T4.3–T4.6) are in `ROADMAP_ARCHIVE.md`:
- **T4.6**, the per-block cost budget: mini-plan 13.
- **T4.5**, the chain split out of the snapshot and appended, never rewritten: mini-plan 14.
- **T4.1**, incremental UTXO maintenance, and **T4.3**, the address → outpoint index: mini-plan 17.
  `DescribeUtxoCacheMismatch` must never be deleted.
- **T4.4**, mostly: mini-plan 12 bounded the journal in memory, and the lifetime rollup (mini-plan 03) is the counter
  source.

#### T4.2 — One canonical chain + one UTXO set, instead of 62 copies

The per-node `BlockchainService` is already acknowledged as vestigial — a comment in `NetworkRoot` says *"single-shared-chain design (every node already holds the same canonical chain via BroadcastBlock)"* (grep the phrase; this cited `~L5209` until 2026-08-23, by which point the line had moved ~900 lines — Standing Convention 15), and `RunConsensusRound` was deleted at T2 for the same reason. A single shared chain + one shared UTXO index, with `NodeAgent` reduced to identity + owned addresses, removes the 62× invalidation storm at its source and collapses `BroadcastBlock` to a no-op. **Blocked-by-design consideration:** per-node chains are the substrate the deferred **Divergent Chains / Fork Simulation** wants. The honest resolution is to make the shared chain the default and reintroduce per-node chains *deliberately* when forks ship, rather than paying for 62 unused copies for years.

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

