using System;

namespace Scripts.Betting
{
    public class ProgressiveBettingStrategy : IBettingStrategy
    {
        private BettingStrategyConfig _config;

        private decimal _currentBet;
        private decimal _sessionStartBalance;

        public bool IsRunning { get; private set; }

        public void ApplyConfiguration(BettingStrategyConfig config)
        {
            _config = config;
            Reset();
        }

        public void StartSession(decimal startingBalance)
        {
            if (_config == null)
                throw new InvalidOperationException("Strategy not configured.");

            _sessionStartBalance = startingBalance;
            _currentBet = _config.BaseBet;

            IsRunning = true;
        }

        public void OnBetResolved(BetOutcome outcome, decimal currentBalance)
        {
            if (!IsRunning)
                return;

            if (_config.IncreasePercent <= 0m)
                return;

            bool shouldIncrease =
                (outcome.IsWin && _config.IncreaseOnWin) ||
                (!outcome.IsWin && _config.IncreaseOnLoss);

            if (!shouldIncrease)
                return;

            var multiplier = 1m + (_config.IncreasePercent / 100m);
            _currentBet *= multiplier;
        }

        public decimal GetNextBet()
        {
            return _currentBet;
        }

        public bool ShouldStop(decimal currentBalance)
        {
            if (!IsRunning)
                return true;

            decimal delta = currentBalance - _sessionStartBalance;

            if (_config.StopOnProfit.HasValue &&
                delta >= _config.StopOnProfit.Value)
                return true;

            if (_config.StopOnLoss.HasValue &&
                delta <= -_config.StopOnLoss.Value)
                return true;

            return false;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void Reset()
        {
            IsRunning = false;
            _currentBet = 0m;
            _sessionStartBalance = 0m;
        }
    }
}
