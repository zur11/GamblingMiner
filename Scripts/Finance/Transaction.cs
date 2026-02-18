namespace Scripts.Finance
{
    public enum TransactionType
    {
        Deposit,
        Withdrawal
    }

    public sealed record Transaction(
        TransactionType Type,
        decimal Amount
    );
}
