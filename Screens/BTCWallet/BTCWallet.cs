using Godot;
using System.Collections.Generic;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
#nullable enable

public partial class BTCWallet : Control
{
	private enum WalletMode { Base, PassphraseLocked, PassphraseUnlocked, Send }

	private SceneManager? _sceneManager;
	private NetworkRoot _networkRoot = null!;

	// Mode panels
	private VBoxContainer _baseWalletPanel = null!;
	private VBoxContainer _passphraseLockedPanel = null!;
	private VBoxContainer _passphraseUnlockedPanel = null!;
	private VBoxContainer _sendPanel = null!;

	// Base wallet
	private Label _baseAddressLabel = null!;
	private Label _baseBalanceLabel = null!;
	private Label _basePendingLabel = null!;

	// Passphrase locked
	private LineEdit _passphraseInput = null!;

	// Passphrase unlocked
	private Label _passphraseAddressLabel = null!;
	private Label _passphraseBalanceLabel = null!;
	private Label _passphrasePendingLabel = null!;

	// Seed popup
	private Panel _seedPopup = null!;
	private Label _seedWord1Label = null!;
	private Label _seedWord2Label = null!;
	private Label _seedWord3Label = null!;

	// Send panel controls (built programmatically)
	private Label _sendFromLabel = null!;
	private OptionButton _toDropdown = null!;
	private LineEdit _manualAddressInput = null!;
	private LineEdit _amountInput = null!;
	private Label _sendFeedback = null!;
	private readonly List<string> _toAddresses = new();

	// Runtime state
	private WalletMode _currentMode = WalletMode.Base;
	private string _currentPassphraseAddress = string.Empty;
	private string _currentPassphraseNodeId = string.Empty;
	private string _sendFromNodeId = string.Empty;
	private WalletMode _modeBeforeSend = WalletMode.Base;
	private double _balanceRefreshTimer = 0d;
	private const double BalanceRefreshInterval = 2.0;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());

		_baseWalletPanel         = GetNode<VBoxContainer>("%BaseWalletPanel");
		_passphraseLockedPanel   = GetNode<VBoxContainer>("%PassphraseLockedPanel");
		_passphraseUnlockedPanel = GetNode<VBoxContainer>("%PassphraseUnlockedPanel");

		_baseAddressLabel = GetNode<Label>("%BaseAddressLabel");
		_baseBalanceLabel = GetNode<Label>("%BaseBalanceLabel");
		_basePendingLabel = GetNode<Label>("%BasePendingLabel");

		_passphraseInput = GetNode<LineEdit>("%PassphraseInput");

		_passphraseAddressLabel = GetNode<Label>("%PassphraseAddressLabel");
		_passphraseBalanceLabel = GetNode<Label>("%PassphraseBalanceLabel");
		_passphrasePendingLabel = GetNode<Label>("%PassphrasePendingLabel");

		_seedPopup      = GetNode<Panel>("%SeedPopup");
		_seedWord1Label = GetNode<Label>("%SeedWord1Label");
		_seedWord2Label = GetNode<Label>("%SeedWord2Label");
		_seedWord3Label = GetNode<Label>("%SeedWord3Label");

		GetNode<Button>("%BackBtn").Pressed             += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);
		GetNode<Button>("%CopyBaseAddressBtn").Pressed  += OnCopyBaseAddressPressed;
		GetNode<Button>("%SendBtcBtn").Pressed          += OnSendBtcBasePressed;
		GetNode<Button>("%OpenPassphraseBtn").Pressed   += OnOpenPassphrasePressed;

		GetNode<Button>("%UnlockPassphraseBtn").Pressed         += OnUnlockPassphrasePressed;
		GetNode<Button>("%BackFromPassphraseLockedBtn").Pressed += OnBackToBaseWalletPressed;

		GetNode<Button>("%SendBtcPassphraseBtn").Pressed    += OnSendBtcPassphrasePressed;
		GetNode<Button>("%BackToBaseWalletBtn").Pressed     += OnBackToBaseWalletPressed;
		GetNode<Button>("%CopyPassphraseAddressBtn").Pressed += OnCopyPassphraseAddressPressed;

		GetNode<Button>("%CopySeedBtn").Pressed    += OnCopySeedPressed;
		GetNode<Button>("%ConfirmSeedBtn").Pressed += OnConfirmSeedPressed;

		_passphraseInput.TextSubmitted += _ => OnUnlockPassphrasePressed();

		BuildSendPanel();
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

	// ── Send panel (programmatic) ─────────────────────────────────────────────

	private void BuildSendPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootVBox");

		_sendPanel = new VBoxContainer();
		_sendPanel.AddThemeConstantOverride("separation", 12);
		_sendPanel.Visible = false;

		var title = new Label { Text = "Send BTC" };
		title.AddThemeFontSizeOverride("font_size", 30);
		_sendPanel.AddChild(title);

		_sendFromLabel = new Label();
		_sendFromLabel.AddThemeFontSizeOverride("font_size", 20);
		_sendPanel.AddChild(_sendFromLabel);

		var toRow = new HBoxContainer();
		toRow.AddThemeConstantOverride("separation", 10);
		var toLabel = new Label { Text = "To:" };
		toLabel.AddThemeFontSizeOverride("font_size", 22);
		_toDropdown = new OptionButton();
		_toDropdown.CustomMinimumSize = new Vector2(420, 0);
		_toDropdown.AddThemeFontSizeOverride("font_size", 20);
		toRow.AddChild(toLabel);
		toRow.AddChild(_toDropdown);
		_sendPanel.AddChild(toRow);

		_manualAddressInput = new LineEdit
		{
			PlaceholderText = "Paste gm1q... address",
			CustomMinimumSize = new Vector2(420, 0),
			Visible = false
		};
		_manualAddressInput.AddThemeFontSizeOverride("font_size", 20);
		_sendPanel.AddChild(_manualAddressInput);

		var amountRow = new HBoxContainer();
		amountRow.AddThemeConstantOverride("separation", 10);
		var amtLabel = new Label { Text = "Amount (BTC):" };
		amtLabel.AddThemeFontSizeOverride("font_size", 22);
		_amountInput = new LineEdit
		{
			PlaceholderText = "0.00000000",
			CustomMinimumSize = new Vector2(220, 0)
		};
		_amountInput.AddThemeFontSizeOverride("font_size", 22);
		amountRow.AddChild(amtLabel);
		amountRow.AddChild(_amountInput);
		_sendPanel.AddChild(amountRow);

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 12);
		var sendBtn = new Button { Text = "Send" };
		sendBtn.AddThemeFontSizeOverride("font_size", 26);
		sendBtn.Pressed += OnSendConfirmed;
		var cancelBtn = new Button { Text = "Cancel" };
		cancelBtn.AddThemeFontSizeOverride("font_size", 26);
		cancelBtn.Pressed += OnSendCancelled;
		_sendFeedback = new Label { Text = string.Empty };
		_sendFeedback.AddThemeFontSizeOverride("font_size", 20);
		_sendFeedback.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btnRow.AddChild(sendBtn);
		btnRow.AddChild(cancelBtn);
		btnRow.AddChild(_sendFeedback);
		_sendPanel.AddChild(btnRow);

		rootVBox.AddChild(_sendPanel);

		_toDropdown.ItemSelected += idx =>
			_manualAddressInput.Visible = (_toAddresses.Count > 0 && idx == _toAddresses.Count - 1);
	}

	private void PopulateToDropdown(string excludeAddress)
	{
		_toDropdown.Clear();
		_toAddresses.Clear();
		_manualAddressInput.Visible = false;

		var playerWallet = WalletInitializationService.PlayerWallet;
		if (playerWallet != null && playerWallet.BaseAddress != excludeAddress)
		{
			_toDropdown.AddItem($"Player — {playerWallet.BaseAddress[..10]}...");
			_toAddresses.Add(playerWallet.BaseAddress);
		}

		var casinoWallet = WalletInitializationService.CasinoWallet;
		if (casinoWallet != null)
		{
			_toDropdown.AddItem($"Casino — {casinoWallet.BaseAddress[..10]}...");
			_toAddresses.Add(casinoWallet.BaseAddress);
		}

		foreach (var bot in BotWalletRegistry.AllBots)
		{
			_toDropdown.AddItem($"{bot.NodeId} — {bot.Address[..10]}...");
			_toAddresses.Add(bot.Address);
		}

		_toDropdown.AddItem("── BTC Address ──");
		_toAddresses.Add(string.Empty);
	}

	private void EnterSendMode(string senderNodeId, string senderAddress, WalletMode returnTo)
	{
		_sendFromNodeId = senderNodeId;
		_modeBeforeSend = returnTo;
		_sendFromLabel.Text = $"From: {senderAddress[..10]}...";
		PopulateToDropdown(senderAddress);
		_amountInput.Text = string.Empty;
		_sendFeedback.Text = string.Empty;
		SetMode(WalletMode.Send);
	}

	// ── Balance ───────────────────────────────────────────────────────────────

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
			(decimal pConfirmed, decimal pPending) = _networkRoot.GetAddressBalanceDetails(_currentPassphraseAddress);
			_passphraseBalanceLabel.Text = $"Balance: {pConfirmed:F8} BTC";
			_passphrasePendingLabel.Visible = pPending > 0m;
			if (pPending > 0m)
				_passphrasePendingLabel.Text = $"Pending outgoing: {pPending:F8} BTC";
		}
	}

	// ── Mode management ───────────────────────────────────────────────────────

	private void SetMode(WalletMode mode)
	{
		_currentMode = mode;
		_baseWalletPanel.Visible         = mode == WalletMode.Base;
		_passphraseLockedPanel.Visible   = mode == WalletMode.PassphraseLocked;
		_passphraseUnlockedPanel.Visible = mode == WalletMode.PassphraseUnlocked;
		_sendPanel.Visible               = mode == WalletMode.Send;
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

	// ── Button handlers ───────────────────────────────────────────────────────

	private void OnCopyBaseAddressPressed()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet != null) DisplayServer.ClipboardSet(wallet.BaseAddress);
	}

	private void OnSendBtcBasePressed()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet == null) return;
		EnterSendMode("player", wallet.BaseAddress, WalletMode.Base);
	}

	private void OnSendBtcPassphrasePressed()
	{
		if (string.IsNullOrEmpty(_currentPassphraseNodeId)) return;
		EnterSendMode(_currentPassphraseNodeId, _currentPassphraseAddress, WalletMode.PassphraseUnlocked);
	}

	private void OnSendCancelled() => SetMode(_modeBeforeSend);

	private void OnSendConfirmed()
	{
		if (string.IsNullOrEmpty(_sendFromNodeId)) return;

		int selected = _toDropdown.Selected;
		if (selected < 0 || selected >= _toAddresses.Count)
		{
			_sendFeedback.Text = "Select a recipient.";
			return;
		}

		string recipientAddress;
		if (selected == _toAddresses.Count - 1)
		{
			recipientAddress = _manualAddressInput.Text.Trim();
			if (!Bech32.IsValidGmAddress(recipientAddress))
			{
				_sendFeedback.Text = "Invalid address — must be a valid gm1q... address.";
				return;
			}
		}
		else
		{
			recipientAddress = _toAddresses[selected];
		}

		if (!decimal.TryParse(_amountInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0m)
		{
			_sendFeedback.Text = "Enter a valid positive amount.";
			return;
		}

		Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_sendFromNodeId, recipientAddress, amount);
		if (tx is null)
		{
			_sendFeedback.Text = "Rejected — insufficient balance or invalid route.";
			return;
		}

		string shortId = tx.TransactionId.Length > 8 ? tx.TransactionId[..8] + "..." : tx.TransactionId;
		_sendFeedback.Text = $"Sent! [{shortId}]";
		_amountInput.Text = string.Empty;
		_manualAddressInput.Text = string.Empty;
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

		_currentPassphraseNodeId = _networkRoot.RegisterPassphraseWallet(seedPhrase, _currentPassphraseAddress);

		SetMode(WalletMode.PassphraseUnlocked);
		RefreshBalances();
	}

	private void OnBackToBaseWalletPressed()
	{
		_currentPassphraseAddress = string.Empty;
		_currentPassphraseNodeId = string.Empty;
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
