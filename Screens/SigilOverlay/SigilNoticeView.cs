using System;
using Godot;
using Scripts.Numerology;

// Graphic Numerology — the temporary notice of D-GN.29: the figure, small, in its level's colour, labelled "Activate Saturn
// Sigil", for 10 minutes of game time after the lower of a pair reaches level 1. On DiceGame and on the collection.
//
// The bet that fires it may run with no scene watching (SimulationService survives scene changes), so a view entering the tree
// asks whether one is live. It is shown by the event, never by a sample: at 9000X it lives about 4 frames, and a 4 Hz sample
// could miss it whole. Its end is Pattern 6's hybrid: one comparison per frame, and only while it is showing.
public partial class SigilNoticeView : HBoxContainer
{
	private const int ThumbnailSide = 72;
	private const int FontSize = 24;

	private CalendarTimeService _calendar;
	private SigilThumbnail _thumbnail;
	private Label _label;

	public override void _Ready()
	{
		_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		AddThemeConstantOverride("separation", 12);

		_thumbnail = new SigilThumbnail
		{
			CustomMinimumSize = new Vector2(ThumbnailSide, ThumbnailSide),
			MouseFilter = MouseFilterEnum.Pass
		};
		AddChild(_thumbnail);

		_label = new Label { VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Pass };
		_label.AddThemeFontSizeOverride("font_size", FontSize);
		AddChild(_label);

		PlayerSigilProgress.NoticeFired += ShowNotice;
		int live = PlayerSigilProgress.NoticePlanetAt(NowUtc());
		if (live >= 0)
			ShowNotice(live);
		else
			HideNotice();
	}

	public override void _ExitTree()
	{
		// A static event outlives this scene; an unremoved handler would keep a freed view alive.
		PlayerSigilProgress.NoticeFired -= ShowNotice;
	}

	public override void _Process(double delta)
	{
		if (PlayerSigilProgress.NoticePlanetAt(NowUtc()) < 0)
			HideNotice();
	}

	private void ShowNotice(int planetIndex)
	{
		// D-GN.29: level 1 only, so the colour of level 1; a combined figure's own card shows its current level.
		Color color = SigilCanvas.ColorForLevel(1);
		int first = planetIndex * 2;
		_thumbnail.SetCombined(first, SigilRules.PartnerOf(first), color);
		_label.Text = "Activate " + PlanetarySquares.All[planetIndex].PlanetarySigilName;
		_label.AddThemeColorOverride("font_color", color);
		Visible = true;
		SetProcess(true);
	}

	private void HideNotice()
	{
		Visible = false;
		SetProcess(false);
	}

	private DateTime NowUtc() => _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow;
}
