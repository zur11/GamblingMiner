using System.Collections.Generic;

namespace Scripts.History
{
	public sealed class BetHistorySnapshot
	{
		public List<BetRecord> Records { get; set; } = new();
		public List<DepositRecord> Deposits { get; set; } = new();
	}
}
