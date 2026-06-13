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
	private const int NonMinerBotCount = 10;

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	public static IReadOnlyList<BotWalletRecord> MinerBots { get; private set; } = [];
	public static IReadOnlyList<BotWalletRecord> NonMinerBots { get; private set; } = [];
	public static IReadOnlyList<BotWalletRecord> AllBots => [..MinerBots, ..NonMinerBots];

	public static void EnsureAll()
	{
		if (FileAccess.FileExists(RegistryPath))
		{
			LoadRegistry();
			GD.Print($"[BotWalletRegistry] Loaded — {MinerBots.Count} miner bots, {NonMinerBots.Count} non-miner bots.");
			return;
		}

		CreateRegistry();
		SaveRegistry();
		GD.Print($"[BotWalletRegistry] Created — {MinerBots.Count} miner bots, {NonMinerBots.Count} non-miner bots.");
	}

	public static BotWalletRecord? GetBot(string nodeId) =>
		AllBots.FirstOrDefault(b => b.NodeId == nodeId);

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
	}
}
