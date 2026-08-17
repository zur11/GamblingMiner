using System;
using System.Collections.Generic;
using Scripts.Finance;
using Scripts.History;

namespace Scripts.User
{
	// The persisted lifetime rollup (mini-plan 03 §6.2/§6.4).
	//
	// WHY IT EXISTS: `UserBettingStats` is rebuilt at every boot by scanning the loaded journal, and since
	// P15.11 the journal RETAINS only the newest MaxRetainedJournalChunks (200,000 bets) — so the moment
	// the first chunk is pruned, every pruned bet silently disappears from the "lifetime" figures on the
	// next restart. A running total is the only structure that can survive its own source being deleted.
	//
	// THE CONSEQUENCE THAT SHAPES EVERYTHING HERE: once pruning has begun this record is the ONLY copy of
	// those bets' contribution. It can never be re-derived, only carried forward — so it is maintained on
	// every settled bet, snapshotted at every block like all player-facing state, and while nothing has
	// been pruned yet it is re-seeded from a full scan so the two can never drift apart unnoticed.
	//
	// Consecutive-run figures are segmented per (GameId, Chance) — INC-002/§40.8's correctness rule, not a
	// presentation choice: a run only means anything at a fixed win chance. Continuing a run across a
	// restart is why CurrentSegmentKey / CurrentLossRun / CurrentWinRun are persisted alongside the maxima.
	public sealed class BetStatsRollup
	{
		// FALSE when this rollup was first seeded from a journal that had ALREADY been pruned — i.e. the
		// bets in the deleted chunks are not counted and never can be. The rollup is still the best figure
		// available and still correct from its seeding point forward; it simply is not a lifetime one, and
		// saying so is the difference between a limitation and a lie (§39.16 rule 1). Worlds that adopt the
		// rollup before their first prune are complete and stay complete.
		public bool IsComplete { get; set; } = true;
		// When the rollup began counting. Only meaningful while IsComplete is false — it is the honest
		// answer to "these totals cover play since when?" and the value any readout should show beside them.
		public DateTime? SeededAtUtc { get; set; }

		public int TotalBets { get; set; }
		public int TotalWins { get; set; }
		public int TotalLosses { get; set; }
		public decimal TotalWagered { get; set; }
		public decimal TotalNetProfit { get; set; }

		public decimal MaxBetAmount { get; set; }
		public decimal MaxLossAmount { get; set; }   // largest single loss, as a positive magnitude
		public decimal MaxWonAmount { get; set; }    // largest single win — the mirror, new in mini-plan 03

		// Everything else UserBettingStats keeps, so it can be reconstructed without scanning the journal.
		// These are running values exactly like the totals above — the since-deposit trio is zeroed by a
		// deposit, the drawdown pair tracks the peak — so the rollup is their natural home. Without them
		// the boot scan could not be removed at all: dropping it would silently reset a player's
		// since-deposit figures and lose their worst drawdown on every restart.
		public int SinceDepositBets { get; set; }
		public decimal SinceDepositWagered { get; set; }
		public decimal SinceDepositProfit { get; set; }
		public decimal PeakProfit { get; set; }
		public decimal CurrentDrawdown { get; set; }
		public decimal MaxDrawdown { get; set; }

		// Key: SegmentKey(gameId, chance). Value: that segment's run maxima.
		public Dictionary<string, SegmentRuns> Segments { get; set; } = new();

		// The run in progress, so a restart resumes it instead of restarting the count at 1.
		public string CurrentSegmentKey { get; set; } = string.Empty;
		public int CurrentLossRun { get; set; }
		public int CurrentWinRun { get; set; }

		// Per-segment AGGREGATES, not just run maxima. The extra fields exist so a filtered view (the
		// chance-to-win selector) can still report true totals: a reader showing "chance 50%" needs that
		// segment's pruned contribution, and a lifetime grand total cannot supply it.
		public sealed class SegmentRuns
		{
			public string GameId { get; set; } = string.Empty;
			public int Chance { get; set; }
			public int MaxConsecutiveLosses { get; set; }
			public int MaxConsecutiveWins { get; set; }

			public int Bets { get; set; }
			public int Wins { get; set; }
			public decimal Wagered { get; set; }
			public decimal NetProfit { get; set; }
			public decimal MaxBetAmount { get; set; }
			public decimal MaxLossAmount { get; set; }
			public decimal MaxWonAmount { get; set; }
		}

		public static string SegmentKey(string gameId, int chance) => $"{gameId}|{chance}";

		public void Reset()
		{
			TotalBets = 0;
			TotalWins = 0;
			TotalLosses = 0;
			TotalWagered = 0m;
			TotalNetProfit = 0m;
			MaxBetAmount = 0m;
			MaxLossAmount = 0m;
			MaxWonAmount = 0m;
			SinceDepositBets = 0;
			SinceDepositWagered = 0m;
			SinceDepositProfit = 0m;
			PeakProfit = 0m;
			CurrentDrawdown = 0m;
			MaxDrawdown = 0m;
			Segments = new Dictionary<string, SegmentRuns>();
			CurrentSegmentKey = string.Empty;
			CurrentLossRun = 0;
			CurrentWinRun = 0;
		}

		// The single write path. Callers feed bets in settle order; the run counters depend on it.
		public void RegisterBet(string gameId, int chance, bool isWin, decimal betAmount, decimal netAmount)
		{
			TotalBets++;
			TotalWagered = Money.Normalize(TotalWagered + betAmount);
			TotalNetProfit = Money.Normalize(TotalNetProfit + netAmount);

			SinceDepositBets++;
			SinceDepositWagered = Money.Normalize(SinceDepositWagered + betAmount);
			SinceDepositProfit = Money.Normalize(SinceDepositProfit + netAmount);

			// Same shape as UserBettingStats.ApplyBet, so the reconstructed object is identical to one
			// built by replaying every bet.
			if (TotalNetProfit > PeakProfit)
			{
				PeakProfit = TotalNetProfit;
			}

			CurrentDrawdown = TotalNetProfit - PeakProfit;
			if (CurrentDrawdown < MaxDrawdown)
			{
				MaxDrawdown = CurrentDrawdown;
			}

			if (betAmount > MaxBetAmount)
			{
				MaxBetAmount = betAmount;
			}

			if (isWin)
			{
				TotalWins++;
				if (netAmount > MaxWonAmount)
				{
					MaxWonAmount = netAmount;
				}
			}
			else
			{
				TotalLosses++;
				decimal loss = Math.Abs(netAmount);
				if (loss > MaxLossAmount)
				{
					MaxLossAmount = loss;
				}
			}

			// A change of (GameId, Chance) ENDS both runs: whatever the player was doing before is a
			// different experiment, and its run does not continue into this one (§40.8).
			string key = SegmentKey(gameId, chance);
			if (!string.Equals(key, CurrentSegmentKey, StringComparison.Ordinal))
			{
				CurrentSegmentKey = key;
				CurrentLossRun = 0;
				CurrentWinRun = 0;
			}

			if (!Segments.TryGetValue(key, out SegmentRuns runs))
			{
				runs = new SegmentRuns { GameId = gameId, Chance = chance };
				Segments[key] = runs;
			}

			runs.Bets++;
			runs.Wagered = Money.Normalize(runs.Wagered + betAmount);
			runs.NetProfit = Money.Normalize(runs.NetProfit + netAmount);
			if (betAmount > runs.MaxBetAmount)
			{
				runs.MaxBetAmount = betAmount;
			}

			if (isWin)
			{
				runs.Wins++;
				if (netAmount > runs.MaxWonAmount)
				{
					runs.MaxWonAmount = netAmount;
				}
			}
			else
			{
				decimal segLoss = Math.Abs(netAmount);
				if (segLoss > runs.MaxLossAmount)
				{
					runs.MaxLossAmount = segLoss;
				}
			}

			if (isWin)
			{
				CurrentWinRun++;
				CurrentLossRun = 0;
				if (CurrentWinRun > runs.MaxConsecutiveWins)
				{
					runs.MaxConsecutiveWins = CurrentWinRun;
				}
			}
			else
			{
				CurrentLossRun++;
				CurrentWinRun = 0;
				if (CurrentLossRun > runs.MaxConsecutiveLosses)
				{
					runs.MaxConsecutiveLosses = CurrentLossRun;
				}
			}
		}

		// A deposit resets the "since deposit" window and nothing else — the lifetime figures and the
		// drawdown peak are unaffected, exactly as UserBettingStats.ResetSessionMetrics does.
		public void RegisterDeposit()
		{
			SinceDepositBets = 0;
			SinceDepositWagered = 0m;
			SinceDepositProfit = 0m;
		}

		public void RegisterRecord(BetRecord record)
		{
			RegisterBet(
				record.GameId,
				record.Chance,
				record.Outcome == BetOutcome.Win,
				record.BetAmount,
				record.NetAmount);
		}

		// Highest run across all segments, with the segment that holds it — what an unfiltered readout shows
		// ("18 (at 50% chance)"). Returns 0 / -1 when there is nothing recorded.
		public (int Run, int Chance) MaxConsecutiveLossesOverall()
		{
			int best = 0;
			int chance = -1;
			foreach (SegmentRuns runs in Segments.Values)
			{
				if (runs.MaxConsecutiveLosses > best)
				{
					best = runs.MaxConsecutiveLosses;
					chance = runs.Chance;
				}
			}

			return (best, chance);
		}

		public (int Run, int Chance) MaxConsecutiveWinsOverall()
		{
			int best = 0;
			int chance = -1;
			foreach (SegmentRuns runs in Segments.Values)
			{
				if (runs.MaxConsecutiveWins > best)
				{
					best = runs.MaxConsecutiveWins;
					chance = runs.Chance;
				}
			}

			return (best, chance);
		}

		public SegmentRuns RunsFor(string gameId, int chance) =>
			Segments.TryGetValue(SegmentKey(gameId, chance), out SegmentRuns runs) ? runs : null;

		public BetStatsRollup Clone()
		{
			var copy = new BetStatsRollup
			{
				IsComplete = IsComplete,
				SeededAtUtc = SeededAtUtc,
				TotalBets = TotalBets,
				TotalWins = TotalWins,
				TotalLosses = TotalLosses,
				TotalWagered = TotalWagered,
				TotalNetProfit = TotalNetProfit,
				MaxBetAmount = MaxBetAmount,
				MaxLossAmount = MaxLossAmount,
				MaxWonAmount = MaxWonAmount,
				SinceDepositBets = SinceDepositBets,
				SinceDepositWagered = SinceDepositWagered,
				SinceDepositProfit = SinceDepositProfit,
				PeakProfit = PeakProfit,
				CurrentDrawdown = CurrentDrawdown,
				MaxDrawdown = MaxDrawdown,
				CurrentSegmentKey = CurrentSegmentKey,
				CurrentLossRun = CurrentLossRun,
				CurrentWinRun = CurrentWinRun,
				Segments = new Dictionary<string, SegmentRuns>()
			};

			foreach (KeyValuePair<string, SegmentRuns> entry in Segments)
			{
				copy.Segments[entry.Key] = new SegmentRuns
				{
					GameId = entry.Value.GameId,
					Chance = entry.Value.Chance,
					MaxConsecutiveLosses = entry.Value.MaxConsecutiveLosses,
					MaxConsecutiveWins = entry.Value.MaxConsecutiveWins,
					Bets = entry.Value.Bets,
					Wins = entry.Value.Wins,
					Wagered = entry.Value.Wagered,
					NetProfit = entry.Value.NetProfit,
					MaxBetAmount = entry.Value.MaxBetAmount,
					MaxLossAmount = entry.Value.MaxLossAmount,
					MaxWonAmount = entry.Value.MaxWonAmount
				};
			}

			return copy;
		}
	}
}
