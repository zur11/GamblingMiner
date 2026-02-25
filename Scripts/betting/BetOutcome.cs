namespace Scripts.Betting
{
    public class BetOutcome
    {
        public decimal BetAmount { get; }
        public decimal Profit { get; }
        public bool IsWin { get; }

        public BetOutcome(decimal betAmount, decimal profit, bool isWin)
        {
            BetAmount = betAmount;
            Profit = profit;
            IsWin = isWin;
        }
    }
}
