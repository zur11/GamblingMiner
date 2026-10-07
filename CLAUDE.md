# GamblingMiner — CLAUDE.md

## Project Overview

**GamblingMiner** is an experimental Godot 4.5.1 / C# prototype that simulates early Bitcoin history combined with a casino betting system. The core mechanic: **time only advances when bets are placed, and each bet simultaneously performs one mining nonce attempt**.

- **Engine**: Godot 4.5.1 (.NET / C#)
- **Target framework**: .NET 8.0
- **Primary platform**: Windows
- **Save format**: Local Godot `user://` data (JSON)
- **Starting condition**: the world begins at genesis, **3 Jan 2009**; the player's first bet is **21 Mar 2009**, after the historical bootstrap. Starting funds **40,000 SC**
- **Public status**: Experimental prototype with a serious game design direction

### Language Policy

All project files, source code, UI text, code-facing names, and documentation inside the repository **must be in English**. Spanish is reserved exclusively for AI chat and planning conversations outside the repository.

---

## Document Policy — what belongs in this file

**Read this before writing anything here.** This file is loaded into context on every message of every session; its size is a cost paid continuously, by everyone.

### Belongs here
Permanent instructions that govern future work: code conventions · invariant rules · canonical decisions (**the statement, not its history**) · indexes saying where detail lives.

### Does not belong here — and where it goes instead

| | Goes to |
|---|---|
| How a decision was reached | the plan or manual that recorded it |
| What is implemented, and when | `Documentation/IMPLEMENTATION_STATUS.md` |
| A system's specification | that system's own doc in `Documentation/` |
| Long code examples | the system's doc — keep the rule and a minimal example here |
| File trees, directory listings | **nowhere.** They go stale by themselves; read them from the filesystem |

### Before writing here — mandatory, in this order

1. **Search first**, in this file *and* `Documentation/`. If the subject already exists, **EDIT it. Never append a second version.**
2. **If the new contradicts the written, do not write both.** Verify which is true **against the CODE**, correct the false one, and say so to the developer.
3. **If it is unclear whether something belongs here or in a doc, ASK** before writing.
4. **A table row or bullet growing past ~500 characters is becoming documentation.** Extract it.

### Budget

| | |
|---|---|
| Target | **85,000 characters** — re-set from 60,000 on 2026-10-07, see below |
| Warning | **95,000** — on crossing it *while writing*, say so in that same reply and propose what to extract |
| Hard limit | **150,000** — where Claude Code reports the file's size at startup |

**Why the target moved, and why that is not a surrender.** 60,000 was set when this file was mostly prose. It is now mostly **rules** — earned across 17 steps and 19 mini-plans — and mini-plan 19 measured what that means: the eight largest sections hold 63,355 characters and **every one of them is instructions**. The depuration carved everything the policy itself mandates (over-long table rows, a section that already said "go read Chapter 29", an environment appendix now in `DEV_ENVIRONMENT.md`) and landed at **~84,700** — a figure that includes this paragraph and the two conventions the plan itself added, since a file being measured is also the file being edited. Going further would mean moving **Pattern 2's checkpoint contract** or the **Canonical Decisions table** out, which are the file's core, or **Pattern 7's conventions**, which were moved *into* this file in August precisely so they would be found. **All three were considered and declined — do not re-litigate them without new evidence.**

**So the target is now the measured floor of what is genuinely extractable, not an aspiration.** A target nothing can reach trains the reader to ignore it — the same failure the suspension note below describes. If the file grows past it, the growth is new material and the question is whether that material is a rule or a case; cases go to `Documentation/`.

**While a depuration plan is actively running, the warning is suspended and only the hard limit applies.** A warning exists to surface an *unnoticed* condition; during a plan whose whole subject is that condition, it is noticed, and repeating it at every step is noise that trains the reader to skip it.

### Why this exists

In August 2026 this file reached **228,348 characters** — of which a **single table cell held 32,104** and one section held **57,722 of design record labelled as status**. It was not caught by review; it was caught by accident. **The failure was not that the file was long, it is that nothing measured it.**

---

## Core Gameplay Loop

```
Place Bet → Dice Roll Resolves + 1 Nonce Attempt → Time Advances →
Block Mined? → BTC Reward + Checkpoint → Manage Bankroll / Strategies → Repeat
```

**The three-layer loop:**
1. **Casino layer** — bet, win or lose SC, manage bankroll discipline
2. **Mining layer** — every bet is one nonce attempt; bots compete for blocks
3. **Historical layer** — time progresses through real early Bitcoin history (2009+)

**Game over**: `Main Balance + Bankroll = 0`

---

## Code Conventions

### Language and Style

- **Language**: C# only. No GDScript for logic files.
- **Files**: `PascalCase.cs` (e.g., `BetHistoryRepository.cs`)
- **Classes**: `PascalCase` (e.g., `class ProgressiveBettingStrategy`)
- **Interfaces**: `IPascalCase` (e.g., `IBettingStrategy`)
- **Methods**: `PascalCase` (e.g., `ExecuteNext()`)
- **Fields / locals**: `camelCase` (e.g., `currentBet`)
- **Private fields**: `_camelCase` (e.g., `_sessionId`)
- **Constants**: `PascalCase` or `UPPER_SNAKE_CASE` — follow existing pattern in the file
- **Scene files**: `.tscn`
- **Resource files**: `.tres`
- **Indentation**: **Tabs** — Godot auto-formats with tabs; never use spaces in `.cs` files opened by the editor

### Godot / C# Integration

- All service singletons extend `Godot.Node` as `partial class`
- Override `_Ready()` for initialization, `_Process(double delta)` for per-frame logic
- Autoloads are registered in `project.godot` and resolved with `GetNodeOrNull<T>("/root/ServiceName")` — **not** by bare class name. Registration ORDER matters too; both rules are Important Patterns §5
- Signals: prefer typed C# `event Action<T>` for service-to-service communication; use Godot signals for scene-to-UI connections where needed
- Node references: `GetNode<T>("%UniqueNodeName")` or `GetNode<T>("ChildName")` — never use `%` or `$` on another object's reference

### UI Layout & Scrolling (Godot) — read before touching a scrollable panel

**Before creating OR editing ANY scene that contains a `ScrollContainer`, READ `Documentation/ProjectDesignManual.md` Chapter 29 ("UI Design & Godot Layout") first.** This is not optional boilerplate — the same layout bugs (won't-scroll, sideways-clip, footer-overflow) have recurred *specifically because that chapter was not consulted before building the scene*. Do not mirror another scene's layout blindly; the mirror may itself carry an anti-pattern (e.g. `CasinoGamblingFinances`'s footer-inside-scroll, which propagated the overflow bug into `ScFinances`).

**Every rule below is a DON'T, each earned by a bug** — a scroll bug once cost a full session. The structure, the diagnostics and every case are Chapter 29; this list exists so you know a trap is there before you fall in it.

- **A panel scrolls only if it has a BOUNDED height smaller than its content.** The bounding chain is `MarginContainer` (`anchors_preset = 15`) → `VBoxContainer` → the scroll element with `size_flags_vertical = 3`. **A container that is not itself height-bounded cannot bound its children.**
- **Pick ONE of two scroll patterns deliberately — never mix them:** a `ScrollContainer` wrapping many controls (`RichTextLabel`s need an explicit `custom_minimum_size`), **or** one `RichTextLabel` with `scroll_active = true` + `fit_content = false`. (§29.2)
- **NEVER put a `fit_content = true` `RichTextLabel` inside a `ScrollContainer` expecting it to scroll** — its reported minimum height is unreliable in containers, so the scroll never learns the content overflows. **The #1 time-sink in this project.**
- **`HSplitContainer` does not reliably bound content height inside a scroll — use `HBoxContainer`.**
- **The mouse wheel is eaten by `mouse_filter = STOP` (the default).** With the `ScrollContainer` pattern, every control from the hovered node up to it needs `PASS`.
- **Persistent nav / Back buttons go in a FIXED FOOTER, OUTSIDE the scroll** — inside it they clip at the bottom, clickable but unreadable. `ScTransactions` is the reference. Reparenting between scroll and footer is safe: controllers resolve by `%UniqueName`. (§29.10)
- **The bottom ~50 px of the 1080 canvas can be OFF-SCREEN** in an embedded window — this, not the scroll, caused the Step 12 "Back button overflows" bug. Keep must-click controls out of that band (`margin_bottom ≥ ~50`), and note an expanding child **pins the following footer to the very bottom edge**, into the danger band. (§29.11)
- **The last line sits flush against the scroll's bottom edge** — append trailing blank lines so it is not half-clipped. And **setting `RichTextLabel.Text` resets its scroll to the top**: on a refreshed panel, save `GetVScrollBar().Value` and restore it.
- **A readout may not claim precision its sampling cannot carry.** A near-constant step per sample **freezes the low digits**, and a *fixed* cadence only moves the freeze one unit up — sampling interval and displayed precision are **one** decision. Use `UI/Readouts/ReadoutSampling.cs` rather than writing a second one, and **never animate or interpolate a counter** (it would show values the world never had). **Exactly five scenes render a live clock and each holds a sampler — a sixth without one is a regression**, checked by two greps with baselines in §29.13.1. Full case: **§29.13**.
- **Diagnose with numbers, never guess.** Print `GetVScrollBar()`'s `MaxValue`/`Page`/`Value`, `Size`, `GetContentHeight()` and whether the data is even present, before restructuring. Add a visible canary to confirm the scene reloaded the edited `.tscn` — C# always rebuilds, external `.tscn` edits need a scene reload.
- **The Block Explorer's change-to-self display filter is DELETED (Step 16 P16.2f) and must not come back.** Every spending participant carries a `DerivedAddressWallet`, so there is no shape left to hide. If a new participant appears without a seed, **give it one — do not reintroduce a filter that makes a real spend's arithmetic fail to add up.** (§29.9)

### Money Handling

- All monetary values: **8 decimal places** (BTC satoshi-model precision)
- Always use `Money.Normalize()` before storing any decimal result
- Use `Money.FormatSignedAdaptive()` for display strings
- Never accumulate fractional profit without using `BetService`'s built-in remainder accumulation
- **Number locale**: canonical format is `1,000,000.00000000` — comma for thousands separator, period for decimal point. This is `CultureInfo.InvariantCulture`. **Never** use a raw C# interpolated string with a decimal format specifier (`:N8`, `:F2`, `:+0.00000000;-0.00000000`, etc.) — it will invert the separators on Spanish/European locales. Always pass `CultureInfo.InvariantCulture` explicitly: use `string.Create(CultureInfo.InvariantCulture, $"… {value:N8} …")` for compound strings, or `.ToString("N8", CultureInfo.InvariantCulture)` for single values. `Money.FormatSignedAdaptive()` already does this internally.
- **Currency labelling — every monetary amount the PLAYER sees names its currency (✅ swept 2026-08-06, same pass as the locale audit).** A bare `39900.00000000` is ambiguous the moment a BTC figure and an SC figure share a screen, which the StatusBar's BTC wallet cell made permanent. **Two shapes, pick by density:**
  - **Standalone labels, status lines, result text, confirmation messages** → suffix inline: `39,900.00000000 SC`, `12.50000000 BTC`. Used by StatusBar, DiceGame's balances + WIN/LOSS line, BankrollProgrammer's status messages and transfer log, the swap desk's availability lines, CentralBank's monetary invariant.
  - **Dense tabular columns** → declare it **once in the column header** (`Bet (SC)`, `Profit/Loss (SC)`, `P/L (SC)`, `Gambled (SC)`, `Balance (SC)`), never on every row — DiceGame's bet history, `FinancialBettingStats`, `BotPlayHistory`, all three Martingale calculators. A row-level suffix in a 50-row list is noise, and the header is the thing a player reads once.
  - **Scope: UI only.** Do NOT push unit strings into the blockchain/transaction/banking machinery — `Transaction`, `TxOutput`, ledger records, checkpoint DTOs, CSV traces and JSON stay pure numbers. The label belongs to the presentation layer that knows which asset it is rendering.
  - **Not monetary, correctly bare:** difficulty, mining power, nonce/roll values, bets-per-second, multipliers, percentages, investigation scores, block counts, and **NST/PST share counts** (labelled with their own token name, not a currency).
  - Where a `.tscn` label is absolutely positioned (`layout_mode = 0` + fixed `offset_right`), widening the text needs the box widened too — DiceGame's two bet-history headers were extended (`Bet` 948→1020, `Profit/Loss` 1181→1290) into empty space. Container-managed labels (`layout_mode = 2`) resize themselves and need no edit.
- **Number-locale detector — run it before believing the project is clean.** The rule above was in force for a year and still violated in **~40 sites**, found only because the developer noticed `0,00000000` in the Block Explorer's Network Status panel. **The full case — the four numeric bug shapes, the fifth TEXT-side shape, the fix shape, the confirmed-clean scope, the bulk-regex trap, and the baseline's history — is `Documentation/ProjectDesignManual.md` §29.12 / §29.12.1 / §29.12.2. Do not re-derive any of it here.** What stays below is the *recipe and its tripwire*, because the next violation will arrive the same way: written by hand, invisible on an English dev machine, spotted by eye on a Spanish one.
  - **Pass 1**, from Git Bash at the repo root — flags an interpolated numeric format specifier with no `InvariantCulture` within the preceding 3 lines (the window that catches multi-line `string.Create(...)` wrappers). Continuation lines of an already-wrapped expression come back as benign hits; read the surrounding lines before editing:
    ```bash
    for f in $(grep -rlE '\$"' --include=*.cs . | grep -v '/\.godot/'); do
      awk -v F="$f" '{ lines[NR]=$0 }
      END { for (i=1;i<=NR;i++) { l=lines[i]
        if (l ~ /\$"/ && l ~ /\{[^{}]*:(F[0-9]|N[0-9]|0\.[#0]|#,#|P[0-9]|\+0)/) {
          ctx = lines[i-3] lines[i-2] lines[i-1] l
          if (ctx !~ /InvariantCulture/) printf "%s:%d: %s\n", F, i, l } } }' "$f"
    done
    ```
    Second pass, for the form the first one misses — `.ToString(format)` with no culture argument: `grep -rnE '\.ToString\("(F[0-9]|N[0-9]|0\.[#0]|#,#|P[0-9])[^"]*"\)' --include=*.cs .`
  - **Passes 3 and 4 — the TEXT side, which neither of the above can see** (full case: §29.12.1). Culture-sensitive **date names** once rendered a month as `Month: mayo (31 days)`, invisible on an English dev box exactly like the numeric cases: `grep -rn "CultureInfo.CurrentCulture" --include=*.cs .` and `grep -rnE '\.ToString\("(ddd|dddd|MMM|MMMM)[^"]*"\)' --include=*.cs . | grep -v InvariantCulture`. *A locale audit scoped to numbers will pass a project whose month names are in the wrong language.*
  - **BASELINE: 17 / 0 / 0 / 0.** Every pass-1 hit is a benign **continuation** line of a `+`-chained interpolated string whose `string.Create(CultureInfo.InvariantCulture, …)` wrapper sits more than 3 lines above it, i.e. outside the detector's window. **Identified by SHAPE, never by line number** (the numbers given here until 2026-08-23 had already drifted +10 — Standing Convention 15). **An 18th hit, or any hit whose wrapper is NOT above it, is a real regression.** Where the current 17 live, and the baseline's history: §29.12.2.
  - **The count is the tripwire, so RE-VERIFY it whenever you run the detector rather than merely consulting it — and when you add a `+`-chained wrapped block whose chain runs more than three lines past its wrapper, update the count in the SAME COMMIT.** It once read "regression" in silence for a week because that did not happen. **A tripwire that cries wolf is the same as one that never fires.**
  - **Fix shape:** wrap in `string.Create(CultureInfo.InvariantCulture, $"…")`, which accepts a whole `+`-chain as ONE handler (turn a trailing bare `"literal"` operand into `$"literal"` so the chain stays all-interpolated); add `using System.Globalization;`. When adding a readout, reach for `Money.FormatSignedAdaptive()` first — it needs no wrapper. ⚠️ **Never fix these with a bulk regex**: a `perl -pi -e` pass once silently blanked 24 lines and still compiled (§29.12). **Known residual, deliberately not chased:** a *bare* hole `$"{someDecimal}"` is equally culture-sensitive and there is no way to find the decimal-typed ones without type analysis, so those are fixed reactively — a comma appearing in a panel is the tell.

### Time

- `DateTime.Utc` for storage, persistence, and internal comparisons
- `DateTime.Local` for player-facing display (game time starts `2009-01-03 18:15:06 Local`)
- Unix **milliseconds** for blockchain timestamps
- Game-time scale: **1 bet tick = 100 in-game seconds**; autobet target: **10 real minutes = 16h 40m in-game**

### JSON Persistence

- All `user://` files use JSON with **CamelCase** naming policy — **one documented exception: the bet journal**
  (`bet_history_*.jsonl`, mini-plan 12 D-12.1). Its lines are flat, short-keyed and carry money as **whole
  satoshis** (`"a":230000` is `0.00230000`) with the multiplier ×10,000, because it is the only file written at
  bet rate. It is still JSON so that `node`/`awk` audits keep working; **read `JournalLine` in
  `BetHistoryRepository` for the field map before reading a journal by hand**, or an audit will be off by 10⁸
- History files are segmented by **10,000 entries per file**, never by date, and retention caps the journal at **20 segments** (~190,000–210,000 records — it oscillates with the active segment; never quote a flat 200,000). The **lifetime rollup** beside it is unpruned and is the only record of pruned bets: `Documentation/SERVICES.md` → `UserStatsService`
- Always use `FileAccess` (Godot API) for `user://` paths

---

## Key Architecture — Autoload Services

**19 autoloads** are registered in `project.godot`, plus one pure static controller in the same layer.
They persist across every scene. **How to access one — and why their registration ORDER is
load-bearing — is Important Patterns §5**, not this chapter.

Full detail for every entry below (persistence paths, invariants, defaults, decision history) lives in
**`Documentation/SERVICES.md`**. Read the line here to know which service owns a concern; open the doc
only for the one you need. Order below is registration order.

| # | Service | Owns |
|---|---|---|
| 1 | `WorldGuardService` | Runs the world-compatibility guard (format-version or timeline-tag mismatch ⇒ full clean world reset) **before any other autoload can load a `user://` file**. Nothing else. Must stay first |
| 2 | `UserStatsService` | The player's betting stats in **two layers with different lifetimes**: the pruned bet journal (`BetHistory`, segmented by 10,000 entries, capped at 20 segments) and the **unpruned lifetime rollup** that outlives it; emits `StatsChanged` on a 250 ms throttle |
| 3 | `CalendarTimeService` | The game clock — local/UTC game time, `SpeedMultiplier`, the DEV `DevTimeScale` (the **effective** scale, written only by `DevTimeScaleGovernor` via `SimulationService`; the selector writes `RequestedDevTimeScale`), and `SimulationThrottle` (the fraction of simulated time the bet engine actually retained) |
| 4 | `BankrollStateService` | The **Bankroll** balance — the active betting subaccount. Balance only; transfers are #6 |
| 5 | `PrincipalBalanceService` | The **Main Balance** — the player's reserve outside active betting; fires `BalanceChanged` on every mutation |
| 6 | `BankrollProgramService` | Main ↔ Bankroll transfers: the auto-recharge dose, the `AutoRechargeEnabled` switch, transfer history, recharge counters |
| 7 | `CasinoScBalanceService` | The **casino's own** SC balance sheet (Main/Bankroll/dose) and its bet-result write path; draws its loans through the FED (#12) |
| 8 | `CasinoClientLedgerService` | Per-client SC deposit/withdrawal ledger **and** the per-client bet-stats book, for the five canonical clients (`player` + `bot_1..4`) |
| 9 | `PlayerBankAccountService` | The player's **Private Bank Account** — an optional SC reserve outside the casino, with four manual/auto Bank↔Main flows. All automation defaults OFF |
| 10 | `CasinoCoinSwapService` | The casino **swap desk**: SC↔BTC quotes and execution, per-asset strategic reserves, the swap fee and its deviation cap |
| 11 | `ScMonetaryLedgerService` | The **SC Monetary Ledger** — every event where SC enters or leaves existence (mint/burn), under `circulation = grants + debt`. Accounting only, never value |
| 12 | `CentralBankService` | The **Central Bank (FED)** — the authoritative per-client loan accounts (outstanding / drawn / repaid / history). No interest, no credit limit |
| 13 | `BlockSessionCheckpointService` | The **block checkpoint** — snapshots every money service plus the game clock at each mined block, and at boot either restores it or runs the pre-genesis reset. See Important Pattern 2 |
| 14 | `SceneManager` | All scene transitions behind a `SceneId` enum, plus a one-deep `PreviousScene`. Documented in **Scene Management** below, not in `SERVICES.md` |
| 15 | `NotepadService` | The in-game notepad's notes (`user://notepad_notes.json`): list/load/save/delete by name. Documented in `ProjectDesignManual.md` §20.1 |
| 16 | `FoundersMiningService` | Satoshi's and Hal's player-era mining power and the regulator math. A **pure controller** — no chain state, no persistence |
| 17 | `SimulationService` | The running **background simulation** — player autobet, bot runners, and the per-frame drive for the founders (#16) and the scheduler below — so it all survives scene changes |
| 18 | `BtcMarketDataService` | The historical BTC/USD daily dataset: O(1) price lookups, `MarketDayChanged`, and the data-driven Market-Birth (trading-unlock) gate |
| 19 | `BtcNetworkDataService` | The historical BTC network daily dataset, plus every derived accessor built on it (era-standard power, cast-size target, tx/block target, the fee schedule it pushes into `NetworkFeePolicy`) |
| — | `NetworkPopulationScheduler` | **Not an autoload** — a `static class` driven per-frame by #17. The historical network's visible cast + invisible mass, and the ghost attribution of their blocks |
---

## Core Game Systems — index

Full specification, per system: **`Documentation/ARCHITECTURE.md`**. Read the line here to know which file owns a concern; open the doc only for the one you need.

| System | Lives in | Owns |
|---|---|---|
| Dice Engine | `Scripts/Dice/` | The 00–99 roll, the multiplier formula, the profit calculation |
| Betting Strategy | `Scripts/Betting/` | `IBettingStrategy`, the progression, `BettingStrategyConfig` and its two independent stops |
| Bet Sessions | `Scripts/Sessions/` | Run state, remaining bets, progression streaks, stop conditions |
| Bet Execution | `Scripts/Game/` | `BetService` — the wallet↔dice↔stats pipeline and the fractional-profit carry |
| Blockchain / Mining | `Scripts/BlockchainPort/` | The continuous regulated difficulty, `NodeAgent`, the UTXO model, founder economics |
| Data models | `Scripts/Finance/`, `Scripts/History/` | `Wallet`, `Money`, `Transaction`, `BetRecord`, the history repository |

### The rules that live here rather than in the doc

Three instructions are embedded in that specification and stay in this file, because each says **what not to write**:

1. **A running session's parameters come from the SESSION, never from the panel** (D-M2.8). A session captures its `BettingStrategyConfig` at `Start()` and nothing re-pushes it, so `StrategyControlPanel` edits the *next* run. Read a live run through `BaseBetSession.SessionConfig`; **every `_strategyPanel.*` read inside a running-session code path is a bug candidate.** `DiceGame._nodeStrategies` is `static` deliberately — as an instance field it emptied on every scene round-trip and silently produced flat betting.
2. **Never use `Transaction`'s legacy `Sender`/`Recipient`/`Amount` shims to scan the chain for address membership.** They expose only `Inputs[0]`/`Outputs[0]`, so a change output at `Outputs[1]` is missed — the bug that made change-held funds vanish from wallets after a restart. Iterate the full `Inputs`/`Outputs` lists.
3. **Never use `tx.Sender` as a PARTICIPANT identity.** A spend whose coin selection consumed a change-address UTXO carries that derived address in `Inputs[0]`. Resolve ownership through the node's full owned-address set (the `BuildAuctionBidderIdentity` pattern) — **an address is a key, not an identity.**

---

## Canonical Decisions

These values are fixed and must be consistent across all docs, UI, and code:

| Decision | Canonical Value |
|---|---|
| General initial balance | `40,000 SC` |
| Specific split | `39,900 SC Main Balance + 100 SC Bankroll`. **⚠️ Produced LAZILY, not at world start:** a freshly reset world reads **`40,000 Main / 0 Bankroll`** until the first entry to `DiceGame` fires the extra-lazy Bankroll recharge. Both states are correct — **verifying a fresh world against this row before opening DiceGame will look like a bug and is not one** |
| Private Bank Account (Step 12) | Starts at `0` — an **optional SC reserve outside the casino**, all automation OFF by default. The player *owns* it (no debt); withdraw Main→Bank to park SC safe, deposit Bank→Main to bring it back. Managed in `ScFinances`. See `PlayerBankAccountService` |
| Player-facing term | `Main Balance` (not "Principal Balance") |
| Game over condition | `Private Bank Account + Main Balance + Bankroll = 0` (Step 12 / D-SF2.1 — total ruin across all three SC accounts; while the bank holds anything it is **not** game over, since the player can always deposit it back). Written to leave room for a future **BTC→SC coin-swap escape hatch** (§7.4) — the check must be interceptable by a later exchange layer, not an irreversible terminal |
| Current mining rule | `1 bet = 1 nonce attempt` |
| Basic Mode halving | `2,100 blocks` (≈ 4 in-game years at 100X scale). **The only independent value of the three rows here** — `NetworkRoot.HalvingIntervalBlocks`, from which the other two are read or contrasted |
| Total BTC supply | `210,000 BTC` — **a CONSEQUENCE of the row above, not a constant.** It is `50 × HalvingIntervalBlocks × 2` and exists nowhere in the code as a literal; change the halving interval and this figure changes with it, silently, with nothing to catch the stale copy. Converges to in-game year ~2141 (`= 34 × HalvingIntervalBlocks` blocks) |
| Real Bitcoin halving | `210,000 blocks` — **NOT used in Basic Mode, and NOT the row above.** The numeral is a coincidence and the units differ: the row above is `210,000` **BTC of supply**, this is `210,000` **blocks between halvings** in real Bitcoin. They are unrelated quantities that happen to share a number, listed adjacently, which is precisely how one gets mistaken for the other |
| Block transaction cap | `24 transactions` ✅ **Implemented** — `BlockTemplateBuilder.MaxBlockTransactions = 24`, counting the coinbase (coinbase + up to 23 mempool txs). Fits the 1:100 fractal replica (real blocks carry ~2,000–3,000 txs) |
| BTC/SC trading unlock (Step 13) | **2010-07-18** (Mt. Gox launch — the first date of the historical price CSV). Before it: no market, no price, no swap UI beyond a locked teaser. **The gate is DATA-DRIVEN (`BtcMarketDataService.FirstDataDateLocal`) — never a second hardcoded date.** The player waits ~484 in-game days from the 21 Mar 2009 start, accepted as historically honest |
| Timeline (DEV alt-timeline guard, Step 13) | The canonical timeline (genesis `2009-01-03`) is the ONLY shipping one: `TimelineConfig.DevAltTimeline` is **`false` on `main`, forever**, and every historical date anchor routes through `TimelineConfig.Shift()`. A timeline switch **auto-wipes the world in BOTH directions** via `world_timeline.stamp` + `NetworkRoot.ResetWorldIfIncompatible()`, run by `WorldGuardService` so the wipe precedes every state-file load. **⚠️ Every new persisted world-state file MUST be added to that delete list** (Pattern 2's third question). Re-mount guide for the DEV simulacrum: `ProjectDesignManual.md` Ch. 35 |
| Hardware cap | `100 nonce attempts` per time cycle (planned) |
| Network fees — Historical Fee Replay (Step 14 ND.7) | **The fee era begins at Market Birth, data-driven — no fee exists anywhere on the network before it**, and from it the real daily historical band is replayed from the network dataset (fees are **fractal-exempt**: face value, never /100). **`NetworkFeePolicy` is the single source of truth** — a static schedule pushed by `BtcNetworkDataService.EnsureLoaded()`; no schedule ⇒ fee-free fallback plus one warning, **never a hardcoded scaffold.** The legacy `DefaultFee/MinFee/MaxFee`, `NetworkRoot.MinBotFeeBtc/MaxBotFeeBtc/CasinoTxFee` and `TimelineConfig.FeeActivationLocal` are **deleted — do not reintroduce a constant fee.** Band derivation, the carry-forward rule, the honest-zero era and the median's provenance: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10 |
| RTP | `99.02%` |
| Number format | `1,000,000.00000000` — comma=thousands, period=decimal (`CultureInfo.InvariantCulture`); never use raw `:N8`/`:F2` in string interpolations |
| Currency for betting | SC only — BTC cannot be wagered directly |
| SC value (D-ND8.30, Step 14 ND.8c) | **The 1:1 USD peg is canon.** The economy simulates SC *quantity and credit* — never *value*: monetary tightening is expressed as credit scarcity (who can borrow, and how much), never as inflation or peg drift. **Option C of the fiat-debt ladder — inflation/devaluation — is rejected forever**; Option 0 (`ScMonetaryLedgerService`) and Option A (`CentralBankService`) are built, Option B (full fractional-reserve) is documented post-Basic-Mode. A price that moves is always a BTC price, never an SC one |
| Casino SC defaults (mirror of an average player) | Casino auto-loan chunk `40,000 SC` (`= InitialLoanAmount`, a player's total start) + bankroll dose `100 SC` (`= DefaultBankroll`, a player's Bankroll). Extra-lazy first funding then reproduces the player's `39,900 Main / 100 Bankroll` split. Dev-configurable (`AutoLoanAmount` / `BankrollTarget`), reverts to these defaults pre-genesis. (CG.3.D) |
| Founders | Satoshi (target `11,000 BTC`, retires ≥ `2011-04-26`, then frozen) + Hal (`P=1.0` drip, fades to 0 by `2009-08-09`) + Mike Hearn (joins ~Apr 2009, never mines; the **round-trip is 32.51 BTC** and he **nets +82.51** — the difference is Satoshi's separate 50.00 gift, `HistoricalEventScheduler` E7a + E7b. `82.51` is the net, never the transaction) |
| Referral auction | 40 non-miner companies, ascending BTC auction, rolling 20-day window. **A win is permanent** (D-ND4b.12). Spec: `Documentation/REFERRAL_AUCTION.md` |
| Player start | `21 Mar 2009`, at the **exact same timestamp** as the bootstrap's last mined block — no dead time, no offset. A specific case of the general rule stated in Pattern 2: **the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world state.** One known exception: an offset of up to one frame for bot-mined blocks (mini-plan 08 D4). How each capture path stamps it: `ProjectDesignManual.md` §24.9 |
| Swap desk fee (Step 13, network-fee component replaced at ND.7) | `10%` default, dev-clamped `1%–10%`, governing **both** directions. **ADDITIVE, never absorbed** (D-SW.11): `totalFee = networkFee + casinoFee`, the network fee being a separate charge summed with the casino's cut. `networkFee` is **the day's replayed median for the current game date**, never a constant. The deviation cap (`MaxFeeDeviationPoints`) limits **the casino's cut alone** — **the network fee is never capped.** Formula detail, the cap's clamp, and the base-address rule: `Documentation/SERVICES.md` → `CasinoCoinSwapService` and `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` |

---

## Implementation Status

Per-feature record — what shipped, when, and the decisions behind each step: `Documentation/IMPLEMENTATION_STATUS.md`. Roadmap priorities: `Documentation/PRIVATE_ROADMAP.md`.


---

## Important Patterns

### 1. Event-Driven Services

Services communicate via typed C# events, not Godot signals:

```csharp
// Emitting
event Action<UserBettingStats> StatsChanged;

// High-frequency throttle pattern
private void EmitStatsChangedIfNeeded()  // 250 ms throttle
```

This is the service-to-service half of a broader project-wide principle — see **Pattern 6** below for the full rule (when `_Process` is and isn't warranted) and the standing project goal to audit every remaining poller before Basic Mode v0.1 ships.

### 2. Checkpoint / Rollback — a block is the only commit to disk

`BlockSessionCheckpointService` captures the full financial state at each block mining event. This is the only rollback mechanism. Do not add ad-hoc save points elsewhere.

**Within a session**, the live clock, balances, and mempool advance and survive scene changes — the autoloads and the **static** `NetworkRoot` hold them in memory. **Nothing between blocks is COMMITTED** — the chain and the mempool are not written at all, and the eagerly self-persisting services below *do* write between blocks but their files are **discarded at every boot** (restored from the checkpoint, or reset pre-genesis), so no between-block state survives a restart either way. *This sentence read "nothing between blocks is persisted to disk — not SC balances" until 2026-08-30, when mini-plan 08 measured `BankrollStateService` writing the bankroll SC balance to disk on **every bet** — 66% of the cost of a bet. The rule was about COMMITMENT and was written as if it were about I/O; the code had always done otherwise, and the false half of the sentence is why nobody looked.* Between-block navigation / node-switch saves use `SaveActiveNodeFinancialState(false)` (in-memory only), and BTC transactions / consensus do not persist either (`NetworkRoot.CreateAndBroadcastTransaction`/`CreateAndBroadcastTransactionToAddress` only mutate the in-memory mempool). The world write is **REQUESTED, not performed** (mini-plan 14 A): `RequestWorldPersist()` marks it dirty at block-mining (`HandleMinedBlock`), baseline node creation and startup, and `FlushWorldIfDirty()` performs **at most one write per frame** — at the top of `SimulationService._Process` *before its early return*, and again *before every checkpoint capture*, which is what keeps **the chain on disk never older than the checkpoint naming it**. A commit therefore lands milliseconds after its block, inside the same frame; the player's block-commit financial write still goes through `SimulationService.CaptureCheckpoint` / `DiceGame.CaptureBlockCheckpoint`. **The chain itself is a separate, append-only file** (`user://blockchain/chain.jsonl`, one block per compact line — never the indented `JsonOptions`), and `state.json` holds the mutable remainder plus a **tip stamp** (height + hash) naming the chain it belongs to: longer-than-stamp is truncated loudly, shorter-than-stamp aborts the load, and **a failed load now also stops the bootstrap and the checkpoint** (`NetworkRoot.WorldLoadFailed`) so a broken world is never replaced by a fabricated one. Consequently an **app restart reverts the whole world to the last mined block** — clock, every participant's balances, **and** un-mined pending transactions — performed at startup by `BlockSessionCheckpointService.ApplyCheckpointToServices()`. Within-session re-entry must never rewind the clock: `DiceGame` skips `EnsureGameEpochInitialized()` while `SimulationService.IsRunning`, and the checkpoint clock/history restore is a once-per-process operation guarded by the static `_checkpointRestoreSpentThisSession`.

**This principle applies to EVERY player-facing persisted value, not just the four services `ApplyCheckpointToServices()` lists** — `BankrollProgramService` (dose + transfer records), the game clock, and the bet-history log (`UserStatsService`) all self-persist eagerly (on every dose change / bet / recharge) and MUST be explicitly included in both the post-first-block checkpoint restore and the pre-genesis reset below, or they silently leak uncommitted state across a restart. When adding a new player-facing autoload or persisted list, ask: "does this need a `BlockSessionCheckpointService` restore path (post-block) AND a `ResetToPreGenesisDefaults()` path (pre-block)?" — if it holds player state that changes outside of a mined block, the answer is yes. **And a third question (TL.3 lesson): "is its `user://` file in the `NetworkRoot.ResetWorldIfIncompatible()` delete list?"** — every persisted **world-state** file must be, or it leaks across a format/timeline clean reset (`casino_coin_swap_state.json` missed this and alt-world hardware/pool state survived a timeline wipe).

**The exempt set, named in full (2026-08-23) — an exemption nobody wrote down is indistinguishable from an omission, which is exactly how two files slipped through.** Deliberately NOT deleted: the five wallet seeds (`wallet_state`, `casino_wallet_state`, `satoshi_wallet_state`, `hal_wallet_state`, `mike_hearn_wallet_state`), `bot_wallet_registry.json`, `notepad_notes.json`, `saved_betting_strategies.json`, and **`wordlist_256.json`** (the seed-phrase wordlist — identity infrastructure; exempt in code since it shipped but unnamed in any document until now). Also exempt: **`user://logs/godot*.log`**, Godot's own engine logs — they are *evidence* (mini-plan 07 §A.6.2 read them while dating INC-004), the engine rotates and caps them at five per world so they cannot accumulate, and no code reads them back. The two stamps `world_format_version.txt` / `world_timeline.stamp` are rewritten by the wipe itself rather than deleted.

**Repair and diagnostic siblings are swept by SUFFIX, not enumerated** — `.tmp`, `.corrupt`, `.prerepair`, in `user://` and `user://blockchain/`. Enumeration failed twice in one week (`cb1779a` created `.corrupt`/`.tmp` and listed neither; two `.prerepair` files then survived the 5→6 wipe), and it **cannot** work for `.prerepair`, which no code writes — a human makes it during a manual repair, so there is no feature to attach an "add it WITH the feature" rule to. The sweep prints every file before destroying it. **The convention that makes it safe, and which you must follow: a file carrying one of those suffixes is NEVER the only copy of anything — if you are repairing a world by hand, archive it OUT of `user://` first.**

**Pre-genesis (no block has EVER been mined — only the historical bootstrap has run)**: a checkpoint is captured **only** by a real block-mined event now (`DiceGame.CaptureBlockCheckpoint()` / `SimulationService.CaptureCheckpoint()`) — never merely by opening the app (there is no more "baseline" auto-capture). Whenever `BlockSessionCheckpointService.HasCheckpoint()` is false, `ResetToPreGenesisDefaults()` runs on every boot instead of `ApplyCheckpointToServices()`, forcing Main Balance/Bankroll/dose/transfer records back to their true canonical defaults, and resetting the calendar + bet history to the historical bootstrap's landing instant (re-derived from the chain tip via `NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()` — before any real block, the tip *is* the bootstrap's last block, so nothing extra needs to be persisted for this). **Canonical rule**: the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world — never offset, not even by one second (every checkpoint capture stamps the instant of the bet that mined the block, so this is true post-first-block — bar the bot-mined offset recorded in the Player start row; the pre-genesis reset and the historical bootstrap's player-start instant both follow the same rule deliberately). See the Canonical Decisions table above ("Player start") and `Documentation/ProjectDesignManual.md` §24.9.

**Canonical rule — game time, never wall-clock, for anything the player can see or that gets persisted.** Every event timestamp that is displayed, stored in a `TransferRecord`/`LoanRecord`/`BetRecord`/ledger entry, or compared against a checkpoint boundary **must** come from `CalendarTimeService` (`.CurrentUtcDateTime` / `.CurrentLocalDateTime`) — **never** `DateTime.Now`/`DateTime.UtcNow` directly. An audit (2026-07-01, OQ-BP.10 in `AIHelperFiles/player-and-casino-bankroll-programmer-plan.md`) found this violated in several places already shipped earlier in the same plan — most seriously, `DiceGame`'s `BetService` timestamp provider used `DateTime.UtcNow` for **every manual bet**, which (since `RollbackHistoryToUtc`/`GetLoadedHistoryStats` compare bet timestamps against the game-time checkpoint boundary) would have silently corrupted the pre-genesis history-rollback fixes above for manual play. All such call sites were fixed to read `CalendarTimeService` (with a `?? DateTime.UtcNow` null-safety fallback only, never as the primary source). **The only legitimate use of real wall-clock time** is pure internal DEV/file bookkeeping metadata the player never sees (e.g. `BlockSessionCheckpointService.CapturedAtUtc`, each service's own `UpdatedAtUtc` snapshot field) or genuine real-time concerns unrelated to game-world state (`UserStatsService`'s 250ms UI-throttle timer, `DiceGame`'s real-bets-per-second rate-measurement fields). When adding any new timestamped record, ask: "is this game-world state, or pure DEV telemetry?" — if the player could ever see it, it's game time.

Full rationale and the bugs this resolved: `Documentation/ProjectDesignManual.md` §24.8 (post-first-block), §24.9 (pre-genesis + the exact-timestamp rule), and §24.10 (the wall-clock-vs-game-time audit).

**⚠️ This rule governs commit TIMING, not commit DURABILITY — they are separate problems.** "A block is the only commit" says *when* to write and is silent on what a half-written file means. **When persisting player-owned state, answer all three:** is the write **atomic** (`.tmp` → flush → rename, never truncate-and-stream) · does a corrupt read **fail loudly** (a `Try` prefix is a promise — honour it or drop it) · can a failed load ever be **persisted back over the good copy** (guard the writer, not just the reader). A world was once lost to all three at once. Case: `INCIDENT_LOG.md` **INC-001**; rules: `ProjectDesignManual.md` **Ch. 40** + §39.16 rule 7.

**⚠️ Three further instalments, each earned by a real loss. The RULES are below; the cases are `INCIDENT_LOG.md` INC-002/003/004 and `ProjectDesignManual.md` §40.8 — read those to learn why a rule has its shape, not to re-derive it.**

**Fixing the WRITER is half a fix** (INC-002/003, §40.8):

- **When an incident names a corrupted input, enumerate its CONSUMERS** and harden the one that *distorts* the error most — a sum hides it, a streak broadcasts it.
- **Where a displayed figure has a cheap closed-form bound, assert it**, or its only detector is a human being surprised.
- **A label is a claim about semantics**, and it gets audited far less often than the arithmetic under it.

**The REPAIR is also a writer** (INC-004):

- **A default value is an assertion, and a fresh object asserts the most flattering one.** A field encoding *coverage* (`IsComplete`, `IsFull`, `HasAll`) must default to the **pessimistic** side, because the paths that skip initialization are the failure paths.
- **Declining to record a value is not the same as erasing the value that was there.** A capture returning "nothing" into a structure rewritten wholesale must **carry the previous value forward**.
- **Fixing a writer creates a new writer; ask it the same three questions.** That fix's own first draft was atomic and loud and still turned a recoverable failure into a permanent one.

**Refusing to persist is HALF a guard** (mini-plan 15 A — a failed load stopped the world write, the bootstrap and the checkpoint while **twelve other services carried on writing**):

- **When one writer learns a world is unreadable, enumerate the others.** They compile, run, and write a plausible file. Every eager writer of world state calls `WorldWriteGuard.RefuseWrite(nameof(X))` first — **add the call in the same commit as any new one.**
- **Reading a checkpoint is fine; writing it back is not.** A restore is not a commit.
- **Guard the disk-mutating PRIMITIVES, not the convenient entry point** — the journal is guarded at its three, not at `Flush`, so `Flush` still drains its buffer and the session stays memory-bounded while writing nothing.
- **Exempt by CLASS, never by list:** identity files and every `user://logs/*` trace (evidence, and a broken session is when it is worth most). The verification predicted the exempt *class* correctly and its *enumeration* wrongly. **The class holds, the list never does.**

### 3. Fractional Profit Accumulation

`BetService` accumulates sub-satoshi remainders internally. Never round individual bet payouts at the call site — let `BetService` handle precision.

### 4. Legacy Naming Migration

Internal service classes still use `PrincipalBalance` names. User-facing labels **must** use `Main Balance`. Internal class renames are deferred. Do not introduce new code that uses `PrincipalBalance` as a user-facing string.

### 5. Autoload Access Pattern

In Godot 4 C#, autoloads are nodes attached under `/root/`. The correct access pattern is `GetNodeOrNull<T>("/root/ServiceName")` called in `_Ready()`, stored in a private field:

```csharp
private CalendarTimeService _calendarTimeService;
private BankrollStateService _bankrollStateService;

public override void _Ready()
{
    _calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
    _bankrollStateService = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
}
```

Use `GetNodeOrNull` (not `GetNode`) so the app does not crash if the autoload is temporarily absent during development. Always null-check before use: `_calendarTimeService?.CurrentLocalDateTime`.

**Do not** access autoloads by bare class name or via a static `Instance` property — Godot C# autoloads do not work that way.

**Registration ORDER in `project.godot` is load-bearing — treat the `[autoload]` block as ordered code, not a list.** Godot runs each autoload's `_Ready()` in declaration order, so a service that reads another's state at boot must be declared after it. Four ordering constraints hold today, each recorded in `Documentation/SERVICES.md`:

- **`WorldGuardService` is FIRST**, and running before everything else is its entire reason for existing. It wipes an incompatible world; an autoload that loaded a `user://` file into a static cache *before* the wipe survives it in memory and re-persists afterwards (the TL.3 incident, where alt-timeline hardware/pool state leaked into a fresh canon world).
- **Every service the checkpoint restores is declared before `BlockSessionCheckpointService`** — it sits at #13 for that reason, and `PlayerBankAccountService` / `CasinoCoinSwapService` / `ScMonetaryLedgerService` / `CentralBankService` were each slotted in ahead of it as they shipped.
- **Within that group the money services have their own order**: `CentralBankService` restores before `CasinoScBalanceService` (which reads its loan figures through the FED) and before `ScMonetaryLedgerService` (whose reconcile reads the FED's casino account).
- **`BtcNetworkDataService` comes after `BtcMarketDataService`**, so Market Birth is already known when its derived accessors compute at load.
- **`UserStatsService` (#2) must stay ahead of `BlockSessionCheckpointService` (#13) — this one is a RECOVERY path, not just an initialization order.** #2 loads the lifetime rollup and latches if the file is unreadable; #13 then restores the checkpoint's own copy and clears the latch. That sequence is what makes a destroyed rollup recoverable at all. Reverse them and the good restored value is overwritten by the failed load, turning a recoverable fault into a permanent one (INC-004).

Two consequences for new work: a service needing one declared *earlier* than itself resolves it **lazily, never in `_Ready`** (`CasinoScBalanceService` resolves the FED this way); and adding an autoload means choosing its position deliberately and saying why — appending to the end of the block is a decision, not a default.

### 6. Prefer Event-Driven Design Over `_Process` Polling

**This is a standing project-wide design principle, not a one-off — apply it to every new system, and treat it as a checklist item before ANY code review is considered done.** `_Process(double delta)` runs every rendered frame. Reaching for it by default is the single most common way to smuggle needless per-frame CPU work into a project whose core loop (bet → nonce attempt → time tick) is already discrete and event-shaped from the ground up.

**The rule:** before writing `_Process`, ask *"does this genuinely need to know about the passage of REAL time, every frame?"*

- **Yes** → advancing a real-time clock, an animation, a UI countdown against wall-clock delta. `_Process` is correct and necessary. Examples already in this codebase: `CalendarTimeService` (advances the game clock by real delta × speed multiplier — nothing else could drive it), `SimulationService` (the background sim's per-tick bet/mining loop), `DiceGame.TickAutoBet` (autobet pacing/animation).
- **No, it only re-reads STATE that changes on a discrete event** (a bet settled, a block mined, a transfer completed, a claim was pressed) → **this is the polling anti-pattern.** The state owner (a service) should fire a typed `event Action<T>` at the exact point the state changes (Pattern 1 above); the consumer (usually a UI scene) should subscribe in `_Ready()` and unsubscribe in `_ExitTree()`, and stop polling entirely.

**The hybrid middle case — a cheap edge-trigger inside `_Process`.** Sometimes the STATE only changes on a boundary that isn't itself a discrete game event (a calendar day rolling over). `BtcMarketDataService`/`BtcNetworkDataService` are the reference pattern: `_Process` does the *cheapest possible* single date comparison against the game clock every frame, and fires `MarketDayChanged`/`NetworkDayChanged` **only** when a day boundary is actually crossed — no timers, no per-frame parsing, no I/O most frames. If you must poll something inside `_Process`, this is the shape: the per-frame cost should be one flag/value comparison, and the real work (rebuilding a panel, hitting disk) belongs behind the resulting edge, never inside the poll itself.

**A signal doesn't have to be a Godot/C# event — an in-memory flag with edge-triggered updates is the same idea.** ND.8d round 3's stuck-bidder-escalation fix (`NetworkRoot._stuckBidderSignatures`, 2026-07-21) is the freshest example: rather than replaying bid history every roll (expensive, and still not `_Process`-shaped) or polling anything per-frame, it stores a small `(signature, sinceBlockIndex)` per (company, bot) — updated once, exactly when the signature actually changes, inside the SAME block-mined event that already drives the whole bidding cascade. No new persisted state, no per-frame cost, no history replay. When a "since when has X been true" question comes up, reach for an edge-triggered signal like this before reaching for either a poll or a full replay.

**But such a cache is EMPTY AND LYING at process start.** Two rules, the second learned only after the first was written down and violated anyway:

- *Any in-memory cache a per-block sweep owns has a window at start where it is empty; if a reader can predict the sweep cheaply, it must.*
- **But predicting a sweep from the current value only works for a MEMORYLESS predicate. A predicate with hysteresis has to be REPLAYED** — between its two thresholds the answer depends on how the value arrived, and no reading of today's value recovers that.

Corollary: a drift filed as "harmless and self-correcting" stops being harmless the moment the mechanism it feeds decides **ownership** rather than pacing. Re-read those judgements when a system's stakes change. Cases: `ProjectDesignManual.md` **§22.18** and **§22.20**.

**Already-good examples in this codebase (services firing typed events on real state changes):** `UserStatsService.StatsChanged` (throttled 250ms — the reference pattern for a HIGH-FREQUENCY event, `EmitStatsChangedIfNeeded()`), `SimulationService.ClientBetSettled`, `CasinoClientLedgerService.LedgerChanged` / `ScMonetaryLedgerService.LedgerChanged`, `PrincipalBalanceService.BalanceChanged` / `CasinoScBalanceService.BalanceChanged`, `PlayerBankAccountService.BankStateChanged`, `CasinoCoinSwapService.SwapDeskChanged`, `BtcMarketDataService.MarketDayChanged`, `BtcNetworkDataService.NetworkDayChanged`.

**The backlog exists and is catalogued elsewhere.** Roughly fifteen scenes still poll on a timer to rebuild a panel from state that only changes on a discrete event. **Do not add a new poll-shaped `_Process` to it without first checking whether an event already exists** (or should) for the state you are reading. The list, the two implementation caveats, and the Basic Mode v0.1 gate: `Documentation/ProjectDesignManual.md` **Ch. 38** (§38.4 the existing events, §38.5 the candidates) · `PRIVATE_ROADMAP.md` §6. ⚠ That catalogue was audited 2026-07-21 and entries have moved since — **re-derive it from the code before working through it** (Step 17 §5.1).

**The INVERSE failure — a correct event, fired far too often, driving expensive work.** It cost more than any poll in the backlog, so migrating a poll to an event is **not automatically an improvement**. Three standing rules:

1. **Frequency is part of a subscription's contract.** Re-examine subscribers when an event's real rate changes — one such change multiplied a rate by 5 and nothing re-checked.
2. **Coalesce at the consumer when the trigger cannot move the value** (Pattern 6's hybrid, used deliberately).
3. **A displayed throttle is a MEASUREMENT, not a diagnosis.** Below-1 retention means *"find what is eating the frame"*, never *"raise `MaxBacklogSeconds`/`MaxBetsPerFrame`"* — which only hands a saturated frame more work.

Case, with the measurement that caught it: `ProjectDesignManual.md` **§38.7**.

**Project goal, tracked in `Documentation/PRIVATE_ROADMAP.md` §6:** before Basic Mode v0.1 is considered complete, audit every `_Process` override in the project against this principle and migrate what's feasible to event-driven design. Not a hard blocker on other work — but do not add a NEW poll-shaped `_Process` to the backlog above without first checking whether an event already exists (or should) for the state you're reading.

**Five ways an instrument or a UNIT lies, each measured the hard way — the rules here, the cases in `ProjectDesignManual.md` §40.12.** Six consecutive mini-plans were performance work and in every one the instrument was as much the subject as the code:

1. **An instrument that filters cannot see what it filters out** — so when a profiler filters, something must still watch the clock.
2. **A residual is not a measurement of the work inside it.** Before predicting a saving, check the instrument measures the work and not the leftover.
3. **A PER-BLOCK MILLISECOND FIGURE DOES NOT REPRODUCE ACROSS SESSIONS HERE — it is not evidence of a code change.** So **state a cost criterion as a COUNT**, **compare within a session**, and **count where the work happens, not where you expect it.**
4. **A structural reading tells you what HAPPENS; only a measurement tells you what it COSTS.** Corollaries: **check who CONSUMES a reader before adding work to it**, and **a lesson recorded in a close-out is not a lesson applied.**
5. **A correlation needs variance in its predictor** — check it moves inside the window the run will cover.
6. **A threshold on a share is a threshold on its denominator too.** **State a cost threshold in absolute units when the denominator is noisier than the term you are watching.**


**Closing rule — a cost note is a MEASUREMENT or it is a guess wearing a measurement's clothes.** Every judgement on this page is a performance judgement. **Time it, or say plainly that you did not** — a figure that merely *looks* measured is the one nobody re-checks, and one such note was five orders of magnitude out. And **when a documented cost comes true, re-read the note for the mitigation it already named.** Case: `ProjectDesignManual.md` **§40.7**.

### 7. Standing Conventions — rules that outlived the phase that produced them

Each of these began as a one-off call inside a single build phase, recurred, and is now a **default for
all work**. They were extracted here from `Documentation/IMPLEMENTATION_STATUS.md` (2026-08-20), where
they were stated as rules but reachable only by reading a status entry — a rule nobody can find is a rule
nobody applies. The phase that produced each is named so the full case is recoverable.

**The six from Step 15 (`Documentation/ProjectDesignManual.md` §39.16):**

1. **Never let a persisted figure diverge from reality** — the exclusions that keep a tracked quantity truthful ship in the *same phase* that creates it. A lying number is invisible and compounds; an absent feature is not.
2. **A phase you cannot observe is a phase you cannot sign off** — pull the minimum readout forward from its nominal subphase and note the borrow.
3. **Prefer deletion to a flag** when something is over — but hunt down consumers that read the record from a different source.
4. **Version-bump and wipe by default**; re-derive only genuinely derivable values, and never contort a design to avoid a bump.
5. **A new field on an existing persisted record gets a sentinel default + backfill** when its populator is guarded by an "already populated?" check. This is about silent failure modes, not about bumps.
6. **A displayed signal must share its source with the action it advertises** (the ND.10d rule) — a preview is a promise about what the resolver will do.

**The five later ones:**

7. **Project, never clamp** (P15.9) — mapping a value into a narrower legal range by clamping collapses distinct inputs onto the bound; project so the distinctions survive.
8. **Re-deriving a verdict from a persisted record means reproducing the resolver's GUARDS, not just its arithmetic** (P15.10) — a kind gate or an entity gate the resolver applies is part of the verdict.
9. **A `> 0` threshold on money produced by division is a threshold on rounding noise** (P15.10) — pick the cutoff where the figure becomes *visible* in its own readout. If the game cannot display it, it cannot be worth acting on.
10. **A phase whose exit depends on states the game cannot yet produce is SUSPENDED, with its missing precondition named** (D-15.32) — never ground for more hours. The mirror of rule 2.
11. **When a system is meant to feel alive, assert that its output actually VARIES** (D-15.34) — the same reflex rule 1 applies to a figure that lies, applied to a figure that never moves.

**Two from Step 16 P16.2/P16.6, both about retired premises:**

12. **When deleting a workaround, re-derive the set of cases it covered from the CODE** — never trust the scope list written when it was added; it was accurate then and the code moved.
13. **When a capability is extended to a new class of participant, the reads that were correct only because that class lacked it will not announce themselves** — they compile, run, and return a plausible number. **Grep for the retired premise, not just the code implementing it.**

**Two from mini-plan 07 (2026-08-22 / 08-23):**

14. **A bulk edit made through a shell or a regex is not finished until a corruption grep over the RESULT says so.** **⚠ And it happened again on 2026-10-07, to this very rule's own neighbourhood:** a `node -e` pass rewriting the locale block **ate every backslash in the stored detector commands** (`\$"` → `$"`, `/\.godot/` → `/.godot/`, `\\n` → a real newline that broke the `printf`), and reported success. **A mangled detector returns 0 hits and reads as "the project is clean"** — the worst possible failure for a tripwire. Restored from `HEAD`, redone with the file tool. **So: for a stored COMMAND, the corruption check is to extract it FROM the document and run it** — never to trust that the document matches what you ran. It returns 17, its stated baseline. Its characteristic failure is not an error — it is **content silently removed**, on a command that reports success. Case: a `node -e "…"` one-liner rewriting five table rows had every **backticked span eaten by the shell** before node ever parsed the script; the rows landed with their code spans and file:line citations blank, and the run printed `rows replaced: 5`. **Before accepting such an edit, grep the result for the shape of what would be missing** — empty code spans, `[V: ]`, a row conspicuously shorter than its neighbours — and prefer the file-editing tool outright when the content carries backticks, `$`, or quotes. The ⚠ bullet under **Money Handling** ("do not fix these with a bulk regex", where a `perl -pi -e` pass blanked 24 lines) is this same rule found on a different day, against different content; **this is its general form, and the two should be read together.**
    **⚠ For a WHITESPACE-only bulk edit specifically, `git diff -w` is NOT a verification — it is the one check guaranteed to pass.** It ignores whitespace everywhere, *including inside string literals*, so it returns empty for a benign added final newline and for a corrupted multi-line verbatim string alike. Verify instead by **comparing the content with only the LEADING indentation stripped** (`sed 's/^[[:space:]]*//'` both sides, then compare hashes) — that catches everything `-w` blinds you to — and **check first whether the files contain verbatim (`@"`) or raw (`"""`) literals**, whose leading spaces are content, not indentation. Measured on the 2026-08-23 tabs normalization: `-w` was empty for all 23 files while the stricter check correctly flagged three.

15. **A number written in PROSE beside the code that computes it is frozen at the moment it was typed. Cite the SYMBOL or the SOURCE, never the value.** Write `InitialLoanAmount`, `HalvingIntervalBlocks`, "every row in `company_roster.csv`", "the day's replayed median" — not `40,000`, not `210,000`, not "all 44 rows", not "0.1 BTC". The value belongs in exactly one place: the declaration. Prose that repeats it creates a second copy with no compiler, no test and no reader watching it, and **the copy does not rot loudly — it stays plausible.** Four instances, all the same shape, found in one afternoon: a comment reading `InitialLoanAmount = 100M` directly above the line that reads the constant, which is `40,000` (2,500× out) · `CompanyRoster` citing "all 44 rows" of a CSV holding 42 · `"chunked by month"` in four documents describing a journal segmented by **entry count**, never by date · the swap desk's flat `0.1 BTC` network fee, retired by ND.7 and quoted for a while afterwards. **Where a documented figure is a CONSEQUENCE rather than a constant** — total supply is `50 × HalvingIntervalBlocks × 2` and appears nowhere as a literal — **say so in the same breath**, because changing the real constant silently invalidates every prose copy of the derived one. This is the written twin of rule 1: rule 1 is a *persisted* figure diverging from reality, this is a *written* one doing the same thing, and neither announces itself.

**One from mini-plan 19 (2026-10-07):**

16. **A ONE-OFF VERIFIER IS AS LIKELY TO BE THE THING THAT IS WRONG — three instances in two plans.** A mechanical differ written to check a documentation edit reported **28 lost rules, every one a false positive** (its regex captured fragments; the fix produced probes that could never match). A trace column was read as milliseconds. A correlation was run against a constant predictor. **Prefer the boring check: one distinctive single TOKEN per changed passage, grepped across the corpus** — immune to rewording, trivially auditable, and runnable BEFORE the edit as well as after. **If a clever check and a boring check disagree, suspect the clever one first.** And note what this rule is NOT: the numbers in this list are citations used across `Documentation/` and the plans, so **a new convention is APPENDED, never inserted** — renumbering would silently break every reference to it.

---

## Glossary Reference

See `Documentation/GLOSSARY.md` for the full canonical terminology list. Key terms:

- **SC** — Stable Coin, simulated USD-pegged currency
- **Main Balance** — player reserve outside active betting
- **Bankroll** — subaccount of Main Balance used for active bets
- **Autobet** — automated repeated betting using the current strategy
- **Nonce** — value miners vary while searching for a valid block hash
- **RTP** — Return to Player (Dice targets 99.02%)
- **Halving** — reward reduction event; Basic Mode = 2,100 blocks (≈ 4 in-game years at 100X)
- **Stop on block mined** — strategy condition that halts betting after a block is found

---

## Development Best Practices

- Always prefer editing existing files to creating new ones
- Never create documentation files unless explicitly requested
- Verify canonical values (balances, intervals, RTP) against this file and `GLOSSARY.md` before hardcoding
- Use `Grep`/`Glob` for exploration; do not use `bash find` or `bash grep`
- Check git status before committing
- Follow existing naming patterns: `PascalCase` for classes and files, `_camelCase` for private fields
- Always call `Money.Normalize()` before storing any decimal result
- Use `DateTime.Utc` for storage; `DateTime.Local` for display
- High-frequency service events must be throttled — see `UserStatsService.EmitStatsChangedIfNeeded()` as the reference pattern

### Asking the developer to read output — NAME THE PANEL, ALWAYS

**Never write "the console", "the log" or "the output" to the developer. Say WHICH ONE, every time**, in the
same sentence as the thing to look for: **the Godot editor's Output panel** (the developer's default, and
where `GD.Print` lands) · **the Godot editor's Debugger → Errors tab** (where a C# `GD.PrintErr` lands) ·
**the terminal running `dotnet build`** · **a `user://logs/*.csv` trace** (a file, not a console at all).

**Why this is a project rule and not a courtesy.** `GD.Print` and `GD.PrintErr` do **not** land in the same
place in the Godot editor. On 2026-08-26 a mini-plan 06 harness banner was emitted with `GD.PrintErr`, the
developer read Output, saw nothing, and a correct armed build was diagnosed as stale — costing a full
aborted run. The same call had been used for **`AssertSingleActorJournal`'s `[BetJournal] UNDECLARED
balance discontinuity`**, whose *silence* is a load-bearing result in mini-plans 05, 06 and INC-003. A
"clean console" reported against the wrong panel is not evidence of anything, and it reads exactly like
evidence.

Three standing consequences:

- **A diagnostic whose passing state is SILENCE must be emitted where the reader actually looks** — in
  practice `GD.Print`, or both. This is the twin of the DEBUG-canary rule: that one settles whether the
  check *exists*, this one settles whether anyone can *hear* it. Both failures end as "nothing happened".
- **When a test protocol says "watch for X", it must name the panel beside X.** Writing the check without
  its channel is writing an unverifiable step.
- **When the developer reports "nothing appeared", the first question is which panel they read** — before
  build staleness, before code paths. It was the answer once and cost a run.

### Auditing a playtest run the developer hands you

The developer playtests and hands back a `user://` journal to audit. Two habits, both learned the hard way (2026-08-06, mini-plan 01 rounds 3–4):

- **When a dataset arrives after you recommended a parameter change, "they took the advice" is the LEADING hypothesis, not the last one.** Advice given here is acted on. The failure shape: every other parameter (base bet, both progression percents, the profit threshold) was *inferred from the data*, and the one parameter that could not be inferred was silently carried over from the previous run — the exact parameter that had just been recommended for change. It inverted a conclusion ("your loss stop never fired" when it had fired, correctly, on the final bet). **Infer what the data determines; ASK for what it does not.** A threshold that fires exactly once, on the last bet, leaves the same trace as a session someone stopped by hand — un-inferable parameters are precisely the ones worth one question.
- **Reproduce the engine's arithmetic EXACTLY, never approximately.** BigInt satoshis, `Money.Normalize` = truncation (`MidpointRounding.ToZero`), `DiceEngine.CalculateMultiplier` = `Round(100×RTP/chance, 4)`, and `BetService`'s `_pendingFractionalProfit` carry (reset only when a new `BetService` is constructed — i.e. per `StartPlayerAutobet`, **not** per auto-recharge restart). A float model turns real 1-satoshi evidence into ~120 phantom mismatches and buries the finding. Once exact, a single-satoshi difference becomes *evidence*: the remainder accumulator's reset independently pinpoints a session restart, cross-validating a boundary nothing persists.
- **Fit competing models, don't just check the current one.** Replaying a run under the *superseded* rule as well as the current one turns "looks right" into a measurement: round 4's shared-segment rule was confirmed by the round-3 model producing **zero** exact fits over a 100×100 threshold grid while round 4 reproduced 3,684 bets bet-for-bet.
- Free integrity checks worth running every time: balance continuity (`BalanceAfter[i-1] + NetAmount[i]`), duplicate `BetRecord.Id` (INC-002), win rate in σ, and longest loss run against `log(n)/log(1/p)`.
- **Audit before the developer restarts the app.** Until the player mines their first block the world is pre-genesis, so `ResetToPreGenesisDefaults()` rolls the clock, the balances **and the bet journal** back to the chain tip on every boot — a completed test run is erased by the next launch.

### Scripting tools on this machine — three facts, each of which has cost a cycle

Full detail — the tool-choice table, the allowlist audit, the PowerShell-7 deferral — is
**`Documentation/DEV_ENVIRONMENT.md`**. These three stay here because each is a trap you fall into *before* you
would think to open a doc:

- **There is NO Python. Never write a `.py` file, never call `python`/`pip`.** `python`/`python3` are on PATH and
  are **Microsoft Store app-execution aliases**, so `which python` *succeeds* and every invocation then fails. An
  availability check cannot detect this. Use **`awk`** for CSV traces, **`node -e`** for `user://` JSON,
  **`dotnet run`** on a scratchpad console project when arithmetic must match the game, **PowerShell** for the
  filesystem.
- **PowerShell here is 5.1, not 7.** No `&&`/`||`, no ternary, no `??`; `Set-Content`/`Add-Content` default to
  **ANSI** so appending `—`/`§`/`✅` needs `-Encoding utf8`; and `2>&1` on a native exe wraps stderr in
  `ErrorRecord`s and sets `$?` to `$false` **even on exit 0** — never gate logic on it. **Upgrading is DEFERRED by
  decision, not oversight — do not propose it as a fix** (`PRIVATE_ROADMAP.md` §8 T5).
- **`node -e` is the ONLY open-tail interpreter rule left, so path permissions are ADVISORY, not a boundary.** A
  `Read(…)`/`Edit(…)` rule binds Claude's own file tools, never a subprocess opening paths through its own
  runtime. Reading the surviving read rules and concluding the agent is confined to those trees is wrong. **A real
  boundary has to be OS-level** (the sandbox merged with the Read/Edit *deny* rules), never the allowlist alone.
  ⚠️ And recognise the shape at an approval prompt: an interpreter's inline-program flag followed by `*)` —
  `Bash(node -e ' *)`, `Bash(awk 'BEGIN{ *)` — approves **any program the interpreter accepts**, not the command
  you just ran.

---

## Open Design Questions

- What threshold lets the casino start repaying bank debt (P6)?
- Should minimum wager requirements be weekly, monthly, or both?
- How harsh should fee penalties be for missing minimum wager requirements?
- How much bot betting history should the player see by default?
- Should private mempool fees be available in Basic Mode or postponed?
- **Network fee market simulation — Option A ✅ IMPLEMENTED (Step 14 ND.7, 2026-07-13)**: the historical fee replay is live (`NetworkFeePolicy` consumes the dataset's daily median/mean band from Market Birth — see the Canonical Decisions fee row and the Implemented bullet). **Option B** (a reactive fee market from our own simulated mempool congestion) remains the **future validation experiment**: if it reproduces a curve similar to the replay, it confirms the Step-14 population/volume simulation was built right — and it is where fee CHOICE enters (queue-jumping above the daily base when the 24-tx cap saturates; today no participant has a reason to pay above base, OQ-ND7.1). See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10.6 and `Documentation/PRIVATE_ROADMAP.md` "Network Fee Market Simulation".

---

## Scene Management — index

Full inventory (25 `SceneId`s, every path verified), the navigation map and the `StatusBar` component: **`Documentation/SCENES.md`**.

**Every scene transition goes through `SceneManager`.** All paths live in one place and call sites use a compile-time-safe enum — there are no `ChangeSceneToFile` calls outside the service, and it must stay that way.

**Adding a scene** — three steps in `Scripts/Services/SceneManager.cs`:

1. add the entry to the `SceneId` enum;
2. add its path to the `Paths` dictionary;
3. call `_sceneManager?.Go(SceneManager.SceneId.X)` at the call site.

`Go()` records a one-deep `PreviousScene`, which is what makes **origin-aware back navigation** work for the scenes reachable from more than one hub (`BetsHistoryExplorer`, `CasinoCoinSwaps`): they return to `SceneManager.PreviousScene ?? MainMenu`.

**The `StatusBar` rule that governs new work:** the bar shows Main Balance, Bankroll, the player's BTC wallet, the clock and the BTC price. The two BTC cells are **different kinds of figure** — the wallet is money owned (bitcoin orange `#F7931A`, beside the SC balances), the price is a market quote (default colour, far end). Once a BTC figure sits in the bar every unlabelled number becomes ambiguous, so **do not add a third bare number to it**.

---

## Testing

**Status**: _[Pending — no test framework configured yet. Document test approach here once established.]_

---

## Architecture Documentation

Detailed design documents are in `Documentation/`:

| File | Contents |
|---|---|
| `SCENES.md` | **Scenes & navigation in full** — the 25-id inventory generated from `SceneManager` with every path verified, the rebuilt navigation map, the `StatusBar` component, and a record of three claims the old section made that the code contradicts. Extracted and **rebuilt** from this file at 7,470 characters (Dep-01 D2.3) |
| `ARCHITECTURE.md` | **The core game systems and data models in full** — Dice, betting strategy, sessions, the execution pipeline, blockchain/mining, and the finance/blockchain/history models. Extracted from this file at 16,696 characters (Dep-01 D2.2). This file keeps only the one-line index in **Core Game Systems — index**, plus the three embedded rules that say what *not* to write |
| `DEV_ENVIRONMENT.md` | **The machine this project is built on** — why there is no Python and what to use instead, PowerShell 5.1's traps, the allowlist audit that left `node -e` as the only open-tail interpreter rule, and the PowerShell-7 deferral. Extracted from this file at 5,590 characters (mini-plan 19 D); CLAUDE.md keeps only the three facts that cost a cycle before you would think to open a doc |
| `DESIGN_OVERVIEW.md` | Target design per system with implementation status labels |
| `GLOSSARY.md` | Canonical terminology (source of truth for naming) |
| `PLAYER_GUIDE.md` | What is playable now (updated for each release) |
| `IMPLEMENTATION_STATUS.md` | What shipped, when, and the reasoning behind each step (Steps 7–16). Extracted from this file at 60,780 characters. Most entries end by pointing at the canonical write-up in `ProjectDesignManual.md` or an `AIHelperFiles/` plan — **those win where they disagree.** Its P0–P8 copy is stale and annotated as such; `PRIVATE_ROADMAP.md` owns those priorities |
| `SERVICES.md` | **The autoload services in full** — one section per service: what it owns, its persistence path, its checkpoint/pre-genesis behaviour, and the decisions behind it. Extracted from this file at 48,517 characters, where it was the last oversized block. This file keeps only the one-line index in **Key Architecture — Autoload Services**; access and registration-order rules stay in **Important Patterns §5** |
| `PRIVATE_ROADMAP.md` | Internal priorities P0–P8, canonical decisions, open questions |
| `ProjectDesignManual.md` | The long-form design record — one chapter per system, written as the work lands. **Ch. 29** UI/Godot layout (read before any `ScrollContainer` work; **§29.12** the number-locale audit + its detector) · **Ch. 30** UTXO model · **Ch. 35** timeline guard · **Ch. 36** network population · **Ch. 38** event-driven vs. `_Process` · **Ch. 39** the Central Bank + §39.16's six standing conventions · **Ch. 40** persistence durability & simulation scale (**§40.8** duplicated records vs. streak metrics — read before trusting any figure computed off `BetHistory`) · **Ch. 41** player participation in company governance (pause / policy / abstention) |
| `INCIDENT_LOG.md` | **Significant design crashes** — data-loss/corruption events whose cause is a design limitation, not a typo. One entry per incident: symptom, timeline, proximate vs. root fault, evidence, blast radius, recovery, the phase that fixes it, and the generalized lesson. Add an entry whenever a crash costs a world/playtest or reveals a persisted figure that had been silently wrong. Currently: INC-001 (the 1.13 GB bet journal + truncated world snapshot, 2026-07-29) · INC-002 (the impossible martingale level — duplicated records amplified by a streak metric, 2026-08-06) · INC-003 (two bettors in a journal that belongs to one — the explorer's retired world-clock rewind, found three days after its own fix, 2026-08-19) · **INC-004** (the lifetime rollup that zeroed itself and called it complete — a non-atomic writer feeding a failed load that was written back over the only copy, 2026-08-22) · **INC-005** (the statistics panel that contradicted the balances — `Stats` rebuilt in two scales, a clamp that hid it, and a checkpoint boundary moved by back-dating, 2026-09-13) |
| `REFERRAL_AUCTION.md` | The referral auction's full spec, extracted from this file's Canonical Decisions row when it reached 32,007 characters. **§1 is the current rule** (opening/raise floors, the tracked pool, bot cadence, the exclusion precedence, the three ladder modes, the stuck escalation) — every figure verified against `NetworkRoot.cs` and cited **by symbol** (line numbers until 2026-08-23, when all nine were found to have drifted +10; Standing Convention 15). **§2 is the amendment history** (EB.2 → ND.10l → P16.6), each entry marked still-current or superseded. Read §1 to implement; read §2 only to learn why a rule has its shape |

---

## Git Workflow

- **`main` is the stable trunk.** It is anchored at known-good points (e.g. a completed roadmap step). Keep it buildable.
- **One branch per category of modifications** (e.g. `scheduled-bot-transactions`, `candidate-block-model`, `historical-founders`). Do feature work on its branch; merge back to `main` when stable.
- **STAGE → ASK IN CHAT → COMMIT (2026-08-14).** When a unit of work is finished, Claude **stages** it (`git add -A`) and **posts the full commit message in the chat**, then stops and waits. The developer authorises in the chat; **Claude then runs `git commit`.** Never commit before that authorisation, and never leave an authorised change uncommitted.
  - **Why:** once committed, the change is folded into history and the developer loses the easy "what exactly did you just do?" view. Staging first keeps the diff reviewable while the reasoning is still fresh, which is the moment review is worth anything.
  - **The message goes in the CHAT, not into `.git/COMMIT_EDITMSG`.** That file was tried first and does **not** surface in the VS Code Source Control input, so the developer never saw it — a prepared message nobody can read is the same as no message.
  - Claude still **writes** the message to the usual standard (what changed, why, and the rule it establishes). Only the go-ahead is the developer's.
  - **Before staging code:** `dotnet build` clean, and the locale detector at its baseline (Money Handling). Say in the same message if either was skipped.
  - A clean working tree usually means the developer already committed; verify via recent commit history, don't assume there's work to commit.
- **PLAN LIFECYCLE — four approvals, and the git operations each one authorises (2026-09-21).** A plan (a mini-plan or a step) lives in `AIHelperFiles/` and runs on its own branch. The developer approves in the chat at four points. **Each approval covers the operations listed with it, so none of them is asked for again:**
  1. **Specify.** Claude writes the plan, with predictions and decision rules registered **before** any data, and a `SPECIFIED` entry in `PRIVATE_ROADMAP.md`. It stages both and posts the commit message **with a proposed branch name** (`miniNN-<subject>`). Approval ⇒ `git checkout -b <branch>` off `main`, then commit.
  2. **Each unit of work** is the stage → ask → commit loop above. Approval ⇒ commit on the plan's branch.
  3. **Measurement runs.** Claude gives a numbered protocol that names every setting and the panel to watch, with the prediction stated first. It then reads `user://logs/*.csv` and the journal **itself**, never asking the developer to transcribe output. Results go into the plan, refuted predictions stated as refuted, and are committed like any unit.
  4. **Close-out.** Claude writes the plan's close-out section, moves the roadmap entry to `DONE`, adds the `IMPLEMENTATION_STATUS.md` entry and marks figures it superseded elsewhere. It stages all of it and posts **both** the close-out commit message and the merge message. Approval ⇒ commit, `git checkout main`, `git merge --no-ff <branch>`, `dotnet build` on `main`, then **`git push origin main`**. That push is a standing authorisation (developer, 2026-09-20).
  - **Keep the plan's `Status:` line current** in every commit that moves it (what is done, what is next). It is how a new session resumes: open the plan named in the roadmap's `SPECIFIED` / `IN PROGRESS` entry, read its Status and its "Order" section, and continue from there.
  - **Still explicit-request only:** any other push (a direct commit on `main`, a feature branch), force-push, rebase, deleting a branch, and a merge not approved through step 4.
  - **Mechanics that have already failed once:**
    - `git merge` cannot read its message from stdin: `-F -` fails with `could not read file '-'`. Write the message to a file in the scratchpad (`mkdir -p` it first) and pass `-F <file>`. `git commit -F -` with a quoted heredoc does work.
    - Run multi-step git sequences under `set -e -o pipefail`, and never pipe `git merge` into `tail`/`head`. A pipe once hid a failed merge, and the push after it said "Everything up-to-date" as if it had worked.
- **Keep docs current on the branch where the work happens — including CLAUDE.md.** When a change alters the architecture, update CLAUDE.md (and the other docs) in the same branch/commits as the change, not deferred to merge. CLAUDE.md stays tracked — do not untrack it (its history matters and Claude Code reads it every session).
