using Scripts.Dice;
using Scripts.Finance;

namespace Scripts.Game
{
    public sealed class BetService
    {
        private readonly DiceEngine _engine;
        private readonly Wallet _wallet;

        public BetService(DiceEngine engine, Wallet wallet)
        {
            _engine = engine;
            _wallet = wallet;
        }

        public DiceResult ExecuteBet(
            decimal bet,
            int chance,
            bool isHigh
        )
        {
            // 1. Validar fondos
            if (bet > _wallet.Balance)
                throw new System.InvalidOperationException("Insufficient balance.");

            // 2. Retirar apuesta
            _wallet.ApplyTransaction(
                new Transaction(TransactionType.Withdrawal, bet)
            );

            // 3. Ejecutar motor
            DiceResult result = _engine.Play(bet, chance, isHigh);

            // 4. Aplicar profit
            if (result.Profit > 0m)
            {
                _wallet.ApplyTransaction(
                    new Transaction(TransactionType.Deposit, result.Profit + bet)
                );
            }

            return result;
        }
    }
}
