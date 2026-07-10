using Godot;
using System;
using System.Collections.Generic;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
#nullable enable

// Step 3a — first-launch historical bootstrap.
// On a brand-new game, mines the blockchain from the genesis instant (3 Jan 2009) up to a random
// time on 21 Mar 2009, so the player always starts on 21 Mar with a believable early chain.
// Satoshi mines almost everything; Hal mines exactly 3 spaced blocks. No betting involved — this is
// the only autonomous (no-player) mining window in the game (OQ-2).
//
// Step 3b (Satoshi 11,000-BTC dynamic ramp / disappearance) and 3c (12 Jan 10 BTC Satoshi→Hal tx)
// build on this; here Satoshi simply mines every non-Hal block.
//
// Step 14 (EB.1, §5.1) — the entry-year extension. This ~113-block canonical segment (genesis →
// 21 Mar 2009) is UNCHANGED and always runs first, identically for every entry year (it IS the shared
// history prefix). When TimelineConfig.DevEntryYear is set, a SECOND phase then continues the SAME
// jittered block-cadence onward — from 21 Mar 2009 to 21 Mar of the chosen year — using the richer
// weighted-power model (the same one live play uses from FoundersMiningService/NetworkPopulationScheduler)
// instead of the first phase's simple Satoshi/Hal turn-taking, because from player-start onward is
// exactly where those schedulers already take over in live play. Nothing here runs on `main`
// (DevEntryYear = 0 ⇒ Phase 2's loop condition is immediately false ⇒ zero behavior change).
public static class HistoricalBootstrapService
{
	// Player always begins on this calendar day; the exact time-of-day is randomised per run.
	// Shared with FoundersMiningService.HalDecayStart via TimelineConfig.PlayerStartDayLocal (§3.2).
	private static readonly DateTime PlayerStartDayLocal = TimelineConfig.PlayerStartDayLocal;

	// Hal joins 11 Jan 2009 and mines exactly 3 spaced bootstrap blocks near these dates.
	private static readonly DateTime[] HalBlockDatesLocal =
	{
		TimelineConfig.Shift(new DateTime(2009, 1, 12, 0, 0, 0, DateTimeKind.Local)),
		TimelineConfig.Shift(new DateTime(2009, 2,  5, 0, 0, 0, DateTimeKind.Local)),
		TimelineConfig.Shift(new DateTime(2009, 3,  5, 0, 0, 0, DateTimeKind.Local)),
	};

	// ~16h 40m in-game per block at 100X (≈585 attempts/block × 100 in-game seconds).
	private const long BlockIntervalMs = 58_500_000L;

	// Step 7.3 (E4): the famous first person-to-person tx — Satoshi → Hal, 10 BTC, ~12 Jan 2009.
	// Injected into the mempool once the bootstrap clock reaches this date, so it confirms in the
	// block whose timestamp ≈ 12 Jan (real block 170; ~block 13 here — dates, not heights, rule).
	private static readonly DateTime E4DateLocal = TimelineConfig.Shift(new DateTime(2009, 1, 12, 0, 0, 0, DateTimeKind.Local));
	private const decimal E4AmountBtc = 10m;
	private const string E4Salt = "hist_E4_satoshi_hal_10";

	public static bool DidRun { get; private set; }
	public static DateTime? LandingLocalDateTime { get; private set; }

	public static void RunIfFirstLaunch()
	{
		if (DidRun)
		{
			return;
		}

		NetworkRoot.EnsureReady();
		if (NetworkRoot.GetPlayerChainLengthStatic() > 1)
		{
			// Chain already has mined history → returning player, not a first launch.
			return;
		}

		Run();
	}

	private static void Run()
	{
		var rng = new Random();

		// Mine until the tip crosses into 21 Mar 2009 (the block that crosses IS mined, unlike a random
		// same-day target that would stop just short of it) — the player's start is derived from that
		// tip below, not from an independently-rolled random time, so there is no dead/idle gap between
		// the last historical block and the player's first in-game instant.
		long marchTwentyFirstStartMs = new DateTimeOffset(PlayerStartDayLocal).ToUnixTimeMilliseconds();

		var halTargets = new Queue<long>();
		foreach (DateTime d in HalBlockDatesLocal)
		{
			halTargets.Enqueue(new DateTimeOffset(d).ToUnixTimeMilliseconds());
		}

		long ts = BlockchainService.GenesisTimestampUnixMs;
		long lastMinedTs = ts;
		long e4DateMs = new DateTimeOffset(E4DateLocal).ToUnixTimeMilliseconds();
		bool e4Injected = false;
		int satoshiBlocks = 0;
		int halBlocks = 0;
		string? entrySummary = null;

		NetworkRoot.BeginBulkMining();
		try
		{
			while (true)
			{
				// Advance by one block interval with ±30% jitter so timestamps look organic.
				double jitterFactor = 1.0 + (rng.NextDouble() - 0.5) * 0.6; // 0.7 .. 1.3
				ts += (long)(BlockIntervalMs * jitterFactor);

				// E4: once the clock reaches 12 Jan, inject the Satoshi→Hal 10 BTC tx BEFORE mining this
				// block so it lands in it. Retries on later blocks if Satoshi isn't funded yet (he is by now).
				if (!e4Injected && ts >= e4DateMs)
				{
					e4Injected = NetworkRoot.InjectHistoricalSignedTxStatic("satoshi", "hal", E4AmountBtc, E4Salt);
				}

				bool halTurn = halTargets.Count > 0 && ts >= halTargets.Peek();
				bool mined;
				if (halTurn)
				{
					halTargets.Dequeue();
					mined = NetworkRoot.MineNodeStatic("hal", ts);
					if (mined)
					{
						halBlocks++;
					}
				}
				else
				{
					mined = NetworkRoot.MineNodeStatic("satoshi", ts);
					if (mined)
					{
						satoshiBlocks++;
					}
				}

				if (mined)
				{
					lastMinedTs = ts;
				}

				if (ts >= marchTwentyFirstStartMs)
				{
					break;
				}
			}

			// Step 14 (EB.1) — Phase 2: continue the SAME jittered cadence past 21 Mar 2009 up to
			// TimelineConfig.EntryDayLocal, if a DEV entry year is set. Runs inside the SAME bulk-mining
			// bracket as Phase 1 (see the class-level comment) — a no-op loop (0 iterations) when
			// DevEntryYear == 0, so canonical play is bit-for-bit unaffected.
			(ts, lastMinedTs, entrySummary) = RunEntryYearExtension(rng, ts, lastMinedTs);
		}
		finally
		{
			NetworkRoot.EndBulkMiningAndPersist();
		}

		// Exactly the last mined block's timestamp — every post-bootstrap checkpoint is captured at the
		// calendar instant equal to the mined block's own timestamp (CaptureCheckpoint reads
		// CalendarTimeService.CurrentLocalDateTime synchronously right after mining, no clock advance in
		// between), so the player's start instant matches that same convention exactly (no +1s offset).
		DateTime landingLocal = DateTimeOffset.FromUnixTimeMilliseconds(lastMinedTs).LocalDateTime;
		DidRun = true;
		LandingLocalDateTime = landingLocal;
		if (entrySummary != null)
		{
			GD.Print($"[HistoricalBootstrap] Entry-year extension → {landingLocal:yyyy-MM-dd HH:mm:ss}. {entrySummary}");
		}
		GD.Print($"[HistoricalBootstrap] First launch — mined genesis → {landingLocal:yyyy-MM-dd HH:mm:ss}. " +
				 $"Satoshi {satoshiBlocks} blocks, Hal {halBlocks} blocks. E4 (10 BTC Satoshi→Hal): {(e4Injected ? "on-chain" : "skipped")}.");
	}

	// Seed amount for each already-introduced non-miner right before landing (D-EB.1, tx-history option
	// A — "a few miner→non-miner donations so holders land funded", NOT the full live ND.3 budget).
	private const decimal SeedFundingAmountBtc = 1.0m;

	// Step 14 (EB.1, §5.1) — Phase 2: continues the jittered cadence from 21 Mar 2009 to
	// TimelineConfig.EntryDayLocal using the weighted-power model (the same one live play hands off to
	// from player start onward). No-op (returns the inputs unchanged, null summary) when DevEntryYear
	// is 0, so canonical play never even allocates the throwaway service instances below.
	private static (long ts, long lastMinedTs, string? summary) RunEntryYearExtension(Random rng, long ts, long lastMinedTs)
	{
		// DevEntryYear is a compile-time const (0 on main forever) — the compiler folds this branch and
		// flags everything below as unreachable on a canonical build. Deliberate: this method's body is
		// dead code on main and must stay for the next DEV entry-year test (TimelineConfig comment /
		// ProjectDesignManual Ch. 35 precedent — same suppression pattern as StatusBar's alt-timeline watermark).
#pragma warning disable CS0162
		if (TimelineConfig.DevEntryYear == 0)
		{
			return (ts, lastMinedTs, null);
		}

		long entryMs = new DateTimeOffset(TimelineConfig.EntryDayLocal).ToUnixTimeMilliseconds();

		// Throwaway instances — never added to the scene tree (nothing here runs before any scene
		// exists, per the class-level comment). FoundersMiningService is a pure controller with no
		// Godot-lifecycle dependency (field-initialized, no _Ready); BtcNetworkDataService needs its
		// explicit EnsureLoaded() (extracted for exactly this use); NetworkRoot's entire state is static
		// (SharedNodesById/SharedNetwork) — a bare `new NetworkRoot()` is a fully functional accessor
		// for its instance methods, identical to using the "real" live autoload instance.
		var founders = new FoundersMiningService();
		var netData = new BtcNetworkDataService();
		netData.EnsureLoaded();
		var netRoot = new NetworkRoot();

		int satoshiBlocks = 0, halBlocks = 0, castBlocks = 0, ghostBlocks = 0, spawnedCast = 0, seededNonMiners = 0;

		while (ts < entryMs)
		{
			double jitterFactor = 1.0 + (rng.NextDouble() - 0.5) * 0.6; // 0.7 .. 1.3 — same cadence as Phase 1
			ts += (long)(BlockIntervalMs * jitterFactor);
			DateTime nowLocal = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
			bool isLandingBlock = ts >= entryMs;

			// Cast spawn-drip (ND.2 pattern, mirrored): at most one new cast miner per block if under target.
			int target = netData.GetTargetVisibleMiners(nowLocal);
			if (BtcNetworkDataService.BaseCast + BotWalletRegistry.CastMiners.Count < target)
			{
				string spawnedId = NetworkPopulationScheduler.NextCastName();
				BotWalletRegistry.AddCastMiner(spawnedId);
				if (netRoot.RegisterCastMinerNode(spawnedId))
				{
					spawnedCast++;
				}
			}

			// Founders FIRST (regulated against last block's cached scheduled total — one-block lag, the
			// SAME order SimulationService uses live), then the population scheduler (fresh founders power).
			decimal satoshiBtc = netRoot.GetNodeSpendableBalance(FoundersMiningService.SatoshiNodeId);
			founders.RecomputeFounderPowers(NetworkPopulationScheduler.TotalScheduledPower, nowLocal, satoshiBtc);
			NetworkPopulationScheduler.Recompute(netData, nowLocal, playerBotsPower: 0d, foundersPower: founders.TotalActiveFounderPower);

			// D-EB.1 seed-funding pass: on the landing block only, before mining it, so the seed txs
			// confirm in the very block that lands the player — no extra block, no timestamp drift.
			// Known, accepted imprecision: GetNonMinerAuctionLedger() reads "now" off the CHAIN TIP
			// (still the previous block — this one hasn't mined yet), not `ts` itself, so the intro-
			// status check is off by at most one block interval (~16-24h). Only consequence: a non-miner
			// whose introduction date falls exactly inside that gap misses its 1-BTC seed gift this run —
			// harmless (the real EB.2 intro SCHEDULE is date-derived and entirely unaffected). Not worth
			// an extra confirmation block or a new ledger overload to close a sub-day, single-run gap.
			if (isLandingBlock)
			{
				foreach (NonMinerDonationSummary s in netRoot.GetNonMinerAuctionLedger())
				{
					if (s.Status == NonMinerAuctionStatus.NotIntroduced)
					{
						continue;
					}
					if (NetworkRoot.InjectHistoricalSignedTxStatic(FoundersMiningService.SatoshiNodeId, s.NonMinerNodeId, SeedFundingAmountBtc, $"eb1_seed_{TimelineConfig.DevEntryYear}_{s.NonMinerNodeId}"))
					{
						seededNonMiners++;
					}
				}
			}

			// Weighted pick among every currently-powered miner: Satoshi, Hal, each powered cast member,
			// and the ghost-attributed invisible mass (D-EB.9 — its rotating pseudonym, D-14.9).
			var candidates = new List<(string id, double weight)>();
			if (founders.SatoshiPower > 0d) candidates.Add((FoundersMiningService.SatoshiNodeId, founders.SatoshiPower));
			if (founders.HalPower > 0d) candidates.Add((FoundersMiningService.HalNodeId, founders.HalPower));
			foreach (string castId in NetworkPopulationScheduler.PoweredCastIds)
			{
				candidates.Add((castId, NetworkPopulationScheduler.CastPowerEach));
			}
			string ghostId = NetworkPopulationScheduler.CurrentGhostId;
			bool ghostCandidate = NetworkPopulationScheduler.LastInvisiblePower > 0d;
			if (ghostCandidate)
			{
				candidates.Add((ghostId, NetworkPopulationScheduler.LastInvisiblePower));
			}

			string winnerId;
			if (candidates.Count == 0)
			{
				winnerId = FoundersMiningService.SatoshiNodeId; // defensive fallback — should not occur (§ above)
			}
			else
			{
				double totalWeight = 0d;
				foreach ((_, double w) in candidates) totalWeight += w;
				double roll = rng.NextDouble() * totalWeight;
				double cumulative = 0d;
				winnerId = candidates[^1].id;
				foreach ((string id, double weight) in candidates)
				{
					cumulative += weight;
					if (roll < cumulative)
					{
						winnerId = id;
						break;
					}
				}
			}

			bool isGhostWinner = ghostCandidate && winnerId == ghostId;
			if (isGhostWinner)
			{
				netRoot.EnsureGhostNodeRegistered(ghostId);
			}

			if (NetworkRoot.MineNodeStatic(winnerId, ts))
			{
				lastMinedTs = ts;
				if (winnerId == FoundersMiningService.SatoshiNodeId) satoshiBlocks++;
				else if (winnerId == FoundersMiningService.HalNodeId) halBlocks++;
				else if (isGhostWinner) { ghostBlocks++; NetworkPopulationScheduler.AdvanceGhostRotation(); }
				else castBlocks++;

				// Scripted player-era events (the Apr-2009 Hearn round-trip) — called DIRECTLY, bypassing
				// HandleMinedBlock's `!_bulkMining` gate (same pattern as E4's hand-rolled Phase-1 call);
				// OnBlockMined only reads block.Timestamp, so a minimal synthetic Block is sufficient.
				HistoricalEventScheduler.OnBlockMined(new Block { Timestamp = ts });
			}
		}

		string summary = $"Satoshi {satoshiBlocks}, Hal {halBlocks}, cast {castBlocks} ({spawnedCast} spawned), " +
			$"ghost {ghostBlocks}, seeded {seededNonMiners} non-miner(s).";
		return (ts, lastMinedTs, summary);
#pragma warning restore CS0162
	}
}
