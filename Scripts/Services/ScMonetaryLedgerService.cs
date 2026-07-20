using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;

// Autoload #18 (Step 14 ND.8c / D-ND8.35). The SC MONETARY LEDGER — monetary-system Option 0 of the
// fiat-debt ladder (step14 plan §12.4.6e): a pure ACCOUNTING service recording every event where SC
// enters or leaves existence (mint/burn). Flows between existing holders (bets, transfers, swaps,
// settlements) are NOT its job — those already have their own ledgers (CasinoClientLedgerService etc.).
//
// Standing invariant (D-ND8.35):  TotalCirculation = TotalGenesisGrants + TotalDebtOutstanding
//
//  • GENESIS GRANTS — the canonical starting SC of the world's casino players: the player's 40,000
//    split and each of bot_1..4's 40,000 (39,900 Main + 100 Bankroll, the same canonical split).
//    Grants are EQUITY: granted once, never repayable, never debt. Registered declaratively at the
//    first-launch / pre-genesis paths and re-established on every pre-genesis reset (the
//    CasinoClientLedgerService "initial" precedent). Note the bots' balances MATERIALIZE lazily in
//    code (NetworkRoot.GetOrCreateNodeFinancialState, first time each bot runs), but canonically the
//    grant exists from world start — the ledger records the canon, not the lazy-init timing.
//  • LOAN DRAWS — every casino bank-loan draw (CasinoScBalanceService.AddLoanRecord: the bankruptcy
//    dose path, PayFromMainWithAutoLoan, and the dev manual loan) MINTS new SC as debt attributed to
//    "casino". The bank is the off-screen printer until ND.8e's Central Bank makes it explicit.
//  • BURNS — reserved for ND.8e (Option A): repayment destroys SC, decrementing the borrower's debt.
//
// Persisted to user://sc_monetary_ledger.json; checkpoint-covered (BlockSessionCheckpointService DTO,
// pre-genesis reset, world-reset delete list — the CLAUDE.md three-question rule). No WorldFormatVersion
// bump: accounting-only, first run in an existing world initializes from live state (grants + the
// casino's current TotalLoaned). See AIHelperFiles/step14-historical-network-population-scheduler-plan.md
// §12.4.6e + §12.5.1.
public partial class ScMonetaryLedgerService : Node
{
	public const string KindGrant    = "grant";
	public const string KindLoanDraw = "loan_draw";
	public const string KindBurn     = "burn"; // reserved — arrives with ND.8e (repayment destroys SC)

	public const string PartyPlayer = "player";
	public const string PartyCasino = "casino";
	// The world's five canonical casino players, each granted the same 40,000 SC start (CG.3.D mirror).
	private static readonly string[] GenesisGrantParties = { PartyPlayer, "bot_1", "bot_2", "bot_3", "bot_4" };
	public static decimal GenesisGrantPerParty => BankrollProgramService.InitialPrincipalBalanceBaseline; // 40,000

	private const string StatePath = "user://sc_monetary_ledger.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	// Loan draws fire per casino depletion — rare, but unbounded over a long run; cap the event log to keep
	// the JSON / checkpoint bounded (oldest trimmed). The running totals stay EXACT independently of the cap.
	private const int MaxEventHistory = 500;

	private CalendarTimeService _calendarTime;

	// One mint/burn event. GameDateLocal is game-world time (CalendarTimeService), never wall-clock —
	// displayed and persisted (CLAUDE.md Pattern 2).
	public sealed class MintRecord
	{
		public string   Kind          { get; set; } = KindLoanDraw; // grant | loan_draw (burn reserved, ND.8e)
		public decimal  Amount        { get; set; }
		public string   PartyId       { get; set; } = string.Empty;
		public string   Reason        { get; set; } = string.Empty; // "genesis" | "auto" | "manual" | "init_sync"
		public DateTime GameDateLocal { get; set; }
	}

	private readonly List<MintRecord> _events = new();
	public IReadOnlyList<MintRecord> Events => _events;

	private readonly Dictionary<string, decimal> _grantsByParty  = new();
	private readonly Dictionary<string, decimal> _debtByBorrower = new();
	public IReadOnlyDictionary<string, decimal> GrantsByParty  => _grantsByParty;
	public IReadOnlyDictionary<string, decimal> DebtByBorrower => _debtByBorrower;

	public decimal TotalGenesisGrants   => Money.Normalize(_grantsByParty.Values.Sum());
	public decimal TotalDebtOutstanding => Money.Normalize(_debtByBorrower.Values.Sum());
	// The debt-backed StableCoin in one line: every SC in existence is a genesis grant or someone's debt.
	public decimal TotalCirculation     => Money.Normalize(TotalGenesisGrants + TotalDebtOutstanding);

	public event Action LedgerChanged;

	public override void _Ready()
	{
		LoadState();
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		GD.Print($"[ScMonetaryLedgerService] Ready — Circulation={TotalCirculation:F8} SC  Grants={TotalGenesisGrants:F8}  Debt={TotalDebtOutstanding:F8}  events={_events.Count}");
	}

	private DateTime GameLocalNow() => _calendarTime?.CurrentLocalDateTime ?? DateTime.Now;

	private void AddEvent(string kind, decimal amount, string partyId, string reason)
	{
		_events.Add(new MintRecord
		{
			Kind          = kind,
			Amount        = Money.Normalize(amount),
			PartyId       = partyId,
			Reason        = reason,
			GameDateLocal = GameLocalNow()
		});
		if (_events.Count > MaxEventHistory)
			_events.RemoveRange(0, _events.Count - MaxEventHistory);
	}

	// ---- Mint API ---------------------------------------------------------------------------------------------

	// A casino bank-loan draw mints `amount` new SC as debt on `borrowerId` (today always "casino").
	// Called from CasinoScBalanceService.AddLoanRecord — the single funnel all three loan-draw sites
	// (bankruptcy dose recharge, auction-settlement funding, dev manual loan) already flow through.
	public void RegisterLoanDraw(string borrowerId, decimal amount, string reason)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || string.IsNullOrEmpty(borrowerId)) return;

		_debtByBorrower[borrowerId] = Money.Normalize(_debtByBorrower.GetValueOrDefault(borrowerId) + amount);
		AddEvent(KindLoanDraw, amount, borrowerId, string.IsNullOrEmpty(reason) ? "auto" : reason);
		SaveState();
		LedgerChanged?.Invoke();
	}

	// Reserved for ND.8e (Option A): a repayment passes SC back up the chain and DESTROYS it, reducing the
	// borrower's outstanding debt. No caller exists yet — armed so the Central Bank subphase plugs in cleanly.
	public void RegisterBurn(string borrowerId, decimal amount, string reason)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || string.IsNullOrEmpty(borrowerId)) return;

		decimal current = _debtByBorrower.GetValueOrDefault(borrowerId);
		_debtByBorrower[borrowerId] = Money.Normalize(Math.Max(0m, current - amount));
		AddEvent(KindBurn, amount, borrowerId, string.IsNullOrEmpty(reason) ? "repayment" : reason);
		SaveState();
		LedgerChanged?.Invoke();
	}

	// Registers the five canonical genesis grants for any party that doesn't have one yet. Idempotent —
	// safe to call on every pre-genesis reset / live-state sync (grants are constants, never duplicated).
	private bool EnsureCanonicalGenesisGrants()
	{
		bool changed = false;
		foreach (string party in GenesisGrantParties)
		{
			if (_grantsByParty.ContainsKey(party)) continue;
			_grantsByParty[party] = GenesisGrantPerParty;
			AddEvent(KindGrant, GenesisGrantPerParty, party, "genesis");
			changed = true;
		}
		return changed;
	}

	// ---- Checkpoint + pre-genesis (mandatory — CLAUDE.md Important Pattern 2) ------------------------------------

	public sealed class CheckpointState
	{
		public Dictionary<string, decimal> GrantsByParty  { get; set; } = new();
		public Dictionary<string, decimal> DebtByBorrower { get; set; } = new();
		public List<MintRecord> Events { get; set; } = new();
	}

	// Called by BlockSessionCheckpointService.CaptureCheckpoint() at each mined block (block = the only commit).
	public CheckpointState CaptureCheckpointState() => new CheckpointState
	{
		GrantsByParty  = new Dictionary<string, decimal>(_grantsByParty),
		DebtByBorrower = new Dictionary<string, decimal>(_debtByBorrower),
		Events         = _events.Select(CloneRecord).ToList()
	};

	// Called by BlockSessionCheckpointService.ApplyCheckpointToServices() on restart, AFTER the casino SC
	// restore. A null DTO means a legacy checkpoint captured before ND.8c existed — initialize from live
	// state instead (D-ND8.35): canonical grants + debt synced from the casino's just-restored TotalLoaned.
	public void RestoreFromCheckpoint(CheckpointState state)
	{
		if (state == null)
		{
			SyncFromLiveWorld();
			return;
		}

		_grantsByParty.Clear();
		foreach (var kv in state.GrantsByParty ?? new Dictionary<string, decimal>())
			if (kv.Value > 0m) _grantsByParty[kv.Key] = Money.Normalize(kv.Value);

		_debtByBorrower.Clear();
		foreach (var kv in state.DebtByBorrower ?? new Dictionary<string, decimal>())
			if (kv.Value > 0m) _debtByBorrower[kv.Key] = Money.Normalize(kv.Value);

		_events.Clear();
		foreach (var r in state.Events ?? new List<MintRecord>())
		{
			if (r == null || r.Amount <= 0m) continue;
			_events.Add(SanitizeRecord(r));
		}
		if (_events.Count > MaxEventHistory)
			_events.RemoveRange(0, _events.Count - MaxEventHistory);

		// Belt-and-braces: a checkpoint captures the ledger and the casino at the same block, so the two
		// should already agree — a mismatch means a legacy/mixed checkpoint, resolved toward the casino
		// (its TotalLoaned is the source of truth for casino debt).
		CasinoScBalanceService casinoSc = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		if (casinoSc != null && _debtByBorrower.GetValueOrDefault(PartyCasino) != casinoSc.TotalLoaned)
		{
			GD.PushWarning($"[ScMonetaryLedger] Checkpoint debt mismatch (ledger={_debtByBorrower.GetValueOrDefault(PartyCasino):F8} vs casino TotalLoaned={casinoSc.TotalLoaned:F8}) — reconciled to the casino.");
			if (casinoSc.TotalLoaned > 0m) _debtByBorrower[PartyCasino] = casinoSc.TotalLoaned;
			else _debtByBorrower.Remove(PartyCasino);
		}

		EnsureCanonicalGenesisGrants(); // legacy DTOs can't lack these, but keep the invariant unconditional
		SaveState();
		GD.Print($"[ScMonetaryLedger] RESTORED from checkpoint — Circulation={TotalCirculation:F8}  Grants={TotalGenesisGrants:F8}  Debt={TotalDebtOutstanding:F8}  events={_events.Count}");
		LedgerChanged?.Invoke();
	}

	// First run in an existing world / legacy checkpoint: establish the canonical grants and take the
	// casino's (already-restored) TotalLoaned as the opening debt, marked by one "init_sync" event so the
	// log shows the ledger's own starting point honestly rather than pretending to know the loan history.
	private void SyncFromLiveWorld()
	{
		bool changed = EnsureCanonicalGenesisGrants();

		CasinoScBalanceService casinoSc = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		decimal liveDebt = casinoSc?.TotalLoaned ?? 0m;
		if (_debtByBorrower.GetValueOrDefault(PartyCasino) != liveDebt)
		{
			if (liveDebt > 0m)
			{
				_debtByBorrower[PartyCasino] = liveDebt;
				AddEvent(KindLoanDraw, liveDebt, PartyCasino, "init_sync");
			}
			else
			{
				_debtByBorrower.Remove(PartyCasino);
			}
			changed = true;
		}

		if (changed)
		{
			SaveState();
			GD.Print($"[ScMonetaryLedger] Initialized from live state — Circulation={TotalCirculation:F8}  Grants={TotalGenesisGrants:F8}  Debt={TotalDebtOutstanding:F8}");
			LedgerChanged?.Invoke();
		}
	}

	// Called by BlockSessionCheckpointService.ResetToPreGenesisDefaults() on every boot until the first real
	// block is mined: back to the true first-launch state — the five canonical grants, zero debt, clean log
	// (nothing is committed pre-genesis; block = the only commit). Runs after the calendar reset, so the
	// grant events carry the player-start game time.
	public void ResetToPreGenesisDefaults()
	{
		_grantsByParty.Clear();
		_debtByBorrower.Clear();
		_events.Clear();
		EnsureCanonicalGenesisGrants();
		SaveState();
		LedgerChanged?.Invoke();
	}

	// ---- Persistence --------------------------------------------------------------------------------------------

	private sealed class Snapshot
	{
		public Dictionary<string, decimal> GrantsByParty  { get; set; } = new();
		public Dictionary<string, decimal> DebtByBorrower { get; set; } = new();
		public List<MintRecord> Events { get; set; } = new();
		public DateTime UpdatedAtUtc { get; set; }
	}

	private void LoadState()
	{
		if (!FileAccess.FileExists(StatePath))
		{
			// First run: leave everything empty — BlockSessionCheckpointService (registered after this
			// service) always drives one of the two boot paths, which establishes grants + debt.
			return;
		}

		try
		{
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
			string json = file.GetAsText();
			Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
			if (snapshot == null) return;

			_grantsByParty.Clear();
			foreach (var kv in snapshot.GrantsByParty ?? new Dictionary<string, decimal>())
				if (kv.Value > 0m) _grantsByParty[kv.Key] = Money.Normalize(kv.Value);

			_debtByBorrower.Clear();
			foreach (var kv in snapshot.DebtByBorrower ?? new Dictionary<string, decimal>())
				if (kv.Value > 0m) _debtByBorrower[kv.Key] = Money.Normalize(kv.Value);

			_events.Clear();
			foreach (var r in snapshot.Events ?? new List<MintRecord>())
			{
				if (r == null || r.Amount <= 0m) continue;
				_events.Add(SanitizeRecord(r));
			}
			if (_events.Count > MaxEventHistory)
				_events.RemoveRange(0, _events.Count - MaxEventHistory);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[ScMonetaryLedgerService] Load failed: {ex.Message}");
		}
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot
			{
				GrantsByParty  = new Dictionary<string, decimal>(_grantsByParty),
				DebtByBorrower = new Dictionary<string, decimal>(_debtByBorrower),
				Events         = _events.Select(CloneRecord).ToList(),
				UpdatedAtUtc   = DateTime.UtcNow
			};
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[ScMonetaryLedgerService] Save failed: {ex.Message}");
		}
	}

	// A clean copy with the Local kind explicitly re-stamped (JSON round-trips DateTimeKind as Unspecified).
	private static MintRecord CloneRecord(MintRecord r) => new MintRecord
	{
		Kind          = r.Kind,
		Amount        = r.Amount,
		PartyId       = r.PartyId,
		Reason        = r.Reason,
		GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
	};

	private static MintRecord SanitizeRecord(MintRecord r) => new MintRecord
	{
		Kind          = string.IsNullOrEmpty(r.Kind) ? KindLoanDraw : r.Kind,
		Amount        = Money.Normalize(r.Amount),
		PartyId       = r.PartyId ?? string.Empty,
		Reason        = r.Reason ?? string.Empty,
		GameDateLocal = r.GameDateLocal.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
			: r.GameDateLocal
	};
}
