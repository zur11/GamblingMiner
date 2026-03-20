namespace Scripts.Betting
{
    public interface IBettingStrategy
    {
        public enum StopReason
        {
            StopOnProfit,
            StopOnLoss,
            InsufficientBalance,
            ManualStop,
            CounterCountReached
        }

        decimal CalculateNextBet(
            decimal currentBet,
            BetOutcome outcome,
            BettingStrategyConfig config);
    }
}