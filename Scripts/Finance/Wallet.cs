using System;

namespace Scripts.Finance
{
    public sealed class Wallet
    {
        private decimal _balance;

        public decimal Balance => _balance;

        public Wallet(decimal initialBalance)
        {
            if (initialBalance < 0m)
                throw new ArgumentException("Initial balance cannot be negative.");

            _balance = initialBalance;
        }

        public void ApplyTransaction(Transaction transaction)
        {
            if (transaction is null)
                throw new ArgumentNullException(nameof(transaction));

            switch (transaction.Type)
            {
                case TransactionType.Deposit:
                    if (transaction.Amount <= 0m)
                        throw new ArgumentException("Deposit must be positive.");

                    _balance += transaction.Amount;
                    break;

                case TransactionType.Withdrawal:
                    if (transaction.Amount <= 0m)
                        throw new ArgumentException("Withdrawal must be positive.");

                    if (transaction.Amount > _balance)
                        throw new InvalidOperationException("Insufficient balance.");

                    _balance -= transaction.Amount;
                    break;

                default:
                    throw new InvalidOperationException("Unknown transaction type.");
            }
        }
    }
}
