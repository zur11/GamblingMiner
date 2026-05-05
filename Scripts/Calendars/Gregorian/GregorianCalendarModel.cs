using System;
using System.Collections.Generic;
using System.Globalization;

public class GregorianCalendarModel : CalendarModel
{
	private static readonly GregorianCalendar Gregorian = new();

	public GregorianCalendarModel() : base("gregorian", "Gregorian")
	{
	}

	public override DateTime AddSeconds(DateTime source, double seconds)
	{
		return source.AddSeconds(seconds);
	}

	public override CalendarDay BuildDay(DateTime source)
	{
		return new CalendarDay(Id, source.Year, source.Month, source.Day, source.DayOfWeek);
	}

	public override CalendarWeek BuildWeek(DateTime source)
	{
		DateTime monday = GetMonday(source);
		var days = new List<CalendarDay>(7);
		for (int i = 0; i < 7; i++)
		{
			days.Add(BuildDay(monday.AddDays(i)));
		}

		int weekOfYear = ISOWeek.GetWeekOfYear(source);
		int weekYear = ISOWeek.GetYear(source);
		return new CalendarWeek(Id, weekYear, weekOfYear, BuildDay(monday), days);
	}

	public override CalendarMonth BuildMonth(DateTime source)
	{
		int daysInMonth = Gregorian.GetDaysInMonth(source.Year, source.Month);
		var days = new List<CalendarDay>(daysInMonth);
		for (int day = 1; day <= daysInMonth; day++)
		{
			DateTime current = new(source.Year, source.Month, day);
			days.Add(BuildDay(current));
		}

		string monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(source.Month);
		return new CalendarMonth(Id, source.Year, source.Month, monthName, days);
	}

	private static DateTime GetMonday(DateTime source)
	{
		int offset = ((int)source.DayOfWeek + 6) % 7;
		return source.Date.AddDays(-offset);
	}
}
