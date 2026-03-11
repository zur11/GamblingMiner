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

        StopReason? LastStopReason { get; }

        void SetLastStopReason(StopReason reason);

        void OnExternalBalanceDelta(decimal amount);

        void StartSession(decimal startingBalance);

        void ApplyConfiguration(BettingStrategyConfig config);

        void OnBetResolved(BetOutcome outcome, decimal currentBalance);

        decimal GetNextBet();

        bool ShouldStop(decimal currentBalance);

        bool IsRunning { get; }

        void Stop();

        void Reset();

        void ClearStopReason();
    }
}