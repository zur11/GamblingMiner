using Godot;
using System;
using System.Text.Json;
using Scripts.Finance;

public partial class BankrollStateService : Node
{
	private const decimal DefaultInitialBalance = 0m;
	private const string StatePath = "user://bankroll_state.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	private sealed class Snapshot
	{
		public decimal CurrentBalance { get; set; }
		public DateTime UpdatedAtUtc { get; set; }
	}
	private bool _initialized;

	// ── Mini-plan 08 P1 — the per-bet disk write, throttled ────────────────────────────────────────────
	// MEASURED: SetBalance was called once per bet and saved unconditionally, costing **933.9 µs of a
	// 1,414 µs bet — 66% of every bet in the game** (four 5,000-bet windows, 2026-08-30). It was the single
	// largest cost in the engine by a factor of two.
	//
	// Its output was also never read. BlockSessionCheckpointService's own comment states the rule: a block
	// is the only commit, so every boot either restores this service from the checkpoint or resets it to
	// pre-genesis defaults, "discarding whatever ... BankrollStateService['s] own self-persisted files
	// accumulated between restarts". Nothing reads bankroll_state.json back within a session either — the
	// autoload holds the live value. **We were spending ~390 ms of every real second writing a file whose
	// content is thrown away when it is loaded.**
	//
	// The shape is copied verbatim from CasinoScBalanceService, which already solved this on the same code
	// path for the same reason (ND.8f): mark dirty, flush from _Process on a REAL-time interval. Real time,
	// not bets and not game time — game time is a quantity the player accelerates by 90×, and a budget
	// denominated in it accelerates with it (the standing rule from §38.7 and the [CasinoSC] trace).
	//
	// Durability is UNCHANGED-to-better. It was never a durability mechanism (see above), and the old write
	// was a truncate-and-stream `ModeFlags.Write` — the non-atomic shape Important Pattern 2 forbids since
	// INC-001 — executed hundreds of times a second. Writing ~2×/second shrinks that exposure window by the
	// same factor it shrinks the cost. Making it atomic is a separate, still-open question, deliberately
	// not bundled into a performance fix.
	private bool _saveDirty;
	private double _saveFlushTimer;
	private const double SaveFlushInterval = 0.5;

	public decimal CurrentBalance { get; private set; } = DefaultInitialBalance;

	public override void _Ready()
	{
		LoadState();
	}

	public override void _Process(double delta)
	{
		if (!_saveDirty) return;
		_saveFlushTimer += delta;
		if (_saveFlushTimer < SaveFlushInterval) return;
		_saveFlushTimer = 0;
		_saveDirty = false;
		SaveState();
	}

	// Godot delivers this before the process exits, so a quit inside the flush window still lands the last
	// value. Without it the throttle would trade a cost nobody wanted for a loss somebody would notice.
	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest || what == NotificationPredelete)
		{
			FlushPendingSave();
		}
	}

	/// <summary>
	/// Write now if anything is pending. Called on quit, and available to any caller that needs the file on
	/// disk at a specific instant rather than within the flush interval.
	/// </summary>
	public void FlushPendingSave()
	{
		if (!_saveDirty) return;
		_saveDirty = false;
		_saveFlushTimer = 0;
		SaveState();
	}

	public void EnsureInitialized(decimal fallbackInitialBalance = DefaultInitialBalance)
	{
		if (_initialized)
		{
			return;
		}

		CurrentBalance = fallbackInitialBalance > 0m ? Money.Normalize(fallbackInitialBalance) : DefaultInitialBalance;
		_initialized = true;
		// Saves IMMEDIATELY, unlike SetBalance: this runs once, at first initialization, and is not on any
		// hot path. Only the per-bet caller needed throttling, and throttling a rare call buys nothing while
		// widening the window in which the file disagrees with memory.
		SaveState();
	}

	/// <summary>
	/// The bankroll balance. Called ONCE PER BET by SimulationService, which is why the save is throttled —
	/// see the _saveDirty note above for the measurement that forced it. Callers needing the file on disk at
	/// a precise instant call <see cref="FlushPendingSave"/>.
	/// </summary>
	public void SetBalance(decimal balance)
	{
		CurrentBalance = Money.Normalize(Math.Max(0m, balance));
		_initialized = true;
		_saveDirty = true;
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

			CurrentBalance = Money.Normalize(Math.Max(0m, snapshot.CurrentBalance));
			_initialized = true;
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[BankrollStateService] Load failed: {ex.Message}");
		}
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot
			{
				CurrentBalance = CurrentBalance,
				UpdatedAtUtc = DateTime.UtcNow
			};
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[BankrollStateService] Save failed: {ex.Message}");
		}
	}
}
