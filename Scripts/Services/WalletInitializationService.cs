using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using GodotBlockchainPort.Blockchain;
using GodotBlockchainPort.Simulation;
using Scripts.Hardware;
#nullable enable

public static class WalletInitializationService
{
	private const string PlayerWalletPath = "user://wallet_state.json";
	private const string CasinoWalletPath = "user://casino_wallet_state.json";
	private const string SatoshiWalletPath = "user://satoshi_wallet_state.json";
	private const string HalWalletPath = "user://hal_wallet_state.json";
	private const string HardwareAllocationPath = "user://hardware_allocation.json";

	private static readonly string[] HardwareNodeIds = { "player", "bot_1", "bot_2", "bot_3", "bot_4" };

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	public static PlayerWalletState? PlayerWallet { get; private set; }
	public static CasinoWalletState? CasinoWallet { get; private set; }
	public static FounderWalletState? SatoshiWallet { get; private set; }
	public static FounderWalletState? HalWallet { get; private set; }

	public static void EnsureAll()
	{
		List<WordlistBootstrapper.WordEntry> wordlist = WordlistBootstrapper.EnsureWordlist();
		PlayerWallet = EnsurePlayerWallet(wordlist);
		CasinoWallet = EnsureCasinoWallet(wordlist);
		SatoshiWallet = EnsureFounderWallet(wordlist, SatoshiWalletPath, "satoshi");
		HalWallet = EnsureFounderWallet(wordlist, HalWalletPath, "hal");
		BotWalletRegistry.EnsureAll();
		EnsureHardwareAllocation();
	}

	private static void EnsureHardwareAllocation()
	{
		// Casino-pool reward ledger loads alongside the hardware allocation (Task 6.1).
		CasinoPoolRepository.EnsureLoaded();

		if (FileAccess.FileExists(HardwareAllocationPath))
		{
			HardwareAllocationRepository.EnsureLoaded();
			GD.Print("[WalletInitializationService] Hardware allocation loaded.");
			return;
		}

		// Bootstrap: each of the 5 miner nodes (player + 4 bots) starts with a single individual-pool
		// credit and 0 casino-pool credits. Starting totals → individual = 5, casino pool = 0 (no casino
		// pool contributors at first launch; players opt in by moving credits to the casino pool).
		foreach (string nodeId in HardwareNodeIds)
		{
			HardwareAllocationRepository.SetNode(new NodeHardwareState
			{
				NodeId = nodeId,
				IndividualPoolCredits = 1,
				CasinoPoolCredits = 0
			});
		}
		GD.Print("[WalletInitializationService] Hardware allocation bootstrapped — 5 nodes, 1 individual + 0 casino credit each.");
	}

	public static void MarkSeedPopupSeen()
	{
		if (PlayerWallet == null) return;
		PlayerWallet = PlayerWallet with { HasSeenSeedPopup = true };
		SavePlayerWallet(PlayerWallet);
	}

	private static PlayerWalletState EnsurePlayerWallet(List<WordlistBootstrapper.WordEntry> wordlist)
	{
		if (FileAccess.FileExists(PlayerWalletPath))
		{
			PlayerWalletState state = LoadPlayerWallet();
			GD.Print($"[WalletInitializationService] Player wallet loaded — {state.BaseAddress}");
			return state;
		}

		string[] words = WordlistBootstrapper.GenerateThreeWords(wordlist, new Random());
		string address = CryptoUtils.DeriveGmAddress(string.Join(" ", words));
		var player = new PlayerWalletState(words, address, HasSeenSeedPopup: false);
		SavePlayerWallet(player);
		GD.Print($"[WalletInitializationService] Player wallet created — {address}");
		GD.Print($"[WalletInitializationService] Player seed words: {string.Join(" ", words)}");
		return player;
	}

	private static CasinoWalletState EnsureCasinoWallet(List<WordlistBootstrapper.WordEntry> wordlist)
	{
		if (FileAccess.FileExists(CasinoWalletPath))
		{
			CasinoWalletState state = LoadCasinoWallet();
			GD.Print($"[WalletInitializationService] Casino wallet loaded — {state.BaseAddress}");
			return state;
		}

		string[] words = WordlistBootstrapper.GenerateThreeWords(wordlist, new Random());
		string address = CryptoUtils.DeriveGmAddress(string.Join(" ", words));
		var casino = new CasinoWalletState(words, address);
		SaveCasinoWallet(casino);
		GD.Print($"[WalletInitializationService] Casino wallet created — {address}");
		return casino;
	}

	private static FounderWalletState EnsureFounderWallet(List<WordlistBootstrapper.WordEntry> wordlist, string path, string founderId)
	{
		if (FileAccess.FileExists(path))
		{
			FounderWalletState state = LoadFounderWallet(path, founderId);
			GD.Print($"[WalletInitializationService] Founder wallet '{founderId}' loaded — {state.BaseAddress}");
			return state;
		}

		string[] words = WordlistBootstrapper.GenerateThreeWords(wordlist, new Random());
		string address = CryptoUtils.DeriveGmAddress(string.Join(" ", words));
		var founder = new FounderWalletState(words, address, founderId);
		SaveFounderWallet(path, founder);
		GD.Print($"[WalletInitializationService] Founder wallet '{founderId}' created — {address}");
		GD.Print($"[WalletInitializationService] Founder '{founderId}' seed words: {string.Join(" ", words)}");
		return founder;
	}

	private static FounderWalletState LoadFounderWallet(string path, string founderId)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		var dto = JsonSerializer.Deserialize<FounderWalletDto>(file.GetAsText(), JsonOptions)!;
		return new FounderWalletState(dto.SeedWords, dto.BaseAddress, founderId);
	}

	private static void SaveFounderWallet(string path, FounderWalletState state)
	{
		var dto = new FounderWalletDto
		{
			SeedWords = state.SeedWords,
			BaseAddress = state.BaseAddress,
			FounderId = state.FounderId
		};
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		file.StoreString(JsonSerializer.Serialize(dto, JsonOptions));
	}

	private static PlayerWalletState LoadPlayerWallet()
	{
		using var file = FileAccess.Open(PlayerWalletPath, FileAccess.ModeFlags.Read);
		var dto = JsonSerializer.Deserialize<PlayerWalletDto>(file.GetAsText(), JsonOptions)!;
		return new PlayerWalletState(dto.SeedWords, dto.BaseAddress, dto.HasSeenSeedPopup);
	}

	private static CasinoWalletState LoadCasinoWallet()
	{
		using var file = FileAccess.Open(CasinoWalletPath, FileAccess.ModeFlags.Read);
		var dto = JsonSerializer.Deserialize<CasinoWalletDto>(file.GetAsText(), JsonOptions)!;
		return new CasinoWalletState(dto.SeedWords, dto.BaseAddress);
	}

	private static void SavePlayerWallet(PlayerWalletState state)
	{
		var dto = new PlayerWalletDto
		{
			SeedWords = state.SeedWords,
			BaseAddress = state.BaseAddress,
			HasSeenSeedPopup = state.HasSeenSeedPopup
		};
		using var file = FileAccess.Open(PlayerWalletPath, FileAccess.ModeFlags.Write);
		file.StoreString(JsonSerializer.Serialize(dto, JsonOptions));
	}

	private static void SaveCasinoWallet(CasinoWalletState state)
	{
		var dto = new CasinoWalletDto
		{
			SeedWords = state.SeedWords,
			BaseAddress = state.BaseAddress
		};
		using var file = FileAccess.Open(CasinoWalletPath, FileAccess.ModeFlags.Write);
		file.StoreString(JsonSerializer.Serialize(dto, JsonOptions));
	}

	private sealed class PlayerWalletDto
	{
		public string[] SeedWords { get; set; } = [];
		public string BaseAddress { get; set; } = string.Empty;
		public bool HasSeenSeedPopup { get; set; }
	}

	private sealed class CasinoWalletDto
	{
		public string[] SeedWords { get; set; } = [];
		public string BaseAddress { get; set; } = string.Empty;
	}

	private sealed class FounderWalletDto
	{
		public string[] SeedWords { get; set; } = [];
		public string BaseAddress { get; set; } = string.Empty;
		public string FounderId { get; set; } = string.Empty;
	}
}
