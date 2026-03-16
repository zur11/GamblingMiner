using System;
using Scripts.Betting;
using Scripts.Dice;
using Scripts.Finance;
using Scripts.Game;

namespace Scripts.Controllers
{
    public class BetController
    {
        private readonly BetService _betService;
        private readonly Wallet _wallet;
        private readonly IBettingStrategy _strategy;

        public bool IsRunning => _strategy.IsRunning;
        public decimal Balance => _wallet.Balance;
        public int RemainingBets { get; private set; }

        public BetController(
            BetService betService,
            Wallet wallet,
            IBettingStrategy strategy)
        {
            _betService = betService;
            _wallet = wallet;
            _strategy = strategy;
        }

        public void ConfigureStrategy(BettingStrategyConfig config)
        {
            _strategy.ApplyConfiguration(config);
        }

        public void StartSession(decimal startingBalance, int betCount)
        {
            RemainingBets = betCount;

            _strategy.StartSession(startingBalance);
        }

        public (DiceResult result, BetTransactionEvent betEvent) ExecuteBet(
            int chance,
            bool isHigh,
            Guid? sessionId)
        {
            decimal bet = _strategy.GetNextBet();

            if (bet <= 0m)
                throw new InvalidOperationException("Invalid bet amount.");

            if (bet > _wallet.Balance)
                throw new InvalidOperationException("Insufficient balance.");

            // Strategy resolution moved to session

            return _betService.ExecuteBet(bet, chance, isHigh, sessionId);
        }

        public decimal GetNextBet()
        {
            return _strategy.GetNextBet();
        }

        public decimal GetNextStrategyBet()
        {
            return _strategy.GetNextBet();
        }

        public (DiceResult result, BetTransactionEvent betEvent) ExecuteManualBet(
            decimal betAmount,
            int chance,
            bool isHigh
            )
        {
            if (betAmount <= 0m)
                throw new InvalidOperationException("Invalid bet amount.");

            if (betAmount > _wallet.Balance)
                throw new InvalidOperationException("Insufficient balance.");

            return _betService.ExecuteBet(
                betAmount,
                chance,
                isHigh,
                null
            );
        }

        public void ResolveManualBet(
            decimal betAmount,
            decimal profit,
            bool isWin
            )
        {
            var outcome = new BetOutcome(
                betAmount,
                profit,
                isWin
            );

            _strategy.OnBetResolved(outcome, _wallet.Balance);
        }

        public void NotifyBetResult(
            decimal betAmount,
            decimal profit,
            bool isWin
            )
        {
            var outcome = new BetOutcome(
                betAmount,
                profit,
                isWin
            );

            _strategy.OnBetResolved(outcome, _wallet.Balance);

            if (RemainingBets > 0)
                RemainingBets--;
        }
    }
}