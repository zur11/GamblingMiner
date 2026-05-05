using System;

namespace Scripts.History
{
	public sealed class TimeBucketSummary
	{
		public DateTime BucketStartUtc { get; set; }
		public TimeBucketType BucketType { get; set; }
		public int TotalBets { get; set; }
		public int Wins { get; set; }
		public int Losses { get; set; }
		public decimal NetAmountSum { get; set; }
	}
}
