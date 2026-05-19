using Godot;
using System;

public partial class CalendarTimeService : Node
{
	public DateTime CurrentLocalDateTime { get; private set; } = DateTime.Now;
	public DateTime ExplorerSelectedLocalDateTime { get; private set; } = DateTime.Now;
	public bool IsRunning { get; set; } = true;
	public double SpeedMultiplier { get; set; } = 1.0;

	public DateTime CurrentUtcDateTime => CurrentLocalDateTime.ToUniversalTime();

	public override void _Process(double delta)
	{
		if (!IsRunning)
		{
			return;
		}

		CurrentLocalDateTime = CurrentLocalDateTime.AddSeconds(delta * SpeedMultiplier);
	}

	public void SetLocalDateTime(DateTime localDateTime)
	{
		CurrentLocalDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local);
	}

	public void SetExplorerSelectedLocalDateTime(DateTime localDateTime)
	{
		ExplorerSelectedLocalDateTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Local);
	}

	public void SetNow()
	{
		SetLocalDateTime(DateTime.Now);
		SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
	}
}
