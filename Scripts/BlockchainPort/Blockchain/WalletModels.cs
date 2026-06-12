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

// Address-only entry for bot participants. SigningPrivateKeyBase64 provisioned at creation
// (OQ-13 Option A) so bots can send transactions without re-deriving keys.
public record BotWalletRecord(
	string NodeId,
	string Address,            // gm1q... only; no seed words stored
	string? SigningPrivateKeyBase64 = null
);
