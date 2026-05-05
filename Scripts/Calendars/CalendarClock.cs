using System;

public class CalendarClock
{
	public CalendarModel Calendar { get; }
	public DateTime CurrentDateTime { get; private set; }
	public bool IsRunning { get; set; } = true;
	public double SpeedMultiplier { get; set; } = 1.0;

	public CalendarClock(CalendarModel calendar, DateTime initialDateTime)
	{
		Calendar = calendar;
		CurrentDateTime = initialDateTime;
	}

	public void SetDateTime(DateTime dateTime)
	{
		CurrentDateTime = dateTime;
	}

	public void Tick(double deltaSeconds)
	{
		if (!IsRunning)
		{
			return;
		}

		double totalSeconds = deltaSeconds * SpeedMultiplier;
		CurrentDateTime = Calendar.AddSeconds(CurrentDateTime, totalSeconds);
	}
}
