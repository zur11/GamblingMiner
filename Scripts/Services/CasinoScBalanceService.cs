using Godot;
using System;
using System.Text.Json;
using Scripts.Finance;

public partial class CasinoScBalanceService : Node
{
	public const decimal InitialLoanAmount  = 100_000_000.00000000m;
	public const decimal DefaultBankroll    =   1_000_000.00000000m;
	// Pre-genesis / pre-first-bet: the full, unsplit loan sits in Main Balance and nothing is in the
	// Bankroll yet (the casino self-funds lazily on the first settled bet — EnsureInitialCasinoFundingIfNeeded).
	public const decimal DefaultMainBalance = InitialLoanAmount;

	private const string StatePath = "user://casino_sc_balance_state.json";
	private int _betCount;
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
	public decimal Bankroll       { get; private set; } = 0m;
	public decimal TotalSc        => Money.Normalize(MainBalance + Bankroll);

	// Positive = casino ahead of all cumulative loans; negative = casino in debt.
	public decimal CumulativeProfitSinceLoan => Money.Normalize(TotalSc - TotalLoaned);

	public decimal BankrollTarget { get; private set; } = DefaultBankroll;
	public int     LoanCount      { get; private set; } = 0;
	public decimal TotalLoaned    { get; private set; } = 0m;

	public event Action BalanceChanged;

	public override void _Ready()
	{
		LoadState();
		GD.Print($"[CasinoScBalanceService] Ready — MainBalance={MainBalance:F8} SC  Bankroll={Bankroll:F8} SC  BankrollTarget={BankrollTarget:F8} SC  LoanCount={LoanCount}  TotalLoaned={TotalLoaned:F8} SC");
	}

	// Called by BlockSessionCheckpointService.ApplyCheckpointToServices() on restart.
	// Sets MainBalance and Bankroll directly to checkpoint values — bypasses auto-recharge, does not persist.
	// Both == 0 means the fields were absent from the JSON (old checkpoint before Phase 11.2) — skip restore.
	public void RestoreCasinoScState(decimal main, decimal bankroll, decimal bankrollTarget, int loanCount, decimal totalLoaned)
	{
		if (main == 0m && bankroll == 0m)
		{
			GD.Print("[CasinoSC] RestoreCasinoScState: skipped (no casino SC in checkpoint yet — using initialized defaults)");
			return;
		}
		MainBalance = Money.Normalize(Math.Max(0m, main));
		Bankroll    = Money.Normalize(Math.Max(0m, bankroll));
		// BankrollTarget/LoanCount/TotalLoaned were added to the checkpoint in Phase CG.0.6; older checkpoints
		// lack them and deserialize to 0, so keep the currently-loaded value rather than zeroing a funded
		// casino's target/loan bookkeeping. Post-first-block checkpoints always carry valid (>0) values.
		if (bankrollTarget > 0m) BankrollTarget = Money.Normalize(bankrollTarget);
		if (loanCount      > 0)  LoanCount      = loanCount;
		if (totalLoaned    > 0m) TotalLoaned    = Money.Normalize(totalLoaned);
		GD.Print($"[CasinoSC] RESTORED from checkpoint — Main={MainBalance:F8}  Bankroll={Bankroll:F8}  Target={BankrollTarget:F8}  LoanCount={LoanCount}  TotalLoaned={TotalLoaned:F8}  P/L={CumulativeProfitSinceLoan:+0.00;-0.00}");
		BalanceChanged?.Invoke();
	}

	// Lazy first-bet funding — mirrors DiceGame.EnsureInitialBankrollFunded() for the player.
	// Pre-genesis the casino holds the full unsplit loan in Main Balance with an empty Bankroll and no
	// loan booked (LoanCount == 0); the first settled bet books the initial loan and splits off the Bankroll
	// using whatever BankrollTarget is currently in effect (case 1: dev-configured; case 2: the 1M default).
	private void EnsureInitialCasinoFundingIfNeeded()
	{
		if (LoanCount > 0) return;              // already funded this session

		LoanCount   = 1;
		TotalLoaned = InitialLoanAmount;
		decimal transfer = Money.Normalize(Math.Min(BankrollTarget, MainBalance));
		MainBalance = Money.Normalize(MainBalance - transfer);
		Bankroll    = Money.Normalize(Bankroll + transfer);
		// Phase CG.2, once LoanHistory exists: AddLoanRecord(InitialLoanAmount, "startup");
	}

	// Called by SimulationService after each settled player bet.
	// casinoDelta = −(player's creditedProfit): positive when player loses, negative when player wins.
	// Bankroll fluctuates freely with each bet result; auto-recharge fires only when it is exhausted.
	public void ApplyBetResult(decimal casinoDelta)
	{
		EnsureInitialCasinoFundingIfNeeded();
		Bankroll = Money.Normalize(Bankroll + casinoDelta);
		if (Bankroll <= 0m)
			TryAutoRecharge();
		Bankroll = Money.Normalize(Math.Max(0m, Bankroll));
		SaveState();
		BalanceChanged?.Invoke();

		_betCount++;
		if (_betCount % 100 == 0)
			GD.Print($"[CasinoSC] bet#{_betCount}  delta={casinoDelta:+0.00000000;-0.00000000}  Bankroll={Bankroll:F8}  Main={MainBalance:F8}  P/L={CumulativeProfitSinceLoan:+0.00;-0.00}");
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

	// Called by BlockSessionCheckpointService.ResetToPreGenesisDefaults() on every boot until the first real
	// block is mined. Forces the casino's SC sheet back to its true "first launch" state — mirrors the player
	// side, since nothing is committed to disk pre-genesis (a block is the only commit). BankrollTarget also
	// reverts here: a custom target only "sticks" once a real block captures it into a checkpoint.
	public void ResetToPreGenesisDefaults()
	{
		MainBalance    = DefaultMainBalance; // 100,000,000
		Bankroll       = 0m;
		BankrollTarget = DefaultBankroll;    // 1,000,000
		LoanCount      = 0;
		TotalLoaned    = 0m;
		// Phase CG.2, once LoanHistory exists: _loanHistory.Clear();
		SaveState();
		BalanceChanged?.Invoke();
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
			// 0 is now a legitimate pre-genesis value (no loan taken until the first settled bet funds the
			// casino), so do NOT coerce it up to 1 / InitialLoanAmount as the old funded-from-boot model did.
			LoanCount      = Math.Max(0, snapshot.LoanCount);
			TotalLoaned    = Money.Normalize(Math.Max(0m, snapshot.TotalLoaned));
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
		MainBalance    = DefaultMainBalance; // 100,000,000 — full unsplit loan
		Bankroll       = 0m;                 // funded lazily on the first settled bet
		BankrollTarget = DefaultBankroll;    // 1,000,000 — the casino's "dose"
		LoanCount      = 0;
		TotalLoaned    = 0m;
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
