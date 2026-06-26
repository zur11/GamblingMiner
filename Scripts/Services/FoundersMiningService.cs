using Godot;
using System;
using System.Collections.Generic;
#nullable enable

// Step 7 — Phase 7.1: the founder hashrate controller.
//
// Owns the PLAYER-ERA mining power of the founder nodes (Satoshi + Hal) and the math that drives it.
// It is a PURE controller: it holds no chain/Godot state and never mines or queries the blockchain
// itself — callers feed it the live facts (other miners' power, the game clock, Satoshi's confirmed
// BTC) and it returns powers + per-founder nonce-attempt counts. The actual mining + chain queries
// live in SimulationService / NetworkRoot (wired in Phase 7.2). No persisted state — everything is
// recomputed from the live world each launch, matching the rest of the between-block model.
//
// Founders mine IN LOCKSTEP with the player's time advancement: they only get nonce attempts while
// the player advances time by betting (DrainFounderAttempts is fed the bets executed that frame), so
// they add hashrate, never clock motion (OQ-2 refinement — see step7 plan §0/§2.1).
//
// The three founders are NOT symmetric (step7 plan §2.3):
//   • Satoshi — power regulated per block to hit SatoshiTargetBtc by SatoshiEarliestDisappearance.
//   • Hal     — drip miner, power decays linearly to 0 by ~Aug 2009 (his real ALS turning point).
//   • Hearn   — never mines (handled by the HistoricalEventScheduler, Phase 7.4); absent here.
public partial class FoundersMiningService : Node
{
	public const string SatoshiNodeId = "satoshi";
	public const string HalNodeId = "hal";

	// ── Satoshi targeting (step7 plan §2.2) ────────────────────────────────────
	// 1% of his real ≈1.1M BTC. Spendable only — excludes the unspendable genesis 50 (OQ-8). The
	// ~10% block share this implies is a HISTORICAL requirement, not a tunable (Q-A1).
	public const decimal SatoshiTargetBtc = 11000m;
	// Never retire before this floor; if still short, ramp exponentially past it, then retire (OQ-3).
	private static readonly DateTime SatoshiEarliestDisappearance = new(2011, 4, 26, 0, 0, 0, DateTimeKind.Local);
	// ≈585 attempts/block × 100 in-game s = 58,500 in-game s/block ⇒ 1.477 blocks per in-game day.
	private const double BlocksPerInGameDay = 1.477;
	// Exponential ramp base used only when past the floor date and still short of target.
	private const double Growth = 1.15;
	// Max win-share fed to shareToWeight, so the weight never diverges (s→1 ⇒ w→∞).
	private const double MaxShare = 0.99;

	// ── Hal decay (step7 plan §2.3, Q-N2) ──────────────────────────────────────
	// Enters the player era at one participant's power and decays linearly to 0 by his ALS turning point.
	private const double HalBaselinePower = 1.0;
	private static readonly DateTime HalDecayStart = new(2009, 3, 21, 0, 0, 0, DateTimeKind.Local); // player start day
	private static readonly DateTime HalDecayEnd = new(2009, 8, 1, 0, 0, 0, DateTimeKind.Local);    // ~ALS diagnosis

	private sealed class FounderRuntime
	{
		public double Power;           // current mining power (same unit as _activeMiningPower: bets/sec-equiv)
		public double Accumulator;     // fractional nonce attempts owed, carried across frames
		public bool Retired;
		public DateTime? RetiredAtLocal;
	}

	private readonly FounderRuntime _satoshi = new();
	private readonly FounderRuntime _hal = new();

	// Last inputs, cached so the FoundersWallets readout (Phase 7.5) can render without re-deriving them.
	private decimal _lastSatoshiConfirmedBtc;
	private double _lastOtherMinersPower;
	private DateTime _lastNowLocal = HalDecayStart;

	// ── Phase 7.1b: per-block power recompute ──────────────────────────────────
	// Recompute each founder's power. Call once per mined block (any miner) in the player era, and from
	// the readout to refresh. `otherMinersPower` = Σ power of every OTHER active miner (player + bots +
	// Hal while he mines — Q-N4); Satoshi is excluded from his own denominator. Pure: updates power +
	// retirement only, never accumulators. Idempotent for a given (otherMinersPower, nowLocal, btc).
	public void RecomputeFounderPowers(double otherMinersPower, DateTime nowLocal, decimal satoshiConfirmedBtc)
	{
		_lastOtherMinersPower = Math.Max(0d, otherMinersPower);
		_lastNowLocal = nowLocal;
		_lastSatoshiConfirmedBtc = satoshiConfirmedBtc;

		RecomputeHalPower(nowLocal);
		// Hal is one of "the others" in Satoshi's share denominator while he still mines (Q-N4).
		double satoshiOthers = _lastOtherMinersPower + (_satoshi.Retired ? 0d : _hal.Power);
		RecomputeSatoshiPower(satoshiOthers, nowLocal, satoshiConfirmedBtc);
	}

	private void RecomputeHalPower(DateTime nowLocal)
	{
		if (nowLocal <= HalDecayStart)
		{
			_hal.Power = HalBaselinePower;
			return;
		}
		if (nowLocal >= HalDecayEnd)
		{
			_hal.Power = 0d; // dormant after ~Aug 2009; receive-only afterward
			return;
		}

		double span = (HalDecayEnd - HalDecayStart).TotalDays;
		double remaining = (HalDecayEnd - nowLocal).TotalDays;
		_hal.Power = HalBaselinePower * Math.Clamp(remaining / span, 0d, 1d);
	}

	private void RecomputeSatoshiPower(double otherMinersPower, DateTime nowLocal, decimal satoshiConfirmedBtc)
	{
		if (_satoshi.Retired)
		{
			_satoshi.Power = 0d;
			return;
		}

		bool pastFloor = nowLocal >= SatoshiEarliestDisappearance;
		if (pastFloor && satoshiConfirmedBtc >= SatoshiTargetBtc)
		{
			Retire(nowLocal);
			return;
		}

		double reward = 50d; // era-0 block reward for this whole window
		double btcRemaining = Math.Max(0d, (double)(SatoshiTargetBtc - satoshiConfirmedBtc));

		if (!pastFloor)
		{
			// Pace the ramp so he reaches ~11,000 around the floor date, never sooner.
			double blocksUntilFloor = Math.Max(1d, Math.Ceiling(InGameDaysUntil(SatoshiEarliestDisappearance, nowLocal) * BlocksPerInGameDay));
			double targetShare = Math.Clamp(btcRemaining / blocksUntilFloor / reward, 0d, 1d);
			_satoshi.Power = ShareToWeight(targetShare, otherMinersPower);
		}
		else
		{
			// Past floor but still short: ramp power EXPONENTIALLY to finish ASAP, then retire (OQ-3).
			double blocksPastFloor = Math.Max(0d, Math.Ceiling(InGameDaysUntil(nowLocal, SatoshiEarliestDisappearance) * BlocksPerInGameDay));
			double basePower = otherMinersPower > 0d ? otherMinersPower : 1d;
			_satoshi.Power = basePower * Math.Pow(Growth, blocksPastFloor);
		}
	}

	// Converts a desired win-share s against the rest of the field W into a weight: w = s/(1-s)·W.
	private static double ShareToWeight(double share, double othersPower)
	{
		double s = Math.Clamp(share, 0d, MaxShare);
		if (s <= 0d) return 0d;
		double others = othersPower > 0d ? othersPower : 1d;
		return s / (1d - s) * others;
	}

	private static double InGameDaysUntil(DateTime to, DateTime from) =>
		Math.Max(0d, (to - from).TotalDays);

	private void Retire(DateTime nowLocal)
	{
		_satoshi.Power = 0d;
		_satoshi.Retired = true;
		_satoshi.RetiredAtLocal = nowLocal;
		GD.Print($"[FoundersMining] Satoshi retired at {nowLocal:yyyy-MM-dd} with {_lastSatoshiConfirmedBtc:F2} BTC (target {SatoshiTargetBtc}).");
	}

	// ── Phase 7.1c: the lockstep attempt accumulator ───────────────────────────
	// For every NON-founder nonce attempt executed this frame (player + bots), each active founder
	// accrues attempts ∝ its power share, and we hand back the whole-number attempts it should now make.
	// Founders only ever advance while the player advances time → no autonomous clock motion. `otherMinersPower`
	// is the same denominator passed to RecomputeFounderPowers (Σ other active miners' power).
	public IReadOnlyList<(string founderId, int attempts)> DrainFounderAttempts(int nonFounderAttempts, double otherMinersPower)
	{
		var result = new List<(string, int)>(2);
		if (nonFounderAttempts <= 0 || otherMinersPower <= 0d)
		{
			return result;
		}

		AddDrained(result, SatoshiNodeId, _satoshi, nonFounderAttempts, otherMinersPower);
		AddDrained(result, HalNodeId, _hal, nonFounderAttempts, otherMinersPower);
		return result;
	}

	private static void AddDrained(List<(string, int)> result, string id, FounderRuntime f, int nonFounderAttempts, double otherMinersPower)
	{
		if (f.Retired || f.Power <= 0d)
		{
			f.Accumulator = 0d;
			return;
		}

		f.Accumulator += nonFounderAttempts * (f.Power / otherMinersPower);
		int attempts = (int)Math.Floor(f.Accumulator);
		if (attempts <= 0)
		{
			return;
		}

		f.Accumulator -= attempts;
		result.Add((id, attempts));
	}

	// Total power the founders add to the network, fed into NetworkRoot.SetActiveMiningPower so the
	// difficulty regulator accounts for them and block pacing stays at TargetBlockSeconds (Phase 7.2).
	public double TotalActiveFounderPower => (_satoshi.Retired ? 0d : _satoshi.Power) + _hal.Power;

	// ── Readout getters (FoundersWallets dev scene, Phase 7.5) ──────────────────
	public double SatoshiPower => _satoshi.Power;
	public double HalPower => _hal.Power;
	public bool SatoshiRetired => _satoshi.Retired;
	public DateTime? SatoshiRetiredAtLocal => _satoshi.RetiredAtLocal;
	public decimal SatoshiConfirmedBtc => _lastSatoshiConfirmedBtc;
	public decimal SatoshiTarget => SatoshiTargetBtc;
	public DateTime SatoshiFloorDateLocal => SatoshiEarliestDisappearance;

	// Satoshi's instantaneous share of the network (his power / total network power), 0 when retired.
	public double SatoshiShare
	{
		get
		{
			double total = _lastOtherMinersPower + _hal.Power + _satoshi.Power;
			return total > 0d ? _satoshi.Power / total : 0d;
		}
	}

	// Rough blocks-until-target at the current pace (for the readout); 0 once retired or at/over target.
	public double EstimatedBlocksUntilTarget
	{
		get
		{
			if (_satoshi.Retired) return 0d;
			double remaining = Math.Max(0d, (double)(SatoshiTargetBtc - _lastSatoshiConfirmedBtc));
			return Math.Ceiling(remaining / 50d);
		}
	}

	// Test/diagnostic reset (no persisted state to clear; restores fresh runtime).
	public void ResetForNewGame()
	{
		_satoshi.Power = 0d; _satoshi.Accumulator = 0d; _satoshi.Retired = false; _satoshi.RetiredAtLocal = null;
		_hal.Power = 0d; _hal.Accumulator = 0d; _hal.Retired = false; _hal.RetiredAtLocal = null;
		_lastSatoshiConfirmedBtc = 0m;
		_lastOtherMinersPower = 0d;
		_lastNowLocal = HalDecayStart;
	}
}
