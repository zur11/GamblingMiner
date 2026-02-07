namespace Scripts.Dice
{
	public class DiceResult
	{
		public int Roll { get; init; }
		public bool IsWin { get; init; }

		public int WinMin { get; init; }
		public int WinMax { get; init; }

		public decimal Bet { get; init; }
		public decimal Profit { get; init; }
		public decimal BalanceAfter { get; init; }

		public decimal Chance { get; init; }
		public decimal Multiplier { get; init; }
		public bool IsHigh { get; init; }
	}
}
