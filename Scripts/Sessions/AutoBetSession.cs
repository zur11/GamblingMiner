using System;
using Godot;
using Scripts.Betting;
using Scripts.Finance;

namespace Scripts.Sessions
{
    public class AutoBetSession
    {
        public event Action<Guid, IBettingStrategy.StopReason?> SessionStopped;

        public Guid SessionId { get; } = Guid.NewGuid();
        private readonly IBettingStrategy _strategy;
        public int RemainingBets
        {
            get
            {
                if (_strategy is ProgressiveBettingStrategy progressive)
                    return progressive.RemainingBets;

                return 0;
            }
        }

        public AutoBetSession(IBettingStrategy strategy)
        {
            _strategy = strategy;
        }

        public void SetBetCount(int count)
        {
            if (_strategy is ProgressiveBettingStrategy progressive)
                progressive.SetBetCount(count);
        }

        public void SetLastStopReason(IBettingStrategy.StopReason reason)
        {
            _strategy.SetLastStopReason(reason);
        }

        public void Configure(BettingStrategyConfig config)
        {
            _strategy.ApplyConfiguration(config);
        }

        public void Start(decimal currentBalance)
        {
            if (!_strategy.IsRunning)
                _strategy.StartSession(currentBalance);
        }

        public void Stop(IBettingStrategy.StopReason reason)
        {
            _strategy.SetLastStopReason(reason);
            _strategy.Stop();

            SessionStopped?.Invoke(SessionId, reason);
        }

        public decimal GetNextBet()
        {
            return _strategy.GetNextBet();
        }

        public void NotifyResult(decimal betAmount, decimal profit, bool isWin, decimal currentBalance)
        {
            var outcome = new BetOutcome(betAmount, profit, isWin);

            _strategy.OnBetResolved(outcome, currentBalance);

            EvaluateStop(currentBalance);
        }

        private void EvaluateStop(decimal balance)
        {
            if (!_strategy.IsRunning)
                return;

            if (_strategy.ShouldStop(balance))
            {
                var reason = _strategy.LastStopReason;

                _strategy.Stop();

                SessionStopped?.Invoke(SessionId, reason);
            }
        }

        public bool ShouldStop(decimal currentBalance)
        {
            GD.Print("Calling ShouldStop");
            return _strategy.ShouldStop(currentBalance);
        }

        public bool IsRunning => _strategy.IsRunning;

        public IBettingStrategy.StopReason? LastStopReason =>
            _strategy.LastStopReason;

        public void ClearStopReason()
        {
            _strategy.ClearStopReason();
        }
    }
}
