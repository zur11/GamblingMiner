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
