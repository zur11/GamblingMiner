# Mini-Plan 19 — CLAUDE.md depuration: carve what the policy already mandates, and re-set a target nothing could reach

**Series note:** nineteenth of the *mini-plan* series. Documentation only — **no code, no measurement runs.** It
finishes what mini-plan 18 C started and stopped: that part extracted every *case narrative* it could place and
landed at 91,594 characters against a ≤88,000 target it refuted.

**Status:** ✅ **DONE 2026-10-07** (close-out §7), branch `mini19-claude-md-depuration`.
**91,594 → 83,776 characters (−8.5%)**, target re-set 60,000 → **84,000** with its reasoning and the three declined
carves named. Most of the saving was **duplication, not relocation** — the file's own "never append a second
version" rule had been broken repeatedly, and the budget was the symptom. **I1's bolded-span differ was abandoned
as unworkable** (28 reported losses, all false positives); the check that worked was a distinctive token per
removed passage, 13 of 13 found. No code touched.

**Scope: Tier 1 + Tier 2, decided by the developer 2026-10-07.** Pattern 7's conventions stay (that carve would
reverse an August decision); the architecture blocks stay (they are the file's core).

---

## 1. Measured first, because the file's own policy says so

**91,594 characters.** Stated target 60,000 · warning 100,000 · hard limit 150,000. Every section ≥1,500 chars:

| chars | section |
|---|---|
| 12,153 | Pattern 2 — Checkpoint / Rollback |
| 9,735 | Canonical Decisions |
| 9,576 | Pattern 6 — Event-Driven vs `_Process` |
| 7,011 | Pattern 7 — Standing Conventions |
| 6,623 | Money Handling |
| 6,105 | UI Layout & Scrolling |
| 5,527 | Scripting tools (no Python) |
| 4,509 | Git Workflow |
| 4,437 | Key Architecture — Autoload Services |
| 4,395 | Architecture Documentation |
| 3,079 | Pattern 5 — Autoload Access |
| 2,521 | Auditing a playtest run |
| 1,810 | NAME THE PANEL |

Plus 26 sections under 1,500 totalling 14,084. **The eight largest hold 63,355 — and all of them are rules**,
which is why mini-plan 18 C's pruning stalled: *the rules are the text.*

---

## 2. The work

### A — the seven table rows the file's own rule already condemns

**"A table row or bullet growing past ~500 characters is becoming documentation. Extract it."** Seven Canonical
Decisions rows breach it, totalling **6,269 characters**:

| chars | row |
|---|---|
| 1,916 | Network fees — Historical Fee Replay |
| 1,051 | Swap desk fee |
| 877 | Player start |
| 754 | Timeline (DEV alt-timeline guard) |
| 589 | Specific split |
| 543 | SC value |
| 539 | BTC/SC trading unlock |

**Every one already names its full doc.** So trimming them removes a *second copy*, which the same policy forbids
twice over ("if the subject already exists, EDIT it — never append a second version"). Each becomes **the canonical
statement plus its pointer**, at ≤450 characters. Where a row's detail has no home, it is **moved there first**,
never dropped.

### B — Pattern 6's five instrument rules, whose cases now live in §40.12

Mini-plan 18 C gave them a home and left the summaries long. Compress to one line each; the numbered case detail
stays in §40.12.

### C — UI Layout & Scrolling, which already tells the reader to go elsewhere

Its own first line is *"READ `ProjectDesignManual.md` Chapter 29 first."* **Keep:** that trigger, and the bullets
that say **what not to write** (the `fit_content` trap, the footer-outside-scroll rule, the readout-precision rule,
the deleted-filter warning) — those are instructions, not description. **Move to Ch. 29:** the bounding-chain
walkthrough, the two scroll patterns, the mouse-wheel mechanics, the trailing-blank-lines tip, the scroll-position
restore. **Verify each exists in Ch. 29 before removing it here**, and add it there if not.

### D — Scripting tools → `Documentation/DEV_ENVIRONMENT.md`

A new doc for the machine's facts. **CLAUDE.md keeps three lines**, because each has cost a cycle when not known
up front: **there is no Python** (the Store aliases shadow it and every call fails) · **PowerShell is 5.1**, not 7
(no `&&`, no ternary, `Set-Content` is ANSI) · **`node -e` is the only open-tail interpreter rule left**, so path
permissions are advisory rather than a boundary. Everything else — the tool-choice table, the allowlist-audit
history, the PowerShell-7 deferral record — moves.

### E — re-set the target, with its reasoning

60,000 was set when this file was mostly prose. **Record the landed figure as the target**, and say why in the
Budget block: *a target nothing can reach trains the reader to ignore it* — the same failure the 100k-warning note
already describes about repeated warnings. Note the two remaining large blocks (Pattern 2, Canonical Decisions) and
that cutting them was considered and declined, so the next reader does not re-litigate it.

---

## 3. Invariants, registered before any edit — this plan has no predictions, it has things that must stay true

- **I1 — NO RULE IS LOST.** Verified mechanically: extract every bolded statement from CLAUDE.md before and after;
  **every one that disappears must be present, verbatim or clearly restated, at a named destination.** A diff, not
  an assurance.
- **I2 — every destination section EXISTS and contains the moved text**, checked by grep per passage. §4's rule
  from mini-plan 18 applies unchanged: *a half-extraction that leaves a rule without its case is worse than the
  characters it saves.*
- **I3 — the stored detector still runs.** Extract it **from the document** and execute it; it must return its
  baseline of **17**. This is the check that caught mini-plan 18 C's corruption, and parts A and D both touch text
  carrying `$`, backticks and quotes.
- **I4 — every edit to content carrying `$`, backticks or quotes uses the FILE TOOL**, never a shell rewrite.
  Standing Convention 14, which this plan family has now violated twice.
- **I5 — no code changes.** `dotnet build` is run to confirm the tree is untouched, not because anything compiled.

## 4. Decision rules

- **A passage with no existing home is MOVED THERE FIRST, in the same commit.** If that cannot be done, the passage
  **stays in CLAUDE.md** and the saving is forgone.
- **If I1's diff shows a lost rule, the commit does not happen** until it is placed.
- **The landed figure is whatever it is.** No target is chased: mini-plan 18's P5 failed precisely because a
  character number was set without checking what was extractable, and repeating that inside the plan that fixes it
  would be absurd.

## 5. Order

1. **I1's "before" snapshot** — every bolded statement, saved to the scratchpad.
2. **A** (table rows) → **B** (Pattern 6) → **C** (UI Layout) → **D** (scripting tools), each verifying I2 as it goes.
3. **I1's diff, I3's detector run.** Fix or revert anything they catch.
4. **E** — re-set the target to the landed figure with its reasoning.
5. **Close out.**

## 6. Out of scope

- **Pattern 7's fifteen standing conventions** (7,011) — carving them reverses the 2026-08-20 decision that moved
  them *into* this file because "a rule nobody can find is a rule nobody applies". Declined by the developer; the
  roadmap entry records it as available.
- **Pattern 2's checkpoint contract** (12,153) and the **Canonical Decisions table** itself (9,735) — the file's
  core. Row-level trimming only (part A).
- **The two queued code items** (`FirstBlockHeightMinedBy`, `BetHistoryItem`'s timestamp) — unrelated to this
  file's size, and each needs its own plan.

---

## 7. Results and close-out (2026-10-07)

**91,594 → 83,776 characters (−7,818, −8.5%).** Target re-set to **84,000**. No code touched; build clean.

| part | saved | what moved, and where |
|---|---|---|
| **A** — seven over-long table rows | **2,339** | Band derivation, the swap formula and cap, the timeline mechanics, the trading-unlock justification → the step-13/14 plans, `SERVICES.md`, Ch. 35. The worst row went **1,916 → 849**. |
| **B** — Pattern 6's instrument rules | **1,466** | Narratives already in §40.12; six one-line rules kept. |
| **C** — UI Layout & Scrolling | **2,037** | The bounding walkthrough, the two patterns, the wheel mechanics → Ch. 29, **verified present there before removal**. **Every DON'T kept** — eleven of them, each grep-checked after. |
| **D** — scripting tools | **~2,000** | → new **`Documentation/DEV_ENVIRONMENT.md`** (6,048 chars, moved verbatim). CLAUDE.md keeps three facts, chosen because each is a trap you hit *before* you would think to open a doc: no Python, PowerShell is 5.1, `node -e` makes path permissions advisory. |
| **E** — the target | — | 60,000 → 84,000, with the reasoning and the three declined carves named so they are not re-litigated. |

### Invariants

- **I2 ✅** — every removed detail verified present at a named destination by **distinctive token** (`MaxFeeMeanMultiplier`, `D-SW.12`, `FirstDataDateLocal`, `Blockchair`, …): 13 of 13 found. Ch. 29 checked for all seven moved UI topics before anything was cut.
- **I3 ✅** — the detector, extracted **from the document** and executed, returns **17**. Run again after the final edits: still 17. Pass 2 / `CurrentCulture` / date-names all 0.
- **I4 ✅** — every edit to text carrying `$`, backticks or quotes used the file tool. The two shell rewrites used were on *generated* content (a line-range splice and a heredoc), not on literal rule text.
- **I5 ✅** — zero `.cs` files changed; `dotnet build --no-incremental` clean.
- **I1 ⚠ ABANDONED, and this is the finding.** The bolded-span differ was supposed to be the mechanical net under I2. It does not work: the regex captures **fragments**, so reworded bolding reads as "gone" (37 of 394 spans), and my attempt to fix it with a content-presence probe was itself broken — the probe's word filter dropped words under three characters, producing sequences that could never match the haystack. It reported **28 losses, every one a false positive.**
  **The lesson, which is this plan's third instance of the same shape:** a one-off verifier written in the same session as the edit it checks is as likely to be the thing that is wrong. Mini-plan 18 had the same experience twice (a trace column read as milliseconds, a correlation against a constant). **What actually worked was the boring check: a distinctive single token per removed passage, grepped across the corpus** — immune to rewording, trivially auditable, and already run as I2's pre-check before each edit. **I1 should have been specified as that, not as a differ.**
- **A small self-referential trap, recorded because it is funny and real:** part E set the target to the measured figure, and writing the explanation of the target then pushed the file past it. The number now accounts for the paragraph that states it.

### What this plan is worth

1. **Most of the saving was DUPLICATION, not relocation.** The locale block (mini-plan 18 C) was a second copy of §29.12; the fee row's detail was a second copy of the step-14 plan; the Player-start row restated Pattern 2's own rule. **The file's "never append a second version" rule had been broken repeatedly, and the budget was the symptom.**
2. **The policy did the work once it was measured.** Every part of A, B and C was mandated by a rule already in the file — the ~500-character row limit, "if the subject already exists, EDIT it", and a section whose own first line says to go read a chapter. **Nothing here required taste.**
3. **The target was wrong, and saying so is the result.** 60,000 was unreachable without moving architecture rules out. Three carves were considered and declined with reasons. **A target that cannot be met trains the reader to ignore it** — which is the same mechanism the file's own warning-suspension note already describes.
