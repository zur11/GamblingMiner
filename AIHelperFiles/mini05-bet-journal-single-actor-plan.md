# Mini-Plan 05 — Who else is writing to the player's bet journal?

**Series note:** fifth entry of the *mini-plan* series, following
`mini04-bets-history-explorer-features-plan.md`, whose §13 found this while replaying history for an
unrelated reason.

**Status:** 🔬 **DIAGNOSTICS BUILT (§3). Runs A, B, C and D done — ALL CLEAN (§4.8, §4.9). All five
hypotheses exhausted (§2.1, §2.2): H1/H4 refuted, H2/H3 unreachable by design, H5 refuted. Next: repeat
at 5 hardware credits, the only untested structural variable.** Branch `bet-journal-single-actor`.

**Objective — and it is deliberately not "fix the bug".** The deliverable is **INCIDENT_LOG.md entry
INC-003**, written *after* the diagnostics in §3 have named the mechanism. The log's own format demands a
proximate fault, a root fault, evidence and a blast radius; §1 has evidence and §2 has hypotheses, and
writing the entry now would fill the root-fault field with a guess. **The plan exists to earn the entry.**

---

## 1. What is ESTABLISHED (measured from the developer's 196k-record journal)

Everything here is a measurement over `user://bet_history*.jsonl`, not an inference.

### 1.1 — The engine obeys the hardware model

| In-game hour (UTC) | bets/h | per 100 game-s |
|---|---|---|
| 2009-05-21 → 05-23 T18, rolling | **180** | **5.00** |
| 2009-05-22 T19 | 270 | 7.50 |
| **2009-05-23 T19 / T20 / T21** | **333 / 359 / 361** | **9.25 / 9.97 / 10.03** |
| 2009-05-23 T22 → 05-24 T18, rolling | **180** | **5.00** |

**180 bets/hour is 5.00 per 100 game-seconds to three digits, hour after hour, across ~99% of the
journal.** At 5 hardware credits that is exactly right, and the developer's expectation ("5 bets per 100
in-game seconds, so ~3 per minute") is the correct model. The anomaly is bounded and it is a *doubling*,
never a drift.

### 1.2 — During the bands, the journal interleaves two balance lines

```
19:36:16  bet=0.00529     W  bal=837.74339880   ← line A
19:36:29  bet=0.78310979  W  bal=771.14328571   ← line B
19:36:35  bet=0.00100     L  bal=837.74239880   ← A
19:36:57  bet=0.00230     W  bal=837.74465372   ← A
19:37:03  bet=0.00100     L  bal=771.14228571   ← B
19:37:15  bet=0.00100     W  bal=837.74563412   ← A
19:37:19  bet=0.00230     L  bal=771.13998571   ← B
19:37:35  bet=0.00100     L  bal=837.74463412   ← A
19:37:36  bet=0.00529     L  bal=771.13469571   ← B
19:37:53  bet=0.01216700  W  bal=771.14662424   ← B
19:37:56  bet=0.00230     W  bal=837.74688904   ← A
```

- Each line satisfies `bal[i] = bal[i-1] + net[i]` **exactly, to the satoshi**, on its own.
- Each carries its **own independent martingale progression** (A resets while B climbs
  `0.001 → 0.0023 → 0.00529 → 0.0121670`).
- Each fires at **~20 game-second spacing** — i.e. **each is individually correct at 5 credits.**
- A two-line fit explains **92.8%** of the last 20,000 records; the residual **7.1%** are auto-recharges,
  which break continuity legitimately.

> **Neither session is misbehaving. The defect is that one journal is recording two of them.**

### 1.3 — The second line is BORN mid-progression, which rules out "a session was created"

The first interleaved instant is **`2009-05-23T19:09:16`**, where two records share one timestamp:

```
19:09:16  bet=0.34048252  net=+0.33380906  bal=837.66381641   ← the arriving line
19:09:16  bet=0.00100000  net=+0.00098040  bal=770.89458247   ← the incumbent
```

`0.34048252 = 0.001 × 2.3⁷`. **The arriving line is already seven steps into a martingale ladder**, so it
was not created at that moment — it was **already running somewhere and only then began being
registered.** That reframes the whole question:

> **The trigger is not "a session started". It is "a session started WRITING".** Whatever we are looking
> for flipped a registration gate, or attached a second registrant, on an engine that was already turning.

### 1.4 — One fact that complicates the obvious story

**Which line is "the player" is not settled.** The journal's tail is on the `837` line, but the `771` line
was still writing at `22:59`, an hour after the hourly rate had returned to a single-stream 180/h. A clean
"impostor arrives, impostor leaves" story does not fit; they appear to **take turns**.

### 1.5 — The 100-second tail: explained, and it is the best instrument in the room

The journal's last hours run at **exactly 100 game-seconds per bet, perfectly periodic**, against ~20 s and
visibly jittered everywhere else. This was *not* an anomaly: the developer had **changed the player's
hardware from 5 pieces to 1** while probing the inconsistencies, and one credit is one bet per cycle —
i.e. the canonical `1 bet tick = 100 in-game seconds`, met literally.

**It confirms the hardware model at a second point, exactly**: 5 credits → 20.0 s/bet, 1 credit → 100.0
s/bet. Measured over the 1-credit stretch (2009-05-24 T20–T23, 131 records):

| bets/h | per 100 game-s | gap histogram | continuity breaks |
|---|---|---|---|
| **36** | **1.00** | 100 s ×110 · 99 s ×9 · 101 s ×9 · 98 s ×1 · 102 s ×1 | **0** |

**84% of gaps are exactly 100 s, and the balance line is unbroken end to end — a single, clean stream.**

> **Run the reproduction at 1 credit (§4).** It is a far better instrument than 5, for three reasons that
> compound: the expected spacing is a round 100 s and is *met exactly*, so a second stream shows up as an
> off-phase bet the moment it appears; there is no backlog, so the `MaxBetsPerFrame = 10` same-timestamp
> clusters that muddy the 5-credit data do not exist; and one bet per real second is slow enough to watch.
> **The cheapest way to find a second signal is to make the first one quiet and regular.**

⚠ **What this stretch does NOT establish.** At 1 credit the clock still advances ~100 game-s per real
second, so 131 records is **~2.2 real minutes** — and the doubling episodes are themselves ~2 real minutes
long and rare. A clean window the same size as one occurrence of the bug cannot distinguish *"it does not
happen at 1 credit"* from *"it did not happen this time"*. The hardware change also came with a settings
change and very likely a session restart, which is a second confound. **The 1-credit stretch is a good
INSTRUMENT and not yet evidence of a cure.**

### 1.6 — What it costs, already visible

`Max bet: 964.63272326 SC` sits in the explorer's summary beside a Bankroll of `837.66`. **A single
wallet cannot bet more than it holds** — that figure was never the player's, and it has been on screen for
some time. `Rollup.TotalBets = 215,723`, wagered, net profit and every max/streak inherit the same
contamination.

---

## 2. What is NOT established

The two live writers into the journal are `DiceGame.cs:1587` and `SimulationService.cs:588`, each guarded
by *"is the player the active node"* and **neither by "is another session already running"**.
`UserStatsService.RegisterSource` — the third possible route — is **dead code with no callers**.

| # | Hypothesis | Why it is plausible | Why archaeology cannot settle it |
|---|---|---|---|
| H1 | DiceGame's local `_session`/`_wallet`/`_betService` ticks in parallel with the delegated `SimulationService` session | Both exist by design; DiceGame both owns a session (`DiceGame.cs:1334`) and calls `StartPlayerAutobet` (`:1114`) | The journal records no author |
| H2 | An `IsPlayerActive` flip lets a bot-node session register as the player | ND.8f/OQ-ND8f.1 already documents a misattribution of this exact shape for recharges | Same |
| H3 | A `StartPlayerAutobet` while one is running leaves the previous session referenced and ticking | Would explain "already 7 steps deep" (§1.3) | Same |
| H4 | Two DiceGame instances (a scene not freed on navigation) | The bands correlate with navigating in and out of DiceGame | Same |

> **The journal cannot answer this because it never recorded who wrote each line.** That absence *is* the
> finding of §7, and fixing it is worth more than fixing whichever of H1–H4 turns out to be true.

### 2.1 — Verdicts after runs A, B and D (2026-08-19)

| # | Verdict |
|---|---|
| H1 | **Refuted by measurement.** Run B: five DiceGame round trips, including two inside two seconds. Five `ManualBetSession`s were constructed — one per scene entry, as `_Ready` does — and **not one was ever started.** |
| H4 | **Refuted by the same run**, for the same reason. |
| H3 | **Refuted where it can happen at all.** The only automatic restart is the recharge, and Run D shows it stopping session 11 on `InsufficientBalance` *before* constructing session 12 at the recharged balance. It is also **unreachable from the UI**: `StartPlayerAutobet` is called only from the toggle handler, which stops first. |
| H2 | **Unreachable by design, not merely unobserved.** The game is **Player Centered**: a bot node can be selected only while paused, and the Start controls are **disabled** while one is active, so a bot can never be the active node of a running session. Selecting a bot node exists to CONFIGURE it (load its strategy), never to run it. |

**H2 and H3 are excluded by the design rather than by evidence, and that distinction belongs in INC-003:**
if the defect reappears it cannot be either of them, whatever the code paths for `!IsPlayerActive` still
look like.

### 2.2 — H5: the one variable the runs did not have — **bots betting alongside the player**

Established from Run A's own console: the journal took **515** records while `[CasinoSC] bet#…` — which
counts *every* client's settled bet — reached **500**. **1:1, so no bot was betting.** With four bots
running the casino counter would climb roughly five times faster than the player's journal.

The historical playtest had them running. That is now the **only** known difference between the reproduced
conditions and the conditions the defect appeared in — and it fits the §1.2 signature better than anything
refuted above: **two concurrent engines at the same cadence on different wallets is exactly what a player
and a bot are.**

Mechanically it should be impossible — `ExecuteBotBet` never calls `_userStats`, and both DiceGame bot
paths (`StartBotRunners`, `RunBotManualBurst`) are guarded by `IsPlayerActive()`. **Which is why it is
worth running:** every hypothesis that looked mechanically plausible has now been refuted, so the next
candidate should be the one the evidence points at rather than the one the code suggests.

**Verdict: REFUTED (Run C, §4.8).** Four bots ran alongside the player — the trace shows five sessions,
five starts, five stops, each with its own `nodeId` and wallet — and with **8 hardware credits spread
across five bettors the player's journal took exactly the 329 records that 1 credit owes.** None of the
bots' ~2,300 bets reached it. The code was right and the evidence's suggestion was wrong.

---

## 3. Diagnostics — ✅ BUILT (2026-08-18)

All three are **in-memory or trace-only**: no `BetRecord` field, no `WorldFormatVersion` bump, and
therefore **no wipe of the evidence** — which is load-bearing, because the journal in hand is the only
known reproduction. Build clean, 0 warnings. What follows is the specification with the as-built notes
folded in.

### 3.1 — D1: name the writer

`OnBetExecutedRegisterBet` takes a `source` string; the two call sites pass `"DiceGame"` and
`"SimulationService"`. Held in memory only. `GD.PrintErr` **the first time two distinct sources register
in one process**, naming both and the game timestamp.

*This is the whole investigation in one line of output, if H1 or H4 is right.*

### 3.2 — D2: session lifecycle trace

`user://logs/session_lifecycle_trace.csv` — one row per session construction / `Start` / `Stop`, carrying
a process-monotonic session id, the owner (`DiceGame` | `SimulationService`), the node id, the wallet's
opening balance, and the game timestamp. **Delete-listed** with the other traces (`NetworkRoot.ResetWorldIfIncompatible`).

An overlap is then a two-line `grep`, and §1.3's "already seven steps deep" becomes checkable: the
arriving session's `Start` row will either exist earlier (H3) or not exist at all (H1/H4).

### 3.3 — D3: the continuity assert, with DECLARED exceptions

At the write boundary, check `bet.BalanceAfter == lastBalanceAfter + bet.CreditedProfit`. It is O(1)
against a field the journal already carries.

The subtlety that makes it usable rather than noisy: **auto-recharge, manual transfer and time-travel
balance sets legitimately break continuity.** So those paths must *announce themselves* —
`UserStatsService.NoteBalanceDiscontinuity(reason)` — and the assert fires only on an **unannounced**
break.

> **Make the legitimate exceptions declare themselves, so that silence means a real anomaly.** A check
> whose false positives are routine gets muted within a week; one that is silent by construction keeps its
> authority.


### 3.4 — As built: four decisions the specification did not anticipate

**(a) D1 and D3 merged, because the interesting report is the pair.** The spec had D1 print "two distinct
sources registered in one process". That test is *wrong*: playing manually in DiceGame and then starting a
delegated autobet legitimately produces two sources, sequentially, with nothing overlapping. What is
diagnostic is not *two sources exist* but **who wrote each side of a continuity break** — so the source is
carried into D3's report (`Written by 'X', previous by 'Y'`) along with per-source counts. One line then
answers whether there are two wallets *and* which code owns each.

> **A signal that fires on a legitimate configuration is not a signal.** The spec's version would have
> tripped on the first manual bet of every session.

**(b) `NoteBalanceDiscontinuity` DROPS the baseline instead of setting a skip-once flag.** A pending
"skip the next one" token can outlive its cause — declare a reseed, have no bet for ten minutes, and the
token silently absorbs a *real* break. Clearing `_hasLastRegisteredBalance` makes the next bet re-seed
instead, so repeated declarations before one bet are harmless and nothing lingers.

**(c) The construct row is tagged `(pre-init)`, never `unknown`.** `Owner` is assigned by the creator
through an object initializer, which runs *after* the base constructor — so every construct row would have
read `unknown` and destroyed the one word that has to carry hypothesis H4. **A sentinel that appears on
every row is not a sentinel.** `unknown` on a `start`/`stop` row now means exactly what it says: a session
nobody tagged.

**(d) `RegisterSource` was kept rather than deleted, and made loud.** §2 established it has no callers,
which made it tempting to remove — but it is a route *into* the journal that nothing guards, and the
investigation needs to know if something starts using it. It now `GD.PrintErr`s on subscription and tags
its bets `RegisterSource`, so a third writer cannot hide inside either known source's count.
*(§39.16 rule 3 says prefer deletion to a flag when something is over. This is the exception the rule
implies: it is not over — it is unobserved, and the plan exists to observe.)*

### 3.5 — Where the hooks landed

| Diagnostic | Site |
|---|---|
| Session id + `Owner`/`OwnerNodeId`, construct/start/stop rows | `BaseBetSession` — **inside the base class, not at the creation sites**, so H4 (a session nobody knows about) cannot escape the trace |
| Owner tags | `SimulationService` ×2 player + ×2 bot · `DiceGame.CreateSession` |
| Source tags | `SimulationService.cs:595` → `SourceSimulation` · `DiceGame.cs:1587` → `SourceDiceGame` · `RegisterSource` → its own |
| Trace file + delete-list entry | `SessionLifecycleTrace.TracePath`, added to `ResetWorldIfIncompatible` **with the feature** (the TL.3/ND.6b rule) |

**Declared discontinuities** — every legitimate balance jump, each stated where it happens:

| Reason | Where | Covers |
|---|---|---|
| `deposit` | `UserStatsService.RegisterDeposit` | every auto-recharge and manual Main→Bankroll transfer, on both the DiceGame and SimulationService paths |
| `autobet_session_wallet` | `SimulationService.StartPlayerAutobet` | the fresh wallet each run seeds from the bankroll |
| `manual_return` | `SimulationService.TryManualTransferToBalance` | Bankroll→Main, a **withdrawal** |
| `wallet_reseed` | `DiceGame.ReseedWalletFromBankrollSource` | every navigation/stop reseed |
| `node_state_load` | `DiceGame.LoadActiveNodeFinancialState` | player↔bot node switches |
| `checkpoint_restore` | `DiceGame` checkpoint restore | the one-shot boot restore |
| `history_rollback` / `history_cleared` | `UserStatsService` | the journal losing its tail under it |

`manual_return` is the one worth pointing at: **it has no `RegisterDeposit` to declare it on its behalf**,
because money leaving is not a deposit. Nothing in the flow implies the exemption, so it had to be stated —
which is precisely the shape this check is built to surface, and the reason the exemptions are declarations
rather than inference.

---

## 4. Reproduction protocol

**Run all of it at 1 hardware credit** (§1.5): one bet per 100 game-seconds, met to the second, so a
second stream is visible by eye in the journal without any tooling at all — and D1–D3 then only have to
say *who*, not *whether*. At 1 credit the pace is ≈ **1 bet per real second**, so a two-minute window is
~120 bets.

### 4.0 — Three corrections to the first draft of this section

The first draft asked for three things the UI cannot do, which is what a protocol written from hypotheses
rather than from the screen looks like:

| Asked for | Why it is impossible |
|---|---|
| "press **Start** again on return" | The control is a toggle. While a run is live it reads **Stop**; there is no Start to press. |
| "switch the Active Node Selector to a bot" mid-run | `SetActiveNodeSelectorLocked(true)` **disables** the selector for the whole run. |
| "start the autobet" in the idle control, under a preamble that said every scenario begins with one already running | Contradicts itself, and never said whether to stop between scenarios. |

**And H3 turns out not to be reachable from the UI at all.** `StartPlayerAutobet` is called only from the
toggle handler, which stops the previous session first — so "a second Start over a running one" cannot be
produced by a player. What replaces it is sharper: do a **normal Stop → Start** and check the trace for the
old session's `stop` row *before* the new session's `start`. **A missing `stop` is H3**, observed rather
than provoked.

> **Write the protocol against the screen, not against the hypothesis.** Three of six steps were
> unexecutable, and every one of them was a step whose shape came from what I wanted to be true.

### 4.1 — Setup, once

1. Player hardware → **1 credit**.
2. Godot console visible (D1/D3 report through `GD.PrintErr`).
3. Delete `user://logs/session_lifecycle_trace.csv` if present — it recreates itself with its header.
4. Have the **StatusBar in-game clock** in view: noting it at each maneuver is what lines the console up
   against the trace and the journal, none of which share a wall-clock timestamp.

### 4.2 — Run A: the control. **Do this one first.**

Start the autobet in DiceGame and **touch nothing for five minutes.** Do not navigate, do not open a
panel, do not switch anything. Then Stop.

*First, because if it trips here the other runs prove nothing about navigation and hypotheses H1/H4
collapse on the spot. A control that runs last is a control that only gets read when the answer is already
assumed.*

### 4.3 — Run B: navigation, **without ever stopping the autobet**

**"One continuous run" means literally this: press Start once at the beginning, and do not press Stop
until B3's wait has finished** — about **nine minutes** of uninterrupted running. That constraint is the
instrument, not an inconvenience: **with no start and no stop from the player, ANY session churn the trace
shows was not caused by the player.** A run interrupted halfway makes every `start`/`stop` row ambiguous.

The route is `DiceGame → CalendarsNavigator → BetsHistoryExplorer`, since there is no direct button, and
the return is a single hop via the explorer's own **Back to Dice**. Three scene loads per round trip
rather than two — better for this test, not worse.

| t (real) | Do | |
|---|---|---|
| 0:00 | **Start** the autobet in DiceGame. Wait 1 minute untouched. | baseline |
| 1:00 | **B1** — DiceGame → Calendar → Explorer → *Back to Dice*. Then wait 2 min in DiceGame. | the base case |
| 3:00 | **B2** — same route, but press **Go to Now** in the explorer before returning. Wait 2 min. | whether the replay scene participates |
| 5:00 | **B3** — DiceGame → any other scene → back. **Twice, quickly.** Wait 2 min. | H4, a scene not freed |
| 7:00 | Optionally repeat B1 once more, then wait 2 min. | the bands are intermittent; one pass is one sample |
| 9:00 | **Stop.** | |

Note the **in-game clock** at each maneuver.

### 4.4 — Run C: **bots betting alongside the player** (revised 2026-08-19)

**The first version of this section asked for a maneuver the game does not permit** — "switch the selector
to a bot → Start" — for the second time in this plan, and from the same cause §4.0 had already named. The
game is **Player Centered**: the Start controls are disabled while a bot node is active, so a bot can never
be the active node of a running session. Selecting one exists to CONFIGURE it, in pause, and nothing else.

> **§4.0's rule did not stick the first time it was written down.** Writing it as a lesson is not the same
> as applying it, and the second violation had the identical shape: a step whose form came from the
> hypothesis it was meant to test. The check that would have caught both is mechanical — *can I point at
> the control this step presses, and is it enabled in the state the step assumes?*

What replaces it is H5 (§2.2), and it is deliberately **Run A with exactly one variable added**:

| | Step |
|---|---|
| 1 | With the autobet **stopped**, configure the bots the normal way: selector → bot node → **Load Strategy `st1`**, for each |
| 2 | Return the active node to **player** |
| 3 | **Start.** Run **five minutes, touching nothing** |
| 4 | **Stop** |

Same 1 credit, same touch-nothing discipline, 515 clean records as the paired baseline.

**Validity check before reading anything else:** the casino's `bet#…` counter must climb roughly five times
faster than the player's journal. If it comes back 1:1 again, the bots did not actually run and the test
proved nothing — establish why before concluding.

Also expected for the first time: `start` rows with owner **`SimulationService.bot`**. Their absence would
itself answer the validity check.

### 4.5 — Run D: the auto-recharge, isolated

This one gets its own run because **forcing a recharge requires a trip to BankrollProgrammer**, and doing
that mid-run would confound it with exactly what B1/B3 test. Isolating it is what keeps a hit
interpretable.

1. **With the autobet STOPPED**, go to BankrollProgrammer and move most of the Bankroll back to Main,
   leaving only a few SC — enough that the progression exhausts it within a couple of minutes.
2. Return to DiceGame, **Start**, and then **navigate nowhere at all.** Wait for the recharge to fire on
   its own, then keep running two more minutes.

It matters most of the four maneuvers because it is the closest match to §1.3's evidence: the second line
arrived **already seven progression steps deep**, so what is being hunted is a session that was already
turning and began writing — and a recharge restart is the only thing that rebuilds a session *without the
player stopping anything*.

Run D is also Run A with one variable added, which is what makes the pair readable: same "touch nothing"
discipline, one difference.

### 4.6 — What counts as a result

**Each observation window must run well past two real minutes.** §1.5's caution applies to every one of
them: a window the same length as one occurrence of the bug proves nothing when it comes back clean. **A
negative result is only worth recording if the window was long enough to have caught a positive** — so
record the duration alongside the outcome, always.

**Repeat the whole protocol at 5 credits** once it is characterised at 1. The credit setting is the only
lever known to change the picture, so "does it also happen at 1?" is itself a finding either way.

### 4.7 — What to capture

- The full `[BetJournal] UNDECLARED balance discontinuity …` line. The decisive part is the pair
  `Written by 'X', previous by 'Y'` — `DiceGame ↔ SimulationService` alternating is **H1**, `unknown` on
  either side is a call site nobody tagged, `RegisterSource` is a third writer that should not exist.
- `session_lifecycle_trace.csv`, or the rows around the event. Three things in it, in order of weight:
  **two `start` rows with no `stop` between them**; an owner of **`unknown`** on a `start`/`stop` row
  (H4 — `(pre-init)` on `construct` rows is normal and means nothing); and **`ALREADY-RUNNING`** in the
  `note` column (H3, literally).
- The in-game clock at each maneuver, and the duration of each window.
- **If the console stays silent but the journal shows bets off the 100-second phase, say so** — that is a
  different and worse finding: the discontinuity check would be failing to detect a break it should see.

### 4.8 — Results: runs A, B and D (2026-08-19)

All three at 1 hardware credit, one continuous app process, no restart between them.

| Run | Real duration | Records | Undeclared discontinuities | Concurrent sessions |
|---|---|---|---|---|
| **A** — control, untouched | 8 m 34 s | 515 | 0 | 1 |
| **B** — five scene round trips | 11 m 25 s | 684 | 0 | 1 |
| **D** — auto-recharge | ~7 m | 268 | 0 *(1 declared: exactly +100.00000000)* | 1 |
| | **~27 min** | **1,467** | **0** | **always 1** |

Every window is several times the ~2 real minutes one historical band lasted, so each clean result is a
measurement rather than an absence of sampling (§4.6).

**Cross-checks that held throughout.** Predicted record counts from game-time span ÷ 100 matched the
journal exactly (A: 514 predicted / 515 actual · B: 684 / 684). Every trace `stop` row's wallet balance
matched the journal's last `BalanceAfter` for that session to the satoshi. The clock ran at 99.8–100.0
game-seconds per real second throughout.

**What the trace showed that static reading could not.**

- **Every DiceGame entry constructs a `ManualBetSession`** (ids 4–8 in run B, one per scene load, including
  a pair two real seconds apart from the double BlockExplorer trip). **None was ever started.** The
  default manual session is inert, which had been assumed and is now measured.
- **The recharge restart is correctly ordered**: `stop … InsufficientBalance` at `1.42416296`, then
  `construct` at `101.42416296`, then `start`. The old session is stopped before the new one exists.
- The single declared discontinuity in run D is the dose, `+100.00000000` exactly, with its paired deposit
  record — i.e. the D3 declaration mechanism did its job on its first real firing.

**The runs did not reproduce the defect**, which is itself the finding that produced H5 (§2.2): the one
condition they all lacked is bots betting alongside the player, and Run A's own console proved they were
absent (515 journal records against a casino counter of 500 — 1:1).

### 4.9 — Run C, and where four clean runs leave the investigation (2026-08-19)

**Run C** — player + all four bots on the same strategy, **8 hardware credits across five bettors**
(player 1, bot_1 1, bots 2–4 two each; one bot mining in a private pool, another in the casino pool).
Five minutes and a half, nothing touched.

The trace shows the bot lifecycle for the first time, and it is symmetric to the row:

```
09:46:38  start  SimulationService      14  player  125.52653835
09:46:38  start  SimulationService.bot  15  bot_1   103.93905480
09:46:38  start  SimulationService.bot  16  bot_2   114.50437796
09:46:38  start  SimulationService.bot  17  bot_3   109.79807906
09:46:38  start  SimulationService.bot  18  bot_4   111.16857891
18:55:27  stop   x5, all ManualStop
```

Five starts, five stops, each with its own node id and its own wallet. No orphan, none left running, no
`unknown`. **The player's journal took 329 records against a predicted 329** (32,929 game-seconds ÷ 100 at
1 credit), ending at `126.74743520` — matching session 14's `stop` row to the satoshi. **Zero undeclared
discontinuities.** The bots' ~2,300 bets did not reach it.

| Run | Real duration | Records | Undeclared discontinuities | Condition |
|---|---|---|---|---|
| A | 8 m 34 s | 515 | 0 | control, untouched |
| B | 11 m 25 s | 684 | 0 | five scene round trips |
| D | ~7 m | 268 | 0 *(1 declared)* | auto-recharge |
| **C** | ~5 m 30 s | 329 | 0 | **four bots + player, 8 credits** |
| | **~33 min** | **1,796** | **0** | |

**All five hypotheses are exhausted:** H1 and H4 refuted by measurement, H2 and H3 unreachable by design,
H5 refuted here.

#### What survives, in order of what I would bet on

1. **It needs 5 credits.** The only structural variable never tested. At 1 credit the engine is never
   frame-tight; at 5 there is backlog, same-frame groups of up to `MaxBetsPerFrame = 10`, and the clamp
   actually biting. **If the duplication is a race, it has been hunted in the one regime where it cannot
   occur — and the historical bands happened at exactly 5.**
2. **The defect no longer exists in this build.** Three commits touched `SimulationService`/`DiceGame`
   between the contaminated journal and now, including *"Fix the PAUSE button, dead since delegation
   landed"* and *"the strategy panel survives a scene round-trip"*. Either could have closed the path
   without anyone knowing it was open.
3. **It needs something still unlisted** — a multi-hour session, a block mined mid-run, something that
   only happens in real play.

#### If 5 credits also comes back clean

Then INC-003's honest conclusion is not a mechanism, and saying so is worth more than picking a favourite
hypothesis to fill the field with:

> **The defect is documented with exact forensic evidence, bounded to a date range, refuted against five
> named hypotheses, and left under a permanent sentinel that will catch it the moment it recurs.** Root
> cause: **open**.

*An incident entry whose root-fault field says "open, and here is precisely what it is not" is more useful
than one that says something plausible. The first is a starting point for whoever meets it next; the
second is a dead end wearing a conclusion's clothes.*

---

## 5. Blast radius to quantify once the mechanism is known

`OnBetExecutedRegisterBet` feeds **three** consumers, and all three take the foreign stream:
`BetHistory` (the journal), `Rollup` (lifetime totals, the thing that survives pruning), and `Stats`.

To check beyond it:

- **`CasinoScBalanceService.ApplyBetResult`** is called from both DiceGame and SimulationService. If two
  sessions run, the casino books both — so the casino's SC balance sheet may be affected too, and it is
  checkpoint-covered, i.e. it persists.
- **`CasinoClientLedgerService.RegisterSettledBet`** — same question.
- **Mining.** Every bet is a nonce attempt. A doubled bet rate is a **doubled hash rate** for the duration,
  which the difficulty regulator would have absorbed as real power. Bounded and self-correcting, but it
  belongs in the entry.
- **INC-002's streak metric cannot separate the streams** (runs are per `(GameId, Chance)`, both are
  `Dice`/`50`), so the loss/win-run figures concatenate two independent sessions — §40.8's failure mode
  wearing new clothes.

---

## 6. Disposition of the contaminated data — DECIDED: (b), wipe (developer, 2026-08-18)

Once the writer is fixed, the journal and rollup still hold the foreign records. Three options were put:

| | Option | Cost |
|---|---|---|
| **a** | Leave it; document the affected window in INC-003 | Every lifetime figure stays wrong, forever, silently |
| **b** | ✅ **CHOSEN** — `WorldFormatVersion` bump + clean wipe, **the project's own default** (§39.16 rule 4, and the developer's standing "no migrations, bump and wipe") | Loses the current playtest world |
| **c** | ❌ **REFUSED** — filter at load by balance-line analysis | Retroactive surgery on a journal, on a heuristic — the shape INC-002 warns about |

**(c) is refused on principle, not on cost:** a heuristic that silently rewrites history is a worse
failure mode than the one it repairs. Balance-line separation works *for a human reading evidence*; as a
loader it would quietly decide which of two indistinguishable sessions was "the player" and leave no trace
of having chosen.

### 6.1 — The ordering is load-bearing

The wipe is **last**, and the reason is not sentiment about the playtest world:

1. **Build D1–D3.** They were designed in §3 to be in-memory/trace-only precisely so they can run against
   **the world that already has the bug in it**. No format change, no wipe, evidence intact.
2. **Reproduce (§4)** on that same world. This is the only known occurrence; a wipe before this point
   throws away the reproduction and leaves us waiting for it to happen again by chance.
3. **Name the mechanism, fix it, write INC-003.**
4. **Archive `user://` first** — the whole directory, to a dated folder outside it, following the P15.8
   precedent (`%APPDATA%\Godot\GamblingMiner_P15.8_run_2026-07-30\`). INC-003 will cite the journal as its
   evidence, and the world reset **deletes the journal and the rollup along with everything else**. An
   incident entry whose evidence no longer exists is an anecdote.
5. **Then** bump `WorldFormatVersion` and let `NetworkRoot.ResetWorldIfIncompatible` do the wipe.

> **Wipe as the project's default, yes — but a wipe is also a destruction of evidence, and the two only
> coexist if the archive happens first.** The bump is cheap and repeatable; the reproduction is not.

---

## 7. The guard that outlives the bug

Whatever H1–H4 turns out to be, D3 ships **permanently** (`[Conditional("DEBUG")]`, O(1) per record).

> **A journal documented as belonging to one actor should ASSERT it.** This ran for at least three in-game
> days and was found by eye, from a replay built for something else, because nothing ever checked a
> property the data already carried. The same is true of `BetRecord.Id`, which existed for the whole life
> of the journal and was read by nothing until INC-002 needed it.

Related question worth answering in the same pass: **should `BetRecord` carry its author?** It is one
string on a persisted record — a format change, hence a wipe — but it would make this class of defect
self-evident forever rather than reconstructible. Decide it in the entry, not before.

---

## 8. Deliverable — INCIDENT_LOG.md INC-003

Written **last**, once §3–§5 have resolved. Per the log's format: symptom · timeline · proximate vs root
fault · evidence · blast radius · recovery · the phase that fixes it · the generalized lesson.

The lesson is already legible and will survive whichever hypothesis wins:

> **A figure nobody checks is a figure nobody can trust, and the check is usually already affordable.**
> Balance continuity was one subtraction per record against a field the journal had carried since it was
> created.

---

## 9. Out of scope

- Anything in mini-plan 04 — the explorer is complete and green; it is the *instrument* that found this,
  not a party to it.
- Hardware progression (P5). The credit setting is used here purely as an instrument (§1.5); nothing in
  this plan changes it.
- The wider "one journal per actor" question for bots (`CasinoClientLedgerService.ClientBetStats` is
  already their book) — unless §5 shows it contaminated too.
- **A direct DiceGame → BetsHistoryExplorer button** (developer, 2026-08-18). Today the route runs through
  `CalendarsNavigator`, which is where the display date is set. Wanted, deliberately not built here: adding
  a navigation path in the middle of an investigation into whether navigation duplicates sessions would
  change the thing being measured. It belongs to whatever phase follows the fix.
