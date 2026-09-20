using Godot;
using Scripts.Finance;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;

public partial class BetHistoryContainer : VBoxContainer
{
	// 260 → 100 (mini-plan 02 §C.6a, 2026-08-07). This is the POOL size as well as the display cap, so it
	// sets how many entry nodes Godot lays out and draws every frame — measured at ~20–30% of the whole
	// simulation's frame budget at 260, in DiceGame and BetsHistoryExplorer alike. It is a cost paid for
	// EXISTING, not for updating: no refresh-cadence work can reach it, only showing fewer rows can.
	// The trade is scrollback depth in the live bet history.
	public const int MaxRecentEntries = 100;

	// Fallback row pitch until the first row has been laid out: a 20 px font in a PanelContainer, as measured
	// on screen (mini-plan 10). Only used for the very first flush; after that the real size is read.
	private const float FallbackRowPitch = 32f;

	// --- Mini-plan 10 A2 step 2 — paint what can be seen, once per frame ---
	//
	// A1 measured the old path (one Setup + one MoveChild per bet) at 0.135 + 0.023 ms per bet across the two
	// lists, 80% of the cost of a bet. Its cost grew with bets, and at 1,700 bets/s it repainted ~1,700 rows a
	// second of which the ~14 on screen were the only ones anybody could see. Now:
	//
	//   - a settled bet only goes into a 100-entry ring buffer: no node work at all;
	//   - rows never move. Row i always shows the i-th newest bet, so a scroll offset maps straight to rows;
	//   - once per frame, if anything changed, only the rows inside the ScrollContainer's visible window are
	//     rewritten. Rows scrolled into view later are written the frame they appear, before it is drawn.
	//
	// The cost per frame is therefore bounded by the number of VISIBLE rows, whatever the bets per frame. Note
	// what does NOT save anything: coalescing alone. With 30–50 bets per frame and 100 rows, no row is written
	// twice in one frame; the saving comes entirely from not writing rows nobody can see.
	//
	// Coalescing within a frame is invisible by construction — Godot draws once per frame — which is why
	// BetsHistoryExplorer's row-by-row replay (mini-plan 04 §2.3) renders identically through this path.
	private readonly BetTransactionEvent[] _ring = new BetTransactionEvent[MaxRecentEntries];
	private int _ringHead;
	private int _ringCount;

	// Every push shifts every row's content by one, so one counter says which rows are current: a row is
	// correct iff it was painted at the current content version.
	private int _contentVersion;
	private readonly int[] _rowVersion = new int[MaxRecentEntries];
	private int _visibleRowCount;

	private DiceGame _game;
	private BetHistoryItem[] _pool;
	private bool _poolReady;
	private ScrollContainer _scroll;
	private bool _flushPending;

	[Export]
	private PackedScene _betHistoryItemScene;

	// Mini-plan 10 A2 — a bet the player cannot see costs nothing. The gate reads TREE visibility rather than a
	// flag of its own, so the window's `Visible` and this can never disagree. Only the LIVE path is gated:
	// LoadFromHistoricalRecords / AppendHistoricalRecord stay unconditional, because BetsHistoryExplorer drives
	// them at its own replay pace into a panel that is not toggled.
	private bool _paintsPerBet = true;

	public override void _Ready()
	{
		_paintsPerBet = IsVisibleInTree();
		VisibilityChanged += () => _paintsPerBet = IsVisibleInTree();

		// Both hosts (DiceGame, BetsHistoryExplorer) put the list directly inside a ScrollContainer. Without one
		// the flush simply paints every row, which is correct and merely slower.
		_scroll = GetParent() as ScrollContainer;
		if (_scroll != null)
		{
			_scroll.GetVScrollBar().ValueChanged += _ => RequestFlush();
			_scroll.Resized += RequestFlush;
		}

		// Processing is switched on only while a flush is pending, so an idle list costs nothing per frame.
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

		Push(betEvent);
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
			Push(ToEvent(record));
		}

		// Synchronous, not deferred: a scene entry or a view switch must show its rows on the frame it happens.
		Flush();
	}

	// The single-record twin of the loader above (mini-plan 04 §2.3). BetsHistoryExplorer used to repaint
	// a whole WINDOW each refresh, which is why its bets arrived in clumps of however many entered the
	// window since the last repaint; rendering one row per bet the replay cursor crosses reproduces
	// DiceGame's event-stream behaviour by construction, because DiceGame's own path is a push per settled
	// bet and this is the same call with a persisted record in place of a live event.
	public void AppendHistoricalRecord(BetRecord record)
	{
		if (record == null)
		{
			return;
		}

		Push(ToEvent(record));
		RequestFlush();
	}

	public void ClearEntries()
	{
		EnsurePool();
		_ringHead = 0;
		_ringCount = 0;
		System.Array.Clear(_ring);
		_contentVersion++;
		SyncRowVisibility();
	}

	private static BetTransactionEvent ToEvent(BetRecord record)
	{
		return new BetTransactionEvent(
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
	}

	private void Push(BetTransactionEvent betEvent)
	{
		EnsurePool();
		_ring[_ringHead] = betEvent;
		_ringHead = (_ringHead + 1) % MaxRecentEntries;
		if (_ringCount < MaxRecentEntries)
		{
			_ringCount++;
		}

		_contentVersion++;
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
		SyncRowVisibility();
		if (_ringCount == 0)
		{
			return;
		}

		(int first, int last) = VisibleRowRange();
		for (int row = first; row <= last; row++)
		{
			if (_rowVersion[row] == _contentVersion)
			{
				continue;
			}

			// Row 0 is the newest bet: the one just behind the ring's write head.
			int slot = (_ringHead - 1 - row + MaxRecentEntries * 2) % MaxRecentEntries;
			_pool[row].Setup(_ring[slot]);
			_rowVersion[row] = _contentVersion;
		}
	}

	// Rows [first, last] that intersect the scroll's viewport, one row of margin each side so a row partly in
	// view is never drawn stale. Rows are uniform (a fixed font in a fixed panel), so the pitch of row 0 is the
	// pitch of every row.
	private (int First, int Last) VisibleRowRange()
	{
		int lastFilled = _ringCount - 1;
		if (_scroll == null)
		{
			return (0, lastFilled);
		}

		float rowHeight = _pool[0].Size.Y > 0f ? _pool[0].Size.Y : FallbackRowPitch;
		float pitch = rowHeight + GetThemeConstant("separation");
		float top = _scroll.ScrollVertical;
		float height = _scroll.Size.Y > 0f ? _scroll.Size.Y : pitch * MaxRecentEntries;

		int first = Mathf.Max(0, (int)(top / pitch) - 1);
		int last = Mathf.Min(lastFilled, (int)((top + height) / pitch) + 1);
		return (first, last);
	}

	// Only rows that hold a bet are visible, so the scroll's content height tracks the number of bets. This
	// changes during the first 100 bets and on a clear; after that it is a no-op.
	private void SyncRowVisibility()
	{
		if (_visibleRowCount == _ringCount)
		{
			return;
		}

		for (int i = 0; i < MaxRecentEntries; i++)
		{
			_pool[i].Visible = i < _ringCount;
		}

		_visibleRowCount = _ringCount;
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
			// Avoid initial noise; rows are shown as bets fill them.
			item.Visible = false;
		}

		_visibleRowCount = 0;
		_poolReady = true;
	}
}
