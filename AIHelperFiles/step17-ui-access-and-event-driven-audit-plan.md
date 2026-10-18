# Step 17 — Direct Access, the Event-Driven Audit, and Two Objectives Held Back

> **Status: RE-SPECIFIED 2026-10-12, NOT STARTED.** §0 re-verifies the 2026-08-20 text against the code and
> the developer's current world, and **§0 wins where it disagrees with anything below it**. Next: the
> developer settled every §8 decision on 2026-10-12. **Unit 1 (the `UiRefreshProbe` instrument) is built.**
> **2026-10-14: §9 added.** Every test now starts from genesis and doubles as a **Rank-System calibration
> run**, and unit 2 (the `RankCalibrationTrace` instrument) is built. **2026-10-15: M0 and R1 are done**
> (§5.5 results, §9.8). M0's P1 is confirmed, so C.1 proceeds. R1 ran to 2012-02-14 and founded 18 companies,
> which lifts `CompanyDetails`'s suspension. Next: the developer picks the next calibration run (§9.9), then
> 17.A and C.1. **Chosen: R1-continued (§9.10).** The developer's two company items are placed: the donor
> count is **17.A3** (§4.5), and the bot preference goes to the Player Holdings Hub entry in the roadmap.
> **Units 3–4 (17.A, A3, A4) committed and verified 2026-10-16 (§4.8 results)**, except the button layout,
> fixed in unit 5 together with the donor wording. **The session's audit found INC-006** (§10): the crash is fixed
> in unit 5, and the root fix is decided (D-17.6 = yes). R1 is closed (§9.11). Next: one session on R1's world to
> measure the freezes (§10.1) and check unit 5's layout; **then** the INC-006 root fix + wipe; then R2. 17.A2
> follows. **2026-10-18: the freeze session ran (§10.1 results), and unit 6 is built**: INC-006's second crash site,
> the root fix, the v12 wipe, and two loudness instruments. **R2 ran 2026-10-18: every prediction confirmed, and
> INC-006 verified** (§9.13). Next: the developer chooses between the code units still open (17.A2, then 17.C.1…) and
> R3; they can overlap, R3 running in the sessions that verify the code.
>
> Scope fixed by the developer (2026-08-20): **all four** objectives in one step, with **17.A and 17.C
> active now** and **17.B and 17.D suspended until A and C are implemented AND tested** — a gate the
> developer set, recorded as such rather than dressed up as a technical dependency.
>
> Branch: **`mini25-event-driven-audit`**. It runs as mini-plan 25, and this file stays the plan of record
> (developer, 2026-10-12). *(The 2026-08-20 suggestion was `explorer-access-and-event-driven-audit`.)*
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

## 0. Re-verification, 2026-10-12 — what twenty mini-plans changed underneath this plan

Done at specification time against `main` @ `34e965d` and the developer's live world, read-only. **Every row
below was read from the code, not carried from §38.5** (§39.16 rule 12). It is 17.C.0's structural half;
17.C.0 keeps only the *measured* half (§5.5).

### 0.1 — Five facts that move the plan

1. **The suspension of §5.1a has expired for ONE of its four targets, not four.** The live world reads game
   date **2012-02-13**, chain height **1,670** — 575 days past Market Birth, so `CasinoCoinSwaps` is
   verifiable. But `CompanyFoundings` holds **0 entries**: no company has ever been founded in this world,
   so **`CompanyDetails` stays suspended**, its precondition renamed from "Market Birth" to **"a founded
   company exists"**. The other two left the list for code reasons: `AuctioningCompanyDetails` **was already
   migrated** (it has no `_Process`; it rebuilds on `NetworkRoot.BlockAccepted`), and `CompaniesWallets` is
   a 24-line subclass of `BotsBtcWallets` that lists the 40 non-miners. Those exist from genesis, so it is
   verifiable today. *(The 0 is an artefact of the TEST scenario, not a defect (developer, 2026-10-12):
   no test run has let the bots mine, so no bot holds the BTC to bid in an auction. That never happens in
   real play. `CompanyDetails`'s precondition is therefore a **test world in which bots have mined long
   enough to win an auction**.)*
2. **One migration covers three scenes.** `CompaniesWallets` and `CastMinerWallets` both inherit
   `BotsBtcWallets`'s 3 s poll. §38.5 listed only the base.
3. **The catalogue's biggest cost may not be a poll at all — it may be §38.7's inverse failure inside a
   scene.** `CasinoCoinSwaps` binds `RefreshAll` and `CasinoGamblingFinances` binds `RefreshLabels`
   **directly** to `CasinoScBalanceService.BalanceChanged`, which fires on every settled casino bet (§38.7
   measured up to ~20 a frame). `RefreshAll` rebuilds the recent-swaps list; `RefreshLabels` does a LINQ
   count over up to 500 loan records. **This is a structural reading, not a cost** (Pattern 6, instrument
   rule 4). M0 counts it before anything is changed.
4. **There is no mempool event**, and five scenes show pending figures (`StatusBar`'s BTC cell,
   `BTCWallet`, `CasinoFinances`, `FoundersWallets`, the `BotsBtcWallets` family). `BlockAccepted` covers
   confirmations, but a broadcast enters the mempool through four sites (`NetworkRoot` ×3,
   `NetworkSimulator` ×1, all calling `BlockchainService.AddTransactionToPendingTransactions`) and
   announces nothing. Today the timers are the only thing that shows a pending spend. → **D-17.1.**
5. **`CalendarsNavigator` repaints every frame a value that only its own inputs can change.** Since
   mini-plan 23 B (from mini-plan 06 §9.7e), the selection is a snapshot taken on arrival and **must never
   follow the clock**, so this is **not a poll to make live**, as the roadmap warns. It is still per-frame
   work: three calendar builds and four string formats per frame, while visible. Repainting from its own
   input events would keep §9.7e intact. It is the cheapest and least valuable item here. → **D-17.2.**

### 0.2 — The re-derived catalogue (25 `_Process` overrides under `Screens/` + `UI/`)

**A — not polls; leave them** (8):

| Scene | Why it stays |
|---|---|
| `DiceGame` | Genuine (§38.2): autobet pacing, sampled clock, edge-triggered flags |
| `BetsHistoryExplorer` | Genuine: the playback transport is real-time by definition; its view rebuild is already real-time-cadenced and append-only (mini-plans 02/04) |
| `BetHistoryContainer`, `PreviousWinnerNumbersGrid`, `PreviousWinnerNumbersRow`, `LatestWinnerNumber` | **Already event-driven.** `_Process` is a one-shot next-frame flush: `SetProcess(true)` on `BetExecuted`, `SetProcess(false)` after one flush |
| `SimRetentionReadout` | §38.3 hybrid: reads `SimulationThrottle`, which the sim writes every frame, and repaints only on change |
| `CalendarsNavigator` | §9.7e: see fact 5. Optional input-driven repaint, D-17.2 |

**B — subscribed, plus a fallback timer** (9). Here the C.0 question is the **fallback**: *which state
does it cover that no event announces?* If the answer is none, delete it (§39.16 rule 3). Otherwise, name
the missing event.

| Scene | Events today | Timer | Note |
|---|---|---|---|
| `StatusBar` | `BlockAccepted`, `MarketDayChanged` | balances 10 Hz (deliberate, mini-plan 15 B); clock sampler (§29.13); BTC 2 s | BTC fallback = the mempool gap (fact 4). The 10 Hz balance cadence is a decision, not a poll |
| `ScFinances` | `BankStateChanged`, `BalanceChanged`, `TransfersChanged`, `AutoRechargeAmountChanged`, `MarketDayChanged` | 2 s + clock sampler | Five events already; the fallback looks vestigial |
| `ScTransactions` | `BankStateChanged`, `BalanceChanged` | 2 s | Same |
| `CasinoGamblingFinances` | casino `BalanceChanged` → `RefreshLabels` **unbuffered** | 2 s + clock sampler | **Inverse-failure suspect** (fact 3) |
| `CasinoCoinSwaps` | `SwapDeskChanged`, casino `BalanceChanged` → `RefreshAll` **unbuffered**, `MarketDayChanged` | 2 s | **Inverse-failure suspect** (fact 3); live input in both quote panels |
| `ClientsTransactions` | `LedgerChanged` | 2 s | |
| `ClientsBetsHistory` | `ClientBetSettled` (live feed) | 2 s summary + rows | Per-bet event: the rebuild must stay coalesced |
| `FinancialBettingStats` | `StatsChanged`, `LedgerChanged` | 0.75 s, only while the sim runs | Already the narrowest timer in the set |
| `CentralBank` | `CentralBankChanged` → dirty + 0.5 s coalesce | 5 s | **Already the target shape**; it was built after §38.5. Fallback question only |

**C — pure timer polls, no subscription** (8 overrides, 10 scenes):

| Scene | Timer | What it re-reads | Event that would serve |
|---|---|---|---|
| `BlockExplorer` | 1 s, full `RefreshUi` | chain, balances, auction | `BlockAccepted` (+ mempool) |
| `BTCPoolsAndHardwareShop` | 1 s, casino detail only | pool/casino stats | `BlockAccepted`, `HardwareAllocationRepository.HardwareChanged` |
| `BotPlayHistory` | 1 s, bot list + table | bot bet history | `ClientBetSettled`, coalesced |
| `CompanyDetails` | 1 s, `RefreshAll` (signature-gated actions, §22.12) | company/governance | `BlockAccepted`. **Suspended: no founded company** |
| `BTCWallet` | 2 s | player BTC balances, addresses, txs | `BlockAccepted` + mempool. Live input: the send form |
| `CasinoFinances` | 2 s | casino BTC side | `BlockAccepted` + mempool. Live input: reserve mode, send form |
| `FoundersWallets` | 2 s, **five** refreshes | founders' balances, address book, txs, economics | `BlockAccepted` + mempool. Live input: send form, lottery |
| `BotsBtcWallets` (+ `CompaniesWallets`, `CastMinerWallets`) | 3 s | holder/miner lists + detail | `BlockAccepted` + mempool. Live input: send form, reactivation |

**Services** (6 overrides outside the scope above, read for completeness): `CalendarTimeService` and
`SimulationService` are genuine. `BtcMarketDataService`/`BtcNetworkDataService` are the §38.3 reference.
`CasinoCoinSwapService` is §38.7's coalescer. `CasinoClientLedgerService` is a dirty-flag disk flush. **None
are migration targets.**

### 0.3 — 17.A: the recommendation flips

§4.1 recommended opening at **the last selected date (a)**, because `ExplorerSelectedLocalDateTime`
"already means where the explorer opens". That premise moved. Since mini-plan 23 B, **every arrival at the
Calendar from anywhere but the explorer re-seeds the selection to the present**, on the developer's stated
intent (mini-plan 06 §9.7e). Nothing re-seeds it on the way back to DiceGame: `← Back` writes the selection
only when its target is the Calendar. So under (a), a player who chose 2010-03-01 in the Calendar, played a
game-year in DiceGame and then pressed the new button would land on 2010-03-01. **That is §9.7e's "the
default position is the least useful one" defect, reopened through a new door.**

**New recommendation: (b), the present.** The rule then reads the same for both doors: *arriving from
anywhere but the explorer means "now"*. That is the rule the developer already chose for the Calendar. The
"two entry semantics" objection in §4.1 inverts: under (a), it is the direct door that would disagree with
the Calendar door. → **D-17.0, the developer's call.**

*(Side note, no action: `CalendarTimeService.ExplorerSelectedLocalDateTime` is initialised to wall-clock
`DateTime.Now`. The bootstrap landing and three of `EnsureGameEpochInitialized`'s four branches overwrite
it. The fourth, an unparsable `calendar_state.json`, does not. `BlockSessionCheckpointService` (#13) then
overwrites it on every boot anyway: the pre-genesis reset always does, and the checkpoint restore does
whenever the snapshot carries `CalendarLocalTicks`. The default therefore survives only if an unparsable
calendar file meets a checkpoint without a clock. A corrupt-file corner, **not chased further** and not
acted on. It is recorded so nobody "fixes" it as a game-time violation without reading this.)*

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

> **⚠ Superseded 2026-10-12 by §0.3 — the recommendation is now (b).** The paragraph below is kept as
> written, because its reasoning was right for the code it read.

**Recommendation (2026-08-20): (a).** `ExplorerSelectedLocalDateTime` already means "where the explorer opens", it is
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
that returns to whichever scene launched it. *(Under D-17.0 = (b), "the same date" means: both doors open at
the present when entered fresh. From the Calendar door that holds only if its own date was left
untouched.)*

### 4.4 — 17.A2: DiceGame's "Numbers" view — PROPOSED fold-in (developer's yes needed, D-17.3)

`PRIVATE_ROADMAP.md` §6 asks for this small item to be folded into "whichever UI plan comes first", and 17.A
already opens `DiceGame.tscn`. Its cost is already solved: `PreviousWinnerNumbersRow` (last 10) and
`LatestWinnerNumber` (one), both live in the explorer behind a toggle since mini-plan 23. **Two
preconditions carried from §6:**

- **Fix the old grid's geometry first.** It has never been on screen, starts at y 534 under the Bet View
  button, and its 540 minimum height runs into the bottom danger band (Ch. 29 §29.11).
- **Read Ch. 29 before touching the scene** (CLAUDE.md). DiceGame's buttons are absolutely positioned,
  and 17.A adds one more button to the same scene.

If the developer declines, it goes back to §6 untouched.

### 4.5 — 17.A3: the donor count in `AuctioningCompanyDetails` (developer, 2026-10-15)

**The request:** show the number of donors in `AuctioningCompanyDetails`, counting **only the player and the
casino miners** (`bot_1..4`).

**What the code does today** (`NetworkRoot.ComputeAuctionLedger`):
- `AuctioningCompanyDetails` shows **no donor figure at all**.
- The only one is `NonMinerDonationSummary.DonorCount`, shown in `BlockExplorer`'s auction rows as
  `(N donor)`. It counts **every distinct sender** to the company's address: cast miners and the non-miner
  economy included, whose sends are economy-only and never bids (D-EB.7).
- So the figure the player sees today already disagrees with the developer's definition.

**Build:**
- Add `BidderCount` to the summary. It is the distinct donors of the same `list`, filtered by the
  `qualifyingBidders` set the same method already builds: player addresses canonicalised to the base
  address, plus `bot_1..4`'s owned addresses.
- Show it in `AuctioningCompanyDetails`'s status line.
- **It has a closed-form bound: 0 ≤ `BidderCount` ≤ 5**, since only five identities qualify. Assert it under
  DEBUG (CLAUDE.md, Pattern 2: "where a displayed figure has a cheap closed-form bound, assert it").

**D-17.4 — `BlockExplorer`'s `(N donor)`.** Recommendation: **show `BidderCount` there too**, under the same
label. Two screens giving two different "donor" numbers for one company is Convention 6's failure. If that
leaves `DonorCount` with no reader, delete it (Convention 3), after grepping every consumer (Conventions 12
and 13).

**Verify** on R1's world, which has auctions in progress:
- the count matches the distinct qualifying senders, recomputed from the chain;
- it never exceeds 5;
- the two screens agree.

**Decided by the developer, 2026-10-15:**
- **D-17.4 = yes.**
- The rest of the funding gets a name: **"Company's BTC Income"**, the BTC received from every wallet that is
  not the player or a casino miner. It updates constantly.

**Built (2026-10-15):**
- `NonMinerDonationSummary.TotalReceived` and `DonorCount` are **deleted**. They mixed bids with income and
  counted sender *addresses*. Their only reader was `BlockExplorer`'s row (grepped).
- In their place:
  - `CompanyBtcIncome`: the BTC from every non-qualifying sender;
  - `BidderCount`: distinct qualifying **identities**. A bot owns several addresses since P16.2, so it is
    mapped to its node id first.
- **`BlockExplorer`'s row:** `BTC income X BTC | N donors`.
- **`AuctioningCompanyDetails`:** a second status line, `Donors: N (player + casino miners) | Company's BTC
  Income: X BTC`.
- **Update rate:** "constantly" means at every block. The page rebuilds on `BlockAccepted`, and income only
  exists once a block confirms it; a payment still in the mempool is not income yet.
- **The 0..5 bound** is asserted under DEBUG: `[AuctionLedger] … counted twice`, latched, printed with
  `GD.Print` and `PrintErr`.

### 4.7 — 17.A4: a company's funding in two phases (developer, 2026-10-15)

**The request:**
- a label with the total BTC donated by the five auction participants (player + `bot_1..4`), live during the
  auction and then kept as the total donated to found the company;
- the other wallets' income becomes **"Total Foundational Income"** at founding;
- "Company's BTC Income" restarts from 0 after founding;
- every post-founding receipt is valued in SC **at its own day's price** and summed, never re-priced.

**The rule, per receipt at the company's address** (`ComputeAuctionLedger`; all derived from the chain on every
computation, nothing persisted, so no wipe):

| When | From | Counts as |
|---|---|---|
| Confirmed by the window's close | a qualifying bidder | **Donated** (`DonatedBtc`), ignored same-block bids included since they are never refunded. The donor count is taken over the same set |
| After the close | a qualifying bidder | nothing: a late bid that `CancelAndRefundStaleAuctionBids` refunds |
| Before founding | anyone else | **Foundational income** (`FoundationalIncomeBtc`). It is the live "Company's BTC Income" during the auction |
| From the founding block on | anyone else | **Income since founding** (`IncomeSinceFoundingBtc`), plus `IncomeSinceFoundingSc` at `GetEffectivePriceUsd(receipt's own date)`. Pre-Market-Birth receipts are reported as unpriced BTC, never as 0 SC |
| Any time | the company's own addresses, or memo `SEIZED` / `COLLATERAL` | nothing: transfers, not income |

The founding split uses `CompanyFounding.FoundedAtUnixMs`, the resolving block. The donation split uses the
window close, which is the auction's own boundary (ND.8d.7).

**Where it shows:**
- `AuctioningCompanyDetails`: `Donors · Donated · Company's BTC Income`;
- `BlockExplorer`'s auction rows: `donated X BTC by N donors | BTC income Y BTC`;
- `CompanyDetails`: two new status lines, the founding figures (donated, donors, Total Foundational Income),
  then income since founding in BTC and in SC.

**A note this narrowed.** D-ND4b.10's comment said *"NOTHING in this system displays a frozen
historical-day value"*. That stays true of the leading bid, and is now scoped to it, because this feature
adds a frozen value on purpose.

**Measured before building:** in R1's world, the post-founding receipts at company base addresses come from:
- cast miners' sell-flow, by far the most (laszlo 3,418 BTC, tcatm 2,813, artforz 2,786, …);
- 725 BTC from derived (change) addresses, which the code resolves through `OwnedAddressSet` and the offline
  survey could not.

**No company was founded before Market Birth** (the first was ArtForz Cluster, 2010-09-24), so the unpriced
branch cannot be exercised in this world.

### 4.8 — Verification V-A, one session (supersedes §4.6), inside R1-continued (§9.10)

**Predictions:**
- **V-A1:** the direct button always arrives at the present, paused.
- **V-A2:** `← Back` returns to the opening scene.
- **V-A3:** donor counts agree across screens and stay within 0..5.
- **V-A4:** no `[AuctionLedger]` line appears.
- **V-A5:** an auction's `donated` and `income` never decrease.
- **V-A6:** a founded company's founding figures are **identical** on two visits blocks apart, and its income
  since founding (BTC and SC) only grows.

**Read from `godot.log`'s `[CompanyFunding]` lines (DEBUG):** V-A5 from the per-block auction lines, V-A6 from
the two `founded` lines. **Refuted if** any step or log line differs.

The steps are the ones given to the developer in the chat on 2026-10-15, kept here verbatim in substance:
1. resume R1 (§9.10);
2. check the button;
3. button → present → `← Back`;
4. the Calendar-old-date regression;
5. the Calendar route;
6. `BlockExplorer` auction rows;
7. an in-auction company's Details for ~1 real minute;
8. **ArtForz Cluster**'s Details, then leave for 2–3 minutes and open it again;
9. no `[AuctionLedger]` in the Output panel;
10. keep R1 running to at least **2013-01-01**, which covers the first halving at block 2,100.

#### V-A results (session of 2026-10-15/16, R1-continued, h1,691 → 2,327)

| | Verdict | Evidence |
|---|---|---|
| **V-A1/A2** (navigation) | ✅ by the developer's report | No navigation defect reported |
| **Button layout** | ❌ **FAILED, fixed** | The developer's screenshot: when the autobet starts, DiceGame shifts its nav column right to clear PAUSE (`ApplyNavShiftIfDirty`), and the new button was not in the shifted list, so the column slid over it. **Fix (developer's layout):** all six buttons at **half height and half font**, with "Bets Explorer" **below** "Calendar Navigator", in the shifted list |
| **V-A3** | ✅ | `donors=4` throughout, the same on both screens |
| **V-A4** | ✅ | No `[AuctionLedger]` line in `godot.log` |
| **V-A5** | ✅ | Coinwash (`non_miner_19`), h1,743–1,781: `donated` only rose (2.49 → 1,451.43 BTC); `income` stayed 0.00 |
| **V-A6** | ✅ | ArtForz Cluster at h1,726 and h1,784: **every figure identical**, founding and running alike, since no income arrived in those 58 blocks |
| **Wording** | changed | The developer asked for the donor count without "(player + casino miners)". Removed on both details pages |

**Found during the audit, outside V-A:** INC-006, §10.

### 4.6 — Verification protocol V-A (17.A + 17.A3) — *superseded by §4.8*

**Predictions:**
- **V-A1:** the direct button opens the explorer **at the present**, paused, from any prior state.
- **V-A2:** `← Back` returns to the scene that opened the explorer.
- **V-A3:** the two screens show the same donor count, never above 5.
- **V-A4:** no `[AuctionLedger]` line ever appears.

**Refuted if** any step below differs.

1. DiceGame, autobet running. **Look:** the **"Bets / Explorer"** button sits right of "Calendar Navigator",
   both lines readable and nothing clipped.
2. Press it. The explorer opens at **the clock's current date and time**, **paused**. Press **"← Back"**: you
   return to **DiceGame**.
3. **The regression test.** DiceGame → Calendar Navigator → set an **old date** (any 2010 date) → Apply →
   open the explorer: it shows that old date. Now press **"Back to Dice"**, then the **direct button**: it
   must open at **the present**, NOT the old date.
4. The Calendar route still works: DiceGame → Calendar Navigator (it shows the present) → open the explorer
   → same date, paused → **"← Back"** returns to the **Calendar**.
5. DiceGame → BlockExplorer → toggle **Auction / Company Mode**. Each in-auction row reads `BTC income …
   BTC | N donors`, with **N between 0 and 5**.
6. Press **Details** on an in-auction company with bids. Its second status line reads `Donors: N (player +
   casino miners) | Company's BTC Income: … BTC`, and **N equals that row's N**. Wait for two or three blocks:
   the income figure may grow. It must never shrink.
7. *Godot editor's Output panel:* **no** `[AuctionLedger]` line for the whole session.

> **The developer's second company item, bots preferring companies with more BTC received, is NOT in this
> plan.** It changes the referral auction's bidding rule, so `REFERRAL_AUCTION.md` §1 and the bid-probability
> panel would move with it (Convention 6). Doing it mid-plan would also change the bots' BTC behaviour halfway
> through R1-continued. It is specified in `PRIVATE_ROADMAP.md`, in the Player Holdings Hub entry.

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

> **⚠ Resolved 2026-10-12 by §0.1 fact 1.** Of the four, only **`CompanyDetails`** is still suspended, and
> its precondition is now **"a founded company exists"**: the live world is past Market Birth with 0
> companies. `CasinoCoinSwaps` and `CompaniesWallets` are active, and `AuctioningCompanyDetails` is already
> migrated. The rule this section states (§39.16 rule 10) still governs `CompanyDetails`.

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

**Exit criterion, stated as COUNTS (added 2026-10-12, CLAUDE.md Pattern 6 instrument rule 3).** A
millisecond or retention figure does not reproduce across sessions here, so it is supporting evidence
only. For every migrated scene, the probe of §5.5 must read:

- **at rest** (sim stopped, no input): **0 refreshes per second**. *A panel at rest does no work.* Today
  every B/C scene does 0.2–1 per second.
- **under the sim**: **≤ 1 ÷ its coalesce interval** (e.g. ≤ 4/s at 0.25 s). It must never track the
  settled-bet rate.
- plus §5.3's two functional checks: correct data after each event it depends on, and an in-progress edit
  that survives.

### 5.4 — Order of work (2026-10-12)

One unit = one stage → message → commit loop. Instrument, then measure, **then** change anything.

| # | Unit | Notes |
|---|---|---|
| 1 | **C.0 instrument** — a DEBUG-only `UiRefreshProbe`: per-scene refresh counts per real second, one `GD.Print` line every 5 s while any count is non-zero | `[Conditional("DEBUG")]` like `ExplorerFrameProbe`; one call at each B/C refresh entry point, plus a count of `AddTransactionToPendingTransactions` successes (D-17.1's rate). It stays as the exit-criterion instrument |
| 2 | **M0** — baseline measurement, §5.5 | No code change between 1 and 2 |
| 3 | **17.A** + **17.A2** (D-17.3 = yes) + **17.A3** (§4.5) | Ch. 29 first. A3 touches `NetworkRoot`'s ledger and two scenes |
| 4 | **C.1** — `CasinoCoinSwaps`, `CasinoGamblingFinances`: route casino `BalanceChanged` through a dirty flag + coalesce, `CentralBank`'s shape | Only if M0 confirms P1. If refuted, it drops to the B group |
| 5 | **C.2** — the mempool event (if D-17.1 = yes), then one unit per chain-side scene: the `BotsBtcWallets` family, `BTCWallet`, `CasinoFinances`, `FoundersWallets`, `BlockExplorer`, `BTCPoolsAndHardwareShop` | Within the group, M0's counts set the order. Live input: §38.5 note 1 |
| 6 | **C.3** — the B group's fallbacks, one scene per unit: delete each, or name the state it covers | `StatusBar`'s 10 Hz balance cadence is out of scope (a decision, mini-plan 15 B) |
| 7 | **C.4** — `BotPlayHistory` onto `ClientBetSettled`, coalesced | Per-bet event: §38.7 rule 1 |
| 8 | **C.5** — `CalendarsNavigator` input-driven repaint, only if D-17.2 = yes | Must not subscribe to the clock (§9.7e) |
| 9 | **C.6** — `CompanyDetails` | **Verifiable since R1 (2026-10-15): 18 founded companies.** No longer blind, provided it is verified on R1's world or another that has companies |
| 10 | **M1** — the M0 protocol again, same world and settings, then close-out | |

### 5.5 — Measurement M0: predictions registered BEFORE the run (2026-10-12)

**Conditions, each with the reason it matters.** *(World changed 2026-10-14: the developer wants every test
from the original genesis, so M0 runs at the start of rank calibration run R1 (§9) on a fresh world, not
on the 2012 world. Nothing in P1/P2 needs the later era. `CasinoCoinSwaps` is pre-Market-Birth there and
shows its locked teaser, but its `BalanceChanged` subscription is unconditional, so P1 is still tested. R1's
bots are betting too, which raises the casino's `BalanceChanged` rate and can only make P1 easier to
confirm, never harder.)* Fix `DevTimeScale` at **9000X** for the whole run: the per-bet event
rate, and with it P1, scales with it. Run a **player autobet**: without settling bets the casino
`BalanceChanged` never fires and P1 cannot be tested. Keep **hardware credits** unchanged for the whole run
and record them. They change bets per frame, but no figure here crosses sessions, so any fixed value works.
**One session.** Retention is compared within it only (instrument rule 3).

| | Prediction | Read from | Refuted if |
|---|---|---|---|
| **P1** | With the sim running, `CasinoCoinSwaps.RefreshAll` and `CasinoGamblingFinances.RefreshLabels` each run **≥ 100 times per real second**. Their timers alone give 0.5/s | the probe's lines in **the Godot editor's Output panel** | either reads **< 10/s**. Between 10 and 100: confirmed in kind, not in size |
| **P2** | In the same session, `Sim: NN%` reads **≥ 10 points lower** in `CasinoCoinSwaps` than in `ScTransactions` (both host the `StatusBar`; `ScTransactions` has no per-bet subscription) | the `Sim: NN%` readout in each scene's **StatusBar**, read after 30 s in each | the gap is **< 5 points**. 5–10: inconclusive, and C.1 proceeds on P1's count alone |
| **Canary** | With the sim **stopped**, every B/C scene reports its timer rate (0.2–1/s). This checks the instrument, not the code: a zero means a probe call is missing | Output panel | — |
| **Recorded** | The mempool add rate under the sim, D-17.1's input. **No prediction**: nothing derivable bounds it | Output panel | — |

**The instrument (unit 1, built).** `Scripts/Diagnostics/UiRefreshProbe`, armed by the **"↻ UI refresh"**
toggle beside "⏱ Block cost" in DiceGame's DEV block. Every 5 s of real time it prints one line:
`[UiRefresh] 5.0 s in <scene> | sim RUNNING, retention NN% | <site> N.N/s · … | mempool adds N.N/s`.
The lines go to the Godot editor's Output panel and to `user://logs/godot.log`, **which is where the run is
read**, so nothing is transcribed. Retention is the Sim% figure averaged over the window, which puts P2 in
the same line as P1. 18 count sites cover the 17 panels; `ClientsBetsHistory` has a second one on its
per-bet feed.

#### M0 protocol

**Settings, each one a condition that changes a reading:**
- a **DEBUG** run, i.e. launched from the Godot editor, because the probe does not exist in an export;
- **DEV time scale 9000X**, which is not persisted, so set it after launching;
- a **player autobet that keeps running for the whole of leg B**: **Stop on block mined OFF**, **Auto
  Recharge ON**, and your usual strategy otherwise. If it stops anyway, the log shows `sim stopped` in that
  window and the window is discarded, so restart it and carry on.

Hardware credits are read afterwards from `hardware_allocation.json`. Nothing to set.

1. Launch from the Godot editor. Main Menu → **DiceGame**. **Layout check:** "↻ UI refresh" sits on the same
   line as "⏱ Block cost", and nothing in that DEV block overlaps the chance slider.
2. Set the DEV time scale to **9000X**. Tick **"↻ UI refresh"**. *Godot editor's Output panel:*
   `[UiRefresh] ARMED`.
3. **Leg A, the canary (no autobet).** Stay **10 s in DiceGame**. *Expected:* one `no refreshes` line, which
   shows the quiet report works. Then Main Menu → ScFinances → **ScTransactions, 15 s** (*expected:*
   `ScTransactions.RefreshAll` ≈ 0.5/s). Then Main Menu → **BotsBtcWallets, 15 s** (*expected:* ≈ 0.3/s).
4. **Leg B (autobet running).** Main Menu → **DiceGame**. Start the autobet. Stay **15 s**.
5. Visit in this order. Times are minimums and longer is fine. Move between scenes by any route.
   - Main Menu → **ScFinances 15 s** → **ScTransactions 30 s** (P2's control)
   - ScFinances → **CasinoCoinSwaps 30 s** (P1, P2)
   - Main Menu → **CasinoGamblingFinances 15 s** (P1) → **ClientsBetsHistory 15 s** → back →
     **ClientsTransactions 15 s**
   - Main Menu → **CentralBank**, **CasinoFinances**, **FoundersWallets**, **BotsBtcWallets**,
     **CompaniesWallets**, **CastMinerWallets**, **BotPlayHistory**, 15 s each
   - Main Menu → DiceGame → **BlockExplorer 15 s**; DiceGame → **BTCWallet 15 s**; DiceGame →
     **BTCPoolsAndHardwareShop**, **select the casino pool, 15 s**. Selecting the casino matters: its
     refresh only runs while the casino is selected.
6. Return to DiceGame, stop the autobet, close the game, and say so in the chat. Don't relaunch before the
   log is read: a launch rotates `godot.log` (the old one is kept, but it has to be identified).

No selection is asked for elsewhere: every other count sits at the top of a refresh that runs whatever is
selected. `CompanyDetails` cannot be reached, because no company exists (§0.1 fact 1).

**Analysis rules (fixed now):** the first window reported in each newly entered scene is discarded, because
it began in the previous scene.

#### M0 results (run 2026-10-14/15, R1's fresh world, 9000X, every client at 1 credit)

Read from `user://logs/godot.log`, lines 42–568: 137 windows, each the median of its scene's
non-transition windows.

| | Verdict | Measured |
|---|---|---|
| **P1** | ✅ **CONFIRMED, decisively** | `CasinoCoinSwaps.RefreshAll` **450/s**, `CasinoGamblingFinances.RefreshLabels` **450/s**. That is exactly 5 clients × 90 bets/s: each rebuild fires once per settled casino bet. `ClientsBetsHistory.OnClientBetSettled` runs at the same 450/s (the per-bet feed) |
| **P2** | ❌ **REFUTED** | Retention read **100% in every window of the run**, `CasinoCoinSwaps` included. At this load (5 credits in total) the per-bet rebuilds cost too little to move retention, and a saturated readout cannot show a difference below its ceiling (instrument rule 5) |
| **Canary** | ✅ passed | `ScTransactions` 0.4–0.6/s, `BotsBtcWallets` 0.2–0.4/s, and the explicit `no refreshes` line in DiceGame at rest |
| **Mempool adds** | recorded | **0–0.2/s**, all of it in the founder-transaction era: **no fee-paying traffic exists before Market Birth**, so this is a floor. D-17.1's rate must be re-measured after 2010-07-18 before it is relied on |

**Rates for C.n's ordering**, each the median under the sim:

| Rate | Panels |
|---|---|
| ~450/s | the two P1 sites, plus the `ClientsBetsHistory` feed |
| ~5/s | `FinancialBettingStats`: the 250 ms `StatsChanged` throttle plus its 0.75 s timer |
| 1/s | `BlockExplorer`, `BTCPoolsAndHardwareShop`, `BotPlayHistory` |
| 0.4–0.6/s | `BTCWallet`, `FoundersWallets`, `ScFinances`, `ScTransactions`, `ClientsTransactions`, `ClientsBetsHistory`'s rebuild |
| 0.2–0.4/s | `BotsBtcWallets` and its two subclasses |
| 0.2/s | `CentralBank` (its fallback alone; no FED event fired) |

**Leaks:** none. Every per-bet rate fell to 0 in the first full window after leaving its scene.
**Not measured:** `CasinoFinances`. The tour visited `CasinoGamblingFinances` twice instead; by code it is
a 2 s timer, i.e. 0.5/s.

**What it means for C.1.** By §5.4's rule C.1 proceeds, since P1 is confirmed. But its benefit will **not**
show as retention at 5 credits. It is justified by the count (450 rebuilds a second for a value no single
bet can visibly move) and by load: the same subscription at 99 credits would fire about 20× as often. C.1's
exit criterion is therefore a count, `≤ 4/s` at a 0.25 s coalesce, never a retention figure. A scene's rate is the median of its remaining windows. P2 compares the median
retention of `CasinoCoinSwaps` against `ScTransactions`.

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
- **Producing a world with founded companies** (§0.1 fact 1: a bot-mining test run). It only gates
  `CompanyDetails`'s verification. Building that world is not part of this plan.

---

## 8. Decisions at specification (2026-10-12) — the developer's

**Taken 2026-10-12:** D-17.1 = **the event** · D-17.2 = **leave it** · D-17.3 = **yes** · branch
`mini25-event-driven-audit` · **17.A is kept** (first asked for 2026-08-18, mini-plan 05 §7, and
re-confirmed) · D-17.0 = **(b)**, by the developer's rule **"always arriving at the present, both
ways"**: DiceGame → Explorer opens at the present, and Explorer → DiceGame shows the present. The return
direction already holds: the explorer never moves the world clock, and DiceGame's checkpoint clock restore
runs once per launch, never on re-entry. 17.A's verification checks it anyway.

| # | Decision | Recommendation |
|---|---|---|
| **D-17.0** | Where 17.A's button opens the explorer: **(a)** the last selected date, or **(b)** the present | **(b)**, §0.3: one rule for both doors, the rule §9.7e already chose |
| **D-17.1** | Add a mempool event (`NetworkRoot`, fired from the four add sites, consumers coalescing), or keep a long fallback timer only for pending figures | **The event.** Otherwise five scenes keep a timer for one gap. Its rate is measured at M0 first (§38.7 rule 1) |
| **D-17.2** | `CalendarsNavigator`: repaint from its own inputs (C.5), or leave it per-frame | **Leave it** unless M0 shows a cost. It runs only while that scene is visible, and §9.7e makes it the scene where a mistake costs most |
| **D-17.3** | Fold DiceGame's "Numbers" view into 17.A as 17.A2 | **Yes.** Same scene and the same Ch. 29 read, and its cost was solved by mini-plan 23 |
| **Branch** | `mini25-event-driven-audit` or `step17-event-driven-audit` | Either. The plan of record stays this file |

---

## 9. Rank calibration runs — every test from genesis (developer, 2026-10-14)

**The developer's decisions, recorded as given.** Every test from now on starts from the **original
genesis**. Throughout, the plan tracks **the cumulative SC each casino player has wagered since the start**.
That figure is what the **Casino Rank System** levels will be measured on: **20 to 33 levels in total**, with
thresholds preferably at **round numbers**, found by running tests with **different bet amounts** and
seeing how many levels a player reaches on average over the game's years. The same data is to inform
**minimum bet amounts**.

This section produces data. It does **not** design the Rank System: that is still its own design plan
(`PRIVATE_ROADMAP.md`, "Casino Rank System"), which these runs feed.

### 9.1 — What is already determined before any run (arithmetic, not measurement)

Each figure is derived from a canonical rule and labelled as derived, so no run is spent re-measuring it.

- **Bets per game-year are fixed by credits.** The 100-second rule gives a node `credits ÷ 100` bets per
  game-second, so **315,576 bets per credit per game-year**. The game's span runs from the player start
  (2009-03-21) to where both historical datasets end (2025-12-31): 6,130 days, **16.78 game-years**, i.e.
  **5,296,320 bets per credit**. *(Assumed span; the developer to confirm, §9.6.)*
- **Expected loss is 0.98% of wagered, for EVERY strategy and every win chance.** The RTP is fixed at
  99.02% and a progression changes only the variance, never the edge.
- **Consequence: without income, expected lifetime wagered is capped at capital ÷ edge = 40,000 / 0.0098 ≈
  4.08M SC, whatever the bet size.** Bet size only sets *when* the cap is reached. With 1 credit and a flat
  bet, the break-even bet that exactly spends the capital over the whole span is **≈ 0.77 SC**:

  | Flat bet, 1 credit | Wagered / game-year | Expected ruin | Lifetime wagered (span-capped) |
  |---|---|---|---|
  | 0.01 SC | 3,156 | never within the span | 52,963 |
  | 0.1 SC | 31,558 | never within the span | 529,632 |
  | 0.5 SC | 157,788 | never within the span (25.9 yr) | 2,648,160 |
  | 1 SC | 315,576 | 12.9 yr | 4.08M |
  | 2 SC | 631,152 | 6.5 yr | 4.08M |
  | 5 SC | 1,577,880 | 2.6 yr | 4.08M |

  These are expectations. The spread is small at these bet counts: one standard deviation of net over
  the span is ≈ base bet × √bets, about 1,800 SC at 0.77 SC against a 40,000 capital.
- **So every rank level above ~4M SC is reachable only through income**, which in Basic Mode means mined
  BTC turned into SC at the swap desk after Market Birth. The top of the ladder is therefore a question about
  mining, the core loop, and that is what the runs must measure. A player's own wagered below the cap
  needs no run at all: it is bets × average bet.
- **The minimum bet is the price of a nonce attempt.** Today it is **1 satoshi** (`DiceGame`'s MIN button
  sets 0.00000001), so a player can advance time and mine at essentially no risk. A minimum bet `m` costs
  `0.0098 × m` SC per nonce attempt, and puts the floor of the rank ladder at `m × 5,296,320 × credits`
  lifetime. **The minimum bet and the rank floor are one decision**, and these runs inform both.

### 9.2 — The instrument

`Scripts/Diagnostics/RankCalibrationTrace` (DEBUG-only) writes **one row per block checkpoint** to
`user://logs/rank_calibration_trace.csv`. Each row has the game date and chain length, then:
- for the player: the lifetime rollup's bets, wagered and net, and the SC in Main, Bankroll and Bank;
- for **each of the five casino clients** (`player`, `bot_1..4`, from the casino's client book): credits,
  bets, wagered, net and BTC held.

It writes inside `BlockSessionCheckpointService.CaptureCheckpoint`, the one path every capture takes, from
the snapshot itself. So a row is always committed state: a restart never rolls one back, and a run can be
continued across sessions on the same world. Bots are valid samples, because they recharge from their own
Main Balance and drop out when it is empty, with no FED loan (`SimulationService`). They are bounded by
capital exactly like the player.

### 9.3 — How a run starts (each time, with the developer's go-ahead)

Claude does steps 1–3 on the developer's machine. **Nothing is deleted until the next launch, which the
developer does.**
1. **Archive** the current world: copy `%APPDATA%/Godot/app_userdata/GamblingMiner` to
   `GamblingMiner_archive_<run>_<date>`, the existing practice.
2. **Arm the wipe:** write `0` into `world_format_version.txt`. On the next launch `WorldGuardService` sees a
   format mismatch and performs the standard clean reset. That wipes world state and resets hardware to
   its defaults. It **keeps** the exempt set: wallet seeds, saved strategies, notes, logs.
3. **Separate the trace:** rename `logs/rank_calibration_trace.csv` to `rank_calibration_<previous run>.csv`.
4. The developer launches, from genesis.

### 9.4 — Run R1 (proposed): five bet sizes in one world

**Five bet sizes, one per casino player, bracketing the 0.77 SC break-even.** Every one is flat (0% on
loss and on win) at 50% chance, so the average bet equals the base bet and the run measures the economy
rather than a strategy:

| Client | Base bet | Why this size |
|---|---|---|
| `bot_1` | 0.01 SC | a near-minimum player: the ladder's floor |
| `bot_2` | 0.1 SC | a cautious player |
| `bot_3` | 0.5 SC | just under break-even: never ruins within the span |
| `player` | 1 SC | just over break-even: ruins in ~12.9 yr without income |
| `bot_4` | 2 SC | an aggressive player: ruins in ~6.5 yr without income |

**Settings:**
- **credits:** the post-wipe defaults, left alone. That is the real starting state, and the trace records
  it.
- **DEV time scale 9000X.**
- **Auto Recharge ON** for everyone (bots have it forced on).
- **Stop on block mined OFF** (forced off for bots).
- **Length:** **at least to Market Birth (2010-07-18)**, about 1.33 game-years. At full retention that is
  roughly 80 real minutes. It may be spread across sessions, and longer is better.

**M0 (§5.5) runs in R1's first minutes.**

**Predictions, registered before the run:**

| | Prediction | Refuted if |
|---|---|---|
| **R1-P1** | Each client's bets per game-year = **315,576 × its credits**: the 100-second rule, per client | any client off by **> 1%** over a full game-year |
| **R1-P2** | Each client's net ≈ **−0.0098 × its wagered**, within ±3σ (σ ≈ base × √bets) | any client outside ±3σ |
| **R1-P3** | The player's rollup wagered **equals** the player's client-book wagered, to the satoshi, at every row | any difference |
| **R1-P4** | No client ruins before Market Birth: the largest bet, 2 SC, ruins in ~6.5 yr | any client's runner disappearing before 2010-07-18 |
| **Measured** | BTC mined per client per game-year, by credits. **No prediction**: it depends on network competition, which nothing above derives | — |

**What R1 answers.** Whether the deterministic part (§9.1) holds in the game. And the first measurement of
the part that decides the top of the ladder: **BTC per credit per year**, priced in SC at the historical
rate.

### 9.5 — Run ledger

| Run | World archived as | Parameters | Game span covered | Result |
|---|---|---|---|---|
| R1 | before: `GamblingMiner_archive_preR1_2026-10-14` (the 2012 world). After: `GamblingMiner_archive_R1_end_2026-10-18` (h2,372) | §9.4 | **2009-03-22 → 2013-05-12, ~4.1 game-years**, chain 117 → 2,372, three sessions | **Closed**, §9.8 + §9.11. Traces: `logs/rank_calibration_R1_part1.csv`, `_part2.csv` |
| R2 | — (the current world) | §9.12 | **2009-03-21 → 2012-01-31, 2.86 game-years**, chain 116 → 1,639, one session | **Done 2026-10-18**, §9.13. Trace: `logs/rank_calibration_trace.csv` (rename to `rank_calibration_R2.csv` at R3's setup) |

### 9.8 — R1 results (2026-10-15)

Read from `rank_calibration_R1_part1.csv` (1,573 block rows) and the chain itself.

| | Verdict | Measured |
|---|---|---|
| **R1-P1** | ✅ **CONFIRMED, exactly** | **315,576 bets per game-year for all five clients, deviation 0.000%.** All five end at 916,081 bets. The 100-second rule holds per client |
| **R1-P2** | ✅ CONFIRMED | Every net within 1.13σ of −0.98% of wagered: player z = 0.92, `bot_1` −0.50, `bot_2` −0.14, `bot_3` 1.13, `bot_4` 0.46 |
| **R1-P3** | ❌ **REFUTED — by its own premise, not by the code** | The client book holds **0** for the player in every row. That is by design (ND.8f): the book records `bot_1..4` only, and the player lives in `UserStatsService`. The trace's claim that the two "must agree" was wrong. **Fixed:** the trace now reads each client from its own source, and the stale columns are gone |
| **R1-P4** | ✅ CONFIRMED | Nobody was ruined: all five bet counts stay equal to the last row. `bot_4` (2 SC) lost 17,078 SC in 2.9 years, about 42% of its capital, on track for the predicted ~6.5-year ruin |

**Wagered after 2.9 game-years, each exactly bets × base bet:**

| Client | Base bet | Wagered (SC) | Net (SC) |
|---|---|---|---|
| `bot_1` | 0.01 | 9,160.81 | −94.50 |
| `bot_2` | 0.1 | 91,608.10 | −910.93 |
| `bot_3` | 0.5 | 458,040.50 | −3,952.60 |
| `player` | 1 | 916,081.00 | −8,105.23 |
| `bot_4` | 2 | 1,832,162.00 | −17,077.87 |

**So the flat-bet part of §9.1 needs no further runs:** a run cannot teach anything that bets × base bet
does not already say.

**Mining, the part only a run can measure.** From the chain (blocks 117–1,690, 1,574 blocks, 542/game-year):
- each casino client at 1 credit mined **98–126 blocks**: player 102, `bot_1` 105, `bot_2` 121, `bot_3` 98,
  `bot_4` 126. That is about **35–43 blocks, or 1,750–2,170 BTC, per credit per game-year in 2009–2012**;
- the rest went to the historical cast (artforz 123, Satoshi 109, laszlo 90, …) and the scheduled mass.

The player kept all **5,100 BTC**. The bots spent about 90% of theirs bidding in the referral auctions and
hold ~510–558 each. **18 companies were founded**, which lifts `CompanyDetails`'s suspension (§0.1 fact 1):
this world can verify it.

**What 5,100 BTC is worth in SC** (historical price, before the 10% swap fee):

| Date | Value |
|---|---|
| 2012-02-14 | ≈ 24k SC |
| 2013-12-04 | ≈ 6.1M SC |
| 2017-12-17 | ≈ 97.7M SC |
| 2021-11-08 | ≈ 335M SC |
| 2025-12-31 | ≈ 450M SC |

Wagered at 0.98% expected loss, the 2021 figure alone is a capacity of **~3 × 10¹⁰ SC**. *An extrapolation,
not a measurement:* the network's competition grows sharply after 2012, and the halving at 2,100 blocks
falls inside the next game-year, so BTC per credit-year will fall. Measuring that fall is the next thing a
run must do.

**The range the ladder has to span, as of R1:**
- **floor:** a minimum-bet player. At 0.01 SC that is ~53k SC lifetime per credit;
- **no-income cap:** ~4.08M SC;
- **top:** a miner who sells and re-wagers, up to ~10¹⁰ SC.

That is roughly **six to seven decades**, and it is where the 20–33 levels must fit. Two round-number
families fit it, written down as candidates for the design plan, **not chosen**:
- **1-2-5 per decade** (3 levels a decade): about 19–22 levels over 10⁴ → 10¹⁰/10¹¹;
- **1-2-3-5 per decade** (4 a decade): 25 levels over 10⁴ → 10¹⁰, or 29 levels from 10³.

### 9.9 — Next runs (proposed 2026-10-15; the developer chooses)

R1 showed that **varying flat bet amounts teaches nothing new**, because the arithmetic is exact. What the
arithmetic cannot predict is:
- **(a)** mining income in later eras, where network competition grows and the halving falls;
- **(b)** progression strategies, whose average bet diverges in theory and is capped only by the bankroll
  and ruin;
- **(c)** more hardware credits.

| Option | What | Why |
|---|---|---|
| **R1-continued** (recommended first) | Keep R1's world and parameters and **continue it while 17.C's migrations are verified**, with no wipe. The trace carries on in a new file | (a) for free: it reaches the 2012–2014 era where competition grows and the first halving lands. It is also the world 17.C.6 needs, now that 18 companies exist. M1 can be the same world, as §5.4 asks |
| **R2** (from genesis) | Progressions, every client at 1 credit. `player` flat 0.1 SC (the control), and the bots at base 0.01 with **+100% / +50% / +25% on loss**, reset on win, plus one with a stop-loss and insist | (b): the average-bet multiplier and ruin under progressions, the case a martingale player brings to the ladder |
| **R3** (from genesis) | Credits: `player` at 10 credits through the DEV credits selector, everyone else as R1 | (c): mining share and bets per year at 10×, which P5's hardware progression will produce |

**Chosen 2026-10-15: R1-continued.** R1's world carries on, with no wipe, and R2/R3 wait until this plan's
migrations are verified.

### 9.10 — R1-continued: how a session resumes

The trace's header changed with R1-P3's fix, so continuation rows go to a fresh
`logs/rank_calibration_trace.csv`, and part 1 stays in `rank_calibration_R1_part1.csv`. The two join on game
date and chain length.

1. Launch from the Godot editor. **No wipe is expected:** the Output panel must NOT show `World reset
   triggered`. If it does, stop and say so.
2. DiceGame → load **`R1 0.01` on `bot_1`**, **`R1 0.1` on `bot_2`**, **`R1 0.5` on `bot_3`**, **`R1 2` on
   `bot_4`**, then **`R1 1` on `player`**, Auto Recharge ON.
3. DEV time scale **9000X**. Leave "↻ UI refresh" OFF unless a verification protocol asks for it.
4. Start the autobet with `player` selected. Any verification protocol from §5 runs inside the session.
5. End as before: stop the autobet, close the game, and say so in the chat.

**One caveat to read the data by:** the 17.C migrations change only how panels refresh, never the
simulation, so R1's parameters stay the same across its parts. Anything that does change the sim (item 2
above, P5, P7) waits until R1 is closed.

### 9.11 — R1 part 2 results (2012-02-14 → 2013-04-10, 1.15 game-years, chain 1,691 → 2,327)

Read from `logs/rank_calibration_trace.csv` (492 rows) and the chain.

- **The bet arithmetic holds exactly again** for every client that kept betting: the player, `bot_2` and `bot_3`,
  each at 315,576 bets/yr, deviation 0.000%. Nets are within 1.02σ.
- **`bot_1` and `bot_4` stopped betting mid-2012**, at 91,169 and 123,740 bets/yr. That is INC-006's amplifier: a
  governance exception killed whichever bot runner had mined the block. Their part-2 figures are truncated and
  are excluded from any rate.
- **The first halving landed at block 2,101 on 2012-11-16** (the real one was 2012-11-28), and the coinbase
  fell to 25 BTC.
- **Mining at 1 credit collapsed after 2012.** In the 637 blocks of part 2 the five casino clients mined **5**
  (player 0, `bot_1` 1, `bot_3` 1, `bot_4` 3). In 2009–2012 it was 35–43 per credit per game-year; now it is
  **about 1**. The historical network outgrew a 1-credit miner, as it outgrew real CPU miners.
- **Bots' BTC rose anyway** (`bot_1` 528 → 2,243, `bot_2` 510 → 1,813) **with almost no mining**: most likely
  dividends from their shareholdings, plus stale-bid refunds. *Inferred, not decomposed.* The player has none of
  it, never having bid.

**What it means for the Rank ladder.** **The rank counts ONLY the SC wagered in the casino's games** (DiceGame
now, other games post-Basic Mode). **Income from activity outside the casino never counts toward it** —
mining, dividends, refunds, swaps (developer, 2026-10-16, correcting this section's first draft, which called
dividends "a second channel the design plan must count"). Outside income matters to these runs for one reason
only: it is SC a player can go on to wager, so it decides how far up the ladder their *wagering* can reach.
With that reading:
- **R3 (more credits) is no longer optional.** It is the run that says whether a player can still mine, and so
  keep wagering big, after 2012;
- **the early-mining windfall is the realistic source** of a 10⁹–10¹⁰ wagering capacity.

### 9.12 — Run R2 (proposed 2026-10-18): progressions, from genesis, on the v12 world

**What it measures:** the average-bet multiplier `m = wagered ÷ (bets × base)` under progressions, which is the
part of a player's wagered that §9.1's arithmetic cannot give. **Every client uses the same base, 0.01 SC, at 50%
chance and 1 credit**, so the five `m`s compare directly. A win resets to base: `IncreaseOnWinPercent = 0` returns
`BaseBet` (`ProgressiveBettingStrategy`).

| Client | Strategy (saved as) | Predicted `m` | Why |
|---|---|---|---|
| `bot_4` | flat (`R2 flat`) | **1.000** exactly | the control |
| `bot_2` | +25% on loss (`R2 L25`) | **1.333** (= 0.5 · Σ 0.625ʲ) | convergent: the bankroll never binds |
| `bot_1` | +50% on loss (`R2 L50`) | **2.000** (= 0.5 · Σ 0.75ʲ) | convergent |
| `player` | +100% on loss (`R2 L100`) | **5–8**, a band | martingale: the series diverges, so `m` is set by how far the bankroll lets it climb, ≈ levels ÷ 2 |
| `bot_3` | +100% on loss, Stop on loss 5 SC, insist (`R2 L100 SL5`) | **below the player's** | the stop resets the ladder early |

The martingale sits on the PLAYER deliberately: only the player's bets are journalled one by one, so its
streaks and recharges can be reconstructed exactly.

**Predictions:**
- **R2-P1:** 315,576 bets per game-year for every client, whatever its strategy.
- **R2-P2:** `bot_2` and `bot_1` within **±3%** of 1.333 and 2.000; `bot_4` exact; the player inside 5–8;
  `bot_3` below the player.
- **R2-P3:** no client is ruined. At `m` = 8 the martingale loses ~250 SC a game-year.
- **R2-P4 (INC-006 verified):**
  - every founding in `state.json` has **no duplicate `HolderId`**;
  - `godot.log` holds **no `[Governance] EXCEPTION`** and **no `[BotAutoBetError]`**;
  - the rank trace has **no missing `chainLength`**, i.e. no aborted block. That is the boring check, and it
    needs no profiler.

**Refuted if** any of these misses. **Length:** to at least **2011-06-30**, about 2.3 game-years, so that several
companies are founded and their first votes close.

### 9.13 — R2 results (2026-10-18): every prediction confirmed

One session on the v12 world, **2009-03-21 → 2012-01-31, 2.86 game-years**, chain 116 → 1,639 (1,524 trace rows).

| | Verdict | Measured |
|---|---|---|
| **R2-P1** | ✅ exact | 315,576 bets/yr for all five clients, deviation 0.000%. 903,921 bets each |
| **R2-P2** | ✅ | `bot_4` flat **m = 1.0000** · `bot_2` +25% **1.3348** (pred 1.333, +0.1%) · `bot_1` +50% **1.9988** (pred 2.000, −0.06%) · player martingale **7.6014** (band 5–8) · `bot_3` martingale + SL5 **6.1617**, below the player |
| **R2-P3** | ✅ | No client was ruined. The player holds 38,606 SC: 40,000 − 1,393.74 |
| **R2-P4** | ✅ **INC-006 verified** | 18 foundings, **0 duplicate `HolderId`** · `godot.log`: 0 `[Governance] EXCEPTION`, 0 `[BotAutoBetError]`, 0 exceptions of any kind · rank trace: **0 gaps** in `chainLength`, so no block was aborted |

**A false alarm, recorded because it was raised.** The progressions lost more than the 0.98% edge (player
−2.03%, `bot_3` −2.55% of wagered). A first noise estimate called `bot_3`'s gap z ≈ −5.7, but it assumed the
stop-loss caps a streak at 9 levels. The code measures loss from the segment's last re-anchor (`BaseBetSession.
ApplyStopConditions`), so a segment in profit lets a streak go deeper: the estimate was a floor. **Measured
exactly instead, on the player's journal** (the last 204,654 bets): net/W −7.08%, exact σ = √(Σbet² × 0.9804) =
826.72 SC, **z = −1.23**. The ladder reached **level 15** (a 327.68 SC bet), each level half as frequent as the one
below it. **A progression's realised loss rate swings widely; its expectation does not** (−edge × wagered). For
the Rank this changes nothing, since only wagered counts.

**What R2 adds to the ladder.** A strategy multiplies wagered by a predictable `m`: 1.33 and 2.00 exactly for the
convergent progressions, about 6–8 for a bankroll-limited martingale. So a player's lifetime wagered is
`5,296,320 × credits × base × m`, capped by `(capital + income) ÷ edge` (§9.1). Every factor except income is now
measured or exact.

**Still open for the ladder: R3, more credits.** It is the run that says whether mining, and therefore the money
that funds big wagering, survives past 2012 (§9.11).

**R1 is CLOSED (2026-10-16), recommended:** 4.05 game-years in total, past the first halving, and the
mining question it could answer is answered. Its world is not reused for R2 (INC-006's fix needs a wipe,
§10).

---

## 10. INC-006 — bots keyed by address in the auction (found 2026-10-16)

Full entry: `Documentation/INCIDENT_LOG.md` **INC-006**. In short:
- `ComputeAuctionLedger` records a bot's donor as the address that **signed** the bid. Since P16.2 that rotates
  through change addresses.
- Founding groups slots per address, so one bot mints several holdings (**21 of 22** companies), with
  NST-vs-PST decided per address group instead of per identity.
- In Casascius one bot held two NST entries. `CloseCompanyVote`'s `ToDictionary` threw mid-`HandleMinedBlock`,
  and `ExecuteBotBet`'s catch-all stopped the runner of the bot that had mined the block. That happened twice.

**Done in this unit:**
1. `CloseCompanyVote` groups by holder and sums NST, so the crash cannot recur on any persisted world.
2. The catch-all now also prints `[BotAutoBetError] <node> STOPPED for this session` with `GD.Print`, so it
   reaches the Output panel.

**D-17.6 — the root fix (the developer's call).** Record a bot's donor under its **base address**, exactly as
the player's has been since 2026-07-14. One founding holding per identity follows, and every donor comparison
in the bid rules becomes per identity.
- **It changes the simulation**, so it carries a **`WorldFormatVersion` bump 11 → 12 and a wipe**. That is
  Standing Convention 4 and the developer's standing "no migrations" rule: the 22 persisted foundings were
  minted wrong, and merging their duplicate rows would not reproduce the slot-bonus arithmetic, which is not
  linear.
- **Recommendation: do it now, as the next code unit, before R2.** R1 is closed and its data is in the trace
  files, which survive the wipe. R2 from genesis is the next run anyway. Running R2 on the old rule would bias
  the bots' BTC, the capital they would otherwise go on to wager.
- **DECIDED 2026-10-16: yes, as recommended.** **Sequencing:** the code is written only AFTER the developer's §10.1
  freeze session on R1's world. Godot builds from the working tree, so a fix sitting there would make the measured
  world run code it never ran before, and confound the measurement.
- **BUILT 2026-10-18 (unit 6), after that session.**
  - `ComputeAuctionLedger` records a bot's donor under its node's base address. The false comment ("Single-address
    participants need no equivalent") is replaced by its history.
  - **Every reader of `DonorAddress` was checked** (Convention 13). The bot-side ones already test
    `OwnedAddressSet(bot).Contains(…)`, which includes the base address, so they stay correct. Founding's
    `perDonor`, the explorer's per-donor slot count and the leader's self-raise rule become per identity.
  - `WorldFormatVersion` **11 → 12**: the next launch wipes.
  - R1's world was archived first, byte-identical, as `GamblingMiner_archive_R1_end_2026-10-18`. Its traces are
    `rank_calibration_R1_part{1,2}.csv`.

### 10.1 — The freezes (developer, 2026-10-16): "slight freezes of up to one second in DiceGame each time a block was mined"

**Not diagnosed, and no fix guessed** (Pattern 6, the closing rule). The instrument exists: **"⏱ Block cost"**
(`BlockCostProfiler`) prints one line per block with its phases, to the Output panel and to
`logs/block_cost_trace.csv`. An older trace (2026-10-07, h1,606, a world with no companies) already shows a
**436 ms block, 426 of them in `botTransactions`**.

**Hypotheses, registered before the run:**
- **H1 `botTransactions`**, the sell-flow and bot sends. It was already the 426 ms phase.
- **H2 `auctions`**, the ledger. It walks the whole chain and is computed several times per block; mini-plan 25
  §4.5/§4.7 added per-company work to it.
- **H3 `governance`**, 22 companies.

**Decision rule:** the phase holding **≥ 50% of the `totalMs` of the blocks over 200 ms** is the subject. The
criterion is stated as a **count**, the number of blocks over 200 ms, so a fix can be judged within one session
(instrument rule 3). **Measured on R1's world before the wipe**, because that is where the freezes were seen.

#### §10.1 results (session of 2026-10-18, R1's world, h2,328 → 2,372, 45 blocks, "⏱ Block cost" armed)

**Two populations, and the instrument had to be read before the code.**

| | Completed blocks (31) | Aborted blocks (14) |
|---|---|---|
| What the row shows | every phase non-zero, checkpoint ≈ 17 ms | **every phase after governance exactly 0.000, checkpoint 0** |
| A rank-trace row (written inside `CaptureCheckpoint`) | yes | **none**: the block never reached its checkpoint |
| Mean `totalMs` | **390 ms** | 6,060 ms, *not a cost* (below) |

- **The freeze on every block is H1, confirmed by the registered rule.** In completed blocks, `botTransactions` is
  **75%** of the total: 9,120 of 12,098 ms. Its cast **sell-flow** alone is **281 ms per block**, and **30 of 31
  blocks exceed 200 ms**. That is the stall the developer felt each time a block was mined.
- **The "governance 3–11 s" rows are not a measurement.** A row is closed by the checkpoint capture, and a phase
  stays open until the next `Enter`. In an aborted block nothing after governance ran, so its open governance
  phase absorbed the real time until the next block's `BeginBlock` closed the row: instrument rule 2, a residual is
  not the work inside it. *The first reading of these rows called them a governance cost; it was retracted once
  the zeros were read, and it was the rank trace that settled it.*
- **14 blocks aborted in governance, 2 logged.** The two are the second INC-006 site, `ComputeReserveVoteOutcome`'s
  `ToDictionary`, which unit 5's grep missed because it matched `Holdings` and `ToDictionary` on **one line** only.
  The other 12 left nothing in `godot.log`, and the data cannot say what stopped them. The likeliest cause is the
  same duplicate-NST exception on the cast-miner path, which has no catch of its own, **but that is not shown**.
  So both blind spots are now made loud (below), and R2 will say.

**Built in unit 6:**
- the second `ToDictionary` is grouped and summed;
- every governance exception is logged at its source with `GD.Print` + `PushError` and **rethrown**, so behaviour is
  unchanged;
- `BlockCostProfiler` prints `NEVER REACHED ITS CHECKPOINT` whenever it has to close an abandoned row.

**Not built, and the next performance question:** the 281 ms sell-flow. It is a real per-block cost on every
block, and the subject of its own unit with its own measurement first (`PRIVATE_ROADMAP.md` §8 T4).

### 9.6 — Answered by the developer (2026-10-14)

- **The game's span is Basic Mode's: 2009-03-21 → 2025-12-31.** §9.1's lifetime figures stand.
- **Who holds a rank: the player AND the four bots**, i.e. every casino player. The bots are therefore not
  only calibration samples but rank holders, and the design plan must show their rank somewhere.

### 9.7 — R1 session protocol (M0 included)

**Two code facts the protocol depends on.** Bots bet only once each has a strategy **stored in DiceGame**
(`BuildBotConfigs` skips a bot without one), and they start together with the player's autobet. Those stored
strategies live in a static that does **not** survive a launch. So every session re-assigns them, and
**saved strategies**, which do survive a wipe, make that a five-click step. A wiped world has no hardware
entries; every node bets at **1 credit** (the clamp in `HardwareRate`), and the trace records that
effective figure.

1. Launch from the Godot editor. *Godot editor's Output panel:* `[NetworkRoot] World reset triggered
   (format 0 → 11 …)`, then the historical bootstrap. Don't touch the DEV credits selector anywhere: R1
   runs at the post-wipe defaults.
2. Main Menu → **DiceGame**. **Layout check:** "↻ UI refresh" sits on the same line as "⏱ Block cost",
   and nothing in that DEV block overlaps the chance slider.
3. **Once only (they survive future wipes):** save five strategies. Each has *Increase on loss 0%*,
   *Increase on win 0%*, no profit/loss stop, *Stop on block mined OFF*, chance **50**. Name them by base
   bet: **`R1 0.01`**, **`R1 0.1`**, **`R1 0.5`**, **`R1 1`**, **`R1 2`**.
4. **Each session:** with the active-node selector, load **`R1 0.01` on `bot_1`**, **`R1 0.1` on
   `bot_2`**, **`R1 0.5` on `bot_3`**, **`R1 2` on `bot_4`**, then select **`player`** and load **`R1 1`**,
   with **Auto Recharge ON**.
5. Set the DEV time scale to **9000X** and tick **"↻ UI refresh"**. *Output panel:* `[UiRefresh] ARMED`.
6. **M0 leg A** (no autobet yet): §5.5 step 3, i.e. DiceGame 10 s, ScTransactions 15 s, BotsBtcWallets
   15 s.
7. Back to DiceGame with **`player`** selected → **start the autobet**. The four bots start with it.
8. **M0 leg B:** §5.5 steps 4–5, the scene tour.
9. Untick "↻ UI refresh" (M0 is done, and it only adds lines from here) and **let R1 run to at least
   2010-07-18** in any scene. A session can end at any time: the trace is written at every block, and a
   restart reverts only to the last block.
10. To end a session: stop the autobet in DiceGame, close the game, and say so in the chat. To continue
    R1 later, launch and repeat steps 4, 5 (9000X only) and 7. **The world is not wiped again**, because the
    wipe rewrote the stamp to 11.
