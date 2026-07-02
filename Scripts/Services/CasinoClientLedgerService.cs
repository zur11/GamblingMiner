using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;

public partial class CasinoClientLedgerService : Node
{
	public sealed class LedgerEntry
	{
		public string   ClientId             { get; set; } = string.Empty;
		public DateTime UtcTimestamp         { get; set; }
		public decimal  Amount               { get; set; }
		public string   Kind                 { get; set; } = string.Empty; // initial | deposit | withdrawal | auto_recharge
		public decimal  TotalWageredSnapshot { get; set; }
		public decimal  NetProfitSnapshot    { get; set; }
	}

	private const string StatePath = "user://casino_client_ledger.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private sealed class Snapshot
	{
		public List<LedgerEntry> Entries { get; set; } = new();
	}

	private readonly List<LedgerEntry> _entries = new();
	public IReadOnlyList<LedgerEntry> Entries => _entries;

	public event Action LedgerChanged;

	private CalendarTimeService _calendarTime;

	public override void _Ready()
	{
		LoadState();
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

		if (!_entries.Any(e => e.ClientId == "player"))
			RegisterInitialDeposit("player", 40000m, _calendarTime?.CurrentUtcDateTime ?? DateTime.UtcNow, 0m, 0m);
	}

	public void RegisterInitialDeposit(string clientId, decimal amount, DateTime utc,
		decimal totalWageredSnapshot, decimal netProfitSnapshot)
	{
		AddEntry(clientId, amount, "initial", utc, totalWageredSnapshot, netProfitSnapshot);
	}

	public void RegisterDeposit(string clientId, decimal amount, DateTime utc,
		decimal totalWageredSnapshot, decimal netProfitSnapshot)
	{
		AddEntry(clientId, amount, "deposit", utc, totalWageredSnapshot, netProfitSnapshot);
	}

	public void RegisterWithdrawal(string clientId, decimal amount, DateTime utc)
	{
		AddEntry(clientId, amount, "withdrawal", utc, 0m, 0m);
	}

	// auto_recharge and startup_default are both internal recharges, not player-initiated deposits.
	// TotalWageredSnapshot/NetProfitSnapshot are captured so ClientsBetsHistory can show
	// "P/L since last Bankroll Recharge" alongside the "since last deposit" metric.
	public void RegisterAutoRecharge(string clientId, decimal amount, DateTime utc,
		decimal totalWageredSnapshot, decimal netProfitSnapshot)
	{
		AddEntry(clientId, amount, "auto_recharge", utc, totalWageredSnapshot, netProfitSnapshot);
	}

	// Returns most recent intentional deposit — auto_recharge/startup_default never reset the
	// since-last-deposit baseline (OQ-11.6 decision).
	public LedgerEntry GetLastDeposit(string clientId)
	{
		return _entries
			.Where(e => e.ClientId == clientId && (e.Kind == "initial" || e.Kind == "deposit"))
			.LastOrDefault();
	}

	// Returns most recent internal recharge entry (auto_recharge kind).
	// Used by ClientsBetsHistory for the "P/L since last Bankroll Recharge" metric.
	public LedgerEntry GetLastAutoRecharge(string clientId)
	{
		return _entries
			.Where(e => e.ClientId == clientId && e.Kind == "auto_recharge")
			.LastOrDefault();
	}

	public IReadOnlyList<LedgerEntry> GetEntriesForClient(string clientId)
	{
		return _entries.Where(e => e.ClientId == clientId).ToList();
	}

	private void AddEntry(string clientId, decimal amount, string kind,
		DateTime utc, decimal wageredSnapshot, decimal profitSnapshot)
	{
		_entries.Add(new LedgerEntry
		{
			ClientId             = clientId,
			UtcTimestamp         = DateTime.SpecifyKind(utc, DateTimeKind.Utc),
			Amount               = Money.Normalize(Math.Abs(amount)),
			Kind                 = kind,
			TotalWageredSnapshot = Money.Normalize(wageredSnapshot),
			NetProfitSnapshot    = Money.Normalize(profitSnapshot)
		});
		SaveState();
		LedgerChanged?.Invoke();
	}

	private void LoadState()
	{
		if (!FileAccess.FileExists(StatePath)) return;
		try
		{
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
			Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(file.GetAsText(), JsonOptions);
			if (snapshot?.Entries == null) return;
			foreach (LedgerEntry e in snapshot.Entries)
			{
				if (e == null || string.IsNullOrEmpty(e.ClientId)) continue;
				_entries.Add(new LedgerEntry
				{
					ClientId             = e.ClientId,
					UtcTimestamp         = DateTime.SpecifyKind(e.UtcTimestamp, DateTimeKind.Utc),
					Amount               = Money.Normalize(Math.Max(0m, e.Amount)),
					Kind                 = e.Kind ?? string.Empty,
					TotalWageredSnapshot = Money.Normalize(Math.Max(0m, e.TotalWageredSnapshot)),
					NetProfitSnapshot    = Money.Normalize(e.NetProfitSnapshot)
				});
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CasinoClientLedgerService] Load failed: {ex.Message}");
		}
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot { Entries = new List<LedgerEntry>(_entries) };
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CasinoClientLedgerService] Save failed: {ex.Message}");
		}
	}
}
