# ScFinances — Player SC Finances Hub + Private Bank Account System (Step 12) — Design Plan

**Status**: 🟢 READY TO IMPLEMENT — v4 (2026-07-03). Round-1, round-2, **and** the round-3 model simplification (§0.5) all locked. No code has been written yet; implementation begins after the developer reviews, branches, and commits (git is developer-controlled).

**Scope**: Close the player↔casino symmetry loop opened by Step 11 / CG.3.D — but on the **player** side the relationship is *ownership, not credit*. The player keeps their canonical **`40,000 SC` start exactly where it is today**, and it lives entirely in **Main Balance** (`40,000` Main / `0` Bankroll) until the first DiceGame entry. On that first entry, the **automatic initial Bankroll recharge** splits **the current auto-recharge dose** off Main — the default `100` → `39,900` Main / `100` Bankroll, or, if the player raised the dose beforehand (e.g. to `500`), that amount → `39,500` Main / `500` Bankroll. This automatic split is skipped only if the player performed a **manual initial Bankroll recharge** first, which satisfies and therefore **deactivates** the automatic initial recharge (no double-fund). Either way the `40,000` total is the player's own money. On top of this, the player gains a **Private Bank Account** — an **initially-empty, optional savings/reserve** the player can move SC *to* (withdraw Main→Bank) and *back* (deposit Bank→Main), manually or automatically. **The bank is not an early-game surface**: it starts at `0`, all its automation defaults OFF, and a new player can ignore it for the first in-game months/years while learning Main↔Bankroll (the existing loop). The transfer machinery (all four flows) is built and functional now, but dormant by default. One centralized player-facing scene — **`ScFinances`** — replaces the DEV-era `DepositPopup` in DiceGame as the canonical home for the player's SC flows (the "SC Wallet scene (planned)" that `Documentation/GLOSSARY.md` has pointed at since Step 11).

> **⚠️ Round-3 pivot (v4) — READ FIRST.** v2/v3 used an **"extra-lazy" model** that seeded the whole `40,000` **at the bank** (Main `0`) and streamed it into the casino via a cascade at DiceGame entry. **That is abandoned.** It was unnecessary ceremony whose end state was identical to today's start, it created a fresh-vs-legacy seed inconsistency, and seeding the bank at `40,000` was effectively the very migration D-SF2.8 forbade. **v4: the bank starts at `0` for everyone (fresh and legacy alike, no migration), Main is funded as today, and the bank is a pure optional reserve.** Wherever this document still says "extra-lazy," "cascade at entry," "Main starts at 0," "seed bank at 40,000," or "AutoDeposit default ON," the v4 rule in §0.5 overrides it; the older text is retained only where it was corrected inline. **What is abandoned is only the *automatic seeding* of the `40,000` at the bank** — the extra-lazy *streaming* capability survives as an **opt-in** (Auto-Deposit ON with a banked reserve at a valid amount, D-SF3.2/3.3).

**Created**: 2026-07-03 (v1: bank-loan model). **Restructured**: 2026-07-03 (v2: Private Bank Account model; v3: round-2 decisions locked; v4: round-3 model simplification — bank starts empty, optional reserve, no extra-lazy **as the default**). Note: extra-lazy funding is **not gone — it becomes opt-in.** Once the player has banked a reserve and turns **Auto-Deposit ON with a valid amount** (a positive `AutoDepositAmount` that the Private Bank Account can cover — the UI validates it against the live bank balance **before** applying the setting), the Bank→Main auto-refill reproduces extra-lazy-style funding (D-SF3.3). What v4 drops is only the *automatic seeding* of the `40,000` at the bank, not the streaming capability.

**Companion docs**: `AIHelperFiles/player-and-casino-bankroll-programmer-plan.md` (CG.0–CG.3 — the casino-side machinery this step mirrors) · `AIHelperFiles/step11-casino-sc-gambling-finances-plan.md` (casino SC architecture, client ledger) · `Documentation/GLOSSARY.md` (SC Deposit vs Recharge distinctions) · `Documentation/ProjectDesignManual.md` §24.8–24.10 (block-is-the-only-commit + game-time rules), §31 (casino SC lifecycle).

---

## 0. Decisions locked (round 1 — 2026-07-03)

| # | Question (v1) | Decision |
|---|---|---|
| **D-SF.1** | Is the player's starting `40,000` a bank loan? | **No — it's the player's own money, and it stays in Main Balance exactly as today** (`39,900` Main / `100` Bankroll). ⚠️ **v4 override (§0.5):** the v2/v3 idea of holding the `40,000` *at the bank* (Main `0`) is dropped. Instead the player gains a **Private Bank Account that starts EMPTY (`0`)** — an optional savings/reserve. The v1 loan machinery is re-semanticized as ownership, not credit: *deposits* move SC **Bank→Main** (limit = bank balance), *withdrawals* move SC **Main→Bank** (limit = Main). Both auto and manual, both toggleable, all functional now — but the bank being empty by default with all its automation OFF, nothing moves until the player chooses to bank SC. Early play is pure Main↔Bankroll, unchanged. Future (documented only, §7): reasons to prefer keeping SC at the bank — e.g. fixed-term frozen deposits earning interest, defined in a pending **`ScBank`** scene design. |
| **D-SF.2** | Infinite credit line / debt cap? | **Moot.** There is no credit and no debt. The natural limit of every bank→casino deposit is `BankAccountBalance`; of every casino→bank withdrawal, `MainBalance`. UI must enforce and *communicate* both limits ("Available: X SC"). |
| **D-SF.3** | Defaults for the new knobs? | ⚠️ **v4 defaults (§0.5), everything opt-in:** `BankAccountBalance = 0` (starts empty), **`AutoDepositEnabled = false`** (bank is a safe reserve; player opts in — D-SF3.2), `AutoDepositAmount = 1,000` (a modest reserve-refill chunk once enabled; configurable — no longer `40,000`, which only made sense under the dead extra-lazy model), `AutoWithdrawEnabled = false`, `AutoWithdrawThreshold = 1,000`, `AutoWithdrawAmount = 100` (one bankroll-dose per installment). All revert pre-genesis, stick at a block. |
| **D-SF.4** | Auto-funding on the manual-bet path? | **Reframed: it's the player's own money, so it's the player's own responsibility — via toggles.** Every automated flow at every level must be individually toggleable ON/OFF: bank→Main auto-deposit (`AutoDepositEnabled`), Main→bank auto-withdrawal (`AutoWithdrawEnabled`), **and the existing Bankroll auto-recharge itself** (new `AutoRechargeEnabled` on `BankrollProgramService` + toggle in `BankrollProgrammer` — it is currently always-on with no off switch). The same toggle principle applies to the casino's bank interactions (its loans, and its future P6 repayments) — casino toggles are **deferred** (§7) because disabling the casino's auto-loan needs an insolvency policy first. |
| **D-SF.5** | Ledger kinds for the new flows? | **Casino keeps distinct kinds for its loans (already true — its loans never touch the client ledger). The player's bank flows use the EXISTING `deposit`/`withdrawal` kinds** — they genuinely are SC deposits (Bank→Main) / withdrawals (Main→Bank), just "special" in that the counterparty is the bank. **Manual vs automatic must be distinguishable in the records** — see §3.7 (a new `Method` field, `"manual"`\|`"auto"`, not new kinds; D-SF2.3). |
| **D-SF.6** | DepositPopup fate? | **Retire it** (v1 recommendation A): DiceGame's Deposit button repoints to `ScFinances`; popup node + handlers removed; `UI/DepositPopup/` deleted once unreferenced. |
| **D-SF.7** | `PrincipalBalanceService.BalanceChanged` event? | **Yes — add it** (invoke in `Deposit`/`TryWithdraw`/`SetBalance`). Used by ScFinances labels, the auto-withdraw hook, StatusBar, and future scenes. Additionally the user asked to elaborate future-version uses of coherent auto deposits/withdrawals — done in §7. |
| **D-SF.8** | Player-facing or DEV? | **Player-facing** MainMenu placement, prototype-level polish (no styling gate). |

---

## 0.5 Decisions locked (round 3 — model simplification, 2026-07-03)

The v2/v3 "extra-lazy" model (seed the whole `40,000` at the bank, stream it into the casino via a cascade at DiceGame entry) is **abandoned**. It was found to contradict itself and add needless machinery — details in D-SF3.1. These override any older text.

| # | Decision |
|---|---|
| **D-SF3.1** | **Bank starts EMPTY (`0`); Main is funded exactly as today. No extra-lazy, no relocation of the `40,000`.** The contradiction that forced this: v3 seeded a fresh world's bank at `40,000`/Main `0` but D-SF2.8 seeded legacy worlds' bank at `0`/Main unchanged — two seeds, and the fresh one *relocates* the canonical start out of Main into the bank, which is the very migration D-SF2.8 forbids. The relocated end state (Bank `0` / Main `39,900` / Bankroll `100` after the cascade) is **identical to today's start anyway**, so the cascade was pure ceremony. v4: **one uniform seed — bank `0` for fresh and legacy alike, no migration ever**; Main holds the full `40,000` at world start (Bankroll `0`); the existing `EnsureInitialBankrollFunded` (Main→Bankroll split at DiceGame entry using **the current auto-recharge dose** — default `100` → `39,900/100`, or the player's chosen dose, e.g. `500` → `39,500/500`) stays **unchanged**, and a **manual** initial Bankroll recharge done first deactivates that automatic split (no double-fund). |
| **D-SF3.2** | **The Private Bank Account is an optional, initially-empty reserve — all four transfer flows built and functional, all automation defaulting OFF.** Manual/auto **withdraw** (Main→Bank) and manual/auto **deposit** (Bank→Main) all work, but `AutoWithdrawEnabled = false` and **`AutoDepositEnabled = false`** by default, and the bank holds `0`, so nothing moves until the player opts in. Auto-Deposit's default is **OFF** deliberately so **banked SC is a *safe reserve*** — money parked at the bank is protected from the casino until the player manually (or by opting into auto) brings it back. **UI requirement:** ScFinances must (a) clearly explain the ON vs OFF consequence of each toggle (esp. Auto-Deposit: ON = seamless auto-refill of Main from reserves, but reserves are gamblable; OFF = reserves stay safe, player retrieves manually) so it is purely the player's informed choice; and (b) **validate `AutoDepositAmount` against the live Private Bank Account balance *before* applying the setting** — the amount must be positive and coverable by the bank. With Auto-Deposit ON and a valid amount, this is the **opt-in extra-lazy** path (D-SF3.3): banked SC streams back into Main on demand. Guard/reject an amount ≤ 0 or an enable attempt while the bank is empty (nothing to stream). |
| **D-SF3.3** | **The `bank→Main` auto-deposit is a fallback, not the primary funding path.** Normal early play funds Main→Bankroll exactly as today; `TryAutoDeposit` only fires when Main can't cover a recharge **and** `AutoDepositEnabled` is ON **and** the player has previously banked SC (bank > 0) — i.e. essentially never in early game. The session-start invariant (D-SF2.6) still holds but is satisfied by Main→Bankroll; the bank branch is a rarely-hit fallback. **When the player opts in** (banks a reserve + Auto-Deposit ON at a valid amount, D-SF3.2), this fallback *is* the **opt-in extra-lazy** funding — the streaming capability lives on, just no longer as the default seeding (retired-as-default: §7.3). |
| **D-SF3.4** | **Ledger `initial` reverts to today's meaning — the starting stake, recorded as today.** With Main funded at start there is no "first bank→Main deposit" to badge; the `initial` entry is the player's starting `40,000` equity, registered as it is today (boot / first recharge). The v3 "event-driven `initial` = first bank deposit" rework (SF.1.5) and the D-SF2.10 manual-first-funding nuance are **dropped as moot**. **D-SF2.4's ledger lifecycle fix (checkpoint snapshot + pre-genesis clear) STAYS** — it is valuable regardless of the model. Bank→Main deposits the player later makes are ordinary `deposit`/`method` entries, never `initial`. |
| **D-SF3.5** | **"Extra-lazy / mirror-the-casino" framing dropped for the player.** The casino borrows on demand (genuinely starts empty); the player *owns* their money. The symmetry is now: casino = credit relationship (starts `0`, draws loans); player = savings relationship (Main funded, optional empty bank). Purge "extra-lazy" as the player-side headline; keep it only where accurately describing the casino. |

---

## 1. The model — three-tier account topology

```
┌─────────────────────────┐   deposit  Bank→Main (opt) ┌───────────────────────────── CASINO ─┐
│  PRIVATE BANK ACCOUNT   │ ──────────────────────────► │  MAIN BALANCE      ⇄      BANKROLL   │──► bets
│  (optional SC reserve)  │ ◄────────────────────────── │  (casino SC acct)   recharge as today  │
│  start: 0 SC            │  withdraw  Main→Bank (opt) │  start: 40,000→39,900   0→100 @entry  │
└─────────────────────────┘                             └───────────────────────────────────────┘
        managed in ScFinances                                   managed in BankrollProgrammer
```

- **Private Bank Account** (`BankAccountBalance`) — an **optional SC reserve** the player can move money to and from, *outside* the casino. **Starts EMPTY (`0`)** (D-SF3.1). New scene `ScFinances` manages it.
- **Casino SC Account** = Main Balance + Bankroll — the player's money *inside* the casino, **funded exactly as today** (`39,900` Main / `100` Bankroll after the existing DiceGame-entry recharge). Existing services, unchanged mechanics.
- The **bank** is the same conceptual entity that lends to the casino, but its two client relationships differ: it *lends* to the casino (credit/debt, casino starts empty and borrows) and merely *holds savings* for the player (the player owns their money; the bank account is an empty reserve they opt into). One bank, two relationship types — and later possibly more clients (bots).

**Funding, v4 (D-SF3.1/3.3) — nothing about early funding changes from today:**

1. At world start Main holds the full `40,000` (Bankroll `0`, bank `0`). On **DiceGame entry**, the existing `EnsureInitialBankrollFunded` splits **the current auto-recharge dose** off Main → e.g. default `100` gives Main `39,900` / Bankroll `100`, or a player-raised dose of `500` gives Main `39,500` / Bankroll `500` — **unchanged mechanics**. A **manual** initial Bankroll recharge done beforehand satisfies this and **deactivates** the automatic initial split (no double-fund).
2. During a session, **Bankroll auto-recharge** pulls a dose from Main exactly as today — *if `AutoRechargeEnabled`* (new toggle, default ON; the only new thing is the off-switch).
3. **Bank→Main auto-deposit is a pure fallback (D-SF3.3):** it fires only when Main can't cover a recharge **and** `AutoDepositEnabled` is ON **and** the bank actually holds SC the player previously parked there. Since the bank starts empty and Auto-Deposit defaults OFF, this is essentially never hit in early game — it exists for the player who deliberately banks a reserve and opts into auto-retrieval.

> **Invariant — session start requires a fundable bankroll (D-SF2.6).** No user (bot **or** human) may begin an auto or manual bet session while Bankroll is below the required bet. With `AutoRechargeEnabled` ON, a running session refills the Bankroll from Main so the flow continues. With `AutoRechargeEnabled` OFF, betting stops and waits for a **manual** Bankroll recharge. In v4 this invariant is satisfied by Main→Bankroll as it is today; the bank→Main auto-deposit only matters as a fallback once the player has banked SC (D-SF3.3). Bots follow the identical rule against `NodeFinancialState.PrincipalBalance` (no bank account — SF.1.7).

**The optional reserve in action (opt-in only):** A player on a winning streak withdraws surplus Main→Bank (manually, or via Auto-Withdraw once enabled) to **lock it away safe from the casino**. Later, if they choose, they deposit Bank→Main (manually, or via Auto-Deposit once enabled) to bring the reserve back into play. With **Auto-Deposit OFF (default)** the reserve is a *safe vault*: running Main+Bankroll to `0` stops betting and prompts a manual retrieval, but it is **not** game-over while the bank holds SC (game over = all three at `0`, D-SF2.1). With **Auto-Deposit ON** the reserve auto-refills Main when it runs low — convenient, but the banked SC is then gamblable. ScFinances must explain this trade-off in the UI (D-SF3.2).

**Updated relationship table:**

| Capability | Casino (exists) | Player after Step 12 (v4) |
|---|---|---|
| Relationship | Bank **loans** (credit, debt) | **Owns** money; bank account = optional savings/reserve (no debt) |
| Start state | All-zero; loan drawn on demand | Main funded as today (`39,900`/`100`); **bank account `0`** |
| Inflow (Bank→Main) | — (casino has no such account) | `TryAutoDeposit` **fallback** (default OFF) + manual deposit; only meaningful once the player has banked SC |
| Inflow trigger | Bankroll ≤ 0 and Main < dose (loan) | Main < dose during a recharge **and** bank > 0 **and** Auto-Deposit ON (rare) |
| Outflow (Main→Bank) | ❌ (repayments = open P6) | Auto/manual **withdrawals** to bank — becomes the P6 template |
| Histories | `LoanRecord` + `RechargeRecord` | `BankTransferRecord` (both directions) + existing `TransferRecord` |
| P/L metric | `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` | `NetWorthSc = Bank + Main + Bankroll`; `OverallPl = NetWorthSc − 40,000` |
| Hub scene | `CasinoGamblingFinances` (DEV) | `ScFinances` (player-facing) |
| Transactions scene | `ClientsTransactions` (casino's view of clients) | `ScTransactions` (player's own bank flows) |

---

## 2. Naming proposals (v2)

| Thing | Proposed name | Alternatives | Rationale |
|---|---|---|---|
| Hub scene | **`ScFinances`** (`Screens/ScFinances/ScFinances.tscn/.cs`) | — (user-chosen) | Player-facing mirror of `CasinoGamblingFinances`. |
| Transactions sub-scene | **`ScTransactions`** (`Screens/ScFinances/ScTransactions.tscn/.cs`) | `BankTransactions` | Mirror of `ClientsTransactions`, hub-nested like the casino's. |
| Future bank scene | **`ScBank`** (documented only, §7) | `BankOffice` | User-named; home of term-deposit/interest dynamics later. |
| New autoload service | **`PlayerBankAccountService`** (`Scripts/Services/PlayerBankAccountService.cs`, autoload #13) | `PrivateBankAccountService`, `ScBankAccountService` | Owns the player's bank-account balance + transfer automation + histories. "Player" prefix keeps room for future per-bot accounts on the same pattern. |
| Balance property | **`BankAccountBalance`** | `PrivateEquity` | User's own term ("private bank sc account = BankAccountBalance"). UI label: **"Private Bank Account"**. |
| Account terms (GLOSSARY) | **Private Bank Account** / **Casino SC Account** (= Main Balance + Bankroll) | — | User's framing; makes every limit message unambiguous. |
| Transfer record | **`BankTransferRecord`** — `Amount`, `Direction` (`"bank_to_main"` \| `"main_to_bank"`), `Method` (`"auto"` \| `"manual"`), `GameDateLocal` | two lists (`DepositRecord`/`WithdrawalRecord`) | One list, two directions — mirrors `BankrollProgramService.TransferRecord`'s proven shape and makes the ScTransactions merge trivial. Direction strings styled after `"balance_to_bankroll"`. |
| Manual inflow API | **`TriggerManualDeposit(decimal amount)`** | `DepositToMainBalance()` | "Deposit" per GLOSSARY = SC entering Main Balance from outside. UI button: **"Deposit → Main Balance"**. |
| Manual outflow API | **`TriggerManualWithdrawal(decimal amount)`** | `WithdrawToBankAccount()` | UI button: **"Withdraw → Private Bank Account"**. |
| Auto inflow API | **`TryAutoDeposit(decimal neededInMain)`** | `TryAutoFundMainBalance()` | `Try` — fails when disabled or bank empty. Parameter = the shortfall, so the loop can size the final partial draw. |
| Auto outflow API | **`TryAutoWithdraw()`** | `TryAutoSweep()` | Threshold/surplus model, §3.5. |
| Inflow settings | **`AutoDepositEnabled`** (default **OFF** — D-SF3.2), **`AutoDepositAmount`** (default `1,000` — a modest reserve-refill chunk; configurable) | `AutoFundingEnabled` | v4: bank starts empty and this is a safe-reserve fallback; OFF by default, small chunk (not the dead `40,000` extra-lazy chunk). |
| Outflow settings | **`AutoWithdrawEnabled`** (default OFF), **`AutoWithdrawThreshold`** (default `1,000`), **`AutoWithdrawAmount`** (default `100`) | `AutoRepay*` (v1 names, dead) | Threshold = Main Balance floor that must remain; Amount = installment per event. |
| Bankroll recharge toggle | **`AutoRechargeEnabled`** on `BankrollProgramService` (default ON) | — | D-SF.4: the missing off-switch for the existing dose recharge; UI in `BankrollProgrammer`. |
| Player metrics | **`NetWorthSc`** (`= BankAccountBalance + Main + Bankroll`), **`OverallPl`** (`= NetWorthSc − 40,000`, the **canonical total start** — NOT `InitialBankAccountBalance`, which is now `0`) — **computed in the ScFinances controller from the three balance sources, not service properties** (D-SF2.7 keeps `PlayerBankAccountService` pure) | `TotalNetWorth` | Replaces v1's debt-based `NetPositionVsBank`. `OverallPl` is the honest all-accounts P/L vs the canonical `40,000` start (fresh world: `0 + 39,900 + 100 − 40,000 = 0`). |
| Ledger method field | **`LedgerEntry.Method`** (`"manual"` \| `"auto"`, default `"manual"` for legacy entries) | new kinds `auto_deposit`/`auto_withdrawal` | §3.7 / D-SF2.3 — keeps every existing kind-based filter working. |
| Internal-movement ledger kind | **`"bankroll_withdrawal"`** for Bankroll→Main (currently mis-filed as `"withdrawal"`) | `"internal_withdrawal"` | §3.7 taxonomy cleanup — frees `"withdrawal"` for its true meaning (Main→bank, SC leaving the casino). |

---

## 3. Target architecture

### 3.1 `PlayerBankAccountService` (new autoload #13)

Owns the Private Bank Account and the bank↔casino transfer automation. Mutates `PrincipalBalanceService` for the Main Balance side; never touches the Bankroll (that remains `BankrollProgramService`'s job).

State (all `Money.Normalize`d, persisted to `user://player_bank_account_state.json`):

```csharp
public const decimal InitialBankAccountBalance = 0.00000000m;	// v4 (D-SF3.1): bank starts EMPTY — the 40,000 stays in Main, as today
public const decimal DefaultAutoDepositAmount  = 1_000.00000000m;	// v4 (D-SF.3): modest reserve-refill chunk, only used once the player banks SC + enables auto-deposit
public const decimal DefaultAutoWithdrawThreshold = 1_000.00000000m;
public const decimal DefaultAutoWithdrawAmount    =   100.00000000m;

public decimal BankAccountBalance   { get; private set; }	// starts 0; freely fillable/emptiable
public bool    AutoDepositEnabled   { get; private set; }	// default FALSE (D-SF3.2 — bank is a safe reserve, opt-in)
public decimal AutoDepositAmount    { get; private set; }	// chunk per auto-deposit draw (fallback path)
public bool    AutoWithdrawEnabled  { get; private set; }	// default false
public decimal AutoWithdrawThreshold{ get; private set; }	// Main Balance floor
public decimal AutoWithdrawAmount   { get; private set; }	// installment per auto-withdraw event

public IReadOnlyList<BankTransferRecord> BankTransferHistory { get; }	// capped at 500 (mirror MaxRechargeHistory)

public decimal TotalDepositedToCasino   { get; }	// running sums for ScTransactions header totals
public decimal TotalWithdrawnFromCasino { get; }

public event Action BankStateChanged;
```

Behavior rules:

- **Every displayed/persisted timestamp is game time** (`_calendarTime?.CurrentLocalDateTime`), per CLAUDE.md Important Pattern 2; wall-clock only in the JSON `UpdatedAtUtc`.
- `TriggerManualDeposit(amount)`: `effective = min(amount, BankAccountBalance)`; if `≤ 0` fail with the available balance in the message. `BankAccountBalance −= effective`, `PrincipalBalanceService.Deposit(effective)`, record (`bank_to_main`/`manual`), ledger entry (§3.7), persist, event.
- `TriggerManualWithdrawal(amount)`: `effective = min(amount, MainBalance)`; `PrincipalBalanceService.TryWithdraw(effective)`, `BankAccountBalance += effective`, record (`main_to_bank`/`manual`), ledger entry, persist, event.
- `TryAutoDeposit(neededInMain)`: no-op unless `AutoDepositEnabled && BankAccountBalance > 0`. Draws `min(AutoDepositAmount, BankAccountBalance)` per iteration into Main, looping (safety-capped, mirror `MaxAutoRechargeIterations`) until Main covers `neededInMain` or the bank is empty — **the final draw may be a partial chunk** (the account "can be freely emptied", D-SF.2). Each draw is one `bank_to_main`/`auto` record. Returns whether Main now covers the need.
- `TryAutoWithdraw()`: no-op unless `AutoWithdrawEnabled`. `effectiveFloor = max(AutoWithdrawThreshold, BankrollProgramService.AutoRechargeAmount)` — the dose floor is the **anti-ping-pong guard**: an auto-deposit fires precisely when Main can't cover a dose, so auto-withdraw must never drain Main back below one dose (deposit→withdraw oscillation). Moves `min(AutoWithdrawAmount, MainBalance − effectiveFloor)` if positive; one installment per trigger event (next event pays the next installment — every movement auditable, mirroring CG.1.8.5's one-dose principle).
- Setters: `SetAutoDepositEnabled/Amount`, `SetAutoWithdrawSettings(enabled, threshold, amount)` — validate, normalize, persist, `BankStateChanged`. **Auto-deposit validation (D-SF3.2):** enabling auto-deposit / setting `AutoDepositAmount` requires a positive amount the Private Bank Account can cover (`0 < amount ≤ BankAccountBalance`); reject otherwise (and refuse to enable while the bank is empty — nothing to stream). The runtime `TryAutoDeposit` still safely partial-draws if the balance later drops; this guard is the *set-time* sanity check the UI enforces first.
- **Checkpoint + pre-genesis (mandatory — Important Pattern 2):**
  - `BlockSessionCheckpointService.Snapshot` gains **one DTO**: `PlayerBankAccountService.CheckpointState PlayerBankState { get; set; }` (balance, 5 settings, history, 2 totals) — the CG.3 design note said to bundle when the flat list gets unwieldy; a brand-new service with no legacy checkpoints is the moment.
  - `RestoreFromCheckpoint(state)` from `ApplyCheckpointToServices()`; null DTO (legacy checkpoint) → keep `LoadState()` values.
  - `ResetToPreGenesisDefaults()`: balance → **`0`** (D-SF3.1), settings → defaults, history cleared; called from `BlockSessionCheckpointService.ResetToPreGenesisDefaults()`. A custom dose/toggle sticks only once a real block commits it — identical to the casino's `BankrollTarget` rule.

### 3.2 Changes to existing services (v4 — minimal)

v4 keeps Main funded exactly as today, so most of the v3 "extra-lazy shift" **reverts**. The player-side start is unchanged; we only *add* an empty bank account and its plumbing. What actually changes:

| Item | Today | After Step 12 (v4) |
|---|---|---|
| `PrincipalBalanceService.DefaultInitialBalance` | `39,900` (→ `40,000` with the `100` Bankroll) | **UNCHANGED** (D-SF3.1) — Main keeps its start |
| `BlockSessionCheckpointService.ResetToPreGenesisDefaults()` | Main/Bankroll → canonical start | **Main/Bankroll unchanged; ADD `PlayerBankAccountService` → bank `0`** (+ settings default, history clear) |
| `DiceGame.EnsureInitialBankrollFunded()` (`startup_default` recharge at `_Ready`) | Splits the `100` Bankroll dose off Main at entry | **UNCHANGED** (D-SF3.1) — no cascade, no repurpose; the v3 "rename to `EnsureCasinoFundedOnEntry` + full cascade" is dropped |
| `CasinoClientLedgerService._Ready()` | Registers `initial` `40,000` for "player" at boot | **`initial` UNCHANGED** (stays boot/first-recharge — D-SF3.4); the only additions are the **`Method` field** for later bank flows and **D-SF2.4 lifecycle coverage** (checkpoint snapshot + pre-genesis clear) |
| `BankrollProgramService` | always-on auto-recharge | + **`AutoRechargeEnabled`** toggle (default ON), respected by `SimulationService.TryPlayerAutoRechargeAndRestart` and the manual-path recharge; UI toggle in `BankrollProgrammer` |
| `BankrollProgramService.GetPerformancePercentVsInitial` | measures Main vs `40,000` | **Not broken in v4** (Main still starts ~`40,000`), but **extended** → surfaced as `OverallPl` (`NetWorthSc − 40,000`, which now includes banked SC) computed in the ScFinances controller from the three balance sources (D-SF2.7); relabel any "vs initial 40,000 (Main alone)" UI |
| Canonical Decisions (CLAUDE.md) | "Specific split 39,900/100" | **Split UNCHANGED**; add one new row: **"Private Bank Account starts at `0`"** (optional reserve) |
| Bank→Main auto-deposit into the recharge cascade | — | Added as a **fallback** (D-SF3.3): only when Main < dose **and** `AutoDepositEnabled` ON **and** bank > 0 |

### 3.3 `ScFinances` scene (hub — mirror of `CasinoGamblingFinances`)

Same skeleton as the casino scene (`MarginContainer` → `ScrollContainer` → `VBox`, CLAUDE.md UI pattern 1 — copy the CG.2-fixed structure):

```
[StatusBar placeholder]
Title: "SC Finances"
GameDateLabel        "Game date: 2009-…"
─────────
Private Bank Account:  0.00000000 SC                  ← PlayerBankAccountService (starts empty)
Main Balance:          39,900.00000000 SC             ← PrincipalBalanceService (funded as today)
Bankroll:              100.00000000 SC                ← BankrollStateService (read-only)
Net Worth (all):       40,000.00000000 SC             ← NetWorthSc
Overall P/L:           +0.00000000 SC                 ← OverallPl, green/red (mirror casino P/L label)
Auto-recharge dose:    100.00000000 SC  (read-only — managed in Bankroll Programmer)
─────────
Deposits — Private Bank Account → Main Balance   (bring reserve back into play)
  Auto-deposit: [OFF ☐]   Refill chunk: [ 1000 ] [Set]     ⓘ OFF = reserve stays safe; ON = auto-refill Main (reserve becomes gamblable)
  Deposit amount (SC): [        ] [Deposit → Main Balance]      ("Available: {BankAccountBalance} SC")
  DepositFeedbackLabel
─────────
Withdrawals — Main Balance → Private Bank Account
  Auto-withdraw: [OFF ☐]   Keep at least (floor): [ 1000 ]   Installment: [ 100 ] [Set]
  Withdraw amount (SC): [        ] [Withdraw → Private Bank Account]   ("Available: {MainBalance} SC")
  WithdrawFeedbackLabel
─────────
Bank Account Transfers                                 ← single history list, both directions
  BankTransferHistoryList (ItemList, "yyyy-MM-dd HH:mm:ss | ±amount SC | deposit/withdrawal | auto/manual")
─────────
[SC Transactions]  [Bets History]  [Bankroll Programmer]
[Back to Main Menu]
```

- **Limits are visible and enforced** (D-SF.2): each manual input row shows the live available balance of its source account; over-amount attempts are **rejected** with the available figure in the feedback label (D-SF2.5 — matches existing `BankrollProgrammer`/`CasinoGamblingFinances` validation). The `min(...)` clamp stays in the service API as a safety net, and a clamp-vs-reject toggle is documented ready (§7.5).
- **Auto-Deposit setup is validated before it applies** (D-SF3.2): the toggle + Refill-chunk row checks `AutoDepositAmount` against the live Private Bank Account balance (`0 < amount ≤ bank`) and refuses to enable while the bank is empty, with an explanatory feedback line — turning Auto-Deposit ON at a valid amount is the **opt-in extra-lazy** path.
- Deliberate exclusion (decided in v1): **no Main↔Bankroll panel** — `BankrollProgrammer` owns that; ScFinances links over and shows Bankroll/dose read-only.
- Controller mirrors `CasinoGamblingFinances.cs`: `GetNodeOrNull` refs, event subscriptions (`BankStateChanged`, `PrincipalBalanceService.BalanceChanged` (D-SF.7), `BankrollProgramService.TransfersChanged`/`AutoRechargeAmountChanged`) + 2 s fallback timer, symmetric `_ExitTree` unsubscribes, `InvariantCulture`, `IsInstanceValid` guards.

### 3.4 `ScTransactions` scene (mirror of `ClientsTransactions`)

The player's own external-SC story, single data source (`BankTransferHistory` — the merge complexity of v1 is gone with the unified record):

- **Header totals**: `Private Bank Account`, `Total deposited → casino`, `Total withdrawn → bank`, `Net inside casino` (= deposited − withdrawn), `Net Worth`.
- **List** (newest first, game time): `[DEPOSIT manual]` / `[DEPOSIT auto]` — green; `[WITHDRAWAL manual]` / `[WITHDRAWAL auto]` — orange. **v4 (D-SF3.4): no `[INITIAL]` tag here** — the starting `40,000` is not a bank transfer (Main is funded directly at start), so it never appears in `BankTransferHistory`; ScTransactions shows only real Bank↔Main moves the player made. (The casino-side `initial` still lives in `ClientsTransactions`, as today.)
- Internal movements (Bankroll↔Main) are **excluded** — same GLOSSARY rule `ClientsTransactions` follows. A fresh world shows an **empty** list until the player first banks SC.
- Back → `ScFinances`.

### 3.5 Auto-withdraw: threshold/surplus model (v1 §3.5 carried over, renamed)

Model A (threshold/surplus) stands as designed: on each trigger event, if `Main > effectiveFloor`, move one `AutoWithdrawAmount` installment of the surplus to the bank. No scheduler, no missed-payment concept; it is also exactly the shape `CasinoScBalanceService` can adopt for P6 repayments later. Model B (periodic/scheduled sweeps) stays deferred (§7).

**Trigger sites**: wherever Main Balance *increases* from play — after a successful `TryTransferBankrollToBalance` (banking winnings; call inside the service so every caller benefits), and via `PrincipalBalanceService.BalanceChanged` as the general hook (reentrancy-guarded: the withdrawal itself changes the balance). Never after a bank→Main deposit (pointless round-trip; the floor guard also blocks it mathematically in sane configs, but skip the call for clarity).

### 3.6 Lifecycle matrix (must all hold)

| Event | Player bank state |
|---|---|
| App restart, no block ever mined | `ResetToPreGenesisDefaults()` — **bank `0`** (D-SF3.1), Main/Bankroll to their canonical start as today, settings default, history empty (pre-genesis deposits/withdrawals vanish, like everything else) |
| Block mined | `CaptureCheckpoint()` snapshots the `CheckpointState` DTO — **post-bet**, inside the same post-`PersistFinancialState` group `ExecutePlayerBetOnce` uses since OQ-CG.10 (an auto-deposit fired during bet K's settlement must be inside bet K's checkpoint) |
| App restart, checkpoint exists | `RestoreFromCheckpoint(...)` — balance/settings/history revert to the last block |
| Legacy checkpoint (pre-Step 12) | DTO null → **seed bank at `0`, no migration** (D-SF2.8); Main Balance restores its checkpointed (pre-Step 12) value which may be > 0 — old worlds keep working, they simply have no bank funds/history yet (the player can withdraw to the bank manually). `WorldFormatVersion` clean reset acceptable if simpler |

### 3.7 `CasinoClientLedgerService` integration + taxonomy cleanup

Two changes, one of them fixing a **pre-existing mislabel this design surfaced**:

1. **New `Method` field** on `LedgerEntry` (`"manual"` \| `"auto"`, absent/legacy → `"manual"`). **v4 (D-SF3.4): the `initial` kind stays exactly as today** — the player's starting `40,000` equity, registered at boot/first-recharge; there is no "first bank→Main deposit = initial" logic (Main is funded at start, so no bank deposit funds it). The player's later **Bank→Main deposits register kind `"deposit"`** (never `initial`) with the real method; **Main→Bank withdrawals register kind `"withdrawal"`** with the real method. All existing kind-based filters (`GetLastDeposit`, deposited/withdrawn totals) keep working unchanged; `ClientsTransactions` gains the manual/auto tag in its line rendering. **Auto**-deposits **also** reset the since-last-deposit baseline (D-SF2.2 — locked) — they are real SC (re-)entering play, which is what the baseline measures.
2. **Taxonomy fix**: today `BankrollProgramService.TryTransferBankrollToBalance` registers kind `"withdrawal"` for **Bankroll → Main** — an *internal* movement (GLOSSARY explicitly calls its mirror "not an SC Deposit"; symmetry says Bankroll→Main is not an SC Withdrawal either). Now that `"withdrawal"` acquires its true meaning (SC leaving the casino to the bank), the internal one must be re-kinded → **`"bankroll_withdrawal"`**, excluded from "Total SC withdrawn" the way `"auto_recharge"` is excluded from deposits. `ClientsTransactions` totals become honest: they show real client↔casino boundary flows only. (Legacy entries with kind `"withdrawal"` that were really Bankroll→Main: accept the historical impurity — pre-genesis worlds reset anyway, and D-SF2.4's ledger lifecycle integration — checkpoint snapshot + pre-genesis clear — wipes discarded-session entries into shape regardless.)

### 3.8 Navigation & DepositPopup retirement (unchanged from v1 except labels)

- `SceneManager`: add `ScFinances`, `ScTransactions` ids/paths + one-deep `PreviousScene` memory in `Go()`.
- `MainMenu`: **"SC Finances"** button in the player-facing group (next to Dice Game, not among the DEV casino scenes).
- `BankrollProgrammer`: "SC Finances" button (and it gains the `AutoRechargeEnabled` toggle per D-SF.4).
- `BetsHistoryExplorer`: back button uses `PreviousScene ?? MainMenu` (three entry origins now).
- `DiceGame`: Deposit button → `Go(SceneId.ScFinances)`; `DepositPopup` node, `OnDepositPopupDepositConfirmed`/`OnDepositCanceled` removed; `UI/DepositPopup/` deleted once unreferenced.

```
MainMenu
├── ScFinances                      → Main Menu
│   ├── ScTransactions              → ScFinances
│   ├── BetsHistoryExplorer         → (origin-aware back)
│   └── BankrollProgrammer          → ScFinances / (existing back)
```

---

## 4. Phase checklist

### Phase SF.0 — `PlayerBankAccountService`: account core + lifecycle

**Files**: `Scripts/Services/PlayerBankAccountService.cs` (new), `project.godot`, `Scripts/Services/BlockSessionCheckpointService.cs`, `Scripts/Services/PrincipalBalanceService.cs`

- [ ] **SF.0.1** Create the service per §3.1: state, `BankTransferRecord`, JSON persistence (mirror `CasinoScBalanceService` structure incl. `SpecifyKind(Local)` and validity guards, 500-cap).
- [ ] **SF.0.2** `CalendarTimeService` in `_Ready()`; `GameLocalNow()` helper; wall-clock only in `UpdatedAtUtc`.
- [ ] **SF.0.3** `TriggerManualDeposit` / `TriggerManualWithdrawal` with `min(...)` limits and record/ledger writes.
- [ ] **SF.0.4** `TryAutoDeposit(needed)` loop (partial final chunk, safety cap) + `TryAutoWithdraw()` (floor guard, single installment) + all setters.
- [ ] **SF.0.5** `PrincipalBalanceService`: add `event Action BalanceChanged` (D-SF.7), invoked in `Deposit`/`TryWithdraw`/`SetBalance`.
- [ ] **SF.0.6** Register autoload #13 (order: after `PrincipalBalanceService`, before `BlockSessionCheckpointService` consumers of it).
- [ ] **SF.0.7** Checkpoint DTO (`CheckpointState`, `Snapshot.PlayerBankState`), capture + restore + null-DTO legacy gate.
- [ ] **SF.0.8** `ResetToPreGenesisDefaults()` + call from `BlockSessionCheckpointService`.

### Phase SF.1 — Existing-service changes (v4 — minimal; the "extra-lazy shift" is dropped)

**Files**: `Scripts/Services/BankrollProgramService.cs`, `Scripts/Services/BlockSessionCheckpointService.cs`, `Scripts/Services/SimulationService.cs`, `Screens/DiceGame/DiceGame.cs`, `Scripts/Services/CasinoClientLedgerService.cs` (`PrincipalBalanceService` only for the `BalanceChanged` event in SF.0.5 — its default balance is **unchanged**)

- [ ] **SF.1.1** ~~`DefaultInitialBalance` → 0~~ **DROPPED (D-SF3.1)** — `PrincipalBalanceService` default and the pre-genesis Main/Bankroll reset are **unchanged**. The only pre-genesis addition is `PlayerBankAccountService` → bank `0` (already covered by SF.0.8). Verify Main/Bankroll pre-genesis behavior is untouched.
- [ ] **SF.1.2** `BankrollProgramService.AutoRechargeEnabled` (default ON) + persistence + `ReplaceState`/checkpoint coverage; respected in `SimulationService.TryPlayerAutoRechargeAndRestart` and the manual-path recharge.
- [ ] **SF.1.3** Wire `TryAutoDeposit` as a **fallback** in the recharge cascade (D-SF3.3): in `TryPlayerAutoRechargeAndRestart` (autobet) and the manual-bet insufficient-funds path, when Main < dose → `TryAutoDeposit(dose)` → retry the recharge once. `TryAutoDeposit` is a no-op unless `AutoDepositEnabled` **and** bank > 0, so in early game (empty bank, toggle OFF) this changes nothing (mirror OQ-CG.7's dual-path lesson — manual and autobet both).
- [ ] **SF.1.4** ~~Repurpose/rename `EnsureInitialBankrollFunded` into a full cascade~~ **DROPPED (D-SF3.1)** — leave `DiceGame.EnsureInitialBankrollFunded()` **exactly as today** (Main→Bankroll split at `_Ready` using the current auto-recharge dose — default `100`, player-configurable; skipped if a manual initial recharge already funded the Bankroll — no bank involvement, no rename). Enforce the session-start invariant (D-SF2.6) at session-start/bet time as a plain guard (Bankroll must cover the bet), which today's Main→Bankroll recharge already satisfies.
- [ ] **SF.1.5** `CasinoClientLedgerService` (v4 — D-SF3.4): add the **`Method`** field; **keep** the boot/first-recharge `initial` registration as today (do **not** make it event-driven, do **not** remove it); add **D-SF2.4 lifecycle coverage** (snapshot `Entries` into the checkpoint DTO, restore in `ApplyCheckpointToServices`, clear player entries in `ResetToPreGenesisDefaults`); `"bankroll_withdrawal"` re-kind in `TryTransferBankrollToBalance`'s registration; totals filters updated (§3.7). Later Bank→Main deposits register `"deposit"` (never `initial`).
- [ ] **SF.1.6** Surface the performance metric (D-SF2.7): compute `NetWorthSc`/`OverallPl` in the **ScFinances controller** from the three balance sources (`PlayerBankAccountService` + `PrincipalBalanceService` + `BankrollStateService`); `PlayerBankAccountService` stays pure (no derived-total property). Relabel any `BankrollProgrammer` "vs initial 40,000 (Main alone)" wording.
- [ ] **SF.1.7** Verify bot recharges untouched (`TryRechargeAndRestartBot` / `NodeFinancialState` — bots have no bank account yet).

### Phase SF.2 — `ScFinances` scene

**Files**: `Screens/ScFinances/ScFinances.tscn/.cs` (new), `Scripts/Services/SceneManager.cs`, `Screens/MainMenu/*`, `Screens/BankrollProgrammer/*`

- [ ] **SF.2.1** Scene skeleton from `CasinoGamblingFinances.tscn` (ScrollContainer wrap, StatusBar, GameDateLabel).
- [ ] **SF.2.2** Balances block per §3.3 (bank / Main / Bankroll / NetWorth / OverallPl colored / dose read-only).
- [ ] **SF.2.3** Deposits section (toggle, dose setter, manual row with live "Available" label, feedback).
- [ ] **SF.2.4** Withdrawals section (toggle, floor + installment setters, manual row with live "Available", feedback).
- [ ] **SF.2.5** `BankTransferHistoryList` (single list, both directions, `±` sign + direction + method per line).
- [ ] **SF.2.6** Controller wiring per §3.3 (events incl. `BalanceChanged`, fallback timer, `_ExitTree`).
- [ ] **SF.2.7** `SceneManager` ids/paths + `PreviousScene`; MainMenu button; ScFinances navigation row.
- [ ] **SF.2.8** `BankrollProgrammer`: "SC Finances" button + `AutoRechargeEnabled` toggle UI.
  - **Follow-up (not in the original plan) — the DiceGame `StrategyControlPanel` auto-recharge toggle is now a proxy of this same service flag.** The plan added `BankrollProgramService.AutoRechargeEnabled` (SF.1.2) and its BankrollProgrammer checkbox (SF.2.8) but did not account for the pre-existing `Auto Recharge: ON/OFF` toggle already living, **coupled**, in DiceGame's `StrategyControlPanel` (it was a stand-alone per-run UI flag, handy for testing). To avoid two independent switches for one concept, the panel toggle is, **for the player**, re-wired into a second *access point* to the service flag (single source of truth = `BankrollProgramService.AutoRechargeEnabled`): it seeds FROM the service on every player-side load (`DiceGame.SyncPlayerAutoRechargeToggleFromService()`, called after `LoadActiveNodeStrategySnapshot` and after a saved-strategy load) and writes TO the service on genuine player interaction (`DiceGame.OnAutoRechargeToggledFromPanel` → `SetAutoRechargeEnabled`, skipped during loads / bot mode). The panel keeps the toggle in place (the testing coupling survives); `StrategyControlPanel.SetAutoRechargeEnabled(bool)` is the new silent seeder (no `AutoRechargeToggled` re-raise). **Bots are untouched** — each bot keeps its own per-node `AutoRechargeEnabled` (always ON), so the proxy is a no-op unless the player node is active. Documented in `Documentation/ProjectDesignManual.md` §25.8.

### Phase SF.3 — `ScTransactions` scene

- [ ] **SF.3.1** Scene mirroring `ClientsTransactions` layout; header totals per §3.4; colored list; Back → ScFinances.
- [ ] **SF.3.2** Render from `BankTransferHistory` (newest first, game time, `[INITIAL]` tag on the first-ever deposit).
- [ ] **SF.3.3** `ClientsTransactions`: render the new `Method` tag; exclude `"bankroll_withdrawal"` from withdrawn totals (regression-check the casino view).

### Phase SF.4 — DepositPopup retirement + BetsHistoryExplorer shortcut

- [ ] **SF.4.1** DiceGame Deposit button → ScFinances; popup node + handlers removed; delete `UI/DepositPopup/` when unreferenced.
- [ ] **SF.4.2** `BetsHistoryExplorer` origin-aware back.

### Phase SF.4B — Centralized player betting stats (3 groups) + persistent in-DiceGame bet history

**Added after SF.4 (not in the original plan; SF.5 documentation is postponed until after this).** Two DiceGame components surfaced as out-of-date now that deposits flow through `PlayerBankAccountService` instead of the retired `DepositPopup`. Both are fixed by pointing them at the **already-centralized** data (`UserStatsService` lifetime stats + `CasinoClientLedgerService` snapshots + `BetHistoryRepository` records) and reusing it across surfaces.

**Files**: `Scripts/History/PlayerFinancialStatsCalculator.cs` (new), `UI/FinancialBettingStats/FinancialBettingStats.cs` + `.tscn`, `Screens/ScFinances/ScFinances.tscn` + `.cs`, `Scripts/Services/UserStatsService.cs`, `Screens/DiceGame/DiceGame.cs`.

#### Task 1 — Three-group financial betting stats, shared by ScFinances + DiceGame

**Problem.** `FinancialBettingStats` (shown in DiceGame) renders "last deposit profit/loss" and "last deposit gambled" from `UserBettingStats.ProfitSinceDeposit` / `AmountWageredSinceDeposit`. That baseline is reset by `UserStatsService.RegisterDeposit()`, which is currently called on **every Bankroll recharge** (`SimulationService.TryPlayerAutoRechargeAndRestart`, `DiceGame.TryProgrammedBankrollTransfer`) — so it actually means "since last recharge", conflates *deposit* with *recharge*, and no longer reflects real SC deposits (which now go Bank→Main via `PlayerBankAccountService`). The correct, already-separated data lives in the **client ledger**: `GetLastDeposit` (kind `initial`|`deposit`) and `GetLastAutoRecharge` (kind `auto_recharge`), each carrying `TotalWageredSnapshot` / `NetProfitSnapshot` — exactly what `ClientsBetsHistory` already uses for the casino's "since last deposit / since last recharge" rows.

**Target: three stat groups**, each with **P/L** and **Gambled**:
| Group | P/L | Gambled | Source |
|---|---|---|---|
| **General** (lifetime) | `Stats.TotalProfit` | `Stats.TotalAmountWagered` | UserStatsService |
| **Since last bank deposit** | `TotalProfit − lastDeposit.NetProfitSnapshot` | `TotalAmountWagered − lastDeposit.TotalWageredSnapshot` | ledger `GetLastDeposit("player")` (initial/deposit) |
| **Since last bankroll recharge** *(NEW)* | `TotalProfit − lastRecharge.NetProfitSnapshot` | `TotalAmountWagered − lastRecharge.TotalWageredSnapshot` | ledger `GetLastAutoRecharge("player")` |

Player sign convention: **P/L = +TotalProfit** (the player's own gain), unlike `ClientsBetsHistory` which negates it for the casino. Before a real bank deposit, `GetLastDeposit` returns the `initial` (snapshots `0`/`0`) ⇒ "since deposit" == lifetime; before any recharge, `GetLastAutoRecharge` is null ⇒ "since recharge" == lifetime (mirror `ClientsBetsHistory`'s null-fallback). Amounts clamped at `≥ 0` (mirror `ClientsBetsHistory`'s `Math.Max(0m, …)`).

- [ ] **SF.4B.1** New pure calculator `Scripts/History/PlayerFinancialStatsCalculator.cs`: `readonly struct PlayerFinancialSummary { TotalProfit, TotalWagered, ProfitSinceDeposit, WageredSinceDeposit, ProfitSinceRecharge, WageredSinceRecharge; DateTime? LastDepositUtc, LastRechargeUtc; }` + `static PlayerFinancialSummary Compute(UserBettingStats stats, CasinoClientLedgerService ledger, string clientId = "player")`. No Godot/UI/state — single source of truth for the numbers (both surfaces call it; the casino's `ClientsBetsHistory` can optionally adopt it later, not required now).
- [ ] **SF.4B.2** **Redesign `FinancialBettingStats` compactly (≈ half the current footprint) AND upgrade it to the 3 groups.** The current layout is two tall side-by-side `VBoxContainer`s (font 27, separation 30, ~466×238 px) holding 4 stacked label pairs; adding a third group would overflow DiceGame's bottom-left slot. Replace it with a **content-sized table** — a `VBoxContainer` root: a small "Betting Statistics" title + a `GridContainer` (`columns = 3`) laid out scope×metric:

  ```
  Betting Statistics
                    P/L                 Gambled
  General           +0.00000000         0.00000000
  Since deposit     +0.00000000         0.00000000
  Since recharge    +0.00000000         0.00000000
  ```

  Header row (`""` | `P/L` | `Gambled`) + 3 scope rows = 12 cells; **font ~16–18** (vs 27) with tight separation ⇒ roughly half the area. Root **sizes to content** (no `anchors_preset = 15` fill, no fixed child offsets), so the exact same scene drops cleanly into both a fixed-offset slot (DiceGame) and a scroll `VBox` (ScFinances) — this is what removes SF.4B.3's old layout risk. The `.cs` keeps 6 `[Export]` value-label refs (3 scopes × {P/L, Gambled}), replaces `UpdateFrom(UserBettingStats)` / `UpdateFromTimeBased(...)` with `UpdateFrom(PlayerFinancialSummary)` (green/red on the three P/L cells via `Money.FormatSignedAdaptive`; gambled via `N8` InvariantCulture), and adds `ConnectTo(UserStatsService, CasinoClientLedgerService)` subscribing **both** `StatsChanged` **and** `LedgerChanged` (the since-X baselines move on ledger events), recomputing via the calculator, unsubscribing both in `_ExitTree`. English-only labels. DiceGame's instance offsets get re-tuned to the smaller block.
- [ ] **SF.4B.3** Show the same block in **ScFinances**: reuse the *same* redesigned component — add a "Betting Statistics" section (separator + the instanced `FinancialBettingStats.tscn`, now content-sized so it flows natively) to `ScFinances.tscn` and `ConnectTo(...)` it in `ScFinances.cs`. One component, one calculator, two host scenes ⇒ DiceGame and ScFinances show byte-identical numbers ("FinancialBettingStats muestra lo mismo de ScFinances"). *(The old fixed-offset fallback is no longer needed — SF.4B.2's content-sized redesign makes the component VBox-friendly by construction.)*
- [ ] **SF.4B.4** Update `DiceGame` to call `_financialStats.ConnectTo(_userStatsService, <ledger>)` (re-add a `CasinoClientLedgerService` ref — it was removed in SF.4.1 as unused; now used again) instead of the old `ConnectTo(UserStatsService)`. Leave `UserBettingStats.ProfitSinceDeposit`/`AmountWageredSinceDeposit` and the recharge-time `RegisterDeposit()` reset **in place but unused by the panel** (cosmetic legacy; a deeper cleanup of that conflation is out of scope here — note it for later).

#### Task 2 — Persistent in-DiceGame bet history on entry

**Problem.** `BetHistoryContainer` fills only from live `BetExecuted` events; on **re-entering** DiceGame it starts empty even though the full history is persisted in `UserStatsService.BetHistory` (`BetHistoryRepository`). The container **already** has `LoadFromHistoricalRecords(IReadOnlyList<BetRecord>)` — DiceGame just never calls it. Mirror how CalendarsNavigator / BetsHistoryExplorer / (now) ScFinances read the same centralized store.

- [ ] **SF.4B.5** Add `UserStatsService.GetRecentBets(int max)` → the last `max` `BetRecord`s from the loaded history (`BetHistory.Records`, after `EnsureFullHistoryLoaded()` or at least the latest chunk), newest-last to match `LoadFromHistoricalRecords`' `TakeLast`. Centralized query, reusing the existing repository.
- [ ] **SF.4B.6** In `DiceGame`, after the container pool exists and **after** the on-entry checkpoint restore / history rollback settles (so it reflects the committed history, not a pre-rollback view — see the existing `GetLoadedHistoryStats()` ordering note ~line 293), seed once: `_betHistoryContainer.LoadFromHistoricalRecords(_userStatsService.GetRecentBets(MaxRecentEntries))`. Live `BetExecuted` appends continue unchanged.
- [ ] **SF.4B.7** Verify: (a) leave DiceGame → return ⇒ recent history reproduces; (b) bets placed by the **delegated autobet while in another scene** are in the store ⇒ shown on return; (c) newest-first ordering preserved (pool `MoveChild(item, 0)`); (d) pre-genesis / block-rollback semantics unchanged (the store already rolls back — the seed just reads whatever survived).

#### Testing (SF.4B)

- [ ] Deposit via ScFinances (Bank→Main) ⇒ "since last deposit" P/L & Gambled reset to `0` in **both** ScFinances and DiceGame; General unchanged.
- [ ] Trigger a Bankroll recharge ⇒ "since last recharge" resets; "since last deposit" does **not** (distinct baselines).
- [ ] Numbers identical between ScFinances and DiceGame's `FinancialBettingStats` at all times.
- [ ] Re-enter DiceGame ⇒ most-recent bet rows reappear; keep betting ⇒ new rows prepend; no duplication of the seeded rows.
- [ ] InvariantCulture / English / colors on all new labels; `_ExitTree` unsubscribes (no dangling events on scene free).

### Phase SF.5 — Documentation truth pass

- [ ] **SF.5.1** `CLAUDE.md`: autoloads 12 → 13 + `PlayerBankAccountService` section; navigation map; Canonical Decisions — **keep the `39,900/100` split unchanged**, add one row: **"Private Bank Account starts at `0`" (optional reserve, all automation OFF by default)**; game-over row per D-SF2.1 (`Bank + Main + Bankroll = 0`, worded to leave room for the future BTC→SC coin-swap escape hatch §7.4); session-start invariant note (D-SF2.6); Implementation Status.
- [ ] **SF.5.2** `GLOSSARY.md`: update **SC Deposit** (DepositPopup → ScFinances; bank as source; auto vs manual), **SC Withdrawal** (new entry — Main → Private Bank Account); new entries **Private Bank Account**, **Casino SC Account**, **Auto-Deposit**, **Auto-Withdraw**, **Net Worth**; fix the Bankroll→Main "withdrawal" wording to `bankroll_withdrawal`; un-plan the "Main Balance Auto/Manual Recharge (planned)" entries if auto-withdraw covers their intent (check wording).
- [ ] **SF.5.3** `ProjectDesignManual.md`: new chapter — three-account topology (Main funded as today + empty optional bank reserve), the opt-in withdraw/deposit flows, auto-deposit-as-fallback, lifecycle matrix, ledger taxonomy fix, future ScBank note.
- [ ] **SF.5.4** `DESIGN_OVERVIEW.md` / `PRIVATE_ROADMAP.md`: status labels; P6 note (casino repayments can adopt the auto-withdraw threshold/surplus mechanism).

---

## 5. Testing checklist

- [ ] **Fresh world (v4)**: ScFinances shows **Bank `0`** / Main + Bankroll = `40,000` (as today, `39,900`/`100` after DiceGame-entry recharge) / Net Worth `40,000` / Overall P/L `+0`; **empty** BankTransferHistory and empty ScTransactions; StatusBar unchanged (D-SF2.9).
- [ ] **DiceGame entry funds as today (default dose)**: Main holds the full `40,000` at world start (Bankroll `0`); entering DiceGame runs the *unchanged* `EnsureInitialBankrollFunded` with the default `100` dose → `39,900/100` — **no bank involvement, bank stays `0`**; no bank deposit/ledger `initial`-via-bank is created; the `initial` in `ClientsTransactions` remains the boot/first-recharge starting-stake entry as today.
- [ ] **DiceGame entry with a changed dose**: raise the auto-recharge dose to `500` (no manual recharge), then enter DiceGame → the automatic initial recharge uses the new dose → Main `39,500` / Bankroll `500`, bank still `0`.
- [ ] **Manual initial recharge preempts the auto split**: do a manual Bankroll recharge (BankrollProgrammer) before first DiceGame entry → entering DiceGame does **not** double-fund; the automatic initial recharge is deactivated, final Bankroll = the player's manual amount, Main = `40,000 − that`.
- [ ] **Auto-Deposit amount validation (D-SF3.2)**: with bank `0`, attempting to enable Auto-Deposit is refused ("nothing to stream"); with bank `5,000`, setting `AutoDepositAmount` to `0`/negative or `> 5,000` is rejected with the available figure; a positive amount `≤ 5,000` is accepted and enables the opt-in extra-lazy fallback.
- [ ] **Bank untouched in normal early play**: run a full autobet session with all bank toggles at defaults (Auto-Deposit OFF, Auto-Withdraw OFF) → **Bank stays `0` throughout**; only Main↔Bankroll move; ScFinances/ScTransactions bank history stays empty.
- [ ] **Opt-in reserve round-trip (manual)**: after some winnings, manual **Withdraw** Main→Bank `5,000` → Bank `5,000`, Main −`5,000`, one `[WITHDRAWAL manual]` row, Net Worth unchanged; then manual **Deposit** Bank→Main `2,000` → Bank `3,000`, Main +`2,000`, one `[DEPOSIT manual]` row.
- [ ] **Auto-Withdraw opt-in (surplus/floor)**: enable Auto-Withdraw floor `1,000` installment `100`; Bankroll→Main leaves Main `1,050` → moves `50` to bank; Main below floor → nothing; disabled → nothing.
- [ ] **Auto-Deposit opt-in as fallback**: bank holds `5,000` (player banked it), Auto-Deposit ON; drive Main+Bankroll toward `0` → when Main can't cover a recharge, `TryAutoDeposit` pulls a `1,000` chunk from bank→Main and play continues. With **Auto-Deposit OFF** (default) in the same state → **no draw**, session stops and prompts a manual deposit, bank's `5,000` stays safe (not game-over, bank > 0).
- [ ] **Session-start invariant (D-SF2.6)**: Bankroll below the required bet → no session (human or bot) may start; satisfied by Main→Bankroll recharge as today. With `AutoRechargeEnabled` OFF, betting stops and waits for a manual Bankroll recharge.
- [ ] **Manual deposit limit**: Bank `200`, request `1,000` → effective `200` (or rejection per D-SF2.5), feedback shows available; Bank hits `0` freely.
- [ ] **Manual withdrawal limit**: symmetric on Main.
- [ ] **No ping-pong**: auto-deposit ON + auto-withdraw ON (bank pre-funded) with floor `0` → after an auto-deposit, no immediate withdraw below one recharge-dose (`effectiveFloor` guard).
- [ ] **Auto-recharge toggle OFF**: bankroll empties → no recharge, session stops with funds still in Main (today's InsufficientBalance path, now player-chosen).
- [ ] **All-empty (game over)**: Bank `0` + Main `0` + Bankroll `0` → session stops; game-over condition per D-SF2.1. (If the player has any banked SC, it's **not** game over — see the Auto-Deposit OFF test.)
- [ ] **Pre-genesis revert**: deposits/withdrawals + toggle changes, restart without a block → **Bank `0`** / Main + Bankroll to canonical start / settings default / empty history; ledger reflects D-SF2.4's decision.
- [ ] **Checkpoint stick**: block mined mid-state → restart → bank balance, settings, history, ledger state all revert exactly to the block.
- [ ] **Checkpoint ordering**: an auto-deposit fired by bet K's recharge is inside bet K's checkpoint (OQ-CG.10 principle).
- [ ] **Legacy world** (pre-Step 12 checkpoint): boots without crash; Main keeps its checkpointed value; bank behaves per D-SF2.8 decision.
- [ ] **Taxonomy**: Bankroll→Main shows as `bankroll_withdrawal` (excluded from casino "Total withdrawn"); Main→bank shows as `withdrawal` (included); manual/auto tags visible in both ScTransactions and ClientsTransactions.
- [ ] **Navigation**: MainMenu→ScFinances→each sub-destination and back; BetsHistoryExplorer returns to its real origin from all three entries; DiceGame Deposit button lands on ScFinances; no `DepositPopup` references remain.
- [ ] **InvariantCulture / English / game-time**: every new label and record; zero `DateTime.Now/UtcNow` outside `UpdatedAtUtc`.
- [ ] **Scroll**: ScFinances with 500 history entries scrolls (pattern 1); wheel works over labels.

---

## 6. Decisions locked (round 2 — 2026-07-03)

All ten round-2 questions are now resolved (`D-SF2.x` = decisions, not questions). **Note:** the round-3 model pivot (§0.5) supersedes parts of D-SF2.6/2.8/2.10 — the v4 override is flagged inline in each affected row.

| # | Decision |
|---|---|
| **D-SF2.1** | **Game-over redefined to total ruin: `BankAccountBalance + Main Balance + Bankroll = 0`.** With toggles OFF a player can always manually deposit while the bank holds anything, so only all-accounts-empty is truly dead. Canonical Decisions update in SF.5.1. **Escape hatch (documented only, NOT this plan — §7.4):** once BTC has in-game value, the player may perform a **BTC→SC coin swap** (and eventually **auto-swaps**) to refill SC and dodge game-over. That belongs to the future SC↔BTC exchange plan; game-over's definition here must be written so it does not preclude a later "BTC can rescue you" path. |
| **D-SF2.2** | **Auto-deposits reset the since-last-deposit baseline (`GetLastDeposit`) exactly like manual ones.** The baseline measures external SC entering play; method is irrelevant. The `Method` field still lets analytics split manual/auto later with no re-migration. |
| **D-SF2.3** | **New `LedgerEntry.Method` field** (`"manual"`\|`"auto"`), not new kinds — zero breakage of existing `Kind ==` filters, one rendering change (§3.7). |
| **D-SF2.4** | **`CasinoClientLedgerService` brought fully into the lifecycle now.** Snapshot `Entries` into the block checkpoint via a `CheckpointState` DTO, restore in `ApplyCheckpointToServices()`, clear player entries in `ResetToPreGenesisDefaults()` — the exact leak class OQ-BP.6/7 fixed for other services. Not deferred. |
| **D-SF2.5** | **Manual over-amount → reject** with the available figure ("Insufficient funds — available: 200.00000000 SC"), matching every existing `BankrollProgrammer`/`CasinoGamblingFinances` validation. `min(...)` stays in the service API as a final safety net; the UI validates first. **Documented alternative kept ready (§7.5):** a **clamp-vs-reject toggle** so the silent-clamp behavior can be switched on trivially if a future UX pass wants it — the `min(...)` service semantics already support it, only a UI branch + one setting would be added. |
| **D-SF2.6** | ⚠️ **Largely superseded by §0.5.** The v3 "keep initial funding at DiceGame entry via a full bank→Main→Bankroll cascade" is **dropped** — in v4 there is no cascade; `EnsureInitialBankrollFunded` (Main→Bankroll `100` split) stays exactly as today (D-SF3.1), and `AutoDepositEnabled` now defaults **OFF** (D-SF3.2), not ON. **What survives:** the **session-start invariant** — no user (bot or human) may start an auto/manual bet session with Bankroll below the required bet; a running session refills via auto-recharge when ON, otherwise stops and waits for a manual recharge. In v4 this is satisfied by Main→Bankroll as today; the bank→Main auto-deposit is a rare fallback (D-SF3.3). |
| **D-SF2.7** | **`NetWorthSc`/`OverallPl` replace `GetPerformancePercentVsInitial`, computed in the `ScFinances` controller by reading the three balance sources (`PlayerBankAccountService` + `PrincipalBalanceService` + `BankrollStateService`) — `PlayerBankAccountService` stays pure** (it never reaches into the other two just to expose a derived total). Any future consumer that needs the figure outside ScFinances (e.g. the deferred StatusBar Net Worth, §7.6) computes it the same way from the three sources. Relabel/repoint any UI still claiming "vs initial 40,000" about Main alone (notably `BankrollProgrammer`'s performance label). |
| **D-SF2.8** | **Seed the bank at `0`, no migration — now UNIFORM for fresh and legacy worlds (v4, D-SF3.1).** The fresh-vs-legacy inconsistency this decision originally exposed is what drove the round-3 pivot: rather than seed fresh worlds' bank at `40,000` (a migration of the start out of Main) and legacy at `0`, **every** world now starts the bank at `0` with Main funded as today. No migration anywhere; `WorldFormatVersion` clean reset acceptable if simpler. **Design intent:** bank interaction is deliberately deferred to *later* in-game — the bank starts empty and its automation defaults OFF, so **Main↔Bankroll alone carries the first in-game months/years** and a new player can ignore the bank entirely, keeping the learning curve manageable. The bank's own dynamics (interest, term deposits) are a **future bank plan**, not this one. |
| **D-SF2.9** | **StatusBar left untouched** — the Private Bank Account is visible only in ScFinances for now, since meaningful bank interaction arrives in later game and a Net Worth figure isn't yet needed. **All Net-Worth-in-UI proposals are documented and implementation-ready (§7.6)** — including a possible future StatusBar Net Worth figure and a broader player-UI redesign around the bank account — to switch on the moment the bank account becomes a live concern. |
| **D-SF2.10** | ⚠️ **Moot / superseded by §0.5 (D-SF3.4).** This resolved "what method does the first *bank→Main* funding carry" — but in v4 Main is funded at start, so there **is** no first bank→Main funding to badge. The ledger `initial` reverts to today's meaning (the starting `40,000` stake, at boot/first-recharge); the player's later Bank→Main deposits are ordinary `deposit` entries with a `Method`, never `initial`. The idempotent/method-agnostic "first-funding" rule is retired along with the cascade. |

---

## 7. Future / deferred (documented only — NOT in this plan's implementation scope)

### 7.1 `ScBank` scene — reasons to keep SC at the bank (user-requested documentation, D-SF.1)

The game will want the player to *prefer* pulling SC out of the casino when possible. Pending design, its own plan later. Candidate dynamics (names proposed):

- **Fixed-Term Deposits** (`TermDeposit`): freeze an amount at the bank for a defined game-time span (7/30/90 game-days) earning interest (`InterestRate` per term); early withdrawal forfeits interest (or a penalty). The bank finally *does something* with the player's equity — and it is the same bank that charges the casino nothing for its infinite credit line, a delicious asymmetry to surface in flavor text.
- **Savings rate**: small passive interest on the free (unfrozen) `BankAccountBalance`, accrued per game-day — a gentle pull vs the casino's RTP < 100%.
- **Casino-side push factors** (interacts with existing open questions): minimum-wager requirements and inactivity fees on idle Main Balance — SC parked *inside* the casino erodes; SC parked at the bank grows. Together they make the auto-withdraw toggle a genuinely strategic choice, not bookkeeping.
- `ScBank` becomes the natural third sibling: `ScFinances` (flows) / `ScBank` (products) / `BankrollProgrammer` (casino-side doses).

### 7.2 Coherent auto-deposit/auto-withdraw evolutions beyond Basic Mode (D-SF.7 elaboration)

- **Profit sweeping on stop conditions**: when a session ends by `StopOnProfit`, auto-withdraw the realized profit (or a %) straight to the bank — "lock in the win" as a strategy checkbox next to the existing stop toggles. Mirror: `StopOnLoss` could trigger a *cool-down* where auto-deposit refuses to fire for N game-hours (self-exclusion mechanics).
- **Scheduled sweeps (model B, revived)**: a game-weekly "payday" that moves any Main Balance surplus above the floor to the bank — pairs naturally with fixed-term ladders (each sweep feeds the next `TermDeposit`).
- **Session budgeting / allowance**: a per-game-day auto-deposit cap ("fund at most `X` SC per day") — the responsible-gambling inverse of the credit line, entirely expressible with the existing knobs plus one counter.
- **Bot bank accounts**: the user already hinted "quizá luego otros bots" — `PlayerBankAccountService`'s shape (balance + two-direction automation + history) is deliberately client-agnostic; a future `clientId`-keyed generalization gives every bot the same bank relationship, and `CasinoClientLedgerService` is already multi-client by design.
- **Casino adopts the same outflow (P6)**: the casino's open "when does it repay the bank" question becomes: the casino runs `TryAutoWithdraw()` with its own floor/installment against its *debt* instead of an account — the threshold/surplus mechanism is identical, only the destination differs (repayment vs equity). One mechanism, two semantics — same as this whole plan.
- **Interest-bearing debt for the casino**: once the player earns interest on equity, symmetry suggests the casino should *pay* interest on `TotalLoaned` — giving `CumulativeProfitSinceLoan` a time dimension and the P6 repayment threshold real urgency.

### 7.3 Retired: the "extra-lazy" player-funding model (v2/v3 — recorded so it isn't re-proposed)

v2/v3 seeded the whole `40,000` **at the bank** (Main `0`) and streamed it into the casino via a bank→Main→Bankroll **cascade** at DiceGame entry, mirroring the casino's on-demand loan draw. It was dropped in v4 (§0.5, D-SF3.1) because: (1) its end state was **identical to today's start** (`39,900/100`, bank `0`), so the cascade was pure ceremony; (2) it forced `PrincipalBalanceService.DefaultInitialBalance → 0` and a repurposed `EnsureInitialBankrollFunded`, touching working init code for no gain; (3) it created a **fresh-vs-legacy seed split** (fresh bank `40,000` vs legacy bank `0`), and seeding a fresh bank at `40,000` is itself the migration D-SF2.8 forbade. The player *owns* their money — there is no reason to model it as on-demand credit like the casino. If a future design genuinely wants "money starts at the bank" (e.g. an ScBank onboarding flow), revisit deliberately; do not reintroduce it as plumbing. **Scope note:** what was retired is only the *automatic seeding* (bank pre-loaded with the `40,000`); the extra-lazy **streaming** itself lives on as the **opt-in** Bank→Main auto-deposit fallback (D-SF3.2/3.3) — a player who banks a reserve and enables Auto-Deposit at a valid amount gets exactly that behavior, by choice.

### 7.4 BTC→SC coin swap as a game-over escape hatch (D-SF2.1 tail — future SC↔BTC exchange plan)

Game-over is locked to total ruin `Bank + Main + Bankroll = 0` (D-SF2.1). But once BTC has real in-game value (the player is far enough into Bitcoin history), a wiped-out SC player should be able to **swap BTC → SC** to refill and keep playing — and, later, configure **auto-swaps** that top up SC from BTC holdings automatically when the accounts run dry. This belongs to the **future SC↔BTC exchange plan**, not Step 12. Requirement carried forward: implement the game-over check so it does **not** hard-close the game in a way that precludes a later "BTC can rescue you" path (e.g. game-over should be a state the exchange layer can later intercept before it's final, rather than an irreversible terminal). No code for this in Step 12 — just don't paint it into a corner.

### 7.5 Clamp-vs-reject toggle for manual transfers (D-SF2.5 tail — ready to enable)

D-SF2.5 locks **reject** as the manual over-amount behavior. The `min(...)` clamp already lives in the service API as a safety net, so the silent-clamp alternative is one small change away: add a single setting (e.g. `ClampManualTransfersToAvailable`, default OFF) and one UI branch — OFF = validate-then-reject (today's behavior); ON = accept the entry and clamp to available, stating the clamped figure in the feedback label. Documented so a future UX pass can flip clamp on without touching the service math.

### 7.6 Net-Worth-in-UI proposals — documented, implementation-ready (D-SF2.9 tail)

StatusBar is left untouched in Step 12 (bank visible only in ScFinances) because meaningful bank interaction arrives later in the game (D-SF2.8/2.9). These are ready to switch on the moment the bank account becomes a live concern:

- **StatusBar Net Worth figure**: add one figure — `NetWorthSc` (= Bank + Main + Bankroll) — to the all-scenes StatusBar, rather than a fourth raw balance. It's the single "how am I doing" number and changes rarely mid-session (cheap to compute/refresh). StatusBar computes it the same way ScFinances does — reading the three balance sources (D-SF2.7 keeps the service pure) — so this is a StatusBar-only change.
- **Player-UI redesign around the bank account**: once the bank account is a real strategic surface (term deposits, savings rate, push/pull vs the casino — §7.1), the player-facing layout can shift to foreground Net Worth + the three account balances as a coherent dashboard, the way this plan's ScFinances hub prototypes it. Treat ScFinances as the seed of that redesign.

### 7.7 Other deferred items

- **Casino auto-loan/auto-repay toggles** (D-SF.4 tail): requires an insolvency policy ("the game never blocks a bet on casino insolvency" would break with the toggle OFF) — design alongside P6.
- **Unified multi-source player ledger**: if ScTransactions ever gains a fourth source (P7 BTC→SC conversions), fold sources into one queryable stream then.
