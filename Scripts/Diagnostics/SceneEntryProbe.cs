using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Scripts.Diagnostics
{
	/// <summary>
	/// Mini-plan 21 Part E — DEBUG-only timing of one scene entry, from the button press to the first frame the new
	/// scene processes. The developer measured "at least one second" before DiceGame appears, at 100X and at 9000X
	/// alike, so it is a load cost, not a simulation one. This splits it before anything is changed (§40.7: time it).
	///
	/// The stages, in the order Godot runs them:
	///   load    — SceneManager.Go reading the .tscn (GD.Load, which is what ChangeSceneToFile did inside);
	///   toReady — from there to the scene root's _Ready: the deferred swap, the old scene's exit, instantiation, and
	///             EVERY CHILD's _Ready, which Godot runs before its parent's;
	///   ready   — the root's own _Ready, split by Mark() calls;
	///   toFrame — from the end of _Ready to the root's first _Process.
	/// Printed once per entry with GD.Print, to the Godot editor's OUTPUT panel. In a release build every call
	/// compiles away.
	/// </summary>
	public static class SceneEntryProbe
	{
		private static string _scene;
		private static long _requestTs;
		private static long _loadedTs;
		private static long _readyStartTs;
		private static long _lastMarkTs;
		private static long _readyEndTs;
		private static readonly StringBuilder _marks = new();
		private static bool _pending;

		private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

		private static string F(double ms) => ms.ToString("0.0", CultureInfo.InvariantCulture);

		[Conditional("DEBUG")]
		public static void BeginRequest(string scene)
		{
			_scene = scene;
			_requestTs = Stopwatch.GetTimestamp();
			_loadedTs = 0;
			_readyStartTs = 0;
			_readyEndTs = 0;
			_marks.Clear();
			_pending = true;
		}

		[Conditional("DEBUG")]
		public static void NoteLoaded()
		{
			if (_pending) _loadedTs = Stopwatch.GetTimestamp();
		}

		[Conditional("DEBUG")]
		public static void ReadyStarted()
		{
			if (!_pending) return;
			_readyStartTs = Stopwatch.GetTimestamp();
			_lastMarkTs = _readyStartTs;
		}

		/// <summary>Closes the stage that started at the previous mark (or at ReadyStarted).</summary>
		[Conditional("DEBUG")]
		public static void Mark(string stage)
		{
			if (!_pending || _readyStartTs == 0) return;
			long now = Stopwatch.GetTimestamp();
			if (_marks.Length > 0) _marks.Append(", ");
			_marks.Append(stage).Append(' ').Append(F(Ms(_lastMarkTs, now)));
			_lastMarkTs = now;
		}

		[Conditional("DEBUG")]
		public static void ReadyEnded()
		{
			if (_pending && _readyStartTs != 0) _readyEndTs = Stopwatch.GetTimestamp();
		}

		/// <summary>Call from the scene root's _Process; reports once, on the first frame after _Ready.</summary>
		[Conditional("DEBUG")]
		public static void FirstProcess()
		{
			if (!_pending || _readyEndTs == 0) return;
			_pending = false;
			long now = Stopwatch.GetTimestamp();
			long loaded = _loadedTs != 0 ? _loadedTs : _requestTs;
			Godot.GD.Print("[SceneEntry] " + _scene
				+ ": total " + F(Ms(_requestTs, now)) + " ms"
				+ " | load .tscn " + F(Ms(_requestTs, loaded))
				+ " | toReady " + F(Ms(loaded, _readyStartTs)) + " (swap, instantiate, children's _Ready)"
				+ " | _Ready " + F(Ms(_readyStartTs, _readyEndTs)) + " (" + _marks + ")"
				+ " | toFirstFrame " + F(Ms(_readyEndTs, now)));
		}
	}
}
