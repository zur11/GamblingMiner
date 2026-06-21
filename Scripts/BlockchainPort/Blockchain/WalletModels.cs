#nullable enable
namespace GodotBlockchainPort.Blockchain;

public record PlayerWalletState(
	string[] SeedWords,        // 3 words; passphrase wallets are not persisted
	string BaseAddress,        // gm1q... derived at save time for quick reads
	bool HasSeenSeedPopup      // true after user dismisses the first-launch popup
);

public record CasinoWalletState(
	string[] SeedWords,
	string BaseAddress         // gm1q...
);

// Founder wallet (Satoshi, Hal — later Mike Hearn). Seed words + base address, like the casino.
// FounderId is the node id ("satoshi" | "hal") used to register the founder as a mining NodeAgent.
// Step 1: one base address per founder. Patoshi-style multi-address per receive is a later step.
public record FounderWalletState(
	string[] SeedWords,
	string BaseAddress,        // gm1q...
	string FounderId           // "satoshi" | "hal"
);

// Wallet entry for bot participants. All bots have a full wallet (address + signing keys)
// so they can send BTC once they have a balance. IsMinerNode distinguishes the four miner
// bots (bot_1..4) from the ten non-miner holder wallets.
// IsActive/ReactivationBlockHeight support the Phase 5.3 "lost BTC" simulation design.
public record BotWalletRecord(
	string NodeId,
	string Address,                          // gm1q... only; no seed words stored
	string? SigningPublicKeyBase64 = null,   // P-256 SubjectPublicKeyInfo
	string? SigningPrivateKeyBase64 = null,  // P-256 PKCS8
	string? Secp256k1PublicKeyBase64 = null, // secp256k1 compressed pubkey
	bool IsActive = true,
	int? ReactivationBlockHeight = null,     // non-null → "sleeping whale" reactivation trigger
	bool IsMinerNode = false
)
{
	public bool HasFullWallet =>
		SigningPublicKeyBase64 is not null &&
		SigningPrivateKeyBase64 is not null &&
		Secp256k1PublicKeyBase64 is not null;
}
