using Godot;
using System;
using System.Globalization;
using Scripts.Finance;
using UI.StatusBar;

public partial class CasinoGamblingFinances : Control
{
	private CasinoScBalanceService _casinoSc;
	private SceneManager _sceneManager;

	private Label _mainBalanceLabel;
	private Label _bankrollLabel;
	private Label _totalLabel;
	private Label _plLabel;
	private Label _loanInfoLabel;
	private Label _targetInfoLabel;

	private LineEdit _bankrollTargetInput;
	private Label _targetFeedbackLabel;

	private LineEdit _transferInput;
	private Label _transferFeedbackLabel;

	private double _fallbackTimer;
	private const double FallbackInterval = 2.0;

	public override void _Ready()
	{
		_casinoSc   = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_mainBalanceLabel    = GetNode<Label>("%MainBalanceLabel");
		_bankrollLabel       = GetNode<Label>("%BankrollLabel");
		_totalLabel          = GetNode<Label>("%TotalLabel");
		_plLabel             = GetNode<Label>("%PlLabel");
		_loanInfoLabel       = GetNode<Label>("%LoanInfoLabel");
		_targetInfoLabel     = GetNode<Label>("%TargetInfoLabel");
		_bankrollTargetInput = GetNode<LineEdit>("%BankrollTargetInput");
		_targetFeedbackLabel = GetNode<Label>("%TargetFeedbackLabel");
		_transferInput       = GetNode<LineEdit>("%TransferInput");
		_transferFeedbackLabel = GetNode<Label>("%TransferFeedbackLabel");

		GetNode<Button>("%SetTargetBtn").Pressed        += OnSetTargetPressed;
		GetNode<Button>("%ToBankrollBtn").Pressed       += OnToBankrollPressed;
		GetNode<Button>("%ToMainBtn").Pressed           += OnToMainPressed;
		GetNode<Button>("%ClientsBetsHistoryBtn").Pressed   += () => _sceneManager?.Go(SceneManager.SceneId.ClientsBetsHistory);
		GetNode<Button>("%ClientsTransactionsBtn").Pressed  += () => _sceneManager?.Go(SceneManager.SceneId.ClientsTransactions);
		GetNode<Button>("%BackBtn").Pressed             += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		if (_casinoSc != null)
			_casinoSc.BalanceChanged += RefreshLabels;

		RefreshLabels();
	}

	public override void _ExitTree()
	{
		if (_casinoSc != null)
			_casinoSc.BalanceChanged -= RefreshLabels;
	}

	public override void _Process(double delta)
	{
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

		decimal pl = _casinoSc.CumulativeProfitSinceLoan;
		_plLabel.Text = string.Create(CultureInfo.InvariantCulture, $"P/L vs loan:   {pl:+0.00000000;-0.00000000} SC");
		_plLabel.AddThemeColorOverride("font_color", pl >= 0m
			? new Color(0.4f, 1f, 0.4f)
			: new Color(1f, 0.4f, 0.4f));

		_loanInfoLabel.Text  = string.Create(CultureInfo.InvariantCulture, $"Bank loans taken: {_casinoSc.LoanCount}   |   Total loaned: {_casinoSc.TotalLoaned:N8} SC");
		_targetInfoLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Bankroll target: {_casinoSc.BankrollTarget:N8} SC   (auto-fills to this level on exhaustion)");
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
