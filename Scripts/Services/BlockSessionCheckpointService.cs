using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using GodotBlockchainPort.Simulation;

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
		else
			ResetToPreGenesisDefaults();
	}

	// No block has ever been mined in this world, so nothing is committed yet (block = the only commit to
	// disk): every boot must present a true "first launch" state for the player's balances/ledger/dose/clock,
	// discarding whatever PrincipalBalanceService/BankrollStateService/BankrollProgramService/
	// CalendarTimeService/UserStatsService's own self-persisted files accumulated between restarts. The
	// auto-recharge dose is included in this reset — a dose configured in BankrollProgrammer only "sticks"
	// once a real block is mined (at which point ApplyCheckpointToServices() above restores the dose from
	// that checkpoint instead); until then, every restart goes back to DefaultAutoRechargeAmount, same as
	// the balances and the transfer records.
	private void ResetToPreGenesisDefaults()
	{
		GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService")
			?.SetBalance(BankrollProgramService.InitialPrincipalBalanceBaseline);
		GetNodeOrNull<BankrollStateService>("/root/BankrollStateService")
			?.SetBalance(0m);
		GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService")
			?.ReplaceState(BankrollProgramService.DefaultAutoRechargeAmount, new List<BankrollProgramService.TransferRecord>());

		// The clock and bet history leak the same way (CalendarTimeService/UserStatsService self-persist on
		// every bet, not just on a mined block). Before any real block, the chain tip IS still the historical
		// bootstrap's last block (see NetworkRoot.GetPlayerLatestBlockTimestampMsStatic), so re-deriving
		// "player start" from it on every boot is exact and needs no extra persistence of its own. No +1s
		// offset: every post-bootstrap checkpoint is captured at the calendar instant EQUAL to the mined
		// block's own timestamp (see HistoricalBootstrapService.Run()), so this matches that same convention.
		long tipMs = NetworkRoot.GetPlayerLatestBlockTimestampMsStatic();
		DateTimeOffset playerStart = DateTimeOffset.FromUnixTimeMilliseconds(tipMs);

		CalendarTimeService calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		if (calendar != null)
		{
			calendar.SetLocalDateTime(playerStart.LocalDateTime);
			calendar.SetExplorerSelectedLocalDateTime(playerStart.LocalDateTime);
			calendar.PersistCurrentTime();
		}

		// Full clear, not a timestamp-boundary rollback: nothing is committed pre-genesis, so there is no
		// legitimate boundary to partially keep — and a boundary comparison is fragile here anyway, since the
		// very first bet/deposit of a fresh session reads a clock that hasn't advanced yet and can land
		// exactly on playerStart (see OQ-BP.11).
		GetNodeOrNull<UserStatsService>("/root/UserStatsService")
			?.ClearAllHistory();
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
		GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService")
			?.ReplaceState(CurrentSnapshot.AutoRechargeAmount, CurrentSnapshot.TransferRecords);
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
