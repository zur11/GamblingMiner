using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Scripts.Dice;

public partial class DiceGame : Control
{
	// --- Enums ---
	private enum BetState
	{
		Idle,
		Progression,
		Bankrupt
	}

	private enum BetEvent
	{
		BetPressed,
		Win,
		Loss,
		InsufficientBalance,
		ManualReset
	}

	// Variables de estado
	private BetState _state = BetState.Idle;
	private readonly Dictionary<(BetState, BetEvent), BetState> _transitions
		= new Dictionary<(BetState, BetEvent), BetState>
	{
		{ (BetState.Idle, BetEvent.BetPressed), BetState.Progression },

		{ (BetState.Progression, BetEvent.Win), BetState.Idle },
		{ (BetState.Progression, BetEvent.Loss), BetState.Progression },
		{ (BetState.Progression, BetEvent.InsufficientBalance), BetState.Bankrupt },

		{ (BetState.Bankrupt, BetEvent.ManualReset), BetState.Idle }
	};

	// --- Engine ---
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
		// Setear Balance inicial en el engine
		_engine = new DiceEngine(initialBalance: 1.00000000m);

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

		// Configurar toggle
		_highLowToggleBtn.ToggleMode = true;
		_highLowToggleBtn.ButtonPressed = false;
		_highLowToggleBtn.Text = "LOW";

		// Conectar señales
		_highLowToggleBtn.Pressed += OnHighLowToggled;
		_chanceSlider.ValueChanged += OnChanceChanged;
		_betBtn.Pressed += OnBetPressed;
		_betInput.TextChanged += OnBetInputChanged;


		UpdateAllUI();
		_resultValue.Text = "Place your bet.";
	}

	// State Machine Methods
	private void OnEnterState(BetState state)
	{
		switch (state)
		{
			case BetState.Idle:
				break;

			case BetState.Progression:
				break;

			case BetState.Bankrupt:
				_resultValue.Text = "Insufficient balance.";
				break;
		}
	}

	private void OnExitState(BetState state)
	{
		// Si necesitas limpiar cosas al salir
	}


	private void Transition(BetEvent trigger)
	{
		var key = (_state, trigger);

		if (_transitions.TryGetValue(key, out var newState))
		{
			LogTransition(_state, trigger, newState);
			OnExitState(_state);
			_state = newState;
			OnEnterState(newState);
		}
		else
		{
			GD.PrintErr($"Invalid transition: {_state} + {trigger}");
		}
	}

	private void LogTransition(BetState from, BetEvent trigger, BetState to)
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

		if (_state == BetState.Idle)
		{
			_baseBet = baseBet;
			_currentBet = baseBet;
			_increasePercent = increasePercent;

			Transition(BetEvent.BetPressed);
		}

		if (_currentBet > _engine.Balance)
		{
			Transition(BetEvent.InsufficientBalance);
			return;
		}

		DiceResult result = _engine.Play(_currentBet, chance, isHigh);

		// Always update UI immediately after play
		UpdateResultUI(result);
		UpdatePreviousNumbers(result);

		if (result.IsWin)
		{
			_currentBet = _baseBet;
			Transition(BetEvent.Win);
		}
		else
		{
			if (_increasePercent > 0m)
			{
				decimal multiplier = 1m + (_increasePercent / 100m);
				_currentBet *= multiplier;
			}

			Transition(BetEvent.Loss);
		}

		_betInput.Text = _currentBet
			.ToString("F8", CultureInfo.InvariantCulture);

		UpdateBalanceUI();
	}

	private void OnBetInputChanged(string newText)
	{
		_userModifiedBase = true;
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
			_engine.Balance.ToString("F8", CultureInfo.InvariantCulture);
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
