# Step 15 — Bank Companies as the SC-Provisioning Backbone

> **Status: DESIGN LOCKED (Rounds 0–3 complete) → ready to define P15.0 subphases.** OQ-15.1…19 and
> the three implementation picks are all decided (`D-15.1…22` in §4). The design has a two-layer
> banking system (Central Bank / FED + four bank companies), the `CollateralBtc` carry model,
> greed-driven governance voting, locked bank categories, seized wallets inherited by category-matching
> banks (FED custodies un-inherited ones 100% as BTC), a Closed-Companies list, and an in-scope FBI
> seizure thread (activates 14 Jun 2011; non-banks first, banks last). **No open questions remain.**
> The one soft item is the FBI tolerance numbers, deliberately left as P15.8 calibration placeholders.
> **ALL phases are now broken into subphases in §8 (P15.1a … P15.8)**; P15.1 is fully locked (Fork A,
> D-15.23) and **✅ IMPLEMENTED (2026-07-26, all five subphases P15.1a–e)**; **P15.2 is likewise ✅
> IMPLEMENTED (2026-07-26, subphases a–d)**, as is **P15.3 (a–b, which also absorbed P15.4a's quarantine)**
> and **P15.4 (b–e)**. Later phases inherit the same reviewed data structures and will firm up as each
> lands. **Next: P15.5a** on branch `bank-companies-sc-provisioning` — the `UnrecoverableShortfallSc` flag
> P15.4e sets is already waiting as its dissolution trigger. In-game verification is deferred to P15.8 by
> the developer's call — every phase since P15.1 has been build-verified only.
>
> Branch (suggested): `bank-companies-sc-provisioning` off `main` (canonical timeline, `DevEntryYear = 0`).

---

## 1. The reform goal

Replace the **provisional casino path** for company BTC→SC conversions with a **two-layer banking
system**:

- **Layer 0 — the Central Bank (FED):** the (currently abstract) non-auctionable house bank made into
  an explicit in-world entity with its **own scene and persistence**. It is the SC lender the casino
  already borrows from; now the four **bank companies** borrow from it too. It holds the debt ledger
  for all its clients (casino + banks).
- **Layer 1 — the four CB1 bank companies:** each becomes an SC dealer "very similar to the casino" —
  it can draw auto-loans from the FED and, in turn, provision SC to the BTC-receiving companies,
  keeping per-client accounts. It funds each provision by borrowing SC from the FED, holds the BTC it
  buys in a quarantined **`CollateralBtc`** account, and repays the FED **extra-lazy** — selling
  *just enough* `CollateralBtc` **on the quarterly payment day** to cover that quarter's installment.
  **No interest** (D-ND8.24).

This is the concrete build-out of step14's **§12.4.6c** ("the bank companies provision SC"),
deliberately scoped **below §12.4.6e / ND.8e** (D-15.1): plan15 builds the FED *entity* and the bank
credit loop, but **not** the fed-funds policy replay or the credit-capacity limits — those sit
*above* the banks in a later step. plan15 is the specific, bank-focused successor the developer wanted
to carve out of step14's over-generalized §12.4.6.

**The core economic loop this installs (the reason it's interesting):** a bank borrows SC from the FED
(no interest) to buy BTC from a company that needs SC, then sits **long BTC** until it must repay.
Across most of Bitcoin's history BTC rises, so the carry is profitable and funds the bank's dividends;
in a crash the bank goes **under-collateralized**, forcing a shareholder vote (dividends-cut vs
reserves-cut, D-15.7) and, at the limit, **bankruptcy → dissolution → federal seizure** (D-15.8).

---

## 2. How it works TODAY (the mechanism being reformed)

Grounded in the current `main` code (post-step14 merge):

- **`NetworkRoot.TryConvertCompanyReserves(gov, block)`** (~L2047) — once per new block, per founded
  company, inside `TickCompanyGovernance`. If `ScReserve` is below the band target
  (`gov.ReserveScPercent`) by more than `ConversionDeficitTriggerFraction`, it prices the deficit at
  the day's clean market rate (no swap fee), computes `btcToSell`, calls
  **`CasinoScBalanceService.TryPayCompanyProvisionSc`** (the **casino** is the SC counterparty for
  *every* company), broadcasts `company → casino` BTC (memo `"CONVERSION"`), and credits `ScReserve`.
- **`CasinoScBalanceService.TryPayCompanyProvisionSc`** (~L320) — pays from casino `MainBalance`; if
  short, auto-draws `AutoLoanAmount` chunks (each via `AddLoanRecord` ⇒ SC Monetary Ledger records
  `"casino"` debt). Casino loan state today: `LoanCount` / `TotalLoaned` / `LoanHistory` persisted in
  `casino_sc_balance_state.json`. (`RechargeHistory` is bankroll-dose data, unrelated to FED debt —
  it stays with the casino; only the loan data migrates to the FED.)

**Why it's provisional (D-ND8.24 / D-ND8.34):** the casino was a stopgap "so conversions work before
the bank companies exist." Today every company converts through the *same* casino balance sheet, so
the roster's banks are economically inert and all SC demand collapses onto one never-repaid loan
counter.

---

## 3. The plan15 architecture (from the Round-1 decisions)

### 3.1 Layer 0 — the Central Bank (FED)

- A new explicit entity, **never auctionable**, replacing the abstract "bank" the casino borrows from.
  Gets **its own scene** (Main Menu → Central Bank) and **its own persistence** (`user://central_bank_state.json`).
- **Single source of truth for all FED-level debt.** Its clients are the **casino** and the **four
  bank companies**. Each client has an account record: outstanding SC debt, loan history, (banks also)
  collateral posted. The casino **stops storing its own `LoanCount`/`TotalLoaned`/`LoanHistory`** and
  instead reads them from the FED registry (D-15.3, D-15.5) — no duplicated banking data.
- **The casino keeps borrowing exactly as today**, just from a now-visible entity: `AddLoanRecord`
  becomes a FED-recorded draw against the casino's FED account. This is a **code re-architecture, not a
  data migration** — because plan15 bumps `WorldFormatVersion` (D-15.10), old worlds clean-reset and
  rebuild under the new FED-centric persistence, so there is no legacy-data conversion to write.
- **Mint/burn:** every FED loan draw mints SC (bank-attributed debt in the SC Monetary Ledger, per
  D-15.5 borrower keys `bank:<id>` / `casino`); every repayment **burns** SC via the ND.8c
  `RegisterBurn` hook (its first real caller). The invariant `circulation = grants + debt` must hold
  every block.
- **Deferred to ND.8e (NOT plan15):** the fed-funds-rate historical replay and the credit-capacity
  *limits* on how much the FED will lend a given client. In plan15 the FED lends without limit
  (auto-loan), exactly as the casino does today — period-accurate for the ZIRP 2009–2015 window.

### 3.2 Layer 1 — the four bank companies

- The four existing CB1 roster banks (Appendix A) gain behavior; **no new companies** (D-15.6). Each is
  reassigned a **market category** so the four span the **Official → Light-Grey → Dark-Grey → Black**
  gradient (D-15.6; the category-matching key for selection, §5.1). This is the only roster-data change
  (the `market` column of three rows); the currency band stays CB1 for all four.
- Each bank behaves like a mini-casino: it can **draw auto-loans from the FED** and **provision SC to
  companies**, and it keeps a **per-client ledger** (which company, how much BTC bought, how much SC
  paid) — the `CasinoClientLedgerService` analog, one level down (D-15.5, the two-layer accounting).
- **Two BTC streams, kept separate (D-15.4):**
  1. **Own business inflows** (the bank as a normal CB1 company) → auto-converted to SC immediately
     (the CB1 100%-SC behavior, unchanged).
  2. **`CollateralBtc`** — BTC bought from the companies it finances → **quarantined** from the CB1
     auto-convert, held on the bank's book, and sold **extra-lazy** to service the FED debt.
- **Provisioning a company (the reroute of `TryConvertCompanyReserves`):** when a company needs SC, the
  BTC leg goes `company → chosen bank` (not `→ casino`); the SC leg is paid by the bank, funded by a
  FED auto-loan; the bought BTC lands in the bank's `CollateralBtc`; the bank now owes the FED that SC.
  Company-selection rule = §5.1 (needs your pick).
- **Extra-lazy repayment (D-15.4 — Hybrid, full-quarter fraction, converting BTC on the day):** on each
  quarter-end (reusing the existing dividend/vote cadence — no new scheduler) the bank owes a fixed
  fraction of its FED principal. It sells **just enough** `CollateralBtc` at that day's clean rate to
  raise exactly that installment, repays the FED, and `RegisterBurn`s the SC. Nothing is sold between
  payment days — that is the "lazy," and it is what leaves the bank long BTC in the interim.

### 3.3 Under-collateralization → the shortfall vote (D-15.7)

If, on a payment day, selling **all** available `CollateralBtc` still can't raise the installment
(BTC fell since purchase), the bank faces a shortfall. The SC amount needed to close it is surfaced in
the **Board Vote panel**, and NST holders vote the split between two sources:

| Source | What it drains |
|---|---|
| **Dividends Cut** | shareholders forgo that slice of this quarter's dividend |
| **Company Reserves Cut** | the slice comes out of the company's own `ScReserve` |

**Default split (no/tied vote): 50% / 50%.** A **new bot greed attribute** (per-world draw, 4 states,
alongside the existing D-ND8.13/26 governance preferences) biases the ballot:

| Greed | Dividends Cut | Reserves Cut |
|---|---|---|
| not-so-greedy | 90% | 10% |
| almost-greedy | 70% | 30% |
| greedy | 30% | 70% |
| extremely-greedy | 10% | 90% |

The **same greed attribute biases every "dividends vs. company money" vote** (D-15.13): the existing
quarterly payout-rate vote (greedier ⇒ higher payout up to the `[0, 2×]` clamp, not-greedy ⇒
retention) and this shortfall split. It does **not** touch the reserve-band (currency-mix) vote — a
different axis. (Seized-wallet inflows, §3.4, are handled by the holding bank's *normal* votes, which
are already greed-biased here — no separate vote.)

The shortfall split is a **new `CompanyVote.Kind`** (D-15.15), opened only when a bank can't cover its
FED installment. For a player NST holder it is a real Board-Vote ballot that **pauses the game**
(D-ND8.18). When the player holds no NST in that bank, the bots resolve it silently — the player is
**notified only at the terminal moment**: when neither a full dividends cut nor the company reserves
can close the gap and the bank is about to be **closed** (§3.4). PST holders keep their earnings intact
until that last instant, then lose their tokens and the company's future payments on liquidation.

### 3.4 Dissolution → seized wallets → federal seizure (D-15.8, D-15.12, D-15.15, D-15.17)

**Dissolution applies to ALL companies, banks included (D-15.17)** — only the casino is exempt (it is
the player's house, keeps its unlimited FED auto-loan, never disappears). Two triggers:

- **Bank debt-default** (banks only): a bank can no longer cover its FED installment by any means —
  `CollateralBtc` exhausted **and** the shortfall vote (§3.3) can't close the gap. The bank is **closed**.
- **FBI seizure** (any company): the FBI investigates and seizes companies by market-category darkness
  and per-business-type tolerated SC balances — built in plan15 (D-15.14), the mechanic in **§5.2**.
  **Non-bank companies are investigated FIRST** (by how far their SC exceeds their category tolerance)
  and, carrying no debt in the spot-sale model, seizure is their **only** dissolution path (D-15.19);
  **banks are investigated LAST** (the FBI builds evidence before striking the "big fish"), so a bank
  can dissolve by debt-default *or* a late seizure.

On dissolution the company **disappears from the live world/UI** and moves to a **Closed-Companies
list** (D-15.15) — a sibling view to `CompanyDetails` recording the closure timestamp and reason
(*debt-default* vs *FBI seizure*).

- **Post-dissolution income redirection:** the dead company's address keeps receiving its scheduled
  automatic inflows **off-UI**; each is forwarded to whoever **absorbed** it. A debt-default bank is
  absorbed by the **FED** (its creditor); an FBI seizure's wallets are **seized by the FED** (D-15.15).
  Over time this plausibly recovers — or profits on — the assumed loss; a **DEV-only tracker** reports
  recovered / underwater / profit (§5.3).
- **Seized wallets (D-15.12, D-15.18):** banks' market categories are **locked** (banks are the only
  companies exempt from the ±1 category-shift vote — keeping the Official→Black gradient intact). In
  exchange banks gain a new feature — **holding seized BTC wallets**. The **FED assigns each seized
  wallet to a solvent bank of the matching market category** (O18-A: a Black bank inherits darknet
  seizures, etc.); that bank then processes the wallet's inflows through its **own band/level and
  normal governance votes** — **not** force-converted to SC, **not** a bespoke per-deposit vote. A
  seized wallet with **no matching solvent bank yet** (e.g. every 2011–2012 seizure before the first
  bank founds) **stays with the FED, held 100% as BTC** (custodial, no allocation) until a matching
  bank inherits it.

---

## 4. Decisions log — Round 1 (`D-15.x`)

- **D-15.1 (OQ-15.1):** plan15 is the **§12.4.6c layer only**. ND.8e's Central Bank *policy replay*
  (fed-funds rate → credit-capacity limits) stays a distinct later step *above* the banks. plan15 does
  build the FED *entity + scene + persistence* now, but with **unlimited (auto-loan) lending**, no rate.
- **D-15.2 (OQ-15.2):** each of the four banks represents one of the four market categories; a company
  prefers the bank nearest its own category with enough funds, else the bank that can fund the whole
  loan, else a split across banks (nearest-category first, then most-capacity). Split/tier-2/3 are
  **dormant until lending limits ship** (auto-loans make funds effectively unlimited now). **Exact
  algorithm & tie-breaks → §5.1, pending pick.**
- **D-15.3 (OQ-15.3):** new **Central Bank (FED) entity + scene + persistence**; it is the SC lender the
  casino already uses (unchanged behavior, now visible). Other banks borrow from the same FED. The
  casino's own auto-loan is **implemented against the FED now, not deferred**. No data migration
  needed (clean reset via D-15.10); the "re-design" is moving the casino's loan records into the FED
  registry so they aren't stored twice.
- **D-15.4 (OQ-15.4):** repayment is **Hybrid — full-quarter fraction**, selling *just enough*
  `CollateralBtc` **on the payment day**. Banks receive BTC like any company but (CB1) auto-convert
  their **own** inflows to SC on arrival; the **`CollateralBtc`** stream (BTC bought when financing
  other companies) is a **separate quarantined account** dedicated to repaying the FED.
- **D-15.5 (OQ-15.5):** **two-layer ledger**. Each bank gets its own SC Monetary Ledger borrower key
  (`bank:first_satoshi_savings`, …); the **casino becomes just another FED client**. Each bank keeps
  accounts for **its own** client companies; the FED keeps accounts for **its** clients (casino +
  banks). Store each layer's data where it belongs, efficiently (no duplication).
- **D-15.6 (OQ-15.6):** **purely add behavior to the four existing CB1 banks** + assign them the
  Official→Black market-category gradient. No new companies.
- **D-15.7 (OQ-15.7):** under-collateralized installments are covered by a **Board-Vote split**
  (Dividends Cut vs Company Reserves Cut, default 50/50), biased by a **new 4-state bot greed
  attribute** (not-so-greedy 90/10 · almost-greedy 70/30 · greedy 30/70 · extremely-greedy 10/90). The
  same greed also biases the **existing quarterly payout-rate vote**.
- **D-15.8 (OQ-15.8):** true bankruptcy **dissolves** the company; its scheduled inflows continue
  off-UI and are **redirected to the creditor** that absorbed the debt (a bank or the FED), plausibly
  recovering it over time (DEV-tracked). FED-absorbed dissolutions are framed as **federal seizures**,
  seeding the future **FBI** thread (§5.2). BTC-balance investigation is post-Basic-Mode.
- **D-15.9 (OQ-15.9):** a founded bank's lending/collateral/debt **surfaces in `CompanyDetails`** for a
  player NST holder. New DEV readouts (FED scene + WorldEconomy) → **proposals in §5.3, pending pick.**
- **D-15.10 (OQ-15.10):** this **bumps `WorldFormatVersion`** (clean reset) — world-defining semantics
  change, and it removes any need to migrate legacy casino-loan persistence.

### Round 2 additions

- **D-15.11 (OQ-15.11):** the company↔bank leg is a **clean spot BTC→SC sale** (the company permanently
  converts, as with the casino today; the only debt is bank↔FED). §12.4.6c's repayable-credit-line
  wording is **dropped**.
- **D-15.12 (OQ-15.12, amended R3):** **banks' market categories are LOCKED** — banks are the only
  companies exempt from the ±1 category-shift vote, preserving the Official→Black gradient. New bank
  feature: **holding seized BTC wallets** — their inflows are **NOT force-converted to SC**; the
  holding bank absorbs them and allocates via its **own band/level and normal governance votes** (no
  bespoke per-deposit vote). Assignment/custody → D-15.18.
- **D-15.13 (OQ-15.13):** **greed** is a **new persistent per-bot field**, drawn per world (like
  D-ND8.13/26), independent of the reserve/market preferences. It biases **all companies' payout-rate
  votes** (not just banks) plus the shortfall and seized-wallet votes. The player (NST holder) gets an
  **explicit ballot control** for these splits, and full account visibility for everything plan15 tracks.
- **D-15.14 (OQ-15.14):** the **FBI is built IN plan15**. Activation is gated behind the historically
  correct event — **Gavin Andresen's 14 Jun 2011 Bitcoin presentation to the CIA via In-Q-Tel** (he did
  *not* meet the FBI; the CIA is flavor-only, never mechanically involved). That date **starts the FBI**
  in-game. (Note: this precedes the first bank founding 2012-09, so 2011–2012 seizures have no bank to
  route to yet — they sit with the FED; see OQ-15.18.)
- **D-15.15 (OQ-15.15):** the shortfall split is a **new `CompanyVote.Kind`**, opened only when a bank
  can't cover a FED installment; it **pauses the game** for a player NST holder like other votes. With
  no player stake it is bot-resolved silently, and the player is **notified only at terminal closure**.
  PST holders keep earnings until liquidation, then lose tokens + future payments. A **Closed-Companies
  list** (sibling to `CompanyDetails`) records each closure's timestamp + reason (debt-default / FBI
  seizure; FBI ⇒ wallets seized by the FED).
- **D-15.16 (OQ-15.16):** the **FED scene** is a **Main Menu entry, DEV-only for now**; the casino's
  `CasinoGamblingFinances` DEV scene does **not** need to link to it yet.
- **D-15.17 (OQ-15.17):** **dissolution applies to all companies, banks included**; the **casino is the
  sole exception** (keeps its unlimited FED auto-loan — period-accurate ZIRP — until ND.8e adds limits).

### Round 3 additions

- **D-15.18 (OQ-15.18):** **O18-A** — the FED assigns each seized wallet to a solvent bank of the
  matching market category, whose normal band/level governance then handles it (D-15.12). A seized
  wallet with no matching solvent bank yet (every 2011–2012 pre-first-bank seizure, or any category
  without a solvent bank) **stays with the FED held 100% as BTC** (custodial, no allocation) until a
  matching bank inherits it.
- **D-15.19 (OQ-15.19):** non-bank companies carry no debt (spot-sale), so **FBI seizure is their only
  dissolution path**, and they are investigated **first** (ranked by how far their SC exceeds their
  category tolerance). **Banks are investigated LAST** (lowest roll priority — the FBI builds evidence
  before striking the "big fish"), so a bank can dissolve by debt-default *or* a late seizure; the
  casino by neither.

### Round 3 final picks (implementation) — developer deferred to recommendations

- **D-15.20 (§5.1):** bank selection = **A1 + B1 + casino fallback** — tie-break toward Official; full
  ordered-preference framework, capacity currently infinite (tiers 2/3 dormant until lending limits);
  casino fallback for any category with no founded bank (and all pre-2012-09).
- **D-15.21 (§5.2):** FBI = the **hybrid** (F1 investigation meter chooses targets + a capped F2 roll
  fires the raid); **throughput-relative tolerances** `tolerance = categoryMultiplier × recentScThroughput`
  (placeholders Official ∞ / Light-Grey 8× / Dark-Grey 3× / Black 1×, calibrated in P15.8).
- **D-15.22 (§5.3):** adopt **all three DEV/UI surfaces** — the DEV-only FED scene, the WorldEconomy
  additions, and the player-facing `CompanyDetails` lending panel (all in P15.7).
- **D-15.23 (§8 fork):** **Fork A** — a new `CentralBankService` owns the per-client FED accounts +
  histories; the `ScMonetaryLedgerService` keeps its macro role, its `_debtByBorrower` synced for free
  via the existing `RegisterLoanDraw`/`RegisterBurn` hooks. Fork B (retiring `_debtByBorrower` so the
  FED is the single debt store) is a **deferred optional cleanup**, not scheduled.

---

## 5. Implementation picks (all DECIDED — see D-15.20…22)

> The developer deferred these three to my recommendations (Round 3). Kept below as the rationale/spec;
> each is now marked **DECIDED** inline.

### 5.1 — Bank selection algorithm (OQ-15.2)

**Category axis (ordinal):** `Official(0) · Light-Grey(1) · Dark-Grey(2) · Black(3)`. Distance between
a company and a bank = `|catCompany − catBank|`. A company's category can already shift ±1 by vote
(§12.4.3), so selection is evaluated fresh at each conversion.

**Proposed bank↔category assignment** (roster `market` column change, easily swapped — it's a name/flavor
call, not mechanics):

| Bank | Founded | Proposed category | Flavor rationale |
|---|---|---|---|
| First Satoshi Savings | 2012-09 | **Official** | first, wholesome retail savings bank |
| Digital Reserve Trust | 2013-06 | **Light-Grey** | a lightly-regulated "reserve trust" |
| Ledger & Sons Private Bank | 2016-03 | **Dark-Grey** | secretive old-money private banking |
| Harbor Coin Bank | 2014-11 | **Black** | offshore "harbor" for illicit funds |

Note: a darker bank inherits §12.4.3's higher dividends **and** higher seizure risk — a shady bank that
pays well but can be busted. That's a feature; confirm you want banks exposed to the seizure roll too.

**The three decisions inside the algorithm:**

- **(a) Tie-break when two banks are equidistant** (e.g. a Dark-Grey company with no Dark-Grey bank
  founded yet sees Light-Grey and Black both at distance 1):
  - **Option A1 — toward Official** *(recommended)*: prefer the more legitimate bank on a tie. Simple,
    thematically "a business reaches for the cleaner bank first."
  - Option A2 — toward the bank with the most free capacity (matters only once limits exist).
  - Option A3 — random / weighted by `inflow_weight`.
- **(b) How much of the 3-tier framework to build now**, given auto-loans make tier-1 always succeed:
  - **Option B1 — build the full ordered-preference framework, capacity currently infinite**
    *(recommended)*: `SelectFinanciers(company, amountSc)` returns an ordered list; today it always
    resolves to a single nearest-category bank, but tiers 2/3 (single full-funder, then split) are
    real code paths that light up for free the day lending limits ship. Minimal extra work, forward-
    compatible, and it makes the eventual ND.8e limits a data change, not a rewrite.
  - Option B2 — implement only tier-1 (nearest founded bank) now; add tiers 2/3 with the limits step.
    Less code now, a bigger diff later.
- **(c) Pre-first-bank fallback:** before the first bank founds (2012-09), and for any category with no
  founded bank yet, **fall back to the casino path** (D-ND8.34) — *recommended*, it's the current
  behavior and keeps 2009–2012 working unchanged.

**DECIDED (D-15.20): A1 + B1 + (c).** Tie-break toward Official; build the full ordered-preference
framework with currently-infinite capacity (tiers 2/3 dormant until lending limits ship); fall back to
the casino path for any category with no founded bank yet (and everything before 2012-09). Honors the
3-tier preference exactly, costs little beyond tier-1 today, and turns the future credit-limit work into
pure calibration.

### 5.2 — The FBI investigation / seizure mechanic (OQ-15.8) — two proposals

Both are **Basic-Mode, SC-balance-based** (BTC-balance forensics deferred), **timeline-gated** behind
the fixed historical event (D-15.14) — **Gavin Andresen's 14 Jun 2011 In-Q-Tel/CIA presentation**,
which starts the FBI in-game — and both **self-fund the FBI** from what it seizes (plus an initial FED
grant at activation). Since this predates the first bank (2012-09), the earliest seizures route to the
FED alone (OQ-15.18).

**Per-business-type SC tolerance** (the shared input): each market category carries a **tolerated SC
balance ceiling** — how much SC a business of that legitimacy can sit on before it looks suspicious.
Darker ⇒ lower tolerance (a black-market stall holding a fortune in SC is a red flag; a licensed
exchange holding the same is normal).

| Category | SC tolerance | Reading |
|---|---|---|
| Official | very high / none | licensed, audited — no ceiling |
| Light-Grey | high | tolerated float |
| Dark-Grey | low | scrutinized |
| Black | very low | any accumulation is a flag |

**Target priority (D-15.19):** the FBI works **non-bank companies first**, ranked by how far each one's
SC exceeds its category tolerance (darkest + most over-tolerance = first), and **banks last** (it builds
evidence before striking the "big fish"). Whichever proposal below is chosen, its roll/meter is applied
in this order, so banks are seized only in the late game after the smaller anomalies are cleaned up.

- **Proposal F1 — Investigation meter (threshold-driven, deterministic ramp).** Each block a company's
  `ScReserve` sits above its category tolerance, an **investigation score** accrues (∝ how far over, ∝
  category darkness, ∝ era factor rising through the late years). Cross a threshold → the company is
  **flagged**; sustain it → **seizure**: dissolve, transfer `ScReserve` + `CollateralBtc`/treasury to
  the FBI, mark addresses seized, redirect future inflows (D-15.8). Falling back under tolerance decays
  the score. *Pro:* legible, tunable, no RNG swings, and it gives the player (as an NST holder) a clear
  "keep this bank's SC lean or vote it lighter" lever. *Con:* deterministic can feel gamey.
- **Proposal F2 — Seizure roll scaled by darkness × wealth × era (extends §12.4.3).** The existing
  per-quarter black-market seizure roll gains an FBI dimension: `P(seizure) = base(category) ×
  over-tolerance-multiplier(ScReserve) × era-multiplier(year)`, only active post-recruitment. On a hit,
  same seizure effect as F1. *Pro:* organic, reuses §12.4.3's cadence, historically "you never know
  when the raid comes." *Con:* RNG can nuke a company the player was managing well.

**DECIDED (D-15.21): the hybrid.** F1's investigation meter decides *who is a target* (deterministic,
player-legible), and a **capped roll on top** (F2-style, only for flagged targets) decides *which block
the raid actually lands* — suspense without pure randomness punishing good play. Gated behind the
14 Jun 2011 date, self-funding, non-bank-first / banks-last priority (D-15.19).

**Tolerance numbers — throughput-relative (calibration placeholders):** rather than absolute SC ceilings
(which go stale across the 2009–2025 span), a company's tolerance scales with **its own recent SC
throughput** `T` (e.g. trailing-quarter SC inflow, a figure the governance engine already has around):
`tolerance = categoryMultiplier × T`. Placeholder multipliers — **Official = ∞** (never flagged on SC
alone) · **Light-Grey = 8× · Dark-Grey = 3× · Black = 1×**. The investigation meter accrues when
`ScReserve > tolerance`, ∝ the overage and ∝ category darkness. All four numbers (and `T`'s exact
window) are the **P15.8 calibration targets** — tuned in the playtest, not fixed now.

### 5.3 — DEV readouts (OQ-15.9) — proposal to split across two scenes

- **New Central Bank (FED) scene** (Main Menu → Central Bank; DEV): per-client account rows — the
  **casino** and each founded **bank** — showing outstanding SC debt, loan history, and (for banks)
  collateral posted; plus system totals (total SC lent, total debt outstanding). This is the D-15.3
  "see the casino's loans from the FED, as its client" view.
- **WorldEconomy additions** (the existing DEV scene): (1) a **banking-layer solvency line** — total
  `CollateralBtc` value vs total bank FED debt (aggregate leverage/health); (2) a **per-bank strip** —
  each bank's FED debt, `CollateralBtc` (+ its live SC value), own SC treasury, # client companies,
  market category, under-collateralization flag; (3) the **post-bankruptcy recovery tracker** (D-15.8)
  — dissolved companies, absorbing creditor, cumulative redirected income vs debt owed,
  recovered/underwater/profit status.
- **`CompanyDetails` (player-facing, D-15.9):** when the player holds NST in a bank, a lending panel —
  FED debt, `CollateralBtc` + value, quarter installment due, and (when it fires) the shortfall vote.

**DECIDED (D-15.22): adopt all three surfaces.** The FED scene (DEV-only, D-15.16), the WorldEconomy
additions, and the `CompanyDetails` lending panel (player-facing, D-15.9) all land in plan15 (P15.7).

---

## 6. Open questions

**Resolved:** OQ-15.11 → D-15.11 · OQ-15.12 → D-15.12 · OQ-15.13 → D-15.13 · OQ-15.14 → D-15.14 ·
OQ-15.15 → D-15.15 · OQ-15.16 → D-15.16 · OQ-15.17 → D-15.17 · OQ-15.18 → D-15.18 · OQ-15.19 → D-15.19
(see §4).

**Still open — the remaining picks (no new questions this round):**

- **§5.1** — bank-selection algorithm: tie-break (A1 toward-Official), 3-tier framework depth (B1 full,
  capacity-infinite now), casino fallback pre-first-bank.
- **§5.2** — FBI mechanic: F1 (meter) vs F2 (roll) vs **hybrid**, plus first-cut per-category SC
  tolerance numbers.
- **§5.3** — DEV-readout split across the FED scene / WorldEconomy / `CompanyDetails`.

Once these three land, the design is complete enough to lock P15.0 and start building.

---

## 7. Phase map (high level — full subphase breakdown in §8)

> Each phase below is broken into concrete, individually-buildable subphases in **§8 (P15.1a … P15.8)**.
> This section is the one-line-per-phase index.

- **P15.0 — Design lock.** Resolve §5 + §6; record `D-15.x`; finalize persisted state shapes (FED
  registry, per-bank collateral/debt/client ledger, greed field) and confirm the `WorldFormatVersion`
  bump + delete-list / checkpoint / pre-genesis treatment (D-ND8.27 three-question rule).
- **P15.1 — The FED entity.** Central Bank service + `central_bank_state.json` + scene; migrate the
  casino's loan bookkeeping to be FED-recorded (casino reads from FED); wire `RegisterBurn` for
  repayments. No behavior change to the casino yet beyond "its loans are now FED-tracked."
  **→ broken into 5 subphases in §8 (P15.1a–e); Fork A adopted (D-15.23) — READY TO IMPLEMENT.**
- **P15.2 — Bank company balance sheets & selection.** Founded CB1 banks gain FED-debt + `CollateralBtc`
  + per-client ledger on the snapshot; assign the market-category gradient; implement the §5.1 selection
  framework with the casino fallback.
- **P15.3 — Reroute conversions through banks.** `TryConvertCompanyReserves`: BTC leg `company → bank`,
  SC leg paid by the bank via a FED auto-loan; collateral quarantined into `CollateralBtc`; ledger mint
  as `bank:<id>` debt.
- **P15.4 — Extra-lazy repayment + greed voting.** Quarterly full-fraction repayment (sell just-enough
  `CollateralBtc` on the day → `RegisterBurn`); the shortfall Board Vote (Dividends/Reserves split); the
  new greed attribute wired into the shortfall vote **and** the existing payout-rate vote.
- **P15.5 — Dissolution, closed-companies list & seized wallets.** Dissolve banks on unrecoverable
  shortfall; the Closed-Companies list (timestamp + reason); off-UI inflow redirection to the absorber;
  seized-wallet holding + the per-deposit reserves-vs-dividends vote (OQ-15.18 assignment rule); the DEV
  post-dissolution recovery tracker.
- **P15.6 — FBI investigation/seizure (in scope, D-15.14).** Per-category SC tolerances, the
  investigation meter + capped roll (§5.2 pick), activation at 14 Jun 2011, self-funding, seizure → FED.
- **P15.7 — Surfacing & telemetry.** FED scene rows, WorldEconomy additions, `CompanyDetails` lending
  panel, `bank_credit_trace.csv`.
- **P15.8 — Calibration playtest.** One DEV entry-year run (temporarily set `DevEntryYear`, restore to 0
  before merge) to verify conversions route through banks, debts accrue/repay, the carry behaves across
  a BTC bull/bear era, and the monetary invariant holds.

---

## Appendix A — The four roster bank companies

From `Data/Companies/company_roster.csv` (all band **CB1**, anchor **fictional**; the `market` column
is what plan15 reassigns per §5.1):

| Company | Founded | Weight | Proposed category |
|---|---|---|---|
| First Satoshi Savings | 2012-09-03 | 5 | Official |
| Digital Reserve Trust | 2013-06-17 | 5 | Light-Grey |
| Harbor Coin Bank | 2014-11-03 | 4 | Black |
| Ledger & Sons Private Bank | 2016-03-21 | 4 | Dark-Grey |

The **FED / Central Bank** is a separate non-auctionable entity (§3.1), not a roster row.

---

## Appendix B — Key code touch-points (from the §2 review)

| Concern | Location |
|---|---|
| Per-block company conversion trigger (to reroute) | `NetworkRoot.TryConvertCompanyReserves` (~L2047) |
| Casino SC counterparty (to become the bank/FED path) | `CasinoScBalanceService.TryPayCompanyProvisionSc` (~L320) |
| Casino loan bookkeeping (to migrate into the FED) | `CasinoScBalanceService`: `LoanCount`/`TotalLoaned`/`LoanHistory`, `AddLoanRecord` (~L119) |
| SC mint funnel / burn hook | `ScMonetaryLedgerService.AddLoanRecord` path / `RegisterBurn` (armed, caller-less) |
| Company governance state / snapshot / votes | `CompanyGovernanceState`, `CompanyVote`, `NetworkRoot.BlockchainStateSnapshot` |
| Quarterly cadence + payout-rate vote (greed hook) | `NetworkRoot.TickCompanyGovernance`, `OpenCompanyVote` (~L2141), vote resolution |
| Company treasury BTC | `NetworkRoot.CompanyTreasuryBtc` |
| Existing per-client ledger analog (the two-layer model) | `CasinoClientLedgerService` |
| DEV / player readouts | `Screens/WorldEconomy/`, `Screens/CompanyDetails/`, `Screens/CasinoGamblingFinances/` |

---

## 8. Detailed subphase breakdowns (P15.1–P15.8)

> All phases broken down below. Each subphase is meant to build + be verified + (optionally) commit on
> its own, step14-style. Line refs are against `main` at plan time. P15.1 is fully locked (Fork A); the
> later phases inherit the same data structures reviewed for P15.1 (`CompanyFounding` /
> `CompanyGovernanceState` / `CompanyVote` on `BlockchainStateSnapshot`; `MarketCategoryOrder =
> ["official","light_grey","dark_grey","black"]` as the §5.1 distance axis; `BotGovernancePreference`
> for the greed field; `CompanyVoteKind*` constants). Numbers/ratios called out are the decided ones;
> anything marked *placeholder* is a P15.8 calibration knob.

### P15.1 — The FED entity — ✅ IMPLEMENTED (2026-07-26, subphases a–e)

> **Build log.** All five subphases landed together on `bank-companies-sc-provisioning`; `dotnet build`
> clean. Files: **new** `Scripts/Services/CentralBankService.cs`, `Screens/CentralBank/CentralBank.{tscn,cs}`;
> **modified** `project.godot` (autoload #19, between `ScMonetaryLedgerService` and
> `BlockSessionCheckpointService`), `CasinoScBalanceService.cs` (loan state → read-through accessors,
> `AddLoanRecord` → `DrawFedLoan`, loan fields off its `Snapshot`), `BlockSessionCheckpointService.cs`
> (`CentralBankState` DTO + restore-before-casino ordering + pre-genesis reset; casino loan DTO fields
> removed), `ScMonetaryLedgerService.cs` (reconcile + live-state init now read the FED's `OutstandingDebt`,
> not the casino's `TotalLoaned`; burn comments de-armed), `NetworkRoot.cs` (`WorldFormatVersion` 3 → 4 +
> `central_bank_state.json` on the delete list), `SceneManager.cs` + `MainMenu.{tscn,cs}` (scene entry),
> `CasinoGamblingFinances.cs` (reads the FED-backed history, shows draw/repay kind + outstanding debt).
> Docs updated in the same branch: `CLAUDE.md` (autoload section, checkpoint pattern, file map, nav map,
> Implemented list) and `Documentation/ProjectDesignManual.md` **Ch. 39**.
>
> Two deviations from the subphase text, both deliberate and documented in Ch. 39:
> - `TotalLoaned` maps to the FED's **`TotalDrawn`** (cumulative) so `CumulativeProfitSinceLoan` keeps its
>   exact meaning, while the **ledger reconcile** compares against **`OutstandingDebt`** — the two figures
>   are equal today (nobody repays) and legitimately diverge from P15.4.
> - The FED account carries `TotalRepaid`/`RepayCount` and a per-client history **cap of 500** (totals stay
>   exact independently of the cap — the `ScMonetaryLedgerService.MaxEventHistory` precedent); both readouts
>   report any surplus as "(+N older)".
>
> **Left for the developer's in-game verification:** the four subphase tests (boot/persist/reload; a
> scripted draw→repay leaving the invariant intact; a casino auto-loan appearing as a FED `draw` with
> `CasinoGamblingFinances` reading identical numbers; block → restart → FED debt restored, and a
> pre-genesis boot resetting it to empty). Note the version bump means **the first launch wipes the world**.

**Goal of P15.1:** stand up the **Central Bank (FED)** as an explicit, persisted, DEV-visible entity
that the casino borrows from — with the casino's loan bookkeeping migrated onto it (no double-storage)
and `RegisterBurn` wired for repayments — **without changing any casino behavior** (the casino still
draws unlimited auto-loans exactly as today, D-15.17). No bank companies yet; that is P15.2.

### What already exists (the substrate — from the §2/§3 code review)

- **`ScMonetaryLedgerService`** already holds `_debtByBorrower` (dict `borrowerId → decimal`, with
  `PartyCasino = "casino"`), `RegisterLoanDraw(borrower, amount, reason)` (mint), and
  **`RegisterBurn(borrower, amount, reason)` — already implemented, explicitly "armed, no caller yet,
  for the Central Bank subphase."** It is checkpoint-covered (`CheckpointState` DTO with `DebtByBorrower`),
  pre-genesis reset, in the delete-list, and surfaced in `WorldEconomy`. This is the **macro monetary
  layer** — mint/burn events + the `circulation = grants + debt` invariant.
- **`CasinoScBalanceService`** today stores its OWN `LoanCount` / `TotalLoaned` / `LoanHistory`
  (persisted in `casino_sc_balance_state.json`, checkpoint DTO fields `CasinoScLoanCount/TotalLoaned/
  LoanHistory`), and `AddLoanRecord` funnels every draw into `RegisterLoanDraw(PartyCasino, …)`. The
  ledger already reconciles `_debtByBorrower["casino"]` against `casino.TotalLoaned` — so casino debt is
  effectively double-stored today (casino's own copy + the ledger's), which is exactly what D-15.3/D-15.5
  want to collapse.

### ✅ Architectural fork RESOLVED (D-15.23) — Fork A adopted, Fork B deferred as optional cleanup

The FED is the **entity/relationship layer** (per-client accounts + loan/repayment histories + a scene +
custodial seized wallets later); the ledger is the **macro accounting layer** (mint/burn log +
circulation invariant). Two ways to relate them:

- **Fork A *(recommended)* — new `CentralBankService`, ledger unchanged in role.** The FED owns per-client
  accounts (`{ OutstandingDebt, TotalDrawn, List<FedLoanRecord> }`) — the authoritative per-client store.
  The ledger keeps its `_debtByBorrower` macro projection, synced **for free** because the FED's draw/
  repay API calls the ledger's existing `RegisterLoanDraw`/`RegisterBurn` (the same lockstep that keeps
  `_debtByBorrower["casino"]` == `casino.TotalLoaned` today). Casino stops storing its own loan copy and
  reads through the FED. *Lowest risk, minimal churn to the audited invariant machinery; the only thing
  that "moves" is the casino's private copy → the FED.*
- **Fork B — the FED subsumes the ledger's debt store.** Retire `_debtByBorrower`; the ledger derives its
  debt total from `CentralBankService`, keeping only the mint/burn event log + genesis grants. Truly one
  debt store, but a bigger refactor of the ledger's checkpoint DTO + reconciliation, with more regression
  surface. Defer as an optional cleanup.

**DECIDED (D-15.23): Fork A.** It satisfies "no double-storage" at the level that mattered (the casino's
copy is gone), keeps the two responsibilities cleanly separated, and touches the least audited code.
**Fork B (unifying the debt store) is deferred as an optional future cleanup**, not scheduled. The
subphases below implement Fork A.

### Subphases

- **P15.1a — `CentralBankService` skeleton + persistence.** New autoload registered **between
  `ScMonetaryLedgerService` and `BlockSessionCheckpointService`** in `project.godot` (must be in the tree
  before the checkpoint restore/reset runs, the `PlayerBankAccountService`/`CasinoCoinSwapService`
  precedent). Holds `Dictionary<string, FedClientAccount>` where `FedClientAccount =
  { decimal OutstandingDebt, decimal TotalDrawn, List<FedLoanRecord> History }`, and `FedLoanRecord =
  { decimal Amount, string Kind ("draw"|"repay"), string Reason, DateTime GameDateLocal }` (game-time per
  the canonical wall-clock rule). `LoadState`/`SaveState` to `user://central_bank_state.json`,
  `EnsureLoaded()`. No behavior wired yet — pure store + read accessors (`OutstandingDebt(clientId)`,
  `TotalDrawn(clientId)`, `TotalOutstandingDebt`, `History(clientId)`). **Test:** boots, persists, reloads.
- **P15.1b — Draw / repay API + ledger sync.** `DrawLoan(clientId, amount, reason)` → `account.OutstandingDebt
  += amount`, `account.TotalDrawn += amount`, append a `"draw"` record, then call
  `ScMonetaryLedgerService.RegisterLoanDraw(clientId, amount, reason)` (mint). `Repay(clientId, amount,
  reason)` → clamp to `OutstandingDebt`, decrement, append a `"repay"` record, then
  `ScMonetaryLedgerService.RegisterBurn(clientId, amount, reason)` (**burn — the ledger's first real
  caller**). Lazy-resolve the ledger (registers before us, so it IS in the tree; still null-guard). **Test:**
  a scripted draw then repay leaves `circulation = grants + debt` intact across both.
- **P15.1c — Migrate the casino onto the FED (Fork A).** Re-point `CasinoScBalanceService.AddLoanRecord`:
  instead of storing `LoanCount`/`TotalLoaned`/`LoanHistory` locally + calling the ledger directly, call
  `CentralBankService.DrawLoan(PartyCasino, amount, reason)`. Turn casino `LoanCount`/`TotalLoaned`/
  `LoanHistory` into **read-through accessors** over the FED account (`TotalLoaned = fed.TotalDrawn(casino)`,
  preserving `CumulativeProfitSinceLoan = TotalSc − TotalLoaned`; `LoanCount = History(casino).Count(draws)`).
  Remove those three fields from the casino's `Snapshot` + `casino_sc_balance_state.json`. Preserve external
  readers (`CasinoGamblingFinances` shows loan count / total / `CumulativeProfitSinceLoan`). **Test:** a
  casino auto-loan (bankruptcy recharge) shows up as a FED `draw` on the casino's FED account, and
  `CasinoGamblingFinances` reads identical numbers to before.
- **P15.1d — Checkpoint / pre-genesis / delete-list + version bump (the D-ND8.27 three-question rule).**
  Add `CentralBankService.CaptureCheckpointState()` / `RestoreFromCheckpoint(state)` /
  `ResetToPreGenesisDefaults()` (empty accounts). Wire into `BlockSessionCheckpointService`: capture in
  `CaptureCheckpoint`; restore **before** the casino restore *and* before the ledger's live-state init
  (which reads casino debt); add the FED reset to `ResetToPreGenesisDefaults`. Move the casino's retired
  loan DTO fields into the FED's checkpoint state. Add `central_bank_state.json` to
  `NetworkRoot.ResetWorldIfIncompatible`'s delete list. **Bump `WorldFormatVersion` 3 → 4** here (D-15.10;
  first plan15 subphase that changes persisted world state — every later plan15 file just joins the same
  delete list, no further bump). **Test:** mine a block → restart → FED debt restored to the block; with
  no block, a boot pre-genesis-resets the FED to empty.
- **P15.1e — The FED DEV scene.** New `Screens/CentralBank/` (Main Menu → "Central Bank [DEV]", D-15.16;
  add `SceneManager.SceneId.CentralBank` + `Paths` entry + a MainMenu button + a `StatusBar`). Rows per
  FED client (today only the casino): outstanding debt, total drawn, a scrollable loan/repayment history,
  and system totals (total drawn, total outstanding). **Read `Documentation/ProjectDesignManual.md` Ch. 29
  before building the scene** (fixed-footer Back button outside the scroll). Refresh via the existing poll
  pattern for now (a migration candidate, Ch. 38 — not blocking). **Test:** the scene shows the casino's
  FED account matching `WorldEconomy`'s casino debt line.

**Exit criteria for P15.1:** the casino borrows from a visible, persisted FED; repayments can burn SC
(exercised by a scripted test even though the casino itself never repays yet); the monetary invariant
holds across draw/repay; checkpoint/reset/delete-list complete; `WorldFormatVersion = 4`; **zero casino
behavior change**. This leaves a clean base for P15.2 to add the bank companies as additional FED clients.

### P15.2 — Bank company balance sheets & selection — ✅ IMPLEMENTED (2026-07-26, subphases a–d)

> **Build log.** `dotnet build` clean. **Modified**: `Data/Companies/company_roster.csv` (the gradient on
> three bank rows + locked-category notes), `CompanyRoster.cs` (`BankCompanyIds`/`IsBank`/`Banks` + a
> loader sanity warning), `NetworkRoot.cs` (`IsBankCompany`/`BankCompanyCategory`/`FoundedBanks`; the
> P15.2b lock in `CloseCompanyVote` + its trace note; `_bankState` + `BankBalanceSheet`/`BankClientAccount`/
> `BankClientEntry` + snapshot capture/restore + `BankSheet`/`GetBankBalanceSheet`/`BankCollateralBtc`/
> `RecordBankProvision`; `SelectFinanciers` + `FinancierChoice` + the tier constants; the two DEV readouts),
> `Screens/CentralBank/CentralBank.cs` (the Banking layer block). Docs: `CLAUDE.md` Implemented list +
> `Documentation/ProjectDesignManual.md` **§39.7–39.8**.
>
> Three decisions taken inside the subphases, all documented in §39.7–39.8:
> - **Bank identification = a closed id set in `CompanyRoster`, not an `is_bank` CSV column** (the plan
>   offered either). plan15 creates no companies (D-15.6), so the set is closed by design; a column would
>   have touched all 44 rows to encode four `true`s and would let a 45th row silently become a bank. Every
>   caller goes through `IsBank`/`Banks`, so promoting it to a column later changes nothing else.
> - **No second `WorldFormatVersion` bump for the P15.2a gradient change.** A bank's category is LOCKED,
>   which makes it a *derived* value — so it is re-derived from the roster in `ApplyStateFromSnapshot`,
>   correcting any bank that founded under the old roster instead of stranding a stale category.
> - **`RecordBankProvision` ships unused**, awaiting P15.3a's provisioning path (which must call it only
>   after BOTH legs succeed, so the client book never records a half-executed swap).
>
> **Scope note:** the FED scene's read-only **Banking layer** block (founded banks + a financier-selection
> preview) is an early slice of **P15.7a**, pulled forward because P15.2 changes no behaviour and would
> otherwise be unverifiable in-game. The preview probes `SelectFinanciers` with a nominal 1 SC — honest
> only while capacity is infinite; revisit at ND.8e.
>
> **Left for the developer's in-game verification:** the four subphase tests (the banks found with the
> assigned categories; a bank's NST holders voting a market shift does not move it; an empty balance sheet
> persists/restores; a 2013 company selects the nearest-category founded bank while a 2011 one shows the
> casino fallback). Reaching 2012-09+ needs a `DevEntryYear` build — on canon from 2009 the Banking layer
> correctly reads "No bank company has founded yet."

**Goal:** make the four CB1 banks real FED clients with a balance sheet, assign the locked category
gradient, and build the §5.1 selection framework — still **no conversions rerouted** (that is P15.3).

- **P15.2a — Roster category gradient + bank flag (data).** In `Data/Companies/company_roster.csv` set
  the `market` column of the four bank rows to the gradient (First Satoshi Savings → `official`, Digital
  Reserve Trust → `light_grey`, Ledger & Sons Private Bank → `dark_grey`, Harbor Coin Bank → `black`,
  per D-15.20/App. A) and add a way to identify banks (a new `is_bank` column, or a known-id set in
  `CompanyRoster`). Confirm `CompanyRoster` parses the new column and foundings apply the category. **Test:**
  the four banks found with the assigned categories.
- **P15.2b — Lock bank categories (D-15.12).** Guard the ±1 market-shift block in the vote resolver
  (~L2255–2262) so a bank company never drifts — its `MarketCategory` stays `== DefaultMarketCategory`.
  Add `IsBankCompany(nodeId)` / `BankCompanyCategory(nodeId)` pure helpers (from P15.2a's flag). **Test:**
  a bank whose NST holders vote a market shift does not move.
- **P15.2c — Bank balance sheet on the snapshot.** A `_bankState` dict keyed by bank nodeId (only banks
  carry it): `{ decimal CollateralBtc, Dictionary<string, BankClientAccount> Clients }`, where
  `BankClientAccount = { decimal BtcBought, decimal ScPaid, List<BankClientEntry> History }` (the D-15.5
  layer-1 per-client ledger). Rides `BlockchainStateSnapshot` (free checkpoint / delete-list / pre-genesis
  by inheritance — the ND.8g precedent). **Test:** a bank's (initially empty) balance sheet persists and
  restores with the world.
- **P15.2d — Selection framework (D-15.20).** `SelectFinanciers(companyId, amountSc, block)` → ordered
  founded-bank list: nearest category by `MarketCategoryOrder` distance (tie-break toward the lower index
  = Official), then any single full-funder, then a split (tiers 2/3 built but **dormant** — capacity is
  infinite under auto-loans, so it always resolves to one nearest bank today). Returns the **casino
  fallback** when no founded bank exists yet (any pre-2012-09 date, or a category with no founded bank).
  **Test:** a 2013 company selects the nearest-category founded bank; a 2011 company → casino fallback.

**Exit:** the four banks are typed, categorized (locked), carry a persisted balance sheet, and
`SelectFinanciers` resolves correctly — with conversions still flowing to the casino (unchanged) until P15.3.

### P15.3 — Reroute conversions through banks — ✅ IMPLEMENTED (2026-07-26, subphases a–b)

> **Build log.** `dotnet build` clean. All in `NetworkRoot.cs`: `_centralBank` autoload reference;
> `CompanyOwnBtc` (the quarantine helper) replacing `CompanyTreasuryBtc` at the three governance sites;
> the `COLLATERAL`-memo skip in `AccumulateCompanyInflows`; `TryConvertCompanyReserves` reworked around
> `SelectFinanciers` + the two new counterparty paths `TryConvertViaBank` / `TryConvertViaCasino`; the
> `CompanyConversionMemo`/`BankCollateralMemo` constants; `AppendBankCreditTrace` + `BankCreditTracePath`
> + its delete-list entry. Docs: `CLAUDE.md` Implemented list + `ProjectDesignManual.md` **§39.9/§39.9.1**.
>
> Three deviations from the subphase text, all documented in §39.9:
> - **P15.4a's `CollateralBtc` quarantine shipped HERE, not next phase.** Without it a bank's own
>   conversion would sell collateral BTC while `_bankState.CollateralBtc` still claimed it existed — a
>   *persisted figure diverging from reality*, which P15.4d would then try to sell. That is a different
>   category of problem from an unfinished feature, so the ~3-line netting shipped with the mechanism that
>   creates the collateral. **P15.4a is therefore already done**; it survives only as a verification item.
> - **A FOURTH leak the plan didn't list:** `AccumulateCompanyInflows` counts BTC arriving at a company
>   address for the D-ND8.18 >30% special vote. Collateral arriving at a bank is the asset leg of a loan,
>   not business inflow — and a spurious special vote **pauses the game** wherever the player holds NST in
>   that bank. Excluded via the `COLLATERAL` display memo, which makes `InputDataText` load-bearing on this
>   one tx type.
> - **`bank_credit_trace.csv` (P15.7d) shipped early**, for the same reason the P15.2 readout did: it is
>   the only observability the credit loop has before the P15.7 surfaces, and the P15.8 run reads it.
>
> **Tier 3 stays honest rather than half-built:** a split answer would need the BTC leg split into several
> fee-bearing sends, which is unbuilt and unreachable while capacity is infinite. The code detects a
> multi-financier answer, warns, and funds the whole conversion from the casino. With
> `BankFundingCapacitySc`, that is the COMPLETE list of what ND.8e must touch to switch real limits on.
>
> **No `WorldFormatVersion` bump** (as planned): conversions that already happened stay internally
> consistent — the counterparty change is forward-only.
>
> **Left for the developer's P15.8 verification:** a scripted conversion moving BTC company→bank with SC to
> the company and FED debt keyed `bank:{id}`; a post-2012-09 exchange routing through the nearest-category
> bank while pre-first-bank conversions still hit the casino; and the quarantine (a bank's collateral
> persists across blocks, is never dividended, and never triggers a >30% vote, while a normal inflow to the
> same bank still converts).

**Goal:** `TryConvertCompanyReserves` (~L2047) provisions through the selected bank instead of the casino.

- **P15.3a — Bank provisioning path.** `TryPayCompanyProvisionScFromBank(bank, company, scAmount, btc,
  block)`: bank draws `CentralBankService.DrawLoan("bank:{id}", scAmount, "provision")` (FED debt +
  mint), credits `company.ScReserve`, receives the BTC on-chain into the bank's wallet → its
  `CollateralBtc`, appends the `BankClientAccount` entry. Failed broadcast unwinds the SC leg (the
  existing `ReceiveSwapSc`/conversion-unwind pattern). **Test:** a scripted conversion moves BTC
  company→bank, SC to the company, and lands FED debt keyed `bank:{id}`.
- **P15.3b — Reroute the trigger.** In `TryConvertCompanyReserves`, replace the `TryPayCompanyProvisionSc`
  (casino) counterparty with `SelectFinanciers(...)` → P15.3a; keep the casino path as the explicit
  fallback branch. Clean-rate pricing + median-fee logic unchanged (D-ND8.24). A bank converting **its
  own** CB1 inflows still uses the normal path — that stream is distinct from `CollateralBtc` (P15.4a).
  **Test:** with a founded bank, an exchange's conversion routes through it; the trace shows the bank as
  counterparty; pre-first-bank conversions still hit the casino.

**Exit:** post-2012-09 company conversions are bank-funded (FED-backed); the casino path survives only
as the pre-first-bank fallback.

### P15.4 — Extra-lazy repayment + greed voting — ✅ IMPLEMENTED (2026-07-26; a at P15.3, b–e here)

> **Build log.** `dotnet build` clean. `NetworkRoot.cs`: the greed constants + `GreedPayoutMultiplier` /
> `GreedDividendsCutPercent` ladders; `GreedPreference` on `BotGovernancePreference` (sentinel default) +
> the greed draw in `EnsureBotGovernancePreferences` + `BackfillGreedPreferences`; `BuildBotBallot` greed
> bias; `CompanyVoteKindShortfall` + `CompanyBallot.DividendsCutPercent` + `CompanyVote.ShortfallScTarget`
> + `CompanyGovernanceState.PendingShortfallSc`/`UnrecoverableShortfallSc`; `BankQuarterlyRepaymentFraction`
> + `TryBankQuarterlyRepayment` + `TrySellCollateralForSc` + `BankRepaymentMemo`; `ApplyShortfallVote`; the
> shortfall branches in `TickCompanyGovernance` / `OpenCompanyVote` / `CloseCompanyVote`; the optional
> `dividendsCutPercent` on `TryRegisterPlayerVote` + `GetOpenVoteKind` / `GetOpenVoteShortfallTarget`;
> shortfall fields on `BankLayerRow`. `CompanyDetails.cs`: `BuildShortfallBallot`. `CentralBank.cs`: the
> ⚠/✗ shortfall rows. Docs: `CLAUDE.md` + `ProjectDesignManual.md` **§39.10–39.11**.
>
> Four decisions taken inside the subphases, all documented in §39.10–39.11:
> - **`GreedPreference` defaults to `""`, not to a stance.** `EnsureBotGovernancePreferences` early-returns
>   once every bot has a record, so a world drawn before greed existed would sit on the neutral stance
>   forever and silently disable the axis. A sentinel makes "absent" distinguishable from "drew neutral",
>   which is what lets `BackfillGreedPreferences` fill only the missing slots — no format bump needed.
> - **The casino buys the collateral** (the plan didn't name a counterparty; BTC on-chain needs a real
>   buyer). It is the designated SC liquidity backstop. Consequence worth watching at P15.8: when the
>   casino must auto-loan to buy, the effect is a debt TRANSFER bank→casino rather than a net burn.
> - **Both "sources" of the shortfall draw from the same `ScReserve`** — the vote decides who BEARS it (a
>   dividends cut also shrinks `QuarterDividendSc`; a reserves cut leaves the dividend whole). A cut larger
>   than the existing dividend spills onto the reserve side; already-dripped SC is never clawed back.
> - **The `CompanyDetails` shortfall ballot control shipped here, not at P15.7c.** The vote pauses the game
>   for a player NST holder, and the existing panel would have shown reserve/market/payout dials the
>   resolver ignores for this kind — actively misleading. The optional parameter on `TryRegisterPlayerVote`
>   additionally guarantees the pause can never deadlock.
>
> **Placeholders for P15.8:** `BankQuarterlyRepaymentFraction` (0.10), the payout multiplier ladder
> (0.5/1.0/1.5/2.0) and the §3.3 dividends-cut table (90/70/30/10).
>
> **Left for the developer's P15.8 verification:** each bot's greed is stable across a restart; an
> extremely-greedy bot ballots a higher payout than a not-so-greedy one on the same company; a bank's FED
> debt steps down each quarter with `circulation = grants + debt` intact and collateral dropping by exactly
> what was sold; and an engineered BTC drop opens the shortfall vote with the correct SC target, bots
> resolving per greed and the gap closing from the two sources in the voted proportions.

### P15.4 — Extra-lazy repayment + greed voting

- **P15.4a — `CollateralBtc` quarantine (D-15.4). — ✅ ALREADY IMPLEMENTED at P15.3a** (see that build log:
  leaving it to this phase would have let `_bankState.CollateralBtc` claim BTC the bank had already sold).
  Shipped as `NetworkRoot.CompanyOwnBtc` (= treasury − collateral) at the three governance sites, plus a
  fourth exclusion the plan hadn't listed — `AccumulateCompanyInflows` skipping `COLLATERAL`-memo arrivals
  so collateral never fires a game-pausing >30% special vote. **Test (still owed, P15.8):** a bank's
  collateral persists across blocks (not auto-sold), while a normal inflow to the bank still converts.
- **P15.4b — Greed attribute (D-15.13).** Add `GreedPreference` (enum `not_so_greedy | almost_greedy |
  greedy | extremely_greedy`) to `BotGovernancePreference` (L5232); draw it per world in
  `EnsureBotGovernancePreferences` (a 4th assigned axis) — persisted in the snapshot's
  `BotGovernancePreferences` + reset path (L4810) for free. **Test:** each bot has a stable greed value
  across a restart.
- **P15.4c — Greed biases the payout vote (D-15.13).** In the bot-ballot builder (~L2183/2190), modulate
  `PayoutRatePercent` by greed (extremely-greedy → toward the `2×` clamp, not-so-greedy → toward
  retention). Applies to **all** companies' quarterly votes. **Test:** on the same company an
  extremely-greedy bot ballots a higher payout than a not-so-greedy one.
- **P15.4d — Quarterly extra-lazy repayment (D-15.4).** In `TickCompanyGovernance`'s quarter-end path,
  each bank owes a fixed fraction of its FED principal; sell **just enough** `CollateralBtc` at the day's
  clean rate → `CentralBankService.Repay("bank:{id}", sc, "quarterly")` (burn). No sales between payment
  days. **Test:** a bank's FED debt steps down each quarter; `circulation = grants + debt` holds;
  collateral drops by exactly what was sold.
- **P15.4e — The shortfall vote (new `CompanyVoteKindShortfall`, D-15.15).** When selling all
  `CollateralBtc` can't raise the installment, open a shortfall vote: dividends-cut vs company-reserves-cut
  split (default 50/50, greed-biased per §3.3's table); **pauses the game** for a player NST holder
  (`IsAwaitingPlayerVote`), bot-resolved silently otherwise; apply the split to close the gap. **Test:**
  engineer a BTC drop so a bank underpays → the vote opens with the correct SC target; bots resolve per
  greed; the gap closes from the two sources in the voted proportions.

**Exit:** banks service their FED debt lazily from collateral; a BTC drop triggers the greed-weighted
shortfall vote; the casino still never repays (D-15.17).

### P15.5 — Dissolution, closed-companies list & seized wallets

- **P15.5a — Dissolution + Closed-Companies list (D-15.15/17).** A bank whose shortfall vote still can't
  cover the installment → **dissolve**. New `CompanyClosure { nodeId, closedAtMs (game time), reason
  ("debt_default"|"fbi_seizure") }` in a `_closedCompanies` snapshot list; drop the company from live
  foundings/governance. **Test:** an unrecoverable bank closes and appears in the list with reason
  `debt_default`.
- **P15.5b — Off-UI inflow redirection (D-15.8).** A per-block sweep forwards any inflow arriving at a
  closed company's address to its **absorber** (the FED for a debt-default), and a DEV tracker accrues
  recovered-vs-owed. **Test:** a scheduled inflow to a dead company lands at the FED and is tracked.
- **P15.5c — Seized-wallet inheritance + FED custody (D-15.18).** The FED assigns a closed company's
  wallet to a **solvent bank of the matching category**, which then processes its inflows through its
  **own band/level normal governance** (no forced SC, no bespoke vote — D-15.12). No matching solvent
  bank ⇒ the FED holds it **100% as BTC**, custodial, until one can inherit it. **Test:** a black-category
  closure is inherited by the (solvent) Black bank; with none solvent, the FED holds it as BTC.
- **P15.5d — Liquidation semantics + notification (D-15.15).** On closure, NST/PST holders lose their
  tokens and the company's future payments; a player holder is notified (a Closed-Companies entry the
  `CompanyDetails` sibling view reads, P15.7c). **Test:** a player PST holder in a closed company loses
  the holding and sees the closure reason.

**Exit:** banks (and, via P15.6, any company) can die, leaving a Closed-Companies record, redirected
income, and a category-matched seized-wallet inheritance chain.

### P15.6 — FBI investigation / seizure (D-15.21, the hybrid)

- **P15.6a — Activation gate + tolerance model.** FBI activity begins on **14 Jun 2011** (route the date
  through `TimelineConfig.Shift`, D-15.14). Per-category SC tolerance `= categoryMultiplier ×
  recentScThroughput` (placeholders **Official ∞ / Light-Grey 8× / Dark-Grey 3× / Black 1×**; `T` window a
  P15.8 knob). **Test:** zero FBI activity before the date.
- **P15.6b — Investigation meter (F1) + priority (D-15.19).** Per block, a company whose `ScReserve` >
  its tolerance accrues an investigation score (∝ overage × category darkness); it decays under
  tolerance. Evaluated **non-banks first** (ranked by overage), **banks last**. **Test:** a black-market
  SC-hoarder accrues and flags; an official company never does; a bank only after non-banks are cleared.
- **P15.6c — Capped seizure roll (F2) + effect.** Each block, flagged targets roll a **capped**
  probability for the raid; on a hit → seizure: reuse P15.5a with reason `fbi_seizure`, move
  `ScReserve` + treasury/`CollateralBtc` to the **FED** (→ P15.5c inheritance), and **self-fund the FBI**
  (+ the initial FED grant at activation). **Test:** a sustained-flagged company is eventually seized;
  its funds land at the FED.
- **P15.6d — Player agency + notification.** A player-held company under investigation surfaces the risk
  in `CompanyDetails` (P15.7c); a seizure notifies. **Test:** the warning shows while over-tolerance and
  clears on de-escalation.

**Exit:** from mid-2011 the FBI cleans up SC-hoarding dark companies first and, in the late game, can
reach banks — all seizures flowing to the FED and its inheritance chain.

### P15.7 — Surfacing & telemetry (D-15.22)

- **P15.7a — FED scene: bank clients.** Extend the P15.1e FED scene with the bank clients (debt, collateral,
  per-client sub-ledger) beside the casino row.
- **P15.7b — WorldEconomy additions.** Banking-layer solvency line (Σ `CollateralBtc` value vs Σ bank FED
  debt), a per-bank strip (debt / collateral+value / own SC / #clients / category / under-collateral flag),
  the post-dissolution recovery tracker, and the Closed-Companies list. (Existing DEV scene — mind Ch. 38
  poll pattern, not blocking.)
- **P15.7c — `CompanyDetails` lending panel (player-facing, D-15.9).** For a player NST holder in a bank:
  FED debt, `CollateralBtc` + live value, the quarter installment, the shortfall vote when live, and the
  FBI-investigation warning (P15.6d). Plus the Closed-Companies sibling view (P15.5d). **Read
  ProjectDesignManual Ch. 29 before any scroll/scene work.**
- **P15.7d — Telemetry.** `user://logs/bank_credit_trace.csv` (one row per provision / repayment /
  shortfall / dissolution / seizure), added to the `NetworkRoot.ResetWorldIfIncompatible` delete list (the
  TL.3 maintenance rule).

**Exit:** every new account and flow is inspectable (FED scene + WorldEconomy DEV, `CompanyDetails`
player-facing) and traced.

### P15.8 — Calibration playtest

- One **DEV entry-year** run (temporarily set `TimelineConfig.DevEntryYear` to a chosen year — restore to
  `0` before merge, the step14 lesson): verify conversions route through banks, FED debts accrue and repay,
  the BTC carry behaves across **a bull and a bear era**, shortfall votes + dissolutions + FBI seizures
  fire sensibly, and `circulation = grants + debt` holds throughout. **Tune the placeholders**: the FBI
  tolerance multipliers + `T` window, and the quarterly repayment fraction. Restore `DevEntryYear = 0`;
  final clean build; hand to the developer for in-game verification + commit. **(Never headless-launch the
  real save — dotnet build + developer verification, per the standing rule.)**

**Exit:** plan15 behaves across eras with the invariant intact; ready to merge to `main` (restore
`DevEntryYear = 0` first).
