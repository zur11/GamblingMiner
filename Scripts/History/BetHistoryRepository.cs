using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Scripts.Finance;

namespace Scripts.History
{
	public sealed class BetHistoryRepository
	{
		private const decimal DefaultInitialBalance = 0m;
		private const int FlushEveryMutations = 200;
		private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(3);
		private const int MaxJournalEntriesPerChunkFile = 10000;
		// INC-001 / D-15.28 — retention cap. An ever-growing store with no stated policy is how a 1.13 GB
		// journal (~5.3M records deserialized at EVERY boot) arrived unnoticed; nothing had ever deleted a
		// bet record except a world reset. 20 segments ≈ 200,000 records ≈ 57 MB, which bounds the boot load
		// by construction. The segment currently being filled is not yet on disk when the cap is enforced,
		// so the true ceiling is cap + 1 in-progress file. `0` disables the cap.
		private const int MaxRetainedJournalChunks = 20;
		private const int ChunkIndexDigits = 6;
		private const int MaxPendingJournalEntriesWhileSuspended = 2000;
		private static readonly TimeSpan SuspendedFlushMinInterval = TimeSpan.FromSeconds(0.5);
		private const string EntryTypeBet = "bet";
		private const string EntryTypeDeposit = "deposit";
		private readonly string _filePath;
		private string _activeJournalPath;
		private int _activeJournalLineCount;
		private readonly string _legacySnapshotPath;
		private readonly List<BetRecord> _records = new();
		private readonly List<DepositRecord> _deposits = new();
		// INC-002 / D-16.20 — the loader's duplicate guard. `BetRecord.Id` is a Guid written on every journal
		// line and, until now, read by nothing: it costs O(1) per record to make "the same bet appears twice
		// in _records" structurally impossible, which is the whole class of bug INC-001 produced and that no
		// READER ever defended against. Kept in lockstep with _records — every site that clears, bulk-loads
		// or truncates the list must go through the helpers below. See ProjectDesignManual §40.8.
		private readonly HashSet<string> _recordIds = new(StringComparer.Ordinal);
		private int _duplicateRecordsSkipped;
		private readonly List<HistoryJournalEntry> _pendingJournalEntries = new();
		private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
		private int _mutationsSinceLastSave;
		private DateTime _lastSaveUtc = DateTime.UtcNow;
		private DateTime _lastSuspendedFlushUtc = DateTime.MinValue;
		private bool _saveSuspended;
		private bool _loadedAllChunks;

		public BetHistoryRepository(string filePath)
		{
			_filePath = filePath;
			_activeJournalPath = filePath;
			_legacySnapshotPath = GetLegacySnapshotPath(filePath);
		}

		public IReadOnlyList<BetRecord> Records => _records;
		public IReadOnlyList<DepositRecord> Deposits => _deposits;

		public DateTime? GetLatestTimestampUtc()
		{
			DateTime? latest = null;

			foreach (BetRecord record in _records)
			{
				if (record == null)
				{
					continue;
				}

				DateTime utc = record.TimestampUtc.Kind == DateTimeKind.Utc
					? record.TimestampUtc
					: record.TimestampUtc.ToUniversalTime();

				if (!latest.HasValue || utc > latest.Value)
				{
					latest = utc;
				}
			}

			foreach (DepositRecord deposit in _deposits)
			{
				if (deposit == null)
				{
					continue;
				}

				DateTime utc = deposit.TimestampUtc.Kind == DateTimeKind.Utc
					? deposit.TimestampUtc
					: deposit.TimestampUtc.ToUniversalTime();

				if (!latest.HasValue || utc > latest.Value)
				{
					latest = utc;
				}
			}

			return latest;
		}

		public decimal GetLatestKnownBalance(decimal fallbackBalance)
		{
			DateTime latestTimestamp = DateTime.MinValue;
			decimal? latestBalance = null;

			foreach (DepositRecord deposit in _deposits)
			{
				if (deposit != null && deposit.TimestampUtc >= latestTimestamp)
				{
					latestTimestamp = deposit.TimestampUtc;
					latestBalance = deposit.BalanceAfter;
				}
			}

			foreach (BetRecord record in _records)
			{
				if (record != null && record.TimestampUtc >= latestTimestamp)
				{
					latestTimestamp = record.TimestampUtc;
					latestBalance = record.BalanceAfter;
				}
			}

			return latestBalance ?? fallbackBalance;
		}

		public void Load()
		{
			LoadLatestChunkOnly();
		}

		public void LoadLatestChunkOnly()
		{
			ResetInMemoryState();
			_loadedAllChunks = false;

			InitializeJournalPathsAndLoadLatestChunk();
			ReportDuplicatesSkipped();
			if (_records.Count > 0 || _deposits.Count > 0)
			{
				return;
			}

			if (File.Exists(_legacySnapshotPath))
			{
				LoadFromLegacySnapshot(_legacySnapshotPath);
				NormalizeLegacyRecordsInPlace();
				RebuildJournalFromCurrentState();
			}
		}

		public void EnsureAllChunksLoaded()
		{
			if (_loadedAllChunks)
			{
				return;
			}

			ResetInMemoryState();
			InitializeJournalPathsAndLoadAllChunks();
			ReportDuplicatesSkipped();
			_loadedAllChunks = true;
		}

		private void ResetInMemoryState()
		{
			_records.Clear();
			_recordIds.Clear();
			_duplicateRecordsSkipped = 0;
			_deposits.Clear();
			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;
		}

		// INC-002 — claims `record.Id` for the in-memory list, returning false if that exact record is
		// already loaded. A record whose journal line carried no "Id" cannot be deduplicated: the property
		// initializer mints a FRESH Guid per deserialization, so two copies of such a row look distinct.
		// That only affects pre-journal legacy snapshots; every line the current writer emits carries one.
		private bool TryClaimRecordId(BetRecord record)
		{
			string id = record?.Id;
			if (string.IsNullOrEmpty(id))
			{
				return true;
			}

			return _recordIds.Add(id);
		}

		// Loud on purpose. A duplicate reaching the loader means a writer produced one (INC-001's shape) or a
		// stale segment survived a rebuild — the guard keeps the READINGS honest, it does not fix the file,
		// and a silent guard would hide exactly the condition it was added to detect.
		private void ReportDuplicatesSkipped()
		{
			if (_duplicateRecordsSkipped <= 0)
			{
				return;
			}

			GD.PushWarning($"[BetHistory] Skipped {_duplicateRecordsSkipped} duplicate bet record(s) while loading " +
				$"the journal ({_records.Count} unique kept). Duplicated history inflates every streak-shaped " +
				"statistic — see ProjectDesignManual §40.8 / INCIDENT_LOG INC-002.");
		}

		private void InitializeJournalPathsAndLoadLatestChunk()
		{
			var paths = GetJournalChunkPaths(includeLegacyBaseFile: true);
			if (paths.Count <= 0)
			{
				_activeJournalPath = _filePath;
				_activeJournalLineCount = 0;
				return;
			}

			string latestPath = paths[^1];
			LoadFromJournalFile(latestPath);

			_activeJournalPath = latestPath;
			try
			{
				_activeJournalLineCount = File.ReadLines(_activeJournalPath).Count();
			}
			catch
			{
				_activeJournalLineCount = 0;
			}
		}

		private void InitializeJournalPathsAndLoadAllChunks()
		{
			var paths = GetJournalChunkPaths(includeLegacyBaseFile: true);
			if (paths.Count <= 0)
			{
				_activeJournalPath = _filePath;
				_activeJournalLineCount = 0;
				return;
			}

			foreach (string path in paths)
			{
				LoadFromJournalFile(path);
			}
			_loadedAllChunks = true;

			// Use the latest chunk as the active append target.
			_activeJournalPath = paths[^1];
			try
			{
				_activeJournalLineCount = File.ReadLines(_activeJournalPath).Count();
			}
			catch
			{
				_activeJournalLineCount = 0;
			}
		}

		private List<string> GetJournalChunkPaths(bool includeLegacyBaseFile)
		{
			var result = new List<string>();

			if (includeLegacyBaseFile && File.Exists(_filePath))
			{
				result.Add(_filePath);
			}

			string folder = Path.GetDirectoryName(_filePath) ?? string.Empty;
			string baseName = Path.GetFileNameWithoutExtension(_filePath);
			string ext = Path.GetExtension(_filePath);

			if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(baseName) || string.IsNullOrWhiteSpace(ext))
			{
				return result;
			}

			string pattern = $"{baseName}_*{ext}";
			string[] files;
			try
			{
				files = Directory.GetFiles(folder, pattern);
			}
			catch
			{
				return result;
			}

			var parsed = new List<(int Index, string Path)>();
			foreach (string file in files)
			{
				string name = Path.GetFileNameWithoutExtension(file);
				if (name == null || !name.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				string suffix = name.Substring(baseName.Length + 1);
				if (!int.TryParse(suffix, out int index))
				{
					continue;
				}

				parsed.Add((index, file));
			}

			foreach (var entry in parsed.OrderBy(p => p.Index))
			{
				result.Add(entry.Path);
			}

			return result;
		}

		private string BuildChunkPath(int index)
		{
			string folder = Path.GetDirectoryName(_filePath) ?? string.Empty;
			string baseName = Path.GetFileNameWithoutExtension(_filePath);
			string ext = Path.GetExtension(_filePath);

			string fileName = $"{baseName}_{index.ToString($"D{ChunkIndexDigits}")}{ext}";
			return Path.Combine(folder, fileName);
		}

		private void RotateToNextChunkFile()
		{
			// Determine next index from existing chunk files.
			var paths = GetJournalChunkPaths(includeLegacyBaseFile: false);
			int nextIndex = 1;
			if (paths.Count > 0)
			{
				string baseName = Path.GetFileNameWithoutExtension(_filePath);
				string lastName = Path.GetFileNameWithoutExtension(paths[^1]);
				if (lastName != null && lastName.StartsWith(baseName + "_", StringComparison.OrdinalIgnoreCase))
				{
					string suffix = lastName.Substring(baseName.Length + 1);
					if (int.TryParse(suffix, out int lastIndex))
					{
						nextIndex = lastIndex + 1;
					}
				}
			}

			_activeJournalPath = BuildChunkPath(nextIndex);
			_activeJournalLineCount = 0;

			// The new segment does not exist on disk yet, so it can never be the one trimmed away here.
			EnforceRetentionCap();
		}

		// INC-001 / D-15.28 — deletes the OLDEST segments beyond MaxRetainedJournalChunks. Ordering comes
		// from GetJournalChunkPaths (legacy base file first, then chunks by ascending index), which is also
		// chronological order, so trimming from the front discards the oldest history first. The base file
		// is deliberately included: after a rebuild it is the oldest segment, and exempting it is how a
		// "chunked" store ends up with one un-trimmable monolith at its head.
		private void EnforceRetentionCap()
		{
			// `excess <= 0` covers the disabled case (cap 0 or negative ⇒ nothing is ever trimmed), so no
			// separate early-return guard is needed — and a `const`-folded one only earns a CS0162.
			List<string> segments = GetJournalChunkPaths(includeLegacyBaseFile: true);
			int excess = MaxRetainedJournalChunks > 0 ? segments.Count - MaxRetainedJournalChunks : 0;

			for (int i = 0; i < excess; i++)
			{
				string path = segments[i];
				if (string.Equals(path, _activeJournalPath, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				try
				{
					File.Delete(path);
				}
				catch
				{
					// Best-effort: a locked segment simply survives until the next rotation.
				}
			}
		}

		// Deletes every file this repository owns — the base file and every chunk. Uses
		// GetJournalChunkPaths so the match is the same strictly-parsed `<base>_<index><ext>` set the loader
		// reads, never a looser glob: this method deletes files, so its notion of "mine" must be exact.
		private void DeleteAllJournalFiles()
		{
			foreach (string path in GetJournalChunkPaths(includeLegacyBaseFile: true))
			{
				try
				{
					File.Delete(path);
				}
				catch
				{
					// Best-effort — a survivor is re-read next boot, which is wrong but not destructive.
				}
			}
		}

		private void EnsureJournalFolderExists()
		{
			string folderPath = Path.GetDirectoryName(_activeJournalPath) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}
		}

		private void LoadFromJournalFile(string path)
		{
			foreach (string rawLine in File.ReadLines(path))
			{
				if (string.IsNullOrWhiteSpace(rawLine))
				{
					continue;
				}

				HistoryJournalEntry entry;
				try
				{
					entry = JsonSerializer.Deserialize<HistoryJournalEntry>(rawLine, _jsonOptions);
				}
				catch
				{
					continue;
				}

				if (entry == null)
				{
					continue;
				}

				ApplyJournalEntry(entry);
			}
		}

		private void LoadFromLegacySnapshot(string path)
		{
			string json = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(json))
			{
				return;
			}

			var snapshot = JsonSerializer.Deserialize<BetHistorySnapshot>(json, _jsonOptions);
			if (snapshot?.Records == null)
			{
				return;
			}

			foreach (BetRecord record in snapshot.Records.Where(r => r != null))
			{
				if (!TryClaimRecordId(record))
				{
					_duplicateRecordsSkipped++;
					continue;
				}

				_records.Add(record);
			}

			if (snapshot.Deposits != null)
			{
				_deposits.AddRange(snapshot.Deposits.Where(d => d != null));
			}
		}

		public void Add(BetRecord record)
		{
			if (record == null)
			{
				throw new ArgumentNullException(nameof(record));
			}

			if (!TryClaimRecordId(record))
			{
				// A live re-registration of an already-stored bet — the double-counting shape, at its source
				// rather than at the loader. Refuse it and say so; silently accepting is what INC-001 did.
				GD.PushError($"[BetHistory] Refused a duplicate live bet record (Id {record.Id}). " +
					"Something registered the same settled bet twice — see INCIDENT_LOG INC-002.");
				return;
			}

			_records.Add(record);
			_pendingJournalEntries.Add(HistoryJournalEntry.FromBet(record));
			MarkDirtyAndSaveIfNeeded();
		}

		public void AddDeposit(DepositRecord record)
		{
			if (record == null)
			{
				throw new ArgumentNullException(nameof(record));
			}

			_deposits.Add(record);
			_pendingJournalEntries.Add(HistoryJournalEntry.FromDeposit(record));
			MarkDirtyAndSaveIfNeeded();
		}

		public IReadOnlyList<BetRecord> GetBetsForCalendarDay(DateTime localDate, TimeZoneInfo timezone = null)
		{
			TimeZoneInfo tz = timezone ?? TimeZoneInfo.Local;
			DateTime dayStartLocal = localDate.Date;
			DateTime dayEndLocal = dayStartLocal.AddDays(1);

			DateTime dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, tz);
			DateTime dayEndUtc = TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, tz);

			return _records
				.Where(r => r.TimestampUtc >= dayStartUtc && r.TimestampUtc < dayEndUtc)
				.OrderBy(r => r.TimestampUtc)
				.ToList();
		}

		public IReadOnlyList<TimeBucketSummary> BuildSummaries(TimeBucketType bucketType)
		{
			return _records
				.GroupBy(r => TruncateToBucketStartUtc(r.TimestampUtc, bucketType))
				.OrderBy(g => g.Key)
				.Select(g => new TimeBucketSummary
				{
					BucketStartUtc = g.Key,
					BucketType = bucketType,
					TotalBets = g.Count(),
					Wins = g.Count(r => r.Outcome == BetOutcome.Win),
					Losses = g.Count(r => r.Outcome == BetOutcome.Loss),
					NetAmountSum = g.Sum(r => r.NetAmount)
				})
				.ToList();
		}

		public decimal GetBalanceAtOrBeforeUtc(DateTime utcDateTime)
		{
			DateTime target = utcDateTime.Kind == DateTimeKind.Utc ? utcDateTime : utcDateTime.ToUniversalTime();
			decimal balance = DefaultInitialBalance;

			foreach (DepositRecord deposit in _deposits)
			{
				if (deposit.TimestampUtc <= target)
				{
					balance = Money.Normalize(balance + deposit.Amount);
				}
			}

			foreach (BetRecord record in _records)
			{
				if (record.TimestampUtc <= target)
				{
					balance = Money.Normalize(balance + record.NetAmount);
				}
			}

			return balance;
		}

		public TimeBasedBetStats BuildStatsUpToUtc(DateTime utcDateTime)
		{
			DateTime target = utcDateTime.Kind == DateTimeKind.Utc ? utcDateTime : utcDateTime.ToUniversalTime();
			DateTime? lastDepositUtc = null;
			foreach (DepositRecord deposit in _deposits)
			{
				if (deposit.TimestampUtc <= target)
				{
					if (!lastDepositUtc.HasValue || deposit.TimestampUtc > lastDepositUtc.Value)
					{
						lastDepositUtc = deposit.TimestampUtc;
					}
				}
			}

			int totalBets = 0;
			int wins = 0;
			int losses = 0;
			decimal totalWagered = 0m;
			decimal netProfit = 0m;
			decimal wageredSinceLastDeposit = 0m;
			decimal netProfitSinceLastDeposit = 0m;

			foreach (BetRecord record in _records)
			{
				if (record.TimestampUtc > target)
				{
					continue;
				}

				totalBets++;
				if (record.Outcome == BetOutcome.Win)
				{
					wins++;
				}
				else if (record.Outcome == BetOutcome.Loss)
				{
					losses++;
				}

				totalWagered += record.BetAmount;
				netProfit += record.NetAmount;

				if (!lastDepositUtc.HasValue || record.TimestampUtc > lastDepositUtc.Value)
				{
					wageredSinceLastDeposit += record.BetAmount;
					netProfitSinceLastDeposit += record.NetAmount;
				}
			}

			return new TimeBasedBetStats
			{
				TotalBets = totalBets,
				Wins = wins,
				Losses = losses,
				TotalWagered = Money.Normalize(totalWagered),
				NetProfit = Money.Normalize(netProfit),
				WageredSinceLastDeposit = Money.Normalize(wageredSinceLastDeposit),
				NetProfitSinceLastDeposit = Money.Normalize(netProfitSinceLastDeposit)
			};
		}

		private static DateTime TruncateToBucketStartUtc(DateTime dateTimeUtc, TimeBucketType bucketType)
		{
			DateTime utc = dateTimeUtc.Kind == DateTimeKind.Utc ? dateTimeUtc : dateTimeUtc.ToUniversalTime();

			return bucketType switch
			{
				TimeBucketType.Second => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, DateTimeKind.Utc),
				TimeBucketType.Minute => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc),
				TimeBucketType.Hour => new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc),
				TimeBucketType.Day => new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc),
				TimeBucketType.Month => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
				TimeBucketType.Year => new DateTime(utc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
				_ => throw new ArgumentOutOfRangeException(nameof(bucketType), bucketType, "Unsupported bucket type")
			};
		}

		private void Flush(bool force = false)
		{
			if (!force && _pendingJournalEntries.Count <= 0)
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(_activeJournalPath))
			{
				_activeJournalPath = _filePath;
			}

			EnsureJournalFolderExists();
			WriteEntriesRotating(_pendingJournalEntries);

			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;
			_lastSaveUtc = DateTime.UtcNow;
		}

		// INC-001 / D-15.27 — THE single writer. Both the incremental path (Flush) and the wholesale path
		// (RebuildJournalFromCurrentState) go through here, because the rotation rule belongs to the FILE,
		// not to whichever function grew it first. Previously only Flush rotated; the rebuild wrote the
		// entire in-memory history into the base file uncapped, which silently defeated the chunking policy
		// and produced a 1.13 GB monolith sitting beside the 114 chunks it had just duplicated.
		private void WriteEntriesRotating(IReadOnlyList<HistoryJournalEntry> entries)
		{
			int index = 0;
			while (index < entries.Count)
			{
				if (_activeJournalLineCount >= MaxJournalEntriesPerChunkFile)
				{
					RotateToNextChunkFile();
					EnsureJournalFolderExists();
				}

				int remainingCapacity = Math.Max(1, MaxJournalEntriesPerChunkFile - _activeJournalLineCount);
				int toWrite = Math.Min(remainingCapacity, entries.Count - index);

				using (var stream = new FileStream(_activeJournalPath, FileMode.Append, System.IO.FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					for (int i = 0; i < toWrite; i++)
					{
						string line = JsonSerializer.Serialize(entries[index + i], _jsonOptions);
						writer.WriteLine(line);
					}
				}

				index += toWrite;
				_activeJournalLineCount += toWrite;
			}
		}

		public void Flush()
		{
			Flush(force: false);
		}

		public void SetSaveSuspended(bool suspended)
		{
			_saveSuspended = suspended;
			if (!suspended)
			{
				Flush(force: false);
			}
		}

		public void RollbackToUtc(DateTime checkpointUtc)
		{
			DateTime checkpoint = checkpointUtc.Kind == DateTimeKind.Utc
				? checkpointUtc
				: checkpointUtc.ToUniversalTime();

			_records.RemoveAll(r => r != null && r.TimestampUtc > checkpoint);
			_deposits.RemoveAll(d => d != null && d.TimestampUtc > checkpoint);
			RebuildRecordIdIndex();
			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;
			RebuildJournalFromCurrentState();
		}

		// The id index mirrors _records, so a truncation has to release the ids it dropped — otherwise a
		// legitimate later re-registration of a rolled-back bet would be refused as a duplicate.
		private void RebuildRecordIdIndex()
		{
			_recordIds.Clear();
			foreach (BetRecord record in _records)
			{
				if (record != null && !string.IsNullOrEmpty(record.Id))
				{
					_recordIds.Add(record.Id);
				}
			}
		}

		public void ClearAll()
		{
			_records.Clear();
			_recordIds.Clear();
			_deposits.Clear();
			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;
			RebuildJournalFromCurrentState();
		}

		private void MarkDirtyAndSaveIfNeeded()
		{
			_mutationsSinceLastSave++;
			if (_saveSuspended)
			{
				// In high-frequency mode we avoid frequent IO, but we must not let RAM grow unbounded.
				if (_pendingJournalEntries.Count >= MaxPendingJournalEntriesWhileSuspended &&
					(DateTime.UtcNow - _lastSuspendedFlushUtc) >= SuspendedFlushMinInterval)
				{
					_lastSuspendedFlushUtc = DateTime.UtcNow;
					Flush(force: true);
				}

				return;
			}

			bool reachedMutationThreshold = _mutationsSinceLastSave >= FlushEveryMutations;
			bool reachedTimeThreshold = (DateTime.UtcNow - _lastSaveUtc) >= FlushInterval;

			if (reachedMutationThreshold || reachedTimeThreshold)
			{
				Flush(force: false);
			}
		}

		// INC-001 / D-15.27 — rewrites the journal from the in-memory state (called by RollbackToUtc and
		// ClearAll: every checkpoint restore and every DiceGame entry). Three things it must do, and used to
		// get wrong:
		//   1. DELETE what it supersedes. It used to truncate only the base file and leave every chunk in
		//      place — and since the loader reads base + chunks, the next boot counted the same records
		//      twice, the next rebuild wrote the doubled set back, and it compounded per session. That is
		//      also why the lifetime stats had been inflated for an unknown number of sessions.
		//   2. ROTATE. It wrote everything into one file with no cap (see WriteEntriesRotating).
		//   3. Leave _activeJournalPath on the LAST segment written, so the next Flush appends to the
		//      newest file instead of re-opening one the loader has already read.
		private void RebuildJournalFromCurrentState()
		{
			DeleteAllJournalFiles();

			_activeJournalPath = _filePath;
			_activeJournalLineCount = 0;
			EnsureJournalFolderExists();

			var entries = new List<HistoryJournalEntry>(_deposits.Count + _records.Count);

			foreach (DepositRecord deposit in _deposits.OrderBy(d => d.TimestampUtc))
			{
				entries.Add(HistoryJournalEntry.FromDeposit(deposit));
			}

			foreach (BetRecord record in _records.OrderBy(r => r.TimestampUtc))
			{
				entries.Add(HistoryJournalEntry.FromBet(record));
			}

			// Chronological by construction (deposits then timestamp-ordered bets), which matches the order
			// GetJournalChunkPaths hands the segments back to the loader.
			WriteEntriesRotating(entries);
			EnforceRetentionCap();
		}

		private void ApplyJournalEntry(HistoryJournalEntry entry)
		{
			if (entry.Type == EntryTypeBet && entry.Bet != null)
			{
				if (!TryClaimRecordId(entry.Bet))
				{
					_duplicateRecordsSkipped++;
					return;
				}

				_records.Add(entry.Bet);
				return;
			}

			if (entry.Type == EntryTypeDeposit && entry.Deposit != null)
			{
				_deposits.Add(entry.Deposit);
			}
		}

		private bool NormalizeLegacyRecordsInPlace()
		{
			bool changed = false;

			foreach (BetRecord record in _records)
			{
				if (record == null)
				{
					continue;
				}

				decimal normalizedBetAmount = Money.Normalize(record.BetAmount);
				if (record.BetAmount != normalizedBetAmount)
				{
					record.BetAmount = normalizedBetAmount;
					changed = true;
				}

				decimal normalizedNetAmount = Money.Normalize(record.NetAmount);
				if (record.NetAmount != normalizedNetAmount)
				{
					record.NetAmount = normalizedNetAmount;
					changed = true;
				}

				decimal normalizedBalanceAfter = Money.Normalize(record.BalanceAfter);
				if (record.BalanceAfter != normalizedBalanceAfter)
				{
					record.BalanceAfter = normalizedBalanceAfter;
					changed = true;
				}

				DateTime utcTimestamp = record.TimestampUtc.Kind == DateTimeKind.Utc
					? record.TimestampUtc
					: record.TimestampUtc.ToUniversalTime();
				if (record.TimestampUtc != utcTimestamp)
				{
					record.TimestampUtc = utcTimestamp;
					changed = true;
				}
			}

			foreach (DepositRecord deposit in _deposits)
			{
				if (deposit == null)
				{
					continue;
				}

				decimal normalizedAmount = Money.Normalize(deposit.Amount);
				if (deposit.Amount != normalizedAmount)
				{
					deposit.Amount = normalizedAmount;
					changed = true;
				}

				decimal normalizedBalanceAfter = Money.Normalize(deposit.BalanceAfter);
				if (deposit.BalanceAfter != normalizedBalanceAfter)
				{
					deposit.BalanceAfter = normalizedBalanceAfter;
					changed = true;
				}

				DateTime utcTimestamp = deposit.TimestampUtc.Kind == DateTimeKind.Utc
					? deposit.TimestampUtc
					: deposit.TimestampUtc.ToUniversalTime();
				if (deposit.TimestampUtc != utcTimestamp)
				{
					deposit.TimestampUtc = utcTimestamp;
					changed = true;
				}
			}

			return changed;
		}

		public static string ResolveDefaultPath()
		{
			return ProjectSettings.GlobalizePath("user://bet_history.jsonl");
		}

		private static string GetLegacySnapshotPath(string currentPath)
		{
			if (currentPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
			{
				return currentPath.Substring(0, currentPath.Length - 1);
			}

			return currentPath + ".json";
		}

		private sealed class HistoryJournalEntry
		{
			public string Type { get; set; } = string.Empty;
			public BetRecord Bet { get; set; }
			public DepositRecord Deposit { get; set; }

			public static HistoryJournalEntry FromBet(BetRecord record)
			{
				return new HistoryJournalEntry
				{
					Type = EntryTypeBet,
					Bet = record
				};
			}

			public static HistoryJournalEntry FromDeposit(DepositRecord deposit)
			{
				return new HistoryJournalEntry
				{
					Type = EntryTypeDeposit,
					Deposit = deposit
				};
			}
		}
	}
}
