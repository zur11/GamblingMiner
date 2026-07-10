using System;
#nullable enable

// Step 13 (TL.0) — the single flag + offset every historical date anchor shifts by.
// DevAltTimeline is false on main FOREVER; only a swaps feature branch flips it to true so the
// first-launch bootstrap lands the player on 2010-07-18 (Mt. Gox launch, the market dataset's first
// date) instead of the canonical ~16-in-game-month wait for a BTC/SC exchange to exist. With Offset
// == TimeSpan.Zero this file is behavior-identical to hardcoding the canonical dates directly.
// See AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md §3.
//
// Step 13 (TL.2/TL.3) — BRANCH-ONLY FLIP. true here means the NEXT app launch wipes the current
// world (ResetWorldIfIncompatible, D-13.7) and regenerates a fresh alt-timeline bootstrap landing on
// 2010-07-18. It must be false again before merging back to main (TL.3, executed 2026-07-07) — see
// the warning box at plan §0. A permanent visible watermark (StatusBar) is required for as long as
// this is true. Re-mount instructions / designing new alt bootstraps: ProjectDesignManual Ch. 35.
public static class TimelineConfig
{
	public const bool DevAltTimeline = false;

	public static readonly TimeSpan Offset = DevAltTimeline ? TimeSpan.FromDays(484) : TimeSpan.Zero;

	// EB.1 (D-EB.11) — the world-compatibility stamp gains an "+ENTRY-{year}" suffix whenever
	// DevEntryYear is set. Any change to the suffix (switching entry years, or turning it on/off) makes
	// this Tag differ from what's on disk, so NetworkRoot.ResetWorldIfIncompatible's EXISTING guard
	// auto-wipes and rebuilds — zero new machinery, the identical mechanism TL.1 already built for
	// DevAltTimeline switches.
	public static readonly string Tag = (DevAltTimeline ? "ALT-2010-07-18" : "CANON-2009-01-03")
		+ (DevEntryYear == 0 ? string.Empty : $"+ENTRY-{DevEntryYear}");

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

	// Step 14 (EB.1, §5.1) — DEV entry-year bootstrap generator. UNLIKE DevAltTimeline (which MOVES
	// genesis and the whole founder crew forward), this leaves genesis and the founders at their true
	// canonical dates and instead FAST-BUILDS real intervening history (founder arcs, cast spawns,
	// halvings) up to a landing on 21 Mar of the chosen year — a canon-compatible world, never a second
	// timeline. `0` on `main` FOREVER (canonical 2009-03-21 start, HistoricalBootstrapService behavior
	// unchanged bit-for-bit); a DEV branch may set 2010-2025 to land directly in that year for testing.
	// Orthogonal to DevAltTimeline — never set both to a non-default value at once.
	public const int DevEntryYear = 2010; // ND.4 calibration playtest — restore to 0 before merging to main

	public static readonly DateTime EntryDayLocal = DevEntryYear == 0
		? PlayerStartDayLocal
		: new DateTime(DevEntryYear, 3, 21, 0, 0, 0, DateTimeKind.Local);
}
