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
	public int AutoRechargeCount => _records.Count(r => r.Direction == "balance_to_bankroll" && r.Reason == "auto_recharge");

	public event Action TransfersChanged;
	public event Action AutoRechargeAmountChanged;

	private CasinoClientLedgerService _ledger;
	private UserStatsService _userStats;

	public override void _Ready()
	{
		LoadState();
		_ledger    = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		_userStats = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
	}

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
			_ledger?.RegisterAutoRecharge("player", amount, DateTime.UtcNow, wageredSnapshot, profitSnapshot);
		else
			_ledger?.RegisterDeposit("player", amount, DateTime.UtcNow, wageredSnapshot, profitSnapshot);

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
		_ledger?.RegisterWithdrawal("player", amount, DateTime.UtcNow);
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
			UtcTimestamp = DateTime.UtcNow,
			Amount = amount,
			Direction = direction,
			Reason = reason
		});
		SaveState();
		TransfersChanged?.Invoke();
	}

	public void ReplaceState(decimal autoRechargeAmount, IEnumerable<TransferRecord> records)
	{
		AutoRechargeAmount = autoRechargeAmount > 0m
			? Money.Normalize(autoRechargeAmount)
			: DefaultAutoRechargeAmount;

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
