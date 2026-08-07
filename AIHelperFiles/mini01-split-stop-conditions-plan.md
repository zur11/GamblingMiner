# Mini-Plan 01 — Independent Stop on Profit / Stop on Loss

**Series note:** first entry of a new *mini-plan* series (`miniNN-…-plan.md`) for small, self-contained
changes that do not warrant a numbered Step plan. Independent numbering from the `stepNN-` series.

**Status:** planned · **Branch:** `split-stop-conditions` (recommended) · **World format bump:** none

---

## 1. What changes

Today `StopOnProfit` and `StopOnLoss` each have their own amount field, but they **share** two controls:

| Shared control | Consequence |
|---|---|
| `Profit/Loss mode` toggle (`UseProgressionAnchorStops`) | One baseline choice (Session/Anchor) governs BOTH stops — you cannot measure a profit target from the session start while capping a losing run per progression. |
| `Insist After Stop` toggle (`InsistAfterStop`) | Insisting applies to both stops; enabled as soon as *either* amount is set. |

After this change each stop is fully independent:

1. **Activation is the amount itself.** A stop is armed iff its field parses to a value **> 0**.
   Empty **or `0` = disabled**. (Today `0` parses to `0m` and *arms* the stop — `StopOnProfit = 0`
   fires on the first bet, since the metric is `>= 0`. That latent defect dies with this change.)
2. **Two mode toggles**: `StopOnProfitUseAnchor` and `StopOnLossUseAnchor`, each Session/Anchor.
3. **`InsistAfterStop` → `InsistAfterStopOnLoss`**, applying to the **loss** side only. A profit stop
   always stops the session; there is no "insist" on profit.

## 2. Decisions

- **D-M1.1 — `decimal?` stays the "armed" representation.** The 0-means-off rule is applied at the
  *parse* boundary (`StrategyControlPanel.BuildConfig`), not in the config record. `HasValue` remains
  the armed test, so `BaseBetSession` and `MartingaleCalculator` need no change to their gating.
- **D-M1.2 — the bankroll-limit reset keeps reading `InsistAfterStopOnLoss`.**
  `BaseBetSession.ApplyStopConditions`'s `_currentBet > balance` branch (ProjectDesignManual §25.5) is a
  loss-side safety net — the grown bet no longer fits the bankroll — so it belongs to the loss toggle.
  Precedence (§25.7) is unchanged: cheap reset first, recharge only as a last resort.
- **D-M1.3 — separate session baselines for the two stops.** `ResetProgressionToBase()` currently
  re-anchors `SessionStartingBalance`, which under split stops would let a *loss* insist-reset move the
  *profit* target's baseline (a `+100` session goal would silently become "+100 from the last dip").
  Split it into `ProfitSessionStartingBalance` / `LossSessionStartingBalance`; a loss insist-reset
  re-anchors the **loss** baseline and `ProgressionAnchorBalance` (a progression-level concept both
  modes read), never the profit one. `SessionStartingBalance` is kept as a read-only alias of the loss
  baseline for the existing UI/calculator readouts.
  *Fallback if this proves noisy in play: revert to one shared baseline re-anchored by any reset.*
- **D-M1.4 — bots never get a profit stop.** `SimulationService` restarts a bot session **only** on
  `InsufficientBalance`, so a non-insisting stop is terminal. `DiceGame.BuildBotStrategyConfig` must
  force `StopOnProfit = null` for bots, and keep `StopOnLoss` only when `InsistAfterStopOnLoss` is on
  (today's `allowProfitLossStops` gate, now loss-only).
- **D-M1.5 — no migration for saved strategies.** `user://` saved strategies are a *personal* file, not
  world state. Old entries simply deserialize the removed properties as absent ⇒ both modes default to
  **Session** and `InsistAfterStopOnLoss` to **OFF**. No migration code, no format bump (project policy:
  bump-and-wipe, never migrate). Re-save any strategy that relied on Anchor mode or insisting.

## 3. Files touched

| File | Change |
|---|---|
| `Scripts/Betting/BettingStrategyConfig.cs` | `UseProgressionAnchorStops` → `StopOnProfitUseAnchor` + `StopOnLossUseAnchor`; `InsistAfterStop` → `InsistAfterStopOnLoss`. |
| `Scripts/Sessions/BaseBetSession.cs` | Two baselines (D-M1.3); two metrics in `ApplyStopConditions`; profit stop → always `Stop`; loss stop → insist-or-stop; `ResetProgressionToBase` re-anchors loss + progression only; comment block at §25.3/§25.4 updated. |
| `UI/StrategyControlPanel/StrategyControlPanel.cs` | New `_stopOnProfitModeToggle` / `_stopOnLossModeToggle` exports; `ParsePositiveDecimal` (0/blank ⇒ null); insist-toggle availability keyed on **StopOnLoss > 0** alone; `ApplyStrategySettings`/`ClearStrategySettings`/`BuildConfig`/`ApplyStrategyModeRestrictions` updated; `ProfitStopModeDoubleClicked` fired by either mode toggle. |
| `UI/StrategyControlPanel/StrategyControlPanel.tscn` | Split `ProfitStopModeContainer` into two cells (`StopOnProfitModeContainer`, `StopOnLossModeContainer`) + `node_paths` entry. Grid is `columns = 3`: 7 cells → 8, i.e. 3 rows with 2 in the last — no layout restructure, no scroll involved (Ch. 29 not in play). Labels: `Profit stop mode` / `Loss stop mode`; button text `Profit: Session|Anchor`, `Loss: Session|Anchor`. Relabel `Stop behavior` → `Loss stop behavior`, button `Insist On Loss: ON/OFF`. |
| `Screens/DiceGame/DiceGame.cs` | `CloneConfig`, `BuildBotStrategyConfig` (D-M1.4), the MartingaleCalculator context build (~line 1714), and the P/L-mode double-click gate handler. |
| `Scripts/Betting/SavedBettingStrategyRepository.cs` | `Clone()` field list (defaults per D-M1.5). |
| `CLAUDE.md` | The `BettingStrategyConfig` bullet list + the "Progression resets vs. auto-recharge" paragraph. |
| `Documentation/ProjectDesignManual.md` | Ch. 25 §25.3 (table now per-stop), §25.4, §25.5, §25.7; new §25.10 recording this change. |

`MartingaleCalculator` reads only `StopOnProfit`/`StopOnLoss` `HasValue` — **no change**.
`MartingaleCalculatorStandalone` does not touch these fields — **no change**.

## 4. Order of work

1. Config record + `BaseBetSession` (semantics first, compiler drives the rest).
2. `StrategyControlPanel.cs` + `.tscn`.
3. `DiceGame` + repository.
4. `dotnet build` clean.
5. Docs (CLAUDE.md + Ch. 25) **in the same branch**, per the git-workflow rule.

## 5. Verification (developer, in-editor — no headless launch)

- Profit `0` / blank ⇒ never stops; profit `5` ⇒ stops once at +5 SC and does **not** resume.
- Loss `5` + `Insist On Loss: ON` ⇒ resets to base bet, keeps running, no recharge consumed.
- Loss `5` + insist OFF ⇒ session stops with `StopOnLoss`; manual re-enabled by double-clicking either
  mode toggle.
- Both armed, `Profit: Session` + `Loss: Anchor` ⇒ the two behave independently across a losing run
  (D-M1.3: the profit target is still measured from the true session start after a loss reset).
- Insist toggle greys out when the **loss** field is empty/0, regardless of the profit field.
- Bot node selected: profit field never reaches the runner; bot recovers via auto-recharge as today.

## 6. Out of scope

Per-stop `StopOnBlockMined`, any change to auto-recharge precedence, and renaming
`ProgressionAnchorBalance` / the `PrincipalBalance` legacy names.
