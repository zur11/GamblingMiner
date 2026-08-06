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
	string Address,                          // gm1q...; DERIVED from SeedWords since Step 16 (D-16.3)
	string? SigningPublicKeyBase64 = null,   // P-256 SubjectPublicKeyInfo
	string? SigningPrivateKeyBase64 = null,  // P-256 PKCS8
	string? Secp256k1PublicKeyBase64 = null, // secp256k1 compressed pubkey
	bool IsActive = true,
	int? ReactivationBlockHeight = null,     // non-null → "sleeping whale" reactivation trigger
	bool IsMinerNode = false,
	// Step 16 P16.2a (OQ-8.2, D-16.3) — the 3-word seed phrase, same 256-word subset the player, casino
	// and founders use. Its presence is what promotes a bot from a single-address participant to a full
	// UTXO citizen: NetworkRoot.CreateAndRegisterNode gives any record carrying one a DerivedAddressWallet
	// (change rotation), and Address is DeriveGmAddress(seed) so base == DeriveAddress(0), exactly as for
	// every other seeded node.
	//
	// Nullable on purpose (§39.16 rule 5's sentinel default): a record written before Step 16 has no seed
	// and keeps behaving exactly as it did — single-address, no rotation — rather than crashing. That
	// graceful degradation is also why BotWalletRegistry version-gates its own regeneration (P16.2b): a
	// stale registry would silently keep the OLD behaviour, which is far harder to notice than an error.
	string[]? SeedWords = null
)
{
	public bool HasFullWallet =>
		SigningPublicKeyBase64 is not null &&
		SigningPrivateKeyBase64 is not null &&
		Secp256k1PublicKeyBase64 is not null;

	// The single test every Step-16 wiring decision keys off — NEVER "is this a ghost / a bot / a cast
	// miner?" (D-16.6 as amended by D-16.17). That discipline is what leaves the ghost-typology door open:
	// a future ghost that can spend arrives seeded, and therefore arrives already rotating change.
	public bool HasSeed => SeedWords is { Length: > 0 };

	public string SeedPhrase => SeedWords is { Length: > 0 } ? string.Join(" ", SeedWords) : string.Empty;
}
