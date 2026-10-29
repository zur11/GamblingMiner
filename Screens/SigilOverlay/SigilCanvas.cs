using Godot;
using Scripts.Numerology;

// Graphic Numerology — the sigil being drawn, on its planetary square (plan §3.2, D-GN.6 to D-GN.8, P-GN.6).
// Repainted only when the drawing changes (SigilOverlay calls QueueRedraw on PlayerSigilProgress.Changed): lines move
// on a hit, a restart or a completed pass, never on an ordinary bet.
public partial class SigilCanvas : Control
{
	// Placeholders for the day / night / eclipse textures still to be made (P-GN.6).
	private static readonly Color DayBackground = new(0.80f, 0.88f, 0.98f);
	private static readonly Color NightBackground = new(0.06f, 0.08f, 0.20f);
	private static readonly Color EclipseBackground = new(0.10f, 0.06f, 0.05f);
	private static readonly Color EclipseRim = new(1.00f, 0.55f, 0.15f);
	private static readonly Color StartCircle = new(0.20f, 0.75f, 0.30f);   // a set's first number (source convention)
	private static readonly Color EndCircle = new(0.85f, 0.20f, 0.20f);     // its last

	public SigilProgressState State { get; set; }

	// The rainbow of D-GN.7: 95% red … ~48% green … 1% violet, one hue step per chance.
	public static Color ColorForChance(int chance)
	{
		float t = (95 - Mathf.Clamp(chance, 1, 95)) / 94f;
		return Color.FromHsv(t * 0.76f, 0.85f, 0.95f);
	}

	public override void _Draw()
	{
		var state = State;
		if (state == null || state.AllComplete)
			return;

		var square = PlanetarySquares.PlanetOfSigil(state.SigilIndex);
		var kind = PlanetarySquares.KindOfSigil(state.SigilIndex);
		int n = square.N;
		float side = Mathf.Min(Size.X, Size.Y);
		float cell = side / n;
		var origin = new Vector2((Size.X - side) / 2f, (Size.Y - side) / 2f);
		bool night = kind == SigilKind.Nocturnal;
		bool eclipsed = square.IsEclipsed(kind);

		Color background = eclipsed ? EclipseBackground : night ? NightBackground : DayBackground;
		Color ink = night || eclipsed ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.35f);
		DrawRect(new Rect2(origin, new Vector2(side, side)), background);
		if (eclipsed)
			DrawRect(new Rect2(origin, new Vector2(side, side)), EclipseRim, false, 4f);

		// The grid and its numbers, faint: the sigil is the subject, the square its ground.
		for (int i = 1; i < n; i++)
		{
			DrawLine(origin + new Vector2(i * cell, 0), origin + new Vector2(i * cell, side), ink, 1f);
			DrawLine(origin + new Vector2(0, i * cell), origin + new Vector2(side, i * cell), ink, 1f);
		}

		Font font = ThemeDB.FallbackFont;
		int fontSize = Mathf.Max(12, (int)(cell * 0.30f));
		for (int r = 0; r < n; r++)
			for (int c = 0; c < n; c++)
			{
				var cellOrigin = origin + new Vector2(c * cell, r * cell);
				DrawString(font, cellOrigin + new Vector2(0, cell * 0.30f), square.ValueAt(r, c).ToString(),
					HorizontalAlignment.Center, cell, fontSize, ink);
			}

		Vector2 Center(int number)
		{
			var (row, col) = square.PositionOf(number);
			return origin + new Vector2((col + 0.5f) * cell, (row + 0.5f) * cell);
		}

		float width = Mathf.Max(3f, cell * 0.07f);
		for (int set = 0; set < n; set++)
		{
			var progress = state.Sets[set];
			bool inProgress = set == state.SetIndex && state.Step > 0;

			// A finished set is violet; an unfinished one carries the colour of its latest completed pass (D-GN.8);
			// a set with neither, and no pass in progress, is not drawn — its silhouette appears with its first hit.
			if (progress.Finished || progress.LastPassChance > 0)
			{
				Color color = progress.Finished ? ColorForChance(1) : ColorForChance(progress.LastPassChance);
				DrawSet(square, kind, set, n, color, width, Center);
			}

			if (inProgress)
			{
				// The silhouette in the colour this pass will earn, faint; the lines already hit, solid on top.
				Color passColor = ColorForChance(state.PassChance);
				DrawSet(square, kind, set, n, new Color(passColor, 0.30f), width, Center);
				for (int k = 1; k < state.Step; k++)
					DrawLine(Center(square.NumberAt(kind, set, k - 1)), Center(square.NumberAt(kind, set, k)), passColor, width, true);
			}
		}

		// The next number to hit, ringed.
		int target = SigilRules.CurrentTarget(state);
		DrawArc(Center(target), cell * 0.38f, 0f, Mathf.Tau, 48, new Color(1f, 0.85f, 0.2f), Mathf.Max(2f, cell * 0.04f), true);
	}

	private void DrawSet(PlanetarySquare square, SigilKind kind, int set, int n, Color color, float width,
		System.Func<int, Vector2> center)
	{
		for (int k = 1; k < n; k++)
			DrawLine(center(square.NumberAt(kind, set, k - 1)), center(square.NumberAt(kind, set, k)), color, width, true);

		float radius = width * 1.6f;
		DrawCircle(center(square.NumberAt(kind, set, 0)), radius, new Color(StartCircle, color.A));
		DrawCircle(center(square.NumberAt(kind, set, n - 1)), radius, new Color(EndCircle, color.A));
	}
}
