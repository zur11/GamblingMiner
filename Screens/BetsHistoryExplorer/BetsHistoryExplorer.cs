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
	private Button _goLiveButton;
	private bool _goLiveVisible;
	private bool _goLiveVisibilityApplied;
	private bool _transportInert;
	private bool _transportStateApplied;
	private Button _backToCalendarButton;
	private Button _backToDiceButton;
	private BetHistoryContainer _betHistoryContainer;
	private PreviousWinnerNumbersGrid _previousWinnerNumbersGrid;
	private Control _loaderPanel;
	private Control _contentPanel;

	private CalendarTimeService _calendarTimeService;
	private UserStatsService _userStatsService;
	private SceneManager _sceneManager;
	// ── The replay cursor (mini-plan 03 §9) ─────────────────────────────────────
	// This scene used to REWIND THE WORLD CLOCK to browse history. There is only one clock and the
	// simulation advances it as the authoritative present, so borrowing it meant bets settled after a
	// rewind were journaled with timestamps in the PAST — corrupting the chronological order the journal,
	// the rollup's run counters and every UpperBound seek all depend on (§6.13).
	//
	// The cursor is this scene's own. Nothing here writes to CalendarTimeService any more: only the owner
	// of the timeline may move it (the simulation, and the checkpoint restore correcting it).
	// Same violet the StatusBar clock used, now marking the CURSOR rather than the world clock.
	private static readonly Color ReplayCursorColor = new(0.72f, 0.45f, 0.95f);
	private bool _labelShowsReplay;
	private bool _labelColorApplied;

	private DateTime _selectedLocal;   // THE CURSOR: the instant being replayed
	private bool _cursorRunning;       // Play/Pause, driving the cursor rather than the clock
	private double _cursorSpeed = 100d; // game-seconds per real second, the old _speedSteps scale
	// Live-follow: the cursor tracks the present each frame instead of advancing on its own.
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

	// ── The pruned prefix (mini-plan 03 §6.8) ───────────────────────────────────
	// Retention deletes the OLDEST chunks, so a scan of what remains under-reports every lifetime figure —
	// this world is already 10,000 bets short that way. Deletion must be invisible in the NUMBERS; the only
	// thing the player should notice is that the replay cannot go back past the window floor.
	//
	// The correction is exact rather than an estimate, and it rests on two facts holding together: every
	// pruned bet is older than the floor, and the selection is CLAMPED to the floor. So for any date the
	// player can actually select, the entire pruned contribution belongs in the total — there is no partial
	// case to get wrong. Displayed = pruned prefix + scan up to the selected date.
	//
	// prefix = rollup lifetime − the retained window's own totals, computed once per load while both are in
	// hand. Held per segment so the chance filter stays truthful too: a filtered view needs THAT chance's
	// pruned share, which no grand total can supply.
	private sealed class PrefixTotals
	{
		public int Bets;
		public int Wins;
		public decimal Wagered;
		public decimal NetProfit;
		public decimal MaxBetAmount;
		public decimal MaxLossAmount;
		public decimal MaxWonAmount;
		public int MaxConsecutiveLosses;
		public int MaxConsecutiveWins;
	}

	private readonly Dictionary<int, PrefixTotals> _prunedPrefixByChance = new();
	private PrefixTotals _prunedPrefixAll = new();

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
	// The win-side mirrors (mini-plan 03 §6.3/§6.7). Same segmentation rule for the same reason: a winning
	// run at 2% chance and one at 50% are not the same event, so they may not be concatenated either.
	private decimal _summaryMaxWonAmount;
	private int _summaryConsecutiveWins;
	private int _summaryMaxWinRun;
	private int _summaryMaxWinRunChance;
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
		_goLiveButton = GetNode<Button>("%GoLiveButton");
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

		// The cursor opens where the calendar (or DiceGame, or the checkpoint) last pointed it. Entering
		// live-follow is now the player's explicit choice via the Live button, not an automatic
		// consequence of an autobet running — browsing history during a run is exactly what §9 makes safe.
		_selectedLocal = _calendarTimeService?.ExplorerSelectedLocalDateTime ?? DateTime.Now;
		_liveMode = false;
		_cursorSpeed = _speedSteps[0];
		// Auto-play only when there is past to replay; sitting at the present, there is nothing to advance
		// through and a running cursor would just butt against the clamp.
		_cursorRunning = _selectedLocal < PresentLocal();

		// Subscribed unconditionally now: the player may enter live-follow at any time, so the handler that
		// keeps the record list current must already be attached when they do.
		if (_userStatsService != null)
			_userStatsService.StatsChanged += OnLiveStatsChanged;

		_playPauseButton.Pressed += OnPlayPausePressed;
		_speedButton.Pressed += OnSpeedButtonPressed;
		_goLiveButton.Pressed += OnGoLivePressed;
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

		AdvanceCursor(delta);
		RefreshGoLiveVisibility();

		DateTime current = GetCurrentLocal();
		_selectedTimeLabel.Text = $"Selected timeline: {current:yyyy-MM-dd HH:mm:ss}{BuildWindowSuffix()}";

		// §9.2 step 6 — the violet moves HERE, to the cursor that is actually in the past. The StatusBar
		// clock keeps its own tint as a TRIPWIRE: after this phase the world clock is never rewound, so if
		// that one ever turns violet it has caught a real regression rather than reported a mode.
		bool replaying = current < PresentLocal();
		if (replaying != _labelShowsReplay || !_labelColorApplied)
		{
			_labelShowsReplay = replaying;
			_labelColorApplied = true;
			_selectedTimeLabel.AddThemeColorOverride("font_color", replaying ? ReplayCursorColor : Colors.White);
		}

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
		ComputePrunedPrefix();
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
		// Fold in what the deleted chunks held. Every pruned bet predates the window floor and the selection
		// is clamped to that floor, so the whole prefix belongs in every total the player can ask for.
		PrefixTotals prefix = ActivePrefix();
		int shownBets = prefix.Bets + _summaryTotalBets;
		decimal shownMaxBet = Math.Max(prefix.MaxBetAmount, _summaryMaxBetAmount);
		decimal shownMaxLoss = Math.Max(prefix.MaxLossAmount, _summaryMaxLossAmount);
		decimal shownMaxWon = Math.Max(prefix.MaxWonAmount, _summaryMaxWonAmount);

		string lossStreak = FormatRun(
			Math.Max(prefix.MaxConsecutiveLosses, _summaryMaxLossRun),
			prefix.MaxConsecutiveLosses > _summaryMaxLossRun ? _chanceFilter : _summaryMaxLossRunChance);
		string winStreak = FormatRun(
			Math.Max(prefix.MaxConsecutiveWins, _summaryMaxWinRun),
			prefix.MaxConsecutiveWins > _summaryMaxWinRun ? _chanceFilter : _summaryMaxWinRunChance);
		string scope = _chanceFilter == AllChances
			? "All bets"
			: string.Format(CultureInfo.InvariantCulture, "Chance {0}%", _chanceFilter);
		// Losses and wins are stated as PAIRS. The loss figures stood alone for a long time and read as a
		// verdict on the engine; beside their mirrors they read as what they are — the two tails of the
		// same distribution (§40.9).
		_summaryLabel.Text = string.Format(
			CultureInfo.InvariantCulture,
			"{0} — up to selected date: {1} | Max bet: {2:F8} SC | Max loss / won: {3:F8} / {4:F8} SC | " +
			"Max consecutive losses / wins: {5} / {6}",
			scope,
			shownBets,
			shownMaxBet,
			shownMaxLoss,
			shownMaxWon,
			lossStreak,
			winStreak
		);
	}

	// Walks the retained window once and subtracts it from the rollup's lifetime figures. What is left is
	// what the deleted chunks held. Runs per load, alongside the sort that is already O(n).
	private void ComputePrunedPrefix()
	{
		_prunedPrefixByChance.Clear();
		_prunedPrefixAll = new PrefixTotals();

		BetStatsRollup rollup = _userStatsService?.Rollup;
		if (rollup == null || _allRecords.Count == 0)
		{
			return;
		}

		// Retained window, per segment — the same walk the summary does, but over the WHOLE window rather
		// than up to a date, because the prefix is a property of the window and not of the selection.
		var retained = new Dictionary<int, PrefixTotals>();
		var runState = new Dictionary<int, (int Loss, int Win)>();
		int lastChance = int.MinValue;
		foreach (BetRecord r in _allRecords)
		{
			if (!retained.TryGetValue(r.Chance, out PrefixTotals t))
			{
				t = new PrefixTotals();
				retained[r.Chance] = t;
			}

			bool isWin = r.Outcome == BetOutcome.Win;
			t.Bets++;
			t.Wagered += r.BetAmount;
			t.NetProfit += r.NetAmount;
			if (r.BetAmount > t.MaxBetAmount) t.MaxBetAmount = r.BetAmount;
			if (isWin)
			{
				t.Wins++;
				if (r.NetAmount > t.MaxWonAmount) t.MaxWonAmount = r.NetAmount;
			}
			else
			{
				decimal loss = Math.Abs(r.NetAmount);
				if (loss > t.MaxLossAmount) t.MaxLossAmount = loss;
			}

			runState.TryGetValue(r.Chance, out (int Loss, int Win) run);
			if (r.Chance != lastChance)
			{
				run = (0, 0); // a change of chance ends both runs (§40.8)
				lastChance = r.Chance;
			}

			run = isWin ? (0, run.Win + 1) : (run.Loss + 1, 0);
			runState[r.Chance] = run;
			if (run.Loss > t.MaxConsecutiveLosses) t.MaxConsecutiveLosses = run.Loss;
			if (run.Win > t.MaxConsecutiveWins) t.MaxConsecutiveWins = run.Win;
		}

		// The ALL-BETS prefix comes from the rollup's TOP-LEVEL totals, never from summing the segments.
		// Per-segment aggregates were added after the rollup shipped, so a file written by the earlier
		// version carries runs but zeroed counts — summing those would silently report a prefix of zero and
		// put the grand total right back where the pruning left it. The top-level figures have been
		// maintained since the first version, so this path is correct for every file that can exist.
		var heldAll = new PrefixTotals();
		foreach (PrefixTotals t in retained.Values)
		{
			heldAll.Bets += t.Bets;
			heldAll.Wins += t.Wins;
			heldAll.Wagered += t.Wagered;
			heldAll.NetProfit += t.NetProfit;
			if (t.MaxBetAmount > heldAll.MaxBetAmount) heldAll.MaxBetAmount = t.MaxBetAmount;
			if (t.MaxLossAmount > heldAll.MaxLossAmount) heldAll.MaxLossAmount = t.MaxLossAmount;
			if (t.MaxWonAmount > heldAll.MaxWonAmount) heldAll.MaxWonAmount = t.MaxWonAmount;
			if (t.MaxConsecutiveLosses > heldAll.MaxConsecutiveLosses) heldAll.MaxConsecutiveLosses = t.MaxConsecutiveLosses;
			if (t.MaxConsecutiveWins > heldAll.MaxConsecutiveWins) heldAll.MaxConsecutiveWins = t.MaxConsecutiveWins;
		}

		_prunedPrefixAll = new PrefixTotals
		{
			Bets = Math.Max(0, rollup.TotalBets - heldAll.Bets),
			Wins = Math.Max(0, rollup.TotalWins - heldAll.Wins),
			Wagered = Math.Max(0m, rollup.TotalWagered - heldAll.Wagered),
			NetProfit = rollup.TotalNetProfit - heldAll.NetProfit,
			MaxBetAmount = rollup.MaxBetAmount > heldAll.MaxBetAmount ? rollup.MaxBetAmount : 0m,
			MaxLossAmount = rollup.MaxLossAmount > heldAll.MaxLossAmount ? rollup.MaxLossAmount : 0m,
			MaxWonAmount = rollup.MaxWonAmount > heldAll.MaxWonAmount ? rollup.MaxWonAmount : 0m,
			MaxConsecutiveLosses = rollup.MaxConsecutiveLossesOverall().Run > heldAll.MaxConsecutiveLosses
				? rollup.MaxConsecutiveLossesOverall().Run : 0,
			MaxConsecutiveWins = rollup.MaxConsecutiveWinsOverall().Run > heldAll.MaxConsecutiveWins
				? rollup.MaxConsecutiveWinsOverall().Run : 0
		};

		foreach (BetStatsRollup.SegmentRuns seg in rollup.Segments.Values)
		{
			retained.TryGetValue(seg.Chance, out PrefixTotals held);
			held ??= new PrefixTotals();

			var prefix = new PrefixTotals
			{
				Bets = Math.Max(0, seg.Bets - held.Bets),
				Wins = Math.Max(0, seg.Wins - held.Wins),
				Wagered = Math.Max(0m, seg.Wagered - held.Wagered),
				NetProfit = seg.NetProfit - held.NetProfit, // may legitimately be negative
				// A lifetime maximum LARGER than anything still on disk can only have come from a pruned
				// bet, so it is carried. When they are equal the record is still retained and the scan will
				// find it — carrying it anyway would double nothing, but claiming it as pruned would let a
				// rewound view show a peak that had not happened yet.
				MaxBetAmount = seg.MaxBetAmount > held.MaxBetAmount ? seg.MaxBetAmount : 0m,
				MaxLossAmount = seg.MaxLossAmount > held.MaxLossAmount ? seg.MaxLossAmount : 0m,
				MaxWonAmount = seg.MaxWonAmount > held.MaxWonAmount ? seg.MaxWonAmount : 0m,
				MaxConsecutiveLosses = seg.MaxConsecutiveLosses > held.MaxConsecutiveLosses ? seg.MaxConsecutiveLosses : 0,
				MaxConsecutiveWins = seg.MaxConsecutiveWins > held.MaxConsecutiveWins ? seg.MaxConsecutiveWins : 0
			};

			_prunedPrefixByChance[seg.Chance] = prefix;
		}
	}

	private PrefixTotals ActivePrefix() =>
		_chanceFilter == AllChances
			? _prunedPrefixAll
			: (_prunedPrefixByChance.TryGetValue(_chanceFilter, out PrefixTotals p) ? p : new PrefixTotals());

	// ── The cursor ──────────────────────────────────────────────────────────────

	// The frontier the cursor may never pass. Reading it does not move it.
	//
	// It is the LATER of the live clock and the recorded frontier, and both halves are needed.
	// `GamePresentLocalDateTime` alone froze live-follow: `_gamePresent` is only written by explicit
	// calls (SetNow, PersistCurrentTime, the init paths) and NOT by the per-frame advance, so during a
	// running autobet it stands still while `CurrentLocalDateTime` moves — the cursor pinned itself to a
	// stale frontier and the view stopped following the game. `CurrentLocalDateTime` alone would be wrong
	// the other way, in the one case where the clock legitimately sits behind the frontier: a checkpoint
	// restore. Taking the max is correct in both.
	private DateTime PresentLocal()
	{
		if (_calendarTimeService == null)
		{
			return DateTime.Now;
		}

		DateTime live = _calendarTimeService.CurrentLocalDateTime;
		DateTime frontier = _calendarTimeService.GamePresentLocalDateTime;
		return live > frontier ? live : frontier;
	}

	// The scene's whole time model, in one method. Live-follow pins the cursor to the present; otherwise
	// Play advances it at the chosen replay speed. Nothing here writes to CalendarTimeService.
	private void AdvanceCursor(double delta)
	{
		DateTime present = PresentLocal();

		if (_liveMode)
		{
			// Following the present rather than replaying: the cursor IS the present each frame, so new
			// bets appear as they settle.
			_selectedLocal = present;
			return;
		}

		if (!_cursorRunning)
		{
			return;
		}

		_selectedLocal = _selectedLocal.AddSeconds(delta * _cursorSpeed);

		// Reaching the present ends the replay — there is nothing past it to show. It does NOT switch to
		// live-follow: that is the player's explicit choice (the Live button), and silently adopting it
		// would make a replay quietly become a live view without anyone asking for it.
		if (_selectedLocal >= present)
		{
			_selectedLocal = present;
			_cursorRunning = false;
			RefreshControlLabels();
		}
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
		// Only the cursor moves. The world clock is not ours (§9.1); the seed is updated so the calendar
		// reopens where the player actually ended up rather than where they asked to go.
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

	// With a chance filter active every record shares that chance, so the "(at N%)" qualifier the
	// unfiltered figure needs (§40.8) is redundant — the scope already says it once, at the front.
	private string FormatRun(int run, int chance)
	{
		if (run <= 0)
		{
			return "0";
		}

		return _chanceFilter == AllChances
			? string.Format(CultureInfo.InvariantCulture, "{0} (at {1}%)", run, chance)
			: run.ToString(CultureInfo.InvariantCulture);
	}

	private void ResetStreakSummary()
	{
		_summaryConsecutiveLosses = 0;
		_summaryMaxLossRun = 0;
		_summaryMaxLossRunChance = 0;
		_summaryConsecutiveWins = 0;
		_summaryMaxWinRun = 0;
		_summaryMaxWinRunChance = 0;
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
			_summaryMaxWonAmount = 0m;
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
			else if (record.NetAmount > _summaryMaxWonAmount)
			{
				_summaryMaxWonAmount = record.NetAmount;
			}

			// A new segment starts wherever the game or the win chance changes: whatever the player was
			// doing before is a different experiment, and neither of its runs continues into this one.
			if (record.Chance != _summarySegmentChance ||
				!string.Equals(record.GameId, _summarySegmentGameId, StringComparison.Ordinal))
			{
				_summarySegmentChance = record.Chance;
				_summarySegmentGameId = record.GameId;
				_summarySegmentBets = 0;
				_summaryConsecutiveLosses = 0;
				_summaryConsecutiveWins = 0;
			}

			_summarySegmentBets++;

			if (record.Outcome == BetOutcome.Loss)
			{
				_summaryConsecutiveWins = 0;
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
			_summaryConsecutiveWins++;
			if (_summaryConsecutiveWins > _summaryMaxWinRun)
			{
				_summaryMaxWinRun = _summaryConsecutiveWins;
				_summaryMaxWinRunChance = _summarySegmentChance;
			}
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

	// Play/Pause and Speed drive the CURSOR. Both leave live-follow, because asking to replay is asking
	// not to be pinned to the present.
	private void OnPlayPausePressed()
	{
		_liveMode = false;
		_cursorRunning = !_cursorRunning;
		RefreshControlLabels();
	}

	private void OnSpeedButtonPressed()
	{
		_liveMode = false;
		int idx = Array.FindIndex(_speedSteps, s => Math.Abs(s - _cursorSpeed) < 0.001d);
		idx = idx < 0 ? 0 : (idx + 1) % _speedSteps.Length;
		_cursorSpeed = _speedSteps[idx];
		RefreshControlLabels();
	}

	// §9.3 — the Live button, which until now was only a CAPTION on Play/Pause that did nothing when
	// pressed. It jumps the cursor to the newest bet and follows the present from there.
	//
	// Enabled only while a player autobet is running: with no run the present does not advance, so
	// "follow the present" and "sit still" are the same thing and the control would be inert. §24.13b's
	// rule — an enabled-but-inert control is a lie — which this button had been since it was labelled.
	private void OnGoLivePressed()
	{
		if (!CanGoLive())
		{
			return;
		}

		_liveMode = true;
		_cursorRunning = false;      // live-follow supersedes replay; nothing to advance
		_cursorSpeed = _speedSteps[0]; // back to 1X, so leaving Live later resumes at a sane rate
		_selectedLocal = PresentLocal();
		_lastRenderedSecond = long.MinValue;
		_lastRenderedEndExclusive = -1;
		RefreshControlLabels();
		RefreshHistoricalViewForCurrentTime(GetCurrentLocal().ToUniversalTime(), forceRebuild: true);
	}

	// Pressing it does something only when a run is producing new bets AND the view is not already
	// following them. Outside that, the button is not greyed — it is not SHOWN (developer's call).
	//
	// Hiding rather than disabling is the stronger version of §24.13b's rule: a greyed control still
	// occupies the eye and still poses a question ("why can't I use that?"), whereas a control that
	// appears exactly when it is useful never poses one. It suits this button in particular because its
	// two unavailable states are both states in which the player has no reason to want it.
	private bool CanGoLive() => _calendarTimeService?.IsAutobetActive == true && !_liveMode;

	// Re-evaluated every frame, not only on a control press: an autobet can start or stop at any moment
	// (a stop condition, a mined block, an exhausted bankroll), and the transport must follow it. Two bool
	// comparisons per frame; the nodes are touched only on an edge.
	private void RefreshGoLiveVisibility()
	{
		if (_goLiveButton != null)
		{
			bool show = CanGoLive();
			if (show != _goLiveVisible || !_goLiveVisibilityApplied)
			{
				_goLiveVisible = show;
				_goLiveVisibilityApplied = true;
				_goLiveButton.Visible = show;
			}
		}

		// Play and Speed are meaningless with nothing ahead of the cursor to replay — following live, or
		// already standing at the present. Not a flicker risk despite the present moving during a run: a
		// replay that reaches the present stops there, the sim then advances the present past it, and both
		// controls become live again because there genuinely IS new material to play through.
		bool nothingToReplay = _liveMode || GetCurrentLocal() >= PresentLocal();
		if (nothingToReplay != _transportInert || !_transportStateApplied)
		{
			_transportInert = nothingToReplay;
			_transportStateApplied = true;
			if (_speedButton != null) _speedButton.Disabled = nothingToReplay;
			if (_playPauseButton != null) _playPauseButton.Disabled = nothingToReplay;
		}
	}

	// Navigation no longer touches the clock at all — the cursor was never the world's time, so there is
	// nothing to put back. (Both handlers previously reset IsRunning, and one called SetNow(); that was
	// the scene tidying up after borrowing something it should not have borrowed.)
	// SF.4.2: origin-aware back — BetsHistoryExplorer is reachable from more than one hub
	// (CalendarsNavigator AND ScFinances), so return to whichever scene launched it, falling back to Main
	// Menu if that memory is empty (e.g. deep-linked or first navigation).
	private void OnBackToCalendarPressed()
	{
		SceneManager.SceneId target = _sceneManager?.PreviousScene ?? SceneManager.SceneId.MainMenu;
		_sceneManager?.Go(target);
	}

	private void OnBackToDicePressed()
	{
		_sceneManager?.Go(SceneManager.SceneId.DiceGame);
	}

	private const double GameBaseSpeed = 100.0;

	private void RefreshControlLabels()
	{
		RefreshGoLiveVisibility();

		if (_liveMode)
		{
			_playPauseButton.Text = "Play";
			_speedButton.Text = "1x (Live)";
			return;
		}

		double speedX = _cursorSpeed / GameBaseSpeed;
		_playPauseButton.Text = _cursorRunning ? "Pause" : "Play";
		_speedButton.Text = string.Create(CultureInfo.InvariantCulture, $"Speed {speedX:0.##}x");
	}

	private DateTime GetCurrentLocal()
	{
		// THE CURSOR — no longer the world clock (§9.2). Every consumer in this scene reads through here,
		// so the summary, the preview window and the chance selector all followed for free.
		return _selectedLocal;
	}
}
