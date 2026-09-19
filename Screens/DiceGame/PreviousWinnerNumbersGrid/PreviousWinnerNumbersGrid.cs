using Godot;
using System;
using Scripts.Finance;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;

public partial class PreviousWinnerNumbersGrid : GridContainer
{
	// 260 → 100, in step with BetHistoryContainer — see the note there. The two containers are always
	// rendered together, so they must be sized together or the cheaper one's saving is invisible.
	private const int MaxRecentEntries = 100;
	private DiceGame _game;
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

	// Mini-plan 10 A2 — the same visibility gate as BetHistoryContainer, and the reason it was found: DiceGame's
	// scene has this grid's ScrollContainer at `visible = false`, so every row written here since then was paid
	// for and never drawn. It is now DiceGame's "Numbers" bet view, which rebuilds it from the journal each time
	// it is shown.
	private bool _paintsPerBet = true;

	public override void _Ready()
	{
		_paintsPerBet = IsVisibleInTree();
		VisibilityChanged += () => _paintsPerBet = IsVisibleInTree();
	}

	private void OnBetExecuted(string _, BetTransactionEvent betEvent)
	{
		if (!_paintsPerBet)
		{
			return;
		}

		// Deliberately still one Setup + MoveChild per bet, unlike BetHistoryContainer (mini-plan 10 A2): all 100
		// cells are on screen at once, so "paint only what is visible" saves nothing here, and a fixed-order
		// rewrite would cost 100 cell writes per frame against today's 30–50. A3 measures this view on its own.
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
