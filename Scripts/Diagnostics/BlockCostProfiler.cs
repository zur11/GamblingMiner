using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Scripts.Diagnostics
{
	/// <summary>
	/// Mini-plan 13 A — times ONE MINED BLOCK, phase by phase. The third profiler, and the one that measures
	/// the cost that GROWS.
	///
	/// <para><b>Why it exists.</b> <see cref="BetCostProfiler"/> prices a bet and <see cref="FrameCostProfiler"/>
	/// prices a frame; both measure costs that are constant per event. A block's cost is not: mini-plan 11 read
	/// it as a single figure that rose with the chain — 24 ms at height 376, 33 ms at 747 — and a single figure
	/// cannot say which of the roadmap's T4.1 / T4.2 / T4.5 to build. This is the roadmap's <b>T4.6</b>, which
	/// T4 itself says to do first, for exactly that reason.</para>
	///
	/// <para><b>One CSV row per block</b>, carrying the chain height and the game date, so cost is read against
	/// the size of the world rather than against the run. The Output panel gets one compact line per block.</para>
	///
	/// <para><b>The UTXO rebuild is the one phase that cannot be timed in place.</b>
	/// <c>BlockchainService.GetUtxoSet()</c> replays the chain from genesis and caches until the chain changes,
	/// so a block invalidates the cache and the replay happens LATER, on whatever query comes next — possibly in
	/// another frame, for any of the ~62 nodes that each hold their own service. It is therefore accumulated
	/// across the interval and reported on the NEXT block's row, which the column names say plainly
	/// (<c>…SincePrev</c>). Rebuilds are COUNTED and the distinct nodes are counted too: whether one node
	/// replays or forty is the difference between T4.1 and T4.2.</para>
	///
	/// <para><b>Off by default, DEBUG only</b>, armed from the toggle beside the DEV time selector — the same
	/// contract as the other two.</para>
	/// </summary>
	public static class BlockCostProfiler
	{
		/// <summary>The phases of a mined block, in the order <c>NetworkRoot.HandleMinedBlock</c> runs them.</summary>
		public enum Phase
		{
			/// <summary>BroadcastBlock — acceptance by every node on the shared network.</summary>
			Broadcast = 0,
			/// <summary>AppendDifficultyTrace — the per-block CSV append.</summary>
			DifficultyTrace,
			/// <summary>ScheduleBotTransactionsAfterBlock — the automated traffic for the next blocks.</summary>
			BotTransactions,
			/// <summary>TrySettleResolvedAuctions + CancelAndRefundStaleAuctionBids, including its mempool sweep
			/// across every node.</summary>
			Auctions,
			/// <summary>TickCompanyGovernance — votes, dividends, and its own trace.</summary>
			Governance,
			/// <summary>HistoricalEventScheduler.OnBlockMined — the scripted player-era transactions.</summary>
			HistoricalEvents,
			/// <summary>PersistStateToDisk — the whole chain serialized and atomically rewritten.</summary>
			Snapshot,
			/// <summary>TryDistributePendingCasinoRewards.</summary>
			CasinoRewards,
			/// <summary>The BlockAccepted event — whatever its subscribers do.</summary>
			Subscribers,
			/// <summary>The checkpoint capture that follows the block, timed from SimulationService.</summary>
			Checkpoint,
		}

		private const int PhaseCount = 10;

		private static readonly string[] PhaseNames =
		{
			"Broadcast", "DifficultyTrace", "BotTransactions", "Auctions", "Governance",
			"HistoricalEvents", "Snapshot", "CasinoRewards", "Subscribers", "Checkpoint",
		};

		public const string TracePath = "user://logs/block_cost_trace.csv";

		private const string Header =
			"reportUtc,chainHeight,gameDateUtc,minerNodeId,totalMs,broadcastMs,difficultyTraceMs,botTransactionsMs," +
			"auctionsMs,governanceMs,historicalEventsMs,snapshotMs,casinoRewardsMs,subscribersMs,checkpointMs," +
			"unaccountedMs,snapshotBytes,snapshotWrites,snapshotWriteMs,utxoRebuildsSincePrev,utxoMsSincePrev,utxoNodesSincePrev";

		private static readonly long[] _phaseTicks = new long[PhaseCount];
		private static int _openPhase = -1;
		private static long _openSince;
		private static long _blockBegin;
		private static bool _inBlock;
		private static int _chainHeight;
		private static DateTime _gameUtc;
		private static string _minerNodeId = string.Empty;
		private static long _snapshotBytes;
		private static int _snapshotWrites;
		private static double _snapshotWriteMs;

		// Accumulated between blocks, because the rebuild they cause happens lazily after the block returns.
		private static int _utxoRebuilds;
		private static double _utxoMs;
		private static readonly HashSet<string> _utxoNodes = new(StringComparer.Ordinal);

		private static bool _headerChecked;

		/// <summary>Armed state. False by default.</summary>
		public static bool Enabled { get; private set; }

		/// <summary>Blocks measured since the profiler was last armed, for a DEV readout.</summary>
		public static int BlockCount { get; private set; }

		/// <summary>Fires after each block is written, so a DEV readout can show <see cref="BlockCount"/>.</summary>
		public static event Action BlockPublished;

		/// <summary>
		/// Turn measurement on or off, announcing it in the Godot editor's <b>Output</b> panel (GD.Print, never
		/// GD.PrintErr — CLAUDE.md's panel rule).
		/// </summary>
		[Conditional("DEBUG")]
		public static void Arm(bool enabled)
		{
			if (Enabled == enabled)
			{
				return;
			}

			Enabled = enabled;
			BlockCount = 0;
			_inBlock = false;
			_openPhase = -1;
			ResetIntervalCounters();
			BlockPublished?.Invoke();

			GD.Print(enabled
				? $"[BlockCost] ARMED — one line per mined block, and a row in {TracePath}. " +
				  "UTXO rebuilds are counted across the interval and reported on the NEXT block's row."
				: "[BlockCost] disarmed.");
		}

		private static void ResetIntervalCounters()
		{
			_utxoRebuilds = 0;
			_utxoMs = 0d;
			_utxoNodes.Clear();
		}

		/// <summary>Opens a block. <paramref name="chainHeight"/> is the tip's index AFTER this block.</summary>
		[Conditional("DEBUG")]
		public static void BeginBlock(string minerNodeId, int chainHeight, DateTime gameUtc)
		{
			if (!Enabled) return;

			// A block is closed by the checkpoint capture that follows it, which is a different file's code
			// path. If one ever fails to arrive, the block still gets its row here rather than vanishing — a
			// missing row would read as "no block", which is the one thing this instrument must not invent.
			if (_inBlock)
			{
				EndBlock();
			}

			_inBlock = true;
			_blockBegin = Stopwatch.GetTimestamp();
			Array.Clear(_phaseTicks, 0, PhaseCount);
			_openPhase = -1;
			_chainHeight = chainHeight;
			_gameUtc = gameUtc;
			_minerNodeId = minerNodeId ?? string.Empty;
			_snapshotBytes = 0;
			_snapshotWrites = 0;
			_snapshotWriteMs = 0d;
		}

		/// <summary>Closes the open phase (if any) and opens <paramref name="phase"/>.</summary>
		[Conditional("DEBUG")]
		public static void Enter(Phase phase)
		{
			if (!Enabled || !_inBlock) return;
			long now = Stopwatch.GetTimestamp();
			CloseOpenPhase(now);
			_openPhase = (int)phase;
			_openSince = now;
		}

		private static void CloseOpenPhase(long now)
		{
			if (_openPhase >= 0)
			{
				_phaseTicks[_openPhase] += now - _openSince;
				_openPhase = -1;
			}
		}

		/// <summary>
		/// One completed world-snapshot write: its bytes and its own time, counted wherever it happens.
		///
		/// <para><b>Why counted rather than phase-timed</b> (mini-plan 13 A, second pass): the first run put 36%
		/// of a block in the Snapshot phase and 46% in Checkpoint — and the checkpoint's own path calls
		/// <c>PersistFinancialState(true)</c>, which reaches <c>PersistStateToDisk</c> again. That makes the whole
		/// chain serialized and atomically rewritten <b>twice per block</b>, which is a claim worth measuring
		/// instead of inferring from two phase totals. This counts the writes and sums their time directly.</para>
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteSnapshotWrite(long bytes, double milliseconds)
		{
			if (!Enabled || !_inBlock) return;
			_snapshotBytes = bytes;
			_snapshotWrites++;
			_snapshotWriteMs += milliseconds;
		}

		/// <summary>
		/// One UTXO-set replay, by the node that owns it. Called from the rebuild path itself, which runs
		/// whenever a query follows a chain change — so this lands between blocks, not inside one.
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteUtxoRebuild(string nodeId, double milliseconds)
		{
			if (!Enabled) return;
			_utxoRebuilds++;
			_utxoMs += milliseconds;
			if (!string.IsNullOrEmpty(nodeId))
			{
				_utxoNodes.Add(nodeId);
			}
		}

		/// <summary>Closes the block, writes its line and its row, and resets the interval counters.</summary>
		[Conditional("DEBUG")]
		public static void EndBlock()
		{
			if (!Enabled || !_inBlock) return;

			long now = Stopwatch.GetTimestamp();
			CloseOpenPhase(now);
			_inBlock = false;

			double totalMs = TicksToMs(now - _blockBegin);
			double accounted = 0d;
			for (int i = 0; i < PhaseCount; i++)
			{
				accounted += TicksToMs(_phaseTicks[i]);
			}

			double Ms(Phase p) => TicksToMs(_phaseTicks[(int)p]);

			var sb = new StringBuilder();
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"[BlockCost] h={_chainHeight:N0} {_gameUtc:yyyy-MM-dd} by {_minerNodeId} · total {totalMs:N1} ms" +
				$" · snapshot {_snapshotWriteMs:N1} ms in {_snapshotWrites} write(s) of {_snapshotBytes / 1024.0:N0} KB" +
				$" · utxo {_utxoMs:N1} ({_utxoRebuilds} rebuild(s), {_utxoNodes.Count} node(s)) since previous block" +
				$" · broadcast {Ms(Phase.Broadcast):N1} · governance {Ms(Phase.Governance):N1}" +
				$" · auctions {Ms(Phase.Auctions):N1} · botTx {Ms(Phase.BotTransactions):N1}" +
				$" · checkpoint {Ms(Phase.Checkpoint):N1} · other {totalMs - accounted:N1}"));
			GD.Print(sb.ToString());

			WriteTraceRow(string.Format(CultureInfo.InvariantCulture,
				"{0:O},{1},{2:O},{3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3},{11:F3},{12:F3},{13:F3},{14:F3},{15:F3},{16},{17},{18:F3},{19},{20:F3},{21}",
				DateTime.UtcNow, _chainHeight, _gameUtc, _minerNodeId, totalMs,
				Ms(Phase.Broadcast), Ms(Phase.DifficultyTrace), Ms(Phase.BotTransactions), Ms(Phase.Auctions),
				Ms(Phase.Governance), Ms(Phase.HistoricalEvents), Ms(Phase.Snapshot), Ms(Phase.CasinoRewards),
				Ms(Phase.Subscribers), Ms(Phase.Checkpoint), totalMs - accounted, _snapshotBytes, _snapshotWrites, _snapshotWriteMs,
				_utxoRebuilds, _utxoMs, _utxoNodes.Count));

			ResetIntervalCounters();
			++BlockCount;
			BlockPublished?.Invoke();
		}

		private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

		private static void WriteTraceRow(string row)
		{
			try
			{
				EnsureHeader();
				using FileAccess file = FileAccess.Open(TracePath, FileAccess.ModeFlags.ReadWrite);
				if (file == null)
				{
					return;
				}

				file.SeekEnd();
				file.StoreString(row + "\n");
			}
			catch (Exception)
			{
				// A diagnostic must never be able to take down the thing it is diagnosing.
			}
		}

		private static void EnsureHeader()
		{
			if (_headerChecked)
			{
				return;
			}

			_headerChecked = true;

			if (!DirAccess.DirExistsAbsolute("user://logs"))
			{
				DirAccess.MakeDirRecursiveAbsolute("user://logs");
			}

			if (FileAccess.FileExists(TracePath))
			{
				// Rotate rather than append rows under a header that no longer describes them (ND.10j).
				using FileAccess existing = FileAccess.Open(TracePath, FileAccess.ModeFlags.Read);
				string firstLine = existing?.GetLine() ?? string.Empty;
				existing?.Close();
				if (string.Equals(firstLine, Header, StringComparison.Ordinal))
				{
					return;
				}

				DirAccess.RenameAbsolute(TracePath, TracePath + ".old");
			}

			using FileAccess created = FileAccess.Open(TracePath, FileAccess.ModeFlags.Write);
			created?.StoreString(Header + "\n");
		}
	}
}
