using System;
using System.Globalization;
using Godot;
using Scripts.Finance;
using UI.StatusBar;

public partial class BankrollProgrammer : Control
{
	private PrincipalBalanceService _principalBalanceService;
	private BankrollStateService _bankrollStateService;
	private BankrollProgramService _bankrollProgramService;
	private CalendarTimeService _calendarTimeService;
	private SceneManager _sceneManager;
	private SimulationService _simulationService;
	private UserStatsService _userStatsService;
	private Wallet _bankrollMirrorWallet;

	private Label _balanceValue;
	private Label _bankrollValue;
	private Label _autoRechargeDoseValue;
	private Label _performanceValue;
	private Label _rechargeCountersValue;
	private LineEdit _autoRechargeAmountInput;
	private LineEdit _manualRechargeToBankrollInput;
	private LineEdit _manualTransferToBalanceInput;
	private ItemList _transfersList;
	private Label _statusValue;
	private CheckBox _autoRechargeEnabledToggle;
	private bool _syncingToggle;

	public override void _Ready()
	{
		_principalBalanceService = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_bankrollStateService = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
		_bankrollProgramService = GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService");
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_simulationService = GetNodeOrNull<SimulationService>("/root/SimulationService");
		_userStatsService = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		_principalBalanceService?.EnsureInitialized();
		_bankrollStateService?.EnsureInitialized(0m);
		_bankrollMirrorWallet = new Wallet(_bankrollStateService?.CurrentBalance ?? 0m);

		_balanceValue = GetNode<Label>("%BalanceValue");
		_bankrollValue = GetNode<Label>("%BankrollValue");
		_autoRechargeDoseValue = GetNode<Label>("%AutoRechargeDoseValue");
		_performanceValue = GetNode<Label>("%PerformanceValue");
		_rechargeCountersValue = GetNode<Label>("%RechargeCountersValue");
		_autoRechargeAmountInput = GetNode<LineEdit>("%AutoRechargeAmountInput");
		_manualRechargeToBankrollInput = GetNode<LineEdit>("%ManualRechargeToBankrollInput");
		_manualTransferToBalanceInput = GetNode<LineEdit>("%ManualTransferToBalanceInput");
		_transfersList = GetNode<ItemList>("%TransfersList");
		_statusValue = GetNode<Label>("%StatusValue");
		_autoRechargeEnabledToggle = GetNode<CheckBox>("%AutoRechargeEnabledToggle");

		GetNode<Button>("%ApplyAutoRechargeAmountBtn").Pressed += OnApplyAutoRechargeAmountPressed;
		GetNode<Button>("%ManualRechargeToBankrollBtn").Pressed += OnManualRechargeToBankrollPressed;
		GetNode<Button>("%TransferToBalanceBtn").Pressed += OnTransferToBalancePressed;
		GetNode<Button>("%ScFinancesBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.ScFinances);
		GetNode<Button>("%BackToDiceBtn").Pressed += OnBackToDicePressed;
		_autoRechargeEnabledToggle.Toggled += OnAutoRechargeEnabledToggled;

		var vbox = GetNode<VBoxContainer>("VBox");
		var statusBar = new StatusBar();
		vbox.AddChild(statusBar);
		vbox.MoveChild(statusBar, 0);

		if (_bankrollProgramService != null)
		{
			_bankrollProgramService.TransfersChanged += RenderAll;
			_bankrollProgramService.AutoRechargeAmountChanged += RenderAll;
			_autoRechargeAmountInput.Text = _bankrollProgramService.AutoRechargeAmount.ToString("F8", CultureInfo.InvariantCulture);
			_syncingToggle = true;
			_autoRechargeEnabledToggle.ButtonPressed = _bankrollProgramService.AutoRechargeEnabled;
			_syncingToggle = false;
		}

		RenderAll();
	}

	public override void _ExitTree()
	{
		if (_bankrollProgramService != null)
		{
			_bankrollProgramService.TransfersChanged -= RenderAll;
			_bankrollProgramService.AutoRechargeAmountChanged -= RenderAll;
		}
	}

	private void OnApplyAutoRechargeAmountPressed()
	{
		if (!TryParseAmount(_autoRechargeAmountInput.Text, out decimal amount))
		{
			_statusValue.Text = "Invalid amount.";
			return;
		}

		decimal mainBalance = Money.Normalize(_principalBalanceService?.CurrentBalance ?? 0m);
		if (amount > mainBalance)
		{
			_statusValue.Text = string.Create(CultureInfo.InvariantCulture,
				$"Dose exceeds available Main Balance ({mainBalance:N8} SC). Enter a lower amount.");
			return;
		}

		_bankrollProgramService?.SetAutoRechargeAmount(amount);
		_statusValue.Text = string.Create(CultureInfo.InvariantCulture, $"Auto-recharge dose updated: {amount:N8} SC");
		RenderAll();
	}

	private void OnManualRechargeToBankrollPressed()
	{
		if (!TryParseAmount(_manualRechargeToBankrollInput.Text, out decimal amount))
		{
			_statusValue.Text = "Invalid amount.";
			return;
		}

		decimal available = _principalBalanceService?.CurrentBalance ?? 0m;
		if (amount > available)
		{
			_statusValue.Text = string.Create(CultureInfo.InvariantCulture,
				$"Insufficient Main Balance. Available: {available:N8} SC.");
			return;
		}

		// While an autobet session is live the transfer must go through the session's own wallet — a plain
		// BankrollStateService write would be overwritten by the next settled bet's write-back, silently
		// destroying the injected SC (Main already paid it).
		if (_simulationService?.IsRunning == true)
		{
			if (!_simulationService.TryManualTransferToBankroll(amount))
			{
				_statusValue.Text = "Transfer failed.";
				return;
			}

			_manualRechargeToBankrollInput.Text = "";
			_statusValue.Text = string.Create(CultureInfo.InvariantCulture,
				$"Recharged {amount:N8} SC to Bankroll. Bankroll now: {_bankrollStateService?.CurrentBalance ?? 0m:N8} SC.");
			RenderAll();
			return;
		}

		decimal currentBankroll = Money.Normalize(_bankrollStateService?.CurrentBalance ?? 0m);
		_bankrollMirrorWallet ??= new Wallet(currentBankroll);
		_bankrollMirrorWallet.SetBalanceForTimeTravel(currentBankroll);

		bool ok = _bankrollProgramService != null &&
			_principalBalanceService != null &&
			_bankrollMirrorWallet != null &&
			_bankrollProgramService.TryTransferBalanceToBankroll(
				_principalBalanceService, _bankrollMirrorWallet, amount, "manual_recharge");

		if (!ok)
		{
			_statusValue.Text = "Transfer failed.";
			return;
		}

		_bankrollStateService?.SetBalance(_bankrollMirrorWallet.Balance);
		// Stats parity with the auto-recharge paths (SimulationService / DiceGame's TryProgrammedBankrollTransfer):
		// a manual recharge also resets the since-recharge stats scope. Game time, never wall-clock (§24.10).
		_userStatsService?.RegisterDeposit(amount, _bankrollMirrorWallet.Balance,
			_calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow);
		_manualRechargeToBankrollInput.Text = "";
		_statusValue.Text = string.Create(CultureInfo.InvariantCulture,
			$"Recharged {amount:N8} SC to Bankroll. Bankroll now: {_bankrollMirrorWallet.Balance:N8} SC.");
		RenderAll();
	}

	private void OnTransferToBalancePressed()
	{
		if (!TryParseAmount(_manualTransferToBalanceInput.Text, out decimal amount))
		{
			_statusValue.Text = "Invalid amount.";
			return;
		}

		decimal currentBankroll = Money.Normalize(_bankrollStateService?.CurrentBalance ?? 0m);
		decimal effectiveAmount = Money.Normalize(Math.Min(amount, currentBankroll));
		if (effectiveAmount <= 0m)
		{
			_statusValue.Text = "No transferable balance.";
			return;
		}

		// Same session-live rule as the recharge above: mutating only BankrollStateService while a session
		// runs would let the next bet's write-back restore the withdrawn amount — duplicating SC.
		if (_simulationService?.IsRunning == true)
		{
			if (!_simulationService.TryManualTransferToBalance(effectiveAmount))
			{
				_statusValue.Text = "Could not transfer from bankroll to Main Balance.";
				return;
			}

			decimal remaining = _bankrollStateService?.CurrentBalance ?? 0m;
			string sessionEmptyHint = remaining <= 0m
				? " Bankroll is now empty — the running session will stop on its next bet."
				: string.Empty;
			_statusValue.Text = string.Create(CultureInfo.InvariantCulture,
				$"Transferred {effectiveAmount:N8} SC to Main Balance. Bankroll remaining: {remaining:N8} SC.{sessionEmptyHint}");
			RenderAll();
			return;
		}

		_bankrollMirrorWallet ??= new Wallet(currentBankroll);
		_bankrollMirrorWallet.SetBalanceForTimeTravel(currentBankroll);

		bool ok = _bankrollProgramService != null &&
			_principalBalanceService != null &&
			_bankrollMirrorWallet != null &&
			_bankrollProgramService.TryTransferBankrollToBalance(_principalBalanceService, _bankrollMirrorWallet, effectiveAmount, "manual_return");
		if (!ok)
		{
			_statusValue.Text = "Could not transfer from bankroll to Main Balance.";
			return;
		}

		_bankrollStateService?.SetBalance(_bankrollMirrorWallet.Balance);
		string emptyHint = _bankrollMirrorWallet.Balance <= 0m
			? " Bankroll is now empty — time stops until funds are added."
			: string.Empty;
		_statusValue.Text = string.Create(CultureInfo.InvariantCulture,
			$"Transferred {effectiveAmount:N8} SC to Main Balance. Bankroll remaining: {_bankrollMirrorWallet.Balance:N8} SC.{emptyHint}");
		RenderAll();
	}

	private void OnBackToDicePressed()
	{
		_sceneManager?.Go(SceneManager.SceneId.MainMenu);
	}

	// SF.2.8 (D-SF.4): the service-level off-switch for the Bankroll dose recharge. Persists + snapshots at a block.
	private void OnAutoRechargeEnabledToggled(bool pressed)
	{
		if (_syncingToggle) return;
		_bankrollProgramService?.SetAutoRechargeEnabled(pressed);
		_statusValue.Text = pressed
			? "Auto-recharge ENABLED — bankroll auto-tops-up from Main Balance when it runs low."
			: "Auto-recharge DISABLED — betting stops on an empty bankroll and waits for a manual recharge.";
	}

	private void RenderAll()
	{
		if (!GodotObject.IsInstanceValid(this) ||
			!GodotObject.IsInstanceValid(_balanceValue) ||
			!GodotObject.IsInstanceValid(_bankrollValue) ||
			!GodotObject.IsInstanceValid(_autoRechargeDoseValue) ||
			!GodotObject.IsInstanceValid(_performanceValue) ||
			!GodotObject.IsInstanceValid(_rechargeCountersValue) ||
			!GodotObject.IsInstanceValid(_transfersList))
		{
			return;
		}

		decimal balance = _principalBalanceService?.CurrentBalance ?? 0m;
		decimal bankroll = _bankrollStateService?.CurrentBalance ?? 0m;
		_balanceValue.Text = string.Create(CultureInfo.InvariantCulture, $"{balance:F8} SC");
		_bankrollValue.Text = string.Create(CultureInfo.InvariantCulture, $"{bankroll:F8} SC");
		_autoRechargeDoseValue.Text = string.Create(CultureInfo.InvariantCulture,
			$"{_bankrollProgramService?.AutoRechargeAmount ?? 0m:N8} SC");

		decimal perf = _bankrollProgramService?.GetPerformancePercentVsInitial(balance) ?? 0m;
		// SF.1.6 / D-SF2.7: this measures MAIN BALANCE alone vs its 40,000 start — NOT net worth (which now
		// includes the Private Bank Account, computed in ScFinances). Labeled explicitly to avoid confusion.
		_performanceValue.Text = string.Create(CultureInfo.InvariantCulture,
			$"{perf:+0.00000000;-0.00000000;0.00000000}% (Main Balance vs 40,000.00000000 SC start)");

		DateTime gameUtcNow = _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow;
		var counts = _bankrollProgramService?.GetAutoRechargeCounts(gameUtcNow) ?? (0, 0, 0);
		int total = _bankrollProgramService?.AutoRechargeCount ?? 0;
		// English-only per the language policy — this line had survived in Spanish.
		_rechargeCountersValue.Text = $"Total: {total} | Day: {counts.Item1} | Week: {counts.Item2} | Month: {counts.Item3}";

		_transfersList.Clear();
		if (_bankrollProgramService == null)
		{
			return;
		}

		foreach (var rec in _bankrollProgramService.Records)
		{
			string when = rec.UtcTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
			string dir = rec.Direction == "balance_to_bankroll" ? "BAL->BR" : "BR->BAL";
			_transfersList.AddItem(string.Create(CultureInfo.InvariantCulture,
				$"{when} | {dir} | {rec.Amount:F8} SC | {rec.Reason}"));
		}
	}

	private static bool TryParseAmount(string text, out decimal value)
	{
		text = (text ?? string.Empty).Trim().Replace(',', '.');
		return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0m;
	}
}
