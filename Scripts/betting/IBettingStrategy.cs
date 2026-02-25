namespace Scripts.Betting
{
    public interface IBettingStrategy
    {
        void StartSession(decimal startingBalance);

        void ApplyConfiguration(BettingStrategyConfig config);

        void OnBetResolved(BetOutcome outcome, decimal currentBalance);

        decimal GetNextBet();

        bool ShouldStop(decimal currentBalance);

        bool IsRunning { get; }

        void Stop();

        void Reset();
    }
}
