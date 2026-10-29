# Graphic Numerology — Plan

**Series note:** a full plan, not a mini-plan. It pauses the run of technical mini-plans (25–29) to open a new goal
in `PRIVATE_ROADMAP.md`: bringing the developer's other project, *Numerología Gráfica*, into the game.

**Status:** 📝 **SPECIFIED 2026-10-28.** The design is decided, D-GN.1–D-GN.19: the zodiac sigils (§4), the drawing
rule, balanced at the same cost at every chance (§5–§6), the Low/High choice (§6.3), the order of the sigils (§7) and
the tracking of bets and SC (§8). **§8's integration, P-GN.1–P-GN.7, is approved (2026-10-29)**, with P-GN.6 an
overlay like the Martingale calculator. §9 registers the predictions and §10 the order of work. **Next: unit 1.**

**Branch:** `graphic_numerology` (created 2026-10-28 off `main` at `0888105`, at the developer's request, before the
plan was written — the lifecycle's step 1 otherwise creates it on approval; the name is the developer's, not the
`miniNN-` form, since this is not a mini-plan).

---

## 1. The source project

*Numerología Gráfica* is a separate project of the developer's, on the same machine at
`C:\Users\PERSONAL\Desktop\Numerologia Grafica`. It replicates, in a Google Sheet and a web page, a series of
undocumented Italian videos about magic squares, the Vedic square (digital roots), planetary sigils, primes and
figures on circles. **It is written entirely in Spanish; everything brought into this repository is translated to
English** (the project's Language Policy), using the terms in §2.

| Source | What it is |
|---|---|
| `GraphicNumerologyGuide.md` | The summary, with every figure already verified. Read first |
| `CLAUDE.md` | That project's memory: rules, findings, and what is **confirmed** versus **hypothesis** |
| `Numerologia_Grafica.gs` | Apps Script that builds the Sheet's 24 tabs; the squares are its `CUADRADOS` constant |
| `web/sigilos.html` | The web page (HTML/SVG/JS, no libraries); the sigil rules are `FAMILIAS` and `trazosDe` |

**Rules for using it:**

- **Read only.** Nothing in that folder is modified unless the developer asks. Data or code used here is **copied**
  into this repository, never referenced across folders.
- **Every claim taken from it carries its status** — confirmed or hypothesis — as that project's `CLAUDE.md` marks it.
- **Its square data already exists twice** (the `.gs` and the `.html`). The port will be a third copy, so it must have
  **one** canonical declaration in this repository, cited by symbol everywhere else (Standing Convention 15). Until
  that declaration exists, **Appendix A** is the reference copy; once it exists, Appendix A is superseded by it.
- Calculations are done with `node` (no Python on this machine) and compared with the guide's verified figures.

**Verified on intake (2026-10-28, Node, from the page's own `CUADRADOS`):** the seven squares are magic; constants
15 · 34 · 65 · 111 · 175 · 260 · 369; totals 45 · 136 · 325 · 666 · 1225 · 2080 · 3321; series cell counts
(3/3/3 … 27/27/27) and neutral series as in §3.1; group-1 step-of-1 paths as in the guide (e.g. Saturn B3-C1-A2, Moon E6-…-D5); the Vedic
square totals 459 and each of the nine Vedic wall blocks sums to 405. **All match the guide.**

---

## 2. Vocabulary — Spanish source term → English term used here

These are the only English names for these concepts in this repository. Each joins `Documentation/GLOSSARY.md` when
the feature that uses it lands.

| Source (Spanish) | English | Meaning |
|---|---|---|
| raíz | **digital root** | `root(x) = 1 + (x − 1) mod 9`, `root(0) = 0` in the source; sum the digits until one remains. **The drawing rule reads 0 as 9 instead** (D-GN.4) |
| series / familias | **series** 1-4-7 · 2-5-8 · 3-6-9 | `x mod 3` = 1 · 2 · 0. Colours: 1-4-7 blue `#3D7FD1`, 2-5-8 green `#21A35A`, 3-6-9 red `#D9674F` |
| cuadrado védico | **Vedic square** | `root(r × c)`, r, c = 1…9 |
| muro védico | **Vedic wall** | Nine blocks; block k is `root(r + k·c)` |
| muro por bases | **wall by base** | Cell `(r, c) = base × r × c`, coloured by its digital root |
| cajas védicas | **Vedic boxes** | A 9 and its eight neighbours in Vedic wall block k |
| Rostro de Saturno · olas | **Face of Saturn** · **waves** | The base-9 wall coloured by unreduced digit sum |
| cuadrados planetarios | **planetary squares** | The seven magic squares of §3.1 |
| anillos · integración · coincidencias | **rings** · **nesting** · **coincidences** | Concentric rings; rings nested across squares; a value equal to its reading position |
| sigilo | **sigil** | See §3.2 |
| saltos de 1 / de n / de k | **steps of 1** / **of n** / **of k** | The sigil families of §3.2 |
| puentes · recorrido completo | **bridges** · **full path** | n → n+1 links; the 1 → n² path (Agrippa's sigil) |
| serie neutral · especulares | **neutral series** · **mirrored series** | §3.3. *"Mirrored" is reserved for this meaning only* (D-GN.1) |
| brújula de los primos · Círculo de 36 | **prime compass** · **Circle of 36** | Figures from the same video series; out of this plan's scope until a goal names them |
| intervalo musical | **musical interval** | Step in the root + 1 (step 1 = second … step 9 = tenth). The author's analogy, accepted by the developer |

---

## 3. The planetary squares and their sigils

### 3.1 The seven squares

| Square | n | Magic constant (root) | Total 1…n² (root) | Neutral series |
|---|---|---|---|---|
| Saturn | 3 | 15 (6) | 45 (9) | 2-5-8 |
| Jupiter | 4 | 34 (7) | 136 (1) | 1-4-7 |
| Mars | 5 | 65 (2) | 325 (1) | 3-6-9 |
| Sun | 6 | 111 (3) | 666 (9) | 2-5-8 |
| Venus | 7 | 175 (4) | 1225 (1) | 1-4-7 |
| Mercury | 8 | 260 (8) | 2080 (1) | 3-6-9 |
| Moon | 9 | 369 (9) | 3321 (9) | 2-5-8 |

The values are in **Appendix A**. The constant is `n(n² + 1) / 2` and the total `n²(n² + 1) / 2` — consequences of
`n`, not independent figures. The source page also carries an eighth square, the "alternative Jupiter (Wiki)"; it
is outside this plan, as it is outside that project's own synthesis.

### 3.2 The sigil rule — confirmed

A sigil **joins, in order and with straight lines, the centres of the cells holding the numbers of each group.**
(Confirmed in the source by pixel measurement of the video's Saturn and Jupiter frames, and by its synthesis table
for all seven squares.) Two families build every planetary sigil:

- **Steps of 1** — groups of n consecutive numbers: group g = `(g−1)·n + 1, …, g·n` (the rows of the natural square).
  Saturn: 1-2-3, 4-5-6, 7-8-9.
- **Steps of n** — group g = `g, g + n, g + 2n, …, g + (n−1)·n` (the columns of the natural square). Saturn: 1-4-7,
  2-5-8, 3-6-9. *The author uses steps of n, not steps of 3 — confirmed on Jupiter.*

Cell notation in this plan: **letter = column** (A = left), **number = row** (1 = top). The cell-by-cell paths of
every group of both families, for all seven squares, are in the source guide §4.3; Saturn's, as an example:

- Steps of 1: g1 B3-C1-A2 · g2 A1-B2-C3 · g3 C2-A3-B1
- Steps of n: g1 B3-A1-C2 · g2 C1-B2-A3 · g3 A2-C3-B1

**Endpoint circles** (confirmed): green at a group's start, red at its end.

### 3.3 Series and the neutral series — confirmed

A **series sigil** draws steps of 1 + steps of n, but only the groups g with `g mod 3` = 1, 2 or 0. Two of the
three series give figures rotated 180° from each other (the **mirrored series**); the third is **neutral**,
unchanged when each g becomes `n + 1 − g`. The neutral series of each square is in §3.1.

**Open in the source (hypothesis or unknown, not used here):** the origin of the list 369 · 471 · 521 · 639 · 741 ·
881 · 999; the + / − / N column of the synthesis table; the "esterni − k" plates.

### 3.4 The day/night pair — a new finding, confirmed by calculation

**The steps-of-n sigil of every planetary square is an exact reflection of its steps-of-1 sigil, cell by cell,**
and group g of one lands on group g of the other:

| Squares | Reflection |
|---|---|
| Odd — Saturn 3, Mars 5, Venus 7, Moon 9 | Left–right (about the vertical centre line) |
| Even — Jupiter 4, Sun 6, Mercury 8 | About the anti-diagonal (lower-left to upper-right) |

**How it was checked (2026-10-28, Node, on the source page's data):** let `T` be the transpose of a number's index in
the natural square, `T((a·n + b) + 1) = (b·n + a) + 1`, which maps each steps-of-1 group onto the steps-of-n group
with the same g. For each square, each of the eight symmetries of the n × n grid was tested for `pos(T(x)) =
σ(pos(x))` for **every** x = 1…n². Exactly one σ holds per square, the one in the table. (Compared as an unordered set
of segments, the figures admit more symmetries — e.g. Saturn's also match under 90° rotations — but only the one above
maps each number onto its partner.) Saturn shows it directly: B3-C1-A2 by day, B3-A1-C2 by night — A and C swap.

This is **not recorded in the source project**. It is what makes the two families a *pair* rather than two unrelated
figures, and it gives the day/night naming of §4 a geometric basis.

**D-GN.1 — the pair is called the day/night pair, never "mirrored".** The source already uses "mirrored" for the
two series rotated 180° against the neutral one (§3.3). Two meanings under one word is the confusion this avoids.
*(Developer, 2026-10-28.)*

---

## 4. The zodiac sigils

**D-GN.2 — each planetary square's two sigils are named after its planet's zodiac domiciles: steps of 1 is the
diurnal sigil, steps of n the nocturnal sigil.** The name is the sign followed by "Sigil". *(Developer, 2026-10-28.)*

The domiciles are the traditional ones: each planet has a day and a night sign, except the two luminaries, which have
one each — the Sun a day sign (Leo), the Moon a night sign (Cancer).

**D-GN.3 — a luminary's missing slot takes its own sign, eclipsed.** The Sun's nocturnal sigil is the **Eclipsed Leo
Sigil**; the Moon's diurnal sigil is the **Eclipsed Cancer Sigil**. *(Developer, 2026-10-28.)*

| Square | Diurnal sigil — steps of 1 | Nocturnal sigil — steps of n |
|---|---|---|
| Saturn 3 | Aquarius Sigil | Capricorn Sigil |
| Jupiter 4 | Sagittarius Sigil | Pisces Sigil |
| Mars 5 | Aries Sigil | Scorpio Sigil |
| Sun 6 | Leo Sigil | Eclipsed Leo Sigil |
| Venus 7 | Libra Sigil | Taurus Sigil |
| Mercury 8 | Virgo Sigil | Gemini Sigil |
| Moon 9 | Eclipsed Cancer Sigil | Cancer Sigil |

Fourteen sigils, twelve signs: each sign appears once, except Leo and Cancer, which appear twice.

**A name belongs to the whole sigil** — all n groups of its family drawn together — and to nothing smaller.

**Each sigil's texture carries a background showing day, night or eclipse**, so the name itself does not need to say
more. **The final names are provisional** and are revisited in the polish pass at the end of Basic Mode.

---

## 5. Drawing a sigil by betting — the Aquarius Sigil

The player draws a sigil **while betting in `DiceGame`**: dice rolls move a running value, and a won bet whose value
lands on the sigil's next number advances the drawing. This section specifies it for the **Aquarius Sigil** (Saturn,
steps of 1), the smallest; numbers 1–9 behave differently from the larger squares, which are specified separately.

### 5.1 The roll as a digital root — D-GN.4

A roll is `00–99` (`DiceEngine.Play`: `_rng.Next(0, 100)`, upper bound exclusive). Its value here is its digital root
`r`: a one-digit roll as is, a two-digit roll's digits summed down to one. **`00` counts as 9** — and 9 is the zero of
digital-root arithmetic, so adding or subtracting it never changes anything. *(Developer, 2026-10-28.)*

### 5.2 The running value V — D-GN.5

V is a single digit 1–9 (a 0 always reads as 9). *(Developer, 2026-10-28.)*

1. **The first bet of each new sigil**, won or lost, sets `V = r` — the sigil never starts negative.
2. **Every later bet:** won ⇒ `V ← root(V + r)`; lost ⇒ `V ← root(V − r)` (the same arithmetic in reverse).
3. **After every won bet, V is compared with the target.** V carries on unchanged whether it hit or not; a lost bet
   is never compared.

The sum is always reduced. *Parked for the larger squares:* the exception where the target is 10, 20 … 90.

### 5.3 Sets, targets and drawing — D-GN.6

A sigil is drawn **set by set**. A **set** is one stroke of the sigil — for Aquarius, the three steps-of-1 groups
1-2-3, 4-5-6 and 7-8-9, each two lines through three points. A **set pass** is three hits on that set's numbers, in
order. *(Developer, 2026-10-28.)*

| Target | A hit… |
|---|---|
| the set's 1st number (1, 4, 7) | draws the set's **silhouette**, in the colour this pass will earn (§5.4) |
| its 2nd number (2, 5, 8) | draws the set's first line (1-2, 4-5, 7-8) |
| its 3rd number (3, 6, 9) | draws its second line (2-3, 5-6, 8-9) — **the set pass is complete** |

A first pass of the whole sigil is nine hits, six lines; bridges (3-4, 6-7) are never drawn. **Every set pass begins
with its silhouette** — there is no once-only reveal.

**Measured (2026-10-28, Node, 2,000 runs per setting):** one pass of the whole sigil costs a mean of **≈ 81 / chance**
bets — 1% ≈ 8,200 · 10% ≈ 820 · 50% ≈ 163 · 95% ≈ 86. Each win has about a one-in-nine chance of hitting. **Low and
High make no difference**: although a win is a contiguous roll range the player could aim, the losses keep moving V by
amounts the player does not control.

### 5.4 Colour, level and the restart — D-GN.7 to D-GN.10

**D-GN.7 — the chance of the bets sets the colour, on a 95-step rainbow:** 1% violet, ~48% green, 95% red. **Violet is
the top.** A sigil drawn at 1% is drawn once; at 95%, 95 times. *(Developer, 2026-10-28.)*

**D-GN.8 = B, balanced — a set needs as many complete passes as the chance it is drawn at.** A pass completed at chance
`c` adds `1/c` of the set; **the set is finished when the sum over its completed passes reaches 1**, and then it is
violet. Under a mix of chances, the colour of a pass in progress is its chance's colour, and completion is shown
separately (e.g. a counter). Finished sets are skipped: the passes cycle through the unfinished sets in order until all
are finished — the whole sigil violet. *(Developer, 2026-10-28; alternative A, a linear level, was rejected because it
did not balance — at ~48% it was 20–30× faster than at either extreme.)*

**D-GN.9 — a pass belongs to one chance. Changing the chance before a set pass completes restarts that set:** the
target goes back to the set's first number, and the partial lines of the interrupted pass are lost. **V is not reset**
— only a new sigil resets it (§5.2). *(Developer, 2026-10-28.)*

**Measured (2026-10-28, Node, the full rule at a constant chance):** the mean bets to finish the violet Aquarius Sigil
is **~8,100–8,200 at every chance** — 1% 8,229 (2,000 runs) · 10% 8,196 (600) · 50% 8,087 (200) · 95% 8,095 (100).
**The balance is exact**: 9 hits per sigil pass × `c` passes × ≈ `900 / c` bets per hit = 8,100, independent of `c`.
Starting every set pass with its silhouette is what makes 1% cost the same as the rest.

**D-GN.10 — a new betting stop, "Stop on Sigil Set Completed"**, so the player can change strategy (and chance)
between sets without losing progress — D-GN.9 makes a mid-set change cost the pass. It joins the existing stops in
`BettingStrategyConfig`, beside `StopOnBlockMined`. *(Developer, 2026-10-28.)* Within one autobet run a set never
restarts by itself: a strategy carries one fixed `WinningChance`, and its progression moves the bet amount, not the
chance.

**D-GN.11 — sigil progress is player state under the checkpoint rule (Pattern 2).** Persisted: every completed sigil
**with the SC wagered and the bets placed completing it** (D-GN.16 and D-GN.19 add the per-planet totals), and every
sigil in progress (V, the target, each set's accumulated passes and the
pass in progress with its chance). A restart reverts it to the last mined block, like every other player-facing value;
a player who resumes **at the same chance** continues the pass from that point. *(Developer, 2026-10-28.)* Pattern 2's
three questions apply to the store when it is built: a `BlockSessionCheckpointService` restore path, a
`ResetToPreGenesisDefaults()` path, and a place in `NetworkRoot.WipeWorld`'s delete list.

---

## 6. The larger squares — two-digit targets

**D-GN.12 — everything in §5 holds unchanged for the targets 1–9, in every square.** Above 9 a target is obtained in
one of two ways. *(Developer, 2026-10-28.)*

| Target | A won bet hits when… |
|---|---|
| 1–9 | `V` after the bet equals the target (§5.2) |
| 10, 20 … 80 — round tens | `V` after the bet equals the tens digit: 10 behaves as 1, 20 as 2 … 80 as 8. (Allowing a sum such as 7 + 3 = 10 was considered and dropped: it is the same as 1.) |
| Every other two-digit target | **`V` before the bet, as the tens digit, beside the roll's root, as the units digit, spells the target** |

The two-digit form, on the developer's example — target 21: `V = 3`, a winning roll 8 spells **38**, a miss, and V
moves on as always to `root(3 + 8) = 2`; the next bet also wins, roll 73 (root 1), and `V = 2` beside 1 spells **21**,
a hit. V then moves on to `root(2 + 1) = 3`. V's own rule (§5.2) never changes; only the comparison does.

The largest target is 81 (Moon), so tens digits run 1–8 and 90 never occurs.

### 6.1 What it costs — measured

A two-digit hit needs two independent one-in-nine events on the same win (V's value **and** the roll's root), so it
costs ≈ 81 wins against ≈ 9 for a single-digit one. Under D-GN.8 (`c` passes of ≈ `900 / c` or `8,100 / c` bets per
hit) a violet sigil therefore costs, independent of the chance:

**violet sigil ≈ 900 × (targets 1–9 and round tens) + 8,100 × (other two-digit targets)** bets.

**Measured (2026-10-28, Node, one diurnal pass simulated and multiplied by `c`; 40 runs at 10%, 120 at 50% and 95%,
Low):**

| Sigil | Targets (1-digit + round / two-digit) | Predicted | 10% | 50% | 95% |
|---|---|---|---|---|---|
| Aquarius (Saturn 3) | 9 + 0 | 8,100 | 8,590 | 8,733 | 8,466 |
| Sagittarius (Jupiter 4) | 10 + 6 | 57,600 | 60,869 | 55,682 | 56,405 |
| Aries (Mars 5) | 11 + 14 | 123,300 | 136,721 | 128,216 | 124,292 |
| Leo (Sun 6) | 12 + 24 | 205,200 | 216,475 | 207,479 | 206,731 |
| Libra (Venus 7) | 13 + 36 | 303,300 | 318,779 | 304,427 | 316,739 |
| Virgo (Mercury 8) | 15 + 49 | 410,400 | 426,107 | 407,084 | 407,846 |
| Eclipsed Cancer (Moon 9) | 17 + 64 | 533,700 | 567,121 | 540,922 | 539,511 |

The prediction holds within sampling noise; 10% runs ~5% high because its winning range 0–9 holds root 9 twice and
the others once. **The balance across chances survives in the larger squares.** The seven diurnal sigils total
≈ 1.64 million bets; at one hardware credit (100 game-seconds per bet) that is ≈ 5.2 game-years, and the nocturnal
seven, with the same numbers, as many again.

### 6.2 Chances of 9% and below — D-GN.13, and the problem it solves

**D-GN.13 — at a chance of 1–9%, a win counts the loss streak before it.** Let `L` be the number of consecutive losses
immediately before the win, and `s = L + r`. A two-digit target hits when `V` before the bet is the tens digit **and
`root(s)` is the units digit**. Hit or miss, `V` then moves on to `root(V + s)` — so at these chances every win moves
V by `s`, and the 1-digit and round-ten targets compare that V. Losses, and every chance of 10% and above, are
unchanged. *(Developer, 2026-10-28, including the two readings the simulation used and the developer then confirmed:
V moves by `s` on a hit too, and the 1-digit and round-ten targets compare that V.)*

**Measured (2026-10-28, Node, violet cost = one pass × `c`; Aquarius 400 runs, Sagittarius 60, Eclipsed Cancer 8):**

| Sigil | Predicted | 1% Low | 1% High | 2% Low | 5% Low | 5% High | 9% Low | 10% Low (normal rule) |
|---|---|---|---|---|---|---|---|---|
| Aquarius | 8,100 | 8,130 | 8,144 | 8,056 | 7,919 | 8,195 | 8,373 | 8,374 |
| Sagittarius | 57,600 | 59,934 | 54,750 | 62,392 | 59,560 | 59,447 | 55,391 | 62,508 |
| Eclipsed Cancer | 533,700 | 510,458 | 507,289 | 517,961 | 542,089 | 534,293 | 535,635 | 523,094 |

**Every chance finishes, and the balance holds from 1% to 95%**, with no step at the 9% / 10% boundary. (The Moon row
has 8 runs per cell; its ±5% is sampling noise.) The streak works because `L` is geometric with a mean of roughly
`100 / c − 1`, so `L mod 9` is close to uniform and supplies the roots the narrow winning range cannot.

What it replaced — **below 9% the plain rule could not finish the larger sigils.** A win is a contiguous roll range
(`DiceEngine.GetWinningRange`), and only a range of at least nine consecutive rolls holds every digital root; at 1%,
Low wins only on 00 and High only on 99, both root 9. Measured: the Moon sigil at 1%, 2% and 5%, Low and High,
20 runs each — none finished in 3 million bets.

### 6.3 Choosing the side bet by bet — D-GN.14, D-GN.15

The player sees `L`, `V` and the target before every bet, so at a low chance they can choose the side (Low/High) whose
winning rolls hold the root the target needs. **Measured (Node, Sagittarius, 150 runs each), violet cost:**

| Chance | Always Low | Side chosen bet by bet |
|---|---|---|
| 2% | ~58,900 | ~40,300 |
| 5% | ~56,400 | **~35,600** |
| 8% | ~58,000 | ~51,700 |

At 5% the two sides together hold every root once, so choosing well makes two-digit targets ~37% cheaper. At 10%
and above each side already holds every root and choosing gains nothing.

Worked case, 5%, target 12 (Sagittarius): Low wins on 00–04 (roots 9 1 2 3 4), High on 95–99 (roots 5 6 7 8 9). With
`V = 1` and `L = 6`, a hit needs `root(6 + r) = 2`, so `r = 5` — only roll 95. A Low bet cannot hit at that moment; a
High bet hits with probability 1/100. A player fixed on one side has the needed root on their side about 5 times in 9.

**D-GN.14 — a side change does not restart the set. The advantage stays open, deliberately, for the manual player who
works the arithmetic out bet by bet.** Only a chance change restarts (D-GN.9). *(Developer, 2026-10-28.)*

**D-GN.15 — autobet gets an option to alternate Low and High, bet by bet.** It gains nothing, because the alternation
knows nothing of the sigil. **Measured (Node, Sagittarius 5%, 300 runs each):** always Low ~55,151 bets, alternating
~57,828 — the same within sampling noise. A future option, not designed yet. *(Developer, 2026-10-28.)*

---

## 7. The order of the sigils

**D-GN.16 — within a planet, the diurnal sigil comes first and the nocturnal second; drawing both completes the
planet's sigil.** Saturn: the Aquarius Sigil, then the Capricorn Sigil ⇒ **the complete Saturn Sigil**. The SC wagered
is totalled for each sigil and for the two together. *(Developer, 2026-10-28.)* Each sigil is a new sigil for §5.2, so
V restarts at the Capricorn Sigil's first roll.

**D-GN.17 — the nocturnal target order: sets in group order, each set's numbers ascending**, exactly as the diurnal
sigils. Capricorn: **1-4-7, 2-5-8, 3-6-9**; Pisces: 1-5-9-13, 2-6-10-14, … The steps-of-n group g is
`g, g + n, …, g + (n−1)·n` (§3.2), so every number 1…n² is still a target exactly once. *(Developer, 2026-10-28.)*

**D-GN.18 — the planets unlock in order of size: Saturn → Jupiter → Mars → Sun → Venus → Mercury → Moon.** A complete
planet sigil (both of its sigils) unlocks the next planet. **Provisional:** after testing, alternatives are reconsidered
— including reasons to *repeat* a sigil — and so is whether bots can join this dynamic easily. *(Developer,
2026-10-28.)*

**Measured (2026-10-28, Node, violet cost, Low):** Capricorn ~7,993 (5%) · ~8,103 (50%) · ~7,981 (95%), 400 runs each;
Pisces ~58,110 at 50%, 150 runs. A nocturnal sigil has the same targets as its diurnal partner, so **it costs the
same**, and a complete planet costs twice its diurnal sigil: Saturn ≈ 16,200 bets, Moon ≈ 1.07 million.

---

## 8. Integration — approved

The design above (§3–§7) is decided. **This section is how it enters the code. Claude proposed P-GN.1–P-GN.7, and the
developer approved all seven on 2026-10-29, amending P-GN.6 and adding the tracking of D-GN.19.** Each proposal names
the code it relies on, read on 2026-10-28.

**P-GN.1 — one canonical declaration of the squares.** A static C# class holds the seven squares and their zodiac
names; constants, groups, set lists and target orders are **derived** from it, never written twice (Standing
Convention 15). Appendix A is superseded the moment it exists. At startup it asserts the cheap closed-form bounds —
every row, column and diagonal equals `n(n² + 1) / 2` — so a typo in a transcribed square fails loudly, not as a sigil
that silently never completes (the INC-002 rule: a cheap bound is asserted).

**P-GN.2 — the rule as a pure class.** One class, no Godot dependency, takes the progress state and a settled bet and
returns the new state plus what happened (hit, set pass complete, set finished, sigil complete). Pure so that it can be
checked against §5–§7's Node figures by a `dotnet run` scratch harness (P9, the unit-test framework, is not
configured). **O(1) per bet, no allocation, no I/O** — it runs on every player bet, at autobet rate.

**P-GN.3 — the hook sits beside the player's bet registration, before the nonce attempt.** Both player bet paths
already register each settled bet in `UserStatsService.OnBetExecutedRegisterBet`, before the bet's mining attempt:
`DiceGame.ExecuteBet` (manual play, when the player is the active node) and `SimulationService`'s player-autobet tick
(guarded by `_config.IsPlayerActive`). The sigil update goes in the same place, in both. **Not** a subscription to
`SimulationService.ClientBetSettled`: that event fires *after* `CaptureCheckpoint`, so the bet that mines a block
would be missing from that block's checkpoint — a lying figure under Pattern 2. `BetTransactionEvent` already carries
everything the rule reads: `IsWin`, `Roll`, `Chance`, `IsHigh`, `BetAmount`.

**P-GN.4 — a state owner, committed only by the checkpoint.** A new autoload (the 21st) owns the progress in memory
and fires a typed event when a hit, a set or a sigil changes something (Pattern 1) — **not on every bet**, although V
moves on every bet; a V readout samples it (§29.13). **It writes no file of its own:** its state is captured into, and
restored from, the block checkpoint, with a pre-genesis default (Pattern 2's first two questions); having no own file
answers the third (`WipeWorld`) and the eager-writer guard (`WorldWriteGuard`) by construction. *Why no own file:*
mini-plan 08 measured a per-bet balance write at 66% of a bet's cost. Its registration slot is chosen deliberately
before `BlockSessionCheckpointService` (Important Patterns §5).

**P-GN.5 — "Stop on Sigil Set Completed" (D-GN.10)** as a field of `BettingStrategyConfig` beside `StopOnBlockMined`,
saved with the strategy (`SavedBettingStrategy`), offered in the strategy panel, and **read from the session in a
running path, never from the panel** (D-M2.8, the rule `StopOnBlockMined` already follows in both paths).

**P-GN.6 — the sigil overlay over `DiceGame`, built like the Martingale calculator** *(amended by the developer,
2026-10-29)*. It covers the DiceGame screen, so each sigil has room to be drawn large with its details live, and it is
closed in one click to carry on playing. The pattern to follow is `MartingaleCalculator`: its own scene
(`Screens/MartingaleCalculator/`), instanced in `DiceGame.tscn` with a unique name and hidden at start; DiceGame opens
it from a button with `Open()` and closes it on its `CloseRequested` signal. **While hidden it does no work** — the
calculator checks `IsVisibleInTree()` and builds lazily (mini-plan 21 E) — which matters more here, since the state it
shows changes on every bet. The progress lives in the service (P-GN.4), so closing the overlay loses nothing.

It shows: the square's grid, the set silhouette, the drawn lines in the colour of their chance, the current target,
V (and L at 1–9%), each set's completed passes as a counter (D-GN.8), and the tracking of D-GN.19. The day / night /
eclipse backgrounds are textures still to be made. **`ProjectDesignManual.md` Chapter 29 is read before the scene is
built.** The colour map: hue moves linearly over the 95 chances, red at 95%, green near 48%, violet at 1%; the exact
hues are a build detail.

**D-GN.19 — each sigil tracks the bets placed on it and the SC wagered on it; a complete planet shows the sum of its
two sigils.** "Bets placed" counts every player bet while that sigil is the one being drawn, won or lost, from its
first roll to its completion. Both figures are part of the persisted progress (D-GN.11), and the bet count is what the
in-game measurement reads against §9. *(Developer, 2026-10-29; "attempts" read as bets.)*

**P-GN.7 — bots are out of the first build** (D-GN.18). They are Dice-only and their bets already pass through
`SimulationService`, so joining later looks cheap; whether it is, is a question for after the tests.

---

## 9. Predictions, registered before any in-game data

From §5–§7's Node model. A build that misses them is checked against the model **before** any figure is tuned.

| # | Prediction | Decision rule |
|---|---|---|
| Pr-1 | Violet Aquarius Sigil at **95%** costs **8,100 bets**. At 95% a completion is 855 hits, so one run's spread is about ±3% (1σ) | One in-game run outside **7,300–8,900** (±10%, ~3σ) ⇒ the code and the model disagree; find which before tuning |
| Pr-2 | At 10% and above, a single-digit target is hit on **≈ 1 win in 9** | Checked by the harness over ≥ 100,000 simulated wins |
| Pr-3 | Low and High cost the same, and so do the diurnal and nocturnal sigils of a planet | Harness: within sampling noise |
| Pr-4 | At 1–9% every sigil finishes, at the same cost as above 9% (D-GN.13) | Harness: Moon at 1%, 2%, 5% finishes |

**In game time:** the violet Aquarius Sigil is ≈ 8,100 bets ≈ 810,000 game-seconds ≈ 9.4 game-days at one hardware
credit; the seven complete planets, ≈ 3.3 million bets ≈ 10 game-years at one credit — fewer with more credits.

---

## 10. Order of work

Each unit is a stage → ask → commit loop on this branch.

1. **Data and rule** — P-GN.1 and P-GN.2, with the harness reproducing Pr-2 to Pr-4.
2. **State and hook** — P-GN.4 and P-GN.3, the checkpoint restore and the pre-genesis reset.
3. **The stop** — P-GN.5.
4. **The panel** — P-GN.6.
5. **Measurement** — a numbered protocol for Pr-1 in game, read afterwards from the logs and the checkpoint.
6. **Review with the developer** — repetition incentives, unlock alternatives and bots (D-GN.18), then close-out.

A win is a contiguous roll range (`DiceEngine.GetWinningRange`), and only a range of **at least nine consecutive
rolls** holds every digital root. Below 9%, some units digits can never be the root of a winning roll — at 1%, Low wins
only on 00 and High only on 99, both root 9 — so every two-digit target whose units digit is outside that set is
unreachable. **Measured:** the Moon sigil at 1%, 2% and 5%, Low and High, 20 runs each — **none finished in 3 million
bets**; at 9%, both sides finish a pass in ~56,000. Single-digit and round-ten targets are unaffected (V absorbs the
roll), so the Aquarius Sigil is reachable at every chance.

Not yet decided. A related lever, if switching Low ↔ High does not restart a pass (D-GN.9 names only the chance):
at 5% the two sides together cover all nine roots once each, which would make a two-digit target cost ≈ 4,500 bets
instead of 8,100 — a manual-play advantage the balance does not intend.

---

## Appendix A — the seven planetary squares (reference copy)

Copied from the source's `CUADRADOS` (`web/sigilos.html`, identical to `Numerologia_Grafica.gs`). **Superseded by the
C# declaration once it exists.**

```
SATURN 3        JUPITER 4         MARS 5
 4  9  2         4 14 15  1        11 24  7 20  3
 3  5  7         9  7  6 12         4 12 25  8 16
 8  1  6         5 11 10  8        17  5 13 21  9
                16  2  3 13        10 18  1 14 22
                                   23  6 19  2 15
SUN 6
  6 32  3 34 35  1
  7 11 27 28  8 30
 19 14 16 15 23 24
 18 20 22 21 17 13
 25 29 10  9 26 12
 36  5 33  4  2 31

VENUS 7
 22 47 16 41 10 35  4
  5 23 48 17 42 11 29
 30  6 24 49 18 36 12
 13 31  7 25 43 19 37
 38 14 32  1 26 44 20
 21 39  8 33  2 27 45
 46 15 40  9 34  3 28

MERCURY 8
  8 58 59  5  4 62 63  1
 49 15 14 52 53 11 10 56
 41 23 22 44 45 19 18 48
 32 34 35 29 28 38 39 25
 40 26 27 37 36 30 31 33
 17 47 46 20 21 43 42 24
  9 55 54 12 13 51 50 16
 64  2  3 61 60  6  7 57

MOON 9
 37 78 29 70 21 62 13 54  5
  6 38 79 30 71 22 63 14 46
 47  7 39 80 31 72 23 55 15
 16 48  8 40 81 32 64 24 56
 57 17 49  9 41 73 33 65 25
 26 58 18 50  1 42 74 34 66
 67 27 59 10 51  2 43 75 35
 36 68 19 60 11 52  3 44 76
 77 28 69 20 61 12 53  4 45
```
