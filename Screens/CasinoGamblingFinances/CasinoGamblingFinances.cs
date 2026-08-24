using Godot;
using System;
using System.Globalization;
using System.Linq;
using Scripts.Finance;
using UI.StatusBar;

public partial class CasinoGamblingFinances : Control
{
	private CasinoScBalanceService _casinoSc;
	private SceneManager _sceneManager;
	private CalendarTimeService _calendarTime;

	private Label _gameDateLabel;
	private Label _mainBalanceLabel;
	private Label _bankrollLabel;
	private Label _bankrollTargetValueLabel;
	private Label _totalLabel;
	private Label _plLabel;
	private Label _loanInfoLabel;
	private Label _autoLoanValueLabel;

	private LineEdit _bankrollTargetInput;
	private Label _targetFeedbackLabel;

	private LineEdit _autoLoanInput;

	private LineEdit _transferInput;
	private Label _transferFeedbackLabel;
	private ItemList _rechargeHistoryList;

	private LineEdit _manualLoanInput;
	private Label _loanFeedbackLabel;
	private ItemList _loanHistoryList;

	// Swap Desk [DEV] row (Step 13 / SW.2, D-SW.9): the fee knob + the SC swap-reserve selector live HERE
	// (CasinoCoinSwaps itself carries no DEV controls). The BTC reserve selector lives in CasinoFinances.
	private CasinoCoinSwapService _swapService;
	private SpinBox _swapFeeSpin;
	private SpinBox _maxFeeDeviationSpin;
	private OptionButton _scReserveModeOption;
	private SpinBox _scReserveSpin;
	private Label _swapDeskInfoLabel;
	// R2 auto-floor toggle (Step 13 / SW.5, §2.3): recharge-pace SC floor, composed as max(manual, auto).
	private CheckBox _scAutoFloorToggle;
	private SpinBox _scAutoFloorSafetySpin;
	private SpinBox _scAutoFloorWindowSpin;
	private Label _scAutoFloorBreakdownLabel;
	// Colored binding dots (dev feedback 2026-07-07): which side of max(manual, auto) currently applies.
	private Label _manualReserveIndicator;
	private Label _autoFloorIndicator;
	private static readonly Color IndicatorGreen = new Color(0.3f, 0.9f, 0.3f);
	private static readonly Color IndicatorRed   = new Color(0.95f, 0.3f, 0.3f);
	private static readonly Color IndicatorGrey  = new Color(0.55f, 0.55f, 0.55f);

	private double _fallbackTimer;
	private const double FallbackInterval = 2.0;

	public override void _Ready()
	{
		_casinoSc   = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_calendarTime = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_gameDateLabel       = GetNode<Label>("%GameDateLabel");
		_mainBalanceLabel    = GetNode<Label>("%MainBalanceLabel");
		_bankrollLabel       = GetNode<Label>("%BankrollLabel");
		_bankrollTargetValueLabel = GetNode<Label>("%BankrollTargetValueLabel");
		_totalLabel          = GetNode<Label>("%TotalLabel");
		_plLabel             = GetNode<Label>("%PlLabel");
		_loanInfoLabel       = GetNode<Label>("%LoanInfoLabel");
		_autoLoanValueLabel  = GetNode<Label>("%AutoLoanValueLabel");
		_bankrollTargetInput = GetNode<LineEdit>("%BankrollTargetInput");
		_targetFeedbackLabel = GetNode<Label>("%TargetFeedbackLabel");
		_autoLoanInput       = GetNode<LineEdit>("%AutoLoanInput");
		_transferInput       = GetNode<LineEdit>("%TransferInput");
		_transferFeedbackLabel = GetNode<Label>("%TransferFeedbackLabel");
		_rechargeHistoryList = GetNode<ItemList>("%RechargeHistoryList");
		_manualLoanInput     = GetNode<LineEdit>("%ManualLoanInput");
		_loanFeedbackLabel   = GetNode<Label>("%LoanFeedbackLabel");
		_loanHistoryList     = GetNode<ItemList>("%LoanHistoryList");

		_swapService         = GetNodeOrNull<CasinoCoinSwapService>("/root/CasinoCoinSwapService");
		_swapFeeSpin         = GetNode<SpinBox>("%SwapFeeSpin");
		_maxFeeDeviationSpin = GetNode<SpinBox>("%MaxFeeDeviationSpin");
		_scReserveModeOption = GetNode<OptionButton>("%ScReserveModeOption");
		_scReserveSpin       = GetNode<SpinBox>("%ScReserveSpin");
		_swapDeskInfoLabel   = GetNode<Label>("%SwapDeskInfoLabel");

		_scReserveModeOption.AddItem("% of Main Balance", 0);
		_scReserveModeOption.AddItem("SC amount", 1);
		if (_swapService != null)
		{
			_swapFeeSpin.Value = (double)_swapService.SwapFeePercent;
			_maxFeeDeviationSpin.Value = (double)_swapService.MaxFeeDeviationPoints;
			_scReserveModeOption.Selected = _swapService.ScReserve.UsePercent ? 0 : 1;
			SyncScReserveSpinToMode();
		}
		_scReserveModeOption.ItemSelected += _ => SyncScReserveSpinToMode();
		GetNode<Button>("%SetSwapFeeBtn").Pressed   += OnApplySwapFeePressed;
		GetNode<Button>("%SetMaxFeeDeviationBtn").Pressed += OnApplyMaxFeeDeviationPressed;
		GetNode<Button>("%SetScReserveBtn").Pressed += OnApplyScReservePressed;

		_scAutoFloorToggle     = GetNode<CheckBox>("%ScAutoFloorToggle");
		_scAutoFloorSafetySpin     = GetNode<SpinBox>("%ScAutoFloorSafetySpin");
		_scAutoFloorWindowSpin     = GetNode<SpinBox>("%ScAutoFloorWindowSpin");
		_scAutoFloorBreakdownLabel = GetNode<Label>("%ScAutoFloorBreakdownLabel");
		_manualReserveIndicator    = GetNode<Label>("%ManualReserveIndicator");
		_autoFloorIndicator        = GetNode<Label>("%AutoFloorIndicator");
		if (_swapService != null)
		{
			_scAutoFloorToggle.ButtonPressed = _swapService.ScFloorEnabled;
			_scAutoFloorSafetySpin.Value     = (double)_swapService.ScAutoFloorSafetyFactor;
			_scAutoFloorWindowSpin.Value     = (double)_swapService.ScAutoFloorWindowDays;
		}
		GetNode<Button>("%SetScAutoFloorBtn").Pressed += OnApplyScAutoFloorPressed;
		// Live preview (dev feedback 2026-07-07): the breakdown must update as the SpinBoxes move, BEFORE
		// Apply — SafetyFactor alone is unreadable without seeing doses × BankrollTarget alongside it.
		_scAutoFloorToggle.Toggled          += _ => RefreshScAutoFloorBreakdown();
		_scAutoFloorSafetySpin.ValueChanged += _ => RefreshScAutoFloorBreakdown();
		_scAutoFloorWindowSpin.ValueChanged += _ => RefreshScAutoFloorBreakdown();
		RefreshScAutoFloorBreakdown();

		GetNode<Button>("%SetTargetBtn").Pressed        += OnSetTargetPressed;
		GetNode<Button>("%SetAutoLoanBtn").Pressed      += OnSetAutoLoanPressed;
		GetNode<Button>("%ToBankrollBtn").Pressed       += OnToBankrollPressed;
		GetNode<Button>("%ToMainBtn").Pressed           += OnToMainPressed;
		GetNode<Button>("%ManualLoanBtn").Pressed       += OnManualLoanPressed;
		GetNode<Button>("%ClientsBetsHistoryBtn").Pressed   += () => _sceneManager?.Go(SceneManager.SceneId.ClientsBetsHistory);
		GetNode<Button>("%ClientsTransactionsBtn").Pressed  += () => _sceneManager?.Go(SceneManager.SceneId.ClientsTransactions);
		GetNode<Button>("%BackBtn").Pressed             += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		if (_casinoSc != null)
		{
			_casinoSc.BalanceChanged += RefreshLabels;
			_bankrollTargetInput.Text = _casinoSc.BankrollTarget.ToString("N8", CultureInfo.InvariantCulture);
			_autoLoanInput.Text = _casinoSc.AutoLoanAmount.ToString("N8", CultureInfo.InvariantCulture);
		}

		// Pre-populate the loan input with the default draw amount (100M).
		_manualLoanInput.Text = CasinoScBalanceService.InitialLoanAmount.ToString("N0", CultureInfo.InvariantCulture);

		RefreshLabels();
	}

	public override void _ExitTree()
	{
		if (_casinoSc != null)
			_casinoSc.BalanceChanged -= RefreshLabels;
	}

	public override void _Process(double delta)
	{
		// Game-date label ticks forward while autobet advances the clock (cheap string format).
		if (_gameDateLabel != null && _calendarTime != null)
		{
			_gameDateLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Game date: {_calendarTime.CurrentLocalDateTime:yyyy-MM-dd HH:mm:ss}");
		}

		_fallbackTimer += delta;
		if (_fallbackTimer >= FallbackInterval)
		{
			_fallbackTimer = 0;
			RefreshLabels();
		}
	}

	private void RefreshLabels()
	{
		if (_casinoSc == null) return;

		_mainBalanceLabel.Text = $"Main Balance:  {Money.FormatSignedAdaptive(_casinoSc.MainBalance)} SC";
		_bankrollLabel.Text    = $"Bankroll:      {Money.FormatSignedAdaptive(_casinoSc.Bankroll)} SC";
		_totalLabel.Text       = $"Total SC:      {Money.FormatSignedAdaptive(_casinoSc.TotalSc)} SC";

		// Under extra-lazy funding (CG.1.8) the casino holds nothing pre-loan (Main 0 / Bankroll 0), so
		// CumulativeProfitSinceLoan is naturally correct in every state — 0 pre-bet, +winnings after a loss
		// with no loan yet, real P/L after a loan. No phantom 100M can appear, so the old OQ-CG.6 LoanCount==0
		// display guard is gone (it would now wrongly hide the post-loss winnings while LoanCount is still 0).
		decimal pl = _casinoSc.CumulativeProfitSinceLoan;
		_plLabel.Text = string.Create(CultureInfo.InvariantCulture, $"P/L vs loan:   {pl:+0.00000000;-0.00000000} SC");
		_plLabel.AddThemeColorOverride("font_color", pl >= 0m
			? new Color(0.4f, 1f, 0.4f)
			: new Color(1f, 0.4f, 0.4f));

		_bankrollTargetValueLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Bankroll Target:  {_casinoSc.BankrollTarget:N8} SC");
		_autoLoanValueLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Auto-loan amount:  {_casinoSc.AutoLoanAmount:N8} SC");

		// Loan info line — include the most recent draw's game date+time. Step 15 P15.1c: the figures and the
		// history now come from the casino's Central Bank (FED) account, not from a copy the casino kept
		// itself. The FED caps each client's history at its newest 500 records while the counters stay exact,
		// so any surplus is flagged as "(+N older)" rather than letting the count and the list look inconsistent.
		var history = _casinoSc.LoanHistory;
		int loggedDraws = history.Count(r => r.Kind == CentralBankService.KindDraw);
		string lastLoanDate = history.Count > 0
			? history[^1].GameDateLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
			: "n/a";
		int untrackedCount = _casinoSc.LoanCount - loggedDraws;
		string untrackedNote = untrackedCount > 0 ? $" (+{untrackedCount} older)" : "";
		_loanInfoLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"FED loans taken: {_casinoSc.LoanCount}{untrackedNote}   |   Total loaned: {_casinoSc.TotalLoaned:N8} SC   |   Outstanding: {_casinoSc.OutstandingFedDebt:N8} SC   |   Last: {lastLoanDate}");

		// Loan history list, newest first (full game timestamp — CG.3.B). Validity guard (CG.2.15): the list can
		// be queried by the fallback timer during a scene teardown frame.
		if (GodotObject.IsInstanceValid(_loanHistoryList))
		{
			_loanHistoryList.Clear();
			for (int i = history.Count - 1; i >= 0; i--)
			{
				var r = history[i];
				_loanHistoryList.AddItem(string.Create(CultureInfo.InvariantCulture,
					$"{r.GameDateLocal:yyyy-MM-dd HH:mm:ss} | {r.Kind,-5} | {r.Amount:N8} SC | {r.Reason}"));
			}
		}

		RefreshSwapDeskInfo();
		RefreshScAutoFloorBreakdown(); // doses-in-window ages with game time even without a new recharge

		// Bankroll recharge history list, newest first (CG.3.A), mirroring the loans list.
		if (GodotObject.IsInstanceValid(_rechargeHistoryList))
		{
			var recharges = _casinoSc.RechargeHistory;
			_rechargeHistoryList.Clear();
			for (int i = recharges.Count - 1; i >= 0; i--)
			{
				var r = recharges[i];
				_rechargeHistoryList.AddItem(string.Create(CultureInfo.InvariantCulture,
					$"{r.GameDateLocal:yyyy-MM-dd HH:mm:ss} | {r.Amount:N8} SC | {r.Reason}"));
			}
		}
	}

	// ── Swap Desk [DEV] (Step 13 / SW.2, D-SW.9) ─────────────────────────────────

	// Mode switch reloads the SpinBox with the mode's current stored value and range (% → 0–100; amount → open).
	private void SyncScReserveSpinToMode()
	{
		if (_swapService == null) return;
		bool percentMode = _scReserveModeOption.Selected == 0;
		_scReserveSpin.MaxValue = percentMode ? 100d : 1_000_000_000_000d;
		_scReserveSpin.Value    = percentMode ? (double)_swapService.ScReserve.Percent : (double)_swapService.ScReserve.Amount;
	}

	private void OnApplySwapFeePressed()
	{
		// SpinBox already refuses values outside 1–10; the service setter clamps again as the safety net.
		_swapService?.SetSwapFeePercent((decimal)_swapFeeSpin.Value);
		RefreshLabels();
	}

	private void OnApplyMaxFeeDeviationPressed()
	{
		// SpinBox already refuses values outside 0–20 (D-SW.12); the service setter clamps again as the safety net.
		_swapService?.SetMaxFeeDeviationPoints((decimal)_maxFeeDeviationSpin.Value);
		RefreshLabels();
	}

	private void OnApplyScReservePressed()
	{
		if (_swapService == null) return;
		bool percentMode = _scReserveModeOption.Selected == 0;
		decimal value = (decimal)_scReserveSpin.Value;
		// Only the active mode's field changes; the other keeps its stored value for a clean round-trip.
		_swapService.SetScReserve(percentMode,
			percentMode ? value : _swapService.ScReserve.Percent,
			percentMode ? _swapService.ScReserve.Amount : value);
		RefreshLabels();
	}

	// R2 auto-floor (§2.3, SW.5): recharge-pace SC floor. Non-positive spin values fall back to defaults
	// service-side; the toggle can be ON with the manual reserve still binding (max() composition).
	private void OnApplyScAutoFloorPressed()
	{
		_swapService?.SetScFloor(_scAutoFloorToggle.ButtonPressed, (decimal)_scAutoFloorSafetySpin.Value, (decimal)_scAutoFloorWindowSpin.Value);
		RefreshLabels();
		RefreshScAutoFloorBreakdown();
	}

	// Live preview of the R2 formula using the SpinBoxes' CURRENT (possibly not-yet-applied) values, so the
	// dev sees exactly what SafetyFactor "weighs" — in SC, broken into its three factors — before hitting
	// Apply (dev feedback 2026-07-07: SafetyFactor alone is meaningless without doses-consumed and
	// BankrollTarget alongside it, since the same SafetyFactor produces very different SC amounts depending
	// on both). Uses GetScAutoFloorDosesConsumedFor(previewWindow) so the doses count itself previews
	// correctly even when the Window spinner hasn't been applied yet.
	private void RefreshScAutoFloorBreakdown()
	{
		if (_swapService == null || !GodotObject.IsInstanceValid(_scAutoFloorBreakdownLabel)) return;

		decimal safety = (decimal)_scAutoFloorSafetySpin.Value;
		decimal windowDays = (decimal)_scAutoFloorWindowSpin.Value;
		int doses = _swapService.GetScAutoFloorDosesConsumedFor(windowDays);
		decimal bankrollTarget = _casinoSc?.BankrollTarget ?? 0m;
		decimal previewFloor = Money.Normalize(safety * doses * bankrollTarget);
		string appliedNote = _scAutoFloorToggle.ButtonPressed ? "" : "  (toggle is OFF — not currently applied)";

		_scAutoFloorBreakdownLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Preview: {safety:0.##} safety × {doses} dose(s) consumed in last {windowDays:0.#} day(s) × {bankrollTarget:N8} SC (BankrollTarget) = {previewFloor:N8} SC{appliedNote}");
	}

	private void RefreshSwapDeskInfo()
	{
		if (_swapService == null || !GodotObject.IsInstanceValid(_swapDeskInfoLabel)) return;
		string reserveDesc = _swapService.ScReserve.UsePercent
			? string.Create(CultureInfo.InvariantCulture, $"{_swapService.ScReserve.Percent:0.##}% of Main")
			: string.Create(CultureInfo.InvariantCulture, $"{_swapService.ScReserve.Amount:N8} SC");
		string autoFloorDesc = _swapService.ScFloorEnabled
			? string.Create(CultureInfo.InvariantCulture, $"ON (safety ×{_swapService.ScAutoFloorSafetyFactor:0.##}, {_swapService.ScAutoFloorWindowDays:0.#}d window) → {_swapService.ScAutoFloor:N8} SC")
			: "OFF";

		// Which side of max(manual, auto) is currently binding (dev feedback 2026-07-07: running both at
		// once was confusing without this) — composition itself is unchanged, just made visible.
		decimal manualAbs = _swapService.ScReserve.ReserveFor(_swapService.CasinoScMainBalance);
		decimal autoAbs   = _swapService.ScAutoFloor;
		bool autoBinds = autoAbs > manualAbs;
		string bindingNote = autoBinds ? " [auto floor binds]" : " [manual reserve binds]";

		// Colored dot per selector (dev feedback 2026-07-07: text alone wasn't fast enough to scan at a
		// glance). Green = this side is currently the effective reserve; red = the other side overrides it;
		// grey = auto floor is off entirely (not a candidate right now).
		if (GodotObject.IsInstanceValid(_manualReserveIndicator))
			_manualReserveIndicator.AddThemeColorOverride("font_color", autoBinds ? IndicatorRed : IndicatorGreen);
		if (GodotObject.IsInstanceValid(_autoFloorIndicator))
			_autoFloorIndicator.AddThemeColorOverride("font_color",
				!_swapService.ScFloorEnabled ? IndicatorGrey : autoBinds ? IndicatorGreen : IndicatorRed);

		_swapDeskInfoLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Fee {_swapService.SwapFeePercent:0.##}% (both directions, capped ≤{_swapService.SwapFeePercent + _swapService.MaxFeeDeviationPoints:0.##}% effective — D-SW.12)   |   SC reserve: {reserveDesc}   |   Auto floor (R2): {autoFloorDesc}   |   Effective reserve: {_swapService.EffectiveScReserve:N8} SC{bindingNote}   |   Offered SC: {_swapService.OfferedSc:N8}");
	}

	private void OnSetTargetPressed()
	{
		_targetFeedbackLabel.Text = "";
		string raw = _bankrollTargetInput.Text.Trim();
		if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
			System.Globalization.CultureInfo.InvariantCulture, out decimal value) || value <= 0m)
		{
			_targetFeedbackLabel.Text = "Invalid amount — enter a positive number.";
			return;
		}
		_casinoSc?.SetBankrollTarget(value);
		_bankrollTargetInput.Text = "";
		_targetFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Bankroll target set to {value:N8} SC.");
		RefreshLabels();
	}

	private void OnSetAutoLoanPressed()
	{
		_loanFeedbackLabel.Text = "";
		string raw = (_autoLoanInput.Text ?? string.Empty).Trim().Replace(",", "");
		if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal value) || value <= 0m)
		{
			_loanFeedbackLabel.Text = "Invalid auto-loan amount — enter a positive number.";
			return;
		}
		_casinoSc?.SetAutoLoanAmount(value);
		_autoLoanInput.Text = "";
		_loanFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Auto-loan amount set to {value:N8} SC.");
		RefreshLabels();
	}

	private void OnManualLoanPressed()
	{
		_loanFeedbackLabel.Text = "";
		// Type ANY specific amount; blank/invalid falls back to the default draw (InitialLoanAmount = 40,000 SC).
		decimal amount = CasinoScBalanceService.InitialLoanAmount;
		string raw = (_manualLoanInput.Text ?? string.Empty).Trim().Replace(",", "");
		if (!string.IsNullOrEmpty(raw) &&
			decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed) &&
			parsed > 0m)
		{
			amount = Money.Normalize(parsed);
		}

		_casinoSc?.TriggerManualLoan(amount);
		_manualLoanInput.Text = CasinoScBalanceService.InitialLoanAmount.ToString("N0", CultureInfo.InvariantCulture);
		_loanFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Loan of {amount:N8} SC added to Main Balance (game: {_calendarTime?.CurrentLocalDateTime:yyyy-MM-dd HH:mm:ss}).");
		RefreshLabels();
	}

	private void OnToBankrollPressed()
	{
		_transferFeedbackLabel.Text = "";
		if (!TryParseTransfer(out decimal amount)) return;
		if (_casinoSc == null || !_casinoSc.TryTransferToBankroll(amount))
		{
			_transferFeedbackLabel.Text = "Transfer failed — insufficient Main Balance.";
			return;
		}
		_transferInput.Text = "";
		_transferFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Transferred {amount:N8} SC → Bankroll.");
		RefreshLabels();
	}

	private void OnToMainPressed()
	{
		_transferFeedbackLabel.Text = "";
		if (!TryParseTransfer(out decimal amount)) return;
		if (_casinoSc == null || !_casinoSc.TryTransferToMainBalance(amount))
		{
			_transferFeedbackLabel.Text = "Transfer failed — insufficient Bankroll.";
			return;
		}
		_transferInput.Text = "";
		_transferFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Transferred {amount:N8} SC → Main Balance.");
		RefreshLabels();
	}

	private bool TryParseTransfer(out decimal amount)
	{
		amount = 0m;
		string raw = _transferInput.Text.Trim();
		if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
			System.Globalization.CultureInfo.InvariantCulture, out amount) || amount <= 0m)
		{
			_transferFeedbackLabel.Text = "Invalid amount — enter a positive number.";
			return false;
		}
		return true;
	}
}
