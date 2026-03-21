using System;
using Godot;
using Scripts.Betting;
using Scripts.Finance;
using Scripts.Controllers;
using Scripts.Dice;
using Scripts.Game;

namespace Scripts.Sessions
{
    public class BetSession
    {
        public event Action<IBettingStrategy.StopReason?> OnStopped;

        public bool IsRunning { get; private set; }

        public int RemainingBets { get; private set; }

        public bool IsInfinite => RemainingBets == int.MaxValue;

        private decimal _currentBet;
        private decimal _sessionProfit;
        private BettingStrategyConfig _config;

        private readonly BetService _betService;
        private readonly Wallet _wallet;
        private readonly ProgressiveBettingStrategy _strategy;

        public BetSession(
            BetService betService,
            Wallet wallet,
            ProgressiveBettingStrategy strategy)
        {
            _betService = betService;
            _wallet = wallet;
            _strategy = strategy;
        }

        public void Start(decimal balance, int betCount, BettingStrategyConfig config)
        {
            RemainingBets = betCount <= 0 ? int.MaxValue : betCount;

            _config = config;

            _currentBet = config.BaseBet;
            _sessionProfit = 0m;

            IsRunning = true;
        }

        public void Stop(IBettingStrategy.StopReason reason)
        {
            IsRunning = false;
            OnStopped?.Invoke(reason);
        }

        public (DiceResult, BetTransactionEvent, decimal nextBet) ExecuteNext(
            int chance,
            bool isHigh)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Session not running");

            var (result, betEvent) =
                _betService.ExecuteBet(_currentBet, chance, isHigh, null);

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

            // stop conditions
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

            // contador
            if (RemainingBets != int.MaxValue)
            {
                RemainingBets--;

                if (RemainingBets <= 0)
                {
                    Stop(IBettingStrategy.StopReason.CounterCountReached);
                }
            }

            return (result, betEvent, _currentBet);
        }
    }
}
