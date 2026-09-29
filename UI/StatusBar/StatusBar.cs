using Godot;
using System;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using Scripts.Finance;
using UI.Readouts;

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

		// The clock turns violet whenever it is NOT showing the present — i.e. while the calendar or the
		// history explorer has wound it back for a replay. A date is the one figure on this bar a player has
		// no way to sanity-check by eye: 2009-05-12 looks exactly as plausible as 2009-05-24, so a rewound
		// clock reads as the real one and every balance beside it silently becomes a historical figure.
		// Deliberately driven by the STATE (current != present), not by which scene is open — leaving those
		// scenes restores the present, so the colour clears itself with no per-scene bookkeeping.
		private static readonly Color ReplayClockColor = new(0.72f, 0.45f, 0.95f);

		private Label _mainBalanceLabel;
		private Label _bankrollLabel;
		private Label _btcBalanceLabel;
		private Label _clockLabel;
		private Label _btcTickerLabel;

		// Mini-plan 15 B — money may not sit still for the second the clock's own cadence can stretch to, so the
		// balances keep a fixed cadence of their own. 10 Hz: faster than a player can read a changing figure.
		private const double BalanceRefreshInterval = 0.1;

		private bool _btcBalanceDirty = true;
		private double _btcBalanceTimer;
		private double _balanceTimer;
		private readonly AdaptiveReadoutSampler _clockSampler = new();
		// Repaint the clock's colour only on the edge — RefreshClock runs on every clock repaint.
		private bool _clockShowsReplay;
		private bool _clockColorApplied;

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

			// LEFTMOST, beside the DEV watermarks: a diagnostic rather than a player figure, and appending it
			// on the right left it off-screen in the scenes whose bar already overflows. It hides itself
			// unless a sim is running, so it costs nothing visually in normal play.
			AddChild(new UI.SimRetentionReadout.SimRetentionReadout());

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

			RefreshBalances();
			RefreshClock();
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
			// Mini-plan 15 B — the clock and the SC balances were one per-frame Refresh(), and the clock had
			// DiceGame's strobe for the same reason: a near-constant game-time step per frame freezes its
			// smallest field. They are separated here because they want different cadences. The clock's is
			// MEASURED (see AdaptiveReadoutSampler) and can stretch to a second at 9000X; money may not sit
			// that long, so the balances keep a fixed 10 Hz — still 6x cheaper than per-frame, and at any speed
			// where a balance changes thousands of times a second no cadence is more "correct" than another.
			_balanceTimer += delta;
			if (_balanceTimer >= BalanceRefreshInterval)
			{
				_balanceTimer = 0.0;
				RefreshBalances();
			}

			if (_clockSampler.ShouldRepaint(delta))
			{
				RefreshClock();
			}

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

		private void RefreshBalances()
		{
			if (_mainBalanceLabel == null) return;

			decimal mainBalance = _principal?.CurrentBalance ?? 0m;
			decimal bankroll = _bankroll?.CurrentBalance ?? 0m;

			_mainBalanceLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Main Balance: {mainBalance:F2} SC");
			_bankrollLabel.Text = string.Create(CultureInfo.InvariantCulture, $"Bankroll: {bankroll:F2} SC");
		}

		private void RefreshClock()
		{
			if (_clockLabel == null) return;

			if (_calendar == null)
			{
				_clockLabel.Text = "--";
				return;
			}

			DateTime local = _calendar.CurrentLocalDateTime;
			_clockSampler.NoteRepaint(local);
			// The date style stays exactly as it was; only the time half narrows when the sample cannot honestly
			// carry it — at 9000X this bar reads "Jul 18, 2010  14h" rather than a frozen seconds field.
			_clockLabel.Text = _clockSampler.FormatGameTime(local, "MMM d, yyyy ");

			// One DateTime comparison per repaint; the theme override is written only when the state flips.
			bool replay = local < _calendar.GamePresentLocalDateTime;
			if (replay != _clockShowsReplay || !_clockColorApplied)
			{
				_clockShowsReplay = replay;
				_clockColorApplied = true;
				_clockLabel.AddThemeColorOverride("font_color", replay ? ReplayClockColor : Colors.White);
			}
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
