using System;
#nullable enable

// Step 13 (TL.0) — the single flag + offset every historical date anchor shifts by.
// DevAltTimeline is false on main FOREVER; only a swaps feature branch flips it to true so the
// first-launch bootstrap lands the player on 2010-07-18 (Mt. Gox launch, the market dataset's first
// date) instead of the canonical ~16-in-game-month wait for a BTC/SC exchange to exist. With Offset
// == TimeSpan.Zero this file is behavior-identical to hardcoding the canonical dates directly.
// See AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md §3.
public static class TimelineConfig
{
	public const bool DevAltTimeline = false;

	public static readonly TimeSpan Offset = DevAltTimeline ? TimeSpan.FromDays(484) : TimeSpan.Zero;
	public static readonly string Tag = DevAltTimeline ? "ALT-2010-07-18" : "CANON-2009-01-03";

	public static DateTime Shift(DateTime canonicalLocal) => canonicalLocal + Offset;
	public static DateTimeOffset Shift(DateTimeOffset canonicalUtc) => canonicalUtc + Offset;

	// The canonical player-start day (21 Mar 2009), shifted. Two independent consumers share this exact
	// calendar anchor — HistoricalBootstrapService.PlayerStartDayLocal and FoundersMiningService.HalDecayStart
	// — so it is defined once here rather than risking the two drifting apart across files.
	public static readonly DateTime PlayerStartDayLocal =
		Shift(new DateTime(2009, 3, 21, 0, 0, 0, DateTimeKind.Local));

	// D-13.9 — the plan's one deliberate functional divergence: alt-timeline network fees activate on the
	// landing/market-open day itself, NOT a uniform +484 shift of 2009-04-26 (which would land 2010-08-23).
	// Canon (DevAltTimeline == false) is untouched — this reads exactly 2009-04-26 as before.
	public static readonly DateTime FeeActivationLocal =
		DevAltTimeline ? PlayerStartDayLocal : new DateTime(2009, 4, 26, 0, 0, 0, DateTimeKind.Local);
}
