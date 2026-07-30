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
> **P15.4 (b–e)**, **P15.5 (a–d)**, **P15.6 (a–d)**, **P15.7**, **P15.9**, **P15.10** and **P15.11**.
> **Every mechanism of the reform is built and the whole credit loop has now been observed running.**
>
> **⏸ CLOSING STATUS (2026-07-30): the step ships; P15.8 is SUSPENDED, not failed.** The calibration
> playtest ran from 2010-03-21 to **~Oct 2014 (block 2699, 30 companies, 2 banks, FBI active)** and
> verified checklist sections **A–F end to end with zero errors and zero tripwires**. It **cannot** reach
> section **G** (dissolution / seizure / insolvency) by playing forward: in 3⅓ in-game years past FBI
> activation the world produced **one** `shortfall_pending` and **no** seizure or dissolution at all. Those
> states are gated behind gameplay depth that does not exist yet, so the placeholder tuning they were meant
> to inform (§9 J) stays **explicitly unfinished and is not a merge blocker**. The run's full audit — six
> findings with the numbers behind them — plus the exact conditions for resuming are **§10**.
> **Resume P15.8-G after Step 16**, which builds the gameplay depth that makes those states reachable.
>
> Branch: `bank-companies-sc-provisioning` off `main` (canonical timeline, `DevEntryYear = 0` restored
> before merge). **Merged to `main` 2026-07-30.**

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
- **D-15.25 (R3, 2026-07-28 — AMENDS D-15.21's basis; multipliers, meter and roll unchanged):** the
  tolerance basis is the **charter reserve**, not recent throughput:
  `tolerance = categoryMultiplier × ReserveScPercent × (CompanyOwnBtc × price + ScReserve)`, valued on the
  byte-identical basis `TryConvertCompanyReserves` targets; no market price ⇒ exempt that block. **Why:**
  the throughput basis judged a FLOW (SC converted in the last ≤2 quarters) against a STOCK the design
  intends to be HELD — conversions stop the moment a company reaches its voted target, so `T` falls to 0
  two quarters later, tolerance reads `0.00`, the overage pins at the cap, and **every** non-Official
  company that ever reached its target was guaranteed a federal file within ~13–25 blocks. Found in a
  Nov-2011 playtest (three companies under investigation, all "0.00 SC tolerated"); the 2011 price crash
  sharpened it, since a falling price shrinks the SC target and so removes the deficit that drives
  conversions. "Explained wealth" is now what a company's own shareholders voted to hold, which also makes
  the player's lever the **reserve % vote** rather than a stale accident of conversion timing. A `0%`
  charter becomes a REAL zero (holding SC the charter forbids) and stays escapable via dividends/a new
  vote. `ScInflow*QuarterSc` are kept and still maintained (activity metric + P15.8 input; deleting a
  persisted field would force a `WorldFormatVersion` wipe of a live playtest) but are no longer read by
  the meter. **Companion:** raid eligibility becomes `score ≥ threshold` **AND** `overage > 0` — with a
  1.0/block decay a company back under tolerance otherwise stayed seizable for ~100 blocks for a condition
  that no longer held; the file stays open (a relapse re-arms instantly) but inactive, and
  `GetFbiInvestigationWarning` gained the matching fourth state. No format bump; display + rule only.
- **D-15.26 (R3, same day — the meter's missing ceiling):** `InvestigationScoreCap = 2 ×
  InvestigationFlagThreshold`. `InvestigationOverageCap` capped the meter's gain RATE; nothing capped its
  accumulated HEIGHT, and gain (up to `0.5 × 4 × 4 = 8`/block, black at the overage cap) outruns decay
  (`1.0`/block) **8:1** — 200 blocks over tolerance bought ~1,600 score and a >2-in-game-year cool-off,
  which silently removed the lever the decay is supposed to BE. **The value is derived, not picked:** the
  roll `min(2%, 0.5% × darkness × score/threshold)` saturates at its 2% cap by `score = 2 × threshold` for
  the lightest non-exempt category (darkness 2) and earlier for darker ones, so beyond that point extra
  score alters no risk and only lengthens the cooldown. Worst-case cool-off becomes 200 blocks (~4.5
  in-game months); **time-to-flag is unchanged**. Clamped on the DECAY branch too, so files inflated under
  the retired throughput basis drop to the ceiling on their first cooling block — this is deliberately
  used *instead of* a one-off amnesty pass, which would have needed a persisted "already amnestied" marker.
  **Board parity:** the FED scene's red/orange split keyed on `score ≥ threshold` alone and so painted an
  already-closing file as a live threat; it now tests both halves of raid eligibility (red ⚑ eligible now ·
  orange over-tolerance and growing · grey cooling) and states the blocks remaining via the new shared
  `NetworkRoot.FbiBlocksToClear` — §39.16 rule 6, since a status colour is a claim about what the tick will
  do next. No format bump.
- **D-15.22 (§5.3):** adopt **all three DEV/UI surfaces** — the DEV-only FED scene, the WorldEconomy
  additions, and the player-facing `CompanyDetails` lending panel (all in P15.7).
- **D-15.23 (§8 fork):** **Fork A** — a new `CentralBankService` owns the per-client FED accounts +
  histories; the `ScMonetaryLedgerService` keeps its macro role, its `_debtByBorrower` synced for free
  via the existing `RegisterLoanDraw`/`RegisterBurn` hooks. Fork B (retiring `_debtByBorrower` so the
  FED is the single debt store) is a **deferred optional cleanup**, not scheduled.

### Round 4 (2026-07-27, from the P15.8 playtest — see P15.9)

- **D-15.24 (P15.9):** a bot's `CurrencyBandPreference` is a position on a **global SC-ness axis**, not a
  literal ballot. Every bot ballot is **projected into the band of the company being voted at** —
  **default-anchored** (the bot's own band default maps to the company's band default, linear on each
  side, so the map is the identity when the two bands agree), rounded to a whole percent, `.5` away from
  zero. **Project, never clamp:** clamping collapses every out-of-band bot onto the same bound, which is
  what left the final average pinned there. The final clamp in `CloseCompanyVote` stays as the guarantee
  and now announces itself (`GD.PrintErr`) if it ever bites.
- **D-15.25 (P15.10):** a bank's locked market dial is **disabled with its reason shown, never hidden**;
  **bot ballots are left untouched** (the `shift_refused=bank_locked` trace records a real intent — an
  honest refusal beats a silent one); and the **result** line in the Last Vote Snapshot names the refusal,
  **re-derived** from the stored ballot weights rather than persisted as a new field. Build it when the
  playtest reaches a founded bank, not before (§39.16 rule 2).

### Round 5 (2026-07-29, from the crash that ended the P15.8 session — see P15.11 and `Documentation/INCIDENT_LOG.md` INC-001)

- **D-15.26 (P15.11):** **persisted world state is written atomically** — `<file>.tmp` → flush → rename over
  the target — and **its loader fails loudly**. `TryLoadSnapshot` will parse inside a `try`, log the path and
  reason on failure, and **abort initialization rather than return an empty-but-plausible snapshot**. The
  incident's world survived only because the throw happened to land before any writer; a single well-meaning
  `catch`-and-continue would have overwritten a 1,666-block chain with an empty one at the next block. The
  commit *timing* rule (§24.8) is untouched; this is the durability half it never covered (§39.16 rule 7).
- **D-15.27 (P15.11):** **invariants belong to the file, not to a function.** `RebuildJournalFromCurrentState`
  must obey every rule `Flush` obeys — rotate at `MaxJournalEntriesPerChunkFile`, and **delete the chunk
  files it supersedes**. A "rebuild from current state" path that quietly writes an un-rotated 1.13 GB
  monolith beside the chunks it duplicated is how the rotation policy was defeated for an unknown number of
  sessions.
- **D-15.28 (P15.11):** **the bet history is expendable and is being deleted, by explicit developer
  authorization.** Rationale is not merely convenience: (a) it is stats, never world state, and it is
  already in the `ResetWorldIfIncompatible` delete list; (b) per INC-001 fault F4 it has been
  **double-counting** into the lifetime totals, so what is being deleted is a record that was already wrong;
  (c) the bank-testing surface (P15.2–P15.10) reads none of it. Going forward the journal gets a **retention
  cap** — an ever-growing store with no stated policy is how this arrived.
- **D-15.29 (P15.11):** **never load an UNBOUNDED history to compute a bounded summary.** The boot load is
  not removed — it is **bounded by construction** once D-15.28's retention cap exists, which keeps every
  existing consumer semantically intact mid-playtest; if it still measures as material afterwards, the full
  load moves behind the screens that genuinely browse history and `UserStatsService._Ready()` keeps the
  cheap latest-chunk path. The proper fix — a persisted lifetime aggregate — is **deferred**, and the
  interim behaviour (totals scoped to retained history) is **stated in the UI** rather than left under a
  "lifetime" caption it no longer earns (§39.16 rule 1: no figure that lies).
- **D-15.30 (P15.11):** **`blocks-*.json` stops being written.** Nothing in the codebase reads it; it costs
  ~5 MB of I/O per mined block and its delete-all-then-rewrite was the first casualty of the interrupted
  write. If a per-month chain view is ever wanted it should be generated on demand, not maintained as a
  write-only mirror of `state.json`.
- **D-15.31 (P15.11):** **the recovery is a repair, not a reset.** `state.json` is 7 characters short of
  valid and its `PlayerChain`, financial, company, bank and FBI sections are all intact, so the world is
  restored by completing the file — **no `WorldFormatVersion` bump, no clean wipe** (the standing bump-and-
  wipe default, §39.16 rule 4, applies to *format* changes; this is a truncation, and wiping would discard a
  perfectly good 1,666-block playtest five in-game days from the first bank's auction close).

### Round 6 (2026-07-30, from the audit that closed the P15.8 run — see §10)

- **D-15.32 (P15.8):** **a phase whose exit condition depends on states the game cannot currently produce
  is suspended, not failed, and it says which one.** P15.8-G needs a bank to actually die; 3⅓ in-game years
  past FBI activation produced one `shortfall_pending` and zero seizures. More hours of the same era carry
  **no new information**, so the run stops and the placeholder tuning it was meant to inform (§9 J) stays
  openly unfinished. The generalization is §39.16 rule 2's mirror image: *rule 2 says pull a readout
  forward to where you can observe it; this says when you CANNOT observe it at all, name the missing
  precondition and stop, rather than grinding for a signal the world cannot emit.*
- **D-15.33 (P15.8/Step 16):** **the FBI thread's calibration is deferred to Step 16, and its numbers stay
  placeholders until then.** Every value in §9 J is untouched by this run's evidence, because the mechanism
  never fired. The honest reading is not "the tolerances are right" — it is "no company ever got close
  enough to test them", which is itself the finding (§10 F6). Do not tune blind.
- **D-15.34 (audit):** **a governance system whose ballots are pure functions of persisted constants is a
  constant, and the trace proves it.** `BuildBotBallot` reads only `(persisted preference, company state)`,
  both invariant between votes, so 517 votes produced ~2 outcome changes and **the player is the only
  source of variance in the entire system** (§10 F1). This is a design fault, not a tuning miss, and no
  amount of playtesting would have surfaced it as anything but "the numbers look static" — it needed the
  trace read column-wise. **Rule: when a system is meant to feel alive, assert that its output actually
  VARIES, the same way §39.16 rule 1 asserts a figure does not lie.**
- **D-15.35 (audit):** **the pause is the game's most expensive interaction and it must be spent on
  decisions that can change something.** 93 of 517 votes froze the whole simulation awaiting the player, to
  produce ~2 different outcomes. Step 16 gates the freeze on **pivotality** and gives every holding a
  **standing policy** that auto-casts otherwise. Recorded here because the cost was created by plan15's own
  vote kinds (the shortfall vote adds a fourth) and because the frequency scales with how *successful* the
  player's holdings are — success currently buys friction.
- **D-15.36 (audit):** **a subsystem's own plumbing must not consume the shared budget it depends on.**
  Bot dividend auto-claims run at **8.66 transactions per block** against an ND.4a historical budget of
  **~5** and 23 usable block slots, so `owed = max(0, target − pending)` has been **structurally zero** for
  most of the run — the historical transaction-shape simulation is drowned by the companies' own dividend
  traffic, and the cast sell-flow that funds those companies is starved by it. ND.10e batched the claims'
  *fees*; nothing ever bounded their *count*. Fix belongs to Step 16 (settle bot claims internally or per
  company at quarter close, not per holder on-chain).
- **D-15.37 (audit):** **the R2 regulator is confirmed correct and the frame-rate complaint is a separate
  problem with a different shape.** Mean solvetime **62,373 s vs the 58,500 s target (+6.6%)** across 1,472
  blocks closes the Round-2 question. The retention throttle averages **0.713 with no monotone decay across
  1,500 blocks**, which *contradicts* a purely chain-length-linear cost and points at chronic saturation
  plus periodic per-block spikes. T4.6 (instrument first) is therefore the right next move and T4.2's
  urgency is mildly demoted — see `Documentation/PRIVATE_ROADMAP.md` §8 T4, updated with this evidence.

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
- **P15.8 — Calibration playtest. ⏸ SUSPENDED 2026-07-30 (A–F ✅ verified, G unreachable).** One DEV
  entry-year run (temporarily set `DevEntryYear`, restore to 0 before merge) to verify conversions route
  through banks, debts accrue/repay, the carry behaves across a BTC bull/bear era, and the monetary
  invariant holds. Ran 2010-03-21 → ~Oct 2014 and confirmed all of that; **stopped at section G**, which
  needs stress states the current game cannot produce (D-15.32). Full audit + resume conditions: **§10**.
- **P15.9 — Bot ballots must respect the company's currency band** *(found during P15.8)*. Bots cast their
  raw global band stance (0/25/50/75/100) regardless of the company's charter, so a CB1 company's vote is
  averaged over illegal ballots and pins to its floor every quarter while the player is held to `[75,100]`.
  Project each bot's stance into the company's band instead of clamping it (D-15.24, default-anchored).
  **→ §8; ✅ IMPLEMENTED 2026-07-27.**
- **P15.10 — The market-shift dial a bank's shareholders cannot move** *(from P15.9 question 5)*. A bank's
  category is locked (D-15.12), so its NST holders are offered a Market-direction control whose every
  option is refused. Present it honestly rather than silently. **→ §8; ✅ IMPLEMENTED 2026-07-29** — picked
  up at its trigger (First Satoshi Savings' first quarterly, 2012-12-28), with one correction to the spec:
  the refusal re-derivation must be gated on `Kind == "quarterly"`, because bots fill `MarketShift` on every
  vote kind while only a quarterly ever evaluates it.
- **P15.11 — Persistence survivability & the bet-journal blowup** *(from INC-001, the crash that ended the
  P15.8 session on 2026-07-29)*. A force-close during a block commit left `state.json` truncated and the
  world silently unloadable; the underlying cause was a 1.13 GB bet journal whose rotation policy its own
  rebuild path defeated. Repair the world, wipe the (already double-counted) history under explicit
  authorization, make the snapshot write atomic and its loader loud, and cap what accumulates.
  **→ §8; ✅ IMPLEMENTED 2026-07-29 (a–e) — world recovered, P15.8 unblocked; f is the launch verification.**

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

### P15.5 — Dissolution, closed-companies list & seized wallets — ✅ IMPLEMENTED (2026-07-26, subphases a–d)

> **Build log.** `dotnet build` clean. `NetworkRoot.cs`: `CompanyClosure` + `_closedCompanies` + snapshot
> capture/restore; `ClosureReason*` constants + `IsCompanyClosed`/`GetCompanyClosure`/`GetClosedCompanies`;
> `DissolveCompany`, `TryDissolveInsolventBanks`, `TryAssignSeizedWallets`, `SweepClosedCompanyInflows` +
> `SeizedInflowMemo`, all three hooked at the end of `TickCompanyGovernance` (whose early-out widened).
> `BlockExplorer.cs`: `BuildClosedCompanyRow` + the closed branch in the Founded list. `CompanyDetails.cs`:
> `ShowClosureNotice`. `CentralBank.cs`: the Closed-companies & recovery block. Docs: `CLAUDE.md` +
> `ProjectDesignManual.md` **§39.12** (with §39.12.1–.3).
>
> Four decisions taken inside the subphases, all documented in §39.12:
> - **Custody is implemented as "the coins do not move".** The FED is SC-only — no node, no keys, no
>   address — but every satoshi must live at a real address. So a closure leaves the wallet on-chain,
>   unspendable, still receiving its scheduled inflows; that state IS D-15.18's "held 100% as BTC,
>   custodial". No FED address, no synthetic transfer, no new identity file. Its leftover **SC** does move
>   (it is a plain number the FED can be repaid with) and is burned against the debt.
> - **Closure DELETES the founding + governance entries** rather than flagging them. That makes "holders
>   lose their tokens and future payments" literal and lets every live loop skip the dead company for free.
> - **The BlockExplorer leak had to be handled explicitly:** the Founded list is driven by the chain-derived
>   auction ledger, not by `_companyFoundings`, and a resolved auction stays `Resolved` forever — so a
>   dissolved company kept appearing with a null founding, which previously meant "not founded yet".
>   Verified that **re-founding is impossible** (`TrySettleResolvedAuctions` only fires on an
>   `InAuction → Resolved` flip, so a long-closed company never flips again).
> - **The recovery tracker + Closed-Companies list went into the FED scene**, not WorldEconomy (P15.7b's
>   nominal home) — the FED is the absorber, and the banking-layer readout already lives there. P15.7b can
>   mirror or move it.
>
> **Cross-cutting conventions.** Every judgement call from P15.1–P15.5 is written up in the manual beside
> the mechanism it belongs to, and the six that recur are collected as standing rules in
> `ProjectDesignManual.md` **§39.16** (was §39.15 until P15.9 took that number) — read that before
> starting a new phase.
>
> **Left for the developer's P15.8 verification:** an unrecoverable bank closes and appears in the list with
> reason `debt_default`; a scheduled inflow to a dead company lands at its heir and is tracked; a
> black-category closure is inherited by the solvent Black bank while an unmatched one stays in FED
> custody as BTC; and a player PST holder in a closed company loses the holding and sees the closure notice.

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

### P15.6 — FBI investigation / seizure — ✅ IMPLEMENTED (2026-07-26, subphases a–d)

> **Build log.** `dotnet build` clean. `NetworkRoot.cs`: `FbiActivationLocal` + `FbiToleranceMultiplier` +
> the meter/roll constants; `ScInflowCurrentQuarterSc`/`ScInflowLastQuarterSc`/`InvestigationScore` on
> `CompanyGovernanceState` (throughput accrued at the conversion credit, rolled at quarter close);
> `FbiToleranceScFor`/`FbiOverageRatio`/`FbiDarkness`; `TickFbiInvestigations` (hooked before the P15.5
> dissolution sweep); the seizure branch in `DissolveCompany`; `_fbiActivated`/`_fbiScFunds` + snapshot
> fields; `GetFbiInvestigationFiles` + `GetFbiInvestigationWarning`. `CompanyDetails.cs`: the risk line.
> `CentralBank.cs`: the Federal-investigations board. Docs: `CLAUDE.md` + `ProjectDesignManual.md`
> **§39.13** (with §39.13.1–.4); the standing-conventions section renumbered to **§39.15** (P15.7 later took §39.14).
>
> Four decisions taken inside the subphases, all documented in §39.13:
> - **`T` is accrued at the single conversion-credit site** — the one place SC ever enters a company — and
>   rolled current→last at quarter close; effective `T` = max(last, current) so a company that has just
>   started converting is not judged against a stale zero.
> - **`T = 0` with SC on hand sits at the overage cap deliberately.** That is the intended reading of
>   "unexplained wealth" (converted heavily three quarters ago, sat on the pile since), not an edge case.
> - **Priority is one ORDERING, not a second pass:** flagged targets sort banks-last then by overage, and
>   only the FIRST is rolled each block. D-15.19's rule and a natural one-raid-per-block throttle fall out
>   of the same line.
> - **The initial grant is a FED loan on client `"fbi"`, not conjured SC** — otherwise it would mint outside
>   `circulation = grants + debt`. Seized SC is a plain transfer (invariant untouched), which is why
>   `DissolveCompany` branches on reason: seizure → the FBI's budget, debt default → burn against the loan.
>   Seized BTC is not moved at all; it flows into P15.5's custody chain unchanged.
>
> **Placeholders for P15.8** (all of them): the four tolerance multipliers, `T`'s window, the meter's
> gain/decay/overage-cap, the roll's base + cap, and `FbiInitialGrantSc`.
>
> **Left for the developer's P15.8 verification:** zero FBI activity before 14 Jun 2011; a black-market
> SC-hoarder accrues and flags while an official company never does; a bank is only reached after the
> non-banks are cleared; a sustained-flagged company is eventually seized with its SC landing in the FBI
> budget and its wallet in federal custody; and the `CompanyDetails` warning appears while over tolerance
> and clears on de-escalation.

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

### P15.7 — Surfacing & telemetry — ✅ IMPLEMENTED (2026-07-26; most of it shipped early with P15.2–P15.6)

> **Build log.** `dotnet build` clean. **Already shipped early** (each with the mechanism it observes, per
> §39.15 rule 2): the FED scene's Banking-layer block + financier preview (P15.7a, at P15.2),
> `bank_credit_trace.csv` (P15.7d, at P15.3), the shortfall ballot control (part of P15.7c, at P15.4e), and
> the Closed-Companies list + recovery tracker (P15.7b/c, at P15.5) and Federal-investigations board (at
> P15.6). **Added here:** the layer-1 per-client sub-ledger under each bank in `CentralBank.cs`; the
> banking-layer aggregate in `WorldEconomy.cs` (`AppendBankingLayer` — per-bank strip + system solvency
> line + a closures pointer); and `NetworkRoot.GetBankLendingSummary` + `CompanyDetails.BuildBankLendingPanel`
> (the player-facing lending book). Docs: `CLAUDE.md` + `ProjectDesignManual.md` **§39.14**; the
> standing-conventions section renumbered §39.14 → **§39.15** so it stays last.
>
> One decision, documented in §39.14:
> - **WorldEconomy got the AGGREGATE only, not copies.** The phase map nominally assigns the
>   Closed-Companies list and the recovery tracker here, but they already live in the Central Bank scene —
>   which is the FED's own page, and the FED is the creditor, absorber and custodian. WorldEconomy takes the
>   macro question (Σ collateral value vs Σ FED debt, per-bank strip with under-collateralized / shortfall /
>   insolvent flags) plus a **one-line pointer** to the detail. Duplicating whole panels would mean two
>   places to keep in step — §39.15 rule 6 applied at panel granularity.
>
> **Left for the developer's P15.8 verification:** the lending panel's installment matching what the
> quarterly repayment actually charges; the solvency line flipping as BTC moves; and the sub-ledger rows
> appearing as a bank finances companies.

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

### P15.8 — Calibration playtest — ⏸ SUSPENDED (2026-07-30; sections A–F ✅ verified, G unreachable)

> **How this phase ended.** The run reached **~Oct 2014 (block 2699)** with 30 companies founded, 2 banks
> live, the FBI active since 2011-06-14, and **zero errors, zero exceptions and zero P15.9 tripwires** in a
> 4.4 MB log. Checklist sections **A–F all pass**. Section **G never became reachable** and cannot be
> reached by playing longer (D-15.32), so the run was stopped deliberately rather than continued for a
> signal the world cannot emit. **§10 is the audit** — six findings with the trace numbers behind them, what
> is verified, what is left, and the exact conditions for picking this up again after Step 16.
>
> **⚠ ACTIVE DEV SETTING (2026-07-26): `TimelineConfig.DevEntryYear = 2010`.** The first launch after this
> change **wipes the world automatically** (the Tag becomes `CANON-2009-01-03+ENTRY-2010`, so
> `ResetWorldIfIncompatible`'s existing guard fires) and the bootstrap fast-builds real history from
> 2009-03-21 to land the player on **21 Mar 2010**. A `[ENTRY-2010 DEV]` StatusBar watermark is shown for
> as long as this is non-zero (added here, mirroring the TL.2 alt-timeline watermark rule — an entry-year
> world is canon-COMPATIBLE and therefore *easier* to mistake for a real playthrough).
> **MUST be restored to `0` before merging to `main`.**
>
> **What 2010-03-21 does and does not exercise.** It lands ~4 months BEFORE Market Birth (2010-07-18) and
> ~2.5 years before the first bank founds (First Satoshi Savings, 2012-09-03). At the landing instant there
> is therefore no BTC price, no fees, no auctions, no companies and no banks — **the entire plan15 banking
> layer is dormant** and the Central Bank scene will correctly read "No bank company has founded yet."
> This is the right setting for watching the whole economy unfold from before the market exists; it is NOT
> the setting for calibrating the plan15 placeholders, which needs the world to reach 2013+ (either by
> playing forward from here, or by a separate run at `DevEntryYear = 2013`+).

- One **DEV entry-year** run (temporarily set `TimelineConfig.DevEntryYear` to a chosen year — restore to
  `0` before merge, the step14 lesson): verify conversions route through banks, FED debts accrue and repay,
  the BTC carry behaves across **a bull and a bear era**, shortfall votes + dissolutions + FBI seizures
  fire sensibly, and `circulation = grants + debt` holds throughout. **Tune the placeholders**: the FBI
  tolerance multipliers + `T` window, and the quarterly repayment fraction. Restore `DevEntryYear = 0`;
  final clean build; hand to the developer for in-game verification + commit. **(Never headless-launch the
  real save — dotnet build + developer verification, per the standing rule.)**

**Exit:** plan15 behaves across eras with the invariant intact; ready to merge to `main` (restore
`DevEntryYear = 0` first).

**Exit as actually taken (2026-07-30).** The invariant held throughout, conversions/repayments/foundings
all routed correctly, and the step merged to `main` — but with **two of the three exit clauses met**: the
bear era arrived (BTC 2013-12 → 2014-10 is a real drawdown in the dataset) and the carry survived it
without ever producing an insolvency, so the *stress* half of "behaves across eras" is untested rather than
passed. **Left open on purpose, tracked as P15.8-G in §10.4.**

### P15.9 — Bot ballots must respect the company's currency band — ✅ IMPLEMENTED (2026-07-27, found at P15.8)

> **Build log.** `dotnet build` clean, 0 warnings. All five questions in §P15.9.5 answered by the developer
> the same day: **Option C** (default-anchored projection, D-15.24) · rounding **nearest, .5 up**
> (`MidpointRounding.AwayFromZero` — `87.4 → 87`, `87.5 → 88`) · both suggestions accepted (band range in
> the ballot-list header, clamp tripwire) · and the market-shift review taken as its own phase (**P15.10**).
> Shipped: `NetworkRoot.ProjectStanceIntoBand` + its one call site in `BuildBotBallot`; the `CloseCompanyVote`
> tripwire; the reworked `PrintBotGovernanceStances` line; `CompanyDetails`' ballot-list header. Docs:
> `CLAUDE.md` + `ProjectDesignManual.md` **§39.15**, standing conventions renumbered §39.15 → **§39.16** so
> they stay last.

> **The finding (developer, P15.8 run, Papa's Pizzeria's first quarterly vote).** The Board Vote panel
> offers the player a reserve dial bounded to the company's band — Papa's Pizzeria is **CB1**, so the
> SpinBox is `[75, 100]` — yet the bot ballots listed beside it read **0%**, **50%**, etc. The player is
> held to the band; the bots are not. That is not a cosmetic mismatch: those out-of-band ballots enter the
> weighted average as real numbers.

#### P15.9.0 — Diagnosis (confirmed by inspection, no repro needed)

`NetworkRoot.BuildBotBallot` fills the reserve dial from the bot's **own** band preference, with no
reference to the company being voted on:

```csharp
ReserveScPercentTarget = BandDefaultScPercent(pref?.CurrencyBandPreference ?? gov.CurrencyBand),
```

`BandDefaultScPercent` returns the bot's global stance — CB1 100 · CB2 75 · CB3 50 · CB4 25 · CB5 0 — so a
CB5 bot voting at a CB1 company casts a literal `0`, twenty-five points below anything that company's
charter permits. The player's ballot, by contrast, is band-bounded twice: the SpinBox
(`CompanyDetails.cs:571-575`) and `TryRegisterPlayerVote`'s `Math.Clamp(..., min, max)`
(`NetworkRoot.cs:3719-3722`).

**The consequence is worse than the display.** `CloseCompanyVote` clamps only the *final weighted average*
to `BandScPercentBounds` (`NetworkRoot.cs:3380-3381`). With the four bots drawn as a permutation of the
five bands, a CB1 company typically carries two or three sub-75 ballots, the average lands below the floor,
and the clamp pins the result to **exactly 75 every single quarter**. The vote produces a constant. The
player's ballot — the one input the game *pauses* to collect (D-ND8.18) — cannot move the outcome, because
it is being averaged against values the rules forbid. This is a §39.16 rule 1 violation: a displayed figure
(the cast ballot) that does not correspond to a legal state of the thing it describes.

Scope: `BuildBotBallot` is shared by **all** vote kinds, so founding, quarterly and >30%-special votes are
all affected. The market-shift, payout and shortfall dials are **not** affected — their bot values are
already inside their own clamps (`{-1,0,1}`, `[0, 2× default]`, `[0,100]`).

#### P15.9.1 — The rule

A bot's band preference is a position on a global **"SC-ness" axis**, not a literal target. A ballot must
express that same position **inside whatever range the company's charter allows** — the band is the
company's identity (fixed at founding, never voted on), the ballot is a tuning dial within it.

So: **project, don't clamp.** Clamping (`Math.Clamp(pref, min, max)`) would collapse the CB5, CB4 and CB3
bots onto exactly 75 in a CB1 company — three identical ballots, and a result still pinned near the floor.
Projection preserves the full five-way spread in every band, which is what makes the player's vote matter.

**The projection is default-anchored (D-15.24, Option C).** The bot's OWN band default maps to the
COMPANY's band default, interpolating linearly on each side:

```
stance ≤ companyDefault :  min        + (stance / companyDefault)               × (companyDefault − min)
stance ≥ companyDefault :  companyDefault + ((stance − companyDefault) / (100 − companyDefault)) × (max − companyDefault)
```

This is the **identity when the two bands agree** — a CB2 bot at a CB2 company votes CB2's own 75 — which
plain `[0,100] → [min,max]` interpolation does not give for the asymmetric bands CB2/CB4, whose default does
not sit at the centre of their own range. The two anchors that sit ON a bound (CB1's 100, CB5's 0) leave one
side degenerate; the guards route the whole stance through the side that exists, and there the two options
coincide. Result rounded to a whole percent — **nearest, `.5` away from zero** (`87.4 → 87`, `87.5 → 88`) —
then `Math.Clamp`ed to the band as a guard for the day a bound stops being an integer.

| Bot stance | Pulls toward | CB1 `[75,100]` | CB2 `[50,100]` | CB3 `[25,75]` | CB4 `[0,50]` | CB5 `[0,25]` |
|---|---|---|---|---|---|---|
| CB5 — `0%` | all-BTC extreme | **75** | 50 | 25 | 0 | *0* |
| CB4 — `25%` | | 81 | 58 | 38 | *25* | 6 |
| CB3 — `50%` | the middle | **88** | 67 | *50* | 33 | 13 |
| CB2 — `75%` | | 94 | *75* | 63 | 42 | 19 |
| CB1 — `100%` | all-SC extreme | ***100*** | 100 | 75 | 50 | 25 |

*Italics = the anchor cell, where the bot's band matches the company's and the projection is the identity.*
Both figures the developer named land exactly: `0% → 75`, `50% → 87.5 → 88`.

#### P15.9.2 — Subphases

- **P15.9a — The projection helper. ✅** `NetworkRoot.ProjectStanceIntoBand(decimal stanceScPercent,
  string companyBand)`, a pure static beside `BandScPercentBounds` implementing the anchored map above.
  Public/static so the DEV printout and any future UI read the **same** helper the ballot does (§39.16
  rule 6). One call site — `BuildBotBallot`'s `ReserveScPercentTarget`, now
  `ProjectStanceIntoBand(BandDefaultScPercent(pref?.CurrencyBandPreference ?? gov.CurrencyBand), gov.CurrencyBand)`.
- **P15.9b — The DEV stance printout. ✅** `PrintBotGovernanceStances` printed `targets 50% SC` — after
  P15.9a that is no ballot any bot will ever cast. The band column is now labelled a **global SC stance**
  and each line spells out what it votes in all five bands
  (`global SC stance 50% → votes CB1 88 · CB2 67 · CB3 50 · CB4 33 · CB5 13`), computed through
  `ProjectStanceIntoBand` itself so the printout cannot drift from the ballots it exists to be read
  against (§39.16 rule 6).
- **P15.9c — The clamp tripwire (accepted suggestion 4). ✅** `CloseCompanyVote` now `GD.PrintErr`s when the
  raw weighted average falls outside the band and the clamp actually bites, naming the company, vote kind,
  raw vs. clamped value and the band. Post-P15.9 it should never fire; if it does, some new ballot source
  is bypassing the projection. This exact failure hid for a whole plan precisely because nothing announced
  it — the clamp silently absorbed it.
- **P15.9d — Ballot-list header (accepted suggestion 3). ✅** `CompanyDetails`' Last Vote Snapshot now reads
  `Ballots cast (band CB1: 75–100% SC):`. That readout is what surfaced this bug, and a bare
  `voted: reserve 0%` required the reader to remember the company's band; with the range stated an illegal
  value is obvious on sight.
- **P15.9e — Verification (developer, in-game).** Open Papa's Pizzeria's next quarterly vote: every listed
  ballot reads a value inside `75–100`; the result is no longer pinned at exactly 75; moving the player's
  own dial visibly moves the outcome; no tripwire line in the log. Second check in a non-CB1 company (a
  CB3/CB4 roster company) that the spread also lands inside *its* band. Also worth one glance at the
  `[Governance]` stance printout on launch — the five projections per bot should match the table above.
- **P15.9f — The OPEN vote's ballots, in the Board Vote panel. ✅** (2026-07-27, from the first live
  verification — see §P15.9.6.) The only ballot list in the scene was the Last Vote Snapshot, which shows a
  **closed** vote: always one quarter too late to inform the ballot being cast. Bots cast the instant a vote
  opens, so at the moment the game **pauses** and asks the player to vote, every other ballot is already
  known and persisted — and was being hidden. Added to `BuildBoardVotePanel`: (a) `BuildOpenVoteBallotList`
  — one row per NST holder with the resolver's own weight and either its cast ballot (kind-aware: reserve /
  market / payout, or the shortfall split) or *not voted yet*; and (b) a live **"if the vote closed now"**
  line under the reserve dial, recomputed on every turn of the SpinBox. Both the preview and
  `CloseCompanyVote` now resolve through ONE new pure static, `NetworkRoot.ComputeReserveVoteOutcome`
  (§39.16 rule 6 — a preview is a *promise about what the resolver will do*, the sharpest case for that
  rule: two implementations of the same weighted average would drift the first time either changed, and the
  player would be deciding on the stale one). Display-only; no persisted state, no bump. Layout re-checked
  against Ch. 29 — `ActionVBox` already lives inside the bounded `ContentScroll` with the Back button as a
  footer sibling, so the added rows extend the scroll and clip nothing.

#### P15.9.3 — Explicitly NOT changed

- **The player's path.** Already correct at both ends (SpinBox + `TryRegisterPlayerVote` clamp).
- **The final clamp in `CloseCompanyVote`.** It stays — with every ballot now in-band the average is in-band
  by construction and the clamp becomes a no-op, but it is the guard that makes that guarantee, not a
  redundancy to delete.
- **Persisted state / `WorldFormatVersion`.** No new field, no changed meaning of an existing one. Old
  `VoteBallotRecord` rows in `VoteHistory` keep their out-of-band values — that is honest history of how
  the vote actually ran, not data to migrate (and the P15.8 world gets wiped on the next entry-year change
  anyway). **No bump.**
- **The band itself.** `gov.CurrencyBand` is set once at founding and never voted on. That is the premise
  this fix rests on, and it stays.

#### P15.9.4 — Expected behaviour change (not a regression)

CB1 company reserve results will **rise off the 75 floor** and start varying quarter to quarter. That is
the fix working: today's constant is the clamp swallowing illegal ballots. Every band is affected the same
way — outcomes move from "pinned at whichever bound the illegal ballots dragged them to" toward a genuine
weighted average of five legal positions. The **banks are all CB1** (Appendix A), so their SC/BTC reserve
mix is exactly where this will show up first and most — worth watching against P15.3's conversion volumes
during the rest of the P15.8 run.

#### P15.9.5 — Questions & suggestions — ALL ANSWERED (developer, 2026-07-27)

1. **The asymmetric-band wrinkle — pure linear (A) or default-anchored (C)? → C (D-15.24).** Pure linear
   had one oddity: a **CB2 bot voting at a CB2 company** would cast `88`, not the `75` that *is* its own
   stated stance, because CB2's default sits below the centre of CB2's range `[50,100]` (same for CB4;
   CB1/CB3/CB5 are unaffected, their default sits at a bound or at the centre). C pins the bot's own band
   default to the company's band default and interpolates on each side, so "a bot in a company that shares
   its band votes that band's default" is true everywhere. Both options produce the **identical CB1
   column**, so the two figures the developer named were never in question.
2. **Rounding → nearest, `.5` up** (`MidpointRounding.AwayFromZero`): `87.4 → 87`, `87.5 → 88`. Under
   Option C this changes one cell against the originally-proposed ceiling (CB4 stance at a CB1 company:
   `81.25` → **81**, not 82); every other value lands exactly or on a `.5`, which still rounds up.
3. **Band range in the ballot-list header → accepted** (shipped as P15.9d).
4. **Clamp tripwire → accepted** (shipped as P15.9c).
5. **Parallel review of the market-shift ballot → yes, as its own phase (P15.10).** It is *not* out of
   range (bots vote `Math.Sign` of a category difference, always `{-1,0,1}`), but it has the structurally
   similar shape this phase is about: a bank's shift is voted and then refused
   (`shift_refused=bank_locked`, D-15.12), so bank shareholders — the player among them — cast a dial that
   cannot move anything. Deliberate and traced, but the same "don't offer a dial that cannot act"
   principle applies. Scoped separately below.

#### P15.9.6 — First live verification (2026-07-27): the tripwire caught its own transition

The developer reported the reserve **still reading 75%** at Papa's Pizzeria one quarter after the fix.
Audited against the live save (`blockchain/state.json`) and `godot.log` — **the fix was working, and the
tripwire had already explained it**:

> `[Governance] P15.9 tripwire — Papa's Pizzeria (non_miner_2) (quarterly) cast an OUT-OF-BAND reserve
> average 37.55%, clamped to 75% (band CB1: 75–100). A ballot source is bypassing ProjectStanceIntoBand.`

**Ballots are cast when a vote OPENS, not when it closes**, and an open vote's `Ballots` dictionary is
persisted in `BlockchainStateSnapshot`. The quarterly that closed had opened at block 890 — *before* the
rebuild — so closing it merely averaged pre-fix raw ballots: `0.4235×0 + 0.4021×50 + 0.1745×100 = 37.55%`,
clamped back up to the 75 floor. The **currently open** vote (block 939), opened post-fix, carries
`bot_1 → 75` (CB5 stance) and `bot_3 → 88` (CB3 stance) — exactly the projection table — so the next close
lands at `67.14 + 0.1745 × (player ballot)`, i.e. **80.2–84.6%**: off the floor for the first time, with the
player's 17.45% weight moving it ~4.4 points. ArtForz Cluster (CB5) showed the identical transitional
signature (raw 37.30% → clamped 25%).

**Two conclusions worth keeping.** (1) A rebuild mid-playtest leaves any *already-open* vote carrying
pre-fix ballots — expected, self-clearing at the next open, and not a reason to wipe. (2) The suggestion-4
tripwire paid for itself on its first day: it named the company, the raw average and the band, which is what
turned "the fix looks broken" into a five-minute audit with an exact arithmetic answer. **A guard that
silently absorbs illegal input is indistinguishable from a guard that is never needed.**

The same session surfaced P15.9f above: the developer had spotted the original bug in the Last Vote
Snapshot's ballot list, then found no equivalent for the vote actually in front of them — because that list
only ever showed *closed* votes.

**Exit:** every ballot cast at a company — bot or player — lies inside that company's currency band; the
reserve result varies within the band instead of pinning to a bound; the DEV stance printout describes the
numbers that actually appear; a voter can see every ballot already cast in the vote they are being asked to
decide, and what their own dial does to it; `dotnet build` clean; developer-verified in-game on a CB1 and a
non-CB1 company.

### P15.10 — The market-shift dial a bank's shareholders cannot move — ✅ IMPLEMENTED (2026-07-29)

> **Picked up at its trigger, as designed.** First Satoshi Savings founded `2012-09-27`, and its **first
> quarterly vote opened `2012-12-28` (block 1800, `awaiting_player`)** — the phase was built with the game
> paused inside that very vote, which cost no playtest progress (the clock is frozen while awaiting a
> ballot, and the open vote rides `BlockchainStateSnapshot`, committed at block 1800 before
> `PersistStateToDisk`, so the rebuild's restart resumed into the identical pause).
>
> **The founding vote was NOT a missed opportunity** (checked before building): `company_governance_trace.csv`
> shows `first_satoshi_savings,vote_close,founding,...,shift=0` and **zero** `shift_refused` rows in the whole
> file. A founding vote neither renders the dial (`CompanyDetails` gates the row on `quarterly`) nor evaluates
> a shift (`CloseCompanyVote` gates on `vote.Kind == CompanyVoteKindQuarterly`), so there was nothing to
> refuse and nothing to label.
>
> **One correction to P15.10b's spec, found while building — see the Kind gate note in P15.10b below.**

#### P15.10.0 — When to start (the trigger)

**The first bank founds `2012-09-03`** — First Satoshi Savings (Appendix A). Two conditions, and only the
first is guaranteed by the calendar:

1. **A bank has founded.** Check in BlockExplorer → Enroll Mode → the Founded list, or the CB scene's
   Banking layer (it stops saying "No bank company has founded yet"). The roster date is when the company
   is *introduced*; founding needs its auction to actually resolve, so it can land later.
2. **You hold NST in it** — otherwise the Board Vote panel never renders and there is nothing to look at.
   That means bidding into that bank's auction and finishing in a **top-3 tracked tier** (§22.15's gold
   projection on the `AuctioningCompanyDetails` frame tells you live whether you are on track). **If you
   miss it, do not restart anything** — the next bank (Digital Reserve Trust, 2013-06-17) works identically,
   and the P15.10c half below is visible to *any* viewer regardless of holding.

**Before touching code, look at the thing first** (this is the observation that makes the phase worth
doing): open the bank's quarterly vote and note that the Market-direction dropdown offers three options, all
of which are counted and then thrown away, and that the Last Vote Snapshot's `Market level:` line shows the
category unchanged with no explanation of why.

#### P15.10.1 — What is wrong (and what is deliberately NOT wrong)

A bank's market category is **LOCKED** (D-15.12): `CloseCompanyVote` counts the shift vote, then refuses to
apply it and traces `shift_refused=bank_locked`. So a bank's NST holders — including the player, whose vote
*pauses the entire simulation* — are offered a dial whose every option is a no-op.

Same family as P15.9, one axis over, but a **weaker** case and it must not be over-fixed: nothing illegal is
stored, no number lies, and the lock itself is load-bearing (a drifting bank silently re-shapes the §5.1
selection distance every other company banks on). **The values are correct; only the presentation is
dishonest.** So this phase changes what is *shown*, and deliberately changes no mechanism.

#### P15.10.2 — Decisions (D-15.25, locked 2026-07-27 — developer accepted all three recommendations)

1. **Disable + explain, never hide.** Hiding the row invites "why does this company have fewer controls than
   the others?", and the reason — this is a bank, its category is what other companies' financier selection
   is measured against — is the *interesting* part, worth teaching rather than concealing.
2. **Bot ballots are NOT changed.** Making bots cast `MarketShift = 0` at banks was the obvious symmetric
   move and it is **rejected**: the current behaviour deliberately records "a rejected attempt rather than
   pretending nobody asked" (the D-15.12 comment), and that intent is real signal in the trace and the
   ballot list. Erasing it would trade an honest refusal for a silent one.
3. **Label the RESULT instead.** The unexplained non-event in the Last Vote Snapshot is the actual defect;
   naming the refusal there fixes it without touching a single ballot.

#### P15.10.3 — Subphases

- **P15.10a — `CompanyDetails` vote panel.** In `BuildVotePanel`'s `if (quarterly)` market block
  (`Screens/CompanyDetails/CompanyDetails.cs`, the `_marketOption` row): when
  `NetworkRoot.IsBankCompany(gov.NonMinerNodeId)` — the public test already exists — keep the row, set
  `_marketOption.Disabled = true`, leave it on `Select(1)` ("Hold the current category"), and add a short
  reason line beneath it, e.g. *"Category locked — a bank's category is fixed at its roster default, because
  it is the distance other companies' financier selection is measured on (D-15.12)."*
  **Gotcha:** do NOT simply skip creating `_marketOption`. `OnSubmitBallot` reads
  `(_marketOption?.Selected ?? 1) - 1`, so a null field is *safe* (it yields shift 0) — but the field
  survives panel rebuilds, so if you ever take the hide-it route, null it explicitly in that branch. With
  the disabled control kept on index 1 the submitted shift is 0 by construction, which is what you want.
- **P15.10b — The Last Vote Snapshot refusal line.** In `BuildLastVoteSnapshot`, beside the
  `Market level: X → Y` line, append a refusal note when this is a bank and the closed vote actually carried
  a shift attempt. **Re-derive, do not persist:** `VoteBallotRecord` already stores `Weight` and
  `MarketShift`, so the same test `CloseCompanyVote` ran is reproducible from the record —
  `Σ weight where MarketShift > 0 ≥ 0.60m` (darker) or the same for `< 0` (lighter), against
  `MarketShiftSupermajorityFraction`. A new persisted `MarketShiftRefused` bool would be the tempting
  alternative and is **worse**: it defaults to `false` on every pre-existing record, which reads as "no
  refusal happened" on exactly the historical bank votes where one did (§39.16 rule 5's silent-failure
  shape), for a value that was derivable all along (rule 4). No new field ⇒ **no `WorldFormatVersion`
  bump**. If a supermajority was reached: *"market shift refused — category locked (bank)"*; if the vote
  simply did not reach 60%, the existing line already tells the true story and needs nothing.
  **⚠ CORRECTION found while building (2026-07-29) — the re-derivation MUST be gated on
  `rec.Kind == "quarterly"`.** The spec above describes only the weight test, and that test alone is wrong:
  `BuildBotBallot` has no kind parameter and fills `MarketShift` for **every** vote kind, while
  `CloseCompanyVote` only evaluates a shift inside `if (vote.Kind == CompanyVoteKindQuarterly)`. So a
  **founding** or **special** record can carry a ≥60% supermajority of `+1` ballots that nothing ever
  considered — and the ungated test would have printed "market shift refused" on a vote where no shift was
  ever on the table, replacing the old silence with a new falsehood. Implemented as
  `NetworkRoot.WasMarketShiftRefused(rec, nodeId)` (kind gate + bank gate + the weights), sharing the
  supermajority verdict with `CloseCompanyVote` through a new pure `ResolveMarketShift(darker, lighter)` —
  the same §39.16 rule 6 shape P15.9f used for `ComputeReserveVoteOutcome`, so the note and the resolution
  cannot drift apart.
- **P15.10c — Verification.** As an NST holder in a founded bank: the market row is present but greyed with
  its reason; submitting a ballot still works and the vote closes normally; the snapshot names the refusal
  when the bots did push a shift, and stays quiet when they did not. Then open **any non-bank** founded
  company and confirm its market dial is fully live and unchanged. `dotnet build` clean.

- **P15.10d — The immaterial shortfall (found in the same panel, 2026-07-29).** Not part of the original
  phase; it surfaced in the very screenshot taken for P15.10.0's "look at the thing first" step, on the same
  company, and would have paused the game within one in-game day. First Satoshi Savings' first quarterly
  repayment succeeded (repaid `1,721.32` = 10% of `17,213.20`) but left a **sub-cent rounding residue**, and
  `TryBankQuarterlyRepayment`'s `gap > 0m` test recorded it as a real shortfall. Since
  `TickCompanyGovernance` opens a shortfall vote on that same `> 0` test, the world was one block away from
  **pausing the entire simulation** for a board vote splitting a gap the UI renders as `⚠ Shortfall of
  0.00 SC`. Three parts: a named `MinMaterialShortfallSc = 0.01m` (one cent — the point at which the figure
  becomes visible in the N2 readouts that report it, so "if the game cannot display it, it cannot be worth a
  vote over"; a *P15.8 calibration knob*); a `shortfall_dust` trace row for the sub-threshold case, so the
  cutoff's own frequency is observable rather than silently discarded; and a **self-heal on snapshot
  restore** clearing a pre-fix residue, because tightening the writer only stops NEW dust while the trigger
  reads the PERSISTED field — the same shape as the P15.2b category re-derivation directly above it in
  `TryLoadSnapshot`, and equally free of a format bump. Monetarily safe by construction: `Repay()` burns
  `raisedSc` and never the installment, so the un-repaid residue simply stays outstanding FED debt and is
  chipped at next quarter — no figure diverges from reality (§39.16 rule 1).

**Exit:** no company offers its shareholders a governance control that cannot change anything, and where a
vote is structurally refused the UI says so instead of leaving it silent — with the bots' attempts, and the
`shift_refused=bank_locked` trace, left exactly as they are. No board vote — and therefore no simulation
pause — is ever opened over a quantity the game cannot display.

---

### P15.11 — Persistence survivability & the bet-journal blowup — ✅ IMPLEMENTED (2026-07-29, subphases a–e; f = developer verification)

> **World recovered 2026-07-29.** P15.11a executed: backup at
> `%APPDATA%\Godot\GamblingMiner_backup_INC001_2026-07-29\` (1.43 GB — the 115 history files were **moved**
> into it, not copied, so the archive cost nothing beyond what was already on disk); `state.json` repaired
> `9,256,960 → 9,256,967` bytes and **validated with a real JSON parser**: 1,666 blocks, tip
> `2012-09-22 18:08:27` UTC, 20 `CompanyFoundings` + 20 `CompanyGovernance`, 4 bot stances,
> `FbiActivated = true`, `BankState` empty (correct — First Satoshi Savings was still in auction). Save
> directory **1.43 GB → 9 MB**. b–e are built and `dotnet build` is clean with 0 warnings; **f is the
> developer's launch check.**
>
> The forensic record — evidence, log lines, exact byte counts — is
> `Documentation/INCIDENT_LOG.md` **INC-001**; the design statement is `Documentation/ProjectDesignManual.md`
> **Chapter 40**. This section is the executable part.

#### P15.11.0 — What happened, in three sentences

At ~9000X, five in-game days before **First Satoshi Savings** (the first bank, and the entire point of the
P15.8 run) closed its auction with the player leading, the app stopped responding on a scene change and was
force-closed **during a block's `PersistStateToDisk()`**. That left `user://blockchain/state.json` truncated
7 characters short of valid, and because `TryLoadSnapshot` had no `try`, every restart since has produced an
**empty world with no error printed** — BlockExplorer blank, no recent bets, while the money services (which
persist to their own files) restored perfectly and made the failure look selective. The freeze itself was
not caused by the auction screen: the bet journal had grown to **1.13 GB**, and every boot was deserializing
**~5.33 million** records while every rollback re-serialized them back out on the main thread.

#### P15.11.1 — The two faults (and one that was already happening)

| | Fault | Where |
|---|---|---|
| **F1** | *Proximate.* World snapshot written non-atomically (truncate + stream 9.25 MB); corrupt snapshot then fails **silently** — `JsonException` escapes `EnsureInitialized` before a single node is registered, `_isInitialized` stays `false`, nothing is logged | `NetworkRoot.PersistStateToDisk` (~L6525), `NetworkRoot.TryLoadSnapshot` (~L6720), called at `EnsureInitialized` (~L1001) |
| **F2** | *Root.* `RebuildJournalFromCurrentState` writes the whole in-memory history into the **base** file with no cap, no rotation, and without deleting the chunks it duplicates — so base + chunks are both loaded next boot and the duplication compounds per session | `BetHistoryRepository.RebuildJournalFromCurrentState` (~L617) vs `Flush` (~L523) |
| **F3** | *Collateral.* `WriteMonthlyChunks` deletes every `blocks-*.json` before rewriting them; it died partway. Harmless only because **nothing reads those files** | `NetworkRoot.WriteMonthlyChunks` (~L6565) |
| **F4** | *Already wrong, nobody knew.* `RebuildStatsFromLoadedHistory` counts the duplicated records, so lifetime bets / wagered / net profit have been **inflated for an unknown number of sessions** | `UserStatsService._Ready` (~L28) |

**The world survived only by accident.** The throw lands before any static state is touched, so
`EnsureInitialized`'s own closing `PersistStateToDisk()` is never reached and the good file was never
overwritten. One `catch`-and-continue in the wrong place would have replaced a 1,666-block chain with an
empty one at the next block.

#### P15.11.2 — The design limitation this exposed (2010-03-21 → 2012-09-22)

Recorded here because it is the reason this is a *phase* and not a hotfix. Full statement in **Chapter 40**.

The persistence layer was designed under the canonical premise — **1 bet = 1 nonce attempt = 100 in-game
seconds**, `TargetBlockSeconds = 58,500`, therefore **~585 player bets per block**, a person clicking or a
modest autobet. Under that premise "load the whole bet history at boot and replay it for the lifetime
stats" is not sloppy, it is the simplest correct thing.

What runs now is a **simulator**: a background autobet plus four bot runners across every scene, hours at a
time, at up to 9000X. What that produced in this world:

| Measure | Value |
|---|---|
| In-game span | 2010-03-21 → **2012-09-22**, ~2.5 in-game years |
| Blocks | **1,666** |
| `state.json` | **9.25 MB**, fully rewritten **every block** |
| `blocks-*.json` | ~5 MB more per block — **read by nothing** |
| Journal records loaded **every boot** | **~5,330,000** (1.126 GB base + 293 MB of chunks, overlapping) |
| Records per block | **~3,200** (about half duplicates) |

Even discounting the duplication that is several times the design premise — partly because of the known
block-pace defect recorded at ND.10j (~2.2 in-game days/block against a 0.68 target, so each block absorbs
roughly three times the intended bets; the R2 regulator work addresses the pace, not this).

**The point is not that the numbers grew.** It is that these subsystems encode the hand-play premise as an
*invariant* rather than a *tuning parameter*, so they do not degrade gracefully — they work perfectly until
they fail completely, in the least visible way available:

- **No retention policy exists anywhere.** Nothing has ever deleted a bet record except a world reset.
- **Lifetime stats are derived by replaying every record ever written** — there is no aggregate, so the
  totals cannot be known without holding the entire history in RAM (~5.3M `BetRecord` objects, an estimated
  1.5–2.5 GB of managed heap, on an Intel UHD 620 laptop).
- **Whole-file rewrites happen synchronously on the main thread** — `RollbackToUtc` on every checkpoint
  restore and every DiceGame entry; `PersistStateToDisk` on every block.
- **Snapshot cost is linear in chain length and paid per block** — 9.25 MB at 1,666 blocks, with nothing in
  the design that stops it at 10,000.

Same shape as §38.7's inverse-poll incident: a decision stayed correct while the premise underneath it was
multiplied, and **nothing was re-examined at the moment the premise changed**.

#### P15.11.3 — What is vital vs. expendable (the deletion authorization)

The developer has explicitly authorized deleting the statistics and anything else not required for the bank
testing the player is entering. Applying that:

**KEEP — vital to the bank testing (P15.2–P15.10) and to the world's identity**

`blockchain/state.json` (repaired — the chain, `NodeFinancialStates`, `CompanyFoundings`,
`CompanyGovernance`, `BankState`, `ClosedCompanies`, FBI state) · `block_session_checkpoint.json` ·
`central_bank_state.json` · `casino_sc_balance_state.json` · `sc_monetary_ledger.json` ·
`casino_client_ledger.json` · `player_bank_account_state.json` · `casino_coin_swap_state.json` ·
`casino_pool_state.json` · `hardware_allocation.json` · `principal_balance_state.json` ·
`bankroll_state.json` · `bankroll_program_state.json` · `calendar_state.json` · the identity files
(`bot_wallet_registry.json`, the five `*_wallet_state.json`, `wordlist_256.json`,
`saved_betting_strategies.json`) · both stamps (`world_format_version.txt` = `4`, `world_timeline.stamp` =
`CANON-2009-01-03+ENTRY-2010`) · `logs/*.csv` — **the traces are the P15.8 calibration record**, keep them.

**DELETE — expendable**

`bet_history.jsonl` + all 114 `bet_history_*.jsonl` (**1.4 GB**; stats only, already in the world-reset
delete list, and per F4 already wrong) · `blockchain/blocks-*.json` (write-only, already partially
destroyed) · the rotated `logs/godot2026-*.log`.

**What deleting the history actually costs:** lifetime bet counters, the `BetsHistoryExplorer` /
`CalendarsNavigator` browsable history, and DiceGame's seeded recent-bets list start empty. The
since-deposit / since-recharge scopes in `FinancialBettingStats` read `CasinoClientLedgerService`, which is
**kept**. No balance, no BTC, no company, bank, FED or auction state depends on the journal.

#### P15.11.4 — Subphases

- **P15.11a — Recover the crashed world. DO THIS FIRST, before any code change or relaunch.**
  1. **Back up** the entire `%APPDATA%\Godot\app_userdata\GamblingMiner\` directory. Non-negotiable — every
     step below is destructive and the world is currently one bad write from gone.
  2. **Repair `blockchain/state.json`**: read it as text, `TrimEnd()`, append `"\r\n  }\r\n}\r\n"`. That
     closes `BotGovernancePreferences` and the root object. Verified: the result balances to brace-depth 0,
     bracket-depth 0, no unterminated string. `CompanyInflowMultipliers` (the last property, never written)
     is absent and deserializes to empty = all ×1.0 — the only loss, and it is DEV knobs.
  3. **Delete** `bet_history.jsonl`, every `bet_history_*.jsonl`, and every `blockchain/blocks-*.json`.
  4. **Relaunch and verify** before touching anything else — see P15.11f.

  **Known artifact, stated not hidden:** the checkpoint (`block_session_checkpoint.json`, clock
  `2012-09-21 10:47` local) was written for block *N*, and the interrupted snapshot contains block *N+1*
  (tip `2012-09-22 18:08:27` UTC). The crash landed **between the two files' writes** — which is precisely
  the atomicity gap D-15.26 closes, here visible across two files rather than inside one. Expected to be
  benign (the money state is a valid earlier commit and the clock advances forward from it), but confirm the
  First Satoshi Savings auction still reads correctly before continuing the run.

- **P15.11b — Atomic snapshot write + a loader that fails loudly (D-15.26).** `NetworkRoot`.
  - `PersistStateToDisk`: serialize to a string, write it to `user://blockchain/state.json.tmp`, **close the
    handle**, then `System.IO.File.Move(tmpAbs, targetAbs, overwrite: true)` on globalized paths (atomic
    replace on Windows). **Gotcha:** the `using FileAccess` scope must *end* before the move — a `using`
    declaration at method scope lives until the method returns, so this needs an explicit block.
  - `TryLoadSnapshot`: parse inside `try`; on `JsonException`/`IOException` `GD.PrintErr` the path, the file
    size and the exception message, set a new static `_snapshotLoadFailed = true`, and rethrow.
  - `PersistStateToDisk`: early-return with a `GD.PrintErr` when `_snapshotLoadFailed` — **the guarantee that
    a failed load can never be written back over the good file**. This is the part that makes the incident
    non-repeatable, more than the atomic write is.
  - Optional, cheap: if `state.json` is unreadable and `state.json.tmp` parses, prefer the tmp and say so.

- **P15.11c — The journal rebuild must obey the file's invariants (D-15.27).** `BetHistoryRepository`.
  - Extract the rotation loop out of `Flush` into a shared private writer (`WriteEntriesRotating`) that
    appends, counts, and rotates at `MaxJournalEntriesPerChunkFile`.
  - `RebuildJournalFromCurrentState` then: **delete the base file and every existing chunk first** (glob
    strictly on `Path.GetFileNameWithoutExtension(_filePath) + "_*" + ext` in the journal's own folder — the
    same pattern `GetJournalChunkPaths` already parses; never a looser glob), reset the counters, and write
    the deposits + records through the shared writer.
  - **Gotcha:** on exit `_activeJournalPath` / `_activeJournalLineCount` must point at the **last chunk
    written**, not the base file, or the next `Flush` appends into a file the loader will read twice.

- **P15.11d — Retention cap, so the boot load is bounded by construction (D-15.28/29).**
  - Add a retention cap — `MaxRetainedJournalChunks` (recommend **20**, ≈200,000 records ≈57 MB) — enforced
    in `RotateToNextChunkFile` and after a rebuild: delete the oldest chunks beyond the cap.
  - With a cap in place `EnsureAllChunksLoaded()` is bounded, so the boot load can **stay** — which keeps
    every existing consumer semantically intact mid-playtest. Measure it once the cap is live; if it is
    still material, move the full load behind `BetsHistoryExplorer`/`CalendarsNavigator` and leave
    `UserStatsService._Ready` on the cheap latest-chunk path.
  - **Say what the number means (§39.16 rule 1).** Once history is capped, "lifetime" totals are really
    "over retained history". Label them that way in `FinancialBettingStats` / `BetsHistoryExplorer` rather
    than letting a bounded figure keep a lifetime caption. A persisted lifetime aggregate is the correct
    long-term answer and is **deferred**, not smuggled in here.

- **P15.11e — Stop writing `blocks-*.json` (D-15.30).** Delete `WriteMonthlyChunks` and its call in
  `PersistStateToDisk`. Keep the `blocks-*` delete loop in `ResetWorldIfIncompatible` so pre-existing files
  are cleaned up. Saves ~5 MB of I/O per block and removes a delete-all-then-rewrite that can only ever lose
  data.

- **P15.11f — Verification.**
  1. **World restored:** the boot log prints `[Governance] Casino miner-bot stances (restored with the
     world)` between `Hardware allocation loaded.` and `[CasinoScBalanceService] Ready` — its **absence is
     the signature of this whole incident**. BlockExplorer shows a chain tip at **2012-09-22**, ~**1,666**
     blocks; wallets and balances populate; the FED scene still shows 2 clients / 260,000 SC outstanding.
  2. **The auction survived:** First Satoshi Savings still in auction with the player leading and a close
     ~5 in-game days out (`AuctioningCompanyDetails`, gold frame per §22.15).
  3. **Atomicity:** with the game running, kill the process during a block commit. Relaunch — either the
     previous world loads cleanly, or the log carries an explicit snapshot-load error. **Never a silent
     empty world.**
  4. **Rotation:** play until a rollback fires (enter DiceGame), then check `user://` — the base file is
     ≤ one chunk's worth, chunk count ≤ the cap, and no chunk predates the rebuild.
  5. **No regression to the bank surface:** conversions still route through `SelectFinanciers`, the monetary
     invariant still reconciles in the CB scene, `dotnet build` clean.

**Exit:** the P15.8 world is playable again from where it stopped; a force-close can no longer produce a
silently empty world; the journal cannot outgrow its own rotation policy; and the plan carries an honest
statement of the scale limit the run exposed, with the parts deliberately left unfixed named in Chapter 40.6.

---

## 9. P15.8 — Developer observation checklist (the `DevEntryYear = 2010` run)

> Everything in P15.2–P15.7 has been **build-verified only** — this run is the first time any of it
> executes. The list is ordered by **when each thing becomes reachable** as you play forward from
> 2010-03-21, so it can be worked top-to-bottom rather than hunted for. Each item names where to look, what
> a correct reading looks like, and the failure signature to watch for.
>
> Two shorthands used below: **CB scene** = Main Menu → *Central Bank [DEV]*; **WE scene** = Main Menu →
> *World Economy [DEV]*.
>
> **RESULT (2026-07-30, run ended at ~Oct 2014 / block 2699):** **A ✅ · B ✅ · C ✅ · D ✅ (except D7) ·
> E ✅ · F ✅ · G ⏸ never reached · H ✅ held throughout · I ✅ read (→ §10) · J ⏸ not answerable.**
> D7 (a seizure lands) and all of G share one cause: no company was ever seized and no bank ever became
> insolvent, so the entire stress half of the checklist — and therefore the J placeholders that only a
> stress state can price — is **carried forward to P15.8-G after Step 16** (D-15.32/33, §10.4).

### A — Immediately at landing (2010-03-21)

| # | What to check | Correct | Failure signature |
|---|---|---|---|
| A1 | StatusBar carries the orange **`[ENTRY-2010 DEV]`** watermark | Present on every screen | Missing ⇒ the const didn't take / stale build |
| A2 | Game clock reads **2010-03-21**, chain has real intervening history | Block Explorer shows blocks dated 2009-03 → 2010-03 with cast miners spawning | A 2009-03 clock ⇒ the world didn't wipe; delete `user://world_timeline.stamp` and relaunch |
| A3 | **CB scene opens** and reads "No client has borrowed from the FED yet" | Empty client list + the pre-loan explanation | A crash or blank page |
| A4 | CB scene → **Banking layer**: "No bank company has founded yet…" | Expected at this date (first bank 2012-09) | A bank listed here in 2010 |
| A5 | CB scene → **financier preview**: no companies yet, or all → *The Casino (fallback)* | `(casino)` tier on every row | A bank named as financier before 2012-09 |
| A6 | WE scene → circulation **200,000 SC** = grants 200,000 + debt 0 | The five 40,000 genesis grants, nothing else | Any non-zero debt before the casino's first loan |

### B — The casino's first FED loan (play until a win empties the casino Bankroll)

| # | What to check | Correct | Failure signature |
|---|---|---|---|
| B1 | `CasinoGamblingFinances` → the loan line now reads **"FED loans taken: 1 … Outstanding: 40,000.00000000 SC"** | Count/total/outstanding all populated | Zeros after a loan clearly fired (the read-through accessors aren't resolving the FED) |
| B2 | Its history list shows a **`draw`** kind column | `2010-… | draw | 40,000.00000000 SC | auto` | Missing kind ⇒ stale scene |
| B3 | CB scene → a **`The Casino (casino)`** account appears, same figures as B1 | Outstanding = total drawn = 40,000; 1 draw | Numbers disagreeing between the two scenes ⇒ double-storage regression |
| B4 | CB scene → the invariant line reads **"FED/ledger debt in sync ✓"** | Green/subtle, `✓` | `OUT OF SYNC ✗` ⇒ the DrawLoan→RegisterLoanDraw lockstep broke |
| B5 | WE scene → debt 40,000 under borrower `casino`; circulation 240,000 | grants 200,000 + debt 40,000 | Circulation ≠ grants + debt |
| B6 | **Mine a block, then restart the app** | The FED account survives with identical figures | Reset to zero ⇒ checkpoint capture/restore ordering broken |

### C — Market Birth (2010-07-18) and the auction era

| # | What to check | Correct | Failure signature |
|---|---|---|---|
| C1 | BTC ticker appears in the StatusBar; swap desk unlocks | Price from the dataset | Still locked after 2010-07-18 |
| C2 | Non-miners start being introduced; bids begin (bots + you) | Block Explorer → Enroll Mode shows in-auction rows | No introductions at all by ~2010-09 |
| C3 | First **company founds** (~20 in-game days after its first bid) | Row moves to *Founded (out of auction)* with a "Details →" | — |
| C4 | Founded company → `CompanyDetails` → its conversions begin once the founding vote closes | `ScReserve` grows; the governance trace logs `conversion` rows | `ScReserve` stuck at 0 long after the founding vote |
| C5 | CB scene → financier preview still says **casino fallback** for every company | Correct until 2012-09 | A bank named before any bank founds |
| C6 | **(P15.9)** At any founded company's quarterly vote, every listed ballot lies **inside** the band named in the new `Ballots cast (band CBn: x–y% SC):` header | e.g. a CB1 company shows only 75–100 values, spread apart rather than identical | A `0%`/`50%` at a CB1 company ⇒ stale build; **any** `[Governance] P15.9 tripwire` line in the log ⇒ a ballot source bypassing `ProjectStanceIntoBand` |
| C7 | **(P15.9)** The reserve result **varies** quarter to quarter instead of sitting on a band bound, and your own dial visibly moves it | Result somewhere inside the band, shifting as bots' weights change | Pinned at exactly the floor every quarter ⇒ the projection is not being applied |
| C8 | **(P15.9)** Launch log → `[Governance]` stances print `global SC stance N% → votes CB1 … CB5 …` | The five projections match the §8 P15.9 table | Still printing `targets N% SC` ⇒ stale build |

### D — FBI activation (14 Jun 2011)

| # | What to check | Correct | Failure signature |
|---|---|---|---|
| D1 | Before that date, CB scene → **Federal investigations** reads "Not active yet — starts 2011-06-14" | Inactive | Any FBI activity before the date |
| D2 | On/after it: "Active since 2011-06-14 · budget 100,000.00 SC" | The initial federal grant landed | Budget 0 ⇒ the grant draw didn't fire |
| D3 | WE scene → a **`fbi`** borrower appears with 100,000 SC debt; circulation rises by the same 100,000 | The grant is a FED loan, so the invariant still balances | Circulation ≠ grants + debt after activation |
| D4 | Over time, **non-official** companies with SC piles accumulate a score | Rows appear in the FBI board with `score N/100` | An `official` company ever appearing (it must be exempt) |
| D5 | A company that stops converting / spends its SC **decays** back down | Score falls ~1.0 per block | Score only ever rises ⇒ decay branch not reached |
| D6 | `CompanyDetails` on a listed company shows the **⚖ investigation line** | Amber while growing, red once flagged | No line while the CB board lists it ⇒ the two readouts disagree |
| D7 | Eventually a flagged company is **seized** | It leaves the live list; CB scene → Closed companies shows reason `fbi_seizure`; the FBI budget grows by its SC | Seizure with no closure record, or SC vanishing |
| D8 | **Banks are never seized before non-banks are cleared** | Banks sort last in the FBI board | A bank raided while non-bank files are open |

### E — The first bank founds (2012-09-03 — the credit loop goes live)

| # | What to check | Correct | Failure signature |
|---|---|---|---|
| E1 | *First Satoshi Savings* founds with category **`official`** | CB scene → Banking layer lists it "category official (locked)" | Wrong category ⇒ roster/derive path broken |
| E2 | As the others found: Digital Reserve Trust **light_grey** (2013-06), Harbor Coin Bank **black** (2014-11), Ledger & Sons **dark_grey** (2016-03) | The Official→Black gradient | Any bank showing `official` other than the first |
| E3 | A bank's NST holders vote a market shift → **it does not move** | Category stays; `company_governance_trace.csv` logs `shift_refused=bank_locked` | Category drifts |
| E4 | CB scene → financier preview now names a **bank** for post-2012 companies, tier `nearest` | Nearest-category bank chosen, ties toward Official | Still `casino` after a bank founded |
| E5 | A company conversion routes through it: CB scene → **`bank:first_satoshi_savings`** account appears with FED debt | Debt = the SC it provisioned | Casino debt growing instead |
| E6 | Bank row shows **CollateralBtc > 0** and a client count | The BTC it bought | Collateral 0 while debt > 0 |
| E7 | Its **layer-1 sub-ledger** lists the financed company | `→ <Company>: bought X BTC for Y SC over N provision(s)` | Empty while E5/E6 populated |
| E8 | `bank_credit_trace.csv` has `provision` rows | One per conversion | File absent ⇒ trace path/permissions |
| E9 | The bank's own collateral is **not** auto-converted away | Collateral persists block to block | Collateral shrinking outside a payment day |
| E10 | No spurious **special vote** fires at the bank from collateral arriving | Only founding/quarterly votes | A >30% special vote right after a provision ⇒ the `COLLATERAL` memo skip failed |

> **⏸ P15.10 STARTS HERE.** E3 is the observation the deferred phase acts on: the shift is voted, refused,
> and *nothing on screen says so*. Once a bank has founded — and ideally once you hold NST in one, which
> takes a top-3 tracked tier in its auction — stop and build **P15.10** (§8; decisions already locked as
> D-15.25, so it is a build, not a design session). If you would rather keep playing, note the date you
> passed this point and come back; the next bank (2013-06-17) gives the same opportunity.

### F — The first bank quarterly (~2012-12) — repayment and the carry

| # | What to check | Correct | Failure signature |
|---|---|---|---|
| F1 | On the payment day, FED debt **steps down ~10%** | CB scene → the bank's outstanding drops; a `repay` row in its movement history | No movement on the quarterly date |
| F2 | Collateral drops by exactly what was sold (+ the fee) | Never more | Collateral < 0, or unchanged while debt fell |
| F3 | WE scene → circulation **falls** by the repayment (or debt moves casino-ward if the casino auto-loaned) | Invariant still balances either way | Circulation unchanged AND casino debt unchanged |
| F4 | `CompanyDetails` on the bank → **Bank lending book** panel | Debt, collateral + live value, health line, next installment + date | Panel missing on a founded bank |
| F5 | The panel's **next installment** matches what F1 actually charged | Same number | Divergence ⇒ shared-source rule broken |
| F6 | WE scene → banking-layer **system solvency line** | `collateral X vs FED debt Y → net Z (solvent)` | Missing block after a bank founded |
| F7 | Over a bull era the health line stays **covered**; watch it flip in a drawdown | The carry working as designed | — |

### G — Stress states (opportunistic — may need a bear era or engineering)

| # | What to check | Correct |
|---|---|---|
| G1 | A BTC drop leaves a bank unable to cover an installment → **shortfall vote opens** | New vote kind `shortfall`; CB scene shows "⚠ shortfall awaiting a board vote: N SC" |
| G2 | If you hold NST there, the **game pauses** and `CompanyDetails` shows the **single dividends-cut dial** (not the reserve/market/payout form) | Submitting resumes play |
| G3 | Bots resolve it per **greed** when you hold nothing | `company_governance_trace.csv` → `shortfall_apply` with `dividendsCutPct` |
| G4 | The gap closes from `ScReserve`, and a dividends cut also shrinks `QuarterDividendSc` | Both move together per §39.11.2 |
| G5 | An unclosable gap → **INSOLVENT** then **dissolution** | CB scene → "✗ INSOLVENT"; then Closed companies with reason `debt_default` |
| G6 | The dead company's Block Explorer row becomes a grey **`✗` terminal row with no button** | And `CompanyDetails` shows the liquidation notice, not "not founded yet?" |
| G7 | If you held stock there, the notice states **what you lost** | NST/PST + unclaimed BTC/SC; already-claimed dividends untouched in your wallet |
| G8 | A closed wallet with a matching solvent bank is **inherited**; without one it stays in FED custody | CB scene → "held by X" vs "FED custody (100% BTC…)" |
| G9 | Later inflows to the dead address are **forwarded** to the heir | `RecoveredBtc` grows; `bank_credit_trace.csv` `seized_inflow` rows |
| G10 | The **recovery tracker** verdict updates with the live price | `RECOVERED (+profit)` / `underwater` |

### H — Invariants to spot-check at any moment

- **`circulation = grants + debt`** on the WE scene, always. Genesis grants stay at exactly 200,000 SC forever.
- **CB scene invariant line = "in sync ✓"** at all times.
- The casino's `LoanCount` / `Total loaned` / `Outstanding` agree between `CasinoGamblingFinances` and the CB scene.
- After **any** block + restart, every figure above returns to its last-block value (never a between-blocks value).
- No company ever shows a **negative** ScReserve, collateral, or debt.

### I — Telemetry to read afterwards (`user://logs/`)

| File | What it should contain |
|---|---|
| `bank_credit_trace.csv` | `provision`, `repay`, `shortfall_pending`, `shortfall_dust` (P15.10d — a sub-cent residue deliberately NOT made a shortfall), `shortfall_closed`/`shortfall_unrecoverable`, `dissolution`, `wallet_inherited`, `seized_inflow`, `fbi_activated` |
| `company_governance_trace.csv` | `conversion` rows carrying `via=` + `tier=`; `vote_close` with `shift_refused=bank_locked` for banks; `shortfall_apply` |
| `company_founding_trace.csv` | The foundings feeding all of the above |

### J — Placeholders to form an opinion on (the actual point of P15.8)

Note whether each **feels** right; exact values are meant to be tuned here, not defended.

- `BankQuarterlyRepaymentFraction` **0.10** — do banks deleverage too fast / too slowly?
- Greed payout ladder **0.5 / 1.0 / 1.5 / 2.0** and the shortfall table **90 / 70 / 30 / 10**.
- FBI tolerance multipliers **∞ / 8× / 3× / 1×** and the `T` window (one quarter).
- Meter gain **0.5**, decay **1.0**, overage cap **4**; roll base **0.5%**, cap **2%**.
- `FbiInitialGrantSc` **100,000**.
- Are seizures too frequent/rare? Do banks ever actually die, or never?

> **Answered by the run (2026-07-30): the last question, and only the last one.** *Banks never die, and
> seizures never happen* — across 3⅓ in-game years past FBI activation the world produced one
> `shortfall_pending`, eleven `shortfall_dust`, and **zero** seizures or dissolutions. Every other bullet
> here is **still unanswered**, because a placeholder can only be priced by the state it governs and none
> of those states occurred. They are carried to P15.8-G (§10.4), NOT silently accepted (D-15.33).

---

## 10. P15.8 — Run outcome, audit and suspension (2026-07-30)

> This section is the **closing record of the step**. It states what the calibration run actually proved,
> what it disproved, what it left untouched, and the conditions under which P15.8 resumes. It is written
> from the run's own telemetry rather than from impressions, because the traces are the only artifact of a
> 4½-in-game-year world that survives it (INC-001's lesson: *that evidence has a short half-life*).

### 10.1 The run

| | |
|---|---|
| World | `DevEntryYear = 2010`, `WorldFormatVersion 4`, stamp `CANON-2009-01-03+ENTRY-2010` |
| Span | landed **2010-03-21**, stopped **~Oct 2014** — block **2699**, ~1,472 blocks traced after the INC-001 rotation |
| Population | **30 companies founded**, **2 banks live** (First Satoshi Savings, Digital Reserve Trust), FBI active since 2011-06-14 |
| Volume | 517 company votes · 444 bank provisions · 12 repayments · 13,691 bot dividend claims |
| Health | **0 errors, 0 exceptions, 0 P15.9 tripwires** in a 4.4 MB log; the monetary invariant reconciled at every check |

**The plan15 machinery is correct.** Nothing below is a defect in the banking reform. What the run found is
that the reform is *inert* — it works perfectly and almost nothing in the world pushes on it.

### 10.2 The six findings

**F1 — Governance is a constant function, and the player is its only source of variance.**
`vote_close` rows, reserve% across each company's whole life: `coinwash` 11.96 × 10 quarters; `casascius`
75.00 × 20 votes; `btc_guild` 14.11 × 18 votes; `digital_reserve_trust` 83.74 × 5. Exactly one company's
reserve target ever moved — `first_satoshi_savings` 78.05 → 90.30 — **and it is the one where the player
holds NST.** Cause is structural: `BuildBotBallot` is a pure function of `(persisted preference, company
state)`, both constant between votes, so the NST-weighted average is constant forever. P15.9 made the
ballots *legal*; nothing made them *alive* (D-15.34).

**F2 — The pause tax.** `vote_open … awaiting_player` = **93** of 517. Ninety-three full-simulation
freezes bought ~2 outcome changes. At active companies the `special` (>30% inflow) votes outnumber the
quarterlies, so **the freeze rate scales with how successful the player's holdings are** (D-15.35).

**F3 — The mempool is saturated by the companies' own dividend plumbing.**
`network_population_trace.csv`: `txTargetPerBlock` ≈ **5.07**, `pendingTxs` = **26–28** against a 24-tx cap
(23 usable). `bot_claim` averages **8.66 tx/block** — 1.7× the entire historical budget. `owed =
max(0, target − pending)` is therefore structurally **0**, so ND.4a's cast sell-flow and non-miner
exchanges have effectively stopped — and cast sell-flow is what *funds the companies* (D-15.36).

**F4 — The money supply is a one-way ratchet.** FED at the stop: casino **2,560,000** outstanding over
**64 draws and 0 repayments**; `fbi` 100,000; `bank:non_miner_22` 2,469,509 (of 3,723,997 drawn);
`bank:non_miner_25` 3,853,132 (of 4,973,349 drawn). **Total outstanding 8,982,641 SC against 200,000 SC of
genesis grants — 97.8% of all SC in existence is debt**, up ~45× in 4½ in-game years. The invariant holds
exactly; what is missing is any *feedback*. Banks deleverage (10%/quarter, working as designed); the casino
never does, because nothing was ever built to make it. This is not a bug — it is the ND.8e credit-capacity
layer's absence, now measured.

**F5 — R2 is confirmed; the lag has a different shape than assumed.** Over 1,472 blocks: mean solvetime
**62,373 s vs the 58,500 s target (+6.6%)** — the Round-2 regulator question is closed. Retention throttle
mean **0.713**, by 200-block bucket `0.752 · 0.693 · 0.801 · 0.806 · 0.653 · 0.599 · 0.706 · 0.688` — **no
monotone decay with chain length**, which does not fit a purely `O(chain)` cost and does fit *chronic
saturation plus periodic per-block spikes* (the developer's "processes a second, stalls a second"). Per
block the engine now does: a ~9 MB snapshot serialize written twice for P15.11 atomicity, 62 UTXO cache
invalidations each repaired by full replay, ~8.7 dividend-claim transactions, and appends to five CSV
traces (`company_governance_trace.csv` alone reached 2 MB). **T4.6 first — instrument, then choose**
(D-15.37).

**F6 — Zero stress states, and that is the finding.** One `shortfall_pending`, eleven `shortfall_dust`
(P15.10d's cutoff doing its job), **no** seizure, **no** dissolution, **no** insolvency — through a real
historical drawdown. So §9 G and D7 never became observable, and every §9 J placeholder that only a stress
state can price remains **unpriced**.

### 10.3 What plan15 may claim, and what it may not

| Claim | Status |
|---|---|
| The FED entity, per-client accounts, two-layer debt architecture | ✅ Verified live (§9 A/B) |
| Conversions route through banks; collateral quarantined; client sub-ledger | ✅ Verified live (§9 E) — 444 provisions |
| Quarterly repayment burns SC and steps FED debt down | ✅ Verified live (§9 F) — 12 repayments |
| Bank categories locked; the refusal surfaced honestly | ✅ Verified live (P15.10) |
| Ballots legal and in-band | ✅ Verified live — 0 tripwires in 517 votes |
| `circulation = grants + debt` across 4½ in-game years and ~9 M SC | ✅ Held throughout |
| The BTC carry survives a bull era | ✅ Observed |
| The BTC carry survives a **drawdown into insolvency** | ⏸ **Untested — never occurred** |
| Shortfall vote → cuts → unrecoverable → dissolution chain | ⏸ **Untested past `shortfall_pending`** |
| Seized-wallet custody, inheritance, recovery tracker | ⏸ **Untested — no seizure ever fired** |
| FBI tolerances / meter / roll calibrated | ⏸ **Unpriced — the mechanism never engaged** |

### 10.4 P15.8-G — the carried-forward phase

**Why it is not a merge blocker.** Everything in the "untested" column is a *terminal* path: it destroys
companies. None of it can corrupt the world while it never fires, and all of it is behind explicit
triggers. Shipping the reform with its stress paths build-verified-only is the same bargain P15.2–P15.7
already took to reach P15.8 — with the difference that this one is now written down instead of assumed.

**Resume conditions — any one of these makes G observable:**

1. **Step 16's evolution/dividend rework** gives companies a reason to run lean and a way to over-extend,
   which is the natural producer of both shortfalls and FBI-visible SC piles.
2. **A forced-scenario DEV harness** (recommended, cheap): a WorldEconomy button that injects a shortfall,
   or drops a chosen bank's collateral value, or sets a company's `InvestigationScore` at the flag
   threshold. This tests the *chain of consequences*, which is what G actually cares about — the arrival
   probability is a separate question and a tuning one.
3. **A `DevEntryYear = 2013` run** landing directly in the drawdown with banks already founded, so the
   carry is stressed within minutes instead of hours.

**Do this when resuming, in order:** (a) build the harness in 2; (b) walk G1→G10 with it; (c) *only then*
tune §9 J, because a placeholder priced against a forced scenario is at least priced against something.
**Do not tune the FBI numbers before the mechanism has been seen firing** (D-15.33).

### 10.5 Handoff to Step 16

F1/F2 (governance is constant, the pause is expensive) and F3/F6 (nothing pushes on the mechanisms) are
one problem wearing three hats: **the world has no depth for the banking layer to react to.** Step 16
addresses that directly — dynamic persona-driven voting, pivotality-gated pauses, company evolution levels
with NST/PST funding rounds, and the dividend-claim traffic fix (D-15.36). F5 hands the performance thread
to `PRIVATE_ROADMAP.md` §8 **T4**, starting at T4.6. `OQ-8.2` (bot seed phrases / full UTXO integration),
promoted to "right after Step 15", joins Step 16 as its own phase.

### 10.6 Where the evidence lives

`user://logs/` from this world, deliberately preserved (the INC-001 precedent — the CSV traces are the
calibration record): `difficulty_trace.csv` (F5), `company_governance_trace.csv` (F1/F2/F3),
`network_population_trace.csv` (F3), `bank_credit_trace.csv` (F6), `company_founding_trace.csv`,
`casino_bot_bid_trace.csv`, plus `central_bank_state.json` for the F4 balances. Every number quoted above
is reproducible from those files; none of it is reconstructible once the world is wiped.
