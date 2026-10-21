using Godot;
using System;
using System.Globalization;
using System.IO;
#nullable enable

// Mini-plan 27 — the home of the PLAYER class (plan §3.2, §3.4): everything under user://player/, which no world wipe
// can reach, by construction (§2.2: no named path, no glob and no sweep of the wipe points here). Two layers, each a
// folder, so a reset deletes a folder's files and nobody has to keep a list of them:
//   creations/    layer 1 — the player's own work (saved strategies, notepad notes). Only "Wipe everything".
//   preferences/  layer 2 — settings.json and the hardware credits. "Reset preferences".
//
// And the two durability primitives every file here uses, so the answer to Pattern 2's three questions is written
// once: the write is atomic, a bad read is loud, and a failed load is never written back over the only copy.
public static class PlayerDataFiles
{
	public const string Root = "user://player";
	public const string CreationsDir = Root + "/creations";
	public const string PreferencesDir = Root + "/preferences";

	/// <summary>
	/// INC-001 / Ch. 40 — writes to a sibling .tmp, flushes it to the device, then renames it over the original; never
	/// truncate-and-stream. Creates the folder if needed. Throws on failure, so the caller reports it.
	/// </summary>
	public static void WriteAtomically(string path, string content)
	{
		string absolute = ProjectSettings.GlobalizePath(path);
		Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
		string tmp = absolute + ".tmp";
		using (var stream = new FileStream(tmp, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
		using (var writer = new StreamWriter(stream))
		{
			writer.Write(content);
			writer.Flush();
			stream.Flush(true);
		}

		File.Move(tmp, absolute, overwrite: true);
	}

	/// <summary>
	/// Plan §3.3 / INC-004 — a file that would not read is moved aside as evidence and never overwritten: a second bad
	/// file finds .corrupt taken and gets a stamped name of its own. Returns where it went, or null if it could not be
	/// moved, in which case the caller must write NOTHING this session, because a write would destroy the only copy.
	/// The stamp is file bookkeeping the player never sees, so it is wall-clock.
	/// </summary>
	public static string? MoveAsideAsCorrupt(string path)
	{
		string target = path + ".corrupt";
		if (Godot.FileAccess.FileExists(target))
		{
			target = path + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".corrupt";
		}

		try
		{
			File.Move(ProjectSettings.GlobalizePath(path), ProjectSettings.GlobalizePath(target));
			return target;
		}
		catch (Exception ex)
		{
			GD.Print($"[PlayerDataFiles] could not move {path} aside to {target}: {ex.Message}");
			return null;
		}
	}
}
