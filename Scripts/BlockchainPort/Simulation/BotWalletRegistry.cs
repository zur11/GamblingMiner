using Godot;
using System;
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

	// Step 16 P16.2b (D-16.4) — the registry's OWN format version, deliberately separate from
	// WorldFormatVersion.
	//
	// This file is an IDENTITY file and is therefore EXEMPT from NetworkRoot.ResetWorldIfIncompatible's
	// delete list (Ch. 35 §35.1 — wallet seeds, saved strategies and the notepad survive a world wipe on
	// purpose). So a WorldFormatVersion bump does NOT renew it, and P16.2a's seeds would never reach an
	// existing installation: every record would load seedless, every bot would stay single-address, and
	// the whole phase would appear not to work WITHOUT PRINTING ANYTHING — the exact silent-failure shape
	// §39.16 rule 5 exists to catch. Bumping this instead regenerates the identities and says so.
	//
	// Version history: 1 = pre-Step-16 (no seed words). 2 = Step 16 P16.2a (seed-derived addresses).
	// Bump ONLY when the identities themselves must be regenerated — never for a world-state change.
	private const int RegistryFormatVersion = 2;

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
		if (FileAccess.FileExists(RegistryPath) && LoadRegistry())
		{
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

		// P16.2a — a mid-session spawn is seeded exactly like a boot-time one, so a cast miner that appears
		// in 2014 is no less a UTXO citizen than one the registry was born with.
		BotWalletRecord record = CreateSeededRecord(nodeId, isMinerNode: true,
			WordlistBootstrapper.EnsureWordlist(), new Random());
		CastMiners = [..CastMiners, record];
		SaveRegistry();
		GD.Print($"[BotWalletRegistry] Cast miner {nodeId} — {record.Address}");
		return record;
	}

	// P16.2a (D-16.3) — the ONE construction path for a seeded participant. Address and both keypairs all
	// derive from the same 3-word phrase, so base == DerivedAddressWallet.DeriveAddress(0) and a bot is
	// structurally identical to the player/casino/founders. The alternative (keep CryptoUtils.GenerateWallet's
	// random address and bolt a seed beside it) is supported by TryResolveInputKeys but would leave 74 nodes
	// on a base != DeriveAddress(0) combination nothing in this project has ever run.
	private static BotWalletRecord CreateSeededRecord(
		string nodeId, bool isMinerNode, List<WordlistBootstrapper.WordEntry> wordlist, Random rng)
	{
		string[] words = WordlistBootstrapper.GenerateThreeWords(wordlist, rng);
		string seedPhrase = string.Join(" ", words);
		string address = CryptoUtils.DeriveGmAddress(seedPhrase);
		(string sigPub, string sigPriv) = CryptoUtils.DeriveSigningKeypair(seedPhrase);
		string secp256k1Pub = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase);

		return new BotWalletRecord(
			NodeId: nodeId,
			Address: address,
			SigningPublicKeyBase64: sigPub,
			SigningPrivateKeyBase64: sigPriv,
			Secp256k1PublicKeyBase64: secp256k1Pub,
			IsMinerNode: isMinerNode,
			SeedWords: words
		);
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
		// EnsureWordlist is idempotent (loads if present, generates otherwise) — WalletInitializationService
		// already called it before us, but asking again keeps this class self-sufficient.
		List<WordlistBootstrapper.WordEntry> wordlist = WordlistBootstrapper.EnsureWordlist();
		var rng = new Random();

		var miners = new List<BotWalletRecord>(MinerBotCount);
		for (int i = 1; i <= MinerBotCount; i++)
		{
			BotWalletRecord record = CreateSeededRecord($"bot_{i}", isMinerNode: true, wordlist, rng);
			miners.Add(record);
			GD.Print($"[BotWalletRegistry] Miner bot_{i} — {record.Address}");
		}

		var nonMiners = new List<BotWalletRecord>(NonMinerBotCount);
		for (int i = 1; i <= NonMinerBotCount; i++)
		{
			BotWalletRecord record = CreateSeededRecord($"non_miner_{i}", isMinerNode: false, wordlist, rng);
			nonMiners.Add(record);
			GD.Print($"[BotWalletRegistry] Non-miner non_miner_{i} — {record.Address}");
		}

		MinerBots = miners;
		NonMinerBots = nonMiners;
		// A regenerated registry starts with no cast: they are spawn-dripped by NetworkPopulationScheduler
		// as the historical curve grows, each seeded through AddCastMiner.
		CastMiners = [];
	}

	// Returns false when the caller must regenerate instead (unreadable, or an outdated format version).
	// P16.2b: the version check is LOUD. A registry silently kept at version 1 would leave every bot
	// seedless and single-address — a phase that appears not to work while printing nothing.
	private static bool LoadRegistry()
	{
		RegistryDto? dto;
		try
		{
			using FileAccess file = FileAccess.Open(RegistryPath, FileAccess.ModeFlags.Read);
			dto = JsonSerializer.Deserialize<RegistryDto>(file.GetAsText(), JsonOptions);
		}
		catch (Exception ex)
		{
			// INC-001 lesson 2 — a loader that can fail on data the player owns must fail LOUDLY. These are
			// identities, not world state: regenerating is correct, but it must never happen in silence.
			GD.PrintErr($"[BotWalletRegistry] Could not read {RegistryPath} ({ex.GetType().Name}: {ex.Message}) — regenerating identities.");
			return false;
		}

		if (dto is null)
		{
			GD.PrintErr($"[BotWalletRegistry] {RegistryPath} deserialized to null — regenerating identities.");
			return false;
		}

		if (dto.FormatVersion != RegistryFormatVersion)
		{
			GD.Print($"[BotWalletRegistry] Registry format {dto.FormatVersion} != {RegistryFormatVersion} "
				+ "(Step 16 P16.2a — seed-derived bot addresses). Regenerating ALL bot/company/cast identities. "
				+ "Their previous on-chain addresses are abandoned, which is why this ships with a WorldFormatVersion bump (D-16.4).");
			return false;
		}

		MinerBots = dto.Miners.Select(d => ToRecord(d, isMinerNode: true)).ToList();
		NonMinerBots = dto.NonMiners.Select(d => ToRecord(d, isMinerNode: false)).ToList();
		// Pre-ND.2 registry files have no Cast array — loads as empty, backward compatible.
		CastMiners = (dto.Cast ?? []).Select(d => ToRecord(d, isMinerNode: true)).ToList();
		return true;
	}

	private static BotWalletRecord ToRecord(BotDto d, bool isMinerNode) => new(
		d.NodeId, d.Address,
		d.SigningPublicKeyBase64, d.SigningPrivateKeyBase64, d.Secp256k1PublicKeyBase64,
		d.IsActive, d.ReactivationBlockHeight, isMinerNode, d.SeedWords);

	private static void SaveRegistry()
	{
		var dto = new RegistryDto
		{
			FormatVersion = RegistryFormatVersion,
			Miners = MinerBots.Select(ToDto).ToList(),
			NonMiners = NonMinerBots.Select(ToDto).ToList(),
			Cast = CastMiners.Select(ToDto).ToList()
		};
		using FileAccess file = FileAccess.Open(RegistryPath, FileAccess.ModeFlags.Write);
		file.StoreString(JsonSerializer.Serialize(dto, JsonOptions));
	}

	// One mapping, three lists. The three hand-copied blocks this replaces are exactly the shape INC-001's
	// lesson 3 warns about: when several paths write the same file, the rules belong to the FILE — and a
	// new field (SeedWords) would otherwise have had to be remembered in three places.
	private static BotDto ToDto(BotWalletRecord b) => new()
	{
		NodeId = b.NodeId,
		Address = b.Address,
		SigningPublicKeyBase64 = b.SigningPublicKeyBase64,
		SigningPrivateKeyBase64 = b.SigningPrivateKeyBase64,
		Secp256k1PublicKeyBase64 = b.Secp256k1PublicKeyBase64,
		IsActive = b.IsActive,
		ReactivationBlockHeight = b.ReactivationBlockHeight,
		SeedWords = b.SeedWords
	};

	private sealed class BotDto
	{
		public string NodeId { get; set; } = string.Empty;
		public string Address { get; set; } = string.Empty;
		public string? SigningPublicKeyBase64 { get; set; }
		public string? SigningPrivateKeyBase64 { get; set; }
		public string? Secp256k1PublicKeyBase64 { get; set; }
		public bool IsActive { get; set; } = true;
		public int? ReactivationBlockHeight { get; set; }
		// P16.2a — absent in a version-1 file; that file is regenerated rather than read, so this is null
		// only for a hand-edited registry (which then degrades to single-address behaviour, not a crash).
		public string[]? SeedWords { get; set; }
	}

	private sealed class RegistryDto
	{
		// Absent in a pre-Step-16 file ⇒ deserializes to 0 ⇒ != RegistryFormatVersion ⇒ regenerate. The
		// default doing the right thing is deliberate: no explicit "is this old?" test to forget.
		public int FormatVersion { get; set; }
		public List<BotDto> Miners { get; set; } = [];
		public List<BotDto> NonMiners { get; set; } = [];
		public List<BotDto>? Cast { get; set; }
	}
}
