# Mini-Plan 05 — Who else is writing to the player's bet journal?

**Series note:** fifth entry of the *mini-plan* series, following
`mini04-bets-history-explorer-features-plan.md`, whose §13 found this while replaying history for an
unrelated reason.

**Status:** 🔬 **DIAGNOSTICS BUILT (§3), AWAITING THE REPRODUCTION RUN (§4).** Branch
`bet-journal-single-actor`. No fix yet, by design — see the objective.

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
say *who*, not *whether*.

The bands last ~2 real minutes (~1,080 bets ÷ 10/s at 5 credits) and correlate with DiceGame navigation.
With D1–D3 armed, run each of these with an autobet already active, and record which trips D1:

1. Start autobet in DiceGame → navigate to BetsHistoryExplorer → return to DiceGame.
2. As (1), but press **Start** again on return.
3. Start autobet → switch the Active Node Selector to a bot → switch back.
4. Start autobet → navigate to any other scene and back twice in quick succession.
5. Start autobet → BetsHistoryExplorer → **Go to Now** → return.
6. Idle control: start autobet, do not navigate, wait two minutes. **Must not trip.**

Note (6) is not filler: it separates "navigation causes it" from "it happens anyway and navigation only
made us look".

**Each run must last well past two real minutes.** §1.5's caution applies to every one of them: an
observation window the same length as one occurrence of the bug proves nothing when it comes back clean.
A negative result is only worth recording if the window was long enough to have caught a positive.

**And repeat the whole protocol at 5 credits once it has been characterised at 1.** The two credit
settings are the only lever known to change the picture, so "does it also happen at 1?" is itself a
finding — either way.

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
