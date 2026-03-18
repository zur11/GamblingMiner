using System;
using Godot;
using Scripts.Betting;
using Scripts.Finance;
using Scripts.Controllers;
using Scripts.Dice;

namespace Scripts.Sessions
{
    public class BetSession
    {
        public event Action<IBettingStrategy.StopReason?> OnStopped;

        private readonly BetController _betController;

        public bool IsRunning { get; private set; }

        public int RemainingBets { get; private set; }

        public bool IsInfinite => RemainingBets == int.MaxValue;

        public BetSession(BetController betController)
        {
            _betController = betController;
        }

        public void Start(decimal balance, int betCount)
        {
            RemainingBets = betCount <= 0 ? int.MaxValue : betCount;

            _betController.StartSession(balance, betCount);

            IsRunning = true;
        }

        public void Stop(IBettingStrategy.StopReason reason)
        {
            IsRunning = false;
            _betController.Stop();
            OnStopped?.Invoke(reason);
        }

        public (DiceResult, BetTransactionEvent, decimal nextBet) ExecuteNext(
            int chance,
            bool isHigh)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Session not running");

            var (result, betEvent) =
                _betController.ExecuteBet(chance, isHigh, null);

            _betController.NotifyBetResult(
                betEvent.BetAmount,
                betEvent.Profit,
                result.IsWin
            );

            // 🔥 CLAVE: obtener siguiente bet ANTES del Stop
            decimal nextBet = _betController.GetNextBet();

            if (RemainingBets != int.MaxValue)
            {
                RemainingBets--;

                if (RemainingBets <= 0)
                {
                    Stop(IBettingStrategy.StopReason.CounterCountReached);
                    return (result, betEvent, nextBet);
                }
            }

            if (!_betController.IsRunning)
            {
                Stop(_betController.LastStopReason
                    ?? IBettingStrategy.StopReason.ManualStop);
            }

            return (result, betEvent, nextBet);
        }

        public decimal GetNextBet()
        {
            return _betController.GetNextBet();
        }
    }
}
