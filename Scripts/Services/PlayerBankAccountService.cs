using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;

// Autoload #13 (Step 12 / SF.0). Owns the player's PRIVATE BANK ACCOUNT — an optional SC reserve that lives
// OUTSIDE the casino (v4 / D-SF3.1: it starts EMPTY at 0; the canonical 40,000 stays in Main Balance as today).
// The player OWNS this money (no credit, no debt — unlike the casino's CasinoScBalanceService loan relationship):
//   • withdraw  Main → Bank  (park a reserve, safe from the casino)   — TriggerManualWithdrawal / TryAutoWithdraw
//   • deposit   Bank → Main  (bring the reserve back into play)       — TriggerManualDeposit  / TryAutoDeposit
// All four flows are built and functional now, but every automation defaults OFF (D-SF3.2), so a new player can
// ignore the bank entirely for the first in-game months/years and play pure Main↔Bankroll as today. This service
// mutates PrincipalBalanceService (the Main side) and NEVER touches the Bankroll (that stays BankrollProgramService's
// job). Persisted per block (block = the only commit) — snapshotted into BlockSessionCheckpointService, reverts to
// the empty-bank defaults on every pre-genesis restart. See AIHelperFiles/step12-player-sc-finances-plan.md §3.1.
public partial class PlayerBankAccountService : Node
{
	// v4 (D-SF3.1): the bank starts EMPTY — the 40,000 stays in Main Balance, funded exactly as today.
	public const decimal InitialBankAccountBalance    = 0.00000000m;
	// v4 (D-SF.3): a modest reserve-refill chunk, only ever used once the player banks SC AND opts into
	// Auto-Deposit (NOT the dead extra-lazy 40,000 chunk).
	public const decimal DefaultAutoDepositAmount     = 1_000.00000000m;
	public const decimal DefaultAutoWithdrawThreshold = 1_000.00000000m; // Main Balance floor to keep in the casino
	public const decimal DefaultAutoWithdrawAmount    =   100.00000000m; // one bankroll-dose installment per event

	public const string DirectionBankToMain = "bank_to_main"; // deposit: SC re-enters Main Balance (into the casino)
	public const string DirectionMainToBank = "main_to_bank"; // withdrawal: SC leaves Main Balance to the bank reserve
	public const string MethodManual        = "manual";
	public const string MethodAuto          = "auto";

	private const string StatePath = "user://player_bank_account_state.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	// Recharges/transfers can fire often (auto-withdraw per winning transfer), so cap history to keep the JSON /
	// checkpoint bounded (oldest trimmed) — mirrors CasinoScBalanceService.MaxRechargeHistory.
	private const int MaxTransferHistory = 500;
	// Safety bound on TryAutoDeposit's loop (mirror CasinoScBalanceService.MaxAutoRechargeIterations). Normal
	// draws take 1–2 iterations; this only trips under a pathological dev misconfiguration to avoid a freeze.
	private const int MaxAutoDepositIterations = 100_000;

	private PrincipalBalanceService  _principalBalance;
	private BankrollProgramService   _bankrollProgram;
	private CalendarTimeService      _calendarTime;
	private CasinoClientLedgerService _ledger;
	private UserStatsService         _userStats;

	// One Bank↔Main transfer. Direction ∈ {bank_to_main, main_to_bank}; Method ∈ {manual, auto}. GameDateLocal
	// is game-world time (CalendarTimeService), never wall-clock — displayed and persisted (CLAUDE.md Pattern 2).
	public sealed class BankTransferRecord
	{
		public decimal  Amount        { get; set; }
		public string   Direction     { get; set; } = string.Empty;
		public string   Method        { get; set; } = MethodManual;
		public DateTime GameDateLocal { get; set; }
	}

	private readonly List<BankTransferRecord> _bankTransferHistory = new();
	public IReadOnlyList<BankTransferRecord> BankTransferHistory => _bankTransferHistory;

	// Balance + the five automation settings. All default to the empty-bank, everything-OFF state (D-SF3.2/3.3).
	public decimal BankAccountBalance    { get; private set; } = InitialBankAccountBalance;
	public bool    AutoDepositEnabled    { get; private set; } = false;
	public decimal AutoDepositAmount     { get; private set; } = DefaultAutoDepositAmount;
	public bool    AutoWithdrawEnabled   { get; private set; } = false;
	public decimal AutoWithdrawThreshold { get; private set; } = DefaultAutoWithdrawThreshold;
	public decimal AutoWithdrawAmount    { get; private set; } = DefaultAutoWithdrawAmount;

	// Running totals for the ScTransactions header (independent of the capped history).
	public decimal TotalDepositedToCasino   { get; private set; } = 0m; // Σ bank_to_main (SC re-entering play)
	public decimal TotalWithdrawnFromCasino { get; private set; } = 0m; // Σ main_to_bank (SC parked at the bank)

	// Reentrancy guard for the PrincipalBalanceService.BalanceChanged → TryAutoWithdraw hook (SF.1): the
	// withdrawal itself changes Main Balance and would otherwise recurse.
	private bool _inTransfer;

	public event Action BankStateChanged;

	public override void _Ready()
	{
		LoadState();
		_principalBalance = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_bankrollProgram  = GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService");
		_calendarTime     = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_ledger           = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		_userStats        = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		GD.Print($"[PlayerBankAccountService] Ready — BankAccountBalance={BankAccountBalance:F8} SC  AutoDeposit={AutoDepositEnabled}({AutoDepositAmount:F8})  AutoWithdraw={AutoWithdrawEnabled}(floor={AutoWithdrawThreshold:F8}, amt={AutoWithdrawAmount:F8})");
	}

	private decimal MainBalance => _principalBalance?.CurrentBalance ?? 0m;

	// Game-world time for a transfer record (never wall-clock). Fallback only if the calendar autoload is absent.
	private DateTime GameLocalNow() => _calendarTime?.CurrentLocalDateTime ?? DateTime.Now;
	private DateTime GameUtcNow()   => _calendarTime?.CurrentUtcDateTime ?? DateTime.UtcNow;

	// Bank → Main deposits register kind "deposit" in the casino client ledger (SF.1.5 / D-SF3.4) — real SC
	// (re-)entering play, which resets the since-last-deposit baseline for both manual and auto (D-SF2.2).
	private void RegisterLedgerDeposit(decimal amount, string method)
	{
		decimal wagered = _userStats?.Stats?.TotalAmountWagered ?? 0m;
		decimal profit  = _userStats?.Stats?.TotalProfit ?? 0m;
		_ledger?.RegisterDeposit("player", amount, GameUtcNow(), wagered, profit, method);
	}

	private void AddTransferRecord(decimal amount, string direction, string method)
	{
		_bankTransferHistory.Add(new BankTransferRecord
		{
			Amount        = Money.Normalize(amount),
			Direction     = direction,
			Method        = method,
			GameDateLocal = GameLocalNow()
		});
		if (_bankTransferHistory.Count > MaxTransferHistory)
			_bankTransferHistory.RemoveRange(0, _bankTransferHistory.Count - MaxTransferHistory);
	}

	// ---- Manual flows (D-SF.2: the natural limit is the source account's balance) --------------------------------

	// Deposit Bank → Main. Clamps to the bank balance (min safety net; the UI rejects over-amounts first, D-SF2.5).
	// Returns false with nothing moved if the bank is empty or the amount is non-positive.
	public bool TriggerManualDeposit(decimal amount)
	{
		amount = Money.Normalize(amount);
		decimal effective = Money.Normalize(Math.Min(amount, BankAccountBalance));
		if (effective <= 0m) return false;

		BankAccountBalance = Money.Normalize(BankAccountBalance - effective);
		_inTransfer = true;
		_principalBalance?.Deposit(effective);
		_inTransfer = false;
		TotalDepositedToCasino = Money.Normalize(TotalDepositedToCasino + effective);
		AddTransferRecord(effective, DirectionBankToMain, MethodManual);
		RegisterLedgerDeposit(effective, MethodManual);
		SaveState();
		BankStateChanged?.Invoke();
		return true;
	}

	// Withdraw Main → Bank. Clamps to the Main balance. Returns false with nothing moved on a non-positive/short amount.
	public bool TriggerManualWithdrawal(decimal amount)
	{
		amount = Money.Normalize(amount);
		decimal effective = Money.Normalize(Math.Min(amount, MainBalance));
		if (effective <= 0m) return false;

		_inTransfer = true;
		bool ok = _principalBalance?.TryWithdraw(effective) ?? false;
		_inTransfer = false;
		if (!ok) return false;

		BankAccountBalance = Money.Normalize(BankAccountBalance + effective);
		TotalWithdrawnFromCasino = Money.Normalize(TotalWithdrawnFromCasino + effective);
		AddTransferRecord(effective, DirectionMainToBank, MethodManual);
		_ledger?.RegisterWithdrawal("player", effective, GameUtcNow(), MethodManual);
		SaveState();
		BankStateChanged?.Invoke();
		return true;
	}

	// ---- Auto flows (fallbacks; default OFF — D-SF3.2/3.3) -------------------------------------------------------

	// Bank → Main auto-deposit fallback (D-SF3.3): fires only when a recharge finds Main short AND the player has
	// opted in (AutoDepositEnabled) AND actually banked SC. Draws min(AutoDepositAmount, bank) per iteration into
	// Main, looping until Main covers neededInMain or the bank empties — the final draw may be a partial chunk (the
	// account can be freely emptied, D-SF.2). Each draw is one bank_to_main/auto record. Returns whether Main now
	// covers the need. No-op (returns whether Main already covers it) when disabled or the bank is empty.
	public bool TryAutoDeposit(decimal neededInMain)
	{
		neededInMain = Money.Normalize(neededInMain);
		if (!AutoDepositEnabled || BankAccountBalance <= 0m)
			return MainBalance >= neededInMain;

		decimal totalDrawn = 0m;
		int safety = 0;
		while (MainBalance < neededInMain && BankAccountBalance > 0m && safety++ < MaxAutoDepositIterations)
		{
			decimal draw = Money.Normalize(Math.Min(AutoDepositAmount, BankAccountBalance));
			if (draw <= 0m) break;
			BankAccountBalance = Money.Normalize(BankAccountBalance - draw);
			_inTransfer = true;
			_principalBalance?.Deposit(draw);
			_inTransfer = false;
			TotalDepositedToCasino = Money.Normalize(TotalDepositedToCasino + draw);
			AddTransferRecord(draw, DirectionBankToMain, MethodAuto);
			totalDrawn = Money.Normalize(totalDrawn + draw);
		}

		if (totalDrawn > 0m)
		{
			// One ledger deposit for the whole streamed amount (the baseline reset is what matters, D-SF2.2) —
			// the per-draw BankTransferRecords above keep the fine-grained ScTransactions detail.
			RegisterLedgerDeposit(totalDrawn, MethodAuto);
			SaveState();
			BankStateChanged?.Invoke();
		}
		return MainBalance >= neededInMain;
	}

	// Main → Bank auto-withdraw (threshold/surplus, §3.5). One installment per trigger event. The floor is the
	// max of AutoWithdrawThreshold and the live recharge dose — the anti-ping-pong guard: an auto-deposit fires
	// precisely when Main can't cover a dose, so auto-withdraw must never drain Main back below one dose. No-op
	// (returns false) unless enabled and a positive surplus above the floor exists.
	public bool TryAutoWithdraw()
	{
		if (!AutoWithdrawEnabled) return false;

		decimal dose = _bankrollProgram?.AutoRechargeAmount ?? 0m;
		decimal effectiveFloor = Money.Normalize(Math.Max(AutoWithdrawThreshold, dose));
		decimal surplus = Money.Normalize(MainBalance - effectiveFloor);
		if (surplus <= 0m) return false;

		decimal move = Money.Normalize(Math.Min(AutoWithdrawAmount, surplus));
		if (move <= 0m) return false;

		_inTransfer = true;
		bool ok = _principalBalance?.TryWithdraw(move) ?? false;
		_inTransfer = false;
		if (!ok) return false;

		BankAccountBalance = Money.Normalize(BankAccountBalance + move);
		TotalWithdrawnFromCasino = Money.Normalize(TotalWithdrawnFromCasino + move);
		AddTransferRecord(move, DirectionMainToBank, MethodAuto);
		_ledger?.RegisterWithdrawal("player", move, GameUtcNow(), MethodAuto);
		SaveState();
		BankStateChanged?.Invoke();
		return true;
	}

	// True while this service is itself moving SC through PrincipalBalanceService — lets the BalanceChanged →
	// TryAutoWithdraw hook (SF.1) skip its own re-entrant balance changes.
	public bool IsPerformingTransfer => _inTransfer;

	// ---- Setters (validate, normalize, persist, event) ----------------------------------------------------------

	// Auto-Deposit validation (D-SF3.2): enabling / setting the amount requires a positive chunk the bank can
	// cover (0 < amount ≤ BankAccountBalance); refuse to enable while the bank is empty (nothing to stream).
	public bool SetAutoDepositEnabled(bool enabled)
	{
		if (enabled && !IsAutoDepositAmountValid(AutoDepositAmount))
			return false;
		AutoDepositEnabled = enabled;
		SaveState();
		BankStateChanged?.Invoke();
		return true;
	}

	public bool SetAutoDepositAmount(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (!IsAutoDepositAmountValid(amount))
			return false;
		AutoDepositAmount = amount;
		SaveState();
		BankStateChanged?.Invoke();
		return true;
	}

	// Set-time sanity check the UI enforces first. TryAutoDeposit still safely partial-draws if the balance
	// later drops below this amount at runtime.
	public bool IsAutoDepositAmountValid(decimal amount)
		=> amount > 0m && amount <= BankAccountBalance;

	public void SetAutoWithdrawSettings(bool enabled, decimal threshold, decimal amount)
	{
		threshold = Money.Normalize(Math.Max(0m, threshold));
		amount    = Money.Normalize(amount);
		if (amount <= 0m) amount = DefaultAutoWithdrawAmount;

		AutoWithdrawEnabled   = enabled;
		AutoWithdrawThreshold = threshold;
		AutoWithdrawAmount    = amount;
		SaveState();
		BankStateChanged?.Invoke();
	}

	// ---- Checkpoint + pre-genesis (mandatory — CLAUDE.md Important Pattern 2) ------------------------------------

	// The block-checkpoint DTO for this service (bundled into BlockSessionCheckpointService.Snapshot as one field,
	// per the CG.3 "bundle when the flat list gets unwieldy" note — a brand-new service is the moment to start).
	public sealed class CheckpointState
	{
		public decimal BankAccountBalance    { get; set; }
		public bool    AutoDepositEnabled    { get; set; }
		public decimal AutoDepositAmount     { get; set; }
		public bool    AutoWithdrawEnabled   { get; set; }
		public decimal AutoWithdrawThreshold { get; set; }
		public decimal AutoWithdrawAmount    { get; set; }
		public decimal TotalDepositedToCasino   { get; set; }
		public decimal TotalWithdrawnFromCasino { get; set; }
		public List<BankTransferRecord> BankTransferHistory { get; set; } = new();
	}

	// Called by BlockSessionCheckpointService.CaptureCheckpoint() at each mined block (block = the only commit).
	public CheckpointState CaptureCheckpointState() => new CheckpointState
	{
		BankAccountBalance       = BankAccountBalance,
		AutoDepositEnabled       = AutoDepositEnabled,
		AutoDepositAmount        = AutoDepositAmount,
		AutoWithdrawEnabled      = AutoWithdrawEnabled,
		AutoWithdrawThreshold    = AutoWithdrawThreshold,
		AutoWithdrawAmount       = AutoWithdrawAmount,
		TotalDepositedToCasino   = TotalDepositedToCasino,
		TotalWithdrawnFromCasino = TotalWithdrawnFromCasino,
		BankTransferHistory      = _bankTransferHistory.Select(CloneRecord).ToList()
	};

	// Called by BlockSessionCheckpointService.ApplyCheckpointToServices() on restart. A null DTO means a legacy
	// checkpoint captured before Step 12 existed — keep whatever LoadState() loaded (no migration, D-SF2.8).
	public void RestoreFromCheckpoint(CheckpointState state)
	{
		if (state == null)
		{
			GD.Print("[PlayerBankAccountService] RestoreFromCheckpoint: skipped (legacy checkpoint — keeping loaded state)");
			return;
		}

		BankAccountBalance    = Money.Normalize(Math.Max(0m, state.BankAccountBalance));
		AutoDepositEnabled    = state.AutoDepositEnabled;
		AutoDepositAmount     = state.AutoDepositAmount > 0m ? Money.Normalize(state.AutoDepositAmount) : DefaultAutoDepositAmount;
		AutoWithdrawEnabled   = state.AutoWithdrawEnabled;
		AutoWithdrawThreshold = Money.Normalize(Math.Max(0m, state.AutoWithdrawThreshold));
		AutoWithdrawAmount    = state.AutoWithdrawAmount > 0m ? Money.Normalize(state.AutoWithdrawAmount) : DefaultAutoWithdrawAmount;
		TotalDepositedToCasino   = Money.Normalize(Math.Max(0m, state.TotalDepositedToCasino));
		TotalWithdrawnFromCasino = Money.Normalize(Math.Max(0m, state.TotalWithdrawnFromCasino));

		_bankTransferHistory.Clear();
		foreach (var r in state.BankTransferHistory ?? new List<BankTransferRecord>())
		{
			if (r == null || r.Amount <= 0m) continue;
			_bankTransferHistory.Add(SanitizeRecord(r));
		}
		if (_bankTransferHistory.Count > MaxTransferHistory)
			_bankTransferHistory.RemoveRange(0, _bankTransferHistory.Count - MaxTransferHistory);

		SaveState();
		GD.Print($"[PlayerBankAccountService] RESTORED from checkpoint — Bank={BankAccountBalance:F8}  AutoDeposit={AutoDepositEnabled}  AutoWithdraw={AutoWithdrawEnabled}  history={_bankTransferHistory.Count}");
		BankStateChanged?.Invoke();
	}

	// Called by BlockSessionCheckpointService.ResetToPreGenesisDefaults() on every boot until the first real block
	// is mined. Forces the bank back to its true "first launch" state — empty balance, everything OFF, no history
	// (D-SF3.1) — mirroring the casino's pre-genesis reset (nothing is committed pre-genesis; block = only commit).
	public void ResetToPreGenesisDefaults()
	{
		BankAccountBalance    = InitialBankAccountBalance; // 0 — the 40,000 stays in Main (D-SF3.1)
		AutoDepositEnabled    = false;
		AutoDepositAmount     = DefaultAutoDepositAmount;
		AutoWithdrawEnabled   = false;
		AutoWithdrawThreshold = DefaultAutoWithdrawThreshold;
		AutoWithdrawAmount    = DefaultAutoWithdrawAmount;
		TotalDepositedToCasino   = 0m;
		TotalWithdrawnFromCasino = 0m;
		_bankTransferHistory.Clear();
		SaveState();
		BankStateChanged?.Invoke();
	}

	// ---- Persistence --------------------------------------------------------------------------------------------

	private sealed class Snapshot
	{
		public decimal  BankAccountBalance    { get; set; }
		public bool     AutoDepositEnabled    { get; set; }
		public decimal  AutoDepositAmount     { get; set; }
		public bool     AutoWithdrawEnabled   { get; set; }
		public decimal  AutoWithdrawThreshold { get; set; }
		public decimal  AutoWithdrawAmount    { get; set; }
		public decimal  TotalDepositedToCasino   { get; set; }
		public decimal  TotalWithdrawnFromCasino { get; set; }
		public List<BankTransferRecord> BankTransferHistory { get; set; } = new();
		public DateTime UpdatedAtUtc { get; set; }
	}

	private void LoadState()
	{
		if (!FileAccess.FileExists(StatePath))
		{
			InitializeDefaults();
			SaveState();
			return;
		}

		try
		{
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
			string json = file.GetAsText();
			Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
			if (snapshot == null)
			{
				InitializeDefaults();
				SaveState();
				return;
			}

			BankAccountBalance    = Money.Normalize(Math.Max(0m, snapshot.BankAccountBalance));
			AutoDepositEnabled    = snapshot.AutoDepositEnabled;
			AutoDepositAmount     = snapshot.AutoDepositAmount > 0m ? Money.Normalize(snapshot.AutoDepositAmount) : DefaultAutoDepositAmount;
			AutoWithdrawEnabled   = snapshot.AutoWithdrawEnabled;
			AutoWithdrawThreshold = Money.Normalize(Math.Max(0m, snapshot.AutoWithdrawThreshold));
			AutoWithdrawAmount    = snapshot.AutoWithdrawAmount > 0m ? Money.Normalize(snapshot.AutoWithdrawAmount) : DefaultAutoWithdrawAmount;
			TotalDepositedToCasino   = Money.Normalize(Math.Max(0m, snapshot.TotalDepositedToCasino));
			TotalWithdrawnFromCasino = Money.Normalize(Math.Max(0m, snapshot.TotalWithdrawnFromCasino));

			_bankTransferHistory.Clear();
			foreach (var r in snapshot.BankTransferHistory ?? new List<BankTransferRecord>())
			{
				if (r == null || r.Amount <= 0m) continue;
				_bankTransferHistory.Add(SanitizeRecord(r));
			}
			if (_bankTransferHistory.Count > MaxTransferHistory)
				_bankTransferHistory.RemoveRange(0, _bankTransferHistory.Count - MaxTransferHistory);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[PlayerBankAccountService] Load failed: {ex.Message}");
			InitializeDefaults();
			SaveState();
		}
	}

	private void InitializeDefaults()
	{
		BankAccountBalance    = InitialBankAccountBalance;
		AutoDepositEnabled    = false;
		AutoDepositAmount     = DefaultAutoDepositAmount;
		AutoWithdrawEnabled   = false;
		AutoWithdrawThreshold = DefaultAutoWithdrawThreshold;
		AutoWithdrawAmount    = DefaultAutoWithdrawAmount;
		TotalDepositedToCasino   = 0m;
		TotalWithdrawnFromCasino = 0m;
		_bankTransferHistory.Clear();
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot
			{
				BankAccountBalance    = BankAccountBalance,
				AutoDepositEnabled    = AutoDepositEnabled,
				AutoDepositAmount     = AutoDepositAmount,
				AutoWithdrawEnabled   = AutoWithdrawEnabled,
				AutoWithdrawThreshold = AutoWithdrawThreshold,
				AutoWithdrawAmount    = AutoWithdrawAmount,
				TotalDepositedToCasino   = TotalDepositedToCasino,
				TotalWithdrawnFromCasino = TotalWithdrawnFromCasino,
				BankTransferHistory   = _bankTransferHistory.Select(CloneRecord).ToList(),
				UpdatedAtUtc          = DateTime.UtcNow
			};
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[PlayerBankAccountService] Save failed: {ex.Message}");
		}
	}

	// A clean copy with the Local kind explicitly re-stamped (JSON round-trips DateTimeKind as Unspecified).
	private static BankTransferRecord CloneRecord(BankTransferRecord r) => new BankTransferRecord
	{
		Amount        = r.Amount,
		Direction     = r.Direction,
		Method        = r.Method,
		GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
	};

	private static BankTransferRecord SanitizeRecord(BankTransferRecord r) => new BankTransferRecord
	{
		Amount        = Money.Normalize(r.Amount),
		Direction     = string.IsNullOrEmpty(r.Direction) ? DirectionMainToBank : r.Direction,
		Method        = string.IsNullOrEmpty(r.Method) ? MethodManual : r.Method,
		GameDateLocal = r.GameDateLocal.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
			: r.GameDateLocal
	};
}
