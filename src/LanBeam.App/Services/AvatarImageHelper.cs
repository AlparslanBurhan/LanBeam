using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LanBeam.App.Services;

public static class AvatarImageHelper
{
    /// <summary>Seçilen görüntüyü ortadan kare kırpıp 128x128 PNG'ye dönüştürür (~10-30 KB).</summary>
    public static byte[] ProcessToSquarePng(string filePath, int size = 128)
    {
        var src = new BitmapImage();
        src.BeginInit();
        src.UriSource = new Uri(filePath);
        src.CacheOption = BitmapCacheOption.OnLoad;
        src.EndInit();

        int side = Math.Min(src.PixelWidth, src.PixelHeight);
        var cropped = new CroppedBitmap(src, new Int32Rect(
            (src.PixelWidth - side) / 2, (src.PixelHeight - side) / 2, side, side));

        double scale = size / (double)side;
        var scaled = new TransformedBitmap(cropped, new ScaleTransform(scale, scale));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
