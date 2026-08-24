# Step 17 — Direct Access, the Event-Driven Audit, and Two Objectives Held Back

> **Status: SPECIFIED, NOT STARTED (2026-08-20).** Scope fixed by the developer: **all four** objectives in
> one step, with **17.A and 17.C active now** and **17.B and 17.D suspended until A and C are implemented
> AND tested** — a gate the developer set, recorded as such rather than dressed up as a technical
> dependency.
>
> Branch (suggested): `explorer-access-and-event-driven-audit` off `main`.
>
> **World treatment: no bump expected.** Nothing in 17.A or 17.C persists. The `WorldFormatVersion` bump
> that mini-plan 05 §6 decided was **executed by mini-plan 07** — step 5, `a0c27ea`, 5 → 6, on 2026-08-23 —
> and is **already done**, independent of this step. It is not this step's to perform and not a thing this
> step waits on. *(Attribution corrected 2026-08-23: this note previously said the bump "belongs to
> **mini-plan 06**". That was the plan of record when this was written; mini-plan 07 took the wipe over as
> its own step 5, and mini-plan 06 — still shelved by D2 — now inherits it as a satisfied precondition
> rather than a task.)* The reason for keeping the two apart still stands: one is a forensic instrument and
> the other is a UI cleanup, and merging their world state would confuse both.
>
> **Prior art this step consumes rather than re-derives:** `ProjectDesignManual.md` **Ch. 38** (the whole
> event-driven principle, the genuine exceptions, the §38.5 catalogue and the §38.7 inverse failure),
> `CLAUDE.md` Important Patterns **§6**, and `PRIVATE_ROADMAP.md` **§6** (the Basic Mode gate) and **§8 T4**.

---

## 1. Why this step exists

Two of these are debts the project already named and scheduled against itself; two are objectives whose
*design* is open, not merely their implementation. Putting all four in one document is deliberate — they
were about to be four separate conversations, and three of them share a dependency the fourth creates.

| | Objective | Kind |
|---|---|---|
| **17.A** | A direct `DiceGame → BetsHistoryExplorer` button | A developer request, deferred once for a reason that expired |
| **17.C** | The **event-driven audit** — migrate the `_Process` polling backlog | A **declared Basic Mode v0.1 gate** (`PRIVATE_ROADMAP.md` §6) |
| **17.B** | The **Betting Statistics** scene — per-strategy figures | Basic Mode objective, **design open** |
| **17.D** | **T4** — the simulation-scale refactor | Standing technical objective, **open**, and INC-001's structural answer |

**The ordering is not arbitrary.** 17.C touches roughly a fifth of the scenes in the project; doing it
*after* 17.B would mean migrating a screen built the old way, and doing it after 17.D would mean
re-measuring everything 17.D measured. Cleanup first, then new surface, then the deep refactor the cleanup
makes measurable.

---

## 2. What already exists (the substrate)

- **Ch. 38's catalogue** (§38.5, audited 2026-07-21) lists **19 scenes** sharing one shape: accumulate
  `delta` in `_Process`, gate on a 1–3 s timer, then rebuild a panel unconditionally from state that only
  changes on a settled bet, a mined block or a transfer.
- **Ch. 38 §38.4** lists the events that already exist. §38.5's own note 2 is the one worth heeding:
  *"Not every candidate needs a NEW event"* — several of these predate an event that would serve them, or
  were built by copying a polling scene rather than the newest event-driven one.
- **§38.7's inverse failure** is the cautionary half: a *correct* event, fired far too often, driving
  expensive work — `CasinoCoinSwapService` recomputing chain-side availability on every settled bet, which
  became the dominant term in the sim's frame time. **Migrating a poll to an event is not automatically an
  improvement; the question is always whether the trigger can actually move the value.**
- **Mini-plan 05's diagnostics are now on `main`** and are relevant here: `SessionLifecycleTrace` and the
  journal's continuity assert. A migration that accidentally changes *when* work happens will show up in
  them before it shows up as a bug.

---

## 3. Phase map

| Phase | Scope | Status |
|---|---|---|
| **17.A** | Direct explorer access from DiceGame | ▶ Active |
| **17.C.0** | **Re-audit the §38.5 catalogue against the code** | ▶ Active — do this first |
| **17.C.1…n** | Migrate one scene per phase, in the order C.0 establishes | ▶ Active |
| **17.B** | Betting Statistics scene | ⏸ **SUSPENDED** — see §6.1 |
| **17.D** | T4 simulation-scale refactor | ⏸ **SUSPENDED** — see §6.2 |

---

## 4. 17.A — Direct explorer access

**What:** today the route is `DiceGame → CalendarsNavigator → BetsHistoryExplorer`, because
`CalendarsNavigator` is where the display date is set. The developer asked for a direct button during
mini-plan 05's testing; it was deferred *only* because that plan was measuring whether navigation
duplicates sessions, and adding a navigation path mid-measurement would have changed what was being
measured. **That reason expired when mini-plan 05 closed.**

### 4.1 — The one real design decision: what date does it open at?

| | Option | Consequence |
|---|---|---|
| **a** | **The last selected date** (`CalendarTimeService.ExplorerSelectedLocalDateTime`) | The button is a *shortcut to the same destination*. Nothing about the explorer's behaviour changes; only the number of clicks does |
| **b** | **The present** | The button becomes a *different destination* — "show me now" — and the Calendar route keeps "show me then" |

**Recommendation: (a).** `ExplorerSelectedLocalDateTime` already means "where the explorer opens", it is
already what the checkpoint restores and what `CalendarsNavigator` writes, and (b) would give one scene two
entry semantics depending on which door you came through — the kind of split that is invisible in code
review and confusing in play.

**And (b) is nearly free anyway once you are there:** the explorer arrives paused (mini-plan 04 §4.1) with
`Go to Now` one press away, so "open at the present" is a click, not a missing feature.

### 4.2 — What must NOT change

- **Arrival stays paused.** §4.1 of mini-plan 04, confirmed by the developer, and the reason a direct
  button is safe at all: a shortcut into a scene that auto-advanced would start destroying the answer it
  exists to give.
- **The back button stays origin-aware.** `SceneManager` records a one-deep `PreviousScene`, so entering
  from DiceGame must return to DiceGame — verify this rather than assume it, since the explorer's
  `OnBackToCalendarPressed` falls back to `MainMenu` when the memory is empty.
- The existing `Calendar → Explorer` route stays. This adds a door; it does not replace one.

### 4.3 — Verification

Open from both doors in one session and confirm: the same date, the same paused arrival, and a back button
that returns to whichever scene launched it.

---

## 5. 17.C — The event-driven audit

**The declared gate:** `PRIVATE_ROADMAP.md` §6 — *"before Basic Mode v0.1 is considered complete, run the
dedicated audit pass Chapter 38 describes over every currently-running `_Process` override and migrate the
poll-based UI refreshes it lists."*

### 5.1 — 17.C.0: re-audit the catalogue BEFORE migrating anything

§38.5 was audited **2026-07-21**. Since then at least two of its entries have changed underneath it:
`BetsHistoryExplorer` was substantially rebuilt by mini-plan 04 (it now appends per crossed bet and keeps
the 1 Hz path only for the wholesale snapshot), and `CasinoCoinSwaps`/`CasinoCoinSwapService` were
restructured by §38.7's coalescing fix.

> **§39.16 rule 12, applied to a list instead of a workaround: re-derive the set from the CODE, never
> trust the scope list written when it was made.** It was accurate then and the code has moved. A
> migration pass that works from a stale catalogue will "fix" something already fixed and miss something
> added since — and both failures look like success.

C.0's output is the thing every later phase reads: for each of the 19 named scenes plus anything the
re-scan adds, **(1)** does it still poll, **(2)** what state does it actually re-read, **(3)** does an event
for that state already exist (§38.4), and **(4)** does it hold live user input that the §38.5 note-1 caveat
applies to.

### 5.1a — ⚠ A constraint this plan missed when it was written (added 2026-08-21)

**Several §38.5 candidates cannot be VERIFIED in any world that exists today**, and the plan declared none
of it. Measured on the live world: chain at **210 blocks**, game date **2009-05-27**, **0 companies
founded**, and **Market Birth (2010-07-18) is 416 in-game days away**. A fresh world is *further* back
(2009-03-21), so wiping does not help and neither does waiting a little.

| Verifiable now | Suspended until Market Birth |
|---|---|
| `StatusBar`, `FinancialBettingStats`, `CalendarsNavigator`, `BetsHistoryExplorer`, `BlockExplorer`, `BTCWallet`, `ScFinances`, `ScTransactions`, `BotPlayHistory`, `CasinoFinances`, `CasinoGamblingFinances`, `ClientsBetsHistory`, `ClientsTransactions`, `FoundersWallets`, `BotsBtcWallets`, `BTCPoolsAndHardwareShop` | `AuctioningCompanyDetails`, `CompanyDetails`, `CompaniesWallets`, `CasinoCoinSwaps` — all need companies or a market, and both begin at Market Birth |

*(The split is provisional: **C.0 re-derives it from the code**, and C.0's output governs.)*

> This is **§39.16 rule 10** — *a phase whose exit depends on states the game cannot yet produce is
> SUSPENDED, with its missing precondition named.* The named precondition is **the world reaching
> 2010-07-18**. Migrating those four blind is permitted; **declaring them done is not.**

**Why it matters more than it looks.** Without this split, 17.C would be signed off with roughly a quarter
of its migrations never once executed — which is precisely the failure this project has now documented
twice in one week: mini-plan 04's emit budget, shipped to `main` without ever running, and Ch. 38's own
catalogue, which aged for a month while reading as current. **A migration nobody could exercise is
indistinguishable from a migration that works.**

### 5.2 — 17.C.1…n: one scene per phase

Ordering comes out of C.0, by measured or estimated cost — not alphabetically, and not by how easy each
looks.

**Three rules for every migration, all of them already earned:**

1. **Check §38.4 before adding an event.** The state may already be announced; several of these scenes
   simply never wired to an event that existed before they did.
2. **Frequency is part of a subscription's contract (§38.7).** Before subscribing, ask what the event's
   *real* rate is and whether the trigger can move the value at all. `BalanceChanged` fires ~20×/frame
   since ND.8f; a chain-side recompute behind it was worse than the poll it replaced.
3. **Split "always safe to rebuild" from "rebuild only on a real state-shape change" (§38.5 note 1).**
   Several of these panels poll *specifically* so an in-progress edit survives. `CompanyDetails.RebuildOrUpdateActions`'s
   signature-gated rebuild (§22.12) is the pattern; abandoning periodic refresh where user input is live is
   not.

**Each phase ships independently and is independently verifiable** — that is the whole reason this is
`C.1…n` and not one block. A scene that turns out to be harder than the catalogue suggests gets deferred on
its own without stalling the rest.

### 5.3 — Verification

Per scene: the panel still shows correct data after each of the events it now depends on, and **an
in-progress edit survives** where one is possible. Across the step: the sim's `SimulationThrottle` readout
should be no worse than before, and ideally better. **It is a measurement, not a target** (§38.7's third
rule) — if it worsens, the question is which migration did it, not whether to raise a budget.

---

## 6. Suspended phases

Both are held by the developer's sequencing decision (2026-08-20): **implemented and tested 17.A and 17.C
first.** That is a scheduling gate, and it is recorded as one rather than dressed as a technical
dependency — but each also carries a genuine reason of its own, below.

### 6.1 — ⏸ 17.B: Betting Statistics scene

**Missing precondition (developer):** 17.A and 17.C implemented and tested.
**Missing precondition (its own):** the design is open, not just the implementation.

`PRIVATE_ROADMAP.md:361` — a player-facing screen that picks a **strategy** and shows *that strategy's*
figures, instead of the lifetime totals `BetsHistoryExplorer` shows.

**Two decisions land with the screen, and the roadmap is explicit that they should be decided against it
rather than in the abstract:** (a) the strategy fingerprint must cover everything that changes what a level
*means* — base bet, both progression percents, both stop amounts, both Insist switches; and (b) storing
that fingerprint **per record** versus only per summary decides whether history can ever be re-segmented
after the fact. **(b) is a persisted-format decision, so it carries a wipe** — which is a further reason
not to start it in the same breath as a UI cleanup that carries none.

Already available to build on: the explorer's chance-to-win selector is the same idea one axis smaller, and
its **time-aware option list** — an option offered only from the moment its first bet exists — is the
pattern to copy. And **max martingale level is free at settle time** from
`BaseBetSession.ProgressionTriggerStreak`; it is **not** the same quantity as INC-002's "max consecutive
losses" and must not be conflated with it.

### 6.2 — ⏸ 17.D: T4, the simulation-scale refactor

**Missing precondition (developer):** 17.A and 17.C implemented and tested.
**Missing precondition (its own): nothing has been timed.**

`PRIVATE_ROADMAP.md` §8 T4 is INC-001's structural answer — several subsystems encode the hand-play premise
(~585 bets/block) as an invariant rather than a tuning parameter and do not degrade when the simulator runs
at 9000X.

**Its first phase is not a refactor.** The roadmap's own T4.6 is titled *"A per-block cost budget (DO THIS
FIRST)"*, and T4.0 says of its leading hypothesis: *"a strong structural hypothesis, not a profiled fact —
it has the right shape and the right growth curve, but nothing has been timed."*

> **This plan therefore does not specify 17.D's build phases, and that is deliberate.** Writing them now
> would produce exactly what §40.7 warns against — a cost note that reads as measured and never was. 17.D
> begins as **T4.6: instrument, then decide**, and its remaining shape is written after the numbers exist.

*Related and unblocked by this: the developer's `DevTimeScale 9000X` stress question is a T4 measurement,
not an INC-003 one. It belongs here, in T4.6's instrumentation, where the numbers it produces will mean
something.*

---

## 7. Out of scope

- **Mini-plan 06** — the deliberate INC-003 reproduction, its harness branch, and the
  `WorldFormatVersion` bump and wipe it sequences. Independent of this step in every respect; do not
  entangle their world state.
- **The ghost-miner typology** (four biographies instead of one, `PRIVATE_ROADMAP.md` after Step 16). Open
  design, unrelated axis.
- Anything that changes what the `_Process` overrides Ch. 38 calls **genuinely necessary** do — the game
  clock, the background sim loop, autobet pacing. 17.C migrates polls, and a poll is not the same thing as
  a per-frame job that needs real delta.
