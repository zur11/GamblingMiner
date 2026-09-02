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
	private const string SavedStrategiesPath = "user://saved_betting_strategies.json";
	private Label _blockchainStatusValue;
	private OptionButton _activeNodeSelector;
	private ImageTexture _readyDotTexture;
	private ImageTexture _notReadyDotTexture;
	private Button _openBlockExplorerBtn;
	private LineEdit _strategyNameInput;
	private Button _saveStrategyBtn;
	private Button _loadStrategyBtn;
	private SavedBettingStrategyRepository _savedStrategyRepository;
	private int _lastAnnouncedMinedBlockIndex;
	private ManualStopGate _manualStopGate = ManualStopGate.None;
	// STATIC so it survives DiceGame being freed and rebuilt on each scene change (mini-plan 02,
	// D-M2.1 — same reason as _checkpointRestoreSpentThisSession / _bootstrapAppliedThisSession
	// below). As an instance field this emptied on every navigation, so LoadActiveNodeStrategySnapshot
	// took its ClearStrategySettings() branch and blanked the whole panel — after which BuildConfig()
	// silently produced flat betting with both stops disarmed, BuildBotConfigs() returned an empty
	// list, and every node's ready dot went red. Process-lifetime, not persisted: an app restart
	// reverts the world to the last mined block, so a config outliving that would describe a run that
	// no longer exists.
	private static readonly Dictionary<string, NodeStrategyState> _nodeStrategies = new();
	private bool _loadingNodeStrategy;
	// Mirrors StrategyControlPanel._runLocked for the DiceGame-owned controls — see ApplyRunLock.
	private bool _runLocked;
	private SceneManager _sceneManager;
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
	private Label _resultValue;

	private Label _winnerNumbersValue;
	private Label _chanceToWinValue;
	private Label _multiplierValue;
	private Label _currentAppTimeValue;
	private OptionButton _apsSelector;

	private Slider _chanceSlider;
	private Button _highLowToggleBtn;

	private Button _depositBtn;
	private Button _openCalculatorBtn;
	private Button _openBankrollProgrammerBtn;
	private Button _openCalendarNavigatorBtn;
	private MartingaleCalculator _martingaleCalculator;

	// --- Componentes del juego ---
	[Export]
	private PreviousWinnerNumbersGrid _previousWinnerNumbersGrid;

	[Export]
	private StrategyControlPanel _strategyPanel;

	// Inicialización
	public override void _Ready()
	{
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
		_blockchainNetworkRoot = new NetworkRoot();
		_blockchainNetworkRoot.Name = "BlockchainNetworkRoot";
		AddChild(_blockchainNetworkRoot);
		_savedStrategyRepository = new SavedBettingStrategyRepository(SavedStrategiesPath);
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
		_apsSelector = GetNode<OptionButton>("%ApsSelector");
		_chanceSlider = GetNode<Slider>("%ChanceSlider");
		_highLowToggleBtn = GetNode<Button>("%HighLowToggleBtn");
		_depositBtn = GetNode<Button>("%DepositBtn");
		_openCalculatorBtn = GetNode<Button>("%OpenCalculatorBtn");
		_openBankrollProgrammerBtn = GetNode<Button>("%OpenBankrollProgrammerBtn");
		_openCalendarNavigatorBtn = GetNode<Button>("%OpenCalendarNavigatorBtn");
		_openBlockExplorerBtn = GetNode<Button>("%OpenBlockExplorerBtn");
		_activeNodeSelector = GetNode<OptionButton>("%ActiveNodeSelector");
		_strategyNameInput = GetNode<LineEdit>("%StrategyNameInput");
		_saveStrategyBtn = GetNode<Button>("%SaveStrategyBtn");
		_loadStrategyBtn = GetNode<Button>("%LoadStrategyBtn");
		_martingaleCalculator = GetNode<MartingaleCalculator>("%MartingaleCalculator");
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
		_openBlockExplorerBtn.Pressed += OnOpenBlockExplorerPressed;
		GetNode<Button>("%MainMenuBtn").Pressed += OnGoToMainMenuPressed;
		_activeNodeSelector.ItemSelected += OnActiveNodeSelected;
		_strategyNameInput.TextChanged += _ => UpdateStrategySaveLoadButtons();
		_saveStrategyBtn.Pressed += OnSaveStrategyPressed;
		_loadStrategyBtn.Pressed += OnLoadStrategyPressed;
		_martingaleCalculator.CloseRequested += OnCalculatorCloseRequested;
		_wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
		_wallet.BalanceDeltaChanged += (_, _) => _bankrollStateService?.SetBalance(_wallet.Balance);
		_previousWinnerNumbersGrid.SubscribeTo(this);
		_betHistoryContainer.SubscribeTo(this);
		_financialStats.ConnectTo(_userStatsService, _casinoClientLedger);
		_strategyPanel.BetOnceBtnPressed += OnManualBetFromPanel;
		_strategyPanel.AutoBetToggled += OnAutoBetToggled;
		_strategyPanel.AutoPauseToggled += OnAutoPauseToggled;
		_strategyPanel.BetAmountInputChanged += OnBetInputChanged;
		_strategyPanel.StrategyConfigChanged += OnStrategyConfigChanged;
		_strategyPanel.StopOnBlockMinedDoubleClicked += OnStopOnBlockMinedDoubleClicked;
		_strategyPanel.ProfitOrLossStopDoubleClicked += OnProfitOrLossStopDoubleClicked;
		_strategyPanel.AutoRechargeToggled += OnAutoRechargeToggledFromPanel;
		InitializeApsSelector();
		RefreshHardwareDrivenSpeed();
		HardwareAllocationRepository.HardwareChanged += OnHardwareChanged;
		_apsSelector.ItemSelected += _ => OnBetsPerSecondChanged(0d);

		// DEV/TEST time-acceleration selector (the ladder is DevTimeScaleSelector's own), placed next to the
		// APS selector — the two together are the `credits × DevTimeScale` throughput demand.
		var devTimeScale = new UI.DevTimeScaleSelector.DevTimeScaleSelector();
		_apsSelector.GetParent().AddChild(devTimeScale);
		_apsSelector.GetParent().MoveChild(devTimeScale, _apsSelector.GetIndex() + 1);
		_session.OnStopped += OnSessionStopped;

		_wallet.BalanceDeltaChanged += (sessionId, delta) =>
		{
			if (_walletController.Balance <= 0m)
			{
				_walletFSM.Fire(WalletEvent.BalanceZero);
			}
		};

		UpdateAllUI();
		UpdateStrategySaveLoadButtons();
		InitializeActiveNodeSelector();
		bool hadAnyNodeFinancialState = _blockchainNetworkRoot?.HasAnyNodeFinancialState() ?? false;
		RestoreLegacyCheckpointIfNeeded();
		// Must run AFTER the checkpoint restore above (it rolls bet history back to the last mined block):
		// GetLoadedHistoryStats() below reads the currently-loaded history, so calling it before the rollback
		// would compute "General P/L" / "last deposit P/L" from uncommitted bets the checkpoint just discarded.
		ApplyRealtimeBootstrapFromLoadedHistory();
		// SF.4B.6: seed the in-game bet-history list from the centralized persistent store so the most-recent
		// history reproduces on entry (before, it started empty on re-entry). Runs AFTER the checkpoint rollback
		// above so it reflects committed history; live BetExecuted events keep prepending after this.
		_betHistoryContainer?.LoadFromHistoricalRecords(
			_userStatsService?.GetRecentBets(BetHistoryContainer.MaxRecentEntries));
		LoadActiveNodeFinancialState();
		LoadActiveNodeStrategySnapshot();
		EnsureInitialBankrollFunded();
		EnsureMissingNodeFinancialStates(hadAnyNodeFinancialState);
		RefreshCalculatorFromGameSettings();
		_resultValue.Text = "Place your bet.";

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
				// The background autobet stopped while we were in another scene — surface the reason now.
				_resultValue.Text = $"Auto stopped: {_simulationService.LastAutobetStopReason}";
				_simulationService.ConsumeStopNotice();
			}
		}
	}

	private void InitializeApsSelector()
	{
		if (_apsSelector == null)
		{
			return;
		}

		_apsSelector.Clear();
		for (int aps = 1; aps <= MaxAutoBetBaseAps; aps++)
		{
			_apsSelector.AddItem($"{aps}X");
		}

		_apsSelector.Select(0); // 1X default
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
			_resultValue.Text = "Stop the autobet to change the active node.";
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
			_betHistoryContainer?.LoadFromHistoricalRecords(
				_userStatsService?.GetRecentBets(BetHistoryContainer.MaxRecentEntries));
		}
		UpdateAllUI();
		RefreshCalculatorFromGameSettings();
		_resultValue.Modulate = Colors.White;
		_resultValue.Text = $"Active node: {_activeNodeId}";
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

	public override void _Process(double delta)
	{
		UpdateCurrentAppTimeUI();
		UpdateBoardVotePauseUi();
		TickAutoBet(delta);
		// AFTER TickAutoBet, deliberately: the background sim settles this frame's bets during _Process, so
		// flushing first would paint the previous frame's state and leave this frame's stale until the next.
		FlushSettledBetUiIfDirty();
	}

	// Step 14 (ND.8b.3/D-ND8.18 follow-up): while a board vote awaits the player's ballot, BOTH betting
	// buttons (manual + AUTO) are disabled — the button-level mirror of the ExecuteBet/SimulationService
	// gates, so the player can see that time cannot be advanced until the company matter is attended.
	// Edge-triggered: the per-frame cost is one static flag read; buttons/notice update only on change.
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
		if (awaiting)
		{
			var pending = NetworkRoot.GetCompaniesAwaitingPlayerVote();
			string where = pending.Count > 0 ? pending[0].companyDisplayName : "a company you co-own";
			_resultValue.Text = $"Board vote pending at {where} — vote in Block Explorer → Enroll Mode → Details to resume play.";
		}
		else
		{
			_resultValue.Text = "Board vote attended — you may resume betting.";
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
		_resultValue.Modulate = Colors.White;
		_resultValue.Text = $"Strategy saved: {strategyName}";
		UpdateStrategySaveLoadButtons();
	}

	private void OnLoadStrategyPressed()
	{
		string strategyName = _strategyNameInput.Text.Trim();
		if (!_savedStrategyRepository.TryGet(GameId, strategyName, out SavedBettingStrategy saved))
		{
			_resultValue.Modulate = Colors.White;
			_resultValue.Text = string.IsNullOrWhiteSpace(strategyName)
				? "No saved strategy found."
				: $"Strategy not found: {strategyName}";
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
		_resultValue.Modulate = Colors.White;
		_resultValue.Text = $"Strategy loaded: {saved.Name}";
		UpdateStrategySaveLoadButtons();
	}

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
		// strategy. Reached from _ExitTree on every navigation away mid-run, and from the APS selector,
		// which is deliberately left enabled during a run.
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

		_nodeStrategies[_activeNodeId] = new NodeStrategyState
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

		RefreshNodeSelectorReadyDots();
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
			_resultValue.Text = "Invalid bet format.";
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
		int executed = 0;
		for (int i = 0; i < attempts && _session.IsRunning; i++)
		{
			if (_session.CurrentBet > _walletController.Balance)
			{
				break;
			}

			ExecuteBet(burstBaseUtc.AddSeconds(i * timePerBet), suppressClockAdvance: true);
			executed++;
		}
		// Stop-on-block must leave the clock EXACTLY at the block it stopped on (canonical rule, OQ-BP.9 /
		// OQ-CG.9), mirroring FreezeCalendarAtBlockStop in the autobet path: the block was mined at the current
		// clock, so DON'T advance one manual tick past it when the burst was halted by a mined block. A normal
		// burst advances as usual. Persist the pinned instant so calendar_state.json matches the checkpoint.
		bool stoppedOnBlock = !_session.IsRunning
			&& _session.LastStopReason == IBettingStrategy.StopReason.StopOnBlockMined;
		if (executed > 0 && !stoppedOnBlock)
			AdvanceClockForBet();
		else if (stoppedOnBlock)
			_calendarTimeService?.PersistCurrentTime();
		if (_session.IsRunning || _session.LastStopReason != IBettingStrategy.StopReason.StopOnBlockMined)
		{
			RunBotManualBurst();
		}
	}

	// --- Autobet Session
	private void OnAutoBetToggled(bool running)
	{
		// Only the player may run an autobet; bots are configured but never bet directly.
		if (running && !IsPlayerActive())
		{
			_strategyPanel.SetAutoRunning(false);
			return;
		}

		if (running)
		{
			if (!_strategyPanel.TryGetValidBet(out decimal bet))
			{
				_resultValue.Text = "Invalid bet format.";
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
		_resultValue.Text = $"Auto running | {GetAutoBetApsText()}";
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
		_resultValue.Text = paused
			? $"Auto paused | {GetAutoBetApsText()}"
			: $"Auto resumed | {GetAutoBetApsText()}";
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
		UpdateBlockchainStatusUI();
		AnnounceLatestMinedBlockIfAny();
	}

	// Fired by SimulationService when the background autobet stops on its own (stop condition).
	private void OnSimAutobetStopped()
	{
		_autobetDelegated = false;
		SetActiveNodeSelectorLocked(false);
		ApplyRunLock(false);
		RefreshNodeSelectorReadyDots(); // D-M2.9: the run ended — dots fall back to "has a strategy".
		ReseedWalletAcrossTransition(); // delegated run ended — this wallet may write the journal again
		_strategyPanel.SetAutoPaused(false);
		_strategyPanel.SetAutoRunning(false);
		_strategyPanel.SetManualEnabled(true);
		_resultValue.Text = $"Auto stopped: {_simulationService?.LastAutobetStopReason}";
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

				// Keep the per-node snapshot in step with what is executing, so stopping the run and
				// re-reading the panel cannot show a third, older configuration.
				SaveActiveNodeStrategySnapshot();
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
		// The scene was rebuilt on entry with every control back at its enabled default, so the run lock
		// has to be re-asserted here — this is exactly where it was missing.
		ApplyRunLock(true);
		RefreshNodeSelectorReadyDots();
		ReseedWalletAcrossTransition(); // scene rebound onto a running sim — a wholesale rebind, not steady state
		_resultValue.Text = "Auto running (background).";
	}

	private void OnBetsPerSecondChanged(double _)
	{
		if (!_loadingNodeStrategy)
		{
			SaveActiveNodeStrategySnapshot();
		}

		if (_session != null && _session.IsRunning && !_isAutoPaused)
		{
			// New speed takes effect immediately via TickAutoBet.
			_resultValue.Text = $"Auto running | {GetAutoBetApsText()}";
		}
	}

	// --- Handlers comunes de sesión ---
	private void HandleSessionStopped(BaseBetSession session, string prefix)
	{
		_resultValue.Text = $"{prefix}: {session.LastStopReason}";
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
			_resultValue.Text = "Bankroll recharged. Restarting progression from base bet.";
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
			HandleSessionStopped(session, "Manual stopped");
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
			HandleSessionStopped(session, "Auto stopped");
		}

		RefreshCalculatorFromGameSettings();
		if (_sessionStartBaseBet > 0m)
		{
			_strategyPanel.SetBetAmount(_sessionStartBaseBet);
		}
		_userStatsService?.FlushHistory();
	}

	public override void _ExitTree()
	{
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
			var awaiting = NetworkRoot.GetCompaniesAwaitingPlayerVote();
			string where = awaiting.Count > 0 ? awaiting[0].companyDisplayName : "a company you co-own";
			_resultValue.Text = $"Board vote pending at {where} — register your vote to resume play.";
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
			if (IsPlayerActive())
			{
				_userStatsService?.OnBetExecutedRegisterBet(GameId, betEvent, UserStatsService.SourceDiceGame);
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
			if (!suppressClockAdvance)
				AdvanceClockForBet();

			_strategyPanel.SetNumberOfBets(
				_session.IsInfinite ? 0 : _session.RemainingBets
			);

			_strategyPanel.SetBetAmount(nextBet);
			RefreshCalculatorFromGameSettingsThrottled();

			if (!_session.IsRunning)
				return;

			UpdateResultUI(result);
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

			_resultValue.Text = $"Auto error: {ex.GetType().Name}";
		}
		catch (Exception ex)
		{
			GD.PushError($"[AutoBetError] {ex}");
			_resultValue.Text = $"Auto error: {ex.GetType().Name}";
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
		_martingaleCalculator.Open();
		RefreshCalculatorFromGameSettings();
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

	private void UpdateCurrentAppTimeUI()
	{
		DateTime local = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
		_currentAppTimeValue.Text = local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		UpdateBlockchainStatusUI();
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

		AnnounceLatestMinedBlockIfAny();
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
		_resultValue.Text = "Manual re-enabled after Stop on Block.";
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
		_resultValue.Text = "Manual re-enabled after P/L stop.";
	}

	private void AnnounceLatestMinedBlockIfAny()
	{
		BlockchainMiningAnnouncement announcement = _blockchainNetworkRoot.GetLatestMiningAnnouncement();
		if (announcement.BlockIndex <= 0 || announcement.BlockIndex == _lastAnnouncedMinedBlockIndex)
		{
			return;
		}

		_lastAnnouncedMinedBlockIndex = announcement.BlockIndex;
		string streakText = announcement.CurrentMinerStreak > 1
			? $" | streak {announcement.CurrentMinerStreak} (best {announcement.BestMinerStreak})"
			: $" | best streak {announcement.BestMinerStreak}";
		_resultValue.Modulate = announcement.WasPlayer ? Colors.LimeGreen : Colors.White;
		_resultValue.Text =
			$"BLOCK #{announcement.BlockIndex} mined by {announcement.MinerNodeId} | nonce {announcement.Nonce}{streakText}";
	}

	private void UpdateBlockchainStatusUI()
	{
		if (_blockchainStatusValue == null || _blockchainNetworkRoot == null)
		{
			return;
		}

		BlockchainMiningAnnouncement announcement = _blockchainNetworkRoot.GetLatestMiningAnnouncement();
		string minedDetails = announcement.BlockIndex <= 0
			? "Last mined: n/a"
			: $"Last mined #{announcement.BlockIndex} | nonce {announcement.Nonce} | miner {announcement.MinerNodeId}\nHash: {announcement.BlockHash}\nMiner address: {announcement.MinerAddress}";
		_blockchainStatusValue.Text = $"{_blockchainNetworkRoot.BuildMiningStatusLine(_activeNodeId)}\n{minedDetails}";
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

	private void UpdateResultUI(DiceResult result)
	{
		_resultValue.Modulate = Colors.White;
		string signedProfit = Money.FormatSignedAdaptive(result.Profit);
		if (result.IsWin)
		{
			_resultValue.Text = $"WIN {signedProfit} SC - Roll: {result.Roll}{BuildAutoBetResultSuffix()}";
		}
		else
		{
			_resultValue.Text = $"LOSS {signedProfit} SC - Roll: {result.Roll}{BuildAutoBetResultSuffix()}";
		}
	}

	private string BuildAutoBetResultSuffix()
	{
		return _session is AutoBetSession && _session.IsRunning
			? $" | {GetAutoBetApsText()}"
			: string.Empty;
	}

	private string GetAutoBetApsText()
	{
		return $"APS: {GetAutoBetBaseAps()}";
	}

	private double GetEffectiveAutoBetsPerGameSecond()
	{
		return GetAutoBetBaseAps();
	}

	private int GetAutoBetBaseAps()
	{
		// Hardware-locked speed (Phase 3): the active node's betting rate = its total hardware credits,
		// NOT a free selection. The ApsSelector is kept in sync + disabled (see RefreshHardwareDrivenSpeed)
		// and is display-only — the actual rate is read here straight from the hardware allocation.
		HardwareAllocationRepository.EnsureLoaded();
		int total = HardwareAllocationRepository.GetNode(_activeNodeId).TotalCredits;
		return Math.Clamp(total, 1, MaxAutoBetBaseAps);
	}

	// Syncs the (disabled, display-only) ApsSelector to the active node's hardware-locked betting speed.
	private void RefreshHardwareDrivenSpeed()
	{
		if (_apsSelector == null || _apsSelector.ItemCount <= 0)
		{
			return;
		}

		_apsSelector.Select(GetAutoBetBaseAps() - 1);
		_apsSelector.Disabled = true;
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
			_resultValue.Text = "Bet input is empty.";
			return false;
		}

		if (input > _walletController.Balance)
		{
			_resultValue.Text = "Insufficient bankroll.";
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
			_userStatsService?.RollbackHistoryToUtc(checkpointUtc);
		}

		if (snapshot.CalendarLocalTicks.HasValue && _calendarTimeService != null)
		{
			DateTime local = new DateTime(snapshot.CalendarLocalTicks.Value, DateTimeKind.Local);
			_calendarTimeService.SetLocalDateTime(local);
			_calendarTimeService.SetExplorerSelectedLocalDateTime(local);
			_calendarTimeService.PersistCurrentTime();
		}
	}

	private void CaptureBlockCheckpoint()
	{
		SaveActiveNodeFinancialState(true);

		if (_blockCheckpointService == null ||
			_principalBalanceService == null ||
			_bankrollStateService == null ||
			_bankrollProgramService == null)
		{
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
	}
}
