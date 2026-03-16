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
        private readonly BetSession _session;

        public bool IsRunning => _session.IsRunning;

        public int RemainingBets => _session.RemainingBets;

        public AutoBetController(
            BetController betController,
            IBettingStrategy strategy)
        {
            _betController = betController;
            _session = new BetSession(strategy);
        }

        public void Configure(BettingStrategyConfig config, int betCount)
        {
            _betController.ConfigureStrategy(config);
        }

        public void Start(decimal balance, int betCount)
        {
            _session.Start(balance, betCount);
        }

        public void Stop(IBettingStrategy.StopReason reason)
        {
            _session.Stop(reason);
        }

        public (DiceResult, BetTransactionEvent) ExecuteNextBet(
            int chance,
            bool isHigh)
        {
            decimal bet = _session.GetNextBet();

            var (result, betEvent) =
                _betController.ExecuteManualBet(
                    bet,
                    chance,
                    isHigh);

            _session.NotifyResult(
                betEvent.BetAmount,
                betEvent.Profit,
                result.IsWin,
                _betController.Balance
            );

            return (result, betEvent);
        }

        public decimal GetNextBet()
        {
            return _session.GetNextBet();
        }
    }
}