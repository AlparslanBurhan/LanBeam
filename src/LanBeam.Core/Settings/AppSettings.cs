using LanBeam.Core.Protocol;

namespace LanBeam.Core.Settings;

public sealed class AppSettings
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = Environment.MachineName;

    public int TcpPort { get; set; } = ProtocolConstants.DefaultTcpPort;
    public int UdpPort { get; set; } = ProtocolConstants.DefaultUdpPort;
    public string MulticastAddress { get; set; } = ProtocolConstants.DefaultMulticastAddress;

    /// <summary>Paralel veri akışı sayısı (1-8).</summary>
    public int StreamCount { get; set; } = 4;

    /// <summary>Avatar seçimi: "preset:N" ya da "custom" (avatar.png). null = cihaz kimliğinden türetilir.</summary>
    public string? AvatarId { get; set; }

    public string DownloadFolder { get; set; } = DefaultDownloadFolder();

    /// <summary>true ise her transferde hedef klasör sorulur; false ise DownloadFolder kullanılır.</summary>
    public bool AlwaysAskDestination { get; set; } = true;

    public bool MinimizeToTray { get; set; } = true;
    public bool AutoStart { get; set; }

    /// <summary>Arayüz dili: "tr" ya da "en". null/boş ise sistem diline göre seçilir.</summary>
    public string? Language { get; set; }

    /// <summary>Güvenlik duvarı kuralı başarıyla eklendi; ilk çalıştırma istemi tekrar gösterilmez.</summary>
    public bool FirewallConfigured { get; set; }

    /// <summary>İlk çalıştırma güvenlik duvarı istemi bir kez gösterildi (reddedilse bile tekrarlanmaz).</summary>
    public bool FirewallPromptShown { get; set; }

    /// <summary>Transfer sonrası parça hash'lerine ek olarak diskten tam doğrulama (v1'de chunk hash zaten aktif).</summary>
    public bool VerifyAfterTransfer { get; set; } = true;

    public static string DefaultDownloadFolder()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, "Downloads", "LanBeam");
    }
}
