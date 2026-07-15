using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanBeam.Core.Security;

/// <summary>
/// Cihaza özel kalıcı self-signed sertifikayı üretir ve saklar.
/// PFX, Windows'ta DPAPI (kullanıcıya bağlı) ile şifrelenerek diske yazılır.
/// </summary>
public static class CertificateManager
{
    private const string FileName = "identity.pfx.dpapi";

    public static X509Certificate2 GetOrCreate(string dataDirectory, string deviceId)
    {
        string path = Path.Combine(dataDirectory, FileName);

        if (File.Exists(path))
        {
            try
            {
                byte[] pfx = Unprotect(File.ReadAllBytes(path));
                // EphemeralKeySet KULLANMA: SChannel (SslStream) ephemeral anahtarla el sıkışamaz.
                return new X509Certificate2(pfx, (string?)null,
                    X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
            }
            catch (Exception)
            {
                // Bozulmuş/başka kullanıcıya ait dosya: yeniden üret. Eski eşleşmeler geçersiz olur
                // ama uygulama çalışmaya devam eder.
            }
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN=LanBeam {deviceId}", key, HashAlgorithmName.SHA256);

        // SChannel'ın istemci ve sunucu kimlik doğrulamasında sertifikayı kabul etmesi için EKU şart.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1"), // serverAuth
                new Oid("1.3.6.1.5.5.7.3.2"), // clientAuth
            },
            critical: false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyAgreement, critical: false));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 ephemeral = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(20));

        byte[] export = ephemeral.Export(X509ContentType.Pfx);
        File.WriteAllBytes(path, Protect(export));

        return new X509Certificate2(export, (string?)null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
    }

    /// <summary>Sertifikanın SHA-256 parmak izi, büyük harf hex.</summary>
    public static string GetFingerprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));

    public static string GetFingerprint(System.Security.Cryptography.X509Certificates.X509Certificate certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(data, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : data;

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.CurrentUser)
            : data;
}
