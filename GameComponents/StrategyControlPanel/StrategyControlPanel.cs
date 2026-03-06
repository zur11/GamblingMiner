using Godot;
using System;
using System.Globalization;

namespace GameComponents.StrategyControlPanel
{
	public partial class StrategyControlPanel : Control
	{
		// --- Eventos ---
		public event Action BetOnceBtnPressed;
		public event Action<bool> AutoBetToggled;
		public event Action<string> BetAmountInputChanged;

		// --- Flags ---
		private bool _internalUpdate = false;

		// --- Nodos UI ---
		[Export]
		private Button _betOnceBtn;
		[Export]
		private Button _autoBetToggle;
		[Export]
		private Button _increaseOnLossWinToggle;
		[Export]
		private LineEdit _betAmountInput;
		[Export]
		private LineEdit _increasePercentageInput;

		// --- Propiedades API ---
		public decimal BetAmount
		{
			get
			{
				string text = _betAmountInput.Text.Trim().Replace(',', '.');
				if (!decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
					return 0m;
				return value;
			}
		}

		public decimal IncreasePercent
		{
			get
			{
				string text = _increasePercentageInput.Text.Trim();
				if (!decimal.TryParse(text, out var value))
					return 0m;
				return value;
			}
		}


		public bool IncreasingOnWin
		{
			get
			{
				return _increaseOnLossWinToggle.ButtonPressed;
			}
		}

		public void SetBetAmount(decimal amount)
		{
			_internalUpdate = true;

			_betAmountInput.Text =
				amount.ToString("F8", CultureInfo.InvariantCulture);

			_internalUpdate = false;
		}

		public override void _Ready()
		{
			_betOnceBtn.Pressed += OnBetOncePressed;
			_autoBetToggle.Pressed += OnAutoTogglePressed;
			_betAmountInput.TextChanged += OnBetAmountInputTextChanged;
			_increaseOnLossWinToggle.Pressed += OnIncreaseOnWinLossTogglePressed;
		}

		private void OnBetOncePressed()
		{
			BetOnceBtnPressed?.Invoke();
		}

		private void OnAutoTogglePressed()
		{
			bool running = _autoBetToggle.ButtonPressed;
			_autoBetToggle.Text = running ? "STOP" : "AUTO";
			AutoBetToggled?.Invoke(running);
		}

		private void OnBetAmountInputTextChanged(string text)
		{
			if (_internalUpdate)
				return;

			BetAmountInputChanged?.Invoke(text);
		}

		private void OnIncreaseOnWinLossTogglePressed()
		{
			bool increasingOnWin = _increaseOnLossWinToggle.ButtonPressed;
			_increaseOnLossWinToggle.Text = increasingOnWin ? "Increase on win" : "Increase on loss"; 
		}
	}
}
