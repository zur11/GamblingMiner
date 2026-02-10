using System;

namespace Scripts.Dice
{
	public class DiceEngine
	{
		private readonly Random _rng = new Random();
		private const decimal RTP = 0.9902m;
		private decimal _balance;

		public decimal Balance => _balance;

		public DiceEngine(decimal initialBalance)
		{
			_balance = initialBalance;
		}

		public DiceResult Play(
			decimal bet,
			int chancePercent,
			bool isHigh
		)
		{
			if (bet <= 0m)
				throw new ArgumentException("Bet must be positive.");

			if (bet > _balance)
				throw new InvalidOperationException("Insufficient balance.");

			if (chancePercent < 1 || chancePercent > 95)
				throw new ArgumentOutOfRangeException(nameof(chancePercent));

			int roll = _rng.Next(0, 100); // 0–99

			GetWinningRange(
				chancePercent,
				isHigh,
				out int winMin,
				out int winMax
			);

			bool isWin = roll >= winMin && roll <= winMax;

			decimal multiplier = CalculateMultiplier(chancePercent);
			decimal profit = 0m;

			if (isWin)
			{
				profit = bet * multiplier - bet;
				_balance += profit;
			}
			else
			{
				_balance -= bet;
			}

			return new DiceResult
			{
				Roll = roll,
				IsWin = isWin,
				WinMin = winMin,
				WinMax = winMax,
				Bet = bet,
				Profit = profit,
				BalanceAfter = _balance,
				Chance = chancePercent,
				Multiplier = multiplier,
				IsHigh = isHigh
			};
		}

		public decimal GetPayoutMultiplier(int chancePercent)
		{
			if (chancePercent < 1 || chancePercent > 95)
				throw new ArgumentOutOfRangeException(nameof(chancePercent));

			return CalculateMultiplier(chancePercent);
		}

		private static void GetWinningRange(
			int chancePercent,
			bool isHigh,
			out int min,
			out int max
		)
		{
			if (isHigh)
			{
				min = 100 - chancePercent;
				max = 99;
			}
			else
			{
				min = 0;
				max = chancePercent - 1;
			}
		}

		private static decimal CalculateMultiplier(int chancePercent)
		{
			return Math.Round((100m * RTP) / chancePercent, 4);
		}
	}
}
