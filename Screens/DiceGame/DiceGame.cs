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
	private readonly Dictionary<string, NodeStrategyState> _nodeStrategies = new();
	private bool _loadingNodeStrategy;
	private SceneManager _sceneManager;

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

	private DepositPopup _depositPopup;
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
		_userStatsService = GetNode<UserStatsService>("/root/UserStatsService");
		_bankrollStateService?.EnsureInitialized(0m);
		decimal initialBalance = _bankrollStateService?.CurrentBalance ?? 0m;
		_wallet = new Wallet(initialBalance);
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_betService = new BetService(
			_engine,
			_wallet,
			TransactionSource.Bet,
			() => DateTime.UtcNow
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
		_depositPopup = GetNode<DepositPopup>("%DepositPopup");
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
		_depositPopup.DepositConfirmed += OnDepositPopupDepositConfirmed;
		_depositPopup.DepositCanceled += OnDepositCanceled;
		_martingaleCalculator.CloseRequested += OnCalculatorCloseRequested;
		_wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
		_wallet.BalanceDeltaChanged += (_, _) => _bankrollStateService?.SetBalance(_wallet.Balance);
		_previousWinnerNumbersGrid.SubscribeTo(this);
		_betHistoryContainer.SubscribeTo(this);
		_financialStats.ConnectTo(_userStatsService);
		ApplyRealtimeBootstrapFromLoadedHistory();
		_strategyPanel.BetOnceBtnPressed += OnManualBetFromPanel;
		_strategyPanel.AutoBetToggled += OnAutoBetToggled;
		_strategyPanel.AutoPauseToggled += OnAutoPauseToggled;
		_strategyPanel.BetAmountInputChanged += OnBetInputChanged;
		_strategyPanel.StrategyConfigChanged += OnStrategyConfigChanged;
		_strategyPanel.StopOnBlockMinedDoubleClicked += OnStopOnBlockMinedDoubleClicked;
		_strategyPanel.ProfitStopModeDoubleClicked += OnProfitStopModeDoubleClicked;
		_strategyPanel.AutoRechargeToggled += _ => UpdateBalanceUI();
		InitializeApsSelector();
		_apsSelector.ItemSelected += _ => OnBetsPerSecondChanged(0d);
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
		LoadActiveNodeFinancialState();
		LoadActiveNodeStrategySnapshot();
		EnsureInitialBankrollFunded();
		EnsureMissingNodeFinancialStates(hadAnyNodeFinancialState);
		CaptureBlockCheckpointIfMissing();
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

	// Shows a green dot next to a node that has a valid, ready-to-play strategy, red otherwise.
	private void RefreshNodeSelectorReadyDots()
	{
		if (_activeNodeSelector == null)
		{
			return;
		}

		for (int index = 0; index < _activeNodeSelector.ItemCount; index++)
		{
			string nodeId = _activeNodeSelector.GetItemText(index);
			bool ready = _nodeStrategies.TryGetValue(nodeId, out NodeStrategyState state) && state.IsValid;
			_activeNodeSelector.SetItemIcon(index, GetReadyDotTexture(ready));
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
		LoadActiveNodeFinancialState();
		LoadActiveNodeStrategySnapshot();
		_betHistoryContainer?.ClearEntries();
		UpdateAllUI();
		RefreshCalculatorFromGameSettings();
		_resultValue.Modulate = Colors.White;
		_resultValue.Text = $"Active node: {_activeNodeId}";
	}

	private bool IsPlayerActive() =>
		string.Equals(_activeNodeId, PlayerNodeId, StringComparison.Ordinal);

	private void LoadActiveNodeFinancialState()
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

		_principalBalanceService?.SetBalance(state.PrincipalBalance);
		_bankrollStateService?.SetBalance(state.BankrollBalance);
		_bankrollProgramService?.ReplaceState(state.AutoRechargeAmount, state.TransferRecords);
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
		TickAutoBet(delta);
	}

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
			AutoRechargeEnabled = _strategyPanel.AutoRechargeEnabled,
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
		_strategyPanel.ApplyStrategySettings(saved.Config, saved.NumberOfBets, saved.AutoRechargeEnabled);
		_chanceSlider.Value = Math.Clamp(saved.WinningChance, 1, 95);
		_highLowToggleBtn.ButtonPressed = saved.BetHigh;
		_highLowToggleBtn.Text = saved.BetHigh ? "HIGH" : "LOW";
		ApplyAutoBetSpeedSettings(saved.BetsPerSecond);
		_loadingNodeStrategy = false;
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
		_loadStrategyBtn.Disabled = _savedStrategyRepository == null || !_savedStrategyRepository.HasAnyForGame(GameId);
	}

	private void ApplyAutoBetSpeedSettings(int betsPerSecond)
	{
		if (_apsSelector == null || _apsSelector.ItemCount <= 0)
		{
			return;
		}

		_apsSelector.Select(Math.Clamp(betsPerSecond, 1, MaxAutoBetBaseAps) - 1);
	}

	private void SaveActiveNodeStrategySnapshot()
	{
		if (_loadingNodeStrategy || string.IsNullOrWhiteSpace(_activeNodeId) || _strategyPanel == null)
		{
			return;
		}

		if (!_strategyPanel.TryGetValidBet(out decimal baseBet) || baseBet <= 0m)
		{
			return;
		}

		BettingStrategyConfig config = _strategyPanel.BuildConfig();
		if (!IsPlayerActive())
		{
			config = BuildBotStrategyConfig(config);
		}

		if (config.BaseBet <= 0m)
		{
			return;
		}

		_nodeStrategies[_activeNodeId] = new NodeStrategyState
		{
			Config = CloneConfig(config),
			NumberOfBets = _strategyPanel.NumberOfBets,
			AutoRechargeEnabled = !IsPlayerActive() || _strategyPanel.AutoRechargeEnabled,
			WinningChance = (int)_chanceSlider.Value,
			BetHigh = _highLowToggleBtn.ButtonPressed,
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
		_strategyPanel.SetBettingControlsEnabled(IsPlayerActive());

		UpdateStrategySaveLoadButtons();
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
			IncreasePercent = config.IncreasePercent,
			IncreaseOnLoss = config.IncreaseOnLoss,
			IncreaseOnWin = config.IncreaseOnWin,
			StopOnProfit = config.StopOnProfit,
			StopOnLoss = config.StopOnLoss,
			StopOnBlockMined = config.StopOnBlockMined,
			UseProgressionAnchorStops = config.UseProgressionAnchorStops,
			InsistAfterStop = config.InsistAfterStop
		};
	}

	private BettingStrategyConfig BuildBotStrategyConfig(BettingStrategyConfig config)
	{
		if (config == null)
		{
			return null;
		}

		bool allowProfitLossStops = config.InsistAfterStop;
		return new BettingStrategyConfig
		{
			BaseBet = config.BaseBet,
			IncreasePercent = config.IncreasePercent,
			IncreaseOnLoss = config.IncreaseOnLoss,
			IncreaseOnWin = config.IncreaseOnWin,
			StopOnProfit = allowProfitLossStops ? config.StopOnProfit : null,
			StopOnLoss = allowProfitLossStops ? config.StopOnLoss : null,
			StopOnBlockMined = false,
			UseProgressionAnchorStops = config.UseProgressionAnchorStops,
			InsistAfterStop = config.InsistAfterStop
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
		DateTime burstBaseUtc = _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow;
		double timePerBet = GameSecondsPerManualBet / Math.Max(1, attempts);
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
		if (executed > 0)
			AdvanceClockForBet();
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
			ReseedWalletFromBankrollSource();
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
		StartBotRunners();

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

	private void OnAutoPauseToggled(bool paused)
	{
		if (_session == null || !_session.IsRunning)
			return;

		_isAutoPaused = paused;
		_strategyPanel.SetAutoPaused(paused);

		if (paused)
		{
			if (_calendarTimeService != null)
			{
				_calendarTimeService.IsRunning = false;
			}
			_autoBetTimer.Stop();
			_resultValue.Text = $"Auto paused | {GetAutoBetApsText()}";
			return;
		}

		if (_calendarTimeService != null)
		{
			_calendarTimeService.IsRunning = true;
		}
		_resultValue.Text = $"Auto resumed | {GetAutoBetApsText()}";
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

	private void ReseedWalletFromBankrollSource()
	{
		decimal bankroll = _bankrollStateService?.CurrentBalance ?? _wallet?.Balance ?? 0m;
		_wallet?.SetBalanceForTimeTravel(bankroll);
		UpdateBalanceUI();
	}

	// Fired by SimulationService after each background player bet (only while DiceGame is on screen).
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
		ReseedWalletFromBankrollSource();
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
		}
		_strategyPanel.SetManualEnabled(false);
		_strategyPanel.SetAutoRunning(true);
		_strategyPanel.SetAutoPaused(false);
		ReseedWalletFromBankrollSource();
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

		if (isAuto)
			return new AutoBetSession(_betService, _wallet, strategy);

		return new ManualBetSession(_betService, _wallet, strategy);
	}

	private void OnSessionStopped(BaseBetSession session)
	{
		if (session.LastStopReason == IBettingStrategy.StopReason.InsufficientBalance &&
			_strategyPanel.AutoRechargeEnabled &&
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
		// If autobet is delegated to the background service, leave the clock's autobet flag alone so the
		// simulation keeps running across scenes (the service owns IsRunning/IsAutobetActive while delegated).
		if (_calendarTimeService != null && !_autobetDelegated)
			_calendarTimeService.IsAutobetActive = false;
		SaveActiveNodeStrategySnapshot();
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
				BetsPerSecond = strategyState.BetsPerSecond
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
				_userStatsService?.OnBetExecutedRegisterBet(GameId, betEvent);
			}
			SaveActiveNodeFinancialState(false);
			ProcessBlockchainAttemptForBet(_activeNodeId, _strategyPanel.StopOnBlockMinedEnabled, _session);
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
			GD.Print($"[AutoBet] actual_real={_autoBetLastMeasuredRealPerSec:0.#}/s aps={GetAutoBetBaseAps()}");
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

	// --- Depositos ---
	private void OnDepositBtnPressed()
	{
		_depositPopup.Open();
	}

	private void OnDepositPopupDepositConfirmed(double amountDouble)
	{
		decimal amount = (decimal)amountDouble;
		_principalBalanceService?.Deposit(amount);

		DateTime timestampUtc = DateTime.UtcNow;
		if (IsPlayerActive())
		{
			_userStatsService.RegisterDeposit(amount, _walletController.Balance, timestampUtc);
		}
		SaveActiveNodeFinancialState(false);

		_resultValue.Text = $"Balance principal +{amount:F8}";

		UpdateBalanceUI();

		if (_walletFSM.State == WalletState.Bankrupt)
		{
			_walletFSM.Fire(WalletEvent.BalanceRestored);
		}
	}

	private void OnDepositCanceled()
	{
		_resultValue.Text = "Deposit canceled.";
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
			IncreasePercent = uiConfig.IncreasePercent,
			IncreaseOnLoss = uiConfig.IncreaseOnLoss,
			IncreaseOnWin = uiConfig.IncreaseOnWin,
			StopOnProfit = uiConfig.StopOnProfit,
			StopOnLoss = uiConfig.StopOnLoss,
			StopOnBlockMined = uiConfig.StopOnBlockMined,
			UseProgressionAnchorStops = uiConfig.UseProgressionAnchorStops,
			InsistAfterStop = uiConfig.InsistAfterStop
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
		if (_userStatsService == null)
		{
			return;
		}

		decimal current = _walletController.Balance;
		decimal restored = _userStatsService.GetLatestKnownBalance(current);
		if (restored != current)
		{
			_wallet.SetBalanceForTimeTravel(restored);
			_bankrollStateService?.SetBalance(restored);
		}

		TimeBasedBetStats loadedStats = _userStatsService.GetLoadedHistoryStats();
		_financialStats.UpdateFromTimeBased(loadedStats);
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
		bool mined = _blockchainNetworkRoot.TryMineSingleNonceAttempt(minerNodeId, out var minedBlock, gameUnixMs);
		if (!mined || minedBlock is null)
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
		if (_session == null ||
			!_session.IsRunning ||
			ReferenceEquals(_session, sessionThatMined) ||
			!IsPlayerActive() ||
			!_strategyPanel.StopOnBlockMinedEnabled)
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

	private void OnProfitStopModeDoubleClicked()
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
		string bankrollText = _walletController.Balance.ToString("F8", CultureInfo.InvariantCulture);
		string balanceText = (_principalBalanceService?.CurrentBalance ?? 0m).ToString("F8", CultureInfo.InvariantCulture);
		_balanceValue.Text = bankrollText;
		_bankrollValue.Text = bankrollText;
		_principalBalanceValue.Text = balanceText;
	}

	private void UpdateChanceAndMultiplierUIs()
	{
		int chance = (int)_chanceSlider.Value;
		decimal payout = _engine.GetPayoutMultiplier(chance);

		_chanceToWinValue.Text = $"{chance}%";
		_multiplierValue.Text = $"x {payout:F4}";
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
			_resultValue.Text = $"WIN {signedProfit} - Roll: {result.Roll}{BuildAutoBetResultSuffix()}";
		}
		else
		{
			_resultValue.Text = $"LOSS {signedProfit} - Roll: {result.Roll}{BuildAutoBetResultSuffix()}";
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
		if (_apsSelector == null || _apsSelector.Selected < 0)
		{
			return 1;
		}

		return Math.Clamp(_apsSelector.Selected + 1, 1, MaxAutoBetBaseAps);
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

		TryProgrammedBankrollTransfer(BankrollProgramService.DefaultAutoRechargeAmount, "startup_default");
	}

	private bool TryAutoRechargeBankroll()
	{
		decimal amount = _bankrollProgramService?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount;
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
			DateTime timestampUtc = DateTime.UtcNow;
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

	private void CaptureBlockCheckpointIfMissing()
	{
		if (_blockCheckpointService == null || _blockCheckpointService.HasCheckpoint())
		{
			return;
		}

		CaptureBlockCheckpoint();
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

		DateTime historyUtc = _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow;
		DateTime calendarLocal = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
		_blockCheckpointService.CaptureCheckpoint(
			_principalBalanceService,
			_bankrollStateService,
			_bankrollProgramService,
			historyUtc,
			calendarLocal);
	}
}
