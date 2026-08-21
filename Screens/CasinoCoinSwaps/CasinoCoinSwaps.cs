using Godot;
using System;
using System.Globalization;
using Scripts.Finance;
using UI.StatusBar;
using GodotBlockchainPort.Blockchain;

// Step 13 (SW.2) — the casino's swap desk (D-13.6, plan §6): two panels (A: SC→BTC "Buy BTC", B: BTC→SC
// "Sell BTC"), casino-as-dealer. DISPLAY-ONLY in SW.2: availability, offered/reserve readouts, live quote
// previews (pure CasinoCoinSwapService.Quote* calls per keystroke), enable/disable states with reasons,
// halt-day / no-market / post-history states. SWAP buttons exist but stay disabled until SW.3/SW.4 wire
// execution. NO DEV controls here (D-SW.9 as amended): the fee knob + SC reserve live in
// CasinoGamblingFinances, the BTC reserve in CasinoFinances — this scene only displays the results.
// Layout: Ch. 29 fixed-footer pattern (ScTransactions reference) — footer Back OUTSIDE the scroll.
//
// Reactive dual inputs (dev feedback 2026-07-07): each panel has a "pay" AND a "receive" field; editing
// either recomputes the other via CasinoCoinSwapService's reverse quotes (QuoteScToBtcForReceivedBtc /
// QuoteBtcToScForReceivedSc), which invert the forward quote's fee curve exactly. Programmatically setting
// LineEdit.Text does NOT raise TextChanged in this Godot binding (confirmed by the existing FillMax method,
// which has always needed a manual EmitSignal to "replay" a MAX fill through the normal handler) — so
// syncing the OTHER field from a computed quote is inert and needs no reentrancy guard.
public partial class CasinoCoinSwaps : Control
{
	private CasinoCoinSwapService  _swapService;
	private CasinoScBalanceService _casinoSc;
	private BtcMarketDataService   _market;
	private CalendarTimeService    _calendarTime;
	private SceneManager           _sceneManager;

	private Label _priceLabel;
	private Label _deskStateLabel;
	private Label _feeLabel;

	private Label    _panelAAvailLabel;
	private LineEdit _panelAInput;
	private LineEdit _panelAReceiveInput;
	private Button   _panelAMaxBtn;
	private Button   _panelAMinBtn;
	private Button   _panelAReceiveMaxBtn;
	private Button   _panelAReceiveMinBtn;
	private Label    _panelAMaxLabel;
	private Label    _panelAQuoteLabel;
	private Button   _panelASwapBtn;
	private Label    _panelAReasonLabel;
	private Label    _panelAPendingLabel;
	private Label    _panelBPendingLabel;

	private Label    _panelBAvailLabel;
	private LineEdit _panelBInput;
	private LineEdit _panelBReceiveInput;
	private Button   _panelBMaxBtn;
	private Button   _panelBMinBtn;
	private Button   _panelBReceiveMaxBtn;
	private Button   _panelBReceiveMinBtn;
	private Label    _panelBMaxLabel;
	private Label    _panelBQuoteLabel;
	private Button   _panelBSwapBtn;
	private Label    _panelBReasonLabel;

	private VBoxContainer _swapsListVBox;

	// Which field is the SOURCE right now (the other one is derived/overwritten) — set by whichever field's
	// TextChanged last fired. Prevents the periodic/event RefreshAll() from clobbering a field the user is
	// actively typing into: the source field is only ever READ, never written, by either refresh path.
	private bool _panelALastEditedReceive;
	private bool _panelBLastEditedReceive;

	private double _refreshTimer;
	private const double RefreshInterval = 2.0;
	private const int MaxRecentSwapsShown = 20;

	private static readonly Color DisabledReasonColor = new Color(1f, 0.45f, 0.35f);
	private static readonly Color PendingColor        = new Color(1f, 0.9f, 0.4f);
	private static readonly Color QuoteOkColor        = new Color(0.6f, 1f, 0.6f);
	private static readonly Color QuoteBadColor       = new Color(1f, 0.75f, 0.4f);
	private static readonly Color GreyedColor         = new Color(0.6f, 0.6f, 0.6f);
	private static readonly Color NormalColor         = new Color(1f, 1f, 1f);

	public override void _Ready()
	{
		_swapService  = GetNodeOrNull<CasinoCoinSwapService>("/root/CasinoCoinSwapService");
		_casinoSc     = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_market       = GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService");
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_priceLabel     = GetNode<Label>("%PriceLabel");
		_deskStateLabel = GetNode<Label>("%DeskStateLabel");
		_feeLabel       = GetNode<Label>("%FeeLabel");

		_panelAAvailLabel     = GetNode<Label>("%PanelAAvailLabel");
		_panelAInput          = GetNode<LineEdit>("%PanelAInput");
		_panelAReceiveInput   = GetNode<LineEdit>("%PanelAReceiveInput");
		_panelAMaxBtn         = GetNode<Button>("%PanelAMaxBtn");
		_panelAMinBtn         = GetNode<Button>("%PanelAMinBtn");
		_panelAReceiveMaxBtn  = GetNode<Button>("%PanelAReceiveMaxBtn");
		_panelAReceiveMinBtn  = GetNode<Button>("%PanelAReceiveMinBtn");
		_panelAMaxLabel     = GetNode<Label>("%PanelAMaxLabel");
		_panelAQuoteLabel   = GetNode<Label>("%PanelAQuoteLabel");
		_panelASwapBtn      = GetNode<Button>("%PanelASwapBtn");
		_panelAReasonLabel  = GetNode<Label>("%PanelAReasonLabel");
		_panelAPendingLabel = GetNode<Label>("%PanelAPendingLabel");
		_panelBPendingLabel = GetNode<Label>("%PanelBPendingLabel");

		_panelBAvailLabel     = GetNode<Label>("%PanelBAvailLabel");
		_panelBInput          = GetNode<LineEdit>("%PanelBInput");
		_panelBReceiveInput   = GetNode<LineEdit>("%PanelBReceiveInput");
		_panelBMaxBtn         = GetNode<Button>("%PanelBMaxBtn");
		_panelBMinBtn         = GetNode<Button>("%PanelBMinBtn");
		_panelBReceiveMaxBtn  = GetNode<Button>("%PanelBReceiveMaxBtn");
		_panelBReceiveMinBtn  = GetNode<Button>("%PanelBReceiveMinBtn");
		_panelBMaxLabel     = GetNode<Label>("%PanelBMaxLabel");
		_panelBQuoteLabel   = GetNode<Label>("%PanelBQuoteLabel");
		_panelBSwapBtn      = GetNode<Button>("%PanelBSwapBtn");
		_panelBReasonLabel  = GetNode<Label>("%PanelBReasonLabel");

		_swapsListVBox = GetNode<VBoxContainer>("%SwapsListVBox");

		_panelAReasonLabel.AddThemeColorOverride("font_color", DisabledReasonColor);
		_panelBReasonLabel.AddThemeColorOverride("font_color", DisabledReasonColor);

		_panelAInput.TextChanged        += _ => { _panelALastEditedReceive = false; RefreshPanelAQuote(); };
		_panelAReceiveInput.TextChanged += _ => { _panelALastEditedReceive = true;  RefreshPanelAQuoteFromReceive(); };
		_panelBInput.TextChanged        += _ => { _panelBLastEditedReceive = false; RefreshPanelBQuote(); };
		_panelBReceiveInput.TextChanged += _ => { _panelBLastEditedReceive = true;  RefreshPanelBQuoteFromReceive(); };
		_panelAMaxBtn.Pressed        += () => { if (_swapService != null) FillPayExtreme(_panelAInput, _swapService.QuoteScToBtc, useMax: true); };
		_panelAMinBtn.Pressed        += () => { if (_swapService != null) FillPayExtreme(_panelAInput, _swapService.QuoteScToBtc, useMax: false); };
		_panelAReceiveMaxBtn.Pressed += () => { if (_swapService != null) FillReceiveExtreme(_panelAReceiveInput, _swapService.QuoteScToBtc, useMax: true); };
		_panelAReceiveMinBtn.Pressed += () => { if (_swapService != null) FillReceiveExtreme(_panelAReceiveInput, _swapService.QuoteScToBtc, useMax: false); };
		_panelBMaxBtn.Pressed        += () => { if (_swapService != null) FillPayExtreme(_panelBInput, _swapService.QuoteBtcToSc, useMax: true); };
		_panelBMinBtn.Pressed        += () => { if (_swapService != null) FillPayExtreme(_panelBInput, _swapService.QuoteBtcToSc, useMax: false); };
		_panelBReceiveMaxBtn.Pressed += () => { if (_swapService != null) FillReceiveExtreme(_panelBReceiveInput, _swapService.QuoteBtcToSc, useMax: true); };
		_panelBReceiveMinBtn.Pressed += () => { if (_swapService != null) FillReceiveExtreme(_panelBReceiveInput, _swapService.QuoteBtcToSc, useMax: false); };
		_panelASwapBtn.Pressed += OnPanelASwapPressed;
		_panelBSwapBtn.Pressed += OnPanelBSwapPressed;

		_panelAPendingLabel.AddThemeColorOverride("font_color", PendingColor);
		_panelBPendingLabel.AddThemeColorOverride("font_color", PendingColor);

		// Origin-aware back (the BetsHistoryExplorer / SF.4.2 pattern) — MainMenu and ScFinances both link here.
		GetNode<Button>("%BackBtn").Pressed += () =>
			_sceneManager?.Go(_sceneManager.PreviousScene ?? SceneManager.SceneId.MainMenu);

		if (_swapService != null) _swapService.SwapDeskChanged += RefreshAll;
		if (_casinoSc != null)    _casinoSc.BalanceChanged += RefreshAll;
		if (_market != null)      _market.MarketDayChanged += OnMarketDayChanged;

		RefreshAll();
	}

	public override void _ExitTree()
	{
		if (_swapService != null) _swapService.SwapDeskChanged -= RefreshAll;
		if (_casinoSc != null)    _casinoSc.BalanceChanged -= RefreshAll;
		if (_market != null)      _market.MarketDayChanged -= OnMarketDayChanged;
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer >= RefreshInterval)
		{
			_refreshTimer = 0;
			RefreshAll();
		}
	}

	private void OnMarketDayChanged(MarketDay day) => RefreshAll();

	// ── Header + panels ─────────────────────────────────────────────────────────

	private void RefreshAll()
	{
		if (!GodotObject.IsInstanceValid(this) || _swapService == null) return;

		RefreshHeader();
		RefreshPanelStates();
		// Recompute from whichever field is the current SOURCE (§ above) — never the field the user might
		// be actively typing into right now.
		if (_panelALastEditedReceive) RefreshPanelAQuoteFromReceive(); else RefreshPanelAQuote();
		if (_panelBLastEditedReceive) RefreshPanelBQuoteFromReceive(); else RefreshPanelBQuote();
		BuildRecentSwapsList();
	}

	private void RefreshHeader()
	{
		DateTime nowLocal = _calendarTime?.CurrentLocalDateTime ?? DateTime.Now;
		decimal? price = _swapService.CurrentPriceSc;
		bool isHalt = _market?.IsHaltDay(nowLocal) ?? false;

		_priceLabel.Text = price is decimal p
			? string.Create(CultureInfo.InvariantCulture, $"1 BTC = {p:N8} SC ({nowLocal:dd MMM yyyy}){(isHalt ? "  [last price]" : "")}")
			: "1 BTC = —  (no market yet)";
		_priceLabel.AddThemeColorOverride("font_color", isHalt || price is null ? GreyedColor : NormalColor);

		var reasonA = _swapService.PanelAReason;
		if (reasonA == CasinoCoinSwapService.PanelDisableReason.MarketNotBornYet)
		{
			_deskStateLabel.Text = _market != null
				? string.Create(CultureInfo.InvariantCulture, $"No exchange exists yet — the first Bitcoin market opens {_market.FirstDataDateLocal:dd MMM yyyy}")
				: "No exchange exists yet";
			_deskStateLabel.AddThemeColorOverride("font_color", GreyedColor);
		}
		else if (reasonA == CasinoCoinSwapService.PanelDisableReason.HaltDay)
		{
			_deskStateLabel.Text = $"DESK CLOSED — {HaltReasonFor(nowLocal)}";
			_deskStateLabel.AddThemeColorOverride("font_color", DisabledReasonColor);
		}
		else if (_market != null && nowLocal.Date > _market.LastDataDateLocal)
		{
			_deskStateLabel.Text = "Desk open (post-history era — price frozen)"; // D-13.5
			_deskStateLabel.AddThemeColorOverride("font_color", NormalColor);
		}
		else
		{
			_deskStateLabel.Text = "Desk open";
			_deskStateLabel.AddThemeColorOverride("font_color", NormalColor);
		}

		// Read-only — the knob lives in CasinoGamblingFinances (D-SW.9). Additive model (2026-07-08,
		// supersedes D-SW.1): the network fee is charged SEPARATELY, on top of the %, never absorbed inside it.
		// The network side is the day's replayed median (ND.7 / D-ND7.9), NOT the retired flat 0.1 scaffold that
		// was hardcoded into this label until 2026-08-20 — the per-quote breakdowns below already read it live.
		// It is a genuine 0 from Market Birth (2010-07-18) through 2011-04-13, and the label must not advertise a
		// charge that is not made. Zero here is a KNOWN value, not missing data, so it reads "no network fee"
		// rather than the "—" this scene uses for absent/locked figures. RefreshHeader polls every 2 s, so the
		// label follows the median across a day boundary with no extra wiring.
		decimal feePercent    = _swapService.GetSwapFeePercentFor("player");
		decimal networkFeeBtc = _swapService.CurrentNetworkFeeBtc;
		_feeLabel.Text = networkFeeBtc > 0m
			? string.Create(CultureInfo.InvariantCulture,
				$"Swap fee: {feePercent:0.##}% + {networkFeeBtc:N8} BTC network fee (both directions, both charged)")
			: string.Create(CultureInfo.InvariantCulture,
				$"Swap fee: {feePercent:0.##}% (both directions) · no network fee at this date");
	}

	// D-13.11 — the two real historical halts the dataset carries (Source == "none" ranges).
	private static string HaltReasonFor(DateTime dateLocal) => dateLocal.Year switch
	{
		2011 => "trading halted after the Mt. Gox hack (June 2011)",
		2016 => "trading halted after the Bitfinex hack (August 2016)",
		_    => "historical trading halt"
	};

	private void RefreshPanelStates()
	{
		// Panel A — offered/reserve readout (BTC reserve knob: CasinoFinances). "Owned" is the casino's full
		// economic position and ALREADY INCLUDES any settling fee share (do not list settling/earmark
		// separately here — the figures would read as additive; the settling detail lives in the red status
		// label, the full identity in the CasinoFinances DEV line).
		_panelAAvailLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Casino BTC available: {_swapService.OfferedBtc:N8} BTC  (owned {_swapService.CasinoBtcOwnedTotal:N8} BTC, reserve {_swapService.BtcReserve.ReserveFor(_swapService.CasinoBtcEquity):N8} BTC — set in Casino Finances [DEV])");

		// Panel B — offered/reserve readout (SC reserve knob: CasinoGamblingFinances).
		_panelBAvailLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Casino SC available: {_swapService.OfferedSc:N8} SC  (Main {_swapService.CasinoScMainBalance:N8} SC − reserve {_swapService.EffectiveScReserve:N8} SC — set in Casino Gambling Finances [DEV])");

		ApplyPanelState(_swapService.IsPanelAEnabled, _swapService.PanelAReason,
			_panelAInput, _panelAReceiveInput, _panelAReasonLabel, isPanelA: true,
			_panelAMaxBtn, _panelAMinBtn, _panelAReceiveMaxBtn, _panelAReceiveMinBtn);
		ApplyPanelState(_swapService.IsPanelBEnabled, _swapService.PanelBReason,
			_panelBInput, _panelBReceiveInput, _panelBReasonLabel, isPanelA: false,
			_panelBMaxBtn, _panelBMinBtn, _panelBReceiveMaxBtn, _panelBReceiveMinBtn);

		_panelASwapBtn.Disabled = !_swapService.IsPanelAEnabled;
		_panelBSwapBtn.Disabled = !_swapService.IsPanelBEnabled;

		RefreshPendingRows();
	}

	// §4.4 — the in-flight BTC leg row: visible while a swap's on-chain send awaits its confirming block,
	// honest about what an app restart would do (both legs unwind together). Panel A's leg is BTC arriving
	// at the player; Panel B's leg is BTC the player already sent (their SC is credited already, but a
	// restart before confirmation reverts both the mempool send AND that SC credit together).
	private void RefreshPendingRows()
	{
		decimal pendingA = 0m, pendingB = 0m;
		foreach (var d in _swapService.PendingBtcDeliveries)
		{
			if (d.ClientId != "player") continue;
			if (d.Direction == CasinoCoinSwapService.DirectionScToBtc) pendingA += d.AmountBtc;
			else if (d.Direction == CasinoCoinSwapService.DirectionBtcToSc) pendingB += d.AmountBtc;
		}

		_panelAPendingLabel.Visible = pendingA > 0m;
		if (pendingA > 0m)
			_panelAPendingLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"⏳ {pendingA:N8} BTC incoming — confirms at the next mined block (a restart before then unwinds the swap)");

		_panelBPendingLabel.Visible = pendingB > 0m;
		if (pendingB > 0m)
			_panelBPendingLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"⏳ {pendingB:N8} BTC sent — confirms at the next mined block (a restart before then unwinds the swap, incl. your SC credit)");
	}

	private void ApplyPanelState(bool enabled, CasinoCoinSwapService.PanelDisableReason reason,
		LineEdit payInput, LineEdit receiveInput, Label reasonLabel, bool isPanelA, params Button[] extremeButtons)
	{
		payInput.Editable     = enabled;
		receiveInput.Editable = enabled;
		foreach (Button b in extremeButtons)
			b.Disabled = !enabled;
		reasonLabel.Visible = !enabled;
		if (!enabled)
			reasonLabel.Text = DisableReasonText(reason, isPanelA);
	}

	// SWAP (Panel A) — the service re-validates every clamp and hard-clamps to the binding max (§4.3);
	// the UI just relays the outcome. Success clears both inputs; the pending row is the visible receipt.
	private void OnPanelASwapPressed()
	{
		if (_swapService == null) return;
		if (!TryParseAmount(_panelAInput.Text, out decimal sc))
		{
			_panelAQuoteLabel.Text = "✖ Enter a valid SC amount first.";
			_panelAQuoteLabel.AddThemeColorOverride("font_color", QuoteBadColor);
			return;
		}

		if (_swapService.TryExecuteScToBtc("player", sc, out string error))
		{
			_panelAInput.Text = string.Empty;
			_panelAReceiveInput.Text = string.Empty;
			RefreshAll();
		}
		else
		{
			_panelAQuoteLabel.Text = $"✖ Swap rejected: {error}";
			_panelAQuoteLabel.AddThemeColorOverride("font_color", QuoteBadColor);
		}
	}

	// SWAP (Panel B) — mirrors Panel A's handler; the service re-validates every clamp and hard-clamps to
	// the binding max (§4.3).
	private void OnPanelBSwapPressed()
	{
		if (_swapService == null) return;
		if (!TryParseAmount(_panelBInput.Text, out decimal btc))
		{
			_panelBQuoteLabel.Text = "✖ Enter a valid BTC amount first.";
			_panelBQuoteLabel.AddThemeColorOverride("font_color", QuoteBadColor);
			return;
		}

		if (_swapService.TryExecuteBtcToSc("player", btc, out string error))
		{
			_panelBInput.Text = string.Empty;
			_panelBReceiveInput.Text = string.Empty;
			RefreshAll();
		}
		else
		{
			_panelBQuoteLabel.Text = $"✖ Swap rejected: {error}";
			_panelBQuoteLabel.AddThemeColorOverride("font_color", QuoteBadColor);
		}
	}

	private string DisableReasonText(CasinoCoinSwapService.PanelDisableReason reason, bool isPanelA) => reason switch
	{
		CasinoCoinSwapService.PanelDisableReason.MarketNotBornYet => "No market yet — the desk is locked until the first exchange opens.",
		CasinoCoinSwapService.PanelDisableReason.HaltDay          => "Desk closed for the day (historical trading halt).",
		CasinoCoinSwapService.PanelDisableReason.NoCasinoBtc      => string.Create(CultureInfo.InvariantCulture, $"Casino has no BTC available for swaps (needs ≥ {_swapService.MinDeliverableBtc:N8} BTC offered)."),
		CasinoCoinSwapService.PanelDisableReason.BtcSettling      => string.Create(CultureInfo.InvariantCulture, $"Casino BTC is settling ({_swapService.CasinoBtcSettling:N8} BTC awaiting its confirming block) — swaps unlock at the next block."),
		CasinoCoinSwapService.PanelDisableReason.NoCasinoSc       => "Casino has no SC available for swaps.",
		_ => string.Empty
	};

	// ── Quotes (pure service calls per keystroke, §4.3) ─────────────────────────
	// Each panel has a FORWARD path (pay input → quote → syncs the receive input) and a REVERSE path
	// (receive input → reverse quote → syncs the pay input). Both share the same rendering/Max-label
	// helpers so the two paths can never disagree about what a given quote looks like.

	private void RefreshPanelAQuote()
	{
		if (_swapService == null) return;
		var probe = _swapService.QuoteScToBtc("player", 0m);
		if (probe.PanelState != CasinoCoinSwapService.PanelDisableReason.None)
		{
			_panelAMaxLabel.Text   = "Max: —";
			_panelAQuoteLabel.Text = "Quote: —";
			return;
		}
		UpdatePanelAMaxLabel(probe);

		if (!TryParseAmount(_panelAInput.Text, out decimal sc))
		{
			_panelAQuoteLabel.Text = "Quote: enter an SC amount";
			_panelAQuoteLabel.AddThemeColorOverride("font_color", NormalColor);
			return;
		}

		var q = _swapService.QuoteScToBtc("player", sc);
		RenderPanelAQuote(q);
		SetText(_panelAReceiveInput, q.NetOut);
	}

	// Reverse path — "I want to receive this much BTC" → back-solve the SC needed to pay (§3.2's fee-floor
	// inversion, via CasinoCoinSwapService.QuoteScToBtcForReceivedBtc) and sync the pay field.
	private void RefreshPanelAQuoteFromReceive()
	{
		if (_swapService == null) return;
		var probe = _swapService.QuoteScToBtc("player", 0m);
		if (probe.PanelState != CasinoCoinSwapService.PanelDisableReason.None)
		{
			_panelAMaxLabel.Text   = "Max: —";
			_panelAQuoteLabel.Text = "Quote: —";
			return;
		}
		UpdatePanelAMaxLabel(probe);

		if (!TryParseAmount(_panelAReceiveInput.Text, out decimal desiredBtc))
		{
			_panelAQuoteLabel.Text = "Quote: enter a BTC amount to receive";
			_panelAQuoteLabel.AddThemeColorOverride("font_color", NormalColor);
			return;
		}

		var q = _swapService.QuoteScToBtcForReceivedBtc("player", desiredBtc);
		RenderPanelAQuote(q);
		SetText(_panelAInput, q.InputAmount);
	}

	private void UpdatePanelAMaxLabel(CasinoCoinSwapService.SwapQuote probe)
	{
		// The minimum's BTC-gross equivalent moves with the current swap fee (§3.2 — a lower fee % needs a
		// bigger gross swap before it clears the flat 0.1 BTC network-fee floor); never hardcode "1 BTC".
		decimal minGrossBtc = probe.PriceUsed > 0m ? probe.MinInput / probe.PriceUsed : 0m;
		_panelAMaxLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Max: {probe.MaxInput:N8} SC ({probe.MaxLimitedBy})  ·  Min: {probe.MinInput:N8} SC ({minGrossBtc:N8} BTC gross)");
	}

	private void RenderPanelAQuote(CasinoCoinSwapService.SwapQuote q)
	{
		// Fee breakdown (dev feedback 2026-07-07): the network's flat share is the day's replayed median
		// (ND.7 / D-ND7.9 — was the flat 0.1 scaffold) and the casino's margin is whatever remains
		// (0 in the fee-floor regime near the minimum swap size — the casino breaks even, by design, §3.2).
		decimal networkFeeBtc = _swapService?.CurrentNetworkFeeBtc ?? 0m;
		decimal casinoFeeBtc  = Math.Max(0m, Money.Normalize(q.FeeCharged - networkFeeBtc));
		// Effective casino margin % (dev feedback 2026-07-07): the flat network fee eats into the nominal
		// SwapFeePercent for any swap not far past the minimum size — e.g. at 10% nominal, a swap at 1.1×
		// the minimum size nets the casino only ~1% real margin (the true margin only approaches the
		// nominal rate for swaps many times larger than the minimum). This is correct/by-design (§34.4),
		// not a bug — shown here so it's never re-derived by hand mid-playtest.
		decimal effectiveMarginPctA = q.GrossConverted > 0m ? casinoFeeBtc / q.GrossConverted * 100m : 0m;
		_panelAQuoteLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"You give {q.InputAmount:N8} SC → gross {q.GrossConverted:N8} BTC − fee {q.FeeCharged:N8} BTC (network {networkFeeBtc:N8} + casino {casinoFeeBtc:N8}, {effectiveMarginPctA:0.##}% effective) → you receive ≈ {q.NetOut:N8} BTC (at next block)");
		if (!q.IsValid)
			_panelAQuoteLabel.Text += string.Create(CultureInfo.InvariantCulture,
				$"   ✖ {(q.InputAmount < q.MinInput ? $"minimum swap is {q.MinInput:N8} SC" : $"exceeds max ({q.MaxLimitedBy})")}");
		_panelAQuoteLabel.AddThemeColorOverride("font_color", q.IsValid ? QuoteOkColor : QuoteBadColor);
	}

	private void RefreshPanelBQuote()
	{
		if (_swapService == null) return;
		var probe = _swapService.QuoteBtcToSc("player", 0m);
		if (probe.PanelState != CasinoCoinSwapService.PanelDisableReason.None)
		{
			_panelBMaxLabel.Text   = "Max: —";
			_panelBQuoteLabel.Text = "Quote: —";
			return;
		}
		UpdatePanelBMaxLabel(probe);

		if (!TryParseAmount(_panelBInput.Text, out decimal btc))
		{
			_panelBQuoteLabel.Text = "Quote: enter a BTC amount";
			_panelBQuoteLabel.AddThemeColorOverride("font_color", NormalColor);
			return;
		}

		var q = _swapService.QuoteBtcToSc("player", btc);
		RenderPanelBQuote(q);
		SetText(_panelBReceiveInput, q.NetOut);
	}

	// Reverse path — "I want to receive this much SC" → back-solve the total BTC to send (via
	// CasinoCoinSwapService.QuoteBtcToScForReceivedSc) and sync the send field.
	private void RefreshPanelBQuoteFromReceive()
	{
		if (_swapService == null) return;
		var probe = _swapService.QuoteBtcToSc("player", 0m);
		if (probe.PanelState != CasinoCoinSwapService.PanelDisableReason.None)
		{
			_panelBMaxLabel.Text   = "Max: —";
			_panelBQuoteLabel.Text = "Quote: —";
			return;
		}
		UpdatePanelBMaxLabel(probe);

		if (!TryParseAmount(_panelBReceiveInput.Text, out decimal desiredSc))
		{
			_panelBQuoteLabel.Text = "Quote: enter an SC amount to receive";
			_panelBQuoteLabel.AddThemeColorOverride("font_color", NormalColor);
			return;
		}

		var q = _swapService.QuoteBtcToScForReceivedSc("player", desiredSc);
		RenderPanelBQuote(q);
		SetText(_panelBInput, q.InputAmount);
	}

	private void UpdatePanelBMaxLabel(CasinoCoinSwapService.SwapQuote probe)
	{
		_panelBMaxLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Max: {probe.MaxInput:N8} BTC ({probe.MaxLimitedBy})  ·  Min: {probe.MinInput:N8} BTC");
	}

	private void RenderPanelBQuote(CasinoCoinSwapService.SwapQuote q)
	{
		// Fee breakdown (dev feedback 2026-07-07) — same logic as Panel A, expressed in SC at this quote's
		// price: the network's flat share is the day's replayed median (ND.7 / D-ND7.9) converted at
		// PriceUsed; the rest is the casino's margin (0 in the fee-floor regime near the minimum swap size).
		decimal networkFeeBtcB = _swapService?.CurrentNetworkFeeBtc ?? 0m;
		decimal networkFeeSc = Money.Normalize(networkFeeBtcB * q.PriceUsed);
		decimal casinoFeeSc  = Math.Max(0m, Money.Normalize(q.FeeCharged - networkFeeSc));
		// Effective casino margin % — see Panel A's identical note.
		decimal effectiveMarginPctB = q.GrossConverted > 0m ? casinoFeeSc / q.GrossConverted * 100m : 0m;
		_panelBQuoteLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"You send {q.InputAmount:N8} BTC ({networkFeeBtcB:N8} network fee inside) → gross {q.GrossConverted:N8} SC − fee {q.FeeCharged:N8} SC (network {networkFeeSc:N8} + casino {casinoFeeSc:N8}, {effectiveMarginPctB:0.##}% effective) → you receive {q.NetOut:N8} SC (instant)");
		if (!q.IsValid)
			_panelBQuoteLabel.Text += string.Create(CultureInfo.InvariantCulture,
				$"   ✖ {(q.InputAmount < q.MinInput ? $"minimum swap is {q.MinInput:N8} BTC" : $"exceeds max ({q.MaxLimitedBy})")}");
		_panelBQuoteLabel.AddThemeColorOverride("font_color", q.IsValid ? QuoteOkColor : QuoteBadColor);
	}

	// MAX/MIN buttons (dev feedback 2026-07-07) — both the pay AND receive fields get one of each, for all
	// four inputs across the two panels. `quoteFn` is whichever panel's FORWARD quote method (QuoteScToBtc /
	// QuoteBtcToSc) — its signature (clientId, amount) => SwapQuote matches both, so one pair of helpers
	// covers all eight buttons.

	// Fills the PAY field directly with the forward quote's own Max/MinInput.
	private void FillPayExtreme(LineEdit payInput, Func<string, decimal, CasinoCoinSwapService.SwapQuote> quoteFn, bool useMax)
	{
		if (quoteFn == null) return;
		var probe = quoteFn("player", 0m);
		if (probe.PanelState != CasinoCoinSwapService.PanelDisableReason.None) return;
		FillAmount(payInput, useMax ? probe.MaxInput : probe.MinInput);
	}

	// Fills the RECEIVE field with the NET amount actually delivered when paying the pay-side Max/Min —
	// i.e. "what you'd get if you paid the most/least you legally can," not a separately-clamped figure.
	private void FillReceiveExtreme(LineEdit receiveInput, Func<string, decimal, CasinoCoinSwapService.SwapQuote> quoteFn, bool useMax)
	{
		if (quoteFn == null) return;
		var probe = quoteFn("player", 0m);
		if (probe.PanelState != CasinoCoinSwapService.PanelDisableReason.None) return;
		var atExtreme = quoteFn("player", useMax ? probe.MaxInput : probe.MinInput);
		FillAmount(receiveInput, atExtreme.NetOut);
	}

	// Replays the fill through the normal reactive TextChanged path (LineEdit.Text alone does not raise it
	// in this Godot binding — see the class doc comment), so the OTHER field of the pair syncs automatically.
	private static void FillAmount(LineEdit input, decimal amount)
	{
		if (amount <= 0m) return;
		input.Text = amount.ToString("0.00000000", CultureInfo.InvariantCulture);
		input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
	}

	// Programmatic sync of the OTHER field from a computed quote. Setting LineEdit.Text does NOT raise
	// TextChanged in this Godot binding (see the class doc comment / FillMax above), so this is inert —
	// no reentrancy guard needed, and no EmitSignal here (that would re-trigger a redundant, double-rounded
	// recompute in the opposite direction while the user is mid-keystroke).
	private static void SetText(LineEdit input, decimal amount) =>
		input.Text = amount > 0m ? amount.ToString("0.00000000", CultureInfo.InvariantCulture) : string.Empty;

	private static bool TryParseAmount(string raw, out decimal value)
	{
		value = 0m;
		if (string.IsNullOrWhiteSpace(raw)) return false;
		// Accept both plain ("1234.5") and display-formatted ("1,234.50000000") input — InvariantCulture only.
		return decimal.TryParse(raw.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0m;
	}

	// ── Recent swaps (D-SW.10 — history surfaces only here for now) ─────────────

	private void BuildRecentSwapsList()
	{
		if (!GodotObject.IsInstanceValid(_swapsListVBox)) return;

		foreach (Node child in _swapsListVBox.GetChildren())
			child.QueueFree();

		var history = _swapService.SwapHistory;
		if (history.Count == 0)
		{
			var empty = new Label { Text = "No swaps yet." };
			empty.AddThemeFontSizeOverride("font_size", 16);
			empty.AddThemeColorOverride("font_color", GreyedColor);
			_swapsListVBox.AddChild(empty);
			return;
		}

		int shown = 0;
		for (int i = history.Count - 1; i >= 0 && shown < MaxRecentSwapsShown; i--, shown++)
		{
			var r = history[i];
			bool isBuy = r.Direction == CasinoCoinSwapService.DirectionScToBtc;
			string line = isBuy
				? string.Create(CultureInfo.InvariantCulture, $"{r.GameDateLocal:yyyy-MM-dd HH:mm:ss}  [BUY BTC]   {r.GrossIn:N8} SC → {r.NetOut:N8} BTC  (fee {r.FeeCharged:N8} BTC, 1 BTC = {r.PriceUsed:N8} SC)")
				: string.Create(CultureInfo.InvariantCulture, $"{r.GameDateLocal:yyyy-MM-dd HH:mm:ss}  [SELL BTC]  {r.GrossIn:N8} BTC → {r.NetOut:N8} SC  (fee {r.FeeCharged:N8} SC, 1 BTC = {r.PriceUsed:N8} SC)");

			var label = new Label { Text = line };
			label.AddThemeFontSizeOverride("font_size", 16);
			label.AddThemeColorOverride("font_color", isBuy ? new Color(0.4f, 1f, 0.4f) : new Color(1f, 0.65f, 0.2f));
			_swapsListVBox.AddChild(label);
		}
	}
}
