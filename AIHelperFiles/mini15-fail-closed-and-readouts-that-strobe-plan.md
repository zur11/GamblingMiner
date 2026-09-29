# Mini-Plan 15 — Fail closed everywhere, and readouts that stop strobing

**Series note:** fifteenth of the *mini-plan* series, following `mini14-write-the-chain-once-plan.md`, which left
one safety item open and whose runs exposed the readout problem this plan fixes.

**Status:** 📋 **SPECIFIED 2026-09-29**, not started. Proposed branch `mini15-fail-closed-and-readouts`.

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
