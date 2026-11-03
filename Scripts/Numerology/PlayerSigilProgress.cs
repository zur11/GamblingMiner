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

		// The record a bet draws: the sigil in the slot, or the planetary sigil being activated (check IsPlanetary before
		// reading a sigil's own figures from it); null when the slot is empty. Cached from Collection, refreshed on every
		// change of the slot and on restore, because every player bet reads it and a bet must not search a list.
		public static SigilProgressState Equipped { get; private set; }

		// What the latest player bet did. "Stop on Sigil Set Completed" reads it right after the bet (D-GN.10).
		public static SigilBetOutcome LastOutcome { get; private set; }

		// The record the latest player bet drew. Not always Equipped: the bet that completes a sigil may also switch the slot
		// (D-GN.31), and a log line of that bet must describe the sigil it completed.
		public static SigilProgressState LastDrawn { get; private set; }

		// Fired when something the sigil overlay draws changed — a hit, a restart, a completed pass — and on an acquisition,
		// a switch or a restore. NOT on every bet, although V moves on every bet: an open overlay samples V itself (§29.13).
		public static event Action<SigilBetOutcome> Changed;

		// D-GN.29: the planet whose pair just reached level 1. Fired in the bet, which may run with no scene watching, so a
		// scene entered later reads NoticePlanetAt instead.
		public static event Action<int> NoticeFired;

		// D-GN.31 — what a completion does. A preference, not world state: the overlay pushes it from UserSettingsService, so
		// no restore touches it.
		public static bool AutoSwitchOnCompletion { get; private set; }

		// D-GN.31 — a finished sigil is in the slot, the player has chosen to stop on that, and there is something else to
		// play: bets are refused until the slot changes. Cached on every change of what it reads, since both bet paths read
		// it on every bet. With nothing else to play it stays false: the clock must not freeze.
		public static bool MustChooseSigil { get; private set; }

		// D-GN.31 — a bet would raise no level: what DiceGame's warning says.
		public static bool DrawsNothing => Equipped == null || Equipped.IsComplete;

		private static int _noticePlanet = NoNotice;
		private static DateTime _noticeFiredAtUtc;
		private const int NoNotice = -1;

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
				LastDrawn = null;
				return;
			}

			LastOutcome = SigilRules.ApplyBet(equipped, bet.Roll, bet.IsWin, bet.Chance, bet.BetAmount, bet.Timestamp, out SigilStep step);
			LastDrawn = equipped;
			_recentSteps[_recentNext] = step;
			_recentNext = (_recentNext + 1) % RecentStepCapacity;
			if (_recentCount < RecentStepCapacity) _recentCount++;

			if (LastOutcome == SigilBetOutcome.None)
				return;

			if ((LastOutcome & SigilBetOutcome.SigilLevelAcquired) != 0)
				FireNoticeIfPairReachedLevelOne(equipped, bet.Timestamp);
			if ((LastOutcome & SigilBetOutcome.SigilCompleted) != 0)
				OnCompleted();
			Changed?.Invoke(LastOutcome);
		}

		// D-GN.29 — the lower of the pair has just gone from 0 to 1. A sigil's level rises one at a time, since no pass adds a
		// whole level (D-GN.23), so this bet lifted its sigil to 1 exactly when that sigil now reads 1, and the lower moved only
		// if the partner already held it. No record of having fired is kept, or needed: a level is reached once in a world,
		// and a restart that reverts it reverts this too. An acquisition cannot fire it, not being a bet.
		private static void FireNoticeIfPairReachedLevelOne(SigilProgressState drawn, DateTime timestampUtc)
		{
			if (drawn.IsPlanetary || SigilRules.SigilLevel(drawn) != 1)
				return;
			var partner = Find(SigilRules.PartnerOf(drawn.SigilIndex));
			if (partner == null || SigilRules.SigilLevel(partner) < 1)
				return;

			_noticePlanet = drawn.SigilIndex / 2;
			_noticeFiredAtUtc = timestampUtc;
			NoticeFired?.Invoke(_noticePlanet);
		}

		// The planet whose notice is showing at this game time, -1 when none. A single notice: reaching a second planet's level
		// 1 within 10 game minutes replaces the first. Live until the clock passes the bet's timestamp + NoticeGameSeconds.
		public static int NoticePlanetAt(DateTime nowUtc)
		{
			if (_noticePlanet != NoNotice && !SigilRules.NoticeIsLive(_noticeFiredAtUtc, nowUtc))
				_noticePlanet = NoNotice;
			return _noticePlanet;
		}

		// Keeps this bet's outcome through the switch: Equip clears it, and the bet's own stops read it next.
		private static void OnCompleted()
		{
			var outcome = LastOutcome;
			TryAutoSwitch();
			LastOutcome = outcome;
			RefreshMustChoose();
		}

		// D-GN.31 — takes the least played sigil, if the player asked for that and the slot's sigil is finished. Called on a
		// completion, an acquisition ("cuando lo tenga") and on turning the toggle on: the only things that make it possible.
		private static bool TryAutoSwitch()
		{
			if (!AutoSwitchOnCompletion || Equipped is not { IsComplete: true })
				return false;
			var next = SigilRules.LeastPlayed(Collection);
			return next != null && Equip(next.SigilIndex);
		}

		// Returns whether the slot changed, so that the caller can log it: this file logs nothing.
		public static bool SetAutoSwitchOnCompletion(bool enabled)
		{
			if (enabled == AutoSwitchOnCompletion)
				return false;

			AutoSwitchOnCompletion = enabled;
			bool switched = TryAutoSwitch();
			RefreshMustChoose();
			Changed?.Invoke(SigilBetOutcome.None);
			return switched;
		}

		private static void RefreshMustChoose() =>
			MustChooseSigil = Equipped is { IsComplete: true } && !AutoSwitchOnCompletion && HasSomethingElseToPlay();

		// Anything the player could press Play or Activate on now: an owned incomplete sigil, or an open level. A search, on a
		// change of what it reads, never on an ordinary bet.
		private static bool HasSomethingElseToPlay()
		{
			if (SigilRules.LeastPlayed(Collection) != null)
				return true;
			for (int planet = 0; planet < PlanetarySquares.All.Count; planet++)
				if (NextOpenLevel(planet) > 0)
					return true;
			return false;
		}

		// D-GN.10, D-GN.31, D-GN.32 — the stop this player bet asks for. The session's two flags come from the session, never
		// the panel (D-M2.8), so the caller passes them. A level is reported before a set: on one bet both can hold.
		public static SigilStop StopFor(SigilBetOutcome outcome, bool stopOnLevelAcquired, bool stopOnSetCompleted)
		{
			if (MustChooseSigil)
				return SigilStop.SigilCompleted;
			if (stopOnLevelAcquired && (outcome & SigilBetOutcome.SigilLevelAcquired) != 0)
				return SigilStop.LevelAcquired;
			if (stopOnSetCompleted && (outcome & SigilBetOutcome.SetPassCompleted) != 0)
				return SigilStop.SetCompleted;
			return SigilStop.None;
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

		// The planetary sigil's record, null until it is first activated. A search, as Find.
		public static SigilProgressState FindPlanetary(int planetIndex)
		{
			foreach (var planetary in Collection.Planetary)
				if (planetary.SigilIndex == planetIndex * 2)
					return planetary;
			return null;
		}

		// D-GN.30's gate for this planet: the next level that can be activated, 0 when none is open.
		public static int NextOpenLevel(int planetIndex) =>
			SigilRules.NextOpenLevel(Find(planetIndex * 2), Find(planetIndex * 2 + 1), FindPlanetary(planetIndex));

		// D-GN.24: one of each, so a second acquisition does nothing and returns false. It does not touch the slot, unless a
		// finished sigil is waiting there and the player asked to be moved on (D-GN.31).
		public static bool Acquire(int sigilIndex)
		{
			if (sigilIndex < 0 || sigilIndex >= PlanetarySquares.SigilCount || IsOwned(sigilIndex))
				return false;

			Collection.Owned.Add(SigilRules.NewProgress(sigilIndex));
			TryAutoSwitch();
			RefreshMustChoose();
			Changed?.Invoke(SigilBetOutcome.None);
			return true;
		}

		// D-GN.25: puts an owned sigil in the slot, at any time. The record leaving loses the pass in progress and nothing
		// else (SigilRules.LeaveSlot). Returns false, changing nothing, for a sigil not owned, already in the slot or complete:
		// D-GN.28, a complete sigil is never played again. RestoreFromCheckpoint does not come through here, so one that
		// completed in the slot is still restored to it.
		public static bool Equip(int sigilIndex)
		{
			var next = Find(sigilIndex);
			if (next == null || next == Equipped || next.IsComplete)
				return false;

			EnterSlot(next);
			return true;
		}

		// D-GN.30, OQ-GN.4: starting an activation is a switch like any other. The planetary sigil starts or resumes its own
		// record; its completed passes stayed, and it may be at a limit since raised. Returns false, changing nothing, with
		// the gate shut or this activation already in the slot.
		public static bool StartActivation(int planetIndex)
		{
			if (planetIndex < 0 || planetIndex >= PlanetarySquares.All.Count)
				return false;
			var record = FindPlanetary(planetIndex);
			if ((record != null && record == Equipped) || NextOpenLevel(planetIndex) == 0)
				return false;

			if (record == null)
			{
				record = SigilRules.NewPlanetaryProgress(planetIndex);
				Collection.Planetary.Add(record);
			}

			record.LevelLimit = SigilRules.CombinedLevel(Find(planetIndex * 2), Find(planetIndex * 2 + 1));
			record.CompletedAtUtc = default;
			EnterSlot(record);
			return true;
		}

		private static void EnterSlot(SigilProgressState next)
		{
			if (Equipped != null)
				SigilRules.LeaveSlot(Equipped);
			Collection.EquippedSigilIndex = next.IsPlanetary ? null : next.SigilIndex;
			Collection.EquippedPlanetIndex = next.IsPlanetary ? next.SigilIndex / 2 : null;
			Equipped = next;
			LastOutcome = SigilBetOutcome.None;
			_recentCount = 0;   // the chain ran through the record that left; it would not connect to this one's V
			RefreshMustChoose();
			Changed?.Invoke(SigilBetOutcome.None);
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
		// the truth, not a guess (Standing Convention 5's sentinel default; no format bump). A notice is not restored: it is a
		// notice, not state, and the bet that raises the level fires it again if this reverted that level (D-GN.29).
		public static void RestoreFromCheckpoint(SigilCollectionState snapshot)
		{
			Collection = snapshot == null ? new SigilCollectionState() : Clone(snapshot);
			Equipped = Collection.EquippedPlanetIndex is int planet ? FindPlanetary(planet)
				: Collection.EquippedSigilIndex is int index ? Find(index)
				: null;
			LastOutcome = SigilBetOutcome.None;
			LastDrawn = null;
			_noticePlanet = NoNotice;
			_recentCount = 0;   // the chain described the world before the restore
			RefreshMustChoose();
			Changed?.Invoke(SigilBetOutcome.None);
		}

		public static void ResetToPreGenesisDefaults() => RestoreFromCheckpoint(null);

		private static SigilCollectionState Clone(SigilCollectionState collection) =>
			JsonSerializer.Deserialize<SigilCollectionState>(JsonSerializer.Serialize(collection));
	}
}
