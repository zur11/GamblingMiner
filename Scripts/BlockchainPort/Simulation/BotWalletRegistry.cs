using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using GodotBlockchainPort.Blockchain;
#nullable enable

namespace GodotBlockchainPort.Simulation;

public static class BotWalletRegistry
{
	private const string RegistryPath = "user://bot_wallet_registry.json";
	private const int MinerBotCount = 4;
	// Step 14 round 3 (D-EB.8, 2026-07-09): raised 10 -> 40 (OQ-EB.5). MUST match
	// BtcNetworkDataService.NonMinerPoolSize exactly — that constant sizes the historical intro
	// SCHEDULE, this one sizes how many real non_miner_N wallet records actually exist to fill it.
	// A registry created under the old count (10) does NOT grow retroactively — non_miner_11..40 simply
	// won't exist until a fresh registry regenerates (delete bot_wallet_registry.json, or the whole
	// user:// folder, then relaunch); identity files are deliberately reset-guard-exempt (Ch. 35 §35.1).
	private const int NonMinerBotCount = 40;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static IReadOnlyList<BotWalletRecord> MinerBots { get; private set; } = [];
	public static IReadOnlyList<BotWalletRecord> NonMinerBots { get; private set; } = [];
	// Step 14 (ND.2) — the scheduler-spawned visible cast (P-14.A). Deliberately a THIRD list, never
	// merged into MinerBots: MinerBots feeds GetBettableNodeIds/BuildBotConfigs (betting runners) and the
	// per-block donation loop, none of which cast miners join in v1 (they mine founder-style via drained
	// attempts, no SC finances; their sell-flow arrives with ND.3). Identity-only, like everything here.
	public static IReadOnlyList<BotWalletRecord> CastMiners { get; private set; } = [];
	public static IReadOnlyList<BotWalletRecord> AllBots => [..MinerBots, ..NonMinerBots, ..CastMiners];

	public static void EnsureAll()
	{
		if (FileAccess.FileExists(RegistryPath))
		{
			LoadRegistry();
			GD.Print($"[BotWalletRegistry] Loaded — {MinerBots.Count} miner bots, {NonMinerBots.Count} non-miner bots, {CastMiners.Count} cast miners.");
			return;
		}

		CreateRegistry();
		SaveRegistry();
		GD.Print($"[BotWalletRegistry] Created — {MinerBots.Count} miner bots, {NonMinerBots.Count} non-miner bots, {CastMiners.Count} cast miners.");
	}

	public static BotWalletRecord? GetBot(string nodeId) =>
		AllBots.FirstOrDefault(b => b.NodeId == nodeId);

	// Step 14 (ND.2) — appends one scheduler-spawned cast miner (fresh wallet) and re-saves. Idempotent
	// per nodeId: an existing record is returned unchanged (spawn decisions are chain/date-derived and may
	// re-fire across restarts).
	public static BotWalletRecord AddCastMiner(string nodeId)
	{
		BotWalletRecord? existing = CastMiners.FirstOrDefault(b => b.NodeId == nodeId);
		if (existing != null)
		{
			return existing;
		}

		var (address, sigPub, sigPriv, secp256k1Pub) = CryptoUtils.GenerateWallet();
		var record = new BotWalletRecord(
			NodeId: nodeId,
			Address: address,
			SigningPublicKeyBase64: sigPub,
			SigningPrivateKeyBase64: sigPriv,
			Secp256k1PublicKeyBase64: secp256k1Pub,
			IsMinerNode: true
		);
		CastMiners = [..CastMiners, record];
		SaveRegistry();
		GD.Print($"[BotWalletRegistry] Cast miner {nodeId} — {address}");
		return record;
	}

	// Updates IsActive and ReactivationBlockHeight for a non-miner bot and re-saves the registry.
	public static void SetBotStatus(string nodeId, bool isActive, int? reactivationBlockHeight)
	{
		var list = NonMinerBots.ToList();
		int idx = list.FindIndex(b => b.NodeId == nodeId);
		if (idx < 0) return;
		list[idx] = list[idx] with { IsActive = isActive, ReactivationBlockHeight = reactivationBlockHeight };
		NonMinerBots = list;
		SaveRegistry();
		GD.Print($"[BotWalletRegistry] {nodeId} — IsActive={isActive}, ReactivationBlockHeight={reactivationBlockHeight}");
	}

	private static void CreateRegistry()
	{
		var miners = new List<BotWalletRecord>(MinerBotCount);
		for (int i = 1; i <= MinerBotCount; i++)
		{
			var (address, sigPub, sigPriv, secp256k1Pub) = CryptoUtils.GenerateWallet();
			miners.Add(new BotWalletRecord(
				NodeId: $"bot_{i}",
				Address: address,
				SigningPublicKeyBase64: sigPub,
				SigningPrivateKeyBase64: sigPriv,
				Secp256k1PublicKeyBase64: secp256k1Pub,
				IsMinerNode: true
			));
			GD.Print($"[BotWalletRegistry] Miner bot_{i} — {address}");
		}

		var nonMiners = new List<BotWalletRecord>(NonMinerBotCount);
		for (int i = 1; i <= NonMinerBotCount; i++)
		{
			var (address, sigPub, sigPriv, secp256k1Pub) = CryptoUtils.GenerateWallet();
			nonMiners.Add(new BotWalletRecord(
				NodeId: $"non_miner_{i}",
				Address: address,
				SigningPublicKeyBase64: sigPub,
				SigningPrivateKeyBase64: sigPriv,
				Secp256k1PublicKeyBase64: secp256k1Pub,
				IsMinerNode: false
			));
			GD.Print($"[BotWalletRegistry] Non-miner non_miner_{i} — {address}");
		}

		MinerBots = miners;
		NonMinerBots = nonMiners;
	}

	private static void LoadRegistry()
	{
		using FileAccess file = FileAccess.Open(RegistryPath, FileAccess.ModeFlags.Read);
		RegistryDto? dto = JsonSerializer.Deserialize<RegistryDto>(file.GetAsText(), JsonOptions);
		if (dto is null) { CreateRegistry(); return; }

		MinerBots = dto.Miners
			.Select(d => new BotWalletRecord(
				d.NodeId, d.Address,
				d.SigningPublicKeyBase64, d.SigningPrivateKeyBase64, d.Secp256k1PublicKeyBase64,
				d.IsActive, d.ReactivationBlockHeight, IsMinerNode: true))
			.ToList();

		NonMinerBots = dto.NonMiners
			.Select(d => new BotWalletRecord(
				d.NodeId, d.Address,
				d.SigningPublicKeyBase64, d.SigningPrivateKeyBase64, d.Secp256k1PublicKeyBase64,
				d.IsActive, d.ReactivationBlockHeight, IsMinerNode: false))
			.ToList();

		// Pre-ND.2 registry files have no Cast array — loads as empty, backward compatible.
		CastMiners = (dto.Cast ?? [])
			.Select(d => new BotWalletRecord(
				d.NodeId, d.Address,
				d.SigningPublicKeyBase64, d.SigningPrivateKeyBase64, d.Secp256k1PublicKeyBase64,
				d.IsActive, d.ReactivationBlockHeight, IsMinerNode: true))
			.ToList();
	}

	private static void SaveRegistry()
	{
		var dto = new RegistryDto
		{
			Miners = MinerBots.Select(b => new BotDto
			{
				NodeId = b.NodeId,
				Address = b.Address,
				SigningPublicKeyBase64 = b.SigningPublicKeyBase64,
				SigningPrivateKeyBase64 = b.SigningPrivateKeyBase64,
				Secp256k1PublicKeyBase64 = b.Secp256k1PublicKeyBase64,
				IsActive = b.IsActive,
				ReactivationBlockHeight = b.ReactivationBlockHeight
			}).ToList(),
			NonMiners = NonMinerBots.Select(b => new BotDto
			{
				NodeId = b.NodeId,
				Address = b.Address,
				SigningPublicKeyBase64 = b.SigningPublicKeyBase64,
				SigningPrivateKeyBase64 = b.SigningPrivateKeyBase64,
				Secp256k1PublicKeyBase64 = b.Secp256k1PublicKeyBase64,
				IsActive = b.IsActive,
				ReactivationBlockHeight = b.ReactivationBlockHeight
			}).ToList(),
			Cast = CastMiners.Select(b => new BotDto
			{
				NodeId = b.NodeId,
				Address = b.Address,
				SigningPublicKeyBase64 = b.SigningPublicKeyBase64,
				SigningPrivateKeyBase64 = b.SigningPrivateKeyBase64,
				Secp256k1PublicKeyBase64 = b.Secp256k1PublicKeyBase64,
				IsActive = b.IsActive,
				ReactivationBlockHeight = b.ReactivationBlockHeight
			}).ToList()
		};
		using FileAccess file = FileAccess.Open(RegistryPath, FileAccess.ModeFlags.Write);
		file.StoreString(JsonSerializer.Serialize(dto, JsonOptions));
	}

	private sealed class BotDto
	{
		public string NodeId { get; set; } = string.Empty;
		public string Address { get; set; } = string.Empty;
		public string? SigningPublicKeyBase64 { get; set; }
		public string? SigningPrivateKeyBase64 { get; set; }
		public string? Secp256k1PublicKeyBase64 { get; set; }
		public bool IsActive { get; set; } = true;
		public int? ReactivationBlockHeight { get; set; }
	}

	private sealed class RegistryDto
	{
		public List<BotDto> Miners { get; set; } = [];
		public List<BotDto> NonMiners { get; set; } = [];
		public List<BotDto>? Cast { get; set; }
	}
}
