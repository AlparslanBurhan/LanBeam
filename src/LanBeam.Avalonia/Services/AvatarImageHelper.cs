using System;
using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;

namespace LanBeam.Ui.Services;

public static class AvatarImageHelper
{
    /// <summary>Seçilen görüntüyü ortadan kare kırpıp 128x128 PNG'ye dönüştürür.</summary>
    public static byte[] ProcessToSquarePng(string filePath, int size = 128)
    {
        using var src = new Bitmap(filePath);
        double side = Math.Min(src.PixelSize.Width, src.PixelSize.Height);
        var srcRect = new Rect((src.PixelSize.Width - side) / 2, (src.PixelSize.Height - side) / 2, side, side);

        var rtb = new RenderTargetBitmap(new PixelSize(size, size));
        using (var ctx = rtb.CreateDrawingContext())
            ctx.DrawImage(src, srcRect, new Rect(0, 0, size, size));

        using var ms = new MemoryStream();
        rtb.Save(ms);
        return ms.ToArray();
    }
}
