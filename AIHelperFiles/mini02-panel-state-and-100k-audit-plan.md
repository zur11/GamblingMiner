# Mini-Plan 02 — Strategy panel state, the 100,000-bet audit, and the BetsHistoryExplorer time collapse

**Series note:** second entry of the *mini-plan* series (`miniNN-…-plan.md`), following
`mini01-split-stop-conditions-plan.md`.

**Status:** ✅ **Parts A, B and C COMPLETE** (2026-08-07) — A and C implemented, verified in play and
committed; B fully measured, every check clean, the engine reproduced bet-for-bet. Scope was
**Part A + Part C** plus Part B as analysis. **Part D is DEFERRED to its own plan** (developer's call, 2026-08-07) — it is written up
in full below so nothing is re-derived when it is picked up. Part A reviewed in play and decided
(§A.3.1–§A.3.4). Part B: archive **done**, **checks 1/2/3/6/8/9 measured, all clean** (§B.6) — the
reported max streak of 18 is confirmed real. ·
**Branch:** `strategy-panel-state-and-100k-audit` · **World format bump:** none ·
**Design record (proposed):** `Documentation/ProjectDesignManual.md` new §24.13 (Part A — §24.11 was already taken by the timestamp-collision entry) and a new
§38.8 (Part C — it is another instance of §38.7's inverse failure); Part B's findings land in this
plan's §B.6, and in `Documentation/INCIDENT_LOG.md` **only** if it turns up a real corruption.

Three pieces of work share one branch: Part B may well produce Part-A- and Part-C-shaped findings,
Part C is measured with the same telemetry Part B reads, and none is large enough to justify its
own branch.

---

# Part A — the StrategyControlPanel empties on a scene round-trip

## A.1 What was reported

Configuration fields in DiceGame's `StrategyControlPanel` are blank after leaving the scene and
coming back. `Increase on loss` was the one noticed; a **general review** of every field was
requested rather than a spot fix.

## A.2 Diagnosis — read from the code, to be confirmed in play

The root cause is one line:

> `Screens/DiceGame/DiceGame.cs:87` — `private readonly Dictionary<string, NodeStrategyState> _nodeStrategies = new();`

It is an **instance** field on a scene that Godot frees on every navigation, so it is empty on
re-entry. `_Ready()` calls `LoadActiveNodeStrategySnapshot()` (line 304), which finds no entry for
`_activeNodeId` and takes the `ClearStrategySettings()` branch (line 821) — blanking **every**
field in the panel, not only the percents.

**Why only *some* fields look lost** (this is what makes the report precise rather than vague):
two other paths write panel fields on re-entry and accidentally mask the clear.

| Path | Restores | Line |
|---|---|---|
| `OnSimBetSettled` — fires on every settled background bet | `Number of bets`, `Amount to bet` | 1145–1146 |
| `BindToRunningBackgroundAutobet` — on re-entry with a live autobet | chance slider, HIGH/LOW | 1182–1184 |

Nothing restores `Increase on loss %`, `Increase on win %`, `Stop on profit`, `Stop on loss`,
`Stop Block`, `Insist On Profit`, `Insist On Loss`. Those are exactly the fields that read as
empty — the report matches the code.

**Blast radius — this is not only cosmetic.** Five consequences, in rough order of severity:

1. **`BuildConfig()` reads the live fields.** Pressing AUTO or Bet Once after a scene round-trip
   builds a config from the blanked panel: **flat betting, both stops disarmed, both insists off**,
   at whatever bet amount happens to be showing. A martingale silently stops being one — the same
   failure shape mini-plan 01 D-M1.9 accepted *once*, at a saved-strategy format change, is here
   happening on ordinary navigation.
2. **`SaveActiveNodeStrategySnapshot()` writes the blank config back.** It fires from many UI
   handlers (lines 631, 637, 682, 708, 744, 948, 1197, 1293, 1408, 1424), so the empty state
   becomes the stored per-node snapshot as soon as the player touches anything.
3. **`BuildBotConfigs()` (line 1364) reads the same dictionary** and skips any node without a valid
   entry — after a re-entry it returns an **empty list**, so bot runners started from that point
   have nothing configured.
4. **`RefreshNodeSelectorReadyDots()` (line 375) reads it** — every node reads as not-ready, *even
   while it is actively betting*, and the selector is locked during a run so the dots are the only
   readout left. Confirmed in play; see §A.3.2.
5. **`_activeNodeId` (line 72) is re-initialised to `player`** on entry regardless of which node was
   selected. Reviewed and **accepted as correct** (§A.3.4) — listed here only so it is not
   re-reported later as a defect.

**The rule this is an instance of:** *a scene is a view; state that outlives the view must not live
in it.* The codebase already knows this — `_checkpointRestoreSpentThisSession` (2085) and
`_bootstrapAppliedThisSession` (2091) are both `static` with the comment *"Static so it survives
DiceGame being freed and rebuilt on each scene change"*. `_nodeStrategies` is the same kind of
state and did not get the same treatment.

## A.3 The general review — ✅ CONFIRMED IN PLAY (developer, 2026-08-07)

Every row below was verified by the developer, with three refinements that changed the picture.
**UI** = what the panel shows; **behaviour** = what the run actually does. Separating the two
columns is the whole value of this table — they do not agree, and that disagreement is a second
defect (§A.3.1).

| State | Owner | UI on re-entry | Behaviour on re-entry |
|---|---|---|---|
| Amount to bet | panel | restored *by accident* while an autobet runs; blank when idle | intact (session-owned) |
| Increase on loss % | panel | **lost** | intact (session-owned) |
| Increase on win % | panel | **lost** | intact (session-owned) |
| Number of bets | panel | restored *by accident* while an autobet runs; blank when idle | intact (session-owned) |
| Stop on profit | panel | **lost** | ⚠️ **also lost** — see §A.3.1 |
| Stop on loss | panel | **lost** | ⚠️ **also lost** — see §A.3.1 |
| Stop Block toggle | panel | **lost** (resets OFF) | ✅ **intact** — see §A.3.1 |
| Insist On Profit | panel | **lost** (resets OFF) | intact (session-owned) |
| Insist On Loss | panel | **lost** (resets OFF) | intact (session-owned) |
| Auto Recharge toggle | `BankrollProgramService` | survives by design (§25.8 proxy) | intact |
| Winning chance slider | DiceGame | restored only while an autobet runs | intact |
| HIGH / LOW | DiceGame | restored only while an autobet runs | intact |
| APS / hardware rate | `HardwareAllocationRepository` | survives (read fresh each use) | intact |
| Active node selection | DiceGame | resets to `player` — **accepted, not a defect** | n/a |
| Node ready dots | DiceGame | ⚠️ **all red**, including active nodes — see §A.3.2 | n/a |
| Per-bot strategy snapshots | DiceGame | not directly observable — see §A.3.3 | believed intact |
| Saved named strategies | `user://` repository | survives (personal file) | intact |

### A.3.1 — Why Stop-on-Block survives and the two stops do not

This asymmetry is the most useful thing the review turned up, because it is not about the panel at
all. The three flags live in **different places** at run time:

- **`StopOnBlockMined` is a top-level field on `SimulationService.PlayerAutobetConfig`** (line 32),
  captured once when the run starts (`DiceGame.cs:1061`) and read from `_config` at
  `SimulationService.cs:596` and `:945`. Nothing re-pushes it, so blanking the toggle cannot reach
  the running session — UI lies, behaviour is correct.
- **`StopOnProfit` / `StopOnLoss` live inside `_config.Strategy`**, which the session captures at
  construction (`SimulationService.cs:667`) — so on the same reasoning they *should* also be
  immune, and the developer reports they are **not**. Something is reaching the live session's
  strategy after a scene round-trip, and until that path is named this is an unexplained
  observation, not a diagnosis.

### A.3.1a — Trace result (2026-08-07): the stops CANNOT lose their behaviour mid-run

Traced before writing any fix, per the work order. **The reported behaviour loss is not reproducible
from the code, and the most likely explanation is a misattribution — an honest one.**

- **The player autobet is ALWAYS delegated.** `OnAutoBetToggled` calls
  `_simulationService.StartPlayerAutobet(...)` (`DiceGame.cs:1053`) unconditionally; DiceGame's local
  `_session` serves **manual** bets only and is inert while delegated.
- `SimulationService` captures `_config.Strategy` once at start and constructs the session from it
  (`:667`). **Nothing re-pushes it** — not `OnSimBetSettled`, not `BindToRunningBackgroundAutobet`,
  not the auto-recharge restart (which reuses the same `_config.Strategy`). So a blanked panel cannot
  reach a running session's `StopOnProfit`/`StopOnLoss` any more than it can reach its
  `StopOnBlockMined`.
- Candidate 1 (`OnStrategyConfigChanged` firing through the `_loadingNodeStrategy` guard) does not
  apply either: `ClearStrategySettings` deliberately does **not** raise `StrategyConfigChanged`, and
  the one path that does is held under the guard.

**The reconciliation — and §B.6.5 supplies it.** In 105,049 bets of the audited run, **no stop ever
fired**: every loss grew the bet, every win reset it, without exception. With base bet `0.000001` a
full 18-deep ladder costs ~4.6 SC against a 100 SC bankroll, so any plausible threshold was simply
never reachable. The stops were therefore *never observed firing* — before or after a scene change —
and a blank panel makes "they stopped working" the natural reading. **Unarmed, unreachable, and
silently-disarmed all look identical from the outside.**

**What was genuinely broken here, and is now fixed:** the **manual** bet path read
`StopOnBlockMined` live off the panel (`DiceGame.cs:1489`, `:1846`) while the delegated path read it
from `_config` — **the same flag with two sources**, disagreeing exactly when a round-trip had
blanked the panel. That is a real defect, independent of how the stops question resolves.

**D-M2.8 — a running session's parameters come from the session, never from the panel.** The panel
is an *editor* for the next run, not a live control surface for the current one. Applied to both
sites above via the new `BaseBetSession.SessionConfig`. After this change **no panel field can reach
a running session at all**, so if the reported stop behaviour was real, its last possible mechanism
is closed too.

*To settle it definitively rather than by elimination:* set `StopOnLoss` to a value the ladder
actually reaches (with base `0.000001`, something under ~1 SC), start an autobet, confirm it fires,
leave to BlockExplorer and return, and confirm it still fires. That is the one test the 100k run
could not perform.

### A.3.2 — The ready dots lie, and they are the only readout left

`RefreshNodeSelectorReadyDots` (`DiceGame.cs:375-388`) colours a node green iff `_nodeStrategies`
holds a valid entry for it — so with the dictionary emptied by the scene change, **every node goes
red, including nodes that are actively betting**. This matters more than a normal cosmetic slip:
the node selector is **disabled while an autobet session is running** (`SetActiveNodeSelectorLocked`),
so during a run the dots are the *only* per-node readout on screen, and they are wrong.

**D-M2.9 — a node that is ACTIVELY RUNNING shows green, unconditionally, player included.**
"Ready" (has a valid stored strategy) and "running" (is actually betting right now) are two
different questions and the dot currently answers neither correctly. The predicate becomes
*running-or-ready*, with the running half sourced from `SimulationService` — the same source that
decides whether the node is really betting. This is §39.16 rule 6 (*a displayed signal must share
its source with the action it advertises*) and the same principle as D-M2.2: **where a live session
exists, it is the truth.**

### A.3.3 — Per-bot snapshots: not observable, so state the acceptance criterion instead

The per-bot snapshots cannot be inspected during a run at all, because the selector is locked. The
developer's read is that bot behaviour is preserved (bots keep betting across the scene change),
which is consistent with the code: `SimulationService` holds its own `BotConfig` list from
`StartBots`, independent of `_nodeStrategies`.

So the requirement is not "verify during the run" but: **when the session stops and the selector
unlocks, selecting a bot must show that bot's configured strategy, not a blank panel.** That is
what D-M2.1 delivers, and A.5 tests it that way. The `BuildBotConfigs()` hazard from §A.2 point 3
is unchanged and still real — it bites on the *next* `StartBots`, not the current run.

### A.3.4 — Accepted as correct

`_activeNodeId` resetting to `player` on scene entry is **deliberate and stays** (developer's call).
Only the dots beside the selector were wrong, not the selection itself.

## A.4 The fix — decided (2026-08-07)

- **D-M2.1 — `_nodeStrategies` becomes `static`** (process-lifetime, not scene-lifetime).
  `_activeNodeId` stays as it is — resetting the *selection* to `player` on entry is accepted
  behaviour (§A.3.4); only the dots beside it were wrong. In-memory, survives scene changes, dies with the process. Exactly the existing
  idiom in this same file (`_checkpointRestoreSpentThisSession` / `_bootstrapAppliedThisSession`,
  both static *for this reason*; `NetworkRoot` holding the whole world in statics; "between-block
  navigation saves are in-memory only", Pattern 2). It does **not** survive an app restart — which
  is correct, not a shortfall: a restart reverts the world to the last mined block anyway, so a
  panel config outliving that would describe a run that no longer exists.
- **D-M2.2 — the panel hydrates from the LIVE SESSION when one is running.** In
  `BindToRunningBackgroundAutobet`, refill from `SimulationService.CurrentConfig.Strategy` (+
  `NumberOfBets`, and the chance/HIGH-LOW it already restores). While a session runs, the session is
  the truthful source: the snapshot is what was **configured**, the session is what is
  **executing**, and today they can already disagree — the static dictionary alone would show the
  configured one and quietly call it current. **Where both are available, the executing config
  wins.**
- **D-M2.3 — `user://` persistence is DEFERRED, not rejected.** Persisting the last-used panel
  config as a personal file (the saved-strategy repository's precedent, exempt from the world-reset
  delete list) would survive an app restart too. Not built here: it needs a real decision on whether
  "last used config" is *personal* state or *run* state, and D-M2.1 + D-M2.2 close the reported
  defect without it. Recorded so the next person doesn't rediscover the option as if it were new.

**One guard rail, independent of the above:** a blank or invalid panel must never silently build a
degraded config. Either the fields are restored, or `BuildConfig()`'s consumers refuse to start —
a run that quietly turns into flat betting with both stops disarmed is the worst of the three
outcomes, and it is what happens today.

## A.5 Verification (developer, in-editor — no headless launch)

- Configure the full panel (both percents, both stops, both insists, Stop Block, chance, HIGH/LOW),
  leave to BlockExplorer, come back **while idle** ⇒ every field is exactly as left.
- Same, but leave and return **with a background autobet running** ⇒ same, and `Amount to bet`
  tracks the live session's current bet as it does today.
- **Stop Block toggle shows ON after the round-trip** when the run was started with it on — the UI
  now agrees with the behaviour that was already correct (§A.3.1).
- **Both stops still fire after a round-trip**, at their configured thresholds — the behaviour half
  of §A.3.1, and the one that was actually broken. Test with a threshold small enough to hit quickly.
- **Ready dots**: with a run in progress, leave and return ⇒ every actively-betting node is **green,
  the player included**, with the selector still locked (D-M2.9). Stop the run ⇒ dots fall back to
  "has a valid strategy" and the selector unlocks.
- Configure a bot, switch to the player, leave, return, **stop the session**, select that bot ⇒ its
  strategy is shown in full, not a blank panel (§A.3.3's acceptance criterion). Then start the
  runners ⇒ that bot actually starts.
- Return to DiceGame and immediately press AUTO without touching anything ⇒ the run uses the
  configured progression, not flat betting.
- Auto Recharge still mirrors `BankrollProgramService` (the §25.8 proxy must not regress).

## A.6 The run lock — D-M2.14 (2026-08-07, developer-reported during A.5)

Reported as "the Insist On Profit toggle is not disabled while an autobet session is active, and it
seems to happen after leaving DiceGame and re-entering". The review it prompted found the problem is
**general, and is the direct consequence of D-M2.8**: once no panel field can reach a running
session, every configuration control left enabled during a run is a **lie** — clickable, and inert.

The panel had **no concept of a run in progress at all**. The only run-aware disabling anywhere was
`SetManualEnabled` / `SetBettingControlsEnabled` (the two bet buttons) and the node selector. The
Insist toggles merely *looked* correct before, because a blanked panel has no stop amount and
`UpdateInsistToggleAvailability` greys a toggle whose amount is empty — so D-M2.2's hydration, by
restoring the amounts, revealed a gap that had been masked by a different bug.

**D-M2.14 — while a player session runs, every control whose value the session CAPTURED is locked;
controls the session RE-READS stay live.** The split is the whole point — a blanket lock would be as
untruthful as no lock, in the other direction.

| Locked during a run | Left enabled |
|---|---|
| Bet amount + MAX / MIN / X2 / ÷2 | **Hardware / APS** — `SimulationService.HardwareRate` reads the allocation fresh each use |
| Increase on loss %, Increase on win % | AUTO/STOP, PAUSE — they control the run, not its configuration |
| Number of bets (becomes a live readout) | Save strategy — records what is on screen, which during a run *is* what is running |
| Stop on profit, Stop on loss | |
| Stop Block, Insist On Profit, Insist On Loss | |
| Winning chance, HIGH/LOW | |
| **Load** strategy — would rewrite the panel without touching the session | |
| **Auto Recharge** — see below | |

**Auto Recharge is locked too** (developer's call after verifying A.5). It was initially left enabled
as the one genuinely live control, but it is only a **proxy** to
`BankrollProgramService.AutoRechargeEnabled`, whose canonical home is the Bankroll Programmer (§25.8)
— so locking it removes a mid-run *edit point*, not the capability. The result is a panel with one
consistent meaning during a run: **it describes the run**, and account-level flags are changed from
the account's own screen.

This is also what turns A.6a from a tidy-up into a **load-bearing** fix: with the DiceGame proxy
locked, a mid-run change necessarily arrives from the Bankroll Programmer, so the live service flag
must be the *only* gate — the captured `_config.AutoRecharge` that used to sit in front of it would
now silently ignore that screen entirely. *Removing an edit point raises the requirement on the
remaining one.*

Implementation: one writer per side — `StrategyControlPanel.SetRunLocked` for the panel's own
controls, `DiceGame.ApplyRunLock` for the DiceGame-owned ones — called at **all four** transitions
(start, manual stop, self-stop, and **re-binding on scene entry**, which is the one that was
missing). The flag is **composed inside** `UpdateInsistToggle` / `ApplyStrategyModeRestrictions`
rather than assigned alongside them, because those are already the single writers of their controls'
`Disabled` state and a second writer would drift them apart.

### A.6a — The half-live toggle found by asking "does this control still work?"

Auditing which controls genuinely survive into a run turned up a real defect in the one being kept
enabled. `TryPlayerAutoRechargeAndRestart` gated on **both** the captured `_config.AutoRecharge`
**and** the live `BankrollProgramService.AutoRechargeEnabled`, so mid-run the toggle worked in one
direction only: switching it **OFF** took effect, switching it back **ON** did not. §25.8 makes the
service flag the single source of truth and CLAUDE.md already states "the service flag wins" — the
captured copy contradicted both. The captured gate is removed; `_config.AutoRecharge` still records
how the run was started but no longer decides anything. Bots are untouched
(`TryRechargeAndRestartBot` keeps its own per-node flag).

*The general rule: **before leaving a control enabled during a run, verify it still does something —
"enabled" is a claim.** The lock and this fix are the same question asked in both directions.*

---

# Part B — auditing the 100,000-bet run

## B.0 Archive the run FIRST — ✅ DONE 2026-08-07

**Archived to `%APPDATA%\Godot\GamblingMiner_100k_run_2026-08-07\` — 62 files, 29.38 MB.** The live
directory was read only, never written. Everything below runs against the archive.

What was captured, from `%APPDATA%\Godot\app_userdata\GamblingMiner\` (last written 08:47) —

- `bet_history.jsonl` + `bet_history_000001.jsonl` … `bet_history_000010.jsonl` (11 chunks,
  ~2.83 MB each, **~30 MB total**)
- `logs/difficulty_trace.csv`, `logs/network_population_trace.csv`, `logs/founders_trace.csv`,
  `logs/godot.log`
- `block_session_checkpoint.json`, `hardware_allocation.json`, `blockchain/`

Continued play extends these, a restart rolls the journal back to the last mined block, and the
traces are in the world-reset delete list — which is why this was done before anything else, on the
P15.8 precedent (§10 of the step15 plan) and CLAUDE.md's *"Auditing a playtest run"* rule.

## B.1 The question actually being asked

Earlier long runs reported an impossible max-loss streak (>100 consecutive losses at 50% chance,
probability 2⁻¹⁰⁰) — **INC-002**. Its cause was *not* the dice engine: duplicated journal records,
landing adjacent because bet timestamps collide heavily and `OrderBy` is stable, **multiplied** the
streak (measured 12 → 36 on the archived journal). The fix — dedup by `BetRecord.Id`, per-
`(GameId, Chance)` segmentation, the renamed metric and a DEBUG tripwire — shipped at `99582bb`.

This run is **the first long one after that fix**, and it was deliberately run at high frequency
(**5 hardware credits ⇒ 5 bets per game-second**, `SimulationService.HardwareRate`) precisely to
stress the timestamp-collision mechanism that did the amplifying. It reports **max streak 18 over
100,000 bets**.

So the audit has one primary question and one secondary one:

1. **Is 18 real?** — i.e. is the metric now measuring what it claims, on data that is now clean.
2. **Did the high frequency leave any residual distortion?** — the collisions still happen; only
   the amplification was supposed to die.

## B.2 Checks

1. **Integrity.** Duplicate `BetRecord.Id` count (must be **0** — this is the direct test of the
   `99582bb` fix on data produced after it); total record count vs. the reported 100,000; no records
   lost or doubled at chunk boundaries; balance continuity
   `BalanceAfter[i-1] + NetAmount[i] == BalanceAfter[i]` to the satoshi across the whole run,
   including across chunk boundaries.
2. **The streak claim, recomputed independently.** Max consecutive losses per `(GameId, Chance)`
   segment, computed from the archive rather than read off the panel; compare against the reported
   18 and against the bound `log(n)/log(1/p)` ⇒ at n≈100,000, p=0.5 that is **≈16.6**, so 18 is
   entirely ordinary. Report the **whole run-length distribution** against the geometric
   expectation, not just the maximum — a single number can be right by luck, and the distribution
   is what actually proves the sequence is clean.
3. **The frequency hypothesis — the developer's real question.** Measure **timestamp collision
   density** (bets per distinct `TimestampUtc`; INC-002 measured ~3.1 at the old rate, and 5 credits
   should push it higher). Then show the streak is **invariant to sort order**: recompute it (a) in
   file order, (b) in `OrderBy(Timestamp)` order, (c) with records shuffled *within* each timestamp
   group. Three equal answers ⇒ the collision amplification is genuinely dead, and the density
   number quantifies how hard the test was.
4. **Win rate in σ.** Observed wins vs. `chance/100`, deviation expressed in standard deviations
   (INC-002's control measurement got 0.5001 over 1,081,554 bets).
5. **Engine arithmetic parity on a slice.** Replay ~10,000 bets with the engine's *exact* arithmetic
   — BigInt satoshis, `Money.Normalize` = truncation (`MidpointRounding.ToZero`),
   `CalculateMultiplier = Round(100×RTP/chance, 4)`, `BetService`'s `_pendingFractionalProfit` carry
   — and require bet-for-bet reproduction, as mini-plan 01 round 3 did over 684 bets. **Blocked on
   §B.5's parameters**: the strategy config cannot be inferred from the journal, and mini01 round 4
   is the standing lesson about guessing it.
6. **Strategy behaviour at scale.** Count profit-stop firings, loss-stop firings, insist resets,
   §25.5 bankroll-limit resets and auto-recharges; report max bet reached vs. base bet. Round 4's
   segment rule (§25.13) predicts the max bet stays near the ladder depth the loss threshold
   implies — over 100,000 bets that is a far stronger test than the 684-bet run it was derived from.
7. **Scale and limits — INC-001 follow-through.** ~30 MB / 11 chunks at 100,000 bets is ~2.8 MB and
   ~9,000 records per chunk. Report boot-time journal load cost and, specifically, whether the
   rotation bounds **what is loaded per boot** or only what is written — INC-001's 1.13 GB failure
   was a *load* cost, and a rotation that keeps loading every chunk has not fixed it.
8. **Block pace / R2 regulator.** From `difficulty_trace.csv`: mean solvetime vs. the 58,500 s
   target, the `simSecOffered`/`simSecConsumed` retention ratio (§38.7's diagnostic — below 1 means
   "find what is eating the frame", never "raise the caps"), and any `configuredPower > 2×
   realizedPower` tripwire hits. This is the first long run at 5 credits, so it is also the first
   real read on R2 since P15.8 measured 0.713 retention.
9. **`godot.log`.** Every `GD.PrintErr`, assertion and tripwire over the run — including
   `AssertLossRunIsPlausible`, which should have stayed silent, and P15.9's clamp warning. **A clean
   log is a result worth stating explicitly**, not an absence of findings.

## B.3 Tools

`awk` over the `.jsonl` / `.csv` (no Python — see CLAUDE.md's scripting-tools section); `node -e`
for the JSON state files; a throwaway `dotnet run` console project in the scratchpad for anything
that must match the engine's `decimal` arithmetic. Reproducing the engine *approximately* is what
turns real single-satoshi evidence into ~120 phantom mismatches — the mini01 round-3 lesson.

## B.4 What this audit is NOT

Not a tuning pass. Findings are recorded and, if any is a genuine defect, fixed on this branch;
parameter changes to the strategy system, the regulator or the retention policy are a **separate
decision**, on D-15.33's precedent (don't price a placeholder before the mechanism has been seen
firing).

## B.5 Run conditions — answered by the developer, 2026-08-07

1. **Progression: `Increase on loss` only**, "a bit over 100%", exact value not remembered and never
   changed during the run; `Increase on win` unused. ⇒ **derived from the data instead** (§B.6.2).
2. **One continuous autobet.** The app was never closed and the session never stopped; DiceGame may
   have been left and re-entered. ⇒ the journal covers the whole run with no pre-genesis rollback,
   **and a Part A scene round-trip plausibly happened mid-run** — §B.6.5 tests what that did.
3. *(Question withdrawn — it was asking where the "18" was read from, which no longer matters: the
   figure was recomputed independently from the archive, §B.6.3.)*
4. **No bots bet** — none were configured to. Every record is the player's.
5. **All 5 hardware credits on the player node for the whole run**, private pool.

Remaining un-inferable: the **stop thresholds and Insist toggles**. §B.6.5 shows no stop ever fired,
which is consistent with either "unarmed" or "armed but never reached" — the data cannot separate
them, and it does not need to for any conclusion below.

## B.6 Findings — measured 2026-08-07 against the archive

Read-only over `%APPDATA%\Godot\GamblingMiner_100k_run_2026-08-07\`, using `awk` per CLAUDE.md.

### B.6.1 Integrity (check 1) — ✅ clean

| | |
|---|---|
| Files | 11 chunks of 10,000 records + a final 5,050 |
| Total lines | **105,050** — 105,049 bets + 1 `deposit` record (the opening 100.00 SC bankroll funding, `2009-03-21T17:38:45.84Z`) |
| **Duplicate `BetRecord.Id`** | **0** — 105,050 lines, 105,050 distinct Ids |
| Chunk ordering | contiguous by timestamp; `bet_history.jsonl` is the OLDEST chunk, `…_000010` the newest |
| Span | `2009-03-21 17:39:07` → `2009-04-15 06:48:58` game time (~24.5 in-game days) |

**Zero duplicates is the direct verification of the `99582bb` fix** on data produced after it —
INC-002's root cause is gone at the source, not merely filtered at the reader.

*Note for whoever writes the next pass: a naive `grep`/`awk` over these files must filter
`"Type":"bet"`. The one deposit record has `"Bet":null`, and an unfiltered numeric extraction silently
reads it as a bet with `Chance:0`, `BetAmount:0` and `Outcome:0` — i.e. a phantom win that also
splits whatever loss run it lands in.*

### B.6.2 Strategy parameters, derived (check 5 prerequisite)

| Parameter | Derived value | How |
|---|---|---|
| Base bet | **`0.00000100` SC** | minimum bet, and the value every post-win bet returns to |
| `IncreaseOnLossPercent` | **127%** (×2.27) | modal next/prev ratio after a loss = `2.270000` (26,241×). The lower variants (`2.268722`, `2.269903`, …) are `Money.Normalize` **truncation** at 8 decimals on tiny bets, and converge to exactly 2.27 as the ladder grows — truncation-below, never above, which is itself a consistency check on the engine |
| `IncreaseOnWinPercent` | **0** | 0 bets grew after a win, out of 52,695 |
| Chance / direction | **50%**, LOW (`IsHigh:false`), multiplier `1.9804` | constant across all 105,049 bets |

So the run is a clean two-parameter martingale: **base `0.000001`, ×2.27 on loss, reset on win.**

### B.6.3 The streak (check 2) — ✅ 18 confirmed, and the distribution is textbook

Recomputed from the archive in settle order: **max consecutive losses = 18**, independently
reproducing the reported figure. Bound `log₂(105,049) = 16.68`, so 18 is ~1.3 above the expected
maximum — ordinary for the max of ~26k geometric runs.

The **whole distribution** matters more than the max (a single number can be right by luck).
Observed 26,217 maximal loss runs vs. `n·p·q = 26,262` expected, and per length against
`R × 0.5^k`:

| len | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 | 11 | 12 | 13 | 14 | 15 | 16 | 17 | 18 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| observed | 13322 | 6423 | 3166 | 1600 | 865 | 404 | 211 | 114 | 51 | 26 | 14 | 11 | 5 | 2 | 0 | 1 | 1 | 1 |
| expected | 13108 | 6554 | 3277 | 1639 | 819 | 410 | 205 | 102 | 51 | 26 | 13 | 6 | 3 | 1.6 | 0.8 | 0.4 | 0.2 | 0.1 |

Win rate **0.501633** (52,696 / 105,049) = **+1.06σ**. The engine is behaving.

### B.6.4 The frequency hypothesis (check 3) — ✅ answered, with a caveat worth keeping

**Collision density: 9.41 bets per distinct timestamp** (105,049 bets over 11,158 distinct
`TimestampUtc` values) — **3× denser than the ~3.1 that INC-002 measured**, exactly the stress the
5-credit run was meant to apply.

Streak under three orderings:

| Ordering | max loss run |
|---|---|
| settle order (= file order = stable `OrderBy` order) — **the truthful one** | **18** |
| shuffled within each timestamp group, seed 1 / 2 / 3 | 15 / 17 / 14 |

**Conclusion: the amplification mechanism is dead.** INC-002 *multiplied* the streak (12 → 36) because
duplicate records landed adjacent; here reordering moves it by ±2–4 in **both directions and never
upward**, which is the ordinary sampling noise of a max statistic, not an artifact.

**The caveat, stated rather than buried:** the metric is **not order-independent among same-timestamp
bets**, and at 9.41 bets per timestamp that is a real ±2–4 sensitivity. It is honest today because
the stable sort preserves settle order — *the correctness depends on `OrderBy`'s stability*, which
nothing asserts. Worth a line in §40.8 rather than a code change.

### B.6.5 Strategy behaviour at scale (check 6) — and what it does NOT tell us

| Transition | Count |
|---|---|
| after a **loss**, bet **grew** | 52,353 |
| after a **loss**, bet **reset to base** | **0** |
| after a **win**, bet **reset to base** | 52,695 |
| after a **win**, bet **grew** | 0 |

Every loss grew the bet and every win reset it, without a single exception in 105,049 bets. So over
the whole run: **no `StopOnLoss` fired, no `StopOnProfit` fired, no §25.5 bankroll-limit reset, and
no auto-recharge restart.** Max bet `2.55877137` = base × 2.27¹⁸, reached at bet #61,232 — the ladder
ran its full 18-loss depth uninterrupted, which is *why* the max bet and the max streak agree.

⚠️ **Therefore this run is NOT evidence about §A.3.1.** The tempting inference — "the developer
re-entered DiceGame mid-run (§B.5.2), and no stop ever fired, so the stops were silently disarmed by
the round-trip" — **does not hold**: with a base bet of `0.000001` a whole 18-deep ladder costs
~4.6 SC on a 100 SC bankroll, so any plausible threshold was simply never reached. Unarmed and
armed-but-unreachable leave an identical trace. §A.3.1 still needs its own targeted test.

Totals: wagered **9.80150397 SC**, net **+1.10424103 SC**, balance `100.00 → 101.10` (range
`98.456 … 101.104`).

### B.6.6 Block pace, R2 and sim retention (check 8) — ✅ regulator on target; one telemetry gap

`logs/difficulty_trace.csv`, 35 rows, blocks **113 … 147 contiguous**:

| Metric | Value | Read |
|---|---|---|
| mean solvetime | **60,539 s** vs the 58,500 s target | **1.03× — the R2 regulator is on target.** P15.8's +6.6% is now +3.5% |
| blocks over 2× target | 7 / 35 (20%) | normal — solvetimes are ≈exponential, `P(>2×mean) = e⁻² ≈ 13.5%` |
| **sim retention** (`simSecConsumed/simSecOffered`) | **0.6304** | the engine retained 63% of offered sim-time — below 1.0, so the frame is partly saturated even in ordinary play (P15.8 measured 0.713) |
| `configuredPower > 2× realizedPower` | 7 / 35 rows, never 3 consecutive | consistent with the R2-ASSERT tripwire not firing (§B.6.7) |

**This is the baseline Part C needs.** Normal-play retention is **0.63**; if `BetsHistoryExplorer`
really collapses 9000X to ~100X, retention there must be ≈0.011 — a **~57× drop**, which is an
unmistakable signal rather than a judgement call. C.4 step 1 now has a number to compare against.

~~⚠️ **Telemetry gap worth fixing separately:** the trace starts at block 113…~~ — **RETRACTED
2026-08-12, this was a misreading.** `AppendDifficultyTrace` is called for **live blocks only**
(`NetworkRoot.cs:2015`, and the comment says so); the historical bootstrap's blocks are deliberately
untraced. So a trace beginning at ~113 is the *first player-era block*, not a truncation. Confirmed
by the second run, whose bootstrap ended one block later and whose trace duly starts at **117**.
*Two runs agreeing on an "anomaly" is the cheapest possible test that it is a feature — worth
reaching for before filing a defect against telemetry.*

### B.6.7 The engine log (check 9) — ✅ clean, and that is the result

`logs/godot.log`, 1,199 lines. The **only** warning in the entire run is Godot's own
`Your video card drivers are known to have low quality OpenGL 3.3 support, switching to ANGLE.`

Zero `GD.PrintErr`, zero exceptions, zero tripwires across 105,049 bets and 147 blocks —
specifically including `AssertLossRunIsPlausible` (INC-002's guard, which would have fired above
~32 at this n), the R2 `configuredPower > 2× realizedPower` 3-block assert, P15.9's out-of-band
ballot clamp warning, and P15.10's `shortfall_dust`. Per D-15.34, a silent log is a measurement:
**nothing in the engine noticed anything wrong during this run.**

### B.6.8 Engine arithmetic replay (check 5) — ✅ EXACT, 105,049/105,049

Replayed with a throwaway `dotnet run` console project against the archive (the prescribed tool: C#
`decimal` is the only faithful model — `Money.Normalize` = `Math.Round(v, 8, MidpointRounding.ToZero)`,
`CalculateMultiplier` = `Round(100×RTP/chance, 4)`, and `BetService`'s `_pendingFractionalProfit`
carry). Parameters: base bet `0.000001`, `IncreaseOnLossPercent = 127`, no win-side percent — derived
from the data and **confirmed correct by the developer**.

| Check | Result |
|---|---|
| Multiplier | **105,049 / 105,049** exact |
| Credited profit (incl. remainder carry) | **105,049 / 105,049** exact, to the satoshi |
| Bet ladder | **105,049 / 105,049** exact |
| Balance continuity | **105,048 / 105,048** exact |

Two facts fall out of *how* it reproduced, neither of which is recorded anywhere else:

- **A SINGLE continuous remainder carry reproduced all 105,049 bets.** `BetService` is constructed
  once per `StartPlayerAutobet`, so an unbroken carry proves the session was **never restarted** —
  independently confirming the developer's "one continuous autobet" answer, from a quantity nothing
  persists. (The same cross-validation trick as mini-plan 01 round 3, used here to confirm rather than
  to locate.)
- **Ladder resets to base following a LOSS: 0.** The ladder reset *only* on wins, across the whole
  run — so no stop fired, no insist reset ran, and no auto-recharge occurred, **ever**. This is the
  rigorous version of §B.6.5's claim, and it is what makes the §A.3.1a reconciliation solid rather
  than merely plausible: the stops were unreachable, not disarmed.

Consistency: max ladder depth **18** (= the reported max streak, matching §B.6.2's independent
recount), max bet **2.55877137 SC** — exactly `0.000001 × 2.27¹⁸`.

#### B.6.8a — The first run failed, and the bug was in the AUDIT

The replay initially reported ~63k ladder mismatches and ~93k balance breaks. The cause was
`List<T>.Sort` — **introsort, which is not stable** — applied to records of which ~3 share every
timestamp. It shuffled colliding bets into an arbitrary order and destroyed the very sequence under
test. **This is INC-002's mechanism met from the other side**: there a stable sort concentrated
duplicates and inflated a streak; here an unstable sort scrambled clean data into apparent
corruption.

The journal's **file order is the settle order** and is chronologically non-decreasing (now asserted
by the replay rather than assumed). Two things follow: *verify an ordering, never impose one* — and,
usefully, this independently validates the Part C assumption that new records append in chronological
order, which is what makes `TryAppendNewRecords` safe.

### B.6.9 Closed / superseded

- **Check 7** — the journal's *load* cost. Superseded by **Part D**, which turns it from an audit
  check into a design change.

---

# Part C — time collapses to ~100X inside BetsHistoryExplorer

## C.1 What was reported

With the DEV time scale at **9000X**, entering `BetsHistoryExplorer` drops the apparent rate to
**~100X** for as long as that scene is open, and it returns to 9000X on going back to Main Menu,
DiceGame or BlockExplorer.

## C.2 Diagnosis — the obvious suspect is NOT the cause

The natural first guess is that the scene writes the clock: `BetsHistoryExplorer` does set
`_calendarTimeService.SpeedMultiplier` in three places (lines 85, 370, 378), and CLAUDE.md is
explicit that while a session is delegated `SimulationService` is the **sole owner** of
`SpeedMultiplier`/`IsRunning`/`IsAutobetActive`. But the numbers rule it out:

- `_speedSteps = { 100, 200, 400, 1000 }` (line 32), so `_speedSteps[0]` is **100** — byte-identical
  to the `GameSecondsPerRealSecond = 100` that DiceGame and `SimulationService` set. Writing it is a
  no-op.
- The `_Ready` write (line 85) is inside `if (!_liveMode)`, and `_liveMode = IsAutobetActive` — with
  an autobet running it is `true`, so that branch never executes.
- `DevTimeScale` lives on the `CalendarTimeService` **autoload** and no scene resets it.

**So nothing is changing the requested rate — which means the game is not *keeping up* with it.**
That is `SimulationThrottle` (R2-C1) doing exactly its job: the clock advances by the sim-time the
bet engine actually **retained**, so a frame starved by UI work slows game time in wall-clock terms
rather than letting time outrun the mining it represents. **The reported "100X" is a measurement of
frame starvation, not a speed setting** — §38.7's rule, third bullet: *a displayed throttle is a
MEASUREMENT, not a diagnosis.*

Two candidate frame-eaters, both in this scene, both scaling with exactly the things that are now
large (a 100,000-record history and a 9000X clock):

1. **`OnLiveStatsChanged` re-sorts the ENTIRE history, up to 4×/second** (lines 110–117). It is
   subscribed to `UserStatsService.StatsChanged`, which is throttled to 250 ms — a throttle sized
   for a cheap UI refresh, not for
   `BetHistory.Records.OrderBy(r => r.TimestampUtc).ToList()` over 100k+ records, which is an
   O(n log n) sort **plus** a fresh 100k-element list allocation, four times a second, on the main
   thread. This is the `CasinoCoinSwapService` shape verbatim: a correct event, a rate nobody
   re-checked, expensive work behind it.
2. **`_Process` re-renders on every changed GAME second** (lines 141–148), which at 9000X is
   **every frame**. Each render rebuilds a 260-entry preview and repopulates **two** UI containers
   (`PreviousWinnerNumbersGrid` + `BetHistoryContainer`) — up to ~520 entry updates per frame — and
   runs `AdvanceSummaryTo`. The cadence is denominated in game time, so raising `DevTimeScale`
   *directly multiplies the UI cost*.

Both are consistent with the report's shape: the collapse appears only in this scene, only when the
history is big, and it scales with the DEV time scale.

## C.3 Decisions

- **D-M2.4 — measure before fixing.** The two suspects above are read from the code, not from a
  profile. Confirm with numbers first: `CalendarTimeService.SimulationThrottle` while the scene is
  open vs. in DiceGame, and `difficulty_trace.csv`'s `simSecOffered`/`simSecConsumed` columns over
  the same window (§38.7's own diagnostic). *If the throttle is ~1.0 in that scene, this whole
  diagnosis is wrong and the cause is elsewhere* — say so and start over rather than fixing a
  suspect that measured innocent. **Do not "fix" it by raising `MaxBacklogSeconds` /
  `MaxBetsPerFrame`** — §38.7's second rule: that hands a saturated frame more work.
- **D-M2.5 — a UI refresh cadence must be denominated in REAL time, never game time.** Any
  per-frame or per-game-second rebuild becomes unbounded the moment the DEV time scale moves. The
  fix for (2) is a real-time throttle (the `UserStatsService.EmitStatsChangedIfNeeded` 250 ms
  reference pattern), *plus* the existing "has the rendered window actually changed" guard — not one
  or the other.
- **D-M2.6 — the live-mode subscription must not re-sort.** `Records` is already appended in
  chronological order in practice; the fix is to stop rebuilding a sorted copy per event (append
  incrementally, or re-sort only when the *rendered window* changes, or drop the subscription in
  live mode and let the real-time-throttled `_Process` drive). Which one is chosen depends on C.4's
  measurement of where the time actually goes.
- **D-M2.7 — the three `SpeedMultiplier` writes get cleaned up anyway.** They are no-ops today only
  because `_speedSteps[0]` happens to equal the base rate — a latent violation of the sole-owner
  rule that will bite the day either constant moves. The live-mode branch of `OnSpeedButtonPressed`
  (line 370) writing the clock at all, while advertising `"1x (Live)"`, is the clearest case.
  *Being harmless by numeric coincidence is not the same as being correct.*

## C.4 Work

1. ✅ **Instrumented (2026-08-07).** `StatusBar` gained a **`Sim: NN%`** cell showing
   `CalendarTimeService.SimulationThrottle` — the fraction of last frame's simulated time the bet
   engine actually retained. It renders **only while a sim is running** (the value is a meaningless
   `1.0` otherwise) and turns **amber below 90%**. The StatusBar was the right host precisely because
   it is instantiated in *every* scene: the question is a **comparison between scenes**, and until now
   the throttle was visible only in `difficulty_trace.csv` at one row per mined block — far too coarse
   to attribute a slowdown to the screen you happen to be standing on.

   **Measurement procedure** (developer): start an autobet, set the DEV time scale to **9000X**, and
   read `Sim:` in DiceGame; then open **BetsHistoryExplorer**, wait a few seconds for it to finish
   loading, and read it again. Reference points: **§B.6.6 measured 0.63 (63%) in ordinary play**, and
   a genuine 9000X → 100X collapse means retention ≈ **0.011 (1%)**.

   | Reading in BetsHistoryExplorer | Verdict |
   |---|---|
   | ~1–2% | hypothesis confirmed — §C.2's suspects are the frame-eaters |
   | ~60%+ (unchanged from DiceGame) | **hypothesis wrong** — the cause is elsewhere, start over (D-M2.4) |
   | in between | partial: something else contributes too; measure before attributing |
2. Fix whichever of the two eaters the measurement indicts (likely both).
3. Re-measure. The success criterion is the **throttle**, not the eye: retention in
   `BetsHistoryExplorer` should approach what DiceGame shows at the same `DevTimeScale`.
4. D-M2.7's cleanup regardless of the outcome.

## C.4b ⚠️ The live `user://` world IS the reproduction case — do not wipe it

The slowdown exists *because* the loaded history is large (§C.2's first suspect re-sorts 100k+
records per event). On a freshly reset world with near-zero bets it would very likely disappear on its
own — so a wipe before C.4's measurement would produce a green re-measurement that means nothing,
and there would be no way to tell a real fix from an absent workload.

Keep the live world until C.4 step 3 is signed off. Nothing on this branch forces a reset (Part A is
in-memory statics, Part C is refresh cadence — no `WorldFormatVersion` bump). The §B.0 archive lives
**outside** `app_userdata` and is unaffected either way; if a wipe ever becomes necessary
mid-branch, copying the archive back into `app_userdata` restores the repro.

## C.5 Scope note

`BetsHistoryExplorer` is already named on **Chapter 38's poll-migration backlog**. This is not that
migration — it is the §38.7 *inverse* failure, and it is being fixed now because it is actively
distorting playtest pacing. If the fix happens to make the Ch. 38 migration trivial for this scene,
take it; do not widen to the other ~18 scenes on that list.

## C.6 Measurements

**Before (2026-08-07, developer, DEV time scale 9000X, ~105k-record history):**

| Scene | Sim retention |
|---|---|
| Main Menu | **100%** |
| BetsHistoryExplorer | **15–18%**, varying in that range |

Same running simulation, same frame, two scenes — **a ~6× collapse attributable to the scene alone.**
§C.2's diagnosis is confirmed: nothing lowers the requested rate, the game simply stops keeping up
with it, exactly as R2-C1's throttle is designed to report. (DiceGame's own reading was not available:
the StatusBar overflows its width there, so the appended sixth cell was off-screen — the readout was
moved leftmost beside the DEV watermarks.)

**Note on the reported "100X".** 15–18% retention at 9000X is ≈1,400X, not 100X, so the original
report understated the remaining speed. That does not weaken the finding — the collapse is real,
large and scene-attributable — it just means the perceived figure was an impression rather than a
measurement, which is the entire reason D-M2.4 required a number before a fix.

**The two fixes (both §C.2 suspects indicted):**

1. **`OnLiveStatsChanged` no longer re-sorts the whole history.** New bets are appended in
   chronological order, so the sorted view is extended with just the tail — **O(new bets) instead of
   O(all bets)**, replacing an `OrderBy(...).ToList()` over ~105k records that ran **four times a
   second**. The full rebuild survives as a guarded fallback (shorter list ⇒ checkpoint rollback,
   changed head ⇒ history reload, out-of-order tail), so a wrong assumption costs a rebuild rather
   than a wrong view.
2. **The view refresh is denominated in REAL time** (D-M2.5), 0.25 s, instead of "whenever the game
   second changes" — a cadence the DEV time scale *multiplies*, so at 9000X it was rebuilding two UI
   containers (~520 entry updates) **every frame**. Both guards are kept: the timer bounds how often,
   the second-changed test still suppresses redundant identical rebuilds.

**After, measured in two rounds (developer, same 9000X setup):**

| Build | BetsHistoryExplorer retention |
|---|---|
| Before any fix | 15–18% |
| + incremental append, 0.25 s real-time refresh | 40–80%, peaks to 98%, dips to 20% |
| + 1 s refresh, skip-unchanged-window guard | **50–70% typical**, max 80%, dips to 18% and **less frequent** |

**~3.5× improvement, and the shape of the residual is now readable.** The middle round's
98%-between-dips proved the *steady* cost was already gone at that point and what remained was a
periodic **spike**; the final round traded spike frequency for a lower ceiling (max 98% → 80%), which
says the residual has **two distinct components**:

- **A steady ~20–30%**, unrelated to refresh cadence. Between rebuilds `_Process` now does almost
  nothing (a date format and a timer increment), so this is very likely the cost of **rendering** the
  ~520 pooled entry nodes the two containers keep on screen — a cost of the screen *existing*, which
  no refresh throttling can reach.
- **1 Hz spikes** to ~18%, the rebuild itself.

**The experiment ran, and the hypothesis was right.** `MaxPreviewEntries` 260 → **50** (validated
first: `ClearEntries` hides unused pooled entries, so this really does cut drawn nodes ~520 → ~100)
⇒ retention **~100%, practically sustained, identical to every other scene**. The residual was
**render cost of the visible entry nodes** — the cost of the screen *existing*, not of updating it.

| `MaxPreviewEntries` | Retention |
|---|---|
| 260 | 50–70% |
| **50** | **~100% sustained** |

So the remaining question was never an optimisation — it is a **design** one: how much history this
screen should show. The in-place-update refactor of the two shared containers is now explicitly **not
needed**; it would have been a large change aimed at the wrong half of the cost.

### C.6a — The finding is NOT confined to this scene

`BetHistoryContainer` and `PreviousWinnerNumbersGrid` are **shared with DiceGame**, which fills the
same 260-entry pool during any long run. Nothing about this cost is specific to BetsHistoryExplorer —
that scene was merely where it was noticed, because the re-sort defect stacked on top of it and pushed
the total somewhere impossible to ignore.

**Measured, and confirmed** (developer, same 9000X setup; the readout had to be extracted into a
shared `SimRetentionReadout` control first — DiceGame has **no StatusBar at all**, it renders its own
balance labels, so it now hosts the reading inside `DevTimeScaleSelector`, beside the control that
asks for 90× the work):

| Scene | Retention at 9000X |
|---|---|
| Main Menu (no history containers) | **100%** |
| **DiceGame** (260-entry list, incremental) | **70–80%** |
| BetsHistoryExplorer, `MaxPreviewEntries = 260` | 50–70% |
| BetsHistoryExplorer, `MaxPreviewEntries = 50` | **~100%** |

The ordering is exactly what the two-component model predicts. DiceGame pays the **steady render
cost** of the same ~520 nodes (~20–30%) but not the rebuild spikes — its list grows one entry at a
time through a ring buffer rather than clear-and-refill. BetsHistoryExplorer paid both. Main Menu
hosts neither container and pays nothing.

Two consequences:

1. ~~**§B.6.6's "ordinary play retains 0.63" was measuring a SCREEN, not the simulation.**~~
   **RETRACTED 2026-08-12 — see §C.6c. The A/B refuted both this and the draw-cost explanation it
   rested on. The 0.63 baseline is UN-retracted: it is real for this world at 5 credits.**
2. **Ch. 38's poll-migration backlog is about update cadence, and neither cost here is on it.** An
   event-driven refresh would not have helped: a rebuild costs what it costs whenever it runs.
   *Migrating a poll cannot fix a cost paid by rebuilding.*

### C.6b — Shipped: 100 entries, both scenes (developer's call, 2026-08-07)

`BetHistoryContainer.MaxRecentEntries` and `PreviousWinnerNumbersGrid.MaxRecentEntries` → **100**
(these size the **pools**, so the nodes genuinely disappear rather than merely hiding), matched by
`BetsHistoryExplorer.MaxPreviewEntries` → 100. DiceGame's history seeding derives from the container
constant and followed automatically. The two containers are always rendered together and were sized
together — cutting one alone hides the other's saving.

**Final measurement: >80% in DiceGame *and* BetsHistoryExplorer**, and moving between scenes is now
practically imperceptible.

**The remaining <20% is accepted, deliberately.** 9000X is a DEV-only setting, the difference is not
felt in play, and the cost is now *known and measured* rather than accidental — which is the
difference that matters. Anyone wanting it back knows the exact lever and its exact price.

## B.7 The post-fix run (2026-08-12) — clean, but it does NOT replace the retracted baseline

A second 100k run was made on the current build after a deliberate world reset (delete
`world_format_version.txt` ⇒ `storedVersion 0 ≠ 5` ⇒ the guard's own clean wipe). Archived as
`GamblingMiner_postfix_100k_2026-08-12`. **Everything load-independent came back clean:**

| Check | Result |
|---|---|
| Records / duplicate `BetRecord.Id` | 100,146 / **0 duplicates** |
| Multiplier | 100,146 / 100,146 exact |
| Balance continuity | 100,145 / 100,145 exact |
| Engine replay (ladder + credited profit + carry) | exact, **except a session boundary — see below** |
| Win rate | 0.5033 (**+2.1σ**, unremarkable) |
| Max consecutive losses | **13** vs an expected ≈16.6 |
| Stops / insist resets / auto-recharges | **0** — the ladder reset only on wins, as before |
| `godot.log` | one line: the world-reset notice. **Zero errors, zero tripwires** |

**The replay found the session boundary by itself.** All four credited-profit divergences and the one
ladder divergence sit at index **≥ 100,000** — the configured bet count. At exactly 100,000 the
session ended and a second one began, which resets both the ladder *and* `BetService`'s remainder
carry. Neither is persisted; the replay located the boundary purely from arithmetic. **This is the
mini-plan-01 round-3 signal used as a positive control** — it is now twice-demonstrated that an
unbroken carry proves an unbroken session, and a broken one dates the restart to the bet.

### B.7a — Why it cannot replace the 0.63 baseline (and why that is my error)

| | Old run | New run |
|---|---|---|
| Player hardware credits | **5** | **1** |
| `configuredPower` | ~7.7 | ~4.0 |
| Bets per block | ~3,000 | ~600 |
| Retention (whole run) | 0.63 | 0.742 |

At 1 credit the engine does ~5× less work per frame, so the retention figures are **not comparable**
and 0.742 cannot stand in for the retracted 0.63. **The instruction that caused this was wrong:** it
said "re-buy the 5 hardware pieces before starting", but a freshly reset world begins with **0 BTC**,
and hardware is bought with mined BTC — so the requested precondition was not reachable at the moment
it was requested. *Before prescribing a wipe, check that the conditions being reproduced survive it.*

The run is not wasted — every check above is load-independent — but the one measurement it was made
for is invalid.

**Also visible, and interesting on its own:** retention **rises** across the run — by fifths,
`0.614 → 0.722 → 0.741 → 0.785 → 0.894`. Cost that grew with history would do the opposite. The
early fifth (0.614) is startup, the world rebuild and scene navigation; and it lands almost exactly
on the **old** run's whole-run 0.63 — whose trace covered only its first 35 live blocks, i.e. *the
same warm-up window*. So **0.63 was largely a measurement of start-up**, a second reason it was never
a baseline. A steady-state figure needs the tail of a run, not its head.

### B.7b — The measurement that WOULD settle it

No new world is needed, and one should not have been made for this: **the build is the variable, not
the world.** `GamblingMiner_prefix_run_2026-08-12` (archived before the wipe) holds the original
world *with its 5 credits*, stamped `world_format_version = 5`, which the current build also reports
— so restoring it triggers no reset. Running an autobet in it under the current build and reading
`Sim:` gives a true A/B: same world, same hardware, same strategy, **only the code differs**.

### C.6c — The A/B: what Part C did and did not do (2026-08-12)

The pre-fix world was restored intact (same chain, same 105k-record journal, same **5 credits**) and
run under the shipped build, isolating the code as the only variable. *(The fresh world made first
was useless for this: a reset wipes `hardware_allocation.json`, so it ran at **1 credit** — see
§B.7a.)*

| Sample — same world, 5 credits, 9000X | Retention |
|---|---|
| blocks 113–147, pre-everything | 0.630 |
| all 95 archived blocks, mixed builds | 0.694 |
| blocks 168–207, immediately pre-restore | **0.757** |
| blocks 208–247, shipped build | **0.624** |

**Post-fix measured LOWER than the adjacent pre-fix window**, and per-block retention within one
session ranged **0.377–0.831**. The variance exceeds the effect, so 40 blocks cannot resolve it —
and the on-screen label, which produced the earlier "DiceGame is 70–80%" impression, resolves it far
less. *An instantaneous readout is a debugging aid, not a measurement.*

**Verdict:**

- **Part C's BetsHistoryExplorer fix stands.** 15–18% → >80% is far outside this noise band, and its
  two defects (a full-history re-sort per `StatsChanged`; a refresh cadence denominated in game
  seconds) are wrong on their own terms regardless of what they cost.
- **The draw-cost explanation is refuted.** If the residual were the nodes' draw cost, cutting
  260 → 100 would have helped DiceGame too — same containers. It did not, because DiceGame *appends*
  through a ring buffer and has no bulk rebuild to make cheaper. The count scaled the **rebuild**,
  not the draw. **DiceGame gained nothing measurable from the entry reduction.**
- **The 0.63 baseline is un-retracted.** Every sample of this world at 5 credits lands in 0.62–0.76
  on every build. It is what this load retains.
- **Open, and out of scope here:** what DiceGame actually spends its frame on at 9000X — the
  per-block commit (~280 KB `state.json` write + checkpoint + governance tick), journal I/O, or the
  engine itself at 5 credits. Belongs with the Ch. 38 / PRIVATE_ROADMAP §8 T4 performance work.

**The lesson:** *an explanation that fits one scene is a hypothesis, not a finding, until it is
tested where it predicts a second outcome.* DiceGame was that test, was available throughout, and was
consulted only after the fix had shipped and been documented.

### C.6d — The A–B–A crossover: Part C confirmed, and the confounder identified (2026-08-12)

Prompted by the developer: *the earlier tests never ran a long autobet, so we never saw the number
after several minutes.* One continuous autobet, restored world, 5 credits, 9000X, ~5 min per leg,
boundaries marked by **trace row count** rather than by reading the label.

| Phase | Retention | Blocks |
|---|---|---|
| A1 — DiceGame, **cold start** | 0.5649 | 26 |
| A1 warm tail (last 6) | **0.7838** | — |
| **B — BetsHistoryExplorer** | **0.7722** | 36 |
| A2 — DiceGame, **warm** | **0.7624** | 30 |

**1. Part C is CONFIRMED.** B sits between A1's warm tail and A2 — no scene effect remains. Pre-fix:
15–18% against DiceGame's ~63%, a ~4× gap. Now: parity. Sustained and trace-measured, which is what
every earlier BetsHistoryExplorer number lacked.

**2. Warm-up was the dominant term all along.** A1 climbed `0.402 → 0.774` within its own quarters,
A2 `0.568 → 0.900`. **That ~0.2–0.35 swing exceeds every scene effect chased in this part.** It
explains the entire contradictory record: "70–80%" was warm, "50–60%" was cold, the fresh run's
`0.614 → 0.894` was the ramp, and **the 0.63 baseline came from a 35-block trace sitting almost
entirely inside it.**

**Corrected figure: warm steady state ≈ 0.76 for this world at 5 credits.** 0.63 is a *cold* number.

**§C.6c's arithmetic is superseded** — its 0.757 vs 0.624 was **warm vs cold**. Warm-to-warm it is
`0.757 → 0.7624`, i.e. no change, so its *conclusions* stand (the entry reduction did not help
DiceGame) on evidence that is now sound.

**The rule: measure warm, and prove it by returning.** The crossover's return leg is what separated
"the scene did it" from "time did it"; every reading here that lacked one was reading time.

## C.7 Part C — CLOSED

| | |
|---|---|
| Reported | entering BetsHistoryExplorer collapsed the game speed at 9000X |
| Diagnosis | not a speed setting — frame starvation, reported honestly by R2-C1's throttle |
| Fixed | full-history re-sort per `StatsChanged`; game-time-denominated refresh cadence; rebuild cost ∝ entry count |
| Result | **15–18% → parity with DiceGame**, confirmed by A–B–A crossover (§C.6d). No change in DiceGame |
| Left behind | `SimRetentionReadout` — the throttle is now visible in every scene instead of one CSV row per block |
| Design record | `Documentation/ProjectDesignManual.md` **§38.8** |

**Judgement:** the reported defect (a scene silently collapsing the simulation rate) is fixed well
past the point of being a playtest hazard. What remains is ordinary "this screen is heavy", and Ch. 38
already lists this scene as a poll-migration candidate — the natural home for any further work.

---

# Part D — a summary layer over the bet journal (developer proposal, 2026-08-07)

> 🚫 **DEFERRED to its own plan — not built on this branch** (developer's call, 2026-08-07). It
> touches persistence, the checkpoint contract and two UI scenes, which is materially larger than
> Parts A/C, and unlike them it fixes an *optimisation* rather than an active annoyance. Kept here in
> full so the next plan starts from a written design rather than memory; when it is picked up, move
> this whole part across.
>
> **Note for whoever picks it up:** D-M2.13 overlaps Part C. If C's fix lands first (a local refresh
> throttle), D later makes part of it unnecessary — that is fine and was decided knowingly.

## D.1 The problem

Every general figure the player sees about their betting life — total bets, net P/L, max bet, max
consecutive losses — is computed by **scanning the whole journal**, which is why the journal must be
fully loaded at boot. That is INC-001's root cause restated: at 105,049 bets it is 30 MB across 11
chunks; the retention that shipped bounds what is **written**, not what is **read**.

## D.2 The proposal

1. **A persisted rollup**, O(1) in size, updated **incrementally as each bet settles** — the general
   figures live in it directly instead of being re-derived.
2. **The detail stays chunked and is loaded on demand**, the way blockchain data already is. The
   chunks exist (`bet_history_NNNNNN.jsonl`); what is missing is an **index** so a consumer can load
   only the chunk it needs.

## D.3 Design notes

**D-M2.10 — "max martingale level" is a BETTER metric than "max consecutive losses", and it is free.**
This is the part of the proposal worth the most. INC-002 renamed the displayed figure precisely
because it was **not** a ladder depth: insist resets, the §25.5 bankroll-limit reset and every
auto-recharge put the bet back to base while the loss run kept counting. The metric the developer is
asking for is the one originally intended — and it needs **no derivation at all**:
`BaseBetSession.ProgressionTriggerStreak` already *is* the current ladder depth, maintained live.
Recording its running maximum is exact, costs one comparison per bet, and is **immune to the
tie-order sensitivity §B.6.4 measured**, because it is captured at settle time in true order rather
than reconstructed from a sort. *Here the rollup is not a cache of a derived value — it is more
correct than the derivation.*

**D-M2.11 — v1 is GLOBAL: one rollup over the player's entire betting life** (developer's call,
2026-08-07). No per-strategy epochs, no fingerprint, no segmentation — `BetsHistoryExplorer` shows
lifetime totals and that is the whole v1 surface. The per-strategy breakdown becomes a **future
scene** (§D.6), and the epoch key is designed *then*, against a real screen, rather than guessed now.
*The one exception that must survive:* **max consecutive losses stays segmented by
`(GameId, Chance)`** — that is INC-002/§40.8's correctness rule, not a presentation choice; a run at
2% chance concatenated onto one at 50% describes neither. Max martingale level, max bet, totals and
net P/L are all genuinely global figures and need no such guard.

**D-M2.12 — a rollup is a persisted figure that can diverge from reality** (§39.16 rule 1 — the rule
this project keeps re-learning). Two guards ship *with* it, not after: (a) the rollup must be exactly
**recomputable** from the detail chunks, with a `[Conditional("DEBUG")]` pass that recomputes and
compares; (b) it is **checkpoint-covered** like every other player-facing persisted value — rolled
back to the last mined block on restart, or a crash leaves the summary ahead of the journal it
summarises. *A summary that can silently disagree with its source is worse than the scan it replaced,
because the scan could not lie.*

**D-M2.13 — the chunk index is what actually fixes INC-001, and it also fixes Part C.** Per chunk:
first/last `TimestampUtc` and record count. With it, `BetsHistoryExplorer` binary-searches to the
chunk covering a date and loads that one — which removes `EnsureFullHistoryLoaded()` from the boot
path **and** removes the 100k-record re-sort that §C.2 fingers as a frame-eater. Parts C and D
converge here: if D lands first, part of C's fix comes free; if C lands first, it is a local
throttle that D later makes unnecessary. Worth deciding the order deliberately.

## D.4 Open questions

- Does the rollup live in `UserStatsService` (which already owns lifetime stats and self-persists per
  bet) or in a new file beside the journal? The former is less surface; the latter separates "stats I
  display" from "index over storage".
- Retention: do old chunks eventually get pruned, or kept forever? The proposal implies "kept, loaded
  on demand" — that bounds the *read*, not the disk.

## D.5 What this does NOT change

The bet journal keeps recording **every** bet. The rollup is an accelerator, never a replacement:
the moment a summary is the only copy of a fact, no audit like §B.6 is possible again.

## D.6 Planned, not built — the per-strategy statistics scene

A future scene where the player picks a **strategy** and sees that strategy's own figures (max
martingale level, max bet, bets, net P/L, streaks). This is where the epoch key of D-M2.11 gets
designed, because that is the first screen that actually needs one.

Two things to carry forward when it is built: the fingerprint must cover everything that changes what
a *level means* (base bet, both progression percents, both stop amounts, both Insist switches), and
whether it is stored **per record** or only per summary decides whether the history can ever be
re-segmented after the fact. Both decisions are cheaper to make with the screen in front of you.

---

## Order of work

1. ~~**B.0 — archive the run.**~~ ✅ done 2026-08-07, before the branch exists.
2. ~~**A.3 — the general review**~~ ✅ done in play 2026-08-07; three refinements folded in
   (§A.3.1–§A.3.4).
3. **Trace §A.3.1** — name the path that reaches a running session's stops, *before* writing any
   fix. It is the one confirmed observation with no verified mechanism behind it.
4. **A.4** — implement D-M2.1 + D-M2.2 + D-M2.8 + D-M2.9 (+ the guard rail).
5. **C.4 step 1** — instrument the throttle *before* touching `BetsHistoryExplorer` (D-M2.4).
6. **C.4 steps 2–4** — fix what the measurement indicts, re-measure, then D-M2.7's cleanup.
7. `dotnet build` clean; developer runs A.5 and C.4's re-measurement.
8. **Part B check 5** — the exact-arithmetic replay against the archive. Pure analysis, no build,
   no developer input; everything else in Part B is already measured (§B.6).
9. Docs in the same branch: ProjectDesignManual §24.13 (Part A — §24.11 was already taken by the timestamp-collision entry) + §38.8 (Part C), B.6/C.6 findings
   here, CLAUDE.md only where an architectural rule actually changes.

**Not on this branch:** Part D (see its banner) and the §D.6 per-strategy statistics scene.

## Out of scope

- The wider `_Process` poll-migration backlog (Ch. 38) beyond `BetsHistoryExplorer` — see §C.5.
  `StrategyControlPanel` is not on that list at all.
- Any change to hardware progression (P5), the difficulty regulator, `MaxBacklogSeconds` /
  `MaxBetsPerFrame`, or the journal retention policy — B.7/B.8 and C.4 may *measure* them; changing
  them is a separate decision (D-M2.4).
- Panel state in scenes other than DiceGame; if the A.3 review finds the same shape elsewhere,
  record it, don't fix it here.
