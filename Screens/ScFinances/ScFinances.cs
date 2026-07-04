using Godot;
using System;
using System.Globalization;
using Scripts.Finance;
using UI.StatusBar;

// Player-facing hub for the player's SC flows (Step 12 / SF.2) — the mirror of CasinoGamblingFinances, but the
// player OWNS their money (no credit). Manages the Private Bank Account (an optional, initially-empty SC reserve)
// and its four transfer flows against Main Balance. NetWorthSc / OverallPl are computed HERE from the three
// balance sources (D-SF2.7 keeps PlayerBankAccountService pure). See AIHelperFiles/step12-player-sc-finances-plan.md §3.3.
public partial class ScFinances : Control
{
	private PlayerBankAccountService _playerBank;
	private PrincipalBalanceService  _principal;
	private BankrollStateService     _bankroll;
	private BankrollProgramService   _bankrollProgram;
	private SceneManager             _sceneManager;
	private CalendarTimeService      _calendarTime;

	private Label _gameDateLabel;
	private Label _privateBankLabel;
	private Label _mainBalanceLabel;
	private Label _bankrollLabel;
	private Label _netWorthLabel;
	private Label _overallPlLabel;
	private Label _doseLabel;

	private CheckBox _autoDepositToggle;
	private LineEdit _refillChunkInput;
	private LineEdit _depositAmountInput;
	private Label    _depositAvailableLabel;
	private Label    _depositFeedbackLabel;

	private CheckBox _autoWithdrawToggle;
	private LineEdit _floorInput;
	private LineEdit _installmentInput;
	private LineEdit _withdrawAmountInput;
	private Label    _withdrawAvailableLabel;
	private Label    _withdrawFeedbackLabel;

	private ItemList _bankTransferHistoryList;

	// Guards the programmatic ButtonPressed writes below from re-triggering the Toggled handlers.
	private bool _syncingToggles;

	private double _fallbackTimer;
	private const double FallbackInterval = 2.0;

	public override void _Ready()
	{
		_playerBank      = GetNodeOrNull<PlayerBankAccountService>("/root/PlayerBankAccountService");
		_principal       = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_bankroll        = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
		_bankrollProgram = GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService");
		_sceneManager    = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_calendarTime    = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_gameDateLabel        = GetNode<Label>("%GameDateLabel");
		_privateBankLabel     = GetNode<Label>("%PrivateBankLabel");
		_mainBalanceLabel     = GetNode<Label>("%MainBalanceLabel");
		_bankrollLabel        = GetNode<Label>("%BankrollLabel");
		_netWorthLabel        = GetNode<Label>("%NetWorthLabel");
		_overallPlLabel       = GetNode<Label>("%OverallPlLabel");
		_doseLabel            = GetNode<Label>("%DoseLabel");

		_autoDepositToggle    = GetNode<CheckBox>("%AutoDepositToggle");
		_refillChunkInput     = GetNode<LineEdit>("%RefillChunkInput");
		_depositAmountInput   = GetNode<LineEdit>("%DepositAmountInput");
		_depositAvailableLabel = GetNode<Label>("%DepositAvailableLabel");
		_depositFeedbackLabel = GetNode<Label>("%DepositFeedbackLabel");

		_autoWithdrawToggle   = GetNode<CheckBox>("%AutoWithdrawToggle");
		_floorInput           = GetNode<LineEdit>("%FloorInput");
		_installmentInput     = GetNode<LineEdit>("%InstallmentInput");
		_withdrawAmountInput  = GetNode<LineEdit>("%WithdrawAmountInput");
		_withdrawAvailableLabel = GetNode<Label>("%WithdrawAvailableLabel");
		_withdrawFeedbackLabel = GetNode<Label>("%WithdrawFeedbackLabel");

		_bankTransferHistoryList = GetNode<ItemList>("%BankTransferHistoryList");

		GetNode<Button>("%SetRefillChunkBtn").Pressed   += OnSetRefillChunkPressed;
		GetNode<Button>("%DepositBtn").Pressed          += OnDepositPressed;
		GetNode<Button>("%SetAutoWithdrawBtn").Pressed  += OnApplyAutoWithdrawPressed;
		GetNode<Button>("%WithdrawBtn").Pressed         += OnWithdrawPressed;
		_autoDepositToggle.Toggled  += OnAutoDepositToggled;
		_autoWithdrawToggle.Toggled += OnAutoWithdrawToggled;

		GetNode<Button>("%ScTransactionsBtn").Pressed     += () => _sceneManager?.Go(SceneManager.SceneId.ScTransactions);
		GetNode<Button>("%BetsHistoryBtn").Pressed        += () => _sceneManager?.Go(SceneManager.SceneId.BetsHistoryExplorer);
		GetNode<Button>("%BankrollProgrammerBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.BankrollProgrammer);
		GetNode<Button>("%BackBtn").Pressed               += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		if (_playerBank != null)
		{
			_playerBank.BankStateChanged += RefreshAll;
			// Seed the setting widgets from the current service state (guarded so the programmatic toggle write
			// doesn't fire the handlers and reapply settings).
			_syncingToggles = true;
			_autoDepositToggle.ButtonPressed  = _playerBank.AutoDepositEnabled;
			_autoWithdrawToggle.ButtonPressed = _playerBank.AutoWithdrawEnabled;
			_syncingToggles = false;
			_refillChunkInput.Text = _playerBank.AutoDepositAmount.ToString("N8", CultureInfo.InvariantCulture);
			_floorInput.Text       = _playerBank.AutoWithdrawThreshold.ToString("N8", CultureInfo.InvariantCulture);
			_installmentInput.Text = _playerBank.AutoWithdrawAmount.ToString("N8", CultureInfo.InvariantCulture);
		}
		if (_principal != null)       _principal.BalanceChanged += RefreshAll;
		if (_bankrollProgram != null)
		{
			_bankrollProgram.TransfersChanged += RefreshAll;
			_bankrollProgram.AutoRechargeAmountChanged += RefreshAll;
		}

		RefreshAll();
	}

	public override void _ExitTree()
	{
		if (_playerBank != null) _playerBank.BankStateChanged -= RefreshAll;
		if (_principal != null)  _principal.BalanceChanged -= RefreshAll;
		if (_bankrollProgram != null)
		{
			_bankrollProgram.TransfersChanged -= RefreshAll;
			_bankrollProgram.AutoRechargeAmountChanged -= RefreshAll;
		}
	}

	public override void _Process(double delta)
	{
		if (_gameDateLabel != null && _calendarTime != null)
		{
			_gameDateLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Game date: {_calendarTime.CurrentLocalDateTime:yyyy-MM-dd HH:mm:ss}");
		}

		_fallbackTimer += delta;
		if (_fallbackTimer >= FallbackInterval)
		{
			_fallbackTimer = 0;
			RefreshAll();
		}
	}

	private void RefreshAll()
	{
		if (!GodotObject.IsInstanceValid(this) || _playerBank == null) return;

		decimal bank     = _playerBank.BankAccountBalance;
		decimal main     = _principal?.CurrentBalance ?? 0m;
		decimal bankrollBal = _bankroll?.CurrentBalance ?? 0m;
		decimal dose     = _bankrollProgram?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount;

		// D-SF2.7: computed here from the three balance sources; OverallPl vs the canonical 40,000 total start.
		decimal netWorth  = Money.Normalize(bank + main + bankrollBal);
		decimal overallPl = Money.Normalize(netWorth - BankrollProgramService.InitialPrincipalBalanceBaseline);

		_privateBankLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Private Bank Account:  {bank:N8} SC");
		_mainBalanceLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Main Balance:          {main:N8} SC");
		_bankrollLabel.Text    = string.Create(CultureInfo.InvariantCulture, $"Bankroll:              {bankrollBal:N8} SC");
		_netWorthLabel.Text    = string.Create(CultureInfo.InvariantCulture, $"Net Worth (all):       {netWorth:N8} SC");

		_overallPlLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Overall P/L:           {overallPl:+0.00000000;-0.00000000} SC");
		_overallPlLabel.AddThemeColorOverride("font_color", overallPl >= 0m
			? new Color(0.4f, 1f, 0.4f)
			: new Color(1f, 0.4f, 0.4f));

		_doseLabel.Text = string.Create(CultureInfo.InvariantCulture,
			$"Auto-recharge dose:    {dose:N8} SC  (read-only — managed in Bankroll Programmer)");

		_depositAvailableLabel.Text  = string.Create(CultureInfo.InvariantCulture, $"Available: {bank:N8} SC");
		_withdrawAvailableLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Available: {main:N8} SC");

		if (GodotObject.IsInstanceValid(_bankTransferHistoryList))
		{
			var history = _playerBank.BankTransferHistory;
			_bankTransferHistoryList.Clear();
			for (int i = history.Count - 1; i >= 0; i--)
			{
				var r = history[i];
				bool isDeposit = r.Direction == PlayerBankAccountService.DirectionBankToMain;
				string dirWord = isDeposit ? "deposit" : "withdrawal";
				string sign    = isDeposit ? "+" : "-";
				_bankTransferHistoryList.AddItem(string.Create(CultureInfo.InvariantCulture,
					$"{r.GameDateLocal:yyyy-MM-dd HH:mm:ss} | {sign}{r.Amount:N8} SC | {dirWord} | {r.Method}"));
			}
		}
	}

	// ---- Deposits (Bank → Main) ---------------------------------------------------------------------------------

	private void OnAutoDepositToggled(bool pressed)
	{
		if (_syncingToggles || _playerBank == null) return;

		// D-SF3.2: enabling requires a positive refill chunk the bank can cover. If the service rejects, revert
		// the checkbox and explain — turning Auto-Deposit ON at a valid amount is the opt-in extra-lazy path.
		if (!_playerBank.SetAutoDepositEnabled(pressed))
		{
			_depositFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Cannot enable Auto-Deposit — set a Refill chunk that is positive and ≤ your Private Bank Account balance ({_playerBank.BankAccountBalance:N8} SC) first (the bank must hold SC to stream).");
			_syncingToggles = true;
			_autoDepositToggle.ButtonPressed = _playerBank.AutoDepositEnabled;
			_syncingToggles = false;
			return;
		}

		_depositFeedbackLabel.Text = pressed
			? "Auto-Deposit ON — Main auto-refills from your reserve when it runs low (the reserve becomes gamblable)."
			: "Auto-Deposit OFF — your reserve stays safe; retrieve it manually with Deposit.";
		RefreshAll();
	}

	private void OnSetRefillChunkPressed()
	{
		_depositFeedbackLabel.Text = "";
		if (!TryParsePositive(_refillChunkInput.Text, out decimal amount))
		{
			_depositFeedbackLabel.Text = "Invalid refill chunk — enter a positive number.";
			return;
		}
		if (_playerBank == null || !_playerBank.SetAutoDepositAmount(amount))
		{
			_depositFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Refill chunk must be positive and ≤ your Private Bank Account balance ({_playerBank?.BankAccountBalance ?? 0m:N8} SC).");
			return;
		}
		_depositFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Auto-deposit refill chunk set to {amount:N8} SC.");
		RefreshAll();
	}

	private void OnDepositPressed()
	{
		_depositFeedbackLabel.Text = "";
		if (!TryParsePositive(_depositAmountInput.Text, out decimal amount))
		{
			_depositFeedbackLabel.Text = "Invalid amount — enter a positive number.";
			return;
		}
		decimal bank = _playerBank?.BankAccountBalance ?? 0m;
		if (amount > bank) // D-SF2.5: reject over-amount with the available figure
		{
			_depositFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Insufficient funds — available: {bank:N8} SC.");
			return;
		}
		if (_playerBank == null || !_playerBank.TriggerManualDeposit(amount))
		{
			_depositFeedbackLabel.Text = "Deposit failed — the Private Bank Account is empty.";
			return;
		}
		_depositAmountInput.Text = "";
		_depositFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Deposited {amount:N8} SC → Main Balance.");
		RefreshAll();
	}

	// ---- Withdrawals (Main → Bank) ------------------------------------------------------------------------------

	private void OnAutoWithdrawToggled(bool pressed)
	{
		if (_syncingToggles) return;
		ApplyAutoWithdrawSettings();
	}

	private void OnApplyAutoWithdrawPressed() => ApplyAutoWithdrawSettings();

	private void ApplyAutoWithdrawSettings()
	{
		if (_playerBank == null) return;

		decimal floor = TryParsePositive(_floorInput.Text, out decimal f) ? f
			: PlayerBankAccountService.DefaultAutoWithdrawThreshold;
		decimal installment = TryParsePositive(_installmentInput.Text, out decimal a) ? a
			: PlayerBankAccountService.DefaultAutoWithdrawAmount;

		_playerBank.SetAutoWithdrawSettings(_autoWithdrawToggle.ButtonPressed, floor, installment);
		_withdrawFeedbackLabel.Text = _autoWithdrawToggle.ButtonPressed
			? string.Create(CultureInfo.InvariantCulture,
				$"Auto-Withdraw ON — keep at least {floor:N8} SC in Main; move {installment:N8} SC to the bank per event.")
			: "Auto-Withdraw OFF — Main Balance is not swept to the bank.";
		RefreshAll();
	}

	private void OnWithdrawPressed()
	{
		_withdrawFeedbackLabel.Text = "";
		if (!TryParsePositive(_withdrawAmountInput.Text, out decimal amount))
		{
			_withdrawFeedbackLabel.Text = "Invalid amount — enter a positive number.";
			return;
		}
		decimal main = _principal?.CurrentBalance ?? 0m;
		if (amount > main) // D-SF2.5: reject over-amount
		{
			_withdrawFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture,
				$"Insufficient funds — available: {main:N8} SC.");
			return;
		}
		if (_playerBank == null || !_playerBank.TriggerManualWithdrawal(amount))
		{
			_withdrawFeedbackLabel.Text = "Withdrawal failed — insufficient Main Balance.";
			return;
		}
		_withdrawAmountInput.Text = "";
		_withdrawFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Withdrew {amount:N8} SC → Private Bank Account.");
		RefreshAll();
	}

	private static bool TryParsePositive(string raw, out decimal amount)
	{
		amount = 0m;
		raw = (raw ?? string.Empty).Trim().Replace(",", "");
		return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out amount) && amount > 0m;
	}
}
