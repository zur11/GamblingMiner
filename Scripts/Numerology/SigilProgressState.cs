using System;
using System.Collections.Generic;

namespace Scripts.Numerology
{
	// The player's Graphic Numerology progress (D-GN.11). Plain data with public setters so the block checkpoint can
	// capture and restore it whole; SigilRules is the only code that changes it.
	public sealed class SigilProgressState
	{
		// The sigil being drawn, in unlock order (PlanetarySquares.PlanetOfSigil / KindOfSigil).
		// PlanetarySquares.SigilCount means every sigil is complete.
		public int SigilIndex { get; set; }

		// The running value, 1–9 (D-GN.5). 0 = this sigil has had no roll yet; its first roll sets V.
		public int V { get; set; }

		// Consecutive losses since the last win (D-GN.13 adds it to the next win at chances 1–9%).
		public int LossStreak { get; set; }

		// The set being drawn (0-based) and how many of its numbers the pass in progress has hit; 0 = the next hit is
		// the set's first number, which draws its silhouette.
		public int SetIndex { get; set; }
		public int Step { get; set; }

		// The chance the pass in progress belongs to (D-GN.9); 0 when no pass is in progress.
		public int PassChance { get; set; }

		public List<SigilSetProgress> Sets { get; set; } = new();

		// D-GN.19: every player bet placed while this sigil is being drawn, won or lost, and the SC it wagered.
		public long BetsPlaced { get; set; }
		public decimal ScWagered { get; set; }

		public List<CompletedSigilRecord> Completed { get; set; } = new();

		public bool AllComplete => SigilIndex >= PlanetarySquares.SigilCount;
	}

	public sealed class SigilSetProgress
	{
		// Completed passes, by the chance they were drawn at. The set is finished when Σ passes / chance ≥ 1 (D-GN.8).
		public Dictionary<int, int> PassesByChance { get; set; } = new();
		public bool Finished { get; set; }
	}

	public sealed class CompletedSigilRecord
	{
		public int SigilIndex { get; set; }
		public long BetsPlaced { get; set; }
		public decimal ScWagered { get; set; }

		// Game time — the timestamp of the bet that completed it (Pattern 2), never wall-clock.
		public DateTime CompletedAtUtc { get; set; }
	}
}
