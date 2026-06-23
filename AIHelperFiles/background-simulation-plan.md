# Background Simulation — Implementation Plan

**Status**: ✅ COMPLETE on branch `background-simulation`. **All phases (1–5) implemented, built clean, and user-tested.** Pending: document in `ProjectDesignManual.md` and merge to `main`.
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
   - **1b — Engine in the service.** ✅ DONE (compiles, inert). `SimulationService` owns a persistent `NetworkRoot` child and the per-bet routine (`ExecutePlayerBetOnce`: balance check → `session.ExecuteNext(config.chance, config.high, ts)` → stats → financial-state persist → one nonce attempt → checkpoint + stop-on-block → sync `BankrollStateService` → `BetSettled` signal) + pacing `_Process`. It compiles but **nothing calls it yet**, so the game is unchanged.
   - **⚠️ Hazard found (drives 1c's design).** Handing DiceGame's **live wallet** to the service is unsafe: `Wallet.BalanceDeltaChanged` and `session.OnStopped` are subscribed to DiceGame, so when DiceGame is **freed** on navigation, the next background bet would invoke a disposed node → crash. **Decision: `BankrollStateService` is the single source of truth.** The service owns its **own** wallet/session (seeded from `BankrollStateService` at start, written back each bet); the service's wallet has **no** subscriptions to any scene. DiceGame and the StatusBar display from `BankrollStateService` (already how StatusBar works). On autobet stop / DiceGame re-entry, DiceGame re-seeds its own wallet from `BankrollStateService` (current → no rewind). This avoids all dangling-event crashes and the two-wallet drift.
   - **1c — DiceGame delegates (next).** Adjust `StartPlayerAutobet` to build the session/wallet in the service from the passed strategy config (not hand DiceGame's live objects). DiceGame: start/stop call the service; while autobet runs, DiceGame shows balances from `BankrollStateService` and reflects "running"; on re-entry, query the service for running state. *Test: start autobet, leave DiceGame → BlockExplorer + StatusBar keep advancing; return → running state intact, no rewind.*
2. **Move bot runners.** ✅ DONE. `BotConfig`/`BotRunner`/`StartBots`/`StopBots`/`RunBotManualBurst`/`TickBots`/`ExecuteBotBet`/`TryAutoRechargeBot`/`SaveBotFinancialState` now live in `SimulationService` (single owner of bot state). Continuous bot ticking runs in the service's `_Process` while the player **autobet** is active (background, across scenes); DiceGame's **manual-bet burst** calls `SimulationService.RunBotManualBurst(configs)` (one-shot temp runners). DiceGame keeps only the per-node strategy UI (`_nodeStrategies`) and supplies snapshots via `BuildBotConfigs()`. `_ExitTree` no longer stops bots when `_autobetDelegated`. *Test: during autobet, bots keep mining/circulating across scenes; manual bets in DiceGame still drive bot bursts.* **Note:** fixed a latent gap from 1c — once autobet was delegated, DiceGame's `TickAutoBet` returned before reaching `TickBotAutoBets`, so bots weren't actually ticking; the service now owns that tick.
3. **Balance sync + StatusBar live everywhere.** ✅ Covered by 1c (player) + Phase 2 (bots): the service writes through to `BankrollStateService` / `NodeFinancialState` each bet, and the clock keeps running across scenes, so StatusBar + BlockExplorer update live (BlockExplorer added a 1 s `_Process` auto-refresh).
4. **Block Explorer mining indicator.** ✅ DONE. `SimulationService.GetActiveMiningRates()` returns nodeId → bets/sec for the player + each running bot; BlockExplorer appends `⛏ <rate>/s` to the matching Network Status line (`BuildNodeStatusLinesWithMiningRates`). Updates live via the 1 s auto-refresh.
5. **Polish.** ✅ DONE.
   - **Player auto-recharge while away** (deferred from 1c): `SimulationService.TryPlayerAutoRechargeAndRestart()` — on `InsufficientBalance` with auto-recharge on, transfers from main balance to bankroll (`BankrollProgramService.TryTransferBalanceToBankroll`), registers the deposit, syncs `BankrollStateService`, and restarts the progression from base bet. Works across scenes. `AutoRecharge` added to `PlayerAutobetConfig` (set from `_strategyPanel.AutoRechargeEnabled`).
   - **Stop-reason banner**: `LastAutobetStopReason` + a consumable `StopNoticePending` flag. DiceGame shows `Auto stopped: <reason>` both via the live `AutobetStopped` signal and, if it stopped while the player was elsewhere, on re-entry (`_Ready` consumes the pending notice).
   - **App restart → start stopped** (OQ-4): nothing persists/restores `IsRunning`, so the service starts idle on launch — no code needed.
   - **Node-switch during background autobet**: switching the active node calls `LoadActiveNodeFinancialState`, which *rewrites the shared `BankrollStateService` / `PrincipalBalanceService`* with the selected node's balances — and a running player autobet uses those as its source of truth. So the active-node selector is **locked** (`SetActiveNodeSelectorLocked`) while a background autobet is delegated, with a defensive guard in `OnActiveNodeSelected` (shows "Stop the autobet to change the active node."). *(Earlier this stopped the whole sim on switch — that was wrong; it now stays running and the selector is simply disabled.)*
   - **Bot auto-recharge parity** (post-test fix, **verified working**): the real root cause was that `BaseBetSession.ApplyStopConditions()` (run at the end of every `ExecuteNext`) **self-stops the session with `InsufficientBalance`** the instant the next progression bet exceeds the bankroll — *inside* `ExecuteNext`, before `ExecuteBotBet`'s own bust check could run (which is why no recharge fired and the bot stopped with leftover bankroll, e.g. 60.16 SC). The player works because its recharge is handled in `_Process` *after* the stop. Fix: `TickBots` now mirrors the player — when a bot session stops with `InsufficientBalance`, it calls `TryRechargeAndRestartBot` (top up the bot's bankroll from its main balance via `TryAutoRechargeBot`, repeatedly if a single 100 SC top-up can't cover the base bet, then `RestartBotSessionFromBase`) instead of removing the runner. Bots now recharge and keep mining from any scene, exactly like the player.

---

## 9. Proposal — live bot **SC bankroll** view inside DiceGame (future enhancement)

**Context / constraint (user, 2026-06-23):** the Block Explorer is a **BTC** view — its node "balance" is the on-chain mining balance and must stay that way. **No casino/SC information belongs in the Block Explorer** except the player's own StatusBar. So watching a bot's *SC bankroll* change live belongs in **DiceGame**, not the explorer.

**Why it isn't trivial today:** the active-node selector is overloaded — selecting a node calls `LoadActiveNodeFinancialState`, which **rewrites the global `BankrollStateService` / `PrincipalBalanceService`** with that node's balances (it's a *control* switch, "play as this node"). During a background autobet those globals are the player's source of truth, so switching corrupts the run — hence the selector is locked while delegated.

**Proposed design (decouple *view* from *control*):**
1. Add a small read-only **"Observe node"** panel in DiceGame (separate from the active-node selector), with its own dropdown of bettable node ids. It never touches the global balance services.
2. `SimulationService` exposes a per-node SC snapshot, e.g. `GetNodeBankrollSnapshot(nodeId) → (bankroll, principal, lastBet, betsPerSec, recharges)` read from the live `BotRunner` (or `NodeFinancialState` when idle). Bot runners already hold this live.
3. DiceGame refreshes the observe panel on each `OnSimBetSettled` tick (or its own ~250 ms throttle), showing the observed bot's **SC bankroll** counting up/down in real time, plus a `⛏` marker — all in casino (SC) terms, staying inside DiceGame.
4. The observe panel is purely informational: it does not change the active node, does not stop the sim, and the active-node selector stays locked during autobet as today.

**Effort:** small-to-medium (one new read-only panel + one service getter; no change to the betting/mining core). Deferred to its own slice; not required for the background-simulation feature to be complete.

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
