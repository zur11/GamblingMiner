# Mini-Plan 04 — Expanding BetsHistoryExplorer

**Series note:** fourth entry of the *mini-plan* series, following
`mini03-bet-journal-index-and-rollup-plan.md`, which gave this scene its own replay cursor (§9) and
made its statistics whole (§6.8).

**Status:** ✅ **IMPLEMENTED, awaiting playtest.** All five items of §5 (record: §8), plus the "Go to Now"
rework and auto-snap (§9). **Test protocol: §10.** Diagnosis §1, objectives §2–§5, decisions §6.
· **World format bump:** none — nothing here persists.

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

---

## 8. Implementation record (2026-08-17)

Three files: `Screens/BetsHistoryExplorer/BetsHistoryExplorer.cs` + `.tscn`, and
`Screens/DiceGame/BetHistoryUi/BetHistoryContainer.cs`. Build clean, 0 warnings. No new persisted state,
no `WorldFormatVersion` bump, no checkpoint or delete-list work.

### 8.1 — Append per crossed bet (§2.3, ordering item 1)

`BetHistoryContainer.AppendHistoricalRecord(BetRecord)` was split out of `LoadFromHistoricalRecords`, which
now calls it in a loop — so the loader and the appender cannot drift apart. It is the same `AddEntry` call
DiceGame's `BetExecuted` handler makes, with a persisted record in place of a live event, which is what
makes §1.3's "reproduces DiceGame's behaviour by construction" literal rather than approximate.
`PreviousWinnerNumbersGrid.AddWinnerNumber` was already public and needed nothing.

In the scene, `EmitCrossedBetsAndSettleCursor` replaces the window repaint as the per-frame render path:
it walks `_renderedEndExclusive → UpperBound(demand)` emitting one row per bet, and **the wholesale
snapshot path became the exception** — it now rebuilds only when the append path *cannot express the
change* (`forceRebuild`, no window yet, or the view moved backwards).

> **That last condition is the load-bearing one.** The old test was `endExclusive != _lastRendered…`,
> which under the new path would have silently undone the whole plan: while the emit budget drains a
> burst, `endExclusive` legitimately runs *ahead* of the emit frontier, and repainting "the last 100 up to
> `endExclusive`" skips everything in between. The comment at the site says so, because restoring the
> obvious-looking test is the natural way for this to regress.

`_lastRenderedEndExclusive` was renamed **`_renderedEndExclusive`** to record the change in meaning: it
stopped being a cache of what the last snapshot happened to show and became the authoritative emit
frontier — the rows, the summary walk, and the cursor's ceiling all read it.

### 8.2 — The clock pays (§6.2)

`MaxAppendRowsPerFrame = 25`, and when it is spent the cursor is left on the timestamp of the last bet
emitted rather than where the demand wanted it. Two details the plan did not have to state but the code
does need:

- **Never below where the cursor already was.** Same-timestamp bets are the common case in this journal
  (median gap 0.00 game-seconds), so `max(settled, current)` keeps the cursor from appearing to move
  backwards while a burst drains.
- **The summary walks to the SAME index the rows did.** Previously the two were driven by separate binary
  searches on the cursor; now `AdvanceSummaryTo(index)` takes the emit frontier, so the figures can never
  describe a window other than the one on screen.

The readout is a `%ReplayThrottleLabel` in the new transport row, hidden while requested and actual agree
(within 5%), measured over a **1-second window** rather than per frame — the figure fluctuates by
construction, since the cursor runs at full speed through the gaps between bursts and pays only inside
them. It is suppressed entirely while live-following, where "requested speed" has no meaning.

Also needed: the summary-label refresh gate. Its 1 Hz test was "has the game second changed?", which a
long same-timestamp burst never satisfies — rows would stream in above a frozen line counting them. It is
now *second changed **or** emit frontier moved*, still behind the 1 Hz timer.

### 8.3 — `_liveMode` deleted; live-follow derived (§6.4a)

```csharp
IsLiveFollowing => _cursorRunning
                && _calendarTimeService?.IsAutobetActive == true
                && _cursorDemandLocal >= _lastPresentLocal;
```

**One correction to §6.4a's expression, forced by §6.3.** The plan wrote the middle term as
`cursor >= present`, but backpressure legitimately leaves the *settled* cursor behind the present while
following — and §6.3 says falling behind is the feature. Testing the settled cursor would therefore end
live-follow at the first throttled frame, at a replay speed too slow to ever catch back up. So the term
tests the **demand**: where the cursor was *asked* to be, which is the half the player controls.

That is the same demand/settled split R2-C1 made one layer down, and it is why `_cursorDemandLocal` is a
field: `_selectedLocal` is now the settled cursor, `_cursorDemandLocal` the requested one. In replay the
demand is rebuilt **from the settled cursor** each frame (§6.2's "the next frame resumes from there"), so
it collapses to a per-frame local; only live-follow gives it a life of its own — the present.

`_lastPresentLocal` accompanies it: the present as of the frame the demand was set against, so
`demand >= present` cannot flip to false merely because the world advanced between two frames. It is a
cache of a computed value, not a mode.

Everything else fell out as the plan predicted: **Go Live is one assignment** (demand := present, play on);
a rewind drops the demand below the present so follow ends while *play* is untouched; a forward step into
the present satisfies the expression again with no special case; a run stopping drops the third term and
the replay halts at the present (§4.2b).

Consequences in the controls:

- **Play/Pause no longer clears anything.** Pausing at the present makes `IsLiveFollowing` false through
  its first term; pressing Play again there makes it true. That *is* the two-axis model.
- **Captions cannot be maintained from the press handlers alone**, because follow can start or end with no
  button pressed (a run beginning or stopping). `ApplyTransportCaptions` was split out and is driven
  edge-triggered from the already-per-frame `RefreshGoLiveVisibility`.
- The Play button reads **"Pause" while following**, not "Play": the panel *is* playing, and a button
  offers the action rather than restating the state.
- `_speedButton` is disabled while following (base × N is meaningless against the world's own pace), and
  Play/Pause only when the present is *static* (no run). The old test disabled both at any present, which
  under the two-axis model would have made leaving live-follow impossible.

### 8.4 — Arrive paused, and the step controls (§4.1, §4.2c)

`_cursorRunning = false` unconditionally in `_Ready` (it used to auto-play whenever there was past to
replay). The transport moved out of the crowded header into its own `TransportRow`, which also hosts the
two new controls: `%StepModeButton` cycling the six kinds (default **rewind by day**) and `%StepApplyButton`
performing one.

A step is `JumpCursorTo` — cursor set, window rebuilt wholesale, emit frontier invalidated. **§6.2's
no-skipped-bet rule deliberately does not apply to a jump**: the player asked to be somewhere else, and the
answer to "what happened here?" is the last 100 bets before that instant. Bounds are the ones already in
force — the replay floor (mini-plan 03 §6.6) and the present, both *clamping* rather than refusing, so a
forward step that would overshoot lands on the present and (if playing) re-engages follow there.

Play state is untouched by a step, per §6.4. The step controls are never disabled: a step is meaningful at
every position the cursor can hold.

The same `JumpCursorTo` also absorbed a case that was previously unhandled — a checkpoint restore
**retracting the present behind the cursor**. Every index in this scene comes from a binary search on the
cursor, so a cursor claiming a future the world has taken back would render bets that, from the world's
point of view, have not happened.

### 8.5 — Speed (§6.1, ordering item 4)

The ladder is untouched, as §6.1 concluded. The only change is §2.1's last bullet: on reaching the present
the multiplier drops to base × 1 automatically.

### 8.6 — Not verified in-engine yet

The build is clean and every `%UniqueName` the controller resolves exists in the scene, but this scene is
not something a headless launch may be used to smoke-test (it writes to the real `user://`). Two things
worth watching on the developer's first run, in this order:

1. **Do bets appear one at a time at 1x?** That is the whole plan; everything else is trim.
2. **`MaxAppendRowsPerFrame = 25` is unpriced on purpose** (§6.2's calibration note + §40.7). Watch the
   `requested / actual` label at 10x across a dense burst *before* touching the number — and remember
   §38.7's third lesson, which now applies to a readout that exists: a displayed throttle is a
   MEASUREMENT, not a diagnosis. If actual sits far below requested the question is what is eating the
   frame, never "raise the budget".

---

## 9. "Go Live" becomes "Go to Now" (developer, 2026-08-18)

Reviewed before the first playtest. §8's implementation was verified to already satisfy the developer's
stated case — *panel arrives paused; stepping to the present while paused engages neither live-follow nor
almost-live, only the final snap; live-follow engages only if Play was pressed first* — because
`IsLiveFollowing`'s first term is `_cursorRunning`. **No behavioural correction was needed there.** What
came out of the review instead was the one control that sat outside that model.

### 9.1 — The button had a name for a mode, and there is no mode

`Go Live` was named after the thing §6.4a deleted. Worse, it *acted* like a mode switch: it set
`_cursorRunning = true` on the player's behalf, so pressing it while paused silently started a replay the
player had not asked for — the two axes of §6.4 collapsed back into one, by the only control that still
believed in modes.

**Renamed `Go to Now`, and it no longer touches play.** It is exactly one assignment — put the cursor at
the present — with **two outcomes the handler does not branch on**, because the derived expression already
tells them apart:

| Panel state | Run active? | Pressing "Go to Now" |
|---|---|---|
| Play | yes | demand lands on the present ⇒ `IsLiveFollowing` true next frame ⇒ **follows** |
| Play | no | snap to the present; `ComputeCursorDemand` then stops the replay (§4.2b) |
| Paused | yes | **snap only** — stays paused, new bets do not enter |
| Paused | no | **snap only** |

> **Pressing it while paused is a request to SEE the end of the history, not to start replaying it.** Those
> are the two axes; this control belongs to the cursor one alone. That the two outcomes need no `if` is
> the strongest evidence yet that §6.4a's derived expression is the right model — a mode flag would have
> needed a branch here, and the branch is where they drift apart.

### 9.2 — Greyed, not hidden — and the threshold is the point

mini-plan 03 §9.3 hid the button, reasoning that a greyed control still poses a question ("why can't I use
that?") while an absent one never does. **That reasoning does not survive the second outcome.** Its
unavailable states are now ones the player actively wants to know about — *the view is tracking the
present for you*, or *it is following it bet by bet* — and §9.4's caption says which. An absent control
could say neither. So: `Disabled`, always visible, and captioned.

*(This paragraph originally argued the opposite from the same premise — that the button's return would
itself be the signal new bets exist. §9.4 replaced the return with an auto-snap, which makes the caption,
not the reappearance, the thing that carries the information.)*

Availability is one predicate, `CanGoToNow()`:

```csharp
!IsLiveFollowing && (present − cursor) >= GoToNowMinGapGameSeconds   // = GameBaseSpeed = 100
```

- **The threshold is not zero, and that is the design.** During a live run the present moves every frame,
  so `cursor < present` is true essentially always — a zero threshold leaves the control permanently
  enabled, offering jumps of a few milliseconds. **One real second at 1x (100 in-game seconds)** is the
  developer's granularity: after a snap the control goes quiet and comes back exactly when a second's
  worth of new material exists. With no run the present is static, so it stays quiet until the player
  rewinds — correct, since there is genuinely nothing forward.
- **The `!IsLiveFollowing` term is not redundant**, even though following implies "at the present":
  backpressure (§6.2) legitimately opens a gap wider than the threshold while following. Offering the jump
  there would invite the player to skip the bets the replay is honestly working through — and §6.3 makes
  that gap the feature, not something a button should close.

### 9.3 — The forward Step shares the predicate, not merely a similar one

A forward-programmed Step clamps to the present exactly as "Go to Now" does, so it asks the identical
question and must give the identical answer — `IsForwardStep(mode) && !CanGoToNow()`, the same call, not a
parallel test. §39.16 rule 6 is the reason: **a displayed signal must share its source with the action it
advertises.** Two independently-written availability tests eventually let one control offer a jump the
other's handler would refuse.

A rewind-programmed Step is never disabled: there is always past to go back to, bounded by the replay
floor.


### 9.4 — Auto-snap: the request outlives the press (developer, 2026-08-18)

**Amends §9.2.** The state "just snapped to now, run active, paused" was going to re-enable the button
every time 100 in-game seconds accrued. The developer's call: *"mejor que rehabilitación hagamos un
auto-snap sin habilitar el botón"* — **pressing once is asking; being asked again every second is a
chore.** So the view re-snaps itself.

**Refined the same day (§9.6): the auto-snap removes the OBLIGATION to press, not the ability to.** The
button stays available while tracking; what it no longer is, is the *only* way to stay current.

`IsAutoSnapping` is the *same* request as `IsLiveFollowing`, read on the other side of the play axis:

```csharp
RequestedThePresent  =>  _cursorDemandLocal >= _lastPresentLocal;
RunIsProducingBets   =>  _calendarTimeService?.IsAutobetActive == true;

IsLiveFollowing      =>   _cursorRunning && RunIsProducingBets && RequestedThePresent;
IsAutoSnapping       =>  !_cursorRunning && RunIsProducingBets && RequestedThePresent;
```

> **One request, two panel states.** This is §6.4's two axes stated outright rather than described — and
> it is the second time the derived model has absorbed a new feature without gaining a branch.

**Auto-snap is not a slow live-follow, and the difference is the point.** Live-follow *emits* every bet
the cursor crosses and is bound by §6.2's promise that none is skipped. Auto-snap *jumps* — it repaints
the newest 100 and skips whatever went past in between, exactly as a manual "Go to Now" does (§8.4: a jump
is not a replay). That is the honest meaning of watching the present while paused: **show me the latest,
do not replay it to me.**

Pressing Play out of auto-snap becomes live-follow — the player is already at the present and has now
asked to see every bet rather than a refreshed snapshot. That transition needs no code at all.

### 9.5 — Two things the amendment forced, both of which improved the model

**(a) Pause must FREEZE — so pausing withdraws the request.** Left alone, pausing a live-follow satisfies
`IsAutoSnapping` exactly (same request, `_cursorRunning` now false), and the view would go on jumping to
the newest bets — the opposite of what anyone reaches for Pause to do. `OnPlayPausePressed` therefore
clears the demand when it pauses. Play deliberately does *not* mirror this, per the transition above.

**(b) A PAUSED PANEL HAS NO DEMAND AT ALL** — and finding this deleted a field rather than adding one.
The first draft had the paused branch re-assert the settled cursor as the demand, which made two ordinary
situations indistinguishable from a request:

- **arriving with the cursor already at the present** — the *common* entry path, not a corner case:
  `CalendarTimeService` seeds `ExplorerSelectedLocalDateTime` **from** the present in `EnsureGameEpochInitialized`
  and `SetNow`, so a scene opened shortly after either starts there. It would have begun tracking on its
  own, breaking §4.1's "nothing moves until the player asks";
- **pausing a live-follow that happened to have no backpressure gap** — the case in (a).

The first draft fixed only the first, with an immutable `_arrivalPresentLocal` and a second comparison
clause. Fixing (a) revealed both were the same bug: **an arrival is not a request and a pause withdraws
one, so in both the demand is simply absent.** `_Process` grew a third branch that holds a
`NoRequestSentinel` while paused-and-not-tracking (and skips the emit entirely — a stationary cursor
cannot be crossed), `RequestedThePresent` went back to one comparison, and the extra field and clause were
deleted.

> **A sentinel that makes "absent" a value the existing comparison already handles beats a second flag
> that has to be kept in step with the first.**

**(c) Two performance consequences, both real at this world's size.** Auto-snap repaints wholesale, so it
obeys **both** guards the snapshot path obeys: at most once per **real** second, and only once ≥ 100
in-game seconds of new material exist. The real-time half is not optional — "+100 in-game seconds" equals
one real second only at the base scale, and the DEV time scale multiplies game time by up to 90×, which is
the lesson already written into `_Process`'s own throttle. And `JumpCursorTo` no longer forces a summary
rebuild: a *forward* jump needs the walk only to advance, and forcing it rewalked all **196,244** records —
now once per real second instead of never. (That change surfaced a latent drift: `ApplyChanceFilter`'s
hand-copied reset was missing `_summaryMaxWonAmount`, harmless only because every caller happened to be
followed by a forced rebuild. Both resets are now one `ResetSummaryAccumulators`.)

### 9.6 — Resulting control matrix

The first draft of this table was **wrong by omission**, and the developer caught it: it had a row for
"mid-history" and a row for "paused after Pause / arrived", neither of which named *paused with a run
active* — the single most common state in the scene, since it is what you get by opening it during an
autobet and what you get every time you press Pause. It had been split across two rows that each described
a different axis. **A control matrix has to be enumerated over the state variables, not over the
situations that came to mind.**

The state is fully determined by four things: `_cursorRunning` (play/pause), `RunIsProducingBets`,
`RequestedThePresent` (did the player ask to be at now), and whether the material gap is open.

**Legend: ✔ = the control is ENABLED, ✖ = greyed. Every cell describes the CONTROL, never the panel.**
The first draft wrote these as "on / off" with a `→ follow` annotation in the Play/Pause column, which
reads as *the panel is playing* rather than *the button is pressable* — and the developer read it exactly
that way, as the system deciding to start playing. It never does: **`_cursorRunning` becomes `true` in
exactly one place in the file, `OnPlayPausePressed`.** The other two writes are both `false` (arrive
paused; a replay reaching the present with no run). *In a table of controls, a column whose control is
named after a state needs a legend, or it will be read as that state.*

| # | Panel | Run | Cursor | Play/Pause | Speed | Step ◀ | Step ▶ | Go to Now |
|---|---|---|---|---|---|---|---|---|
| 1 | **paused** | **active** | in the past, not tracking | ✔ | ✔ | ✔ | ✔ | ✔ `Go to Now` |
| 2 | paused | active | tracking, just snapped | ✔ | ✖ | ✔ | ✖ | ✖ `Tracking Now` |
| 3 | paused | active | tracking, gap reopened | ✔ | ✖ | ✔ | ✔ | ✔ `Go to Now` |
| 4 | playing | active | replaying the past | ✔ | ✔ | ✔ | ✔ | ✔ `Go to Now` |
| 5 | playing | active | following the present | ✔ | ✖ | ✔ | ✖ | ✖ `Following Now` |
| 6 | either | none | in the past | ✔ | ✔ | ✔ | ✔ | ✔ `Go to Now` |
| 7 | either | none | at the present | ✖ | ✖ | ✔ | ✖ | ✖ |

**What pressing Play does, per row** — always as a consequence of the press, never on its own: rows 1 and
6 start a replay from where the cursor stands; rows 2 and 3 upgrade tracking to live-follow (row 5),
because playing at the present with a live run *is* live-follow (§6.4); row 7 cannot be pressed. In rows
4 and 5 the button reads `Pause` and stops the replay where it stands (§9.5a — pausing freezes).

**Row 1 is the one that was missing, and it is the scene's default.** Arriving during a run lands here;
so does pressing Pause from row 2, 3 or 5. Everything is enabled, which is right — the player is stopped
in the middle of a live history and every direction is open to them. Its only transient is the first
instant after Pause, when the cursor is still at the present and the gap has not reopened yet, so Step ▶
and Go to Now are briefly off; a live run reopens it within about a second.

Rows 2 and 3 are both tracking, because the button's guard and the auto-snap's are **different questions**:
the material gap (≥ 100 in-game seconds) asks *is there anything new worth showing*, while the auto-snap
additionally waits on the **1-real-second repaint floor** (§2.3 — the snapshot path never got cheaper, it
just runs less often). At exactly base scale the two coincide and the second row lasts about a frame; at
any raised DEV time scale the gap opens first and the row is a real window in which pressing forces the
refresh early. `JumpCursorTo` resets the auto-snap timer, so a manual press **substitutes** for the
automatic one rather than adding a second repaint beside it.

`Tracking Now` is therefore keyed on the button being **disabled**, not on the panel auto-snapping: a
pressable control must be captioned with what pressing *does*. A live button reading `Tracking Now` would
state the panel's condition exactly where the player is looking for the action's name.

The disabled captions — `Tracking Now` / `Following Now` — exist because "Go to Now" is the control both
derived states grey out, and a greyed control that does not say why is §24.13b's problem wearing the other
hat. Leaving tracking is Step ◀ (always enabled), or Play to upgrade to a full replay.

---

## 10. Test protocol (2026-08-18)

**Do not wipe `user://`.** The journal *is* the test material: 196,244 records on disk across 20 chunks,
against a rollup lifetime of 215,723 — so ~19,479 already pruned, which exercises mini-plan 03's pruned
prefix for free. A fresh world has nothing to replay. The world is post-genesis (a checkpoint exists), so
restarts preserve the journal.

Checks are ordered by what would invalidate the most work if it failed. **1 is the plan; the rest is trim.**

### 10.1 — Do bets appear ONE AT A TIME? *(the whole plan)*

Open the explorer on a stretch of history, press **Play** at **Speed 1x**, and watch the Bet History list.

- **Expect:** rows appearing individually, at roughly the rhythm the bets were actually placed —
  ~4.7/second at 5 credits — with visible gaps between bursts. It should feel like DiceGame's live list,
  not like a list that redraws.
- **Fails if:** rows still arrive in clumps of ~5 once a second. That would mean the wholesale path is
  still repainting; the suspect is `RefreshHistoricalViewForCurrentTime`'s `mustRebuild` condition (§8.1).

### 10.2 — Arrival, and the two Go-to-Now outcomes

With an autobet **running**, enter the explorer from DiceGame.

| Step | Expect |
|---|---|
| a. On arrival | **Paused.** Nothing moves — not the cursor, not the rows. Even if the cursor lands at the present (§9.5b) |
| b. Press **Go to Now** while still paused | The list snaps to the newest bets. **Panel stays paused.** Caption becomes `Tracking Now`, greyed |
| c. Wait ~1–2 s | The view re-snaps by itself; the button flicks back to `Go to Now` when the gap reopens (§9.6 rows 2↔3) |
| d. Press **Play** | Caption `Following Now`; rows now stream one by one instead of snapping |
| e. Press **Pause** | **Freezes.** It must NOT keep snapping — that would mean §9.5a's withdrawal is not firing |

### 10.3 — The requested/actual readout at 10x

Cycle Speed to **10x** over a dense stretch.

- **Expect:** either nothing (the budget keeps up) or `Speed: 10x requested / N x actual` while it does
  not. It must appear **only** while the two differ.
- **Report the lowest `actual` you see, and whether the row flow looked smooth.** `MaxAppendRowsPerFrame`
  = 25 is a deliberate placeholder (§6.2, §40.7) — **timed before tuned.** Do not change it; a low actual
  means "find what is eating the frame", never "raise the budget" (§38.7).

### 10.4 — Step controls

Cycle the setter through all six kinds and use each once.

- Rewinds always available; forward ones grey out when there is nothing forward.
- The floor (oldest stored bet) and the present both **clamp** rather than refuse.
- A rewind while playing keeps playing — from the new point, forward (§6.4's two axes).

### 10.5 — Free integrity checks while you are in there

- The **summary line** must always describe the rows on screen — no lag behind a streaming burst.
- The **chance filter** still appears/disappears as the cursor crosses each chance's first bet.
- The header stays **violet** while behind the present, white at it.
- Watch the Godot log for `[BetsHistory] Implausible loss run` (INC-002's tripwire — it should not fire).

### 10.6 — What to send back

The lowest `actual` from 10.3, anything from 10.1 that still clumped, and any control that was enabled
while doing nothing (or greyed while it should have worked) — the §24.13b cases are the ones most likely
to have slipped, since the availability matrix (§9.6) was reasoned out rather than observed.
