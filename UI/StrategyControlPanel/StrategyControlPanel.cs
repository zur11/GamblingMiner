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
		// Raised by a double click on EITHER insist toggle — DiceGame's gate for re-enabling manual betting
		// after a profit/loss stop, and either stop may have been the one that fired.
		public event Action ProfitOrLossStopDoubleClicked;
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
		private LineEdit _increaseOnLossPercentInput;
		[Export]
		private LineEdit _increaseOnWinPercentInput;
		[Export]
		private LineEdit _numberOfBetsInput;
		[Export]
		private LineEdit _stopOnProfitInput;
		[Export]
		private LineEdit _stopOnLossInput;
		[Export]
		private Button _stopOnBlockMinedToggle;
		[Export]
		private Button _autoRechargeToggle;
		[Export]
		private Button _insistAfterStopOnProfitToggle;
		[Export]
		private Button _insistAfterStopOnLossToggle;

		private double _lastStopOnBlockMinedPressAt;
		private double _lastInsistOnProfitPressAt;
		private double _lastInsistOnLossPressAt;
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

		// Each outcome carries its own progression percent, armed by its own value: blank, unparseable or
		// <= 0 all mean "this outcome resets the bet to base" (the same 0-means-off rule the stop amounts use).
		public decimal IncreaseOnLossPercent => ParsePercent(_increaseOnLossPercentInput?.Text);
		public decimal IncreaseOnWinPercent => ParsePercent(_increaseOnWinPercentInput?.Text);

		private static decimal ParsePercent(string text)
		{
			if (!decimal.TryParse(
					(text ?? string.Empty).Trim().Replace(',', '.'),
					NumberStyles.Number,
					CultureInfo.InvariantCulture,
					out decimal value) ||
				value <= 0m)
			{
				return 0m;
			}

			return value;
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
		public bool AutoRechargeEnabled => _autoRechargeToggle?.ButtonPressed ?? true;
		// Each insist switch is gated on ITS OWN stop amount — insisting on a stop that is not armed is
		// meaningless. In bot strategy mode both are forced ON (see ApplyStrategyModeRestrictions).
		public bool InsistAfterStopOnProfitEnabled =>
			_insistAfterStopOnProfitToggle?.ButtonPressed == true &&
			(_botStrategyMode || HasStopOnProfitAmount());
		public bool InsistAfterStopOnLossEnabled =>
			_insistAfterStopOnLossToggle?.ButtonPressed == true &&
			(_botStrategyMode || HasStopOnLossAmount());

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
			_increaseOnLossPercentInput.Text = FormatOptionalPercent(config.IncreaseOnLossPercent);
			_increaseOnWinPercentInput.Text = FormatOptionalPercent(config.IncreaseOnWinPercent);
			_numberOfBetsInput.Text = Math.Max(0, numberOfBets).ToString(CultureInfo.InvariantCulture);
			_stopOnProfitInput.Text = FormatOptionalDecimal(config.StopOnProfit);
			_stopOnLossInput.Text = FormatOptionalDecimal(config.StopOnLoss);
			_stopOnBlockMinedToggle.ButtonPressed = config.StopOnBlockMined;
			_stopOnBlockMinedToggle.Text = config.StopOnBlockMined ? "Stop Block: ON" : "Stop Block: OFF";
			_autoRechargeToggle.ButtonPressed = autoRechargeEnabled;
			_autoRechargeToggle.Text = autoRechargeEnabled ? "Auto Recharge: ON" : "Auto Recharge: OFF";
			_insistAfterStopOnProfitToggle.ButtonPressed = config.InsistAfterStopOnProfit;
			_insistAfterStopOnLossToggle.ButtonPressed = config.InsistAfterStopOnLoss;
			_internalUpdate = false;

			UpdateInsistToggleAvailability();
			ApplyStrategyModeRestrictions();
			StrategyConfigChanged?.Invoke();
			BetAmountInputChanged?.Invoke(_betAmountInput.Text);
			AutoRechargeToggled?.Invoke(autoRechargeEnabled);
		}

		public void ClearStrategySettings()
		{
			_internalUpdate = true;
			_betAmountInput.Text = string.Empty;
			_increaseOnLossPercentInput.Text = string.Empty;
			_increaseOnWinPercentInput.Text = string.Empty;
			_numberOfBetsInput.Text = string.Empty;
			_stopOnProfitInput.Text = string.Empty;
			_stopOnLossInput.Text = string.Empty;
			_stopOnBlockMinedToggle.ButtonPressed = false;
			_stopOnBlockMinedToggle.Text = "Stop Block: OFF";
			_autoRechargeToggle.ButtonPressed = true;
			_autoRechargeToggle.Text = "Auto Recharge: ON";
			_insistAfterStopOnProfitToggle.ButtonPressed = false;
			_insistAfterStopOnLossToggle.ButtonPressed = false;
			_internalUpdate = false;

			UpdateInsistToggleAvailability();
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
			_increaseOnLossPercentInput.TextChanged += _ => OnStrategyInputChanged();
			_increaseOnWinPercentInput.TextChanged += _ => OnStrategyInputChanged();
			_maxBetAmountBtn.Pressed += OnMaxBetAmountBtnPressed;
			_minBetAmountBtn.Pressed += OnMinBetAmountBtnPressed;
			_x2BetAmountBtn.Pressed += OnX2BetAmountBtnPressed;
			_divBy2BetAmountBtn.Pressed += OnDivBy2BetAmountBtnPressed;
			_stopOnProfitInput.TextChanged += _ => OnProfitOrLossStopInputChanged();
			_stopOnLossInput.TextChanged += _ => OnProfitOrLossStopInputChanged();
			_numberOfBetsInput.TextChanged += _ => OnStrategyInputChanged();
			_stopOnBlockMinedToggle.Pressed += OnStopOnBlockMinedTogglePressed;
			_autoRechargeToggle.Pressed += OnAutoRechargeTogglePressed;
			_insistAfterStopOnProfitToggle.Pressed += OnInsistAfterStopOnProfitTogglePressed;
			_insistAfterStopOnLossToggle.Pressed += OnInsistAfterStopOnLossTogglePressed;
			UpdateInsistToggleAvailability();
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

		// Seed the auto-recharge toggle from an EXTERNAL source of truth without raising AutoRechargeToggled.
		// For the player this panel toggle is only an access point to the service-level
		// BankrollProgramService.AutoRechargeEnabled (the same flag the Bankroll Programmer toggle owns — Step 12
		// SF.2.8 follow-up); DiceGame calls this to reflect the service value into the panel. A programmatic
		// ButtonPressed write does not raise the Button's Pressed signal, so OnAutoRechargeTogglePressed is not
		// re-entered; _internalUpdate is set anyway for consistency with the panel's other silent-set paths.
		public void SetAutoRechargeEnabled(bool enabled)
		{
			if (_autoRechargeToggle == null)
			{
				return;
			}

			_internalUpdate = true;
			_autoRechargeToggle.ButtonPressed = enabled;
			_autoRechargeToggle.Text = enabled ? "Auto Recharge: ON" : "Auto Recharge: OFF";
			_internalUpdate = false;
		}

		private void OnProfitOrLossStopInputChanged()
		{
			if (_internalUpdate)
			{
				return;
			}

			UpdateInsistToggleAvailability();
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

		// Both insist toggles raise ProfitOrLossStopDoubleClicked — DiceGame's gate for re-enabling manual
		// betting after a profit/loss stop, and either stop may have been the one that fired. They keep
		// SEPARATE double-click timers so alternating one click on each is not read as a double click.
		private void OnInsistAfterStopOnProfitTogglePressed()
		{
			if (!_botStrategyMode && !HasStopOnProfitAmount())
			{
				_insistAfterStopOnProfitToggle.ButtonPressed = false;
			}

			UpdateInsistToggleAvailability();
			ApplyStrategyModeRestrictions();
			CheckDoubleClickAndEmit(
				ref _lastInsistOnProfitPressAt,
				() => ProfitOrLossStopDoubleClicked?.Invoke()
			);
			StrategyConfigChanged?.Invoke();
		}

		private void OnInsistAfterStopOnLossTogglePressed()
		{
			if (!_botStrategyMode && !HasStopOnLossAmount())
			{
				_insistAfterStopOnLossToggle.ButtonPressed = false;
			}

			UpdateInsistToggleAvailability();
			ApplyStrategyModeRestrictions();
			CheckDoubleClickAndEmit(
				ref _lastInsistOnLossPressAt,
				() => ProfitOrLossStopDoubleClicked?.Invoke()
			);
			StrategyConfigChanged?.Invoke();
		}

		// Each insist toggle is enabled only while its own stop amount is armed. In bot strategy mode both are
		// forced ON and locked: a bot session is restarted only on InsufficientBalance, so a stop that cannot
		// insist would end that bot's betting for good (see ApplyStrategyModeRestrictions).
		private void UpdateInsistToggleAvailability()
		{
			UpdateInsistToggle(_insistAfterStopOnProfitToggle, HasStopOnProfitAmount(), "Insist On Profit");
			UpdateInsistToggle(_insistAfterStopOnLossToggle, HasStopOnLossAmount(), "Insist On Loss");
		}

		// Single writer for a toggle's pressed/disabled/text triple — split across two methods it drifts (the
		// bot-mode force would leave the label reading OFF on a pressed toggle).
		private void UpdateInsistToggle(Button toggle, bool hasStopAmount, string label)
		{
			if (toggle == null)
			{
				return;
			}

			if (_botStrategyMode)
			{
				toggle.ButtonPressed = true;
				toggle.Disabled = true;
			}
			else
			{
				if (!hasStopAmount)
				{
					toggle.ButtonPressed = false;
				}

				toggle.Disabled = !hasStopAmount;
			}

			toggle.Text = toggle.ButtonPressed ? $"{label}: ON" : $"{label}: OFF";
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

			// Both stop amount fields stay editable in bot strategy mode: with insisting forced ON (below), a
			// bot's stop resets its progression instead of ending its run, so neither is terminal any more.
			UpdateInsistToggleAvailability();
		}

		private bool HasStopOnProfitAmount()
		{
			return HasPositiveDecimal(_stopOnProfitInput?.Text);
		}

		private bool HasStopOnLossAmount()
		{
			return HasPositiveDecimal(_stopOnLossInput?.Text);
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
				IncreaseOnLossPercent = IncreaseOnLossPercent,
				IncreaseOnWinPercent = IncreaseOnWinPercent,
				StopOnProfit = ParsePositiveDecimal(_stopOnProfitInput.Text),
				StopOnLoss = ParsePositiveDecimal(_stopOnLossInput.Text),
				StopOnBlockMined = !_botStrategyMode && StopOnBlockMinedEnabled,
				InsistAfterStopOnProfit = InsistAfterStopOnProfitEnabled,
				InsistAfterStopOnLoss = InsistAfterStopOnLossEnabled
			};
		}

		// A stop is armed by its AMOUNT alone: blank, unparseable or <= 0 all mean "disabled". This is the
		// single parse boundary for that rule, so everything downstream can keep using HasValue as the test.
		private decimal? ParsePositiveDecimal(string text)
		{
			return TryParseDecimal(text, out decimal value) && value > 0m
				? value
				: null;
		}

		// 0 renders as an EMPTY field, matching the "blank means off" reading the player types.
		private static string FormatOptionalPercent(decimal value)
		{
			return value > 0m
				? value.ToString(CultureInfo.InvariantCulture)
				: string.Empty;
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
