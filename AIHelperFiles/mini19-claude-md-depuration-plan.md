# Mini-Plan 19 — CLAUDE.md depuration: carve what the policy already mandates, and re-set a target nothing could reach

**Series note:** nineteenth of the *mini-plan* series. Documentation only — **no code, no measurement runs.** It
finishes what mini-plan 18 C started and stopped: that part extracted every *case narrative* it could place and
landed at 91,594 characters against a ≤88,000 target it refuted.

**Status:** 📋 **SPECIFIED 2026-10-07**, not started. Proposed branch `mini19-claude-md-depuration`.

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
