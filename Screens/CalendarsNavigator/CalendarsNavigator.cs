using Godot;
using System;
using System.Globalization;
using UI.StatusBar;

public partial class CalendarsNavigator : Control
{
	private Label _dayPresenter;
	private Label _timePresenter;
	private Label _weekPresenter;
	private Label _monthPresenter;
	private CheckButton _hourFormatToggle;
	// Replay Mode, default ON: the calendar is limited to dates the bet journal can actually replay.
	// Turning it OFF is an explicit statement that the player wants to travel further back for reasons
	// other than bet history — the world still refuses to go before the player's own start.
	private CheckButton _replayModeToggle;
	private Label _replayWindowLabel;
	private UserStatsService _userStatsService;
	private OptionButton _timeSpeedSelector;
	private SpinBox _yearInput;
	private SpinBox _monthInput;
	private SpinBox _dayInput;
	private SpinBox _hourInput;
	private SpinBox _minuteInput;
	private SpinBox _secondInput;
	private Button _applyDateTimeButton;
	private Button _setNowButton;
	private Button _backToDiceGameButton;
	private Button _openHistoryExplorerButton;

	private GregorianCalendarModel _gregorianCalendar;
	private CalendarTimeService _calendarTimeService;
	private SceneManager _sceneManager;

	public override void _Ready()
	{
		_dayPresenter = GetNode<Label>("%DayPresenter");
		_timePresenter = GetNode<Label>("%TimePresenter");
		_weekPresenter = GetNode<Label>("%WeekPresenter");
		_monthPresenter = GetNode<Label>("%MonthPresenter");
		_hourFormatToggle = GetNode<CheckButton>("%HourFormatToggle");
		_replayModeToggle = GetNode<CheckButton>("%ReplayModeToggle");
		_replayWindowLabel = GetNode<Label>("%ReplayWindowLabel");
		_userStatsService = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		_timeSpeedSelector = GetNode<OptionButton>("%TimeSpeedSelector");
		_yearInput = GetNode<SpinBox>("%YearInput");
		_monthInput = GetNode<SpinBox>("%MonthInput");
		_dayInput = GetNode<SpinBox>("%DayInput");
		_hourInput = GetNode<SpinBox>("%HourInput");
		_minuteInput = GetNode<SpinBox>("%MinuteInput");
		_secondInput = GetNode<SpinBox>("%SecondInput");
		_applyDateTimeButton = GetNode<Button>("%ApplyDateTimeButton");
		_setNowButton = GetNode<Button>("%SetNowButton");
		_backToDiceGameButton = GetNode<Button>("%BackToDiceGameButton");
		_openHistoryExplorerButton = GetNode<Button>("%OpenHistoryExplorerButton");

		_gregorianCalendar = new GregorianCalendarModel();
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");
		_sceneManager = GetNodeOrNull<SceneManager>("/root/SceneManager");

		var rootVBox = GetNode<VBoxContainer>("RootMargin/RootVBox");
		var statusBar = new StatusBar();
		rootVBox.AddChild(statusBar);
		rootVBox.MoveChild(statusBar, 0);

		_hourFormatToggle.Toggled += _ => UpdatePresenters();
		_replayModeToggle.Toggled += OnReplayModeToggled;
		RefreshReplayWindowLabel();
		_timeSpeedSelector.ItemSelected += OnTimeSpeedSelected;
		_applyDateTimeButton.Pressed += OnApplyDateTimePressed;
		_setNowButton.Pressed += OnSetNowPressed;
		// The existing button actually goes to the Main Menu — relabel it and add a real "Go to DiceGame".
		_backToDiceGameButton.Text = "Go to Main Menu";
		_backToDiceGameButton.Pressed += OnGoToMainMenuPressed;
		var goToDiceButton = new Button { Text = "Go to DiceGame" };
		goToDiceButton.Pressed += OnGoToDiceGamePressed;
		_backToDiceGameButton.GetParent().AddChild(goToDiceButton);
		_backToDiceGameButton.GetParent().MoveChild(goToDiceButton, _backToDiceGameButton.GetIndex() + 1);
		_openHistoryExplorerButton.Pressed += OnOpenHistoryExplorerPressed;
		_yearInput.ValueChanged += _ => ValidateDayInput();
		_monthInput.ValueChanged += _ => ValidateDayInput();

		SyncInputsFromClock();
		InitializeTimeSpeedSelector();
		UpdatePresenters();
	}

	public override void _Process(double delta)
	{
		if (!Visible) return;
		if (_calendarTimeService?.IsRunning == true && !(_calendarTimeService?.IsAutobetActive ?? false))
		{
			DateTime present = _calendarTimeService.GamePresentLocalDateTime;
			if (_calendarTimeService.CurrentLocalDateTime >= present)
			{
				_calendarTimeService.SetLocalDateTime(present);
				_calendarTimeService.IsRunning = false;
				SyncInputsFromClock();
			}
		}
		UpdatePresenters();
	}

	private void OnTimeSpeedSelected(long index)
	{
		if (_calendarTimeService == null)
			return;

		double selectedSpeed = _timeSpeedSelector.GetItemMetadata((int)index).AsDouble();
		if (_calendarTimeService.IsAutobetActive)
		{
			double x1Speed = _timeSpeedSelector.ItemCount > 0
				? _timeSpeedSelector.GetItemMetadata(0).AsDouble() : 100d;
			if (selectedSpeed > x1Speed)
			{
				_calendarTimeService.SpeedMultiplier = x1Speed;
				_timeSpeedSelector.Select(0);
			}
			else
			{
				_calendarTimeService.SpeedMultiplier = selectedSpeed;
			}
		}
		else
		{
			_calendarTimeService.SpeedMultiplier = selectedSpeed;
		}
		UpdatePresenters();
	}

	// Superseded by EffectiveFloorLocal (mini-plan 03 §6.11) and deliberately removed rather than left
	// unused: it was a SECOND hardcoded copy of the genesis date, and the canonical anchor now used —
	// TimelineConfig.PlayerStartDayLocal — is timeline-shiftable, which a literal never is. Every historical
	// date anchor in this project routes through TimelineConfig for exactly that reason (D-13.x).

	private void OnApplyDateTimePressed()
	{
		ValidateDayInput();

		DateTime selected = new(
			(int)_yearInput.Value,
			(int)_monthInput.Value,
			(int)_dayInput.Value,
			(int)_hourInput.Value,
			(int)_minuteInput.Value,
			(int)_secondInput.Value,
			DateTimeKind.Local
		);

		DateTime gamePresent = _calendarTimeService?.GamePresentLocalDateTime ?? selected;
		// Replaces a hardcoded 2009-01-03 genesis floor. That constant let the clock be set into the
		// FOUNDERS' era, months before the player's world exists — and with Replay Mode on it is further
		// raised to the oldest stored bet, so every date the calendar accepts is one the explorer can
		// actually replay (mini-plan 03 §6.11).
		DateTime floor = EffectiveFloorLocal();
		if (selected < floor) selected = floor;
		if (selected > gamePresent) selected = gamePresent;

		_calendarTimeService?.SetLocalDateTime(selected);
		_calendarTimeService?.SetExplorerSelectedLocalDateTime(selected);
		SyncInputsFromClock();
		UpdatePresenters();
	}

	private void OnSetNowPressed()
	{
		_calendarTimeService?.SetNow();
		SyncInputsFromClock();
		UpdatePresenters();
	}

	// This button goes to the Main Menu. The background sim is an autoload and survives scene changes, so
	// we always navigate normally — the old overlay/PopOverlay path trapped the user when autobet was active
	// but this scene wasn't actually an overlay (e.g. reached from the Main Menu).
	private void OnGoToMainMenuPressed()
	{
		_sceneManager?.Go(SceneManager.SceneId.MainMenu);
	}

	private void OnGoToDiceGamePressed()
	{
		_sceneManager?.Go(SceneManager.SceneId.DiceGame);
	}

	// The oldest bet still on disk, or null when the journal is empty. This is the earliest instant a
	// bet REPLAY can show anything (mini-plan 03 §6.6).
	private DateTime? ReplayFloorLocal()
	{
		DateTime? utc = _userStatsService?.GetOldestRetainedBetUtc();
		return utc?.ToLocalTime();
	}

	// The floor the calendar enforces, which depends on what the player asked for:
	//   Replay Mode ON  → the oldest stored bet: every date offered can actually be replayed.
	//   Replay Mode OFF → the player's own start: they have said they want to travel for some reason
	//                     other than bet history, and the world simply has nothing before that instant.
	// Off-mode still clamps, and immediately — an unbounded calendar would let the clock wander into the
	// founders' era, where none of this world's player state exists.
	private DateTime EffectiveFloorLocal()
	{
		DateTime worldFloor = TimelineConfig.PlayerStartDayLocal;
		if (_replayModeToggle?.ButtonPressed != true)
		{
			return worldFloor;
		}

		DateTime? replayFloor = ReplayFloorLocal();
		// With Replay Mode on but nothing recorded yet, the replay floor would be meaningless — fall back
		// to the world floor rather than pinning the player to "now".
		return replayFloor.HasValue && replayFloor.Value > worldFloor ? replayFloor.Value : worldFloor;
	}

	private void RefreshReplayWindowLabel()
	{
		if (_replayWindowLabel == null)
		{
			return;
		}

		DateTime? replayFloor = ReplayFloorLocal();
		string floorText = replayFloor.HasValue
			? replayFloor.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
			: "no bets recorded yet";

		// Always state BOTH bounds, whichever mode is on: the point of the label is to answer "how far
		// back can I go?", and that answer changes with the toggle right beside it.
		_replayWindowLabel.Text = _replayModeToggle?.ButtonPressed == true
			? $"Bet replays available from: {floorText}   (Replay Mode ON — the calendar stops here)"
			: string.Create(CultureInfo.InvariantCulture,
				$"Bet replays available from: {floorText}   (Replay Mode OFF — the calendar may go back to " +
				$"{TimelineConfig.PlayerStartDayLocal:yyyy-MM-dd HH:mm:ss}, with no bets to show before the date above)");
	}

	private void OnReplayModeToggled(bool _)
	{
		RefreshReplayWindowLabel();

		// Re-apply the floor at once, so flipping the toggle can never leave the calendar sitting on a
		// date the new mode forbids.
		DateTime floor = EffectiveFloorLocal();
		if (GetCurrentLocalDateTime() < floor)
		{
			_calendarTimeService?.SetLocalDateTime(floor);
			_calendarTimeService?.SetExplorerSelectedLocalDateTime(floor);
			SyncInputsFromClock();
			UpdatePresenters();
		}
	}

	private void OnOpenHistoryExplorerPressed()
	{
		DateTime selected = GetCurrentLocalDateTime();

		// Clamp at BOTH ends of the trip. Clamping only on arrival means the player watches the date they
		// chose change under them; correcting at the point of departure is the same fix delivered before
		// it looks like a malfunction. The explorer clamps to the replay floor regardless of this toggle —
		// it cannot show bets that are not on disk — so with Replay Mode OFF the player may legitimately
		// arrive at an earlier date and see the explorer snap forward. That is the mode working, not a bug.
		DateTime? floorLocal = ReplayFloorLocal();
		if (floorLocal.HasValue && selected < floorLocal.Value)
		{
			selected = floorLocal.Value;
		}

		_calendarTimeService?.SetExplorerSelectedLocalDateTime(selected);
		_calendarTimeService?.SetLocalDateTime(selected);
		_sceneManager?.Go(SceneManager.SceneId.BetsHistoryExplorer);
	}

	private void SyncInputsFromClock()
	{
		DateTime now = GetCurrentLocalDateTime();
		_yearInput.Value = now.Year;
		_monthInput.Value = now.Month;
		_dayInput.Value = now.Day;
		_hourInput.Value = now.Hour;
		_minuteInput.Value = now.Minute;
		_secondInput.Value = now.Second;
	}

	private void ValidateDayInput()
	{
		int year = Math.Clamp((int)_yearInput.Value, 1, 9999);
		int month = Math.Clamp((int)_monthInput.Value, 1, 12);
		int maxDay = DateTime.DaysInMonth(year, month);
		_dayInput.MaxValue = maxDay;
		if (_dayInput.Value > maxDay)
		{
			_dayInput.Value = maxDay;
		}
	}

	private void UpdatePresenters()
	{
		DateTime current = GetCurrentLocalDateTime();
		CalendarDay day = _gregorianCalendar.BuildDay(current);
		CalendarWeek week = _gregorianCalendar.BuildWeek(current);
		CalendarMonth month = _gregorianCalendar.BuildMonth(current);

		_dayPresenter.Text = $"{day.DayOfWeek}, {day.DayOfMonth:D2}/{day.Month:D2}/{day.Year}";
		_timePresenter.Text = _hourFormatToggle.ButtonPressed
			? current.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
			: current.ToString("h:mm:ss tt", CultureInfo.InvariantCulture);

		if (_calendarTimeService != null)
		{
			_timePresenter.Text += string.Create(CultureInfo.InvariantCulture, $"  |  Speed x{_calendarTimeService.SpeedMultiplier:0.##}");
		}

		_weekPresenter.Text = $"ISO Week: {week.WeekOfYear} (Monday to Sunday)";
		_monthPresenter.Text = $"Month: {month.MonthName} ({month.Days.Count} days)";
	}

	private void InitializeTimeSpeedSelector()
	{
		_timeSpeedSelector.Clear();
		AddSpeedOption("x1", 100.0);
		AddSpeedOption("x2", 200.0);
		AddSpeedOption("x4", 400.0);
		AddSpeedOption("x10", 1000.0);

		double currentSpeed = _calendarTimeService?.SpeedMultiplier ?? 1.0;
		int selectedIndex = FindBestSpeedIndex(currentSpeed);
		_timeSpeedSelector.Select(selectedIndex);

		if (_calendarTimeService != null)
		{
			_calendarTimeService.SpeedMultiplier = _timeSpeedSelector.GetItemMetadata(selectedIndex).AsDouble();
		}
	}

	private void AddSpeedOption(string label, double speed)
	{
		int index = _timeSpeedSelector.ItemCount;
		_timeSpeedSelector.AddItem(label);
		_timeSpeedSelector.SetItemMetadata(index, speed);
	}

	private int FindBestSpeedIndex(double speed)
	{
		int bestIndex = 0;
		double bestDistance = double.MaxValue;
		for (int i = 0; i < _timeSpeedSelector.ItemCount; i++)
		{
			double option = _timeSpeedSelector.GetItemMetadata(i).AsDouble();
			double distance = Math.Abs(option - speed);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestIndex = i;
			}
		}

		return bestIndex;
	}

	private DateTime GetCurrentLocalDateTime()
	{
		return _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
	}
}
