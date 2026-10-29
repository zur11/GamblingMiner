using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Scripts.Dice;
using Scripts.Finance;
using Scripts.Game;
using Scripts.Sessions;
using Scripts.Betting;
using Scripts.StateMachines;
using Scripts.Controllers;
using Scripts.History;
using Scripts.Hardware;
using UI.StrategyControlPanel;
using UI.StatusBar;
using UI.Readouts;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;

public partial class DiceGame : Control, IBetEventSource
{
	// --- Eventos ---
	public event Action<string, BetTransactionEvent> BetExecuted;

	// --- Propiedades ---
	public string GameId => "Dice";

	// --- Motor de juego ---
	private DiceEngine _engine;

	// --- Finanzas ---
	private Wallet _wallet;

	// --- Servicio de apuestas ---
	private WalletController _walletController;
	private BetService _betService;
	private UserStatsService _userStatsService;
	private CalendarTimeService _calendarTimeService;
	private BankrollStateService _bankrollStateService;
	private PrincipalBalanceService _principalBalanceService;
	private BankrollProgramService _bankrollProgramService;
	private BlockSessionCheckpointService _blockCheckpointService;
	private FinancialBettingStats _financialStats;
	private SimulationService _simulationService;
	private CasinoScBalanceService _casinoSc;
	private bool _autobetDelegated;
	private BetTransactionEvent _lastLoggedBetEvent;
	private Timer _autoBetTimer;
	private BaseBetSession _session;
	private bool _isAutoPaused;
	private double _lastCalculatorRefreshRealtimeSeconds = -1d;
	private const double AutoUiCalculatorRefreshIntervalSeconds = 0.2d;
	private decimal _sessionStartBaseBet;
	private double _autoBetAccumulatorGameSeconds;
	private const int MaxAutoBetsPerFrame = 10;
	private const double MaxAutoBetGameDeltaPerFrameSeconds = 0.25d;
	private const double MaxAutoBetBacklogGameSeconds = 2.0d;
	private const double MaxAutoBetsPerRealSecond = 500.0d;
	private const int MaxAutoBetBaseAps = 99;
	private long _autoBetLastRateSampleMsec;
	private int _autoBetBetsSinceSample;
	private double _autoBetLastMeasuredRealPerSec;
	private double _autoBetLastMeasuredGamePerSec;
	private double _lastPrintedMeasuredRealPerSec;
	private long _lastAutoBetTelemetryPrintMsec;
	private DateTime _autoBetVirtualTimestampUtc;
	private bool _autoBetVirtualTimestampInitialized;
	private DateTime? _autoBetLastExecutedTimestampUtc;
	private NetworkRoot _blockchainNetworkRoot;
	private const string PlayerNodeId = "player";
	private string _activeNodeId = PlayerNodeId;
	private const double GameSecondsPerRealSecond = 100.0d; // 10 real min -> 16h 40m game time
	private const double GameSecondsPerManualBet = 100.0d; // 1 manual bet tick
	private Label _blockchainStatusValue;
	private OptionButton _activeNodeSelector;
	private ImageTexture _readyDotTexture;
	private ImageTexture _notReadyDotTexture;
	private Button _openBlockExplorerBtn;
	private LineEdit _strategyNameInput;
	private Button _saveStrategyBtn;
	private Button _loadStrategyBtn;
	private OptionButton _savedStrategySelect;
	private SavedBettingStrategyRepository _savedStrategyRepository;
	private ManualStopGate _manualStopGate = ManualStopGate.None;

	// ── The manual burst is PACED ACROSS FRAMES, not run inside the press ──────────────────────────────
	// One press buys GameSecondsPerManualBet of game time, which at 99 credits is 99 bets. They used to run
	// in a single `for` loop inside the button handler, so all 99 rolls and all 99 history rows appeared in
	// the same frame — the developer's report: autobet looks fluid, manual arrives "de golpe". Nothing was
	// wrong with the bets; the whole second of play was invisible.
	//
	// The bets themselves are unchanged: same count, same game-time stamps (`base + i × timePerBet`), same
	// clock advance once at the end. Only their execution is spread over ManualBurstRealSeconds of REAL time,
	// which is what the autobet path does per frame and why it reads as play rather than as a result.
	// Betting controls are disabled while it runs, so a second press cannot interleave two bursts.
	private const double ManualBurstRealSeconds = 1.0d;
	private int _manualBurstTotal;
	private int _manualBurstRemaining;
	private int _manualBurstIndex;
	private int _manualBurstExecuted;
	private double _manualBurstTimePerBet;
	private double _manualBurstAccumulator;
	private DateTime _manualBurstBaseUtc;
	private bool ManualBurstActive => _manualBurstRemaining > 0;
	// STATIC so it survives DiceGame being freed and rebuilt on each scene change (mini-plan 02,
	// D-M2.1 — same reason as _checkpointRestoreSpentThisSession / _bootstrapAppliedThisSession
	// below). As an instance field this emptied on every navigation, so LoadActiveNodeStrategySnapshot
	// took its ClearStrategySettings() branch and blanked the whole panel — after which BuildConfig()
	// silently produced flat betting with both stops disarmed, BuildBotConfigs() returned an empty
	// list, and every node's ready dot went red.
	//
	// Persisted since mini-plan 27 (D-27.5), as a PLAYER preference: written through UserSettingsService on every
	// snapshot, restored here once per launch (RestoreNodeStrategiesFromSettingsOnce). This said "not persisted: a
	// config outliving a restart would describe a run that no longer exists" until then. Since D-M2.8 a snapshot is
	// the strategy's CONFIGURATION, never a mid-run rung, and a configuration describes the next run as well as the
	// last one. So after a restart every node is set up as it was, bots' readiness included (BuildBotConfigs reads
	// this store).
	private static readonly Dictionary<string, NodeStrategyState> _nodeStrategies = new();
	private static bool _nodeStrategiesRestored;
	private bool _loadingNodeStrategy;
	// Mirrors StrategyControlPanel._runLocked for the DiceGame-owned controls — see ApplyRunLock.
	private bool _runLocked;
	private SceneManager _sceneManager;
	private UserSettingsService _userSettings;
	private PlayerBankAccountService _playerBankAccountService;
	private CasinoClientLedgerService _casinoClientLedger; // SF.4B: feeds FinancialBettingStats' since-X baselines

	private enum ManualStopGate
	{
		None,
		BlockMined,
		ProfitOrLoss
	}

	private sealed class NodeStrategyState
	{
		public BettingStrategyConfig Config { get; set; }
		public int NumberOfBets { get; set; }
		public bool AutoRechargeEnabled { get; set; }
		public int WinningChance { get; set; }
		public bool BetHigh { get; set; }
		public int BetsPerSecond { get; set; }

		public bool IsValid => Config != null && Config.BaseBet > 0m;

		public NodeStrategyState Clone() => new()
		{
			Config = CloneConfig(Config),
			NumberOfBets = NumberOfBets,
			AutoRechargeEnabled = AutoRechargeEnabled,
			WinningChance = WinningChance,
			BetHigh = BetHigh,
			BetsPerSecond = BetsPerSecond
		};
	}

	// Componentes UI
	[Export]
	private BetHistoryContainer _betHistoryContainer;

	// --- State Machines ---
	private WalletStateMachine _walletFSM;

	// --- Nodos UI ---
	private Label _balanceValue;
	private Label _bankrollValue;
	private Label _principalBalanceValue;
	// Shows ONLY the latest dice roll (00–99), written by ShowRoll and nothing else. It used to carry ~30
	// status messages (stops, strategy save/load, block announcements, errors) while the scene kept it
	// `visible = false`, so none of them ever reached a player. Now that it says one thing, the scene shows
	// it; its placement and styling belong to the final player-facing UI design.
	private Label _resultValue;

	private Label _winnerNumbersValue;
	private Label _chanceToWinValue;
	private Label _multiplierValue;
	private Label _currentAppTimeValue;
	// Mini-plan 23 D — the credits in use, as a LABEL. It was a disabled 1X–99X dropdown that looked like a control
	// and did nothing: the rate has been read from HardwareAllocationRepository (GetAutoBetBaseAps) since Phase 3,
	// and an enumeration of the dropdown's 12 references found nothing reading it back. "Credits", not "bets per
	// second", because the credit count is true at every DevTimeScale and the bet rate is not.
	private Label _creditsInUseValue;

	private Slider _chanceSlider;
	private Button _highLowToggleBtn;

	private Button _depositBtn;
	private Button _openCalculatorBtn;
	private Button _openBankrollProgrammerBtn;
	private Button _openCalendarNavigatorBtn;
	private Button _openBetsExplorerBtn;
	private MartingaleCalculator _martingaleCalculator;
	private SigilOverlay _sigilOverlay;   // Graphic Numerology (P-GN.6), opened over this screen like the calculator

	// --- Game components ---
	// (The 100-cell PreviousWinnerNumbersGrid export was removed by mini-plan 25 17.A2: DiceGame's Numbers views are the
	// last-10 row and the latest-only number, built in BuildBetDisplayToggle.)
	[Export]
	private StrategyControlPanel _strategyPanel;

	// Inicialización
	public override void _Ready()
	{
		Scripts.Diagnostics.SceneEntryProbe.ReadyStarted();   // mini-plan 21 E, DEBUG-only
		// Inicializar motor y servicios
		_engine = new DiceEngine();
		_bankrollStateService = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
		_principalBalanceService = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_principalBalanceService?.EnsureInitialized();
		_bankrollProgramService = GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService");
		_blockCheckpointService = GetNodeOrNull<BlockSessionCheckpointService>("/root/BlockSessionCheckpointService");
		_simulationService = GetNodeOrNull<SimulationService>("/root/SimulationService");
		_casinoSc = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_userStatsService = GetNode<UserStatsService>("/root/UserStatsService");
		_playerBankAccountService = GetNodeOrNull<PlayerBankAccountService>("/root/PlayerBankAccountService");
		_casinoClientLedger = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		_userSettings = GetNodeOrNull<UserSettingsService>("/root/UserSettingsService");
		// Before anything reads the per-node store: the node selector's ready dots and the snapshot load below.
		RestoreNodeStrategiesFromSettingsOnce();
		_bankrollStateService?.EnsureInitialized(0m);
		decimal initialBalance = _bankrollStateService?.CurrentBalance ?? 0m;
		_wallet = new Wallet(initialBalance);
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_betService = new BetService(
			_engine,
			_wallet,
			TransactionSource.Bet,
			() => _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow
		);
		// Only (re)initialize the epoch from persisted state when no background autobet is live. While the
		// SimulationService advances the clock across scenes, the running in-memory time is authoritative;
		// reloading calendar_state.json here would rewind it to the last persisted instant — the regression
		// that grew the longer the player stayed out of DiceGame (mirrors the checkpoint-restore guard below).
		if (_simulationService?.IsRunning != true)
		{
			_calendarTimeService?.EnsureGameEpochInitialized();
		}
		if (_calendarTimeService != null)
		{
			_calendarTimeService.SpeedMultiplier = GameSecondsPerRealSecond;
			_calendarTimeService.IsRunning = false;
		}
		Scripts.Diagnostics.SceneEntryProbe.Mark("services");
		_blockchainNetworkRoot = new NetworkRoot();
		_blockchainNetworkRoot.Name = "BlockchainNetworkRoot";
		AddChild(_blockchainNetworkRoot);
		Scripts.Diagnostics.SceneEntryProbe.Mark("networkRoot");
		_savedStrategyRepository = new SavedBettingStrategyRepository(SavedBettingStrategyRepository.DefaultPath);
		var strategy = new ProgressiveBettingStrategy();

		_session = CreateSession(false); // default manual

		_walletController = new WalletController(_wallet);

		_autoBetTimer = new Timer();
		_autoBetTimer.WaitTime = 1.0; // 1 segundo
		_autoBetTimer.OneShot = true;

		AddChild(_autoBetTimer);

		// Inicializar state machines
		_walletFSM = new WalletStateMachine();

		// Obtener nodos
		_balanceValue = GetNode<Label>("%BalanceValue");
		_bankrollValue = GetNode<Label>("%BankrollValue");
		_principalBalanceValue = GetNode<Label>("%PrincipalBalanceValue");
		_resultValue = GetNode<Label>("%ResultValue");
		_winnerNumbersValue = GetNode<Label>("%WinnerNumbersValue");
		_chanceToWinValue = GetNode<Label>("%ChanceToWinValue");
		_multiplierValue = GetNode<Label>("%MultiplierValue");
		_currentAppTimeValue = GetNode<Label>("%CurrentAppTimeValue");
		_blockchainStatusValue = GetNode<Label>("%BlockchainStatusValue");
		_creditsInUseValue = GetNode<Label>("%CreditsInUseValue");
		_chanceSlider = GetNode<Slider>("%ChanceSlider");
		_highLowToggleBtn = GetNode<Button>("%HighLowToggleBtn");
		_depositBtn = GetNode<Button>("%DepositBtn");
		_openCalculatorBtn = GetNode<Button>("%OpenCalculatorBtn");
		_openBankrollProgrammerBtn = GetNode<Button>("%OpenBankrollProgrammerBtn");
		_openCalendarNavigatorBtn = GetNode<Button>("%OpenCalendarNavigatorBtn");
		_openBetsExplorerBtn = GetNode<Button>("%OpenBetsExplorerBtn");
		_openBlockExplorerBtn = GetNode<Button>("%OpenBlockExplorerBtn");
		_activeNodeSelector = GetNode<OptionButton>("%ActiveNodeSelector");
		_strategyNameInput = GetNode<LineEdit>("%StrategyNameInput");
		_saveStrategyBtn = GetNode<Button>("%SaveStrategyBtn");
		_loadStrategyBtn = GetNode<Button>("%LoadStrategyBtn");
		_martingaleCalculator = GetNode<MartingaleCalculator>("%MartingaleCalculator");
		_sigilOverlay = GetNode<SigilOverlay>("%SigilOverlay");
		_financialStats = GetNode<FinancialBettingStats>("%FinancialBettingStats");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		// Configurar etiqueta de High/Low toggle Btn
		_highLowToggleBtn.Text = "LOW";

		// Conectar señales
		_highLowToggleBtn.Pressed += OnHighLowToggled;
		_chanceSlider.ValueChanged += OnChanceChanged;
		_depositBtn.Pressed += OnDepositBtnPressed;
		_openCalculatorBtn.Pressed += OnOpenCalculatorPressed;
		_openBankrollProgrammerBtn.Pressed += OnOpenBankrollProgrammerPressed;
		_openCalendarNavigatorBtn.Pressed += OnOpenCalendarNavigatorPressed;
		_openBetsExplorerBtn.Pressed += OnOpenBetsExplorerPressed;
		_openBlockExplorerBtn.Pressed += OnOpenBlockExplorerPressed;
		GetNode<Button>("%MainMenuBtn").Pressed += OnGoToMainMenuPressed;
		_activeNodeSelector.ItemSelected += OnActiveNodeSelected;
		_strategyNameInput.TextChanged += _ =>
		{
			SyncSavedStrategySelectToName();
			UpdateStrategySaveLoadButtons();
		};
		_saveStrategyBtn.Pressed += OnSaveStrategyPressed;
		_loadStrategyBtn.Pressed += OnLoadStrategyPressed;
		_martingaleCalculator.CloseRequested += OnCalculatorCloseRequested;
		GetNode<Button>("%OpenSigilsBtn").Pressed += () => _sigilOverlay.Open();
		_sigilOverlay.CloseRequested += () => _sigilOverlay.Close();
		_wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
		_wallet.BalanceDeltaChanged += (_, _) => _bankrollStateService?.SetBalance(_wallet.Balance);
		_betHistoryContainer.SubscribeTo(this);
		_financialStats.ConnectTo(_userStatsService, _casinoClientLedger);
		_strategyPanel.BetOnceBtnPressed += OnManualBetFromPanel;

		// The strategy panel's PAUSE button is a column to the right of its top row now (2026-09-18), and it
		// reaches into the navigation buttons' column (x 1392). Those buttons are absolutely placed here, not in a
		// container shared with the panel, so they cannot be pushed by layout: they are shifted by code, just far
		// enough to clear PAUSE's real edge, and returned home when it hides. Both signals only mark the shift
		// dirty; _Process applies it on the next frame, after the containers have laid PAUSE out (Pattern 6's
		// edge-trigger shape: one flag read per frame, work only on change).
		// Mini-plan 25: "OpenBetsExplorerBtn" joined the column (below the Calendar) after a playtest showed what being
		// left out of this list does: the column moved for PAUSE and the new button stayed put, under it.
		foreach (string navName in new[] { "OpenCalculatorBtn", "OpenBlockExplorerBtn", "OpenBankrollProgrammerBtn", "MainMenuBtn", "OpenCalendarNavigatorBtn", "OpenBetsExplorerBtn" })
		{
			if (GetNodeOrNull<Control>("%" + navName) is Control navButton)
			{
				_navButtonsHome.Add((navButton, navButton.Position));
			}
		}
		_strategyPanel.PauseButton.VisibilityChanged += () => _navShiftDirty = true;
		_strategyPanel.PauseButton.Resized += () => _navShiftDirty = true;
		_strategyPanel.AutoBetToggled += OnAutoBetToggled;
		_strategyPanel.AutoPauseToggled += OnAutoPauseToggled;
		_strategyPanel.BetAmountInputChanged += OnBetInputChanged;
		_strategyPanel.StrategyConfigChanged += OnStrategyConfigChanged;
		_strategyPanel.StopOnBlockMinedDoubleClicked += OnStopOnBlockMinedDoubleClicked;
		_strategyPanel.ProfitOrLossStopDoubleClicked += OnProfitOrLossStopDoubleClicked;
		_strategyPanel.AutoRechargeToggled += OnAutoRechargeToggledFromPanel;
		RefreshHardwareDrivenSpeed();
		HardwareAllocationRepository.HardwareChanged += OnHardwareChanged;
		Scripts.Diagnostics.SceneEntryProbe.Mark("nodes+signals+aps");

		// DEV/TEST time-acceleration selector (the ladder is DevTimeScaleSelector's own), placed next to the
		// credits-in-use label — the two together are the `credits × DevTimeScale` throughput demand.
		var devTimeScale = new UI.DevTimeScaleSelector.DevTimeScaleSelector();
		// Its DEV test controls (the two profiler toggles, the Cap/frame picker, the governor readout) go in the
		// empty block right of the LOW button and under Auto Recharge, instead of trailing the top row where they
		// sat over "Amount to bet" and MAX/MIN (developer's request, 2026-09-18). Bounds from the scene:
		// HighLowToggleBtn ends at x 252, StrategySaveControls starts at x 704 and ChanceSlider at y 637; Auto
		// Recharge, laid out inside StrategyControlPanel, ends near y 430. That last bound holds during an autobet
		// too since PAUSE became a column to the right of the panel's top row instead of growing it downwards
		// (the interim fix, moving this block to y 498, is superseded by that). Must be set BEFORE the selector
		// enters the tree, because it builds those controls in _Ready.
		var devDiagnostics = new VBoxContainer { Position = new Vector2(270f, 445f) };
		devDiagnostics.AddThemeConstantOverride("separation", 4);
		AddChild(devDiagnostics);
		devTimeScale.DiagnosticsHost = devDiagnostics;
		_creditsInUseValue.GetParent().AddChild(devTimeScale);
		_creditsInUseValue.GetParent().MoveChild(devTimeScale, _creditsInUseValue.GetIndex() + 1);

		// Mini-plan 20 Part 0 — DEV: the player's credits, set directly. Beside the credits-in-use label (mini-plan 23 D,
		// which kept the old APS selector's right edge at x 1741), since it changes the value the label shows; the free
		// space runs from there to the canvas edge. The shipping control remains Mining Pools & Hardware.
		var devCredits = new UI.DevPlayerCreditsSelector.DevPlayerCreditsSelector { Position = new Vector2(1752f, 563f) };
		AddChild(devCredits);
		BuildHoldingsAndBtcWallet();
		Scripts.Diagnostics.SceneEntryProbe.Mark("devControls");
		_session.OnStopped += OnSessionStopped;

		_wallet.BalanceDeltaChanged += (sessionId, delta) =>
		{
			if (_walletController.Balance <= 0m)
			{
				_walletFSM.Fire(WalletEvent.BalanceZero);
			}
		};

		UpdateAllUI();
		Scripts.Diagnostics.SceneEntryProbe.Mark("UpdateAllUI");
		UpdateStrategySaveLoadButtons();
		InitializeActiveNodeSelector();
		Scripts.Diagnostics.SceneEntryProbe.Mark("nodeSelector");
		bool hadAnyNodeFinancialState = _blockchainNetworkRoot?.HasAnyNodeFinancialState() ?? false;
		RestoreLegacyCheckpointIfNeeded();
		Scripts.Diagnostics.SceneEntryProbe.Mark("legacyRestore");
		// Must run AFTER the checkpoint restore above (it rolls bet history back to the last mined block):
		// GetLoadedHistoryStats() below reads the currently-loaded history, so calling it before the rollback
		// would compute "General P/L" / "last deposit P/L" from uncommitted bets the checkpoint just discarded.
		ApplyRealtimeBootstrapFromLoadedHistory();
		Scripts.Diagnostics.SceneEntryProbe.Mark("historyBootstrap");
		// SF.4B.6: seed the in-game bet-history list from the centralized persistent store so the most-recent
		// history reproduces on entry (before, it started empty on re-entry). Runs AFTER the checkpoint rollback
		// above so it reflects committed history; live BetExecuted events keep prepending after this.
		IReadOnlyList<BetRecord> recentOnEntry = _userStatsService?.GetRecentBets(BetHistoryContainer.MaxRecentEntries);
		_betHistoryContainer?.LoadFromHistoricalRecords(recentOnEntry);
		SeedRollFromRecentBets(recentOnEntry);
		Scripts.Diagnostics.SceneEntryProbe.Mark("recentBets");
		BuildBetDisplayToggle();
		BuildSavedStrategySelect();
		Scripts.Diagnostics.SceneEntryProbe.Mark("betViewToggle");
		LoadActiveNodeFinancialState();
		Scripts.Diagnostics.SceneEntryProbe.Mark("nodeFinancialState");
		LoadActiveNodeStrategySnapshot();
		Scripts.Diagnostics.SceneEntryProbe.Mark("strategySnapshot");
		EnsureInitialBankrollFunded();
		Scripts.Diagnostics.SceneEntryProbe.Mark("bankrollFunded");
		EnsureMissingNodeFinancialStates(hadAnyNodeFinancialState);
		Scripts.Diagnostics.SceneEntryProbe.Mark("missingNodeStates");
		RefreshCalculatorFromGameSettings();
		Scripts.Diagnostics.SceneEntryProbe.Mark("calculator");

		// Background autobet (SimulationService): subscribe for live UI updates, and if a background
		// autobet is already running (we navigated back into DiceGame), bind to it instead of starting fresh.
		if (_simulationService != null)
		{
			_simulationService.BetSettled += OnSimBetSettled;
			_simulationService.AutobetStopped += OnSimAutobetStopped;
			if (_simulationService.IsRunning)
			{
				BindToRunningBackgroundAutobet();
			}
			else if (_simulationService.StopNoticePending)
			{
				// The background autobet stopped while we were in another scene — consume its pending notice.
				_simulationService.ConsumeStopNotice();
			}
		}

		Scripts.Diagnostics.SceneEntryProbe.Mark("simBind");
		Scripts.Diagnostics.SceneEntryProbe.ReadyEnded();
	}

	private void InitializeActiveNodeSelector()
	{
		if (_activeNodeSelector == null || _blockchainNetworkRoot == null)
		{
			return;
		}

		_activeNodeSelector.Clear();
		int selectedIndex = 0;
		IReadOnlyList<string> nodeIds = _blockchainNetworkRoot.GetBettableNodeIds();
		for (int index = 0; index < nodeIds.Count; index++)
		{
			string nodeId = nodeIds[index];
			_activeNodeSelector.AddItem(nodeId);
			if (string.Equals(nodeId, _activeNodeId, StringComparison.Ordinal))
			{
				selectedIndex = index;
			}
		}

		if (_activeNodeSelector.ItemCount > 0)
		{
			_activeNodeSelector.Select(selectedIndex);
			_activeNodeId = _activeNodeSelector.GetItemText(selectedIndex);
		}

		RefreshNodeSelectorReadyDots();
	}

	// Green = this node is RUNNING right now, or has a valid ready-to-play strategy; red otherwise
	// (mini-plan 02, D-M2.9). "Running" and "ready" are two different questions and the dot used to
	// answer only the second one, from _nodeStrategies alone — so a scene change turned every node
	// red INCLUDING nodes that were actively betting. That matters more than a normal cosmetic slip:
	// the selector is disabled while an autobet runs, so during a run these dots are the only
	// per-node readout on screen. The running half is sourced from SimulationService — the same place
	// that decides whether the node is really betting (§39.16 rule 6).
	private void RefreshNodeSelectorReadyDots()
	{
		if (_activeNodeSelector == null)
		{
			return;
		}

		bool playerRunning = _simulationService?.IsRunning == true;
		IReadOnlyList<string> activeBots =
			_simulationService?.GetActiveBotNodeIds() ?? Array.Empty<string>();

		for (int index = 0; index < _activeNodeSelector.ItemCount; index++)
		{
			string nodeId = _activeNodeSelector.GetItemText(index);
			bool running = string.Equals(nodeId, PlayerNodeId, StringComparison.Ordinal)
				? playerRunning
				: activeBots.Contains(nodeId);
			bool ready = _nodeStrategies.TryGetValue(nodeId, out NodeStrategyState state) && state.IsValid;
			_activeNodeSelector.SetItemIcon(index, GetReadyDotTexture(running || ready));
		}
	}

	private ImageTexture GetReadyDotTexture(bool ready)
	{
		if (ready)
		{
			_readyDotTexture ??= CreateDotTexture(new Color(0.20f, 0.80f, 0.25f));
			return _readyDotTexture;
		}

		_notReadyDotTexture ??= CreateDotTexture(new Color(0.85f, 0.20f, 0.20f));
		return _notReadyDotTexture;
	}

	private static ImageTexture CreateDotTexture(Color color)
	{
		const int size = 16;
		Image image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
		image.Fill(new Color(0, 0, 0, 0));

		var center = new Vector2(size / 2f, size / 2f);
		float radius = size / 2f - 2f;
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				if (center.DistanceTo(new Vector2(x + 0.5f, y + 0.5f)) <= radius)
				{
					image.SetPixel(x, y, color);
				}
			}
		}

		return ImageTexture.CreateFromImage(image);
	}

	private void OnActiveNodeSelected(long selectedIndex)
	{
		if (_activeNodeSelector == null || selectedIndex < 0 || selectedIndex >= _activeNodeSelector.ItemCount)
		{
			return;
		}

		string nextNodeId = _activeNodeSelector.GetItemText((int)selectedIndex);
		if (string.Equals(nextNodeId, _activeNodeId, StringComparison.Ordinal))
		{
			return;
		}

		// Switching the active node rewrites the shared balance services (BankrollStateService /
		// PrincipalBalanceService) with the selected node's balances. A running background autobet uses
		// those as its source of truth, so switching mid-autobet would corrupt it. The selector is
		// disabled while delegated; this is a defensive guard. To watch bot balances live, use the
		// Block Explorer (per-node, auto-refreshing).
		if (_autobetDelegated)
		{
			return;
		}

		if (_session != null && _session.IsRunning)
		{
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
		}
		StopAllBotRunners();

		SaveActiveNodeStrategySnapshot();
		// Block = the only commit to disk: between-block financial advances stay in-memory (the static
		// NetworkRoot survives scene/node changes) and are NOT persisted, so an app restart reverts every
		// participant to the last mined block. Disk persistence happens only at block-mining (CaptureBlockCheckpoint).
		SaveActiveNodeFinancialState(false);
		_activeNodeId = nextNodeId;
		// restorePlayerFromMirror: when switching BACK to the player the services hold the outgoing bot's
		// balances and must be restored from the player mirror saved at the player→bot switch.
		LoadActiveNodeFinancialState(restorePlayerFromMirror: true);
		LoadActiveNodeStrategySnapshot();
		RefreshHardwareDrivenSpeed(); // re-lock the betting speed to the newly selected node's hardware
		_betHistoryContainer?.ClearEntries();
		// SF.4B.6 parity: switching back to the player reseeds the list from the persistent store, exactly
		// like scene entry does — clearing alone left the player's recent plays blank until the next
		// scene re-entry. Bots keep the cleared list (their history lives in BotPlayHistory).
		if (IsPlayerActive())
		{
			IReadOnlyList<BetRecord> recentForPlayer = _userStatsService?.GetRecentBets(BetHistoryContainer.MaxRecentEntries);
			_betHistoryContainer?.LoadFromHistoricalRecords(recentForPlayer);
			SeedRollFromRecentBets(recentForPlayer);
		}
		UpdateAllUI();
		RefreshCalculatorFromGameSettings();
	}

	private bool IsPlayerActive() =>
		string.Equals(_activeNodeId, PlayerNodeId, StringComparison.Ordinal);

	private void LoadActiveNodeFinancialState(bool restorePlayerFromMirror = false)
	{
		if (_blockchainNetworkRoot == null || _wallet == null)
		{
			return;
		}

		decimal fallbackPrincipal = _principalBalanceService?.CurrentBalance ?? BankrollProgramService.InitialPrincipalBalanceBaseline;
		decimal fallbackBankroll = _wallet.Balance;
		NodeFinancialState state = _blockchainNetworkRoot.GetOrCreateNodeFinancialState(
			_activeNodeId,
			fallbackPrincipal,
			fallbackBankroll);

		// The player's real balances live in PrincipalBalanceService/BankrollStateService/BankrollProgramService
		// (each persists itself and is the single source of truth — see SimulationService's header comment).
		// NodeFinancialState only mirrors the player here for the Active Node Selector's bot-viewing UI. Two
		// call sites, opposite rules:
		//  - SCENE ENTRY (_Ready): NEVER apply the mirror to the player — the live services are authoritative
		//    and the mirror may be stale (refreshed only on the way OUT of DiceGame; BankrollProgrammer/ScFinances
		//    bypass it entirely), so applying it would revert transfers made in other scenes.
		//  - NODE SWITCH back to the player (restorePlayerFromMirror = true): MUST apply the mirror — the
		//    services currently hold the OUTGOING BOT's balances (the selector rewrites them on switch), and the
		//    player mirror was freshly saved at the player→bot switch of this same visit. Skipping the restore
		//    here left the bot's balances masquerading as the player's (silent SC corruption/destruction).
		if (IsPlayerActive() && !restorePlayerFromMirror)
		{
			return;
		}

		_principalBalanceService?.SetBalance(state.PrincipalBalance);
		_bankrollStateService?.SetBalance(state.BankrollBalance);
		_bankrollProgramService?.ReplaceState(state.AutoRechargeAmount, state.TransferRecords);
		_userStatsService?.NoteBalanceDiscontinuity("node_state_load");
		_wallet.SetBalanceForTimeTravel(state.BankrollBalance);
	}

	private void SaveActiveNodeFinancialState(bool persist)
	{
		if (_blockchainNetworkRoot == null || string.IsNullOrWhiteSpace(_activeNodeId))
		{
			return;
		}

		NodeFinancialState state = new()
		{
			PrincipalBalance = _principalBalanceService?.CurrentBalance ?? 0m,
			BankrollBalance = _walletController?.Balance ?? _wallet?.Balance ?? 0m,
			AutoRechargeAmount = _bankrollProgramService?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount,
			TransferRecords = _bankrollProgramService?.Records
				.Select(r => new BankrollProgramService.TransferRecord
				{
					UtcTimestamp = DateTime.SpecifyKind(r.UtcTimestamp, DateTimeKind.Utc),
					Amount = r.Amount,
					Direction = r.Direction,
					Reason = r.Reason
				})
				.ToList() ?? new List<BankrollProgramService.TransferRecord>()
		};

		_blockchainNetworkRoot.SetNodeFinancialState(_activeNodeId, state, persist);
	}

	private void EnsureMissingNodeFinancialStates(bool useStableInitialTemplate)
	{
		if (_blockchainNetworkRoot == null)
		{
			return;
		}

		NodeFinancialState template = useStableInitialTemplate
			? BuildStableInitialNodeFinancialState()
			: BuildCurrentNodeFinancialState();

		_blockchainNetworkRoot.EnsureMissingNodeFinancialStates(template, true);
	}

	private NodeFinancialState BuildStableInitialNodeFinancialState()
	{
		decimal bankroll = BankrollProgramService.DefaultAutoRechargeAmount;
		return new NodeFinancialState
		{
			PrincipalBalance = Math.Max(0m, BankrollProgramService.InitialPrincipalBalanceBaseline - bankroll),
			BankrollBalance = bankroll,
			AutoRechargeAmount = _bankrollProgramService?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount,
			TransferRecords = new List<BankrollProgramService.TransferRecord>()
		};
	}

	private NodeFinancialState BuildCurrentNodeFinancialState() => new()
	{
		PrincipalBalance = _principalBalanceService?.CurrentBalance ?? 0m,
		BankrollBalance = _walletController?.Balance ?? _wallet?.Balance ?? 0m,
		AutoRechargeAmount = _bankrollProgramService?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount,
		TransferRecords = _bankrollProgramService?.Records
			.Select(r => new BankrollProgramService.TransferRecord
			{
				UtcTimestamp = DateTime.SpecifyKind(r.UtcTimestamp, DateTimeKind.Utc),
				Amount = r.Amount,
				Direction = r.Direction,
				Reason = r.Reason
			})
			.ToList() ?? new List<BankrollProgramService.TransferRecord>()
	};

	// Mini-plan 15 B — the clock and the mining-status block are the two fastest-moving readouts in the game,
	// and they were rebuilt EVERY frame, which is both why a digit froze (a near-constant step per sample) and
	// per-frame string work of exactly the shape Pattern 6 warns about. One sampler drives both, on a cadence
	// measured from the clock itself; the quantizers decide how many digits of each nonce count are real.
	private readonly AdaptiveReadoutSampler _readoutSampler = new() { TraceName = "DiceGame clock" };
	private readonly CounterQuantizer _ownNonceQuantizer = new();
	private readonly CounterQuantizer _casinoNonceQuantizer = new();

	public override void _Process(double delta)
	{
		Scripts.Diagnostics.SceneEntryProbe.FirstProcess();   // mini-plan 21 E: reports once per entry, DEBUG-only
		if (_readoutSampler.ShouldRepaint(delta))
		{
			UpdateCurrentAppTimeUI(cadenceSample: true);
		}

		UpdateBoardVotePauseUi();
		ApplyNavShiftIfDirty();
		TickManualBurst(delta);
		TickAutoBet(delta);
		// AFTER TickAutoBet, deliberately: the background sim settles this frame's bets during _Process, so
		// flushing first would paint the previous frame's state and leave this frame's stale until the next.
		FlushSettledBetUiIfDirty();
	}

	// Step 14 (ND.8b.3/D-ND8.18 follow-up): while a board vote awaits the player's ballot, BOTH betting
	// buttons (manual + AUTO) are disabled — the button-level mirror of the ExecuteBet/SimulationService
	// gates, so the player can see that time cannot be advanced until the company matter is attended.
	// Edge-triggered: the per-frame cost is one static flag read; buttons update only on change.
	private bool _boardVotePauseActive;

	private void UpdateBoardVotePauseUi()
	{
		bool awaiting = NetworkRoot.IsAwaitingPlayerVote;
		if (awaiting == _boardVotePauseActive)
		{
			return;
		}

		_boardVotePauseActive = awaiting;
		ApplyBettingControlsAvailability();
	}

	private readonly List<(Control Button, Vector2 Home)> _navButtonsHome = new();
	private bool _navShiftDirty;
	private const float NavClearanceFromPause = 12f;

	private void ApplyNavShiftIfDirty()
	{
		if (!_navShiftDirty || _navButtonsHome.Count == 0)
		{
			return;
		}

		_navShiftDirty = false;
		float shift = 0f;
		Control pause = _strategyPanel?.PauseButton;
		if (pause != null && pause.IsVisibleInTree())
		{
			float homeLeft = float.MaxValue;
			foreach ((Control _, Vector2 home) in _navButtonsHome)
			{
				homeLeft = Math.Min(homeLeft, home.X);
			}

			// DiceGame fills the viewport from its origin, so the panel's global coordinates are this scene's.
			shift = Math.Max(0f, pause.GetGlobalRect().End.X + NavClearanceFromPause - homeLeft);
		}

		foreach ((Control button, Vector2 home) in _navButtonsHome)
		{
			button.Position = home + new Vector2(shift, 0f);
		}
	}

	// Composes the two independent button locks: only the player node may bet (bot-active lock, see the
	// node-selector load path), and a pending board vote suspends betting entirely (D-ND8.18).
	private void ApplyBettingControlsAvailability() =>
		_strategyPanel.SetBettingControlsEnabled(IsPlayerActive() && !NetworkRoot.IsAwaitingPlayerVote);

	// --- Eventos UI ---
	private void OnHighLowToggled()
	{
		_highLowToggleBtn.Text = _highLowToggleBtn.ButtonPressed ? "HIGH" : "LOW";
		SaveActiveNodeStrategySnapshot();
		UpdateAllUI();
	}

	private void OnChanceChanged(double _)
	{
		SaveActiveNodeStrategySnapshot();
		UpdateAllUI();
		RefreshCalculatorFromGameSettings();
	}

	// --- Eventos de componentes ---
	private void OnBetInputChanged(string newText)
	{
		if (newText == "MAX")
		{
			decimal maxBet = _walletController.Balance;
			_strategyPanel.ManualSetBetAmount(maxBet);
			UpdateStrategySaveLoadButtons();
			return;
		}

		if (newText == "MIN")
		{
			decimal minBet = 0.00000001m;
			_strategyPanel.ManualSetBetAmount(minBet);
			UpdateStrategySaveLoadButtons();
			return;
		}

		RefreshCalculatorFromGameSettings();
		UpdateStrategySaveLoadButtons();
	}

	private void OnStrategyConfigChanged()
	{
		if (_loadingNodeStrategy)
		{
			return;
		}

		// 🔥 reset inmediato en manual
		if (_session.IsRunning)
		{
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
		}

		if (_walletFSM.State != WalletState.Bankrupt)
			_strategyPanel.SetManualEnabled(true);

		RefreshCalculatorFromGameSettings();
		SaveActiveNodeStrategySnapshot();
		UpdateStrategySaveLoadButtons();
	}

	private void OnSaveStrategyPressed()
	{
		string strategyName = _strategyNameInput.Text.Trim();
		if (string.IsNullOrWhiteSpace(strategyName) || !_strategyPanel.TryGetValidBet(out decimal baseBet) || baseBet <= 0m)
		{
			UpdateStrategySaveLoadButtons();
			return;
		}

		BettingStrategyConfig config = _strategyPanel.BuildConfig();
		_savedStrategyRepository.Save(new SavedBettingStrategy
		{
			Name = strategyName,
			GameId = GameId,
			Config = config,
			NumberOfBets = _strategyPanel.NumberOfBets,
			// Auto-recharge is an ACCOUNT setting, not part of a strategy — see SavedBettingStrategy.
			WinningChance = (int)_chanceSlider.Value,
			BetHigh = _highLowToggleBtn.ButtonPressed,
			BetsPerSecond = GetAutoBetBaseAps()
		});

		SaveActiveNodeStrategySnapshot();
		RefreshSavedStrategySelect();
		UpdateStrategySaveLoadButtons();
	}

	private void OnLoadStrategyPressed()
	{
		string strategyName = _strategyNameInput.Text.Trim();
		if (!_savedStrategyRepository.TryGet(GameId, strategyName, out SavedBettingStrategy saved))
		{
			UpdateStrategySaveLoadButtons();
			return;
		}

		if (_session != null && _session.IsRunning)
		{
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
		}

		_strategyNameInput.Text = saved.Name;
		_loadingNodeStrategy = true;
		// Carry the CURRENT auto-recharge value through unchanged: a saved strategy no longer stores one,
		// because it is an account setting rather than a strategy property (see SavedBettingStrategy).
		_strategyPanel.ApplyStrategySettings(
			saved.Config,
			saved.NumberOfBets,
			_strategyPanel.AutoRechargeEnabled);
		_chanceSlider.Value = Math.Clamp(saved.WinningChance, 1, 95);
		_highLowToggleBtn.ButtonPressed = saved.BetHigh;
		_highLowToggleBtn.Text = saved.BetHigh ? "HIGH" : "LOW";
		ApplyAutoBetSpeedSettings(saved.BetsPerSecond);
		_loadingNodeStrategy = false;
		// A saved strategy's stored auto-recharge value does NOT override the player's service-level flag — the
		// panel toggle is only an access point to BankrollProgramService.AutoRechargeEnabled (SF.2.8). Re-seed
		// from the service before snapshotting so the per-node snapshot captures the authoritative value.
		SyncPlayerAutoRechargeToggleFromService();
		SaveActiveNodeStrategySnapshot();
		UpdateAllUI();
		RefreshCalculatorFromGameSettings();
		UpdateStrategySaveLoadButtons();
		// Mini-plan 27 unit 3b: two lines appended, nothing above them changed. The name field was set from code,
		// which emits no TextChanged, so the dropdown is re-synced here.
		SyncSavedStrategySelectToName();
		TraceStrategyDropdown($"[StrategyLoad] '{saved.Name}' -> {_activeNodeId}");
	}

	// ── The saved-strategies dropdown (mini-plan 27 unit 3b, developer 2026-10-21) ──────────────────────────────
	// Lists every saved strategy for this game, so one can be loaded onto the player or any bot without typing its
	// name. Choosing an item only SELECTS it: it writes the name into the name field, and the existing Load button
	// applies it (D-27.10). Applying on choice would overwrite the active node's panel, unsaved edits included, on a
	// mis-click. And OnLoadStrategyPressed stays the one apply path. The dropdown never shows a name other than the one
	// Load will use (Convention 6): editing the field re-selects the matching item, or none.
	//
	// Built in code, in the free part of the band between StrategySaveControls (which ends at y 526) and the
	// bet-history headers (y 574), beside the Bet View button (x 706-966), and above the bottom band (§29.11). Code-built
	// for now, like its neighbour: every such control moves into a scene at Basic Mode's UI refinement (PRIVATE_ROADMAP
	// "UI built in scenes").
	private void BuildSavedStrategySelect()
	{
		_savedStrategySelect = new OptionButton
		{
			Position = new Vector2(976f, 528f),
			CustomMinimumSize = new Vector2(280f, 42f),
			Size = new Vector2(280f, 42f),
			ClipText = true,
			TooltipText = "Your saved strategies for this game. Choosing one puts its name in the field on the left; "
				+ "Load Strategy then applies it to the selected node (the player or a bot).",
		};
		_savedStrategySelect.ItemSelected += OnSavedStrategySelected;
		AddChild(_savedStrategySelect);
		RefreshSavedStrategySelect();
	}

	// Rebuilt from the repository on entry and after every Save. DiceGame's repository instance is the only writer
	// while the scene is open, so nothing else can change the list under it (Pattern 6: no event needed).
	private void RefreshSavedStrategySelect()
	{
		if (_savedStrategySelect == null || _savedStrategyRepository == null)
		{
			return;
		}

		List<string> names = _savedStrategyRepository.GetNamesForGame(GameId);
		_savedStrategySelect.Clear();
		foreach (string name in names)
		{
			_savedStrategySelect.AddItem(name);
		}

		ApplySavedStrategySelectAvailability();
		SyncSavedStrategySelectToName();
		TraceStrategyDropdown($"[StrategyDropdown] listed {names.Count} for {GameId}");
	}

	// Disabled with nothing to list, and under the run lock (developer, 2026-10-21): during an autobet the dropdown
	// is locked like Load, the chance slider and HIGH/LOW. One rule, called from both the refresh and ApplyRunLock,
	// so it holds whichever of the two runs first on scene entry.
	private void ApplySavedStrategySelectAvailability()
	{
		if (_savedStrategySelect != null)
		{
			_savedStrategySelect.Disabled = _runLocked || _savedStrategySelect.ItemCount == 0;
		}
	}

	private void OnSavedStrategySelected(long index)
	{
		// Setting Text from code emits no TextChanged, so the Load/Save buttons are refreshed by hand.
		_strategyNameInput.Text = _savedStrategySelect.GetItemText((int)index);
		UpdateStrategySaveLoadButtons();
	}

	private void SyncSavedStrategySelectToName()
	{
		if (_savedStrategySelect == null)
		{
			return;
		}

		string typed = _strategyNameInput?.Text.Trim() ?? string.Empty;
		for (int index = 0; index < _savedStrategySelect.ItemCount; index++)
		{
			if (string.Equals(_savedStrategySelect.GetItemText(index), typed, StringComparison.OrdinalIgnoreCase))
			{
				_savedStrategySelect.Select(index);
				return;
			}
		}

		// No match: nothing selected, and the closed button says what it holds instead of showing a blank.
		_savedStrategySelect.Select(-1);
		_savedStrategySelect.Text = _savedStrategySelect.ItemCount == 0
			? "No saved strategies"
			: $"Saved strategies ({_savedStrategySelect.ItemCount})";
	}

	// S9's instruments: read from godot.log afterwards, so they print with GD.Print, DEBUG builds only.
	[System.Diagnostics.Conditional("DEBUG")]
	private static void TraceStrategyDropdown(string line) => GD.Print(line);

	private void UpdateStrategySaveLoadButtons()
	{
		if (_saveStrategyBtn == null || _loadStrategyBtn == null || _strategyNameInput == null || _strategyPanel == null)
		{
			return;
		}

		bool hasName = !string.IsNullOrWhiteSpace(_strategyNameInput.Text);
		bool hasValidBaseBet = _strategyPanel.TryGetValidBet(out decimal baseBet) && baseBet > 0m;
		_saveStrategyBtn.Disabled = !hasName || !hasValidBaseBet;
		// Loading a strategy mid-run would rewrite the panel without touching the session that is
		// actually executing — the same lie the run lock exists to remove. SAVING stays available: it
		// records what is on screen, which during a run is what is running.
		_loadStrategyBtn.Disabled = _runLocked
			|| _savedStrategyRepository == null
			|| !_savedStrategyRepository.HasAnyForGame(GameId);
	}

	private void ApplyAutoBetSpeedSettings(int betsPerSecond)
	{
		// Speed is hardware-locked (Phase 3): a saved/loaded BetsPerSecond no longer drives the selector.
		// Always re-lock to the active node's current hardware total so the display can't go stale (e.g. to 1X).
		RefreshHardwareDrivenSpeed();
	}

	private void SaveActiveNodeStrategySnapshot()
	{
		if (_loadingNodeStrategy || string.IsNullOrWhiteSpace(_activeNodeId) || _strategyPanel == null)
		{
			return;
		}

		// D-M2.8, the case that proves the rule: while a player session runs, the panel's "Amount to bet"
		// and "Number of bets" are LIVE READOUTS of the progression, not inputs — OnSimBetSettled writes
		// the current bet and the remaining count into them on every settled bet. Snapshotting the panel
		// here therefore stored a mid-ladder rung as the BASE BET (observed: base 0.01 saved as 0.02110000
		// after two losses at +111%), and re-entering the scene restored that corrupted value as the
		// strategy. Reached from _ExitTree on every navigation away mid-run. (It was also reached from the APS
		// selector's ItemSelected; that dropdown is a label since mini-plan 23 D, and the path is gone.)
		//
		// So take the executing config from the SESSION, which is what actually governs the run. Only the
		// player's autobet is delegated; a bot snapshot still comes from the panel that configured it.
		SimulationService.PlayerAutobetConfig liveConfig =
			IsPlayerActive() && _simulationService?.IsRunning == true ? _simulationService.CurrentConfig : null;

		BettingStrategyConfig config;
		int numberOfBets;
		if (liveConfig?.Strategy != null)
		{
			config = liveConfig.Strategy;
			numberOfBets = liveConfig.NumberOfBets;
		}
		else
		{
			if (!_strategyPanel.TryGetValidBet(out decimal baseBet) || baseBet <= 0m)
			{
				return;
			}

			config = _strategyPanel.BuildConfig();
			numberOfBets = _strategyPanel.NumberOfBets;
			if (!IsPlayerActive())
			{
				config = BuildBotStrategyConfig(config);
			}
		}

		if (config == null || config.BaseBet <= 0m)
		{
			return;
		}

		NodeStrategyState state = new NodeStrategyState
		{
			Config = CloneConfig(config),
			NumberOfBets = numberOfBets,
			AutoRechargeEnabled = !IsPlayerActive() || _strategyPanel.AutoRechargeEnabled,
			// Chance and HIGH/LOW are locked during a run, so the widgets still hold what the session
			// captured — but read them from the session when it exists, for the same reason as above.
			WinningChance = liveConfig?.Chance ?? (int)_chanceSlider.Value,
			BetHigh = liveConfig?.BetHigh ?? _highLowToggleBtn.ButtonPressed,
			BetsPerSecond = GetAutoBetBaseAps()
		};
		_nodeStrategies[_activeNodeId] = state;
		// Mini-plan 27: the service writes only when a value really changed, so the many snapshots that restate the
		// same strategy (a manual bet, a bot burst, leaving the scene) cost a comparison and no write.
		_userSettings?.SetStrategyPanelValues(_activeNodeId, new UserSettingsService.StrategyPanelValues
		{
			Config = CloneConfig(state.Config),
			NumberOfBets = state.NumberOfBets,
			WinningChance = state.WinningChance,
			BetHigh = state.BetHigh
		});

		RefreshNodeSelectorReadyDots();
	}

	// Mini-plan 27 unit 2 — once per launch, before the store is first read. A node already in the store is left as
	// it is (the store outlives the scene; the file only seeds it). Of NodeStrategyState's fields, two are not
	// preferences and are rebuilt here instead of stored (plan §4): auto-recharge (the player's is
	// BankrollProgramService's flag, re-synced on load; a bot's is always ON) and BetsPerSecond (hardware-locked,
	// ApplyAutoBetSpeedSettings ignores it).
	private void RestoreNodeStrategiesFromSettingsOnce()
	{
		if (_nodeStrategiesRestored || _userSettings == null)
		{
			return;
		}

		_nodeStrategiesRestored = true;
		foreach (string nodeId in _userSettings.GetStrategyPanelNodeIds())
		{
			UserSettingsService.StrategyPanelValues values = _userSettings.GetStrategyPanelValues(nodeId);
			if (_nodeStrategies.ContainsKey(nodeId) || values?.Config == null || values.Config.BaseBet <= 0m)
			{
				continue;
			}

			_nodeStrategies[nodeId] = new NodeStrategyState
			{
				Config = values.Config,
				NumberOfBets = values.NumberOfBets,
				AutoRechargeEnabled = !string.Equals(nodeId, PlayerNodeId, StringComparison.Ordinal)
					|| (_bankrollProgramService?.AutoRechargeEnabled ?? true),
				WinningChance = Math.Clamp(values.WinningChance, 1, 95),
				BetHigh = values.BetHigh,
				BetsPerSecond = 1
			};
		}
	}

	private void LoadActiveNodeStrategySnapshot()
	{
		if (_strategyPanel == null)
		{
			return;
		}

		_loadingNodeStrategy = true;
		try
		{
			_strategyPanel.SetBotStrategyMode(!IsPlayerActive());
			if (!_nodeStrategies.TryGetValue(_activeNodeId, out NodeStrategyState state) || !state.IsValid)
			{
				_strategyPanel.ClearStrategySettings();
				ApplyAutoBetSpeedSettings(1);
			}
			else
			{
				_strategyPanel.ApplyStrategySettings(state.Config, state.NumberOfBets, state.AutoRechargeEnabled);
				_chanceSlider.Value = Math.Clamp(state.WinningChance, 1, 95);
				_highLowToggleBtn.ButtonPressed = state.BetHigh;
				_highLowToggleBtn.Text = state.BetHigh ? "HIGH" : "LOW";
				ApplyAutoBetSpeedSettings(state.BetsPerSecond);
			}
		}
		finally
		{
			_loadingNodeStrategy = false;
		}

		// Only the player may place bets / autobet; bots can be configured but not bet manually.
		// (Composed with the ND.8b.3 board-vote pause lock — see ApplyBettingControlsAvailability.)
		ApplyBettingControlsAvailability();

		// For the player, the panel's auto-recharge toggle is only an access point to the service-level flag —
		// reflect the service value (single source of truth), overriding whatever the per-node snapshot set.
		SyncPlayerAutoRechargeToggleFromService();

		UpdateStrategySaveLoadButtons();
	}

	// The auto-recharge toggle in the StrategyControlPanel used to be a stand-alone per-run control (a coupling
	// that was convenient for testing). Since Step 12 (SF.2.8) the PLAYER's Bankroll auto-recharge is owned by
	// BankrollProgramService.AutoRechargeEnabled — the same flag the new Bankroll Programmer toggle sets. So for
	// the player the panel toggle is now merely a second access point to that service flag: it seeds FROM the
	// service on load (here) and writes TO the service on user interaction (OnAutoRechargeToggledFromPanel).
	// Bots keep their own per-node AutoRechargeEnabled (always ON — BuildBotStrategyConfig / bot strategy mode),
	// so this proxy is a no-op unless the player node is active.
	private void SyncPlayerAutoRechargeToggleFromService()
	{
		if (IsPlayerActive() && _bankrollProgramService != null)
		{
			_strategyPanel?.SetAutoRechargeEnabled(_bankrollProgramService.AutoRechargeEnabled);
		}
	}

	// User flipped the panel's auto-recharge toggle. For the player, push it into the service (single source of
	// truth). Skips writes during a snapshot/saved-strategy load (ApplyStrategySettings itself raises this event)
	// and in bot strategy mode, so only genuine player interaction updates the shared flag.
	private void OnAutoRechargeToggledFromPanel(bool enabled)
	{
		if (IsPlayerActive() && !_loadingNodeStrategy)
		{
			_bankrollProgramService?.SetAutoRechargeEnabled(enabled);
		}
		UpdateBalanceUI();
	}

	private static BettingStrategyConfig CloneConfig(BettingStrategyConfig config)
	{
		if (config == null)
		{
			return null;
		}

		return new BettingStrategyConfig
		{
			BaseBet = config.BaseBet,
			IncreaseOnLossPercent = config.IncreaseOnLossPercent,
			IncreaseOnWinPercent = config.IncreaseOnWinPercent,
			StopOnProfit = config.StopOnProfit,
			StopOnLoss = config.StopOnLoss,
			StopOnBlockMined = config.StopOnBlockMined,
			StopOnSigilSetCompleted = config.StopOnSigilSetCompleted,
			InsistAfterStopOnProfit = config.InsistAfterStopOnProfit,
			InsistAfterStopOnLoss = config.InsistAfterStopOnLoss
		};
	}

	private BettingStrategyConfig BuildBotStrategyConfig(BettingStrategyConfig config)
	{
		if (config == null)
		{
			return null;
		}

		// A bot session is only ever restarted on InsufficientBalance (SimulationService), so a stop that
		// STOPS is terminal for that bot. Both insist switches are therefore forced ON here — not merely
		// mirrored from the panel — so the invariant holds no matter what a stored per-node snapshot carries:
		// a bot's stop always resets the progression to base and keeps betting.
		return new BettingStrategyConfig
		{
			BaseBet = config.BaseBet,
			IncreaseOnLossPercent = config.IncreaseOnLossPercent,
			IncreaseOnWinPercent = config.IncreaseOnWinPercent,
			StopOnProfit = config.StopOnProfit,
			StopOnLoss = config.StopOnLoss,
			StopOnBlockMined = false,
			StopOnSigilSetCompleted = false,   // a bot draws no sigil
			InsistAfterStopOnProfit = true,
			InsistAfterStopOnLoss = true
		};
	}

	private int GetManualBurstAttemptCount()
	{
		double effective = GetEffectiveAutoBetsPerGameSecond();
		return Math.Max(1, Math.Min((int)MaxAutoBetsPerRealSecond, (int)Math.Floor(effective)));
	}

	// --- Manual Bet Session
	private void OnManualBetFromPanel()
	{
		// Only the player bets; bots are configured but never bet directly.
		if (!IsPlayerActive())
			return;

		if (!_strategyPanel.TryGetValidBet(out _))
		{
			return;
		}

		if (!IsBetAmountValid(_strategyPanel.BetAmount))
			return;

		// Manual mining must see the same total network power as autobet (player + configured bots), so the
		// difficulty regulator behaves identically — otherwise manual stays at the player-only difficulty.
		SetManualMiningPower();

		EnsureSession(false); // 🔥 manual

		SaveActiveNodeStrategySnapshot();
		int attempts = GetManualBurstAttemptCount();
		double timePerBet = GameSecondsPerManualBet / Math.Max(1, attempts);
		// Start one tick AFTER "now", never exactly at it — a bet's timestamp must always land strictly after
		// whatever the clock was previously (see OQ-BP.11 in player-and-casino-bankroll-programmer-plan.md): an
		// exact match with a reset/checkpoint boundary is indistinguishable from "part of that boundary" and
		// can survive a rollback (RollbackToUtc's `TimestampUtc > checkpoint` check) that should have discarded it.
		DateTime burstBaseUtc = (_calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow).AddSeconds(timePerBet);
		_manualBurstTotal = attempts;
		_manualBurstRemaining = attempts;
		_manualBurstIndex = 0;
		_manualBurstExecuted = 0;
		_manualBurstTimePerBet = timePerBet;
		_manualBurstBaseUtc = burstBaseUtc;
		_manualBurstAccumulator = 0d;
		_strategyPanel.SetBettingControlsEnabled(false);

		// The first bet lands inside the press itself, so the button answers immediately; the rest are paced
		// by TickManualBurst. A one-bet burst therefore never waits for a frame.
		if (!ExecuteManualBurstBet())
		{
			FinishManualBurst();
		}
	}

	// One bet of the running burst. Returns false when the burst must end — the session stopped (a stop
	// condition, a mined block) or the next bet no longer fits the bankroll. These are the two checks the
	// old synchronous loop made per iteration, unchanged.
	private bool ExecuteManualBurstBet()
	{
		if (!ManualBurstActive || _session == null || !_session.IsRunning)
		{
			return false;
		}

		if (_session.CurrentBet > _walletController.Balance)
		{
			return false;
		}

		ExecuteBet(_manualBurstBaseUtc.AddSeconds(_manualBurstIndex * _manualBurstTimePerBet), suppressClockAdvance: true);
		_manualBurstIndex++;
		_manualBurstExecuted++;
		_manualBurstRemaining--;
		return _manualBurstRemaining > 0 && _session.IsRunning;
	}

	// Driven from _Process. The whole burst occupies ManualBurstRealSeconds regardless of how many bets it
	// holds, so the pace is the hardware's: 99 credits fill the second with 99 rolls, 1 credit with one.
	private void TickManualBurst(double delta)
	{
		if (!ManualBurstActive)
		{
			return;
		}

		double interval = ManualBurstRealSeconds / Math.Max(1, _manualBurstTotal);
		_manualBurstAccumulator += Math.Max(0d, delta);
		while (ManualBurstActive && _manualBurstAccumulator >= interval)
		{
			_manualBurstAccumulator -= interval;
			if (!ExecuteManualBurstBet())
			{
				break;
			}
		}

		if (!ManualBurstActive || _session == null || !_session.IsRunning)
		{
			FinishManualBurst();
		}
	}

	// The tail the button handler used to run straight after its loop: advance the clock once for the whole
	// burst, then let the bots take their matching turn. Also reached when the burst is cut short — by a stop
	// condition, or by leaving the scene — so the clock is never left owing a tick for bets already settled.
	private void FinishManualBurst()
	{
		int executed = _manualBurstExecuted;
		_manualBurstTotal = 0;
		_manualBurstRemaining = 0;
		_manualBurstIndex = 0;
		_manualBurstExecuted = 0;
		_manualBurstAccumulator = 0d;

		// Stop-on-block must leave the clock EXACTLY at the block it stopped on (canonical rule, OQ-BP.9 /
		// OQ-CG.9), mirroring FreezeCalendarAtBlockStop in the autobet path: the block was mined at the current
		// clock, so DON'T advance one manual tick past it when the burst was halted by a mined block. A normal
		// burst advances as usual. Persist the pinned instant so calendar_state.json matches the checkpoint.
		bool stoppedOnBlock = _session != null
			&& !_session.IsRunning
			&& _session.LastStopReason == IBettingStrategy.StopReason.StopOnBlockMined;
		if (executed > 0 && !stoppedOnBlock)
			AdvanceClockForBet();
		else if (stoppedOnBlock)
			_calendarTimeService?.PersistCurrentTime();
		if (_session == null || _session.IsRunning || _session.LastStopReason != IBettingStrategy.StopReason.StopOnBlockMined)
		{
			RunBotManualBurst();
		}

		// Hand the buttons back, unless something else is holding them: a bot active node or a pending board
		// vote (both composed by ApplyBettingControlsAvailability), or a stop gate that deliberately keeps
		// manual betting disabled until the player double-clicks the toggle that set it.
		ApplyBettingControlsAvailability();
		if (_manualStopGate != ManualStopGate.None || _autobetDelegated)
		{
			_strategyPanel.SetManualEnabled(false);
		}
	}

	// --- Autobet Session
	// Developer's request, 2026-09-29: a second click landing on AUTO right after a stop restarted the run
	// immediately — a double-click on STOP is enough to do it, and during a measurement that silently starts a
	// new session where the protocol expected none. So a start is ignored for a moment after a stop.
	//
	// ONE DIRECTION ONLY, and that asymmetry is the whole point: STOP is never blocked, at any time, by
	// anything here. A guard that could swallow a stop would be worse than the accident it prevents — stopping
	// is how a player ends a run that is losing money.
	private const ulong AutobetRestartGuardMsec = 1000;
	private ulong _autobetStoppedAtMsec;

	// Called from both stop paths: the player's own STOP, and a session that stopped itself.
	private void NoteAutobetStopped() => _autobetStoppedAtMsec = Godot.Time.GetTicksMsec();

	private void OnAutoBetToggled(bool running)
	{
		// Only the player may run an autobet; bots are configured but never bet directly.
		if (running && !IsPlayerActive())
		{
			_strategyPanel.SetAutoRunning(false);
			return;
		}

		// The restart guard. `_autobetStoppedAtMsec > 0` keeps it from firing on the first start of a process,
		// where the clock itself is still inside the window.
		if (running && _autobetStoppedAtMsec > 0
			&& Godot.Time.GetTicksMsec() - _autobetStoppedAtMsec < AutobetRestartGuardMsec)
		{
			_strategyPanel.SetAutoRunning(false); // keep the button honest about what is actually running
			GD.Print($"[AutoBet] Start ignored — it arrived within {AutobetRestartGuardMsec} ms of the last stop " +
					 "(the double-click guard). Press AUTO again.");
			return;
		}

		if (running)
		{
			if (!_strategyPanel.TryGetValidBet(out decimal bet))
			{
				return;
			}

			bool isValidBet = IsBetAmountValid(_strategyPanel.BetAmount);

			if (!isValidBet)
			{
				_strategyPanel.SetAutoRunning(!running);
				return;
			}
		}

		_strategyPanel.SetManualEnabled(!running);
		_strategyPanel.SetAutoRunning(running);
		_strategyPanel.SetAutoPaused(false);
		_isAutoPaused = false;

		if (!running)
		{
			NoteAutobetStopped(); // starts the restart guard's window; stopping itself is never guarded
			// Stop the background player autobet (owned by SimulationService) and re-sync DiceGame's
			// own wallet from the bankroll source of truth so manual betting resumes correctly.
			_simulationService?.Stop();
			_autobetDelegated = false;

			if (_calendarTimeService != null)
			{
				_calendarTimeService.IsRunning = false;
				_calendarTimeService.IsAutobetActive = false;
			}
			_autoBetTimer.Stop();
			_autoBetAccumulatorGameSeconds = 0d;
			_autoBetLastRateSampleMsec = 0;
			_autoBetBetsSinceSample = 0;
			_autoBetLastMeasuredRealPerSec = 0d;
			_autoBetLastMeasuredGamePerSec = 0d;
			_autoBetVirtualTimestampInitialized = false;
			_autoBetLastExecutedTimestampUtc = null;
			_userStatsService?.SetHighFrequencyMode(false);
			StopAllBotRunners();
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
			SetActiveNodeSelectorLocked(false);
			ApplyRunLock(false);
			RefreshNodeSelectorReadyDots(); // D-M2.9
			ReseedWalletAcrossTransition(); // delegated run ended — this wallet may write the journal again
			RefreshCalculatorFromGameSettings();
			return;
		}

		_userStatsService?.SetHighFrequencyMode(true);
		if (_calendarTimeService != null)
		{
			_calendarTimeService.SpeedMultiplier = GameSecondsPerRealSecond;
			_calendarTimeService.IsRunning = true;
			_calendarTimeService.IsAutobetActive = true;
		}
		// Delegate the PLAYER autobet to SimulationService so it keeps running across scene changes.
		// The service builds its own session/wallet (seeded from the bankroll source of truth).
		_simulationService?.StartPlayerAutobet(new SimulationService.PlayerAutobetConfig
		{
			Chance = (int)_chanceSlider.Value,
			BetHigh = _highLowToggleBtn.ButtonPressed,
			BetsPerSecond = GetEffectiveAutoBetsPerGameSecond(),
			NumberOfBets = _strategyPanel.NumberOfBets,
			ActiveNodeId = _activeNodeId,
			GameId = GameId,
			StopOnBlockMined = _strategyPanel.StopOnBlockMinedEnabled,
			AutoRecharge = _strategyPanel.AutoRechargeEnabled,
			IsPlayerActive = IsPlayerActive(),
			Strategy = _strategyPanel.BuildConfig()
		});
		_autobetDelegated = true;
		SetActiveNodeSelectorLocked(true);
		ApplyRunLock(true);
		StartBotRunners();
		RefreshNodeSelectorReadyDots(); // D-M2.9: "running" just changed — the dots must follow it.

		_autoBetAccumulatorGameSeconds = 0d;
		_autoBetLastRateSampleMsec = 0;
		_autoBetBetsSinceSample = 0;
		_autoBetLastMeasuredRealPerSec = 0d;
		_autoBetLastMeasuredGamePerSec = 0d;
		_autoBetVirtualTimestampUtc = DateTime.UtcNow;
		_autoBetVirtualTimestampInitialized = true;
		_autoBetLastExecutedTimestampUtc = null;
		_lastPrintedMeasuredRealPerSec = 0d;
		_lastAutoBetTelemetryPrintMsec = 0;
		GD.Print($"[AutoBet] Start aps={GetAutoBetBaseAps()}");
		RefreshCalculatorFromGameSettings();
	}

	// The PAUSE button. This used to guard on `_session` — DiceGame's LOCAL session, which serves manual
	// bets and is inert while the autobet is delegated — so the handler returned on its first line for
	// every background run and the button did nothing at all (dead since delegation landed 2026-06-22;
	// see ProjectDesignManual §24.13c). It now targets the run that actually exists.
	//
	// The freeze itself belongs to SimulationService, not here: while delegated that service is the sole
	// owner of CalendarTimeService.IsRunning and re-asserts it every frame, so a DiceGame-side clock stop
	// would be undone on the next tick. `_isAutoPaused` is still maintained for the local autobet path.
	private void OnAutoPauseToggled(bool paused)
	{
		if (_simulationService == null || !_simulationService.IsRunning)
			return;

		_isAutoPaused = paused;
		_simulationService.SetPaused(paused);
		_strategyPanel.SetAutoPaused(paused);
	}

	// ── Background autobet (SimulationService) integration ──────────────────────

	// The active-node selector rewrites the shared balance services on switch, so it must be locked
	// while a background autobet is running (it uses those services as its source of truth).
	private void SetActiveNodeSelectorLocked(bool locked)
	{
		if (_activeNodeSelector != null)
		{
			_activeNodeSelector.Disabled = locked;
		}
	}

	// Single writer for "a player session is running, so its captured settings are read-only".
	// Covers the DiceGame-owned controls (chance, HIGH/LOW, Load strategy) and delegates the panel's
	// own controls to StrategyControlPanel.SetRunLocked. Called at every transition that changes
	// whether a run is in progress — start, stop, self-stop, and re-binding on scene entry — because
	// re-entering DiceGame rebuilds the scene with every control back at its default enabled state.
	private void ApplyRunLock(bool locked)
	{
		_runLocked = locked;
		_strategyPanel?.SetRunLocked(locked);

		if (_chanceSlider != null)
		{
			_chanceSlider.Editable = !locked;
			_chanceSlider.Modulate = locked ? new Color(1f, 1f, 1f, 0.5f) : Colors.White;
		}

		if (_highLowToggleBtn != null)
		{
			_highLowToggleBtn.Disabled = locked;
		}

		UpdateStrategySaveLoadButtons();
		ApplySavedStrategySelectAvailability();
	}

	/// <summary>
	/// Reseed across a TRANSITION — the delegated autobet started, stopped, or the scene rebound to a
	/// running one. Declares the jump, because after a transition this wallet may become the journal's
	/// writer again (a manual bet), and its balance has just moved wholesale.
	/// </summary>
	private void ReseedWalletAcrossTransition()
	{
		// Mini-plan 05 D3: a reseed replaces the wallet balance wholesale, so it is a declared jump.
		_userStatsService?.NoteBalanceDiscontinuity("wallet_reseed");
		ReseedWalletFromBankrollSource();
	}

	/// <summary>
	/// The same reseed WITHOUT declaring a discontinuity, for the steady state of a delegated autobet.
	///
	/// <para><b>Why the declaration had to go, mini-plan 08 P1.</b>
	/// <see cref="UserStatsService.NoteBalanceDiscontinuity"/> DROPS the journal's comparison baseline — by
	/// design, so the next registered bet re-seeds instead of being compared across a declared jump. This
	/// reseed ran once per settled bet, immediately after <c>OnBetExecutedRegisterBet</c> had just set that
	/// baseline. So every bet's baseline was destroyed before the next bet could be compared against it, and
	/// **the continuity sentinel was comparing nothing at all on the entire delegated-autobet path.**</para>
	///
	/// <para>Its silence is load-bearing in mini-plans 05 and 06 and in INC-003, and CLAUDE.md states that
	/// the silence "is evidence". Here it was structural. <b>A sentinel disarmed by a UI subscriber reads
	/// exactly like a sentinel that found nothing</b> — the same failure the T0 boot banner exists to
	/// prevent, one layer further in: that banner proves the check is COMPILED, and nothing proved it was
	/// COMPARING.</para>
	///
	/// <para><b>Why dropping it is safe, verified rather than assumed.</b> This wallet is a DISPLAY COPY
	/// while the autobet is delegated; the journal's writer is <c>SimulationService</c>'s own wallet, so a
	/// jump here is not a jump in the audited series. And every genuine discontinuity on that path is
	/// already declared at its source by <c>SimulationService</c>: <c>autobet_session_wallet</c> when the
	/// session wallet is built, <c>manual_return</c> on a withdrawal, and <c>deposit</c> (via
	/// <c>RegisterDeposit</c>) on every auto-recharge. This declaration added no coverage; it only destroyed
	/// the baseline.</para>
	/// </summary>
	private void ReseedWalletFromBankrollSource()
	{
		decimal bankroll = _bankrollStateService?.CurrentBalance ?? _wallet?.Balance ?? 0m;
		_wallet?.SetBalanceForTimeTravel(bankroll);
		UpdateBalanceUI();
	}

	// Fired by SimulationService after each background player bet (only while DiceGame is on screen).
	// Mini-plan 08 P1 — MEASURED at 382.7 µs per bet, 27.1% of a 1,414 µs bet, second only to the bankroll
	// disk write (four 5,000-bet windows, 2026-08-30). It fires once per settled bet, and SimulationService
	// settles up to MaxBetsPerFrame bets in a single frame — so this ran once per settled bet and
	// **only the last run's output was ever drawn.** The other nine rebuilt the blockchain status line,
	// recomputing live difficulty, reading the chain tip and counting the mempool, to paint pixels that were
	// overwritten in the same frame before anyone saw them.
	//
	// This is CLAUDE.md Pattern 6's second rule verbatim — *coalesce at the consumer when the trigger cannot
	// move the value* — and §38.7's warning that a correct event fired far too often costs more than any
	// poll in the backlog. The event is right; the subscriber was doing per-bet work that is per-FRAME work.
	//
	// The split is by what the work actually depends on, not by cost:
	//   • PER BET, kept here — the bet-history feed. Every settled bet is a distinct row; coalescing would
	//     DROP data, not merely defer a repaint. This is the part that must never be throttled.
	//   • PER FRAME, deferred — the wallet reseed, the two panel readouts, the blockchain status line and
	//     the mined-block announcement. All four are idempotent reads of current state: running them once
	//     after the frame's last bet produces exactly what running them ten times produced.
	private void OnSimBetSettled()
	{
		if (_simulationService == null) return;
		// Feed the bet-history container (it subscribes to BetExecuted), since the autobet now settles
		// inside SimulationService rather than DiceGame's local ExecuteBet. BetSettled also fires on
		// non-bet refreshes (e.g. after an auto-recharge restart), so dedupe by event reference to avoid
		// logging the same bet twice.
		BetTransactionEvent settled = _simulationService.LastSettledBetEvent;
		if (settled != null && !ReferenceEquals(settled, _lastLoggedBetEvent))
		{
			_lastLoggedBetEvent = settled;
			BetExecuted?.Invoke(GameId, settled);
		}
		// Marked HERE rather than in SimulationService because only this scene knows where the fan-out ends.
		// Legal despite living in a different file: EmitSignal dispatches synchronously, so this closes
		// inside the same bet the profiler is timing. See the segment's own note for what it is settling.
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.BetHistoryFeed);

		_betSettledUiDirty = true;
	}

	// --- Mini-plan 10 A2 — the bet display window ---
	//
	// The bet window's two states: the detailed list, or nothing. A VIEW preference, not a speed setting.
	//
	// It was built as a speed setting. When it shipped, the list cost 0.198 ms per bet against 0.040 with nothing
	// drawn (A1), so hiding it was how the player bought speed. Mini-plan 10's own fix then removed that reason:
	// the list now paints only its ~14 visible rows once per frame and measures 0.023–0.026 ms per bet, the same
	// as drawing nothing (0.025), and both states run 99 credits at the clock's ceiling. Nothing on screen may
	// promise a speed-up that is no longer there. If a later, heavier view revives the trade, measure it first.
	//
	// The containers gate themselves on TREE visibility, so this button only has to show and hide nodes; there
	// is no second flag that could drift from what is on screen. The view is a player preference (mini-plan 27):
	// each press writes it through UserSettingsService, and every entry opens on the last one chosen.
	//
	// The red/green roll NUMBERS (mini-plan 25, 17.A2). A 100-cell grid shipped here on 2026-09-19 and was withdrawn the
	// next day: A3 measured ~0.21 ms per cell repaint, and at 160 bets a frame all 100 cells change every frame, halving
	// the frame rate whatever is written. Mini-plan 23 E solved the cost by showing LESS: PreviousWinnerNumbersRow (the
	// last 10, 0.4–0.6 ms a frame at 99 bets/s) and LatestWinnerNumber (one number, ~0.02 ms), both proven in
	// BetsHistoryExplorer. They return here as two more states of the same button, and the grid is gone from this scene
	// instead of being repaired. Its geometry (y 534 + 540 min height, into the bottom band) was the open precondition,
	// and deletion meets it (Standing Convention 3). Both views take every roll into a ring and paint only while
	// visible, so a hidden one costs a push per bet.
	private enum BetDisplayMode
	{
		Detailed = 0,
		LastTen,
		LatestOnly,
		Off,
	}

	private Button _betDisplayToggleBtn;
	private Control[] _detailedWindow;
	private BetDisplayMode _betDisplayMode = BetDisplayMode.Detailed;
	private Control _numbersWindow;
	private Label _numbersTitle;
	private PreviousWinnerNumbersRow _winnersRow;
	private LatestWinnerNumber _latestWinner;

	private void BuildBetDisplayToggle()
	{
		// The detailed window is the list's scroll plus its four column headers. The headers are absolutely
		// positioned labels under UiTextLabels and are resolved BY PATH rather than reparented into a container:
		// moving an absolutely-positioned control into a container relayouts it (ProjectDesignManual Ch. 29).
		string[] detailedPaths =
		{
			"ScrollContainer2",
			"UiTextLabels/BetTimeText",
			"UiTextLabels/MultiplierText2",
			"UiTextLabels/BetAmountText",
			"UiTextLabels/ProfitAmountText",
		};

		var nodes = new List<Control>();
		foreach (string path in detailedPaths)
		{
			if (GetNodeOrNull<Control>(path) is Control node)
			{
				nodes.Add(node);
			}
		}

		_detailedWindow = nodes.ToArray();
		if (_detailedWindow.Length == 0)
		{
			return;
		}

		BuildNumbersWindow();

		// Sits in the free band between the strategy save controls (which end at y 526) and the column headers
		// (which start at y 574), directly above the window it governs.
		_betDisplayToggleBtn = new Button
		{
			Position = new Vector2(706f, 528f),
			CustomMinimumSize = new Vector2(260f, 42f),
			TooltipText = "Choose how your latest bets are shown: the detailed list, the last 10 winning numbers, only "
				+ "the latest one, or nothing. Each press moves to the next view, and a view reloads from your history "
				+ "when it opens.",
		};
		_betDisplayToggleBtn.Pressed += () =>
		{
			SetBetDisplayMode(_betDisplayMode switch
			{
				BetDisplayMode.Detailed => BetDisplayMode.LastTen,
				BetDisplayMode.LastTen => BetDisplayMode.LatestOnly,
				BetDisplayMode.LatestOnly => BetDisplayMode.Off,
				_ => BetDisplayMode.Detailed,
			});
			_userSettings?.SetDiceBetView(_betDisplayMode.ToString());
		};
		AddChild(_betDisplayToggleBtn);
		SetBetDisplayMode(UserSettingsService.TryParseEnum(_userSettings?.GetDiceBetView(), out BetDisplayMode savedView)
			? savedView
			: BetDisplayMode.Detailed);
	}

	// The Numbers views occupy the detailed window's own place: the title in the column-header band (y 574), the
	// numbers under it, everything far above the bottom band (Ch. 29 §29.11). Built in code beside the toggle, from the
	// same scene and script BetsHistoryExplorer uses. Fed from this scene's own BetExecuted, which fires for manual
	// bets and for every bet the background sim settles for the player — the same feed the bet list reads.
	private void BuildNumbersWindow()
	{
		var window = new VBoxContainer { Position = new Vector2(706f, 574f), Visible = false };
		window.AddThemeConstantOverride("separation", 8);

		_numbersTitle = new Label();
		_numbersTitle.AddThemeFontSizeOverride("font_size", 26);
		window.AddChild(_numbersTitle);

		var rowScene = GD.Load<PackedScene>("res://Screens/DiceGame/PreviousWinnerNumbersRow/PreviousWinnerNumbersRow.tscn");
		_winnersRow = rowScene?.Instantiate<PreviousWinnerNumbersRow>();
		if (_winnersRow != null)
		{
			window.AddChild(_winnersRow);
		}

		_latestWinner = new LatestWinnerNumber { Text = "-" };
		_latestWinner.AddThemeFontSizeOverride("font_size", 50);
		window.AddChild(_latestWinner);

		AddChild(window);
		_numbersWindow = window;

		BetExecuted += (_, betEvent) =>
		{
			_winnersRow?.AddWinnerNumber(betEvent.Roll, betEvent.IsWin);
			_latestWinner?.AddWinnerNumber(betEvent.Roll, betEvent.IsWin);
		};
	}

	private void SetBetDisplayMode(BetDisplayMode mode)
	{
		if (_detailedWindow == null)
		{
			return;
		}

		_betDisplayMode = mode;
		foreach (Control node in _detailedWindow)
		{
			node.Visible = mode == BetDisplayMode.Detailed;
		}

		bool numbers = mode == BetDisplayMode.LastTen || mode == BetDisplayMode.LatestOnly;
		if (_numbersWindow != null)
		{
			_numbersWindow.Visible = numbers;
		}
		if (_winnersRow != null)
		{
			_winnersRow.Visible = mode == BetDisplayMode.LastTen;
		}
		if (_latestWinner != null)
		{
			_latestWinner.Visible = mode == BetDisplayMode.LatestOnly;
		}
		if (_numbersTitle != null)
		{
			_numbersTitle.Text = mode == BetDisplayMode.LatestOnly ? "Latest Winner Number" : "Last 10 Winner Numbers";
		}

		// A view that was hidden painted nothing while it was closed, so it has no content of its own to
		// preserve: showing it rebuilds from the journal. Restoring whatever was on screen when it closed would
		// be a lie with a timestamp on it.
		if (mode == BetDisplayMode.Detailed)
		{
			_betHistoryContainer?.LoadFromHistoricalRecords(
				_userStatsService?.GetRecentBets(BetHistoryContainer.MaxRecentEntries));
		}
		else if (numbers)
		{
			IReadOnlyList<BetRecord> recent = _userStatsService?.GetRecentBets(PreviousWinnerNumbersRow.MaxRecentEntries);
			_winnersRow?.LoadFromHistoricalRecords(recent);
			_latestWinner?.LoadFromHistoricalRecords(recent);
		}

		_betDisplayToggleBtn.Text = mode switch
		{
			BetDisplayMode.Detailed => "Bet View: Detailed",
			BetDisplayMode.LastTen => "Bet View: Last 10",
			BetDisplayMode.LatestOnly => "Bet View: Latest",
			_ => "Bet View: OFF",
		};

		// So every frame-cost report says which view it was measured under (mini-plan 10 A3).
		Scripts.Diagnostics.BetUiDiagnostics.ReportView(mode.ToString());
	}

	// Set by OnSimBetSettled, consumed once per frame by _Process. A flag rather than a timer: the work is
	// idempotent and cheap once, so there is nothing to gain by deferring it past the frame that asked for
	// it — and a frame-late readout during a 9000X autobet would be visible.
	private bool _betSettledUiDirty;

	private void FlushSettledBetUiIfDirty()
	{
		if (!_betSettledUiDirty || _simulationService == null) return;
		_betSettledUiDirty = false;

		ReseedWalletFromBankrollSource();
		_strategyPanel.SetNumberOfBets(_simulationService.SessionInfinite ? 0 : _simulationService.SessionRemainingBets);
		_strategyPanel.SetBetAmount(_simulationService.SessionCurrentBet);
		// Mini-plan 15 B — the mining-status rebuild that used to happen here is GONE, not moved. It made the
		// block a second per-frame rebuild during an autobet (the sampler-gated call in _Process being the
		// first), which is half the per-frame cost this part removes and half the reason a digit froze. Nothing
		// in that block needs to be fresher than one sampler repaint, and the roll animation below still runs
		// every frame, so the "a frame-late readout at 9000X would be visible" note above does not apply to it.
		if (_simulationService.LastSettledBetEvent is BetTransactionEvent lastSettled)
		{
			ShowRoll(lastSettled.Roll, lastSettled.IsWin);
		}
	}

	// Fired by SimulationService when the background autobet stops on its own (stop condition).
	private void OnSimAutobetStopped()
	{
		NoteAutobetStopped(); // a self-stop (a stop condition, insufficient funds) arms the same guard
		_autobetDelegated = false;
		SetActiveNodeSelectorLocked(false);
		ApplyRunLock(false);
		RefreshNodeSelectorReadyDots(); // D-M2.9: the run ended — dots fall back to "has a strategy".
		ReseedWalletAcrossTransition(); // delegated run ended — this wallet may write the journal again
		_strategyPanel.SetAutoPaused(false);
		_strategyPanel.SetAutoRunning(false);
		_strategyPanel.SetManualEnabled(true);
		_simulationService?.ConsumeStopNotice();
		RefreshCalculatorFromGameSettings();
	}

	// On entering DiceGame while the background autobet is already running, bind the UI to it
	// (no new session, no rewind).
	private void BindToRunningBackgroundAutobet()
	{
		_autobetDelegated = true;
		SetActiveNodeSelectorLocked(true);
		// _Ready() stops the clock (line ~180, "start stopped"); since the background autobet is still
		// running, re-assert the clock here or it would stay frozen in every scene until app restart.
		if (_calendarTimeService != null)
		{
			_calendarTimeService.SpeedMultiplier = GameSecondsPerRealSecond;
			_calendarTimeService.IsRunning = true;
			_calendarTimeService.IsAutobetActive = true;
		}
		SimulationService.PlayerAutobetConfig cfg = _simulationService?.CurrentConfig;
		if (cfg != null)
		{
			_chanceSlider.Value = cfg.Chance;
			_highLowToggleBtn.ButtonPressed = cfg.BetHigh;
			_highLowToggleBtn.Text = cfg.BetHigh ? "HIGH" : "LOW";
			Scripts.Diagnostics.SceneEntryProbe.Mark("bind:slider+toggle");

			// D-M2.2: while a session runs, the SESSION is the truthful source — the per-node snapshot
			// is what was CONFIGURED, this is what is EXECUTING, and the two can already disagree.
			// Refill the panel from it so the displayed strategy is the one actually running, rather
			// than merely non-blank. Guarded because ApplyStrategySettings raises StrategyConfigChanged
			// (which would stop the local session and re-save the snapshot mid-bind) and
			// AutoRechargeToggled.
			if (cfg.Strategy != null)
			{
				_loadingNodeStrategy = true;
				try
				{
					_strategyPanel.ApplyStrategySettings(cfg.Strategy, cfg.NumberOfBets, cfg.AutoRecharge);
				}
				finally
				{
					_loadingNodeStrategy = false;
				}

				Scripts.Diagnostics.SceneEntryProbe.Mark("bind:applyStrategy");
				// Keep the per-node snapshot in step with what is executing, so stopping the run and
				// re-reading the panel cannot show a third, older configuration.
				SaveActiveNodeStrategySnapshot();
				Scripts.Diagnostics.SceneEntryProbe.Mark("bind:saveSnapshot");
			}
		}
		_strategyPanel.SetManualEnabled(false);
		_strategyPanel.SetAutoRunning(true);
		// Re-entering while the background run is PAUSED must show it paused — SetAutoRunning resets the
		// button to "PAUSE", so the real state has to be read back from the service (the same D-M2.2 rule
		// as the strategy fields: while a session exists, the session is the truth).
		bool paused = _simulationService?.IsPaused == true;
		_isAutoPaused = paused;
		_strategyPanel.SetAutoPaused(paused);
		Scripts.Diagnostics.SceneEntryProbe.Mark("bind:panelState");
		// The scene was rebuilt on entry with every control back at its enabled default, so the run lock
		// has to be re-asserted here — this is exactly where it was missing.
		ApplyRunLock(true);
		Scripts.Diagnostics.SceneEntryProbe.Mark("bind:runLock");
		RefreshNodeSelectorReadyDots();
		ReseedWalletAcrossTransition(); // scene rebound onto a running sim — a wholesale rebind, not steady state
		Scripts.Diagnostics.SceneEntryProbe.Mark("bind:readyDots+reseed");
	}

	private BaseBetSession CreateSession(bool isAuto)
	{
		var strategy = new ProgressiveBettingStrategy();

		// Mini-plan 05 D2: tag the owner so the lifecycle trace can tell a DiceGame-owned session from a
		// SimulationService-owned one. DiceGame keeps a local session even while the autobet is DELEGATED
		// (hypothesis H1), so which of the two is alive at a given moment is precisely the open question.
		if (isAuto)
			return new AutoBetSession(_betService, _wallet, strategy)
			{
				Owner = UserStatsService.SourceDiceGame,
				OwnerNodeId = _activeNodeId ?? ""
			};

		return new ManualBetSession(_betService, _wallet, strategy)
		{
			Owner = UserStatsService.SourceDiceGame,
			OwnerNodeId = _activeNodeId ?? ""
		};
	}

	private void OnSessionStopped(BaseBetSession session)
	{
		if (session.LastStopReason == IBettingStrategy.StopReason.InsufficientBalance &&
			_strategyPanel.AutoRechargeEnabled &&
			(_bankrollProgramService?.AutoRechargeEnabled ?? true) && // SF.1.2: service-level off-switch (D-SF.4)
			TryAutoRechargeBankroll())
		{
			if (_sessionStartBaseBet > 0m)
			{
				_strategyPanel.SetBetAmount(_sessionStartBaseBet);
			}
			StartOrRestartSession(session is AutoBetSession);
			return;
		}

		if (session is ManualBetSession)
		{
			StopAllBotRunners();
			_manualStopGate = session.LastStopReason switch
			{
				IBettingStrategy.StopReason.StopOnBlockMined => ManualStopGate.BlockMined,
				IBettingStrategy.StopReason.StopOnProfit => ManualStopGate.ProfitOrLoss,
				IBettingStrategy.StopReason.StopOnLoss => ManualStopGate.ProfitOrLoss,
				_ => ManualStopGate.None
			};
			_strategyPanel.SetManualEnabled(false);
		}

		else if (session is AutoBetSession)
		{
			if (_calendarTimeService != null)
			{
				_calendarTimeService.IsRunning = false;
				_calendarTimeService.IsAutobetActive = false;
			}
			_autoBetTimer.Stop();
			_autoBetAccumulatorGameSeconds = 0d;
			_autoBetVirtualTimestampInitialized = false;
			_autoBetLastExecutedTimestampUtc = null;
			_isAutoPaused = false;
			StopAllBotRunners();
			_strategyPanel.SetAutoPaused(false);
			_strategyPanel.SetAutoRunning(false);
			_userStatsService?.SetHighFrequencyMode(false);
		}

		RefreshCalculatorFromGameSettings();
		if (_sessionStartBaseBet > 0m)
		{
			_strategyPanel.SetBetAmount(_sessionStartBaseBet);
		}
		_userStatsService?.FlushHistory();
	}

	// Mini-plan 29 unit 4 (D-29.8) — DiceGame builds no StatusBar, so two figures that a run changes outside its own SC
	// balances get a place here: the holdings badge, and the player's BTC wallet. Both go under the navigation column
	// (OpenBetsExplorerBtn ends at y 278 and "Current app time:" starts at y 467, across x 1392–1812, with nothing
	// placed between), the badge as one more navigation button of the same width and font, since it opens a scene.
	// The left column, beside the SC balances, has no free band tall enough (DepositBtn fills y 275–337).
	private const float NavColumnX = 1392f;
	private Label _btcWalletLabel = null!;
	private bool _btcWalletRefreshQueued;

	private void BuildHoldingsAndBtcWallet()
	{
		var holdingsBadge = new UI.HoldingsBadge.HoldingsBadge
		{
			Host = "DiceGame",
			Position = new Vector2(NavColumnX, 288f),
			CustomMinimumSize = new Vector2(341f, 38f)
		};
		holdingsBadge.AddThemeFontSizeOverride("font_size", 16);
		AddChild(holdingsBadge);

		// The StatusBar's own colour and text (StatusBar.BtcWalletColor / FormatBtcWallet): money the player owns, in
		// bitcoin orange, so it reads as the same figure the bar shows in every other scene.
		_btcWalletLabel = new Label { Position = new Vector2(NavColumnX, 336f) };
		_btcWalletLabel.AddThemeFontSizeOverride("font_size", 20);
		_btcWalletLabel.AddThemeColorOverride("font_color", StatusBar.BtcWalletColor);
		AddChild(_btcWalletLabel);

		NetworkRoot.BlockAccepted += OnBlockAcceptedForBtcWallet;
		RefreshBtcWallet();
	}

	// In DiceGame the player's BTC changes only with a block (a reward, or a send confirmed); the sends themselves are
	// made in other scenes, and _Ready reads the wallet again on the way back. So BlockAccepted alone drives it, through
	// one deferred refresh: one UTXO pass per block, the cost the StatusBar already pays in every other scene. Not
	// timed here; it is the same call the bar makes.
	private void OnBlockAcceptedForBtcWallet(Block _)
	{
		if (_btcWalletRefreshQueued) return;
		_btcWalletRefreshQueued = true;
		Callable.From(RefreshBtcWallet).CallDeferred();
	}

	private void RefreshBtcWallet()
	{
		_btcWalletRefreshQueued = false;
		if (!IsInsideTree()) return;
		_btcWalletLabel.Text = StatusBar.FormatBtcWallet(NetworkRoot.GetPlayerSpendableBalanceStatic());
	}

	public override void _ExitTree()
	{
		NetworkRoot.BlockAccepted -= OnBlockAcceptedForBtcWallet; // static event — must not outlive this scene

		// A paced manual burst is mid-flight only if the player navigated away inside its ~1 second. Close it
		// here rather than letting it die with the scene: the bets already settled are owed the clock tick and
		// the bots their matching turn, and neither survives _Process going away. The bets not yet executed are
		// simply never placed, which is the same outcome as releasing the button early would have.
		if (ManualBurstActive)
		{
			FinishManualBurst();
		}

		// Stop listening to the background sim; Godot auto-disconnects on free, this is explicit + safe.
		if (_simulationService != null)
		{
			_simulationService.BetSettled -= OnSimBetSettled;
			_simulationService.AutobetStopped -= OnSimAutobetStopped;
		}
		HardwareAllocationRepository.HardwareChanged -= OnHardwareChanged;
		// If autobet is delegated to the background service, leave the clock's autobet flag alone so the
		// simulation keeps running across scenes (the service owns IsRunning/IsAutobetActive while delegated).
		if (_calendarTimeService != null && !_autobetDelegated)
			_calendarTimeService.IsAutobetActive = false;
		SaveActiveNodeStrategySnapshot();
		// If a bot is still the active node, the shared balance services hold the BOT's balances. They must
		// not escape DiceGame (every other scene — StatusBar, BankrollProgrammer, ScFinances — would read and
		// mutate them as the player's, and they self-persist). Save the bot's state, then restore the player
		// mirror onto the services. Safe here: the selector is locked while an autobet is delegated, so a
		// non-player active node implies no background session owns these services.
		if (!IsPlayerActive())
		{
			SaveActiveNodeFinancialState(false);
			_activeNodeId = PlayerNodeId;
			LoadActiveNodeFinancialState(restorePlayerFromMirror: true);
		}
		// Bots live in SimulationService now; only stop them if the player is NOT running a background
		// autobet (otherwise they must keep mining across the scene change).
		if (!_autobetDelegated)
			_simulationService?.StopBots();
		_userStatsService?.FlushHistory();
		_calendarTimeService?.PersistCurrentTime();
	}

	private void EnsureSession(bool isAuto)
	{
		if (_session != null && _session.IsRunning)
			return;

		_session = CreateSession(isAuto);
		_session.OnStopped += OnSessionStopped;

		var config = _strategyPanel.BuildConfig();
		_sessionStartBaseBet = config.BaseBet;

		_session.Start(_strategyPanel.NumberOfBets, config);
	}

	private void StartOrRestartSession(bool isAuto)
	{
		if (_session != null && _session.IsRunning)
		{
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
		}

		bool sameType =
			(isAuto && _session is AutoBetSession) ||
			(!isAuto && _session is ManualBetSession);
		if (!sameType || _session == null)
		{
			_session = CreateSession(isAuto);
			_session.OnStopped += OnSessionStopped;
		}

		var config = _strategyPanel.BuildConfig();
		_sessionStartBaseBet = config.BaseBet;
		_session.Start(_strategyPanel.NumberOfBets, config);
	}

	// Total active mining power for the difficulty regulator during MANUAL play = player rate + configured
	// bots (those that burst alongside a manual bet). Mirrors SimulationService's autobet power so manual and
	// auto produce the same difficulty. (Autobet itself sets the power from SimulationService.)
	private void SetManualMiningPower()
	{
		if (_blockchainNetworkRoot == null || !IsPlayerActive())
			return;

		double power = GetAutoBetBaseAps();
		foreach (SimulationService.BotConfig cfg in BuildBotConfigs())
			power += cfg.BetsPerSecond;
		_blockchainNetworkRoot.SetActiveMiningPower(power);
	}

	// Bots now live in SimulationService (Phase 2) so they keep mining across scene changes while the
	// player autobet is active. DiceGame just supplies the per-node strategy snapshots and delegates.
	private List<SimulationService.BotConfig> BuildBotConfigs()
	{
		var configs = new List<SimulationService.BotConfig>();
		if (_blockchainNetworkRoot == null)
		{
			return configs;
		}

		foreach (string nodeId in _blockchainNetworkRoot.GetBettableNodeIds())
		{
			if (string.Equals(nodeId, PlayerNodeId, StringComparison.Ordinal))
			{
				continue;
			}

			if (!_nodeStrategies.TryGetValue(nodeId, out NodeStrategyState strategyState) || !strategyState.IsValid)
			{
				continue;
			}

			configs.Add(new SimulationService.BotConfig
			{
				NodeId = nodeId,
				Strategy = CloneConfig(strategyState.Config),
				NumberOfBets = strategyState.NumberOfBets,
				AutoRechargeEnabled = strategyState.AutoRechargeEnabled,
				WinningChance = strategyState.WinningChance,
				BetHigh = strategyState.BetHigh,
				// Hardware-locked speed (Phase 3): the bot bets at its total hardware credits, not a free value.
				BetsPerSecond = Math.Clamp(HardwareAllocationRepository.GetNode(nodeId).TotalCredits, 1, MaxAutoBetBaseAps)
			});
		}

		return configs;
	}

	private void StartBotRunners()
	{
		if (!IsPlayerActive())
		{
			_simulationService?.StopBots();
			return;
		}

		SaveActiveNodeStrategySnapshot();
		_simulationService?.StartBots(BuildBotConfigs());
	}

	private void StopAllBotRunners()
	{
		_simulationService?.StopBots();
	}

	private void RunBotManualBurst()
	{
		if (!IsPlayerActive())
		{
			return;
		}

		SaveActiveNodeStrategySnapshot();
		_simulationService?.RunBotManualBurst(BuildBotConfigs());
	}

	private void ExecuteBet(DateTime? timestampUtc = null, bool suppressClockAdvance = false)
	{
		if (_session == null || !_session.IsRunning)
		{
			return;
		}

		// Step 14 (ND.8b.3, D-ND8.18): an open board vote in a company where the player holds NST pauses
		// ALL play — manual bets included — until the player registers a ballot (BlockExplorer Enroll
		// Mode → Details → Board Vote). SimulationService gates the delegated autobet on the same flag.
		if (NetworkRoot.IsAwaitingPlayerVote)
		{
			return;
		}

		if (_session.CurrentBet > _walletController.Balance)
		{
			_session.Stop(IBettingStrategy.StopReason.InsufficientBalance);
			return;
		}

		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;
		DateTime effectiveTimestampUtc = timestampUtc
			?? _calendarTimeService?.CurrentUtcDateTime
			?? DateTime.UtcNow;

		try
		{
			if (_session is AutoBetSession && _session.IsRunning)
			{
				_autoBetBetsSinceSample++;
			}

			var (result, betEvent, nextBet) =
				_session.ExecuteNext(chance, isHigh, effectiveTimestampUtc);

			BetExecuted?.Invoke(GameId, betEvent);
			var sigilOutcome = Scripts.Numerology.SigilBetOutcome.None;
			if (IsPlayerActive())
			{
				_userStatsService?.OnBetExecutedRegisterBet(GameId, betEvent, UserStatsService.SourceDiceGame);
				sigilOutcome = Scripts.Numerology.PlayerSigilProgress.LastOutcome;   // this bet's, registered just above
			}
			else
			{
				// ND.8f: with a bot active in the node selector, the settled bet is the BOT's play — it
				// accrues in the casino's per-client bet-stats book (player stats stay in UserStatsService).
				_casinoClientLedger ??= GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
				_casinoClientLedger?.RegisterSettledBet(_activeNodeId, betEvent.BetAmount, betEvent.CreditedProfit, betEvent.IsWin);
			}
			// Route the inverse of the client's profit to/from the casino SC bankroll, exactly as
			// SimulationService does for autobet. Manual bets do NOT go through SimulationService, so
			// without this the casino never funds/settles for manual play (with lazy first-bet funding it
			// would stay at Bankroll 0 forever). Since ND.8f (OQ-11.1 resolved) EVERY client's bet routes
			// here — player and bots alike (previously player-only). Safe from double-counting: while
			// autobet is delegated to SimulationService, ExecuteBet is inert (TickAutoBet returns early on
			// _autobetDelegated and manual betting is disabled).
			_casinoSc?.ApplyBetResult(-betEvent.CreditedProfit);
			SaveActiveNodeFinancialState(false);
			// D-M2.8: read the flag from the SESSION, not the panel. The delegated autobet already does
			// this (SimulationService._config.StopOnBlockMined, captured at start), so the same flag had
			// two different sources depending on which session owned the run — and they disagreed
			// exactly when a scene round-trip had blanked the panel.
			ProcessBlockchainAttemptForBet(
				_activeNodeId,
				_session?.SessionConfig?.StopOnBlockMined ?? false,
				_session);
			// Graphic Numerology (D-GN.10): stop between two set passes, where a chance change costs nothing. After
			// the mining attempt, so a block stop on the same bet keeps its reason; from the session (D-M2.8).
			if ((sigilOutcome & Scripts.Numerology.SigilBetOutcome.SetPassCompleted) != 0
				&& _session is { IsRunning: true }
				&& _session.SessionConfig?.StopOnSigilSetCompleted == true)
			{
				_session.Stop(IBettingStrategy.StopReason.StopOnSigilSetCompleted);
				GD.Print("[Sigil] STOP on set completed (DiceGame) — " + Scripts.Numerology.SigilRules.Describe(Scripts.Numerology.PlayerSigilProgress.State));
			}
			if (!suppressClockAdvance)
				AdvanceClockForBet();

			_strategyPanel.SetNumberOfBets(
				_session.IsInfinite ? 0 : _session.RemainingBets
			);

			_strategyPanel.SetBetAmount(nextBet);
			RefreshCalculatorFromGameSettingsThrottled();

			// Shown unconditionally. This used to return first when the session had stopped, so the roll would
			// not overwrite the "stopped" message — which meant a manual bet, whose session stops after its one
			// bet, never showed its roll at all. The stop message is gone, and so is the reason to skip.
			ShowRoll(result.Roll, result.IsWin);
		}
		catch (InvalidOperationException ex)
		{
			// Prevent unhandled exceptions from crashing the game during high-frequency autobet.
			GD.PushError($"[AutoBetError] {ex}");
			try
			{
				_session?.Stop(IBettingStrategy.StopReason.InsufficientBalance);
			}
			catch
			{
				// Ignore secondary failures.
			}
		}
		catch (Exception ex)
		{
			GD.PushError($"[AutoBetError] {ex}");
		}
	}

	private void ExecuteAutoBetOnce(double intervalGameSeconds)
	{
		_autoBetLastExecutedTimestampUtc = _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow;
		ExecuteBet(_autoBetLastExecutedTimestampUtc);
	}

	private void TickAutoBet(double realDeltaSeconds)
	{
		if (_session == null || !_session.IsRunning)
		{
			return;
		}

		if (_session is not AutoBetSession)
		{
			return;
		}

		if (_isAutoPaused)
		{
			return;
		}

		double betsPerGameSecond = GetEffectiveAutoBetsPerGameSecond();
		double targetRealPerSec = betsPerGameSecond;
		double effectiveRealDelta = Math.Max(0.0d, realDeltaSeconds);
		if (targetRealPerSec > MaxAutoBetsPerRealSecond && betsPerGameSecond > 0.0001d)
		{
			// Hard cap to prevent freezing the main thread at extreme simulated speeds.
			effectiveRealDelta *= MaxAutoBetsPerRealSecond / betsPerGameSecond;
		}
		double effectiveGameDelta = effectiveRealDelta;
		// Avoid "spiral of death": if a frame stalls, don't try to catch up an unbounded amount of game time in one tick.
		effectiveGameDelta = Math.Min(effectiveGameDelta, MaxAutoBetGameDeltaPerFrameSeconds);

		_autoBetAccumulatorGameSeconds += effectiveGameDelta;
		_autoBetAccumulatorGameSeconds = Math.Min(_autoBetAccumulatorGameSeconds, MaxAutoBetBacklogGameSeconds);

		UpdateAutoBetMeasuredRates(1.0d);
		MaybePrintAutoBetTelemetry();

		// Both the PLAYER autobet and the bots are driven by SimulationService now (so they survive scene
		// changes). DiceGame's local loop below is only a fallback and is inert while delegated.
		if (_autobetDelegated) return;

		double intervalGameSeconds = 1.0d / Math.Max(0.0001d, betsPerGameSecond);
		int executedThisFrame = 0;

		while (_autoBetAccumulatorGameSeconds >= intervalGameSeconds &&
			executedThisFrame < MaxAutoBetsPerFrame &&
			_session.IsRunning &&
			!_isAutoPaused)
		{
			_autoBetAccumulatorGameSeconds -= intervalGameSeconds;
			ExecuteAutoBetOnce(intervalGameSeconds);
			executedThisFrame++;
		}
	}

	private void UpdateAutoBetMeasuredRates(double speedMultiplier)
	{
		long now = unchecked((long)Time.GetTicksMsec());
		if (_autoBetLastRateSampleMsec == 0)
		{
			_autoBetLastRateSampleMsec = now;
			_autoBetBetsSinceSample = 0;
			return;
		}

		long elapsedMsec = now - _autoBetLastRateSampleMsec;
		if (elapsedMsec < 500)
		{
			return;
		}

		double elapsedSec = elapsedMsec / 1000.0d;
		double realPerSec = _autoBetBetsSinceSample / Math.Max(0.0001d, elapsedSec);
		_autoBetLastMeasuredRealPerSec = realPerSec;
		_autoBetLastMeasuredGamePerSec = realPerSec / Math.Max(0.0001d, speedMultiplier);

		_autoBetLastRateSampleMsec = now;
		_autoBetBetsSinceSample = 0;
	}

	private void MaybePrintAutoBetTelemetry()
	{
		long now = unchecked((long)Time.GetTicksMsec());
		if (now - _lastAutoBetTelemetryPrintMsec < 1500)
		{
			return;
		}

		_lastAutoBetTelemetryPrintMsec = now;
		if (Math.Abs(_autoBetLastMeasuredRealPerSec - _lastPrintedMeasuredRealPerSec) >= 5.0d)
		{
			_lastPrintedMeasuredRealPerSec = _autoBetLastMeasuredRealPerSec;
			GD.Print(string.Create(CultureInfo.InvariantCulture, $"[AutoBet] actual_real={_autoBetLastMeasuredRealPerSec:0.#}/s aps={GetAutoBetBaseAps()}"));
		}
	}

	private void RefreshCalculatorFromGameSettingsThrottled()
	{
		if (_session is AutoBetSession && _session.IsRunning)
		{
			double now = Time.GetTicksMsec() / 1000.0d;
			if (_lastCalculatorRefreshRealtimeSeconds >= 0d &&
				(now - _lastCalculatorRefreshRealtimeSeconds) < AutoUiCalculatorRefreshIntervalSeconds)
			{
				return;
			}

			_lastCalculatorRefreshRealtimeSeconds = now;
		}

		RefreshCalculatorFromGameSettings();
	}

	public bool IsHighFrequencyAutoMode() => false;

	// --- Deposits ---
	// SF.4.1: the inline DepositPopup is retired. The "Deposit Balance" button now opens the canonical SC-flows
	// hub (ScFinances), where the player moves SC between the Private Bank Account and Main Balance. All deposit
	// handling (balance mutation, ledger, stats) lives in PlayerBankAccountService, not here anymore.
	private void OnDepositBtnPressed()
	{
		_sceneManager?.Go(SceneManager.SceneId.ScFinances);
	}

	private void OnOpenCalculatorPressed()
	{
		// Refresh first, while the calculator is still hidden: it records the inputs and marks its table pending,
		// and Open() then builds the rows ONCE from that fresh context (mini-plan 21 E). In the old order the
		// table was built by the refresh after Open, and the lazy build would have made it twice.
		RefreshCalculatorFromGameSettings();
		_martingaleCalculator.Open();
	}

	private void OnOpenBankrollProgrammerPressed()
	{
		// In-memory only (block = the only disk commit) — see the node-switch handler above.
		SaveActiveNodeFinancialState(false);
		_calendarTimeService?.PersistCurrentTime();
		_sceneManager?.Go(SceneManager.SceneId.BankrollProgrammer);
	}

	private void OnOpenCalendarNavigatorPressed()
	{
		// The background simulation is an autoload and survives scene changes, so we navigate normally
		// (the old overlay path is obsolete and caused "trapped" back-buttons when autobet was active).
		_calendarTimeService?.PersistCurrentTime();
		_sceneManager?.Go(SceneManager.SceneId.CalendarsNavigator);
	}

	// Mini-plan 25 / Step 17.A (developer, 2026-08-18; arrival rule 2026-10-12) — a direct door to the bets explorer,
	// beside the Calendar route rather than instead of it. It ALWAYS arrives at the present: the developer's rule
	// for every entry that is not a return from the explorer itself (mini-plan 06 §9.7e, built for the Calendar by
	// mini-plan 23 B). Opening at the last selected date instead would reopen §9.7e's defect through a new door —
	// nothing re-seeds the selection on the way back to DiceGame, so it could be a game-year stale. "The present"
	// is the Calendar's own definition (CalendarsNavigator.PresentLocal): the later of the live clock and the
	// present frontier. Only the explorer's SELECTION is written, never the world clock, and the explorer still
	// arrives paused (mini-plan 04 §4.1). Its "← Back" returns here through SceneManager.PreviousScene.
	private void OnOpenBetsExplorerPressed()
	{
		// In-memory only (block = the only disk commit) — the same pair as the Block Explorer door below.
		SaveActiveNodeFinancialState(false);
		_calendarTimeService?.PersistCurrentTime();
		if (_calendarTimeService != null)
		{
			DateTime live = _calendarTimeService.CurrentLocalDateTime;
			DateTime frontier = _calendarTimeService.GamePresentLocalDateTime;
			_calendarTimeService.SetExplorerSelectedLocalDateTime(live > frontier ? live : frontier);
		}

		_sceneManager?.Go(SceneManager.SceneId.BetsHistoryExplorer);
	}

	private void OnOpenBlockExplorerPressed()
	{
		// In-memory only (block = the only disk commit) — see the node-switch handler above.
		SaveActiveNodeFinancialState(false);
		_calendarTimeService?.PersistCurrentTime();
		_sceneManager?.Go(SceneManager.SceneId.BlockExplorer);
	}

	private void OnGoToMainMenuPressed()
	{
		// In-memory only (block = the only disk commit) — see the node-switch handler above.
		SaveActiveNodeFinancialState(false);
		_calendarTimeService?.PersistCurrentTime();
		_sceneManager?.Go(SceneManager.SceneId.MainMenu);
	}

	private void OnCalculatorCloseRequested()
	{
		_martingaleCalculator.Close();
	}

	private void RefreshCalculatorFromGameSettings()
	{
		var uiConfig = _strategyPanel.BuildConfig();
		bool strategyRunning = _session != null && _session.IsRunning;
		bool hasPendingProgressionWhileStopped =
			_session != null &&
			!_session.IsRunning &&
			_session.ProgressionTriggerStreak > 0;
		bool useSessionProgressionContext = strategyRunning || hasPendingProgressionWhileStopped;
		decimal baseBet = uiConfig.BaseBet;
		if (useSessionProgressionContext)
		{
			baseBet = _session.SessionBaseBet;
		}
		decimal bankrollForCalculator = useSessionProgressionContext
			? _session.ProgressionAnchorBalance
			: _walletController.Balance;
		var config = new BettingStrategyConfig
		{
			BaseBet = baseBet,
			IncreaseOnLossPercent = uiConfig.IncreaseOnLossPercent,
			IncreaseOnWinPercent = uiConfig.IncreaseOnWinPercent,
			StopOnProfit = uiConfig.StopOnProfit,
			StopOnLoss = uiConfig.StopOnLoss,
			StopOnBlockMined = uiConfig.StopOnBlockMined,
			StopOnSigilSetCompleted = uiConfig.StopOnSigilSetCompleted,
			InsistAfterStopOnProfit = uiConfig.InsistAfterStopOnProfit,
			InsistAfterStopOnLoss = uiConfig.InsistAfterStopOnLoss
		};
		int chance = (int)_chanceSlider.Value;

		_martingaleCalculator.UpdateFromGameSettings(
			bankrollForCalculator,
			config,
			useSessionProgressionContext ? _session.CurrentBet : _strategyPanel.BetAmount,
			useSessionProgressionContext,
			strategyRunning,
			chance,
			_session?.ExecutedBetsCount ?? 0,
			_session?.ProgressionTriggerStreak ?? 0,
			_session?.SessionProfit ?? 0m
		);
	}

	// --- Handlers Intermediarios---

	private void OnBalanceDeltaChanged(Guid? sessionId, decimal amount)
	{
		SaveActiveNodeFinancialState(false);
		UpdateBalanceUI();
	}

	private void ApplyRealtimeBootstrapFromLoadedHistory()
	{
		if (_bootstrapAppliedThisSession)
		{
			return;
		}
		_bootstrapAppliedThisSession = true;

		if (_userStatsService == null)
		{
			return;
		}

		// Balance restore from bet history used to run here, but it is now provably redundant AND harmful:
		// BankrollStateService/PrincipalBalanceService hold the live balance in memory and self-persist it
		// (BankrollStateService on a 0.5 s throttle since mini-plan 08 P1 — its unthrottled per-bet write was
		// 66% of a bet; the throttle is invisible here because this reasoning depends on the IN-MEMORY value
		// and on the checkpoint, never on the file's write cadence), and
		// BlockSessionCheckpointService.ApplyCheckpointToServices() (autoload boot, before any scene loads)
		// already reverts them to the last mined block — the actual single source of truth (see
		// SimulationService's header comment). Bet history logs every bet regardless of whether a block was
		// later mined, so on a cold app restart it can be AHEAD of the checkpoint (containing bets from an
		// uncommitted period the checkpoint revert correctly discarded); restoring from it here silently
		// undid that revert on first DiceGame entry after a restart. See OQ-BP.4 in
		// player-and-casino-bankroll-programmer-plan.md.
		// SF.4B.2: FinancialBettingStats is now event-driven (ConnectTo → StatsChanged/LedgerChanged, recomputed
		// via the shared calculator). Force one recompute here now that the on-entry history rollback has settled,
		// so the panel reflects committed history immediately rather than waiting for the next event.
		_financialStats?.Refresh();
	}

	// --- UI Updates ---
	private void UpdateAllUI()
	{
		UpdateBalanceUI();
		UpdateChanceAndMultiplierUIs();
		UpdateWinnerRangeUI();
		UpdateCurrentAppTimeUI();
	}

	// cadenceSample: true only from the sampler-gated call in _Process. The other callers (scene entry, a node
	// switch, a settled manual bet) are one-off repaints that must paint immediately, and feeding them into the
	// sampler would pollute the very measurement that decides the cadence — so they render at whatever
	// resolution is currently chosen and measure nothing.
	private void UpdateCurrentAppTimeUI(bool cadenceSample = false)
	{
		DateTime local = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
		if (cadenceSample)
		{
			_readoutSampler.NoteRepaint(local);
			// Mini-plan 20 C — the bet list never shows a finer time field than this clock can.
			_betHistoryContainer?.SetMotionUnit(_readoutSampler.Unit);
		}

		_currentAppTimeValue.Text = _readoutSampler.FormatGameTime(local, "yyyy-MM-dd");
		UpdateBlockchainStatusUI(cadenceSample);
	}

	private void AdvanceClockForBet()
	{
		if (_session is AutoBetSession)
		{
			return;
		}

		_calendarTimeService?.AdvanceSeconds(GameSecondsPerManualBet);
		_calendarTimeService?.PersistCurrentTime();
	}

	private void ProcessBlockchainAttemptForBet(string minerNodeId, bool stopOnBlockMined, BaseBetSession sessionToStop)
	{
		if (_blockchainNetworkRoot is null)
		{
			return;
		}

		long gameUnixMs = new DateTimeOffset(_calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow).ToUnixTimeMilliseconds();
		// One nonce attempt per manual bet, routed by the node's hardware allocation (individual → own
		// chain; casino pool → casino chain). Mirrors the autobet path in SimulationService.RouteNonceAttempt.
		Block minedBlock;
		if (HardwareAllocationRepository.NextNonceTarget(minerNodeId) == HardwareAllocationRepository.NoncePoolTarget.Casino)
		{
			_blockchainNetworkRoot.TryCasinoNonceAttempt(out var casinoBlock, gameUnixMs);
			minedBlock = casinoBlock;
		}
		else
		{
			_blockchainNetworkRoot.TryMineSingleNonceAttempt(minerNodeId, out var ownBlock, gameUnixMs);
			minedBlock = ownBlock;
		}

		if (minedBlock is null)
		{
			return;
		}

		CaptureBlockCheckpoint();
		StopPlayerSessionOnExternalBlockMined(sessionToStop);
		if (sessionToStop != null && sessionToStop.IsRunning && stopOnBlockMined)
		{
			sessionToStop.Stop(IBettingStrategy.StopReason.StopOnBlockMined);
		}
	}

	private void StopPlayerSessionOnExternalBlockMined(BaseBetSession sessionThatMined)
	{
		// D-M2.8: the running session's own flag, not the panel's current text.
		if (_session == null ||
			!_session.IsRunning ||
			ReferenceEquals(_session, sessionThatMined) ||
			!IsPlayerActive() ||
			_session.SessionConfig?.StopOnBlockMined != true)
		{
			return;
		}

		_session.Stop(IBettingStrategy.StopReason.StopOnBlockMined);
	}

	private void OnStopOnBlockMinedDoubleClicked()
	{
		if (_manualStopGate != ManualStopGate.BlockMined)
		{
			return;
		}

		_manualStopGate = ManualStopGate.None;
		_strategyPanel.SetManualEnabled(true);
	}

	// Double-clicking either Insist toggle clears the manual-bet gate left by a profit/loss stop.
	private void OnProfitOrLossStopDoubleClicked()
	{
		if (_manualStopGate != ManualStopGate.ProfitOrLoss)
		{
			return;
		}

		_manualStopGate = ManualStopGate.None;
		_strategyPanel.SetManualEnabled(true);
	}

	private void UpdateBlockchainStatusUI(bool cadenceSample = false)
	{
		if (_blockchainStatusValue == null || _blockchainNetworkRoot == null)
		{
			return;
		}

		// Both nonce counters gain hundreds to thousands between repaints at speed. Sample the step only on a
		// cadence repaint (the one whose spacing the quantum is a statement about); a one-off repaint reuses the
		// quantum already established, so it shows the same number of digits as the repaint before it.
		long ownNonce = _blockchainNetworkRoot.GetCandidateNonce(_activeNodeId);
		long casinoNonce = _blockchainNetworkRoot.GetCasinoPoolCandidateNonce();
		long ownQuantum = cadenceSample ? _ownNonceQuantizer.NoteSample(ownNonce) : _ownNonceQuantizer.Quantum;
		long casinoQuantum = cadenceSample ? _casinoNonceQuantizer.NoteSample(casinoNonce) : _casinoNonceQuantizer.Quantum;

		BlockchainMiningAnnouncement announcement = _blockchainNetworkRoot.GetLatestMiningAnnouncement();
		string minedDetails = announcement.BlockIndex <= 0
			? "Last mined: n/a"
			: $"Last mined #{announcement.BlockIndex} | nonce {announcement.Nonce} | miner {announcement.MinerNodeId}\nHash: {announcement.BlockHash}\nMiner address: {announcement.MinerAddress}";
		// "Current nonce attempt" (in BuildMiningStatusLine) reads only the active node's OWN chain — its private
		// pool. Credits moved to the casino pool mine on the casino node's candidate instead, so that count is shown
		// on its own line, followed by how the active node's credits are split between the two pools.
		NodeHardwareState hw = HardwareAllocationRepository.GetNode(_activeNodeId);
		int totalCredits = hw.TotalCredits;
		decimal privatePct = totalCredits > 0 ? 100m * hw.IndividualPoolCredits / totalCredits : 0m;
		decimal casinoPct = totalCredits > 0 ? 100m * hw.CasinoPoolCredits / totalCredits : 0m;
		string poolLines = string.Create(CultureInfo.InvariantCulture,
			$"Current casino pool nonce attempt: {CounterQuantizer.Format(casinoNonce, casinoQuantum)}\n" +
			$"Hardware split: private {hw.IndividualPoolCredits} ({privatePct:0.0}%) | casino pool {hw.CasinoPoolCredits} ({casinoPct:0.0}%) of {totalCredits} credits");
		string ownNonceText = CounterQuantizer.Format(ownNonce, ownQuantum);
		_blockchainStatusValue.Text = $"{_blockchainNetworkRoot.BuildMiningStatusLine(_activeNodeId, ownNonceText)}\n{poolLines}\n{minedDetails}";
	}


	private void UpdateBalanceUI()
	{
		// Both figures are SC — named explicitly so the two big numbers at the top of the screen can never be
		// read as BTC now that the StatusBar carries a BTC wallet cell right above them.
		string bankrollText = string.Create(CultureInfo.InvariantCulture, $"{_walletController.Balance:F8} SC");
		decimal mainBalance = _principalBalanceService?.CurrentBalance ?? 0m;
		string balanceText = string.Create(CultureInfo.InvariantCulture, $"{mainBalance:F8} SC");
		_balanceValue.Text = bankrollText;
		_bankrollValue.Text = bankrollText;
		_principalBalanceValue.Text = balanceText;
	}

	private void UpdateChanceAndMultiplierUIs()
	{
		int chance = (int)_chanceSlider.Value;
		decimal payout = _engine.GetPayoutMultiplier(chance);

		_chanceToWinValue.Text = $"{chance}%";
		_multiplierValue.Text = string.Create(CultureInfo.InvariantCulture, $"x {payout:F4}");
	}

	private void UpdateWinnerRangeUI()
	{
		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		if (isHigh)
		{
			int min = 100 - chance;
			_winnerNumbersValue.Text =
				chance == 1
					? "99"
					: $"{min:00} to 99";
		}
		else
		{
			int max = chance - 1;
			_winnerNumbersValue.Text =
				chance == 1
					? "00"
					: $"00 to {max:00}";
		}
	}

	// Same two colours as the bet-history rows (BetHistoryItem's own exports), so a roll and the row it
	// produces agree at a glance rather than by coincidence.
	[Export] private Color _rollWinColor = Colors.Green;
	[Export] private Color _rollLossColor = Colors.Red;

	// The roll readout survives leaving and re-entering the scene, and an app restart, by reading the SAME list
	// that seeds the bet-history container — so the two can never disagree about which bet was last, and
	// nothing new is persisted for it. `GetRecentBets` returns oldest-first, so the newest record is the tail.
	//
	// After a restart that list has already been rolled back to the checkpoint boundary, which since mini-plan
	// 08 D4 is the instant of the bet that MINED the last block. The roll shown on entry is therefore that
	// bet's — the one a Stop-on-block run would have stopped on — and the history's top row is the same bet.
	private void SeedRollFromRecentBets(IReadOnlyList<BetRecord> recent)
	{
		if (recent == null || recent.Count <= 0)
		{
			return;
		}

		BetRecord newest = recent[recent.Count - 1];
		// Qualified: Scripts.Betting and Scripts.History each declare a BetOutcome, and this scene imports both.
		ShowRoll(newest.Roll, newest.Outcome == Scripts.History.BetOutcome.Win);
	}

	// The ONLY writer of ResultValue: the roll, two digits, as the winner-range readout prints it ("00 to 49"),
	// tinted by whether that roll won.
	private void ShowRoll(int roll, bool isWin)
	{
		_resultValue.Text = roll.ToString("00", CultureInfo.InvariantCulture);
		_resultValue.Modulate = isWin ? _rollWinColor : _rollLossColor;
	}

	private double GetEffectiveAutoBetsPerGameSecond()
	{
		return GetAutoBetBaseAps();
	}

	private int GetAutoBetBaseAps()
	{
		// Hardware-locked speed (Phase 3): the active node's betting rate = its total hardware credits,
		// NOT a free selection. The credits-in-use label only displays it (see RefreshHardwareDrivenSpeed);
		// the actual rate is read here straight from the hardware allocation.
		HardwareAllocationRepository.EnsureLoaded();
		int total = HardwareAllocationRepository.GetNode(_activeNodeId).TotalCredits;
		return Math.Clamp(total, 1, MaxAutoBetBaseAps);
	}

	// Shows the active node's hardware credits in use — the same clamped value the rate is read from, so the label
	// and the betting speed cannot disagree (Standing Convention 6). Called on entry, on a node switch, and on
	// every HardwareChanged (a shop purchase or the DEV ladder), so it follows a change without leaving DiceGame.
	private void RefreshHardwareDrivenSpeed()
	{
		if (_creditsInUseValue == null)
		{
			return;
		}

		_creditsInUseValue.Text = GetAutoBetBaseAps().ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	// Raised by HardwareAllocationRepository after a credit change (e.g. from the Pools & Hardware shop).
	// Re-lock the betting speed if the change affected the node we're currently showing.
	private void OnHardwareChanged(string nodeId)
	{
		if (string.Equals(nodeId, _activeNodeId, StringComparison.Ordinal))
		{
			RefreshHardwareDrivenSpeed();
		}
	}

	// Funciones auxiliares
	private bool IsBetAmountValid(decimal input)
	{
		if (input == 0m)
		{
			return false;
		}

		if (input > _walletController.Balance)
		{
			return false;
		}

		return true;
	}

	private void EnsureInitialBankrollFunded()
	{
		if (_walletController.Balance > 0m)
		{
			return;
		}

		decimal dose = _bankrollProgramService?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount;
		TryProgrammedBankrollTransfer(dose, "startup_default");
	}

	private bool TryAutoRechargeBankroll()
	{
		decimal amount = _bankrollProgramService?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount;
		// SF.1.3 fallback (D-SF3.3): if Main can't cover the dose, stream it from the player's bank reserve first.
		// No-op unless Auto-Deposit is ON and the bank holds SC — so early game (empty bank, toggle OFF) is unchanged.
		if ((_principalBalanceService?.CurrentBalance ?? 0m) < amount)
			_playerBankAccountService?.TryAutoDeposit(amount);
		return TryProgrammedBankrollTransfer(amount, "auto_recharge");
	}

	private bool TryProgrammedBankrollTransfer(decimal amount, string reason)
	{
		if (_bankrollProgramService == null || _principalBalanceService == null)
		{
			return false;
		}

		bool ok = _bankrollProgramService.TryTransferBalanceToBankroll(_principalBalanceService, _wallet, amount, reason);
		if (ok)
		{
			DateTime timestampUtc = _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow;
			if (IsPlayerActive())
			{
				_userStatsService?.RegisterDeposit(amount, _walletController.Balance, timestampUtc);
			}
			SaveActiveNodeFinancialState(false);
			UpdateBalanceUI();
		}
		return ok;
	}

	// True once the one-shot checkpoint restore opportunity has been spent for this app process. Static so it
	// survives DiceGame being freed and rebuilt on each scene change, resetting only on a real app restart.
	// The checkpoint clock/history restore is only for resuming a fresh app start; re-entering DiceGame within
	// a session must never re-run it or it rewinds the clock to the last mined block's time (the reset on
	// re-entry). The flag is marked spent on the FIRST DiceGame load regardless of whether a checkpoint
	// existed yet — otherwise a brand-new game (no checkpoint on first load, one captured moments later)
	// would rewind on its second entry.
	private static bool _checkpointRestoreSpentThisSession;

	// True once ApplyRealtimeBootstrapFromLoadedHistory() has run for this app process. Static so it survives
	// DiceGame being freed and rebuilt on each scene change (see BP.1 in player-and-casino-bankroll-programmer-plan.md).
	// The bootstrap re-reads history to seed balances on a cold app start; re-running it on later re-entries can
	// silently overwrite the authoritative BankrollStateService value with a stale historical balance.
	private static bool _bootstrapAppliedThisSession;

	private void RestoreLegacyCheckpointIfNeeded()
	{
		// Only the very first DiceGame load of the app process may restore. Mark the opportunity spent up
		// front so any later re-entry (autobet running or stopped) skips it.
		if (_checkpointRestoreSpentThisSession)
		{
			return;
		}
		_checkpointRestoreSpentThisSession = true;

		if (_blockCheckpointService == null || !_blockCheckpointService.HasCheckpoint())
		{
			return;
		}

		// Defensive: if a background autobet is somehow already live at first load, its running clock is
		// authoritative — don't rewind it to the last block's checkpoint time.
		if (_simulationService?.IsRunning == true)
		{
			return;
		}

		if (_blockchainNetworkRoot != null && _blockchainNetworkRoot.HasAnyNodeFinancialState())
		{
			RestoreCheckpointClockAndHistoryOnly();
			return;
		}

		var snapshot = _blockCheckpointService.CurrentSnapshot;
		_principalBalanceService?.SetBalance(snapshot.PrincipalBalance);
		_bankrollStateService?.SetBalance(snapshot.BankrollBalance);
		_userStatsService?.NoteBalanceDiscontinuity("checkpoint_restore");
		_wallet?.SetBalanceForTimeTravel(snapshot.BankrollBalance);
		_bankrollProgramService?.ReplaceState(snapshot.AutoRechargeAmount, snapshot.TransferRecords);

		if (snapshot.HistoryCheckpointUtcTicks.HasValue)
		{
			DateTime checkpointUtc = new DateTime(snapshot.HistoryCheckpointUtcTicks.Value, DateTimeKind.Utc);
			_userStatsService?.RollbackHistoryToUtc(checkpointUtc);
		}

		if (snapshot.CalendarLocalTicks.HasValue && _calendarTimeService != null)
		{
			DateTime local = new DateTime(snapshot.CalendarLocalTicks.Value, DateTimeKind.Local);
			_calendarTimeService.SetLocalDateTime(local);
			_calendarTimeService.SetExplorerSelectedLocalDateTime(local);
			_calendarTimeService.PersistCurrentTime();
		}

		UpdateBalanceUI();
	}

	private void RestoreCheckpointClockAndHistoryOnly()
	{
		if (_blockCheckpointService == null || !_blockCheckpointService.HasCheckpoint())
		{
			return;
		}

		var snapshot = _blockCheckpointService.CurrentSnapshot;
		if (snapshot.HistoryCheckpointUtcTicks.HasValue)
		{
			DateTime checkpointUtc = new DateTime(snapshot.HistoryCheckpointUtcTicks.Value, DateTimeKind.Utc);
			Scripts.Diagnostics.SceneEntryProbe.Mark("restore:before");
			_userStatsService?.RollbackHistoryToUtc(checkpointUtc);
			Scripts.Diagnostics.SceneEntryProbe.Mark("restore:rollbackHistory");
		}

		if (snapshot.CalendarLocalTicks.HasValue && _calendarTimeService != null)
		{
			DateTime local = new DateTime(snapshot.CalendarLocalTicks.Value, DateTimeKind.Local);
			_calendarTimeService.SetLocalDateTime(local);
			_calendarTimeService.SetExplorerSelectedLocalDateTime(local);
			_calendarTimeService.PersistCurrentTime();
			Scripts.Diagnostics.SceneEntryProbe.Mark("restore:clock");
		}
	}

	private void CaptureBlockCheckpoint()
	{
		// Mini-plan 13 A — the manual-bet twin of SimulationService.CaptureCheckpoint: the block's last phase,
		// and the call that closes its row in the block-cost trace.
		Scripts.Diagnostics.BlockCostProfiler.Enter(Scripts.Diagnostics.BlockCostProfiler.Phase.Checkpoint);
		SaveActiveNodeFinancialState(true);

		if (_blockCheckpointService == null ||
			_principalBalanceService == null ||
			_bankrollStateService == null ||
			_bankrollProgramService == null)
		{
			Scripts.Diagnostics.BlockCostProfiler.EndBlock(); // the block still happened; close its row
			return;
		}

		// A checkpoint always captures the PLAYER's financial state — the same information as every block
		// commit, no matter which node is active (identical to the background path, where the player's
		// services are captured while any miner mines). With a bot active, the shared services hold the
		// BOT's balances (the node selector rewrote them), so swap the player mirror in for the capture
		// and re-apply the bot's state after (SaveActiveNodeFinancialState above just refreshed it).
		bool botActive = !IsPlayerActive();
		string activeBotId = _activeNodeId;
		if (botActive)
		{
			_activeNodeId = PlayerNodeId;
			LoadActiveNodeFinancialState(restorePlayerFromMirror: true);
		}

		DateTime historyUtc = _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow;
		DateTime calendarLocal = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
		// Mini-plan 14 A — the manual-bet twin of SimulationService's flush: the world write lands before the
		// checkpoint that refers to it, so the chain on disk is never older than the checkpoint.
		NetworkRoot.FlushWorldIfDirty();
		_blockCheckpointService.CaptureCheckpoint(
			_principalBalanceService,
			_bankrollStateService,
			_bankrollProgramService,
			historyUtc,
			calendarLocal);

		if (botActive)
		{
			_activeNodeId = activeBotId;
			LoadActiveNodeFinancialState();
		}

		Scripts.Diagnostics.BlockCostProfiler.EndBlock();
	}
}
