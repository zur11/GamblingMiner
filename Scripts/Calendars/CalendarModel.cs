using System;

public abstract class CalendarModel
{
	public string Id { get; }
	public string DisplayName { get; }

	protected CalendarModel(string id, string displayName)
	{
		Id = id;
		DisplayName = displayName;
	}

	public abstract DateTime AddSeconds(DateTime source, double seconds);
	public abstract CalendarDay BuildDay(DateTime source);
	public abstract CalendarWeek BuildWeek(DateTime source);
	public abstract CalendarMonth BuildMonth(DateTime source);
}
