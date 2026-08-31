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

			int current = System.Array.IndexOf(Multipliers, _calendar?.DevTimeScale ?? 1);
			_selector.Select(current < 0 ? 0 : current);
			_selector.ItemSelected += OnScaleSelected;
			AddChild(_selector);

			// The retention readout belongs beside the control that causes the saturation: this selector is
			// what asks for 90× the work, and Sim% is what says whether the engine is actually delivering it.
			// It is also the only way the reading reaches DICEGAME, which renders its own balance labels and
			// has no StatusBar to host it.
			AddChild(new UI.SimRetentionReadout.SimRetentionReadout(18));

			AddBetCostToggle();
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
		[System.Diagnostics.Conditional("DEBUG")]
		private void AddBetCostToggle()
		{
			var toggle = new CheckButton
			{
				Text = "⏱ Bet cost",
				// Default OFF is load-bearing, not a preference: the profiler adds a few percent to every
				// bet, and mini-plan 08's P2 measures the throughput frontier — where that few percent is
				// precisely the quantity under test. Arm it for P1, read the breakdown, disarm it for P2.
				ButtonPressed = false,
				TooltipText =
					"DEV — time one bet segment by segment (P1). Reports to the Godot editor's Output panel "
					+ "and to user://logs/bet_cost_trace.csv. Leave it OFF while measuring throughput: it "
					+ "costs a few percent of every bet.",
			};
			toggle.AddThemeFontSizeOverride("font_size", 16);
			toggle.Toggled += pressed => Scripts.Diagnostics.BetCostProfiler.Arm(pressed);
			AddChild(toggle);
		}

		private void OnScaleSelected(long index)
		{
			if (_calendar != null && index >= 0 && index < Multipliers.Length)
			{
				// The item list is built from Multipliers in order, so the index maps straight back into it.
				// Do not restate the ladder's values here — they live in exactly one place, that array.
				_calendar.DevTimeScale = Multipliers[index];
			}
		}
	}
}
