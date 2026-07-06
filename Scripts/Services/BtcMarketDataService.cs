using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using Scripts.Finance;
#nullable enable

// Step 13 (MD.1) — autoload #14. Loads the historical BTC/USD daily dataset once and exposes O(1)
// day lookups + a day-change event to every consumer (BTCWallet valuation line, StatusBar ticker,
// ScFinances, the future BtcSwap desk). No timers, no per-frame parsing — the CSV never changes at
// runtime, and the only live work is noticing the game clock crossing a day boundary.
// See AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md §1, §4.1.
public sealed record MarketDay(DateTime DateLocal, decimal? PriceUsd, decimal? VolumeBtc, long? NumTrades, string Source);

public partial class BtcMarketDataService : Node
{
	private const string CsvPath = "res://Data/HistoricalPrices/btc_usd_daily_2010_2025.csv";

	private CalendarTimeService? _calendarTimeService;
	private List<MarketDay> _days = new();
	private decimal?[] _effectivePriceUsd = Array.Empty<decimal?>();
	private DateTime _lastSeenDateLocal = DateTime.MinValue;
	private bool _lastSeenDateInitialized;

	public DateTime FirstDataDateLocal { get; private set; }
	public DateTime LastDataDateLocal { get; private set; }

	// Fired when the game clock (CalendarTimeService) crosses a calendar-day boundary. Payload is null
	// if the new day falls outside the dataset's range (before FirstDataDateLocal / after LastDataDateLocal).
	public event Action<MarketDay?>? MarketDayChanged;

	public override void _Ready()
	{
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		LoadCsv();
	}

	public override void _Process(double delta)
	{
		if (_calendarTimeService == null)
		{
			return;
		}

		DateTime nowDateLocal = _calendarTimeService.CurrentLocalDateTime.Date;
		if (_lastSeenDateInitialized && nowDateLocal == _lastSeenDateLocal)
		{
			return;
		}

		_lastSeenDateLocal = nowDateLocal;
		_lastSeenDateInitialized = true;

		TryGetDay(nowDateLocal, out MarketDay? day);
		GD.Print(day is null
			? $"[BtcMarketDataService] Day changed → {nowDateLocal:yyyy-MM-dd} (no market data yet)"
			: $"[BtcMarketDataService] Day changed → {day.DateLocal:yyyy-MM-dd} price={(day.PriceUsd?.ToString(CultureInfo.InvariantCulture) ?? "null")} source={day.Source}");
		MarketDayChanged?.Invoke(day);
	}

	// now >= FirstDataDateLocal — the trading-unlock gate (§2). Data-driven, never a second hardcoded date.
	public bool IsMarketBorn(DateTime nowLocal) => _days.Count > 0 && nowLocal.Date >= FirstDataDateLocal;

	public bool TryGetDay(DateTime dateLocal, out MarketDay? day)
	{
		int index = DayIndex(dateLocal);
		if (index < 0 || index >= _days.Count)
		{
			day = null;
			return false;
		}

		day = _days[index];
		return true;
	}

	// source == "none" — a real historical trading halt (D-13.11), not a data error.
	public bool IsHaltDay(DateTime dateLocal) => TryGetDay(dateLocal, out MarketDay? day) && day!.Source == "none";

	// The day's step-function price (D-13.2), carrying forward over halt days and freezing at the last
	// known price beyond LastDataDateLocal (D-13.5 — "post-history era"). Null before market birth.
	public decimal? GetEffectivePriceUsd(DateTime nowLocal)
	{
		if (_days.Count == 0)
		{
			return null;
		}

		DateTime dateLocal = nowLocal.Date;
		if (dateLocal < FirstDataDateLocal)
		{
			return null;
		}

		int index = dateLocal > LastDataDateLocal ? _days.Count - 1 : DayIndex(dateLocal);
		if (index < 0 || index >= _days.Count)
		{
			return null;
		}

		return _effectivePriceUsd[index];
	}

	// /100 fractal accessors (D-13.10) — ALL gameplay consumption uses these; raw MarketDay fields are
	// DEV/provenance-only (the sanity check that motivates this: a real day's volume can exceed the sim's
	// entire eventual 210,000-BTC supply many times over — see plan §1.6).
	public decimal? GetGameVolumeBtc(DateTime dateLocal)
	{
		if (!TryGetDay(dateLocal, out MarketDay? day) || day!.VolumeBtc is not decimal rawVolume)
		{
			return null;
		}

		return Money.Normalize(rawVolume / 100m);
	}

	// Floor-to-1 so a day with ANY real trades never reads as zero market activity; an explicit 0 (halt
	// days) and a blank/no-data day (Bitfinex regime) both stay their true value (0 and null respectively).
	public long? GetGameNumTrades(DateTime dateLocal)
	{
		if (!TryGetDay(dateLocal, out MarketDay? day) || day!.NumTrades is not long rawTrades)
		{
			return null;
		}

		if (rawTrades <= 0)
		{
			return rawTrades;
		}

		return Math.Max(1L, (long)Math.Round(rawTrades / 100.0, MidpointRounding.AwayFromZero));
	}

	private int DayIndex(DateTime dateLocal) => (int)(dateLocal.Date - FirstDataDateLocal).TotalDays;

	private void LoadCsv()
	{
		using FileAccess file = FileAccess.Open(CsvPath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushWarning($"[BtcMarketDataService] Could not open {CsvPath} — market data unavailable.");
			return;
		}

		string[] lines = file.GetAsText().Split('\n');
		var days = new List<MarketDay>(lines.Length);

		// Header row (date,price_usd,volume_btc,num_trades,source) is skipped by starting at index 1.
		for (int i = 1; i < lines.Length; i++)
		{
			string line = lines[i].TrimEnd('\r', '\n');
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cols = line.Split(',');
			if (cols.Length < 5)
			{
				continue;
			}

			DateTime dateLocal = DateTime.SpecifyKind(
				DateTime.ParseExact(cols[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None),
				DateTimeKind.Local);

			days.Add(new MarketDay(dateLocal, ParseNullableDecimal(cols[1]), ParseNullableDecimal(cols[2]), ParseNullableLong(cols[3]), cols[4].Trim()));
		}

		days.Sort((a, b) => a.DateLocal.CompareTo(b.DateLocal));
		_days = days;

		if (_days.Count > 0)
		{
			FirstDataDateLocal = _days[0].DateLocal;
			LastDataDateLocal = _days[^1].DateLocal;
		}

		BuildEffectivePriceCarryForward();

		GD.Print($"[BtcMarketDataService] Loaded {_days.Count} market days ({FirstDataDateLocal:yyyy-MM-dd} → {LastDataDateLocal:yyyy-MM-dd}).");
	}

	private void BuildEffectivePriceCarryForward()
	{
		_effectivePriceUsd = new decimal?[_days.Count];
		decimal? carry = null;
		for (int i = 0; i < _days.Count; i++)
		{
			if (_days[i].PriceUsd is decimal price)
			{
				carry = price;
			}
			_effectivePriceUsd[i] = carry;
		}
	}

	// Empty cell = no data, never zero (§1.1) — decimal.Parse("") would throw, and a silent 0 would poison
	// charts/swaps.
	private static decimal? ParseNullableDecimal(string raw)
	{
		string trimmed = raw.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}

		return Money.Normalize(decimal.Parse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture));
	}

	private static long? ParseNullableLong(string raw)
	{
		string trimmed = raw.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}

		return long.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
	}
}
