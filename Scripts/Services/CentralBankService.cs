using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;

// Autoload #19 (Step 15 P15.1 / D-15.3, D-15.23 "Fork A"). THE CENTRAL BANK (FED) — the explicit
// in-world entity behind the SC the casino has always borrowed from, now visible, persisted and
// (from P15.2) shared with the four bank companies.
//
// RESPONSIBILITY SPLIT (D-15.23, Fork A):
//   • This service is the ENTITY / RELATIONSHIP layer — per-client accounts (outstanding debt, total
//     drawn/repaid, loan & repayment history). It is the AUTHORITATIVE per-client store; the casino no
//     longer keeps its own LoanCount/TotalLoaned/LoanHistory copy (P15.1c collapsed that duplication).
//   • ScMonetaryLedgerService stays the MACRO ACCOUNTING layer — the mint/burn event log and the
//     standing invariant `circulation = grants + debt`. It is kept in lockstep for free: every DrawLoan
//     calls its RegisterLoanDraw (mint) and every Repay calls its RegisterBurn (burn — the ledger's
//     first real caller, armed since ND.8c).
//
// SCOPE (D-15.1): plan15 builds the FED entity + scene + persistence with UNLIMITED (auto-loan)
// lending and NO interest — period-accurate for the ZIRP 2009–2015 window. The fed-funds-rate
// historical replay and the per-client credit-capacity LIMITS stay deferred to ND.8e, one layer above.
//
// Clients today: the casino ("casino"). From P15.2: the four CB1 bank companies, keyed BankClientId().
// The casino is the sole entity exempt from dissolution (D-15.17) — its credit line never closes.
//
// Persisted to user://central_bank_state.json; checkpoint-covered (BlockSessionCheckpointService DTO,
// pre-genesis reset, world-reset delete list — the CLAUDE.md three-question rule). Registered BETWEEN
// ScMonetaryLedgerService and BlockSessionCheckpointService in project.godot: it must already be in the
// tree when the checkpoint restore / pre-genesis reset runs (the PlayerBankAccountService /
// CasinoCoinSwapService precedent), and the ledger it syncs into must be in the tree before IT loads.
// See AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md §3.1 + §8 (P15.1a/P15.1b).
public partial class CentralBankService : Node
{
	public const string KindDraw  = "draw";
	public const string KindRepay = "repay";

	// The FED's own client key for the casino — the same id the monetary ledger attributes casino debt to,
	// so the two layers agree by construction.
	public const string ClientCasino = ScMonetaryLedgerService.PartyCasino;

	// Layer-1 bank clients are keyed "bank:<companyNodeId>" (D-15.5). One helper so the key can never drift
	// between the FED account, the monetary ledger's borrower key, and the P15.7 telemetry.
	public static string BankClientId(string companyNodeId) => $"bank:{companyNodeId}";

	private const string StatePath = "user://central_bank_state.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	// Draws fire per casino depletion / per bank provision — unbounded over a 16-year run, so the per-client
	// history is capped (oldest trimmed) to keep the JSON + checkpoint DTO bounded. The running totals
	// (OutstandingDebt / TotalDrawn / TotalRepaid / DrawCount / RepayCount) stay EXACT independently of the
	// cap — the ScMonetaryLedgerService MaxEventHistory precedent.
	private const int MaxHistoryPerClient = 500;

	private CalendarTimeService _calendarTime;
	private ScMonetaryLedgerService _monetaryLedger;

	// One loan draw or repayment on a client's FED account. GameDateLocal is game-world time
	// (CalendarTimeService), never wall-clock — it is displayed and persisted (CLAUDE.md Pattern 2).
	public sealed class FedLoanRecord
	{
		public decimal  Amount        { get; set; }
		public string   Kind          { get; set; } = KindDraw;   // draw | repay
		public string   Reason        { get; set; } = string.Empty; // "auto" | "manual" | "provision" | "quarterly" | …
		public DateTime GameDateLocal { get; set; }
	}

	// One FED client's account. OutstandingDebt is what it still owes (draws − repayments); TotalDrawn is
	// cumulative and never decreases (it is what the casino's retired TotalLoaned meant, preserving
	// CumulativeProfitSinceLoan = TotalSc − TotalDrawn).
	public sealed class FedClientAccount
	{
		public decimal OutstandingDebt { get; set; }
		public decimal TotalDrawn      { get; set; }
		public decimal TotalRepaid     { get; set; }
		public int     DrawCount       { get; set; }
		public int     RepayCount      { get; set; }
		public List<FedLoanRecord> History { get; set; } = new();
	}

	private readonly Dictionary<string, FedClientAccount> _accounts = new();
	public IReadOnlyDictionary<string, FedClientAccount> Accounts => _accounts;

	// ---- Read accessors (null-safe for a client that has never borrowed) ------------------------------------

	public decimal OutstandingDebt(string clientId) => Get(clientId)?.OutstandingDebt ?? 0m;
	public decimal TotalDrawn(string clientId)      => Get(clientId)?.TotalDrawn ?? 0m;
	public decimal TotalRepaid(string clientId)     => Get(clientId)?.TotalRepaid ?? 0m;
	public int     DrawCount(string clientId)       => Get(clientId)?.DrawCount ?? 0;
	public int     RepayCount(string clientId)      => Get(clientId)?.RepayCount ?? 0;
	public bool    HasAccount(string clientId)      => Get(clientId) != null;

	public IReadOnlyList<FedLoanRecord> History(string clientId) =>
		(IReadOnlyList<FedLoanRecord>)Get(clientId)?.History ?? Array.Empty<FedLoanRecord>();

	public IEnumerable<string> ClientIds => _accounts.Keys;

	public decimal TotalOutstandingDebt => Money.Normalize(_accounts.Values.Sum(a => a.OutstandingDebt));
	public decimal TotalLentAllTime     => Money.Normalize(_accounts.Values.Sum(a => a.TotalDrawn));
	public decimal TotalRepaidAllTime   => Money.Normalize(_accounts.Values.Sum(a => a.TotalRepaid));

	public event Action CentralBankChanged;

	public override void _Ready()
	{
		LoadState();
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		GD.Print(string.Create(CultureInfo.InvariantCulture, $"[CentralBankService] Ready — clients={_accounts.Count}  Outstanding={TotalOutstandingDebt:F8} SC  LentAllTime={TotalLentAllTime:F8} SC"));
	}

	private FedClientAccount Get(string clientId) =>
		string.IsNullOrEmpty(clientId) ? null : _accounts.GetValueOrDefault(clientId);

	private FedClientAccount GetOrCreate(string clientId)
	{
		if (!_accounts.TryGetValue(clientId, out FedClientAccount account))
		{
			account = new FedClientAccount();
			_accounts[clientId] = account;
		}
		return account;
	}

	// Game-world time for every record (never wall-clock). Fallback only if the calendar autoload is absent.
	private DateTime GameLocalNow() => _calendarTime?.CurrentLocalDateTime ?? DateTime.Now;

	// The monetary ledger registers BEFORE us, so it IS in the tree by our _Ready — but resolve lazily and
	// null-guard anyway (the CasinoScBalanceService precedent: an absent autoload must never crash a draw).
	private ScMonetaryLedgerService Ledger =>
		_monetaryLedger ??= GetNodeOrNull<ScMonetaryLedgerService>("/root/ScMonetaryLedgerService");

	private void AppendRecord(FedClientAccount account, decimal amount, string kind, string reason)
	{
		account.History.Add(new FedLoanRecord
		{
			Amount        = Money.Normalize(amount),
			Kind          = kind,
			Reason        = string.IsNullOrEmpty(reason) ? "auto" : reason,
			GameDateLocal = GameLocalNow()
		});
		if (account.History.Count > MaxHistoryPerClient)
			account.History.RemoveRange(0, account.History.Count - MaxHistoryPerClient);
	}

	// ---- Draw / repay API (P15.1b) -------------------------------------------------------------------------

	// A client draws SC from the FED. Unlimited in plan15 (D-15.1) — the draw always succeeds. MINTS the SC
	// into existence via the monetary ledger, attributed as this client's debt (D-15.5 borrower keys).
	public void DrawLoan(string clientId, decimal amount, string reason)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || string.IsNullOrEmpty(clientId)) return;

		FedClientAccount account = GetOrCreate(clientId);
		account.OutstandingDebt = Money.Normalize(account.OutstandingDebt + amount);
		account.TotalDrawn      = Money.Normalize(account.TotalDrawn + amount);
		account.DrawCount++;
		AppendRecord(account, amount, KindDraw, reason);

		Ledger?.RegisterLoanDraw(clientId, amount, reason); // mint — keeps circulation = grants + debt
		SaveState();
		CentralBankChanged?.Invoke();
	}

	// A client repays the FED. Clamped to what it actually owes (over-payment is never debt-negative), and
	// BURNS the repaid SC out of existence via the ledger — Option A of the ND.8c fiat-debt ladder, whose
	// RegisterBurn hook has been armed and caller-less since ND.8c and gets its first real caller here.
	// Returns the amount actually repaid (0 when the client owes nothing).
	public decimal Repay(string clientId, decimal amount, string reason)
	{
		amount = Money.Normalize(amount);
		if (amount <= 0m || string.IsNullOrEmpty(clientId)) return 0m;

		FedClientAccount account = Get(clientId);
		if (account == null || account.OutstandingDebt <= 0m) return 0m;

		decimal applied = Money.Normalize(Math.Min(amount, account.OutstandingDebt));
		if (applied <= 0m) return 0m;

		account.OutstandingDebt = Money.Normalize(account.OutstandingDebt - applied);
		account.TotalRepaid     = Money.Normalize(account.TotalRepaid + applied);
		account.RepayCount++;
		AppendRecord(account, applied, KindRepay, string.IsNullOrEmpty(reason) ? "repayment" : reason);

		Ledger?.RegisterBurn(clientId, applied, reason); // burn — repayment destroys SC
		SaveState();
		CentralBankChanged?.Invoke();
		return applied;
	}

	// ---- Checkpoint + pre-genesis (mandatory — CLAUDE.md Important Pattern 2) --------------------------------

	public sealed class CheckpointState
	{
		public Dictionary<string, FedClientAccount> Accounts { get; set; } = new();
	}

	// Called by BlockSessionCheckpointService.CaptureCheckpoint() at each mined block (block = the only commit).
	public CheckpointState CaptureCheckpointState() => new CheckpointState
	{
		Accounts = _accounts.ToDictionary(kv => kv.Key, kv => CloneAccount(kv.Value))
	};

	// Called by BlockSessionCheckpointService.ApplyCheckpointToServices() on restart — BEFORE the casino SC
	// restore (the casino now reads its loan figures through us) and before the monetary ledger's restore
	// (whose legacy live-state init reads our casino debt). A null DTO means a checkpoint captured before
	// plan15 existed; the WorldFormatVersion 3 → 4 bump wipes those worlds, so this only guards the path.
	public void RestoreFromCheckpoint(CheckpointState state)
	{
		if (state == null) return;

		_accounts.Clear();
		foreach (var kv in state.Accounts ?? new Dictionary<string, FedClientAccount>())
		{
			if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
			_accounts[kv.Key] = SanitizeAccount(kv.Value);
		}

		SaveState();
		GD.Print(string.Create(CultureInfo.InvariantCulture, $"[CentralBank] RESTORED from checkpoint — clients={_accounts.Count}  Outstanding={TotalOutstandingDebt:F8}  LentAllTime={TotalLentAllTime:F8}"));
		CentralBankChanged?.Invoke();
	}

	// Called by BlockSessionCheckpointService.ResetToPreGenesisDefaults() on every boot until the first real
	// block is mined: the FED has lent nothing yet — no accounts, no history (nothing is committed
	// pre-genesis; a block is the only commit). Mirrors the casino's own pre-genesis reset, which used to
	// clear the very loan counters that now live here.
	public void ResetToPreGenesisDefaults()
	{
		_accounts.Clear();
		SaveState();
		CentralBankChanged?.Invoke();
	}

	// ---- Persistence ----------------------------------------------------------------------------------------

	private sealed class Snapshot
	{
		public Dictionary<string, FedClientAccount> Accounts { get; set; } = new();
		public DateTime UpdatedAtUtc { get; set; }
	}

	private void LoadState()
	{
		if (!FileAccess.FileExists(StatePath))
		{
			// First run: leave empty — BlockSessionCheckpointService (registered after this service) always
			// drives one of the two boot paths, which establishes the accounts.
			return;
		}

		try
		{
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
			string json = file.GetAsText();
			Snapshot snapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
			if (snapshot == null) return;

			_accounts.Clear();
			foreach (var kv in snapshot.Accounts ?? new Dictionary<string, FedClientAccount>())
			{
				if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
				_accounts[kv.Key] = SanitizeAccount(kv.Value);
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CentralBankService] Load failed: {ex.Message}");
		}
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot
			{
				Accounts     = _accounts.ToDictionary(kv => kv.Key, kv => CloneAccount(kv.Value)),
				UpdatedAtUtc = DateTime.UtcNow
			};
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CentralBankService] Save failed: {ex.Message}");
		}
	}

	private static FedClientAccount CloneAccount(FedClientAccount a) => new FedClientAccount
	{
		OutstandingDebt = a.OutstandingDebt,
		TotalDrawn      = a.TotalDrawn,
		TotalRepaid     = a.TotalRepaid,
		DrawCount       = a.DrawCount,
		RepayCount      = a.RepayCount,
		History         = a.History.Select(CloneRecord).ToList()
	};

	private static FedClientAccount SanitizeAccount(FedClientAccount a)
	{
		var clean = new FedClientAccount
		{
			OutstandingDebt = Money.Normalize(Math.Max(0m, a.OutstandingDebt)),
			TotalDrawn      = Money.Normalize(Math.Max(0m, a.TotalDrawn)),
			TotalRepaid     = Money.Normalize(Math.Max(0m, a.TotalRepaid)),
			DrawCount       = Math.Max(0, a.DrawCount),
			RepayCount      = Math.Max(0, a.RepayCount)
		};
		foreach (var r in a.History ?? new List<FedLoanRecord>())
		{
			if (r == null || r.Amount <= 0m) continue;
			clean.History.Add(SanitizeRecord(r));
		}
		if (clean.History.Count > MaxHistoryPerClient)
			clean.History.RemoveRange(0, clean.History.Count - MaxHistoryPerClient);
		return clean;
	}

	// A clean copy with the Local kind explicitly re-stamped (JSON round-trips DateTimeKind as Unspecified).
	private static FedLoanRecord CloneRecord(FedLoanRecord r) => new FedLoanRecord
	{
		Amount        = r.Amount,
		Kind          = r.Kind,
		Reason        = r.Reason,
		GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
	};

	private static FedLoanRecord SanitizeRecord(FedLoanRecord r) => new FedLoanRecord
	{
		Amount        = Money.Normalize(r.Amount),
		Kind          = string.IsNullOrEmpty(r.Kind) ? KindDraw : r.Kind,
		Reason        = string.IsNullOrEmpty(r.Reason) ? "auto" : r.Reason,
		GameDateLocal = r.GameDateLocal.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
			: r.GameDateLocal
	};
}
