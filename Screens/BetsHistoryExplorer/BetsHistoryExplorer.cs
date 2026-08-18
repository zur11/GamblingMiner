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
	private Button _goToNowButton;
	// ── Stepping through time (§4.2c) ───────────────────────────────────────────
	// A multi-toggle that picks the KIND of movement and a second button that performs one of them. Split
	// in two rather than offered as six buttons because the six are mutually exclusive and only one is
	// wanted at a time; the setter states which, so the action button never has to be read twice.
	//
	// Built for the paused state first but correct while playing too: a step is a jump of the cursor, and
	// the cursor is just a DateTime whether or not something else is also advancing it. Per §6.4 a step
	// changes WHERE the cursor is and never whether the panel is playing — the two are separate axes.
	private static readonly (string Label, int Days, int Hours, int Minutes)[] StepModes =
	{
		("◀ Rewind by day", -1, 0, 0),
		("◀ Rewind by hour", 0, -1, 0),
		("◀ Rewind by minute", 0, 0, -1),
		("Forward by day ▶", 1, 0, 0),
		("Forward by hour ▶", 0, 1, 0),
		("Forward by minute ▶", 0, 0, 1),
	};

	private Button _stepModeButton;
	private Button _stepApplyButton;
	private int _stepModeIndex;   // default: rewind by day (§4.2c)
	private bool _goToNowDisabled;
	private bool _goToNowDisabledApplied;
	private int _transportCaptionState = -1;
	private bool _transportCaptionApplied;
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

	private DateTime _selectedLocal;   // THE CURSOR: the instant being replayed — where it ACTUALLY is
	private bool _cursorRunning;       // Play/Pause, driving the cursor rather than the clock
	private double _cursorSpeed = 100d; // game-seconds per real second, the old _speedSteps scale
	private readonly double[] _speedSteps = { 100d, 200d, 400d, 1000d };

	// ── Demand vs. settled (mini-plan 04 §6.2 / §6.4a) ──────────────────────────
	// Where the cursor was ASKED to be this frame, as opposed to `_selectedLocal`, where the frame's emit
	// budget actually let it get to. This is R2-C1's split one layer up: there, the simulation's demand is
	// `delta × SpeedMultiplier` and the game CLOCK gives way when the engine cannot keep up; here the
	// demand is `delta × replay speed` and the replay CURSOR gives way when the view cannot emit every bet
	// the demand crosses. In both cases the thing that must never be corrupted is preserved — mining work
	// per in-game second there, every bet of the retained range here.
	private DateTime _cursorDemandLocal;
	// The present as of the frame in which the demand above was set, so `demand >= present` can be asked
	// without the answer flipping merely because the world advanced between two frames. A cache of a
	// computed value, deliberately NOT a mode flag — see IsLiveFollowing.
	private DateTime _lastPresentLocal;
	// "No outstanding request" — the value the demand holds whenever the player is not asking to be
	// anywhere: on arrival, and from the moment they press Pause. See RequestedThePresent.
	private static readonly DateTime NoRequestSentinel = DateTime.MinValue;
	// Auto-snap repaints wholesale, so it obeys BOTH guards the snapshot path already obeys: at most once
	// per real second, and only once a material amount of new material exists. The real-time half is not
	// optional — the developer's "+100 in-game seconds" equals one real second only at the base scale, and
	// game time is a quantity the DEV time scale multiplies by up to 90x. A refresh cadence denominated in
	// game time accelerates with it (the lesson already written into _Process's own throttle).
	private double _autoSnapTimer;

	// §6.4a — live-follow is DERIVED, never stored. mini-plan 03's `_liveMode` was a flag decided in
	// _Ready that later became something the player could enter and leave, which is precisely the shape
	// that drifts out of step with what it claims to describe. A derived value cannot disagree with its
	// own definition:
	//
	//     liveFollowing == playing && cursor-demand is at the present && a run is producing bets
	//
	// Every behaviour in the plan falls out of it: rewinding drops the demand below the present so follow
	// ends while PLAY is untouched (§6.4's two axes); forward-stepping into the present satisfies it again
	// with no special case; a run stopping drops the last term, and the replay then halts at the present
	// (§4.2b). "Go to Now" is one assignment — demand := present — which follows only if the panel is also
	// in play, and is otherwise just the final snap.
	//
	// The demand rather than the settled cursor is what is tested, because backpressure (§6.2) legitimately
	// leaves the SETTLED cursor behind the present while following, and falling behind is the feature
	// (§6.3), not an exit from it.
	//
	// ── "did the player ASK to be at the present?" ──────────────────────────────
	// A PAUSED PANEL HAS NO DEMAND AT ALL — that is the whole of it, and it is what keeps the expression
	// this short. The first draft had the paused branch re-assert the settled cursor as a demand, which
	// made two unrelated situations indistinguishable from a request: arriving with the cursor already at
	// the present (`CalendarTimeService` seeds `ExplorerSelectedLocalDateTime` FROM the present on boot and
	// on `SetNow`, so this is the common entry path, not a corner case), and pausing a live-follow that had
	// no backpressure gap. Both would have started tracking the present on their own — the first breaking
	// §4.1's "nothing moves until the player asks", the second making Pause not pause.
	//
	// An arrival is not a request and a pause withdraws one, so in both the demand is simply absent. The
	// sentinel earns its keep by making "absent" a value the comparison already handles, rather than a
	// second flag asking to be kept in step with the first.
	private bool RequestedThePresent => _cursorDemandLocal >= _lastPresentLocal;

	private bool RunIsProducingBets => _calendarTimeService?.IsAutobetActive == true;

	private bool IsLiveFollowing => _cursorRunning && RunIsProducingBets && RequestedThePresent;

	// ── Auto-snap (developer, 2026-08-18) ───────────────────────────────────────
	// The SAME request, on the other side of the play axis. Having asked to be at the present, the player
	// should not have to keep asking as the present moves — so rather than re-enabling "Go to Now" every
	// second, the view re-snaps itself and the button stays quiet.
	//
	// It is NOT a slow live-follow, and the difference is the whole point: live-follow EMITS every bet the
	// cursor crosses, one row at a time, and is bound by §6.2's promise that none is skipped. Auto-snap
	// JUMPS — it repaints the newest MaxPreviewEntries and skips whatever went past in between, exactly as
	// a manual "Go to Now" does (§8.4: a jump is not a replay). That is the honest meaning of watching the
	// present while PAUSED: show me the latest, do not replay it to me.
	//
	//     playing  + requested the present + a run → live-follow (every bet)
	//     paused   + requested the present + a run → auto-snap   (the latest, refreshed)
	//
	// Which makes the pair the two-axis model of §6.4 stated outright: one request, two panel states.
	private bool IsAutoSnapping => !_cursorRunning && RunIsProducingBets && RequestedThePresent;

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
	// How far into `_sortedRecords` the view has actually been rendered, exclusive. Under mini-plan 04 this
	// is no longer a cache of "what the last snapshot happened to show" but the authoritative emit
	// frontier: the append path advances it one bet at a time, the summary walks to exactly this index, and
	// the cursor is not allowed past the bet it names. -1 means "no window rendered yet", which forces the
	// wholesale path.
	private int _renderedEndExclusive = -1;
	// The emit frontier as of the last summary-label refresh, so a burst that never changes the game
	// second still refreshes the line that counts it.
	private int _lastSummaryRenderedEnd = -1;

	// ── The per-frame emit budget (§6.2) ────────────────────────────────────────
	// The rule this enforces: when the frame cannot render every bet the requested speed demands, the
	// REPLAY CLOCK slows down — not one bet of the retained range is ever skipped. A row cap that DROPPED
	// bets was considered and rejected: dropping bets is the exact failure this plan exists to end, and
	// announcing the drop makes it legible, not acceptable.
	//
	// DELIBERATELY UNPRICED (§6.2's calibration note + §40.7): this is a placeholder until it is watched at
	// 10x across a dense burst, and it is TIMED before it is tuned. Getting it wrong costs only smoothness,
	// never a bet. For scale: at 10x the cursor covers 1000 game-seconds per real second and the measured
	// density is 0.047 bets/game-second (§1.2a), i.e. ~47 bets/s — about 0.8 per frame at 60 fps, so 25 is
	// roughly 30× headroom, spent only on the same-timestamp bursts the journal is full of.
	private const int MaxAppendRowsPerFrame = 25;

	// The requested-vs-actual readout (§6.2). Measured over a window rather than per frame because the
	// figure fluctuates by construction — the cursor runs at full speed through the long empty stretches
	// between bursts and pays only inside them.
	//
	// §38.7's third lesson applies the moment this appears: a displayed throttle is a MEASUREMENT, not a
	// diagnosis. If actual sits far below requested the question is what is eating the frame — never
	// "raise MaxAppendRowsPerFrame", which only hands a saturated frame more work.
	private const double ThrottleWindowSeconds = 1.0;
	private Label _replayThrottleLabel;
	private double _throttleWindowRealSeconds;
	private double _throttleWindowGameSeconds;
	private double _throttleActualSpeedX = -1d;
	private string _throttleLabelApplied;
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
		_goToNowButton = GetNode<Button>("%GoToNowButton");
		_stepModeButton = GetNode<Button>("%StepModeButton");
		_stepApplyButton = GetNode<Button>("%StepApplyButton");
		_replayThrottleLabel = GetNode<Label>("%ReplayThrottleLabel");
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
		_cursorSpeed = _speedSteps[0];
		// §4.1 — the scene ARRIVES PAUSED. It used to open playing, cursor already advancing. The scene's
		// job is to answer "what happened here?", and an auto-advancing view starts destroying that answer
		// the moment it appears; nothing moves now until the player asks.
		_cursorRunning = false;
		_lastPresentLocal = PresentLocal();
		_cursorDemandLocal = NoRequestSentinel;   // arriving is not asking to be here

		// Subscribed unconditionally now: the player may enter live-follow at any time, so the handler that
		// keeps the record list current must already be attached when they do.
		if (_userStatsService != null)
			_userStatsService.StatsChanged += OnLiveStatsChanged;

		_playPauseButton.Pressed += OnPlayPausePressed;
		_speedButton.Pressed += OnSpeedButtonPressed;
		_goToNowButton.Pressed += OnGoToNowPressed;
		_stepModeButton.Pressed += OnStepModePressed;
		_stepApplyButton.Pressed += OnStepApplyPressed;
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

		DateTime present = PresentLocal();

		// A checkpoint restore can retract the present BEHIND the cursor. Clamping is not optional: every
		// index in this scene is derived from a binary search on the cursor, so a cursor claiming a future
		// the world has taken back would render bets that, from the world's point of view, have not
		// happened. Jump rather than slide, because the window has to be rebuilt backwards anyway.
		if (_selectedLocal > present)
		{
			JumpCursorTo(present);
		}

		// Read before `_lastPresentLocal` is rewritten below — the derived states are answers about the
		// frame the demand was set in, not about this one.
		bool autoSnapping = IsAutoSnapping;

		if (!_cursorRunning && !autoSnapping)
		{
			// PAUSED AND NOT TRACKING. The cursor does not move, so nothing can cross it and there is
			// nothing to emit; and no demand is outstanding, because arriving somewhere is not asking to be
			// there and pausing withdrew whatever was. Holding the sentinel here rather than re-asserting
			// the cursor is what makes both of those true for more than one frame.
			_cursorDemandLocal = NoRequestSentinel;
			_lastPresentLocal = present;
			_autoSnapTimer = 0d;
			ResetThrottleMeasurement();
		}
		else if (autoSnapping)
		{
			// The request is re-asserted every frame, which is what makes the state self-sustaining in the
			// same way live-follow is: demand == present == _lastPresentLocal, so RequestedThePresent stays
			// true until the player rewinds, steps back, or the run stops. The JUMP is throttled
			// separately — sustaining the state costs nothing, repainting does.
			_cursorDemandLocal = present;
			_lastPresentLocal = present;
			ResetThrottleMeasurement();   // a snap has no "requested speed" to fall short of

			// Two guards, and they are different questions: the real-second floor is the REPAINT cost
			// (§2.3 — the snapshot path never got cheaper, it just runs less often), the in-game gap is
			// whether there is anything new worth repainting FOR. JumpCursorTo resets the timer, so a
			// manual press in the meantime substitutes for this rather than adding to it.
			_autoSnapTimer += delta;
			if (_autoSnapTimer >= ViewRefreshIntervalSeconds &&
				(present - _selectedLocal).TotalSeconds >= GoToNowMinGapGameSeconds)
			{
				JumpCursorTo(present);
			}
		}
		else
		{
			_autoSnapTimer = 0d;

			DateTime previousCursor = _selectedLocal;
			_cursorDemandLocal = ComputeCursorDemand(delta, present);
			_lastPresentLocal = present;

			// §6.2 — the emit step is what actually MOVES the cursor. It renders every bet between the last
			// emit frontier and the demand, and if the frame's budget runs out first it leaves the cursor
			// on the timestamp of the last bet emitted instead of where the demand wanted it. The replay
			// falls behind wall-clock; it never falls behind the data.
			EmitCrossedBetsAndSettleCursor(_cursorDemandLocal);
			AccumulateThrottleMeasurement(delta, previousCursor);
		}

		RefreshTransportAvailability();

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

		RefreshThrottleLabel();

		if (_sortedRecords.Count <= 0)
		{
			return;
		}

		// §2.3 — THE 1 Hz FLOOR STAYS IN FORCE FOR THE SNAPSHOT PATH. Appending one row is cheap;
		// repainting a hundred is what §38.8 measured and removed, and the new append path earns its rate
		// by doing far less per step, not by lifting this guard. What runs below is now only the
		// wholesale/summary-label half — the per-bet rendering happens above, every frame, unthrottled.
		//
		// Its cadence used to be "whenever the GAME second changes", which is a cadence the DEV time scale
		// multiplies: at 9000X the game second changes every frame, so this ran ~520 entry updates per
		// frame. A refresh cadence must be denominated in REAL time — game time is a quantity the player
		// can accelerate by 90×, and any per-game-second budget accelerates with it. Both guards are kept:
		// the timer bounds how often, the second-changed test avoids redundant identical work when the
		// cursor is slow or stopped.
		_viewRefreshTimer += delta;
		if (_viewRefreshTimer < ViewRefreshIntervalSeconds)
		{
			return;
		}

		// The second-changed test is no longer sufficient on its own. The emit budget can hold the cursor on
		// ONE timestamp for many frames while it drains a same-timestamp burst — the journal is full of
		// them (median gap 0.00 game-seconds) — and during that the rows stream in while the summary line
		// beneath them would sit frozen, describing a window that is no longer on screen. So either moving
		// clock or a moved emit frontier earns a refresh; the 1 Hz timer still bounds how often.
		long currentSecond = new DateTimeOffset(current).ToUnixTimeSeconds();
		if (currentSecond == _lastRenderedSecond && _renderedEndExclusive == _lastSummaryRenderedEnd)
		{
			return;
		}

		_viewRefreshTimer = 0d;
		_lastRenderedSecond = currentSecond;
		_lastSummaryRenderedEnd = _renderedEndExclusive;
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

		// THE WHOLESALE PATH IS NOW THE EXCEPTION, and the condition matters more than it looks. It used to
		// repaint whenever the window had moved AT ALL — which under mini-plan 04 would silently undo the
		// whole plan: while the emit budget is draining a burst, `endExclusive` legitimately runs ahead of
		// `_renderedEndExclusive`, and repainting "the last 100 up to endExclusive" would SKIP every bet in
		// between. That is the exact failure §6.2 exists to end.
		//
		// So it rebuilds only when the append path cannot express the change: no window rendered yet, or
		// the view moved BACKWARDS (a rewind, a retracted present), or a caller forced it. Forward motion
		// belongs to EmitCrossedBetsAndSettleCursor, which also owns the summary walk while it does.
		bool mustRebuild = forceRebuild || _renderedEndExclusive < 0 || endExclusive < _renderedEndExclusive;
		if (mustRebuild)
		{
			_renderedEndExclusive = endExclusive;
			int start = Math.Max(0, endExclusive - MaxPreviewEntries);
			List<BetRecord> preview = _sortedRecords.GetRange(start, endExclusive - start);
			_previousWinnerNumbersGrid.LoadFromHistoricalRecords(preview);
			_betHistoryContainer.LoadFromHistoricalRecords(preview);

			// `forceRebuild: false` even here, and it is load-bearing rather than an optimisation. A
			// FORWARD jump — a step, a manual "Go to Now", and now an auto-snap once per real second —
			// needs the summary only to ADVANCE, which the walk does natively; forcing it would rewalk the
			// entire journal (196k records in the developer's current world) on every one of them. The
			// walk still resets itself on the case that genuinely needs it, `endExclusive < _summaryCursor`
			// (a rewind), and every caller that invalidates the VIEW rather than the position goes through
			// ApplyChanceFilter, which zeroes the accumulators itself.
			AdvanceSummaryTo(endExclusive, forceRebuild: false);
		}
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

	// The scene's whole time model, in one method — but only its DEMAND half. This says where the cursor
	// is asked to be; EmitCrossedBetsAndSettleCursor says where it gets to. Nothing here writes to
	// CalendarTimeService.
	//
	// Note the replay branch builds the demand from `_selectedLocal`, the SETTLED cursor, not from the
	// previous demand: §6.2's rule is that a throttled frame leaves the cursor on the last bet emitted and
	// "the next frame resumes from there". A demand that kept running ahead would open a gap that never
	// closed. Only live-follow has a demand of its own — the present — which is why falling behind while
	// following is a persistent, honest gap (§6.3) rather than a lost position.
	// Called only while the panel is PLAYING — a paused panel has no demand at all (see
	// RequestedThePresent), and _Process handles that case without asking this method.
	private DateTime ComputeCursorDemand(double delta, DateTime present)
	{
		if (IsLiveFollowing)
		{
			return present;
		}

		DateTime next = _selectedLocal.AddSeconds(delta * _cursorSpeed);
		if (next < present)
		{
			return next;
		}

		// §2.1 — on reaching the present the multiplier drops automatically to base × 1.
		if (Math.Abs(_cursorSpeed - _speedSteps[0]) > 0.001d)
		{
			_cursorSpeed = _speedSteps[0];
			RefreshControlLabels();
		}

		// §6.4 supersedes mini-plan 03 §9.3's "reaching the present must never enter live-follow silently".
		// Under the two-axis model that is not a decision at all: the player already expressed the intent
		// by leaving the panel in PLAY, and arriving at the present does not need a second confirmation —
		// with a run still producing bets, `IsLiveFollowing` is simply true from the next frame. With NO
		// run the present is static, so there is nothing left to replay and the panel stops (§4.2b).
		if (!RunIsProducingBets)
		{
			_cursorRunning = false;
			RefreshControlLabels();
		}

		return present;
	}

	// ── The emit step (§2.3 / §6.2) ─────────────────────────────────────────────
	// Renders ONE ROW PER BET the cursor crosses, exactly as DiceGame does for a settled bet, and settles
	// the cursor at whatever that could actually cover this frame.
	//
	// The clumping this replaces was never a speed problem and never a timestamp-collision problem: the
	// scene repainted a WINDOW where DiceGame emits an EVENT, so the number of "new" bets seen per repaint
	// was (repaint rate) × (cursor rate) × (bet density) — three multipliers, none of which is "one bet".
	// Rendering every crossed bet reproduces DiceGame's behaviour by construction, at any speed, and gets
	// the plan's whole pacing specification for free: the hardware rate is already recorded in the data as
	// the SPACING between bets, so a stretch played at 1 piece replays at 1X because its bets are spaced
	// that way, and crossing into a 2X stretch speeds up at exactly the first faster bet — no hardware
	// lookup, no detection step, no threshold. (Which matters, because hardware history is not persisted:
	// a base derived from hardware STATE could not be computed for a past date at all.)
	private void EmitCrossedBetsAndSettleCursor(DateTime demandLocal)
	{
		// No window rendered yet (fresh load, filter change, invalidated view): the wholesale path owns the
		// containers until it has run once, so the cursor is free to move and the rebuild will catch up.
		if (_renderedEndExclusive < 0 || _sortedRecords.Count <= 0)
		{
			_selectedLocal = demandLocal;
			return;
		}

		int target = UpperBound(_sortedRecords, demandLocal.ToUniversalTime());
		if (target <= _renderedEndExclusive)
		{
			// Nothing crossed — the cursor is passing through a gap between bets and pays nothing for it.
			_selectedLocal = demandLocal;
			return;
		}

		int index = _renderedEndExclusive;
		int budget = MaxAppendRowsPerFrame;
		while (index < target && budget > 0)
		{
			BetRecord record = _sortedRecords[index];
			_previousWinnerNumbersGrid.AddWinnerNumber(record.Roll, record.Outcome == BetOutcome.Win);
			_betHistoryContainer.AppendHistoricalRecord(record);
			index++;
			budget--;
		}

		// The summary walks to the SAME index the rows did, so the figures can never describe a window
		// different from the one on screen — the two used to be driven by separate binary searches.
		AdvanceSummaryTo(index, forceRebuild: false);
		_renderedEndExclusive = index;

		if (index >= target)
		{
			_selectedLocal = demandLocal;
			return;
		}

		// Budget spent: the CLOCK pays. Leave the cursor on the last bet emitted; the next frame resumes
		// from there. Never below where it already was — bets sharing a timestamp are common in this
		// journal (median gap 0.00 game-seconds), and the cursor must not appear to move backwards while
		// a burst is being drained.
		DateTime settled = _sortedRecords[index - 1].TimestampUtc.ToLocalTime();
		_selectedLocal = settled > _selectedLocal ? settled : _selectedLocal;
	}

	// Moves the cursor somewhere it did not walk to — a step (§4.2c), "Go to Now", or a retracted present.
	// A jump is NOT a replay, so §6.2's no-skipped-bet rule does not apply to it: the player asked to be
	// somewhere else, and the answer to "what happened here?" is the last MaxPreviewEntries bets before
	// that instant, which is exactly what the wholesale path shows.
	private void JumpCursorTo(DateTime targetLocal)
	{
		_selectedLocal = targetLocal;
		_cursorDemandLocal = targetLocal;
		_renderedEndExclusive = -1;      // force the wholesale rebuild rather than an append from nowhere
		_lastRenderedSecond = long.MinValue;
		// Any jump restarts the auto-snap cadence, so a manual press genuinely TAKES THE PLACE of the
		// automatic refresh rather than landing just before one and paying for both repaints.
		_autoSnapTimer = 0d;
		ResetThrottleMeasurement();
		RefreshHistoricalViewForCurrentTime(targetLocal.ToUniversalTime(), forceRebuild: true);
	}

	// ── The requested-vs-actual readout (§6.2) ──────────────────────────────────
	private void AccumulateThrottleMeasurement(double delta, DateTime previousCursor)
	{
		// Only a replay has a "requested speed" to fall short of. While following the present the demand is
		// the world's own pace, so comparing it against the multiplier would report a shortfall that means
		// nothing — the gap that matters there is already legible in the two clocks (§3).
		if (!_cursorRunning || IsLiveFollowing)
		{
			ResetThrottleMeasurement();
			return;
		}

		_throttleWindowRealSeconds += delta;
		_throttleWindowGameSeconds += (_selectedLocal - previousCursor).TotalSeconds;
		if (_throttleWindowRealSeconds < ThrottleWindowSeconds)
		{
			return;
		}

		_throttleActualSpeedX = _throttleWindowGameSeconds / (_throttleWindowRealSeconds * GameBaseSpeed);
		_throttleWindowRealSeconds = 0d;
		_throttleWindowGameSeconds = 0d;
	}

	private void ResetThrottleMeasurement()
	{
		_throttleWindowRealSeconds = 0d;
		_throttleWindowGameSeconds = 0d;
		_throttleActualSpeedX = -1d;
	}

	// Shown ONLY while the two figures differ, so it reads as information rather than a permanent warning.
	private void RefreshThrottleLabel()
	{
		if (_replayThrottleLabel == null)
		{
			return;
		}

		double requested = _cursorSpeed / GameBaseSpeed;
		string text = null;
		if (_throttleActualSpeedX >= 0d && _throttleActualSpeedX < requested * 0.95d)
		{
			text = string.Create(
				CultureInfo.InvariantCulture,
				$"Speed: {requested:0.##}x requested / {_throttleActualSpeedX:0.##}x actual");
		}

		if (string.Equals(text, _throttleLabelApplied, StringComparison.Ordinal))
		{
			return;
		}

		_throttleLabelApplied = text;
		_replayThrottleLabel.Text = text ?? string.Empty;
		_replayThrottleLabel.Visible = text != null;
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
		ResetSummaryAccumulators();
		_renderedEndExclusive = -1;
		_lastRenderedSecond = long.MinValue;
	}

	// The two places that zero the summary walk. They were written out twice and had ALREADY drifted —
	// this one was missing `_summaryMaxWonAmount`, latent only because every caller happened to be followed
	// by a forced rebuild that zeroed it again. One method, so they cannot drift a second time.
	private void ResetSummaryAccumulators()
	{
		_summaryCursor = 0;
		_summaryTotalBets = 0;
		_summaryMaxBetAmount = 0m;
		_summaryMaxLossAmount = 0m;
		_summaryMaxWonAmount = 0m;
		ResetStreakSummary();
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
			ResetSummaryAccumulators();
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

	// Play/Pause toggles the PANEL's state and nothing else (§6.4). It no longer clears a live-follow flag,
	// because there is no flag: pausing at the present simply makes `IsLiveFollowing` false through its
	// first term, and pressing Play again there makes it true again — which is exactly right, since
	// "playing at the present while the world produces bets" IS what live-follow means.
	// THE ONLY PLACE `_cursorRunning` CAN BECOME TRUE, and that is load-bearing rather than incidental.
	// Live-follow and auto-snap are both derived from it, so if anything else could start the panel
	// playing, the scene would be able to put itself into live-follow — which is precisely the thing §4.1
	// forbids ("nothing moves until the player asks"). The file's only other two writes are both `false`:
	// arriving (§4.1) and a replay reaching the present with no run left to follow (§4.2b).
	private void OnPlayPausePressed()
	{
		_cursorRunning = !_cursorRunning;

		if (!_cursorRunning)
		{
			// PAUSING FREEZES — and this line is what makes it so. Without it, pausing a live-follow would
			// land straight in auto-snap (the identical request, the other side of the play axis) and the
			// view would go on jumping to the newest bets: the exact opposite of what a player reaches for
			// Pause to do. Withdrawing the request is the honest statement of what they asked for — stop,
			// not keep bringing me to now.
			//
			// Play deliberately does NOT do the mirror of this. Pressing Play out of auto-snap SHOULD
			// become live-follow: the player is at the present, already asked to be, and has now asked to
			// see every bet rather than a refreshed snapshot.
			_cursorDemandLocal = NoRequestSentinel;
		}

		RefreshControlLabels();
	}

	// §6.1 — the ladder is left exactly as it was, and that is a RESULT, not an omission. The base is the
	// hardware rate in bets per real second (~5 bets/s at 5 credits), so 100 game-seconds/s already means
	// "as it happened" and the top step, 1000, is already the specified base × 10 ceiling. The premise for
	// changing it came from a bad statistic — a rate divided by the part of the interval where something
	// happened rather than by the whole of it, over-reporting density 32× (§1.2a).
	private void OnSpeedButtonPressed()
	{
		int idx = Array.FindIndex(_speedSteps, s => Math.Abs(s - _cursorSpeed) < 0.001d);
		idx = idx < 0 ? 0 : (idx + 1) % _speedSteps.Length;
		_cursorSpeed = _speedSteps[idx];
		ResetThrottleMeasurement();
		RefreshControlLabels();
		// Raising the speed while parked at the present is how a paused viewer asks to replay again; the
		// cursor itself is untouched, so nothing here needs to rebuild.
	}

	// ── "Go to Now" (developer, 2026-08-18 — supersedes mini-plan 03 §9.3's "Go Live") ──────────────────
	// ONE ASSIGNMENT — put the cursor at the present — with TWO OUTCOMES the handler does not branch on,
	// because §6.4a's derived expression already tells them apart:
	//
	//   panel in PLAY + a run producing bets → the jump lands the demand on the present, so on the very
	//                                          next frame `IsLiveFollowing` is true and the panel follows.
	//   anything else (paused, or no run)    → the same jump is just the final SNAP: the last
	//                                          MaxPreviewEntries bets up to now, and the cursor stays.
	//
	// So the button no longer sets play, as the old "Go Live" did. Pressing it while paused is a request
	// to SEE the end of the history, not to start replaying it — and those are the two axes of §6.4, of
	// which this control belongs to the cursor one alone.
	private void OnGoToNowPressed()
	{
		if (!CanGoToNow())
		{
			return;
		}

		DateTime present = PresentLocal();
		_lastPresentLocal = present;
		JumpCursorTo(present);
		RefreshControlLabels();
	}

	// ── Stepping through time (§4.2c) ───────────────────────────────────────────
	private void OnStepModePressed()
	{
		_stepModeIndex = (_stepModeIndex + 1) % StepModes.Length;
		RefreshControlLabels();
	}

	private void OnStepApplyPressed()
	{
		(string _, int days, int hours, int minutes) = StepModes[_stepModeIndex];
		DateTime target = _selectedLocal.AddDays(days).AddHours(hours).AddMinutes(minutes);

		// Both bounds are the ones already in force everywhere else in this scene: the replay floor
		// (mini-plan 03 §6.6 — the oldest bet still on disk) and the present. A forward step that would
		// overshoot LANDS ON the present rather than being refused, and if the panel is in play, follow
		// re-engages there by itself (§6.4) — the derived expression needs no case for it.
		DateTime present = PresentLocal();
		if (target > present)
		{
			target = present;
		}

		if (_windowFloorLocal.HasValue && target < _windowFloorLocal.Value)
		{
			target = _windowFloorLocal.Value;
		}

		if (target == _selectedLocal)
		{
			return;
		}

		// Play state is deliberately NOT touched: rewinding leaves live-follow but does not leave PLAY, so
		// stepping back from the present while playing keeps playing — from the new point, forward, as a
		// replay (§6.4).
		_lastPresentLocal = present;
		JumpCursorTo(target);
		RefreshControlLabels();
	}

	// ── What "there is somewhere forward to go" means ───────────────────────────
	// ONE predicate behind BOTH forward controls — "Go to Now" and a forward-programmed Step — because
	// §39.16 rule 6 applies squarely here: a displayed signal must share its source with the action it
	// advertises. Two independently-written tests would eventually let a button offer a jump the handler
	// then refuses, or grey out one that would have worked.
	//
	// The threshold is not zero, and that is the whole design of it. During a live run the present moves
	// every frame, so `cursor < present` is true essentially always — a zero threshold would leave both
	// controls permanently enabled, offering jumps of a few milliseconds and flickering on every repaint.
	// One real second at 1x (= GameBaseSpeed = 100 in-game seconds) is the developer's chosen granularity:
	// after a snap the controls go quiet, and they come back exactly when a second's worth of new material
	// exists to go to. With NO run the present is static, so once you arrive they stay quiet until you
	// rewind — which is correct, since there is genuinely nothing forward.
	private const double GoToNowMinGapGameSeconds = GameBaseSpeed;

	private bool HasMaterialGapToPresent() =>
		(PresentLocal() - GetCurrentLocal()).TotalSeconds >= GoToNowMinGapGameSeconds;

	// Disabled while FOLLOWING, and only there. Backpressure (§6.2) can legitimately open a gap wider than
	// the threshold while following, and offering the jump would invite the player to skip the bets the
	// replay is still honestly working through — §6.3 makes that gap the feature, not a defect a button
	// should close.
	//
	// While AUTO-SNAPPING it stays available (developer, 2026-08-18): the auto-snap removes the OBLIGATION
	// to press, not the ability to. The two use different guards and that is what gives the button
	// something to do — the auto-snap additionally waits on the 1-real-second repaint floor, so whenever
	// game time runs faster than 100 in-game seconds per real second (any raised DEV time scale) the
	// material gap opens first and pressing forces the refresh early. At exactly base scale the two
	// coincide and the button is live for about a frame, which costs nothing.
	private bool CanGoToNow() => !IsLiveFollowing && HasMaterialGapToPresent();

	private static bool IsForwardStep(int modeIndex)
	{
		(string _, int days, int hours, int minutes) = StepModes[modeIndex];
		return days + hours + minutes > 0;
	}

	// Re-evaluated every frame, not only on a control press: an autobet can start or stop at any moment
	// (a stop condition, a mined block, an exhausted bankroll), and the transport must follow it. A handful
	// of comparisons per frame; the nodes are touched only on an edge.
	private void RefreshTransportAvailability()
	{
		// Greyed, no longer HIDDEN. mini-plan 03 §9.3 hid it because its only unavailable states were ones
		// the player had no reason to want it in. That stopped being true once the button gained its second
		// outcome: a greyed "Go to Now" now means one of two things the player DOES want to know — the view
		// is tracking the present for you, or it is following it bet by bet — and the caption below says
		// which. An absent control could say neither.
		bool goToNowDisabled = !CanGoToNow();
		if (_goToNowButton != null &&
			(goToNowDisabled != _goToNowDisabled || !_goToNowDisabledApplied))
		{
			_goToNowDisabled = goToNowDisabled;
			_goToNowDisabledApplied = true;
			_goToNowButton.Disabled = goToNowDisabled;
			_goToNowButton.Visible = true;
		}

		// Play/Pause is inert only when the cursor stands at a present that is not moving — no run, nothing
		// ahead, so playing and sitting still are the same thing. It is NOT disabled at a present a run is
		// still advancing: pausing there is the player leaving live-follow, which is a real action.
		//
		// Speed is inert in that case too, and additionally in BOTH states that honour a request to be at
		// the present: while following, the demand is the world's own pace rather than base × N; while
		// auto-snapping, the cursor jumps and never reads a speed at all. §24.13b — an enabled-but-inert
		// control is a lie.
		bool presentIsStatic = GetCurrentLocal() >= PresentLocal() && !RunIsProducingBets;
		bool speedInert = presentIsStatic || IsLiveFollowing || IsAutoSnapping;
		if (presentIsStatic != _transportInert || !_transportStateApplied)
		{
			_transportInert = presentIsStatic;
			_transportStateApplied = true;
			if (_playPauseButton != null) _playPauseButton.Disabled = presentIsStatic;
		}

		if (_speedButton != null && _speedButton.Disabled != speedInert)
		{
			_speedButton.Disabled = speedInert;
		}

		// The Step ACTION follows the SAME predicate as "Go to Now" whenever it is programmed to move
		// forward — not merely a similar one. A forward step of any size clamps to the present, so it asks
		// the identical question and must give the identical answer, including the `!IsLiveFollowing` term:
		// while following, a forward jump would skip the bets the replay is still honestly working through.
		// A rewind-programmed step is never disabled — there is always past to go back to, bounded by the
		// replay floor (§4.2c).
		bool stepInert = IsForwardStep(_stepModeIndex) && !CanGoToNow();
		if (_stepApplyButton != null && _stepApplyButton.Disabled != stepInert)
		{
			_stepApplyButton.Disabled = stepInert;
		}

		// Both derived states can begin or end with NO button pressed — a run starting or stopping is
		// enough — so the captions cannot be maintained from the press handlers alone.
		//
		// "Tracking Now" is keyed on the button being DISABLED, not merely on auto-snapping: once the
		// material gap reopens the button is pressable again, and a pressable control must be captioned
		// with what pressing does. A live button reading "Tracking Now" would state the panel's state where
		// the player is looking for the action's name.
		int captionState = IsLiveFollowing ? 2 : ((IsAutoSnapping && goToNowDisabled) ? 1 : 0);
		if (captionState != _transportCaptionState || !_transportCaptionApplied)
		{
			_transportCaptionState = captionState;
			_transportCaptionApplied = true;
			ApplyTransportCaptions();
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
		RefreshTransportAvailability();
		ApplyTransportCaptions();
	}

	// Live-follow is derived, so it can start or end WITHOUT any button being pressed — a run beginning or
	// stopping is enough. The captions therefore cannot be maintained only from the press handlers, which
	// is why RefreshTransportAvailability (already per-frame, already edge-triggered) drives them too.
	private void ApplyTransportCaptions()
	{
		if (_stepModeButton != null)
		{
			_stepModeButton.Text = StepModes[_stepModeIndex].Label;
		}

		// The "Go to Now" caption carries the state, because that button is the one the two derived states
		// grey out — and a greyed control that does not say why is the §24.13b problem in its other form.
		// A disabled button reading "Tracking Now" answers the question before it is asked.
		if (_goToNowButton != null)
		{
			_goToNowButton.Text = IsLiveFollowing
				? "Following Now"
				: ((IsAutoSnapping && !CanGoToNow()) ? "Tracking Now" : "Go to Now");
		}

		if (IsLiveFollowing)
		{
			// Still "Pause", not "Play": the panel IS playing — following the present is what playing means
			// here (§6.4), and the button must offer the action, not restate the state.
			_playPauseButton.Text = "Pause";
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
