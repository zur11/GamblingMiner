using Godot;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;

// Mini-plan 23 E (developer's decision, 2026-10-12) — the LAST 10 winning numbers, in one row, newest on the left.
//
// It replaces PreviousWinnerNumbersGrid in BetsHistoryExplorer. The grid showed 100 cells, and every bet shifts every
// cell by one, so every frame that showed a bet rewrote all 100 labels. Measured in the explorer at 99 bets/s: 3–4 ms of
// script per frame in the grid's flush alone, against 0.2 ms for the whole bet list, with the frame going from 17 ms
// idle to 37–50 ms, most of it outside every timed script (re-shaping and drawing 100 labels). A hundred numbers that
// change 99 times a second cannot be read anyway; ten can.
//
// Same interface as the grid (AddWinnerNumber / LoadFromHistoricalRecords / ClearEntries) and the same cell scene
// (WinnerNumberPresenter, which skips a write whose content did not change), so the two are interchangeable at a call
// site. Like the grid and BetHistoryContainer, cells never move: cell i always shows the i-th newest roll, a pushed roll
// only goes into a ring buffer, and the row is rewritten at most once per frame.
public partial class PreviousWinnerNumbersRow : HBoxContainer
{
	public const int MaxRecentEntries = 10;

	[Export]
	private PackedScene _winnerPresenterScene;

	private readonly int[] _ringRoll = new int[MaxRecentEntries];
	private readonly bool[] _ringWon = new bool[MaxRecentEntries];
	private int _ringHead;
	private int _ringCount;
	private int _visibleCellCount = -1;
	private WinnerNumberPresenter[] _pool;
	private bool _flushPending;

	public override void _Ready()
	{
		EnsurePool();
		// Processing is switched on only while a flush is pending, so an idle row costs nothing per frame.
		SetProcess(false);
		// The explorer can switch to its single-number view (LatestWinnerNumber): a hidden row keeps taking rolls into
		// its ring but paints nothing, and becoming visible paints it on that frame.
		VisibilityChanged += () =>
		{
			if (IsVisibleInTree())
			{
				Flush();
			}
		};
	}

	public override void _Process(double delta)
	{
		_flushPending = false;
		SetProcess(false);
		long probeStart = System.Diagnostics.Stopwatch.GetTimestamp();
		Flush();
		ExplorerFrameProbe.AddWinnersFlush(probeStart);   // DEBUG-only
	}

	public void AddWinnerNumber(int number, bool won)
	{
		Push(number, won);
		RequestFlush();
	}

	public void LoadFromHistoricalRecords(IReadOnlyList<BetRecord> records)
	{
		ClearEntries();
		if (records == null || records.Count <= 0)
		{
			return;
		}

		foreach (BetRecord record in records.TakeLast(MaxRecentEntries))
		{
			Push(record.Roll, record.Outcome == BetOutcome.Win);
		}

		// Synchronous, not deferred: a scene entry or a rebuilt window must show its cells on the frame it happens.
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
		if (_flushPending || !IsVisibleInTree())
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

	// Only cells that hold a roll are visible: this changes while the row fills and on a clear, and is a no-op after.
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
		if (_pool != null)
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
	}
}
