using Godot;
using System;
using Scripts.Finance;

public partial class PreviousWinnerNumbersGrid : GridContainer
{
	private const int MaxRecentEntries = 260;
	private const int HighFrequencySampleEvery = 4;
	private DiceGame _game;
	private int _highFrequencySkipCounter;

	[Export]
	private PackedScene _winnerPresenterScene;

	public void SubscribeTo(DiceGame game)
	{
		_game = game;
		game.BetExecuted += OnBetExecuted;
	}

	private void OnBetExecuted(string _, BetTransactionEvent betEvent)
	{
		if (_game != null && _game.IsHighFrequencyAutoMode())
		{
			_highFrequencySkipCounter++;
			if (_highFrequencySkipCounter < HighFrequencySampleEvery)
			{
				return;
			}

			_highFrequencySkipCounter = 0;
		}
		else
		{
			_highFrequencySkipCounter = 0;
		}

		AddWinnerNumber(betEvent.Roll, betEvent.IsWin);
	}

	public void AddWinnerNumber(int number, bool won)
	{
		var item = _winnerPresenterScene.Instantiate<WinnerNumberPresenter>();

		AddChild(item);
		item.Setup(number, won);

		MoveChild(item, 0);
		TrimToRecentLimit();
	}

	private void TrimToRecentLimit()
	{
		while (GetChildCount() > MaxRecentEntries)
		{
			Node oldest = GetChild(GetChildCount() - 1);
			oldest.QueueFree();
		}
	}
}
