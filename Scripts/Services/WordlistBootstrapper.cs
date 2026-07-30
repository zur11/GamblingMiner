using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
#nullable enable

public static class WordlistBootstrapper
{
	public record WordEntry(int Index, string Word);

	private const string SourcePath = "res://Scripts/BlockchainPort/BIP-0039/bip39_2048.txt";
	private const string StorePath = "user://wordlist_256.json";
	private const int SubsetSize = 256;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	// Step 16 P16.2f (found in the P16.6 launch log) — the list is a FIXED 256-word subset, immutable once
	// generated, so it is read from disk at most once per process.
	//
	// Until P16.2a this was called about twice per launch and re-parsing was free. P16.2a made
	// BotWalletRegistry.AddCastMiner call it for EVERY cast-miner spawn, which turned it into a per-spawn
	// disk read + JSON parse + log line — 11 of them visible in a single entry-year bootstrap, and one more
	// on every mid-session spawn for the rest of the world's life. The defect was invisible in code review
	// and obvious in the log, interleaved between the cast-miner lines.
	//
	// General rule: an "Ensure…" that re-does its work on every call is fine while it has two callers and a
	// liability the moment it gains a hot one. When you add a call site to a helper, check what the helper
	// does on the SECOND call.
	private static List<WordEntry>? _cached;

	public static List<WordEntry> EnsureWordlist()
	{
		if (_cached is { Count: > 0 })
		{
			return _cached;
		}

		_cached = FileAccess.FileExists(StorePath) ? Load() : Generate();
		return _cached;
	}

	public static string[] GenerateThreeWords(List<WordEntry> wordlist, Random rng)
	{
		string a, b, c;
		do
		{
			a = wordlist[rng.Next(wordlist.Count)].Word;
			b = wordlist[rng.Next(wordlist.Count)].Word;
			c = wordlist[rng.Next(wordlist.Count)].Word;
		} while (a == b && b == c);

		return [a, b, c];
	}

	private static List<WordEntry> Load()
	{
		using var file = FileAccess.Open(StorePath, FileAccess.ModeFlags.Read);
		string json = file.GetAsText();
		var snapshot = JsonSerializer.Deserialize<WordlistSnapshot>(json, JsonOptions);
		var entries = snapshot?.Words
			.Select(d => new WordEntry(d.Index, d.Word))
			.ToList() ?? new List<WordEntry>();
		string[] sample = GenerateThreeWords(entries, new Random());
		GD.Print($"[WordlistBootstrapper] Loaded {entries.Count} words — sample seed phrase: {string.Join(" ", sample)}");
		return entries;
	}

	private static List<WordEntry> Generate()
	{
		using var sourceFile = FileAccess.Open(SourcePath, FileAccess.ModeFlags.Read);
		string text = sourceFile.GetAsText();
		var all = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
		              .Select(w => w.Trim())
		              .Where(w => !string.IsNullOrEmpty(w))
		              .ToList();

		var rng = new Random();
		for (int i = all.Count - 1; i > 0; i--)
		{
			int j = rng.Next(i + 1);
			(all[i], all[j]) = (all[j], all[i]);
		}

		var subset = all.Take(SubsetSize).OrderBy(w => w, StringComparer.Ordinal).ToList();
		var entries = subset.Select((w, i) => new WordEntry(i + 1, w)).ToList();

		var snapshot = new WordlistSnapshot
		{
			GeneratedAt = DateTime.UtcNow.ToString("o"),
			Words = entries.Select(e => new WordEntryDto { Index = e.Index, Word = e.Word }).ToList()
		};

		using var outFile = FileAccess.Open(StorePath, FileAccess.ModeFlags.Write);
		outFile.StoreString(JsonSerializer.Serialize(snapshot, JsonOptions));

		string[] sample = GenerateThreeWords(entries, new Random());
		GD.Print($"[WordlistBootstrapper] Generated {entries.Count}-word subset from BIP39 2048-word list — saved to {StorePath}");
		GD.Print($"[WordlistBootstrapper] Sample seed phrase: {string.Join(" ", sample)}");
		return entries;
	}

	private sealed class WordlistSnapshot
	{
		public string GeneratedAt { get; set; } = string.Empty;
		public List<WordEntryDto> Words { get; set; } = new();
	}

	private sealed class WordEntryDto
	{
		public int Index { get; set; }
		public string Word { get; set; } = string.Empty;
	}
}
