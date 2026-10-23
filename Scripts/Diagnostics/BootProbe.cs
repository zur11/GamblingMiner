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

		// Mini-plan 28 unit 2 — the wallet rescan, split: its one chain pass, then each wallet's derivations.
		private static double _rescanChainPassMs;
		private static int _rescanUsedAddresses;
		private static int _rescanWallets;
		private static int _rescanDerivations;
		private static double _rescanWalletMs;
		private static string _largestWalletNodeId = string.Empty;
		private static int _largestWalletDerivations;
		private static double _largestWalletMs;

#if DEBUG
		/// <summary>
		/// Mini-plan 28 E3 — when true, the boot ALSO runs the pre-fix paths after the new ones (73 extra ChainIsValid
		/// calls, and every derived address re-derived by the old scalar multiply) and prints both from the same
		/// process. About a minute longer, once. Deleted at close-out with its output and the old-path references
		/// (Convention 3).
		/// </summary>
		public const bool CompareOldPathsAtBoot = true;

		/// <summary>Total ms of every ChainIsValid so far, for E3's "new path" figure.</summary>
		public static double ChainValidationTotalMs
		{
			get
			{
				double total = 0;
				foreach (double ms in _chainValidationMs) total += ms;
				return total;
			}
		}

		public static int ChainValidationCalls => _chainValidationMs.Count;

		/// <summary>The rescan's own derivations and their ms, for E3's per-derivation comparison.</summary>
		public static (int derivations, double ms) RescanDerivationTotals => (_rescanDerivations, _rescanWalletMs);
#endif

		private static double MsSince(long startTicks) => (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;

		/// <summary>The rescan's one pass over the chain collecting every used address (BuildUsedAddressSet).</summary>
		[Conditional("DEBUG")]
		public static void NoteRescanChainPass(long startTicks, int usedAddresses)
		{
			_rescanChainPassMs += MsSince(startTicks);
			_rescanUsedAddresses = usedAddresses;
		}

		/// <summary>One wallet's rescan: how many addresses it derived (each a secp256k1 multiply) and its ms.</summary>
		[Conditional("DEBUG")]
		public static void NoteWalletRescan(string nodeId, int derivations, long startTicks)
		{
			double ms = MsSince(startTicks);
			_rescanWallets++;
			_rescanDerivations += derivations;
			_rescanWalletMs += ms;
			if (derivations > _largestWalletDerivations)
			{
				_largestWalletNodeId = nodeId;
				_largestWalletDerivations = derivations;
				_largestWalletMs = ms;
			}
		}

		[Conditional("DEBUG")]
		public static void PrintRescanSummary()
		{
			double total = _rescanChainPassMs + _rescanWalletMs;
			double chainShare = total > 0 ? _rescanChainPassMs / total : 0;
			double perDerivation = _rescanDerivations > 0 ? _rescanWalletMs / _rescanDerivations : 0;
			double largestShare = _rescanDerivations > 0 ? (double)_largestWalletDerivations / _rescanDerivations : 0;
			GD.Print(string.Create(CultureInfo.InvariantCulture,
				$"[Boot] Rescan: chain pass {_rescanChainPassMs:N1} ms ({chainShare:P1} of the rescan, {_rescanUsedAddresses} used addresses); " +
				$"{_rescanWallets} wallets derived {_rescanDerivations} addresses in {_rescanWalletMs:N1} ms ({perDerivation:N3} ms each); " +
				$"largest wallet {_largestWalletNodeId}: {_largestWalletDerivations} derivations ({largestShare:P1}), {_largestWalletMs:N1} ms"));
		}

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
