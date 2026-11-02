using Godot;
using Scripts.Numerology;

// Graphic Numerology — a sigil's shape, small, for the collection (D-GN.24): every set's path on the sky of its square, in
// one colour. A combined figure draws both sigils of a planet in the same colour on a neutral ground (D-GN.27). Repainted
// only when what it shows changes, which is a level at the most, never a bet.
public partial class SigilThumbnail : Control
{
	private const int NoSigil = -1;
	private static readonly Color CombinedBackground = new(0.11f, 0.11f, 0.13f);

	private int _first = NoSigil;
	private int _second = NoSigil;
	private Color _ink;

	public void SetSigil(int sigilIndex, Color ink) => SetFigure(sigilIndex, NoSigil, ink);

	public void SetCombined(int first, int second, Color ink) => SetFigure(first, second, ink);

	private void SetFigure(int first, int second, Color ink)
	{
		if (first == _first && second == _second && ink == _ink)
			return;
		_first = first;
		_second = second;
		_ink = ink;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_first == NoSigil)
			return;

		var square = PlanetarySquares.PlanetOfSigil(_first);
		var kind = PlanetarySquares.KindOfSigil(_first);
		bool combined = _second != NoSigil;
		float side = Mathf.Min(Size.X, Size.Y);
		var origin = new Vector2((Size.X - side) / 2f, (Size.Y - side) / 2f);

		DrawRect(new Rect2(origin, new Vector2(side, side)), combined ? CombinedBackground : SigilCanvas.GroundFor(square, kind));
		if (!combined && square.IsEclipsed(kind))
			DrawRect(new Rect2(origin, new Vector2(side, side)), SigilCanvas.EclipseRim, false, 2f);

		DrawSets(square, kind, origin, side);
		if (combined)
			DrawSets(square, PlanetarySquares.KindOfSigil(_second), origin, side);
	}

	private void DrawSets(PlanetarySquare square, SigilKind kind, Vector2 origin, float side)
	{
		int n = square.N;
		float cell = side / n;
		float width = Mathf.Max(1.5f, cell * 0.09f);

		Vector2 Center(int number)
		{
			var (row, col) = square.PositionOf(number);
			return origin + new Vector2((col + 0.5f) * cell, (row + 0.5f) * cell);
		}

		for (int set = 0; set < n; set++)
			for (int k = 1; k < n; k++)
				DrawLine(Center(square.NumberAt(kind, set, k - 1)), Center(square.NumberAt(kind, set, k)), _ink, width, true);
	}
}
