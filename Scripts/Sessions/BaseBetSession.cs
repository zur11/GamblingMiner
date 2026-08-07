using System;
using System.Globalization;
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
        // Stop baselines: the bankroll the session started from. The two stops keep SEPARATE baselines because
        // each insists on its own — a reset re-anchors ONLY the side that fired (see ResetProgressionToBase),
        // so one stop's reset can never redefine what the other one is measuring.
        public decimal ProfitSessionStartingBalance { get; private set; }
        public decimal LossSessionStartingBalance { get; private set; }
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
            ProfitSessionStartingBalance = _wallet.Balance;
            LossSessionStartingBalance = _wallet.Balance;

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
        // progression streak. A "trigger outcome" is one whose own increase percent is > 0 — i.e. the outcome
        // grows the bet instead of resetting it to base (see ProgressiveBettingStrategy.CalculateNextBet).
        private void UpdateProgressionStreak(
            BetOutcome outcome,
            decimal balanceBeforeBet,
            decimal balanceAfterBet)
        {
            bool isTriggerOutcome = outcome.IsWin
                ? _config.IncreaseOnWinPercent > 0m
                : _config.IncreaseOnLossPercent > 0m;

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
            // Each stop measures P/L as (current balance − its own baseline), always from where the session
            // (or that stop's last insist reset) started. There is no second baseline mode: the progression-run
            // "Anchor" alternative was removed — see ProjectDesignManual §25.12.
            decimal profitMetric = _wallet.Balance - ProfitSessionStartingBalance;
            decimal lossMetric = _wallet.Balance - LossSessionStartingBalance;

            if (_config.StopOnProfit.HasValue &&
                profitMetric >= _config.StopOnProfit.Value)
            {
                HandleStopOnProfit();
                if (!IsRunning)
                    return;
            }

            if (_config.StopOnLoss.HasValue &&
                lossMetric <= -_config.StopOnLoss.Value)
            {
                HandleStopOnLoss();
                if (!IsRunning)
                    return;
            }

            if (_currentBet > _wallet.Balance)
            {
                if (_config.InsistAfterStopOnLoss && _config.BaseBet <= _wallet.Balance)
                {
                    // Insist: the grown progression bet is unaffordable, but the base bet still fits the
                    // bankroll — reset the progression to base and keep going WITHOUT a recharge. A recharge
                    // only happens when even the base bet can't be afforded (handled below by stopping with
                    // InsufficientBalance, which the simulation then recharges + restarts from base).
                    // A loss-side condition, so it closes the segment on the same terms as a StopOnLoss hit.
                    ResetProgressionToBase(
                        reanchorProfit: _config.InsistAfterStopOnProfit,
                        reanchorLoss: true);
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

        private void HandleStopOnProfit()
        {
            LastStopReason = IBettingStrategy.StopReason.StopOnProfit;

            if (!_config.InsistAfterStopOnProfit)
            {
                Stop(LastStopReason);
                return;
            }

            ResetProgressionToBase(
                reanchorProfit: true,
                reanchorLoss: _config.InsistAfterStopOnLoss);
        }

        private void HandleStopOnLoss()
        {
            LastStopReason = IBettingStrategy.StopReason.StopOnLoss;

            if (!_config.InsistAfterStopOnLoss)
            {
                Stop(LastStopReason);
                return;
            }

            ResetProgressionToBase(
                reanchorProfit: _config.InsistAfterStopOnProfit,
                reanchorLoss: true);
        }

        // Restart the progression from the base bet (used by both Insist switches, and by the bankroll-limit
        // branch when the grown bet no longer fits but the base bet still does).
        //
        // The baseline of the side that fired is ALWAYS re-anchored: a reset adds no money, so without it the
        // metric would still be past the threshold on the very next bet and would re-trigger forever.
        //
        // Whether the OTHER side re-anchors too is the caller's decision, and it turns on whether that side
        // insists (ProjectDesignManual §25.13). An insisting stop is segment-scoped — it says "close this
        // stretch and start another" — so when both insist they share one segment and either threshold ends
        // it. A stop that does NOT insist is a whole-session goal ("stop when I am up 100 overall"), and its
        // baseline must survive the other side's resets or the goal quietly becomes "up 100 since the last
        // drawdown". The progression anchor is a progression-level concept (it feeds the Martingale calculator
        // and the trigger streak) and always moves with the reset.
        private void ResetProgressionToBase(bool reanchorProfit, bool reanchorLoss)
        {
            _currentBet = _config.BaseBet;
            _sessionProfit = 0m;
            ProgressionTriggerStreak = 0;
            ProgressionAnchorBalance = _wallet.Balance;

            if (reanchorProfit)
            {
                ProfitSessionStartingBalance = _wallet.Balance;
            }

            if (reanchorLoss)
            {
                LossSessionStartingBalance = _wallet.Balance;
            }
        }

        protected virtual void DebugAssertProgression(decimal previousBet, BetOutcome outcome, decimal nextBet)
        {
            if (_config == null)
            {
                return;
            }

            if (outcome.IsWin || _config.IncreaseOnLossPercent <= 0m)
            {
                return;
            }

            // If we lost and should increase on loss, next bet should be > previous bet (unless clamped by balance later).
            if (nextBet <= previousBet && nextBet == _config.BaseBet)
            {
                GD.Print(string.Create(CultureInfo.InvariantCulture, $"[ProgressionDebug] Loss but next bet did not increase. prev={previousBet:F8} next={nextBet:F8} base={_config.BaseBet:F8} lossInc%={_config.IncreaseOnLossPercent}"));
            }
        }
    }
}
