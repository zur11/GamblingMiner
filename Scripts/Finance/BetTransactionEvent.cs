using System;
using Scripts.Dice;

namespace Scripts.Finance
{
    public sealed record BetTransactionEvent(
        decimal BetAmount,
        decimal Profit,
        decimal BalanceAfter,
        bool IsWin,
        int Roll,
        int Chance,
        decimal Multiplier,
        bool IsHigh,
        DateTime Timestamp
    );
}
