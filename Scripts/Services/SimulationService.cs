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

	// ── Mini-plan 08 §2 — per-bet timestamp fidelity ────────────────────────────────────────────────────
	//
	// How far BEFORE the calendar's current instant the bet being settled right now actually occurred, in
	// game-seconds. Zero everywhere except inside the two settle loops, which set it per bet and clear it
	// on the way out — so every other reader of the clock is untouched by construction.
	//
	// A field rather than a parameter because the timestamp reaches BetService through a provider delegate
	// captured at construction (`() => SettleTimestampUtc()`), and threading an argument through
	// ExecuteNext would change a signature four call sites deep for a value only these two loops ever set.
	private double _settleBackdateGameSeconds;

	// Used only when the calendar is absent — the same fallback shape the timestamp reads already use. The
	// literal is DiceGame's GameSecondsPerRealSecond; it is duplicated rather than referenced because a
	// service must not depend on a scene, and it is only ever reached when there is no clock at all.
	private const double GameSecondsPerRealSecondFallback = 100.0d;

	// The clock, minus this bet's distance from the end of the frame. THE SINGLE SOURCE for every settled
	// bet's timestamp, player and bot alike; if a third settle path ever appears it must come through here.
	private DateTime SettleTimestampUtc()
	{
		DateTime now = _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow;
		return _settleBackdateGameSeconds > 0d ? now.AddSeconds(-_settleBackdateGameSeconds) : now;
	}

	// ── Mini-plan 08 P3 — the back-date may not reach further back than the clock moved forward ────────
	//
	// MEASURED FAILURE (Tools/verify-bet-journal.js, first run, 2026-09-10): 114 bets timestamped BEFORE
	// their predecessor in write order, median 20.3 game-seconds backwards, worst 102.7.
	//
	// The arithmetic. A batch spans `(planned − 1) × interval × SpeedMultiplier` game-seconds, but the
	// calendar advances `simDelta × SpeedMultiplier ×` **SimulationThrottle** — and the backlog clamp can
	// hand `planned` a carried remainder that the clock never had to pay for. Whenever the span exceeds
	// what the clock actually moved, the frame's FIRST bet lands before the previous frame's LAST one.
	//
	// Raising MaxBetsPerFrame 10 → 20 is what made it frequent: at 10 credits × 9000X the span was 90
	// game-seconds against a 150-second advance (regressing only below throttle 0.60, which the measured
	// dips to 63% just cleared); at 20 the span is 140 against 150, so ANY dip below 0.93 regresses.
	//
	// **CLAMP, DO NOT PREDICT.** §2.2 declined to compute the clock's advance from
	// `simDelta × SpeedMultiplier × SimulationThrottle` because recomputing it "risks disagreeing with what
	// the calendar did, and a disagreement in the wrong direction future-dates a bet". That reasoning was
	// sound and incomplete — it weighed only the hazard of the option it rejected, and the opposite
	// disagreement past-dates one, which is what shipped. Reading the clock's ACTUAL movement has neither
	// hazard: it is not a forecast, it needs no throttle (the aggregate is power-weighted across the bots
	// and is not known until after TickBots), and at throttle 1 with no carried backlog it never binds.
	private DateTime _previousFrameClockUtc = DateTime.MinValue;

	// Per-bet spacing for a batch of `planned` bets, compressed if — and only if — the nominal spacing
	// would not fit inside the game-time this frame actually bought. Shared by the player and the bots
	// because they settle in the same frame against the same clock, and a fix applied to one of two
	// identical loops is how the next investigation gets a mixed dataset.
	private double ClampedStepGameSeconds(double nominalStep, int planned, DateTime clockNowUtc)
	{
		if (planned <= 1 || nominalStep <= 0d)
		{
			return nominalStep;
		}

		// First frame of a run, or the clock jumped backwards (a checkpoint restore, a freeze-on-block).
		// Re-seed rather than clamp against a stale or negative span: a jump is not a throttle.
		if (_previousFrameClockUtc == DateTime.MinValue || clockNowUtc < _previousFrameClockUtc)
		{
			return nominalStep;
		}

		double availableGameSeconds = (clockNowUtc - _previousFrameClockUtc).TotalSeconds;

		// Unclamped only if the batch's OLDEST bet lands strictly AFTER the previous frame's clock. Strictly,
		// because the previous frame's last bet was stamped at backdate 0 — which IS that clock — so a batch
		// reaching exactly back to it lands on a bet already written.
		double nominalSpan = (planned - 1) * nominalStep;
		if (nominalSpan < availableGameSeconds)
		{
			return nominalStep;
		}

		// ── Clamped: spread the batch over the HALF-OPEN interval (previousFrameClock, clockNow] ──────────
		// `planned` bets: the last on clockNow, the first ONE FULL STEP after the previous frame's clock.
		//
		// This was `available ÷ (planned − 1)` — the CLOSED interval — and the 99-credit escalation measured
		// what that costs: 864 same-millisecond pairs in 202,057 bets at 3000X, every one exactly size 2 and
		// 857 of 864 followed by compressed spacing. The closed interval puts a clamped frame's first bet ON
		// the previous frame's clock, where that frame's last bet already sits. At tick resolution: 448 exact
		// duplicates and 416 at exactly +1 tick, the +1 being DateTime.AddSeconds truncating the double
		// back-date's fractional tick.
		//
		// That truncation is also why ordering never broke: it always errs FORWARD, so a boundary collision
		// landed on or after the previous bet, never before it. Had it rounded to nearest, about half of those
		// 416 would have been one-tick REGRESSIONS. Monotonicity was being held by a rounding direction nobody
		// chose. With a full step of separation, which way a double rounds stops mattering.
		//
		// RETRACTED WITH THIS CHANGE: the conditional one-tick floor that stood here. It was built for sub-tick
		// collapse INSIDE a batch — a mechanism reasoned rather than observed, which would produce groups of up
		// to `planned` bets; no group in any run has exceeded 2. Under the half-open interval it is also
		// redundant: whenever the frame can hold `planned` distinct ticks, `available ÷ planned` is already at
		// least one tick. Removed rather than kept as a no-op.
		//
		// What remains at the true limit, stated rather than hidden: a frame whose clock advanced by less than
		// `planned` ticks (4 µs of game time at 40 bets) cannot give every bet a distinct instant. It still
		// never regresses and never passes the clock — the span is strictly shorter than the advance, and
		// truncation only ever moves a timestamp later — so ordering and the clock bound hold even there, in a
		// frame that effectively did not advance.
		return availableGameSeconds / planned;
	}

	// How many bets this frame's accumulator can afford, capped the same way the loop that follows is.
	// Computed BEFORE the loop because a bet's back-date is its distance from the LAST bet of the batch,
	// which is not knowable while the batch is still draining.
	private static int PlannedBetsThisFrame(double accumulatorSeconds, double interval)
	{
		if (interval <= 0d)
		{
			return 1;
		}

		return Math.Clamp((int)(accumulatorSeconds / interval), 1, MaxBetsPerFrame);
	}

	// 10 → 20 (mini-plan 08 P1, 2026-08-30). THE FIRST TIME THIS NUMBER HAS BEEN A MEASUREMENT.
	//
	// It was 10 for its whole life with nothing behind it, and P1's §3 assumed it was loosely conservative.
	// It was not: at the 1,414 µs a bet cost before P1's fixes, a 16.67 ms frame fit **12.8 bets if it did
	// nothing else**, so 10 was at ~78% of an unshareable budget. The constant was accidentally close to
	// right, which is not the same as justified.
	//
	// After P1 fixed the two per-bet costs that were 93% of a bet (the bankroll disk write and DiceGame's
	// per-bet UI rebuild), a bet costs **296 µs measured**. 20 bets is then 5.9 ms — 35% of the frame — which
	// leaves real headroom for rendering, the bot runners, the founders and the scheduled network, all of
	// which draw from the same 16.67 ms.
	//
	// **Why raising it is legitimate here and is NOT the move §38.7 forbids.** That rule says a low `Sim%`
	// means "find what is eating the frame", never "raise MaxBetsPerFrame" — because handing a saturated
	// frame more work makes it worse. The saturation was found and removed FIRST; this constant is what
	// remained binding afterwards. Order is the whole difference, and it is why the number may move now and
	// could not before.
	//
	// 20 → 40 (2026-09-12), to serve 99 credits at 2000X: `99 × 20 ÷ 60 =` **33 bets/frame**, which 20
	// cannot express at all. 40 clears that demand by ~21%.
	//
	// ⚠ THE MEANING OF THIS CONSTANT CHANGED WITH THAT STEP, and the change is worth stating rather than
	// leaving for someone to infer from a slow frame. At the measured 276.5 µs/bet (99 credits, P4), 40 bets
	// is **11.1 ms — 66% of a 16.67 ms frame**, and 33 is 9.1 ms (55%). Up to and including 20 this cap sat
	// comfortably inside a frame that had plenty left over for rendering, the bot runners, the founders and
	// the scheduled network. It no longer does.
	//
	// So from 40 onward the cap is set by **the demand it is meant to serve**, not by a budget the frame can
	// absorb without noticing, and a run that actually reaches it will show as `Sim%` dips rather than as
	// dropped work. That is still the honest mechanism — R2-C1 converts a frame the engine cannot fill into
	// a slower wall clock, never into distorted in-game dynamics (P4 demonstrated exactly this at 94.3%
	// retention) — but it is a different régime, and **the next person to raise this number should re-price
	// a bet first rather than extrapolating from here.**
	//
	// 40 → 160 (D-10.5, 2026-09-20), and the instruction above was followed: a bet WAS re-priced first.
	// Mini-plan 10 took DiceGame's per-bet cost from 0.198 ms to **0.025** (the bet list stopped moving rows and
	// now paints only the ~14 inside the scroll's viewport, once per frame), so 160 bets is ~3.7 ms of the
	// frame, back inside the régime the ⚠ above says 40 had left. The demand it serves is the clock's ceiling:
	// `99 credits × 90 ÷ 60 =` **149 bets/frame**, which 40 cannot express — at 40 the game could not exceed
	// 2,400 bets/s whatever the budget allowed. Two sessions measured cap 160 delivering all ~8,910 bets/s at
	// 58–60 fps, frame p50 16.6 ms, p95 22–24 ms, retention 1.000.
	//
	// 160 → 180 (D-11.2, 2026-09-21). This cap is PER ENGINE: the player's loop and each bot runner's loop apply
	// it separately. Mini-plan 11 ran all five at the hardware cap and found it binding BEFORE the frame did. At
	// 56–57 fps it cut 60–93% of frames, so the densest configuration delivered 99.2–99.5% of its demand and the
	// clock ran slow. The reason is arithmetic: one engine at the cap and the clock's ceiling asks for 8,910 bets/s,
	// which is 160 bets per frame at ~55.7 fps. Any frame longer than ~18 ms therefore under-serves it, and a full
	// 800-bet frame takes about that long, which leaves nothing to catch up after a block's 20–50 ms frame. 180 is
	// the smallest clean figure ≥ `8,910 ÷ 50`: one engine at the cap, served down to D-09.1's 50 fps floor.
	// Re-priced first, as the ⚠ above requires. A player bet costs 0.0286 ms and a bot bet 0.0062 ms (mini-plan 11
	// B), so a full frame at 180 is `180 × 0.0286 + 4 × 180 × 0.0062` ≈ 9.6 ms of bets.
	// This is NOT the raise §38.7 rule 3 forbids. That rule stops the cap being raised to hand a SATURATED frame
	// more work. Here the frame was not saturated: 56 fps, above the 50 fps floor, with the sim at 49% of it. The
	// below-1 retention was diagnosed first, and what was eating it was this cap, not the frame.
	private const int DefaultMaxBetsPerFrame = 180;

	// Mini-plan 09 P3a — a DEBUG-only runtime override, so the cap can be swept A–B–A inside ONE run. P1 showed
	// the ~2,000 bets/s ceiling in DiceGame IS this cap (bound on 100% of saturated frames) and that each extra
	// bet per frame costs frame rate, not throughput. The only way to measure that trade is to move the cap
	// while nothing else moves, and between runs the spread is 34%. 0 = no override. RELEASE builds cannot set it
	// (the setter is Conditional), so there the property always reads the default.
	private static int _maxBetsPerFrameOverride;
	private static int MaxBetsPerFrame => _maxBetsPerFrameOverride > 0 ? _maxBetsPerFrameOverride : DefaultMaxBetsPerFrame;

	// Mini-plan 08 P1 — BetCostProfiler prints the measured per-bet cost next to the cap that is supposed to be
	// justified by it, so a report can be read without opening this file. Reads the EFFECTIVE cap, override
	// included, so a report taken during a sweep names the cap it actually ran under.
	public static int MaxBetsPerFrameForDiagnostics => MaxBetsPerFrame;

	/// <summary>DEBUG only — sets the per-frame bet cap for a within-run sweep; 0 restores the default.</summary>
	[System.Diagnostics.Conditional("DEBUG")]
	public static void SetMaxBetsPerFrameOverrideForDiagnostics(int cap)
	{
		_maxBetsPerFrameOverride = cap <= 0 ? 0 : Math.Min(cap, 1000);
		GD.Print($"[FrameCap] MaxBetsPerFrame is now {MaxBetsPerFrame} (default {DefaultMaxBetsPerFrame}) — DEBUG override, not persisted.");
	}
	private const double MaxBacklogSeconds = 2.0;

	// Mini-plan 09 D-09.5 — the backlog window must not shrink below a floor of REAL time. MaxBacklogSeconds is
	// in SIMULATED seconds, so its real-time width is MaxBacklogSeconds ÷ DevTimeScale: 2 s at 100X, but 22 ms at
	// 9000X, and ordinary frames cross 22 ms (p95 ~21–22 ms, checkpoint frames 30–43 ms). P3b measured the
	// consequence at 1 credit, where nothing is saturated: retention 0.960 at 9000X, 0.9992 at 6000X (33 ms), and
	// 1.0000 at 3000X (67 ms). The floor is the window 3000X already had.
	//
	// This is NOT the move §38.7 forbids. That rule stops MaxBacklogSeconds being raised to hand a SATURATED
	// frame more work. The time lost here is catch-up after a long frame at a demand the engine meets with ease,
	// and a governed run is by construction not saturated. Below ×30 the floor never binds, so 100X–3000X
	// behave exactly as before.
	private const double MinBacklogWindowRealSeconds = 1.0 / 15.0;

	private double BacklogWindowSimSeconds() =>
		Math.Max(MaxBacklogSeconds, MinBacklogWindowRealSeconds * Math.Max(1, _calendar?.DevTimeScale ?? 1));
	// PUBLIC so the hardware shop's DEV "Set to cap" button targets the clamp HardwareRate applies (mini-plan 11).
	public const int MaxAutoBetBaseAps = 99;

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

	// True while THIS service froze the calendar — for a board vote (Step 14 ND.8b.3, D-ND8.18) or a
	// player pause. Bets are what advance time, but the calendar also ticks on its own while delegated,
	// so a freeze must pin the clock, and IsRunning must be restored only if WE were the ones who
	// stopped it. One flag for both reasons on purpose: the gate in _Process ORs the reasons, so the
	// clock thaws only when EVERY reason has cleared.
	private bool _calendarFrozenBySim;

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

	// The DiceGame PAUSE button (mini-plan 02). A paused run stays RUNNING — it is still the owner of
	// the clock and its session/config are intact — it simply performs no work per frame. Deliberately
	// NOT persisted and reset by StartPlayerAutobet/ClearRunningState: a pause is a live-run state, and
	// an app restart reverts the world to the last mined block anyway.
	//
	// Before this existed, PAUSE was wired only to DiceGame's LOCAL session, which is inert while the
	// autobet is delegated — so the button had been visible and doing nothing since delegation landed
	// (2026-06-22). See ProjectDesignManual §24.13c.
	public bool IsPaused { get; private set; }

	public void SetPaused(bool paused)
	{
		// Only meaningful while a run exists; a stray call cannot leave a stopped service "paused".
		IsPaused = paused && IsRunning;
	}

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

		// Mini-plan 09 §4 — the governor's two event-driven inputs. The third, bot runners starting, stopping or
		// removing themselves, is caught in _Process by comparing the power it already computes every frame.
		if (_calendar != null)
		{
			_calendar.RequestedDevTimeScaleChanged += OnGovernorInputChanged;
		}
		HardwareAllocationRepository.HardwareChanged += OnHardwareChangedForGovernor;
		DevTimeScaleGovernor.BudgetOverrideChanged += OnBudgetOverrideChanged;
		AssertGovernorNeverLimits100X();
		OnGovernorInputChanged();

		_networkRoot = new NetworkRoot();
		AddChild(_networkRoot); // persistent — lives under this autoload

		// One shared dice engine for the player and all bots (stateless per Play call).
		_engine = new DiceEngine();
	}

	// DiceGame calls this when the player starts autobet. The service builds its own session/wallet,
	// seeded from the current bankroll (the single source of truth).
	// ── Mini-plan 09 §4 — DevTimeScale governor ────────────────────────────────────────────────────────────
	private double _lastGovernedCredits = -1d;
	private int _lastGovernedRequest = -1;

	// The player and bot_1..4 — CasinoClientLedgerService's five canonical clients, the only nodes that bet.
	private const int BettableNodeCount = 5;

	private void GovernDevTimeScale(double runningCredits)
	{
		if (_calendar == null)
		{
			return;
		}

		int requested = _calendar.RequestedDevTimeScale;
		if (runningCredits == _lastGovernedCredits && requested == _lastGovernedRequest)
		{
			return;
		}

		_lastGovernedCredits = runningCredits;
		_lastGovernedRequest = requested;
		(int effective, DevTimeScaleLimit limit) = DevTimeScaleGovernor.Govern(requested, runningCredits);
		_calendar.ApplyGovernedDevTimeScale(effective, limit, runningCredits);
	}

	// While nothing runs, the governor PREVIEWS against the player's own credits, so the readout already says what
	// the next autobet will run at instead of changing the moment it starts.
	private void OnGovernorInputChanged() =>
		GovernDevTimeScale(IsRunning ? GetTotalActiveMiningPower() : HardwareRate(PlayerNodeId));

	private void OnHardwareChangedForGovernor(string _) => OnGovernorInputChanged();

	// Mini-plan 10 A3 — the budget moved while credits and request did not, so the (credits, request) cache
	// above would swallow the change. Forget it and re-govern.
	private void OnBudgetOverrideChanged()
	{
		_lastGovernedCredits = -1d;
		OnGovernorInputChanged();
	}

	// §4's promise that 100X is never transformed holds only while the budget covers every bettable node at the
	// credit cap. Printed where the developer reads (the Output panel), not asserted silently.
	[System.Diagnostics.Conditional("DEBUG")]
	private static void AssertGovernorNeverLimits100X()
	{
		double maxCredits = BettableNodeCount * MaxAutoBetBaseAps;
		if (DevTimeScaleGovernor.BetBudgetPerSecond < maxCredits)
		{
			GD.Print(string.Create(System.Globalization.CultureInfo.InvariantCulture,
				$"[DevTimeScale] WARNING — BetBudgetPerSecond {DevTimeScaleGovernor.BetBudgetPerSecond:N0} is below " +
				$"{maxCredits:N0} bets/s ({BettableNodeCount} nodes × {MaxAutoBetBaseAps} credits), so 100X can be slowed."));
		}
	}

	public override void _ExitTree()
	{
		if (_calendar != null)
		{
			_calendar.RequestedDevTimeScaleChanged -= OnGovernorInputChanged;
		}
		HardwareAllocationRepository.HardwareChanged -= OnHardwareChangedForGovernor;
		DevTimeScaleGovernor.BudgetOverrideChanged -= OnBudgetOverrideChanged;
	}

	public void StartPlayerAutobet(PlayerAutobetConfig config)
	{
		IsPaused = false; // a fresh run always starts unpaused
		_config = config;
		_engine ??= new DiceEngine();
		decimal bankroll = _bankroll?.CurrentBalance ?? 0m;
		// Mini-plan 05 D3: a fresh wallet reseeded from the bankroll is a legitimate balance jump away from
		// whatever the previous session's last bet reported. Declare it so the continuity check re-seeds
		// instead of reporting a false break on the run's first bet.
		_userStats?.NoteBalanceDiscontinuity("autobet_session_wallet");
		_wallet = new Wallet(bankroll);
		_betService = new BetService(_engine, _wallet, TransactionSource.Bet,
			() => SettleTimestampUtc());

		// Mini-plan 05 D2: tag the session so the lifecycle trace can name its owner. Note this method
		// OVERWRITES `_session` without stopping the previous one — hypothesis H3. If the old session is
		// still referenced and ticked elsewhere, the trace will show its "start" with no matching "stop".
		var session = new AutoBetSession(_betService, _wallet, new ProgressiveBettingStrategy())
		{
			Owner = UserStatsService.SourceSimulation,
			OwnerNodeId = config.ActiveNodeId ?? ""
		};
		session.Start(config.NumberOfBets, config.Strategy);
		_session = session;

		_accumulatorSeconds = 0d;
		_lastFounderChainLen = -1; // force a founder-power recompute on the first frame of this run
		_lastPopulationChainLen = -1; // and a population-scheduler recompute (Step 14)
		// Mini-plan 08 P3 — a run must not clamp its first frame against an anchor left by a previous run
		// (or by a scene round-trip), which could be hours of game time stale and would collapse the batch
		// onto a single instant. MinValue means "no anchor yet" and yields the nominal spacing.
		_previousFrameClockUtc = DateTime.MinValue;
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
		IsPaused = false; // a pause belongs to a live run; never carry it into the next one
		_session = null;
		_betService = null;
		_wallet = null;
		_config = null;
		_accumulatorSeconds = 0d;
		_previousFrameClockUtc = DateTime.MinValue; // see the note at the field: a stopped run leaves no anchor
		OnGovernorInputChanged(); // no engines run now: fall back to the idle preview of the player's own credits

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

		// TWO independent reasons freeze the simulation, and they compose through one gate so that
		// clearing either one cannot thaw a clock the other is still holding:
		//
		//  • Board vote (Step 14 ND.8b.3, D-ND8.18) — an open vote in a company where the player holds
		//    NST pauses the game until a ballot is registered (CompanyDetails' Board Vote panel).
		//    DiceGame gates manual bets on the same flag.
		//  • Player pause (mini-plan 02, 2026-08-07) — the PAUSE button in DiceGame's strategy panel.
		//
		// Either one skips the whole tick: no bets, no bots, no founder/scheduled mining, calendar
		// pinned. Re-checked EVERY frame rather than only on the edge, because DiceGame re-asserts
		// IsRunning on scene re-entry (BindToRunningBackgroundAutobet) and that must not thaw a frozen
		// clock.
		if (NetworkRoot.IsAwaitingPlayerVote || IsPaused)
		{
			_calendarFrozenBySim = true;
			if (_calendar is { IsRunning: true })
			{
				_calendar.IsRunning = false;
				_calendar.PersistCurrentTime();
			}
			return;
		}
		if (_calendarFrozenBySim)
		{
			_calendarFrozenBySim = false;
			if (_calendar != null)
			{
				_calendar.IsRunning = true;
			}
		}

		// Mini-plan 09 P1 — whole-frame timing (Scripts/Diagnostics/FrameCostProfiler.cs), DEBUG-only and disarmed
		// by default. It starts HERE, after the pause gate, because a frozen frame simulates nothing. The segments
		// below are contiguous: each Enter closes the previous one.
		Scripts.Diagnostics.FrameCostProfiler.BeginFrame();
		Scripts.Diagnostics.FrameCostProfiler.Enter(Scripts.Diagnostics.FrameCostProfiler.Segment.Recompute);

		// Step 7.2: founders mine concurrently with the player (no autonomous clock). Recompute their
		// power only when a new block appeared (cheap-guard around Satoshi's full-chain BTC scan). Step 14
		// adds the population scheduler's two layers (visible cast + invisible mass) the same way, then
		// feeds player+bots+founders+scheduled power to the difficulty regulator so block pacing stays
		// constant while SHARES follow the historical curve (step14 plan §3.0).
		double otherMinersPower = GetTotalActiveMiningPower();
		// Mini-plan 09 §4 — one comparison per frame; the governor runs only when the running credits or the
		// request actually changed. It runs HERE, before simDelta reads DevTimeScale, so a change applies to this
		// frame's bets. The calendar already advanced this frame at the previous scale (autoload #3 runs before
		// #17); the half-open clamp spreads the batch over whatever the clock actually moved.
		GovernDevTimeScale(otherMinersPower);
		RecomputeFoundersOnNewBlock(otherMinersPower);
		RecomputePopulationOnNewBlock(otherMinersPower);
		_networkRoot?.SetActiveMiningPower(otherMinersPower + (_founders?.TotalActiveFounderPower ?? 0d) + NetworkPopulationScheduler.TotalScheduledPower);

		Scripts.Diagnostics.FrameCostProfiler.Enter(Scripts.Diagnostics.FrameCostProfiler.Segment.PlayerLoop);

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
				Scripts.Diagnostics.FrameCostProfiler.AbortFrame(); // the run ended mid-frame; not a simulated frame
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
		_accumulatorSeconds = Math.Min(offeredBacklog, BacklogWindowSimSeconds());
		RecordSimTimeRetention(simDelta, offeredBacklog - _accumulatorSeconds, betsPerSecond);

		// Mini-plan 08 §2 — SPREAD THIS FRAME'S BETS ACROSS THE TIME THEY ACTUALLY OCCUPIED.
		//
		// The clock advances once per frame; every bet settled inside the frame used to read it and so
		// carried the SAME instant. At 100X that is invisible (1.67 game-seconds per frame, rarely more than
		// one bet in it). At 9000X the frame is 150 game-seconds wide and up to MaxBetsPerFrame bets fall
		// into it, so the journal asserted "ten bets at once, then a 150-second void" — measured on a real
		// world at 7,926 bets across 949 distinct timestamps (mini-plan 06 §9.10c).
		//
		// The engine already knows the true spacing: `interval` is in SIMULATED seconds and the calendar
		// advances SpeedMultiplier game-seconds per simulated second, so one bet occupies
		// `interval × SpeedMultiplier` GAME-seconds — correct at every DevTimeScale, because the scale
		// multiplies both sides. So plan the batch, then back-date each bet by its own distance from the
		// end of the frame.
		//
		// The LAST bet keeps the clock's exact value. That is deliberate and load-bearing: CLAUDE.md's
		// canonical rule is that the calendar equals the timestamp of the event that most recently defines
		// the world, and back-dating forward from a frame START would need a frame start this service does
		// not have.
		int planned = PlannedBetsThisFrame(_accumulatorSeconds, interval);
		DateTime clockNowUtc = _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow;
		double stepGameSeconds = ClampedStepGameSeconds(
			interval * (_calendar?.SpeedMultiplier ?? GameSecondsPerRealSecondFallback), planned, clockNowUtc);

		int executed = 0;
		while (_accumulatorSeconds >= interval && executed < MaxBetsPerFrame && _session.IsRunning)
		{
			_accumulatorSeconds -= interval;
			_settleBackdateGameSeconds = Math.Max(0, planned - 1 - executed) * stepGameSeconds;
			ExecutePlayerBetOnce();
			executed++;
		}

		_settleBackdateGameSeconds = 0d;
		Scripts.Diagnostics.FrameCostProfiler.CountPlayerBets(executed, executed >= MaxBetsPerFrame);
		Scripts.Diagnostics.FrameCostProfiler.Enter(Scripts.Diagnostics.FrameCostProfiler.Segment.BotLoop);

		// Bots advance alongside the player autobet, in every scene (Phase 2).
		int botExecuted = TickBots(simDelta);
		Scripts.Diagnostics.FrameCostProfiler.CountBotBets(botExecuted);

		// Step 7.2: drive the founders' concurrent attempts in lockstep with the time the player just
		// advanced (one founder attempt per its power-share of the player+bot attempts this frame).
		Scripts.Diagnostics.FrameCostProfiler.Enter(Scripts.Diagnostics.FrameCostProfiler.Segment.FounderDrive);
		DriveFounderMining(executed + botExecuted, otherMinersPower);

		// Step 14 (ND.2): drive the scheduled network (visible cast + invisible mass) the same way —
		// concurrent miners in lockstep with the player's time advancement, never clock movers.
		Scripts.Diagnostics.FrameCostProfiler.Enter(Scripts.Diagnostics.FrameCostProfiler.Segment.ScheduledDrive);
		DriveScheduledMining(executed + botExecuted, otherMinersPower);
		Scripts.Diagnostics.FrameCostProfiler.Enter(Scripts.Diagnostics.FrameCostProfiler.Segment.Tail);

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

		// Mini-plan 08 P3 — advance the back-dating anchor LAST, after both settle paths have measured
		// against it. Placed here rather than beside the player's loop because the bots settle in the same
		// frame off the same clock; moving it earlier would give them a zero-width window and collapse
		// their spacing to nothing.
		// Mini-plan 10 B1 — R2-C1's overspend, measured instead of reconstructed. The calendar (autoload #3) moved
		// this frame by `delta × rate × LAST frame's retained fraction`; the engines retained `simDelta × THIS
		// frame's fraction`. Summed over a report, the ratio of the two is the overspend mini-plan 08 inferred from
		// journal gaps (0.620% under saturation). Read BEFORE the anchor below moves. -1 marks a frame with no
		// previous anchor (a run's first) or a clock that moved backwards (a freeze onto a block), neither of
		// which is an advance.
		double calendarAdvanceGameSeconds = _previousFrameClockUtc == DateTime.MinValue || clockNowUtc < _previousFrameClockUtc
			? -1d
			: (clockNowUtc - _previousFrameClockUtc).TotalSeconds;
		double retainedGameSeconds = simDelta * retainedFraction * (_calendar?.SpeedMultiplier ?? GameSecondsPerRealSecondFallback);

		_previousFrameClockUtc = clockNowUtc;

		// Mini-plan 11 C1 — the era this frame ran in. Chain height is the tip's index (genesis is 0).
		Scripts.Diagnostics.FrameCostProfiler.NoteEra(clockNowUtc, (_networkRoot?.GetPlayerChainLength() ?? 0) - 1,
			NetworkPopulationScheduler.PoweredCastIds.Count, NetworkPopulationScheduler.TotalScheduledPower);

		// Demand = what the running engines asked for this frame: their credits (bets per simulated second)
		// × DevTimeScale. Mini-plan 08 matched this formula against delivered rates to within 1%.
		Scripts.Diagnostics.FrameCostProfiler.EndFrame(
			otherMinersPower * Math.Max(1, _calendar?.DevTimeScale ?? 1), retainedFraction,
			calendarAdvanceGameSeconds, retainedGameSeconds);
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
			Scripts.Diagnostics.FrameCostProfiler.CountFounderAttempts(attempts);
			for (int i = 0; i < attempts; i++)
			{
				Scripts.Diagnostics.FrameCostProfiler.BeginAttempt();
				_networkRoot.TryMineSingleNonceAttempt(founderId, out Block? block, tsMs);
				if (block != null)
				{
					CaptureCheckpoint();
					StopPlayerOnExternalBlockMined();
					Scripts.Diagnostics.FrameCostProfiler.EndBlockWork();
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
		Scripts.Diagnostics.FrameCostProfiler.CountScheduledCapBound(NetworkPopulationScheduler.LastDrainBudgetBound);
		if (drained.Count == 0)
		{
			return;
		}

		long tsMs = new DateTimeOffset(_calendar?.CurrentUtcDateTime ?? DateTime.UtcNow).ToUnixTimeMilliseconds();
		foreach ((string minerId, int attempts, bool isGhost) in drained)
		{
			Scripts.Diagnostics.FrameCostProfiler.CountScheduledAttempts(attempts);
			if (isGhost)
			{
				_networkRoot.EnsureGhostNodeRegistered(minerId);
			}

			for (int i = 0; i < attempts; i++)
			{
				Scripts.Diagnostics.FrameCostProfiler.BeginAttempt();
				_networkRoot.TryMineSingleNonceAttempt(minerId, out Block? block, tsMs);
				if (block != null)
				{
					if (isGhost)
					{
						NetworkPopulationScheduler.AdvanceGhostRotation();
					}
					CaptureCheckpoint();
					StopPlayerOnExternalBlockMined();
					Scripts.Diagnostics.FrameCostProfiler.EndBlockWork();
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

		DateTime tsUtc = SettleTimestampUtc();

		// Mini-plan 08 P1 — segment timing, DEBUG-only and disarmed by default (Scripts/Diagnostics/
		// BetCostProfiler.cs). Every call below compiles to nothing in RELEASE. The marks sit immediately
		// after the work they name, so a segment's time is its own and the residue lands in "unaccounted"
		// rather than being quietly absorbed by a neighbour.
		Scripts.Diagnostics.BetCostProfiler.BeginBet();

		try
		{
			var (_, betEvent, _) = _session.ExecuteNext(_config.Chance, _config.BetHigh, tsUtc);
			Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.ExecuteNext);
			LastSettledBetEvent = betEvent;
			if (_config.IsPlayerActive)
			{
				_userStats?.OnBetExecutedRegisterBet(_config.GameId, betEvent, UserStatsService.SourceSimulation);
			}
			Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.RegisterBet);
		}
		catch (InvalidOperationException)
		{
			// This bet never completed, so it must not be counted — and leaving the profiler "inside a bet"
			// would attribute the auto-recharge's own BetSettled emit, which follows this stop immediately,
			// to a bet that does not exist.
			Scripts.Diagnostics.BetCostProfiler.AbortBet();
			_session.Stop(IBettingStrategy.StopReason.InsufficientBalance);
			return;
		}

		PersistFinancialState(false);
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.PersistFinancial);

		// Settle THIS bet's balances BEFORE mining/checkpoint (OQ-CG.10): if this same bet mines a block, the
		// checkpoint it captures must reflect the bet's own result, consistently with the bet-history boundary
		// (HistoryCheckpointUtcTicks) which already includes this bet. Keep the bankroll autoload (the source of
		// truth) in sync so every scene reflects it live, and route the inverse of the client's profit to/from
		// the casino SC bankroll — since ND.8f (OQ-11.1 resolved) EVERY client's bet routes there, so a
		// delegated autobet running on a bot node is simply that bot's play (correct semantics, no longer an
		// inconsistency with the comment); its settled bets also accrue in the casino's per-client book.
		_bankroll?.SetBalance(_wallet.Balance);
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.BankrollSetBalance);

		_casinoSc?.ApplyBetResult(-(LastSettledBetEvent?.CreditedProfit ?? 0m));
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.CasinoApplyBetResult);

		if (!_config.IsPlayerActive && LastSettledBetEvent != null)
		{
			_clientLedger ??= GetNodeOrNull<CasinoClientLedgerService>("/root/CasinoClientLedgerService");
			_clientLedger?.RegisterSettledBet(_config.ActiveNodeId, LastSettledBetEvent.BetAmount,
				LastSettledBetEvent.CreditedProfit, LastSettledBetEvent.IsWin);
		}
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.ClientLedger);

		// One nonce attempt per bet (1 bet = 1 attempt), routed by the active node's hardware allocation
		// (individual pool → own chain; casino pool → casino chain). Real PoW on the shared chain.
		long tsMs = new DateTimeOffset(tsUtc).ToUnixTimeMilliseconds();
		Scripts.Diagnostics.FrameCostProfiler.BeginAttempt();
		Block? block = RouteNonceAttempt(_config.ActiveNodeId, tsMs);
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.NonceAttempt);

		if (block != null)
		{
			// This bet was stamped by SettleTimestampUtc(), so the block and the checkpoint share its instant.
			CaptureCheckpoint(_settleBackdateGameSeconds);
			if (_config.StopOnBlockMined && _session.IsRunning)
			{
				_session.Stop(IBettingStrategy.StopReason.StopOnBlockMined);
				FreezeCalendarAtBlockStop();
			}
			Scripts.Diagnostics.FrameCostProfiler.EndBlockWork();
		}
		// The block path gets its OWN segment, separate from the attempt above. Both readings are honest and
		// they answer different questions: amortised over thousands of bets this is a few µs (the cost every
		// bet shares), while the same work inside ONE bet was measured at 96.7 ms — 5.8 frames — which is
		// what the brief Sim% dips at high throughput actually are. A mean cannot show a spike, and a spike
		// and a saturation have opposite fixes.
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.BlockCommit);

		if (LastSettledBetEvent != null)
			ClientBetSettled?.Invoke(_config.ActiveNodeId, _config.GameId, LastSettledBetEvent);
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.ClientBetSettledEvent);

		EmitSignal(SignalName.BetSettled);
		Scripts.Diagnostics.BetCostProfiler.Mark(Scripts.Diagnostics.BetCostProfiler.Segment.BetSettledSignal);
		Scripts.Diagnostics.BetCostProfiler.EndBet();
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
		if (_session.LastStopReason != IBettingStrategy.StopReason.InsufficientBalance) return false;
		if (_bankrollProgram == null || _principal == null) return false;
		// SF.1.2 (D-SF.4): the service-level off-switch, and since mini-plan 02 the ONLY gate. When OFF, no
		// auto top-up — the session stops and waits for a manual Bankroll recharge.
		//
		// The captured `_config.AutoRecharge` used to be ANDed in front of this, which made the toggle
		// half-live: turning it OFF mid-run worked (this check), turning it back ON did not (the captured
		// false still blocked). §25.8 makes BankrollProgramService.AutoRechargeEnabled the single source of
		// truth and CLAUDE.md already documented "the service flag wins" — the captured copy contradicted
		// both. It is the one panel control deliberately left ENABLED during a run, so it has to actually
		// work in both directions. (`_config.AutoRecharge` is still set by DiceGame and still describes how
		// the run was started; it is simply no longer a gate. Bots are unaffected — TryRechargeAndRestartBot
		// keeps its own per-node cfg.AutoRechargeEnabled.)
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
		var session = new AutoBetSession(_betService, _wallet, new ProgressiveBettingStrategy())
		{
			Owner = UserStatsService.SourceSimulation,
			OwnerNodeId = _config.ActiveNodeId ?? ""
		};
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

		// Mini-plan 05 D3: a WITHDRAWAL is a legitimate discontinuity too, and it has no RegisterDeposit to
		// declare it on its behalf — the money leaves the session wallet without ever being a deposit. This
		// is the shape the check is designed to surface: an exemption that must be stated because nothing
		// else in the flow implies it.
		_userStats?.NoteBalanceDiscontinuity("manual_return");
		_bankroll?.SetBalance(_wallet.Balance);
		PersistFinancialState(false);
		EmitSignal(SignalName.BetSettled); // refresh live UI: bankroll dropped
		return true;
	}

	// Mini-plan 08 D4 — the instant the latest checkpoint was captured at, in the calendar's LOCAL time, kept so
	// FreezeCalendarAtBlockStop can stop the clock ON that instant rather than wherever the frame clock stands.
	// Cleared at the start of every capture, so one that bails out early can never hand the freeze a stale
	// instant belonging to an earlier block.
	private DateTime? _checkpointInstantLocal;

	/// <param name="settlingBackdateGameSeconds">
	/// The back-date of the bet that MINED the block, passed only when the capture runs inside the player's own
	/// bet. Every other caller passes nothing and captures at the frame clock — see the D4 note below for why that
	/// asymmetry is correct and not an oversight.
	/// </param>
	private void CaptureCheckpoint(double settlingBackdateGameSeconds = 0d)
	{
		_checkpointInstantLocal = null;
		Scripts.Diagnostics.FrameCostProfiler.CountCheckpoint();
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

		// Mini-plan 08 D4 — THE CHECKPOINT BOUNDARY IS THE INSTANT OF THE LAST BET WHOSE BALANCES IT HOLDS.
		//
		// Back-dating (§2) put the player's mining bet up to a frame's width BEHIND the clock, and this read the
		// clock. The player's loop then kept settling bets stamped between the two — after the capture, so not
		// in its balances, but at or before its boundary, so RollbackToUtc KEPT them on restart. Measured in the
		// stats test (plan §D4): journal bets surviving a restart that no restored balance had ever paid for.
		//
		// The asymmetry is the frame's ORDER, not an oversight. The player's loop runs to completion before
		// TickBots, and the founders and the scheduled network mine after both. So when anything OTHER than the
		// player's own bet mines, every player bet of the frame — stamped up to the clock — is already inside
		// the balances captured here, and the clock is the correct boundary. Only a capture from inside the
		// player's loop has bets still to come in the same frame, and only it passes its back-date.
		DateTime historyUtc = (_calendar?.CurrentUtcDateTime ?? DateTime.UtcNow).AddSeconds(-settlingBackdateGameSeconds);
		DateTime calendarLocal = (_calendar?.CurrentLocalDateTime ?? DateTime.Now).AddSeconds(-settlingBackdateGameSeconds);
		_checkpoint.CaptureCheckpoint(_principal, _bankroll, _bankrollProgram, historyUtc, calendarLocal);
		_checkpointInstantLocal = calendarLocal;

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
			() => SettleTimestampUtc());
		var session = new AutoBetSession(betService, wallet, new ProgressiveBettingStrategy())
		{
			Owner = "SimulationService.bot",
			OwnerNodeId = cfg.NodeId
		};
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
			runner.AccumulatorSeconds = Math.Min(botBacklog, BacklogWindowSimSeconds());
			RecordSimTimeRetention(botOffered, botBacklog - runner.AccumulatorSeconds, betsPerSecond);

			// Mini-plan 08 §2, applied to the bots for the same reason and by the same arithmetic. Their
			// records feed CasinoClientLedgerService and BotPlayHistory rather than the player's journal,
			// but a per-frame timestamp collapse distorts those readings identically — and leaving one of
			// two identical loops unfixed is how the next investigation gets a mixed dataset.
			int botPlanned = PlannedBetsThisFrame(runner.AccumulatorSeconds, interval);
			double botStepGameSeconds = ClampedStepGameSeconds(
				interval * (_calendar?.SpeedMultiplier ?? GameSecondsPerRealSecondFallback),
				botPlanned,
				_calendar?.CurrentUtcDateTime ?? DateTime.UtcNow);

			int executed = 0;
			while (runner.AccumulatorSeconds >= interval && executed < MaxBetsPerFrame && runner.Session.IsRunning)
			{
				runner.AccumulatorSeconds -= interval;
				_settleBackdateGameSeconds = Math.Max(0, botPlanned - 1 - executed) * botStepGameSeconds;
				ExecuteBotBet(runner);
				executed++;
			}

			_settleBackdateGameSeconds = 0d;
			totalExecuted += executed;
			// Mini-plan 11 A1 — each runner has its own cap, and H2 sees only the player's.
			Scripts.Diagnostics.FrameCostProfiler.CountBotEngine(executed >= MaxBetsPerFrame);
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
			DateTime tsUtc = SettleTimestampUtc();
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
			Scripts.Diagnostics.FrameCostProfiler.BeginAttempt();
			Block? block = RouteNonceAttempt(runner.NodeId, tsMs);
			if (block != null)
			{
				CaptureCheckpoint();
				StopPlayerOnExternalBlockMined();
				Scripts.Diagnostics.FrameCostProfiler.EndBlockWork();
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
	// block, and that drifted value later gets persisted to calendar_state.json (OQ-CG.9). The drift was
	// normally sub-second, but the casino's on-demand loan (CG.1.8) added real-time latency to the block frame,
	// inflating the next frame's delta and making the overshoot large enough to notice.
	//
	// This used to freeze the clock IN PLACE, on the reasoning that the clock still equalled what the capture
	// had just read. Mini-plan 08 D4 made that false for the player's own block: the capture now reads the
	// mining bet's back-dated instant, up to a frame's width behind the clock. So the freeze sets the clock ON
	// the captured instant — a no-op for every external block, whose capture read the clock itself. The
	// half-open clamp already treats a clock that moved backwards as a re-seed, not a throttle.
	private void FreezeCalendarAtBlockStop()
	{
		if (_calendar == null) return;
		_calendar.IsRunning = false;
		if (_checkpointInstantLocal is DateTime checkpointLocal)
		{
			_calendar.SetLocalDateTime(checkpointLocal);
		}
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
			() => SettleTimestampUtc());
		var session = new AutoBetSession(betService, runner.Wallet, new ProgressiveBettingStrategy())
		{
			Owner = "SimulationService.bot",
			OwnerNodeId = runner.NodeId ?? ""
		};
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
