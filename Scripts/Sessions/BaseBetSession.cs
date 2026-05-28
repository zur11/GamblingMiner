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
        public event Action<BaseBetSession> OnStopped;

        public bool IsRunning { get; protected set; }
        public int RemainingBets { get; protected set; }
        public IBettingStrategy.StopReason LastStopReason { get; private set; }
        public bool IsInfinite => RemainingBets == int.MaxValue;
        public int ExecutedBetsCount { get; private set; }
        public decimal CurrentBet => _currentBet;
        public int ProgressionTriggerStreak { get; private set; }
        public decimal SessionBaseBet => _config?.BaseBet ?? 0m;
        public decimal SessionStartingBalance { get; private set; }
        public decimal ProgressionAnchorBalance { get; private set; }
        public decimal SessionProfit => _sessionProfit;

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
            SessionStartingBalance = _wallet.Balance;

            _config = config;
            _currentBet = config.BaseBet;
            _sessionProfit = 0m;
            ExecutedBetsCount = 0;
            ProgressionTriggerStreak = 0;
            ProgressionAnchorBalance = _wallet.Balance;

            IsRunning = true;
        }

        public virtual void Stop(IBettingStrategy.StopReason reason)
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            LastStopReason = reason;

            OnStopped?.Invoke(this);
        }

        public (DiceResult, BetTransactionEvent, decimal) ExecuteNext(
            int chance,
            bool isHigh,
            DateTime? timestampUtc = null)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Session not running");

            var (result, betEvent) =
                _betService.ExecuteBet(
                    _currentBet,
                    chance,
                    isHigh,
                    GetSessionId(),
                    timestampUtc
                );

            var outcome = new BetOutcome(
                betEvent.BetAmount,
                betEvent.CreditedProfit,
                result.IsWin
            );

            decimal balanceBeforeBet = betEvent.BalanceAfter - outcome.Profit;

            _sessionProfit += outcome.Profit;
            UpdateProgressionStreak(outcome, balanceBeforeBet, betEvent.BalanceAfter);

            decimal previousBet = _currentBet;
            decimal nextBet = Money.Normalize(_strategy.CalculateNextBet(
                outcome.BetAmount,
                outcome,
                _config
            ));
            DebugAssertProgression(previousBet, outcome, nextBet);
            _currentBet = nextBet;
            ExecutedBetsCount++;

            ApplyStopConditions();

            return (result, betEvent, _currentBet);
        }

        private void UpdateProgressionStreak(
            BetOutcome outcome,
            decimal balanceBeforeBet,
            decimal balanceAfterBet)
        {
            bool isTriggerOutcome =
                (outcome.IsWin && _config.IncreaseOnWin) ||
                (!outcome.IsWin && _config.IncreaseOnLoss);

            if (isTriggerOutcome)
            {
                if (ProgressionTriggerStreak == 0)
                    ProgressionAnchorBalance = balanceBeforeBet;

                ProgressionTriggerStreak++;
                return;
            }

            ProgressionTriggerStreak = 0;
            ProgressionAnchorBalance = balanceAfterBet;
        }

        // 🔥 EXTENSION POINTS

        protected abstract Guid? GetSessionId();

        protected virtual void ApplyStopConditions()
        {
            decimal stopBaseline = _config.UseProgressionAnchorStops
                ? ProgressionAnchorBalance
                : SessionStartingBalance;
            decimal stopProfitMetric = _wallet.Balance - stopBaseline;

            if (_config.StopOnProfit.HasValue &&
                stopProfitMetric >= _config.StopOnProfit.Value)
            {
                HandleProfitOrLossStop(IBettingStrategy.StopReason.StopOnProfit);
                if (!IsRunning)
                    return;
            }

            if (_config.StopOnLoss.HasValue &&
                stopProfitMetric <= -_config.StopOnLoss.Value)
            {
                HandleProfitOrLossStop(IBettingStrategy.StopReason.StopOnLoss);
                if (!IsRunning)
                    return;
            }

            if (_currentBet > _wallet.Balance)
            {
                LastStopReason = IBettingStrategy.StopReason.InsufficientBalance;
                Stop(LastStopReason);
            }

            if (RemainingBets != int.MaxValue)
            {
                RemainingBets--;

                if (RemainingBets <= 0)
                {
                    LastStopReason = IBettingStrategy.StopReason.CounterCountReached;
                    Stop(LastStopReason);
                }
            }
        }

        private void HandleProfitOrLossStop(IBettingStrategy.StopReason reason)
        {
            LastStopReason = reason;

            if (!_config.InsistAfterStop)
            {
                Stop(LastStopReason);
                return;
            }

            _currentBet = _config.BaseBet;
            _sessionProfit = 0m;
            ProgressionTriggerStreak = 0;
            SessionStartingBalance = _wallet.Balance;
            ProgressionAnchorBalance = _wallet.Balance;
        }

        protected virtual void DebugAssertProgression(decimal previousBet, BetOutcome outcome, decimal nextBet)
        {
            if (_config == null)
            {
                return;
            }

            if (outcome.IsWin || !_config.IncreaseOnLoss || _config.IncreasePercent <= 0m)
            {
                return;
            }

            // If we lost and should increase on loss, next bet should be > previous bet (unless clamped by balance later).
            if (nextBet <= previousBet && nextBet == _config.BaseBet)
            {
                GD.Print($"[ProgressionDebug] Loss but next bet did not increase. prev={previousBet:F8} next={nextBet:F8} base={_config.BaseBet:F8} inc%={_config.IncreasePercent}");
            }
        }
    }
}
