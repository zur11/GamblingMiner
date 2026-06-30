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
		public decimal CasinoScMainBalance { get; set; }
		public decimal CasinoScBankroll    { get; set; }
		public DateTime CapturedAtUtc { get; set; }
	}

	public Snapshot CurrentSnapshot { get; private set; }

	public override void _Ready()
	{
		LoadState();
		if (CurrentSnapshot != null)
			ApplyCheckpointToServices();
	}

	// Called once on startup after all other autoloads have loaded their own files.
	// Ensures every scene (including MainMenu) sees checkpoint values, not live transaction values.
	// Block = the only commit point: an app restart reverts the clock and balances to the last mined
	// block, discarding any between-block advance. The clock revert lives here (not in DiceGame) so it
	// applies at startup regardless of which scene the app opens into.
	private void ApplyCheckpointToServices()
	{
		GetNodeOrNull<BankrollStateService>("/root/BankrollStateService")
			?.SetBalance(CurrentSnapshot.BankrollBalance);
		GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService")
			?.SetBalance(CurrentSnapshot.PrincipalBalance);
		GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService")
			?.RestoreCasinoScState(CurrentSnapshot.CasinoScMainBalance, CurrentSnapshot.CasinoScBankroll);

		if (CurrentSnapshot.CalendarLocalTicks.HasValue)
		{
			CalendarTimeService calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
			if (calendar != null)
			{
				DateTime checkpointLocal = new DateTime(CurrentSnapshot.CalendarLocalTicks.Value, DateTimeKind.Local);
				calendar.SetLocalDateTime(checkpointLocal);
				calendar.SetExplorerSelectedLocalDateTime(checkpointLocal);
				calendar.PersistCurrentTime(); // also resets the present frontier (_gamePresent) to the last block
			}
		}
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

		CasinoScBalanceService casinoSc = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");

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
			CasinoScMainBalance = casinoSc?.MainBalance ?? 0m,
			CasinoScBankroll    = casinoSc?.Bankroll ?? 0m,
			CapturedAtUtc = DateTime.UtcNow
		};

		SaveState();
		GD.Print($"[Checkpoint] CAPTURED — PlayerBankroll={CurrentSnapshot.BankrollBalance:F8}  PlayerMain={CurrentSnapshot.PrincipalBalance:F8}  CasinoMain={CurrentSnapshot.CasinoScMainBalance:F8}  CasinoBankroll={CurrentSnapshot.CasinoScBankroll:F8}");
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
