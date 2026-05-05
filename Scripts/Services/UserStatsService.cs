using Godot;
using System;
using Scripts.Finance;
using Scripts.User;
using Scripts.Game;
using Scripts.History;
using System.Collections.Generic;

public partial class UserStatsService : Node
{
    public event Action<UserBettingStats> StatsChanged;

    public UserBettingStats Stats { get; private set; }
    public BetHistoryRepository BetHistory { get; private set; }

    public override void _Ready()
    {
        Stats = new UserBettingStats();
        BetHistory = new BetHistoryRepository(BetHistoryRepository.ResolveDefaultPath());
        BetHistory.Load();
    }

    public void OnBetExecutedRegisterBet(string gameId, BetTransactionEvent bet)
    {
        var record = new BetRecord
        {
            GameId = gameId,
            TimestampUtc = DateTime.SpecifyKind(bet.Timestamp, DateTimeKind.Utc),
            Outcome = bet.IsWin ? BetOutcome.Win : BetOutcome.Loss,
            BetAmount = bet.BetAmount,
            NetAmount = bet.Profit,
            BalanceAfter = bet.BalanceAfter
        };

        BetHistory.Add(record);

        Stats.RegisterBet(gameId, bet);
        StatsChanged?.Invoke(Stats);
    }

    public void RegisterDeposit(decimal amount, decimal balanceAfter, DateTime timestampUtc)
    {
        var depositRecord = new DepositRecord
        {
            Amount = amount,
            BalanceAfter = balanceAfter,
            TimestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc)
        };
        BetHistory.AddDeposit(depositRecord);

        Stats.RegisterDeposit();
        StatsChanged?.Invoke(Stats);
    }

    public void RegisterSource(IBetEventSource source)
    {
        source.BetExecuted += OnBetExecutedRegisterBet;
    }

    public IReadOnlyList<BetRecord> GetBetsForCalendarDay(DateTime localDate, TimeZoneInfo timezone = null)
    {
        return BetHistory.GetBetsForCalendarDay(localDate, timezone);
    }

    public IReadOnlyList<TimeBucketSummary> GetTimeBucketSummaries(TimeBucketType bucketType)
    {
        return BetHistory.BuildSummaries(bucketType);
    }

    public decimal GetBalanceAtOrBefore(DateTime localDateTime, TimeZoneInfo timezone = null)
    {
        TimeZoneInfo tz = timezone ?? TimeZoneInfo.Local;
        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        return BetHistory.GetBalanceAtOrBeforeUtc(utc);
    }

    public TimeBasedBetStats GetStatsUpTo(DateTime localDateTime, TimeZoneInfo timezone = null)
    {
        TimeZoneInfo tz = timezone ?? TimeZoneInfo.Local;
        DateTime utc = TimeZoneInfo.ConvertTimeToUtc(localDateTime, tz);
        return BetHistory.BuildStatsUpToUtc(utc);
    }
}
