using Godot;
using System;

public partial class CalendarTimeService : Node
{
	private static readonly DateTime GameStartLocal = TimelineConfig.Shift(new DateTime(2009, 1, 3, 18, 15, 6, DateTimeKind.Local));
	private static readonly DateTime LegacyStartLocal = new DateTime(2009, 10, 3, 0, 0, 0, DateTimeKind.Local);

	public DateTime CurrentLocalDateTime { get; private set; } = DateTime.Now;
	public DateTime ExplorerSelectedLocalDateTime { get; private set; } = DateTime.Now;
	public bool IsRunning { get; set; } = false;
	public bool IsAutobetActive { get; set; } = false;
	public double SpeedMultiplier { get; set; } = 1.0;

	// DEV/TEST ONLY — orthogonal time-acceleration multiplier on top of SpeedMultiplier (1 = 100X base; the
	// ladder of offered steps is DevTimeScaleSelector.Multipliers, never restated here — its top step is
	// asserted against MaxGameSecondsPerRealSecond below). It scales BOTH the calendar clock (here) and the bet-execution rate
	// (SimulationService._Process), keeping attempts-per-IN-GAME-second — and therefore the difficulty /
	// power / solvetime dynamics — mathematically invariant. Only wall-clock time compresses. NOT persisted;
	// resets to 1 on restart. Set via the DEV time-scale selector in DiceGame / BlockExplorer.
	public int DevTimeScale { get; set; } = 1;

	// R2-C1 (2026-07-27, btc-pools-hardware-plan.md §R2.3a/§R2.7) — the fraction of last frame's simulated
	// time the bet engine actually retained. `1.0` = it kept up, and the clock advances exactly as it always
	// has; below 1 the engine's backlog clamp discarded simulated time, and the calendar must NOT spend what
	// was never simulated.
	//
	// Why this exists: the comment above claims attempts-per-IN-GAME-second is invariant under DevTimeScale.
	// That was only true while the engine kept up. Past its saturation knee (≈45 fps at 90×) the bet loop
	// silently dropped work while this clock kept its full stride, so in-game block intervals stretched by
	// exactly the dropped fraction — measured at up to 6× during a founder power spike. Throttling here
	// converts that into an honest wall-clock slowdown instead of a corrupted simulation.
	//
	// Written by SimulationService each frame while it drives the sim, and reset to 1.0 when it stops (the
	// calendar also runs outside the delegated autobet, where nothing is being dropped).
	public double SimulationThrottle { get; set; } = 1.0;

	private DateTime _gamePresent = DateTime.Now;
	public DateTime GamePresentLocalDateTime => _gamePresent;

	public DateTime CurrentUtcDateTime => CurrentLocalDateTime.ToUniversalTime();

	public override void _Ready()
	{
		WordlistBootstrapper.EnsureWordlist();
		WalletInitializationService.EnsureAll();

		// Step 3a: on a brand-new game, Satoshi + Hal pre-mine the chain to 21 Mar 2009 (first launch
		// only). When that runs, the player's epoch becomes the random landing time on 21 Mar rather
		// than the genesis instant — overriding any stale calendar_state.json from a partial reset.
		HistoricalBootstrapService.RunIfFirstLaunch();
		if (HistoricalBootstrapService.DidRun && HistoricalBootstrapService.LandingLocalDateTime is DateTime landing)
		{
			SetLocalDateTime(landing);
			SetExplorerSelectedLocalDateTime(landing);
			_gamePresent = CurrentLocalDateTime;
			PersistCurrentTime();
		}
		else
		{
			EnsureGameEpochInitialized();
		}
	}

	// THE ABSOLUTE CEILING on how fast game time may run, in game-seconds per real second. 9000X is not a
	// preference: past it the frame budget collapses and the game stops being playable — which is why
	// DevTimeScaleSelector's ladder stops at ×90 on the 100X base.
	//
	// That ladder is a UI, and a UI cannot be an invariant. The rate is a PRODUCT of two independently-set
	// factors — SpeedMultiplier, which CalendarsNavigator offers up to 1000, and DevTimeScale, up to 90 —
	// so the arithmetic ceiling of the controls that exist today is 90000X, ten times the playable limit.
	// Nothing computed it, and nothing forbade it; it was held down only by a coincidence of three separate
	// facts in three files (see §9.6 of AIHelperFiles/mini06-clock-rewind-reproduction-plan.md, which
	// enumerates them and shows one of them is exactly what that plan's harness sets out to break).
	//
	// Clamping HERE, at the single line that spends the rate, makes the limit hold for every writer —
	// including writers not yet written, which is the only kind a guard can actually protect against.
	public const double MaxGameSecondsPerRealSecond = 9000.0;

	private bool _rateCeilingWarned;

	public override void _Process(double delta)
	{
		if (!IsRunning)
		{
			return;
		}

		double requestedRate = SpeedMultiplier * Math.Max(1, DevTimeScale);
		WarnOnceIfRateExceedsCeiling(requestedRate);

		CurrentLocalDateTime = CurrentLocalDateTime.AddSeconds(
			delta * Math.Min(requestedRate, MaxGameSecondsPerRealSecond)
				  * Math.Clamp(SimulationThrottle, 0d, 1d));
	}

	// A clamp that silently rescues its caller teaches nobody anything, and the caller who needed rescuing
	// is precisely the thing worth knowing. DEBUG-only, once per process, naming BOTH factors so the setter
	// at fault is identifiable rather than merely implied.
	[System.Diagnostics.Conditional("DEBUG")]
	private void WarnOnceIfRateExceedsCeiling(double requestedRate)
	{
		if (_rateCeilingWarned || requestedRate <= MaxGameSecondsPerRealSecond)
		{
			return;
		}

		_rateCeilingWarned = true;
		GD.PrintErr(string.Format(
			System.Globalization.CultureInfo.InvariantCulture,
			"[Clock] Requested game-time rate {0:N0}X exceeds the {1:N0}X ceiling and was clamped " +
			"(SpeedMultiplier={2:N0} × DevTimeScale={3}). Past the ceiling the frame budget collapses and " +
			"the game is unplayable — find the setter that asked for it. Do not raise the ceiling.",
			requestedRate, MaxGameSecondsPerRealSecond, SpeedMultiplier, DevTimeScale));
	}

	public void SetLocalDateTime(DateTime localDateTime)
	{
		CurrentLocalDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local);
	}

	public void SetExplorerSelectedLocalDateTime(DateTime localDateTime)
	{
		ExplorerSelectedLocalDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local);
	}

	public void SetNow()
	{
		SetLocalDateTime(_gamePresent);
		SetExplorerSelectedLocalDateTime(_gamePresent);
	}

	public void EnsureGameEpochInitialized()
	{
		const string statePath = "user://calendar_state.json";
		if (!FileAccess.FileExists(statePath))
		{
			SetLocalDateTime(GameStartLocal);
			SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
			_gamePresent = CurrentLocalDateTime;
			PersistCurrentTime();
			return;
		}

		using FileAccess file = FileAccess.Open(statePath, FileAccess.ModeFlags.Read);
		string value = file.GetAsText();
		if (!long.TryParse(value, out long ticks))
		{
			SetLocalDateTime(GameStartLocal);
			_gamePresent = CurrentLocalDateTime;
			PersistCurrentTime();
			return;
		}

		DateTime loaded = new DateTime(ticks, DateTimeKind.Local);
		// Migrate legacy bootstrap values to the updated genesis-adjacent start.
		if (loaded == LegacyStartLocal || loaded == new DateTime(2009, 1, 3, 12, 0, 0, DateTimeKind.Local))
		{
			loaded = GameStartLocal;
			SetLocalDateTime(loaded);
			SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
			_gamePresent = CurrentLocalDateTime;
			PersistCurrentTime();
			return;
		}

		SetLocalDateTime(loaded);
		SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
		_gamePresent = CurrentLocalDateTime;
	}

	public void AdvanceSeconds(double seconds)
	{
		if (seconds <= 0d)
		{
			return;
		}

		CurrentLocalDateTime = CurrentLocalDateTime.AddSeconds(seconds);
	}

	public void PersistCurrentTime()
	{
		_gamePresent = CurrentLocalDateTime;
		const string statePath = "user://calendar_state.json";
		using FileAccess file = FileAccess.Open(statePath, FileAccess.ModeFlags.Write);
		file.StoreString(CurrentLocalDateTime.Ticks.ToString());
	}
}
