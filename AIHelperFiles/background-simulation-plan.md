# Background Simulation — Implementation Plan

**Status**: 🆕 LEAD on branch `background-simulation`. **All decisions resolved (§7) — implementing, Phase 1 first.**
**Goal**: while the player keeps an autobet strategy running, the **whole simulation keeps running regardless of the active scene** — bets fire, nodes mine, time advances, balances change — so the Block Explorer, StatusBar, etc. update in real time. Leaving/entering DiceGame (or any scene) must **not** interrupt or rewind it. Plus: show in the Block Explorer **which miner bots are mining and at what speed**.

---

## 1. Root cause (confirmed)

The entire autobet simulation lives **inside the DiceGame scene**:

- `DiceGame._Process(delta)` → `TickAutoBet(delta)` paces the **player** autobet (`ExecuteAutoBetOnce`) and calls `TickBotAutoBets` → `ExecuteBotBet` for the **bot runners**.
- State is held in **DiceGame fields**: `_session` (the player `AutoBetSession`), `_botRunners`, `_wallet`/`_betService`/`_walletController`, `_engine`.
- Per bet, the flow does: `session.ExecuteNext(...)` → `ProcessBlockchainAttemptForBet(nodeId, …)` (one nonce attempt / mining) → checkpoint + stats.

When you navigate away, Godot's `ChangeSceneToFile` **frees DiceGame** → `_Process` stops and `_session`/`_botRunners` are destroyed. The `CalendarTimeService` autoload may keep advancing the clock (`IsRunning`, `SpeedMultiplier = 100`), but **no bets and no mining happen**, and `_ExitTree` persists the clock; on re-entry DiceGame builds a **fresh** session and reloads time → the **rewind** you observed. The StatusBar looks frozen because balances stop changing.

**Conclusion:** the simulation must live in something that survives scene changes — in Godot, an **autoload**. There is no smaller fix.

---

## 2. Target architecture — `SimulationService` (autoload)

A new autoload `Scripts/Services/SimulationService.cs`, registered in `project.godot`, that **owns the running autobet** and ticks in its own `_Process` (so it runs in every scene):

```
SimulationService (autoload, persistent)
├── owns: DiceEngine, player Wallet + BetService + AutoBetSession, bot runners
├── _Process(delta): if a session is active → pace player bets + bot bets,
│                    each bet → 1 nonce attempt (mining) + checkpoint + stats + balance sync
├── reads/writes the autoload balance services (source of truth) so every scene sees it live
└── public API: StartPlayerAutobet(config), Stop(), IsRunning, snapshots for the UI
```

DiceGame becomes a **view / controller**:
- It builds the strategy `config` from the UI and calls `SimulationService.StartPlayerAutobet(config)` / `Stop()`.
- It **displays** live state read from the service (current bet, balance, last roll, rates) — it no longer owns the loop.
- Entering/leaving DiceGame does nothing to the running simulation; on entry it just binds its UI to the service's current state (no fresh session, no rewind).
- **Manual** single bets stay in DiceGame (they only happen when the player is there clicking).

### What moves into `SimulationService`
`TickAutoBet`, `ExecuteAutoBetOnce`, `TickBotAutoBets`, `ExecuteBotBet`, `StartBotRunners`/`StopAllBotRunners`, the player `_session` + `_wallet`/`_betService`, the autobet accumulators/rate telemetry, the per-bet mining call (`ProcessBlockchainAttemptForBet`), block checkpoints, and stats registration, plus ownership of `CalendarTimeService.IsRunning/SpeedMultiplier/IsAutobetActive` while autobet is active.

### What stays in DiceGame
The whole UI, the strategy panel, manual betting, the MartingaleCalculator popup, and per-node strategy editing — all of which now **drive** the service rather than run the loop. **Manual bets remain DiceGame-only and still advance the bots** (a bot burst per manual bet), but that burst is requested from `SimulationService` so bot state has a single owner. The cross-scene background loop only runs while autobet is active (OQ-1).

---

## 3. Wallet & balance ownership (the key integration)

Today the player `Wallet` is seeded from `BankrollStateService` and mutated in-memory in DiceGame. For the service to run headless and have **every scene reflect balances live**, the **autoload balance services are the source of truth**:

- `SimulationService` holds the player `Wallet` + `BetService`, and **writes resolved balances back to `BankrollStateService` / `PrincipalBalanceService`** as bets settle (and auto-recharge runs) — so `StatusBar` (which reads those services) updates in any scene.
- Bot financial state already lives in `NetworkRoot.NodeFinancialState` (per node) — the service keeps using that, unchanged.

---

## 4. NetworkRoot access (mining)

`NetworkRoot` is a scene node but operates on **static** shared state, and we already exposed static entry points for the bootstrap (`EnsureReady`, `MineNodeStatic`). The service will mine via NetworkRoot the same way DiceGame does (per-bet single-nonce attempt). If needed, add a small static `TryMineSingleNonceAttemptStatic(nodeId, …)` mirroring the instance method, so the service doesn't need a scene-tree NetworkRoot instance.

---

## 5. Block Explorer — "who's mining + speed"

Once the service owns the runners, it can answer "which nodes are actively mining and at what bets/sec." Augment the existing node list (`GetNodeStatusLines` / Network Status) so an actively-mining node shows its rate, e.g.:

```
bot_2 | block: 130 | pending: 1 | balance: 312.5 | ⛏ 2.0/s
player | block: 130 | … | ⛏ 3.0/s
non_miner_4 | block: 130 | …            (no ⛏ — not mining)
```

Source: `SimulationService` exposes per-active-node bets/sec; BlockExplorer reads it and appends the marker. Because the sim now runs in the background, this updates live while you watch.

---

## 6. Phases (sliced for testing)

1. **`SimulationService` + move player autobet.** Because the autobet loop is deeply coupled to DiceGame's UI (`ExecuteBet` touches sliders, strategy panel, result labels, stats, checkpoint, clock), this phase is split into safe, buildable micro-steps:
   - **1a — Autoload skeleton (no behavior change).** Create `Scripts/Services/SimulationService.cs`, register it in `project.godot`, define the `PlayerAutobetConfig` snapshot + public API (`StartPlayerAutobet`, `Stop`, `IsRunning`, a `BetSettled` event) + headless ownership of `DiceEngine`/`Wallet`/`BetService`/`AutoBetSession`. DiceGame still runs its own loop — nothing is wired yet, so the game is unchanged and the build stays green. *Foundation only.*
   - **1b — Move the player loop into the service.** Port `TickAutoBet`/`ExecuteAutoBetOnce`/the per-bet core (decoupled from UI: chance/isHigh/rate/stops come from the config; mining + clock + stats + balance write-through happen in the service; results surface via the `BetSettled` event). Add `TryMineSingleNonceAttemptStatic` to NetworkRoot if needed.
   - **1c — DiceGame delegates.** Start/stop/manual call the service; DiceGame subscribes to `BetSettled` (and polls on entry) to update its UI; remove its own loop. *Test: start autobet, leave DiceGame → BlockExplorer + StatusBar keep advancing; return → no rewind.*
2. **Move bot runners.** `StartBotRunners`/`TickBotAutoBets`/`ExecuteBotBet` into the service (single owner of bot state). The continuous bot ticking runs while the player **autobet** is active (background, across scenes); DiceGame's **manual-bet burst** now calls the service to advance bots one burst. *Test: during autobet, bots keep mining/circulating across scenes; manual bets in DiceGame still drive bot bursts.*
3. **Balance sync + StatusBar live everywhere.** Write-through to balance services; confirm StatusBar updates in all scenes. *Test: balance moves while in MainMenu/BlockExplorer.*
4. **Block Explorer mining indicator.** Per-node `⛏ rate`. *Test: see which bots mine + speed, live.*
5. **Polish:** stop-condition handling while away, app-restart behavior (per OQs), telemetry.

---

## 7. Resolved Decisions (2026-06-21)

**OQ-1 — Bot betting scope (clarified to avoid future confusion).** Bots advance whenever the **player bets** — this includes **manual bets inside DiceGame** (each manual bet drives a bot burst, exactly as today) **and** the autobet loop. The rule *"bots run only while the player's autobet is active"* governs **only the cross-scene background simulation**: once you leave DiceGame, autobet is the *only* way the player is still betting (manual betting exists only inside DiceGame), so background bot activity runs **iff the player autobet is active**. Inside DiceGame, manual bets keep advancing bots normally. To keep a **single owner of bot state**, the manual-bet burst is routed through `SimulationService` too (DiceGame asks the service to advance the bots one burst per manual bet) rather than DiceGame keeping its own separate bot runners.

**OQ-2 — Stop-while-away → silent (starter).** If the player's session hits a stop condition (profit/loss/block) while in another scene, the whole sim stops (bots too) and shows the stopped state on return. A small "autobet stopped: \<reason\>" StatusBar banner is a later polish.

**OQ-3 — Unfocused/minimized → keep running.** The sim keeps going when the window loses focus (simplest; revisit if problematic).

**OQ-4 — App restart → start stopped.** Scene changes keep autobet running (the bug fix); a full app **restart** starts with autobet **stopped** (it's something the player actively starts).

**OQ-5 — Mining indicator → append to the node list.** Append `⛏ <bets/sec>` to the existing Block Explorer node list (no separate panel).

**OQ-6 — Same speed across scenes.** The background sim runs at the same speed regardless of the active scene.

---

## 8. Risks / notes

- **Big refactor of DiceGame** — the loop and session move out; many DiceGame methods become thin calls into the service. Done in slices (phase 1 first) to keep it testable.
- **Double-driving time:** ensure only the service controls `CalendarTimeService.IsRunning/SpeedMultiplier` while autobet runs, so we don't get two owners.
- **Performance:** same per-frame work as today, just not gated to DiceGame; BlockExplorer/StatusBar already refresh per-frame, so no new hotspots expected.
- **Save compatibility:** no blockchain format change; balances already persist via the autoload services.

---

*Created: 2026-06-21 on branch `background-simulation`. Pairs with the DiceGame autobet system and `candidate-block-model-plan.md` (mining).*
