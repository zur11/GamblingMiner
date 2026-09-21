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
	/// <b>D-11.1 (2026-09-21): 9,000 → 44,000, MEASURED again, and D-10.4's derivation below is retired.</b> It
	/// means what it meant before D-10.4: what the frame can take. Mini-plan 11 ran the largest demand the game can
	/// produce — the player and all four bots at the hardware cap, at the clock's ceiling, 44,550 bets/s — in
	/// DiceGame's Detailed view, in two sessions. The slower one delivered <b>44,195 bets/s at 56 fps</b>, and
	/// 44,000 is that figure rounded down, per mini-plan 11 §4 A.
	///
	/// <para>It is a <b>lower bound</b> on the frame, not its limit: the frame never broke, because there was no
	/// more demand to give it. So it binds in exactly one configuration — all five engines at the cap — and holds
	/// it to 88X instead of 90X. At 90X that configuration was already slightly saturated: the clock ran 0.3–0.5%
	/// slower than the readout said. Every other configuration reaches the ceiling.</para>
	///
	/// <para><b>One budget for every engine type, priced on the dearest.</b> A bot bet costs 0.0062 ms, a player
	/// bet 0.0286 ms (mini-plan 11 B). Weighting engines would let bots run faster; it is a design change nothing
	/// here needs, because the budget binds only at the configuration above. <b>Measured in August 2009</b>, when
	/// the historical network mines almost nothing; mini-plan 11 Part C measures later eras.</para>
	///
	/// <para>The history below is kept because it is what the number used to mean.</para>
	///
	/// D-10.4 (2026-09-20): 1,700 → 9,000, DERIVED rather than measured. It was what the clock could demand at
	/// the hardware cap — 100 credits × the ceiling's scale of 90 — so <b>with the player betting alone</b>, for the
	/// hardware that exists, the budget never bound and <see cref="CalendarTimeService.MaxGameSecondsPerRealSecond"/>
	/// governed instead.
	///
	/// <para>⚠ <b>Not with bots running (mini-plan 11 §0, 2026-09-21).</b> <c>runningCredits</c> is the sum over
	/// EVERY running engine, and each engine is clamped to the hardware cap on its own, so the player and four
	/// bots at the cap ask for five times what one engine can. The budget then holds the scale to about a fifth of
	/// the ceiling — a consequence of the derivation above, not a measured limit. It did throttle the bots for
	/// nothing: the frame took all five at the ceiling (D-11.1 above).</para>
	///
	/// <para>What made that safe was mini-plan 10: DiceGame's bet list cost 0.198 ms per bet and now costs
	/// <b>0.025</b>, because rows no longer move and only the ~14 rows inside the scroll's viewport are painted,
	/// once per frame. Two sessions (2026-09-19 and 2026-09-20) agree: at 99 credits × 9000X the game delivers
	/// all ~8,910 bets/s at 58–60 fps with retention 1.000, and the frame's own limit was never found because
	/// the clock's ceiling binds first. This figure is therefore NOT "the most the frame can take" — it is "more
	/// than anything can ask for today", which is a different claim and the honest one.</para>
	///
	/// <para><b>Measured with the player betting alone, in 2009, in DiceGame.</b> A bot bet was assumed to cost
	/// like a player bet (mini-plan 11 B measured it at 22% of one). The budget is a TOTAL over every running engine, so if it
	/// ever binds again, re-price a bet first — and with several engines running, per-engine
	/// <c>MaxBetsPerFrame</c> is the other half of the story.</para>
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
	public const double BetBudgetPerSecond = 44000.0;

	// Mini-plan 10 A3 — a DEBUG-only override, so the budget can be lifted inside ONE run to find what a cheaper
	// bet view can actually deliver: the budget was the figure under re-measurement, so it could not be left in
	// charge of the measurement (A1's Off leg sat at vsync with headroom nobody could see). It is kept because
	// D-10.4's budget is derived, not measured — the next re-measurement needs the same escape hatch.
	// 0 = no override; double.PositiveInfinity = no budget, only the clock's ceiling. RELEASE builds cannot set
	// it (the setter is Conditional), so there BudgetInForce always reads the constant.
	private static double _budgetOverride;

	/// <summary>Raised when the DEBUG override changes, so the caller re-governs instead of waiting for credits.</summary>
	public static event Action BudgetOverrideChanged;

	/// <summary>The budget <see cref="Govern"/> actually applies: the override when one is set, else the constant.</summary>
	public static double BudgetInForce => _budgetOverride > 0d ? _budgetOverride : BetBudgetPerSecond;

	/// <summary>DEBUG only — overrides the bet budget for a measurement run; 0 restores the constant.</summary>
	[System.Diagnostics.Conditional("DEBUG")]
	public static void SetBudgetOverrideForDiagnostics(double betsPerSecond)
	{
		_budgetOverride = betsPerSecond > 0d ? betsPerSecond : 0d;
		Godot.GD.Print(double.IsPositiveInfinity(BudgetInForce)
			? "[DevTimeScale] bet budget is now OFF — only the clock's ceiling governs. DEBUG override, not persisted."
			: string.Create(System.Globalization.CultureInfo.InvariantCulture,
				$"[DevTimeScale] bet budget is now {BudgetInForce:N0} bets/s (default {BetBudgetPerSecond:N0}) — DEBUG override, not persisted."));
		BudgetOverrideChanged?.Invoke();
	}

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
			? (double.IsPositiveInfinity(BudgetInForce)
				? int.MaxValue
				: Math.Max(1, (int)Math.Floor(BudgetInForce / runningCredits)))
			: int.MaxValue;

		int effective = Math.Min(wanted, Math.Min(ceiling, byCredits));
		if (effective >= wanted)
		{
			return (wanted, DevTimeScaleLimit.None);
		}

		return (effective, byCredits <= ceiling ? DevTimeScaleLimit.Credits : DevTimeScaleLimit.Ceiling);
	}
}
