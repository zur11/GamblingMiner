using System;
using Scripts.Dice;
using Scripts.Finance;

namespace Scripts.Game
{
	public sealed class BetService
	{
		private readonly DiceEngine _engine;
		private readonly Wallet _wallet;
		private readonly TransactionSource _source;
		private readonly Func<DateTime> _utcNowProvider;
		private decimal _pendingFractionalProfit;

		public BetService(DiceEngine engine, Wallet wallet, TransactionSource source, Func<DateTime> utcNowProvider = null)
		{
			_engine = engine;
			_wallet = wallet;
			_source = source;
			_utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
		}

		public (DiceResult Result, BetTransactionEvent Event) ExecuteBet(
			decimal bet,
			int chance,
			bool isHigh,
			Guid? sessionId,
			DateTime? timestampUtc = null
		)
		{
			bet = Money.Normalize(bet);

			if (bet > _wallet.Balance)
				throw new InvalidOperationException("Insufficient balance.");

			// Withdraw bet
			_wallet.ApplyTransaction(
				new Transaction(TransactionType.Withdrawal, TransactionSource.Bet, sessionId, bet)
			);
  
			// Execute engine
			DiceResult result = _engine.Play(bet, chance, isHigh);

			decimal payout = 0m;
			decimal multiplier = result.Multiplier;
			decimal creditedProfit = result.Profit;

			if (result.IsWin)
			{
				decimal combinedProfit = result.Profit + _pendingFractionalProfit;
				creditedProfit = Money.Normalize(combinedProfit);
				_pendingFractionalProfit = combinedProfit - creditedProfit;

				payout = bet + creditedProfit;

				_wallet.ApplyTransaction(
					new Transaction(TransactionType.Deposit, TransactionSource.Bet, sessionId, payout)
				);
			}

			var transactionEvent = new BetTransactionEvent(
				BetAmount: bet,
				Profit: result.Profit,
				CreditedProfit: creditedProfit,
				BalanceAfter: _wallet.Balance,
				IsWin: result.IsWin,
				Roll: result.Roll,
				Chance: (int)result.Chance,
				Multiplier: multiplier,
				IsHigh: result.IsHigh,
				Timestamp: timestampUtc ?? _utcNowProvider()
			);

			return (result, transactionEvent);
		}
	}
}
