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
		private const string EntryTypeBet = "bet";
		private const string EntryTypeDeposit = "deposit";
		private readonly string _filePath;
		private readonly string _legacySnapshotPath;
		private readonly List<BetRecord> _records = new();
		private readonly List<DepositRecord> _deposits = new();
		private readonly List<HistoryJournalEntry> _pendingJournalEntries = new();
		private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };
		private int _mutationsSinceLastSave;
		private DateTime _lastSaveUtc = DateTime.UtcNow;
		private bool _saveSuspended;

		public BetHistoryRepository(string filePath)
		{
			_filePath = filePath;
			_legacySnapshotPath = GetLegacySnapshotPath(filePath);
		}

		public IReadOnlyList<BetRecord> Records => _records;
		public IReadOnlyList<DepositRecord> Deposits => _deposits;

		public void Load()
		{
			_records.Clear();
			_deposits.Clear();
			_pendingJournalEntries.Clear();
			_mutationsSinceLastSave = 0;

			if (File.Exists(_filePath))
			{
				LoadFromJournalFile(_filePath);
				return;
			}

			if (File.Exists(_legacySnapshotPath))
			{
				LoadFromLegacySnapshot(_legacySnapshotPath);
				NormalizeLegacyRecordsInPlace();
				RebuildJournalFromCurrentState();
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

			string folderPath = Path.GetDirectoryName(_filePath) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}

			using var stream = new FileStream(_filePath, FileMode.Append, System.IO.FileAccess.Write, FileShare.Read);
			using var writer = new StreamWriter(stream);
			foreach (HistoryJournalEntry entry in _pendingJournalEntries)
			{
				string line = JsonSerializer.Serialize(entry, _jsonOptions);
				writer.WriteLine(line);
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
			string folderPath = Path.GetDirectoryName(_filePath) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}

			using var stream = new FileStream(_filePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.Read);
			using var writer = new StreamWriter(stream);

			foreach (DepositRecord deposit in _deposits.OrderBy(d => d.TimestampUtc))
			{
				string line = JsonSerializer.Serialize(HistoryJournalEntry.FromDeposit(deposit), _jsonOptions);
				writer.WriteLine(line);
			}

			foreach (BetRecord record in _records.OrderBy(r => r.TimestampUtc))
			{
				string line = JsonSerializer.Serialize(HistoryJournalEntry.FromBet(record), _jsonOptions);
				writer.WriteLine(line);
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
