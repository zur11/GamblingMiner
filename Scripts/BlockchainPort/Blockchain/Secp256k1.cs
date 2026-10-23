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

		return Compress(ScalarMulG(privateKey)); // mini-plan 28 F-2
	}

#if DEBUG
	/// <summary>
	/// Mini-plan 28 E3 — the pre-F-2 path (double-and-add, ModPow inverse), kept only so the boot can time and check
	/// the two side by side in one process. Deleted at close-out with BootProbe.CompareOldPathsAtBoot (Convention 3).
	/// </summary>
	public static byte[] GetCompressedPublicKeyByDoubleAndAdd(byte[] privateKey)
	{
		BigInteger k = ToBigInteger(privateKey);
		if (k <= BigInteger.Zero || k >= N)
			throw new ArgumentOutOfRangeException(nameof(privateKey),
				"Private key value is not in the valid secp256k1 range [1, N−1].");

		return Compress(ScalarMul(Gx, Gy, k));
	}
#endif

	// The 33-byte compressed encoding: 0x02/0x03 by the parity of y, then x big-endian.
	private static byte[] Compress((BigInteger X, BigInteger Y) point)
	{
		byte[] result = new byte[33];
		result[0] = point.Y.IsEven ? (byte)0x02 : (byte)0x03;
		ToBytes32(point.X).CopyTo(result, 1);
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
	// ⚠ Mini-plan 28 (2026-10-23): "~8 s" was true for ~1,900 derivations and did not stay true. Every coinbase
	// Satoshi mines and every change output a rotating wallet makes takes a FRESH address, so the rescan derives
	// one per address the world has ever used: 15,750 on R3's chain (~2016), measured at 44.7 s of a 57.9 s boot.
	// A cost that scales with history has to be written per unit of history (here ~2.4 ms per derivation before
	// F-2, ~0.25 after), never as a figure. The fixed-base table below is the fix for the cost; cast miners'
	// change returning to their base address (NetworkRoot, F-3) is the fix for the count.
	//
	// The curve math is unchanged, so this is bit-for-bit output-identical: verified against the previous
	// implementation over 311 vectors (300 seed-derived keys, the " #r{i}" / "sign:" seed shapes the
	// wallet code actually produces, the empty seed, and the edge scalars 1, 2, 3, 255, N−2, N−1), plus
	// the k=1 known-answer test that the result is the standard compressed encoding of G. Because the
	// derived addresses do not change, this needs NO WorldFormatVersion bump and no world reset.
	//
	// Formulas are the standard ones from the Explicit-Formulas Database for short Weierstrass a = 0:
	// "dbl-2009-l" (doubling) and "add-2007-bl" (addition).

	// --- Fixed-base multiplication of G (mini-plan 28 F-2) ---
	//
	// Every scalar multiply in the game is k·G: GetCompressedPublicKey is the only caller, and it always multiplies
	// the generator. The boot's wallet rescan does one per address the world has ever used (~17,000 on R3's world,
	// mini-plan 28 §5A), so G's multiples are computed ONCE into a table and every k·G becomes a sum of entries.
	//
	// The scalar is read as its 32 bytes, k = Σ d_j·256^j with d_j in 0..255, and the table holds d·256^j·G for
	// every j and every d = 1..255 (8,160 affine points, built once per process in ~0.2 s). k·G is then one mixed
	// addition per nonzero byte, at most 32 and NO doublings, against ~256 doublings and ~128 additions for
	// double-and-add; the final conversion inverts by extended Euclid rather than ModPow. Measured side by side in
	// one process (mini-plan 28 harness, Debug, 6,000 calls): 0.25 ms against 2.85, 8.6–8.9% of the old cost. The
	// 4-bit table (960 points) measured 23.4%, and with ModPow left in place the inversion alone was over half of
	// what remained.
	//
	// Same curve, same field, the same standard formulas, so the result is bit-for-bit the one ScalarMul gives:
	// 0 mismatches over 7,422 vectors (Step 16's seed shapes, " #r{i}" and "sign:" seeds, the empty seed, the edge
	// scalars 1, 2, 3, 15, 16, 17, 255, 256, 2^128, 2^255, N−2, N−1, and 5,000 random keys), plus the k=1 known
	// answer. No WorldFormatVersion bump, no reset. Edge cases, from the arithmetic: the windows are summed from
	// the lowest up, so before window j the running sum is a scalar below 256^j, and the term added is at least
	// 256^j. The two are never equal (no doubling case) and their sum never exceeds k < N (never the point at
	// infinity). JacobianAddAffine still handles both, so the claim is not load-bearing.
	private const int WindowCount = 32;
	private const int DigitsPerWindow = 255;

	// Built on first use, once per process. Lazy<T>'s default mode is thread-safe.
	private static readonly Lazy<(BigInteger X, BigInteger Y)[]> GTable = new(BuildGTable);

	// k·G from the 32-byte big-endian scalar, already range-checked by the caller.
	private static (BigInteger X, BigInteger Y) ScalarMulG(byte[] bigEndianScalar)
	{
		(BigInteger X, BigInteger Y)[] table = GTable.Value;
		var result = JacobianInfinity;
		for (int j = 0; j < WindowCount; j++)
		{
			int digit = bigEndianScalar[31 - j];
			if (digit != 0)
				result = JacobianAddAffine(result, table[j * DigitsPerWindow + digit - 1]);
		}
		return JacobianToAffineByEuclid(result);
	}

	// JacobianToAffine with the inverse taken by the extended Euclidean algorithm instead of Fermat's ModPow:
	// measured 0.023 ms against 0.223 per inverse (mini-plan 28 harness), which left alone would be over half of a
	// fixed-base multiply. Same value: the inverse mod a prime is unique.
	private static (BigInteger X, BigInteger Y) JacobianToAffineByEuclid((BigInteger X, BigInteger Y, BigInteger Z) p)
	{
		if (p.Z.IsZero) return Infinity;

		BigInteger zInv  = ModInverseByEuclid(p.Z);
		BigInteger zInv2 = Mod(zInv * zInv, P);
		return (Mod(p.X * zInv2, P), Mod(Mod(p.Y * zInv2, P) * zInv, P));
	}

	// a⁻¹ mod P by the extended Euclidean algorithm. a is nonzero mod P (a Z coordinate of a finite point).
	private static BigInteger ModInverseByEuclid(BigInteger a)
	{
		BigInteger t = BigInteger.Zero, newT = BigInteger.One;
		BigInteger r = P, newR = Mod(a, P);
		while (!newR.IsZero)
		{
			BigInteger quotient = BigInteger.DivRem(r, newR, out BigInteger remainder);
			(t, newT) = (newT, t - quotient * newT);
			(r, newR) = (newR, remainder);
		}
		return t.Sign < 0 ? t + P : t;
	}

	// table[j·255 + d − 1] = d·256^j·G. Built in Jacobian coordinates, then converted to affine with ONE inversion
	// (Montgomery's batch trick), so the lookups can use the cheaper mixed addition.
	private static (BigInteger X, BigInteger Y)[] BuildGTable()
	{
		var jacobian = new (BigInteger X, BigInteger Y, BigInteger Z)[WindowCount * DigitsPerWindow];
		var windowBase = (X: Gx, Y: Gy, Z: BigInteger.One); // 256^j·G
		for (int j = 0; j < WindowCount; j++)
		{
			var multiple = windowBase;
			for (int d = 1; d <= DigitsPerWindow; d++)
			{
				jacobian[j * DigitsPerWindow + d - 1] = multiple;
				multiple = JacobianAdd(multiple, windowBase); // (d+1)·256^j·G
			}
			windowBase = multiple; // 256·256^j·G = 256^(j+1)·G
		}

		// Batch inversion: prefix[i] = Z_0·…·Z_i, one ModInverse of the last, then walk back.
		int n = jacobian.Length;
		var prefix = new BigInteger[n];
		BigInteger running = BigInteger.One;
		for (int i = 0; i < n; i++)
		{
			running = Mod(running * jacobian[i].Z, P);
			prefix[i] = running;
		}

		var affine = new (BigInteger X, BigInteger Y)[n];
		BigInteger inverse = ModInverse(running, P);
		for (int i = n - 1; i >= 0; i--)
		{
			BigInteger zInv = i > 0 ? Mod(inverse * prefix[i - 1], P) : inverse;
			inverse = Mod(inverse * jacobian[i].Z, P);
			BigInteger zInv2 = Mod(zInv * zInv, P);
			affine[i] = (Mod(jacobian[i].X * zInv2, P), Mod(Mod(jacobian[i].Y * zInv2, P) * zInv, P));
		}
		return affine;
	}

	// P1 + P2 where P2 is affine (Z2 = 1): "madd-2007-bl". No modular inverse, and fewer products than JacobianAdd.
	private static (BigInteger X, BigInteger Y, BigInteger Z) JacobianAddAffine(
		(BigInteger X, BigInteger Y, BigInteger Z) p1,
		(BigInteger X, BigInteger Y) p2)
	{
		if (p1.Z.IsZero) return (p2.X, p2.Y, BigInteger.One);

		BigInteger z1z1 = Mod(p1.Z * p1.Z, P);
		BigInteger u2   = Mod(p2.X * z1z1, P);               // U2 = X2·Z1²
		BigInteger s2   = Mod(Mod(p2.Y * p1.Z, P) * z1z1, P); // S2 = Y2·Z1³

		if (u2 == p1.X)
		{
			// Same affine x: either the same point (double) or a vertical line (infinity).
			return s2 == p1.Y ? JacobianDouble(p1) : JacobianInfinity;
		}

		BigInteger h  = Mod(u2 - p1.X, P);                   // H = U2 − X1
		BigInteger hh = Mod(h * h, P);                       // HH = H²
		BigInteger i  = Mod(4 * hh, P);                      // I = 4·HH
		BigInteger j  = Mod(h * i, P);                       // J = H·I
		BigInteger r  = Mod(2 * (s2 - p1.Y), P);             // r = 2(S2 − Y1)
		BigInteger v  = Mod(p1.X * i, P);                    // V = X1·I
		BigInteger x3 = Mod(r * r - j - 2 * v, P);
		BigInteger y3 = Mod(r * (v - x3) - 2 * p1.Y * j, P);
		BigInteger zh = Mod(p1.Z + h, P);
		BigInteger z3 = Mod(zh * zh - z1z1 - hh, P);         // Z3 = (Z1+H)² − Z1² − HH
		return (x3, y3, z3);
	}

	// Computes k * P using double-and-add over Jacobian coordinates. Since mini-plan 28 F-2 only the DEBUG reference
	// above calls it; it goes at close-out with that reference.
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
