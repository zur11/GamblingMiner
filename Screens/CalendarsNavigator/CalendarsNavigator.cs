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
		_timeSpeedSelector.ItemSelected += OnTimeSpeedSelected;
		_applyDateTimeButton.Pressed += OnApplyDateTimePressed;
		_setNowButton.Pressed += OnSetNowPressed;
		_backToDiceGameButton.Pressed += OnBackToDiceGamePressed;
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
				? _timeSpeedSelector.GetItemMetadata(0).AsDouble() : 48d;
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

	private static readonly DateTime GameEpochLocal = new(2009, 1, 3, 18, 15, 6, DateTimeKind.Local);

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
		if (selected < GameEpochLocal) selected = GameEpochLocal;
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

	private void OnBackToDiceGamePressed()
	{
		if (_calendarTimeService?.IsAutobetActive == true)
			_sceneManager?.PopOverlay();
		else
			_sceneManager?.Go(SceneManager.SceneId.MainMenu);
	}

	private void OnOpenHistoryExplorerPressed()
	{
		DateTime selected = GetCurrentLocalDateTime();
		_calendarTimeService?.SetExplorerSelectedLocalDateTime(selected);
		_calendarTimeService?.SetLocalDateTime(selected);
		if (_calendarTimeService?.IsAutobetActive == true && _sceneManager != null)
		{
			Visible = false;
			Node overlay = _sceneManager.PushScene(SceneManager.SceneId.BetsHistoryExplorer);
			overlay.TreeExited += () => { if (IsInsideTree()) Visible = true; };
		}
		else
		{
			_sceneManager?.Go(SceneManager.SceneId.BetsHistoryExplorer);
		}
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
			_timePresenter.Text += $"  |  Speed x{_calendarTimeService.SpeedMultiplier:0.##}";
		}

		_weekPresenter.Text = $"ISO Week: {week.WeekOfYear} (Monday to Sunday)";
		_monthPresenter.Text = $"Month: {month.MonthName} ({month.Days.Count} days)";
	}

	private void InitializeTimeSpeedSelector()
	{
		_timeSpeedSelector.Clear();
		AddSpeedOption("x1", 48.0);
		AddSpeedOption("x2", 96.0);
		AddSpeedOption("x4", 192.0);
		AddSpeedOption("x10", 480.0);

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
