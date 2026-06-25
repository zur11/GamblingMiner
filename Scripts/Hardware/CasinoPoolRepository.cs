using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Scripts.Hardware;

#nullable enable

// Persists the casino community-pool reward ledger to user://casino_pool_state.json.
// Each entry is a CasinoPoolRewardEvent: what a casino-mined block owed contributors, and
// whether those payouts have been broadcast yet. Writes happen at block-mining time (the
// only commit point), driven from NetworkRoot.
public static class CasinoPoolRepository
{
	private const string SavePath = "user://casino_pool_state.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private static CasinoPoolState _state = new();
	private static bool _loaded;

	public static CasinoPoolState Current
	{
		get { EnsureLoaded(); return _state; }
	}

	public static void EnsureLoaded()
	{
		if (_loaded) return;
		_loaded = true;

		if (!FileAccess.FileExists(SavePath))
		{
			_state = new CasinoPoolState();
			return;
		}

		using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		CasinoPoolState? loaded = JsonSerializer.Deserialize<CasinoPoolState>(file.GetAsText(), JsonOptions);
		_state = loaded ?? new CasinoPoolState();
	}

	public static void AddRewardEvent(CasinoPoolRewardEvent evt)
	{
		EnsureLoaded();
		_state.RewardHistory.Add(evt);
		Save();
	}

	public static void MarkDistributed(int blockIndex)
	{
		EnsureLoaded();
		List<CasinoPoolRewardEvent> history = _state.RewardHistory;
		bool changed = false;
		for (int i = 0; i < history.Count; i++)
		{
			if (history[i].BlockIndex == blockIndex && !history[i].Distributed)
			{
				history[i] = history[i] with { Distributed = true };
				changed = true;
			}
		}
		if (changed) Save();
	}

	public static List<CasinoPoolRewardEvent> GetUndistributed()
	{
		EnsureLoaded();
		return _state.RewardHistory.Where(e => !e.Distributed).ToList();
	}

	private static void Save()
	{
		using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		file.StoreString(JsonSerializer.Serialize(_state, JsonOptions));
	}
}
