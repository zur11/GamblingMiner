using System.Collections.Generic;
using Scripts.Finance;

namespace Scripts.User
{
    public class UserBettingStats
    {
        private readonly List<UserBetRecord> _allBets = new();

        public IReadOnlyList<UserBetRecord> AllBets => _allBets;

        public int BetsSinceLastDeposit { get; private set; }
        public int TotalBets { get; private set; }

        public decimal AmountWageredSinceDeposit { get; private set; }
        public decimal TotalAmountWagered { get; private set; }

        public decimal ProfitSinceDeposit { get; private set; }
        public decimal TotalProfit { get; private set; }

        public void RegisterBet(string gameId, BetTransactionEvent bet)
        {
            var record = new UserBetRecord(
                gameId,
                bet.Timestamp,
                bet.BetAmount,
                bet.Profit,
                bet.IsWin
            );

            _allBets.Add(record);

            BetsSinceLastDeposit++;
            TotalBets++;

            AmountWageredSinceDeposit += bet.BetAmount;
            TotalAmountWagered += bet.BetAmount;

            ProfitSinceDeposit += bet.Profit;
            TotalProfit += bet.Profit;
        }

        public void RegisterDeposit()
        {
            BetsSinceLastDeposit = 0;
            AmountWageredSinceDeposit = 0m;
            ProfitSinceDeposit = 0m;
        }
    }
}
