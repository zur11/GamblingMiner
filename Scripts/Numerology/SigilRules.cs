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
		LevelReached = 128,      // the pass raised its set's level (D-GN.22) — what the DEBUG log prints, since a pass no longer does
		SigilLevelAcquired = 256 // the pass raised the SIGIL's level, or a planetary sigil's activated one: "Stop on Sigil Level Acquired" (D-GN.32)
	}

	// Which stop a player bet asks for, if any (D-GN.10, D-GN.31, D-GN.32). The engines map it to their session's StopReason;
	// SigilRules cannot name that type, since the harness compiles this folder without the betting code.
	public enum SigilStop
	{
		None,
		SigilCompleted,
		LevelAcquired,
		SetCompleted
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
			var state = new SigilProgressState { SigilIndex = sigilIndex };
			int n = PlanetarySquares.PlanetOfSigil(sigilIndex).N;
			for (int i = 0; i < n; i++)
				state.Sets.Add(new SigilSetProgress());
			return state;
		}

		// D-GN.30 — a planetary sigil's own record, starting fresh like any new sigil (OQ-GN.3): 2n sets, one list, so the
		// drawing rule below needs no second copy and its V runs through both sigils.
		public static SigilProgressState NewPlanetaryProgress(int planetIndex)
		{
			var state = new SigilProgressState { SigilIndex = planetIndex * 2, IsPlanetary = true };
			int sets = PlanetarySquares.All[planetIndex].N * 2;
			for (int i = 0; i < sets; i++)
				state.Sets.Add(new SigilSetProgress());
			return state;
		}

		// A planetary record's set, as its sigil and that sigil's own set: the diurnal sigil's sets come first (D-GN.33).
		public static (SigilKind Kind, int Set) PlanetarySet(int n, int setIndex) =>
			setIndex < n ? (SigilKind.Diurnal, setIndex) : (SigilKind.Nocturnal, setIndex - n);

		public static string NameOf(SigilProgressState state) => state.IsPlanetary
			? PlanetarySquares.PlanetOfSigil(state.SigilIndex).PlanetarySigilName
			: PlanetarySquares.NameOfSigil(state.SigilIndex);

		// D-GN.25 — a sigil leaves the slot. It loses the pass in progress, exactly what a chance change loses (D-GN.9), and
		// nothing else: V and the loss streak wait with it. Its latest bet is forgotten, so the next bet after it returns
		// credits no game time for the time it was out. Returns whether a pass was lost.
		public static bool LeaveSlot(SigilProgressState state)
		{
			state.LastBetAtUtc = default;
			if (state.Step == 0)
				return false;

			state.Step = 0;
			state.PassChance = 0;
			state.Sets[state.SetIndex].PassRestarts++;
			return true;
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
			if (state.IsPlanetary)
			{
				var (kind, set) = PlanetarySet(square.N, state.SetIndex);
				return square.NumberAt(kind, set, state.Step);
			}

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
			if (state.IsComplete)
				return SigilBetOutcome.None;   // a violet sigil takes no more progress (D-GN.26)

			var outcome = SigilBetOutcome.None;
			var currentSet = state.Sets[state.SetIndex];
			state.BetsPlaced++;
			state.ScWagered = Money.Normalize(state.ScWagered + betAmount);
			currentSet.BetsPlaced++;
			currentSet.ScWagered = Money.Normalize(currentSet.ScWagered + betAmount);

			// The time since the previous bet belongs to the set this bet lands on. Game time passes only as bets are
			// simulated (the 100-second rule), so the gaps add up to the time the sigil took. A negative gap would be a
			// rewound clock; it is not counted rather than subtracted. No latest bet = the sigil has just entered the slot,
			// and the time it was out is not its time (D-GN.25); StartedAtUtc alone says whether this is its first bet ever.
			if (state.StartedAtUtc == default)
				state.StartedAtUtc = timestampUtc;
			if (state.LastBetAtUtc != default && timestampUtc > state.LastBetAtUtc)
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

			return outcome | (state.IsPlanetary ? CompletePlanetaryPass(state, timestampUtc) : CompletePass(state, timestampUtc));
		}

		private static SigilBetOutcome CompletePass(SigilProgressState state, DateTime timestampUtc)
		{
			var outcome = SigilBetOutcome.SetPassCompleted;
			var set = state.Sets[state.SetIndex];
			var (numerator, denominator) = Progress(set);
			int levelBefore = LevelOf(numerator, denominator);
			(numerator, denominator) = Add(numerator, denominator, 1, PassesPerChance * state.PassChance);
			set.PassesByChance[state.PassChance] = set.PassesByChance.GetValueOrDefault(state.PassChance) + 1;
			set.PassesCompleted++;
			state.Step = 0;
			state.PassChance = 0;

			int levelAfter = LevelOf(numerator, denominator);
			if (levelAfter > levelBefore)
			{
				outcome |= SigilBetOutcome.LevelReached;
				// A set's level is not the sigil's (D-GN.27): this one rose past the others' lowest, or it did not.
				int lowestOther = MaxLevel;
				foreach (var other in state.Sets)
					if (other != set)
						lowestOther = Math.Min(lowestOther, SetLevel(other));
				if (Math.Min(levelAfter, lowestOther) > Math.Min(levelBefore, lowestOther))
					outcome |= SigilBetOutcome.SigilLevelAcquired;
			}

			if (numerator >= denominator)
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

			// A complete sigil stays where it is, violet, until the player chooses another (D-GN.26).
			state.CompletedAtUtc = timestampUtc;
			return outcome | SigilBetOutcome.SigilCompleted;
		}

		// D-GN.33 — the passes run through every set in order, so the sigils alternate by construction and neither repeats:
		// diurnal 1 … n, nocturnal 1 … n, again. A level is credited only here, when a whole pass ends, so none can be earned
		// without the complete figure. Reaching the limit is this record's completion: it stays, taking no progress, until
		// the player chooses another (D-GN.31), and the combined level cannot rise before then, since nothing else takes bets.
		private static SigilBetOutcome CompletePlanetaryPass(SigilProgressState state, DateTime timestampUtc)
		{
			var outcome = SigilBetOutcome.SetPassCompleted;
			var set = state.Sets[state.SetIndex];
			set.PassesByChance[state.PassChance] = set.PassesByChance.GetValueOrDefault(state.PassChance) + 1;
			set.PassesCompleted++;
			state.Step = 0;
			state.PassChance = 0;
			state.SetIndex = (state.SetIndex + 1) % state.Sets.Count;
			if (state.SetIndex != 0)
				return outcome;

			int reached = Math.Min(state.LevelLimit, LevelsPaidFor(state));
			if (reached > state.ActivatedLevel)
			{
				state.ActivatedLevel = reached;
				outcome |= SigilBetOutcome.LevelReached | SigilBetOutcome.SigilLevelAcquired;
			}

			if (state.ActivatedLevel >= state.LevelLimit)
			{
				state.CompletedAtUtc = timestampUtc;
				outcome |= SigilBetOutcome.SigilCompleted;
			}

			return outcome;
		}

		// D-GN.30, OQ-GN.2 — the chance a whole pass counts as exactly one: a pass at chance c counts 95 / c, keeping the
		// balance of D-GN.8 inside this mechanic.
		public const int ActivationFullValueChance = 95;

		// D-GN.33 — the levels the whole passes so far have paid for. Level k costs k whole passes (OQ-GN.1, reading A), so
		// level L is paid at L(L+1)/2, and the value is cumulative, so what a pass overpays carries forward. Each set pass is
		// 1/(2n) of a whole pass. Exact fractions, as Progress: a floating sum would stop a hair under the target (Standing
		// Convention 9). Read only when a whole pass ends.
		private static int LevelsPaidFor(SigilProgressState state)
		{
			BigInteger numerator = 0, denominator = 1;
			foreach (var set in state.Sets)
				foreach (var (chance, passes) in set.PassesByChance)
					(numerator, denominator) = Add(numerator, denominator, passes * ActivationFullValueChance, chance * state.Sets.Count);

			int level = 0;
			while (level < MaxLevel && 2 * numerator >= (level + 1) * (level + 2) * denominator)
				level++;
			return level;
		}

		// For display only: the whole passes this record has paid, counted at their value.
		public static double WholePassesPaid(SigilProgressState state)
		{
			double sum = 0;
			foreach (var set in state.Sets)
				foreach (var (chance, passes) in set.PassesByChance)
					sum += (double)passes * ActivationFullValueChance / (chance * state.Sets.Count);
			return sum;
		}

		// For display only: how far into the level being activated, and what that level costs. Can read above the cost when an
		// earlier pass overpaid and the gate held the credit back.
		public static (double Paid, int Cost) NextLevelProgress(SigilProgressState state)
		{
			int activated = state.ActivatedLevel;
			return (WholePassesPaid(state) - activated * (activated + 1) / 2.0, activated + 1);
		}

		// D-GN.30's gate: the next level of a planet's sigil that can be activated, 0 when none is open. Both sigils must hold at
		// least that level, which is what the combined level says. A null is a sigil not owned, or a planetary sigil not begun.
		public static int NextOpenLevel(SigilProgressState first, SigilProgressState second, SigilProgressState planetary)
		{
			int activated = planetary?.ActivatedLevel ?? 0;
			return HasCombinedFigure(first, second) && activated < CombinedLevel(first, second) ? activated + 1 : 0;
		}

		// D-GN.29 — the notice lasts this long in game time, and is still live at exactly this long.
		public const int NoticeGameSeconds = 600;

		public static bool NoticeIsLive(DateTime firedAtUtc, DateTime nowUtc) =>
			nowUtc <= firedAtUtc.AddSeconds(NoticeGameSeconds);

		// D-GN.31 — the automatic switch's choice until the Basic Mode polish: the owned incomplete sigil with the fewest bets,
		// the collection's order breaking a tie. Never an activation: D-GN.30 runs only when the player chooses it. Null when
		// there is none. A search, for a completion or a button, never an ordinary bet.
		public static SigilProgressState LeastPlayed(SigilCollectionState collection)
		{
			SigilProgressState least = null;
			foreach (int sigilIndex in PlanetarySquares.SigilsByName)
				foreach (var sigil in collection.Owned)
					if (sigil.SigilIndex == sigilIndex && !sigil.IsComplete && (least == null || sigil.BetsPlaced < least.BetsPlaced))
						least = sigil;
			return least;
		}

		// D-GN.23 — a pass at chance c adds 1 / (PassesPerChance × c) of its set: the factor multiplies the cost of violet
		// and leaves the balance between chances exactly as it was (D-GN.8).
		public const int PassesPerChance = 100;

		// A set's progress as an exact fraction, Σ passes / (PassesPerChance × chance): what the finished verdict and the
		// levels both read. A floating sum would stop a hair under 1 and leave the set one pass short forever (the Standing
		// Convention 9 trap: a threshold on the noise of a division).
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
		// fraction as the finished verdict, so the two can never disagree, and it never goes down: passes are only ever
		// added.
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
		// A planetary record's level is what it has activated: its sets hold passes at their value (D-GN.33), not progress
		// towards violet.
		public static int SigilLevel(SigilProgressState state)
		{
			if (state.IsPlanetary)
				return state.ActivatedLevel;

			int lowest = MaxLevel;
			foreach (var set in state.Sets)
				lowest = Math.Min(lowest, SetLevel(set));
			return lowest;
		}

		public static int CombinedLevel(SigilProgressState first, SigilProgressState second) =>
			Math.Min(SigilLevel(first), SigilLevel(second));

		// The sigil a planet pairs with this one: the other kind, on the same square (D-GN.16). Index = planet × 2 + kind.
		public static int PartnerOf(int sigilIndex) => sigilIndex ^ 1;

		// D-GN.27 — a combined figure exists once both sigils are owned and the lower reaches level 1. Until then nothing on
		// screen may tie the two together: D-GN.24 leaves the pair for the player to deduce. A null is a sigil not owned.
		public static bool HasCombinedFigure(SigilProgressState first, SigilProgressState second) =>
			first != null && second != null && CombinedLevel(first, second) >= 1;

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
			if (state.Sets.Count == 0)
				return 0.0;

			double sum = 0;
			foreach (var set in state.Sets)
				sum += SetCompletion(set);
			return sum / state.Sets.Count;
		}

		// D-GN.24 — what the collection says of a sigil: its planet and its sky, which is what the player deduces the pairs
		// from, and the shape of its sets. Never a partner's name, nor a sign at all: "Eclipsed Leo" holds its partner's, so
		// the only safe description is one that names none (the harness checks this text, not the names, which D-GN.3 fixes).
		public static string DescriptionOf(int sigilIndex)
		{
			var square = PlanetarySquares.PlanetOfSigil(sigilIndex);
			var kind = PlanetarySquares.KindOfSigil(sigilIndex);
			string planet = square.Planet is "Sun" or "Moon" ? "The " + square.Planet : square.Planet;
			string sky = square.IsEclipsed(kind) ? "in eclipse" : kind == SigilKind.Diurnal ? "by day" : "by night";
			int step = kind == SigilKind.Diurnal ? 1 : square.N;   // D-GN.2
			return string.Create(CultureInfo.InvariantCulture,
				$"{planet}'s square, {sky}: {square.N} sets of {square.N} numbers, counting by {step}.");
		}

		// One line for godot.log (the measurements read it afterwards): the sigil, its counters, V and the target, the pass in
		// progress, and every set as passes / level / completion / restarts / bets / game-seconds. Null = the empty slot.
		public static string Describe(SigilProgressState state)
		{
			if (state == null)
				return "slot empty";

			var sets = new StringBuilder();
			foreach (var set in state.Sets)
				sets.Append(state.IsPlanetary
					? string.Create(CultureInfo.InvariantCulture, $" [{set.PassesCompleted}p r{set.PassRestarts} {set.BetsPlaced}b {set.GameSeconds:F0}s]")
					: string.Create(CultureInfo.InvariantCulture,
						$" [{set.PassesCompleted}p L{SetLevel(set)} {SetCompletion(set) * 100:F3}% r{set.PassRestarts} {set.BetsPlaced}b {set.GameSeconds:F0}s]"));
			string target = state.IsComplete ? "complete" : CurrentTarget(state).ToString(CultureInfo.InvariantCulture);
			string level = state.IsPlanetary
				? string.Create(CultureInfo.InvariantCulture, $"activated={state.ActivatedLevel} limit={state.LevelLimit}")
				: string.Create(CultureInfo.InvariantCulture, $"level={SigilLevel(state)}");
			return string.Create(CultureInfo.InvariantCulture,
				$"{NameOf(state)}: {level} bets={state.BetsPlaced} sc={state.ScWagered:F8} " +
				$"v={state.V} streak={state.LossStreak} target={target} pass={state.Step}@{state.PassChance} set={state.SetIndex + 1} sets:{sets}");
		}

		// The whole collection on one line, each owned sigil as above and then each planetary sigil begun: what a checkpoint
		// commits and a boot restores, so the two can be compared as text (Pr-13, Pr-19). Owned in the order acquired.
		public static string Describe(SigilCollectionState collection)
		{
			string slot = collection.EquippedPlanetIndex is int planet
				? PlanetarySquares.All[planet].PlanetarySigilName
				: collection.EquippedSigilIndex is int index
					? PlanetarySquares.NameOfSigil(index)
					: "empty";
			var sb = new StringBuilder(string.Create(CultureInfo.InvariantCulture,
				$"slot={slot} owned={collection.Owned.Count} planetary={collection.Planetary.Count}"));
			foreach (var sigil in collection.Owned)
				sb.Append(" | ").Append(Describe(sigil));
			foreach (var planetary in collection.Planetary)
				sb.Append(" | ").Append(Describe(planetary));
			return sb.ToString();
		}

		// A sigil's final figures, each set with its passes by chance (D-GN.20).
		public static string DescribeCompleted(SigilProgressState state)
		{
			var square = PlanetarySquares.PlanetOfSigil(state.SigilIndex);
			var sets = new StringBuilder();
			foreach (var set in state.Sets)
				sets.Append(string.Create(CultureInfo.InvariantCulture,
					$" [{set.PassesCompleted}p r{set.PassRestarts} {set.BetsPlaced}b {string.Join(",", set.PassesByChance.Select(p => $"{p.Value}@{p.Key}"))}]"));
			return string.Create(CultureInfo.InvariantCulture,
				$"{square.SigilName(PlanetarySquares.KindOfSigil(state.SigilIndex))}: bets={state.BetsPlaced} sc={state.ScWagered:F8} " +
				$"game-days={state.Sets.Sum(s => s.GameSeconds) / 86400:F2} sets:{sets}");
		}
	}
}
