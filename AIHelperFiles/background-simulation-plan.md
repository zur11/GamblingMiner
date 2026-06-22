# Background Simulation — Implementation Plan

**Status**: 🆕 LEAD on branch `background-simulation`. Design + open questions — **answer the OQs before coding**.
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
The whole UI, the strategy panel, manual betting, the MartingaleCalculator popup, and per-node strategy editing — all of which now **drive** the service rather than run the loop.

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

1. **`SimulationService` skeleton + move player autobet.** Create the autoload; move `TickAutoBet`/`ExecuteAutoBetOnce` + player session/wallet + time control; DiceGame calls `StartPlayerAutobet`/`Stop` and displays service state. *Test: start autobet, leave DiceGame → BlockExplorer keeps advancing; return → no rewind.*
2. **Move bot runners.** `StartBotRunners`/`TickBotAutoBets`/`ExecuteBotBet` into the service, tied to the player autobet being active. *Test: bots keep mining/circulating across scenes.*
3. **Balance sync + StatusBar live everywhere.** Write-through to balance services; confirm StatusBar updates in all scenes. *Test: balance moves while in MainMenu/BlockExplorer.*
4. **Block Explorer mining indicator.** Per-node `⛏ rate`. *Test: see which bots mine + speed, live.*
5. **Polish:** stop-condition handling while away, app-restart behavior (per OQs), telemetry.

---

## 7. Open Questions (please answer)

**OQ-1 — Bots tied to player autobet?** Should bot runners run **only while the player's autobet is active** (consistent with the "time follows the player's bets" model), and stop when the player stops? *Recommendation: yes — bots run iff the player autobet is running.*

**OQ-2 — Stop condition reached while in another scene.** If the player's session hits stop-on-profit/loss/block while you're away, the whole sim stops (bots too). Do you want a **notification on return** (toast/badge), or just show the stopped state silently? *Recommendation: stop silently for the starter; add a small "autobet stopped: <reason>" banner on the StatusBar later.*

**OQ-3 — Run while the app is unfocused/minimized?** Keep simulating when the window loses focus? *Recommendation: yes (simplest, matches "keeps running"); revisit if it causes issues.*

**OQ-4 — Resume autobet after an app restart?** Scene-change persistence is the bug we're fixing. But if the player **quits the whole app** mid-autobet, should it auto-resume on next launch, or start stopped? *Recommendation: start **stopped** on app launch (autobet is something you actively start); only scene changes keep it running.*

**OQ-5 — Mining indicator format.** OK with appending `⛏ <bets/sec>` to the existing Block Explorer node list (vs a separate panel)? *Recommendation: append to the existing list — least UI churn, exactly your suggestion.*

**OQ-6 — Speed across scenes.** Run the background sim at the **same** speed as in DiceGame (no slow-down when not viewing it)? *Recommendation: yes, same speed.*

---

## 8. Risks / notes

- **Big refactor of DiceGame** — the loop and session move out; many DiceGame methods become thin calls into the service. Done in slices (phase 1 first) to keep it testable.
- **Double-driving time:** ensure only the service controls `CalendarTimeService.IsRunning/SpeedMultiplier` while autobet runs, so we don't get two owners.
- **Performance:** same per-frame work as today, just not gated to DiceGame; BlockExplorer/StatusBar already refresh per-frame, so no new hotspots expected.
- **Save compatibility:** no blockchain format change; balances already persist via the autoload services.

---

*Created: 2026-06-21 on branch `background-simulation`. Pairs with the DiceGame autobet system and `candidate-block-model-plan.md` (mining).*
