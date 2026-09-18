using Godot;

namespace Scripts.Diagnostics
{
	/// <summary>How DiceGame's two per-bet lists react to a settled bet — a DEV measurement switch.</summary>
	public enum BetUiMode
	{
		/// <summary>Today's behaviour: Setup one pooled row, then MoveChild it to index 0.</summary>
		Full = 0,
		/// <summary>Setup only; rows stay where they are, so the list shows the wrong order. Isolates what the
		/// reorder (and the layout it invalidates) costs.</summary>
		NoReorder,
		/// <summary>Neither list reacts at all. Isolates the whole per-bet UI cost.</summary>
		Off,
	}

	/// <summary>
	/// Mini-plan 10 A1 — attributes DiceGame's per-bet UI cost by switching what its two per-bet lists
	/// (<c>BetHistoryContainer</c>, <c>PreviousWinnerNumbersGrid</c>) do on each settled bet, mid-run, so the three
	/// modes can be compared inside one session (mini-plan 08 P1g: between sessions the spread is up to 34%).
	///
	/// <para>DEBUG only: the setter is Conditional, so a RELEASE build always reads <see cref="BetUiMode.Full"/>.
	/// Only the LIVE per-bet path consults it — the lists' historical loaders and BetsHistoryExplorer's replay are
	/// untouched, so no persisted or replayed view can ever be affected.</para>
	/// </summary>
	public static class BetUiDiagnostics
	{
		public static BetUiMode Mode { get; private set; } = BetUiMode.Full;

		[System.Diagnostics.Conditional("DEBUG")]
		public static void SetMode(BetUiMode mode)
		{
			if (mode == Mode)
			{
				return;
			}

			Mode = mode;
			GD.Print($"[BetUi] per-bet list mode is now {mode} — DEBUG measurement switch, not persisted.");
		}
	}
}
