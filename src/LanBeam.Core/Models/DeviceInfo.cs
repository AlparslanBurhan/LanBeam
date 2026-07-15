namespace LanBeam.Core.Models;

/// <summary>Ağda keşfedilen bir cihaz.</summary>
public sealed class DeviceInfo
{
    public required string DeviceId { get; init; }
    public required string Name { get; set; }

    /// <summary>Cihazın IPv4 adresi (keşif paketinin geldiği adres).</summary>
    public required string Address { get; set; }

    /// <summary>Cihazın transfer dinleyicisinin TCP portu.</summary>
    public required int Port { get; set; }

    /// <summary>Cihaz sertifikasının SHA-256 parmak izi (hex).</summary>
    public required string CertFingerprint { get; set; }

    /// <summary>Avatar etiketi ("preset:N" ya da "img:HASH8"); eski sürümlerde null.</summary>
    public string? AvatarTag { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public DeviceInfo Clone() => new()
    {
        DeviceId = DeviceId,
        Name = Name,
        Address = Address,
        Port = Port,
        CertFingerprint = CertFingerprint,
        AvatarTag = AvatarTag,
        LastSeen = LastSeen,
    };
}
