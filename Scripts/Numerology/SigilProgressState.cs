using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Scripts.Numerology
{
	// What the player holds (D-GN.24, D-GN.25): the sigils owned, at most one of each, and the one in the slot. Plain data so
	// the block checkpoint can capture and restore it whole. A fresh object is the truth of a new world: nothing owned, an
	// empty slot.
	public sealed class SigilCollectionState
	{
		public List<SigilProgressState> Owned { get; set; } = new();

		// The sigil in the slot, by its index in PlanetarySquares; null = the slot is empty, and a bet draws nothing.
		public int? EquippedSigilIndex { get; set; }

		// D-GN.30 — the planetary sigils begun, one record each, in the order begun. A checkpoint from before them carries
		// neither field, and so restores nothing activated and no activation in the slot: the truth, not a guess
		// (Standing Convention 5).
		public List<SigilProgressState> Planetary { get; set; } = new();

		// The planetary sigil in the slot, by planet index. One slot (D-GN.25): at most one of the two fields holds a value.
		public int? EquippedPlanetIndex { get; set; }
	}

	// One owned sigil's progress (D-GN.11), or a planetary sigil's own record (D-GN.30, OQ-GN.3). Plain data with public
	// setters so the block checkpoint can capture and restore it whole; SigilRules is the only code that changes it.
	public sealed class SigilProgressState
	{
		// Which sigil this is (PlanetarySquares.PlanetOfSigil / KindOfSigil). On a planetary record, its planet's diurnal
		// sigil, which names the planet and nothing else: read IsPlanetary before reading the kind.
		public int SigilIndex { get; set; }

		// D-GN.30 — this record draws both of a planet's sigils. Its Sets then hold 2n sets, the diurnal sigil's and then the
		// nocturnal's, which is the order the passes run in (D-GN.33); SigilRules.PlanetarySet maps one back to its sigil.
		public bool IsPlanetary { get; set; }

		// The levels of the planetary sigil activated so far, and the highest open when this record last entered the slot:
		// the combined level then, which cannot move while it stays, since only the record in the slot takes bets.
		public int ActivatedLevel { get; set; }
		public int LevelLimit { get; set; }

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

		// D-GN.19: every player bet placed while this sigil is in the slot, won or lost, and the SC it wagered.
		public long BetsPlaced { get; set; }
		public decimal ScWagered { get; set; }

		// Game time (Pattern 2), never wall-clock. StartedAtUtc: its first bet ever. LastBetAtUtc: its latest bet since it
		// last entered the slot, default until then, so the time it spent out of the slot is never its time (D-GN.25).
		// CompletedAtUtc: the bet that finished its last set, default until then (D-GN.26). On a planetary record, the bet
		// that reached LevelLimit, cleared when it enters the slot again with a higher limit (D-GN.33).
		public DateTime StartedAtUtc { get; set; }
		public DateTime LastBetAtUtc { get; set; }
		public DateTime CompletedAtUtc { get; set; }

		[JsonIgnore]
		public bool IsComplete => CompletedAtUtc != default;
	}

	public sealed class SigilSetProgress
	{
		// Completed passes, by the chance they were drawn at. The set is finished when
		// Σ passes / (SigilRules.PassesPerChance × chance) ≥ 1 (D-GN.8, D-GN.23).
		public Dictionary<int, int> PassesByChance { get; set; } = new();
		public bool Finished { get; set; }

		// D-GN.20: what this set cost — every bet placed while it was the set being drawn, the SC those bets wagered,
		// the game time between them, its completed passes at any chance, and the passes a chance change or a switch
		// threw away.
		public long BetsPlaced { get; set; }
		public decimal ScWagered { get; set; }
		public double GameSeconds { get; set; }
		public int PassesCompleted { get; set; }
		public int PassRestarts { get; set; }
	}
}
