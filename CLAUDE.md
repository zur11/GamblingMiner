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
    **Baseline: pass 1 returns exactly 5 lines, pass 2 returns 0** (re-verified 2026-08-23). **The 5 are identified by SHAPE, not by line number** — every one is a *continuation* line of a `+`-chained interpolated string whose `string.Create(CultureInfo.InvariantCulture, …)` wrapper sits more than 3 lines above, i.e. outside the detector's context window. Two in `FoundersWallets.cs` (the Hal and Mike Hearn rows of the founders readout) and three in `NetworkRoot.cs` (one in the address-details block, two in the mining-status block). **A 6th hit, or any hit whose wrapper is NOT above it, is a real regression.** *Line numbers were given here until 2026-08-23 and had already drifted +10 — see Standing Convention 15; check the shape, never the number.*
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
| Specific split | `39,900 SC Main Balance + 100 SC Bankroll` (unchanged by Step 12 — the `40,000` stays in the casino accounts, funded as today). **⚠️ The split is produced LAZILY, not at world start.** A freshly reset world reads **`40,000 Main / 0 Bankroll`** on the StatusBar; the first entry to `DiceGame` fires the extra-lazy Bankroll auto-recharge, which moves the `100` dose across and yields the canonical figures. Both states are correct — **verifying a fresh world against this row before opening DiceGame will look like a bug and is not one** (observed 2026-08-23, post-wipe) |
| Private Bank Account (Step 12) | Starts at `0` — an **optional SC reserve outside the casino**, all automation OFF by default. The player *owns* it (no debt); withdraw Main→Bank to park SC safe, deposit Bank→Main to bring it back. Managed in `ScFinances`. See `PlayerBankAccountService` |
| Player-facing term | `Main Balance` (not "Principal Balance") |
| Game over condition | `Private Bank Account + Main Balance + Bankroll = 0` (Step 12 / D-SF2.1 — total ruin across all three SC accounts; while the bank holds anything it is **not** game over, since the player can always deposit it back). Written to leave room for a future **BTC→SC coin-swap escape hatch** (§7.4) — the check must be interceptable by a later exchange layer, not an irreversible terminal |
| Current mining rule | `1 bet = 1 nonce attempt` |
| Basic Mode halving | `2,100 blocks` (≈ 4 in-game years at 100X scale). **The only independent value of the three rows here** — `NetworkRoot.HalvingIntervalBlocks`, from which the other two are read or contrasted |
| Total BTC supply | `210,000 BTC` — **a CONSEQUENCE of the row above, not a constant.** It is `50 × HalvingIntervalBlocks × 2` and exists nowhere in the code as a literal; change the halving interval and this figure changes with it, silently, with nothing to catch the stale copy. Converges to in-game year ~2141 (`= 34 × HalvingIntervalBlocks` blocks) |
| Real Bitcoin halving | `210,000 blocks` — **NOT used in Basic Mode, and NOT the row above.** The numeral is a coincidence and the units differ: the row above is `210,000` **BTC of supply**, this is `210,000` **blocks between halvings** in real Bitcoin. They are unrelated quantities that happen to share a number, listed adjacently, which is precisely how one gets mistaken for the other |
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
| Founders | Satoshi (target `11,000 BTC`, retires ≥ `2011-04-26`, then frozen) + Hal (`P=1.0` drip, fades to 0 by `2009-08-09`) + Mike Hearn (joins ~Apr 2009, never mines; the **round-trip is 32.51 BTC** and he **nets +82.51** — the difference is Satoshi's separate 50.00 gift, `HistoricalEventScheduler` E7a + E7b. `82.51` is the net, never the transaction) |
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

**This principle applies to EVERY player-facing persisted value, not just the four services `ApplyCheckpointToServices()` lists** — `BankrollProgramService` (dose + transfer records), the game clock, and the bet-history log (`UserStatsService`) all self-persist eagerly (on every dose change / bet / recharge) and MUST be explicitly included in both the post-first-block checkpoint restore and the pre-genesis reset below, or they silently leak uncommitted state across a restart. When adding a new player-facing autoload or persisted list, ask: "does this need a `BlockSessionCheckpointService` restore path (post-block) AND a `ResetToPreGenesisDefaults()` path (pre-block)?" — if it holds player state that changes outside of a mined block, the answer is yes. **And a third question (TL.3 lesson): "is its `user://` file in the `NetworkRoot.ResetWorldIfIncompatible()` delete list?"** — every persisted **world-state** file must be, or it leaks across a format/timeline clean reset (`casino_coin_swap_state.json` missed this and alt-world hardware/pool state survived a timeline wipe).

**The exempt set, named in full (2026-08-23) — an exemption nobody wrote down is indistinguishable from an omission, which is exactly how two files slipped through.** Deliberately NOT deleted: the five wallet seeds (`wallet_state`, `casino_wallet_state`, `satoshi_wallet_state`, `hal_wallet_state`, `mike_hearn_wallet_state`), `bot_wallet_registry.json`, `notepad_notes.json`, `saved_betting_strategies.json`, and **`wordlist_256.json`** (the seed-phrase wordlist — identity infrastructure; exempt in code since it shipped but unnamed in any document until now). Also exempt: **`user://logs/godot*.log`**, Godot's own engine logs — they are *evidence* (mini-plan 07 §A.6.2 read them while dating INC-004), the engine rotates and caps them at five per world so they cannot accumulate, and no code reads them back. The two stamps `world_format_version.txt` / `world_timeline.stamp` are rewritten by the wipe itself rather than deleted.

**Repair and diagnostic siblings are swept by SUFFIX, not enumerated** — `.tmp`, `.corrupt`, `.prerepair`, in `user://` and `user://blockchain/`. Enumeration failed twice in one week (`cb1779a` created `.corrupt`/`.tmp` and listed neither; two `.prerepair` files then survived the 5→6 wipe), and it **cannot** work for `.prerepair`, which no code writes — a human makes it during a manual repair, so there is no feature to attach an "add it WITH the feature" rule to. The sweep prints every file before destroying it. **The convention that makes it safe, and which you must follow: a file carrying one of those suffixes is NEVER the only copy of anything — if you are repairing a world by hand, archive it OUT of `user://` first.**

**Pre-genesis (no block has EVER been mined — only the historical bootstrap has run)**: a checkpoint is captured **only** by a real block-mined event now (`DiceGame.CaptureBlockCheckpoint()` / `SimulationService.CaptureCheckpoint()`) — never merely by opening the app (there is no more "baseline" auto-capture). Whenever `BlockSessionCheckpointService.HasCheckpoint()` is false, `ResetToPreGenesisDefaults()` runs on every boot instead of `ApplyCheckpointToServices()`, forcing Main Balance/Bankroll/dose/transfer records back to their true canonical defaults, and resetting the calendar + bet history to the historical bootstrap's landing instant (re-derived from the chain tip via `NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()` — before any real block, the tip *is* the bootstrap's last block, so nothing extra needs to be persisted for this). **Canonical rule**: the in-game calendar clock always exactly equals the timestamp of the block that most recently defines the checkpointed world — never offset, not even by one second (every checkpoint capture reads the clock synchronously right after mining, so this is naturally true post-first-block; the pre-genesis reset and the historical bootstrap's player-start instant both follow the same rule deliberately). See the Canonical Decisions table above ("Player start") and `Documentation/ProjectDesignManual.md` §24.9.

**Canonical rule — game time, never wall-clock, for anything the player can see or that gets persisted.** Every event timestamp that is displayed, stored in a `TransferRecord`/`LoanRecord`/`BetRecord`/ledger entry, or compared against a checkpoint boundary **must** come from `CalendarTimeService` (`.CurrentUtcDateTime` / `.CurrentLocalDateTime`) — **never** `DateTime.Now`/`DateTime.UtcNow` directly. An audit (2026-07-01, OQ-BP.10 in `AIHelperFiles/player-and-casino-bankroll-programmer-plan.md`) found this violated in several places already shipped earlier in the same plan — most seriously, `DiceGame`'s `BetService` timestamp provider used `DateTime.UtcNow` for **every manual bet**, which (since `RollbackHistoryToUtc`/`GetLoadedHistoryStats` compare bet timestamps against the game-time checkpoint boundary) would have silently corrupted the pre-genesis history-rollback fixes above for manual play. All such call sites were fixed to read `CalendarTimeService` (with a `?? DateTime.UtcNow` null-safety fallback only, never as the primary source). **The only legitimate use of real wall-clock time** is pure internal DEV/file bookkeeping metadata the player never sees (e.g. `BlockSessionCheckpointService.CapturedAtUtc`, each service's own `UpdatedAtUtc` snapshot field) or genuine real-time concerns unrelated to game-world state (`UserStatsService`'s 250ms UI-throttle timer, `DiceGame`'s real-bets-per-second rate-measurement fields). When adding any new timestamped record, ask: "is this game-world state, or pure DEV telemetry?" — if the player could ever see it, it's game time.

Full rationale and the bugs this resolved: `Documentation/ProjectDesignManual.md` §24.8 (post-first-block), §24.9 (pre-genesis + the exact-timestamp rule), and §24.10 (the wall-clock-vs-game-time audit).

**⚠️ This rule governs commit TIMING, not commit DURABILITY — they are separate problems.** "A block is the only commit" says *when* to write and is silent on what a half-written file means. **When persisting player-owned state, answer all three:** is the write **atomic** (`.tmp` → flush → rename, never truncate-and-stream) · does a corrupt read **fail loudly** (a `Try` prefix is a promise — honour it or drop it) · can a failed load ever be **persisted back over the good copy** (guard the writer, not just the reader). A world was once lost to all three at once. Case: `INCIDENT_LOG.md` **INC-001**; rules: `ProjectDesignManual.md` **Ch. 40** + §39.16 rule 7.

**⚠️ Its sequel — fixing the WRITER is half a fix.** Three standing rules, each earned by a figure that was wrong on screen for an unknown number of sessions:

- **When an incident names a corrupted input, enumerate its CONSUMERS** and harden the one that *distorts* the error most — a sum hides it, a streak broadcasts it.
- **Where a displayed figure has a cheap closed-form bound, assert it**, or its only detector is a human being surprised.
- **A label is a claim about semantics**, and it gets audited far less often than the arithmetic under it.

Cases: `INCIDENT_LOG.md` **INC-002** and **INC-003**; analysis: `ProjectDesignManual.md` **§40.8**.

**⚠️ Its third instalment — the REPAIR is also a writer.** Three more, from INC-004:

- **A default value is an assertion, and a fresh object asserts the most flattering one.** A field encoding *coverage* (`IsComplete`, `IsFull`, `HasAll`) must default to the **pessimistic** side, because the paths that skip initialization are the failure paths — a zeroed object inheriting `true` is how a lie reaches the screen.
- **Declining to record a value is not the same as erasing the value that was there.** Any capture that returns "nothing" into a structure rewritten wholesale must **carry the previous value forward**, or the first write after a fault destroys the last good copy.
- **Fixing a writer creates a new writer; ask it the same three questions.** This fix's own first draft was atomic and loud and still turned a recoverable failure into a permanent one.

Case: `INCIDENT_LOG.md` **INC-004**.

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

14. **A bulk edit made through a shell or a regex is not finished until a corruption grep over the RESULT says so.** Its characteristic failure is not an error — it is **content silently removed**, on a command that reports success. Case: a `node -e "…"` one-liner rewriting five table rows had every **backticked span eaten by the shell** before node ever parsed the script; the rows landed with their code spans and file:line citations blank, and the run printed `rows replaced: 5`. **Before accepting such an edit, grep the result for the shape of what would be missing** — empty code spans, `[V: ]`, a row conspicuously shorter than its neighbours — and prefer the file-editing tool outright when the content carries backticks, `$`, or quotes. The ⚠ bullet under **Money Handling** ("do not fix these with a bulk regex", where a `perl -pi -e` pass blanked 24 lines) is this same rule found on a different day, against different content; **this is its general form, and the two should be read together.**
    **⚠ For a WHITESPACE-only bulk edit specifically, `git diff -w` is NOT a verification — it is the one check guaranteed to pass.** It ignores whitespace everywhere, *including inside string literals*, so it returns empty for a benign added final newline and for a corrupted multi-line verbatim string alike. Verify instead by **comparing the content with only the LEADING indentation stripped** (`sed 's/^[[:space:]]*//'` both sides, then compare hashes) — that catches everything `-w` blinds you to — and **check first whether the files contain verbatim (`@"`) or raw (`"""`) literals**, whose leading spaces are content, not indentation. Measured on the 2026-08-23 tabs normalization: `-w` was empty for all 23 files while the stricter check correctly flagged three.

15. **A number written in PROSE beside the code that computes it is frozen at the moment it was typed. Cite the SYMBOL or the SOURCE, never the value.** Write `InitialLoanAmount`, `HalvingIntervalBlocks`, "every row in `company_roster.csv`", "the day's replayed median" — not `40,000`, not `210,000`, not "all 44 rows", not "0.1 BTC". The value belongs in exactly one place: the declaration. Prose that repeats it creates a second copy with no compiler, no test and no reader watching it, and **the copy does not rot loudly — it stays plausible.** Four instances, all the same shape, found in one afternoon: a comment reading `InitialLoanAmount = 100M` directly above the line that reads the constant, which is `40,000` (2,500× out) · `CompanyRoster` citing "all 44 rows" of a CSV holding 42 · `"chunked by month"` in four documents describing a journal segmented by **entry count**, never by date · the swap desk's flat `0.1 BTC` network fee, retired by ND.7 and quoted for a while afterwards. **Where a documented figure is a CONSEQUENCE rather than a constant** — total supply is `50 × HalvingIntervalBlocks × 2` and appears nowhere as a literal — **say so in the same breath**, because changing the real constant silently invalidates every prose copy of the derived one. This is the written twin of rule 1: rule 1 is a *persisted* figure diverging from reality, this is a *written* one doing the same thing, and neither announces itself.

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

### Scripting tools on this machine — **there is NO Python. Do not reach for it.**

`python` / `python3` **appear on PATH and are not Python**: they are the Microsoft Store *app execution alias* (`AppInstallerPythonRedirector.exe`). `which python` succeeds, so availability checks pass; then every invocation prints `Python was not found; run without arguments to install from the Microsoft Store` and exits non-zero. This has cost repeated retry-and-switch cycles and left dead `Bash(python -c ...)` entries in `.claude/settings.local.json` that can never succeed. **Never write a `.py` file and never call `python`/`pip`.**

This is a deliberate choice, not a gap — for THIS project's workload the installed tools are the better instruments:

| Task | Use | Why |
|---|---|---|
| Aggregating CSV telemetry (`casino_bot_bid_trace.csv`, `difficulty_trace.csv`, `network_population_trace.csv`, …) | **`awk`** (Git Bash, via the Bash tool) | Per-row filter/group/sum is what it's for; handles the large traces without loading them. Extensive precedent in the allowlist. |
| Inspecting `user://` state (`state.json`, registries, checkpoints) | **`node -e`** (v22, already allowlisted) | JSON is native — no parser, no quoting fight. PowerShell's `ConvertFrom-Json` returns PSCustomObjects and is clumsy on nested chain state. |
| Verifying arithmetic that must MATCH the game | **`dotnet run`** on a throwaway console project in the scratchpad — **expect one approval prompt; deliberately not allowlisted** | The only faithful option. C# `decimal` ≠ Python/JS float, and `Money.Normalize`, the secp256k1 math and the DP verifications are exactly the cases where a reimplementation in another numeric model proves nothing. Precedent: the P16.6 secp256k1 benchmark, the ND.10l knapsack-DP brute-force check. |
| Filesystem, `%APPDATA%`, HTTP/dataset building | **PowerShell** | Windows-native; all the historical dataset scripts (`Get-BtcNetworkDaily.ps1`, …) are PowerShell. |

⚠️ **A trailing open `*` on an interpreter is arbitrary execution — learn the SHAPE, it is what you must recognise at the next approval prompt.** Claude Code's Bash patterns let a single `*` span any text including spaces, so `Bash(node -e ' *)`, `Bash(awk 'BEGIN{ *)` and `Bash(perl -i -pe ' *)` do not approve *a command* — they approve *any program the interpreter will accept*. The prefix reads specific and is not: what follows the quote is unbounded. The tell is an interpreter's inline-program flag (`-e`, `-c`, an opening `{`) followed by `*)`. **When a prompt offers to save a rule of that shape, it is a decision about arbitrary code execution, not about the command you just ran.** Approve it only for a tool with a standing, documented need — otherwise take the per-use prompt.

**The 2026-08-23 allowlist audit (237 → 60 entries) settled this for every such rule in the project, and `node -e` is now the ONLY one left.** Removed: `perl -i -pe` (an in-place, no-backup rewrite of any path — the exact shape Standing Convention 14 was written about), `dotnet run` (arbitrary compilation; one prompt per throwaway console project is a fair price), and two open-tail `awk` rules (`awk`'s `system()` is the same hole in a smaller disguise). The closed, fully-specified `awk` aggregations over the network and roster CSVs stayed — a *bounded* interpreter invocation is not this problem.

**The consequence to keep straight: while `node -e` stands, path permissions are ADVISORY, not a boundary.** `Read(…)`/`Edit(…)` rules bind Claude's own file tools and the file commands it recognizes (`cat`, `head`, `tail`, `sed`); they do **not** bind a subprocess that opens files through its own runtime. So a Node one-liner reaches any path on the machine however narrow the read rules look. That is an accepted trade — `node -e` is the sanctioned way to inspect `user://` JSON and the friction of re-approving it several times a session buys nothing real — but it is a trade, and it has a failure mode: **reading the two surviving `Read(…)` rules and concluding the agent is confined to those two trees.** It is not. A real boundary has to be OS-level — Claude Code's sandbox, which merges `sandbox.filesystem` with the Read/Edit **deny** rules — never the allowlist on its own.

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
| `DESIGN_OVERVIEW.md` | Target design per system with implementation status labels |
| `GLOSSARY.md` | Canonical terminology (source of truth for naming) |
| `PLAYER_GUIDE.md` | What is playable now (updated for each release) |
| `IMPLEMENTATION_STATUS.md` | What shipped, when, and the reasoning behind each step (Steps 7–16). Extracted from this file at 60,780 characters. Most entries end by pointing at the canonical write-up in `ProjectDesignManual.md` or an `AIHelperFiles/` plan — **those win where they disagree.** Its P0–P8 copy is stale and annotated as such; `PRIVATE_ROADMAP.md` owns those priorities |
| `SERVICES.md` | **The autoload services in full** — one section per service: what it owns, its persistence path, its checkpoint/pre-genesis behaviour, and the decisions behind it. Extracted from this file at 48,517 characters, where it was the last oversized block. This file keeps only the one-line index in **Key Architecture — Autoload Services**; access and registration-order rules stay in **Important Patterns §5** |
| `PRIVATE_ROADMAP.md` | Internal priorities P0–P8, canonical decisions, open questions |
| `ProjectDesignManual.md` | The long-form design record — one chapter per system, written as the work lands. **Ch. 29** UI/Godot layout (read before any `ScrollContainer` work; **§29.12** the number-locale audit + its detector) · **Ch. 30** UTXO model · **Ch. 35** timeline guard · **Ch. 36** network population · **Ch. 38** event-driven vs. `_Process` · **Ch. 39** the Central Bank + §39.16's six standing conventions · **Ch. 40** persistence durability & simulation scale (**§40.8** duplicated records vs. streak metrics — read before trusting any figure computed off `BetHistory`) · **Ch. 41** player participation in company governance (pause / policy / abstention) |
| `INCIDENT_LOG.md` | **Significant design crashes** — data-loss/corruption events whose cause is a design limitation, not a typo. One entry per incident: symptom, timeline, proximate vs. root fault, evidence, blast radius, recovery, the phase that fixes it, and the generalized lesson. Add an entry whenever a crash costs a world/playtest or reveals a persisted figure that had been silently wrong. Currently: INC-001 (the 1.13 GB bet journal + truncated world snapshot, 2026-07-29) · INC-002 (the impossible martingale level — duplicated records amplified by a streak metric, 2026-08-06) · INC-003 (two bettors in a journal that belongs to one — the explorer's retired world-clock rewind, found three days after its own fix, 2026-08-19) · **INC-004** (the lifetime rollup that zeroed itself and called it complete — a non-atomic writer feeding a failed load that was written back over the only copy, 2026-08-22) |
| `REFERRAL_AUCTION.md` | The referral auction's full spec, extracted from this file's Canonical Decisions row when it reached 32,007 characters. **§1 is the current rule** (opening/raise floors, the tracked pool, bot cadence, the exclusion precedence, the three ladder modes, the stuck escalation) — every figure verified against `NetworkRoot.cs` and cited **by symbol** (line numbers until 2026-08-23, when all nine were found to have drifted +10; Standing Convention 15). **§2 is the amendment history** (EB.2 → ND.10l → P16.6), each entry marked still-current or superseded. Read §1 to implement; read §2 only to learn why a rule has its shape |

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
