using Godot;
using System;
using System.Globalization;

namespace UI.SimRetentionReadout
{
	// DEV/TEST ONLY — shows CalendarTimeService.SimulationThrottle, the fraction of last frame's simulated
	// time the bet engine actually RETAINED. §38.7's standing rule is that this is THE diagnostic for frame
	// saturation, and that a low reading means "find what is eating the frame", never "raise
	// MaxBacklogSeconds / MaxBetsPerFrame" — which only hands a saturated frame more work.
	//
	// Before this existed the value was visible only in difficulty_trace.csv, one row per MINED BLOCK, which
	// is far too coarse to attribute a slowdown to the screen you are standing on. It was written as a
	// StatusBar cell first, then extracted here because DiceGame — the scene that matters most for this
	// measurement — has NO StatusBar (it renders its own balance labels), so the reading was invisible
	// exactly where it was most needed. Built programmatically like its two hosts.
	//
	// Hosts: StatusBar (every scene that has one) and DevTimeScaleSelector (DiceGame + BlockExplorer, right
	// beside the control that causes the saturation being measured).
	public partial class SimRetentionReadout : Label
	{
		// Below this the reading is amber, so a collapse is legible at a glance without reading the number.
		private const double HealthyFloor = 0.90;
		private static readonly Color WarnColor = new(1f, 0.72f, 0.20f);

		private readonly int _fontSize;

		private CalendarTimeService _calendar;
		// Repaint only when the displayed integer changes — this runs every frame.
		private int _lastShownPercent = int.MinValue;

		public SimRetentionReadout() : this(22)
		{
		}

		public SimRetentionReadout(int fontSize)
		{
			_fontSize = fontSize;
		}

		public override void _Ready()
		{
			AddThemeFontSizeOverride("font_size", _fontSize);
			_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
			Visible = false;
		}

		public override void _Process(double delta)
		{
			// Hidden unless a simulation is actually running: outside a delegated autobet nothing is being
			// dropped and the value is an uninformative, permanent 1.0.
			if (_calendar?.IsAutobetActive != true)
			{
				if (Visible)
				{
					Visible = false;
					_lastShownPercent = int.MinValue;
				}
				return;
			}

			Visible = true;

			double retention = Math.Clamp(_calendar.SimulationThrottle, 0.0, 1.0);
			int percent = (int)Math.Round(retention * 100.0);
			if (percent == _lastShownPercent)
			{
				return;
			}

			_lastShownPercent = percent;
			// Not a currency, so no SC/BTC suffix — the "Sim" prefix and the "%" say what it is.
			Text = string.Create(CultureInfo.InvariantCulture, $"Sim: {percent}%");
			AddThemeColorOverride("font_color", retention < HealthyFloor ? WarnColor : Colors.White);
		}
	}
}
