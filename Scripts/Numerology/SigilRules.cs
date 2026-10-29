using System;
using System.Collections.Generic;
using System.Numerics;
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

	// Graphic Numerology — the drawing rule (AIHelperFiles/graphic_numerology-plan.md §5–§7, P-GN.2). No Godot, no I/O,
	// no allocation on an ordinary bet: it runs on every player bet, at autobet rate.
	public static class SigilRules
	{
		// At this chance and below, a win counts the loss streak before it (D-GN.13): a winning range narrower than nine
		// consecutive rolls cannot hold every digital root.
		public const int LossStreakMaxChance = 9;

		public static SigilProgressState NewProgress()
		{
			var state = new SigilProgressState();
			StartSigil(state, 0);
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
		)
		{
			if (state.AllComplete)
				return SigilBetOutcome.None;

			var outcome = SigilBetOutcome.None;
			state.BetsPlaced++;
			state.ScWagered = Money.Normalize(state.ScWagered + betAmount);

			// D-GN.9: a pass belongs to one chance; a change before it completes sends the target back to the set's
			// first number. V is untouched — only a new sigil resets it. Low ↔ High is NOT a change (D-GN.14).
			if (state.Step > 0 && chance != state.PassChance)
			{
				state.Step = 0;
				state.PassChance = 0;
				outcome |= SigilBetOutcome.PassRestarted;
			}

			int r = RootOf(roll);

			if (!isWin)
			{
				state.V = state.V == 0 ? r : Wrap(state.V - r);
				state.LossStreak++;
				return outcome;
			}

			int s = chance <= LossStreakMaxChance ? state.LossStreak + r : r;
			state.LossStreak = 0;
			int vBefore = state.V;
			state.V = vBefore == 0 ? RootOf(s) : Wrap(vBefore + s);

			int target = CurrentTarget(state);
			bool hit = target <= 9 || target % 10 == 0
				// 1–9 and the round tens (10 behaves as 1 … 80 as 8) compare V after the bet (D-GN.12).
				? state.V == (target <= 9 ? target : target / 10)
				// Every other two-digit target: V before the bet beside the root of the win spells it.
				: vBefore == target / 10 && RootOf(s) == target % 10;

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
				CompletedAtUtc = timestampUtc
			});
			outcome |= SigilBetOutcome.SigilCompleted;

			StartSigil(state, state.SigilIndex + 1);
			if (state.AllComplete)
				outcome |= SigilBetOutcome.AllSigilsCompleted;

			return outcome;
		}

		// Σ passes / chance ≥ 1, in exact rationals. A floating sum would leave three passes at 3% at 0.999… — one pass
		// short forever (the Standing Convention 9 trap: a threshold on the noise of a division).
		private static bool IsFinished(SigilSetProgress set)
		{
			BigInteger numerator = 0, denominator = 1;
			foreach (var (chance, passes) in set.PassesByChance)
			{
				numerator = numerator * chance + passes * denominator;
				denominator *= chance;
				var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
				numerator /= gcd;
				denominator /= gcd;
			}

			return numerator >= denominator;
		}

		// For display only: how far a set is towards finished, 0–1.
		public static double SetCompletion(SigilSetProgress set)
		{
			double sum = 0;
			foreach (var (chance, passes) in set.PassesByChance)
				sum += (double)passes / chance;
			return set.Finished ? 1.0 : Math.Min(sum, 1.0);
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
			state.Sets = new List<SigilSetProgress>();
			if (state.AllComplete)
				return;

			int n = PlanetarySquares.PlanetOfSigil(sigilIndex).N;
			for (int i = 0; i < n; i++)
				state.Sets.Add(new SigilSetProgress());
		}
	}
}
