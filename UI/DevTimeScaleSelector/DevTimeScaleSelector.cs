using Godot;

namespace UI.DevTimeScaleSelector
{
	// DEV/TEST ONLY — a small selector (label + OptionButton) to accelerate the simulation from the 100X
	// base up to the clock's ceiling. It drives CalendarTimeService.DevTimeScale, which scales BOTH the
	// calendar clock and the bet-execution rate by the same factor, leaving the difficulty / power /
	// solvetime dynamics invariant (only wall-clock time compresses). Built programmatically (like StatusBar)
	// so it can be dropped into any screen without editing its .tscn. Not persisted; resets to 100X on restart.
	public partial class DevTimeScaleSelector : HBoxContainer
	{
		// DevTimeScale multipliers on the 100X base clock. Two régimes, deliberately:
		//
		//   ×1..×9   — a FINE range in 100X steps, added 2026-08-30 for mini-plan 08. The throughput
		//              frontier is `credits × DevTimeScale`, so at high credit counts the highest
		//              sustainable scale lands in here — with only ×1 and ×10 on offer, the entire frontier
		//              fell in a gap the selector could not express and P2's sweep had no grid to sweep.
		//              It runs to ×9, not to the ×6 the 99-credit frontier predicts, because raising
		//              SimulationService.MaxBetsPerFrame (P1's whole purpose) MOVES that frontier upward,
		//              and a ladder that stops at today's measured knee reintroduces the same gap one
		//              measurement later. See §3 of
		//              AIHelperFiles/mini08-timestamp-fidelity-and-throughput-limits-plan.md.
		//   ×10..×90 — the original coarse range, in 1000X steps, for the low-credit runs that saturate
		//              nowhere near it.
		//
		// (Capped at ×90 — 10000X hit the MaxBetsPerFrame throughput ceiling and lagged.)
		//
		// This ladder is a CONVENIENCE, not the limit. The limit is
		// CalendarTimeService.MaxGameSecondsPerRealSecond, enforced where the rate is spent, because this
		// selector is only one of two factors in it — see that constant's note. Shortening this array does
		// not lower the ceiling and lengthening it does not raise one.
		private static readonly int[] Multipliers = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 30, 40, 50, 60, 70, 80, 90 };

		// The top of the ladder is meant to BE the ceiling, on the 100X base. Asserted rather than trusted:
		// the two live in different files and the failure mode is a selector offering a speed the clock
		// silently refuses to deliver — a lying control, which is worse than a missing one.
		private const double BaseGameSecondsPerRealSecond = 100.0;

		private OptionButton _selector;
		private CalendarTimeService _calendar;

		[System.Diagnostics.Conditional("DEBUG")]
		private static void AssertLadderTopMatchesCeiling()
		{
			double top = Multipliers[Multipliers.Length - 1] * BaseGameSecondsPerRealSecond;
			if (top != CalendarTimeService.MaxGameSecondsPerRealSecond)
			{
				GD.PrintErr(string.Format(
					System.Globalization.CultureInfo.InvariantCulture,
					"[DevTimeScale] The selector's top step is {0:N0}X but the clock's ceiling is {1:N0}X. " +
					"One of the two moved without the other; the selector must never offer a speed the " +
					"clock will clamp.",
					top, CalendarTimeService.MaxGameSecondsPerRealSecond));
			}
		}

		public override void _Ready()
		{
			AssertLadderTopMatchesCeiling();
			AddThemeConstantOverride("separation", 8);

			_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

			var label = new Label { Text = "DEV ⏩ Time:" };
			label.AddThemeFontSizeOverride("font_size", 18);
			AddChild(label);

			_selector = new OptionButton();
			foreach (int mult in Multipliers)
			{
				_selector.AddItem($"{mult * 100}X");
			}

			// The selector shows what was REQUESTED; what actually runs is shown by the governor readout below.
			int current = System.Array.IndexOf(Multipliers, _calendar?.RequestedDevTimeScale ?? 1);
			_selector.Select(current < 0 ? 0 : current);
			_selector.ItemSelected += OnScaleSelected;
			AddChild(_selector);

			// The retention readout belongs beside the control that causes the saturation: this selector is
			// what asks for 90× the work, and Sim% is what says whether the engine is actually delivering it.
			// It is also the only way the reading reaches DICEGAME, which renders its own balance labels and
			// has no StatusBar to host it.
			AddChild(new UI.SimRetentionReadout.SimRetentionReadout(18));

			AddBetCostToggle();
			AddFrameCostToggle();
			AddFrameCapSelector();
			AddBetUiModePicker();

			// Mini-plan 09 §4 — the governor readout, placed AFTER the diagnostic column so appearing, disappearing
			// or changing width can never push the toggles out of reach (P3's run lost the Frame cost toggle that
			// way to the Sim% text). Hidden while nothing limits the request.
			_effectiveScaleLabel = new Label { MouseFilter = MouseFilterEnum.Pass };
			_effectiveScaleLabel.AddThemeFontSizeOverride("font_size", 18);
			_effectiveScaleLabel.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.20f));
			(DiagnosticsHost ?? (Node)this).AddChild(_effectiveScaleLabel);
			if (_calendar != null)
			{
				_calendar.DevTimeScaleChanged += RefreshEffectiveScale;
			}
			RefreshEffectiveScale();
		}

		private Label _effectiveScaleLabel;

		public override void _ExitTree()
		{
			if (_calendar != null)
			{
				_calendar.DevTimeScaleChanged -= RefreshEffectiveScale;
			}
		}

		// Event-driven: runs only when the governor changes the effective scale or its reason.
		private void RefreshEffectiveScale()
		{
			if (!GodotObject.IsInstanceValid(this) || _effectiveScaleLabel == null || _calendar == null)
			{
				return;
			}

			if (_calendar.DevTimeScaleLimit == DevTimeScaleLimit.None)
			{
				_effectiveScaleLabel.Visible = false;
				return;
			}

			double runningRate = _calendar.DevTimeScale * DevTimeScaleGovernor.DevBaseGameSecondsPerRealSecond;
			double requestedRate = _calendar.RequestedDevTimeScale * DevTimeScaleGovernor.DevBaseGameSecondsPerRealSecond;
			double credits = _calendar.DevTimeScaleGovernedCredits;
			_effectiveScaleLabel.Visible = true;

			if (_calendar.DevTimeScaleLimit == DevTimeScaleLimit.Credits)
			{
				_effectiveScaleLabel.Text = string.Create(System.Globalization.CultureInfo.InvariantCulture,
					$"⇣ {runningRate:N0}X · {credits:N0} credits");
				_effectiveScaleLabel.TooltipText = string.Create(System.Globalization.CultureInfo.InvariantCulture,
					$"Requested {requestedRate:N0}X, running at {runningRate:N0}X. {credits:N0} running hardware credits × " +
					$"{_calendar.DevTimeScale} = {credits * _calendar.DevTimeScale:N0} bets/s, the most that fits the " +
					$"{DevTimeScaleGovernor.BetBudgetPerSecond:N0} bets/s budget (DiceGame at 50 fps, mini-plan 09). Fewer credits run faster.");
			}
			else
			{
				_effectiveScaleLabel.Text = string.Create(System.Globalization.CultureInfo.InvariantCulture,
					$"⇣ {runningRate:N0}X · ceiling");
				_effectiveScaleLabel.TooltipText = string.Create(System.Globalization.CultureInfo.InvariantCulture,
					$"Requested {requestedRate:N0}X, running at {runningRate:N0}X: the clock's absolute ceiling " +
					$"(CalendarTimeService.MaxGameSecondsPerRealSecond).");
			}
		}

		// Mini-plan 09 P3a — the per-frame bet cap, changeable mid-run so it can be swept A–B–A without a rebuild.
		// The options are the model's candidates, not a ladder: 40 is today's default, 48 fits the chosen 50 fps
		// minimum, 60 and 80 test the curve beyond it. It reads the cap back from SimulationService on build, so
		// returning to the scene shows the cap actually in force (the toggle-mirroring lesson from P1's run).
		[System.Diagnostics.Conditional("DEBUG")]
		private void AddFrameCapSelector()
		{
			int[] caps = { 40, 48, 60, 80 };
			var picker = new OptionButton
			{
				TooltipText = "DEV — SimulationService.MaxBetsPerFrame, overridden for this session only. "
					+ "Each extra bet per frame buys throughput with frame rate (mini-plan 09 P1).",
			};
			picker.AddThemeFontSizeOverride("font_size", 16);
			foreach (int cap in caps)
			{
				picker.AddItem(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Cap/frame {cap}"));
			}

			int current = System.Array.IndexOf(caps, SimulationService.MaxBetsPerFrameForDiagnostics);
			picker.Select(current < 0 ? 0 : current);
			picker.ItemSelected += index => SimulationService.SetMaxBetsPerFrameOverrideForDiagnostics(caps[(int)index]);
			DiagnosticColumn().AddChild(picker);
		}

		// Mini-plan 10 A1 — what DiceGame's two per-bet lists do on each settled bet: Full (today), No reorder
		// (Setup only) or Off. Switchable mid-run so the three can be compared inside one session. Reads the mode
		// back on build, so a scene re-entry shows the mode actually in force.
		[System.Diagnostics.Conditional("DEBUG")]
		private void AddBetUiModePicker()
		{
			Scripts.Diagnostics.BetUiMode[] modes =
			{
				Scripts.Diagnostics.BetUiMode.Full,
				Scripts.Diagnostics.BetUiMode.NoReorder,
				Scripts.Diagnostics.BetUiMode.Off,
			};
			var picker = new OptionButton
			{
				TooltipText = "DEV — what DiceGame's bet history and winner grid do per settled bet (mini-plan 10 A1). "
					+ "No reorder shows rows in the wrong order on purpose; Off shows nothing new.",
			};
			picker.AddThemeFontSizeOverride("font_size", 16);
			foreach (Scripts.Diagnostics.BetUiMode mode in modes)
			{
				picker.AddItem($"Bet UI {mode}");
			}

			int current = System.Array.IndexOf(modes, Scripts.Diagnostics.BetUiDiagnostics.Mode);
			picker.Select(current < 0 ? 0 : current);
			picker.ItemSelected += index => Scripts.Diagnostics.BetUiDiagnostics.SetMode(modes[(int)index]);
			DiagnosticColumn().AddChild(picker);
		}

		// The diagnostic toggles stack in ONE COLUMN at the end of the row instead of extending it sideways.
		// With two of them side by side the second landed off the reachable area of the screen and could not be
		// clicked (developer's report, 2026-09-17). Created on first use, so a RELEASE build — where both toggle
		// methods compile away — adds no empty container.
		private VBoxContainer _diagnosticColumn;

		/// <summary>
		/// Optional container, set by the host scene BEFORE this selector enters the tree, that receives the DEV
		/// test controls (the profiler toggles, the Cap/frame picker, the governor readout) instead of this row.
		/// DiceGame uses it to put them in a free block of its layout; BlockExplorer leaves it null and keeps them
		/// at the end of the row.
		/// </summary>
		public VBoxContainer DiagnosticsHost { get; set; }

		private VBoxContainer DiagnosticColumn()
		{
			if (DiagnosticsHost != null)
			{
				return DiagnosticsHost;
			}

			if (_diagnosticColumn == null)
			{
				_diagnosticColumn = new VBoxContainer();
				_diagnosticColumn.AddThemeConstantOverride("separation", 0);
				AddChild(_diagnosticColumn);
			}

			return _diagnosticColumn;
		}

		// Mini-plan 09 P1 — arms Scripts/Diagnostics/FrameCostProfiler, the whole-frame twin of the bet toggle
		// above. Same contract, same reasons: DEBUG-only (absent, not disabled, in an exported build), OFF by
		// default, and to be armed BEFORE starting the autobet, because a saturated frame makes any control
		// here slow to answer a click.
		[System.Diagnostics.Conditional("DEBUG")]
		private void AddFrameCostToggle()
		{
			var toggle = new CheckButton
			{
				Text = "⏱ Frame cost",
				// Mirrors the static profiler state, which survives scene changes. It was built as `false`, so on
				// returning from the hardware shop an ARMED profiler read as off and the run looked disarmed.
				ButtonPressed = Scripts.Diagnostics.FrameCostProfiler.Enabled,
				TooltipText =
					"DEV — time the WHOLE frame while the background sim runs: its segments, what lies outside it, "
					+ "bets and PoW attempts per frame, checkpoints and GC. Reports to the Godot editor's Output "
					+ "panel and to user://logs/frame_cost_trace.csv.",
			};
			toggle.AddThemeFontSizeOverride("font_size", 16);
			toggle.Toggled += pressed => Scripts.Diagnostics.FrameCostProfiler.Arm(pressed);
			DiagnosticColumn().AddChild(toggle);

			GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
				$"[FrameCost] toggle built in this scene — tick '{toggle.Text}' beside the DEV time selector to arm " +
				$"whole-frame timing (reports every {Scripts.Diagnostics.FrameCostProfiler.ReportEveryFrames:N0} simulated frames)."));
		}

		// Mini-plan 08 P1 — arms Scripts/Diagnostics/BetCostProfiler, which times one bet segment by segment.
		//
		// It sits HERE, with the scale selector and the Sim% readout, because the three are one instrument:
		// the selector sets the demand, Sim% says whether the engine met it, and this says WHERE the frame
		// went when it did not. §38.7's standing rule is that a low Sim% means "find what is eating the
		// frame" — this is the thing that answers it, instead of the forbidden reflex of raising
		// MaxBetsPerFrame.
		//
		// DEBUG-only, and absent rather than disabled in an exported build: the profiler's entry points are
		// all Conditional("DEBUG"), so a RELEASE toggle would be a control wired to nothing — a lying
		// control, which the ladder assert above exists to prevent in its own domain.
		//
		// KNOWN AND DELIBERATELY NOT FIXED (developer's call, 2026-08-30): it is effectively unclickable
		// WHILE an autobet runs at a high DEV scale. Nothing disables it — ApplyRunLock does not touch this
		// subtree — the frame is simply saturated (§38.7 measured this world pinned at ~133 ms/frame, i.e.
		// ~7 fps), so a click lands late or not at all. Idle, it works normally.
		//
		// It does not need fixing because the protocol never asks for a mid-run press: arm BEFORE starting
		// the autobet, which also captures from bet #1 rather than from wherever the click lands. The one
		// consequence to remember is that DISARMING before a P2 throughput sweep means stopping the autobet
		// first — which P2 does anyway, being a separate run.
		[System.Diagnostics.Conditional("DEBUG")]
		private void AddBetCostToggle()
		{
			var toggle = new CheckButton
			{
				Text = "⏱ Bet cost",
				// Default OFF is load-bearing, not a preference: the profiler adds a few percent to every
				// bet, and mini-plan 08's P2 measures the throughput frontier — where that few percent is
				// precisely the quantity under test. Arm it for P1, read the breakdown, disarm it for P2.
				// The PROFILER defaults off; the toggle only MIRRORS it. Its state is static and outlives this
				// scene, so a toggle built as `false` on re-entry showed an armed profiler as off (mini-plan 09
				// P1's run). Set before Toggled is connected, so mirroring can never re-arm anything.
				ButtonPressed = Scripts.Diagnostics.BetCostProfiler.Enabled,
				TooltipText =
					"DEV — time one bet segment by segment (P1). Reports to the Godot editor's Output panel "
					+ "and to user://logs/bet_cost_trace.csv. Leave it OFF while measuring throughput: it "
					+ "costs a few percent of every bet.",
			};
			toggle.AddThemeFontSizeOverride("font_size", 16);
			toggle.Toggled += pressed => Scripts.Diagnostics.BetCostProfiler.Arm(pressed);
			DiagnosticColumn().AddChild(toggle);

			// Announce that the CONTROL exists, separately from the profiler announcing that it is armed.
			// The first P1b attempt produced no [BetCost] output at all, and that silence had two possible
			// causes which no amount of staring at the log could separate: the toggle was never pressed, or
			// the toggle was never reachable in this scene's layout. One line here splits them — if this
			// prints and no ARMED line follows, the control exists and was not used.
			//
			// This is the DEBUG-canary rule applied to a UI affordance rather than to a check: a control
			// whose absence and whose non-use look identical in the log is not diagnosable.
			GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
				$"[BetCost] toggle built in this scene — tick '{toggle.Text}' beside the DEV time selector " +
				$"to arm per-bet segment timing (reports every " +
				$"{Scripts.Diagnostics.BetCostProfiler.ReportEveryBets:N0} player bets)."));
		}

		private void OnScaleSelected(long index)
		{
			if (_calendar != null && index >= 0 && index < Multipliers.Length)
			{
				// The item list is built from Multipliers in order, so the index maps straight back into it.
				// Do not restate the ladder's values here — they live in exactly one place, that array.
				_calendar.RequestedDevTimeScale = Multipliers[index];
			}
		}
	}
}
