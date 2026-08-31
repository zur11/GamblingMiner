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
		//   ×1..×6  — a FINE low range (100X..600X), added 2026-08-30 for mini-plan 08. The throughput
		//             frontier is `credits × DevTimeScale`, so at the 99-credit hardware cap the highest
		//             sustainable scale sits near the TOP of this range, not in the coarse one — with only
		//             ×1 and ×10 on offer, the entire frontier fell in a gap the selector could not express
		//             and P2's sweep had no grid to sweep. See §2 of
		//             AIHelperFiles/mini08-timestamp-fidelity-and-throughput-limits-plan.md.
		//   ×10..×90 — the original coarse range, for the low-credit runs that saturate nowhere near it.
		//
		// (Capped at ×90 — 10000X hit the MaxBetsPerFrame throughput ceiling and lagged.)
		//
		// This ladder is a CONVENIENCE, not the limit. The limit is
		// CalendarTimeService.MaxGameSecondsPerRealSecond, enforced where the rate is spent, because this
		// selector is only one of two factors in it — see that constant's note. Shortening this array does
		// not lower the ceiling and lengthening it does not raise one.
		private static readonly int[] Multipliers = { 1, 2, 3, 4, 5, 6, 10, 20, 30, 40, 50, 60, 70, 80, 90 };

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
