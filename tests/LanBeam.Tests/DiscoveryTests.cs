using System.Net;
using System.Net.Sockets;
using LanBeam.Core.Discovery;
using LanBeam.Core.Models;

namespace LanBeam.Tests;

public class DiscoveryTests
{
    private static int GetFreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task IkiServis_BirbiriniKesfeder()
    {
        int udpPort = GetFreeUdpPort();
        const string multicast = "239.255.42.98"; // uygulamanın gerçek grubuyla çakışmasın

        using var a = new DiscoveryService("cihaz-a", () => "Cihaz A", 50001, udpPort, multicast, "FPA");
        using var b = new DiscoveryService("cihaz-b", () => "Cihaz B", 50002, udpPort, multicast, "FPB");

        var aFoundB = new TaskCompletionSource<DeviceInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bFoundA = new TaskCompletionSource<DeviceInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        a.DeviceUpdated += d => { if (d.DeviceId == "cihaz-b") aFoundB.TrySetResult(d); };
        b.DeviceUpdated += d => { if (d.DeviceId == "cihaz-a") bFoundA.TrySetResult(d); };

        a.Start();
        b.Start();

        // Periyodik announce 5 sn'de bir; iki yön için bolca pay bırak.
        Task all = Task.WhenAll(aFoundB.Task, bFoundA.Task);
        Task finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(15)));

        Assert.True(finished == all, "Cihazlar 15 sn içinde birbirini keşfedemedi.");

        DeviceInfo b2 = await aFoundB.Task;
        Assert.Equal("Cihaz B", b2.Name);
        Assert.Equal(50002, b2.Port);
        Assert.Equal("FPB", b2.CertFingerprint);
    }
}
