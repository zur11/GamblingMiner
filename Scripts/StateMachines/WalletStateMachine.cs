using System;
using Godot;
using System.Collections.Generic;

namespace Scripts.StateMachines
{
	public enum WalletState
	{
		Normal,
		Bankrupt
	}

	public enum WalletEvent
	{
		BalanceZero,
		BalanceRestored
	}

	public class WalletStateMachine
	{
		private WalletState _state = WalletState.Normal;

		public WalletState State => _state;

		public event Action<WalletState> StateChanged;

		public void Fire(WalletEvent ev)
		{
			var oldState = _state;

			switch (_state)
			{
				case WalletState.Normal:

					if (ev == WalletEvent.BalanceZero)
						_state = WalletState.Bankrupt;

					break;

				case WalletState.Bankrupt:

					if (ev == WalletEvent.BalanceRestored)
						_state = WalletState.Normal;

					break;
			}

			if (oldState != _state)
				StateChanged?.Invoke(_state);
		}
	}
}
