using System.Security.Cryptography;
using System.Text;

namespace LanBeam.Core.Security;

/// <summary>
/// PIN tabanlı karşılıklı doğrulama: iki taraf da TLS el sıkışmasında gördüğü sertifika
/// parmak izleri + nonce'lar üzerinden PIN anahtarlı HMAC üretir. Aradaki bir MITM kendi
/// sertifikasını araya soktuğunda parmak izleri değişeceği için PIN'i bilmeden doğru
/// kanıt üretemez.
/// </summary>
public static class PairingMath
{
    public const int MaxAttempts = 3;

    public static string GeneratePin() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    public static byte[] GenerateNonce() => RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Kanıt = HMAC-SHA256(key: PIN, data: fpProver || fpVerifier || nonceProver || nonceVerifier).
    /// Taraflar rolleri simetrik ters sırayla kullanır; böylece iki yönün kanıtları birbirinden
    /// farklıdır (yansıtma saldırısı engellenir).
    /// </summary>
    public static byte[] ComputeProof(string pin, string proverFingerprint, string verifierFingerprint,
        byte[] proverNonce, byte[] verifierNonce)
    {
        byte[] fpA = Encoding.UTF8.GetBytes(proverFingerprint.ToUpperInvariant());
        byte[] fpB = Encoding.UTF8.GetBytes(verifierFingerprint.ToUpperInvariant());

        byte[] data = new byte[fpA.Length + fpB.Length + proverNonce.Length + verifierNonce.Length];
        int pos = 0;
        fpA.CopyTo(data, pos); pos += fpA.Length;
        fpB.CopyTo(data, pos); pos += fpB.Length;
        proverNonce.CopyTo(data, pos); pos += proverNonce.Length;
        verifierNonce.CopyTo(data, pos);

        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(pin), data);
    }

    public static bool VerifyProof(byte[] expected, byte[] actual) =>
        expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
}
