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

	// --- Mini-plan 10 D-10.2 — cells never move; the grid is rewritten once per frame ---
	//
	// A3's first run measured this view at 0.12–0.15 ms per bet in the settle loop plus 29–53 ms per frame
	// OUTSIDE it: every MoveChild made the GridContainer re-lay out all 100 cells, so at 40–120 bets per frame
	// the grid could not hold even the old 1,700 bets/s budget (14–33 fps). The move was the cost, not the
	// cell write — the opposite of what A2 had assumed when it kept this view on the per-bet path.
	//
	// Now the grid works like BetHistoryContainer, minus the viewport part: all 100 cells fit on screen, so all
	// of them are "visible" and all are rewritten on a dirty frame. Cell i always shows the i-th newest roll, a
	// settled bet only goes into a ring buffer, and no node ever changes position — so the container never
	// re-sorts. A cell whose content did not change skips the write (WinnerNumberPresenter.Setup).
	private readonly int[] _ringRoll = new int[MaxRecentEntries];
	private readonly bool[] _ringWon = new bool[MaxRecentEntries];
	private int _ringHead;
	private int _ringCount;
	private int _visibleCellCount;

	private DiceGame _game;
	private WinnerNumberPresenter[] _pool;
	private bool _poolReady;
	private bool _flushPending;

	[Export]
	private PackedScene _winnerPresenterScene;

	// Mini-plan 10 A2 — the same visibility gate as BetHistoryContainer, and the reason it was found: DiceGame's
	// scene has this grid's ScrollContainer at `visible = false`, so every row written here since then was paid
	// for and never drawn. It is now DiceGame's "Numbers" bet view, which rebuilds it from the journal each time
	// it is shown.
	private bool _paintsPerBet = true;

	public override void _Ready()
	{
		_paintsPerBet = IsVisibleInTree();
		VisibilityChanged += () => _paintsPerBet = IsVisibleInTree();

		// Processing is switched on only while a flush is pending, so an idle grid costs nothing per frame.
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		_flushPending = false;
		SetProcess(false);
		Flush();
	}

	public void SubscribeTo(DiceGame game)
	{
		_game = game;
		game.BetExecuted += OnBetExecuted;
	}

	private void OnBetExecuted(string _, BetTransactionEvent betEvent)
	{
		if (!_paintsPerBet)
		{
			return;
		}

		Push(betEvent.Roll, betEvent.IsWin);
		RequestFlush();
	}

	public void AddWinnerNumber(int number, bool won)
	{
		Push(number, won);
		RequestFlush();
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
			Push(record.Roll, record.Outcome == BetOutcome.Win);
		}

		// Synchronous, not deferred: a view switch must show its cells on the frame it happens.
		Flush();
	}

	public void ClearEntries()
	{
		EnsurePool();
		_ringHead = 0;
		_ringCount = 0;
		SyncCellVisibility();
	}

	private void Push(int roll, bool won)
	{
		EnsurePool();
		_ringRoll[_ringHead] = roll;
		_ringWon[_ringHead] = won;
		_ringHead = (_ringHead + 1) % MaxRecentEntries;
		if (_ringCount < MaxRecentEntries)
		{
			_ringCount++;
		}
	}

	private void RequestFlush()
	{
		if (_flushPending)
		{
			return;
		}

		_flushPending = true;
		SetProcess(true);
	}

	private void Flush()
	{
		EnsurePool();
		SyncCellVisibility();
		for (int cell = 0; cell < _ringCount; cell++)
		{
			// Cell 0 is the newest roll: the one just behind the ring's write head.
			int slot = (_ringHead - 1 - cell + MaxRecentEntries * 2) % MaxRecentEntries;
			_pool[cell].Setup(_ringRoll[slot], _ringWon[slot]);
		}
	}

	// Only cells that hold a roll are visible. This changes during the first 100 bets and on a clear; after
	// that it is a no-op, so the container re-lays out only while it is filling.
	private void SyncCellVisibility()
	{
		if (_visibleCellCount == _ringCount)
		{
			return;
		}

		for (int i = 0; i < MaxRecentEntries; i++)
		{
			_pool[i].Visible = i < _ringCount;
		}

		_visibleCellCount = _ringCount;
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

		_visibleCellCount = 0;
		_poolReady = true;
	}
}
