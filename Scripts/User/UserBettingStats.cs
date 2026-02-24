using System;
using System.Collections.Generic;
using Scripts.Finance;

namespace Scripts.User
{
	public class UserBettingStats
	{
		private readonly List<UserBetRecord> _allBets = new();

		public IReadOnlyList<UserBetRecord> AllBets => _allBets.AsReadOnly();

		public int BetsSinceLastDeposit { get; private set; }
		public int TotalBets { get; private set; }

		public decimal AmountWageredSinceDeposit { get; private set; }
		public decimal TotalAmountWagered { get; private set; }

		public decimal ProfitSinceDeposit { get; private set; }
		public decimal TotalProfit { get; private set; }

		public void RegisterBet(string gameId, BetTransactionEvent bet)
		{
			if (bet == null)
				throw new ArgumentNullException(nameof(bet));

			var record = CreateRecord(gameId, bet);

			AddRecord(record);
			UpdateCounters(bet);
			UpdateAmounts(bet);
		}

		public void RegisterDeposit()
		{
			ResetSessionCounters();
		}

		private UserBetRecord CreateRecord(string gameId, BetTransactionEvent bet)
		{
			return new UserBetRecord(
				gameId,
				bet.Timestamp,
				bet.BetAmount,
				bet.Profit,
				bet.IsWin
			);
		}

		private void AddRecord(UserBetRecord record)
		{
			_allBets.Add(record);
		}

		private void UpdateCounters(BetTransactionEvent bet)
		{
			BetsSinceLastDeposit++;
			TotalBets++;
		}

		private void UpdateAmounts(BetTransactionEvent bet)
		{
			AmountWageredSinceDeposit += bet.BetAmount;
			TotalAmountWagered += bet.BetAmount;

			ProfitSinceDeposit += bet.Profit;
			TotalProfit += bet.Profit;
		}

		private void ResetSessionCounters()
		{
			BetsSinceLastDeposit = 0;
			AmountWageredSinceDeposit = 0m;
			ProfitSinceDeposit = 0m;
		}
	}
}
