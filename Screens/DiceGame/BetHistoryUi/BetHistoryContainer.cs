using Godot;
using Scripts.Finance;

public partial class BetHistoryContainer : VBoxContainer
{
	private const int MaxRecentEntries = 260;
	private const int HighFrequencySampleEvery = 4;
	private DiceGame _game;
	private int _highFrequencySkipCounter;
	private BetHistoryItem[] _pool;
	private int _poolIndex;
	private bool _poolReady;

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
		EnsurePool();

		BetHistoryItem item = _pool[_poolIndex];
		_poolIndex = (_poolIndex + 1) % MaxRecentEntries;

		item.Setup(betEvent);
		MoveChild(item, 0);
	}

	private void EnsurePool()
	{
		if (_poolReady)
		{
			return;
		}

		_pool = new BetHistoryItem[MaxRecentEntries];
		for (int i = 0; i < MaxRecentEntries; i++)
		{
			var item = _betHistoryItemScene.Instantiate<BetHistoryItem>();
			_pool[i] = item;
			AddChild(item);
			// Avoid initial noise; items will be populated as bets arrive.
			item.Visible = false;
		}

		_poolIndex = 0;
		_poolReady = true;
	}
}
