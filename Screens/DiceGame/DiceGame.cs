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
	private WalletController _walletController;
	private BetService _betService;
	private UserStatsService _userStatsService;
	private FinancialBettingStats _financialStats;
	private Timer _autoBetTimer;
	private ManualBetSession _manualSession;
	private AutoBetSession _autoSession;

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

	private Slider _chanceSlider;
	private Button _highLowToggleBtn;

	private DepositPopup _depositPopup;
	private Button _depositBtn;

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
		_wallet = new Wallet(1.00000000m);
		_betService = new BetService(_engine, _wallet, TransactionSource.Bet);
		var strategy = new ProgressiveBettingStrategy();

		_manualSession = new ManualBetSession(_betService, _wallet, strategy);
		_autoSession = new AutoBetSession(_betService, _wallet, strategy);

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
		_manualSession.OnStopped += OnManualBetSessionStopped;
		_autoSession.OnStopped += OnAutoBetSessionStopped;

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
	}

	private void OnStrategyConfigChanged()
	{
		// 🔥 reset inmediato en manual
		if (_manualSession.IsRunning)
		{
			_manualSession.Stop(IBettingStrategy.StopReason.ManualStop);
		}

		if (_walletFSM.State != WalletState.Bankrupt)
			_strategyPanel.SetManualEnabled(true);
	}

	// --- Manual Bet Session
	private void OnManualBetFromPanel()
	{
		if (!_strategyPanel.TryGetValidBet(out decimal bet))
		{
			_resultValue.Text = "Invalid bet format.";
			return;
		}

		bool isValidBet = IsBetAmountValid(_strategyPanel.BetAmount);

		if (!isValidBet)
			return;

		ExecuteManualBet();
	}

	private void ExecuteManualBet()
	{
		decimal bet = _strategyPanel.BetAmount;

		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		if (!_manualSession.IsRunning)
		{
			var config = _strategyPanel.BuildConfig();

			_manualSession.Start(
				_strategyPanel.NumberOfBets,
				config
			);
		}

		var (result, betEvent, nextBet) =
			_manualSession.ExecuteNext(chance, isHigh);

		BetExecuted?.Invoke(GameId, betEvent);

		_strategyPanel.SetNumberOfBets(
			_manualSession.IsInfinite ? 0 : _manualSession.RemainingBets
		);

		_strategyPanel.SetBetAmount(nextBet);

		if (!_manualSession.IsRunning)
			return;

		UpdateResultUI(result);
	}

	private void OnManualBetSessionStopped(IBettingStrategy.StopReason? reason)
	{
		_strategyPanel.SetManualEnabled(false);
		HandleSessionStopped(_manualSession, "Manual stopped");
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

		if (!running)
		{
			_autoBetTimer.Stop();
			_autoSession.Stop(IBettingStrategy.StopReason.ManualStop);
			return;
		}

		var config = _strategyPanel.BuildConfig();

		_autoSession.Start(
			_strategyPanel.NumberOfBets,
			config
		);

		_autoBetTimer.Start();
	}

	private void ExecuteNextAutoBet()
	{
		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		var (result, betEvent, nextBet) =
			_autoSession.ExecuteNext(chance, isHigh);

		BetExecuted?.Invoke(GameId, betEvent);

		_strategyPanel.SetNumberOfBets(
			_autoSession.IsInfinite ? 0 : _autoSession.RemainingBets
		);

		_strategyPanel.SetBetAmount(nextBet);

		if (!_autoSession.IsRunning)
			return; // 🔥 CLAVE: evita pisar el mensaje del evento

		UpdateResultUI(result); // Si se coloca antes del return, _resultLabel no muestra razon de parada
		_autoBetTimer.Start();
	}

	private void OnAutoBetSessionStopped(IBettingStrategy.StopReason? reason)
	{
		_autoBetTimer.Stop();
		_strategyPanel.SetAutoRunning(false);
		HandleSessionStopped(_autoSession, "Auto stopped");
	}

	private void OnAutoBetTimerTimeout()
	{
		ExecuteNextAutoBet();
	}

	// --- Handlers comunes de sesión ---
	private void HandleSessionStopped(BaseBetSession session, string prefix)
	{
		_resultValue.Text = $"{prefix}: {session.LastStopReason}";
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
