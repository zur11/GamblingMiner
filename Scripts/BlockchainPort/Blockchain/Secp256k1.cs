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

	// --- Internal EC arithmetic (affine coordinates) ---

	// Computes k * P using the double-and-add algorithm.
	// For a 256-bit k this performs ~256 doublings and ~128 additions on average.
	private static (BigInteger X, BigInteger Y) ScalarMul(BigInteger px, BigInteger py, BigInteger k)
	{
		var result  = Infinity;
		var current = (X: px, Y: py);

		while (k > BigInteger.Zero)
		{
			if (!k.IsEven)
				result = PointAdd(result, current);
			current = PointDouble(current);
			k >>= 1;
		}
		return result;
	}

	// P1 + P2 on the secp256k1 curve (affine coordinates).
	private static (BigInteger X, BigInteger Y) PointAdd(
		(BigInteger X, BigInteger Y) p1,
		(BigInteger X, BigInteger Y) p2)
	{
		if (p1 == Infinity) return p2;
		if (p2 == Infinity) return p1;

		if (p1.X == p2.X)
		{
			// Same x-coordinate: either the same point (double) or vertical line (infinity)
			if (p1.Y == p2.Y) return PointDouble(p1);
			return Infinity; // P1 and P2 are negations: P + (-P) = O
		}

		BigInteger lam = Mod((p2.Y - p1.Y) * ModInverse(p2.X - p1.X), P);
		BigInteger x3  = Mod(lam * lam - p1.X - p2.X, P);
		BigInteger y3  = Mod(lam * (p1.X - x3) - p1.Y, P);
		return (x3, y3);
	}

	// 2*P on the secp256k1 curve (tangent-line formula, simplified for a=0).
	private static (BigInteger X, BigInteger Y) PointDouble((BigInteger X, BigInteger Y) p)
	{
		if (p == Infinity || p.Y == BigInteger.Zero) return Infinity;

		// λ = (3x²) / (2y) mod p   [a = 0 for secp256k1]
		BigInteger num = Mod(3 * p.X * p.X, P);
		BigInteger den = Mod(2 * p.Y, P);
		BigInteger lam = Mod(num * ModInverse(den, P), P);
		BigInteger x3  = Mod(lam * lam - 2 * p.X, P);
		BigInteger y3  = Mod(lam * (p.X - x3) - p.Y, P);
		return (x3, y3);
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
