using Godot;
using System;
using Scripts.Finance;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;

public partial class PreviousWinnerNumbersGrid : GridContainer
{
	private const int MaxRecentEntries = 260;
	private const int HighFrequencySampleEvery = 4;
	private DiceGame _game;
	private int _highFrequencySkipCounter;
	private WinnerNumberPresenter[] _pool;
	private int _poolIndex;
	private bool _poolReady;

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
		EnsurePool();

		WinnerNumberPresenter item = _pool[_poolIndex];
		_poolIndex = (_poolIndex + 1) % MaxRecentEntries;

		item.Setup(number, won);
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
			AddWinnerNumber(record.Roll, record.Outcome == BetOutcome.Win);
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

		_pool = new WinnerNumberPresenter[MaxRecentEntries];
		for (int i = 0; i < MaxRecentEntries; i++)
		{
			var item = _winnerPresenterScene.Instantiate<WinnerNumberPresenter>();
			_pool[i] = item;
			AddChild(item);
			item.Visible = false;
		}

		_poolIndex = 0;
		_poolReady = true;
	}
}
