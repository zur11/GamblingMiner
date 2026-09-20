using Godot;

namespace Scripts.Diagnostics
{
	/// <summary>Which of a Numbers cell's two writes is performed — the split A3 run 2 asked for.</summary>
	public enum WinnerCellWrite
	{
		/// <summary>The number's text and the cell's background colour: what the game does.</summary>
		Both = 0,
		/// <summary>Only the number's text. Colours go stale on purpose.</summary>
		TextOnly,
		/// <summary>Only the background colour. Numbers go stale on purpose.</summary>
		ColourOnly,

		/// <summary>
		/// Neither — the grid still flushes, the cells still exist and are still visible, but nothing is written
		/// to them. The baseline run 3 lacked: splitting the two writes could not separate them, because each
		/// one queues the SAME redraw of the whole cell. This is what says whether the redraw is the cost.
		/// </summary>
		None,
	}

	/// <summary>
	/// Mini-plan 10 A3 run 3 — a DEBUG measurement switch for the Numbers grid's remaining cost.
	///
	/// D-10.2 removed the per-bet <c>MoveChild</c> and took the view's in-loop cost down to Detailed's
	/// 0.025 ms per bet, but the frame stayed at ~26 fps: 20–45 ms per dirty frame OUTSIDE the simulation,
	/// independent of the bets in it. That is ~0.35 ms per cell, which is far too much for a two-digit
	/// number, and a cell performs exactly two writes. This says which one costs — the same method A1 used
	/// on the two lists, one level down.
	/// </summary>
	public static class WinnerCellDiagnostics
	{
		public static WinnerCellWrite Mode { get; private set; } = WinnerCellWrite.Both;

		[System.Diagnostics.Conditional("DEBUG")]
		public static void SetMode(WinnerCellWrite mode)
		{
			if (mode == Mode)
			{
				return;
			}

			Mode = mode;
			GD.Print($"[WinnerCell] cell writes are now {mode} — DEBUG measurement switch, not persisted. "
				+ "Cells already on screen keep whatever they last showed.");
		}
	}
}
