using Godot;
using Scripts.Finance;

public partial class BetHistoryContainer : VBoxContainer
{
	private const int MaxRecentEntries = 260;
	private const int HighFrequencySampleEvery = 4;
	private DiceGame _game;
	private int _highFrequencySkipCounter;

	[Export]
	private PackedScene _betHistoryItemScene;

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

		AddEntry(betEvent);
	}

	private void AddEntry(BetTransactionEvent betEvent)
	{
		var item = _betHistoryItemScene
			.Instantiate<BetHistoryItem>();

		AddChild(item);
		item.Setup(betEvent);

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
