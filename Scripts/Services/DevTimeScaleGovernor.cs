using System;

/// <summary>Why the effective DevTimeScale sits below the one the developer requested, if it does.</summary>
public enum DevTimeScaleLimit
{
	/// <summary>Running at the requested scale.</summary>
	None = 0,
	/// <summary>The running engines' hardware credits × the scale would exceed <see cref="DevTimeScaleGovernor.BetBudgetPerSecond"/>.</summary>
	Credits,
	/// <summary>The scale would exceed <see cref="CalendarTimeService.MaxGameSecondsPerRealSecond"/>.</summary>
	Ceiling,
}

/// <summary>
/// Mini-plan 09 §4 — the DevTimeScale governor. The developer REQUESTS a scale; the game runs at the highest
/// scale the running hardware credits allow, so buying credits mid-autobet lowers it step by step and
/// discarding them raises it back towards the request.
///
/// <para><b>Pure and static on purpose.</b> This is the formula and nothing else, so it can be checked in a
/// throwaway console project exactly as the game computes it. Who calls it, and when, lives in
/// <c>SimulationService</c>, the only place that knows which bet engines are running.</para>
/// </summary>
public static class DevTimeScaleGovernor
{
	/// <summary>
	/// D-09.4 — the bets per real second the governor lets the running engines demand.
	///
	/// D-09.6 (2026-09-18): 2,000 → 1,700, sized for the SLOWEST session measured, not the fastest.
	///
	/// The first value came from mini-plan 09 P3a: in DiceGame, the most expensive scene to bet in, a saturated
	/// frame at the default <c>MaxBetsPerFrame</c> delivered ~2,070–2,120 bets/s at ~52 fps, and 2,000 sat 5%
	/// below it. §6's verification ran the whole frame ~17% slower, evenly across sim and outside work, which is
	/// the between-session spread mini-plan 08 P1g already measured (up to 34%). At 2,000 that session held
	/// ~46 fps and retention ~0.93, so the readout said 2000X while game time ran at ~1,870X. The 5% margin had
	/// been set against within-run noise, which was the wrong reference.
	///
	/// 1,700 holds D-09.1's 50 fps minimum, at full retention, in the slower session as well (P3a's fitted
	/// frame scaled by that session's factor gives ~34 bets per frame at 20 ms). 99 credits now govern to 1700X.
	/// The price is ~15% of speed on a fast session, accepted because what the readout shows must be what runs.
	/// A budget that follows measured delivery instead is recorded as a candidate for after this plan
	/// (mini-plan 09, D-09.6 option (c)).
	///
	/// Lighter scenes (the hardware shop measured at 0.04 ms of sim per bet against DiceGame's ~0.16–0.18)
	/// could run faster. One conservative budget everywhere is a deliberate simplification. <b>Measured on one
	/// machine, in 2009:</b> a later era may cost more per bet (mini-plan 09 §5).
	/// </summary>
	public const double BetBudgetPerSecond = 1700.0;

	/// <summary>Game-seconds per real second at DevTimeScale 1 — the 100X base every DEV scale multiplies.</summary>
	public const double DevBaseGameSecondsPerRealSecond = 100.0;

	/// <param name="requested">The scale the developer asked for (a DevTimeScale multiplier, ≥ 1).</param>
	/// <param name="runningCredits">
	/// Σ hardware credits of every bet engine that runs in the frame: the player's active node plus every running
	/// bot runner. Each credit is one bet per simulated second, so demand is this × the scale. Founder and
	/// scheduled-network attempts are not a term: they are drained in proportion to these attempts and cost
	/// under 0.05 ms per frame in 2009 (P1, H3).
	/// </param>
	public static (int Effective, DevTimeScaleLimit Limit) Govern(int requested, double runningCredits)
	{
		int wanted = Math.Max(1, requested);
		int ceiling = Math.Max(1, (int)Math.Floor(CalendarTimeService.MaxGameSecondsPerRealSecond / DevBaseGameSecondsPerRealSecond));
		int byCredits = runningCredits > 0d
			? Math.Max(1, (int)Math.Floor(BetBudgetPerSecond / runningCredits))
			: int.MaxValue;

		int effective = Math.Min(wanted, Math.Min(ceiling, byCredits));
		if (effective >= wanted)
		{
			return (wanted, DevTimeScaleLimit.None);
		}

		return (effective, byCredits <= ceiling ? DevTimeScaleLimit.Credits : DevTimeScaleLimit.Ceiling);
	}
}
