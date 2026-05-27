using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

public partial class BlockSessionCheckpointService : Node
{
	private const string StatePath = "user://block_session_checkpoint.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public sealed class Snapshot
	{
		public decimal PrincipalBalance { get; set; }
		public decimal BankrollBalance { get; set; }
		public decimal AutoRechargeAmount { get; set; }
		public List<BankrollProgramService.TransferRecord> TransferRecords { get; set; } = new();
		public long? HistoryCheckpointUtcTicks { get; set; }
		public long? CalendarLocalTicks { get; set; }
		public DateTime CapturedAtUtc { get; set; }
	}

	public Snapshot CurrentSnapshot { get; private set; }

	public override void _Ready()
	{
		LoadState();
	}

	public void CaptureCheckpoint(
		PrincipalBalanceService principal,
		BankrollStateService bankroll,
		BankrollProgramService program,
		DateTime historyCheckpointUtc,
		DateTime calendarLocalDateTime)
	{
		if (principal == null || bankroll == null || program == null)
		{
			return;
		}

		CurrentSnapshot = new Snapshot
		{
			PrincipalBalance = principal.CurrentBalance,
			BankrollBalance = bankroll.CurrentBalance,
			AutoRechargeAmount = program.AutoRechargeAmount,
			TransferRecords = program.Records.Select(r => new BankrollProgramService.TransferRecord
			{
				UtcTimestamp = DateTime.SpecifyKind(r.UtcTimestamp, DateTimeKind.Utc),
				Amount = r.Amount,
				Direction = r.Direction,
				Reason = r.Reason
			}).ToList(),
			HistoryCheckpointUtcTicks = DateTime.SpecifyKind(historyCheckpointUtc, DateTimeKind.Utc).Ticks,
			CalendarLocalTicks = DateTime.SpecifyKind(calendarLocalDateTime, DateTimeKind.Local).Ticks,
			CapturedAtUtc = DateTime.UtcNow
		};

		SaveState();
	}

	public bool HasCheckpoint() => CurrentSnapshot != null;

	private void LoadState()
	{
		if (!FileAccess.FileExists(StatePath))
		{
			return;
		}

		try
		{
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Read);
			string json = file.GetAsText();
			CurrentSnapshot = JsonSerializer.Deserialize<Snapshot>(json, JsonOptions);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[BlockSessionCheckpointService] Load failed: {ex.Message}");
		}
	}

	private void SaveState()
	{
		try
		{
			using FileAccess file = FileAccess.Open(StatePath, FileAccess.ModeFlags.Write);
			file.StoreString(JsonSerializer.Serialize(CurrentSnapshot, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[BlockSessionCheckpointService] Save failed: {ex.Message}");
		}
	}
}
