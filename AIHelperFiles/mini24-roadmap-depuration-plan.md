# Mini-Plan 24 — PRIVATE_ROADMAP depuration: only what is open stays

**Series note:** twenty-fourth of the *mini-plan* series. **Documentation only — no code, no runs.** Asked for by the
developer on 2026-10-12, after mini-plan 23, together with a handoff prompt for the next agent. That prompt is a chat
artefact, not a repository file.

**Status:** ✅ **COMPLETE (2026-10-12)**, branch `mini24-roadmap-depuration`. Specified, done and closed in one unit,
because nothing in it is measured.

---

## 1. What was wrong

`Documentation/PRIVATE_ROADMAP.md` had reached **162,826 characters / 1,495 lines**, and about 70% of it was finished
work:
- every mini-plan 08–23 entry;
- PH, P3, P4, P10, P15.11;
- T1–T3, and T4's finished parts;
- the ticked half of §6.

All of it is already recorded in `IMPLEMENTATION_STATUS.md` and in each plan. Worse, several entries **still read as
open**:
- the "▶ NEXT" order of 2026-08-21, which still listed mini-plan 06 as pending (it finished 2026-08-26);
- §5's pointer to `IMPLEMENTATION_ROADMAP.md` as "authoritative" (untouched since 2026-06-30);
- P15.11 "SCHEDULED, blocks P15.8";
- mini-plan 20 C's strobe entry "DIAGNOSED";
- an answered §7 question (when BTC trading unlocks);
- the old game-over rule in §3, §4 and §6.

## 2. Decisions (developer, 2026-10-12)

- **D-24.1 — finished entries move to an archive**, verbatim and under their original headings:
  `Documentation/ROADMAP_ARCHIVE.md`. They are not condensed into one-liners.
- **D-24.2 — the Casino Referral System is Post-Basic Mode; the Casino Rank System stays in Basic Mode.** The
  auction's Basic-Mode reward is the companies it founds, so the commission, the `Referrals` scene and Miner Referral
  conversion moved under §5's Post-Basic sections. The developer then corrected the first draft, which had moved the
  rank as well: **the rank is Basic Mode, with a design plan before its implementation plan.** It now sits after P8.
  Its design must first say what the rank drives in Basic Mode, now that the commission it was written for is
  Post-Basic.
- **D-24.3 — the next working order:** Step 17.A + 17.C → user settings persistence → Player Holdings Hub → Betting
  Statistics (17.B) and the Basic Mode economy (P2, P5–P8, the Casino Rank System), each with a design round. The
  rank's is a design plan of its own.
- **D-24.4 — DiceGame's "Numbers" bet view becomes a small Basic Mode item.** Mini-plan 23's row and latest-only
  components solved its cost. It is folded into whichever UI plan comes first.

## 3. What was done

- **Assembled by line range, never by rewriting.** Each block was copied whole into the roadmap or the archive. New
  text was written only for:
  - the new §5 header and "▶ NEXT";
  - a short Option-B entry replacing the fee-market research record;
  - the Post-Basic note for referral and rank;
  - the open half of §6;
  - T1–T3's and T4's status lines.
- **Verified two ways** (Convention 16's boring check):
  - every original heading is present in one of the two files;
  - every non-blank original line is present in one of them, except the single line deliberately rewritten (the
    Numbers view), whose original text was then appended to the archive. Final count: **0 missing.**
- **Small corrections after the move:**
  - Main Risks annotated as of 2026-10-12;
  - game over now includes the Private Bank Account (§3, §4);
  - the answered §7 trading-unlock question removed (the decision is CLAUDE.md's Canonical Decisions row);
  - P7 annotated with what Step 13 shipped;
  - the settings table gained the explorer's winning-numbers view;
  - the Post-Basic checklist gained referral/rank and Option B;
  - CLAUDE.md's pointer to the renamed fee-market section updated.

**Result:** `PRIVATE_ROADMAP.md` **162,826 → ~63,000 characters**; `ROADMAP_ARCHIVE.md` ~113,000. §5–§8 keep their
numbers, because CLAUDE.md and the Manual cite `PRIVATE_ROADMAP.md` §6 and §8 T4/T5.

## 4. Left open, on purpose

- **"Why 60,000 was unreachable"** stays in the roadmap. Extracting Pattern 7 into a `STANDING_CONVENTIONS.md` is
  still the developer's decision, unmade.
- **P0 and P1** stay as written. Whether `PLAYER_GUIDE.md` is current and whether "performance against the 40,000 SC
  baseline" is shown anywhere were not verified here.
- **Step 17's plan text** is not edited. "▶ NEXT" lists what to re-verify in it before relying on it.
