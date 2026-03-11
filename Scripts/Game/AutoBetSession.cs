using System;
using Godot;
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

        public void SubscribeToBalanceChanged(Wallet wallet)
        {   
            _wallet = wallet;
            _wallet.BalanceDeltaChanged += OnBalanceDeltaChanged;
        }

        private void OnBalanceDeltaChanged(Guid? sessionId, decimal amount)
        {
            if (sessionId == SessionId)
            {
                // delta causado por esta sesión
                return;
            }

            // delta externo
            _strategy.OnExternalBalanceDelta(amount);

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

            GD.Print("NotifyResult called");
            EvaluateStop();
        }

        public bool ShouldStop(decimal currentBalance)
        {
            GD.Print("Calling ShouldStop");
            return _strategy.ShouldStop(currentBalance);
        }

        private void EvaluateStop()
        {
            GD.Print("EvaluateStop running");

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
