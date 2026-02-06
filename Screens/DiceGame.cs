using Godot;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

public partial class DiceGame : Control
{
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

	// --- Estado ---
	private decimal _balance = 1.00000000m;
	private const decimal RTP = 0.9902m;

	private Random _rng = new Random();

	// --- Validación decimal ---
	private static readonly Regex BetRegex =
		new Regex(@"^\d+(\.\d{1,8})?$", RegexOptions.Compiled);

	public override void _Ready()
	{
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
		if (!TryGetBet(out decimal bet))
		{
			_resultValue.Text = "Invalid bet.";
			return;
		}

		if (bet > _balance)
		{
			_resultValue.Text = "Insufficient balance.";
			return;
		}

		int roll = _rng.Next(0, 100);
		int chance = (int)_chanceSlider.Value;
		bool isHigh = _highLowToggleBtn.ButtonPressed;

		bool win = isHigh
			? roll >= 100 - chance
			: roll < chance;

		if (win)
		{
			decimal multiplier = GetMultiplier(chance);
			decimal profit = bet * multiplier - bet;
			_balance += profit;
			_resultValue.Text = $"WIN 🎉 Roll: {roll:00}";
		}
		else
		{
			_balance -= bet;
			_resultValue.Text = $"LOSS ❌ Roll: {roll:00}";
		}

		UpdateBalanceUI();
	}

	// --- Cálculos ---

	private decimal GetMultiplier(int chance)
	{
		return RTP * 100m / chance;
	}

	// --- UI Updates ---

	private void UpdateAllUI()
	{
		UpdateBalanceUI();
		UpdateChanceUI();
		UpdateWinnerRangeUI();
		UpdateMultiplierUI();
	}

	private void UpdateBalanceUI()
	{
		_balanceValue.Text = _balance.ToString("F8", CultureInfo.InvariantCulture);
	}

	private void UpdateChanceUI()
	{
		int chance = (int)_chanceSlider.Value;
		_chanceToWinValue.Text = $"{chance}%";
	}

	private void UpdateMultiplierUI()
	{
		int chance = (int)_chanceSlider.Value;
		decimal multiplier = GetMultiplier(chance);
		_multiplierValue.Text = $"x {multiplier:F4}";
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
