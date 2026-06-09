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
	private const int MaxPreviewEntries = 260;

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
	private readonly double[] _speedSteps = { 48d, 96d, 192d, 480d };
	private List<BetRecord> _sortedRecords = new();
	private long _lastRenderedSecond = long.MinValue;
	private int _summaryCursor;
	private int _summaryTotalBets;
	private decimal _summaryMaxBetAmount;
	private decimal _summaryMaxLossAmount;
	private int _summaryConsecutiveLosses;
	private int _summaryMartingaleLevel;

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

	private void OnLiveStatsChanged(UserBettingStats _)
	{
		if (_userStatsService?.BetHistory == null) return;
		_sortedRecords = _userStatsService.BetHistory.Records
			.OrderBy(r => r.TimestampUtc)
			.ToList();
		_lastRenderedSecond = long.MinValue;
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
		_selectedTimeLabel.Text = $"Selected timeline: {current:yyyy-MM-dd HH:mm:ss}";

		if (_sortedRecords.Count <= 0)
		{
			return;
		}

		long currentSecond = new DateTimeOffset(current).ToUnixTimeSeconds();
		if (currentSecond == _lastRenderedSecond)
		{
			return;
		}

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
		_sortedRecords = _userStatsService.BetHistory.Records
			.OrderBy(r => r.TimestampUtc)
			.ToList();
		_loaderProgress.Value = 35;

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		_loaderLabel.Text = "Computing full summaries...";
		_loaderProgress.Value = 70;
		_summaryCursor = 0;
		_summaryTotalBets = 0;
		_summaryMaxBetAmount = 0m;
		_summaryMaxLossAmount = 0m;
		_summaryConsecutiveLosses = 0;
		_summaryMartingaleLevel = 0;
		RefreshHistoricalViewForCurrentTime(GetCurrentLocal().ToUniversalTime(), forceRebuild: true);

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		_loaderProgress.Value = 100;
		_loaderLabel.Text = "History ready";
		_loaderPanel.Visible = false;
		_contentPanel.Visible = true;
	}

	private void RefreshHistoricalViewForCurrentTime(DateTime currentUtc, bool forceRebuild = false)
	{
		if (_sortedRecords.Count <= 0)
		{
			_summaryLabel.Text = "No bets available up to selected date.";
			_betHistoryContainer.ClearEntries();
			_previousWinnerNumbersGrid.ClearEntries();
			return;
		}

		int endExclusive = UpperBound(_sortedRecords, currentUtc);
		int start = Math.Max(0, endExclusive - MaxPreviewEntries);
		List<BetRecord> preview = _sortedRecords.GetRange(start, endExclusive - start);
		_previousWinnerNumbersGrid.LoadFromHistoricalRecords(preview);
		_betHistoryContainer.LoadFromHistoricalRecords(preview);

		AdvanceSummaryTo(endExclusive, forceRebuild);
		_summaryLabel.Text = string.Format(
			CultureInfo.InvariantCulture,
			"Bets up to selected date: {0} | Max bet amount: {1:F8} | Max loss amount: {2:F8} | Martingale level reached: {3}",
			_summaryTotalBets,
			_summaryMaxBetAmount,
			_summaryMaxLossAmount,
			_summaryMartingaleLevel
		);
	}

	private void AdvanceSummaryTo(int endExclusive, bool forceRebuild)
	{
		if (forceRebuild || endExclusive < _summaryCursor)
		{
			_summaryCursor = 0;
			_summaryTotalBets = 0;
			_summaryMaxBetAmount = 0m;
			_summaryMaxLossAmount = 0m;
			_summaryConsecutiveLosses = 0;
			_summaryMartingaleLevel = 0;
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

			if (record.Outcome == BetOutcome.Loss)
			{
				_summaryConsecutiveLosses++;
				_summaryMartingaleLevel = Math.Max(_summaryMartingaleLevel, _summaryConsecutiveLosses);
				continue;
			}

			if (record.Outcome == BetOutcome.Win)
			{
				if (_summaryConsecutiveLosses > 0)
				{
					_summaryMartingaleLevel = Math.Max(_summaryMartingaleLevel, _summaryConsecutiveLosses + 1);
				}

				_summaryConsecutiveLosses = 0;
			}
		}

		_summaryCursor = endExclusive;
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

	private void OnBackToCalendarPressed()
	{
		if (_liveMode)
		{
			_sceneManager?.PopOverlay();
			return;
		}
		if (_calendarTimeService != null)
			_calendarTimeService.IsRunning = false;
		_sceneManager?.Go(SceneManager.SceneId.CalendarsNavigator);
	}

	private void OnBackToDicePressed()
	{
		if (_liveMode)
		{
			_sceneManager?.PopAllOverlays();
			return;
		}
		if (_calendarTimeService != null)
		{
			_calendarTimeService.IsRunning = false;
			_calendarTimeService.SetNow();
		}
		_sceneManager?.Go(SceneManager.SceneId.DiceGame);
	}

	private const double GameBaseSpeed = 48.0;

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
		_speedButton.Text = $"Speed {speedX:0.##}x";
	}

	private DateTime GetCurrentLocal()
	{
		return _calendarTimeService?.CurrentLocalDateTime ?? _selectedLocal;
	}
}
