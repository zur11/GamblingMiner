using System.Collections.Generic;

public class CalendarMonth
{
	public string CalendarId { get; }
	public int Year { get; }
	public int MonthNumber { get; }
	public string MonthName { get; }
	public IReadOnlyList<CalendarDay> Days { get; }

	public CalendarMonth(string calendarId, int year, int monthNumber, string monthName, IReadOnlyList<CalendarDay> days)
	{
		CalendarId = calendarId;
		Year = year;
		MonthNumber = monthNumber;
		MonthName = monthName;
		Days = days;
	}
}
