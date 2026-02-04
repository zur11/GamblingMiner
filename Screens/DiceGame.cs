using Godot;
using System;
using System.Globalization;
using System.Text.RegularExpressions;

public partial class DiceGame : Control
{
	// Nodos
	private Label _balanceValue;
	private LineEdit _betInput;
	private Label _resultValue;
	private Button _playHighBtn;
	private Button _playLowBtn;

	// Estado del juego
	private decimal _balance = 1.00000000m;
	private const decimal Payout = 1.9804m;

	private Random _rng = new Random();

	// Regexp para validar entrada decimal
	private static readonly Regex BetRegex =
	new Regex(@"^\d+(\.\d{1,8})?$", RegexOptions.Compiled);

	public override void _Ready()
	{
		// Obtener nodos
		_balanceValue = GetNode<Label>("%BalanceValue");
		_betInput = GetNode<LineEdit>("%BetInput");
		_resultValue = GetNode<Label>("%ResultValue");
		_playHighBtn = GetNode<Button>("%PlayHighBtn");
		_playLowBtn = GetNode<Button>("%PlayLowBtn");

		// Conectar señales
		_playHighBtn.Pressed += OnPlayHigh;
		_playLowBtn.Pressed += OnPlayLow;

		UpdateBalanceUI();
		_resultValue.Text = "Place your bet.";
	}

	private void OnPlayHigh()
	{
		PlayRound(isHigh: true);
	}

	private void OnPlayLow()
	{
		PlayRound(isHigh: false);
	}

	private void PlayRound(bool isHigh)
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

		int roll = _rng.Next(0, 100); // 0–99
		bool win = isHigh ? roll >= 50 : roll < 50;

		if (win)
		{
			decimal profit = bet * Payout - bet;
			_balance += profit;
			_resultValue.Text = $"WIN 🎉 Roll: {roll}";
		}
		else
		{
			_balance -= bet;
			_resultValue.Text = $"LOSS ❌ Roll: {roll}";
		}

		UpdateBalanceUI();
	}

	private bool TryGetBet(out decimal bet)
	{
		bet = 0m;

		string text = _betInput.Text.Trim().Replace(',', '.');

		// 1. Validar formato
		if (!BetRegex.IsMatch(text))
			return false;

		// 2. Parsear decimal
		if (!decimal.TryParse(
			text,
			NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture,
			out bet))
			return false;

		// 3. Valor positivo
		return bet > 0m;
	}


	private void UpdateBalanceUI()
	{
		_balanceValue.Text = _balance.ToString("F8", CultureInfo.InvariantCulture);
	}
}
