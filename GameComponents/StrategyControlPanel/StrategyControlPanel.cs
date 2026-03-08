using Godot;
using System;
using System.Globalization;
using Scripts.Betting;

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
		private Button _maxBetAmountBtn;
		[Export]
		private Button _minBetAmountBtn;
		[Export]
		private Button _x2BetAmountBtn;
		[Export]
		private Button _divBy2BetAmountBtn;
		[Export]
		private LineEdit _betAmountInput;
		[Export]
		private LineEdit _increasePercentageInput;
		[Export]
		private LineEdit _numberOfBetsInput;
		[Export]
		private LineEdit _stopOnProfitInput;
		[Export]
		private LineEdit _stopOnLossInput;

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

		public int NumberOfBets
		{
			get
			{
				if (int.TryParse(_numberOfBetsInput.Text, out var value))
					return value;

				return 0;
			}
		}

		public void SetBetAmount(decimal amount)
		{
			_internalUpdate = true;

			_betAmountInput.Text =
				amount.ToString("F8", CultureInfo.InvariantCulture);

			_internalUpdate = false;
		}

		public void ManualSetBetAmount(decimal amount)
		{
			_betAmountInput.Text =
				amount.ToString("F8", CultureInfo.InvariantCulture);
		}

		public override void _Ready()
		{
			_betOnceBtn.Pressed += OnBetOncePressed;
			_autoBetToggle.Pressed += OnAutoTogglePressed;
			_betAmountInput.TextChanged += OnBetAmountInputTextChanged;
			_increaseOnLossWinToggle.Pressed += OnIncreaseOnWinLossTogglePressed;
			_maxBetAmountBtn.Pressed += OnMaxBetAmountBtnPressed;
			_minBetAmountBtn.Pressed += OnMinBetAmountBtnPressed;
			_x2BetAmountBtn.Pressed += OnX2BetAmountBtnPressed;
			_divBy2BetAmountBtn.Pressed += OnDivBy2BetAmountBtnPressed;
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

		private void OnMaxBetAmountBtnPressed()
		{
			BetAmountInputChanged?.Invoke("MAX");
		}

		private void OnMinBetAmountBtnPressed()
		{ 
			BetAmountInputChanged?.Invoke("MIN"); 
		}

		private void OnX2BetAmountBtnPressed()
		{ 
			ManualSetBetAmount(BetAmount * 2);
			BetAmountInputChanged?.Invoke(_betAmountInput.Text); 
		}

		private void OnDivBy2BetAmountBtnPressed()
		{
			ManualSetBetAmount(BetAmount / 2);
			BetAmountInputChanged?.Invoke(_betAmountInput.Text);
		}

		public BettingStrategyConfig BuildConfig()
		{
			return new BettingStrategyConfig
			{
				BaseBet = BetAmount,
				IncreasePercent = IncreasePercent,
				IncreaseOnLoss = !IncreasingOnWin,
				IncreaseOnWin = IncreasingOnWin,
				StopOnProfit = ParseDecimal(_stopOnProfitInput.Text),
				StopOnLoss = ParseDecimal(_stopOnLossInput.Text)
			};
		}

		private decimal? ParseDecimal(string text)
		{
			if (decimal.TryParse(text, out var value))
				return value;

			return null;
		}
	}
}
