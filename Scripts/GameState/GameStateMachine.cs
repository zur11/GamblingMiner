using System;
using System.Collections.Generic;

namespace Scripts.GameState
{
    public enum BetState
    {
        Idle,
        Progression,
        Bankrupt
    }

    public enum GameEvent
    {
        BetPressed,
        Win,
        Loss,
        ProgressionAborted,
        BankruptDetected,
        ManualReset,
        BalanceRefilled
    }

    public class GameStateMachine
    {
        private BetState _currentState;

        private readonly Dictionary<(BetState, GameEvent), BetState> _transitions;

        public BetState CurrentState => _currentState;

        public event Action<BetState> StateEntered;
        public event Action<BetState> StateExited;
        public event Action<BetState, GameEvent, BetState> OnTransition;

        public GameStateMachine()
        {
            _currentState = BetState.Idle;

            _transitions = new()
            {
                { (BetState.Idle, GameEvent.BetPressed), BetState.Progression },

                { (BetState.Progression, GameEvent.Win), BetState.Idle },
                { (BetState.Progression, GameEvent.Loss), BetState.Progression },
                { (BetState.Progression, GameEvent.ProgressionAborted), BetState.Idle },

                { (BetState.Progression, GameEvent.ManualReset), BetState.Idle },
                { (BetState.Progression, GameEvent.BankruptDetected), BetState.Bankrupt },
                { (BetState.Idle, GameEvent.BankruptDetected), BetState.Bankrupt },

                { (BetState.Bankrupt, GameEvent.BalanceRefilled), BetState.Idle }
            };
        }

        public void Fire(GameEvent trigger)
        {
            var key = (_currentState, trigger);

            if (_transitions.TryGetValue(key, out var newState))
            {
                StateExited?.Invoke(_currentState);
                OnTransition?.Invoke(_currentState, trigger, newState);

                _currentState = newState;

                StateEntered?.Invoke(newState);

            }
            else
            {
                Console.WriteLine($"Invalid transition: {_currentState} + {trigger}");
            }
        }
    }
}
