using System;

namespace Scripts.History
{
	public sealed class BetRecord
	{
		// Mini-plan 13 B (D-13.2) — a per-world SEQUENCE, assigned by BetHistoryRepository when the record is
		// added, not a Guid minted here. `0` means "not yet assigned". Its only reader is the journal's INC-002
		// duplicate guard; the Guid cost a string allocation, a 32-character hash on every insert and trim, and
		// ~30 of a journal line's bytes, to express a uniqueness a counter simply has.
		public long Id { get; set; }
		public string GameId { get; set; } = string.Empty;
		public DateTime TimestampUtc { get; set; }
		public BetOutcome Outcome { get; set; }
		public decimal BetAmount { get; set; }
		public decimal NetAmount { get; set; }
		public decimal BalanceAfter { get; set; }
		public int Roll { get; set; }
		public int Chance { get; set; }
		public decimal Multiplier { get; set; }
		public bool IsHigh { get; set; }
	}
}
