using System;
using System.Collections.Generic;

namespace Scripts.Numerology
{
	public enum SigilKind
	{
		Diurnal,   // steps of 1 (D-GN.2)
		Nocturnal  // steps of n
	}

	public sealed class PlanetarySquare
	{
		public string Planet { get; }
		public int N { get; }
		public string DiurnalSigilName { get; }
		public string NocturnalSigilName { get; }

		private readonly int[,] _values;
		private readonly (int Row, int Col)[] _positions;   // index = number, 1..N²

		internal PlanetarySquare(string planet, string diurnalSigilName, string nocturnalSigilName, int[,] values)
		{
			Planet = planet;
			N = values.GetLength(0);
			DiurnalSigilName = diurnalSigilName;
			NocturnalSigilName = nocturnalSigilName;
			_values = values;

			// A transcription typo must fail here, loudly, not surface as a sigil that can never be finished.
			AssertMagic();

			_positions = new (int, int)[N * N + 1];
			for (int r = 0; r < N; r++)
				for (int c = 0; c < N; c++)
					_positions[values[r, c]] = (r, c);
		}

		// n(n² + 1) / 2 — a consequence of N, never a second written figure (Standing Convention 15).
		public int MagicConstant => N * (N * N + 1) / 2;

		public int ValueAt(int row, int col) => _values[row, col];

		// Row 0 is the top, column 0 the left: the plan's cell "B3" is (row 2, col 1).
		public (int Row, int Col) PositionOf(int number) => _positions[number];

		public string SigilName(SigilKind kind) => kind == SigilKind.Diurnal ? DiurnalSigilName : NocturnalSigilName;

		// The number at step `step` of set `set` (both 0-based), in drawing order (D-GN.6, D-GN.17).
		// Diurnal set g: (g−1)·n + 1 … g·n. Nocturnal set g: g, g + n … g + (n−1)·n. Computed, not stored: it runs on
		// every bet.
		public int NumberAt(SigilKind kind, int set, int step) =>
			kind == SigilKind.Diurnal ? set * N + step + 1 : set + 1 + step * N;

		private void AssertMagic()
		{
			int k = MagicConstant;
			int diagonal = 0, antiDiagonal = 0;
			var seen = new bool[N * N + 1];
			for (int i = 0; i < N; i++)
			{
				int row = 0, col = 0;
				for (int j = 0; j < N; j++)
				{
					int v = _values[i, j];
					if (v < 1 || v > N * N || seen[v])
						throw new InvalidOperationException($"{Planet} square: {v} is out of range or repeated.");
					seen[v] = true;
					row += v;
					col += _values[j, i];
				}

				if (row != k || col != k)
					throw new InvalidOperationException($"{Planet} square: row or column {i + 1} does not sum to {k}.");
				diagonal += _values[i, i];
				antiDiagonal += _values[i, N - 1 - i];
			}

			if (diagonal != k || antiDiagonal != k)
				throw new InvalidOperationException($"{Planet} square: a diagonal does not sum to {k}.");
		}
	}

	// Graphic Numerology (AIHelperFiles/graphic_numerology-plan.md) — the ONE declaration of the seven planetary squares
	// and their zodiac sigils (P-GN.1). Everything else is derived from it; the plan's Appendix A is superseded by it.
	// Copied from the developer's Numerología Gráfica project (web/sigilos.html, CUADRADOS), where every value is verified.
	public static class PlanetarySquares
	{
		// Unlock order (D-GN.18): smallest to largest. Index = planet index.
		public static readonly IReadOnlyList<PlanetarySquare> All = Build();

		// Two sigils per planet, diurnal first (D-GN.16): sigil index = planet index × 2 + kind.
		public static int SigilCount => All.Count * 2;

		public static PlanetarySquare PlanetOfSigil(int sigilIndex) => All[sigilIndex / 2];

		public static SigilKind KindOfSigil(int sigilIndex) => (SigilKind)(sigilIndex % 2);

		private static IReadOnlyList<PlanetarySquare> Build()
		{
			return new[]
			{
				new PlanetarySquare("Saturn", "Aquarius Sigil", "Capricorn Sigil", new[,]
				{
					{ 4, 9, 2 },
					{ 3, 5, 7 },
					{ 8, 1, 6 },
				}),
				new PlanetarySquare("Jupiter", "Sagittarius Sigil", "Pisces Sigil", new[,]
				{
					{ 4, 14, 15, 1 },
					{ 9, 7, 6, 12 },
					{ 5, 11, 10, 8 },
					{ 16, 2, 3, 13 },
				}),
				new PlanetarySquare("Mars", "Aries Sigil", "Scorpio Sigil", new[,]
				{
					{ 11, 24, 7, 20, 3 },
					{ 4, 12, 25, 8, 16 },
					{ 17, 5, 13, 21, 9 },
					{ 10, 18, 1, 14, 22 },
					{ 23, 6, 19, 2, 15 },
				}),
				// The Sun has no night domicile and the Moon no day domicile: the empty slot takes the luminary's own
				// sign, eclipsed (D-GN.3).
				new PlanetarySquare("Sun", "Leo Sigil", "Eclipsed Leo Sigil", new[,]
				{
					{ 6, 32, 3, 34, 35, 1 },
					{ 7, 11, 27, 28, 8, 30 },
					{ 19, 14, 16, 15, 23, 24 },
					{ 18, 20, 22, 21, 17, 13 },
					{ 25, 29, 10, 9, 26, 12 },
					{ 36, 5, 33, 4, 2, 31 },
				}),
				new PlanetarySquare("Venus", "Libra Sigil", "Taurus Sigil", new[,]
				{
					{ 22, 47, 16, 41, 10, 35, 4 },
					{ 5, 23, 48, 17, 42, 11, 29 },
					{ 30, 6, 24, 49, 18, 36, 12 },
					{ 13, 31, 7, 25, 43, 19, 37 },
					{ 38, 14, 32, 1, 26, 44, 20 },
					{ 21, 39, 8, 33, 2, 27, 45 },
					{ 46, 15, 40, 9, 34, 3, 28 },
				}),
				new PlanetarySquare("Mercury", "Virgo Sigil", "Gemini Sigil", new[,]
				{
					{ 8, 58, 59, 5, 4, 62, 63, 1 },
					{ 49, 15, 14, 52, 53, 11, 10, 56 },
					{ 41, 23, 22, 44, 45, 19, 18, 48 },
					{ 32, 34, 35, 29, 28, 38, 39, 25 },
					{ 40, 26, 27, 37, 36, 30, 31, 33 },
					{ 17, 47, 46, 20, 21, 43, 42, 24 },
					{ 9, 55, 54, 12, 13, 51, 50, 16 },
					{ 64, 2, 3, 61, 60, 6, 7, 57 },
				}),
				new PlanetarySquare("Moon", "Eclipsed Cancer Sigil", "Cancer Sigil", new[,]
				{
					{ 37, 78, 29, 70, 21, 62, 13, 54, 5 },
					{ 6, 38, 79, 30, 71, 22, 63, 14, 46 },
					{ 47, 7, 39, 80, 31, 72, 23, 55, 15 },
					{ 16, 48, 8, 40, 81, 32, 64, 24, 56 },
					{ 57, 17, 49, 9, 41, 73, 33, 65, 25 },
					{ 26, 58, 18, 50, 1, 42, 74, 34, 66 },
					{ 67, 27, 59, 10, 51, 2, 43, 75, 35 },
					{ 36, 68, 19, 60, 11, 52, 3, 44, 76 },
					{ 77, 28, 69, 20, 61, 12, 53, 4, 45 },
				}),
			};
		}
	}
}
