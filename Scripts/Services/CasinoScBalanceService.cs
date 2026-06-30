using Godot;
using System;
using System.Text.Json;
using Scripts.Finance;

public partial class CasinoScBalanceService : Node
{
	public const decimal InitialLoanAmount  = 100_000_000.00000000m;
	public const decimal DefaultBankroll    =   1_000_000.00000000m;
	public const decimal DefaultMainBalance =  99_000_000.00000000m;

	private const string StatePath = "user://casino_sc_balance_state.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private sealed class Snapshot
	{
		public decimal  MainBalance    { get; set; }
		public decimal  Bankroll       { get; set; }
		public decimal  BankrollTarget { get; set; }
		public int      LoanCount      { get; set; }
		public decimal  TotalLoaned    { get; set; }
		public DateTime UpdatedAtUtc   { get; set; }
	}

	public decimal MainBalance    { get; private set; } = DefaultMainBalance;
	public decimal Bankroll       { get; private set; } = DefaultBankroll;
	public decimal TotalSc        => Money.Normalize(MainBalance + Bankroll);

	// Positive = casino ahead of all cumulative loans; negative = casino in debt.
	public decimal CumulativeProfitSinceLoan => Money.Normalize(TotalSc - TotalLoaned);

	public decimal BankrollTarget { get; private set; } = DefaultBankroll;
	public int     LoanCount      { get; private set; } = 1;
	public decimal TotalLoaned    { get; private set; } = InitialLoanAmount;

	public event Action BalanceChanged;

	public override void _Ready()
	{
		LoadState();
		GD.Print($"[CasinoScBalanceService] Ready — MainBalance={MainBalance:F8} SC  Bankroll={Bankroll:F8} SC  BankrollTarget={BankrollTarget:F8} SC  LoanCount={LoanCount}  TotalLoaned={TotalLoaned:F8} SC");
	}

	// Called by BlockSessionCheckpointService.ApplyCheckpointToServices() on restart.
	// Sets MainBalance and Bankroll directly to checkpoint values — bypasses auto-recharge, does not persist.
	public void RestoreCasinoScState(decimal main, decimal bankroll)
	{
		if (main < 0m && bankroll < 0m) return; // ignore invalid checkpoint (e.g. missing fields default to 0)
		MainBalance = Money.Normalize(Math.Max(0m, main));
		Bankroll    = Money.Normalize(Math.Max(0m, bankroll));
		BalanceChanged?.Invoke();
	}

	// Called by SimulationService after each settled player bet.
	// casinoDelta = −(player's creditedProfit): positive when player loses, negative when player wins.
	public void ApplyBetResult(decimal casinoDelta)
	{
		Bankroll = Money.Normalize(Bankroll + casinoDelta);
		if (Bankroll < BankrollTarget)
			TryAutoRecharge();
		Bankroll = Money.Normalize(Math.Max(0m, Bankroll));
		SaveState();
		BalanceChanged?.Invoke();
	}

	// Target-to-fill auto-recharge. If MainBalance is insufficient, injects a 100M SC bank loan first.
	// Always succeeds — the casino has an infinite credit line in Basic Mode.
	public void TryAutoRecharge()
	{
		decimal needed = BankrollTarget - Bankroll;
		if (needed <= 0m) return;

		if (MainBalance < needed)
		{
			MainBalance  = Money.Normalize(MainBalance + InitialLoanAmount);
			LoanCount++;
			TotalLoaned  = Money.Normalize(TotalLoaned + InitialLoanAmount);
			GD.Print($"[CasinoScBalanceService] Bank re-loan #{LoanCount} fired — TotalLoaned={TotalLoaned:F8} SC");
		}

		decimal transfer = Money.Normalize(Math.Min(needed, MainBalance));
		MainBalance = Money.Normalize(MainBalance - transfer);
		Bankroll    = Money.Normalize(Bankroll + transfer);
	}

	public bool TryTransferToBankroll(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || amount > MainBalance) return false;

		MainBalance = Money.Normalize(MainBalance - amount);
		Bankroll    = Money.Normalize(Bankroll + amount);
		SaveState();
		BalanceChanged?.Invoke();
		return true;
	}

	public bool TryTransferToMainBalance(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || amount > Bankroll) return false;

		Bankroll    = Money.Normalize(Bankroll - amount);
		MainBalance = Money.Normalize(MainBalance + amount);
		SaveState();
		BalanceChanged?.Invoke();
		return true;
	}

	public void SetBankrollTarget(decimal target)
	{
		target = Money.Normalize(target);
		if (target <= 0m) return;

		BankrollTarget = target;
		SaveState();
		BalanceChanged?.Invoke();
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

			MainBalance    = Money.Normalize(Math.Max(0m, snapshot.MainBalance));
			Bankroll       = Money.Normalize(Math.Max(0m, snapshot.Bankroll));
			BankrollTarget = snapshot.BankrollTarget > 0m ? Money.Normalize(snapshot.BankrollTarget) : DefaultBankroll;
			LoanCount      = snapshot.LoanCount > 0 ? snapshot.LoanCount : 1;
			TotalLoaned    = snapshot.TotalLoaned > 0m ? Money.Normalize(snapshot.TotalLoaned) : InitialLoanAmount;
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CasinoScBalanceService] Load failed: {ex.Message}");
			InitializeDefaults();
			SaveState();
		}
	}

	private void InitializeDefaults()
	{
		MainBalance    = DefaultMainBalance;
		Bankroll       = DefaultBankroll;
		BankrollTarget = DefaultBankroll;
		LoanCount      = 1;
		TotalLoaned    = InitialLoanAmount;
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot
			{
				MainBalance    = MainBalance,
				Bankroll       = Bankroll,
				BankrollTarget = BankrollTarget,
				LoanCount      = LoanCount,
				TotalLoaned    = TotalLoaned,
				UpdatedAtUtc   = DateTime.UtcNow
			};
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CasinoScBalanceService] Save failed: {ex.Message}");
		}
	}
}
