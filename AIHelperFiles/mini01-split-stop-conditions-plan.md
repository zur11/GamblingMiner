# Mini-Plan 01 — Independent Stop on Profit / Stop on Loss

**Series note:** first entry of a new *mini-plan* series (`miniNN-…-plan.md`) for small, self-contained
changes that do not warrant a numbered Step plan. Independent numbering from the `stepNN-` series.

**Status:** ✅ **COMPLETE (2026-08-07)** — rounds 1–4 all implemented, build clean and **playtested OK**
(round 3 additionally audited over 684 bets against an exact engine replay, §9) ·
**Branch:** `split-stop-conditions` · **World format bump:** none ·
**Design record:** `Documentation/ProjectDesignManual.md` §25.10 (r1) + §25.11 (r2) + §25.12 (r3) + §25.13 (r4)

> ⚠️ **Round 3 supersedes part of rounds 1–2**: the per-stop Session/Anchor mode (D-M1.2's second half,
> D-M1.8) is **deleted** — everything measures from session start — and D-M1.4 ("bots never receive a
> `StopOnProfit`") is **reversed**. Read §8 before treating §1–§7 as current.

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
  modes read), never the profit one.
  *Fallback if this proves noisy in play: revert to one shared baseline re-anchored by any reset.*
  **As built:** `SessionStartingBalance` was **removed** rather than kept as an alias — it had no reader
  outside `BaseBetSession` itself (`DiceGame`'s calculator context reads `ProgressionAnchorBalance`), so
  an alias would have been dead surface.
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

**As built — two details decided during implementation:**

- **The profit field and its mode toggle are LOCKED in bot strategy mode**, not merely nulled in
  `BuildBotStrategyConfig`. Nulling alone would leave an editable control with no effect — the D-M1.4
  lock is visible rather than silent.
- **Each mode toggle keeps its own double-click timer.** Both raise `ProfitStopModeDoubleClicked`
  (DiceGame's gate for re-enabling manual betting after a P/L stop — either stop may have fired), but a
  shared timer would read "one click on each toggle" as a double click.

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

---

## 7. Round 2 — the progression percents, same treatment (2026-08-06)

Round 1 was verified in play; the identical "one value + a toggle choosing which side it applies to" shape
was still present one control up. Two requests:

1. **`IncreasePercent` + the `IncreaseOnLoss`/`IncreaseOnWin` toggle → `IncreaseOnLossPercent` and
   `IncreaseOnWinPercent`**, two fields, each armed by its own value (`0`/blank ⇒ that outcome resets the
   bet to base — the §1 rule, applied by `StrategyControlPanel.ParsePercent`).
2. **Each stop's mode toggle is disabled while its own amount field is empty**, the gate the Insist toggle
   already had.

### Decisions

- **D-M1.6 — the pair replaces the trio outright.** `ProgressiveBettingStrategy.CalculateNextBet` selects the
  percent by outcome; `BaseBetSession.UpdateProgressionStreak`'s trigger test becomes *this outcome's percent
  > 0*. The old model could express loss-side **or** win-side; the pair also expresses **both** (grow on every
  bet) and **neither** (flat betting).
- **D-M1.7 — a two-sided progression collapses Anchor mode into Session mode.** With both percents > 0 every
  outcome is a trigger, so the streak never breaks and `ProgressionAnchorBalance` only moves on a reset. This
  is the honest behavior of "grow on everything", recorded in §25.3 rather than special-cased.
- **D-M1.8 — a greyed mode toggle KEEPS its value.** Clearing an amount to retype it must not silently
  discard the Session/Anchor choice; the flag is inert while the stop is disarmed, since `ApplyStopConditions`
  never reads a mode without a `HasValue` amount.
- **D-M1.9 — the migration cost is larger here and is accepted.** A pre-split saved strategy loses
  `IncreasePercent` entirely and loads as **flat betting**, not merely with a mode flag reset — a saved
  martingale stops being one until re-saved. Still no migration code (D-M1.5's reasoning is unchanged), and
  the loss is **visible in the panel** as two empty percent fields rather than silent at runtime.

### Files touched (beyond §3)

`Scripts/Betting/ProgressiveBettingStrategy.cs` · `Scripts/Sessions/BaseBetSession.cs`
(`UpdateProgressionStreak`, `DebugAssertProgression`) · `Screens/MartingaleCalculator/MartingaleCalculator.cs`
(reads the loss-side percent; its `!IncreaseOnLoss` guards drop out) — plus the round-1 file list.
`MartingaleCalculatorStandalone` owns its own inputs and is untouched.

### Verification (developer)

- Loss `130` / win blank ⇒ classic martingale, unchanged from before this change.
- Loss blank / win `100` ⇒ bet doubles after each **win**, resets on a loss.
- **Both** set ⇒ the bet grows on every bet and only a stop/reset brings it back to base.
- Both blank ⇒ flat betting at base bet.
- Profit/Loss mode toggles grey out when their amount field is emptied, and **keep** Session/Anchor when the
  amount is retyped.
- A strategy saved *before* this change loads with both percent fields empty (expected — re-save it).

---

## 8. Round 3 — Anchor mode deleted, Insist On Profit added (2026-08-06)

Round 2's D-M1.7 (a two-sided progression collapses Anchor into Session) was accepted as a reason to drop
the mode rather than document a control that silently means nothing. Removing it then removed the last
per-run bound on a progression, which is what makes profit-insist necessary rather than optional.

### Decisions

- **D-M1.10 — Anchor mode is deleted, not defaulted off.** `StopOnProfitUseAnchor` /
  `StopOnLossUseAnchor`, both toggles and `FormatStopModeText` are gone; every stop measures from session
  start. **`ProgressionAnchorBalance` / `ProgressionTriggerStreak` survive untouched** — they answer "where
  did the current progression run begin", which is what the Martingale calculator projects from. *Delete the
  USE, not the value.* (Supersedes D-M1.2's baseline-mode half and all of D-M1.8.)
- **D-M1.11 — `InsistAfterStopOnProfit`, mirroring the loss switch.** `HandleStopOnProfit` is the exact twin
  of `HandleStopOnLoss`; `ResetProgressionToBase(reanchorProfit, reanchorLoss)` takes two flags so **a reset
  re-anchors only the side that fired** — round 1's asymmetry generalized instead of special-cased. Without
  it, a win-side or two-sided progression has no upper reset and returns to base only after the loss stop
  fires, i.e. after giving the profit back.
- **D-M1.12 — D-M1.4 is REVERSED: bots get a profit stop, with both Insist switches forced ON.** The
  original rule's reasoning was "no *terminal* stop for a bot" (`SimulationService` restarts a bot session
  only on `InsufficientBalance`), not "no profit target"; insisting removes the terminality. Forced in
  **`DiceGame.BuildBotStrategyConfig`**, not merely mirrored from the panel — a per-node snapshot captured
  before this change carries `false` and would re-create the terminal stop. *Assert an invariant where the
  runtime config is built, not only where the user sets it.*
- **D-M1.13 — the manual-bet double-click gate moves to the Insist toggles.** `ProfitStopModeDoubleClicked`
  → `ProfitOrLossStopDoubleClicked`, raised by either Insist toggle (separate per-control timers, as before).
  Both stop-amount fields are now always editable in bot strategy mode.

### Verification (developer)

- Profit `5`, Insist On Profit **OFF** ⇒ unchanged from round 1: stops once, does not resume.
- Profit `5`, Insist On Profit **ON** ⇒ resets to base at +5 and keeps running; the next +5 is measured
  **from the reset point**, and the loss threshold still measures from session start.
- Win-side progression (`Increase on win` set, loss blank) + profit insist ⇒ the bet climbs through a
  winning run and drops back to base at the threshold instead of running away.
- Each Insist toggle greys out when its own amount is empty; double-clicking either one re-enables manual
  betting after a P/L stop.
- Bot node selected ⇒ both Insist toggles show ON and locked, both amount fields editable, and the bot never
  ends its run on a stop (it resets and keeps betting).
- No mode toggles remain anywhere in the panel.

---

## 9. Round 4 — an insisting stop is a segment boundary (2026-08-06)

Round 3 was playtested over **684 bets** with both stops and both insists active, then audited against an
exact replay of the engine (BigInt satoshis, `MidpointRounding.ToZero`, `BetService`'s fractional-remainder
carry). **Audit result: 684/684 bet amounts and 684/684 credited profits reproduced to the satoshi, balance
continuity exact, 46 profit-stop resets all correctly spaced, no session ever ended, no auto-recharge.** The
single bet-amount anomaly was an autobet restart, independently confirmed by the remainder accumulator
resetting at the same bet.

**But the bet still reached 66× base** (`66.26`, bankroll peak ~135) with `StopOnLoss` armed and insisting.

### Decisions

- **D-M1.14 — a stop's reset-to-base is a no-op BY CONSTRUCTION when its own outcome doesn't drive the
  progression.** The profit stop can only fire on a winning bet, the loss stop only on a losing one; so with
  `IncreaseOnWinPercent = 0` / `IncreaseOnLossPercent = 0` respectively, the bet is already base when the
  stop fires. This is structural, not a bug — and it is why "make each stop work independently of the
  percents" cannot be solved by changing the reset.
- **D-M1.15 — an insisting stop re-anchors its own baseline always, and the OTHER side's only if that side
  also insists.** Two insisting stops share one segment (either threshold closes it); a non-insisting stop
  keeps its whole-session anchor, so D-M1.3's rationale survives for the mixed case it was actually written
  for. Neither insisting ⇒ byte-identical to round 3.
- **D-M1.16 — the diagnostic worth keeping:** the loss stop fired 5–12 times at every threshold tried and the
  max bet stayed `66.26407466` *unchanged to eight decimals*. **When a knob's value provably cannot change
  any observable, the defect is in what the mechanism is wired to do, not in its arithmetic.**

### Measured (same outcome sequence, both insisting)

| `StopOnLoss` | max bet, round 3 | max bet, round 4 |
|---|---|---|
| 0.1 / 0.5 / 1 | `66.26407466` | **`1.21`** |
| 2 | `66.26407466` | `3.79749829` |
| 5 | `66.26407466` | `10.83470575` |

### Files touched

`Scripts/Sessions/BaseBetSession.cs` only (three call sites of `ResetProgressionToBase` + its comment).
No config field, no UI change, no persistence change, no `WorldFormatVersion` bump.

### Verification (developer)

- Win-side progression + both stops + both insists ⇒ the bet no longer climbs past a couple of steps after a
  drawdown; the loss threshold visibly changes how far it gets (it previously changed nothing).
- `Increase on loss` > 0 with a loss stop ⇒ still resets mid-losing-run as before.
- Profit stop insisting, loss stop **not** insisting ⇒ the profit target still measures from session start
  across a losing stretch (the mixed case must not regress).
