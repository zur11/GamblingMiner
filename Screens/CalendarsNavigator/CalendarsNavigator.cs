using Godot;
using System;
using System.Globalization;

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
		UpdatePresenters();
	}

	private void OnTimeSpeedSelected(long index)
	{
		if (_calendarTimeService == null)
		{
			return;
		}

		double selectedSpeed = _timeSpeedSelector.GetItemMetadata((int)index).AsDouble();
		_calendarTimeService.SpeedMultiplier = selectedSpeed;
		UpdatePresenters();
	}

	private void OnApplyDateTimePressed()
	{
		ValidateDayInput();

		DateTime selected = new(
			(int)_yearInput.Value,
			(int)_monthInput.Value,
			(int)_dayInput.Value,
			(int)_hourInput.Value,
			(int)_minuteInput.Value,
			(int)_secondInput.Value
		);

		_calendarTimeService?.SetLocalDateTime(selected);
		_calendarTimeService?.SetExplorerSelectedLocalDateTime(selected);
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
		GetTree().ChangeSceneToFile("res://Screens/DiceGame/DiceGame.tscn");
	}

	private void OnOpenHistoryExplorerPressed()
	{
		DateTime selected = GetCurrentLocalDateTime();
		_calendarTimeService?.SetExplorerSelectedLocalDateTime(selected);
		_calendarTimeService?.SetLocalDateTime(selected);
		GetTree().ChangeSceneToFile("res://Screens/BetsHistoryExplorer/BetsHistoryExplorer.tscn");
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
		AddSpeedOption("x1", 1.0);
		AddSpeedOption("x2", 2.0);
		AddSpeedOption("x3", 3.0);
		AddSpeedOption("x4", 4.0);

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
