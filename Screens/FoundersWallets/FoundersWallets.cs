using Godot;
using System.Collections.Generic;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
using UI.NotepadPopup;
#nullable enable

// Dev scene for the founder wallets (Satoshi & Hal — room for Mike Hearn later).
// Mirrors CasinoFinances, parameterised by the currently selected founder.
public partial class FoundersWallets : Control
{
	private enum WalletMode { Base, PassphraseLocked, PassphraseUnlocked, Send }

	private NetworkRoot _networkRoot = null!;
	private SceneManager? _sceneManager;

	// Founder selection
	private FounderWalletState? _currentFounder;
	private Button _satoshiBtn = null!;
	private Button _halBtn = null!;
	private Label _founderTitle = null!;

	// Mode panels
	private VBoxContainer _baseWalletPanel = null!;
	private VBoxContainer _passphraseLockedPanel = null!;
	private VBoxContainer _passphraseUnlockedPanel = null!;
	private VBoxContainer _sendPanel = null!;

	// Base wallet display
	private Label _addressLabel = null!;
	private Label _balanceLabel = null!;
	private Label _pendingLabel = null!;

	// Passphrase locked
	private LineEdit _passphraseInput = null!;

	// Passphrase unlocked
	private Label _passphraseAddressLabel = null!;
	private Label _passBalanceLabel = null!;
	private Label _passPendingLabel = null!;

	// Seed words popup
	private Panel _seedWordsPopup = null!;
	private Label _seedWordsLabel = null!;
	private Label _seedPopupTitle = null!;

	// Notepad
	private NotepadPopup _notepadPopup = null!;

	// Send panel controls (built programmatically)
	private Label _sendFromLabel = null!;
	private OptionButton _toDropdown = null!;
	private LineEdit _manualAddressInput = null!;
	private LineEdit _amountInput = null!;
	private Label _sendFeedback = null!;
	private readonly List<string> _toAddresses = new();

	// Runtime state
	private string? _currentPassphraseAddress;
	private string _currentPassphraseNodeId = string.Empty;
	private string _sendFromNodeId = string.Empty;
	private WalletMode _modeBeforeSend = WalletMode.Base;
	private double _refreshTimer;
	private const double RefreshInterval = 2.0;

	public override void _Ready()
	{
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		// Founder selector
		_satoshiBtn = GetNode<Button>("%SatoshiBtn");
		_halBtn     = GetNode<Button>("%HalBtn");
		_satoshiBtn.Pressed += () => SelectFounder(WalletInitializationService.SatoshiWallet);
		_halBtn.Pressed     += () => SelectFounder(WalletInitializationService.HalWallet);

		// Base wallet
		_baseWalletPanel = GetNode<VBoxContainer>("%BaseWalletPanel");
		_founderTitle    = GetNode<Label>("%Title");
		_addressLabel    = GetNode<Label>("%AddressLabel");
		_balanceLabel    = GetNode<Label>("%BalanceLabel");
		_pendingLabel    = GetNode<Label>("%PendingLabel");

		GetNode<Button>("%CopyAddressBtn").Pressed +=
			() => { if (_currentFounder != null) DisplayServer.ClipboardSet(_currentFounder.BaseAddress); };
		GetNode<Button>("%ShowSeedWordsBtn").Pressed  += OnShowSeedWordsPressed;
		GetNode<Button>("%OpenPassphraseBtn").Pressed += () => SetMode(WalletMode.PassphraseLocked);
		GetNode<Button>("%SendBtcBtn").Pressed        += OnSendBtcBasePressed;

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
		GetNode<Button>("%SendBtcPassphraseBtn").Pressed += OnSendBtcPassphrasePressed;
		GetNode<Button>("%BackToBaseBtn2").Pressed       += () => SetMode(WalletMode.Base);

		// Seed words popup
		_seedWordsPopup = GetNode<Panel>("%SeedWordsPopup");
		_seedWordsLabel = GetNode<Label>("%SeedWordsLabel");
		_seedPopupTitle = GetNode<Label>("%PopupTitle");
		GetNode<Button>("%CopySeedWordsBtn").Pressed += OnCopySeedWordsPressed;
		GetNode<Button>("%ClosePopupBtn").Pressed    += () => _seedWordsPopup.Visible = false;

		_notepadPopup = new NotepadPopup();
		AddChild(_notepadPopup);
		GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

		BuildSendPanel();

		// Default to Satoshi.
		SelectFounder(WalletInitializationService.SatoshiWallet);
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;
		RefreshBalances();
	}

	// ── Founder selection ──────────────────────────────────────────────────────

	private void SelectFounder(FounderWalletState? founder)
	{
		_currentFounder = founder;
		_satoshiBtn.ButtonPressed = founder?.FounderId == "satoshi";
		_halBtn.ButtonPressed     = founder?.FounderId == "hal";

		string displayName = founder?.FounderId switch
		{
			"satoshi" => "Satoshi Nakamoto",
			"hal"     => "Hal Finney",
			_         => "Founder"
		};
		_founderTitle.Text = $"{displayName} Wallet";
		_addressLabel.Text = founder?.BaseAddress ?? "—";

		SetMode(WalletMode.Base);
	}

	// ── Send panel (programmatic) ─────────────────────────────────────────────

	private void BuildSendPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootVBox");

		_sendPanel = new VBoxContainer();
		_sendPanel.AddThemeConstantOverride("separation", 12);
		_sendPanel.Visible = false;

		var title = new Label { Text = "Send BTC" };
		title.AddThemeFontSizeOverride("font_size", 22);
		_sendPanel.AddChild(title);

		_sendFromLabel = new Label();
		_sendFromLabel.AddThemeFontSizeOverride("font_size", 18);
		_sendPanel.AddChild(_sendFromLabel);

		var toRow = new HBoxContainer();
		toRow.AddThemeConstantOverride("separation", 10);
		var toLabel = new Label { Text = "To:" };
		toLabel.AddThemeFontSizeOverride("font_size", 20);
		_toDropdown = new OptionButton();
		_toDropdown.CustomMinimumSize = new Vector2(400, 0);
		_toDropdown.AddThemeFontSizeOverride("font_size", 18);
		toRow.AddChild(toLabel);
		toRow.AddChild(_toDropdown);
		_sendPanel.AddChild(toRow);

		_manualAddressInput = new LineEdit
		{
			PlaceholderText = "Paste gm1q... address",
			CustomMinimumSize = new Vector2(400, 0),
			Visible = false
		};
		_manualAddressInput.AddThemeFontSizeOverride("font_size", 18);
		_sendPanel.AddChild(_manualAddressInput);

		var amountRow = new HBoxContainer();
		amountRow.AddThemeConstantOverride("separation", 10);
		var amtLabel = new Label { Text = "Amount (BTC):" };
		amtLabel.AddThemeFontSizeOverride("font_size", 20);
		_amountInput = new LineEdit
		{
			PlaceholderText = "0.00000000",
			CustomMinimumSize = new Vector2(200, 0)
		};
		_amountInput.AddThemeFontSizeOverride("font_size", 20);
		amountRow.AddChild(amtLabel);
		amountRow.AddChild(_amountInput);
		_sendPanel.AddChild(amountRow);

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 12);
		var sendBtn = new Button { Text = "Send" };
		sendBtn.AddThemeFontSizeOverride("font_size", 22);
		sendBtn.Pressed += OnSendConfirmed;
		var cancelBtn = new Button { Text = "Cancel" };
		cancelBtn.AddThemeFontSizeOverride("font_size", 22);
		cancelBtn.Pressed += OnSendCancelled;
		_sendFeedback = new Label { Text = string.Empty };
		_sendFeedback.AddThemeFontSizeOverride("font_size", 18);
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

		void AddTarget(string label, string address)
		{
			if (address == excludeAddress) return;
			_toDropdown.AddItem($"{label} — {address[..10]}...");
			_toAddresses.Add(address);
		}

		var playerWallet = WalletInitializationService.PlayerWallet;
		if (playerWallet != null) AddTarget("Player", playerWallet.BaseAddress);

		var casinoWallet = WalletInitializationService.CasinoWallet;
		if (casinoWallet != null) AddTarget("Casino", casinoWallet.BaseAddress);

		var satoshi = WalletInitializationService.SatoshiWallet;
		if (satoshi != null) AddTarget("Satoshi", satoshi.BaseAddress);

		var hal = WalletInitializationService.HalWallet;
		if (hal != null) AddTarget("Hal", hal.BaseAddress);

		foreach (var bot in BotWalletRegistry.AllBots)
			AddTarget(bot.NodeId, bot.Address);

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

	private void RefreshBalances()
	{
		if (_currentFounder == null) return;

		(decimal confirmed, decimal pendingOut) = _networkRoot.GetAddressBalanceDetails(_currentFounder.BaseAddress);
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

	// ── Mode management ───────────────────────────────────────────────────────

	private void SetMode(WalletMode mode)
	{
		_baseWalletPanel.Visible         = mode == WalletMode.Base;
		_passphraseLockedPanel.Visible   = mode == WalletMode.PassphraseLocked;
		_passphraseUnlockedPanel.Visible = mode == WalletMode.PassphraseUnlocked;
		_sendPanel.Visible               = mode == WalletMode.Send;

		if (mode != WalletMode.PassphraseUnlocked && mode != WalletMode.Send)
			_passphraseInput.Text = string.Empty;

		if (mode == WalletMode.Base)
		{
			_currentPassphraseAddress = null;
			_currentPassphraseNodeId = string.Empty;
		}

		RefreshBalances();
	}

	// ── Button handlers ───────────────────────────────────────────────────────

	private void OnSendBtcBasePressed()
	{
		if (_currentFounder == null) return;
		EnterSendMode(_currentFounder.FounderId, _currentFounder.BaseAddress, WalletMode.Base);
	}

	private void OnSendBtcPassphrasePressed()
	{
		if (string.IsNullOrEmpty(_currentPassphraseNodeId) || _currentPassphraseAddress == null) return;
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

	private void OnUnlockPressed()
	{
		if (_currentFounder == null) return;
		string passphrase = _passphraseInput.Text.Trim();
		if (string.IsNullOrEmpty(passphrase)) return;

		string seedPhrase = string.Join(" ", _currentFounder.SeedWords) + " " + passphrase;
		_currentPassphraseAddress = CryptoUtils.DeriveGmAddress(seedPhrase);
		_passphraseInput.Text = string.Empty;
		_passphraseAddressLabel.Text = _currentPassphraseAddress;

		_currentPassphraseNodeId = _networkRoot.RegisterPassphraseWallet(seedPhrase, _currentPassphraseAddress);

		SetMode(WalletMode.PassphraseUnlocked);
	}

	private void OnShowSeedWordsPressed()
	{
		if (_currentFounder == null) return;
		string displayName = _currentFounder.FounderId switch
		{
			"satoshi" => "Satoshi",
			"hal"     => "Hal Finney",
			_         => "Founder"
		};
		_seedPopupTitle.Text = $"{displayName} Seed Words";
		_seedWordsLabel.Text = string.Join("   ", _currentFounder.SeedWords);
		_seedWordsPopup.Visible = true;
	}

	private void OnCopySeedWordsPressed()
	{
		if (_currentFounder == null) return;
		DisplayServer.ClipboardSet(string.Join(" ", _currentFounder.SeedWords));
	}
}
