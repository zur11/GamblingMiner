using Godot;
#nullable enable

// Background simulation — Phase 1a (autoload skeleton, no behavior change yet).
//
// This autoload will OWN the running autobet so it keeps simulating across scene changes
// (the bug: today the loop lives in DiceGame._Process and dies when the scene is freed).
// In 1a it is inert — DiceGame still drives its own loop, so the game is unchanged.
// Phase 1b moves the player autobet loop + per-bet execution in here; Phase 2 moves the bots.
public partial class SimulationService : Node
{
	// Snapshot of what DiceGame needs to run the player's autobet headlessly (filled out in 1b
	// with the BettingStrategyConfig + wallet/engine wiring).
	public sealed class PlayerAutobetConfig
	{
		public int Chance;
		public bool BetHigh;
		public double BetsPerSecond;
		public int NumberOfBets;          // 0 = infinite
		public string ActiveNodeId = "player";
		public bool StopOnBlockMined;
	}

	public bool IsRunning { get; private set; }
	public PlayerAutobetConfig? CurrentConfig { get; private set; }

	// Raised after each settled bet (wired in 1b). A Godot signal, so DiceGame and other scenes
	// can subscribe to refresh their UI from live simulation state.
	[Signal] public delegate void BetSettledEventHandler();

	public override void _Ready()
	{
		// Runtime dependencies (CalendarTimeService, balance services, NetworkRoot static mining)
		// are wired in 1b when the loop actually moves here.
		GD.Print("[SimulationService] Ready (Phase 1a skeleton — loop not active yet).");
	}

	// DiceGame will call this instead of running its own loop (from 1c onward).
	public void StartPlayerAutobet(PlayerAutobetConfig config)
	{
		CurrentConfig = config;
		IsRunning = true;
		GD.Print($"[SimulationService] StartPlayerAutobet (skeleton; node={config.ActiveNodeId}, aps={config.BetsPerSecond:0.##}).");
	}

	public void Stop()
	{
		IsRunning = false;
		CurrentConfig = null;
	}

	public override void _Process(double delta)
	{
		if (!IsRunning)
		{
			return;
		}

		// Phase 1b: pace the player autobet + execute bets (mining, clock, stats, balance) here,
		// then Phase 2 ticks the bot runners — all running regardless of the active scene.
	}
}
