using Godot;
using System.Globalization;
using Scripts.Finance;
using UI.StatusBar;

// The player's own external-SC story (Step 12 / SF.3) — mirror of ClientsTransactions, but from the PLAYER's
// side and with a single data source: PlayerBankAccountService.BankTransferHistory (Bank↔Main moves only). The
// starting 40,000 is NOT a bank transfer (Main is funded directly at world start — D-SF3.4), so there is no
// [INITIAL] row here and a fresh world shows an EMPTY list until the player first banks SC. Internal Main↔Bankroll
// movements never appear (they aren't bank flows). See AIHelperFiles/step12-player-sc-finances-plan.md §3.4.
public partial class ScTransactions : Control
{
	private PlayerBankAccountService _playerBank;
	private PrincipalBalanceService  _principal;
	private BankrollStateService     _bankroll;
	private SceneManager             _sceneManager;

	private Label _privateBankLabel;
	private Label _totalDepositedLabel;
	private Label _totalWithdrawnLabel;
	private Label _netInsideLabel;
	private Label _netWorthLabel;
	private VBoxContainer _txListVBox;

	private double _refreshTimer;
	private const double RefreshInterval = 2.0;

	public override void _Ready()
	{
		_playerBank   = GetNodeOrNull<PlayerBankAccountService>("/root/PlayerBankAccountService");
		_principal    = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_bankroll     = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_privateBankLabel     = GetNode<Label>("%PrivateBankLabel");
		_totalDepositedLabel  = GetNode<Label>("%TotalDepositedLabel");
		_totalWithdrawnLabel  = GetNode<Label>("%TotalWithdrawnLabel");
		_netInsideLabel       = GetNode<Label>("%NetInsideLabel");
		_netWorthLabel        = GetNode<Label>("%NetWorthLabel");
		_txListVBox           = GetNode<VBoxContainer>("%TxListVBox");

		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.ScFinances);

		if (_playerBank != null) _playerBank.BankStateChanged += RefreshAll;
		if (_principal != null)  _principal.BalanceChanged += RefreshAll;

		RefreshAll();
	}

	public override void _ExitTree()
	{
		if (_playerBank != null) _playerBank.BankStateChanged -= RefreshAll;
		if (_principal != null)  _principal.BalanceChanged -= RefreshAll;
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

	private void RefreshAll()
	{
		if (!GodotObject.IsInstanceValid(this) || _playerBank == null) return;

		decimal bank      = _playerBank.BankAccountBalance;
		decimal deposited = _playerBank.TotalDepositedToCasino;
		decimal withdrawn = _playerBank.TotalWithdrawnFromCasino;
		decimal netInside = Money.Normalize(deposited - withdrawn);
		decimal main      = _principal?.CurrentBalance ?? 0m;
		decimal bankrollBal = _bankroll?.CurrentBalance ?? 0m;
		decimal netWorth  = Money.Normalize(bank + main + bankrollBal);

		_privateBankLabel.Text     = string.Create(CultureInfo.InvariantCulture, $"Private Bank Account:      {bank:N8} SC");
		_totalDepositedLabel.Text  = string.Create(CultureInfo.InvariantCulture, $"Total deposited → casino:  {deposited:N8} SC");
		_totalWithdrawnLabel.Text  = string.Create(CultureInfo.InvariantCulture, $"Total withdrawn → bank:    {withdrawn:N8} SC");
		_netInsideLabel.Text       = string.Create(CultureInfo.InvariantCulture, $"Net inside casino:         {netInside:N8} SC");
		_netWorthLabel.Text        = string.Create(CultureInfo.InvariantCulture, $"Net Worth (all):           {netWorth:N8} SC");

		BuildTxList();
	}

	private void BuildTxList()
	{
		if (!GodotObject.IsInstanceValid(_txListVBox)) return;

		foreach (Node child in _txListVBox.GetChildren())
			child.QueueFree();

		var history = _playerBank.BankTransferHistory;
		for (int i = history.Count - 1; i >= 0; i--)
		{
			var r = history[i];
			bool isDeposit = r.Direction == PlayerBankAccountService.DirectionBankToMain;
			string kindLabel = isDeposit
				? $"[DEPOSIT {r.Method}]"        // Bank → Main — SC re-entering play
				: $"[WITHDRAWAL {r.Method}]";    // Main → Bank — SC parked at the reserve
			Color color = isDeposit
				? new Color(0.4f, 1f, 0.4f)   // green
				: new Color(1f, 0.65f, 0.2f); // orange

			string line = string.Create(CultureInfo.InvariantCulture,
				$"{r.GameDateLocal:yyyy-MM-dd HH:mm:ss}  {kindLabel}  {r.Amount:N8} SC");

			var label = new Label();
			label.Text = line;
			label.AddThemeFontSizeOverride("font_size", 16);
			label.AddThemeColorOverride("font_color", color);
			_txListVBox.AddChild(label);
		}
	}
}
