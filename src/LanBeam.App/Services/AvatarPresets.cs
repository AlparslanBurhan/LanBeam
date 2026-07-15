using System.Windows.Media;
using LanBeam.Core.Models;

namespace LanBeam.App.Services;

/// <summary>Uygulama içi hazır avatarlar: Segoe MDL2 silüet + renk kombinasyonları.</summary>
public static class AvatarPresets
{
    private static readonly string[] Glyphs =
    [
        "", // kişi
        "", // monitör
        "", // oyun kolu
        "", // kulaklık
        "", // kamera
        "", // dünya
    ];

    private static readonly Color[] Colors =
    [
        (Color)ColorConverter.ConvertFromString("#0078D4"), // mavi
        (Color)ColorConverter.ConvertFromString("#107C10"), // yeşil
        (Color)ColorConverter.ConvertFromString("#D13438"), // kırmızı
        (Color)ColorConverter.ConvertFromString("#8764B8"), // mor
        (Color)ColorConverter.ConvertFromString("#CA5010"), // turuncu
        (Color)ColorConverter.ConvertFromString("#018574"), // turkuaz
        (Color)ColorConverter.ConvertFromString("#C239B3"), // pembe
        (Color)ColorConverter.ConvertFromString("#986F0B"), // altın
    ];

    public static int Count => AvatarTags.PresetCount;

    public static (string Glyph, Brush Brush) Get(int presetId)
    {
        int i = ((presetId % Count) + Count) % Count;
        var brush = new SolidColorBrush(Colors[i % Colors.Length]);
        brush.Freeze();
        return (Glyphs[i % Glyphs.Length], brush);
    }

    /// <summary>Etiketten görsel: preset etiketi doğrudan, aksi halde cihaz kimliğinden türet.</summary>
    public static (string Glyph, Brush Brush) ForDevice(string? avatarTag, string deviceId)
    {
        if (AvatarTags.TryGetPreset(avatarTag, out int preset))
            return Get(preset);
        AvatarTags.TryGetPreset(AvatarTags.DefaultFor(deviceId), out int fallback);
        return Get(fallback);
    }
}
