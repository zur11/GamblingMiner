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
	/// Measured, not chosen (mini-plan 09 P3a): in DiceGame, the most expensive scene to bet in, a saturated
	/// frame at the default <c>MaxBetsPerFrame</c> delivered ~2,070–2,120 bets/s at ~52 fps, which holds D-09.1's
	/// 50 fps minimum. This sits about 5% below that, so 99 credits govern to 2000X, where retention was 0.995.
	/// Lighter scenes (the hardware shop measured at 1.83 ms of sim for the same 40 bets) could run faster; one
	/// conservative budget everywhere is a deliberate simplification, not an oversight.
	///
	/// <b>Measured on one machine, in 2009.</b> A later era may cost more per bet (mini-plan 09 §5). A budget
	/// that turns out generous costs honest wall-clock slowdown through R2-C1, never distorted game dynamics.
	/// </summary>
	public const double BetBudgetPerSecond = 2000.0;

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
