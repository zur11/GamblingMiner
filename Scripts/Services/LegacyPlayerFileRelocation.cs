using Godot;
using Scripts.Betting;
using Scripts.Hardware;
using System;
using System.IO;
#nullable enable

// Mini-plan 27 unit 3 (D-27.2 = a) — moves the three player files that existed before user://player/ into it, ONCE,
// by a rename of the same bytes: not a format migration, so nothing is parsed and nothing can be lost in a rewrite.
//
// Runs from WorldGuardService, before the world guard and before any other autoload, because the hardware file is
// loaded by CalendarTimeService (#3) and must be read from its new home.
//
// ⚠ DELETE THIS FILE, and its call in WorldGuardService, at the close-out of the plan after next (plan §3.4): by
// then every world that had the old files has launched once. The old paths below are its only copies.
public static class LegacyPlayerFileRelocation
{
	private static readonly (string From, string To)[] Moves =
	{
		("user://saved_betting_strategies.json", SavedBettingStrategyRepository.DefaultPath),
		("user://notepad_notes.json", NotepadService.SavePath),
		("user://hardware_allocation.json", HardwareAllocationRepository.SavePath),
	};

	public static void Run()
	{
		foreach ((string from, string to) in Moves)
		{
			// Absent is the normal case after the first launch, and notepad_notes.json never existed on the
			// developer's machine (plan S6): nothing to move is not an error, and prints nothing.
			if (!Godot.FileAccess.FileExists(from))
			{
				continue;
			}

			// Both present: never choose between them silently, and never overwrite either. The new one is in use;
			// the old one stays where it is, untouched, for a human to decide.
			if (Godot.FileAccess.FileExists(to))
			{
				GD.Print($"[PlayerFiles] NOT moved: {from} and {to} both exist. {to} is in use; {from} is left untouched.");
				continue;
			}

			try
			{
				string absoluteFrom = ProjectSettings.GlobalizePath(from);
				string absoluteTo = ProjectSettings.GlobalizePath(to);
				long bytes = new FileInfo(absoluteFrom).Length;
				Directory.CreateDirectory(Path.GetDirectoryName(absoluteTo)!);
				File.Move(absoluteFrom, absoluteTo);
				GD.Print($"[PlayerFiles] moved {from} -> {to} ({bytes} bytes, renamed, not rewritten)");
			}
			catch (Exception ex)
			{
				GD.Print($"[PlayerFiles] could not move {from} -> {to}: {ex.Message}. The file is left where it was.");
			}
		}
	}
}
