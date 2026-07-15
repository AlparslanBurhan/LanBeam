namespace LanBeam.Core.Protocol;

/// <summary>İki uç arasında anlaşılmış protokol sabitleri. Değiştirmek eski sürümlerle uyumu bozar.</summary>
public static class ProtocolConstants
{
    public const int ProtocolVersion = 1;

    /// <summary>Veri kanallarında dosyaların bölündüğü parça boyutu.</summary>
    public const int ChunkSize = 16 * 1024 * 1024;

    /// <summary>Bu boyutun altındaki dosyalar tek parça olarak gönderilir (bölünmez).</summary>
    public const long SplitThreshold = 32L * 1024 * 1024;

    /// <summary>
    /// Kimlik doğrulaması/eşleştirme öncesi tüm JSON çerçeveleri için üst sınır (2 MB).
    /// Eşleşmemiş bir cihazın büyük tampon ayırtarak bellek-DoS yapmasını engeller.
    /// Avatar yanıtı (≤512 KB PNG → base64) da bu sınıra sığar.
    /// </summary>
    public const int MaxControlFrame = 2 * 1024 * 1024;

    /// <summary>
    /// Yalnızca eşleştirme sonrası, Offer (çok dosyalı metadata) için yükseltilen sınır (64 MB).
    /// Bu, JsonChannel.MaxFrameBytes güvenilir cihaz için elle yükseltildiğinde geçerli olur.
    /// </summary>
    public const int MaxOfferFrame = 64 * 1024 * 1024;

    /// <summary>Eşzamanlı gelen bağlantı üst sınırı (bağlantı seli / kaynak tükenmesi koruması).</summary>
    public const int MaxConcurrentConnections = 64;

    /// <summary>Veri kanalında "akış bitti" işareti olarak kullanılan dosya kimliği.</summary>
    public const int EndOfStreamFileId = -1;

    public const int DefaultTcpPort = 45655;
    public const int DefaultUdpPort = 45654;
    public const string DefaultMulticastAddress = "239.255.42.99";

    /// <summary>Soket gönderme/alma tampon boyutu.</summary>
    public const int SocketBufferSize = 1024 * 1024;

    /// <summary>Dosya okuma/yazma tampon boyutu.</summary>
    public const int FileBufferSize = 1024 * 1024;
}
