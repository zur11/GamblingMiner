using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using Scripts.Finance;

public partial class BankrollProgramService : Node
{
	public const decimal DefaultAutoRechargeAmount = 100.00000000m;
	public const decimal InitialPrincipalBalanceBaseline = 40000.00000000m;
	private const string StatePath = "user://bankroll_program_state.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private sealed class Snapshot
	{
		public decimal AutoRechargeAmount { get; set; }
		// Nullable so a legacy state file (written before SF.1.2 added the toggle) deserializes to null and
		// defaults to ON — never OFF (which would silently disable the always-on recharge on upgrade).
		public bool? AutoRechargeEnabled { get; set; }
		public List<TransferRecord> Records { get; set; } = new();
	}

	public sealed class TransferRecord
	{
		public DateTime UtcTimestamp { get; set; }
		public decimal Amount { get; set; }
		public string Direction { get; set; } = string.Empty; // balance_to_bankroll | bankroll_to_balance
		public string Reason { get; set; } = string.Empty;
	}

	private readonly List<TransferRecord> _records = new();
	public IReadOnlyList<TransferRecord> Records => _records;
	public decimal AutoRechargeAmount { get; private set; } = DefaultAutoRechargeAmount;
	// D-SF.4: the off-switch for the (formerly always-on) Bankroll dose recharge. Default ON = today's behavior.
	// When OFF, SimulationService/DiceGame skip the auto top-up on InsufficientBalance and let the session stop,
	// waiting for a manual Bankroll recharge. UI toggle lives in BankrollProgrammer (SF.2.8). Snapshotted at each
	// block, reverted to ON pre-genesis (a custom setting sticks only once a real block commits it).
	public bool AutoRechargeEnabled { get; private set; } = true;
	public int AutoRechargeCount => _records.Count(r => r.Direction == "balance_to_bankroll" && r.Reason == "auto_recharge");

	public event Action TransfersChanged;
	public event Action AutoRechargeAmountChanged;

	private CasinoClientLedgerService _ledger;
	private UserStatsService _userStats;
	private CalendarTimeService _calendarTime;

	public override void _Ready()
	{
		LoadState();
		_ledger       = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		_userStats    = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
	}

	// Every persisted/displayed event timestamp in this service must be GAME time, never real wall-clock —
	// see CLAUDE.md's "canonical rule" note under Important Pattern 2. Falls back to DateTime.UtcNow only if
	// the autoload is somehow unavailable (defensive null-safety, not an intended code path).
	private DateTime GameUtcNow() => _calendarTime?.CurrentUtcDateTime ?? DateTime.UtcNow;

	public void SetAutoRechargeAmount(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m)
		{
			return;
		}

		AutoRechargeAmount = amount;
		AutoRechargeAmountChanged?.Invoke();
		SaveState();
	}

	// D-SF.4 off-switch. Reuses AutoRechargeAmountChanged as the "recharge settings changed" notification the
	// BankrollProgrammer UI already listens to.
	public void SetAutoRechargeEnabled(bool enabled)
	{
		AutoRechargeEnabled = enabled;
		AutoRechargeAmountChanged?.Invoke();
		SaveState();
	}

	public bool TryTransferBalanceToBankroll(PrincipalBalanceService principal, Scripts.Finance.Wallet bankrollWallet, decimal amount, string reason)
	{
		amount = Money.Normalize(amount);
		if (principal == null || bankrollWallet == null || amount <= 0m)
		{
			return false;
		}

		if (!principal.TryWithdraw(amount))
		{
			return false;
		}

		bankrollWallet.ApplyTransaction(new Transaction(TransactionType.Deposit, TransactionSource.External, null, amount));
		AddRecord(amount, "balance_to_bankroll", reason);

		decimal wageredSnapshot = _userStats?.Stats?.TotalAmountWagered ?? 0m;
		decimal profitSnapshot  = _userStats?.Stats?.TotalProfit ?? 0m;

		// Internal recharges (auto or startup init) are NOT player-initiated deposits.
		// "deposit" is reserved for future explicit player transfers via the SC wallet screen.
		bool isInternalRecharge = string.Equals(reason, "auto_recharge", StringComparison.Ordinal)
		                       || string.Equals(reason, "startup_default", StringComparison.Ordinal)
		                       || string.Equals(reason, "manual_recharge", StringComparison.Ordinal);
		if (isInternalRecharge)
			_ledger?.RegisterAutoRecharge("player", amount, GameUtcNow(), wageredSnapshot, profitSnapshot);
		else
			_ledger?.RegisterDeposit("player", amount, GameUtcNow(), wageredSnapshot, profitSnapshot);

		return true;
	}

	public bool TryTransferBankrollToBalance(PrincipalBalanceService principal, Scripts.Finance.Wallet bankrollWallet, decimal amount, string reason)
	{
		amount = Money.Normalize(amount);
		if (principal == null || bankrollWallet == null || amount <= 0m || amount > bankrollWallet.Balance)
		{
			return false;
		}

		bankrollWallet.ApplyTransaction(new Transaction(TransactionType.Withdrawal, TransactionSource.External, null, amount));
		principal.Deposit(amount);
		AddRecord(amount, "bankroll_to_balance", reason);
		// §3.7 taxonomy fix: Bankroll → Main is an INTERNAL movement, not an SC Withdrawal (that term is now
		// reserved for Main → Private Bank Account, SC leaving the casino). Register it as "bankroll_withdrawal"
		// so it is excluded from the casino's "Total SC withdrawn" the way "auto_recharge" is excluded from deposits.
		_ledger?.RegisterBankrollWithdrawal("player", amount, GameUtcNow());
		return true;
	}

	public decimal GetPerformancePercentVsInitial(decimal currentPrincipalBalance)
	{
		decimal diff = currentPrincipalBalance - InitialPrincipalBalanceBaseline;
		return Money.Normalize((diff / InitialPrincipalBalanceBaseline) * 100m);
	}

	public (int Day, int Week, int Month) GetAutoRechargeCounts(DateTime utcNow)
	{
		DateTime dayStart = utcNow.Date;
		DateTime weekStart = dayStart.AddDays(-(((int)dayStart.DayOfWeek + 6) % 7));
		DateTime monthStart = new DateTime(dayStart.Year, dayStart.Month, 1, 0, 0, 0, DateTimeKind.Utc);

		int day = CountAutoRechargesSince(dayStart);
		int week = CountAutoRechargesSince(weekStart);
		int month = CountAutoRechargesSince(monthStart);
		return (day, week, month);
	}

	private int CountAutoRechargesSince(DateTime utcFrom) =>
		_records.Count(r =>
			r.Direction == "balance_to_bankroll" &&
			r.Reason == "auto_recharge" &&
			r.UtcTimestamp >= utcFrom);

	private void AddRecord(decimal amount, string direction, string reason)
	{
		_records.Add(new TransferRecord
		{
			UtcTimestamp = GameUtcNow(),
			Amount = amount,
			Direction = direction,
			Reason = reason
		});
		SaveState();
		TransfersChanged?.Invoke();
	}

	// autoRechargeEnabled: null (default) leaves the current toggle untouched — used by in-session node-state
	// restores (DiceGame) that don't track it. The block checkpoint restore passes the stored value; the
	// pre-genesis reset passes true. This keeps the toggle out of per-node snapshots yet checkpoint-covered.
	public void ReplaceState(decimal autoRechargeAmount, IEnumerable<TransferRecord> records, bool? autoRechargeEnabled = null)
	{
		AutoRechargeAmount = autoRechargeAmount > 0m
			? Money.Normalize(autoRechargeAmount)
			: DefaultAutoRechargeAmount;

		if (autoRechargeEnabled.HasValue)
			AutoRechargeEnabled = autoRechargeEnabled.Value;

		_records.Clear();
		if (records != null)
		{
			foreach (TransferRecord r in records)
			{
				if (r == null || r.Amount <= 0m)
				{
					continue;
				}

				_records.Add(new TransferRecord
				{
					UtcTimestamp = DateTime.SpecifyKind(r.UtcTimestamp, DateTimeKind.Utc),
					Amount = Money.Normalize(r.Amount),
					Direction = r.Direction ?? string.Empty,
					Reason = r.Reason ?? string.Empty
				});
			}
		}

		SaveState();
		AutoRechargeAmountChanged?.Invoke();
		TransfersChanged?.Invoke();
	}

	private void LoadState()
	{
		if (!FileAccess.FileExists(StatePath))
		{
			return;
		}

		try
		{
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
			string json = file.GetAsText();
			Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
			if (snapshot == null)
			{
				return;
			}

			AutoRechargeAmount = snapshot.AutoRechargeAmount > 0m
				? Money.Normalize(snapshot.AutoRechargeAmount)
				: DefaultAutoRechargeAmount;
			AutoRechargeEnabled = snapshot.AutoRechargeEnabled ?? true; // legacy file (null) → ON

			_records.Clear();
			foreach (TransferRecord record in snapshot.Records ?? new List<TransferRecord>())
			{
				if (record == null || record.Amount <= 0m)
				{
					continue;
				}

				_records.Add(new TransferRecord
				{
					UtcTimestamp = DateTime.SpecifyKind(record.UtcTimestamp, DateTimeKind.Utc),
					Amount = Money.Normalize(record.Amount),
					Direction = record.Direction ?? string.Empty,
					Reason = record.Reason ?? string.Empty
				});
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[BankrollProgramService] Load failed: {ex.Message}");
		}
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot
			{
				AutoRechargeAmount = AutoRechargeAmount,
				AutoRechargeEnabled = AutoRechargeEnabled,
				Records = _records
					.Select(r => new TransferRecord
					{
						UtcTimestamp = DateTime.SpecifyKind(r.UtcTimestamp, DateTimeKind.Utc),
						Amount = Money.Normalize(r.Amount),
						Direction = r.Direction,
						Reason = r.Reason
					})
					.ToList()
			};

			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[BankrollProgramService] Save failed: {ex.Message}");
		}
	}
}
