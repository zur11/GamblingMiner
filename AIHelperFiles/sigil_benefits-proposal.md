# Sigil Benefits — Proposal for the next plan

**Status: a PROPOSAL, not a decision.** Input for the plan that follows `graphic_numerology-plan.md`. The developer
liked the direction on 2026-11-01; nothing here is built or chosen except §1, the developer's own rules. Every benefit
in §4 still needs its size, its level thresholds and its go-ahead.

**What it answers:** what a player gains from a sigil, in every field the game already has, with each benefit tied to
the symbolism of its planet and zodiac sign. Mechanics of the sigils themselves live in `graphic_numerology-plan.md`.

---

## 1. Fixed by the developer (2026-11-01)

These are recorded as D-GN.23 to D-GN.30 in `graphic_numerology-plan.md`; the proposal is built on them.

- **Violet costs 100× what the measured rule cost** (`SigilRules.PassesPerChance`): 100 passes per set at 1%, `100 × c`
  at chance `c`. See §5 for what that does to every figure.
- **One of each of the 14 sigils exists in the whole game.** The player buys or receives it once, and deduces which
  ones combine from each item's name, shape, colour and description. Nothing states a pair outright.
- **One sigil slot, for now.** The player chooses which owned sigil to play, at any time. Switching loses only the pass
  in progress, and it is committed by the next mined block like everything else.
- **A sigil's level is the highest level all its sets share** — the lowest of its sets. This is what unlocks benefits.
- **A combined figure's level is the highest both sigils share** — the lower of the two. Level 10 + level 4 = level 4.
- **That level only opens the planetary sigil's levels; each must then be activated** (2026-11-02, D-GN.30): level `k`
  takes `k` whole passes through both sigils, drawn one after the other, once both hold level `k`. Whether each level's
  price adds to the last (OQ-GN.1) is open in the plan's §8.2.
- **A complete sigil cannot be played** (D-GN.28): only an activation draws it again. This is a first answer to the old
  question of why anyone would repeat a sigil (D-GN.18).

Levels run 1 (red) to `SigilRules.MaxLevel` (violet). So levels are already the answer to *"how does a benefit grow?"*;
§6 keeps only what they do not settle.

---

## 2. What a benefit may never do

Each limit comes from a canonical decision in `CLAUDE.md`.

1. **It does not improve the dice.** The RTP is canonical, so no better wins, no better multipliers, and no bankroll
   "insurance", which is a higher RTP under another name. Benefits work around the bet, never inside it.
2. **It does not change what SC is worth.** The 1:1 peg is canon; only SC *quantity and credit* move. New SC must enter
   as a recorded event in `ScMonetaryLedgerService`, under `circulation = grants + debt`. ⚠️ The only grants today are
   the genesis ones (`GenesisGrantPerParty`), so any SC reward needs a new kind of ledger event, designed on purpose.
3. **It does not touch the network fee.** A swap discount comes off the casino's cut alone, and only within
   `MinSwapFeePercent`–`MaxSwapFeePercent`.
4. **It does not bypass the block.** A benefit whose effect sits in checkpointed state reverts with the rest of the
   world on a restart, which is correct. A benefit that writes anything new must answer Pattern 2's three questions,
   the third being whether its file is on `WipeWorld`'s list.
5. **What it shows must be what it does** (Standing Convention 6). A displayed benefit shares its source with the
   mechanism it advertises.

---

## 3. The shape: day sigils act, night sigils receive

In the traditional symbolism the day houses are active and outward and the night houses receptive and inward. The
proposal mirrors that:

- **A day sigil gives a TOOL:** something the player uses.
- **A night sigil gives a PROTECTION or a way of SEEING.**
- **A combined figure gives the stronger form of the two.** Discovering it is the reward for working out which two
  sigils belong together. *At which level is §6's question 8: this line said "the combined level" until D-GN.30 gave the
  planetary sigil an activated level of its own.*

Names and pairs follow `PlanetarySquares.All` (the developer's domicile table), which is not always the textbook
assignment: Virgo is the day sigil of Mercury here, Gemini the night one.

---

## 4. The fourteen benefits

**Readiness:** 🟢 the system exists, the benefit only needs wiring · 🟡 the system exists but the benefit needs new
mechanics inside it · 🔴 it depends on a system not built yet.

### Saturn — time, limits, structure → hardware and credit

| Sigil | Symbolism | Benefit | Touches | |
|---|---|---|---|---|
| ☀ **Aquarius** | Networks, technology | **Hardware credits** — "your node in the network". ⚠️ Worthless until hardware has a real price: today's Buy button is free and DEV-only | `HardwareAllocationRepository`; the betting cap `MaxAutoBetBaseAps` | 🔴 |
| ☾ **Capricorn** | Institutions, patience | **Standing with the Central Bank:** access to credit once tightening exists (D-ND8.30: scarcity is expressed in *who can borrow*). The player has no FED account today | `CentralBankService` | 🔴 |

### Jupiter — expansion, abundance, law → companies and the auction

| Sigil | Symbolism | Benefit | Touches | |
|---|---|---|---|---|
| ☀ **Sagittarius** | Expansion, ventures | **A wider share band:** the player's bid mints full-rights NST one tier further down than `NstTopTierCount` allows | Auction settlement, NST minting | 🟡 |
| ☾ **Pisces** | Dissolution, forgiveness | **Forgiveness:** eases the FED installment of bank companies where the player holds NST, which reaches the player as dividends. Must keep `circulation = grants + debt` (for example, debt reclassified as a grant) | `CentralBankService`, `ScMonetaryLedgerService` | 🟡 |

### Mars — action, competition → rivalry with the bots

| Sigil | Symbolism | Benefit | Touches | |
|---|---|---|---|---|
| ☀ **Aries** | Initiative, being first | **First strike:** after the player's bid, bots skip the following block on that pool, a head start measured in blocks | `BuildBotPoolOpportunities` | 🟡 |
| ☾ **Scorpio** | Secrets, death and rebirth | **Sight:** the bots' play history, which is only a DEV scene today (`BotPlayHistory`). This answers an open design question. **Alternative — rebirth:** escape game over once. ⚠️ D-SF2.1 specifies game over, but a code search found only a comment in `ScFinances`; verify it exists first | `BotPlayHistory` / the game-over check | 🟢 / 🔴 |

### Sun — identity, authority, recognition → governance

| Sigil | Symbolism | Benefit | Touches | |
|---|---|---|---|---|
| ☀ **Leo** | Leadership, display | **Weight in the boardroom:** a multiplier on the player's NST-weighted ballot, so it needs NST to act on. Also, as new work, a personal signature on blocks the player mines | Board votes (`ProjectDesignManual.md` Ch. 41) | 🟡 |
| ☾ **Eclipsed Leo** | The hidden sun | **Concealment:** companies where the player holds a stake build FBI pressure more slowly. The investigation meter is per company, not per person, and starts at `FbiActivationLocal` | `TickFbiInvestigations`, `InvestigationGainPerBlock` | 🟡 |

### Venus — value, balance, beauty → the swap desk and the bank

| Sigil | Symbolism | Benefit | Touches | |
|---|---|---|---|---|
| ☀ **Libra** | Scales, fair exchange | **A lighter casino cut** at the swap desk, towards `MinSwapFeePercent`, for the player alone | `CasinoCoinSwapService` | 🟢 |
| ☾ **Taurus** | Possessions, savings | **The vault:** an SC yield on term deposits in the Private Bank Account. Bound by §2.2: it needs the new ledger event | `PlayerBankAccountService`, `ScMonetaryLedgerService` | 🟡 |

### Mercury — exchange, numbers, messages → information

| Sigil | Symbolism | Benefit | Touches | |
|---|---|---|---|---|
| ☀ **Virgo** | Analysis, precision | **Analysis tools:** advanced statistics, an exact what-if calculator, extensions to the Martingale calculators | `UserStatsService`, the calculators | 🟢 |
| ☾ **Gemini** | Twins, duality | **The sigil-aware side:** autobet picks Low or High according to the root the current target needs. **This is a real gain:** at 5% on Sagittarius, the manual player's best play costs ~37% less than a fixed side (plan §6.3). Blind alternation is not a benefit: D-GN.15 measured that it gains nothing | `SigilRules`, autobet | 🟡 |

### Moon — cycles, tides, memory → the market

| Sigil | Symbolism | Benefit | Touches | |
|---|---|---|---|---|
| ☀ **Eclipsed Cancer** | The dark moon | **Seeing the hidden network:** the split of network power between named miners and the invisible mass. A ghost miner is only a pseudonym with nobody behind it, so there is no "real" miner to unmask | `NetworkPopulationScheduler` | 🟢 |
| ☾ **Cancer** | Tides, cycles | **The oracle:** the tide of the coming week's BTC price. The most expensive planet gets the most mystical reward. The history is real and public, so the edge is narrative more than unfair; no price exists before Market Birth | `BtcMarketDataService` | 🟢 |

### Across all of them (Venus: beauty)

Cosmetics fit every level: the day, night and eclipse textures still to be made, the sigils in the UI, titles.

---

## 5. What levels cost

Costs are `Predicted(n) × PassesPerChance` from the harness: the same at every chance, by design (D-GN.8). A set of
figures for sizing benefits, not a promise: each is a mean, and a combined figure costs **twice** the sigil, since both
must reach the level, **plus the price of activating it** (D-GN.30, table in the plan's §8.2). Real time assumes 100X, where 99 credits (`MaxAutoBetBaseAps`) make 99 bets a real second.

| Planet (n) | One sigil to violet (bets) | One level (bets) | One sigil to violet at 99 credits (real time) |
|---|---|---|---|
| Saturn (3) | 810,000 | ~8,500 | ~2.3 hours |
| Jupiter (4) | 5,760,000 | ~60,600 | ~16 hours |
| Mars (5) | 12,330,000 | ~129,800 | ~1.4 days |
| Sun (6) | 20,520,000 | ~216,000 | ~2.4 days |
| Venus (7) | 30,330,000 | ~319,300 | ~3.5 days |
| Mercury (8) | 41,040,000 | ~432,000 | ~4.8 days |
| Moon (9) | 53,370,000 | ~561,800 | ~6.2 days |
| **All fourteen** | **328,320,000** | | **~38 days** |

Two consequences to design around. A violet sigil is a very long goal, so **the early levels are where most players
will live**, and benefits need meaningful thresholds well below violet. And the planets differ by a factor of 66 in
cost, so a level on Saturn and a level on the Moon are not the same achievement.

---

## 6. Questions for the next plan

1. **Thresholds.** Continuous (every level adds a little) or tiers (say at levels 10, 25, 50, 75, 95)? Tiers are easier
   to display and to balance.
2. **Does a benefit need its sigil in the slot?** If only the equipped sigil's benefit is active, the one slot is a real
   choice between drawing and benefiting. If every owned sigil counts, the slot only decides what is being drawn.
3. **Are any benefits spent?** Rebirth or forgiveness could be charges. With one of each sigil and violet as the end,
   what would recharge them is open, and so is the older question of why anyone would repeat a sigil (D-GN.18).
4. **Buying and receiving.** Which sigils are sold, for what, and by whom? Which are received, and for what event? A
   price needs data this proposal does not have. Should the order Saturn → Moon (retired as an unlock by D-GN.26) come
   back as a gate on acquiring them?
5. **Bots.** "One in the whole game" makes a sigil owned by a bot a sigil the player can never have. Is that the race we
   want, or do bots stay out (P-GN.7)? Bots with Leo or Aries would act against the player directly.
6. **Uncommitted progress.** Should the overlay show how much sigil progress rides on the next block? At high credits
   it is a lot: in the 2026-10-29 measurement a whole violet Aquarius was lost to a restart (plan §10.5).
7. **More slots** ("only one for now"): earned how, and what would a second slot change in question 2?
8. **Which level does a combined benefit read?** D-GN.30 separates the combined level (what can be activated) from the
   activated level (what has been). The word "activate" suggests the benefit reads the second, so that an unactivated
   figure gives nothing. Not decided.

---

## 7. Corrections to the first version of this proposal

The first version, given in chat on 2026-10-29, was checked against the code on 2026-11-01. Five of its claims were
wrong, and the table above already replaces them:

- **Pisces forgave "the player's FED loan".** The player has none: their starting SC is a genesis grant, and the FED's
  clients are the casino and the bank companies.
- **Sagittarius lowered the raise floor.** The player's floor is already one satoshi (ND.4d, `REFERRAL_AUCTION.md`).
- **Aries won ties.** One bid per donor per block, at a one-satoshi floor, makes a tie close to irrelevant.
- **Eclipsed Leo hid the player's identity.** The FBI meter belongs to companies, not people.
- **Gemini offered Low/High alternation.** D-GN.15 had already measured that alternating gains nothing.
