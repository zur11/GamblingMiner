namespace Scripts.Betting
{
    public class SessionCapitalTracker
    {
        private decimal _differenceWithBalance;

        public void OnBalanceDeltaChanged(decimal amount)
        {
            _differenceWithBalance += amount;
        }

        public decimal GetDifferenceWithBalance()
        {
            return _differenceWithBalance;
        }

        public void Reset()
        {
            _differenceWithBalance = 0m;
        }
    }
}