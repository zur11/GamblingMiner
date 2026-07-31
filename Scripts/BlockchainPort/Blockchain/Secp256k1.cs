using System;
using System.Numerics;

namespace GodotBlockchainPort.Blockchain;

// Minimal secp256k1 elliptic curve implementation.
// Used exclusively for address derivation: private key → compressed public key → gm1q... address.
// Transaction signing continues to use P-256 via CryptoUtils.Sign() — purely game-internal.
//
// secp256k1 is the same curve Bitcoin uses. Replacing Bech32.GameHrp ("gm") with "bc" in
// CryptoUtils.DeriveGmAddress() would produce valid Bitcoin mainnet P2WPKH addresses from
// the same private keys — the math is identical.
public static class Secp256k1
{
	// --- Curve parameters (SECG SEC 2, section 2.4.1) ---

	// Field prime p: 2^256 − 2^32 − 977
	private static readonly BigInteger P = BigInteger.Parse(
		"00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFC2F",
		System.Globalization.NumberStyles.HexNumber);

	// Curve order n: number of distinct points on the curve
	private static readonly BigInteger N = BigInteger.Parse(
		"00FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEBAAEDCE6AF48A03BBFD25E8CD0364141",
		System.Globalization.NumberStyles.HexNumber);

	// Generator point G (the standard "starting point" every secp256k1 implementation uses)
	private static readonly BigInteger Gx = BigInteger.Parse(
		"0079BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798",
		System.Globalization.NumberStyles.HexNumber);
	private static readonly BigInteger Gy = BigInteger.Parse(
		"00483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8",
		System.Globalization.NumberStyles.HexNumber);

	// Point at infinity O — identity element for elliptic curve addition.
	// (0,0) is safe as a sentinel: 0² ≠ 0³ + 7 mod p, so it is never a valid curve point.
	private static readonly (BigInteger X, BigInteger Y) Infinity = (BigInteger.Zero, BigInteger.Zero);

	// Jacobian form of the same identity. (X : Y : Z) represents the affine point (X/Z², Y/Z³), and
	// Z == 0 is the point at infinity — X and Y are then arbitrary, so 1/1 is used by convention.
	private static readonly (BigInteger X, BigInteger Y, BigInteger Z) JacobianInfinity =
		(BigInteger.One, BigInteger.One, BigInteger.Zero);

	// --- Public API ---

	/// <summary>
	/// Returns the 33-byte compressed public key for the given 32-byte private key.
	/// The key must be in the valid range [1, N-1].
	/// Use IsValidPrivateKey() to check before calling, or use CryptoUtils.DeriveGmAddress()
	/// which handles the astronomically-rare out-of-range case automatically.
	/// </summary>
	/// <param name="privateKey">32-byte big-endian private key scalar.</param>
	/// <returns>33-byte compressed public key: 0x02/0x03 prefix + 32-byte X coordinate.</returns>
	public static byte[] GetCompressedPublicKey(byte[] privateKey)
	{
		if (privateKey == null || privateKey.Length != 32)
			throw new ArgumentException("Private key must be exactly 32 bytes.", nameof(privateKey));

		BigInteger k = ToBigInteger(privateKey);
		if (k <= BigInteger.Zero || k >= N)
			throw new ArgumentOutOfRangeException(nameof(privateKey),
				"Private key value is not in the valid secp256k1 range [1, N−1].");

		var (x, y) = ScalarMul(Gx, Gy, k);

		byte[] result = new byte[33];
		result[0] = y.IsEven ? (byte)0x02 : (byte)0x03;
		ToBytes32(x).CopyTo(result, 1);
		return result;
	}

	/// <summary>
	/// Returns true if the 32 bytes represent a valid secp256k1 private key (value in [1, N−1]).
	/// </summary>
	public static bool IsValidPrivateKey(byte[] privateKey)
	{
		if (privateKey == null || privateKey.Length != 32) return false;
		BigInteger k = ToBigInteger(privateKey);
		return k > BigInteger.Zero && k < N;
	}

	// --- Internal EC arithmetic (Jacobian projective coordinates) ---
	//
	// PERFORMANCE (Step 16, 2026-07-30 — the 6-minute-launch fix). The original implementation worked in
	// AFFINE coordinates, where both the point-add and the point-double formulas need a division, i.e. a
	// modular inverse. Ours computes that inverse by Fermat (ModInverse → BigInteger.ModPow(a, p−2, p)),
	// which is itself a full 256-bit modular exponentiation — so ONE address derivation ran ~384 modexps
	// and measured **127 ms**. That was survivable while only the founders + player + casino carried a
	// DerivedAddressWallet (~6 wallets), but P16.2 gave a seed to every bot, cast miner and company: the
	// launch-time RescanDerivedReceiveWallets gap-scan (~21 derivations per wallet, ~79 wallets, plus
	// Satoshi's ~220 rotated coinbase addresses) went to ~1,900 derivations ≈ 4 MINUTES inside
	// EnsureInitialized, on top of which the app still had to reach the main menu.
	//
	// Jacobian coordinates represent the affine point (x, y) as (X : Y : Z) with x = X/Z², y = Y/Z³. The
	// Z-denominator absorbs exactly the division the affine formulas performed eagerly, so add and double
	// become inversion-free and the ONLY modular inverse left is the single Z⁻¹ that converts the final
	// accumulator back to affine — 384 modexps become 1. Measured 127 ms → 4.2 ms per address (34x), so
	// the boot rescan above drops from ~270 s to ~8 s.
	//
	// The curve math is unchanged, so this is bit-for-bit output-identical: verified against the previous
	// implementation over 311 vectors (300 seed-derived keys, the " #r{i}" / "sign:" seed shapes the
	// wallet code actually produces, the empty seed, and the edge scalars 1, 2, 3, 255, N−2, N−1), plus
	// the k=1 known-answer test that the result is the standard compressed encoding of G. Because the
	// derived addresses do not change, this needs NO WorldFormatVersion bump and no world reset.
	//
	// Formulas are the standard ones from the Explicit-Formulas Database for short Weierstrass a = 0:
	// "dbl-2009-l" (doubling) and "add-2007-bl" (addition).

	// Computes k * P using double-and-add over Jacobian coordinates.
	// For a 256-bit k this performs ~256 doublings and ~128 additions, then one inversion at the end.
	private static (BigInteger X, BigInteger Y) ScalarMul(BigInteger px, BigInteger py, BigInteger k)
	{
		var result  = JacobianInfinity;
		var current = (X: Mod(px, P), Y: Mod(py, P), Z: BigInteger.One);

		while (k > BigInteger.Zero)
		{
			if (!k.IsEven)
				result = JacobianAdd(result, current);
			current = JacobianDouble(current);
			k >>= 1;
		}
		return JacobianToAffine(result);
	}

	// P1 + P2 in Jacobian coordinates ("add-2007-bl"). No modular inverse.
	private static (BigInteger X, BigInteger Y, BigInteger Z) JacobianAdd(
		(BigInteger X, BigInteger Y, BigInteger Z) p1,
		(BigInteger X, BigInteger Y, BigInteger Z) p2)
	{
		if (p1.Z.IsZero) return p2;
		if (p2.Z.IsZero) return p1;

		BigInteger z1z1 = Mod(p1.Z * p1.Z, P);
		BigInteger z2z2 = Mod(p2.Z * p2.Z, P);
		BigInteger u1   = Mod(p1.X * z2z2, P);              // U1 = X1·Z2²
		BigInteger u2   = Mod(p2.X * z1z1, P);              // U2 = X2·Z1²
		BigInteger s1   = Mod(Mod(p1.Y * z2z2, P) * p2.Z, P); // S1 = Y1·Z2³
		BigInteger s2   = Mod(Mod(p2.Y * z1z1, P) * p1.Z, P); // S2 = Y2·Z1³

		if (u1 == u2)
		{
			// Same affine x: either the same point (double) or a vertical line (infinity).
			return s1 == s2 ? JacobianDouble(p1) : JacobianInfinity;
		}

		BigInteger h  = Mod(u2 - u1, P);
		BigInteger i  = Mod(4 * h * h, P);                  // I = (2H)²
		BigInteger j  = Mod(h * i, P);                      // J = H·I
		BigInteger r  = Mod(2 * (s2 - s1), P);              // r = 2(S2 − S1)
		BigInteger v  = Mod(u1 * i, P);                     // V = U1·I
		BigInteger x3 = Mod(r * r - j - 2 * v, P);
		BigInteger y3 = Mod(r * (v - x3) - 2 * s1 * j, P);
		// Z3 = ((Z1+Z2)² − Z1² − Z2²)·H  — the doubled-product trick, one squaring instead of a multiply.
		BigInteger zSum = Mod(p1.Z + p2.Z, P);
		BigInteger z3   = Mod(Mod(Mod(zSum * zSum, P) - z1z1 - z2z2, P) * h, P);
		return (x3, y3, z3);
	}

	// 2*P in Jacobian coordinates ("dbl-2009-l", a = 0). No modular inverse.
	private static (BigInteger X, BigInteger Y, BigInteger Z) JacobianDouble(
		(BigInteger X, BigInteger Y, BigInteger Z) p)
	{
		if (p.Z.IsZero || p.Y.IsZero) return JacobianInfinity;

		BigInteger a  = Mod(p.X * p.X, P);                  // A = X²
		BigInteger b  = Mod(p.Y * p.Y, P);                  // B = Y²
		BigInteger c  = Mod(b * b, P);                      // C = B²
		BigInteger xb = Mod(p.X + b, P);
		BigInteger d  = Mod(2 * (Mod(xb * xb, P) - a - c), P); // D = 2((X+B)² − A − C) = 4·X·B
		BigInteger e  = Mod(3 * a, P);                      // E = 3A
		BigInteger f  = Mod(e * e, P);                      // F = E²
		BigInteger x3 = Mod(f - 2 * d, P);
		BigInteger y3 = Mod(e * (d - x3) - 8 * c, P);
		BigInteger z3 = Mod(2 * p.Y * p.Z, P);
		return (x3, y3, z3);
	}

	// (X : Y : Z) → (X/Z², Y/Z³). The single modular inversion of the whole scalar multiply.
	private static (BigInteger X, BigInteger Y) JacobianToAffine((BigInteger X, BigInteger Y, BigInteger Z) p)
	{
		if (p.Z.IsZero) return Infinity;

		BigInteger zInv  = ModInverse(p.Z, P);
		BigInteger zInv2 = Mod(zInv * zInv, P);
		return (Mod(p.X * zInv2, P), Mod(Mod(p.Y * zInv2, P) * zInv, P));
	}

	// a⁻¹ mod p via Fermat's little theorem: a^(p−2) mod p.
	// Valid because p is prime, so a^(p−1) ≡ 1 (Fermat), giving a * a^(p−2) ≡ 1.
	private static BigInteger ModInverse(BigInteger a, BigInteger m = default)
	{
		if (m == default) m = P;
		return BigInteger.ModPow(Mod(a, m), m - 2, m);
	}

	// Proper positive modulo: BigInteger % can return negative values in C# for negative operands.
	private static BigInteger Mod(BigInteger a, BigInteger m)
	{
		BigInteger r = a % m;
		return r < BigInteger.Zero ? r + m : r;
	}

	// Interprets a 32-byte big-endian array as a positive BigInteger.
	// BigInteger constructor is little-endian; we reverse and append 0x00 for the positive sign bit.
	private static BigInteger ToBigInteger(byte[] bigEndian)
	{
		byte[] le = new byte[bigEndian.Length + 1]; // +1 = 0x00 sign byte (ensures positive)
		for (int i = 0; i < bigEndian.Length; i++)
			le[i] = bigEndian[bigEndian.Length - 1 - i];
		return new BigInteger(le);
	}

	// Serializes a positive BigInteger as a 32-byte big-endian array (leading zeros if needed).
	// BigInteger.ToByteArray() is little-endian and may include a trailing 0x00 sign byte.
	private static byte[] ToBytes32(BigInteger value)
	{
		byte[] le  = value.ToByteArray();
		int    len = le.Length;
		if (len > 0 && le[len - 1] == 0x00) len--; // trim sign byte
		byte[] result = new byte[32];
		for (int i = 0; i < Math.Min(len, 32); i++)
			result[31 - i] = le[i]; // reverse LE → BE, pad MSBs with zero
		return result;
	}
}
