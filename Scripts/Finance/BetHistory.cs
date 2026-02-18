using System.Collections.Generic;

namespace Scripts.Finance
{
    public sealed class BetHistory
    {
        private readonly List<BetTransactionEvent> _events = new();

        public IReadOnlyList<BetTransactionEvent> Events => _events;

        public void Add(BetTransactionEvent betEvent)
        {
            _events.Add(betEvent);
        }
    }
}
