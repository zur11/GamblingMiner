using Godot;
using System.Collections.Generic;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
#nullable enable

// Step 16 P16.3b (D-16.7) — the 40 non-miner COMPANIES' BTC wallets, split out of BotsBtcWallets.
//
// They had been listed beside the four casino-miner bots since Step 14 introduced them, on the strength of
// a shared registry file — but the two populations share nothing else: the bots bet, bid and mine for the
// casino, while these are the auction/governance economy (treasuries, dividends, collateral, seizures).
// Reading either list meant scrolling past the other.
//
// Everything below the list is inherited unchanged: the detail panel already branched on IsMinerNode, so
// the wallet/transaction/dev-control UI is the same code the other two screens run.
public partial class CompaniesWallets : BotsBtcWallets
{
	// Companies are HOLDER wallets: they carry the IsActive lifecycle (and therefore the inactive filter),
	// and they never mine. The miner section is left empty, and the base hides its header.
	protected override IReadOnlyList<BotWalletRecord> HolderPopulation => BotWalletRegistry.NonMinerBots;
	protected override IReadOnlyList<BotWalletRecord> MinerPopulation => [];
	protected override string HolderSectionTitle => "── Companies ──";
	protected override string NoSelectionPrompt => "Select a company from the list.";
}
