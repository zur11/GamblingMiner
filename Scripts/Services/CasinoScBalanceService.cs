using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;

public partial class CasinoScBalanceService : Node
{
	// CANONICAL (CG.3.D): the casino is an exact mirror of one average player/client. Its foundational loan
	// equals a player's total starting funds (40,000 SC) and its bankroll dose equals a player's starting
	// Bankroll (100 SC) — so the first extra-lazy funding (draw a loan, transfer one dose) lands the casino at
	// 39,900 Main + 100 Bankroll, bit-for-bit the player's canonical 39,900 / 100 split.
	public const decimal InitialLoanAmount  = 40_000.00000000m;
	public const decimal DefaultBankroll    =    100.00000000m;
	// Extra-lazy funding (CG.1.8): pre-loan the casino holds NOTHING. The 40,000 foundational loan is drawn
	// on demand — only when a player win empties the Bankroll (TryAutoRecharge). Until then the casino just
	// accumulates player losses in its Bankroll with no loan and no recharge. So all balances start at 0
	// (only BankrollTarget keeps its 100 SC dose default). InitialLoanAmount stays 40,000 as the on-demand draw.
	public const decimal DefaultMainBalance = 0m;

	private const string StatePath = "user://casino_sc_balance_state.json";
	private int _betCount;
	private CalendarTimeService _calendarTime;
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	// One bank-loan draw (auto = the on-demand bankruptcy recharge; manual = dev-requested via CasinoGamblingFinances).
	// GameDateLocal is game-world time (CalendarTimeService), never wall-clock — displayed and persisted.
	public sealed class LoanRecord
	{
		public decimal  Amount        { get; set; }
		public string   Reason        { get; set; } = string.Empty; // "auto" | "manual"
		public DateTime GameDateLocal { get; set; }
	}

	private readonly List<LoanRecord> _loanHistory = new();
	public IReadOnlyList<LoanRecord> LoanHistory => _loanHistory;

	// One Bankroll recharge (auto = the on-demand TryAutoRecharge dose; manual = the Main Balance → Bankroll
	// transfer). Parallel to LoanRecord (CG.3.A). GameDateLocal is game-world time — displayed and persisted.
	public sealed class RechargeRecord
	{
		public decimal  Amount        { get; set; }
		public string   Reason        { get; set; } = string.Empty; // "auto" | "manual"
		public DateTime GameDateLocal { get; set; }
	}

	// Recharges fire far more often than loans (every bankroll-empty), so cap the history to keep the JSON /
	// checkpoint bounded (oldest trimmed). Loans stay uncapped — they're rare (one 40,000 chunk per depletion).
	private const int MaxRechargeHistory = 500;
	private readonly List<RechargeRecord> _rechargeHistory = new();
	public IReadOnlyList<RechargeRecord> RechargeHistory => _rechargeHistory;

	// Safety bound on TryAutoRecharge's loop. Normal recharges take 1–2 iterations; this only trips under a
	// pathological dev misconfiguration (a tiny AutoLoanAmount vs. a huge single-win deficit) to avoid a freeze.
	private const int MaxAutoRechargeIterations = 100_000;

	private sealed class Snapshot
	{
		public decimal  MainBalance    { get; set; }
		public decimal  Bankroll       { get; set; }
		public decimal  BankrollTarget { get; set; }
		public decimal  AutoLoanAmount { get; set; }
		public int      LoanCount      { get; set; }
		public decimal  TotalLoaned    { get; set; }
		public List<LoanRecord>     LoanHistory     { get; set; } = new();
		public List<RechargeRecord> RechargeHistory { get; set; } = new();
		public DateTime UpdatedAtUtc   { get; set; }
	}

	public decimal MainBalance    { get; private set; } = DefaultMainBalance;
	public decimal Bankroll       { get; private set; } = 0m;
	public decimal TotalSc        => Money.Normalize(MainBalance + Bankroll);

	// Positive = casino ahead of all cumulative loans; negative = casino in debt.
	public decimal CumulativeProfitSinceLoan => Money.Normalize(TotalSc - TotalLoaned);

	public decimal BankrollTarget { get; private set; } = DefaultBankroll;
	// Dose drawn per on-demand auto-loan (bankruptcy recharge). Dev-configurable (CG.3.C); reverts to this
	// default on every pre-genesis restart, sticks only once a real block commits it (mirrors BankrollTarget).
	public decimal AutoLoanAmount { get; private set; } = InitialLoanAmount;
	public int     LoanCount      { get; private set; } = 0;
	public decimal TotalLoaned    { get; private set; } = 0m;

	public event Action BalanceChanged;

	public override void _Ready()
	{
		LoadState();
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		GD.Print($"[CasinoScBalanceService] Ready — MainBalance={MainBalance:F8} SC  Bankroll={Bankroll:F8} SC  BankrollTarget={BankrollTarget:F8} SC  LoanCount={LoanCount}  TotalLoaned={TotalLoaned:F8} SC");
	}

	// Game-world time for a loan record (never wall-clock). Fallback only if the calendar autoload is absent.
	private void AddLoanRecord(decimal amount, string reason)
	{
		_loanHistory.Add(new LoanRecord
		{
			Amount        = Money.Normalize(amount),
			Reason        = reason,
			GameDateLocal = _calendarTime?.CurrentLocalDateTime ?? DateTime.Now
		});
	}

	// Game-world time for a recharge record; trim to the last MaxRechargeHistory so the history stays bounded.
	private void AddRechargeRecord(decimal amount, string reason)
	{
		_rechargeHistory.Add(new RechargeRecord
		{
			Amount        = Money.Normalize(amount),
			Reason        = reason,
			GameDateLocal = _calendarTime?.CurrentLocalDateTime ?? DateTime.Now
		});
		if (_rechargeHistory.Count > MaxRechargeHistory)
			_rechargeHistory.RemoveRange(0, _rechargeHistory.Count - MaxRechargeHistory);
	}

	// Called by BlockSessionCheckpointService.ApplyCheckpointToServices() on restart.
	// Sets MainBalance and Bankroll directly to checkpoint values — bypasses auto-recharge, does not persist.
	// Both == 0 means the fields were absent from the JSON (old checkpoint before Phase 11.2) — skip restore.
	public void RestoreCasinoScState(decimal main, decimal bankroll, decimal bankrollTarget, decimal autoLoanAmount, int loanCount, decimal totalLoaned, IReadOnlyList<LoanRecord> loanHistory, IReadOnlyList<RechargeRecord> rechargeHistory)
	{
		if (main == 0m && bankroll == 0m)
		{
			GD.Print("[CasinoSC] RestoreCasinoScState: skipped (no casino SC in checkpoint yet — using initialized defaults)");
			return;
		}
		MainBalance = Money.Normalize(Math.Max(0m, main));
		Bankroll    = Money.Normalize(Math.Max(0m, bankroll));
		// BankrollTarget/LoanCount/TotalLoaned were added to the checkpoint in Phase CG.0.6, LoanHistory in CG.2.
		// Under extra-lazy funding (CG.1.8), LoanCount==0 / TotalLoaned==0 / empty history are all VALID restorable
		// values (a block mined during a pure loss streak, before any loan), so we must not skip them. Gate on
		// BankrollTarget instead — it is always >0 in any CG.0.6+ checkpoint and absent/0 only in a legacy
		// pre-CG.0.6 one: when present, restore verbatim; when absent, keep what LoadState() loaded (legacy path).
		if (bankrollTarget > 0m)
		{
			BankrollTarget = Money.Normalize(bankrollTarget);
			AutoLoanAmount = autoLoanAmount > 0m ? Money.Normalize(autoLoanAmount) : InitialLoanAmount;
			LoanCount      = Math.Max(0, loanCount);
			TotalLoaned    = Money.Normalize(Math.Max(0m, totalLoaned));

			// Keep both histories in lockstep with LoanCount/TotalLoaned (block = the only commit): otherwise a
			// loan/recharge that happened after the checkpoint but before a restart would survive as a phantom entry.
			_loanHistory.Clear();
			foreach (var r in loanHistory ?? Array.Empty<LoanRecord>())
			{
				if (r == null || r.Amount <= 0m) continue;
				_loanHistory.Add(new LoanRecord
				{
					Amount        = Money.Normalize(r.Amount),
					Reason        = string.IsNullOrEmpty(r.Reason) ? "auto" : r.Reason,
					GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
				});
			}

			_rechargeHistory.Clear();
			foreach (var r in rechargeHistory ?? Array.Empty<RechargeRecord>())
			{
				if (r == null || r.Amount <= 0m) continue;
				_rechargeHistory.Add(new RechargeRecord
				{
					Amount        = Money.Normalize(r.Amount),
					Reason        = string.IsNullOrEmpty(r.Reason) ? "auto" : r.Reason,
					GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
				});
			}
			if (_rechargeHistory.Count > MaxRechargeHistory)
				_rechargeHistory.RemoveRange(0, _rechargeHistory.Count - MaxRechargeHistory);
		}
		GD.Print($"[CasinoSC] RESTORED from checkpoint — Main={MainBalance:F8}  Bankroll={Bankroll:F8}  Target={BankrollTarget:F8}  LoanCount={LoanCount}  TotalLoaned={TotalLoaned:F8}  P/L={CumulativeProfitSinceLoan:+0.00;-0.00}");
		BalanceChanged?.Invoke();
	}

	// Called by SimulationService (autobet) and DiceGame.ExecuteBet (manual) after each settled player bet.
	// casinoDelta = −(player's creditedProfit): positive when player loses, negative when player wins.
	// Extra-lazy funding (CG.1.8): the Bankroll simply accumulates player losses; the foundational loan is
	// NEVER drawn on a losing streak. Only when a player win pushes the Bankroll ≤ 0 does TryAutoRecharge()
	// fire — the sole funding trigger — injecting one BankrollTarget dose (drawing the 40,000 loan iff Main is
	// short) so the win's overage is absorbed by the recharged Bankroll, not by Main.
	public void ApplyBetResult(decimal casinoDelta)
	{
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

	// On-demand recharge — fixed-DOSE model (CG.1.8 correction). When a player win empties the Bankroll (≤ 0),
	// inject a BankrollTarget "dose" from Main into the Bankroll, drawing an AutoLoanAmount loan first if Main
	// can't cover a dose (CG.3.C — AutoLoanAmount is the dev-configurable loan chunk, default 40,000). The player's
	// winning payout that pushed the Bankroll negative is absorbed by the recharged Bankroll itself, NOT by Main —
	// Main only ever loses one dose per injection, never dose + payout overage (the old fill-to-target wrongly
	// made Main pay both). The loop iterates while Bankroll ≤ 0, so it always returns positive (ApplyBetResult's
	// Math.Max(0,…) clamp never discards real SC). Iteration count is bounded by the deficit ÷ (loan/dose), not by
	// the target — one iteration in the common case. If AutoLoanAmount < BankrollTarget the recharge under-fills
	// (transfer is capped at what a loan provides) and more iterations run; that's the dev's tradeoff — recommend
	// AutoLoanAmount ≥ BankrollTarget. MaxAutoRechargeIterations guards against a pathological freeze.
	// Always succeeds — the casino has an infinite credit line in Basic Mode.
	public void TryAutoRecharge()
	{
		if (BankrollTarget <= 0m) return;
		decimal loanChunk = AutoLoanAmount > 0m ? AutoLoanAmount : InitialLoanAmount;

		int safety = 0;
		while (Bankroll <= 0m && safety++ < MaxAutoRechargeIterations)
		{
			if (MainBalance < BankrollTarget)
			{
				MainBalance  = Money.Normalize(MainBalance + loanChunk);
				LoanCount++;
				TotalLoaned  = Money.Normalize(TotalLoaned + loanChunk);
				AddLoanRecord(loanChunk, "auto");
				GD.Print($"[CasinoScBalanceService] Bank loan #{LoanCount} drawn on demand ({loanChunk:F2} SC) — TotalLoaned={TotalLoaned:F8} SC");
			}

			decimal transfer = Money.Normalize(Math.Min(BankrollTarget, MainBalance));
			if (transfer <= 0m) break; // no funds and no loan possible — avoid a spin (never trips with loanChunk > 0)
			MainBalance = Money.Normalize(MainBalance - transfer);
			Bankroll    = Money.Normalize(Bankroll + transfer);
			AddRechargeRecord(transfer, "auto");
		}
	}

	// Dev-requested loan (CasinoGamblingFinances). Adds funds to Main Balance only — does not auto-recharge the
	// Bankroll (D16). Blank/invalid input defaults to InitialLoanAmount at the UI layer; here amount ≤ 0 → 40,000.
	public bool TriggerManualLoan(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m) amount = InitialLoanAmount;

		MainBalance = Money.Normalize(MainBalance + amount);
		LoanCount++;
		TotalLoaned = Money.Normalize(TotalLoaned + amount);
		AddLoanRecord(amount, "manual");
		SaveState();
		BalanceChanged?.Invoke();
		return true;
	}

	public bool TryTransferToBankroll(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || amount > MainBalance) return false;

		MainBalance = Money.Normalize(MainBalance - amount);
		Bankroll    = Money.Normalize(Bankroll + amount);
		AddRechargeRecord(amount, "manual");
		SaveState();
		BalanceChanged?.Invoke();
		return true;
	}

	// Step 13 (SW.3/SW.4) — the swap desk's casino SC legs (D-SW.3: a swap touches the casino's Main Balance
	// only; the Bankroll stays ApplyBetResult's bet float). Called by CasinoCoinSwapService only.
	public void ReceiveSwapSc(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m) return;

		MainBalance = Money.Normalize(MainBalance + amount);
		SaveState();
		BalanceChanged?.Invoke();
	}

	public bool TryPaySwapSc(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || amount > MainBalance) return false;

		MainBalance = Money.Normalize(MainBalance - amount);
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
		MainBalance    = DefaultMainBalance; // 0 — no loan drawn yet (CG.1.8)
		Bankroll       = 0m;
		BankrollTarget = DefaultBankroll;    // 100
		AutoLoanAmount = InitialLoanAmount;  // 40,000 auto-loan default (CG.3.C)
		LoanCount      = 0;
		TotalLoaned    = 0m;
		_loanHistory.Clear();
		_rechargeHistory.Clear();
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

	public void SetAutoLoanAmount(decimal amount)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m) return;

		AutoLoanAmount = amount;
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
			AutoLoanAmount = snapshot.AutoLoanAmount > 0m ? Money.Normalize(snapshot.AutoLoanAmount) : InitialLoanAmount;
			// 0 is now a legitimate pre-genesis value (no loan taken until the first settled bet funds the
			// casino), so do NOT coerce it up to 1 / InitialLoanAmount as the old funded-from-boot model did.
			LoanCount      = Math.Max(0, snapshot.LoanCount);
			TotalLoaned    = Money.Normalize(Math.Max(0m, snapshot.TotalLoaned));

			_loanHistory.Clear();
			foreach (var r in snapshot.LoanHistory ?? new List<LoanRecord>())
			{
				if (r == null || r.Amount <= 0m) continue;
				_loanHistory.Add(new LoanRecord
				{
					Amount        = Money.Normalize(r.Amount),
					Reason        = string.IsNullOrEmpty(r.Reason) ? "auto" : r.Reason,
					GameDateLocal = r.GameDateLocal.Kind == DateTimeKind.Unspecified
						? DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
						: r.GameDateLocal
				});
			}

			_rechargeHistory.Clear();
			foreach (var r in snapshot.RechargeHistory ?? new List<RechargeRecord>())
			{
				if (r == null || r.Amount <= 0m) continue;
				_rechargeHistory.Add(new RechargeRecord
				{
					Amount        = Money.Normalize(r.Amount),
					Reason        = string.IsNullOrEmpty(r.Reason) ? "auto" : r.Reason,
					GameDateLocal = r.GameDateLocal.Kind == DateTimeKind.Unspecified
						? DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
						: r.GameDateLocal
				});
			}
			if (_rechargeHistory.Count > MaxRechargeHistory)
				_rechargeHistory.RemoveRange(0, _rechargeHistory.Count - MaxRechargeHistory);
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
		MainBalance    = DefaultMainBalance; // 0 — no loan drawn until on-demand (CG.1.8)
		Bankroll       = 0m;                 // accumulates player losses; refilled only when a win empties it
		BankrollTarget = DefaultBankroll;    // 100 — the casino's "dose" (auto-recharge target)
		AutoLoanAmount = InitialLoanAmount;  // 40,000 — the auto-loan chunk (CG.3.C)
		LoanCount      = 0;
		TotalLoaned    = 0m;
		_loanHistory.Clear();               // no history entry for the (now on-demand) foundational loan — D15
		_rechargeHistory.Clear();
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
				AutoLoanAmount = AutoLoanAmount,
				LoanCount      = LoanCount,
				TotalLoaned    = TotalLoaned,
				LoanHistory    = _loanHistory
					.Select(r => new LoanRecord
					{
						Amount        = r.Amount,
						Reason        = r.Reason,
						GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
					})
					.ToList(),
				RechargeHistory = _rechargeHistory
					.Select(r => new RechargeRecord
					{
						Amount        = r.Amount,
						Reason        = r.Reason,
						GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
					})
					.ToList(),
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
