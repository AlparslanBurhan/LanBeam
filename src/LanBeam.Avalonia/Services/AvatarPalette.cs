using Avalonia.Media;
using LanBeam.Core.Models;

namespace LanBeam.Ui.Services;

/// <summary>
/// Hazır avatarlar: vektör (Material Design) ikon + renkli daire arka planı.
/// Windows (WPF) Segoe MDL2 kullanıyor; macOS'ta o font yok. Emoji denendi ama Apple Color
/// Emoji metrikleri daire içinde düzgün ortalanmıyordu; bu yüzden aynı düz görünümü veren
/// vektör path'ler kullanılır — kesin sınırları olduğu için Viewbox içinde kusursuz ortalanır.
/// </summary>
public static class AvatarPalette
{
    // 24x24 tuval içinde tasarlanmış ikonlar: kişi, monitör, oyun kolu, kulaklık, kamera, küre.
    private static readonly string[] IconPaths =
    [
        "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z",
        "M21,16H3V4H21M21,2H3C1.89,2 1,2.89 1,4V16A2,2 0 0,0 3,18H10V20H8V22H16V20H14V18H21A2,2 0 0,0 23,16V4C23,2.89 22.1,2 21,2Z",
        "M7.97,16L5,19C4.67,19.3 4.23,19.5 3.75,19.5A1.75,1.75 0 0,1 2,17.75V17.5L3,10.12C3.21,7.81 5.14,6 7.5,6H16.5C18.86,6 20.79,7.81 21,10.12L22,17.5V17.75A1.75,1.75 0 0,1 20.25,19.5C19.77,19.5 19.33,19.3 19,19L16.03,16H7.97M7,8V10H5V11H7V13H8V11H10V10H8V8H7M16.5,8A0.75,0.75 0 0,0 15.75,8.75A0.75,0.75 0 0,0 16.5,9.5A0.75,0.75 0 0,0 17.25,8.75A0.75,0.75 0 0,0 16.5,8M14.5,10A0.75,0.75 0 0,0 13.75,10.75A0.75,0.75 0 0,0 14.5,11.5A0.75,0.75 0 0,0 15.25,10.75A0.75,0.75 0 0,0 14.5,10M18.5,10A0.75,0.75 0 0,0 17.75,10.75A0.75,0.75 0 0,0 18.5,11.5A0.75,0.75 0 0,0 19.25,10.75A0.75,0.75 0 0,0 18.5,10M16.5,12A0.75,0.75 0 0,0 15.75,12.75A0.75,0.75 0 0,0 16.5,13.5A0.75,0.75 0 0,0 17.25,12.75A0.75,0.75 0 0,0 16.5,12Z",
        "M12,1C7,1 3,5 3,10V17A3,3 0 0,0 6,20H9V12H5V10A7,7 0 0,1 12,3A7,7 0 0,1 19,10V12H15V20H18A3,3 0 0,0 21,17V10C21,5 17,1 12,1Z",
        "M4,4H7L9,2H15L17,4H20A2,2 0 0,1 22,6V18A2,2 0 0,1 20,20H4A2,2 0 0,1 2,18V6A2,2 0 0,1 4,4M12,7A5,5 0 0,0 7,12A5,5 0 0,0 12,17A5,5 0 0,0 17,12A5,5 0 0,0 12,7M12,9A3,3 0 0,1 15,12A3,3 0 0,1 12,15A3,3 0 0,1 9,12A3,3 0 0,1 12,9Z",
        "M16.36,14C16.44,13.34 16.5,12.68 16.5,12C16.5,11.32 16.44,10.66 16.36,10H19.74C19.9,10.64 20,11.31 20,12C20,12.69 19.9,13.36 19.74,14M14.59,19.56C15.19,18.45 15.65,17.25 15.97,16H18.92C17.96,17.65 16.43,18.93 14.59,19.56M14.34,14H9.66C9.56,13.34 9.5,12.68 9.5,12C9.5,11.32 9.56,10.65 9.66,10H14.34C14.43,10.65 14.5,11.32 14.5,12C14.5,12.68 14.43,13.34 14.34,14M12,19.96C11.17,18.76 10.5,17.43 10.09,16H13.91C13.5,17.43 12.83,18.76 12,19.96M8,8H5.08C6.03,6.34 7.57,5.06 9.4,4.44C8.8,5.55 8.35,6.75 8,8M5.08,16H8C8.35,17.25 8.8,18.45 9.4,19.56C7.57,18.93 6.03,17.65 5.08,16M4.26,14C4.1,13.36 4,12.69 4,12C4,11.31 4.1,10.64 4.26,10H7.64C7.56,10.66 7.5,11.32 7.5,12C7.5,12.68 7.56,13.34 7.64,14M12,4.03C12.83,5.23 13.5,6.57 13.91,8H10.09C10.5,6.57 11.17,5.23 12,4.03M18.92,8H15.97C15.65,6.75 15.19,5.55 14.59,4.44C16.43,5.07 17.96,6.34 18.92,8M12,2C6.47,2 2,6.5 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
    ];

    private static readonly Geometry?[] IconCache = new Geometry?[IconPaths.Length];

    private static readonly Color[] Colors =
    [
        Color.Parse("#0078D4"), Color.Parse("#107C10"), Color.Parse("#D13438"),
        Color.Parse("#8764B8"), Color.Parse("#CA5010"), Color.Parse("#018574"),
        Color.Parse("#C239B3"), Color.Parse("#986F0B"),
    ];

    public static int Count => AvatarTags.PresetCount;

    public static (Geometry Icon, IBrush Brush) Get(int presetId)
    {
        int i = ((presetId % Count) + Count) % Count;
        int gi = i % IconPaths.Length;
        Geometry icon = IconCache[gi] ??= Geometry.Parse(IconPaths[gi]);
        return (icon, new SolidColorBrush(Colors[i % Colors.Length]));
    }

    public static (Geometry Icon, IBrush Brush) ForDevice(string? avatarTag, string deviceId)
    {
        if (AvatarTags.TryGetPreset(avatarTag, out int preset))
            return Get(preset);
        AvatarTags.TryGetPreset(AvatarTags.DefaultFor(deviceId), out int fallback);
        return Get(fallback);
    }
}
