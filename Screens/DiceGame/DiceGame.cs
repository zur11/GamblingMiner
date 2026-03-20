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
	private BetController _betController;
	private WalletController _walletController;
	private BetService _betService;
	private UserStatsService _userStatsService;
	private FinancialBettingStats _financialStats;
	private Timer _autoBetTimer;
	private BetSession _betSession;
	private bool _isAutoRunning;
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

	private Slider _chanceSlider;
	private Button _highLowToggleBtn;

	private DepositPopup _depositPopup;
	private Button _depositBtn;

	// --- Componentes del juego ---
	[Export]
	private PreviousWinnerNumbersGrid _previousWinnerNumbersGrid;

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

		_betController = new BetController(
			_betService,
			_wallet,
			strategy
		);

		_betSession = new BetSession(_betController);

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
		_chanceSlider = GetNode<Slider>("%ChanceSlider");
		_highLowToggleBtn = GetNode<Button>("%HighLowToggleBtn");
		_depositPopup = GetNode<DepositPopup>("%DepositPopup");
		_depositBtn = GetNode<Button>("%DepositBtn");
		_userStatsService = GetNode<UserStatsService>("/root/UserStatsService");
		_financialStats = GetNode<FinancialBettingStats>("%FinancialBettingStats");

		// Configurar etiqueta de High/Low toggle Btn 
		_highLowToggleBtn.Text = "LOW";

		// Conectar señales
		_highLowToggleBtn.Pressed += OnHighLowToggled;
		_chanceSlider.ValueChanged += OnChanceChanged;
		_depositBtn.Pressed += OnDepositBtnPressed;
		_depositPopup.DepositConfirmed += OnDepositPopupDepositConfirmed;
		_depositPopup.DepositCanceled += OnDepositCanceled;
		_wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
		_previousWinnerNumbersGrid.SubscribeTo(this);
		_betHistoryContainer.SubscribeTo(this);
		_userStatsService.RegisterSource(this);
		_financialStats.ConnectTo(_userStatsService);
		_strategyPanel.BetOnceBtnPressed += OnManualBetFromPanel;
		_strategyPanel.AutoBetToggled += OnAutoBetToggled;
		_strategyPanel.BetAmountInputChanged += OnBetInputChanged;
		_autoBetTimer.Timeout += OnAutoBetTimerTimeout;
		_strategyPanel.StrategyConfigChanged += OnStrategyConfigChanged;
		_betSession.OnStopped += OnBetSessionStopped;

		_wallet.BalanceDeltaChanged += (sessionId, delta) =>
		{
			if (_walletController.Balance <= 0m)
			{
				_walletFSM.Fire(WalletEvent.BalanceZero);
			}
		};

		UpdateAllUI();
		_resultValue.Text = "Place your bet.";
	}

	// --- API ---
	public decimal GetCurrentBalance()
	{
		return _walletController.Balance; // No usado actualmente, pero expuesto para posibles futuras necesidades
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
	}

	// --- Eventos de componentes ---
	private void OnManualBetFromPanel()
	{
		ExecuteManualBet();
	}

	private void OnStrategyConfigChanged()
	{
		if (_walletFSM.State != WalletState.Bankrupt)
			_strategyPanel.SetManualEnabled(true);
	}

	// --- Autobet Session
	private void OnAutoBetToggled(bool running)
	{
		_isAutoRunning = running;

		_strategyPanel.SetManualEnabled(!running);
		_strategyPanel.SetAutoRunning(running);

		if (!running)
		{
			_autoBetTimer.Stop();
			_betSession.Stop(IBettingStrategy.StopReason.ManualStop);
			return;
		}

		var config = _strategyPanel.BuildConfig();

		_betSession.Start(
			_walletController.Balance,
			_strategyPanel.NumberOfBets,
			config
		);

		_autoBetTimer.Start();
	}

	private void OnBetSessionStopped(IBettingStrategy.StopReason? reason)
	{
		if (_isAutoRunning)
		{
			_autoBetTimer.Stop();
			_strategyPanel.SetAutoRunning(false); 
			_isAutoRunning = false;
			_resultValue.Text = $"Stopped: {reason}";
			return;
		}

		if (reason != IBettingStrategy.StopReason.ManualStop)
		{
			_strategyPanel.SetManualEnabled(false);
		}

		_resultValue.Text = $"Stopped: {reason}";
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

		_walletController.Deposit(amount);

		_userStatsService.RegisterDeposit();

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
			_resultValue.Text = $"WIN - Roll: {result.Roll}";
		}
		else
		{
			_resultValue.Text = $"LOSS - Roll: {result.Roll}";
		}
	}

	// --- Metodos ejecutores de apuesta ---
	private void ExecuteManualBet()
	{
		decimal bet = _strategyPanel.BetAmount;

		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		if (bet <= 0m)
		{
			_resultValue.Text = "Invalid bet amount.";
			return;
		}

		if (bet > _walletController.Balance)
		{
			_resultValue.Text = "Insufficient balance.";
			return;
		}

		if (!_betSession.IsRunning)
		{
			var config = _strategyPanel.BuildConfig();

			_betSession.Start(
				_walletController.Balance,
				_strategyPanel.NumberOfBets,
				config
			);
		}

		var (result, betEvent, nextBet) =
			_betSession.ExecuteNext(chance, isHigh);

		BetExecuted?.Invoke(GameId, betEvent);
		UpdateResultUI(result);

		_strategyPanel.SetBetAmount(nextBet);

		_strategyPanel.SetNumberOfBets(
			_betSession.IsInfinite ? 0 : _betSession.RemainingBets
		);
	}

	private void ExecuteNextAutoBet()
	{
		if (!_betSession.IsRunning)
			return;

		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		var (result, betEvent, nextBet) =
			_betSession.ExecuteNext(chance, isHigh);

		BetExecuted?.Invoke(GameId, betEvent);

		UpdateResultUI(result);

		_strategyPanel.SetBetAmount(nextBet);

		_strategyPanel.SetNumberOfBets(
			_betSession.IsInfinite ? 0 : _betSession.RemainingBets
		);

		if (_betSession.IsRunning)
			_autoBetTimer.Start();
	}
}
