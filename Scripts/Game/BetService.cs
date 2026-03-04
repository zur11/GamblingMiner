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

        public BetService(DiceEngine engine, Wallet wallet, TransactionSource source)
        {
            _engine = engine;
            _wallet = wallet;
            _source = source;
        }

        public (DiceResult Result, BetTransactionEvent Event) ExecuteBet(
            decimal bet,
            int chance,
            bool isHigh,
            Guid? sessionId
        )
        {
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

            if (result.IsWin)
            {
                payout = bet + result.Profit;

                _wallet.ApplyTransaction(
                    new Transaction(TransactionType.Deposit, TransactionSource.Bet, sessionId, payout)
                );
            }

            var transactionEvent = new BetTransactionEvent(
                BetAmount: bet,
                Profit: result.Profit,
                BalanceAfter: _wallet.Balance,
                IsWin: result.IsWin,
                Roll: result.Roll,
                Chance: (int)result.Chance,
                Multiplier: multiplier,
                IsHigh: result.IsHigh,
                Timestamp: DateTime.UtcNow
            );

            return (result, transactionEvent);
        }
    }
}
