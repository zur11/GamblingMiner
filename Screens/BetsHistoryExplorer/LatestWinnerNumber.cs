using Godot;
using Scripts.History;
using System.Collections.Generic;
using System.Globalization;

// Mini-plan 23 E (developer's request, 2026-10-12) — the explorer's second winning-numbers view: only the LATEST roll,
// one big tinted number, the way DiceGame's ResultValue shows it. The 10-number row stays the default; a toggle in the
// explorer switches between the two.
//
// Why it is cheaper than the row: every bet shifts all ten cells of the row, so a frame that shows a bet rewrites ten
// label texts and ten stylebox colours (each emitting `changed` and a redraw). This view rewrites one text and one
// Modulate — a colour change on Modulate costs no re-shaping.
//
// Same interface as PreviousWinnerNumbersRow, so the explorer feeds both the same way. Like the row it keeps its state
// while hidden and paints only while visible, at most once per frame.
public partial class LatestWinnerNumber : Label
{
	// The same colours DiceGame's ResultValue uses (DiceGame._rollWinColor / _rollLossColor defaults).
	[Export] private Color _winColor = Colors.Green;
	[Export] private Color _lossColor = Colors.Red;

	private int _roll = -1;
	private bool _won;
	private int _shownRoll = -1;
	private bool _shownWon;
	private bool _flushPending;

	public override void _Ready()
	{
		VisibilityChanged += OnVisibilityChanged;
		SetProcess(false);
		Flush();
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
		_roll = number;
		_won = won;
		RequestFlush();
	}

	public void LoadFromHistoricalRecords(IReadOnlyList<BetRecord> records)
	{
		if (records == null || records.Count <= 0)
		{
			ClearEntries();
			return;
		}

		BetRecord newest = records[records.Count - 1];
		_roll = newest.Roll;
		_won = newest.Outcome == BetOutcome.Win;
		if (IsVisibleInTree())
		{
			Flush();
		}
	}

	public void ClearEntries()
	{
		_roll = -1;
		if (IsVisibleInTree())
		{
			Flush();
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

	// A hidden view keeps its state; becoming visible paints it on that frame.
	private void OnVisibilityChanged()
	{
		if (IsVisibleInTree())
		{
			Flush();
		}
	}

	private void Flush()
	{
		if (_roll == _shownRoll && _won == _shownWon)
		{
			return;
		}

		_shownRoll = _roll;
		_shownWon = _won;
		if (_roll < 0)
		{
			Text = "-";
			Modulate = Colors.White;
			return;
		}

		Text = _roll.ToString("00", CultureInfo.InvariantCulture);
		Modulate = _won ? _winColor : _lossColor;
	}
}
