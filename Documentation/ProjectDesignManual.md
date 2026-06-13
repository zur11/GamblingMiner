# GamblingMiner — Project Design Manual

> **Audience**: This document is written for developers first and eventually adapted for players.  
> Developers: you will find implementation details, code references, and rationale for every design decision.  
> Players: each section opens with a plain-language summary before the technical dive.

---

## Chapter 1 — How Bitcoin Addresses Work in GamblingMiner

### The Short Version (for everyone)

Every participant in the GamblingMiner blockchain — the player, the casino, and all miner and non-miner bots — has at least one Bitcoin-style address. An address is like an email address for money: you can share it publicly so others know where to send BTC, but only the person who holds the secret behind it can spend what's received there.

In GamblingMiner, addresses look like this:

```
gm1qxy2kgdygjrsqtzq2n0yrf2493p83kkfjhx0wlh
```

The `gm` at the start marks it as a GamblingMiner address (instead of Bitcoin's `bc`). The rest follows identical mathematical rules to real Bitcoin mainnet P2WPKH (Native SegWit) addresses. If you changed `gm` to `bc`, these addresses would be valid on the real Bitcoin network.

---

### 1.1 — The Derivation Pipeline

Every address in GamblingMiner is produced by this exact sequence:

```
Secret phrase (3 or 4 words, or 32 random bytes for bots)
    │
    ▼  SHA-256 hash
32-byte private key
    │
    ▼  secp256k1 elliptic curve multiplication   [Secp256k1.cs]
33-byte compressed public key
    │
    ▼  SHA-256, then RIPEMD-160                  [Ripemd160.cs]
20-byte public key hash
    │
    ▼  Bech32 encoding with prefix "gm"          [Bech32.cs]
gm1q... address (42 characters)
```

Three cryptographic steps, three C# files. Each step is described in its own section below.

---

### 1.2 — Why Three Steps?

Each step solves a specific problem:

| Step | Problem it solves |
|---|---|
| secp256k1 | Converts a secret number into a public one that can't be reversed |
| RIPEMD-160 | Shortens the 33-byte public key into 20 bytes without collision risk |
| Bech32 | Encodes 20 bytes as readable text with a built-in typo-detection checksum |

Bitcoin uses all three for the same reasons. GamblingMiner follows the same design because it makes addresses compatible with the same math, tooling, and mental model as real Bitcoin — which is the point of the simulation.

---

## Chapter 2 — secp256k1: The Secret-to-Public Step

**File**: `Scripts/BlockchainPort/Blockchain/Secp256k1.cs`  
**Status**: Implemented (Phase 0.3)

### Plain Language

Imagine a very large piece of graph paper — so large that it would cover the known universe. On this paper, a specific mathematical curve is drawn. Every point on this curve has coordinates (X, Y). One special point, called **G** (the generator), is agreed upon by everyone in the world who uses Bitcoin.

Your private key is just a big number — let's call it `k`. Your public key is the result of adding the point G to itself exactly `k` times. This is called **scalar multiplication**: `public key = k × G`.

The magic — and the security — comes from this: going from `k` to `k × G` is fast (a computer does it in milliseconds), but going backwards from `k × G` to `k` would take longer than the age of the universe even with the fastest computers we can imagine. This one-way property is what makes it safe to share your public key (and your address) without revealing your private key.

### The Curve: secp256k1

The curve used by Bitcoin and GamblingMiner is called **secp256k1**. Its equation is:

```
y² = x³ + 7   (mod p)
```

where `p` is the specific enormous prime number `2²⁵⁶ − 2³² − 977`. The curve exists in a finite field of numbers rather than on real-number graph paper, but the arithmetic rules are analogous.

Key parameters (hardcoded in `Secp256k1.cs`):

```
p  = FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F
n  = FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141
Gx = 79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798
Gy = 483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8
```

- `p` is the field prime (all arithmetic is done modulo this number)
- `n` is the curve order (the number of valid points; also the maximum private key value minus 1)
- `(Gx, Gy)` is the generator point G

### The Compressed Public Key

A point on the curve has two coordinates: X and Y. But since the curve equation ties Y to X (given X, Y can only be one of two values — one even, one odd), we only need to transmit X plus a one-byte hint about which Y to use:

- `0x02` + X → Y is even  
- `0x03` + X → Y is odd  

This is the **compressed public key**: 33 bytes. It is what gets fed into the next step.

### Implementation Details

`Secp256k1.GetCompressedPublicKey(byte[] privateKey)`:

1. Converts the 32-byte big-endian private key to a `BigInteger` scalar `k`
2. Validates `1 ≤ k ≤ n-1`  
3. Calls `ScalarMul(G, k)` using double-and-add:
   - Iterates over each bit of `k` from LSB to MSB
   - For each `1` bit: add current doubling-point to result
   - For each bit: double the current point
4. Reads Y parity → sets prefix byte `0x02` or `0x03`
5. Returns `[prefix] + [32-byte big-endian X]`

The modular inverse needed in point addition is computed via **Fermat's little theorem**: `a⁻¹ ≡ a^(p−2) mod p`, which works because `p` is prime.

**Test vector** (verifiable in any secp256k1 tool):
- Private key: `0x0000...0001` (value = 1)
- Expected compressed pubkey: `0279BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798`
  (This is just G itself: 1 × G = G)

### The Private Key Validity Edge Case

The valid range for a secp256k1 private key is `[1, n−1]`. The curve order `n` is slightly less than `2²⁵⁶`. SHA-256 produces a 256-bit output, meaning roughly 1 in every `2¹²⁸` key derivations would fall outside this range.

In numerical terms: that is `1 in 340,282,366,920,938,463,463,374,607,431,768,211,456`.  

This will never happen in practice. However, our code in `CryptoUtils.DeriveGmAddress()` handles it cleanly: if the SHA-256 of the seed phrase happens to be outside the valid range, it tries `SHA256(phrase + ":1")`, then `":2"`, etc. In all practical cases, iteration `0` (no suffix) is used and the address is stable.

---

## Chapter 3 — RIPEMD-160: The Fingerprinting Step

**File**: `Scripts/BlockchainPort/Blockchain/Ripemd160.cs`  
**Status**: Implemented (Phase 0.1)

### Plain Language

After we have the 33-byte compressed public key, we need to shrink it to something shorter that can fit nicely in an address. We also want any change in the input — even a single bit — to produce a completely different output. Hash functions do exactly this.

Bitcoin uses two hash functions back-to-back:
1. **SHA-256** (already in .NET's standard library)
2. **RIPEMD-160** (not in .NET 8 on most platforms — so we wrote our own)

The combination `RIPEMD160(SHA256(pubkey))` is called **Hash160** in Bitcoin documentation. It produces 20 bytes — short enough for readable addresses, but statistically impossible to find two different public keys that produce the same 20-byte result.

### Why RIPEMD-160 specifically?

Bitcoin's creator Satoshi Nakamoto chose this combination for its dual-hash security model: even if SHA-256 were ever broken, an attacker would still need to break RIPEMD-160 (and vice versa) to forge an address. For GamblingMiner this is the same rationale — we follow the real Bitcoin standard to make our simulation mathematically equivalent.

### Why we had to implement it ourselves

.NET 8 removed `RIPEMD160.Create()` from `System.Security.Cryptography` on non-Windows platforms (and even on Windows it's an unreliable path). To ensure the game builds and runs identically on any platform, `Ripemd160.cs` is a complete pure-C# implementation based on the RFC 2286 specification.

The algorithm processes data in 64-byte blocks using two parallel computation tracks (left and right), each running 80 rounds with a different sequence of bitwise operations, message word selections, and rotation amounts. The two tracks are combined at the end of each block.

### Usage

```csharp
byte[] hash20 = Ripemd160.Hash(SHA256.HashData(compressedPubKey));
```

**Test vectors** (use these to verify the implementation produces correct results):

| Input | Expected RIPEMD-160 output |
|---|---|
| `""` (empty) | `9c1185a5c5e9fc54612808977ee8f548b2258d31` |
| `"abc"` | `8eb208f7e05d987a9b044a8e98c6b087f15a0bfc` |

---

## Chapter 4 — Bech32: The Encoding Step

**File**: `Scripts/BlockchainPort/Blockchain/Bech32.cs`  
**Status**: Implemented (Phase 0.2)

### Plain Language

We now have 20 bytes of public key hash. But raw bytes are not user-friendly: they look like `9c1185a5c5e9fc54612808977ee8f548` and are easy to mistype or corrupt.

**Bech32** solves this by encoding the bytes as a sequence of characters from a carefully chosen alphabet, and appending a **checksum** at the end. The checksum catches almost any single-character typo, transposition, or copy-paste error. A Bech32 address that has been corrupted in transit will fail validation — the funds won't be lost by sending to a typo.

### The Format

A GamblingMiner address has this structure:

```
gm  1  q  [32 characters]  [6 checksum characters]
│   │  │
│   │  └─ witness version (0 = P2WPKH), encoded as 'q' in Bech32 alphabet
│   └──── separator (always '1')
└──────── HRP: Human-Readable Part — "gm" for GamblingMiner
```

Total length: **42 characters**.

### The Bech32 Alphabet

Instead of base64 (which has uppercase, lowercase, `+`, `/`, `=`), Bech32 uses 32 characters:

```
q p z r y 9 x 8 g f 2 t v d w 0 s 3 j n 5 4 k h c e 6 m u a 7 l
```

This alphabet was specifically designed to avoid visually similar characters: no `0`/`O`, no `1`/`l`/`I`, no `b`/`6`, no mixed case. A handwritten or verbally-communicated address is far less likely to be misrecorded.

### The Checksum

The 6 trailing characters are not part of the data — they are a polynomial checksum over the entire address (HRP + data). The polynomial is defined over GF(2⁵) and can detect any single substitution error, any single transposition of adjacent characters, or any single extra/deleted character.

### GamblingMiner-specific constant

```csharp
Bech32.GameHrp = "gm"
```

This is the only game-specific value in the encoder. Every other part of the algorithm is identical to Bitcoin's mainnet HRP `"bc"`. This means: to make GamblingMiner addresses validate on Bitcoin mainnet, the only change needed is this one constant.

### Usage

```csharp
// Encode a 20-byte hash as a gm1q... address
string address = Bech32.Encode(Bech32.GameHrp, witnessVersion: 0x00, witnessProgram: hash20);

// Validate an address
bool valid = Bech32.IsValidGmAddress(address);

// Decode (for internal use)
if (Bech32.TryDecode(address, out string hrp, out byte version, out byte[] program))
{
    // hrp == "gm", version == 0, program == 20-byte hash
}
```

---

## Chapter 5 — Putting It All Together: A Complete Example

This is what `CryptoUtils.DeriveGmAddress("abandon ability able")` does, step by step.

**Step 1: SHA-256 of the seed phrase**
```
Input:  "abandon ability able"
Output: 32 bytes (the private key scalar k)
        e.g. a7f823... (varies by exact phrase)
```

**Step 2: secp256k1 scalar multiplication** (`Secp256k1.GetCompressedPublicKey`)
```
k (32 bytes) → k × G on secp256k1 → point (X, Y)
Y is even → prefix 0x02
Output: 33 bytes   02[X as 32 bytes]
```

**Step 3: Hash160** (`SHA256.HashData` then `Ripemd160.Hash`)
```
Input:  33-byte compressed public key
SHA256: 32 bytes
RIPEMD160 of SHA256: 20 bytes (this is the witness program)
```

**Step 4: Bech32 encoding** (`Bech32.Encode`)
```
HRP:             "gm"
witness version: 0x00 (→ 'q')
witness program: 20 bytes → 32 base32 groups
checksum:        6 characters
Final address:   gm1q... (42 chars)
```

**The same phrase always produces the same address.** This is deterministic derivation — the backbone of the wallet system. A player who knows their three words can always recover their address, and can sign transactions from that address without storing any private key on disk.

---

## Chapter 6 — Seed Phrase System

### Why three words?

A seed phrase of three words chosen from 256 possible words gives:

```
Total ordered combinations (with max-one-repeat rule): 256 × 255 × 257 = ~16.7 million
```

That is 16.7 million unique wallets from a 256-word subset. With ~600 total participants planned (player, casino, ~100 miner bots, ~500 non-miner bots), the collision probability is approximately:

```
P(any collision among 600) ≈ 600² / (2 × 16,700,000) ≈ 0.001%
```

Essentially zero. And unlike purely random bytes, three real English words are memorable and writeable by a human, which is exactly the point.

### The 256-word subset

At first game launch, `WordlistBootstrapper` takes the full 2048-word BIP39 English list and randomly selects 256 words, sorts them alphabetically, and saves them to `user://wordlist_256.json`. This means every game installation has its own unique vocabulary — making each "world" subtly different and addresses from one installation not accidentally reusable in another.

### Passphrase wallets (4-word derivation)

Adding a fourth word (the passphrase) to the seed phrase produces a completely different private key, and therefore a completely different address:

```
SHA256("word1 word2 word3")           → address A (base wallet)
SHA256("word1 word2 word3 passphrase") → address B (passphrase wallet)
```

Address B exists independently on the blockchain. It receives and sends BTC like any other address. The game has no record that A and B belong to the same person — that knowledge exists only in the player's memory (or notes). This mirrors how real Bitcoin privacy works: address unlinkability.

---

## Chapter 7 — Signing and Verification

### Why two separate curves?

secp256k1 is used for **address derivation** (one-time computation at wallet creation).  
P-256 (existing `CryptoUtils.Sign()` pipeline) is used for **transaction signing** (every time a transaction is created or verified).

This split exists because:
- Implementing secp256k1 signing (in addition to point multiplication) would require ~300 more lines and the ECDSA signing algorithm with RFC 6979 deterministic nonce generation
- Transaction signatures in GamblingMiner are verified only within the game — no external tool validates them
- P-256 ECDSA is already implemented, tested, and available via .NET's standard library

The practical consequence: a GamblingMiner transaction's signature cannot be validated by Bitcoin tooling, but the **address** derived from the same private key can be independently verified by any Bitcoin address calculator.

### Signing keys for seed-phrase wallets

When the player or casino needs to sign a transaction (to send BTC), a P-256 signing key is derived deterministically from their seed phrase with a prefix to prevent key reuse:

```csharp
byte[] signingKeyMaterial = SHA256.HashData(Encoding.UTF8.GetBytes("sign:" + seedPhrase));
// → used to create an ECDsa P-256 key via ECParameters.D
```

The `"sign:"` prefix ensures the signing key is a different 32 bytes than the secp256k1 key used for the address — the same raw bytes cannot accidentally be reused for both purposes.

---

## Chapter 8 — Phase 0.4: Wiring the Pipeline into the Game

**Files changed**: `CryptoUtils.cs`, `Models.cs`, `NodeAgent.cs`, `BlockchainService.cs`  
**Status**: Implemented (Phase 0.4)

### The Problem Phase 0.4 Solved

Before this phase, `CryptoUtils.DeriveAddressFromPublicKey()` accepted a P-256 SubjectPublicKeyInfo blob and produced a 40-character hex string (old address format). The same field — `Transaction.PublicKeyBase64` — was used in `BlockchainService.ValidateTransactionSignature()` for two unrelated purposes:

1. **Address verification**: `DeriveAddressFromPublicKey(tx.PublicKeyBase64) == tx.Sender`
2. **Signature verification**: `CryptoUtils.Verify(payload, tx.SignatureBase64, tx.PublicKeyBase64)`

In the new system these require incompatible key types:
- Address verification needs the **secp256k1 compressed public key** (33 bytes, Bitcoin format)
- Signature verification needs the **P-256 SubjectPublicKeyInfo** (.NET ECDSA format)

The same field cannot hold both. This chapter describes how the split was made.

### The Solution: Two Fields for Two Roles

A new field was added to `Transaction`:

```csharp
// Models.cs
public string Secp256k1PublicKeyBase64 { get; set; } = string.Empty;  // for address ownership check
public string PublicKeyBase64 { get; set; } = string.Empty;           // for P-256 signature check (unchanged)
```

The validation method in `BlockchainService` was updated to use each field for its correct purpose:

```csharp
// BlockchainService.ValidateTransactionSignature() — simplified
if (/* coinbase */) return true;

if (any field is empty) return false;

// Address ownership: secp256k1 public key → Hash160 → Bech32 must match Sender
if (!Equals(tx.Sender, CryptoUtils.DeriveAddressFromPublicKey(tx.Secp256k1PublicKeyBase64)))
    return false;

// Signature: P-256 signing key verifies the transaction payload
return CryptoUtils.Verify(payload, tx.SignatureBase64, tx.PublicKeyBase64);
```

### Updated `GenerateWallet()` — Now a 4-Tuple

`CryptoUtils.GenerateWallet()` now returns four values:

```csharp
(string address,
 string signingPublicKeyBase64,    // P-256, used by Verify()
 string signingPrivateKeyBase64,   // P-256 PKCS8, used by Sign()
 string secp256k1PublicKeyBase64)  // secp256k1 compressed pubkey, used by DeriveAddressFromPublicKey()
```

Internally, 32 random bytes serve as the **source of truth** for the wallet. Those bytes are used for:
- The secp256k1 scalar (→ compressed pubkey → address)
- The P-256 `ECParameters.D` (→ signing keypair)

Both derivations from the same key material are independent: secp256k1 and P-256 are different curves with different orders, so the same 32 bytes produce entirely different public keys on each curve.

`NodeAgent` was updated to destructure the 4-tuple and store `WalletSecp256k1PublicKey`. `CreateSignedTransaction()` now sets both `tx.PublicKeyBase64` and `tx.Secp256k1PublicKeyBase64`.

### The P-256 Validity Edge Case (OQ-16)

When creating a P-256 key via `ECParameters.D = someBytes`, the bytes must be in the valid range for the P-256 curve's scalar field. If they are not, `ECDsa.Create(ecParams)` throws a `CryptographicException`. This is the P-256 equivalent of the secp256k1 OQ-12 edge case described in Chapter 2.

The fix uses the same retry-with-suffix counter pattern:

```csharp
// DeriveSigningKeypair() — simplified
int attempt = 0;
while (true)
{
    string input = attempt == 0 ? ("sign:" + seedPhrase) : ("sign:" + seedPhrase + ":" + attempt);
    byte[] seed  = SHA256.HashData(Encoding.UTF8.GetBytes(input));
    try
    {
        using ECDsa ecdsa = ECDsa.Create(new ECParameters { Curve = nistP256, D = seed });
        return (base64PubKey, base64PrivKey);
    }
    catch (CryptographicException) { attempt++; }
}
```

The same suffix convention (`":1"`, `":2"`) is used for both OQ-12 (secp256k1) and OQ-16 (P-256), in their respective derivation paths (`DeriveGmAddress` uses the bare seed phrase; `DeriveSigningKeypair` uses the `"sign:"` prefix). This ensures that:
- Both methods are deterministic: the same input always produces the same output
- The two derivations never interfere with each other

The probability of needing even one retry is approximately 1 in 2¹²⁸ — effectively zero. The loop exists purely as a correctness guarantee, not as a practical concern.

### Updated `DeriveAddressFromPublicKey()`

The signature changed:

```
Old: DeriveAddressFromPublicKey(string p256SubjectPublicKeyInfoBase64) → string (40-char hex)
New: DeriveAddressFromPublicKey(string secp256k1CompressedPubKeyBase64) → string (gm1q...)
```

Internally: `base64 → 33 bytes → RIPEMD160(SHA256) → Bech32.Encode("gm", 0, hash20) → gm1q...`

This method is used in `ValidateTransactionSignature()` at runtime (checking that the transaction's secp256k1 pubkey hashes to the claimed sender address) and can also be used to verify any address independently.

---

---

## Chapter 9 — Phase 0.5: Wallet Address Persistence

**Files changed**: `NodeAgent.cs`, `NetworkRoot.cs`  
**Status**: Implemented (Phase 0.5)

### The Problem

After Phase 0.4 introduced `gm1q...` addresses, a session-persistence bug became clearly visible: every game launch produced different wallet addresses for the player and all bots. The blockchain data (coinbase recipients, transaction senders and recipients) recorded addresses from the session that mined those blocks, but the live game showed freshly-generated addresses. The blockchain and the live wallet were perpetually out of sync.

The bug had two visible symptoms:

1. **Across sessions**: restarting the game lost all address continuity. A player who mined a block in session 1 would see zero balance in session 2 — the rewards had gone to an address that no longer matched any live node.

2. **Within a session**: if the previous session ended mid-block-cycle (with a pending coinbase for the next block), reloading that pending transaction would include a coinbase addressed to the *previous* session's player address. When the player mined the next block, the block's coinbase pointed to the old address while the UI showed the new (current session's) address. This made it appear as if the player's address changed without any navigation or restart.

### Root Cause

`NodeAgent` always derived wallet credentials in its constructor:

```csharp
// Old — called on every construction with fresh random bytes
(WalletAddress, WalletPublicKey, WalletPrivateKey, WalletSecp256k1PublicKey) = CryptoUtils.GenerateWallet();
```

`BlockchainStateSnapshot` saved the blockchain chain, pending transactions, and financial states — but never the wallet addresses or signing keys. Nothing survived game restart.

### The Fix

**`NodeAgent.cs`** — A second constructor was added that accepts all four wallet fields directly:

```csharp
public NodeAgent(string nodeId, string address, string signingPublicKey,
                  string signingPrivateKey, string secp256k1PublicKey)
{
    NodeId = nodeId;
    WalletAddress = address;
    WalletPublicKey = signingPublicKey;
    WalletPrivateKey = signingPrivateKey;
    WalletSecp256k1PublicKey = secp256k1PublicKey;
}
```

The original constructor (random generation) is untouched — it is still the code path for first-launch wallet creation.

**`NetworkRoot.cs`** — The initialization sequence was restructured so wallet data is loaded *before* nodes are constructed:

```
Old order:
  create nodes (random wallets) → load chain state from disk → done

New order:
  read snapshot from disk → create nodes (use saved wallets if present) → apply chain state → done
```

`BlockchainStateSnapshot` now includes a `NodeWallets` dictionary. On every save, each node's four wallet fields are written to this dictionary keyed by node ID. A `NodeWalletSnapshot.IsComplete()` guard ensures a partially-written record (e.g., from an old save file) is treated as absent rather than partially applied.

```csharp
// Saved per node in every PersistStateToDisk() call
NodeWallets = SharedNodesById.ToDictionary(
    pair => pair.Key,
    pair => new NodeWalletSnapshot
    {
        Address                  = pair.Value.WalletAddress,
        SigningPublicKeyBase64    = pair.Value.WalletPublicKey,
        SigningPrivateKeyBase64   = pair.Value.WalletPrivateKey,
        Secp256k1PublicKeyBase64 = pair.Value.WalletSecp256k1PublicKey
    })
```

On startup, `CreateAndRegisterNode()` checks the saved snapshot:

```csharp
if (savedState?.NodeWallets?.TryGetValue(nodeId, out wallet) == true && wallet.IsComplete())
    node = new NodeAgent(nodeId, wallet.Address, wallet.SigningPublicKeyBase64,
                         wallet.SigningPrivateKeyBase64, wallet.Secp256k1PublicKeyBase64);
else
    node = new NodeAgent(nodeId);  // first launch: generate fresh
```

### Invariant After This Fix

Once a `user://blockchain/state.json` exists:
- The player's `gm1q...` address is the same in every session.
- All bot addresses are the same in every session.
- Coinbase recipients in the blockchain always match the live node addresses.
- Pending coinbase transactions from the previous session resolve to the same address as the current session's player.

The first launch (no saved state) generates fresh random wallets, writes them, and all subsequent launches restore those exact credentials.

---

---

## Chapter 10 — Phase 1.1: Wordlist File Rename

**File changed**: `Scripts/BlockchainPort/BIP-0039/bip39_2048.txt` (renamed from `2048WordsList`)  
**Status**: Implemented (Phase 1.1)

### What Is This File?

`bip39_2048.txt` is the standard BIP39 English wordlist — exactly 2048 common English words, one per line. BIP39 (Bitcoin Improvement Proposal 39) defines the vocabulary used for human-readable wallet seed phrases across the Bitcoin ecosystem.

GamblingMiner uses this list as the source from which a 256-word in-game subset is randomly selected on first launch (Phase 1.2). That subset is what the player's seed phrase, the casino's seed phrase, and all bot wallets draw from.

### Why `.txt`?

The original file had no extension (`2048WordsList`). This caused two problems:

1. **Tools ignore it**: Godot's asset pipeline, export tooling, and external editors do not track extensionless files as text resources. They cannot include the file in an exported PCK automatically.
2. **Export is undefined**: `FileAccess.Open("res://...")` on an extensionless file works in the editor (which reads directly from the project directory) but is undefined in exported builds where the PCK builder may silently skip the file.

Renaming to `.txt` fixes both: the file is unambiguously a text resource, and Godot's export system can be told to include `*.txt` files via the export preset's include filter.

### Export Filter Requirement

Export presets live in `export_presets.cfg` (per-platform). When a preset is configured, add `*.txt` to its `include_filter` so the file lands in the PCK:

```ini
# export_presets.cfg — relevant field per platform preset
include_filter="*.txt"
```

In editor/development mode this is not required — `res://` maps directly to the project directory on disk.

### Runtime Access

`WordlistBootstrapper.EnsureWordlist()` (Phase 1.2) opens the file as:

```csharp
using var file = FileAccess.Open(
    "res://Scripts/BlockchainPort/BIP-0039/bip39_2048.txt",
    FileAccess.ModeFlags.Read);
```

It reads all 2048 lines, Fisher-Yates shuffles them, takes the first 256, sorts them alphabetically, and saves the result to `user://wordlist_256.json`. After that first run the source file is never opened again — `user://wordlist_256.json` is the live wordlist for all subsequent sessions.

---

---

## Chapter 11 — Phase 1.2: WordlistBootstrapper

**File**: `Scripts/Services/WordlistBootstrapper.cs`  
**Status**: Implemented (Phase 1.2)

### What It Does

`WordlistBootstrapper` is a static class that produces the 256-word in-game vocabulary every participant's wallet is drawn from. It runs once at startup and is idempotent — calling it a second time returns the already-saved list without regenerating.

### The Two Code Paths

**First launch** (no `user://wordlist_256.json` yet):

```
res://Scripts/BlockchainPort/BIP-0039/bip39_2048.txt
    │
    ▼  Read all 2048 lines
    │
    ▼  Fisher-Yates in-place shuffle (cryptographically random seed via new Random())
    │
    ▼  Take first 256 of the shuffled list
    │
    ▼  Sort alphabetically (StringComparer.Ordinal)
    │
    ▼  Assign indices 1..256
    │
    ▼  Serialize to user://wordlist_256.json (CamelCase JSON)
    │
    ▼  Return List<WordEntry>
```

**Subsequent launches**:

```
user://wordlist_256.json
    │
    ▼  Read + deserialize (via private WordEntryDto → public WordEntry)
    │
    ▼  Return List<WordEntry>
```

Every game installation gets a permanently different 256-word set. There is no reset mechanic — once generated, the wordlist is fixed for the life of that save. This means the "world" of each installation is subtly unique.

### Word Selection for Seed Phrases

`GenerateThreeWords(wordlist, rng)` draws three words independently at random. The only rejection rule is all-three-identical: if A == B == C it redraws. This allows two-of-three repeats (e.g., "oak oak river"), which are valid seed phrases. The probability of needing a redraw is ~1 in 256² — negligible.

### JSON Format

`user://wordlist_256.json` follows the project's CamelCase naming policy:

```json
{
  "generatedAt": "2026-06-12T10:23:45.123Z",
  "words": [
    { "index": 1, "word": "abandon" },
    { "index": 2, "word": "ability" },
    ...
    { "index": 256, "word": "zone" }
  ]
}
```

`generatedAt` records the real-world UTC timestamp of generation (not game time). It is metadata only — the game does not use it.

### Serialization Architecture

The internal `WordlistSnapshot` and `WordEntryDto` classes handle JSON. The public `WordEntry` record is kept separate from the JSON DTO so the public API stays clean and independent of JSON formatting concerns. Conversion is done at the boundary in `Load()`.

### Startup Output (How to Verify)

Open the Godot Output panel after running. You will see one of two messages:

**First launch**:
```
[WordlistBootstrapper] Generated 256-word subset from BIP39 2048-word list — saved to user://wordlist_256.json
[WordlistBootstrapper] First 3: <word>, <word>, <word>
```

**Subsequent launches**:
```
[WordlistBootstrapper] Loaded 256 words from user://wordlist_256.json — first 3: <word>, <word>, <word>
```

The word count (256) and the three sample words confirm the list is valid. If the source file is missing, `FileAccess.Open()` throws — the game will fail to start, which is the correct fail-fast behaviour.

---

## Chapter 12 — Phase 1.3: Wiring WordlistBootstrapper into Startup

**File changed**: `Scripts/Services/CalendarTimeService.cs`  
**Status**: Implemented (Phase 1.3)

### The Change

`WordlistBootstrapper.EnsureWordlist()` is now the first call in `CalendarTimeService._Ready()`:

```csharp
public override void _Ready()
{
    WordlistBootstrapper.EnsureWordlist();  // Phase 1.3
    EnsureGameEpochInitialized();
}
```

`CalendarTimeService` is the earliest autoload that does meaningful work. It was chosen as the host because wallet initialization (Phase 3) must also happen before any game logic runs, and both depend on the wordlist. Keeping the startup sequence in one place avoids ordering confusion across multiple autoloads.

### Planned Insertion Point for Phase 3

When `WalletInitializationService` is implemented, the call sequence becomes:

```csharp
public override void _Ready()
{
    WordlistBootstrapper.EnsureWordlist();           // Phase 1.3 — already done
    WalletInitializationService.EnsureAll();         // Phase 3 — pending
    EnsureGameEpochInitialized();
}
```

`EnsureWordlist()` is idempotent, so `WalletInitializationService.EnsureAll()` can also call it internally if it needs the wordlist — it will load from disk on the second call rather than regenerating.

---

## Chapter 13 — Phase 2: Wallet Persistence Models

### The Short Version (for everyone)

Every wallet that matters in the game has a small data record describing it: the player's wallet, the casino's wallet, and the bots' wallets. Phase 2 defines what those records look like and where they will be stored. Nothing is saved to disk yet — that happens in Phase 3 — but the data shapes are established here so all future code agrees on the structure.

---

### 13.1 — Why a Separate Models File

The three wallet types are used by different systems: `WalletInitializationService` (Phase 3) creates and loads them, `BTCWallet` (Phase 4) displays the player's, the `BotWalletRegistry` (Phase 5.4) manages bot entries, and the casino dev scene (Phase 7) reads the casino's. Defining them in one file (`WalletModels.cs`) gives every system a single import source and avoids duplication.

---

### 13.2 — The Three Records

**File**: `Scripts/BlockchainPort/Blockchain/WalletModels.cs`  
**Namespace**: `GodotBlockchainPort.Blockchain`

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

public record BotWalletRecord(
    string NodeId,
    string Address,            // gm1q... only; no seed words stored
    string? SigningPrivateKeyBase64 = null
);
```

#### `PlayerWalletState`

- `SeedWords`: always exactly 3 words from the 256-word game subset. Passphrase wallets (4-word derivations) are ephemeral — they exist only while the user has typed the passphrase into the UI and are never written to disk.
- `BaseAddress`: the `gm1q...` address derived from the 3 seed words at wallet creation. Stored so the app can display the address instantly without re-running the full derivation pipeline on every launch.
- `HasSeenSeedPopup`: starts `false`. Set to `true` when the player confirms they have saved their seed words. The first-launch popup in `BTCWallet` checks this flag.

#### `CasinoWalletState`

Identical structure to `PlayerWalletState` without the popup flag. The casino wallet is created at game start alongside the player wallet (Phase 3). Its seed words are accessible from the dev-only CasinoFinances scene (Phase 7), not from the player-facing BTCWallet.

#### `BotWalletRecord`

- `Address`: the only persistent credential for bots. Bot private keys are not stored in `BotWalletRecord` — instead, the signing key is provisioned at creation and stored separately in the `BotWalletRegistry` (Phase 5.4).
- `SigningPrivateKeyBase64`: per OQ-13 (Option A resolved), all bots — miner and non-miner alike — receive a P-256 signing key at creation time. This field is nullable only for forward-compatibility; in practice it is always populated when a bot is registered.

---

### 13.3 — Persistence Locations (Phase 3 responsibility)

These records are defined here but not persisted yet. Phase 3 (`WalletInitializationService`) will read and write:

| Record | File |
|---|---|
| `PlayerWalletState` | `user://wallet_state.json` |
| `CasinoWalletState` | `user://casino_wallet_state.json` |
| `BotWalletRecord[]` | `user://bot_wallet_registry.json` (Phase 5.4) |

All files follow the project's CamelCase JSON naming policy.

---

---

## Chapter 14 — Phase 3: Game Startup Wallet Initialization

### The Short Version (for everyone)

When the game launches for the first time, it automatically creates a Bitcoin-style wallet for the player and a separate one for the casino. Each wallet is three randomly chosen words that, together, determine a unique address where BTC can be received. After the first launch, the wallets are loaded from disk — no new words are generated. The player's seed words are shown once in a popup when they first visit the BTCWallet screen (Phase 4); after that, they are no longer shown automatically.

---

### 14.1 — Startup Sequence

`WalletInitializationService.EnsureAll()` is called from `CalendarTimeService._Ready()`, after `WordlistBootstrapper.EnsureWordlist()` and before `EnsureGameEpochInitialized()`:

```csharp
public override void _Ready()
{
    WordlistBootstrapper.EnsureWordlist();       // Phase 1.3 — loads or generates 256-word subset
    WalletInitializationService.EnsureAll();     // Phase 3 — creates or loads player + casino wallets
    EnsureGameEpochInitialized();
}
```

`EnsureAll()` calls `WordlistBootstrapper.EnsureWordlist()` internally as well. Since the wordlist is already on disk after Phase 1.3 runs, the second call is a fast disk read — no regeneration happens.

---

### 14.2 — Two Code Paths per Wallet

Each wallet (`EnsurePlayerWallet`, `EnsureCasinoWallet`) follows the same pattern:

**First launch** — `user://wallet_state.json` (or `casino_wallet_state.json`) does not exist:
1. Call `WordlistBootstrapper.GenerateThreeWords()` to pick 3 words from the 256-word subset.
2. Call `CryptoUtils.DeriveGmAddress(string.Join(" ", words))` to produce the `gm1q...` address.
3. Create the record (`PlayerWalletState` / `CasinoWalletState`) and save to disk.
4. Print address (and seed words for the player wallet) to Godot Output for verification.

**Subsequent launches** — file exists:
1. Load from disk via internal DTO, convert to the public record type.
2. Print address to Output.

---

### 14.3 — Public API

```csharp
public static class WalletInitializationService
{
    public static PlayerWalletState? PlayerWallet { get; }   // set after EnsureAll()
    public static CasinoWalletState? CasinoWallet { get; }  // set after EnsureAll()

    public static void EnsureAll();          // called once at startup from CalendarTimeService._Ready()
    public static void MarkSeedPopupSeen();  // called from BTCWallet after player confirms seed words
}
```

`PlayerWallet` and `CasinoWallet` are `null` only before `EnsureAll()` has run. All game screens that need wallet data access them via these static properties after startup completes.

`MarkSeedPopupSeen()` updates `PlayerWalletState.HasSeenSeedPopup` to `true` and re-saves `user://wallet_state.json`. Called by `BTCWallet` (Phase 4) when the player taps "I have saved my words."

---

### 14.4 — JSON Format

`user://wallet_state.json` (CamelCase per project policy):
```json
{
  "seedWords": ["oak", "river", "flash"],
  "baseAddress": "gm1q...",
  "hasSeenSeedPopup": false
}
```

`user://casino_wallet_state.json`:
```json
{
  "seedWords": ["amber", "north", "climb"],
  "baseAddress": "gm1q..."
}
```

Serialization uses internal DTO classes (`PlayerWalletDto`, `CasinoWalletDto`) — same pattern as `WordlistBootstrapper` — so the public `PlayerWalletState` / `CasinoWalletState` records stay clean and decoupled from JSON concerns.

---

### 14.5 — Startup Output (Godot Output Panel)

First launch:
```
[WalletInitializationService] Player wallet created — gm1q...
[WalletInitializationService] Player seed words: oak river flash
[WalletInitializationService] Casino wallet created — gm1q...
```

Subsequent launches:
```
[WalletInitializationService] Player wallet loaded — gm1q...
[WalletInitializationService] Casino wallet loaded — gm1q...
```

---

---

## Chapter 15 — Phase 4: BTCWallet Scene

### The Short Version (for everyone)

The Bitcoin Wallet screen is the player's window into their on-chain BTC holdings. From here they can see their deposit address (share it to receive mining rewards), check their confirmed balance, and — once the passphrase feature is unlocked — access a second hidden wallet derived from a fourth word. On first visit, a popup shows the three seed words that control the base wallet. These words appear only once automatically; after that, the player is responsible for keeping them safe.

---

### 15.1 — Navigation and Entry Point

`MainMenu` → `Bitcoin Wallet` button → `BTCWallet` scene (`res://Screens/BTCWallet/BTCWallet.tscn`).

`SceneManager.SceneId.BTCWallet` added to the enum and `Paths` dictionary. `MainMenu.tscn` has a `BTCWalletBtn` button wired in `MainMenu.cs`.

---

### 15.2 — Three-Mode Panel Architecture

The scene contains three `VBoxContainer` panels that never overlap — only one is visible at a time, controlled by `SetMode(WalletMode)`:

| Mode | Panel visible | Description |
|---|---|---|
| `Base` | `BaseWalletPanel` | Default. Shows base wallet address + balance. |
| `PassphraseLocked` | `PassphraseLockedPanel` | User entering their passphrase word. |
| `PassphraseUnlocked` | `PassphraseUnlockedPanel` | Passphrase wallet open with its own address + balance. |

---

### 15.3 — Balance Display

Balance is queried via `NetworkRoot.GetAddressBalanceDetails(address)`, which scans the player node's `BlockchainService`:

```csharp
public (decimal confirmedBalance, decimal pendingOutgoing) GetAddressBalanceDetails(string address)
{
    AddressData data = node.Blockchain.GetAddressData(address);
    decimal pendingOut = node.Blockchain.PendingTransactions
        .Where(t => t.Sender == address).Sum(t => t.Amount);
    return (data.AddressBalance, pendingOut);
}
```

`BTCWallet._Process()` refreshes balances every 2 seconds. "Pending outgoing" label is hidden when there are no pending sends (`pendingOutgoing == 0`).

---

### 15.4 — Seed Popup

`SeedPopup` is a `Panel` node that is the last child of the root `Control`, so it renders on top of everything. It is initially `visible = false` in the .tscn. On `_Ready()`, if `PlayerWalletState.HasSeenSeedPopup == false`, the popup is shown with the three seed words at 44pt font.

The player has two options:
- **[Copy to clipboard]** — copies `"word1 word2 word3"` to the system clipboard via `DisplayServer.ClipboardSet()`.
- **[I have saved my words]** — calls `WalletInitializationService.MarkSeedPopupSeen()` (sets `HasSeenSeedPopup = true`, re-saves `user://wallet_state.json`), then hides the popup.

After this popup is dismissed it never appears automatically again. The seed words remain derivable from a future "show seed words" button (not yet implemented).

---

### 15.5 — Passphrase Wallet Mechanics

Entering the passphrase locked panel clears the `LineEdit`. Clicking **Unlock** (or pressing Enter):

1. Takes `passphrase = PassphraseInput.Text.Trim()`
2. Derives `seedPhrase = "word1 word2 word3 passphrase"`
3. Calls `CryptoUtils.DeriveGmAddress(seedPhrase)` — deterministic, no storage
4. Clears the `LineEdit` immediately
5. Shows the unlocked panel with the derived address

The passphrase address is cleared from `_currentPassphraseAddress` when the player navigates back. No passphrase-derived key material is retained in memory after leaving the unlocked panel.

---

### 15.6 — Send BTC Placeholder

Both `SendBtcBtn` (base wallet) and `SendBtcPassphraseBtn` (passphrase wallet) are `disabled = true` in the .tscn with a tooltip "Send BTC (not yet available)". The full send flow is planned for Phase 6 (DevTransferTool) and will be surfaced directly in BTCWallet in a later update.

---

### 15.7 — Connecting Mining Rewards to the BTCWallet Address

After Phase 4 was tested, it became clear that the player's `NodeAgent` (the mining node that collects coinbase rewards) was still using a randomly-generated address from Phase 0.5, while BTCWallet showed the seed-phrase address. These were two different addresses, so the wallet always showed 0 BTC regardless of how many blocks were mined.

**Fix**: `NetworkRoot.CreateAndRegisterNode` was updated to give the player node its credentials from `WalletInitializationService.PlayerWallet` instead of the saved random wallet or a fresh random generation.

Two components are derived from the seed phrase on every launch:

1. **Signing keypair** (P-256, game-internal): `CryptoUtils.DeriveSigningKeypair("word1 word2 word3")` → deterministic `signingPublicKeyBase64` + `signingPrivateKeyBase64`

2. **secp256k1 compressed public key**: `CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64("word1 word2 word3")` — a new helper that shares the identical derivation path with `DeriveGmAddress` (`SHA256(phrase)` → `Secp256k1.GetCompressedPublicKey()`) but returns the raw 33-byte compressed pubkey as base64 instead of the Bech32-encoded address. This is stored in `NodeAgent.WalletSecp256k1PublicKey` and attached to every coinbase transaction as `tx.Secp256k1PublicKeyBase64`, which `BlockchainService.ValidateTransactionSignature()` uses to verify sender ownership.

After this fix, the player node's `WalletAddress` matches `PlayerWalletState.BaseAddress` exactly. Every mined block's coinbase reward is credited to the address shown in BTCWallet.

**Migration note**: blocks mined before this change remain in the blockchain with coinbase outputs addressed to the old random address. Those outputs are not retroactively reassigned. The balance in BTCWallet starts accumulating from the first block mined after the fix. Clearing `user://blockchain/` starts with a clean slate.

**Tested**: clean blockchain run confirmed — BTCWallet address, BlockExplorer player address, and coinbase recipient all show the same `gm1q...` address.

---

---

---

## Chapter 16 — Phase 5: Bot Wallet Registry

**Files changed**: `WalletModels.cs`, `BotWalletRegistry.cs` (new), `WalletInitializationService.cs`, `NetworkRoot.cs`  
**Status**: Implemented (Phases 5.1, 5.2, 5.4)

### The Short Version (for everyone)

Before Phase 5, each bot node had a randomly-generated wallet that changed every session. Phase 5 introduces a persistent registry that assigns permanent, stable addresses to all 14 bot participants: four miner bots (who can sign and send transactions) and ten non-miner bots (holder wallets — address only, no signing keys). The registry is created once and loaded on every subsequent launch, so bot addresses never change.

---

### 16.1 — Phase 5.1: Bot Addresses Already Use gm1q Format

Phase 5.1 was a verification step, not a code change. The plan required confirming that bots receive `gm1q...` addresses — not the old 40-character hex format. This was already true since Phase 0.4/0.5: `CryptoUtils.GenerateWallet()` always calls `DeriveGmAddress()` → `Bech32.Encode("gm", ...)`. No code change was needed; only the plan status was updated.

---

### 16.2 — Extended `BotWalletRecord`

**File**: `Scripts/BlockchainPort/Blockchain/WalletModels.cs`

The `BotWalletRecord` introduced in Phase 2 was extended with three key fields, two lifecycle fields, a node-type flag, and a computed property:

```csharp
public record BotWalletRecord(
    string NodeId,
    string Address,                          // gm1q..., always present
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
```

**`HasFullWallet`**: computed property — `true` when all three key fields are populated. All bots (miners and non-miners alike) are created with full wallets, so `HasFullWallet` is always `true` for records loaded from a current registry file. It is `false` only for non-miners loaded from an old registry file (written before this change) that did not store non-miner keys.

**`IsMinerNode`**: `true` for the four miner bots (`bot_1`–`bot_4`), `false` for non-miner holder wallets. Used to decide which detail panel sections to show (Mining Stats vs. Wallet Status) and whether the Send section appears unconditionally (miners) or conditionally on balance (non-miners).

**`IsActive` / `ReactivationBlockHeight`**: lifecycle fields for the planned "sleeping whale" simulation (Phase 5.3). A non-miner bot can be deactivated, optionally tagged with a block height at which it should reactivate. Both fields are persisted in the registry JSON and are mutable via `BotWalletRegistry.SetBotStatus()`.

---

### 16.3 — `BotWalletRegistry` Static Class

**File**: `Scripts/BlockchainPort/Simulation/BotWalletRegistry.cs`  
**Namespace**: `GodotBlockchainPort.Simulation`  
**Persistence**: `user://bot_wallet_registry.json`

`BotWalletRegistry` is a static class (not a Godot Node) that owns the authoritative list of all bot wallet records.

#### Public API

```csharp
public static IReadOnlyList<BotWalletRecord> MinerBots { get; }    // bot_1 … bot_4
public static IReadOnlyList<BotWalletRecord> NonMinerBots { get; } // non_miner_1 … non_miner_10
public static IReadOnlyList<BotWalletRecord> AllBots { get; }      // MinerBots ++ NonMinerBots

public static void EnsureAll();
public static BotWalletRecord? GetBot(string nodeId);
public static void SetBotStatus(string nodeId, bool isActive, int? reactivationBlockHeight);
```

#### `EnsureAll()` — Create or Load

If `user://bot_wallet_registry.json` does not exist, `CreateRegistry()` runs:

- **4 miner bots** (`bot_1`–`bot_4`): each calls `CryptoUtils.GenerateWallet()` (32 random bytes → full 4-tuple). All four fields are stored, and `IsMinerNode: true` is set.
- **10 non-miner bots** (`non_miner_1`–`non_miner_10`): each also calls `CryptoUtils.GenerateWallet()`. All four fields are stored (including signing keys), and `IsMinerNode: false` is set. Non-miners have full wallets so they can sign and send transactions once they have a balance.

After creation, the registry is saved to disk and each address is printed to Godot Output.

On subsequent launches, `LoadRegistry()` deserializes `user://bot_wallet_registry.json`. All key fields are restored from JSON for both miners and non-miners. `IsMinerNode` is set at load time based on which JSON array the entry came from (`miners` → `true`, `nonMiners` → `false`).

#### `SetBotStatus()` — Mutating Lifecycle Fields

```csharp
public static void SetBotStatus(string nodeId, bool isActive, int? reactivationBlockHeight)
{
    var list = NonMinerBots.ToList();
    int idx = list.FindIndex(b => b.NodeId == nodeId);
    if (idx < 0) return;
    list[idx] = list[idx] with { IsActive = isActive, ReactivationBlockHeight = reactivationBlockHeight };
    NonMinerBots = list;
    SaveRegistry();
}
```

Because `BotWalletRecord` is an immutable record, mutations use `with {}` syntax to produce a new record. `SaveRegistry()` is called immediately — changes are durable after this method returns. Only non-miner bots can be toggled; miner bots are always active.

#### JSON Format

`user://bot_wallet_registry.json` (CamelCase, `WhenWritingNull` omits key fields for non-miners):

```json
{
  "miners": [
    {
      "nodeId": "bot_1",
      "address": "gm1q...",
      "signingPublicKeyBase64": "...",
      "signingPrivateKeyBase64": "...",
      "secp256k1PublicKeyBase64": "...",
      "isActive": true
    }
  ],
  "nonMiners": [
    {
      "nodeId": "non_miner_1",
      "address": "gm1q...",
      "signingPublicKeyBase64": "...",
      "signingPrivateKeyBase64": "...",
      "secp256k1PublicKeyBase64": "...",
      "isActive": true
    },
    {
      "nodeId": "non_miner_3",
      "address": "gm1q...",
      "signingPublicKeyBase64": "...",
      "signingPrivateKeyBase64": "...",
      "secp256k1PublicKeyBase64": "...",
      "isActive": false,
      "reactivationBlockHeight": 500
    }
  ]
}
```

`DefaultIgnoreCondition = WhenWritingNull` omits `reactivationBlockHeight` when null, keeping the file compact. Key fields are present for all bots.

---

### 16.4 — `WalletInitializationService.EnsureAll()` Updated

**File**: `Scripts/Services/WalletInitializationService.cs`

`BotWalletRegistry.EnsureAll()` is now called at the end of `WalletInitializationService.EnsureAll()`, after the player and casino wallets are ready:

```csharp
public static void EnsureAll()
{
    List<WordlistBootstrapper.WordEntry> wordlist = WordlistBootstrapper.EnsureWordlist();
    PlayerWallet = EnsurePlayerWallet(wordlist);
    CasinoWallet = EnsureCasinoWallet(wordlist);
    BotWalletRegistry.EnsureAll();   // Phase 5.2 addition
}
```

The full startup sequence in `CalendarTimeService._Ready()` is therefore:

```
WordlistBootstrapper.EnsureWordlist()          // Phase 1.3
    → WalletInitializationService.EnsureAll()  // Phase 3
        → EnsurePlayerWallet()
        → EnsureCasinoWallet()
        → BotWalletRegistry.EnsureAll()        // Phase 5.2
    → EnsureGameEpochInitialized()
```

`NetworkRoot._Ready()` runs after all autoloads complete, so `BotWalletRegistry.MinerBots` is fully populated before any node is constructed.

---

### 16.5 — `NetworkRoot` Bot Branch: Registry as Primary Source

**File**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`

`CreateAndRegisterNode()` now uses `BotWalletRegistry` as the authoritative source for bot wallet credentials, falling back to the blockchain snapshot only as a migration path:

```csharp
// Bot branch (nodeId != "player")
BotWalletRecord? botRecord = BotWalletRegistry.GetBot(nodeId);
if (botRecord?.HasFullWallet == true)
    node = new(nodeId, botRecord.Address, botRecord.SigningPublicKeyBase64!,
               botRecord.SigningPrivateKeyBase64!, botRecord.Secp256k1PublicKeyBase64!);
else if (savedState?.NodeWallets?.TryGetValue(nodeId, out NodeWalletSnapshot? wallet) == true
         && wallet?.IsComplete() == true)
    node = new(nodeId, wallet.Address, wallet.SigningPublicKeyBase64,
               wallet.SigningPrivateKeyBase64, wallet.Secp256k1PublicKeyBase64);
else
    node = new(nodeId);  // fresh random wallet (unexpected fallback)
```

**Priority**: `BotWalletRegistry` → blockchain snapshot → fresh random.

All bots in a current registry file have `HasFullWallet == true` and take the first branch. Non-miner bots are also registered as NodeAgents in `EnsureInitialized()` (see below), so they appear in `SharedNodesById` and can broadcast signed transactions.

`EnsureInitialized()` registers non-miners conditionally:

```csharp
foreach (BotWalletRecord nonMiner in BotWalletRegistry.NonMinerBots)
{
    if (nonMiner.HasFullWallet)
        SharedNetwork.RegisterNode(CreateAndRegisterNode(nonMiner.NodeId, savedState));
}
```

The `HasFullWallet` guard is a migration safety net: old registry files written before non-miner keys were stored will load with `HasFullWallet == false` for non-miners, and those bots simply skip registration. After deleting `user://bot_wallet_registry.json` and restarting, a fresh registry is created with full keys and non-miners are registered normally.

---

### 16.6 — Migration Note

If `user://blockchain/state.json` contains blocks with coinbase outputs addressed to old random bot addresses (generated before the registry existed), those blocks remain unchanged. After the registry is created, bot nodes use the new registry addresses going forward. To start with a clean slate, clear `user://blockchain/`. This is the accepted migration pattern for this prototype.

---

---

## Chapter 17 — Phase 6: BotsBtcWallets Dev Scene + BlockExplorer Cleanup

**Files changed**: `BlockExplorer.cs`, `BlockExplorer.tscn`, `NetworkRoot.cs`, `SceneManager.cs`, `MainMenu.cs`, `MainMenu.tscn`  
**Files added**: `Screens/BotsBtcWallets/BotsBtcWallets.cs`, `Screens/BotsBtcWallets/BotsBtcWallets.tscn`  
**Status**: Implemented (Phase 6)

### The Short Version (for everyone)

Phase 6 adds a developer-facing scene — **Bot Wallets [DEV]** — where all 14 bot participants can be inspected: their addresses, BTC balances, confirmed transactions, and (for miner bots) mining history and outbound send capability. At the same time, the BlockExplorer is simplified to a read-only inspector by removing the transaction-creation controls that were never used in normal play.

---

### 17.1 — BlockExplorer: Transfer Controls Removed

**Files**: `Screens/BlockExplorer/BlockExplorer.cs`, `Screens/BlockExplorer/BlockExplorer.tscn`

The BlockExplorer previously contained a transfer section (sender dropdown, recipient dropdown, amount input, "Create Transaction" button). This was a convenience tool that belonged in a dedicated dev scene, not in the player-facing blockchain inspector.

**Removed from `BlockExplorer.cs`**:
- Fields: `_fromNodeOption`, `_toNodeOption`, `_amountInput`, `_createTxButton`
- Methods: `OnCreateTransactionPressed()`, `RefreshTransferState()`, `TryGetTransferContext()`
- `using System.Globalization` (no longer needed)

**Removed from `BlockExplorer.tscn`**:
- `TxTitle` Label node
- `TxControls` HBoxContainer with its four children (`FromNodeOption`, `ToNodeOption`, `AmountInput`, `CreateTxButton`)

**What remains**: The `_minerNodeOption` dropdown (formerly `_fromNodeOption`) now serves only the mining action and lookup queries. `_actionFeedbackLabel` is kept for mine / consensus / refresh feedback. The BlockExplorer is fully read-only from the player's perspective.

---

### 17.2 — Two New `NetworkRoot` Helpers

**File**: `Scripts/BlockchainPort/Simulation/NetworkRoot.cs`

Two methods were added to support BotsBtcWallets without duplicating blockchain traversal logic.

#### `GetAddressConfirmedTransactions(string address)`

```csharp
public IReadOnlyList<(Transaction tx, int blockIndex)> GetAddressConfirmedTransactions(string address)
```

Scans the full player node's confirmed chain, collects every transaction where `tx.Sender == address || tx.Recipient == address`, and returns the list sorted by `blockIndex` descending (most recent first).

Used in BotsBtcWallets to build the transaction history list and to compute mining stats (filtered by `tx.Sender == BlockchainService.CoinbaseSender`).

#### `CreateAndBroadcastTransactionToAddress(string fromNodeId, string recipientAddress, decimal amount)`

```csharp
public Transaction? CreateAndBroadcastTransactionToAddress(
    string fromNodeId, string recipientAddress, decimal amount)
```

The existing `CreateAndBroadcastTransaction(fromNodeId, recipientNodeId, ...)` requires both sender and recipient to be registered `NodeAgent` instances. Non-miner bots and passphrase wallets are never registered as nodes. This overload takes the sender by `nodeId` (must be registered) and the recipient by raw `gm1q...` address, allowing sends to any participant regardless of whether they have a `NodeAgent`.

Self-send (sender address == recipient address) returns `null`. Calls `sender.CreateSignedTransaction(amount, recipientAddress)` directly, then broadcasts and persists.

---

### 17.3 — BotsBtcWallets Scene Architecture

**Scene**: `Screens/BotsBtcWallets/BotsBtcWallets.tscn`  
**Controller**: `Screens/BotsBtcWallets/BotsBtcWallets.cs`

The scene has two structural elements at the root `Control`:
- `NetworkRoot` child node (script attached) — initializes the blockchain network
- `RootMargin` (40/30 px margins) → `RootVBox` — layout container for all UI

Layout:

```
TopBar (HBoxContainer)
  BackBtn             (→ MainMenu)
  StatusBarPlaceholder (StatusBar inserted here in _Ready)

ContentSplit (HSplitContainer, split_offset=320)
  BotListScrollContainer (min_size=280, no horizontal scroll)
    BotListVBox
      MinersSectionLabel
      MinersList (unique)
      HoldersSectionHeader
        HoldersSectionLabel (ExpandFill)
        ShowInactiveCheck (unique)
      HoldersList (unique)

  BotDetailScrollContainer (ExpandFill, no horizontal scroll)
    BotDetailVBox (unique)
```

---

### 17.4 — Bot List Panel

`BuildBotList()` runs in `_Ready()` and creates all bot list rows dynamically from registry data.

**Miner bots**: a `Button` per bot added to `MinersList`. Each button shows `nodeId`, truncated address, and confirmed balance (`F8` BTC). Pressing a button calls `SelectBot(bot)`.

**Non-miner (holder) bots**: an `HBoxContainer` per bot containing a `Button` and a `Label` indicator (`●` active, `○` inactive). Inactive rows are grayed (`Modulate = (1,1,1,0.45)`) and hidden by default. The `ShowInactiveCheck` checkbox toggles their visibility via `RefreshHoldersVisibility()`.

Internal caches:
```csharp
private readonly List<(Button btn, BotWalletRecord bot)> _minerButtons;
private readonly List<(HBoxContainer row, Button btn, Label indicator, BotWalletRecord bot)> _holderRows;
```

Both caches are iterated in `RefreshBotListBalances()` (called every 3 seconds) to update balance display without rebuilding the node tree.

---

### 17.5 — Detail Panel

`BuildDetailPanel()` creates all detail nodes programmatically once in `_Ready()`. `RefreshDetailPanel(bot)` populates and shows/hides sections based on `bot.IsMinerNode` and runtime balance.

**Always visible** (any bot selected):
- Badge label: `"Miner Node · bot_1"` or `"Holder Wallet"`
- Address + Copy button (copies full address to clipboard)
- Confirmed balance label
- Pending outgoing label (hidden when zero)
- All Transactions (`RichTextLabel`, BBCode, `FitContent=true`, color-coded `+`/`-`)

**Visible for miner bots only** (`bot.IsMinerNode == true`):
- Mining Stats section: blocks mined count and total BTC mined (derived from `GetAddressConfirmedTransactions` filtered to `tx.Sender == CoinbaseSender`)

**Visible for non-miner bots only** (`bot.IsMinerNode == false`):
- Wallet Status section: active/inactive text; reactivation block and blocks-remaining labels (hidden when no reactivation height is set)
- Dev Controls section (see 17.7)

**Send BTC section** — visible when `bot.HasFullWallet` and either:
- `bot.IsMinerNode` (miners can always send), or
- `!bot.IsMinerNode && bot.IsActive && confirmedBalance > 0` (non-miners can send once they have received BTC and are not inactive)

This means the Send section appears and disappears dynamically for non-miners as their balance and active status change. The 3-second refresh loop (see 17.8) handles this automatically.

When no bot is selected, a `"Select a bot from the list."` placeholder label is shown and the detail VBox is hidden.

---

### 17.6 — Send BTC

All bots that have a full wallet can potentially send. The send form visibility is gated by the conditions described in 17.5. The form contains:
- A recipient `OptionButton` populated by `PopulateToDropdown()`: all 14 bots + Player + Casino — 16 entries total. A parallel `List<string> _toAddresses` stores the corresponding `gm1q...` addresses.
- An amount `LineEdit` (decimal, invariant culture)
- A `Send` button wired to `OnSendPressed()`

`OnSendPressed()` validates the amount, retrieves `recipientAddress = _toAddresses[_toDropdown.Selected]`, guards against self-send, then calls `_networkRoot.CreateAndBroadcastTransactionToAddress(_selectedBot.NodeId, recipientAddress, amount)`. Success shows a truncated tx ID; failure shows a feedback string.

**Non-miner send requirement**: All bots have full wallets (signing keys) generated at registry creation time. Non-miner bots are also registered as NodeAgents in `NetworkRoot.EnsureInitialized()` (conditional on `HasFullWallet`, so old registry files without non-miner keys skip registration gracefully). This makes them first-class senders — they add the transaction to their own pending pool and broadcast it via `SharedNetwork`. A miner must include it in the next block for it to confirm.

---

### 17.7 — Dev Controls (Non-Miner Bots Only)

**Toggle Active button**: calls `BotWalletRegistry.SetBotStatus(nodeId, !bot.IsActive, bot.ReactivationBlockHeight)`. After the call, `RefreshSelectedBotFromRegistry()` reloads the bot record from the registry (the local reference is stale because `BotWalletRecord` is immutable), updates the `_holderRows` cache via `UpdateHolderListRow()`, and re-renders the detail panel.

**Reactivation block input + Set button**: reads a positive integer from `_reactivationBlockInput`, calls `SetBotStatus(nodeId, bot.IsActive, blockHeight)`. An empty field passes `null`, clearing the reactivation trigger. The same refresh sequence runs after the call.

`UpdateHolderListRow(nodeId)` keeps the `_holderRows` tuple cache in sync: replaces the `BotWalletRecord` entry in the tuple, updates the indicator label text, and updates `Modulate` and `Visible` to match the new active state.

---

### 17.8 — 3-Second Refresh Loop

```csharp
private const double RefreshInterval = 3.0;

public override void _Process(double delta)
{
    _refreshTimer += delta;
    if (_refreshTimer < RefreshInterval) return;
    _refreshTimer = 0d;
    RefreshBotListBalances();
    if (_selectedBot != null) RefreshDetailPanel(_selectedBot);
}
```

Every 3 real seconds the balance column in all bot list rows is updated, and the full detail panel for the selected bot is re-rendered. This keeps balances and transaction lists current during a dev session without manual refresh.

---

### 17.9 — Navigation

**`SceneManager.cs`**: `BotsBtcWallets` added to the `SceneId` enum and `Paths` dictionary:

```csharp
[SceneId.BotsBtcWallets] = "res://Screens/BotsBtcWallets/BotsBtcWallets.tscn"
```

**`MainMenu.tscn`**: A `BotsBtcWalletsBtn` button (`text="Bot Wallets [DEV]"`, `font_size=34`, `min_size=(420,0)`) placed after the `BTCWalletBtn`.

**`MainMenu.cs`**:

```csharp
GetNode<Button>("%BotsBtcWalletsBtn").Pressed +=
    () => _sceneManager?.Go(SceneManager.SceneId.BotsBtcWallets);
```

Back navigation from BotsBtcWallets goes to `MainMenu` (not DiceGame).

The `[DEV]` label marks this as a developer tool. A player-facing equivalent would require gameplay rationale (e.g., unlock after the first block mined) and is deferred to a later phase.

---

### 17.10 — Transactions Display (> 1000 Blocks — Deferred)

`BuildTransactionsList()` renders all confirmed transactions inline in the `RichTextLabel`. For very long play sessions (> 1000 mined blocks), the list could become impractically long. An abbreviation strategy (e.g., show last 50, summarize older) is planned for that point but not yet implemented.

---

*This document covers Phases 0.1, 0.2, 0.3, 0.4, 0.5, 1.1, 1.2, 1.3, 2, 3, 4, 5, and 6 of the BTC Wallet Address System.*  
*See `AIHelperFiles/btc-wallet-system-plan.md` for the full implementation roadmap.*  
*Last updated: 2026-06-13*
