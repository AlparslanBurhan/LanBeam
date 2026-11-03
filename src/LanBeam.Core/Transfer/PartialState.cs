using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanBeam.Core.Protocol;

namespace LanBeam.Core.Transfer;

/// <summary>
/// Yarım kalan bir alımın durumu. Uygulama veri klasöründe, (gönderen parmak izi + içerik imzası)
/// başına bir dosya olarak saklanır: aynı gönderen aynı içeriği yeniden gönderirse tamamlanan
/// parçalar atlanır. Parçalar dosya kimliğiyle değil göreli yolla anahtarlanır — gönderen
/// yeniden taradığında dosya kimlikleri değişebilir.
/// </summary>
public sealed class PartialState
{
    public string Signature { get; set; } = "";
    public string SenderFingerprint { get; set; } = "";
    public string DestinationRoot { get; set; } = "";

    /// <summary>
    /// true: bağlantı koptu ya da gönderen duraklattı — aynı içerik geldiğinde sormadan bu klasöre
    /// devam edilir. false: kullanıcı iptal etti — yeniden sorulur, aynı klasör seçilirse devam edilir.
    /// </summary>
    public bool AutoResume { get; set; }

    /// <summary>Hedefte zaten var olduğu için yeniden adlandırılan kök öğeler ("Fotoğraflar" → "Fotoğraflar (2)").</summary>
    public Dictionary<string, string> RootRenames { get; set; } = new();

    /// <summary>
    /// Göreli yol → (parça indeksi → o parçanın xxHash3'ü). Resume'da diskteki baytlar bu hash'le
    /// yeniden doğrulanır; yerelde bozulmuş bir parça sessizce kabul edilmez.
    /// </summary>
    public Dictionary<string, Dictionary<int, ulong>> CompletedChunkHashesByPath { get; set; } = new();

    /// <summary>İçeriğin kimliği: sıralı (yol|boyut) listesinin SHA-256'sı.</summary>
    public static string ComputeSignature(IEnumerable<FileEntry> files)
    {
        var sb = new StringBuilder();
        foreach (FileEntry f in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
            sb.Append(f.RelativePath).Append('|').Append(f.Size).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    public static string PathFor(string directory, string senderFingerprint, string signature) =>
        Path.Combine(directory, Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(senderFingerprint.ToUpperInvariant() + "|" + signature)))[..32] + ".json");

    public static PartialState? TryLoad(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<PartialState>(File.ReadAllText(path)) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this));
        File.Move(tmp, path, overwrite: true);
    }

    public static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception) { }
    }
}
