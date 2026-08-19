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
			AppendHistoricalRecord(record);
		}
	}

	// The single-record twin of the loader above (mini-plan 04 §2.3). BetsHistoryExplorer used to repaint
	// a whole WINDOW each refresh, which is why its bets arrived in clumps of however many entered the
	// window since the last repaint; rendering one row per bet the replay cursor crosses reproduces
	// DiceGame's event-stream behaviour by construction, because DiceGame's own path is `AddEntry` per
	// settled bet and this is the same call with a persisted record in place of a live event.
	public void AppendHistoricalRecord(BetRecord record)
	{
		if (record == null)
		{
			return;
		}

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
