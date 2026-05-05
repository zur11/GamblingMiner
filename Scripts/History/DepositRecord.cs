using System;

namespace Scripts.History
{
	public sealed class DepositRecord
	{
		public string Id { get; set; } = Guid.NewGuid().ToString("N");
		public DateTime TimestampUtc { get; set; }
		public decimal Amount { get; set; }
		public decimal BalanceAfter { get; set; }
	}
}
