using Godot;
using System;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
#nullable enable

// Step 7.4 — scripted player-era historical transactions.
//
// Some famous early-Bitcoin events fall AFTER the 21 Mar 2009 player start, when in-game time is driven
// by the player's bets. This scheduler injects their real signed transactions when the game clock (the
// mined block's in-game timestamp) crosses their date during normal play. Hooked from
// NetworkRoot.HandleMinedBlock, beside ScheduleBotTransactionsAfterBlock.
//
// All state is DERIVED FROM THE CHAIN (each step's deterministic-salt txid is checked for confirmation),
// never a side flag file — so it survives the revert-to-last-block model. Steps run strictly IN ORDER:
// a step is only attempted once the previous one is confirmed on-chain, and injection is idempotent.
//
// v1 roster event: the famous April 2009 Satoshi ↔ Mike Hearn 32.51 round-trip (Q-N1, literal: Hearn
// returns the coin first). Step 8.3 makes it UTXO-faithful: E6 spends a 50-BTC coinbase and leaves a real
// 17.49 change output to a fresh Satoshi address (E8); Satoshi receives E6b at a fresh address; and the
// 82.51 return is split into E7a (32.51, the returned coin) + E7b (50.00, the gift) — see InjectHistoricalSignedTxStatic.
public static class HistoricalEventScheduler
{
	private const string Satoshi = "satoshi";
	private const string Hearn = "mike_hearn";

	// All ~18 Apr 2009. The later steps also gate on the previous step being confirmed, so the shared
	// date is fine — they fan out across consecutive blocks once the date is crossed.
	private static readonly long HearnDealDateMs =
		new DateTimeOffset(new DateTime(2009, 4, 18, 0, 0, 0, DateTimeKind.Local)).ToUnixTimeMilliseconds();

	// One scripted transfer. `Salt` makes the content-hash txid reproducible → idempotent + chain-checkable.
	private sealed record Step(string FromNodeId, string ToNodeId, decimal Amount, long DateMs, string Salt);

	// The Hearn round-trip, in order (E6 → E6b → E7a → E7b). Hearn nets +82.51 and signs exactly one tx (E6b).
	// E7 is split into two single-input sends (Step 8.3 decision): under UTXO-lite no single 50-BTC coinbase
	// can fund 82.51, and this restores the historically-accurate amounts (E6=32.51 test, E7=50.00 gift).
	private static readonly Step[] Steps =
	{
		// E6  — Satoshi seeds Hearn the test coin so he can "send 32.51 first". Spends a 50-BTC coinbase,
		//       leaving 17.49 change to a fresh Satoshi address (E8).
		new(Satoshi, Hearn, 32.51m, HearnDealDateMs, "hist_E6_satoshi_hearn_3251"),
		// E6b — Hearn returns the coin (his single outgoing tx) to a fresh Satoshi address.
		new(Hearn, Satoshi, 32.51m, HearnDealDateMs, "hist_E6b_hearn_satoshi_3251"),
		// E7a — Satoshi returns the coin, spending exactly the 32.51 Hearn just sent (no change).
		new(Satoshi, Hearn, 32.51m, HearnDealDateMs, "hist_E7a_satoshi_hearn_3251"),
		// E7b — Satoshi adds the 50 BTC gift, spending one 50-BTC coinbase (no change).
		new(Satoshi, Hearn, 50.00m, HearnDealDateMs, "hist_E7b_satoshi_hearn_5000"),
	};

	// Called after every live (non-bootstrap) mined block. Advances at most one step per block: it finds
	// the first step not yet confirmed on-chain and, if its date has passed, injects it (idempotent if it
	// is already pending). The next step waits until this one is mined.
	public static void OnBlockMined(Block block)
	{
		long nowMs = block.Timestamp;

		foreach (Step step in Steps)
		{
			if (NetworkRoot.IsHistoricalTxConfirmedStatic(step.FromNodeId, step.ToNodeId, step.Amount, step.Salt))
			{
				continue; // this step is on-chain — move to the next
			}

			// First unconfirmed step. Not yet its date → nothing to do this block.
			if (nowMs < step.DateMs)
			{
				return;
			}

			// Inject (no-op if already pending, or if the sender can't afford it yet — retried next block).
			bool ok = NetworkRoot.InjectHistoricalSignedTxStatic(step.FromNodeId, step.ToNodeId, step.Amount, step.Salt);
			if (ok)
			{
				GD.Print($"[HistoricalEvents] queued {step.FromNodeId} → {step.ToNodeId} {step.Amount} BTC ({step.Salt}).");
			}
			return; // one step at a time; the next waits for this to confirm
		}
	}
}
