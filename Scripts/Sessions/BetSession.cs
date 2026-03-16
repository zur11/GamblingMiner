using System;
using Godot;
using Scripts.Betting;
using Scripts.Finance;

namespace Scripts.Sessions
{
    public class BetSession
    {
        private readonly IBettingStrategy _strategy;

        public Guid SessionId { get; } = Guid.NewGuid();

        public bool IsRunning { get; private set; }

        public int RemainingBets { get; private set; }

        public IBettingStrategy.StopReason? LastStopReason { get; private set; }

        public BetSession(IBettingStrategy strategy)
        {
            _strategy = strategy;
        }

        public void Start(decimal startingBalance, int betCount)
        {
            _strategy.StartSession(startingBalance);

            RemainingBets = betCount;

            LastStopReason = null;

            IsRunning = true;
        }

        public decimal GetNextBet()
        {
            return _strategy.GetNextBet();
        }

        public void NotifyResult(
            decimal betAmount,
            decimal profit,
            bool isWin,
            decimal balance)
        {
            if (!IsRunning)
                return;

            var outcome = new BetOutcome(betAmount, profit, isWin);

            _strategy.OnBetResolved(outcome, balance);

            if (RemainingBets > 0)
                RemainingBets--;

            EvaluateStop(balance);
        }

        private void EvaluateStop(decimal balance)
        {
            if (RemainingBets == 0)
            {
                Stop(IBettingStrategy.StopReason.CounterCountReached);
                return;
            }

            if (_strategy.ShouldStop(balance))
            {
                Stop(_strategy.LastStopReason ??
                    IBettingStrategy.StopReason.ManualStop);
            }
        }

        public void Stop(IBettingStrategy.StopReason reason)
        {
            LastStopReason = reason;
            IsRunning = false;
            _strategy.Stop();
        }
    }
}
