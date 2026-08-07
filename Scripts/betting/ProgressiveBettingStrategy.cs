using System;
using Godot;

namespace Scripts.Betting
{
	public class ProgressiveBettingStrategy : IBettingStrategy
	{
		public decimal CalculateNextBet(
			decimal currentBet,
			BetOutcome outcome,
			BettingStrategyConfig config)
		{
			// The outcome selects its own percent; a percent of 0 means "this outcome does not grow the bet",
			// so the progression resets to base — the same shape the old single-percent + which-outcome pair
			// produced, now expressible on both outcomes at once.
			decimal increasePercent = outcome.IsWin
				? config.IncreaseOnWinPercent
				: config.IncreaseOnLossPercent;

			if (increasePercent <= 0m)
				return config.BaseBet;

			var multiplier = 1m + (increasePercent / 100m);

			return currentBet * multiplier;
		}
	}
}
