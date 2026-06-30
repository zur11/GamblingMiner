using Godot;
using System;
using Scripts.Finance;
using Scripts.User;
using Scripts.Game;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;

public partial class UserStatsService : Node
{
    private static readonly TimeSpan HighFrequencyStatsEmitInterval = TimeSpan.FromMilliseconds(250);
    private static bool EnableHistoryPersistence = true;
    public event Action<UserBettingStats> StatsChanged;

    public UserBettingStats Stats { get; private set; }
    public BetHistoryRepository BetHistory { get; private set; }
    private CalendarTimeService _calendarTimeService;
    private bool _highFrequencyMode;
    private DateTime _lastStatsEmitUtc = DateTime.MinValue;
    private bool _hasPendingStatsChange;

    public override void _Ready()
    {
        Stats = new UserBettingStats();
        if (EnableHistoryPersistence)
        {
            BetHistory = new BetHistoryRepository(BetHistoryRepository.ResolveDefaultPath());
            BetHistory.EnsureAllChunksLoaded();
            RebuildStatsFromLoadedHistory();
        }
        else
        {
            BetHistory = null;
        }
        _calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
        ApplyClockJumpToFarthestFutureIfAny();
    }

    private void ApplyClockJumpToFarthestFutureIfAny()
    {
        if (_calendarTimeService == null || BetHistory == null)
        {
            return;
        }

        DateTime? latestUtc = BetHistory.GetLatestTimestampUtc();
        if (!latestUtc.HasValue)
        {
            return;
        }

        DateTime latestLocal = latestUtc.Value.ToLocalTime();
        DateTime nowLocal = DateTime.Now;

        if (latestLocal > nowLocal)
        {
            _calendarTimeService.SetLocalDateTime(latestLocal);
        }
    }

    public void OnBetExecutedRegisterBet(string gameId, BetTransactionEvent bet)
    {
        if (EnableHistoryPersistence && BetHistory != null)
        {
            var record = new BetRecord
            {
                GameId = gameId,
                TimestampUtc = DateTime.SpecifyKind(bet.Timestamp, DateTimeKind.Utc),
                Outcome = bet.IsWin ? BetOutcome.Win : BetOutcome.Loss,
                BetAmount = bet.BetAmount,
                NetAmount = bet.CreditedProfit,
                BalanceAfter = bet.BalanceAfter,
                Roll = bet.Roll,
                Chance = bet.Chance,
                Multiplier = bet.Multiplier,
                IsHigh = bet.IsHigh
            };

            BetHistory.Add(record);
        }

        Stats.RegisterBet(gameId, bet);
        EmitStatsChangedIfNeeded();
    }

    public void RegisterDeposit(decimal amount, decimal balanceAfter, DateTime timestampUtc)
    {
        if (EnableHistoryPersistence && BetHistory != null)
        {
            var depositRecord = new DepositRecord
            {
                Amount = amount,
                BalanceAfter = balanceAfter,
                TimestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc)
            };
            BetHistory.AddDeposit(depositRecord);
        }

        Stats.RegisterDeposit();
        EmitStatsChangedImmediate();
    }

    public void RegisterSource(IBetEventSource source)
    {
        source.BetExecuted += OnBetExecutedRegisterBet;
    }

    public void FlushHistory()
    {
        if (EnableHistoryPersistence)
        {
            BetHistory?.Flush();
        }
    }

    public void RollbackHistoryToUtc(DateTime checkpointUtc)
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return;
        }

        BetHistory.EnsureAllChunksLoaded();
        BetHistory.RollbackToUtc(checkpointUtc);
        RebuildStatsFromLoadedHistory();
        EmitStatsChangedImmediate();
    }

    public void EnsureFullHistoryLoaded()
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return;
        }

        BetHistory.EnsureAllChunksLoaded();
    }

    public decimal GetLatestKnownBalance(decimal fallbackBalance)
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return fallbackBalance;
        }

        return BetHistory.GetLatestKnownBalance(fallbackBalance);
    }

    public TimeBasedBetStats GetLoadedHistoryStats()
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return new TimeBasedBetStats();
        }

        DateTime? latestUtc = BetHistory.GetLatestTimestampUtc();
        if (!latestUtc.HasValue)
        {
            return new TimeBasedBetStats();
        }

        return BetHistory.BuildStatsUpToUtc(latestUtc.Value);
    }

    public void SetHighFrequencyMode(bool enabled)
    {
        bool wasEnabled = _highFrequencyMode;
        _highFrequencyMode = enabled;
        if (EnableHistoryPersistence)
        {
            BetHistory?.SetSaveSuspended(enabled);
        }
        if (wasEnabled && !enabled)
        {
            if (_hasPendingStatsChange)
            {
                EmitStatsChangedImmediate();
            }
        }
    }

    private void EmitStatsChangedIfNeeded()
    {
        if (!_highFrequencyMode)
        {
            EmitStatsChangedImmediate();
            return;
        }

        _hasPendingStatsChange = true;
        DateTime now = DateTime.UtcNow;
        if ((now - _lastStatsEmitUtc) < HighFrequencyStatsEmitInterval)
        {
            return;
        }

        EmitStatsChangedImmediate();
    }

    private void EmitStatsChangedImmediate()
    {
        _hasPendingStatsChange = false;
        _lastStatsEmitUtc = DateTime.UtcNow;
        StatsChanged?.Invoke(Stats);
    }

    public IReadOnlyList<BetRecord> GetBetsForCalendarDay(DateTime localDate, TimeZoneInfo timezone = null)
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return Array.Empty<BetRecord>();
        }

        return BetHistory.GetBetsForCalendarDay(localDate, timezone);
    }

    public IReadOnlyList<TimeBucketSummary> GetTimeBucketSummaries(TimeBucketType bucketType)
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return Array.Empty<TimeBucketSummary>();
        }

        return BetHistory.BuildSummaries(bucketType);
    }

    public decimal GetBalanceAtOrBefore(DateTime localDateTime, TimeZoneInfo timezone = null)
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            // With history disabled, we can't time-travel reconstruct. Caller should use current wallet/bankroll state.
            return 1.00000000m;
        }

        TimeZoneInfo tz = timezone ?? TimeZoneInfo.Local;
        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        return BetHistory.GetBalanceAtOrBeforeUtc(utc);
    }

    public TimeBasedBetStats GetStatsUpTo(DateTime localDateTime, TimeZoneInfo timezone = null)
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return new TimeBasedBetStats();
        }

        TimeZoneInfo tz = timezone ?? TimeZoneInfo.Local;
        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        return BetHistory.BuildStatsUpToUtc(utc);
    }

    private void RebuildStatsFromLoadedHistory()
    {
        Stats = new UserBettingStats();
        if (BetHistory == null)
        {
            return;
        }

        var timeline = new List<(DateTime TimestampUtc, bool IsDeposit, DepositRecord Deposit, BetRecord Bet)>();
        foreach (DepositRecord d in BetHistory.Deposits)
        {
            timeline.Add((d.TimestampUtc, true, d, null));
        }
        foreach (BetRecord b in BetHistory.Records)
        {
            timeline.Add((b.TimestampUtc, false, null, b));
        }

        foreach (var item in timeline.OrderBy(x => x.TimestampUtc))
        {
            if (item.IsDeposit)
            {
                Stats.RegisterDeposit();
                continue;
            }

            BetRecord b = item.Bet;
            var evt = new BetTransactionEvent(
                BetAmount: b.BetAmount,
                Profit: b.NetAmount,
                CreditedProfit: b.NetAmount,
                BalanceAfter: b.BalanceAfter,
                IsWin: b.Outcome == BetOutcome.Win,
                Roll: b.Roll,
                Chance: b.Chance,
                Multiplier: b.Multiplier,
                IsHigh: b.IsHigh,
                Timestamp: DateTime.SpecifyKind(b.TimestampUtc, DateTimeKind.Utc));
            Stats.RegisterBet(b.GameId, evt);
        }
    }
}
