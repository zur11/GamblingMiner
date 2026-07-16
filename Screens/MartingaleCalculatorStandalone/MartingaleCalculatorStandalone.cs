using Godot;
using System;
using System.Globalization;
using UI.StatusBar;

public partial class MartingaleCalculatorStandalone : Control
{
	private LineEdit _totalBankrollInput;
	private LineEdit _initialBetInput;
	private LineEdit _increaseOnLossInput;
	private LineEdit _winChanceInput;
	private VBoxContainer _rowsContainer;
	private Label _statusLabel;
	private PackedScene _rowScene;

	private SceneManager _sceneManager;

	public override void _Ready()
	{
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		var statusBarSlot = GetNode<HBoxContainer>("%StatusBarPlaceholder");
		statusBarSlot.AddChild(new StatusBar());

		_totalBankrollInput  = GetNode<LineEdit>("%TotalBankrollInput");
		_initialBetInput     = GetNode<LineEdit>("%InitialBetInput");
		_increaseOnLossInput = GetNode<LineEdit>("%IncreaseOnLossInput");
		_winChanceInput      = GetNode<LineEdit>("%WinChanceInput");
		_rowsContainer       = GetNode<VBoxContainer>("%RowsContainer");
		_statusLabel         = GetNode<Label>("%StatusLabel");
		_rowScene            = GD.Load<PackedScene>("res://Screens/MartingaleCalculator/BetRollRow/BetRollRow.tscn");

		GetNode<Button>("%CalculateButton").Pressed += OnCalculatePressed;
		GetNode<Button>("%ResetButton").Pressed     += OnResetPressed;
		GetNode<Button>("%BackBtn").Pressed         += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);
	}

	private void OnCalculatePressed()
	{
		if (!TryParsePositive(_totalBankrollInput.Text, out double bankroll)
			|| !TryParsePositive(_initialBetInput.Text, out double initialBet))
		{
			_statusLabel.Text = "Invalid input. Use values greater than 0.";
			return;
		}

		if (!TryParseNonNegative(_increaseOnLossInput.Text, out double increaseOnLossPercent))
		{
			_statusLabel.Text = "Increase On Loss % must be 0 or greater.";
			return;
		}

		if (!TryParsePercent(_winChanceInput.Text, out double winChance))
		{
			_statusLabel.Text = "Win Chance must be between 0 and 100 (exclusive).";
			return;
		}

		if (initialBet > bankroll)
		{
			_statusLabel.Text = "Initial bet cannot exceed bankroll.";
			return;
		}

		OnResetPressed();
		BuildRows(bankroll, initialBet, increaseOnLossPercent, winChance);
	}

	private void OnResetPressed()
	{
		foreach (Node child in _rowsContainer.GetChildren())
		{
			child.QueueFree();
		}

		_statusLabel.Text = "Results reset.";
	}

	private void BuildRows(double totalBankroll, double initialBet, double increaseOnLossPercent, double winChance)
	{
		// Each losing step keeps the previous bet and adds the configured increase —
		// the same formula as ProgressiveBettingStrategy / the DiceGame calculator autofill.
		double multiplier = 1.0 + increaseOnLossPercent / 100.0;
		double remaining  = totalBankroll;
		double nextBet    = initialBet;
		double lossProb   = 1.0 - winChance / 100.0;
		int roll          = 1;
		const int maxRows = 500;

		while (nextBet <= remaining && roll <= maxRows)
		{
			var row = _rowScene.Instantiate<BetRollRow>();
			_rowsContainer.AddChild(row);

			remaining -= nextBet;
			row.SetData(roll, nextBet, remaining);
			row.SetFailProbability(Math.Pow(lossProb, roll) * 100.0);

			nextBet *= multiplier;
			roll++;
		}

		_statusLabel.Text = $"Generated {roll - 1} bets.";
		if (roll > maxRows && nextBet <= remaining)
			_statusLabel.Text += $" Sequence truncated at {maxRows} rows.";
	}

	private static bool TryParsePositive(string text, out double value)
	{
		string normalized = text.Trim().Replace(',', '.');
		bool parsed = double.TryParse(
			normalized,
			NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture,
			out value);

		return parsed && value > 0.0;
	}

	private static bool TryParseNonNegative(string text, out double value)
	{
		string normalized = text.Trim().Replace(',', '.');
		bool parsed = double.TryParse(
			normalized,
			NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture,
			out value);

		return parsed && value >= 0.0;
	}

	private static bool TryParsePercent(string text, out double value)
	{
		string normalized = text.Trim().Replace(',', '.');
		bool parsed = double.TryParse(
			normalized,
			NumberStyles.AllowDecimalPoint,
			CultureInfo.InvariantCulture,
			out value);

		return parsed && value > 0.0 && value < 100.0;
	}
}
