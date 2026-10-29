namespace Scripts.Betting
{
	public interface IBettingStrategy
	{
		public enum StopReason
		{
			StopOnProfit,
			StopOnLoss,
			StopOnBlockMined,
			InsufficientBalance,
			ManualStop,
			CounterCountReached,
			InvalidBetAmount,
			StopOnSigilSetCompleted   // appended, so the values above keep their numbers
		}

		decimal CalculateNextBet(
			decimal currentBet,
			BetOutcome outcome,
			BettingStrategyConfig config);
	}
}
