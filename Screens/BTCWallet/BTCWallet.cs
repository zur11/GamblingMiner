using Godot;
using System;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
#nullable enable

public partial class BTCWallet : Control
{
	private enum WalletMode { Base, PassphraseLocked, PassphraseUnlocked }

	private SceneManager? _sceneManager;
	private NetworkRoot _networkRoot = null!;

	private VBoxContainer _baseWalletPanel = null!;
	private VBoxContainer _passphraseLockedPanel = null!;
	private VBoxContainer _passphraseUnlockedPanel = null!;

	private Label _baseAddressLabel = null!;
	private Label _baseBalanceLabel = null!;
	private Label _basePendingLabel = null!;

	private LineEdit _passphraseInput = null!;

	private Label _passphraseAddressLabel = null!;
	private Label _passphraseBalanceLabel = null!;
	private Label _passphrasePendingLabel = null!;

	private Panel _seedPopup = null!;
	private Label _seedWord1Label = null!;
	private Label _seedWord2Label = null!;
	private Label _seedWord3Label = null!;

	private WalletMode _currentMode = WalletMode.Base;
	private string _currentPassphraseAddress = string.Empty;
	private double _balanceRefreshTimer = 0d;
	private const double BalanceRefreshInterval = 2.0;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_baseWalletPanel        = GetNode<VBoxContainer>("%BaseWalletPanel");
		_passphraseLockedPanel  = GetNode<VBoxContainer>("%PassphraseLockedPanel");
		_passphraseUnlockedPanel = GetNode<VBoxContainer>("%PassphraseUnlockedPanel");

		_baseAddressLabel  = GetNode<Label>("%BaseAddressLabel");
		_baseBalanceLabel  = GetNode<Label>("%BaseBalanceLabel");
		_basePendingLabel  = GetNode<Label>("%BasePendingLabel");

		_passphraseInput = GetNode<LineEdit>("%PassphraseInput");

		_passphraseAddressLabel  = GetNode<Label>("%PassphraseAddressLabel");
		_passphraseBalanceLabel  = GetNode<Label>("%PassphraseBalanceLabel");
		_passphrasePendingLabel  = GetNode<Label>("%PassphrasePendingLabel");

		_seedPopup      = GetNode<Panel>("%SeedPopup");
		_seedWord1Label = GetNode<Label>("%SeedWord1Label");
		_seedWord2Label = GetNode<Label>("%SeedWord2Label");
		_seedWord3Label = GetNode<Label>("%SeedWord3Label");

		GetNode<Button>("%BackBtn").Pressed              += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);
		GetNode<Button>("%CopyBaseAddressBtn").Pressed   += OnCopyBaseAddressPressed;
		GetNode<Button>("%SendBtcBtn").Pressed           += OnSendBtcPressed;
		GetNode<Button>("%OpenPassphraseBtn").Pressed    += OnOpenPassphrasePressed;

		GetNode<Button>("%UnlockPassphraseBtn").Pressed          += OnUnlockPassphrasePressed;
		GetNode<Button>("%BackFromPassphraseLockedBtn").Pressed  += OnBackToBaseWalletPressed;

		GetNode<Button>("%SendBtcPassphraseBtn").Pressed         += OnSendBtcPressed;
		GetNode<Button>("%BackToBaseWalletBtn").Pressed          += OnBackToBaseWalletPressed;
		GetNode<Button>("%CopyPassphraseAddressBtn").Pressed     += OnCopyPassphraseAddressPressed;

		GetNode<Button>("%CopySeedBtn").Pressed    += OnCopySeedPressed;
		GetNode<Button>("%ConfirmSeedBtn").Pressed += OnConfirmSeedPressed;

		// Also unlock on Enter key in passphrase input
		_passphraseInput.TextSubmitted += _ => OnUnlockPassphrasePressed();

		InitializeBaseWallet();
		SetMode(WalletMode.Base);
		ShowSeedPopupIfNeeded();
	}

	public override void _Process(double delta)
	{
		_balanceRefreshTimer += delta;
		if (_balanceRefreshTimer >= BalanceRefreshInterval)
		{
			_balanceRefreshTimer = 0d;
			RefreshBalances();
		}
	}

	private void InitializeBaseWallet()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet == null) return;
		_baseAddressLabel.Text = wallet.BaseAddress;
		RefreshBalances();
	}

	private void RefreshBalances()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet == null) return;

		(decimal confirmed, decimal pendingOut) = _networkRoot.GetAddressBalanceDetails(wallet.BaseAddress);
		_baseBalanceLabel.Text = $"Balance: {confirmed:F8} BTC";
		_basePendingLabel.Visible = pendingOut > 0m;
		if (pendingOut > 0m)
			_basePendingLabel.Text = $"Pending outgoing: {pendingOut:F8} BTC";

		if (_currentMode == WalletMode.PassphraseUnlocked && !string.IsNullOrEmpty(_currentPassphraseAddress))
		{
			(decimal pConfirmed, decimal pPendingOut) = _networkRoot.GetAddressBalanceDetails(_currentPassphraseAddress);
			_passphraseBalanceLabel.Text = $"Balance: {pConfirmed:F8} BTC";
			_passphrasePendingLabel.Visible = pPendingOut > 0m;
			if (pPendingOut > 0m)
				_passphrasePendingLabel.Text = $"Pending outgoing: {pPendingOut:F8} BTC";
		}
	}

	private void SetMode(WalletMode mode)
	{
		_currentMode = mode;
		_baseWalletPanel.Visible        = mode == WalletMode.Base;
		_passphraseLockedPanel.Visible  = mode == WalletMode.PassphraseLocked;
		_passphraseUnlockedPanel.Visible = mode == WalletMode.PassphraseUnlocked;
	}

	private void ShowSeedPopupIfNeeded()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet == null || wallet.HasSeenSeedPopup) return;

		_seedWord1Label.Text = wallet.SeedWords.Length > 0 ? wallet.SeedWords[0] : "---";
		_seedWord2Label.Text = wallet.SeedWords.Length > 1 ? wallet.SeedWords[1] : "---";
		_seedWord3Label.Text = wallet.SeedWords.Length > 2 ? wallet.SeedWords[2] : "---";
		_seedPopup.Visible = true;
	}

	private void OnCopyBaseAddressPressed()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet != null) DisplayServer.ClipboardSet(wallet.BaseAddress);
	}

	private void OnSendBtcPressed()
	{
		// Send BTC — reserved for Phase 6 (DevTransferTool / BTCWallet send flow)
	}

	private void OnOpenPassphrasePressed()
	{
		_passphraseInput.Text = string.Empty;
		SetMode(WalletMode.PassphraseLocked);
	}

	private void OnUnlockPassphrasePressed()
	{
		string passphrase = _passphraseInput.Text.Trim();
		if (string.IsNullOrEmpty(passphrase)) return;

		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet == null) return;

		string seedPhrase = string.Join(" ", wallet.SeedWords) + " " + passphrase;
		_currentPassphraseAddress = CryptoUtils.DeriveGmAddress(seedPhrase);
		_passphraseAddressLabel.Text = _currentPassphraseAddress;
		_passphraseInput.Text = string.Empty;

		SetMode(WalletMode.PassphraseUnlocked);
		RefreshBalances();
	}

	private void OnBackToBaseWalletPressed()
	{
		_currentPassphraseAddress = string.Empty;
		_passphraseInput.Text = string.Empty;
		SetMode(WalletMode.Base);
	}

	private void OnCopyPassphraseAddressPressed()
	{
		if (!string.IsNullOrEmpty(_currentPassphraseAddress))
			DisplayServer.ClipboardSet(_currentPassphraseAddress);
	}

	private void OnCopySeedPressed()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet != null) DisplayServer.ClipboardSet(string.Join(" ", wallet.SeedWords));
	}

	private void OnConfirmSeedPressed()
	{
		WalletInitializationService.MarkSeedPopupSeen();
		_seedPopup.Visible = false;
	}
}
