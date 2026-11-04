using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanBeam.Core;

namespace LanBeam.App.Services;

/// <summary>Avatar önbelleği, WPF görüntüsü olarak (indirme ve güven kuralları Core'da).</summary>
public sealed class AvatarCacheService(LanBeamNode node) : AvatarCache<ImageSource>(node, LoadFrozen)
{
    private static ImageSource LoadFrozen(byte[] bytes)
    {
        var image = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze(); // UI dışı iş parçacığından kullanılabilsin
        return image;
    }
}
