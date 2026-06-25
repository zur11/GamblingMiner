using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Scripts.Hardware;

#nullable enable

// Persists per-node hardware credit allocation (individual vs casino pool) to
// user://hardware_allocation.json. Static repository following the
// BotWalletRegistry pattern: CamelCase JSON via Godot FileAccess.
public static class HardwareAllocationRepository
{
	private const string SavePath = "user://hardware_allocation.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private static HardwareAllocationSnapshot _snapshot = new();
	private static bool _loaded;

	public static void EnsureLoaded()
	{
		if (_loaded) return;
		_loaded = true;

		if (!FileAccess.FileExists(SavePath))
		{
			_snapshot = new HardwareAllocationSnapshot();
			return;
		}

		using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
		HardwareAllocationSnapshot? loaded =
			JsonSerializer.Deserialize<HardwareAllocationSnapshot>(file.GetAsText(), JsonOptions);
		_snapshot = loaded ?? new HardwareAllocationSnapshot();
	}

	// Returns the node's state, or a fresh zero-credit state if it has none yet.
	public static NodeHardwareState GetNode(string nodeId)
	{
		EnsureLoaded();
		return _snapshot.Nodes.FirstOrDefault(n => n.NodeId == nodeId)
			?? new NodeHardwareState { NodeId = nodeId };
	}

	public static void SetNode(NodeHardwareState updated)
	{
		EnsureLoaded();
		List<NodeHardwareState> nodes = _snapshot.Nodes.ToList();
		int idx = nodes.FindIndex(n => n.NodeId == updated.NodeId);
		if (idx >= 0) nodes[idx] = updated;
		else nodes.Add(updated);
		_snapshot = _snapshot with { Nodes = nodes };
		Save();
	}

	// New hardware purchased; lands in the individual pool by default.
	public static void AddCredits(string nodeId, int count)
	{
		if (count <= 0) return;
		NodeHardwareState current = GetNode(nodeId);
		SetNode(current with { IndividualPoolCredits = current.IndividualPoolCredits + count });
	}

	// Move credits from the casino pool back to the individual pool (clamped to availability).
	public static void MoveToIndividual(string nodeId, int count)
	{
		if (count <= 0) return;
		NodeHardwareState current = GetNode(nodeId);
		int move = Math.Min(count, current.CasinoPoolCredits);
		if (move <= 0) return;
		SetNode(current with
		{
			CasinoPoolCredits = current.CasinoPoolCredits - move,
			IndividualPoolCredits = current.IndividualPoolCredits + move
		});
	}

	// Move credits from the individual pool into the casino pool (clamped to availability).
	public static void MoveToCasinoPool(string nodeId, int count)
	{
		if (count <= 0) return;
		NodeHardwareState current = GetNode(nodeId);
		int move = Math.Min(count, current.IndividualPoolCredits);
		if (move <= 0) return;
		SetNode(current with
		{
			IndividualPoolCredits = current.IndividualPoolCredits - move,
			CasinoPoolCredits = current.CasinoPoolCredits + move
		});
	}

	public static int TotalCasinoPoolCredits()
	{
		EnsureLoaded();
		return _snapshot.Nodes.Sum(n => n.CasinoPoolCredits);
	}

	public static int TotalIndividualCredits()
	{
		EnsureLoaded();
		return _snapshot.Nodes.Sum(n => n.IndividualPoolCredits);
	}

	public static IReadOnlyList<NodeHardwareState> AllNodes()
	{
		EnsureLoaded();
		return _snapshot.Nodes;
	}

	private static void Save()
	{
		using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
		file.StoreString(JsonSerializer.Serialize(_snapshot, JsonOptions));
	}
}
