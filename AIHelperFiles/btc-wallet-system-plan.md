# BTC Wallet Address System — Implementation Plan

**Status**: Phase 0.1 ✓  Phase 0.2 ✓  Phase 0.3 ✓  Phase 0.4 ✓  Phase 0.5 ✓  Phase 1.1 ✓  —  Next: Phase 1.2 (WordlistBootstrapper)
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
- `CryptoUtils.cs` ✓ — `DeriveGmAddress()`, `DeriveSigningKeypair()`, updated `GenerateWallet()` (4-tuple), updated `DeriveAddressFromPublicKey()` (now takes secp256k1 compressed pubkey base64)
- `NodeAgent.cs` ✓ — `WalletSecp256k1PublicKey` property; constructor uses 4-tuple; `CreateSignedTransaction()` sets `tx.Secp256k1PublicKeyBase64`
- `Models.cs` ✓ — `Transaction` has new `Secp256k1PublicKeyBase64` field (address verification) alongside existing `PublicKeyBase64` (P-256 signing)
- `BlockchainService.cs` ✓ — `ValidateTransactionSignature()` uses `tx.Secp256k1PublicKeyBase64` for address check; `tx.PublicKeyBase64` still used by `CryptoUtils.Verify()`
- `Scripts/BlockchainPort/BIP-0039/bip39_2048.txt` ✓ — renamed from `2048WordsList`; 2048 BIP39 words one per line; read-only source for `WordlistBootstrapper`; requires `*.txt` in export preset include filter when presets are configured
- `Documentation/ProjectDesignManual.md` ✓ — Chapter 1–7 covering Phases 0.1–0.3 in full detail

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

### Task 1.2 — Implement `WordlistBootstrapper`  TODO

**File**: `Scripts/Services/WordlistBootstrapper.cs`  
**Note on paths**: `res://` is read-only in Godot exported builds. The 2048-word source stays at `res://` (read-only). The generated 256-word subset is saved to `user://wordlist_256.json`.

**`user://wordlist_256.json` format**:
```json
{
  "GeneratedAt": "2009-01-03T18:15:06Z",
  "Words": [
    { "Index": 1, "Word": "abandon" },
    { "Index": 2, "Word": "ability" }
  ]
}
```

**Logic** (idempotent):
```
if user://wordlist_256.json exists → load and return
else:
  1. FileAccess.Open("res://Scripts/BlockchainPort/BIP-0039/bip39_2048.txt")
  2. Read all lines → List<string> (2048 words)
  3. Fisher-Yates shuffle → take first 256
  4. Sort alphabetically
  5. Assign index 1..256
  6. Save JSON to user://wordlist_256.json
  7. Return List<WordEntry>
```

**Word selection rule** (for 3-word seed generation):
- Pick A, B, C independently from the 256 words
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

### Task 1.3 — Wire into startup  TODO

`WordlistBootstrapper.EnsureWordlist()` called in `CalendarTimeService._Ready()` before wallet initialization.

---

## Phase 2 — Wallet Persistence Models  TODO

**File**: `Scripts/BlockchainPort/Blockchain/WalletModels.cs`

```csharp
public record PlayerWalletState(
    string[] SeedWords,   // 3 words; never 4 (passphrase wallets not persisted)
    string BaseAddress,   // gm1q... derived at save time for quick read without re-deriving
    bool HasSeenSeedPopup // true after user dismisses the first-launch popup
);

public record CasinoWalletState(
    string[] SeedWords,
    string BaseAddress
);

public record BotWalletRecord(
    string NodeId,
    string Address        // gm1q... only; no seed words; private key not stored
);
```

---

## Phase 3 — Game Startup Wallet Initialization  TODO

**File**: `Scripts/Services/WalletInitializationService.cs`  

**Startup sequence** (ordering matters):
```
CalendarTimeService._Ready()
  → WordlistBootstrapper.EnsureWordlist()        [Phase 1]
  → WalletInitializationService.EnsureAll()      [Phase 3]
     → EnsurePlayerWallet()
     → EnsureCasinoWallet()
```

**Player wallet**: if `user://wallet_state.json` missing → generate 3 words → derive address → save → set `HasSeenSeedPopup = false`.  
**Casino wallet**: if `user://casino_wallet_state.json` missing → generate 3 words (separate RNG call) → derive address → save.  
Block mining rewards (coinbase) are automatically sent to `PlayerWalletState.BaseAddress`. Player cannot change this address in Basic Mode.

---

## Phase 4 — BTCWallet Scene  TODO

**Path**: `Screens/BTCWallet/BTCWallet.tscn` + `BTCWallet.cs`  
**Navigation**: MainMenu → BTCWallet (add to `SceneManager.SceneId` + `Paths`)  
**Player-facing from day one**: full polish required.

### Scene layout

```
[TopBar: < Main Menu | StatusBar]

=== BASE WALLET MODE (default) ===
  "Base Wallet"
  "Deposit Address"
  [gm1q...address]  [Copy]
  "Balance: X.XXXXXXXX BTC"  (confirmed only — see OQ-3)
  "Pending outgoing: Y.XXXXXXXX BTC"  (if any unconfirmed sends)
  [Send BTC]   [Open Passphrase Wallet →]

=== PASSPHRASE WALLET MODE ===
  [IF locked]
    "Enter your passphrase word"
    [LineEdit]  [Unlock]
    ⚠ "Save your passphrase offline. It cannot be recovered from this app."
  [IF unlocked]
    "Passphrase Wallet"
    "Deposit Address"
    [gm1q...passphrase-address]  [Copy]
    "Balance: X.XXXXXXXX BTC"
    "Pending outgoing: Y.XXXXXXXX BTC"
    [Send BTC]   [← Base Wallet]
    ⚠ "Save your passphrase. This reminder always appears when using a passphrase wallet."
```

### First-launch popup (shown when `HasSeenSeedPopup == false`)

- Shown even if the player has already been betting for hours — wallets are created at game start
- Displays 3 words clearly (large font, separated)
- [Copy to clipboard]
- Bold warning: "Write these words down. This is the only time they will be shown automatically."
- [I have saved my words] → sets `HasSeenSeedPopup = true`, saves state, dismisses popup

### Passphrase wallet mechanics

- Any word from the 256-word list (or any string, actually — not validated against wordlist) can be a passphrase
- `CryptoUtils.DeriveGmAddress("word1 word2 word3 passphrase")` → always the same address for the same 4 inputs
- No storage, no validation of "correct" passphrase — any input generates a valid wallet
- To confirm you're accessing the same passphrase wallet: copy the address, log out, log in again, compare addresses
- This mechanic is explained in a small help tooltip on the passphrase input field
- Passphrase is cleared from memory (field emptied) when returning to base wallet
- Sign to send: derive signing keypair from `"sign:word1 word2 word3 passphrase"` on the fly, sign, discard

### UTXO balance display (OQ-2 decision: UTXO scan)

- Balance = sum of all unspent transaction outputs to this address in the blockchain
- "UTXO" tooltip/label visible (educational — see Notes below)
- Didactic note near balance: "Your balance is made up of X unspent outputs (UTXOs)"
- Confirmed balance: only outputs in mined blocks count
- Pending outgoing: transactions in mempool from this address reduce "available to send"

---

## Phase 5 — NodeAgent & Bot Wallet Update  TODO

### Task 5.1 — Update `CryptoUtils.GenerateWallet()`

All `NodeAgent` mining bots call `GenerateWallet()`. After update: address becomes `gm1q...` automatically. No changes to `NodeAgent.cs` needed.

### Task 5.2 — Bot wallet creation timeline (OQ-8 resolved)

**Initial population** (at game start, `WalletInitializationService.EnsureAll()`):
- 4 miner bots (`NodeAgent`, already have keys for signing)
- 10 non-miner bots (`BotWalletRecord`, address only for now)

**Growth cadence**: historically-proportional expansion tracked in a future `BotScheduler` service.  
Reference data to research: BTC wallet count growth 2009–2012, mining node count, transaction volume.

**Non-miner bots will eventually send** — `BotWalletRecord` needs a signing key added when sending is enabled. Design now: add optional `string SigningPrivateKeyBase64` field (null until bot needs to send).

### Task 5.3 — Historically-inspired "lost BTC" simulation  (Design — not yet implemented)

Some non-miner bots are marked permanently inactive — they hold BTC forever (simulating early wallets lost to hardware failure, discarded drives, forgotten keys). Inspired by Chainalysis estimates of ~20% of total BTC supply lost.

Design notes:
- Inactive bots: `IsActive = false`, `WalletAddress` still receives mining rewards proportionally (simulated)
- Some "sleeping whales": `ReactivationBlockHeight` set to a future block number — they "come back" when that block is mined, simulating someone who found their old wallet/keys
- The number of lost/inactive bots should track historical % of supply in dormant wallets

### Task 5.4 — Bot wallet registry  TODO

**File**: `Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs`  
**Persistence**: `user://bot_wallet_registry.json`  
```json
{
  "Bots": [
    { "NodeId": "bot_001", "Address": "gm1q...", "IsActive": true, "ReactivationBlockHeight": null },
    { "NodeId": "bot_002", "Address": "gm1q...", "IsActive": false, "ReactivationBlockHeight": 4381 }
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
| `Scripts/BlockchainPort/Blockchain/WalletModels.cs` | 2 | TODO |
| `Scripts/Services/WordlistBootstrapper.cs` | 1.2 | TODO |
| `Scripts/Services/WalletInitializationService.cs` | 3 | TODO |
| `Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs` | 5.4 | TODO |
| `Screens/BTCWallet/BTCWallet.tscn` | 4 | TODO |
| `Screens/BTCWallet/BTCWallet.cs` | 4 | TODO |
| `Screens/DevTransferTool/DevTransferTool.tscn` | 6 | TODO |
| `Screens/DevTransferTool/DevTransferTool.cs` | 6 | TODO |

| File | Phase | Change |
|---|---|---|
| `Scripts/BlockchainPort/BIP-0039/bip39_2048.txt` | 1.1 | ✓ DONE (renamed from `2048WordsList`) |
| `Scripts/Services/CalendarTimeService.cs` | 1.3 + 3 | Add `WordlistBootstrapper` + `WalletInitializationService` calls in `_Ready()` |
| `Scripts/Services/SceneManager.cs` | 4 | Add `BTCWallet` to `SceneId` enum + `Paths` |
| `Screens/BlockExplorer/BlockExplorer.cs` | 6 | Remove existing transfer logic |
| `Screens/MainMenu/MainMenu.tscn` + `.cs` | 4 | Add BTCWallet navigation button |

---

*Last updated: 2026-06-12*
