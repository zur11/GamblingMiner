using System;
using Godot;

namespace GodotBlockchainPort.Blockchain;

// ND.7 (step14 plan §10) — Historical Fee Replay. The Step-10 flat-fee scaffold (0.1 BTC from
// 2009-04-26) is RETIRED (D-ND7.1): no fee exists anywhere on the network before Market Birth
// (2010-07-18); from Market Birth the real daily historical band governs. The schedule is pushed
// once at load by BtcNetworkDataService.EnsureLoaded() via SetFeeSchedule (D-ND7.5 — the
// SetNonMinerIntroSchedule precedent, so EB.1's throwaway bootstrap instances arm it too), keeping
// this a pure static class with O(1) day lookups on both bases (game-local date + block Unix-ms).
//
// The daily band (D-ND7.2/7.3): MEDIAN = the day's real median fee (the base every participant
// pays by default), MEAN = fee_total ÷ tx_count (paid by the cast miners' sell-flow — they ARE the
// network's average activity), MAX = max(median, mean) × MaxFeeMeanMultiplier (a documented
// approximation — no real daily-max metric exists in any source). Both components carry forward
// their last positive value across zero/blank days (D-ND7.4). The median has NO positive value
// before 2011-04-14 (found at ND.7.0 — most txs genuinely paid no fee), so the effective median is
// an honest 0 from Market Birth through 2011-04-13; the band [0, max] stays valid since the mean
// is positive from the data's own start.
//
// No schedule pushed (CSV load failure) ⇒ fee-free fallback + one warning — never a crash and
// never the 0.1 scaffold back (D-ND7.5).
public static class NetworkFeePolicy
{
    // One effective-replay day (already carry-forward-resolved and Money.Normalize'd by the builder).
    public readonly record struct FeeDay(decimal Median, decimal Mean, decimal Max);

    // D-ND7.2 — the documented approximation factor for the derived daily max.
    public const decimal MaxFeeMeanMultiplier = 10m;

    private static bool _hasSchedule;
    private static DateTime _firstReplayDayLocal;
    private static long _firstReplayDayMs;
    private static FeeDay[] _days = Array.Empty<FeeDay>();
    private static bool _warnedNoSchedule;

    // Pushed by BtcNetworkDataService.EnsureLoaded(). entries[0] is firstReplayDayLocal itself and
    // the array is day-contiguous (the dataset carries a hard continuity assertion). Past the last
    // entry the values freeze (D-ND7.4, mirroring the price service's D-13.5 freeze).
    public static void SetFeeSchedule(DateTime firstReplayDayLocal, FeeDay[] entries)
    {
        if (entries == null || entries.Length == 0)
        {
            return; // fee-free fallback stands
        }

        _firstReplayDayLocal = firstReplayDayLocal.Date;
        // Local calendar date interpreted as midnight UTC — the same gate convention the Step-10
        // activation used (Kind stripped to Unspecified first; see SetNonMinerIntroSchedule, which
        // mirrors this convention for the same block-timestamp comparison basis).
        _firstReplayDayMs = new DateTimeOffset(
            DateTime.SpecifyKind(_firstReplayDayLocal, DateTimeKind.Unspecified), TimeSpan.Zero)
            .ToUnixTimeMilliseconds();
        _days = entries;
        _hasSchedule = true;
    }

    // UI layer: compare against the game clock (CalendarTimeService.CurrentLocalDateTime).
    public static bool IsActive(DateTime gameLocalDateTime)
        => _hasSchedule && gameLocalDateTime.Date >= _firstReplayDayLocal;

    // Backend layer: compare against a block's Unix-ms timestamp.
    public static bool IsActiveByTimestamp(long blockTimestampMs)
        => _hasSchedule && blockTimestampMs >= _firstReplayDayMs;

    // ── Daily band accessors — 0 before Market Birth (fee-free era) and 0 under the no-schedule
    // fallback; frozen at the last entry past the dataset end ────────────────────────────────────

    public static decimal MedianFeeFor(DateTime gameLocalDateTime) => EntryFor(gameLocalDateTime).Median;
    public static decimal MeanFeeFor(DateTime gameLocalDateTime) => EntryFor(gameLocalDateTime).Mean;
    public static decimal MaxFeeFor(DateTime gameLocalDateTime) => EntryFor(gameLocalDateTime).Max;

    public static decimal MedianFeeAt(long blockTimestampMs) => EntryAt(blockTimestampMs).Median;
    public static decimal MeanFeeAt(long blockTimestampMs) => EntryAt(blockTimestampMs).Mean;
    public static decimal MaxFeeAt(long blockTimestampMs) => EntryAt(blockTimestampMs).Max;

    // Date-aware clamp (D-ND7.8, replacing the legacy ClampOrDefault): any value outside the day's
    // [median, max] band → the day's median. Never throws. Pre-birth (or no schedule) → 0.
    public static decimal ClampOrDefaultFor(decimal fee, DateTime gameLocalDateTime)
    {
        FeeDay day = EntryFor(gameLocalDateTime);
        if (!IsActive(gameLocalDateTime))
        {
            return 0m;
        }

        return (fee >= day.Median && fee <= day.Max) ? fee : day.Median;
    }

    private static FeeDay EntryFor(DateTime gameLocalDateTime)
    {
        if (!_hasSchedule)
        {
            WarnNoScheduleOnce();
            return default;
        }

        int index = (int)(gameLocalDateTime.Date - _firstReplayDayLocal).TotalDays;
        return EntryAtIndex(index);
    }

    private static FeeDay EntryAt(long blockTimestampMs)
    {
        if (!_hasSchedule)
        {
            WarnNoScheduleOnce();
            return default;
        }

        long deltaMs = blockTimestampMs - _firstReplayDayMs;
        int index = deltaMs < 0 ? -1 : (int)(deltaMs / 86_400_000L);
        return EntryAtIndex(index);
    }

    private static FeeDay EntryAtIndex(int index)
    {
        if (index < 0)
        {
            return default; // pre-birth: fee-free
        }

        return _days[Math.Min(index, _days.Length - 1)]; // past the end: freeze at the last day
    }

    private static void WarnNoScheduleOnce()
    {
        if (_warnedNoSchedule)
        {
            return;
        }

        _warnedNoSchedule = true;
        GD.PushWarning("[NetworkFeePolicy] No fee schedule pushed (network dataset failed to load?) — replaying fee-free.");
    }
}
