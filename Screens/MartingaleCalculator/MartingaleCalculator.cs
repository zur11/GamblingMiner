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
	private LineEdit _winChanceInput;
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

	// Mini-plan 21 E — the table is built only where someone can see it. CalculateFromGameContext instantiates up
	// to 500 row scenes, and DiceGame refreshed it while the calculator was CLOSED: three times on every entry with
	// a run going (strategy snapshot, explicit refresh, binding to the run), ~330 ms each — measured by the
	// [SceneEntry] probe at 658–740 + 350–375 ms of a ~1.4 s entry — plus freeing 1,500 discarded rows in the next
	// frame, and again on every throttled refresh during the run. The inputs are still recorded at once; only the
	// rows wait for Open(). Mini-plan 10's rule for the bet list, applied here: a row nobody can see costs nothing.
	private bool _tablePending;

	public override void _Ready()
	{
		_totalBankrollInput = GetNode<LineEdit>("%TotalBankrollInput");
		_initialBetInput = GetNode<LineEdit>("%InitialBetInput");
		_multiplyOnLossInput = GetNode<LineEdit>("%MultiplyOnLossInput");
		_winChanceInput = GetNode<LineEdit>("%WinChanceInput");
		_rowsContainer = GetNode<VBoxContainer>("%RowsContainer");
		_statusLabel = GetNode<Label>("%StatusLabel");
		_progressionStartingBalanceLabel = GetNode<Label>("%ProgressionStartingBalanceLabel");
		_rowScene = GD.Load<PackedScene>("res://Screens/MartingaleCalculator/BetRollRow/BetRollRow.tscn");

		// Mini-plan 21 E (developer's screenshot, 2026-10-11): the panel used the default theme's semi-transparent
		// style, so DiceGame's bet list showed through the table. The full-screen Background stays a translucent
		// dim on purpose; the panel itself is made opaque here, without touching the .tscn layout.
		GetNode<PanelContainer>("Panel").AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.16f, 0.16f, 0.17f, 1f),
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6
		});

		GetNode<Button>("%CalculateButton").Pressed += OnCalculatePressed;
		GetNode<Button>("%ResetButton").Pressed += OnResetPressed;
		GetNode<Button>("%CloseCalculatorButton").Pressed += OnClosePressed;

		Visible = false;
	}

	public void Open()
	{
		// DiceGame builds several controls in code AFTER this node (the DEV block, the Bet View toggle, the DEV credits
		// ladder), and Godot draws — and hands clicks to — later siblings on top. Becoming the last child on every
		// open puts the calculator above all of them, for drawing and for input alike, whatever is added later.
		MoveToFront();
		Visible = true;
		if (_tablePending && _hasGameContext)
		{
			_tablePending = false;
			CalculateFromGameContext();
		}

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
		BuildRows(bankroll, initialBet, multiplyOnLoss, winChance);
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
		// The calculator projects a LOSING ladder, so it reads the loss-side percent only.
		_multiplyOnLossInput.Text = (1m + (config.IncreaseOnLossPercent / 100m))
			.ToString("F8", CultureInfo.InvariantCulture);
		_winChanceInput.Text = chance.ToString(CultureInfo.InvariantCulture);
		_progressionStartingBalanceLabel.Text =
			string.Create(CultureInfo.InvariantCulture, $"Progression starting balance: {bankroll:F8} SC");

		if (!IsVisibleInTree())
		{
			_tablePending = true;   // built by Open(), from the context recorded above
			return;
		}

		_tablePending = false;
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
		decimal multiplier = 1m + (_ctxConfig.IncreaseOnLossPercent / 100m);
		decimal payoutMultiplier = (100m * 0.9902m) / Math.Max(1, _ctxChance);
		int roll = 1;
		int maxRows = 500;
		bool stopLossMarked = false;
		bool stopWinMarked = false;
		bool truncatedByOverflow = false;
		int currentAttemptIndex = ResolveCurrentAttemptIndex(multiplier, maxRows);
		double lossProb = _ctxChance > 0 ? 1.0 - _ctxChance / 100.0 : 0.0;
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
			row.SetFailProbability(Math.Pow(lossProb, roll) * 100.0);
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
		if (_ctxConfig.IncreaseOnLossPercent <= 0m)
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
		if (_ctxConfig.IncreaseOnLossPercent <= 0m)
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

	private void BuildRows(double totalBankroll, double initialBet, double multiplyOnLoss, double winChance)
	{
		double remaining = totalBankroll;
		double nextBet   = initialBet;
		double lossProb  = 1.0 - winChance / 100.0;
		int roll         = 1;

		while (nextBet <= remaining)
		{
			var row = _rowScene.Instantiate<BetRollRow>();
			_rowsContainer.AddChild(row);

			remaining -= nextBet;
			row.SetData(roll, nextBet, remaining);
			row.SetFailProbability(Math.Pow(lossProb, roll) * 100.0);

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
