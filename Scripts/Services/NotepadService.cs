using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
#nullable enable

public partial class NotepadService : Node
{
	private const string SavePath = "user://notepad_notes.json";
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	private Dictionary<string, string> _notes = new();

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

	private void LoadNotes()
	{
		if (!FileAccess.FileExists(SavePath)) return;
		using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		if (file == null) return;
		try
		{
			_notes = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText(), JsonOptions)
			         ?? new Dictionary<string, string>();
		}
		catch { _notes = new Dictionary<string, string>(); }
	}

	private void PersistNotes()
	{
		using var file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		if (file == null) return;
		file.StoreString(JsonSerializer.Serialize(_notes, JsonOptions));
	}
}
