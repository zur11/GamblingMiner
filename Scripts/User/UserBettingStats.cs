using System;
using Scripts.Finance;

namespace Scripts.User
{
	public class UserBettingStats
	{
		public int TotalBets { get; private set; }
		public decimal TotalAmountWagered { get; private set; }
		public decimal TotalProfit { get; private set; }

		public int BetsSinceLastDeposit { get; private set; }
		public decimal AmountWageredSinceDeposit { get; private set; }
		public decimal ProfitSinceDeposit { get; private set; }

		public int TotalWins { get; private set; }
		public int TotalLosses => TotalBets - TotalWins;

		public decimal CurrentDrawdown { get; private set; }
		public decimal MaxDrawdown { get; private set; }

		private decimal _peakProfit = 0m;

		public void RegisterBet(string gameId, BetTransactionEvent bet)
		{
			ValidateBet(bet);
			ApplyBet(bet);
		}

		public void RegisterDeposit()
		{
			ResetSessionMetrics();
		}

		public decimal GetRoi()
		{
			if (TotalAmountWagered == 0m)
				return 0m;

			return TotalProfit / TotalAmountWagered;
		}

		public decimal GetSessionRoi()
		{
			if (AmountWageredSinceDeposit == 0m)
				return 0m;

			return ProfitSinceDeposit / AmountWageredSinceDeposit;
		}

		public decimal GetWinRate()
		{
			if (TotalBets == 0)
				return 0m;

			return (decimal)TotalWins / TotalBets;
		}

		public bool IsInDrawdown => CurrentDrawdown < 0m;

		// Rebuilds this object from the persisted rollup instead of by replaying the journal (mini-plan 03
		// stage 1). Every field here is a running value the rollup already maintains with identical
		// arithmetic, so the result is the same object a full replay would produce — which is what allows
		// the boot-time scan of ~200,000 records to be dropped entirely.
		public static UserBettingStats FromRollup(BetStatsRollup rollup)
		{
			var stats = new UserBettingStats();
			if (rollup == null)
			{
				return stats;
			}

			stats.TotalBets = rollup.TotalBets;
			stats.TotalWins = rollup.TotalWins;
			stats.TotalAmountWagered = rollup.TotalWagered;
			stats.TotalProfit = rollup.TotalNetProfit;
			stats.BetsSinceLastDeposit = rollup.SinceDepositBets;
			stats.AmountWageredSinceDeposit = rollup.SinceDepositWagered;
			stats.ProfitSinceDeposit = rollup.SinceDepositProfit;
			stats._peakProfit = rollup.PeakProfit;
			stats.CurrentDrawdown = rollup.CurrentDrawdown;
			stats.MaxDrawdown = rollup.MaxDrawdown;
			return stats;
		}

		private void ValidateBet(BetTransactionEvent bet)
		{
			if (bet == null)
				throw new ArgumentNullException(nameof(bet));

			if (bet.BetAmount <= 0m)
				throw new InvalidOperationException("Bet amount must be positive.");
		}

		private void ApplyBet(BetTransactionEvent bet)
		{
			TotalBets++;
			TotalAmountWagered += bet.BetAmount;
			TotalProfit += bet.CreditedProfit;
			BetsSinceLastDeposit++;
			AmountWageredSinceDeposit += bet.BetAmount;
			ProfitSinceDeposit += bet.CreditedProfit;

			if (bet.IsWin)
				TotalWins++;

			if (TotalProfit > _peakProfit)
				_peakProfit = TotalProfit;

			CurrentDrawdown = TotalProfit - _peakProfit;

			if (CurrentDrawdown < MaxDrawdown)
				MaxDrawdown = CurrentDrawdown;
		}

		private void ResetSessionMetrics()
		{
			BetsSinceLastDeposit = 0;
			AmountWageredSinceDeposit = 0m;
			ProfitSinceDeposit = 0m;
		}
	}
}
