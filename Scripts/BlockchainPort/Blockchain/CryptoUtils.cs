using System;
using System.Security.Cryptography;
using System.Text;

namespace GodotBlockchainPort.Blockchain;

public static class CryptoUtils
{
    public static string Sha256Hex(string input)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static (string publicKeyBase64, string privateKeyBase64) GenerateWallet()
    {
        using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (
            Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey())
        );
    }

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
