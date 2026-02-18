using System;
using Scripts.Dice;
using Scripts.Finance;

namespace Scripts.Game
{
    public sealed class BetService
    {
        private readonly DiceEngine _engine;
        private readonly Wallet _wallet;

        public BetService(DiceEngine engine, Wallet wallet)
        {
            _engine = engine;
            _wallet = wallet;
        }

        public (DiceResult Result, BetTransactionEvent Event) ExecuteBet(
            decimal bet,
            int chance,
            bool isHigh
        )
        {
            if (bet > _wallet.Balance)
                throw new InvalidOperationException("Insufficient balance.");

            // Withdraw bet
            _wallet.ApplyTransaction(
                new Transaction(TransactionType.Withdrawal, bet)
            );

            // Execute engine
            DiceResult result = _engine.Play(bet, chance, isHigh);

            decimal payout = 0m;

            if (result.IsWin)
            {
                payout = bet + result.Profit;

                _wallet.ApplyTransaction(
                    new Transaction(TransactionType.Deposit, payout)
                );
            }

            var transactionEvent = new BetTransactionEvent(
                BetAmount: bet,
                Profit: result.Profit,
                BalanceAfter: _wallet.Balance,
                IsWin: result.IsWin,
                Roll: result.Roll,
                Chance: (int)result.Chance,
                IsHigh: result.IsHigh,
                Timestamp: DateTime.UtcNow
            );

            return (result, transactionEvent);
        }
    }
}
