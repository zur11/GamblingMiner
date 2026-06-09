using Godot;
using System;
using System.Globalization;
using UI.StatusBar;

public partial class MartingaleCalculatorStandalone : Control
{
	private LineEdit _totalBankrollInput;
	private LineEdit _initialBetInput;
	private LineEdit _multiplyOnLossInput;
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
		_multiplyOnLossInput = GetNode<LineEdit>("%MultiplyOnLossInput");
		_rowsContainer       = GetNode<VBoxContainer>("%RowsContainer");
		_statusLabel         = GetNode<Label>("%StatusLabel");
		_rowScene            = GD.Load<PackedScene>("res://Screens/Shared/MartingaleCalculatorSnapshot/BetRollRow/BetRollRow.tscn");

		GetNode<Button>("%CalculateButton").Pressed += OnCalculatePressed;
		GetNode<Button>("%ResetButton").Pressed     += OnResetPressed;
		GetNode<Button>("%BackBtn").Pressed         += () => _sceneManager?.Go(SceneManager.SceneId.MainMenu);
	}

	private void OnCalculatePressed()
	{
		if (!TryParsePositive(_totalBankrollInput.Text, out double bankroll)
			|| !TryParsePositive(_initialBetInput.Text, out double initialBet)
			|| !TryParsePositive(_multiplyOnLossInput.Text, out double multiplyOnLoss))
		{
			_statusLabel.Text = "Invalid input. Use values greater than 0.";
			return;
		}

		if (initialBet > bankroll)
		{
			_statusLabel.Text = "Initial bet cannot exceed bankroll.";
			return;
		}

		OnResetPressed();
		BuildRows(bankroll, initialBet, multiplyOnLoss);
	}

	private void OnResetPressed()
	{
		foreach (Node child in _rowsContainer.GetChildren())
		{
			child.QueueFree();
		}

		_statusLabel.Text = "Results reset.";
	}

	private void BuildRows(double totalBankroll, double initialBet, double multiplyOnLoss)
	{
		double remaining = totalBankroll;
		double nextBet   = initialBet;
		int roll         = 1;

		while (nextBet <= remaining)
		{
			var row = _rowScene.Instantiate<BetRollRow>();
			_rowsContainer.AddChild(row);

			remaining -= nextBet;
			row.SetData(roll, nextBet, remaining);

			nextBet *= multiplyOnLoss;
			roll++;
		}

		_statusLabel.Text = $"Generated {roll - 1} bets.";
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
}
