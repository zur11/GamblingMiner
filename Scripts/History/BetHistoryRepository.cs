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
		private const decimal DefaultInitialBalance = 1.00000000m;
		private const int FlushEveryMutations = 200;
		private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(3);
		private const int MaxJournalEntriesPerChunkFile = 10000;
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
			_records.Clear();
			_deposits.Clear();
			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;
			_loadedAllChunks = false;

			InitializeJournalPathsAndLoadLatestChunk();
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

			_records.Clear();
			_deposits.Clear();
			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;
			InitializeJournalPathsAndLoadAllChunks();
			_loadedAllChunks = true;
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

			_records.AddRange(snapshot.Records.Where(r => r != null));
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

			string folderPath = Path.GetDirectoryName(_activeJournalPath) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}

			int index = 0;
			while (index < _pendingJournalEntries.Count)
			{
				if (_activeJournalLineCount >= MaxJournalEntriesPerChunkFile)
				{
					RotateToNextChunkFile();
					folderPath = Path.GetDirectoryName(_activeJournalPath) ?? string.Empty;
					if (!string.IsNullOrWhiteSpace(folderPath))
					{
						Directory.CreateDirectory(folderPath);
					}
				}

				int remainingCapacity = Math.Max(1, MaxJournalEntriesPerChunkFile - _activeJournalLineCount);
				int toWrite = Math.Min(remainingCapacity, _pendingJournalEntries.Count - index);

				using (var stream = new FileStream(_activeJournalPath, FileMode.Append, System.IO.FileAccess.Write, FileShare.Read))
				using (var writer = new StreamWriter(stream))
				{
					for (int i = 0; i < toWrite; i++)
					{
						HistoryJournalEntry entry = _pendingJournalEntries[index + i];
						string line = JsonSerializer.Serialize(entry, _jsonOptions);
						writer.WriteLine(line);
					}
				}

				index += toWrite;
				_activeJournalLineCount += toWrite;
			}

			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;
			_lastSaveUtc = DateTime.UtcNow;
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

		private void RebuildJournalFromCurrentState()
		{
			_activeJournalPath = _filePath;
			_activeJournalLineCount = 0;

			string folderPath = Path.GetDirectoryName(_activeJournalPath) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}

			using var stream = new FileStream(_activeJournalPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
			using var writer = new StreamWriter(stream);

			foreach (DepositRecord deposit in _deposits.OrderBy(d => d.TimestampUtc))
			{
				string line = JsonSerializer.Serialize(HistoryJournalEntry.FromDeposit(deposit), _jsonOptions);
				writer.WriteLine(line);
				_activeJournalLineCount++;
			}

			foreach (BetRecord record in _records.OrderBy(r => r.TimestampUtc))
			{
				string line = JsonSerializer.Serialize(HistoryJournalEntry.FromBet(record), _jsonOptions);
				writer.WriteLine(line);
				_activeJournalLineCount++;
			}
		}

		private void ApplyJournalEntry(HistoryJournalEntry entry)
		{
			if (entry.Type == EntryTypeBet && entry.Bet != null)
			{
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
