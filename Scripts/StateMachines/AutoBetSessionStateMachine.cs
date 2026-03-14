using System;
using Godot;
using System.Collections.Generic;

namespace Scripts.StateMachines
{
	public enum AutoBetSessionState
	{
		Stopped,
		Running,
		Stopping
	}

	public enum AutoBetSessionEvent
	{
		Start,
		StopRequested,
		Finished
	}

	public class AutoBetSessionStateMachine
	{
		private AutoBetSessionState _state = AutoBetSessionState.Stopped;

		public AutoBetSessionState State => _state;

		public event Action<AutoBetSessionState> StateChanged;

		public void Fire(AutoBetSessionEvent ev)
		{
			var oldState = _state;

			switch (_state)
			{
				case AutoBetSessionState.Stopped:

					if (ev == AutoBetSessionEvent.Start)
						_state = AutoBetSessionState.Running;

					break;

				case AutoBetSessionState.Running:

					if (ev == AutoBetSessionEvent.StopRequested)
						_state = AutoBetSessionState.Stopping;

					if (ev == AutoBetSessionEvent.Finished)
						_state = AutoBetSessionState.Stopped;

					break;

				case AutoBetSessionState.Stopping:

					if (ev == AutoBetSessionEvent.Finished)
						_state = AutoBetSessionState.Stopped;

					break;
			}

			if (oldState != _state)
				StateChanged?.Invoke(_state);
		}
	}
}
