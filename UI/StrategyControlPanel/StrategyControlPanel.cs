using Godot;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Scripts.Betting;

namespace UI.StrategyControlPanel
{
	public partial class StrategyControlPanel : Control
	{
		// --- Eventos ---
		public event Action BetOnceBtnPressed;
		public event Action<bool> AutoBetToggled;
		public event Action<bool> AutoPauseToggled;
		public event Action<string> BetAmountInputChanged;
		public event Action StrategyConfigChanged;
		public event Action StopOnBlockMinedDoubleClicked;
		public event Action ProfitStopModeDoubleClicked;

        // --- Validación decimal ---
        private static readonly Regex BetRegex =
            new Regex(@"^\d+(\.\d{1,8})?$", RegexOptions.Compiled);

        // --- Flags ---
        private bool _internalUpdate = false;

		// --- Nodos UI ---
		[Export]
		private Button _betOnceBtn;
		[Export]
		private Button _autoBetToggle;
		[Export]
		private Button _autoPauseResumeToggle;
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
		[Export]
		private Button _stopOnBlockMinedToggle;
		[Export]
		private Button _profitStopModeToggle;

		private double _lastStopOnBlockMinedPressAt;
		private double _lastProfitStopModePressAt;
		private const double DoubleClickSeconds = 0.35d;

        // --- Propiedades API ---
        public decimal BetAmount
        {
            get
            {
                return TryParseDecimal(_betAmountInput.Text, out var value)
                    ? value
                    : 0m;
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

		public bool StopOnBlockMinedEnabled => _stopOnBlockMinedToggle?.ButtonPressed ?? false;
		public bool UseProgressionAnchorStops => _profitStopModeToggle?.ButtonPressed ?? false;

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
			StrategyConfigChanged?.Invoke();
		}

		public void SetNumberOfBets(int value)
		{
			_numberOfBetsInput.Text = value.ToString();
		}

		public void SetManualEnabled(bool enabled)
		{
			_betOnceBtn.Disabled = !enabled;
		}

		public void SetAutoRunning(bool running)
		{
			_autoBetToggle.ButtonPressed = running;
			_autoBetToggle.Text = running ? "STOP" : "AUTO";
			_autoPauseResumeToggle.Visible = running;
			_autoPauseResumeToggle.ButtonPressed = false;
			_autoPauseResumeToggle.Text = "PAUSE";

			if (!running)
			{
				StrategyConfigChanged?.Invoke();
			}
        }

		public void SetAutoPaused(bool paused)
		{
			_autoPauseResumeToggle.ButtonPressed = paused;
			_autoPauseResumeToggle.Text = paused ? "RESUME" : "PAUSE";
		}

		public override void _Ready()
		{
			_betOnceBtn.Pressed += OnBetOncePressed;
			_autoBetToggle.Pressed += OnAutoTogglePressed;
			_autoPauseResumeToggle.Pressed += OnAutoPauseResumePressed;
            _betAmountInput.TextChanged += OnBetAmountInputTextChanged;
            _increaseOnLossWinToggle.Pressed += OnIncreaseOnWinLossTogglePressed;
			_maxBetAmountBtn.Pressed += OnMaxBetAmountBtnPressed;
			_minBetAmountBtn.Pressed += OnMinBetAmountBtnPressed;
			_x2BetAmountBtn.Pressed += OnX2BetAmountBtnPressed;
			_divBy2BetAmountBtn.Pressed += OnDivBy2BetAmountBtnPressed;
			_stopOnProfitInput.TextChanged += _ => StrategyConfigChanged?.Invoke();
			_increasePercentageInput.TextChanged += _ => StrategyConfigChanged?.Invoke();
			_stopOnLossInput.TextChanged += _ => StrategyConfigChanged?.Invoke();
			_numberOfBetsInput.TextChanged += _ => StrategyConfigChanged?.Invoke();
			_stopOnBlockMinedToggle.Pressed += OnStopOnBlockMinedTogglePressed;
			_profitStopModeToggle.Pressed += OnProfitStopModeTogglePressed;
		}

		private void OnBetOncePressed()
		{
			BetOnceBtnPressed?.Invoke();
		}

		private void OnAutoTogglePressed()
		{
			bool running = _autoBetToggle.ButtonPressed;
			AutoBetToggled?.Invoke(running);
		}

		private void OnAutoPauseResumePressed()
		{
			bool paused = _autoPauseResumeToggle.ButtonPressed;
			AutoPauseToggled?.Invoke(paused);
		}

        private void OnBetAmountInputTextChanged(string text)
        {
            if (_internalUpdate)
                return;

            if (TryParseDecimal(text, out decimal _))
            {
                BetAmountInputChanged?.Invoke(text);
                StrategyConfigChanged?.Invoke();
            }
        }

        private void OnIncreaseOnWinLossTogglePressed()
		{
			bool increasingOnWin = _increaseOnLossWinToggle.ButtonPressed;
			_increaseOnLossWinToggle.Text = increasingOnWin ? "Increase on win" : "Increase on loss";
			StrategyConfigChanged?.Invoke();
		}

		private void OnMaxBetAmountBtnPressed()
		{
			BetAmountInputChanged?.Invoke("MAX");
			StrategyConfigChanged?.Invoke();
		}

		private void OnMinBetAmountBtnPressed()
		{ 
			BetAmountInputChanged?.Invoke("MIN");
			StrategyConfigChanged?.Invoke();
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

		private void OnStopOnBlockMinedTogglePressed()
		{
			_stopOnBlockMinedToggle.Text = _stopOnBlockMinedToggle.ButtonPressed
				? "Stop Block: ON"
				: "Stop Block: OFF";
			CheckDoubleClickAndEmit(
				ref _lastStopOnBlockMinedPressAt,
				() => StopOnBlockMinedDoubleClicked?.Invoke()
			);
			StrategyConfigChanged?.Invoke();
		}

		private void OnProfitStopModeTogglePressed()
		{
			_profitStopModeToggle.Text = _profitStopModeToggle.ButtonPressed
				? "P/L Mode: Anchor"
				: "P/L Mode: Session";
			CheckDoubleClickAndEmit(
				ref _lastProfitStopModePressAt,
				() => ProfitStopModeDoubleClicked?.Invoke()
			);
			StrategyConfigChanged?.Invoke();
		}

		private void CheckDoubleClickAndEmit(ref double lastPressedAt, Action emit)
		{
			double now = Time.GetTicksMsec() / 1000.0d;
			if ((now - lastPressedAt) <= DoubleClickSeconds)
			{
				emit();
			}
			lastPressedAt = now;
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
				StopOnLoss = ParseDecimal(_stopOnLossInput.Text),
				StopOnBlockMined = StopOnBlockMinedEnabled,
				UseProgressionAnchorStops = UseProgressionAnchorStops
			};
		}

        private decimal? ParseDecimal(string text)
        {
            return TryParseDecimal(text, out var value)
                ? value
                : null;
        }

        public bool TryGetValidBet(out decimal value)
        {
            return TryParseDecimal(_betAmountInput.Text, out value);
        }

        private bool TryParseDecimal(string text, out decimal value)
        {
            value = 0m;

            text = text.Trim().Replace(',', '.');

            if (!BetRegex.IsMatch(text))
                return false;

            return decimal.TryParse(
                text,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }
}
