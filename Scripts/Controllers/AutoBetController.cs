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
		private readonly BetController _betController;

		public bool IsRunning => _betController.IsRunning;
		public int RemainingBets => _betController.RemainingBets;

		public AutoBetController(BetController betController)
		{
			_betController = betController;
		}

		public void Configure(BettingStrategyConfig config, int betCount)
		{
			_betController.ConfigureStrategy(config);
		}

		public void Start(decimal balance, int betCount)
		{
			_betController.StartSession(balance, betCount);
		}

		public void Stop(IBettingStrategy.StopReason reason)
		{
			_betController.Stop(); // ← necesitas exponer esto
		}

		public (DiceResult, BetTransactionEvent) ExecuteNextBet(
			int chance,
			bool isHigh)
		{
			var (result, betEvent) =
				_betController.ExecuteBet(
					chance,
					isHigh,
					null
				);

			_betController.NotifyBetResult(
				betEvent.BetAmount,
				betEvent.Profit,
				result.IsWin
			);

			return (result, betEvent);
		}

		public decimal GetNextBet()
		{
			return _betController.GetNextBet();
		}
	}
}
