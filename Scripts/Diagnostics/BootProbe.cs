using System.Diagnostics;
using System.Globalization;
using Godot;

namespace Scripts.Diagnostics
{
	/// <summary>
	/// Mini-plan 27 — DEBUG-only timing of the boot, from the engine's start to MainMenu's first _Ready. The developer
	/// reported (2026-10-22) that launching had grown to "almost half a minute, more than double", during this plan's
	/// verification run, and nothing measured the boot: the scene-entry probe starts at a button press. This splits it
	/// before anything is changed (§40.7: time it).
	///
	/// Each mark prints the time since the ENGINE started (Godot's own clock, so the first mark already shows what
	/// passed before any script ran) and since the previous mark. Six marks: WorldGuardService's start and end (the
	/// first autoload, and the only one this plan added work to), UserSettingsService's start and end (the last
	/// autoload, new in this plan), and MainMenu's _Ready. Whatever the developer feels beyond the last figure happened
	/// before the engine started: the editor building the C# project after a code change, and launching the game.
	///
	/// Printed with GD.Print, so godot.log receives it. In a release build every call compiles away.
	/// </summary>
	public static class BootProbe
	{
		private static ulong _lastUsec;

		[Conditional("DEBUG")]
		public static void Mark(string label)
		{
			ulong now = Time.GetTicksUsec();
			double sinceStartMs = now / 1000.0;
			double sincePreviousMs = _lastUsec == 0 ? sinceStartMs : (now - _lastUsec) / 1000.0;
			_lastUsec = now;
			GD.Print(string.Create(CultureInfo.InvariantCulture,
				$"[Boot] {label}: {sinceStartMs:N1} ms since engine start (+{sincePreviousMs:N1} ms)"));
		}
	}
}
