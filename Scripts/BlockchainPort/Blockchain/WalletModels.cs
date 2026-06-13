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

// Wallet entry for bot participants. Miner bots have all three key fields populated
// (OQ-13 Option A) so they can sign transactions immediately. Non-miner bots have null
// key fields until sending is enabled. IsActive/ReactivationBlockHeight support the
// Phase 5.3 "lost BTC" simulation design.
public record BotWalletRecord(
	string NodeId,
	string Address,                          // gm1q... only; no seed words stored
	string? SigningPublicKeyBase64 = null,   // P-256 SubjectPublicKeyInfo (miner bots only)
	string? SigningPrivateKeyBase64 = null,  // P-256 PKCS8 (miner bots only)
	string? Secp256k1PublicKeyBase64 = null, // secp256k1 compressed pubkey (miner bots only)
	bool IsActive = true,
	int? ReactivationBlockHeight = null      // non-null → "sleeping whale" reactivation trigger
)
{
	public bool HasFullWallet =>
		SigningPublicKeyBase64 is not null &&
		SigningPrivateKeyBase64 is not null &&
		Secp256k1PublicKeyBase64 is not null;
}
