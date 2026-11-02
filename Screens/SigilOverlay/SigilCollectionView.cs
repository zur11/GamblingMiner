using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Scripts.Numerology;

// Graphic Numerology — the collection, the second view inside SigilOverlay (D-GN.24 to D-GN.27): the fourteen sigils in
// alphabetical order, so the layout does not give the pairs away; a DEV button to acquire one and a Play button to put it
// in the slot; below them, the combined figures that already exist, and no trace of the others.
//
// Ch. 29 Pattern A: many controls in one ScrollContainer, the slot line and the overlay's footer outside it. Every card
// is built once, on the first look (mini-plan 21 E), and SigilOverlay calls Refresh only on an acquisition, a switch, a
// restore or a level, so a bet repaints nothing here. Refresh sets no scroll position, so the list stays where it was.
public partial class SigilCollectionView : VBoxContainer
{
	private const int NotOwned = -1;
	private const int ThumbnailSide = 104;
	private const int CardMinWidth = 850;
	private const int CardMargin = 8;
	private const int NameFontSize = 22;
	private const int LevelFontSize = 18;
	private const int DescriptionFontSize = 17;
	// Two lines at DescriptionFontSize: no description wraps further at CardMinWidth, and a wrapped Label reports its
	// height late, so its room is reserved (plan §10.6).
	private const int DescriptionHeight = 52;
	private const string MutedColor = "#9a9a9a";
	private const string WarningColor = "#ff9a3c";

	private sealed class SigilCard
	{
		public int SigilIndex;
		public SigilThumbnail Thumbnail;
		public Label Level;
		public Button Acquire;
		public Button Play;
		public int PaintedLevel = int.MinValue;
	}

	private sealed class CombinedCard
	{
		public int PlanetIndex;
		public Control Root;
		public SigilThumbnail Thumbnail;
		public Label Name;
		public Label Level;
		public Label Members;
	}

	// The player pressed Play and the sigil is in the slot: the overlay goes on to draw it.
	public event Action SigilPlayed;

	private Label _title;
	private RichTextLabel _slotLabel;
	private Button _confirmPlayButton;
	private Button _cancelPlayButton;
	private GridContainer _sigilGrid;
	private Label _combinedHeader;
	private GridContainer _combinedGrid;

	private readonly List<SigilCard> _sigilCards = new();
	private readonly List<CombinedCard> _combinedCards = new();
	private StyleBoxFlat _cardStyle;

	// The sigil a Play is waiting on, while the warning asks whether to lose the pass in progress.
	private int? _pendingSigil;
	private string _paintedSlotText;

	public override void _Ready()
	{
		_title = GetNode<Label>("%SigilTitle");
		_slotLabel = GetNode<RichTextLabel>("%SigilSlotLabel");
		_confirmPlayButton = GetNode<Button>("%ConfirmPlayButton");
		_cancelPlayButton = GetNode<Button>("%CancelPlayButton");
		_sigilGrid = GetNode<GridContainer>("%SigilCollectionGrid");
		_combinedHeader = GetNode<Label>("%CombinedHeader");
		_combinedGrid = GetNode<GridContainer>("%CombinedGrid");

		_confirmPlayButton.Pressed += OnConfirmPlayPressed;
		_cancelPlayButton.Pressed += CancelPending;
	}

	// Called by SigilOverlay when this view is shown, and whenever its dirty flag is consumed.
	public void Refresh()
	{
		if (_sigilCards.Count == 0)
			BuildCards();

		_title.Text = string.Create(CultureInfo.InvariantCulture,
			$"The sigil collection  —  {PlayerSigilProgress.Collection.Owned.Count} of {PlanetarySquares.SigilCount} owned");

		foreach (var card in _sigilCards)
			RefreshSigilCard(card);

		bool anyCombined = false;
		foreach (var card in _combinedCards)
			anyCombined |= RefreshCombinedCard(card);
		// D-GN.24, D-GN.27: until a figure exists, nothing on this screen may say that sigils combine, a header included.
		_combinedHeader.Visible = anyCombined;

		PaintSlotLine();
	}

	// SigilOverlay's 4 Hz: only a waiting warning moves between refreshes, since it names the pass as it stands now.
	public void SamplePending()
	{
		if (_pendingSigil != null)
			PaintSlotLine();
	}

	public void CancelPending()
	{
		_pendingSigil = null;
		_confirmPlayButton.Visible = false;
		_cancelPlayButton.Visible = false;
		if (_sigilCards.Count > 0)
			PaintSlotLine();
	}

	private void RefreshSigilCard(SigilCard card)
	{
		var state = PlayerSigilProgress.Find(card.SigilIndex);
		bool inSlot = state != null && state == PlayerSigilProgress.Equipped;
		card.Acquire.Visible = state == null;
		card.Play.Visible = state != null;
		card.Play.Disabled = inSlot;
		card.Play.Text = inSlot ? "In the slot" : "Play";

		int level = state == null ? NotOwned : SigilRules.SigilLevel(state);
		if (level == card.PaintedLevel)
			return;

		card.PaintedLevel = level;
		Color color = state == null ? new Color(MutedColor) : SigilCanvas.ColorForLevel(level);
		card.Thumbnail.SetSigil(card.SigilIndex, color);
		card.Level.Text = state == null
			? "Not owned"
			: string.Create(CultureInfo.InvariantCulture, $"Level {level} of {SigilRules.MaxLevel}{(state.IsComplete ? "  ·  complete" : "")}");
		card.Level.AddThemeColorOverride("font_color", color);
	}

	// A figure's card holds no text at all until the figure exists. Returns whether it does.
	private static bool RefreshCombinedCard(CombinedCard card)
	{
		int first = card.PlanetIndex * 2;
		int second = SigilRules.PartnerOf(first);
		var firstState = PlayerSigilProgress.Find(first);
		var secondState = PlayerSigilProgress.Find(second);
		bool exists = SigilRules.HasCombinedFigure(firstState, secondState);
		card.Root.Visible = exists;
		if (!exists)
			return false;

		int level = SigilRules.CombinedLevel(firstState, secondState);
		Color color = SigilCanvas.ColorForLevel(level);
		card.Thumbnail.SetCombined(first, second, color);
		card.Name.Text = string.Create(CultureInfo.InvariantCulture, $"{PlanetarySquares.All[card.PlanetIndex].Planet}  —  combined figure");
		card.Level.Text = string.Create(CultureInfo.InvariantCulture, $"Level {level} of {SigilRules.MaxLevel}");
		card.Level.AddThemeColorOverride("font_color", color);
		card.Members.Text = string.Create(CultureInfo.InvariantCulture,
			$"{PlanetarySquares.NameOfSigil(first)} at level {SigilRules.SigilLevel(firstState)}  +  {PlanetarySquares.NameOfSigil(second)} at level {SigilRules.SigilLevel(secondState)}");
		return true;
	}

	private void PaintSlotLine()
	{
		string text = _pendingSigil is int pending ? BuildWarning(pending) : BuildSlotStatus();
		if (text == _paintedSlotText)
			return;
		_paintedSlotText = text;
		_slotLabel.Text = text;
	}

	private static string BuildSlotStatus()
	{
		var equipped = PlayerSigilProgress.Equipped;
		if (equipped == null)
			return $"[b]The slot is empty.[/b] [color={MutedColor}]A bet draws nothing until you press Play on a sigil you own.[/color]";

		int level = SigilRules.SigilLevel(equipped);
		string levelColor = SigilCanvas.ColorForLevel(level).ToHtml(false);
		string state = equipped.IsComplete ? "Complete: bets draw nothing until another sigil is in the slot." : "The next bet draws it.";
		return string.Create(CultureInfo.InvariantCulture,
			$"In the slot: [b]{PlanetarySquares.NameOfSigil(equipped.SigilIndex)}[/b], level [color=#{levelColor}][b]{level}[/b][/color] of {SigilRules.MaxLevel}. [color={MutedColor}]{state}[/color]");
	}

	// D-GN.25: a switch loses the pass in progress and nothing else, and this names that pass. A running autobet moves it
	// while the warning waits, so it may also honestly say there is nothing to lose right now.
	private static string BuildWarning(int pending)
	{
		string next = PlanetarySquares.NameOfSigil(pending);
		var leaving = PlayerSigilProgress.Equipped;
		if (leaving == null || leaving.Step == 0)
			return string.Create(CultureInfo.InvariantCulture, $"Playing the [b]{next}[/b] loses nothing right now: no pass is in progress.");

		return string.Create(CultureInfo.InvariantCulture,
			$"[color={WarningColor}]Playing the [b]{next}[/b] loses the {PlanetarySquares.NameOfSigil(leaving.SigilIndex)}'s pass in progress: {DescribePass(leaving)}. Its completed passes stay.[/color]");
	}

	private static string DescribePass(SigilProgressState state) =>
		string.Create(CultureInfo.InvariantCulture,
			$"set {state.SetIndex + 1}, {state.Step} of {PlanetarySquares.PlanetOfSigil(state.SigilIndex).N} numbers hit at {state.PassChance}%");

	private void OnAcquirePressed(int sigilIndex)
	{
		if (!PlayerSigilProgress.Acquire(sigilIndex))
			return;

		GD.Print("[Sigil] acquired — " + PlanetarySquares.NameOfSigil(sigilIndex) + " — " + SigilRules.Describe(PlayerSigilProgress.Collection));
		Refresh();
	}

	private void OnPlayPressed(int sigilIndex)
	{
		if (PlayerSigilProgress.Equipped?.Step > 0)
		{
			_pendingSigil = sigilIndex;
			_confirmPlayButton.Visible = true;
			_cancelPlayButton.Visible = true;
			PaintSlotLine();
			return;
		}

		Switch(sigilIndex);
	}

	private void OnConfirmPlayPressed()
	{
		if (_pendingSigil is int sigilIndex)
			Switch(sigilIndex);
	}

	// Runs between bets, autobet included: the bet loop is on this thread, so the next bet simply draws the new sigil.
	private void Switch(int sigilIndex)
	{
		// Equip does not report the loss, and the sigil leaving is the one thing it changes that the log must show.
		var leaving = PlayerSigilProgress.Equipped;
		string from = leaving == null ? "the empty slot" : PlanetarySquares.NameOfSigil(leaving.SigilIndex);
		string loss = leaving == null ? "nothing to lose"
			: leaving.Step > 0 ? "LOST a pass in progress: " + DescribePass(leaving)
			: "no pass in progress, nothing lost";
		string before = SigilRules.Describe(leaving);

		bool switched = PlayerSigilProgress.Equip(sigilIndex);
		CancelPending();
		if (!switched)
			return;

		GD.Print("[Sigil] switched — " + from + " → " + PlanetarySquares.NameOfSigil(sigilIndex) + ", " + loss
			+ " — before: " + before + " — after: " + SigilRules.Describe(PlayerSigilProgress.Collection));
		SigilPlayed?.Invoke();
	}

	private void BuildCards()
	{
		_cardStyle = new StyleBoxFlat { BgColor = new Color(0.21f, 0.21f, 0.23f, 1f) };
		_cardStyle.SetCornerRadiusAll(4);
		_cardStyle.SetContentMarginAll(CardMargin);

		foreach (int sigilIndex in PlanetarySquares.SigilsByName)
			BuildSigilCard(sigilIndex);

		foreach (int planetIndex in Enumerable.Range(0, PlanetarySquares.All.Count).OrderBy(p => PlanetarySquares.All[p].Planet, StringComparer.Ordinal))
			BuildCombinedCard(planetIndex);
	}

	private void BuildSigilCard(int sigilIndex)
	{
		var card = new SigilCard { SigilIndex = sigilIndex };
		var row = NewCardRow(_sigilGrid, out _, out card.Thumbnail);
		var text = NewTextColumn(row);
		text.AddChild(NewLabel(PlanetarySquares.NameOfSigil(sigilIndex), NameFontSize));
		card.Level = NewLabel("", LevelFontSize);
		text.AddChild(card.Level);
		text.AddChild(NewDescriptionLabel(SigilRules.DescriptionOf(sigilIndex)));

		var buttons = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(190, 0),
			Alignment = BoxContainer.AlignmentMode.Center,
			MouseFilter = MouseFilterEnum.Pass
		};
		row.AddChild(buttons);

		card.Acquire = NewButton("Acquire [DEV]");
		card.Acquire.TooltipText = "DEV — free for now. Prices and channels belong to the next plan.";
		card.Acquire.Pressed += () => OnAcquirePressed(sigilIndex);
		buttons.AddChild(card.Acquire);

		card.Play = NewButton("Play");
		card.Play.Pressed += () => OnPlayPressed(sigilIndex);
		buttons.AddChild(card.Play);

		_sigilCards.Add(card);
	}

	private void BuildCombinedCard(int planetIndex)
	{
		var card = new CombinedCard { PlanetIndex = planetIndex };
		var row = NewCardRow(_combinedGrid, out card.Root, out card.Thumbnail);
		card.Root.Visible = false;
		var text = NewTextColumn(row);
		card.Name = NewLabel("", NameFontSize);
		text.AddChild(card.Name);
		card.Level = NewLabel("", LevelFontSize);
		text.AddChild(card.Level);
		card.Members = NewDescriptionLabel("");
		text.AddChild(card.Members);
		_combinedCards.Add(card);
	}

	// Everything inside the scroll passes the wheel on (Ch. 29, trap 3).
	private HBoxContainer NewCardRow(GridContainer grid, out Control root, out SigilThumbnail thumbnail)
	{
		var panel = new PanelContainer
		{
			CustomMinimumSize = new Vector2(CardMinWidth, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Pass
		};
		panel.AddThemeStyleboxOverride("panel", _cardStyle);
		grid.AddChild(panel);
		root = panel;

		var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Pass };
		row.AddThemeConstantOverride("separation", 12);
		panel.AddChild(row);

		thumbnail = new SigilThumbnail
		{
			CustomMinimumSize = new Vector2(ThumbnailSide, ThumbnailSide),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Pass
		};
		row.AddChild(thumbnail);
		return row;
	}

	private static VBoxContainer NewTextColumn(HBoxContainer row)
	{
		var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Pass };
		text.AddThemeConstantOverride("separation", 0);
		row.AddChild(text);
		return text;
	}

	private static Label NewLabel(string text, int fontSize)
	{
		var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Pass };
		label.AddThemeFontSizeOverride("font_size", fontSize);
		return label;
	}

	private static Label NewDescriptionLabel(string text)
	{
		var label = NewLabel(text, DescriptionFontSize);
		label.AutowrapMode = TextServer.AutowrapMode.Word;
		label.CustomMinimumSize = new Vector2(0, DescriptionHeight);
		label.AddThemeColorOverride("font_color", new Color(MutedColor));
		return label;
	}

	private static Button NewButton(string text)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(190, 44), MouseFilter = MouseFilterEnum.Pass };
		button.AddThemeFontSizeOverride("font_size", 18);
		return button;
	}
}
