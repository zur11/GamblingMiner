using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;

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
	// §3.1 — rank-ready fee model. One percent governs BOTH swap directions. ADDITIVE since D-SW.11
	// (2026-07-08, superseding the original inclusive D-SW.1 this comment described until 2026-08-20): the
	// network fee is a SEPARATE charge SUMMED with the casino's cut — totalFee = networkFee + fee×(gross +
	// networkFee) — never absorbed inside it, so the player pays it on top. D-SW.12 caps the casino's own cut
	// alone; the network fee is never capped and is always charged in full. And since ND.7 (D-ND7.9) that
	// networkFee is the day's replayed historical MEDIAN, not the retired flat 0.1 scaffold — it is a genuine
	// 0 from Market Birth through 2011-04-13. See ComputeScToBtcCore / ComputeBtcToScCore for the arithmetic.
	public const decimal DefaultSwapFeePercent = 10m;
	public const decimal MinSwapFeePercent     = 1m;   // D-SW.9 clamp range
	public const decimal MaxSwapFeePercent     = 10m;

	// D-SW.12 (2026-07-08) — max fee deviation, in PERCENTAGE POINTS above SwapFeePercent. Dev feedback: the
	// additive model's effective margin can run considerably above nominal on swaps near the minimum size
	// (e.g. ~13.6% at the §3.2 minimum when nominal is 10% — the flat 0.1 BTC network fee dominates a small
	// base). This caps how far the EFFECTIVE % is allowed to stray above SwapFeePercent; see ComputeScToBtcCore/
	// ComputeBtcToScCore for the clamp math. 0 points ⇒ effective % locked to nominal exactly (the casino then
	// absorbs 100% of the network cost on small swaps, like the pre-D-SW.11 inclusive model).
	public const decimal DefaultMaxFeeDeviationPoints = 2m;
	public const decimal MinMaxFeeDeviationPoints     = 0m;
	public const decimal MaxMaxFeeDeviationPoints     = 20m;

	// §2.3 R2 (SW.5) — the recharge-pace auto floor, superseding the R1 static-multiple placeholder (SW.0).
	// Sizes the SC floor to what the betting pace has ACTUALLY drawn recently: SafetyFactor × doses consumed
	// via auto-recharge in the last WindowDays of GAME time × BankrollTarget. Both dev-tunable; default OFF.
	// Window is in whole GAME days (dev feedback 2026-07-07 — days read more naturally than raw hours).
	// See ProjectDesignManual Ch. 33 for the full rationale + the R3 (drawdown-based) alternative.
	public const decimal DefaultScAutoFloorSafetyFactor = 1.5m;
	public const decimal DefaultScAutoFloorWindowDays   = 1m;

	// §3.2 — minimum swap size, redefined AGAIN under dev feedback (2026-07-08, same day as D-SW.11): a pure
	// "net > 0" floor (the first D-SW.11 cut) is mathematically valid but economically absurd — it lets a
	// player pay almost entirely in fees to receive a handful of satoshi (e.g. paying ~0.275 BTC-equivalent
	// to net a few satoshi back). The new floor is a VALUE guarantee: the player must receive AT LEAST as
	// much as they pay in total fees — i.e. net(gross) ≥ totalFee(gross), equivalently net ≥ gross/2 (since
	// gross = net + fee, net ≥ fee ⟺ net ≥ gross − net ⟺ 2×net ≥ gross). Solving net(gross) = totalFee(gross)
	// for gross directly (algebra, not reused from BaseFromNet — this is a different equation, "net equals
	// the fee" rather than "net equals a fixed target"):
	//   gross = 2×totalFee(gross) = 2×[networkFee×(1+fee) + fee×gross]
	//   gross×(1 − 2×fee) = 2×networkFee×(1+fee)
	//   gross = 2×networkFee×(1+fee) / (1 − 2×fee)
	// networkFee was the flat 0.1 scaffold (≈0.275 BTC at the 10% default) until ND.7 (D-ND7.9): it is now
	// the day's replayed MEDIAN for the current game date, so the floor scales with history (≈0.00055 BTC
	// at a 0.0002 median; 0 during the 2010-07→2011-04 zero-median era, where a single satoshi swaps
	// legally). The `fee >= 0.5m` guard is defensive only — SwapFeePercent is clamped to [1%,10%]
	// (D-SW.9), so `2×fee` never approaches 1 in practice.
	private static decimal MinSwapGrossBtcFor(decimal fee, decimal networkFee)
	{
		if (fee >= 0.5m) return 0m;
		return Money.Normalize(2m * networkFee * (1m + fee) / (1m - 2m * fee));
	}

	private const string CasinoNodeId = "casino";
	private const string PlayerNodeId = "player";

	public const string DirectionScToBtc = "sc_to_btc"; // Panel A — player buys BTC with SC (casino sells)
	public const string DirectionBtcToSc = "btc_to_sc"; // Panel B — player sells BTC for SC (casino buys)

	// On-chain display memos (Transaction.InputDataText) so wallet history panels can tell swap txs apart
	// from ordinary sends / pool payouts. The "swap:" prefix is the machine check; keep it stable.
	public const string SwapTxMemoScToBtc = "swap: casino desk SC→BTC";
	public const string SwapTxMemoBtcToSc = "swap: casino desk BTC→SC";
	public const string MethodManual     = "manual";
	public const string MethodAuto       = "auto";      // reserved for the future auto-swaps-scheduler / bots

	private const string StatePath = "user://casino_coin_swap_state.json";
	private const string TracePath = "user://logs/swap_desk_trace.csv";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	// Cap the history to keep the JSON / checkpoint bounded (oldest trimmed) — mirrors MaxTransferHistory /
	// MaxRechargeHistory on the other financial services.
	private const int MaxSwapHistory = 500;

	private CasinoScBalanceService    _casinoSc;
	private CalendarTimeService       _calendarTime;
	private BtcMarketDataService      _market;
	private PrincipalBalanceService   _principalBalance;
	private CasinoClientLedgerService _ledger;
	// Cheap facade over NetworkRoot's static world (the SimulationService pattern). Only touched from
	// RecomputeAvailability, which never runs before the deferred init — so the blockchain's lazy
	// EnsureInitialized is never triggered mid-autoload-boot (checkpoint restore order stays untouched).
	private NetworkRoot            _networkRoot;
	private bool _availabilityReady;

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
	public decimal MaxFeeDeviationPoints { get; private set; } = DefaultMaxFeeDeviationPoints;
	public bool    ScFloorEnabled          { get; private set; } = false;
	public decimal ScAutoFloorSafetyFactor { get; private set; } = DefaultScAutoFloorSafetyFactor;
	public decimal ScAutoFloorWindowDays   { get; private set; } = DefaultScAutoFloorWindowDays;

	public event Action SwapDeskChanged;

	public override void _Ready()
	{
		LoadState();
		_casinoSc         = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_calendarTime     = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_market           = GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService");
		_principalBalance = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_ledger           = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		_networkRoot      = new NetworkRoot();

		// §1.1 event set — availability is event-driven, never per-frame. Handlers no-op until the deferred
		// initial recompute has run (the checkpoint restore fires BalanceChanged during autoload boot, before
		// the blockchain world should be touched).
		if (_casinoSc != null) _casinoSc.BalanceChanged += OnCasinoScBalanceChanged;
		if (_market != null)   _market.MarketDayChanged += OnMarketDayChanged;
		NetworkRoot.BlockAccepted += OnBlockAccepted;
		CallDeferred(nameof(InitializeAvailability));

		// Built in pieces: a NESTED interpolated string does not inherit the outer handler's format provider,
		// and `decimal + "%"` formats with the current culture too — both would print "0,00" on a Spanish locale.
		GD.Print(string.Create(CultureInfo.InvariantCulture,
			$"[CasinoCoinSwapService] Ready — Fee={SwapFeePercent:F2}%  BtcReserve={DescribeReserve(BtcReserve, "BTC")}  ScReserve={DescribeReserve(ScReserve, "SC")}  ScFloor={DescribeScFloor()}  history={_swapHistory.Count}"));
	}

	public override void _ExitTree()
	{
		if (_casinoSc != null) _casinoSc.BalanceChanged -= OnCasinoScBalanceChanged;
		if (_market != null)   _market.MarketDayChanged -= OnMarketDayChanged;
		NetworkRoot.BlockAccepted -= OnBlockAccepted; // static event — must not outlive the autoload
	}

	private static string DescribeReserve(ReserveSetting reserve, string assetLabel) => reserve.UsePercent
		? string.Create(CultureInfo.InvariantCulture, $"{reserve.Percent}%")
		: string.Create(CultureInfo.InvariantCulture, $"{reserve.Amount} {assetLabel}");

	private string DescribeScFloor() => ScFloorEnabled
		? string.Create(CultureInfo.InvariantCulture, $"ON (safety={ScAutoFloorSafetyFactor:F2}, window={ScAutoFloorWindowDays:F1}d)")
		: "OFF";

	// THE accessor every quote/execution path uses (§3.1). Today it ignores clientId and returns the global
	// value; the rank system (future step) overrides per client HERE and nowhere else.
	public decimal GetSwapFeePercentFor(string clientId) => SwapFeePercent;

	// ---- Availability + first-funds gating (§1.1, SW.1) -----------------------------------------------------------

	public enum PanelDisableReason
	{
		None,             // panel enabled
		MarketNotBornYet, // game clock < BtcMarketDataService.FirstDataDateLocal — no exchange exists yet
		HaltDay,          // D-13.11 — real historical trading halt closes the whole desk
		NoCasinoBtc,      // Panel A: casino offered BTC below the smallest legal swap (MinDeliverableBtc)
		BtcSettling,      // Panel A: the casino OWNS enough BTC but it is settling (coinbase/payout change
		                  // awaiting its confirming block) — swaps unlock at the next block(s)
		NoCasinoSc        // Panel B: casino offered SC below the minimum swap's net payout
	}

	// Casino spendable BTC = confirmed, mature UTXOs across ALL its addresses (base + change rotation) minus
	// any UTXO already reserved by a pending outgoing tx — GetNodeSpendableBalance/GetSpendableUtxos semantics,
	// the same figure the CasinoFinances send panel shows. Cached; recomputed on the §1.1 event set only.
	public decimal CasinoBtcBalance { get; private set; }

	// UNBACKED pool payouts — owed by events whose backing coinbase is gone from the canonical chain (e.g.
	// lost a consensus race). The distribution retry will claim these from ANY spendable casino UTXO, so
	// the desk must treat them as already spent. (This raid is what made the desk briefly offer 30+ BTC
	// and then collapse to ~10 in the first playtest — SW.1 hardening.) Normally 0.
	public decimal CasinoBtcPoolObligation { get; private set; }

	// The casino's OWN BTC that exists economically but is not yet a spendable UTXO: its fee share still
	// locked inside an unspent pool coinbase, or the payout tx's change (or any incoming BTC) awaiting its
	// confirming block. Displayed as "settling" and counted in CasinoBtcOwnedTotal, but NEVER offered —
	// a Panel A swap must spend a CONFIRMED UTXO now (the engine does not chain unconfirmed spends).
	public decimal CasinoBtcSettling { get; private set; }

	// What the casino actually OWNS and can spend NOW — the only BTC the desk may offer or reserve against.
	public decimal CasinoBtcEquity =>
		Money.Normalize(Math.Max(0m, CasinoBtcBalance - CasinoBtcPoolObligation));

	// The casino's full economic BTC position (spendable equity + settling) — the honest "owned" display
	// figure, so the fee share of a freshly mined pool block reads as the casino's from block one.
	public decimal CasinoBtcOwnedTotal =>
		Money.Normalize(CasinoBtcEquity + CasinoBtcSettling);

	// The market-day step-function price (SC per BTC, D-13.2), carried forward over halts and frozen
	// post-history (D-13.5). Null before market birth. Cached at each availability recompute.
	public decimal? CurrentPriceSc { get; private set; }

	public bool IsPanelAEnabled { get; private set; }
	public bool IsPanelBEnabled { get; private set; }
	public PanelDisableReason PanelAReason { get; private set; } = PanelDisableReason.MarketNotBornYet;
	public PanelDisableReason PanelBReason { get; private set; } = PanelDisableReason.MarketNotBornYet;

	public decimal CasinoScMainBalance => _casinoSc?.MainBalance ?? 0m;

	// §2.3 R2 (SW.5) — "doses consumed" = the count of AUTO-recharge events (CasinoScBalanceService.
	// RechargeHistory, Reason == "auto") whose GameDateLocal falls within the last ScAutoFloorWindowDays of
	// GAME time (never wall-clock — CLAUDE.md Pattern 2). Exposed on its own (independent of ScFloorEnabled)
	// so the DEV UI can show the live breakdown BEHIND the final ScAutoFloor number — SafetyFactor alone is
	// unreadable without knowing this count and BankrollTarget too (dev feedback 2026-07-07: the same
	// SafetyFactor produces wildly different SC amounts depending on both). 0 if data is unavailable.
	public int ScAutoFloorDosesConsumed => GetScAutoFloorDosesConsumedFor(ScAutoFloorWindowDays);

	// Parameterized so the DEV UI can preview the doses count for a SpinBox value the dev hasn't applied
	// yet (SetScFloor), not only for the currently-committed ScAutoFloorWindowDays.
	public int GetScAutoFloorDosesConsumedFor(decimal windowDays)
	{
		if (_casinoSc == null || _calendarTime == null) return 0;
		DateTime windowStart = _calendarTime.CurrentLocalDateTime.AddDays(-(double)windowDays);
		return _casinoSc.RechargeHistory.Count(r => r.Reason == "auto" && r.GameDateLocal >= windowStart);
	}

	// §2.3 R2 (SW.5) — the recharge-pace auto floor: SafetyFactor × ScAutoFloorDosesConsumed × BankrollTarget.
	// Each dose is one BankrollTarget-sized draw the betting pace actually made, so the floor sizes itself to
	// what play has recently needed, with SafetyFactor as the margin above that raw historical draw. 0 when
	// disabled or data is unavailable. Full rationale + the R3 (drawdown-based) alternative: Documentation/
	// ProjectDesignManual.md Ch. 33.
	public decimal ScAutoFloor =>
		ScFloorEnabled && _casinoSc != null
			? Money.Normalize(ScAutoFloorSafetyFactor * ScAutoFloorDosesConsumed * _casinoSc.BankrollTarget)
			: 0m;

	// Effective SC floor = max(manual reserve, R2 auto floor when enabled) — the same max() composition as
	// TryAutoWithdraw's anti-ping-pong guard (§2.3).
	public decimal EffectiveScReserve =>
		Money.Normalize(Math.Max(ScReserve.ReserveFor(CasinoScMainBalance), ScAutoFloor));

	public decimal OfferedBtc =>
		Money.Normalize(Math.Max(0m, CasinoBtcEquity - BtcReserve.ReserveFor(CasinoBtcEquity)));

	public decimal OfferedSc =>
		Money.Normalize(Math.Max(0m, CasinoScMainBalance - EffectiveScReserve));

	// §4.1 — the smallest OfferedBtc for which any legal swap exists: the minimum swap's net delivery plus
	// the 0.1 network fee the casino pays on the send. Re-derived for the VALUE floor (2026-07-08, §3.2):
	// net(minGross) = minGross/2 = MinFee×(1+fee)/(1−2×fee) (half of MinSwapGrossBtcFor, by the floor's own
	// definition net = totalFee = gross − net). Unlike the superseded "net>0" floor this is fee-DEPENDENT
	// again — a higher casino cut requires a bigger minimum to still guarantee net ≥ fee — so it must read
	// the live fee (the player is the only client today; the rank system will parameterize this by client).
	public decimal MinDeliverableBtc
	{
		get
		{
			decimal fee = GetSwapFeePercentFor(PlayerNodeId) / 100m;
			if (fee >= 0.5m) return 0m;
			return Money.Normalize(CurrentNetworkFeeBtc * (2m - fee) / (1m - 2m * fee));
		}
	}

	// ND.7 (D-ND7.9) — the flat network-fee component every swap-desk formula reads: the day's replayed
	// MEDIAN for the CURRENT game date (0 pre-birth and during the 2010-07→2011-04 zero-median era).
	// Quotes recompute per keystroke and executions re-gate on fresh state, so a day boundary between
	// quote and click is already handled. Public: the CasinoCoinSwaps fee-breakdown UI reads it too.
	public decimal CurrentNetworkFeeBtc =>
		NetworkFeePolicy.MedianFeeFor(_calendarTime?.CurrentLocalDateTime ?? DateTime.Now);

	// §1.1 — Panel B's enable threshold: the net SC the minimum legal swap would pay out at the given price.
	// Same value-floor derivation as MinDeliverableBtc, expressed in SC: netSc(minGross) = priceSc ×
	// MinFee×(1+fee)/(1−2×fee) (ComputeBtcToScCore's netSc is exactly priceSc × the BTC-side net).
	public decimal MinScPayoutAt(decimal priceSc)
	{
		decimal fee = GetSwapFeePercentFor(PlayerNodeId) / 100m;
		if (fee >= 0.5m) return 0m;
		return Money.Normalize(priceSc * CurrentNetworkFeeBtc * (1m + fee) / (1m - 2m * fee));
	}

	// Deferred one frame past autoload boot (see _Ready) — the first legitimate touch of the blockchain world.
	private void InitializeAvailability()
	{
		_availabilityReady = true;
		RecomputeAvailability(notify: true);
	}

	// ── R3 (2026-07-28) — the SC-balance trigger must be COALESCED, never per-bet ──────────────────────
	// RecomputeAvailability is a CHAIN-side recompute: AggregateSpendable walks the casino's whole address
	// book, each address scanning the full UTXO set, and GetCasinoBtcSettlement re-derives every
	// undistributed pool event. CasinoScBalanceService.BalanceChanged, however, fires on EVERY settled bet
	// — since ND.8f that is all five clients, up to ~20 bets per frame — and an SC balance movement cannot
	// change one single chain-side figure. That made the swap desk the dominant term in the simulation's
	// frame time (measured at DevTimeScale 90: the bet engine sustained only 2 sim-seconds per frame, so
	// R2-C1's honest throttle held the clock at ~1/6 of the requested speed).
	//
	// So: the inputs that genuinely move the chain-side figures — a new block, a new market day — still
	// recompute IMMEDIATELY (they fire once per block / once per in-game day). An SC-balance change only
	// raises a dirty flag, drained at most every AvailabilityCoalesceSeconds in _Process. This is CLAUDE.md
	// Pattern 6's documented hybrid: the per-frame cost is one bool test, and the real work sits behind it.
	private const double AvailabilityCoalesceSeconds = 0.25;
	private bool   _availabilityDirty;
	private double _availabilityCoalesceTimer;

	public override void _Process(double delta)
	{
		if (!_availabilityDirty) return;

		_availabilityCoalesceTimer += delta;
		if (_availabilityCoalesceTimer < AvailabilityCoalesceSeconds) return;

		_availabilityCoalesceTimer = 0d;
		_availabilityDirty = false;
		RecomputeAvailability(notify: true);
	}

	// Immediate path — for inputs that actually move the chain/market side.
	private void OnAvailabilityInputChanged()
	{
		if (!_availabilityReady) return;
		_availabilityDirty = false;
		_availabilityCoalesceTimer = 0d;
		RecomputeAvailability(notify: true);
	}

	// Coalesced path — the casino's SC balance moved (a settled bet, a recharge, a loan draw).
	private void OnCasinoScBalanceChanged()
	{
		if (_availabilityReady) _availabilityDirty = true;
	}

	private void OnMarketDayChanged(MarketDay day) => OnAvailabilityInputChanged();
	private void OnBlockAccepted(Block block)      => OnAvailabilityInputChanged();

	// Recomputes the cached balance/price/panel states. With notify, fires SwapDeskChanged only when the
	// enablement snapshot actually changed (OfferedSc moves on every settled bet — consumers that want live
	// balances subscribe to CasinoScBalanceService.BalanceChanged themselves; this event is for desk state).
	private void RecomputeAvailability(bool notify)
	{
		var before = (CasinoBtcBalance, CasinoBtcPoolObligation, CasinoBtcSettling, CurrentPriceSc, IsPanelAEnabled, PanelAReason, IsPanelBEnabled, PanelBReason, _pendingDeliveries.Count);

		DateTime nowLocal = _calendarTime?.CurrentLocalDateTime ?? DateTime.Now;
		CasinoBtcBalance  = Money.Normalize(_networkRoot?.GetNodeSpendableBalance(CasinoNodeId) ?? 0m);
		(CasinoBtcSettling, CasinoBtcPoolObligation) = _networkRoot?.GetCasinoBtcSettlement() ?? (0m, 0m);
		CurrentPriceSc    = _market?.GetEffectivePriceUsd(nowLocal);

		// Drop deliveries whose confirming block arrived (§4.4 — the pending row clears itself).
		_pendingDeliveries.RemoveAll(d => !(_networkRoot?.IsTransactionPending(d.TxId) ?? false));

		if (_market == null || !_market.IsMarketBorn(nowLocal) || CurrentPriceSc is not decimal price)
		{
			SetPanelStates(PanelDisableReason.MarketNotBornYet, PanelDisableReason.MarketNotBornYet);
		}
		else if (_market.IsHaltDay(nowLocal))
		{
			SetPanelStates(PanelDisableReason.HaltDay, PanelDisableReason.HaltDay);
		}
		else
		{
			// Panel A: distinguish "no BTC at all" from "owned BTC is settling" — with the settling funds
			// counted the offer WOULD clear the minimum, so tell the player it unlocks at the next block(s).
			decimal ownedIfSettled   = CasinoBtcOwnedTotal;
			decimal offeredIfSettled = Money.Normalize(Math.Max(0m, ownedIfSettled - BtcReserve.ReserveFor(ownedIfSettled)));
			PanelDisableReason panelA =
				OfferedBtc >= MinDeliverableBtc     ? PanelDisableReason.None :
				offeredIfSettled >= MinDeliverableBtc ? PanelDisableReason.BtcSettling :
				PanelDisableReason.NoCasinoBtc;

			SetPanelStates(
				panelA,
				OfferedSc >= MinScPayoutAt(price) ? PanelDisableReason.None : PanelDisableReason.NoCasinoSc);
		}

		var after = (CasinoBtcBalance, CasinoBtcPoolObligation, CasinoBtcSettling, CurrentPriceSc, IsPanelAEnabled, PanelAReason, IsPanelBEnabled, PanelBReason, _pendingDeliveries.Count);
		if (notify && !before.Equals(after))
			SwapDeskChanged?.Invoke();
	}

	private void SetPanelStates(PanelDisableReason panelA, PanelDisableReason panelB)
	{
		PanelAReason    = panelA;
		PanelBReason    = panelB;
		IsPanelAEnabled = panelA == PanelDisableReason.None;
		IsPanelBEnabled = panelB == PanelDisableReason.None;
	}

	// ---- Quotes (§4.1/§4.2 — pure, the UI calls these per keystroke; execution re-checks in SW.3/SW.4) -------------

	// One live quote. InputAmount/GrossConverted/FeeCharged/NetOut are in each side's natural asset (Panel A:
	// SC in → BTC figures; Panel B: BTC in → SC figures). MaxInput is the binding maximum for the input field
	// with MaxLimitedBy naming WHOSE balance binds (§4.3 — players must understand whose balance runs out).
	public sealed class SwapQuote
	{
		public decimal InputAmount    { get; init; }
		public decimal PriceUsed      { get; init; }
		public decimal GrossConverted { get; init; }
		public decimal FeeCharged     { get; init; }
		public decimal NetOut         { get; init; }
		public decimal MinInput       { get; init; }   // §3.2 minimum swap size (net > 0 floor), in the input's asset
		public decimal MaxInput       { get; init; }
		public string  MaxLimitedBy   { get; init; } = string.Empty;
		public bool    IsValid        { get; init; }
		public PanelDisableReason PanelState { get; init; } // != None → the desk itself refuses the panel
	}

	public decimal PlayerScMainBalance   => _principalBalance?.CurrentBalance ?? 0m;
	public decimal PlayerSpendableBtc    => Money.Normalize(_networkRoot?.GetNodeSpendableBalance(PlayerNodeId) ?? 0m);

	// Panel A — player pays S SC, casino delivers net BTC on-chain (§4.1).
	public SwapQuote QuoteScToBtc(string clientId, decimal scAmount)
	{
		decimal price = CurrentPriceSc ?? 0m;
		decimal fee   = GetSwapFeePercentFor(clientId) / 100m;
		decimal maxDev = MaxFeeDeviationPoints / 100m;
		decimal networkFee = CurrentNetworkFeeBtc;
		if (PanelAReason != PanelDisableReason.None || price <= 0m)
			return new SwapQuote { InputAmount = scAmount, PanelState = PanelAReason };

		(decimal grossBtc, decimal feeBtc, decimal netBtc) = ComputeScToBtcCore(scAmount, price, fee, maxDev, networkFee);

		// Casino-side cap: netBtc + the network fee must fit in OfferedBtc; player-side cap: his Main Balance.
		decimal minSc       = FindMinScInput(price, fee, maxDev, networkFee);
		// Floor at minSc (dev feedback 2026-07-08): this cap and the panel's own enable gate (RecomputeAvailability,
		// OfferedBtc >= MinDeliverableBtc) are two INDEPENDENT derivations of "can the casino cover the minimum
		// swap" — each with its own Money.Normalize truncation — so they can disagree by a few satoshi right at
		// the boundary. Since PanelAReason == None here already proves (via the enable gate) that the casino can
		// afford at least minSc, never let this independently-truncated estimate report LESS than that — only
		// the (untouched, exact, no-formula-involved) player-side cap below may legitimately keep Max < Min.
		decimal casinoMaxSc = Money.Normalize(Math.Max(BaseFromNet(OfferedBtc - networkFee, fee, networkFee) * price, minSc));
		decimal playerMaxSc = PlayerScMainBalance;
		bool casinoBinds    = casinoMaxSc < playerMaxSc;

		return new SwapQuote
		{
			InputAmount    = scAmount,
			PriceUsed      = price,
			GrossConverted = grossBtc,
			FeeCharged     = feeBtc,
			NetOut         = netBtc,
			MinInput       = minSc,
			MaxInput       = Money.Normalize(Math.Min(playerMaxSc, casinoMaxSc)),
			MaxLimitedBy   = casinoBinds ? "casino BTC available" : "your Main Balance",
			IsValid        = scAmount >= minSc && scAmount <= Math.Min(playerMaxSc, casinoMaxSc) && netBtc > 0m,
			PanelState     = PanelDisableReason.None
		};
	}

	// Panel B — player parts with B BTC TOTAL (0.1 network fee inside it), casino credits net SC (§4.2).
	public SwapQuote QuoteBtcToSc(string clientId, decimal btcAmount)
	{
		decimal price = CurrentPriceSc ?? 0m;
		decimal fee   = GetSwapFeePercentFor(clientId) / 100m;
		decimal maxDev = MaxFeeDeviationPoints / 100m;
		decimal networkFee = CurrentNetworkFeeBtc;
		if (PanelBReason != PanelDisableReason.None || price <= 0m)
			return new SwapQuote { InputAmount = btcAmount, PanelState = PanelBReason };

		(decimal grossSc, decimal feeSc, decimal netSc) = ComputeBtcToScCore(btcAmount, price, fee, maxDev, networkFee);

		// Casino-side cap: netSc ≤ OfferedSc (invert the same net-of-fee curve, in BTC terms); player-side
		// cap: his spendable BTC (confirmed − pending outgoing).
		decimal minGross     = FindMinBtcInput(price, fee, maxDev, networkFee);
		// Floor at minGross — same reasoning as Panel A above: the enable gate (OfferedSc >= MinScPayoutAt)
		// and this independent BaseFromNet inversion can disagree by a few satoshi at the boundary, worse at
		// low BTC prices (a single BTC-satoshi moves the SC-side net by less than one SC-satoshi there, so the
		// truncation gap is wider). PanelBReason == None already proves the casino can cover minGross.
		decimal casinoMaxBtc = Money.Normalize(Math.Max(BaseFromNet(OfferedSc / price, fee, networkFee), minGross));
		decimal playerMaxBtc = PlayerSpendableBtc;
		bool casinoBinds     = casinoMaxBtc < playerMaxBtc;

		return new SwapQuote
		{
			InputAmount    = btcAmount,
			PriceUsed      = price,
			GrossConverted = grossSc,
			FeeCharged     = feeSc,
			NetOut         = netSc,
			MinInput       = minGross,
			MaxInput       = Money.Normalize(Math.Min(playerMaxBtc, casinoMaxBtc)),
			MaxLimitedBy   = casinoBinds ? "casino SC available" : "your BTC balance",
			IsValid        = btcAmount >= minGross && btcAmount <= Math.Min(playerMaxBtc, casinoMaxBtc) && netSc > 0m,
			PanelState     = PanelDisableReason.None
		};
	}

	// ---- Reverse quotes — "I want to receive exactly X" reactive inputs (dev feedback 2026-07-07) -------------------
	// Both invert the forward quote's net(base) curve via BaseFromNet (linear under the additive fee model,
	// 2026-07-08 — mathematically exact: net(base) is strictly increasing in base for fee<1, so BaseFromNet
	// returns the ONE base whose net equals the target precisely, no piecewise regions needed), then replay
	// the result through the FORWARD quote. Every clamp/IsValid/MaxLimitedBy rule is therefore evaluated in
	// exactly one place — the reverse quote can never disagree with the forward one, and a desired amount the
	// casino/player can't actually cover surfaces the normal invalid state (correct MaxLimitedBy reason)
	// instead of a second, possibly-inconsistent set of rules.

	// Smallest representable unit in either currency (8-decimal convention, CLAUDE.md Money Handling).
	private const decimal OneSatoshi = 0.00000001m;
	// Safety bound on the exact-match nudge loops below — normal convergence takes 1–3 iterations; this
	// only trips if the desired amount is unreachable at all (the loop still terminates safely either way).
	private const int MaxExactMatchIterations = 10;

	// Panel A reverse — "I want to receive exactly this much BTC" → the SC I'd need to pay.
	public SwapQuote QuoteScToBtcForReceivedBtc(string clientId, decimal desiredNetBtc)
	{
		decimal price = CurrentPriceSc ?? 0m;
		decimal fee   = GetSwapFeePercentFor(clientId) / 100m;
		if (PanelAReason != PanelDisableReason.None || price <= 0m || desiredNetBtc <= 0m)
			return QuoteScToBtc(clientId, 0m);

		decimal grossBtc   = BaseFromNet(desiredNetBtc, fee, CurrentNetworkFeeBtc);
		decimal requiredSc = Money.Normalize(grossBtc * price);
		SwapQuote quote = QuoteScToBtc(clientId, requiredSc);

		// Exact-match nudge (dev feedback 2026-07-07): the SC↔BTC round-trip through two independent
		// 8-decimal Money.Normalize steps can under-deliver by a few satoshi (worse at low market prices —
		// see ProjectDesignManual Ch. 33's rounding note). Rather than silently shortchanging the requested
		// receive amount, nudge requiredSc up by the BTC shortfall's SC-equivalent (or one SC-satoshi,
		// whichever is larger) until NetOut is AT LEAST desiredNetBtc — the negligible surplus folds
		// invisibly into the pay amount, exactly as intended: the player always gets what they asked for.
		int iterations = 0;
		while (quote.NetOut < desiredNetBtc && iterations++ < MaxExactMatchIterations)
		{
			decimal shortfall = desiredNetBtc - quote.NetOut;
			decimal bump = Math.Max(Money.Normalize(shortfall * price), OneSatoshi);
			requiredSc = Money.Normalize(requiredSc + bump);
			quote = QuoteScToBtc(clientId, requiredSc);
		}
		return quote;
	}

	// Panel B reverse — "I want to receive exactly this much SC" → the BTC (total, network fee included) I'd
	// need to send. desiredNetSc is converted to its BTC-equivalent before inverting, since §4.2's curve is
	// naturally expressed in BTC (B is BTC; netSc = price × net(B)).
	public SwapQuote QuoteBtcToScForReceivedSc(string clientId, decimal desiredNetSc)
	{
		decimal price = CurrentPriceSc ?? 0m;
		decimal fee   = GetSwapFeePercentFor(clientId) / 100m;
		if (PanelBReason != PanelDisableReason.None || price <= 0m || desiredNetSc <= 0m)
			return QuoteBtcToSc(clientId, 0m);

		decimal targetBtcEquivalent = Money.Normalize(desiredNetSc / price);
		decimal requiredBtc = BaseFromNet(targetBtcEquivalent, fee, CurrentNetworkFeeBtc);
		SwapQuote quote = QuoteBtcToSc(clientId, requiredBtc);

		// Exact-match nudge — same rationale as Panel A's, mirrored: nudge requiredBtc up by the SC
		// shortfall's BTC-equivalent (or one BTC-satoshi) until NetOut is AT LEAST desiredNetSc.
		int iterations = 0;
		while (quote.NetOut < desiredNetSc && iterations++ < MaxExactMatchIterations)
		{
			decimal shortfallSc = desiredNetSc - quote.NetOut;
			decimal bumpBtc = Math.Max(Money.Normalize(shortfallSc / price), OneSatoshi);
			requiredBtc = Money.Normalize(requiredBtc + bumpBtc);
			quote = QuoteBtcToSc(clientId, requiredBtc);
		}
		return quote;
	}

	// Inverts net(base) = base×(1−fee) − networkFee×(1+fee) — the additive fee model (2026-07-08, supersedes
	// D-SW.1's max()-based formula). net(base) is strictly increasing in base for fee<1, so for a POSITIVE
	// targetNet this returns the EXACT base whose net equals it, not merely a bound — one linear line covers
	// every case (the old model needed a piecewise floor-region/percentage-region split; this one doesn't,
	// since there is no more max() to create a kink in the curve). The `targetNet <= 0m` guard is still
	// required for the Max-clamp callers (e.g. "the casino's offered BTC minus the network fee it must pay"
	// can itself be ≤ 0 when the casino is nearly out of funds) — that means literally no swap is affordable,
	// so the correct answer is 0, not the small positive base the raw inversion would otherwise imply.
	private static decimal BaseFromNet(decimal targetNet, decimal fee, decimal networkFee)
	{
		if (targetNet <= 0m || fee >= 1m) return 0m;
		return Money.Normalize((targetNet + networkFee * (1m + fee)) / (1m - fee));
	}

	// ---- Pure core math (no clamps, no validity) — shared by the public quotes AND the minimum-finder
	// helpers below, so the latter can verify a candidate WITHOUT calling back into QuoteScToBtc/QuoteBtcToSc
	// (which would recurse: those methods call FindMin*Input for their own MinInput field).

	// maxDeviationFraction = MaxFeeDeviationPoints/100 (D-SW.12). The cap is applied to the CASINO'S OWN CUT
	// only — never to the flat network cost, which is always charged in full (it's a real pass-through cost,
	// not margin). This is deliberate: clamping the network fee itself into a percentage cap would create an
	// unavoidable conflict near the minimum swap size (the flat fee alone can exceed nominal% of a tiny base,
	// so a "never below cost" floor and a "never above nominal+points" ceiling on the SAME combined total
	// would sometimes be mutually impossible to satisfy). Capping only the casino's cut sidesteps that: the
	// floor (casino margin ≥ 0) and the ceiling (casino margin ≤ (fee+maxDeviationFraction)×gross) can never
	// conflict since maxDeviationFraction ≥ 0. `effectiveMarginPercent = casinoFee/gross×100` (the UI's own
	// metric, which already excludes the network fee) is therefore bounded in [0, SwapFeePercent+MaxFeeDeviationPoints]
	// by construction, for every swap size — see ProjectDesignManual Ch. 34 §34.4.
	private static (decimal grossBtc, decimal feeBtc, decimal netBtc) ComputeScToBtcCore(decimal scAmount, decimal price, decimal fee, decimal maxDeviationFraction, decimal networkFee)
	{
		decimal grossBtc = Money.Normalize(scAmount / price);
		// Additive fee model (2026-07-08, supersedes D-SW.1): the casino's %-cut is fee×(gross+networkFee) —
		// its own cut ON TOP of the network cost, summed, never max()'d. See ProjectDesignManual Ch. 34 / plan §3.1a.
		decimal casinoFeeUncapped = Money.Normalize(fee * (grossBtc + networkFee));
		decimal casinoFeeCap      = Money.Normalize((fee + maxDeviationFraction) * grossBtc);
		decimal casinoFee = Math.Max(0m, Math.Min(casinoFeeUncapped, casinoFeeCap));
		decimal feeBtc = Money.Normalize(networkFee + casinoFee);
		decimal netBtc = Money.Normalize(grossBtc - feeBtc);
		return (grossBtc, feeBtc, netBtc);
	}

	private static (decimal grossSc, decimal feeSc, decimal netSc) ComputeBtcToScCore(decimal btcAmount, decimal price, decimal fee, decimal maxDeviationFraction, decimal networkFee)
	{
		decimal grossSc = Money.Normalize(btcAmount * price);
		// Additive fee model — same logic as Panel A, expressed in SC: the network cost's SC-equivalent
		// (networkFee × price) is a flat pass-through; the casino's %-cut is capped the same way, in SC terms.
		decimal networkFeeSc = Money.Normalize(networkFee * price);
		decimal casinoFeeUncapped = Money.Normalize(fee * (grossSc + networkFeeSc));
		decimal casinoFeeCap      = Money.Normalize((fee + maxDeviationFraction) * grossSc);
		decimal casinoFee = Math.Max(0m, Math.Min(casinoFeeUncapped, casinoFeeCap));
		decimal feeSc = Money.Normalize(networkFeeSc + casinoFee);
		decimal netSc = Money.Normalize(grossSc - feeSc);
		return (grossSc, feeSc, netSc);
	}

	// ---- Minimum-input finders (dev feedback 2026-07-08) — the MIN button/quote gap fix -----------------------------
	// `MinSwapGrossBtcFor`'s analytical estimate assumes exact arithmetic, but `Money.Normalize` TRUNCATES
	// (MidpointRounding.ToZero, never rounds up) at every step of the grossBtc→feeBtc→netBtc chain — three
	// compounding truncations can shave the intended "net = fee" value-floor target to just under it. Verify
	// the analytical estimate against the REAL (truncating) core math and nudge up by one satoshi until the
	// value floor (net ≥ feeCharged, i.e. the player nets back at least as much as they paid in fees) is
	// actually satisfied — the same exact-match pattern the reverse "receive X" quotes already use, just
	// targeting "net covers its own fee" instead of a specific desired net.
	// D-SW.12's deviation cap only ever REDUCES the charged fee relative to the pure additive model (never
	// increases it — see the two-sided clamp in ComputeScToBtcCore/ComputeBtcToScCore), so a gross that
	// satisfies "net ≥ fee" under the uncapped additive formula still satisfies it (with more slack) once the
	// cap is applied — MinSwapGrossBtcFor's analytical estimate (derived from the uncapped formula) therefore
	// remains a safe, if occasionally slightly conservative, starting point under the cap too.
	private static decimal FindMinScInput(decimal price, decimal fee, decimal maxDeviationFraction, decimal networkFee)
	{
		decimal scAmount = Money.Normalize(MinSwapGrossBtcFor(fee, networkFee) * price);
		int iterations = 0;
		while (iterations++ < MaxExactMatchIterations)
		{
			var (_, feeBtc, netBtc) = ComputeScToBtcCore(scAmount, price, fee, maxDeviationFraction, networkFee);
			if (netBtc > 0m && netBtc >= feeBtc) break;
			scAmount = Money.Normalize(scAmount + OneSatoshi);
		}
		return scAmount;
	}

	private static decimal FindMinBtcInput(decimal price, decimal fee, decimal maxDeviationFraction, decimal networkFee)
	{
		decimal btcAmount = MinSwapGrossBtcFor(fee, networkFee);
		int iterations = 0;
		while (iterations++ < MaxExactMatchIterations)
		{
			var (_, feeSc, netSc) = ComputeBtcToScCore(btcAmount, price, fee, maxDeviationFraction, networkFee);
			if (netSc > 0m && netSc >= feeSc) break;
			btcAmount = Money.Normalize(btcAmount + OneSatoshi);
		}
		return btcAmount;
	}

	// ---- Execution — Panel A (SC → BTC, §4.1 / SW.3) ---------------------------------------------------------------

	// A swap's on-chain BTC leg awaiting its confirming block (§4.4). In-memory only, deliberately: an app
	// restart discards the mempool AND reverts the SC balances to the last block, so both legs unwind
	// together — persisting this list would fabricate deliveries the restarted world never made.
	public sealed class PendingBtcDelivery
	{
		public string  TxId      { get; init; } = string.Empty;
		public string  ClientId  { get; init; } = "player";
		public string  Direction { get; init; } = DirectionScToBtc;
		public decimal AmountBtc { get; init; }
	}

	private readonly List<PendingBtcDelivery> _pendingDeliveries = new();
	public IReadOnlyList<PendingBtcDelivery> PendingBtcDeliveries => _pendingDeliveries;

	// The full §4.1 pipeline. Clamps are re-validated service-side (the UI validates first): the input is
	// hard-clamped to the binding maximum (§4.3, TriggerManualDeposit's Math.Min safety) and the §3.2
	// minimum swap size is enforced. Legs: player Main −S (instant) → casino Main +S (instant, D-SW.3) →
	// casino → player base address on-chain send of netBtc (D-SW.6), carrying the day's replayed median as the
	// network fee (ND.7 / D-ND7.9 — was a flat 0.1 under D-SW.1). The casino's wallet funds that on-chain fee,
	// but NOT out of its margin as the inclusive model had it: under D-SW.11 it was already collected from the
	// player inside quote.FeeCharged. A failed broadcast unwinds both SC legs — no partial swap ever commits.
	public bool TryExecuteScToBtc(string clientId, decimal scAmount, out string error)
	{
		error = string.Empty;
		if (_networkRoot == null || _principalBalance == null || _casinoSc == null)
		{
			error = "Swap desk unavailable.";
			return false;
		}

		// Re-gate on fresh state before committing money (§1.1 — a panel can run dry mid-session).
		RecomputeAvailability(notify: false);
		SwapQuote probe = QuoteScToBtc(clientId, 0m);
		if (probe.PanelState != PanelDisableReason.None)
		{
			error = "The swap desk is closed for this panel.";
			return false;
		}

		scAmount = Money.Normalize(scAmount);
		if (scAmount <= 0m)
		{
			error = "Enter a positive SC amount.";
			return false;
		}
		if (probe.MaxInput < probe.MinInput)
		{
			error = string.Create(CultureInfo.InvariantCulture,
				$"No swap currently possible — minimum is {probe.MinInput:N8} SC but only {probe.MaxInput:N8} SC is available.");
			return false;
		}
		// Clamp into the legal range in BOTH directions (user feedback 2026-07-07): an amount above the max
		// already clamped down and executed — a positive amount below the minimum now clamps UP to the
		// minimum and executes too, instead of being rejected. Symmetric with the MAX-side behavior (§4.3).
		scAmount = Math.Clamp(scAmount, probe.MinInput, probe.MaxInput);
		SwapQuote quote = QuoteScToBtc(clientId, scAmount);
		if (!quote.IsValid || quote.NetOut <= 0m)
		{
			error = string.Create(CultureInfo.InvariantCulture,
				$"Minimum swap is {quote.MinInput:N8} SC.");
			return false;
		}

		// 1. Player SC leg (instant).
		if (!_principalBalance.TryWithdraw(scAmount))
		{
			error = "Insufficient Main Balance.";
			return false;
		}

		// 2. Casino SC leg (instant, Main only — D-SW.3).
		_casinoSc.ReceiveSwapSc(scAmount);

		// 3. On-chain BTC leg: casino → the client node's BASE address (D-SW.6 — no fresh-address-per-swap).
		// ND.7 (D-ND7.9): the attached network fee is the day's replayed median (can be 0 in the zero-median era).
		var tx = _networkRoot.CreateAndBroadcastTransaction(CasinoNodeId, clientId, quote.NetOut, CurrentNetworkFeeBtc, SwapTxMemoScToBtc);
		if (tx == null)
		{
			// Unwind both SC legs — the swap never happened.
			if (!_casinoSc.TryPaySwapSc(scAmount))
				GD.PushWarning("[CasinoCoinSwapService] Rollback anomaly: casino could not return the SC leg.");
			_principalBalance.Deposit(scAmount);
			error = "On-chain send failed — swap aborted, no funds moved.";
			RecomputeAvailability(notify: true);
			return false;
		}

		_pendingDeliveries.Add(new PendingBtcDelivery
		{
			TxId      = tx.TransactionId,
			ClientId  = clientId,
			Direction = DirectionScToBtc,
			AmountBtc = quote.NetOut
		});

		// 4. Ledger (D-SW.4) + SwapRecord + trace + re-gate + SwapDeskChanged (inside RegisterSwap).
		_ledger?.RegisterSwapScOut(clientId, scAmount, _calendarTime?.CurrentUtcDateTime ?? DateTime.UtcNow, MethodManual);
		RegisterSwap(clientId, DirectionScToBtc, scAmount, quote.FeeCharged, quote.NetOut, quote.PriceUsed, MethodManual);
		return true;
	}

	// ---- Execution — Panel B (BTC → SC, §4.2 / SW.4) -----------------------------------------------------------

	// The full §4.2 pipeline. Clamps are re-validated service-side (input hard-clamped to the binding max,
	// §4.3; the §3.2 minimum swap size enforced). Legs run in the OPPOSITE order from Panel A: the on-chain
	// send is the CLIENT's own broadcast, so it goes FIRST — client → casino base address of (B − networkFee)
	// with that day's replayed median attached as the fee, the client paying exactly B total (ND.7 / D-ND7.9 —
	// was a flat 0.1 under D-SW.1, and it can be 0 in the zero-median era). The SC leg then fires INSTANTLY
	// without waiting for confirmation (§4.4 — a restart before the block reverts the mempool AND the SC
	// balances together, so crediting ahead of confirmation carries no real risk). Doing the broadcast first
	// means a failed send never needs a rollback — nothing has moved yet.
	public bool TryExecuteBtcToSc(string clientId, decimal btcAmount, out string error)
	{
		error = string.Empty;
		if (_networkRoot == null || _principalBalance == null || _casinoSc == null)
		{
			error = "Swap desk unavailable.";
			return false;
		}

		// Re-gate on fresh state before committing money (§1.1 — a panel can run dry mid-session).
		RecomputeAvailability(notify: false);
		SwapQuote probe = QuoteBtcToSc(clientId, 0m);
		if (probe.PanelState != PanelDisableReason.None)
		{
			error = "The swap desk is closed for this panel.";
			return false;
		}

		btcAmount = Money.Normalize(btcAmount);
		if (btcAmount <= 0m)
		{
			error = "Enter a positive BTC amount.";
			return false;
		}
		if (probe.MaxInput < probe.MinInput)
		{
			error = string.Create(CultureInfo.InvariantCulture,
				$"No swap currently possible — minimum is {probe.MinInput:N8} BTC but only {probe.MaxInput:N8} BTC is available.");
			return false;
		}
		// Clamp into the legal range in BOTH directions (user feedback 2026-07-07) — symmetric with Panel A.
		btcAmount = Math.Clamp(btcAmount, probe.MinInput, probe.MaxInput);
		SwapQuote quote = QuoteBtcToSc(clientId, btcAmount);
		if (!quote.IsValid || quote.NetOut <= 0m)
		{
			error = string.Create(CultureInfo.InvariantCulture,
				$"Minimum swap is {quote.MinInput:N8} BTC.");
			return false;
		}

		// 1. On-chain BTC leg: client → the casino's BASE address. `amount` = quote.InputAmount − networkFee
		// (what the casino receives); `fee` = the day's replayed median (ND.7 / D-ND7.9 — was the flat 0.1;
		// the client's UTXOs cover need = amount + fee = the full B), matching the §3.3 worked example's shape.
		decimal networkFee = CurrentNetworkFeeBtc;
		decimal sendAmount = Money.Normalize(quote.InputAmount - networkFee);
		var tx = _networkRoot.CreateAndBroadcastTransaction(clientId, CasinoNodeId, sendAmount, networkFee, SwapTxMemoBtcToSc);
		if (tx == null)
		{
			error = "On-chain send failed — swap aborted, no funds moved.";
			return false;
		}

		// 2. Casino SC leg (instant, Main only — D-SW.3). Cannot fail in practice: quote.NetOut ≤ OfferedSc ≤
		// CasinoScMainBalance by construction (freshly re-gated above), but guard anyway — if it somehow did,
		// the on-chain send is already broadcast and cannot be un-sent (logged, not silently swallowed).
		if (!_casinoSc.TryPaySwapSc(quote.NetOut))
		{
			GD.PushWarning("[CasinoCoinSwapService] TryExecuteBtcToSc: casino could not pay the SC leg after the BTC send was already broadcast — desk state may be inconsistent.");
			error = "Swap failed after broadcasting — please check your balances.";
			return false;
		}
		_principalBalance.Deposit(quote.NetOut);

		_pendingDeliveries.Add(new PendingBtcDelivery
		{
			TxId      = tx.TransactionId,
			ClientId  = clientId,
			Direction = DirectionBtcToSc,
			AmountBtc = quote.InputAmount
		});

		// 3. Ledger (D-SW.4) + SwapRecord + trace + re-gate + SwapDeskChanged (inside RegisterSwap).
		_ledger?.RegisterSwapScIn(clientId, quote.NetOut, _calendarTime?.CurrentUtcDateTime ?? DateTime.UtcNow, MethodManual);
		RegisterSwap(clientId, DirectionBtcToSc, quote.InputAmount, quote.FeeCharged, quote.NetOut, quote.PriceUsed, MethodManual);
		return true;
	}

	// ---- Setters (§2.2 — the single mutation API the DEV scenes AND the future scheduler share) -------------------

	public void SetBtcReserve(bool usePercent, decimal percent, decimal amount)
	{
		BtcReserve = SanitizeReserve(usePercent, percent, amount);
		CommitKnobChange("btc_reserve_set");
	}

	public void SetScReserve(bool usePercent, decimal percent, decimal amount)
	{
		ScReserve = SanitizeReserve(usePercent, percent, amount);
		CommitKnobChange("sc_reserve_set");
	}

	// Clamps to [1,10] (D-SW.9) — the UI SpinBox refuses out-of-range values first; this is the safety net.
	public void SetSwapFeePercent(decimal percent)
	{
		SwapFeePercent = Math.Clamp(Money.Normalize(percent), MinSwapFeePercent, MaxSwapFeePercent);
		CommitKnobChange("fee_set");
	}

	// D-SW.12 — clamps to [0,20] points. 0 locks the effective % to nominal exactly (casino absorbs the full
	// network cost on small swaps); higher values allow more deviation before the cap engages.
	public void SetMaxFeeDeviationPoints(decimal points)
	{
		MaxFeeDeviationPoints = Math.Clamp(Money.Normalize(points), MinMaxFeeDeviationPoints, MaxMaxFeeDeviationPoints);
		CommitKnobChange("max_fee_deviation_set");
	}

	// R2 auto-floor toggle + tunables (§2.3, SW.5). Non-positive inputs fall back to their defaults.
	public void SetScFloor(bool enabled, decimal safetyFactor, decimal windowDays)
	{
		ScFloorEnabled          = enabled;
		ScAutoFloorSafetyFactor = safetyFactor > 0m ? Money.Normalize(safetyFactor) : DefaultScAutoFloorSafetyFactor;
		ScAutoFloorWindowDays   = windowDays > 0m ? Money.Normalize(windowDays) : DefaultScAutoFloorWindowDays;
		CommitKnobChange("sc_floor_set");
	}

	// Shared knob-change tail: persist, re-gate (a reserve/fee change moves the offered figures and can flip a
	// panel), trace the new state, notify once.
	private void CommitKnobChange(string traceEvent)
	{
		SaveState();
		if (_availabilityReady) RecomputeAvailability(notify: false);
		AppendTrace(traceEvent, string.Empty, 0m, 0m, 0m, 0m);
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
		if (_availabilityReady) RecomputeAvailability(notify: false); // a swap consumes offered funds — re-gate now
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
				CasinoScMainBalance, CasinoBtcEquity,
				EffectiveScReserve, BtcReserve.ReserveFor(CasinoBtcEquity),
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
		public decimal MaxFeeDeviationPoints { get; set; } = DefaultMaxFeeDeviationPoints;
		public bool    ScFloorEnabled    { get; set; }
		public decimal ScAutoFloorSafetyFactor { get; set; } = DefaultScAutoFloorSafetyFactor;
		public decimal ScAutoFloorWindowDays   { get; set; } = DefaultScAutoFloorWindowDays;
		public List<SwapRecord> SwapHistory { get; set; } = new();
	}

	// Called by BlockSessionCheckpointService.CaptureCheckpoint() at each mined block (block = the only commit).
	public CheckpointState CaptureCheckpointState() => new CheckpointState
	{
		BtcReserve        = CloneReserve(BtcReserve),
		ScReserve         = CloneReserve(ScReserve),
		SwapFeePercent    = SwapFeePercent,
		MaxFeeDeviationPoints = MaxFeeDeviationPoints,
		ScFloorEnabled    = ScFloorEnabled,
		ScAutoFloorSafetyFactor = ScAutoFloorSafetyFactor,
		ScAutoFloorWindowDays   = ScAutoFloorWindowDays,
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
		MaxFeeDeviationPoints = Math.Clamp(Money.Normalize(state.MaxFeeDeviationPoints), MinMaxFeeDeviationPoints, MaxMaxFeeDeviationPoints);
		ScFloorEnabled          = state.ScFloorEnabled;
		ScAutoFloorSafetyFactor = state.ScAutoFloorSafetyFactor > 0m ? Money.Normalize(state.ScAutoFloorSafetyFactor) : DefaultScAutoFloorSafetyFactor;
		ScAutoFloorWindowDays   = state.ScAutoFloorWindowDays > 0m ? Money.Normalize(state.ScAutoFloorWindowDays) : DefaultScAutoFloorWindowDays;

		_swapHistory.Clear();
		foreach (var r in state.SwapHistory ?? new List<SwapRecord>())
		{
			if (r == null || r.GrossIn <= 0m) continue;
			_swapHistory.Add(SanitizeRecord(r));
		}
		if (_swapHistory.Count > MaxSwapHistory)
			_swapHistory.RemoveRange(0, _swapHistory.Count - MaxSwapHistory);

		SaveState();
		if (_availabilityReady) RecomputeAvailability(notify: false); // boot-time restore precedes the deferred init — skipped there
		GD.Print(string.Create(CultureInfo.InvariantCulture,
			$"[CasinoCoinSwapService] RESTORED from checkpoint — Fee={SwapFeePercent:F2}%  ScFloor={DescribeScFloor()}  history={_swapHistory.Count}"));
		SwapDeskChanged?.Invoke();
	}

	// Called by BlockSessionCheckpointService.ResetToPreGenesisDefaults() on every boot until the first real
	// block is mined. Forces the desk back to its true "first launch" state — reserves 0 (100% offered), fee
	// 10%, auto floor OFF, no history. Settings stick only at a block, like every other knob.
	public void ResetToPreGenesisDefaults()
	{
		BtcReserve        = new ReserveSetting();
		ScReserve         = new ReserveSetting();
		SwapFeePercent    = DefaultSwapFeePercent;
		MaxFeeDeviationPoints = DefaultMaxFeeDeviationPoints;
		ScFloorEnabled          = false;
		ScAutoFloorSafetyFactor = DefaultScAutoFloorSafetyFactor;
		ScAutoFloorWindowDays   = DefaultScAutoFloorWindowDays;
		_swapHistory.Clear();
		SaveState();
		if (_availabilityReady) RecomputeAvailability(notify: false); // boot-time reset precedes the deferred init — skipped there
		SwapDeskChanged?.Invoke();
	}

	// ---- Persistence --------------------------------------------------------------------------------------------

	private sealed class Snapshot
	{
		public ReserveSetting BtcReserve { get; set; } = new();
		public ReserveSetting ScReserve  { get; set; } = new();
		public decimal  SwapFeePercent    { get; set; } = DefaultSwapFeePercent;
		public decimal  MaxFeeDeviationPoints { get; set; } = DefaultMaxFeeDeviationPoints;
		public bool     ScFloorEnabled    { get; set; }
		public decimal  ScAutoFloorSafetyFactor { get; set; } = DefaultScAutoFloorSafetyFactor;
		public decimal  ScAutoFloorWindowDays   { get; set; } = DefaultScAutoFloorWindowDays;
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
			MaxFeeDeviationPoints = Math.Clamp(Money.Normalize(snapshot.MaxFeeDeviationPoints), MinMaxFeeDeviationPoints, MaxMaxFeeDeviationPoints);
			ScFloorEnabled          = snapshot.ScFloorEnabled;
			ScAutoFloorSafetyFactor = snapshot.ScAutoFloorSafetyFactor > 0m ? Money.Normalize(snapshot.ScAutoFloorSafetyFactor) : DefaultScAutoFloorSafetyFactor;
			ScAutoFloorWindowDays   = snapshot.ScAutoFloorWindowDays > 0m ? Money.Normalize(snapshot.ScAutoFloorWindowDays) : DefaultScAutoFloorWindowDays;

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
		MaxFeeDeviationPoints = DefaultMaxFeeDeviationPoints;
		ScFloorEnabled          = false;
		ScAutoFloorSafetyFactor = DefaultScAutoFloorSafetyFactor;
		ScAutoFloorWindowDays   = DefaultScAutoFloorWindowDays;
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
				MaxFeeDeviationPoints = MaxFeeDeviationPoints,
				ScFloorEnabled    = ScFloorEnabled,
				ScAutoFloorSafetyFactor = ScAutoFloorSafetyFactor,
				ScAutoFloorWindowDays   = ScAutoFloorWindowDays,
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
