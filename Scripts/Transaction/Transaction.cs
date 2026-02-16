using System;

namespace Scripts.Transaction
{
	public enum TransactionType
	{
		Deposit,
		Withdrawal
	}

	public class Transaction
	{
		public TransactionType Type { get; }
		public decimal Amount { get; }
		public DateTime Timestamp { get; }

		public Transaction(TransactionType type, decimal amount)
		{
			if (amount <= 0m)
				throw new ArgumentException("Transaction amount must be greater than zero.");

			Type = type;
			Amount = amount;
			Timestamp = DateTime.UtcNow;
		}
	}
}
