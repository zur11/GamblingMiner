# GamblingMiner — CLAUDE.md

## Project Overview

**GamblingMiner** is an experimental Godot 4.5.1 / C# prototype that simulates early Bitcoin history combined with a casino betting system. The core mechanic: **time only advances when bets are placed, and each bet simultaneously performs one mining nonce attempt**.

- **Engine**: Godot 4.5.1 (.NET / C#)
- **Target framework**: .NET 8.0
- **Primary platform**: Windows
- **Save format**: Local Godot `user://` data (JSON)
- **Starting condition**: Player begins on **January 3, 2009** with **40,000 SC** total funds
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
| Target | **60,000 characters** |
| Warning | **100,000** — on crossing it *while writing*, say so in that same reply and propose what to extract |
| Hard limit | **150,000** — where Claude Code reports the file's size at startup |

**While a depuration plan is actively running, the 100k warning is suspended and only the hard limit applies.** A warning exists to surface an *unnoticed* condition; during a plan whose whole subject is that condition, it is noticed, and repeating it at every step is noise that trains the reader to skip it. The warning resumes when the plan closes — by which point the file should be under target anyway.

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

Hard-won rules (a scroll bug once cost a full session — full write-up + diagnostics in `Documentation/ProjectDesignManual.md` Chapter 29):

- **A panel scrolls only if it has a BOUNDED height smaller than its content.** The reliable bounding chain is `MarginContainer` (fills the screen via `anchors_preset = 15`) → `VBoxContainer` → the scroll element with `size_flags_vertical = Fill+Expand (3)`. A container that isn't itself height-bounded can't bound its children.
- **Pick ONE of two scroll patterns deliberately — never mix them:**
  1. **`ScrollContainer` wrapping the content** — for a column of many controls (Labels/Buttons/inputs, or `RichTextLabel`s **with an explicit `custom_minimum_size`**). Used by `FoundersWallets`, `BotsBtcWallets`.
  2. **A single `RichTextLabel` with `scroll_active = true` + `fit_content = false`** (bounded height) — for one big block of dynamic BBCode text. Used by `BlockExplorer`'s right column.
- **NEVER put a `fit_content = true` `RichTextLabel` inside a `ScrollContainer` expecting it to scroll.** `fit_content`'s reported minimum height is unreliable inside containers, so the `ScrollContainer` never learns the content overflows. This is the #1 time-sink.
- **`HSplitContainer` does not reliably bound/report content height inside a scroll — use `HBoxContainer`** for two columns that must scroll.
- **Mouse wheel + `mouse_filter`:** with pattern (1), the wheel reaches the `ScrollContainer` only if every control in the chain from the hovered node up to it has `mouse_filter = PASS (1)` (default is `STOP`, which eats the wheel). A big label filling the panel will swallow the wheel — set it to `PASS`, or use pattern (2) where the label scrolls its own wheel.
- **The last line sits flush against the scroll's bottom edge** (`scroll_active` max = content height). Append a few trailing blank lines (`"\n\n\n"`) so the final real line clears the edge and isn't half-clipped.
- **Persistent nav / Back buttons go in a FIXED FOOTER, OUTSIDE the scroll** — never as the last child inside the `ScrollContainer` (there they overflow/clip at the bottom, clickable but unreadable). Structure: `MarginContainer → VBoxContainer (bounded) → { ScrollContainer(size_flags_vertical=3) for the content, THEN the nav/Back row as a sibling footer }`. `ScTransactions` is the reference; `ScFinances` was fixed to match (§29.10). Reparenting nodes between the scroll and the footer is safe — controllers resolve widgets by `%UniqueName`, which is path-independent.
- **The bottom ~50 px of the 1080 canvas can be OFF-SCREEN** in a plain/embedded window (the editor's Game view runs Windowed) — this, not the scroll, was the real cause of the Step 12 "Back button overflows" bug (it happened even with no active scroll). Keep must-read/must-click controls out of that band: give the page's `MarginContainer` a `margin_bottom ≥ ~50`, and remember an expanding child (`size_flags_vertical = 3`) **pins the following footer to the very bottom edge**, straight into the danger band. The exported build starts Maximized (`project.godot window/size/mode=2`, with `window/size/mode.editor=0` so editor embedding still works). Full write-up: §29.11.
- **Setting `RichTextLabel.Text` resets its internal scroll to the top.** On a timer-refreshed panel, save `GetVScrollBar().Value` before setting `Text` and restore it after.
- **Diagnose with numbers, never guess.** If a panel won't scroll, print `GetVScrollBar()` `MaxValue`/`Page`/`Value`, `Size`, `GetContentHeight()`, and whether the data is even present — before restructuring. Add a visible canary (e.g. a title marker) to confirm the scene actually reloaded the edited `.tscn` (C# always rebuilds; external `.tscn` edits need a scene reload in the editor).
- **Block Explorer display filter (OQ-8.2 cosmetic, `BlockExplorer.cs`) — ✅ DELETED at Step 16 P16.2f (2026-07-30).** `IsSelfChangeTransaction` / `ExternalOutputs` no longer exist: every spending participant carries a `DerivedAddressWallet`, so there is no change-to-self shape left to hide, and the explorer shows blocks exactly as they are on-chain. If a new participant ever appears without a seed, **give it one — do not reintroduce a filter that makes a real spend's arithmetic fail to add up.** History + the removal check's finding: `Documentation/ProjectDesignManual.md` §29.9.

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
- **Locale sweep — ✅ project-wide audit done 2026-08-06 (Step 16 post-merge).** The rule above had been in force for a long time and was still violated in **~40 sites**, found only because the developer noticed `0,00000000` in the Block Explorer's Network Status panel. Every one is fixed and the build is clean; the value of this bullet is the **recipe**, because the next violation will arrive the same way — written by hand, invisible on an English dev machine, spotted by eye on a Spanish one. Full write-up: `Documentation/ProjectDesignManual.md` **§29.12**.
  - **Detector (run it before believing the project is clean).** From Git Bash at the repo root — flags an interpolated numeric format specifier with no `InvariantCulture` within the preceding 3 lines (the window that catches multi-line `string.Create(...)` wrappers). A handful of continuation lines of an already-wrapped expression come back as false positives; read the surrounding lines before editing:
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
    **Baseline as of 2026-08-06: pass 1 returns exactly 5 lines, pass 2 returns 0.** The 5 are all continuation lines of an already-wrapped multi-line expression (`FoundersWallets.cs` 247–248, `NetworkRoot.cs` 6918 / 7475–7476) — a 6th hit is a real regression.
  - **Four shapes carry the bug** — the last two are the ones that keep getting missed: (1) `$"{v:F8}"` unwrapped; (2) `v.ToString("F8")` with no culture; (3) a **nested** `$"…"` inside an interpolation hole — it does **not** inherit the outer handler's provider, so wrapping the outer string fixes nothing (`CasinoCoinSwapService`'s two startup lines had to be restructured into helpers); (4) `decimal + "%"` string concatenation, which calls the culture-sensitive `ToString()`.
  - **A FIFTH shape, on the TEXT side, that neither detector above can see (2026-08-14):** culture-sensitive **date names**. `CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(...)` rendered the Calendar Navigator's month as `Month: mayo (31 days)` on a Spanish machine — half a sentence in each language, and invisible on an English dev box exactly like the numeric cases. Both detectors scan for *numeric* format specifiers, so they were structurally incapable of finding it. Extra passes: `grep -rn "CultureInfo.CurrentCulture" --include=*.cs .` (**baseline: 0 hits**, the one occurrence is fixed) and `grep -rnE '\.ToString\("(ddd|dddd|MMM|MMMM)[^"]*"\)' --include=*.cs . | grep -v InvariantCulture` (**baseline: 0**). *A locale audit scoped to numbers will pass a project whose month names are in the wrong language.*
  - **Fix shape:** wrap in `string.Create(CultureInfo.InvariantCulture, $"…")` — it accepts a chain of `+`-concatenated interpolated strings as ONE handler, so a multi-line display block needs only the wrapper plus a closing paren (turn any trailing bare `"literal"` operand into `$"literal"` so the chain stays all-interpolated). Add `using System.Globalization;` — most files still lack it.
  - **Scope confirmed clean, do not re-audit:** all CSV telemetry writers already use `string.Format(CultureInfo.InvariantCulture, …)`, so no trace file was ever affected. **Known residual risk, deliberately not chased:** a *bare* hole `$"{someDecimal}"` (no format specifier) is also culture-sensitive; there are thousands of holes and no way to tell which are decimals without type analysis, so these are fixed **reactively** — when a comma shows up in a panel, that is the likely shape.
  - **Where they actually hid:** the `Money.*` helpers were always correct, so the violations clustered where a value is formatted *ad hoc* for display — Block Explorer / wallet panels, `NetworkRoot`'s string builders, and `GD.Print` diagnostics. When adding a readout, reach for `Money.FormatSignedAdaptive()` first; it needs no wrapper.
  - ⚠️ **Do not "fix" these with a bulk regex.** A batch `perl -pi -e` pass during this audit silently blanked 24 lines: the capture groups `$1/$2/$3` were reset by the *later* condition regexes in the same statement, so the replacement wrote `GD.Print(string.Create(CultureInfo.InvariantCulture, $""));`. It was caught and restored from `HEAD`, but a build alone would not have caught it (`$""` compiles). Edit these by hand, or capture into lexical variables **immediately** after the match and diff every line before building.

### Time

- `DateTime.Utc` for storage, persistence, and internal comparisons
- `DateTime.Local` for player-facing display (game time starts `2009-01-03 18:15:06 Local`)
- Unix **milliseconds** for blockchain timestamps
- Game-time scale: **1 bet tick = 100 in-game seconds**; autobet target: **10 real minutes = 16h 40m in-game**

### JSON Persistence

- All `user://` files use JSON with **CamelCase** naming policy
- History files are chunked by month to keep file sizes manageable
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
| 2 | `UserStatsService` | The player's betting stats (`Stats`) and the persistent bet journal (`BetHistory`, chunked by month); emits `StatsChanged` on a 250 ms throttle |
| 3 | `CalendarTimeService` | The game clock — local/UTC game time, `SpeedMultiplier`, the DEV `DevTimeScale`, and `SimulationThrottle` (the fraction of simulated time the bet engine actually retained) |
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

## Core Game Systems

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

## Data Models

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

---

## File Organization

```
GamblingMiner/
├── Documentation/              # Design docs (English only)
│   ├── DESIGN_OVERVIEW.md      # Target design with implementation status labels
│   ├── GLOSSARY.md             # Canonical terminology
│   ├── PLAYER_GUIDE.md         # What is actually playable now
│   ├── IMPLEMENTATION_STATUS.md # What shipped, when, and the decisions behind it
│   ├── SERVICES.md             # The 19 autoloads in full — one section per service
│   ├── PRIVATE_ROADMAP.md      # Internal priorities P0–P8
│   ├── ProjectDesignManual.md  # Long-form design record — one chapter per system
│   ├── INCIDENT_LOG.md         # Design crashes: data-loss/corruption post-mortems
│   └── REFERRAL_AUCTION.md     # Referral auction spec + its amendment history
│
├── Screens/                    # UI scenes + screen controllers
│   ├── DiceGame/               # Main game loop (ManualBet, AutoBet, strategy selector)
│   ├── BlockExplorer/          # Blockchain inspector
│   ├── BankrollProgrammer/     # Main Balance ↔ Bankroll UI
│   ├── BetsHistoryExplorer/    # Historical stats browser
│   ├── CalendarsNavigator/     # Time-based history browsing
│   ├── MartingaleCalculator/   # Strategy planner
│   ├── ScFinances/             # Player SC-flows hub + ScTransactions (Step 12)
│   ├── CasinoGamblingFinances/ # Casino SC finances [DEV] + ClientsBetsHistory/ClientsTransactions
│   ├── CasinoCoinSwaps/        # Casino swap desk — SC↔BTC (Step 13)
│   ├── AuctioningCompanyDetails/ # Per-non-miner live tracked-donation pool while InAuction (Step 14 ND.5; forwards to CompanyDetails once founded)
│   ├── CompanyDetails/         # Founded company: stock summary + Board Vote / dividend panels (Step 14 ND.8b.4);
│   │                           #   Vote Policy + abstention + pause locator (Step 16 P16.5/P16.8, ProjectDesignManual Ch. 41)
│   ├── CompaniesWallets/       # The 40 companies' BTC wallets [DEV] (Step 16 P16.3b — split out of BotsBtcWallets)
│   ├── CastMinerWallets/       # The Step-14 historical cast's BTC wallets [DEV] (Step 16 P16.3c — previously unlisted)
│   ├── WorldEconomy/           # SC Monetary Ledger readout + company inflow/expansion knobs [DEV] (Step 14 ND.8c/ND.8b.6)
│   ├── CentralBank/            # Central Bank (FED) per-client accounts + monetary invariant [DEV] (Step 15 P15.1e)
│   └── Shared/                 # Reusable UI components
│
├── Scripts/                    # Core logic (~50 C# files)
│   ├── Services/               # Autoload singletons (19 services)
│   ├── Betting/                # Strategy config, interface, progression logic
│   ├── Sessions/               # Bet loop controllers (Base, Auto, Manual)
│   ├── Dice/                   # DiceEngine, DiceResult
│   ├── Finance/                # Wallet, Money, Transaction, BetTransactionEvent
│   ├── Game/                   # BetService, IBetEventSource
│   ├── History/                # BetHistoryRepository, BetRecord, stats, PlayerFinancialStatsCalculator
│   ├── BlockchainPort/
│   │   ├── Blockchain/         # BlockchainService, Models, CryptoUtils
│   │   └── Simulation/         # NodeAgent, NetworkSimulator
│   ├── Calendars/              # CalendarModel, GregorianCalendarModel
│   ├── StateMachines/          # AutoBetSessionStateMachine
│   ├── Controllers/            # WalletController
│   └── User/                   # UserBettingStats, UserBetRecord
│
├── UI/                         # Reusable UI component scripts
│   ├── StrategyControlPanel/
│   ├── FinancialBettingStats/  # Compact 3-scope betting stats (redesigned Step 12); reused in DiceGame + ScFinances
│   └── StatusBar/              # (DepositPopup/ retired in Step 12 — deposits now flow through ScFinances)
│
├── GamblingMiner.csproj        # .NET 8.0, Godot.NET.Sdk 4.5.1
├── GamblingMiner.sln
├── Main.cs / Main.tscn
├── project.godot
└── CLAUDE.md
```

---

## Canonical Decisions

These values are fixed and must be consistent across all docs, UI, and code:

| Decision | Canonical Value |
|---|---|
| General initial balance | `40,000 SC` |
| Specific split | `39,900 SC Main Balance + 100 SC Bankroll` (unchanged by Step 12 — the `40,000` stays in the casino accounts, funded as today) |
| Private Bank Account (Step 12) | Starts at `0` — an **optional SC reserve outside the casino**, all automation OFF by default. The player *owns* it (no debt); withdraw Main→Bank to park SC safe, deposit Bank→Main to bring it back. Managed in `ScFinances`. See `PlayerBankAccountService` |
| Player-facing term | `Main Balance` (not "Principal Balance") |
| Game over condition | `Private Bank Account + Main Balance + Bankroll = 0` (Step 12 / D-SF2.1 — total ruin across all three SC accounts; while the bank holds anything it is **not** game over, since the player can always deposit it back). Written to leave room for a future **BTC→SC coin-swap escape hatch** (§7.4) — the check must be interceptable by a later exchange layer, not an irreversible terminal |
| Current mining rule | `1 bet = 1 nonce attempt` |
| Basic Mode halving | `2,100 blocks` (≈ 4 in-game years at 100X scale) |
| Total BTC supply | `210,000 BTC` — converges to in-game year ~2141 |
| Real Bitcoin halving | `210,000 blocks` — NOT used in Basic Mode |
| Block transaction cap | `24 transactions` ✅ **Implemented** — `BlockTemplateBuilder.MaxBlockTransactions = 24`, counting the coinbase (coinbase + up to 23 mempool txs). Fits the 1:100 fractal replica (real blocks carry ~2,000–3,000 txs) |
| BTC/SC trading unlock (Step 13) | **2010-07-18** (Mt. Gox launch — the first date of `Data/HistoricalPrices/btc_usd_daily_2010_2025.csv`). Before that in-game date there is no market, no price, and no swap UI beyond a locked teaser. The gate is **data-driven** (`BtcMarketDataService.FirstDataDateLocal`), never a second hardcoded date. In the canonical timeline the player waits ~484 in-game days (~715 blocks) from the 21 Mar 2009 start — accepted and historically honest (BTC accumulates from mining long before it becomes tradable) |
| Timeline (DEV alt-timeline guard, Step 13) | The canonical timeline (genesis `2009-01-03`) is the ONLY shipping timeline: `TimelineConfig.DevAltTimeline` is **`false` on `main`, forever**. All historical date anchors route through `TimelineConfig.Shift()`; `user://world_timeline.stamp` + `NetworkRoot.ResetWorldIfIncompatible()` auto-wipe the world (full delete list, D-13.7) on any timeline switch, both directions — run by `WorldGuardService`, the **first** autoload, so the wipe precedes every state-file load (ordering is load-bearing; new persisted world-state files MUST be added to the delete list). The DEV simulacrum (+484 days, landing 2010-07-18) is a throwaway dev scaffold — re-mount guide: `Documentation/ProjectDesignManual.md` Ch. 35 |
| Hardware cap | `100 nonce attempts` per time cycle (planned) |
| Network fees — Historical Fee Replay (Step 14 ND.7, 2026-07-13 — **supersedes Step 10's flat 2009-04-26 era, which is retired**) | **The fee era begins at Market Birth (2010-07-18), data-driven** — no fee exists anywhere on the network before it; from it the **real daily historical band** governs, replayed from the network dataset's `fee_median_btc`/`fee_total_btc` (fees are fractal-exempt — face value, never /100). Per day: **median** = every participant's base fee (send panels default-fill it; player clamp band `[median, max]`), **mean** = `fee_total ÷ tx_count`, paid ONLY by the cast miners' sell-flow (they ARE the network's average activity), **max** = `max(median, mean) × MaxFeeMeanMultiplier (10)` — a documented approximation, no real daily-max metric exists. Each component carries forward its last positive value across zero/blank days (D-ND7.4); the median has NO positive value before **2011-04-14 (= 0.01 BTC)**, so the effective median is an honest **0** from Market Birth through 2011-04-13 (most txs genuinely paid no fee then — the swap desk opens into a ~9-month zero-network-fee era, deliberate). `NetworkFeePolicy` stays the single source of truth: a pure static class holding the schedule pushed by `BtcNetworkDataService.EnsureLoaded()` (`SetFeeSchedule` — armed for EB.1 throwaway bootstrap instances too); no schedule ⇒ fee-free fallback + one warning, never the 0.1 scaffold back. The legacy `DefaultFee/MinFee/MaxFee` consts, `NetworkRoot.MinBotFeeBtc/MaxBotFeeBtc/CasinoTxFee`, and `TimelineConfig.FeeActivationLocal` (D-13.9, absorbed) are **deleted**. Fee semantics are world-defining ⇒ `WorldFormatVersion` 2 → 3 (clean reset). Median column provenance: Blockchair true medians (2010-07-18 → 2011-04-13) + BitInfoCharts USD median ÷ price (2011-04-14 →) — Coin Metrics' `FeeMedNtv` is paid-tier. See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10 |
| RTP | `99.02%` |
| Number format | `1,000,000.00000000` — comma=thousands, period=decimal (`CultureInfo.InvariantCulture`); never use raw `:N8`/`:F2` in string interpolations |
| Currency for betting | SC only — BTC cannot be wagered directly |
| SC value (D-ND8.30, Step 14 ND.8c) | **The 1:1 USD peg is canon.** The economy simulates SC *quantity and credit* — never *value*: monetary tightening is expressed as credit scarcity (who can borrow, and how much), never as inflation or peg drift. **Option C of the fiat-debt ladder — inflation/devaluation — is rejected forever**; Option 0 (`ScMonetaryLedgerService`) and Option A (`CentralBankService`) are built, Option B (full fractional-reserve) is documented post-Basic-Mode. A price that moves is always a BTC price, never an SC one |
| Casino SC defaults (mirror of an average player) | Casino auto-loan chunk `40,000 SC` (`= InitialLoanAmount`, a player's total start) + bankroll dose `100 SC` (`= DefaultBankroll`, a player's Bankroll). Extra-lazy first funding then reproduces the player's `39,900 Main / 100 Bankroll` split. Dev-configurable (`AutoLoanAmount` / `BankrollTarget`), reverts to these defaults pre-genesis. (CG.3.D) |
| Founders | Satoshi (target `11,000 BTC`, retires ≥ `2011-04-26`, then frozen) + Hal (`P=1.0` drip, fades to 0 by `2009-08-09`) + Mike Hearn (joins ~Apr 2009, never mines, +82.51 BTC round-trip) |
| Referral auction | 40 non-miner companies, ascending BTC auction, rolling 20-day window. **A win is permanent** (D-ND4b.12). Spec: `Documentation/REFERRAL_AUCTION.md` |
| Player start | `21 Mar 2009` after the first-launch bootstrap, at the **exact same timestamp** as the bootstrap's last mined block (`HistoricalBootstrapService`) — no dead/idle time, no offset. This is a specific case of a general rule: **the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world state.** Every checkpoint capture (`BlockSessionCheckpointService.CaptureCheckpoint`) reads the clock synchronously right after mining, so this holds automatically post-first-block; pre-genesis, `BlockSessionCheckpointService.ResetToPreGenesisDefaults()` re-derives it the same way from the chain tip. See `Documentation/ProjectDesignManual.md` §24.9 |
| Swap desk fee (Step 13; network-fee component replaced at ND.7) | `10%` default, dev-clamped `1%–10%` (`CasinoGamblingFinances`), governing **both** swap directions. **Additive** (D-SW.11, 2026-07-08): `totalFee = networkFee + casinoFee` where `casinoFee = fee×(base+networkFee)` — the network fee is a SEPARATE charge summed with the casino's cut, not absorbed inside it. Since ND.7 (D-ND7.9) `networkFee` is **the day's replayed median for the current game date** (`CasinoCoinSwapService.CurrentNetworkFeeBtc`), not the retired flat 0.1 — the min-swap size and panel thresholds scale with it live (0 in the 2010-07→2011-04 zero-median era). **Max fee deviation cap** (D-SW.12): `MaxFeeDeviationPoints` (default `2.0`, clamped `[0,20]` points) caps the casino's cut alone at `nominal+points`% effective margin — the network fee is never capped, always charged in full. Bought BTC always lands on the player's **base address** (no fresh-address-per-swap, D-SW.6). See `CasinoCoinSwapService` and `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` |

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

**Within a session**, the live clock, balances, and mempool advance and survive scene changes — the autoloads and the **static** `NetworkRoot` hold them in memory. **Nothing between blocks is persisted to disk** — not SC balances, not the chain, not the mempool. Between-block navigation / node-switch saves use `SaveActiveNodeFinancialState(false)` (in-memory only), and BTC transactions / consensus do not persist either (`NetworkRoot.CreateAndBroadcastTransaction`/`CreateAndBroadcastTransactionToAddress` only mutate the in-memory mempool). `PersistStateToDisk()` runs **only** at block-mining (`HandleMinedBlock`), baseline node creation, and startup; the player's block-commit financial write goes through `SimulationService.CaptureCheckpoint` / `DiceGame.CaptureBlockCheckpoint`. Consequently an **app restart reverts the whole world to the last mined block** — clock, every participant's balances, **and** un-mined pending transactions — performed at startup by `BlockSessionCheckpointService.ApplyCheckpointToServices()`. Within-session re-entry must never rewind the clock: `DiceGame` skips `EnsureGameEpochInitialized()` while `SimulationService.IsRunning`, and the checkpoint clock/history restore is a once-per-process operation guarded by the static `_checkpointRestoreSpentThisSession`.

**This principle applies to EVERY player-facing persisted value, not just the four services `ApplyCheckpointToServices()` lists** — `BankrollProgramService` (dose + transfer records), the game clock, and the bet-history log (`UserStatsService`) all self-persist eagerly (on every dose change / bet / recharge) and MUST be explicitly included in both the post-first-block checkpoint restore and the pre-genesis reset below, or they silently leak uncommitted state across a restart. When adding a new player-facing autoload or persisted list, ask: "does this need a `BlockSessionCheckpointService` restore path (post-block) AND a `ResetToPreGenesisDefaults()` path (pre-block)?" — if it holds player state that changes outside of a mined block, the answer is yes. **And a third question (TL.3 lesson): "is its `user://` file in the `NetworkRoot.ResetWorldIfIncompatible()` delete list?"** — every persisted **world-state** file must be, or it leaks across a format/timeline clean reset (`casino_coin_swap_state.json` missed this and alt-world hardware/pool state survived a timeline wipe). Identity/personal files (wallet seeds, bot registry, notepad, saved strategies) are deliberately exempt.

**Pre-genesis (no block has EVER been mined — only the historical bootstrap has run)**: a checkpoint is captured **only** by a real block-mined event now (`DiceGame.CaptureBlockCheckpoint()` / `SimulationService.CaptureCheckpoint()`) — never merely by opening the app (there is no more "baseline" auto-capture). Whenever `BlockSessionCheckpointService.HasCheckpoint()` is false, `ResetToPreGenesisDefaults()` runs on every boot instead of `ApplyCheckpointToServices()`, forcing Main Balance/Bankroll/dose/transfer records back to their true canonical defaults, and resetting the calendar + bet history to the historical bootstrap's landing instant (re-derived from the chain tip via `NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()` — before any real block, the tip *is* the bootstrap's last block, so nothing extra needs to be persisted for this). **Canonical rule**: the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world — never offset, not even by one second (every checkpoint capture reads the clock synchronously right after mining, so this is naturally true post-first-block; the pre-genesis reset and the historical bootstrap's player-start instant both follow the same rule deliberately). See the Canonical Decisions table above ("Player start") and `Documentation/ProjectDesignManual.md` §24.9.

**Canonical rule — game time, never wall-clock, for anything the player can see or that gets persisted.** Every event timestamp that is displayed, stored in a `TransferRecord`/`LoanRecord`/`BetRecord`/ledger entry, or compared against a checkpoint boundary **must** come from `CalendarTimeService` (`.CurrentUtcDateTime` / `.CurrentLocalDateTime`) — **never** `DateTime.Now`/`DateTime.UtcNow` directly. An audit (2026-07-01, OQ-BP.10 in `AIHelperFiles/player-and-casino-bankroll-programmer-plan.md`) found this violated in several places already shipped earlier in the same plan — most seriously, `DiceGame`'s `BetService` timestamp provider used `DateTime.UtcNow` for **every manual bet**, which (since `RollbackHistoryToUtc`/`GetLoadedHistoryStats` compare bet timestamps against the game-time checkpoint boundary) would have silently corrupted the pre-genesis history-rollback fixes above for manual play. All such call sites were fixed to read `CalendarTimeService` (with a `?? DateTime.UtcNow` null-safety fallback only, never as the primary source). **The only legitimate use of real wall-clock time** is pure internal DEV/file bookkeeping metadata the player never sees (e.g. `BlockSessionCheckpointService.CapturedAtUtc`, each service's own `UpdatedAtUtc` snapshot field) or genuine real-time concerns unrelated to game-world state (`UserStatsService`'s 250ms UI-throttle timer, `DiceGame`'s real-bets-per-second rate-measurement fields). When adding any new timestamped record, ask: "is this game-world state, or pure DEV telemetry?" — if the player could ever see it, it's game time.

Full rationale and the bugs this resolved: `Documentation/ProjectDesignManual.md` §24.8 (post-first-block), §24.9 (pre-genesis + the exact-timestamp rule), and §24.10 (the wall-clock-vs-game-time audit).

**⚠️ This rule governs commit TIMING, not commit DURABILITY — they are separate problems (INC-001, 2026-07-29).** "A block is the only commit" says *when* to write and is silent on what a half-written file means. A force-close during a block commit left `blockchain/state.json` truncated 7 bytes short of valid; `TryLoadSnapshot` had no `try`, so the `JsonException` escaped `EnsureInitialized` and **every subsequent launch produced an empty world with nothing printed in the log** — chain, wallets and explorer blank while the money services (own files) restored perfectly. The good file survived only because the throw landed before any writer ran. When persisting player-owned state, answer all three: **is the write atomic** (write `.tmp` → flush → rename, never truncate-and-stream), **does a corrupt read fail loudly** (a `Try` prefix is a promise — honour it or drop it), and **can a failed load ever be persisted back over the good copy** (guard the writer, not just the reader). The same incident exposed a scale limitation — several subsystems encode the hand-play premise (~585 bets/block) as an invariant rather than a tuning parameter, and do not degrade when the background simulator runs them at 9000X: the bet journal reached **1.13 GB / ~5.3M records loaded per boot**, with no retention policy anywhere, and its lifetime stats had been silently double-counting. Full statement: `Documentation/ProjectDesignManual.md` **Chapter 40** (limits + the durability rules) and §39.16 rule 7; forensic record: `Documentation/INCIDENT_LOG.md` INC-001; fix: `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` **P15.11**.

**⚠️ Its sequel — fixing the WRITER is half a fix (INC-002, 2026-08-06, `Documentation/ProjectDesignManual.md` §40.8).** P15.11 closed the duplication source and INC-001 recorded that the lifetime stats had been inflated by it — then stopped, without asking **which readers consume that input**. The reader that mattered was `BetsHistoryExplorer`'s max-loss-streak figure, reported by the developer as showing **>100 consecutive losses at 50% chance** (probability 2⁻¹⁰⁰). Duplicated records do not merely scale a streak the way they scale a sum: bet timestamps **collide heavily** (~3.1 bets share each `CurrentUtcDateTime`, since the calendar advances once per frame while the simulator settles many bets in it) and `OrderBy` is **stable**, so the copies land **adjacent** and the streak **multiplies** — measured 12 → 36 on the archived journal. Verified read-only over 1,081,554 real bets at `Chance=50`: win rate **0.5001**, true max run **19** (theory `log₂n ≈ 20`) — the engine was never at fault. Three fixes shipped together: **(1)** `BetHistoryRepository` deduplicates by `BetRecord.Id` (a Guid written on every journal line since the journal existed and, until now, **read by nothing**) at the loader, the legacy loader and live `Add`, reporting skips loudly; **(2)** the metric is segmented per `(GameId, Chance)`, no longer adds the closing win, and is renamed **"Max consecutive losses"** — it was never a martingale level, since the Insist switches (then a single `InsistAfterStop`) / the bankroll-limit reset / auto-recharge all reset the progression while the run kept counting; **(3)** `AssertLossRunIsPlausible` (`[Conditional("DEBUG")]`) tripwires above `log(n)/log(1/p) + 12`. **Two standing rules: when an incident names a corrupted input, enumerate its consumers and harden the one that DISTORTS the error most (a sum hides it, a streak broadcasts it) — and where a displayed figure has a cheap closed-form bound, assert it, or its only detector is a human being surprised.** A label is also a claim about semantics, and gets audited far less often than the arithmetic under it.

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

Two consequences for new work: a service needing one declared *earlier* than itself resolves it **lazily, never in `_Ready`** (`CasinoScBalanceService` resolves the FED this way); and adding an autoload means choosing its position deliberately and saying why — appending to the end of the block is a decision, not a default.

### 6. Prefer Event-Driven Design Over `_Process` Polling

**This is a standing project-wide design principle, not a one-off — apply it to every new system, and treat it as a checklist item before ANY code review is considered done.** `_Process(double delta)` runs every rendered frame. Reaching for it by default is the single most common way to smuggle needless per-frame CPU work into a project whose core loop (bet → nonce attempt → time tick) is already discrete and event-shaped from the ground up.

**The rule:** before writing `_Process`, ask *"does this genuinely need to know about the passage of REAL time, every frame?"*

- **Yes** → advancing a real-time clock, an animation, a UI countdown against wall-clock delta. `_Process` is correct and necessary. Examples already in this codebase: `CalendarTimeService` (advances the game clock by real delta × speed multiplier — nothing else could drive it), `SimulationService` (the background sim's per-tick bet/mining loop), `DiceGame.TickAutoBet` (autobet pacing/animation).
- **No, it only re-reads STATE that changes on a discrete event** (a bet settled, a block mined, a transfer completed, a claim was pressed) → **this is the polling anti-pattern.** The state owner (a service) should fire a typed `event Action<T>` at the exact point the state changes (Pattern 1 above); the consumer (usually a UI scene) should subscribe in `_Ready()` and unsubscribe in `_ExitTree()`, and stop polling entirely.

**The hybrid middle case — a cheap edge-trigger inside `_Process`.** Sometimes the STATE only changes on a boundary that isn't itself a discrete game event (a calendar day rolling over). `BtcMarketDataService`/`BtcNetworkDataService` are the reference pattern: `_Process` does the *cheapest possible* single date comparison against the game clock every frame, and fires `MarketDayChanged`/`NetworkDayChanged` **only** when a day boundary is actually crossed — no timers, no per-frame parsing, no I/O most frames. If you must poll something inside `_Process`, this is the shape: the per-frame cost should be one flag/value comparison, and the real work (rebuilding a panel, hitting disk) belongs behind the resulting edge, never inside the poll itself.

**A signal doesn't have to be a Godot/C# event — an in-memory flag with edge-triggered updates is the same idea.** ND.8d round 3's stuck-bidder-escalation fix (`NetworkRoot._stuckBidderSignatures`, 2026-07-21) is the freshest example: rather than replaying bid history every roll (expensive, and still not `_Process`-shaped) or polling anything per-frame, it stores a small `(signature, sinceBlockIndex)` per (company, bot) — updated once, exactly when the signature actually changes, inside the SAME block-mined event that already drives the whole bidding cascade. No new persisted state, no per-frame cost, no history replay. When a "since when has X been true" question comes up, reach for an edge-triggered signal like this before reaching for either a poll or a full replay.

**But such a cache is EMPTY AND LYING at process start, and the fix depends on whether the predicate has memory.** ND.10j (§22.18) established the first half: *any in-memory cache a per-block sweep owns has a window at start where it is empty; if a reader can predict the sweep cheaply, it must.* Step 16 P16.6 (2026-07-31, §22.20) found the second half the hard way — **that rule was already written and got violated again**, because ND.10j applied it to the *reader* of `_botsRestingOnReserve` and not to the cache's own *memory*. The bot reserve guard is a **hysteresis** (rest at ≤ 200 BTC, resume only at ≥ 300), so between the thresholds the answer depends on how the bot arrived, and no reading of today's balance can recover it. Every restart silently resolved that ambiguity as "not resting": `bot_4` peaked at 250, never reached 300, and — after a rebuild wiped the set — took the **leading bid in six auctions** it should never have entered. Fixed by `EnsureReserveGuardSeeded`, one chain replay per process (derived, not persisted — no snapshot field, no bump), plus a launch line naming each bot's state and a `[Conditional("DEBUG")]` tripwire at the bid broadcast. **Rule: predicting a sweep from the current value only works for a MEMORYLESS predicate. A predicate with hysteresis has to be REPLAYED.** Corollary worth keeping: a drift filed as "harmless and self-correcting" stops being harmless the moment the mechanism it feeds decides ownership rather than pacing — re-read those judgements when a system's stakes change.

**Already-good examples in this codebase (services firing typed events on real state changes):** `UserStatsService.StatsChanged` (throttled 250ms — the reference pattern for a HIGH-FREQUENCY event, `EmitStatsChangedIfNeeded()`), `SimulationService.ClientBetSettled`, `CasinoClientLedgerService.LedgerChanged` / `ScMonetaryLedgerService.LedgerChanged`, `PrincipalBalanceService.BalanceChanged` / `CasinoScBalanceService.BalanceChanged`, `PlayerBankAccountService.BankStateChanged`, `CasinoCoinSwapService.SwapDeskChanged`, `BtcMarketDataService.MarketDayChanged`, `BtcNetworkDataService.NetworkDayChanged`.

**Known migration candidates (audited 2026-07-21, none fixed yet — this is the backlog, not a mandate to stop and fix them now):** roughly fifteen scenes poll on a `RefreshInterval`/`FallbackInterval` timer purely to rebuild a panel from service state that only actually changes on a settled bet, a mined block, or a transfer — `StatusBar`, `FinancialBettingStats`, `CalendarsNavigator`, `BetsHistoryExplorer`, `BTCWallet`, `AuctioningCompanyDetails`, `CompanyDetails`, `BlockExplorer`, `CasinoFinances`, `BotPlayHistory`, `ScFinances`, `ScTransactions`, `CasinoGamblingFinances`, `ClientsTransactions`, `ClientsBetsHistory`, `FoundersWallets`, `CasinoCoinSwaps`, `BTCPoolsAndHardwareShop`, `BotsBtcWallets`. Each is a candidate to migrate to "rebuild once in `_Ready()`, then only when a subscribed event fires" — full write-up, rationale, and the Basic Mode v0.1 gate: `Documentation/ProjectDesignManual.md` Chapter 38.

**The INVERSE failure — a correct event, fired far too often, driving expensive work (R3, 2026-07-28, §38.7)**: this cost more than any poll in the backlog above. `CasinoCoinSwapService` (an **autoload**, alive in every scene) ran the full `RecomputeAvailability` — a CHAIN-side recompute walking the casino's whole address book plus every undistributed pool event — off `CasinoScBalanceService.BalanceChanged`, which since ND.8f fires **on every settled bet of all five clients (~20/frame)**, for an input that cannot move a single chain-side figure. It became the dominant term in the sim's frame time; the only visible symptom was R2-C1's honest `SimulationThrottle` holding the clock at **1/6** of a requested 9000X (the proof was already on disk — `difficulty_trace.csv`'s flat `simSecConsumed/simSecOffered = 1/6`, i.e. the engine retaining exactly its `MaxBacklogSeconds = 2.0` per frame). Fix: `BlockAccepted`/`MarketDayChanged` stay **immediate** (they genuinely move those figures, once per block / in-game day); `BalanceChanged` raises a dirty flag drained at most every `AvailabilityCoalesceSeconds` (0.25 s) in `_Process` — Pattern 6's hybrid used deliberately. Three standing rules: **(1) frequency is part of a subscription's contract** — re-examine subscribers when an event's real rate changes (ND.8f multiplied this one by 5 and nothing re-checked); **(2) coalesce at the consumer when the trigger cannot move the value**; **(3) a displayed throttle is a MEASUREMENT, not a diagnosis** — below-1 retention means "find what is eating the frame", never "raise `MaxBacklogSeconds`/`MaxBetsPerFrame`", which only hands a saturated frame more work. Shipped with it: **`NetworkRoot.AggregateSpendable` now makes ONE pass over the UTXO set for a node's whole owned address set** instead of one full pass PER address (`O(addresses × utxos)` → `O(utxos)`, identical result — an outpoint has exactly one address), which also speeds every wallet panel and bot affordability check. Also fixed here: `difficulty_trace.csv` gained the ND.10j stale-schema `.old` rotation it was missing, so R2-T's appended `simSecOffered,simSecConsumed` columns stop sitting under a 9-column header.

**Project goal, tracked in `Documentation/PRIVATE_ROADMAP.md` §6:** before Basic Mode v0.1 is considered complete, audit every `_Process` override in the project against this principle and migrate what's feasible to event-driven design. Not a hard blocker on other work — but do not add a NEW poll-shaped `_Process` to the backlog above without first checking whether an event already exists (or should) for the state you're reading.

**Closing rule — a cost note is a MEASUREMENT or it is a guess wearing a measurement's clothes (Step 16 P16.6, 2026-07-31, §40.7).** Every judgement on this page is a performance judgement, and the P16.2 rescan carried one that read as quantified and had never been timed: *"~20 SHA256 per node past its frontier."* A derivation is a secp256k1 scalar multiply, not a hash — the real figure was **127 ms**, five orders of magnitude out, and because it *looked* measured it was the one number nobody re-checked in the phase that multiplied it by thirteen. Cost: a six-minute cold start. **Time it, or say plainly that you did not.** Its other half was right and worth copying — *"if that ever measures as material it is a T4 finding, never a reason to skip the rescan"* held exactly, and pointed at the layer that actually needed fixing. **When a documented cost comes true, re-read the note for the mitigation it already named.**

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

### Auditing a playtest run the developer hands you

The developer playtests and hands back a `user://` journal to audit. Two habits, both learned the hard way (2026-08-06, mini-plan 01 rounds 3–4):

- **When a dataset arrives after you recommended a parameter change, "they took the advice" is the LEADING hypothesis, not the last one.** Advice given here is acted on. The failure shape: every other parameter (base bet, both progression percents, the profit threshold) was *inferred from the data*, and the one parameter that could not be inferred was silently carried over from the previous run — the exact parameter that had just been recommended for change. It inverted a conclusion ("your loss stop never fired" when it had fired, correctly, on the final bet). **Infer what the data determines; ASK for what it does not.** A threshold that fires exactly once, on the last bet, leaves the same trace as a session someone stopped by hand — un-inferable parameters are precisely the ones worth one question.
- **Reproduce the engine's arithmetic EXACTLY, never approximately.** BigInt satoshis, `Money.Normalize` = truncation (`MidpointRounding.ToZero`), `DiceEngine.CalculateMultiplier` = `Round(100×RTP/chance, 4)`, and `BetService`'s `_pendingFractionalProfit` carry (reset only when a new `BetService` is constructed — i.e. per `StartPlayerAutobet`, **not** per auto-recharge restart). A float model turns real 1-satoshi evidence into ~120 phantom mismatches and buries the finding. Once exact, a single-satoshi difference becomes *evidence*: the remainder accumulator's reset independently pinpoints a session restart, cross-validating a boundary nothing persists.
- **Fit competing models, don't just check the current one.** Replaying a run under the *superseded* rule as well as the current one turns "looks right" into a measurement: round 4's shared-segment rule was confirmed by the round-3 model producing **zero** exact fits over a 100×100 threshold grid while round 4 reproduced 3,684 bets bet-for-bet.
- Free integrity checks worth running every time: balance continuity (`BalanceAfter[i-1] + NetAmount[i]`), duplicate `BetRecord.Id` (INC-002), win rate in σ, and longest loss run against `log(n)/log(1/p)`.
- **Audit before the developer restarts the app.** Until the player mines their first block the world is pre-genesis, so `ResetToPreGenesisDefaults()` rolls the clock, the balances **and the bet journal** back to the chain tip on every boot — a completed test run is erased by the next launch.

### Scripting tools on this machine — **there is NO Python. Do not reach for it.**

`python` / `python3` **appear on PATH and are not Python**: they are the Microsoft Store *app execution alias* (`AppInstallerPythonRedirector.exe`). `which python` succeeds, so availability checks pass; then every invocation prints `Python was not found; run without arguments to install from the Microsoft Store` and exits non-zero. This has cost repeated retry-and-switch cycles and left dead `Bash(python -c ...)` entries in `.claude/settings.local.json` that can never succeed. **Never write a `.py` file and never call `python`/`pip`.**

This is a deliberate choice, not a gap — for THIS project's workload the installed tools are the better instruments:

| Task | Use | Why |
|---|---|---|
| Aggregating CSV telemetry (`casino_bot_bid_trace.csv`, `difficulty_trace.csv`, `network_population_trace.csv`, …) | **`awk`** (Git Bash, via the Bash tool) | Per-row filter/group/sum is what it's for; handles the large traces without loading them. Extensive precedent in the allowlist. |
| Inspecting `user://` state (`state.json`, registries, checkpoints) | **`node -e`** (v22, already allowlisted) | JSON is native — no parser, no quoting fight. PowerShell's `ConvertFrom-Json` returns PSCustomObjects and is clumsy on nested chain state. |
| Verifying arithmetic that must MATCH the game | **`dotnet run`** on a throwaway console project in the scratchpad | The only faithful option. C# `decimal` ≠ Python/JS float, and `Money.Normalize`, the secp256k1 math and the DP verifications are exactly the cases where a reimplementation in another numeric model proves nothing. Precedent: the P16.6 secp256k1 benchmark, the ND.10l knapsack-DP brute-force check. |
| Filesystem, `%APPDATA%`, HTTP/dataset building | **PowerShell** | Windows-native; all the historical dataset scripts (`Get-BtcNetworkDaily.ps1`, …) are PowerShell. |

Note PowerShell here is **5.1**, not 7 — see the PowerShell tool's own constraints (no `&&`/`||`, no ternary, `Import-Csv` is slow on large traces; prefer `awk` for those). Two more 5.1 traps worth knowing up front: **`2>&1` on a native exe** (e.g. `dotnet build`) wraps stderr in `ErrorRecord`s and sets `$?` to `$false` even on exit 0 — don't gate logic on it; and **`Set-Content`/`Add-Content` default to the ANSI codepage**, so appending to a doc containing `—`/`§`/`✅` needs an explicit `-Encoding utf8` or `[System.IO.File]::AppendAllText`.

**Upgrading to PowerShell 7 is DEFERRED by decision (2026-08-06), not an oversight — do not propose it as a fix.** Installing `pwsh` would silently switch the agent's PowerShell tool to 7.x (Claude Code autodetects it), so it is a project decision, not a machine tweak. The full record — measured state, what 5.1 costs, why the risk isn't worth it today, the explicit reactivation triggers, and the install trap to avoid — is `Documentation/PRIVATE_ROADMAP.md` **§8 T5**. Read that before raising the topic again.

If Python is ever installed, delete this section — and disable the Store aliases first (Settings → Apps → Advanced app settings → App execution aliases), or the stub keeps shadowing the real interpreter.

---

## Open Design Questions

- What threshold lets the casino start repaying bank debt (P6)?
- Should minimum wager requirements be weekly, monthly, or both?
- How harsh should fee penalties be for missing minimum wager requirements?
- How much bot betting history should the player see by default?
- Should private mempool fees be available in Basic Mode or postponed?
- **Network fee market simulation — Option A ✅ IMPLEMENTED (Step 14 ND.7, 2026-07-13)**: the historical fee replay is live (`NetworkFeePolicy` consumes the dataset's daily median/mean band from Market Birth — see the Canonical Decisions fee row and the Implemented bullet). **Option B** (a reactive fee market from our own simulated mempool congestion) remains the **future validation experiment**: if it reproduces a curve similar to the replay, it confirms the Step-14 population/volume simulation was built right — and it is where fee CHOICE enters (queue-jumping above the daily base when the 24-tx cap saturates; today no participant has a reason to pay above base, OQ-ND7.1). See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §10.6 and `Documentation/PRIVATE_ROADMAP.md` "Network Fee Market Simulation".

---

## Scene Management

### Current State (to be migrated)

Scene transitions are currently done inline with hardcoded paths:
```csharp
GetTree().ChangeSceneToFile("res://Screens/DiceGame/DiceGame.tscn");
```
This pattern is scattered across multiple screen files. It is fragile and should be replaced.

### `SceneManager` Autoload

A `SceneManager` autoload centralizes all scene transitions. All paths live in one place; call sites use a compile-time-safe enum.

**`Scripts/Services/SceneManager.cs`** (registered in `project.godot`):

```csharp
public partial class SceneManager : Node
{
    public enum SceneId
    {
        DiceGame,
        BlockExplorer,
        BankrollProgrammer,
        BetsHistoryExplorer,
        CalendarsNavigator,
        MartingaleCalculator,
        MainMenu,           // planned
        // Add new scenes here only
    }

    private static readonly Dictionary<SceneId, string> Paths = new()
    {
        [SceneId.DiceGame]              = "res://Screens/DiceGame/DiceGame.tscn",
        [SceneId.BlockExplorer]         = "res://Screens/BlockExplorer/BlockExplorer.tscn",
        [SceneId.BankrollProgrammer]    = "res://Screens/BankrollProgrammer/BankrollProgrammer.tscn",
        [SceneId.BetsHistoryExplorer]   = "res://Screens/BetsHistoryExplorer/BetsHistoryExplorer.tscn",
        [SceneId.CalendarsNavigator]    = "res://Screens/CalendarsNavigator/CalendarsNavigator.tscn",
        [SceneId.MartingaleCalculator]  = "res://Screens/MartingaleCalculator/MartingaleCalculator.tscn",
        [SceneId.MainMenu]              = "res://Screens/MainMenu/MainMenu.tscn",
    };

    public void Go(SceneId scene) => GetTree().ChangeSceneToFile(Paths[scene]);
}
```

**Usage in any screen after migration:**
```csharp
private SceneManager _sceneManager;

public override void _Ready()
{
    _sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
}

private void OnBackButtonPressed()
{
    _sceneManager?.Go(SceneManager.SceneId.DiceGame);
}
```

All existing main screens have been migrated. Adding a new scene: (1) add entry to `SceneId` enum, (2) add path to `Paths` dictionary, (3) call `_sceneManager?.Go(SceneId.X)` at the call site.

The example above omits several DEV-only scenes for brevity (e.g. `CasinoFinances`, `FoundersWallets`, `BotPlayHistory`). Step 11 added three more, all DEV-only: `CasinoGamblingFinances` (Main Menu → casino SC balances/loans/transfers), `ClientsBetsHistory` (→ from `CasinoGamblingFinances`, per-client P/L + live bet feed), and `ClientsTransactions` (→ from `CasinoGamblingFinances`, per-client SC deposit/withdrawal ledger) — see `Screens/CasinoGamblingFinances/`. Step 12 added two **player-facing** scenes: `ScFinances` (MainMenu → the player's SC-flows hub: Private Bank Account balances, deposit/withdraw, betting stats) and `ScTransactions` (→ from `ScFinances`, the player's own Bank↔Main transfer history) — see `Screens/ScFinances/`. Step 13 added the **player-facing** `CasinoCoinSwaps` (MainMenu + ScFinances → the casino's SC↔BTC swap desk; carries no DEV controls itself — its reserve/fee/auto-floor knobs live in the existing `CasinoFinances`/`CasinoGamblingFinances` DEV scenes) — see `Screens/CasinoCoinSwaps/`. `SceneManager.Go()` also records a one-deep `PreviousScene` for origin-aware back navigation.

### `StatusBar` Component

**`UI/StatusBar/StatusBar.cs`** — pure C# `HBoxContainer` (no .tscn needed). Instantiated programmatically in each screen's `_Ready()`.

Shows Main Balance, Bankroll, **the player's BTC wallet**, the game clock, and the BTC price ticker.

**The two BTC cells are different KINDS of figure and must stay visually distinguishable (2026-08-06).** `BTC Wallet: 12.50000000` is money the player *owns* — coloured **bitcoin orange** (`#F7931A`), placed beside the SC balances it belongs with. `BTC Price: 1,234.56 SC` is a market quote they don't own — default text colour, at the far end, showing `BTC Price: —` before Market Birth (2010-07-18) and `BTC Price: HALT` on the 13 halt days. The SC suffix on the price and the SC suffix on DiceGame's Bankroll / Main Balance labels landed in the same change, for the same reason: once a BTC figure sits in the bar, every unlabelled number becomes ambiguous. **Do not add a third bare number to this bar.**

Refresh cadence differs per cell and this is deliberate:
- SC balances + clock — `_Process` every frame (cheap field reads; the clock genuinely needs real delta).
- BTC price — event-only, on `BtcMarketDataService.MarketDayChanged` (daily step function).
- **BTC wallet — `NetworkRoot.BlockAccepted` (dirty flag drained next frame) + a 2 s fallback tick.** It reads `NetworkRoot.GetPlayerSpendableBalanceStatic()`, which is **one `AggregateSpendable` pass over the whole UTXO set** — cheap at this cadence, ruinous per frame (§38.7's inverse-failure lesson). The block event is the real edge; the fallback exists only because a player's own send (BTCWallet or a swap sell) drops spendable the instant it is broadcast, with no block to announce it. The static event is unsubscribed in `_ExitTree`.

`GetPlayerSpendableBalanceStatic()` is a **static** twin of the instance `GetNodeSpendableBalance` because the StatusBar is instantiated programmatically in every screen and owns no `NetworkRoot` node (the `GetPlayerChainLengthStatic` precedent). It reads the **owned address set**, never `WalletAddress` alone — the P16.6 trap: base-only reads went to zero once change rotation landed.

```csharp
// In _Ready() of any screen — insert at top of a VBoxContainer:
var vbox = GetNode<VBoxContainer>("ContainerPath");
var statusBar = new StatusBar();
vbox.AddChild(statusBar);
vbox.MoveChild(statusBar, 0);

// Or for scenes that use a placeholder slot (MainMenu, MartingaleCalculatorStandalone):
GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
```

### Navigation Map

```
MainMenu
├── DiceGame          (also reachable directly; DiceGame has its own "Main Menu" button)
│   ├── ScFinances          → Main Menu   (DiceGame's "Deposit Balance" button opens ScFinances; DepositPopup retired in Step 12)
│   ├── BankrollProgrammer  → Main Menu
│   ├── BlockExplorer       → Main Menu
│   │   ├── AuctioningCompanyDetails (Step 14 ND.5, Enroll Mode "Details →" while InAuction; forwards to CompanyDetails on resolution) → BlockExplorer
│   │   └── CompanyDetails (Step 14 ND.8b.4, Enroll Mode Founded rows' "Details →" — Board Vote / dividend claims) → BlockExplorer
│   └── CalendarsNavigator  → Main Menu / BetsHistoryExplorer
│       └── BetsHistoryExplorer → origin-aware back (Main Menu / CalendarsNavigator / ScFinances)
├── ScFinances [player-facing]  → Main Menu   (Step 12 — the player's SC-flows hub)
│   ├── ScTransactions              → ScFinances
│   ├── BetsHistoryExplorer         → (origin-aware back to its launcher)
│   ├── BankrollProgrammer          → ScFinances / (its own Main Menu back)
│   └── CasinoCoinSwaps             → origin-aware back (Main Menu / ScFinances)
├── CasinoCoinSwaps [player-facing]  → Main Menu   (Step 13 — the casino's SC↔BTC swap desk)
├── MartingaleCalculator (standalone, full-screen) → Main Menu
├── WorldEconomy [DEV]  → Main Menu   (Step 14 ND.8c — SC Monetary Ledger readout; + ND.8b.6 company inflow/expansion knobs)
├── CentralBank [DEV]   → Main Menu   (Step 15 P15.1e — the FED's per-client loan accounts, D-15.16)
└── CasinoGamblingFinances [DEV]  → Main Menu
    ├── ClientsBetsHistory [DEV]    → Casino Gambling Finances
    └── ClientsTransactions [DEV]   → Casino Gambling Finances
```

`BetsHistoryExplorer`'s back button is **origin-aware** (Step 12 / SF.4.2): it returns to `SceneManager.PreviousScene ?? MainMenu`, so it goes back to whichever hub launched it (`CalendarsNavigator` or `ScFinances`).

DiceGame's MartingaleCalc button opens the **popup version** (`Screens/MartingaleCalculator/`) inline — it does not navigate away. The standalone version (`Screens/MartingaleCalculatorStandalone/`) is a full screen reachable only from MainMenu.

---

## Testing

**Status**: _[Pending — no test framework configured yet. Document test approach here once established.]_

---

## Architecture Documentation

Detailed design documents are in `Documentation/`:

| File | Contents |
|---|---|
| `DESIGN_OVERVIEW.md` | Target design per system with implementation status labels |
| `GLOSSARY.md` | Canonical terminology (source of truth for naming) |
| `PLAYER_GUIDE.md` | What is playable now (updated for each release) |
| `IMPLEMENTATION_STATUS.md` | What shipped, when, and the reasoning behind each step (Steps 7–16). Extracted from this file at 60,780 characters. Most entries end by pointing at the canonical write-up in `ProjectDesignManual.md` or an `AIHelperFiles/` plan — **those win where they disagree.** Its P0–P8 copy is stale and annotated as such; `PRIVATE_ROADMAP.md` owns those priorities |
| `SERVICES.md` | **The autoload services in full** — one section per service: what it owns, its persistence path, its checkpoint/pre-genesis behaviour, and the decisions behind it. Extracted from this file at 48,517 characters, where it was the last oversized block. This file keeps only the one-line index in **Key Architecture — Autoload Services**; access and registration-order rules stay in **Important Patterns §5** |
| `PRIVATE_ROADMAP.md` | Internal priorities P0–P8, canonical decisions, open questions |
| `ProjectDesignManual.md` | The long-form design record — one chapter per system, written as the work lands. **Ch. 29** UI/Godot layout (read before any `ScrollContainer` work; **§29.12** the number-locale audit + its detector) · **Ch. 30** UTXO model · **Ch. 35** timeline guard · **Ch. 36** network population · **Ch. 38** event-driven vs. `_Process` · **Ch. 39** the Central Bank + §39.16's six standing conventions · **Ch. 40** persistence durability & simulation scale (**§40.8** duplicated records vs. streak metrics — read before trusting any figure computed off `BetHistory`) · **Ch. 41** player participation in company governance (pause / policy / abstention) |
| `INCIDENT_LOG.md` | **Significant design crashes** — data-loss/corruption events whose cause is a design limitation, not a typo. One entry per incident: symptom, timeline, proximate vs. root fault, evidence, blast radius, recovery, the phase that fixes it, and the generalized lesson. Add an entry whenever a crash costs a world/playtest or reveals a persisted figure that had been silently wrong. Currently: INC-001 (the 1.13 GB bet journal + truncated world snapshot, 2026-07-29) · INC-002 (the impossible martingale level — duplicated records amplified by a streak metric, 2026-08-06) · INC-003 (two bettors in a journal that belongs to one — the explorer's retired world-clock rewind, found three days after its own fix, 2026-08-19) |
| `REFERRAL_AUCTION.md` | The referral auction's full spec, extracted from this file's Canonical Decisions row when it reached 32,007 characters. **§1 is the current rule** (opening/raise floors, the tracked pool, bot cadence, the exclusion precedence, the three ladder modes, the stuck escalation) — every figure verified against `NetworkRoot.cs` with line references. **§2 is the amendment history** (EB.2 → ND.10l → P16.6), each entry marked still-current or superseded. Read §1 to implement; read §2 only to learn why a rule has its shape |

---

## Git Workflow

- **`main` is the stable trunk.** It is anchored at known-good points (e.g. a completed roadmap step). Keep it buildable.
- **One branch per category of modifications** (e.g. `scheduled-bot-transactions`, `candidate-block-model`, `historical-founders`). Do feature work on its branch; merge back to `main` when stable.
- **STAGE → ASK IN CHAT → COMMIT (2026-08-14).** When a unit of work is finished, Claude **stages** it (`git add -A`) and **posts the full commit message in the chat**, then stops and waits. The developer authorises in the chat; **Claude then runs `git commit`.** Never commit before that authorisation, and never leave an authorised change uncommitted.
  - **Why:** once committed, the change is folded into history and the developer loses the easy "what exactly did you just do?" view. Staging first keeps the diff reviewable while the reasoning is still fresh, which is the moment review is worth anything.
  - **The message goes in the CHAT, not into `.git/COMMIT_EDITMSG`.** That file was tried first and does **not** surface in the VS Code Source Control input, so the developer never saw it — a prepared message nobody can read is the same as no message.
  - Claude still **writes** the message to the usual standard (what changed, why, and the rule it establishes). Only the go-ahead is the developer's.
  - `push`, `merge`, `checkout -b` and branch operations remain **explicit-request only**, unchanged.
  - A clean working tree usually means the developer already committed; verify via recent commit history, don't assume there's work to commit.
- **Keep docs current on the branch where the work happens — including CLAUDE.md.** When a change alters the architecture, update CLAUDE.md (and the other docs) in the same branch/commits as the change, not deferred to merge. CLAUDE.md stays tracked — do not untrack it (its history matters and Claude Code reads it every session).
