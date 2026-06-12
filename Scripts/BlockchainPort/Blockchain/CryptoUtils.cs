using System;
using System.Security.Cryptography;
using System.Text;

namespace GodotBlockchainPort.Blockchain;

public static class CryptoUtils
{
	// --- Legacy utility: used for block/transaction hashing ---

	public static string Sha256Hex(string input)
	{
		byte[] bytes = Encoding.UTF8.GetBytes(input);
		byte[] hash  = SHA256.HashData(bytes);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	// --- Address derivation ---

	// Derives a gm1q... address from a secp256k1 compressed public key (33 bytes, base64 encoded).
	// Used to verify that a transaction's Sender address matches the public key attached to the transaction.
	public static string DeriveAddressFromPublicKey(string secp256k1CompressedPubKeyBase64)
	{
		byte[] compPubKey = Convert.FromBase64String(secp256k1CompressedPubKeyBase64);
		byte[] pubKeyHash = Ripemd160.Hash(SHA256.HashData(compPubKey));
		return Bech32.Encode(Bech32.GameHrp, 0x00, pubKeyHash);
	}

	// Derives a gm1q... address from a seed phrase (3 words for base wallet, 4 for passphrase wallet).
	// Pipeline: SHA256(phrase) → secp256k1 compressed pubkey → RIPEMD160(SHA256) → Bech32.
	// The while loop handles the ~1-in-2^128 chance of SHA256 output falling outside secp256k1's valid
	// range [1, n−1]. In practice the loop always exits on the first attempt.
	public static string DeriveGmAddress(string seedPhrase)
	{
		int attempt = 0;
		while (true)
		{
			string input      = attempt == 0 ? seedPhrase : seedPhrase + ":" + attempt;
			byte[] privateKey = SHA256.HashData(Encoding.UTF8.GetBytes(input));

			if (Secp256k1.IsValidPrivateKey(privateKey))
			{
				byte[] compPubKey = Secp256k1.GetCompressedPublicKey(privateKey);
				byte[] pubKeyHash = Ripemd160.Hash(SHA256.HashData(compPubKey));
				return Bech32.Encode(Bech32.GameHrp, 0x00, pubKeyHash);
			}
			attempt++;
		}
	}

	// --- Wallet creation ---

	// Generates a new random wallet for bots and mining nodes.
	// Returns:
	//   address                  — gm1q... (secp256k1-derived, Bitcoin-authentic)
	//   signingPublicKeyBase64   — P-256 SubjectPublicKeyInfo base64 (used by Verify())
	//   signingPrivateKeyBase64  — P-256 PKCS8 base64 (used by Sign())
	//   secp256k1PublicKeyBase64 — secp256k1 33-byte compressed pubkey base64 (used by DeriveAddressFromPublicKey())
	//
	// The same 32-byte keyMaterial drives both derivations:
	//   secp256k1 path → address (Bitcoin math)
	//   P-256 path     → signing keypair (game-internal, no external verification needed)
	//
	// The while loop exits immediately in all practical cases; the two catch paths guard against the
	// astronomically-rare event that the random bytes fall outside a curve's valid scalar range.
	public static (string address,
	                string signingPublicKeyBase64,
	                string signingPrivateKeyBase64,
	                string secp256k1PublicKeyBase64) GenerateWallet()
	{
		while (true)
		{
			byte[] keyMaterial = RandomNumberGenerator.GetBytes(32);
			if (!Secp256k1.IsValidPrivateKey(keyMaterial)) continue;

			ECDsa signingKey;
			try
			{
				var ecParams = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = keyMaterial };
				signingKey = ECDsa.Create(ecParams);
			}
			catch (CryptographicException) { continue; }

			using (signingKey)
			{
				byte[] compPubKey = Secp256k1.GetCompressedPublicKey(keyMaterial);
				string address    = DeriveAddressFromPublicKey(Convert.ToBase64String(compPubKey));

				return (
					address,
					Convert.ToBase64String(signingKey.ExportSubjectPublicKeyInfo()),
					Convert.ToBase64String(signingKey.ExportPkcs8PrivateKey()),
					Convert.ToBase64String(compPubKey)
				);
			}
		}
	}

	// Derives a deterministic P-256 signing keypair from a seed phrase.
	// Used when the player or casino needs to sign a transaction without having a stored private key.
	// "sign:" prefix ensures the derived bytes differ from DeriveGmAddress's secp256k1 key.
	// The while loop handles the negligible chance of SHA256 output falling outside P-256's valid range.
	public static (string signingPublicKeyBase64, string signingPrivateKeyBase64) DeriveSigningKeypair(string seedPhrase)
	{
		int attempt = 0;
		while (true)
		{
			string input = attempt == 0 ? ("sign:" + seedPhrase) : ("sign:" + seedPhrase + ":" + attempt);
			byte[] seed  = SHA256.HashData(Encoding.UTF8.GetBytes(input));
			try
			{
				var ecParams = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = seed };
				using ECDsa ecdsa = ECDsa.Create(ecParams);
				return (
					Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
					Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey())
				);
			}
			catch (CryptographicException) { attempt++; }
		}
	}

	// --- Transaction signing (P-256, game-internal) ---

	public static string Sign(string payload, string privateKeyBase64)
	{
		using ECDsa ecdsa = ECDsa.Create();
		ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
		byte[] signature = ecdsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256);
		return Convert.ToBase64String(signature);
	}

	public static bool Verify(string payload, string signatureBase64, string publicKeyBase64)
	{
		using ECDsa ecdsa = ECDsa.Create();
		ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
		return ecdsa.VerifyData(
			Encoding.UTF8.GetBytes(payload),
			Convert.FromBase64String(signatureBase64),
			HashAlgorithmName.SHA256
		);
	}
}
