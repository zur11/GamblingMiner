# BTC Wallet Address System — Implementation Plan

**Status**: Phase 0.1 ✓  Phase 0.2 ✓  Phase 0.3 ✓  Phase 0.4 ✓  Phase 0.5 ✓  Phase 1.1 ✓  Phase 1.2 ✓  Phase 1.3 ✓  Phase 2 ✓  Phase 3 ✓  Phase 4 ✓  Phase 5.1 ✓  Phase 5.2 ✓  Phase 5.4 ✓  —  Next: Phase 6 (BlockExplorer Transfer Refactor)
**HRP**: `gm` → addresses like `gm1q...`  
**Curve**: secp256k1 for address derivation (all participants); P-256 for transaction signing (existing pipeline)  
**Passphrase model**: `SHA256("w1 w2 w3 [w4]")` → 32-byte private key → secp256k1 → gm1q... address  
**Wordlist**: 256 words randomly selected from BIP39 2048 on first launch → `user://wordlist_256.json`  
**Bot wallets**: address only (no seed words stored)  
**Player wallet**: seed words + base address in `user://wallet_state.json`  
**Casino wallet**: seed words + base address in `user://casino_wallet_state.json`  

---

## Current State

- `Ripemd160.cs` ✓ — pure C# RIPEMD-160 (RFC spec); `Hash(byte[])` / `Hash(ReadOnlySpan<byte>)`
- `Bech32.cs` ✓ — BIP173 encoder/decoder; `Encode("gm", 0, hash20)` → `"gm1q..."`; `TryDecode()` and `IsValidGmAddress()` helpers; `Bech32.GameHrp = "gm"` constant
- `Secp256k1.cs` ✓ — minimal secp256k1; `GetCompressedPublicKey(byte[32])` → `byte[33]`; `IsValidPrivateKey()`; double-and-add scalar multiplication; affine coordinates; Fermat modular inverse
- `CryptoUtils.cs` ✓ — `DeriveGmAddress()`, `DeriveSigningKeypair()`, `DeriveSecp256k1CompressedPublicKeyBase64()` (same derivation path as `DeriveGmAddress` but returns the compressed pubkey instead of the address — used to populate `NodeAgent.WalletSecp256k1PublicKey` for the player node), updated `GenerateWallet()` (4-tuple), updated `DeriveAddressFromPublicKey()` (now takes secp256k1 compressed pubkey base64)
- `NodeAgent.cs` ✓ — `WalletSecp256k1PublicKey` property; constructor uses 4-tuple; `CreateSignedTransaction()` sets `tx.Secp256k1PublicKeyBase64`
- `Models.cs` ✓ — `Transaction` has new `Secp256k1PublicKeyBase64` field (address verification) alongside existing `PublicKeyBase64` (P-256 signing)
- `BlockchainService.cs` ✓ — `ValidateTransactionSignature()` uses `tx.Secp256k1PublicKeyBase64` for address check; `tx.PublicKeyBase64` still used by `CryptoUtils.Verify()`
- `Scripts/BlockchainPort/BIP-0039/bip39_2048.txt` ✓ — renamed from `2048WordsList`; 2048 BIP39 words one per line; read-only source for `WordlistBootstrapper`; requires `*.txt` in export preset include filter when presets are configured
- `Scripts/Services/WordlistBootstrapper.cs` ✓ — idempotent; `EnsureWordlist()` generates/loads 256-word subset; `GenerateThreeWords()` for 3-word seed generation; `GD.Print` output on both code paths for verification
- `Scripts/Services/CalendarTimeService._Ready()` ✓ — `WordlistBootstrapper.EnsureWordlist()` is the first call; `WalletInitializationService.EnsureAll()` slot reserved between it and `EnsureGameEpochInitialized()`
- `Scripts/BlockchainPort/Blockchain/WalletModels.cs` ✓ — `PlayerWalletState`, `CasinoWalletState`, `BotWalletRecord` records; namespace `GodotBlockchainPort.Blockchain`; `SigningPrivateKeyBase64` on `BotWalletRecord` per OQ-13
- `Scripts/Services/WalletInitializationService.cs` ✓ — static class; `EnsureAll()` creates/loads player + casino wallets from `user://`; `MarkSeedPopupSeen()` for Phase 4 popup; DTO-based JSON serialization; `GD.Print` output on both code paths
- `Scripts/Services/CalendarTimeService._Ready()` ✓ — `WalletInitializationService.EnsureAll()` wired between `EnsureWordlist()` and `EnsureGameEpochInitialized()`
- `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` ✓ — `GetAddressBalanceDetails(address)` → `(confirmedBalance, pendingOutgoing)` (Phase 4); `CreateAndRegisterNode` for `"player"` always derives wallet from `WalletInitializationService.PlayerWallet` seed phrase (Phase 4 post-test fix); bot branch now uses `BotWalletRegistry.GetBot(nodeId)` as primary source → snapshot fallback → fresh random wallet (Phase 5.2)
- `Scripts/Services/SceneManager.cs` ✓ (Phase 4) — `BTCWallet` in enum + Paths
- `Screens/MainMenu/MainMenu.tscn` + `.cs` ✓ (Phase 4) — `BTCWalletBtn` added and wired
- `Screens/BTCWallet/BTCWallet.tscn` ✓ — three mode panels (Base, PassphraseLocked, PassphraseUnlocked) + SeedPopup overlay + NetworkRoot child
- `Screens/BTCWallet/BTCWallet.cs` ✓ — full controller; 2s balance refresh; passphrase derive-on-unlock; clipboard copy; seed popup flow
- `Scripts/BlockchainPort/Blockchain/WalletModels.cs` ✓ (Phase 5.2) — `BotWalletRecord` extended: added `SigningPublicKeyBase64?`, `Secp256k1PublicKeyBase64?`, `IsActive`, `ReactivationBlockHeight?`; `HasFullWallet` property checks all three keys non-null
- `Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs` ✓ (Phase 5.2 + 5.4) — static registry; `EnsureAll()` generates/loads 4 miner bots (full keys via `CryptoUtils.GenerateWallet()`) + 10 non-miner bots (address only); `GetBot(nodeId)` lookup; `user://bot_wallet_registry.json` with CamelCase JSON; separate `Miners`/`NonMiners` arrays in JSON
- `Scripts/Services/WalletInitializationService.cs` ✓ (Phase 5.2) — `EnsureAll()` now calls `BotWalletRegistry.EnsureAll()` after player + casino wallets
- `Documentation/ProjectDesignManual.md` ✓ — Chapters 1–15 covering Phases 0.1–0.5, 1.1–1.3, 2, 3, and 4

---

## Phase 0 — Foundational Cryptography

### Task 0.1 — RIPEMD-160  ✓ DONE
**File**: `Scripts/BlockchainPort/Blockchain/Ripemd160.cs`  
Pure C# implementation per RFC 2286. Two overloads: `Hash(byte[])` and `Hash(ReadOnlySpan<byte>)`.  
Test vectors (verify before Phase 0.4):
- `RIPEMD160("")` → `9c1185a5c5e9fc54612808977ee8f548b2258d31`
- `RIPEMD160("abc")` → `8eb208f7e05d987a9b044a8e98c6b087f15a0bfc`

### Task 0.2 — Bech32 Encoder  ✓ DONE
**File**: `Scripts/BlockchainPort/Blockchain/Bech32.cs`  
BIP173 encoder and decoder. Key constants/API:
```csharp
Bech32.GameHrp                              // "gm"
Bech32.Encode("gm", 0x00, hash20)          // → "gm1q..."
Bech32.TryDecode(address, out hrp, ...)    // → bool
Bech32.IsValidGmAddress(address)           // → bool
```
P2WPKH address format with HRP "gm": `gm` + `1` + `q` + 32 data chars + 6 checksum chars = 42 chars total.  
Changing `GameHrp` to `"bc"` would produce valid Bitcoin mainnet addresses from the same private keys — the math is identical.

### Task 0.3 — secp256k1 minimal implementation  ✓ DONE

**File**: `Scripts/BlockchainPort/Blockchain/Secp256k1.cs`  
**Namespace**: `GodotBlockchainPort.Blockchain`  
**Dependencies**: `System.Numerics.BigInteger` (already in .NET 8, no new packages)  

**Why secp256k1 for ALL participants (not just player/casino)**:
- secp256k1 is used ONLY for address derivation: `private_key → compressed_pubkey → gm1q... address`
- Transaction signing stays on P-256 (existing `CryptoUtils.Sign()`) — purely internal to the game, no external verification needed
- This means ALL addresses (player, casino, miner bots, non-miner bots) are derived via the same secp256k1 path
- For player/casino: `private_key = SHA256("word1 word2 word3")` → secp256k1 pubkey → address (deterministic from words, not stored)
- For bots: `private_key = 32 random bytes` → secp256k1 pubkey → address (address stored, not the key bytes)
- External verifiability: replace `"gm"` HRP with `"bc"` → the address is a valid real Bitcoin mainnet P2WPKH address

**Curve parameters** (secp256k1, hardcoded):
```
p  = FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F
a  = 0   (y² = x³ + 7)
b  = 7
Gx = 79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798
Gy = 483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8
n  = FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141
```

**Required operations**:
- `PointAdd(EcPoint p, EcPoint q)` — affine coordinates, handles P+P and P+Q
- `PointDouble(EcPoint p)`
- `ScalarMul(EcPoint p, BigInteger k)` — double-and-add
- `GetCompressedPublicKey(byte[] privateKey32)` → `byte[33]` (prefix 02/03 + 32-byte X)

**Public API**:
```csharp
public static class Secp256k1
{
    public static byte[] GetCompressedPublicKey(byte[] privateKey);  // 32 bytes in → 33 bytes out
}
```

**Test vector** (verify after implementation):
- Private key: `0000...0001` (32 bytes, last byte = 1)
- Compressed pubkey: `0279BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798`
  (This is just G itself — the generator point, since 1 × G = G)

### Task 0.4 — Wire secp256k1 into CryptoUtils and dependent files  ✓ DONE

**Files changed**: `CryptoUtils.cs`, `Models.cs`, `NodeAgent.cs`, `BlockchainService.cs`

**The double-duty problem (why 4 files needed updating)**:

Before Phase 0.4, `tx.PublicKeyBase64` served two purposes in `ValidateTransactionSignature()`:
1. Address verification: `CryptoUtils.DeriveAddressFromPublicKey(tx.PublicKeyBase64)` compared against `tx.Sender`
2. Signature verification: `CryptoUtils.Verify(payload, tx.SignatureBase64, tx.PublicKeyBase64)`

In the new system these require different key types:
- Address verification needs the **secp256k1 compressed public key** (33 bytes)
- Signature verification needs the **P-256 SubjectPublicKeyInfo** (used by `ECDsa.ImportSubjectPublicKeyInfo`)

A single field can no longer serve both. Solution: add `Transaction.Secp256k1PublicKeyBase64` for address verification, keep `PublicKeyBase64` for P-256 signing.

**`CryptoUtils.cs` changes**:
- `GenerateWallet()` → now a 4-tuple: `(address, signingPublicKeyBase64, signingPrivateKeyBase64, secp256k1PublicKeyBase64)`
- `DeriveAddressFromPublicKey(secp256k1CompressedPubKeyBase64)` → takes secp256k1 33-byte compressed pubkey (base64), produces `gm1q...` via Hash160 + Bech32
- `DeriveGmAddress(seedPhrase)` → full pipeline with OQ-12 safety loop (see OQ-12)
- `DeriveSigningKeypair(seedPhrase)` → deterministic P-256 keypair with OQ-16 try/catch loop (see OQ-16)
- `Sign()`, `Verify()`, `Sha256Hex()` → unchanged

**`GenerateWallet()` key material design**: 32 random bytes drive both derivations. secp256k1 validity is checked first (OQ-12 probability); then P-256 key is created via `ECParameters.D = keyMaterial` inside a try/catch (OQ-16). The loop retries on either failure — in practice always exits on first attempt.

**`Models.cs`**: `Transaction` gains `Secp256k1PublicKeyBase64 { get; set; } = string.Empty`. Existing field `PublicKeyBase64` retains its role as the P-256 SubjectPublicKeyInfo for `Verify()`.

**`NodeAgent.cs`**: Constructor destructures the 4-tuple into a new `WalletSecp256k1PublicKey` property. `CreateSignedTransaction()` sets `tx.Secp256k1PublicKeyBase64 = WalletSecp256k1PublicKey`.

**`BlockchainService.ValidateTransactionSignature()`**: Now checks `Secp256k1PublicKeyBase64` for address ownership and `PublicKeyBase64` for the P-256 signature. Transactions missing either field are rejected.

### Task 0.5 — Wallet address persistence  ✓ DONE

**Files changed**: `NodeAgent.cs`, `NetworkRoot.cs`

**Problem**: `NodeAgent` always called `CryptoUtils.GenerateWallet()` with `RandomNumberGenerator.GetBytes(32)` in its constructor, producing a different random address on every game launch. `BlockchainStateSnapshot` saved the blockchain chain and financial states but never the wallet addresses or keys. Each session's coinbase rewards were recorded against the addresses generated in that session; restarting the game created a new set of addresses, making all previous blockchain records orphaned. The bug also appeared within a session: if pending transactions from the previous session (loaded from disk) contained coinbases addressed to the old session's addresses, mining a block would include those old-address coinbases, making it look as if the address changed mid-session without navigation.

**Fix in `NodeAgent.cs`**: Added a second constructor that accepts pre-existing wallet credentials `(nodeId, address, signingPublicKey, signingPrivateKey, secp256k1PublicKey)` directly, bypassing `GenerateWallet()`. The original random-generation constructor is untouched and is still used for first-launch wallet creation.

**Fix in `NetworkRoot.cs`**:
- `EnsureInitialized()` now calls `TryLoadSnapshot()` **before** creating nodes, so saved wallet data is available at construction time.
- `CreateAndRegisterNode(nodeId, savedState)` checks `savedState.NodeWallets` for a complete wallet snapshot; uses it if present, falls back to random generation otherwise.
- `PersistStateToDisk()` now writes a `NodeWallets` dictionary (all node IDs → address + signing keys + secp256k1 pubkey) into the snapshot.
- `LoadStateFromDisk()` was refactored into two cleaner methods: `TryLoadSnapshot()` (reads and deserializes JSON) and `ApplyStateFromSnapshot()` (applies chain + financial state to live nodes). Wallet restoration happens earlier, in `EnsureInitialized()`, before node construction.
- `BlockchainStateSnapshot` gains a `NodeWallets` property; a new private `NodeWalletSnapshot` class holds the four persisted fields and an `IsComplete()` guard that rejects partially-saved records.

**Migration note**: existing `user://blockchain/state.json` saved before this fix has no `NodeWallets` entry. On the first launch after the fix, all nodes receive freshly-generated addresses, so any previous blockchain history (coinbases to old addresses) becomes inaccessible. Clear `user://blockchain/` for a clean start.

---

## Phase 1 — Wordlist System

### Task 1.1 — Rename wordlist file  ✓ DONE

**File**: `Scripts/BlockchainPort/BIP-0039/bip39_2048.txt` (renamed from `2048WordsList`)

Renamed the extensionless file to `bip39_2048.txt`. The `.txt` extension is required for Godot's export pipeline to recognize and include the file in PCK builds. At runtime, `WordlistBootstrapper.EnsureWordlist()` (Phase 1.2) opens it via `FileAccess.Open("res://Scripts/BlockchainPort/BIP-0039/bip39_2048.txt")`. The file contains exactly 2048 BIP39 English words, one per line.

**Export filter note**: when export presets are configured in `export_presets.cfg`, add `*.txt` to `include_filter` for each platform preset. In editor/development mode `res://` reads directly from the project directory — no filter needed.

### Task 1.2 — Implement `WordlistBootstrapper`  ✓ DONE

**File**: `Scripts/Services/WordlistBootstrapper.cs`

`EnsureWordlist()` is idempotent: if `user://wordlist_256.json` exists it deserializes and returns from disk; otherwise it opens the 2048-word source at `res://`, Fisher-Yates shuffles all words, takes 256, sorts alphabetically, assigns indices 1..256, serializes to `user://wordlist_256.json`, and returns the list.

JSON format (`user://wordlist_256.json`, CamelCase per project policy):
```json
{
  "generatedAt": "2009-01-03T18:15:06.000Z",
  "words": [
    { "index": 1, "word": "abandon" },
    { "index": 2, "word": "ability" }
  ]
}
```

Internal serialization uses a private `WordEntryDto` class so the public `WordEntry` record is kept clean and the JSON DTO is kept isolated. The private `WordlistSnapshot` class holds the top-level structure.

**Word selection rule** (for 3-word seed generation):
- Pick A, B, C independently at random from the 256 words
- Reject and retry only if all three are identical (A == B == C)
- One repeat within the set is allowed (e.g., "able able abandon" is valid)

**Public API**:
```csharp
public static class WordlistBootstrapper
{
    public record WordEntry(int Index, string Word);

    public static List<WordEntry> EnsureWordlist();
    public static string[] GenerateThreeWords(List<WordEntry> wordlist, Random rng);
}
```

**Startup prints** (visible in Godot Output panel):
- First launch: `[WordlistBootstrapper] Generated 256-word subset from BIP39 2048-word list — saved to user://wordlist_256.json` + `First 3: <word>, <word>, <word>`
- Subsequent launches: `[WordlistBootstrapper] Loaded 256 words from user://wordlist_256.json — first 3: <word>, <word>, <word>`

### Task 1.3 — Wire into startup  ✓ DONE

`WordlistBootstrapper.EnsureWordlist()` added as the first call in `CalendarTimeService._Ready()`, before `EnsureGameEpochInitialized()`. Phase 3 (`WalletInitializationService.EnsureAll()`) will be inserted between the two calls once implemented.

---

## Phase 2 — Wallet Persistence Models  ✓ DONE

**File**: `Scripts/BlockchainPort/Blockchain/WalletModels.cs`

Three records in namespace `GodotBlockchainPort.Blockchain`. No persistence logic here — reading/writing happens in Phase 3 (`WalletInitializationService`) and Phase 5.4 (`BotWalletRegistry`).

```csharp
public record PlayerWalletState(
    string[] SeedWords,        // 3 words; passphrase wallets are not persisted
    string BaseAddress,        // gm1q... derived at save time for quick reads
    bool HasSeenSeedPopup      // true after user dismisses the first-launch popup
);

public record CasinoWalletState(
    string[] SeedWords,
    string BaseAddress         // gm1q...
);

// SigningPrivateKeyBase64 provisioned at creation per OQ-13 (Option A).
// Nullable only for forward-compatibility; always populated when a bot is registered.
public record BotWalletRecord(
    string NodeId,
    string Address,            // gm1q... only; no seed words stored
    string? SigningPrivateKeyBase64 = null
);
```

**Persistence locations** (Phase 3 responsibility):
- `PlayerWalletState` → `user://wallet_state.json`
- `CasinoWalletState` → `user://casino_wallet_state.json`
- `BotWalletRecord[]` → `user://bot_wallet_registry.json` (Phase 5.4)

---

## Phase 3 — Game Startup Wallet Initialization  ✓ DONE

**File**: `Scripts/Services/WalletInitializationService.cs`

Static class. `EnsureAll()` is the single entry point — called once at startup from `CalendarTimeService._Ready()`.

**Startup sequence** (ordering matters):
```
CalendarTimeService._Ready()
  → WordlistBootstrapper.EnsureWordlist()        [Phase 1]
  → WalletInitializationService.EnsureAll()      [Phase 3]
     → EnsurePlayerWallet()
     → EnsureCasinoWallet()
  → EnsureGameEpochInitialized()
```

**Player wallet**: if `user://wallet_state.json` missing → `GenerateThreeWords()` → `DeriveGmAddress()` → save `PlayerWalletState` with `HasSeenSeedPopup = false` → print address + seed words.  
**Casino wallet**: if `user://casino_wallet_state.json` missing → same pipeline with separate `new Random()` call → save `CasinoWalletState`.  
Both paths load from disk on subsequent launches (DTO-based deserialization, CamelCase JSON).

**Public API**:
```csharp
public static PlayerWalletState? PlayerWallet { get; }   // non-null after EnsureAll()
public static CasinoWalletState? CasinoWallet { get; }  // non-null after EnsureAll()
public static void EnsureAll();
public static void MarkSeedPopupSeen();  // updates HasSeenSeedPopup = true, re-saves wallet_state.json
```

Block mining coinbase rewards are automatically sent to `PlayerWalletState.BaseAddress`. Player cannot change this address in Basic Mode.

---

## Phase 4 — BTCWallet Scene  ✓ DONE

**Path**: `Screens/BTCWallet/BTCWallet.tscn` + `BTCWallet.cs`  
**Navigation**: MainMenu → BTCWallet (wired via `SceneManager.SceneId.BTCWallet`)  
**Player-facing from day one**: full polish required.

### Scene layout (implemented)

```
[TopBar: < Main Menu | StatusBar]

=== BASE WALLET MODE (default) ===
  "Base Wallet"
  "Deposit Address"
  [gm1q...address]  [Copy]
  "Balance: X.XXXXXXXX BTC"  (confirmed only)
  "Pending outgoing: Y.XXXXXXXX BTC"  (only shown when pendingOut > 0)
  [Send BTC — disabled]   [Open Passphrase Wallet →]

=== PASSPHRASE WALLET MODE ===
  [IF locked]
    "Enter your passphrase word"
    [LineEdit]  [Unlock]  (Enter key also triggers Unlock)
    ⚠ "Save your passphrase offline. It cannot be recovered from this app."
    [← Base Wallet]
  [IF unlocked]
    "Passphrase Wallet"
    "Deposit Address"
    [gm1q...passphrase-address]  [Copy]
    "Balance: X.XXXXXXXX BTC"
    "Pending outgoing: Y.XXXXXXXX BTC"  (only shown when > 0)
    [Send BTC — disabled]   [← Base Wallet]
    ⚠ "Save your passphrase. This reminder always appears when using a passphrase wallet."
```

### First-launch popup (implemented)

- `SeedPopup` Panel overlays full screen (last child of root Control)
- Shown when `WalletInitializationService.PlayerWallet.HasSeenSeedPopup == false`
- Displays 3 words at 44pt font, separated
- [Copy to clipboard] — copies space-joined words to clipboard
- Warning: "Write these words down. This is the only time they will be shown automatically."
- [I have saved my words] → calls `WalletInitializationService.MarkSeedPopupSeen()`, hides popup

### Passphrase wallet mechanics (implemented)

- Any string can be a passphrase — not validated against wordlist
- Seed phrase passed to `CryptoUtils.DeriveGmAddress("word1 word2 word3 passphrase")`
- Passphrase field emptied immediately after Unlock and on back navigation
- `_currentPassphraseAddress` cleared on back navigation

### Balance display (implemented)

- `NetworkRoot.GetAddressBalanceDetails(address)` returns `(confirmedBalance, pendingOutgoing)`
  - `confirmedBalance` = `BlockchainService.GetAddressData(address).AddressBalance`
  - `pendingOutgoing` = sum of `PendingTransactions` where `Sender == address`
- Refreshed every 2 seconds via `_Process()` timer
- "Pending outgoing" label hidden when `pendingOutgoing == 0`
- [Send BTC] is disabled (placeholder for Phase 6 send flow)

### Files changed for Phase 4

- `Scripts/BlockchainPort/Blockchain/CryptoUtils.cs` — added `DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase)`: same derivation path as `DeriveGmAddress` (SHA256 → secp256k1 private key → compressed pubkey), returns the base64 pubkey needed to populate `NodeAgent.WalletSecp256k1PublicKey`
- `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` — added `GetAddressBalanceDetails(string address)` → `(decimal, decimal)`; `CreateAndRegisterNode` for `"player"` now always uses `WalletInitializationService.PlayerWallet` (address + `DeriveSigningKeypair` + `DeriveSecp256k1CompressedPublicKeyBase64`) — persisted random wallet for the player is ignored going forward
- `Scripts/Services/SceneManager.cs` — added `BTCWallet` to `SceneId` enum + `Paths`
- `Screens/MainMenu/MainMenu.tscn` — added `BTCWalletBtn`
- `Screens/MainMenu/MainMenu.cs` — wired `BTCWalletBtn` → `SceneManager.SceneId.BTCWallet`
- `Screens/BTCWallet/BTCWallet.tscn` — new scene (NetworkRoot child + three mode panels + SeedPopup overlay)
- `Screens/BTCWallet/BTCWallet.cs` — new controller

**Tested**: clean blockchain run confirmed — BTCWallet address and BlockExplorer player address match; coinbase rewards accrue to the correct address.

---

## Phase 5 — NodeAgent & Bot Wallet Update

### Task 5.1 — Update `CryptoUtils.GenerateWallet()`  ✓ DONE (completed during Phase 0.5)

`GenerateWallet()` already calls `DeriveAddressFromPublicKey()` → `Bech32.Encode(Bech32.GameHrp, 0x00, pubKeyHash)` → `gm1q...`. The 4-tuple return `(address, signingPublicKeyBase64, signingPrivateKeyBase64, secp256k1PublicKeyBase64)` was added in Phase 0.5. All `NodeAgent` mining bots created fresh (no saved snapshot) automatically receive `gm1q...` addresses via the `NodeAgent(string nodeId)` constructor. No code changes needed.

### Task 5.2 — Bot wallet creation timeline (OQ-8 resolved)  ✓ DONE

**Initial population** (at game start, `WalletInitializationService.EnsureAll()`):
- 4 miner bots (`NodeAgent` IDs `bot_1`–`bot_4`, full keys stored in registry)
- 10 non-miner bots (IDs `non_miner_1`–`non_miner_10`, address only for now)

`BotWalletRegistry.EnsureAll()` is called after player + casino wallets, before `NetworkRoot.EnsureInitialized()`. This guarantees bot addresses are stable before `NodeAgent` instances are created.

**Migration note**: upgrading an existing save requires clearing `user://blockchain/` (delete the folder). This is the same procedure as for the Phase 4 post-test fix.

**Growth cadence**: historically-proportional expansion tracked in a future `BotScheduler` service.  
Reference data to research: BTC wallet count growth 2009–2012, mining node count, transaction volume.

### Task 5.3 — Historically-inspired "lost BTC" simulation  (Design — not yet implemented)

Some non-miner bots are marked permanently inactive — they hold BTC forever (simulating early wallets lost to hardware failure, discarded drives, forgotten keys). Inspired by Chainalysis estimates of ~20% of total BTC supply lost.

Design notes:
- Inactive bots: `IsActive = false`, address still receives mining rewards proportionally (simulated)
- Some "sleeping whales": `ReactivationBlockHeight` set to a future block number — they "come back" when that block is mined, simulating someone who found their old wallet/keys
- Both fields are already present in `BotWalletRecord` and persisted in `bot_wallet_registry.json`

### Task 5.4 — Bot wallet registry  ✓ DONE

**File**: `Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs`  
**Persistence**: `user://bot_wallet_registry.json`  
```json
{
  "miners": [
    { "nodeId": "bot_1", "address": "gm1q...", "signingPublicKeyBase64": "...", "signingPrivateKeyBase64": "...", "secp256k1PublicKeyBase64": "...", "isActive": true }
  ],
  "nonMiners": [
    { "nodeId": "non_miner_1", "address": "gm1q...", "isActive": true },
    { "nodeId": "non_miner_2", "address": "gm1q...", "isActive": false, "reactivationBlockHeight": 4381 }
  ]
}
```

---

## Phase 6 — BlockExplorer Transfer Refactor  TODO

**Decision**: Option B confirmed — `BTCWallet` owns all send functionality. BlockExplorer remains read-only for the player.

**Existing transfer logic in BlockExplorer**: remove it entirely.

**New dev-only transfer scene**:  
**Path**: `Screens/DevTransferTool/DevTransferTool.tscn` + `.cs`  
**Purpose**: developer testing tool for simulating BTC transfers between any participants (bots, player, casino). Not accessible from MainMenu in player build.  
**Features**:
- Dropdown: select sender (all registered wallets: player base, casino, all bots)
- Text field: recipient address (or dropdown select)
- Amount field
- Fee field (1–10 BTC range per OQ-9)
- [Submit Transaction] → adds to mempool, signed with sender's key
- Shows pending mempool transactions
- "Mine Next Block" button (dev shortcut — mines immediately)

**Navigation**: accessible from a hidden dev launcher or MainMenu dev mode only.

---

## Phase 7 — Casino Wallet (Dev Scene)  TODO

Casino wallet is created at game startup (Phase 3). It is able to participate in the blockchain immediately but it will not be required until BTC/SC trading (october 3rd 2009) in planned basic mode.

### CasinoFinances scene integration (planned)

- Label: casino base address (`gm1q...`)
- [Show/Copy Seed Words] button → popup with 3 words + copy button (no "first time only" restriction — dev access)
- [Open Passphrase Wallet] → enter passphrase word → show derived address → copy
- Note: "Save this passphrase in the in-game notepad" reminder (notepad feature — future, Phase 8+)
- Casino's `gm1q...` address is pre-registered in the blockchain address registry for SC↔BTC trades (future P7)

---

## Phase 8 — In-Game Notepad  (Future, not yet designed)

**Trigger**: Player needs a place to record passphrase words and wallet addresses. Currently the only guidance is "save it offline."

**Design placeholder**:
- Simple persistent text area per-address or global
- Can save custom labels ("My passphrase wallet #1 = gm1q...", "passphrase = oak")
- Stored in `user://notepad.json`
- Accessible from BTCWallet and any address-related screen

---

## Implementation Order (recommended)

```
0.3 Secp256k1.cs
→ 0.4 CryptoUtils updates + test vectors verified
→ 0.5 Wallet address persistence (NodeAgent + NetworkRoot)
→ 1.1 Rename wordlist file
→ 1.2 WordlistBootstrapper
→ 2   WalletModels.cs
→ 3   WalletInitializationService (startup sequence)
→ 5.1 NodeAgent address format (gm1q... live everywhere)
→ 4   BTCWallet scene (player wallet end-to-end)
→ 5.4 BotWalletRegistry
→ 6   BlockExplorer cleanup + DevTransferTool
→ 7   Casino wallet CasinoFinances scene hooks
→ 8   Notepad (design TBD)
```

---

## Open Questions

**OQ-1 — RESOLVED**: secp256k1 for all address derivation; P-256 stays for signing. See Phase 0.3/0.4.

**OQ-2 — RESOLVED**: UTXO model (scan all blockchain transactions per address). Balance display is educational — shows UTXO count tooltip. Confirmed-only balance displayed.

**OQ-3 — RESOLVED**: Confirmed-only. Player sees "Available" vs "Pending outgoing" in BTCWallet.

**OQ-4 — RESOLVED**: Full privacy intended. No helper system. Passphrase wallets exist only in the blockchain record; no app-level tracing between base and passphrase addresses.

**OQ-5 — RESOLVED**: No passphrase wallet history. User learns empirically (copy address → re-enter same passphrase → compare). Tooltip in BTCWallet explains this mechanic.

**OQ-6 — RESOLVED**: `user://wordlist_256.json` for the generated 256-word subset. `res://` is read-only in exports.

**OQ-7 — RESOLVED**: No migration needed. Old hex-substring addresses are gone; everything regenerates with gm1q... addresses.

**OQ-8 — RESOLVED**: Start with 4 miner bots + 10 non-miner bots. Growth cadence will track BTC historical data. Some bots permanently inactive (lost BTC sim). Some sleeping whales (future reactivation at defined block heights).

**OQ-9 — RESOLVED**: Sender chooses fee: 1–10 BTC for now. Post-Oct 3, 2009 (when BTC becomes tradeable for SC): fee will be calculated as BTC amount that represents a roughly fixed SC value adjusted for network traffic.

**OQ-10 — RESOLVED**: BTCWallet is player-facing from day one. Casino dev scene is dev-only.

---

**OQ-11 — RESOLVED**: Scan on demand (Option B). Add in-memory UTXO index when block count approaches ~1000.

**OQ-12 — RESOLVED**: Loop with counter suffix implemented in `CryptoUtils.DeriveGmAddress()`. In practice the loop always exits on the first iteration (probability of needing retry is ~1 in 2^128). The same pattern applies to `DeriveSigningKeypair()` for P-256 D parameter validity. See `Documentation/ProjectDesignManual.md` Chapter 2 for plain-language explanation.

**OQ-13 — RESOLVED**: Option A — provision all non-miner bots with signing keys at creation time, stored in registry. `BotWalletRecord` will include `SigningPrivateKeyBase64` from the start.

**OQ-14 — RESOLVED**: Yes, miner collects all transaction fees in the block. Fee amount per transaction = sender-chosen value (1–10 BTC for now). Fee collection added to coinbase reward calculation in Phase 5 / Phase 6 implementation.

**OQ-15 — RESOLVED with scope clarification**: Historical simulation covers 2009–2026 as the long-term target. **2009–2012 is the initial design and testing window** (Basic Mode v0.1). Bot growth cadence, dormant wallet %, and whale reactivation events will be calibrated against real BTC historical data for this window first. Full dataset expansion to 2026 is a future milestone. Research and sources documented in `Documentation/DESIGN_OVERVIEW.md` once the cadence system is implemented.

**OQ-16 — RESOLVED**: `DeriveSigningKeypair()` wraps `ECDsa.Create(ecParams)` in a try/catch loop. If `CryptographicException` is thrown (D outside P-256 valid range), the attempt counter increments and the next seed is `SHA256("sign:" + seedPhrase + ":N")`. Same suffix convention as OQ-12, applied to the "sign:" prefix path. Both `GenerateWallet()` (random bytes path) and `DeriveSigningKeypair()` (seed phrase path) use the same safety loop pattern. See `Documentation/ProjectDesignManual.md` Chapter 8 for the plain-language explanation.

---

## Files Summary

| File | Phase | Status |
|---|---|---|
| `Scripts/BlockchainPort/Blockchain/Ripemd160.cs` | 0.1 | ✓ DONE |
| `Scripts/BlockchainPort/Blockchain/Bech32.cs` | 0.2 | ✓ DONE |
| `Scripts/BlockchainPort/Blockchain/Secp256k1.cs` | 0.3 | ✓ DONE |
| `Scripts/BlockchainPort/Blockchain/CryptoUtils.cs` | 0.4 | ✓ DONE |
| `Scripts/BlockchainPort/Blockchain/Models.cs` | 0.4 | ✓ DONE |
| `Scripts/BlockchainPort/Simulation/NodeAgent.cs` | 0.4 + 0.5 | ✓ DONE |
| `Scripts/BlockchainPort/Blockchain/BlockchainService.cs` | 0.4 | ✓ DONE |
| `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` | 0.5 | ✓ DONE |
| `Documentation/ProjectDesignManual.md` | 0.1–0.5 | ✓ DONE |
| `Scripts/BlockchainPort/Blockchain/WalletModels.cs` | 2 | ✓ DONE |
| `Scripts/Services/WordlistBootstrapper.cs` | 1.2 | ✓ DONE |
| `Scripts/Services/WalletInitializationService.cs` | 3 | ✓ DONE |
| `Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs` | 5.4 | TODO |
| `Screens/BTCWallet/BTCWallet.tscn` | 4 | ✓ DONE |
| `Screens/BTCWallet/BTCWallet.cs` | 4 | ✓ DONE |
| `Screens/DevTransferTool/DevTransferTool.tscn` | 6 | TODO |
| `Screens/DevTransferTool/DevTransferTool.cs` | 6 | TODO |

| File | Phase | Change |
|---|---|---|
| `Scripts/BlockchainPort/BIP-0039/bip39_2048.txt` | 1.1 | ✓ DONE (renamed from `2048WordsList`) |
| `Scripts/Services/CalendarTimeService.cs` | 1.3 + 3 | ✓ DONE — `EnsureWordlist()` + `WalletInitializationService.EnsureAll()` + `EnsureGameEpochInitialized()` |
| `Scripts/Services/SceneManager.cs` | 4 | ✓ DONE — `BTCWallet` added to `SceneId` + `Paths` |
| `Screens/BlockExplorer/BlockExplorer.cs` | 6 | Remove existing transfer logic |
| `Screens/MainMenu/MainMenu.tscn` + `.cs` | 4 | ✓ DONE — `BTCWalletBtn` added and wired |

---

*Last updated: 2026-06-12*
