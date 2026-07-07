using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;

// Autoload #15 (Step 13 / SW.0). Owns the casino SWAP DESK's state and policy — per-asset strategic reserves,
// the rank-ready swap fee, and the swap history — for the CasinoCoinSwaps scene (SW.2). Execution legs (SW.3/SW.4)
// delegate to the existing owners (PrincipalBalanceService, CasinoScBalanceService, NetworkRoot); this service
// never moves money itself in SW.0. Availability rule (§1): OfferedForSwap(asset) = max(0, balance − reserve);
// the non-offered remainder is the casino's strategic reserve (BTC = war chest, SC = betting-pace float).
// All knobs mutate ONLY through the setters below — the future auto-swaps-scheduler calls the exact same API
// as the DEV scenes (CasinoFinances hosts the BTC reserve knob, CasinoGamblingFinances the fee + SC reserve;
// D-SW.9). Persisted per block (block = the only commit) — snapshotted into BlockSessionCheckpointService,
// reverts to defaults (reserves 0, fee 10%, floor OFF, no history) on every pre-genesis restart.
// See AIHelperFiles/step13-sw-casino-coin-swaps-plan.md.
public partial class CasinoCoinSwapService : Node
{
	// §3.1 — rank-ready fee model. One percent governs BOTH swap directions; it INCLUDES the 0.1 BTC network
	// fee (the casino pays the on-chain fee out of its collected margin, never the player on top — D-SW.1).
	public const decimal DefaultSwapFeePercent = 10m;
	public const decimal MinSwapFeePercent     = 1m;   // D-SW.9 clamp range
	public const decimal MaxSwapFeePercent     = 10m;

	// §2.3 R1 placeholder SC floor: effective SC reserve ≥ N × CasinoScBalanceService.BankrollTarget when
	// enabled — a static stand-in for the SW.5 recharge-pace floor (R2). Dev-tunable N, default OFF.
	public const decimal DefaultScFloorMultiplier = 10m;

	public const string DirectionScToBtc = "sc_to_btc"; // Panel A — player buys BTC with SC (casino sells)
	public const string DirectionBtcToSc = "btc_to_sc"; // Panel B — player sells BTC for SC (casino buys)
	public const string MethodManual     = "manual";
	public const string MethodAuto       = "auto";      // reserved for the future auto-swaps-scheduler / bots

	private const string StatePath = "user://casino_coin_swap_state.json";
	private const string TracePath = "user://logs/swap_desk_trace.csv";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	// Cap the history to keep the JSON / checkpoint bounded (oldest trimmed) — mirrors MaxTransferHistory /
	// MaxRechargeHistory on the other financial services.
	private const int MaxSwapHistory = 500;

	private CasinoScBalanceService _casinoSc;
	private CalendarTimeService    _calendarTime;

	// §2.2 — one per asset. Percent-of-balance OR absolute amount, toggleable; the reserve is the floor the
	// casino keeps back, only the surplus above it is offered for swaps (the TryAutoWithdraw threshold/surplus
	// shape, applied statically). Mutate only via SetBtcReserve/SetScReserve — public setters exist for JSON.
	public sealed class ReserveSetting
	{
		public bool    UsePercent { get; set; } = true;   // toggle: percent-of-balance vs absolute amount
		public decimal Percent    { get; set; } = 0m;     // 0–100; START 0 (= 100% offered, for testing)
		public decimal Amount     { get; set; } = 0m;     // absolute floor in the asset's own unit

		public decimal ReserveFor(decimal balance) =>
			Money.Normalize(UsePercent ? balance * (Percent / 100m) : Math.Min(Amount, balance));
	}

	// One executed swap (§5). GrossIn/NetOut are in the asset each side actually moved (Panel A: SC in / BTC out;
	// Panel B: BTC in / SC out); PriceUsed is the market-day SC-per-BTC quote. GameDateLocal is game-world time
	// (CalendarTimeService), never wall-clock — displayed and persisted (CLAUDE.md Pattern 2).
	public sealed class SwapRecord
	{
		public DateTime GameDateLocal { get; set; }
		public string   ClientId      { get; set; } = "player";
		public string   Direction     { get; set; } = string.Empty;
		public decimal  GrossIn       { get; set; }
		public decimal  FeeCharged    { get; set; }
		public decimal  NetOut        { get; set; }
		public decimal  PriceUsed     { get; set; }
		public string   Method        { get; set; } = MethodManual;
	}

	private readonly List<SwapRecord> _swapHistory = new();
	public IReadOnlyList<SwapRecord> SwapHistory => _swapHistory;

	// Defaults: reserve 0 ⇒ 100% offered (the user-specified testing starting point, §2.2).
	public ReserveSetting BtcReserve { get; private set; } = new();
	public ReserveSetting ScReserve  { get; private set; } = new();
	public decimal SwapFeePercent    { get; private set; } = DefaultSwapFeePercent;
	public bool    ScFloorEnabled    { get; private set; } = false;
	public decimal ScFloorMultiplier { get; private set; } = DefaultScFloorMultiplier;

	public event Action SwapDeskChanged;

	public override void _Ready()
	{
		LoadState();
		_casinoSc     = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		GD.Print($"[CasinoCoinSwapService] Ready — Fee={SwapFeePercent:F2}%  BtcReserve={(BtcReserve.UsePercent ? BtcReserve.Percent + "%" : BtcReserve.Amount + " BTC")}  ScReserve={(ScReserve.UsePercent ? ScReserve.Percent + "%" : ScReserve.Amount + " SC")}  ScFloor={(ScFloorEnabled ? $"ON (N={ScFloorMultiplier:F2})" : "OFF")}  history={_swapHistory.Count}");
	}

	// THE accessor every quote/execution path uses (§3.1). Today it ignores clientId and returns the global
	// value; the rank system (future step) overrides per client HERE and nowhere else.
	public decimal GetSwapFeePercentFor(string clientId) => SwapFeePercent;

	// ---- Availability skeleton (fully wired in SW.1) --------------------------------------------------------------

	// SW.1 replaces this with the NetworkRoot helper: casino confirmed UTXOs − pending outgoing. Until then the
	// desk sees zero casino BTC, which is also the true simulacrum starting state (extra-lazy, §1.1).
	public decimal CasinoBtcBalance => 0m;

	public decimal CasinoScMainBalance => _casinoSc?.MainBalance ?? 0m;

	// §2.3 — the R1 static floor value (0 when disabled). SW.5 composes the R2 auto floor into the same max().
	public decimal ScAutoFloor =>
		ScFloorEnabled ? Money.Normalize(ScFloorMultiplier * (_casinoSc?.BankrollTarget ?? 0m)) : 0m;

	// Effective SC floor = max(manual reserve, R1 floor when enabled) — the same max() composition as
	// TryAutoWithdraw's anti-ping-pong guard (§2.3).
	public decimal EffectiveScReserve =>
		Money.Normalize(Math.Max(ScReserve.ReserveFor(CasinoScMainBalance), ScAutoFloor));

	public decimal OfferedBtc =>
		Money.Normalize(Math.Max(0m, CasinoBtcBalance - BtcReserve.ReserveFor(CasinoBtcBalance)));

	public decimal OfferedSc =>
		Money.Normalize(Math.Max(0m, CasinoScMainBalance - EffectiveScReserve));

	// ---- Setters (§2.2 — the single mutation API the DEV scenes AND the future scheduler share) -------------------

	public void SetBtcReserve(bool usePercent, decimal percent, decimal amount)
	{
		BtcReserve = SanitizeReserve(usePercent, percent, amount);
		SaveState();
		AppendTrace("btc_reserve_set", string.Empty, 0m, 0m, 0m, 0m);
		SwapDeskChanged?.Invoke();
	}

	public void SetScReserve(bool usePercent, decimal percent, decimal amount)
	{
		ScReserve = SanitizeReserve(usePercent, percent, amount);
		SaveState();
		AppendTrace("sc_reserve_set", string.Empty, 0m, 0m, 0m, 0m);
		SwapDeskChanged?.Invoke();
	}

	// Clamps to [1,10] (D-SW.9) — the UI SpinBox refuses out-of-range values first; this is the safety net.
	public void SetSwapFeePercent(decimal percent)
	{
		SwapFeePercent = Math.Clamp(Money.Normalize(percent), MinSwapFeePercent, MaxSwapFeePercent);
		SaveState();
		AppendTrace("fee_set", string.Empty, 0m, 0m, 0m, 0m);
		SwapDeskChanged?.Invoke();
	}

	// R1 placeholder floor toggle + multiplier (§2.3). Non-positive N falls back to the default.
	public void SetScFloor(bool enabled, decimal multiplier)
	{
		ScFloorEnabled    = enabled;
		ScFloorMultiplier = multiplier > 0m ? Money.Normalize(multiplier) : DefaultScFloorMultiplier;
		SaveState();
		AppendTrace("sc_floor_set", string.Empty, 0m, 0m, 0m, 0m);
		SwapDeskChanged?.Invoke();
	}

	// ---- Swap history (the SW.3/SW.4 execution pipelines call this after moving the money) -------------------------

	public void RegisterSwap(string clientId, string direction, decimal grossIn, decimal feeCharged, decimal netOut, decimal priceUsed, string method)
	{
		_swapHistory.Add(new SwapRecord
		{
			GameDateLocal = _calendarTime?.CurrentLocalDateTime ?? DateTime.Now,
			ClientId      = string.IsNullOrEmpty(clientId) ? "player" : clientId,
			Direction     = direction,
			GrossIn       = Money.Normalize(grossIn),
			FeeCharged    = Money.Normalize(feeCharged),
			NetOut        = Money.Normalize(netOut),
			PriceUsed     = Money.Normalize(priceUsed),
			Method        = string.IsNullOrEmpty(method) ? MethodManual : method
		});
		if (_swapHistory.Count > MaxSwapHistory)
			_swapHistory.RemoveRange(0, _swapHistory.Count - MaxSwapHistory);

		SaveState();
		AppendTrace(direction, clientId, grossIn, feeCharged, netOut, priceUsed);
		SwapDeskChanged?.Invoke();
	}

	// ---- Trace CSV (§2.4 — the dataset the future scheduler's rules will be tuned from) ---------------------------
	// One row per swap and per knob change: game date, event, amounts, fee, both casino balances, both reserves,
	// both offered figures. Mirrors founders_trace.csv (best-effort; never throws). InvariantCulture throughout.

	private void AppendTrace(string eventName, string clientId, decimal grossIn, decimal feeCharged, decimal netOut, decimal priceUsed)
	{
		try
		{
			if (!DirAccess.DirExistsAbsolute("user://logs"))
			{
				DirAccess.MakeDirRecursiveAbsolute("user://logs");
			}

			bool exists = FileAccess.FileExists(TracePath);
			using FileAccess file = exists
				? FileAccess.Open(TracePath, FileAccess.ModeFlags.ReadWrite)
				: FileAccess.Open(TracePath, FileAccess.ModeFlags.Write);
			if (file == null)
			{
				return;
			}

			if (exists)
			{
				file.SeekEnd();
			}
			else
			{
				file.StoreLine("gameDateLocal,event,clientId,grossIn,feeCharged,netOut,priceUsed,swapFeePercent,casinoScMain,casinoBtc,scReserveEffective,btcReserve,offeredSc,offeredBtc");
			}

			DateTime gameLocal = _calendarTime?.CurrentLocalDateTime ?? DateTime.Now;
			file.StoreLine(string.Format(CultureInfo.InvariantCulture,
				"{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3:F8},{4:F8},{5:F8},{6:F8},{7:F2},{8:F8},{9:F8},{10:F8},{11:F8},{12:F8},{13:F8}",
				gameLocal, eventName, clientId,
				grossIn, feeCharged, netOut, priceUsed, SwapFeePercent,
				CasinoScMainBalance, CasinoBtcBalance,
				EffectiveScReserve, BtcReserve.ReserveFor(CasinoBtcBalance),
				OfferedSc, OfferedBtc));
		}
		catch (Exception e)
		{
			GD.PushWarning($"[SwapDeskTrace] failed: {e.Message}");
		}
	}

	// ---- Checkpoint + pre-genesis (mandatory — CLAUDE.md Important Pattern 2, both paths, day one) -----------------

	// The block-checkpoint DTO, bundled into BlockSessionCheckpointService.Snapshot as one field (the
	// PlayerBankAccountService bundling pattern).
	public sealed class CheckpointState
	{
		public ReserveSetting BtcReserve { get; set; } = new();
		public ReserveSetting ScReserve  { get; set; } = new();
		public decimal SwapFeePercent    { get; set; } = DefaultSwapFeePercent;
		public bool    ScFloorEnabled    { get; set; }
		public decimal ScFloorMultiplier { get; set; } = DefaultScFloorMultiplier;
		public List<SwapRecord> SwapHistory { get; set; } = new();
	}

	// Called by BlockSessionCheckpointService.CaptureCheckpoint() at each mined block (block = the only commit).
	public CheckpointState CaptureCheckpointState() => new CheckpointState
	{
		BtcReserve        = CloneReserve(BtcReserve),
		ScReserve         = CloneReserve(ScReserve),
		SwapFeePercent    = SwapFeePercent,
		ScFloorEnabled    = ScFloorEnabled,
		ScFloorMultiplier = ScFloorMultiplier,
		SwapHistory       = _swapHistory.Select(CloneRecord).ToList()
	};

	// Called by BlockSessionCheckpointService.ApplyCheckpointToServices() on restart. A null DTO means a legacy
	// checkpoint captured before SW.0 existed — keep whatever LoadState() loaded (no migration).
	public void RestoreFromCheckpoint(CheckpointState state)
	{
		if (state == null)
		{
			GD.Print("[CasinoCoinSwapService] RestoreFromCheckpoint: skipped (legacy checkpoint — keeping loaded state)");
			return;
		}

		BtcReserve        = SanitizeReserve(state.BtcReserve?.UsePercent ?? true, state.BtcReserve?.Percent ?? 0m, state.BtcReserve?.Amount ?? 0m);
		ScReserve         = SanitizeReserve(state.ScReserve?.UsePercent ?? true, state.ScReserve?.Percent ?? 0m, state.ScReserve?.Amount ?? 0m);
		SwapFeePercent    = Math.Clamp(Money.Normalize(state.SwapFeePercent), MinSwapFeePercent, MaxSwapFeePercent);
		ScFloorEnabled    = state.ScFloorEnabled;
		ScFloorMultiplier = state.ScFloorMultiplier > 0m ? Money.Normalize(state.ScFloorMultiplier) : DefaultScFloorMultiplier;

		_swapHistory.Clear();
		foreach (var r in state.SwapHistory ?? new List<SwapRecord>())
		{
			if (r == null || r.GrossIn <= 0m) continue;
			_swapHistory.Add(SanitizeRecord(r));
		}
		if (_swapHistory.Count > MaxSwapHistory)
			_swapHistory.RemoveRange(0, _swapHistory.Count - MaxSwapHistory);

		SaveState();
		GD.Print($"[CasinoCoinSwapService] RESTORED from checkpoint — Fee={SwapFeePercent:F2}%  ScFloor={(ScFloorEnabled ? "ON" : "OFF")}  history={_swapHistory.Count}");
		SwapDeskChanged?.Invoke();
	}

	// Called by BlockSessionCheckpointService.ResetToPreGenesisDefaults() on every boot until the first real
	// block is mined. Forces the desk back to its true "first launch" state — reserves 0 (100% offered), fee
	// 10%, R1 floor OFF, no history. Settings stick only at a block, like every other knob.
	public void ResetToPreGenesisDefaults()
	{
		BtcReserve        = new ReserveSetting();
		ScReserve         = new ReserveSetting();
		SwapFeePercent    = DefaultSwapFeePercent;
		ScFloorEnabled    = false;
		ScFloorMultiplier = DefaultScFloorMultiplier;
		_swapHistory.Clear();
		SaveState();
		SwapDeskChanged?.Invoke();
	}

	// ---- Persistence --------------------------------------------------------------------------------------------

	private sealed class Snapshot
	{
		public ReserveSetting BtcReserve { get; set; } = new();
		public ReserveSetting ScReserve  { get; set; } = new();
		public decimal  SwapFeePercent    { get; set; } = DefaultSwapFeePercent;
		public bool     ScFloorEnabled    { get; set; }
		public decimal  ScFloorMultiplier { get; set; } = DefaultScFloorMultiplier;
		public List<SwapRecord> SwapHistory { get; set; } = new();
		public DateTime UpdatedAtUtc { get; set; }
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

			BtcReserve        = SanitizeReserve(snapshot.BtcReserve?.UsePercent ?? true, snapshot.BtcReserve?.Percent ?? 0m, snapshot.BtcReserve?.Amount ?? 0m);
			ScReserve         = SanitizeReserve(snapshot.ScReserve?.UsePercent ?? true, snapshot.ScReserve?.Percent ?? 0m, snapshot.ScReserve?.Amount ?? 0m);
			SwapFeePercent    = Math.Clamp(Money.Normalize(snapshot.SwapFeePercent), MinSwapFeePercent, MaxSwapFeePercent);
			ScFloorEnabled    = snapshot.ScFloorEnabled;
			ScFloorMultiplier = snapshot.ScFloorMultiplier > 0m ? Money.Normalize(snapshot.ScFloorMultiplier) : DefaultScFloorMultiplier;

			_swapHistory.Clear();
			foreach (var r in snapshot.SwapHistory ?? new List<SwapRecord>())
			{
				if (r == null || r.GrossIn <= 0m) continue;
				_swapHistory.Add(SanitizeRecord(r));
			}
			if (_swapHistory.Count > MaxSwapHistory)
				_swapHistory.RemoveRange(0, _swapHistory.Count - MaxSwapHistory);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CasinoCoinSwapService] Load failed: {ex.Message}");
			InitializeDefaults();
			SaveState();
		}
	}

	private void InitializeDefaults()
	{
		BtcReserve        = new ReserveSetting();
		ScReserve         = new ReserveSetting();
		SwapFeePercent    = DefaultSwapFeePercent;
		ScFloorEnabled    = false;
		ScFloorMultiplier = DefaultScFloorMultiplier;
		_swapHistory.Clear();
	}

	private void SaveState()
	{
		try
		{
			var snapshot = new Snapshot
			{
				BtcReserve        = CloneReserve(BtcReserve),
				ScReserve         = CloneReserve(ScReserve),
				SwapFeePercent    = SwapFeePercent,
				ScFloorEnabled    = ScFloorEnabled,
				ScFloorMultiplier = ScFloorMultiplier,
				SwapHistory       = _swapHistory.Select(CloneRecord).ToList(),
				UpdatedAtUtc      = DateTime.UtcNow
			};
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[CasinoCoinSwapService] Save failed: {ex.Message}");
		}
	}

	private static ReserveSetting SanitizeReserve(bool usePercent, decimal percent, decimal amount) => new ReserveSetting
	{
		UsePercent = usePercent,
		Percent    = Math.Clamp(Money.Normalize(percent), 0m, 100m),
		Amount     = Money.Normalize(Math.Max(0m, amount))
	};

	private static ReserveSetting CloneReserve(ReserveSetting r) => new ReserveSetting
	{
		UsePercent = r.UsePercent,
		Percent    = r.Percent,
		Amount     = r.Amount
	};

	// A clean copy with the Local kind explicitly re-stamped (JSON round-trips DateTimeKind as Unspecified).
	private static SwapRecord CloneRecord(SwapRecord r) => new SwapRecord
	{
		GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local),
		ClientId      = r.ClientId,
		Direction     = r.Direction,
		GrossIn       = r.GrossIn,
		FeeCharged    = r.FeeCharged,
		NetOut        = r.NetOut,
		PriceUsed     = r.PriceUsed,
		Method        = r.Method
	};

	private static SwapRecord SanitizeRecord(SwapRecord r) => new SwapRecord
	{
		GameDateLocal = r.GameDateLocal.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
			: r.GameDateLocal,
		ClientId      = string.IsNullOrEmpty(r.ClientId) ? "player" : r.ClientId,
		Direction     = string.IsNullOrEmpty(r.Direction) ? DirectionScToBtc : r.Direction,
		GrossIn       = Money.Normalize(r.GrossIn),
		FeeCharged    = Money.Normalize(r.FeeCharged),
		NetOut        = Money.Normalize(r.NetOut),
		PriceUsed     = Money.Normalize(r.PriceUsed),
		Method        = string.IsNullOrEmpty(r.Method) ? MethodManual : r.Method
	};
}
