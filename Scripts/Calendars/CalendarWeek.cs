using System;
using System.Collections.Generic;

public class CalendarWeek
{
	public string CalendarId { get; }
	public int Year { get; }
	public int WeekOfYear { get; }
	public CalendarDay StartDay { get; }
	public IReadOnlyList<CalendarDay> Days { get; }

	public CalendarWeek(string calendarId, int year, int weekOfYear, CalendarDay startDay, IReadOnlyList<CalendarDay> days)
	{
		CalendarId = calendarId;
		Year = year;
		WeekOfYear = weekOfYear;
		StartDay = startDay;
		Days = days;
	}
}
