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
			if (config.IncreasePercent <= 0m)
				return config.BaseBet;

			bool shouldIncrease =
				(outcome.IsWin && config.IncreaseOnWin) ||
				(!outcome.IsWin && config.IncreaseOnLoss);

			if (!shouldIncrease)
				return config.BaseBet;

			var multiplier = 1m + (config.IncreasePercent / 100m);

			return currentBet * multiplier;
		}
	}
}
