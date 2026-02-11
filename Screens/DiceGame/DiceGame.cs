using Godot;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Scripts.Dice;

public partial class DiceGame : Control
{
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
	private decimal _baseBet;
	private decimal _currentBet;
	private decimal _increasePercent;
	private bool _isFirstBet = true;

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

	private void OnBetPressed()
	{
		if (!TryGetBet(out decimal baseBet))
		{
			_resultValue.Text = "Invalid base bet.";
			return;
		}

		if (!TryGetIncreasePercent(out decimal increasePercent))
		{
			_resultValue.Text = "Invalid increase %.";
			return;
		}

		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		try
		{
			// --- Inicialización de ciclo ---
			if (_isFirstBet)
			{
				_baseBet = baseBet;
				_currentBet = baseBet;
				_increasePercent = increasePercent;
				_isFirstBet = false;
			}

			// Verificar si tenemos saldo suficiente
			if (_currentBet > _engine.Balance)
			{
				_resultValue.Text = "Bet exceeds balance.";
				return;
			}

			var result = _engine.Play(
				bet: _currentBet,
				chancePercent: chance,
				isHigh: isHigh
			);

			if (result.IsWin)
			{
				_resultValue.Text = $"WIN 🎉 Roll: {result.Roll:00}";

				// Reset al base
				_currentBet = _baseBet;
				_isFirstBet = true;
			}
			else
			{
				_resultValue.Text = $"LOSS ❌ Roll: {result.Roll:00}";

				// Incremento porcentual
				decimal multiplier = 1m + (_increasePercent / 100m);
				_currentBet *= multiplier;
			}

			// Mostrar siguiente apuesta en el LineEdit
			_betInput.Text = _currentBet
				.ToString("F8", CultureInfo.InvariantCulture);

			UpdateBalanceUI();

			_previousWinnerNumbersGrid.AddWinnerNumber(
				result.Roll,
				result.IsWin
			);
		}
		catch (Exception ex)
		{
			_resultValue.Text = ex.Message;
		}
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

	// --- Aumento progresivo de apuesta ---
	private bool TryGetIncreasePercent(out decimal percent)
	{
		percent = 0m;

		string text = _increaseOnLossInput.Text.Trim().Replace(',', '.');

		if (!BetRegex.IsMatch(text))
			return false;

		if (!decimal.TryParse(
			text,
			NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture,
			out percent))
			return false;

		return percent >= 0m;
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
