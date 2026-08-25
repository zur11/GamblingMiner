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
	// INC-004 A-F1 — the rollup is written .tmp → rename, never truncate-and-stream. FileAccess.Open(Write)
	// TRUNCATES AT OPEN, so the exposure window is not "mid-write" but from open until StoreString returns;
	// a kill anywhere in it left a zero-byte or half-written file. Since this file is, past the pruning
	// boundary, the ONLY record of the pruned bets, that window destroyed history permanently.
	private const string RollupTempPath = "user://bet_stats_rollup.json.tmp";
	// A damaged file is PRESERVED rather than replaced. It is evidence, and the first thing anyone will ask
	// is what it contained; the wipe-before-archive lesson (INC-003) applies to a single file too.
	private const string RollupCorruptPath = "user://bet_stats_rollup.json.corrupt";
	private static readonly JsonSerializerOptions RollupJsonOptions = new() { WriteIndented = true };

	// INC-004 A-F2 — set when the rollup on disk could not be read. While true, NOTHING may persist the
	// in-memory rollup: a failed load leaves it at `new()` (zeroed, and claiming IsComplete = true), and
	// the old code wrote that back over the good copy on the very next settled bet. Guarding the READER
	// was never enough — the guard belongs on the WRITER. See ProjectDesignManual Ch. 40 and INC-004.
	private bool _rollupLoadFailed;
	private bool _rollupSaveBlockedReported;
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
		AnnounceSentinelArming();
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

	// ── Mini-plan 05 D1 + D3 — the journal asserts that it belongs to ONE actor ─────────────────────
	// Mini-plan 04 §13 proved from the data that two independent wallets had been writing here: two
	// balance lines, each internally exact to the satoshi, each with its own martingale progression,
	// interleaved second by second. It went unnoticed for at least three in-game days and was found by
	// eye, from a replay built for something else, because nothing ever checked a property the records
	// already carried.
	//
	// `BalanceAfter[i] == BalanceAfter[i-1] + NetAmount[i]` is one subtraction per bet against fields the
	// journal has always held. `source` names the writer, so a break says WHO as well as WHETHER — the
	// question the journal could not answer, because it records no author.
	public const string SourceDiceGame = "DiceGame";
	public const string SourceSimulation = "SimulationService";

	private decimal _lastRegisteredBalanceAfter;
	private bool _hasLastRegisteredBalance;
	private string _lastRegisteredSource;
	private string _lastDiscontinuityReason;
	private int _continuityBreaksReported;
	private const int MaxContinuityBreaksReported = 20;
	private readonly Dictionary<string, int> _registrationsBySource = new();

	/// <summary>
	/// Declares that the next registered bet will legitimately break balance continuity — an auto-recharge,
	/// a manual transfer, a wallet reseed, a time-travel balance set.
	///
	/// The exceptions have to ANNOUNCE THEMSELVES rather than be inferred, and that is what makes the check
	/// worth having: a diagnostic whose false positives are routine gets muted within a week, while one that
	/// is silent by construction keeps its authority. Silence here means a real anomaly.
	/// </summary>
	public void NoteBalanceDiscontinuity(string reason)
	{
		// Drops the baseline rather than setting a "skip once" flag. The next registered bet re-seeds it
		// instead of being compared across the jump, which makes repeated declarations before a single bet
		// harmless — and leaves no pending token that could silently absorb a real break much later.
		_hasLastRegisteredBalance = false;
		_lastDiscontinuityReason = string.IsNullOrEmpty(reason) ? "unspecified" : reason;
	}

	// A SILENT SENTINEL IS AMBIGUOUS, and that ambiguity is the whole reason this line exists.
	// AssertSingleActorJournal is Conditional("DEBUG"), so "no discontinuity was reported" and "the check
	// was never compiled in" look identical from the console — and mini-plan 06's P5' expects silence as a
	// RESULT, which makes the two impossible to tell apart at exactly the moment it matters most.
	//
	// Deliberately NOT Conditional: its job in a Release build is to say so. One line, once, at boot.
	private static void AnnounceSentinelArming()
	{
		bool debug = OS.IsDebugBuild();
		GD.Print(debug
			? "[Build] DEBUG — the bet-journal continuity sentinel is ARMED; silence from it is evidence."
			: "[Build] RELEASE — the bet-journal continuity sentinel is COMPILED OUT; silence from it " +
			  "means nothing. Do not run a mini-plan 06 reproduction on this build (B1.1).");
	}

	[System.Diagnostics.Conditional("DEBUG")]
	private void AssertSingleActorJournal(BetTransactionEvent bet, string source)
	{
		source ??= "unknown";
		_registrationsBySource.TryGetValue(source, out int seen);
		_registrationsBySource[source] = seen + 1;

		if (_hasLastRegisteredBalance)
		{
			decimal expected = Money.Normalize(_lastRegisteredBalanceAfter + bet.CreditedProfit);
			if (expected != Money.Normalize(bet.BalanceAfter) &&
				_continuityBreaksReported < MaxContinuityBreaksReported)
			{
				_continuityBreaksReported++;
				GD.PrintErr(string.Format(
					System.Globalization.CultureInfo.InvariantCulture,
					"[BetJournal] UNDECLARED balance discontinuity #{0}: previous BalanceAfter {1:F8} " +
					"+ net {2:F8} = {3:F8}, but this bet reports {4:F8} (delta {5:F8}). " +
					"Written by '{6}', previous by '{7}'. Counts so far: {8}. Last declared jump: {9}. " +
					"Two writers on one journal is mini-plan 05's whole question — see its §1.2.",
					_continuityBreaksReported,
					_lastRegisteredBalanceAfter,
					bet.CreditedProfit,
					expected,
					bet.BalanceAfter,
					bet.BalanceAfter - expected,
					source,
					_lastRegisteredSource ?? "none",
					DescribeSourceCounts(),
					_lastDiscontinuityReason ?? "none"));

				if (_continuityBreaksReported == MaxContinuityBreaksReported)
				{
					GD.PrintErr("[BetJournal] Continuity-break reporting capped; further breaks are silent. " +
								"The bet journal itself remains the full record.");
				}
			}
		}

		_lastRegisteredBalanceAfter = bet.BalanceAfter;
		_hasLastRegisteredBalance = true;
		_lastRegisteredSource = source;
	}

	private string DescribeSourceCounts()
	{
		var parts = new List<string>();
		foreach (KeyValuePair<string, int> entry in _registrationsBySource)
		{
			parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
				"{0}={1}", entry.Key, entry.Value));
		}

		return string.Join(" ", parts);
	}

	public void OnBetExecutedRegisterBet(string gameId, BetTransactionEvent bet, string source = "unknown")
	{
		// The default is deliberately "unknown" rather than a plausible name: a call site nobody tagged is
		// exactly what this is hunting, and it must be able to say so.
		AssertSingleActorJournal(bet, source);

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
		// Mini-plan 05 D3: money arriving from outside the betting loop IS the legitimate discontinuity,
		// and this method is already the point where every one of them announces itself. Declaring it here
		// rather than at each recharge/transfer call site means a new funding path inherits the exemption
		// by construction instead of tripping a false alarm nobody wired it into.
		NoteBalanceDiscontinuity("deposit");

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

	// Mini-plan 05 §2 established this has NO callers. It is kept — rather than deleted — precisely because
	// it is a route into the journal that nothing currently guards, and the investigation needs to be able
	// to tell whether something started using it. Anything arriving this way is tagged as such, so it
	// cannot hide inside either of the two known writers' counts.
	public const string SourceRegisteredEventSource = "RegisterSource";

	public void RegisterSource(IBetEventSource source)
	{
		GD.PrintErr("[BetJournal] UserStatsService.RegisterSource was called — it had no callers when " +
					"mini-plan 05 was written. Every bet arriving through it is a third writer into the " +
					"player's journal. See the plan's §2.");
		source.BetExecuted += (gameId, bet) =>
			OnBetExecutedRegisterBet(gameId, bet, SourceRegisteredEventSource);
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
			if (file == null)
			{
				NoteRollupLoadFailure($"could not open the file ({FileAccess.GetOpenError()})");
				return;
			}

			BetStatsRollup loaded = JsonSerializer.Deserialize<BetStatsRollup>(file.GetAsText(), RollupJsonOptions);
			if (loaded == null)
			{
				// A file holding the literal `null` parses without throwing and yields null. The old code
				// treated that as "nothing to do" and fell through with a zeroed rollup — one of the three
				// damage modes reproduced for INC-004, and the only one that raised no exception at all.
				NoteRollupLoadFailure("the file parsed to null");
				return;
			}

			loaded.Segments ??= new Dictionary<string, BetStatsRollup.SegmentRuns>();
			Rollup = loaded;
		}
		catch (Exception ex)
		{
			// Loud, never silent: past the pruning boundary this file is the ONLY record of the pruned
			// bets, so losing it loses history permanently (§40.5's durability standard, INC-001).
			NoteRollupLoadFailure(ex.Message);
		}
	}

	// INC-004 A-F2. Three things, in this order, and each of them was missing:
	//   1. LATCH — so the writer below refuses to run. A `Try`-shaped read that returns a zeroed object
	//      and lets the caller carry on is a promise the code does not keep.
	//   2. PRESERVE the damaged file, once. It is the only evidence of what was lost, and overwriting it
	//      with a second failure would destroy the first one's contents.
	//   3. PushError, not PrintErr — this is a data-loss event, and it should stop a developer rather
	//      than scroll past them.
	private void NoteRollupLoadFailure(string detail)
	{
		_rollupLoadFailed = true;
		PreserveCorruptRollupFile();

		GD.PushError(
			$"[UserStatsService] Could not read {RollupPath} — {detail}. The lifetime rollup is the ONLY " +
			$"record of bets retention has already deleted, so it will NOT be overwritten: persistence is " +
			$"disabled until a block checkpoint restores it. A copy of the damaged file is at " +
			$"{RollupCorruptPath}. See INCIDENT_LOG INC-004.");
	}

	// Never overwrites an existing preserved copy: the FIRST failure holds the most history, and a later
	// one arriving on an already-zeroed world would replace real evidence with none.
	private void PreserveCorruptRollupFile()
	{
		if (!FileAccess.FileExists(RollupPath) || FileAccess.FileExists(RollupCorruptPath))
		{
			return;
		}

		try
		{
			System.IO.File.Copy(
				ProjectSettings.GlobalizePath(RollupPath),
				ProjectSettings.GlobalizePath(RollupCorruptPath));
		}
		catch (Exception ex)
		{
			GD.PushError($"[UserStatsService] Could not preserve the damaged rollup: {ex.Message}");
		}
	}

	public void SaveRollupIfDirty()
	{
		if (!EnableHistoryPersistence || !_rollupDirty)
		{
			return;
		}

		// INC-004 A-F2 — the guard that belongs on the WRITER. In memory the rollup is zeroed and claims
		// to be complete; writing it would replace a recoverable file with an authoritative-looking lie.
		// Reported once: this runs at every block, and an error repeated every block is an error nobody
		// reads.
		if (_rollupLoadFailed)
		{
			if (!_rollupSaveBlockedReported)
			{
				_rollupSaveBlockedReported = true;
				GD.PushError(
					$"[UserStatsService] Refusing to persist the lifetime rollup: it never loaded, so the " +
					$"in-memory copy is empty and would overwrite the real one. {RollupPath} is untouched.");
			}

			return;
		}

		try
		{
			// INC-004 A-F1 — atomic: serialize, write the temp file, close it, then rename over the real
			// one. The rename is the commit; until it happens the good file is intact, and a crash at any
			// point leaves either the old file or the new one, never half of either.
			string payload = JsonSerializer.Serialize(Rollup, RollupJsonOptions);

			using (FileAccess file = FileAccess.Open(RollupTempPath, FileAccess.ModeFlags.Write))
			{
				if (file == null)
				{
					// Was `file?.StoreString(...)` — a null handle silently wrote NOTHING and still cleared
					// the dirty flag, so the failure was invisible and the next flush had nothing to retry.
					GD.PushError(
						$"[UserStatsService] Could not open {RollupTempPath} " +
						$"({FileAccess.GetOpenError()}); the rollup was not saved.");
					return;
				}

				file.StoreString(payload);
			}

			System.IO.File.Move(
				ProjectSettings.GlobalizePath(RollupTempPath),
				ProjectSettings.GlobalizePath(RollupPath),
				overwrite: true);

			// Cleared only on success. Previously it was cleared BEFORE the write, so a failed save was
			// never retried — the in-memory total simply ran ahead of the file until the next mutation.
			_rollupDirty = false;
		}
		catch (Exception ex)
		{
			GD.PushError($"[UserStatsService] Could not write {RollupPath}: {ex.Message}");
		}
	}

	// Replaces the rollup wholesale — the checkpoint restore path (a block is the only commit, so the
	// rollup rolls back with everything else) and the pre-genesis reset.
	public void ApplyRollupSnapshot(BetStatsRollup snapshot)
	{
		Rollup = snapshot?.Clone() ?? new BetStatsRollup();

		// INC-004 — THIS IS THE RECOVERY PATH, and it is the reason the writer above can safely refuse to
		// run. A block checkpoint carries its own rollup snapshot, so a world whose rollup file was
		// destroyed gets a real one back at the next restore — a block is the only commit, and it turns
		// out to be the backup as well. Clearing the latch here is what lets persistence resume.
		if (snapshot != null)
		{
			_rollupLoadFailed = false;
			_rollupSaveBlockedReported = false;
		}

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

		// INC-004 — with a failed load the in-memory rollup is zeroed, and capturing it would launder the
		// loss INTO the checkpoint: the restore path would then hand that zero back as authoritative and
		// the corruption would outlive the damaged file. Returning null instead records NO rollup for this
		// block, which the restore already skips (BlockSessionCheckpointService's null guard) — leaving
		// the last good snapshot in place. **Recording nothing beats recording a figure known to be wrong.**
		if (_rollupLoadFailed)
		{
			return null;
		}

		return Rollup.Clone();
	}

	public void RollbackHistoryToUtc(DateTime checkpointUtc)
	{
		// Mini-plan 05 D3: the journal just lost its tail, so the tracked balance no longer describes the
		// last surviving record. Re-seed rather than compare across a rollback.
		NoteBalanceDiscontinuity("history_rollback");

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
		NoteBalanceDiscontinuity("history_cleared");

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
		// INC-004 — the other legitimate way out of a failed load: a pre-genesis reset discards the world's
		// history by definition, so there is nothing left for a damaged file to have held, and the zeroed
		// rollup here is CORRECT rather than a symptom. Persistence resumes.
		_rollupLoadFailed = false;
		_rollupSaveBlockedReported = false;
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

		// Must NOT depend on the journal being loaded: since stage 1, boot reads nothing, so the calendar
		// asked this of an empty in-memory list and reported "no bets recorded yet" for a world holding
		// 215,550 of them. The repository answers it from the oldest segment's first line instead — one
		// short read, and it prefers memory when the journal does happen to be loaded.
		return BetHistory.TryGetOldestRecordTimestampUtc(out DateTime oldest) ? oldest : null;
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
