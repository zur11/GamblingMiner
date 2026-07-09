using Godot;
using System;
using System.Collections.Generic;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
#nullable enable

// Step 14 (ND.2) — the historical network population scheduler: the PURE controller for the two
// hybrid layers of P-14.A (step14 plan §3.1).
//
//   • VISIBLE CAST — real, named, registry-backed miner bots spawned as the historical hashrate curve
//     grows (one per new block at most, "spawn drip"); each powered member wields the era-standard
//     power. They mine founder-style: drained nonce attempts in lockstep with the player's time
//     advancement — concurrent miners, never clock movers. No SC finances, no betting runners in v1.
//   • INVISIBLE MASS — one aggregate power term covering the rest of the network
//     (TotalNetworkUnits − player − bots − founders − cast), whose mined blocks are attributed to
//     rotating GHOST miner names (D-14.9) with session-transient one-off wallets — their BTC is
//     frozen forever (D-14.11, the retired-Satoshi precedent).
//
// Pure and stateless-on-disk, like FoundersMiningService: callers (SimulationService) feed it the live
// facts each block and it returns powers + per-miner attempt counts. Nothing persists — cast IDENTITY
// lives in BotWalletRegistry (reset-spared like all identity files); everything else re-derives from
// the game date + the dataset (D-14.7 time-shiftability for free).
public static class NetworkPopulationScheduler
{
	// Chronological flavor pool for the visible cast — early individuals first, pool-era handles later,
	// so spawns read historically as the clock advances. Cosmetic only (D-14.5 keeps pool MECHANICS out
	// of scope; a miner named like a pool is just that pool's presence in the cast). Exhaustion falls
	// back to miner_extra_N (never expected: pool size > max cast 33 − BaseCast 4).
	private static readonly string[] CastNamePool =
	{
		"artforz", "laszlo", "tcatm", "jgarzik", "theymos", "nanotube", "dooglus", "davout",
		"molecular", "vladimir", "deepbit", "btcguild", "eligius", "bitminter", "ozcoin", "p2pool",
		"asicminer", "ghash_io", "f2pool", "bitfury", "kncminer", "antpool", "bwpool", "viabtc",
		"btc_top", "poolin", "btc_com", "huobi_pool", "okex_pool", "foundry_usa", "marapool", "luxor",
		"ocean_pool", "sbi_crypto", "braiins", "ultimus"
	};

	// Rotating pseudonym pool for the invisible mass's blocks (D-14.9) — anonymous flavor, deliberately
	// distinct from the cast pool. The rotation advances once per ghost-mined block.
	private static readonly string[] GhostNamePool =
	{
		"unknown_miner", "anon_cpu_rig", "garage_gpu", "dorm_room_rig", "mystery_hasher",
		"stranger_node", "lost_keys_miner", "nomad_rig", "basement_farm", "silent_hasher",
		"drifter_node", "forgotten_rig"
	};

	// Per-frame budget for drained scheduled attempts: each attempt is a real candidate-header hash, and
	// late-game the scheduled mass can owe hundreds of attempts per player bet — an unbounded drain
	// could stall a frame at high DevTimeScale. Undelivered attempts stay in the accumulators (capped
	// below, shedding truly unpayable debt); a sustained shortfall just slows blocks slightly, which the
	// difficulty regulator's LWMA feedback then trims — the system self-corrects by design (Ch. 26).
	private const int MaxScheduledAttemptsPerFrame = 5000;
	private const double AccumulatorCap = 10000d;

	private static readonly Dictionary<string, double> _castAccumulators = new();
	private static double _invisibleAccumulator;
	private static int _ghostIndex;

	// Cached by Recompute (once per new block); read by SimulationService every frame.
	private static readonly List<string> _poweredCastIds = new();
	private static double _castPowerEach;
	private static double _invisiblePower;
	private static double _lastDecades;
	private static int _lastCastTarget;

	public static double TotalScheduledPower { get; private set; }
	public static double LastInvisiblePower => _invisiblePower;
	public static double CastPowerEach => _castPowerEach;
	public static IReadOnlyList<string> PoweredCastIds => _poweredCastIds;

	// ── Per-block recompute (SimulationService, once per new chain length) ─────────────────────────────
	// playerBotsPower = the player+bots power (the same value fed to the founders' drain denominator);
	// foundersPower = FoundersMiningService.TotalActiveFounderPower AFTER its own recompute this block.
	public static void Recompute(BtcNetworkDataService data, DateTime nowLocal, double playerBotsPower, double foundersPower)
	{
		_lastDecades = data.GetDecades(nowLocal);
		_lastCastTarget = data.GetTargetVisibleMiners(nowLocal);
		_castPowerEach = data.GetEraStandardPower(nowLocal);

		// Power the first (target − BaseCast) cast members, in spawn order. Extra registry members (an
		// identity-spared registry meeting a younger world after a clean reset) simply sit dormant.
		int poweredCount = Math.Clamp(_lastCastTarget - BtcNetworkDataService.BaseCast, 0, BotWalletRegistry.CastMiners.Count);
		_poweredCastIds.Clear();
		for (int i = 0; i < poweredCount; i++)
		{
			_poweredCastIds.Add(BotWalletRegistry.CastMiners[i].NodeId);
		}

		double castTotal = _castPowerEach * _poweredCastIds.Count;
		double totalUnits = data.GetTotalNetworkUnits(nowLocal);
		_invisiblePower = Math.Max(0d, totalUnits - playerBotsPower - foundersPower - castTotal);
		TotalScheduledPower = castTotal + _invisiblePower;
	}

	// The next cast name a spawn should use (first pool name not yet in the registry), or a numbered
	// fallback if the curated pool is somehow exhausted.
	public static string NextCastName()
	{
		var taken = new HashSet<string>();
		foreach (BotWalletRecord record in BotWalletRegistry.CastMiners)
		{
			taken.Add(record.NodeId);
		}

		foreach (string name in CastNamePool)
		{
			if (!taken.Contains(name))
			{
				return name;
			}
		}

		return $"miner_extra_{BotWalletRegistry.CastMiners.Count + 1}";
	}

	public static string CurrentGhostId => GhostNamePool[_ghostIndex];

	public static void AdvanceGhostRotation() => _ghostIndex = (_ghostIndex + 1) % GhostNamePool.Length;

	// ── The lockstep drain (founder accumulator pattern) ───────────────────────────────────────────────
	// For every player+bot nonce attempt executed this frame, each powered cast member and the invisible
	// mass accrue attempts ∝ their power share of playerBotsPower, budget-capped per frame. Returns
	// (minerId, attempts, isGhost) tuples; ghost attempts are attributed to CurrentGhostId by the caller.
	public static IReadOnlyList<(string minerId, int attempts, bool isGhost)> DrainScheduledAttempts(int playerBotAttempts, double playerBotsPower)
	{
		var result = new List<(string, int, bool)>();
		if (playerBotAttempts <= 0 || playerBotsPower <= 0d)
		{
			return result;
		}

		int budget = MaxScheduledAttemptsPerFrame;

		foreach (string castId in _poweredCastIds)
		{
			if (budget <= 0)
			{
				break;
			}

			double acc = _castAccumulators.TryGetValue(castId, out double a) ? a : 0d;
			acc = Math.Min(AccumulatorCap, acc + playerBotAttempts * (_castPowerEach / playerBotsPower));
			int attempts = Math.Min(budget, (int)Math.Floor(acc));
			if (attempts > 0)
			{
				acc -= attempts;
				budget -= attempts;
				result.Add((castId, attempts, false));
			}
			_castAccumulators[castId] = acc;
		}

		if (_invisiblePower > 0d && budget > 0)
		{
			_invisibleAccumulator = Math.Min(AccumulatorCap, _invisibleAccumulator + playerBotAttempts * (_invisiblePower / playerBotsPower));
			int attempts = Math.Min(budget, (int)Math.Floor(_invisibleAccumulator));
			if (attempts > 0)
			{
				_invisibleAccumulator -= attempts;
				result.Add((CurrentGhostId, attempts, true));
			}
		}

		return result;
	}

	// ── DEV telemetry (founders_trace.csv precedent) — one row per new block ───────────────────────────
	private const string TracePath = "user://logs/network_population_trace.csv";

	public static void AppendTelemetry(int blockIndex, string lastMiner, long tsMs, double playerBotsPower, double foundersPower, string? spawnedNodeId)
	{
		try
		{
			if (!DirAccess.DirExistsAbsolute("user://logs"))
			{
				DirAccess.MakeDirRecursiveAbsolute("user://logs");
			}

			bool exists = FileAccess.FileExists(TracePath);
			using FileAccess file = exists
				? FileAccess.Open(TracePath, FileAccess.ModeFlags.ReadWrite)
				: FileAccess.Open(TracePath, FileAccess.ModeFlags.Write);
			if (file == null)
			{
				return;
			}

			if (exists)
			{
				file.SeekEnd();
			}
			else
			{
				file.StoreLine("utcMs,blockIndex,lastMiner,decades,castTarget,castPowered,castPowerEach,invisiblePower,playerBotsPower,foundersPower,totalPower,spawned");
			}

			double totalPower = playerBotsPower + foundersPower + TotalScheduledPower;
			file.StoreLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
				"{0},{1},{2},{3:F3},{4},{5},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3},{11}",
				tsMs, blockIndex, lastMiner, _lastDecades, _lastCastTarget, _poweredCastIds.Count,
				_castPowerEach, _invisiblePower, playerBotsPower, foundersPower, totalPower,
				spawnedNodeId ?? string.Empty));
		}
		catch (Exception e)
		{
			GD.PushWarning($"[NetworkPopulationTrace] failed: {e.Message}");
		}
	}

	// Test/diagnostic reset (no persisted state; restores fresh runtime).
	public static void ResetRuntime()
	{
		_castAccumulators.Clear();
		_invisibleAccumulator = 0d;
		_ghostIndex = 0;
		_poweredCastIds.Clear();
		_castPowerEach = 0d;
		_invisiblePower = 0d;
		TotalScheduledPower = 0d;
		_lastDecades = 0d;
		_lastCastTarget = 0;
	}
}
