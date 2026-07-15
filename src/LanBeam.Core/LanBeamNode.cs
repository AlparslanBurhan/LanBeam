using System.Security.Cryptography.X509Certificates;
using LanBeam.Core.Discovery;
using LanBeam.Core.Models;
using LanBeam.Core.Security;
using LanBeam.Core.Settings;
using LanBeam.Core.Transfer;

namespace LanBeam.Core;

/// <summary>
/// Uygulamanın çekirdeği: ayarlar, kimlik, keşif, dinleyici ve gönderim API'sini bir arada tutar.
/// UI yalnızca bu sınıfla konuşur.
/// </summary>
public sealed class LanBeamNode : IDisposable
{
    public SettingsStore Settings { get; }
    public TrustedDeviceStore TrustedDevices { get; }
    public DiscoveryService Discovery { get; }
    public TransferListener Listener { get; }
    public string CertFingerprint { get; }

    /// <summary>Bu cihazın yayınlanan avatar etiketi ("preset:N" ya da "img:HASH8").</summary>
    public string AvatarTag { get; private set; } = "";

    /// <summary>Özel avatar fotoğrafının yolu (%APPDATA%\LanBeam\avatar.png).</summary>
    public string CustomAvatarPath => Path.Combine(Settings.DataDirectory, "avatar.png");

    private byte[]? _customAvatarPng;
    private readonly X509Certificate2 _certificate;
    private readonly TransferSender _sender;

    /// <summary>Yeni bir transfer (gönderim ya da alım) başladı; UI listesine eklenmeli.</summary>
    public event Action<TransferHandle>? TransferAdded;

    public LanBeamNode(string? dataDirectory = null)
    {
        Settings = new SettingsStore(dataDirectory);
        AppSettings s = Settings.Current;

        _certificate = CertificateManager.GetOrCreate(Settings.DataDirectory, s.DeviceId);
        CertFingerprint = CertificateManager.GetFingerprint(_certificate);

        TrustedDevices = new TrustedDeviceStore(Settings.DataDirectory);

        RefreshAvatar();

        Discovery = new DiscoveryService(s.DeviceId, () => Settings.Current.DeviceName,
            s.TcpPort, s.UdpPort, s.MulticastAddress, CertFingerprint, () => AvatarTag);

        Listener = new TransferListener(_certificate, CertFingerprint, TrustedDevices,
            s.DeviceId, () => Settings.Current.DeviceName, () => Settings.Current.StreamCount)
        {
            AvatarProvider = () => (AvatarTag, _customAvatarPng),
        };
        Listener.TransferStarted += h => TransferAdded?.Invoke(h);

        _sender = new TransferSender(_certificate, CertFingerprint, TrustedDevices,
            s.DeviceId, () => Settings.Current.DeviceName);
    }

    public void Start()
    {
        Listener.Start(Settings.Current.TcpPort);
        Discovery.Start();
    }

    /// <summary>
    /// Avatar seçimini ayarlardan ve avatar.png'den yeniden yükler. Yeni etiket bir sonraki
    /// announce ile (≤5 sn) diğer cihazlara yayılır.
    /// </summary>
    public void RefreshAvatar()
    {
        string? id = Settings.Current.AvatarId;

        if (id == "custom" && File.Exists(CustomAvatarPath))
        {
            try
            {
                byte[] png = File.ReadAllBytes(CustomAvatarPath);
                if (png.Length <= AvatarTags.MaxImageBytes)
                {
                    _customAvatarPng = png;
                    AvatarTag = AvatarTags.ForImageBytes(png);
                    return;
                }
            }
            catch (Exception) { }
        }

        _customAvatarPng = null;
        AvatarTag = AvatarTags.TryGetPreset(id, out int preset)
            ? $"preset:{preset}"
            : AvatarTags.DefaultFor(Settings.Current.DeviceId);
    }

    /// <summary>Karşı cihazın özel avatar fotoğrafını çeker (preset kullanıyorsa null).</summary>
    public Task<byte[]?> FetchAvatarAsync(DeviceInfo target, CancellationToken ct = default) =>
        _sender.FetchAvatarAsync(target, ct);

    /// <summary>
    /// Gönderimi başlatır ve izleme kolu döndürür. Dosya taraması ve tüm ağ işi arka planda yürür.
    /// </summary>
    public TransferHandle Send(DeviceInfo target, IReadOnlyList<string> paths, ISendInteraction interaction)
    {
        var handle = new TransferHandle
        {
            Direction = TransferDirection.Send,
            DisplayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0]))
                          + (paths.Count > 1 ? $" (+{paths.Count - 1})" : ""),
            PeerName = target.Name,
        };
        TransferAdded?.Invoke(handle);

        _ = Task.Run(async () =>
        {
            ScannedTree tree;
            try
            {
                tree = FileTreeScanner.Scan(paths);
                handle.TotalBytes = tree.TotalBytes;
                handle.FileCount = tree.Files.Count;
            }
            catch (Exception ex)
            {
                handle.SetState(TransferState.Failed, $"Dosyalar okunamadı: {ex.Message}");
                return;
            }

            await _sender.RunAsync(target, tree, interaction, handle).ConfigureAwait(false);
        });

        return handle;
    }

    public void Dispose()
    {
        Discovery.Dispose();
        Listener.Dispose();
        _certificate.Dispose();
    }
}
