using Godot;
using System;
using System.Globalization;
using Scripts.Finance;
using UI.StatusBar;

// Step 13 (SW.2) — the casino's swap desk (D-13.6, plan §6): two panels (A: SC→BTC "Buy BTC", B: BTC→SC
// "Sell BTC"), casino-as-dealer. DISPLAY-ONLY in SW.2: availability, offered/reserve readouts, live quote
// previews (pure CasinoCoinSwapService.Quote* calls per keystroke), enable/disable states with reasons,
// halt-day / no-market / post-history states. SWAP buttons exist but stay disabled until SW.3/SW.4 wire
// execution. NO DEV controls here (D-SW.9 as amended): the fee knob + SC reserve live in
// CasinoGamblingFinances, the BTC reserve in CasinoFinances — this scene only displays the results.
// Layout: Ch. 29 fixed-footer pattern (ScTransactions reference) — footer Back OUTSIDE the scroll.
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
	private Button   _panelAMaxBtn;
	private Label    _panelAMaxLabel;
	private Label    _panelAQuoteLabel;
	private Button   _panelASwapBtn;
	private Label    _panelAReasonLabel;
	private Label    _panelAPendingLabel;
	private Label    _panelBPendingLabel;

	private Label    _panelBAvailLabel;
	private LineEdit _panelBInput;
	private Button   _panelBMaxBtn;
	private Label    _panelBMaxLabel;
	private Label    _panelBQuoteLabel;
	private Button   _panelBSwapBtn;
	private Label    _panelBReasonLabel;

	private VBoxContainer _swapsListVBox;

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

		_panelAAvailLabel  = GetNode<Label>("%PanelAAvailLabel");
		_panelAInput       = GetNode<LineEdit>("%PanelAInput");
		_panelAMaxBtn      = GetNode<Button>("%PanelAMaxBtn");
		_panelAMaxLabel    = GetNode<Label>("%PanelAMaxLabel");
		_panelAQuoteLabel   = GetNode<Label>("%PanelAQuoteLabel");
		_panelASwapBtn      = GetNode<Button>("%PanelASwapBtn");
		_panelAReasonLabel  = GetNode<Label>("%PanelAReasonLabel");
		_panelAPendingLabel = GetNode<Label>("%PanelAPendingLabel");
		_panelBPendingLabel = GetNode<Label>("%PanelBPendingLabel");

		_panelBAvailLabel  = GetNode<Label>("%PanelBAvailLabel");
		_panelBInput       = GetNode<LineEdit>("%PanelBInput");
		_panelBMaxBtn      = GetNode<Button>("%PanelBMaxBtn");
		_panelBMaxLabel    = GetNode<Label>("%PanelBMaxLabel");
		_panelBQuoteLabel  = GetNode<Label>("%PanelBQuoteLabel");
		_panelBSwapBtn     = GetNode<Button>("%PanelBSwapBtn");
		_panelBReasonLabel = GetNode<Label>("%PanelBReasonLabel");

		_swapsListVBox = GetNode<VBoxContainer>("%SwapsListVBox");

		_panelAReasonLabel.AddThemeColorOverride("font_color", DisabledReasonColor);
		_panelBReasonLabel.AddThemeColorOverride("font_color", DisabledReasonColor);

		_panelAInput.TextChanged += _ => RefreshPanelAQuote();
		_panelBInput.TextChanged += _ => RefreshPanelBQuote();
		_panelAMaxBtn.Pressed += () => FillMax(_panelAInput, _swapService?.QuoteScToBtc("player", 0m));
		_panelBMaxBtn.Pressed += () => FillMax(_panelBInput, _swapService?.QuoteBtcToSc("player", 0m));
		_panelASwapBtn.Pressed += OnPanelASwapPressed;

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
		RefreshPanelAQuote();
		RefreshPanelBQuote();
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

		// Read-only — the knob lives in CasinoGamblingFinances (D-SW.9).
		_feeLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Swap fee: {_swapService.GetSwapFeePercentFor("player"):0.##}% (both directions, incl. 0.1 BTC network fee)");
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
			$"Casino BTC available: {_swapService.OfferedBtc:N8}  (owned {_swapService.CasinoBtcOwnedTotal:N8}, reserve {_swapService.BtcReserve.ReserveFor(_swapService.CasinoBtcEquity):N8} — set in Casino Finances [DEV])");

		// Panel B — offered/reserve readout (SC reserve knob: CasinoGamblingFinances).
		_panelBAvailLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Casino SC available: {_swapService.OfferedSc:N8}  (Main {_swapService.CasinoScMainBalance:N8} − reserve {_swapService.EffectiveScReserve:N8} — set in Casino Gambling Finances [DEV])");

		ApplyPanelState(_swapService.IsPanelAEnabled, _swapService.PanelAReason,
			_panelAInput, _panelAMaxBtn, _panelAReasonLabel, isPanelA: true);
		ApplyPanelState(_swapService.IsPanelBEnabled, _swapService.PanelBReason,
			_panelBInput, _panelBMaxBtn, _panelBReasonLabel, isPanelA: false);

		// Panel A executes since SW.3; Panel B's button stays hard-disabled until SW.4.
		_panelASwapBtn.Disabled = !_swapService.IsPanelAEnabled;

		RefreshPendingRows();
	}

	// §4.4 — the in-flight BTC leg row: visible while a swap's on-chain send awaits its confirming block,
	// honest about what an app restart would do (both legs unwind together).
	private void RefreshPendingRows()
	{
		decimal pendingA = 0m;
		foreach (var d in _swapService.PendingBtcDeliveries)
			if (d.Direction == CasinoCoinSwapService.DirectionScToBtc && d.ClientId == "player")
				pendingA += d.AmountBtc;

		_panelAPendingLabel.Visible = pendingA > 0m;
		if (pendingA > 0m)
			_panelAPendingLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"⏳ {pendingA:N8} BTC incoming — confirms at the next mined block (a restart before then unwinds the swap)");
	}

	private void ApplyPanelState(bool enabled, CasinoCoinSwapService.PanelDisableReason reason,
		LineEdit input, Button maxBtn, Label reasonLabel, bool isPanelA)
	{
		input.Editable  = enabled;
		maxBtn.Disabled = !enabled;
		reasonLabel.Visible = !enabled;
		if (!enabled)
			reasonLabel.Text = DisableReasonText(reason, isPanelA);
	}

	// SWAP (Panel A) — the service re-validates every clamp and hard-clamps to the binding max (§4.3);
	// the UI just relays the outcome. Success clears the input; the pending row is the visible receipt.
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
			RefreshAll();
		}
		else
		{
			_panelAQuoteLabel.Text = $"✖ Swap rejected: {error}";
			_panelAQuoteLabel.AddThemeColorOverride("font_color", QuoteBadColor);
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

		_panelAMaxLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Max: {probe.MaxInput:N8} SC ({probe.MaxLimitedBy})  ·  Min: {probe.MinInput:N8} SC (1 BTC gross)");

		if (!TryParseAmount(_panelAInput.Text, out decimal sc))
		{
			_panelAQuoteLabel.Text = "Quote: enter an SC amount";
			_panelAQuoteLabel.AddThemeColorOverride("font_color", NormalColor);
			return;
		}

		var q = _swapService.QuoteScToBtc("player", sc);
		_panelAQuoteLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"You give {q.InputAmount:N8} SC → gross {q.GrossConverted:N8} BTC − fee {q.FeeCharged:N8} BTC → you receive ≈ {q.NetOut:N8} BTC (at next block)");
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

		_panelBMaxLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Max: {probe.MaxInput:N8} BTC ({probe.MaxLimitedBy})  ·  Min: {probe.MinInput:N8} BTC");

		if (!TryParseAmount(_panelBInput.Text, out decimal btc))
		{
			_panelBQuoteLabel.Text = "Quote: enter a BTC amount";
			_panelBQuoteLabel.AddThemeColorOverride("font_color", NormalColor);
			return;
		}

		var q = _swapService.QuoteBtcToSc("player", btc);
		_panelBQuoteLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"You send {q.InputAmount:N8} BTC (0.1 network fee inside) → gross {q.GrossConverted:N8} SC − fee {q.FeeCharged:N8} SC → you receive {q.NetOut:N8} SC (instant)");
		if (!q.IsValid)
			_panelBQuoteLabel.Text += string.Create(CultureInfo.InvariantCulture,
				$"   ✖ {(q.InputAmount < q.MinInput ? $"minimum swap is {q.MinInput:N8} BTC" : $"exceeds max ({q.MaxLimitedBy})")}");
		_panelBQuoteLabel.AddThemeColorOverride("font_color", q.IsValid ? QuoteOkColor : QuoteBadColor);
	}

	private void FillMax(LineEdit input, CasinoCoinSwapService.SwapQuote probe)
	{
		if (probe == null || probe.PanelState != CasinoCoinSwapService.PanelDisableReason.None) return;
		input.Text = probe.MaxInput.ToString("0.00000000", CultureInfo.InvariantCulture);
		input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text); // refresh the quote through the same path
	}

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
