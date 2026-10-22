using Godot;
using GodotBlockchainPort.Simulation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
#nullable enable

// Mini-plan 27 unit 4 (D-27.7, plan §3.4) — the User Settings scene's three resets. Each runs at the NEXT LAUNCH, never
// in place: the scene writes this request and quits, and WorldGuardService (autoload #1) performs it before any other
// autoload has loaded anything. That is the TL.3 lesson applied by construction: a static cache loaded before a
// wipe survives it in memory and writes itself back, and a reset performed before the caches load cannot meet one.
//
//   ResetPreferences  every file in user://player/preferences/ (layer 2). Nothing else: not the creations, not the
//                     world, not the hardware credits, which are world state (§3.5, D-27.11).
//   ResetWorld        NetworkRoot.WipeWorld, the same code a format bump runs (D-27.8). Both player layers are kept.
//   WipeEverything    every file under user://, recursively (D-27.9): the world, both layers, the identity files and
//                     user://logs/. What it cannot delete (Windows holds the session's open godot.log) it skips and
//                     reports by name. The rest of this launch is then a first launch.
public static class PlayerResetRequest
{
	public enum ResetKind { ResetPreferences, ResetWorld, WipeEverything }

	public const string RequestPath = PlayerDataFiles.Root + "/reset_request.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		WriteIndented = true
	};

	private sealed class RequestFile
	{
		public string? Kind { get; set; }
		// File bookkeeping, printed when the request is performed; never game-world state, so wall-clock.
		public DateTime RequestedAtUtc { get; set; }
	}

	/// <summary>Records the request for the next launch, replacing any earlier one. False if it could not be written.</summary>
	public static bool Write(ResetKind kind)
	{
		try
		{
			DurableFile.WriteAtomically(RequestPath, JsonSerializer.Serialize(
				new RequestFile { Kind = kind.ToString(), RequestedAtUtc = DateTime.UtcNow }, JsonOptions));
			GD.Print($"[Reset] requested '{kind}': it runs at the next launch, before anything loads.");
			return true;
		}
		catch (Exception ex)
		{
			GD.Print($"[Reset] could not write {RequestPath}: {ex.Message}. Nothing will be reset.");
			return false;
		}
	}

	/// <summary>The pending request, if a readable one exists, so the scene can say a reset is waiting.</summary>
	public static ResetKind? Pending() => TryRead(out ResetKind kind, out _, out _) ? kind : null;

	/// <summary>Withdraws a pending request. False if one exists and could not be deleted.</summary>
	public static bool Cancel()
	{
		if (!Godot.FileAccess.FileExists(RequestPath))
		{
			return true;
		}

		bool removed = DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(RequestPath)) == Error.Ok;
		GD.Print(removed ? "[Reset] pending request withdrawn." : $"[Reset] could not withdraw {RequestPath}.");
		return removed;
	}

	/// <summary>Called once, first thing, by WorldGuardService.</summary>
	public static void PerformPending()
	{
		if (!Godot.FileAccess.FileExists(RequestPath))
		{
			return;
		}

		if (!TryRead(out ResetKind kind, out DateTime requestedAtUtc, out string problem))
		{
			// An ambiguous request destroys nothing. It is kept as evidence, out of the way of the next launch.
			string? kept = DurableFile.MoveAsideAsCorrupt(RequestPath);
			GD.Print($"[Reset] {RequestPath} could not be read ({problem}). NOTHING was reset. " +
				(kept != null ? $"The file is preserved as {kept}." : "It could not be moved aside either."));
			return;
		}

		// AT MOST ONCE: the request is removed BEFORE the reset runs. Removed after, a request that could not be deleted
		// would wipe the world again at every launch. An interrupted reset is the lesser risk, and for the world it is
		// covered: WipeWorld deletes the version stamp first and rewrites it last, so an unfinished wipe leaves no
		// stamp and the guard finishes it at the next launch. If the request cannot be removed, nothing runs.
		if (DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(RequestPath)) != Error.Ok)
		{
			GD.Print($"[Reset] could not delete {RequestPath}, so '{kind}' was NOT performed: it would otherwise " +
				"repeat at every launch. Delete the file by hand to clear it.");
			return;
		}

		GD.Print($"[Reset] performing '{kind}', requested " +
			requestedAtUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC.");
		switch (kind)
		{
			case ResetKind.ResetPreferences:
				DeleteEveryFileUnder(PlayerDataFiles.PreferencesDir);
				break;
			case ResetKind.ResetWorld:
				NetworkRoot.WipeWorld("the player's Reset world request");
				break;
			case ResetKind.WipeEverything:
				DeleteEveryFileUnder("user://");
				break;
		}
	}

	private static bool TryRead(out ResetKind kind, out DateTime requestedAtUtc, out string problem)
	{
		kind = default;
		requestedAtUtc = default;
		problem = string.Empty;
		if (!Godot.FileAccess.FileExists(RequestPath))
		{
			problem = "no request";
			return false;
		}

		try
		{
			RequestFile? request = JsonSerializer.Deserialize<RequestFile>(
				File.ReadAllText(ProjectSettings.GlobalizePath(RequestPath)), JsonOptions);
			if (request == null || !UserSettingsService.TryParseEnum(request.Kind, out kind))
			{
				problem = $"unknown kind '{request?.Kind}'";
				return false;
			}

			requestedAtUtc = request.RequestedAtUtc;
			return true;
		}
		catch (Exception ex)
		{
			problem = ex.Message;
			return false;
		}
	}

	// Every file under the folder, at any depth, each printed as it goes; a file that cannot be deleted is skipped and
	// reported by name, never fatal. Emptied sub-folders are then removed, deepest first. The folder itself is kept.
	private static void DeleteEveryFileUnder(string userDir)
	{
		string root = ProjectSettings.GlobalizePath(userDir);
		if (!Directory.Exists(root))
		{
			GD.Print($"[Reset] {userDir} does not exist: nothing to delete.");
			return;
		}

		int deleted = 0;
		var skipped = new List<string>();
		foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
		{
			string shown = ToUserPath(root, userDir, file);
			try
			{
				File.Delete(file);
				deleted++;
				GD.Print($"[Reset]     deleted   {shown}");
			}
			catch (Exception ex)
			{
				skipped.Add(shown);
				GD.Print($"[Reset]     SKIPPED   {shown} ({ex.Message})");
			}
		}

		string[] folders = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
		Array.Sort(folders, (a, b) => b.Length.CompareTo(a.Length));
		foreach (string folder in folders)
		{
			try
			{
				if (Directory.GetFileSystemEntries(folder).Length == 0)
				{
					Directory.Delete(folder);
				}
			}
			catch
			{
				// best-effort: a folder still holding a skipped file stays, and that file is already reported
			}
		}

		GD.Print($"[Reset] {userDir}: {deleted} file(s) deleted, {skipped.Count} skipped" +
			(skipped.Count > 0 ? ": " + string.Join(", ", skipped) : "."));
	}

	// "user://" already ends in a slash; "user://player/preferences" does not.
	private static string ToUserPath(string root, string userDir, string file) =>
		(userDir.EndsWith("/", StringComparison.Ordinal) ? userDir : userDir + "/")
		+ Path.GetRelativePath(root, file).Replace('\\', '/');
}
