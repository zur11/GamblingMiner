using Godot;
using GodotBlockchainPort.Simulation;
#nullable enable

// Step 13 (TL.3 ordering fix, 2026-07-07) — MUST be the FIRST autoload in project.godot.
//
// Runs the world-compatibility guard (NetworkRoot.RunWorldCompatibilityGuard → ResetWorldIfIncompatible:
// format-version OR timeline-tag mismatch ⇒ full clean reset) before ANY other autoload's _Ready can load
// a user:// state file into a static cache. Without this, a stale file loaded before the wipe survives in
// memory and re-persists later — exactly how alt-timeline hardware credits and casino-pool shares leaked
// into the fresh canon world on the first TL.3 relaunch (CalendarTimeService, autoload #2, loads
// hardware/pool state via WalletInitializationService.EnsureAll long before NetworkRoot's own
// initialization runs the guard). See ProjectDesignManual Ch. 35 (§35.1) and the maintenance rule above
// NetworkRoot.ResetWorldIfIncompatible.
public partial class WorldGuardService : Node
{
	public override void _Ready()
	{
		NetworkRoot.RunWorldCompatibilityGuard();
	}
}
