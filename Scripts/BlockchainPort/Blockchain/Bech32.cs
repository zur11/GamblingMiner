using System;
using System.Collections.Generic;

namespace GodotBlockchainPort.Blockchain;

// Bech32 encoder/decoder per BIP173.
// Produces gm1q... Native SegWit (P2WPKH) addresses using HRP "gm".
// Real Bitcoin uses HRP "bc" (mainnet) or "tb" (testnet) — same math, different prefix.
public static class Bech32
{
	public const string GameHrp = "gm";

	private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

	private static readonly uint[] Generator = {
		0x3b6a57b2u, 0x26508e6du, 0x1ea119fau, 0x3d4233ddu, 0x2a1462b3u
	};

	// Encodes a witness program as a gm1q... address.
	// witnessVersion: 0 for P2WPKH (produces 'q' as the first data character).
	// witnessProgram: 20-byte RIPEMD160(SHA256(compressedPubKey)) for P2WPKH.
	public static string Encode(string hrp, byte witnessVersion, byte[] witnessProgram)
	{
		byte[] data5 = ConvertBits(witnessProgram, 8, 5, pad: true);
		byte[] payload = new byte[data5.Length + 1];
		payload[0] = witnessVersion;
		data5.CopyTo(payload, 1);

		byte[] checksum = CreateChecksum(hrp, payload);

		char[] result = new char[hrp.Length + 1 + payload.Length + 6];
		hrp.CopyTo(0, result, 0, hrp.Length);
		result[hrp.Length] = '1';
		for (int i = 0; i < payload.Length; i++)
			result[hrp.Length + 1 + i] = Charset[payload[i]];
		for (int i = 0; i < 6; i++)
			result[hrp.Length + 1 + payload.Length + i] = Charset[checksum[i]];

		return new string(result);
	}

	// Returns false if the address is malformed or checksum fails.
	public static bool TryDecode(string address, out string hrp, out byte version, out byte[] program)
	{
		hrp = null; version = 0; program = null;

		address = address.ToLowerInvariant();
		int sep = address.LastIndexOf('1');
		if (sep < 1 || sep + 7 > address.Length) return false;

		hrp = address[..sep];
		byte[] decoded = new byte[address.Length - sep - 1];
		for (int i = 0; i < decoded.Length; i++)
		{
			int ci = Charset.IndexOf(address[sep + 1 + i]);
			if (ci < 0) return false;
			decoded[i] = (byte)ci;
		}

		if (!VerifyChecksum(hrp, decoded)) return false;

		version = decoded[0];
		program = ConvertBits(decoded[1..^6], 5, 8, pad: false);
		return program != null;
	}

	// Returns true if the string looks like a valid gm1q... address (format check only, no chain state).
	public static bool IsValidGmAddress(string address)
	{
		return TryDecode(address, out string hrp, out _, out _) && hrp == GameHrp;
	}

	private static byte[] CreateChecksum(string hrp, byte[] data)
	{
		// values = expandedHrp + data + 6 zero bytes
		byte[] exp = ExpandHrp(hrp);
		byte[] values = new byte[exp.Length + data.Length + 6];
		exp.CopyTo(values, 0);
		data.CopyTo(values, exp.Length);

		uint mod = Polymod(values) ^ 1;
		byte[] checksum = new byte[6];
		for (int i = 0; i < 6; i++)
			checksum[i] = (byte)((mod >> (5 * (5 - i))) & 31);
		return checksum;
	}

	private static bool VerifyChecksum(string hrp, byte[] data)
	{
		byte[] exp = ExpandHrp(hrp);
		byte[] values = new byte[exp.Length + data.Length];
		exp.CopyTo(values, 0);
		data.CopyTo(values, exp.Length);
		return Polymod(values) == 1;
	}

	// Each HRP character contributes two 5-bit values: (char >> 5) and (char & 31),
	// separated by a zero byte.
	private static byte[] ExpandHrp(string hrp)
	{
		byte[] result = new byte[hrp.Length * 2 + 1];
		for (int i = 0; i < hrp.Length; i++)
		{
			result[i]               = (byte)(hrp[i] >> 5);
			result[hrp.Length + 1 + i] = (byte)(hrp[i] & 31);
		}
		return result;
	}

	private static uint Polymod(byte[] values)
	{
		uint chk = 1;
		foreach (byte v in values)
		{
			uint b = chk >> 25;
			chk = ((chk & 0x1FFFFFF) << 5) ^ v;
			for (int i = 0; i < 5; i++)
				if (((b >> i) & 1) != 0)
					chk ^= Generator[i];
		}
		return chk;
	}

	// Converts a byte array between bit-group sizes.
	// Returns null (on decode path) if the conversion would require non-zero padding.
	private static byte[] ConvertBits(byte[] data, int fromBits, int toBits, bool pad)
	{
		int acc = 0, bits = 0;
		int maxv = (1 << toBits) - 1;
		List<byte> result = new();

		foreach (byte b in data)
		{
			acc = (acc << fromBits) | b;
			bits += fromBits;
			while (bits >= toBits)
			{
				bits -= toBits;
				result.Add((byte)((acc >> bits) & maxv));
			}
		}

		if (pad && bits > 0)
			result.Add((byte)((acc << (toBits - bits)) & maxv));
		else if (!pad && (bits >= fromBits || ((acc << (toBits - bits)) & maxv) != 0))
			return null;

		return result.ToArray();
	}
}
