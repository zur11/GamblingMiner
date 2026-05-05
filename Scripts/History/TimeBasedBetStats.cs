namespace Scripts.History
{
	public sealed class TimeBasedBetStats
	{
		public int TotalBets { get; set; }
		public int Wins { get; set; }
		public int Losses { get; set; }
		public decimal TotalWagered { get; set; }
		public decimal NetProfit { get; set; }
		public decimal WageredSinceLastDeposit { get; set; }
		public decimal NetProfitSinceLastDeposit { get; set; }
	}
}
