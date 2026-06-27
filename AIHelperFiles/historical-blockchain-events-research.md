# Historical Blockchain Events — Research & Data Capture

**Status**: **Step 7 roster + events CANONICAL** (implemented; see `step7-historical-character-economics-plan.md`). The v1 roster (Satoshi/Hal/Hearn) and the reproduced events (E1 genesis, E4 10 BTC Satoshi→Hal, E6/E6b/E7 Hearn 32.51 round-trip → +82.51) are frozen and wired in code. **Still open (Step 8):** §6 address-reuse research + E8 (17.49 change) as a real change output. Companion to `historical-founders-and-bootstrap-plan.md`.
**Purpose**: build one authoritative table of **every early-Bitcoin character** and **every on-chain in/out event** we want to reproduce in the fractal (100X, 1%-supply) replica — grounded in real history wherever possible. This file is filled in two passes:

1. **Questionnaire pass (now)** — list every fact we need, mark what is already known vs. what must be decided/researched. Answer inline under each `❓`.
2. **Canonical pass (later)** — once answers exist, freeze the Character Roster and Events Ledger tables and reference them from code (`HistoricalBootstrapService`, `FoundersMiningService`).

> Convention: ✅ = confirmed/known · ❓ = needs your decision or research · 🔢 = derived/fractal value to compute · ⚠️ = design conflict to resolve.

---

## 1. What we already know (captured from source texts)

From `Los Primeros Mineros de Bitcoin.txt` and `satoshi interactions.txt`:

| # | Real date / time (UTC) | Event | Amount | From → To | Real block | Notes |
|---|---|---|---|---|---|---|
| E1 | 2009-01-03 18:15:05 | Genesis coinbase | 50 BTC | coinbase → Satoshi | 0 | Unspendable. Headline inscription. |
| E2 | 2009-01-09 | Block 1 onward, Satoshi mining | 50 BTC/blk | coinbase → Satoshi | 1+ | Satoshi dominant. |
| E3 | 2009-01-11 | Hal Finney runs a node ("Running bitcoin") | — | — | — | Hal becomes 2nd miner. |
| E4 | 2009-01-12 | First p2p tx: **10 BTC Satoshi → Hal** | 10 BTC | Satoshi → Hal | confirmed in **170** (spent from block-9 coinbase) | Pure test tx. |
| E5 | 2009-02-23 10:05:15 | Satoshi mines a block whose reward later funds the Hearn sends | 50 BTC | coinbase → Satoshi (`1AfRNhdvL5zYL1FTxM9mHyYnHuHK4TpLYh`) | — | Input UTXO for E6/E7. |
| E6 | 2009-04-18 15:55:19 | **32.51 BTC Satoshi → Mike Hearn** (test coins) | 32.51 BTC | Satoshi → Hearn (`1JuEjh9znXwqsy5RrnKqgzqY4Ldg7rnj5n`) | **11408** | Hearn emailed his addr at 15:08. |
| E7 | 2009-04-18 15:55:19 | **50.00 BTC Satoshi → Mike Hearn** (full block reward gift) | 50.00 BTC | Satoshi → Hearn | **11408** | Same block as E6. |
| E8 | 2009-04-18 15:55:19 | **17.49 BTC change → Satoshi** (auto UTXO change) | 17.49 BTC | Satoshi → new Satoshi addr | **11408** | 50 − 32.51 = 17.49. ⚠️ see §5. |
| E9 | 2010-12-12 | Satoshi's last public post (0.3.19 DoS) | — | — | — | Last login 2010-12-13; 575 posts. |
| E10 | (historical estimate) | Satoshi total holdings | ≈ 1.1 M BTC | — | — | Fractal target **11,000 BTC** (1%). |

**Other characters named but not yet scoped**: Mike Hearn (E6–E8, joins ~Apr 2009), Gavin Andresen (mentioned, no tx yet).

---

## 2. Character Roster (DRAFT — confirm/extend)

| Character | Node id | Enters chain | Leaves / dormant | Special miner? | BTC target | Real base address (reference) | Our `gm1q…` addr |
|---|---|---|---|---|---|---|---|
| Satoshi Nakamoto | `satoshi` | Genesis 2009-01-03 | Retire ≥ 2011-04-26 | ✅ Yes (weighted, dominant) | ✅ 11,000 BTC | `1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa` (genesis); `1AfRNhdv…` (E5) | derived from seed |
| Hal Finney | `hal` | 2009-01-11 | ❓ (real: active to 2014, died 2014-08-28) | ✅ Yes (3 bootstrap blocks) | None | ❓ | derived from seed |
| Mike Hearn | `mike_hearn` | ~2009-04-18 (after player) | ❓ (real: left Bitcoin Jan 2016) | ❓ does he mine, or only receive? | None | `1JuEjh9znXwqsy5RrnKqgzqY4Ldg7rnj5n` | derived from seed |
| Gavin Andresen | ❓ | ❓ | ❓ | ❓ | None | ❓ | ❓ |

❓ **Q-R1**: Confirm the four characters above are the full v1 roster, or name others to include (e.g. Gavin Andresen, Martti Malmi / "sirius", Laszlo Hanyecz of the 2010 pizza).
❓ **Q-R2**: For each character, do we keep a *reference* to their real base58 address (documentation only) while paying our derived `gm1q…` address? (Plan assumes yes.)

---

## 3. Per-character questionnaire

### 3.1 Satoshi (`satoshi`) — mostly resolved
- ✅ Target 11,000 BTC, retire ≥ 2011-04-26, exponential ramp if short.
- ✅ Genesis recipient (unspendable) + dominant bootstrap miner.
- ❓ **Q-S1**: After retirement, do his coins stay frozen forever (recommended), or can a late-game "Satoshi returns" event ever move them?
- ✅ **Q-S2 (RESOLVED via Q-X1)**: Model **multiple** Satoshi addresses — a fresh passphrase-derived address per coinbase reward / deposit ("Patoshi pattern"), plus change addresses for E8. One single base address is the testing-only shortcut, not the target. Pending the §6 research on whether any real Satoshi address was reused.

### 3.2 Hal (`hal`)
- ✅ Joins 2009-01-11; exactly 3 bootstrap blocks (~12 Jan, ~early Feb, ~early Mar).
- ❓ **Q-H1**: Does Hal keep mining (tiny weight) into the player era, or stop at 21 Mar until a future dynamic uses him?
- ❓ **Q-H2**: Any Hal *outgoing* tx to reproduce, or is he receive-only (the 10 BTC from Satoshi) in v1?
- ❓ **Q-H3**: Model his 2014 death as an on-chain "dormant forever" flag, or ignore until that era is in scope?

### 3.3 Mike Hearn (`mike_hearn`)
- ✅ Enters after player (~Apr 2009). Receives E6 (32.51) + E7 (50.00); never spends them (real history: untouched).
- ❓ **Q-M1**: Does Mike Hearn **mine** at all, or is he a receive-only holder node (like a `non_miner`)? Real Hearn was a dev, not a notable miner.
- ❓ **Q-M2**: His funds were never moved — flag his address permanently dormant (consistent with lost-BTC sim)?
- ❓ **Q-M3**: The 18 Apr transfers happen ~block 🔢 (see §4) in our fractal — but that's *after* the player has started and time is bet-driven. How do we trigger a scripted historical tx in the player era? Options: (a) fire it automatically when the clock crosses the target date during normal play; (b) pre-bake it into the bootstrap even though the date is post-21-Mar. **Recommendation: (a) — a lightweight `HistoricalEventScheduler` that injects scripted txs when the game clock passes their date.** Confirm.

### 3.4 Other characters
- ❓ **Q-O1**: Include Gavin Andresen / the Bitcoin Faucet (mid-2010)? Laszlo's 10,000 BTC pizza (2010-05-22) as a flavour event? These fall in the player era and need the §3.3-M3 scheduler.

---

## 4. On-chain Events Ledger (DRAFT — fractal mapping to compute)

Real **dates** are the source of truth; fractal **block heights** are derived (≈1.477 blocks/in-game-day from genesis). Heights compress massively vs. real Bitcoin.

| Event | Real date | Real block | 🔢 Fractal block (approx) | In bootstrap or player era? | Reproduce as |
|---|---|---|---|---|---|
| E1 genesis | 2009-01-03 | 0 | 0 | bootstrap | existing coinbase, recipient fixed |
| E4 10 BTC → Hal | 2009-01-12 | 170 | ~13 (9 days × 1.477) | bootstrap | real signed tx, Satoshi → Hal |
| E6 32.51 → Hearn | 2009-04-18 | 11408 | ~155 (105 days × 1.477) | **player era** | scheduled historical tx |
| E7 50.00 → Hearn | 2009-04-18 | 11408 | ~155 | player era | scheduled historical tx |
| E8 17.49 change | 2009-04-18 | 11408 | ~155 | player era | ⚠️ see §5 |

❓ **Q-E1**: Confirm the fractal-block approximations are "by timestamp, not by height" (i.e. we don't try to reproduce real heights like 170 or 11408 — impossible at 1% block density). Plan assumes yes.
❓ **Q-E2**: Should the famous tx amounts be exact (10, 32.51, 50.00, 17.49) even though our economy is fractal? **Recommendation: keep exact amounts — they're iconic and BTC values aren't scaled, only block density and total supply are.** Confirm.

---

## 5. Cross-cutting design questions — ALL RESOLVED (2026-06-19)

### ⭐ Q-X1 — Balance model now, UTXO-realism target  ✅ RESOLVED (with a major design direction)

**The current `BlockchainService` being balance/account-based (`GetAddressData` sums per-address amounts) is a TESTING-STAGE state, not the destination.** The design intent is to simulate a UTXO-style system **as realistically as possible**, made tangible to the player **at least through the passphrase-wallet system**, which already derives many distinct addresses from one seed phrase. This turns address management into an educational, hands-on Bitcoin concept.

Concrete consequences to bake into the plan:

- **"One fresh address per receive" (Patoshi-pattern reproduction).** To mirror how the real Satoshi ("Patoshi") used a new address for nearly every mined block, our Satoshi node should **derive a fresh passphrase address for each coinbase reward (and, ideally, each incoming deposit)** rather than reusing a single base address. This same mechanic is the educational backbone of UTXO realism for the player's own wallet later.
- **Change transaction E8 (17.49 BTC).** With per-receive addresses this stops being purely cosmetic: spending from a single-receive address naturally leaves change that must go somewhere. Reproduce E8 as a real change output to a **new** Satoshi address — realistic, not just flavour.
- **Research needed:** find which address(es) the known human senders (Mike Hearn, others) actually paid Satoshi to — and whether any Satoshi address was **reused** (received more than once). This determines whether strict one-address-per-receive holds historically or has documented exceptions. → added to §6.

> Documentation impact: this intent must be reflected across the docs that describe the address/balance model — done in `btc-wallet-system-plan.md` (UTXO-realism roadmap), `Documentation/GLOSSARY.md`, `Documentation/DESIGN_OVERVIEW.md`, `Documentation/ProjectDesignManual.md` (Chapter 1), and `CLAUDE.md`.

### Q-X2 — Realism scope for v1  ✅ RESOLVED

v1 = **E1–E4** (genesis + bootstrap era + the 10 BTC Satoshi→Hal tx). **E5–E8** (the Mike Hearn April transfers + change) are a **fast-follow** once the player-era `HistoricalEventScheduler` (§3.3-M3) exists.

### Q-X3 — Address authenticity  ✅ RESOLVED

Keep real base58 addresses **as commented references only**; all actual payouts use derived `gm1q…` addresses. Optional educational tooltips showing the real base58 string are welcome where they add teaching value.

### Q-X4 — Inscriptions / messages per event  ✅ RESOLVED

**For now, only the genesis block carries an `InputData` note** (the existing headline). Keep the `InputData` system fully wired and ready to attach messages to other events/contexts when it becomes handy, but add no other historical inscriptions in v1.

---

## 6. Data still to verify (research TODO)

- ⭐ **Which address(es) did known humans pay Satoshi to** (Mike Hearn and any others), and was any Satoshi address **reused** (received > once)? Decides whether strict one-address-per-receive holds or needs documented exceptions. (Drives Q-X1 / Q-S2.)
- ❓ Hal Finney's real receiving address for the 10 BTC (block 170) — for the reference column.
- ❓ Exact real timestamps for blocks 1–170 to validate our "Satoshi mines ~everything, Hal 3 blocks" bootstrap spacing feels right.
- ❓ Whether to include the 2010-05-22 pizza (10,000 BTC) and the mid-2010 faucet as player-era flavour events.
- ❓ Gavin Andresen's first Satoshi interaction date/amount, if we add him.

---

*Created: 2026-06-19 · Q-X1–X4 resolved. Next: research the §6 address items, then promote §2 and §4 to canonical and wire them into `HistoricalBootstrapService` / a new `HistoricalEventScheduler`.*
