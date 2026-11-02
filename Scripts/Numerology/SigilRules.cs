using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Scripts.Finance;

namespace Scripts.Numerology
{
	[Flags]
	public enum SigilBetOutcome
	{
		None = 0,
		PassRestarted = 1,       // the chance changed mid-pass (D-GN.9)
		Hit = 2,
		SilhouetteDrawn = 4,     // the hit was the set's first number
		LineDrawn = 8,
		SetPassCompleted = 16,   // "Stop on Sigil Set Completed" stops here (D-GN.10)
		SetFinished = 32,
		SigilCompleted = 64,
		AllSigilsCompleted = 128
	}

	// One bet's operation on V, as the overlay's V chain shows it (D-GN.21): V before, then + or − the root (plus the loss
	// streak a win at 1–9% adds, D-GN.13), then V after. VBefore 0 = the sigil's first roll, which sets V. Spelled is the
	// number V-before-and-root spelled for a two-digit target (D-GN.12), 0 when the target is not of that kind.
	public readonly record struct SigilStep(
		int VBefore,
		bool IsWin,
		int Root,
		int Streak,
		int VAfter,
		int Target,
		int Spelled,
		bool Hit
	);

	// Graphic Numerology — the drawing rule (AIHelperFiles/graphic_numerology-plan.md §5–§7, P-GN.2). No Godot, no I/O,
	// no allocation on an ordinary bet: it runs on every player bet, at autobet rate.
	public static class SigilRules
	{
		// At this chance and below, a win counts the loss streak before it (D-GN.13): a winning range narrower than nine
		// consecutive rolls cannot hold every digital root.
		public const int LossStreakMaxChance = 9;

		public static SigilProgressState NewProgress(int sigilIndex = 0)
		{
			var state = new SigilProgressState();
			StartSigil(state, sigilIndex);
			return state;
		}

		// Digital root of a roll 00–99, with 0 read as 9 (D-GN.4).
		public static int RootOf(int value) => value % 9 == 0 ? 9 : value % 9;

		// Digital-root arithmetic for V: the result is 1–9, 9 standing for 0 (D-GN.5).
		private static int Wrap(int value)
		{
			int m = ((value % 9) + 9) % 9;
			return m == 0 ? 9 : m;
		}

		public static int CurrentTarget(SigilProgressState state)
		{
			var square = PlanetarySquares.PlanetOfSigil(state.SigilIndex);
			return square.NumberAt(PlanetarySquares.KindOfSigil(state.SigilIndex), state.SetIndex, state.Step);
		}

		public static SigilBetOutcome ApplyBet(
			SigilProgressState state,
			int roll,
			bool isWin,
			int chance,
			decimal betAmount,
			DateTime timestampUtc
		) => ApplyBet(state, roll, isWin, chance, betAmount, timestampUtc, out _);

		// The same, also reporting the operation the bet performed on V — what the overlay's V chain shows (D-GN.21).
		public static SigilBetOutcome ApplyBet(
			SigilProgressState state,
			int roll,
			bool isWin,
			int chance,
			decimal betAmount,
			DateTime timestampUtc,
			out SigilStep step
		)
		{
			step = default;
			if (state.AllComplete)
				return SigilBetOutcome.None;

			var outcome = SigilBetOutcome.None;
			var currentSet = state.Sets[state.SetIndex];
			state.BetsPlaced++;
			state.ScWagered = Money.Normalize(state.ScWagered + betAmount);
			currentSet.BetsPlaced++;
			currentSet.ScWagered = Money.Normalize(currentSet.ScWagered + betAmount);

			// The time since the previous bet belongs to the set this bet lands on. Game time passes only as bets are
			// simulated (the 100-second rule), so the gaps add up to the time the sigil took. A negative gap would be a
			// rewound clock; it is not counted rather than subtracted.
			if (state.LastBetAtUtc == default)
				state.StartedAtUtc = timestampUtc;
			else if (timestampUtc > state.LastBetAtUtc)
				currentSet.GameSeconds += (timestampUtc - state.LastBetAtUtc).TotalSeconds;
			state.LastBetAtUtc = timestampUtc;

			// D-GN.9: a pass belongs to one chance; a change before it completes sends the target back to the set's
			// first number. V is untouched — only a new sigil resets it. Low ↔ High is NOT a change (D-GN.14).
			if (state.Step > 0 && chance != state.PassChance)
			{
				state.Step = 0;
				state.PassChance = 0;
				currentSet.PassRestarts++;
				outcome |= SigilBetOutcome.PassRestarted;
			}

			int r = RootOf(roll);
			int target = CurrentTarget(state);
			int vBefore = state.V;

			if (!isWin)
			{
				state.V = vBefore == 0 ? r : Wrap(vBefore - r);
				state.LossStreak++;
				step = new SigilStep(vBefore, false, r, 0, state.V, target, 0, false);
				return outcome;
			}

			int streak = chance <= LossStreakMaxChance ? state.LossStreak : 0;
			int s = streak + r;
			state.LossStreak = 0;
			state.V = vBefore == 0 ? RootOf(s) : Wrap(vBefore + s);

			bool spells = target > 9 && target % 10 != 0;
			bool hit = !spells
				// 1–9 and the round tens (10 behaves as 1 … 80 as 8) compare V after the bet (D-GN.12).
				? state.V == (target <= 9 ? target : target / 10)
				// Every other two-digit target: V before the bet beside the root of the win spells it.
				: vBefore == target / 10 && RootOf(s) == target % 10;
			step = new SigilStep(vBefore, true, r, streak, state.V, target, spells && vBefore != 0 ? vBefore * 10 + RootOf(s) : 0, hit);

			if (!hit)
				return outcome;

			outcome |= SigilBetOutcome.Hit;
			if (state.Step == 0)
			{
				state.PassChance = chance;
				outcome |= SigilBetOutcome.SilhouetteDrawn;
			}
			else
			{
				outcome |= SigilBetOutcome.LineDrawn;
			}

			state.Step++;
			if (state.Step < PlanetarySquares.PlanetOfSigil(state.SigilIndex).N)
				return outcome;

			return outcome | CompletePass(state, timestampUtc);
		}

		private static SigilBetOutcome CompletePass(SigilProgressState state, DateTime timestampUtc)
		{
			var outcome = SigilBetOutcome.SetPassCompleted;
			var set = state.Sets[state.SetIndex];
			set.PassesByChance[state.PassChance] = set.PassesByChance.GetValueOrDefault(state.PassChance) + 1;
			set.PassesCompleted++;
			state.Step = 0;
			state.PassChance = 0;

			if (IsFinished(set))
			{
				set.Finished = true;
				outcome |= SigilBetOutcome.SetFinished;
			}

			// The passes cycle through the sets still unfinished, in order (D-GN.8).
			int count = state.Sets.Count;
			for (int i = 1; i <= count; i++)
			{
				int next = (state.SetIndex + i) % count;
				if (!state.Sets[next].Finished)
				{
					state.SetIndex = next;
					return outcome;
				}
			}

			state.Completed.Add(new CompletedSigilRecord
			{
				SigilIndex = state.SigilIndex,
				BetsPlaced = state.BetsPlaced,
				ScWagered = state.ScWagered,
				StartedAtUtc = state.StartedAtUtc,
				CompletedAtUtc = timestampUtc,
				Sets = state.Sets   // StartSigil gives the next sigil a fresh list, so this one is no longer written
			});
			outcome |= SigilBetOutcome.SigilCompleted;

			StartSigil(state, state.SigilIndex + 1);
			if (state.AllComplete)
				outcome |= SigilBetOutcome.AllSigilsCompleted;

			return outcome;
		}

		// D-GN.23 — a pass at chance c adds 1 / (PassesPerChance × c) of its set: the factor multiplies the cost of violet
		// and leaves the balance between chances exactly as it was (D-GN.8).
		public const int PassesPerChance = 100;

		// Σ passes / (PassesPerChance × chance) ≥ 1, in exact rationals. A floating sum would stop a hair under 1 and leave
		// the set one pass short forever (the Standing Convention 9 trap: a threshold on the noise of a division).
		private static bool IsFinished(SigilSetProgress set)
		{
			var (numerator, denominator) = Progress(set);
			return numerator >= denominator;
		}

		// A set's progress as an exact fraction: what IsFinished and the levels both read.
		private static (BigInteger Numerator, BigInteger Denominator) Progress(SigilSetProgress set)
		{
			BigInteger numerator = 0, denominator = 1;
			foreach (var (chance, passes) in set.PassesByChance)
				(numerator, denominator) = Add(numerator, denominator, passes, PassesPerChance * chance);
			return (numerator, denominator);
		}

		// Adds passes / passValue to the fraction, reduced.
		private static (BigInteger, BigInteger) Add(BigInteger numerator, BigInteger denominator, int passes, int passValue)
		{
			numerator = numerator * passValue + passes * denominator;
			denominator *= passValue;
			var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
			return (numerator / gcd, denominator / gcd);
		}

		// D-GN.22 — a set's level is its progress on a 95-step rainbow: ⌊MaxLevel × progress⌋, the whole steps completed —
		// 1 red … MaxLevel violet. 0 until the first whole step, which since D-GN.23 takes many passes at most chances.
		// Rounding DOWN makes MaxLevel mean finished and nothing else (⌊95p⌋ = 95 ⇔ p ≥ 1). Computed from the same exact
		// fraction as IsFinished, so the level and the verdict can never disagree, and it never goes down: passes are
		// only ever added.
		public const int MaxLevel = 95;

		public static int SetLevel(SigilSetProgress set)
		{
			var (numerator, denominator) = Progress(set);
			return set.Finished ? MaxLevel : LevelOf(numerator, denominator);
		}

		// The level the set would reach with one more pass at this chance — the overlay's preview.
		public static int SetLevelAfterPass(SigilSetProgress set, int chance)
		{
			if (set.Finished)
				return MaxLevel;
			var (numerator, denominator) = Progress(set);
			(numerator, denominator) = Add(numerator, denominator, 1, PassesPerChance * chance);
			return LevelOf(numerator, denominator);
		}

		// How many more passes at this chance reach the next level; 0 for a finished set. What the overlay shows between
		// levels, since most passes no longer change one (D-GN.23): the smallest k with progress + k / (PassesPerChance ×
		// chance) ≥ (level + 1) / MaxLevel, in integers.
		public static int PassesToNextLevel(SigilSetProgress set, int chance)
		{
			if (set.Finished)
				return 0;
			var (numerator, denominator) = Progress(set);
			var needed = ((LevelOf(numerator, denominator) + 1) * denominator - MaxLevel * numerator) * (PassesPerChance * chance);
			var scale = MaxLevel * denominator;
			return (int)((needed + scale - 1) / scale);
		}

		// D-GN.27 — a sigil's level is the highest all its sets share, which is the lowest of them; a combined figure's is
		// the highest its two sigils share, the lower of the two. What the benefits of the next plan will read.
		public static int SigilLevel(SigilProgressState state)
		{
			int lowest = MaxLevel;
			foreach (var set in state.Sets)
				lowest = Math.Min(lowest, SetLevel(set));
			return lowest;
		}

		public static int CombinedLevel(SigilProgressState first, SigilProgressState second) =>
			Math.Min(SigilLevel(first), SigilLevel(second));

		private static int LevelOf(BigInteger numerator, BigInteger denominator) =>
			numerator >= denominator ? MaxLevel : (int)(MaxLevel * numerator / denominator);   // integer division floors

		// D-GN.22 — the canvas shows one ROUND at a time: a round is one pass on every unfinished set, in the order the
		// passes already cycle (D-GN.8). Within a round the sets already passed have one pass more than the rest, so
		// this needs no stored state; when every unfinished set has the same count, the round has ended and the canvas
		// is clear. A finished set is not part of any round (it stays drawn, violet).
		public static bool PassedThisRound(SigilProgressState state, int setIndex)
		{
			var set = state.Sets[setIndex];
			if (set.Finished)
				return false;

			int fewest = int.MaxValue;
			foreach (var other in state.Sets)
				if (!other.Finished && other.PassesCompleted < fewest)
					fewest = other.PassesCompleted;
			return set.PassesCompleted > fewest;
		}

		// For display only: how far a set is towards finished, 0–1.
		public static double SetCompletion(SigilSetProgress set)
		{
			double sum = 0;
			foreach (var (chance, passes) in set.PassesByChance)
				sum += (double)passes / (PassesPerChance * chance);
			return set.Finished ? 1.0 : Math.Min(sum, 1.0);
		}

		// For display only: the sigil's completion, 0–1 — its sets weigh the same, as each needs the same passes.
		// Counts completed passes only; the pass in progress can still be lost to a chance change.
		public static double SigilCompletion(SigilProgressState state)
		{
			if (state.AllComplete || state.Sets.Count == 0)
				return state.AllComplete ? 1.0 : 0.0;

			double sum = 0;
			foreach (var set in state.Sets)
				sum += SetCompletion(set);
			return sum / state.Sets.Count;
		}

		// One line for godot.log (unit 5's measurement reads it afterwards): the sigil, its counters, V and the target,
		// the pass in progress, and every set as passes / completion / restarts / bets / game-seconds.
		public static string Describe(SigilProgressState state)
		{
			if (state.AllComplete)
				return string.Create(CultureInfo.InvariantCulture, $"all sigils complete ({state.Completed.Count})");

			var square = PlanetarySquares.PlanetOfSigil(state.SigilIndex);
			var sets = new StringBuilder();
			foreach (var set in state.Sets)
				sets.Append(string.Create(CultureInfo.InvariantCulture,
					$" [{set.PassesCompleted}p L{SetLevel(set)} {SetCompletion(set) * 100:F3}% r{set.PassRestarts} {set.BetsPlaced}b {set.GameSeconds:F0}s]"));
			return string.Create(CultureInfo.InvariantCulture,
				$"{square.SigilName(PlanetarySquares.KindOfSigil(state.SigilIndex))}: bets={state.BetsPlaced} sc={state.ScWagered:F8} " +
				$"v={state.V} streak={state.LossStreak} target={CurrentTarget(state)} pass={state.Step}@{state.PassChance} " +
				$"completed={state.Completed.Count} sets:{sets}");
		}

		public static string Describe(CompletedSigilRecord record)
		{
			var square = PlanetarySquares.PlanetOfSigil(record.SigilIndex);
			var sets = new StringBuilder();
			foreach (var set in record.Sets)
				sets.Append(string.Create(CultureInfo.InvariantCulture,
					$" [{set.PassesCompleted}p r{set.PassRestarts} {set.BetsPlaced}b {string.Join(",", set.PassesByChance.Select(p => $"{p.Value}@{p.Key}"))}]"));
			return string.Create(CultureInfo.InvariantCulture,
				$"{square.SigilName(PlanetarySquares.KindOfSigil(record.SigilIndex))}: bets={record.BetsPlaced} sc={record.ScWagered:F8} " +
				$"game-days={record.Sets.Sum(s => s.GameSeconds) / 86400:F2} sets:{sets}");
		}

		private static void StartSigil(SigilProgressState state, int sigilIndex)
		{
			state.SigilIndex = sigilIndex;
			state.V = 0;
			state.LossStreak = 0;
			state.SetIndex = 0;
			state.Step = 0;
			state.PassChance = 0;
			state.BetsPlaced = 0;
			state.ScWagered = 0m;
			state.StartedAtUtc = default;
			state.LastBetAtUtc = default;
			state.Sets = new List<SigilSetProgress>();
			if (state.AllComplete)
				return;

			int n = PlanetarySquares.PlanetOfSigil(sigilIndex).N;
			for (int i = 0; i < n; i++)
				state.Sets.Add(new SigilSetProgress());
		}
	}
}
