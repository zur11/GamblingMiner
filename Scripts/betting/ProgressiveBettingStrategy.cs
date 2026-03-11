using System;
using Godot;

namespace Scripts.Betting
{
    public class ProgressiveBettingStrategy : IBettingStrategy
    {
        private BettingStrategyConfig _config;

        private decimal _currentBet;
        private decimal _sessionProfit;
        private int _remainingBets;

        public bool IsRunning { get; private set; }

        public IBettingStrategy.StopReason? LastStopReason { get; private set; }

        public int RemainingBets => _remainingBets;

        public void ApplyConfiguration(BettingStrategyConfig config)
        {
            _config = config;
            Reset();
        }

        public void SetBetCount(int count)
        {
            _remainingBets = count;
        }

        public void SetLastStopReason(IBettingStrategy.StopReason reason)
        {
            LastStopReason = reason;
        }

        public void StartSession(decimal startingBalance)
        {
            if (_config == null)
                throw new InvalidOperationException("Strategy not configured.");

            _currentBet = _config.BaseBet;

            _sessionProfit = 0m;

            LastStopReason = null;

            IsRunning = true;
        }

        public void OnBetResolved(BetOutcome outcome, decimal currentBalance)
        {
            if (!IsRunning)
                return;

            _sessionProfit += outcome.Profit;

            if (_config.IncreasePercent <= 0m)
                return;

            bool shouldIncrease =
                (outcome.IsWin && _config.IncreaseOnWin) ||
                (!outcome.IsWin && _config.IncreaseOnLoss);

            if (!shouldIncrease)
            {
                _currentBet = _config.BaseBet;
                return;
            }

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
                return false;

            if (_config.StopOnProfit.HasValue &&
                _sessionProfit >= _config.StopOnProfit.Value)
            {
                LastStopReason = IBettingStrategy.StopReason.StopOnProfit;
                IsRunning = false;
                return true;
            }

            if (_config.StopOnLoss.HasValue &&
                _sessionProfit <= -_config.StopOnLoss.Value)
            {
                LastStopReason = IBettingStrategy.StopReason.StopOnLoss;
                IsRunning = false;
                return true;
            }

            if (currentBalance < _currentBet)
            {
                LastStopReason = IBettingStrategy.StopReason.InsufficientBalance;
                IsRunning = false;
                return true;
            }

            if (_remainingBets > 0)
            {
                _remainingBets--;

                if (_remainingBets == 0)
                {
                    LastStopReason = IBettingStrategy.StopReason.CounterCountReached;
                    IsRunning = false;
                    return true;
                }
            }

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

            _sessionProfit = 0m;

            _remainingBets = 0;

            LastStopReason = null;
        }

        public void ClearStopReason()
        {
            LastStopReason = null;
        }
    }
}