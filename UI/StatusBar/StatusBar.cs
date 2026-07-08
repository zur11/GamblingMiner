using Godot;
using System;
using System.Globalization;
using Scripts.Finance;

namespace UI.StatusBar
{
	public partial class StatusBar : HBoxContainer
	{
		private Label _mainBalanceLabel;
		private Label _bankrollLabel;
		private Label _clockLabel;
		private Label _btcTickerLabel;

		private PrincipalBalanceService _principal;
		private BankrollStateService _bankroll;
		private CalendarTimeService _calendar;
		private BtcMarketDataService _btcMarketData;

		public override void _Ready()
		{
			AddThemeConstantOverride("separation", 40);

			// Step 13 (TL.2) — a permanent, unmissable watermark whenever the DEV alt-timeline simulacrum is
			// active, so no screenshot/session can ever be mistaken for canon (plan §0 warning box). Leftmost
			// for maximum visibility. DevAltTimeline is a compile-time const — this branch either always
			// renders or never does, for a given build (hence the CS0162 suppression: the block is deliberate
			// dead code on canon builds and must stay for the next simulacrum re-mount — ProjectDesignManual Ch. 35).
#pragma warning disable CS0162
			if (TimelineConfig.DevAltTimeline)
			{
				var watermark = BuildLabel();
				watermark.Text = "[ALT-TIMELINE DEV]";
				watermark.AddThemeColorOverride("font_color", new Color(1f, 0.15f, 0.15f));
				watermark.AddThemeFontSizeOverride("font_size", 24);
			}
#pragma warning restore CS0162

			_mainBalanceLabel = BuildLabel();
			_bankrollLabel = BuildLabel();
			_clockLabel = BuildLabel();
			_btcTickerLabel = BuildLabel();

			_principal = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
			_bankroll = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
			_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
			_btcMarketData = GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService");

			if (_btcMarketData != null)
			{
				_btcMarketData.MarketDayChanged += OnMarketDayChanged;
			}

			Refresh();
			RefreshBtcTicker();
		}

		public override void _ExitTree()
		{
			if (_btcMarketData != null)
			{
				_btcMarketData.MarketDayChanged -= OnMarketDayChanged;
			}
		}

		public override void _Process(double delta)
		{
			Refresh();
		}

		private Label BuildLabel()
		{
			var label = new Label();
			label.AddThemeFontSizeOverride("font_size", 22);
			AddChild(label);
			return label;
		}

		private void Refresh()
		{
			if (_mainBalanceLabel == null) return;

			decimal mainBalance = _principal?.CurrentBalance ?? 0m;
			decimal bankroll = _bankroll?.CurrentBalance ?? 0m;

			_mainBalanceLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Main Balance: {mainBalance:F2} SC");
			_bankrollLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Bankroll: {bankroll:F2} SC");
			_clockLabel.Text = _calendar?.CurrentLocalDateTime.ToString("MMM d, yyyy  HH:mm:ss", CultureInfo.InvariantCulture) ?? "--";
		}

		// Step 13 (MD.2 / D-13.3-b) — a compact, high-visibility BTC price cell. Refreshes only on
		// MarketDayChanged (the price is a daily step function — zero per-frame cost), not from _Process.
		private void OnMarketDayChanged(MarketDay day) => RefreshBtcTicker();

		private void RefreshBtcTicker()
		{
			if (_btcTickerLabel == null)
			{
				return;
			}

			DateTime gameTime = _calendar?.CurrentLocalDateTime ?? DateTime.MinValue;
			if (_btcMarketData == null || !_btcMarketData.IsMarketBorn(gameTime))
			{
				_btcTickerLabel.Text = "BTC —";
				_btcTickerLabel.RemoveThemeColorOverride("font_color");
				return;
			}

			if (_btcMarketData.IsHaltDay(gameTime))
			{
				_btcTickerLabel.Text = "BTC HALT";
				_btcTickerLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
				return;
			}

			_btcTickerLabel.RemoveThemeColorOverride("font_color");
			_btcTickerLabel.Text = _btcMarketData.GetEffectivePriceUsd(gameTime) is decimal price
				? string.Create(CultureInfo.InvariantCulture, $"BTC {price:N2}")
				: "BTC —";
		}
	}
}
