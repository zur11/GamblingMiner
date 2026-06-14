using Godot;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
#nullable enable

public partial class CasinoFinances : Control
{
	private enum WalletMode { Base, PassphraseLocked, PassphraseUnlocked }

	private NetworkRoot _networkRoot = null!;
	private SceneManager? _sceneManager;
	private CasinoWalletState? _casinoWallet;

	private VBoxContainer _baseWalletPanel = null!;
	private Label _addressLabel = null!;
	private Label _balanceLabel = null!;
	private Label _pendingLabel = null!;

	private VBoxContainer _passphraseLockedPanel = null!;
	private LineEdit _passphraseInput = null!;

	private VBoxContainer _passphraseUnlockedPanel = null!;
	private Label _passphraseAddressLabel = null!;
	private Label _passBalanceLabel = null!;
	private Label _passPendingLabel = null!;
	private string? _currentPassphraseAddress;

	private Panel _seedWordsPopup = null!;
	private Label _seedWordsLabel = null!;

	private double _refreshTimer;
	private const double RefreshInterval = 2.0;

	public override void _Ready()
	{
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_casinoWallet = WalletInitializationService.CasinoWallet;

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		// Base wallet
		_baseWalletPanel = GetNode<VBoxContainer>("%BaseWalletPanel");
		_addressLabel    = GetNode<Label>("%AddressLabel");
		_balanceLabel    = GetNode<Label>("%BalanceLabel");
		_pendingLabel    = GetNode<Label>("%PendingLabel");

		if (_casinoWallet != null)
			_addressLabel.Text = _casinoWallet.BaseAddress;

		GetNode<Button>("%CopyAddressBtn").Pressed +=
			() => { if (_casinoWallet != null) DisplayServer.ClipboardSet(_casinoWallet.BaseAddress); };
		GetNode<Button>("%ShowSeedWordsBtn").Pressed  += OnShowSeedWordsPressed;
		GetNode<Button>("%OpenPassphraseBtn").Pressed += () => SetMode(WalletMode.PassphraseLocked);

		// Passphrase locked
		_passphraseLockedPanel = GetNode<VBoxContainer>("%PassphraseLockedPanel");
		_passphraseInput       = GetNode<LineEdit>("%PassphraseInput");
		_passphraseInput.TextSubmitted += _ => OnUnlockPressed();
		GetNode<Button>("%UnlockBtn").Pressed      += OnUnlockPressed;
		GetNode<Button>("%BackToBaseBtn1").Pressed += () => SetMode(WalletMode.Base);

		// Passphrase unlocked
		_passphraseUnlockedPanel = GetNode<VBoxContainer>("%PassphraseUnlockedPanel");
		_passphraseAddressLabel  = GetNode<Label>("%PassphraseAddressLabel");
		_passBalanceLabel        = GetNode<Label>("%PassBalanceLabel");
		_passPendingLabel        = GetNode<Label>("%PassPendingLabel");

		GetNode<Button>("%CopyPassphraseBtn").Pressed +=
			() => { if (_currentPassphraseAddress != null) DisplayServer.ClipboardSet(_currentPassphraseAddress); };
		GetNode<Button>("%BackToBaseBtn2").Pressed += () => SetMode(WalletMode.Base);

		// Seed words popup
		_seedWordsPopup = GetNode<Panel>("%SeedWordsPopup");
		_seedWordsLabel = GetNode<Label>("%SeedWordsLabel");
		GetNode<Button>("%CopySeedWordsBtn").Pressed += OnCopySeedWordsPressed;
		GetNode<Button>("%ClosePopupBtn").Pressed    += () => _seedWordsPopup.Visible = false;

		SetMode(WalletMode.Base);
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;
		RefreshBalances();
	}

	private void SetMode(WalletMode mode)
	{
		_baseWalletPanel.Visible         = mode == WalletMode.Base;
		_passphraseLockedPanel.Visible   = mode == WalletMode.PassphraseLocked;
		_passphraseUnlockedPanel.Visible = mode == WalletMode.PassphraseUnlocked;

		if (mode != WalletMode.PassphraseUnlocked)
			_passphraseInput.Text = string.Empty;

		if (mode == WalletMode.Base)
			_currentPassphraseAddress = null;

		RefreshBalances();
	}

	private void RefreshBalances()
	{
		if (_casinoWallet == null) return;

		(decimal confirmed, decimal pendingOut) = _networkRoot.GetAddressBalanceDetails(_casinoWallet.BaseAddress);
		_balanceLabel.Text = $"Confirmed balance:  {confirmed:F8} BTC";
		_pendingLabel.Visible = pendingOut > 0m;
		if (pendingOut > 0m)
			_pendingLabel.Text = $"Pending outgoing:   {pendingOut:F8} BTC";

		if (_currentPassphraseAddress == null) return;
		(decimal passConfirmed, decimal passPending) = _networkRoot.GetAddressBalanceDetails(_currentPassphraseAddress);
		_passBalanceLabel.Text = $"Balance:  {passConfirmed:F8} BTC";
		_passPendingLabel.Visible = passPending > 0m;
		if (passPending > 0m)
			_passPendingLabel.Text = $"Pending outgoing:   {passPending:F8} BTC";
	}

	private void OnUnlockPressed()
	{
		if (_casinoWallet == null) return;
		string passphrase = _passphraseInput.Text.Trim();
		if (string.IsNullOrEmpty(passphrase)) return;

		string seedPhrase = string.Join(" ", _casinoWallet.SeedWords) + " " + passphrase;
		_currentPassphraseAddress = CryptoUtils.DeriveGmAddress(seedPhrase);
		_passphraseInput.Text = string.Empty;
		_passphraseAddressLabel.Text = _currentPassphraseAddress;
		SetMode(WalletMode.PassphraseUnlocked);
	}

	private void OnShowSeedWordsPressed()
	{
		if (_casinoWallet == null) return;
		_seedWordsLabel.Text = string.Join("   ", _casinoWallet.SeedWords);
		_seedWordsPopup.Visible = true;
	}

	private void OnCopySeedWordsPressed()
	{
		if (_casinoWallet == null) return;
		DisplayServer.ClipboardSet(string.Join(" ", _casinoWallet.SeedWords));
	}
}
