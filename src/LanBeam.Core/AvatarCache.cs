using System.Collections.Concurrent;
using LanBeam.Core.Models;

namespace LanBeam.Core;

/// <summary>
/// Karşı cihazların özel avatar fotoğraflarını indirir ve önbellekler
/// (bellek + disk: veri klasörü\avatars\HASH8.png). Görüntü çözme UI'ya özgüdür ve dışarıdan verilir.
/// </summary>
public class AvatarCache<TImage> where TImage : class
{
    private readonly LanBeamNode _node;
    private readonly Func<byte[], TImage> _decode;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, TImage> _memory = new();
    private readonly ConcurrentDictionary<string, bool> _fetching = new();

    /// <summary>Bir cihazın avatarı indirildi (parametre: deviceId).</summary>
    public event Action<string>? AvatarReady;

    public AvatarCache(LanBeamNode node, Func<byte[], TImage> decode)
    {
        _node = node;
        _decode = decode;
        _cacheDir = Path.Combine(node.Settings.DataDirectory, "avatars");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>Önbellekten görsel döndürür; yoksa null (indirme EnsureFetched ile tetiklenir).</summary>
    public TImage? TryGet(string? avatarTag)
    {
        if (!AvatarTags.IsImage(avatarTag)) return null;
        string tag = avatarTag!;
        if (_memory.TryGetValue(tag, out TImage? cached)) return cached;

        string path = PathFor(tag);
        if (!File.Exists(path)) return null;
        try
        {
            return _memory[tag] = _decode(File.ReadAllBytes(path));
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

                TImage image = _decode(png); // çözülemeyen görüntü diske yazılmasın
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
}
