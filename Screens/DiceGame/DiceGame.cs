using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Scripts.Dice;
using Scripts.GameState;
using Scripts.Finance;
using Scripts.Game;

public partial class DiceGame : Control
{
	// --- State Machine ---
	private GameStateMachine _fsm;

	// --- Finanzas ---
	private Wallet _wallet;

	// --- Servicio de apuestas ---
	private BetService _betService;
	private DiceEngine _engine;

	// --- Nodos UI ---
	private Label _balanceValue;
	private LineEdit _betInput;
	private Label _resultValue;

	private Label _winnerNumbersValue;
	private Label _chanceToWinValue;
	private Label _multiplierValue;

	private Slider _chanceSlider;
	private Button _highLowToggleBtn;
	private Button _betBtn;

	private LineEdit _increaseOnLossInput;

	private DepositPopup _depositPopup;
	private Button _depositBtn;

	// --- Variables para aumento progresivo de apuesta ---
	private decimal _baseBet = 0m;
	private decimal _currentBet = 0m;
	private decimal _increasePercent = 0m;

	private bool _userModifiedBase = false;

	[Export]
	private PreviousWinnerNumbersGrid _previousWinnerNumbersGrid;


	// --- Validación decimal ---
	private static readonly Regex BetRegex =
		new Regex(@"^\d+(\.\d{1,8})?$", RegexOptions.Compiled);

	public override void _Ready()
	{
		// Inicializar motor y servicios
		_engine = new DiceEngine();
		_wallet = new Wallet(1.00000000m);
		_betService = new BetService(_engine, _wallet);

		// Obtener nodos
		_balanceValue = GetNode<Label>("%BalanceValue");
		_betInput = GetNode<LineEdit>("%BetInput");
		_resultValue = GetNode<Label>("%ResultValue");
		_winnerNumbersValue = GetNode<Label>("%WinnerNumbersValue");
		_chanceToWinValue = GetNode<Label>("%ChanceToWinValue");
		_multiplierValue = GetNode<Label>("%MultiplierValue");
		_chanceSlider = GetNode<Slider>("%ChanceSlider");
		_highLowToggleBtn = GetNode<Button>("%HighLowToggleBtn");
		_betBtn = GetNode<Button>("%BetBtn");
		_increaseOnLossInput = GetNode<LineEdit>("%IncreaseOnLossInput");
		_depositPopup = GetNode<DepositPopup>("%DepositPopup");
		_depositBtn = GetNode<Button>("%DepositBtn");

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
		_betBtn.Pressed += OnBetPressed;
		_betInput.TextChanged += OnBetInputChanged;
		_depositBtn.Pressed += OnDepositBtnPressed;
		_depositPopup.DepositConfirmed += OnDepositConfirmed;
		_depositPopup.DepositCanceled += OnDepositCanceled;
		_fsm.OnTransition += LogTransition;

		UpdateAllUI();
		_resultValue.Text = "Place your bet.";
	}

	// State Machine Methods
	private void OnStateEntered(BetState state)
	{
		switch (state)
		{
			case BetState.Bankrupt:
				_resultValue.Text = "Bankrupt. Deposit required.";
				_betBtn.Disabled = true;
				break;
		}
	}

	private void OnStateExited(BetState state)
	{
		if (state == BetState.Bankrupt)
		{
			_betBtn.Disabled = false;
		}
	}

	private void HandleBetPressed(decimal baseBet, decimal increasePercent)
	{
		_baseBet = baseBet;
		_currentBet = baseBet;
		_increasePercent = increasePercent;

		_fsm.Fire(GameEvent.BetPressed);
	}

	private void HandleWin()
	{
		_currentBet = _baseBet;
		_fsm.Fire(GameEvent.Win);
	}

	private void HandleLoss()
	{
		if (_increasePercent > 0m)
		{
			decimal multiplier = 1m + (_increasePercent / 100m);
			_currentBet *= multiplier;
		}

		_fsm.Fire(GameEvent.Loss);
	}

	private void HandleProgressionAborted()
	{
		_currentBet = 0m;
		_resultValue.Text = "Bet exceeds balance. Progression stopped.";

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

	private void OnBetPressed()
	{
		if (!TryGetBet(out decimal baseBet))
			return;

		if (!TryGetIncreasePercent(out decimal increasePercent))
			return;

		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		if (_fsm.CurrentState == BetState.Idle)
		{
			HandleBetPressed(baseBet, increasePercent);
		}

		if (_currentBet > _wallet.Balance)
		{
			_resultValue.Text = "Bet exceeds current balance.";

			if (_fsm.CurrentState == BetState.Progression)
			{
				HandleProgressionAborted();
			}

			return;
		}

		DiceResult result;

		try
		{
			result = _betService.ExecuteBet(_currentBet, chance, isHigh);
		}
		catch
		{
			_resultValue.Text = "Insufficient balance.";
			return;
		}

		// Always update UI immediately after play
		UpdateResultUI(result);
		UpdatePreviousNumbers(result);

		if (result.IsWin)
		{
			HandleWin();
		}
		else
		{
			HandleLoss();
		}

		_betInput.Text = _currentBet
			.ToString("F8", CultureInfo.InvariantCulture);

		UpdateBalanceUI();
		
		if (_wallet.Balance <= 0m)
		{
			_fsm.Fire(GameEvent.BankruptDetected);
		}
	}

	private void OnBetInputChanged(string newText)
	{
		if (_fsm.CurrentState == BetState.Progression)
		{
			_currentBet = 0m;
			_baseBet = 0m;
			_increasePercent = 0m;

			_fsm.Fire(GameEvent.ManualReset);

			_resultValue.Text = "Progression manually reset.";
		}
	}

	private void OnDepositBtnPressed()
	{
		_depositPopup.Open();
	}

	private void OnDepositConfirmed(double amountDouble)
	{
		decimal amount = (decimal)amountDouble;

		var transaction = new Transaction(
			TransactionType.Deposit,
			amount);

		_wallet.ApplyTransaction(transaction);

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

	private void UpdatePreviousNumbers(DiceResult result)
	{
		_previousWinnerNumbersGrid.AddWinnerNumber(
			result.Roll,
			result.IsWin
			);
	}

	// --- Aumento progresivo de apuesta ---
	private bool TryGetIncreasePercent(out decimal percent)
	{
		percent = 0m;

		string text = _increaseOnLossInput.Text.Trim();

		// Vacío = sin incremento
		if (string.IsNullOrEmpty(text))
			return true;

		// Solo enteros
		if (!int.TryParse(text, out int value))
			return false;

		if (value < 0 || value > 100_000_000)
			return false;

		percent = value;
		return true;
	}

	// --- Validación apuesta ---
	private bool TryGetBet(out decimal bet)
	{
		bet = 0m;

		string text = _betInput.Text.Trim().Replace(',', '.');

		if (!BetRegex.IsMatch(text))
			return false;

		if (!decimal.TryParse(
			text,
			NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture,
			out bet))
			return false;

		return bet > 0m;
	}
}
