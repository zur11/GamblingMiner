using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using Scripts.Finance;
#nullable enable

// Step 14 (ND.1) — autoload #17. Loads the historical BTC network dataset once and exposes O(1) day
// lookups + the scheduler's derived target accessors (§3.1/§3.2 of the step14 plan). Mirrors
// BtcMarketDataService exactly: load once in _Ready, day-indexed array, a day-change event, no timers,
// no per-frame parsing, read-only over a static asset (no persistence, no checkpoint coverage).
//
// Dataset provenance (ND.0, 2026-07-07): Coin Metrics Community API v4, cross-checked against
// blockchain.com (tx_count median rel-diff 0.81%, hashrate shape median 0.00%). IMPORTANT semantics:
// TxCount EXCLUDES coinbase transactions (proven empirically at ND.0) — it is exactly the non-coinbase
// numerator the fullness-parity target wants, so GetTargetTxPerBlock must NOT subtract coinbase again.
// FeeTotalBtc is the raw daily TOTAL in BTC (mean per tx = total / count, derived when the fee-replay
// step consumes it); fees are fractal-exempt like price_usd (D-14.6 — never /100).
public sealed record NetworkDay(
	DateTime DateLocal,
	long? TxCount,
	double? HashRate,
	long? ActiveAddresses,
	long? BlockCount,
	decimal? FeeTotalBtc);

public partial class BtcNetworkDataService : Node
{
	private const string CsvPath = "res://Data/HistoricalNetwork/btc_network_daily_2009_2025.csv";

	// D-14.8 model constants (§3.1). BaseCast = today's bot_1..4. CastPerDecade = 2 is LOCKED by the
	// D-14.8 arithmetic itself (28 visible miners at the ~12-decade historical max ⇒ the 1/28 ≈ 3.6%
	// player-share anchor). EraMaxHardwareCredits mirrors the canonical (planned) 100-attempt hardware
	// cap — no code constant exists for it yet (Ch. 27 hardware credits have no max); when P5 lands one,
	// route this through it.
	public const int BaseCast = 4;
	public const double CastPerDecade = 2.0;
	public const double EraMaxHardwareCredits = 100.0;

	// EB.2 (D-EB.3/4), pool size raised at round 3 (D-EB.8, 2026-07-09, OQ-EB.5) — the historical
	// non-miner introduction schedule: entering from Market Birth along the active-address curve at
	// 1 + NonMinersPerAddressDecade × log10(runningMaxAddresses / addressesAtBirth). The dataset's true
	// birth->peak span is 3.201 address-decades (peak 2021-04-15, 1,366,494 — corrected 2026-07-09; an
	// earlier "Dec 2017 / 2.9 decades" claim was never actually checked against the CSV and was wrong).
	// NonMinersPerAddressDecade = (NonMinerPoolSize - 1) / 3.201, calibrated so the full pool deploys
	// almost exactly at that historical peak; MUST be recalibrated together if NonMinerPoolSize changes
	// again (BotWalletRegistry.NonMinerBotCount must match NonMinerPoolSize exactly — see its comment).
	public const int NonMinerPoolSize = 40;
	public const int BaseNonMinersAtBirth = 1;
	public const double NonMinersPerAddressDecade = 12.183693;

	private CalendarTimeService? _calendarTimeService;
	private List<NetworkDay> _days = new();
	private DateTime _lastSeenDateLocal = DateTime.MinValue;
	private bool _lastSeenDateInitialized;

	public DateTime FirstDataDateLocal { get; private set; }
	public DateTime LastDataDateLocal { get; private set; }

	// The decades() anchor (§3.1): the real-world hashrate on the player-start day — routed through
	// TimelineConfig (D-14.7) so an alt-timeline world anchors decades = 0 at its own landing day.
	private double _anchorHashRate;
	private double _decadesAtDatasetEnd;

	// Fired when the game clock (CalendarTimeService) crosses a calendar-day boundary. Payload is null
	// if the new day falls outside the dataset's range.
	public event Action<NetworkDay?>? NetworkDayChanged;

	private bool _loaded;

	public override void _Ready()
	{
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		EnsureLoaded();
	}

	// EB.1 (§5.1) — the load/anchor/schedule work extracted from _Ready so it can also run on a
	// THROWAWAY instance (`new BtcNetworkDataService()`) created directly by HistoricalBootstrapService,
	// which runs from CalendarTimeService._Ready() — long before this autoload's own _Ready() would
	// otherwise fire (it is autoload #17, near the end of the list). Idempotent: safe to call more than
	// once on the same instance (the real autoload calls it once via _Ready; a bootstrap-time throwaway
	// instance calls it explicitly and is then discarded — the two never share state, by design, since
	// each is a fresh `_days`/anchor set parsed from the same static CSV).
	public void EnsureLoaded()
	{
		if (_loaded)
		{
			return;
		}
		_loaded = true;

		LoadCsv();
		InitializeAnchors();
		ComputeNonMinerIntroSchedule();
	}

	// EB.2 (D-EB.4) — precompute the 10 non-miner introduction dates from the address curve and push
	// them into NetworkRoot's auction ledger (the ONE shared schedule for canonical live play and the
	// EB.1 entry-year fast-builds alike). Non-miner i is introduced on the first date the running-max
	// address count reaches target i+1. The gate is Market Birth, read from BtcMarketDataService
	// (registered directly before this autoload, so its _Ready — and FirstDataDateLocal — ran already).
	private void ComputeNonMinerIntroSchedule()
	{
		if (_days.Count == 0)
		{
			return;
		}

		// EB.1's throwaway instances (`new BtcNetworkDataService()`, never added to the scene tree — see
		// EnsureLoaded's own comment) can't resolve an absolute "/root/..." path: Godot's native
		// get_node_or_null() prints an engine-level error (not a catchable C# exception) whenever it's
		// called on a node outside the active tree, even though the C#-side GetNodeOrNull still returns
		// null gracefully afterward. Gate on IsInsideTree() first so only the REAL autoload (always in the
		// tree by the time its own _Ready → EnsureLoaded runs) attempts the lookup; a throwaway instance
		// silently falls through to the hardcoded Market Birth date below, exactly as before.
		BtcMarketDataService? market = IsInsideTree()
			? GetNodeOrNull<BtcMarketDataService>("/root/BtcMarketDataService")
			: null;
		DateTime birthLocal = market?.FirstDataDateLocal
			?? DateTime.SpecifyKind(new DateTime(2010, 7, 18), DateTimeKind.Local);

		// The running max starts AT birth — the curve measures growth SINCE the market exists. Including
		// pre-birth history would let the July-2010 slashdot address spike (6,752, days before Mt. Gox)
		// inflate the anchor and compress the span to ~2.3 decades, silently stranding 2 of the 10 bots
		// forever (found empirically against the CSV; with the birth-day anchor of 860 all 10 deploy,
		// the last on 2016-01-20).
		var introDatesLocal = new List<DateTime>(NonMinerPoolSize);
		double runMax = 0d;
		double anchor = 0d;
		foreach (NetworkDay day in _days)
		{
			if (day.DateLocal < birthLocal)
			{
				continue;
			}
			if (day.ActiveAddresses is long a && a > runMax)
			{
				runMax = a;
			}
			if (anchor <= 0d)
			{
				anchor = Math.Max(1d, runMax);
			}

			int target = Math.Min(NonMinerPoolSize,
				BaseNonMinersAtBirth + (int)Math.Round(
					NonMinersPerAddressDecade * Math.Log10(Math.Max(1d, runMax / anchor)),
					MidpointRounding.AwayFromZero));
			while (introDatesLocal.Count < target)
			{
				introDatesLocal.Add(day.DateLocal);
			}
			if (introDatesLocal.Count >= NonMinerPoolSize)
			{
				break;
			}
		}

		// Local calendar date → unix ms, mirroring NetworkFeePolicy's gate convention (Kind stripped to
		// Unspecified, offset zero) so the comparison basis matches the other block-timestamp date gates.
		long[] scheduleMs = new long[introDatesLocal.Count];
		for (int i = 0; i < introDatesLocal.Count; i++)
		{
			scheduleMs[i] = new DateTimeOffset(
				DateTime.SpecifyKind(introDatesLocal[i], DateTimeKind.Unspecified), TimeSpan.Zero)
				.ToUnixTimeMilliseconds();
		}
		NetworkRoot.SetNonMinerIntroSchedule(scheduleMs);

		GD.Print($"[BtcNetworkDataService] Non-miner intro schedule ({introDatesLocal.Count}/{NonMinerPoolSize}): " +
			string.Join(", ", introDatesLocal.ConvertAll(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
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

		TryGetDay(nowDateLocal, out NetworkDay? day);
		NetworkDayChanged?.Invoke(day);
	}

	public bool TryGetDay(DateTime dateLocal, out NetworkDay? day)
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

	// ── Derived scheduler accessors (§3.1 / §3.2 — ALL gameplay consumption goes through these; the
	// raw NetworkDay fields are DEV/provenance-only) ─────────────────────────────────────────────────

	// decades(date) = log10(H(date) / H(playerStart)) — the one era-agnostic growth quantity (P-14.A).
	// Clamped to ≥ 0 (the pre-player bootstrap era reads as baseline); frozen at the last value past the
	// dataset end (D-14.3); 0 while hashrate data is missing (genesis week).
	public double GetDecades(DateTime dateLocal)
	{
		if (_days.Count == 0 || _anchorHashRate <= 0.0)
		{
			return 0.0;
		}

		int index = dateLocal.Date > LastDataDateLocal ? _days.Count - 1 : DayIndex(dateLocal);
		if (index < 0 || index >= _days.Count)
		{
			return 0.0;
		}

		double? hashRate = LastKnownHashRateAt(index);
		if (hashRate is not double h || h <= 0.0)
		{
			return 0.0;
		}

		return Math.Max(0.0, Math.Log10(h / _anchorHashRate));
	}

	// §3.1 visible cast target: BaseCast + round(CastPerDecade × decades).
	public int GetTargetVisibleMiners(DateTime dateLocal)
	{
		return BaseCast + (int)Math.Round(CastPerDecade * GetDecades(dateLocal), MidpointRounding.AwayFromZero);
	}

	// §3.1 D-14.8 derivation: TotalNetworkUnits = EraStandardPower × cast size, where EraStandardPower
	// ramps 1 → EraMaxHardwareCredits along the decades scale. A player wielding the era-standard power
	// therefore always holds one cast member's share (1/28 ≈ 3.6% at the historical max).
	public double GetEraStandardPower(DateTime dateLocal)
	{
		if (_decadesAtDatasetEnd <= 0.0)
		{
			return 1.0;
		}

		double fraction = Math.Min(1.0, GetDecades(dateLocal) / _decadesAtDatasetEnd);
		return Math.Pow(EraMaxHardwareCredits, fraction);
	}

	public double GetTotalNetworkUnits(DateTime dateLocal)
	{
		return GetEraStandardPower(dateLocal) * GetTargetVisibleMiners(dateLocal);
	}

	// §3.2 fullness parity (P-14.B): target AUTOMATED txs per block = real txs-per-block ÷ 100, clamped
	// to the non-coinbase block capacity. TxCount already excludes coinbase (ND.0 finding) — do not
	// subtract again. Freezes at the last data row past the dataset end; 0 when data is missing or no
	// blocks were mined that day.
	public decimal GetTargetTxPerBlock(DateTime dateLocal)
	{
		if (_days.Count == 0)
		{
			return 0m;
		}

		int index = dateLocal.Date > LastDataDateLocal ? _days.Count - 1 : DayIndex(dateLocal);
		if (index < 0 || index >= _days.Count)
		{
			return 0m;
		}

		NetworkDay day = _days[index];
		if (day.TxCount is not long tx || day.BlockCount is not long blocks || blocks <= 0)
		{
			return 0m;
		}

		decimal target = Money.Normalize(tx / (decimal)blocks / 100m);
		return Math.Clamp(target, 0m, BlockTemplateBuilder.MaxBlockTransactions - 1);
	}

	private int DayIndex(DateTime dateLocal) => _days.Count == 0 ? -1 : (int)(dateLocal.Date - FirstDataDateLocal).TotalDays;

	// Genesis week has null hashrate; a handful of later cells could in principle be blank too. For a
	// growth-ratio quantity the honest gap policy is carry-forward of the last known value (same shape
	// as the market service's halt-day carry-forward).
	private double? LastKnownHashRateAt(int index)
	{
		for (int i = index; i >= 0; i--)
		{
			if (_days[i].HashRate is double h)
			{
				return h;
			}
		}

		return null;
	}

	private void InitializeAnchors()
	{
		if (_days.Count == 0)
		{
			return;
		}

		int anchorIndex = DayIndex(TimelineConfig.PlayerStartDayLocal.Date);
		anchorIndex = Math.Clamp(anchorIndex, 0, _days.Count - 1);
		_anchorHashRate = LastKnownHashRateAt(anchorIndex) ?? 0.0;
		_decadesAtDatasetEnd = _anchorHashRate <= 0.0
			? 0.0
			: Math.Max(0.0, Math.Log10((LastKnownHashRateAt(_days.Count - 1) ?? _anchorHashRate) / _anchorHashRate));

		GD.Print(string.Create(CultureInfo.InvariantCulture,
			$"[BtcNetworkDataService] Anchors: H(playerStart {TimelineConfig.PlayerStartDayLocal:yyyy-MM-dd}) = {_anchorHashRate}, decades(end) = {_decadesAtDatasetEnd:F3}, cast(end) = {GetTargetVisibleMiners(LastDataDateLocal)}, units(end) = {GetTotalNetworkUnits(LastDataDateLocal):F0}"));
	}

	private void LoadCsv()
	{
		using FileAccess file = FileAccess.Open(CsvPath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushWarning($"[BtcNetworkDataService] Could not open {CsvPath} — network data unavailable.");
			return;
		}

		string[] lines = file.GetAsText().Split('\n');
		var days = new List<NetworkDay>(lines.Length);

		// Header row (date,tx_count,hashrate,active_addresses,block_count,fee_total_btc,source) skipped.
		for (int i = 1; i < lines.Length; i++)
		{
			string line = lines[i].TrimEnd('\r', '\n');
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			string[] cols = line.Split(',');
			if (cols.Length < 7)
			{
				continue;
			}

			DateTime dateLocal = DateTime.SpecifyKind(
				DateTime.ParseExact(cols[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None),
				DateTimeKind.Local);

			days.Add(new NetworkDay(
				dateLocal,
				ParseNullableLong(cols[1]),
				ParseNullableDouble(cols[2]),
				ParseNullableLong(cols[3]),
				ParseNullableLong(cols[4]),
				ParseNullableDecimal(cols[5])));
		}

		days.Sort((a, b) => a.DateLocal.CompareTo(b.DateLocal));
		_days = days;

		if (_days.Count > 0)
		{
			FirstDataDateLocal = _days[0].DateLocal;
			LastDataDateLocal = _days[^1].DateLocal;
		}

		GD.Print($"[BtcNetworkDataService] Loaded {_days.Count} network days ({FirstDataDateLocal:yyyy-MM-dd} → {LastDataDateLocal:yyyy-MM-dd}).");
	}

	// Empty cell = no data, never zero (ND.0 parsing rule — same as BtcMarketDataService).
	private static long? ParseNullableLong(string raw)
	{
		string trimmed = raw.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}

		return long.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
	}

	// Hashrate is a physical measure consumed only through log-ratios — double, not decimal (the raw
	// CSV strings carry more digits than decimal can hold, and money rules don't apply to it).
	private static double? ParseNullableDouble(string raw)
	{
		string trimmed = raw.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}

		return double.Parse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture);
	}

	private static decimal? ParseNullableDecimal(string raw)
	{
		string trimmed = raw.Trim();
		if (trimmed.Length == 0)
		{
			return null;
		}

		return Money.Normalize(decimal.Parse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture));
	}
}
