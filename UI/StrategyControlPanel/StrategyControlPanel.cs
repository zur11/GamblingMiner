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
		public event Action<bool> AutoRechargeToggled;

        // --- Validación decimal ---
        private static readonly Regex BetRegex =
            new Regex(@"^\d+(\.\d{1,8})?$", RegexOptions.Compiled);

        // --- Flags ---
        private bool _internalUpdate = false;
		private bool _botStrategyMode = false;

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
		[Export]
		private Button _autoRechargeToggle;
		[Export]
		private Button _insistAfterStopToggle;

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
		public bool AutoRechargeEnabled => _autoRechargeToggle?.ButtonPressed ?? true;
		public bool InsistAfterStopEnabled =>
			_insistAfterStopToggle?.ButtonPressed == true &&
			(_botStrategyMode || HasProfitOrLossStopAmount());

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

		public void ApplyStrategySettings(BettingStrategyConfig config, int numberOfBets, bool autoRechargeEnabled)
		{
			if (config == null)
			{
				return;
			}

			_internalUpdate = true;
			_betAmountInput.Text = config.BaseBet.ToString("F8", CultureInfo.InvariantCulture);
			_increasePercentageInput.Text = config.IncreasePercent.ToString(CultureInfo.InvariantCulture);
			_increaseOnLossWinToggle.ButtonPressed = config.IncreaseOnWin;
			_increaseOnLossWinToggle.Text = config.IncreaseOnWin ? "Increase on win" : "Increase on loss";
			_numberOfBetsInput.Text = Math.Max(0, numberOfBets).ToString(CultureInfo.InvariantCulture);
			_stopOnProfitInput.Text = FormatOptionalDecimal(config.StopOnProfit);
			_stopOnLossInput.Text = FormatOptionalDecimal(config.StopOnLoss);
			_stopOnBlockMinedToggle.ButtonPressed = config.StopOnBlockMined;
			_stopOnBlockMinedToggle.Text = config.StopOnBlockMined ? "Stop Block: ON" : "Stop Block: OFF";
			_profitStopModeToggle.ButtonPressed = config.UseProgressionAnchorStops;
			_profitStopModeToggle.Text = config.UseProgressionAnchorStops ? "P/L Mode: Anchor" : "P/L Mode: Session";
			_autoRechargeToggle.ButtonPressed = autoRechargeEnabled;
			_autoRechargeToggle.Text = autoRechargeEnabled ? "Auto Recharge: ON" : "Auto Recharge: OFF";
			_insistAfterStopToggle.ButtonPressed = config.InsistAfterStop;
			_internalUpdate = false;

			UpdateInsistAfterStopToggleAvailability();
			ApplyStrategyModeRestrictions();
			StrategyConfigChanged?.Invoke();
			BetAmountInputChanged?.Invoke(_betAmountInput.Text);
			AutoRechargeToggled?.Invoke(autoRechargeEnabled);
		}

		public void ClearStrategySettings()
		{
			_internalUpdate = true;
			_betAmountInput.Text = string.Empty;
			_increasePercentageInput.Text = string.Empty;
			_increaseOnLossWinToggle.ButtonPressed = false;
			_increaseOnLossWinToggle.Text = "Increase on loss";
			_numberOfBetsInput.Text = string.Empty;
			_stopOnProfitInput.Text = string.Empty;
			_stopOnLossInput.Text = string.Empty;
			_stopOnBlockMinedToggle.ButtonPressed = false;
			_stopOnBlockMinedToggle.Text = "Stop Block: OFF";
			_profitStopModeToggle.ButtonPressed = false;
			_profitStopModeToggle.Text = "P/L Mode: Session";
			_autoRechargeToggle.ButtonPressed = true;
			_autoRechargeToggle.Text = "Auto Recharge: ON";
			_insistAfterStopToggle.ButtonPressed = false;
			_internalUpdate = false;

			UpdateInsistAfterStopToggleAvailability();
			ApplyStrategyModeRestrictions();
		}

		public void SetBotStrategyMode(bool enabled)
		{
			_botStrategyMode = enabled;
			ApplyStrategyModeRestrictions();
		}

		public void SetManualEnabled(bool enabled)
		{
			_betOnceBtn.Disabled = !enabled;
		}

		// Enables/disables BOTH betting buttons. Used to lock betting when the active node is a bot —
		// only the player may place bets (and thereby advance time).
		public void SetBettingControlsEnabled(bool enabled)
		{
			_betOnceBtn.Disabled = !enabled;
			_autoBetToggle.Disabled = !enabled;
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
			_stopOnProfitInput.TextChanged += _ => OnProfitOrLossStopInputChanged();
			_increasePercentageInput.TextChanged += _ => OnStrategyInputChanged();
			_stopOnLossInput.TextChanged += _ => OnProfitOrLossStopInputChanged();
			_numberOfBetsInput.TextChanged += _ => OnStrategyInputChanged();
			_stopOnBlockMinedToggle.Pressed += OnStopOnBlockMinedTogglePressed;
			_profitStopModeToggle.Pressed += OnProfitStopModeTogglePressed;
			_autoRechargeToggle.Pressed += OnAutoRechargeTogglePressed;
			_insistAfterStopToggle.Pressed += OnInsistAfterStopTogglePressed;
			UpdateInsistAfterStopToggleAvailability();
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
			if (_botStrategyMode)
			{
				_stopOnBlockMinedToggle.ButtonPressed = false;
				_stopOnBlockMinedToggle.Text = "Stop Block: OFF";
				return;
			}

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

		private void OnAutoRechargeTogglePressed()
		{
			if (_botStrategyMode)
			{
				_autoRechargeToggle.ButtonPressed = true;
				_autoRechargeToggle.Text = "Auto Recharge: ON";
				AutoRechargeToggled?.Invoke(true);
				return;
			}

			bool enabled = _autoRechargeToggle.ButtonPressed;
			_autoRechargeToggle.Text = enabled ? "Auto Recharge: ON" : "Auto Recharge: OFF";
			AutoRechargeToggled?.Invoke(enabled);
		}

		private void OnProfitOrLossStopInputChanged()
		{
			if (_internalUpdate)
			{
				return;
			}

			UpdateInsistAfterStopToggleAvailability();
			StrategyConfigChanged?.Invoke();
		}

		private void OnStrategyInputChanged()
		{
			if (_internalUpdate)
			{
				return;
			}

			StrategyConfigChanged?.Invoke();
		}

		private void OnInsistAfterStopTogglePressed()
		{
			if (!_botStrategyMode && !HasProfitOrLossStopAmount())
			{
				_insistAfterStopToggle.ButtonPressed = false;
			}

			UpdateInsistAfterStopToggleAvailability();
			ApplyStrategyModeRestrictions();
			StrategyConfigChanged?.Invoke();
		}

		private void UpdateInsistAfterStopToggleAvailability()
		{
			if (_insistAfterStopToggle == null)
			{
				return;
			}

			bool canEnable = HasProfitOrLossStopAmount();
			if (_botStrategyMode)
			{
				canEnable = true;
			}

			if (!canEnable)
			{
				_insistAfterStopToggle.ButtonPressed = false;
			}

			_insistAfterStopToggle.Disabled = !canEnable;
			_insistAfterStopToggle.Text = _insistAfterStopToggle.ButtonPressed
				? "Insist After Stop: ON"
				: "Insist After Stop: OFF";
		}

		private void ApplyStrategyModeRestrictions()
		{
			if (_stopOnBlockMinedToggle != null)
			{
				if (_botStrategyMode)
				{
					_stopOnBlockMinedToggle.ButtonPressed = false;
					_stopOnBlockMinedToggle.Text = "Stop Block: OFF";
				}
				_stopOnBlockMinedToggle.Disabled = _botStrategyMode;
			}

			if (_autoRechargeToggle != null)
			{
				if (_botStrategyMode)
				{
					_autoRechargeToggle.ButtonPressed = true;
					_autoRechargeToggle.Text = "Auto Recharge: ON";
				}
				_autoRechargeToggle.Disabled = _botStrategyMode;
			}

			bool profitLossInputsEnabled = !_botStrategyMode || (_insistAfterStopToggle?.ButtonPressed == true);
			if (_stopOnProfitInput != null)
			{
				_stopOnProfitInput.Editable = profitLossInputsEnabled;
			}
			if (_stopOnLossInput != null)
			{
				_stopOnLossInput.Editable = profitLossInputsEnabled;
			}
		}

		private bool HasProfitOrLossStopAmount()
		{
			return HasPositiveDecimal(_stopOnProfitInput?.Text) ||
				HasPositiveDecimal(_stopOnLossInput?.Text);
		}

		private bool HasPositiveDecimal(string text)
		{
			return TryParseDecimal(text ?? string.Empty, out decimal value) && value > 0m;
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
				StopOnProfit = _botStrategyMode && !InsistAfterStopEnabled ? null : ParseDecimal(_stopOnProfitInput.Text),
				StopOnLoss = _botStrategyMode && !InsistAfterStopEnabled ? null : ParseDecimal(_stopOnLossInput.Text),
				StopOnBlockMined = !_botStrategyMode && StopOnBlockMinedEnabled,
				UseProgressionAnchorStops = UseProgressionAnchorStops,
				InsistAfterStop = InsistAfterStopEnabled
			};
		}

        private decimal? ParseDecimal(string text)
        {
            return TryParseDecimal(text, out var value)
                ? value
                : null;
        }

		private string FormatOptionalDecimal(decimal? value)
		{
			return value.HasValue
				? value.Value.ToString("F8", CultureInfo.InvariantCulture)
				: string.Empty;
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
