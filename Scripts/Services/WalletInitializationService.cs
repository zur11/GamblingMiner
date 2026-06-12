using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using GodotBlockchainPort.Blockchain;
#nullable enable

public static class WalletInitializationService
{
	private const string PlayerWalletPath = "user://wallet_state.json";
	private const string CasinoWalletPath = "user://casino_wallet_state.json";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	public static PlayerWalletState? PlayerWallet { get; private set; }
	public static CasinoWalletState? CasinoWallet { get; private set; }

	public static void EnsureAll()
	{
		List<WordlistBootstrapper.WordEntry> wordlist = WordlistBootstrapper.EnsureWordlist();
		PlayerWallet = EnsurePlayerWallet(wordlist);
		CasinoWallet = EnsureCasinoWallet(wordlist);
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
}
