using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using UI.StatusBar;
#nullable enable

public partial class BotsBtcWallets : Control
{
	private SceneManager? _sceneManager;
	private NetworkRoot _networkRoot = null!;

	// Bot list
	private VBoxContainer _minersList = null!;
	private VBoxContainer _holdersList = null!;
	private CheckBox _showInactiveCheck = null!;
	private readonly List<(Button btn, BotWalletRecord bot)> _minerButtons = new();
	private readonly List<(HBoxContainer row, Button btn, Label indicator, BotWalletRecord bot)> _holderRows = new();

	// Detail panel root
	private VBoxContainer _botDetailVBox = null!;

	// No-selection placeholder
	private Label _noSelectionLabel = null!;

	// Detail container (hidden until bot selected)
	private VBoxContainer _detailVBox = null!;

	// Header
	private Label _badgeLabel = null!;
	private Label _addressLabel = null!;

	// Balance
	private Label _confirmedBalanceLabel = null!;
	private Label _pendingOutgoingLabel = null!;

	// Mining stats (miners only)
	private VBoxContainer _miningStatsSection = null!;
	private Label _blocksMined = null!;
	private Label _totalBtcMinedLabel = null!;

	// Wallet status (non-miners only)
	private VBoxContainer _walletStatusSection = null!;
	private Label _activeStatusLabel = null!;
	private Label _reactivationBlockLabel = null!;
	private Label _blocksRemainingLabel = null!;

	// Dev controls (non-miners only)
	private VBoxContainer _devControlsSection = null!;
	private Button _toggleActiveBtn = null!;
	private LineEdit _reactivationBlockInput = null!;
	private Label _devFeedbackLabel = null!;

	// Transactions
	private RichTextLabel _transactionsLabel = null!;

	// Send section (miners only)
	private VBoxContainer _sendSection = null!;
	private OptionButton _toDropdown = null!;
	private LineEdit _amountInput = null!;
	private Label _sendFeedbackLabel = null!;
	private readonly List<string> _toAddresses = new();

	private BotWalletRecord? _selectedBot;
	private double _refreshTimer;
	private const double RefreshInterval = 3.0;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");
		_networkRoot = GetNode<NetworkRoot>("NetworkRoot");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		_minersList = GetNode<VBoxContainer>("%MinersList");
		_holdersList = GetNode<VBoxContainer>("%HoldersList");
		_showInactiveCheck = GetNode<CheckBox>("%ShowInactiveCheck");
		_showInactiveCheck.Toggled += _ => RefreshHoldersVisibility();

		_botDetailVBox = GetNode<VBoxContainer>("%BotDetailVBox");

		BuildDetailPanel();
		BuildBotList();
		ShowNoSelection();
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;
		RefreshBotListBalances();
		if (_selectedBot != null) RefreshDetailPanel(_selectedBot);
	}

	// ── Bot list ─────────────────────────────────────────────────────────────

	private void BuildBotList()
	{
		foreach (BotWalletRecord bot in BotWalletRegistry.MinerBots)
		{
			var btn = new Button { Text = BuildBotRowText(bot), Alignment = HorizontalAlignment.Left };
			btn.Pressed += () => SelectBot(bot);
			_minersList.AddChild(btn);
			_minerButtons.Add((btn, bot));
		}

		foreach (BotWalletRecord bot in BotWalletRegistry.NonMinerBots)
		{
			var row = new HBoxContainer();
			var btn = new Button
			{
				Text = BuildBotRowText(bot),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				Alignment = HorizontalAlignment.Left
			};
			var indicator = new Label { Text = bot.IsActive ? "●" : "○" };
			btn.Pressed += () => SelectBot(bot);
			row.AddChild(btn);
			row.AddChild(indicator);
			_holdersList.AddChild(row);
			_holderRows.Add((row, btn, indicator, bot));

			if (!bot.IsActive)
			{
				row.Modulate = new Color(1, 1, 1, 0.45f);
				row.Visible = _showInactiveCheck.ButtonPressed;
			}
		}
	}

	private string BuildBotRowText(BotWalletRecord bot)
	{
		string addr = TruncateAddress(bot.Address);
		decimal balance = _networkRoot.GetAddressBalanceDetails(bot.Address).confirmedBalance;
		return $"{bot.NodeId,-14} {addr}  {balance:F8} BTC";
	}

	private void RefreshBotListBalances()
	{
		foreach (var (btn, bot) in _minerButtons)
			btn.Text = BuildBotRowText(bot);

		foreach (var (_, btn, _, bot) in _holderRows)
			btn.Text = BuildBotRowText(bot);
	}

	private void RefreshHoldersVisibility()
	{
		bool show = _showInactiveCheck.ButtonPressed;
		foreach (var (row, _, _, bot) in _holderRows)
		{
			if (!bot.IsActive) row.Visible = show;
		}
	}

	private void SelectBot(BotWalletRecord bot)
	{
		_selectedBot = bot;
		_noSelectionLabel.Visible = false;
		_detailVBox.Visible = true;
		_amountInput.Text = string.Empty;
		_sendFeedbackLabel.Text = string.Empty;
		RefreshDetailPanel(bot);
	}

	private void ShowNoSelection()
	{
		_noSelectionLabel.Visible = true;
		_detailVBox.Visible = false;
		_selectedBot = null;
	}

	// ── Detail panel — built once in _Ready, populated on SelectBot ──────────

	private void BuildDetailPanel()
	{
		_noSelectionLabel = new Label { Text = "Select a bot from the list." };
		_botDetailVBox.AddChild(_noSelectionLabel);

		_detailVBox = new VBoxContainer();
		_detailVBox.AddThemeConstantOverride("separation", 10);
		_botDetailVBox.AddChild(_detailVBox);

		// Badge + address
		_badgeLabel = new Label();
		_badgeLabel.AddThemeFontSizeOverride("font_size", 20);
		_detailVBox.AddChild(_badgeLabel);

		var addrRow = new HBoxContainer { Theme = null };
		_addressLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var copyBtn = new Button { Text = "Copy" };
		copyBtn.Pressed += () => { if (_selectedBot != null) DisplayServer.ClipboardSet(_selectedBot.Address); };
		addrRow.AddChild(_addressLabel);
		addrRow.AddChild(copyBtn);
		_detailVBox.AddChild(addrRow);

		// Balance
		_confirmedBalanceLabel = new Label();
		_detailVBox.AddChild(_confirmedBalanceLabel);
		_pendingOutgoingLabel = new Label { Visible = false };
		_detailVBox.AddChild(_pendingOutgoingLabel);

		// Mining stats (miners only)
		_miningStatsSection = new VBoxContainer();
		_detailVBox.AddChild(_miningStatsSection);
		_miningStatsSection.AddChild(new Label { Text = "── Mining Stats ──" });
		_blocksMined = new Label();
		_miningStatsSection.AddChild(_blocksMined);
		_totalBtcMinedLabel = new Label();
		_miningStatsSection.AddChild(_totalBtcMinedLabel);

		// Wallet status (non-miners only)
		_walletStatusSection = new VBoxContainer();
		_detailVBox.AddChild(_walletStatusSection);
		_walletStatusSection.AddChild(new Label { Text = "── Wallet Status ──" });
		_activeStatusLabel = new Label();
		_walletStatusSection.AddChild(_activeStatusLabel);
		_reactivationBlockLabel = new Label { Visible = false };
		_walletStatusSection.AddChild(_reactivationBlockLabel);
		_blocksRemainingLabel = new Label { Visible = false };
		_walletStatusSection.AddChild(_blocksRemainingLabel);

		// Dev controls (non-miners only)
		_devControlsSection = new VBoxContainer();
		_detailVBox.AddChild(_devControlsSection);
		_devControlsSection.AddChild(new Label { Text = "── Dev Controls ──" });
		_toggleActiveBtn = new Button();
		_toggleActiveBtn.Pressed += OnToggleActivePressed;
		_devControlsSection.AddChild(_toggleActiveBtn);

		var reactivationRow = new HBoxContainer();
		reactivationRow.AddChild(new Label { Text = "Reactivation block:" });
		_reactivationBlockInput = new LineEdit
		{
			CustomMinimumSize = new Vector2(100, 0),
			PlaceholderText = "block #"
		};
		reactivationRow.AddChild(_reactivationBlockInput);
		var setBtn = new Button { Text = "Set" };
		setBtn.Pressed += OnSetReactivationBlockPressed;
		reactivationRow.AddChild(setBtn);
		_devControlsSection.AddChild(reactivationRow);

		_devFeedbackLabel = new Label();
		_devControlsSection.AddChild(_devFeedbackLabel);

		// All transactions
		_detailVBox.AddChild(new Label { Text = "── All Transactions ──" });
		_transactionsLabel = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false,
			SelectionEnabled = true,
			CustomMinimumSize = new Vector2(0, 60)
		};
		_detailVBox.AddChild(_transactionsLabel);

		// Send section (miners only)
		_sendSection = new VBoxContainer();
		_detailVBox.AddChild(_sendSection);
		_sendSection.AddChild(new Label { Text = "── Send BTC ──" });

		var toRow = new HBoxContainer();
		toRow.AddChild(new Label { Text = "To:" });
		_toDropdown = new OptionButton { CustomMinimumSize = new Vector2(300, 0) };
		PopulateToDropdown();
		toRow.AddChild(_toDropdown);
		_sendSection.AddChild(toRow);

		var amountRow = new HBoxContainer();
		amountRow.AddChild(new Label { Text = "Amount:" });
		_amountInput = new LineEdit
		{
			CustomMinimumSize = new Vector2(180, 0),
			PlaceholderText = "0.00000000"
		};
		amountRow.AddChild(_amountInput);
		var sendBtn = new Button { Text = "Send" };
		sendBtn.Pressed += OnSendPressed;
		amountRow.AddChild(sendBtn);
		_sendSection.AddChild(amountRow);

		_sendFeedbackLabel = new Label();
		_sendSection.AddChild(_sendFeedbackLabel);
	}

	private void RefreshDetailPanel(BotWalletRecord bot)
	{
		bool isMiner = bot.IsMinerNode;

		_badgeLabel.Text = isMiner ? $"Miner Node  ·  {bot.NodeId}" : "Holder Wallet";
		_addressLabel.Text = $"Address: {bot.Address}";

		(decimal confirmed, decimal pendingOut) = _networkRoot.GetAddressBalanceDetails(bot.Address);
		_confirmedBalanceLabel.Text = $"Confirmed balance:  {confirmed:F8} BTC";
		_pendingOutgoingLabel.Visible = pendingOut > 0m;
		if (pendingOut > 0m)
			_pendingOutgoingLabel.Text = $"Pending outgoing:   {pendingOut:F8} BTC";

		// Mining stats
		_miningStatsSection.Visible = isMiner;
		if (isMiner)
		{
			var (mined, totalBtc) = GetMiningStats(bot.Address);
			_blocksMined.Text = $"Blocks mined:      {mined}";
			_totalBtcMinedLabel.Text = $"Total BTC mined:   {totalBtc:F8} BTC";
		}

		// Wallet status + dev controls
		_walletStatusSection.Visible = !isMiner;
		_devControlsSection.Visible = !isMiner;
		if (!isMiner)
		{
			_activeStatusLabel.Text = bot.IsActive ? "Status: ● Active" : "Status: ○ Inactive";
			_toggleActiveBtn.Text = bot.IsActive ? "Set Inactive" : "Set Active";
			_devFeedbackLabel.Text = string.Empty;

			bool hasReactivation = bot.ReactivationBlockHeight.HasValue;
			_reactivationBlockLabel.Visible = hasReactivation;
			_blocksRemainingLabel.Visible = hasReactivation;
			if (hasReactivation)
			{
				int chainLen = _networkRoot.GetPlayerChainLength();
				int remaining = Math.Max(0, bot.ReactivationBlockHeight!.Value - chainLen);
				_reactivationBlockLabel.Text = $"Reactivates at block: {bot.ReactivationBlockHeight.Value}";
				_blocksRemainingLabel.Text = $"Blocks remaining:     {remaining}";
				_reactivationBlockInput.Text = bot.ReactivationBlockHeight.Value.ToString();
			}
			else
			{
				_reactivationBlockInput.Text = string.Empty;
			}
		}

		// Transactions
		BuildTransactionsList(bot.Address);

		// Send section: miners always; non-miners only when active and have a balance to send
		_sendSection.Visible = bot.HasFullWallet && (isMiner || (bot.IsActive && confirmed > 0m));
	}

	private (int blocksMined, decimal totalBtc) GetMiningStats(string address)
	{
		var txs = _networkRoot.GetAddressConfirmedTransactions(address);
		var coinbaseTxs = txs.Where(t => t.tx.Sender == BlockchainService.CoinbaseSender).ToList();
		return (coinbaseTxs.Count, coinbaseTxs.Sum(t => t.tx.Amount));
	}

	private void BuildTransactionsList(string address)
	{
		var txs = _networkRoot.GetAddressConfirmedTransactions(address);
		if (txs.Count == 0) { _transactionsLabel.Text = "No transactions yet."; return; }

		var sb = new StringBuilder();
		foreach (var (tx, blockIndex) in txs)
		{
			bool isIncoming = tx.Recipient == address;
			string sign = isIncoming ? "[color=green]+[/color]" : "[color=red]-[/color]";
			string txIdShort = tx.TransactionId.Length > 8 ? tx.TransactionId[..8] + "..." : tx.TransactionId;
			string counterpart = tx.Sender == BlockchainService.CoinbaseSender
				? "coinbase"
				: isIncoming ? TruncateAddress(tx.Sender) : TruncateAddress(tx.Recipient);
			sb.AppendLine($"{sign}{tx.Amount:F8} BTC  block #{blockIndex}  {counterpart}  [{txIdShort}]");
		}
		_transactionsLabel.Text = sb.ToString().TrimEnd();
	}

	// ── Dev controls handlers ─────────────────────────────────────────────────

	private void OnToggleActivePressed()
	{
		if (_selectedBot == null || _selectedBot.HasFullWallet) return;
		BotWalletRegistry.SetBotStatus(_selectedBot.NodeId, !_selectedBot.IsActive, _selectedBot.ReactivationBlockHeight);
		RefreshSelectedBotFromRegistry();
	}

	private void OnSetReactivationBlockPressed()
	{
		if (_selectedBot == null || _selectedBot.HasFullWallet) return;

		int? blockHeight = null;
		string text = _reactivationBlockInput.Text.Trim();
		if (!string.IsNullOrEmpty(text))
		{
			if (!int.TryParse(text, out int parsed) || parsed <= 0)
			{
				_devFeedbackLabel.Text = "Enter a valid positive block number.";
				return;
			}
			blockHeight = parsed;
		}

		BotWalletRegistry.SetBotStatus(_selectedBot.NodeId, _selectedBot.IsActive, blockHeight);
		RefreshSelectedBotFromRegistry();
	}

	private void RefreshSelectedBotFromRegistry()
	{
		if (_selectedBot == null) return;
		string nodeId = _selectedBot.NodeId;
		_selectedBot = BotWalletRegistry.GetBot(nodeId);
		UpdateHolderListRow(nodeId);
		if (_selectedBot != null) RefreshDetailPanel(_selectedBot);
	}

	private void UpdateHolderListRow(string nodeId)
	{
		int idx = _holderRows.FindIndex(r => r.bot.NodeId == nodeId);
		if (idx < 0) return;

		BotWalletRecord? updated = BotWalletRegistry.GetBot(nodeId);
		if (updated == null) return;

		var (row, btn, indicator, _) = _holderRows[idx];
		_holderRows[idx] = (row, btn, indicator, updated);

		indicator.Text = updated.IsActive ? "●" : "○";
		row.Modulate = updated.IsActive ? new Color(1, 1, 1, 1f) : new Color(1, 1, 1, 0.45f);
		if (!updated.IsActive) row.Visible = _showInactiveCheck.ButtonPressed;
		else row.Visible = true;
	}

	// ── Send handler ──────────────────────────────────────────────────────────

	private void OnSendPressed()
	{
		if (_selectedBot == null || !_selectedBot.HasFullWallet)
		{
			_sendFeedbackLabel.Text = "No wallet selected.";
			return;
		}

		int selected = _toDropdown.Selected;
		if (selected < 0 || selected >= _toAddresses.Count)
		{
			_sendFeedbackLabel.Text = "Select a recipient.";
			return;
		}

		string recipientAddress = _toAddresses[selected];
		if (recipientAddress == _selectedBot.Address)
		{
			_sendFeedbackLabel.Text = "Cannot send to self.";
			return;
		}

		if (!decimal.TryParse(_amountInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0m)
		{
			_sendFeedbackLabel.Text = "Enter a valid positive amount.";
			return;
		}

		Transaction? tx = _networkRoot.CreateAndBroadcastTransactionToAddress(_selectedBot.NodeId, recipientAddress, amount);
		if (tx is null)
		{
			_sendFeedbackLabel.Text = "Rejected — insufficient balance or invalid route.";
		}
		else
		{
			string shortId = tx.TransactionId.Length > 8 ? tx.TransactionId[..8] + "..." : tx.TransactionId;
			_sendFeedbackLabel.Text = $"Sent! [{shortId}]";
			_amountInput.Text = string.Empty;
			RefreshDetailPanel(_selectedBot);
		}
	}

	// ── To-dropdown ───────────────────────────────────────────────────────────

	private void PopulateToDropdown()
	{
		_toDropdown.Clear();
		_toAddresses.Clear();

		foreach (BotWalletRecord bot in BotWalletRegistry.AllBots)
		{
			_toDropdown.AddItem($"{bot.NodeId}  {TruncateAddress(bot.Address)}");
			_toAddresses.Add(bot.Address);
		}

		var playerWallet = WalletInitializationService.PlayerWallet;
		if (playerWallet != null)
		{
			_toDropdown.AddItem($"Player  {TruncateAddress(playerWallet.BaseAddress)}");
			_toAddresses.Add(playerWallet.BaseAddress);
		}

		var casinoWallet = WalletInitializationService.CasinoWallet;
		if (casinoWallet != null)
		{
			_toDropdown.AddItem($"Casino  {TruncateAddress(casinoWallet.BaseAddress)}");
			_toAddresses.Add(casinoWallet.BaseAddress);
		}
	}

	// ── Utilities ─────────────────────────────────────────────────────────────

	private static string TruncateAddress(string address) =>
		address.Length > 16 ? address[..8] + "..." + address[^6..] : address;
}
