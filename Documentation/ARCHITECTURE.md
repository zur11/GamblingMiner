# Architecture — Core Game Systems & Data Models

> Extracted from `CLAUDE.md` on 2026-08-21 (Dep-01 D2.2), which had carried it as a section since the
> file began. It is a **system specification**: what each subsystem is, where it lives, and the design
> record behind it. Per the Document Policy, specifications live here and `CLAUDE.md` keeps only the
> index plus the rules that govern how new code is written.
>
> **Three permanent rules were deliberately LEFT BEHIND in `CLAUDE.md`** rather than moved with their
> surrounding prose — they tell a developer what not to write, which is the one thing that must stay in
> the file that is always loaded. They are repeated here in context, marked ⚠, so this document is
> readable on its own.

---

## 1. Core Game Systems

### Dice Engine
**Location**: `Scripts/Dice/DiceEngine.cs`

- 00–99 roll system with configurable chance and multiplier
- **RTP**: 99.02% (house-favorable)
- Multiplier formula: `(100 * RTP) / chance%`
- Profit: `win ? (bet * multiplier - bet) : -bet`

### Betting Strategy System
**Locations**: `Scripts/Betting/`

- `IBettingStrategy` — strategy interface
- `ProgressiveBettingStrategy` — the outcome picks its own percent (`IncreaseOnWinPercent` on a win, `IncreaseOnLossPercent` on a loss) and multiplies the bet by `1 + (percent / 100)`; a percent of `0` resets to base bet
- `BettingStrategyConfig` — data model with all parameters:
  - `BaseBet`
  - `IncreaseOnLossPercent` / `IncreaseOnWinPercent` — **one progression percent per outcome, independent, each armed by its own value** (mini-plan 01, 2026-08-06; replaces the single `IncreasePercent` + the `IncreaseOnLoss`/`IncreaseOnWin` which-outcome pair). `0` or blank means *that outcome resets the bet to base*, so a strategy can grow on losses only, on wins only, on **both**, or on neither (flat betting) — the last two were not expressible before. A "trigger outcome" (the thing `ProgressionTriggerStreak` / `ProgressionAnchorBalance` count) is now simply an outcome whose own percent is `> 0`.
  - `StopOnProfit`, `StopOnLoss` (optional thresholds) — **fully independent since mini-plan 01 (2026-08-06)**. Each is **armed by its own amount alone**: blank, unparseable or `<= 0` all mean *disabled*, normalized at the single parse boundary `StrategyControlPanel.ParsePositiveDecimal` so `HasValue` stays the armed test everywhere downstream. (Before that split, a typed `0` **armed** the profit stop, which then fired on the first bet — the metric is `>= 0`.)
  - `StopOnBlockMined` — halts session when a block is mined
  - **Every stop measures from session start** — `ProfitSessionStartingBalance` / `LossSessionStartingBalance`, one baseline per stop. **There is no second baseline mode**: the per-run "Anchor" alternative was deleted at mini-plan 01 round 3 (§25.12) because it is indistinguishable from Session mode whenever both progression percents are set — every outcome is then a trigger, so `ProgressionAnchorBalance` never moves. That value still exists and is still maintained; it just isn't a stop baseline (the Martingale calculator projects from it). The two baselines are **separate fields on purpose**: a reset re-anchors **only the side that fired**, so one stop's reset can never redefine what the other is measuring. See Chapter 25.3.
  - `InsistAfterStopOnProfit` / `InsistAfterStopOnLoss` — **one switch per stop**: on a hit, **reset the progression to base bet and keep going** instead of stopping. Each UI toggle is gated on **its own** stop amount. **An insisting stop is a SEGMENT boundary** (§25.13): it re-anchors its own baseline always and the *other* side's **only if that side also insists** — two insisting stops share one segment, a non-insisting one keeps its whole-session anchor. This is what makes each stop meaningful independently of the progression percents: a stop's reset-to-base is a **no-op by construction** when its own outcome doesn't drive the progression (the profit stop can only fire on a winning bet, the loss stop only on a losing one), so the segment boundary — not the reset — is what actually bites. `StopOnBlockMined` is never insisted (a mined block always stops if that toggle is on). Insist-on-profit is not just convenience: with Anchor mode gone, the two Insist resets are the *only* things that bring a grown bet back to base, so a win-side (or two-sided) progression without it has no upper reset and only returns to base after the loss stop fires — i.e. after giving the profit back.
  - **A bot's two Insist switches are forced ON** (mini-plan 01 round 3, reversing D-M1.4): `SimulationService` restarts a bot session only on `InsufficientBalance`, so a stop that *stops* is terminal for that bot — but an insisting stop is not, which is what makes a bot profit stop both safe and necessary (it is the only cap on the bot's bet growth). Forced in **`DiceGame.BuildBotStrategyConfig`**, not merely mirrored from the panel, so a per-node snapshot captured before the change cannot re-create a terminal stop; the panel locks both toggles ON in bot strategy mode and leaves both amount fields editable.
- `SavedBettingStrategy` / `SavedBettingStrategyRepository` — persistence of named strategies

**Progression resets vs. auto-recharge (bankroll management).** Implemented in `BaseBetSession.ApplyStopConditions` + `SimulationService`; shared by player **and** bot sessions. Order of preference — *reset cheaply, recharge only as a last resort*:
1. **`StopOnLoss` + `InsistAfterStopOnLoss`** (primary): threshold set **below** the bankroll caps a losing run's depth, resetting to base with **no** recharge. `StopOnProfit` + `InsistAfterStopOnProfit` is its mirror on the way up — the upper reset that banks a run and restarts from base.
2. **Bankroll-limit reset** (safety net): if the grown bet exceeds the bankroll but the **base** bet still fits and `InsistAfterStopOnLoss` is on → reset to base, **no** recharge. This branch reads the loss toggle because it *is* a loss-side condition — the grown bet no longer fits.
3. **Auto-recharge** (last resort): only when even the **base** bet can't be afforded does the session stop with `InsufficientBalance`; then — *after* the stop — `SimulationService.TryPlayerAutoRechargeAndRestart` / `TryRechargeAndRestartBot` moves funds (Main Balance→Bankroll for the player, `NodeFinancialState.PrincipalBalance` for bots) and **restarts the progression from base**. The recharge is decided *after* the stop because `ApplyStopConditions` self-stops on `InsufficientBalance` *inside* `ExecuteNext`. `InsistAfterStopOnLoss` stays active across recharges. See `Documentation/ProjectDesignManual.md` Chapter 25 (and 24.5).

### Bet Sessions
**Locations**: `Scripts/Sessions/`

- `BaseBetSession` — abstract; handles run state, remaining bets, current bet, progression streaks, stop conditions; calls `BetService.ExecuteBet()`
- `AutoBetSession` — extends `BaseBetSession`; adds session ID tracking
- `ManualBetSession` — single-bet handler

**A running session's parameters come from the SESSION, never from the panel** (mini-plan 02, D-M2.8, 2026-08-07). A session captures its `BettingStrategyConfig` at `Start()` and nothing re-pushes it, so `StrategyControlPanel` is an **editor for the next run**, not a live control surface for the current one. Read a live run's settings through **`BaseBetSession.SessionConfig`** (safe to expose — the config is init-only); every `_strategyPanel.*` read inside a running-session code path is a bug candidate. Two corollaries: (a) **`DiceGame.ApplyRunLock` / `StrategyControlPanel.SetRunLocked` disable every captured-value control while a player session runs** (D-M2.14) — an enabled-but-inert control is a lie; the exceptions are what the session genuinely re-reads (hardware/APS via `HardwareRate`) plus the run controls themselves; (b) **where a stored per-node snapshot and a live session disagree, the executing config wins** (D-M2.2) — re-entering DiceGame refills the panel from `SimulationService.CurrentConfig.Strategy`. `DiceGame._nodeStrategies` is **`static`** so it survives the scene being freed on navigation (D-M2.1 — as an instance field it emptied on every round-trip and silently produced flat betting with both stops disarmed). Full write-up: `Documentation/ProjectDesignManual.md` **§24.13**.

### Bet Execution Pipeline

```
User/Session calls ExecuteNext()
  → BetService.ExecuteBet()
      → Wallet.ApplyTransaction(withdrawal)
      → DiceEngine.Play()
      → If win: Wallet.ApplyTransaction(payout)
      → Accumulate fractional profit remainder
      → Emit BetTransactionEvent
  → ProgressiveBettingStrategy.CalculateNextBet()
  → BaseBetSession.ApplyStopConditions()
  → UserStatsService.OnBetExecutedRegisterBet()
  → BankrollProgramService.TryTransferBalanceToBankroll() (auto-recharge if configured)
```

### Blockchain / Mining System
**Locations**: `Scripts/BlockchainPort/`

- **Difficulty regulator Round 2 (2026-07-27, `AIHelperFiles/btc-pools-hardware-plan.md` §R2)** — diagnosed from a playtest report of ~2-day blocks. **Not** the casino pool (`playerBotsPower` was a flat 10 throughout): Satoshi's end-game catch-up ramp hit `MaxShare = 0.99`, and since `shareToWeight` is `w = s/(1−s)` that authorized **99× the rest of the network** — power 7,037 vs 72, difficulty anchored at 4.16M, blocks 4–7× target. Underneath it sat a structural fault: **the bet engine can retain at most `MaxBacklogSeconds` (2.0) of simulated time per frame** — the `Math.Min` in `SimulationService.Tick` discards the rest — while `CalendarTimeService` advanced the **full** frame delta regardless, so past the saturation knee (≈45 fps at `DevTimeScale` 90) game time silently outran the mining work and in-game block intervals stretched by exactly the dropped fraction. Four fixes shipped together: **R2-A** `MaxShare` 0.99 → **0.90** (99× → 9×); **R2-D** asymmetric feedback — `MinFeedbackTrim` 0.5 → **0.25** with `DifficultyEaseAlphaDown = 0.9` (cede an overhang fast, take on difficulty slowly: too-high difficulty is slow blocks, too-low **mints coins early**), unwinding the measured overhang in ≤2 blocks instead of 4–5; **R2-C1** `CalendarTimeService.SimulationThrottle` — the clock now advances by the sim-time the engine actually **retained** (`offered − dropped`, power-weighted over player + running bots; the accumulator's *carried* remainder is not a loss), so it is **exactly 1.0 and byte-for-byte inert below the knee** and above it the game slows in **wall-clock** rather than corrupting in-game pacing; **R2-T/R2-ASSERT** `simSecOffered,simSecConsumed` columns in `difficulty_trace.csv` plus a `GD.PrintErr` when `configuredPower > 2× realizedPower` for 3 consecutive blocks (3-block gate because single-block solvetimes are ≈exponential). **Canon decision D-R2.1: Satoshi's retirement DATE is canon, the 11,000 BTC is a TARGET** — under a bounded ceiling he may now retire SHORT, which is what makes any ceiling legal at all and removes the feedback loop (slow blocks → fewer blocks by the deadline → more power demanded). The June 2026 "regulator is correctly calibrated, close this section" verdict still holds **for what it tested** (executable powers 1–10); Round 2 is the envelope outside it. `MaxStepDown` (0.5) is now unread, kept as documentation.
- `BlockchainService` — **continuous, regulated difficulty** (Step 6, D.1–D.4): `Difficulty` = expected nonce attempts per block; a 64-hex hash meets target when, read as a 256-bit `BigInteger`, `H ≤ 2²⁵⁶ / Difficulty`. `InitialDifficulty = 4096/7 ≈ 585.14` (the exact probability of the old `"00"`+next-hex-≤'6' rule, so pace is unchanged). Persisted per block (`Block.Difficulty`); `ChainIsValid` validates each block against its own stored difficulty (no genesis replay). `GetNextBlockDifficulty(networkPower)` is the **HYBRID retarget**: `target = anchor × feedbackTrim`, eased `next = current + DifficultyEaseAlpha·(target − current)`. **anchor** = `InitialDifficulty × power` (feed-forward from total active power = Σ miners' bets/sec, pushed by `SimulationService.SetActiveMiningPower`); **feedbackTrim** = LWMA over the last `LwmaWindow=20` block solvetimes vs `TargetBlockSeconds=58,500`, clamped `[0.5×,2×]`; `DifficultyEaseAlpha=0.7`. Power `0` (bootstrap/idle) → feedback-only. See `AIHelperFiles/btc-pools-hardware-plan.md` + ProjectDesignManual Ch.26.
- `NodeAgent` — generates ECDSA wallet keypair; `TryMineSingleNonceAttempt()` = one attempt per call (enforces `1 bet = 1 attempt` rule); caches candidate block to avoid recomputing on each attempt
- `CryptoUtils` — ECDSA signing/verification, SHA256 hashing, address derivation
- **Genesis block**: nonce=100, hash=`"0"`, previous=`"0"`, timestamp `2009-01-03 18:15:05 Unix ms`
- **Coinbase reward**: starts at 50 BTC, halves every **2,100 blocks** (≈ 4 in-game years at 100X); total supply **210,000 BTC** (converges to in-game year ~2141)
- **Block cap**: 24 transactions per block (`BlockTemplateBuilder.MaxBlockTransactions`, counting the coinbase — implemented)
- **Founder economics** (Step 7): Satoshi & Hal are **regulated concurrent miners** (`FoundersMiningService`, driven by `SimulationService`) — they mine their own candidates in lockstep with the player's bets (no autonomous clock). Satoshi targets ~10% share toward **11,000 BTC by 2011-04-26**; Hal fades to 0 by **9 Aug 2009**. Scripted historical txs: the **12 Jan 2009 10 BTC Satoshi→Hal** tx (`HistoricalBootstrapService`, in the bootstrap) and the **April 2009 Mike Hearn 32.51 round-trip** (`HistoricalEventScheduler`, player era, → Hearn +82.51, never mines). See `AIHelperFiles/step7-historical-character-economics-plan.md`.
- **Balance model**: a **real multi-input/multi-output UTXO model** (Step 8 / Appendix A — implemented & in-engine audited). A `Transaction` holds `Inputs[]` (each an `OutPoint` + per-input signature) and `Outputs[]`; balance = Σ of an address's unspent outputs; fee = Σin − Σout. The **UTXO set** is rebuilt by replaying the chain (cached by `_chainVersion`, never persisted — consistent with "a block is the only commit"). One spend path `NetworkRoot.BuildAndBroadcastUtxoSpend` coin-selects owned UTXOs (exact match else largest-first **multi-input** combine) + change to a fresh derived address. **Address non-reuse** (a fresh derived address per receive/coinbase) is **Satoshi-only** (his ~220-address "one coinbase per address" spread). The **player, casino, Hal, and Mike Hearn** become multi-address only via **change outputs on send** (`ReceiveWallet` + `NodeAgent.RotateCoinbaseAddress = false` → coinbase/receives stay on base, change rotates); **only the bots stay single-address** (no stored seed — OQ-8.2). Hearn's one outgoing tx (E6b → Satoshi 32.51) is an exact-match send (no change), so his rotation is inert today — kept for consistency. E8 (17.49 Hearn change) is now a real change output. Legacy `Sender`/`Recipient`/`Amount` survive as read-only `[JsonIgnore]` shims — they expose only `Inputs[0]`/`Outputs[0]`, so **never use them to scan the chain for address membership** (a change output at `Outputs[1]` would be missed — the bug that made change-held funds vanish from wallets after a restart); iterate the full `Inputs`/`Outputs` lists instead. **And never use `tx.Sender` as a PARTICIPANT identity**: a spend whose coin selection consumed a change-address UTXO carries that derived address in `Inputs[0]` — the 2026-07-14 auction donor incident, where the player's bid displayed as an anonymous address. Resolve ownership through the node's full owned-address set (`BuildAuctionBidderIdentity` pattern; an address is a key, not an identity — `Documentation/ProjectDesignManual.md` §30.9). The account→UTXO switch used a **clean reset** (`WorldFormatVersion`). See `Documentation/ProjectDesignManual.md` Ch. 30 + `AIHelperFiles/step8-utxo-realism-plan.md` (Appendix A). NOTE: "Patoshi pattern" is a **misnomer** for this address mechanic — it is **address non-reuse**; the real Patoshi pattern is a mining-forensic fingerprint (D0).

---

## 2. Data Models

### Finance

| Class | Purpose |
|---|---|
| `Wallet` | Simple balance ledger; `ApplyTransaction()`, `SetBalanceForTimeTravel()` |
| `Money` | Static utility; 8-decimal precision; `Normalize()`, `FormatSignedAdaptive()` |
| `Transaction` | Enum types: `Deposit`/`Withdrawal`; source types: `Bet`/`External`/`OtherGame` |
| `BetTransactionEvent` | Record capturing full roll metadata (bet, profit, roll, chance, multiplier, direction, timestamp) |
| `BetRecord` | Persistent history entry (game ID, outcome, amounts, roll details) |

### Blockchain

| Class | Purpose |
|---|---|
| `Block` | Index, Timestamp (Unix ms), Transactions[], Nonce, Hash, PreviousBlockHash, MinedByNodeId |
| `BlockTransaction` | Sender/Recipient (BTC addresses), amount, fee, signature (Base64 ECDSA), IsSpendable |
| `NodeAgent` | Mining node with ECDSA keypair; mines nonces, creates signed transactions |

### History

| Class | Purpose |
|---|---|
| `BetHistoryRepository` | Loads/saves JSON chunked by month; rollback to UTC timestamp; time-bucket summaries |
| `UserBettingStats` | Aggregates wins/losses, total wagered, net profit; per-game stats |
| `TimeBasedBetStats` | Pre-calculated summaries for fast performance queries |

