using System;
using Scripts.Finance;

namespace Scripts.Game
{
    public interface IBetEventSource
    {
        event Action<string, BetTransactionEvent> BetExecuted;
        string GameId { get; }
    }
}
