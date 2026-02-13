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
			if (initialBalance < 0m)
				throw new ArgumentException("Initial balance cannot be negative.");

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

			// --- Roll ---
			int roll = _rng.Next(0, 100); // 0–99

			// --- Winning range ---
			GetWinningRange(
				chancePercent,
				isHigh,
				out int winMin,
				out int winMax
			);

			bool isWin = roll >= winMin && roll <= winMax;

			// --- Multiplier ---
			decimal multiplier = CalculateMultiplier(chancePercent);

			// --- Financial calculation ---
			decimal profit;
			decimal balanceAfter;

			if (isWin)
			{
				profit = bet * multiplier - bet;
				balanceAfter = _balance + profit;
			}
			else
			{
				profit = -bet;
				balanceAfter = _balance - bet;
			}

			// Commit balance change AFTER calculation
			_balance = balanceAfter;

			// --- Construct immutable result ---
			return new DiceResult(
				roll,
				isWin,
				bet,
				chancePercent,
				multiplier,
				isHigh,
				profit,
				balanceAfter,
				winMin,
				winMax
			);
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
