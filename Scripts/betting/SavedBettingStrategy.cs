using System;

namespace Scripts.Betting
{
	public sealed class SavedBettingStrategy
	{
		public string Name { get; set; } = string.Empty;
		public string GameId { get; set; } = string.Empty;
		public DateTime SavedAtUtc { get; set; }
		public BettingStrategyConfig Config { get; set; } = new();
		public int NumberOfBets { get; set; }
		// NOTE: auto-recharge is deliberately NOT part of a saved strategy (mini-plan 02, 2026-08-07).
		// It is an ACCOUNT-level setting owned by BankrollProgramService (§25.8), not a property of a
		// betting strategy: it decides what happens to the player's money when a run runs out, which is
		// the same answer whichever strategy is loaded. It changes only through direct player action —
		// the Bankroll Programmer (always) or the DiceGame panel toggle (while no session is running).
		// Everything else here IS saved, StopOnBlockMined included; a bot's mandatory overrides are
		// applied at BuildBotStrategyConfig, so loading a shared strategy onto a bot stays safe.
		// Old files carrying the removed property simply ignore it on load (personal file, no migration).
		public int WinningChance { get; set; }
		public bool BetHigh { get; set; }
		public int BetsPerSecond { get; set; } = 1;
	}
}
