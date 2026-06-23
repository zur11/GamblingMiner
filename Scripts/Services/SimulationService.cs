using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Scripts.Finance;
using Scripts.Sessions;
using Scripts.Betting;
using Scripts.Controllers;
using GodotBlockchainPort.Simulation;
using GodotBlockchainPort.Blockchain;
#nullable enable

// Background simulation (Phase 1b): owns and drives the PLAYER autobet so it keeps running across
// scene changes. DiceGame builds the session/wallet (plain C# objects) and HANDS them here; because
// this autoload holds them, they survive DiceGame being freed, and this service's _Process keeps
// betting + mining + advancing balances regardless of the active scene.
//
// Bots are still ticked by DiceGame for now (Phase 2 moves them here). Manual betting stays in DiceGame.
public partial class SimulationService : Node
{
	public sealed class PlayerAutobetConfig
	{
		public int Chance;
		public bool BetHigh;
		public double BetsPerSecond;        // the APS the player selected
		public string ActiveNodeId = "player";
		public string GameId = "Dice";
		public bool StopOnBlockMined;
		public bool IsPlayerActive = true;  // whether the active node is the player (drives stats)
	}

	private const int MaxBetsPerFrame = 10;
	private const double MaxBacklogSeconds = 2.0;

	// Autoload dependencies (resolved in _Ready).
	private CalendarTimeService? _calendar;
	private UserStatsService? _userStats;
	private PrincipalBalanceService? _principal;
	private BankrollStateService? _bankroll;
	private BankrollProgramService? _bankrollProgram;
	private BlockSessionCheckpointService? _checkpoint;
	// Persistent NetworkRoot instance (its state is static/shared, so this mirrors scene NetworkRoots).
	private NetworkRoot _networkRoot = null!;

	// Live player-autobet state (handed from DiceGame; survives DiceGame being freed).
	private BaseBetSession? _session;
	private WalletController? _walletController;
	private PlayerAutobetConfig? _config;
	private double _accumulatorSeconds;
	private int _lastCheckpointBlockIndex = -1;

	public bool IsRunning { get; private set; }
	public PlayerAutobetConfig? CurrentConfig => _config;

	// Raised after each settled player bet so the (live) DiceGame UI can refresh from current state.
	[Signal] public delegate void BetSettledEventHandler();

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

	// Called by DiceGame when the player starts autobet. The session/wallet are already built + started.
	public void StartPlayerAutobet(BaseBetSession session, WalletController walletController, PlayerAutobetConfig config)
	{
		_session = session;
		_walletController = walletController;
		_config = config;
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
		_walletController = null;
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
		if (!IsRunning || _config == null || _session == null || _walletController == null)
		{
			return;
		}

		// The session may have stopped itself (profit/loss/block/insufficient) while we were away.
		if (!_session.IsRunning)
		{
			ClearRunningState();
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
		if (_session == null || _walletController == null || _config == null) return;

		if (_session.CurrentBet > _walletController.Balance)
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

		// One nonce attempt for this bet (1 bet = 1 attempt); mining is real PoW via the shared chain.
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

		// Keep the bankroll autoload in sync so the StatusBar (and any scene) reflects it live.
		_bankroll?.SetBalance(_walletController.Balance);

		EmitSignal(SignalName.BetSettled);
	}

	private void PersistFinancialState(bool persist)
	{
		if (_config == null || _walletController == null) return;

		var state = new NodeFinancialState
		{
			PrincipalBalance = _principal?.CurrentBalance ?? 0m,
			BankrollBalance = _walletController.Balance,
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
