using Godot;
using Scripts.Numerology;

// Graphic Numerology — the 95-level rainbow of D-GN.22 under the sigil: the levels the set being drawn has reached are
// bright, a level the next pass would add is half-bright, the rest dim. A white mark above the set's level, a gold one
// above where the next pass would take it. Repainted by SigilOverlay only when either level changes.
public partial class SigilColorStrip : Control
{
	private const float MarkHeight = 12f;
	private static readonly Color CurrentMark = new(1f, 1f, 1f);
	private static readonly Color PreviewMark = new(1f, 0.85f, 0.2f);

	public int CurrentLevel { get; private set; } = -1;
	public int PreviewLevel { get; private set; } = -1;

	// A preview no higher than the current level draws no gold mark: the next pass reaches no new level.
	public void SetLevels(int currentLevel, int previewLevel)
	{
		if (currentLevel == CurrentLevel && previewLevel == PreviewLevel)
			return;
		CurrentLevel = currentLevel;
		PreviewLevel = previewLevel;
		QueueRedraw();
	}

	public override void _Draw()
	{
		int levels = SigilRules.MaxLevel;
		float cellWidth = Size.X / levels;
		float barTop = MarkHeight + 2f;
		float barHeight = Size.Y - barTop;

		for (int k = 1; k <= levels; k++)
		{
			float alpha = k <= CurrentLevel ? 1f : k <= PreviewLevel ? 0.55f : 0.18f;
			// One pixel of overlap so no seam shows between cells at fractional widths.
			DrawRect(new Rect2((k - 1) * cellWidth, barTop, cellWidth + 1f, barHeight), new Color(SigilCanvas.ColorForLevel(k), alpha));
		}

		if (PreviewLevel > CurrentLevel)
			DrawMark(PreviewLevel, cellWidth, PreviewMark);
		if (CurrentLevel > 0)
			DrawMark(CurrentLevel, cellWidth, CurrentMark);
	}

	private void DrawMark(int level, float cellWidth, Color color)
	{
		float x = (level - 0.5f) * cellWidth;
		DrawColoredPolygon(new[] { new Vector2(x - 6f, 0f), new Vector2(x + 6f, 0f), new Vector2(x, MarkHeight) }, color);
	}
}
