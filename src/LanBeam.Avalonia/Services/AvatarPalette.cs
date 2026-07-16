using Avalonia.Media;
using LanBeam.Core.Models;

namespace LanBeam.Ui.Services;

/// <summary>
/// Hazır avatarlar: emoji (çapraz-platform, macOS'ta da render olur) + renkli daire arka planı.
/// WPF sürümü Segoe MDL2 kullanıyordu; macOS'ta o font yok, bu yüzden emoji tercih edildi.
/// </summary>
public static class AvatarPalette
{
    private static readonly string[] Glyphs = ["🧑", "🖥️", "🎮", "🎧", "📷", "🌐"];

    private static readonly Color[] Colors =
    [
        Color.Parse("#0078D4"), Color.Parse("#107C10"), Color.Parse("#D13438"),
        Color.Parse("#8764B8"), Color.Parse("#CA5010"), Color.Parse("#018574"),
        Color.Parse("#C239B3"), Color.Parse("#986F0B"),
    ];

    public static int Count => AvatarTags.PresetCount;

    public static (string Glyph, IBrush Brush) Get(int presetId)
    {
        int i = ((presetId % Count) + Count) % Count;
        return (Glyphs[i % Glyphs.Length], new SolidColorBrush(Colors[i % Colors.Length]));
    }

    public static (string Glyph, IBrush Brush) ForDevice(string? avatarTag, string deviceId)
    {
        if (AvatarTags.TryGetPreset(avatarTag, out int preset))
            return Get(preset);
        AvatarTags.TryGetPreset(AvatarTags.DefaultFor(deviceId), out int fallback);
        return Get(fallback);
    }
}
