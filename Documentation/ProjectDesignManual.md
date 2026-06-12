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

*This document covers Phases 0.1, 0.2, 0.3, 0.4, 0.5, and 1.1 of the BTC Wallet Address System.*  
*See `AIHelperFiles/btc-wallet-system-plan.md` for the full implementation roadmap.*  
*Last updated: 2026-06-12*
