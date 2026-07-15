using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LanBeam.Core;
using LanBeam.Core.Models;

namespace LanBeam.App.Services;

/// <summary>
/// Karşı cihazların özel avatar fotoğraflarını indirir ve önbellekler
/// (bellek + disk: %APPDATA%\LanBeam\avatars\HASH8.png).
/// </summary>
public sealed class AvatarCacheService
{
    private readonly LanBeamNode _node;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, ImageSource> _memory = new();
    private readonly ConcurrentDictionary<string, bool> _fetching = new();

    /// <summary>Bir cihazın avatarı indirildi (parametre: deviceId).</summary>
    public event Action<string>? AvatarReady;

    public AvatarCacheService(LanBeamNode node)
    {
        _node = node;
        _cacheDir = Path.Combine(node.Settings.DataDirectory, "avatars");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Önbellekten görsel döndürür; yoksa null (indirme EnsureFetched ile tetiklenir).</summary>
    public ImageSource? TryGet(string? avatarTag)
    {
        if (!AvatarTags.IsImage(avatarTag)) return null;
        string tag = avatarTag!;

        if (_memory.TryGetValue(tag, out ImageSource? cached))
            return cached;

        string path = PathFor(tag);
        if (!File.Exists(path)) return null;

        try
        {
            ImageSource image = LoadFrozen(File.ReadAllBytes(path));
            _memory[tag] = image;
            return image;
        }
        catch (Exception)
        {
            try { File.Delete(path); } catch (Exception) { }
            return null;
        }
    }

    /// <summary>Gerekirse avatarı arka planda indirir; bittiğinde AvatarReady tetiklenir.</summary>
    public void EnsureFetched(DeviceInfo device)
    {
        string? tag = device.AvatarTag;
        if (!AvatarTags.IsImage(tag) || _memory.ContainsKey(tag!) || File.Exists(PathFor(tag!)))
            return;

        // Yalnızca eşleşmiş (güvenilir) cihazlardan avatar çek. Eşleşmemiş bir cihazın
        // duyurusuyla otomatik dış bağlantı açıp güvenilmeyen görüntü çözmeyi engeller.
        if (!_node.TrustedDevices.IsTrusted(device.CertFingerprint))
            return;
        if (!_fetching.TryAdd(tag!, true))
            return; // zaten indiriliyor

        DeviceInfo snapshot = device.Clone();
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                byte[]? png = await _node.FetchAvatarAsync(snapshot, timeout.Token).ConfigureAwait(false);
                if (png is null || AvatarTags.ForImageBytes(png) != tag)
                    return; // avatar bu arada değişmiş ya da yok

                ImageSource image = LoadFrozen(png);
                await File.WriteAllBytesAsync(PathFor(tag!), png).ConfigureAwait(false);
                _memory[tag!] = image;
                AvatarReady?.Invoke(snapshot.DeviceId);
            }
            catch (Exception) { }
            finally
            {
                _fetching.TryRemove(tag!, out _);
            }
        });
    }

    private string PathFor(string tag) => Path.Combine(_cacheDir, tag[4..] + ".png");

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
