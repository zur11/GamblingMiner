using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Scripts.Dice;
using Scripts.GameState;
using Scripts.Finance;
using Scripts.Game;
using Scripts.Betting;
using GameComponents.StrategyControlPanel;

public partial class DiceGame : Control, IBetEventSource
{
	// --- Eventos ---
	public event Action<string, BetTransactionEvent> BetExecuted;
	public event Action BankruptDetected;

	// --- Propiedades ---
	public string GameId => "Dice";

	// --- State Machine ---
	private GameStateMachine _fsm;

	// --- Finanzas ---
	private Wallet _wallet;

	// --- Servicio de apuestas ---
	private BetService _betService;
	private UserStatsService _userStatsService;
	private FinancialBettingStats _financialStats;
	private AutoBetSession _autoBetSession;
	private Timer _autoBetTimer;

	[Export]
	private BetHistoryContainer _betHistoryContainer;

	private DiceEngine _engine;

	// --- Nodos UI ---
	private Label _balanceValue;
	private Label _resultValue;

	private Label _winnerNumbersValue;
	private Label _chanceToWinValue;
	private Label _multiplierValue;

	private Slider _chanceSlider;
	private Button _highLowToggleBtn;

	private DepositPopup _depositPopup;
	private Button _depositBtn;

	// --- Variables para aumento progresivo de apuesta ---
	private decimal _baseBet = 0m;
	private decimal _currentBet = 0m;
	private decimal _increasePercent = 0m;

	private bool _userModifiedBase = false;

	[Export]
	private PreviousWinnerNumbersGrid _previousWinnerNumbersGrid;

	// --- Componentes del juego ---
	[Export]
	private StrategyControlPanel _strategyPanel;

	// --- Validación decimal ---
	private static readonly Regex BetRegex =
		new Regex(@"^\d+(\.\d{1,8})?$", RegexOptions.Compiled);

	public override void _Ready()
	{
		// Inicializar motor y servicios
		_engine = new DiceEngine();
		_wallet = new Wallet(1.00000000m);
		_betService = new BetService(_engine, _wallet, TransactionSource.Bet);
		var strategy = new ProgressiveBettingStrategy();

		_autoBetSession = new AutoBetSession(strategy);

		_autoBetTimer = new Timer();
		_autoBetTimer.WaitTime = 1.0; // 1 segundo
		_autoBetTimer.OneShot = true;

		AddChild(_autoBetTimer);


		// Obtener nodos
		_balanceValue = GetNode<Label>("%BalanceValue");
		_resultValue = GetNode<Label>("%ResultValue");
		_winnerNumbersValue = GetNode<Label>("%WinnerNumbersValue");
		_chanceToWinValue = GetNode<Label>("%ChanceToWinValue");
		_multiplierValue = GetNode<Label>("%MultiplierValue");
		_chanceSlider = GetNode<Slider>("%ChanceSlider");
		_highLowToggleBtn = GetNode<Button>("%HighLowToggleBtn");
		_depositPopup = GetNode<DepositPopup>("%DepositPopup");
		_depositBtn = GetNode<Button>("%DepositBtn");
		_userStatsService = GetNode<UserStatsService>("/root/UserStatsService");
		_financialStats = GetNode<FinancialBettingStats>("%FinancialBettingStats");

		// Configurar toggle
		_highLowToggleBtn.ToggleMode = true;
		_highLowToggleBtn.ButtonPressed = false;
		_highLowToggleBtn.Text = "LOW";

		// Conectar señales
		_fsm = new GameStateMachine();
		_fsm.StateEntered += OnStateEntered;
		_fsm.StateExited += OnStateExited;
		_highLowToggleBtn.Pressed += OnHighLowToggled;
		_chanceSlider.ValueChanged += OnChanceChanged;
		_depositBtn.Pressed += OnDepositBtnPressed;
		_depositPopup.DepositConfirmed += OnDepositPopupDepositConfirmed;
		_depositPopup.DepositCanceled += OnDepositCanceled;
		_fsm.OnTransition += LogTransition;
		_wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
		_previousWinnerNumbersGrid.SubscribeTo(this);
		_betHistoryContainer.SubscribeTo(this);
		_userStatsService.RegisterSource(this);
		_financialStats.ConnectTo(_userStatsService);
		_strategyPanel.BetOnceBtnPressed += OnManualBetFromPanel;
		_strategyPanel.AutoBetToggled += OnAutoBetToggled;
		_strategyPanel.BetAmountInputChanged += OnBetInputChanged;
		_autoBetSession.SubscribeToBalanceChanged(_wallet);
		_autoBetSession.SessionStopped += OnAutoBetSessionStopped;
		_autoBetTimer.Timeout += OnAutoBetTimerTimeout;

		UpdateAllUI();
		_resultValue.Text = "Place your bet.";
	}

	// --- API ---
	public decimal GetCurrentBalance()
	{
		return _wallet.Balance;
	}

	public (DiceResult result, BetTransactionEvent betEvent)
	ExecuteAutoBet(decimal amount, Guid sessionId)
	{
		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		return _betService.ExecuteBet(amount, chance, isHigh, sessionId);
	}

	public Wallet GetWallet()
	{
		return _wallet;
	}

	// State Machine Methods
	private void OnStateEntered(BetState state)
	{
		switch (state)
		{
			case BetState.Bankrupt:
				_resultValue.Text = "Bankrupt. Deposit required.";
				//_betBtn.Disabled = true;
				break;
		}
	}

	private void OnStateExited(BetState state)
	{
		if (state == BetState.Bankrupt)
		{
			//_betBtn.Disabled = false;
		}
	}

	private void HandleIdleBet(decimal baseBet, decimal increasePercent)
	{
		_baseBet = baseBet;
		_currentBet = baseBet;
		_increasePercent = increasePercent;

		if (_increasePercent > 0m && _strategyPanel.IncreasingOnWin)
		{
			_fsm.Fire(GameEvent.StartWinProgression);
		}
		if (_increasePercent > 0m && !_strategyPanel.IncreasingOnWin)
		{
			_fsm.Fire(GameEvent.StartLossProgression);
		}
	}

	private void HandleWin()
	{
		if (_increasePercent > 0m && _strategyPanel.IncreasingOnWin) 
		{ 
			decimal multiplier = 1m + (_increasePercent / 100m);
			_currentBet *= multiplier;
		}
		else 
		{
			_currentBet = _baseBet;
		}
		_fsm.Fire(GameEvent.Win);
	}

	private void HandleLoss()
	{
		if (_increasePercent > 0m && !_strategyPanel.IncreasingOnWin)
		{
			decimal multiplier = 1m + (_increasePercent / 100m);
			_currentBet *= multiplier;
		}
		else
		{
			_currentBet = _baseBet;
		}
		_fsm.Fire(GameEvent.Loss);
	}

	private void HandleProgressionAborted()
	{
		_currentBet = 0m;
		_resultValue.Text = "Bet exceeds balance. ProgressionOnLoss stopped.";

		_fsm.Fire(GameEvent.ProgressionAborted);
	}

	private void LogTransition(BetState from, GameEvent trigger, BetState to)
	{
		GD.Print($"[FSM] {from} --({trigger})--> {to}");
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
	}

	private void OnBetInputChanged(string newText)
	{
		if (_fsm.CurrentState == BetState.ProgressionOnLoss ||
			 _fsm.CurrentState == BetState.ProgressionOnWin)
		{
			_currentBet = 0m;
			_baseBet = 0m;
			_increasePercent = 0m;

			_fsm.Fire(GameEvent.ManualReset);

			_resultValue.Text = "Progression manually reset.";
		}

		if (newText == "MAX")
		{
			decimal maxBet = _wallet.Balance;
			_strategyPanel.ManualSetBetAmount(maxBet);
			return;
		}

		if (newText == "MIN")
		{
			decimal minBet = 0.00000001m;
			_strategyPanel.ManualSetBetAmount(minBet);
			return;
		}
	}

	// --- Eventos de componentes ---
	private void OnManualBetFromPanel()
	{
		if (_fsm.CurrentState == BetState.Idle)
		{
			decimal baseBet = _strategyPanel.BetAmount;
			decimal increasePercent = _strategyPanel.IncreasePercent;

			if (baseBet <= 0m) return;

			HandleIdleBet(baseBet, increasePercent);
		}

		ExecuteCurrentStateBet();
	}

	// --- Autobet Session
	private void OnAutoBetToggled(bool running)
	{
		if (!running)
		{
			_autoBetSession.Stop();
			_autoBetTimer.Stop();
			return;
		}

		var config = _strategyPanel.BuildConfig();

		_autoBetSession.Configure(config);
		_autoBetSession.SetBetCount(_strategyPanel.NumberOfBets);

		_autoBetSession.Start(_wallet.Balance);

		_autoBetTimer.Start();
	}

	private void OnAutoBetSessionStopped(Guid id,
	IBettingStrategy.StopReason? reason)
	{
		GD.Print($"AutoBet stopped: {reason}");
	}

	// --- Depositos ---
	private void OnDepositBtnPressed()
	{
		_depositPopup.Open();
	}

	private void OnAutoBetTimerTimeout()
	{
		ExecuteNextAutoBet();
	}

	private void OnDepositPopupDepositConfirmed(double amountDouble)
	{
		decimal amount = (decimal)amountDouble;

		var transaction = new Transaction(
			TransactionType.Deposit,
			TransactionSource.External,
			null,
			amount);

		_wallet.ApplyTransaction(transaction);
		_userStatsService.RegisterDeposit();

		_resultValue.Text = $"Deposited {amount:F8}";

		UpdateBalanceUI();

		if (_fsm.CurrentState == BetState.Bankrupt)
		{
			_fsm.Fire(GameEvent.BalanceRefilled);
		}
	}

	private void OnDepositCanceled()
	{
		_resultValue.Text = "Deposit canceled.";
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
	}

	private void UpdateBalanceUI()
	{
		_balanceValue.Text =
			_wallet.Balance.ToString("F8", CultureInfo.InvariantCulture);
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
			_resultValue.Text = $"WIN - Roll: {result.Roll}";
		}
		else
		{
			_resultValue.Text = $"LOSS - Roll: {result.Roll}";
		}
	}

	// --- Metodos ejecutores de apuesta ---
	private void ExecuteCurrentStateBet()
	{
		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		if (_currentBet > _wallet.Balance)
		{
			HandleProgressionAborted();
			return;
		}

		var (result, betEvent) =
			_betService.ExecuteBet(_currentBet, chance, isHigh, null);

		BetExecuted?.Invoke(GameId, betEvent);

		UpdateResultUI(result);

		if (result.IsWin)
			HandleWin();
		else
			HandleLoss();

		_strategyPanel.SetBetAmount(_currentBet);
	}

	private void ExecuteNextAutoBet()
	{
		if (!_autoBetSession.IsRunning)
			return;

		decimal bet = _autoBetSession.GetNextBet();

		var (result, betEvent) =
			ExecuteAutoBet(bet, _autoBetSession.SessionId);

		BetExecuted?.Invoke(GameId, betEvent);
		UpdateResultUI(result);

		var outcome = new BetOutcome(
			bet,
			betEvent.Profit,
			result.IsWin
		);

		_autoBetSession.NotifyResult(
			outcome.BetAmount,
			outcome.Profit,
			outcome.IsWin,
			_wallet.Balance
		);

		decimal nextBet = _autoBetSession.GetNextBet();
		_strategyPanel.SetBetAmount(nextBet);
		_strategyPanel.SetNumberOfBets(_autoBetSession.RemainingBets);

		if (_autoBetSession.IsRunning)
			_autoBetTimer.Start();
	}
}
