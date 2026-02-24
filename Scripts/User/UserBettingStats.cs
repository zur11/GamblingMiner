using System;
using System.Collections.Generic;
using Scripts.Finance;

namespace Scripts.User
{
	public class UserBettingStats
	{
		private readonly List<UserBetRecord> _allBets = new();

		// --- Exposición controlada ---
		public IReadOnlyCollection<UserBetRecord> AllBets => _allBets.AsReadOnly();

		// --- Métricas acumuladas ---
		public int TotalBets { get; private set; }
		public decimal TotalAmountWagered { get; private set; }
		public decimal TotalProfit { get; private set; }

		// --- Métricas de sesión (desde último depósito) ---
		public int BetsSinceLastDeposit { get; private set; }
		public decimal AmountWageredSinceDeposit { get; private set; }
		public decimal ProfitSinceDeposit { get; private set; }

		// --- Métricas derivadas ---
		public int TotalWins { get; private set; }
		public int TotalLosses => TotalBets - TotalWins;

		public decimal CurrentDrawdown { get; private set; }
		public decimal MaxDrawdown { get; private set; }

		private decimal _peakProfit = 0m;

		// ================================
		// ========= AGGREGATE API ========
		// ================================

		public void RegisterBet(string gameId, BetTransactionEvent bet)
		{
			ValidateBet(bet);

			var record = CreateRecord(gameId, bet);

			ApplyRecord(record);
		}

		public void RegisterDeposit()
		{
			ResetSessionMetrics();
		}

		// ================================
		// ========= DERIVED DATA =========
		// ================================

		public decimal GetRoi()
		{
			if (TotalAmountWagered == 0m)
				return 0m;

			return TotalProfit / TotalAmountWagered;
		}

		public decimal GetSessionRoi()
		{
			if (AmountWageredSinceDeposit == 0m)
				return 0m;

			return ProfitSinceDeposit / AmountWageredSinceDeposit;
		}

		public decimal GetWinRate()
		{
			if (TotalBets == 0)
				return 0m;

			return (decimal)TotalWins / TotalBets;
		}

		public bool IsInDrawdown => CurrentDrawdown < 0m;

		// ================================
		// ========= INTERNAL LOGIC =======
		// ================================

		private void ValidateBet(BetTransactionEvent bet)
		{
			if (bet == null)
				throw new ArgumentNullException(nameof(bet));

			if (bet.BetAmount <= 0m)
				throw new InvalidOperationException("Bet amount must be positive.");
		}

		private UserBetRecord CreateRecord(string gameId, BetTransactionEvent bet)
		{
			return new UserBetRecord(
				gameId,
				bet.Timestamp,
				bet.BetAmount,
				bet.Profit,
				bet.IsWin
			);
		}

		private void ApplyRecord(UserBetRecord record)
		{
			_allBets.Add(record);

			UpdateTotals(record);
			UpdateSessionMetrics(record);
			UpdateWinLoss(record);
			UpdateDrawdown(record);
		}

		private void UpdateTotals(UserBetRecord record)
		{
			TotalBets++;
			TotalAmountWagered += record.BetAmount;
			TotalProfit += record.Profit;
		}

		private void UpdateSessionMetrics(UserBetRecord record)
		{
			BetsSinceLastDeposit++;
			AmountWageredSinceDeposit += record.BetAmount;
			ProfitSinceDeposit += record.Profit;
		}

		private void UpdateWinLoss(UserBetRecord record)
		{
			if (record.IsWin)
				TotalWins++;
		}

		private void UpdateDrawdown(UserBetRecord record)
		{
			if (TotalProfit > _peakProfit)
				_peakProfit = TotalProfit;

			CurrentDrawdown = TotalProfit - _peakProfit;

			if (CurrentDrawdown < MaxDrawdown)
				MaxDrawdown = CurrentDrawdown;
		}

		private void ResetSessionMetrics()
		{
			BetsSinceLastDeposit = 0;
			AmountWageredSinceDeposit = 0m;
			ProfitSinceDeposit = 0m;
		}
	}
}
