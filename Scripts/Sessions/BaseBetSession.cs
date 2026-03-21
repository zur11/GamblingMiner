using System;
using Godot;
using Scripts.Betting;
using Scripts.Finance;
using Scripts.Controllers;
using Scripts.Dice;
using Scripts.Game;

namespace Scripts.Sessions
{
    public abstract class BaseBetSession
    {
        public event Action<IBettingStrategy.StopReason?> OnStopped;

        public bool IsRunning { get; protected set; }
        public int RemainingBets { get; protected set; }
        public bool IsInfinite => RemainingBets == int.MaxValue;

        protected decimal _currentBet;
        protected decimal _sessionProfit;
        protected BettingStrategyConfig _config;

        protected readonly BetService _betService;
        protected readonly Wallet _wallet;
        protected readonly ProgressiveBettingStrategy _strategy;

        protected BaseBetSession(
            BetService betService,
            Wallet wallet,
            ProgressiveBettingStrategy strategy)
        {
            _betService = betService;
            _wallet = wallet;
            _strategy = strategy;
        }

        public virtual void Start(int betCount, BettingStrategyConfig config)
        {
            RemainingBets = betCount <= 0 ? int.MaxValue : betCount;

            _config = config;
            _currentBet = config.BaseBet;
            _sessionProfit = 0m;

            IsRunning = true;
        }

        public virtual void Stop(IBettingStrategy.StopReason reason)
        {
            IsRunning = false;
            OnStopped?.Invoke(reason);
        }

        public (DiceResult, BetTransactionEvent, decimal) ExecuteNext(
            int chance,
            bool isHigh)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Session not running");

            var (result, betEvent) =
                _betService.ExecuteBet(
                    _currentBet,
                    chance,
                    isHigh,
                    GetSessionId() // 🔥 punto clave
                );

            var outcome = new BetOutcome(
                betEvent.BetAmount,
                betEvent.Profit,
                result.IsWin
            );

            _sessionProfit += outcome.Profit;

            _currentBet = _strategy.CalculateNextBet(
                _currentBet,
                outcome,
                _config
            );

            ApplyStopConditions();

            return (result, betEvent, _currentBet);
        }

        // 🔥 EXTENSION POINTS

        protected abstract Guid? GetSessionId();

        protected virtual void ApplyStopConditions()
        {
            if (_config.StopOnProfit.HasValue &&
                _sessionProfit >= _config.StopOnProfit.Value)
            {
                Stop(IBettingStrategy.StopReason.StopOnProfit);
            }

            if (_config.StopOnLoss.HasValue &&
                _sessionProfit <= -_config.StopOnLoss.Value)
            {
                Stop(IBettingStrategy.StopReason.StopOnLoss);
            }

            if (_currentBet > _wallet.Balance)
            {
                Stop(IBettingStrategy.StopReason.InsufficientBalance);
            }

            if (RemainingBets != int.MaxValue)
            {
                RemainingBets--;

                if (RemainingBets <= 0)
                {
                    Stop(IBettingStrategy.StopReason.CounterCountReached);
                }
            }
        }
    }
}
