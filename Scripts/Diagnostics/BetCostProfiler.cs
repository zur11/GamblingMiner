using Godot;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Scripts.Diagnostics
{
	/// <summary>
	/// Mini-plan 08 P1 — prices ONE BET, segment by segment, inside the running engine.
	///
	/// <para><b>Why this exists at all.</b> <c>SimulationService.MaxBetsPerFrame</c> is 10. That number is a
	/// CONSTANT, not a measurement — nobody has ever timed a bet — and it is the binding constraint on the
	/// developer's 99-credits × high-scale target (mini-plan 08 §3). CLAUDE.md's closing rule under
	/// Important Pattern 6 is that a cost note is a measurement or it is a guess wearing a measurement's
	/// clothes. This is the measurement.</para>
	///
	/// <para><b>Why it could not be desk work.</b> P1 was specified as a throwaway console project. Half of
	/// <c>ExecutePlayerBetOnce</c> is reachable that way — the dice roll, the wallet, the decimal arithmetic,
	/// the progression — and that half WAS priced there (scratchpad harness, 2026-08-30): <b>1.77 µs/bet in
	/// DEBUG, 0.70 µs in RELEASE</b>. The other half is autoloads and static chain state — the journal
	/// append, the SC balance sheet, the client ledger, the nonce attempt, and the four events each bet
	/// fires — none of which exist outside the Godot runtime. A console project cannot see the half the
	/// plan's own hypothesis blames, so the measurement has to happen here.</para>
	///
	/// <para><b>Off by default, and that is load-bearing.</b> P2 sweeps the throughput frontier; a profiler
	/// adding a few percent to every bet would move the very frontier P2 is measuring. Arm it for P1, read
	/// the breakdown, disarm it before P2. The toggle lives in <c>DevTimeScaleSelector</c>, beside the
	/// controls that set the demand and the readout that shows the delivery.</para>
	///
	/// <para><b>DEBUG only.</b> Every entry point is <see cref="ConditionalAttribute"/>-guarded, so an
	/// exported RELEASE build contains no calls at all — not a disabled branch, no calls. The corollary is
	/// mini-plan 06's rule: silence from this class in a RELEASE build means "never compiled in", not
	/// "nothing to report", which is why <see cref="Arm"/> announces itself.</para>
	/// </summary>
	public static class BetCostProfiler
	{
		/// <summary>The stages of one bet, in the order <c>ExecutePlayerBetOnce</c> runs them.</summary>
		public enum Segment
		{
			/// <summary>_session.ExecuteNext — dice, wallet, decimal arithmetic, progression, stops.
			/// The ONE segment the scratchpad harness also measured, so it doubles as a cross-check:
			/// if this reads far from 1.77 µs, the two measurements disagree and the harness is not
			/// modelling what the engine runs.</summary>
			ExecuteNext = 0,
			/// <summary>UserStatsService.OnBetExecutedRegisterBet — the bet journal append + the rollup.</summary>
			RegisterBet,
			/// <summary>PersistFinancialState(false) — builds a NodeFinancialState and hands it to NetworkRoot.</summary>
			PersistFinancial,
			/// <summary>BankrollStateService.SetBalance — which calls SaveState(), a synchronous JSON write.
			/// Split out of the old combined MoneyServices segment after round 1 measured that segment at
			/// 67% of the whole bet: a two-call bundle cannot say WHICH call to fix.</summary>
			BankrollSetBalance,
			/// <summary>CasinoScBalanceService.ApplyBetResult — marks its own save dirty (no disk) and fires
			/// BalanceChanged, so whatever this costs belongs to that event's subscribers.</summary>
			CasinoApplyBetResult,
			/// <summary>CasinoClientLedgerService.RegisterSettledBet — skipped entirely when the player's own
			/// node is active, so on a player run this segment is expected to read ~0. It is measured anyway:
			/// a segment assumed to be zero and never checked is how a cost hides.</summary>
			ClientLedger,
			/// <summary>RouteNonceAttempt — one real proof-of-work attempt, plus the block path when it hits.</summary>
			NonceAttempt,
			/// <summary>The ClientBetSettled C# event alone.</summary>
			ClientBetSettledEvent,
			/// <summary>The bet-history fan-out inside DiceGame.OnSimBetSettled — BetExecuted → the two
			/// pooled UI containers, each doing a Setup() and a MoveChild(). Marked from DiceGame itself,
			/// which is legal because OnSimBetSettled runs SYNCHRONOUSLY inside the EmitSignal below, so it
			/// still closes in bet order.
			///
			/// Split off because P1c left BetSettled at 189.7 µs — 74% of what a bet now costs — and reading
			/// the two containers could not settle whether the cost is Setup (text/label writes) or
			/// MoveChild (container re-sort and relayout). Those have different fixes, so guessing would
			/// mean optimising one of them blind.</summary>
			BetHistoryFeed,
			/// <summary>EmitSignal(BetSettled) MINUS the segment above — the Godot signal's own marshalling
			/// into native and back, plus OnSimBetSettled's dedupe and dirty-flag set.</summary>
			BetSettledSignal,
		}

		private const int SegmentCount = 10;

		private static readonly string[] SegmentNames =
		{
			"ExecuteNext (dice+wallet+progression)",
			"RegisterBet (journal + rollup)",
			"PersistFinancialState",
			"BankrollSetBalance (SYNC DISK WRITE)",
			"CasinoApplyBetResult (BalanceChanged)",
			"ClientLedger (skipped on player node)",
			"NonceAttempt (PoW + block path)",
			"ClientBetSettled (C# event)",
			"BetHistoryFeed (2 pooled UI containers)",
			"BetSettled (signal marshalling + rest)",
		};

		public const string TracePath = "user://logs/bet_cost_trace.csv";

		private const string Header =
			"reportUtc,bets,totalUsPerBet,accountedUsPerBet,unaccountedUsPerBet," +
			"executeNextUs,registerBetUs,persistFinancialUs,bankrollSetBalanceUs,casinoApplyBetResultUs," +
			"clientLedgerUs,nonceAttemptUs,clientBetSettledUs,betHistoryFeedUs,betSettledSignalUs," +
			"maxTotalUs,betsPerFrameAt60";

		// How many bets accumulate before a report. A report is one GD.Print block and one CSV line — never
		// per bet — so the cost of reporting is noise at any of these sizes, and the only real constraint is
		// that the sample be large enough for a stable mean. 5,000 is abundant for that.
		//
		// It was 20,000 for one run, and that was a MISCALIBRATION against how a session is actually paced.
		// The developer's natural unit of "let it run a while" is MINED BLOCKS, and in the measured world a
		// block costs ~2,400 player bets — so 20,000 bets meant ~8 blocks per report and ~25 for the three
		// the protocol asked for. At 5,000 a report lands roughly every other block, which is a length
		// somebody will actually sit through.
		//
		// The general rule: a diagnostic's reporting period is denominated in the units the OPERATOR paces
		// the session in, not merely in the units the measurement is taken in.
		//
		// PUBLIC because the toggle that arms this quotes it to the developer. Standing Convention 15: the
		// value lives in exactly one place and every mention of it reads that place.
		public const int ReportEveryBets = 5_000;

		private static readonly long[] _ticks = new long[SegmentCount];
		private static long _betTicks;
		private static long _maxBetTicks;
		private static int _betsSinceReport;
		private static bool _headerChecked;

		private static long _betStart;
		private static long _segmentStart;

		/// <summary>Armed state. False by default — see the class remarks on why P2 needs it off.</summary>
		public static bool Enabled { get; private set; }

		/// <summary>
		/// Turn measurement on or off, announcing the transition. It ANNOUNCES rather than toggling
		/// silently for mini-plan 06 §9.1's reason: a diagnostic whose passing state is silence must say
		/// out loud whether it is running, or "nothing appeared" is ambiguous between "no finding" and
		/// "never armed". Emitted with GD.Print — the Godot editor's <b>Output</b> panel — never
		/// GD.PrintErr, which lands in the Debugger → Errors tab where nobody was looking (CLAUDE.md,
		/// "Asking the developer to read output").
		/// </summary>
		[Conditional("DEBUG")]
		public static void Arm(bool enabled)
		{
			if (Enabled == enabled)
			{
				return;
			}

			// FLUSH BEFORE DISARMING — a partial window is data, and throwing it away is a choice.
			//
			// Round 2's first attempt ended at ~2,600 bets and produced NOTHING, because the operator
			// stopped before the 5,000-bet window closed. The measurement was real, complete for its
			// sample, sitting in these accumulators, and was discarded by Reset() for no better reason
			// than not reaching a round number. The threshold is a REPORTING CADENCE, not a minimum
			// sample size, and it was silently acting as both.
			//
			// General rule: an instrument that reports only on its own schedule loses every run that ends
			// off-schedule — and runs end off-schedule for reasons the instrument never gets to hear.
			if (!enabled && _betsSinceReport > 0)
			{
				Report(partial: true);
			}

			Enabled = enabled;
			Reset();

			if (enabled)
			{
				GD.Print(string.Create(CultureInfo.InvariantCulture,
					$"[BetCost] ARMED — a breakdown prints every {ReportEveryBets:N0} player bets, and a " +
					$"final partial one prints when you disarm, however few bets it holds. Output panel " +
					$"plus {TracePath}. Measured overhead is 0.016% of a bet, so leaving it armed does not " +
					$"perturb a throughput measurement."));
			}
			else
			{
				GD.Print("[BetCost] disarmed — bets are no longer being timed.");
			}
		}

		private static void Reset()
		{
			Array.Clear(_ticks, 0, SegmentCount);
			_betTicks = 0;
			_maxBetTicks = 0;
			_betsSinceReport = 0;
		}

		/// <summary>Called at the top of one bet, before any of its work.</summary>
		[Conditional("DEBUG")]
		public static void BeginBet()
		{
			if (!Enabled) return;
			_betStart = Stopwatch.GetTimestamp();
			_segmentStart = _betStart;
		}

		/// <summary>
		/// Closes the segment that has been running since the previous mark (or since <see cref="BeginBet"/>)
		/// and attributes its time to <paramref name="segment"/>. Call it immediately AFTER the work it names.
		/// </summary>
		[Conditional("DEBUG")]
		public static void Mark(Segment segment)
		{
			if (!Enabled) return;
			long now = Stopwatch.GetTimestamp();
			_ticks[(int)segment] += now - _segmentStart;
			_segmentStart = now;
		}

		/// <summary>
		/// Closes the bet. The gap between this total and the sum of the marked segments is reported as
		/// <c>unaccounted</c>, and it is deliberately not hidden: it holds both the code between the marks
		/// and this profiler's own <see cref="Stopwatch.GetTimestamp"/> calls. A breakdown that silently
		/// forced the parts to sum to the whole would be unable to reveal its own overhead.
		/// </summary>
		[Conditional("DEBUG")]
		public static void EndBet()
		{
			if (!Enabled) return;

			long elapsed = Stopwatch.GetTimestamp() - _betStart;
			_betTicks += elapsed;
			if (elapsed > _maxBetTicks)
			{
				_maxBetTicks = elapsed;
			}

			if (++_betsSinceReport >= ReportEveryBets)
			{
				Report();
				Reset();
			}
		}

		private static double TicksToMicroseconds(long ticks, int bets)
		{
			if (bets <= 0) return 0d;
			return ticks * 1_000_000.0 / Stopwatch.Frequency / bets;
		}

		/// <param name="partial">True for the flush on disarm, where the window did not fill. The sample
		/// size is printed either way, but a short window is LABELLED, because a mean over 300 bets and a
		/// mean over 5,000 read identically once they are numbers in a table.</param>
		private static void Report(bool partial = false)
		{
			int bets = _betsSinceReport;
			double totalUs = TicksToMicroseconds(_betTicks, bets);
			double maxUs = TicksToMicroseconds(_maxBetTicks, 1);

			double accountedUs = 0d;
			var perSegment = new double[SegmentCount];
			for (int i = 0; i < SegmentCount; i++)
			{
				perSegment[i] = TicksToMicroseconds(_ticks[i], bets);
				accountedUs += perSegment[i];
			}

			// THE number this whole phase exists to produce: how many bets fit in one 60 fps frame if the
			// frame did nothing else. It is an UPPER BOUND — rendering, the bots, the founders, the
			// scheduled network and every UI subscriber all draw from the same 16.67 ms — so
			// MaxBetsPerFrame belongs well below it, never at it.
			double betsPerFrameAt60 = totalUs > 0d ? (1000.0 / 60.0) * 1000.0 / totalUs : 0d;

			var sb = new StringBuilder();
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"[BetCost]{(partial ? " PARTIAL WINDOW —" : "")} {bets:N0} player bets — " +
				$"{totalUs:N3} µs/bet mean, {maxUs:N1} µs worst\n"));
			for (int i = 0; i < SegmentCount; i++)
			{
				double share = totalUs > 0d ? perSegment[i] / totalUs * 100.0 : 0d;
				sb.Append(string.Create(CultureInfo.InvariantCulture,
					$"           {perSegment[i],8:N3} µs  {share,5:N1}%  {SegmentNames[i]}\n"));
			}

			double unaccountedUs = totalUs - accountedUs;
			double unaccountedShare = totalUs > 0d ? unaccountedUs / totalUs * 100.0 : 0d;
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           {unaccountedUs,8:N3} µs  {unaccountedShare,5:N1}%  unaccounted (inter-mark code + this profiler)\n"));
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"           ⇒ {betsPerFrameAt60:N0} bets per 16.67 ms frame if the frame did NOTHING else " +
				$"(MaxBetsPerFrame is currently {SimulationService.MaxBetsPerFrameForDiagnostics})"));

			GD.Print(sb.ToString());
			WriteTraceRow(bets, totalUs, accountedUs, unaccountedUs, perSegment, maxUs, betsPerFrameAt60);
		}

		private static void WriteTraceRow(
			int bets, double totalUs, double accountedUs, double unaccountedUs,
			double[] perSegment, double maxUs, double betsPerFrameAt60)
		{
			try
			{
				EnsureHeader();

				// Real wall-clock, deliberately: this is DEV telemetry about the MACHINE, not game-world
				// state, and CLAUDE.md's game-time rule names exactly that exemption. A game-time stamp
				// here would be actively misleading — the quantity measured is real microseconds.
				// Built by LOOPING over the segments rather than by a fixed placeholder list. The first
				// version hardcoded six holes, and splitting two segments into five silently left the row
				// misaligned against its own header until it was caught by hand. A writer whose column
				// count is stated twice will eventually state it two different ways.
				var row = new StringBuilder();
				row.Append(string.Create(CultureInfo.InvariantCulture,
					// Real wall-clock, deliberately: this is DEV telemetry about the MACHINE, not
					// game-world state, and CLAUDE.md's game-time rule names exactly that exemption. A
					// game-time stamp here would be actively misleading — the quantity is real microseconds.
					$"{DateTime.UtcNow:O},{bets},{totalUs:F3},{accountedUs:F3},{unaccountedUs:F3}"));
				for (int i = 0; i < SegmentCount; i++)
				{
					row.Append(string.Create(CultureInfo.InvariantCulture, $",{perSegment[i]:F3}"));
				}
				row.Append(string.Create(CultureInfo.InvariantCulture,
					$",{maxUs:F1},{betsPerFrameAt60:F0}\n"));

				using FileAccess file = FileAccess.Open(TracePath, FileAccess.ModeFlags.ReadWrite);
				if (file == null)
				{
					return;
				}

				file.SeekEnd();
				file.StoreString(row.ToString());
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
				// ND.10j's stale-schema rule, as SessionLifecycleTrace applies it: rotate rather than append
				// rows under a header that no longer describes them. A misaligned trace is worse than no
				// trace, because it is read as data.
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
