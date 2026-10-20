using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Godot;

namespace Scripts.Diagnostics
{
	/// <summary>
	/// Mini-plan 25 (Step 17.C.0) — DEBUG-only COUNT of how often each UI panel rebuilds itself, per real second.
	///
	/// <para><b>Why a count and not a time.</b> 17.C's exit criterion is stated in counts (plan §5.3): a panel at rest
	/// must do 0 refreshes a second, and under the sim no more than one per coalesce interval. A millisecond figure
	/// does not reproduce across sessions here (CLAUDE.md, Pattern 6 instrument rule 3); a count does. It also answers
	/// prediction P1 directly — whether a refresh bound to a per-bet event runs at the bet rate.</para>
	///
	/// <para><b>What it counts.</b> One <see cref="Count"/> call at the top of each polling panel's refresh entry point
	/// (the 17 panels of plan §0.2 groups B and C), so a refresh is counted whoever caused it — timer, event or button.
	/// <c>ClientsBetsHistory</c> carries a second call on its per-bet live feed, so that rate sits beside the rebuilds. And <see cref="CountMempoolAdd"/> at the three places a transaction ORIGINATES
	/// (<c>NetworkRoot</c>): D-17.1's mempool event needs its real rate before anyone subscribes to it (§38.7 rule 1).
	/// The broadcast fan-out into every other node's mempool is deliberately not counted — it would multiply each
	/// transaction by the node count.</para>
	///
	/// <para><b>How it reports.</b> Every <see cref="WindowSeconds"/> of real time, one line to the Godot editor's
	/// OUTPUT panel (<c>GD.Print</c>, which the engine also writes to <c>user://logs/godot.log</c>, where the run is
	/// read afterwards): the scene on screen, whether the sim is running and its retention averaged over the window
	/// (the Sim: NN% figure), and each site's rate. A window
	/// with nothing in it prints ONE explicit "no refreshes" line, then stays silent until something refreshes or the
	/// scene changes — so the passing state of the exit criterion is a printed line, never bare silence (CLAUDE.md,
	/// "Asking the developer to read output").</para>
	///
	/// <para><b>Off by default</b>, armed from the "UI refresh" toggle beside the DEV time selector, like the three cost
	/// profilers. Arm it before starting an autobet at a high DEV scale: the toggles are slow to answer a click
	/// while the frame is saturated. It stays armed across scene changes. In a release build every call compiles
	/// away. Its own cost (a dictionary increment per refresh, one timestamp read per frame) has not been timed.</para>
	/// </summary>
	public static class UiRefreshProbe
	{
		public const double WindowSeconds = 5.0;

		public static bool Enabled { get; private set; }

		private static readonly Dictionary<string, int> _counts = new();
		private static int _mempoolAdds;
		private static long _windowStartTs;
		private static string _lastReportedScene = string.Empty;
		private static bool _quietReported;
		private static SceneTree _tree;
		private static CalendarTimeService _calendar;
		private static double _throttleSum;
		private static int _throttleFrames;

		[Conditional("DEBUG")]
		public static void Arm(bool on)
		{
			if (on == Enabled) return;
			_tree ??= Engine.GetMainLoop() as SceneTree;
			if (_tree == null) return;

			_calendar ??= _tree.Root.GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
			Enabled = on;
			ResetWindow();
			_lastReportedScene = string.Empty;
			_quietReported = false;
			if (on)
			{
				_tree.ProcessFrame += OnProcessFrame;
				GD.Print(string.Create(CultureInfo.InvariantCulture,
					$"[UiRefresh] ARMED — one line per {WindowSeconds:0.#} s of real time in this panel while anything refreshes."));
			}
			else
			{
				_tree.ProcessFrame -= OnProcessFrame;
				GD.Print("[UiRefresh] disarmed.");
			}
		}

		/// <summary>One refresh of a panel. <paramref name="site"/> is "Scene.Method".</summary>
		[Conditional("DEBUG")]
		public static void Count(string site)
		{
			if (!Enabled) return;
			_counts.TryGetValue(site, out int n);
			_counts[site] = n + 1;
		}

		/// <summary>One NEW transaction accepted into its originator's mempool.</summary>
		[Conditional("DEBUG")]
		public static void CountMempoolAdd()
		{
			if (!Enabled) return;
			_mempoolAdds++;
		}

		private static void OnProcessFrame()
		{
			// The same figure the Sim: NN% readout shows, averaged over the window, so prediction P2 is read from the
			// log beside the counts instead of being transcribed off the screen.
			bool simRunning = _calendar?.IsAutobetActive == true;
			if (simRunning)
			{
				_throttleSum += Mathf.Clamp(_calendar.SimulationThrottle, 0.0, 1.0);
				_throttleFrames++;
			}

			double elapsed = (Stopwatch.GetTimestamp() - _windowStartTs) / (double)Stopwatch.Frequency;
			if (elapsed < WindowSeconds) return;

			string scene = _tree?.CurrentScene?.Name.ToString() ?? "?";
			bool sceneChanged = scene != _lastReportedScene;
			bool quiet = _counts.Count == 0 && _mempoolAdds == 0;

			if (quiet && _quietReported && !sceneChanged)
			{
				ResetWindow();
				return;
			}

			string sim = _throttleFrames > 0
				? string.Create(CultureInfo.InvariantCulture, $"sim RUNNING, retention {100.0 * _throttleSum / _throttleFrames:0}%")
				: "sim stopped";
			var line = new StringBuilder();
			line.Append(string.Create(CultureInfo.InvariantCulture, $"[UiRefresh] {elapsed:0.0} s in {scene} | {sim} | "));

			if (_counts.Count == 0)
			{
				line.Append("no refreshes");
			}
			else
			{
				line.Append(string.Join(" · ", _counts
					.OrderByDescending(kv => kv.Value)
					.Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key} {kv.Value / elapsed:0.0}/s"))));
			}

			line.Append(string.Create(CultureInfo.InvariantCulture, $" | mempool adds {_mempoolAdds / elapsed:0.0}/s"));
			if (quiet) line.Append(" (silent from here until something refreshes or the scene changes)");
			GD.Print(line.ToString());

			_quietReported = quiet;
			_lastReportedScene = scene;
			ResetWindow();
		}

		private static void ResetWindow()
		{
			_counts.Clear();
			_mempoolAdds = 0;
			_throttleSum = 0;
			_throttleFrames = 0;
			_windowStartTs = Stopwatch.GetTimestamp();
		}
	}
}
