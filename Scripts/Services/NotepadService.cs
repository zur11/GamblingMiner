using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
#nullable enable

public partial class NotepadService : Node
{
	// Mini-plan 27 — a player CREATION (layer 1, plan §3.4): it survives every reset except "Wipe everything".
	// It lived at user://notepad_notes.json until LegacyPlayerFileRelocation moved it here.
	public const string SavePath = PlayerDataFiles.CreationsDir + "/notepad_notes.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private Dictionary<string, string> _notes = new();
	// Set when an unreadable file could not be moved aside: a save would then write the empty set over the only copy
	// of the player's notes, so nothing is written (plan §3.3, INC-004).
	private bool _persistBlocked;

	public override void _Ready() => LoadNotes();

	public IReadOnlyList<string> GetAllNames()
	{
		var names = new List<string>(_notes.Keys);
		names.Sort(StringComparer.OrdinalIgnoreCase);
		return names;
	}

	public string LoadNote(string name) =>
		_notes.TryGetValue(name, out var content) ? content : string.Empty;

	public void SaveNote(string name, string content)
	{
		if (string.IsNullOrEmpty(name)) return;
		_notes[name] = content;
		PersistNotes();
	}

	public void DeleteNote(string name)
	{
		if (_notes.Remove(name))
			PersistNotes();
	}

	// A bad read used to be caught SILENTLY and replaced by an empty set, which the next save wrote over the file:
	// one unreadable file lost every note without a line in any log. Now it is kept as evidence (plan §3.3).
	private void LoadNotes()
	{
		if (!FileAccess.FileExists(SavePath)) return;
		try
		{
			using (var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read))
			{
				if (file == null) throw new System.IO.IOException($"could not open the file ({FileAccess.GetOpenError()})");
				_notes = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText(), JsonOptions)
				         ?? throw new JsonException("the file holds JSON null");
			}
		}
		catch (Exception ex)
		{
			_notes = new Dictionary<string, string>();
			string? target = DurableFile.MoveAsideAsCorrupt(SavePath);
			_persistBlocked = target == null;
			GD.Print(target != null
				? $"[NotepadService] could not read {SavePath} ({ex.Message}). It is preserved as {target}; the notepad starts empty."
				: $"[NotepadService] could not read {SavePath} ({ex.Message}), and could not move it aside. Nothing will be saved this session.");
			GD.PushWarning($"[NotepadService] Load failed: {ex.Message}");
		}
	}

	private void PersistNotes()
	{
		if (_persistBlocked)
		{
			GD.Print($"[NotepadService] not saved: {SavePath} could not be read or moved aside this session.");
			return;
		}

		try
		{
			DurableFile.WriteAtomically(SavePath, JsonSerializer.Serialize(_notes, JsonOptions));
		}
		catch (Exception ex)
		{
			GD.Print($"[NotepadService] Save failed: {ex.Message}");
			GD.PushWarning($"[NotepadService] Save failed: {ex.Message}");
		}
	}
}
