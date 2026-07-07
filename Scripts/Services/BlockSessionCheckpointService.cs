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
		// Nullable: legacy checkpoint (pre-SF.1.2) → null → restored as ON, never OFF.
		public bool? AutoRechargeEnabled { get; set; }
		public List<BankrollProgramService.TransferRecord> TransferRecords { get; set; } = new();
		public long? HistoryCheckpointUtcTicks { get; set; }
		public long? CalendarLocalTicks { get; set; }
		public decimal CasinoScMainBalance    { get; set; }
		public decimal CasinoScBankroll       { get; set; }
		public decimal CasinoScBankrollTarget { get; set; }
		public decimal CasinoScAutoLoanAmount { get; set; }
		public int     CasinoScLoanCount      { get; set; }
		public decimal CasinoScTotalLoaned    { get; set; }
		public List<CasinoScBalanceService.LoanRecord>     CasinoScLoanHistory     { get; set; } = new();
		public List<CasinoScBalanceService.RechargeRecord> CasinoScRechargeHistory { get; set; } = new();
		// Step 12 (SF.0.7): the player's Private Bank Account, bundled as one DTO. Null in a legacy pre-Step-12
		// checkpoint → PlayerBankAccountService keeps its loaded state (no migration, D-SF2.8).
		public PlayerBankAccountService.CheckpointState PlayerBankState { get; set; }
		// Step 12 (SF.1.5 / D-SF2.4): the casino client ledger is a player-facing persisted list, so it is
		// snapshotted at each block. Null in a legacy checkpoint → keep loaded entries.
		public List<CasinoClientLedgerService.LedgerEntry> ClientLedgerEntries { get; set; }
		// Step 13 (SW.0): the casino swap desk's reserves/fee/floor/history, bundled as one DTO. Null in a
		// legacy pre-SW.0 checkpoint → CasinoCoinSwapService keeps its loaded state (no migration).
		public CasinoCoinSwapService.CheckpointState CasinoCoinSwapState { get; set; }
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
			?.ReplaceState(BankrollProgramService.DefaultAutoRechargeAmount, new List<BankrollProgramService.TransferRecord>(), true); // toggle → ON
		GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService")
			?.ResetToPreGenesisDefaults();
		GetNodeOrNull<PlayerBankAccountService>("/root/PlayerBankAccountService")
			?.ResetToPreGenesisDefaults(); // Step 12 (SF.0.8): bank → 0, settings default, history cleared
		GetNodeOrNull<CasinoCoinSwapService>("/root/CasinoCoinSwapService")
			?.ResetToPreGenesisDefaults(); // Step 13 (SW.0): reserves 0, fee 10%, floor OFF, history cleared

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

		// SF.1.5 / D-SF2.4: the client ledger self-persists on every deposit/withdrawal/recharge, so it leaks
		// across restarts the same way. Discard the accumulated player entries and re-establish the single clean
		// "initial" 40,000 stake (uses the just-set playerStart clock for its game-time timestamp).
		GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService")
			?.ResetToPreGenesisDefaults();
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
			?.ReplaceState(CurrentSnapshot.AutoRechargeAmount, CurrentSnapshot.TransferRecords, CurrentSnapshot.AutoRechargeEnabled ?? true);
		GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService")
			?.RestoreCasinoScState(
				CurrentSnapshot.CasinoScMainBalance,
				CurrentSnapshot.CasinoScBankroll,
				CurrentSnapshot.CasinoScBankrollTarget,
				CurrentSnapshot.CasinoScAutoLoanAmount,
				CurrentSnapshot.CasinoScLoanCount,
				CurrentSnapshot.CasinoScTotalLoaned,
				CurrentSnapshot.CasinoScLoanHistory,
				CurrentSnapshot.CasinoScRechargeHistory);
		GetNodeOrNull<PlayerBankAccountService>("/root/PlayerBankAccountService")
			?.RestoreFromCheckpoint(CurrentSnapshot.PlayerBankState); // null DTO (legacy) → keeps loaded state
		GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService")
			?.RestoreFromCheckpoint(CurrentSnapshot.ClientLedgerEntries); // null (legacy) → keeps loaded entries
		GetNodeOrNull<CasinoCoinSwapService>("/root/CasinoCoinSwapService")
			?.RestoreFromCheckpoint(CurrentSnapshot.CasinoCoinSwapState); // null DTO (legacy) → keeps loaded state

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
			CasinoScMainBalance    = casinoSc?.MainBalance ?? 0m,
			CasinoScBankroll       = casinoSc?.Bankroll ?? 0m,
			CasinoScBankrollTarget = casinoSc?.BankrollTarget ?? 0m,
			CasinoScAutoLoanAmount = casinoSc?.AutoLoanAmount ?? 0m,
			CasinoScLoanCount      = casinoSc?.LoanCount ?? 0,
			CasinoScTotalLoaned    = casinoSc?.TotalLoaned ?? 0m,
			CasinoScLoanHistory    = casinoSc?.LoanHistory
				.Select(r => new CasinoScBalanceService.LoanRecord
				{
					Amount        = r.Amount,
					Reason        = r.Reason,
					GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
				}).ToList() ?? new List<CasinoScBalanceService.LoanRecord>(),
			CasinoScRechargeHistory = casinoSc?.RechargeHistory
				.Select(r => new CasinoScBalanceService.RechargeRecord
				{
					Amount        = r.Amount,
					Reason        = r.Reason,
					GameDateLocal = DateTime.SpecifyKind(r.GameDateLocal, DateTimeKind.Local)
				}).ToList() ?? new List<CasinoScBalanceService.RechargeRecord>(),
			AutoRechargeEnabled = program.AutoRechargeEnabled,
			PlayerBankState = GetNodeOrNull<PlayerBankAccountService>("/root/PlayerBankAccountService")?.CaptureCheckpointState(),
			ClientLedgerEntries = GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService")?.CaptureEntriesForCheckpoint(),
			CasinoCoinSwapState = GetNodeOrNull<CasinoCoinSwapService>("/root/CasinoCoinSwapService")?.CaptureCheckpointState(),
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
