using System;
using Scripts.Betting;
using Scripts.Finance;

namespace Scripts.Game
{
    public class AutoBetSession
    {
        public event Action<Guid, IBettingStrategy.StopReason?> SessionStopped;

        public Guid SessionId { get; } = Guid.NewGuid();
        private readonly IBettingStrategy _strategy;
        private Wallet _wallet;

        public AutoBetSession(IBettingStrategy strategy)
        {
            _strategy = strategy;
        }

        public void SetBetCount(int count)
        {
            if (_strategy is ProgressiveBettingStrategy progressive)
                progressive.SetBetCount(count);
        }

        public void SubscribeToBalanceChanged(Wallet wallet)
        {   
            _wallet = wallet;
            _wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
        }

        private void OnBalanceDeltaChanged(Guid? sessionId, decimal amount)
        {
            if (sessionId == SessionId)
            {
                _strategy.OnBalanceDeltaChanged(amount);
                return; // evita EvaluateStop prematuro
            }

            EvaluateStop();
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

        public void Stop()
        {
            _strategy.Stop();
        }

        public decimal GetNextBet()
        {
            return _strategy.GetNextBet();
        }

        public void NotifyResult(decimal betAmount, decimal profit, bool isWin, decimal currentBalance)
        {
            var outcome = new BetOutcome(betAmount, profit, isWin);
            
            _strategy.OnBetResolved(outcome, currentBalance);
            EvaluateStop();
        }

        public bool ShouldStop(decimal currentBalance)
        {
            return _strategy.ShouldStop(currentBalance);
        }

        private void EvaluateStop()
        {
            if (!_strategy.IsRunning)
                return;

            if (_wallet == null)
                return;

            if (_strategy.ShouldStop(_wallet.Balance))
            {
                var reason = _strategy.LastStopReason;
                _strategy.Stop();

                SessionStopped?.Invoke(SessionId, reason);
            }
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
