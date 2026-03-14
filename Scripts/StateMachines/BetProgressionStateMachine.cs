using System;
using Godot;
using System.Collections.Generic;

namespace Scripts.StateMachines
{
	public enum BetProgressionState
	{
		Idle,
		ProgressionOnLoss,
		ProgressionOnWin
	}

	public enum BetProgressionEvent
	{
		StartLossProgression,
		StartWinProgression,
		Win,
		Loss,
		Abort,
        Reset
	}

	public class BetProgressionStateMachine
	{
		private BetProgressionState _state = BetProgressionState.Idle;

		public BetProgressionState State => _state;

		public event Action<BetProgressionState> StateChanged;

		public void Fire(BetProgressionEvent ev)
		{
			var oldState = _state;

			switch (_state)
			{
				case BetProgressionState.Idle:

					if (ev == BetProgressionEvent.StartLossProgression)
						_state = BetProgressionState.ProgressionOnLoss;

					if (ev == BetProgressionEvent.StartWinProgression)
						_state = BetProgressionState.ProgressionOnWin;

                    if (ev == BetProgressionEvent.Abort)
                        _state = BetProgressionState.Idle;

                    break;

				case BetProgressionState.ProgressionOnLoss:

					if (ev == BetProgressionEvent.Win)
						_state = BetProgressionState.Idle;

					if (ev == BetProgressionEvent.Loss)
						_state = BetProgressionState.ProgressionOnLoss;

					if (ev == BetProgressionEvent.Abort)
						_state = BetProgressionState.Idle;

					break;

				case BetProgressionState.ProgressionOnWin:

					if (ev == BetProgressionEvent.Loss)
						_state = BetProgressionState.Idle;

					if (ev == BetProgressionEvent.Win)
						_state = BetProgressionState.ProgressionOnWin;

					if (ev == BetProgressionEvent.Abort)
						_state = BetProgressionState.Idle;

					break;
			}

			if (oldState != _state)
				StateChanged?.Invoke(_state);
		}
	}
}
