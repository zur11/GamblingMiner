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
	}

	public void SetBalance(decimal amount)
	{
		CurrentBalance = Money.Normalize(Math.Max(0m, amount));
		_initialized = true;
		SaveState();
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
			GD.PushWarning($"[PrincipalBalanceService] Save failed: {ex.Message}");
		}
	}
}
