using Godot;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Scripts.Diagnostics
{
	/// <summary>
	/// Mini-plan 09 P1 — times the WHOLE FRAME while the background simulation runs, not one bet.
	///
	/// <para><b>Why a second profiler.</b> <see cref="BetCostProfiler"/> is scoped to the player's bet. At 99
	/// credits × 3000X mini-plan 08 could only DERIVE that the frame ran at ≈51 fps with the bets taking
	/// ~6.8 ms of ~19.5 ms — the other ~65% was outside every instrument. Whether the ~2,000 bets/s ceiling is
	/// the per-frame cap or the CPU turns on that unmeasured share, and so does the budget the DevTimeScale
	/// governor will be built on. This measures it.</para>
	///
	/// <para><b>What a frame is here.</b> The period between two consecutive <see cref="BeginFrame"/> calls,
	/// by <see cref="Stopwatch"/>. That period contains this frame's simulation AND everything else the engine
	/// did before the next one — rendering, UI, other nodes' <c>_Process</c> — which is exactly the part the
	/// bet profiler cannot see. A frame is therefore recorded one call late, when its period is known. Godot's
	/// <c>delta</c> is not used for this: it describes the PREVIOUS frame, so it would pair each frame's sim
	/// time with its neighbour's duration.</para>
	///
	/// <para><b>Answers four pre-registered hypotheses</b> (mini-plan 09 §3 P1), each printed as its own line
	/// so a report reads as a verdict rather than a table: H1 the sim's share of the frame, H2 how often
	/// <c>MaxBetsPerFrame</c> binds, H3 founder + scheduled PoW attempts per player/bot bet, H4 whether every
	/// frame over 50 ms carries a checkpoint or a GC.</para>
	///
	/// <para><b>Off by default, DEBUG only</b>, armed from the toggle beside the DEV time selector — the same
	/// contract as <see cref="BetCostProfiler"/>, and for the same reasons. Its own overhead is a handful of
	/// <see cref="Stopwatch.GetTimestamp"/> calls and GC counter reads per frame; that has <b>not been
	/// timed</b>, and is stated here as expected to be far below a millisecond rather than as a
	/// measurement (CLAUDE.md, Pattern 6's closing rule).</para>
	/// </summary>
	public static class FrameCostProfiler
	{
		/// <summary>Contiguous stages of <c>SimulationService._Process</c>, in the order they run.</summary>
		public enum Segment
		{
			/// <summary>Mining-power totals, plus the founder and population recomputes (real work only on a
			/// new block).</summary>
			Recompute = 0,
			/// <summary>The session-restart check, batch planning and the player's settle loop.</summary>
			PlayerLoop,
			/// <summary>TickBots — every running bot runner's own settle loop.</summary>
			BotLoop,
			/// <summary>DriveFounderMining — the founders' drained PoW attempts, and any checkpoint they cause.</summary>
			FounderDrive,
			/// <summary>DriveScheduledMining — the cast's and the invisible mass's drained PoW attempts.</summary>
			ScheduledDrive,
			/// <summary>The retention throttle, the saturation accumulator and the back-dating anchor.</summary>
			Tail,
		}

		private const int SegmentCount = 6;

		private static readonly string[] SegmentNames =
		{
			"Recompute (power totals; founders/population on a new block)",
			"PlayerLoop (restart check + plan + player settle loop)",
			"BotLoop (TickBots)",
			"FounderDrive (founder PoW attempts + their checkpoints)",
			"ScheduledDrive (cast + invisible-mass PoW attempts)",
			"Tail (retention throttle + trace accumulation)",
		};

		public const string TracePath = "user://logs/frame_cost_trace.csv";

		// ~10 seconds at 60 fps. Denominated in frames because frames are what this measures; long enough for a
		// p95 to mean something (30 frames above it), short enough to land several times inside one credit
		// step of mini-plan 09's P3 sweep. PUBLIC because the toggle quotes it (Standing Convention 15).
		public const int ReportEveryFrames = 600;

		private const double FrameBudgetMs = 1000.0 / 60.0;

		// A period longer than this is a discontinuity — the sim stopped, the game paused, a scene loaded — not a
		// frame. Recording it would put one enormous "frame" into the percentiles.
		private const double DiscontinuityMs = 1000.0;

		private const string Header =
			"reportUtc,frames,partial,fps,periodP50Ms,periodP95Ms,periodMaxMs,framesOver16Ms,framesOver33Ms,framesOver50Ms," +
			"simP50Ms,simP95Ms,simMaxMs,simShare,recomputeMs,playerLoopMs,botLoopMs,founderDriveMs,scheduledDriveMs,tailMs," +
			"unaccountedMs,playerBetsPerFrame,capBoundShare,botBetsPerFrame,founderAttemptsPerFrame,scheduledAttemptsPerFrame," +
			"deliveredBetsPerSec,demandBetsPerSec,retentionMean,checkpoints,over50WithCheckpointOrGc,gcFrames,capPerFrame," +
			"calendarAdvanceGameSec,retainedGameSec,overspend,betUiMode,budgetInForce";

		// ── Committed frames of the current report window (flat arrays: no allocation per frame) ──────────
		private static readonly double[] _periodMs = new double[ReportEveryFrames];
		private static readonly double[] _simMs = new double[ReportEveryFrames];
		private static readonly double[] _segMs = new double[ReportEveryFrames * SegmentCount];
		private static readonly int[] _playerBets = new int[ReportEveryFrames];
		private static readonly int[] _botBets = new int[ReportEveryFrames];
		private static readonly int[] _founderAttempts = new int[ReportEveryFrames];
		private static readonly int[] _scheduledAttempts = new int[ReportEveryFrames];
		private static readonly bool[] _capBound = new bool[ReportEveryFrames];
		private static readonly int[] _checkpoints = new int[ReportEveryFrames];
		private static readonly bool[] _gc = new bool[ReportEveryFrames];
		private static readonly double[] _demand = new double[ReportEveryFrames];
		private static readonly double[] _retention = new double[ReportEveryFrames];
		// Mini-plan 10 B1 — R2-C1's two sides per frame; a negative advance marks a frame with no valid anchor.
		private static readonly double[] _calendarAdvance = new double[ReportEveryFrames];
		private static readonly double[] _retainedGame = new double[ReportEveryFrames];
		private static int _count;

		// ── The frame in progress ─────────────────────────────────────────────────────────────────────────
		private static bool _inFrame;
		private static long _curBegin;
		private static int _curGc0;
		private static readonly long[] _curSegTicks = new long[SegmentCount];
		private static int _openSegment = -1;
		private static long _openSince;
		private static int _curPlayerBets, _curBotBets, _curFounderAttempts, _curScheduledAttempts, _curCheckpoints;
		private static bool _curCapBound;

		// ── The frame waiting for its period (recorded when the NEXT frame begins) ───────────────────────
		private static bool _pendingValid;
		private static long _pendingBegin;
		private static int _pendingGc0;
		private static long _pendingSimTicks;
		private static readonly long[] _pendingSegTicks = new long[SegmentCount];
		private static int _pendingPlayerBets, _pendingBotBets, _pendingFounderAttempts, _pendingScheduledAttempts, _pendingCheckpoints;
		private static bool _pendingCapBound;
		private static double _pendingDemand, _pendingRetention, _pendingCalendarAdvance, _pendingRetainedGame;

		private static bool _headerChecked;

		/// <summary>Armed state. False by default.</summary>
		public static bool Enabled { get; private set; }

		/// <summary>
		/// Reports written since the profiler was last armed. A test protocol is counted in reports ("three per
		/// leg"), and counting them by watching the Godot editor's Output panel scroll is the part of the protocol
		/// the developer cannot do while playing — so the count, and only the count, is readable from inside the
		/// game. The report CONTENT stays in the Output panel and in the CSV, where it is read afterwards.
		/// </summary>
		public static int ReportCount { get; private set; }

		/// <summary>Fires after each report is written, so a DEV readout can show <see cref="ReportCount"/>.</summary>
		public static event Action ReportPublished;

		/// <summary>
		/// Turn measurement on or off, announcing the transition in the Godot editor's <b>Output</b> panel
		/// (GD.Print, never GD.PrintErr). Disarming flushes a partial window: a run that ends off-schedule is
		/// still data (BetCostProfiler's round-2 lesson).
		/// </summary>
		[Conditional("DEBUG")]
		public static void Arm(bool enabled)
		{
			if (Enabled == enabled)
			{
				return;
			}

			if (!enabled && _count > 0)
			{
				Report(partial: true);
			}

			Enabled = enabled;
			// Counted per RUN, not per process: the protocol counts reports since arming.
			ReportCount = 0;
			ReportPublished?.Invoke();
			ResetWindow();
			_pendingValid = false;
			_inFrame = false;

			if (enabled)
			{
				GD.Print(string.Create(CultureInfo.InvariantCulture,
					$"[FrameCost] ARMED — a whole-frame report prints every {ReportEveryFrames:N0} simulated frames " +
					$"(~10 s at 60 fps) while an autobet runs, and a partial one when you disarm. Output panel plus {TracePath}."));
			}
			else
			{
				GD.Print("[FrameCost] disarmed — frames are no longer being timed.");
			}
		}

		private static void ResetWindow()
		{
			_count = 0;
		}

		/// <summary>Top of a simulated frame. Commits the previous frame now that its period is known.</summary>
		[Conditional("DEBUG")]
		public static void BeginFrame()
		{
			if (!Enabled) return;

			long now = Stopwatch.GetTimestamp();
			int gc0 = GC.CollectionCount(0);

			if (_pendingValid)
			{
				double periodMs = TicksToMs(now - _pendingBegin);
				if (periodMs <= DiscontinuityMs)
				{
					Commit(periodMs, gc0 != _pendingGc0);
				}

				_pendingValid = false;
			}

			// The new frame starts counting AFTER the commit, not at `now`. Every ReportEveryFrames frames that
			// commit runs Report() — percentile sorts, a print, a CSV append — and timing from `now` would bill
			// that to the next frame, manufacturing a spike on a fixed cadence that H4 would then try to
			// explain. The report's own cost is therefore measured by nothing, which is stated rather than hidden.
			_inFrame = true;
			_curBegin = Stopwatch.GetTimestamp();
			_curGc0 = gc0;
			Array.Clear(_curSegTicks, 0, SegmentCount);
			_openSegment = -1;
			_curPlayerBets = _curBotBets = _curFounderAttempts = _curScheduledAttempts = _curCheckpoints = 0;
			_curCapBound = false;
		}

		/// <summary>Closes the open segment (if any) and opens <paramref name="segment"/>.</summary>
		[Conditional("DEBUG")]
		public static void Enter(Segment segment)
		{
			if (!Enabled || !_inFrame) return;
			long now = Stopwatch.GetTimestamp();
			CloseOpenSegment(now);
			_openSegment = (int)segment;
			_openSince = now;
		}

		private static void CloseOpenSegment(long now)
		{
			if (_openSegment >= 0)
			{
				_curSegTicks[_openSegment] += now - _openSince;
				_openSegment = -1;
			}
		}

		[Conditional("DEBUG")]
		public static void CountPlayerBets(int executed, bool capBound)
		{
			if (!Enabled || !_inFrame) return;
			_curPlayerBets += executed;
			_curCapBound |= capBound;
		}

		[Conditional("DEBUG")]
		public static void CountBotBets(int executed)
		{
			if (!Enabled || !_inFrame) return;
			_curBotBets += executed;
		}

		[Conditional("DEBUG")]
		public static void CountFounderAttempts(int attempts)
		{
			if (!Enabled || !_inFrame) return;
			_curFounderAttempts += attempts;
		}

		[Conditional("DEBUG")]
		public static void CountScheduledAttempts(int attempts)
		{
			if (!Enabled || !_inFrame) return;
			_curScheduledAttempts += attempts;
		}

		/// <summary>One block checkpoint captured inside this frame, by whichever segment is open.</summary>
		[Conditional("DEBUG")]
		public static void CountCheckpoint()
		{
			if (!Enabled || !_inFrame) return;
			_curCheckpoints++;
		}

		/// <summary>
		/// Discards the frame in progress — for the early return where the autobet stops itself. The previous
		/// frame was already committed by this frame's <see cref="BeginFrame"/>, so nothing else is lost.
		/// </summary>
		[Conditional("DEBUG")]
		public static void AbortFrame()
		{
			_inFrame = false;
			_openSegment = -1;
		}

		/// <summary>
		/// Bottom of a simulated frame. The frame is held until the next <see cref="BeginFrame"/> supplies its
		/// period. <paramref name="demandBetsPerSecond"/> is what the running engines asked for this frame
		/// (their credits × DevTimeScale), so delivered vs demanded can be read off the same report.
		/// <paramref name="calendarAdvanceGameSeconds"/> and <paramref name="retainedGameSeconds"/> are R2-C1's two
		/// sides (mini-plan 10 B1): how far the clock moved this frame, and how much simulated time the engines
		/// actually retained, in game-seconds. A negative advance marks a frame with no valid anchor; it is skipped.
		/// </summary>
		[Conditional("DEBUG")]
		public static void EndFrame(double demandBetsPerSecond, double retention,
			double calendarAdvanceGameSeconds, double retainedGameSeconds)
		{
			if (!Enabled || !_inFrame) return;
			long now = Stopwatch.GetTimestamp();
			CloseOpenSegment(now);
			_inFrame = false;

			_pendingValid = true;
			_pendingBegin = _curBegin;
			_pendingGc0 = _curGc0;
			_pendingSimTicks = now - _curBegin;
			Array.Copy(_curSegTicks, _pendingSegTicks, SegmentCount);
			_pendingPlayerBets = _curPlayerBets;
			_pendingBotBets = _curBotBets;
			_pendingFounderAttempts = _curFounderAttempts;
			_pendingScheduledAttempts = _curScheduledAttempts;
			_pendingCheckpoints = _curCheckpoints;
			_pendingCapBound = _curCapBound;
			_pendingDemand = demandBetsPerSecond;
			_pendingRetention = retention;
			_pendingCalendarAdvance = calendarAdvanceGameSeconds;
			_pendingRetainedGame = retainedGameSeconds;
		}

		private static void Commit(double periodMs, bool gcDuringFrame)
		{
			int i = _count;
			_periodMs[i] = periodMs;
			_simMs[i] = TicksToMs(_pendingSimTicks);
			for (int s = 0; s < SegmentCount; s++)
			{
				_segMs[i * SegmentCount + s] = TicksToMs(_pendingSegTicks[s]);
			}
			_playerBets[i] = _pendingPlayerBets;
			_botBets[i] = _pendingBotBets;
			_founderAttempts[i] = _pendingFounderAttempts;
			_scheduledAttempts[i] = _pendingScheduledAttempts;
			_capBound[i] = _pendingCapBound;
			_checkpoints[i] = _pendingCheckpoints;
			_gc[i] = gcDuringFrame;
			_demand[i] = _pendingDemand;
			_retention[i] = _pendingRetention;
			_calendarAdvance[i] = _pendingCalendarAdvance;
			_retainedGame[i] = _pendingRetainedGame;

			if (++_count >= ReportEveryFrames)
			{
				Report();
				ResetWindow();
			}
		}

		private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

		private static double Percentile(double[] source, int n, double p)
		{
			if (n <= 0) return 0d;
			var sorted = new double[n];
			Array.Copy(source, sorted, n);
			Array.Sort(sorted);
			int index = (int)Math.Ceiling(p * n) - 1;
			return sorted[Math.Clamp(index, 0, n - 1)];
		}

		private static void Report(bool partial = false)
		{
			int n = _count;
			if (n <= 0) return;

			double sumPeriod = 0d, sumSim = 0d, maxPeriod = 0d, maxSim = 0d, sumDemand = 0d, sumRetention = 0d;
			double sumUnaccounted = 0d;
			var segSum = new double[SegmentCount];
			long sumPlayer = 0, sumBot = 0, sumFounder = 0, sumScheduled = 0;
			int capBound = 0, checkpoints = 0, over16 = 0, over33 = 0, over50 = 0, over50Explained = 0, gcFrames = 0;
			int worst = 0;

			for (int i = 0; i < n; i++)
			{
				double period = _periodMs[i];
				sumPeriod += period;
				sumSim += _simMs[i];
				if (period > maxPeriod) { maxPeriod = period; worst = i; }
				if (_simMs[i] > maxSim) maxSim = _simMs[i];

				double segTotal = 0d;
				for (int s = 0; s < SegmentCount; s++)
				{
					double ms = _segMs[i * SegmentCount + s];
					segSum[s] += ms;
					segTotal += ms;
				}
				sumUnaccounted += _simMs[i] - segTotal;

				sumPlayer += _playerBets[i];
				sumBot += _botBets[i];
				sumFounder += _founderAttempts[i];
				sumScheduled += _scheduledAttempts[i];
				if (_capBound[i]) capBound++;
				checkpoints += _checkpoints[i];
				if (_gc[i]) gcFrames++;
				sumDemand += _demand[i];
				sumRetention += _retention[i];

				if (period > FrameBudgetMs) over16++;
				if (period > 2 * FrameBudgetMs) over33++;
				if (period > 50.0)
				{
					over50++;
					if (_checkpoints[i] > 0 || _gc[i]) over50Explained++;
				}
			}

			double seconds = sumPeriod / 1000.0;
			double fps = seconds > 0d ? n / seconds : 0d;
			double simShare = sumPeriod > 0d ? sumSim / sumPeriod : 0d;
			double outsideMeanMs = (sumPeriod - sumSim) / n;
			double capShare = (double)capBound / n;
			long bets = sumPlayer + sumBot;
			double attemptsPerBet = bets > 0 ? (double)(sumFounder + sumScheduled) / bets : 0d;
			double delivered = seconds > 0d ? bets / seconds : 0d;
			double demand = sumDemand / n;
			double retention = sumRetention / n;

			// Mini-plan 10 B1 — R2-C1's overspend over this window: how much further the clock moved than the time
			// the engines retained. Frames without a valid anchor (a run's first, a rewind onto a block) are skipped
			// on BOTH sides, so neither sum carries a frame the other lacks.
			double sumCalendarAdvance = 0d, sumRetainedGame = 0d;
			for (int i = 0; i < n; i++)
			{
				if (_calendarAdvance[i] < 0d) continue;
				sumCalendarAdvance += _calendarAdvance[i];
				sumRetainedGame += _retainedGame[i];
			}
			double overspend = sumRetainedGame > 0d ? sumCalendarAdvance / sumRetainedGame - 1d : 0d;

			int worstLargestSeg = 0;
			for (int s = 1; s < SegmentCount; s++)
			{
				if (_segMs[worst * SegmentCount + s] > _segMs[worst * SegmentCount + worstLargestSeg]) worstLargestSeg = s;
			}

			double p50Period = Percentile(_periodMs, n, 0.50), p95Period = Percentile(_periodMs, n, 0.95);
			double p50Sim = Percentile(_simMs, n, 0.50), p95Sim = Percentile(_simMs, n, 0.95);

			var sb = new StringBuilder();
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"[FrameCost]{(partial ? " PARTIAL WINDOW —" : "")} {n:N0} simulated frames — {fps:N1} fps · frame p50 {p50Period:N2} ms, p95 {p95Period:N2} ms, max {maxPeriod:N1} ms\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           sim p50 {p50Sim:N2} ms, p95 {p95Sim:N2} ms, max {maxSim:N1} ms · over 16.7 ms: {over16:N0} · over 33 ms: {over33:N0} · over 50 ms: {over50:N0}\n"));
			for (int s = 0; s < SegmentCount; s++)
			{
				double meanMs = segSum[s] / n;
				double share = sumPeriod > 0d ? segSum[s] / sumPeriod * 100.0 : 0d;
				sb.Append(string.Create(CultureInfo.InvariantCulture,
					$"           {meanMs,8:N3} ms  {share,5:N1}% of frame  {SegmentNames[s]}\n"));
			}
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           {sumUnaccounted / n,8:N3} ms  unaccounted inside the sim (between marks + this profiler)\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           {outsideMeanMs,8:N3} ms  {(1.0 - simShare) * 100.0,5:N1}% of frame  OUTSIDE SimulationService._Process (render, UI, other nodes)\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           delivered {delivered:N0} bets/s against {demand:N0} demanded · retention {retention:N3} · player {(double)sumPlayer / n:N1} + bots {(double)sumBot / n:N1} bets/frame\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           H1 sim share of the frame: {simShare * 100.0:N1}%  (pre-registered: ~35% at 99 credits × 3000X)\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           H2 MaxBetsPerFrame ({SimulationService.MaxBetsPerFrameForDiagnostics}) bound on {capShare * 100.0:N1}% of frames\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           H3 founder + scheduled PoW attempts per player/bot bet: {attemptsPerBet:N2}  ({(double)sumFounder / n:N1} + {(double)sumScheduled / n:N1} per frame)\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           H4 frames over 50 ms: {over50:N0}, of which {over50Explained:N0} carried a checkpoint or a GC · checkpoints {checkpoints:N0} · frames with a GC {gcFrames:N0}\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           R2-C1 overspend: {overspend * 100.0:N3}%  (clock advanced {sumCalendarAdvance:N1} game-s vs {sumRetainedGame:N1} retained) · Bet view {Scripts.Diagnostics.BetUiDiagnostics.View}\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           worst frame {maxPeriod:N1} ms: sim {_simMs[worst]:N1} ms, largest segment {SegmentNames[worstLargestSeg]}, checkpoints {_checkpoints[worst]}, GC {(_gc[worst] ? "yes" : "no")}"));

			GD.Print(sb.ToString());

			++ReportCount;
			ReportPublished?.Invoke();

			WriteTraceRow(string.Format(CultureInfo.InvariantCulture,
				"{0:O},{1},{2},{3:F2},{4:F3},{5:F3},{6:F3},{7},{8},{9},{10:F3},{11:F3},{12:F3},{13:F4},{14:F4},{15:F4},{16:F4},{17:F4},{18:F4},{19:F4},{20:F4},{21:F3},{22:F4},{23:F3},{24:F3},{25:F3},{26:F1},{27:F1},{28:F4},{29},{30},{31},{32},{33:F3},{34:F3},{35:F6},{36},{37:F0}",
				DateTime.UtcNow, n, partial ? 1 : 0, fps, p50Period, p95Period, maxPeriod, over16, over33, over50,
				p50Sim, p95Sim, maxSim, simShare,
				segSum[0] / n, segSum[1] / n, segSum[2] / n, segSum[3] / n, segSum[4] / n, segSum[5] / n, sumUnaccounted / n,
				(double)sumPlayer / n, capShare, (double)sumBot / n, (double)sumFounder / n, (double)sumScheduled / n,
				delivered, demand, retention, checkpoints, over50Explained, gcFrames,
				// The cap in force when the report closed. A window that straddles a change is a transition and is
				// read as one; the per-frame bets column shows where inside it the change landed.
				SimulationService.MaxBetsPerFrameForDiagnostics,
				sumCalendarAdvance, sumRetainedGame, overspend, Scripts.Diagnostics.BetUiDiagnostics.View,
				DevTimeScaleGovernor.BudgetInForce));
		}

		private static void WriteTraceRow(string row)
		{
			try
			{
				EnsureHeader();
				using FileAccess file = FileAccess.Open(TracePath, FileAccess.ModeFlags.ReadWrite);
				if (file == null)
				{
					return;
				}

				file.SeekEnd();
				file.StoreString(row + "\n");
			}
			catch (Exception)
			{
				// A diagnostic must never be able to take down the thing it is diagnosing.
			}
		}

		private static void EnsureHeader()
		{
			if (_headerChecked)
			{
				return;
			}

			_headerChecked = true;

			if (!DirAccess.DirExistsAbsolute("user://logs"))
			{
				DirAccess.MakeDirRecursiveAbsolute("user://logs");
			}

			if (FileAccess.FileExists(TracePath))
			{
				// Rotate rather than append rows under a header that no longer describes them (ND.10j).
				using FileAccess existing = FileAccess.Open(TracePath, FileAccess.ModeFlags.Read);
				string firstLine = existing?.GetLine() ?? string.Empty;
				existing?.Close();
				if (string.Equals(firstLine, Header, StringComparison.Ordinal))
				{
					return;
				}

				DirAccess.RenameAbsolute(TracePath, TracePath + ".old");
			}

			using FileAccess created = FileAccess.Open(TracePath, FileAccess.ModeFlags.Write);
			created?.StoreString(Header + "\n");
		}
	}
}
