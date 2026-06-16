using Godot;
using Scripts.Finance;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;

public partial class BetHistoryContainer : VBoxContainer
{
	private const int MaxRecentEntries = 260;
	private DiceGame _game;
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

	public void LoadFromHistoricalRecords(IReadOnlyList<BetRecord> records)
	{
		EnsurePool();
		ClearEntries();

		if (records == null || records.Count <= 0)
		{
			return;
		}

		foreach (BetRecord record in records.TakeLast(MaxRecentEntries))
		{
			BetTransactionEvent evt = new(
				record.BetAmount,
				record.NetAmount,
				record.NetAmount,
				record.BalanceAfter,
				record.Outcome == BetOutcome.Win,
				record.Roll,
				record.Chance,
				record.Multiplier,
				record.IsHigh,
				record.TimestampUtc
			);

			AddEntry(evt);
		}
	}

	public void ClearEntries()
	{
		EnsurePool();
		_poolIndex = 0;
		for (int i = 0; i < _pool.Length; i++)
		{
			_pool[i].Visible = false;
		}
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
