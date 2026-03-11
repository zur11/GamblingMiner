using System;
using Godot;
using System.Collections.Generic;

namespace Scripts.GameState
{
    public enum BetState
    {
        Idle,
        ProgressionOnLoss,
        ProgressionOnWin,
        Bankrupt
    }

    public enum GameEvent
    {
        StartLossProgression,
        StartWinProgression,

        Win,
        Loss,

        ProgressionAborted,
        BankruptDetected,
        ManualReset,
        BalanceRefilled,
        CounterCountReached,
        AutoBetSessionStarted
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
                { (BetState.Idle, GameEvent.StartLossProgression), BetState.ProgressionOnLoss },
                { (BetState.Idle, GameEvent.StartWinProgression), BetState.ProgressionOnWin },

                { (BetState.ProgressionOnLoss, GameEvent.Win), BetState.Idle },
                { (BetState.ProgressionOnLoss, GameEvent.Loss), BetState.ProgressionOnLoss },

                { (BetState.ProgressionOnWin, GameEvent.Win), BetState.ProgressionOnWin },
                { (BetState.ProgressionOnWin, GameEvent.Loss), BetState.Idle },

                { (BetState.ProgressionOnLoss, GameEvent.ProgressionAborted), BetState.Idle },
                { (BetState.ProgressionOnWin, GameEvent.ProgressionAborted), BetState.Idle },

                { (BetState.ProgressionOnLoss, GameEvent.ManualReset), BetState.Idle },
                { (BetState.ProgressionOnWin, GameEvent.ManualReset), BetState.Idle },

                { (BetState.ProgressionOnLoss, GameEvent.CounterCountReached), BetState.Idle },
                { (BetState.ProgressionOnWin, GameEvent.CounterCountReached), BetState.Idle },
                
                { (BetState.ProgressionOnLoss, GameEvent.AutoBetSessionStarted), BetState.Idle },
                { (BetState.ProgressionOnWin, GameEvent.AutoBetSessionStarted), BetState.Idle },

                { (BetState.ProgressionOnLoss, GameEvent.BankruptDetected), BetState.Bankrupt },
                { (BetState.ProgressionOnWin, GameEvent.BankruptDetected), BetState.Bankrupt },
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
                GD.Print($"Invalid transition: {_currentState} + {trigger}");
            }
        }
    }
}
