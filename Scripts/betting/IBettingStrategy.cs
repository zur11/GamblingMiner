namespace Scripts.Betting
{
    public interface IBettingStrategy
    {
        public enum StopReason
        {
            StopOnProfit,
            StopOnLoss,
            StopOnBlockMined,
            InsufficientBalance,
            ManualStop,
            CounterCountReached,
            InvalidBetAmount
        }

        decimal CalculateNextBet(
            decimal currentBet,
            BetOutcome outcome,
            BettingStrategyConfig config);
    }
}
