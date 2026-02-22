using System;

namespace Scripts.User
{
    public class UserBetRecord
    {
        public string GameId { get; }
        public DateTime Timestamp { get; }
        public decimal BetAmount { get; }
        public decimal Profit { get; }
        public bool IsWin { get; }

        public UserBetRecord(
            string gameId,
            DateTime timestamp,
            decimal betAmount,
            decimal profit,
            bool isWin)
        {
            GameId = gameId;
            Timestamp = timestamp;
            BetAmount = betAmount;
            Profit = profit;
            IsWin = isWin;
        }
    }
}
