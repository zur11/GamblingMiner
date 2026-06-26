using Godot;

namespace UI.DevTimeScaleSelector
{
	// DEV/TEST ONLY — a small selector (label + OptionButton) to accelerate the simulation from the 100X
	// base up to 1000X, in 10 steps. It drives CalendarTimeService.DevTimeScale, which scales BOTH the
	// calendar clock and the bet-execution rate by the same factor, leaving the difficulty / power /
	// solvetime dynamics invariant (only wall-clock time compresses). Built programmatically (like StatusBar)
	// so it can be dropped into any screen without editing its .tscn. Not persisted; resets to 100X on restart.
	public partial class DevTimeScaleSelector : HBoxContainer
	{
		// DevTimeScale multipliers on the 100X base clock: 100X, then 1000X..9000X in 1000X steps.
		// (Capped at 9000X — 10000X hit the MaxBetsPerFrame throughput ceiling and lagged.)
		private static readonly int[] Multipliers = { 1, 10, 20, 30, 40, 50, 60, 70, 80, 90 };

		private OptionButton _selector;
		private CalendarTimeService _calendar;

		public override void _Ready()
		{
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
		}

		private void OnScaleSelected(long index)
		{
			if (_calendar != null && index >= 0 && index < Multipliers.Length)
			{
				_calendar.DevTimeScale = Multipliers[index]; // index 0 → ×1 (100X) … index 9 → ×90 (9000X)
			}
		}
	}
}
