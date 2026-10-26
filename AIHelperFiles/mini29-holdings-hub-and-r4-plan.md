# Mini-Plan 29 — The Player Holdings Hub, and R4: a world where every strategy is known

**Series note:** twenty-ninth of the *mini-plan* series. It takes `PRIVATE_ROADMAP.md` ▶ NEXT **item 3**, the Player
Holdings Hub, and opens **R4**, a new test world whose strategies are fixed for the whole run. The developer chose
both on 2026-10-23, after mini-plan 28's close-out: "use the roster", and write this plan "following all your
recommendations".

**Status:** 🔄 **IN PROGRESS 2026-10-23.** **W is done: W1–W3 hold (§8.1)**, and that also ran mini-plan 27's S8
"Reset world" half. **R4 sessions 1–2 are done (§8.2, §8.4): 2009-03-21 → 2011-08-14, h1411, past R4's minimum
date.** P1–P5, P7 and P8 hold. **P6 is refuted as registered** (its measure, not the variance). **P9 is registered**
for `bot_1`'s +2.76σ win rate, to be tested on new bets only. The auction step landed (§8.3): the player holds NST in
ArtForz Cluster and PST in Papa's Pizzeria. **Units 1–3 are built (§6.1).** **Session 3 = V-H is done (§8.5), to
2012-12-24:** H1, H2 and H4 hold, and **P9 holds** (forward z −1.15). H3's `BlockAccepted` refresh is untested,
because DiceGame has no StatusBar. Next: **the developer's D-29.8** (a badge in DiceGame or not), **then V-H3 (§5)**,
then close-out.

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
| **R4-P9** *(registered 2026-10-25, after session 2, and tested **only on bets made after h1411**)* | **`bot_1`'s win rate at 50% is fair.** After session 2 its win count is +2.76σ (§8.4), while the two other 50% clients read −0.71 and +1.39. A persistent bias of the observed size (+0.158 pp) would give **z ≈ +2.1 per 430,000 new bets**, while a fair die gives 0. **Forward z** = (wins − bets/2) ÷ √(bets/4), counted from the h1411 checkpoint's `ClientBetStats` (`bot_1`: **757,407 bets, 379,905 wins**) | the forward z over ≥ 430,000 new bets is **above +1.0**, the midpoint between the two hypotheses. Then the dice's source for `bot_1` is investigated |

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
5. Let it run **80 real minutes** from AUTO (min 78, max 82), then **STOP** and exit the usual way. Tell me the
   session's real length.

**Session lengths, from a measured rate (added 2026-10-23, at the developer's request for a minimum and a maximum).**
9000X caps a game day at **9.6 real seconds** (86,400 ÷ 9,000). R2, the same setup (every client at 1 credit, 9000X),
ran at **9.64–9.76 s per game day** over its whole session: its `session_lifecycle_trace.csv` pairs game time with
real time (2009-12-05 at 41.6 min, 2011-06-12 at 132.0 min, 2012-02-01 at 170.3 min). A session can run slower than
that, never faster, so a stopwatch can undershoot a date but not overshoot it.
- **Session 1 ends between 2010-07-26 and 2010-08-05.** By then the first three companies have been introduced
  (2010-07-18, 07-24, 07-25), so their auctions are open. None can be founded before 2010-08-07, the first
  introduction plus one 20-day window (`AuctionWindowMs`); R2's first founding came on 2010-09-07. **80 minutes** at
  9.6–9.8 s per day lands on 2010-07-24 – 08-03. If it lands early, the auction step waits for a short session whose
  length Claude computes from the date reached.
- **Session 2 starts with the auction step** and runs to **at least 2011-06-30** (R4's minimum, D-29.6): about 335
  game days, **~55 real minutes**. It has no hard maximum, but stopping by ~2011-09 (~70 minutes) keeps V-H's first
  votes close to when the hub is built (§6).

**Later sessions:** the strategies are saved per node in the preferences now (mini-plan 27), so they come back armed.
Check that the five nodes show their strategies, then 9000X and AUTO. **Never change a strategy while R4 is open.** If
one is changed by accident, say so: the plan records the block where it happened.

**The auction step:** when Claude reports from a session's trace that auctions are open (expected ~2010-2011), Claude
gives a short step for the player to donate BTC to two of them. The amounts are chosen then, from the player's
mined BTC.

**Reading:** after every session Claude reads the trace and `godot.log`, and keeps a per-session table in §8.

**Session 3 — V-H, then R4 continues (registered 2026-10-25, before the run).** Predictions, read afterwards from
`godot.log` and `state.json`:
- **H1/H2:** the first `[Holdings] hub:` block lists **exactly 2 rows**: `artforz_cluster nst=2983.24937140 pst=0
  border=gold claim=True button='Claim →'` and `papas_pizzeria nst=0 pst=371.75400422 border=silver claim=True
  button='Claim →'`, matching `CompanyFoundings`.
- **H3:** the first `[Holdings] badge:` line reads `"Holdings: 2 waiting" (held 2 · votes 0 · claims 2)`. After the
  claim at Papa's Pizzeria (step 4) it reads **1 waiting**, and during the run it returns to **2 waiting** when
  Papa's next daily drip passes the fee. That return can only come from the `BlockAccepted` path, since the
  developer is in DiceGame and no scene is entered.
- **H4:** the developer's look (step 3).
- **R4:** P1–P8 continue, and **P9's forward test** needs ≥ 430,000 new bets, about 80 real minutes at 9000X.

*Result (§8.5): H1, H2 and H4 hold. H3's return-to-2 could not be observed, because DiceGame has no StatusBar. That
was a false premise in this protocol, which is why V-H3 below exists.*

**V-H3 — the badge's `BlockAccepted` refresh, observed (registered before the run, the protocol depending on
D-29.8).** Claim Papa's Pizzeria's dividend so the badge reads 1, then let the sim run with a StatusBar on screen.
Papa's daily SC drip makes it claimable again within about one game day (~10 real seconds at 9000X). **Prediction:**
a `[Holdings] badge: "Holdings: 2 waiting"` line is printed **while the run goes**, before the session's last
`[Readout] … Hours -> Seconds` line (the STOP). Nothing else can print it there, since no scene is entered.
**Refuted** if the line appears only after the STOP, or never.

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

### 6.1 As built (2026-10-25), display-only, one commit for the three units

`dotnet build` and `-c ExportRelease` both at 0 warnings. Locale detector **17 / 0 / 0 / 0**, and the 17 hit lines are
identical in content to `HEAD`'s. WipeWorld tripwire 0. No new persisted file, no new `_Process` override.

- **Unit 1 — `UI/CompanyRows/CompanyRowSignals.cs`.** The explorer's colours, `StakeBorderColor`, the founding →
  border mapping (`HoldingBorderColor`), `PendingWorkColor`, the button text, the ⚠ title prefix, `ApplyWorkState`,
  `ApplyButtonBorder`, `BuildTitlePanel` and the dissolved-company row moved here **unchanged**. BlockExplorer
  keeps no copy (grep: 0). **Beyond the plan, same rule:** the gold/silver/black constants also had copies in
  CompanyDetails and AuctioningCompanyDetails, and those now alias the helper. The FBI line's amber/red rule
  (`FbiWarningColor`) is shared with CompanyDetails. The explorer's founded rows now read the founding by node id
  (`NetworkRoot.GetCompanyFoundingByNodeId`), the same record without **one whole-chain ledger walk per founded
  row**: `GetCompanyFounding(address)` resolves the address through `ComputeAuctionLedger` on every call.
- **Read-only additions to `NetworkRoot`:** `DescribeCompany(companyId, nodeId)`, which shares one format with the
  summary form; `GetCompanyFoundingByNodeId` / `GetCompanyFoundings`; and `HasPlayerClaimableDividendsStatic`, the
  static twin the StatusBar needs (the instance method delegates to it, so it is one predicate). Nothing that moves
  money, mines or bids was touched (D-29.7).
- **Unit 2 — `SceneId.PlayerHoldings`**, `Screens/PlayerHoldings/`, MainMenu's **"My Companies"** (after Block
  Explorer). **Deviation from "built in code":** a `.tscn` skeleton holds the fixed layout, the ScTransactions shape
  (Ch. 29 §29.10: `margin_bottom` 50, Pattern A scroll with `size_flags_vertical = 3`, the wheel chain on `PASS`,
  the Back button in a fixed footer outside the scroll). Only the repeated rows are built in code, which is the case
  the roadmap's "UI built in scenes" entry already separates out. Each row is the explorer's row (same helper calls)
  plus what an index needs: the stake held, `Claimable now:` with CompanyDetails' own below-the-fee note, the FBI
  warning, and a bank's lending line. The rows refresh on `BlockAccepted` through one `CallDeferred`. **Navigation:**
  `SceneManager` remembers one step, which a hub → company → hub round trip would break, so the hub keeps a static
  return target. **CompanyDetails is now origin-aware:** "← Back to My Companies" when the hub opened it, the
  explorer otherwise.
- **Unit 3 — the StatusBar badge**, a button on the LEFT of the bar (the right end overflows in some scenes), hidden
  at no holdings, **"Holdings: N waiting"** in the rows' own four-state colour. N comes from
  `CompanyRowSignals.TallyPlayerHoldings`, which the hub header also reads. It refreshes on `BlockAccepted` via one
  `CallDeferred`, plus at `_Ready`: **no `_Process` work**.
- **DEBUG instruments** (`[Conditional("DEBUG")]`, `GD.Print`): `[Holdings] hub: …` plus one `[Holdings]   <company> nst=…
  pst=… border=… vote=… claim=… button='…'` line per row, printed when the rows change (H1, H2); `[Holdings] badge:
  "…"` at each change of the badge (H3).
- **Docs:** `SCENES.md` (inventory, navigation map, the badge's cadence row, the one-step-memory note). CLAUDE.md's two
  scene counts are now written without a number (Convention 15: the count had just gone stale).
- **Godot will create `.uid` files** for the two new scripts the first time the editor imports them. They are
  committed at close-out.
- **Close-out:** delete `Scripts/Services/LegacyPlayerFileRelocation.cs` and its call in `WorldGuardService`
  (mini-plan 27 scheduled it here). Then R4's results are written, whether R4 continues afterwards or not (D-29.6).

## 7. Out of scope

Listed in §2: the bidding preference, the bulk toggles, "Wipe everything". Also: any change to the client book or the
rollup to record per-strategy splits. R4 makes them unnecessary by fixing the strategies. Whether the game itself
should record them is a question for Betting Statistics (17.B), roadmap item 4.

## 8. Results

*(Filled in as the runs report: W, then one table per R4 session, then V-H.)*

### 8.1 W — the reset (2026-10-23): W1, W2 and W3 hold

**Which log is which launch** (each was identified by its content before any figure was read). *Corrected 2026-10-23,
after session 1: Godot names a rotated log by the moment it was **rotated**, i.e. the start of the next launch, not by
its last write as first written here. `godot2026-10-23T19.31.12.log` holds this reset launch, byte for byte (10,988).
The identification below was made by content, so no figure changes. Godot keeps `godot.log` plus four rotated
copies, so each launch drops the oldest: session 1's launch dropped `godot2026-10-23T13.29.44.log` (R3's last
session, whose tally was already in Step 17 §9.16). A `godot.log` worth keeping must be copied under its own name, as
`M1_godot.log` was.*

| File | Launch | What it shows |
|---|---|---|
| `godot2026-10-23T15.18.58.log` | an extra open-and-exit of R3's world, before step 1 | 4,047 blocks loaded, `JournalRollback` dropped 0, `UserSettings` 0 writes. Harmless |
| `godot2026-10-23T15.19.32.log` | **step 1**, the request | `[Reset] requested 'ResetWorld': it runs at the next launch, before anything loads.` |
| `godot.log` | **step 2**, the reset + the bootstrap (15:19:33 → 15:20:27) | below |

| # | Result | Evidence |
|---|---|---|
| **W1** ✅ | `WorldGuardService` performed the request (`[Reset] performing 'ResetWorld', requested 2026-10-23 20:19:27 UTC`) through `WipeWorld` ("the player's Reset world request"). **43 deletion lines:** 17 world files (the version stamp first, then `state.json`, `chain.jsonl`, the checkpoint, the clock and every money service, `hardware_allocation.json`), 8 of the 9 `WorldTracePaths` (`swap_desk_trace.csv` did not exist, and is listed only when present), and the 20 journal segments `bet_history_002310`–`002329`. No `.tmp` / `.corrupt` / `.prerepair` sibling existed. Then `[HistoricalBootstrap] First launch — mined genesis → 2009-03-21 04:17:51. Satoshi 111 blocks, Hal 3 blocks. E4 (10 BTC Satoshi→Hal): on-chain.` | `godot.log` |
| **W2** ✅ | **All 11 hashes unchanged** (stdin `sha256sum`, first 16 hex): `saved_betting_strategies.json` `10fab9ed593e7122` · `settings.json` `749b1e4d92832838` · `rank_calibration_R3.csv` `f8174bc41ec2325e` · `bot_wallet_registry.json` `3803f1eaf2937dfe` · `wordlist_256.json` `8aef71a67a98eb44` · `wallet_state.json` `845128a05226ef8b` · `casino_wallet_state.json` `eea85a50580ff8be` · `satoshi_wallet_state.json` `2747c7160b258259` · `hal_wallet_state.json` `2aba60a0d44d4dc9` · `mike_hearn_wallet_state.json` `120e6e89df092828` · `world_timeline.stamp` `51bb9a965df368ec`. **`logs/` keeps every evidence file:** `M1_godot.log`, `M2_godot.log`, the `_F1/_M1/_M2` block, frame and send traces, `rank_calibration_R1_part1/_part2/R2/R3.csv`, `bet_cost_trace.csv` and the five `.old` files, and every `godot*.log`. The only `logs/` files deleted are the named `WorldTracePaths`, which the code lists as world state (`NetworkRoot.WorldTracePaths`). No `rank_calibration_trace.csv` exists, so R4's trace starts fresh with the current header | hashes and listing, 15:21 |
| **W3** ✅ | **Chain:** `chain.jsonl` holds **115 blocks** (genesis + 111 Satoshi + 3 Hal), `state.json` stamps `ChainHeight 114` and the tip `001e7b67…`, the tip's timestamp is 2009-03-21 04:17:51 local, and `calendar_state.json`'s ticks decode to the same instant. `CompanyFoundings` is empty, and there is no `block_session_checkpoint.json`: the world is pre-genesis. **Balances:** `principal_balance_state.json` **40,000.00000000**, `bankroll_state.json` **0** (written at exit, 15:20:27). **Hardware:** `hardware_allocation.json` rewritten (`7a21a42a44c9d375` → `d9aa05c4e376f250`, "bootstrapped — 5 nodes, 1 individual + 0 casino credit each"), and it reads 1 credit for every node. **`world_format_version.txt` = 12** | the files and `godot.log` |

**Also seen:**
- The boot of a fresh world reached the main menu in **4.09 s**, of which `WorldGuardService` with the wipe took 155 ms.
- The four casino bots' governance stances were **drawn anew** for this world, so they differ from R3's.
- **`settings.json` was not rewritten** (`0 writes` in all three launches). It holds a strategy panel for **`player`
  only** (base 0.02, +100% on loss: R3's last), none for the four bots. So step 3 of §5 must load all five
  strategies, and they are saved from then on. *Corrected after session 1: its requested DEV time scale of **90** is
  already 9000X. `DevTimeScaleSelector` stores the ladder's multiplier on the 100X base and labels it `mult × 100`,
  so step 4 was a check, not a change.*
- **The two existing roster strategies were read from `saved_betting_strategies.json`:** `R1 0.01` (base 0.01, 0% / 0%,
  no stops, chance 50) and `R2 L100 SL5` (base 0.01, +100% on loss, `StopOnLoss` 5 with insist-after-stop ON, so it
  resumes after each cut, chance 50). Both match §3.2.

**S8's "Reset world" half is now run** and is recorded in `mini27-user-settings-plan.md`. "Wipe everything" stays
pending (§2).

### 8.2 R4, session 1 (2026-10-23): 2009-03-21 → 2010-08-02, chain 115 → 859

**Setup, as run** (`godot.log`): three strategies saved (the dropdown went 13 → 16), then exactly five loads, `R1 0.01`
→ `bot_1`, `R2 L100 SL5` → `bot_2`, `R4 L50 0.05` → `bot_3`, `R4 C10 0.1` → `bot_4`, `R4 C90 0.1` → `player`, then
`[AutoBet] Start`. `settings.json` now holds all five panels, each matching §3.2, so later sessions come back armed.
**No strategy changed during the session.**

**Length:** AUTO ran **80:00 real minutes** (`session_lifecycle_trace.csv`: 00:45:38 → 02:05:38 UTC), covering
**499.53 game days: 9.609 s per game day, 99.9% of 9000X**. It landed at **2010-08-02 16:55:51**, inside §5's window.
The last block, h859, is at 14:38:22. The next launch restores to it (Pattern 2), so the player's 82 bets after it
(the rollup's 431,590 against the trace's 431,508) are rolled back then.

| Client | Bets | Wagered (SC) | `m` | Net (SC) | Expected −0.98% × W | z | BTC held | Empty-bankroll restarts |
|---|---|---|---|---|---|---|---|---|
| `bot_1` `R1 0.01` | 431,508 | 4,315.08 | **1.0000** | −33.65 | −42.29 | +1.33 | 4,186.06 | 0 |
| `bot_2` `R2 L100 SL5` | 431,508 | 25,417.58 | **5.890** | −571.56 | | | 5,812.47 | 5 |
| `bot_3` `R4 L50 0.05` | 431,508 | 42,526.89 | **1.971** | −395.57 | | | 4,397.12 | 4 |
| `bot_4` `R4 C10 0.1` | 431,508 | 43,150.80 | **1.0000** | −493.97 | −422.88 | −0.36 | 4,178.37 | 5 |
| `player` `R4 C90 0.1` | 431,508 | 43,150.80 | **1.0000** | −414.63 | −422.88 | +0.38 | 5,350.00 | 4 |

z uses §3.3's σ = base × M × √(N·p·(1−p)): 6.51 (`bot_1`), 195.1 (`bot_4`), 21.7 (the player). Each restart is Auto
Recharge reloading a 100 SC Bankroll after a stop for insufficient balance. The bet counts stay equal, so no restart
cost a bet.

| # | Verdict at session 1 | Measured |
|---|---|---|
| **R4-P1** | ✅ exact | 431,508 bets for all five. × 100 s = 499.43 days from the start, which lands on h859's timestamp within one bet. 0 gaps in `chainLength` |
| **R4-P2** | ✅ | `m` = 1.0000 exactly for `bot_1`, `bot_4` and the player. **§9.16's open question is answered: a flat bettor is never clipped** |
| **R4-P3** | ✅ | `bot_3` m = **1.971**, −1.4%, inside 1.94–2.06. It is below R2's 1.9988 at 1/5 the base, in the direction the prediction named, but not outside the band |
| **R4-P4** | ✅ | `bot_2` m = **5.890**, inside 5–8 (R2: 6.16) |
| **R4-P5** | ✅ | every flat client inside ±3σ: z = +1.33 / −0.36 / +0.38 |
| **R4-P6** | ❌ **REFUTED as registered** | the sd of per-block Δnet is **6.823 (`bot_4`) against 0.926 (the player) = 7.37×**, outside 8.1–9.9. **The prediction's measure was wrong, not the variance:** bets per block swing almost as much as their mean (sd 559 against 581, 743 blocks), and both clients share the same drift of −0.00098 SC per bet, so a common term rides on that swing and compresses the ratio. **Post hoc, not a rescue:** with the drift removed and each block normalised by √(its own bets), the per-bet sd is 0.0332 (player, theory 0.0330) and 0.2929 (`bot_4`, theory 0.2971): **8.82×, −2.0% from 9.0**. Lesson: a per-block statistic inherits the variance of the block's size. *Corrected after session 2 (§8.4): that average of per-block z² weights a 3-bet block like a 1,500-bet one. The **pooled** estimator, √(Σ residual² ÷ Σ bets), is the correct one and gives **8.63×** for session 1, with the player at 0.0327 and `bot_4` at 0.2826* |
| **R4-P7** | ⏳ registered over ~726,000 bets; **interim reading outside its band** | the player's longest losing run is **4** (band 5–8) and its longest winning run **110** (band 100–160), in both the rollup and the retained journal. The journal's last 201,593 bets give losing runs of 16,341 / 1,598 / 170 / 18 at lengths 1 / 2 / 3 / 4, each about a tenth of the previous: the 90% chance's own geometry. About 3.9 runs of 5 were expected over 431,590 bets, so none is a ~2% event, not a fault. Verdict at the run's end |
| **R4-P8** | ✅ so far | all five bet to the end |

**Integrity:** `godot.log` holds 0 exceptions, 0 `UNDECLARED` journal lines, and the UTXO assert is silent after ARMED.
The journal has 0 duplicate ids, and no gap between consecutive player bets exceeds 100 s (the 100-second rule,
across all four restarts).

**Also seen, both pre-existing:**
- `[HistoricalEvents] queued satoshi → mike_hearn 32,51 BTC` prints a **comma decimal**. That is the bare-hole residual
  the locale detector cannot see (CLAUDE.md, Money Handling). R2's `godot.log` has the same 4 lines. It is a log line,
  so fixing it is display-only and allowed under D-29.7, but it is outside this plan's units: recorded for a later fix.
- `[R2] Difficulty is pricing UN-EXECUTABLE power` once, at block 422 (2009-10-14, Hal's fade). R2's log had 4 and
  M1 2. It is a known transient.

**Auctions at the end** (`casino_bot_bid_trace.csv`; `CompanyFoundings` empty): `non_miner_1` Bitcoin Market (leader
136.50 BTC, `bot_3`), `non_miner_2` **Papa's Pizzeria** (25.92, `bot_1`), `non_miner_3` **ArtForz Cluster** (10.34,
`bot_1`, last bid 2010-07-30). The non-miner ↔ company pairing is fixed by appearance order (`CompanyRoster`,
D-ND8.37).

### 8.3 The auction step (given 2026-10-23, at the start of session 2)

Stock goes to the **top 10** donations by amount, and the **top 3** are the NST band (`REFERRAL_AUCTION.md` §1). In R2
the same three companies closed with a 1st place of 1,150–2,021 BTC, a 3rd of 350–1,720 and a 10th of 5–40.
The player holds 5,350 BTC. Two donations, sized to give the hub **both stake colours**:
- **ArtForz Cluster: 2,000 BTC**, aiming at the NST band (gold border). That is R2's 2nd place for this company.
- **Papa's Pizzeria: 200 BTC**, aiming at PST (silver border): R2's 6th–7th place, and 7× its 10th.

That is 2,200 BTC plus two day-median fees, leaving ~3,150. Both are sent before AUTO in the same launch, because an
unmined send is lost if the game restarts first (Pattern 2). The verdict comes from `CompanyFoundings` once each
company is founded.

**Result: both landed exactly as aimed** (`state.json` `CompanyFoundings`):
- **ArtForz Cluster** (founded 2010-08-28, h902): the player is **tier 3**, the last seat of the NST band, with **2,983.25
  NST** (gold). Above it: `bot_2` 2,605.19 BTC (tiers 1/5) and `bot_3` 2,174.67 BTC.
- **Papa's Pizzeria** (founded 2010-09-06, h914): the player is **tier 7** with **371.75 PST** (silver). Its top three
  closed at 1,598 / 1,526 / 1,416 BTC.
- These are the only two founded companies with 5 holders. The other nine have the four bots or fewer.

### 8.4 R4, session 2 (2026-10-25): 2010-08-02 → 2011-08-14, chain 859 → 1,411

**Launches:** `godot2026-10-25T09.26.30.log` holds a short launch, 09:14 → 09:26, that opened DiceGame and exited with no
send and no bet. Its boot rolled the journal back to the h859 checkpoint, dropping the 82 bets after it, as §8.2
said it would. `godot.log` is session 2. **Both sends** showed `Sent!` with a different id each (the developer).
**No strategy was loaded or changed** (0 `StrategyLoad` lines): the five panels came back from `settings.json`.

**Length:** AUTO ran **60:26 real minutes** (14:30:59 → 15:31:25 UTC; developer's stopwatch 1:00:21), covering **377.2
game days at 9.612 s per day (99.9% of 9000X)**, to **2011-08-14 20:24**. The last block, h1411, is at 19:23:25.
**R4 has passed its minimum date, 2011-06-30 (D-29.6).** The trace has 0 gaps in `chainLength` across both sessions
and the restart, and h859 appears exactly once.

| Client | S2 bets | S2 `m` | S2 net (SC) | S2 z | Cum. bets | Cum. `m` | Cum. net (SC) | Cum. z | Win rate (book) | Win z | BTC held | S2 empty-bankroll restarts |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| `bot_1` | 325,899 | 1.0000 | −16.78 | **+2.68** | 757,407 | **1.0000** | −50.43 | **+2.76** | 50.158% | **+2.76** | 313.97 | 0 |
| `bot_2` | 325,899 | 6.438 | −297.52 | | 757,407 | **6.126** | −869.08 | | 49.959% | −0.71 | 444.11 | 5 |
| `bot_3` | 325,899 | 1.985 | −395.99 | | 757,407 | **1.977** | −791.56 | | 50.080% | +1.39 | 392.41 | 3 |
| `bot_4` | 325,899 | 1.0000 | −490.59 | −1.01 | 757,407 | **1.0000** | −984.56 | −0.94 | 9.968% | −0.94 | 657.39 | 4 |
| `player` | 325,899 | 1.0000 | −294.85 | +1.34 | 757,407 | **1.0000** | −709.48 | +1.19 | 90.041% | +1.18 | 3,500.00 | 3 |

The bots' BTC fell from ~4,200–5,800 to ~310–660: they spent it in the auctions. The player's went 5,350 → 3,500:
2,200 donated, plus what it mined. Satoshi retired on **2011-04-26 with 11,007.50 BTC** (target 11,000), and the **FBI
activated on 2011-06-15** (the UTXO assert now covers 74 nodes, not 73).

| # | Verdict after session 2 | Measured |
|---|---|---|
| **R4-P1** | ✅ exact | 757,407 bets for all five. × 100 s = 876.63 days, which lands on h1411's timestamp |
| **R4-P2** | ✅ | `m` = 1.0000 exactly, cumulative and per session, for all three flat clients |
| **R4-P3** | ✅ | `bot_3` cumulative m = **1.977** (−1.2%); session 2 alone 1.985 |
| **R4-P4** | ✅ | `bot_2` cumulative m = **6.126** (R2: 6.16); session 2 alone 6.438 |
| **R4-P5** | ✅, **one client close to its bound** | cumulative z: player +1.19, `bot_4` −0.94, **`bot_1` +2.76**. The book's own counts (379,921 wins in 757,443 bets) reproduce its net to the satoshi, so the excess is in the rolls, not the arithmetic. The two other 50% clients don't share it (−0.71, +1.39). Across five fair clients, a largest \|z\| this high appears ~3% of the time. **Watched by R4-P9**, which only new data can decide |
| **R4-P6** | ❌ **stays REFUTED as registered** | raw per-block ratio **7.38** in session 2, **7.37** cumulative. **Pooled per-bet ratio (post hoc): 8.82 in session 2, 8.72 cumulative**. Every component is within ±5% of theory: player 0.0344 / 0.0335 against 0.0330, `bot_4` 0.3035 / 0.2918 against 0.2971 |
| **R4-P7** | ✅ **holds at its registered horizon** | the longest losing run is **5**, and the longest winning run **117**. Both runs of 5 came before bet #726,000: #637,198 on 2011-03-28 and #681,217 on 2011-05-18. The 117 run started 2011-04-08. The retained journal (207,449 bets, 2010-12-17 → 2011-08-15) gives losing runs of 16,644 / 1,660 / 164 / 20 / 2 at lengths 1–5, against an expected 16,803 / 1,680 / 168 / 17 / 1.7 (the last for ≥ 5) |
| **R4-P8** | ✅ | all five bet to the end |

**Integrity:** 0 exceptions, 0 `UNDECLARED` journal lines, the UTXO assert is silent, 0 duplicate journal ids, and no
gap between player bets exceeds 100 s. One `[R2] Difficulty is pricing UN-EXECUTABLE power` line appears, at block
1097 (2011-02), the known transient.

**What the hub will have to show (read now, for V-H):** two holdings and **no open vote at either**. The player's
votes there have been **auto-cast** (`WasAutoCast: true`, `PlayerPauseOnVotes` false). Both hold a **claimable
dividend**: ArtForz 203.20 BTC + 10.14 SC, Papa's 1.77 BTC + 9.28 SC, with 0 claims made. FBI score 0 at both.
**H3's badge should therefore read "Holdings: 2 waiting"**, until a claim or a new vote changes it.

**Also noted, outside this plan** (the developer, 2026-10-25): after a Send, the BTC Wallet's send panel does not
refresh. Recorded as a UI refinement item in `PRIVATE_ROADMAP.md` ("Refresh after an action").

### 8.5 R4, session 3 = V-H (2026-10-25): 2011-08-14 → 2012-12-24, chain 1,411 → 2,142

**Launch:** one. `godot2026-10-25T17.44.52.log` is session 2's log, rotated at session 3's start, so `godot.log` is
session 3. The developer: "everything looked in order with the new UI elements" (H4). **AUTO ran 80:18 real minutes**
(22:48:08 → 00:08:26 UTC; developer's stopwatch 80:16), covering 497.8 game days at **9.68 s per day, 99.2% of
9000X**. No strategy was loaded or changed. 0 exceptions, 0 `UNDECLARED`, the UTXO assert is silent, and 0 gaps in
`chainLength` across all three sessions (h1411 appears once). The claim at Papa's Pizzeria is recorded in its
`PlayerClaimHistory`: **1.76586494 BTC + 9.27799542 SC**, at h1411.

**V-H:**

| # | Verdict | Measured (`godot.log`, `state.json`) |
|---|---|---|
| **H1** | ✅ | the first `[Holdings] hub:` block lists **exactly the 2 predicted rows**: `artforz_cluster nst=2983.24937140 border=gold claim=True button='Claim →'` and `papas_pizzeria pst=371.75400422 border=silver claim=True button='Claim →'`, the two foundings that hold the player, and no other |
| **H2** | ✅ | each row's border, work state and button come from the same `CompanyRowSignals` calls the explorer makes (§6.1, grep: two callers each). After the claim, the Papa's row reads `claim=False button='Details →'` |
| **H3** | ⚠️ **two of three parts hold; the third was untestable as registered, by my error** | the badge read **"Holdings: 2 waiting"** at the Main Menu, and **"1 waiting"** right after the claim, as predicted. **The return to "2 waiting" did not come from `BlockAccepted`:** it was printed by the Main Menu's new StatusBar at the session's end (its `_Ready`). **The registered premise was false: DiceGame has no StatusBar** (24 scenes build one, and DiceGame is not among them). So during the 80-minute run no badge existed to refresh. The `BlockAccepted` path is **untested, not refuted**. It needs a run with the sim going while a StatusBar scene is open (§5, V-H3) |
| **H4** | ✅ | the developer's look: in order |

**R4, session 3 and cumulative** (`rank_calibration_trace.csv` rows 1296 → 2028; the checkpoint's `ClientBetStats`):

| Client | S3 bets | S3 `m` | S3 net (SC) | S3 z | Cum. bets | Cum. `m` | Cum. net (SC) | Cum. z | Cum. win z (book) | BTC held |
|---|---|---|---|---|---|---|---|---|---|---|
| `bot_1` | 429,538 | 1.0000 | −49.58 | −1.15 | 1,186,945 | **1.0000** | −100.01 | +1.51 | +1.51 | 561.27 |
| `bot_2` | 429,538 | 6.208 | −589.51 | | 1,186,945 | **6.156** | −1,458.58 | | −0.94 | 958.42 |
| `bot_3` | 429,538 | 1.974 | −388.54 | | 1,186,945 | **1.976** | −1,180.10 | | +0.45 | 989.30 |
| `bot_4` | 429,538 | 1.0000 | −215.78 | +1.05 | 1,186,945 | **1.0000** | −1,200.34 | −0.11 | −0.11 | 1,196.61 |
| `player` | 429,538 | 1.0000 | −402.35 | +0.90 | 1,186,945 | **1.0000** | −1,111.84 | +1.49 | +1.49 | 3,801.77 |

| # | Verdict after session 3 | Measured |
|---|---|---|
| **R4-P1** | ✅ exact | 1,186,945 bets for all five; × 100 s lands on the last trace row (2012-12-23 23:00:23) |
| **R4-P2** | ✅ | `m` = 1.0000 for the three flat clients, three sessions running |
| **R4-P3 / P4** | ✅ | `bot_3` 1.976 (band 1.94–2.06), `bot_2` 6.156 (band 5–8) |
| **R4-P5** | ✅ | cumulative z +1.51 / −0.11 / +1.49, all well inside ±3σ |
| **R4-P6** | ❌ stays refuted as registered | raw per-block ratio 6.93 (S3) and 7.21 cumulative. Pooled per-bet ratio 8.34 (S3) and 8.58 cumulative. `bot_4`'s component holds at 0.2926 against 0.2971, but **the player's reads 0.0351, +6% above its 0.0330** this session, outside the ±5% §8.4 reported. That is post hoc and noted, not chased |
| **R4-P7** | ✅ (closed at its horizon, §8.4) | still losing run 5 and winning run 117 at the checkpoint |
| **R4-P8** | ✅ | all five bet to the end |
| **R4-P9** | ✅ **holds: the excess was luck** | `bot_1`'s forward window (from the h1411 baseline to the h2142 checkpoint) holds **429,538 bets with 214,391 wins (49.912%): forward z −1.15**, against the refutation line of +1.0. The window is 462 bets short of the registered 430,000, but no outcome of those bets could change the verdict: even if all 462 were wins, z rises by at most 462 ÷ √(430,000/4) = +1.41, to +0.26. The cumulative z fell from +2.76 to **+1.51**, as a fair die predicts. The other forward z: `bot_2` −0.61, `bot_3` −1.11, `bot_4` +1.05 |

**Also seen:** one `[R2] Difficulty is pricing UN-EXECUTABLE power` line, at block 1920, the known transient. The
world holds 22 foundings and 0 dissolved companies. The player holds no stock beyond the two holdings above.

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
| **D-29.8** *(opened 2026-10-25, after session 3)* | **Does DiceGame show the holdings badge?** It is the scene where a run happens, and it has no StatusBar, so a player betting there never sees "Holdings: N waiting", which is the hub's reason to exist (§1). | **Proposed: yes**, as a unit 4: the same badge control, built in code beside DiceGame's existing controls (Ch. 29: an absolutely positioned layout), display-only. V-H3 then reads its `BlockAccepted` refresh while the run goes. *Alternative:* no badge in DiceGame; V-H3 is then run with the hub open over a running sim |
