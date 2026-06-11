using System;

namespace GodotBlockchainPort.Blockchain;

// Pure C# RIPEMD-160 per the original spec (https://homes.esat.kuleuven.be/~bosselae/ripemd160.html).
// .NET 8 removed RIPEMD160 from System.Security.Cryptography on most platforms.
public static class Ripemd160
{
	private static readonly uint[] H0 = { 0x67452301u, 0xEFCDAB89u, 0x98BADCFEu, 0x10325476u, 0xC3D2E1F0u };

	// Additive constants — left track (one per 16-step round)
	private static readonly uint[] KL = { 0x00000000u, 0x5A827999u, 0x6ED9EBA1u, 0x8F1BBCDCu, 0xA953FD4Eu };

	// Additive constants — right track (rounds run in reverse function order)
	private static readonly uint[] KR = { 0x50A28BE6u, 0x5C4DD124u, 0x6D703EF3u, 0x7A6D76E9u, 0x00000000u };

	// Message word indices — left track
	private static readonly int[] RL = {
		 0,  1,  2,  3,  4,  5,  6,  7,  8,  9, 10, 11, 12, 13, 14, 15,
		 7,  4, 13,  1, 10,  6, 15,  3, 12,  0,  9,  5,  2, 14, 11,  8,
		 3, 10, 14,  4,  9, 15,  8,  1,  2,  7,  0,  6, 13, 11,  5, 12,
		 1,  9, 11, 10,  0,  8, 12,  4, 13,  3,  7, 15, 14,  5,  6,  2,
		 4,  0,  5,  9,  7, 12,  2, 10, 14,  1,  3,  8, 11,  6, 15, 13
	};

	// Message word indices — right track
	private static readonly int[] RR = {
		 5, 14,  7,  0,  9,  2, 11,  4, 13,  6, 15,  8,  1, 10,  3, 12,
		 6, 11,  3,  7,  0, 13,  5, 10, 14, 15,  8, 12,  4,  9,  1,  2,
		15,  5,  1,  3,  7, 14,  6,  9, 11,  8, 12,  2, 10,  0,  4, 13,
		 8,  6,  4,  1,  3, 11, 15,  0,  5, 12,  2, 13,  9,  7, 10, 14,
		12, 15, 10,  4,  1,  5,  8,  7,  6,  2, 13, 14,  0,  3,  9, 11
	};

	// Rotation amounts — left track
	private static readonly int[] SL = {
		11, 14, 15, 12,  5,  8,  7,  9, 11, 13, 14, 15,  6,  7,  9,  8,
		 7,  6,  8, 13, 11,  9,  7, 15,  7, 12, 15,  9, 11,  7, 13, 12,
		11, 13,  6,  7, 14,  9, 13, 15, 14,  8, 13,  6,  5, 12,  7,  5,
		11, 12, 14, 15, 14, 15,  9,  8,  9, 14,  5,  6,  8,  6,  5, 12,
		 9, 15,  5, 11,  6,  8, 13, 12,  5, 12, 13, 14, 11,  8,  5,  6
	};

	// Rotation amounts — right track
	private static readonly int[] SR = {
		 8,  9,  9, 11, 13, 15, 15,  5,  7,  7,  8, 11, 14, 14, 12,  6,
		 9, 13, 15,  7, 12,  8,  9, 11,  7,  7, 12,  7,  6, 15, 13, 11,
		 9,  7, 15, 11,  8,  6,  6, 14, 12, 13,  5, 14, 13, 13,  7,  5,
		15,  5,  8, 11, 14, 14,  6, 14,  6,  9, 12,  9, 12,  5, 15,  8,
		 8,  5, 12,  9, 12,  5, 14,  6,  8, 13,  6,  5, 15, 13, 11, 11
	};

	public static byte[] Hash(byte[] data) => Hash((ReadOnlySpan<byte>)data);

	public static byte[] Hash(ReadOnlySpan<byte> data)
	{
		int len = data.Length;
		int paddedLen = ((len + 8) / 64 + 1) * 64;
		byte[] msg = new byte[paddedLen];
		data.CopyTo(msg);
		msg[len] = 0x80;
		ulong bitLen = (ulong)len * 8;
		for (int i = 0; i < 8; i++)
			msg[paddedLen - 8 + i] = (byte)(bitLen >> (8 * i));

		uint[] h = (uint[])H0.Clone();

		for (int block = 0; block < paddedLen; block += 64)
			ProcessBlock(msg, block, h);

		byte[] result = new byte[20];
		for (int i = 0; i < 5; i++)
		{
			result[i * 4]     = (byte) h[i];
			result[i * 4 + 1] = (byte)(h[i] >>  8);
			result[i * 4 + 2] = (byte)(h[i] >> 16);
			result[i * 4 + 3] = (byte)(h[i] >> 24);
		}
		return result;
	}

	private static void ProcessBlock(byte[] msg, int offset, uint[] h)
	{
		uint[] x = new uint[16];
		for (int i = 0; i < 16; i++)
			x[i] = (uint)(
				msg[offset + i * 4    ]        |
				msg[offset + i * 4 + 1] <<  8  |
				msg[offset + i * 4 + 2] << 16  |
				msg[offset + i * 4 + 3] << 24);

		uint al = h[0], bl = h[1], cl = h[2], dl = h[3], el = h[4];
		uint ar = h[0], br = h[1], cr = h[2], dr = h[3], er = h[4];

		for (int j = 0; j < 80; j++)
		{
			int r = j / 16;
			unchecked
			{
				uint tl = RotL(al + F(r,     bl, cl, dl) + x[RL[j]] + KL[r], SL[j]) + el;
				al = el; el = dl; dl = RotL(cl, 10); cl = bl; bl = tl;

				uint tr = RotL(ar + F(4 - r, br, cr, dr) + x[RR[j]] + KR[r], SR[j]) + er;
				ar = er; er = dr; dr = RotL(cr, 10); cr = br; br = tr;
			}
		}

		unchecked
		{
			uint t = h[1] + cl + dr;
			h[1]   = h[2] + dl + er;
			h[2]   = h[3] + el + ar;
			h[3]   = h[4] + al + br;
			h[4]   = h[0] + bl + cr;
			h[0]   = t;
		}
	}

	private static uint F(int round, uint x, uint y, uint z) => round switch
	{
		0 => x ^ y ^ z,
		1 => (x & y) | (~x & z),
		2 => (x | ~y) ^ z,
		3 => (x & z) | (y & ~z),
		_ => x ^ (y | ~z)
	};

	private static uint RotL(uint x, int n) => (x << n) | (x >> (32 - n));
}
