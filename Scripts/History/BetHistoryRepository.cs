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
		private readonly string _filePath;
		private readonly List<BetRecord> _records = new();
		private readonly List<DepositRecord> _deposits = new();
		private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

		public BetHistoryRepository(string filePath)
		{
			_filePath = filePath;
		}

		public IReadOnlyList<BetRecord> Records => _records;
		public IReadOnlyList<DepositRecord> Deposits => _deposits;

		public void Load()
		{
			_records.Clear();
			_deposits.Clear();

			if (!File.Exists(_filePath))
			{
				return;
			}

			string json = File.ReadAllText(_filePath);
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

			if (NormalizeLegacyRecordsInPlace())
			{
				Save();
			}
		}

		public void Add(BetRecord record)
		{
			if (record == null)
			{
				throw new ArgumentNullException(nameof(record));
			}

			_records.Add(record);
			Save();
		}

		public void AddDeposit(DepositRecord record)
		{
			if (record == null)
			{
				throw new ArgumentNullException(nameof(record));
			}

			_deposits.Add(record);
			Save();
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

			var events = new List<(DateTime TimestampUtc, int Priority, decimal Amount)>();
			events.AddRange(_deposits
				.Where(d => d.TimestampUtc <= target)
				.Select(d => (d.TimestampUtc, 0, d.Amount)));
			events.AddRange(_records
				.Where(r => r.TimestampUtc <= target)
				.Select(r => (r.TimestampUtc, 1, r.NetAmount)));

			foreach (var timelineEvent in events.OrderBy(e => e.TimestampUtc).ThenBy(e => e.Priority))
			{
				balance = Money.Normalize(balance + timelineEvent.Amount);
			}

			return balance;
		}

		public TimeBasedBetStats BuildStatsUpToUtc(DateTime utcDateTime)
		{
			DateTime target = utcDateTime.Kind == DateTimeKind.Utc ? utcDateTime : utcDateTime.ToUniversalTime();
			var filtered = _records.Where(r => r.TimestampUtc <= target).ToList();

			return new TimeBasedBetStats
			{
				TotalBets = filtered.Count,
				Wins = filtered.Count(r => r.Outcome == BetOutcome.Win),
				Losses = filtered.Count(r => r.Outcome == BetOutcome.Loss),
				TotalWagered = Money.Normalize(filtered.Sum(r => r.BetAmount)),
				NetProfit = Money.Normalize(filtered.Sum(r => r.NetAmount)),
				WageredSinceLastDeposit = Money.Normalize(BuildWageredSinceLastDeposit(target)),
				NetProfitSinceLastDeposit = Money.Normalize(BuildProfitSinceLastDeposit(target))
			};
		}

		private decimal BuildWageredSinceLastDeposit(DateTime targetUtc)
		{
			DateTime? lastDeposit = _deposits
				.Where(d => d.TimestampUtc <= targetUtc)
				.OrderBy(d => d.TimestampUtc)
				.Select(d => (DateTime?)d.TimestampUtc)
				.LastOrDefault();

			return _records
				.Where(r => r.TimestampUtc <= targetUtc && (!lastDeposit.HasValue || r.TimestampUtc > lastDeposit.Value))
				.Sum(r => r.BetAmount);
		}

		private decimal BuildProfitSinceLastDeposit(DateTime targetUtc)
		{
			DateTime? lastDeposit = _deposits
				.Where(d => d.TimestampUtc <= targetUtc)
				.OrderBy(d => d.TimestampUtc)
				.Select(d => (DateTime?)d.TimestampUtc)
				.LastOrDefault();

			return _records
				.Where(r => r.TimestampUtc <= targetUtc && (!lastDeposit.HasValue || r.TimestampUtc > lastDeposit.Value))
				.Sum(r => r.NetAmount);
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

		private void Save()
		{
			string folderPath = Path.GetDirectoryName(_filePath) ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}

			var snapshot = new BetHistorySnapshot
			{
				Records = new List<BetRecord>(_records),
				Deposits = new List<DepositRecord>(_deposits)
			};
			string json = JsonSerializer.Serialize(snapshot, _jsonOptions);
			File.WriteAllText(_filePath, json);
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
			return ProjectSettings.GlobalizePath("user://bet_history.json");
		}
	}
}
