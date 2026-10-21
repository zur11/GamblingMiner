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
			"unaccountedMs,snapshotBytes,snapshotWrites,snapshotWriteMs,utxoRebuildsSincePrev,utxoMsSincePrev,utxoNodesSincePrev," +
			"stateWrites,stateFiles,stateBytes,stateWriteMs," +
			"botDonationMs,botBudgetMs,botSellFlowMs,botExchangeMs," +
			"aggSpendableCalls,spendableReads,outpointsWalked,outpointsMax," +
			// Mini-plan 26 A — inside the send.
			"budget,sellAttempts,sellSends,exchAttempts,exchSends,gateMs,firstMinedMs,firstMinedBlocksScanned," +
			"sends,sendFails,sendInputs,sendInputsMax,sendMs,selectMs,keysMs,signMs,localAdmitMs,fanOutMs," +
			"admissions,orphanAdmissions,dupScanMs,txsScanned,sigCheckMs,sigVerifies,utxoCheckMs,keyDerivations," +
			"sellSendMs,exchSendMs,govSends,govSendMs,donationSends,donationSendMs,sellSendInputs,exchSendInputs," +
			"allocMB,gcPauseMs,gc0,gc1,gc2," +
			// Mini-plan 26 B — F-a's single verification per broadcast.
			"broadcastVerifies,broadcastVerifyMs";

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

		// ── Mini-plan 16 A — every world-state file write, counted ──────────────────────────────────────
		// Measured before this plan: the block path writes ~14 files totalling ~75 KB in ~14 ms, which is
		// ~5 MB/s — three orders of magnitude below the disk. That says the cost is per-write OVERHEAD, not
		// volume, and `calendar_state.json` is 18 bytes with 20 call sites. This counts the writes so the
		// claim is measured rather than inferred from two phase totals — the same reasoning that made
		// NoteSnapshotWrite a counter instead of a phase.
		//
		// The per-block figures are what the plan's predictions are stated in (a COUNT reproduces; a
		// per-block millisecond figure demonstrably does not — mini-plan 15). The per-path totals are
		// session-cumulative and ranked, because part B needs to know WHICH writers write more than once.
		private static int _stateWrites;
		private static double _stateWriteMs;
		private static long _stateBytes;
		private static readonly HashSet<string> _stateFilesThisBlock = new(StringComparer.Ordinal);

		private sealed class StateWriteStat
		{
			public int Writes;
			public double Ms;
			public long Bytes;
		}

		// ── Mini-plan 17 A1 — the journal, broken into the operations it actually performs ──────────────
		// Mini-plan 16 measured the journal at 19.33 writes/block and 5.12 ms per append, which made the
		// APPENDS look like the target. Reading the code suggests the rotation instead: segments hold 10,000
		// entries, the run rotated ~49 times a minute, and each rotation runs Directory.GetFiles TWICE (once
		// to parse the highest index off a filename, once inside EnforceRetentionCap) plus a delete. This
		// splits them so the premise is measured rather than argued.
		//
		// ⚠ The journal's append is counted by BOTH instruments: here, and as a world-state write for
		// mini-plan 16's comparability. **Do not sum the two.** There is deliberately no Create op — a new
		// segment is created implicitly by the first append's FileMode.Append, so its cost is inside that
		// append and a separate counter would be a line that never fires.
		public enum JournalOp
		{
			Append,
			DirectoryScan,
			Delete
		}

		// ⚠ Mini-plan 18 A — SESSION TOTALS ONLY. The per-block counterparts of these four existed for one plan
		// and were deleted: the journal flushes at **bet rate, between blocks**, so a per-block column for it
		// can only ever read zero. All 195 rows of mini-plan 17's run did, and seven zeroes in a trace read as
		// a result rather than as an absence. Mini-plan 16 had already established where the journal does its
		// work; mini-plan 17 built the per-block view anyway, which is why "a lesson recorded is not a lesson
		// applied" is now a rule in CLAUDE.md. **Do not add per-block journal columns back.**
		private const int JournalOpCount = 3;
		private static readonly int[] _journalOpsTotal = new int[JournalOpCount];
		private static readonly double[] _journalOpMsTotal = new double[JournalOpCount];
		private static int _journalRotationsTotal;

		private static readonly Dictionary<string, StateWriteStat> _stateWritesByPath = new(StringComparer.Ordinal);
		private const int PerPathReportEveryBlocks = 50;

		// ── Mini-plan 18 B — inside the bot-transaction phase, which became the dominant per-block term ─────
		// Mini-plan 17 measured the whole phase at median 0.957 ms / mean 76.899 / max 469.256 in a populated
		// era, as ONE opaque number. The split below follows the method's real structure, and the asymmetry is
		// the point: TryCasinoBotDonation runs on EVERY block, while CastSellFlow and NonMinerExchanges run only
		// when the budget is positive (≈ half the blocks at a 0.5 tx/block target). A median/mean gap that large
		// has to come from a path that does not run every time — this says which.
		//
		// These ARE per-block events (unlike the journal's, deleted above): the phase runs inside the block.
		public enum BotPhase
		{
			CasinoDonation,
			Budget,
			CastSellFlow,
			NonMinerExchanges
		}

		private const int BotPhaseCount = 4;
		private static readonly double[] _botPhaseMs = new double[BotPhaseCount];
		private static readonly double[] _botPhaseMsTotal = new double[BotPhaseCount];
		private static readonly int[] _botPhaseHits = new int[BotPhaseCount];

		// The UTXO reads the phase performs. AggregateSpendable is the wrapper every bidder / affordability
		// check / treasury read goes through; GetSpendableUtxos is the layer under it and is also reachable
		// from elsewhere, so counting BOTH says how much of the lower layer belongs to this phase.
		private static int _aggregateSpendableCalls;
		private static int _spendableReads;
		private static long _outpointsWalked;
		private static int _outpointsWalkedMax;

		// ── Mini-plan 26 A — inside the send ────────────────────────────────────────────────────────────────
		// Mini-plan 18's split put the cost in the sell-flow and named a cause from the code, never measured.
		// Mini-plan 26 §2 re-read F1's rows: the cost follows SENDS, and every automated spend goes through one
		// primitive (NetworkRoot.BuildAndBroadcastUtxoSpend, plus the pool payout and the dividend batch, which
		// build their own). This times that primitive stage by stage, splits each of its ~72 admissions into the
		// three checks they run, and counts the attempts that never became a send, so "where does a send's time
		// go" is measured rather than argued.
		//
		// A send's stages are CONTIGUOUS, like Phase: EnterSendStage closes the previous one. Sends never nest (a
		// broadcast admits, it does not send). A send still open when the next one begins, or when its block
		// closes, is closed as Abandoned and COUNTED, so a missed EndSend shows up as a number rather than as a
		// silently inflated stage.

		/// <summary>Where a send ran: the open phase, or inside the bot phase, the bot sub-phase.</summary>
		public enum SendContext
		{
			SellFlow,
			Exchange,
			Donation,
			Governance,
			Auctions,
			OtherInBlock,
			OutsideBlock
		}

		private const int SendContextCount = 7;

		private static readonly string[] SendContextNames =
		{
			"SellFlow", "Exchange", "Donation", "Governance", "Auctions", "OtherInBlock", "OutsideBlock",
		};

		/// <summary>The stages of one send, in the order the send primitive runs them.</summary>
		public enum SendStage
		{
			/// <summary>The owned-address set, the spendable read and coin selection.</summary>
			Select,
			/// <summary>The signing keys for every input (a derived address re-derives them, uncached).</summary>
			Keys,
			/// <summary>The outputs (with the change address), the txid and one signature per input.</summary>
			Sign,
			/// <summary>Admission to the sender's own mempool, and the MempoolChanged event.</summary>
			LocalAdmit,
			/// <summary>BroadcastTransaction: admission at every other node.</summary>
			FanOut
		}

		private const int SendStageCount = 5;

		private static readonly string[] SendStageNames = { "select", "keys", "sign", "local admit", "fan-out" };

		public enum SendOutcome
		{
			Sent,
			SelectFailed,
			KeysFailed,
			AdmitFailed,
			Abandoned
		}

		private const int SendOutcomeCount = 5;

		/// <summary>The check that ended a sell-flow or exchange attempt. <c>ReachedSend</c> means it called the
		/// send primitive, whatever that returned.</summary>
		public enum AttemptGate
		{
			NoNode,
			NeverMined,
			Warmup,
			LowBalance,
			AmountFee,
			Self,
			ReachedSend
		}

		private const int GateCount = 7;

		private static readonly string[] GateNames =
		{
			"no node", "never mined", "warmup", "low balance", "amount+fee", "self", "reached send",
		};

		// Admission: where it ran (0 local, 1 fan-out, 2 with no send open) × which check (0 dup, 1 sig, 2 utxo).
		private const int AdmitLocal = 0, AdmitFanOut = 1, AdmitOrphan = 2;

		private static int _botPhaseOpen = -1; // set by MarkBotPhase, cleared by every Enter and BeginBlock

		// The send in progress.
		private static bool _inSend;
		private static int _sendContext;
		private static long _sendBegin;
		private static int _sendOpenStage = -1;
		private static long _sendStageSince;
		private static readonly long[] _sendStageTicks = new long[SendStageCount];
		private static int _sendFanOutNodes, _sendFanOutAccepted;

		// The attempt in progress (sell-flow / exchange); its gate time is its own time minus the send inside it.
		private static bool _inAttempt;
		private static int _attemptContext;
		private static long _attemptBegin;
		private static double _attemptSendMsAtBegin;

		// Per block — reset by BeginBlock, written by EndBlock.
		private static int _blkBudget;
		private static int _blkSends, _blkSendFails, _blkSendInputs, _blkSendInputsMax, _blkKeyDerivations;
		private static double _blkSendMs;
		private static readonly double[] _blkStageMs = new double[SendStageCount];
		private static readonly int[] _blkSentByCtx = new int[SendContextCount];
		private static readonly double[] _blkSendMsByCtx = new double[SendContextCount];
		private static readonly int[] _blkInputsByCtx = new int[SendContextCount];
		private static readonly int[] _blkAttemptsByCtx = new int[SendContextCount];
		private static double _blkGateMs, _blkFirstMinedMs;
		private static long _blkFirstMinedBlocks;
		private static int _blkAdmissions, _blkOrphanAdmissions, _blkSigVerifies;
		private static long _blkTxsScanned;
		private static double _blkDupMs, _blkSigMs, _blkUtxoMs;
		private static long _blkAllocBegin;
		private static TimeSpan _blkPauseBegin;
		private static int _blkGc0Begin, _blkGc1Begin, _blkGc2Begin;

		// Session totals since arming — what the SENDS summary reports.
		private static double _sendMsTotal; // every send, every context: the attempt gate subtracts from it
		private static readonly double[,] _sesStageMs = new double[SendContextCount, SendStageCount];
		private static readonly double[] _sesSendMs = new double[SendContextCount];
		private static readonly int[,] _sesOutcomes = new int[SendContextCount, SendOutcomeCount];
		private static readonly long[] _sesInputs = new long[SendContextCount];
		private static int _sesInputsMax;
		private static long _sesKeyDerivations;
		private static readonly int[,] _sesGates = new int[SendContextCount, GateCount];
		private static readonly double[] _sesGateMs = new double[SendContextCount];
		private static double _sesFirstMinedMs;
		private static long _sesFirstMinedBlocks;
		private static int _sesFirstMinedCalls;
		private static readonly double[,] _sesAdmitMs = new double[3, 3];
		private static readonly int[] _sesAdmissions = new int[3];
		private static readonly int[] _sesAdmitRejected = new int[3];
		private static long _sesTxsScanned, _sesSigVerifies;
		private static int _sesSentFanOutNodes, _sesSentWithFanOut;
		private static double _sesBotTxMs, _sesSellFlowMs;
		// P4 — Sent sends by input count: 1 · 2–5 · 6–30 · 31+.
		private static readonly int[] _sesBucketSends = new int[4];
		private static readonly double[] _sesBucketMs = new double[4];
		private static readonly long[] _sesBucketInputs = new long[4];
		private static double _sesAllocMB, _sesGcPauseMs;

		// One row per send, held as numbers and formatted only after the block's timing has closed, so that writing
		// the trace is never billed to the stage it describes.
		private struct SendRow
		{
			public DateTime Utc;
			public int Height, Context, Inputs, BaseInputs, Outputs, Outcome, FanOutNodes, FanOutAccepted;
			public double TotalMs, SelectMs, KeysMs, SignMs, LocalMs, FanOutMs;
		}

		private static readonly List<SendRow> _sendRows = new(64);

		public const string SendTracePath = "user://logs/send_cost_trace.csv";

		private const string SendHeader =
			"reportUtc,chainHeight,context,inputs,baseAddressInputs,outputs,outcome,totalMs,selectMs,keysMs,signMs," +
			"localAdmitMs,fanOutMs,fanOutNodes,fanOutAccepted";

		private static bool _sendHeaderChecked;

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

			// Mini-plan 26 A — the run's last word, printed while still armed, and the per-send rows of sends made
			// since the last block, which would otherwise wait for a block that is not coming.
			if (!enabled)
			{
				ReportSends(final: true);
				FlushSendRows();
			}

			Enabled = enabled;
			BlockCount = 0;
			_inBlock = false;
			_openPhase = -1;
			_inSend = false;
			_inAttempt = false;
			ResetIntervalCounters();
			if (enabled)
			{
				ResetSendSession();
			}

			BlockPublished?.Invoke();

			GD.Print(enabled
				? string.Create(CultureInfo.InvariantCulture,
					$"[BlockCost] ARMED — one line per mined block, and a row in {TracePath}. " +
					$"UTXO rebuilds are counted across the interval and reported on the NEXT block's row. " +
					$"Mini-plan 26: one row per send in {SendTracePath}; nodes in the network now: {GodotBlockchainPort.Simulation.NetworkRoot.NodeCountForDiagnostics} (V1).")
				: "[BlockCost] disarmed.");
		}

		private static void ResetSendSession()
		{
			_sendMsTotal = 0d;
			Array.Clear(_sesStageMs);
			Array.Clear(_sesSendMs);
			Array.Clear(_sesOutcomes);
			Array.Clear(_sesInputs);
			_sesInputsMax = 0;
			_sesKeyDerivations = 0;
			Array.Clear(_sesGates);
			Array.Clear(_sesGateMs);
			_sesFirstMinedMs = 0d;
			_sesFirstMinedBlocks = 0;
			_sesFirstMinedCalls = 0;
			Array.Clear(_sesAdmitMs);
			Array.Clear(_sesAdmissions);
			Array.Clear(_sesAdmitRejected);
			_sesTxsScanned = 0;
			_sesSigVerifies = 0;
			_sesSentFanOutNodes = 0;
			_sesSentWithFanOut = 0;
			_sesBotTxMs = 0d;
			_sesSellFlowMs = 0d;
			Array.Clear(_sesBucketSends);
			Array.Clear(_sesBucketMs);
			Array.Clear(_sesBucketInputs);
			_sesAllocMB = 0d;
			_sesGcPauseMs = 0d;
			_sendRows.Clear();
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
				// Mini-plan 25 (INC-006) — this is not a timing detail, it is a finding: the previous block's processing
				// stopped before its checkpoint, so its row is about to be closed HERE, with its last open phase holding
				// all the real time until this block began. 14 such rows in one session read as "governance 3–11 s"
				// before anyone noticed every later phase was exactly 0. Said out loud, in the Output panel.
				GD.Print(string.Create(CultureInfo.InvariantCulture,
					$"[BlockCost] h={_chainHeight:N0} NEVER REACHED ITS CHECKPOINT — its processing was aborted; that row's last open phase is not a cost."));
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
			_stateWrites = 0;
			_stateWriteMs = 0d;
			_stateBytes = 0;
			_stateFilesThisBlock.Clear();
			Array.Clear(_botPhaseMs, 0, BotPhaseCount);
			_aggregateSpendableCalls = 0;
			_spendableReads = 0;
			_outpointsWalked = 0;
			_outpointsWalkedMax = 0;

			// Mini-plan 26 A.
			_botPhaseOpen = -1;
			_blkBudget = -1; // -1 = the budget was never computed in this block (the bot phase did not run)
			_blkSends = _blkSendFails = _blkSendInputs = _blkSendInputsMax = _blkKeyDerivations = 0;
			_blkSendMs = 0d;
			Array.Clear(_blkStageMs);
			Array.Clear(_blkSentByCtx);
			Array.Clear(_blkSendMsByCtx);
			Array.Clear(_blkInputsByCtx);
			Array.Clear(_blkAttemptsByCtx);
			_blkGateMs = _blkFirstMinedMs = 0d;
			_blkFirstMinedBlocks = 0;
			_blkAdmissions = _blkOrphanAdmissions = _blkSigVerifies = 0;
			_blkTxsScanned = 0;
			_blkDupMs = _blkSigMs = _blkUtxoMs = 0d;
			_blkBroadcastVerifies = 0;
			_blkBroadcastVerifyMs = 0d;
			_blkAllocBegin = GC.GetAllocatedBytesForCurrentThread();
			_blkPauseBegin = GC.GetTotalPauseDuration();
			_blkGc0Begin = GC.CollectionCount(0);
			_blkGc1Begin = GC.CollectionCount(1);
			_blkGc2Begin = GC.CollectionCount(2);
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
			_botPhaseOpen = -1; // a bot sub-phase belongs to the bot phase only (mini-plan 26 A)
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
		/// <summary>
		/// Mini-plan 16 A — one world-state file write. Called by every writer `WorldWriteGuard` already marks,
		/// which is the ready-made list of exactly the writers that matter.
		///
		/// <para><paramref name="startTicks"/> is a <see cref="Stopwatch.GetTimestamp"/> taken <b>before the
		/// file was opened</b>, and the caller must report <b>after it is closed</b> — on Windows the close and
		/// its metadata flush are most of a small file's cost, so timing only the `StoreString` would measure
		/// the cheap part and conclude the writes are free.</para>
		///
		/// <para>Counted even outside a block (`_inBlock` is not required): a writer firing between blocks is
		/// itself a finding, and the per-path totals are what part B ranks. Only the per-BLOCK columns need a
		/// block to be open.</para>
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteStateWrite(string path, long bytes, long startTicks)
		{
			if (!Enabled) return;

			double ms = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
			string key = path ?? "unknown";

			if (!_stateWritesByPath.TryGetValue(key, out StateWriteStat stat))
			{
				stat = new StateWriteStat();
				_stateWritesByPath[key] = stat;
			}

			stat.Writes++;
			stat.Ms += ms;
			stat.Bytes = bytes;

			if (!_inBlock) return;

			_stateWrites++;
			_stateWriteMs += ms;
			_stateBytes += bytes;
			_stateFilesThisBlock.Add(key);
		}

		/// <summary>
		/// Mini-plan 17 A1 — one journal filesystem operation. <paramref name="startTicks"/> follows
		/// <see cref="NoteStateWrite"/>'s contract: taken before the operation, reported after it completes.
		/// Counted outside a block too, for the same reason state writes are — the journal's work happens at
		/// bet rate, which is mostly between blocks.
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteJournalOp(JournalOp op, long startTicks)
		{
			if (!Enabled) return;

			double ms = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
			int i = (int)op;

			// No `_inBlock` branch: the journal's work happens between blocks, so a per-block split of it is
			// structurally empty (see the field comment above).
			_journalOpsTotal[i]++;
			_journalOpMsTotal[i] += ms;
		}

		/// <summary>
		/// Mini-plan 17 A1 — one segment rotation. **Counted, never timed**: a rotation is an envelope around
		/// the scans and deletes already counted above, so timing it too would double-count every millisecond
		/// it contains. The count is what the plan predicts; the time belongs to the operations inside it.
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteJournalRotation()
		{
			if (!Enabled) return;
			_journalRotationsTotal++;
		}

		/// <summary>
		/// Mini-plan 18 B — one sub-phase of the bot-transaction phase. Separate from <see cref="Phase"/> on
		/// purpose: that enum's phases are **contiguous** (each <see cref="Enter"/> closes the previous one), and
		/// slotting sub-phases into it would break that contract for the outer phases. These are timed
		/// independently and may be entered zero or one time per block.
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteBotPhase(BotPhase phase, long startTicks)
		{
			if (!Enabled) return;

			double ms = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
			int i = (int)phase;
			_botPhaseMs[i] += ms;
			_botPhaseMsTotal[i] += ms;
			_botPhaseHits[i]++;
		}

		/// <summary>Mini-plan 18 B — one <c>AggregateSpendable</c> call: the wrapper every bid cap, affordability
		/// check and treasury read goes through.</summary>
		[Conditional("DEBUG")]
		public static void NoteAggregateSpendable()
		{
			if (!Enabled || !_inBlock) return;
			_aggregateSpendableCalls++;
		}

		/// <summary>
		/// Mini-plan 18 B — one <c>GetSpendableUtxos</c> call and the number of outpoints it actually walked.
		/// <paramref name="outpointsWalked"/> is the quantity mini-plan 17's address index was built to shrink,
		/// so this verifies that as a COUNT rather than as a timing.
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteSpendableRead(int outpointsWalked)
		{
			if (!Enabled || !_inBlock) return;
			_spendableReads++;
			_outpointsWalked += outpointsWalked;
			if (outpointsWalked > _outpointsWalkedMax) _outpointsWalkedMax = outpointsWalked;
		}

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

		// ── Mini-plan 26 B — F-a's single verification ────────────────────────────────────────────────────────
		// M2's A/B switch and its two oracles lived here; they were deleted at close-out (Convention 3) after 311,740
		// old-path checks with no disagreement (plan §8.3). What stays is the count of the one verification F-a does.
		private static int _blkBroadcastVerifies;
		private static double _blkBroadcastVerifyMs;

		/// <summary>
		/// F-a's single verification in <c>NetworkSimulator.BroadcastTransaction</c>, counted as signature verifies
		/// (B1) and timed on its own: it runs inside the fan-out stage but outside every admission.
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteBroadcastVerify(long startTicks, int inputs)
		{
			if (!Enabled || startTicks == 0) return;
			double ms = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
			_sesSigVerifies += inputs;
			if (!_inBlock) return;
			_blkBroadcastVerifies += inputs;
			_blkSigVerifies += inputs;
			_blkBroadcastVerifyMs += ms;
		}

		// ── Mini-plan 26 A — the entry points ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// A timestamp when armed, and 0 otherwise. Always 0 outside DEBUG, so the release build pays an inlined
		/// constant on the hot admission path (~72 admissions per send). A start of 0 tells the matching Note call
		/// that nothing was timed.
		/// </summary>
		public static long Clock()
		{
#if DEBUG
			return Enabled ? Stopwatch.GetTimestamp() : 0L;
#else
			return 0L;
#endif
		}

		/// <summary>
		/// Marks which bot sub-phase is running, so a send inside the bot phase is attributed to the sell-flow, the
		/// exchanges or the donation cycle. Does not time anything: <see cref="NoteBotPhase"/> keeps its contract.
		/// </summary>
		[Conditional("DEBUG")]
		public static void MarkBotPhase(BotPhase phase)
		{
			if (!Enabled || !_inBlock) return;
			_botPhaseOpen = (int)phase;
		}

		/// <summary>The automated-tx budget this block's bot phase computed (how many sends it was asked for).</summary>
		[Conditional("DEBUG")]
		public static void NoteBotBudget(int budget)
		{
			if (!Enabled || !_inBlock) return;
			_blkBudget = budget;
		}

		private static SendContext CurrentContext()
		{
			if (!_inBlock) return SendContext.OutsideBlock;

			switch ((Phase)_openPhase)
			{
				case Phase.BotTransactions:
					return _botPhaseOpen switch
					{
						(int)BotPhase.CastSellFlow => SendContext.SellFlow,
						(int)BotPhase.NonMinerExchanges => SendContext.Exchange,
						(int)BotPhase.CasinoDonation => SendContext.Donation,
						_ => SendContext.OtherInBlock,
					};
				case Phase.Governance:
					return SendContext.Governance;
				case Phase.Auctions:
					return SendContext.Auctions;
				default:
					return SendContext.OtherInBlock;
			}
		}

		/// <summary>Opens a send, in its first stage (<see cref="SendStage.Select"/>).</summary>
		[Conditional("DEBUG")]
		public static void BeginSend()
		{
			if (!Enabled) return;
			if (_inSend)
			{
				CloseSend((int)SendOutcome.Abandoned, 0, 0, 0);
			}

			_inSend = true;
			_sendContext = (int)CurrentContext();
			_sendBegin = Stopwatch.GetTimestamp();
			Array.Clear(_sendStageTicks);
			_sendOpenStage = (int)SendStage.Select;
			_sendStageSince = _sendBegin;
			_sendFanOutNodes = 0;
			_sendFanOutAccepted = 0;
		}

		/// <summary>Closes the open send stage and opens <paramref name="stage"/>.</summary>
		[Conditional("DEBUG")]
		public static void EnterSendStage(SendStage stage)
		{
			if (!Enabled || !_inSend) return;
			long now = Stopwatch.GetTimestamp();
			if (_sendOpenStage >= 0)
			{
				_sendStageTicks[_sendOpenStage] += now - _sendStageSince;
			}

			_sendOpenStage = (int)stage;
			_sendStageSince = now;
		}

		/// <summary>
		/// Closes the send. <paramref name="baseInputs"/> are the inputs spent from the node's base address, whose
		/// keys need no derivation; every other input re-derived its signing context.
		/// </summary>
		[Conditional("DEBUG")]
		public static void EndSend(SendOutcome outcome, int inputs, int baseInputs, int outputs)
		{
			if (!Enabled || !_inSend) return;
			CloseSend((int)outcome, inputs, baseInputs, outputs);
		}

		private static void CloseSend(int outcome, int inputs, int baseInputs, int outputs)
		{
			long now = Stopwatch.GetTimestamp();
			if (_sendOpenStage >= 0)
			{
				_sendStageTicks[_sendOpenStage] += now - _sendStageSince;
				_sendOpenStage = -1;
			}

			_inSend = false;

			int c = _sendContext;
			double totalMs = TicksToMs(now - _sendBegin);
			int derivations = Math.Max(0, inputs - baseInputs);
			bool sent = outcome == (int)SendOutcome.Sent;

			_sendMsTotal += totalMs;
			_sesSendMs[c] += totalMs;
			_sesOutcomes[c, outcome]++;
			_sesInputs[c] += inputs;
			if (inputs > _sesInputsMax) _sesInputsMax = inputs;
			_sesKeyDerivations += derivations;
			for (int s = 0; s < SendStageCount; s++)
			{
				_sesStageMs[c, s] += TicksToMs(_sendStageTicks[s]);
			}

			if (sent)
			{
				int b = inputs <= 1 ? 0 : inputs <= 5 ? 1 : inputs <= 30 ? 2 : 3;
				_sesBucketSends[b]++;
				_sesBucketMs[b] += totalMs;
				_sesBucketInputs[b] += inputs;
				if (_sendFanOutNodes > 0)
				{
					_sesSentWithFanOut++;
					_sesSentFanOutNodes += _sendFanOutNodes;
				}
			}

			if (_inBlock && c != (int)SendContext.OutsideBlock)
			{
				if (sent) _blkSends++; else _blkSendFails++;
				_blkSendInputs += inputs;
				if (inputs > _blkSendInputsMax) _blkSendInputsMax = inputs;
				_blkKeyDerivations += derivations;
				_blkSendMs += totalMs;
				for (int s = 0; s < SendStageCount; s++)
				{
					_blkStageMs[s] += TicksToMs(_sendStageTicks[s]);
				}

				if (sent) _blkSentByCtx[c]++;
				_blkSendMsByCtx[c] += totalMs;
				_blkInputsByCtx[c] += inputs;
			}

			_sendRows.Add(new SendRow
			{
				Utc = DateTime.UtcNow,
				Height = _chainHeight,
				Context = c,
				Inputs = inputs,
				BaseInputs = baseInputs,
				Outputs = outputs,
				Outcome = outcome,
				FanOutNodes = _sendFanOutNodes,
				FanOutAccepted = _sendFanOutAccepted,
				TotalMs = totalMs,
				SelectMs = TicksToMs(_sendStageTicks[(int)SendStage.Select]),
				KeysMs = TicksToMs(_sendStageTicks[(int)SendStage.Keys]),
				SignMs = TicksToMs(_sendStageTicks[(int)SendStage.Sign]),
				LocalMs = TicksToMs(_sendStageTicks[(int)SendStage.LocalAdmit]),
				FanOutMs = TicksToMs(_sendStageTicks[(int)SendStage.FanOut]),
			});
		}

		/// <summary>Opens one sell-flow or exchange attempt: one candidate sender, whether or not it sends.</summary>
		[Conditional("DEBUG")]
		public static void BeginSendAttempt()
		{
			if (!Enabled) return;
			_inAttempt = true;
			_attemptContext = (int)CurrentContext();
			_attemptBegin = Stopwatch.GetTimestamp();
			_attemptSendMsAtBegin = _sendMsTotal;
		}

		/// <summary>
		/// Closes the attempt with the check that ended it. Its GATE time is the attempt's own time minus the send
		/// nested inside it (if it reached one), so the decision work is separated from the send it led to.
		/// </summary>
		[Conditional("DEBUG")]
		public static void EndSendAttempt(AttemptGate gate)
		{
			if (!Enabled || !_inAttempt) return;
			_inAttempt = false;

			double attemptMs = TicksToMs(Stopwatch.GetTimestamp() - _attemptBegin);
			double gateMs = Math.Max(0d, attemptMs - (_sendMsTotal - _attemptSendMsAtBegin));
			int c = _attemptContext;
			_sesGates[c, (int)gate]++;
			_sesGateMs[c] += gateMs;

			if (_inBlock)
			{
				_blkAttemptsByCtx[c]++;
				_blkGateMs += gateMs;
			}
		}

		/// <summary>
		/// One <c>FirstBlockHeightMinedBy</c> scan, mini-plan 18's named cause (P5). <paramref name="startTicks"/>
		/// comes from <see cref="Clock"/>; <paramref name="blocksScanned"/> is how far into the chain it walked.
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteFirstMinedScan(long startTicks, int blocksScanned)
		{
			if (!Enabled || startTicks == 0) return;
			double ms = TicksToMs(Stopwatch.GetTimestamp() - startTicks);
			_sesFirstMinedMs += ms;
			_sesFirstMinedBlocks += blocksScanned;
			_sesFirstMinedCalls++;
			if (!_inBlock) return;
			_blkFirstMinedMs += ms;
			_blkFirstMinedBlocks += blocksScanned;
		}

		/// <summary>
		/// One mempool admission (<c>BlockchainService.AddTransactionToPendingTransactions</c>), in three spans:
		/// the duplicate scan [<paramref name="begin"/>, <paramref name="dupEnd"/>], the signature check
		/// [dupEnd, <paramref name="sigEnd"/>] and the UTXO checks [sigEnd, <paramref name="end"/>]. A span end of 0
		/// means the admission was rejected before reaching it. It is attributed to the send stage that is open:
		/// the sender's own admission, the fan-out, or no send at all (an orphan, which is itself a finding).
		///
		/// <para><b>"Transactions scanned" is DERIVED, not counted:</b> a txid that passes the duplicate check was
		/// compared against every PENDING transaction (a scan with no early exit on a miss). Since mini-plan 26 F-b the
		/// confirmed half is one set lookup, so <paramref name="pendingBefore"/> is the whole of it; until then this
		/// count also carried the confirmed chain (17,452 per admission in M2's old path, plan §8.3).</para>
		/// </summary>
		[Conditional("DEBUG")]
		public static void NoteAdmission(long begin, long dupEnd, long sigEnd, long end, bool accepted, int inputs,
			int pendingBefore, bool verifiedHere)
		{
			if (!Enabled || begin == 0) return;

			long dupTicks = (dupEnd != 0 ? dupEnd : end) - begin;
			long sigTicks = dupEnd == 0 ? 0 : (sigEnd != 0 ? sigEnd : end) - dupEnd;
			long utxoTicks = sigEnd == 0 ? 0 : end - sigEnd;
			long scanned = dupEnd != 0 ? pendingBefore : 0;
			// Mini-plan 26 B — an admission handed F-a's verdict did not verify anything itself (B1 counts real work).
			int verifies = sigEnd != 0 && verifiedHere ? inputs : 0;

			int where = !_inSend ? AdmitOrphan
				: _sendOpenStage == (int)SendStage.LocalAdmit ? AdmitLocal
				: _sendOpenStage == (int)SendStage.FanOut ? AdmitFanOut
				: AdmitOrphan;

			_sesAdmitMs[where, 0] += TicksToMs(dupTicks);
			_sesAdmitMs[where, 1] += TicksToMs(sigTicks);
			_sesAdmitMs[where, 2] += TicksToMs(utxoTicks);
			_sesAdmissions[where]++;
			if (!accepted) _sesAdmitRejected[where]++;
			_sesTxsScanned += scanned;
			_sesSigVerifies += verifies;

			if (where == AdmitFanOut)
			{
				_sendFanOutNodes++;
				if (accepted) _sendFanOutAccepted++;
			}

			if (!_inBlock) return;
			_blkAdmissions++;
			if (where == AdmitOrphan) _blkOrphanAdmissions++;
			_blkDupMs += TicksToMs(dupTicks);
			_blkSigMs += TicksToMs(sigTicks);
			_blkUtxoMs += TicksToMs(utxoTicks);
			_blkTxsScanned += scanned;
			_blkSigVerifies += verifies;
		}

		/// <summary>
		/// Mini-plan 26 A — the session's send figures since arming, one verdict-shaped line per prediction, in the
		/// Godot editor's <b>Output panel</b> (GD.Print). Every <see cref="PerPathReportEveryBlocks"/> blocks, and
		/// once more on disarm. The CSVs carry the same numbers per block and per send; this is the readable form.
		/// </summary>
		private static void ReportSends(bool final)
		{
			if (!Enabled || BlockCount <= 0) return;

			int sent = 0, failed = 0, abandoned = 0;
			long inputs = 0;
			double sendMs = 0d;
			var stageMs = new double[SendStageCount];
			for (int c = 0; c < SendContextCount; c++)
			{
				sent += _sesOutcomes[c, (int)SendOutcome.Sent];
				abandoned += _sesOutcomes[c, (int)SendOutcome.Abandoned];
				for (int o = 0; o < SendOutcomeCount; o++)
				{
					if (o != (int)SendOutcome.Sent && o != (int)SendOutcome.Abandoned) failed += _sesOutcomes[c, o];
				}

				inputs += _sesInputs[c];
				sendMs += _sesSendMs[c];
				for (int s = 0; s < SendStageCount; s++)
				{
					stageMs[s] += _sesStageMs[c, s];
				}
			}

			double Pct(double part, double whole) => whole > 0d ? 100.0 * part / whole : 0d;
			int blocks = BlockCount;
			int allSends = Math.Max(1, sent + failed + abandoned);
			var sb = new StringBuilder();

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"[BlockCost] SENDS over {blocks:N0} block(s){(final ? " (final, on disarm)" : "")} — {sent:N0} sent, {failed:N0} failed, " +
				$"{abandoned:N0} abandoned · {(double)sent / blocks:N2} sent/block · {(double)inputs / allSends:N2} inputs/send (max {_sesInputsMax:N0})"));

			// V1 — every Sent send is admitted at every other node.
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n    V1 fan-out width per sent send: {(_sesSentWithFanOut > 0 ? (double)_sesSentFanOutNodes / _sesSentWithFanOut : 0d):N2} nodes " +
				$"(network: {GodotBlockchainPort.Simulation.NetworkRoot.NodeCountForDiagnostics} nodes, so expect one fewer)"));

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n    P2 send time {sendMs:N0} ms: select {Pct(stageMs[0], sendMs):N1}% · keys {Pct(stageMs[1], sendMs):N1}% · " +
				$"sign {Pct(stageMs[2], sendMs):N1}% · local admit {Pct(stageMs[3], sendMs):N1}% · fan-out {Pct(stageMs[4], sendMs):N1}%  (P2: fan-out >= 50%)"));

			double admitMs = 0d;
			var checkMs = new double[3];
			int admissions = 0, orphans = _sesAdmissions[AdmitOrphan];
			for (int w = 0; w < 3; w++)
			{
				admissions += _sesAdmissions[w];
				for (int k = 0; k < 3; k++)
				{
					checkMs[k] += _sesAdmitMs[w, k];
					admitMs += _sesAdmitMs[w, k];
				}
			}

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n    P3 admission time {admitMs:N0} ms over {admissions:N0} admissions: dup scan {Pct(checkMs[0], admitMs):N1}% · " +
				$"signature {Pct(checkMs[1], admitMs):N1}% · utxo checks {Pct(checkMs[2], admitMs):N1}%  (P3: signature >= 50%, dup <= 25%)"));

			// V2 and V3 — the scan's time per transaction compared, and verifies against inputs × admissions.
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n    V2 dup scan {(_sesTxsScanned > 0 ? checkMs[0] * 1e6 / _sesTxsScanned : 0d):N1} ns per tx scanned ({_sesTxsScanned:N0} scanned) · " +
				$"V3 {_sesSigVerifies:N0} signature verifies · {_sesKeyDerivations:N0} key derivations · orphan admissions {orphans:N0} (expected 0)"));

			for (int c = 0; c < SendContextCount; c++)
			{
				int n = 0;
				for (int o = 0; o < SendOutcomeCount; o++) n += _sesOutcomes[c, o];
				if (n == 0) continue;
				sb.Append(string.Create(CultureInfo.InvariantCulture,
					$"\n    {SendContextNames[c],-12} {_sesOutcomes[c, (int)SendOutcome.Sent],5:N0} sent of {n,5:N0} · {_sesSendMs[c],9:N0} ms · " +
					$"{_sesSendMs[c] / n,7:N1} ms/send · {(double)_sesInputs[c] / n,5:N2} inputs/send · fan-out {Pct(_sesStageMs[c, 4], _sesSendMs[c]),5:N1}%"));
			}

			for (int c = 0; c < SendContextCount; c++)
			{
				int attempts = 0;
				for (int g = 0; g < GateCount; g++) attempts += _sesGates[c, g];
				if (attempts == 0) continue;
				var gates = new StringBuilder();
				for (int g = 0; g < GateCount; g++)
				{
					if (_sesGates[c, g] > 0) gates.Append(string.Create(CultureInfo.InvariantCulture, $" {GateNames[g]} {_sesGates[c, g]:N0} ·"));
				}

				sb.Append(string.Create(CultureInfo.InvariantCulture,
					$"\n    attempts {SendContextNames[c],-9} {attempts:N0}, gate time {_sesGateMs[c]:N0} ms:{gates.ToString().TrimEnd('·', ' ')}"));
			}

			double botSendMs = _sesSendMs[(int)SendContext.SellFlow] + _sesSendMs[(int)SendContext.Exchange] + _sesSendMs[(int)SendContext.Donation];
			double gateTotal = _sesGateMs[(int)SendContext.SellFlow] + _sesGateMs[(int)SendContext.Exchange];
			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n    P1 sends {Pct(botSendMs, _sesBotTxMs):N1}% of botTx ({botSendMs:N0} of {_sesBotTxMs:N0} ms), gate {Pct(gateTotal, _sesBotTxMs):N1}%  (P1: >= 80% and < 10%) · " +
				$"P5 FirstBlockHeightMinedBy {_sesFirstMinedMs:N1} ms = {Pct(_sesFirstMinedMs, _sesSellFlowMs):N2}% of sell-flow over {_sesFirstMinedCalls:N0} call(s), " +
				$"{(_sesFirstMinedCalls > 0 ? (double)_sesFirstMinedBlocks / _sesFirstMinedCalls : 0d):N0} blocks/call  (P5: < 1%)"));

			string[] bucketNames = { "1", "2-5", "6-30", "31+" };
			var buckets = new StringBuilder();
			for (int b = 0; b < 4; b++)
			{
				buckets.Append(string.Create(CultureInfo.InvariantCulture,
					$" {bucketNames[b]}: {_sesBucketSends[b]:N0} sends, {(_sesBucketInputs[b] > 0 ? _sesBucketMs[b] / _sesBucketInputs[b] : 0d):N1} ms/input ·"));
			}

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n    P4 by inputs (sent):{buckets.ToString().TrimEnd('·', ' ')}  (precondition: >= 20 sends with >= 6 inputs, have {_sesBucketSends[2] + _sesBucketSends[3]:N0})"));

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n    per block: allocated {_sesAllocMB / blocks:N1} MB, GC pause {_sesGcPauseMs / blocks:N1} ms"));
			GD.Print(sb.ToString());
		}

		/// <summary>Writes the buffered per-send rows. Called after a block's timing has closed, and on disarm.</summary>
		private static void FlushSendRows()
		{
			if (_sendRows.Count == 0) return;

			var sb = new StringBuilder(_sendRows.Count * 128);
			foreach (SendRow r in _sendRows)
			{
				sb.Append(string.Format(CultureInfo.InvariantCulture,
					"{0:O},{1},{2},{3},{4},{5},{6},{7:F3},{8:F3},{9:F3},{10:F3},{11:F3},{12:F3},{13},{14}\n",
					r.Utc, r.Height, SendContextNames[r.Context], r.Inputs, r.BaseInputs, r.Outputs,
					(SendOutcome)r.Outcome, r.TotalMs, r.SelectMs, r.KeysMs, r.SignMs, r.LocalMs, r.FanOutMs,
					r.FanOutNodes, r.FanOutAccepted));
			}

			_sendRows.Clear();

			try
			{
				EnsureSendHeader();
				using FileAccess file = FileAccess.Open(SendTracePath, FileAccess.ModeFlags.ReadWrite);
				if (file == null)
				{
					return;
				}

				file.SeekEnd();
				file.StoreString(sb.ToString());
			}
			catch (Exception)
			{
				// A diagnostic must never be able to take down the thing it is diagnosing.
			}
		}

		private static void EnsureSendHeader()
		{
			if (_sendHeaderChecked)
			{
				return;
			}

			_sendHeaderChecked = true;
			EnsureTraceFile(SendTracePath, SendHeader);
		}

		/// <summary>Closes the block, writes its line and its row, and resets the interval counters.</summary>
		[Conditional("DEBUG")]
		public static void EndBlock()
		{
			if (!Enabled || !_inBlock) return;

			// Mini-plan 26 A — a send or an attempt still open here never closed: counted, not lost.
			if (_inSend)
			{
				CloseSend((int)SendOutcome.Abandoned, 0, 0, 0);
			}

			_inAttempt = false;

			long now = Stopwatch.GetTimestamp();
			// Read beside `now`, before this method allocates a single string of its own.
			double allocMB = (GC.GetAllocatedBytesForCurrentThread() - _blkAllocBegin) / (1024.0 * 1024.0);
			double gcPauseMs = (GC.GetTotalPauseDuration() - _blkPauseBegin).TotalMilliseconds;
			int gc0 = GC.CollectionCount(0) - _blkGc0Begin;
			int gc1 = GC.CollectionCount(1) - _blkGc1Begin;
			int gc2 = GC.CollectionCount(2) - _blkGc2Begin;
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
				$" · checkpoint {Ms(Phase.Checkpoint):N1} · other {totalMs - accounted:N1}" +
				$" · state {_stateWriteMs:N1} ms in {_stateWrites} write(s) across {_stateFilesThisBlock.Count}" +
				$" file(s) of {_stateBytes / 1024.0:N1} KB"));
			GD.Print(sb.ToString());

			// Mini-plan 26 A — the same block, inside its sends. A second line, so the first keeps its shape.
			int ctxSell = (int)SendContext.SellFlow, ctxExch = (int)SendContext.Exchange;
			int ctxGov = (int)SendContext.Governance, ctxDon = (int)SendContext.Donation;
			_sesBotTxMs += Ms(Phase.BotTransactions);
			_sesSellFlowMs += _botPhaseMs[(int)BotPhase.CastSellFlow];
			_sesAllocMB += allocMB;
			_sesGcPauseMs += gcPauseMs;
			GD.Print(string.Create(CultureInfo.InvariantCulture,
				$"[BlockCost]   sends {_blkSends} (+{_blkSendFails} failed) of budget {_blkBudget} · {_blkSendInputs} input(s), max {_blkSendInputsMax} · {_blkSendMs:N1} ms:" +
				$" select {_blkStageMs[0]:N1} · keys {_blkStageMs[1]:N1} · sign {_blkStageMs[2]:N1} · local {_blkStageMs[3]:N1} · fan-out {_blkStageMs[4]:N1} (dup {_blkDupMs:N1} · sig {_blkSigMs:N1} · utxo {_blkUtxoMs:N1} over {_blkAdmissions} admissions)" +
				$" · attempts sell {_blkAttemptsByCtx[ctxSell]} exch {_blkAttemptsByCtx[ctxExch]}, gate {_blkGateMs:N1} ms · allocated {allocMB:N1} MB, GC pause {gcPauseMs:N1} ms ({gc0}/{gc1}/{gc2})"));

			WriteTraceRow(string.Format(CultureInfo.InvariantCulture,
				"{0:O},{1},{2:O},{3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3},{11:F3},{12:F3},{13:F3},{14:F3},{15:F3},{16},{17},{18:F3},{19},{20:F3},{21},{22},{23},{24},{25:F3}," +
				"{26:F3},{27:F3},{28:F3},{29:F3},{30},{31},{32},{33}",
				DateTime.UtcNow, _chainHeight, _gameUtc, _minerNodeId, totalMs,
				Ms(Phase.Broadcast), Ms(Phase.DifficultyTrace), Ms(Phase.BotTransactions), Ms(Phase.Auctions),
				Ms(Phase.Governance), Ms(Phase.HistoricalEvents), Ms(Phase.Snapshot), Ms(Phase.CasinoRewards),
				Ms(Phase.Subscribers), Ms(Phase.Checkpoint), totalMs - accounted, _snapshotBytes, _snapshotWrites, _snapshotWriteMs,
				_utxoRebuilds, _utxoMs, _utxoNodes.Count,
				_stateWrites, _stateFilesThisBlock.Count, _stateBytes, _stateWriteMs,
				_botPhaseMs[(int)BotPhase.CasinoDonation], _botPhaseMs[(int)BotPhase.Budget],
				_botPhaseMs[(int)BotPhase.CastSellFlow], _botPhaseMs[(int)BotPhase.NonMinerExchanges],
				_aggregateSpendableCalls, _spendableReads, _outpointsWalked, _outpointsWalkedMax) +
				// Mini-plan 26 A — inside the send (same order as the header).
				string.Format(CultureInfo.InvariantCulture,
					",{0},{1},{2},{3},{4},{5:F3},{6:F3},{7},{8},{9},{10},{11},{12:F3},{13:F3},{14:F3},{15:F3},{16:F3},{17:F3}," +
					"{18},{19},{20:F3},{21},{22:F3},{23},{24:F3},{25},{26:F3},{27:F3},{28},{29:F3},{30},{31:F3},{32},{33}," +
					"{34:F3},{35:F3},{36},{37},{38}",
					_blkBudget, _blkAttemptsByCtx[ctxSell], _blkSentByCtx[ctxSell], _blkAttemptsByCtx[ctxExch], _blkSentByCtx[ctxExch],
					_blkGateMs, _blkFirstMinedMs, _blkFirstMinedBlocks,
					_blkSends, _blkSendFails, _blkSendInputs, _blkSendInputsMax, _blkSendMs,
					_blkStageMs[0], _blkStageMs[1], _blkStageMs[2], _blkStageMs[3], _blkStageMs[4],
					_blkAdmissions, _blkOrphanAdmissions, _blkDupMs, _blkTxsScanned, _blkSigMs, _blkSigVerifies, _blkUtxoMs, _blkKeyDerivations,
					_blkSendMsByCtx[ctxSell], _blkSendMsByCtx[ctxExch], _blkSentByCtx[ctxGov], _blkSendMsByCtx[ctxGov],
					_blkSentByCtx[ctxDon], _blkSendMsByCtx[ctxDon], _blkInputsByCtx[ctxSell], _blkInputsByCtx[ctxExch],
					allocMB, gcPauseMs, gc0, gc1, gc2) +
				string.Format(CultureInfo.InvariantCulture, ",{0},{1:F3}", _blkBroadcastVerifies, _blkBroadcastVerifyMs));

			// Outside the block's timing, which closed above: writing the rows is never billed to the stage they describe.
			FlushSendRows();

			ResetIntervalCounters();
			++BlockCount;
			if (BlockCount % PerPathReportEveryBlocks == 0)
			{
				ReportStateWritesByPath();
				ReportSends(final: false);
			}

			BlockPublished?.Invoke();
		}

		/// <summary>
		/// Mini-plan 16 A — the ranked table part B works from: which world-state files are written, how often
		/// per block, and what each one costs. Printed to the Godot editor's <b>Output panel</b> (GD.Print —
		/// the panel the developer actually reads) every <see cref="PerPathReportEveryBlocks"/> blocks.
		///
		/// <para>Ranked by <b>writes per block</b>, not by milliseconds, because that is the quantity the plan
		/// predicts and the one that reproduces. A file at more than 1.0 writes/block is a coalescing target;
		/// a file at exactly 1.0 is already at its floor and part B must leave it alone.</para>
		/// </summary>
		[Conditional("DEBUG")]
		public static void ReportStateWritesByPath()
		{
			if (!Enabled || _stateWritesByPath.Count == 0 || BlockCount <= 0) return;

			var rows = new List<KeyValuePair<string, StateWriteStat>>(_stateWritesByPath);
			rows.Sort((a, b) => b.Value.Writes.CompareTo(a.Value.Writes));

			var sb = new StringBuilder();

			// Mini-plan 17 A1 — the journal's own breakdown leads, because it is 94.9% of the write cost and
			// the question is WHICH of its operations that cost belongs to. Rotations are a count; the
			// milliseconds sit in the scans, deletes and appends they contain.
			double jAppendMs = _journalOpMsTotal[(int)JournalOp.Append];
			double jScanMs = _journalOpMsTotal[(int)JournalOp.DirectoryScan];
			double jDeleteMs = _journalOpMsTotal[(int)JournalOp.Delete];
			double jMetaMs = jScanMs + jDeleteMs;
			double jTotalMs = jAppendMs + jMetaMs;
			double rot = Math.Max(1, _journalRotationsTotal);

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"[BlockCost] JOURNAL over {BlockCount:N0} block(s) — {_journalRotationsTotal:N0} rotation(s)," +
				$" {_journalRotationsTotal / (double)Math.Max(1, BlockCount):N2}/block:" +
				$"\n    appends  {_journalOpsTotal[(int)JournalOp.Append],7:N0} · {jAppendMs,9:N1} ms" +
				$"\n    scans    {_journalOpsTotal[(int)JournalOp.DirectoryScan],7:N0} · {jScanMs,9:N1} ms" +
				$" · {_journalOpsTotal[(int)JournalOp.DirectoryScan] / rot,5:N2}/rotation" +
				$"\n    deletes  {_journalOpsTotal[(int)JournalOp.Delete],7:N0} · {jDeleteMs,9:N1} ms" +
				$" · {_journalOpsTotal[(int)JournalOp.Delete] / rot,5:N2}/rotation" +
				$"\n    METADATA share of journal time: {(jTotalMs > 0 ? 100.0 * jMetaMs / jTotalMs : 0),5:N1}%" +
				$"  (P1 predicts >= 50%)"));

			sb.Append(string.Create(CultureInfo.InvariantCulture,
				$"\n[BlockCost] world-state writes over {BlockCount:N0} block(s), ranked by writes per block:"));

			foreach (KeyValuePair<string, StateWriteStat> row in rows)
			{
				sb.Append(string.Create(CultureInfo.InvariantCulture,
					$"\n    {row.Value.Writes / (double)BlockCount,6:N2}/block · {row.Value.Writes,7:N0} write(s)" +
					$" · {row.Value.Ms,9:N1} ms total · {row.Value.Ms / Math.Max(1, row.Value.Writes),6:N3} ms each" +
					$" · {row.Value.Bytes,8:N0} B · {row.Key}"));
			}

			GD.Print(sb.ToString());
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
			EnsureTraceFile(TracePath, Header);
		}

		// Shared by the block trace and (mini-plan 26 A) the per-send trace.
		private static void EnsureTraceFile(string path, string header)
		{
			if (!DirAccess.DirExistsAbsolute("user://logs"))
			{
				DirAccess.MakeDirRecursiveAbsolute("user://logs");
			}

			if (FileAccess.FileExists(path))
			{
				// Rotate rather than append rows under a header that no longer describes them (ND.10j).
				using FileAccess existing = FileAccess.Open(path, FileAccess.ModeFlags.Read);
				string firstLine = existing?.GetLine() ?? string.Empty;
				existing?.Close();
				if (string.Equals(firstLine, header, StringComparison.Ordinal))
				{
					return;
				}

				DirAccess.RenameAbsolute(path, path + ".old");
			}

			using FileAccess created = FileAccess.Open(path, FileAccess.ModeFlags.Write);
			created?.StoreString(header + "\n");
		}
	}
}
