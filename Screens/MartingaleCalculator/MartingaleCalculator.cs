using Godot;
using System;
using System.Globalization;
using Scripts.Betting;

public partial class MartingaleCalculator : Control
{
	[Signal]
	public delegate void CloseRequestedEventHandler();

	private LineEdit _totalBankrollInput;
	private LineEdit _initialBetInput;
	private LineEdit _multiplyOnLossInput;
	private VBoxContainer _rowsContainer;
	private Label _statusLabel;
	private Label _progressionStartingBalanceLabel;
	private PackedScene _rowScene;
	private bool _hasGameContext;
	private decimal _ctxBankroll;
	private BettingStrategyConfig _ctxConfig;
	private decimal _ctxCurrentBet;
	private bool _ctxStrategyStarted;
	private int _ctxChance;
	private int _ctxExecutedBetsCount;
	private int _ctxProgressionStreak;
	private decimal _ctxSessionProfit;
	private bool _ctxShowDoneRows;

	public override void _Ready()
	{
		_totalBankrollInput = GetNode<LineEdit>("%TotalBankrollInput");
		_initialBetInput = GetNode<LineEdit>("%InitialBetInput");
		_multiplyOnLossInput = GetNode<LineEdit>("%MultiplyOnLossInput");
		_rowsContainer = GetNode<VBoxContainer>("%RowsContainer");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_progressionStartingBalanceLabel = GetNode<Label>("%ProgressionStartingBalanceLabel");
		_rowScene = GD.Load<PackedScene>("res://Screens/MartingaleCalculator/BetRollRow/BetRollRow.tscn");

		GetNode<Button>("%CalculateButton").Pressed += OnCalculatePressed;
		GetNode<Button>("%ResetButton").Pressed += OnResetPressed;
		GetNode<Button>("%CloseCalculatorButton").Pressed += OnClosePressed;

		Visible = false;
	}

	public void Open()
	{
		Visible = true;
		_totalBankrollInput.GrabFocus();
	}

	public void Close()
	{
		Visible = false;
	}

	private void OnClosePressed()
	{
		EmitSignal(SignalName.CloseRequested);
		Close();
	}

	private void OnResetPressed()
	{
		foreach (Node child in _rowsContainer.GetChildren())
		{
			child.QueueFree();
		}

		_statusLabel.Text = "Results reset.";
	}

	private void OnCalculatePressed()
	{
		if (_hasGameContext)
		{
			CalculateFromGameContext();
			return;
		}

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

	public void UpdateFromGameSettings(
		decimal bankroll,
		BettingStrategyConfig config,
		decimal currentBet,
		bool strategyStarted,
		bool showDoneRows,
		int chance,
		int executedBetsCount,
		int progressionStreak,
		decimal sessionProfit)
	{
		_ctxBankroll = bankroll;
		_ctxConfig = config;
		_ctxCurrentBet = currentBet;
		_ctxStrategyStarted = strategyStarted;
		_ctxShowDoneRows = showDoneRows;
		_ctxChance = chance;
		_ctxExecutedBetsCount = executedBetsCount;
		_ctxProgressionStreak = progressionStreak;
		_ctxSessionProfit = sessionProfit;
		_hasGameContext = true;

		_totalBankrollInput.Text = bankroll.ToString("F8", CultureInfo.InvariantCulture);
		_initialBetInput.Text = config.BaseBet.ToString("F8", CultureInfo.InvariantCulture);
		_multiplyOnLossInput.Text = (1m + (config.IncreasePercent / 100m))
			.ToString("F8", CultureInfo.InvariantCulture);
		_progressionStartingBalanceLabel.Text =
			$"Progression starting balance: {bankroll.ToString("F8", CultureInfo.InvariantCulture)}";

		CalculateFromGameContext();
	}

	private void CalculateFromGameContext()
	{
		if (_ctxBankroll <= 0m || _ctxConfig.BaseBet <= 0m)
		{
			OnResetPressed();
			_statusLabel.Text = "Waiting for valid strategy values.";
			return;
		}

		OnResetPressed();

		decimal nextBet = _ctxConfig.BaseBet;
		decimal cumulativeLoss = 0m;
		decimal multiplier = 1m + (_ctxConfig.IncreasePercent / 100m);
		decimal payoutMultiplier = (100m * 0.9902m) / Math.Max(1, _ctxChance);
		int roll = 1;
		int maxRows = 500;
		bool stopLossMarked = false;
		bool stopWinMarked = false;
		bool truncatedByOverflow = false;
		int currentAttemptIndex = ResolveCurrentAttemptIndex(multiplier, maxRows);
		decimal remaining = _ctxBankroll;

		if (!_ctxShowDoneRows && _ctxStrategyStarted && _ctxProgressionStreak > 0)
		{
			for (int i = 1; i < currentAttemptIndex; i++)
			{
				if (!TrySafeSubtract(remaining, nextBet, out remaining))
				{
					truncatedByOverflow = true;
					break;
				}
				if (!TrySafeAdd(cumulativeLoss, nextBet, out cumulativeLoss))
				{
					truncatedByOverflow = true;
					break;
				}
				try
				{
					nextBet = GetNextLossBet(nextBet, multiplier);
				}
				catch (OverflowException)
				{
					truncatedByOverflow = true;
					break;
				}
			}

			roll = currentAttemptIndex;
		}

		while (roll <= maxRows)
		{
			if (nextBet > remaining)
				break;

			var row = _rowScene.Instantiate<BetRollRow>();
			_rowsContainer.AddChild(row);

			if (!TrySafeSubtract(remaining, nextBet, out remaining))
			{
				truncatedByOverflow = true;
				break;
			}
			if (!TrySafeAdd(cumulativeLoss, nextBet, out cumulativeLoss))
			{
				truncatedByOverflow = true;
				break;
			}

			if (!TrySafeSubtract(cumulativeLoss, nextBet, out decimal previousLosses))
			{
				truncatedByOverflow = true;
				break;
			}
			if (!TrySafeMultiply(nextBet, payoutMultiplier, out decimal grossWin))
			{
				truncatedByOverflow = true;
				break;
			}
			if (!TrySafeSubtract(grossWin, nextBet, out decimal winProfit))
			{
				truncatedByOverflow = true;
				break;
			}
			if (!TrySafeSubtract(winProfit, previousLosses, out decimal profitIfWinNow))
			{
				truncatedByOverflow = true;
				break;
			}

			bool isDoneAttempt = _ctxShowDoneRows && _ctxStrategyStarted && _ctxProgressionStreak > 0 && roll < currentAttemptIndex;
			bool isCurrentAttempt = _ctxStrategyStarted && roll == currentAttemptIndex;
			decimal projectedSessionProfitIfLoss = _ctxSessionProfit - cumulativeLoss;
			decimal projectedSessionProfitIfWinNow = _ctxSessionProfit + profitIfWinNow;
			bool hitsStopOnLoss = !stopLossMarked
				&& _ctxConfig.StopOnLoss.HasValue
				&& projectedSessionProfitIfLoss <= -_ctxConfig.StopOnLoss.Value;
			bool hitsStopOnProfit = !stopWinMarked
				&& _ctxConfig.StopOnProfit.HasValue
				&& projectedSessionProfitIfWinNow >= _ctxConfig.StopOnProfit.Value;

			if (hitsStopOnLoss) stopLossMarked = true;
			if (hitsStopOnProfit) stopWinMarked = true;

			row.SetData(roll, (double)nextBet, (double)remaining);
			row.SetFlags(isDoneAttempt, isCurrentAttempt, hitsStopOnLoss, hitsStopOnProfit);

			try
			{
				nextBet = GetNextLossBet(nextBet, multiplier);
			}
			catch (OverflowException)
			{
				truncatedByOverflow = true;
				break;
			}
			roll++;
		}

		_statusLabel.Text = _ctxStrategyStarted
			? "Auto-calculated from active progression (full sequence view)."
			: "Auto-calculated from current game inputs.";

		if (truncatedByOverflow)
			_statusLabel.Text += " Sequence truncated due to very large bet values.";
	}

	private decimal GetNextLossBet(decimal currentBet, decimal multiplier)
	{
		if (_ctxConfig.IncreasePercent <= 0m || !_ctxConfig.IncreaseOnLoss)
			return _ctxConfig.BaseBet;

		return decimal.Multiply(currentBet, multiplier);
	}

	private static bool AreClose(decimal a, decimal b)
	{
		return Math.Abs(a - b) <= 0.00000001m;
	}

	private int ResolveCurrentAttemptIndex(decimal multiplier, int maxRows)
	{
		if (!_ctxStrategyStarted)
			return 1;
		if (_ctxProgressionStreak >= 0)
			return Math.Clamp(_ctxProgressionStreak + 1, 1, maxRows);
		if (_ctxExecutedBetsCount > 0)
			return Math.Clamp(_ctxExecutedBetsCount + 1, 1, maxRows);
		if (AreClose(_ctxCurrentBet, _ctxConfig.BaseBet))
			return 1;
		if (_ctxConfig.IncreasePercent <= 0m || !_ctxConfig.IncreaseOnLoss)
			return 1;

		decimal probe = _ctxConfig.BaseBet;
		for (int i = 1; i <= maxRows; i++)
		{
			if (AreClose(probe, _ctxCurrentBet))
				return i;

			if (!TrySafeMultiply(probe, multiplier, out probe))
				break;
		}

		return 1;
	}

	private static bool TrySafeAdd(decimal a, decimal b, out decimal result)
	{
		try
		{
			result = decimal.Add(a, b);
			return true;
		}
		catch (OverflowException)
		{
			result = 0m;
			return false;
		}
	}

	private static bool TrySafeSubtract(decimal a, decimal b, out decimal result)
	{
		try
		{
			result = decimal.Subtract(a, b);
			return true;
		}
		catch (OverflowException)
		{
			result = 0m;
			return false;
		}
	}

	private static bool TrySafeMultiply(decimal a, decimal b, out decimal result)
	{
		try
		{
			result = decimal.Multiply(a, b);
			return true;
		}
		catch (OverflowException)
		{
			result = 0m;
			return false;
		}
	}

	private void BuildRows(double totalBankroll, double initialBet, double multiplyOnLoss)
	{
		double remaining = totalBankroll;
		double nextBet = initialBet;
		int roll = 1;

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
