using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
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
	private const int MaxAutoBetBaseAps = 9;
	private const int MaxAutoBetApsMultiplier = 5;
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
	private const string ActiveMinerNodeId = "player";
	private const double GameSecondsPerRealSecond = 48.0d; // 10 real min -> 8 game hours
	private const double GameSecondsPerManualBet = 48.0d; // 1 manual bet tick
	private const string SavedStrategiesPath = "user://saved_betting_strategies.json";
	private Label _blockchainStatusValue;
	private Button _openBlockExplorerBtn;
	private LineEdit _strategyNameInput;
	private Button _saveStrategyBtn;
	private Button _loadStrategyBtn;
	private SavedBettingStrategyRepository _savedStrategyRepository;
	private int _lastAnnouncedMinedBlockIndex;
	private ManualStopGate _manualStopGate = ManualStopGate.None;

	private enum ManualStopGate
	{
		None,
		BlockMined,
		ProfitOrLoss
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
	private SpinBox _betsPerSecondInput;
	private OptionButton _apsMultiplierSelector;

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
		_userStatsService = GetNode<UserStatsService>("/root/UserStatsService");
		_bankrollStateService?.EnsureInitialized(0m);
		RestoreFromBlockCheckpointIfAny();
		decimal initialBalance = _bankrollStateService?.CurrentBalance ?? 0m;
		_wallet = new Wallet(initialBalance);
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_betService = new BetService(
			_engine,
			_wallet,
			TransactionSource.Bet,
			() => DateTime.UtcNow
		);
		_calendarTimeService?.EnsureGameEpochInitialized();
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
		_betsPerSecondInput = GetNode<SpinBox>("%BetsPerSecondInput");
		_apsMultiplierSelector = GetNode<OptionButton>("%ApsMultiplierSelector");
		_chanceSlider = GetNode<Slider>("%ChanceSlider");
		_highLowToggleBtn = GetNode<Button>("%HighLowToggleBtn");
		_depositPopup = GetNode<DepositPopup>("%DepositPopup");
		_depositBtn = GetNode<Button>("%DepositBtn");
		_openCalculatorBtn = GetNode<Button>("%OpenCalculatorBtn");
		_openBankrollProgrammerBtn = GetNode<Button>("%OpenBankrollProgrammerBtn");
		_openCalendarNavigatorBtn = GetNode<Button>("%OpenCalendarNavigatorBtn");
		_openBlockExplorerBtn = GetNode<Button>("%OpenBlockExplorerBtn");
		_strategyNameInput = GetNode<LineEdit>("%StrategyNameInput");
		_saveStrategyBtn = GetNode<Button>("%SaveStrategyBtn");
		_loadStrategyBtn = GetNode<Button>("%LoadStrategyBtn");
		_martingaleCalculator = GetNode<MartingaleCalculator>("%MartingaleCalculator");
		_financialStats = GetNode<FinancialBettingStats>("%FinancialBettingStats");

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
		_userStatsService.RegisterSource(this);
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
		_betsPerSecondInput.ValueChanged += OnBetsPerSecondChanged;
		_apsMultiplierSelector.ItemSelected += _ => OnBetsPerSecondChanged(0);
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
		EnsureInitialBankrollFunded();
		CaptureBlockCheckpointIfMissing();
		RefreshCalculatorFromGameSettings();
		_resultValue.Text = "Place your bet.";
		InitializeApsMultiplierSelector();
	}

	private void InitializeApsMultiplierSelector()
	{
		if (_apsMultiplierSelector == null)
		{
			return;
		}

		_apsMultiplierSelector.Clear();
		for (int mult = 1; mult <= MaxAutoBetApsMultiplier; mult++)
		{
			int index = _apsMultiplierSelector.ItemCount;
			_apsMultiplierSelector.AddItem($"x{mult}");
			_apsMultiplierSelector.SetItemMetadata(index, mult);
		}

		_apsMultiplierSelector.Select(0); // x1 default
	}

	public override void _Process(double delta)
	{
		UpdateCurrentAppTimeUI();
		TickAutoBet(delta);
	}

	// --- Eventos UI ---
	private void OnHighLowToggled()
	{
		_highLowToggleBtn.Text = _highLowToggleBtn.ButtonPressed ? "HIGH" : "LOW";
		UpdateAllUI();
	}

	private void OnChanceChanged(double _)
	{
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
		// 🔥 reset inmediato en manual
		if (_session.IsRunning)
		{
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
		}

		if (_walletFSM.State != WalletState.Bankrupt)
			_strategyPanel.SetManualEnabled(true);

		RefreshCalculatorFromGameSettings();
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
			BetsPerSecond = GetAutoBetBaseAps(),
			BetsPerSecondMultiplier = GetAutoBetApsMultiplier()
		});

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
		_strategyPanel.ApplyStrategySettings(saved.Config, saved.NumberOfBets, saved.AutoRechargeEnabled);
		_chanceSlider.Value = Math.Clamp(saved.WinningChance, 1, 95);
		_highLowToggleBtn.ButtonPressed = saved.BetHigh;
		_highLowToggleBtn.Text = saved.BetHigh ? "HIGH" : "LOW";
		ApplyAutoBetSpeedSettings(saved.BetsPerSecond, saved.BetsPerSecondMultiplier);
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

	private void ApplyAutoBetSpeedSettings(int betsPerSecond, int multiplier)
	{
		if (_betsPerSecondInput != null)
		{
			_betsPerSecondInput.Value = Math.Clamp(betsPerSecond, 1, MaxAutoBetBaseAps);
		}

		if (_apsMultiplierSelector == null || _apsMultiplierSelector.ItemCount <= 0)
		{
			return;
		}

		int clampedMultiplier = Math.Clamp(multiplier, 1, MaxAutoBetApsMultiplier);
		for (int index = 0; index < _apsMultiplierSelector.ItemCount; index++)
		{
			Variant meta = _apsMultiplierSelector.GetItemMetadata(index);
			if (TryReadMultiplierMetadata(meta, out int itemMultiplier) && itemMultiplier == clampedMultiplier)
			{
				_apsMultiplierSelector.Select(index);
				return;
			}
		}
	}

	// --- Manual Bet Session
	private void OnManualBetFromPanel()
	{
		if (!_strategyPanel.TryGetValidBet(out _))
		{
			_resultValue.Text = "Invalid bet format.";
			return;
		}

		if (!IsBetAmountValid(_strategyPanel.BetAmount))
			return;

		EnsureSession(false); // 🔥 manual

		ExecuteBet();
	}

	// --- Autobet Session
	private void OnAutoBetToggled(bool running)
	{
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
			if (_calendarTimeService != null)
			{
				_calendarTimeService.IsRunning = false;
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
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
			RefreshCalculatorFromGameSettings();
			return;
		}

		_userStatsService?.SetHighFrequencyMode(true);
		if (_calendarTimeService != null)
		{
			_calendarTimeService.SpeedMultiplier = GameSecondsPerRealSecond;
			_calendarTimeService.IsRunning = true;
		}
		StartOrRestartSession(true);

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
		GD.Print($"[AutoBet] Start aps_base={GetAutoBetBaseAps()} mult=x{GetAutoBetApsMultiplier()} effective_game_aps={GetEffectiveAutoBetsPerGameSecond()}");
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

	private void OnBetsPerSecondChanged(double _)
	{
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
			}
			_autoBetTimer.Stop();
			_autoBetAccumulatorGameSeconds = 0d;
			_autoBetVirtualTimestampInitialized = false;
			_autoBetLastExecutedTimestampUtc = null;
			_isAutoPaused = false;
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

	private void ExecuteBet(DateTime? timestampUtc = null)
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
			ProcessBlockchainAttemptForBet();
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
			GD.Print($"[AutoBet] actual_real={_autoBetLastMeasuredRealPerSec:0.#}/s aps_base={GetAutoBetBaseAps()} mult=x{GetAutoBetApsMultiplier()} effective_game_aps={GetEffectiveAutoBetsPerGameSecond()}");
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

	public bool IsHighFrequencyAutoMode()
	{
		return _session is AutoBetSession &&
			_session.IsRunning &&
			(_betsPerSecondInput != null && (int)Math.Round(_betsPerSecondInput.Value) >= 10);
	}

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
		_userStatsService.RegisterDeposit(amount, _walletController.Balance, timestampUtc);

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
		_calendarTimeService?.PersistCurrentTime();
		GetTree().ChangeSceneToFile("res://Screens/BankrollProgrammer/BankrollProgrammer.tscn");
	}

	private void OnOpenCalendarNavigatorPressed()
	{
		GetTree().ChangeSceneToFile("res://Screens/CalendarsNavigator/CalendarsNavigator.tscn");
	}

	private void OnOpenBlockExplorerPressed()
	{
		_calendarTimeService?.PersistCurrentTime();
		GetTree().ChangeSceneToFile("res://Screens/BlockExplorer/BlockExplorer.tscn");
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

	private void ProcessBlockchainAttemptForBet()
	{
		if (_blockchainNetworkRoot is null)
		{
			return;
		}

		long gameUnixMs = new DateTimeOffset(_calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow).ToUnixTimeMilliseconds();
		bool mined = _blockchainNetworkRoot.TryMineSingleNonceAttempt(ActiveMinerNodeId, out var minedBlock, gameUnixMs);
		if (!mined || minedBlock is null)
		{
			return;
		}

		AnnounceLatestMinedBlockIfAny();
		CaptureBlockCheckpoint();
		if (_session != null && _session.IsRunning && _strategyPanel.StopOnBlockMinedEnabled)
		{
			_session.Stop(IBettingStrategy.StopReason.StopOnBlockMined);
		}
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
		_blockchainStatusValue.Text = $"{_blockchainNetworkRoot.BuildMiningStatusLine(ActiveMinerNodeId)}\n{minedDetails}";
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
		int baseAps = GetAutoBetBaseAps();
		int multiplier = GetAutoBetApsMultiplier();
		double effective = GetEffectiveAutoBetsPerGameSecond();
		return multiplier == 1
			? $"APS: {baseAps}"
			: $"APS: {baseAps} x{multiplier} (= {effective:0.##})";
	}

	private double GetEffectiveAutoBetsPerGameSecond()
	{
		int baseAps = GetAutoBetBaseAps();
		int multiplier = GetAutoBetApsMultiplier();
		return baseAps * multiplier;
	}

	private int GetAutoBetBaseAps()
	{
		if (_betsPerSecondInput == null)
		{
			return 1;
		}

		return Math.Clamp((int)Math.Round(_betsPerSecondInput.Value), 1, MaxAutoBetBaseAps);
	}

	private int GetAutoBetApsMultiplier()
	{
		if (_apsMultiplierSelector == null)
		{
			return 1;
		}

		Variant meta = _apsMultiplierSelector.GetItemMetadata(_apsMultiplierSelector.Selected);
		if (TryReadMultiplierMetadata(meta, out int multiplier))
		{
			return Math.Clamp(multiplier, 1, MaxAutoBetApsMultiplier);
		}

		return 1;
	}

	private bool TryReadMultiplierMetadata(Variant meta, out int multiplier)
	{
		multiplier = 1;
		if (meta.VariantType == Variant.Type.Int)
		{
			multiplier = meta.AsInt32();
			return true;
		}

		if (meta.VariantType == Variant.Type.Float)
		{
			multiplier = (int)Math.Round(meta.AsDouble());
			return true;
		}

		return false;
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
			_userStatsService?.RegisterDeposit(amount, _walletController.Balance, timestampUtc);
			UpdateBalanceUI();
		}
		return ok;
	}

	private void RestoreFromBlockCheckpointIfAny()
	{
		if (_blockCheckpointService == null || !_blockCheckpointService.HasCheckpoint())
		{
			return;
		}

		var snapshot = _blockCheckpointService.CurrentSnapshot;
		_principalBalanceService?.SetBalance(snapshot.PrincipalBalance);
		_bankrollStateService?.SetBalance(snapshot.BankrollBalance);
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
