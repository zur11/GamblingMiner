namespace Scripts.Dice
{
	public sealed class DiceResult
	{
		public int Roll { get; }
		public bool IsWin { get; }

		public decimal Bet { get; }
		public decimal Chance { get; }
		public decimal Multiplier { get; }
		public bool IsHigh { get; }

		public decimal Profit { get; }
		public decimal BalanceAfter { get; }

		public int WinMin { get; }
		public int WinMax { get; }

		public DiceResult(
			int roll,
			bool isWin,
			decimal bet,
			decimal chance,
			decimal multiplier,
			bool isHigh,
			decimal profit,
			decimal balanceAfter,
			int winMin,
			int winMax)
		{
			Roll = roll;
			IsWin = isWin;
			Bet = bet;
			Chance = chance;
			Multiplier = multiplier;
			IsHigh = isHigh;
			Profit = profit;
			BalanceAfter = balanceAfter;
			WinMin = winMin;
			WinMax = winMax;
		}
	}
}
