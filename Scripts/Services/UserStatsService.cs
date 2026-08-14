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

            bool hadRollupFile = FileAccess.FileExists(RollupPath);
            LoadRollup();

            // STAGE 1 (mini-plan 03 D1): with a rollup on disk, boot reads NOTHING from the journal.
            // Every figure Stats holds is a running value the rollup already maintains with identical
            // arithmetic, so it is reconstructed instead of replayed — which is what removes INC-001's
            // remaining cost. Retention bounded what was WRITTEN; this is what finally bounds what is READ.
            // Only the first-run seeding path below still needs the records.
            if (hadRollupFile)
            {
                RollupIsAuthoritative = true;
                Stats = UserBettingStats.FromRollup(Rollup);
                return;
            }

            BetHistory.EnsureAllChunksLoaded();

            // ONCE SEEDED, THE ROLLUP IS NEVER RE-SEEDED. This replaces an earlier "self-healing" design
            // that re-derived it from the journal whenever nothing appeared to be pruned, and the reason
            // it had to go is worth keeping: **the journal cannot report its own completeness.**
            // RollbackToUtc REWRITES the journal from scratch, recreating the base file and renumbering
            // chunks from 1 — so after any rollback a journal that has lost 10,000 pruned bets is
            // indistinguishable from one that never lost any. Every structural test for "has this been
            // pruned?" fails on that, which meant the self-heal would eventually re-derive a SHORT total
            // over a correct one and destroy exactly the history the rollup exists to preserve.
            //
            // A running total is only ever adjusted by the thing that owns the world's timeline: the
            // checkpoint (a block is the only commit). Nothing else may touch it.
            //
            // Reaching here means there was no rollup file, so this is the one and only seeding. Scanning
            // is the only source available and it can see only what retention still holds — so unless this
            // world has never recorded a bet, the seed is a FLOOR rather than a lifetime figure, and says
            // so rather than quietly presenting itself as one.
            Rollup.SeededAtUtc = DateTime.UtcNow;
            Rollup.IsComplete = BetHistory.Records.Count == 0;
            if (!Rollup.IsComplete)
            {
                GD.PrintErr(
                    "[UserStatsService] Seeding the lifetime rollup by scanning an EXISTING journal. " +
                    "Anything retention already deleted cannot be counted, so these totals are a floor, " +
                    "not a lifetime figure. They are exact from this point forward.");
            }

            RebuildStatsFromLoadedHistory();
            RollupIsAuthoritative = true; // seeded — from here on nothing may re-derive it
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

        Rollup.RegisterDeposit();   // zeroes the since-deposit window, exactly as Stats does
        _rollupDirty = true;

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

        // This one MUST load everything, and the reason is worth stating so nobody "optimises" it later:
        // RollbackToUtc trims in memory and then REBUILDS THE JOURNAL FROM WHAT IS IN MEMORY. Loading only
        // the tail chunks would rewrite the journal from that tail alone and delete every older chunk —
        // turning a rollback of a few bets into the loss of the entire retained history.
        // Once per process (DiceGame's checkpoint restore is guarded), and only when a checkpoint exists.
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

        // No load first: ClearAll empties the in-memory state and rewrites the journal from it, so reading
        // ~200,000 records in order to discard them was pure cost. (It also rewrites from whatever IS in
        // memory — which is exactly why the rollback path below must still load everything.)
        BetHistory.ClearAll();

        // The rollup is zeroed EXPLICITLY rather than left to the rebuild below: past the pruning boundary
        // the rebuild deliberately does not touch it, so a pre-genesis reset would otherwise leave lifetime
        // totals from a world that no longer exists. Clearing everything also un-prunes by definition —
        // there is nothing left to have pruned — so the rollup goes back to being complete and re-seedable.
        Rollup.Reset();
        Rollup.IsComplete = true;   // nothing exists to have been lost
        Rollup.SeededAtUtc = null;
        RollupIsAuthoritative = true; // it is correct and owns itself; never re-derive from the journal
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

        // Only the NEWEST records are ever wanted here (DiceGame seeds its on-screen list with ~100), and a
        // chunk holds 10,000 — so the newest chunk alone always satisfies it. Loading every chunk to take
        // the tail was the second half of INC-001's read cost.
        //
        // Guarded because LoadLatestChunkOnly RESETS in-memory state: if a consumer has already loaded the
        // full history (the explorer does), throwing it away here would force that consumer to re-read
        // everything on its next refresh — turning a saving into a cost.
        if (BetHistory.Records.Count < max)
        {
            BetHistory.LoadLatestChunkOnly();
        }

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

        // Seeds the rollup ONLY on its very first creation (RollupIsAuthoritative is false exactly then).
        // Every later call — a checkpoint rollback, a pre-genesis clear — leaves it alone, because the
        // journal cannot report its own completeness and a re-derivation would silently replace a correct
        // running total with whatever retention happens to still hold. See the note in _Ready.
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
