using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
using UI.NotepadPopup;
#nullable enable

public partial class CasinoFinances : Control
{
	private enum WalletMode { Base, PassphraseLocked, PassphraseUnlocked, Send }

	private NetworkRoot _networkRoot = null!;
	private SceneManager? _sceneManager;
	private CasinoWalletState? _casinoWallet;
	private CalendarTimeService? _calendarTimeService;

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

	// Notepad
	private NotepadPopup _notepadPopup = null!;

	// Send panel controls (built programmatically)
	private Label _sendFromLabel = null!;
	private Label _sendBalanceLabel = null!;
	private HBoxContainer _feeRow = null!;
	private LineEdit _feeInput = null!;
	private OptionButton _toDropdown = null!;
	private LineEdit _manualAddressInput = null!;
	private LineEdit _amountInput = null!;
	private Label _sendFeedback = null!;
	private readonly List<string> _toAddresses = new();

	// Address book (Step 8 — casino has a ReceiveWallet for change-only rotation, like the player)
	private Button _addressListToggle = null!;
	private CheckBox _showEmptyAddrToggle = null!;
	private RichTextLabel _addressListLabel = null!;
	private bool _addressListExpanded;
	// Transactions history panel (Step 8)
	private Button _txToggle = null!;
	private CheckBox _hideMiningToggle = null!;
	private RichTextLabel _txLabel = null!;
	private bool _txExpanded;

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
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
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
		GetNode<Button>("%CopySeedWordsBtn").Pressed += OnCopySeedWordsPressed;
		GetNode<Button>("%ClosePopupBtn").Pressed    += () => _seedWordsPopup.Visible = false;

		_notepadPopup = new NotepadPopup();
		AddChild(_notepadPopup);
		GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;

		BuildSendPanel();
		BuildAddressListSection();
		BuildTransactionsSection();
		SetMode(WalletMode.Base);
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;
		RefreshBalances();
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

		_sendBalanceLabel = new Label { Text = string.Empty };
		_sendBalanceLabel.AddThemeFontSizeOverride("font_size", 18);
		_sendPanel.AddChild(_sendBalanceLabel);

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

		var feeLabel = new Label { Text = "Fee (BTC):" };
		feeLabel.AddThemeFontSizeOverride("font_size", 20);
		_feeInput = new LineEdit
		{
			PlaceholderText = "0.10000000",
			CustomMinimumSize = new Vector2(200, 0)
		};
		_feeInput.AddThemeFontSizeOverride("font_size", 20);
		_feeInput.FocusExited += OnFeeInputFocusExited;
		_feeRow = new HBoxContainer();
		_feeRow.AddThemeConstantOverride("separation", 10);
		_feeRow.AddChild(feeLabel);
		_feeRow.AddChild(_feeInput);
		_sendPanel.AddChild(_feeRow);

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 12);
		var sendBtn = new Button { Text = "Send" };
		sendBtn.AddThemeFontSizeOverride("font_size", 22);
		sendBtn.Pressed += OnSendConfirmed;
		var cancelBtn = new Button { Text = "Go Back" };
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

		var playerWallet = WalletInitializationService.PlayerWallet;
		if (playerWallet != null)
		{
			_toDropdown.AddItem($"Player — {playerWallet.BaseAddress[..10]}...");
			_toAddresses.Add(playerWallet.BaseAddress);
		}

		var casinoWallet = WalletInitializationService.CasinoWallet;
		if (casinoWallet != null && casinoWallet.BaseAddress != excludeAddress)
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
		_sendBalanceLabel.Text = BuildSendBalanceText(senderNodeId, senderAddress);
		PopulateToDropdown(senderAddress);
		_amountInput.Text = string.Empty;
		_sendFeedback.Text = string.Empty;
		ApplyFeeState();
		SetMode(WalletMode.Send);
	}

	// ── Address book (Step 8 — change-address rotation, mirrors BTCWallet) ─────

	private void BuildAddressListSection()
	{
		var panel = _baseWalletPanel;

		_addressListToggle = new Button { Text = "Show addresses ▸" };
		_addressListToggle.AddThemeFontSizeOverride("font_size", 20);
		_addressListToggle.Pressed += OnToggleAddressList;
		panel.AddChild(_addressListToggle);

		// Spent change addresses keep a 0.00000000 balance forever (never reused). Hidden by default.
		_showEmptyAddrToggle = new CheckBox { Text = "View empty addresses", ButtonPressed = false, Visible = false };
		_showEmptyAddrToggle.AddThemeFontSizeOverride("font_size", 16);
		_showEmptyAddrToggle.Toggled += _ => RefreshAddressList();
		panel.AddChild(_showEmptyAddrToggle);

		// Pattern B (ProjectDesignManual Ch. 29) — own internal scroll, bounded height, lists every address.
		_addressListLabel = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = false,
			ScrollActive = true,
			CustomMinimumSize = new Vector2(0, 240),
			Visible = false
		};
		_addressListLabel.AddThemeFontSizeOverride("normal_font_size", 16);
		panel.AddChild(_addressListLabel);

		// Position the three just after the pending label.
		int insertAt = _pendingLabel.GetIndex() + 1;
		panel.MoveChild(_addressListToggle, insertAt);
		panel.MoveChild(_showEmptyAddrToggle, insertAt + 1);
		panel.MoveChild(_addressListLabel, insertAt + 2);
	}

	private void OnToggleAddressList()
	{
		_addressListExpanded = !_addressListExpanded;
		_addressListLabel.Visible = _addressListExpanded;
		_showEmptyAddrToggle.Visible = _addressListExpanded;
		_addressListToggle.Text = _addressListExpanded ? "Hide addresses ▾" : "Show addresses ▸";
		if (_addressListExpanded) RefreshAddressList();
	}

	private void RefreshAddressList()
	{
		bool showEmpty = _showEmptyAddrToggle.ButtonPressed;
		var sb = new System.Text.StringBuilder();
		int hidden = 0;
		foreach ((string address, decimal confirmed, bool isBase, long createdMs) in _networkRoot.GetNodeAddressBook("casino"))
		{
			// Hide spent/empty (0-balance) non-base addresses by default — never reused, only kept for history.
			if (!showEmpty && confirmed == 0m && !isBase) { hidden++; continue; }
			string tag = isBase ? "[color=aqua]base  [/color]" : "[color=gray]change[/color]";
			sb.AppendLine($"{tag}  {address}   —   {confirmed:F8} BTC   [color=gray](created {FmtDate(createdMs)})[/color]");
		}
		if (hidden > 0 && !showEmpty)
			sb.AppendLine($"[color=gray]… {hidden} empty (spent) address(es) hidden — tick above to show.[/color]");
		sb.Append("\n\n\n"); // trailing blank lines so the last row clears the scroll's bottom edge (Ch. 29)

		double scroll = _addressListLabel.GetVScrollBar().Value;
		_addressListLabel.Text = sb.ToString();
		_addressListLabel.GetVScrollBar().Value = scroll;
	}

	// ── Transactions panel (Step 8) ───────────────────────────────────────────

	private void BuildTransactionsSection()
	{
		var panel = _baseWalletPanel;

		_txToggle = new Button { Text = "Show transactions ▸" };
		_txToggle.AddThemeFontSizeOverride("font_size", 20);
		_txToggle.Pressed += OnToggleTransactions;
		panel.AddChild(_txToggle);

		// The casino doesn't mine, but keep the filter for consistency (and any received coinbase edge case).
		_hideMiningToggle = new CheckBox { Text = "Hide mining rewards", ButtonPressed = true, Visible = false };
		_hideMiningToggle.AddThemeFontSizeOverride("font_size", 16);
		_hideMiningToggle.Toggled += _ => RefreshTransactions();
		panel.AddChild(_hideMiningToggle);

		_txLabel = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = false,
			ScrollActive = true,
			CustomMinimumSize = new Vector2(0, 240),
			Visible = false
		};
		_txLabel.AddThemeFontSizeOverride("normal_font_size", 15);
		panel.AddChild(_txLabel);

		int insertAt = _addressListLabel.GetIndex() + 1;
		panel.MoveChild(_txToggle, insertAt);
		panel.MoveChild(_hideMiningToggle, insertAt + 1);
		panel.MoveChild(_txLabel, insertAt + 2);
	}

	private void OnToggleTransactions()
	{
		_txExpanded = !_txExpanded;
		_txLabel.Visible = _txExpanded;
		_hideMiningToggle.Visible = _txExpanded;
		_txToggle.Text = _txExpanded ? "Hide transactions ▾" : "Show transactions ▸";
		if (_txExpanded) RefreshTransactions();
	}

	private void RefreshTransactions()
	{
		bool hideMining = _hideMiningToggle.ButtonPressed;
		var history = _networkRoot.GetNodeTransactionHistory("casino");
		var sb = new System.Text.StringBuilder();
		int minedHidden = 0;
		foreach ((long unixMs, string kind, decimal amount, string counterparty) in history)
		{
			if (hideMining && kind == "mined") { minedHidden++; continue; }
			(string color, string sign, string desc) = kind switch
			{
				"mined"    => ("lime",   "+", "mined (coinbase)"),
				"received" => ("lime",   "+", $"received from {Short(counterparty)}"),
				_          => ("orange", "−", $"sent to {Short(counterparty)}"),
			};
			sb.AppendLine($"[color=gray]{FmtDate(unixMs)}[/color]  [color={color}]{sign}{amount:F8} BTC[/color]  {desc}");
		}
		if (sb.Length == 0)
			sb.AppendLine("[color=gray]No transfers yet.[/color]");
		if (minedHidden > 0)
			sb.AppendLine($"[color=gray]… {minedHidden} mining reward(s) hidden — untick to show.[/color]");
		sb.Append("\n\n\n");
		double scroll = _txLabel.GetVScrollBar().Value;
		_txLabel.Text = sb.ToString();
		_txLabel.GetVScrollBar().Value = scroll;
	}

	private static string Short(string addr) => addr.Length > 16 ? addr[..16] + "…" : addr;
	private static string FmtDate(long unixMs) =>
		unixMs <= 0 ? "—" : System.DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm");

	// ── Balance ───────────────────────────────────────────────────────────────

	private void RefreshBalances()
	{
		if (_casinoWallet == null) return;

		// Step 8 — aggregate across the casino's owned set (base + any change addresses); it becomes
		// multi-address by spending (change → fresh derived address), like the player.
		var book = _networkRoot.GetNodeAddressBook("casino");
		decimal total = 0m, pendingOut = 0m;
		foreach ((string address, decimal confirmed, bool _, long _) in book)
		{
			total += confirmed;
			pendingOut += _networkRoot.GetAddressBalanceDetails(address).pendingOutgoing;
		}
		_balanceLabel.Text = book.Count > 1
			? $"Wallet total ({book.Count} addresses):  {total:F8} BTC"
			: $"Confirmed balance:  {total:F8} BTC";
		_pendingLabel.Visible = pendingOut > 0m;
		if (pendingOut > 0m)
			_pendingLabel.Text = $"Pending outgoing:   {pendingOut:F8} BTC";

		if (_addressListExpanded) RefreshAddressList();
		if (_txExpanded) RefreshTransactions();

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
		if (_casinoWallet == null) return;
		EnterSendMode("casino", _casinoWallet.BaseAddress, WalletMode.Base);
	}

	private void OnSendBtcPassphrasePressed()
	{
		if (string.IsNullOrEmpty(_currentPassphraseNodeId) || _currentPassphraseAddress == null) return;
		EnterSendMode(_currentPassphraseNodeId, _currentPassphraseAddress, WalletMode.PassphraseUnlocked);
	}

	private void OnSendCancelled() => SetMode(_modeBeforeSend);

	private void ApplyFeeState()
	{
		DateTime gameTime = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.MinValue;
		bool active = NetworkFeePolicy.IsActive(gameTime);
		_feeRow.Visible = active;
		if (active) _feeInput.Text = NetworkFeePolicy.DefaultFee.ToString("F8");
	}

	private void OnFeeInputFocusExited()
	{
		DateTime gameTime = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.MinValue;
		if (!NetworkFeePolicy.IsActive(gameTime)) return;
		_feeInput.Text = NetworkFeePolicy.ClampOrDefault(TryParseFee(_feeInput.Text)).ToString("F8");
	}

	private static decimal TryParseFee(string text)
		=> decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal v) ? v : -1m;

	private string BuildSendBalanceText(string senderNodeId, string senderAddress)
	{
		if (!senderNodeId.StartsWith("pass_", StringComparison.Ordinal))
		{
			decimal total = _networkRoot.GetNodeSpendableBalance(senderNodeId);
			return $"Balance: {total:F8} BTC";
		}
		decimal confirmed = _networkRoot.GetAddressBalanceDetails(senderAddress).confirmedBalance;
		return $"Balance: {confirmed:F8} BTC";
	}

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

		DateTime gameTime = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.MinValue;
		decimal fee = 0m;
		if (NetworkFeePolicy.IsActive(gameTime))
		{
			decimal parsed = TryParseFee(_feeInput.Text);
			fee = NetworkFeePolicy.ClampOrDefault(parsed);
			_feeInput.Text = fee.ToString("F8");
		}

		Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_sendFromNodeId, recipientAddress, amount, fee);
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
		if (_casinoWallet == null) return;
		string passphrase = _passphraseInput.Text.Trim();
		if (string.IsNullOrEmpty(passphrase)) return;

		string seedPhrase = string.Join(" ", _casinoWallet.SeedWords) + " " + passphrase;
		_currentPassphraseAddress = CryptoUtils.DeriveGmAddress(seedPhrase);
		_passphraseInput.Text = string.Empty;
		_passphraseAddressLabel.Text = _currentPassphraseAddress;

		_currentPassphraseNodeId = _networkRoot.RegisterPassphraseWallet(seedPhrase, _currentPassphraseAddress);

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
