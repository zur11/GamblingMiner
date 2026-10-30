using Godot;
using Scripts.Numerology;

// Graphic Numerology — the 95-step rainbow of D-GN.22 under the sigil: the steps the set being drawn has reached are
// bright, the ones the next pass would add are half-bright, the rest dim. A white mark above the set's step, a gold one
// above where the next pass would take it. Repainted by SigilOverlay only when either step changes.
public partial class SigilColorStrip : Control
{
	private const float MarkHeight = 12f;
	private static readonly Color CurrentMark = new(1f, 1f, 1f);
	private static readonly Color PreviewMark = new(1f, 0.85f, 0.2f);

	public int CurrentStep { get; private set; } = -1;
	public int PreviewStep { get; private set; } = -1;

	// 0 = no colour yet; a preview of 0 or below the current step draws no gold mark (nothing to preview).
	public void SetSteps(int currentStep, int previewStep)
	{
		if (currentStep == CurrentStep && previewStep == PreviewStep)
			return;
		CurrentStep = currentStep;
		PreviewStep = previewStep;
		QueueRedraw();
	}

	public override void _Draw()
	{
		int steps = SigilRules.ColorSteps;
		float cellWidth = Size.X / steps;
		float barTop = MarkHeight + 2f;
		float barHeight = Size.Y - barTop;

		for (int k = 1; k <= steps; k++)
		{
			float alpha = k <= CurrentStep ? 1f : k <= PreviewStep ? 0.55f : 0.18f;
			// One pixel of overlap so no seam shows between cells at fractional widths.
			DrawRect(new Rect2((k - 1) * cellWidth, barTop, cellWidth + 1f, barHeight), new Color(SigilCanvas.ColorForStep(k), alpha));
		}

		if (PreviewStep > CurrentStep)
			DrawMark(PreviewStep, cellWidth, PreviewMark);
		if (CurrentStep > 0)
			DrawMark(CurrentStep, cellWidth, CurrentMark);
	}

	private void DrawMark(int step, float cellWidth, Color color)
	{
		float x = (step - 0.5f) * cellWidth;
		DrawColoredPolygon(new[] { new Vector2(x - 6f, 0f), new Vector2(x + 6f, 0f), new Vector2(x, MarkHeight) }, color);
	}
}
