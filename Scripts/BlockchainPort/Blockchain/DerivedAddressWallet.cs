using System;
using System.Collections.Generic;
#nullable enable

namespace GodotBlockchainPort.Blockchain;

// Phase 8.1 (Step 8 — "address non-reuse") — HD-lite derived-address wallet.
//
// A node that owns a seed phrase derives an unbounded, deterministic address book by index, so every
// coinbase / receive can land on a FRESH address (the real Satoshi "one address per block reward"
// practice — ~220 addresses at fractal scale). This is the address-non-reuse mechanic; it is NOT the
// Patoshi mining fingerprint (see step8-utxo-realism-plan.md §0 / Decision D0).
//
// Pure C#: no Godot, no chain reference, NO persisted state. The live frontier (NextReceiveIndex) and
// the owned/used set are reconstructed from the chain via Rescan (Decision D3), because "a block is the
// only commit to disk" — an app restart reverts the world to the last block, so the wallet must be
// re-derivable from the chain, never read from a side file.
//
// Derivation (§2.1):
//   addr(0)    == the existing base address (empty suffix → fully back-compatible)
//   addr(i>=1) == CryptoUtils.DeriveGmAddress(seed + " #r" + i)
// The " #r" namespace keeps receive addresses distinct from ordinary derivation. A passphrase wallet
// that happened to use the literal passphrase "#rN" would collide with addr(N) — but that is the same
// owner's own seed, so the (astronomically unlikely) overlap is harmless.
public sealed class DerivedAddressWallet
{
	// OQ-8.4 — BIP44 convention. Our strictly-sequential assignment means gaps should never occur, so
	// this is a cheap safety margin: probed against a prebuilt used-address set, "scan 20 past the last
	// hit" is ~20 SHA256 derivations, not 20 chain scans.
	public const int DefaultGapLimit = 20;

	private readonly string _seedPhrase;
	private readonly Dictionary<int, string> _addressByIndex = new();
	private readonly Dictionary<string, int> _indexByAddress = new();

	public DerivedAddressWallet(string seedPhrase)
	{
		_seedPhrase = seedPhrase ?? throw new ArgumentNullException(nameof(seedPhrase));
		OwnedAddresses = new HashSet<string>();
		// index 0 == the base / identity address (genesis, p2p receives such as E4). Mined coinbases and
		// fresh receives start at index 1, so a node's reward never lands on its own identity address.
		NextReceiveIndex = 1;
	}

	// The next fresh receive index = first unused address after the highest used one (always >= 1).
	public int NextReceiveIndex { get; private set; }

	// Every derived address that currently appears on-chain (the funded/used set), set by Rescan.
	public HashSet<string> OwnedAddresses { get; private set; }

	public string BaseAddress => DeriveAddress(0);

	private static string SeedForIndex(string seedPhrase, int i) => i == 0 ? seedPhrase : $"{seedPhrase} #r{i}";

	// Derives (and caches) the gm1q... address at a given index.
	public string DeriveAddress(int index)
	{
		if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
		if (_addressByIndex.TryGetValue(index, out string? cached)) return cached;

		string address = CryptoUtils.DeriveGmAddress(SeedForIndex(_seedPhrase, index));
		_addressByIndex[index] = address;
		_indexByAddress[address] = index;
		return address;
	}

	// Full signing context for a derived index (address + P-256 keypair + secp256k1 pubkey) — the keys a
	// NodeAgent needs to sign a spend whose Sender is this address. Mirrors RegisterPassphraseWallet's
	// derivation, generalized to the index-derived address book.
	public (string address, string signingPublicKeyBase64, string signingPrivateKeyBase64, string secp256k1PublicKeyBase64)
		DeriveSigningContext(int index)
	{
		string seed = SeedForIndex(_seedPhrase, index);
		string address = DeriveAddress(index);
		(string signPub, string signPriv) = CryptoUtils.DeriveSigningKeypair(seed);
		string secp = CryptoUtils.DeriveSecp256k1CompressedPublicKeyBase64(seed);
		return (address, signPub, signPriv, secp);
	}

	// The fresh address the next receive (coinbase / deposit) should be paid to. Does NOT advance the
	// frontier — that moves only when Rescan sees the address confirmed on-chain (a block is the commit).
	public string NextReceiveAddress() => DeriveAddress(NextReceiveIndex);

	// Reconstruct the wallet from the chain (Decision D3 / §2.3). `appearsOnChain` should be backed by a
	// single-pass used-address set (OQ-8.4) so probing is O(1). Derives addresses in order, tolerating up
	// to `gapLimit` consecutive unused indices before concluding the wallet ends.
	public void Rescan(Func<string, bool> appearsOnChain, int gapLimit = DefaultGapLimit)
	{
		ArgumentNullException.ThrowIfNull(appearsOnChain);

		var owned = new HashSet<string>();
		int lastUsed = -1;
		int consecutiveUnused = 0;
		int i = 0;
		while (consecutiveUnused < gapLimit)
		{
			string address = DeriveAddress(i);
			if (appearsOnChain(address))
			{
				owned.Add(address);
				lastUsed = i;
				consecutiveUnused = 0;
			}
			else
			{
				consecutiveUnused++;
			}
			i++;
		}

		OwnedAddresses = owned;
		// Reserve index 0 as the base/identity address: the receive frontier is always >= 1.
		NextReceiveIndex = Math.Max(lastUsed + 1, 1);
	}

	// Advances the receive frontier past the address just paid — the in-session hot path after a block the
	// node mined to NextReceiveAddress() commits. Cheap (no chain scan); the full Rescan re-derives the
	// frontier from the chain on launch / after a revert-to-last-block (Decision D3).
	public void MarkReceiveConsumed()
	{
		OwnedAddresses.Add(NextReceiveAddress());
		NextReceiveIndex++;
	}

	// The signing context for a held address, so any owned derived address can sign a spend (used by the
	// UTXO-lite spend path in Phase 8.3). Returns false if the address is not in this wallet's book.
	public bool TryFindSpendingContext(
		string fundedAddress,
		out (string address, string signingPublicKeyBase64, string signingPrivateKeyBase64, string secp256k1PublicKeyBase64) context)
	{
		if (_indexByAddress.TryGetValue(fundedAddress, out int known))
		{
			context = DeriveSigningContext(known);
			return true;
		}

		// Not cached yet — derive forward up to the known frontier (+ a gap margin) looking for a match.
		int limit = NextReceiveIndex + DefaultGapLimit;
		for (int i = 0; i <= limit; i++)
		{
			if (DeriveAddress(i) == fundedAddress)
			{
				context = DeriveSigningContext(i);
				return true;
			}
		}

		context = default;
		return false;
	}
}
