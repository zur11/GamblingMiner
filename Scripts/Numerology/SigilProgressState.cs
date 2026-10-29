using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

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

		// Game time of this sigil's first bet and of its latest one (D-GN.20). Default = no bet yet.
		public DateTime StartedAtUtc { get; set; }
		public DateTime LastBetAtUtc { get; set; }

		public List<CompletedSigilRecord> Completed { get; set; } = new();

		[JsonIgnore]
		public bool AllComplete => SigilIndex >= PlanetarySquares.SigilCount;
	}

	public sealed class SigilSetProgress
	{
		// Completed passes, by the chance they were drawn at. The set is finished when Σ passes / chance ≥ 1 (D-GN.8).
		public Dictionary<int, int> PassesByChance { get; set; } = new();
		public bool Finished { get; set; }

		// D-GN.20: what this set cost — every bet placed while it was the set being drawn, the SC those bets wagered,
		// the game time between them, its completed passes at any chance, and the passes a chance change threw away.
		public long BetsPlaced { get; set; }
		public decimal ScWagered { get; set; }
		public double GameSeconds { get; set; }
		public int PassesCompleted { get; set; }
		public int PassRestarts { get; set; }
	}

	public sealed class CompletedSigilRecord
	{
		public int SigilIndex { get; set; }
		public long BetsPlaced { get; set; }
		public decimal ScWagered { get; set; }

		// Game time — the timestamps of its first bet and of the bet that completed it (Pattern 2), never wall-clock.
		public DateTime StartedAtUtc { get; set; }
		public DateTime CompletedAtUtc { get; set; }

		// Each set's final figures, kept so the sets can be compared after the sigil is done (D-GN.20).
		public List<SigilSetProgress> Sets { get; set; } = new();
	}
}
