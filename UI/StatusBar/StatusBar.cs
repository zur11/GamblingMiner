using Godot;
using System;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using Scripts.Finance;

namespace UI.StatusBar
{
	public partial class StatusBar : HBoxContainer
	{
		// The player's BTC holding is money they OWN; the ticker beside it is a market quote they don't.
		// Bitcoin orange marks the wallet cell so the two can never be read as the same kind of figure
		// (the wording — "BTC Wallet:" vs "BTC Price: … SC" — carries the rest).
		private static readonly Color BtcWalletColor = new(0.97f, 0.58f, 0.10f);

		// One AggregateSpendable pass over the UTXO set — cheap at this cadence, ruinous per frame (§38.7).
		// BlockAccepted is the real edge; the timer only covers the player's own mid-block sends (a BTCWallet
		// send or a swap sell reduces spendable the instant it is broadcast, with no block to announce it).
		private const double BtcBalanceFallbackInterval = 2.0;

		private Label _mainBalanceLabel;
		private Label _bankrollLabel;
		private Label _btcBalanceLabel;
		private Label _clockLabel;
		private Label _btcTickerLabel;

		private bool _btcBalanceDirty = true;
		private double _btcBalanceTimer;

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

			// Step 15 (P15.8 prep) — the same watermark rule for the EB.1 DEV ENTRY-YEAR bootstrap. An
			// entry-year world is canon-COMPATIBLE (genesis and the founders keep their true dates; the
			// intervening history is really built), which makes it far easier to mistake for a canonical
			// playthrough than the alt-timeline simulacrum ever was — so it needs the marker MORE, not less.
			// Same compile-time-const dead-code situation, same CS0162 suppression.
			if (TimelineConfig.DevEntryYear != 0)
			{
				var entryWatermark = BuildLabel();
				entryWatermark.Text = $"[ENTRY-{TimelineConfig.DevEntryYear} DEV]";
				entryWatermark.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.1f));
				entryWatermark.AddThemeFontSizeOverride("font_size", 24);
			}
#pragma warning restore CS0162

			_mainBalanceLabel = BuildLabel();
			_bankrollLabel = BuildLabel();
			_btcBalanceLabel = BuildLabel();
			_btcBalanceLabel.AddThemeColorOverride("font_color", BtcWalletColor);
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

			NetworkRoot.BlockAccepted += OnBlockAccepted;

			Refresh();
			RefreshBtcBalance();
			RefreshBtcTicker();
		}

		public override void _ExitTree()
		{
			if (_btcMarketData != null)
			{
				_btcMarketData.MarketDayChanged -= OnMarketDayChanged;
			}

			NetworkRoot.BlockAccepted -= OnBlockAccepted; // static event — must not outlive this node
		}

		public override void _Process(double delta)
		{
			Refresh();

			_btcBalanceTimer += delta;
			if (_btcBalanceTimer >= BtcBalanceFallbackInterval)
			{
				_btcBalanceTimer = 0.0;
				_btcBalanceDirty = true;
			}

			if (_btcBalanceDirty)
			{
				_btcBalanceDirty = false;
				RefreshBtcBalance();
			}
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

		// The player's own BTC holding, in every scene. BlockAccepted fires from inside HandleMinedBlock, so
		// it only raises a dirty flag here — the UTXO pass runs on the next frame, never inside the block
		// commit (the AuctioningCompanyDetails precedent).
		private void OnBlockAccepted(Block block) => _btcBalanceDirty = true;

		private void RefreshBtcBalance()
		{
			if (_btcBalanceLabel == null)
			{
				return;
			}

			decimal btc = NetworkRoot.GetPlayerSpendableBalanceStatic();
			_btcBalanceLabel.Text = string.Create(CultureInfo.InvariantCulture, $"BTC Wallet: {btc:N8}");
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
				_btcTickerLabel.Text = "BTC Price: —";
				_btcTickerLabel.RemoveThemeColorOverride("font_color");
				return;
			}

			if (_btcMarketData.IsHaltDay(gameTime))
			{
				_btcTickerLabel.Text = "BTC Price: HALT";
				_btcTickerLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
				return;
			}

			_btcTickerLabel.RemoveThemeColorOverride("font_color");
			_btcTickerLabel.Text = _btcMarketData.GetEffectivePriceUsd(gameTime) is decimal price
				? string.Create(CultureInfo.InvariantCulture, $"BTC Price: {price:N2} SC")
				: "BTC Price: —";
		}
	}
}
