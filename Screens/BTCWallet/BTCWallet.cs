using Godot;
using System.Collections.Generic;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
using UI.NotepadPopup;
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

	// Seed popup - reveal phase
	private Panel _seedPopup = null!;
	private VBoxContainer _seedRevealPanel = null!;
	private VBoxContainer _seedVerifyPanel = null!;
	private Label _seedWord1Label = null!;
	private Label _seedWord2Label = null!;
	private Label _seedWord3Label = null!;

	// Seed popup - verify phase
	private Label _seedVerifyProgress = null!;
	private Label _seedVerifyPrompt = null!;
	private LineEdit _seedVerifyInput = null!;
	private Label _seedVerifyFeedback = null!;
	private int[] _verifyOrder = new int[3];
	private int _verifyStep;

	// Send panel controls (built programmatically)
	private Label _sendFromLabel = null!;
	private OptionButton _toDropdown = null!;
	private LineEdit _manualAddressInput = null!;
	private LineEdit _amountInput = null!;
	private LineEdit _feeInput = null!;
	private Label _sendFeedback = null!;
	private readonly List<string> _toAddresses = new();

	// Address book (Phase 8.4) — the player's wallet as a collection of addresses (base + change addresses).
	private Button _addressListToggle = null!;
	private CheckBox _showEmptyAddrToggle = null!;
	private VBoxContainer _addressListContainer = null!;
	private bool _addressListExpanded;

	// Notepad
	private NotepadPopup _notepadPopup = null!;

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

		_seedPopup          = GetNode<Panel>("%SeedPopup");
		_seedRevealPanel    = GetNode<VBoxContainer>("%SeedRevealPanel");
		_seedVerifyPanel    = GetNode<VBoxContainer>("%SeedVerifyPanel");
		_seedWord1Label     = GetNode<Label>("%SeedWord1Label");
		_seedWord2Label     = GetNode<Label>("%SeedWord2Label");
		_seedWord3Label     = GetNode<Label>("%SeedWord3Label");
		_seedVerifyProgress = GetNode<Label>("%SeedVerifyProgress");
		_seedVerifyPrompt   = GetNode<Label>("%SeedVerifyPrompt");
		_seedVerifyInput    = GetNode<LineEdit>("%SeedVerifyInput");
		_seedVerifyFeedback = GetNode<Label>("%SeedVerifyFeedback");

		GetNode<Button>("%BackBtn").Pressed             += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);
		GetNode<Button>("%CopyBaseAddressBtn").Pressed  += OnCopyBaseAddressPressed;
		GetNode<Button>("%SendBtcBtn").Pressed          += OnSendBtcBasePressed;
		GetNode<Button>("%OpenPassphraseBtn").Pressed   += OnOpenPassphrasePressed;

		GetNode<Button>("%UnlockPassphraseBtn").Pressed         += OnUnlockPassphrasePressed;
		GetNode<Button>("%BackFromPassphraseLockedBtn").Pressed += OnBackToBaseWalletPressed;

		GetNode<Button>("%SendBtcPassphraseBtn").Pressed     += OnSendBtcPassphrasePressed;
		GetNode<Button>("%BackToBaseWalletBtn").Pressed      += OnBackToBaseWalletPressed;
		GetNode<Button>("%CopyPassphraseAddressBtn").Pressed += OnCopyPassphraseAddressPressed;

		GetNode<Button>("%SeedWrittenBtn").Pressed      += ShowVerifyPhase;
		GetNode<Button>("%SeedVerifySubmitBtn").Pressed += OnVerifySubmit;

		_passphraseInput.TextSubmitted += _ => OnUnlockPassphrasePressed();

		_notepadPopup = new NotepadPopup();
		AddChild(_notepadPopup);
		GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

		BuildSendPanel();
		BuildAddressListSection();
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

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
		if (key.Keycode != Key.Enter && key.Keycode != Key.KpEnter) return;

		if (_seedRevealPanel is not null && _seedRevealPanel.Visible)
		{
			GetViewport().SetInputAsHandled();
			ShowVerifyPhase();
			return;
		}

		if (_seedVerifyPanel is not null && _seedVerifyPanel.Visible)
		{
			GetViewport().SetInputAsHandled();
			OnVerifySubmit();
			if (_seedVerifyPanel.Visible)
				_seedVerifyInput.GrabFocus();
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

		var feeRow = new HBoxContainer();
		feeRow.AddThemeConstantOverride("separation", 10);
		var feeLabel = new Label { Text = "Fee (BTC):" };
		feeLabel.AddThemeFontSizeOverride("font_size", 22);
		_feeInput = new LineEdit
		{
			PlaceholderText = "0.00000000",
			CustomMinimumSize = new Vector2(220, 0)
		};
		_feeInput.AddThemeFontSizeOverride("font_size", 22);
		feeRow.AddChild(feeLabel);
		feeRow.AddChild(_feeInput);
		_sendPanel.AddChild(feeRow);

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
		_feeInput.Text = string.Empty;
		_sendFeedback.Text = string.Empty;
		SetMode(WalletMode.Send);
	}

	// ── Address book (Phase 8.4) ──────────────────────────────────────────────

	// An expandable list showing the wallet as a collection of addresses (base + change addresses), so the
	// player sees that "a wallet is a set of addresses/UTXOs" (OQ-2). Built programmatically and inserted just
	// above the action row of the base wallet panel.
	private void BuildAddressListSection()
	{
		var panel = GetNode<VBoxContainer>("%BaseWalletPanel");

		_addressListToggle = new Button { Text = "Show addresses ▸" };
		_addressListToggle.AddThemeFontSizeOverride("font_size", 20);
		_addressListToggle.Pressed += OnToggleAddressList;
		panel.AddChild(_addressListToggle);

		// Spent change addresses keep a 0.00000000 balance forever (address non-reuse — a real HD wallet never
		// reuses them). Hidden by default so the list stays clean; tick to reveal them. Unchecked = unchecked.
		_showEmptyAddrToggle = new CheckBox { Text = "View empty addresses", ButtonPressed = false, Visible = false };
		_showEmptyAddrToggle.AddThemeFontSizeOverride("font_size", 16);
		_showEmptyAddrToggle.Toggled += _ => RefreshAddressList();
		panel.AddChild(_showEmptyAddrToggle);

		_addressListContainer = new VBoxContainer { Visible = false };
		_addressListContainer.AddThemeConstantOverride("separation", 4);
		panel.AddChild(_addressListContainer);

		// Position all three just after the pending label, before the Send / Open-Passphrase action row.
		int insertAt = _basePendingLabel.GetIndex() + 1;
		panel.MoveChild(_addressListToggle, insertAt);
		panel.MoveChild(_showEmptyAddrToggle, insertAt + 1);
		panel.MoveChild(_addressListContainer, insertAt + 2);
	}

	private void OnToggleAddressList()
	{
		_addressListExpanded = !_addressListExpanded;
		_addressListContainer.Visible = _addressListExpanded;
		_showEmptyAddrToggle.Visible = _addressListExpanded;
		_addressListToggle.Text = _addressListExpanded ? "Hide addresses ▾" : "Show addresses ▸";
		if (_addressListExpanded) RefreshAddressList();
	}

	private void RefreshAddressList()
	{
		foreach (Node child in _addressListContainer.GetChildren())
			child.QueueFree();

		bool showEmpty = _showEmptyAddrToggle.ButtonPressed;
		int hidden = 0;
		foreach ((string address, decimal confirmed, bool isBase) in _networkRoot.GetNodeAddressBook("player"))
		{
			// Hide spent/empty (0-balance) addresses by default — they are never reused, only kept for history.
			if (!showEmpty && confirmed == 0m && !isBase) { hidden++; continue; }

			var row = new Label
			{
				Text = $"{(isBase ? "[base]   " : "[change] ")}{address}   —   {confirmed:F8} BTC"
			};
			row.AddThemeFontSizeOverride("font_size", 16);
			_addressListContainer.AddChild(row);
		}

		if (hidden > 0 && !showEmpty)
		{
			var note = new Label { Text = $"… {hidden} empty (spent) address(es) hidden — tick above to show." };
			note.AddThemeFontSizeOverride("font_size", 14);
			note.Modulate = new Color(1, 1, 1, 0.6f);
			_addressListContainer.AddChild(note);
		}
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

		// Phase 8.4 — aggregate across the wallet's address set (base + any change addresses). The wallet
		// becomes multi-address only after a send produces change; until then it is just the base address.
		var book = _networkRoot.GetNodeAddressBook("player");
		decimal total = 0m;
		decimal pendingOut = 0m;
		foreach ((string address, decimal confirmed, _) in book)
		{
			total += confirmed;
			pendingOut += _networkRoot.GetAddressBalanceDetails(address).pendingOutgoing;
		}

		_baseBalanceLabel.Text = book.Count > 1
			? $"Wallet total ({book.Count} addresses): {total:F8} BTC"
			: $"Balance: {total:F8} BTC";
		_basePendingLabel.Visible = pendingOut > 0m;
		if (pendingOut > 0m)
			_basePendingLabel.Text = $"Pending outgoing: {pendingOut:F8} BTC";

		if (_addressListExpanded) RefreshAddressList();

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
		ShowSeedRevealPhase();
	}

	// ── Seed backup flow ──────────────────────────────────────────────────────

	private void ShowSeedRevealPhase()
	{
		_seedRevealPanel.Visible = true;
		_seedVerifyPanel.Visible = false;
		_seedVerifyInput.Text    = string.Empty;
		_seedVerifyFeedback.Text = string.Empty;
	}

	private void ShowVerifyPhase()
	{
		_verifyOrder = new[] { 0, 1, 2 };
		var rng = new System.Random();
		for (int i = 2; i > 0; i--)
		{
			int j = rng.Next(i + 1);
			(_verifyOrder[i], _verifyOrder[j]) = (_verifyOrder[j], _verifyOrder[i]);
		}
		_verifyStep = 0;
		_seedRevealPanel.Visible = false;
		_seedVerifyPanel.Visible = true;
		ShowVerifyStep();
	}

	private void ShowVerifyStep()
	{
		int wordNumber = _verifyOrder[_verifyStep] + 1;
		_seedVerifyProgress.Text = $"Step {_verifyStep + 1} / 3";
		_seedVerifyPrompt.Text   = $"Enter word #{wordNumber}:";
		_seedVerifyInput.Text    = string.Empty;
		_seedVerifyFeedback.Text = string.Empty;
		_seedVerifyInput.GrabFocus();
	}

	private void OnVerifySubmit()
	{
		var wallet = WalletInitializationService.PlayerWallet;
		if (wallet == null) return;

		string entered  = _seedVerifyInput.Text.Trim();
		string expected = wallet.SeedWords[_verifyOrder[_verifyStep]];

		if (!entered.Equals(expected, System.StringComparison.OrdinalIgnoreCase))
		{
			_seedVerifyFeedback.Text = "Incorrect — review your words carefully and try again.";
			ShowSeedRevealPhase();
			return;
		}

		_verifyStep++;
		if (_verifyStep >= 3)
		{
			WalletInitializationService.MarkSeedPopupSeen();
			_seedPopup.Visible = false;
			return;
		}

		ShowVerifyStep();
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

		// Fee is optional (blank = 0). The fee goes to whichever miner includes the transaction.
		string feeText = _feeInput.Text.Trim();
		decimal fee = 0m;
		if (feeText.Length > 0 && (!decimal.TryParse(feeText, NumberStyles.Number, CultureInfo.InvariantCulture, out fee) || fee < 0m))
		{
			_sendFeedback.Text = "Enter a valid fee (0 or more), or leave it blank.";
			return;
		}

		Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_sendFromNodeId, recipientAddress, amount, fee);
		if (tx is null)
		{
			_sendFeedback.Text = "Rejected — insufficient balance (amount + fee) or invalid route.";
			return;
		}

		string shortId = tx.TransactionId.Length > 8 ? tx.TransactionId[..8] + "..." : tx.TransactionId;
		_sendFeedback.Text = $"Sent! [{shortId}]";
		_amountInput.Text = string.Empty;
		_feeInput.Text = string.Empty;
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
}
