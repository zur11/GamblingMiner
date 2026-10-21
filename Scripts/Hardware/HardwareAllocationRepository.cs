using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Scripts.Hardware;

#nullable enable

// Persists per-node hardware credit allocation (individual vs casino pool). Static repository following the
// BotWalletRegistry pattern: CamelCase JSON.
//
// Mini-plan 27 (developer, 2026-10-21; plan §3.4) — the credits are PLAYER data, layer 2 (preferences): they
// survive a world wipe and are reset with the preferences. They lived at user://hardware_allocation.json, on the
// wipe's delete list, until LegacyPlayerFileRelocation moved them here. Being player class, the file is written
// atomically, a bad read is preserved rather than thrown at boot, and it is not under WorldWriteGuard (§3.3: a
// broken world must not stop a player-class write; exempt by class). ⚠ P5 must revisit this once hardware is
// priced in world SC: credits that outlive the world that paid for them are then an inconsistency.
public static class HardwareAllocationRepository
{
	public const string SavePath = PlayerDataFiles.PreferencesDir + "/hardware_allocation.json";
	// Set when an unreadable file could not be moved aside: a save would then write over the only copy.
	private static bool _persistBlocked;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	private static HardwareAllocationSnapshot _snapshot = new();
	private static bool _loaded;

	// Raised after any credit modification (add / move / set), with the affected nodeId. DiceGame
	// subscribes to re-lock the active node's betting speed when its hardware changes.
	public static event Action<string>? HardwareChanged;

	// Which chain a single bet's nonce attempt is routed to.
	public enum NoncePoolTarget { Individual, Casino }

	// Per-node round-robin cursor over the node's credit slots. Transient (runtime distribution
	// mechanism only) — not persisted, reset on app restart.
	private static readonly Dictionary<string, int> _routingCursors = new();

	// Routes ONE nonce attempt for this bet (1 bet = 1 nonce attempt, linear model). The node's first
	// IndividualPoolCredits slots route to its own chain; the remaining CasinoPoolCredits slots to the
	// casino pool. Over TotalCredits consecutive bets this yields exactly IndividualPoolCredits own +
	// CasinoPoolCredits casino attempts — a true reallocation of mining power, never a multiplier.
	public static NoncePoolTarget NextNonceTarget(string nodeId)
	{
		NodeHardwareState hw = GetNode(nodeId);
		int total = hw.TotalCredits;
		if (total <= 0 || hw.CasinoPoolCredits <= 0) return NoncePoolTarget.Individual;
		if (hw.IndividualPoolCredits <= 0) return NoncePoolTarget.Casino;

		_routingCursors.TryGetValue(nodeId, out int slot);
		slot %= total;
		_routingCursors[nodeId] = (slot + 1) % total;
		return slot < hw.IndividualPoolCredits ? NoncePoolTarget.Individual : NoncePoolTarget.Casino;
	}

	public static void EnsureLoaded()
	{
		if (_loaded) return;
		_loaded = true;

		if (!FileAccess.FileExists(SavePath))
		{
			_snapshot = new HardwareAllocationSnapshot();
			return;
		}

		try
		{
			using (FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read))
			{
				if (file == null) throw new System.IO.IOException($"could not open the file ({FileAccess.GetOpenError()})");
				_snapshot = JsonSerializer.Deserialize<HardwareAllocationSnapshot>(file.GetAsText(), JsonOptions)
					?? throw new JsonException("the file holds JSON null");
			}
		}
		catch (Exception ex)
		{
			// Was an uncaught throw at boot. Once moved aside, the file is absent, so WalletInitializationService
			// bootstraps the initial allocation exactly as on a first launch.
			_snapshot = new HardwareAllocationSnapshot();
			string? target = PlayerDataFiles.MoveAsideAsCorrupt(SavePath);
			_persistBlocked = target == null;
			GD.Print(target != null
				? $"[HardwareAllocationRepository] could not read {SavePath} ({ex.Message}). It is preserved as {target}; the initial allocation is bootstrapped."
				: $"[HardwareAllocationRepository] could not read {SavePath} ({ex.Message}), and could not move it aside. Every node reads 0 credits and nothing will be saved this session.");
			GD.PushWarning($"[HardwareAllocationRepository] Load failed: {ex.Message}");
		}
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
		HardwareChanged?.Invoke(updated.NodeId);
	}

	// New hardware purchased; lands in the individual pool by default.
	public static void AddCredits(string nodeId, int count)
	{
		if (count <= 0) return;
		NodeHardwareState current = GetNode(nodeId);
		SetNode(current with { IndividualPoolCredits = current.IndividualPoolCredits + count });
	}

	// DEV/TEST: discard hardware (power-decrease tests). Removes credits from the CASINO pool first, then
	// the individual pool, but never below 1 total credit — a node must keep ≥1 so its reported power
	// (HardwareRate clamps to min 1) stays consistent with TotalCredits. From (1 indiv + 1 casino) one
	// discard yields (1 indiv + 0 casino) = "a single credit in the private pool".
	public static void RemoveCredits(string nodeId, int count)
	{
		if (count <= 0) return;
		NodeHardwareState current = GetNode(nodeId);
		int removable = Math.Max(0, current.TotalCredits - 1); // keep at least 1 credit
		int remove = Math.Min(count, removable);
		if (remove <= 0) return;

		int fromCasino = Math.Min(remove, current.CasinoPoolCredits);
		int fromIndividual = remove - fromCasino;
		SetNode(current with
		{
			CasinoPoolCredits = current.CasinoPoolCredits - fromCasino,
			IndividualPoolCredits = current.IndividualPoolCredits - fromIndividual
		});
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
		// No WorldWriteGuard call since mini-plan 27: this is player class (see the header). It was guarded while the
		// credits were world state (mini-plan 15 A).
		if (_persistBlocked)
		{
			GD.Print($"[HardwareAllocationRepository] not saved: {SavePath} could not be read or moved aside this session.");
			return;
		}

		// Mini-plan 16 A — counted; see PrincipalBalanceService.SaveState for why the timer brackets the close.
		string payload = JsonSerializer.Serialize(_snapshot, JsonOptions);
		long writeBegin = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			PlayerDataFiles.WriteAtomically(SavePath, payload);
		}
		catch (Exception ex)
		{
			GD.Print($"[HardwareAllocationRepository] Save failed: {ex.Message}");
			GD.PushWarning($"[HardwareAllocationRepository] Save failed: {ex.Message}");
			return;
		}
		Scripts.Diagnostics.BlockCostProfiler.NoteStateWrite(SavePath, payload.Length, writeBegin);
	}
}
