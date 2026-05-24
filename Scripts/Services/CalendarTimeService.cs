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

	public void EnsureGameEpochInitialized()
	{
		const string statePath = "user://calendar_state.json";
		if (!FileAccess.FileExists(statePath))
		{
			// Bitcoin genesis-era style game bootstrap date requested by design.
			DateTime bootstrapLocal = new DateTime(2009, 10, 3, 0, 0, 0, DateTimeKind.Local);
			SetLocalDateTime(bootstrapLocal);
			SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
			PersistCurrentTime();
			return;
		}

		using FileAccess file = FileAccess.Open(statePath, FileAccess.ModeFlags.Read);
		string value = file.GetAsText();
		if (!long.TryParse(value, out long ticks))
		{
			SetLocalDateTime(new DateTime(2009, 10, 3, 0, 0, 0, DateTimeKind.Local));
			PersistCurrentTime();
			return;
		}

		SetLocalDateTime(new DateTime(ticks, DateTimeKind.Local));
		SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
	}

	public void AdvanceSeconds(double seconds)
	{
		if (seconds <= 0d)
		{
			return;
		}

		CurrentLocalDateTime = CurrentLocalDateTime.AddSeconds(seconds);
	}

	public void PersistCurrentTime()
	{
		const string statePath = "user://calendar_state.json";
		using FileAccess file = FileAccess.Open(statePath, FileAccess.ModeFlags.Write);
		file.StoreString(CurrentLocalDateTime.Ticks.ToString());
	}
}
