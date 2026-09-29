# Mini-Plan 15 — Fail closed everywhere, and readouts that stop strobing

**Series note:** fifteenth of the *mini-plan* series, following `mini14-write-the-chain-once-plan.md`, which left
one safety item open and whose runs exposed the readout problem this plan fixes.

**Status:** 🔄 **IN PROGRESS** on branch `mini15-fail-closed-and-readouts`. **A built 2026-09-29** (15 writers
guarded). **B built 2026-09-29** — and it refuted its own spec before any run: the prescribed fixed 10 Hz cadence
would have moved the freeze to the minutes digit rather than removing it (§2 B). Both builds clean, locale
detector at baseline (10 / 0 / 0 / 0). **Next: the run** — P2 by eye, P3 and P4 from the traces, then the
torn-chain re-run for P1.

**Three parts, two of them small:**

- **A — fail closed everywhere** (from mini-plan 14's P5 gate). The world refuses to persist, bootstrap and
  checkpoint on a failed load; **every other writer still writes.**
- **B — the readouts that strobe** (the developer's report, 2026-09-29). At speed, DiceGame's clock and the
  nonce-attempt counter freeze a digit — usually the fastest-moving one — then jump. It must read as motion.
- **C — the UTXO replay, measured rather than assumed.** Mini-plan 14's rule makes T4.1 the next plan when it
  passes 40% of a block. It was 28% at height 400 and is the only cost still growing. B's runs measure it at a
  taller chain, and the rule decides. **No T4.1 work happens here.**

---

## 1. What the code says, before changing anything (read 2026-09-29)

**A — what still writes after a failed load.** `NetworkRoot.WorldLoadFailed` is consulted by exactly three
places: the world write, `HistoricalBootstrapService`, and `BlockSessionCheckpointService`. Measured during the
gate, with the world load aborted: `calendar_state.json`, `bankroll_state.json`, `principal_balance_state.json`,
`bet_stats_rollup.json` and two journal segments were **all rewritten**. They carried the real checkpoint's
values that time — which is luck, not design.

**B — why a digit freezes, and it is arithmetic, not a glitch.**
`DiceGame._Process` calls `UpdateCurrentAppTimeUI()` **every frame**, which formats the clock as
`yyyy-MM-dd HH:mm:ss` and then rebuilds the entire mining-status block (`BuildMiningStatusLine` + pool lines +
mined-block details) — also every frame. `StatusBar._Process` formats its own clock with seconds every frame.

The game clock advances `100 × DevTimeScale ÷ fps` game-seconds per frame:

| speed | per frame | what the seconds field does |
|---|---|---|
| 100X | ~1.7 s | moves one or two steps — readable |
| 1000X | ~17 s | units digit drifts, tens jump |
| **9000X** | **~150 s** | `150 mod 60 = 30`: the seconds alternate between **two values**, so the units digit sits still while the tens flip |

The nonce counter does the same thing from the other side: at 99 credits × 9000X it gains **~148 per frame**, a
near-constant step, so its last two digits cycle through a short repeating pattern. **A frozen digit is what a
near-constant step per sample looks like.** Frame jitter is why it un-freezes for a moment and then re-locks.

**So there are two independent levers**: the *step* (how often the readout samples) and the *precision* (how
many digits it shows). A display cannot show detail finer than its own sampling, and today both readouts claim
to.

**And a cost, measured by mini-plan 10's instrument:** rebuilding that whole status block 60 times a second is
per-frame UI work of exactly the shape Pattern 6 warns about — the state it reads changes on a block, not on a
frame.

---

## 2. The work

### A — fail closed everywhere

Every eager writer consults `NetworkRoot.WorldLoadFailed` before writing and says so **once**: the clock, the
Bankroll and Main balances, the lifetime rollup, the bet journal, and the remaining money services
(`CasinoScBalanceService`, `PlayerBankAccountService`, `CasinoCoinSwapService`, `ScMonetaryLedgerService`,
`CentralBankService`, `BankrollProgramService`, `CasinoClientLedgerService`, `HardwareAllocationRepository`,
`CasinoPoolRepository`). A session that could not load its world writes **nothing world-shaped**.

**✅ Built 2026-09-29.** `Scripts/Services/WorldWriteGuard.cs` asks the one question and announces a refusal
**once per writer**, to *both* the Output panel and the Errors tab (CLAUDE.md "NAME THE PANEL" — a refusal nobody
reads is a refusal that did not happen). **15 writers** call it, derived by sweeping every disk-writing file in
`Scripts/`+`Screens/` rather than from the list above — which was two short:

- **13 money/state services** — the nine named above plus `BankrollStateService`, `PrincipalBalanceService`,
  `CalendarTimeService` (`PersistCurrentTime`), `BlockSessionCheckpointService` (whose `CaptureCheckpoint` already
  refused; now its `SaveState` does too).
- **`UserStatsService.SaveRollupIfDirty`** — the same reflex as its INC-004 guard, one level up: that one asks
  *did MY file load*, this one asks *did the WORLD load*. A rollup that loaded fine is the wrong rollup for a
  world that did not.
- **`BetHistoryRepository`**, guarded at its **three disk-mutating primitives** (`WriteEntriesRotating`,
  `EnforceRetentionCap`, `DeleteAllJournalFiles`) and deliberately **not** at `Flush`: Flush still runs and still
  clears `_pendingJournalEntries`, so the session stays memory-bounded (mini-plan 12) while writing nothing.
  Guarding `Flush` would have traded a disk fault for an unbounded pending list.

**Exempt, by class, stated so the next sweep does not re-litigate it:** identity + dev conveniences (the wallet
seeds via `WalletInitializationService`, `BotWalletRegistry`, `WordlistBootstrapper`, `SavedBettingStrategyRepository`,
`NotepadService`) — not world state, and exempt from a world wipe for the same reason; and every
`user://logs/*.csv` trace (`BetCostProfiler`, `BlockCostProfiler`, `FrameCostProfiler`, `SessionLifecycleTrace`,
`FoundersMiningService`, `NetworkPopulationScheduler`) — **evidence, and a broken session is exactly when it is
worth most.** `UserStatsService`'s `.corrupt` preservation of a damaged rollup is exempt on the same ground.

### B — the readouts

1. **Sample on a fixed cadence, not per frame.** These readouts refresh at a steady interval (~10 Hz), which
   also removes a per-frame string rebuild. The number of frames per sample varies, so the step per sample
   varies with it — which is what breaks a lock.
2. **Show no unit that moves faster than the sample can show it.** The smallest displayed unit is chosen from
   the *effective* `DevTimeScale`: seconds while the clock advances ≲2 s per sample, minutes beyond that. The
   date and time above it are unchanged.
3. **The nonce counter is rounded to its own step**, and says so with a `~`, rather than printing digits that
   are noise at that speed.
4. The **StatusBar clock** gets the same treatment — it has the same bug for the same reason.

**What this must not do:** invent intermediate values to animate. A rolling counter that shows numbers between
two samples is a smoother lie, and every figure this project displays is meant to be the real one.

**✅ Built 2026-09-29 — and B.1/B.2 above are WRONG, in a way worth keeping on the page.** The spec treated the
cadence and the precision as two levers. They are **one**: a fixed cadence turns a fixed rate into a fixed step,
one unit up. At the prescribed ~10 Hz and 9000X the clock advances **~15 minutes per sample**, and `15 mod 10 = 5`
puts the identical freeze on the minutes' units digit. *The fix as specified would have moved the bug, not
removed it* — caught by doing the arithmetic before writing the code, not by a run.

**What shipped instead** (`UI/Readouts/ReadoutSampling.cs`, shared by both screens): repaint on a cadence chosen
so **the finest displayed field advances by ~2 of its own steps**, and do not display the fields below it. The
rate is **measured from the clock itself** (game-seconds advanced ÷ real seconds elapsed, EMA-smoothed) rather
than derived from `DevTimeScale`, so it follows the simulation throttle, a paused sim and a rewound calendar for
free. Verified numerically over the whole speed range before building — every rate lands at exactly 2.0 steps per
repaint:

| measured rate (game-s per real s) | field shown | repaints/s | steps/repaint |
|---|---|---|---|
| 100 (**100X**) | seconds | 50 | 2.0 |
| 1,000 | minutes | 8.3 | 2.0 |
| 7,200 | minutes | 60 | 2.0 |
| **9,000 (9000X)** | **hours** (`2010-07-18 14h`) | **1.3** | 2.0 |

So the ladder is **four rungs (seconds → minutes → hours → days), not the spec's two, and 9000X lands on hours**,
not minutes. That is the honest reading of the same rule: minutes at 9000X is either a strobe (at 10 Hz) or a
per-frame repaint (at 60 Hz, which forfeits P3). **The developer judges the look; the arithmetic only rules out
the options that cannot work.**

**The flap protection was got wrong once, in the obvious way.** The first draft used a 1.6× deadband on the unit
threshold. That band spans 75–120 game-seconds per real second — and **100X sits inside it**, so once a session
had visited a high speed the clock would never show seconds again at the game's normal speed. Replaced by a
**6-repaint confirmation streak** plus a **narrow 1.15× margin** (needed because at a boundary the streak's dwell
is asymmetric, and a 9000X run throttled to 0.8 sits exactly on the minutes/hours boundary). Verified: 100X is
seconds from every direction, and 9000X stays on hours down to a throttle of ~0.7. **A deadband on a threshold
must be checked against the values the system actually takes** — the bands are 104–110 and 6,260–7,500, and no
game speed is in either.

**The nonce counters** use the same measured step: each is floored to the largest power of ten at or below its
step per repaint and marked `~` (`~148,000`), decade bands being wide enough that jitter cannot make digits
appear and disappear. It floors and never rounds up — an attempt that has not happened must not be on screen.
`BuildMiningStatusLine` takes the finished text rather than a quantum, so the display policy stays in the UI and
the engine gains no dependency on it; `NetworkRoot.GetCandidateNonce(nodeId)` was added so the readout can
*sample* the counter it renders.

**One rebuild was deleted, not moved:** `FlushSettledBetUiIfDirty` rebuilt the whole mining-status block a second
time every frame during an autobet. Its comment argued a frame-late readout at 9000X would be visible — true of
the roll animation beside it, which still runs per frame, and not true of a block whose fastest figure is now
quantized anyway. **StatusBar** was split: the clock takes the adaptive cadence, the SC balances keep a fixed
10 Hz, because money may not sit still for the second the clock's cadence can stretch to.

### C — the UTXO replay

No build. B's runs carry `Block cost` armed, and the trace already reports rebuilds, their cost and the nodes
that made them. The plan records the share at the tallest height reached.

---

## 3. Predictions, registered before any data

- **P1 — A changes nothing in normal play.** A healthy world's files are written exactly as before; the guards
  only fire on a failed load. Verified by a normal run plus a re-run of the torn-chain case, where **no file in
  `user://` is modified** during the failed session.
- **P2 — B removes the freeze at 9000X.** The clock's smallest shown unit advances by one or two steps per
  sample at every speed, and the developer reports the motion as organic.
- **P3 — B is also cheaper.** Outside-sim time per frame falls by **≥ 0.5 ms** at 9000X in DiceGame
  (`FrameCostProfiler`'s outside figure), because a per-frame string rebuild becomes a 10 Hz one.
- **P4 — the UTXO replay passes 40% of a block by height ~800**, on the growth measured in mini-plan 14
  (0.97 → 2.37 ms from height 144 → 392, at a constant 8 rebuilds).

## 4. Decision rules, registered before the data

- **A ships when the torn-chain session writes nothing.** If any file is still modified, the writer that did it
  is named and fixed before the plan closes — the point is the absence of writes, not the count of guards.
- **B is judged by the developer's eye, and P3's number.** If the freeze persists at any speed, the sampling
  cadence was not the cause and the plan says so rather than adding animation on top.
- **P3 fails ⇒ keep the change anyway if P2 held** (the bug is the subject; the cost was a bonus), and record
  the measured figure.
- **C:** UTXO ≥ 40% of a block ⇒ **T4.1 is the next plan**, by mini-plan 14's own rule. Below 40% ⇒ record the
  height and share, and the rule waits.

## 5. Order

1. **Build A** → one build.
2. **Build B** → one build.
3. **Run** (~20 minutes at 9000X, Block cost + Frame cost armed): P2 by eye, P3 and P4 from the traces.
4. **Re-run the torn-chain case** (staged by hand, game closed) → P1.
5. **Close out**, and let C's rule choose what follows.

## 6. Out of scope

- **T4.1 itself** — measured here, built only if the rule fires.
- **Animated or interpolated counters.** Stated as rejected, not forgotten: they would show values the world
  never had at that instant.
- **The other scenes' timer-driven panels** (`ProjectDesignManual.md` Ch. 38's backlog). Same family, different
  plan.
