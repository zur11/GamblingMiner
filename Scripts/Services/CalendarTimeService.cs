using Godot;
using System;

public partial class CalendarTimeService : Node
{
	private static readonly DateTime GameStartLocal = new DateTime(2009, 1, 3, 18, 15, 6, DateTimeKind.Local);
	private static readonly DateTime LegacyStartLocal = new DateTime(2009, 10, 3, 0, 0, 0, DateTimeKind.Local);

	public DateTime CurrentLocalDateTime { get; private set; } = DateTime.Now;
	public DateTime ExplorerSelectedLocalDateTime { get; private set; } = DateTime.Now;
	public bool IsRunning { get; set; } = false;
	public bool IsAutobetActive { get; set; } = false;
	public double SpeedMultiplier { get; set; } = 1.0;

	private DateTime _gamePresent = DateTime.Now;
	public DateTime GamePresentLocalDateTime => _gamePresent;

	public DateTime CurrentUtcDateTime => CurrentLocalDateTime.ToUniversalTime();

	public override void _Ready()
	{
		EnsureGameEpochInitialized();
	}

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
		SetLocalDateTime(_gamePresent);
		SetExplorerSelectedLocalDateTime(_gamePresent);
	}

	public void EnsureGameEpochInitialized()
	{
		const string statePath = "user://calendar_state.json";
		if (!FileAccess.FileExists(statePath))
		{
			SetLocalDateTime(GameStartLocal);
			SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
			_gamePresent = CurrentLocalDateTime;
			PersistCurrentTime();
			return;
		}

		using FileAccess file = FileAccess.Open(statePath, FileAccess.ModeFlags.Read);
		string value = file.GetAsText();
		if (!long.TryParse(value, out long ticks))
		{
			SetLocalDateTime(GameStartLocal);
			_gamePresent = CurrentLocalDateTime;
			PersistCurrentTime();
			return;
		}

		DateTime loaded = new DateTime(ticks, DateTimeKind.Local);
		// Migrate legacy bootstrap values to the updated genesis-adjacent start.
		if (loaded == LegacyStartLocal || loaded == new DateTime(2009, 1, 3, 12, 0, 0, DateTimeKind.Local))
		{
			loaded = GameStartLocal;
			SetLocalDateTime(loaded);
			SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
			_gamePresent = CurrentLocalDateTime;
			PersistCurrentTime();
			return;
		}

		SetLocalDateTime(loaded);
		SetExplorerSelectedLocalDateTime(CurrentLocalDateTime);
		_gamePresent = CurrentLocalDateTime;
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
		_gamePresent = CurrentLocalDateTime;
		const string statePath = "user://calendar_state.json";
		using FileAccess file = FileAccess.Open(statePath, FileAccess.ModeFlags.Write);
		file.StoreString(CurrentLocalDateTime.Ticks.ToString());
	}
}
