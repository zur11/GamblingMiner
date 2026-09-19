namespace Scripts.Diagnostics
{
	/// <summary>
	/// Which bet view DiceGame is showing, recorded so every FrameCostProfiler report says which of the three it
	/// was measured under (mini-plan 10 A3). DiceGame reports it on each switch; nothing reads it but the trace.
	///
	/// History: this began as A1's DEBUG-only "Bet UI" picker (Full / NoReorder / Off), a measurement switch for
	/// the old per-bet list path. A1 was measured, A2 replaced that path — rows no longer move, so NoReorder
	/// stopped meaning anything — and the picker was deleted rather than kept alive behind a flag. The player's
	/// Bet View button now does what its Off mode did.
	/// </summary>
	public static class BetUiDiagnostics
	{
		/// <summary>The bet view in force, as DiceGame names it ("Detailed", "Numbers", "Off").</summary>
		public static string View { get; private set; } = "Detailed";

		public static void ReportView(string view)
		{
			View = view;
		}
	}
}
