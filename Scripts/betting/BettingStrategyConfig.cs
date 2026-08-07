namespace Scripts.Betting
{
    public class BettingStrategyConfig
    {
        public decimal BaseBet { get; init; }

        // One progression percent per outcome, each armed by its own value: > 0 grows the bet after that
        // outcome, 0 (or blank in the panel) resets to base. They are independent — a strategy may grow on
        // losses only, on wins only, on both, or on neither (flat betting).
        public decimal IncreaseOnLossPercent { get; init; }
        public decimal IncreaseOnWinPercent { get; init; }

        // A stop is ARMED iff its amount has a value. The "0 or blank means disabled" rule is applied at the
        // parse boundary (StrategyControlPanel.BuildConfig), so a value that reaches here is always > 0.
        public decimal? StopOnProfit { get; init; }
        public decimal? StopOnLoss { get; init; }
        public bool StopOnBlockMined { get; init; }

        // Insisting = reset the progression to base and keep going instead of stopping. One switch per stop:
        // without it, a strategy that only grows would be capped by whichever stop it happens to reach.
        public bool InsistAfterStopOnProfit { get; init; }
        public bool InsistAfterStopOnLoss { get; init; }
    }
}
