using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Godot;
using GodotBlockchainPort.Simulation;

namespace Scripts.Diagnostics
{
	/// <summary>
	/// Mini-plan 25 §9 — the data the Casino Rank System will be calibrated on: every casino player's CUMULATIVE SC
	/// wagered, one row per block checkpoint, against the game date.
	///
	/// <para><b>Why it exists.</b> The developer decided (2026-10-14) that rank levels are measured on how much SC a
	/// casino player has wagered, with 20–33 levels in total, and that the thresholds are to be found by running tests
	/// from genesis with different bet amounts. The totals already exist — the player's unpruned lifetime rollup and
	/// the casino's per-client bet book — but only as a CURRENT value. Nothing records them against time, and a
	/// threshold ladder is a question about time: "how far up does a player get by year N?".</para>
	///
	/// <para><b>Why at the checkpoint.</b> <see cref="BlockSessionCheckpointService.CaptureCheckpoint"/> is the one
	/// path both capture sites funnel through, and its snapshot is the committed state — the one a restart restores.
	/// A row written here therefore never describes a moment a restart later rolls back (Pattern 2), and the values
	/// come out of the snapshot itself, so the row and the checkpoint cannot disagree.</para>
	///
	/// <para><b>Columns.</b> One identical block per casino client. The player's bets/wagered/net come from the
	/// lifetime rollup and the bots' from the casino's client book, because that is where each one LIVES: the book
	/// deliberately never records the player (ND.8f — "player stats stay in UserStatsService";
	/// <c>SimulationService</c> and <c>DiceGame</c> both skip it). The first version of this trace read the player from
	/// the book too, claiming a cross-check, and R1 wrote 0 for the player in every row: a column whose label claims
	/// more than its source can give. Credits are per client because bets per game second
	/// are credits ÷ 100 (the 100-second rule), so wagered cannot be read without them. BTC is per client too,
	/// because without income a player's expected lifetime wagered is capped near capital ÷ house edge whatever the
	/// bet size (plan §9.1), so everything above that cap is mined BTC turned into SC. Five UTXO passes per block;
	/// not timed, and at block cadence.</para>
	///
	/// <para>DEBUG-only, like every other calibration instrument: absent from an export. <c>user://logs/*</c> is
	/// evidence and exempt from the world wipe by class, so a file can span several worlds. A new world shows as
	/// <c>chainLength</c> dropping between two rows; the runs are separated by renaming the file between them.</para>
	/// </summary>
	public static class RankCalibrationTrace
	{
		public const string TracePath = "user://logs/rank_calibration_trace.csv";

		// The five canonical casino clients (CasinoClientLedgerService.CanonicalClients), in a fixed column order.
		private static readonly string[] Clients = { "player", "bot_1", "bot_2", "bot_3", "bot_4" };

		private static readonly string Header = BuildHeader();
		private static bool _headerChecked;

		[Conditional("DEBUG")]
		public static void WriteBlockRow(BlockSessionCheckpointService.Snapshot snapshot)
		{
			if (snapshot == null) return;
			try
			{
				EnsureHeader();

				DateTime gameLocal = snapshot.CalendarLocalTicks.HasValue
					? new DateTime(snapshot.CalendarLocalTicks.Value, DateTimeKind.Local)
					: DateTime.MinValue;
				var rollup = snapshot.BetStatsRollup;
				Dictionary<string, CasinoClientLedgerService.ClientBetStats> book = snapshot.ClientBetStats;

				var row = new StringBuilder();
				row.Append(string.Create(CultureInfo.InvariantCulture,
					$"{gameLocal:yyyy-MM-dd HH:mm:ss},{NetworkRoot.GetPlayerChainLengthStatic()},"));
				row.Append(string.Create(CultureInfo.InvariantCulture,
					$"{snapshot.PrincipalBalance:F8},{snapshot.BankrollBalance:F8},{snapshot.PlayerBankState?.BankAccountBalance ?? 0m:F8}"));

				foreach (string client in Clients)
				{
					int bets;
					decimal wagered, net;
					if (client == "player")
					{
						bets = rollup?.TotalBets ?? 0;
						wagered = rollup?.TotalWagered ?? 0m;
						net = rollup?.TotalNetProfit ?? 0m;
					}
					else
					{
						CasinoClientLedgerService.ClientBetStats stats = null;
						book?.TryGetValue(client, out stats);
						bets = stats?.TotalBets ?? 0;
						wagered = stats?.TotalWagered ?? 0m;
						net = stats?.NetProfit ?? 0m;
					}

					// The EFFECTIVE credits — the rate the node actually bets at. A freshly wiped world has no hardware
					// entries, so TotalCredits reads 0 while SimulationService.HardwareRate and DiceGame.BuildBotConfigs
					// clamp it to [1, MaxAutoBetBaseAps]; recording the raw 0 would make bets ÷ credits meaningless.
					int credits = Math.Clamp(
						Scripts.Hardware.HardwareAllocationRepository.GetNode(client)?.TotalCredits ?? 0,
						1, SimulationService.MaxAutoBetBaseAps);
					decimal btc = NetworkRoot.GetNodeSpendableBalanceStatic(client);
					row.Append(string.Create(CultureInfo.InvariantCulture,
						$",{credits},{bets},{wagered:F8},{net:F8},{btc:F8}"));
				}

				row.Append('\n');

				using FileAccess file = FileAccess.Open(TracePath, FileAccess.ModeFlags.ReadWrite);
				if (file == null) return;
				file.SeekEnd();
				file.StoreString(row.ToString());
			}
			catch (Exception)
			{
				// A diagnostic must never be able to take down the thing it is diagnosing.
			}
		}

		private static string BuildHeader()
		{
			var header = new StringBuilder(
				"gameTimeLocal,chainLength,p_scMain,p_scBankroll,p_scBank");
			foreach (string client in Clients)
			{
				header.Append($",{client}_credits,{client}_bets,{client}_wagered,{client}_net,{client}_btc");
			}

			return header.ToString();
		}

		private static void EnsureHeader()
		{
			if (_headerChecked) return;
			_headerChecked = true;

			if (!DirAccess.DirExistsAbsolute("user://logs"))
			{
				DirAccess.MakeDirRecursiveAbsolute("user://logs");
			}

			if (FileAccess.FileExists(TracePath))
			{
				// ND.10j's stale-schema rule: rotate rather than append rows under a header that no longer describes them.
				using FileAccess existing = FileAccess.Open(TracePath, FileAccess.ModeFlags.Read);
				string firstLine = existing?.GetLine() ?? string.Empty;
				existing?.Close();
				if (string.Equals(firstLine, Header, StringComparison.Ordinal)) return;
				DirAccess.RenameAbsolute(TracePath, TracePath + ".old");
			}

			using FileAccess created = FileAccess.Open(TracePath, FileAccess.ModeFlags.Write);
			created?.StoreString(Header + "\n");
		}
	}
}
