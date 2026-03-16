using System;
using Scripts.Finance;

namespace Scripts.Controllers
{
    public class WalletController
    {
        private readonly Wallet _wallet;

        public event Action<decimal> Deposited;
        public event Action<decimal> Withdrawn;

        public WalletController(Wallet wallet)
        {
            _wallet = wallet;
        }

        public decimal Balance => _wallet.Balance;

        public void Deposit(decimal amount)
        {
            var transaction = new Transaction(
                TransactionType.Deposit,
                TransactionSource.External,
                null,
                amount
            );

            _wallet.ApplyTransaction(transaction);

            Deposited?.Invoke(amount);
        }

        public void Withdraw(decimal amount)
        {
            if (amount <= 0m)
                throw new InvalidOperationException("Invalid withdrawal.");

            var transaction = new Transaction(
                TransactionType.Withdrawal,
                TransactionSource.External,
                null,
                amount
            );

            _wallet.ApplyTransaction(transaction);

            Withdrawn?.Invoke(amount);
        }

        public bool IsBankrupt()
        {
            return _wallet.Balance <= 0m;
        }
    }
}