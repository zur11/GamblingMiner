using System;

namespace Scripts.History
{
	public sealed class DepositRecord
	{
		// Mini-plan 13 B — the same per-world sequence the bets use, assigned by BetHistoryRepository. Deposits
		// share it so one journal has one id space; nothing reads a deposit's id today.
		public long Id { get; set; }
		public DateTime TimestampUtc { get; set; }
		public decimal Amount { get; set; }
		public decimal BalanceAfter { get; set; }
	}
}
