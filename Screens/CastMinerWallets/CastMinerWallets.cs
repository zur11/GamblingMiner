using Godot;
using System.Collections.Generic;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
#nullable enable

// Step 16 P16.3c (D-16.7) — the Step-14 historical CAST miners' BTC wallets (artforz, foundry_usa, …).
//
// They had no screen at all: BotWalletRegistry.CastMiners is a deliberately separate third list (ND.2 —
// they mine founder-style via drained attempts, join no betting runner and hold no SC), so nothing ever
// listed them, and after P16.2 they carry real seeds and rotating change like every other participant.
// This is where that becomes visible.
//
// The list grows as the historical curve spawns them (spawn-drip, at most one per block), so an early-game
// world shows a short list and a 2020s world shows ~30 — which is itself the readout.
public partial class CastMinerWallets : BotsBtcWallets
{
	// Cast miners mine but never hold: no IsActive lifecycle, so no holder section and no inactive filter.
	protected override IReadOnlyList<BotWalletRecord> MinerPopulation => BotWalletRegistry.CastMiners;
	protected override IReadOnlyList<BotWalletRecord> HolderPopulation => [];
	protected override string MinerSectionTitle => "── Historical Cast Miners ──";
	protected override string NoSelectionPrompt =>
		BotWalletRegistry.CastMiners.Count == 0
			? "No cast miners have spawned yet — they arrive as the historical hashrate curve grows."
			: "Select a cast miner from the list.";
}
