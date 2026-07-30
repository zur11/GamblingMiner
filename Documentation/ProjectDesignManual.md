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

> **Note — balance model vs. UTXO realism (design direction).** Today balances are computed **account/balance-based**: `GetAddressData` simply sums every confirmed transaction touching an address. This is a **testing-stage** simplification. The destination is to simulate a **UTXO-style** system as realistically as possible, made tangible through the passphrase-wallet system (many addresses from one seed). The key mechanic is **one fresh address per receive** — the historical "Patoshi pattern" (a new address per mined block) — which makes spends produce genuine change outputs and lets players learn UTXO mechanics by doing. Founder nodes (Satoshi first) adopt this pattern. See `AIHelperFiles/historical-founders-and-bootstrap-plan.md` and `historical-blockchain-events-research.md`.

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

### 15.4 — Seed Backup Popup

`SeedPopup` is a `Panel` node that is the last child of the root `Control`, so it renders on top of everything. It is initially `visible = false` in the .tscn. On `_Ready()`, if `PlayerWalletState.HasSeenSeedPopup == false`, the popup opens and `ShowSeedRevealPhase()` is called. The flow is mandatory and has two phases — neither can be skipped.

---

**Phase 1 — Reveal** (`SeedRevealPanel` is visible; `SeedVerifyPanel` is hidden):

The panel shows:
1. Title: "Your Seed Words"
2. Instruction label: *"Write these 3 words on paper, in this exact order. This is the only time they will appear automatically."*
3. Notepad warning label: *"⚠ Never store your seed words in the In-Game Notepad or any digital document — not even this app. If your paper is lost, your BTC cannot be recovered."*
4. The three words displayed in a numbered vertical list (`1.` / `2.` / `3.`) at 44pt font.
5. Button: **[I have written them down offline →]**

There is no copy-to-clipboard option. The design intent is to force a physical write-down. Pressing the button calls `ShowVerifyPhase()`.

---

**Phase 2 — Verify** (`SeedVerifyPanel` is visible; `SeedRevealPanel` is hidden):

`ShowVerifyPhase()` generates a randomized test order via Fisher-Yates shuffle of `[0, 1, 2]` and sets `_verifyStep = 0`. Each step is rendered by `ShowVerifyStep()`:

- Progress label: "Step X / 3"
- Prompt label: "Enter word #N:" (where N is the 1-based word number at this step in the shuffled order)
- `LineEdit` for the player to type the word
- **[Confirm]** button

The **Enter key also submits** — handled via a `_Input` override rather than `TextSubmitted`. When Enter is detected while `_seedVerifyPanel` is visible, `GetViewport().SetInputAsHandled()` consumes the event before Godot's `ui_accept` system can process it further (preventing focus from being stolen), `OnVerifySubmit()` runs, and `GrabFocus()` is called synchronously on the input while nothing else can interfere. `TextSubmitted` is intentionally not wired — `CallDeferred` and `_Process`-flag approaches were tried first but Godot's `ui_accept` handling can still run after them and take focus back.

**Initial focus**: `ShowVerifyStep()` also calls `_seedVerifyInput.GrabFocus()` directly so focus lands on the input as soon as the verify panel opens — the first step is triggered by a button click, which has no `ui_accept` conflict, so a synchronous call is sufficient.

`OnVerifySubmit()` compares the entered string (trimmed, `OrdinalIgnoreCase`) against the expected word:

- **Correct, step not finished**: `_verifyStep++` → `ShowVerifyStep()` for the next word.
- **Correct, all 3 done**: calls `WalletInitializationService.MarkSeedPopupSeen()` (sets `HasSeenSeedPopup = true`, re-saves `user://wallet_state.json`), then hides the popup.
- **Incorrect**: shows *"Incorrect — review your words carefully and try again."* and calls `ShowSeedRevealPhase()`, returning to Phase 1 so the player can re-read the full seed phrase before retrying.

On re-entry to Phase 2 (`ShowVerifyPhase()` is called again), a new shuffled order is generated — the player will not necessarily be asked the same word that failed. The loop continues until all 3 words are entered correctly in a single attempt. `MarkSeedPopupSeen()` is never called on a partial run.

After the popup is dismissed it never appears automatically again. The seed words remain derivable from a future "show seed words" button (not yet implemented).

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
- A recipient `OptionButton` populated by `PopulateToDropdown()`: all 14 bots + Player + Casino + `"── BTC Address ──"` — 17 entries total. A parallel `List<string> _toAddresses` stores the corresponding `gm1q...` addresses, with `string.Empty` as a sentinel value for the last entry.
- A `_manualAddressInput` `LineEdit` (hidden by default, placeholder `"Paste gm1q... address"`) inserted directly below the dropdown. The `_toDropdown.ItemSelected` lambda reveals it when the `"── BTC Address ──"` entry is selected: `idx => _manualAddressInput.Visible = (idx == _toAddresses.Count - 1)`.
- An amount `LineEdit` (decimal, invariant culture)
- A `Send` button wired to `OnSendPressed()`

`OnSendPressed()` validates the amount, then resolves the recipient address:
- If the last dropdown entry (`"── BTC Address ──"`) is selected, it reads `_manualAddressInput.Text.Trim()` and validates with `Bech32.IsValidGmAddress()`. An invalid or empty value shows `"Invalid address — must be a valid gm1q... address."` and returns.
- Otherwise, `recipientAddress = _toAddresses[_toDropdown.Selected]` (a pre-populated `gm1q...` address).

After address resolution it guards against self-send, then calls `_networkRoot.CreateAndBroadcastTransactionToAddress(_selectedBot.NodeId, recipientAddress, amount)`. Success shows a truncated tx ID; failure shows a feedback string. On success, `_manualAddressInput.Text` is also cleared (visibility is retained so the user can immediately paste another address).

The `"── BTC Address ──"` option is the primary way to target passphrase-derived addresses (obtained from BTCWallet or CasinoFinances via the copy button) and any other `gm1q...` address that has no registered `NodeAgent`.

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

**Input preservation**: `RefreshDetailPanel` deliberately does not clear `_amountInput` or `_sendFeedbackLabel`. Those fields are only reset in two places: `SelectBot()` (when the user picks a different bot) and `OnSendPressed()` (after a successful send). Without this rule the periodic refresh would wipe whatever the user was typing into the amount field every 3 seconds.

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

---

## Chapter 18 — Phase 7: CasinoFinances Dev Scene

**Files added**: `Screens/CasinoFinances/CasinoFinances.tscn`, `Screens/CasinoFinances/CasinoFinances.cs`  
**Files changed**: `SceneManager.cs`, `MainMenu.tscn`, `MainMenu.cs`  
**Status**: Implemented (Phase 7)

### The Short Version (for everyone)

Phase 7 adds the **Casino Finances [DEV]** screen — a developer tool for inspecting the casino's own BTC wallet. The casino has held a `gm1q...` address since Phase 3 (WalletInitializationService). This scene makes its seed words, base wallet balance, and passphrase wallet accessible via a UI that mirrors the player-facing BTCWallet scene, with one key difference: seed words can be shown at any time without a first-launch gate.

---

### 18.1 — Scene Architecture

The scene follows the same three-mode panel pattern as `BTCWallet`:

| Mode | Panel | Trigger |
|---|---|---|
| `Base` | `BaseWalletPanel` | Default / back navigation |
| `PassphraseLocked` | `PassphraseLockedPanel` | "Open Passphrase Wallet →" button |
| `PassphraseUnlocked` | `PassphraseUnlockedPanel` | Unlock button / Enter key |

`SetMode(WalletMode)` shows the active panel and hides the other two. On leaving `PassphraseUnlocked` mode, `_passphraseInput.Text` is cleared and `_currentPassphraseAddress` is nulled.

---

### 18.2 — Seed Words Popup

`SeedWordsPopup` is a full-screen `Panel` node (last child of the root `Control`, renders on top) initially `visible = false`. It is opened by the **[Show Seed Words]** button — which is always present, with no `HasSeenSeedPopup` gate. This is the key difference from `BTCWallet`: the casino's seed words are never "seen and dismissed" — they are always accessible for dev inspection.

The popup shows the three seed words at 44pt font, a **[Copy to Clipboard]** button (`DisplayServer.ClipboardSet(string.Join(" ", SeedWords))`), and a **[Close]** button that simply hides the popup.

---

### 18.3 — Balance Refresh

`_refreshTimer` in `_Process()` triggers `RefreshBalances()` every 2 seconds:

```csharp
private const double RefreshInterval = 2.0;

private void RefreshBalances()
{
    var (confirmed, pending) = _networkRoot.GetAddressBalanceDetails(_casinoWallet.BaseAddress);
    _balanceLabel.Text = $"Balance: {confirmed:F8} BTC";
    _pendingLabel.Visible = pending > 0m;
    _pendingLabel.Text    = $"Pending outgoing: {pending:F8} BTC";

    if (_currentPassphraseAddress is not null)
    {
        var (pc, pp) = _networkRoot.GetAddressBalanceDetails(_currentPassphraseAddress);
        _passBalanceLabel.Text  = $"Balance: {pc:F8} BTC";
        _passPendingLabel.Visible = pp > 0m;
        _passPendingLabel.Text    = $"Pending outgoing: {pp:F8} BTC";
    }
}
```

Passphrase wallet balance is only queried when `_currentPassphraseAddress` is non-null (i.e., when the wallet is unlocked).

---

### 18.4 — Passphrase Wallet Mechanics

Identical to `BTCWallet` (Chapter 15.5). Clicking **Unlock** (or pressing Enter):

1. `passphrase = _passphraseInput.Text.Trim()`
2. `seedPhrase = string.Join(" ", SeedWords) + " " + passphrase`
3. `_currentPassphraseAddress = CryptoUtils.DeriveGmAddress(seedPhrase)`
4. Clears `_passphraseInput.Text` immediately
5. Shows the unlocked panel with the derived address and its balance

**Send BTC** buttons in both base and passphrase panels are `disabled = true` placeholders. Full send capability for the casino wallet is deferred to Phase 8.

---

### 18.5 — Navigation

**`SceneManager.cs`**: `CasinoFinances` added to `SceneId` enum and `Paths`:

```csharp
[SceneId.CasinoFinances] = "res://Screens/CasinoFinances/CasinoFinances.tscn"
```

**`MainMenu.tscn`**: `CasinoFinancesBtn` button (`text="Casino Finances [DEV]"`, `font_size=34`, `min_size=(420,0)`) added after `BotsBtcWalletsBtn`.

**`MainMenu.cs`**:
```csharp
GetNode<Button>("%CasinoFinancesBtn").Pressed +=
    () => _sceneManager?.Go(SceneManager.SceneId.CasinoFinances);
```

The `[DEV]` label marks this as a developer tool pending player-facing integration with the BTC/SC trading mechanic.

---

---

## Chapter 19 — Phase 8: Player and Casino BTC Wallet Send

**Files changed**: `NetworkRoot.cs`, `BTCWallet.tscn`, `BTCWallet.cs`, `CasinoFinances.tscn`, `CasinoFinances.cs`  
**Status**: Implemented (Phase 8)

### The Short Version (for everyone)

Both the `BTCWallet` (player) and `CasinoFinances` (casino) scenes now support full outbound BTC transfers. The player can send from their base wallet or any passphrase-derived wallet; the casino can do the same. Both scenes share the same four-mode architecture and the same `"── BTC Address ──"` manual entry pattern from Phase 6.1.

---

### 19.1 — Four-Mode Architecture

Each scene gains a fourth `WalletMode.Send` that renders a programmatic send panel (`_sendPanel`, a `VBoxContainer`) appended to `RootMargin/RootVBox` in `_Ready()`. Only one panel is visible at a time:

| Mode | Panel visible |
|---|---|
| `Base` | `BaseWalletPanel` |
| `PassphraseLocked` | `PassphraseLockedPanel` |
| `PassphraseUnlocked` | `PassphraseUnlockedPanel` |
| `Send` | `_sendPanel` (programmatic) |

`EnterSendMode(senderNodeId, senderAddress, returnTo)` is the shared entry point. It stores who is sending (`_sendFromNodeId`), which mode to return to on Cancel (`_modeBeforeSend`), populates the recipient dropdown, clears the amount and feedback fields, and calls `SetMode(WalletMode.Send)`.

---

### 19.2 — Recipient Dropdown

`PopulateToDropdown(excludeAddress)` builds the same dropdown pattern used in Phase 6.1:

- Player base wallet (excluded when player is the sender)
- Casino base wallet (excluded when casino is the sender)
- All 14 bots from `BotWalletRegistry.AllBots`
- `"── BTC Address ──"` sentinel (last entry, `string.Empty` in `_toAddresses`)

When the sentinel is selected, `_manualAddressInput` (`LineEdit`, hidden by default) is revealed via the `_toDropdown.ItemSelected` lambda. `OnSendConfirmed()` reads the manual input and validates it with `Bech32.IsValidGmAddress()` before proceeding.

---

### 19.3 — Casino NodeAgent Registration

Before Phase 8, the casino had no registered `NodeAgent`, so `CreateAndBroadcastTransactionToAddress("casino", ...)` would always return `null`.

**Fix in `NetworkRoot.EnsureInitialized()`**: after the non-miner bot registration loop and before `ApplyStateFromSnapshot`, a `"casino"` `NodeAgent` is created from `WalletInitializationService.CasinoWallet.SeedWords`:

```csharp
string casinoSeed = string.Join(" ", casinoWalletState.SeedWords);
(string signPub, string signPriv) = CryptoUtils.DeriveSigningKeypair(casinoSeed);
string secp256k1Pub = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(casinoSeed);
var casinoNode = new NodeAgent("casino", casinoWalletState.BaseAddress,
                               signPub, signPriv, secp256k1Pub);
SharedNetwork.RegisterNode(casinoNode);
SharedNodesById["casino"] = casinoNode;
```

Registration before `ApplyStateFromSnapshot` ensures the casino node receives the same synced chain state as the player and miner nodes, giving it accurate UTXO awareness from the start of every session.

---

### 19.4 — Passphrase Wallet NodeAgent Registration

Passphrase-derived addresses are ephemeral — keys are derived on demand and no `NodeAgent` is persisted for them. Without a registered `NodeAgent`, sending from a passphrase wallet is impossible.

**`NetworkRoot.RegisterPassphraseWallet(string seedPhrase, string walletAddress) → string nodeId`**:

```csharp
public string RegisterPassphraseWallet(string seedPhrase, string walletAddress)
{
    EnsureInitialized();
    string nodeId = $"pass_{walletAddress[4..12]}";
    if (!SharedNodesById.ContainsKey(nodeId))
    {
        (string signPub, string signPriv) = CryptoUtils.DeriveSigningKeypair(seedPhrase);
        string secp256k1Pub = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seedPhrase);
        var node = new NodeAgent(nodeId, walletAddress, signPub, signPriv, secp256k1Pub);
        if (SharedNodesById.TryGetValue("player", out NodeAgent? player))
            node.Blockchain.TryReplaceChain(player.Blockchain.Chain,
                                             player.Blockchain.PendingTransactions);
        SharedNetwork.RegisterNode(node);
        SharedNodesById[nodeId] = node;
    }
    return nodeId;
}
```

- `nodeId` is `"pass_"` + 8 characters from the address — deterministic per address, no collisions with other node IDs.
- The player chain is synced via `TryReplaceChain` so the passphrase wallet has correct UTXO awareness.
- `!SharedNodesById.ContainsKey(nodeId)` makes the method idempotent — re-entering the same passphrase in the same session does not register a second node.

**Integration**: `BTCWallet.OnUnlockPassphrasePressed()` and `CasinoFinances.OnUnlockPressed()` both call `RegisterPassphraseWallet(seedPhrase, address)` after deriving the passphrase address, storing the returned nodeId in `_currentPassphraseNodeId`. The Send Passphrase button then calls `EnterSendMode(_currentPassphraseNodeId, _currentPassphraseAddress, WalletMode.PassphraseUnlocked)`.

---

### 19.5 — Send Flow

`OnSendConfirmed()` is identical in both scenes:

1. Guard: `_sendFromNodeId` must be non-empty.
2. Resolve recipient address: if last dropdown entry selected, read `_manualAddressInput.Text.Trim()` and validate with `Bech32.IsValidGmAddress()`. Otherwise, `_toAddresses[selected]`.
3. Parse `_amountInput.Text` with `CultureInfo.InvariantCulture` — must be a positive decimal.
4. Call `_networkRoot.CreateAndBroadcastTransactionToAddress(_sendFromNodeId, recipientAddress, amount)`.
5. On success: show `"Sent! [txId8chars...]"`; clear amount and manual address inputs.
6. On failure (`null` returned): show `"Rejected — insufficient balance or invalid route."`.

Cancel (`OnSendCancelled`) calls `SetMode(_modeBeforeSend)` — returning to whichever mode was active before entering Send.

---

*This document covers Phases 0.1, 0.2, 0.3, 0.4, 0.5, 1.1, 1.2, 1.3, 2, 3, 4, 5, 6, 6.1, 7, 8, and 9 of the BTC Wallet Address System.*  
*See `AIHelperFiles/btc-wallet-system-plan.md` for the full implementation roadmap.*  
*Last updated: 2026-06-14*

---

## Chapter 20 — Phase 9: In-Game Notepad

**Files changed**: `NotepadService.cs` (new), `NotepadPopup.cs` (new), `project.godot`, `BTCWallet.tscn`, `BTCWallet.cs`, `BotsBtcWallets.tscn`, `BotsBtcWallets.cs`, `BlockExplorer.tscn`, `BlockExplorer.cs`, `CasinoFinances.tscn`, `CasinoFinances.cs`  
**Status**: Implemented (Phase 9)

### The Short Version (for everyone)

The Notepad is a simple in-game text editor that lets the player save private notes — passphrase hints, wallet address labels, anything they want to remember. It is accessible as a popup from every BTC wallet-related screen. It is **not** a place to store seed words; that warning appears every time the notepad opens.

---

### 20.1 — NotepadService (Autoload)

`Scripts/Services/NotepadService.cs` is registered in `project.godot` as a global autoload. It owns all persistence for the notepad feature.

**Save file**: `user://notepad_notes.json` — a flat JSON object where each key is a note name and each value is the note content:

```json
{
  "My passphrase hint": "oak → wallet 1",
  "Casino address": "gm1q..."
}
```

**API**:

| Method | Description |
|---|---|
| `GetAllNames() → IReadOnlyList<string>` | All saved note names, sorted alphabetically |
| `LoadNote(string name) → string` | Returns content for name, or empty string |
| `SaveNote(string name, string content)` | Creates or overwrites; persists immediately |
| `DeleteNote(string name)` | Removes entry; persists immediately |

Dictionary keys are user-provided note names and are stored verbatim (not camelCased). Both load and persist use `System.Text.Json.JsonSerializer` via Godot `FileAccess`.

---

### 20.2 — NotepadPopup Component

`UI/NotepadPopup/NotepadPopup.cs` (namespace `UI.NotepadPopup`) is a fully programmatic `Panel` component — no `.tscn` needed. Any screen that needs the notepad calls `AddChild(new NotepadPopup())` and keeps a reference to call `Open()`.

**Layout** (all built in `_Ready()`):

```
Panel (full-screen anchor, Visible = false)
└── MarginContainer (80px horizontal, 60px vertical)
    └── VBoxContainer
        ├── HBoxContainer
        │   ├── Label "Notepad" (36pt, expand)
        │   └── Button "✕ Close"
        ├── Label (warning, 18pt, word-wrap)
        ├── HBoxContainer (load row)
        │   ├── Label "Saved notes:"
        │   ├── OptionButton _loadDropdown (expand)
        │   └── Button _deleteBtn (disabled until note selected)
        ├── Label "Note content:"
        ├── TextEdit _contentInput (min 260px height, vertically expands)
        └── HBoxContainer (save row)
            ├── Label "Save as:"
            ├── LineEdit _nameInput (expand)
            └── Button _saveBtn (disabled until both inputs non-empty)
```

**Overlay behavior**: `SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect)` makes the Panel fill the parent scene's root Control. Since it is added as the last child of the scene root, it renders on top of all other UI. `Visible = false` hides it until `Open()` is called.

**Warning**: Always visible at the top of the popup (below the title bar):
> ⚠ Never store your seed words in the In-Game Notepad or any digital document — not even this app. If your paper is lost, your BTC cannot be recovered.

---

### 20.3 — Interaction Flow

**Load a note**: Select a name from the dropdown. Content loads into the TextEdit; the name pre-fills the LineEdit; the Delete button enables.

**Write a new note**: Type content into the TextEdit; type a name into the LineEdit. The Save button enables when both have at least one character. Press Save → note is stored; the dropdown refreshes with the saved name selected.

**Overwrite an existing note**: Select from the dropdown (name loads into LineEdit), edit the TextEdit, press Save. The existing entry is overwritten in place.

**Delete a note**: Select from the dropdown, press Delete. Both inputs clear and the dropdown returns to the placeholder.

**Save button enable rule**: `_saveBtn.Disabled = string.IsNullOrEmpty(_nameInput.Text.Trim()) || _contentInput.Text.Length == 0`. The check fires on every keystroke in either input via `TextChanged` signals.

---

### 20.4 — Where the Notepad Button Lives

A `NotepadBtn` button (`unique_name_in_owner = true`) is added to the navigation bar of each address-related screen:

| Screen | Button parent node |
|---|---|
| `BTCWallet` | `RootMargin/RootVBox/TopBar` (between BackBtn and StatusBarPlaceholder) |
| `BotsBtcWallets` | `RootMargin/RootVBox/TopBar` (same position) |
| `CasinoFinances` | `RootMargin/RootVBox/TopBar` (same position) |
| `BlockExplorer` | `Margin/MainVBox/TopActions` (after BackToDiceButton) |

Each screen's `.cs` file adds `using UI.NotepadPopup;` and wires up the popup in `_Ready()`:

```csharp
private NotepadPopup _notepadPopup = null!;

// in _Ready():
_notepadPopup = new NotepadPopup();
AddChild(_notepadPopup);
GetNode<Button>("%NotepadBtn").Pressed += _notepadPopup.Open;
```

---

## Chapter 21 — Step 4: The Per-Node Candidate Block Model

**Files changed (4a)**: `Models.cs`, `MerkleTree.cs` (new), `BlockchainService.cs`, `NodeAgent.cs`, `NetworkRoot.cs`
**Files changed (4b.1)**: `BlockTemplateBuilder.cs` (new), `BlockchainService.cs`, `NodeAgent.cs`, `NetworkRoot.cs`
**Files changed (4b.2)**: `BlockchainService.cs`, `NodeAgent.cs`, `NetworkRoot.cs`
**Files changed (4c, surfacing)**: `BlockExplorer.cs`, `NetworkRoot.cs`
**Files changed (4b.3)**: `Models.cs`, `BlockchainService.cs`, `MerkleTree.cs`, `NodeAgent.cs`, `BlockTemplateBuilder.cs`
**Files changed (fee selector)**: `BTCWallet.cs`
**Status**: ✅ **STEP 4 COMPLETE** — Merkle + header hashing (4a); template builder + coinbase-in-block + maturity N=1 (4b.1); fees end-to-end (4b.2); content-hash txids (4b.3); BlockExplorer surfacing + BTCWallet fee selector (4c).
**Plan**: `AIHelperFiles/candidate-block-model-plan.md`

### The Short Version (for everyone)

Until now, mining in GamblingMiner used a deliberately simplified model: a miner just grabbed *all* of its pending transactions, glued them together with the previous block's hash and a nonce, and hashed that whole blob over and over until the result started with `00…`. It worked, but it wasn't how Bitcoin actually builds blocks.

Step 4 replaces that with the **real model**, the same shape Bitcoin uses:

- A miner builds a **candidate block** — its own proposed next block.
- The transactions in it are summarised into a single fingerprint called the **Merkle root**.
- Only a tiny **block header** (previous hash + Merkle root + timestamp + nonce) is hashed during mining — not the whole transaction list.
- Whoever's header hash hits the difficulty target first wins the block.

This chapter explains the pieces delivered in **4a** (the foundation), and previews what 4b/4c add. The headline benefits: it's **more realistic**, it's **faster** (we hash a short header, not a giant blob), and it's **tamper-evident** (change any transaction and the block's hash no longer matches).

> **Why split Step 4?** It's a big change to the heart of the chain, so we cut it into testable slices: **4a** = data model + Merkle + header hashing (this chapter's "done" part); **4b** = the mempool/template builder, fees, and moving the coinbase into the block; **4c** = showing all of it in the Block Explorer.

---

### 21.1 — What the old model did, and why we changed it

The old `BlockchainService.HashBlock` did this, once per nonce attempt:

```
hash = SHA256( previousBlockHash + nonce + JSON.Serialize({ transactions, index }) )
```

Two problems:

1. **Slow & unrealistic.** It re-serialised the entire transaction list to JSON on *every* attempt (~585 attempts per block). Bitcoin never does this — it hashes a fixed 80-byte header.
2. **No commitment structure.** There was nothing like a Merkle root, so the block had no compact, tamper-evident summary of its contents.

Step 4a fixes both.

---

### 21.2 — The Merkle Tree (`MerkleTree.cs`)

**Plain language.** A Merkle tree is a way to squeeze *any number* of transactions down to a single hash — the **Merkle root** — such that if even one transaction changes by a single character, the root changes completely. It's like a tamper seal for the whole list.

**How it's built:**

1. Each transaction is hashed into a **leaf** (`MerkleTree.LeafHash`).
2. Leaves are paired up and each pair is hashed together, halving the count.
3. If a level has an odd number of hashes, the last one is **duplicated** so it can pair with itself (this is exactly what Bitcoin does).
4. Repeat until a single hash remains — that's the **root**.

```
   leaf(tx0) leaf(tx1) leaf(tx2)          ← 3 transactions
        \      /          |
       hash(0,1)     hash(2,2)            ← odd → tx2 duplicated
            \          /
             \        /
            ROOT = hash( hash(0,1) + hash(2,2) )
```

**The leaf is a content hash.** `LeafHash(tx)` is the double-SHA256 of the transaction's *content* — amount, sender, recipient, fee, id, input data, spendable flag — **not** its signature. So the Merkle root commits to *what* a transaction does. (This content hash is exactly what will become the transaction's real id in 4b — see 21.6.)

**Double-SHA256.** Like Bitcoin, every hash here is SHA256 applied twice: `Sha256Hex(Sha256Hex(x))`. The historical reason in Bitcoin is defence against a class of length-extension attacks; we mirror it to stay faithful to the real protocol.

---

### 21.3 — The Block Header and why we hash it (`BlockchainService.HashHeader`)

Instead of hashing the whole block, we now hash a compact **header** made of four fields:

```
HashHeader(previousBlockHash, merkleRoot, timestamp, nonce)
   = doubleSHA256( "prevHash | merkleRoot | timestamp | nonce" )
```

This is the real Bitcoin idea: the header is small and fixed-size, and because it contains the **Merkle root**, hashing the header still effectively commits to every transaction in the block. The miner varies the **nonce** and re-hashes the header until the result meets the difficulty target.

**The difficulty target is unchanged.** A hash still wins if it starts with `"00"` and the next hex digit is `≤ '6'` (`IsHashAtTargetDifficulty`). That's about `7/4096` ≈ **1 in 585** attempts — the same block rhythm as before. We only changed *what string* gets hashed, not *how hard* it is.

**Validation now checks two things** (`ChainIsValid`, for every block after genesis):

1. The block's stored `MerkleRoot` must equal `MerkleTree.ComputeRoot(block.Transactions)` — the tamper check.
2. `HashHeader(prev.Hash, block.MerkleRoot, block.Timestamp, block.Nonce)` must meet the difficulty target and the chain links must match.

The genesis block is the one exception: its hash is the literal `"0"` (it was never mined), so it's validated by the separate genesis rules, not the header check.

---

### 21.4 — Why the timestamp moved *before* mining

This is a subtle but important change. The block **timestamp is now part of the hashed header**, so it must be fixed *before* the nonce search begins — you can't change it afterward without invalidating the hash.

Previously the timestamp was stamped on *after* mining (`HandleMinedBlock` overrode it). Now:

- `NodeAgent.MinePendingTransactions(reward, timestampUnixMs)` and `TryMineSingleNonceAttempt(reward, timestampUnixMs)` take the timestamp up front.
- `NetworkRoot` computes the timestamp (the in-game time, or the bootstrap's marching time) and passes it **into** mining; it no longer overrides it afterward.
- For the bet-driven path, `NodeAgent` caches the candidate's Merkle root (`_candidateMerkleRoot`) so that across the many bets it takes to find a block, only the nonce rolls — the expensive Merkle computation happens once per candidate, not once per bet.

---

### 21.5 — Coinbase maturity: why N = 1 here (the "100 confirmations" lesson)

Real Bitcoin won't let a miner spend a block reward until **100 confirmations** — 100 more blocks mined on top. It's a safety margin against chain reorganisations. People often assume "100" means a long time; it's really only ~100 × 10 min ≈ **16.7 hours**.

Here's the fractal twist that decides our value: in GamblingMiner **one block already spans ~16.25 in-game hours** (100× time scale, ~585 attempts/block). So the faithful equivalent of "~16 hours of maturity" is **≈ 1 block**, not 100.

Using 100 here would mean ~68 in-game days before any mined coin is spendable — and worse, it would break dated historical events (e.g. the 12 Jan Satoshi→Hal transaction spends an early coinbase that, at our compressed block heights, wouldn't be mature under a 100-block rule). So **N = 1** is the correct fractal maturity.

**Implemented in 4b.1.** The coinbase is now built **into** the block it rewards (transaction #0, via `BlockTemplateBuilder` + `BlockchainService.CommitBlock`), and `GetAddressData` enforces `CoinbaseMaturity = 1`: a coinbase is excluded from an address's spendable balance until at least one more block sits on top of it. Concretely, the reward for the block you just mined shows up once the *next* block is mined — the same ~1-block delay as the old model, but now with the realistic structure (and ready to collect fees in 4b.2).

---

### 21.6 — The template builder (4b.1, done) and what remains

**4b.1 ✓ — `BlockTemplateBuilder` + coinbase-in-block:**
- `BlockTemplateBuilder.Build(minerAddress, reward, mempool)` selects up to **23** mempool transactions by **fee** (highest first; equal fees keep arrival order = age tie-break), then prepends a **coinbase** paying `reward + collected fees` to the miner. Cap = **24 including the coinbase**.
- The coinbase is committed **inside** the block (`CommitBlock`), and only the *included* mempool transactions are removed from the miner's pending pool (unselected ones stay). The candidate is built once per `(tip, mempool)` state and cached on the node, so across the many bets it takes to find a block only the nonce rolls.
- Coinbase maturity `N = 1` is live (see 21.5). Fees are currently **0** (the `Fee` field exists but nothing sets it yet).

**4b.2 ✓ — fees end-to-end:** `Transaction.Fee` is now part of the **signed payload** (so the chosen fee is tamper-evident), and the sender pays **`Amount + Fee`** — enforced in `AddTransactionToPendingTransactions` and reflected in `GetAddressData`/`GetAddressSpendableBalance`. The miner collects the sum of included fees via the coinbase (the `BlockTemplateBuilder` from 4b.1). **The money conserves:** for a transfer of amount `A` with fee `F`, the sender loses `A + F`, the recipient gains `A`, and the block's miner gains `F` (on top of the block reward, which is the only new issuance). Miner bots now attach a random `0.1–1.0 BTC` fee when they recirculate BTC. The engine accepts a fee on every send path; the **player-facing fee selector in the wallet UIs is deferred to 4c** (UI sends currently pass fee 0).

**4b.3 ✓ — content-hash transaction id (OQ-C6):** the transaction id is now `BlockchainService.ComputeTransactionId` — the double-SHA256 of the transaction's content (amount, parties, fee, input data, spendability, and a uniqueness `Salt`). It's the *same* value used as the Merkle leaf (id and leaf agree), and `ValidateTransactionSignature` rejects any non-coinbase transaction whose id isn't its content hash. Because our account model has no UTXO inputs to make otherwise-identical payments distinct, the `Salt` provides that uniqueness — random for normal transactions, and the block height (`coinbase:{height}`, BIP34-style) for coinbases so equal-reward coinbases never collide. The genesis coinbase and the block-2 bootstrap transaction keep their human-readable sentinel ids (unique and unsigned); their Merkle leaf is still the recomputed content hash.

**Player fee selector ✓:** `BTCWallet`'s send form now has a **Fee (BTC)** field (blank = 0); the send is rejected if the wallet can't cover amount + fee. The dev wallets (Casino/Founders/Bots) send with fee 0 — they aren't player-facing.

> **Planned — network-wide fee activation (`~2009-04-26`, own branch).** Today bots/casino attach fees from the start while the scripted historical txs are fee-free — a dev-time contradiction. The planned model makes the **whole network fee-free until a `FeeActivationDate` ≈ 26 Apr 2009** (nearest mined block, just after the 18 Apr Hearn round-trip), after which **all** participants pay fees — matching early-Bitcoin's zero-fee history. Only the *attaching* of fees is gated (bot fee in `ScheduleBotTransactionsAfterBlock`, `CasinoTxFee`, player default → 0 before the date); this fee-collection engine is unchanged. See `AIHelperFiles/step8-utxo-realism-plan.md` OQ-8.7.

**4c ✓ — visibility:** the Block Explorer surfaces, for every block, the **Merkle root**, the block **time**, the total **fees collected**, and per-transaction **fee** with a **`[COINBASE]`** marker on transaction #0; the transaction lookup shows fee too. Bootstrap/founder blocks show a coinbase of exactly 50 BTC (no fees); blocks that include a bot recirculation transaction show a coinbase **greater than 50** (reward + collected fees) — the visible proof that fee collection works.

> **Reading a block's fees (avoid this common confusion).** The **coinbase does not pay a fee — it *collects* them.** It is new issuance, so the Block Explorer shows no `Fee` line on the `[COINBASE]` transaction. The only fee-paying transactions are the ordinary (non-coinbase) ones.
>
> So in a block with one bot transfer:
> - `Fee` on the transfer = the fee that transfer pays (e.g. `0.15176540`).
> - `Fees collected` = the **sum of every non-coinbase fee** in the block (here, just that one → `0.15176540`).
> - `Coinbase Amount` = **block reward + Fees collected** (`50 + 0.15176540 = 50.15176540`).
>
> The same number (`0.15176540`) therefore appears **twice for one single fee**, in two roles: once as the fee the transfer *pays*, and once folded into the coinbase amount the miner *collects*. It is **not** two separate fees — two fee-paying transactions would make `Fees collected` the sum of both, and the coinbase `50 + fee₁ + fee₂`.

---

### 21.7 — Migration note

The block structure changed (new `MerkleRoot`, new header hashing), so blocks saved before Step 4a fail the new validation. This is a **clean-save break**: delete `user://blockchain/` before running. The first launch afterward re-runs the historical bootstrap to 21 Mar 2009 on the new format.

---

---

## Engineering Note — Autobet Speed Selector Redesign

**Files changed**: `DiceGame.cs`, `DiceGame.tscn`, `BetHistoryContainer.cs`, `PreviousWinnerNumbersGrid.cs`, `SavedBettingStrategyRepository.cs`  
**Date**: 2026-06-15

### Background

The autobet system executes bets at a configurable rate measured in bets per real second (APS — Attempts Per Second). Each bet is simultaneously one mining nonce attempt, so APS is also the mining throughput. The original design used two separate controls:

- **SpinBox** (`BetsPerSecondInput`): range 1–9, the base APS
- **OptionButton** (`ApsMultiplierSelector`): options x1–x5, a multiplier applied to the base

Effective APS = SpinBox × OptionButton → range 1–45.

This two-control system worked reliably but imposed unnecessary cognitive overhead: selecting "18 APS" required thinking "9 base × 2 multiplier." The goal was to replace both controls with a single selector offering precise values from 1 to 99.

---

### Why This Proved Difficult

Three separate issues were encountered across multiple attempts. Each is documented here because understanding them is necessary to avoid reintroducing them.

---

### Issue 1 — SpinBox Intermediate `value_changed` Signals

**First attempt**: Replace both controls with a single SpinBox (range 1–99). The SpinBox already existed; changing `max_value` from 9 to 99 seemed sufficient.

**What happened**: When the SpinBox value was set to any two-digit number (≥ 10), the autobet speed locked at 1 APS instead of the selected value. The behavior persisted until the value was reduced back to ≤ 9.

**Root cause**: Godot 4's `SpinBox` processes its internal `LineEdit` text on each keystroke via `_text_changed`. When a user types "10":

1. The digit "1" is entered → `value_changed(1.0)` fires immediately
2. The digit "0" completes to "10" → `value_changed(10.0)` fires

The first signal fires `OnBetsPerSecondChanged(1.0)`, which calls `SaveActiveNodeStrategySnapshot()`. At that exact moment `GetAutoBetBaseAps()` reads `_betsPerSecondInput.Value = 1.0` (the confirmed value has not advanced to 10 yet) and saves `BetsPerSecond = 1` to `_nodeStrategies[_activeNodeId]`. The second signal fires correctly with value 10, but depending on frame timing the session can end up with the stale APS = 1 snapshot as the operative value.

The original two-control design was immune because the SpinBox max was 9 — the digit "1" alone equalled a valid, stable value, so the intermediate signal was harmless. As soon as the cap was raised to 99, any two-digit entry exposed the race.

---

### Issue 2 — OptionButton Crash from `GetItemMetadata(-1)`

A prior implementation attempt used a single `OptionButton` with items "1X" through "99X" and stored the APS integer as Godot `Variant` **metadata** on each item. The APS was read via:

```csharp
Variant meta = _apsSelector.GetItemMetadata(_apsSelector.Selected);
```

**What crashed**: When `_apsSelector.Selected == -1` (the OptionButton has no items, or has been cleared mid-initialization), `GetItemMetadata(-1)` throws an index-out-of-range exception in the Godot engine layer. This can be triggered if any signal fires `OnBetsPerSecondChanged` before `InitializeApsSelector()` finishes populating items, or if the OptionButton is cleared via `Clear()` and a downstream callback reads the selector before `AddItem()` runs.

---

### The Solution — Index-Based OptionButton, No Metadata

Both issues are solved by a single design change: use an `OptionButton` where the **item index directly encodes the APS value**.

```
index 0  → "1X"  (1 APS)
index 1  → "2X"  (2 APS)
...
index 98 → "99X" (99 APS)
```

Reading APS:

```csharp
private int GetAutoBetBaseAps()
{
    if (_apsSelector == null || _apsSelector.Selected < 0)
        return 1;
    return Math.Clamp(_apsSelector.Selected + 1, 1, MaxAutoBetBaseAps);
}
```

This eliminates Issue 1 because `OptionButton` emits `item_selected` only when the user explicitly clicks a finished choice — never on intermediate keystroke states. There is no typing, no transient value, no race condition.

This eliminates Issue 2 because `GetItemMetadata` is never called. The `Selected < 0` guard handles any empty-selector edge case by returning the safe default of 1 APS.

`InitializeApsSelector()` runs and populates all 99 items before the `ItemSelected` signal is connected in `_Ready()`. Even if `Select(0)` triggered a signal (it does not in Godot 4), the handler would see `Selected = 0`, which maps cleanly to 1 APS.

---

### Issue 3 — Display Throttling Plateau at APS = 20

After the OptionButton fix, actual bet execution scaled correctly across the full 1–99 range. However, the visual display in `BetHistoryContainer` and `PreviousWinnerNumbersGrid` appeared to plateau at approximately 20 APS.

**Root cause**: `IsHighFrequencyAutoMode()` returned `true` when `GetAutoBetBaseAps() >= 10`. Both UI components used this flag to skip 3 out of every 4 bet events (`HighFrequencySampleEvery = 4`), showing only 1-in-4 updates. In the original two-control system, the SpinBox was structurally capped at 9, making `IsHighFrequencyAutoMode()` permanently `false` — every bet was displayed regardless of effective APS. With the new system, the threshold activated at APS = 10, cutting the visible update rate from 9/s (at APS = 9) to 2.5/s (at APS = 10): a sudden 3.5× regression in perceived speed.

A dynamic-interval fix was then attempted (`interval = Max(1, APS / 10)`) to normalize visible updates to ~10/s at all APS values. This created a new problem: all APS values from 20 to 29 produced `interval = 2`, making the display appear unchanged across that entire 10-unit range. The user perceived it as a hard ceiling at 20.

**Final fix**: All display throttling was removed from both components. `BetHistoryContainer` and `PreviousWinnerNumbersGrid` register every bet event directly:

```csharp
private void OnBetExecuted(string _, BetTransactionEvent betEvent)
{
    AddEntry(betEvent);  // no sampling, no skip counter
}
```

Both components use pre-allocated object pools (260 items, `MoveChild` only — no node creation or destruction). At 99 APS / 60 fps, this produces 1–2 pool operations per frame, well within Godot's render budget. `IsHighFrequencyAutoMode()` is retained in `DiceGame.cs` as a permanent `false` stub — a placeholder for future throttling if APS ranges increase significantly beyond 99.

---

### Key Rules to Preserve

1. **Never read `SpinBox.Value` as a proxy for a user-selected integer in any path triggered by `value_changed`.** Godot SpinBox fires intermediate values while the user types. For integer-selection menus, use `OptionButton` with index encoding instead.

2. **Never call `OptionButton.GetItemMetadata(Selected)` without first confirming `Selected >= 0` and `ItemCount > 0`.** During startup signal wiring the selector may be empty. Prefer index-only encoding — it requires no metadata at all.

3. **Connect the `ItemSelected` signal after `InitializeApsSelector()` finishes.** Connecting before population creates a window in which any signal dispatch finds `Selected == -1` and would crash a metadata read or produce a default that overwrites a valid session state.

---

## Chapter 22 — Referral Auction (Starter): Gradual Non-Miner Introduction

**Files changed**: `NetworkRoot.cs`, `BlockExplorer.cs`, `BTCWallet.cs`
**Status**: **Starter implemented** on the `scheduled-bot-transactions` branch (2026-06-21). Auction *timing + winner* are live and observable; the *commission economy* (1%→5% payout) is still gated (Casino Rank System, casino finances, bot betting sim).
**⚠️ Canonically amended three times**: (2026-07-09, Step 14 EB.2 — §22.6) introduction gate → Market Birth, bidder eligibility → casino players only; (2026-07-10, Step 14 ND.4b/ND.4c — §22.7) cumulative-donation leaderboard → real ascending auction, window 100 days (fixed, first-bid-only) → **20 days, rolling, resets on every raise**; (2026-07-10, same day — **§22.8, current bidding rules**) the player's own raise floor split from the casino-bots': **1 satoshi** for the player, the §22.7 10%/20% formula unchanged for `bot_1..4`. §22.1's original timing model and §22.6's bidding rules below are both superseded starters — read straight to §22.8 for how bidding actually works today.
**Plan**: `AIHelperFiles/scheduled-bot-transactions-plan.md` (starter) · `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §5.2–5.3 (the EB.2 amendment) · same plan, ND.4b/ND.4c (the ascending-auction rework)

### The Short Version (for everyone)

Non-miner "holder" bots (`non_miner_1`…`non_miner_10`) are the prizes of a **referral auction**: whoever donates the most BTC to one of them wins it as a permanent **casino referral**. Miner bots donate automatically (the recirculation scheduler); the player can donate from BTCWallet. The highest donor when a bot's window closes wins it forever.

The catch this chapter solves: the game **starts on 21 March 2009** (after the historical bootstrap), but the original design started each bot's window at the genesis instant (3 Jan) — those windows would have closed *before the player ever arrives*. So we switched to **gradual introduction**: bots enter the auction one at a time *after live mining begins*, each with a fresh window.

### 22.1 — The timing model (all derived from the chain)

Nothing here is persisted — it's all recomputed from the canonical chain, so it survives reloads with zero sync bugs:

- **Anchor** = the **first live block**: the first block mined by a non-founder (not Satoshi/Hal), i.e. roughly the player's first mined block on/after 21 March. (`FirstLiveBlockTimestamp`.)
- **Introduction**: non-miner *i* (0-indexed in the registry) enters the auction at `firstLiveTimestamp + i × 2 in-game days` (`NonMinerIntroIntervalMs`). So the holders appear gradually over ~20 in-game days, mirroring a community that grows over time.
- **Window**: each bot's auction runs **7 in-game days** from its introduction (`AuctionWindowMs`).
- **Status** (computed at "now" = the latest block's timestamp): `NotIntroduced` → `InAuction` → `Resolved`.
- **Winner**: when a window closes, the **top cumulative donor among donations confirmed by the close timestamp** wins it **permanently** (it leaves the auction forever). If nobody donated, it closes with no winner.

`NetworkRoot.GetNonMinerAuctionLedger()` returns this per-bot state (status, totals, leading donor, window close, winner); `ComputeAuctionLedger(nowMs)` is the shared core (the scheduler calls it at the new block's time, the explorer at the latest block's time).

### 22.2 — Who can be donated to

The recirculation scheduler now targets **only non-miners currently in an open window** (`InAuctionNonMinerAddresses(block.Timestamp)`). Before the first bot is introduced, the pool is empty (no donations); once a bot resolves, the scheduler stops donating to it (the auction is over). The player may still *send* to any address, but donations after a window closes don't change its winner (they're past the close timestamp).

### 22.3 — Seeing it: BlockExplorer "Auction / Company Mode"

A toggle (default off) reveals the auction: a header counter (`In auction / Resolved / Not yet introduced`), each in-auction bot with its total received, donor count, **leading donor**, and **days left**, then a list of resolved bots with **who won** each. This is the player's window into the live auction.

**Naming (ND.9a, 2026-07-22):** the toggle was renamed **"Enroll Mode" → "Auction / Company Mode"** because it now surfaces founded companies (with a "Details →" into `CompanyDetails`) alongside still-recruitable auctions. **Deferred, non-priority Basic-Mode objective:** in Basic Mode the toggle reverts to **Auction Mode only**, and founded companies move to their **own dedicated scene/list** (not folded into the explorer). Internal identifiers keep the `enrollMode` name (code-facing).

### 22.4 — What's deliberately *not* here yet (starter scope)

- **No commission payout.** Winning records the referral (derived), but the 1%→5% SC commission isn't paid — that needs the **Casino Rank System** (sets the %), casino finances (P6, the payer), and the bot betting simulation (the SC-winnings source).
- **No persisted enrollment / `Referrals` scene yet.** Winners are derived on the fly; the dedicated `Referrals` + Miner Referrals scenes come later.
- **Tuning is provisional.** The 2-day stagger and 7-day window are starting values, chosen to be "realistic enough," not final.

### 22.5 — Migration note

Auction state is derived, so there's no new save format. But it depends on the bootstrap having run (to define the first live block), so as always start from a clean `user://blockchain/` when testing.

### 22.6 — Canonical amendment (Step 14 EB.2, 2026-07-09): Market-Birth gate, windows, first-bid countdown

Four decisions (D-EB.4/5/6/7, `step14-historical-network-population-scheduler-plan.md` §5.2–5.3) supersede §22.1's starter timing model; a round-3 revision (D-EB.8/9/10, §6) then retuned the pool size and window. All implement at Step 14 EB.2:

- **Introduction gate & rhythm (D-EB.4, pool size revised by D-EB.8)**: non-miners no longer enter 1-per-~2-days from the first live block. The referral process starts at **Market Birth** (2010-07-18, `BtcMarketDataService.FirstDataDateLocal` — the dataset shows active addresses ≈ blocks/day through all of 2009, i.e. zero non-miner holders until the first exchange), and the pool populates along the **active-address curve**: `1 + 12.18 per address-decade` (running-max since birth; the anchor is the birth day itself — pre-birth spikes like the July-2010 slashdot surge are excluded). The pool is capped at **40** (raised from the original 10 at round 3, 2026-07-09 — D-EB.8): empirically, all 40 deploy by **2017-12-13** (20 by the June-2011 bubble, 30 by the 2013 bubble), front-loaded near Market Birth by design (the log curve's shape — accepted, D-EB.10, as historically motivated: real address counts did jump fast right after Mt. Gox opened). The dataset's true active-address peak is **2021-04-15** (1,366,494; birth→peak span 3.201 address-decades) — an earlier "Dec 2017 / 2.9 decades" claim in this section was never checked against the CSV and has been corrected. One shared pure `date → count` function drives canonical live play AND the EB.1 entry-year fast-builds. `NonMinerIntroIntervalMs` is retired.
- **Window duration (D-EB.5, revised by D-EB.9)**: **100 in-game days** (confirmed 2026-07-09, superseding both the 7-day starter and an interim 6-in-game-month provisional value chosen when the pool was still only 10) — with a much larger pool, individual windows can run shorter without any single auction feeling rushed. **⚠️ Superseded again the next day (§22.7, D-ND4b.1): 20 in-game days, and now rolling (resets on every accepted raise) rather than counted once from the first bid.**
- **Countdown anchor (D-EB.6)**: the window countdown starts at the bot's **first QUALIFYING donation — its first real bid** — not at its introduction. A bot nobody has bid on stays **recruitable indefinitely**; every window that expires necessarily has ≥ 1 bid, so **every resolved auction has a winner** (§22.1's "closes with no winner" state disappears). The "leading donor" itself was a cumulative donation SUM under this amendment — **§22.7 replaces that with a real ascending-auction ratchet.**
- **Bidder eligibility (D-EB.7, corrected 2026-07-09)**: **only casino players can bid** — a referral is a casino relationship, and an entity that doesn't play at the casino can't hold one. The real test is **mining that requires betting at the casino** (bet-driven, hardware-credit-locked speed — exactly how the player mines): today that means **the player AND the classic casino-miner-bots `bot_1..4`** (`BotWalletRegistry.MinerBots`) — they already run real betting sessions (`StartBots`/`BuildBotConfigs`) to mine, so their existing sell-flow donations qualify as bids with no new machinery. The much larger, historically-growing **Step-14 cast** of miners (`BotWalletRegistry.CastMiners`, ND.2 — up to 29 additional beyond the classic 4, 33 total at the 2025 maximum) does **NOT** qualify: they mine via drained attempts (founder-style, concurrent with the player's time advancement), never place a bet, and so never form a casino relationship. Every non-qualifying transfer — the cast's sell-flow, any non-miner↔non-miner exchange, EB.1's seed-funding pass — is **economy, not a bid**: it funds wallets but never starts a countdown, never leads, and never wins; the auction ledger distinguishes qualifying bidders from mere senders. In EB.1 entry-year worlds (where `bot_1..4` are NOT part of the fast-build — they enter fresh with the landing crew, §35 precedent) the seed pass therefore funds holders **without touching auctions** — the player lands to pristine, not-yet-started windows. **Recommendation, not yet built**: keep the casino-miner-bot roster fixed at the classic 4 — promoting cast miners to full casino-player status is a substantial, separate feature (its own "casino membership growth" system), deferred rather than a natural extension of this plan.

**Scaling further, later**: pushing the pool past 40 (toward 100–220, the developer's original interest) needs a companion architecture change first — every registered `NodeAgent` currently receives every broadcast block/transaction and locally replicates chain state for UTXO checks, so registered node count directly multiplies a cost that already grows with chain length. The detailed theoretical write-up of the fix (decoupling non-miner wallet identity from full broadcast registration) is now documented as a not-built option in **Chapter 36 §36.6** — see `step14-historical-network-population-scheduler-plan.md` §6.2/§6.4/§9.7 for the full investigation.

§22.1–22.4 above are kept as the historical record of the starter; read them through this amendment.

### 22.7 — Canonical rework (Step 14 ND.4b/ND.4c, 2026-07-10): a real ascending auction, not a cumulative-donation contest

The FIRST ND.4 calibration playtest (a `TimelineConfig.DevEntryYear = 2010` world, §36.5, played 21 Mar → 17 Oct 2010) surfaced a real design gap in §22.6's model: **§22.6's "leading donor" is a running SUM of everything a bidder has ever sent**, so a bidder could win by drip-feeding many small donations rather than by ever placing one genuinely competitive bid, and there was no requirement that a later bid actually OUTBID the current leader by any margin. The developer's response (D-ND4b.1–13, `step14-historical-network-population-scheduler-plan.md` §ND.4b/ND.4c) replaces this with a real **ascending auction** — the mechanics below supersede §22.6's bidding rules; the introduction schedule (§22.6, D-EB.4/8) and bidder-eligibility test (§22.6, D-EB.7) are unchanged.

**The bid ladder.** A recruitable non-miner's very first qualifying donation is pinned at a **fixed 0.1 BTC floor** (not a fraction of the sender's balance) — this both funds the non-miner and activates its countdown. Every LATER donation must clear a strictly higher floor to become the new leading bid:

```
raiseMin(leadingBid) = max(0.1, 0.10 × leadingBid)
raiseMax(leadingBid) = max(0.2, 0.20 × leadingBid)
requiredNextBid       = leadingBid + raiseMin(leadingBid)   // the enforced floor; no upper cap
```

A donation below `requiredNextBid` still funds the non-miner (counted toward the displayed total/donor count, same treatment §22.6 already gives non-qualifying senders) but does **not** become leader and does **not** touch the countdown. `NetworkRoot.ComputeAuctionLedger` replaced its old cumulative-sum `TopDonor` helper entirely with this ratchet.

**The countdown is now rolling, and shorter.** The auction window shrank from §22.6's 100 in-game days to **20 in-game days** (D-ND4b.1) — and, critically, it now **resets to a fresh 20 days on every accepted raise**, not just once from the very first bid. A non-miner therefore stays in-auction indefinitely as long as competing bids keep landing inside each rolling window; it only resolves once 20 days pass with no bid clearing the floor. Internally, `FirstBidUnixMs` was renamed `LeadingBidUnixMs` to reflect that it now tracks the CURRENT leader's own timestamp, not literally the first-ever bid.

**Permanence across the rule change.** A non-miner already `Resolved` under §22.6's old cumulative-sum rule stays resolved forever — the new ratchet never reopens a past winner (D-ND4b.12, the existing "a win is permanent" invariant, unchanged). Because the auction ledger has no persisted state (it is always fully re-derived from the immutable chain — the same "nothing between blocks persists" principle as everywhere else in this engine), an `InAuction` non-miner's PRE-rework donation history is re-evaluated once, the first time the new code runs against it, under the new last-bid-wins logic — which can legitimately pick a different in-progress leader than the old cumulative model would have shown, a known and accepted consequence, not a bug, called out explicitly rather than silently absorbed.

**The casino-miner-bots' (`bot_1..4`) own bidding cadence is now independent of the historical tx budget.** §23's fullness-parity budget governs the CAST's sell-flow and non-miner↔non-miner exchanges, but `bot_1..4`'s referral-auction donations (`NetworkRoot.TryCasinoBotDonation`) run on their own separate per-block draw: a weighted 0/1/2 donation-COUNT roll each live block (15% / 70% / 15% — deliberately not a flat "always exactly one," which read monotone in the first playtest), each attempt targeting the **soonest-to-expire, affordable** recruitable non-miner (never a target no remaining bot can afford the floor for), sending either the fixed opening floor or a coin-flip between `leadingBid + raiseMin` and `leadingBid + raiseMax`. Every amount gets a small random additive tail (topped up, never subtracted, so a bid can never fail its own floor because of it) specifically so consecutive bids never repeat as clean round numbers — a purely cosmetic but deliberate realism touch. **Same-block collisions** (two bots' independently-computed bids, both racing the SAME pre-block leader, landing in the same block) are both broadcast for real — neither is rejected — but only the higher of the two counts as the new leader; the vanishingly-rare exact-amount tie breaks on chain append order (no new timestamp field needed, since both target/sender selection and the per-block count are already randomized, so this ordering already varies block to block on its own).

**Player-facing surfaces.** BlockExplorer's Enroll Mode now shows the leading bid's **LIVE, current SC value** alongside its BTC amount (`BtcMarketDataService`'s price as of NOW, recomputed fresh on every refresh — corrected 2026-07-11; "day-of-donation" / "point-in-time valuation" was a wording mistake in the original D-ND4b.10 spec that leaked into earlier drafts of this manual — nothing in this system displays a value frozen at a historical day; it is always re-priced against today's rate). BTCWallet's send panel shows a live, non-blocking amber warning — "sends below X BTC won't count as a competitive bid" — whenever the selected recipient is a currently-recruitable non-miner and the entered amount is under the enforced floor; the Send button is never disabled, the warning is purely informational. **The exact figure `X` is NOT the `RaiseMin` formula described above — see §22.8, an asymmetric refinement added the same day.**

### 22.8 — Refinement (2026-07-10, same day): the player's own raise floor is 1 satoshi, not 10%

Playtesting §22.7 surfaced a design question the developer resolved immediately: should the PLAYER be held to the same 10%-of-leader raise floor (`RaiseMin`) that governs the casino-bots' own bidding? The developer's answer is **no** — the player's minimum valid raise over the current leading bid is a flat **1 satoshi (`0.00000001` BTC)**, regardless of how large the current leading bid is. The casino-bots' own `RaiseMin`/`RaiseMax` formula (§22.7) is **completely unchanged** — this is a one-sided exception for the player's OWN bids only, not a change to the auction's overall raise economics.

**Why this is safe rather than a loophole.** The asymmetry is deliberately NOT risk-free for the player. `NetworkRoot.TryCasinoBotDonation` (the casino-bots' own bidding cycle) always computes ITS next raise against whatever the CURRENT leading bid is, using the unchanged `RaiseMin`/`RaiseMax` formula — it has no awareness that the leader got there cheaply. So a player who retakes the lead with the minimum possible 1-satoshi raise hands the very next casino-bot bid a 10–20% jump over that same low number, which is trivial for a bot to clear (bots' targets are affordability-filtered, so a small required raise is almost always achievable for at least one of `bot_1..4`). A player who wants to hold the lead for any real length of time still needs to bid meaningfully above the leader — the 1-satoshi floor is a **permission**, not a **recommendation**, and the game does not block or warn against the risk beyond the existing "below the minimum, won't count as a bid" message; the player is left to learn the tradeoff empirically, the same way a real underbid in a real ascending auction teaches the lesson.

**Where this lives in code** (`NetworkRoot.cs`):

```
OneSatoshi = 0.00000001m  // new constant, ND.4d

// ComputeAuctionLedger's per-block ratchet walk — the floor a candidate bid must clear now depends
// on WHO is bidding, not only on the current leader:
floor = !leader.HasValue
    ? MinBidBtc                                          // D-ND4b.5, unchanged — the opening floor
    : playerAddresses.Contains(donor)
        ? leader.amount + OneSatoshi                      // ND.4d — the player's own floor
        : leader.amount + RaiseMin(leader.amount)          // D-ND4b.6, unchanged — the bots' floor

// GetMinimumCompetitiveBidBtc(address) — the figure BTCWallet's warning label reads — is ALWAYS the
// player's own floor (it only ever answers "what does the PLAYER need to send"), so it now returns
// leadingBid + OneSatoshi directly, never RaiseMin.
```

`playerAddresses` is a new set split out from the existing `qualifyingBidders` (which still combines the player AND `bot_1..4` for the unrelated bid-eligibility test, D-EB.7 — unchanged) specifically so the ratchet walk can distinguish "is this bid FROM the player" from "is this bid a QUALIFYING bid at all." Both checks matter and answer different questions; conflating them would have wrongly given `bot_1..4` the 1-satoshi floor too.

**Worked example.** Casino-bot leads at 0.40000000 BTC. Under §22.7's formula alone, the player would need `0.40000000 + max(0.1, 10%×0.4) = 0.50000000` BTC to retake the lead. Under this refinement, the player only needs `0.40000001` BTC. Suppose the player sends exactly that and becomes leader. The next casino-bot bid still computes its OWN required raise the old way: `0.40000001 + max(0.1, 10%×0.40000001) ≈ 0.50000001` BTC — a routine, easily-affordable jump for `bot_1..4`'s cycle, so the lead likely changes hands again within the very next donation-count roll (§22.7 — up to 2 attempts per block, ~1 on average). The player retains the OPTION to bid a real margin above the leader instead (there is no upper cap on a bid, D-ND4b.6) precisely to avoid this outcome — the choice, and its consequence, is entirely the player's.

### 22.9 — Auction Settlement: SC cashback for tracked donors (Step 14 ND.5, 2026-07-10) — ⚠️ SUPERSEDED at ND.8b (see §22.12)

> **⚠️ Superseded (Step 14 ND.8b.2, 2026-07-19, D-ND8.14):** the SC cashback and the BTC sweep this section describes are **retired**. Auction close now FOUNDS a company: the non-miner keeps its BTC as its treasury, and the tracked pool mints the NST/PST stock distribution instead; value returns to backers over time as dividends. The tracked-pool mechanics, the once-per-resolution block-diff trigger, and the two-valuation-moments rule below all survive unchanged — only the *effect* of resolution changed. Full write-up: **§22.12**. This section is kept as history.

Once §22.7's auction actually resolves, the donors who fed it received nothing back — the non-miner just sat there holding their BTC as a permanent referral. ND.5 (`step14-historical-network-population-scheduler-plan.md` §7, D-ND5.1…10) closes that loop: every non-miner tracks its own **Tracked Donation Pool** (a *Glossary* term — the global top-10-by-BTC-value ranking of every qualifying donation it has ever received, win-or-lose bids alike, competed for by every donor together), and the instant its auction resolves, every donor still holding a slot in that pool gets paid back in SC, and the non-miner sweeps the pool's BTC to the casino.

**The tracked pool is value-ranked, not chronological.** `ComputeAuctionLedger` now computes it alongside the ratchet walk: as each qualifying bid arrives in chronological order, it joins the pool if fewer than 10 are tracked, or evicts the CURRENT SMALLEST tracked amount if it is strictly larger (a tie never evicts — first-in stays). This means the pool is NOT "the 10 most recent bids" — a large early bid can easily outlast many later, smaller ones. BTC from an evicted (or never-tracked) donation becomes the non-miner's own property **forever** — excluded from both the SC payout and the BTC sweep below, even after the non-miner becomes a referral.

**Two valuation moments, deliberately different (corrected 2026-07-11).** BlockExplorer's Enroll Mode and the `AuctioningCompanyDetails` scene's live list both show each tracked donation's **LIVE, current** SC value (`BtcMarketDataService`'s price as of NOW, recomputed fresh on every refresh) — informational only. **Earlier drafts of this section (and the original D-ND5.3 spec text) wrongly called this a "day-of-donation" value; that was a writing mistake, corrected here — nothing in this system ever displays or computes a value frozen at a historical day outside of settlement itself.** Settlement instead revalues the ENTIRE pool at the **closing date's** price (the resolution block's own game-calendar date), applied uniformly regardless of each donation's original date — this is the ONE place a non-"now" valuation is deliberately used. The list view answers "what is this worth right now"; settlement answers "what is this worth today, the day the auction actually closed." Implementations must not conflate the two.

**Settlement fires exactly once, from a block-diff, never from a UI refresh.** `ComputeAuctionLedger` stays a PURE function, called freely and repeatedly by every UI panel that shows auction state. Settlement (paying SC, sweeping BTC) is a real state-changing event, so it needs its own trigger: `NetworkRoot.TrySettleResolvedAuctions(block)`, called once per live block from `HandleMinedBlock` (alongside `ScheduleBotTransactionsAfterBlock` / `TryDistributePendingCasinoRewards`). It diffs the CURRENT block's auction ledger against the PREVIOUS block's (recomputed on demand from the previous block's timestamp — no new persisted state, consistent with "nothing between blocks persists") and settles only the non-miners whose status just flipped `InAuction → Resolved` on THIS block. A `user://logs/auction_settlement_trace.csv` telemetry row is appended per settlement so a playtest can directly verify the trigger fired exactly once per resolution.

**Payout: per-donor, funded by an on-demand Main-only auto-loan.** Every tracked donation is grouped by donor and summed, so a donor with several tracked donations gets ONE aggregated SC payout (the closing price is the same for all of them anyway). Funding comes from `CasinoScBalanceService.MainBalance` ONLY — never the Bankroll. If Main can't cover the total settlement, the casino draws on-demand `AutoLoanAmount` chunks into Main first (`CasinoScBalanceService.PayFromMainWithAutoLoan` — the exact same bankruptcy-flavor loan pattern `TryAutoRecharge` already uses for the Bankroll, just retargeted to a Main-coverage trigger). The player is paid via `PrincipalBalanceService.Deposit` + a new `CasinoClientLedgerService` entry (`Kind = "auction_payout"`, visible in `ClientsTransactions`/`ScTransactions`, excluded from the deposited/withdrawn totals like `swap_sc_in`); `bot_1..4` are paid via their `NodeFinancialState.PrincipalBalance` — the identical funding source and loan-fallback rule for both recipient types.

**BTC sweep: to the casino, fee deducted from the total.** Once every tracked donor is paid, the non-miner (now confirmed as the leading bidder's permanent referral, exactly as §22.7 already established) sends the tracked pool's TOTAL BTC to the casino in one transfer, following the engine's universal `amount + fee ≤ spendable` convention (`BuildAndBroadcastUtxoSpend`) — **the network fee comes out of the swept total**, so the casino ends up receiving `windowTotal − fee`, a hair short of the SC it just paid out. **This is an accepted, documented asymmetry, not a bug** — the developer's explicit call ("no importa que casino pierda un poco en el cambio"), flagged here as a candidate to revisit later only if a future rank/commission system needs the books to balance exactly.

**Display-only recomputation, never a second trigger.** The `AuctioningCompanyDetails` scene (reached via a "Details →" button in BlockExplorer's Enroll Mode, shown only for non-miners with a leading bid) shows the live tracked pool while `InAuction`, or a settlement summary once `Resolved` — but it never settles anything itself. `NetworkRoot.GetAuctionSettlementSummary(address)` is a second PURE function that reconstructs the same closing-price/payout/sweep figures purely for display, by finding the exact block that first crossed the auction's `WindowCloseUnixMs` and replaying the same math `TrySettleResolvedAuctions` already executed live. Since a `Resolved` non-miner's tracked pool can never change again (no further bids are structurally possible — Enroll Mode's button itself disappears once resolved, D-ND5.2/5.9), this recomputation is safe and always agrees with what actually happened.

### 22.10 — The Saturation Ladder: casino-bot re-bidding refinement (Step 14 ND.6, 2026-07-12; ND.8d bid-count-aware refinement 2026-07-20; ND.8d round-3 stuck-bidder escalation 2026-07-21)

A post-ND.5 playtest surfaced a **structural stall**: under the first-cut self-competition rules (never bid where you hold a top-5 tracked slot), the 4 casino-bots × 10 tracked slots per pool system converged on a deterministic absorbing state — every bot eventually held a top-5 slot in every open auction, every bot's target list went empty, no bot bid ever fired again, and any player leading bid rode its 20-day window uncontested, every time. ND.6 (`step14-historical-network-population-scheduler-plan.md` §8, D-ND6.1…10) replaces that hard filter with a **probabilistic** one — a re-bid probability that grows as a bot's position in a pool sinks — which has no absorbing silent state by construction. Lives entirely in `NetworkRoot.TryCasinoBotDonateOnce` + `TryBuildCasinoBotBid` (the old `FilterAndPrioritizeTargetsForBot` is deleted); nothing persists — every input is chain-derived per block.

**Terminology (D-ND6.3): "tier", never "rank".** A tracked slot's position in a pool's value order is its **tier** (tier 1 = largest donation … tier 10 = the smallest slot of a full pool). The word "rank" is reserved for the future casino ranking system and must not appear in this feature's code, telemetry, or docs.

**The ladder (D-ND6.4), two-mode since ND.6d.** Exact-tier re-bid probabilities, kept as literal constant tables — never derived from a formula. Which table applies is decided per pool by its **current occupied tracked-slot count**:

```
NORMAL     (pool has ≥7 occupied slots):             tier 4 → 5%   tier 5 → 8%   tier 6 → 13%   tier 7 → 21%   tier 8 → 34%   tier 9 → 55%
URGENCY    (NORMAL pool, final 7 days of its window): tier 4 → 8%   tier 5 → 13%  tier 6 → 21%   tier 7 → 34%   tier 8 → 55%   tier 9 → 89%
EARLY RUSH (pool has  <7 occupied slots):             tier 4 → 34%  tier 5 → 55%  tier 6 → 89%
```

Tiers 1–3 have no entry in any table — that is the **satisfied** state (a bot holding any top-3 slot in a pool never re-bids there). Tier 10 has no NORMAL/URGENCY entry: a bot whose best slot is tier 10 necessarily holds the smallest slot of a full pool, which the self-eviction guard (below) excludes before any roll — first removed as dead weight at ND.6c; restore a tier-10 entry (the sequence's next step) only if that guard is ever relaxed. The EARLY-RUSH table needs no tier 7+ entry: a pool in early-rush holds at most 6 slots (a 7th slot **is** the mode switch to NORMAL), so a best-slot roll there can only ever land on tier 4/5/6.

**ND.6e — the urgency ladder (2026-07-15, Option B from D-ND6.10's pre-approved levers).** The continuing 2011 playtest showed the scarcity recurring through ND.6d's own blind side: the early rush keeps YOUNG pools contested, but once pools matured to NORMAL mode the calm 5%/8%/13% shallow tiers throttled re-bids again — with 3 pools player-led, the `casino_bot_bid_trace.csv` tail (blocks ~1005–1119, 120 visits) read 79 `roll-declined` (66 of them NORMAL tier 4/5/6) against only 7 rolled re-bids into participated pools, with zero `nothing-affordable` (spendables 674–1448 BTC vs required 2–4 BTC — affordability again never the constraint). Rather than raising the whole NORMAL table permanently, the fix is **urgency-scoped**: while a NORMAL pool's rolling window is inside its **final 7 in-game days** (`IsAuctionInUrgencyWindow(WindowCloseUnixMs, nowMs)`, shared by the roll and the UI label), every tier rolls the URGENCY table — one Fibonacci level up per tier (`UrgentReBidProbabilityPercentByTier`). Challenges therefore cluster into an organic late-window "sniping" phase — exactly the shape Option B predicted — and an accepted raise (which resets the 20-day window, D-ND4b.1) drops the pool back to the calm table, so urgency is self-extinguishing. Early-rush pools ignore urgency (their table is steeper at every tier it has); unparticipated first-time bids stay deterministic (no window ⇒ never urgent). The `AuctioningCompanyDetails` section title flags the mode (`normal bidding — FINAL-WEEK URGENCY (≤7d left)`), and the per-slot `[re-bid NN%]` labels read the same urgency-aware source of truth.

**ND.6d — the early probability rush (2026-07-14, calibration fix).** A ~1-in-game-year 2011 playtest confirmed the stall recurred through a different door than ND.6 closed: the player's asymmetric +1-satoshi retakes (§22.8) kept pushing every contested bot's best slot **up** to tier 4/5, where the NORMAL 5%/8% roll left bots declining ~95% of the time — the `casino_bot_bid_trace.csv` tail was pure `roll-declined` at tier 4/5 with spendable ~1000 BTC (affordability was never the constraint) and **zero** donations, so the player won every referral uncontested. The fix is the EARLY-RUSH table above: while a pool is young (<7 tracked slots) the shallow tiers roll 34%/55%/89% instead of 5%/8%/13%, so casino-bots contest young pools hard; once a pool matures to 7 slots (a lot of competition has already happened) it reverts to the calmer NORMAL ladder. Mode is a pure function of the pool's live occupied count — no persisted state, self-correcting as slots fill. The `AuctioningCompanyDetails` pool panel now prints each slot's live re-bid chance (`[re-bid NN%]` / `satisfied`) and the pool's current mode in the section title, sharing the SAME `NetworkRoot.ReBidProbabilityLabel` / `ReBidProbabilityPercentFor` source of truth as the roll.

**Per-slot pipeline.** ⚠️ **Steps 2–4 below were SUPERSEDED by ND.10c on 2026-07-23** (see §22.14 and plan §14.4): the bot draw now restricts to *eligible* bots (D-ND6.1 reversed), and the spread-wide ordering + first-affordable walk + single ladder roll (D-ND6.6/6.8/6.5) collapsed into **parallel per-pool rolls with a tie-break among the hits** — every affordable qualifying pool rolls its own probability each slot. (That tie-break was **uniform** as shipped and became **weighted** at ND.10l on 2026-07-28, once telemetry showed always-hitting fresh pools crowding out escalated re-bids — see §22.14's "The weighted tie-break".) Step 1's qualifying rules (satisfied, self-eviction guard) and the ladder tables themselves are unchanged, and both now live in the shared `BuildBotPoolOpportunities`. The description below is retained as the historical record of the ND.6→ND.8d pipeline. Each donation slot (§22.7's weighted 0/1/2 per-block draw, unchanged) picks its bot uniformly at random among not-yet-used-this-block bots (D-ND6.1 — a bot keeps its full selection probability even when its own rules will produce no donation), and the chosen bot then runs:

1. **Qualifying pools (D-ND6.2/6.7)** — all currently `InAuction` non-miners, EXCEPT pools where the bot holds a top-3 tracked slot (satisfied — subsumes the old "never outbid yourself" rule, since the leading bid is by construction tier 1) or the smallest slot of a full pool (the **self-eviction guard**: its own new bid, entering a full pool, would evict its own smallest donation and forfeit the settlement refund already secured as the auction stands, §22.9). "Participation" = holds ≥1 CURRENTLY TRACKED donation there — so a bot whose donations get fully evicted from a pool re-engages it as if new, an intended self-balancing recycling.
2. **Bot-centric preference order (D-ND6.6)** — qualifying pools sorted by ascending count of the bot's OWN tracked slots (0-participation pools first: spread wide before ever re-bidding), ties broken soonest-to-expire.
3. **Half-spendable affordability walk (D-ND6.8)** — the first pool in that order whose `required + fee` fits within `spendable × MaxBidBalanceFraction (0.5)` is THE target; the cap bounds the ENTIRE send (`required + additive tail + fee`), with the §22.7 coin-flip principal clamped under it and the tail's headroom measured against it.
4. **ONE ladder roll (D-ND6.5; ND.8d round-2 refinement)** — only if the selected target is participated. As shipped at ND.6 it rolled the single lowest-probability slot; **ND.8d round-2 (2026-07-20) sums the bot's TWO LOWEST slot re-bid probabilities** (`SumTwoLowestReBidProbabilities`, ranked by probability not tier — see the ND.8d subsection below), so a bot spread across multiple non-satisfied slots re-bids harder; a satisfied slot's 0 caps the sum back to a single value (the boost applies only to a bot with no satisfied slot). A failed roll = no donation this slot, never re-rolled against another slot or pool. Unparticipated targets donate deterministically (first-time bids need no ladder).

**The affordability cascade is the ONLY substitution path (D-ND6.9).** A rule-excluded target list or a failed roll consumes the slot — no other bot substitutes. But when the chosen bot HAS qualifying targets and can afford NONE of them under the half-spendable cap, the slot passes to another bot (which re-runs its OWN full pipeline), potentially through all four. Bookkeeping: a bot that cascades away is NOT marked used-this-block (it did nothing); only the bot that actually donates consumes its once-per-block eligibility.

**Why this fixes the stall.** Probabilistic filters have no absorbing state: as long as one bot sits below top-3 somewhere and can afford the raise, a challenge eventually fires. Auctions now end by two organic mechanisms instead of universal silence — top-3 satisfaction (the largest bidders occupy the satisfied seats and go quiet) and the half-spendable cap (raises grow geometrically, bot wealth only by mining income, so every bot eventually prices out — the auction's true terminator becomes economic rather than temporal). A player's "bid once and wait" strategy now survives a full window uncontested only ≈23% of the time even in the worst all-bots-shallow case (vs 100% before); the ladder defeats ABSENT players, not ACTIVE ones — an attentive player min-raising (§22.8) after each bot challenge still wins the auctions they actively defend.

**Mandatory telemetry (ND.6b).** Probabilistic rules cannot be calibrated from gameplay feel: `user://logs/casino_bot_bid_trace.csv` logs one row per BOT VISIT within a donation slot (same `slot` index + ascending `hop` when a cascade fires) — bot, outcome (`donated` / `no-qualifying-target` / `nothing-affordable` / `roll-declined` / `broadcast-failed`), selected pool, the bot's own tiers there (`"4|7"`), rolled tier + probability, required/amount/fee vs the cap figures. Every decline is logged, not just successful bids — the declines ARE the calibration signal.

**Pre-approved calibration levers (D-ND6.10).** The ruleset shipped as specified (Option A). **Option B (urgency-weighted ladder) was exercised at ND.6e (2026-07-15** — implemented as the discrete final-7-days URGENCY table above, not a continuous scale); if the player's min-raise strategy still feels too safe → **Option D** (escalation memory: after ≥N leader changes in a window, bump every bot's ladder tier one step in that pool). Two further options (wealth-aware aggression, per-bot cooldown) are documented in the plan for specific pathologies only. Known deferred issue: settlement sweeps drain bot BTC one-way into the casino while refunds arrive in SC the bots can't spend — the late game re-derives "player always wins" economically unless `bot_1..4` eventually get a swap-desk path (plan §9.8, deferred).

**ND.8d — the bid-count-aware shallow-tier ladder + last-bid preservation (2026-07-20).** A calibration run right after the ND.8f docs pass (3 pools open from Market Birth) exposed a residual: with only 4 bidders and a 3-tier satisfied band, the moment 3 distinct bots each held a distinct top-3 slot in a pool — near-instant in the early rush — that pool had at most ONE eligible challenger forever (each win enters the top-3 and pushes another bot down to tier 4 as the new lone challenger), and the bot holding a top-3 slot in all three pools went fully idle. All three pools converged to the identical *3-satisfied + 1-lone-tier-4-challenger* state; the bottleneck was the challenger **count**, not the ladder probability — which is why the ND.6d/6e bumps never cured it. Fix (`D-ND8d.1…7`): tiers 2 & 3 abandon the flat satisfied rule and re-bid on probabilities keyed by the pool mode AND the bot's OWN tracked-slot count in that pool (`ownTiers.Count`, "1 bid" vs "≥2 bids").

- **Tier 1 stays ALWAYS satisfied** — the leader never re-bids (no self-outbid, no window reset): the bots' last-bid preservation. The developer chose this over relaxing tier 1 and adding a separate bot leader-skip, so bot code needs no leader guard.
- **Tier 2 is NEVER satisfied**; **tier 3 is satisfied only at ≥2 own bids** (it locks its NST slot and rests once it has bid twice — exactly the `bot_3`/`bot_4` "why don't they place a second bid?" case). `SatisfiedTopTierCount` is retired; `IsBidderSatisfied(bestTier, ownBidCount)` replaces it.

```
              tier 2 (never satisfied)      tier 3 (satisfied at ≥2 bids)
              1 bid   / ≥2 bids             1 bid   / ≥2 bids
EARLY RUSH    21%     / 21%                 13%     / satisfied
NORMAL         5%     /  3%                  2%     / satisfied
URGENCY        5%     /  5%                  3%     / satisfied
```

Invariants (retuned 2026-07-20 round 2): **tier 2 out-probabilities tier 3 at EQUAL bid-count in every mode** (early 21>13 · normal 5>2 · urgency 5>3 — a deliberate inversion of the base ladder's "worse slot ⇒ more prob," since the runner-up closest to winning fights hardest), every value stays on the **Fibonacci** sequence, and **urgency ≥ normal** at every cell (the final-week snipe). Tiers 4–9 keep the bid-count-INDEPENDENT tables above; tier 10's self-eviction guard is unchanged. **Two-lowest-probability roll (round-2):** a participated pool's re-bid roll is the SUM of the bot's two LOWEST slot probabilities (`SumTwoLowestReBidProbabilities`, ranked by probability not tier, clamped to 100) rather than the single best-tier value — a bot spread across multiple non-satisfied slots re-bids harder; a satisfied slot contributes 0, so the sum caps back to a single value and the boost applies solely to a bot with no satisfied slot (single-slot bots unchanged). The gate stays best-tier `IsBidderSatisfied`; the per-slot `AuctioningCompanyDetails` label still shows each slot's own % (a deliberate display/mechanic split). `ReBidProbabilityPercentFor`/`ReBidProbabilityLabel` gained an `ownBidCount` parameter; the `AuctioningCompanyDetails` per-slot label now factors the occupant's bid-count AND blanks entirely on a **player-held slot** (`IsPlayerBidderAddress` — the player bids manually, never rolls the ladder).

**Player bid-safety warnings + last-bid preservation (BTC wallet, NON-blocking — the send ALWAYS proceeds).** Two advisory warnings join the existing below-minimum one: (a) *already-leading (last-bid preservation, round-2 correction 2026-07-20)* — a send from the CURRENT leader does **not count as a bid**: the `ComputeAuctionLedger` ascending ratchet skips any bid whose donor equals the current leader (recorded in `ignoredSelfRaiseSeqs`, also excluded from the tracked pool), so it never re-leads, never resets the 20-day window, and earns no stock — the player's leading bid simply stands; the transfer still leaves the wallet on-chain as a plain non-participating send, so the warning states exactly that (`NetworkRoot.IsPlayerLeadingCompanyBid`). This supersedes the round-1 wording, which wrongly described the re-bid as resetting the window — that was the OLD unpreserved behavior. Universal in the ledger, but only the player triggers it (bots' tier-1 satisfied rule keeps them off their own leader pool). (b) *closing-soon* — the auction closes within `AuctionClosingSoonWarningDays` (2) in-game days, so if no block is mined before close the bid may not be counted (`GetAuctionDaysUntilClose`). The label is multi-line; nothing is ever blocked.

**Stale-bid cancellation + cash-back (D-ND8d.7) — the closing-race safety net.** A bid counts only once MINED into a block at or before its target's `WindowCloseUnixMs` (D-ND4b.12 bars post-close bids); a late bid is worthless. Three coordinated pieces, for player + `bot_1..4` alike: (1) `ComputeAuctionLedger` now filters the tracked pool to bids with `ts ≤ WindowCloseUnixMs`, so a post-close bid earns no stock and joins no donor set — a no-op for InAuction companies (close in the future), biting only a resolved company's late bids; (2) `NetworkRoot.CancelAndRefundStaleAuctionBids` (per block, after founding) sweeps every still-PENDING qualifying bid to a resolved company from all mempools — since a mempool tx has not spent its UTXOs yet (the UTXO set is chain-derived, §30), the sender simply keeps the BTC: an implicit cash-back with no refund tx; (3) the edge net — a qualifying bid CONFIRMED in the very block that crossed the window (block timestamp > close) already moved BTC, so the founded company's treasury refunds it (`RefundStaleBid`, memo "· AUCTION REFUND", network fee deducted from the refund, the §22.9 sweep precedent). Only qualifying-bidder sends are touched — cast-miner sell-flow to a company is economy, not a bid. Console telemetry (`[ND.8d]`) logs each cancellation/refund.

**Future debt (documented, not built).** Move the whole bid system into internal casino wallets that then broadcast to the chain, so the contest is manageable without waiting for the next mined block to read the standings — the per-block-resolved warnings + this cleanup are the interim stopgap.

**ND.8d round 3 — the stuck-single-bidder escalation (2026-07-21).** A later playtest surfaced a DIFFERENT residual shape than rounds 1–2 fixed: on **The Silk Market** (`non_miner_6`), reconstructing the tracked pool from `casino_bot_bid_trace.csv` showed `bot_2` correctly satisfied (tier 1), `bot_3` holding tiers {2,6} with a genuinely decent `SumTwoLowestReBidProbabilities` (≈16–26%, confirmed against the trace's own logged values) but simply BUSY elsewhere (the bot-centric preference order sends it to zero-participation pools first), and `bot_4` ALONE at tier 4 rolling a flat, never-changing URGENCY-mode 8% every time it was drawn, declining indefinitely. Rounds 1–2 solved the *challenger-count* problem (too few eligible re-bidders per pool); this is a *flat-probability* problem — a lone bidder with nowhere better to go can sit at an unchanging low roll forever.

Fix (`NetworkRoot.ComputeStuckEscalationProbabilityPercent`): a bot holding EXACTLY ONE tracked slot at a non-top-3 tier (4–9) rolls an ESCALATION that grows **linearly** by that tier's plain NORMAL-mode base value, one step per block it remains stuck at the same tier — `escalation = base × (blocksElapsed + 1)`, clamped to 100% (tier 4 base 5%: 5, 10, 15, 20% … reaching 100% at block 20; tier 9 base 55%: 55%, then 100% by block 2). Reset the instant the bot re-bids there (now ≥2 slots, falling out of this path back to the round-2 sum) or ANY OTHER party's bid (another bot, or the player) changes its rank.

**⚠️ `max()` non-regression floor (2026-07-21 audit fix — supersedes "ignores early-rush/urgency entirely").** The first cut used the escalation to REPLACE the pool's mode rate. That regressed EARLY-RUSH pools: a single-slot below-top-3 bot there rolls the steep early-rush rate (tier 4/5/6 = 34/55/89%), but the replacement handed it the NORMAL base (5/8/13%) on block 1, only recovering over ~7 blocks — and active-pool churn kept resetting it to base, so it effectively sat near the low base indefinitely. This was **the DeepBit (`non_miner_7`) stagnation** a developer observed a few in-game months after Silk: reconstructed from `casino_bot_bid_trace.csv` block 1166, `bot_4` alone at tier 5 in a 6-slot (early-rush) DeepBit rolled `8%` where early-rush would have given `55%`. The caller (`TryBuildCasinoBotBid`) now composes it as **`max(mode-appropriate rate, escalation)`** — the escalation is a FLOOR ON TOP of the mode rate, never a replacement, so early-rush's steep young-pool aggression is preserved AND a genuinely stuck bot still escalates toward 100%. (This also cures the originally-diagnosed URGENCY case, where the raw escalation's block-1 tier-4 `5%` was itself below the urgency `8%` it meant to fix.) `ComputeStuckEscalationProbabilityPercent` is unchanged (still returns the raw NORMAL-base escalation and owns the edge-triggered signature side effect); only the caller's composition changed from replace to `max`.

**Label parity (2026-07-21, after the `max()` fix).** Verifying the escalation from the `AuctioningCompanyDetails` per-slot "re-bid NN%" labels, the developer saw no movement. The escalation lived ONLY in the roll (`TryBuildCasinoBotBid`); the label helper (`ReBidProbabilityLabel` → `ReBidProbabilityPercentFor`) computed only the static mode-rate table, so a lone below-top-3 slot's label sat frozen while its actual roll climbed (`bot_1`'s tier-5 BitInstant slot showed a fixed `8%` while the trace shows the roll went `8→16→24` across blocks 1206–1208). Fix: a **side-effect-free peek** (`PeekStuckEscalationProbabilityPercent` — reads `_stuckBidderSignatures` without stamping it, so a 1 s UI refresh can never corrupt the roll's timing; it and the compute path share `EscalatedStuckPercent`), consumed by a new **instance** method `ReBidProbabilityLabelForSlot(summary, donorAddress, tier, occupied, urgent, ownBidCount)` composing the SAME `max(mode rate, escalation)` the roll uses — resolving the occupant bot from the donor address (single-address bots, OQ-8.2) and reading the chain tip. The static `ReBidProbabilityLabel` was retired (its lone caller now uses the instance method). Labels now climb in lockstep with the roll, and an early-rush pool's label shows the `max()` floor.

**First cut and its gap.** The initial implementation derived "since" purely from `TrackedDonations`' current snapshot — the bot's tier is `1 + count(other tracked donations with a strictly greater amount)`, and "since" was the latest timestamp among GREATER-valued donations arriving after the bot's own (a rank-push from above). The developer asked, before playtesting further: what happens when a bot holding 2 slots (say tier 5 and tier 9) loses the SMALLER one to a third party's larger donation — does the surviving tier-5 slot's escalation correctly restart? It did not: nothing above tier 5 changed, so the snapshot-only read kept reaching back to the tier-5 donation's own (possibly old) timestamp, which could jump the probability near 100% instantly instead of ramping from base — wrong, because the bot spent its whole 2-slot period under the round-2 `SumTwoLowestReBidProbabilities` roll, never "stuck at 1 bid," so the escalation must start fresh the moment it actually becomes single-slot.

**Corrected design — an in-memory signal, event-driven, no `_Process`/polling (developer-directed).** A private dictionary `_stuckBidderSignatures: Dictionary<(nonMinerNodeId, botNodeId), (signature, sinceBlockIndex)>` — NOT part of `BlockchainStateSnapshot` (no checkpoint/pre-genesis/delete-list work; like `_lastMinedByNodeId`/`_currentMinerStreak` elsewhere in `NetworkRoot`, this is pure bidding-behavior bookkeeping that harmlessly resets on an app restart). The signature is `"multi"` when the bot holds ≥2 tracked slots, else `"single:{tier}"` — folding BOTH reset triggers (a rank-push AND a multi→single transition from losing another slot) into one comparable value. `ComputeStuckEscalationProbabilityPercent` is called on EVERY participated roll, multi- or single-slot alike, so the multi-slot state is always kept current; whenever the recorded signature differs from now, `sinceBlockIndex` is stamped to the current block index (`Block.Index`) — an edge-triggered update that fires exactly once per this bot's per-block evaluation, inside the SAME block-mined event that already drives the whole bidding cascade (`HandleMinedBlock` → `ScheduleBotTransactionsAfterBlock` → `TryCasinoBotDonation`), never oftener. `blocksElapsed = currentBlockIndex − sinceBlockIndex`; `probability = min(100, base × (blocksElapsed + 1))`. The earlier `chain`/`FindBlockIndexAtOrBeforeTimestamp` plumbing was removed as unnecessary once this signal replaced the snapshot-only derivation.

**Accepted residual imprecision.** If a bot's tracked donation is evicted all the way to ZERO slots in a pool and it later lands a brand-new donation at the exact same numeric tier by coincidence, the stale recorded signature would match and skip the reset it should trigger. Extremely low-probability (requires a full eviction to zero AND an exact tier-number coincidence on rejoining) — accepted rather than adding cleanup bookkeeping for it. Full diagnosis, matrix and decision log: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §12.5.5.

#### 22.10.1 — ND.10i (2026-07-27): the escalation slope collision, and the ceiling on the NST band

**The finding (DeepBit, `non_miner_7`).** Watching the pool's per-slot ladder, the developer saw *the tier-2 label climbing while tier 4 stood still, and both then reading 40%* — wrong for a ladder whose entire vocabulary is "the worse your position, the more desperate you are".

**Cause: two tables calibrated for different questions, meeting at a seam nobody had read side by side.** `StuckEscalationBasePercent` feeds the escalation's slope. Tiers 4-9 take the NORMAL ladder (5, 8, 13, 21, 34, 55); since D-ND10c.3 tiers 2-3 take the *shallow* table's NORMAL/one-bid cell (5, 2). That rule — "the base is the tier's plain NORMAL-mode value" — is internally consistent and lands **tier 2 on exactly tier 4's slope**. The shallow table was calibrated at ND.8d so that *tier 2 out-probabilities tier 3*; the deep table at ND.6 so that *deeper is more desperate*; neither had an opinion about tier 2 versus tier 4, and the composition gave a 2nd-place bot — inside the NST band, one raise from the lead — the same march to certainty as a 4th-place bot, and **2.5× faster than a 3rd-place** one.

Why one *looked* faster: the display is `max(mode floor, escalation)`, and the floors differ far more than the slopes did. In an early-rush pool tier 2's floor is 21 and tier 4's is 34, so a common slope of 5 lifts tier 2 off its floor at block 5 (→25) while tier 4 stays pinned at 34 until block 7 (→35); both then land on 40. In a NORMAL pool it is worse and invisible: floor 5 *and* slope 5 on both, so tiers 2 and 4 are numerically **identical at every block, forever**. Label and roll agreed throughout — ND.10c/d's shared-source discipline held; the ladder itself was the defect.

**D-ND10i.1 — tier 2 gets its own slope, 3.** Slopes now read `2 (t3) < 3 (t2) < 5 (t4) < 8 …`. The ND.8d tier2 > tier3 crossover survives *because it is about a different question*: tier 3 can be **satisfied** at ≥2 own bids and tier 2 never is — a statement about satisfaction, not about desperation, and it must not leak into the slope. The accidental 2/4 collision does not survive. `3` is the only Fibonacci-family value between 2 and 5 (D-ND6.4: every ladder literal is drawn from the sequence, never formula-derived).

**D-ND10i.2 — a ceiling of 34% on the NST band (tiers ≤ `NstTopTierCount`).** The audit raised a second, larger question that the slope fix alone does not answer. Any accepted bid must clear the leader's floor, so **every successful re-bid takes the lead and resets the 20-day rolling window** (D-ND4b.1). Combined with "tier 1 is always satisfied" (D-ND8d.1) and an escalation running to 100%, that is a **leapfrog engine**: the runner-up eventually bids every block, becomes leader and stops, the displaced leader becomes tier 2 and starts escalating, and while two solvent bots contest a pool the countdown never expires. DeepBit's own history shows it — opened ~27 Feb, still live 7 Apr in-game, its window already reset at least once.

Capping the NST band keeps ND.10c's fix (no lone tier-2/3 occupant frozen at a flat 5%/2% forever) without turning the runner-up into a metronome. Below the band the escalation still runs to 100%: a bot outside the stock-minting tiers has nothing to lose by pressing. Resolution of a genuinely contested pool then rests where §22.10 always intended it — **price-out, the economic terminator**. The ceiling bounds the escalation only; callers compose `max(mode rate, escalation)`, so a calibrated mode rate above it would still win (none is today — tier 2 tops out at 21 in early rush).

The rejected third option is worth recording because it is the most *semantically* correct: escalate a lone top-3 occupant only while nobody else has bid recently, since "stuck" should mean stuck and a pool with a fresh leader is working, not stalled. It needs a new recent-activity signal, which is more than this finding justified; if the ceiling proves to be the wrong lever, that is where to go next.

**A note on why equal numbers are normal.** `SweepStuckBidderSignatures` stamps every occupant of a pool at the same block when an incoming bid re-ranks it, so slots legitimately climb in lockstep afterwards and **only the slopes distinguish tiers**. That is what made this collision permanent rather than transient: with equal slopes, one re-rank left tier 2 and tier 4 indistinguishable for the rest of the auction.

**Two habits shipped with the fix, both aimed at the way it hid rather than at the defect itself.**

*The ladder now checks its own ordering at launch.* `AssertEscalationSlopesAreOrdered` — `[Conditional("DEBUG")]`, run once from `EnsureInitialized` — walks the slopes in their expected ascending order `t3 < t2 < t4 … < t9` and prints the offending pair if any step fails. It encodes the single intended exception explicitly, so the deliberate t3/t2 swap can never again be confused with an accidental collision, and it is stripped from release builds (a broken ladder in an exported build is undetectable anyway; the point is to catch it the moment someone edits a table). This is the same reflex as §39.15.1's clamp tripwire, which had proved itself two days earlier: **an invariant that lives only in prose is an invariant nobody checks.** The general form — *when several tables are consumed by one rule, assert the relationship between them where the rule lives, not in a comment* — is what both incidents are really about.

*The per-slot label shows the escalation's composition.* Where the escalation is the binding term, the label now reads `[re-bid 24% (base 3 ×8 blocks stuck)]`, gaining `, capped` once the ceiling binds; where the static mode rate still wins, nothing is appended, because there is no escalation story to tell. A bare `40%` cannot be told apart from another tier's `40%` — which, given that equal readings are *normal* after a re-rank, is precisely how the collision stayed invisible. `EscalatedStuckDetail`/`PeekStuckEscalationDetail` return `(percent, base, multiplier, capped)` and the previous percent-only helpers became one-line wrappers over them, so the roll path is untouched and label/roll parity holds by construction.

### 22.11 — Donor identity canonicalization (2026-07-14 playtest fix)

A bid whose coin selection spent a **change-address UTXO** used to be recorded under that raw derived address (the `tx.Sender` = `Inputs[0]` shim), making the player appear as a second, unnamed participant in Enroll Mode. All mechanics (qualification, the §22.8 one-satoshi floor, §22.9 payout routing) already recognized those bids as the player's via the full owned-address set — only the recorded identity and its display were wrong. `ComputeAuctionLedger` now canonicalizes player donations to the base address at record time, and `DescribeAddress` resolves derived addresses to the owning node. Because the ledger is chain-derived, the fix healed existing worlds retroactively with no migration. Full incident write-up + the "an address is a key, not an identity" rule: **§30.9**.

### 22.12 — The Business Migration (Step 14 ND.8b, 2026-07-19) — companies, stock-tokens, dividends & votes

The auction's abstract *recruitable non-miner / referral* is reworked into a **named historical company** miners fund and then co-own as shareholders (design rounds `D-ND8.1…39`, full spec `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §12.4; staged build log §12.5.2). The auction *front end* (§22.6–22.8, §22.10) is untouched — what changed is everything after resolution:

- **ND.8b.0/1 — identity.** `Data/Companies/company_roster.csv` (42 rows: 40 auctionable + Gavin's Faucet and Mt. Gox as non-auctionable flavor anchors, D-ND8.2/39) is loaded by the pure-static `CompanyRoster`; `non_miner_{i+1}` ↔ `Auctionable[i]` (appearance-date order), and the intro schedule takes `max(pace, appearance_date)` per slot (D-ND8.37 hybrid — the calibrated address-curve still sets the pace). Enroll Mode and the detail scenes display company names/bands/categories; "non-miner" survives only as the internal NodeId (D-ND8.10).
- **ND.8b.2 — founding (supersedes §22.9's settlement, D-ND8.14/15).** `SettleResolvedAuction` → `FoundCompany`: no SC cashback, no BTC sweep — the company keeps its on-chain balance as its **treasury**, and the tracked pool mints the stock distribution: `base = participationShare × 10,000 ST`, plus the halving slot-bonus ladder (5.2% at tier 1 → 0.0102% at tier 10); a top-3-tier holder mints **NST** (dividends + votes), everyone else **PST** (dividends only). Stored as `CompanyFounding` in `NetworkRoot`'s own `BlockchainStateSnapshot` (rides the block-commit rule + the `StatePath` delete-list entry by inheritance). Telemetry: `company_founding_trace.csv`.
- **ND.8b.3 — the governance engine (D-ND8.17/18/19b).** Block-driven (`TickCompanyGovernance`, from `HandleMinedBlock`, before `PersistStateToDisk` — every mutation commits in the same block write). Three vote kinds: **founding-day** (fires at close; initial reserve direction), **quarterly** (every 3 calendar months from founding; payout rate + reserve + ±1 market shift), **special reserve** (cumulative new inflow since the last vote close > 30% of the reserve value at that close; never reschedules the quarterly). Every vote runs 1 in-game day, applies as the window elapses. Resolution: NST-weighted continuous average for the reserve target, clamped to the Currency Band's ±25% range; ≥60% weighted supermajority for a market shift, clamped within ±1 of the roster default (D-ND8.7); ties/sub-threshold = status quo. Bots vote deterministically from per-world draws (D-ND8.13: a distinct 4-of-5 band set; D-ND8.26: a distinct market-category permutation — both persisted in the snapshot). Dividends (D-ND8.17): the quarterly vote **finalizes** `rate% ×` each reserve side separately (BTC / SC, never live accrual; category default rates 5/8/13/21 %/quarter — official→black, assistant-proposed calibration constants, ballots clamp to [0, 2× default]); **PST** accrue a date-diffed daily drip, **NST** get the lump at quarter end, residuals flushed so the finalized amounts distribute exactly. Bots auto-claim **every dividend arrival** — NST lumps and PST drips alike, swept on-chain the block they land (fee deducted from the claim; the only gate is the physical claim > fee, since a send cannot net ≤ 0). **Game pause (D-ND8.18):** while any open vote awaits the ballot of an NST-holding player, `NetworkRoot.IsAwaitingPlayerVote` pauses everything — `SimulationService` skips its tick and freezes the calendar in place; `DiceGame` refuses manual bets with a where-to-vote notice **and disables both betting buttons (manual + AUTO)** until the vote is attended (manual play advances the clock only through `ExecuteBet`, so the button lock + bet gate together freeze time in manual mode too). Telemetry: `company_governance_trace.csv` (in the world-reset delete list).
- **ND.8b.4 — the `CompanyDetails` scene (D-ND8.16).** BlockExplorer Enroll Mode's **Founded** rows carry "Details →" into `Screens/CompanyDetails/` (Ch. 29 layout) — a row whose open vote awaits the player's ballot paints itself **red** ("⚠ BOARD VOTE PENDING", button relabeled "Vote →") so the pausing company is instantly locatable; pending dividends never highlight (claiming never blocks); `AuctioningCompanyDetails` stays the InAuction-only view and forwards there on resolution. Founding summary + governance readout always; then holding-gated panels — NST: **Board Vote** (band-clamped reserve target; market direction + payout rate on quarterly votes; submitting lifts the pause) + **Quarterly Dividend** claim; PST: **Daily Dividend** claim only; no shares: summary only. Pure display/action surface — the engine never runs from UI.
- **A pending board vote across an app restart (D-ND8.18 × the checkpoint model — 2026-07-21 clarification, expected behavior, NOT a bug).** Casting a ballot (`TryRegisterPlayerVote`) sets `AwaitingPlayerVote = false` and records the ballot **in memory only** — it lifts the pause immediately, but it is not written to disk until the next block is mined (Ch. 24: *a block is the only commit to disk*). This composes with the pause in a way that surprises players, so it is documented here explicitly: because an awaiting-player vote **freezes mining**, no block can ever be mined *past* an open vote — so while a vote is pending it always sits exactly at the committed chain frontier. Two consequences: **(1)** if you vote and then close the app before mining at least one new block, that block never commits your ballot, so on restart the world reverts to the last mined block where the vote is *still open* and your ballot is gone — the vote reappears pending (correct: identical to how the clock, balances, and mempool revert). **(2)** For the same reason, reopening the app with any pending vote drops you **straight into the paused state** — the vote is required immediately, before you can bet, with DiceGame's buttons disabled — there is no runway to "play up to the vote day again," because the vote day already *is* the frontier. To make a ballot stick, keep the simulation running until **at least one block mines** after you vote (autobet, or a manual bet, then let it settle into a block), *then* stop. There is deliberately no special-case commit-on-vote (that would violate the block-only-commit invariant, Ch. 24 Pattern 2); the surprise is purely a UX artifact of a correct rule and is surfaced to the player only via the CompanyDetails red "⚠ BOARD VOTE PENDING" marker + the DiceGame where-to-vote notice.
- **ND.8b.5 — historically-anchored inflow (D-ND8.20/36).** The cast sell-flow's recipient is now drawn ∝ `inflow_weight × expansion step-up × dev multiplier` among introduced companies (`EffectiveInflowWeight`); the roster's five expansion rows are permanent step-ups from their dates. Amounts still track the sender's balance — relative weights over the existing activity curves, no absolute targets (D-14.2 intact).
- **ND.8b.6 — SC provisioning, provisional casino path (D-ND8.24/34).** Once a company's founding vote closes, its reserves converge on the voted SC% each block: an on-chain company→casino BTC send (median network fee only — no desk fee) paired with an SC credit to the company's `ScReserve` at the **clean day-price reference rate**, funded by `CasinoScBalanceService.TryPayCompanyProvisionSc` (Main Balance, auto-loan chunks when short — each draw flows through the `AddLoanRecord` funnel, so the SC Monetary Ledger books it as casino debt automatically, §36.9). Conversion floors: SC deficit ≥ 5% of reserve value AND BTC ≥ max(0.01, 2× fee). v1 converts BTC→SC only (the reverse is the swap desk / future bank companies' job, D-ND8.34/ND.8e). SC dividend claims now pay for real: bots into `NodeFinancialState.PrincipalBalance`, the player into Main Balance — partial when the reserve is short, remainder stays accrued. DEV knobs (D-ND8.25): WorldEconomy's "Company Inflow & Expansion" section (per-company inflow multiplier + weight/expansion readout).

**What still holds:** the ascending-bid ladder, the Saturation Ladder, the 20-day window, "a win is permanent", and D-EB.7's only-casino-players-bid rule are all unchanged; the checkpoint discipline is satisfied by inheritance (all new state lives in `NetworkRoot`'s snapshot — foundings, governance, bot preferences, inflow multipliers — and a founding can only exist well post-genesis). **Deferred:** stock trading (D-ND8.21, held-forever in Basic Mode), SC→BTC reserve rebalancing, the bank companies' takeover of credit (ND.8e), seizure/bust rolls and the other §12.4.6b expansion modifiers.

### 22.13 — Player Dividend Claim History panel (Step 14 ND.8g, 2026-07-21)

A playtest gap surfaced right after §22.12's dividend engine shipped: a player who had genuinely earned and accrued a PST dividend in ArtForz Cluster hadn't noticed it, because nothing durably records what a player has actually been paid over a company's lifetime — only the CURRENT unclaimed balance is visible (`ClaimableByHolder`), and it resets to zero the moment it's claimed. `CompanyDetails` gained a per-company **Dividend Claim History** panel to close this gap.

- **What's logged.** Every successful `TryClaimPlayerCompanyDividends` press that pays something (`paidBtc > 0m || paidSc > 0m`) appends one `CompanyDividendClaimRecord` — claimed-at (game time, the chain tip's own block timestamp, never wall-clock), BTC paid this press, SC paid this press, and that day's BTC/SC price (`BtcMarketDataService.GetEffectivePriceUsd` at claim time; SC is USD-pegged 1:1, so the same figure serves both). Bot auto-claims (`TryAutoClaimBotDividends`) never write here — this is a PLAYER-ONLY history, matching `CompanyDetails`' already player-facing, holding-gated design.
- **Where it lives.** A new `List<CompanyDividendClaimRecord> PlayerClaimHistory` on `CompanyGovernanceState`, capped defensively at 500 (the `PlayerBankAccountService.BankTransferRecord` precedent). No new persisted file, no new checkpoint/pre-genesis-reset/delete-list work: `CompanyGovernanceState` already rides `NetworkRoot`'s own `BlockchainStateSnapshot` for free, the same inheritance `CompanyFounding`/`ClaimableByHolder` themselves rely on (a company's governance state can only exist well post-genesis).
- **What the panel shows.** Three lifetime totals, computed fresh from the record list every refresh:
  1. **Total SC received** — the plain sum of every record's SC leg.
  2. **Total BTC received** — the plain sum of every record's BTC leg, with two DERIVED valuations shown side by side:
     - **Historical BTC/SC payment value** — `Σ (BtcAmount × that record's own day-price)`, a true "what it was worth when you got it" figure, never recomputed.
     - **Current BTC/SC payment value** — the SAME BTC total revalued at TODAY's live price (`_btcMarketDataService.GetEffectivePriceUsd` against `_networkRoot.GetPlayerLatestBlock().Timestamp`, the `AuctioningCompanyDetails`/`BTCWallet` "always live" convention). The gap between the two is an at-a-glance read of how much BTC appreciation/depreciation the player's dividends have seen since being paid.
  Below the totals, the log itself — most-recent-first, capped to the 30 most recently displayed rows (the full history up to 500 stays in state regardless).
- **Placement and refresh.** Lives in `CompanyDetails.RebuildInfo` (after Vote History), inside the SAME scroll the action panels already share — no new `ScrollContainer`, Ch. 29 compliant by construction. Rebuilt on EVERY 1 s refresh (unlike the holding-gated action panels, which only rebuild on a signature change) so a fresh claim appears immediately without needing a vote/holding-state change to trigger it.

See `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §12.5.6.

### 22.14 — The bid-opportunity model: per-slot vs per-block probability (Step 14 ND.10c, 2026-07-23)

This section exists because ND.10b's `AuctioningCompanyDetails` per-bot panel put a probability on screen, and a number on screen invites the question *"a probability of what, exactly?"* — a question with three defensible answers in this system, which is precisely why it needs pinning down. It documents the layers of chance a single bid passes through, the number the panel reports (**per block**, since ND.10c), and what the earlier **per-slot** number meant and why it was abandoned.

**The three independent layers.** A casino-bot bid is not one roll. In each mined block:

1. **The count draw** — how many donation slots this block gets at all: `0` (15%), `1` (70%), `2` (15%) (`DrawCasinoBotDonationCount`, §22.7). Expected value exactly `1.0`; this is the global throttle on auction activity and is deliberately untouched by ND.10c.
2. **The bot draw (roll 1)** — each slot picks one bot uniformly among the **eligible** bots: those holding ≥1 qualifying, affordable pool with a nonzero ladder probability (D-ND10c.1; before ND.10c the draw ran over all four regardless, D-ND6.1). Call the eligible count `B`.
3. **The pool + ladder layer** — the chosen bot rolls **every** qualifying, affordable pool's own ladder probability `r_k` in parallel; the hits are collected and one is picked uniformly (D-ND10c.2). `r_k` is the mode-aware `max(mode rate, stuck escalation)` of §22.10, or a deterministic `1.0` for an unparticipated pool.

#### What "per-slot" meant (ND.10b) and why it was replaced

ND.10b's panel reported layer 3 only — *"given this bot runs its pipeline this block, where does its one action land?"* — deliberately excluding layers 1 and 2 so that a world with a single open auction would degenerate to that pool's raw in-pool rule (its ND.10b Q1 decision). Two artifacts followed:

- **It could not distinguish "unlikely" from "impossible".** Under the pre-ND.10c deterministic pool walk (spread-wide-first, D-ND6.6), a pool the bot never selected got a hard `0%` even while its per-slot ladder label next to it read `[re-bid 94%]`. Both numbers were correct; they simply answered different questions, and the pair read as a contradiction. This is the observation that opened ND.10c.
- **It was not comparable to anything the developer could observe.** Playtest evidence arrives as *"I watched this pool for 30 blocks and nothing happened"* — a per-block quantity. A per-slot number is off by the count draw and by `B`, so it cannot be checked against play without mental arithmetic.

#### The per-block number (ND.10c)

Fix a bot and a pool `k`. Let `r₁ … r_N` be that bot's qualifying, affordable pools' ladder probabilities this block.

> ⚠️ **Step 1's tie-break was replaced at ND.10l (2026-07-28)** — the uniform draw became a **weighted** one, so `q_k` is now a DP over the other hits' total *weight* rather than their *count*. The count form below is kept because it is the clearer statement of the shape, and because the worked example that follows is the historical ND.10c case. See "The weighted tie-break" at the end of this section for the current formula.

**Step 1 — collapse the parallel rolls into a per-pool share (`q_k`).** All `N` pools roll independently; if several hit, one wins uniformly. So `k` wins when it hits *and* survives the tie-break against however many other pools also hit:

```
q_k = r_k · Σ_m  P(H₋ₖ = m) / (m + 1)
```

where `H₋ₖ` is the number of the bot's OTHER pools that hit — a **Poisson-binomial** count (independent trials, different probabilities). Its distribution comes from a standard O(N²) DP: start with `P(H=0) = 1`, then fold in each other pool `j` with `P'(H=h) = P(H=h)·(1−r_j) + P(H=h−1)·r_j`. `N` is small (a bot's qualifying pools rarely exceed a handful), so this is negligible work on a per-block UI refresh.

*Free self-check:* summing over all of the bot's pools must give the probability that it bids **at all** —

```
Σ_k q_k  =  1 − ∏_k (1 − r_k)
```

— an exact identity, cheap to assert against the DP.

**Step 2 — fold in the bot draw (roll 1).** Each slot picks uniformly among the `B` eligible bots:

```
p_k = q_k / B          ← per-slot probability, INCLUDING roll 1
```

**Step 3 — fold in the count draw.** With `p = p_k` per slot and the two slots treated as independent draws against the same pre-block state:

```
P_k = 0.70·p + 0.15·(1 − (1−p)²)  =  0.70p + 0.15(2p − p²)  =  p − 0.15·p²
```

Note this is slightly **less** than `p × E[count] = p × 1.0`: a bot drawn for both slots can bid the same pool twice, but only *one* of those makes it the leading bidder from the panel's point of view — the `−0.15p²` term is exactly that double-count removed. At realistic values the correction is under 4% relative, but it is exact and free, so the panel carries it rather than an approximation.

**Worked example (the BitPaid case that motivated ND.10c).** bot_1, four eligible bots (`B = 4`), three qualifying pools: **BitPaid** `r = 0.02` (tier 3, one own bid, NORMAL mode), **The Silk Market** `r = 0.13`, **DeepBit** `r = 1.00` (unparticipated — always hits).

```
BitPaid    others {0.13, 1.00} → P(H=1)=0.87, P(H=2)=0.13
           q = 0.02 · (0.87/2 + 0.13/3) = 0.02 · 0.478333 =  0.009567   ( 0.96%)
Silk       others {0.02, 1.00} → P(H=1)=0.98, P(H=2)=0.02
           q = 0.13 · (0.98/2 + 0.02/3) = 0.13 · 0.496667 =  0.064567   ( 6.46%)
DeepBit    others {0.02, 0.13} → P(H=0)=0.8526, P(H=1)=0.1448, P(H=2)=0.0026
           q = 1.00 · (0.8526 + 0.1448/2 + 0.0026/3)      =  0.925867   (92.59%)
                                                    Σ q  =  1.000000   ✓ = 1 − (0.98·0.87·0.00)

BitPaid    p = 0.009567 / 4 = 0.002392        → P = p − 0.15p² = 0.002391  →  shown as  0.24%
DeepBit    p = 0.925867 / 4 = 0.231467        → P = p − 0.15p² = 0.223431  →  shown as 22.34%
```

Read against the same pool under the old model: BitPaid showed a flat **`0%`** (never selected by the deterministic walk), while its neighbouring slot label showed **`2%`** (the raw ladder rate, which overstates the real per-block chance by roughly 8×). `0.24%` is the honest figure, and it composes with play directly: over one 20-day auction window (~29.5 blocks at `TargetBlockSeconds = 58,500`) that is `1 − (1 − 0.0024)^29.5 ≈ 6.8%` — a number the developer can falsify by watching. *(That 6.8% is also what makes D-ND10c.3's tier-2/3 escalation necessary: the selection rework alone makes a stuck pool visible, not contested.)*

**Documented approximations.** Two, both deliberate:

- **The two slots are modelled as independent draws against the pre-block state.** In reality slot 2 runs *after* slot 1's bid, so it sees a new leader, shifted tiers, higher required amounts, and an eligible set with the slot-1 donor removed. Modelling that faithfully would mean simulating the whole block; the error is confined to the 15% two-slot case.
- **`B` and every `r_k` are evaluated against the chain tip**, i.e. the state as of the last mined block — which is exactly the state the next block's pipeline will actually read, so this one is a fidelity feature rather than an approximation.

**Display convention.** Two decimals, `CultureInfo.InvariantCulture` (Ch. 1 number-format rule): realistic values live in `0.10%–25%`, and integer rounding collapses most of them to `0%` — the very ambiguity this model exists to remove. A `0%` in the panel now means genuinely impossible (satisfied, self-eviction-guarded, or unaffordable), never merely unlikely. The per-slot ladder label on each bids-list row (`[re-bid NN%]`) is **kept as an integer percent and kept per-slot on purpose**: it answers the mechanic's question ("what does this occupant roll when it looks at this pool?"), while the right-hand panel answers the player's ("how likely is this bot to take the lead here before the next block?"). The two numbers differing is expected, and §22.14 is where that difference is defined.


**The weighted tie-break (ND.10l, 2026-07-28).** ND.10c's uniform draw fixed an ordering problem and quietly created its mirror image. An **unparticipated pool always hits** (`r = 1.0` is a sentinel meaning "a first bid is deterministic and never rolls", D-ND6.5), so every fresh company arriving on the historical curve took an equal share of the slot from whatever else hit — halving, each time, the real chance of exactly the escalated re-bids ND.10c existed to make reachable. ND.10j's `poolRolls` column is what made it visible: at block 964, `bot_2`'s BitInstant roll **hit and lost the coin-flip** to a 0.03 BTC seed bid on a brand-new pool.

The draw is now weighted by how much each pool actually wants the slot:

```
q_k = r_k · Σ_W  P(W₋ₖ = W) · w_k / (w_k + W)
```

where `W₋ₖ` is the **total tie-break weight** of the bot's other pools that hit, and `w_k` is pool `k`'s own weight.

**The weight is not the probability**, and that distinction is the whole subphase. Weighting by the raw `r_k` looks like the obvious reading of "weight by probability" and does the **opposite** of what is wanted: `r = 100` is the largest number in the system, so fresh pools would take *more* of the slot than uniform gave them (a 64% escalated re-bid falling from ½ to 64/164 ≈ 39%). A participated pool weighs its composed ladder probability; an unparticipated pool weighs an explicit `FreshPoolSeedingWeight` (**34**, Fibonacci per D-ND6.4) — a first bid is *certain* but not *urgent*. Against one fresh pool a 64% escalated re-bid now keeps 64/98 ≈ 65% of the tie-break instead of 50%, lifting its true per-block chance from ~30% to ~40%.

It stays a **draw, never a priority**. An absolute rule — "expiring auctions before fresh ones" — would starve seeding completely, and with 40 companies arriving over fifteen in-game years their first bids are how auctions start at all. Under the weighted draw a fresh pool still wins outright on every slot where nothing else hits, which is most of them while few pools are live.

The DP changes shape with it: the count-based Poisson-binomial becomes a 0/1-knapsack convolution over integer weights. Two collapses keep it cheap — a pool with `r ≥ 1` always hits, so it is not a random variable and its weight folds into a constant offset; a pool with `r ≤ 0` never hits and drops out. Only genuinely stochastic pools enter the DP, and a bot holds slots in a handful of pools at most. **The `Σ_k q_k = 1 − ∏_k (1−r_k)` identity is unchanged and still asserted at runtime** — it holds for *any* rule that picks exactly one winner from a non-empty hit set, so the same free self-check now guards the weighted DP too. Verified against brute-force subset enumeration to machine precision, including the degenerate cases (equal weights reproduce the uniform result exactly).

`FreshPoolSeedingWeight` is a **calibration placeholder**, like the ND.10e treasury thresholds: block frequency changes how often pools contest at all, so it should be re-read once the R2 regulator's block pace is verified in play.
**Four kinds of zero (ND.10d, 2026-07-23).** A `0%` in either column can mean four different things, and the first playtest of the per-block panel found the two columns disagreeing because of it: The Silk Market showed `bot_1` at `0%` in the panel while its bids-list slot advertised `[re-bid 100%]`. Both numbers were individually right — the panel enforces the half-spendable affordability cap (D-ND6.8) and the label did not, while the ND.10c stuck escalation ratcheted that label to its 100% ceiling (a lone tier-6 slot, NORMAL base 13%, clamps after 7 stuck blocks). Reconstructing the pool from the chain confirmed the mechanics were correct throughout: the leader was `bot_2` at 337.42 BTC, so the required raise was ≈371 BTC against caps of 264 (bot_1) and 119 (bot_4) — both **priced out**, exactly the economic terminator §22.10 designs for; `bot_3` could afford it but held the pool's smallest slot in a full pool (**self-eviction guard**); `bot_2` was the **leader**. The fix gives all three surfaces one shared exclusion vocabulary (`BotPoolOpportunity.Exclusion` ∈ `satisfied` / `guard` / `priced out`, `null` = biddable): the per-slot label now returns `priced out` when the raise no longer fits the cap, and a `0.00%` panel row prints its reason (`bot_4  0.00%  (priced out)`, via `NetworkRoot.BotPoolExclusionNote`). A real-but-tiny probability prints `<0.01%` instead of rounding into a bare zero. **The escalation deliberately keeps ratcheting while a bot is priced out** — so it bids the instant its mining income clears the bar, reading as a bot that has been waiting for its chance — but that ratcheting number is no longer displayed while it cannot act.

**Bot treasury sustainability (ND.10e, 2026-07-23).** Being priced out is by design, but the bots were reaching it far faster than their mining income could refill them, so four economy constants changed together: the **opening bid became price-anchored** (worth `$0.10`, capped `1 BTC` — so an auction can no longer open for pocket change in the sub-$0.10 era *or* cost a fortune later; judged at each bid's own block timestamp, keeping the replayed ledger deterministic), the **raise band halved to 5–10%**, every bot gained a **BTC reserve guard** (withdraw from all auctions at ≤ 200 BTC, resume at ≥ 300 — the fourth exclusion, `reserve`, which outranks the per-pool rules), and **dividend auto-claims are batched at 10× the network fee**. That last one came out of the audit the change requested: the bots' auto-claims were working correctly (555 claims across all four bots, SC leg included, every claimable drained), but firing the instant the accrual cleared the fee meant the smallest payments netted `0.00039` BTC against a `0.01` fee — **96% of the dividend burned**, and since the fee is paid out of the dividend, straight off the bots' income. All five thresholds are explicit placeholders; the variable-reserve policy that should replace them (BTC price, SC position, mining income, dividend inflow, per-bot personality) is recorded in `PRIVATE_ROADMAP.md` → *Casino-Bot Treasury Policy*, to be re-tuned only once hardware progression (P5) and maturing dividend inflow change the arithmetic.

Full decision log: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §14.4 (D-ND10c.1…7), §14.5 (D-ND10d.1…3) and §14.6 (D-ND10e.1…4).

### 22.15 — The stake border colours: gold / silver / black (Step 14 ND.9b, 2026-07-22; extended to open auctions at ND.10f, 2026-07-23)

**Files**: `CompanyDetails.cs`, `AuctioningCompanyDetails.cs`, `BlockExplorer.cs`, `NetworkRoot.cs` (`GetPlayerProjectedStake`, `NstTopTierCount`, `PlayerAuctionStake`)

One visual vocabulary answers one question — **"what is MY stake in this company?"** — everywhere a company appears, from the moment it opens for bidding to long after it is founded. Three colours, one meaning each:

| Colour | Hex | Founded company (ND.9b) | Open auction (ND.10f) |
|---|---|---|---|
| **Gold** | `#D9A621` | the player holds **NST** — voting shares | the player currently occupies a **top-3 tier** of the tracked pool, so it *would* mint NST if the auction closed at this block |
| **Silver** | `#BFBFC7` | the player holds **PST** — dividend shares, no votes | the player holds only tracked tiers **below** the top 3 — it *would* mint PST |
| **Black** | `#0D0D0D` | the player holds **nothing** in this company | the player holds **no slot** in any of the 10 tracked tiers — it would mint nothing |

The colour therefore never changes meaning across a company's life; only its **certainty** does. On a founded company it is a settled fact read from `CompanyFounding.Holdings`. On an open auction it is a **live projection** — see below.

#### Where it appears

Two surfaces per lifecycle stage, deliberately symmetric:

- **The page frame** — a mouse-transparent `Panel` at child index 0, inset 8 px from the screen edge, `StyleBoxFlat` with a transparent centre so only the coloured frame shows. Used by `CompanyDetails` (founded) and `AuctioningCompanyDetails` (in auction). It sits *behind* the content and never participates in the layout, so it is Ch. 29-safe by construction — it cannot affect a scroll's bounding chain or push a footer into the off-screen band.
- **The list-row title** — in BlockExplorer's Auction / Company Mode, each row's title `Label` is wrapped in a bordered `PanelContainer` (2 px border, content margins, `MouseFilter = Pass` so the wheel still reaches the enclosing scroll). Both the in-auction rows and the founded rows are built by the one shared `BlockExplorer.BuildTitlePanel(label, colour)`, so the two row kinds cannot drift cosmetically.

The page frames additionally carry a caption stating the same thing in words — `"You hold: NST (voting shares)"` on a founded company, `"If it closed now you would mint: …"` on an open auction. Colour alone is never the only channel.

#### The projection rule (ND.10f)

`NetworkRoot.GetPlayerProjectedStake(summary)` is a pure, side-effect-free read in the same family as `GetNonMinerAuctionLedger` / `GetCompanyFounding` — a display call can never perturb auction mechanics. It ranks `summary.TrackedDonations` **descending by BTC principal** (tier 1 = largest, §22.9's tracked-pool ordering), finds the player's best tier via `IsPlayerBidderAddress`, and compares it to `NstTopTierCount`.

Two properties matter:

1. **It cannot drift from the real mint.** It reuses `FoundCompany`'s own two inputs — the identical value-descending ranking and the identical NST-band threshold, `NstTopTierCount`, which was hoisted out of `FoundCompany`'s literal `3` at ND.10f precisely so one constant defines the band for both the forecast and the mint (D-ND8.15 step 4).
2. **Identity is resolved by owned-address set, not by `tx.Sender`.** A bid whose coin selection spent a derived change-address UTXO still colours the row, because `IsPlayerBidderAddress` tests the player's full owned-address set — the §30.9 rule that *an address is a key, not an identity*.

**It is a forecast, not an entitlement (D-ND10f.1).** Nothing is minted until the auction closes, and every later bid re-orders the tracked pool beneath the player. Gold on an open pool means "top-3 *right now*"; one bot raise can turn it silver on the next block. That volatility is the point — the border makes the **top-3 cliff** (the votes/no-votes boundary of D-ND8.15) legible while it can still be contested, and makes being pushed off it a visible loss instead of a surprise at close.

**`PlayerAuctionStake` (`None` / `Pst` / `Nst`, D-ND10f.3)** is the auction-time counterpart of a founded holding's class. Three states are exactly what the three colours need, and keeping it an engine-side enum stops each UI call site from re-deriving `tier <= 3` for itself. It is purely a UI signal — no mechanic reads it.

#### Signals that coexist

A founded row can be **both** bordered and tinted: the tint is about **pending work** (§22.16), the border about **stake**. They are independent and deliberately not merged — one is "what must I do here?", the other "what do I own here?". Likewise, the per-slot re-bid `%` and exclusion labels of §22.14 are untouched by the border: those describe the *bots'* behaviour, the border describes the *player's* position.

Full decision log: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §13 (ND.9b) and §14.7 (D-ND10f.1…3).

### 22.16 — The pending-work tint: red / green / mocha / black (Step 14 ND.10h, 2026-07-23)

**Files**: `BlockExplorer.cs` (`PendingWorkColor`, `ApplyButtonBorder`), `NetworkRoot.cs` (`HasPlayerClaimableDividends`)

The second colour axis on a founded company's row in Auction / Company Mode. Where §22.15's border answers *"what do I own here?"*, the row label + button answer **"what must I do here?"**:

| Board vote pending | Dividends claimable | Colour | Button text |
|---|---|---|---|
| yes | yes | **mocha** `#C08552` | `Vote →` |
| yes | no | **red** `#FF4D4D` | `Vote →` |
| no | yes | **green** `#4DD97A` | `Claim →` |
| no | no | **black border**, no tint | `Details →` |

**Why dividends earned a colour at all.** The red vote tint (ND.8b.3) solves an *urgent* problem — a board vote where the player holds NST **pauses the game** (§22.12), so "which company is blocking me?" must be answerable at a glance. Dividends are the exact opposite: never urgent, never blocking, and therefore easy to forget entirely. That is not hypothetical — ND.8g's claim-history panel exists because a playtest found a genuinely earned, genuinely unclaimed ArtForz Cluster PST balance the player had simply never noticed. ND.8g made a forgotten dividend discoverable *once you open the company*; §22.16 makes it visible without opening anything. The mocha state is the one that earns its keep: it says *"you were coming here for the vote anyway — collect while you're in"*, at exactly the moment the player is already navigating there.

**It is a pure function of current state.** Voting clears the vote flag; claiming clears the claimable; the table is re-evaluated on each refresh. Nothing about what the player did on a previous visit is recorded — no history, no persisted state, no event.

#### Two implementation rules worth keeping

**Mocha is hand-picked, and must not be "corrected" to yellow.** The request was red and green *mixed like pigments*. Averaging them in RGB is additive and yields `#A6C364`, a muddy yellow-green — the opposite result. Pigment mixing is **subtractive**: in CMY, `#FF4D4D → (0, .70, .70)` and `#4DD97A → (.70, .15, .52)`; summed and clamped that is `(.70, .85, 1.0)` → `#4D2600`, a deep brown. Correct hue, but unreadable as a font colour on a dark theme — so the shipped constant keeps the hue and lifts the value to `#C08552`.

**"Claimable" means *payable*, not *non-zero*.** The naive test (`claim.Btc > 0 || claim.Sc > 0`) produces a permanently-green button that pays nothing when pressed, because `TryClaimPlayerCompanyDividends` deducts the network fee **from the claim itself** and so skips a BTC leg at or below the fee — and pays the SC leg only up to the company's own `ScReserve`. `NetworkRoot.HasPlayerClaimableDividends` therefore tests `(Sc > 0 && ScReserve > 0) || Btc > medianFee`, and is the **single source** for every surface that advertises a claim: the row button lights from it, and `CompanyDetails`' "Claimable now:" line reads it too, appending *"— below the network fee; still accruing"* so a non-zero figure beside an un-tinted row button explains itself. This is §22.14's "four kinds of zero" lesson (ND.10d) applied to a different pair of surfaces: **a displayed signal must share its source with the action it advertises.** It is also the same fee-vs-payment arithmetic ND.10e found burning 96% of the bots' smallest auto-claims.

**A `Button` has four stylebox states.** `normal`, `hover`, `pressed`, `focus` — overriding only `normal` makes the border disappear the moment the cursor touches it, which reads as a rendering bug. `ApplyButtonBorder` duplicates the existing stylebox per state so the button keeps its theme background and only gains the frame.

Full decision log: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §14.9 (D-ND10h.1…5).

### 22.17 — Companies are named everywhere the player looks (Step 14 ND.10g, 2026-07-23)

**Files**: `NetworkRoot.cs` (`DescribeNodeForDisplay` / `DescribeNodeForDev` / `GetSendableBotTargets` / `GetIntroducedNonMinerAddresses`), `BTCWallet.cs`, `FoundersWallets.cs`, `CasinoFinances.cs`, `BotsBtcWallets.cs`, `BlockExplorer.cs`

ND.8b.1 gave the 40 auction non-miners real historical company identities, but only the auction and company screens used them. Everywhere else the internal id leaked: the player bid against *The Silk Market*, watched it get founded, claimed its dividends — then opened the BTC wallet to send it money and had to know it was `non_miner_6`. ND.10g closes that, and it is the last structural gap in the Business Migration's fiction.

**Two tiers, on purpose.** Player-facing surfaces show the **company name alone** (`Mt. Gox`). Diagnostic surfaces — BlockExplorer's Network Status, the Node → Address directory, the node selector, `BotsBtcWallets`' holder list — show **both** (`Mt. Gox (non_miner_7)`), because `non_miner_#` is the **join key of every CSV trace in the project** (`casino_bot_bid_trace`, `company_founding_trace`, `company_governance_trace`, `auction_settlement_trace`) and of the `_companyFoundings` / `_companyGovernance` / `_stuckBidderSignatures` dictionaries. Dropping it would mean hand-matching a trace row against the roster CSV during exactly the sort of audit that produced ND.10c/d/e. Traces, logs, persisted JSON and every dictionary key stay on the raw id — they are not UI.

`DescribeNodeForDisplay` / `DescribeNodeForDev` are the only two places the `non_miner_{i}` ↔ `CompanyRoster.Auctionable[i-1]` pairing (D-ND8.37) becomes UI text. Every other node passes through untouched: `player`, `casino`, `bot_1..4` and the founders keep their ids, and the **cast miners already carry human names** (`artforz`, `foundry_usa`, … — `NetworkPopulationScheduler`'s chronological pool), so the rename is scoped precisely to the 40 companies. `DescribeAddress` routes through the display resolver, which is what carries the rename into the auction rows, the tracked-pool bids list and every wallet history panel at once — audited at build time to confirm all its call sites are display-only, per the §30.9 rule that an address is a key and not an identity.

**A company you cannot see yet cannot be sent money.** The four BTC send panels used to list all 40 non-miners unconditionally. Under a raw id that leaked nothing; under real names it would have put *Coinbase* and *Foundry USA* in a 2011 dropdown. They now list only companies already introduced on the historical curve (`GetIntroducedNonMinerAddresses`, using the existing ledger status — no new state, no new date math), which also matches how `ScheduleBotTransactionsAfterBlock` has always picked its automated recipients. This is a small deliberate **behavior** change, not just a rename. All four panels share one `GetSendableBotTargets()` so the naming rule and the filter cannot drift apart; the `IsMinerNode` flag is the discriminator, since it is false **only** for the 40 auction non-miners (cast miners register as miners).

**Two regressions the rename would otherwise have caused, both anticipated and fixed:** `GetNodeStatusLines` now returns `(nodeId, line)` pairs, because BlockExplorer's ⛏ mining-rate decorator used to recover the id by re-parsing the line's own prefix — renaming the prefix would have silently stopped the marker appearing. And BlockExplorer keeps `_selectorNodeIds` parallel to its option items, because the transaction/address/block lookup handlers fed `GetItemText(...)` straight into calls that resolve real nodes by id. **Both are the same mistake in two shapes: recovering data from formatted display text.** When a label's text becomes translatable, prettified, or renamed, every such read breaks silently — carry the id as data.

Full decision log: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §14.8 (D-ND10g.1…3).

---

### 22.18 — An in-memory signal must not read "absent" as "just now" (Step 14 ND.10j, 2026-07-28)

The stuck-bidder escalation (§22.10.1) is deliberately **not persisted**: `_stuckBidderSignatures` is bidding bookkeeping, not world state, and D-ND10e.3 filed a restart wiping it under "harmless and self-correcting", following `_lastMinedByNodeId`. That judgement was right about the *storage* decision and wrong about the *default*.

Its single writer is a per-block sweep. So an absent key does not mean "this bot just became stuck" — it means **"this process has never looked at this pair"**, which is true of every pair after a restart, and of *every* pair before a session's first mined block. Reading absent as "now" gave every stuck bidder `base × 1`. A live BitInstant audit caught it four in-game days from that auction's close: bot_1, a lone tier-5 occupant stuck since block 957, showed and rolled the flat urgency **13%** instead of its true **64%**. With roughly two blocks left in the window there was no way back, and the auction would have resolved on its leader unopposed — the exact stagnation the escalation exists to prevent. The reset is harmless only where nothing is *counting* the blocks; the escalation counts them, and it is the mechanism of last resort in a closing window.

**The rule this yields:** when a value is derived from "how long has X been true", an absent record is a *question*, not an answer. Either persist it, or **derive it** — never let it silently mean zero.

Here the derivation is free, because the chain already knows. A tracked donation ranked *below* a bot cannot have changed that bot's tier, so the bot has held its tier since **the most recent donation ranked at or above it** — its own slot (the block it took the tier) or a later raise that pushed it down. `TrackedDonation.TimestampMs` is a confirming block's timestamp, so it maps straight back to a block index. A *first sight* seeds from that; a key that is **present** with a changed signature still stamps the current block, because that is an exact edge this process observed and must outrank an estimate. One case stays out of reach — an eviction of the bot's *other* slot, which drops it to single-slot without disturbing anything above it, is invisible once the evicted donation is gone. That is the case that forced the in-memory signal to exist in the first place (ND.10a); it is caught exactly while the process runs, and pre-restart it merely over-estimates a long-standing lone occupant, which is much closer to the truth than resetting it to zero.

**The same cold start, one axis over.** `_botsRestingOnReserve` (ND.10e) is written by the same sweep, so before the first block a resting bot advertised a percentage — the §39.16 rule-6 violation ND.10d closed for "priced out", reappearing through a different door. The hysteresis now lives in one predicate that applies it to the **live balance** rather than reading the set, and the sweep is that predicate's write-back. **Generalising: any in-memory cache a per-block sweep owns has a window at process start where it is empty and lying. If a reader can predict the sweep cheaply, it must.**

**And the declines had to become readable.** ND.6b's premise is that declines are the calibration signal, but `rolledProbabilityPercent` was written only after a pick, so every `roll-declined` row logged a bare `0` — which is why this defect needed a chain replay to find rather than a trace read. The new `poolRolls` column records what *every* biddable pool rolled and which hit, making both the escalation and the tie-break observable. It immediately exposed a second defect: an unparticipated pool always hits (`r = 1.0`), so each newly introduced company halved an escalated re-bid's chance that block. That became **ND.10l**, which replaced the uniform tie-break with a weighted one — see §22.14's "The weighted tie-break". Worth noting how it was found: the telemetry added to explain *one* defect surfaced a second, unrelated one within a single block's rows. **Instrumenting a mechanism tends to be worth more than the bug that prompted it.**

Display-only for the reserve label, behavioral for the escalation; **no persisted field, no `WorldFormatVersion` bump, no wipe** — chosen so a running playtest survives the fix. Full decision log and the deferred tie-break options: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §14.11 (D-ND10j.1…4).

---

### 22.19 — A rule written for two parties has a same-party case (Step 14 ND.10k, 2026-07-28)

ND.8d.6 stopped a leader from bidding against itself. D-ND4b.11 decided how two bidders racing in the **same block** resolve: they are judged against the same starting leader, never against each other in sequence. Both are correct. Put together they left a hole neither owner would have looked for — because the self-raise test reads the leader *as of the start of the group*, **two bids from the same party in one block are both "not the leader"**, and both count.

That is how one participant came to hold tiers **1 and 2** of BitInstant's pool: a 10 BTC bid sat unconfirmed in the mempool (invisible to the ledger, which is a pure chain replay — Pattern 2), the player sent another believing the first had failed, and one block confirmed both.

It matters because the tracked pool is not just a leaderboard — it is the **stock mint**. Two slots for the same total BTC means two entries in the 5.2%-halving slot-bonus ladder (D-ND8.15) and one fewer NST seat for anybody else, so **splitting a bid strictly beats making it once**. Bots can reach it too: D-ND6.9's affordability cascade deliberately leaves a declining bot un-marked, so one bot can be drawn twice in a block.

**The rule:** within a block, a donor participates with its **highest** bid only; the rest are ignored exactly as a leader self-raise is — no lead, no window reset, no slot, no stock, and (D-ND10k.2) **no refund**. Highest rather than first keeps it consistent with D-ND4b.11's own same-block resolution and means a small accidental send cannot knock out a large deliberate one. The refund was considered and declined: D-ND8d.7 refunds a post-close bid because it never had a chance to participate, whereas an ignored same-block bid is a duplicate of one that *did*.

**Two general lessons.** First: **when two rules each own part of a state machine, the gap is the case neither one names.** ND.8d.6 says "not the leader"; D-ND4b.11 says "same starting leader"; nobody wrote "and the same donor twice". Worth asking, of any pair of interacting rules, what the *conjunction* permits — the answer is rarely in either comment.

Second, the UX half: **the mempool is a state the player cannot see, and the game never said so.** The ledger, every panel and every warning are chain-derived, so an unconfirmed bid exists nowhere on screen — which made re-sending the rational move. The fix is a fourth non-blocking wallet warning that reads `PendingTransactions` **directly** rather than the ledger, precisely because the ledger structurally cannot see it. Generalising: where a system's read model is a replay, any pending-but-uncommitted action needs its own explicit surface, or the player will act as though it never happened.

The ledger holds no persisted state, so the rule applies to history already on chain (consistent with D-ND4b.12 — only a *win* is permanent). Full decision log: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §14.12 (D-ND10k.1…3).

---

## Chapter 23 — Scheduled Bot Transactions (BTC Recirculation)

**Files**: `NetworkRoot.cs` (`ScheduleBotTransactionsAfterBlock`, `FirstBlockHeightMinedBy`), `BlockchainService.cs` (no-self-send guard)
**Status**: Implemented (merged from the `scheduled-bot-transactions` branch).
**Plan**: `AIHelperFiles/scheduled-bot-transactions-plan.md`

### The Short Version (for everyone)

Miner bots don't just hoard the BTC they mine — they periodically **send a slice of it to non-miner "holder" bots**. This creates visible BTC circulation in the Block Explorer, and it *is* the donation mechanism that drives the referral auction (Chapter 22). Left alone, the holder bots' balances only grow, which makes them interesting accumulation targets.

### 23.1 — When it runs

After every mined block, `NetworkRoot.HandleMinedBlock` calls `ScheduleBotTransactionsAfterBlock(block)`. It is **skipped during the historical bootstrap** (the `_bulkMining` flag is set then), so recirculation only happens in the **live era** (after the 21 Mar player start), once real mining is underway.

### 23.2 — Per-bot warmup (so a bot has something to circulate)

A miner bot only starts donating once **at least `CirculationWarmupBlocks` (5) blocks have passed since its *own* first mined block** (`FirstBlockHeightMinedBy`). This is measured **per bot**, not as an absolute chain height — which is what makes it work for bots that are introduced gradually rather than all at block 1. A bot that has never mined is skipped entirely.

### 23.3 — What each eligible bot does, per block

For each miner bot past its warmup:

1. Check it has at least `MinBotSpendableBalanceBtc` (1.0) spendable.
2. Roll `BotSendProbabilityPerBlock` (≈50%) — so on average ~half the eligible miners send each block.
3. Choose a send amount = a random **10–40%** of spendable.
4. Choose a **fee** = random **0.1–1.0 BTC** (Step 4b.2 — the fee is collected by whoever mines the block that includes this transaction).
5. Ensure `amount + fee ≤ spendable`.
6. Pick a recipient **only from non-miners currently in an open auction window** (`InAuctionNonMinerAddresses(block.Timestamp)` — see Chapter 22). Never send to self.
7. Sign + broadcast the transaction; it confirms when included in a future block.

### 23.4 — Safety rails

- **No self-send (two layers):** the scheduler skips a recipient equal to the sender, and `BlockchainService.AddTransactionToPendingTransactions` rejects **any** transaction where `Sender == Recipient` (the coinbase sender `00` is never a real address, so coinbases are unaffected).
- **Recipients are non-miners only**, and only those still in auction — so circulation stays contained and focused on the live referral competition.

### 23.5 — Where to see it

- **Block Explorer block/transaction lookup:** bot→non-miner transfers appear with their fee; the block's coinbase is `50 + collected fees`.
- **Block Explorer "Enroll Mode":** the donation race per non-miner (leading donor, days left) — Chapter 22.

### 23.6 — Roles (Basic Mode)

| Node | Sends (auto) | Receives (auto) |
|---|---|---|
| `bot_1`…`bot_4` (miners) | **Yes** (this system) | No |
| `non_miner_*` (holders) | No | **Yes** (only while in auction) |
| `player`, `casino`, founders | No | No |

The player participates manually (donating from BTCWallet) to compete in the referral auction; the casino and founders are excluded from automatic recirculation.

---

## Chapter 24 — Background Simulation (the game keeps running across scenes)

**Files**: `Scripts/Services/SimulationService.cs` (new autoload), `Screens/DiceGame/DiceGame.cs` (now a view/controller), `Screens/BlockExplorer/BlockExplorer.cs` (live auto-refresh + mining indicator), `project.godot` (autoload registration)
**Status**: Implemented and user-tested (branch `background-simulation`).
**Plan**: `AIHelperFiles/background-simulation-plan.md`

### The Short Version (for everyone)

Before this change, the **entire** autobet + mining loop lived inside the DiceGame scene. The moment you navigated away (to the Block Explorer, etc.), Godot freed DiceGame, the loop stopped, and on return it rebuilt a fresh session and reloaded the clock — so the world looked **frozen** and even **rewound**. Now the loop lives in a persistent **autoload** (`SimulationService`) that survives scene changes: while a player autobet is running, bets fire, bots bet, blocks are mined, time advances and balances change **in every scene**. DiceGame became a thin "view + controls" layer on top of it.

### 24.1 — Why an autoload was the only fix

A Godot scene node dies with its scene (`ChangeSceneToFile` frees it). Anything that must keep ticking regardless of the visible screen has to live somewhere that *isn't* tied to a scene — in Godot, that's an **autoload** (a node parented under `/root` for the whole app lifetime). So `SimulationService` is registered in `project.godot` alongside the other six services and owns the running autobet in its own `_Process`.

### 24.2 — The single-source-of-truth rule (the key design decision)

The dangerous-but-tempting approach is to hand DiceGame's live `Wallet`/session to the service. That **crashes**: a `Wallet`'s C# events (`BalanceDeltaChanged`) and the session's `OnStopped` are wired to DiceGame, so the next background bet after DiceGame is freed would invoke a disposed node.

The rule that avoids this: **`BankrollStateService` is the single source of truth for the player's bankroll.**

- `SimulationService` builds its **own** wallet/session, **seeded from `BankrollStateService`** at start, and **writes the resolved balance back** to `BankrollStateService` after every settled bet.
- The service's wallet has **no** subscriptions to any scene, so freeing a scene can never crash it.
- DiceGame and the `StatusBar` simply **display** from `BankrollStateService`. On stop / re-entry, DiceGame re-seeds its own display wallet from it (current value → no rewind).

### 24.3 — DiceGame as a view/controller

DiceGame no longer runs the player loop. Instead:

- **Start autobet** → builds a `PlayerAutobetConfig` (chance, high/low, bets/sec, number of bets, active node, stop-on-block, **auto-recharge**, strategy) and calls `SimulationService.StartPlayerAutobet(config)`; sets `_autobetDelegated = true`.
- **Stop autobet** → `SimulationService.Stop()`.
- While delegated, DiceGame's own `TickAutoBet` early-returns; it just reflects live state.
- Two Godot signals from the service drive the UI: `BetSettled` (per player bet) and `AutobetStopped` (the run ended on its own). Godot auto-disconnects these when DiceGame is freed; DiceGame also unsubscribes explicitly in `_ExitTree`.
- **Re-entry**: if the service `IsRunning` when DiceGame loads, `BindToRunningBackgroundAutobet()` binds the UI to it (no new session, no rewind). If it stopped while you were away, a consumable `StopNoticePending` flag lets DiceGame show `Auto stopped: <reason>` on return.

### 24.4 — Bots live in the service too (Phase 2)

`BotConfig`/`BotRunner`, `StartBots`/`StopBots`, `TickBots`, `ExecuteBotBet`, `RunBotManualBurst`, and the bot recharge live in `SimulationService`. DiceGame keeps only the **per-node strategy UI** (`_nodeStrategies`) and hands the service snapshots via `BuildBotConfigs()`.

- During a background autobet, `TickBots` runs in `_Process` so bots keep mining/circulating in every scene.
- **Manual** bets (DiceGame-only) call `RunBotManualBurst(configs)` — a one-shot burst on temporary runners, so manual betting still advances the bots.

### 24.5 — Auto-recharge: player and bots use the *same* post-stop pattern

This is the subtle part, and it caused a real bug worth remembering.

`BaseBetSession.ApplyStopConditions()` runs at the end of **every** `ExecuteNext` and **self-stops the session with `InsufficientBalance` the instant the next progression bet exceeds the bankroll** — *inside* `ExecuteNext`. So a bet-loop's own "can I afford the next bet?" check at the *top* never sees it; the session is already stopped, with leftover bankroll (e.g. a martingale bot stopping at 60.16 SC).

The correct place to recharge is therefore **after** the session stops:

- **Player**: `SimulationService._Process` detects `!_session.IsRunning`; if the reason is `InsufficientBalance` and auto-recharge is on, `TryPlayerAutoRechargeAndRestart()` transfers from Main Balance to Bankroll (`BankrollProgramService.TryTransferBalanceToBankroll`), syncs `BankrollStateService`, and **restarts the progression from base bet**.
- **Bots**: `TickBots` does the mirror — on a stopped bot with `InsufficientBalance`, `TryRechargeAndRestartBot()` tops up the bot's **own** main balance (`NodeFinancialState.PrincipalBalance`, repeatedly if one 100 SC top-up can't cover the base bet) and `RestartBotSessionFromBase()`. Only if the top-up can't be afforded does the runner get removed.

**Restarting from base bet matters**: a single top-up rarely covers a *grown* martingale bet, so without the reset the bot would re-stop immediately. Resetting to base makes the next bet affordable, exactly as the player behaves.

### 24.6 — Watching it live in the Block Explorer

The Block Explorer got a 1-second `_Process` auto-refresh so the background sim is visible without clicking Refresh. Its **Network Status** lines also show **which nodes are actively mining and how fast**: `SimulationService.GetActiveMiningRates()` returns nodeId → bets/sec for the player plus each running bot, and `BuildNodeStatusLinesWithMiningRates()` appends `⛏ <rate>/s` to the matching line.

> **Scope note:** the Block Explorer is a **BTC** view — its per-node "balance" is the on-chain mining balance. Casino/SC information is intentionally **not** surfaced there (except the player's StatusBar). Watching a bot's *SC bankroll* change live belongs in DiceGame; that read-only "Observe node" panel is a planned future slice (see §9 of the plan).

### 24.7 — Edge cases & decisions

- **Node switch during autobet**: selecting an active node calls `LoadActiveNodeFinancialState`, which *rewrites* the shared `BankrollStateService` / `PrincipalBalanceService` (it's a "play as this node" control). That would corrupt a running player autobet, so the **active-node selector is locked** while delegated (`SetActiveNodeSelectorLocked`), with a guard message "Stop the autobet to change the active node."
- **Stop while away** → silent; the reason is shown on return (banner).
- **App restart** → starts **stopped**: nothing persists/restores `IsRunning`; autobet is something the player actively starts.
- **Window unfocused/minimized** → keeps running.
- **Clock ownership**: only `SimulationService` drives `CalendarTimeService.IsRunning` / `SpeedMultiplier` / `IsAutobetActive` while autobet is active, so there is never a second owner of time.

### 24.8 — Time/balance persistence: the "block = the only commit" model (re-entry and restart)

Three related clock/balance bugs were fixed together; their resolution defines exactly how time and money persist.

**The model in one line:** *within* a session the live in-memory state (clock + balances, held by the autoloads and the **static** `NetworkRoot`) is authoritative and survives scene changes; **a mined block is the only thing that commits state to disk**, so an app **restart** reverts every participant to the last mined block. Mining a block is the commit that makes progress durable.

**Bug 1 — clock rewound on re-entry while autobet was *running*.** `DiceGame._Ready()` calls `CalendarTimeService.EnsureGameEpochInitialized()`, which reloads the clock from `calendar_state.json`. The background sim advances time across scenes *without* persisting it every tick, so that reload snapped the clock back to the last-persisted instant — and the lost interval grew the longer the player stayed away. **Fix:** skip the reload when `SimulationService.IsRunning` (the running in-memory clock is authoritative). `Screens/DiceGame/DiceGame.cs`, in `_Ready()` around the `EnsureGameEpochInitialized()` call.

**Bug 2 — clock rewound on re-entry while autobet was *stopped*.** `RestoreLegacyCheckpointIfNeeded()` runs on every `_Ready()` and (when no sim is running) restores the clock + history to the last block's checkpoint — behaviour intended **only** for resuming a fresh app start. It re-ran on every DiceGame entry, snapping the clock back to the last block. **Fix:** a process-`static` guard (`_checkpointRestoreSpentThisSession`) marks the one-shot restore as spent on the **first** DiceGame load — *before* the `HasCheckpoint()` early-return, so a brand-new game (no checkpoint on first load, one captured moments later by `CaptureBlockCheckpointIfMissing`) does not rewind on its second entry. The flag resets only on a real app restart (new process).

**Bug 3 — restart *persisted* between-block advances instead of reverting.** Two things defeated the checkpoint on restart: (a) `NetworkRoot` wrote every participant's `NodeFinancialState` to `blockchain/state.json` on **every scene exit** (`SaveActiveNodeFinancialState(true)`), and `LoadActiveNodeFinancialState()` then re-applied those *advanced* values over the checkpoint that the autoloads had just restored; (b) the clock revert lived only in DiceGame, so whether it ran depended on opening DiceGame first. **Fix (chosen approach: "block = the only commit to disk"):**

- The between-block **navigation / node-switch** saves now use `SaveActiveNodeFinancialState(false)`. The static `NetworkRoot` keeps the advance in memory (so scene changes preserve it), but **nothing reaches disk**. Financial state is written to disk **only at block-mining**: player via `SimulationService.CaptureCheckpoint → PersistFinancialState(true)` (autobet) and `DiceGame.CaptureBlockCheckpoint` (manual); bots via `HandleMinedBlock → PersistStateToDisk`. The only remaining `persist:true` financial writes are those two commit paths.
- The clock revert moved **up to the autoload** `BlockSessionCheckpointService.ApplyCheckpointToServices()` (which already reset balances on startup). It now also restores the clock **and the present frontier** (`_gamePresent`, via `PersistCurrentTime()`) to the checkpoint's `CalendarLocalTicks`. Because this autoload loads *after* `CalendarTimeService`, the revert applies at startup regardless of which scene the app opens into (MainMenu shows the reverted time immediately).

**Net effect (T1 summary).** Play and navigate freely — everything advances and survives scene changes. Close the app *without mining a block* → on reopen the whole world reverts to the last mined block: the clock returns to that block's time (Satoshi's bootstrap tip on a fresh save), every participant's balance/bankroll returns to its last-block (initial) value, and any pending transactions not yet in a block are discarded. **A mined block is the only thing that commits state to disk** — `PersistStateToDisk()` runs only at block-mining (`HandleMinedBlock`), baseline node creation, and startup; nothing between blocks (chain, mempool, or SC balances) is persisted.

**Former edge — now closed (T1): nothing persists between blocks.** `NetworkRoot.PersistStateToDisk()` used to also fire on a BTC-transaction send / consensus run, flushing the live in-memory state mid-session. That was inconsistent: it persisted the *pending transaction* (which carries its own `Timestamp`) while the rest of the world would still revert to the last block on restart. The fix removes those between-block writes entirely — `CreateAndBroadcastTransaction`, `CreateAndBroadcastTransactionToAddress`, and `RunConsensus` now only mutate the **in-memory** chain/mempool. `PersistStateToDisk()` is called **only** at block-mining (`HandleMinedBlock`), baseline node creation, and startup. So a tx broadcast between blocks lives in the mempool and becomes durable when the next block is mined; if the app closes first, the whole world — clock, balances **and** the un-mined pending transactions — reverts to the last mined block. A block is the only commit. See `Documentation/PRIVATE_ROADMAP.md` §8 T1.

### 24.9 — Closing the remaining "block = the only commit" gaps, incl. pre-genesis (2026-07-01)

§24.8 covered restarts **after** the player's first real block. Testing the BankrollProgrammer UI plan (`AIHelperFiles/player-and-casino-bankroll-programmer-plan.md`, OQ-BP.4–OQ-BP.9) surfaced five more leaks — some post-first-block, some in the **pre-genesis** window (no player/bot/founder block has ever been mined; only the historical bootstrap has run).

**Bug 4 — a BankrollProgrammer transfer reverted the instant DiceGame was re-entered.** `NetworkRoot.SharedNodesById` is `static` and outlives `DiceGame`'s per-`_Ready()` `NetworkRoot` instance. `DiceGame.LoadActiveNodeFinancialState()` applied the player's *cached* `NodeFinancialState` (frozen at the moment DiceGame was left, via `SaveActiveNodeFinancialState(false)`) back onto `PrincipalBalanceService`/`BankrollStateService`/`BankrollProgramService` on every `_Ready()` — undoing anything changed in the meantime (e.g. in `BankrollProgrammer`). **Fix:** `LoadActiveNodeFinancialState()` now skips applying the cached snapshot when `IsPlayerActive()` — those three services are already self-persisting and are the actual source of truth for the player (see the `SimulationService` header comment); `NodeFinancialState` now only round-trips for bot nodes, its real use case (the Active Node Selector).

**Bug 5 — same revert on a genuine app restart, before placing any bet.** `DiceGame.ApplyRealtimeBootstrapFromLoadedHistory()` (guarded by the `_bootstrapAppliedThisSession` static flag from §24.8 Bug 2's era) still runs once per **process** — the flag resets on a real restart, so it fires again on the first `_Ready()`. It called `GetLatestKnownBalance()`, which scans `bet_history.jsonl` — a log that records every bet regardless of whether a block was later mined — and overwrote the already-correct, checkpoint-reverted balance with that stale value. **Fix:** the method no longer touches the balance at all; `BlockSessionCheckpointService.ApplyCheckpointToServices()` (autoload boot, before any scene loads) is the sole source of truth for the balance on a cold start.

**Bug 6 — `BankrollProgramService` (dose + transfer records) and "General P/L" never reverted.** Two gaps: (a) `ApplyCheckpointToServices()` restored `BankrollStateService`/`PrincipalBalanceService`/`CasinoScBalanceService` but never `BankrollProgramService` — its dose/records only got restored via the fragile, DiceGame-scoped `RestoreLegacyCheckpointIfNeeded()`, which is skipped whenever `NetworkRoot.HasAnyNodeFinancialState()` is true (i.e. on essentially every real restart after the first). (b) `ApplyRealtimeBootstrapFromLoadedHistory()` (which builds the "General P/L" stats panel from `GetLoadedHistoryStats()`) ran **before** `RestoreLegacyCheckpointIfNeeded()` (which rolls bet history back to the checkpoint boundary via `RollbackHistoryToUtc`), so the stats reflected un-rolled-back history. **Fix:** `ApplyCheckpointToServices()` now also calls `BankrollProgramService.ReplaceState(...)`, matching the other three services; the stats-refresh call was reordered to run immediately after the history rollback.

**Bug 7 — a "baseline" checkpoint baked the startup recharge in forever, so restart never reached true pre-genesis state.** `DiceGame.CaptureBlockCheckpointIfMissing()` captured a checkpoint on the very first `_Ready()` whenever none existed yet — guarded only by "no checkpoint exists", not "a real block was mined" — so `EnsureInitialBankrollFunded()`'s `startup_default` 100 SC recharge got folded into a permanent baseline immediately, and every restart before the player's first real block showed 39,900/100 instead of the true 40,000/0. **Fix (canonical, see also the Canonical Decisions table in `CLAUDE.md`):** removed `CaptureBlockCheckpointIfMissing()` entirely — a checkpoint is now captured **only** by a real block-mined event (`DiceGame.CaptureBlockCheckpoint()`, `SimulationService.CaptureCheckpoint()`), never by merely opening the app. `BlockSessionCheckpointService._Ready()` now calls `ResetToPreGenesisDefaults()` whenever `HasCheckpoint()` is false: Main Balance → 40,000.00, Bankroll → 0.00, dose → `DefaultAutoRechargeAmount`, transfer records → cleared, on **every** boot — discarding whatever those services' own eagerly-self-persisted JSON files accumulated. The dose is deliberately included in this reset (not preserved as a "sticky preference") — a dose configured in `BankrollProgrammer` before ever mining a block only survives a restart once a real block commits it; `DiceGame.EnsureInitialBankrollFunded()` now reads `BankrollProgramService.AutoRechargeAmount` (not a hardcoded constant) so, once committed, the startup recharge on a genuinely fresh game correctly uses that dose.

**Bug 8 — dead blockchain time, and the clock/bet-history not resetting pre-genesis either.** `HistoricalBootstrapService.Run()` picked an independently-random landing instant within 21 Mar 2009 and mined blocks only until the timestamp *would* reach it (the crossing block itself was never mined) — leaving up to a full jittered block interval (~11–21h) of dead time between the last historical block and the player's actual start. Separately, `CalendarTimeService`/`UserStatsService` self-persist on every bet (not just a mined block), so `ResetToPreGenesisDefaults()` (Bug 7) also needed to revert them — but had nothing to revert *to*, since the bootstrap's landing instant is a one-time, in-memory computation (`HistoricalBootstrapService.LandingLocalDateTime`) never re-derivable on a later restart. **Fix:** rewrote the bootstrap loop to mine the block that *crosses* into 21 Mar too (tracking it as `lastMinedTs`) instead of stopping just short of it. Added `NetworkRoot.GetPlayerLatestBlockTimestampMsStatic()` (mirrors the existing `GetPlayerChainLengthStatic()` static-surface pattern) so the player-start instant is always re-derivable from the **chain tip** — before any real block, the tip *is* the historical bootstrap's last block — with no separate persistence needed. `ResetToPreGenesisDefaults()` now also resets `CalendarTimeService` to that instant and calls `UserStatsService.RollbackHistoryToUtc()` on every boot with no checkpoint yet.

**Canonical rule (added 2026-07-01): in-game calendar time always exactly equals the timestamp of the block that most recently defines the checkpointed world — never offset by even one second.** Verified invariant: every checkpoint capture (`DiceGame.CaptureBlockCheckpoint()`, `SimulationService.CaptureCheckpoint()`) reads `CalendarTimeService.CurrentLocalDateTime` **synchronously, immediately after** mining, with no clock advance in between (the clock only ticks via `_Process(delta)`; these are plain synchronous calls) — so a checkpoint's saved calendar time is always bit-for-bit equal to its triggering block's `Timestamp`. The player's very first instant (right after the historical bootstrap, before any real block) follows the same rule for consistency: `HistoricalBootstrapService.LandingLocalDateTime` and `BlockSessionCheckpointService.ResetToPreGenesisDefaults()`'s recomputed clock are both `DateTimeOffset.FromUnixTimeMilliseconds(tipMs)` with **no** `AddSeconds(1)`. See `CLAUDE.md`'s Canonical Decisions table for the one-line version of this rule.

**Net effect.** Pre-genesis (no player/bot/founder block ever mined), the game presents a true first-launch state on **every** restart: Main Balance 40,000.00 SC, Bankroll 0.00 SC, dose 100.00 SC (default), no transfer records, "General P/L" 0.00 SC, and the calendar exactly at the historical bootstrap's last block instant — regardless of how much was played, recharged, or configured in the interim. The player can freely experiment with a custom auto-recharge dose in `BankrollProgrammer` before ever mining a block: it "sticks" for that session's `EnsureInitialBankrollFunded()`, but only survives a restart once a real mined block commits it via the ordinary §24.8 checkpoint model.

### 24.10 — Audit: game time vs. real wall-clock (2026-07-01)

While revising the casino's loan panel (`AIHelperFiles/player-and-casino-bankroll-programmer-plan.md` §1.6), a stale bullet — "dates in the scene are wall-clock, must use `CalendarTimeService`" — prompted the question: does this violation still exist anywhere already *shipped*, not just in the not-yet-built loan panel? A full audit of every `DateTime.Now`/`DateTime.UtcNow` call site under `Scripts/Services/` and `Screens/` found it **already violated in code implemented earlier in this same plan** (Phases BP.2/BP.4) and in pre-existing player-facing code.

**The highest-impact one: manual bets were timestamped with real wall-clock time.** `DiceGame._Ready()` constructed its `BetService` with `() => DateTime.UtcNow` as the timestamp provider — every **manual** bet's `BetTransactionEvent.Timestamp` (and therefore its `BetRecord.TimestampUtc`) was the real system clock, not the in-game 2009 clock. This is the timestamp `UserStatsService.RollbackHistoryToUtc()`/`GetLoadedHistoryStats()` compare against the game-time checkpoint boundary in §24.9's pre-genesis reset — a real-vs-game mismatch here would have silently undermined the history-rollback fixes (Bugs 5/6/8 above) for any session that included manual bets, since "keep records with timestamp ≤ the 2009 checkpoint boundary" would never match a 2026-dated record. Autobet's own `BetService` (`SimulationService`) already correctly used `() => _calendar?.CurrentUtcDateTime ?? DateTime.UtcNow` — only the manual path was wrong. **Fix:** `DiceGame`'s `BetService` now uses the same game-time-first pattern.

**Also fixed, same class of bug:**
- `BankrollProgramService.AddRecord()` (every `TransferRecord`, including `manual_recharge`/`auto_recharge`) and its three `CasinoClientLedgerService.Register*()` call sites — all used `DateTime.UtcNow`. `BankrollProgramService` gained a `CalendarTimeService` reference and a `GameUtcNow()` helper.
- `BankrollProgrammer.cs`'s day/week/month auto-recharge-counter query passed `DateTime.UtcNow` to `GetAutoRechargeCounts()` — now passes game time (the scene gained its own `CalendarTimeService` reference).
- `DiceGame`'s two `RegisterDeposit()` call sites (`TryProgrammedBankrollTransfer` — startup/auto-recharge — and `OnDepositPopupDepositConfirmed`) both built a `timestampUtc` from `DateTime.UtcNow`.
- `SimulationService`'s bot-recharge transfer record and player auto-recharge deposit (`TryRechargeAndRestartBot`/`TryPlayerAutoRechargeAndRestart`) both used `DateTime.UtcNow` despite the service already holding a `_calendar` reference used correctly everywhere else in the same file.
- `CasinoClientLedgerService`'s very first "initial deposit" entry (`RegisterInitialDeposit("player", 40000m, DateTime.UtcNow, ...)` in `_Ready()`) — gained its own `CalendarTimeService` reference (autoload order places it after `CalendarTimeService`, so the real game epoch is already established by then).

**Removed as dead/broken code:** `UserStatsService.ApplyClockJumpToFarthestFutureIfAny()`. It ran inside `UserStatsService._Ready()` — autoload order #1, **before** `CalendarTimeService._Ready()` (order #2) establishes the real game epoch — so any `SetLocalDateTime()` call it made was immediately overwritten moments later; a pure no-op in practice. Its *read* side was also broken under the current architecture: it compared the latest recorded bet's timestamp against real wall-clock `DateTime.Now`, intending to "jump the clock forward" if bet history was ahead — but with the game permanently anchored in 2009 (historical bootstrap) and real "now" always in the present, `latestLocal (2009) > nowLocal (2026+)` can never hold, so the one conditional branch inside it could never fire in the intended direction. A vestigial leftover from an earlier design era, before the historical bootstrap fixed the game clock to 2009; deleted along with its now-unused `_calendarTimeService` field.

**What legitimately stays real wall-clock** (verified case by case, not blanket-exempted): `BlockSessionCheckpointService.CapturedAtUtc` and each service's own `UpdatedAtUtc` snapshot field — pure JSON file-bookkeeping metadata, never read back into any displayed value, confirmed via a repo-wide grep that no `Screens/` file references either field. `UserStatsService`'s `_lastStatsEmitUtc` — the 250ms `StatsChanged` UI-throttle timer (a *real-time* UI-responsiveness concern, unrelated to game-world state). `DiceGame`'s `_autoBetVirtualTimestampUtc` and neighboring fields — intentionally measure real-world bets-per-second throughput for the APS rate display, not game time. `WordlistBootstrapper.GeneratedAt` — a one-time file-generation timestamp. `CalendarTimeService`'s own field initializers — the bootstrap fallback value *before* the real game epoch is established; by definition cannot use itself.

**Canonical rule (added 2026-07-01, `CLAUDE.md` Important Pattern 2): every event timestamp that is persisted, displayed, or compared against a checkpoint boundary must come from `CalendarTimeService`, never `DateTime.Now`/`DateTime.UtcNow` directly.** The only legitimate exception is internal DEV/file bookkeeping metadata a player never sees. When adding a new timestamped record, ask "is this game-world state, or pure DEV telemetry?" — if the player could ever see it, it's game time.

### 24.11 — A subtler timestamp bug: exact-boundary collisions (2026-07-01)

§24.10's fix made bet timestamps correctly *game time* — but a follow-up regression test (`AIHelperFiles/player-and-casino-bankroll-programmer-plan.md` OQ-BP.11) found a second, subtler bug hiding behind the first one: "General P/L" kept showing a stale value after playing and restarting without mining a block, but **only when the session ended in a net loss, never a net profit**. The win/loss asymmetry turned out to be a red herring — a losing session happened to be how the user's repro surfaced it, not the actual trigger.

**Root cause: a bet's timestamp can exactly equal the reset/checkpoint boundary it should be measured against.** `DiceGame.OnManualBetFromPanel()` reads `burstBaseUtc = CalendarTimeService.CurrentUtcDateTime` *before* its bet loop, uses it unshifted as the first bet's timestamp, and only calls `AdvanceClockForBet()` *after* the whole burst. So a fresh session's *very first* manual bet is timestamped at the **exact same instant** as `BlockSessionCheckpointService.ResetToPreGenesisDefaults()`'s `playerStart` boundary (itself computed with zero offset, per §24.9/OQ-BP.9's "no `+1s`" rule). Live evidence from `bet_history.jsonl` confirmed it: a session's `startup_default` deposit, its one manual bet (`NetAmount: -2`), and the *next* session's `startup_default` deposit all carried the identical timestamp `2009-03-21T07:58:21.772Z`. `BetHistoryRepository.RollbackToUtc()`'s filter (`TimestampUtc > checkpoint`, strictly greater) treats an exact tie as "not after the boundary," so the loss bet was **kept** on every subsequent rollback — permanently. A winning first bet would hit the identical code path and persist identically; there is nothing loss-specific about the bug itself.

**Fix, two parts:**
1. **`OnManualBetFromPanel()` now starts its burst timestamp one tick *after* "now"**: `burstBaseUtc = CalendarTimeService.CurrentUtcDateTime.AddSeconds(timePerBet)` instead of the bare current time. Every burst's first bet now always lands strictly after whatever the clock was previously — closing the collision not just for the pre-genesis case but also the analogous risk of a first manual bet colliding with a post-first-block checkpoint's own restore instant.
2. **`BlockSessionCheckpointService.ResetToPreGenesisDefaults()` now calls a new `UserStatsService.ClearAllHistory()`** (→ `BetHistoryRepository.ClearAll()`) instead of `RollbackHistoryToUtc(playerStart)`. Pre-genesis has no legitimate boundary to *partially* keep — nothing is committed yet, so everything is discardable by definition — making an unconditional full clear both simpler and structurally immune to the entire class of exact-timestamp-collision bug, regardless of which bet path produced the colliding record. (`RollbackHistoryToUtc` is unchanged and still correct for the post-first-block case, where genuinely-committed history must be *partially* kept up to the checkpoint.)

**Why autobet was never at risk.** `SimulationService.StartPlayerAutobet()` sets `CalendarTimeService.IsRunning = true`, and the clock advances continuously via `CalendarTimeService._Process(delta)` every frame, *independent of whether a bet has fired yet*. `SimulationService`'s own `_Process` (which fires the first bet via `ExecutePlayerBetOnce`) is a separate Godot per-frame callback that necessarily executes after at least one calendar tick has already applied (at `SpeedMultiplier = 100×`) — so autobet's first-bet timestamp is never exactly equal to a restart boundary. Manual play has no equivalent continuous tick (the clock sits static between explicit `AdvanceClockForBet()` calls), which is precisely why only the manual path was vulnerable.

### 24.12 — The vanishing manual recharges: session-wallet routing, the node-selector mirror, and player-only checkpoints (2026-07-15)

**Incident.** During the Step-14 combined calibration playtest (round 2), the developer made two manual Main→Bankroll recharges in `BankrollProgrammer` and ~20,000 SC vanished from the world — Main Balance had paid, but the Bankroll didn't keep the injection, and the SC went nowhere (not to the casino, not to any account). A code audit found **three independent holes, all the same family**: an absolute-value write of the shared balance services (`PrincipalBalanceService`/`BankrollStateService`) from a source that didn't know about the change. All three were fixed the same day and verified by developer playtest in every combination (autobet ON/OFF, node switches, restarts). Distinguish all of this from the *designed* revert: a recharge made between blocks legitimately reverts on app restart (with Main refunded — a consistent rollback, never a net loss), per the "block = the only commit" model (§24.8).

**Hole 1 — autobet ON: the session-wallet clobber.** `SimulationService` seeds its private session wallet from the Bankroll once at `StartPlayerAutobet` and writes it back **absolutely** (`_bankroll.SetBalance(_wallet.Balance)`) on every settled bet. `BankrollProgrammer`'s manual transfer wrote only to `BankrollStateService` — so the very next settled bet overwrote the injection out of existence (Main had already paid ⇒ SC destroyed). The inverse direction (Bankroll→Main mid-session) had the mirror-image bug: the write-back would have *restored* the withdrawn amount ⇒ SC duplicated. **Fix:** two new session-safe methods on `SimulationService` — `TryManualTransferToBankroll` / `TryManualTransferToBalance` — that mutate the live session wallet (the same pattern as `TryPlayerAutoRechargeAndRestart`), then sync `BankrollStateService` and `PersistFinancialState`. `BankrollProgrammer` routes through them whenever `SimulationService.IsRunning`, falling back to the direct services path when idle. **Rule: while a session is live, any external Main↔Bankroll mutation MUST go through the session wallet.**

**Hole 2 — autobet OFF: the node-selector switch-back (the actual SC destroyer in the playtest).** DiceGame's Active Node Selector deliberately rewrites the shared balance services with the selected node's `NodeFinancialState` mirror (that's how "play as `bot_N`" works; the selector is locked while an autobet is delegated precisely so the scene never conflicts with the active autosessions). Switching **player→bot** saved the player's fresh values into the player mirror and loaded the bot's onto the services — correct. But switching **back to the player** hit an early-return guard (added earlier to stop the *scene-entry* mirror-apply from reverting transfers made in other scenes, e.g. a BankrollProgrammer recharge) and **never restored the player's mirror** — the bot's balances silently became "the player's", self-persisted, and escaped into every other scene (StatusBar, BankrollProgrammer, ScFinances) if DiceGame was exited with a bot still active. Bots idle near the `39,900/100` player-like split, so the swap is easy to miss while the player's real (recharged) balances quietly vanish. **Fix:** `LoadActiveNodeFinancialState(bool restorePlayerFromMirror)` — the two call sites get opposite rules: **scene ENTRY never applies the mirror to the player** (the live services are authoritative; the old revert bug stays fixed), while a **node switch back to the player always does** (the mirror was freshly saved at the player→bot switch of the same visit). `_ExitTree` additionally restores the player mirror if a bot is still active, so bot balances can never leave DiceGame — safe because the selector lock guarantees a non-player active node implies no background session owns the services.

**Hole 3 — checkpoints captured whatever was on the services.** Both capture sites (`DiceGame.CaptureBlockCheckpoint`, manual path; `SimulationService.CaptureCheckpoint`, background path — a delegated autobet CAN be started with a bot as active node) snapshotted the live services. With a bot active, a mined block checkpointed the **bot's** balances as the player's — and the next app restart restored them onto the player. **Fix + canonical rule (developer decision, 2026-07-15): a checkpoint always captures the PLAYER's financial state — the same information at every block commit, without distinction, no matter which node is active or betting** (exactly as when the player sits in another scene while any miner mines a block). With a bot active, both sites now save the bot's fresh state to its mirror, swap the player mirror onto the services for the capture, and re-apply the bot's values after.

**Companion fixes, same day:**
- **Stats parity**: manual recharges now call `UserStatsService.RegisterDeposit` (game time, per §24.10) on both routes — idle (`BankrollProgrammer`) and session (`TryManualTransferToBankroll`, gated on `IsPlayerActive`) — matching the auto-recharge paths, so the "since recharge" stats scope resets on manual recharges too.
- **DiceGame bet-history list on switch-back**: `OnActiveNodeSelected` cleared the list but never reseeded it, so returning to the player showed an empty history until the next scene re-entry (which reseeds from the persistent store, SF.4B.6). Switching back to the player now reseeds via the same `LoadFromHistoricalRecords(GetRecentBets(...))` call scene entry uses; bots keep the cleared list (their history lives in `BotPlayHistory`).

**The general rule this section adds:** the shared balance services belong to the **player**. Another node's values may occupy them only transiently, inside DiceGame, while the selector holds a bot — and they must never escape through a scene exit, a session write-back, or a checkpoint.

## Chapter 25 — Bankroll Management: Progression Resets, Insist After Stop, and Auto-Recharge

**Files**: `Scripts/Sessions/BaseBetSession.cs` (`ApplyStopConditions`, `HandleProfitOrLossStop`, `ResetProgressionToBase`), `Scripts/Betting/ProgressiveBettingStrategy.cs`, `Scripts/Services/SimulationService.cs` (`TryPlayerAutoRechargeAndRestart`, `TryRechargeAndRestartBot`)
**Status**: Implemented and user-tested.

### The Short Version (for everyone)

A progressive strategy grows the bet on each trigger (e.g. ×2.3 on every loss). Left unchecked it would balloon until the bankroll is gone. Three mechanisms keep it under control, in order of preference:

1. **`StopOnLoss` / `StopOnProfit` + Insist After Stop** — the *primary* bankroll manager. You set a loss (or profit) threshold **below** the bankroll; when the running progression reaches it, the bet **resets to base** and keeps going. This caps how deep any one losing run goes, so the bankroll lasts many cycles **without spending a single recharge**.
2. **Bankroll-limit reset (safety net)** — if the grown bet ever exceeds the bankroll but the **base** bet still fits, the progression also resets to base (no recharge). This is the fallback for when a threshold was set too high (or, for bots, not set at all).
3. **Auto-recharge (last resort)** — only when even the **base** bet can't be afforded does the system move money from the Main Balance into the Bankroll and restart from base.

So: **reset cheaply as long as you can; only recharge when you absolutely must.**

### 25.1 — The progression itself

`ProgressiveBettingStrategy.CalculateNextBet` is pure and stateless: on a trigger outcome (`IncreaseOnLoss` and a loss, or `IncreaseOnWin` and a win) it returns `currentBet × (1 + IncreasePercent/100)`; otherwise it returns `BaseBet`. With `IncreasePercent = 130` the multiplier is **×2.3**, so base 10 → 23 → 52.9 → 121.67 → …

### 25.2 — Where the decisions happen: `ApplyStopConditions`

This runs at the **end of every** `ExecuteNext`, *after* `_currentBet` has already been advanced to the **next** bet. In order:

1. **`StopOnProfit`** reached → `HandleProfitOrLossStop(StopOnProfit)`.
2. **`StopOnLoss`** reached → `HandleProfitOrLossStop(StopOnLoss)`.
3. **`_currentBet > balance`** (can't afford the next bet) → either reset to base (insist) or stop (see §25.5).
4. **`RemainingBets`** countdown → stop on `CounterCountReached`.

The profit/loss metric is `currentBalance − baseline`, where the baseline depends on **Session vs Anchor** mode — see §25.3.

### 25.3 — Session vs Anchor stops: where profit/loss is measured from

`StopOnProfit` / `StopOnLoss` always compare `currentBalance − baseline` against your threshold. **Which baseline** is chosen by `UseProgressionAnchorStops` (`BaseBetSession.ApplyStopConditions`):

| | **Session mode** (`UseProgressionAnchorStops = false`) | **Anchor mode** (`UseProgressionAnchorStops = true`) |
|---|---|---|
| Baseline | `SessionStartingBalance` — the bankroll when the autobet session started | `ProgressionAnchorBalance` — the bankroll at the start of the **current progression run** (the last base bet that began the run) |
| Question it answers | "How is the **whole session** doing?" | "How is **this one progression run** doing?" |
| Effect of a win | Win profit nets against the running total, but the baseline does **not** move | A win ends the run and **re-anchors** the baseline; the next run measures fresh |
| With Insist After Stop | Re-anchored to the current balance on each reset (each post-reset segment measures fresh) | Already moves per run; also re-anchored on reset |

**How the anchor moves (anchor mode).** `UpdateProgressionStreak` sets `ProgressionAnchorBalance` to the balance **just before** the first bet of a new streak (the base bet that starts a run). Any non-trigger outcome — e.g. a win when `IncreaseOnLoss` — ends the streak and re-anchors to the current balance, so the next base bet's run measures from zero again.

**Why Session mode re-anchors on an Insist reset.** A reset adds no money, so if `SessionStartingBalance` stayed put, `balance − baseline` would still be past `−StopOnLoss` right after the reset and would re-trigger **every** bet (stuck at base). Re-anchoring on each reset (`ResetProgressionToBase`) makes each post-reset segment measure fresh. So: **no insist → Session baseline is fixed at session start; with insist → it measures from the last reset.**

**Illustration** (base 10, ×2.3 on loss, `StopOnLoss = 50`, Insist ON) — sequence *lose, lose, win, lose, lose…*:

- **Anchor mode:** the first two losses don't reach −50 for that run; the **win re-anchors**, and the next losing run starts measuring fresh. The reset-to-base fires only when a **single run** drops 50.
- **Session mode:** the win's profit nets against the total since session start, so the reset-to-base fires when the **net session** is down 50 — wins literally buy more room before the next reset.

(The §25.4 canonical example uses `StopOnLoss = 33` with **no win** in the run, so Session and Anchor coincide there — both anchor at 100 because it's the very first run.)

**When to use which.**
- **Anchor** — cap the damage of *any single* losing run; resets the martingale frequently, run-by-run (tight per-run control).
- **Session** — cap the *net drawdown* of the whole session; tolerate deeper individual runs as long as wins keep the session afloat.

Both modes feed the same downstream logic (Insist resets, the bankroll-limit fallback, and auto-recharge) described in §25.4–§25.6.

### 25.4 — `HandleProfitOrLossStop` and Insist After Stop

- **Insist OFF** → `Stop(reason)`. The session ends; the player sees `Auto stopped: StopOnProfit/StopOnLoss`.
- **Insist ON** → `ResetProgressionToBase()` instead of stopping: `_currentBet = BaseBet`, profit metric zeroed, streak cleared, and the baselines (`SessionStartingBalance`, `ProgressionAnchorBalance`) re-anchored to the current balance. **No recharge.** The run simply continues from base.

> **Insist After Stop applies only to `StopOnProfit` / `StopOnLoss`.** `StopOnBlockMined` is handled outside the session (by `SimulationService` / DiceGame) and is **never** insisted — a mined block always stops the run if that toggle is on.

**Worked example (the canonical bankroll-management setup):** base 10, ×2.3 on loss, **`StopOnLoss = 33`**, Insist ON, start 100 SC.

| Bet | Amount | Result | Balance | Cumulative loss vs anchor | Action |
|----:|-------:|--------|--------:|--------------------------:|--------|
| 1 | 10 | loss | 90 | 10 | 10 < 33 → grow to 23 |
| 2 | 23 | loss | 67 | 33 | 33 ≥ 33 → **reset to base** (no recharge) |
| 3 | 10 | … | … | re-anchored | continue from base |

Two losses cap the drawdown at ~33 SC and the bet returns to 10 — exactly the intent: manage the bankroll in few attempts, spending **no** recharges. (Setting `StopOnLoss` *above* the bankroll defeats its purpose; the threshold is meant to live **below** the current bankroll.)

### 25.5 — The bankroll-limit branch (`_currentBet > balance`)

After the threshold checks, if the next bet still can't be afforded:

```csharp
if (_currentBet > _wallet.Balance)
{
    if (_config.InsistAfterStop && _config.BaseBet <= _wallet.Balance)
        ResetProgressionToBase();          // grown bet too big, but base fits → reset, NO recharge
    else
    {
        LastStopReason = InsufficientBalance;
        Stop(LastStopReason);              // even base won't fit (or no insist) → stop; recharge happens next
    }
}
```

This is the safety net (item 2 of the Short Version): with Insist ON, a too-deep progression that outruns the bankroll still falls back to base **for free**, as long as the base bet fits. Only when the **base** bet itself is unaffordable does the session stop with `InsufficientBalance`.

### 25.6 — Auto-recharge: the last resort, *after* the stop

`ApplyStopConditions` never recharges — it only ever stops with `InsufficientBalance`. The recharge is decided one level up, **after** the session has stopped (see Chapter 24.5 for why this placement is mandatory):

- **Player** — `SimulationService._Process`: on `!IsRunning` with reason `InsufficientBalance` and auto-recharge enabled, `TryPlayerAutoRechargeAndRestart()` transfers `AutoRechargeAmount` from Main Balance to Bankroll, syncs `BankrollStateService`, and **restarts the progression from base**.
- **Bots** — `SimulationService.TickBots`: the mirror, `TryRechargeAndRestartBot()`, tops up the bot's own `NodeFinancialState.PrincipalBalance` (repeatedly if one top-up can't cover the base bet) and restarts from base.

Because the restart reuses the same `BettingStrategyConfig`, **Insist After Stop stays active after a recharge** — the run keeps resetting cheaply to base until, once again, even the base bet can't be afforded and another recharge is strictly necessary.

### 25.7 — Precedence, in one sentence

On every settled bet: **profit/loss threshold reset (insist)** → else **bankroll-limit reset to base (insist, if base fits)** → else **stop `InsufficientBalance`** → then, post-stop, **auto-recharge + restart from base** (if enabled). Resets are free; recharges are the last resort. This logic lives in the shared `BaseBetSession`, so **player and bot sessions behave identically**.

### 25.8 — The auto-recharge ENABLE toggle: one flag, two access points (Step 12, SF.2.8)

"Auto-recharge enabled" is the on/off switch for the whole last-resort step above (§25.6). Before Step 12 the player had only **one** control for it — the **`Auto Recharge: ON/OFF` toggle inside the DiceGame `StrategyControlPanel`** — and it was a stand-alone per-run UI flag (`_strategyPanel.AutoRechargeEnabled`), read directly wherever the recharge decision was made. That coupling was convenient for testing but meant the switch lived *inside a strategy panel*, not with the account it governs.

Step 12 (D-SF.4) gave `BankrollProgramService` a real, persisted **`AutoRechargeEnabled`** flag (default ON), snapshotted at each block and reverted to ON pre-genesis — exactly like the auto-recharge dose (`AutoRechargeAmount`). This flag is now the **single source of truth** for the player's Bankroll auto-recharge, and it has **two access points** that stay in sync:

1. **Bankroll Programmer** — the canonical home: an `AutoRechargeEnabledToggle` checkbox wired straight to `BankrollProgramService.SetAutoRechargeEnabled` (seeded from the service on entry).
2. **DiceGame `StrategyControlPanel`** — the original toggle, **kept in place** (the testing coupling survives) but re-purposed for the player into a *proxy* of the same service flag:
   - it **seeds FROM** the service on every player-side load (`SyncPlayerAutoRechargeToggleFromService()` — called after `LoadActiveNodeStrategySnapshot` and after a saved-strategy load, so a saved strategy's stored auto-recharge value never overrides the account-level flag), and
   - it **writes TO** the service on genuine player interaction (`OnAutoRechargeToggledFromPanel` → `SetAutoRechargeEnabled`), skipping writes during a load (the load itself raises `AutoRechargeToggled`) and in bot strategy mode.

**Bots are unchanged**: each bot keeps its own per-node `NodeStrategyState.AutoRechargeEnabled` (forced ON in bot strategy mode), so the proxy is a no-op unless the player node is active. Both recharge call sites also gate on the service flag directly (`SimulationService.TryPlayerAutoRechargeAndRestart` and DiceGame's manual-path `OnSessionStopped`), so the service flag wins even if a panel value were momentarily stale — defense in depth. Turning it **OFF** makes an empty Bankroll simply stop betting and wait for a manual recharge (today's `InsufficientBalance` path, now player-chosen). See `Documentation/GLOSSARY.md` and `AIHelperFiles/step12-player-sc-finances-plan.md` (SF.2.8).

### 25.9 — Standalone Martingale Calculator: progression parity with the game's strategy semantics (Step 14 ND.8a, 2026-07-15)

The MainMenu-reachable `MartingaleCalculatorStandalone` consumed its "Multiply On Loss" input as a **bare multiplier** (`nextBet *= input`), while the DiceGame-integrated popup — always driven by `UpdateFromGameSettings` — converts the strategy's `IncreasePercent` into `1 + pct/100` before filling the same field. A player thinking in the game's vocabulary (entering `1` for a +100% increase) therefore got a **flat** sequence from the standalone instead of a doubling one. Fix (ND.8a): the field is relabeled **"Increase On Loss %"** (placeholder `100` = classic doubling martingale; `0` = flat betting, matching `IncreasePercent`'s domain), `BuildRows` computes `multiplier = 1 + increaseOnLossPercent / 100` — every losing step keeps the previous bet and adds the configured increase, exactly like `ProgressiveBettingStrategy` (§25.1) and the integrated popup — and a `maxRows = 500` safety cap (mirroring the popup's) prevents a flat/slow progression from instantiating an unbounded row list. The integrated popup was untouched (its game-context path already had the correct formula). Full write-up: step14 plan §12.3 (ND.8a).

## Chapter 26 — Network Difficulty (continuous, persisted, validated)

**Files**: `Scripts/BlockchainPort/Blockchain/Models.cs` (`Block.Difficulty`), `Scripts/BlockchainPort/Blockchain/BlockchainService.cs`, `Scripts/BlockchainPort/Simulation/NodeAgent.cs`, `Screens/BlockExplorer/BlockExplorer.cs`
**Status**: **D.1–D.4 implemented, user-tested & validation-closed** — continuous difficulty (D.1), hybrid feed-forward + LWMA retarget with easing (D.2), Block Explorer live readout (D.3), calibrated (D.4). A 2026-06-25 power-step validation campaign (§26.9) confirmed the regulator is correct at steady state (power 1/2/10) and across up/down steps; the drafted contingency fixes were all closed unimplemented.
**Plan**: `AIHelperFiles/btc-pools-hardware-plan.md` → "Network Difficulty Regulator".

### The Short Version (for everyone)

"Difficulty" is **how hard it is to mine one block** — concretely, *how many nonce attempts a block is expected to take*. Until now it was a **fixed** rule (a hash had to start with `00` and the next hex digit be ≤ `6`). That works, but it can't *move*: when more miners or faster hardware join, blocks would just come faster and faster. Real Bitcoin solves this by **adjusting difficulty so the average time between blocks stays near a target**. This chapter is the foundation for that: difficulty is now a **single tunable number stored on every block**, ready for the regulator to start moving it (next step).

### 26.1 — Why the old rule had to change

The discrete `"00"` + next-hex-≤`'6'` check has only a few possible "tightness" settings (lengthen the prefix, lower the max hex) — coarse jumps, not a smooth dial. A regulator needs to nudge difficulty by small percentages every block, so difficulty had to become a **continuous value**.

### 26.2 — The continuous model

- **`Difficulty` = expected nonce attempts per block.** Higher = harder.
- A 64-hex double-SHA256 block hash, read as a 256-bit integer `H`, **meets target** when:

  ```
  H ≤ 2²⁵⁶ / Difficulty
  ```

  A uniformly random hash satisfies that with probability `1 / Difficulty` — so on average it takes `Difficulty` attempts to find a valid block. (This is exactly how Bitcoin's "target" works; we just store the human-friendly *attempts* number instead of the raw target.)
- Implemented in `IsHashAtTargetDifficulty(hash, difficulty)` using `System.Numerics.BigInteger` (the hash is parsed with a leading `0` nibble so it's always read as a non-negative 256-bit number).

### 26.3 — Same pace as before (`InitialDifficulty`)

The old rule's success probability was `(1/16²) × (7/16) = 7/4096`, i.e. **`4096/7 ≈ 585.14` expected attempts**. So `InitialDifficulty = 4096/7` reproduces the **exact** old probability. D.1 changes only the *representation*, not the block pace: genesis and every new block are seeded at `InitialDifficulty`, so nothing about gameplay timing changes until the regulator (D.2) starts moving the number. Target pace stays **58,500 in-game seconds per block** (≈16h40m at the 100X scale).

### 26.4 — Persisted per block, validated without replay

- Every block stores the difficulty it was mined against in **`Block.Difficulty`** (serialized with the block in the monthly JSON chunks — no schema work needed).
- Mining (`NodeAgent.MinePendingTransactions` and the 1-bet-per-attempt `TryMineSingleNonceAttempt`) asks `Blockchain.GetNextBlockDifficulty()`, mines against it, and stamps it on the block via `CommitBlock`.
- **`ChainIsValid` checks each block's hash against its own stored `Difficulty`.** This is the key reason difficulty is *persisted* rather than recomputed: validating (and knowing the current difficulty on load) is **O(1)** — read the tip — instead of replaying every retarget from genesis (which would grow with chain height). `EffectiveDifficulty` treats a missing/zero value (a pre-D.1 save) as `InitialDifficulty`, so old chains still validate.
- **`GetNextBlockDifficulty(networkPower)` is the single retarget hook**, called by both mining paths — so the bootstrap and the weighted lottery inherit the regulator automatically. Its body is the hybrid regulator (§26.6).

### 26.5 — The regulator: how difficulty actually moves (D.2)

The whole point is to keep the **average time between blocks near a target** (`TargetBlockSeconds = 58,500` in-game sec ≈ 16h15m) as mining power changes. `GetNextBlockDifficulty` combines three pieces:

```
target = anchor × feedbackTrim
next   = current + DifficultyEaseAlpha × (target − current)
```

1. **Feed-forward anchor** — the *instant, exact* part. In-game time runs at clock-speed × real time and a block needs ≈`Difficulty` attempts, so the difficulty that holds block time at target is `(TargetBlockSeconds / clockSpeed) × power = InitialDifficulty × power`, where **power** = the total active mining rate (Σ of all active miners' bets/sec). When a miner joins/leaves or hardware changes, the anchor reflects it immediately — no waiting for feedback. When power is unknown (`0`: the historical bootstrap or idle), the anchor holds at the current difficulty (feedback-only).
2. **Feedback trim (LWMA)** — the "real Bitcoin" part: `TargetBlockSeconds / lwmaSolvetime`, where `lwmaSolvetime` is a Linear-Weighted Moving Average of the last `W = 20` block solvetimes (recent blocks weighted more). It corrects calibration drift and PoW luck. **Clamped to `[0.5×, 2×]`** per block so noise can't swing it wildly.
3. **Easing** (`DifficultyEaseAlpha = 0.7`) — instead of snapping to `target`, close 70% of the gap each block, so a change ramps in over ~3 blocks rather than instantly (gives a brief, fair transition window).

**Why hybrid (and not pure block-time like Bitcoin)?** Bitcoin uses *only* block time because it has millions of miners → the signal is smooth. Our network has **1–5 miners**, so per-block solvetime is noisy and pure feedback converges slowly. We *know* the exact total power, so the feed-forward anchors the level instantly and the LWMA just trims — best of both.

**Why total power, not average or participant count:** difficulty must track the **sum** of all miners' rates (`avg × count`). The average alone would ignore how many are mining; the sum captures both per-miner power and the number of participants.

**Plumbing of `power`:** `SimulationService` sums `GetActiveMiningRates()` each frame and calls `NetworkRoot.SetActiveMiningPower(total)` (0 when idle); `NetworkRoot` passes it into `NodeAgent` → `GetNextBlockDifficulty(power)`. Each mined block stores the power used in `Block.MiningPower` (diagnostic).

**Two important refinements:**
- **The bootstrap is exempt.** The historical pre-mine (genesis → 21 Mar) uses *scripted* block timestamps, so block-time feedback there is meaningless — running it would drift the starting difficulty (it once fell to ~100). So while `_bulkMining` is set, every bootstrap block is pinned to `InitialDifficulty` (`MineForNode` passes a `forcedDifficulty`). The game therefore always starts at ≈585, and the regulator only governs **live** play.
- **Difficulty is locked on the first nonce attempt of a block, per tip.** It's fixed the moment the first attempt at a new tip (block height) happens (`NodeAgent._candidateDifficulty`, keyed by `_difficultyTipHash`) and **kept for the whole block — even across mempool changes** (a bot broadcasting a tx rebuilds the candidate *template*, but must not move the difficulty). So a power/participant change *before* the first attempt counts for that block; *after* it, it applies only to the **next** block. The Block Explorer's "mining difficulty" shows this locked value (`GetPlayerNextBlockDifficulty`).
- **Manual and autobet behave identically.** Both mine through `TryMineSingleNonceAttempt`, so both honour the per-tip lock. The power input is what differs: autobet's `SimulationService` pushes it each frame; **manual betting sets the same total (player + configured bots) via `SetManualMiningPower` before the bet** — otherwise manual would stay stuck at the player-only difficulty.

### 26.6 — Seeing it in the Block Explorer (D.3)

- The **main readout** (chain-info line) shows the **difficulty of the block being mined now** (`GetPlayerNextBlockDifficulty`, i.e. the next-block difficulty at the current power) + a trend arrow (vs the last block) + the **recent average block time** vs target. This is the live "where is difficulty heading" view.
- Each **already-mined block** shows its *own* stored difficulty in the Latest-Block panel and the per-block Lookup (those don't change).
- Everything auto-refreshes on the explorer's 1-second tick.

### 26.7 — Calibration (D.4)

Tuned by play-testing: `DifficultyEaseAlpha = 0.7` (ramps a change in over ~3 blocks — fast enough to track participants/hardware, smooth enough to avoid jerk), `LwmaWindow = 20`, clamp `[0.5×, 2×]`, `MinDifficulty = 1.0`. `TargetBlockSeconds = 58,500` is fixed for 100X temporal coherence. Fractal-scale calibration of the *absolute* difficulty jumps across later eras (CPU→GPU→ASIC) is a future tuning item.

### 26.8 — Verifying `ChainIsValid`

`ChainIsValid` has no UI — it runs automatically on load: `ApplyStateFromSnapshot` → `TryReplaceChain`, which **only accepts the chain if `ChainIsValid` passes** (each block's hash checked against its own stored `Difficulty`). Practical check: mine some blocks, restart the app; if the chain **survives intact** (full length, every block with its difficulty), validation passed. If a block's hash didn't meet its stored difficulty, the chain would be rejected and reset to genesis-only.

### 26.9 — Validation campaign & verdict (2026-06-25): the regulator is sound, no fixes

After the hardware/pools work landed, the regulator was stress-tested across power steps with real instrumentation. **Verdict: the hybrid regulator is fundamentally correct and needs no changes.** A contingency plan (EMA on power, asymmetric easing, startup-stall fixes, lowering α) was drafted and then **closed unimplemented** — every proposed fix turned out unjustified. The full investigation lives in `AIHelperFiles/btc-pools-hardware-plan.md` ("Difficulty Regulator — Power-Step Contingency Plan").

**How it was measured (kept as permanent assets):**
- **F0 difficulty trace** — `NetworkRoot.AppendDifficultyTrace()` appends one CSV row per *live*-mined block (excluded during `_bulkMining`) to `user://logs/difficulty_trace.csv`: `configuredPower`, `realizedPower = difficulty × (TargetBlockSeconds/InitialDifficulty) / solveSec`, `difficulty`, `anchor`, `solveSec`, `solveRatio`. Inverting the calibration this way recovers the **true attempt-execution rate** per block, so claims can be checked against data instead of inferred.
- **DEV time-acceleration tool** (Ch. 27.7) to run 30-block samples in a fraction of the wall-clock.

**What the data showed:**
- **Per-block solvetime is ≈ exponential** — single-block `solveRatio` ranged 0.02→3.7 at *constant* power. ⇒ The regulator must always be judged by **aggregates over ≥20–30 blocks**, never single blocks. An early "structural stall" read was a bootstrap-data artifact (the bulk-mined baseline is semi-synthetic, sd ≈ 0.16, unlike live PoW).
- **Steady-state calibration is correct at power 1, 2 and 10.** In each regime difficulty settled at `anchor = InitialDifficulty × power` with aggregate realized power ≈ configured and mean `solveRatio` ≈ 1.0 (e.g. power 10: realized 9.6, ratio 1.03, difficulty 5793 vs anchor 5851).
- **Up-step (2→10):** mild, *variance-driven* overshoot — peak 1.13× anchor, settled within ~2 blocks. An earlier run that appeared to overshoot ~1.4× had simply not converged (a lucky run of fast blocks inflating the LWMA); it was a transient, not a calibration error.
- **Down-step (10→1):** symmetric — difficulty cedes from ~5600 to the new anchor (~585) in ~3 blocks; the only genuinely slow block is the 1-frame power-read-lag transition block. No prolonged stall ⇒ asymmetric easing not needed.
- **Power accounting audited correct:** total power = Σ`HardwareRate` over {player + running bots} = Σ`TotalCredits`; every casino-routed attempt originates from a counted credit; the `casino` node is not itself a runner. So `anchor = InitialDifficulty × power` is fed the right number (see Ch. 27.4).

**Minor, non-actionable observation:** at power 1 and 2 difficulty settles ~10% *below* anchor (ratio ~0.87); at power 10 it sits right on it. Within the noise floor and in the harmless direction — left as-is. Re-run the F0 trace + dev tools to re-validate if the regulator, its constants, or the pool/hardware model ever change.

## Chapter 27 — Hardware Credits & Mining Pools (individual vs casino community pool)

**Files**: `Scripts/Hardware/HardwareModels.cs` (`NodeHardwareState`), `Scripts/Hardware/HardwareAllocationRepository.cs`, `Scripts/Hardware/CasinoPoolRepository.cs`, `Scripts/Services/WalletInitializationService.cs` (bootstrap), `Scripts/Services/SimulationService.cs` (`HardwareRate`, `RouteNonceAttempt`, power feed), `Scripts/BlockchainPort/Simulation/NetworkRoot.cs` (casino nonce, fee, distribution), `Screens/BTCPoolsAndHardwareShop/`, `Screens/DiceGame/DiceGame.cs` (speed lock, manual nonce routing).
**Status**: **Implemented & validated** (Step 6 hardware/pools). **Plan**: `AIHelperFiles/btc-pools-hardware-plan.md`.

### The Short Version (for everyone)

Mining power in GamblingMiner is measured in **hardware credits**. Each credit = **one nonce attempt per second** (= one bet/sec of betting speed). A node's credits sit in one of two pools:
- **Individual pool** — *solo mining*: the credit's attempts go to the node's own blocks; if it mines, it keeps the **full** block reward.
- **Casino community pool** — *shared mining*: the credit's attempts mine on the casino's behalf; when the casino mines a block, the reward is split **proportionally** among all casino-pool contributors, **minus a dynamic fee**.

The trade-off mirrors real mining pools: solo = full reward but spiky (you might mine nothing for a long time); pool = smaller, steadier payouts minus a fee. **`1 bet = 1 nonce attempt` always holds** — credits *reallocate* where each attempt is aimed; they never multiply attempts.

### 27.1 — The credit model (`NodeHardwareState`)

Per node: `IndividualPoolCredits`, `CasinoPoolCredits`, and `TotalCredits = Individual + Casino`. Persisted in `user://hardware_allocation.json` (CamelCase JSON) by the static `HardwareAllocationRepository`, which raises `HardwareChanged(nodeId)` after any change so DiceGame re-locks the active node's betting speed live.

- **Betting speed is hardware-locked.** `SimulationService.HardwareRate(nodeId) = Clamp(TotalCredits, 1, 99)` bets/sec — read **fresh every tick**, so buying/moving/discarding credits mid-run takes effect immediately (bet rate, Block Explorer ⛏ readout, and the difficulty feed-forward all update at once). The DiceGame APS selector is display-only, re-locked to hardware.
- Repository operations: `AddCredits` (buy → lands in the individual pool), `RemoveCredits` (discard → from the **casino pool first, then individual**, floored at **1 total** so reported power stays consistent with `TotalCredits`), `MoveToCasinoPool` / `MoveToIndividual` (reallocate the split, total unchanged).

### 27.2 — How one attempt is routed (round-robin, never a multiplier)

`HardwareAllocationRepository.NextNonceTarget(nodeId)` decides where a single bet's attempt goes. A per-node cursor walks the node's credit slots: the first `IndividualPoolCredits` slots route to the node's **own** chain, the remaining `CasinoPoolCredits` slots to the **casino** chain. Over `TotalCredits` consecutive bets this yields exactly `IndividualPoolCredits` own + `CasinoPoolCredits` casino attempts — a **true reallocation of power**, avoiding any quadratic `TotalCredits²` blow-up. `SimulationService.RouteNonceAttempt` (autobet, player + bots) and `DiceGame.ProcessBlockchainAttemptForBet` (manual) both call it, so manual and autobet behave identically.

### 27.3 — The casino community pool: fee & reward distribution

When a casino-routed attempt mines a block (`NetworkRoot.TryCasinoNonceAttempt` → `HandleMinedBlock` + `QueueCasinoRewardForDistribution`):
- **Dynamic fee** (`CalculateCasinoFeePercent(casinoTotal, individualTotal)`): a function of `ratio = casinoTotal / individualTotal` — **30%** at a balanced 1:1 ratio, scaling **up to 50%** when the casino pool dominates and **down to 10%** when individual pools dominate (exact form `0.30 + clamp((ratio−1)/2, 0, 1)×0.20` for `ratio ≥ 1`, symmetric downward). This makes solo vs pool a live economic decision.
- **Proportional payout**: each contributor receives `poolAmount × (its CasinoPoolCredits / casinoTotal)`, minus a fixed `CasinoTxFee = 0.1` per payout. Payouts wait for **coinbase maturity (N = 1 block)** and are then distributed (`TryDistributePendingCasinoRewards`, retried after every block). The reward ledger (events, payouts, status) is persisted by `CasinoPoolRepository` and surfaced in the Pools & Hardware screen.

### 27.4 — How pools feed the difficulty regulator (one chain, honest power)

All nodes share **one canonical chain** (consensus via `BroadcastBlock`; block indices stay globally sequential). Whether an attempt is "individual-routed" or "casino-routed" only changes the candidate's coinbase recipient/transactions — every attempt still extends the same chain tip at the same difficulty. So the **total attempt rate on the chain = Σ`TotalCredits` of all active miners**, which is exactly what `SimulationService.GetTotalActiveMiningPower()` sums (player + running bots) and pushes to `NetworkRoot.SetActiveMiningPower()` → the regulator's feed-forward anchor `InitialDifficulty × power` (Ch. 26.5). This identity was **audited** during the validation campaign (Ch. 26.9): the casino node is not itself a runner, and casino-routed attempts come from already-counted credits — no double-count, no uncounted attempts.

### 27.5 — First-launch bootstrap: 1 individual + 0 casino (revised 2026-06-25)

`WalletInitializationService.EnsureHardwareAllocation()` seeds credits **only on first launch** (guarded by the existence of `hardware_allocation.json`; existing saves keep their allocation). Each of the 5 miner nodes (`player`, `bot_1..4`) starts with **1 individual-pool credit and 0 casino-pool credits** → everyone begins at a single private-pool credit, 1 bet/sec, and an **empty casino pool**. Casino participation is **opt-in**: a node joins by moving a credit into the casino pool.

> **Design determination**: the original bootstrap was `1 individual + 1 casino` per node (casino pool pre-populated, fee starting at 30%). It was changed so the starting world is the simplest possible solo-mining baseline and the casino pool is something players deliberately opt into — also the cleanest baseline for power/regulator testing. To observe the new bootstrap on an existing install, the `hardware_allocation.json` must be cleared (or user data reset).

### 27.6 — The Pools & Hardware screen (`BTCPoolsAndHardwareShop`)

A left node list + right detail panel, reading straight from the static repositories (no `NetworkRoot` instance). For a mining node it shows the individual↔casino split with **move** buttons and two **DEV** buttons: **Buy Hardware** (+1 credit → individual pool) and **Discard Hardware (−1)** (removes a credit, casino-first, disabled at the 1-credit floor). For the casino it shows pool totals, the current dynamic fee, contributors, and the recent reward-event table. The Discard button exists primarily for **power-decrease test runs** (dropping a node to a single private-pool credit and back down from high power).

### 27.7 — DEV tooling: time acceleration (100X→9000X)

To run validation samples in a fraction of the wall-clock without altering the dynamics under measurement, `CalendarTimeService.DevTimeScale` (an integer multiplier on the 100X base clock) scales **both** the calendar clock (`delta × SpeedMultiplier × DevTimeScale`) **and** the bet-execution rate (`SimulationService._Process`: `simDelta = delta × DevTimeScale` for player + bots) by the same factor. The power fed to the regulator is **deliberately not scaled**, so `attempts/in-game-second = (rate·k)/(100·k) = rate/100` stays invariant — difficulty, power, in-game solvetimes and ratios are identical; only wall-clock compresses.

- **Why not just raise `SpeedMultiplier`?** That speeds the clock but not bet execution, so in-game solvetime per block inflates by the factor, the regulator reads "blocks too slow", and the clamped `feedbackTrim` can't compensate → difficulty collapses. Both must scale together.
- **UI**: `UI/DevTimeScaleSelector/DevTimeScaleSelector.cs` (programmatic, like `StatusBar`) — selector with **10 options: 100X, then 1000X..9000X** in 1000X steps — in DiceGame (next to the APS selector) and BlockExplorer (under the StatusBar). Live; **not persisted** (resets to 100X on restart).
- **Caveat**: `MaxBetsPerFrame = 10`/node/frame caps throughput at ~600 bets/s/node; at very high scale × high single-node hardware the acceleration stops being linear (measured dynamics stay intact). The 10000X option was removed for hitting this ceiling; **9000X is the tested-smooth ceiling**. Irrelevant for the normal measurement regime (power split across low-rate nodes).

---

## Chapter 28 — Founder Economics (Step 7: Satoshi, Hal, Mike Hearn)

Makes the early-Bitcoin opening historically faithful on top of the real candidate engine (Ch. 21) and the difficulty regulator (Ch. 26). Implementation + decisions + test log: `AIHelperFiles/step7-historical-character-economics-plan.md`. Original design provenance: `historical-founders-and-bootstrap-plan.md` (Phases 4/6/7).

### 28.1 — Founders are regulated concurrent miners (the model)

After the first-launch bootstrap (21 Mar 2009 baseline), the founders **keep mining in the player era** — but they never advance the clock. This **refines OQ-2** ("no autonomous mining after the bootstrap"): there is no autonomous **time** advancement, yet founders **do** add hashrate. They only perform nonce attempts *while the player advances time by betting* (lockstep).

`FoundersMiningService` (autoload, pure controller — no chain/Godot state, nothing persisted) owns each founder's **power** and the regulator math. `SimulationService` drives it each frame:
1. `GetTotalActiveMiningPower()` = **player + running bots only** (`W_others`). ⚠️ It must **not** sum `GetActiveMiningRates()` (which also lists founders + casino for the Block Explorer ⛏ display) — doing so double-counts the founders into their own denominator and inflates Satoshi's share (a bug caught and fixed in the 7.5 test).
2. Once per **new block**, `RecomputeFounderPowers(W_others, nowLocal, satoshiConfirmedBtc)` updates powers (Satoshi's chain-scanned BTC query is too costly per frame).
3. Every frame, `SetActiveMiningPower(W_others + ΣfounderPower)` so the regulator raises difficulty for the founders' hashrate and block pacing stays at `TargetBlockSeconds`. Net effect: the player loses ~Satoshi-share of blocks — thematically, "Satoshi mined most early blocks".
4. After the bet loops, `DrainFounderAttempts(nonFounderAttempts, W_others)` accrues each founder `attempts ∝ power/W_others` into a fractional accumulator; `SimulationService` mines those whole attempts on the founders' own candidates (own coinbase). A founder block is an **external block** — `CaptureCheckpoint()` + `StopPlayerOnExternalBlockMined()`, exactly like a bot's.

Share identity: with `power = s/(1−s)·W_others` (`shareToWeight`), a founder wins fraction `s` of blocks regardless of how many bots are online, because difficulty is shared (blocks won ∝ attempts made).

### 28.2 — Satoshi's regulator (11,000 BTC by 2011-04-26)

Recomputed per block while active (`SatoshiTargetBtc = 11000`, spendable, excludes the unspendable genesis 50; `SatoshiEarliestDisappearance = 2011-04-26`):
- **Before the floor date:** `targetShare = clamp01(btcRemaining / blocksUntilFloor / 50)`; `power = shareToWeight(targetShare, W_others)`. From ~5,550 BTC at player start this is **~10% share** — a *historical requirement*, not a tunable (it's the output, never capped to make the player richer).
- **Past the floor, still short:** ramp power **exponentially** (`W_others · GROWTH^blocksPastFloor`) to finish ASAP, then retire.
- **Retire** when clock ≥ floor **and** confirmed ≥ 11,000 → power 0, flagged retired, **coins frozen forever** in Basic Mode (a "Satoshi returns" event is left open for the full version / DLCs).

### 28.3 — Hal: a `P=1.0` drip that fades by 9 Aug 2009

Hal keeps **one participant's worth of power** (`HalBaselinePower = 1.0`, deliberately *not* lowered) and fades linearly to 0 between 21 Mar and **9 Aug 2009** (his real ALS turning point), then dormant. He has **no BTC target** (emergent). The intended dynamic is that the player + a growing miner field outgrow him so he shrinks **relatively**; the linear absolute fade is the **v1 stand-in** for the network-coupled fade, pending the postponed gradual-miner-spawning feature. He counts in Satoshi's `W_others` while he mines (so Satoshi's ~10% stays exact). His bootstrap holdings (3 blocks + 10 BTC) are a known overshoot vs strict fractal scaling, accepted because the bootstrap baseline is locked.

### 28.4 — Mike Hearn + scripted player-era events

Mike Hearn is a registered founder node who **never mines** (real history: no documented mining) — a receive-only holder entering ~12 Apr 2009. Player-era scripted transactions are injected by **`HistoricalEventScheduler`** (a static class, like `HistoricalBootstrapService`), hooked into `NetworkRoot.HandleMinedBlock` beside the bot-tx scheduler. State is **derived from the chain** (each step's deterministic-salt txid checked for confirmation via `IsHistoricalTxConfirmedStatic`) — no side flag file, so it survives the revert-to-last-block model — and steps run strictly in order, idempotently.

The famous **~18 Apr 2009 32.51 round-trip** (literal — Hearn sends first; Q-N1):
- **E6** Satoshi → Hearn 32.51 (seed) → **E6b** Hearn → Satoshi 32.51 (his single outgoing tx) → **E7** Satoshi → Hearn 82.51 (the coin + 50 gift). Net **Hearn +82.51**.
- **E8** (17.49 change) was **not** modelled in Step 7 — change was implicit in the account model and a Satoshi→Satoshi self-send was rejected by the engine. It is **now a real change output** under the **Step 8** UTXO model (a fresh Satoshi address; audited on-chain — see Ch. 30).

### 28.5 — The 12 Jan 2009 10 BTC Satoshi → Hal tx (E4)

Injected in the **bootstrap** (`HistoricalBootstrapService`) when the scripted clock crosses 12 Jan, via `NetworkRoot.InjectHistoricalSignedTxStatic` (a real signed tx, deterministic-salt txid for idempotency, no `InputData` note per Q-X4 — only the genesis carries an inscription in v1). Confirmed in the block whose timestamp ≈ 12 Jan (real block 170; ~block 13 here — **dates are the source of truth, not heights**).

### 28.6 — Historical timeline (real date → in-game)

| Real date | Event | In-game reproduction |
|---|---|---|
| 2009-01-03 | Genesis, 50 BTC (unspendable) | Genesis coinbase → Satoshi's derived `gm1q…` |
| 2009-01-11 | Hal "Running bitcoin" | Hal joins the bootstrap miners |
| 2009-01-12 | First p2p tx: 10 BTC Satoshi→Hal | **E4**, injected in the bootstrap (~block 13) |
| 2009-03-21 | — | **Player start** (random time-of-day) after the bootstrap |
| 2009-04-18 | Satoshi↔Hearn 32.51 + 50 gift | **E6/E6b/E7** round-trip via the scheduler (Hearn +82.51) |
| 2009-08-09 | Hal's ALS turning point | Hal's power reaches 0; dormant after |
| ≥ 2011-04-26 | Satoshi's disappearance | Retirement once ≥ 11,000 BTC; coins frozen |

### 28.7 — DEV surfacing & telemetry

`FoundersWallets` adds a **"Founder Economics [DEV]"** readout (live Satoshi target/power/share/retirement, Hal decay, Hearn holdings) + a Mike Hearn selector. `FoundersMiningService.AppendTelemetry` writes `user://logs/founders_trace.csv` (one row/block: powers, Satoshi share + BTC, Hal/Hearn BTC, retired flag), the founder-economics counterpart to the difficulty trace (Ch. 26). The Block Explorer's ⛏ indicator also lists the casino (Σ active casino-pool credits) and the founders' regulated power.

**Verified in-engine:** Satoshi 9.4% share (5/53) on target; Hal hits 0 exactly on 9 Aug 2009; the Hearn round-trip lands on 18 Apr (Hearn +82.51); a 168-block durability run stayed sequential, monotonic, crash-free, and chain-valid on reload.

---

## Chapter 29 — UI Design & Godot Layout (especially scrolling)

This chapter exists because a single "Satoshi isn't visible at the bottom of the address list" bug consumed an entire session of wrong guesses. Almost none of the guesses were the real cause. Read this **before** building or fixing any scrollable panel — it will save hours.

### 29.1 — The core mental model

A panel scrolls **only if it has a height that is BOTH bounded AND smaller than its content.** Two things must be true at once:

1. **Bounded:** some ancestor pins the panel to a finite height. The dependable bounding chain is:
   `MarginContainer` (fills the screen: `anchors_preset = 15`, full-rect) → `VBoxContainer` → the scroll element with `size_flags_vertical = Fill+Expand (3)`.
   A `MarginContainer` *forces* its child to fill it; a `VBoxContainer` then gives its **expand** child the remaining space. If an ancestor is itself unbounded, nothing below it is bounded either.
2. **Overflowing:** the content's natural/declared height exceeds that bounded height, so a scrollbar appears.

If a panel "won't scroll," exactly one of these is false — usually (1) the element isn't actually bounded, or the content's reported height is wrong.

### 29.2 — The two scroll patterns (pick ONE, never mix)

**Pattern A — `ScrollContainer` wrapping the content.** Best for a column of many distinct controls (Labels, Buttons, inputs, or `RichTextLabel`s **that carry an explicit `custom_minimum_size`**). The container scrolls the whole stack with one scrollbar.
- Structure: `MarginContainer → ScrollContainer (horizontal_scroll_mode = 0) → VBoxContainer (size_flags_horizontal = 3) → content`.
- Used by `FoundersWallets`, `BotsBtcWallets`, `MainMenu` (button list).
- **Why content needs real minimum heights:** the `ScrollContainer` decides whether to scroll from its child's *minimum size*. Regular Labels/Buttons report a correct minimum; a `fit_content` `RichTextLabel` does **not** (see 29.3). FoundersWallets works because its dynamic labels set `CustomMinimumSize` explicitly.

**Pattern B — a single `RichTextLabel`'s own internal scroll.** Best for one big block of dynamic BBCode text.
- Settings: `scroll_active = true`, `fit_content = false`, and a **bounded** height (via `size_flags_vertical = 3` inside an expanding parent).
- The label renders **all** of its text into an internal scroll buffer and shows its own scrollbar — no height estimation, no clipping. It also handles its own mouse wheel.
- Used by `BlockExplorer`'s right column (Latest Block + Network Status + Address Directory merged into one label).

### 29.3 — The traps that wasted the session (in the order they bit)

1. **`fit_content = true` `RichTextLabel` inside a `ScrollContainer` → never scrolls.** `fit_content` reports an unreliable minimum height inside containers, so the `ScrollContainer` thinks the content fits. This is the #1 trap. Fix: use Pattern A with `custom_minimum_size`, or Pattern B.
2. **`HSplitContainer` does not reliably bound/report content height inside a scroll.** It is built to *fill* and let you drag a divider, not to size-to-content. Replace it with `HBoxContainer` (give one column a fixed `custom_minimum_size` width and the other `size_flags_horizontal = 3`).
3. **Mouse wheel eaten by `mouse_filter`.** In Pattern A, the wheel reaches the `ScrollContainer` only if **every** control from the hovered node up the parent chain has `mouse_filter = PASS (1)`. The default is `STOP (0)`, which marks the event handled and stops propagation. A big label (or any container) in the chain with `STOP` swallows the wheel. Either set the whole chain to `PASS`, or use Pattern B (the label scrolls its own wheel, so `STOP` is correct there).
4. **The last line is flush against the bottom edge.** A `scroll_active` label's maximum scroll equals its content height, so the final line sits exactly at the viewport's bottom edge and looks half-clipped — the user sees the *second-to-last* line as "the bottom." Fix: append a few trailing blank lines (`"\n\n\n"`) so the last real line clears the edge. (This was the actual final bug: "the scrollbar always stops at `player`," the entry just before `satoshi`.)
5. **`Text` resets the scroll position.** Setting `RichTextLabel.Text` snaps its internal scroll back to the top. On a timer-refreshed panel, capture `GetVScrollBar().Value` before assigning `Text` and restore it after.

### 29.4 — Diagnose with numbers; never guess at layout

The session was lost to *guessing* at structure. The fix came in one shot once real numbers were printed. When a panel won't scroll, print — for the scroll element and its content label — at least:

- `GetVScrollBar()` → `Value`, `MaxValue`, `Page`, `Visible` (if `MaxValue > Page` and `Visible`, scrolling *works*; if `MaxValue ≈ Page`, the element isn't bounded or the content height is underreported),
- the label's `Size`, `GetContentHeight()`, `GetLineCount()`, `ScrollActive`, `FitContent`,
- proof the data is even present (e.g. the tail of `Text`, or the last data row) — sometimes "missing" content is a data bug, not a layout bug.

Also add a **visible canary** (e.g. temporarily append a marker to a Title) to confirm the running app actually reloaded the edited `.tscn`. C# changes always rebuild the assembly; **external `.tscn` edits require the scene to be reloaded** in the editor (or a Godot restart) — if the canary doesn't change, the problem is stale scene loading, not the layout. Strip the diagnostic + canary once fixed.

### 29.5 — Checklist for a new scrollable panel

1. Bounded chain: `MarginContainer → … → expand element`.
2. Choose Pattern A or B; do not nest a `fit_content` `RichTextLabel` in a `ScrollContainer`.
3. Two columns that scroll → `HBoxContainer`, not `HSplitContainer`.
4. Pattern A + a big label → set the wheel chain to `mouse_filter = PASS`.
5. Dynamic text → preserve scroll across refresh; add trailing blank lines.
6. If it doesn't scroll, **print the numbers** before changing structure.

### 29.6 — Horizontal overflow: a non-wrapping `Label` forces the panel's min width (Step 8 fix)

Symptom (hit while adding the FoundersWallets "Address Book" panel): the panel **clipped on the left**, so a left-aligned control — a `CheckBox`, whose tick box sits at its left edge — became **unreachable**.

Cause: a **`Label` with `autowrap_mode = Off` (the default) reports a minimum width equal to its FULL single-line text width.** A long description label (~230 chars) therefore demanded a width wider than the scroll viewport. The vertical `ScrollContainer` has `horizontal_scroll_mode = 0` (disabled, correct), so it can't scroll sideways — instead the oversized content is pushed past the left edge and clipped. The widest child sets the whole `VBoxContainer`'s minimum width, so **one** over-long label drags the entire column off-screen.

Fixes / rules:
- **Long `Label`s in a width-bounded column must wrap:** set `AutowrapMode = TextServer.AutowrapMode.Word` (C#) / `autowrap_mode = 3` (`.tscn`). A wrapping label's minimum width collapses to ~one word, so it fits the viewport and grows in height instead.
- **`RichTextLabel` wraps at spaces by default**, so its minimum width = its widest *unbreakable token*. A 42-char `gm1q…` address is one such token (no break points) — keep anything after it (e.g. `  —  50.00000000 BTC`) **after a space** so it wraps onto the next line rather than widening the panel. This is why the address-book label (full addresses + balances) did **not** overflow but the plain description label did.
- **Diagnosing:** if a panel clips sideways with horizontal scroll disabled, look for the *widest* child's minimum width — it's almost always a non-wrapping `Label`, not the scroll setup. Left-aligned interactive controls (checkbox ticks, radio buttons) are the first to become unclickable, which is the tell.

### 29.7 — The scrollable address-book list (Step 8 — Pattern B in practice)

The wallet address lists (FoundersWallets "Address Book", BTCWallet "Show addresses") are the canonical in-app use of **Pattern B** (29.2): one `RichTextLabel` with its *own* internal scroll. The first cut used a non-scrolling list (a `VBoxContainer` of `Label`s in BTCWallet; a `fit_content` `RichTextLabel` capped at 80 rows in FoundersWallets) — so Satoshi's ~109 coinbase addresses showed only the first rows with a dead "… and N more" note and **no way to reach the rest**. The fix made each a Pattern B label:

- `ScrollActive = true`, `FitContent = false`, and a **bounded** `CustomMinimumSize` height (320 px founders, 240 px player). Bounded-smaller-than-content is exactly what makes it scroll (29.1); the fixed min height bounds it directly, so it works even though BTCWallet's panel has no outer `ScrollContainer`.
- **No row cap** — list every address; the internal scroll handles 100+.
- **Preserve the scroll position across the 2 s refresh.** Assigning `RichTextLabel.Text` snaps the internal scroll to the top (29.3 trap #5), which on a timer-refreshed list would yank the user back up every tick. Save `GetVScrollBar().Value` before setting `Text`, restore it after.
- **Trailing blank lines** (`"\n\n\n"`) so the last address clears the bottom edge (29.3 trap #4).
- **BBCode tags, not literal brackets.** With `BbcodeEnabled = true`, a literal `[base]`/`[change]` row prefix would be parsed as a (broken) tag. Use color-word tags instead: `[color=aqua]base[/color]`, `[color=gray]change[/color]` / `coinbase`. (The earlier `Label`-row version could use literal `[base]` because a plain `Label` doesn't parse BBCode.)

A shared "View empty addresses" `CheckBox` (default **unchecked**) filters spent/0-balance non-base rows in both screens — hidden by default (a real HD wallet keeps but never reuses them), revealed on tick.

### 29.8 — The transactions panel + per-address creation dates (Step 8)

Each non-bot wallet (BTCWallet, CasinoFinances, FoundersWallets) has a **"Transactions"** panel — another Pattern B scrollable `RichTextLabel` — listing the wallet's confirmed history from `NetworkRoot.GetNodeTransactionHistory(nodeId)`: one line per tx, newest-first, `date · ±amount · sent to/received from/mined`, color-coded (orange sent, lime received/mined). The address-book rows also gained a **creation date** on the right (`NetworkRoot.GetNodeAddressBook` now returns the first-seen block timestamp per address). Dates use the Block-Explorer convention: `DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm")`.

- **"Hide mining rewards" `CheckBox` (default checked)** — a mining wallet accrues *hundreds* of coinbase entries that bury the actual transfers, so coinbases are hidden by default with a "… N mining reward(s) hidden — untick to show" note. This is the opposite default polarity from "View empty addresses" (checked = hide), chosen because the noise is the common case for transfers visibility; each label is self-explanatory.
- Same scroll-preservation + trailing-blank-lines rules as the address book (29.7). FoundersWallets' panel is always-visible in Base mode (with the founder economics/dev panels); BTCWallet/CasinoFinances gate it behind a "Show transactions ▸" expander like the address list.

### 29.9 — Block Explorer display filter for OQ-8.2 bot change-to-self (P10 / cosmetic) — ✅ REMOVED (Step 16 P16.2f, 2026-07-30)

> **This section is HISTORY.** Both helpers and all their callers were deleted once every spending
> participant gained a `DerivedAddressWallet` (Step 16 P16.2, OQ-8.2 resolved). The Block Explorer now shows
> every transaction and every output exactly as it is on-chain. Kept here because the *reasoning* still
> matters: a cosmetic filter that hides a real output makes a real spend's arithmetic fail to add up (block
> 1274's nine-input bot bid appeared to lose 6.05 BTC), and a filter is only ever removable when the **last**
> participant that can produce the hidden shape is migrated.
>
> **What the removal check actually found (P16.2f):** the migration list everyone had been working from —
> casino-miner bots, cast miners, companies, ghosts-excluded — was **incomplete**. **Passphrase wallets**
> (`NetworkRoot.RegisterPassphraseWallet`) spend, had no `ReceiveWallet`, and were therefore the last
> single-address change-producer standing. They were found by asking *"who can still produce a
> change-to-self output?"* rather than by trusting the list. **General rule: when deleting a workaround,
> re-derive the set of cases it covered from the code — do not trust the scope list written when it was
> added.** Two related gotchas recorded with it: a passphrase wallet is created **mid-session**, so it
> misses the init-time `RescanDerivedReceiveWallets` and needs a single-node rescan at registration or its
> change-held funds go unowned; and ghosts stay keyless by design (D-14.11) but never spend, so they never
> blocked this.

**Context (as it was):** Bots were single-address — they have no `ReceiveWallet` (no persistent seed, OQ-8.2). Every bot spend therefore produces a change output addressed back to the bot's own input address. This is valid UTXO behaviour, but it creates visual noise in the Block Explorer: a bot donation to a non-miner shows two outputs, one external and one going straight back to the sender's address.

Two helpers in `BlockExplorer.cs` apply a **display-only cosmetic filter** until OQ-8.2 is resolved (bots get simplified seeds + `DerivedAddressWallet`):

- **`IsSelfChangeTransaction(tx)`** — returns `true` when *all* outputs go to addresses that were also inputs (a pure self-loop: no external recipient at all). Such a transaction is hidden entirely from the block lookup panel and the right-column preview.
- **`ExternalOutputs(tx)`** — returns only the outputs whose address is *not* in the input-address set. Applied to every non-coinbase tx in both display locations, stripping the change-to-self output while leaving the real recipient output visible. The count label (`Outputs (N)`) reflects only the visible outputs.

**What remains visible:** coinbases are never filtered (no inputs → `IsSelfChangeTransaction` is false). A bot donation to a non-miner still appears with `Outputs (1)` showing only the recipient — the change leg is invisible. Casino pool distributions and player/founder sends are unaffected (their change goes to a fresh derived address, so it never matches any input address).

**When to remove:** ✅ **DONE — Step 16 P16.2f (2026-07-30).** `IsSelfChangeTransaction`, `ExternalOutputs` and all their callers are deleted; the display shows all outputs correctly, as predicted.

### 29.10 — Persistent nav / footer buttons live OUTSIDE the scroll (Step 12 fix)

**Symptom (recurring):** a full-page scrollable screen's **Back / nav buttons overflow off the bottom** — you can click them but not read them. Seen on `ScFinances` (Step 12).

> **⚠️ Refinement (Step 12, SF.4D) — read together with §29.11.** The fixed-footer move below is correct practice and is **kept**, but on `ScFinances`/`ScTransactions` it turned out **not** to be the actual cause of *this* overflow: the tell was that `ScTransactions` overflowed **even with no active scroll** (content far shorter than the viewport can't be clipped by a scroll). The real cause was the **canvas bottom band falling off-screen** (§29.11). A fixed footer only stays readable if it *also* clears the bottom safe area — the two rules go together.

**Cause — two compounding mistakes, both in the same anti-pattern:**
1. **`MarginContainer → ScrollContainer` *directly*** (no intermediate `VBoxContainer`, no `size_flags_vertical = 3` on the scroll). This is **not** the canonical bounding chain of §29.1 (`MarginContainer → VBoxContainer → expand element`). It can still bound, but it's fragile and offers nowhere to anchor a fixed footer.
2. **The `NavRow` + `BackBtn` are the last children *inside* the scroll.** So they're only reachable by scrolling to the very end, and the scroll's max offset ends exactly at the content edge — the footer is flush-clipped (the button-analog of the §29.3 trap #4 "last line flush against the bottom edge").

`ScFinances` inherited this from its DEV mirror `CasinoGamblingFinances`. Its sibling `ScTransactions` (mirrored from `ClientsTransactions`) did **not** have the bug because it already used the correct pattern.

**Rule — persistent nav/Back buttons belong in a FIXED FOOTER, outside the scroll:**

```
RootMargin (MarginContainer, anchors_preset = 15)
  RootVBox (VBoxContainer)                     ← bounded by the margin
    [ StatusBar / fixed header, optional ]      ← fixed top
    ContentScroll (ScrollContainer,             ← the ONLY thing that scrolls
        size_flags_vertical = 3,                 ← expand: eats all space between header & footer
        horizontal_scroll_mode = 0)
      FormVBox (size_flags_horizontal = 3,       ← all the scrollable content
        mouse_filter = 1)
    [ HSeparator ]                              ← fixed footer …
    NavRow (HBoxContainer)                       ← … always visible …
    BackBtn (Button)                             ← … never clipped
```

The footer nodes are **siblings of `ContentScroll`, not children of it**, so they're pinned to the bottom of the bounded `RootVBox` and stay readable at any scroll position — **provided the footer also clears the bottom safe area (§29.11); a bottom-pinned footer sits exactly where the off-screen band bites.** `ScTransactions` is the reference implementation (list scrolls, `BackBtn` pinned).

**Reparenting is safe for the controller:** scene scripts resolve widgets by `%UniqueName` (`unique_name_in_owner = true`), which is path-independent — moving nodes between the scroll and the footer needs **zero** `.cs` changes as long as the unique names are preserved.

**Checklist addition (extends §29.5):** *7. Persistent nav/Back buttons go in a fixed footer OUTSIDE the scroll (bounded `VBox` → `ScrollContainer(size_flags_vertical = 3)` for content, then the footer row as a sibling). Never leave them as the last child inside the `ScrollContainer`.*

**Not-yet-fixed mirrors:** the DEV scenes `CasinoGamblingFinances` and `ClientsBetsHistory` still use the old inside-scroll-footer anti-pattern; fix them the same way if the overflow is observed (they're DEV-only, so left as-is for now).

### 29.11 — The canvas bottom band can fall off-screen (the real Step 12 overflow) — keep a bottom safe area

**This was the actual cause of the `ScFinances`/`ScTransactions` "Back button overflows the bottom" bug** — not the scroll (§29.10). Symptom that pins it down: the overflow appeared **even in a scene with no active scroll** (`ScTransactions`, few rows), so nothing was being *scroll*-clipped; the whole canvas bottom was simply not on-screen.

**Cause.** The project renders a fixed **1920×1080** design canvas (`project.godot [display]`: `viewport_width/height`, `stretch/mode = "canvas_items"`). In a **plain window** (the default, and how the editor's embedded Game view runs), the OS title bar + taskbar make the window taller than the visible area, so the **bottom ~30–70 px of the 1080 canvas is pushed off-screen**. Any control in that band is cut off — clickable but unreadable. Scenes are affected **only** when a control sits in that band; a content-driven Back button that floats higher (e.g. `BankrollProgrammer`, ~y 750) never notices, which is why only the new scenes showed it.

**The `size_flags_vertical = 3` trap.** An *expanding* child (a `ScrollContainer` or list set to Fill+Expand) eats all the free vertical space and **pins the following siblings to the very bottom** of the bounded area — driving a fixed footer straight into the off-screen band, *regardless of how little content there is*. So a fixed footer (§29.10) and a bottom safe area (this section) are a **pair**; doing only one leaves the button clipped.

**Fixes (both applied in Step 12 / SF.4D):**
1. **Window mode = Maximized for the shipped game.** `project.godot` → `window/size/mode=2` (Maximized; value **2**, *not* 3 = Fullscreen) so the client area fits the screen and the whole canvas is visible. **Caveat:** the editor's **Game embedding is disabled when the game starts maximized** ("Game embedding not available when the game starts maximized"). Keep embedding working with a **feature-tag override**: `window/size/mode.editor=0` (Windowed in the editor, Maximized in exports). Because the editor then runs Windowed, maximize alone does **not** fix the in-editor test view — hence fix #2.
2. **Bottom safe area.** Keep critical/interactive controls — **especially bottom-pinned footers** — out of the bottom ~50 px. In practice: set the page's `MarginContainer` `margin_bottom` to **~50** (Step 12 used 50; 70 was tried but felt too large). This lifts a bottom-pinned footer above the danger band and works in **every** context (windowed, embedded, maximized).

**Rule of thumb:** the game runs a fixed 1080 canvas with no guaranteed full-height window, so **treat the bottom ~50 px as unsafe**. Prefer running maximized/fullscreen, and never place a must-read/must-click control flush against the canvas bottom — give it a ≥ ~50 px bottom margin (or don't pin it to the bottom at all).

---

## Chapter 30 — UTXO Realism & Address Non-Reuse (Step 8)

Replaces the **testing-stage account/balance model** with a **real, multi-input/multi-output UTXO model** (Bitcoin's actual transaction model): a transaction spends a set of prior outputs ("coins") as **inputs** and creates new **outputs**; balance = the sum of an address's unspent outputs; the fee = Σinputs − Σoutputs. On top of that, every receive can land on a *fresh* derived address (Satoshi's "one address per block reward" practice) and every spend returns change to a *fresh* address. Built on the candidate engine (Ch. 21) and founder economics (Ch. 28). Full plan + decisions + test log: `AIHelperFiles/step8-utxo-realism-plan.md` (core phases 8.1–8.4 in §3; the full model is Appendix A, **now implemented**).

> **Terminology (Decision D0):** the many-addresses mechanic is **address non-reuse** / **one address per receive**, *not* "the Patoshi pattern." The real Patoshi pattern is a **mining-forensic** fingerprint (ExtraNonce/decrementing-nonce/timestamp artifacts) our engine can't reproduce — reserved for the optional, unbuilt Phase 8.5. Address non-reuse is a *wallet/privacy* practice and is what this step implements.

> **History (why two models):** core Step 8 (8.1–8.4) first shipped a **UTXO-lite** stand-in (single-input + a paired change tx) on top of the still-single-sender `Transaction`. The first real multi-address send that no single address could fund (a player consolidating many coinbase UTXOs) hit UTXO-lite's single-input wall — exactly the trigger the plan named — so the **full UTXO model (Appendix A)** was promoted and now supersedes UTXO-lite. This chapter documents the **implemented full model**; the UTXO-lite phrasing survives only in the historical plan notes.

### 30.1 — `DerivedAddressWallet`: the HD-lite core (Phase 8.1)

`Scripts/BlockchainPort/Blockchain/DerivedAddressWallet.cs` — pure C#, **no Godot, no chain reference, no persisted state**. A node that owns a seed phrase derives an unbounded, deterministic address book by index:

```
addr(0)    == the existing base address (empty suffix → fully back-compatible)
addr(i>=1) == CryptoUtils.DeriveGmAddress(seed + " #r" + i)
```

`i = 0` reproduces today's base address exactly, so genesis/bootstrap pins and existing balances are untouched; `i ≥ 1` are the fresh receive/change addresses. The `" #r"` namespace keeps these distinct from ordinary passphrase derivation.

- **No persistence (Decision D3):** "a block is the only commit to disk," so an app restart reverts the world to the last block. The wallet must therefore be **reconstructed from the chain**, never read from a side file. `Rescan(appearsOnChain, gapLimit)` derives `addr(0), addr(1), …` and walks until `gapLimit` consecutive unused indices (BIP44 convention = 20, OQ-8.4), setting `NextReceiveIndex` (first gap) and `OwnedAddresses` (the funded/used set). `NetworkRoot.CollectUsedAddressSet()` builds the on-chain address set in **one pass** so each probe is O(1).
- **⚠️ Bug fix — the used-address scan must read ALL inputs/outputs, not the shims.** `BuildUsedAddressSet`/`CollectUsedAddressSet` originally collected `tx.Sender`/`tx.Recipient`, which are the migration **shims** exposing only `Inputs[0]`/`Outputs[0]`. A **change** output lives at `Outputs[1]`, so the scan never saw change addresses — after a restart the rescan couldn't mark a node's change addresses owned, its change-held funds vanished from the wallet (the funds stayed on-chain, just unattributed), and the receive frontier reset (→ change-address reuse). Satoshi was masked because his funds sit on coinbase recipients (`Outputs[0]`); the change-rotating nodes (player, casino, Hal, Hearn) broke. **Fix:** iterate the full `Inputs`/`Outputs` lists. Same fix applied to `GetAddressConfirmedTransactions`. **General rule: never use the `Sender`/`Recipient`/`Amount` shims to scan the chain for address membership — they only see vin/vout 0.**
- `NextReceiveAddress()` returns `addr(NextReceiveIndex)` **without advancing**; `MarkReceiveConsumed()` advances the frontier when a rotated receive actually commits (a block mined / a change output created). `TryFindSpendingContext(address)` resolves the per-address keypair so any owned address can sign.

### 30.2 — The full UTXO transaction model (Appendix A — implemented)

**Data model** (`Models.cs`):

```
OutPoint { PrevTxId, Vout }                         // references a prior output (the coin being spent)
TxInput  { OutPoint Source; Address;                // Address = the consumed output's owner (denormalized)
           SignatureBase64; PublicKeyBase64; Secp256k1PublicKeyBase64 }   // per-input ownership + signature
TxOutput { Address; Amount }                          // its position in Outputs[] is its vout
Transaction { List<TxInput> Inputs; List<TxOutput> Outputs; Fee; Salt; TransactionId; InputData*; IsSpendable }
```

`Inputs`/`Outputs` are the **single source of truth**. The legacy `Sender`/`Recipient`/`Amount` (and the single signature fields) survive as **read-only computed shims** (`[JsonIgnore]`) so unported readers (Block Explorer, stats, scripted-activity scans) keep working: `Sender` = first input's address (or `"00"` for a coinbase), `Recipient`/`Amount` = first output. A **coinbase is an input-less tx** (`IsCoinbase => Inputs.Count == 0`); the old `"00"` sentinel is now only a display label.

**The UTXO set** (`BlockchainService`, A.3) is rebuilt by **replaying the chain** oldest→newest (`GetUtxoSet`, keyed `"txid:vout"`), cached and invalidated by a `_chainVersion` counter bumped on every chain mutation — never persisted (consistent with revert-to-last-block). Each entry carries its block height, `IsCoinbase` and `IsSpendable`. Balance / spendable / coin-selection all read from it: `GetAddressData` (confirmed mature spendable, excluding the unspendable genesis and immature coinbase) and `GetSpendableUtxos(addresses)` (the selectable coins, minus any reserved by a pending tx).

**Validation** (`AddTransactionToPendingTransactions`, A.4) — per spend: every input's outpoint exists in the confirmed set, is **unspent**, **mature** if coinbase, not **double-spent** by another pending tx, and its referenced output's address matches the input's recorded address; `Σinputs ≥ Σoutputs`; and `Fee == Σinputs − Σoutputs` exactly. Coinbases never enter the mempool (input-less are rejected there; they are minted only inside the block template). The old "a tx may not pay its own sender" rule is **dropped** — change to one's own (fresh) address is now legitimate and expressed as a distinct output.

**Signing** (`NodeAgent.BuildSignedSpend`, A.5) is **one signature per input**, each over the tx **sighash = its txid** (the content hash committing to every input/output/fee/salt), so inputs can be drawn from several owned derived addresses and nothing can be reshuffled. `ComputeTransactionId` (A.6) hashes the canonical input-outpoint + output-(address,amount) form; the Merkle leaf is unchanged (still = txid), so `MerkleTree`/header hashing needs no edit.

### 30.3 — One unified spend path (multi-input coin selection + change)

`NetworkRoot.BuildAndBroadcastUtxoSpend(sender, recipient, amount, fee, salt)` is **THE** spend path for *every* node — player, founders, bots, casino, scripted historical injector. It:

1. Gathers the sender's owned addresses (`ReceiveWallet.OwnedAddresses ∪ base`, or just base).
2. **Coin-selects** via `SelectUtxos`: prefer an **exact single-UTXO match** (amount+fee → no change; preserves the scripted exact-amount events); else **accumulate largest-first** until covered — *combining several UTXOs into one transaction* (the multi-input consolidation case).
3. Resolves each chosen UTXO's signing keys (`TryResolveInputKeys`: base → node keypair; derived → `ReceiveWallet.TryFindSpendingContext`).
4. Builds one signed tx: output 0 = the payee; output 1 = **change** = `Σselected − amount − fee`, to a **fresh** derived address (player/Satoshi) or the base (bots/casino — single-address reuse, acceptable for them).

This replaced the UTXO-lite `CreateSpendWithChange` / `TrySelectSpendSource` and the old per-node `CreateSignedTransaction*`. All the old send entry points (`CreateAndBroadcastTransaction`, `CreateAndBroadcastTransactionToAddress`, `SendFromCasino`, bot recirculation, `InjectHistoricalSignedTxStatic`) now funnel through it. The **scripted historical events** become single 1-in/2-out txs: E6 = Satoshi→Hearn 32.51 with **E8 = the 17.49 change as output 1** (no longer a separate tx); E7a/E7b/E4 likewise; idempotency stays **salt-based**.

### 30.4 — Player vs Satoshi: the one-line difference, and the UI

Both share the *same* spend path; they differ only in **coinbase routing**, governed by `NodeAgent.RotateCoinbaseAddress`:

| | Satoshi | Player |
|---|---|---|
| `ReceiveWallet` | yes | yes |
| `RotateCoinbaseAddress` | **true** | **false** |
| Coinbase lands on | a **fresh** derived address each block (`CoinbaseRecipient` → `NextReceiveAddress`, frontier advances via `OnCoinbaseCommitted`) | always the **base** address |
| Becomes multi-address by | **mining** (one address per block) | **spending only** (change → fresh address) |
| Topology | ~109+ addresses, one 50-BTC coin each (address non-reuse) | many coins piled on one base address |

So a Satoshi 5000-BTC send combines ~100 inputs across ~100 *distinct* addresses; a player big send combines many coins from *few* addresses — **identical coin-selection**, different UTXO topology. **The casino, Hal, and Mike Hearn share the player's profile** (`ReceiveWallet`, `RotateCoinbaseAddress = false` → change-only rotation, §30.7); only **Satoshi** spreads his coinbase (the "Patoshi" trait). Only the **bots** stay single-address (no `ReceiveWallet` — they have no stored seed, OQ-8.2).

**Spent addresses are never reused.** When a spend fully consumes an address's coins, that address keeps a `0.00000000` balance forever — the correct HD-wallet privacy behavior. Addresses are free (deterministically re-derivable), so the wallet always moves to a fresh index; the empty ones are kept only so the wallet still recognizes them, never handed out again.

**UI — every non-bot wallet (BTCWallet, CasinoFinances, FoundersWallets) shows the wallet *as a set of addresses* + its history (full layout rules in §29.7–29.8):**
- **Address book**: "Wallet total (N addresses)" = aggregate across the owned set + a scrollable list (`NetworkRoot.GetNodeAddressBook(nodeId)`), each row = tag (`base` / `change` / `coinbase` for Satoshi) + balance + **creation date** (the first-seen block's time). For Satoshi this renders his ~109 coinbase addresses, making the address-non-reuse spread legible.
- **"View empty addresses" toggle** (**default unchecked**): spent/0-balance non-base addresses are hidden by default with a "… N empty (spent) address(es) hidden" note; tick to reveal. The base address always shows even at 0.
- **Transactions panel** (`GetNodeTransactionHistory(nodeId)`): the wallet's confirmed history from its own perspective — `date · ±amount · sent to / received from / mined` — newest-first, color-coded. Internal change is netted out, not listed as a separate line. A **"Hide mining rewards" toggle (default checked)** hides the many coinbase entries so transfers stand out (untick to show). Founders' scripted historical txs still also appear in the separate **"Automatic Activity"** panel (Step 8.2 / OQ-8.6).
- These views are how the cross-session UTXO behaviour is *audited from inside the game* — they surfaced the §30.1 rescan bug (a node's change funds appearing in-session but vanishing after restart).

### 30.5 — Clean reset (no in-place migration)

The old account-model chain has **no input→output (UTXO) linkage**, so it cannot be replayed into a UTXO set. Rather than a fragile heuristic migration, the format change triggers a **clean reset** (`NetworkRoot.ResetWorldIfFormatChanged`, gated by `WorldFormatVersion`): on a version bump it deletes the chain state, the per-block checkpoint, the game clock (`calendar_state.json`) and the SC balance states, then writes the new version stamp — so the next launch re-bootstraps a pristine UTXO world from genesis to 21 Mar 2009. The block-history chunks (`blocks-*.json`) are wiped too. Idempotent (runs once per version).

### 30.6 — In-engine audit (post-implementation verification)

A 122-block run (player era reached, founders mining, a manual dev send) was audited directly from `state.json`:

| Check | Result |
|---|---|
| **Conservation** `Σinputs − Σoutputs == Fee` | ✅ holds on **every** spend |
| **Total supply** | ✅ 6150.2 BTC = genesis 50 + bootstrap 50 + 121×50 reward + 0.2 fees recirculated |
| **Double-spends** | ✅ **0** — every outpoint consumed at most once |
| **Address non-reuse (Satoshi)** | ✅ **109 distinct coinbase addresses** (one 50-BTC coin each; tracking toward the fractal ~220) |
| **Player / Hal coinbase** | ✅ **one** address each (spread is Satoshi-only) |
| **Coinbase structure** | ✅ input-less, single output, `salt = coinbase:N` |
| **Multi-input consolidation** | ✅ a **100-input → 1-output 5000-BTC** Satoshi→player tx: 100 *distinct* input addresses, each matching its referenced output exactly (0 mismatches) — the headline demonstration |
| **Scripted E4** (12 Jan, Satoshi→Hal 10) | ✅ 1-in/2-out: 10 to Hal + 40 change |

Integrity is impeccable: money is conserved, coinbase maturity is respected, no double-spend is possible, and multi-input consolidation works exactly like real Bitcoin. (The ~18 Apr Hearn round-trip / E6+E8 was not yet reached — the run was still in March 2009.)

### 30.7 — Generalising to casino, Hal & Hearn (done), bots (deferred), and sustainability

The engine is **already general** — `BuildAndBroadcastUtxoSpend` works for any node, and `ReceiveWallet` + `RotateCoinbaseAddress` are per-node config. Separate three capabilities by cost:

- **Multi-input spending (combine UTXOs)** — applies to **everyone** (shared path). Cost: trivial. Fully sustainable, no limit.
- **Change-address rotation** (the player's pattern) — cheap and general. **✅ Now also enabled for the casino, Hal Finney, and Mike Hearn** (`ReceiveWallet` seeded from their own phrase, `RotateCoinbaseAddress = false`): their coinbase/receives stay on the base address, and only the **change on send** rotates to a fresh derived address. Casino balance now **aggregates across its owned set** (it would otherwise hide post-send change), and CasinoFinances gained the same address-book + "View empty addresses" view as BTCWallet; FoundersWallets labels the derived rows **change** (Hal/Hearn) vs **coinbase** (Satoshi). Scripted **receives** still land on **base** — receive rotation is gated to `RotateCoinbaseAddress` (Satoshi only), so Hal's E4 10-BTC and Hearn's E6/E7 receives stay on base, and incoming deposit rotation stays deferred (OQ-8.3). **Hearn note:** his one outgoing tx, **E6b (Hearn → Satoshi 32.51)**, already flows through the unified UTXO spend path; it is an exact-match send (32.51 in → 32.51 out, **no change**), so his change rotation is inert today — the `ReceiveWallet` is for consistency/future-proofing. **Bots:** still blocked by **no stored seed** (OQ-8.2) — `DerivedAddressWallet` needs a seed to derive `addr(i)`; bots are created from random/registry keypairs, so each would need a generated, persisted seed.
- **Coinbase-address spread (full address non-reuse)** — the **cost driver** and the sustainability line. It is what multiplies the address universe (Satoshi alone ~220). Generalising it to all miners would be **historically wrong** (other early miners reused addresses) *and* would inflate the per-launch chain-rescan (D3, no persistence: `Rescan` derives SHA256 per index up to each rotating wallet's frontier+gap). **Keep it Satoshi-only** — simultaneously the faithful choice and the performance safeguard.

**Verdict:** sustainable at a general level *as long as coinbase spread stays Satoshi-only*. The thing to watch as the chain grows is the launch-time chain-replay rescan (today O(chain) cached + O(addresses) derivation — comfortable at fractal scale); if it ever bites, cache the frontier/UTXO set per session and re-scan only on a real revert. **Casino caveat:** the casino sends far more often than the player (one pool-payout `SendFromCasino` per contributor per reward event), and each change-bearing send mints one fresh change address — so the casino's address count (and thus its share of the rescan cost) grows fastest of all participants. Acceptable at current scale; it is the first place the per-session-cache mitigation would pay off. (Also note: the UTXO model already limits the casino to roughly one funding-source consumption per block until confirmation — a second same-block payout needs a *second* confirmed UTXO — but this predates and is independent of change rotation.) A separate future note: there is **no block-weight model** (`SizeVBytes = 1` uniform), so a pathological many-input tx isn't penalised — a per-input cost or auto-consolidation in low-fee periods would be the real-Bitcoin answer if fragmentation ever grows large.

### 30.8 — What's deferred

- **Phase 8.5** (the real Patoshi *mining-forensic* view) — documented only, **not built** in Basic Mode v1 (OQ-8.5).
- **Phase 8.6** (global D0 terminology rename across the remaining docs) — pending.
- **Bot multi-address** (OQ-8.2 — needs a per-bot seed), **player deposit-address rotation** (OQ-8.3), **separate receive/change derivation branches** (BIP44 external/internal — we use one index space), and **network-wide fee activation ≈ 2009-04-26** (OQ-8.7, its own branch) — all deliberately carried forward.

### 30.9 — The change-address donor incident (2026-07-14): an address is not a participant

**Context.** First calibration playtest of the ENTRY-2010 world after ND.7 (the combined auction + fees playtest). The developer reported that the referral auction had "started registering the player as a different participant": BlockExplorer Enroll Mode showed `leading bid gm1q…` — a raw, truncated address, one that was by then an **empty** address in the player's wallet — instead of the usual `player`.

**Root cause chain** (every link is normal, correct UTXO behavior — only the last link was wrong):

1. Change rotation (§30.3): the player's earlier sends/swap legs had moved part of their balance onto fresh **derived change addresses**.
2. Coin selection (largest-first): a later auction bid happened to spend a change-address UTXO, so the transaction's `Inputs[0].Address` — and therefore the legacy `tx.Sender` shim — was that change address, not the base address.
3. `ComputeAuctionLedger` recorded the donor as raw `tx.Sender`, and `DescribeAddress` only knew how to name **base** addresses (`node.WalletAddress`) — so the bid displayed as an anonymous address. (The address then showed as *empty* because the bid had spent that UTXO entirely — correct, and exactly why it looked so alien.)

**What was NOT broken — the half that matters.** Every auction *mechanic* already resolved identity through the full owned-address set (`BuildAuctionBidderIdentity` = base + `ReceiveWallet.OwnedAddresses`): bid **qualification**, the §22.8 one-satoshi **player floor**, and D-ND5.7 **settlement payout routing** all treated those bids as the player's. No money was ever at risk and no auction outcome differed. What broke was only the **recorded identity string** and everything display-shaped downstream of it: the raw-address leader label, a split `DonorCount`, and (had a settlement fired) one payout row per player address instead of one for the player.

**The fix** (both in `NetworkRoot`): (1) `ComputeAuctionLedger` now **canonicalizes** any donation sent from a player-owned address to the player's **base address** at record time — one participant, one donor identity; (2) `DescribeAddress` now also resolves any node's **derived** addresses (change rotation, Satoshi's coinbase spread) to the owning node's name. Single-address participants (`bot_1..4`, OQ-8.2) need no equivalent.

**The retroactive heal — why this incident earned a section.** Because the auction ledger is a **pure function of the chain** (nothing about it is persisted), shipping the fix healed the developer's existing world instantly: the very next recompute read the *same* blocks and reported the corrected identity. No migration, no save-file surgery, no world reset — the misattributed leading bid simply started reading `player` again on relaunch. This is the chain-derived model's signature advantage in action; Chapter 37 collects the pattern.

**The rule this hardens: an address is a key, not an identity.** Any system that attributes behavior to *participants* (auctions, future rank/referral systems, achievements, leaderboards) must resolve ownership through the participant's full address set — never by comparing `tx.Sender` / `Inputs[0]` to a single base address. This is the **second** incident of the legacy-shim bug class (the first: scanning `Sender`/`Recipient` for address membership missed change outputs at `Outputs[1]` — see CLAUDE.md's balance-model note). When the shims are finally deleted (post-OQ-8.2), both traps go with them.

## Chapter 31 — Casino SC Balance Sheet: Pre-Genesis Parity & the Loan Configuration Proposal

**Status (2026-07-02): Phases CG.0 + CG.1 + CG.1.8 + CG.2 + CG.3.A/B/C are ✅ IMPLEMENTED & manually verified. The authoritative final casino funding model is §31.1.1 (extra-lazy, on-demand loan) — it supersedes the concrete defaults/funding of §31.1 (Phase CG.0), kept for the pre-genesis-parity rationale. CG.2 (loan history + manual-loan input + game-date) is in §31.1.2; CG.3.A/B/C (Bankroll-recharge history panel, full timestamps on both panels, dev-configurable `AutoLoanAmount`) is in §31.1.3. §31.2's `AutoLoanAmount` shipped as CG.3.C; only `ManualLoanDefaultAmount` stays deferred. **Phase CG.3.D (planned, not yet built): a canonical change making the casino an exact mirror of an average player — `InitialLoanAmount` 100M → 40,000, `DefaultBankroll` dose 1M → 100 — so the casino's first funding produces the player's own 39,900 Main / 100 Bankroll split.** Checklists in `AIHelperFiles/player-and-casino-bankroll-programmer-plan.md`.**

### 31.1 — Mirroring the player's pre-genesis lifecycle onto the casino (Phase CG.0)

> ⚠️ **SUPERSEDED (funding mechanic + concrete defaults) by §31.1.1 — read that for the authoritative final behavior.** The pre-genesis-parity *rationale* below still holds (the casino mirrors the player's "block is the only commit" lifecycle, resets to canonical defaults on every pre-genesis restart, and closes the checkpoint gap). What changed: CG.0 funded the casino on the player's *first settled bet regardless of outcome* (`MainBalance` default `100,000,000`, split off on bet 1); the extra-lazy correction (CG.1.8, §31.1.1) instead starts the casino at **all-zero** and draws the loan **only on demand** — when a player win empties the Bankroll. Treat the `MainBalance=100,000,000` default and the `EnsureInitialCasinoFundingIfNeeded()` pseudocode below as historical. **All `100,000,000` / `1,000,000` / `99,000,000` figures in this §31.1 are the CG.0-era model — the current canonical loan/dose is `40,000` / `100` (CG.3.D; casino = mirror of an average player). See §31.1.1's canonical-update note and CLAUDE.md Canonical Decisions.**

Context: the player-side "block is the only commit" model (Ch. 24.8–24.9) was hardened to also cover the **pre-genesis** window — no player/bot/founder block has ever been mined, only the historical bootstrap has run. Before a real block, *every* restart resets Main Balance/Bankroll/dose/records to true canonical defaults; only mining a block makes progress durable. The casino's own SC balance sheet (`CasinoScBalanceService`) never received this treatment: it eagerly self-funds (`MainBalance=99,000,000` / `Bankroll=1,000,000` / `LoanCount=1` / `TotalLoaned=100,000,000`) the instant the app boots, regardless of whether the player has ever placed a bet, and its checkpoint restore (`RestoreCasinoScState`) only covers `MainBalance`/`Bankroll` — not `BankrollTarget`/`LoanCount`/`TotalLoaned`.

**The trigger is the player's first SETTLED bet, not a mined block** — deliberately different from the player's own trigger (`EnsureInitialBankrollFunded()`, keyed off `DiceGame._Ready()`/wallet balance). The casino's balance sheet only moves inside `ApplyBetResult()`, called once per settled player bet (`SimulationService`, `casinoDelta = -betEvent.CreditedProfit`) — so "has the casino ever been funded" is naturally keyed off the first call to that method, not off opening a scene. Both triggers ultimately answer to the *same* outer boundary though: `BlockSessionCheckpointService.HasCheckpoint()` (has a block ever been mined). Within a pre-genesis session — regardless of how the casino got funded (**case 1**: dev configures `BankrollTarget` in `CasinoGamblingFinances` before ever placing a bet; **case 2**: dev bets first, using whatever `BankrollTarget` was already in effect, normally the default) — closing the app *without* mining a block and reopening *always* reverts to the true pre-genesis state. There is no behavioral difference between case 1 and case 2 after such a restart; the only difference is which `BankrollTarget` value gets used to fund the casino *during* that particular pre-genesis session.

**New true defaults** (`CasinoScBalanceService`):

| Field | Old default | New pre-genesis default | Rationale |
|---|---|---|---|
| `MainBalance` | `99,000,000` | `100,000,000` (`InitialLoanAmount`, unsplit) | Mirrors the player: `PrincipalBalanceService` starts at the FULL `InitialPrincipalBalanceBaseline` (`40,000`), not the pre-split `39,900` — the split only appears once the dose actually moves. |
| `Bankroll` | `1,000,000` | `0` | Mirrors `BankrollStateService` starting at `0`. |
| `BankrollTarget` | `1,000,000` | `1,000,000` (unchanged) | This is the casino's "dose" — same role as `BankrollProgramService.AutoRechargeAmount`. Still resets to this default every pre-genesis restart (mirrors the rule from OQ-BP.9, Ch. 24.9). |
| `LoanCount` | `1` | `0` | Mirrors "no transfer records yet" for the player — the foundational loan hasn't *happened* yet in-world. |
| `TotalLoaned` | `100,000,000` | `0` | Same reasoning as `LoanCount`. |

**Pseudocode — the lazy first-bet funding** (mirrors `DiceGame.EnsureInitialBankrollFunded()`):

```csharp
// CasinoScBalanceService.cs
private void EnsureInitialCasinoFundingIfNeeded()
{
    if (LoanCount > 0) return;              // already funded this pre-genesis session

    LoanCount   = 1;
    TotalLoaned = InitialLoanAmount;         // 100,000,000 — materializes the foundational loan's bookkeeping
    decimal transfer = Money.Normalize(Math.Min(BankrollTarget, MainBalance));
    MainBalance = Money.Normalize(MainBalance - transfer);
    Bankroll    = Money.Normalize(Bankroll + transfer);
    // Once Phase CG.2's LoanHistory exists: AddLoanRecord(InitialLoanAmount, "startup");
}

public void ApplyBetResult(decimal casinoDelta)
{
    EnsureInitialCasinoFundingIfNeeded();     // <-- new: first line of the method
    Bankroll = Money.Normalize(Bankroll + casinoDelta);
    if (Bankroll <= 0m) TryAutoRecharge();
    Bankroll = Money.Normalize(Math.Max(0m, Bankroll));
    SaveState();
    BalanceChanged?.Invoke();
    // ...existing bet-count telemetry unchanged...
}
```

With `BankrollTarget` left at its default, this reproduces the familiar `99,000,000` / `1,000,000` split — but only from the player's first settled bet onward, not from app boot, and using whatever `BankrollTarget` was configured at that moment.

**Pseudocode — the pre-genesis reset** (mirrors `BankrollStateService`/`PrincipalBalanceService`/`BankrollProgramService`'s treatment in `BlockSessionCheckpointService.ResetToPreGenesisDefaults()`, Ch. 24.9):

```csharp
// CasinoScBalanceService.cs
public void ResetToPreGenesisDefaults()
{
    MainBalance    = DefaultMainBalance; // 100,000,000
    Bankroll       = 0m;
    BankrollTarget = DefaultBankroll;    // 1,000,000
    LoanCount      = 0;
    TotalLoaned    = 0m;
    // Once Phase CG.2's LoanHistory exists: _loanHistory.Clear();
    SaveState();
    BalanceChanged?.Invoke();
}

// BlockSessionCheckpointService.cs, inside ResetToPreGenesisDefaults()
GetNodeOrNull<CasinoScBalanceService>("/root/CasinoScBalanceService")
    ?.ResetToPreGenesisDefaults();
```

**Closing the checkpoint gap** — today `BlockSessionCheckpointService.Snapshot` only captures `CasinoScMainBalance`/`CasinoScBankroll`; a `BankrollTarget` change made *after* a real block, followed by a restart *without* mining another block, would incorrectly persist instead of reverting — the exact bug fixed for the player's own dose in OQ-BP.6 (Ch. 24.9, Bug 6). `Snapshot` needs `CasinoScBankrollTarget`/`CasinoScLoanCount`/`CasinoScTotalLoaned` alongside the existing two fields, and `RestoreCasinoScState(...)` needs the matching parameters.

Full implementation checklist: `AIHelperFiles/player-and-casino-bankroll-programmer-plan.md`, Phase CG.0.

**✅ Implemented & verified (2026-07-02).** All seven checklist items (CG.0.1–CG.0.7) shipped as designed above. Two things surfaced during implementation/testing, both now resolved:

1. **Manual bets never routed to the casino at all (pre-existing gap, exposed by lazy funding — OQ-CG.7).** The design above keyed "has the casino been funded" off the first call to `ApplyBetResult()`. But that method was only ever invoked by `SimulationService` — i.e. only for **autobet** player bets. **Manual** bets run through `DiceGame.ExecuteBet()` → the DiceGame-owned session, a completely separate path that never touched the casino SC sheet. Under the *old* eager-funding model this was invisible (the casino was already at `Bankroll=1,000,000` from boot); lazy funding made it visible as "place a manual bet, casino Bankroll stays `0`." **Fix:** `DiceGame` now holds a `_casinoSc` reference and `ExecuteBet()` calls `_casinoSc?.ApplyBetResult(-betEvent.CreditedProfit)` for player bets, mirroring `SimulationService`. No double-counting: while autobet is delegated to `SimulationService`, `ExecuteBet` is inert (`TickAutoBet` returns early on `_autobetDelegated`, and manual betting is disabled), so exactly one code path settles any given bet. *(`CLAUDE.md`'s `CasinoScBalanceService` description was updated 2026-07-02 to note `ApplyBetResult` is called by both `SimulationService` and `DiceGame.ExecuteBet`.)*

2. **`LoadState()` guards coerced the new `0` defaults back up.** The pre-existing guards `LoanCount > 0 ? … : 1` and `TotalLoaned > 0 ? … : InitialLoanAmount` treated `0` as "missing, use funded default" — the exact opposite of the new model where `0` is the legitimate pre-genesis value. Changed to `Math.Max(0, …)` so a persisted `0` survives the load. (This is belt-and-suspenders only: `BlockSessionCheckpointService` runs *after* `CasinoScBalanceService` in autoload order and always overrides the loaded state via `ResetToPreGenesisDefaults()` (pre-block) or `RestoreCasinoScState()` (post-block) — but leaving the guards wrong was a latent trap.)

Also note the `RestoreCasinoScState(...)` restore guards the three new fields with `> 0` checks so a legacy checkpoint captured *before* CG.0.6 (which lacks them → deserializes to `0`) doesn't zero out a funded casino's target/loan bookkeeping.

### 31.1.1 — The "extra-lazy" correction + cross-cutting fixes (Phase CG.1.8) — AUTHORITATIVE final model

Implemented & verified 2026-07-02. **Supersedes the concrete defaults and funding mechanic of §31.1**; the pre-genesis-parity rationale of §31.1 (mirror the player's lifecycle, reset on every pre-genesis restart, close the checkpoint gap) still holds.

**Motivation.** CG.0 funded the casino on the player's *first settled bet regardless of outcome* — booking the 100M loan and splitting off the Bankroll even when the player *lost* (the casino *gained* and needed nothing). The user wanted the casino to draw its loan and fill its Bankroll **only when it must pay a player win it can't cover**: on a losing streak the casino simply accumulates winnings in its Bankroll, with no loan and no recharge, and the P/L reads exactly the player's net loss.

**Final defaults — all zero pre-loan** (`CasinoScBalanceService`):

| Field | CG.0 (superseded) | CG.1.8 final | Why |
|---|---|---|---|
| `MainBalance` | `100,000,000` | **`0`** | No loan drawn until on demand — the 100M *is* the loan, so it can't sit in Main before the loan exists. |
| `Bankroll` | `0` | `0` | Accumulates player losses; refilled only when a win empties it. |
| `BankrollTarget` | `1,000,000` | `1,000,000` | The dose (auto-recharge target); still resets every pre-genesis restart. |
| `LoanCount` | `0` | `0` | No loan booked yet. |
| `TotalLoaned` | `0` | `0` | Same. |

`DefaultMainBalance` is now `0m`; `InitialLoanAmount` is retained purely as the **on-demand loan-draw chunk** inside `TryAutoRecharge()`.

> **CANONICAL UPDATE (CG.3.D, 2026-07-02):** the loan chunk (`InitialLoanAmount` / `AutoLoanAmount` default) is **`40,000`** and the dose (`DefaultBankroll` / `BankrollTarget` default) is **`100`** — *not* the `100,000,000` / `1,000,000` shown in the CG.0/CG.1.8-era text above. The casino is an **exact mirror of an average player**: loan `40,000` (a player's total start) + dose `100` (a player's Bankroll) ⇒ the first extra-lazy funding lands it at `39,900` Main / `100` Bankroll, the player's own split. All concrete examples below now use these figures. See Canonical Decisions in `CLAUDE.md`.

**Mechanics.**
- `EnsureInitialCasinoFundingIfNeeded()` was **removed**. `ApplyBetResult(casinoDelta)` reduces to `Bankroll += casinoDelta; if (Bankroll <= 0) TryAutoRecharge(); Bankroll = Max(0, Bankroll); SaveState(); BalanceChanged?.Invoke();` — so the loan/recharge path is reached *only* when a win drives the Bankroll ≤ 0.
- `TryAutoRecharge()` switched from **fill-to-target** to a **fixed dose** (CG.1.8.5). It injects exactly `BankrollTarget` per dose (drawing an `AutoLoanAmount` loan iff `MainBalance < a dose`), looping only while `Bankroll <= 0`. The winning payout that pushed the Bankroll negative is absorbed by the recharged Bankroll, **not** by Main — Main only ever loses one dose per injection (the old fill-to-target wrongly made Main pay `dose + payout overage`). Worked example (canonical CG.3.D figures — Bankroll `0`, player wins `10`): the casino draws a `40,000` loan (Main `0 → 40,000`), transfers one `100` dose (Main `→ 39,900`, Bankroll `= 90` = dose − 10); casino P/L `= (39,900 + 90) − 40,000 = −10`. The loop guarantees the Bankroll returns positive even if a single win exceeds a whole dose, so `ApplyBetResult`'s `Math.Max(0,…)` clamp never discards real SC (conservation preserved).
- The OQ-CG.6 P/L display guard was **removed**. With Main `0` pre-loan there is no phantom loan-chunk sitting in Main, so `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` is correct in every state (0 pre-bet; +winnings after a loss with no loan; real P/L after a loan) — and keeping the guard would have wrongly *hidden* the post-loss winnings while `LoanCount` is still 0.
- `RestoreCasinoScState(...)` gates the three CG.0.6 fields on `bankrollTarget > 0` (always true in a CG.0.6+ checkpoint, absent/`0` only in a legacy one) so a legitimately-zero `LoanCount`/`TotalLoaned` — a block mined during a pure loss streak — restores correctly instead of being skipped.

**Cross-cutting fixes found during CG.1.8 testing (both verified):**
- **Clock must stop AT the block on stop-on-block (OQ-CG.9).** When stop-on-block halted a run, the calendar kept advancing past the block — for a frame or two in autobet, one manual tick in manual — before the run fully stopped, and that drift got persisted, violating the canonical "clock == last mined block" rule (OQ-BP.9, Ch. 24.9). Fixed with `SimulationService.FreezeCalendarAtBlockStop()` (called after every stop-on-block `Stop()` — the player-mined path in `ExecutePlayerBetOnce` and the external-block path in `StopPlayerOnExternalBlockMined` — it sets `IsRunning=false` and persists, freezing the clock in place, which at that synchronous point still equals the value `CaptureCheckpoint()` just read); and in the manual path by skipping `AdvanceClockForBet()` when `LastStopReason == StopOnBlockMined`. The casino's on-demand loan (its loop + `GD.Print` + `SaveState` file I/O) had inflated the block frame's real-time latency, so the *next* frame's `delta` was large and the autobet overshoot grew to ~400 in-game seconds — the correlation the user observed.
- **Checkpoint captures the block-mining bet (OQ-CG.10).** `ExecutePlayerBetOnce` now settles the Bankroll autoload and the casino (`ApplyBetResult`) **before** `RouteNonceAttempt`/`CaptureCheckpoint`, so a block mined by bet K checkpoints the post-K balances — consistent with the bet-history boundary, which already included bet K (previously they were one bet stale). The manual path was already correct (casino applied before mining; Bankroll synced live via the `_wallet.BalanceDeltaChanged` handler during `ExecuteNext`).

**`CLAUDE.md` updated 2026-07-02** (`CasinoScBalanceService` section — three items now reflected there): (1) the casino no longer starts at "99M Main + 1M Bankroll from boot"; it is **0 / 0 / loan-count 0 / total-loaned 0** pre-loan, drawing the (canonical `40,000`, CG.3.D) loan on demand; (2) `ApplyBetResult` is called by **both** `SimulationService` (autobet) **and** `DiceGame.ExecuteBet` (manual play); (3) auto-recharge is a **fixed dose** (`BankrollTarget` per injection), not "target-to-fill".

### 31.1.2 — Loan history + manual loan + game date (Phase CG.2) — ✅ implemented 2026-07-02

`CasinoGamblingFinances` gained a **Bank Loans** section: a `LoanRecord` log (`Amount` / `Reason` `"auto"|"manual"` / game-time `GameDateLocal`) with a `TriggerManualLoan(amount)` path (type any amount → "Request Loan → Main Balance", blank ⇒ `InitialLoanAmount`), an `ItemList` history (newest first), and a live **game-date** label that ticks with the clock. Each on-demand `TryAutoRecharge` loan draw logs an `"auto"` record; manual requests log `"manual"`.

Persistence follows the project's rule verbatim: the history self-persists in `casino_sc_balance_state.json` **and** is snapshotted/restored at each block (`CasinoScLoanHistory` in `BlockSessionCheckpointService.Snapshot`, restored in `RestoreCasinoScState` inside the `bankrollTarget > 0` gate, in lockstep with `LoanCount`/`TotalLoaned`) — so "block is the only commit" holds: a loan drawn after a block but before a restart does not survive as a phantom list entry. Pre-genesis reset clears it. A `LoanCount`-vs-history mismatch (older checkpoints without the history field) surfaces as a `(+N pre-log)` note. The scene is wrapped in a `ScrollContainer` (pattern 1) so the growing panel stays fully reachable. Full checklist + notes: plan file, Phase CG.2.

### 31.1.3 — Bankroll-recharge history + full timestamps + configurable auto-loan amount (Phase CG.3.A/B/C) — ✅ implemented & verified 2026-07-02

**CG.3.A — Bankroll recharge history.** A "Bankroll Recharges" panel (mirror of the loans panel) logging every dose injected into the casino Bankroll: `RechargeRecord` (`Amount`/`Reason` `"auto"|"manual"`/game-time `GameDateLocal`). `"auto"` is logged for each `TryAutoRecharge` dose transfer; `"manual"` for each `Main Balance → Bankroll` transfer (`TryTransferToBankroll`). The list is capped at `MaxRechargeHistory = 500` (recharges are far more frequent than loans) and follows the project's persistence rule exactly — self-persisted **and** snapshotted/restored at each block (`CasinoScRechargeHistory` in `BlockSessionCheckpointService.Snapshot`, restored in `RestoreCasinoScState` inside the `bankrollTarget > 0` gate, in lockstep with the loan history) — plus cleared on pre-genesis reset.

**CG.3.B — full timestamps.** Both panels' list rows and the loan feedback now show `yyyy-MM-dd HH:mm:ss` (the records always stored game-time; display-only change).

**CG.3.C — configurable `AutoLoanAmount`** (delivers §31.2's first half). The dose drawn per on-demand auto-loan is now a dev-settable, persistent `AutoLoanAmount` (setter + "Set" button, mirroring `BankrollTarget`), replacing the hardcoded `InitialLoanAmount` in `TryAutoRecharge`. It follows the same extra-lazy/checkpoint rules (reverts to default pre-genesis; sticks only at a block — `CasinoScAutoLoanAmount` in the checkpoint). Design note: `TryAutoRecharge` keeps its single-draw-per-iteration loop (bounded by the deficit, not the target); if `AutoLoanAmount < BankrollTarget` the recharge under-fills and runs more iterations — recommend `AutoLoanAmount ≥ BankrollTarget`. `MaxAutoRechargeIterations` guards a pathological freeze. `OQ-CG.11` flags that `SaveState` runs per-bet and now serialises the (bounded) recharge history — debounce if per-bet I/O ever bites. Full checklist + notes: plan file, Phase CG.3.

### 31.2 — the "loan dosificador" mirroring `BankrollProgrammer`'s Auto-Recharge Dose (`AutoLoanAmount` ✅ shipped as CG.3.C; `ManualLoanDefaultAmount` still deferred)

**Status update 2026-07-02.** The ad-hoc **manual-loan text input** shipped in Phase CG.2 (§31.1.2). Of the two persistent *default dose* amounts this section proposed: **`AutoLoanAmount`** (the amount drawn per bankruptcy auto-loan) **shipped as Phase CG.3.C** (§31.1.3) — a dev-settable, persistent, checkpoint-committed value mirroring `BankrollTarget`. Only **`ManualLoanDefaultAmount`** (the manual-loan input's persistent pre-fill) stays deferred — the manual input already accepts any typed amount, so it is low value. The rest of this section is the original design writeup that CG.3.C built from.

**Scope — what this section is NOT about.** The ad-hoc **manual-loan text input** (`ManualLoanInput`/`TriggerManualLoan(amount)`) — where the dev types *any specific amount* and clicks "Request Loan" for that one loan — shipped in Phase CG.2. This section is only about making the **default/dose amounts** *persistently configurable*.

Originally deferred by explicit user decision (2026-07-01) until Phase CG.0 (§31.1) was implemented and confirmed stable ("the default start with 100 million SC we don't touch for now, to not complicate things further"). CG.0 is now stable, so `AutoLoanAmount` was picked up as CG.3.C.

**Today's limitation**: `TryAutoRecharge()`'s auto-loan amount (fired automatically when the casino's bankroll is exhausted) and `TriggerManualLoan()`'s **fallback/pre-fill** amount (used only when `ManualLoanInput` is left blank — the dev can already override it ad-hoc per CG.2) are both **always** `InitialLoanAmount` (`100,000,000`) — there is no persistent, dev-configurable *default dose* for either, unlike the player's `BankrollProgramService.AutoRechargeAmount`. `OQ-CG.3`/`OQ-CG.5` in the plan file already flagged this gap; this section is its elaboration.

**Proposed new fields** (`CasinoScBalanceService`), mirroring `BankrollProgramService.AutoRechargeAmount`:

```csharp
public decimal AutoLoanAmount          { get; private set; } = InitialLoanAmount; // dose for TryAutoRecharge()'s injections
public decimal ManualLoanDefaultAmount { get; private set; } = InitialLoanAmount; // pre-fills the manual-loan input

public void SetAutoLoanAmount(decimal amount) { /* validate > 0, Money.Normalize, persist, BalanceChanged?.Invoke() — mirrors SetBankrollTarget() */ }
public void SetManualLoanDefaultAmount(decimal amount) { /* same shape */ }
```

`TryAutoRecharge()` would use `AutoLoanAmount` instead of the hardcoded `InitialLoanAmount`:

```csharp
if (MainBalance < needed)
{
    MainBalance  = Money.Normalize(MainBalance + AutoLoanAmount);   // was InitialLoanAmount
    LoanCount++;
    TotalLoaned  = Money.Normalize(TotalLoaned + AutoLoanAmount);   // was InitialLoanAmount
}
```

`TriggerManualLoan(decimal amount)` (Phase CG.2) would fall back to `ManualLoanDefaultAmount` instead of `InitialLoanAmount` when the input is blank/invalid, mirroring `BankrollProgrammer`'s `TryProgrammedBankrollTransfer` fallback pattern.

**Both new doses follow the SAME "before first bet" rule as `BankrollTarget`** (§31.1) — configurable any time via `CasinoGamblingFinances`, but only "stick" for the current pre-genesis session, and only survive a restart once a real block commits them (extend `BlockSessionCheckpointService.Snapshot`/`ResetToPreGenesisDefaults()`/`ApplyCheckpointToServices()` with two more fields, exactly like `BankrollTarget` in §31.1).

**UI sketch** — original proposal (illustrative only). *Amounts shown are pre-canonical:* the shipped canonical defaults are auto-loan **`40,000`** / dose **`100`** (CG.3.D), and the shipped CG.3.C UI is simpler than this mockup — the `AutoLoanValueLabel` + "Set auto-loan amount" row live inside the existing "Bank Loans" section, and `ManualLoanDefaultAmount` was never built (deferred). Kept as the original design intent:

```
┌─────────────────────────────────────────────────────────────┐
│  Main Balance             100,000,000.00000000               │
│  Bankroll                    1,000,000.00000000               │
│  Bankroll Target              1,000,000.00000000  [ Set ]     │
│  ...                                                            │
├─ Loan Settings ─────────────────────────────────────────────────┤
│  Auto-Loan Amount            100,000,000.00000000              │  ← standalone value label,
│  [ Amount (SC)         ] [ Set Auto-Loan Amount ]               │     same font weight as
│                                                                   │     BankrollTargetValueLabel
│  Manual-Loan Default         100,000,000.00000000              │
│  [ Amount (SC)         ] [ Set Manual-Loan Default ]            │
├─ Bank Loans (Phase CG.2) ────────────────────────────────────────┤
│  Bank loans taken: 0   |   Total loaned: 0.00000000 SC          │
│  [ Loan amount (SC)    ] [ Request Loan → Main Balance ]         │
│  (loan history list...)                                          │
└─────────────────────────────────────────────────────────────┘
```

`.tscn` sketch (mirrors `BankrollProgrammer`'s `AutoRechargeRow` pattern exactly):

```
[node name="Sep_LoanSettings" type="HSeparator" parent="RootMargin/RootVBox"]
[node name="LoanSettingsLabel" type="Label" ...]              text = "Loan Settings"

[node name="AutoLoanAmountValueLabel" type="Label" ...]       # standalone, font 24 — mirrors AutoRechargeDoseValue
[node name="AutoLoanAmountRow" type="HBoxContainer" ...]
  [node name="AutoLoanAmountInput" type="LineEdit" ...]         placeholder_text = "Auto-loan amount (SC)"
  [node name="SetAutoLoanAmountBtn" type="Button" ...]          text = "Set Auto-Loan Amount"

[node name="ManualLoanDefaultValueLabel" type="Label" ...]    # standalone, font 24
[node name="ManualLoanDefaultRow" type="HBoxContainer" ...]
  [node name="ManualLoanDefaultInput" type="LineEdit" ...]      placeholder_text = "Manual-loan default (SC)"
  [node name="SetManualLoanDefaultBtn" type="Button" ...]       text = "Set Manual-Loan Default"
```

Handler pseudocode (`CasinoGamblingFinances.cs`), mirroring `BankrollProgrammer.OnApplyAutoRechargeAmountPressed()`:

```csharp
private void OnSetAutoLoanAmountPressed()
{
    if (!TryParseAmount(_autoLoanAmountInput.Text, out decimal amount))
    {
        _loanSettingsFeedbackLabel.Text = "Invalid amount.";
        return;
    }
    _casinoSc?.SetAutoLoanAmount(amount);
    _loanSettingsFeedbackLabel.Text = string.Create(CultureInfo.InvariantCulture,
        $"Auto-loan amount set to {amount:N8} SC.");
    RefreshLabels();
}
// OnSetManualLoanDefaultPressed() — identical shape, calls SetManualLoanDefaultAmount().
```

Note: unlike the player's `AutoRechargeAmount` (blocked from exceeding Main Balance — D13/BP.2.9), the casino has an infinite credit line, so **no such block applies here** — any positive amount is valid, matching `TryAutoRecharge()`'s existing "always succeeds" design.

Not addressed by this proposal (left for whenever it's actually scheduled): whether `LoanHistory` records (Phase CG.2) should note which dose was in effect at the time of each loan, and whether the two new value labels need their own standalone placement vs. being folded into `LoanSectionLabel`'s row — the sketch above assumes standalone, mirroring `AutoRechargeDoseValue`'s placement exactly.

### 31.3 — Bots as first-class casino clients (Step 14 ND.8f, 2026-07-19 — resolves OQ-11.1; supersedes every "per settled PLAYER bet" phrasing above)

Everything in §31.1–31.2 was written when only the player's bets moved the casino's balance sheet (Step 11 deferred bot routing as OQ-11.1). ND.8f closes that: **`bot_1..4` are the casino's other four canonical clients** — the same five-party set as the ND.8c genesis grants (§36.9) — and the casino now does their accounting identically. Where an earlier section says "per settled player bet", read "per settled **client** bet".

- **Every settled bet routes to `ApplyBetResult`** (`casinoDelta = −creditedProfit`): the player autobet (`SimulationService.ExecutePlayerBetOnce`), the **bot runners** (`ExecuteBotBet` — autobet ticks and manual bursts), DiceGame manual bets with the player **or a bot** active in the node selector, and the delegated-autobet-on-a-bot-node path (whose casino call already existed — its "player bets only" comment was the inconsistency; the routing is now correct semantics). The casino Bankroll therefore fluctuates with all five clients' play; more dose recharges/loans firing is the intended, correct consequence, and each loan draw still flows into the SC Monetary Ledger via the single `AddLoanRecord` funnel (§36.9).
- **Perf guard — no per-bet disk I/O.** Bots settle many bets per second in the background across all scenes, so `ApplyBetResult`'s per-bet `SaveState()` became a dirty-flag flush (0.5 s, `_Process`); the ledger's bet-stats updates flush at 1 s and fire **no** `LedgerChanged` (the per-client scenes poll on their own 2 s timer). Safe because a restart restores from the block checkpoint regardless — block = the only commit; the eager file is only a legacy fallback. Loans/transfers/setters keep their immediate saves.
- **`CasinoClientLedgerService` is multi-client for real**: a `CanonicalClients` list (player + `bot_1..4`); `EnsureCanonicalInitialDeposits()` gives every canonical client exactly one `"initial"` 40,000 entry (idempotent — at boot, after a checkpoint restore so a legacy checkpoint's entry list can't wipe the bots' migration entries, and on every pre-genesis reset, which now clears + re-registers all five); and a per-client **bet-stats book** (`ClientBetStats`: bets/wins/losses/wagered/net-profit, `RegisterSettledBet`) — the bots' stats source for `ClientsBetsHistory` (the player's row keeps reading `UserStatsService`). The book deliberately does **not** live on `NodeFinancialState`: `DiceGame.SaveActiveNodeFinancialState` rebuilds that DTO from the shared services on every save and would clobber stats fields.
- **Bot lifecycle entries**: `SimulationService.TryAutoRechargeBot` registers per-bot `"auto_recharge"` entries with wagered/profit snapshots from the book (the "P/L since last bankroll recharge" baseline, same shape as the player's `BankrollProgramService` registration); auction-settlement payouts to bots register `"auction_payout"` (previously player-only in `NetworkRoot.SettleResolvedAuction`).
- **Checkpoint coverage**: the book joins the ledger's checkpoint surface (`ClientBetStats` beside `ClientLedgerEntries` in `BlockSessionCheckpointService.Snapshot`; restored verbatim; a legacy pre-ND.8f null map keeps the loaded book; pre-genesis zeroes it). No new `user://` file (the book persists inside `casino_client_ledger.json`), so no delete-list change and no `WorldFormatVersion` bump.
- **Scenes**: `ClientsTransactions`' selector lists all five clients; `ClientsBetsHistory` renders one metrics row per client and "Total SC wagered (all clients)" literally sums all five sources. The live feed shows **all five clients' bets** (a developer-requested follow-up after the verification playtest): a typed `SimulationService.ClientBetSettled` event (`nodeId, gameId, BetTransactionEvent` — a C# event, not a Godot signal, since `BetTransactionEvent` isn't a Variant) fires per bot bet and per delegated player-autobet bet; the feed prefixes each row with the client's display name and keeps the 50-row cap (manual DiceGame bets can only occur while DiceGame is the active scene, so the feed can't miss them live). A **per-client filter dropdown** ("All Clients" + the five canonical clients) sits beside the game filter, mirroring its shape exactly — index 0 = all, index *i* > 0 maps to `CanonicalClients[i − 1]`, and changing either filter clears the feed (same behavior the game filter always had), so a single bot's play can be isolated out of the fast-scrolling all-clients stream.
- **Known limitation (`OQ-ND8f.1`, pre-existing, DEV-only)**: with a bot active in DiceGame's selector, a Bankroll recharge still flows through `BankrollProgramService`, which ledgers it as `"player"` (no active-node awareness) — left for a future selector-aware recharge pass; the bots' own background recharges attribute correctly.

Full spec + verification checklist: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md` §12.5.4.

---

## Chapter 32 — Player SC Finances Hub & the Private Bank Account (Step 12)

Closes the player↔casino symmetry opened by Step 11 (Ch. 31) — but on the **player** side the relationship is **ownership, not credit**. The casino borrows on demand (starts all-zero, draws loans); the player *owns* their money and gains an **optional savings reserve** they opt into. Full plan + decisions: `AIHelperFiles/step12-player-sc-finances-plan.md`.

### 32.1 — The three-account topology

```
┌─────────────────────────┐  deposit  Bank→Main (opt) ┌───────────── CASINO SC ACCOUNT ─┐
│  PRIVATE BANK ACCOUNT   │ ────────────────────────► │  MAIN BALANCE   ⇄   BANKROLL    │──► bets
│  (optional SC reserve)  │ ◄──────────────────────── │  (funded as today)  recharge     │
│  start: 0 SC            │  withdraw Main→Bank (opt) │  39,900 → 100 @ DiceGame entry   │
└─────────────────────────┘                           └──────────────────────────────────┘
        managed in ScFinances                                managed in BankrollProgrammer
```

- **Private Bank Account** (`PlayerBankAccountService.BankAccountBalance`) — an optional SC reserve **outside** the casino. **Starts EMPTY (`0`)** (D-SF3.1); the canonical `40,000` stays in the Casino SC Account, funded exactly as before Step 12 (no migration, no "extra-lazy" seeding — the abandoned v2/v3 model that seeded the whole `40,000` at the bank was dropped, §7.3 of the plan).
- **Casino SC Account** = Main Balance + Bankroll — the player's money **inside** the casino. Unchanged mechanics: `EnsureInitialBankrollFunded` still splits the current dose off Main at DiceGame entry (default `100` → `39,900`/`100`).
- One **bank** entity, two relationship types: it *lends* to the casino (credit/debt) and merely *holds savings* for the player (ownership, no debt).

### 32.2 — The four transfer flows (all built now; automation OFF by default)

| Flow | API | Direction | Default |
|---|---|---|---|
| Manual deposit | `TriggerManualDeposit(amount)` | Bank → Main (bring reserve into play) | — (on demand) |
| Auto deposit | `TryAutoDeposit(needed)` | Bank → Main (fallback refill) | `AutoDepositEnabled = false` |
| Manual withdrawal | `TriggerManualWithdrawal(amount)` | Main → Bank (park winnings safe) | — (on demand) |
| Auto withdrawal | `TryAutoWithdraw()` | Main → Bank (surplus sweep) | `AutoWithdrawEnabled = false` |

- **Limits** (D-SF.2): a deposit is capped by the bank balance, a withdrawal by Main. The UI (`ScFinances`) validates-then-**rejects** over-amounts with the available figure (D-SF2.5); the service `min(...)` clamp is the final safety net.
- **Auto-Deposit is a fallback, not the primary path** (D-SF3.3): it fires only when a recharge finds Main short **and** `AutoDepositEnabled` **and** the bank holds SC — essentially never in early game (empty bank, toggle OFF). Wired into both the autobet recharge (`SimulationService.TryPlayerAutoRechargeAndRestart`) and the manual-bet recharge (`DiceGame.TryAutoRechargeBankroll`): when Main < dose → `TryAutoDeposit(dose)` → retry. With the player opting in (banked reserve + Auto-Deposit ON at a valid amount, `0 < amount ≤ bank`), this fallback *is* the opt-in "extra-lazy" streaming.
- **Auto-Withdraw** (threshold/surplus, Model A): `effectiveFloor = max(AutoWithdrawThreshold, live recharge dose)` is the **anti-ping-pong guard** — an auto-deposit fires precisely when Main can't cover a dose, so auto-withdraw must never drain Main back below one dose. Moves one `AutoWithdrawAmount` installment per trigger event. This is exactly the shape `CasinoScBalanceService` can adopt for P6 debt repayments (one mechanism, two semantics: equity vs. repayment).

### 32.3 — Safe reserve vs. gamblable reserve (the Auto-Deposit trade-off)

With **Auto-Deposit OFF (default)** the bank is a *safe vault*: running Main+Bankroll to `0` stops betting and prompts a manual retrieval, but it is **not** game-over while the bank holds SC. With **Auto-Deposit ON** the reserve auto-refills Main when low — convenient, but the banked SC becomes gamblable. `ScFinances` must explain this in the UI (D-SF3.2). **Game over** is now total ruin across all three accounts: `Bank + Main + Bankroll = 0` (D-SF2.1), written to leave room for a future BTC→SC coin-swap rescue (plan §7.4).

### 32.4 — Metrics: NetWorthSc / OverallPl (computed in the controller, service stays pure)

`NetWorthSc = BankAccountBalance + Main + Bankroll`; `OverallPl = NetWorthSc − 40,000` (the canonical start — **not** `InitialBankAccountBalance`, which is `0`). Both are computed in the **`ScFinances` controller** from the three balance sources (D-SF2.7) — `PlayerBankAccountService` never reaches into the other two to expose a derived total. `BankrollProgramService.GetPerformancePercentVsInitial` still measures **Main Balance alone** vs `40,000` (relabeled in the `BankrollProgrammer` UI to avoid confusion with net worth).

### 32.5 — Ledger taxonomy fix (the `withdrawal` reclaim)

Before Step 12, `CasinoClientLedgerService` filed the internal **Bankroll → Main** movement under `kind = "withdrawal"` — a mislabel (GLOSSARY calls its mirror "not an SC Deposit"; by symmetry Bankroll→Main is not an SC Withdrawal). Step 12 reclaims `"withdrawal"` for its true meaning (**Main → bank**, SC leaving the casino) and re-kinds the internal movement to **`"bankroll_withdrawal"`**, excluded from "Total SC withdrawn" the way `"auto_recharge"` is excluded from deposits. A new **`LedgerEntry.Method`** (`"manual"` | `"auto"`, D-SF2.3) distinguishes automatic from player-initiated flows without new kinds, so every existing `Kind ==` filter keeps working. `ClientsTransactions` renders the method tag and hides both internal kinds; `ScTransactions` is the player's own view of the same flows.

### 32.6 — Lifecycle matrix (block = the only commit; pre-genesis resets everything)

The Private Bank Account and the client ledger are **player-facing persisted values**, so both were brought fully into the checkpoint lifecycle (D-SF2.4) — the same leak class §24.8–24.10 fixed for the other services. `ApplyCheckpointToServices()` now restores **six** services (adds `PlayerBankAccountService` via a `CheckpointState` DTO, and `CasinoClientLedgerService` entries); `ResetToPreGenesisDefaults()` clears the bank to `0` / settings to default / history empty, and clears the player's ledger entries + re-establishes the `initial` stake.

| Event | Private Bank state |
|---|---|
| App restart, no block ever mined | `ResetToPreGenesisDefaults()` — bank `0`, settings default, history empty; Main/Bankroll to canonical start as today |
| Block mined | `CaptureCheckpoint` snapshots the `CheckpointState` DTO (post-bet, inside the same group as the balances) |
| App restart, checkpoint exists | `RestoreFromCheckpoint(...)` — balance/settings/history revert to the last block |
| Legacy checkpoint (pre-Step 12) | DTO null → **seed bank at `0`, no migration** (D-SF2.8); Main restores its checkpointed value |

### 32.7 — Scenes, navigation, retirements

- **`ScFinances`** (player-facing hub, MainMenu + DiceGame's "Deposit Balance" button): balances (Bank/Main/Bankroll/NetWorth/OverallPl/dose), a compact 3-scope betting-stats panel (the shared `FinancialBettingStats`, §32.9), deposit & withdrawal sections (with the Auto-Deposit/Auto-Withdraw toggles + validated setters), and the `BankTransferHistory` list. Uses the **fixed-footer + bottom-safe-area** layout (§29.10–29.11).
- **`ScTransactions`** (→ from `ScFinances`): the player's own Bank↔Main flow history + header totals (deposited/withdrawn/net inside casino/net worth). No `[INITIAL]` row — the starting `40,000` is funded directly into Main, never a bank transfer (D-SF3.4).
- **`DepositPopup` retired**: DiceGame's Deposit button now opens `ScFinances`; `UI/DepositPopup/` deleted.
- **`SceneManager.PreviousScene`**: one-deep memory so `BetsHistoryExplorer`'s back button is origin-aware (returns to `CalendarsNavigator` **or** `ScFinances`).
- The **`AutoRechargeEnabled`** off-switch and its two access points (BankrollProgrammer toggle + the DiceGame StrategyControlPanel proxy) are documented in §25.8.

### 32.8 — Future: the `ScBank` scene (documented only)

The bank is deliberately deferred to *later* in-game (it starts empty, automation OFF), so Main↔Bankroll alone carries the first in-game months/years and the learning curve stays manageable. A future **`ScBank`** scene is where the bank finally *does something* with the player's equity: **fixed-term deposits** (freeze SC for a game-time span at interest, early-withdrawal penalty), a **savings rate** on the free balance, and casino-side **push factors** (minimum-wager / inactivity fees on idle Main) that together make the auto-withdraw toggle a genuinely strategic choice. `ScFinances` (flows) / `ScBank` (products) / `BankrollProgrammer` (casino-side doses) become the three siblings. See plan §7.1.

### 32.9 — Betting statistics: the shared 3-scope panel + the live-sync timer (SF.4B)

The `FinancialBettingStats` panel shows **three scopes**, each with **P/L** and **Gambled**: **General** (lifetime), **Since last bank deposit**, and **Since last bankroll recharge**. It is a compact, content-sized `VBoxContainer + GridContainer` so the *same* scene node drops into both DiceGame (absolute placement) and `ScFinances` (inside the scroll) unchanged.

- **Single source of truth = `PlayerFinancialStatsCalculator`** (`Scripts/History/`). A pure `Compute(UserBettingStats, CasinoClientLedgerService)` returns all six numbers; both host scenes render the *same* struct, so they are byte-identical by construction. Lifetime P/L/Gambled come from `UserStatsService.Stats`; the two "since-X" baselines come from the **client-ledger snapshots** (`GetLastDeposit` → kind `initial`|`deposit`, `GetLastAutoRecharge` → kind `auto_recharge`), each carrying the lifetime wagered/profit captured at that event. This deliberately does **not** use `UserBettingStats.ProfitSinceDeposit`, whose baseline is reset on every recharge (it conflates deposit with recharge). Player sign convention: P/L = **+**`TotalProfit` (the player's own gain), unlike `ClientsBetsHistory` which negates it for the casino.
- **Live-sync (why it also refreshes on a timer, not only on events).** `UserStatsService` throttles `StatsChanged` to 250 ms in high-frequency (autobet) mode and **defers the final batch** until the next bet or a `SetHighFrequencyMode(false)` flush. DiceGame recovers (it toggles that flag and re-`Refresh()`s on entry), but a **passive subscriber** — e.g. `ScFinances`, which never touches that flag — could be left showing the last throttled value when betting pauses, producing a *live-only* discrepancy between the two panels that self-heals on restart (the persisted data was always consistent). The fix: `FinancialBettingStats._Process` also calls `Refresh()` on a **0.75 s timer** (converge-to-live), in addition to the `StatsChanged`/`LedgerChanged` subscriptions — so the panel is correct in any host scene regardless of which events it caught. **The timer runs ONLY while `SimulationService.IsRunning`** (an autobet is actively advancing time): that is the only window where `StatsChanged` is throttled AND the only time stats move without a discrete player action — when idle, game time doesn't advance and every manual bet / deposit / recharge fires an *immediate* event, so the reconcile would be pure waste (the `Compute` reads `Stats` + two small ledger scans). **Rule for future live-data panels:** don't rely solely on a throttled event to stay current — add a cheap periodic reconcile *gated on the activity that causes the throttling*, or ensure every pause path flushes.
- The same panel also seeds DiceGame's **in-game bet-history list** from the centralized persistent store on entry (`UserStatsService.GetRecentBets`), so recent history reproduces on re-entry instead of starting empty.

---

## Chapter 33 — The Swap Desk's SC Auto-Floor: R2 (Implemented) and R3 (Documented Alternative)

The casino's swap desk (Step 13, `CasinoCoinSwapService`) lets the player trade SC for BTC and back — the desk itself gets its own chapter (Ch. 34). This chapter is scoped to one narrow but important question inside it: **how much SC should the casino always refuse to sell, no matter how attractive a swap looks?** Full plan: `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md` §2.3.

### 33.1 — The problem in plain terms

The casino's SC "Main Balance" does two jobs at once:

1. It is the **float that feeds the betting Bankroll** — every time a player win empties the Bankroll, `CasinoScBalanceService.TryAutoRecharge()` pulls one `BankrollTarget`-sized "dose" out of Main and drops it into the Bankroll so betting can continue uninterrupted.
2. Since Step 13, it is also **the SC the swap desk is allowed to sell for BTC** (Panel A: player pays SC, casino delivers BTC).

Those two jobs compete for the same pool of money. If the swap desk is naive and offers *all* of Main Balance, a big BTC purchase can drain Main to zero — and the very next time a player wins big enough to empty the Bankroll, there is nothing left to recharge it with. The betting loop would stall waiting on a dose that isn't there.

The fix is a **reserve**: a floor under Main Balance that swaps are never allowed to eat into. `OfferedSc = max(0, MainBalance − EffectiveReserve)` — only the surplus above the floor is ever offered (§2.2/§2.3 of the swap plan). The **manual** half of that reserve is a dev-set percentage or flat SC amount (`CasinoGamblingFinances`'s "SC swap reserve" knob) — a human has to guess a number and keep it updated as the game's economy grows. The **auto floor** is the interesting part: a formula that *computes* a sensible reserve from the casino's own recent history, so the dev doesn't have to guess. Two designs were considered for that formula — **R2**, now implemented, and **R3**, kept as a documented (not yet built) alternative.

Both compose with the manual reserve the same way `PlayerBankAccountService.TryAutoWithdraw` composes its threshold with the live recharge dose — **`EffectiveScReserve = max(manual reserve, auto floor)`** — so turning the auto floor on can only ever *raise* the protection, never lower whatever the dev already set by hand. This is the same anti-ping-pong shape documented in §32.2 for the bank account, reused here for the same reason: two independent safety nets should stack, not fight each other.

### 33.2 — R2: Recharge Pace (implemented, SW.5)

**The idea, in one sentence:** *look at how much SC the betting pace has actually drawn out of Main recently, and keep at least that much (times a safety margin) in reserve.*

**Why this is the natural first choice:** the whole point of the reserve is "don't sell SC the Bankroll is about to need." The Bankroll's need is not abstract — it shows up as a concrete, already-logged event every time it happens: an **auto-recharge**, i.e. one `BankrollTarget`-sized dose moving from Main to Bankroll. `CasinoScBalanceService.RechargeHistory` already records every one of these (amount, reason `"auto"` vs `"manual"`, and the **game-world** timestamp) — it existed before the swap desk did, capped at 500 entries, and needs no new plumbing. R2 just reads that list.

**The formula:**

```
AutoFloor = SafetyFactor × dosesConsumedInWindow × BankrollTarget
```

- **`dosesConsumedInWindow`** — the number of `RechargeHistory` entries with `Reason == "auto"` whose `GameDateLocal` falls within the last `WindowDays` of **game time** (never wall-clock — the whole game runs on an accelerated clock, consistent with every other timestamp in this codebase, CLAUDE.md Important Pattern 2). Each such entry represents one dose the betting pace genuinely needed — this is a *measured* fact, not a guess.
- **`BankrollTarget`** — the casino's own dose size (`CasinoScBalanceService.BankrollTarget`, defaults to 100 SC). Multiplying by it converts "how many doses were drawn" back into an SC amount.
- **`SafetyFactor`** — a dev-tunable multiplier (default **1.5**) that pads the raw historical draw with a margin, so the floor isn't shaved paper-thin against exactly what happened last window — a plausible worse-than-average burst is still covered.
- **`WindowDays`** — how far back to look, in whole **game-time** days (default **1**; originally shipped as raw hours, switched to whole days after dev feedback 2026-07-07 — days are the natural unit a dev reasons in). A short window reacts fast to a hot losing streak but forgets it fast too; a long window smooths over noise but reacts slower to a genuine change in pace.

**Worked example.** Say `BankrollTarget = 100 SC`, `SafetyFactor = 1.5`, `WindowDays = 1`, and in the last in-game day the Bankroll needed recharging 6 times (a rough losing patch for the house). Then:

```
AutoFloor = 1.5 × 6 × 100 = 900 SC
```

If the dev's manual reserve was set to, say, 500 SC, the *effective* reserve is `max(500, 900) = 900` — the desk automatically holds back more than the manual setting because recent play has shown it needs to, without anyone having to notice and adjust the manual number.

**Where it lives:** `CasinoCoinSwapService.ScAutoFloor` (a computed property, re-evaluated live — it is not cached/frozen at the last knob change) composes into `EffectiveScReserve` exactly as described in §33.1. The DEV toggle ("Auto floor (R2, recharge pace)", plus the `SafetyFactor` and `WindowDays` SpinBoxes) sits in `CasinoGamblingFinances`, directly beside the manual SC reserve selector it composes with (D-SW.9 — every swap-desk DEV knob lives in the two existing casino DEV scenes, never in `CasinoCoinSwaps` itself). Default **OFF**, like every other swap-desk knob — the dev opts in once ready to test it.

**Cost / complexity: small.** No new state, no new subscriptions, no new persisted list — it is a pure read over data that already exists and was already checkpoint-covered before Step 13 began. This is why it was the recommended first implementation (§2.3 of the swap plan) and why it shipped in SW.5.

**A known coarseness (why R3 exists as an alternative).** R2 only sees *whole recharge events* — it has no idea whether a single recharge happened because the Bankroll dripped down slowly over hundreds of small losing bets, or because one enormous win emptied it in a single bet. Both look identical to R2 (one `RechargeHistory` entry each), even though the second case might represent a much larger, faster swing in the casino's exposure. If testing ever shows R2's floor feels "too coarse" — reacting to the wrong signal, or lagging behind real risk — R3 is the documented next step.

**Usability follow-up (dev feedback, 2026-07-07 — after using the selector).** Two gaps surfaced once the toggle was actually used, both fixed without touching the formula: (1) `SafetyFactor` alone is unreadable in isolation — the same value produces very different absolute SC floors depending on `dosesConsumedInWindow` and `BankrollTarget`, so `CasinoGamblingFinances` now shows a **live breakdown line** ("Preview: 1.5 safety × 5 dose(s) consumed in last 1 day(s) × 100.00000000 SC (BankrollTarget) = 750.00000000 SC") that updates as the SpinBoxes move, before Apply, via a new parameterized `CasinoCoinSwapService.GetScAutoFloorDosesConsumedFor(windowDays)`; the SafetyFactor SpinBox's original `max_value = 20` was also an arbitrary UI choice with no formula backing, raised to `1000` since the breakdown is what actually informs the dev now. (2) Running the manual reserve and the R2 auto floor at the same time was confusing because nothing showed which side of `max(manual, auto)` was binding — the swap-desk info line now appends `[auto floor binds]` / `[manual reserve binds]`; the composition itself is unchanged.

### 33.3 — R3: Drawdown-Based (documented alternative — NOT implemented, no plan yet)

**The idea, in one sentence:** *instead of counting how many times the Bankroll got refilled, measure directly how far the Bankroll's balance actually swung down from its recent peak, and keep enough reserve to absorb a swing that size again.*

This is the concept of a **drawdown** borrowed from finance/trading risk management: if a balance climbs to a high point and then falls, the drawdown is the size of that fall (peak minus trough), *before* it recovers. It answers a subtly different question than R2's dose-count: R2 asks "how often did we need to top up," R3 asks "how deep did the hole get before we topped up." A single catastrophic loss that empties the Bankroll in one bet produces a *big* drawdown but only *one* R2-counted dose — R3 would see the danger that R2 undercounts; conversely, many tiny doses drawn during a long, shallow losing drip produce a *small* per-event drawdown each time but a large R2 dose-count — R3 would size the floor smaller than R2 in that case, correctly recognizing that no single swing was ever actually dangerous.

**The formula:**

```
AutoFloor = k × maxBankrollDrawdown(last M bets)
```

- **`maxBankrollDrawdown(last M bets)`** — track the Bankroll's balance over a trailing window of the last `M` bets (or, alternatively, the last `W` game-days, mirroring R2's day-based windowing), and compute the largest peak-to-trough drop observed in that window. This is the standard "running max, running max-drawdown" pattern: keep a running high-water mark of the balance; every time the balance sits below that high-water mark, compare the gap to the largest gap seen so far.
- **`k`** — a dev-tunable multiplier (R3's equivalent of R2's `SafetyFactor`), sizing the floor as some multiple of the worst recent swing rather than exactly matching it.

**Worked example (illustrative, not implemented).** Suppose over the last 500 bets the Bankroll's balance history looked like: starts at 100, climbs to 340 (a hot streak), then a bad run drags it down to 60 before recovering — that is a drawdown of `340 − 60 = 280 SC`. Later in the same window it climbs to 200 and drops to 150 (a drawdown of only 50). The *max* drawdown in the window is the larger of the two: 280 SC. With `k = 1.2`:

```
AutoFloor = 1.2 × 280 = 336 SC
```

**What building it would require (the honest cost comparison with R2):** unlike R2, this data does **not** already exist anywhere in the codebase. It would need:

1. **A new ring buffer** inside `CasinoCoinSwapService` (or a small new helper it owns), subscribed to `CasinoScBalanceService.BalanceChanged`, sampling the Bankroll balance on every change (or every settled bet) and retaining the last `M` samples.
2. **Running high-water-mark / max-drawdown bookkeeping** over that buffer — cheap per-sample, but it is new logic that has to be gotten right (in particular: what happens at a restart, given "a block is the only commit" — CLAUDE.md Important Pattern 2 — means this buffer, like the pending BTC deliveries in §4.4 of the swap plan, would almost certainly need to be **in-memory only, never persisted**, and would legitimately reset to "no history yet" on every restart until it re-accumulates from live play).
3. **A choice of windowing units** (bet-count `M` vs. game-hours `W`) that R2 didn't have to make, because `RechargeHistory` is already timestamped and R2 just filters it.

This is why the swap plan calls R3 "medium cost — new telemetry" versus R2's "small — pure read," and why R2 was built first. R3 is not hard, conceptually — it is a well-understood pattern — but it is genuinely new plumbing, not a re-read of data that already exists.

### 33.4 — Choosing between them (for whoever writes R3's implementation plan later)

| Question | R2 (recharge pace) | R3 (drawdown-based) |
|---|---|---|
| What it measures | *How often* the Bankroll needed refilling | *How far* the Bankroll actually fell before recovering |
| Data source | `CasinoScBalanceService.RechargeHistory` (already exists) | A new balance-sample ring buffer (does not exist yet) |
| Blind spot | Can't tell a big single loss from many small ones — both look like "N doses" | None of R2's blind spot, but needs its own tuning of `M`/`W` and `k` |
| Cost to build | Already done (SW.5) | New subscription + buffer + drawdown bookkeeping + restart-safety design |
| Reacts fastest to | A *change in frequency* of recharges | A *single large swing*, even if it's the first one in a long time |
| Good fallback signal if | The betting pace is roughly steady-sized bets | Bet sizes vary a lot, or whales/streaks matter more than frequency |

**When to actually build R3:** only if real testing sessions (using the `swap_desk_trace.csv` telemetry the swap plan already logs, §2.4) show R2's floor either reacting to the wrong signal (e.g. staying low through a single huge loss because it was "only one dose") or lagging behind genuine changes in the casino's risk exposure. Until that evidence exists, R2 stays the shipped default and R3 remains this chapter — a fully-explained idea, deliberately **not** turned into an implementation plan yet, so that whoever picks it up later can go straight from "why" to "how" without re-deriving the reasoning above.

### 33.5 — Testing notes

Both the manual reserve and R2's auto floor can be exercised without waiting for organic play: `CasinoScBalanceService.RechargeHistory` fills up naturally the moment bots/player start losing bets fast enough to empty the Bankroll repeatedly (see the funding-timeline note in the swap plan, SW.1). To see the auto floor move, toggle it on in `CasinoGamblingFinances`, watch the "Auto floor (R2)" readout in the same panel's info line update as `RechargeHistory` accumulates auto entries, and confirm `EffectiveScReserve`/`OfferedSc` on the `CasinoCoinSwaps` scene track it live (`ScAutoFloor` is a computed property, so it is always current — no stale cache to worry about). Widening `WindowDays` or lowering `SafetyFactor` should visibly shrink the floor for the same history; the reverse should grow it — the live breakdown line makes this immediately visible before Apply.

---

## Chapter 34 — The Swap Desk UI: Reactive Dual Inputs, the Reverse-Quote Math, and the Exact-Match Rounding Fold-In

Chapter 33 covered one narrow piece of the swap desk (the SC reserve's auto-floor); this is the "desk itself" writeup — the `CasinoCoinSwaps` scene's two-panel trading UI, once it grew a second input per panel and the rounding subtlety that came with it (dev feedback, 2026-07-07). Full plan: `AIHelperFiles/step13-sw-casino-coin-swaps-plan.md`.

### 34.1 — Two inputs per panel, reactive in both directions

Each panel (A: "Buy BTC," SC→BTC; B: "Sell BTC," BTC→SC) originally had one input — "how much will you pay/send." A second field was added — "how much do you want to receive" — and the two stay in sync automatically: typing in either one recomputes the other.

- **Forward direction** (pay → receive): the existing `QuoteScToBtc`/`QuoteBtcToSc` methods, unchanged.
- **Reverse direction** (receive → pay): two new methods, `QuoteScToBtcForReceivedBtc` and `QuoteBtcToScForReceivedSc`. Each *inverts* the forward quote's fee curve to answer "what would I need to pay to net exactly this much?", then **replays the result through the ordinary forward quote** rather than duplicating any validation logic. This is the load-bearing design decision in this chapter: the reverse quote can never disagree with the forward one, and every clamp/`IsValid`/`MaxLimitedBy` rule is evaluated in exactly one place. A desired receive amount the casino or player genuinely cannot afford surfaces the same "exceeds max" state a forward-direction overpay would.
- The inversion itself reuses `BaseFromNet` (named `MaxGrossForNet` before the D-SW.11 fee-model redesign, §34.4a) — a helper originally built for the Max-input clamp math (§4.3's "how much could the casino/player afford at most"). It turned out to be mathematically *exact*, not merely an upper bound: for a given fee, it returns the one base amount whose net equals the target precisely. Under the current additive fee model this is a single linear formula (no piecewise region-splitting needed); that is what makes reusing it for "solve for the input given a desired output" valid instead of just approximate.

### 34.2 — Avoiding a clobber bug: which field is the "source" right now

Two inputs that both write to each other raises an obvious risk: could updating one accidentally erase what the user is still typing into the other? Two design choices prevent this:

1. **Setting `LineEdit.Text` from code does not raise `TextChanged`** in this Godot binding (confirmed by the pre-existing MAX-button code, which already needed a manual `EmitSignal` call to "replay" a fill through the normal handler — a clue that was there before this feature and is now explained). Syncing the *other* field with a plain `.Text =` assignment is therefore inert and cannot recurse into an infinite loop.
2. **A per-panel "last edited" flag** (`_panelALastEditedReceive` / `_panelBLastEditedReceive`) tracks which field's `TextChanged` fired most recently. The periodic 2-second refresh and every event-driven refresh (`SwapDeskChanged`, `BalanceChanged`, market-day change) recompute from whichever field is the current *source* — never the field the user might be actively typing into, which is only ever written *to*, never read from, by the opposite path. This was caught and fixed before shipping, not discovered by the user testing it — worth internalizing as a general rule: **any time a UI syncs two input fields bidirectionally, a periodic/event-driven refresh must know which field is the source, or it will eventually clobber live user input.**

### 34.3 — The exact-match rounding fold-in (why a 10 BTC request could return 9.99999996)

**The symptom.** Typing "10.00" into Panel A's receive field could return a quote whose `NetOut` was `9.99999996` — four satoshi short of what was typed.

**The cause.** The reverse solve (§34.1) computes an exact, high-precision gross BTC amount via `BaseFromNet`, then converts it to an SC amount via `Money.Normalize(gross × price)` — an 8-decimal rounding step, per this project's Money Handling convention (CLAUDE.md). That rounded SC amount is then run back through the *forward* quote, which independently re-derives its own gross BTC (`SC ÷ price`, rounded again) and fee. Two independent 8-decimal roundings, straddling a division/multiplication by `price`, do not perfectly cancel — the classic "convert, round, convert back" precision loss. (This rounding behavior is unaffected by the D-SW.11 fee-model redesign — it is a property of the SC↔BTC currency conversion, orthogonal to whether the fee curve is linear or piecewise.)

**Why it's worse at low prices, and why it's still not economically material.** A single rounding step can be off by up to half a unit in its own last decimal place (≤ 0.5×10⁻⁸ of whichever currency it rounds). When that SC-side error is reflected back in BTC terms, it is effectively divided by `price` — so a *low* price (as in the earliest, lowest-price days of the simulated market, e.g. ≈0.0679 SC/BTC at landing) *amplifies* a fixed SC rounding error into a larger-looking BTC error: `error_BTC ≈ error_SC ÷ price`. At price ≈0.0679, a 0.5×10⁻⁸ SC rounding step becomes ≈7×10⁻⁸ BTC (≈7 satoshi) once reflected in BTC — in the same ballpark as the 4-satoshi gap actually observed. As the simulated price rises through the historical dataset (into the thousands of dollars per BTC in later years), the same SC-side rounding step shrinks toward a negligible fraction of a satoshi in BTC terms. In absolute terms, even the worst case is minuscule next to any legal swap size (bounded below by the §3.2 minimum, itself many multiples of a satoshi) — so this was never an economically material bug, but it *was* a real, deterministic shortfall against the exact number the player typed, worth fixing on principle.

**The fix — nudge the pay side up, never shortchange the receive side.** Both reverse-quote methods now follow their one-shot estimate with a small bounded loop (`MaxExactMatchIterations = 10`, converges in 1–3 iterations in every observed case): while the forward-replayed quote's `NetOut` is still short of the desired amount, bump the pay-side input up by the shortfall's cross-currency equivalent (or one satoshi, whichever is larger) and re-quote. The loop terminates either when `NetOut` reaches or exceeds the target, or — for a desired amount the casino/player genuinely cannot afford — once the bump pushes the quote past the real `MaxInput`, at which point it correctly reports invalid with the right `MaxLimitedBy` reason instead of looping pointlessly.

**Where the tiny difference ends up, and why there is no new UI line for it.** The (usually zero, occasionally 1–4 satoshi) surplus this nudge introduces is folded silently into the **pay amount** — it is not, and should not be, surfaced as its own labeled line item anywhere in the panel. There is nothing coherent to call it: it is not casino margin (§34.4's fee breakdown accounts for that separately), not the network fee, and not a rank-based adjustment — it is pure decimal-precision residue from a currency-conversion round-trip, and inventing a UI category for it would suggest it means something it doesn't. It is real (it appears in the total SC actually charged), just not worth a dedicated line — this paragraph is that explanation, satisfying "the player always gets at least what they typed" without pretending the game has invented a new kind of fee.

**A second, related bug this same rounding class produced (2026-07-08): the MIN buttons.** The minimum-swap-size figure (`MinSwapGrossBtcFor`) is derived algebraically assuming exact arithmetic — but `Money.Normalize` **truncates** (`Math.Round(value, 8, MidpointRounding.ToZero)` — it never rounds up, only down in magnitude), and the real pipeline truncates *three times in a row* computing a quote (`grossBtc`, `feeBtc`, `netBtc`, each its own `Money.Normalize`). Those compounding truncations shaved the analytically-exact "net = +1 satoshi" at the computed minimum down to a NEGATIVE or exactly-zero net in practice — so the displayed "Min" figure, and the MAX/MIN-button fill value derived from it, failed the panel's own `net > 0m` validity check (showed invalid/orange), and the receive-field MIN buttons did nothing at all (their fill amount was that same ≤0 `NetOut`, silently swallowed by the fill helper's positive-amount guard). **Fix**: don't trust the algebraic minimum — verify it against the real (truncating) core math and nudge it up by one satoshi at a time (bounded, same pattern as this section's exact-match nudge) until it actually produces a positive net. **General rule this confirms**: in this codebase, never assume an algebraically-derived boundary value survives a multi-step `Money.Normalize` pipeline unchanged — `Money.Normalize` truncates, it does not round to nearest, so a value calibrated to land EXACTLY on a boundary (e.g. `net = 0` or `net = OneSatoshi`) needs to be verified against the real pipeline, not trusted from algebra alone, whenever more than one `Money.Normalize` call sits between the derivation and the value's use as a validity threshold.

**A third revision, same day: the minimum swap size itself was redefined again, from "net > 0" to a VALUE floor.** After the MIN-button fix above shipped, dev testing of the "net > 0" floor found it economically absurd in practice — Panel A's minimum purchase netted back a mere `0.00000008 BTC` while paying almost the entire amount in fees ("es absurdo pagar tantos fees para cambiar una fraccion"). The floor was redefined to require `net(base) ≥ totalFee(base)` — the player must net back *at least* as much as they pay in total fees, not merely a positive amount. Solving `net(base) = totalFee(base)` gives `base = 2×NetworkFeePolicy.MinFee×(1+fee)/(1−2×fee)` — **≈0.275 BTC at the 10% default** (up from the "net>0" floor's ≈0.1222 BTC, still far below the original D-SW.1 flat 1.0 BTC). The MIN-button truncation fix above still applies unchanged in spirit — `FindMinScInput`/`FindMinBtcInput` just nudge toward `net ≥ fee` now instead of `net > 0` — but one consequence of the new floor is NOT fee-independent the way the old one was: `MinDeliverableBtc`/`MinScPayoutAt` (the panels' enable thresholds) must now read the live `SwapFeePercent`, since the minimum swap's net delivery is `minGross/2`, which moves with fee%, rather than a fixed `OneSatoshi`.

**A fourth bug, same day: Panel B's MIN button still failed after the value-floor fix — this time in the Max clamp, not the Min.** With the value floor in place, Panel A's MIN button worked, but Panel B's still showed invalid. The cause was a DIFFERENT instance of the same "algebra doesn't survive independent truncation" class of bug as the MIN-button fix above, but on the *opposite* boundary: `casinoMaxBtc = BaseFromNet(OfferedSc/price, fee)` and the panel-enable gate's own `MinScPayoutAt(price)` threshold are two INDEPENDENT derivations of "can the casino afford the minimum swap," each truncated separately — algebra proves they should agree exactly, but verified numerically (PowerShell) that `casinoMaxBtc` can land a few satoshi BELOW `minGross` right when the casino's offered balance is barely above the minimum (the exact scenario a MIN-button press exercises). An "adaptive proportional-jump" fix that tried to make the Max-clamp itself truncation-exact (mirroring the MIN-button's nudge, climbing instead of descending) was prototyped and abandoned — it failed to converge at very low BTC prices, where a single BTC-satoshi moves the SC-side net by less than one SC-satoshi, requiring thousands of nudge iterations (verified this failure numerically before dropping the approach). The shipped fix is much simpler: since the panel's own enable gate already proves a legal minimum swap exists, floor the casino-side Max estimate at the (already truncation-safe) `MinInput` — `casinoMaxSc = Math.Max(BaseFromNet(...)×price, minSc)`, `casinoMaxBtc = Math.Max(BaseFromNet(...), minGross)` — leaving the exact, unadjusted player-side balance cap untouched (a player genuinely short of the minimum still correctly shows invalid).

### 34.4 — Fee breakdown: network cost vs. casino margin (additive model, D-SW.11)

> **⚠️ Superseded (2026-07-08).** This section originally described the INCLUSIVE fee model (`max(SwapFeePercent% × gross, NetworkFeePolicy.MinFee)`, D-SW.1), where the casino's percentage fee absorbed the network cost rather than adding to it — the casino's real margin started at 0% exactly at the minimum swap size and only *rose* toward the nominal % for much larger swaps. Dev feedback after using the desk (2026-07-08) found this backwards from the intuitive "we charge you the network cost, PLUS our own cut" model real exchanges use — **D-SW.11 replaces `max()` with a sum**, and the margin behavior below is now the OPPOSITE direction: it starts *above* nominal near the minimum and *settles down* toward nominal as swaps get larger. The math below reflects the current (additive) model.

Each quote's combined `FeeCharged` is now `NetworkFeePolicy.MinFee × (1 + fee) + fee × base` (§3.1a of the swap plan) — the network's flat cost and the casino's percentage cut are **summed**, so unlike the old model, both portions can be extracted independently without ambiguity:

```
networkFee = NetworkFeePolicy.MinFee                    (in BTC terms — Panel A)
networkFee = NetworkFeePolicy.MinFee × PriceUsed         (in SC terms — Panel B)
casinoFee  = FeeCharged − networkFee                     (= fee × (base + networkFee) — always > 0)
```

Unlike the old inclusive model, `casinoFee` can no longer be `0` — the casino now earns its full percentage cut on top of the network cost at *every* swap size, including the smallest one (§3.1a's minimum swap size no longer exists to prevent a casino loss — that's impossible under this model — but to guarantee the player nets back at least as much as they pay in total fees, a VALUE floor rather than a mere non-degenerate-delivery one; see §34.3's third-revision addendum and §3.1a of the plan for the derivation).

**The effective margin starts ABOVE nominal near the minimum, settling down as swaps get larger.** The general identity, derived from `casinoFee/base`:

```
effectiveMarginPercent = SwapFeePercent × (1 + NetworkFeePolicy.MinFee ÷ base)
```

This is *always* ≥ the nominal `SwapFeePercent` (since `NetworkFeePolicy.MinFee ÷ base > 0`), converging toward it only as `base` grows large relative to the flat network cost. **Worked example** (10% nominal, `base = 1.1` BTC — 10% above the pre-D-SW.11 flat 1.0 BTC minimum, chosen so this reads against the same swap size Ch. 34's original dev report used): `feeCharged = 0.1×1.10 + 0.10×1.1 = 0.22` — network `0.1` + casino `0.12` — **effective margin ≈10.909%**, noticeably above the 10% nominal rate. The gap shrinks fast with size: at `base = 10` BTC, effective margin is `10%×(1+0.1/10) = 10.1%` (within 0.1 point of nominal); at `base = 100` BTC, `10%×(1+0.1/100) = 10.01%` (within 0.01 point) — the same "large swaps approach nominal" shape as before, just approached from the opposite side. Both quote labels in `CasinoCoinSwaps` show this **effective %** alongside the network/casino BTC-or-SC split, so it never needs to be hand-derived mid-playtest.

### 34.5 — Capping the deviation: D-SW.12's max fee deviation points

After using the desk with the additive model above, the dev asked: the near-minimum effective margin (~13.6% at the current ≈0.275 BTC minimum, 10% nominal) strays further from nominal than they'd like — could the surplus be trimmed so the casino's real cut never wanders too far from the configured percentage? The answer is a new dev knob, `MaxFeeDeviationPoints` (default `2.0`, clamped `[0,20]` points, `CasinoGamblingFinances`), that caps how many *percentage points* above nominal the effective margin may run.

**The cap targets the casino's own cut, never the combined total the player pays.** The first implementation attempt capped the COMBINED `totalFee` directly — `totalFee = max(NetworkFeePolicy.MinFee, min(additiveFee, (fee+maxDeviationFraction)×base))` — but this is mathematically broken for small `base`: whenever the flat network fee alone already exceeds `(nominal+points)%` of the base (this starts at a real, in-range swap size — ≈0.5 BTC at the 10%/2pt defaults, well above the ≈0.275 BTC minimum), the "never charge less than the real network cost" floor and the "never charge more than nominal+points%" ceiling become mutually impossible to satisfy at the same time, and the floor always wins — producing an effective *total* cost WORSE than the uncapped model at that size (verified numerically: 20% at `base = 0.5` BTC under the broken design, vs. the uncapped model's own 12% at the same size — the opposite of the intended fix). This was caught before shipping, via the same "verify algebra against real numbers" discipline established earlier in this chapter.

The corrected design caps the CASINO'S CUT only, with the network fee always charged in full, unconditionally, on top:

```
networkFee   = NetworkFeePolicy.MinFee                              (BTC terms — Panel A; ×price for Panel B)
casinoFeeRaw = fee × (base + networkFee)                            (§34.4's uncapped additive cut)
casinoFeeCap = (fee + maxDeviationFraction) × base                  (the ceiling: never above nominal+points%)
casinoFee    = max(0, min(casinoFeeRaw, casinoFeeCap))
totalFee     = networkFee + casinoFee
```

This has no floor/ceiling conflict — `casinoFee`'s floor (`0`) and ceiling (`casinoFeeCap`, itself always `≥ 0`) can never contradict each other, for any `maxDeviationFraction ≥ 0`. Below the crossover point where `casinoFeeRaw = casinoFeeCap` (solves to `base = fee×NetworkFeePolicy.MinFee/maxDeviationFraction`, ≈0.5 BTC at the 10%/2pt defaults), the cap binds and `effectiveMarginPercent` holds flat at exactly `SwapFeePercent + MaxFeeDeviationPoints`; above it, the uncapped additive formula from §34.4 governs unchanged, and margin decays toward nominal as `base` grows, exactly as before. At the ≈0.275 BTC minimum swap size, effective margin is now capped to 12% (at the 10%/2pt defaults) instead of the uncapped model's ~13.6%.

**Interaction with the §34.3 value-floor minimum swap size.** The cap only ever *reduces* `casinoFee` (hence `totalFee`) relative to the uncapped value — it never increases it — so `net = base − totalFee` only ever *increases* relative to the uncapped case. The value floor's defining inequality (`net ≥ totalFee`) was derived from the uncapped formula and therefore still holds (with strictly more slack) at the same ≈0.275 BTC minimum under the cap; the minimum was not re-derived for the capped case, which would need its own piecewise solve.

## Chapter 35 — The DEV Alt-Timeline Bootstrap (Simulacrum): How to Re-Mount It and How to Design New Ones

Step 13's swap desk was developed against a **DEV-only alternative timeline** (the "simulacrum world"): the entire early-Bitcoin bootstrap shifted forward by a constant offset so the player lands on **2010-07-18** — the first day of the BTC/USD market dataset — instead of the canonical 2009-03-21, eliminating a ~484-in-game-day grind before swap tooling could be exercised against live prices. The full design is in `AIHelperFiles/step13-btc-market-data-and-dev-alt-timeline-plan.md` (§0, §3); this chapter is the **operational guide** written at TL.3 (exit) time, so the simulacrum — or a sibling targeting a different era — can be re-mounted in minutes without re-deriving anything.

**The single most important fact**: the simulacrum is NOT a branch, a save file, or a separate build. It is **one `const bool`** plus an automatic world-wipe guard. Everything else in this chapter is detail.

### 35.1 — The machinery (all of it already merged to `main`, permanently)

Four pieces, all timeline-agnostic while the flag is `false`:

1. **`TimelineConfig`** (`Scripts/Services/TimelineConfig.cs`) — a pure static class (not a Node, like `NetworkFeePolicy`):
	- `DevAltTimeline` — the flag. `false` on `main`, forever.
	- `Offset` — `TimeSpan.FromDays(484)` when true, `TimeSpan.Zero` when false. With `Zero`, every shifted date is bit-identical to the canonical literal — that is why the refactor lives safely on `main`.
	- `Tag` — `"ALT-2010-07-18"` / `"CANON-2009-01-03"` — the world-compatibility stamp (see #3).
	- `Shift(DateTime)` / `Shift(DateTimeOffset)` — the one operation every anchor routes through.
	- `PlayerStartDayLocal` — the shifted 2009-03-21, shared by `HistoricalBootstrapService` and `FoundersMiningService.HalDecayStart` so two consumers can never drift apart.
	- `FeeActivationLocal` — the **one deliberate functional divergence** (D-13.9): under the alt flag, fees activate on the landing day itself (not the uniformly-shifted date), because canon is long fee-active by the time trading unlocks and swap tooling had to be born fee-aware. Canon path reads exactly `2009-04-26`, untouched.
2. **The seven anchor sites** — every calendar-dated world anchor routes its `static readonly` date through `TimelineConfig.Shift(...)`: `BlockchainService.GenesisTimestampUnixMs`, `CalendarTimeService.GameStartLocal`, `HistoricalBootstrapService` (player start + Hal block dates + E4), `FoundersMiningService` (Satoshi disappearance + Hal decay start/end), `HistoricalEventScheduler.HearnDealDateMs`, `NetworkFeePolicy.ActivationDateLocal`. Everything **not** anchored to a calendar date is timeline-agnostic by construction and needs no change: halving (block-height 2,100), the difficulty regulator (solvetimes), the pre-genesis reset and the player-start instant (both chain-tip-derived), the §24.9 clock rule, bot scheduling (block-relative). This is why the uniform-offset approach is cheap and safe.
3. **The incompatibility guard** — `NetworkRoot.ResetWorldIfIncompatible()` (the generalized `ResetWorldIfFormatChanged`) compares `user://world_timeline.stamp` against `TimelineConfig.Tag` at every boot. A mismatch (either direction) triggers the **full clean reset** (D-13.7): chain state + monthly block chunks, checkpoint, calendar, bankroll, principal, bankroll-program, casino SC state, player bank account, client ledger, the bet-history file + monthly chunks, **hardware allocation, casino-pool ledger, swap-desk state** (the last three added at TL.3 — see the incident below), and the three DEV trace CSVs (`difficulty`/`founders`/`swap_desk`) — then re-stamps. **Deliberately spared** (identity/personal data, not world state): the wallet seed/address files (player/casino/satoshi/hal/mike_hearn, `bot_wallet_registry` — a fresh bootstrap reuses the same identities), `saved_betting_strategies`, `notepad_notes`, `wordlist_256`. A *missing* stamp is backfilled silently, never treated as a mismatch (protects pre-TL.1 saves). Net effect: **flipping the flag and launching is the entire migration procedure, both ways. No manual file surgery exists, by design.**
	- **Ordering is load-bearing (TL.3 incident, 2026-07-07)**: the guard MUST run before ANY service/repository loads its `user://` file into a static cache — a file deleted *after* being loaded lives on in memory and re-persists later. The first canon relaunch leaked alt-world hardware credits, bot pool shares, and a casino-pool ledger referencing wiped blocks, precisely because `CalendarTimeService` (an early autoload) loads hardware/pool state via `WalletInitializationService.EnsureAll()` long before `NetworkRoot`'s own initialization ran the guard (the checkpoint-covered services masked the same ordering hole only because the pre-genesis reset overwrites them in memory afterwards). Fix: **`WorldGuardService` — the FIRST autoload in `project.godot` (#1, autoload #16 overall)** — calls `NetworkRoot.RunWorldCompatibilityGuard()` (idempotent) so the wipe precedes every load; the original call inside `EnsureInitialized` remains as a safety net.
	- **Maintenance rule**: every NEW persisted world-state file must be added to the delete list when it ships (`casino_coin_swap_state.json` was created *after* the list was written and missed it). The rule is recorded in code, above `ResetWorldIfIncompatible` itself.
4. **The watermark** — `StatusBar` prepends a red `[ALT-TIMELINE DEV]` label on every screen whenever `DevAltTimeline` is true. Non-negotiable: it is the guard against alt-world screenshots leaking into design docs as canon.

### 35.2 — Re-mounting THIS simulacrum (the 2010-07-18 / Mt. Gox landing)

1. Be on a feature branch (never `main` — see §35.5).
2. Flip `TimelineConfig.DevAltTimeline = true` (one line).
3. `dotnet build`, then launch **in the editor yourself** (never headless via the assistant — it writes to the real `user://`).
4. The guard wipes the world and the bootstrap regenerates the alt world automatically. Verify against the known-good TL.2 log signature (`godot.log`):
	- `[NetworkRoot] World reset triggered (format N → N, timeline 'CANON-2009-01-03' → 'ALT-2010-07-18')`
	- `[HistoricalBootstrap] First launch — mined genesis → 2010-07-18 …. Satoshi ~110 blocks, Hal 3 blocks. E4 … on-chain.` (block counts jitter by ±1–2)
	- `[BtcMarketDataService] Day changed → 2010-07-18 price=0.0678842 source=mtgox`
	- Red `[ALT-TIMELINE DEV]` watermark visible on every screen.
5. To exit: flip the flag back to `false`, launch → the guard wipes again → pristine canon 2009 world. That is TL.3's whole mechanical content (plus re-verifying the swap desk sits locked until the clock crosses 2010-07-18).

Both wipes are **total and unconditional** — whatever playtest world exists at flip time is destroyed. This is a feature (worlds across timelines are corrupt hybrids if mixed), but check you're not sitting on a playthrough you care about before flipping.

### 35.3 — Designing a NEW simulacrum for a different era (the recipe)

To land the player on some other historically interesting day `L` (a halving, the 2013 bubble, a halt week…):

1. **Compute the offset**: `Offset = L − 2009-03-21` in whole days (`TimeSpan` arithmetic absorbs leap years automatically — the 484-day original spans none between the anchors, but Satoshi's disappearance date does cross the 2012 leap day and shifts correctly for free).
2. **Edit `TimelineConfig` only**: set `DevAltTimeline = true`, change `TimeSpan.FromDays(484)` to the new day count, and change the `Tag` to a unique string (e.g. `"ALT-2013-11-29"`). **The tag must differ from every tag previously stamped on this machine's save** — the guard fires on *difference*, so even alt→alt switches reset correctly as long as tags are unique per timeline.
3. **Decide the functional divergences deliberately.** The uniform shift reproduces canon's *shape*; anything that should instead match canon's *state at era L* needs an explicit special case in `TimelineConfig`, following the `FeeActivationLocal` precedent (D-13.9): a named `static readonly` with a comment, never a silent shift. For any post-2009-04-26 era, keeping fee activation = landing day (as D-13.9 already does via `PlayerStartDayLocal`) is almost certainly what you want.
4. **Touch nothing else.** The seven anchor sites already read `TimelineConfig`; the guard already handles the wipe; the watermark already reads the flag. If a new anchor was added to the game since Step 13 (any new `static readonly` calendar date that positions a world event), route it through `Shift()` first — that is the only maintenance this system needs.
5. **Verify with the §3.5-style acceptance checklist**, re-derived for the new offset: ~113 bootstrap blocks with Hal exactly 3 (the 76.24-day genesis→landing arc is preserved verbatim, just translated); E4 on-chain near `2009-01-12 + Offset`; landing block timestamp ≥ `L` 00:00 local; `calendar_state.json` == landing block timestamp exactly (the §24.9 rule is chain-derived and holds under any offset, unmodified); a pre-first-block restart resets to the landing instant, not to any canonical date; `BtcMarketDataService` returns era-appropriate data on day `L` (halt days show the desk closed).

### 35.4 — What the uniform offset can and CANNOT give you

The offset **translates** the canonical 76-day / ~113-block bootstrap arc; it does not fabricate the intervening history. A simulacrum landing in 2013 still hands the player a *newborn* network: Satoshi holding ~110 blocks (not his full arc), difficulty at bootstrap levels, ~5,650 BTC ever mined, no bots' years of transactions — while the market feed confidently reports the real 2013 price of a five-year-old Bitcoin. For a **dev scaffold** this mismatch is exactly as acceptable as the genesis-headline anachronism (D-13.0: cosmetic noise in a throwaway world, never worth patching). For the **player-facing** "start in the Mt. Gox era / the first bubble" feature (plan §9.1), it is disqualifying — those bootstraps must keep genesis at 2009-01-03 and fast-build the *real* intervening chain/founder/difficulty history to produce canon-compatible worlds. Do not conflate the two: the simulacrum moves genesis itself and its worlds are always throwaway.

### 35.5 — Rules that must never be broken

- `DevAltTimeline` is **`false` on `main`, forever**. Flip it only on a feature branch; revert it (and confirm `Tag` reverts) before merging back. The flip commit and the revert commit should both be explicit, greppable one-liners.
- **Never bypass the guard** (hand-editing the stamp, restoring `user://` backups across timelines): a canon save under the alt flag is a corrupt hybrid (2009 chain tip vs. 2010 fee gate, bets dated before genesis).
- **The watermark ships with the flag.** If a future refactor moves StatusBar, the `[ALT-TIMELINE DEV]` label moves with it.
- No alt-world screenshot, log excerpt, or balance figure may be presented as canon in any design doc — label them.
- Date-anachronistic cosmetics inside a simulacrum are **accepted, never patched** (D-13.0). Time spent polishing a throwaway world is time wasted.

---

## Chapter 36 — Historical Network Population Scheduler (Step 14 ND): The Two-Layer Hybrid Model, and a Documented (Not-Built) Scale-Up Path

Step 14 makes the surrounding Bitcoin network feel historically alive without simulating every real participant individually: total network power at any calendar date is derived from the real hashrate/address-growth curve (`Data/HistoricalNetwork/btc_network_daily_2009_2025.csv`), then split between a small set of **named, visible** miners and one **invisible aggregate** covering everyone else. Full design history (all rounds, every decision) lives in `AIHelperFiles/step14-historical-network-population-scheduler-plan.md`; this chapter is the durable reference — what's built, how it fits together, and (§36.6) the one significant scaling idea that was investigated and deliberately **not** built.

### 36.1 — Why a hybrid model, not a full simulation

Real Bitcoin's participant count is unusable directly: from a handful of hobbyist CPUs in 2009 to industrial ASIC farms by 2017, spanning **~14.6 orders of magnitude** of hashrate (`BtcNetworkDataService`'s own measurement — `decades(2025) ≈ 14.6`, not the step14 plan's original ~12 estimate). Registering one `NodeAgent` per real historical miner is neither meaningful (most real miners left no individually interesting trace) nor affordable (§36.6). The design instead asks: **"if the network's total power at date D matches history, and a handful of NAMED miners are visible for flavor, does it matter whether the rest are individually modeled?"** — for a single-player prototype, no. So:

- **Visible cast** — a small, growing set of real, persistent, named miner identities (`artforz`, `laszlo`, … `foundry_usa`) the player can see mining blocks and circulating BTC.
- **Invisible mass** — one power term standing in for the rest of the real network, its blocks attributed to a rotating anonymous pseudonym so it isn't silently absent from the Block Explorer.

Both layers are driven by the **same single scale anchor** (§36.2), so the split between them never has to be tuned independently — only `EraMaxHardwareCredits`/`CastPerDecade` (the cast's own sizing) are real knobs.

### 36.2 — The scale anchor: `EraStandardPower` and `TotalNetworkUnits`

`BtcNetworkDataService` (autoload #17, mirrors `BtcMarketDataService` exactly — CSV loaded once, O(1) day lookups, no persistence) exposes the whole model as pure `date → value` functions:

```
decades(date)          = log10( hashrate(date) / hashrate(PlayerStartDayLocal) )   // clamped ≥ 0
EraStandardPower(date) = EraMaxHardwareCredits ^ ( decades(date) / decadesAtDatasetEnd )
TargetVisibleMiners(date) = BaseCast (4) + CastPerDecade (2.0) × decades(date)
TotalNetworkUnits(date)   = EraStandardPower(date) × TargetVisibleMiners(date)
```

`EraStandardPower(date)` answers **"what is one of today's historic miners worth?"** — and because `TotalNetworkUnits / TargetVisibleMiners = EraStandardPower` by construction, it is *also* exactly the network average at every date, with no feedback loop from the player's, founders', or any bot's own live power (confirmed by inspection at step14 §6.1 — this was a design question the developer asked explicitly, and the answer was "already built exactly as described"). A powered cast member always wields exactly `EraStandardPower(date)`; a player at era-standard hardware therefore always represents "about one cast member's worth" of the total, by design, at every point across the game's ~132-year span (2009 → ~2141 supply exhaustion).

At the canonical player start (`decades = 0`), `TargetVisibleMiners = BaseCast = 4` and `TotalNetworkUnits = 4 × EraStandardPower(0) = 4 × 1 = 4` — smaller than any live participant's power, so the scheduler is a complete no-op until the historical curve actually grows past it. Nothing needs a special "off" switch for the early game; the math handles it.

### 36.3 — The two layers in code: `NetworkPopulationScheduler`

`NetworkPopulationScheduler` (`Scripts/Services/NetworkPopulationScheduler.cs`) is a plain `static class` — not a `Node`, not a `project.godot` autoload, the same pure-controller shape as `FoundersMiningService`/`HistoricalEventScheduler` taken one step further (no per-frame `_Process` of its own; `SimulationService` drives it directly). Nothing persists across restarts except cast **identity** (`BotWalletRegistry.CastMiners` — a deliberately separate third list from `MinerBots`, since cast members join none of the betting-runner/donation-loop machinery `MinerBots` feeds).

- **Visible cast, spawn-drip**: `Recompute(date, playerBotsPower, foundersPower)`, called once per new block, powers the first `TargetVisibleMiners(date) − BaseCast` registered cast members (in spawn order) at exactly `EraStandardPower(date)` each; a new cast member is registered **at most one per block** as the target grows, drawn from a chronologically-flavored name pool (36 early-individual → pool-era handles, `artforz` through `foundry_usa`; exhaustion falls back to `miner_extra_N`, never expected at the current pool size). Cast members mine **founder-style** — drained nonce attempts in lockstep with the player's own bets (`DrainScheduledAttempts`, the identical accumulator pattern `FoundersMiningService.DrainFounderAttempts` uses), never advancing the clock on their own.
- **Invisible mass**: `max(0, TotalNetworkUnits(date) − playerBotsPower − foundersPower − castTotal)` — whatever's left of the historical total once every individually-modeled participant is subtracted. Its mined blocks are attributed to a **rotating ghost pseudonym** (12 names, `unknown_miner` → `forgotten_rig`) via **session-transient, one-off wallets** — keys that die with the process, so ghost-mined BTC is frozen forever the moment the app closes (the same "coins nobody can ever move again" precedent as retired-Satoshi). As of ND.4a (2026-07-10), `AdvanceGhostRotation()` draws the **next** pseudonym uniformly at random (plus a randomized initial index) rather than a fixed round-robin — the original fixed rotation kept all 12 names permanently tied in blocks-mined, which read as obviously synthetic in the Block Explorer; randomizing attribution spreads the exact same invisible-mass total organically without touching the underlying power math at all.
- **Per-frame drain budget**: capped at `MaxScheduledAttemptsPerFrame = 5000` (accumulators capped at `10,000`) — late in the game the scheduled mass can owe thousands of attempts per player bet, and an unbounded drain could stall a frame at high `DevTimeScale`. A sustained shortfall just slows blocks slightly, which the difficulty regulator's LWMA feedback (Chapter 26) then trims automatically — the system self-corrects rather than needing a hard cap tuned per era.
- **Telemetry**: `user://logs/network_population_trace.csv`, one row per live block (decades, cast target/powered count/power-each, invisible power, player+bot/founder/total power, tx target, pending txs, spawned-this-block id) — the same DEV-readout precedent as `founders_trace.csv`/`swap_desk_trace.csv`.

### 36.4 — The transaction layer: fullness parity, not a flat probability

A separate accessor, `GetTargetTxPerBlock(date)`, drives how MUCH automated transaction traffic should exist per block — the real historical average, non-coinbase, clamped to `[0, MaxBlockTransactions − 1]`. `NetworkRoot.ScheduleBotTransactionsAfterBlock` turns this into a budget every block: `owed = max(0, target + fractionalCarry − pendingOrganicTxs)` — **organic traffic (player sends, swap legs, pool payouts) always counts first**, so automation only ever tops up whatever the player hasn't already generated; the fractional carry accumulates sub-1 targets so 2010's ≈0.01 tx/block becomes roughly one automated tx every hundred blocks rather than rounding to zero forever. Two independent cycles compete for that budget:

- **Cast sell-flow** (`TryCastSellFlow`) — cast miners circulating mined BTC to introduced non-miners, fair random rotation (a fixed iteration order was found, during the first ND.4 calibration playtest, to starve every cast member behind whichever miner iterates first — fixed at ND.4a).
- **Non-miner↔non-miner exchanges** (`TryNonMinerExchanges`) — real UTXO sends between funded holders, same budget, same fairness pattern.

The casino-miner-bots' (`bot_1..4`) own **referral-auction bidding cadence** is deliberately OUTSIDE this budget entirely — see Chapter 22 §22.7.

### 36.5 — Spot-checking eras without a full playthrough: the EB.1 entry-year bootstrap

Waiting in real time for a live game to reach 2013 or 2020 to verify the curve is impractical. `TimelineConfig.DevEntryYear` (`0` on `main`, forever) lets a developer fast-build a **canon-compatible** world — genesis and the founders stay at their true dates, but the intervening history (founder arcs, cast spawns, halvings) is generated at the bootstrap's own accelerated cadence, landing the player directly on 21 March of the chosen year (2010–2025). The key architectural finding that makes this cheap: `FoundersMiningService`, `BtcNetworkDataService`, `NetworkPopulationScheduler`, and `NetworkRoot` are all either pure-static or hold zero meaningful instance state, so a bare `new BtcNetworkDataService()` (etc.), never added to the scene tree, is a fully functional accessor — no autoload-ordering problem, no `project.godot` changes, zero risk to the already-shipped live-play paths. This is how the ND.4/ND.4a calibration playtests (§36.3's ghost-rotation fix, and Chapter 22 §22.7's auction rework) were actually exercised — land directly in 2010, play forward a while, observe.

### 36.6 — Scaling the non-miner pool past 40: a documented, NOT-built proposal

The referral auction's non-miner pool (Chapter 22) currently caps at **40** (`BtcNetworkDataService.NonMinerPoolSize`, raised from an initial 10 during Step 14 round 3, D-EB.8). The developer's original interest was scaling toward **100–220** — a pool sized closer to the real historical count of early addresses. This section is the detailed, theoretical write-up of that investigation, promised at D-EB.8 and delivered here at ND.4, per the developer's explicit direction: **fully specified, deliberately NOT implemented.**

**The schedule math itself needs no new data.** `NonMinerPoolSize` / `NonMinersPerAddressDecade` / `BaseNonMinersAtBirth` already parametrize the whole introduction schedule off the existing active-address curve — retuning them to a larger N is a two-constant change, not a new dataset or pipeline. Numeric simulation against the real CSV (birth-anchored running-max, `perDecade = (N−1) / peakDecades`, `peakDecades = 3.201` — the dataset's true birth→peak span, corrected during this investigation from an unverified earlier "~2.9 decades, Dec 2017" claim; the real peak is **2021-04-15, 1,366,494 addresses**):

| N | perDecade | last slot deploys | count by 2011-06-19 (bubble) | count by 2013-11-29 |
|---|---|---|---|---|
| 10 (shipped pre-round-3) | 3.0 | 2016-01-20 | 6 | 8 |
| 20 | 5.94 | 2017-11-29 | 10 | 15 |
| 40 (shipped) | 12.18 | 2017-12-13 | 20 | 30 |
| 100 | 30.93 | 2021-01-06 | 49 | 75 |
| 220 | 68.41 | 2021-01-06 | 106 | 164 |

**A real design consideration, independent of performance**: the log curve front-loads growth rather than spreading it evenly. At N=220, 48% of the pool (106 of 220) deploys before the June-2011 bubble, with several **same-day** introductions in the first week after Market Birth (`round()` jumping multiple integers in one day once the density constant is high — e.g. `2010-07-18, 2010-07-19, 2010-07-19, 2010-07-20, 2010-07-25`). This isn't a bug, but a pool that "feels" like a handful of rare, gradually-arriving individuals at N=10 becomes dozens of same-day arrivals in year one at N=220 — a decision to make deliberately if this is ever built, not discover by accident.

**The real technical obstacle: registered node count multiplies an existing, chain-length-growing cost.** Traced through the actual engine code, not assumed:

- Every `NodeAgent` registered via `SharedNetwork.RegisterNode` receives **every** mined block (`BroadcastBlock`) and **every** broadcast transaction (`BroadcastTransaction`) into its own local `.Blockchain` replica — a real per-node cost, not bookkeeping.
- `AddTransactionToPendingTransactions` (called on every registered node, for every broadcast tx) needs that node's `GetUtxoSet()`, cached per node per `_chainVersion` — cheap when warm, but a **full chain replay** (`O(chain length)`) the first time it's called after that node accepts a new block. A new block invalidates every registered node's cache simultaneously, so the very next broadcast transaction after ANY mined block forces every node to rebuild. **The cost of "the first transaction after a mined block" therefore scales as `registered node count × chain length`.**
- This is not a new problem introduced by a larger non-miner pool — it already exists today at roughly 40–55 registered nodes (player + 4 casino-miner-bots + up to ~33 cast + 40 non-miners + casino + 2 founders + occasional session-transient ghosts), and it already grows every block as a long playthrough's chain lengthens (up to 71,400 blocks at the canonical ~2141 supply-exhaustion horizon). Scaling non-miners alone from 40 → 220 would push total registered nodes toward ~250–265 — roughly a **5–6× multiplier on a cost that is already the single most expensive step in the per-block pipeline**. (No wall-clock benchmark exists for this claim — the project's "never headless-launch the game" rule means this is architectural reasoning from the code, stated as such, not a measured number.)

**The proposed fix, if N ever needs to grow well past ~50: decouple "non-miner wallet identity" from "full broadcast-registered `NodeAgent`."** The engine already has the precedent this needs — `NetworkRoot.GetAddressBalanceDetails(address)` and `CollectUsedAddressSet()` both read balance/UTXO membership **directly off the player's own canonical chain replica, by address**, with no requirement that the queried address own a registered `NodeAgent` at all (`GetAddressBalanceDetails` looks up `node.Blockchain.GetAddressData(address)` on the PLAYER's node, not the queried address's own node). A "wallet-only" non-miner would:

1. Hold keys (for signing, when the exchange scheduler or a casino-bot's donation picks it as a sender) without ever calling `SharedNetwork.RegisterNode` — removed entirely from the `BroadcastBlock`/`BroadcastTransaction` loops, so its existence costs nothing per block.
2. Have its spendable balance/UTXOs read off the **player's** canonical chain replica by address (the `GetAddressBalanceDetails` pattern), rather than maintaining its own `.Blockchain` copy.
3. Build sends through a NEW spend path that coin-selects from that canonical UTXO set instead of `sender.Blockchain.GetSpendableUtxos` (the assumption `BuildAndBroadcastUtxoSpend` currently bakes in everywhere it's called) — this is the one real code surface the refactor touches, and it is not small: every call site that spends on behalf of a non-miner (`TryNonMinerExchanges`, and a scaled-up cast/casino-bot sell-flow if those senders were ever wallet-only too) would need the new path instead of the current `NodeAgent`-owns-its-chain assumption.

This decouples "how many non-miner identities exist" from "how many nodes pay the O(nodes × chain length) broadcast cost" — the non-miner *count* could scale toward 100–220 (or further) while the actually-registered node count (and therefore the per-block cost) stays flat at the handful of participants that truly need their own replicated chain (player, betting bots, founders, cast miners — everyone that either bets, mines, or needs its own mempool view). **This is a scoped, real refactor, not a small tuning change**, and it is not built. It is recorded here as the concrete, evaluated path forward the next time non-miner pool scale becomes a priority.

### 36.7 — Historical Fee Replay (Step 14 ND.7, 2026-07-13): real daily network fees from Market Birth

The last big flat constant in the economy — `NetworkFeePolicy`'s 0.1 BTC scaffold, active since Step 10's 2009-04-26 gate — was replaced by a historical replay of real daily fees (D-14.6's locked Option A, promoted into Step 14 scope as ND.7). Step 10's flat era is **retired entirely**; its plumbing (fee rows in the four send panels, per-tx `Fee = Σin − Σout`, coinbase fee collection) is what the replay now feeds.

- **The gate (D-ND7.1)**: the fee era begins at **Market Birth (2010-07-18)**, data-driven — the schedule's own first replayed day, read from `BtcMarketDataService.FirstDataDateLocal`, never a second hardcoded date. Before it, every tx on the network is fee-free and the fee row stays hidden. `TimelineConfig.FeeActivationLocal` (D-13.9's alt-timeline special case) was deleted — the alt timeline lands on 2010-07-18, so "fees begin at Market Birth" absorbs it with zero special-casing.
- **The daily band (D-ND7.2)**: `median` = the dataset's new `fee_median_btc` column; `mean` = `fee_total_btc ÷ tx_count` (derived); `max` = `max(median, mean) × MaxFeeMeanMultiplier (10)` — the multiplier is a **documented approximation**: no confirmed source publishes a true daily min or max per-tx fee (the real daily minimum was effectively zero for most of history), so the developer accepted median-as-base and a derived max. Fees are fractal-exempt (face value, never /100 — like `price_usd`).
- **Median column provenance (ND.7.0, data-honesty note)**: Coin Metrics community's `FeeMedNtv` turned out to be paid-tier (re-verified 2026-07-13, exactly as ND.0 had recorded), so the column is a developer-approved hybrid — **true medians computed from Blockchair's per-day transaction dumps** for 2010-07-18 → 2011-04-13, then **BitInfoCharts' daily USD median ÷ their own price** for 2011-04-14 → 2025-12-31, spot-checked against independently computed true medians on five later days (all within 0.2%). Full pipeline detail: step14 plan §10.5.
- **Who pays what (D-ND7.3)**: the cast miners' sell-flow pays the day's **mean** (they ARE the network's average activity); every other automated tx — non-miner exchanges, casino pool payouts, casino-bot auction bids (bid *amounts* untouched, D-ND7.7), settlement sweeps, swap-desk legs — pays the day's **median**; the player's send panels default-fill the median and clamp to `[median, max]` (`ClampOrDefaultFor`). Nobody has a reason to pay above base until the Option-B congestion layer exists (OQ-ND7.1, in `PRIVATE_ROADMAP.md`).
- **Carry-forward (D-ND7.4) and the honest-zero era**: each band component independently carries forward its last positive value across zero/blank days, seeded from the data's own start. The data forced one refinement: **no positive median exists anywhere before 2011-04-14 (= 0.01 BTC, the era's standard client fee)**, so the effective median is an honest **0** from Market Birth through 2011-04-13 — most transactions genuinely paid no fee then, and the swap desk deliberately opens into that ~9-month zero-network-fee era (the band stays valid: the mean is positive from 2009-02-03, so `max` is never 0 inside the fee era).
- **Architecture (D-ND7.5)**: `BtcNetworkDataService.ComputeAndPushFeeSchedule()` builds the effective day-indexed schedule at load and pushes it into the pure-static `NetworkFeePolicy.SetFeeSchedule` (the `SetNonMinerIntroSchedule` precedent) — the same push works for EB.1's throwaway bootstrap instances, so entry-year fast-builds crossing Market Birth pay replay fees during the bootstrap itself. No schedule (CSV load failure) ⇒ fee-free fallback + one warning, never the 0.1 scaffold back. Expected load print: `Fee schedule pushed: 5646 days from 2010-07-18 (seed median=0.00000000 mean=0.00318548 max=0.03185480; end median=0.00000277 mean=0.00000748 max=0.00007480)`.
- **Reset guard (D-ND7.6)**: fee semantics are world-defining, so ND.7 shipped as `WorldFormatVersion` 2 → 3 — the established clean-reset mechanism handles migration; no compatibility shim.
- **Telemetry (D-ND7.10)**: `network_population_trace.csv` gained `feeMedianBtc,feeMeanBtc` columns (the day's effective values at each block row).

### 36.8 — Cross-references

- Referral auction mechanics (introduction schedule, bidding rules, the ND.4b/ND.4c ascending-auction rework): Chapter 22, especially §22.6–22.7.
- Difficulty regulator / LWMA feedback that absorbs the population scheduler's power contribution: Chapter 26.
- Founder economics (the pattern `NetworkPopulationScheduler` extends to a second concurrent-miner layer): Chapter 28.
- DEV alt-timeline / entry-year bootstrap machinery this scheduler's throwaway-instance trick depends on: Chapter 35.
- Swap-desk fee formulas the ND.7 live median now feeds: Chapters 33–34.
- The SC Monetary Ledger (ND.8c) that accounts every SC mint/burn: §36.9 below.
- Full decision history, every round, every open question: `AIHelperFiles/step14-historical-network-population-scheduler-plan.md`.

### 36.9 — SC Monetary Ledger (Step 14 ND.8c, 2026-07-19): monetary-system Option 0, the fiat-debt accounting substrate

The first build of the **fiat-debt ladder** (step14 plan §12.4.6e, `D-ND8.30…35`): before this, SC was created from nothing, unaccounted, at several code sites — the player's initial `40,000` split, `bot_1..4`'s starting balances, and every casino bank-loan draw (`LoanCount`/`TotalLoaned` grew forever from an abstract off-screen bank). The ledger makes SC **conserved by bookkeeping**: every SC in existence is now attributable, under the standing invariant

> **`TotalCirculation = TotalGenesisGrants + TotalDebtOutstanding`** — the debt-backed dollar in accounting form.

- **`ScMonetaryLedgerService`** (autoload #18, registered **before** `BlockSessionCheckpointService` — the `PlayerBankAccountService`/`CasinoCoinSwapService` boot-ordering precedent): records only **mint/burn** events (SC entering/leaving existence); flows between existing holders stay with their own ledgers (`CasinoClientLedgerService` etc.). Event log capped at 500 (totals stay exact independently of the cap); event timestamps are **game time** (`CalendarTimeService`, the Pattern-2 canonical rule).
- **Genesis grants (`D-ND8.35`)**: the five canonical casino players — `player` + `bot_1..4` — each hold a `40,000 SC` grant (the same canonical split each). Grants are **equity**: granted once, never repayable, never debt. They are registered **declaratively** at the pre-genesis/first-run paths (the client-ledger `"initial"` precedent) even though the bots' balances materialize lazily in code (`GetOrCreateNodeFinancialState`) — the ledger records the canon, not the lazy-init timing.
- **Loan draws**: one hook in `CasinoScBalanceService.AddLoanRecord` covers **all three** loan-draw sites (the bankruptcy dose recharge, `PayFromMainWithAutoLoan`, and the dev manual loan) — each draw mints its amount as debt on `"casino"`, keeping the ledger in lockstep with `TotalLoaned` by construction. **Burns** (`RegisterBurn`) are built but caller-less — armed for ND.8e (Option A), where repayment destroys SC.
- **Checkpoint discipline** (the three-question rule, all three answered): a `CheckpointState` DTO snapshotted/restored by `BlockSessionCheckpointService` (restored **after** the casino SC restore — the legacy-null path initializes from the casino's just-restored `TotalLoaned` plus the canonical grants, marked by one honest `init_sync` event); `ResetToPreGenesisDefaults()` re-registers the five grants at the player-start clock and zeroes debt; `user://sc_monetary_ledger.json` is in the world-reset delete list. **No `WorldFormatVersion` bump** — accounting-only, existing worlds initialize from live state.
- **DEV readout — the `WorldEconomy` scene** (`D-ND8.25`, Main Menu → "World Economy [DEV]"): circulation/grants/debt totals, per-party breakdowns, and the mint/burn event log (a Pattern-B `RichTextLabel` per §29.2, fixed footer per §29.10, bottom safe area per §29.11). ND.8b.6 added the company inflow/expansion knobs to this same scene (§22.12).
- **What comes next (documented, not built)**: ND.8e replaces the "off-screen printer" with the explicit **Central Bank + fed-funds policy replay** (`D-ND8.31…34` — credit-capacity multiplier, quarterly repayment, default = bank keeps collateral); post-Basic-Mode, Option B (full fractional-reserve) remains the documented successor. Option C (inflation/peg drift) is rejected forever — the 1:1 peg is canon; monetary tightening is expressed as credit scarcity, never value loss.

## Chapter 37 — Building on a Chain-Derived World: Emerging Advantages, Honest Trade-Offs, and the Story to Tell

**Status**: Evaluation chapter (2026-07-14), written at the developer's request after the §30.9 incident demonstrated the model's self-correcting property live. This is an honest assessment — the cons are real and listed with the same seriousness as the pros.

### 37.1 — The pattern, named

GamblingMiner's world state follows one architectural rule, arrived at incrementally and now deliberate: **the blockchain is the database, and almost everything else is a pure function of it.**

- The **UTXO set** is replayed from the chain, never persisted (§30.2).
- **Balances** don't exist as stored numbers — they are sums over unspent outputs, recomputed on demand.
- The **auction ledger** (leaders, windows, tracked donation pools, winners) is recomputed from the chain on every block and every UI refresh (`ComputeAuctionLedger` — §22.7).
- **Founder mining state** re-derives from the live world each launch (Ch. 28); the **population scheduler** re-derives from the game date + the historical dataset (Ch. 36); **fees** replay from a static dataset (§36.7).
- The only disk commits are blocks (Ch. 24's "a block is the only commit"), plus identity files (seeds, registries) that name *who exists*, never *what happened*.

The classic alternative — persisting derived state (an "auction standings" file, per-address balance caches, a donor database) — was avoided case by case, and each avoidance has now paid for itself at least once.

### 37.2 — Advantages already banked (each with a dated receipt)

1. **Retroactive bug healing — the DEV superpower.** When derivation logic is wrong, fixing the *function* fixes all of *history*: the world's next recompute reads the same chain and reports the corrected truth. Receipts: the §30.9 donor-identity fix healed a live playtest world with zero migration (2026-07-14); the live-SC-value display correction (2026-07-11) rewrote how every existing bid was priced, instantly; the ND.4b auction-model rework replayed old cumulative-era bids under entirely new rules (D-ND4b.12) without touching a single save file. With persisted derived state, every one of these would have been a migration script or a world wipe.
2. **Crash consistency for free.** Nothing between blocks is trusted to disk, so there is no partial-write corruption class at all — a crash reverts cleanly to the last block, which is a *defined, auditable* world state. The restart-revert behavior (Ch. 24) is the same mechanism as the rollback feature; one design carries both.
3. **Migration by clean reset, honestly.** World-defining changes ship as a `WorldFormatVersion` bump (UTXO switch v1→2, fee replay v2→3) with no compatibility shims and no half-migrated states — viable *because* so little is persisted that re-bootstrapping is cheap and complete.
4. **Free time-shiftability.** `TimelineConfig.Shift()`, the DEV alt-timeline (Ch. 35), and the EB.1 entry-year fast-builds all work because no derived state anchors to absolute persisted values — the same code replays any era. Building a "start in 2015" test world required near-zero new machinery (D-14.7).
5. **Auditability on demand.** The §30.6 in-engine audit (conservation, zero double-spends, supply total) ran from the chain file alone. Any future dispute — "did the casino overpay?", "did that bid qualify?" — is answerable from first principles, because the ledger *is* the evidence.
6. **A whole class of sync bugs cannot exist.** There is no reconciliation between an auction database and the chain, or a balance cache and the UTXO set, because there is no second copy to drift. The bugs we do get (§30.9) are *interpretation* bugs — visible, testable, and fixable retroactively — rather than *divergence* bugs, which are silent and compounding.

### 37.3 — Honest cons and future risks (the bill comes later)

1. **Replay cost grows with the chain — O(n) and unavoidable.** Every full recompute walks the whole chain. Today (hundreds to ~1,000 blocks) it's invisible; a played-to-2025 world (tens of thousands of blocks) will not be. Known hotspots, in expected order of pain: the launch-time rescan (§30.7's named watch-item), `ComputeAuctionLedger` (full-chain walk per block *and* per UI refresh), and per-address history scans in wallet panels. The mitigations are standard and already sketched — incremental caches keyed by `_chainVersion` (the UTXO set does this), per-session frontier caches — but they must be written *before* the late game is playable, and every cache reintroduces a small piece of the divergence risk the model was chosen to avoid. **This is the model's structural tax.**
2. **Retroactivity cuts both ways.** The same property that heals bugs also means *a rule change rewrites history's interpretation*: replaying old bids under the ND.4b rules could legitimately pick a different in-progress leader (accepted explicitly as D-ND4b.12). Anything that must never change after the fact (a resolved auction, a paid settlement) needs **deliberately designed permanence** — derived state has no memory unless you give it one. Settlement got this right only by side-effect design (block-diffed, fires once, §22.9); future systems must each answer "what here is allowed to be reinterpreted?"
3. **The legacy shims are a standing trap — two incidents and counting.** `tx.Sender`/`tx.Recipient`/`tx.Amount` project a multi-input/multi-output reality onto single values. Incident 1: membership scans missing change outputs (CLAUDE.md balance-model note). Incident 2: §30.9's donor identity. The shims should be deleted once bots go multi-address (OQ-8.2); until then every new consumer of transaction data is one careless line away from incident 3.
4. **Clean resets are a DEV luxury.** Pre-release, wiping the world on a format bump costs one developer a playtest. Post-release it costs every player their world. A shipped version needs either format stability, true migrations (expensive, the thing this model avoids), or an honest "seasons/prestige" framing where resets are part of the design. This decision is still open and should be made *before* release, not after the first post-release format change is needed.
5. **"Nothing between blocks persists" is a player-facing sharp edge.** Progress since the last mined block is lost on quit or crash — by design (it *is* the rollback mechanic), but a player who doesn't understand it will experience it as data loss. It must be communicated in-game, prominently, as a rule of the world ("the chain is the save file") rather than discovered by surprise.
6. **Single-world assumption is baked deep.** The static `NetworkRoot`/`SharedNodesById` world and the fixed `user://` layout mean save slots, multiple concurrent worlds, or any future multiplayer would be a genuine refactor, not a feature flag.
7. **The chain is a single point of total failure.** Pure derivation means a corrupted `blocks-*.json` breaks *everything* downstream at once. `ChainIsValid` detects it; nothing yet recovers from it. A periodic chain backup (cheap — it's the only file that matters) would convert "world lost" into "world restored," and is probably the highest-value low-effort hardening item on this list.

**Net assessment.** The pros are compounding (every new system built on derivation inherits all six advantages automatically — the auction got retroactive healing without asking for it), while the cons are mostly *scheduled* rather than structural: performance work before late-game scale, permanence design per feature, shim deletion, a reset policy before release, one backup mechanism. That trade — pay with planned engineering later, get correctness and velocity now — is the right one for a solo experimental project, and it is why development has been able to rework whole systems (auction model, fee model) in single-day rounds without ever writing a migration.

### 37.4 — The story to tell (the honest pitch, for future users)

Angles that are both promotional *and true*, in rough order of strength:

1. **"You don't read about early Bitcoin — you move in."** The game starts the day the genesis block was mined. Difficulty, network growth, fees, and the market all follow the real 2009–2025 history at 1:100 scale. Mt. Gox opening (18 Jul 2010) is not a tutorial unlock — it's a date you *wait for*, mining BTC that has no price yet, exactly as the first miners did.
2. **"Every satoshi is real."** Balances aren't numbers in a save file — they're unspent transaction outputs on a real (miniature) blockchain, with real coin selection, change addresses, signatures, and fees. The Block Explorer isn't a decoration: it's the actual ledger, and a curious player can audit the whole world from it. The game *cannot* fake a balance, because balances don't exist as stored values.
3. **"Time only moves when you act."** Every bet is one mining attempt and 100 seconds of world time. History unfolds at the pace of your own play — an idle world is a frozen world. This single rule fuses the casino, the mining, and the history into one loop.
4. **"It teaches by consequence, with no real money."** Bankroll discipline, progression systems, auto-recharge, stop conditions — the full vocabulary of gambling risk management — operating on a 99.02% RTP that is *disclosed*. The long-run house edge isn't hidden; watching it grind against your strategies is the lesson. The same honesty applies to mining: rewards halve, difficulty regulates, and the era decides whether your hardware matters.
5. **"Nothing is scripted that can be simulated."** Bots bid in real auctions with real BTC they really mined; fees replay real history; the difficulty regulator reacts to actual network power. The few scripted moments (Satoshi's arc, Hal's fade, Hearn's round-trip) are historical events, labeled as such.

The anti-pitch to avoid: this is not a get-rich game, not a crypto product, and not a trading simulator with dopamine dressing. Its promise is narrower and more durable — *the most honest small-scale reconstruction of how Bitcoin's economy actually worked, playable as a casino survival game*. Marketing that overclaims past that will attract users the design will disappoint.

---

## Chapter 38 — Event-Driven Design: `_Process` Only Where Truly Necessary

**Status**: Design-principle chapter, written 2026-07-21 at the developer's explicit request, after the ND.8d round-3 stuck-bidder-escalation fix applied this principle directly — a considered-and-rejected per-frame/history-replay design was replaced with an in-memory, edge-triggered signal. The developer asked that this become a standing rule for every future system, not a one-off, and that its enforcement be tracked as a goal before Basic Mode v0.1 ships.

### 38.1 — The principle

`_Process(double delta)` runs on every rendered frame, forever, whether or not anything relevant has changed. GamblingMiner's core loop is already fundamentally discrete and event-shaped — time only advances on a bet, blocks are discrete mined events, transfers are discrete ledger entries — so almost nothing in this project's domain model is *actually* continuous. Reaching for `_Process` by habit, rather than by need, is the most common way to smuggle needless per-frame work into a codebase whose real state changes are all point events.

**The test to apply before writing any `_Process` override:** does this genuinely need to observe the passage of REAL time, every frame, to do its job? If yes, `_Process` is the right tool. If the code only re-reads STATE that changes on a discrete occurrence elsewhere in the program — a bet settling, a block being mined, a transfer completing, a claim being pressed — that is the **polling anti-pattern**: the owner of that state should fire a typed event at the exact point it changes, and every consumer should subscribe once and react, never poll.

### 38.2 — Genuine exceptions (real continuous processes)

These need `_Process` and should not be "fixed":

- **`CalendarTimeService`** — advances `CurrentLocalDateTime` by real delta × `SpeedMultiplier` every frame. This *is* the game clock; nothing else could drive it.
- **`SimulationService`** — the background simulation's own per-tick loop: player autobet pacing, bot betting cadence, founder mining, the board-vote pause gate. Genuinely continuous background work that must keep running across scene changes.
- **`DiceGame.TickAutoBet`** — the autobet loop's per-bet timing and UI animation each frame. `DiceGame._Process` also does an edge-triggered flag read for the board-vote pause banner (a one-line comment there already states the discipline: "the per-frame cost is one static flag read; buttons/notice update only on change" — exactly the hybrid pattern in §38.3 below).

### 38.3 — The hybrid middle case: a cheap edge-trigger inside `_Process`

Some state changes on a boundary that isn't itself a discrete game event fired by another system — a calendar day rolling over is the clearest example, since nothing "emits" that; it's simply a fact about the advancing clock. `BtcMarketDataService` and `BtcNetworkDataService` are the reference implementation: `_Process` does the cheapest possible operation — one cached-date comparison against the game clock — every frame, and fires `MarketDayChanged`/`NetworkDayChanged` **only** on the frame a day boundary is actually crossed. No timers, no per-frame parsing, no I/O on the frames nothing changed.

If a system genuinely needs to poll something inside `_Process`, this is the shape to copy: the per-frame cost must be a single cheap comparison, and all the real work (rebuilding a panel, touching disk, recomputing a derived value) happens behind the resulting edge — never unconditionally inside the poll.

**A signal doesn't have to be a Godot/C# `event` to follow this discipline.** An in-memory flag or small tuple, updated only when it actually changes, is the same idea applied without a subscriber list. `NetworkRoot._stuckBidderSignatures` (ND.8d round 3, 2026-07-21 — §12.5.5 of the step14 plan) is the freshest example: rather than replaying a pool's full bid history on every roll to answer "how long has this bot been stuck at this tier" (correct, but neither cheap nor `_Process`-shaped), or polling anything per frame, it stores one `(signature, sinceBlockIndex)` pair per (company, bot), updated exactly once — when the signature changes — inside the SAME block-mined event that already drives the whole bidding cascade (`HandleMinedBlock` → `ScheduleBotTransactionsAfterBlock` → `TryCasinoBotDonation`). No new persisted state (it deliberately isn't part of `BlockchainStateSnapshot` — see CLAUDE.md's `_lastMinedByNodeId`/`_currentMinerStreak` precedent), no per-frame cost, no history replay. When a "since when has X been true" question comes up in a future system, this shape — an edge-triggered signal living beside the event that could change it — should be the first thing reached for, before either a poll or a full replay.

### 38.4 — Already-good examples (services firing typed events on real state changes)

The project already leans on this pattern successfully in many places — it is the established norm, not a new idea:

| Event | Fires when | Notable subscribers |
|---|---|---|
| `UserStatsService.StatsChanged` | A bet/deposit/rollback, throttled 250ms while high-frequency | `BetsHistoryExplorer`, `FinancialBettingStats` |
| `SimulationService.ClientBetSettled` | Every settled bot/player bet | `ClientsBetsHistory` |
| `BtcMarketDataService.MarketDayChanged` | A calendar day boundary crosses (edge-triggered, §38.3) | `CasinoCoinSwaps`, `ScFinances`, `CasinoCoinSwapService`, `StatusBar` |
| `BtcNetworkDataService.NetworkDayChanged` | Same edge-triggered pattern | (reserved for future consumers) |
| `CasinoClientLedgerService.LedgerChanged` / `ScMonetaryLedgerService.LedgerChanged` | A ledger entry is recorded | `ClientsTransactions`, `WorldEconomy`, `FinancialBettingStats` |
| `CasinoCoinSwapService.SwapDeskChanged` | A swap executes or a desk knob changes | `CasinoCoinSwaps` |
| `PrincipalBalanceService.BalanceChanged` / `CasinoScBalanceService.BalanceChanged` | A balance mutates | `CasinoCoinSwaps`, `CasinoGamblingFinances`, `ScFinances`, `ScTransactions`, `CasinoCoinSwapService` |
| `PlayerBankAccountService.BankStateChanged` | A bank transfer/setting changes | `ScFinances`, `ScTransactions` |

### 38.5 — Known migration candidates (audited 2026-07-21 — the backlog, not a mandate to stop and fix immediately)

Roughly fifteen scenes share the identical shape: accumulate `delta` in `_Process`, gate on a `RefreshInterval`/`FallbackInterval` timer (commonly 1–3 seconds), then unconditionally rebuild a panel or list from service state that in truth only changes on a settled bet, a mined block, or a transfer. None of these are broken — they work — but each is a candidate for migrating to "build once in `_Ready()`, then rebuild only when a subscribed event actually fires":

`StatusBar`, `FinancialBettingStats`, `CalendarsNavigator`, `BetsHistoryExplorer`, `BTCWallet`, `AuctioningCompanyDetails`, `CompanyDetails`, `BlockExplorer`, `CasinoFinances`, `BotPlayHistory`, `ScFinances`, `ScTransactions`, `CasinoGamblingFinances`, `ClientsTransactions`, `ClientsBetsHistory`, `FoundersWallets`, `CasinoCoinSwaps`, `BTCPoolsAndHardwareShop`, `BotsBtcWallets`.

Two implementation notes for whoever migrates one of these:
1. **The existing input-preservation caveat still applies.** Several of these panels rebuild on a timer specifically so an in-progress edit (a SpinBox, an OptionButton) survives — the established fix (see `CompanyDetails.RebuildOrUpdateActions`'s signature-gated rebuild, §22.12) is to split "always-safe-to-rebuild" info from "rebuild only on a real state-shape change," not to abandon periodic refresh entirely where user input is live.
2. **Not every candidate needs a NEW event.** Some of these already have an applicable event in the table above and simply never wired to it (they predate the event, or were built by copying an existing polling scene rather than the newest event-driven one). Check §38.4 before adding a new event — the state you need may already be announced.

### 38.6 — The Basic Mode v0.1 gate

**Goal, tracked in `Documentation/PRIVATE_ROADMAP.md` §6:** before Basic Mode v0.1 is considered complete, run a dedicated audit pass over every `_Process` override in the project against the §38.1 test, and migrate what's feasible from the §38.5 backlog to event-driven design. This is explicitly **not** a blocker on other Basic Mode work — it is a scheduled cleanup pass, the same category as the T1–T3 tech-debt tasks in `PRIVATE_ROADMAP.md` §8 — but it must happen before v0.1 is called done, both for the runtime cost (fifteen-plus scenes each doing unconditional rebuild work on a timer compounds as more scenes and more session length accumulate) and because every new system built the polling way from here on adds to the very backlog this chapter exists to eventually clear.

**Standing rule for all new work, effective immediately:** before adding a new `_Process` override anywhere in this project, apply the §38.1 test first. If an equivalent event already exists (§38.4), subscribe to it instead of polling. If the state change is genuinely a new discrete event with no owner yet, add the event to whichever service owns that state rather than reaching for a timer in the consumer.

### 38.7 — The inverse failure: a correct event, fired far too often, driving expensive work (R3, 2026-07-28)

Everything above is about *polling where an event would do*. The swap desk hit the mirror-image fault, and it cost more than any poll in §38.5: **the subscription was correct, the event was correct, and the handler was catastrophically expensive at that event's real frequency.**

`CasinoCoinSwapService` (an **autoload** — always alive, in every scene) subscribed to `CasinoScBalanceService.BalanceChanged` and ran the full `RecomputeAvailability` on it. That handler is a **chain-side** recompute: `AggregateSpendable` walks the casino's entire address book, and `GetCasinoBtcSettlement` re-derives every undistributed pool event against the UTXO set. But since ND.8f made all five clients casino players, `ApplyBetResult` fires `BalanceChanged` **on every settled bet — up to ~20 per frame**. An SC balance movement cannot change one single chain-side figure, so essentially all of that work was pure waste, and it was the dominant term in the simulation's frame time.

**How it hid, and how it surfaced.** It did not present as a performance bug at all: R2-C1's `SimulationThrottle` (Ch. 26 / `btc-pools-hardware-plan.md` §R2) correctly converts an engine that cannot keep up into a *slower clock* rather than a corrupted simulation. So the only symptom was the player reporting that raising the DEV time scale to 9000X "advances time much more slowly than it used to" — a complaint about the **clock**, whose actual cause was a swap-desk subscription. The proof was already being written to disk: `difficulty_trace.csv`'s `simSecOffered`/`simSecConsumed` columns read a flat **1/6** across hundreds of blocks, i.e. the bet engine retained exactly its `MaxBacklogSeconds = 2.0` per frame and dropped the rest, which pins the frame time at `2.0 / (offeredPerFrame / delta)` — ~133 ms.

Three lessons, all of which generalise:

1. **Frequency is part of a subscription's contract.** Before subscribing an always-alive autoload to an event, ask what the event's *real* rate is under load — not what it was when you wired it. `BalanceChanged` was a reasonable, low-rate event when only the player bet; ND.8f multiplied its rate by five and nothing re-examined its subscribers.
2. **Coalesce at the consumer when the trigger cannot move the value.** The fix keeps `BlockAccepted` / `MarketDayChanged` **immediate** (they genuinely move the chain-side figures, and fire once per block / once per in-game day) and routes `BalanceChanged` through a dirty flag drained at most every `AvailabilityCoalesceSeconds` (0.25 s) in `_Process`. That is §38.3's documented hybrid used deliberately: per-frame cost is one `bool` test, and the real work sits behind the edge.
3. **A displayed throttle is a *measurement*, not a diagnosis.** R2-C1 did its job perfectly — it made the shortfall honest and visible instead of silently stretching in-game block times. It does not say *why* the engine is short. When `simSecConsumed / simSecOffered` sits below 1, the next question is always "what is eating the frame", never "raise the caps": raising `MaxBacklogSeconds` / `MaxBetsPerFrame` only hands a saturated frame more work, and total throughput does not move.

A second, independent fix shipped alongside it and is worth stating on its own, because it helps every wallet panel and every bot affordability check too: **`NetworkRoot.AggregateSpendable` now makes ONE pass over the UTXO set for the node's whole owned address set**, instead of calling `GetAddressSpendableBalance` once per address — each of which walked the *entire* UTXO set and rebuilt the pending-spent outpoint set. That is `O(addresses × utxos)` collapsed to `O(utxos)`, with an identical result by construction (an outpoint has exactly one address, so no double counting is possible). The casino, whose change rotation gives it the largest address book in the world, was the worst case and also the one the swap desk asked for on every bet.

---

## Chapter 39 — The Central Bank (FED): The Two-Layer Debt Architecture (Step 15 P15.1)

**Status**: Implemented 2026-07-26 (Step 15 phase P15.1, subphases a–e), on branch `bank-companies-sc-provisioning`. Design locked in `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md` (`D-15.1`, `D-15.3`, `D-15.5`, `D-15.10`, `D-15.16`, `D-15.17`, `D-15.23`). This chapter documents what P15.1 built; the bank companies that become the FED's other clients arrive at P15.2–P15.4.

### 39.1 — What the FED is, and what it deliberately is not

Before Step 15 the casino borrowed SC from an **abstract, off-screen "bank"**: `CasinoScBalanceService` incremented its own `LoanCount`/`TotalLoaned` counters and appended to its own `LoanHistory`, and mirrored each draw into `ScMonetaryLedgerService` as `"casino"` debt. Nobody was on the other side of the transaction. P15.1 makes that counterparty an explicit in-world entity — `CentralBankService` (autoload #19) — with its own persistence (`user://central_bank_state.json`), its own DEV scene (Main Menu → *Central Bank [DEV]*, D-15.16) and per-client accounts.

**What it deliberately is NOT, in plan15 (D-15.1):** it has **no interest rate** and **no credit limit**. Every `DrawLoan` succeeds, exactly as the casino's auto-loan always has. The fed-funds-rate historical replay and the per-client credit-capacity limits are ND.8e's job, one layer *above* the banks; putting them here would have coupled the entity to the policy. Unlimited zero-rate lending is also period-accurate for the ZIRP 2009–2015 window the game spends most of its time in.

**Its clients:** the casino today; the four CB1 bank companies from P15.2 (keyed `bank:<companyNodeId>` via `CentralBankService.BankClientId`). The casino is the sole entity exempt from dissolution (D-15.17) — its credit line never closes.

### 39.2 — The two layers, and why they were NOT merged (Fork A, D-15.23)

The obvious instinct on seeing "the casino stores its debt AND the monetary ledger stores its debt" is to collapse them into one store. The design deliberately collapses only *half* of that:

| Layer | Owner | Holds | Question it answers |
|---|---|---|---|
| **Entity / relationship** | `CentralBankService` | Per-client accounts: `OutstandingDebt`, `TotalDrawn`, `TotalRepaid`, `DrawCount`, `RepayCount`, and a capped per-client `History` of draws + repayments | *Who owes the FED what, and how did they get there?* |
| **Macro accounting** | `ScMonetaryLedgerService` | The mint/burn event log, the genesis grants, `_debtByBorrower`, and the standing invariant `circulation = grants + debt` | *How much SC exists in the world, and why?* |

**Fork A (adopted):** the FED becomes the authoritative *per-client* store, the casino's private copy is deleted, and the ledger keeps its macro projection — synced **for free**, because the FED's write API calls the ledger's existing hooks. `DrawLoan` → `RegisterLoanDraw` (mint); `Repay` → `RegisterBurn` (burn). This is the identical lockstep that already kept `_debtByBorrower["casino"]` equal to `casino.TotalLoaned`, just moved one layer out.

**Fork B (rejected, deferred as optional cleanup):** retire `_debtByBorrower` entirely and have the ledger derive its debt total from the FED. Genuinely *one* debt store — but it refactors the ledger's checkpoint DTO and its reconciliation path, both of which are audited invariant machinery, for a duplication that Fork A already removes at the level that mattered (the casino's copy). Not scheduled.

The two responsibilities are genuinely different questions, and the FED will grow relationship-shaped features the ledger has no business holding (custodial seized wallets at P15.5c, per-client collateral at P15.2c). Keeping them apart is the point, not a compromise.

### 39.3 — `RegisterBurn` finally has a caller

`ScMonetaryLedgerService.RegisterBurn` shipped at ND.8c **armed and caller-less**, with a comment saying it was waiting for the Central Bank subphase. `CentralBankService.Repay` is that caller. A repayment clamps to what the client actually owes, decrements `OutstandingDebt`, appends a `"repay"` record, and burns the SC out of existence — Option A of the ND.8c fiat-debt ladder (repayment destroys SC; Option B fractional-reserve stays post-Basic-Mode, Option C inflation is rejected forever since the 1:1 USD peg is canon).

Nothing in P15.1 repays: the casino never does, by design (D-15.17). The path exists so the invariant `circulation = grants + debt` is provably intact across a draw→repay pair *before* P15.4 makes bank quarterly repayments the real, high-volume caller. The FED scene surfaces this directly — it prints the invariant and an explicit **FED/ledger debt in sync ✓ / OUT OF SYNC ✗** marker, so drift between the two layers is visible rather than silent.

**One subtlety worth stating**, because it is the first place the two figures can legitimately differ: `TotalDrawn` is cumulative and never decreases; `OutstandingDebt` decreases on repayment. The casino's `TotalLoaned` shim maps to **`TotalDrawn`**, deliberately — `CumulativeProfitSinceLoan = TotalSc − TotalLoaned` means "ahead of every loan ever taken," and mapping it to outstanding debt would silently redefine the casino's P/L metric the moment anyone repays. The ledger's reconcile, by contrast, compares against **`OutstandingDebt`**, because that is what "debt" means in the monetary invariant.

### 39.4 — The casino as a read-through client (P15.1c)

`CasinoScBalanceService` keeps no loan state of its own. `LoanCount`, `TotalLoaned` and `LoanHistory` are now **read-through accessors** over its FED account, and `OutstandingFedDebt` is new. Its three draw sites (the bankruptcy dose recharge in `TryAutoRecharge`, the dev `TriggerManualLoan`, and the provisional company-provisioning path `TryPayCompanyProvisionSc`) all funnel through one private `DrawFedLoan` — preserving ND.8c's "one hook covers every draw site" property, one layer further out.

**A load-order note that matters when adding future clients:** `CentralBankService` registers *after* `CasinoScBalanceService` in `project.godot` (it must sit between `ScMonetaryLedgerService` and `BlockSessionCheckpointService`, so it is in the tree before the checkpoint restore/reset runs — the `PlayerBankAccountService`/`CasinoCoinSwapService` precedent). So the casino **cannot** resolve the FED in `_Ready`; it resolves lazily on first use and null-guards. That is also why the casino's `_Ready` print no longer reports loan figures — they would read 0 at that instant, which would look like data loss and isn't.

### 39.5 — Persistence, checkpoint and the version bump (P15.1d)

The FED answers all three of CLAUDE.md's mandatory questions for a new persisted player-facing service:

1. **Checkpoint restore (post-block):** `CaptureCheckpointState()` / `RestoreFromCheckpoint(state)` on the `BlockSessionCheckpointService` snapshot. **Ordering is load-bearing** — the FED restores *before* the casino (which reads its loan figures through the FED) and *before* the monetary ledger (whose reconcile and legacy live-state init both read the FED's casino account).
2. **Pre-genesis reset:** `ResetToPreGenesisDefaults()` → no accounts at all. It runs *before* the casino's reset, which no longer clears the loan counters that moved here.
3. **World-reset delete list:** `central_bank_state.json` was added to `NetworkRoot.ResetWorldIfIncompatible` **with the feature** — the TL.3 maintenance rule.

**`WorldFormatVersion` 3 → 4 (D-15.10).** The casino's loan fields leave both `casino_sc_balance_state.json` and the checkpoint DTO. Rather than write a migration for a DEV-era save, world-defining banking semantics ride the same clean-reset mechanism every previous bump used. **Every later plan15 file simply joins the delete list — no further bump for the rest of the plan.**

**Standing project rule, restated here because it governs every phase of this plan:** persisted-state changes are resolved by **bumping the version and wiping `user://`**, never by writing migration code and never by pausing to ask. The developer does not keep long-running playtests alive across feature work, so an old world has no value to preserve and migration code is pure cost. The only reason to *avoid* a bump is when the affected value is genuinely **derivable** — re-deriving a bank's locked category from the roster (§39.7) is a simplification, not a migration.

### 39.6 — The FED DEV scene (P15.1e)

`Screens/CentralBank/` — one section per client (outstanding debt, total drawn/repaid with counts, and the newest 60 movements colour-coded orange=draw / green=repay), plus system totals and the invariant line from §39.3. Pure display: it never draws, repays or mutates anything.

Two conventions it follows deliberately:

- **Ch. 29 layout**, read before building it: bounded chain `MarginContainer → VBoxContainer → ScrollContainer(size_flags_vertical = 3)`, Back button in a **fixed footer outside the scroll** (§29.10), 50 px bottom margin clearing the off-screen band (§29.11), rows as plain `Label`s (Pattern A — they report honest minimum heights and default to `mouse_filter = IGNORE`, so the wheel reaches the scroll; the one bare `Control` spacer sets `IGNORE` explicitly, since `Control` defaults to `STOP`).
- **Ch. 38 event discipline**, since this is new work: it subscribes to `CentralBankChanged` + `LedgerChanged` rather than polling. Because a casino recharge streak can fire many draws per second, the events set a **dirty flag** coalesced at 0.5 s instead of rebuilding per event, with a slow 5 s fallback as a safety net. It is deliberately **not** a new entry in the §38.5 migration backlog.

The FED's per-client history is capped at its newest 500 records (totals stay exact independently of the cap — the `ScMonetaryLedgerService.MaxEventHistory` precedent), and both the FED scene and `CasinoGamblingFinances` report any surplus as *"(+N older)"* so a count and its list can never look inconsistent.

### 39.7 — The four bank companies: typed, categorised, locked (P15.2)

P15.2 makes the four CB1 roster banks structurally real without changing where a single SC flows — conversions still go to the casino until P15.3.

**The Official→Black gradient (P15.2a, D-15.20/App. A).** The `market_category` column of three bank rows changed: First Satoshi Savings stays `official`, Digital Reserve Trust → `light_grey`, Ledger & Sons → `dark_grey`, Harbor Coin Bank → `black`. This is not flavour — it is the **distance axis** the §5.1 selection algorithm measures on. A darker bank consequently inherits §12.4.3's higher default dividend rate *and* (from P15.6) higher seizure risk: a shady bank that pays well but can be busted. That is the intended trade, confirmed at design lock.

**Identifying a bank: a closed id set, not a CSV column.** `CompanyRoster.BankCompanyIds` (+ `IsBank`/`Banks`) holds the four ids. plan15 explicitly adds behaviour to existing companies and creates none (D-15.6), so the set is closed by design; an `is_bank` column would have touched all 44 rows to encode four `true`s, and would let a 45th row silently become a bank. Every caller goes through `IsBank`/`Banks`, so promoting it to a column later changes nothing else. The loader warns if the set and the CSV ever disagree.

**Locking the category (P15.2b, D-15.12).** Banks are the **only** companies exempt from the ±1 market-shift vote. A drifting bank would silently re-shape which companies bank where; in exchange banks gain the seized-wallet holding feature (P15.5c). Only the *application* is blocked — the supermajority is still computed, and the governance trace records `shift=±1;shift_refused=bank_locked`, so a refused attempt reads as "the holders asked and were denied" rather than "nobody asked."

**A locked category is a DERIVED value — so it self-heals on restore.** Because a bank's category can never move by vote, it is always exactly its roster default, which means re-deriving it from the roster on every snapshot restore is *always* correct. `ApplyStateFromSnapshot` does exactly that. This is what let P15.2a reassign three categories **without a second `WorldFormatVersion` bump**: a bank that founded under the old roster is corrected on the next load instead of stranding a stale category. The general rule worth carrying forward: *if a persisted field is guaranteed derivable from static data, re-derive it on restore rather than bumping the world format to re-seed it.*

**The layer-1 balance sheet (P15.2c, D-15.4/D-15.5).** `NetworkRoot._bankState` maps a bank's `NonMinerNodeId` → `BankBalanceSheet { CollateralBtc, Clients }`, where each `BankClientAccount` carries `{ BtcBought, ScPaid, ProvisionCount, History }`. It rides `BlockchainStateSnapshot`, so checkpoint coverage, delete-list membership and the pre-genesis path are all inherited (the ND.8g argument) — a bank can only have a balance sheet once founded, i.e. 2012-09 at the earliest, so it needs no path of its own. `CollateralBtc` is the **quarantined** account of §3.2: BTC bought from financed companies, deliberately excluded from the bank's own CB1 auto-convert and sold extra-lazily only on a payment day (P15.4). Two BTC streams, one wallet, two books. Per-client history caps at 200 with exact totals — same shape as the FED's and the ledger's.

### 39.8 — `SelectFinanciers`: building the dormant tiers on purpose (P15.2d, D-15.20)

`SelectFinanciers(companyNodeId, amountSc)` returns an ordered list of `FinancierChoice(BankNodeId, AmountSc, Tier)` summing to the requested amount, with a **null `BankNodeId` meaning the casino fallback**.

| Tier | Rule |
|---|---|
| 1 `nearest` | The founded bank nearest the company's *current* category by `\|catCompany − catBank\|`; ties break **toward Official** (A1 — "a business reaches for the cleaner bank first"), then toward the earlier-founded bank so the order is total |
| 2 `funder` | Nearest bank that can fund the **whole** amount alone |
| 3 `split` | Spread across banks: nearest-category first, most capacity first within an equal preference |
| — `casino` | No founded bank at all (every date before 2012-09, or a category with no founded bank) — D-15.20 (c) |

**Tiers 2 and 3 are unreachable today, and that is the decision (B1).** `BankFundingCapacitySc` returns `decimal.MaxValue`, because a bank funds every provision with an unlimited FED auto-loan (D-15.1 defers all credit limits to ND.8e), so tier 1 always wins. They were still built because they are *real, compiled, reviewed* code paths that light up the day limits ship — turning the eventual ND.8e work into a change to **one method** rather than a rewrite of the selection logic. `BankFundingCapacitySc` is deliberately `private`: nothing outside selection should read a capacity that is knowingly fake.

Selection is evaluated **fresh at every conversion**, because a company's category can still shift ±1 by vote while a bank's cannot — which is precisely what keeps the axis stable underneath a moving company.

**Verifying a phase that changes no behaviour.** P15.2 deliberately routes nothing new, which would leave it unverifiable in-game — so the FED scene gained a read-only **Banking layer** block: the founded banks (locked category, FED debt, `CollateralBtc`, client count) and a **financier-selection preview** row per founded company showing which counterparty it *would* route to and at which tier. It probes with a nominal 1 SC, honest only because capacity is currently infinite — a note to revisit when ND.8e makes the amount matter. This is an early slice of P15.7a, pulled forward because a phase you cannot observe is a phase you cannot sign off.

### 39.9 — The credit loop closes: conversions route through banks (P15.3)

`TryConvertCompanyReserves` keeps every calibration floor and its clean-rate pricing (D-ND8.24); what changed is **who is on the other side**. It now asks `SelectFinanciers` and dispatches to one of two paths:

- **`TryConvertViaCasino`** — byte-for-byte the old ND.8b.6 behaviour, surviving *only* as the pre-first-bank fallback (D-15.20 (c)). SC leaves the casino's Main Balance (auto-loan when short, so the draw still lands on the casino's FED account), BTC comes in, memo `CONVERSION`.
- **`TryConvertViaBank`** — the reform. The bank draws the SC from the FED (`DrawLoan("bank:<id>", …, "provision")`, minting it as that bank's debt), the company's `ScReserve` is credited, and the BTC lands in the bank's wallet as quarantined `CollateralBtc`, memo `COLLATERAL`.

**What the bank pointedly does NOT do is touch its own `ScReserve`.** The borrowed SC passes straight through to the company, leaving the bank holding a FED debt on one side and collateral on the other. That spread — carried until the quarterly repayment (P15.4d) — *is* the economic point of the whole reform (§1): the bank sits long BTC on borrowed money.

**Unwinding.** Both paths put the SC leg first and unwind it if the broadcast fails, mirroring the SW.4 swap-desk pattern. The bank's unwind is the more interesting one: it **repays the loan it just drew**, which burns the SC back out of existence and leaves `circulation = grants + debt` exactly as it was. The client book is written only after *both* legs succeed, so it can never record a half-executed swap.

**A bank finances peers, never itself.** `SelectFinanciers` filters the requesting company out of the candidate list, so a bank converting its own CB1 business inflows routes to another founded bank — or, while it is the only one (2012-09 → 2013-06), to the casino. Its own inflows convert normally; only the collateral stream is special.

**Tier 3 is where the honesty matters.** A split across banks would need the BTC leg split into several sends with their own fees, which is *not* built — it is unreachable while capacity is infinite. Rather than silently executing a half-split, the code detects a multi-financier answer, warns, and funds the whole conversion from the casino. Together with `BankFundingCapacitySc`, that is the complete list of what ND.8e must touch to switch real credit limits on: two places, both flagged in comments.

#### 39.9.1 — The quarantine, and the three places collateral must not leak

`CollateralBtc` and the bank's own money live in **one wallet but two books**, so every computation that means "the company's own money" has to net the collateral out. The split is expressed as one helper pair: `CompanyTreasuryBtc` (raw on-chain spendable) and **`CompanyOwnBtc` = treasury − quarantined collateral** (which returns the plain treasury for every non-bank, since their collateral is 0). `CompanyOwnBtc` replaced `CompanyTreasuryBtc` at all three governance sites:

1. **The reserve-mix conversion base** — otherwise a bank would try to sell the very collateral backing its debt to top up its own SC reserve.
2. **The quarterly dividend base** — otherwise shareholders would be paid out of the collateral, draining the asset while the FED debt stayed put.
3. **The >30%-inflow vote baseline** — see below.

The fourth leak is not a treasury read at all: **`AccumulateCompanyInflows`**, which measures new BTC arriving at a company address to trigger the D-ND8.18 special vote. A collateral arrival is not business inflow — it is the asset leg of a loan — and counting it would fire spurious special votes at a bank. Where the player holds NST in that bank, **every one of those pauses the game**. The fix rides the existing display-memo channel: the provisioning send is tagged `COLLATERAL` at broadcast and skipped here. That makes the memo *load-bearing* rather than cosmetic on this one tx type — worth remembering before treating `InputDataText` as purely a display concern.

**Why this landed in P15.3 rather than P15.4a as planned.** Without the quarantine, a bank's own conversion would sell collateral BTC while `_bankState.CollateralBtc` still claimed it existed — a **persisted figure diverging from reality**, which P15.4d would then try to sell. That is a different category of problem from an unfinished feature, so the ~3-line netting shipped with the mechanism that creates the collateral.

**Telemetry.** `user://logs/bank_credit_trace.csv` (one row per provision now; repayments, shortfalls, dissolutions and seizures as those phases land) also shipped early, for the same reason: it is the only observability the credit loop has before the P15.7 readouts, and the P15.8 calibration run reads it. Delete-listed with the feature, per the TL.3 rule.

### 39.10 — Greed: a third governance axis (P15.4b/c)

`BotGovernancePreference` gains **`GreedPreference`** ∈ `not_so_greedy · almost_greedy · greedy · extremely_greedy`, drawn per world as its own shuffled permutation alongside the D-ND8.13/26 currency-band and market-category draws — so with four bots all four stances are always represented exactly once, and which bot holds which changes per world.

It answers one question and one only: **money in shareholders' pockets vs. money kept in the company.** It therefore biases the quarterly payout-rate ballot (all companies, P15.4c) and the P15.4e shortfall split — and pointedly **not** the reserve-band (currency-mix) vote, which is a different axis entirely (D-15.13).

The payout ladder is written as a multiplier on the category's default rate — `0.5 / 1.0 / 1.5 / 2.0` — chosen to span exactly the ballot's existing `[0, 2× default]` clamp. Note that `almost_greedy` sits at `1.0`: the pre-greed behaviour ("bots vote the standard") is now *one of four stances* rather than the only one, which is what makes this change additive rather than a re-tuning of existing balance.

**The field defaults to `""`, not to a stance — deliberately.** Greed arrived after the other two axes, and `EnsureBotGovernancePreferences` early-returns once every bot has a preference record, so a world whose preferences were already drawn would never re-draw and would sit on the neutral stance forever, **silently disabling the whole axis**. An empty default makes "absent" distinguishable from "genuinely drew neutral", which is what lets `BackfillGreedPreferences` fill *only* the missing slots while leaving the already-meaningful band/market choices untouched. Readers normalize empty → `almost_greedy`, so an un-backfilled world behaves exactly as it did before.

To be clear about *why* this is worth doing, since it is **not** about dodging a version bump — this project's standing rule is the opposite (see §39.5: wiping `user://` and re-testing clean is the norm, and a bump is always the cheap answer to a persistence change). The sentinel earns its place because the failure mode here is **silent**: a stance-valued default produces a world that loads fine, plays fine, and quietly has one of its three governance axes pinned flat. A bump fixes that only if someone notices it is needed. **The general shape worth copying: when adding a field to an existing per-entity record whose population code is guarded by a "already populated?" check, default to a sentinel and backfill it — so a stale record announces itself instead of masquerading as a valid one.**

### 39.11 — Extra-lazy repayment and the shortfall vote (P15.4d/e)

**The payment day.** On each quarter end — after the closing quarter's dividends settle, before the new quarterly vote opens — a bank owes `BankQuarterlyRepaymentFraction` (placeholder **10%**, a P15.8 knob) of its *outstanding* FED principal. It sells **just enough** collateral to raise exactly that, repays, and the SC is **burned**. Nothing is sold between payment days: that is the "lazy," and it is precisely what leaves the bank long BTC in the interim (§1).

**The fee comes out of the collateral pool**, not the bank's own money. Coin selection cannot tell the two streams apart, so `CollateralBtc` is decremented by `sold + fee` — the book stays conservative and can never claim BTC the wallet has already spent, which is the same §39.9.1 rule that pulled the quarantine forward into P15.3.

#### 39.11.1 — Whose BTC is being sold, and who buys it

This is worth spelling out because the word "collateral" invites two wrong readings.

**The collateral belongs to the BANK, outright.** When a company sells BTC to a bank (§39.9), that is a **clean spot sale** — D-15.11 — and the transaction is *finished the moment it settles*. The company keeps the SC and owes nothing, ever. The BTC is now the bank's property. It is called "collateral" only because the bank earmarks it against the FED loan it took to fund that purchase; it is not posted to anyone, not held in escrow, and not encumbered by any counterparty.

**The only debt anywhere in this system is bank → FED.** Companies carry none (spot sale). The casino carries its own, separately, from its own borrowing. So when a bank sells collateral to service its debt, no other company's obligations are touched or involved in any way — the companies it once financed are entirely out of the picture.

**Why a buyer has to exist at all.** BTC lives on-chain: it cannot be "sold to the market" in the abstract, because every satoshi must end up at a real address. So the design has to name a counterparty. That counterparty is the **casino**, at the day's clean rate (no desk fee — the D-ND8.24 model the company conversions already use). It is the designated SC liquidity backstop: the only party with an unlimited credit line (D-15.17), and already the SC side of both the swap desk and the pre-first-bank conversion path.

**The side effect worth watching (a P15.8 observation).** This is **not always a net burn**:

- If the casino pays out of SC it already holds, circulation genuinely **falls** by the repayment amount — the intended Option-A behaviour.
- If the casino must draw a FED auto-loan to afford the purchase, the same SC is minted as *casino* debt and immediately burned as *bank* debt. Total circulation is unchanged; the debt has simply **moved** from the bank to the casino, and the BTC moved with it.

Both are coherent, but they mean "banks repay their debt" can quietly become "the casino absorbs it." Which one dominates over a long run is a calibration question for P15.8, and a candidate input for ND.8e's credit-capacity work.

#### 39.11.2 — The shortfall, and what the two "sources" actually are

If selling everything still can't raise the installment, the gap is parked on `gov.PendingShortfallSc` and a **`shortfall` vote** opens — but not immediately. It waits until no other vote is running, which in practice means *after the quarterly closes*, because the dividend it may cut has to have been finalized first. It takes precedence over the >30%-inflow special vote (an unpaid installment is the more urgent question).

**The ballot is one dial**: what share of the gap comes out of shareholders' dividends vs. the company's own SC reserve. Default 50/50 (no or tied vote); bots ballot the §3.3 greed table (`not_so_greedy` 90% dividends-cut … `extremely_greedy` 10%). A greedy holder protects its own dividend and makes the company pay.

**The two "sources" are not two pots.** A company has exactly **one** pot of SC — `gov.ScReserve`. There is no separate balance labelled "dividends," so the shortfall is paid out of the reserve *either way*. What the vote actually decides is whether **shareholders' claim shrinks alongside it**.

Worked example. A bank owes a **1,000 SC** installment, raises **700** by selling collateral, leaving a **gap of 300**. It holds `ScReserve = 500`, and this quarter's finalized `QuarterDividendSc` is `200`:

| Vote | `ScReserve` after | `QuarterDividendSc` after | Balance sheet afterwards |
|---|---|---|---|
| **70% dividends cut** | 500 → **200** | 200 → **0** | 200 SC free and clear — shareholders forfeited their whole claim |
| **10% dividends cut** | 500 → **200** | 200 → **170** | 200 SC of which **170 is already owed out** — only 30 free |

The same 300 SC leaves the company in both rows. The difference is what it can still operate on afterwards: **200 free vs. 30 free**. That is the real trade a greedy shareholder votes for — protect my payout now, leave the company with nothing to run on.

Two consequences fall out of this shape:

- **A dividends cut larger than the dividend that exists simply spills onto the reserve side.** You cannot cut 210 out of a 200 dividend; the remainder was already taken from the reserve regardless.
- **Already-dripped SC is never clawed back.** A cut only touches what has not yet been paid — it is forward-looking, never retroactive.

The shortfall vote is also the only kind that takes its own exit in `CloseCompanyVote`: it must not move the reserve mix, the market category or the payout rate as a side effect of asking a completely different question.

**Insolvency.** Whatever neither source can close accumulates on `gov.UnrecoverableShortfallSc`. That flag *is* the dissolution trigger P15.5a reads (D-15.8); until it ships, the flag accumulates visibly in the trace and in the FED scene's banking layer (⚠ pending / ✗ INSOLVENT rows).

#### 39.11.3 — The player's ballot, and why its control shipped early

`CompanyDetails`' Board Vote panel **swaps its whole body** for the single shortfall dial when that vote kind is open, rather than adding a fourth row to the usual form.

The control itself belongs to P15.7c's lending panel, but it was pulled forward into P15.4e for a concrete reason: **this vote pauses the game** for a player NST holder (D-ND8.18), and the pre-existing panel would have presented reserve / market / payout dials that the shortfall resolver *ignores*. The player would have been frozen, asked to vote, and given only controls that do nothing — worse than no control at all. Shipping a half-delivered vote for a whole phase was not worth the tidier phase boundary.

`TryRegisterPlayerVote` also gained `dividendsCutPercent` as an **optional** parameter (default 50). That is a deadlock guard, not a convenience: if any caller ever opens this vote kind without the new control wired, it still registers a legal 50/50 ballot and lifts the pause, instead of leaving the game frozen with no way out.

Nothing else of P15.7c was pulled forward — it still owes the rest of the bank lending panel (FED debt, `CollateralBtc` + live value, the quarter installment due, and the P15.6d FBI-investigation warning).

### 39.12 — Dissolution, the Closed-Companies list and seized-wallet custody (P15.5)

Dissolution applies to **every** company, banks included (D-15.17). The casino alone is exempt: it is the player's house, keeps its unlimited FED credit line, and never disappears.

**The trigger that exists today** is debt default: a bank carrying `gov.UnrecoverableShortfallSc > 0` — meaning P15.4e's vote could close the gap with *neither* a full dividends cut *nor* the company's own reserves — is insolvent and dies. P15.6 adds `fbi_seizure` as the second reason (and, for non-banks, the only one — they carry no debt, D-15.19).

**Dissolution is collected and applied OUTSIDE the governance loop.** `TryDissolveInsolventBanks` gathers the doomed first and kills them after, because dissolving mutates the very dictionary `TickCompanyGovernance` iterates. `TickCompanyGovernance`'s early-out also had to widen from "no live companies" to "no live companies *and* no closed ones" — otherwise the closed-company sweeps would stop running the moment the last company died.

#### 39.12.1 — Liquidation is deletion, not a rule to remember

On closure the company is removed from **both** `_companyFoundings` and `_companyGovernance`, leaving only a `CompanyClosure` record. That is a deliberate choice over "keep the record and mark it dead everywhere":

- Removing the founding removes `Holdings`, which **is** the destruction of every holder's NST/PST. Removing the governance state removes `ClaimableByHolder`, which is the loss of everything unclaimed. D-15.15's "holders lose their tokens and the company's future payments" becomes literal — not a condition that twenty other loops each have to remember to check.
- Every live loop (`AccumulateCompanyInflows`, the vote/dividend/conversion/repayment ticks, `IsAwaitingPlayerVote`, `HasPlayerClaimableDividends`) skips the dead company **for free**, because the entry simply is not there.

What survives is what should: dividends the holder had **already claimed** are in their own wallet and are untouched. The closure record keeps a copy of what the player held at the moment of death purely so the notice can say what was lost.

**The one place this leaks is display**, and it had to be handled explicitly: the BlockExplorer "Founded" list is driven by the **chain-derived auction ledger**, not by `_companyFoundings`, and a resolved auction stays `Resolved` forever (D-ND4b.12). So a dissolved company keeps appearing there with a null founding — which previously meant "founding hasn't fired yet." It now gets its own terminal row (grey, `✗`, reason + date + what the player lost, **no action button**), and `CompanyDetails` distinguishes the two cases rather than showing "not founded yet?" over a company the player watched die.

**Re-founding is not a risk**, worth stating because it looks like one: `TrySettleResolvedAuctions` fires `FoundCompany` only when a status *flips* `InAuction → Resolved` on that block, diffed against the previous block's recomputed ledger. A long-closed company never flips again, so removing its founding cannot resurrect it.

#### 39.12.2 — Custody: seize the wallet, don't move the coins

The FED is an SC-only entity — `CentralBankService` has no node, no keys and no address. But BTC has to live *somewhere*: every satoshi must sit at a real address. So "the FED seizes the wallet" is implemented as **the coins do not move at all**:

- The dead company's wallet stays on-chain exactly as it was, unspendable by anything (no code path owns a dissolved company), and keeps receiving whatever automatic inflows were already scheduled to it — the network does not know it died.
- That state *is* D-15.18's "held with the FED, 100% as BTC, custodial, no allocation." It needs no FED address, no synthetic transfer, and no new identity file.
- Its remaining **SC** reserve is different — SC is a plain number the FED can genuinely be repaid with, so it is applied against the debt on the way out and burned, like any repayment.

**Inheritance (P15.5c, D-15.18 "O18-A").** Each block, every custodied wallet looks for a **solvent** founded bank of its **matching market category** — solvency being the meaningful qualifier: a bank carrying its own shortfall cannot be handed more to manage. When one is found the wallet is assigned to it and, from then on, `SweepClosedCompanyInflows` forwards the accumulated balance and every later arrival to that bank (memo `SEIZED`, dust-floored like every other automated send). If the holder later dies itself, the assignment is **released** and the wallet returns to FED custody, eligible again.

This is why the pre-2012-09 seizures the design anticipated are handled without a special case: with no bank founded, there is simply no heir, and the coins sit exactly where they are until one exists.

**What the heir receives is ordinary business inflow, not collateral.** It is a windfall the bank now owns outright; its own band/level governance decides what to do with it (D-15.12 — never force-converted to SC, never a bespoke per-deposit vote).

#### 39.12.3 — The recovery tracker

`CompanyClosure` carries `DebtAtClosureSc` (what the FED wrote off) against `RecoveredBtc` (everything actually delivered to an absorber since). The FED scene's Closed-Companies block values the recovery at the **live** price — never frozen at a historical day, the `AuctioningCompanyDetails`/`BTCWallet` "always live" precedent — and prints the verdict: *RECOVERED (+profit)* / *underwater* / *no loss to recover*.

Recovery is stored in **BTC** and converted only for display, which is the point of the whole thread: the FED eats an SC loss and is repaid in an appreciating asset, so whether it comes out ahead is a genuine question about *when* the company died, not an accounting identity. Non-bank seizures carry no debt, so they show no tracker line (D-15.19).

**Why it lives in the FED scene rather than WorldEconomy.** The phase map nominally puts the recovery tracker and the Closed-Companies list in WorldEconomy (P15.7b). They went into the FED scene instead, for two reasons: the **FED is the absorber** — these are its written-off losses and its custodied wallets, so the readout belongs beside its client accounts — and the banking-layer block was already there from P15.2. P15.7b is free to mirror or move it; nothing else reads the block.

### 39.13 — The FBI investigation / seizure thread (P15.6)

**The gate (D-15.14).** The FBI does not exist in-game before **14 Jun 2011** — the date Gavin Andresen presented Bitcoin to the CIA via In-Q-Tel. He did *not* meet the FBI; the CIA connection is flavour only and never mechanically involved. The date is used because it is the historically honest moment "law enforcement noticed Bitcoin," and it is routed through `TimelineConfig.Shift` like every other anchor. Note it precedes the first bank founding (2012-09) by over a year, which is exactly why P15.5's custody model had to work with no heir available (§39.12.2).

**The hybrid (D-15.21).** F1's **investigation meter** decides *who is a target* — deterministic and player-legible, so keeping a company's SC lean is a real lever rather than a prayer. A **capped roll** on top decides *which block the raid actually lands*, so there is suspense without pure randomness punishing good play. Neither half alone was acceptable: a pure meter is gamey and a pure roll can nuke a company the player was managing well.

#### 39.13.1 — Charter-relative tolerance (amended R3, 2026-07-28 — supersedes the throughput basis)

Absolute SC ceilings go stale across a 2009–2025 span, so a company's tolerance is **relative** to something the company itself defines: `tolerance = categoryMultiplier × charterSc`, with placeholder multipliers **Official ∞ · Light-Grey 8× · Dark-Grey 3× · Black 1×**. Darker ⇒ lower: a licensed exchange sitting on a float is normal; a black-market stall sitting on the same fortune is a flag. **Official is exempt entirely** — never flagged on SC alone.

`charterSc` is what the company's **own shareholders voted to hold**: `ReserveScPercent × (CompanyOwnBtc × price + ScReserve)`. The valuation basis is deliberately byte-identical to `TryConvertCompanyReserves`' own target, so the figure the FBI judges can never drift from the figure the conversion aims at (§39.16 rule 6). No market price ⇒ the company is exempt for that block rather than judged against a BTC treasury nobody can value (structurally unreachable post-founding — auctions start at Market Birth).

**What this replaced, and why.** P15.6a measured the tolerance off recent SC *throughput*: `T = max(ScInflowLastQuarterSc, ScInflowCurrentQuarterSc)`, with a documented "`T = 0` is a feature — unexplained wealth" reading. That reasoning was wrong in a way that only a long playtest exposes, because **it judged a FLOW against a STOCK the design intends to be HELD**. `TryConvertCompanyReserves` stops converting the instant a company reaches its voted reserve target — that is the whole point of a target — so a perfectly healthy company reports zero inflow two quarters later, the tolerance collapses to `0.00`, `FbiOverageRatio` pins it at the overage cap, and it flags within ~13–25 blocks depending on darkness. **Every non-Official company that ever reached its target was therefore guaranteed a federal file.** A November-2011 playtest hit exactly that: three companies under investigation, all reading *"0.00 SC tolerated"*. The wealth was not unexplained; it was explained by a conversion the two-quarter window had simply forgotten. (The 2011 price crash sharpened it — a falling BTC price shrinks `totalValueSc`, which shrinks the SC target, which removes the deficit, which stops conversions entirely.)

The charter basis fixes the category error rather than widening the window: a company sitting exactly at its target has overage 0 and cools off; one hoarding SC well beyond its charter still heats up; and the player's levers get *sharper*, not weaker — vote the reserve % down, or hold a lighter category, neither of which is a stale accident of when the last conversion happened.

**A `0%` charter is now a REAL zero**, unlike the accidental one it replaced: it means "our charter says hold no SC" while the company holds some, which is exactly what the meter is for — and it is escapable, since dividends drain SC and the reserve % is itself votable.

`ScInflowCurrentQuarterSc` / `ScInflowLastQuarterSc` are **kept and still maintained** (accurate, cheap business-activity metrics and a P15.8 calibration input) — they are simply no longer what the meter reads. Deliberately not deleted: they do not diverge from reality (§39.16 rule 1 is unaffected), and removing a persisted field would force a `WorldFormatVersion` bump and wipe a live playtest for no gain.

#### 39.13.2 — The meter, the priority, and the roll

The meter is sized against **quarters, not seconds**, because a block is ~16h40m of game time (≈1.5 blocks/in-game-day, ≈135 per quarter). At pressure 1 a company flags in ~200 blocks (~1.5 quarters); a badly-over black company flags in well under one. Gain is `0.5 × overage × darkness` per block (darkness = category index + 1, so light-grey 2 … black 4), and the overage ratio is capped at 4 so an absurdly over-tolerance company does not flag instantly. Falling back under tolerance **decays** the score at 1.0/block — that decay is the player's lever, and it is what makes "keep this company lean, or vote it lighter" a real strategy rather than advice.

**Priority (D-15.19) is expressed as a single ordering, not a separate pass:** flagged targets sort **banks last**, everything else by how far over it is. The FBI works the small anomalies first and builds evidence before striking a big fish, so a bank can only be reached in the late game once the board is otherwise clean.

**At most one raid per block**, on the single highest-priority flagged target. That falls out of the same ordering and doubles as a throttle — the thread can never clear the board in a burst.

**The score itself needs a ceiling, not just its gain rate (R3).** `InvestigationOverageCap` bounds how *fast* the meter climbs; nothing bounded how *high*. Gain reaches `0.5 × 4 × 4 = 8`/block for a black company at the overage cap, against a `1.0`/block decay — an **8:1 asymmetry**, so a company held over tolerance for 200 blocks accrued ~1,600 and then needed ~1,600 blocks (over two in-game years) to cool to zero, sitting red on the FED board throughout. The decay is documented as *the player's lever*; unbounded accumulation quietly took the lever away.

`InvestigationScoreCap = 2 × InvestigationFlagThreshold`, and the value is derived rather than picked: the roll is `min(2%, 0.5% × darkness × score/threshold)`, which **saturates at its 2% cap by `score = 2 × threshold` for the lightest non-exempt category** (darkness 2) and earlier for every darker one. Past that point extra score changes nothing about the risk — it is pure dead weight buying an unpayable cooldown. Capping there bounds the worst case cool-off at **200 blocks (~4.5 in-game months)** and leaves time-to-flag completely unchanged. The clamp is applied on the decay branch too, so files inflated under the retired throughput basis (§39.13.1) drop to the ceiling on their first cooling block instead of serving out a sentence for a defect — no one-off amnesty pass needed.

**A raid needs a LIVE case, not just a thick file (R3).** Raid eligibility is now `score ≥ threshold` **AND** `overage > 0`. Because the score decays at only 1.0/block, a company that gets back under its tolerance otherwise stayed seizable for ~100 blocks for a condition that no longer held — the decay said "cooling off" while the roll still said "any block now". Requiring a current overage makes the decay mean what it says, turns the player's lever into an **immediate** effect instead of a 100-block wait, and keeps the file itself intact so a relapse re-arms at once rather than from zero. `GetFbiInvestigationWarning` gained the matching fourth state — *"file open but INACTIVE — back under tolerance, no raid while it stays there"* — because the line must state exactly what the tick will do (§39.16 rule 6).

The roll itself is `min(2%, 0.5% × darkness × score/threshold)` per block. The cap is the "capped" half of the hybrid: even a maximally-suspicious black company gets a 2% ceiling, so a raid is a matter of when-ish, never an instant execution.

#### 39.13.3 — Self-funding, and why the grant is a loan

The FBI carries its own SC budget: an initial federal grant at activation, then whatever it seizes. The grant is booked as a **FED loan on client `"fbi"`** rather than conjured into existence — otherwise it would mint SC outside `circulation = grants + debt` and break the invariant the whole monetary layer rests on. It is simply never repaid, exactly like the casino's (D-15.17).

**Seized SC, by contrast, is a plain transfer** and touches neither side of the invariant: the SC already existed in the company's reserve. This is why `DissolveCompany` branches on the closure reason — an FBI seizure moves the reserve to the FBI, while a debt default **burns** it against the FED loan.

The seized **BTC is not moved at all**: it flows straight into P15.5's existing custody chain (§39.12.2), which is what let P15.6 add a whole second dissolution cause without touching the wallet machinery.

#### 39.13.4 — Player agency (P15.6d)

`GetFbiInvestigationWarning` returns a line for `CompanyDetails` stating the state (*under investigation* / *FLAGGED — a raid can land on any block* / *cooling off*), the progress toward the threshold, the SC reserve against the tolerated figure for that category, and what to do about it. It is shown to **any** viewer, not just holders — the risk is a fact about the company, not about the holding — and it returns `null` when there is genuinely nothing to say (FBI inactive, category exempt, comfortably under tolerance), so the panel never carries a permanent decorative warning.

The FED scene gained a matching DEV board listing every open file **in the same order the roll picks its target**, sharing `FbiToleranceScFor`/`FbiOverageRatio` with the roll itself — §39.16 rule 6, so a displayed number cannot drift from the mechanism.

**The board reports three states, not two (R3).** Its original red/orange split keyed on `score ≥ threshold` alone, which after the raid-eligibility change no longer matched what the tick would do: a row reading *"score 340/100, SC comfortably under tolerance"* rendered red and read as a live threat when it was in fact a file already closing. The colour now tests **both** halves of eligibility — red ⚑ raid-eligible right now · orange · over tolerance with the file still growing · grey · under tolerance and cooling — and the cooling row states **how many blocks remain before it disappears** (`NetworkRoot.FbiBlocksToClear`, sharing `InvestigationDecayPerBlock` with the tick). That estimate exists because *"will this red row ever go away?"* was a question the board could not answer, which is precisely the ambiguity rule 6 exists to prevent: a status colour is a claim about what the mechanism will do next, and it must be computed from the same predicate the mechanism uses.

**Every number above is a P15.8 calibration placeholder**: the four tolerance multipliers, `T`'s window, the meter's gain/decay/cap, the roll's base and cap, and the initial grant.

### 39.14 — Surfacing & telemetry (P15.7)

P15.7 is the phase that mostly **already happened**. Four of its deliverables shipped early, each alongside the mechanism it observes, under §39.16's rule 2 ("a phase you cannot observe is a phase you cannot sign off"): the FED scene's Banking-layer block + financier preview (P15.7a, at P15.2), `bank_credit_trace.csv` (P15.7d, at P15.3), the shortfall ballot control (part of P15.7c, at P15.4e), and the Closed-Companies list + recovery tracker and the Federal-investigations board (P15.7b/c, at P15.5/P15.6).

What P15.7 itself added is the remainder.

**The layer-1 sub-ledger (P15.7a).** Each founded bank in the FED scene now lists the companies it financed — BTC bought, SC paid, provision count. The distinction on that page is worth keeping straight: the **client accounts** at the top are layer 0 (bank ↔ FED), and this strip is layer 1 (bank ↔ company). That is the D-15.5 two-layer model made literally visible on one screen.

**The banking-layer aggregate (P15.7b), and where it deliberately does NOT go.** WorldEconomy is the *macro* monetary scene, so it took the system-wide question — total collateral value vs total FED debt, plus a per-bank strip with an under-collateralized / shortfall / insolvent flag — and a **one-line pointer** to the Central Bank scene for closures. It did **not** take a copy of the Closed-Companies list, the recovery tracker or the FBI board, even though the phase map nominally assigned them here: those belong to the FED's own page (it is the creditor, the absorber and the custodian), and duplicating them would create two places to keep in step. One source per signal — §39.16 rule 6 applied to whole panels rather than single numbers.

Leverage is the honest headline for that block, and it is valued at **today's** price: banks borrow SC and sit on BTC, so "is the banking layer solvent?" is a live price question whose answer legitimately changes every day.

**The bank lending panel (P15.7c).** A bank is not an ordinary company — most of its balance sheet is borrowed, and what kills it is a *date* — so a shareholder needs a picture the other panels never show: Central Bank debt (with drawn/repaid), collateral held and its live value, the **collateral-vs-debt health line** (this is the carry the whole reform exists to create: profitable while BTC rises, dangerous when it falls), the next installment and when it falls due, and the pending-shortfall / insolvent states.

Every figure comes from `NetworkRoot.GetBankLendingSummary`, which computes them from the **same** constants and helpers the repayment itself uses (`BankQuarterlyRepaymentFraction`, the FED account, `BankCollateralBtc`). The installment shown is therefore the installment that will actually be charged — §39.16 rule 6, applied where it matters most: a number the player will make a decision on.

### 39.15 — Bot ballots must be legal ballots: projecting a stance into a company's band (P15.9)

Found by the developer during the P15.8 run, at Papa's Pizzeria's first quarterly vote — and it is the most instructive bug of the plan, because everything about it was *working as written*.

**The symptom.** The Board Vote panel offers the player a reserve dial bounded to the company's currency band. Papa's Pizzeria is **CB1**, so that dial runs `[75, 100]`. Beside it, the cast bot ballots read **0%** and **50%**. The player is held to the charter; the bots are not.

**The cause.** `BuildBotBallot` filled the reserve dial from the bot's *own* band preference — `BandDefaultScPercent(pref.CurrencyBandPreference)`, i.e. its global stance (CB1 100% … CB5 0%) — with no reference at all to the company being voted at. The player's ballot is bounded twice (the SpinBox, and `TryRegisterPlayerVote`'s clamp); the bot path had no bound anywhere.

**Why it mattered far more than the display.** `CloseCompanyVote` clamps only the **final weighted average** to the band. The four casino bots are drawn as a permutation of the five bands (§39.10's draw), so a CB1 company reliably carries two or three sub-75 ballots; the average landed below the floor; the clamp pinned the result to **exactly 75, every quarter, forever**. The vote produced a constant — and the player's ballot, the one input the game *pauses the entire simulation* to collect (D-ND8.18), could not move it, because it was being averaged against values the rules forbid. A governance system that visibly asks for your vote and structurally discards it is worse than one that never asked.

**The fix is a projection, not a clamp — and that distinction is the whole design (D-15.24).** A bot's band preference is a position on a global "SC-ness" axis, not a literal target; the band is the company's *identity* (fixed at founding, never voted on) and the ballot is a tuning dial *inside* it. So the stance is mapped into the company's range rather than cut off at its edge. Clamping would have collapsed the CB5, CB4 and CB3 bots onto exactly 75 in a CB1 company — three identical ballots, and a result still pinned near the floor. It would have fixed the *displayed* value and left the *behaviour* broken. Projection keeps all five stances distinct in every band, which is precisely what gives the player's vote leverage.

`NetworkRoot.ProjectStanceIntoBand` is **default-anchored**: the bot's own band default maps to the company's band default, interpolating linearly on each side. That makes the map the identity when the two bands agree — a CB2 bot at a CB2 company votes CB2's own 75 — which plain `[0,100] → [min,max]` interpolation does *not* give for CB2 and CB4, whose defaults do not sit at the centre of their own ranges. Results round to a whole percent (matching the player's `Step = 1` SpinBox), `.5` away from zero.

| Bot stance | CB1 `[75,100]` | CB2 `[50,100]` | CB3 `[25,75]` | CB4 `[0,50]` | CB5 `[0,25]` |
|---|---|---|---|---|---|
| CB5 — 0% | 75 | 50 | 25 | 0 | *0* |
| CB4 — 25% | 81 | 58 | 38 | *25* | 6 |
| CB3 — 50% | 88 | 67 | *50* | 33 | 13 |
| CB2 — 75% | 94 | *75* | 63 | 42 | 19 |
| CB1 — 100% | *100* | 100 | 75 | 50 | 25 |

**Expected consequence, and it is the point:** reserve outcomes now vary within the band instead of sitting on a bound. All four banks are CB1 (§39.7), so their SC/BTC mix is where this shows first and hardest, against P15.3's conversion volumes.

**Two habits this reinforces.** First — *the clamp hid the bug for an entire plan*, so it now announces itself: `CloseCompanyVote` `GD.PrintErr`s when the raw average actually falls outside the band. Post-P15.9 it should never fire; if it does, some new ballot source is bypassing the projection. A guard that silently absorbs illegal input is indistinguishable from a guard that is never needed — make it say which it is. Second — the DEV stance printout used to read `targets 50% SC`, which after this fix is a number no bot will ever cast; it now prints the global stance *and* what it votes in each of the five bands, computed through `ProjectStanceIntoBand` itself (§39.16 rule 6: the printout exists to be read against observed ballots, so it must not describe a number that does not appear). The `CompanyDetails` ballot list — the readout that surfaced this — now names the band range in its header, so an out-of-band value is obvious on sight instead of requiring the reader to remember the company's charter.

**The sibling case, deferred to P15.10.** A bank's market category is locked (§39.7 / D-15.12): the shift is voted, then refused. Nothing illegal is stored there, but its shareholders are still offered a control that cannot act. Same principle, separate phase.

#### 39.15.1 — The first live verification, and what the tripwire bought (P15.9f)

One quarter after the fix shipped, the reserve at Papa's Pizzeria still read 75% and it looked like the projection had not taken. It had — and the tripwire had already written the explanation to the log: *"cast an OUT-OF-BAND reserve average 37.55%, clamped to 75%"*.

**Ballots are cast when a vote OPENS, not when it closes**, and an open vote's ballots are persisted in the snapshot. The vote that closed had opened *before* the rebuild, so closing it averaged pre-fix raw ballots (`0.4235×0 + 0.4021×50 + 0.1745×100 = 37.55%`) and clamped them back to the floor. The vote opened *after* the rebuild carried `bot_1 → 75` and `bot_3 → 88`, exactly the table. **A rebuild mid-playtest leaves any already-open vote carrying pre-fix ballots** — expected, self-clearing, never a reason to wipe a world.

That is the argument for suggestion 4 in one incident: the guard named the company, the raw average and the band, which turned "the fix looks broken" into a five-minute audit with an arithmetic answer. It is worth generalizing — **when a clamp, a `Math.Max`, or a fallback exists to enforce an invariant that should already hold upstream, make it say so when it fires.** Otherwise a guard doing real work every day is indistinguishable from one that has never been needed, and the upstream defect lives behind it indefinitely. This one had.

**And the readout gap it exposed (P15.9f).** The original bug was spotted in the Last Vote Snapshot's ballot list — which shows a **closed** vote. There was no equivalent for the vote actually in front of the player: bots cast the instant a vote opens, so at the moment the game *pauses* and demands a ballot, every other ballot is already known, persisted, and was being hidden. The Board Vote panel now lists them (each holder's resolver weight, its cast ballot or *not voted yet*) plus a live **"if the vote closed now"** line under the dial. Both that preview and `CloseCompanyVote` resolve through one pure static, `NetworkRoot.ComputeReserveVoteOutcome` — §39.16 rule 6 in its sharpest form: **a preview is a promise about what the resolver will do**, so two implementations of the same weighted average would drift the first time either side changed, and the player would be deciding on the stale one.

### 39.16 — Conventions this plan established (apply these to later phases)

Six rules came out of building P15.1–P15.5. They are recorded together here because each was first reached as a one-off judgement call and each turned out to apply again a phase or two later — they should be treated as defaults for P15.6–P15.8 and beyond, not re-derived each time.

**1. Never let a persisted figure diverge from reality — fix it in the same phase that creates it.** `_bankState.CollateralBtc` would have claimed BTC the bank had already sold if the quarantine had waited for its nominal phase (§39.9.1). A *persisted number that lies* is a different category of problem from an unfinished feature: the feature is visibly absent, the lie is invisible and compounds. When a phase introduces a tracked quantity, the exclusions that keep it truthful ship with it.

**2. A phase you cannot observe is a phase you cannot sign off — pull the readout forward.** This happened four times: the P15.2 banking-layer block (a phase with *no* behaviour change), `bank_credit_trace.csv` at P15.3 (P15.7d's), the shortfall ballot control at P15.4e (P15.7c's), and the recovery tracker at P15.5 (P15.7b's). The pattern is deliberate — build the minimum surface that makes the new mechanism inspectable, note in the plan which later subphase it was borrowed from, and let that subphase polish rather than originate. **Prefer this to shipping a mechanism nobody can see for two more phases.**

**3. Prefer deletion to a flag when "this thing is over".** Removing a dissolved company from `_companyFoundings`/`_companyGovernance` destroys its holdings and claimables *by construction* and makes every live loop skip it for free (§39.12.1). A `IsDead` flag would have required every present and future loop to remember to check it. The cost is that any consumer reading the record from a *different* source (here, the chain-derived auction ledger) needs an explicit closed-aware branch — find those deliberately rather than discovering them.

**4. Version-bump and wipe by default; re-derive only what is genuinely derivable.** The standing rule (§39.5) is bump + clean reset, never a migration and never a pause to ask. The one exception is a value that is *derivable from static data* — a bank's locked category re-derived from the roster on restore (§39.7) is a simplification, not a migration. Do not contort a design to avoid a bump.

**5. New field on an existing per-entity record → sentinel default + backfill.** When the code that populates a record is guarded by an "already populated?" check, a stance-valued default for a new field produces a world that loads fine, plays fine, and has the new axis silently pinned flat (§39.10). A sentinel makes stale records announce themselves. This is about **silent failure modes**, not about avoiding a bump (see rule 4).

**6. A displayed signal must share its source with the action it advertises.** Inherited from ND.10d and reinforced throughout: `HasPlayerClaimableDividends` backs both the row button and the claim panel; `BuildBotPoolOpportunities` backs the roll, the eligibility test and the panel; `NetworkRoot.GetPlayerProjectedStake` reuses `FoundCompany`'s own ranking so the forecast cannot drift from the mint. When adding a readout, ask what performs the corresponding action and call *that*.

One further habit worth naming, from §39.9.1: **an on-chain display memo can become load-bearing.** `InputDataText` is normally cosmetic, but the `COLLATERAL` tag is what keeps a bank's collateral out of its business-inflow measure — and therefore what stops spurious game-pausing special votes. Before treating a memo as purely decorative, check whether anything reads it.

**7. Commit *timing* is not commit *durability* (added 2026-07-29, from INC-001 — see Chapter 40).** "A block is the only commit to disk" tells you *when* to write. It is silent on what a half-written file means, and for two whole steps nobody asked. Any code that persists player-owned state must also answer: is the write atomic, does a corrupt read fail loudly, and can a failed load ever be persisted back over the good copy? Full statement, plus the simulation-scale limitation the same incident exposed, in **Chapter 40**.

### 39.17 — The control that could not change anything, and the shortfall that was not one (P15.10)

*Placed after §39.16 rather than before it, against chronology: the conventions section is referenced 59 times across twelve files, several from code comments, and renumbering it to keep this in date order would be pure churn for no reader's benefit.*

**The phase was deliberately parked until it could be observed.** P15.10's decisions were locked (D-15.25) on 2026-07-27, and then nothing was built for two days, because the panel it changes cannot be opened until a bank has founded *and* the player holds NST in it — §39.16 rule 2 read forwards instead of backwards. It was picked up on 2026-07-29 at its trigger: First Satoshi Savings founded 2012-09-27 and opened its first quarterly vote on 2012-12-28, with the player holding 49% of the NST. Building it with the game paused inside that vote cost no playtest progress at all — the clock is frozen while a ballot is awaited, and the open vote rides `BlockchainStateSnapshot`, committed at the block that opened it, so the rebuild's restart resumed into the identical pause.

#### 39.17.1 — Two live dials and one inert one, all looking identical

A quarterly ballot carries three dials: the reserve target (% held as SC), the market direction, and the quarterly payout rate. At a bank the first and third move real money; the second is counted and then refused, because a bank's market category is **locked** (D-15.12). Before this phase all three rendered as live controls, so the holder whose ballot *pauses the entire simulation* was being asked to choose among three options that were all no-ops, and the Last Vote Snapshot then showed the category unchanged with no explanation of why.

**Nothing was illegal, and that is what makes this a weaker case than P15.9 — it had to not be over-fixed.** No number was stored out of range, no figure lied, and the lock itself is load-bearing: a bank's category is the distance axis `SelectFinanciers` measures on (§39.8), so a drifting bank would silently re-shape which companies bank where, and it would also inherit a darker category's higher dividend rate and higher seizure risk. Banks are compensated with seized-wallet custody (§39.12.2). **The values were all correct; only the presentation was dishonest.** So the phase changed what is shown and deliberately changed no mechanism.

The three decisions, in the order they matter: **disable and explain, never hide** (a silently missing control invites "why does this company have fewer dials?", and the reason is the interesting part worth teaching); **bots' ballots are not changed** (the current behaviour records "a rejected attempt rather than pretending nobody asked", and that a bank's other NST holders want it darker is a real economic fact — erasing it would trade an honest refusal for a silent one); **label the result** (the unexplained non-event in the snapshot was the actual defect).

#### 39.17.2 — Re-deriving a verdict means reproducing the resolver's GUARDS, not just its arithmetic

The spec called for re-deriving the refusal from the persisted ballots rather than storing a flag — the flag would default to `false` on every pre-existing record, reading as "no refusal happened" on exactly the historical votes where one did (§39.16 rule 5's silent-failure shape) for a value derivable all along (rule 4). That reasoning held. But the spec described only the weight test, and **the weight test alone is wrong**: `BuildBotBallot` takes no vote-kind parameter and fills `MarketShift` for *every* kind, while `CloseCompanyVote` evaluates a shift only inside `if (vote.Kind == CompanyVoteKindQuarterly)`. A founding or special record can therefore hold a ≥60% supermajority of `+1` ballots that nothing ever considered — and the ungated test would have printed "market shift refused" on a vote where no shift was ever on the table, replacing an old silence with a new falsehood.

This sharpens §39.16 rule 6 into something more specific and easy to get wrong: **when a readout re-derives a verdict from a persisted record, sharing the arithmetic is not enough — it must reproduce the guards that decided whether the arithmetic ran at all.** Implemented as `NetworkRoot.WasMarketShiftRefused(rec, nodeId)` (kind gate + bank gate + weights), with the supermajority verdict itself extracted into a pure `ResolveMarketShift(darker, lighter)` that `CloseCompanyVote` now calls too — so the note and the resolution are the same verdict by construction, the `ComputeReserveVoteOutcome` pattern from §39.15.1.

#### 39.17.3 — Disabling a control changes the arithmetic of every threshold that control could reach

An unforeseen consequence, worth recording because it was not obvious and was noticed only when planning the verification. Disabling the player's market dial means their ballot always carries shift 0. At First Satoshi Savings the player holds 49% of the NST and the two bot NST holders hold 27.55% and 23.45% — **51% combined, permanently short of the 60% supermajority.** So at the very company whose vote triggered the phase, P15.10b's refusal note can now *never* fire.

That outcome is correct (no supermajority means there is genuinely no refusal to announce), but it makes the second half of the phase unverifiable there, and the verification has to move to a bank where the bots control ≥60% of the NST — reachable for any viewer, since the snapshot renders without a holding. The general lesson: **a vote is a weight in someone else's threshold. Before disabling a control, check what else was counting on that weight** — here, removing the player's ability to *lose* a vote also removed everyone else's ability to *win* one.

#### 39.17.4 — The shortfall that was not one (P15.10d)

Found in the same screenshot taken for the "look at the thing first" step, on the same company, and it would have paused the game within one in-game day. First Satoshi Savings' first quarterly repayment succeeded — `1,721.32` SC repaid, exactly 10% of `17,213.20` — but selling collateral for *just enough* SC left a **sub-cent rounding residue** (8-decimal money divided by a BTC price), and `TryBankQuarterlyRepayment`'s `gap > 0m` test recorded it as a real shortfall. Since `TickCompanyGovernance` opens a shortfall vote on that same `> 0` test, the world was one block away from **pausing the entire simulation** for a board vote deciding who absorbs a quantity every readout in the game renders as `⚠ Shortfall of 0.00 SC`.

**A `> 0` threshold on a money quantity produced by division is a threshold on rounding noise.** The fix is a named `MinMaterialShortfallSc = 0.01m`, and the value is reasoned rather than picked: one cent is exactly where the figure becomes *visible* in the N2 readouts that report it, so the rule is **if the game cannot display it, it cannot be worth a vote over**. Dropping the residue is monetarily safe by construction because `Repay()` burns `raisedSc` and never the installment — the un-repaid dust simply remains outstanding FED debt and is chipped at next quarter, so no figure diverges from reality (§39.16 rule 1). A `shortfall_dust` trace row records the sub-threshold case, because a silently discarded quantity is precisely what hides a badly-set cutoff.

The third part is the one that is easy to forget: **tightening a writer does not fix a reader already holding what the old writer produced.** The residue was already persisted in the snapshot, and the vote trigger reads the persisted field — so without a self-heal the pointless pause would still have fired on the next tick. `TryLoadSnapshot` now clears a below-threshold `PendingShortfallSc` on restore, directly beneath the P15.2b category re-derivation that does the same kind of work (§39.7), idempotent because post-fix worlds never carry a value in that range, and free of a format bump. **When a fix narrows what gets written, ask what is already written.**

---

## Chapter 40 — Persistence Durability & Simulation Scale: The Limits of a Design Sized for Hand-Play

> Written 2026-07-29, immediately after **INC-001** (`Documentation/INCIDENT_LOG.md`) destroyed a P15.8
> playtest session five in-game days before the first bank company's auction closed. The incident entry is
> the forensic record — what broke, in what order, with what evidence. **This chapter is the design
> statement**: what the crash proved about assumptions this project has been carrying since long before
> Step 15, and which of them are now known to be false.
>
> The fix is **P15.11** in `AIHelperFiles/step15-bank-companies-sc-provisioning-plan.md`.

### 40.1 — The blind spot inside "a block is the only commit to disk"

The commit rule (Ch. 24 §24.8, CLAUDE.md Important Pattern 2) is one of this project's best decisions. It
gives a single, legible commit point; it makes an app restart mean something exact ("the world reverts to
the last mined block"); it kept a dozen services from inventing their own save cadences.

It also answers exactly one question — **when** to write — and we treated it as if it answered the whole
subject. Three questions it never addressed, all of which INC-001 answered for us:

1. **Is the write atomic?** It was not. `NetworkRoot.PersistStateToDisk` truncates `state.json` and streams
   9.25 MB into it. A process death mid-write leaves a file that is page-aligned, plausible-looking, and
   invalid — in the incident, 7 characters short of parseable.
2. **Does a corrupt read fail loudly?** It did not. `TryLoadSnapshot` had no `try`, so the `JsonException`
   escaped `EnsureInitialized` before it registered a single node, leaving `_isInitialized = false` and
   every consumer looking at an empty world **with nothing printed**. The money services, persisting to
   their own files, restored perfectly — so the failure presented as "some screens are blank", which is
   about the least diagnosable shape a total world-load failure could take.
3. **Can a failed load be written back over the good copy?** In this case no, and **only by accident**: the
   throw happened before any static state was touched, so the closing `PersistStateToDisk()` was never
   reached and the good file survived. A slightly more forgiving code path — one `catch` that logged and
   continued with an empty snapshot — would have overwritten a 1,666-block chain with an empty one at the
   next mined block. The world was recoverable because of where the exception landed, not because anything
   protected it.

**The general form:** a rule about persistence *timing* needs a companion rule about persistence
*durability*, and the second does not follow from the first. This is now §39.16 rule 7.

### 40.2 — The scale limitation this world exposed (2010-03-21 → 2012-09-22)

The crashed world is the most demanding one this project has ever produced, and it is worth stating exactly
what it contained, because the numbers are the argument:

| Measure | Value |
|---|---|
| In-game span | **2010-03-21 → 2012-09-22** — ~2.5 in-game years |
| Blocks mined | **1,666** |
| World snapshot (`state.json`) | **9.25 MB**, fully rewritten **on every block** |
| Monthly block chunks (`blocks-*.json`) | ~5 MB more per block — **deleted and rewritten wholesale**, and **read by nothing** |
| Bet-journal records loaded **at every boot** | **~5,330,000** |
| Bet records per block | **~3,200** (~half of them duplicates — see §40.4) |

Compare that against the premise the persistence layer was designed under. The canonical loop is
**1 bet = 1 nonce attempt = 100 in-game seconds**, and `TargetBlockSeconds = 58,500`, so the intended
arithmetic is **~585 player bets per block** — a person clicking, or a modest autobet, generating a few
hundred rows between commits. Under that premise, "load the entire bet history at boot and rebuild the
lifetime stats from it" is not merely acceptable, it is the *simplest correct thing*.

What actually runs now is a **simulator**: `SimulationService` ticking a background autobet plus four bot
runners across every scene, at `DevTimeScale` up to 9000X, for hours. Even after discounting the
duplication, the true history sits in the low millions of records for 1,666 blocks — several times the
design premise. (Part of that multiple is the known block-pace defect recorded at ND.10j: blocks were
running at ~2.2 in-game days against a 0.68-day target, so each block absorbed roughly three times the
intended number of bets. The R2 regulator work addresses the pace; it does not change the conclusion below.)

**The conclusion is not "the numbers got big".** It is that **several subsystems encode the hand-play
premise as an invariant rather than as a tuning parameter**, so they do not degrade as the scale grows —
they work perfectly until they stop working at all, and they stop in the least visible way available.

### 40.3 — Where the hand-play premise is still baked in

An honest inventory, as of this world. Each of these is correct under the original premise and wrong under
the simulator:

- **Unbounded lifetime history.** `UserStatsService._Ready()` calls `EnsureAllChunksLoaded()`
  unconditionally, because lifetime stats are derived by replaying every record ever written. There is no
  aggregate snapshot, so there is no way to know the totals without holding the whole history in RAM. **No
  retention policy exists anywhere** — nothing has ever deleted a bet record except a world reset.
- **Synchronous whole-file rewrites on the main thread.** `RollbackToUtc` re-serializes the entire history;
  `PersistStateToDisk` re-serializes the entire chain. Both were microseconds at design scale. At this
  world's scale the first is a multi-second-to-minutes main-thread stall on a 1.1 GB file, which is the
  proximate reason the app appeared to hang on a scene change.
- **Snapshot growth is linear in chain length, and it is rewritten per block.** By construction, the cost
  of committing block *N* is proportional to *N*. At 1,666 blocks that is 9.25 MB per block; there is
  nothing in the design that stops it at 10,000 blocks.
- **Write amplification nobody is reading.** `WriteMonthlyChunks` deletes every `blocks-*.json` and rewrites
  them on each commit. Grep confirms **no code path reads them**. They cost ~5 MB of I/O per block and, in
  the incident, were the first casualty of the interrupted write.
- **In-memory record objects, not a compact store.** ~5.3M `BetRecord` objects with per-record `Id` and
  `GameId` strings — an estimated 1.5–2.5 GB of managed heap, on a laptop with an Intel UHD 620.

None of these is a bug in the sense of a mistake. Each is a reasonable decision whose premise expired
quietly when the game acquired a background simulator and a 9000X dev scale, and **nothing was re-examined
at the moment the premise changed** — the same failure mode as §38.7's inverse-poll incident, where an event
subscription stayed correct while the event's frequency was multiplied by five.

### 40.4 — The specific defect: two writers, one file, different rules

Worth isolating, because it is the most transferable lesson in the incident.

`BetHistoryRepository` has **two** code paths that write the journal:

- `Flush()` — the incremental writer. Appends, counts lines, and **rotates to a new chunk every 10,000
  lines**. Correct.
- `RebuildJournalFromCurrentState()` — the "write everything out from current state" path, called by
  `RollbackToUtc` and `ClearAll` (so: every checkpoint restore, every DiceGame entry). It points the write
  target back at the **base** file, dumps the entire in-memory history with `FileMode.Create`, applies **no
  cap and no rotation**, and **does not delete the chunk files it has just duplicated**.

Because `GetJournalChunkPaths(includeLegacyBaseFile: true)` loads the base file *and* the chunks, the next
boot reads every record twice, and the next rollback writes that doubled set back into the base file. It
compounds per session — 1.126 GB in the base file against 293 MB of chunks covering the same period.

Two consequences, and the second is worse than the first:

1. The rotation policy — the thing specifically designed to keep file sizes manageable — was **defeated by
   the project's own code**, silently, for an unknown number of sessions.
2. `RebuildStatsFromLoadedHistory()` counts whatever was loaded, so **the player's lifetime bet count, total
   wagered and net profit have been inflated by duplication**, with nothing to compare against and no way to
   notice. This is §39.16 rule 1 (*a persisted figure that lies is invisible and compounds*) observed in the
   wild rather than reasoned about.

**The rule:** when two paths write the same file, the invariants belong to the **file**, not to whichever
function grew them first. A "rebuild from current state" path must satisfy every invariant the incremental
path does — rotation, capping, and cleanup of what it supersedes — or it must not exist.

### 40.5 — Durability rules going forward

These are now defaults, not one-offs:

1. **Write persisted state atomically.** Write `<file>.tmp`, flush, then rename over the target. A rename is
   atomic on every platform we target; a truncate-and-stream is not. Applies to anything a player would be
   upset to lose.
2. **A loader that can fail must fail loudly, and must never let a failed load reach a writer.** If a
   snapshot cannot be parsed, say so in the log with the path and the reason, and leave the file untouched.
   A `Try` prefix is a promise that the function handles its own failure — either honour it or drop the
   prefix.
3. **Never delete the old copy before the new one is complete.** `WriteMonthlyChunks`'s delete-all-then-
   rewrite is the anti-pattern; if a derived artefact must be regenerated wholesale, build it beside the
   old one and swap.
4. **Every unbounded, ever-growing store needs a stated retention policy at the moment it is created** —
   even if the policy is "unbounded, and here is the arithmetic showing that is fine at expected scale".
   The bet journal had no such statement, so nobody ever noticed the arithmetic had stopped being fine.
5. **Do not load an unbounded history to compute a bounded summary.** Lifetime totals want a persisted
   aggregate; full history loads belong behind the screens that actually browse history.
6. **When a system's operating scale changes, re-audit what was sized for the old scale.** The background
   simulator and `DevTimeScale` were introduced as *features*; neither was accompanied by a pass over the
   subsystems whose cost is per-bet or per-block. §38.7 is the same lesson in the event-frequency domain.

### 40.6 — What is deliberately not being fixed (and where it went instead)

Scope discipline, so P15.11 does not become an infrastructure project in the middle of a bank playtest. Everything below — plus the **progressive frame-rate decay** reported in the same run, which P15.11 does not address at all — is collected as **`PRIVATE_ROADMAP.md` §8 T4, "Simulation-Scale Refactor"**: an open technical objective, deliberately unscheduled. Its headline finding is worth repeating here, because it reframes the whole problem: **the bet journal is player-only** — the four bots already use aggregate `ClientBetStats` counters, so **the correct pattern was in the codebase all along and had simply never been applied to the player**. T4 also names the strongest structural suspect for the decay: ~62 per-node `BlockchainService` instances whose UTXO caches are all invalidated every block and rebuilt by full chain replay, so per-block cost grows linearly with chain length.

- **Snapshot growth remains linear in chain length.** At 1,666 blocks and 9.25 MB it is affordable, and
  making it incremental means a real chain-store redesign. It is recorded as a limit here and in
  `PRIVATE_ROADMAP.md`, not scheduled.
- **The stats aggregate is not being built now.** P15.11 stops the boot from loading everything and caps
  what accumulates; a proper persisted lifetime aggregate is the correct long-term answer and is deferred.
- **Nothing is being made crash-proof at the bet level.** The commit rule stands: a restart still reverts
  to the last mined block, and that is the intended contract, not a limitation.

