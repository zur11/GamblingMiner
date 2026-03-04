using System;

namespace Scripts.Finance
{
    public enum TransactionType
    {
        Deposit,
        Withdrawal
    }

    public enum TransactionSource
    {
        Bet,
        External,
        OtherGame
    }

    public sealed record Transaction(
    TransactionType Type,
    TransactionSource Source,
    Guid? SessionId,
    decimal Amount
    );
}
