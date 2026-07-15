using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanBeam.Core.Protocol;

namespace LanBeam.Core.Transfer;

/// <summary>
/// Yarım kalan transferin durumu. Hedef klasörde ".lanbeam-partial.json" olarak saklanır;
/// aynı içerik (imza eşleşmesi) aynı hedefe yeniden gönderilirse tamamlanan parçalar atlanır.
/// Parçalar dosya kimliğiyle değil göreli yolla anahtarlanır — gönderen yeniden taradığında
/// dosya kimlikleri değişebilir.
/// </summary>
public sealed class PartialState
{
    public const string FileName = ".lanbeam-partial.json";

    public string Signature { get; set; } = "";

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

    public static PartialState? TryLoad(string destinationRoot, string expectedSignature)
    {
        string path = Path.Combine(destinationRoot, FileName);
        try
        {
            if (!File.Exists(path)) return null;
            var state = JsonSerializer.Deserialize<PartialState>(File.ReadAllText(path));
            return state?.Signature == expectedSignature ? state : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(string destinationRoot)
    {
        string path = Path.Combine(destinationRoot, FileName);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this));
        File.Move(tmp, path, overwrite: true);
    }

    public static void Delete(string destinationRoot)
    {
        try { File.Delete(Path.Combine(destinationRoot, FileName)); }
        catch (Exception) { }
    }
}
