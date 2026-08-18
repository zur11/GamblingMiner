# Mini-Plan 04 — Expanding BetsHistoryExplorer

**Series note:** fourth entry of the *mini-plan* series, following
`mini03-bet-journal-index-and-rollup-plan.md`, which gave this scene its own replay cursor (§9) and
made its statistics whole (§6.8).

**Status:** ✅ **READY TO IMPLEMENT — every open question resolved (§6).** Diagnosis §1, objectives
§2–§5, decisions §6, build order §5. Branch created, no code touched. · **World format bump:** none expected.

---

## 1. Diagnosis — why DiceGame shows every bet and the explorer shows them in clumps

**The two scenes do not render the same way, and the difference is not a setting.**

### 1.1 — DiceGame renders an EVENT STREAM

`SimulationService` raises `BetSettled` once per settled bet. `DiceGame.OnSimBetSettled` forwards that
one bet to `BetExecuted`, and `BetHistoryContainer` **prepends a single row**. One bet = one event =
one row appearing.

You see each bet because **each bet announces itself**. Nothing is sampled or summarised; the list
*is* the stream.

### 1.2 — BetsHistoryExplorer renders a TIME-WINDOW SNAPSHOT

It does nothing per bet. Each refresh it:

1. binary-searches the records for the cursor's instant (`UpperBound`),
2. slices the **last `MaxPreviewEntries` (100)** records before that point,
3. **rebuilds both containers wholesale** from that slice.

It never emits a bet — it repaints a window. **The number of "new" bets seen per repaint is simply
however many entered the window since the previous one**, which is a function of three multipliers,
none of which is "one bet":

| Multiplier | Value | Where |
|---|---|---|
| Repaint rate | **at most 1 per real second** | `ViewRefreshIntervalSeconds = 1.0` (mini-plan 02 §38.8 set this to stop the scene eating the frame) |
| Cursor rate at **"1x"** | **100 game-seconds per real second** | `_speedSteps[0] = 100`; `GameBaseSpeed = 100` is what makes it *display* as 1x |
| Bet density | **0.047 bets/game-second** = **4.7 per REAL second** at 5 credits (measured, §1.2a) | the sim's own rate = hardware credits |

A single repaint at the **slowest** available speed advances **100 game-seconds, roughly 5 bets** —
which is exactly the reported clumping. **The speed is not the problem; the repaint granularity is.**

#### 1.2a — A measurement error of mine, corrected (2026-08-15)

This section first claimed **~1.5 bets per game-second**, and from it concluded that 1X replay would
be ~500 bets/second and therefore unwatchable. **Both were wrong.** The statistic divided total bets
by *the number of whole seconds that contain at least one bet*, silently discarding every empty
second — and the bets arrive in small same-frame bursts separated by long gaps (median gap 0.00
game-seconds, p90 **33.33**), so most seconds are empty. It over-reported density by **32x**.

Measured properly over 3,000 consecutive records: 63,498.8 game-seconds of span, **21.17 game-seconds
between bets on average**, i.e. **0.047 bets per game-second — 4.7 per real second** at 5 credits,
matching the developer's figure exactly.

> **A rate is a count divided by the WHOLE interval, not by the part of it where something happened.**
> The bad version cannot look wrong on its own: it is a plausible number, in the right units, off by
> whatever fraction of the interval was idle.

### 1.3 — The conclusion, and why it makes the fix small

The clumping is **not** a speed problem and **not** a timestamp-collision problem. It is that the
scene repaints a *window* where DiceGame emits an *event*.

> **Rendering every bet the cursor crosses — appending one row, exactly as DiceGame does — reproduces
> DiceGame's behaviour by construction, at any speed.**

That single change also delivers, for free, most of what §2 asks for. See §2.2.

---

## 2. Replay pacing (developer's design, 2026-08-15)

### 2.1 — The specification

- **The existing speed control stays as it is.**
- **The base rhythm is set by hardware** — bets per 100 in-game seconds. Today each piece is +1X of
  bet speed; when hardware design lands, total power will be summed and pieces will contribute
  unequally.
- **The base follows HISTORY, not the present.** If the player now has 2X but scrubs back to when
  they had 1X, the replay runs at 1X **until the first bet that was made at the faster rate**, and
  from that instant the base becomes 2X.
- **The speed control multiplies that base**, up to **base x 10**.
- **On reaching the present the multiplier drops automatically to base x 1.**

### 2.2 — Proposal: the base needs no hardware lookup at all

**The hardware rate is already recorded in the data — as the spacing between bets.** So if the cursor
advances through *game time* at a fixed rate and the view renders **every bet it crosses**, the
observed cadence is automatically the cadence those bets were made at.

Consequences, all of which the specification asks for and none of which needs new machinery:

- A stretch played with 1 piece replays at 1X **because its bets are spaced that way**.
- Crossing into a 2X stretch speeds up **at exactly the first faster bet** — the transition the
  specification describes, with no detection step and no threshold to tune.
- Future hardware changes (unequal power per piece) need **zero** changes here: whatever cadence the
  sim produced is what the journal holds and therefore what the replay shows.

**This matters because hardware history is not persisted.** `hardware_allocation.json` stores only
the *current* allocation — there is no record of what the player owned last month, so a base derived
from hardware *state* could not be computed for a past date at all. Derived from bet *spacing*, it is
exact and free.

> **The general rule: when a past rate must be known, prefer the artefact the rate produced over a
> state that was never versioned.**

### 2.3 — What actually changes

- **Append per crossed bet** instead of rebuilding the window (this is the whole fix for §1).
- The **speed control's meaning becomes base x N**, N in 1..10, where *base* is the unmodified passage
  of game time — so 1X literally means "as it happened".
- On reaching the present, N resets to 1.

**The 1 Hz repaint throttle must stay in force for the snapshot path.** Appending one row is cheap;
repainting 100 rows at a high rate is what §38.8 measured and removed. The new path earns its rate by
doing far less per step, not by lifting the guard.

And when even the cheap path cannot keep up, **the cursor slows rather than the content thinning** —
see §6.2, which is R2-C1's throttle applied to the replay.

---

## 3. Live-follow: "almost live"

Following an active autobet should track the sim **as closely as it can**, and **the gap is the
feature, not a defect**: the two clocks are already on screen — the StatusBar's world clock and the
explorer's violet cursor — so any drift is legible without a new indicator.

**Resolved in §6.3:** fall behind while a run is active (the gap is honest and visible in the two
clocks), snap when none is. And per §6.4, almost-live is not a mode that is entered — it is what
playing at the present means while the world is still producing bets.

---

## 4. Arrival state and time navigation

### 4.1 — Arrive PAUSED (change from today)

The scene currently opens playing, with the cursor already advancing. Instead it opens showing **the
last 100 bets up to the selected instant, paused**, cursor violet. Nothing moves until the player
asks.

*This is the right default because the scene's job is to answer "what happened here?", and an
auto-advancing view starts destroying that answer the moment it appears.*

### 4.2 — From there, three ways forward

- **a. Play** — the existing dynamic. Play is a panel state, independent of the cursor (§6.4).
- **b. Speed** — left at 1X the cursor never catches the present (the sim advances at least as fast);
  above 1X it eventually does, and on arrival:
  - autobet **running** → drop to 1X and continue in **almost-live** (§3),
  - autobet **stopped** → the replay stops.
- **c. Step through time** with two new controls:

| Control | Behaviour |
|---|---|
| **Setter** (multi-toggle) | cycles `rewind by day` → `rewind by hour` → `rewind by minute` → `forward by day` → `forward by hour` → `forward by minute`. **Default: rewind by day.** |
| **Action** | executes one movement of the selected kind |

Built for the paused state first, **designed to work while playing too** — the cursor is just a
`DateTime`, so a jump is the same operation whether or not it is also advancing.

Both remain subject to the existing bounds: the replay floor (mini-plan 03 §6.6, with Replay Mode
deciding which floor applies) and the present.

---

## 5. Ordering

1. **Append per crossed bet** (§2.3) — the core fix; everything else reads better once bets appear
   individually.
2. **Arrive paused** (§4.1) — small, and it makes the rest testable without fighting a moving cursor.
3. **Setter + Action buttons** (§4.2c).
4. **base x N speed semantics and the auto-reset at the present** (§2.1).
5. **Almost-live tracking** (§3).

---

## 6. Decisions (developer, 2026-08-15)

### 6.1 — Speed: RESOLVED by measurement, and nothing needs changing

The question was whether the ladder should start below the game's own pace. It should not — and the
premise behind asking it was a bad statistic (§1.2a).

**The base is the hardware rate in bets per REAL second** — 5 credits, ~5 bets/second — and the
existing ladder is already exactly what the specification asks for:

| Ladder step | `_speedSteps` | Meaning | At 5 credits |
|---|---|---|---|
| 1x | 100 | as it happened | ~5 bets/s |
| 2x | 200 | | ~10 bets/s |
| 4x | 400 | | ~20 bets/s |
| **10x** | 1000 | the specified ceiling | **~50 bets/s** |

So `base × 10` is already the maximum, `base × 1` is already "as it happened", and **the speed control
genuinely stays untouched**. Combined with §2.2, the entire pacing specification reduces to one
change: **append per crossed bet**.

### 6.2 — No bet is ever skipped: the CLOCK pays, not the content (developer, 2026-08-15)

The earlier suggestion — cap the rows per frame and surface that it capped — is **rejected**. A cap
drops bets, and dropping bets is the exact failure this plan exists to end; announcing it only makes
the loss legible, not acceptable.

**The rule instead:**

> **When the frame cannot render every bet the requested speed demands, the REPLAY CLOCK slows down.
> Not one bet of the retained range is ever skipped.**

The cursor advances only as far as the rows actually emitted allow: emit bets in order, and when the
frame's budget is spent, **leave the cursor on the timestamp of the last bet emitted** rather than
where the requested speed wanted it. The next frame resumes from there. The replay falls behind
wall-clock; it never falls behind the data.

**Shown, not inferred:** a label states both figures whenever they differ, e.g.
`Speed: 10x requested / 7x actual`. Hidden while they agree, so it reads as information rather than a
permanent warning.

#### 6.2a — This is R2-C1's throttle, one layer up

The project already solved this exact problem for the simulation, and the parallel is worth making
explicit because it means the vocabulary and the reasoning already exist:

| | Simulation (R2-C1) | Replay (this plan) |
|---|---|---|
| Demand | `delta x SpeedMultiplier x DevTimeScale` | `delta x replay speed` |
| Bounded by | bets the engine can execute per frame | rows the view can emit per frame |
| What gives way | **the game clock** (`SimulationThrottle`) | **the replay cursor** |
| What is preserved | mining work per in-game second | **every bet in the retained range** |
| Readout | StatusBar `Sim: NN%` | `10x requested / 7x actual` |

R2-C1's rationale transfers word for word: *game time can never outrun the work it represents.* Here,
**the cursor can never outrun the bets it claims to be showing.**

And so does §38.7's third lesson, which applies to the new readout the moment it appears: **a
displayed throttle is a MEASUREMENT, not a diagnosis.** If actual sits far below requested, the
question is what is eating the frame — never "raise the per-frame budget", which only hands a
saturated frame more work.

**A calibration note, deliberately unpriced:** the per-frame emit budget is a placeholder until it is
watched at 10x across a dense burst. Per §40.7, it is timed before it is tuned — and unlike a row cap,
getting it wrong costs only smoothness, never a bet.

### 6.3 — Almost-live: snap when idle, fall behind while running

- **Autobet running → fall behind.** The gap is honest and already legible in the two clocks (§3).
- **No autobet → snap.** The present is static, so there is nothing to fall behind and a gap would be
  pure lag.

*Neat property: the two rules agree at the boundary — when a run stops, the cursor catches up and
stays caught up, so the transition needs no special case.*

### 6.4 — Play state and live-follow are two different axes (developer, 2026-08-15)

The original question was whether a step button should end live-follow. The answer refines the model
rather than just answering it:

- **Play/pause is a state of the PANEL**, independent of where the cursor sits.
- **Rewinding leaves live-follow but does NOT leave play.** Step back from almost-live and the panel
  keeps playing — from the new point, forward, as a replay.
- **Forward-stepping is clamped at the present** (a step that would overshoot lands on it), and
  **if the panel is in play, live-follow re-engages automatically at 1X.**
- With **no autobet running**, reaching the present stops the replay instead (§4.2b).

**This supersedes an earlier line in this plan** that said reaching the present must never enter
live-follow silently. That was reasoning from mini-plan 03 §9.3, where entering live-follow was a
separate decision; under the two-axis model it is not a decision at all.

> **Live-follow is not a mode the player enters. It is what "playing" MEANS once the cursor is at the
> present and the world is still producing bets.** The player already expressed the intent by leaving
> the panel in play; arriving at the present does not need a second confirmation.

### 6.4a — Proposal: make `_liveMode` derived, not stored

If the rule above is the definition, then live-follow should be **computed**, not remembered:

```
liveFollowing  ==  playing  &&  cursor >= present  &&  autobetRunning
```

Every behaviour in this plan falls out of that single expression:

- rewinding drops `cursor >= present` → follow ends, `playing` untouched ✔
- forward-stepping into the present satisfies it → follow resumes, no special case ✔
- a run stopping drops `autobetRunning` → follow ends and the replay halts (§4.2b) ✔
- **Go Live** becomes "set cursor := present" — one assignment, with follow as the consequence ✔

It also retires the risk mini-plan 03 §9.6 flagged: `_liveMode` was a stored flag decided in `_Ready`
and later became something the player could enter and leave, which is precisely the shape that drifts
out of step with the thing it claims to describe. **A derived value cannot disagree with its own
definition.**

The stored flag stays only if a case turns up that the expression cannot express — none is known.

---

## 7. Out of scope

- The **Betting Statistics scene** (`PRIVATE_ROADMAP.md`, Basic Mode objective).
- Chunk index stages 2/3 — deferred in mini-plan 03 §6.12 with its trigger recorded.
- Any change to the journal, the rollup, or the checkpoint contract.
- Hardware progression itself (P5) — this plan consumes whatever cadence the sim produced.
