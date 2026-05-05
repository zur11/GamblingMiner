using System;
using System.Collections.Generic;

public class CalendarDay
{
	public string CalendarId { get; }
	public int Year { get; }
	public int Month { get; }
	public int DayOfMonth { get; }
	public DayOfWeek DayOfWeek { get; }
	public Dictionary<string, string> Metadata { get; } = new();

	public CalendarDay(string calendarId, int year, int month, int dayOfMonth, DayOfWeek dayOfWeek)
	{
		CalendarId = calendarId;
		Year = year;
		Month = month;
		DayOfMonth = dayOfMonth;
		DayOfWeek = dayOfWeek;
	}
}
