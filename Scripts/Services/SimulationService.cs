using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Dice;
using Scripts.Finance;
using Scripts.Game;
using Scripts.Sessions;
using Scripts.Betting;
using Scripts.Hardware;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;
#nullable enable

// Background simulation (Phase 1c): OWNS and drives the player's autobet so it keeps running across
// scene changes. Single source of truth = BankrollStateService: the service builds its OWN wallet
// (seeded from the bankroll), bets on it, and writes the bankroll back each settled bet — so its
// wallet has NO subscriptions to any scene and there are no dangling-event crashes when a scene is
// freed. DiceGame and the StatusBar display from BankrollStateService.
//
// Bots are still ticked by DiceGame for now (Phase 2 moves them here). Manual betting stays in DiceGame.
public partial class SimulationService : Node
{
	public sealed class PlayerAutobetConfig
	{
		public int Chance;
		public bool BetHigh;
		public double BetsPerSecond;        // the APS the player selected
		public int NumberOfBets;            // 0 = infinite
		public string ActiveNodeId = "player";
		public string GameId = "Dice";
		public bool StopOnBlockMined;
		public bool AutoRecharge;            // auto top-up bankroll from main balance on insufficient funds
		public bool IsPlayerActive = true;
		public BettingStrategyConfig Strategy = null!;
	}

	// Snapshot of a bot node's strategy, handed in by DiceGame (so the service owns no UI state).
	public sealed class BotConfig
	{
		public string NodeId = "";
		public BettingStrategyConfig Strategy = null!;
		public int NumberOfBets;            // 0 = infinite
		public bool AutoRechargeEnabled;
		public int WinningChance;
		public bool BetHigh;
		public int BetsPerSecond;
	}

	// One settled-bet entry in a bot's rolling play history (for the Bot Play-History study screen).
	// Mirrors the player's BetTransactionEvent fields that matter for studying a strategy.
	public sealed record BotPlayEntry(
		decimal BetAmount,
		int Roll,
		decimal Multiplier,
		bool IsWin,
		decimal Profit,
		DateTime TimestampUtc);

	private sealed class BotRunner
	{
		public string NodeId = "";
		public Wallet Wallet = null!;
		public AutoBetSession Session = null!;
		public BotConfig Config = null!;
		public double AccumulatorSeconds;
	}

	private const int MaxBetsPerFrame = 10;
	private const double MaxBacklogSeconds = 2.0;
	private const int MaxAutoBetBaseAps = 99;

	// ── Round 2 (R2-T / R2-C1, 2026-07-27) — simulated-time saturation ────────────────────────────────
	// The bet engine can retain at most MaxBacklogSeconds of simulated time per frame: the Math.Min below
	// DISCARDS everything beyond it, permanently. Until now the calendar advanced by the FULL frame delta
	// regardless, so whenever a frame offered more sim-time than the engine could hold (≈ any frame under
	// ~45 fps at DevTimeScale 90), game time silently ran ahead of the mining work — which is what turned a
	// founder power spike into 1.5-day blocks (btc-pools-hardware-plan.md §R2.3a). Note the accumulator's
	// CARRIED remainder is not a loss: it executes next frame. Only the Math.Min clamp loses time.
	//
	// Two consumers: R2-C1 throttles the clock by the retained fraction (power-weighted across the player
	// and every running bot, since each keeps its own accumulator), and R2-T reports the same figures per
	// block into difficulty_trace.csv so saturation is measured instead of inferred.
	private double _frameWeightedOffered;
	private double _frameWeightedRetained;

	// One frame's contribution for a single bet engine (player or bot): how much of `simDelta` survived the
	// backlog clamp, weighted by that node's rate so the aggregate is attempts-accurate rather than
	// node-count-accurate.
	private void RecordSimTimeRetention(double offeredSeconds, double droppedSeconds, double betsPerSecond)
	{
		if (offeredSeconds <= 0d || betsPerSecond <= 0d) return;
		_frameWeightedOffered += offeredSeconds * betsPerSecond;
		_frameWeightedRetained += Math.Max(0d, offeredSeconds - droppedSeconds) * betsPerSecond;
	}

	private CalendarTimeService? _calendar;
	private UserStatsService? _userStats;
	private PrincipalBalanceService? _principal;
	private BankrollStateService? _bankroll;
	private BankrollProgramService? _bankrollProgram;
	private BlockSessionCheckpointService? _checkpoint;
	private FoundersMiningService? _founders;
	private CasinoScBalanceService? _casinoSc;
	// ND.8f: the casino's per-client ledger/bet-stats book. Lazy-resolved at the call sites (bot bets can
	// settle before/independently of any _Ready ordering assumptions).
	private CasinoClientLedgerService? _clientLedger;

	// ND.8f follow-up: per-client settled-bet feed for ClientsBetsHistory — a typed C# event (not a Godot
	// signal: BetTransactionEvent is not a Variant). Fired for the delegated player autobet AND every bot
	// bet; manual DiceGame bets can only happen while DiceGame itself is the active scene, so a live-feed
	// subscriber in another scene cannot miss them. (nodeId, gameId, settled bet.)
	public event Action<string, string, BetTransactionEvent>? ClientBetSettled;
	private PlayerBankAccountService? _playerBank;
	private NetworkRoot _networkRoot = null!;

	// Step 7.2: founder powers are recomputed only when a new block appears (Satoshi's confirmed-BTC
	// query is a full chain scan — too costly per frame). The cached power still feeds the difficulty
	// every frame. -1 forces a recompute on the first frame of a run.
	private int _lastFounderChainLen = -1;

	// Step 14 (ND.2): same per-new-block guard for the population scheduler (spawn check + power
	// recompute + telemetry row happen once per block, mirroring the founder pattern).
	private BtcNetworkDataService? _networkData;
	private int _lastPopulationChainLen = -1;

	// Service-owned autobet engine (built from config; not handed from any scene).
	private DiceEngine? _engine;
	private Wallet? _wallet;
	private BetService? _betService;
	private BaseBetSession? _session;
	private PlayerAutobetConfig? _config;
	private double _accumulatorSeconds;

	// Step 14 (ND.8b.3, D-ND8.18): true while this service froze the calendar because an open board vote
	// is waiting for the player's ballot (NetworkRoot.IsAwaitingPlayerVote). Bets are what advance time,
	// but the calendar also ticks on its own while delegated — the pause must pin the clock where the
	// vote-opening block left it, and must restore IsRunning only if WE were the ones who stopped it.
	private bool _pausedForBoardVote;

	// Bot runners (Phase 2): continuous background betting for casino bot nodes while the player autobet
	// is active. Single owner of bot state lives here, not in DiceGame.
	private readonly Dictionary<string, BotRunner> _botRunners = new();

	// Per-bot rolling play history (Bot Play-History screen). Keyed by nodeId — NOT on the transient
	// BotRunner — so it survives a recharge/restart (the history is the bot's, not the session's).
	// In-memory only: cleared on app restart. Each buffer caps at BotHistoryCapacity (newest kept).
	private const int BotHistoryCapacity = 260;
	private readonly Dictionary<string, Queue<BotPlayEntry>> _botHistories = new();

	private const string PlayerNodeId = "player";

	public bool IsRunning { get; private set; }
	public PlayerAutobetConfig? CurrentConfig => _config;

	// Last settled player bet, so DiceGame can feed its bet-history container while autobet is delegated.
	public BetTransactionEvent? LastSettledBetEvent { get; private set; }

	// Why the background autobet last stopped, for the "Auto stopped: <reason>" banner on return.
	public IBettingStrategy.StopReason LastAutobetStopReason { get; private set; }

	// Set when the autobet stops on its own; lets DiceGame show the reason even if it stopped while the
	// player was in another scene. Consumed (cleared) once shown.
	public bool StopNoticePending { get; private set; }
	public void ConsumeStopNotice() => StopNoticePending = false;

	// Display snapshots for the (live) DiceGame UI.
	public int SessionRemainingBets => _session?.RemainingBets ?? 0;
	public decimal SessionCurrentBet => _session?.CurrentBet ?? 0m;
	public bool SessionInfinite => _session?.IsInfinite ?? false;

	[Signal] public delegate void BetSettledEventHandler();
	[Signal] public delegate void AutobetStoppedEventHandler();

	public override void _Ready()
	{
		_calendar = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_userStats = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		_principal = GetNodeOrNull<PrincipalBalanceService>("/root/PrincipalBalanceService");
		_bankroll = GetNodeOrNull<BankrollStateService>("/root/BankrollStateService");
		_bankrollProgram = GetNodeOrNull<BankrollProgramService>("/root/BankrollProgramService");
		_checkpoint = GetNodeOrNull<BlockSessionCheckpointService>("/root/BlockSessionCheckpointService");
		_founders = GetNodeOrNull<FoundersMiningService>("/root/FoundersMiningService");
		_casinoSc = GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService");
		_playerBank = GetNodeOrNull<PlayerBankAccountService>("/root/PlayerBankAccountService");
		_networkData = GetNodeOrNull<BtcNetworkDataService>("/root/BtcNetworkDataService");

		_networkRoot = new NetworkRoot();
		AddChild(_networkRoot); // persistent — lives under this autoload

		// One shared dice engine for the player and all bots (stateless per Play call).
		_engine = new DiceEngine();
	}

	// DiceGame calls this when the player starts autobet. The service builds its own session/wallet,
	// seeded from the current bankroll (the single source of truth).
	public void StartPlayerAutobet(PlayerAutobetConfig config)
	{
		_config = config;
		_engine ??= new DiceEngine();
		decimal bankroll = _bankroll?.CurrentBalance ?? 0m;
		_wallet = new Wallet(bankroll);
		_betService = new BetService(_engine, _wallet, TransactionSource.Bet,
			() => _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow);

		var session = new AutoBetSession(_betService, _wallet, new ProgressiveBettingStrategy());
		session.Start(config.NumberOfBets, config.Strategy);
		_session = session;

		_accumulatorSeconds = 0d;
		_lastFounderChainLen = -1; // force a founder-power recompute on the first frame of this run
		_lastPopulationChainLen = -1; // and a population-scheduler recompute (Step 14)
		IsRunning = true;

		if (_calendar != null)
		{
			_calendar.SpeedMultiplier = 100.0d;
			_calendar.IsRunning = true;
			_calendar.IsAutobetActive = true;
		}
		_userStats?.SetHighFrequencyMode(true);
	}

	public void Stop()
	{
		if (_session is { IsRunning: true })
		{
			_session.Stop(IBettingStrategy.StopReason.ManualStop);
		}

		ClearRunningState();
	}

	private void ClearRunningState()
	{
		StopBots();
		IsRunning = false;
		_session = null;
		_betService = null;
		_wallet = null;
		_config = null;
		_accumulatorSeconds = 0d;

		if (_calendar != null)
		{
			_calendar.IsRunning = false;
			_calendar.IsAutobetActive = false;
		}
		_userStats?.SetHighFrequencyMode(false);
		_networkRoot?.SetActiveMiningPower(0d); // idle → difficulty feed-forward no-ops
		// R2-C1: an idle engine must not leave the clock throttled — nothing is being dropped when nothing
		// is running, and CalendarTimeService is used outside the delegated autobet too.
		if (_calendar != null)
		{
			_calendar.SimulationThrottle = 1d;
		}
		_frameWeightedOffered = 0d;
		_frameWeightedRetained = 0d;
	}

	// Live betting rate for a node = its current total hardware credits (1 credit = 1 bet/sec, Phase 3).
	// Read FRESH each use (no cached BetsPerSecond) so buying/moving hardware mid-run takes effect at once.
	private static double HardwareRate(string nodeId) =>
		Math.Clamp(HardwareAllocationRepository.GetNode(nodeId).TotalCredits, 1, MaxAutoBetBaseAps);

	// Non-founder network power = Σ (player + running bots) bets/sec. This is the "W_others" the founder
	// regulator competes against AND the base the founders' power is added to for the difficulty feed-forward,
	// so it must include ONLY the player + bots — NOT the founders or the casino (casino-pool attempts are
	// already part of each node's HardwareRate). It is deliberately computed directly here, NOT from
	// GetActiveMiningRates() (which also lists founders/casino for the Block Explorer display) — mixing the
	// two double-counts the founders into their own denominator and inflates Satoshi's share.
	private double GetTotalActiveMiningPower()
	{
		double total = 0d;
		if (IsRunning && _config != null)
		{
			total += HardwareRate(_config.ActiveNodeId);
		}
		foreach (BotRunner runner in _botRunners.Values)
		{
			if (runner.Session.IsRunning)
			{
				total += HardwareRate(runner.NodeId);
			}
		}
		return total;
	}

	public override void _Process(double delta)
	{
		if (!IsRunning || _config == null || _session == null || _wallet == null)
		{
			return;
		}

		// Step 14 (ND.8b.3, D-ND8.18): an open board vote in a company where the player holds NST pauses
		// the game until the player registers a ballot (CompanyDetails' Board Vote panel). Skip the whole
		// tick (no bets, no bots, no founder/scheduled mining) and freeze the calendar in place; restore
		// it the frame after the ballot lands. DiceGame gates manual bets on the same flag.
		if (NetworkRoot.IsAwaitingPlayerVote)
		{
			_pausedForBoardVote = true;
			// Re-checked every frame (not only on the pause edge): DiceGame re-asserts IsRunning on
			// scene re-entry (BindToRunningBackgroundAutobet), which must not thaw a vote-paused clock.
			if (_calendar is { IsRunning: true })
			{
				_calendar.IsRunning = false;
				_calendar.PersistCurrentTime();
			}
			return;
		}
		if (_pausedForBoardVote)
		{
			_pausedForBoardVote = false;
			if (_calendar != null)
			{
				_calendar.IsRunning = true;
			}
		}

		// Step 7.2: founders mine concurrently with the player (no autonomous clock). Recompute their
		// power only when a new block appeared (cheap-guard around Satoshi's full-chain BTC scan). Step 14
		// adds the population scheduler's two layers (visible cast + invisible mass) the same way, then
		// feeds player+bots+founders+scheduled power to the difficulty regulator so block pacing stays
		// constant while SHARES follow the historical curve (step14 plan §3.0).
		double otherMinersPower = GetTotalActiveMiningPower();
		RecomputeFoundersOnNewBlock(otherMinersPower);
		RecomputePopulationOnNewBlock(otherMinersPower);
		_networkRoot?.SetActiveMiningPower(otherMinersPower + (_founders?.TotalActiveFounderPower ?? 0d) + NetworkPopulationScheduler.TotalScheduledPower);

		// The session may have stopped itself (profit/loss/block/insufficient) while we were away.
		if (!_session.IsRunning)
		{
			// On insufficient funds, auto-recharge the bankroll (if enabled) and restart from base bet —
			// this now works across scenes too, not only inside DiceGame.
			if (!TryPlayerAutoRechargeAndRestart())
			{
				LastAutobetStopReason = _session.LastStopReason;
				StopNoticePending = true;
				ClearRunningState();
				EmitSignal(SignalName.AutobetStopped);
				return;
			}
		}

		// DEV/TEST time-acceleration: scale the execution delta by the calendar's DevTimeScale so bets fire
		// DevTimeScale× faster in real time. The calendar clock is scaled by the same factor (in
		// CalendarTimeService._Process), so attempts-per-IN-GAME-second — and thus the difficulty / power /
		// solvetime dynamics under measurement — stay invariant; only wall-clock time compresses. The power
		// fed to the difficulty regulator (HardwareRate / GetTotalActiveMiningPower) is deliberately NOT scaled.
		double simDelta = Math.Max(0d, delta) * Math.Max(1, _calendar?.DevTimeScale ?? 1);

		double betsPerSecond = HardwareRate(_config.ActiveNodeId);
		double interval = 1.0d / betsPerSecond;
		// R2-T/R2-C1: measure what the backlog clamp discards before applying it.
		_frameWeightedOffered = 0d;
		_frameWeightedRetained = 0d;
		double offeredBacklog = _accumulatorSeconds + simDelta;
		_accumulatorSeconds = Math.Min(offeredBacklog, MaxBacklogSeconds);
		RecordSimTimeRetention(simDelta, offeredBacklog - _accumulatorSeconds, betsPerSecond);

		int executed = 0;
		while (_accumulatorSeconds >= interval && executed < MaxBetsPerFrame && _session.IsRunning)
		{
			_accumulatorSeconds -= interval;
			ExecutePlayerBetOnce();
			executed++;
		}

		// Bots advance alongside the player autobet, in every scene (Phase 2).
		int botExecuted = TickBots(simDelta);

		// Step 7.2: drive the founders' concurrent attempts in lockstep with the time the player just
		// advanced (one founder attempt per its power-share of the player+bot attempts this frame).
		DriveFounderMining(executed + botExecuted, otherMinersPower);

		// Step 14 (ND.2): drive the scheduled network (visible cast + invisible mass) the same way —
		// concurrent miners in lockstep with the player's time advancement, never clock movers.
		DriveScheduledMining(executed + botExecuted, otherMinersPower);

		// R2-C1 (D-R2.5) — THE CLOCK MAY NOT SPEND TIME THE ENGINE COULD NOT SIMULATE. The retained
		// fraction is 1.0 whenever nothing was discarded, which is every frame that keeps up: below the
		// saturation knee this is byte-for-byte the previous behaviour. Above it, the calendar slows to
		// exactly the pace the bet engine sustained, so attempts-per-IN-GAME-second stays invariant — the
		// property SimulationService has always claimed and only actually had below the knee (§R2.3a).
		// Consequence: at a high DevTimeScale the game now advances more slowly in WALL-CLOCK terms instead
		// of quietly stretching in-game block times. That trade is the whole point — a simulation that runs
		// slower is honest; one that silently drops simulated work is not.
		double retainedFraction = _frameWeightedOffered > 0d
			? Math.Clamp(_frameWeightedRetained / _frameWeightedOffered, 0d, 1d)
			: 1d;
		if (_calendar != null)
		{
			_calendar.SimulationThrottle = retainedFraction;
		}
		// R2-T — the same figures, accumulated for the per-block difficulty trace.
		NetworkRoot.AccumulateSimSaturation(simDelta, simDelta * retainedFraction);
	}

	// Recompute founder powers exactly once per new block on the canonical chain. Satoshi's confirmed-BTC
	// query (GetNodeSpendableBalance) scans the whole chain, so it must not run every frame.
	private void RecomputeFoundersOnNewBlock(double otherMinersPower)
	{
		if (_founders == null || _networkRoot == null)
		{
			return;
		}

		int chainLen = _networkRoot.GetPlayerChainLength();
		if (chainLen == _lastFounderChainLen)
		{
			return;
		}

		_lastFounderChainLen = chainLen;
		decimal satoshiBtc = _networkRoot.GetNodeSpendableBalance(FoundersMiningService.SatoshiNodeId);
		DateTime nowLocal = _calendar?.CurrentLocalDateTime ?? DateTime.Now;
		// Step 14: Satoshi's SHARE regulator competes against the WHOLE non-founder network — player+bots
		// plus the scheduled cast/invisible mass (last block's cached total; one-block lag is fine, both
		// sides are feedback regulators). The founders' DRAIN denominator stays player+bots only (it must
		// match the attempt basis actually counted in DriveFounderMining).
		_founders.RecomputeFounderPowers(otherMinersPower + NetworkPopulationScheduler.TotalScheduledPower, nowLocal, satoshiBtc);

		// Phase 7.5 telemetry: one row per new block, so the founder ramp/decay tests can be measured.
		Block latest = _networkRoot.GetPlayerLatestBlock();
		decimal halBtc = _networkRoot.GetNodeSpendableBalance(FoundersMiningService.HalNodeId);
		decimal hearnBtc = _networkRoot.GetNodeSpendableBalance("mike_hearn");
		_founders.AppendTelemetry(latest.Index, latest.MinedByNodeId ?? string.Empty, latest.Timestamp, satoshiBtc, halBtc, hearnBtc);
	}

	// Founders perform their owed nonce attempts on their OWN chains (own coinbase). A founder-mined block
	// is an external block, exactly like a bot's: it checkpoints and can stop the player's stop-on-block run.
	private void DriveFounderMining(int nonFounderAttempts, double otherMinersPower)
	{
		if (_founders == null || _networkRoot == null || nonFounderAttempts <= 0)
		{
			return;
		}

		IReadOnlyList<(string founderId, int attempts)> drained = _founders.DrainFounderAttempts(nonFounderAttempts, otherMinersPower);
		if (drained.Count == 0)
		{
			return;
		}

		long tsMs = new DateTimeOffset(_calendar?.CurrentUtcDateTime ?? DateTime.UtcNow).ToUnixTimeMilliseconds();
		foreach ((string founderId, int attempts) in drained)
		{
			for (int i = 0; i < attempts; i++)
			{
				_networkRoot.TryMineSingleNonceAttempt(founderId, out Block? block, tsMs);
				if (block != null)
				{
					CaptureCheckpoint();
					StopPlayerOnExternalBlockMined();
				}
			}
		}
	}

	// Step 14 (ND.2) — once per new block: spawn-drip check (at most ONE new cast miner per block, so a
	// backlog never mass-spawns), power recompute for both scheduled layers, and the telemetry row.
	private void RecomputePopulationOnNewBlock(double otherMinersPower)
	{
		if (_networkData == null || _networkRoot == null)
		{
			return;
		}

		int chainLen = _networkRoot.GetPlayerChainLength();
		if (chainLen == _lastPopulationChainLen)
		{
			return;
		}

		_lastPopulationChainLen = chainLen;
		DateTime nowLocal = _calendar?.CurrentLocalDateTime ?? DateTime.Now;

		string? spawned = null;
		int target = _networkData.GetTargetVisibleMiners(nowLocal);
		if (BtcNetworkDataService.BaseCast + BotWalletRegistry.CastMiners.Count < target)
		{
			spawned = NetworkPopulationScheduler.NextCastName();
			BotWalletRegistry.AddCastMiner(spawned);
			if (!_networkRoot.RegisterCastMinerNode(spawned))
			{
				spawned = null;
			}
		}

		NetworkPopulationScheduler.Recompute(_networkData, nowLocal, otherMinersPower, _founders?.TotalActiveFounderPower ?? 0d);

		// Step 14 (ND.3): push the fullness-parity tx target for the NEXT blocks' automated traffic
		// (NetworkRoot consumes it inside ScheduleBotTransactionsAfterBlock on every mined block).
		decimal txTarget = _networkData.GetTargetTxPerBlock(nowLocal);
		NetworkRoot.SetScheduledTxTargetPerBlock(txTarget);

		Block latest = _networkRoot.GetPlayerLatestBlock();
		NetworkPopulationScheduler.AppendTelemetry(latest.Index, latest.MinedByNodeId ?? string.Empty, latest.Timestamp,
			otherMinersPower, _founders?.TotalActiveFounderPower ?? 0d, txTarget, _networkRoot.GetPlayerPendingTransactionCount(), spawned);
	}

	// Step 14 (ND.2) — the scheduled network's owed attempts, mined exactly like the founders': on each
	// miner's own candidate, external-block semantics (checkpoint + stop-on-block). Ghost blocks advance
	// the pseudonym rotation so consecutive invisible-mass blocks read as different anonymous rigs.
	private void DriveScheduledMining(int nonScheduledAttempts, double otherMinersPower)
	{
		if (_networkRoot == null || nonScheduledAttempts <= 0)
		{
			return;
		}

		IReadOnlyList<(string minerId, int attempts, bool isGhost)> drained =
			NetworkPopulationScheduler.DrainScheduledAttempts(nonScheduledAttempts, otherMinersPower);
		if (drained.Count == 0)
		{
			return;
		}

		long tsMs = new DateTimeOffset(_calendar?.CurrentUtcDateTime ?? DateTime.UtcNow).ToUnixTimeMilliseconds();
		foreach ((string minerId, int attempts, bool isGhost) in drained)
		{
			if (isGhost)
			{
				_networkRoot.EnsureGhostNodeRegistered(minerId);
			}

			for (int i = 0; i < attempts; i++)
			{
				_networkRoot.TryMineSingleNonceAttempt(minerId, out Block? block, tsMs);
				if (block != null)
				{
					if (isGhost)
					{
						NetworkPopulationScheduler.AdvanceGhostRotation();
					}
					CaptureCheckpoint();
					StopPlayerOnExternalBlockMined();
				}
			}
		}
	}

	private void ExecutePlayerBetOnce()
	{
		if (_session == null || _wallet == null || _config == null) return;

		if (_session.CurrentBet > _wallet.Balance)
		{
			_session.Stop(IBettingStrategy.StopReason.InsufficientBalance);
			return;
		}

		DateTime tsUtc = _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow;

		try
		{
			var (_, betEvent, _) = _session.ExecuteNext(_config.Chance, _config.BetHigh, tsUtc);
			LastSettledBetEvent = betEvent;
			if (_config.IsPlayerActive)
			{
				_userStats?.OnBetExecutedRegisterBet(_config.GameId, betEvent);
			}
		}
		catch (InvalidOperationException)
		{
			_session.Stop(IBettingStrategy.StopReason.InsufficientBalance);
			return;
		}

		PersistFinancialState(false);

		// Settle THIS bet's balances BEFORE mining/checkpoint (OQ-CG.10): if this same bet mines a block, the
		// checkpoint it captures must reflect the bet's own result, consistently with the bet-history boundary
		// (HistoryCheckpointUtcTicks) which already includes this bet. Keep the bankroll autoload (the source of
		// truth) in sync so every scene reflects it live, and route the inverse of the client's profit to/from
		// the casino SC bankroll — since ND.8f (OQ-11.1 resolved) EVERY client's bet routes there, so a
		// delegated autobet running on a bot node is simply that bot's play (correct semantics, no longer an
		// inconsistency with the comment); its settled bets also accrue in the casino's per-client book.
		_bankroll?.SetBalance(_wallet.Balance);
		_casinoSc?.ApplyBetResult(-(LastSettledBetEvent?.CreditedProfit ?? 0m));
		if (!_config.IsPlayerActive && LastSettledBetEvent != null)
		{
			_clientLedger ??= GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
			_clientLedger?.RegisterSettledBet(_config.ActiveNodeId, LastSettledBetEvent.BetAmount,
				LastSettledBetEvent.CreditedProfit, LastSettledBetEvent.IsWin);
		}

		// One nonce attempt per bet (1 bet = 1 attempt), routed by the active node's hardware allocation
		// (individual pool → own chain; casino pool → casino chain). Real PoW on the shared chain.
		long tsMs = new DateTimeOffset(tsUtc).ToUnixTimeMilliseconds();
		Block? block = RouteNonceAttempt(_config.ActiveNodeId, tsMs);
		if (block != null)
		{
			CaptureCheckpoint();
			if (_config.StopOnBlockMined && _session.IsRunning)
			{
				_session.Stop(IBettingStrategy.StopReason.StopOnBlockMined);
				FreezeCalendarAtBlockStop();
			}
		}

		if (LastSettledBetEvent != null)
			ClientBetSettled?.Invoke(_config.ActiveNodeId, _config.GameId, LastSettledBetEvent);
		EmitSignal(SignalName.BetSettled);
	}

	private void PersistFinancialState(bool persist)
	{
		if (_config == null || _wallet == null) return;

		var state = new NodeFinancialState
		{
			PrincipalBalance = _principal?.CurrentBalance ?? 0m,
			BankrollBalance = _wallet.Balance,
			AutoRechargeAmount = _bankrollProgram?.AutoRechargeAmount ?? BankrollProgramService.DefaultAutoRechargeAmount,
			TransferRecords = _bankrollProgram?.Records
				.Select(r => new BankrollProgramService.TransferRecord
				{
					UtcTimestamp = DateTime.SpecifyKind(r.UtcTimestamp, DateTimeKind.Utc),
					Amount = r.Amount,
					Direction = r.Direction,
					Reason = r.Reason
				})
				.ToList() ?? new List<BankrollProgramService.TransferRecord>()
		};

		_networkRoot.SetNodeFinancialState(_config.ActiveNodeId, state, persist);
	}

	// If the player's autobet stopped for insufficient funds and auto-recharge is on, top up the bankroll
	// from the main balance and restart the session from base bet. Returns true if it kept running.
	private bool TryPlayerAutoRechargeAndRestart()
	{
		if (_session == null || _config == null || _wallet == null || _betService == null) return false;
		if (!_config.AutoRecharge) return false;
		if (_session.LastStopReason != IBettingStrategy.StopReason.InsufficientBalance) return false;
		if (_bankrollProgram == null || _principal == null) return false;
		// SF.1.2 (D-SF.4): the service-level off-switch. When OFF, no auto top-up — the session stops and waits
		// for a manual Bankroll recharge (today's InsufficientBalance path, now player-chosen).
		if (!_bankrollProgram.AutoRechargeEnabled) return false;

		decimal amount = _bankrollProgram.AutoRechargeAmount > 0m
			? _bankrollProgram.AutoRechargeAmount
			: BankrollProgramService.DefaultAutoRechargeAmount;

		// SF.1.3 fallback (D-SF3.3): if Main can't cover the dose, try to stream it from the player's bank
		// reserve. No-op unless Auto-Deposit is ON and the bank holds SC — so in early game (empty bank, toggle
		// OFF) this changes nothing and the transfer below simply fails as it does today.
		if (_principal.CurrentBalance < amount)
			_playerBank?.TryAutoDeposit(amount);

		if (!_bankrollProgram.TryTransferBalanceToBankroll(_principal, _wallet, amount, "auto_recharge"))
		{
			return false;
		}

		if (_config.IsPlayerActive)
		{
			_userStats?.RegisterDeposit(amount, _wallet.Balance, _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow);
		}
		_bankroll?.SetBalance(_wallet.Balance);
		PersistFinancialState(false);

		// Restart the progression from base bet (mirrors DiceGame's recharge-then-restart behaviour).
		var session = new AutoBetSession(_betService, _wallet, new ProgressiveBettingStrategy());
		session.Start(_config.NumberOfBets, _config.Strategy);
		_session = session;

		EmitSignal(SignalName.BetSettled); // refresh UI: balance jumped, progression reset
		return true;
	}

	// Manual Main ↔ Bankroll transfers made while an autobet session is live (BankrollProgrammer). They
	// MUST mutate the SESSION wallet: a write that only touches BankrollStateService is clobbered by the
	// next settled bet's write-back (`_bankroll.SetBalance(_wallet.Balance)` in ExecutePlayerBet), which
	// destroys an injected amount (Main already paid it) or duplicates a withdrawn one. Returns false
	// when no session is running — the caller falls back to the idle BankrollStateService path.
	public bool TryManualTransferToBankroll(decimal amount)
	{
		if (!IsRunning || _wallet == null || _bankrollProgram == null || _principal == null) return false;
		if (!_bankrollProgram.TryTransferBalanceToBankroll(_principal, _wallet, amount, "manual_recharge"))
		{
			return false;
		}

		// Stats parity with the auto-recharge path above: a manual recharge also resets the since-recharge
		// stats scope (DiceGame's TryProgrammedBankrollTransfer registers every Main→Bankroll transfer too).
		if (_config?.IsPlayerActive == true)
		{
			_userStats?.RegisterDeposit(amount, _wallet.Balance, _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow);
		}
		_bankroll?.SetBalance(_wallet.Balance);
		PersistFinancialState(false);
		EmitSignal(SignalName.BetSettled); // refresh live UI: bankroll jumped
		return true;
	}

	public bool TryManualTransferToBalance(decimal amount)
	{
		if (!IsRunning || _wallet == null || _bankrollProgram == null || _principal == null) return false;
		// Clamp against the live session wallet — the authoritative balance mid-run. If the withdrawal
		// leaves less than the current bet, the session stops on InsufficientBalance naturally.
		amount = Money.Normalize(Math.Min(amount, _wallet.Balance));
		if (!_bankrollProgram.TryTransferBankrollToBalance(_principal, _wallet, amount, "manual_return"))
		{
			return false;
		}

		_bankroll?.SetBalance(_wallet.Balance);
		PersistFinancialState(false);
		EmitSignal(SignalName.BetSettled); // refresh live UI: bankroll dropped
		return true;
	}

	private void CaptureCheckpoint()
	{
		PersistFinancialState(true);
		if (_principal == null || _bankroll == null || _bankrollProgram == null || _checkpoint == null)
		{
			return;
		}

		// A checkpoint always captures the PLAYER's financial state — the same information as every block
		// commit, no matter which node the delegated session bets for. When the session was started with a
		// bot as the active node, the shared services hold the BOT's balances (DiceGame's node selector
		// rewrote them), so swap the player's NodeFinancialState mirror in for the capture and restore the
		// bot's values after (PersistFinancialState above just refreshed the bot's mirror).
		bool botSession = _config != null && !_config.IsPlayerActive;
		if (botSession)
		{
			NodeFinancialState playerState = _networkRoot.GetOrCreateNodeFinancialState(
				PlayerNodeId, _principal.CurrentBalance, _bankroll.CurrentBalance);
			_principal.SetBalance(playerState.PrincipalBalance);
			_bankroll.SetBalance(playerState.BankrollBalance);
			_bankrollProgram.ReplaceState(playerState.AutoRechargeAmount, playerState.TransferRecords);
		}

		DateTime historyUtc = _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow;
		DateTime calendarLocal = _calendar?.CurrentLocalDateTime ?? DateTime.Now;
		_checkpoint.CaptureCheckpoint(_principal, _bankroll, _bankrollProgram, historyUtc, calendarLocal);

		if (botSession)
		{
			NodeFinancialState botState = _networkRoot.GetOrCreateNodeFinancialState(
				_config!.ActiveNodeId, _principal.CurrentBalance, _bankroll.CurrentBalance);
			_principal.SetBalance(botState.PrincipalBalance);
			_bankroll.SetBalance(botState.BankrollBalance);
			_bankrollProgram.ReplaceState(botState.AutoRechargeAmount, botState.TransferRecords);
		}
	}

	// ── Bots (Phase 2) ──────────────────────────────────────────────────────────

	// Start continuous bot runners from DiceGame-provided strategy snapshots. Each runner owns its own
	// wallet (seeded from the node's persisted financial state) and session — no scene-bound state.
	public void StartBots(IReadOnlyList<BotConfig>? bots)
	{
		StopBots();
		if (bots == null) return;

		_engine ??= new DiceEngine();
		foreach (BotConfig cfg in bots)
		{
			if (cfg == null || cfg.Strategy == null || cfg.Strategy.BaseBet <= 0m) continue;
			if (string.Equals(cfg.NodeId, "player", StringComparison.Ordinal)) continue;

			_botRunners[cfg.NodeId] = BuildBotRunner(cfg);
		}
	}

	public void StopBots()
	{
		foreach (BotRunner runner in _botRunners.Values)
		{
			if (runner.Session.IsRunning)
			{
				runner.Session.Stop(IBettingStrategy.StopReason.ManualStop);
			}
			SaveBotFinancialState(runner);
		}
		_botRunners.Clear();
	}

	// One-shot bot burst, requested by DiceGame per manual bet (bots advance with manual betting too).
	// Independent of the background runners — builds temporary runners, bursts, then saves and discards.
	public void RunBotManualBurst(IReadOnlyList<BotConfig>? bots)
	{
		if (bots == null) return;
		_engine ??= new DiceEngine();

		foreach (BotConfig cfg in bots)
		{
			if (cfg == null || cfg.Strategy == null || cfg.Strategy.BaseBet <= 0m) continue;
			if (string.Equals(cfg.NodeId, "player", StringComparison.Ordinal)) continue;

			BotRunner runner = BuildBotRunner(cfg);
			int attempts = Math.Clamp(cfg.BetsPerSecond, 1, MaxAutoBetBaseAps);
			for (int i = 0; i < attempts && runner.Session.IsRunning; i++)
			{
				ExecuteBotBet(runner);
			}
			if (runner.Session.IsRunning)
			{
				runner.Session.Stop(IBettingStrategy.StopReason.ManualStop);
			}
			SaveBotFinancialState(runner);
		}
	}

	private BotRunner BuildBotRunner(BotConfig cfg)
	{
		NodeFinancialState financialState = _networkRoot.GetOrCreateNodeFinancialState(
			cfg.NodeId,
			BankrollProgramService.InitialPrincipalBalanceBaseline - BankrollProgramService.DefaultAutoRechargeAmount,
			BankrollProgramService.DefaultAutoRechargeAmount);
		var wallet = new Wallet(financialState.BankrollBalance);
		var betService = new BetService(_engine!, wallet, TransactionSource.Bet,
			() => _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow);
		var session = new AutoBetSession(betService, wallet, new ProgressiveBettingStrategy());
		session.Start(cfg.NumberOfBets, cfg.Strategy);
		return new BotRunner { NodeId = cfg.NodeId, Wallet = wallet, Session = session, Config = cfg };
	}

	// Returns the total number of bot bets (= nonce attempts) executed this frame, so the founder drive
	// can size the founders' lockstep attempts against ALL non-founder mining (player + bots).
	private int TickBots(double delta)
	{
		if (_botRunners.Count == 0) return 0;

		int totalExecuted = 0;
		foreach (BotRunner runner in _botRunners.Values.ToList())
		{
			if (!runner.Session.IsRunning)
			{
				// The session self-stops (in ApplyStopConditions) the instant the next progression bet
				// exceeds the bankroll. Mirror the player: on InsufficientBalance, recharge from the bot's
				// main balance and restart from base bet instead of removing the runner.
				if (runner.Session.LastStopReason == IBettingStrategy.StopReason.InsufficientBalance
					&& TryRechargeAndRestartBot(runner))
				{
					// Recharged + restarted; keep it running.
				}
				else
				{
					SaveBotFinancialState(runner);
					_botRunners.Remove(runner.NodeId);
					continue;
				}
			}

			double betsPerSecond = HardwareRate(runner.NodeId);
			double interval = 1.0d / Math.Max(0.0001d, betsPerSecond);
			// R2-T/R2-C1: each bot keeps its OWN accumulator, so each can saturate independently — a bot's
			// dropped sim-time removes its bets AND the founder/scheduled attempts drained off them.
			double botOffered = Math.Max(0d, delta);
			double botBacklog = runner.AccumulatorSeconds + botOffered;
			runner.AccumulatorSeconds = Math.Min(botBacklog, MaxBacklogSeconds);
			RecordSimTimeRetention(botOffered, botBacklog - runner.AccumulatorSeconds, betsPerSecond);

			int executed = 0;
			while (runner.AccumulatorSeconds >= interval && executed < MaxBetsPerFrame && runner.Session.IsRunning)
			{
				runner.AccumulatorSeconds -= interval;
				ExecuteBotBet(runner);
				executed++;
			}
			totalExecuted += executed;
		}

		return totalExecuted;
	}

	private void ExecuteBotBet(BotRunner runner)
	{
		if (!runner.Session.IsRunning) return;

		// Defensive: if the current (base, after a restart) bet can't be afforded, recharge + restart.
		if (runner.Session.CurrentBet > runner.Wallet.Balance && !TryRechargeAndRestartBot(runner))
		{
			runner.Session.Stop(IBettingStrategy.StopReason.InsufficientBalance);
			SaveBotFinancialState(runner);
			return;
		}

		try
		{
			DateTime tsUtc = _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow;
			var (_, betEvent, _) = runner.Session.ExecuteNext(
				Math.Clamp(runner.Config.WinningChance, 1, 95),
				runner.Config.BetHigh,
				tsUtc);

			// Record the settled bet in the bot's rolling history (for the study screen).
			PushBotPlayEntry(runner.NodeId, betEvent);

			// ND.8f (OQ-11.1 resolved): a bot's settled bet routes its inverse to the casino exactly like
			// the player's, and accrues in the casino's per-client bet-stats book (the bots' stats source
			// for ClientsBetsHistory). ApplyBetResult's save is throttled, so this is cheap per bet.
			_casinoSc?.ApplyBetResult(-betEvent.CreditedProfit);
			_clientLedger ??= GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
			_clientLedger?.RegisterSettledBet(runner.NodeId, betEvent.BetAmount, betEvent.CreditedProfit, betEvent.IsWin);
			ClientBetSettled?.Invoke(runner.NodeId, "Dice", betEvent); // bots are Dice-only (no GameId on BotConfig)

			long tsMs = new DateTimeOffset(tsUtc).ToUnixTimeMilliseconds();
			Block? block = RouteNonceAttempt(runner.NodeId, tsMs);
			if (block != null)
			{
				CaptureCheckpoint();
				StopPlayerOnExternalBlockMined();
			}
			SaveBotFinancialState(runner);
		}
		catch (InvalidOperationException)
		{
			runner.Session.Stop(IBettingStrategy.StopReason.InsufficientBalance);
			SaveBotFinancialState(runner);
		}
		catch (Exception ex)
		{
			GD.PushError($"[BotAutoBetError] node={runner.NodeId} {ex}");
			runner.Session.Stop(IBettingStrategy.StopReason.ManualStop);
			SaveBotFinancialState(runner);
		}
	}

	// Routes this bet's single nonce attempt by the node's hardware allocation (Phase 3, linear model):
	// individual-pool slots mine the node's own chain; casino-pool slots mine the casino pool's chain.
	// Returns the mined block (own-chain OR casino), or null if the attempt didn't solve a block.
	private Block? RouteNonceAttempt(string nodeId, long tsMs)
	{
		if (HardwareAllocationRepository.NextNonceTarget(nodeId) == HardwareAllocationRepository.NoncePoolTarget.Casino)
		{
			_networkRoot.TryCasinoNonceAttempt(out Block? casinoBlock, tsMs);
			return casinoBlock;
		}

		_networkRoot.TryMineSingleNonceAttempt(nodeId, out Block? ownBlock, tsMs);
		return ownBlock;
	}

	// When a bot mines a block, stop the player's background autobet if it requested stop-on-block.
	private void StopPlayerOnExternalBlockMined()
	{
		if (_session is { IsRunning: true } && _config?.StopOnBlockMined == true)
		{
			_session.Stop(IBettingStrategy.StopReason.StopOnBlockMined);
			FreezeCalendarAtBlockStop();
		}
	}

	// Stop-on-block must leave the game clock EXACTLY at the block it stopped on (canonical rule, OQ-BP.9:
	// the calendar always equals the timestamp of the block that defines the checkpointed world). Without
	// this, the session stops but CalendarTimeService.IsRunning stays true for the frame(s) until the next
	// _Process reaches ClearRunningState — so CalendarTimeService._Process keeps advancing the clock PAST the
	// block, and that drifted value later gets persisted to calendar_state.json (OQ-CG.9). We freeze it in
	// place rather than re-setting it: at this synchronous point the clock still equals the value CaptureCheckpoint
	// just read (no _Process ran in between), so freezing pins it bit-for-bit to the checkpoint. The drift was
	// normally sub-second, but the casino's on-demand loan (CG.1.8) added real-time latency to the block frame,
	// inflating the next frame's delta and making the overshoot large enough to notice.
	private void FreezeCalendarAtBlockStop()
	{
		if (_calendar == null) return;
		_calendar.IsRunning = false;
		_calendar.PersistCurrentTime();
	}

	private bool TryAutoRechargeBot(BotRunner runner)
	{
		if (!runner.Config.AutoRechargeEnabled)
		{
			return false;
		}

		NodeFinancialState state = _networkRoot.GetOrCreateNodeFinancialState(
			runner.NodeId,
			BankrollProgramService.InitialPrincipalBalanceBaseline - BankrollProgramService.DefaultAutoRechargeAmount,
			runner.Wallet.Balance);
		decimal amount = Money.Normalize(state.AutoRechargeAmount > 0m
			? state.AutoRechargeAmount
			: BankrollProgramService.DefaultAutoRechargeAmount);
		if (amount <= 0m || state.PrincipalBalance < amount)
		{
			return false;
		}

		state.PrincipalBalance = Money.Normalize(state.PrincipalBalance - amount);
		runner.Wallet.ApplyTransaction(new Scripts.Finance.Transaction(TransactionType.Deposit, TransactionSource.External, null, amount));
		state.BankrollBalance = runner.Wallet.Balance;
		state.TransferRecords ??= new List<BankrollProgramService.TransferRecord>();
		state.TransferRecords.Add(new BankrollProgramService.TransferRecord
		{
			UtcTimestamp = _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow,
			Amount = amount,
			Direction = "balance_to_bankroll",
			Reason = "auto_recharge"
		});
		_networkRoot.SetNodeFinancialState(runner.NodeId, state, false);

		// ND.8f: mirror the recharge into the casino's client ledger with wagered/profit snapshots from the
		// per-client book — the bots' equivalent of the player path's BankrollProgramService registration
		// (the "P/L since last bankroll recharge" baseline in ClientsBetsHistory).
		_clientLedger ??= GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
		CasinoClientLedgerService.ClientBetStats? book = _clientLedger?.GetBetStats(runner.NodeId);
		_clientLedger?.RegisterAutoRecharge(runner.NodeId, amount,
			_calendar?.CurrentUtcDateTime ?? DateTime.UtcNow, book?.TotalWagered ?? 0m, book?.NetProfit ?? 0m);
		return true;
	}

	// Recharge the bot's bankroll from its main balance (repeatedly if a single top-up can't cover the
	// base bet) and restart the progression from base bet. Returns true if the bot can keep running.
	private bool TryRechargeAndRestartBot(BotRunner runner)
	{
		decimal baseBet = runner.Config.Strategy?.BaseBet ?? 0m;
		bool recharged = TryAutoRechargeBot(runner);
		while (runner.Wallet.Balance < baseBet && TryAutoRechargeBot(runner))
		{
			recharged = true;
		}
		if (!recharged)
		{
			return false;
		}

		RestartBotSessionFromBase(runner);
		return runner.Wallet.Balance >= runner.Session.CurrentBet;
	}

	// Rebuilds a bot's session so its progression restarts from base bet (used right after a recharge).
	private void RestartBotSessionFromBase(BotRunner runner)
	{
		_engine ??= new DiceEngine();
		var betService = new BetService(_engine, runner.Wallet, TransactionSource.Bet,
			() => _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow);
		var session = new AutoBetSession(betService, runner.Wallet, new ProgressiveBettingStrategy());
		session.Start(runner.Config.NumberOfBets, runner.Config.Strategy);
		runner.Session = session;
	}

	private void SaveBotFinancialState(BotRunner runner)
	{
		NodeFinancialState state = _networkRoot.GetOrCreateNodeFinancialState(
			runner.NodeId,
			BankrollProgramService.InitialPrincipalBalanceBaseline - BankrollProgramService.DefaultAutoRechargeAmount,
			runner.Wallet.Balance);
		state.BankrollBalance = runner.Wallet.Balance;
		_networkRoot.SetNodeFinancialState(runner.NodeId, state, false);
	}

	// ── Bot play history (study screen) ─────────────────────────────────────────

	private void PushBotPlayEntry(string nodeId, BetTransactionEvent e)
	{
		if (!_botHistories.TryGetValue(nodeId, out Queue<BotPlayEntry>? buffer))
		{
			buffer = new Queue<BotPlayEntry>(BotHistoryCapacity);
			_botHistories[nodeId] = buffer;
		}

		buffer.Enqueue(new BotPlayEntry(
			e.BetAmount, e.Roll, e.Multiplier, e.IsWin, e.Profit, e.Timestamp));

		while (buffer.Count > BotHistoryCapacity)
		{
			buffer.Dequeue();
		}
	}

	// Last (up to 260) settled bets for a bot, newest first. Empty if the bot has no recorded plays.
	public IReadOnlyList<BotPlayEntry> GetBotPlayHistory(string nodeId)
	{
		if (_botHistories.TryGetValue(nodeId, out Queue<BotPlayEntry>? buffer) && buffer.Count > 0)
		{
			var list = buffer.ToList();
			list.Reverse(); // queue is oldest→newest; the screen wants newest first
			return list;
		}
		return Array.Empty<BotPlayEntry>();
	}

	// Bots that currently have a running session OR any recorded play history, sorted for a stable list.
	public IReadOnlyList<string> GetActiveBotNodeIds()
	{
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (var kvp in _botHistories)
		{
			if (kvp.Value.Count > 0) ids.Add(kvp.Key);
		}
		foreach (var kvp in _botRunners)
		{
			if (kvp.Value.Session.IsRunning) ids.Add(kvp.Key);
		}
		var result = ids.ToList();
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	// Per-node mining rates for the active simulation, for the Block Explorer "who's mining + speed"
	// indicator. Includes the player + running bots, the casino pool, and the founders (Satoshi/Hal) —
	// all the entities that mine while a player autobet is active. Empty when the background sim is idle.
	public IReadOnlyDictionary<string, double> GetActiveMiningRates()
	{
		var rates = new Dictionary<string, double>();
		if (!IsRunning)
		{
			return rates; // nothing mines unless the player's autobet is driving time
		}

		// Casino-pool hashrate = the casino-pool credits of every currently-mining node (those attempts
		// route to the casino chain). Accumulated as we add each active miner below.
		double casinoRate = 0d;

		if (_config != null)
		{
			rates[_config.ActiveNodeId] = HardwareRate(_config.ActiveNodeId);
			casinoRate += HardwareAllocationRepository.GetNode(_config.ActiveNodeId).CasinoPoolCredits;
		}

		foreach (BotRunner runner in _botRunners.Values)
		{
			if (runner.Session.IsRunning)
			{
				rates[runner.NodeId] = HardwareRate(runner.NodeId);
				casinoRate += HardwareAllocationRepository.GetNode(runner.NodeId).CasinoPoolCredits;
			}
		}

		if (casinoRate > 0d)
		{
			rates["casino"] = casinoRate;
		}

		// Founders mine concurrently in lockstep with the player's time advancement (Step 7.2). Show their
		// regulated power (same bets/sec-equivalent unit) while they are active.
		if (_founders != null)
		{
			if (_founders.SatoshiPower > 0d) rates[FoundersMiningService.SatoshiNodeId] = _founders.SatoshiPower;
			if (_founders.HalPower > 0d) rates[FoundersMiningService.HalNodeId] = _founders.HalPower;
		}

		// Step 14 (ND.2): the scheduled network — each powered cast miner at the era-standard power, plus
		// one aggregate "network" row for the invisible mass (its blocks carry rotating ghost names).
		foreach (string castId in NetworkPopulationScheduler.PoweredCastIds)
		{
			rates[castId] = NetworkPopulationScheduler.CastPowerEach;
		}
		if (NetworkPopulationScheduler.LastInvisiblePower > 0d)
		{
			rates["network"] = NetworkPopulationScheduler.LastInvisiblePower;
		}

		return rates;
	}
}
