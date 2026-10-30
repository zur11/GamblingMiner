# Graphic Numerology — Plan

**Series note:** a full plan, not a mini-plan. It pauses the run of technical mini-plans (25–29) to open a new goal
in `PRIVATE_ROADMAP.md`: bringing the developer's other project, *Numerología Gráfica*, into the game.

**Status:** 📝 **SPECIFIED 2026-10-28.** The design is decided, D-GN.1–D-GN.20: the zodiac sigils (§4), the drawing
rule, balanced at the same cost at every chance (§5–§6), the Low/High choice (§6.3), the order of the sigils (§7) and
the tracking of bets, SC and each set's figures (§8). **§8's integration, P-GN.1–P-GN.7, is approved (2026-10-29)**,
with P-GN.6 an overlay like the Martingale calculator. §9 registers the predictions and §10 the order of work.
**Unit 1 is done (§10.1):** the squares and the rule are in `Scripts/Numerology/`, with each set's own figures
(D-GN.20), and `Tools/SigilHarness` confirms Pr-2 to Pr-4. **Unit 2 is done (§10.2):** `PlayerSigilProgress` holds the
progress, fed by every player bet and committed by the block checkpoint. **Unit 3 is done (§10.3):** "Stop on Sigil Set Completed". **Unit 4 is built (§10.4):** the overlay, awaiting the developer's first look. **Unit 5 is measured (§10.5): Pr-1 and Pr-5 to Pr-9 hold** (Aquarius violet at 8,297 bets), with three findings
recorded there. **D-GN.22 is built (§10.6):** the colour is the set's progress in 95 steps, the canvas draws one
round at a time, and a strip previews the next pass; the developer's look (L1–L4) passed, bar a clipped warning, now
fixed. **Next: unit 6.**

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
the top.** A sigil drawn at 1% is drawn once; at 95%, 95 times. *(Developer, 2026-10-28.)* **Amended by D-GN.22
(2026-10-29): the colour is the set's PROGRESS on the same rainbow, not the chance of its latest pass** — the two agree
on a set's first pass only at 95% and 1%; the chance now decides how far each pass moves the colour.

**D-GN.8 = B, balanced — a set needs as many complete passes as the chance it is drawn at.** A pass completed at chance
`c` adds `1/c` of the set; **the set is finished when the sum over its completed passes reaches 1**, and then it is
violet. ~~Under a mix of chances, the colour of a pass in progress is its chance's colour, and completion is shown
separately (e.g. a counter).~~ *Superseded by D-GN.22: the colour **is** the completion.* Finished sets are skipped: the passes cycle through the unfinished sets in order until all
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
*Built (unit 2) as one call **inside** `UserStatsService.OnBetExecutedRegisterBet` rather than two beside it: both
paths funnel through that method, and its only other route, `RegisterSource`, has no callers by design — so one line
covers both, and a third caller cannot forget it (the reasoning `CaptureCheckpoint`'s balance flush already uses).*

**P-GN.4 — a state owner, committed only by the checkpoint.** A new autoload (the 21st) owns the progress in memory
and fires a typed event when a hit, a set or a sigil changes something (Pattern 1) — **not on every bet**, although V
moves on every bet; a V readout samples it (§29.13). **It writes no file of its own:** its state is captured into, and
restored from, the block checkpoint, with a pre-genesis default (Pattern 2's first two questions); having no own file
answers the third (`WipeWorld`) and the eager-writer guard (`WorldWriteGuard`) by construction. *Why no own file:*
mini-plan 08 measured a per-bet balance write at 66% of a bet's cost. Its registration slot is chosen deliberately
before `BlockSessionCheckpointService` (Important Patterns §5).
*Built (unit 2) as a **static class, `PlayerSigilProgress`, not an autoload**. An autoload the checkpoint restores
must be declared before it, at #13, which would renumber #13–#20: eight `BootProbe.AutoloadStarts` indices, the
CLAUDE.md and SERVICES.md tables, and every document that cites "#13". A static owner the checkpoint pushes into is
the project's existing alternative (`NetworkRoot`'s world state; `NetworkPopulationScheduler`): no `_Ready`, so no
order to break, and Godot-free, so the harness tests it. Everything else in P-GN.4 holds as written.*

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

**D-GN.20 — every set keeps its own figures, and the overlay compares them.** For each set: the **passes** it took to
reach violet, the **bets** placed and **SC** wagered while it was the set being drawn, the **game time** between those
bets, and the **restarts** a chance change cost it. The sigil's figures are the sum of its sets, and a completed sigil
keeps each set's figures, so the sets can be compared after the fact. The overlay shows, **live**, each set's and the
sigil's completion and what remains, in percent, and compares the sets with each other — which took more passes,
bets or time — as shares of the sigil. *(Developer, 2026-10-29.)* Completion counts completed passes only, since the
pass in progress can still be lost to a chance change (D-GN.9); the overlay shows that pass separately.

**D-GN.21 — the overlay shows V's arithmetic, bet by bet, so the player can follow it.** Each bet appears as an
operation: the current V, a **−** (loss) or **+** (win), the roll's digital root **in red on a loss and in green on a
win**, and the resulting V — which is then the left-hand side of the next bet's operation, so the line reads as a
chain. *(Developer, 2026-10-29; built in unit 4, the overlay.)* Details to settle when it is built: at 1–9% a win adds
`L + r`, not `r` (D-GN.13), so the operation must show the streak too (e.g. `V + (L + r)`); a two-digit target is
checked against V **before** the bet beside the root (D-GN.12), so the readout should mark a hit on the spelled
number, not on the new V; and at autobet rate bets arrive far faster than the eye, so the chain is a **sampled**
readout (§29.13, `ReadoutSampling`), never a log of every bet.

**D-GN.22 — the colour shows the set's progress, and the canvas draws one round at a time.** *(Developer, 2026-10-29,
from the first look at phase A; amends D-GN.7.)* Four parts:

1. **Colour = progress, in 95 numbered steps, 1 red … 95 violet:** `step = ⌊95 × Σ passes / chance⌋`, the whole 95ths
   completed, computed from the **same exact fraction** that decides a set is finished (D-GN.8) — so step 95 means
   finished and nothing else, and a step never goes down. **The chance still decides the colour, through how far a
   pass moves it:** a pass at `c` adds `95 / c` steps — 1 at 95%, 1.0106 at 94%, 1.9 at 50%, 9.5 at 10%, 95 at 1% — on
   top of whatever the set already holds, at whatever chances those passes were drawn. *The developer first described
   a pass at 94% adding 2/95 (a linear `(96 − c) / 95`); shown that it makes ~48% about 24× cheaper than either
   extreme — the rejected alternative A of D-GN.8 — they kept `1 / c` (2026-10-29).* Rounding **down**, not up: up
   showed a single 94% pass a whole step past a 95% one for 1% more progress, and needed a cap to keep 95 for finished.
2. **One round at a time.** A round is one pass on every unfinished set, in D-GN.8's cycle. During a round the sets
   already passed show in their new colour, the set being drawn shows its silhouette faint with the numbers hit solid
   on top, and the rest are clear; when the round ends the canvas clears and the next one draws one colour further
   along. **Finished sets stay drawn, violet.** No state is stored for it: within a round the sets already passed hold
   one pass more than the rest (`SigilRules.PassedThisRound`).
3. **The preview of the next pass.** Under the canvas, a strip of the 95 steps: bright up to the set's step, half-bright
   to where a pass at the next bet's chance would take it (gold mark), dim beyond. The next bet's chance is the running
   autobet's (D-M2.8), else the panel's — so moving the chance slider and opening the overlay previews that setup.
4. **The warning names what is actually lost** (D-GN.9 unchanged): when a pass is partly drawn and the next bet's chance
   differs, *that pass's* numbers are lost; every completed pass stays, and the new chance adds its own share on top.

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
| Pr-5 | *(registered 2026-10-29, unit 5)* At a constant 95%, a violet Aquarius has **exactly 95 passes in each set, all at 95%, and 0 restarts** | Any other count ⇒ the in-game rule differs from the harness's; the harness already shows exactly `c` |
| Pr-6 | *(unit 5)* After a restart, the progress restored at boot is **identical** to the last block's checkpoint of the previous session — not to where the run ended, since bets after the last block are reverted by design | Any difference ⇒ a capture or restore bug; it blocks the close-out |
| Pr-7 | *(unit 5)* With "Stop on Sigil Set Completed" ON, an autobet run stops **on the bet that completes a set pass** — after ~28 bets on average at 95% (3 hits × ≈ 9.5 bets), and almost never after more than 200 | A stop on any other bet, or no stop ⇒ a wiring bug in the stop |
| Pr-8 | *(unit 5)* With a flat bet of 0.01 SC, a completed sigil's SC wagered is **exactly 0.01 × its bets** | Any other figure ⇒ the D-GN.19 tracking drifts |
| Pr-9 | *(unit 5, added 2026-10-29 before any run)* A set's game time grows by **`100 / credits` game-seconds per bet** — 100 s at 1 credit, 10 s at 10, 2 s at 50 — whatever the DEV time scale; the idle time between two runs is never counted | Off by more than 2% ⇒ the D-GN.20 time tracking does not measure what it claims; idle time in a set ⇒ its "game time passes only as bets are simulated" premise is false |

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

### 10.1 Unit 1 — done (2026-10-29)

**Built:** `Scripts/Numerology/` — `PlanetarySquares` (the one declaration of the squares and their sigil names; each
square asserts it is magic when built, so a transcription typo throws at first use), `SigilRules` (the rule, with
`ApplyBet` returning a `SigilBetOutcome` flag set: hit, silhouette, line, set pass completed, set finished, sigil
completed, pass restarted) and `SigilProgressState` (plain data for the checkpoint in unit 2). A set is finished when
`Σ passes / chance ≥ 1` **in exact rationals** — in floating point, three passes at 3% sum to 0.999… and the set would
never finish. `dotnet build` clean; locale detector at its baseline, 17 / 0 / 0 / 0.

**Harness** (a scratch console project compiling the game's own `Scripts/Numerology/*.cs`, `Money.cs` and
`DiceEngine.cs`, so every roll is the game's; not in the repository): **all checks pass.**

- The squares: the seven magic constants; Aquarius, Capricorn and both Moon paths cell by cell against the source
  guide; Capricorn's target order 1-4-7-2-5-8-3-6-9; the day/night reflection (§3.4) for all seven squares.
- The rule: the first roll sets V; a chance change restarts the pass and keeps V; the D-GN.12 worked example (V 3 +
  roll 8 spells 38, miss, V → 2; V 2 + roll 73 spells 21, hit); D-GN.19 counts bets and SC.
- **Exact completion:** Aquarius at a constant `c` takes exactly `3c` set passes at 3%, 7%, 50% and 95%.
- **Pr-2 holds:** hits / wins = 0.1113 over 300,000 wins at 50% (1/9 = 0.1111).
- **Pr-3 and Pr-4 hold:** all fourteen sigils, in unlock order, at 1% Low, 1% High, 5% Low, 5% alternating, 50% Low
  and 95% High, 6 runs each (12.5 s). Every sigil finishes at every setting, and from Mars up every cell is within
  15% of the prediction (worst 14.1%). At 5% and above the spread is within ~8%; the widest cells are at 1%, where a
  violet sigil is a single pass and 6 runs leave ~6% (1σ) of sampling noise. Moon, for example: predicted 533,700 —
  519,190 · 554,528 · 550,444 · 541,680 · 532,951 · 531,071 across the six settings.

**Unit 1b — the per-set figures (D-GN.20) and the harness in the repository (2026-10-29).** `SigilSetProgress` now
carries each set's bets, SC, game time, passes and restarts; `CompletedSigilRecord` keeps the sets, its start and its
completion; `SigilRules.SigilCompletion` gives the live percentage. A bet's game time is the gap since the previous
bet, credited to the set it lands on — game time passes only as bets are simulated (the 100-second rule), so the gaps
add up to the sigil's time.

The harness is now **`Tools/SigilHarness`** (`dotnet run -c Release` in its folder), excluded from the game build by
`GamblingMiner.csproj`'s `Compile Remove="Tools\**\*.cs"` and from the Godot editor by a `.gdignore`. Two changes on
the way in:

- **The tolerance of the Pr-3 / Pr-4 check was the thing that was wrong (Standing Convention 16).** A fixed 15% was
  ~1.5σ for Aries at 1%, where a violet sigil is one pass: it passed twice by luck (worst 14.1% both times) and would
  have failed about half its runs with the rule correct. It is now 4σ, with σ computed for each cell from its own
  expected noise (each target hit `c` times; a geometric's sd ≈ its mean). Four runs: worst 2.11σ, 2.72σ, 2.38σ,
  2.73σ, each time in a different cell — noise, not bias.
- **A new check, 6:** an Aquarius run at one bet per 100 game-seconds, with a forced chance change: the sets' bets,
  SC and game time add up exactly to the sigil's, and every set ends at 100%. One run: 48 passes per set, 1 restart,
  bets split 32.7% / 31.6% / 35.7%, 2.96 / 2.86 / 3.24 game-days.

### 10.2 Unit 2 — done (2026-10-29)

**Built:** `Scripts/Numerology/PlayerSigilProgress` — the static owner (P-GN.4, as amended): the live state, a
`Changed` event and `LastOutcome`. **Fed** by one line in `UserStatsService.OnBetExecutedRegisterBet` (P-GN.3, as
amended). **Committed** by `BlockSessionCheckpointService`: `Snapshot.SigilProgress` is captured as a copy at every
block, restored at boot, and reset pre-genesis; a checkpoint from before this field existed restores fresh progress,
which is the truth for a player who has drawn nothing — so **no format bump, and R4's world stays playable.** Docs:
`SERVICES.md` has its section; CLAUDE.md's autoload table has its row.

**Harness, new checks 7 and 8 — all pass:**

- **The checkpoint round trip.** 3,000 bets registered, `Changed` fired 177 times and never for a bet that changed
  nothing; the captured copy does not move when 500 more bets change the live state; restoring it reproduces the
  block's state byte for byte (as JSON); a null snapshot restores fresh progress.
- **Cost:** **39.1–47.8 ns and 0.00 bytes allocated per bet** (5,000,000 bets at 50%, two runs). Measured in the
  harness, outside Godot — **not measured in game.**

**Not yet verified in game:** that a restart after a mined block restores the progress, and that a restart before any
block resets it. The overlay (unit 4) is what will make it visible; the measurement protocol (unit 5) includes it.

### 10.3 Unit 3 — done (2026-10-29)

**Built: "Stop on Sigil Set Completed" (P-GN.5, D-GN.10).** `BettingStrategyConfig.StopOnSigilSetCompleted`, with a
new `StopReason.StopOnSigilSetCompleted` (appended, so no existing reason changes number). Every place that rebuilds a
config field by field carries it — DiceGame's player, bot (forced OFF: a bot draws no sigil) and calculator builders,
the panel's `BuildConfig`, and the saved-strategy repository (a strategy saved before it loads with it OFF). The
panel's toggle, **"Stop Set: ON/OFF"**, sits in the grid's third row beside "Bankroll reload", where the row already
existed, so the panel does not grow; it locks during a run and in bot strategy mode, like Stop Block.

**Where it fires — both paths, after the bet's mining attempt, from the session (D-M2.8):** `SimulationService`'s
player autobet tick and `DiceGame.ExecuteBet`, each reading the outcome `PlayerSigilProgress` recorded for that bet
when it was registered. After the mining attempt, so a block stop on the same bet keeps its own reason and its
calendar freeze. A manual burst ends with the session, as it does on any stop.

**Two differences from Stop Block, deliberate:** it sets **no manual-bet gate** — the gate exists so a block stop is
not clicked past by accident, and a set stop only has to end the run at a set boundary; and it does **not freeze the
calendar** — that freeze puts the clock on a block's instant, and a set boundary has none, exactly like the profit and
loss stops. Docs: `ARCHITECTURE.md` (the config's stops), `GLOSSARY.md`.

**Not testable in the harness** (the stop lives in Godot code); verified by `dotnet build` only. The in-game check is
in unit 5's protocol: a run at a fixed chance with the stop ON must end on the bet whose outcome carries
`SetPassCompleted`.

### 10.4 Unit 4 — the overlay, built (2026-10-29); awaiting the developer's first look

**Built: `Screens/SigilOverlay/`** — `SigilOverlay` (the controller) and `SigilCanvas` (the drawing), instanced in
`DiceGame.tscn` beside the Martingale calculator and opened by a **"Sigils"** button placed in the scene file
(not in code, per the roadmap's "UI built in scenes"), under DiceGame's BTC wallet line in the navigation column
(x 1392, y 382–420 — the free band between that line and "Current app time" at y 467). Chapter 29 was read first;
the overlay has no `ScrollContainer`: two Pattern-B labels (the sets table and the completed sigils) scroll
themselves, each in its own label (§29.2), keeping their scroll across refreshes with a padded last line (§29.3),
and the Close button sits in a fixed footer, 100 px above the canvas bottom (§29.10, §29.11).

- **The drawing (`SigilCanvas`):** the square on a day, night or eclipse ground — **placeholder colours** for the
  textures still to be made — with its numbers faint; each set in the colour of its latest completed pass, violet
  when finished (D-GN.8); the set in progress as a faint silhouette in its pass's colour with the lines already hit
  solid on top; green and red circles at each drawn set's start and end; the next target ringed. Colour:
  `hue = (95 − c) / 94 × 0.76` — 95% red, ~48% green, 1% violet. **Repainted only on `PlayerSigilProgress.Changed`.**
  *(Colour and layout revised by D-GN.22 — see §10.6.)*
- **The figures:** the next target and what it needs; V and the loss streak; the pass in progress (and that a chance
  change loses it); the sigil's completion and what remains (D-GN.20); bets, SC wagered and game time; the planet's
  running total while its nocturnal sigil is drawn; a table of the sets — passes, completion, bets and their share,
  game-days, restarts; and the completed sigils, with a planet's total once both are drawn (D-GN.19).
- **The V chain (D-GN.21):** the latest 8 bets as `V − r = V'` (loss, root red) or `V + r = V'` (win, root green),
  `V + (L + r)` with the streak at 1–9%, "spells 38, target 21" for a two-digit target, and **HIT** on a hit. Fed by a
  16-entry ring in `PlayerSigilProgress` that `SigilRules.ApplyBet` fills through a new `out SigilStep` (O(1), no
  allocation); emptied by a restore. Each set also records `LastPassChance`.
- **Cost:** hidden, it does nothing (`SetProcess(false)`). Open, it repaints at 4 Hz and only if the bet count moved
  or the drawing changed; counters floored by `CounterQuantizer` (§29.13), SC and game-days to the decimals the
  quantum can carry. Recorded in §38.5's table (26 overrides now).

**Verified:** `dotnet build` clean; locale detector 17 / 0 / 0 / 0; the two live-clock detectors 0 / 1 (the overlay
renders no clock); the harness passes, with new checks pinning `SigilStep` to the worked examples (`3 + 8 = 2, spells
38`; `2 + 1 = 3, spells 21, HIT`; a loss; a 5% win adding the streak) and the ring (8 latest, each starting where the
previous ended; a restore empties it). *One harness check failed on its first run, and the check was the fault
(Standing Convention 16): it read the ring after a restore that empties it by design.*

**Not yet seen in game.** Layout, fonts and colours are unverified until the developer opens it. **The developer
deferred that look to just before the close-out (2026-10-29).**

### 10.5 Unit 5 — the measurement protocol (2026-10-29)

**Instrumentation, so the run can be read afterwards** (the developer watches nothing while it goes). `GD.Print` lines
in `godot.log`, each carrying `SigilRules.Describe` — the sigil, bets, SC, V, the loss streak, the target, the pass in
progress, and every set as passes / completion / restarts / bets / game-seconds:

| Line | When | Build |
|---|---|---|
| `[Sigil] checkpoint — …` | every block capture, beside `[Checkpoint] CAPTURED` | always |
| `[Sigil] restored at boot — …` / `[Sigil] pre-genesis reset at boot — …` | at boot | always |
| `[Sigil] SIGIL COMPLETE — …` | a sigil completes; each set also lists its passes by chance (`95@95`) | always |
| `[Sigil] STOP on set completed (autobet \| DiceGame) — …` | the set stop fires | always |
| `[Sigil] pass complete — …` / `[Sigil] pass restarted (chance changed) — …` | each pass, each restart | DEBUG only — at 95% a line every ~28 bets |

The harness checks `Describe` carries the figures, in invariant format.

**The protocol — two sessions, each run set by a bet count or by the stop, never by watching.** *Revised before any
run (2026-10-29) at the developer's request to change hardware too: phase M1-B and Pr-9 were added, and each set's
game-seconds joined the log line. Revised twice more, still before any run, at the developer's direction: the speeds
are named by the selector's own labels, 100X–9000X (the earlier "×10" and "×30" were 1000X and 3000X), and M1 now
starts at the base pace — 1 credit at 100X — and accelerates credits and speed phase by phase.* Hardware is set with
DiceGame's **DEV credits selector** (beside "Credits in use"; it writes through the shop's own calls) and the speed
with the **DEV time-scale selector**. One credit is one bet per 100 game-seconds tick, so bets land `100 / credits`
game-seconds apart; the time scale changes how fast that happens in real time — one credit is one bet per real second
at 100X, so the demand is `credits × scale / 100` bets per real second — **not** the game time between bets.

**Why phase A is 600 bets:** at 1 credit and 100X the game makes one bet per real second, so the 12,000 bets M1 needs
would take 3 h 20 min. 600 bets are 10 real minutes — exactly the canonical autobet target, *10 real minutes = 16 h
40 m in-game* (600 × 100 game-seconds) — and the faster phases carry Aquarius to completion. Pr-1 counts bets, not
time, so it is unaffected by the phases as long as the chance stays at 95.

*Session M1 — the strategy, set once (steps 1–3).*

1. In the Godot editor, run the game (F5) — a fresh process, so the boot line records where Aquarius starts.
2. Open **DiceGame** with the **player** as the active node.
3. Strategy: **Bet amount `0.01`** · Increase on loss **blank** · Increase on win **blank** · Stop on profit **blank**
   · Stop on loss **blank** · **Stop Block OFF** · **Stop Set OFF** · **Auto Recharge ON** · **Chance `95`** · **Low**.
   It stays like this for all of M1; only the hardware, the speed and the bet count change.

*Phase A — the base pace (Pr-9 at 100 s a bet).*

4. DEV credits **`1 cr`** · DEV time scale **`100X`** · **Number of bets `600`**. Press **AUTO**. It ends by its
   count after ~10 real minutes (16 h 40 m in-game).

*Phase B — ten times the credits, ten times the speed (Pr-9 at 10 s a bet).*

5. When the button reads AUTO again: DEV credits **`10 cr`** · DEV time scale **`1000X`** · **Number of bets
   `3000`**. Press **AUTO**. ≈ 100 bets per real second, so ~30 seconds.

*Phase C — the top (Pr-9 at 2 s a bet; Aquarius completes here or in B).*

6. When it reads AUTO again: DEV credits **`50 cr`** · DEV time scale **`9000X`** · **Number of bets `8400`**
   (12,000 in all). Press **AUTO**. The demand is ≈ 4,500 bets per real second; the simulation may throttle it, which
   changes nothing measured here.
7. When it reads AUTO again, **close the game window**.

*Session M2 — Pr-6, Pr-7.*

8. Run the game again (F5) and open **DiceGame**. Check the DEV credits selector reads **`50 cr`** and the time scale
   **`9000X`**; set them if not.
9. The same strategy, except **Number of bets `0`** and **Stop Set ON**. Press **AUTO**. It should stop by itself
   within seconds. Then **close the game window**.
10. Say "done". **Claude reads** `user://logs/godot.log` (M2) and the log Godot rotated aside at M2's launch (M1).

If a board vote opens, play pauses until the player votes, as always: vote, then press AUTO again.

**How each prediction is read:**

- **Pr-1:** M1's `SIGIL COMPLETE — Aquarius Sigil: bets=N`. If M1's boot line shows Aquarius **fresh** (bets=0, every
  set 0p), N is compared with 7,300–8,900 directly. If not, the bets after boot are compared with what the boot state
  had left: `Σ sets 95·(1 − completion) passes × 3 hits × 900/95 bets`.
- **Pr-5:** the same line, every set `95p r0 … 95@95` (fresh start only); and **no** `pass restarted` line in M1.
- **Pr-8:** the same line, `sc = 0.01 × bets` exactly.
- **Pr-6:** M2's `restored at boot` line, after the dash, is identical to M1's **last** `[Sigil] checkpoint` line.
- **Pr-7:** M2 holds exactly one `STOP on set completed (autobet)` line, and the `pass complete` line printed on the
  same bet carries the same state; the bets between the boot line and the stop are counted from the two `bets=` fields.
- **Pr-9:** the phases end on the 600th, 3,600th and 12,000th bet after boot. A `pass complete` line's position is
  the sigil's `bets=` (plus Aquarius's N once Capricorn is being drawn), so each line falls in a known phase. Between
  two lines of the same sigil in the same phase, `Δ seconds / Δ bets` — seconds summed over the sigil's sets — should be
  **100 s in A, 10 s in B, 2 s in C**. Across a phase boundary, the idle time between the two runs must **not** appear
  in any set.

#### Results — M1 and M2 (run 2026-10-29)

Read by Claude from `godot2026-10-29T19.22.09.log` (M1, rotated aside at M2's launch), `godot.log` (M2) and
`session_lifecycle_trace.csv` (each run's start and stop in game time). M1's boot line shows Aquarius **fresh**
(`bets=0`, every set `0p`), so Pr-1 is read directly. Neither session holds a `pass restarted` line.

| | Predicted | Measured | Verdict |
|---|---|---|---|
| Pr-1 | 8,100 bets, one run within 7,300–8,900 | **8,297** (+2.4%, ≈ +0.8σ) | holds |
| Pr-5 | every set `95p r0 … 95@95` | `[95p r0 2806b 95@95] [95p r0 2929b 95@95] [95p r0 2562b 95@95]`; 0 restart lines | holds |
| Pr-8 | `sc = 0.01 × bets` | `sc=82.97000000` for 8,297 bets | holds |
| Pr-6 | M2's restore = M1's last checkpoint | identical after the dash: `bets=502 … [7p 7.4% r0 156b 15500s] [6p 6.3% r0 194b 19400s] [6p 6.3% r0 152b 15200s]` | holds |
| Pr-7 | stops on the pass-completing bet, ~28 bets | one `STOP on set completed (autobet)` line, same state as the `pass complete` line at `bets=523` — **21 bets** after the restore; trace reason `StopOnSigilSetCompleted` | holds |
| Pr-9 | 100 / 10 / 2 s a bet within a phase; idle time never counted | every Δ between two lines of one phase is **exactly** 100.00, 10.00 or 2.00; the ~3 real hours between phases A and B count **0** | holds, one residual (2 below) |

**Three findings beyond the predictions:**

1. **Phase B ran 1,000 bets, not 3,000** — inferred from the data, not asked: the trace has B lasting 10,013.8
   game-seconds (18:25:48.042 → 21:12:41.825), 1,000 bets at 10 s plus finding 2's tail, and the pace changes from
   10 s to 2 s between Aquarius's lines at bets 1,598 and 1,617. The boundaries were 600 / 1,600 / 10,000, not
   600 / 3,600 / 12,000. No prediction depends on it: Aquarius completed in C either way, and Capricorn's last line
   (`bets=1691`) fits 10,000 in all.
2. **A run's stop carries up to one frame of world time into the next run's first gap.** The first bet of B came
   11.35 s after A's last (not 10 s), the first of C ≈ 15.8 s after B's last (not 2 s). The surplus is one rendered
   frame at the old run's scale: the trace stops A 1.347 game-seconds after its 600th bet (≈ 13.5 ms at 100X), B
   13.783 s after its last (≈ 13.8 ms at 1000X) and C 171.4 s after its 8,400th (≈ 19 ms at 9000X). That frame is
   world time the bots really simulated, not idle time, so Pr-9's premise stands; the residual is at most one frame
   per run boundary — 15 s of Aquarius's 83,309. **Recorded, not fixed.**
3. **The violet Aquarius was reverted by the restart — Pattern 2 as written, with a consequence for unit 6.** M1's
   only checkpoints were at Aquarius bets 152, 497 and 502, all in phase A; no capture happened in the ~7.5 game-hours
   of B and C (at A's rate, 3 in 16.7 h, about 1.3 were expected, so none is a ~26% outcome, not an anomaly). M2
   therefore restored `bets=502`, and 8,297 bets of finished sigil are gone. Blocks come per game-time while bets per
   game-time scale with credits, so **the more hardware, the more sigil progress rides uncommitted between blocks** —
   at 50 credits, thousands of bets. A question for unit 6: whether the overlay should show how much is uncommitted.

### 10.6 The developer's notes on phase A — D-GN.22 built (2026-10-29)

From a screenshot taken mid-run, at Aquarius bet 600. **What was wrong, and why:**

- **"The lines stay red."** All 23 passes were at 95%, and the colour was the chance of a set's latest pass (D-GN.7),
  so it could not move without a chance change. → D-GN.22.1, colour = progress.
- **"Why white dots?"** The faint silhouette of the set in progress drew its start/end circles at 30% alpha, which on
  the day ground reads as white. Now a circle is drawn only when its number is hit, always solid.
- **The faint silhouette was invisible:** the set in progress was also drawn solid in its previous pass's colour, on
  top of it. The round clear (D-GN.22.2) removes the old drawing, so the silhouette shows.
- **The V chain's last line was clipped:** 9 lines of ~28 px in a 240 px label. Now 270 px.
- **The sets table's header was scrolled off:** three padding lines under a three-row table made it scroll by their
  own height. `SetTextKeepingScroll` now takes the padding per label — one line for the sets.

**What was built.** `SigilRules.ColorStep` / `ColorStepAfterPass` / `PassedThisRound`, reading `IsFinished`'s exact
fraction (now `Progress`); `SigilCanvas` drawing by round and by step (`ColorForStep` replaces `ColorForChance`); a new
`SigilColorStrip` (no `_Process`: it repaints when `SetSteps` changes a step); a preview label; a **Colour** column in
the sets table; DiceGame's `NextChanceProvider`. **Layout:** the canvas went from 760 to 620 px to make room for the
strip (40) and the preview (84) beneath it in a left column of the same 760 px; the status label 220 → 190, the chain
240 → 270. The right column's minimum is 756 px, under the canvas column's 760, so the panel and its footer do not move.

**Verified:** `dotnet build` clean; locale detector 17 / 0 / 0 / 0; the harness passes with a new section 9: the worked
steps (1 at 95% → 1; 1 at 94% → 1, 94 of them → 95; 1 at 50% → 1, 2 → 3; 10% → 9; 2% → 47; 1% → 95; 2 at 3% → 63,
3 at 3% → 95 exactly; 1/2 + 1/3 + 1/7 + 1/43 = 1805/1806 → 94, never 95), the preview, and — over 162 passes at mixed
chances — steps never falling, 95 ⇔ finished, every preview coming true, and the round invariants holding. **Not yet
seen in game:** part of the deferred first look.

**The look protocol (L1–L4), predictions first.** A visual check, so the developer reports with screenshots; nothing
here needs a log. The world boots at M1's last checkpoint (M2 mined no block): Aquarius `bets=502`, sets `7p / 6p /
6p` at 95%, set 2 next, target 4, no pass in progress. The game-space predictions below follow from that state.

- **L1 — at boot, chance 95, before any bet.** Set 1 (1-2-3) drawn solid in step 7 (red), green circle on 1, red on 3;
  sets 2 and 3 clear; the ring on 4. Table colours 7 / 6 / 6. Strip: white mark at 6, gold at 7. Preview: *"Set 2:
  colour 6 of 95 · next bet at 95%: a pass at that chance takes it to 7"*. No warning.
- **L2 — chance moved, no bet.** At 10%: *"takes it to 15"* (6 + 9.5 = 15.5), strip half-bright from 7 to 15. At 1%:
  *"takes it to 95"*. No warning (no pass in progress).
- **L3 — watched live at 1 credit, 100X, 95%.** About one bet a second, ~28 a pass. (a) The first hit on 4: set 2's
  silhouette faint in step 7, a solid green circle on 4. (b) The hit on 5: the 4→5 segment solid. (c) The hit on 6:
  set 2 solid in step 7 with its red circle; the ring moves to 7. (d) Set 3 the same. (e) **When set 3 completes the
  canvas clears entirely** (all three sets at 7 passes) and set 1's next silhouette comes in step 8. Throughout, the V
  chain shows all 8 lines and the sets table its header.
- **L4 — the warning.** Stopped with a pass in progress, the chance set to 50%: an orange *"Betting at 50% restarts
  this pass: its N number(s) hit at 95% … lost. The set keeps every pass already completed."* Back to 95%: no warning,
  *"the pass in progress takes it to …"*.

**Results (developer, 2026-10-29, eight screenshots):** **L1, L2 and L3 hold as predicted** — the boot drawing, table
and strip (6 → 7); the preview at 10% (15) and 1% (95); the silhouette on the hit on 4, the solid 4→5 segment, and the
clear canvas once set 3 completed, set 1 then drawing in step 8 with every set at 7 / 7.4%. The V chain showed all 8
lines and the table its header in every shot. **L4 failed on layout, not on logic:** the warning was right (shown at
50% with 2 numbers in progress, gone back at 95%) but its end was cut — the first sentence wrapped to two lines in the
620 px column, so the text needed four lines in a label sized for three. Fixed by shortening both (*"at 50%, the next
pass takes it to 8"*; *"Betting at 50% restarts this pass and loses its 2 numbers hit at 95%. Completed passes
stay."*) and moving 6 px from the strip (40 → 34) to the label (84 → 90). **Not re-seen in game.**

**Deferred to just before the close-out (developer, 2026-10-29):** the first look at the overlay (§10.4).

*Removed from here on 2026-10-29: two paragraphs of the old "§6.2 Open — D-GN.13" text. That rewrite replaced only the
section's heading, its body stayed behind, and every later section was inserted above it, so it drifted to the end of
the plan still reading "not yet decided". Its content is in §6.2 ("What it replaced") and §6.3, both decided.*

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
