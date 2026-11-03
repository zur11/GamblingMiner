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
fixed. **Unit 6's review is done (2026-11-01):** it produced the closing decisions D-GN.23 to D-GN.27 (§8.1) — violet
×100, one of each sigil, one slot, the levels — and `AIHelperFiles/sigil_benefits-proposal.md` for the next plan.
**Unit 7 is done (§10.7):** ×100 and the levels. **Unit 8 is done (§10.8):** the collection and the slot, in state and
rules; the harness confirms Pr-12, Pr-13's round trip and Pr-14. **Unit 9 is built (§10.9):** the collection's screen,
with the DEV Acquire and Play buttons, not yet seen in game. **The developer then added D-GN.28 to D-GN.38 (§8.2,
2026-11-02):** a complete sigil cannot be played, a temporary notice at level 1 on a pair, the planetary sigil's
activation, what happens when a sigil completes, and stops on each level acquired. **Unit 10 is built (§10.10), not yet seen in game;** Pr-18
was refuted as written, the model being what was wrong. **The developer then overruled part of it (D-GN.34 to D-GN.38): the price is now a
fractal of the zodiac sigils, 100 × c whole passes for all 95 levels, each set carrying its own level.** **Next: unit 10b builds those; then unit 11, the measurement, Pr-10 to Pr-22 in
game with the first look at units 7, 9, 10 and 10b; then close-out.**

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

*(D-GN.23, 2026-11-01: every pass count and cost below is now ×100 — the balance is unchanged, the size is not.)*

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

*(D-GN.26, 2026-11-01: the player now chooses what to draw, so the order below — D-GN.16's and D-GN.18's — no longer
binds. A planet's two sigils still make its complete figure, D-GN.27.)*

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

### 8.1 The closing decisions — D-GN.23 to D-GN.27 (developer, 2026-11-01)

The developer's five changes to finish this plan, after the look at D-GN.22. The benefits these levels will unlock are
a proposal for the next plan: `AIHelperFiles/sigil_benefits-proposal.md`.

**D-GN.23 — violet costs exactly 100× what it did.** A pass at chance `c` now adds `1 / (PassesPerChance × c)` of its
set, the symbol being `SigilRules.PassesPerChance`, so a set takes 100 passes at 1% and `100 × c` at chance `c`. The
balance of D-GN.8 is untouched: every chance still costs the same. **Consequences:** every violet figure in §6–§7 and
§9 is multiplied — Aquarius at any chance is 810,000 bets, not 8,100 (`Predicted(n) × PassesPerChance` in the harness;
table in the proposal's §5). A single pass no longer moves the colour at most chances: at 95%, a level now takes 100
passes. So the overlay must show the progress **inside** a level — how many more passes to the next one, and more
decimals in "Done" — or a whole round would look like nothing happened (the Convention 11 reflex).

**D-GN.24 — one sigil of each class exists in the whole game.** Fourteen items, each obtainable once, by purchase or by
receipt; owning is permanent, and there is no second copy. **The player deduces which combine:** nothing states a pair
outright. Each item's name, shape, colour and description make it clear, and its description may name its planet and
its sky, which is how the deduction is made; the collection is listed alphabetically, not by planet, so that the layout
does not give pairs away. **Acquisition in this plan is a free, DEV-marked button** (developer's choice, 2026-11-01),
like the DEV Buy Hardware; prices and channels belong to the next plan. Polished item art comes with the Basic Version
polish; this plan's items are functional, with the shape drawn from the sigil itself.

**D-GN.25 — one sigil slot, for now, and the player chooses what it holds at any time**, including during an autobet:
the next bet draws the new sigil. **Switching loses only the pass in progress** — the numbers hit so far in the set
being drawn, exactly what a chance change loses (D-GN.9, counted the same way in `PassRestarts`). *"If a set is
incomplete, the progress in that set is lost"* means this, **confirmed by the developer on 2026-11-01**: only the partly
drawn pass; completed passes stay. Each owned sigil keeps its own passes, figures, V and loss streak while it waits. **Time spent on another sigil
is not that sigil's time:** D-GN.20's game seconds count only while it is in the slot. **All of it is committed by the
next mined block, as everything is (Pattern 2):** a restart before one reverts the switch and the bets after it. The
slot may be empty, as it is in a new world, and then a bet draws nothing.

**D-GN.26 — the automatic order is retired.** With a chosen slot, finishing a sigil no longer moves on by itself: a
violet sigil stays in the slot and takes no more progress until the player chooses another. **D-GN.18's unlock order
and D-GN.16's "diurnal first" stop being rules**; `CompletedSigilRecord` and `AllComplete` go with them (Convention 3),
since each owned sigil now carries its own completion. Whether an order returns as a gate on *acquiring* sigils is a
question for the next plan.

**D-GN.27 — a sigil's level is the lowest level among its sets, and a combined figure's is the lower of its two
sigils'.** A level is the D-GN.22 step, `SigilRules.MaxLevel` being violet. *The highest level all the sets share* is
the lowest of them, since a set at level 10 has also passed level 4. **Combined figures are the planet's two sigils**
(D-GN.16's *complete Saturn Sigil*); one exists from the moment the player owns both and the lower of them reaches
level 1, which is when it first appears in the collection. Sigil A at 10 and sigil B at 4 make a figure at 4. Nothing
reads a level for a benefit yet: this plan builds and displays the levels; the next one spends them.

**P-GN.7 — bots are out of the first build** (D-GN.18). They are Dice-only and their bets already pass through
`SimulationService`, so joining later looks cheap; whether it is, is a question for after the tests.

### 8.2 The planetary sigil — D-GN.28 to D-GN.30 (developer, 2026-11-02)

The developer's overrule and new mechanic, given after unit 9's commit and before anyone had seen that screen. **Unit 10 built them (§10.10); D-GN.34 to D-GN.38, at the end of this section, overrule parts of what follows, and unit 10b builds those.**

**D-GN.28 — a complete sigil cannot be played.** *Overrules unit 9's default (§10.9), and limits D-GN.25's "any time".*
There is nothing to gain by it. Its card offers no Play, and `PlayerSigilProgress.Equip` refuses it, so the rule holds in
the state and not only on the button. A sigil that completes while in the slot still stays there, taking no progress,
until the player chooses another (D-GN.26), and once it has left it does not return. **D-GN.30 is the only way a complete
sigil is ever drawn again.**

**D-GN.29 — reaching level 1 on a pair is announced, for 10 in-game minutes.** When the lower of a planet's two sigils
reaches level 1, the moment `SigilRules.HasCombinedFigure` first holds, the figure appears **small, in the colour of its
combined level, with a label such as *"Activate Saturn Sigil"***, on the collection screen **and on `DiceGame`**. It
disappears by itself after 10 minutes of game time. The combined figure's card in the collection (D-GN.27, unit 9)
is permanent and stays; only this notice is temporary.

*What 10 game minutes are (arithmetic, `MaxGameSecondsPerRealSecond` for the top rate): 600 game-seconds is **6 real
seconds at 100X** and **0.067 real seconds at 9000X**, about 4 frames at 60 fps, shorter than the overlay's 4 Hz sample. So
the DEV speeds of a measurement run can show it for no visible time, and `DiceGame`'s display must not depend on a
sample that may miss it. It is also 6 bets at 1 credit and 300 at 50. While a run goes, the clock moves only with
simulated bets (the 100-second rule), so a stopped autobet holds the notice up until the clock next moves; whether
manual play moves it the same way is for unit 10 to read in the code.*

**D-GN.30 — activating a level of the planetary sigil, by drawing both sigils.** The **planetary sigil** (D-GN.16's
*complete Saturn Sigil*) has its own 95 levels, and they are not free. A new dynamic in the sigil overlay, which runs only
when the player chooses to activate one:

1. **The gate:** level `k` of the planetary sigil can be activated only when **both** sigils already hold at least level
   `k`, that is, when the combined level of D-GN.27 is at least `k`.
2. **The drawing:** both sigils are drawn **one after the other**.
3. **The price:** level 1 takes **1 whole pass through both sigils**, level 2 takes 2, and so on up to `MaxLevel`, which
   takes `MaxLevel`. A whole pass through a sigil is one pass on every one of its sets, what D-GN.22 calls a round.
   ***Overruled by D-GN.34:*** *`PassesPerChance × c` whole passes for all 95 levels, every level alike. The definition of a whole pass stands.*

So **the combined level says what can be activated, and the activated level is what has been.** Sigil A at 10 and sigil B
at 4 make a figure at 4 (D-GN.27), and with nothing activated its levels 1 to 4 are open.

**The cost, for sizing (mean bets, from the harness's `Predicted(n)`, constant 95%):** one round of one sigil is
`Predicted(n) / 95`, so one pass through both is `2 × Predicted(n) / 95` — **170.5 bets on Saturn, 11,236 on the Moon**.

**Answered by the developer, 2026-11-02: all four at their defaults, and OQ-GN.4's answer also brought D-GN.31 to D-GN.33, below. Unit 10
built that. The developer then replaced OQ-GN.1 and OQ-GN.2's answers with D-GN.34's fractal, so neither the table nor the `95 / c` below
is the rule any more; they stay as what unit 10's code does until unit 10b.**

- **OQ-GN.1 — does the price add up? Yes, reading A** (*"vamos con default, 48% más"*): each level in order, each costing
  its own `k`.

  | Reading | Saturn, all 95 levels at 95% | Moon | Against drawing both sigils to violet |
  |---|---|---|---|
  | **A. Each level in order, each costing its own `k`** (1 + 2 + … + 95 = 4,560 passes) — **chosen** | 777,600 bets | 51,235,200 bets | **48%** on top |
  | B. Any open level directly for `k`, skipping the rest (95 passes to the top) — declined | 16,200 | 1,067,400 | 1% |
  | C. `k` passes in total to reach level `k`, so each new level costs one more (95 passes to the top) — declined | 16,200 | 1,067,400 | 1% |

- **OQ-GN.2 — does the chance matter? Yes** (*"el chance sí tiene que importar"*). A pass at 1% costs 95× one at 95%
  (§5–§6), so **a pass at chance `c` counts `95 / c` passes**, in the same exact fractions as `Progress`: level `k` is `k`
  passes at 95%, exactly as the developer stated it, while one pass at 1% is worth 95. D-GN.9 holds as it stands: a pass
  belongs to one chance.
- **OQ-GN.3 — does activating feed the sigils? No.** It has its own record, passes, bets, SC and game time (D-GN.19,
  D-GN.20) and its own V, starting fresh like any new sigil (§5.2). Each sigil's own passes, V and level do not move. It
  has to work this way for level 95 at least: both sigils are violet then, and take no progress (D-GN.26).
- **OQ-GN.4 — does it use the slot? Yes, there is one slot (D-GN.25).** Starting an activation is a switch, losing only
  the slot sigil's pass in progress, with the same warning; leaving it loses only the activation's pass in progress, and its
  completed passes stay. **Diurnal sigil first, then nocturnal.**

**D-GN.31 — what a completion does: a toggle in the sigil overlay** (developer, 2026-11-02). *A completion* is a zodiac
sigil finishing, or a planetary sigil reaching the highest level open to it. Until the Basic Mode polish (the objective in
`PRIVATE_ROADMAP.md`), the player chooses between two modes, and the choice is a preference, not world state:

- **Stop on Sigil Completed** *(the default until D-GN.35)*: the run stops on the completing bet, with a message to choose another
  sigil. **Bets are refused, manual and autobet alike, until the slot changes.** Player active node only: a bot draws no
  sigil, and its bets are never refused.
- **Automatic switch:** the slot takes **the least played sigil the player owns**: the fewest `BetsPlaced` among owned
  incomplete zodiac sigils, the collection's alphabetical order breaking a tie. The run goes on. **It never starts an
  activation:** D-GN.30 says that runs only when the player chooses it, and the developer's words are "un nuevo sigilo".
  ***Overruled by D-GN.35:*** *it does, and it is the default.*
- **In both modes, with nothing else to play, the stop does not fire and nothing is refused:** bets run and draw nothing,
  and **a warning on `DiceGame` says levels are not advancing because no incomplete sigil is in the slot.** That is what
  keeps a player with only finished sigils from freezing the clock, which only bets can move. *Nothing else to play* means no
  owned incomplete sigil and, for Stop's refusal, no open activation either, since the player could choose that by hand.
  The warning's condition is literal: an empty slot shows it too. ***D-GN.36:*** *it is a DEV warning.*

An acquisition, or turning the toggle to automatic, while a complete sigil sits in the slot also takes the least played
sigil. This is the one narrow exception to "acquiring does not auto-equip" (§10.8), and it is the developer's *"(cuando lo
tenga)"*. An empty slot stays empty: nothing was completed, so there is nothing to change from.

**D-GN.32 — Stop on Sigil Level Acquired, a real switch in `DiceGame`'s strategy panel, beside Stop Set** (developer,
2026-11-02). OFF by default, saved with a strategy, read from the session (D-M2.8), locked OFF for a bot. It stops on the bet
that raises a sigil's level, one of its 95: a zodiac sigil's own level (D-GN.27, the lowest of its sets, so a set rising
without lifting the lowest does not stop it) or a planetary sigil's activated level. **It has nothing to do with D-GN.31**,
and the two coincide only where a level is also a completion: level 95, and a planetary sigil's highest open level.

**D-GN.33 — the planetary figure: strict alternation, and one activation runs on through the open levels** (developer,
2026-11-02). Every whole pass draws **all of one sigil's sets, then all of the other's, never the same sigil twice in a
row**: diurnal set 1 … n, nocturnal set 1 … n, again. The canvas builds the complete planetary figure on one ground, both
sigils on it, and clears when the whole pass ends. *The developer's stop fires at "el nivel máximo que puede conseguir", so
an activation does not end at each level:* it continues to the next open one, and stops only when the gate shuts.

**Two consequences unit 10 decided, for the developer to overrule** *(both overruled by D-GN.34, which has no need of either)*. *(1) A level is credited only when a whole pass ends*,
so that no level can be earned without the complete figure: at 1% on Saturn, a single set pass is worth 95/6 whole passes,
and crediting it at once would activate levels from one set of one sigil, against what the developer asked. *(2) What a
whole pass overpays carries forward:* the value is cumulative and exact, level `L` being paid once it reaches `L(L+1)/2`.
So one whole pass at 1% (worth 95) reaches level 13 (91) if the gate allows, and what the gate holds back stays paid for,
credited at the end of a later whole pass. Dropping it would make low chances cost more than the balance says (OQ-GN.2).

### The developer's answers to unit 10's questions — D-GN.34 to D-GN.38 (2026-11-02)

Given after unit 10 was staged, against the seven points it asked the developer to check. **Decided, not built: unit 10b builds them (§10).
Until then the code does what §10.10 describes, the price included.** The harness still tests that price, and passes against it.

**D-GN.34 — the planetary sigil is a fractal of the zodiac sigils.** *Overrules OQ-GN.1's reading A, D-GN.30's price, OQ-GN.2's `95 / c`,
and D-GN.33's two consequences. The developer first read as one level per 100 whole passes, then settled it on 2026-11-03: **100 whole passes at
1% for all 95 levels.*** So the planetary sigil is priced exactly as a set is (D-GN.23): **`PassesPerChance × c` whole passes through both
sigils, one after the other, for the whole 95 levels**: 100 at 1%, 1,000 at 10%, 5,000 at 50%, 9,500 at 95%, which at 95% is 100 whole passes a
level. Every level costs the same. It starts at the open level, which is always 1 at first, and continues to the next, up to level 95. D-GN.30's
gate stands: level `k` needs both sigils at level `k` or more.

| Mean bets, from `Predicted(n)` | Bets | Real time at the last run's ~4,200 bets a second | Game time, 1 credit (100 s a bet) | 10 credits | 50 credits | 99 credits |
|---|---|---|---|---|---|---|
| Aquarius Sigil, violet | 810,000 | 3.2 minutes | 2.6 years | 94 days | 19 days | 9.5 days |
| Cancer Sigil, violet | 53,370,000 | 3.5 hours | 169 years | 16.9 years | 3.4 years | 1.7 years |
| Saturn, planetary sigil alone, violet | 1,620,000 | 6.4 minutes | 5.1 years | 188 days | 38 days | 19 days |
| **Saturn from nothing:** both its sigils, then the planetary | 3,240,000 | 12.9 minutes | 10.3 years | 1.0 year | 75 days | 38 days |
| Moon, planetary sigil alone, violet | 106,740,000 | 7.1 hours | 338 years | 33.8 years | 6.8 years | 3.4 years |
| **Moon from nothing:** both its sigils, then the planetary | 213,480,000 | 14.1 hours | 676 years | 67.6 years | 13.5 years | 6.8 years |
| One planetary level: Saturn / Moon | 17,053 / 1,123,579 | 4 seconds / 4.5 minutes | 20 days / 3.6 years | 2 days / 130 days | 9.5 hours / 26 days | 4.8 hours / 13 days |

*Arithmetic: a planetary sigil's violet is `200 × Predicted(n)`, whatever the chance, the cost of drawing both its sigils to violet, so **activating
everything doubles the planet: 100% on top, against reading A's 48% and the first fractal reading's 9,500%**. The gate makes the "from nothing" rows the
real ones, since level 95 needs both sigils violet. `Predicted` is approximate (§10.10): the exact chain at 95% reads Saturn's violet 1,619,358 and the
Moon's 106,815,981. The real-time column is one measured rate on the developer's machine, and the rate falls with what else the frame does.*

**The rule, for unit 10b: a planetary record is a sigil of 2n sets, under the same rule.** Each set carries its own progress and level through
`Progress`, `SetLevel`, `SetLevelAfterPass` and `PassesToNextLevel`, and the planetary sigil's level is the lowest of its sets' (D-GN.27). A pass at
chance `c` adds `1 / (PassesPerChance × c)` of its set, exact as ever. **That is what "fractal" comes to in the code:** `CompletePlanetaryPass`,
`LevelsPaidFor`, `ActivationFullValueChance`, `WholePassesPaid`, `NextLevelProgress` and the stored `ActivatedLevel` all go (Convention 3), and
`SigilLevel` loses its planetary branch. **Three differences remain, and only three:**

- **The sets keep cycling in order, all of them, even past 100%:** no set is marked `Finished` and none is skipped until the record ends. Otherwise, with mixed
  chances, one sigil's sets could finish first and the other sigil would then repeat alone, which D-GN.33 forbids.
- **The record ends at its limit:** when `SigilLevel` reaches `LevelLimit`, it takes `CompletedAtUtc`, and a later activation clears it.
- **The names and the canvas's ground**, as now.

**A level now rises on the bet that lifts the lowest set.** At a constant chance that is the last set of a whole pass, since every set holds the same count. With mixed
chances it can be another set, the one that was behind. Every set has then reached the level, so the figure is complete at that level either way. *This replaces
unit 10's "credited only when a whole pass ends", and with it the question of overpayment:* no pass adds a whole level, so none can jump one. What a set holds past the gate
simply stays in its progress, and it is the record's own, as a set's always was. D-GN.33's alternation and its running on through the open levels both stand, and the
developer's answer confirms the second.

**D-GN.35 — Auto-switch includes activations, and it is the default.** *Amends D-GN.31 and D-GN.30's "only when the player chooses".* **The
toggle starts with Stop off**, that is, on Auto-switch. The choice, **a provisional default for the Basic Mode polish to design properly:** the
least played owned incomplete sigil as before, and the open activation with the lowest activated level, the alphabetical planet on a tie;
**between the two, the activation only if its activated level is strictly below that sigil's level: at the same level, always the zodiac sigil.**
With no zodiac sigil left, the activation. *Nothing else to play* now means no owned incomplete sigil and no open activation, in both modes.

**D-GN.36 — "Levels are not advancing" is a DEV warning.** Marked `[DEV]` and shown only in a DEBUG build. **The game is to be designed so
that the player always has a sigil to draw**, or, without one, is not told about sigils, or is told along with a way to get one, depending on
the stage: `PRIVATE_ROADMAP.md`, *Sigils in the historical narrative*. Stop mode's message to choose another sigil is not this warning, and stays.

**D-GN.37 — the notice is a temporary button.** *Amends D-GN.29's label.* Pressing "Activate Saturn Sigil" does what the combined card's
Activate does, its warning included when a pass is in progress; on `DiceGame` it opens the overlay on the collection first. **That press's
target is this plan's default, for the developer to overrule.** A second planet's notice within the 10 minutes replaces the first: confirmed.

**D-GN.38 — Stop Set becomes Stop on Set Level Acquired, on zodiac and planetary sigils alike.** *Overrules D-GN.10's trigger, and Pr-7.* It stops on the bet that
raises a **set's** level (`LevelReached`), no longer on every completed pass. Stop on Sigil Level Acquired (D-GN.32) stays beside it, on the sigil's level. The reason
D-GN.10 gave still holds, since a level rises at the end of a pass, where a chance change costs nothing.

**On a planetary sigil (developer, 2026-11-03) it fires only in the last whole pass of those a level needs, on each set, so that the player watches the line of that level
painted set by set.** At 95%, a level is 100 whole passes: nothing fires for 99 of them, and the 100th stops on every one of its 2n sets, six on Saturn, 18 on the Moon. This is
not a rule of its own: it is what D-GN.34's per-set levels do at a constant chance, each set reaching the level on its own pass of that same whole pass, and the canvas already
paints a passed set in its level's colour. At 1%, where one whole pass is 0.95 of a level, nearly every whole pass does this. The panel reads "Stop on set level" / "Stop Set
Level: OFF", and the field and `StopReason` are renamed; a strategy saved with the old flag loads with it OFF, as a personal file does (no migration).

**Defaults that need no answer unless the developer objects:** the notice fires once for each planet, at level 1 only, as
stated, not at each later level. The activated level is committed by the block like everything else, in the checkpoint's
`SigilCollectionState`, with no file of its own (P-GN.4) and a sentinel default so an older checkpoint restores nothing
activated (Convention 5). A notice live at a restart is not restored, since it is a notice and not state; the bet that
raises the level fires it again if a restart reverted that level. **What activating unlocks is a question for the next
plan** (`sigil_benefits-proposal.md` §6), though the word suggests the benefit reads the activated level, not the combined
one. Nothing in this plan spends either.

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
| Pr-7 | *(unit 5; its trigger is overruled by D-GN.38, see Pr-22)* With "Stop on Sigil Set Completed" ON, an autobet run stops **on the bet that completes a set pass** — after ~28 bets on average at 95% (3 hits × ≈ 9.5 bets), and almost never after more than 200 | A stop on any other bet, or no stop ⇒ a wiring bug in the stop |
| Pr-8 | *(unit 5)* With a flat bet of 0.01 SC, a completed sigil's SC wagered is **exactly 0.01 × its bets** | Any other figure ⇒ the D-GN.19 tracking drifts |
| Pr-9 | *(unit 5, added 2026-10-29 before any run)* A set's game time grows by **`100 / credits` game-seconds per bet** — 100 s at 1 credit, 10 s at 10, 2 s at 50 — whatever the DEV time scale; the idle time between two runs is never counted | Off by more than 2% ⇒ the D-GN.20 time tracking does not measure what it claims; idle time in a set ⇒ its "game time passes only as bets are simulated" premise is false |
| Pr-10 | *(registered 2026-11-01, before any ×100 code)* Violet Aquarius at **95%** in game costs **810,000 bets** (D-GN.23). 85,500 hits, so one run's 1σ is ≈ 0.34% | One run outside **802,000–818,000** (±1%, ~3σ) ⇒ the code and the model disagree |
| Pr-11 | *(2026-11-01)* At a constant 95%, that sigil has **exactly 9,500 passes in each set**, all at 95%. Its level is `MaxLevel` only when it is complete, and always the lowest of its sets' (D-GN.27) | Any other count, or a level above a set's ⇒ a rule bug |
| Pr-12 | *(2026-11-01)* Switching the slot with a pass in progress changes exactly: that sigil's `Step` and `PassChance` to 0 and its set's `PassRestarts` by +1. Its passes, V and loss streak survive, and on returning, the game seconds of its sets have not grown by the time it was out (D-GN.25) | Any other difference ⇒ the switch loses more, or less, than the rule says |
| Pr-13 | *(2026-11-01)* A restart after a mined block restores what is owned, the slot and every sigil's progress exactly; a restart before one reverts any switch and acquisition made since | Any difference ⇒ a capture or restore bug; it blocks the close-out |
| Pr-14 | *(2026-11-01)* An ordinary bet still allocates **0 bytes** and costs the same order as Unit 1's ~40–50 ns, with the equipped sigil reached without a search | An allocation, or a cost several times higher ⇒ the per-bet path regressed |
| Pr-15 | *(registered 2026-11-02, before any code for §8.2)* `Equip` on a complete sigil returns false and changes nothing, and its card has no Play. A sigil completing in the slot stays there, and can leave but not return (D-GN.28) | Any return ⇒ the rule lives on the button alone, or not at all |
| Pr-16 | *(2026-11-02)* The notice fires **exactly once per planet**, on the bet that takes the lower of the two sigils to level 1, never on an acquisition (a new sigil is at level 0, so owning cannot raise the lower), and ends when the game clock passes that bet's timestamp + 600 s (D-GN.29) | A second firing, one on any other bet, or an end at any other game time ⇒ an edge or clock bug. **Harness for the edge; the display in game, at 100X, where it lasts 6 real seconds** |
| Pr-17 | *(2026-11-02; **restated for D-GN.34 before its code, 2026-11-03**)* Level `k` cannot start while the combined level is below `k`. At a constant chance `c`, all 95 levels take **exactly `PassesPerChance × c` whole passes: `2 × n × PassesPerChance × c` set passes**, 100 whole passes at 1% and 9,500 at 95%; each level is reached exactly as a set reaches it (at 95%, one per 100 whole passes), and no other planet's sigil moves. *As first registered: `k` whole passes for level `k`, which unit 10's harness confirmed and D-GN.34 retired* | Any other count ⇒ a rule bug |
| Pr-18 | *(2026-11-02; **restated for D-GN.34 before its code, 2026-11-03**)* The planetary sigil's violet costs `200 × Predicted(n)` bets on average at every chance, 1,620,000 on Saturn, the exact chain of §10.10 refining the figure; a whole pass at 95% stays 170.5 bets on Saturn. While activating, **neither sigil's own passes, V, level or figures move** (OQ-GN.3's default) | Harness: outside 4σ, the bar section 5 uses ⇒ the code and the model disagree; a sigil's JSON changing ⇒ a leak between the records. **Refuted as first written, by the model (§10.10):** `Predicted` is up to 2.2% off on the larger squares; the code matches an exact chain, which the harness tests against. The second half holds |
| Pr-19 | *(2026-11-02)* A restart after a mined block restores the activated levels and an activation in progress exactly; a restart before one reverts them (Pattern 2, extending Pr-13). An ordinary bet with no activation running costs as Pr-14 says: the notice's edge is read only behind `LevelReached` | Any difference ⇒ a capture or restore bug, or the per-bet path regressed |
| Pr-20 | *(registered 2026-11-02; **restated for D-GN.34 before its code, 2026-11-03**)* **The order and the level (D-GN.33, D-GN.34):** at any chance, mixed ones and past a set's 100% included, an activation's set passes run diurnal 1 … n, nocturnal 1 … n, repeating, never the same sigil twice in a row. The planetary level is always the lowest of its 2n sets' and rises one at a time, exactly `MaxLevel` times over the record's life; at a constant chance, on the bet that ends a whole pass. A mix pays in exact fractions | Any other order, a level above a set's, a jump, or a fraction lost ⇒ a rule bug. Harness |
| Pr-21 | *(2026-11-02; **amended for D-GN.35 before its code**)* **The completion and the level stop (D-GN.31, D-GN.32).** Stop mode with something else to play: the run stops on the completing bet, and the next bet of either path is refused until the slot changes. Automatic: the slot takes the owned incomplete sigil with the fewest bets, alphabetical on a tie, **or, since D-GN.35, the open activation with the lowest activated level if that is strictly below the sigil's level**; at the same level, the sigil. **Automatic is the default.** Nothing else to play, open activations now counted: no stop, no refusal, and the warning's condition holds. The level stop's outcome fires exactly `MaxLevel` times over a sigil's life at any chance, and on every activated level | Any other firing or choice ⇒ a rule bug. **Harness for the rule; the panel, the toggle, the refusal and the warning in game** |
| Pr-22 | *(registered 2026-11-02, before any code for D-GN.36 to D-GN.38; **amended 2026-11-03**)* **Stop on Set Level Acquired** fires on exactly the bets `LevelReached` holds: `n × MaxLevel` times over a zodiac sigil's life at any chance, 285 on Aquarius, and `2n × MaxLevel` on a planetary sigil, 570 on Saturn, never on a mere completed pass. **At 95% on Saturn its firings come in six, one per set, all inside the 100th whole pass of each level.** **The notice's button** does what the combined card's Activate does, warning included, and is gone with the notice. **The warning** shows `[DEV]` in a DEBUG build, and in a release build nothing | Any other firing ⇒ a rule bug; a warning in a release build ⇒ D-GN.36 missed. **Harness for the stop's firings; the rest in game, and the release half only by reading the guard, since no release build is run** |

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
6. **Review with the developer** — repetition incentives, unlock alternatives and bots (D-GN.18): **done 2026-11-01.**
   It produced D-GN.23 to D-GN.27 (§8.1) and the proposal for the next plan.
7. **×100 and levels** — D-GN.23 and D-GN.27's arithmetic: `PassesPerChance`, the levels, the passes to the next level,
   the overlay's readouts. The harness can no longer draw every sigil to violet in every setting (11.8 billion bets), so
   its cost table measures one-hundredth of violet — `c` passes per set, the same bets and the same noise as before —
   and exact completion is checked to full violet where it is cheap.
8. **The collection and the slot** — D-GN.24 to D-GN.26: the state, acquiring, equipping, the switch, the checkpoint.
   The snapshot's field is renamed, so an existing checkpoint restores an empty collection, the same way one from
   before this plan restored fresh progress; no format bump or wipe. Only test progress is lost.
9. **The collection's screen** — the fourteen items, their shape, description and DEV button; the combined figures.
10. **The planetary sigil** *(added 2026-11-02; until then this number was the measurement)* — D-GN.28 to D-GN.30, §8.2:
    the refusal, the notice, the activation and its record, what a completion does (D-GN.31) and the level stop (D-GN.32),
    with harness sections for Pr-15 to Pr-21.
    **10b.** *(added 2026-11-02)* **The developer's answers to unit 10** — D-GN.34 to D-GN.38: the fractal price, Auto-switch with activations and
    as the default, the DEV warning, the notice as a button, Stop on Set Level Acquired.
11. **Measurement** — Pr-10 to Pr-22, with the first look at units 7, 9, 10 and 10b, then close-out.

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

### 10.7 Unit 7 — ×100 and levels, done (2026-11-01)

**Built.** `SigilRules.PassesPerChance` enters the one fraction everything reads (`Progress`), so finishing, levels and
the display cannot disagree. The D-GN.22 "colour step" is now named what the developer calls it, a **level**:
`SetLevel`, `SetLevelAfterPass`, `MaxLevel`, `ColorForLevel`. New: `PassesToNextLevel`, exact in integers;
`SigilLevel` and `CombinedLevel` (D-GN.27), displayed but read by nothing else yet. `LastPassChance` is deleted, its
last reader having gone with D-GN.22 (Convention 3).

**The overlay after ×100.** At most chances a pass no longer changes a level, so the text under the strip gives the
passes to the next one (*"Set 2: level 6 of 95 · at 95%, 100 passes to level 7"*), "Done" and the sigil's completion
went from one decimal to two, and the status line leads with the sigil's level. The strip's gold mark now appears only
when the very next pass reaches a new level. **Level 0, the state of a new sigil for its first 100 rounds at 95%, draws
as level 1's red:** red is where every set starts.

**The harness.** Drawing every sigil to violet in every setting would be 11.8 billion bets, so section 5 measures `c`
passes per set, one-`PassesPerChance`-th of violet, the same bets and noise as before: every cell within 4σ (worst
2.91σ). Section 3 checks exact completion to full violet — Aquarius at 1%, 3%, 7%, 50% and 95%, 796,693–836,811 bets,
exactly `3 × PassesPerChance × c` passes — and Cancer at 1%: 900 passes, 52,708,141 bets against 53,370,000. Section 9
is rewritten for levels: the worked cases, 2,000 random sets where exactly `PassesToNextLevel` passes reach a higher
level and one fewer does not, the developer's example (10 and 4 combine at 4), and 14,205 passes at mixed chances to
violet. Cost: 56.9 ns and 0 bytes a bet. A full run takes 27 s.

**Verified:** `dotnet build` clean; locale detector 17 / 0 / 0 / 0; the harness passes. **Not seen in game**, and still
linear: the automatic move to the next sigil stays until unit 8. **Not measured:** Pr-10 and Pr-11 wait for the measurement unit (unit 11 since §8.2).

### 10.8 Unit 8 — the collection and the slot, done (2026-11-02)

**Built.** `SigilCollectionState` holds `Owned` and a nullable `EquippedSigilIndex`, and a fresh one is the truth of a
new world: nothing owned, an empty slot. `SigilProgressState` is now one sigil's record and carries its own
`CompletedAtUtc`. `PlayerSigilProgress` caches `Equipped`, refreshed on equip and on restore, with `Acquire`, `Equip`,
`Find` and `IsOwned` beside it. The checkpoint's field is `SigilCollection`, so an older checkpoint restores an empty
collection. **Deleted (Convention 3):** `CompletedSigilRecord`, `Completed`, `AllComplete`, `StartSigil`, and
`AllSigilsCompleted`, which nothing read. **The time trap:** `SigilRules.LeaveSlot` forgets `LastBetAtUtc`, and the
first-bet test is now `StartedAtUtc == default`.

**Defaults this unit chose, for the developer to overrule:** acquiring does not equip; there is no "unequip", since a
slot is empty only until the first equip. **Planet totals wait for the combined figure.** The overlay's *"Saturn so far
… (with the Aquarius Sigil)"* appears when `SigilRules.HasCombinedFigure` holds (both owned, the lower at level 1), not
at the moment both are owned. It states the pair, and D-GN.27 gives that one moment; a second, earlier one would have
been a reveal with no decision behind it. Any owned pair counts, not only a finished first sigil.

**The trap that was not there.** The brief expected `CurrentTarget` to go out of range on a complete sigil. It cannot
now: with `StartSigil(next)` gone, `SigilIndex` never reaches 14. It returns a valid and meaningless number instead,
so its readers check `IsComplete`: the canvas draws no ring, `Describe` prints `target=complete`, the preview says
*Complete* where it would have said "to level 96", and no row of the sets table is marked as the set being drawn.

**The pass line became a level line (decided before the run, as the brief asked).** At 95% a violet Aquarius has 28,500
passes, about 150 lines a real second at 4,200 bets a second, each carrying every set. A new outcome,
`SigilBetOutcome.LevelReached`, drives `[Sigil] level reached — …` in DEBUG instead of `pass complete` (§10.5's table
predates this). A pass adds at most 0.95 of a level, so each set reaches every level exactly once, at any chance: 285
lines for Aquarius, against 28,500, and the harness counts exactly that at 95%, 50% and 1%. **Pr-9-style readings use
these lines:** two of them in one phase still give `Δ seconds / Δ bets`, since each carries every set's bets and
seconds. **The stop's own `STOP on set completed` line carries the state Pr-7's reading compared against `pass complete`.**
`Describe` gained `level=` and lost `completed=`; `Describe(SigilCollectionState)` prints the slot and every owned
sigil on **one line**, so Pr-13 compares a restore against a checkpoint as text.

**Not built here:** the `[Sigil]` lines for an acquisition and a switch. No Godot code calls `Acquire` or `Equip` until
unit 9's buttons, and `Scripts/Numerology/` stays free of Godot for the harness's sake. Until unit 9 the overlay shows
*"The sigil slot is empty"*, and **no bet in the game can draw a sigil**.

**The harness, rewritten for the new shape.** Section 7 is Pr-13, including a switch and an acquisition after the block
being reverted, and the cached slot being the restored list's own object rather than the snapshot's. Section 10 is new
and is Pr-12, by comparing the sigil's JSON with a copy edited by hand to hold exactly the predicted differences; it
also covers one-of-each, the sigil out of the slot not moving, the first bet back crediting none of the time away and
the second crediting its 100 s, and a complete sigil taking no progress and returning still complete.

**Pr-14, measured in the harness, and a bug in the measurement.** `LevelReached` costs a second exact fraction per
pass. Compared within one run, on Cancer at 50%: the rule alone **64.4 ns**, through `PlayerSigilProgress` with fourteen
owned and the last in the slot **75.9 ns** (1.18×), **0.00 bytes** in both; an empty slot **4.1 ns**, 0 bytes. One capture
with fourteen owned, each nearly fresh: 0.94 ms, once per block. *The section's old form had drawn Aquarius for all
5,000,000 bets and first printed 23.0 ns: D-GN.26 completes it near bet 810,000, and the rest of the run measured the
early return. Against that flattering figure the slot looked 5× dearer than the rule. Both now read one sigil, one list
of events. Per Pattern 6's third instrument rule, 64.4 is not comparable with unit 7's 56.9 from another session and
another sigil, and no regression is claimed or denied from it.*

**Verified:** `dotnet build` clean; locale detector 17 / 0 / 0 / 0; the harness passes. **Not seen in
game, and not measurable there until unit 9.**

### 10.9 Unit 9 — the collection's screen, built (2026-11-02)

**Built.** A second view inside the overlay, `SigilCollectionView`, over the same footer, which gains a "Collection" /
"Back to the sigil" button. The overlay opens on it when the slot is empty and on the drawing otherwise; pressing Play
goes on to the drawing. Fourteen cards in two columns, in `PlanetarySquares.SigilsByName`'s order (ordinal, so no locale
can reorder it). Each has its name, a `SigilThumbnail`, its level in that level's colour (grey "Not owned" otherwise),
`SigilRules.DescriptionOf`, and either **Acquire [DEV]** or **Play**. Below them, the combined figures that exist.

**Layout (Ch. 29).** Pattern A, because the items hold buttons: a `ScrollContainer` with `size_flags_vertical = 3`
between a fixed slot line and the footer, which stays outside it. Every control inside passes the wheel. Seven rows of
about 124 px make about 920 px against about 680 px of room, so **it scrolls by design**. The description's two lines are
reserved rather than measured, because a wrapped `Label` reports its height late. No `fit_content` label is involved.
The slot line is 64 px, two lines at its font: the longest warning, 142 characters at about 10.5 px each in the 1,418 px
left beside its buttons, wraps once. *These heights are arithmetic, not measurements; §10.6's clipped warning was the
same kind of figure, and the first look is what checks them.*

**The Play warning.** With a pass in progress, Play asks first: the slot line names the pass (*"Playing the Libra Sigil
loses the Aquarius Sigil's pass in progress: set 2, 2 of 3 numbers hit at 95%. Its completed passes stay."*), beside
**Play anyway** and **Cancel**. With none in progress it switches at once. A running autobet moves the pass while the
warning waits, so the warning is sampled at the overlay's 4 Hz, and says *"loses nothing right now"* when that is true.
What is lost is read at the moment of the confirmation, not of the first press.

**The cost.** No new `_Process`; §38.5's row now says what the existing one does in this view. The cards repaint on an
acquisition, a switch, a restore or a level, never on a hit, and a thumbnail only when its colour changes. **Not timed:**
at the ~4,200 bets a second of the last run, Aquarius reaches about 1.5 levels a real second (285 in 810,000 bets), each
paying for fourteen `Find`s and the owned sigils' `SigilLevel`, and none of it runs unless this view is showing. That
this is negligible is a judgement from those counts, not a measurement.

**The log lines, for Pr-12 and Pr-13.** `[Sigil] acquired — <name> — <Describe(collection)>` and `[Sigil] switched — <from>
→ <to>, <LOST a pass in progress: …, or nothing lost> — before: <Describe(leaving)> — after: <Describe(collection)>`. **The
`before:` part is beyond the brief:** Pr-12 needs the leaving sigil's state on both sides of the switch, and without it
the only earlier reading could be thousands of bets old. Both are `GD.Print`, from the Godot-side handlers.

**The descriptions, and a check that was wrong.** *"Saturn's square, by day: 3 sets of 3 numbers, counting by 1."* No
description names any sign, not merely its partner's: "Eclipsed Leo" holds "Leo", so any sign word risks it. The new
harness section 11 tests that on the description text, against twelve sign words and a list of pair words, and first
proves it can fail on four bad examples (Convention 16). **It also carried a check of mine, that no pair sits side by
side, and that check failed: the alphabet itself puts Pisces beside Sagittarius.** The check asked for more than the
decision does, since D-GN.24 says alphabetical, not scattered, so it was removed and the order kept. In the two-column
grid, Cancer also sits directly above Eclipsed Cancer and Eclipsed Leo directly above Leo, which their names say anyway.
**For the developer to judge on seeing it.** Combined figures: their cards hold no text and their header stays hidden
until `HasCombinedFigure`, so nothing is there to read before then.

**Verified:** `dotnet build` clean; locale detector 17 / 0 / 0 / 0; the harness passes. **Not seen in game, none of it:**
the screen, the scroll, the wheel, the thumbnails, the warning and the log lines have only been compiled. **The switch
during an autobet is reasoned, not run:** no `Task.Run`, `Thread` or `Parallel` in `SimulationService` or
`UserStatsService`, so a handler runs between bets. The measurement unit runs it (unit 11 since §8.2). Godot wrote the new
scripts' `.uid` files on its next run, and they came in with the commit after this one.

**Overruled afterwards (developer, 2026-11-02):** this unit let a complete sigil be played again; D-GN.28 forbids it, and
unit 10 changes the card, `Equip`, and harness section 10, which still asserts that such a sigil *"leaves and returns"*.

**Deferred to just before the close-out (developer, 2026-10-29):** the first look at the overlay (§10.4).

### 10.10 Unit 10 — the planetary sigil, built (2026-11-02)

**Built:** D-GN.28 to D-GN.33 (§8.2), with the developer's answers recorded there before any code. OQ-GN.1 to OQ-GN.3 took their
defaults, so Pr-17 and Pr-18 were not restated; OQ-GN.4 brought D-GN.31 to D-GN.33 and Pr-20 and Pr-21.

**One record, not a copy.** A planetary sigil is a `SigilProgressState` with `IsPlanetary` and 2n sets, the diurnal sigil's then
the nocturnal's. Everything `ApplyBet` does on a bet (V, the streak, D-GN.9's restart, D-GN.19's and D-GN.20's figures, `LeaveSlot`)
is the same code, and the sets running in list order **is** the alternation of D-GN.33: there is no second path that could repeat
a sigil. Only the end of a pass differs (`CompletePlanetaryPass`), plus one branch in `CurrentTarget`. `CompletedAtUtc` means *at
its limit* on this record, so every "a complete record takes no progress" reader, the stop's flag included, needed no new case.

**The gate is read when the record enters the slot** (`LevelLimit`), which keeps the rule free of the collection. It cannot go
stale: the combined level moves only when a sigil is drawn, and that takes the record out of the slot.

**The readers of `PlayerSigilProgress.Equipped` (Convention 13), all of them checked:**

| Reader | What it assumed | Now |
|---|---|---|
| `UserStatsService.LogSigilOutcome` | the slot holds what this bet drew | reads `LastDrawn`: **an automatic switch happens inside the bet**, and the slot's sigil would have printed as the one completed. Planetary lines of their own |
| The two `STOP on set completed` lines | the same | one `UserStatsService.StopPlayerSessionForSigil`, reading `LastDrawn` |
| Overlay `Repaint` | a sigil's levels and kind | `RepaintPlanetary`, its own words; the V chain as it is |
| `SigilCanvas` | one kind, n sets | maps each of the 2n sets through `PlanetarySet`, on the combined figure's ground |
| Collection: card, slot line, warning, `DescribePass` | a sigil's name | `NameOf`; the pass names which sigil it is in |
| `SigilLevel` | sets hold progress | a planetary record returns its activated level, rather than a plausible figure from passes that mean something else |
| `SigilCompletion`, `SetLevel`, `DescribeCompleted` | sets hold progress | **no planetary path reaches them**, checked by grep |

**"Stop on Sigil Set Completed" during an activation: it fires, on each set pass.** Its reason is unchanged (D-GN.10): between two
set passes a chance change costs nothing, and that holds here as much as on a sigil.

**The notice's edge needs no record of having fired.** A sigil's level rises one at a time, since no pass adds a whole level, so the
bet with `SigilLevelAcquired` that leaves its sigil on 1 with the partner at 1 or more is exactly the lower going 0 → 1. A level is
reached once in a world, and a restart that reverts it reverts the firing with it. Read only behind that flag, so an ordinary bet
pays nothing. **If a second planet reaches level 1 within the 10 minutes, it replaces the first** (one notice at a time; a default,
for the developer to overrule). **The notice is not a button:** the label reads as one, and clicking does nothing. The brief asked
for a label, so this is for the developer to judge on seeing it.

**Manual play, the question §8.2 left open: read in the code.** The clock moves by one fixed `GameSecondsPerManualBet` per press,
however many bets the press's credits fire (`AdvanceClockForBet`, once at `FinishManualBurst`), and not between presses:
`CalendarTimeService._Process` returns while `IsRunning` is false, and only an autobet sets it. So **in manual play a notice lasts 6
presses at any credit count, and it holds on screen indefinitely between them**, as it does after a stopped autobet.

**The refusal leaves the buttons enabled.** `SetBettingControlsEnabled` disables AUTO, and AUTO is also STOP: a lock composed there
could take STOP away from a paused run, which the code's own comments forbid. So the press does nothing and the warning on screen
says why; both bet paths also refuse at the bet itself (`RefusePlayerBetForSigil`), which covers a restart no button began.

**The preference reaches the rule from the overlay's `_Ready`.** `Scripts/Numerology/` cannot read `UserSettingsService`, and the
overlay comes with `DiceGame`, where every run starts, so it is set before any bet can be. `SetAutoSwitchOnCompletion` then also moves
a finished sigil on, if the toggle says so.

**The new DiceGame controls are in the scene**, `SigilNotice` and `SigilWarningLabel` in `DiceGame.tscn`, not built in code: the
roadmap's *UI built in scenes* item says code-built controls are not how this UI should be designed, and these two had no reason to
add to it. They sit in the free block under `BlockchainStatusValue` (y 732) and right of the bet history (x 1255), clear of the
bottom band. The notice has its own `_Process`, on only while it shows: the new row in §38.5.

**Pr-18 was REFUTED as written, and the model was what was wrong.** Raised to 40 runs a planet, Jupiter read low on all three
repeats (5.03σ, 2.20σ, 3.84σ) and Mars low on all three (3.01σ, 2.43σ, 4.01σ), so the rule applied: find which disagrees before
tuning. **At 12 runs it had passed, at 3.27σ.** An exact Markov chain over V, written to the game's roll rule, settles it. Per whole
pass at 95%, Jupiter: `Predicted` 1,212.6, exact 1,185.9, measured 1,183.3. Mars: 2,595.8, 2,563.3, 2,568.7. Saturn: 170.5, 170.5,
171.0. **`Predicted` treats targets as independent, and they are not:** a hit leaves V known, and hitting 10 leaves V on 1, which is
exactly what 11 asks for. **The activation's cost is its two sigils' cost apart**, in the chain and in the code, so nothing here
leaks; the zodiac sigils carry the same approximation. The harness now tests against the chain, which it can pass only if the code draws what the rule says, and prints all seven:

| One whole pass, 95% | Saturn | Jupiter | Mars | Sun | Venus | Mercury | Moon |
|---|---|---|---|---|---|---|---|
| Exact ÷ `Predicted` | 0.9996 | 0.9780 | 0.9875 | 0.9955 | 1.0023 | 0.9979 | 1.0007 |

So §8.2's figures hold to the precision they were sized at: **Saturn's 95 levels are 777,292 bets exact (777,600 stated), the Moon's
51,271,671 (51,235,200).** Pr-10's ±1% around Aquarius is untouched by a 0.04% difference. **Section 5 still tests against
`Predicted`**, with too few runs for this to show as a rule, but it did trip once: 4.02σ, Libra at 1% Low, in one of the six full
runs of this unit whose section 5 was read. No code it measures changed, and its chain would be its own exercise (the streak at 1–9%), so it is
reported here rather than fixed. A harness run that fails only on that cell is this; run it again.

**A check of mine was wrong, twice, in one shape.** Two tests raised a gate directly while the finished record sat in the slot, where
play cannot, and `StartActivation` rightly refused. Both then waited forever on a record taking no progress. **The rule was right and
the test was what hung**, for over nine minutes the first time. `PlayChance` now throws after 50 million bets.

**The harness:** sections 12 to 17, Pr-15 to Pr-21, each first proving its check can fail. Section 10's *"leaves and returns"* became
D-GN.28's own assertion. About a minute a run. **Cost, compared within one run:** the rule alone on Cancer 68.7 ns, through the slot
79.0 ns (1.15×), an empty slot 5.4 ns, an activation's ordinary bet on the Moon 96.7 ns, all at 0.00 bytes; a finished sigil in the
slot with nothing else to play also allocates nothing, so the clock can run on at full rate. An earlier run of the same code read
102.8 and 106.5 ns, for no reason looked into, so per Pattern 6's third rule none of these is evidence against another session's.

**Verified:** `dotnet build` clean; locale detector 17 / 0 / 0 / 0; the harness passes. **Not seen in game, none of it:** the panel's
new switch and its fit in the grid, the footer's toggle, the combined card's button, the planetary drawing, the notice and the warning
have only been compiled. **Their layout figures are arithmetic, not measurements**: the status label's six lines in 190 px, the preview's three
in 90 px, the warning's two lines in 590 px. **Untimed:** the collection view's cost at bet rate, as §10.9 said, and `RefreshSigilWarning`,
which runs only on a slot change or a completion.

**Overruled afterwards (developer, 2026-11-02), before any of it was seen:** D-GN.34 to D-GN.38, §8.2. **What this section says of the price, the one-record shortcut `CompletePlanetaryPass`, the
`LevelsPaidFor` value, the jump to level 13, the overpayment, a level credited only at a whole pass's end, Auto-switch never starting an activation, Stop as the default, the warning for
everyone, the label that is not a button, and Stop Set firing on each pass, all describe this unit's code and not the rule any more.** Unit 10b
changes them. The exact chain, the readers table, the single record of 2n sets, the alternation and the refusal's enabled buttons all stand.

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
