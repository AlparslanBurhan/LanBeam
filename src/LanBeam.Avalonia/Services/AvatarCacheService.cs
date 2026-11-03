using System.IO;
using Avalonia.Media.Imaging;
using LanBeam.Core;

namespace LanBeam.Ui.Services;

/// <summary>Avatar önbelleği, Avalonia görüntüsü olarak (indirme ve güven kuralları Core'da).</summary>
public sealed class AvatarCacheService(LanBeamNode node)
    : AvatarCache<Bitmap>(node, png => new Bitmap(new MemoryStream(png)));
