using Godot;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using Scripts.History;
using Scripts.User;
using UI.StatusBar;

public partial class BetsHistoryExplorer : Control
{
	// 260 → 100 (mini-plan 02 §C.6a). The experiment at 50 confirmed the residual frame cost scales with
	// the entry count (retention 50–70% at 260, ~100% at 50) — through the clear-and-refill REBUILD, not
	// the draw, per §38.8a's correction. 100 is the developer's chosen point on that trade, matched to
	// BetHistoryContainer.MaxRecentEntries so this scene never asks for more rows than the containers show.
	private const int MaxPreviewEntries = 100;

	// Sentinel for the "All Bets" entry of the chance-to-win filter (a real chance is 1..95).
	private const int AllChances = -1;

	private Label _selectedTimeLabel;
	private Label _summaryLabel;
	private Label _loaderLabel;
	private ProgressBar _loaderProgress;
	private Button _playPauseButton;
	private Button _speedButton;
	private Button _backToCalendarButton;
	private Button _backToDiceButton;
	private BetHistoryContainer _betHistoryContainer;
	private PreviousWinnerNumbersGrid _previousWinnerNumbersGrid;
	private Control _loaderPanel;
	private Control _contentPanel;

	private CalendarTimeService _calendarTimeService;
	private UserStatsService _userStatsService;
	private SceneManager _sceneManager;
	private DateTime _selectedLocal;
	private bool _liveMode;
	private readonly double[] _speedSteps = { 100d, 200d, 400d, 1000d };

	// Chance-to-win filter. _allRecords is the full chronological history; _sortedRecords is the VIEW the
	// rest of this scene reads (identical instance when the filter is "All Bets", so the unfiltered path
	// costs nothing). Filtering is one O(n) pass done ONLY when the selection changes or new records
	// arrive — never inside the refresh path, which is what Part C spent its effort removing.
	//
	// This makes visible a segmentation the summary already had to do internally: INC-002/§40.8 forced the
	// loss-run metric to be measured per (GameId, Chance), because a run of losses only means something at
	// a fixed win chance. Filtered to one chance, that figure stops needing the caveat.
	private OptionButton _chanceFilterSelector;
	private List<BetRecord> _allRecords = new();
	private int _chanceFilter = AllChances;
	// A chance is OFFERED only from the moment its first bet was placed — this scene is a time-travel
	// replay, so the selector obeys the selected date like everything else on screen. Rewind two days and
	// a chance first played yesterday disappears from the list; replay forward past that instant and it
	// reappears. Showing it earlier would offer a filter that can only ever produce an empty view, and
	// would leak the future into a view of the past.
	private readonly Dictionary<int, DateTime> _chanceFirstSeenUtc = new();
	private readonly List<int> _selectorChanceByIndex = new(); // item index -> chance (AllChances for item 0)
	// Number of chances the selector currently offers, so a per-refresh check for "has the timeline crossed
	// a chance's first bet?" is one integer comparison rather than a rebuild.
	private int _selectorVisibleChanceCount = -1;

	// Floor of the replay window in GAME-LOCAL time — the oldest bet still on disk (§6.6). Selecting a
	// date below it snaps here instead of opening an empty replay, and the header states the floor so the
	// limit is visible before the player hits it rather than after.
	private DateTime? _windowFloorLocal;
	private bool _selectionWasClamped;

	private List<BetRecord> _sortedRecords = new();
	private long _lastRenderedSecond = long.MinValue;
	// Real-time floor between historical-view rebuilds — see the note in _Process.
	//
	// A rebuild is a SPIKE, not steady load: it repopulates two pooled UI containers with up to
	// MaxPreviewEntries rows each (~520 entry updates plus a like number of allocations). At 0.25 s the
	// measured retention swung between 20% on rebuild frames and 98% between them — the average is set by
	// how OFTEN the spike lands, so frequency is the direct lever. 1 Hz is still far more than the eye
	// needs from a history browser, and is independent of the DEV time scale by construction.
	private const double ViewRefreshIntervalSeconds = 1.0;
	private double _viewRefreshTimer;
	// The index of the last record rendered into the preview. When the visible window has not moved there
	// is nothing to rebuild — cheap, and it makes an idle/paused/time-travel view cost nothing at all.
	private int _lastRenderedEndExclusive = -1;
	private int _summaryCursor;
	private int _summaryTotalBets;
	private decimal _summaryMaxBetAmount;
	private decimal _summaryMaxLossAmount;
	// INC-002 / D-16.21 — the streak is measured per (GameId, Chance) SEGMENT, never across the whole
	// history. A run of consecutive losses only means anything at a fixed win chance: concatenating a
	// stretch at 2% onto a stretch at 50% produces a number that describes neither. See §40.8.
	private int _summaryConsecutiveLosses;
	private int _summaryMaxLossRun;
	private int _summaryMaxLossRunChance;
	private string _summarySegmentGameId;
	private int _summarySegmentChance = -1;
	private long _summarySegmentBets;
	private long _summaryMaxLossRunSegmentBets;
	private bool _summaryImplausibleStreakReported;

	public override void _Ready()
	{
		_selectedTimeLabel = GetNode<Label>("%SelectedTimeLabel");
		_summaryLabel = GetNode<Label>("%SummaryLabel");
		_loaderLabel = GetNode<Label>("%LoaderLabel");
		_loaderProgress = GetNode<ProgressBar>("%LoaderProgress");
		_playPauseButton = GetNode<Button>("%PlayPauseButton");
		_speedButton = GetNode<Button>("%SpeedButton");
		_backToCalendarButton = GetNode<Button>("%BackToCalendarButton");
		_backToDiceButton = GetNode<Button>("%BackToDiceButton");
		_betHistoryContainer = GetNode<BetHistoryContainer>("%BetHistoryContainer");
		_previousWinnerNumbersGrid = GetNode<PreviousWinnerNumbersGrid>("%PreviousWinnerNumbersGrid");
		_loaderPanel = GetNode<Control>("%LoaderPanel");
		_contentPanel = GetNode<Control>("%ContentPanel");
		_chanceFilterSelector = GetNode<OptionButton>("%ChanceFilterSelector");

		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_userStatsService = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootVBox");
		var statusBar = new StatusBar();
		rootVBox.AddChild(statusBar);
		rootVBox.MoveChild(statusBar, 0);

		_liveMode = _calendarTimeService?.IsAutobetActive ?? false;
		if (!_liveMode)
		{
			_selectedLocal = _calendarTimeService?.ExplorerSelectedLocalDateTime ?? DateTime.Now;
			_calendarTimeService?.SetLocalDateTime(_selectedLocal);
			if (_calendarTimeService != null)
			{
				bool isPast = _selectedLocal < _calendarTimeService.GamePresentLocalDateTime;
				_calendarTimeService.IsRunning = isPast;
				if (isPast)
					_calendarTimeService.SpeedMultiplier = _speedSteps[0];
			}
		}
		else
		{
			_selectedLocal = _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
			if (_userStatsService != null)
				_userStatsService.StatsChanged += OnLiveStatsChanged;
		}

		_playPauseButton.Pressed += OnPlayPausePressed;
		_speedButton.Pressed += OnSpeedButtonPressed;
		_chanceFilterSelector.ItemSelected += OnChanceFilterSelected;
		_backToCalendarButton.Pressed += OnBackToCalendarPressed;
		_backToDiceButton.Pressed += OnBackToDicePressed;

		RefreshControlLabels();
		_ = LoadHistoricalDataAsync();
	}

	public override void _ExitTree()
	{
		if (_userStatsService != null)
			_userStatsService.StatsChanged -= OnLiveStatsChanged;
	}

	// StatsChanged fires at its own 250 ms throttle — a cadence sized for a cheap UI refresh. This handler
	// used to re-sort and re-materialise the ENTIRE loaded history on every one of them: at the ~105k
	// records of a long run that is an O(n log n) sort plus a fresh 105k-element list, four times a second,
	// on the main thread. Measured cost: sim retention fell from 100% to 15–18% purely by standing in this
	// scene (mini-plan 02 §C.6) — the §38.7 inverse failure, a correct event whose real rate nobody
	// re-checked against the work behind it.
	//
	// New bets are appended in chronological order (game time is non-decreasing, and every bet is
	// registered through OnBetExecutedRegisterBet), so the sorted view can be extended with just the tail
	// instead of rebuilt: O(new bets) per event rather than O(all bets). The full rebuild survives as the
	// fallback for the cases where that premise does not hold — a shorter list (checkpoint rollback), a
	// changed head (history reload), or an out-of-order tail — so a wrong assumption costs a rebuild, never
	// a wrong view.
	private void OnLiveStatsChanged(UserBettingStats _)
	{
		if (_userStatsService?.BetHistory == null) return;

		System.Collections.Generic.IReadOnlyList<BetRecord> source = _userStatsService.BetHistory.Records;
		int knownBefore = _allRecords.Count;

		if (!TryAppendNewRecords(source))
		{
			_allRecords = source.OrderBy(r => r.TimestampUtc).ToList();
			_chanceFirstSeenUtc.Clear();
			RegisterChances(_allRecords);
			ApplyChanceFilter();          // rebuilds the view and invalidates every cached index
			_selectorVisibleChanceCount = -1;  // force the next refresh to rebuild the option list
		}
		else if (_allRecords.Count > knownBefore)
		{
			// Only the newly arrived tail can introduce a chance never seen before — this is how a bet
			// placed at a new chance in DiceGame reaches the selector without rescanning the history. The
			// refresh decides whether it is yet VISIBLE (its first-seen instant vs. the selected date); in
			// live mode that instant is essentially now, so it appears immediately.
			if (RegisterChances(_allRecords.GetRange(knownBefore, _allRecords.Count - knownBefore)))
			{
				_selectorVisibleChanceCount = -1;
			}
		}

		_lastRenderedSecond = long.MinValue;
	}

	// Returns false when the incremental path cannot be trusted and the caller must rebuild.
	// Appends to _allRecords AND, when a filter is active, to the filtered view — keeping the two in step
	// without re-filtering the whole history on every settled bet.
	private bool TryAppendNewRecords(System.Collections.Generic.IReadOnlyList<BetRecord> source)
	{
		int known = _allRecords.Count;
		if (known == 0 || source.Count < known)
		{
			return false;
		}

		// Cheap identity check that the list we grew from is still the same one (a reload replaces it).
		if (!ReferenceEquals(source[0], _allRecords[0]))
		{
			return false;
		}

		if (source.Count == known)
		{
			return true;
		}

		DateTime last = _allRecords[known - 1].TimestampUtc;
		bool filtered = _chanceFilter != AllChances;
		for (int i = known; i < source.Count; i++)
		{
			BetRecord record = source[i];
			if (record.TimestampUtc < last)
			{
				return false; // not append-ordered after all — fall back rather than render a wrong order
			}

			last = record.TimestampUtc;
			_allRecords.Add(record);

			// With no filter the view IS _allRecords (same instance), so appending again would double it.
			if (filtered && record.Chance == _chanceFilter)
			{
				_sortedRecords.Add(record);
			}
		}

		return true;
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		if (_calendarTimeService?.IsRunning == true && !_liveMode)
		{
			DateTime present = _calendarTimeService.GamePresentLocalDateTime;
			if (_calendarTimeService.CurrentLocalDateTime >= present)
			{
				_calendarTimeService.SetLocalDateTime(present);
				_calendarTimeService.IsRunning = false;
				RefreshControlLabels();
			}
		}

		DateTime current = GetCurrentLocal();
		_selectedTimeLabel.Text = $"Selected timeline: {current:yyyy-MM-dd HH:mm:ss}{BuildWindowSuffix()}";

		if (_sortedRecords.Count <= 0)
		{
			return;
		}

		// The rebuild below repopulates TWO UI containers with up to MaxPreviewEntries rows each and
		// re-walks the summary cursor. Its cadence used to be "whenever the GAME second changes", which is
		// a cadence the DEV time scale multiplies: at 9000X the game second changes every frame, so this
		// ran ~520 entry updates per frame. A refresh cadence must be denominated in REAL time — game time
		// is a quantity the player can accelerate by 90×, and any per-game-second budget accelerates with
		// it. Both guards are kept: the timer bounds how often, the second-changed test avoids redundant
		// identical rebuilds when the clock is slow or stopped.
		_viewRefreshTimer += delta;
		if (_viewRefreshTimer < ViewRefreshIntervalSeconds)
		{
			return;
		}

		long currentSecond = new DateTimeOffset(current).ToUnixTimeSeconds();
		if (currentSecond == _lastRenderedSecond)
		{
			return;
		}

		_viewRefreshTimer = 0d;
		_lastRenderedSecond = currentSecond;
		RefreshHistoricalViewForCurrentTime(current.ToUniversalTime());
	}

	private async System.Threading.Tasks.Task LoadHistoricalDataAsync()
	{
		_loaderPanel.Visible = true;
		_contentPanel.Visible = false;
		_loaderProgress.Value = 5;
		_loaderLabel.Text = "Loading nearest historical window...";

		if (_userStatsService?.BetHistory == null)
		{
			_summaryLabel.Text = "History unavailable.";
			_loaderPanel.Visible = false;
			_contentPanel.Visible = true;
			return;
		}

		_userStatsService.EnsureFullHistoryLoaded();
		_allRecords = _userStatsService.BetHistory.Records
			.OrderBy(r => r.TimestampUtc)
			.ToList();
		_loaderProgress.Value = 35;

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		_loaderLabel.Text = "Computing full summaries...";
		_loaderProgress.Value = 70;
		ApplyReplayWindowFloor();
		_chanceFirstSeenUtc.Clear();
		RegisterChances(_allRecords);
		ApplyChanceFilter();   // resets the summary + render caches; defaults to All Bets
		// The selector is built by the refresh below, from the selected date — not from the whole history.
		_selectorVisibleChanceCount = -1;
		RefreshHistoricalViewForCurrentTime(GetCurrentLocal().ToUniversalTime(), forceRebuild: true);

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		_loaderProgress.Value = 100;
		_loaderLabel.Text = "History ready";
		_loaderPanel.Visible = false;
		_contentPanel.Visible = true;
	}

	private void RefreshHistoricalViewForCurrentTime(DateTime currentUtc, bool forceRebuild = false)
	{
		// Has the replay crossed the first bet of a chance not yet offered (or rewound past one)? One int
		// comparison per refresh; the rebuild only runs on the crossing itself. This sits BEFORE the
		// empty-view return so the selector still updates while the filtered view has nothing to show.
		if (CountVisibleChances(currentUtc) != _selectorVisibleChanceCount)
		{
			RebuildChanceSelector(currentUtc);
		}

		if (_sortedRecords.Count <= 0)
		{
			_summaryLabel.Text = _chanceFilter == AllChances
				? "No bets available up to selected date."
				: string.Format(CultureInfo.InvariantCulture,
					"No bets at {0}% chance up to selected date.", _chanceFilter);
			_betHistoryContainer.ClearEntries();
			_previousWinnerNumbersGrid.ClearEntries();
			return;
		}

		int endExclusive = UpperBound(_sortedRecords, currentUtc);

		// The summary still advances (its cursor is incremental and cheap), but repopulating the two
		// containers with the identical window is pure waste — and it is the expensive half.
		if (forceRebuild || endExclusive != _lastRenderedEndExclusive)
		{
			_lastRenderedEndExclusive = endExclusive;
			int start = Math.Max(0, endExclusive - MaxPreviewEntries);
			List<BetRecord> preview = _sortedRecords.GetRange(start, endExclusive - start);
			_previousWinnerNumbersGrid.LoadFromHistoricalRecords(preview);
			_betHistoryContainer.LoadFromHistoricalRecords(preview);
		}

		AdvanceSummaryTo(endExclusive, forceRebuild);
		// With a chance filter active every record shares that chance, so the "(at N% chance)" qualifier the
		// unfiltered figure needs (§40.8: a loss run only means something at a fixed chance) is redundant —
		// the scope line already says it once, at the front.
		string streak = _summaryMaxLossRun > 0
			? (_chanceFilter == AllChances
				? string.Format(CultureInfo.InvariantCulture, "{0} (at {1}% chance)", _summaryMaxLossRun, _summaryMaxLossRunChance)
				: _summaryMaxLossRun.ToString(CultureInfo.InvariantCulture))
			: "0";
		string scope = _chanceFilter == AllChances
			? "All bets"
			: string.Format(CultureInfo.InvariantCulture, "Chance {0}%", _chanceFilter);
		_summaryLabel.Text = string.Format(
			CultureInfo.InvariantCulture,
			"{0} — up to selected date: {1} | Max bet amount: {2:F8} SC | Max loss amount: {3:F8} SC | Max consecutive losses: {4}",
			scope,
			_summaryTotalBets,
			_summaryMaxBetAmount,
			_summaryMaxLossAmount,
			streak
		);
	}

	// ── Replay window ───────────────────────────────────────────────────────────

	// Establishes the window floor from the loaded history and snaps the selection up to it if the player
	// asked for an earlier date. Runs once per load, before the first render, so the very first frame is
	// already inside the window — a clamp applied later would flash an empty replay first.
	private void ApplyReplayWindowFloor()
	{
		_windowFloorLocal = null;
		_selectionWasClamped = false;

		if (_allRecords.Count <= 0)
		{
			return;
		}

		DateTime floorLocal = _allRecords[0].TimestampUtc.ToLocalTime();
		_windowFloorLocal = floorLocal;

		if (_selectedLocal >= floorLocal)
		{
			return;
		}

		// Below the floor: snap to the oldest bet we still hold. The calendar is moved with it, so the
		// clock, this scene and whatever the player picks next all agree — leaving them disagreeing is how
		// a "date I chose" quietly stops matching the history being shown.
		_selectedLocal = floorLocal;
		_selectionWasClamped = true;
		_calendarTimeService?.SetLocalDateTime(floorLocal);
		_calendarTimeService?.SetExplorerSelectedLocalDateTime(floorLocal);
	}

	private string BuildWindowSuffix()
	{
		if (_windowFloorLocal == null)
		{
			return string.Empty;
		}

		string floor = _windowFloorLocal.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		return _selectionWasClamped
			? $"   |   ⟵ snapped to the oldest stored bet ({floor})"
			: $"   |   History stored from: {floor}";
	}

	// ── Chance-to-win filter ────────────────────────────────────────────────────

	// Rebuilds the dropdown from the chances actually PRESENT in the loaded history: an option the player
	// has never played would filter to an empty view, and one they have played must always be reachable.
	// A chance appears as soon as a bet at that chance is in the loaded history; surviving a restart is
	// the existing checkpoint rule's business (the journal rolls back to the last mined block), not
	// something this selector needs to enforce.
	// How many chances have been played at or before `currentUtc`. Cheap by construction: a dice chance is
	// 1..95, so this dictionary can never hold more than 95 entries.
	private int CountVisibleChances(DateTime currentUtc)
	{
		int count = 0;
		foreach (KeyValuePair<int, DateTime> entry in _chanceFirstSeenUtc)
		{
			if (entry.Value <= currentUtc)
			{
				count++;
			}
		}

		return count;
	}

	private void RebuildChanceSelector(DateTime currentUtc)
	{
		if (_chanceFilterSelector == null)
		{
			return;
		}

		List<int> visible = _chanceFirstSeenUtc
			.Where(e => e.Value <= currentUtc)
			.Select(e => e.Key)
			.OrderBy(c => c)          // ascending by chance reads better than by first-played order
			.ToList();

		_chanceFilterSelector.Clear();
		_selectorChanceByIndex.Clear();

		// No counts on the items: the summary line counts bets "up to the selected date", and a total-history
		// count sitting beside it would read as the same quantity while disagreeing with it.
		_chanceFilterSelector.AddItem("All Bets");
		_selectorChanceByIndex.Add(AllChances);

		foreach (int chance in visible)
		{
			_chanceFilterSelector.AddItem($"{chance}%");
			_selectorChanceByIndex.Add(chance);
		}

		_selectorVisibleChanceCount = visible.Count;

		int index = _selectorChanceByIndex.IndexOf(_chanceFilter);
		if (index < 0)
		{
			// The selected chance is no longer offered — either the timeline rewound past its first bet, or
			// a reload dropped it. Fall back to All Bets rather than leaving the view filtered by something
			// the player can no longer see or re-select.
			_chanceFilter = AllChances;
			index = 0;
			ApplyChanceFilter();
		}

		_chanceFilterSelector.Select(index);
	}

	// Records the FIRST time each chance was played. Callers feed chronologically-ordered records, so the
	// first occurrence encountered is the earliest — hence the plain "add if absent". Returns true when a
	// chance was seen for the first time, i.e. the option list may need rebuilding.
	private bool RegisterChances(IEnumerable<BetRecord> records)
	{
		bool added = false;
		foreach (BetRecord record in records)
		{
			if (_chanceFirstSeenUtc.ContainsKey(record.Chance))
			{
				continue;
			}

			_chanceFirstSeenUtc[record.Chance] = record.TimestampUtc;
			added = true;
		}

		return added;
	}

	// The ONE place the filtered view is materialised. O(n), and deliberately called only on a selection
	// change or a full history rebuild — never from _Process. With no filter the view IS the full list (same
	// instance), so the default path allocates nothing and behaves exactly as before this feature existed.
	private void ApplyChanceFilter()
	{
		_sortedRecords = _chanceFilter == AllChances
			? _allRecords
			: _allRecords.Where(r => r.Chance == _chanceFilter).ToList();

		// The view changed underneath every cached index, so invalidate both caches: the summary cursor
		// (which counts through the view) and the rendered-window guard.
		_summaryCursor = 0;
		_summaryTotalBets = 0;
		_summaryMaxBetAmount = 0m;
		_summaryMaxLossAmount = 0m;
		ResetStreakSummary();
		_lastRenderedEndExclusive = -1;
		_lastRenderedSecond = long.MinValue;
	}

	private void OnChanceFilterSelected(long index)
	{
		if (index < 0 || index >= _selectorChanceByIndex.Count)
		{
			return;
		}

		int chance = _selectorChanceByIndex[(int)index];
		if (chance == _chanceFilter)
		{
			return;
		}

		_chanceFilter = chance;
		ApplyChanceFilter();
		RefreshHistoricalViewForCurrentTime(GetCurrentLocal().ToUniversalTime(), forceRebuild: true);
	}

	private void ResetStreakSummary()
	{
		_summaryConsecutiveLosses = 0;
		_summaryMaxLossRun = 0;
		_summaryMaxLossRunChance = 0;
		_summarySegmentGameId = null;
		_summarySegmentChance = -1;
		_summarySegmentBets = 0;
		_summaryMaxLossRunSegmentBets = 0;
		_summaryImplausibleStreakReported = false;
	}

	// INC-002 — this reports the longest run of consecutive LOSSES, which is what it always computed; the
	// old "Martingale level reached" label was a second defect on top of the inflated number, because a
	// progression resets to base bet on InsistAfterStopOnLoss, on the bankroll-limit reset and on every
	// auto-recharge, while the loss run keeps counting straight through all three. It is also no longer
	// counted across a change of game or win chance, and the closing win is no longer added to the run
	// (the old code did that on a win but not on a trailing loss, so the same streak reported two values
	// depending on where the view happened to end).
	private void AdvanceSummaryTo(int endExclusive, bool forceRebuild)
	{
		if (forceRebuild || endExclusive < _summaryCursor)
		{
			_summaryCursor = 0;
			_summaryTotalBets = 0;
			_summaryMaxBetAmount = 0m;
			_summaryMaxLossAmount = 0m;
			ResetStreakSummary();
		}

		for (int i = _summaryCursor; i < endExclusive; i++)
		{
			BetRecord record = _sortedRecords[i];
			_summaryTotalBets++;
			if (record.BetAmount > _summaryMaxBetAmount)
			{
				_summaryMaxBetAmount = record.BetAmount;
			}

			if (record.NetAmount < 0m)
			{
				decimal absLoss = Math.Abs(record.NetAmount);
				if (absLoss > _summaryMaxLossAmount)
				{
					_summaryMaxLossAmount = absLoss;
				}
			}

			// A new segment starts wherever the game or the win chance changes: whatever the player was
			// doing before is a different experiment, and its losing run does not continue into this one.
			if (record.Chance != _summarySegmentChance ||
				!string.Equals(record.GameId, _summarySegmentGameId, StringComparison.Ordinal))
			{
				_summarySegmentChance = record.Chance;
				_summarySegmentGameId = record.GameId;
				_summarySegmentBets = 0;
				_summaryConsecutiveLosses = 0;
			}

			_summarySegmentBets++;

			if (record.Outcome == BetOutcome.Loss)
			{
				_summaryConsecutiveLosses++;
				if (_summaryConsecutiveLosses > _summaryMaxLossRun)
				{
					_summaryMaxLossRun = _summaryConsecutiveLosses;
					_summaryMaxLossRunChance = _summarySegmentChance;
					_summaryMaxLossRunSegmentBets = _summarySegmentBets;
				}

				continue;
			}

			_summaryConsecutiveLosses = 0;
		}

		_summaryCursor = endExclusive;
		AssertLossRunIsPlausible();
	}

	// INC-002 / §39.16 rule 1 — the figure went wrong for an unknown number of sessions precisely because
	// nothing ever checked it against what the dice can actually produce. For n bets at loss probability p
	// the longest run is ~log(n)/log(1/p); exceeding that by 12 has probability ~2^-12, so a hit is a data
	// fault (duplicated records, a mixed-up segment), not a bad night. Cheap and once per rebuild.
	[System.Diagnostics.Conditional("DEBUG")]
	private void AssertLossRunIsPlausible()
	{
		if (_summaryImplausibleStreakReported || _summaryMaxLossRun <= 0 || _summaryMaxLossRunSegmentBets <= 1)
		{
			return;
		}

		if (_summaryMaxLossRunChance <= 0 || _summaryMaxLossRunChance >= 100)
		{
			return;
		}

		double lossProbability = 1d - (_summaryMaxLossRunChance / 100d);
		double expected = Math.Log(_summaryMaxLossRunSegmentBets) / Math.Log(1d / lossProbability);
		double bound = expected + 12d;
		if (_summaryMaxLossRun <= bound)
		{
			return;
		}

		_summaryImplausibleStreakReported = true;
		GD.PrintErr(string.Format(
			CultureInfo.InvariantCulture,
			"[BetsHistory] Implausible loss run: {0} consecutive losses at {1}% chance over {2} bets in that " +
			"segment (expected ~{3:F1}, alarm above {4:F1}). The bet history is almost certainly carrying " +
			"duplicated records — see ProjectDesignManual §40.8 / INCIDENT_LOG INC-002.",
			_summaryMaxLossRun, _summaryMaxLossRunChance, _summaryMaxLossRunSegmentBets, expected, bound));
	}

	private static int UpperBound(List<BetRecord> records, DateTime targetUtc)
	{
		int lo = 0;
		int hi = records.Count;
		while (lo < hi)
		{
			int mid = lo + ((hi - lo) / 2);
			if (records[mid].TimestampUtc <= targetUtc)
			{
				lo = mid + 1;
			}
			else
			{
				hi = mid;
			}
		}

		return lo;
	}

	private void OnPlayPausePressed()
	{
		if (_calendarTimeService == null || _liveMode)
			return;

		_calendarTimeService.IsRunning = !_calendarTimeService.IsRunning;
		RefreshControlLabels();
	}

	private void OnSpeedButtonPressed()
	{
		if (_calendarTimeService == null)
			return;

		if (_liveMode)
		{
			_calendarTimeService.SpeedMultiplier = _speedSteps[0];
			RefreshControlLabels();
			return;
		}

		double current = _calendarTimeService.SpeedMultiplier;
		int idx = Array.FindIndex(_speedSteps, s => Math.Abs(s - current) < 0.001d);
		idx = idx < 0 ? 0 : (idx + 1) % _speedSteps.Length;
		_calendarTimeService.SpeedMultiplier = _speedSteps[idx];
		RefreshControlLabels();
	}

	// The background sim is an autoload and survives scene changes → always navigate normally. In live mode
	// the clock is owned by the running autobet, so we don't touch it; only the time-travel (non-live)
	// browsing resets the clock before leaving.
	// SF.4.2: origin-aware back — BetsHistoryExplorer is now reachable from more than one hub (CalendarsNavigator
	// AND ScFinances), so return to whichever scene launched it (SceneManager.PreviousScene), falling back to
	// Main Menu if that memory is empty (e.g. deep-linked or first navigation).
	private void OnBackToCalendarPressed()
	{
		if (!_liveMode && _calendarTimeService != null)
			_calendarTimeService.IsRunning = false;
		SceneManager.SceneId target = _sceneManager?.PreviousScene ?? SceneManager.SceneId.MainMenu;
		_sceneManager?.Go(target);
	}

	private void OnBackToDicePressed()
	{
		if (!_liveMode && _calendarTimeService != null)
		{
			_calendarTimeService.IsRunning = false;
			_calendarTimeService.SetNow();
		}
		_sceneManager?.Go(SceneManager.SceneId.DiceGame);
	}

	private const double GameBaseSpeed = 100.0;

	private void RefreshControlLabels()
	{
		if (_liveMode)
		{
			_playPauseButton.Text = "Live";
			_speedButton.Text = "1x (Live)";
			return;
		}
		bool running = _calendarTimeService?.IsRunning ?? true;
		double speed = _calendarTimeService?.SpeedMultiplier ?? GameBaseSpeed;
		double speedX = speed / GameBaseSpeed;
		_playPauseButton.Text = running ? "Pause" : "Play";
		_speedButton.Text = string.Create(CultureInfo.InvariantCulture, $"Speed {speedX:0.##}x");
	}

	private DateTime GetCurrentLocal()
	{
		return _calendarTimeService?.CurrentLocalDateTime ?? _selectedLocal;
	}
}
