using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Dice;
using Scripts.Finance;
using Scripts.Game;
using Scripts.Sessions;
using Scripts.Betting;
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
		public bool IsPlayerActive = true;
		public BettingStrategyConfig Strategy = null!;
	}

	private const int MaxBetsPerFrame = 10;
	private const double MaxBacklogSeconds = 2.0;

	private CalendarTimeService? _calendar;
	private UserStatsService? _userStats;
	private PrincipalBalanceService? _principal;
	private BankrollStateService? _bankroll;
	private BankrollProgramService? _bankrollProgram;
	private BlockSessionCheckpointService? _checkpoint;
	private NetworkRoot _networkRoot = null!;

	// Service-owned autobet engine (built from config; not handed from any scene).
	private DiceEngine? _engine;
	private Wallet? _wallet;
	private BetService? _betService;
	private BaseBetSession? _session;
	private PlayerAutobetConfig? _config;
	private double _accumulatorSeconds;

	public bool IsRunning { get; private set; }
	public PlayerAutobetConfig? CurrentConfig => _config;

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

		_networkRoot = new NetworkRoot();
		AddChild(_networkRoot); // persistent — lives under this autoload
	}

	// DiceGame calls this when the player starts autobet. The service builds its own session/wallet,
	// seeded from the current bankroll (the single source of truth).
	public void StartPlayerAutobet(PlayerAutobetConfig config)
	{
		_config = config;
		_engine = new DiceEngine();
		decimal bankroll = _bankroll?.CurrentBalance ?? 0m;
		_wallet = new Wallet(bankroll);
		_betService = new BetService(_engine, _wallet, TransactionSource.Bet,
			() => _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow);

		var session = new AutoBetSession(_betService, _wallet, new ProgressiveBettingStrategy());
		session.Start(config.NumberOfBets, config.Strategy);
		_session = session;

		_accumulatorSeconds = 0d;
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
		IsRunning = false;
		_session = null;
		_betService = null;
		_wallet = null;
		_engine = null;
		_config = null;
		_accumulatorSeconds = 0d;

		if (_calendar != null)
		{
			_calendar.IsRunning = false;
			_calendar.IsAutobetActive = false;
		}
		_userStats?.SetHighFrequencyMode(false);
	}

	public override void _Process(double delta)
	{
		if (!IsRunning || _config == null || _session == null || _wallet == null)
		{
			return;
		}

		// The session may have stopped itself (profit/loss/block/insufficient) while we were away.
		if (!_session.IsRunning)
		{
			ClearRunningState();
			EmitSignal(SignalName.AutobetStopped);
			return;
		}

		double betsPerSecond = Math.Max(0.0001d, _config.BetsPerSecond);
		double interval = 1.0d / betsPerSecond;
		_accumulatorSeconds = Math.Min(_accumulatorSeconds + Math.Max(0d, delta), MaxBacklogSeconds);

		int executed = 0;
		while (_accumulatorSeconds >= interval && executed < MaxBetsPerFrame && _session.IsRunning)
		{
			_accumulatorSeconds -= interval;
			ExecutePlayerBetOnce();
			executed++;
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

		// One nonce attempt per bet (1 bet = 1 attempt), real PoW on the shared chain.
		long tsMs = new DateTimeOffset(tsUtc).ToUnixTimeMilliseconds();
		bool mined = _networkRoot.TryMineSingleNonceAttempt(_config.ActiveNodeId, out Block? block, tsMs);
		if (mined && block != null)
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
}
