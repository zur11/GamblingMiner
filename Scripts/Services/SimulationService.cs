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

	private CalendarTimeService? _calendar;
	private UserStatsService? _userStats;
	private PrincipalBalanceService? _principal;
	private BankrollStateService? _bankroll;
	private BankrollProgramService? _bankrollProgram;
	private BlockSessionCheckpointService? _checkpoint;
	private FoundersMiningService? _founders;
	private NetworkRoot _networkRoot = null!;

	// Step 7.2: founder powers are recomputed only when a new block appears (Satoshi's confirmed-BTC
	// query is a full chain scan — too costly per frame). The cached power still feeds the difficulty
	// every frame. -1 forces a recompute on the first frame of a run.
	private int _lastFounderChainLen = -1;

	// Service-owned autobet engine (built from config; not handed from any scene).
	private DiceEngine? _engine;
	private Wallet? _wallet;
	private BetService? _betService;
	private BaseBetSession? _session;
	private PlayerAutobetConfig? _config;
	private double _accumulatorSeconds;

	// Bot runners (Phase 2): continuous background betting for casino bot nodes while the player autobet
	// is active. Single owner of bot state lives here, not in DiceGame.
	private readonly Dictionary<string, BotRunner> _botRunners = new();

	// Per-bot rolling play history (Bot Play-History screen). Keyed by nodeId — NOT on the transient
	// BotRunner — so it survives a recharge/restart (the history is the bot's, not the session's).
	// In-memory only: cleared on app restart. Each buffer caps at BotHistoryCapacity (newest kept).
	private const int BotHistoryCapacity = 260;
	private readonly Dictionary<string, Queue<BotPlayEntry>> _botHistories = new();

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
	}

	// Live betting rate for a node = its current total hardware credits (1 credit = 1 bet/sec, Phase 3).
	// Read FRESH each use (no cached BetsPerSecond) so buying/moving hardware mid-run takes effect at once.
	private static double HardwareRate(string nodeId) =>
		Math.Clamp(HardwareAllocationRepository.GetNode(nodeId).TotalCredits, 1, MaxAutoBetBaseAps);

	// Total active mining power for the difficulty feed-forward = Σ (player + running bots) bets/sec.
	private double GetTotalActiveMiningPower()
	{
		double total = 0d;
		foreach (double rate in GetActiveMiningRates().Values)
		{
			total += rate;
		}
		return total;
	}

	public override void _Process(double delta)
	{
		if (!IsRunning || _config == null || _session == null || _wallet == null)
		{
			return;
		}

		// Step 7.2: founders mine concurrently with the player (no autonomous clock). Recompute their
		// power only when a new block appeared (cheap-guard around Satoshi's full-chain BTC scan), then
		// feed player+bots+founders power to the difficulty regulator so block pacing stays constant.
		double otherMinersPower = GetTotalActiveMiningPower();
		RecomputeFoundersOnNewBlock(otherMinersPower);
		_networkRoot?.SetActiveMiningPower(otherMinersPower + (_founders?.TotalActiveFounderPower ?? 0d));

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
		_accumulatorSeconds = Math.Min(_accumulatorSeconds + simDelta, MaxBacklogSeconds);

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
		_founders.RecomputeFounderPowers(otherMinersPower, nowLocal, satoshiBtc);
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
			}
		}

		// Keep the bankroll autoload (the source of truth) in sync so every scene reflects it live.
		_bankroll?.SetBalance(_wallet.Balance);

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

		decimal amount = _bankrollProgram.AutoRechargeAmount > 0m
			? _bankrollProgram.AutoRechargeAmount
			: BankrollProgramService.DefaultAutoRechargeAmount;

		if (!_bankrollProgram.TryTransferBalanceToBankroll(_principal, _wallet, amount, "auto_recharge"))
		{
			return false;
		}

		if (_config.IsPlayerActive)
		{
			_userStats?.RegisterDeposit(amount, _wallet.Balance, DateTime.UtcNow);
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

	private void CaptureCheckpoint()
	{
		PersistFinancialState(true);
		if (_principal == null || _bankroll == null || _bankrollProgram == null || _checkpoint == null)
		{
			return;
		}

		DateTime historyUtc = _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow;
		DateTime calendarLocal = _calendar?.CurrentLocalDateTime ?? DateTime.Now;
		_checkpoint.CaptureCheckpoint(_principal, _bankroll, _bankrollProgram, historyUtc, calendarLocal);
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
			runner.AccumulatorSeconds = Math.Min(runner.AccumulatorSeconds + Math.Max(0d, delta), MaxBacklogSeconds);

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
		}
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
			UtcTimestamp = DateTime.UtcNow,
			Amount = amount,
			Direction = "balance_to_bankroll",
			Reason = "auto_recharge"
		});
		_networkRoot.SetNodeFinancialState(runner.NodeId, state, false);
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

	// Per-node mining rates for the active simulation (player + running bots), for the Block Explorer
	// "who's mining + speed" indicator. Empty when the background sim is idle.
	public IReadOnlyDictionary<string, double> GetActiveMiningRates()
	{
		var rates = new Dictionary<string, double>();
		if (IsRunning && _config != null)
		{
			rates[_config.ActiveNodeId] = HardwareRate(_config.ActiveNodeId);
		}
		foreach (BotRunner runner in _botRunners.Values)
		{
			if (runner.Session.IsRunning)
			{
				rates[runner.NodeId] = HardwareRate(runner.NodeId);
			}
		}
		return rates;
	}
}
