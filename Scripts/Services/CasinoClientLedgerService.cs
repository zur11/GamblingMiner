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
		public string   Kind                 { get; set; } = string.Empty; // initial | deposit | withdrawal | auto_recharge | bankroll_withdrawal | swap_sc_out | swap_sc_in
		// SF.1.5 / D-SF2.3: distinguishes automatic from player-initiated flows WITHOUT new kinds (every existing
		// Kind== filter keeps working). Absent/legacy → "manual".
		public string   Method               { get; set; } = "manual"; // manual | auto
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
		AddEntry(clientId, amount, "initial", "manual", utc, totalWageredSnapshot, netProfitSnapshot);
	}

	// SF.1.5: the player's later Bank → Main deposits register kind "deposit" (never "initial") with the real
	// method — auto-deposits reset the since-last-deposit baseline exactly like manual ones (D-SF2.2).
	public void RegisterDeposit(string clientId, decimal amount, DateTime utc,
		decimal totalWageredSnapshot, decimal netProfitSnapshot, string method = "manual")
	{
		AddEntry(clientId, amount, "deposit", method, utc, totalWageredSnapshot, netProfitSnapshot);
	}

	// SF.1.5: Main → Private Bank Account — SC genuinely leaving the casino. The "withdrawal" kind now means
	// exactly this (the internal Bankroll → Main movement moved to "bankroll_withdrawal", below).
	public void RegisterWithdrawal(string clientId, decimal amount, DateTime utc, string method = "manual")
	{
		AddEntry(clientId, amount, "withdrawal", method, utc, 0m, 0m);
	}

	// §3.7 taxonomy fix: the INTERNAL Bankroll → Main movement. Not a client↔casino boundary flow, so it is
	// excluded from "Total SC withdrawn" (the ClientsTransactions filter keys on "withdrawal") and hidden from
	// the client transaction list, the way "auto_recharge" is excluded from deposits.
	public void RegisterBankrollWithdrawal(string clientId, decimal amount, DateTime utc)
	{
		AddEntry(clientId, amount, "bankroll_withdrawal", "auto", utc, 0m, 0m);
	}

	// Step 13 (SW.3/SW.4, D-SW.4) — swap-desk SC flows, from the casino's operational perspective:
	// swap_sc_out = the client's SC paid INTO the casino buying BTC (Panel A); swap_sc_in = SC credited to
	// the client selling BTC (Panel B). Own kinds so they are excluded from the deposited/withdrawn totals
	// AND from the since-last-deposit baseline (GetLastDeposit filters initial|deposit) by construction.
	public void RegisterSwapScOut(string clientId, decimal amount, DateTime utc, string method = "manual")
	{
		AddEntry(clientId, amount, "swap_sc_out", method, utc, 0m, 0m);
	}

	public void RegisterSwapScIn(string clientId, decimal amount, DateTime utc, string method = "manual")
	{
		AddEntry(clientId, amount, "swap_sc_in", method, utc, 0m, 0m);
	}

	// auto_recharge and startup_default are both internal recharges, not player-initiated deposits.
	// TotalWageredSnapshot/NetProfitSnapshot are captured so ClientsBetsHistory can show
	// "P/L since last Bankroll Recharge" alongside the "since last deposit" metric.
	public void RegisterAutoRecharge(string clientId, decimal amount, DateTime utc,
		decimal totalWageredSnapshot, decimal netProfitSnapshot)
	{
		AddEntry(clientId, amount, "auto_recharge", "auto", utc, totalWageredSnapshot, netProfitSnapshot);
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

	// ---- Lifecycle (D-SF2.4): the ledger is a player-facing persisted list, so it must be checkpoint-covered
	// (snapshot at each block) and pre-genesis-cleared — the exact leak class the other services already fix.

	// Deep copy of every entry, for BlockSessionCheckpointService.CaptureCheckpoint (block = the only commit).
	public List<LedgerEntry> CaptureEntriesForCheckpoint() => _entries.Select(CloneEntry).ToList();

	// Restore from a block checkpoint. Null (legacy pre-SF.1.5 checkpoint) → keep whatever LoadState() loaded.
	public void RestoreFromCheckpoint(List<LedgerEntry> entries)
	{
		if (entries == null)
		{
			GD.Print("[CasinoClientLedgerService] RestoreFromCheckpoint: skipped (legacy checkpoint — keeping loaded entries)");
			return;
		}

		_entries.Clear();
		foreach (LedgerEntry e in entries)
		{
			if (e == null || string.IsNullOrEmpty(e.ClientId)) continue;
			_entries.Add(CloneEntry(e));
		}
		SaveState();
		LedgerChanged?.Invoke();
	}

	// Pre-genesis reset: discard every player entry accumulated between restarts and re-establish the single
	// clean "initial" starting-stake deposit (D-SF3.4 — reverts to today's meaning). Called from
	// BlockSessionCheckpointService.ResetToPreGenesisDefaults() on every boot until the first real block.
	public void ResetToPreGenesisDefaults()
	{
		_entries.RemoveAll(e => e.ClientId == "player");
		RegisterInitialDeposit("player", 40000m, _calendarTime?.CurrentUtcDateTime ?? DateTime.UtcNow, 0m, 0m);
		SaveState();
		LedgerChanged?.Invoke();
	}

	private static LedgerEntry CloneEntry(LedgerEntry e) => new LedgerEntry
	{
		ClientId             = e.ClientId,
		UtcTimestamp         = DateTime.SpecifyKind(e.UtcTimestamp, DateTimeKind.Utc),
		Amount               = Money.Normalize(Math.Max(0m, e.Amount)),
		Kind                 = e.Kind ?? string.Empty,
		Method               = string.IsNullOrEmpty(e.Method) ? "manual" : e.Method,
		TotalWageredSnapshot = Money.Normalize(Math.Max(0m, e.TotalWageredSnapshot)),
		NetProfitSnapshot    = Money.Normalize(e.NetProfitSnapshot)
	};

	private void AddEntry(string clientId, decimal amount, string kind, string method,
		DateTime utc, decimal wageredSnapshot, decimal profitSnapshot)
	{
		_entries.Add(new LedgerEntry
		{
			ClientId             = clientId,
			UtcTimestamp         = DateTime.SpecifyKind(utc, DateTimeKind.Utc),
			Amount               = Money.Normalize(Math.Abs(amount)),
			Kind                 = kind,
			Method               = string.IsNullOrEmpty(method) ? "manual" : method,
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
					Method               = string.IsNullOrEmpty(e.Method) ? "manual" : e.Method,
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
