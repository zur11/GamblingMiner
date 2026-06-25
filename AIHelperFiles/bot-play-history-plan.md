# Bot Play-History Scene — Implementation Plan

**Status**: ✅ Implemented (H.1–H.4; builds clean). Part of roadmap **Step 6** (`IMPLEMENTATION_ROADMAP.md`), tracked separately from `btc-pools-hardware-plan.md`. **Sequenced after the Network Difficulty Regulator** (the regulator is foundational; this scene is independent and can follow it).

**Goal**: let the player **study how the miner bots bet**, so they can learn strategies. Show the **last 260 plays of each miner bot currently playing alongside the player**.

---

## Context

- Miner bots run inside **`SimulationService`** (each is a `BotRunner` with its own `AutoBetSession`/`Wallet`). They only advance while the player participates (time only moves on player bets — unchanged).
- Today only the **player's** bets are recorded (`UserStatsService` / bet-history container). **Bot bets are not stored anywhere** — so this feature needs a new lightweight per-bot history.
- The **in-game Notepad** already exists (`NotepadService` + `UI/NotepadPopup/NotepadPopup`), reusable from any scene.

---

## Decisions (resolved 2026-06-23)

| ID | Decision |
|---|---|
| OQ-13 | **In-memory only** for the 260-play buffers to start (cleared on app restart). *Future:* explore *partial* persistence — e.g. persist only a small set of player-"followed" bots; the rest stay session-memory only. |
| OQ-14 | **MainMenu entry** (round-trip), **no DiceGame link** for now. Notepad access required regardless. |

Open: whether to also show it inline in DiceGame later (nice-to-have, not now); the "followed bots" partial-persistence model (later).

---

## Design

### Data: per-bot rolling history (in `SimulationService`)
- Add a **per-`BotRunner` ring buffer of size 260** holding settled-bet entries:
  `{ betAmount, roll, isWin, profit, bankrollAfter, timestampUtc }` (mirror the player's `BetTransactionEvent` fields that matter for study).
- Push an entry in `ExecuteBotBet` right after the bot's `ExecuteNext` settles (where `SaveBotFinancialState` already runs).
- In-memory only; lives as long as the runner. When a runner is rebuilt on recharge/restart, **keep the history** (it's the *bot's* history, not the session's) — so store the buffer keyed by **nodeId** in `SimulationService`, not on the transient `BotRunner`.
- Expose: `IReadOnlyList<BotPlayEntry> GetBotPlayHistory(string nodeId)` and `IReadOnlyList<string> GetActiveBotNodeIds()` (bots with a running session / non-empty history).

### Scene: `BotPlayHistory`
```
BotPlayHistory (Control)
├── StatusBarPlaceholder (HBoxContainer)  — StatusBar injected
├── BackBtn → MainMenu
├── NotepadBtn → opens NotepadPopup
└── MainSplit (HSplitContainer)
    ├── BotListPanel (VBox)   — one button per active miner bot
    └── HistoryPanel (VBox)
        ├── BotTitleLabel
        └── HistoryTable (last 260 plays: #, bet, roll, W/L, profit, bankroll)
```
- Selecting a bot fills the table from `GetBotPlayHistory(nodeId)` (newest first).
- Refresh on a light timer (reuse the ~1 s pattern) so it updates live while a background autobet runs.

---

## Small steps

- **H.1 — History buffer.** ✅ `BotPlayEntry` record + per-nodeId 260-ring buffer (`_botHistories`, keyed by nodeId so it survives recharge/restart) in `SimulationService`; pushed in `ExecuteBotBet`; getters `GetBotPlayHistory(nodeId)` (newest first) / `GetActiveBotNodeIds()` (running session or non-empty history).
- **H.2 — Scene.** ✅ `Screens/BotPlayHistory/BotPlayHistory.{tscn,cs}`: live bot list (left) + history table (right, bbcode `[table=6]`: #, bet, roll, W/L, profit, bankroll, newest first) + StatusBar. Refreshes on a 1 s timer.
- **H.3 — Notepad.** ✅ Notepad button in the top bar wired to `NotepadPopup`.
- **H.4 — Navigation.** ✅ `SceneManager.SceneId.BotPlayHistory` + path; MainMenu "Bot Play History [DEV]" button (round-trip, Back → MainMenu).

---

## File checklist

| File | Status |
|---|---|
| `Scripts/Services/SimulationService.cs` | ✅ modified (ring buffer + getters + push in `ExecuteBotBet`) |
| `Screens/BotPlayHistory/BotPlayHistory.tscn` | ✅ created |
| `Screens/BotPlayHistory/BotPlayHistory.cs` | ✅ created |
| `Scripts/Services/SceneManager.cs` | ✅ modified (enum + path) |
| `Screens/MainMenu/MainMenu.tscn` / `.cs` | ✅ modified (entry button) |
