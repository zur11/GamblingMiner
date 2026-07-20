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

	// ND.8f: the five canonical casino clients — the same set as the ND.8c genesis grants. Each carries one
	// "initial" 40,000 entry (see EnsureCanonicalInitialDeposits) and its own row in the per-client scenes.
	public static readonly (string Id, string Display)[] CanonicalClients =
	{
		("player", "Player"),
		("bot_1", "Bot 1"),
		("bot_2", "Bot 2"),
		("bot_3", "Bot 3"),
		("bot_4", "Bot 4"),
	};

	// ND.8f — the casino-side per-client betting book (OQ-11.1 resolved): cumulative bets/wins/losses/
	// wagered/net-profit per client. The stats source for the NON-player clients (the player's row keeps
	// reading UserStatsService); NetProfit is the CLIENT's own net profit, so casino P/L = −NetProfit.
	// Deliberately NOT stored on NodeFinancialState — DiceGame.SaveActiveNodeFinancialState rebuilds that
	// DTO from the shared services on every save, which would clobber any stats fields.
	public sealed class ClientBetStats
	{
		public int     TotalBets    { get; set; }
		public int     TotalWins    { get; set; }
		public int     TotalLosses  { get; set; }
		public decimal TotalWagered { get; set; }
		public decimal NetProfit    { get; set; }
	}

	private const string StatePath = "user://casino_client_ledger.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private sealed class Snapshot
	{
		public List<LedgerEntry> Entries { get; set; } = new();
		public Dictionary<string, ClientBetStats> BetStats { get; set; } = new();
	}

	private readonly List<LedgerEntry> _entries = new();
	public IReadOnlyList<LedgerEntry> Entries => _entries;

	private readonly Dictionary<string, ClientBetStats> _betStats = new();

	// ND.8f: bet-stats updates arrive many times per second with bots running — persisting per bet would
	// thrash the disk for nothing (a restart restores from the block checkpoint regardless; block = the
	// only commit). Dirty-flag flush instead; everything else keeps its immediate SaveState.
	private bool _statsSaveDirty;
	private double _statsSaveFlushTimer;
	private const double StatsSaveFlushInterval = 1.0;

	public event Action LedgerChanged;

	private CalendarTimeService _calendarTime;

	public override void _Ready()
	{
		LoadState();
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		EnsureCanonicalInitialDeposits();
	}

	public override void _Process(double delta)
	{
		if (!_statsSaveDirty) return;
		_statsSaveFlushTimer += delta;
		if (_statsSaveFlushTimer < StatsSaveFlushInterval) return;
		_statsSaveFlushTimer = 0;
		_statsSaveDirty = false;
		SaveState();
	}

	// ND.8f: every canonical client (player + bot_1..4) carries exactly one "initial" 40,000 entry — the
	// ledger mirror of its ND.8c genesis grant. Idempotent; called at boot, after a checkpoint restore (a
	// legacy checkpoint's entry list would otherwise wipe the bots' migration entries), and by the
	// pre-genesis reset. A mid-world migration timestamps the bots at the current game time (honest
	// "enrolled now" semantics — their play only starts being accounted from this build onward).
	private void EnsureCanonicalInitialDeposits()
	{
		foreach ((string id, string _) in CanonicalClients)
		{
			if (_entries.Any(e => e.ClientId == id && e.Kind == "initial")) continue;
			RegisterInitialDeposit(id, 40000m, _calendarTime?.CurrentUtcDateTime ?? DateTime.UtcNow, 0m, 0m);
		}
	}

	// ND.8f — one settled bet accrued into the per-client book. In-memory + throttled flush (see _Process);
	// no LedgerChanged (the per-client scenes poll on a 2 s timer — firing per bot bet would churn the UI).
	public void RegisterSettledBet(string clientId, decimal betAmount, decimal creditedProfit, bool isWin)
	{
		if (string.IsNullOrEmpty(clientId)) return;
		if (!_betStats.TryGetValue(clientId, out ClientBetStats stats))
		{
			stats = new ClientBetStats();
			_betStats[clientId] = stats;
		}
		stats.TotalBets++;
		if (isWin) stats.TotalWins++;
		else stats.TotalLosses++;
		stats.TotalWagered = Money.Normalize(stats.TotalWagered + Math.Abs(betAmount));
		stats.NetProfit    = Money.Normalize(stats.NetProfit + creditedProfit);
		_statsSaveDirty = true;
	}

	// Null when the client has no accrued bets yet. Read-only use by the scenes/recharge snapshots.
	public ClientBetStats GetBetStats(string clientId)
		=> _betStats.TryGetValue(clientId, out ClientBetStats stats) ? stats : null;

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

	// ND.8f: the per-client bet-stats book joins the checkpoint surface beside the entries list.
	public Dictionary<string, ClientBetStats> CaptureBetStatsForCheckpoint()
		=> _betStats.ToDictionary(kv => kv.Key, kv => CloneStats(kv.Value));

	// Restore from a block checkpoint. Null entries (legacy pre-SF.1.5 checkpoint) → keep whatever
	// LoadState() loaded. Null betStats (legacy pre-ND.8f checkpoint) → keep the loaded book (no better
	// source exists). After an entries restore the canonical "initial" deposits are re-ensured — a legacy
	// checkpoint's list predates the bots' migration entries and would otherwise silently wipe them.
	public void RestoreFromCheckpoint(List<LedgerEntry> entries, Dictionary<string, ClientBetStats> betStats)
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

		if (betStats != null)
		{
			_betStats.Clear();
			foreach (var kv in betStats)
			{
				if (kv.Value == null || string.IsNullOrEmpty(kv.Key)) continue;
				_betStats[kv.Key] = CloneStats(kv.Value);
			}
		}

		EnsureCanonicalInitialDeposits();
		SaveState();
		LedgerChanged?.Invoke();
	}

	// Pre-genesis reset: discard every canonical client's entries accumulated between restarts, zero the
	// bet-stats book, and re-establish the five clean "initial" starting-stake deposits (D-SF3.4 semantics,
	// extended to all clients at ND.8f). Called from BlockSessionCheckpointService.ResetToPreGenesisDefaults()
	// on every boot until the first real block.
	public void ResetToPreGenesisDefaults()
	{
		foreach ((string id, string _) in CanonicalClients)
			_entries.RemoveAll(e => e.ClientId == id);
		_betStats.Clear();
		EnsureCanonicalInitialDeposits();
		SaveState();
		LedgerChanged?.Invoke();
	}

	private static ClientBetStats CloneStats(ClientBetStats s) => new ClientBetStats
	{
		TotalBets    = Math.Max(0, s.TotalBets),
		TotalWins    = Math.Max(0, s.TotalWins),
		TotalLosses  = Math.Max(0, s.TotalLosses),
		TotalWagered = Money.Normalize(Math.Max(0m, s.TotalWagered)),
		NetProfit    = Money.Normalize(s.NetProfit)
	};

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
			if (snapshot == null) return;

			foreach (var kv in snapshot.BetStats ?? new Dictionary<string, ClientBetStats>())
			{
				if (kv.Value == null || string.IsNullOrEmpty(kv.Key)) continue;
				_betStats[kv.Key] = CloneStats(kv.Value);
			}

			if (snapshot.Entries == null) return;
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
			var snapshot = new Snapshot
			{
				Entries  = new List<LedgerEntry>(_entries),
				BetStats = _betStats.ToDictionary(kv => kv.Key, kv => CloneStats(kv.Value))
			};
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CasinoClientLedgerService] Save failed: {ex.Message}");
		}
	}
}
