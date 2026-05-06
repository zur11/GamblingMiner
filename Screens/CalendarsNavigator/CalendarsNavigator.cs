using Godot;
using System;
using System.Globalization;
using Scripts.History;
using System.Collections.Generic;
using System.Linq;

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
	private Label _daySummaryPresenter;
	private Label _hourSummaryPresenter;
	private Label _minuteSummaryPresenter;
	private Label _balanceAtTimePresenter;
	private Label _statsAtTimePresenter;
	private ItemList _dayBetsList;
	private Label _betListTitle;

	private GregorianCalendarModel _gregorianCalendar;
	private UserStatsService _userStatsService;
	private CalendarTimeService _calendarTimeService;
	private double _summaryRefreshAccumulator;

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
		_daySummaryPresenter = GetNode<Label>("%DaySummaryPresenter");
		_hourSummaryPresenter = GetNode<Label>("%HourSummaryPresenter");
		_minuteSummaryPresenter = GetNode<Label>("%MinuteSummaryPresenter");
		_balanceAtTimePresenter = GetNode<Label>("%BalanceAtTimePresenter");
		_statsAtTimePresenter = GetNode<Label>("%StatsAtTimePresenter");
		_dayBetsList = GetNode<ItemList>("%DayBetsList");
		_betListTitle = GetNode<Label>("%BetListTitle");

		_gregorianCalendar = new GregorianCalendarModel();
		_userStatsService = GetNodeOrNull<UserStatsService>("/root/UserStatsService");
		_calendarTimeService = GetNodeOrNull<CalendarTimeService>("/root/CalendarTimeService");

		_hourFormatToggle.Toggled += OnHourFormatToggled;
		_timeSpeedSelector.ItemSelected += OnTimeSpeedSelected;
		_applyDateTimeButton.Pressed += OnApplyDateTimePressed;
		_setNowButton.Pressed += OnSetNowPressed;
		_backToDiceGameButton.Pressed += OnBackToDiceGamePressed;
		_yearInput.ValueChanged += _ => ValidateDayInput();
		_monthInput.ValueChanged += _ => ValidateDayInput();

		if (_calendarTimeService == null)
		{
			GD.PushWarning("CalendarTimeService not found. Falling back to local DateTime.Now behavior.");
		}

		SyncInputsFromClock();
		InitializeTimeSpeedSelector();
		UpdatePresenters();
		UpdateHistoryPanels();
	}

	public override void _Process(double delta)
	{
		UpdatePresenters();

		_summaryRefreshAccumulator += delta;
		if (_summaryRefreshAccumulator >= 0.5d)
		{
			_summaryRefreshAccumulator = 0d;
			UpdateHistoryPanels();
		}
	}

	private void OnHourFormatToggled(bool toggledOn)
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

		int year = (int)_yearInput.Value;
		int month = (int)_monthInput.Value;
		int day = (int)_dayInput.Value;
		int hour = (int)_hourInput.Value;
		int minute = (int)_minuteInput.Value;
		int second = (int)_secondInput.Value;

		DateTime selected = new(year, month, day, hour, minute, second);
		_calendarTimeService?.SetLocalDateTime(selected);
		UpdatePresenters();
		UpdateHistoryPanels();
	}

	private void OnSetNowPressed()
	{
		_calendarTimeService?.SetNow();
		SyncInputsFromClock();
		UpdatePresenters();
		UpdateHistoryPanels();
	}

	private void OnBackToDiceGamePressed()
	{
		GetTree().ChangeSceneToFile("res://Screens/DiceGame/DiceGame.tscn");
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
		AddSpeedOption("x0.25", 0.25);
		AddSpeedOption("x0.5", 0.5);
		AddSpeedOption("x1", 1.0);
		AddSpeedOption("x2", 2.0);
		AddSpeedOption("x4", 4.0);
		AddSpeedOption("x8", 8.0);

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

	private void UpdateHistoryPanels()
	{
		if (_userStatsService == null)
		{
			_daySummaryPresenter.Text = "Day summary: UserStatsService not found";
			_hourSummaryPresenter.Text = "Hour summary: UserStatsService not found";
			_minuteSummaryPresenter.Text = "Minute summary: UserStatsService not found";
			_balanceAtTimePresenter.Text = "Balance at selected time: unavailable";
			_statsAtTimePresenter.Text = "Stats up to selected time: unavailable";
			_betListTitle.Text = "Bets in selected day (0)";
			_dayBetsList.Clear();
			return;
		}

		DateTime selectedLocal = GetCurrentLocalDateTime();
		IReadOnlyList<BetRecord> dayBets = _userStatsService.GetBetsForCalendarDay(selectedLocal, TimeZoneInfo.Local);

		DateTime dayStart = selectedLocal.Date;
		DateTime dayEnd = dayStart.AddDays(1);
		DateTime hourStart = new DateTime(selectedLocal.Year, selectedLocal.Month, selectedLocal.Day, selectedLocal.Hour, 0, 0);
		DateTime hourEnd = hourStart.AddHours(1);
		DateTime minuteStart = new DateTime(selectedLocal.Year, selectedLocal.Month, selectedLocal.Day, selectedLocal.Hour, selectedLocal.Minute, 0);
		DateTime minuteEnd = minuteStart.AddMinutes(1);

		var daySummary = BuildSummary(dayBets, dayStart, dayEnd);
		var hourSummary = BuildSummary(dayBets, hourStart, hourEnd);
		var minuteSummary = BuildSummary(dayBets, minuteStart, minuteEnd);

		_daySummaryPresenter.Text = $"Day summary: Bets {daySummary.TotalBets} | Wins {daySummary.Wins} | Losses {daySummary.Losses} | Net profit {FormatSignedAmount(daySummary.NetProfit)}";
		_hourSummaryPresenter.Text = $"Hour summary: Bets {hourSummary.TotalBets} | Wins {hourSummary.Wins} | Losses {hourSummary.Losses} | Net profit {FormatSignedAmount(hourSummary.NetProfit)}";
		_minuteSummaryPresenter.Text = $"Minute summary: Bets {minuteSummary.TotalBets} | Wins {minuteSummary.Wins} | Losses {minuteSummary.Losses} | Net profit {FormatSignedAmount(minuteSummary.NetProfit)}";
		decimal balanceAtTime = _userStatsService.GetBalanceAtOrBefore(selectedLocal, TimeZoneInfo.Local);
		TimeBasedBetStats statsAtTime = _userStatsService.GetStatsUpTo(selectedLocal, TimeZoneInfo.Local);
		_balanceAtTimePresenter.Text = $"Balance at selected time: {balanceAtTime:F8}";
		_statsAtTimePresenter.Text =
			$"Stats up to selected time: Bets {statsAtTime.TotalBets} | Wins {statsAtTime.Wins} | Losses {statsAtTime.Losses} | Wagered {statsAtTime.TotalWagered:F8} | Net profit {FormatSignedAmount(statsAtTime.NetProfit)}";

		_betListTitle.Text = $"Bets in selected day ({daySummary.TotalBets})";
		_dayBetsList.Clear();

		foreach (BetRecord bet in dayBets)
		{
			DateTime local = TimeZoneInfo.ConvertTimeFromUtc(bet.TimestampUtc, TimeZoneInfo.Local);
			string sign = bet.NetAmount >= 0m ? "+" : string.Empty;
			string line = $"{local:HH:mm:ss} | {bet.GameId} | {bet.Outcome} | {sign}{bet.NetAmount:F8}";
			_dayBetsList.AddItem(line);
		}
	}

	private static (int TotalBets, int Wins, int Losses, decimal NetProfit) BuildSummary(
		IReadOnlyList<BetRecord> records,
		DateTime startLocalInclusive,
		DateTime endLocalExclusive)
	{
		var inRange = records.Where(record =>
		{
			DateTime local = TimeZoneInfo.ConvertTimeFromUtc(record.TimestampUtc, TimeZoneInfo.Local);
			return local >= startLocalInclusive && local < endLocalExclusive;
		}).ToList();

		int total = inRange.Count;
		int wins = inRange.Count(record => record.Outcome == BetOutcome.Win);
		int losses = total - wins;
		decimal net = inRange.Sum(record => record.NetAmount);
		return (total, wins, losses, net);
	}

	private static string FormatSignedAmount(decimal amount)
	{
		string sign = amount >= 0m ? "+" : string.Empty;
		return $"{sign}{amount:F8}";
	}

	private DateTime GetCurrentLocalDateTime()
	{
		return _calendarTimeService?.CurrentLocalDateTime ?? DateTime.Now;
	}
}
