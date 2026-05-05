using System;

namespace Scripts.History
{
	public sealed class BetRecord
	{
		public string Id { get; set; } = Guid.NewGuid().ToString("N");
		public string GameId { get; set; } = string.Empty;
		public DateTime TimestampUtc { get; set; }
		public BetOutcome Outcome { get; set; }
		public decimal BetAmount { get; set; }
		public decimal NetAmount { get; set; }
		public decimal BalanceAfter { get; set; }
	}
}
