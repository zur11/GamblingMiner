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
public static class HistoricalBootstrapService
{
	// Player always begins on this calendar day; the exact time-of-day is randomised per run.
	private static readonly DateTime PlayerStartDayLocal = new(2009, 3, 21, 0, 0, 0, DateTimeKind.Local);

	// Hal joins 11 Jan 2009 and mines exactly 3 spaced bootstrap blocks near these dates.
	private static readonly DateTime[] HalBlockDatesLocal =
	{
		new(2009, 1, 12, 0, 0, 0, DateTimeKind.Local),
		new(2009, 2,  5, 0, 0, 0, DateTimeKind.Local),
		new(2009, 3,  5, 0, 0, 0, DateTimeKind.Local),
	};

	// ~16h 40m in-game per block at 100X (≈585 attempts/block × 100 in-game seconds).
	private const long BlockIntervalMs = 58_500_000L;

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

		DateTime landingLocal = PlayerStartDayLocal.AddSeconds(rng.Next(0, 86400));
		long landingMs = new DateTimeOffset(landingLocal).ToUnixTimeMilliseconds();

		var halTargets = new Queue<long>();
		foreach (DateTime d in HalBlockDatesLocal)
		{
			halTargets.Enqueue(new DateTimeOffset(d).ToUnixTimeMilliseconds());
		}

		long ts = BlockchainService.GenesisTimestampUnixMs;
		int satoshiBlocks = 0;
		int halBlocks = 0;

		NetworkRoot.BeginBulkMining();
		try
		{
			while (true)
			{
				// Advance by one block interval with ±30% jitter so timestamps look organic.
				double jitterFactor = 1.0 + (rng.NextDouble() - 0.5) * 0.6; // 0.7 .. 1.3
				ts += (long)(BlockIntervalMs * jitterFactor);
				if (ts >= landingMs)
				{
					break;
				}

				bool halTurn = halTargets.Count > 0 && ts >= halTargets.Peek();
				if (halTurn)
				{
					halTargets.Dequeue();
					if (NetworkRoot.MineNodeStatic("hal", ts))
					{
						halBlocks++;
					}
				}
				else if (NetworkRoot.MineNodeStatic("satoshi", ts))
				{
					satoshiBlocks++;
				}
			}
		}
		finally
		{
			NetworkRoot.EndBulkMiningAndPersist();
		}

		DidRun = true;
		LandingLocalDateTime = landingLocal;
		GD.Print($"[HistoricalBootstrap] First launch — mined genesis → {landingLocal:yyyy-MM-dd HH:mm:ss}. " +
				 $"Satoshi {satoshiBlocks} blocks, Hal {halBlocks} blocks.");
	}
}
