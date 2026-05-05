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
	private SpinBox _yearInput;
	private SpinBox _monthInput;
	private SpinBox _dayInput;
	private SpinBox _hourInput;
	private SpinBox _minuteInput;
	private Button _applyDateTimeButton;
	private Button _setNowButton;

	private GregorianCalendarModel _gregorianCalendar;
	private CalendarClock _clock;

	public override void _Ready()
	{
		_dayPresenter = GetNode<Label>("%DayPresenter");
		_timePresenter = GetNode<Label>("%TimePresenter");
		_weekPresenter = GetNode<Label>("%WeekPresenter");
		_monthPresenter = GetNode<Label>("%MonthPresenter");
		_hourFormatToggle = GetNode<CheckButton>("%HourFormatToggle");
		_yearInput = GetNode<SpinBox>("%YearInput");
		_monthInput = GetNode<SpinBox>("%MonthInput");
		_dayInput = GetNode<SpinBox>("%DayInput");
		_hourInput = GetNode<SpinBox>("%HourInput");
		_minuteInput = GetNode<SpinBox>("%MinuteInput");
		_applyDateTimeButton = GetNode<Button>("%ApplyDateTimeButton");
		_setNowButton = GetNode<Button>("%SetNowButton");

		_gregorianCalendar = new GregorianCalendarModel();
		_clock = new CalendarClock(_gregorianCalendar, DateTime.Now);

		_hourFormatToggle.Toggled += OnHourFormatToggled;
		_applyDateTimeButton.Pressed += OnApplyDateTimePressed;
		_setNowButton.Pressed += OnSetNowPressed;
		_yearInput.ValueChanged += _ => ValidateDayInput();
		_monthInput.ValueChanged += _ => ValidateDayInput();

		SyncInputsFromClock();
		UpdatePresenters();
	}

	public override void _Process(double delta)
	{
		_clock.Tick(delta);
		UpdatePresenters();
	}

	private void OnHourFormatToggled(bool toggledOn)
	{
		UpdatePresenters();
	}

	private void OnApplyDateTimePressed()
	{
		ValidateDayInput();

		int year = (int)_yearInput.Value;
		int month = (int)_monthInput.Value;
		int day = (int)_dayInput.Value;
		int hour = (int)_hourInput.Value;
		int minute = (int)_minuteInput.Value;

		DateTime selected = new(year, month, day, hour, minute, 0);
		_clock.SetDateTime(selected);
		UpdatePresenters();
	}

	private void OnSetNowPressed()
	{
		_clock.SetDateTime(DateTime.Now);
		SyncInputsFromClock();
		UpdatePresenters();
	}

	private void SyncInputsFromClock()
	{
		DateTime now = _clock.CurrentDateTime;
		_yearInput.Value = now.Year;
		_monthInput.Value = now.Month;
		_dayInput.Value = now.Day;
		_hourInput.Value = now.Hour;
		_minuteInput.Value = now.Minute;
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
		DateTime current = _clock.CurrentDateTime;
		CalendarDay day = _gregorianCalendar.BuildDay(current);
		CalendarWeek week = _gregorianCalendar.BuildWeek(current);
		CalendarMonth month = _gregorianCalendar.BuildMonth(current);

		_dayPresenter.Text = $"{day.DayOfWeek}, {day.DayOfMonth:D2}/{day.Month:D2}/{day.Year}";
		_timePresenter.Text = _hourFormatToggle.ButtonPressed
			? current.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
			: current.ToString("h:mm:ss tt", CultureInfo.InvariantCulture);
		_weekPresenter.Text = $"Semana ISO: {week.WeekOfYear} (Lunes a Domingo)";
		_monthPresenter.Text = $"Mes: {month.MonthName} ({month.Days.Count} dias)";
	}
}
