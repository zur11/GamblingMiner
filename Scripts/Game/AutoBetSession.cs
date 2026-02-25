using Scripts.Betting;
using Scripts.Finance;

namespace Scripts.Game
{
    public class AutoBetSession
    {
        private readonly IBettingStrategy _strategy;

        public AutoBetSession(IBettingStrategy strategy)
        {
            _strategy = strategy;
        }

        public void Configure(BettingStrategyConfig config)
        {
            _strategy.ApplyConfiguration(config);
        }

        public void Start(decimal currentBalance)
        {
            _strategy.StartSession(currentBalance);
        }

        public void Stop()
        {
            _strategy.Stop();
        }

        public bool ShouldStop(decimal currentBalance)
        {
            return _strategy.ShouldStop(currentBalance);
        }

        public decimal GetNextBet()
        {
            return _strategy.GetNextBet();
        }

        public void NotifyResult(decimal betAmount, decimal profit, bool isWin, decimal currentBalance)
        {
            var outcome = new BetOutcome(betAmount, profit, isWin);
            _strategy.OnBetResolved(outcome, currentBalance);
        }

        public bool IsRunning => _strategy.IsRunning;
    }
}
