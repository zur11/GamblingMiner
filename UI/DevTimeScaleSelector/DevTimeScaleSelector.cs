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
		private const int MaxScale = 10; // 10 options: 100X (×1) … 1000X (×10)

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
			for (int scale = 1; scale <= MaxScale; scale++)
			{
				_selector.AddItem($"{scale * 100}X");
			}

			int current = Mathf.Clamp((_calendar?.DevTimeScale ?? 1) - 1, 0, MaxScale - 1);
			_selector.Select(current);
			_selector.ItemSelected += OnScaleSelected;
			AddChild(_selector);
		}

		private void OnScaleSelected(long index)
		{
			if (_calendar != null)
			{
				_calendar.DevTimeScale = (int)index + 1; // index 0 → ×1 (100X), index 9 → ×10 (1000X)
			}
		}
	}
}
