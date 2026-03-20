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

        public decimal Balance => _wallet.Balance;

        public BetController(
            BetService betService,
            Wallet wallet,
            IBettingStrategy strategy)
        {
            _betService = betService;
            _wallet = wallet;
            _strategy = strategy;
        }

        public decimal CalculateNextBet(
            decimal currentBet,
            BetOutcome outcome,
            BettingStrategyConfig config)
        {
            if (_strategy is ProgressiveBettingStrategy progressive)
                return progressive.CalculateNextBet(currentBet, outcome, config);

            throw new InvalidOperationException("Strategy does not support CalculateNextBet");
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
    }
}