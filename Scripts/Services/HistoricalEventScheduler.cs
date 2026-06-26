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
// returns the coin first). E8 (17.49 change) is NOT modelled here — change is implicit in the account
// model and a Satoshi→Satoshi self-send is rejected by the engine; it returns as a real change output
// in Step 8 (UTXO realism).
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

	// The Hearn round-trip, in order (E6 → E6b → E7). Hearn nets +82.51 and signs exactly one tx (E6b).
	private static readonly Step[] Steps =
	{
		// E6  — Satoshi seeds Hearn the test coin so he can "send 32.51 first".
		new(Satoshi, Hearn, 32.51m, HearnDealDateMs, "hist_E6_satoshi_hearn_3251"),
		// E6b — Hearn returns the coin (his single outgoing tx).
		new(Hearn, Satoshi, 32.51m, HearnDealDateMs, "hist_E6b_hearn_satoshi_3251"),
		// E7  — Satoshi returns the coin plus the 50 BTC gift (32.51 + 50 = 82.51).
		new(Satoshi, Hearn, 82.51m, HearnDealDateMs, "hist_E7_satoshi_hearn_8251"),
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
