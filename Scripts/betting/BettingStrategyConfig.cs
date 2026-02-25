namespace Scripts.Betting
{
    public class BettingStrategyConfig
    {
        public decimal BaseBet { get; init; }

        public decimal IncreasePercent { get; init; }

        public bool IncreaseOnLoss { get; init; }
        public bool IncreaseOnWin { get; init; }

        public decimal? StopOnProfit { get; init; }
        public decimal? StopOnLoss { get; init; }
    }
}
