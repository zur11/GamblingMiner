using System;
using Scripts.Betting;
using Scripts.Dice;
using Scripts.Finance;
using Scripts.Game;
using Scripts.Sessions;

namespace Scripts.Controllers
{
	public class AutoBetController
	{
		private readonly BetSession _session;

		public AutoBetController(BetSession session)
		{
			_session = session;
		}

		public bool IsRunning => _session.IsRunning;

		public int RemainingBets => _session.RemainingBets;

		public bool IsInfinite => _session.IsInfinite;

		public event Action<IBettingStrategy.StopReason?> OnStopped
		{
			add => _session.OnStopped += value;
			remove => _session.OnStopped -= value;
		}

		public void Start(decimal balance, int betCount)
		{
			_session.Start(balance, betCount);
		}

		public void Stop(IBettingStrategy.StopReason reason)
		{
			_session.Stop(reason);
		}

		public (DiceResult, BetTransactionEvent, decimal nextBet) ExecuteNextBet(int chance, bool isHigh)
		{
			return _session.ExecuteNext(chance, isHigh);
		}

		public decimal GetNextBet()
		{
			return _session.GetNextBet();
		}
	}
}
