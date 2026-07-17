using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LanBeam.Core.Security;

/// <summary>
/// Cihaza özel kalıcı self-signed sertifikayı üretir ve saklar.
/// Windows'ta PFX, DPAPI (kullanıcıya bağlı) ile şifrelenir; macOS/Linux'ta dosya yalnızca
/// sahibin okuyabileceği izinle (chmod 600) yazılır.
/// </summary>
public static class CertificateManager
{
    private const string FileName = "identity.pfx.dpapi";

    /// <summary>
    /// Windows'ta SChannel (SslStream) ephemeral anahtarla el sıkışamadığından UserKeySet gerekir.
    /// macOS'ta Apple sağlayıcısı EphemeralKeySet ile PFX yüklemeyi DESTEKLEMEZ (çöker), o yüzden
    /// bayrağı kaldırıyoruz (varsayılan anahtar seti). Linux'ta EphemeralKeySet sorunsuz çalışır ve
    /// anahtarı sistem deposuna yazmaz (PFX'i biz zaten kalıcı tutuyoruz).
    /// </summary>
    private static X509KeyStorageFlags LoadFlags
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable;
            if (OperatingSystem.IsMacOS())
                return X509KeyStorageFlags.Exportable;
            return X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;
        }
    }

    public static X509Certificate2 GetOrCreate(string dataDirectory, string deviceId)
    {
        string path = Path.Combine(dataDirectory, FileName);

        if (File.Exists(path))
        {
            try
            {
                byte[] pfx = Unprotect(File.ReadAllBytes(path));
                return new X509Certificate2(pfx, (string?)null, LoadFlags);
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
        RestrictToOwner(path);

        return new X509Certificate2(export, (string?)null, LoadFlags);
    }

    /// <summary>macOS/Linux'ta dosyayı yalnızca sahibin okuyup yazabileceği izne (600) çeker.</summary>
    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch (Exception) { }
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
