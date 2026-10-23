# Mini-Plan 29 — The Player Holdings Hub, and R4: a world where every strategy is known

**Series note:** twenty-ninth of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 3**, the Player
Holdings Hub, and opens **R4**, a new test world whose strategies are fixed for the whole run. The developer chose
both on 2026-10-23, after mini-plan 28's close-out: "use the roster", and write this plan "following all your
recommendations".

**Status:** 📝 **SPECIFIED 2026-10-23.** Nothing built. Next: the **world reset W** (§4), which is also mini-plan 27's
pending check S8. Then R4's first session (§5), then unit 1 (§6).

**Branch:** `mini29-holdings-hub-r4` (proposed, D-29.1).

**World treatment: one reset, at the start, by the player's own "Reset world".** R3's world (v12, ~2016-06, height
4,047) is retired. Its final tally is recorded in `step17-ui-access-and-event-driven-audit-plan.md` §9.16, and its trace
is archived as `user://logs/rank_calibration_R3.csv` (renamed 2026-10-23, sha256 `f8174bc41ec2325e…`). **No format
bump:** the world stays v12.

**Prior art this plan consumes rather than re-derives:**
- The hub's specification: `PRIVATE_ROADMAP.md` §5, "Player Holdings Hub". Its rule: **reuse the existing signal
  sources, never re-derive them** (Convention 6). An index that disagrees with the page it points at is worse than
  none.
- R1–R3's method and results: `step17-…-plan.md` §9.1–§9.16. In particular, R2 measured the average-bet multiplier
  `m = wagered ÷ (bets × base)` for each progression (§9.13), and §9.7 is the session protocol R4 reuses.
- UI layout: `ProjectDesignManual.md` **Ch. 29**, read before the hub's scene is built (CLAUDE.md, UI Layout).

---

## 1. Why this plan exists

- **The hub.** The only way to reach a company the player holds stock in is to hunt for it among all 40 in
  `BlockExplorer`'s Enroll Mode. Votes, claims and FBI warnings live in five scenes, and nothing says "something is
  waiting for you". Roadmap item 3 calls it "the highest engagement-per-line change available".
- **R4.** R3's totals per client are exact, but **which strategy produced them is lost** (§9.16): the strategies
  changed across sessions, and the game records no per-strategy split. A test world is only as useful as what can
  be attributed in it. R4 fixes every client's strategy before the first bet. It also gives the hub what it needs to
  be verified: a player who actually holds stock. R3's player held none.

## 2. Scope, and its one rule

**In:** the world reset (W, which runs S8's "Reset world" half), R4's setup and runs, the hub (an index screen plus a
StatusBar badge), and deleting `LegacyPlayerFileRelocation` at close-out (scheduled by mini-plan 27).

**The rule while R4 is open: no unit of this plan changes betting, mining, bidding or any economy code.** The hub is
display-only. Everything it shows already exists and is already computed. A change to how money moves would split
R4's data into a before and an after, which is exactly R3's problem.

**Out, each with its home:**
- **The casino bots' bidding preference** (the roadmap's "bots prefer to bid on companies with more BTC received"). It
  changes how bots spend BTC, its design questions are undecided, and the roadmap says it must not land while a
  calibration run is open. **R4 counts as open.** It becomes its own plan, with a design round, after R4 closes.
- **Bulk management of the per-company pause/abstain toggles** from the hub. The roadmap calls the hub "the natural
  place" for it; it is a second step, after the index exists and has been used (D-29.5).
- **"Wipe everything"**, S8's other half. It would delete the saved strategies R4 needs. It stays pending.

## 3. Predictions (registered before any data)

### 3.1 W, the reset

| # | Prediction | How it is read |
|---|---|---|
| **W1** | "Reset world" runs the one `WipeWorld`. `godot.log` lists every deleted file, and the next launch runs the historical bootstrap from genesis to 2009-03-21 | `godot.log` |
| **W2** | **Untouched, byte for byte** (sha256, first 16 hex, taken 2026-10-23 before the reset): `saved_betting_strategies.json` `10fab9ed593e7122` · `settings.json` `749b1e4d92832838` · `rank_calibration_R3.csv` `f8174bc41ec2325e` · `bot_wallet_registry.json` `3803f1eaf2937dfe` · `wordlist_256.json` `8aef71a67a98eb44` · `wallet_state.json` `845128a05226ef8b` · `casino_wallet_state.json` `eea85a50580ff8be` · `satoshi_wallet_state.json` `2747c7160b258259` · `hal_wallet_state.json` `2aba60a0d44d4dc9` · `mike_hearn_wallet_state.json` `120e6e89df092828` · `world_timeline.stamp` `51bb9a965df368ec`. And every other file in `logs/` is still there | Claude hashes them after the reset |
| **W3** | **Reset:** the chain restarts at the bootstrap's height. The player has 40,000 SC Main / 0 Bankroll before DiceGame is opened (the Canonical Decisions table's lazy split), and **every node is back to 1 hardware credit** (mini-plan 27 §3.5): `hardware_allocation.json` (`7a21a42a44c9d375`, the player at 10) is gone or rewritten. `world_format_version.txt` still says 12 | the files and the first boot's log |

### 3.2 R4, the roster (the developer: "use the roster")

**Every client at 1 credit** (the reset's default), so all five make the same number of bets and only the strategy
differs. The DEV time scale is 9000X. Auto Recharge is ON. No profit or loss stop except `bot_2`'s. Stop on block
mined is OFF.

| Client | Saved strategy | Base | On loss | Chance | Payout ×  | What it tests |
|---|---|---|---|---|---|---|
| `bot_1` | `R1 0.01` (exists) | 0.01 | 0% | 50 | 1.9804 | **the control** |
| `bot_2` | `R2 L100 SL5` (exists) | 0.01 | +100% | 50 | 1.9804 | a martingale with a stop-loss: how often it is cut |
| `bot_3` | **`R4 L50 0.05`** (new) | 0.05 | +50% | 50 | 1.9804 | R2's +50% at 5× the base: does the bankroll start to bind? |
| `bot_4` | **`R4 C10 0.1`** (new) | 0.1 | 0% | **10** | 9.9020 | a long-shot, flat |
| `player` | **`R4 C90 0.1`** (new) | 0.1 | 0% | **90** | 1.1002 | a near-certainty, flat. Same wagered as `bot_4`, opposite variance |

`bot_4` and the player make the cleanest pair: same base, same bets, same wagered, so the **same expected loss**.
Only the variance differs. The chance goes on the player deliberately: only the player has a full rollup
(streaks, drawdown, segments by chance), so the extreme it can measure in detail is the one it carries.

**Length:** to at least **2011-06-30**, about 2.3 game-years, as in R2. That is long enough for several companies to
be founded and for their first votes to close, which the hub needs.

### 3.3 R4's predictions

`N` = bets per client. `p` = the win chance, `M` = its payout multiplier (`DiceEngine.CalculateMultiplier`, 4 dp).
Edge 0.98%. Sources: the new `rank_calibration_trace.csv` (per block, per client), the client book, the rollup.

| # | Prediction | Refuted if |
|---|---|---|
| **R4-P1** | **315,576 bets per game-year for every client**, exact (the 100-second rule at 1 credit) | any client off by more than one block's bets |
| **R4-P2** | **`m` for the flat clients is exactly 1.0000** (`bot_1`, `bot_4`, the player). This also tests §9.16's open question: a flat average below its base would mean bets are clipped to a short bankroll | any flat `m` ≠ 1.0000 |
| **R4-P3** | `bot_3` (+50% at base 0.05): **m = 2.000 ± 3%**, R2's figure at 1/5 the base | outside 1.94–2.06. Below means the bankroll binds at this base |
| **R4-P4** | `bot_2` (`R2 L100 SL5`): **m between 5 and 8** (R2: 6.16) | outside 5–8 |
| **R4-P5** | Each flat client's net is within **±3σ of −0.0098 × wagered**, with σ = base × M × √(N·p·(1−p)) | any flat client outside ±3σ |
| **R4-P6** | **Same expectation, ~9× the swing:** the standard deviation of per-block Δnet is **9.0× larger for `bot_4` than for the player** (M·√(p(1−p)) = 9.902 × 0.3 against 1.1002 × 0.3), ±10%. Over one game-year that is σ ≈ 167 SC against ≈ 19 SC, around the same expected −309 SC | outside 8.1–9.9× |
| **R4-P7** | The player's (90%) longest losing run is **5–8** (≈ ln N ÷ ln 10), and its longest winning run is **100–160** (≈ ln N ÷ ln(1/0.9)), over ~726,000 bets | either outside its band |
| **R4-P8** | No client is ruined: every client still bets at the run's end | a client with 0 bets in its last session |

**Not a prediction, a setup step:** the player donates BTC to **at least two auctioning companies**, so that it holds
NST or PST when they are founded. The step is given when the first auctions are open (§5). Without it, the hub has
nothing to show.

### 3.4 The hub

| # | Prediction | How it is read |
|---|---|---|
| **H1** | The hub lists **exactly** the companies whose founding holds the player with NST > 0 or PST > 0, and no other. Dissolved holdings appear as terminal grey rows, as in the explorer | a DEBUG `[Holdings]` line per row in `godot.log`, against `state.json`'s `CompanyFoundings` |
| **H2** | Each row's border (gold NST / silver PST), work state (red vote · green claim · mocha both) and button text are the **same** as the explorer's row for that company, **because both read one helper** (unit 1), not because two copies happen to agree | the code: one helper, two callers, grep-verified; and H1's lines |
| **H3** | The StatusBar badge shows **"Holdings: N waiting"**, N = companies with a vote awaiting the player + companies with a claimable dividend, each company counted once. It is hidden when the player holds nothing. It refreshes on `NetworkRoot.BlockAccepted`, which the StatusBar already subscribes to: **no new `_Process` work** (Pattern 6, §38.5) | the code, and a DEBUG line at each change of N |
| **H4** | The hub's scene follows Ch. 29: one scroll pattern, the Back button in a fixed footer outside the scroll, nothing in the bottom ~50 px | the developer opens it once (a look, not a watch) |

## 4. W — the reset (the developer runs it; Claude reads the files)

1. Launch from the Godot editor. Main Menu → **User Settings** → **Reset world** → confirm. The game quits, as it does
   after any reset request.
2. Launch again and wait for the main menu. The reset runs now, then the historical bootstrap. Exit with **Main Menu →
   Exit Game → Exit**, without opening DiceGame (W3 reads the balances before the lazy split).
3. Tell me "W done". I read `godot.log` and hash the files (W1–W3).

## 5. R4 — sessions

**First session:**
1. Launch, Main Menu → **DiceGame**. Don't touch the DEV credits selector: every node stays at 1 credit.
2. **Save three new strategies** (once only; they survive future resets):
   - **`R4 L50 0.05`**: base **0.05**, Increase on loss **50%**, Increase on win 0%, no profit/loss stop, Stop Block
     OFF, chance **50**.
   - **`R4 C10 0.1`**: base **0.1**, both increases 0%, no stops, Stop Block OFF, chance **10**.
   - **`R4 C90 0.1`**: base **0.1**, both increases 0%, no stops, Stop Block OFF, chance **90**.
3. With the active-node selector, load **`R1 0.01` on `bot_1`**, **`R2 L100 SL5` on `bot_2`**, **`R4 L50 0.05` on
   `bot_3`**, **`R4 C10 0.1` on `bot_4`**, then select **`player`** and load **`R4 C90 0.1`**. Auto Recharge **ON**.
4. DEV ⏩ Time: **9000X**. With `player` selected, press **AUTO**. The four bots start with it.
5. Let it run for a time you choose, then **STOP** and exit the usual way. Tell me the session's real length.

**Later sessions:** the strategies are saved per node in the preferences now (mini-plan 27), so they come back armed.
Check that the five nodes show their strategies, then 9000X and AUTO. **Never change a strategy while R4 is open.** If
one is changed by accident, say so: the plan records the block where it happened.

**The auction step:** when Claude reports from a session's trace that auctions are open (expected ~2010-2011), Claude
gives a short step for the player to donate BTC to two of them. The amounts are chosen then, from the player's
mined BTC.

**Reading:** after every session Claude reads the trace and `godot.log`, and keeps a per-session table in §8.

## 6. Units (display-only, built while R4 runs)

- **Unit 1 — one source for a company's player signals.** Extract from `BlockExplorer`'s founded-company row: the
  stake border colour (from the founding's holdings), the work state (`GetCompaniesAwaitingPlayerVote`,
  `HasPlayerClaimableDividends`), the button text, and the terminal row for a dissolved company. They move into one
  static helper that both the explorer and the hub call. **No visible change in the explorer** (Convention 6: the
  hub's row and the explorer's row are then the same function).
- **Unit 2 — the hub scene.** `SceneId.PlayerHoldings` (the three steps in CLAUDE.md, Scene Management), reached from
  a MainMenu button **"My Companies"** and from the badge. One row per holding: name (`DescribeCompany`), NST / PST
  held, the helper's border, work colour and button (→ `CompanyDetails`, via `PendingNonMinerAddress`), the FBI
  warning (`GetFbiInvestigationWarning`), and for a bank its lending summary (`GetBankLendingSummary`). An empty state
  says what to do: "You hold no company stock yet: bid in an auction from the Block Explorer's Enroll Mode." Back
  returns to `PreviousScene ?? MainMenu`. Ch. 29 read first. Built in code like its neighbours: moving it into a
  `.tscn` belongs to Basic Mode's UI refinement.
- **Unit 3 — the StatusBar badge.** A labelled button, never a bare number (CLAUDE.md, the StatusBar rule): "Holdings:
  N waiting", hidden at no holdings. It refreshes on `BlockAccepted`, and opens the hub.
- **Verification V-H**, once the player holds stock in R4: H1–H4.
- **Close-out:** delete `Scripts/Services/LegacyPlayerFileRelocation.cs` and its call in `WorldGuardService`
  (mini-plan 27 scheduled it here). Then R4's results are written, whether R4 continues afterwards or not (D-29.6).

## 7. Out of scope

Listed in §2: the bidding preference, the bulk toggles, "Wipe everything". Also: any change to the client book or the
rollup to record per-strategy splits. R4 makes them unnecessary by fixing the strategies. Whether the game itself
should record them is a question for Betting Statistics (17.B), roadmap item 4.

## 8. Results

*(Filled in as the runs report: W, then one table per R4 session, then V-H.)*

## 9. Decisions at specification (the developer's)

| # | Decision | Proposed |
|---|---|---|
| **D-29.1** | Branch name | `mini29-holdings-hub-r4` |
| **D-29.2** | R4's roster (§3.2), every client at 1 credit | **approved by the developer, 2026-10-23** ("use the roster"). The credits are this plan's proposal: 1 for all five, so only the strategy differs |
| **D-29.3** | R3's final tally lives in the Step 17 plan's §9.16, beside R3's setup and results, not in a new file | yes |
| **D-29.4** | The reset is the player's "Reset world", which also runs S8's first half. "Wipe everything" stays pending | yes |
| **D-29.5** | The hub is an index plus a badge. Bulk toggles come later; the bidding preference is its own plan after R4 | yes |
| **D-29.6** | R4 runs to at least 2011-06-30. The plan closes when V-H passes and R4 has reached that date. R4's world may continue afterwards as the next plans' playtest world | yes |
| **D-29.7** | While R4 is open, only display-only code changes (§2's rule) | yes |
