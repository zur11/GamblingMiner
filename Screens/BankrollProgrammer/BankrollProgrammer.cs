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
	private SceneManager _sceneManager;
	private Wallet _bankrollMirrorWallet;

	private Label _balanceValue;
	private Label _bankrollValue;
	private Label _autoRechargeDoseValue;
	private Label _performanceValue;
	private Label _rechargeCountersValue;
	private LineEdit _autoRechargeAmountInput;
	private LineEdit _manualTransferToBalanceInput;
	private ItemList _transfersList;
	private Label _statusValue;

	public override void _Ready()
	{
		_principalBalanceService = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_bankrollStateService = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
		_bankrollProgramService = GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_principalBalanceService?.EnsureInitialized();
		_bankrollStateService?.EnsureInitialized(0m);
		_bankrollMirrorWallet = new Wallet(_bankrollStateService?.CurrentBalance ?? 0m);

		_balanceValue = GetNode<Label>("%BalanceValue");
		_bankrollValue = GetNode<Label>("%BankrollValue");
		_autoRechargeDoseValue = GetNode<Label>("%AutoRechargeDoseValue");
		_performanceValue = GetNode<Label>("%PerformanceValue");
		_rechargeCountersValue = GetNode<Label>("%RechargeCountersValue");
		_autoRechargeAmountInput = GetNode<LineEdit>("%AutoRechargeAmountInput");
		_manualTransferToBalanceInput = GetNode<LineEdit>("%ManualTransferToBalanceInput");
		_transfersList = GetNode<ItemList>("%TransfersList");
		_statusValue = GetNode<Label>("%StatusValue");

		GetNode<Button>("%ApplyAutoRechargeAmountBtn").Pressed += OnApplyAutoRechargeAmountPressed;
		GetNode<Button>("%TransferToBalanceBtn").Pressed += OnTransferToBalancePressed;
		GetNode<Button>("%BackToDiceBtn").Pressed += OnBackToDicePressed;

		var vbox = GetNode<VBoxContainer>("VBox");
		var statusBar = new StatusBar();
		vbox.AddChild(statusBar);
		vbox.MoveChild(statusBar, 0);

		if (_bankrollProgramService != null)
		{
			_bankrollProgramService.TransfersChanged += RenderAll;
			_bankrollProgramService.AutoRechargeAmountChanged += RenderAll;
			_autoRechargeAmountInput.Text = _bankrollProgramService.AutoRechargeAmount.ToString("F8", CultureInfo.InvariantCulture);
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
		_statusValue.Text = string.Create(CultureInfo.InvariantCulture, $"Auto-recharge dose updated: {amount:N8}");
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
			$"Transferred {effectiveAmount:N8} to Main Balance. Bankroll remaining: {_bankrollMirrorWallet.Balance:N8}.{emptyHint}");
		RenderAll();
	}

	private void OnBackToDicePressed()
	{
		_sceneManager?.Go(SceneManager.SceneId.MainMenu);
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
		_balanceValue.Text = balance.ToString("F8", CultureInfo.InvariantCulture);
		_bankrollValue.Text = bankroll.ToString("F8", CultureInfo.InvariantCulture);
		_autoRechargeDoseValue.Text = (_bankrollProgramService?.AutoRechargeAmount ?? 0m)
			.ToString("N8", CultureInfo.InvariantCulture);

		decimal perf = _bankrollProgramService?.GetPerformancePercentVsInitial(balance) ?? 0m;
		_performanceValue.Text = $"{perf:+0.00000000;-0.00000000;0.00000000}% vs 40000.00000000";

		var counts = _bankrollProgramService?.GetAutoRechargeCounts(DateTime.UtcNow) ?? (0, 0, 0);
		int total = _bankrollProgramService?.AutoRechargeCount ?? 0;
		_rechargeCountersValue.Text = $"Total: {total} | Dia: {counts.Item1} | Semana: {counts.Item2} | Mes: {counts.Item3}";

		_transfersList.Clear();
		if (_bankrollProgramService == null)
		{
			return;
		}

		foreach (var rec in _bankrollProgramService.Records)
		{
			string when = rec.UtcTimestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
			string dir = rec.Direction == "balance_to_bankroll" ? "BAL->BR" : "BR->BAL";
			_transfersList.AddItem($"{when} | {dir} | {rec.Amount:F8} | {rec.Reason}");
		}
	}

	private static bool TryParseAmount(string text, out decimal value)
	{
		text = (text ?? string.Empty).Trim().Replace(',', '.');
		return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value > 0m;
	}
}
