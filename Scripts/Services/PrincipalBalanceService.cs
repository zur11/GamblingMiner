using Godot;
using System;
using System.Text.Json;
using Scripts.Finance;

public partial class PrincipalBalanceService : Node
{
	private const decimal DefaultInitialBalance = 40000.00000000m;
	private const string StatePath = "user://principal_balance_state.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
	private sealed class Snapshot
	{
		public decimal CurrentBalance { get; set; }
		public DateTime UpdatedAtUtc { get; set; }
	}
	private bool _initialized;

	public decimal CurrentBalance { get; private set; } = DefaultInitialBalance;

	// Fired on every Main Balance mutation (D-SF.7). Consumers: ScFinances labels, PlayerBankAccountService's
	// auto-withdraw hook, StatusBar, and future scenes. Invoked AFTER the balance is updated and persisted.
	public event Action BalanceChanged;

	public override void _Ready()
	{
		LoadState();
	}

	public void EnsureInitialized(decimal fallbackInitialBalance = DefaultInitialBalance)
	{
		if (_initialized)
		{
			return;
		}

		CurrentBalance = fallbackInitialBalance >= 0m ? Money.Normalize(fallbackInitialBalance) : DefaultInitialBalance;
		_initialized = true;
		SaveState();
	}

	public bool TryWithdraw(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || amount > CurrentBalance)
		{
			return false;
		}

		CurrentBalance = Money.Normalize(CurrentBalance - amount);
		_initialized = true;
		SaveState();
		BalanceChanged?.Invoke();
		return true;
	}

	public void Deposit(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m)
		{
			return;
		}

		CurrentBalance = Money.Normalize(CurrentBalance + amount);
		_initialized = true;
		SaveState();
		BalanceChanged?.Invoke();
	}

	public void SetBalance(decimal amount)
	{
		CurrentBalance = Money.Normalize(Math.Max(0m, amount));
		_initialized = true;
		SaveState();
		BalanceChanged?.Invoke();
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
			GD.PushWarning($"[PrincipalBalanceService] Load failed: {ex.Message}");
		}
	}

	private void SaveState()
	{
		// Mini-plan 15 A — a session that could not load its world writes nothing world-shaped.
		if (WorldWriteGuard.RefuseWrite(nameof(PrincipalBalanceService))) return;

		try
		{
			var snapshot = new Snapshot
			{
				CurrentBalance = CurrentBalance,
				UpdatedAtUtc = DateTime.UtcNow
			};
			// Mini-plan 16 A — every world-state write is counted. The timer starts BEFORE the open and the
			// report happens AFTER the close, because on Windows the close and its metadata flush are most of a
			// small file's cost: timing only the StoreString would measure the cheap part and conclude the
			// write is free. Hence the block-scoped `using` — a statement-scoped one closes at method exit,
			// outside the measurement. Same shape in all 15 writers.
			string payload = JsonSerializer.Serialize(snapshot, JsonOptions);
			long writeBegin = System.Diagnostics.Stopwatch.GetTimestamp();
			using (FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write))
			{
				file.StoreString(payload);
			}
			Scripts.Diagnostics.BlockCostProfiler.NoteStateWrite(StatePath, payload.Length, writeBegin);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[PrincipalBalanceService] Save failed: {ex.Message}");
		}
	}
}
