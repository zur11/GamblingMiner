#nullable enable

// Mini-plan 27 — the home of the PLAYER class (plan §3.2, §3.4): everything under user://player/, which no world wipe
// can reach, by construction (§2.2: no named path, no glob and no sweep of the wipe points here). Two layers, each a
// folder, so a reset deletes a folder's files and nobody has to keep a list of them:
//   creations/    layer 1 — the player's own work (saved strategies, notepad notes). Only "Wipe everything".
//   preferences/  layer 2 — settings.json. "Reset preferences".
// Every file here is written with DurableFile's two primitives (plan §3.3).
public static class PlayerDataFiles
{
	public const string Root = "user://player";
	public const string CreationsDir = Root + "/creations";
	public const string PreferencesDir = Root + "/preferences";
}
