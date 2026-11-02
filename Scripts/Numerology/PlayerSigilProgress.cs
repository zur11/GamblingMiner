using System;
using System.Text.Json;
using Scripts.Finance;

namespace Scripts.Numerology
{
	// The player's Graphic Numerology collection, in memory (P-GN.4, D-GN.24, D-GN.25, AIHelperFiles/graphic_numerology-plan.md).
	//
	// A static owner rather than an autoload, like NetworkRoot's world state: it has no _Ready and no boot order to keep.
	// BlockSessionCheckpointService pushes into it at boot — the restore after a mined block, or the pre-genesis reset —
	// and captures it at every block. It writes no file of its own, so a block is its only commit (Pattern 2) and
	// WipeWorld and WorldWriteGuard have nothing of it to cover. No Godot here: the harness compiles this file, so the
	// callers log what they do.
	public static class PlayerSigilProgress
	{
		public static SigilCollectionState Collection { get; private set; } = new();

		// The sigil in the slot, null when it is empty. Cached from Collection, refreshed on equip and restore, because
		// every player bet reads it and a bet must not search a list.
		public static SigilProgressState Equipped { get; private set; }

		// What the latest player bet did. "Stop on Sigil Set Completed" reads it right after the bet (D-GN.10).
		public static SigilBetOutcome LastOutcome { get; private set; }

		// Fired when something the sigil overlay draws changed — a hit, a restart, a completed pass — and on an acquisition,
		// a switch or a restore. NOT on every bet, although V moves on every bet: an open overlay samples V itself (§29.13).
		public static event Action<SigilBetOutcome> Changed;

		// Called from UserStatsService.OnBetExecutedRegisterBet, the one sink both player bet paths (DiceGame's manual
		// play, SimulationService's autobet) already pass through — and BEFORE either path's mining attempt, so the bet
		// that mines a block is inside that block's checkpoint.
		public static void RegisterPlayerBet(BetTransactionEvent bet)
		{
			var equipped = Equipped;
			// An empty slot or a complete sigil draws nothing (D-GN.25, D-GN.26), and is no operation on V to show.
			if (equipped == null || equipped.IsComplete)
			{
				LastOutcome = SigilBetOutcome.None;
				return;
			}

			LastOutcome = SigilRules.ApplyBet(equipped, bet.Roll, bet.IsWin, bet.Chance, bet.BetAmount, bet.Timestamp, out SigilStep step);
			_recentSteps[_recentNext] = step;
			_recentNext = (_recentNext + 1) % RecentStepCapacity;
			if (_recentCount < RecentStepCapacity) _recentCount++;

			if (LastOutcome != SigilBetOutcome.None)
				Changed?.Invoke(LastOutcome);
		}

		public static bool IsOwned(int sigilIndex) => Find(sigilIndex) != null;

		// The owned sigil's progress, null if it is not owned. A search: for the collection's screen, never a bet.
		public static SigilProgressState Find(int sigilIndex)
		{
			foreach (var sigil in Collection.Owned)
				if (sigil.SigilIndex == sigilIndex)
					return sigil;
			return null;
		}

		// D-GN.24: one of each, so a second acquisition does nothing and returns false. It does not touch the slot.
		public static bool Acquire(int sigilIndex)
		{
			if (sigilIndex < 0 || sigilIndex >= PlanetarySquares.SigilCount || IsOwned(sigilIndex))
				return false;

			Collection.Owned.Add(SigilRules.NewProgress(sigilIndex));
			Changed?.Invoke(SigilBetOutcome.None);
			return true;
		}

		// D-GN.25: puts an owned sigil in the slot, at any time. The sigil leaving loses the pass in progress and nothing
		// else (SigilRules.LeaveSlot). Returns false, changing nothing, for a sigil not owned or already in the slot.
		public static bool Equip(int sigilIndex)
		{
			var next = Find(sigilIndex);
			if (next == null || next == Equipped)
				return false;

			if (Equipped != null)
				SigilRules.LeaveSlot(Equipped);
			Collection.EquippedSigilIndex = sigilIndex;
			Equipped = next;
			LastOutcome = SigilBetOutcome.None;
			_recentCount = 0;   // the chain ran through the sigil that left; it would not connect to this one's V
			Changed?.Invoke(SigilBetOutcome.None);
			return true;
		}

		// D-GN.21 — the latest bets' operations on V, for the overlay's chain. A fixed ring written in place: O(1) and no
		// allocation per bet. In memory only — a restart starts the chain empty, which is honest: it shows bets, not state.
		public const int RecentStepCapacity = 16;
		private static readonly SigilStep[] _recentSteps = new SigilStep[RecentStepCapacity];
		private static int _recentNext;
		private static int _recentCount;

		// Copies the latest `count` operations into `buffer`, oldest first; returns how many it copied.
		public static int CopyRecentSteps(SigilStep[] buffer, int count)
		{
			int n = Math.Min(Math.Min(count, _recentCount), buffer.Length);
			for (int i = 0; i < n; i++)
				buffer[i] = _recentSteps[(_recentNext - n + i + RecentStepCapacity) % RecentStepCapacity];
			return n;
		}

		// A copy: the live state keeps changing after the block, and the snapshot must describe the block.
		public static SigilCollectionState CaptureForCheckpoint() => Clone(Collection);

		// Null = a checkpoint written before this collection existed: the player holds nothing, so an empty collection is
		// the truth, not a guess (Standing Convention 5's sentinel default; no format bump).
		public static void RestoreFromCheckpoint(SigilCollectionState snapshot)
		{
			Collection = snapshot == null ? new SigilCollectionState() : Clone(snapshot);
			Equipped = Collection.EquippedSigilIndex is int index ? Find(index) : null;
			LastOutcome = SigilBetOutcome.None;
			_recentCount = 0;   // the chain described the world before the restore
			Changed?.Invoke(SigilBetOutcome.None);
		}

		public static void ResetToPreGenesisDefaults() => RestoreFromCheckpoint(null);

		private static SigilCollectionState Clone(SigilCollectionState collection) =>
			JsonSerializer.Deserialize<SigilCollectionState>(JsonSerializer.Serialize(collection));
	}
}
