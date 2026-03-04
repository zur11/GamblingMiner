using System;

namespace Scripts.Betting
{
    public class ProgressiveBettingStrategy : IBettingStrategy
    {
        private BettingStrategyConfig _config;

        private decimal _currentBet;
        private decimal _sessionStartBalance;
		private SessionCapitalTracker _capitalTracker = new();

		public bool IsRunning { get; private set; }
        public IBettingStrategy.StopReason? LastStopReason { get; private set; }
		private int _remainingBets;

		public void OnBalanceDeltaChanged(decimal amount)
		{
			_capitalTracker.OnBalanceDeltaChanged(amount);
		}

		public void SetBetCount(int count)
        {
            _remainingBets = count; // 0 = infinito
        }

        public void ApplyConfiguration(BettingStrategyConfig config)
        {
            _config = config;
            Reset();
        }

        public void StartSession(decimal startingBalance)
        {
            _capitalTracker.Reset();
            if (_config == null)
                throw new InvalidOperationException("Strategy not configured.");

            _sessionStartBalance = startingBalance;
            _currentBet = _config.BaseBet;

            LastStopReason = null;
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
                return false;

            decimal delta = currentBalance - (_sessionStartBalance + _capitalTracker.GetDifferenceWithBalance());

			if (_config.StopOnProfit.HasValue &&
                delta >= _config.StopOnProfit.Value)
            {
                LastStopReason = IBettingStrategy.StopReason.StopOnProfit;
                IsRunning = false;
                return true;
            }

            if (_config.StopOnLoss.HasValue &&
                delta <= -_config.StopOnLoss.Value)
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
					IsRunning = false;
                    LastStopReason = null; // no es stop por regla
                    return true;
                }
            }

            return false;
        }

        public void Stop()
        {
            IsRunning = false;
        }

        public void RestartSession(decimal currentBalance)
        {
            _sessionStartBalance = currentBalance;
            _currentBet = _config.BaseBet;
            LastStopReason = null;
            IsRunning = true;
			_capitalTracker.Reset();
		}

        public void Reset()
        {
            IsRunning = false;
            _currentBet = 0m;
            _sessionStartBalance = 0m;
            LastStopReason = null;
            _capitalTracker.Reset();
            _remainingBets = 0;
        }

        public void ClearStopReason()
        {
            LastStopReason = null;
        }
    }
}