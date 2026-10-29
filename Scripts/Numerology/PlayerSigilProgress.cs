using System;
using System.Text.Json;
using Scripts.Finance;

namespace Scripts.Numerology
{
	// The player's Graphic Numerology progress, in memory (P-GN.4, AIHelperFiles/graphic_numerology-plan.md §8).
	//
	// A static owner rather than an autoload, like NetworkRoot's world state: it has no _Ready and no boot order to keep.
	// BlockSessionCheckpointService pushes into it at boot — the restore after a mined block, or the pre-genesis reset —
	// and captures it at every block. It writes no file of its own, so a block is its only commit (Pattern 2) and
	// WipeWorld and WorldWriteGuard have nothing of it to cover.
	public static class PlayerSigilProgress
	{
		public static SigilProgressState State { get; private set; } = SigilRules.NewProgress();

		// What the latest player bet did. "Stop on Sigil Set Completed" reads it right after the bet (D-GN.10).
		public static SigilBetOutcome LastOutcome { get; private set; }

		// Fired when a bet changed what the sigil overlay draws — a hit, a restart, a completed pass — and on a restore.
		// NOT on every bet, although V moves on every bet: an open overlay samples V itself (§29.13).
		public static event Action<SigilBetOutcome> Changed;

		// Called from UserStatsService.OnBetExecutedRegisterBet, the one sink both player bet paths (DiceGame's manual
		// play, SimulationService's autobet) already pass through — and BEFORE either path's mining attempt, so the bet
		// that mines a block is inside that block's checkpoint.
		public static void RegisterPlayerBet(BetTransactionEvent bet)
		{
			LastOutcome = SigilRules.ApplyBet(State, bet.Roll, bet.IsWin, bet.Chance, bet.BetAmount, bet.Timestamp);
			if (LastOutcome != SigilBetOutcome.None)
				Changed?.Invoke(LastOutcome);
		}

		// A copy: the live state keeps changing after the block, and the snapshot must describe the block.
		public static SigilProgressState CaptureForCheckpoint() => Clone(State);

		// Null = a checkpoint written before Graphic Numerology existed: the player has drawn nothing, so fresh progress
		// is the truth, not a guess (Standing Convention 5's sentinel default; no format bump).
		public static void RestoreFromCheckpoint(SigilProgressState snapshot)
		{
			State = snapshot == null ? SigilRules.NewProgress() : Clone(snapshot);
			LastOutcome = SigilBetOutcome.None;
			Changed?.Invoke(SigilBetOutcome.None);
		}

		public static void ResetToPreGenesisDefaults() => RestoreFromCheckpoint(null);

		private static SigilProgressState Clone(SigilProgressState state) =>
			JsonSerializer.Deserialize<SigilProgressState>(JsonSerializer.Serialize(state));
	}
}
