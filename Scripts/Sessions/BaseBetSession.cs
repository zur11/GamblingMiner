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
        // Baseline for stop-condition P/L in SESSION mode: the bankroll when the session started. Re-anchored
        // to the current balance on every Insist-After-Stop reset (see ResetProgressionToBase).
        public decimal SessionStartingBalance { get; private set; }
        // Baseline for stop-condition P/L in ANCHOR mode: the bankroll at the start of the CURRENT progression
        // streak (the last base bet that began the run). Maintained by UpdateProgressionStreak.
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

        // Tracks ANCHOR-mode's baseline (ProgressionAnchorBalance) = the bankroll at the start of the current
        // progression streak. A "trigger outcome" is the one that grows the bet (a loss with IncreaseOnLoss, or
        // a win with IncreaseOnWin).
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
                // First bet of a new streak → anchor to the balance just before this (base) bet.
                if (ProgressionTriggerStreak == 0)
                    ProgressionAnchorBalance = balanceBeforeBet;

                ProgressionTriggerStreak++;
                return;
            }

            // Non-trigger outcome ends the streak; the next base bet starts fresh from here.
            ProgressionTriggerStreak = 0;
            ProgressionAnchorBalance = balanceAfterBet;
        }

        // 🔥 EXTENSION POINTS

        protected abstract Guid? GetSessionId();

        protected virtual void ApplyStopConditions()
        {
            // Stop conditions measure profit/loss as (current balance − baseline). The baseline depends on
            // the chosen mode:
            //   • SESSION mode (UseProgressionAnchorStops = false): from SessionStartingBalance — the bankroll
            //     when the session started (cumulative session P/L).
            //   • ANCHOR mode  (UseProgressionAnchorStops = true):  from ProgressionAnchorBalance — the bankroll
            //     at the start of the current progression streak (P/L of just this run; a win clears the streak).
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
                if (_config.InsistAfterStop && _config.BaseBet <= _wallet.Balance)
                {
                    // Insist: the grown progression bet is unaffordable, but the base bet still fits the
                    // bankroll — reset the progression to base and keep going WITHOUT a recharge. A recharge
                    // only happens when even the base bet can't be afforded (handled below by stopping with
                    // InsufficientBalance, which the simulation then recharges + restarts from base).
                    ResetProgressionToBase();
                }
                else
                {
                    LastStopReason = IBettingStrategy.StopReason.InsufficientBalance;
                    Stop(LastStopReason);
                }
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

            ResetProgressionToBase();
        }

        // Restart the progression from the base bet (used by Insist After Stop, both on a profit/loss
        // stop and when the bankroll can no longer sustain the grown bet but still covers the base bet).
        // Re-anchors BOTH stop baselines to the current balance: each post-reset segment is then measured
        // fresh. This re-anchor is what makes SESSION-mode insist work — without it the P/L metric would
        // still be past the threshold right after a reset and would re-trigger every bet.
        private void ResetProgressionToBase()
        {
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
