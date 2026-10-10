using System;
using System.Globalization;

namespace UI.Readouts
{
	/// <summary>
	/// Mini-plan 15 B (2026-09-29) — the shared rule for a readout whose value moves faster than a screen can
	/// show it: DiceGame's game clock and mining-status block, and the StatusBar's clock.
	///
	/// <para><b>The bug this removes, stated as arithmetic rather than as a glitch.</b> Both readouts were
	/// rebuilt every frame. The game clock advances a near-constant amount per frame, so at 9000X it gained
	/// ~150 game-seconds each time — and <c>150 mod 60 = 30</c>, so the seconds field alternated between two
	/// values while the units digit sat perfectly still. <b>A frozen digit is what a near-constant step per
	/// sample looks like</b>; frame jitter is why it unfroze for a moment and then re-locked. The nonce counter
	/// did the same from the other side, gaining ~148 per frame.</para>
	///
	/// <para><b>Why a fixed sampling interval is not the fix.</b> A fixed cadence turns a fixed rate into a
	/// fixed step, one unit up. At a 10 Hz sample and 9000X the clock advances ~15 minutes per sample, and
	/// <c>15 mod 10 = 5</c> puts the same freeze on the minutes' units digit. The sampling interval and the
	/// displayed precision are <b>one</b> decision, not two.</para>
	///
	/// <para><b>The rule:</b> repaint on a cadence chosen so the <i>finest displayed field</i> advances by
	/// about <see cref="TargetStepsPerRepaint"/> of its own steps, and do not display the fields below it. Every
	/// digit on screen then visibly moves, none of them looks random, and nothing claims precision the sample
	/// cannot support. The rate is <b>measured</b> from the clock itself rather than derived from
	/// <c>DevTimeScale</c>, so it follows the simulation throttle, a paused sim and a rewound calendar for free.
	/// </para>
	///
	/// <para><b>What this deliberately does not do:</b> invent intermediate values to animate. A rolling counter
	/// that shows numbers between two samples is a smoother lie, and every figure this project displays is
	/// meant to be the real one.</para>
	/// </summary>
	public sealed class AdaptiveReadoutSampler
	{
		/// <summary>The finest field a repaint still shows. Coarser than <see cref="ClockUnit.Days"/> is never
		/// needed: a day per repaint is already the top of the speed range this game can reach.</summary>
		public enum ClockUnit
		{
			Seconds,
			Minutes,
			Hours,
			Days
		}

		// Two steps per repaint: enough that every displayed digit visibly changes, few enough that the finest
		// one is not a near-multiple of its own modulus (which is the freeze) nor effectively random.
		private const double TargetStepsPerRepaint = 2.0;

		// One frame at 60 fps. Repainting faster than the display refreshes buys nothing.
		private const double MinIntervalSeconds = 1.0 / 60.0;

		// A correct readout that waits longer than this still reads as frozen, so the cadence stops stretching
		// here and the step per repaint is allowed to grow past the target instead.
		private const double MaxIntervalSeconds = 1.0;

		// A jittering rate must not flip the FORMAT between repaints (14:23:45 <-> 14:23 <-> 14h) — that looks
		// worse than the frozen digit this class exists to remove. The flap protection is a CONFIRMATION STREAK
		// rather than a deadband around the threshold, deliberately: a deadband wide enough to be useful is also
		// wide enough to swallow a speed the game actually runs at. The first draft used one, and 100X — the
		// game's normal speed, and the one speed whose seconds display was never broken — sat inside the band,
		// so once a session had visited a high speed the clock never showed seconds again.
		private const int UnitChangeConfirmations = 6;

		// A NARROW deadband on top of the streak, applied only when moving to a finer unit. The streak alone
		// leaves a rate parked exactly on a boundary switching formats every few seconds, because the dwell is
		// asymmetric (six repaints at 0.8 s in one unit, six at 0.017 s in the next) — and a 9000X run throttled
		// to 0.8 sits on the minutes/hours boundary, so that is a real case, not a hypothetical. 1.15 turns each
		// boundary into a band roughly 15% wide; the draft's 1.6 made the band 75..120 game-seconds per real
		// second, which swallowed 100X. The margin must stay well clear of the speeds the game runs at.
		private const double UnitFinerMargin = 1.15;

		// How much of each new rate measurement is folded in. Low enough that one hitched frame moves nothing.
		private const double RateSmoothing = 0.2;

		// Mini-plan 20 Part 0 — a measurement this far from the estimate is a DIAL CHANGE (DevTimeScale moved, the
		// autobet stopped or started), not jitter, so it replaces the estimate instead of being averaged in.
		//
		// MEASURED IN THE EDITOR (developer, 2026-10-08): returning from 9000X to 100X took well over ten seconds
		// to show seconds again, longer at 99 credits. The arithmetic: the smoothed estimate must fall from 9000
		// to the finer-unit bar of 104 (100X sits only 4% under it), so 8,900 × 0.8ⁿ ≤ 4 takes ~35 repaints —
		// and while the readout still shows hours or minutes it repaints about once a second. Smoothing was
		// sized for jitter and was being asked to absorb a step change 90× its size.
		//
		// 2× is above any jitter this sampler has seen and below the smallest step that moves a unit boundary
		// far; the 1.11× steps at the top of the DevTimeScale ladder (90 → 80) are still averaged, which is fine
		// because they never cross more than one boundary. The unit still needs its confirmation streak, so a
		// single odd sample can replace the estimate but cannot flip the format.
		private const double RateSnapRatio = 2.0;

		private static readonly double[] UnitSeconds = { 1.0, 60.0, 3600.0, 86400.0 };

		private double _accumulator;
		private double _intervalSeconds = MinIntervalSeconds;
		private ClockUnit _unit = ClockUnit.Seconds;
		private ClockUnit _pendingUnit = ClockUnit.Seconds;
		private int _pendingUnitStreak;
		private double _gameSecondsPerRealSecond;
		private bool _hasRate;
		private DateTime _previousSampleGameTime;
		private bool _hasPreviousSample;

		/// <summary>The finest field the caller may display right now.</summary>
		public ClockUnit Unit => _unit;

		/// <summary>Measured game-seconds per real second. Diagnostic only — the sampler acts on it itself.</summary>
		public double MeasuredRate => _gameSecondsPerRealSecond;

		/// <summary>Call every frame with the real frame delta. True means repaint now, then call
		/// <see cref="NoteRepaint"/>.</summary>
		public bool ShouldRepaint(double realDelta)
		{
			_accumulator += realDelta;
			return _accumulator >= _intervalSeconds;
		}

		/// <summary>Call immediately after a repaint that <see cref="ShouldRepaint"/> authorised, with the game
		/// time that was painted. This is where the rate is measured and the next cadence chosen.</summary>
		public void NoteRepaint(DateTime gameLocalNow)
		{
			// The accumulator IS the real time since the previous repaint. Zeroing it rather than subtracting
			// the interval drops a stall's backlog instead of replaying it as a burst of catch-up repaints.
			double elapsedReal = Math.Max(_accumulator, MinIntervalSeconds);
			_accumulator = 0.0;

			if (_hasPreviousSample)
			{
				double advancedGame = (gameLocalNow - _previousSampleGameTime).TotalSeconds;

				// A rewind is not a rate. The calendar navigator and the history explorer both wind the clock
				// back; reading that as "very slow" would drop the readout to seconds for several repaints.
				if (advancedGame >= 0.0)
				{
					double rate = advancedGame / elapsedReal;
					bool isStep = _hasRate
						&& (rate > _gameSecondsPerRealSecond * RateSnapRatio
							|| rate * RateSnapRatio < _gameSecondsPerRealSecond);
					_gameSecondsPerRealSecond = _hasRate && !isStep
						? (_gameSecondsPerRealSecond * (1.0 - RateSmoothing)) + (rate * RateSmoothing)
						: rate;
					_hasRate = true;
					if (isStep)
					{
						_realSecondsSinceRateStep = 0.0;
					}
				}
			}

			_realSecondsSinceRateStep += elapsedReal;
			_previousSampleGameTime = gameLocalNow;
			_hasPreviousSample = true;
			ClockUnit before = _unit;
			ChooseUnitAndInterval();
			if (_unit != before)
			{
				TraceUnitChange(before);
			}
		}

		/// <summary>DEV diagnostic label. When set, every change of displayed unit is printed (DEBUG builds only)
		/// with the time it took after the last rate step, so a recovery delay is a number, not an impression.</summary>
		public string TraceName { get; init; }

		// Real seconds since the last sample that replaced the estimate (a dial change). Diagnostic only.
		private double _realSecondsSinceRateStep;

		// GD.Print, not PrintErr: this lands in the Godot editor's OUTPUT panel, where the developer reads.
		[System.Diagnostics.Conditional("DEBUG")]
		private void TraceUnitChange(ClockUnit before)
		{
			if (TraceName == null)
			{
				return;
			}

			Godot.GD.Print(string.Create(CultureInfo.InvariantCulture,
				$"[Readout] {TraceName}: {before} -> {_unit} at {_gameSecondsPerRealSecond:0.0} game-s/s, " +
				$"{_realSecondsSinceRateStep:0.00} s after the last rate step"));
		}

		private void ChooseUnitAndInterval()
		{
			if (!_hasRate)
			{
				// Not yet measured — the first repaint of a scene has no previous sample to measure against.
				// "Unknown" is not "stopped": bootstrap at the floor so the second repaint arrives within a
				// frame or two and the real cadence is chosen from real data, rather than making the readout
				// sit still for a second while it waits to find out how fast the clock is going.
				_unit = ClockUnit.Seconds;
				_intervalSeconds = MinIntervalSeconds;
				return;
			}

			if (_gameSecondsPerRealSecond <= 0.0)
			{
				// A stopped clock. Nothing is moving, so nothing can strobe: show the finest unit and repaint
				// as rarely as the ceiling allows.
				_unit = ClockUnit.Seconds;
				_intervalSeconds = MaxIntervalSeconds;
				return;
			}

			// The finest unit whose target cadence still clears the frame floor — by the narrow margin above if
			// that would mean displaying a finer unit than the one on screen now.
			int current = (int)_unit;
			int chosen = UnitSeconds.Length - 1;
			for (int i = 0; i < UnitSeconds.Length; i++)
			{
				double bar = i < current ? MinIntervalSeconds * UnitFinerMargin : MinIntervalSeconds;
				if (TargetStepsPerRepaint * UnitSeconds[i] / _gameSecondsPerRealSecond >= bar)
				{
					chosen = i;
					break;
				}
			}

			if (chosen == (int)_unit)
			{
				_pendingUnit = _unit;
				_pendingUnitStreak = 0;
			}
			else if ((ClockUnit)chosen == _pendingUnit && ++_pendingUnitStreak >= UnitChangeConfirmations)
			{
				_unit = _pendingUnit;
				_pendingUnitStreak = 0;
			}
			else if ((ClockUnit)chosen != _pendingUnit)
			{
				_pendingUnit = (ClockUnit)chosen;
				_pendingUnitStreak = 1;
			}

			// The cadence follows the unit actually DISPLAYED — otherwise a fine format would be repainted at a
			// spacing chosen for a coarser one, which is the original bug.
			_intervalSeconds = Math.Clamp(
				TargetStepsPerRepaint * UnitSeconds[(int)_unit] / _gameSecondsPerRealSecond,
				MinIntervalSeconds,
				MaxIntervalSeconds);

			// ...with one exception, which is safe in only one direction (mini-plan 20 Part 0). While a FINER unit
			// is waiting out its confirmation streak, repaint at the finer unit's cadence. A coarse format painted
			// MORE often than it needs moves by less than one of its own steps per repaint, so it cannot freeze or
			// strobe; it only reaches its confirmations sooner. Without this the six confirmations for "back to
			// seconds" arrived at the coarse unit's one-second cadence: six seconds of a readout that was already
			// right to change. The reverse (a fine format painted at a coarse cadence) is the bug, so a pending
			// COARSER unit keeps the displayed unit's faster cadence, which the line above already gives it.
			if (_pendingUnitStreak > 0 && _pendingUnit < _unit)
			{
				double pendingInterval = Math.Clamp(
					TargetStepsPerRepaint * UnitSeconds[(int)_pendingUnit] / _gameSecondsPerRealSecond,
					MinIntervalSeconds,
					MaxIntervalSeconds);
				_intervalSeconds = Math.Min(_intervalSeconds, pendingInterval);
			}
		}

		/// <summary>The time half of a date-time pattern for <paramref name="unit"/> — empty at
		/// <see cref="ClockUnit.Days"/>, where the time of day is entirely below the sample's resolution.
		/// Callers prepend their own date pattern so each screen keeps its established date style.</summary>
		public static string TimePatternFor(ClockUnit unit) => unit switch
		{
			ClockUnit.Seconds => "HH:mm:ss",
			ClockUnit.Minutes => "HH:mm",
			ClockUnit.Hours => "HH'h'",
			_ => string.Empty
		};

		/// <summary>Formats a game time at the resolution this sampler can honestly show.
		/// <paramref name="datePattern"/> is always rendered; the time half is appended when there is one.</summary>
		public string FormatGameTime(DateTime gameLocal, string datePattern)
		{
			string timePattern = TimePatternFor(_unit);
			string pattern = timePattern.Length == 0 ? datePattern.TrimEnd() : datePattern + " " + timePattern;
			return gameLocal.ToString(pattern, CultureInfo.InvariantCulture);
		}
	}

	/// <summary>
	/// Mini-plan 15 B — the same idea applied to a counter instead of a clock: a nonce count that gains
	/// thousands between repaints is showing four digits of noise. This measures the step per repaint and
	/// floors the displayed value to the largest power of ten at or below it, marking the result <c>~</c>.
	///
	/// <para>The quantum moves in decades, which is a wide enough band that a jittering step cannot make
	/// digits appear and disappear between repaints. It floors and never rounds up: an attempt count that has
	/// not happened yet must not appear on screen.</para>
	/// </summary>
	public sealed class CounterQuantizer
	{
		private const long MaxQuantum = 100000000L;

		private long _previousSample;
		private bool _hasPreviousSample;
		private long _quantum = 1L;

		/// <summary>The step the display is currently floored to. 1 means the exact value is shown.</summary>
		public long Quantum => _quantum;

		/// <summary>Call once per repaint with the counter's live value; returns the quantum to display it at.</summary>
		public long NoteSample(long value)
		{
			if (_hasPreviousSample)
			{
				long step = value - _previousSample;

				// A reset is not a step — a mined block restarts the candidate's nonce at 0, and reading that
				// as a step would collapse the display to single digits for one repaint. Re-baseline instead.
				if (step >= 0L)
				{
					while (step >= _quantum * 10L && _quantum < MaxQuantum)
					{
						_quantum *= 10L;
					}

					while (_quantum > 1L && step < _quantum)
					{
						_quantum /= 10L;
					}
				}
			}

			_previousSample = value;
			_hasPreviousSample = true;
			return _quantum;
		}

		/// <summary>Renders <paramref name="value"/> floored to <paramref name="quantum"/>, prefixed <c>~</c>
		/// whenever anything was dropped. Static so a caller that only holds the quantum can format with it.</summary>
		public static string Format(long value, long quantum)
		{
			if (quantum <= 1L || value <= 0L)
			{
				return value.ToString(CultureInfo.InvariantCulture);
			}

			long floored = value - (value % quantum);
			return string.Create(CultureInfo.InvariantCulture, $"~{floored}");
		}
	}
}
