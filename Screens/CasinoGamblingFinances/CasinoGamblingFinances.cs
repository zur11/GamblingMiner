using Godot;
using System;
using System.Globalization;
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

		// Loan info line — include the most recent loan's game date+time, and flag any loans drawn before
		// loan-history logging existed (pre-CG.2 checkpoints) as "(+N pre-log)" so LoanCount and the list can't
		// look inconsistent.
		var history = _casinoSc.LoanHistory;
		string lastLoanDate = history.Count > 0
			? history[^1].GameDateLocal.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
			: "n/a";
		int unloggedCount = _casinoSc.LoanCount - history.Count;
		string unloggedNote = unloggedCount > 0 ? $" (+{unloggedCount} pre-log)" : "";
		_loanInfoLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Bank loans taken: {_casinoSc.LoanCount}{unloggedNote}   |   Total loaned: {_casinoSc.TotalLoaned:N8} SC   |   Last: {lastLoanDate}");

		// Loan history list, newest first (full game timestamp — CG.3.B). Validity guard (CG.2.15): the list can
		// be queried by the fallback timer during a scene teardown frame.
		if (GodotObject.IsInstanceValid(_loanHistoryList))
		{
			_loanHistoryList.Clear();
			for (int i = history.Count - 1; i >= 0; i--)
			{
				var r = history[i];
				_loanHistoryList.AddItem(string.Create(CultureInfo.InvariantCulture,
					$"{r.GameDateLocal:yyyy-MM-dd HH:mm:ss} | {r.Amount:N8} SC | {r.Reason}"));
			}
		}

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
		// Type ANY specific amount; blank/invalid falls back to the default draw (InitialLoanAmount = 100M).
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
