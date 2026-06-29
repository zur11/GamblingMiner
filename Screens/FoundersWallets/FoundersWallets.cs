using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using Scripts.Hardware;
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
	private CalendarTimeService? _calendarTimeService;
	private FoundersMiningService? _foundersMining;

	// Founder selection
	private FounderWalletState? _currentFounder;
	private Button _satoshiBtn = null!;
	private Button _halBtn = null!;
	private Button _hearnBtn = null!;
	private Label _founderTitle = null!;

	// Founder economics readout (programmatic)
	private RichTextLabel _economicsLabel = null!;

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

	// Mining lottery dev panel (built programmatically)
	private VBoxContainer _lotteryDevPanel = null!;
	private LineEdit _satoshiWeightInput = null!;
	private LineEdit _halWeightInput = null!;
	private LineEdit _lotteryBlocksInput = null!;
	private Label _lotteryResultLabel = null!;

	// Derived-address dev panel (Step 8.1 — built programmatically)
	private VBoxContainer _derivedDevPanel = null!;
	private RichTextLabel _derivedResultLabel = null!;

	// Automatic-activity panel (Step 8.2 — scripted historical events, built programmatically)
	private RichTextLabel _activityLabel = null!;

	// Address-book panel (Step 8 — full UTXO model; mirrors the player's BTCWallet address list so Satoshi's
	// address-non-reuse spread is as legible as the player's wallet).
	private VBoxContainer _addressBookPanel = null!;
	private RichTextLabel _addressBookLabel = null!;
	private CheckBox _founderShowEmptyToggle = null!;

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
		_foundersMining = GetNodeOrNull<FoundersMiningService>("/root/FoundersMiningService");

		GetNode<HBoxContainer>("%StatusBarPlaceholder").AddChild(new StatusBar());
		GetNode<Button>("%BackBtn").Pressed += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);

		// Founder selector
		_satoshiBtn = GetNode<Button>("%SatoshiBtn");
		_halBtn     = GetNode<Button>("%HalBtn");
		_satoshiBtn.Pressed += () => SelectFounder(WalletInitializationService.SatoshiWallet);
		_halBtn.Pressed     += () => SelectFounder(WalletInitializationService.HalWallet);

		// Mike Hearn selector — added programmatically as a sibling of the existing founder buttons.
		_hearnBtn = new Button { Text = "Mike Hearn", ToggleMode = true };
		_hearnBtn.AddThemeFontSizeOverride("font_size", _halBtn.GetThemeFontSize("font_size"));
		_halBtn.GetParent().AddChild(_hearnBtn);
		_hearnBtn.Pressed += () => SelectFounder(WalletInitializationService.MikeHearnWallet);

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
		BuildLotteryDevPanel();
		BuildDerivedAddressDevPanel();
		BuildAutomaticActivityPanel();
		BuildAddressBookPanel();
		BuildFounderEconomicsPanel();

		// Default to Satoshi.
		SelectFounder(WalletInitializationService.SatoshiWallet);
	}

	public override void _Process(double delta)
	{
		_refreshTimer += delta;
		if (_refreshTimer < RefreshInterval) return;
		_refreshTimer = 0d;
		RefreshBalances();
		RefreshAutomaticActivity();
		RefreshAddressBook();
		RefreshFounderEconomics();
	}

	// ── Founder economics readout (Phase 7.5) ─────────────────────────────────
	// Live status for all three founders — Satoshi's 11,000-BTC ramp + retirement, Hal's decay, Hearn's
	// holdings — so the founder dynamics are observable in-engine without reading the chain by hand.

	private void BuildFounderEconomicsPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootScroll/RootVBox");

		var panel = new VBoxContainer();
		panel.AddThemeConstantOverride("separation", 6);

		var title = new Label { Text = "Founder Economics [DEV]" };
		title.AddThemeFontSizeOverride("font_size", 20);
		panel.AddChild(title);

		_economicsLabel = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			CustomMinimumSize = new Vector2(0, 96),
			ScrollActive = false
		};
		_economicsLabel.AddThemeFontSizeOverride("normal_font_size", 16);
		panel.AddChild(_economicsLabel);

		rootVBox.AddChild(panel);
		RefreshFounderEconomics();
	}

	private void RefreshFounderEconomics()
	{
		if (_economicsLabel == null) return;
		if (_foundersMining == null)
		{
			_economicsLabel.Text = "FoundersMiningService unavailable.";
			return;
		}

		// Recompute against the live clock + Satoshi's confirmed BTC so the readout is fresh even when idle.
		// otherMinersPower mirrors the player's hardware rate (the dominant non-founder miner in the tests).
		decimal satoshiBtc = _networkRoot.GetNodeSpendableBalance("satoshi");
		decimal halBtc      = _networkRoot.GetNodeSpendableBalance("hal");
		decimal hearnBtc    = _networkRoot.GetNodeSpendableBalance("mike_hearn");
		double otherPower   = Math.Clamp(HardwareAllocationRepository.GetNode("player").TotalCredits, 1, 99);
		DateTime nowLocal   = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
		_foundersMining.RecomputeFounderPowers(otherPower, nowLocal, satoshiBtc);

		string satoshiState = _foundersMining.SatoshiRetired
			? $"[color=gray]RETIRED {_foundersMining.SatoshiRetiredAtLocal:yyyy-MM-dd}[/color]"
			: "[color=lime]ACTIVE[/color]";
		string halState = _foundersMining.HalPower > 0.0001d ? "[color=lime]MINING[/color]" : "[color=gray]DORMANT[/color]";

		_economicsLabel.Text =
			$"[b]Satoshi[/b]  {satoshiBtc:F2} / {_foundersMining.SatoshiTarget:F0} BTC  ·  power {_foundersMining.SatoshiPower:F3}  ·  " +
			$"share {_foundersMining.SatoshiShare * 100:F1}%  ·  ~{_foundersMining.EstimatedBlocksUntilTarget:F0} blk to target  ·  " +
			$"retire ≥ {_foundersMining.SatoshiFloorDateLocal:yyyy-MM-dd}  ·  {satoshiState}\n" +
			$"[b]Hal[/b]  {halBtc:F2} BTC  ·  power {_foundersMining.HalPower:F3}  ·  fading → 0 by 2009-08-09  ·  {halState}\n" +
			$"[b]Mike Hearn[/b]  {hearnBtc:F2} BTC  ·  never mines  ·  [color=gray]holder[/color]";
	}

	// ── Founder selection ──────────────────────────────────────────────────────

	private void SelectFounder(FounderWalletState? founder)
	{
		_currentFounder = founder;
		_satoshiBtn.ButtonPressed = founder?.FounderId == "satoshi";
		_halBtn.ButtonPressed     = founder?.FounderId == "hal";
		_hearnBtn.ButtonPressed   = founder?.FounderId == "mike_hearn";

		string displayName = founder?.FounderId switch
		{
			"satoshi"    => "Satoshi Nakamoto",
			"hal"        => "Hal Finney",
			"mike_hearn" => "Mike Hearn",
			_            => "Founder"
		};
		_founderTitle.Text = $"{displayName} Wallet";
		_addressLabel.Text = founder?.BaseAddress ?? "—";

		SetMode(WalletMode.Base);
	}

	// ── Send panel (programmatic) ─────────────────────────────────────────────

	private void BuildSendPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootScroll/RootVBox");

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

	// ── Mining lottery dev panel ──────────────────────────────────────────────

	private void BuildLotteryDevPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootScroll/RootVBox");

		_lotteryDevPanel = new VBoxContainer();
		_lotteryDevPanel.AddThemeConstantOverride("separation", 8);

		var title = new Label { Text = "Mining Lottery [DEV]" };
		title.AddThemeFontSizeOverride("font_size", 20);
		_lotteryDevPanel.AddChild(title);

		_lotteryDevPanel.AddChild(new Label
		{
			Text = "Mine founder blocks via the weighted lottery (Satoshi vs Hal). Verifies HashrateWeight distribution."
		});

		var weightRow = new HBoxContainer();
		weightRow.AddThemeConstantOverride("separation", 10);
		weightRow.AddChild(new Label { Text = "Satoshi weight:" });
		_satoshiWeightInput = new LineEdit { Text = "9", CustomMinimumSize = new Vector2(80, 0) };
		weightRow.AddChild(_satoshiWeightInput);
		weightRow.AddChild(new Label { Text = "Hal weight:" });
		_halWeightInput = new LineEdit { Text = "1", CustomMinimumSize = new Vector2(80, 0) };
		weightRow.AddChild(_halWeightInput);
		_lotteryDevPanel.AddChild(weightRow);

		var runRow = new HBoxContainer();
		runRow.AddThemeConstantOverride("separation", 10);
		runRow.AddChild(new Label { Text = "Blocks:" });
		_lotteryBlocksInput = new LineEdit { Text = "20", CustomMinimumSize = new Vector2(80, 0) };
		runRow.AddChild(_lotteryBlocksInput);
		var runBtn = new Button { Text = "Run Lottery" };
		runBtn.Pressed += OnRunLotteryPressed;
		runRow.AddChild(runBtn);
		_lotteryDevPanel.AddChild(runRow);

		_lotteryResultLabel = new Label { Text = string.Empty };
		_lotteryDevPanel.AddChild(_lotteryResultLabel);

		rootVBox.AddChild(_lotteryDevPanel);
	}

	private void OnRunLotteryPressed()
	{
		if (!double.TryParse(_satoshiWeightInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out double sWeight) || sWeight < 0d)
			sWeight = 0d;
		if (!double.TryParse(_halWeightInput.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out double hWeight) || hWeight < 0d)
			hWeight = 0d;
		if (!int.TryParse(_lotteryBlocksInput.Text.Trim(), out int blocks) || blocks <= 0)
			blocks = 1;
		blocks = Math.Min(blocks, 200);

		_networkRoot.SetHashrateWeight("satoshi", sWeight);
		_networkRoot.SetHashrateWeight("hal", hWeight);

		// Stamp each mined block at increasing game-time so timestamps stay in the 2009 range
		// without mutating the player's calendar (this is a dev tool).
		long baseMs = _calendarTimeService != null
			? new DateTimeOffset(_calendarTimeService.CurrentUtcDateTime).ToUnixTimeMilliseconds()
			: new DateTimeOffset(2009, 1, 3, 18, 15, 6, TimeSpan.Zero).ToUnixTimeMilliseconds();
		const long blockIntervalMs = 58_500_000L; // ~16h40m in-game per block at 100X

		int satoshiWins = 0, halWins = 0, none = 0;
		var miners = new List<string> { "satoshi", "hal" };
		for (int i = 0; i < blocks; i++)
		{
			string? winner = _networkRoot.RunWeightedBlockLottery(miners, baseMs + i * blockIntervalMs);
			if (winner == "satoshi") satoshiWins++;
			else if (winner == "hal") halWins++;
			else none++;
		}

		_lotteryResultLabel.Text = $"Mined {blocks}:  Satoshi {satoshiWins}  ·  Hal {halWins}" + (none > 0 ? $"  ·  none {none}" : "");
		RefreshBalances();
	}

	// ── Derived-address dev panel (Step 8.1) ──────────────────────────────────
	// Verifies the HD-lite DerivedAddressWallet against the live chain: addr(0) must equal the founder's
	// current base address (back-compatibility), the first sample must be distinct (address non-reuse),
	// and a chain rescan must find the receive frontier + owned set. No model wiring yet (that is 8.2).

	private void BuildDerivedAddressDevPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootScroll/RootVBox");

		_derivedDevPanel = new VBoxContainer();
		_derivedDevPanel.AddThemeConstantOverride("separation", 8);

		var title = new Label { Text = "Derived Addresses [DEV] (Step 8.1)" };
		title.AddThemeFontSizeOverride("font_size", 20);
		_derivedDevPanel.AddChild(title);

		_derivedDevPanel.AddChild(new Label
		{
			Text = "HD-lite address-non-reuse check for the selected founder: derives the first addresses, " +
			       "verifies addr(0) == base + distinctness, and rescans the chain for the receive frontier."
		});

		var runBtn = new Button { Text = "Inspect Derived Addresses" };
		runBtn.Pressed += OnInspectDerivedAddressesPressed;
		_derivedDevPanel.AddChild(runBtn);

		_derivedResultLabel = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			CustomMinimumSize = new Vector2(0, 140),
			ScrollActive = false
		};
		_derivedResultLabel.AddThemeFontSizeOverride("normal_font_size", 14);
		_derivedDevPanel.AddChild(_derivedResultLabel);

		rootVBox.AddChild(_derivedDevPanel);
	}

	private void OnInspectDerivedAddressesPressed()
	{
		if (_currentFounder == null)
		{
			_derivedResultLabel.Text = "No founder selected.";
			return;
		}

		string seed = string.Join(" ", _currentFounder.SeedWords);
		var wallet = new DerivedAddressWallet(seed);

		const int sample = 8;
		var addrs = new List<string>();
		for (int i = 0; i < sample; i++) addrs.Add(wallet.DeriveAddress(i));

		bool baseMatches = addrs[0] == _currentFounder.BaseAddress;
		bool distinct = new HashSet<string>(addrs).Count == addrs.Count;

		wallet.Rescan(_networkRoot.CollectUsedAddressSet().Contains);
		decimal total = _networkRoot.GetWalletTotalConfirmed(wallet.OwnedAddresses);

		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"[b]{_currentFounder.FounderId}[/b]  seed-derived address book");
		sb.AppendLine($"addr(0) == base: {(baseMatches ? "[color=lime]YES[/color]" : "[color=red]NO[/color]")}" +
		              $"   ·   first {sample} distinct: {(distinct ? "[color=lime]YES[/color]" : "[color=red]NO[/color]")}");
		sb.AppendLine($"rescan → nextReceiveIndex [b]{wallet.NextReceiveIndex}[/b]  ·  owned [b]{wallet.OwnedAddresses.Count}[/b]  ·  total [b]{total:F2}[/b] BTC");
		for (int i = 0; i < sample; i++)
		{
			bool owned = wallet.OwnedAddresses.Contains(addrs[i]);
			sb.AppendLine($"  addr({i})  {addrs[i]}  {(owned ? "[color=lime]●[/color]" : "[color=gray]○[/color]")}");
		}
		_derivedResultLabel.Text = sb.ToString();
	}

	// ── Automatic activity panel (Step 8.2) ──────────────────────────────────
	// Scripted, system-driven historical transactions (the Hearn round-trip, the 10-BTC Satoshi→Hal tx, …)
	// shown SEPARATELY from the main balance, because the founder did not order them manually. This is what
	// makes the "32.51 in then immediately out" legible: it is a historical script, not a manual withdrawal.

	private void BuildAutomaticActivityPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootScroll/RootVBox");

		var panel = new VBoxContainer();
		panel.AddThemeConstantOverride("separation", 6);

		var title = new Label { Text = "Automatic Activity [DEV]" };
		title.AddThemeFontSizeOverride("font_size", 20);
		panel.AddChild(title);

		panel.AddChild(new Label
		{
			Text = "System-driven historical transactions (NOT manual withdrawals): the Hearn round-trip, " +
			       "the 10-BTC Satoshi→Hal tx, etc. The main balance above only counts funds available to move manually."
		});

		_activityLabel = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			CustomMinimumSize = new Vector2(0, 96),
			ScrollActive = false
		};
		_activityLabel.AddThemeFontSizeOverride("normal_font_size", 15);
		panel.AddChild(_activityLabel);

		rootVBox.AddChild(panel);
	}

	private void RefreshAutomaticActivity()
	{
		if (_activityLabel == null || _currentFounder == null) return;

		var activity = _networkRoot.GetNodeScriptedActivity(_currentFounder.FounderId);
		if (activity.Count == 0)
		{
			_activityLabel.Text = "[color=gray]No automatic historical activity.[/color]";
			return;
		}

		var sb = new System.Text.StringBuilder();
		foreach ((string label, bool outgoing, decimal amount, string counterparty, bool confirmed) in activity)
		{
			string status = confirmed ? "[color=lime]✓ done[/color]" : "[color=yellow]⏳ pending[/color]";
			string dir = outgoing ? "→ sent to" : "← received from";
			string shortAddr = counterparty.Length > 14 ? counterparty[..14] + "…" : counterparty;
			sb.AppendLine($"{status}  [b]{label}[/b]  {dir} {shortAddr}  ({amount:F8} BTC)");
		}
		_activityLabel.Text = sb.ToString();
	}

	// ── Address book panel (Step 8 — full UTXO model) ─────────────────────────
	// The founder's wallet as its set of addresses, mirroring the player's BTCWallet list. For Satoshi this
	// makes the address-non-reuse spread legible (one coinbase per fresh derived address — the fractal ~220).
	// Spent (empty) addresses are hidden by default, exactly like a real HD wallet that never reuses them.

	private void BuildAddressBookPanel()
	{
		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootScroll/RootVBox");

		_addressBookPanel = new VBoxContainer();
		_addressBookPanel.AddThemeConstantOverride("separation", 6);

		var title = new Label { Text = "Address Book [DEV] (Step 8 — UTXO)" };
		title.AddThemeFontSizeOverride("font_size", 20);
		_addressBookPanel.AddChild(title);

		_addressBookPanel.AddChild(new Label
		{
			Text = "The wallet as its collection of addresses (UTXOs). Satoshi spreads one coinbase per fresh " +
			       "address (address non-reuse); the player/Hal/Hearn keep one. Spent (empty) addresses are " +
			       "hidden by default — like a real HD wallet, they are kept for history but never reused.",
			// Wrap instead of forcing a huge single-line minimum width (which pushed the panel past the
			// scroll viewport and clipped the toggle's tick box on the left).
			AutowrapMode = TextServer.AutowrapMode.Word
		});

		_founderShowEmptyToggle = new CheckBox { Text = "View empty addresses", ButtonPressed = false };
		_founderShowEmptyToggle.AddThemeFontSizeOverride("font_size", 16);
		_founderShowEmptyToggle.Toggled += _ => RefreshAddressBook();
		_addressBookPanel.AddChild(_founderShowEmptyToggle);

		// Pattern B (ProjectDesignManual Ch. 29): a single RichTextLabel with its OWN internal scroll
		// (ScrollActive = true, FitContent = false, bounded height) so ALL of Satoshi's ~109+ addresses are
		// listed and scrollable in-place — no row cap, no reliance on the page scroll.
		_addressBookLabel = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = false,
			ScrollActive = true,
			CustomMinimumSize = new Vector2(0, 320)
		};
		_addressBookLabel.AddThemeFontSizeOverride("normal_font_size", 14);
		_addressBookPanel.AddChild(_addressBookLabel);

		rootVBox.AddChild(_addressBookPanel);
	}

	private void RefreshAddressBook()
	{
		if (_addressBookLabel == null || _currentFounder == null) return;

		var book = _networkRoot.GetNodeAddressBook(_currentFounder.FounderId);
		decimal total = 0m;
		foreach ((string _, decimal confirmed, bool _) in book) total += confirmed;

		bool showEmpty = _founderShowEmptyToggle.ButtonPressed;
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"[b]Wallet total[/b]  ({book.Count} address(es)):  [b]{total:F8} BTC[/b]");

		int shown = 0, hidden = 0;
		foreach ((string address, decimal confirmed, bool isBase) in book)
		{
			// Hide spent/empty (0-balance) non-base addresses by default — never reused, only kept for history.
			if (!showEmpty && confirmed == 0m && !isBase) { hidden++; continue; }
			string tag = isBase ? "[color=aqua]base[/color]" : "[color=gray]coinbase[/color]";
			sb.AppendLine($"  {tag}  {address}  —  {confirmed:F8} BTC");
			shown++;
		}
		if (hidden > 0) sb.AppendLine($"[color=gray]… {hidden} empty (spent) address(es) hidden — tick to show.[/color]");
		sb.Append("\n\n\n"); // trailing blank lines so the last row clears the scroll's bottom edge (Ch. 29)

		// Setting Text resets the internal scroll to the top — preserve the user's position across the 2s refresh.
		double scroll = _addressBookLabel.GetVScrollBar().Value;
		_addressBookLabel.Text = sb.ToString();
		_addressBookLabel.GetVScrollBar().Value = scroll;
	}

	// ── Balance ───────────────────────────────────────────────────────────────

	private void RefreshBalances()
	{
		if (_currentFounder == null) return;

		// Main balance = AVAILABLE (spendable): settled holdings NOT committed to an in-flight automatic
		// process — i.e. what could be moved manually right now. The automatic scripted processes (the Hearn
		// round-trip, etc.) are shown SEPARATELY in the Automatic Activity panel, since the founder did not
		// order them manually (Step 8.2 design decision).
		decimal available = _networkRoot.GetNodeSpendableBalance(_currentFounder.FounderId);
		_balanceLabel.Text = $"Available balance:  {available:F8} BTC";
		_pendingLabel.Visible = false; // automatic activity is shown in its own panel, not as a wallet "pending"

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
		_lotteryDevPanel.Visible         = mode == WalletMode.Base;
		_derivedDevPanel.Visible         = mode == WalletMode.Base;
		_addressBookPanel.Visible        = mode == WalletMode.Base;

		if (mode != WalletMode.PassphraseUnlocked && mode != WalletMode.Send)
			_passphraseInput.Text = string.Empty;

		if (mode == WalletMode.Base)
		{
			_currentPassphraseAddress = null;
			_currentPassphraseNodeId = string.Empty;
		}

		RefreshBalances();
		RefreshAddressBook();
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
			"satoshi"    => "Satoshi",
			"hal"        => "Hal Finney",
			"mike_hearn" => "Mike Hearn",
			_            => "Founder"
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
