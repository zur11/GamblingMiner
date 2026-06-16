using System;

namespace Scripts.Betting
{
	public sealed class SavedBettingStrategy
	{
		public string Name { get; set; } = string.Empty;
		public string GameId { get; set; } = string.Empty;
		public DateTime SavedAtUtc { get; set; }
		public BettingStrategyConfig Config { get; set; } = new();
		public int NumberOfBets { get; set; }
		public bool AutoRechargeEnabled { get; set; }
		public int WinningChance { get; set; }
		public bool BetHigh { get; set; }
		public int BetsPerSecond { get; set; } = 1;
	}
}
