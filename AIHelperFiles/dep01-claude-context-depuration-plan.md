# Dep-01 — Claude Context Depuration: the policy, the last cuts, a guard, and a contradiction sweep

> **First entry of the *depuration* series** — a third track beside the `stepNN` plans (product) and the
> `miniNN` plans (bounded work). What distinguishes it: **its subject is the system Claude reads, not the
> game.** `CLAUDE.md`, the `Documentation/` set, and the Claude Code configuration are project artefacts
> with their own failure modes, and they had never had a plan of their own.
>
> **Status: SPECIFIED, NOT STARTED (2026-08-21).** Branch `claude-context-depuration` off `main`.
> **No game code is touched by any phase.**

---

## 1. Where this started, and why it was urgent

The work began mid-way through mini-plan 05, **out of scope and out of order**, because the problem was
discovered rather than scheduled: `CLAUDE.md` had reached **228,348 characters** and nobody was aware of
it. Every session was paying for it in every message.

The commits below are that emergency pass. They are listed here because **this plan is their continuation,
not a new idea** — the series starts at Dep-01 but the work started at `761265c`.

| Commit | What it did | `CLAUDE.md` after |
|---|---|---|
| *(before)* | — | **228,348** |
| `761265c` | extract the referral auction spec → `REFERRAL_AUCTION.md` | 196,244 · **−32,104** |
| `56bd28a` | ignore `*.bak` scratch files | 196,244 |
| `f1aef97` | list `REFERRAL_AUCTION.md` where the other design docs are | 196,887 |
| `52f11f5` | complete the `Documentation/` file tree | 197,072 |
| `f992049` | extract Implementation Status → `IMPLEMENTATION_STATUS.md`, promoting its buried rules | 139,350 · **−57,722** |
| `01a7b75` | correct four wrong roadmap verdicts in the extraction note | 139,350 |
| `9d2a04c` | extract Autoload Services → `SERVICES.md`, leaving a verified index | 97,409 · **−41,941** |
| `a3c8508` | promote the SC value decision to Canonical Decisions | 97,959 |
| `d36b5bc` · `9cebc8f` · `1fc97bc` | correct three swap-desk claims describing a retired fee model | 97,959 |

**Net: −130,389 characters, a 57% reduction**, all of it merged to `main` in `6f4b475`.

Two of those three big cuts match the shape the developer named: **a single table cell holding 32,104
characters**, and **a section of 57,722 characters of design record labelled as status.**

> **The failure was not that the file was long. It was that nothing measured it, so nobody could notice.**
> Everything below exists to make the next 130k impossible to accumulate unobserved — which is why the
> policy comes before the cuts and the guard comes before the sweep.

---

## 2. Current state, measured (2026-08-21)

`CLAUDE.md` — **97,959 characters**, by section:

| Section | chars | Phase |
|---|---|---|
| Important Patterns | **26,137** | D2.4 — trim examples, keep the rules |
| Core Game Systems | **15,461** | D2.2 — extract |
| Code Conventions | 14,694 | *stays* — permanent instructions |
| Canonical Decisions | 8,312 | *stays* |
| Scene Management | **8,267** | D2.3 — extract |
| Development Best Practices | 6,491 | *stays* |
| File Organization | **4,302** | D2.1 — delete |
| Key Architecture — Autoload Services | 4,228 | *stays* — already an index |
| Architecture Documentation | 3,254 | *stays* — the useful index |
| Data Models | **1,235** | D2.2 — extract |
| *(nine smaller sections)* | 5,578 | *stays* |

**The four D2 targets total 55,402 characters** — 57% of what is left. Removing or trimming them lands the
file near the 60k objective without touching a single permanent instruction.

**Where the extracted material now lives** — and the reason D4 exists:

| | chars |
|---|---|
| `ProjectDesignManual.md` | 651,414 |
| `PRIVATE_ROADMAP.md` | 87,028 |
| `IMPLEMENTATION_STATUS.md` | 65,318 |
| `SERVICES.md` | 50,118 |
| `GLOSSARY.md` | 32,071 |
| *(four others)* | 63,808 |
| **Total `Documentation/`** | **949,757** |

> **Extraction does not delete a fact; it moves it, and briefly there are two copies.** Every extraction
> this series performs is a potential redundancy finding for D4 — the two phases are coupled, and D4 is
> what stops the cleanup from having quietly created the next problem.

---

## 3. D1 — The Document Policy

**Ships first, because it defines the criteria every later cut uses.** A new section in `CLAUDE.md`,
placed right after Project Overview so it is read before anything else, and **under 3,000 characters** —
*a policy that needs 10k to explain itself is already part of the problem.*

Four parts:

1. **What belongs** — permanent instructions governing future work: code conventions, invariant rules,
   canonical decisions (the statement, not its history), indexes pointing at where detail lives.
2. **What does not, and where it goes instead** — the history of how a decision was reached → the plan or
   manual that recorded it · what is implemented → `IMPLEMENTATION_STATUS.md` · system specifications →
   their own doc · long code examples → the system's doc · file trees and listings that go stale by
   themselves → **nowhere; read them from the filesystem.**
3. **The procedure before writing here**, mandatory and in order: (a) search `CLAUDE.md` *and*
   `Documentation/` first — if it exists, **EDIT it, never append a second version**; (b) if the new
   contradicts the written, **do not write both** — verify against the **CODE**, correct the false one, and
   tell the developer; (c) if it is unclear whether something belongs here or in a doc, **ask before
   writing**; (d) a table row or bullet past ~500 characters is turning into documentation — extract it.
4. **The budget** — 60k target, 100k warning, **150k hard limit** (where Claude Code reports it at
   startup). Crossing 100k while writing must be reported **in that same reply**, with a proposal of what
   to extract.

And two lines on why it exists, with the numbers from §1, so the policy carries its own evidence.

**Commit: its own.**

---

## 4. D2 — Finish the cuts, applying the policy just written

One section at a time, **a commit per section**, reporting size before and after.

| | Section | Action |
|---|---|---|
| **D2.1** | `## File Organization` (4,302) | ✅ **Done** — deleted. See §4.1 for what the measurement found |
| **D2.2** | `## Core Game Systems` (15,461) + `## Data Models` (1,235) | ✅ **Done** — → `Documentation/ARCHITECTURE.md`; index + three embedded rules kept |
| **D2.3** | `## Scene Management` (8,267) | ✅ **Done** — **rebuilt** into `Documentation/SCENES.md` from `SceneManager`, not moved; three false claims recorded there in §5 |
| **D2.4** | `## Important Patterns` (26,159) | ✅ **Done** — 26,159 → 20,845. **The phase's premise was wrong and the report caught it** (§4.2) |

**Standing rules for every one of them:** verify against the code any claim that is kept, never infer from
a file name, UTF-8, and index the new docs where the others are indexed.

> D2.4's "report before touching" is the phase's most important instruction, not a formality. **The split
> between rule and illustration is a judgement, and making it visible before acting is what lets the
> developer overrule it while it is still cheap.**

### 4.1 — D2.1's finding: the tree was not an inventory, it only looked like one

Checked before deleting, because a delete is the one edit that cannot be reviewed afterwards:

- **The `Documentation/` branch was fully redundant** — all 9 docs are in the Architecture Documentation
  table that D2.1 keeps. Nothing lost.
- **The `Screens/` branch listed 16 of 24 real entries.** Missing: `BTCPoolsAndHardwareShop`, `BTCWallet`,
  `BotPlayHistory`, `BotsBtcWallets`, `CasinoFinances`, `FoundersWallets`,
  `MartingaleCalculatorStandalone` — and **`MainMenu`**, the entry point of the whole navigation graph.

> **It had been stale for so long that it was wrong about a third of its own subject, and nothing showed
> it, because a tree carries its authority in its shape.** It reads as complete whether or not it is. That
> is the argument for the policy's "file trees go nowhere" rule stated better than the rule states it: the
> problem is not that they go stale, it is that they go stale *invisibly*.

**Carried forward to D2.3, which must not inherit the same defect:** the `### Navigation Map` inside
`## Scene Management` is missing **nine** scenes — the eight above plus `CastMinerWallets` and
`CompaniesWallets` (`MartingaleCalculatorStandalone` appears in prose there but not in the map).
**`Documentation/SCENES.md` must be built from the filesystem and verified against `SceneManager.SceneId`,
never by moving the existing map across.**

*Two scenes — `CompaniesWallets` and `CastMinerWallets` — were named ONLY in the deleted tree. They are not
lost: they are on disk, they are in `SceneManager`, and D2.3 inventories them properly. What is gone is the
appearance of a complete picture that was never complete.*

---

## 5. D3 — The size guard (hook)

A Claude Code hook watching `CLAUDE.md`'s size.

**Verify the mechanics against the official documentation before writing it**
(`code.claude.com/docs/en/hooks`). The developer's understanding, to be confirmed and corrected if stale:

- configured in `.claude/settings.json` under `"hooks"`, three levels: event → group with `"matcher"` →
  handlers;
- `PostToolUse` with matcher `"Edit|Write"` (the matcher tests against `tool_name`);
- the handler receives JSON on stdin; the path is at `tool_input.file_path`;
- **the critical detail: in `PostToolUse`, stderr with exit 0 goes only to the debug log and Claude does
  not see it. Exit 2 is what surfaces it. Exit 1 does nothing — the classic mistake.**

**Behaviour**

| Size | Action |
|---|---|
| Not `CLAUDE.md` | exit 0, silent |
| under 100,000 | exit 0, **total silence** — *a noisy hook gets ignored* |
| 100,000–150,000 | exit 2, stderr: current size, growth against the 60k target, and that it is time to propose what to extract, citing the Document Policy |
| over 150,000 | exit 2, stronger: the limit where Claude Code reports it at startup has been crossed, and every message of every session pays for it |

**Implementation:** Node (v22 installed; **there is no Python on this machine** — Development Best
Practices). In `.claude/hooks/`, path via `$CLAUDE_PROJECT_DIR`.

**Test it manually with JSON on stdin for all three cases and show the output of each before accepting it.**

### 5.1 — Two questions to answer, one of which matters more than the hook

**(a) `settings.json` or `settings.local.json`?** Measured on this machine: `.claude/` holds only
`settings.local.json`, nothing under `.claude/` is tracked, and `settings.local.json` is ignored by the
**global** git ignore (`~/.config/git/ignore`), not by the repo's. So the versioned choice is
`settings.json`, which does not exist yet.

**(b) Does the hook fire when the DEVELOPER edits the file by hand in VS Code, or only when Claude edits
it?** Report what the documentation says. **If an event covers the manual case, name it but do not
implement it yet.**

> **(b) is the question that decides whether this guard is worth anything.** A `PostToolUse` hook sees only
> Claude's edits — and a file that reached 228k did so over months, through both hands. **A guard that
> watches one of the two ways a file grows will report a clean bill of health while the file doubles.**
> Answer it before the hook is called done.

---

## 6. D4 — Contradiction and redundancy sweep (report only)

**The highest-value phase, and the one never yet done.** Compare `CLAUDE.md` against `Documentation/` for:

1. **Contradictions** — two places asserting incompatible things. Three surfaced in a single day during the
   emergency pass (autoload access, the false Hardware-cap conflict, GLOSSARY entries 32 vs 73). **For each
   one, verify against the CODE which is true.**
2. **Redundancy** — the same fact asserted in two or more files. *The risk is not the space: it is that
   updating one leaves the other lying.*
3. **Present-tense claims inside historical records** — a dated chapter saying "is pinned at", which reads
   as a live rule.

**Output: a table** — location A, location B, what each says, which was verified correct, severity.
**Edit nothing.** The developer decides case by case.

If one pass is too large, **start with `CLAUDE.md` against `GLOSSARY.md`**, which is the source of truth for
terminology.

> Category 3 is the subtlest and the project has already been bitten by it: a design record written in the
> present tense is indistinguishable from an instruction, and `CLAUDE.md`'s own history is largely the
> story of records that were read as rules. **The tense is load-bearing.**

---

## 7. Ordering, and what is out of scope

**D1 → D2 → D3 → D4.** The policy first because it supplies the criteria; the cuts second because they are
what the policy authorises; the guard third because it protects the result; the sweep last because it is
the only phase whose findings depend on where everything has finally landed.

**Out of scope**

- Any game code. This branch touches documentation, `.claude/` configuration, and nothing else.
- `ProjectDesignManual.md`'s own size (651k). It is a long-form record read on demand, not context loaded
  every session — a different problem, if it is one at all.
- The `Documentation/` files' internal quality beyond what D4 reports. D4 reports; it does not fix.

### 4.2 — D2.4's finding: the phase's own premise was wrong, and the mandatory report caught it

D2.4 was specified as *"what leaves is the long code examples"*. **Measured before touching anything, as
the phase required: only 477 of 26,159 characters were code blocks — 1.8%.** There were no long examples
to remove.

What the section actually held was different, and more delicate. Two patterns were **74%** of it with zero
code between them (Checkpoint/Rollback 9,744 · Prefer Event-Driven 9,697), and inside them six paragraphs
carried 9,416 characters in one recurring shape: **a dated incident narrative followed by the rule it
earned.**

**And every narrative was a second copy.** Each cited its own home and the home had it — INC-001 and
INC-002 in `INCIDENT_LOG.md`; §22.18, §22.20, §38.5, §38.7, §40.7, §40.8 in `ProjectDesignManual.md`. So
the trim removed duplication rather than content.

| Kept | Removed |
|---|---|
| Every rule, verbatim or tightened | The story of the incident that produced it |
| A clause of *why*, where one line does it | The measurements, the dates, the fix's implementation detail |
| A pointer to the case | — |

**One entry was deleted outright rather than trimmed:** the copy of Ch. 38's migration-candidate catalogue.
It was duplicated *and* stale — the same trap D2.1 and D2.3 each found once. It is now a pointer that
carries Step 17 §5.1's warning to re-derive the list from the code.

**And `### 7. Standing Conventions` was left untouched, deliberately.** Thirteen numbered rules averaging
240 characters each; all of it is rule. Trimming it would have been trimming to reach a number rather than
to apply the policy.

> **The instruction that made this phase work was "report before touching".** It was written as a courtesy
> — a chance for the developer to overrule a judgement while it was cheap — and what it actually did was
> falsify the phase's own premise before a single edit. **A plan that requires measurement before action
> gets to be wrong safely.**
