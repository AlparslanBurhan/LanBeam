using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using LanBeam.Core;
using LanBeam.Core.Models;

namespace LanBeam.Ui.Services;

/// <summary>Karşı cihazların özel avatar fotoğraflarını indirir ve önbellekler (yalnızca güvenilir cihazlar).</summary>
public sealed class AvatarCacheService
{
    private readonly LanBeamNode _node;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, Bitmap> _memory = new();
    private readonly ConcurrentDictionary<string, bool> _fetching = new();

    public event Action<string>? AvatarReady;

    public AvatarCacheService(LanBeamNode node)
    {
        _node = node;
        _cacheDir = Path.Combine(node.Settings.DataDirectory, "avatars");
        Directory.CreateDirectory(_cacheDir);
    }

    public Bitmap? TryGet(string? avatarTag)
    {
        if (!AvatarTags.IsImage(avatarTag)) return null;
        string tag = avatarTag!;
        if (_memory.TryGetValue(tag, out Bitmap? cached)) return cached;

        string path = PathFor(tag);
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new Bitmap(path);
            _memory[tag] = bmp;
            return bmp;
        }
        catch (Exception)
        {
            try { File.Delete(path); } catch (Exception) { }
            return null;
        }
    }

    public void EnsureFetched(DeviceInfo device)
    {
        string? tag = device.AvatarTag;
        if (!AvatarTags.IsImage(tag) || _memory.ContainsKey(tag!) || File.Exists(PathFor(tag!)))
            return;
        if (!_node.TrustedDevices.IsTrusted(device.CertFingerprint))
            return;
        if (!_fetching.TryAdd(tag!, true))
            return;

        DeviceInfo snapshot = device.Clone();
        _ = Task.Run(async () =>
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                byte[]? png = await _node.FetchAvatarAsync(snapshot, timeout.Token).ConfigureAwait(false);
                if (png is null || AvatarTags.ForImageBytes(png) != tag) return;

                await File.WriteAllBytesAsync(PathFor(tag!), png).ConfigureAwait(false);
                using var ms = new MemoryStream(png);
                _memory[tag!] = new Bitmap(ms);
                AvatarReady?.Invoke(snapshot.DeviceId);
            }
            catch (Exception) { }
            finally { _fetching.TryRemove(tag!, out _); }
        });
    }

    private string PathFor(string tag) => Path.Combine(_cacheDir, tag[4..] + ".png");
}
