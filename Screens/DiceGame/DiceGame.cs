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
	private FinancialBettingStats _financialStats;
	private Timer _autoBetTimer;
	private BaseBetSession _session;
	private bool _isAutoPaused;
	private long _lastAppliedTimelineSecond = long.MinValue;

	// Componentes UI
	[Export]
	private BetHistoryContainer _betHistoryContainer;

	// --- State Machines ---
	private WalletStateMachine _walletFSM;

	// --- Nodos UI ---
	private Label _balanceValue;
	private Label _resultValue;

	private Label _winnerNumbersValue;
	private Label _chanceToWinValue;
	private Label _multiplierValue;
	private Label _currentAppTimeValue;
	private SpinBox _betsPerSecondInput;

	private Slider _chanceSlider;
	private Button _highLowToggleBtn;

	private DepositPopup _depositPopup;
	private Button _depositBtn;
	private Button _openCalculatorBtn;
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
		_bankrollStateService?.EnsureInitialized(1.00000000m);
		decimal initialBalance = _bankrollStateService?.CurrentBalance ?? 1.00000000m;
		_wallet = new Wallet(initialBalance);
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_betService = new BetService(
			_engine,
			_wallet,
			TransactionSource.Bet,
			() => _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow
		);
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
		_resultValue = GetNode<Label>("%ResultValue");
		_winnerNumbersValue = GetNode<Label>("%WinnerNumbersValue");
		_chanceToWinValue = GetNode<Label>("%ChanceToWinValue");
		_multiplierValue = GetNode<Label>("%MultiplierValue");
		_currentAppTimeValue = GetNode<Label>("%CurrentAppTimeValue");
		_betsPerSecondInput = GetNode<SpinBox>("%BetsPerSecondInput");
		_chanceSlider = GetNode<Slider>("%ChanceSlider");
		_highLowToggleBtn = GetNode<Button>("%HighLowToggleBtn");
		_depositPopup = GetNode<DepositPopup>("%DepositPopup");
		_depositBtn = GetNode<Button>("%DepositBtn");
		_openCalculatorBtn = GetNode<Button>("%OpenCalculatorBtn");
		_openCalendarNavigatorBtn = GetNode<Button>("%OpenCalendarNavigatorBtn");
		_martingaleCalculator = GetNode<MartingaleCalculator>("%MartingaleCalculator");
		_userStatsService = GetNode<UserStatsService>("/root/UserStatsService");
		_financialStats = GetNode<FinancialBettingStats>("%FinancialBettingStats");

		// Configurar etiqueta de High/Low toggle Btn 
		_highLowToggleBtn.Text = "LOW";

		// Conectar señales
		_highLowToggleBtn.Pressed += OnHighLowToggled;
		_chanceSlider.ValueChanged += OnChanceChanged;
		_depositBtn.Pressed += OnDepositBtnPressed;
		_openCalculatorBtn.Pressed += OnOpenCalculatorPressed;
		_openCalendarNavigatorBtn.Pressed += OnOpenCalendarNavigatorPressed;
		_depositPopup.DepositConfirmed += OnDepositPopupDepositConfirmed;
		_depositPopup.DepositCanceled += OnDepositCanceled;
		_martingaleCalculator.CloseRequested += OnCalculatorCloseRequested;
		_wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
		_wallet.BalanceDeltaChanged += (_, _) => _bankrollStateService?.SetBalance(_wallet.Balance);
		_previousWinnerNumbersGrid.SubscribeTo(this);
		_betHistoryContainer.SubscribeTo(this);
		_userStatsService.RegisterSource(this);
		_financialStats.ConnectTo(_userStatsService);
		_strategyPanel.BetOnceBtnPressed += OnManualBetFromPanel;
		_strategyPanel.AutoBetToggled += OnAutoBetToggled;
		_strategyPanel.AutoPauseToggled += OnAutoPauseToggled;
		_strategyPanel.BetAmountInputChanged += OnBetInputChanged;
		_autoBetTimer.Timeout += OnAutoBetTimerTimeout;
		_strategyPanel.StrategyConfigChanged += OnStrategyConfigChanged;
		_betsPerSecondInput.ValueChanged += OnBetsPerSecondChanged;
		_session.OnStopped += OnSessionStopped;

		_wallet.BalanceDeltaChanged += (sessionId, delta) =>
		{
			if (_walletController.Balance <= 0m)
			{
				_walletFSM.Fire(WalletEvent.BalanceZero);
			}
		};

		UpdateAllUI();
		RefreshCalculatorFromGameSettings();
		_resultValue.Text = "Place your bet.";
	}

	public override void _Process(double delta)
	{
		UpdateCurrentAppTimeUI();
		ApplyTemporalStateFromCalendarIfNeeded();
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
			return;
		}

		if (newText == "MIN")
		{
			decimal minBet = 0.00000001m;
			_strategyPanel.ManualSetBetAmount(minBet);
			return;
		}

		RefreshCalculatorFromGameSettings();
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
			_autoBetTimer.Stop();
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
			RefreshCalculatorFromGameSettings();
			return;
		}

		EnsureSession(true);

		StartAutoBetTimerWithCurrentSpeed();
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
			_autoBetTimer.Stop();
			_resultValue.Text = $"Auto paused | {GetAutoBetApsText()}";
			return;
		}

		StartAutoBetTimerWithCurrentSpeed();
		_resultValue.Text = $"Auto resumed | {GetAutoBetApsText()}";
	}
	private void OnAutoBetTimerTimeout()
	{
		if (_isAutoPaused)
			return;

		ExecuteBet();

		if (_session.IsRunning)
			StartAutoBetTimerWithCurrentSpeed();
	}

	private void OnBetsPerSecondChanged(double _)
	{
		if (_session != null && _session.IsRunning && !_isAutoPaused)
		{
			StartAutoBetTimerWithCurrentSpeed();
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
		if (session is ManualBetSession)
		{
			_strategyPanel.SetManualEnabled(false);
			HandleSessionStopped(session, "Manual stopped");
		}

		else if (session is AutoBetSession)
		{
			_autoBetTimer.Stop();
			_isAutoPaused = false;
			_strategyPanel.SetAutoPaused(false);
			_strategyPanel.SetAutoRunning(false);
			HandleSessionStopped(session, "Auto stopped");
		}

		RefreshCalculatorFromGameSettings();
	}

	private void EnsureSession(bool isAuto)
	{
		if (_session != null && _session.IsRunning)
			return;

		_session = CreateSession(isAuto);
		_session.OnStopped += OnSessionStopped;

		var config = _strategyPanel.BuildConfig();

		_session.Start(_strategyPanel.NumberOfBets, config);
	}

	private void ExecuteBet()
	{
		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		var (result, betEvent, nextBet) =
			_session.ExecuteNext(chance, isHigh);

		BetExecuted?.Invoke(GameId, betEvent);

		_strategyPanel.SetNumberOfBets(
			_session.IsInfinite ? 0 : _session.RemainingBets
		);

		_strategyPanel.SetBetAmount(nextBet);
		RefreshCalculatorFromGameSettings();

		if (!_session.IsRunning)
			return;

		UpdateResultUI(result);
	}

	// --- Depositos ---
	private void OnDepositBtnPressed()
	{
		_depositPopup.Open();
	}

	private void OnDepositPopupDepositConfirmed(double amountDouble)
	{
		decimal amount = (decimal)amountDouble;

		_walletController.Deposit(amount);

		DateTime timestampUtc = _calendarTimeService?.CurrentUtcDateTime ?? DateTime.UtcNow;
		_userStatsService.RegisterDeposit(amount, _walletController.Balance, timestampUtc);

		_resultValue.Text = $"Deposited {amount:F8}";

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

	private void OnOpenCalendarNavigatorPressed()
	{
		GetTree().ChangeSceneToFile("res://Screens/CalendarsNavigator/CalendarsNavigator.tscn");
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
			StopOnLoss = uiConfig.StopOnLoss
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
	}

	private void ApplyTemporalStateFromCalendarIfNeeded()
	{
		if (_userStatsService == null || _calendarTimeService == null)
		{
			return;
		}

		DateTime local = _calendarTimeService.CurrentLocalDateTime;
		long currentSecond = new DateTimeOffset(local).ToUnixTimeSeconds();
		if (currentSecond == _lastAppliedTimelineSecond)
		{
			return;
		}

		_lastAppliedTimelineSecond = currentSecond;

		decimal reconstructedBalance = _userStatsService.GetBalanceAtOrBefore(local, TimeZoneInfo.Local);
		_wallet.SetBalanceForTimeTravel(reconstructedBalance);
		_bankrollStateService?.SetBalance(reconstructedBalance);
		UpdateBalanceUI();

		TimeBasedBetStats statsAtTime = _userStatsService.GetStatsUpTo(local, TimeZoneInfo.Local);
		_financialStats.UpdateFromTimeBased(statsAtTime);
	}

	private void UpdateBalanceUI()
	{
		_balanceValue.Text =
			_walletController.Balance.ToString("F8", CultureInfo.InvariantCulture);
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
		if (result.IsWin)
		{
			_resultValue.Text = $"WIN - Roll: {result.Roll}{BuildAutoBetResultSuffix()}";
		}
		else
		{
			_resultValue.Text = $"LOSS - Roll: {result.Roll}{BuildAutoBetResultSuffix()}";
		}
	}

	private void StartAutoBetTimerWithCurrentSpeed()
	{
		double effectiveBetsPerSecond = GetEffectiveAutoBetsPerSecond();
		double intervalSeconds = 1.0d / effectiveBetsPerSecond;
		_autoBetTimer.Start(intervalSeconds);
	}

	private string BuildAutoBetResultSuffix()
	{
		return _session is AutoBetSession && _session.IsRunning
			? $" | {GetAutoBetApsText()}"
			: string.Empty;
	}

	private string GetAutoBetApsText()
	{
		int betsPerSecond = Math.Clamp((int)Math.Round(_betsPerSecondInput.Value), 1, 100);
		double effectiveBetsPerSecond = GetEffectiveAutoBetsPerSecond();
		return $"APS: {betsPerSecond} (effective: {effectiveBetsPerSecond:0.##}/s)";
	}

	private double GetEffectiveAutoBetsPerSecond()
	{
		int betsPerSecond = Math.Clamp((int)Math.Round(_betsPerSecondInput.Value), 1, 100);
		double timeSpeed = _calendarTimeService?.SpeedMultiplier ?? 1.0d;
		double normalizedSpeed = Math.Max(0.0001d, timeSpeed);
		return betsPerSecond * normalizedSpeed;
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
			_resultValue.Text = "Insufficient balance.";
			return false;
		}

		return true;
	}
}
