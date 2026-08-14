using Godot;
using System;
using Scripts.Finance;
using Scripts.User;
using Scripts.Game;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public partial class UserStatsService : Node
{
    private static readonly TimeSpan HighFrequencyStatsEmitInterval = TimeSpan.FromMilliseconds(250);
    private static bool EnableHistoryPersistence = true;
    public event Action<UserBettingStats> StatsChanged;

    public UserBettingStats Stats { get; private set; }
    public BetHistoryRepository BetHistory { get; private set; }
    private bool _highFrequencyMode;
    private DateTime _lastStatsEmitUtc = DateTime.MinValue;
    private bool _hasPendingStatsChange;

    // ── Lifetime rollup (mini-plan 03) ──────────────────────────────────────────
    // Stats is rebuilt by scanning the journal, and the journal retains only its newest 200,000 bets —
    // so scanning stops being a LIFETIME measurement the moment the first chunk is pruned. The rollup is
    // the running total that survives its own source being deleted. See §6.2 of the plan.
    private const string RollupPath = "user://bet_stats_rollup.json";
    private static readonly JsonSerializerOptions RollupJsonOptions = new() { WriteIndented = true };
    // Bets settle faster than any disk can keep up with; the rollup follows the journal's own dirty-flag
    // discipline and is flushed with it (FlushHistory) and at every block commit.
    private bool _rollupDirty;

    public BetStatsRollup Rollup { get; private set; } = new();

    // While the journal still holds EVERY bet, a full scan is authoritative and the rollup is re-seeded
    // from it — self-healing, and it keeps the two from drifting apart unnoticed. Once anything has been
    // pruned the scan can no longer see the whole history, and the rollup becomes the only truth.
    public bool RollupIsAuthoritative { get; private set; }

    public override void _Ready()
    {
        Stats = new UserBettingStats();
        if (EnableHistoryPersistence)
        {
            BetHistory = new BetHistoryRepository(BetHistoryRepository.ResolveDefaultPath());
            BetHistory.EnsureAllChunksLoaded();

            bool hadRollupFile = FileAccess.FileExists(RollupPath);
            LoadRollup();
            bool pruned = BetHistory.HasPrunedHistory();

            // FIRST RUN with this feature: there is no rollup yet, so it must be seeded by scanning —
            // even in a world that has already pruned, where the scan cannot see the whole history. That
            // world's totals then start from the retained window rather than from zero, which is the best
            // available answer; it is marked incomplete so nothing downstream calls it a lifetime figure.
            // Only AFTER a rollup exists does pruning make it authoritative and forbid re-seeding.
            RollupIsAuthoritative = pruned && hadRollupFile;
            if (!hadRollupFile && pruned)
            {
                Rollup.IsComplete = false;
                Rollup.SeededAtUtc = DateTime.UtcNow;
                GD.PrintErr(
                    "[UserStatsService] Seeding the lifetime rollup from an ALREADY-PRUNED journal: bets in " +
                    "deleted chunks cannot be recovered and are not counted. Totals are marked incomplete " +
                    "and are accurate from now on.");
            }

            RebuildStatsFromLoadedHistory();
        }
        else
        {
            BetHistory = null;
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

        // Maintained on EVERY settled bet, in every mode — a rollup that only starts counting once
        // pruning begins has already lost the pruned bets (§6.2).
        Rollup.RegisterBet(gameId, bet.Chance, bet.IsWin, bet.BetAmount, bet.CreditedProfit);
        _rollupDirty = true;

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
            SaveRollupIfDirty();
        }
    }

    // ── Rollup persistence ──────────────────────────────────────────────────────

    private void LoadRollup()
    {
        if (!FileAccess.FileExists(RollupPath))
        {
            return;
        }

        try
        {
            using FileAccess file = FileAccess.Open(RollupPath, FileAccess.ModeFlags.Read);
            BetStatsRollup loaded = JsonSerializer.Deserialize<BetStatsRollup>(file.GetAsText(), RollupJsonOptions);
            if (loaded != null)
            {
                loaded.Segments ??= new Dictionary<string, BetStatsRollup.SegmentRuns>();
                Rollup = loaded;
            }
        }
        catch (Exception ex)
        {
            // Loud, never silent: past the pruning boundary this file is the ONLY record of the pruned
            // bets, so losing it loses history permanently (§40.5's durability standard, INC-001).
            GD.PrintErr($"[UserStatsService] Could not read {RollupPath} — lifetime totals may be incomplete: {ex.Message}");
        }
    }

    public void SaveRollupIfDirty()
    {
        if (!EnableHistoryPersistence || !_rollupDirty)
        {
            return;
        }

        _rollupDirty = false;
        try
        {
            using FileAccess file = FileAccess.Open(RollupPath, FileAccess.ModeFlags.Write);
            file?.StoreString(JsonSerializer.Serialize(Rollup, RollupJsonOptions));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[UserStatsService] Could not write {RollupPath}: {ex.Message}");
        }
    }

    // Replaces the rollup wholesale — the checkpoint restore path (a block is the only commit, so the
    // rollup rolls back with everything else) and the pre-genesis reset.
    public void ApplyRollupSnapshot(BetStatsRollup snapshot)
    {
        Rollup = snapshot?.Clone() ?? new BetStatsRollup();
        _rollupDirty = true;
        SaveRollupIfDirty();
    }

    // Called by BlockSessionCheckpointService at each block. Flushing here too keeps the standalone file
    // and the checkpoint in agreement: previously the file was only written from FlushHistory, which the
    // DELEGATED autobet never calls, so it sat at whatever boot had last computed while the in-memory
    // total ran ahead. The checkpoint restore corrected it on the next launch, so nothing broke — but two
    // persisted copies where one is routinely wrong is the §39.16 rule-1 trap, and a block is exactly the
    // right moment to write, since a block is the only commit.
    public BetStatsRollup CaptureRollupSnapshot()
    {
        SaveRollupIfDirty();
        return Rollup.Clone();
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

    // Used only for the pre-genesis reset (no block has ever been mined): unlike RollbackHistoryToUtc, there
    // is no legitimate boundary to partially keep — everything before the player's first real block is
    // discardable by definition, so this clears unconditionally instead of comparing timestamps. Avoids the
    // class of bug where a record's timestamp exactly equals the reset boundary (the very first bet/deposit
    // after any reset reads a clock that hasn't advanced yet) and so survives a `>`-based rollback it should
    // not have (see OQ-BP.11).
    public void ClearAllHistory()
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return;
        }

        BetHistory.EnsureAllChunksLoaded();
        BetHistory.ClearAll();

        // The rollup is zeroed EXPLICITLY rather than left to the rebuild below: past the pruning boundary
        // the rebuild deliberately does not touch it, so a pre-genesis reset would otherwise leave lifetime
        // totals from a world that no longer exists. Clearing everything also un-prunes by definition —
        // there is nothing left to have pruned — so the rollup goes back to being complete and re-seedable.
        Rollup.Reset();
        RollupIsAuthoritative = false;
        _rollupDirty = true;
        SaveRollupIfDirty();

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

    // SF.4B.5: the most recent `max` bet records from the centralized persistent history, oldest-first (so a
    // consumer's TakeLast / newest-first prepend works). Used to seed DiceGame's in-game bet-history list on
    // entry so it reproduces the recent history instead of starting empty. Loads all chunks once (cached).
    public IReadOnlyList<BetRecord> GetRecentBets(int max)
    {
        if (!EnableHistoryPersistence || BetHistory == null || max <= 0)
        {
            return Array.Empty<BetRecord>();
        }

        BetHistory.EnsureAllChunksLoaded();
        IReadOnlyList<BetRecord> records = BetHistory.Records;
        if (records.Count <= max)
        {
            return records;
        }

        return records.Skip(records.Count - max).ToList();
    }

    // The floor of the REPLAY WINDOW: the oldest bet still on disk (mini-plan 03 §6.6). Retention is a
    // storage decision the player never made and cannot see, and its only visible consequence is history
    // that isn't there — so the limit has to be expressed in the units the player thinks in (a game date),
    // and picking a date below it has to snap rather than silently open an empty replay.
    // Null when no bet has been recorded yet. Records are kept in chronological order, so this is [0].
    public DateTime? GetOldestRetainedBetUtc()
    {
        if (!EnableHistoryPersistence || BetHistory == null)
        {
            return null;
        }

        IReadOnlyList<BetRecord> records = BetHistory.Records;
        return records.Count > 0 ? records[0].TimestampUtc : null;
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

        // Re-seed the rollup from the same scan WHILE the scan can still see everything. Past the pruning
        // boundary this must not happen — a rebuild there would overwrite the lifetime totals with a
        // "last 200,000 bets" figure, which is the very defect the rollup exists to fix.
        bool reseedRollup = !RollupIsAuthoritative;
        if (reseedRollup)
        {
            Rollup.Reset();
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

            if (reseedRollup)
            {
                Rollup.RegisterRecord(b);
            }
        }

        if (reseedRollup)
        {
            _rollupDirty = true;
            SaveRollupIfDirty();
        }
    }
}
