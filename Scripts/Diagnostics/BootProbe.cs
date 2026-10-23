using System.Collections.Generic;
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
	/// passed before any script ran) and since the previous mark. Whatever the developer feels beyond the last figure
	/// happened before the engine started: the editor building the C# project after a code change, and launching the
	/// game.
	///
	/// Mini-plan 28 unit 1 — mini-plan 27's marks left 95% of the boot inside autoloads #2–#19 untimed, so:
	/// <list type="bullet">
	/// <item>every autoload marks the start of its _Ready (<see cref="AutoloadStarts"/>). Godot readies them one after
	/// another in registration order, so the start of #N+1 is the end of #N: 20 marks time 19 spans, and #20's span
	/// ends at its own "done" mark;</item>
	/// <item>NetworkRoot.EnsureInitialized marks each of its steps with the counts beside them ("world: …");</item>
	/// <item>every <c>BlockchainService.ChainIsValid</c> call is counted and timed (<see cref="NoteChainValidation"/>),
	/// and <see cref="PrintChainValidationSummary"/> prints calls, total and min / median / max once the world has
	/// loaded. It counts every call from every caller, not only the boot loop's, so a call from elsewhere shows up.</item>
	/// </list>
	///
	/// Printed with GD.Print, so godot.log receives it. In a release build every call compiles away, arguments
	/// included.
	/// </summary>
	public static class BootProbe
	{
		private static ulong _lastUsec;

		// One entry per ChainIsValid call, in ms. ~73 at boot; nothing is appended outside DEBUG.
		private static readonly List<double> _chainValidationMs = new();

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

		/// <summary>The first line of autoload #<paramref name="index"/>'s _Ready, the number being its position in
		/// project.godot's [autoload] block.</summary>
		[Conditional("DEBUG")]
		public static void AutoloadStarts(int index, string name) => Mark($"autoload #{index} ({name}) starts");

		/// <summary>
		/// One ChainIsValid call. <paramref name="startTicks"/> is a <see cref="Stopwatch.GetTimestamp"/> taken before
		/// the validation began; the caller reports after it returned (BlockCostProfiler's contract).
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteChainValidation(long startTicks)
		{
			_chainValidationMs.Add((Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency);
		}

		/// <summary>Calls, total and min / median / max per call of every ChainIsValid so far in this process.</summary>
		[Conditional("DEBUG")]
		public static void PrintChainValidationSummary(string when)
		{
			int calls = _chainValidationMs.Count;
			if (calls == 0)
			{
				GD.Print($"[Boot] ChainIsValid {when}: 0 calls");
				return;
			}

			var sorted = new List<double>(_chainValidationMs);
			sorted.Sort();
			double total = 0;
			foreach (double ms in sorted) total += ms;
			double median = calls % 2 == 1
				? sorted[calls / 2]
				: (sorted[calls / 2 - 1] + sorted[calls / 2]) / 2.0;

			GD.Print(string.Create(CultureInfo.InvariantCulture,
				$"[Boot] ChainIsValid {when}: {calls} calls, {total:N1} ms total, per call min {sorted[0]:N1} / median {median:N1} / max {sorted[^1]:N1} ms"));
		}
	}
}
