using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LanBeam.Core.Models;

namespace LanBeam.Core.Discovery;

/// <summary>UDP multicast/broadcast üzerinden cihaz keşfi (LocalSend'in hibrit modeline benzer).</summary>
public sealed class DiscoveryService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DeviceTimeout = TimeSpan.FromSeconds(15);

    private readonly string _deviceId;
    private readonly Func<string> _deviceName;
    private readonly int _tcpPort;
    private readonly int _udpPort;
    private readonly IPAddress _multicastAddress;
    private readonly string _certFingerprint;
    private readonly Func<string>? _avatarTag;

    private readonly Dictionary<string, DeviceInfo> _devices = new();
    private readonly object _gate = new();

    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private readonly List<Task> _loops = [];

    /// <summary>Cihaz eklendi ya da güncellendi.</summary>
    public event Action<DeviceInfo>? DeviceUpdated;

    /// <summary>Cihaz zaman aşımıyla ya da veda paketiyle düştü.</summary>
    public event Action<string>? DeviceLost;

    public DiscoveryService(string deviceId, Func<string> deviceName, int tcpPort, int udpPort,
        string multicastAddress, string certFingerprint, Func<string>? avatarTag = null)
    {
        _deviceId = deviceId;
        _deviceName = deviceName;
        _tcpPort = tcpPort;
        _udpPort = udpPort;
        _multicastAddress = IPAddress.Parse(multicastAddress);
        _certFingerprint = certFingerprint;
        _avatarTag = avatarTag;
    }

    public IReadOnlyList<DeviceInfo> Devices
    {
        get { lock (_gate) return _devices.Values.Select(d => d.Clone()).ToList(); }
    }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();

        var listener = new UdpClient(AddressFamily.InterNetwork);
        listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        listener.Client.Bind(new IPEndPoint(IPAddress.Any, _udpPort));
        JoinMulticastOnAllInterfaces(listener);
        listener.EnableBroadcast = true;
        _listener = listener;

        _loops.Add(Task.Run(() => ReceiveLoopAsync(_cts.Token)));
        _loops.Add(Task.Run(() => AnnounceLoopAsync(_cts.Token)));
        _loops.Add(Task.Run(() => SweepLoopAsync(_cts.Token)));
    }

    private void JoinMulticastOnAllInterfaces(UdpClient client)
    {
        bool joinedAny = false;
        foreach (IPAddress address in GetLocalIPv4Addresses())
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership,
                    new MulticastOption(_multicastAddress, address));
                joinedAny = true;
            }
            catch (SocketException) { }
        }

        if (!joinedAny)
        {
            try { client.JoinMulticastGroup(_multicastAddress); }
            catch (SocketException) { }
        }
    }

    private static List<IPAddress> GetLocalIPv4Addresses()
    {
        var result = new List<IPAddress>();
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            foreach (UnicastIPAddressInformation addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    result.Add(addr.Address);
            }
        }
        return result;
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await SendPacketAsync(isReply: false, isBye: false, target: null).ConfigureAwait(false); }
            catch (Exception) { }

            try { await Task.Delay(AnnounceInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SendPacketAsync(bool isReply, bool isBye, IPEndPoint? target)
    {
        var packet = new DiscoveryPacket(
            App: "lanbeam",
            V: Protocol.ProtocolConstants.ProtocolVersion,
            Id: _deviceId,
            Name: _deviceName(),
            Port: _tcpPort,
            Fp: _certFingerprint,
            Reply: isReply,
            Bye: isBye,
            Av: _avatarTag?.Invoke());

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packet, JsonOptions);

        if (target is not null)
        {
            // Doğrudan yanıt (unicast): multicast alamayan cihaz da bizi görür.
            using var sender = new UdpClient(AddressFamily.InterNetwork);
            await sender.SendAsync(bytes, target).ConfigureAwait(false);
            return;
        }

        // Her arayüzden hem multicast hem broadcast gönder (çok NIC'li makineler için).
        foreach (IPAddress local in GetLocalIPv4Addresses())
        {
            try
            {
                using var sender = new UdpClient(new IPEndPoint(local, 0));
                sender.EnableBroadcast = true;
                sender.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface,
                    local.GetAddressBytes());
                await sender.SendAsync(bytes, new IPEndPoint(_multicastAddress, _udpPort)).ConfigureAwait(false);
                await sender.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, _udpPort)).ConfigureAwait(false);
            }
            catch (SocketException) { }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is { } listener)
        {
            UdpReceiveResult result;
            try { result = await listener.ReceiveAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }
            catch (ObjectDisposedException) { break; }

            try { HandlePacket(result); }
            catch (Exception) { }
        }
    }

    private void HandlePacket(UdpReceiveResult result)
    {
        DiscoveryPacket? packet = JsonSerializer.Deserialize<DiscoveryPacket>(result.Buffer, JsonOptions);
        if (packet is null || packet.App != "lanbeam" || packet.Id == _deviceId)
            return;

        if (packet.Bye)
        {
            bool removed;
            lock (_gate) removed = _devices.Remove(packet.Id);
            if (removed) DeviceLost?.Invoke(packet.Id);
            return;
        }

        DeviceInfo snapshot;
        lock (_gate)
        {
            if (_devices.TryGetValue(packet.Id, out DeviceInfo? existing))
            {
                existing.Name = packet.Name;
                existing.Address = result.RemoteEndPoint.Address.ToString();
                existing.Port = packet.Port;
                existing.CertFingerprint = packet.Fp;
                existing.AvatarTag = packet.Av;
                existing.LastSeen = DateTimeOffset.Now;
                snapshot = existing.Clone();
            }
            else
            {
                var fresh = new DeviceInfo
                {
                    DeviceId = packet.Id,
                    Name = packet.Name,
                    Address = result.RemoteEndPoint.Address.ToString(),
                    Port = packet.Port,
                    CertFingerprint = packet.Fp,
                    AvatarTag = packet.Av,
                    LastSeen = DateTimeOffset.Now,
                };
                _devices[packet.Id] = fresh;
                snapshot = fresh.Clone();
            }
        }

        DeviceUpdated?.Invoke(snapshot);

        // Announce'a unicast yanıt ver ki karşı taraf da bizi hemen görsün (yanıt döngüsünü önle).
        if (!packet.Reply)
        {
            _ = SendPacketAsync(isReply: true, isBye: false,
                target: new IPEndPoint(result.RemoteEndPoint.Address, _udpPort));
        }
    }

    private async Task SweepLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            List<string> lost = [];
            lock (_gate)
            {
                DateTimeOffset cutoff = DateTimeOffset.Now - DeviceTimeout;
                foreach (var kvp in _devices.Where(k => k.Value.LastSeen < cutoff).ToList())
                {
                    _devices.Remove(kvp.Key);
                    lost.Add(kvp.Key);
                }
            }
            foreach (string id in lost)
                DeviceLost?.Invoke(id);
        }
    }

    public void Dispose()
    {
        try { SendPacketAsync(isReply: false, isBye: true, target: null).Wait(TimeSpan.FromSeconds(1)); }
        catch (Exception) { }

        _cts?.Cancel();
        _listener?.Dispose();
        _cts?.Dispose();
        _cts = null;
        _listener = null;
    }

    private sealed record DiscoveryPacket(string App, int V, string Id, string Name, int Port,
        string Fp, bool Reply, bool Bye, string? Av = null);
}
