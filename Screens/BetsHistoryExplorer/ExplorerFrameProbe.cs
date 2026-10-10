using System.Diagnostics;

/// <summary>
/// Mini-plan 20 F — DEBUG-only stage timing for BetsHistoryExplorer's frame. The explorer measured ~59 fps idle,
/// ~41 at 10 rows/s and ~28 at 99–700 rows/s: a cost paid per frame that SHOWS a row, not per bet, and absent in
/// DiceGame at the same row rate. This names the stage that pays it.
///
/// Two of the stages run in OTHER nodes' _Process (the bet list and the winning-numbers grid flush on their own),
/// so they add into these static accumulators; the explorer reads and resets them once per [ExplorerPerf] window.
/// Both classes are shared with DiceGame, where the accumulators simply grow unread — and in a release build every
/// call compiles away.
/// </summary>
public static class ExplorerFrameProbe
{
	public static long RowFlushTicks;
	public static long GridFlushTicks;

	public static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

	[Conditional("DEBUG")]
	public static void AddRowFlush(long startTimestamp) =>
		RowFlushTicks += Stopwatch.GetTimestamp() - startTimestamp;

	[Conditional("DEBUG")]
	public static void AddGridFlush(long startTimestamp) =>
		GridFlushTicks += Stopwatch.GetTimestamp() - startTimestamp;

	public static void Reset()
	{
		RowFlushTicks = 0;
		GridFlushTicks = 0;
	}
}
