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
	// §3.1 — rank-ready fee model. One percent governs BOTH swap directions; it INCLUDES the 0.1 BTC network
	// fee (the casino pays the on-chain fee out of its collected margin, never the player on top — D-SW.1).
	public const decimal DefaultSwapFeePercent = 10m;
	public const decimal MinSwapFeePercent     = 1m;   // D-SW.9 clamp range
	public const decimal MaxSwapFeePercent     = 10m;

	// §2.3 R1 placeholder SC floor: effective SC reserve ≥ N × CasinoScBalanceService.BankrollTarget when
	// enabled — a static stand-in for the SW.5 recharge-pace floor (R2). Dev-tunable N, default OFF.
	public const decimal DefaultScFloorMultiplier = 10m;

	// §3.2 — minimum swap size of 1 BTC gross: at the 10% max fee, fee ≥ NetworkFeePolicy.MinFee (0.1) exactly
	// at 1 BTC, so the casino never swaps at a loss. Surfaced as an honest "minimum swap is X" UI message (SW.6).
	public const decimal MinSwapGrossBtc = 1m;

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
	public bool    ScFloorEnabled    { get; private set; } = false;
	public decimal ScFloorMultiplier { get; private set; } = DefaultScFloorMultiplier;

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
		if (_casinoSc != null) _casinoSc.BalanceChanged += OnAvailabilityInputChanged;
		if (_market != null)   _market.MarketDayChanged += OnMarketDayChanged;
		NetworkRoot.BlockAccepted += OnBlockAccepted;
		CallDeferred(nameof(InitializeAvailability));

		GD.Print($"[CasinoCoinSwapService] Ready — Fee={SwapFeePercent:F2}%  BtcReserve={(BtcReserve.UsePercent ? BtcReserve.Percent + "%" : BtcReserve.Amount + " BTC")}  ScReserve={(ScReserve.UsePercent ? ScReserve.Percent + "%" : ScReserve.Amount + " SC")}  ScFloor={(ScFloorEnabled ? $"ON (N={ScFloorMultiplier:F2})" : "OFF")}  history={_swapHistory.Count}");
	}

	public override void _ExitTree()
	{
		if (_casinoSc != null) _casinoSc.BalanceChanged -= OnAvailabilityInputChanged;
		if (_market != null)   _market.MarketDayChanged -= OnMarketDayChanged;
		NetworkRoot.BlockAccepted -= OnBlockAccepted; // static event — must not outlive the autoload
	}

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

	// §2.3 — the R1 static floor value (0 when disabled). SW.5 composes the R2 auto floor into the same max().
	public decimal ScAutoFloor =>
		ScFloorEnabled ? Money.Normalize(ScFloorMultiplier * (_casinoSc?.BankrollTarget ?? 0m)) : 0m;

	// Effective SC floor = max(manual reserve, R1 floor when enabled) — the same max() composition as
	// TryAutoWithdraw's anti-ping-pong guard (§2.3).
	public decimal EffectiveScReserve =>
		Money.Normalize(Math.Max(ScReserve.ReserveFor(CasinoScMainBalance), ScAutoFloor));

	public decimal OfferedBtc =>
		Money.Normalize(Math.Max(0m, CasinoBtcEquity - BtcReserve.ReserveFor(CasinoBtcEquity)));

	public decimal OfferedSc =>
		Money.Normalize(Math.Max(0m, CasinoScMainBalance - EffectiveScReserve));

	// §4.1 — the smallest OfferedBtc for which any legal swap exists: the minimum swap's net delivery plus the
	// 0.1 network fee the casino pays on the send. At the 10% default fee: 0.9 + 0.1 = 1.0 BTC exactly.
	public decimal MinDeliverableBtc
	{
		get
		{
			decimal feeBtc = Math.Max(GetSwapFeePercentFor("player") / 100m * MinSwapGrossBtc, NetworkFeePolicy.MinFee);
			return Money.Normalize(MinSwapGrossBtc - feeBtc + NetworkFeePolicy.MinFee);
		}
	}

	// §1.1 — Panel B's enable threshold: the net SC the minimum legal swap (1 BTC gross) would pay out at the
	// given price. Below this, no swap the desk accepts can be honored.
	public decimal MinScPayoutAt(decimal priceSc)
	{
		decimal feeBtc = Math.Max(GetSwapFeePercentFor("player") / 100m * MinSwapGrossBtc, NetworkFeePolicy.MinFee);
		return Money.Normalize(priceSc * (MinSwapGrossBtc - feeBtc));
	}

	// Deferred one frame past autoload boot (see _Ready) — the first legitimate touch of the blockchain world.
	private void InitializeAvailability()
	{
		_availabilityReady = true;
		RecomputeAvailability(notify: true);
	}

	private void OnAvailabilityInputChanged()
	{
		if (_availabilityReady) RecomputeAvailability(notify: true);
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
		public decimal MinInput       { get; init; }   // the §3.2 1-BTC-gross floor, in the input's asset
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
		if (PanelAReason != PanelDisableReason.None || price <= 0m)
			return new SwapQuote { InputAmount = scAmount, PanelState = PanelAReason };

		decimal grossBtc = Money.Normalize(scAmount / price);
		decimal feeBtc   = Money.Normalize(Math.Max(fee * grossBtc, NetworkFeePolicy.MinFee));
		decimal netBtc   = Money.Normalize(grossBtc - feeBtc);

		// Casino-side cap: netBtc + 0.1 network fee must fit in OfferedBtc; player-side cap: his Main Balance.
		decimal casinoMaxSc = Money.Normalize(MaxGrossForNet(OfferedBtc - NetworkFeePolicy.MinFee, fee) * price);
		decimal playerMaxSc = PlayerScMainBalance;
		bool casinoBinds    = casinoMaxSc < playerMaxSc;
		decimal minSc       = Money.Normalize(MinSwapGrossBtc * price);

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
		if (PanelBReason != PanelDisableReason.None || price <= 0m)
			return new SwapQuote { InputAmount = btcAmount, PanelState = PanelBReason };

		decimal grossSc = Money.Normalize(btcAmount * price);
		decimal feeSc   = Money.Normalize(Math.Max(fee * grossSc, NetworkFeePolicy.MinFee * price));
		decimal netSc   = Money.Normalize(grossSc - feeSc);

		// Casino-side cap: netSc ≤ OfferedSc (invert the same net-of-fee curve, in BTC terms); player-side
		// cap: his spendable BTC (confirmed − pending outgoing).
		decimal casinoMaxBtc = Money.Normalize(MaxGrossForNet(OfferedSc / price, fee));
		decimal playerMaxBtc = PlayerSpendableBtc;
		bool casinoBinds     = casinoMaxBtc < playerMaxBtc;

		return new SwapQuote
		{
			InputAmount    = btcAmount,
			PriceUsed      = price,
			GrossConverted = grossSc,
			FeeCharged     = feeSc,
			NetOut         = netSc,
			MinInput       = MinSwapGrossBtc,
			MaxInput       = Money.Normalize(Math.Min(playerMaxBtc, casinoMaxBtc)),
			MaxLimitedBy   = casinoBinds ? "casino SC available" : "your BTC balance",
			IsValid        = btcAmount >= MinSwapGrossBtc && btcAmount <= Math.Min(playerMaxBtc, casinoMaxBtc) && netSc > 0m,
			PanelState     = PanelDisableReason.None
		};
	}

	// Inverts net(g) = g − max(fee·g, MinFee): the largest gross amount whose net stays within targetNet.
	// Piecewise because the 0.1 floor binds below gross = MinFee/fee and the percent above it (§3.2).
	private static decimal MaxGrossForNet(decimal targetNet, decimal fee)
	{
		if (targetNet <= 0m || fee >= 1m) return 0m;
		decimal floorRegionEnd = NetworkFeePolicy.MinFee / Math.Max(fee, 0.0001m);
		decimal netAtRegionEnd = floorRegionEnd - NetworkFeePolicy.MinFee;
		return targetNet <= netAtRegionEnd
			? targetNet + NetworkFeePolicy.MinFee
			: targetNet / (1m - fee);
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
	// casino → player base address on-chain send of netBtc with the 0.1 network fee paid by the casino out
	// of its margin (D-SW.1/D-SW.6). A failed broadcast unwinds both SC legs — no partial swap ever commits.
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

		scAmount = Money.Normalize(Math.Min(Money.Normalize(scAmount), probe.MaxInput));
		SwapQuote quote = QuoteScToBtc(clientId, scAmount);
		if (!quote.IsValid || quote.NetOut <= 0m)
		{
			error = string.Create(CultureInfo.InvariantCulture,
				$"Minimum swap is {quote.MinInput:N8} SC (1 BTC gross).");
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
		var tx = _networkRoot.CreateAndBroadcastTransaction(CasinoNodeId, clientId, quote.NetOut, NetworkFeePolicy.MinFee, SwapTxMemoScToBtc);
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

	// R1 placeholder floor toggle + multiplier (§2.3). Non-positive N falls back to the default.
	public void SetScFloor(bool enabled, decimal multiplier)
	{
		ScFloorEnabled    = enabled;
		ScFloorMultiplier = multiplier > 0m ? Money.Normalize(multiplier) : DefaultScFloorMultiplier;
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
		if (_availabilityReady) RecomputeAvailability(notify: false); // boot-time restore precedes the deferred init — skipped there
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
		if (_availabilityReady) RecomputeAvailability(notify: false); // boot-time reset precedes the deferred init — skipped there
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
