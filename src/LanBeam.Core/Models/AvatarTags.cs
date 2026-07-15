using System.Security.Cryptography;

namespace LanBeam.Core.Models;

/// <summary>
/// Avatar etiketi: keşif paketinde yayınlanan kısa kimlik.
/// "preset:N" → uygulama içi hazır avatar (ağdan veri çekilmez).
/// "img:HASH8" → özel fotoğraf; eşleşen önbellek yoksa TLS üzerinden çekilir.
/// </summary>
public static class AvatarTags
{
    public const int PresetCount = 12;
    public const int MaxImageBytes = 512 * 1024;

    /// <summary>Etiketi olmayan (eski sürüm) cihazlar için cihaz kimliğinden kararlı hazır avatar türet.</summary>
    public static string DefaultFor(string deviceId) => $"preset:{StableHash(deviceId) % PresetCount}";

    public static bool TryGetPreset(string? tag, out int presetId)
    {
        presetId = 0;
        if (tag is null || !tag.StartsWith("preset:", StringComparison.Ordinal)) return false;
        if (!int.TryParse(tag.AsSpan(7), out int id)) return false;
        presetId = ((id % PresetCount) + PresetCount) % PresetCount;
        return true;
    }

    public static bool IsImage(string? tag) => tag?.StartsWith("img:", StringComparison.Ordinal) == true;

    public static string ForImageBytes(byte[] png) =>
        "img:" + Convert.ToHexString(SHA256.HashData(png))[..8];

    private static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in s) h = h * 31 + c;
            return h == int.MinValue ? 0 : Math.Abs(h);
        }
    }
}
