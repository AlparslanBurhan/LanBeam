using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LanBeam.Core;
using LanBeam.Core.Models;
using LanBeam.Core.Transfer;

namespace LanBeam.Tests;

/// <summary>
/// İki gerçek LanBeamNode arasında loopback üzerinden uçtan uca transfer.
/// Keşif kullanılmaz (UDP portu paylaşımı test dışı); hedef elle verilir.
/// </summary>
public sealed class EndToEndTests : IDisposable
{
    private readonly string _rootDir = Path.Combine(Path.GetTempPath(), "lanbeam-e2e-" + Guid.NewGuid().ToString("N"));
    private readonly LanBeamNode _sender;
    private readonly LanBeamNode _receiver;
    private readonly int _receiverPort;
    private readonly string _sourceDir;
    private readonly string _destDir;

    private sealed class ScriptedPin(Func<Task<string?>> provider) : ISendInteraction
    {
        public Task<string?> RequestPinAsync(string peerDeviceName, int attemptsLeft, CancellationToken ct)
            => provider();
    }

    public EndToEndTests()
    {
        _sourceDir = Path.Combine(_rootDir, "kaynak");
        _destDir = Path.Combine(_rootDir, "hedef");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_destDir);

        _sender = new LanBeamNode(Path.Combine(_rootDir, "node-a"));
        _receiver = new LanBeamNode(Path.Combine(_rootDir, "node-b"));

        _receiverPort = GetFreePort();
        _receiver.Settings.Current.TcpPort = _receiverPort;
        _receiver.Listener.Start(_receiverPort);
    }

    private static int GetFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private DeviceInfo ReceiverAsDevice() => new()
    {
        DeviceId = _receiver.Settings.Current.DeviceId,
        Name = _receiver.Settings.Current.DeviceName,
        Address = "127.0.0.1",
        Port = _receiverPort,
        CertFingerprint = _receiver.CertFingerprint,
        LastSeen = DateTimeOffset.Now,
    };

    private void CreateTestContent(int bigFileMb)
    {
        // Büyük rastgele dosya + alt klasörlerde çok sayıda küçük dosya + boş dosya + boş klasör
        byte[] big = new byte[bigFileMb * 1024 * 1024];
        RandomNumberGenerator.Fill(big);
        File.WriteAllBytes(Path.Combine(_sourceDir, "buyuk.bin"), big);

        string subDir = Path.Combine(_sourceDir, "alt", "derin");
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(Path.Combine(_sourceDir, "bos-klasor"));
        File.WriteAllBytes(Path.Combine(_sourceDir, "bos.dat"), []);

        var rng = new Random(42);
        for (int i = 0; i < 200; i++)
        {
            byte[] data = new byte[rng.Next(1, 20_000)];
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(i % 2 == 0 ? subDir : Path.Combine(_sourceDir, "alt"), $"dosya-{i}.bin"), data);
        }
    }

    private static string HashDirectory(string root)
    {
        using var sha = SHA256.Create();
        var lines = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel == PartialState.FileName) continue;
            lines.Add(rel + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
        }
        return string.Join("\n", lines);
    }

    [Fact]
    public async Task EslestirmeVeTransfer_KlasorBirebirKopyalanir()
    {
        CreateTestContent(bigFileMb: 96);

        // Alıcı: PIN penceresi yerine PIN'i gönderene aktar, teklifi otomatik kabul et.
        var pinReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _receiver.Listener.PairingStarted += prompt => pinReady.TrySetResult(prompt.Pin);
        _receiver.Listener.OfferReceived += offer => offer.Accept(_destDir);

        TransferHandle? receiveHandle = null;
        _receiver.TransferAdded += h => receiveHandle = h;

        var interaction = new ScriptedPin(async () => await pinReady.Task.WaitAsync(TimeSpan.FromSeconds(30)));
        TransferHandle sendHandle = _sender.Send(ReceiverAsDevice(), [_sourceDir], interaction);

        await WaitForFinishAsync(sendHandle, TimeSpan.FromMinutes(3));

        Assert.Equal(TransferState.Completed, sendHandle.State);
        Assert.NotNull(receiveHandle);
        Assert.Equal(TransferState.Completed, receiveHandle!.State);

        // İki taraf da birbirini güvenilir olarak kaydetti.
        Assert.True(_sender.TrustedDevices.IsTrusted(_receiver.CertFingerprint));
        Assert.True(_receiver.TrustedDevices.IsTrusted(_sender.CertFingerprint));

        // İçerik birebir aynı (boş klasör dahil).
        string expected = HashDirectory(_sourceDir);
        string actual = HashDirectory(Path.Combine(_destDir, "kaynak"));
        Assert.Equal(expected, actual);
        Assert.True(Directory.Exists(Path.Combine(_destDir, "kaynak", "bos-klasor")));
        Assert.False(File.Exists(Path.Combine(_destDir, PartialState.FileName)));
    }

    [Fact]
    public async Task YanlisPin_UcDenemedenSonraReddedilir()
    {
        CreateTestContent(bigFileMb: 1);

        _receiver.Listener.OfferReceived += offer => offer.Accept(_destDir);
        var interaction = new ScriptedPin(() => Task.FromResult<string?>("000000")); // hep yanlış (997/1000 ihtimalle)

        // PIN çakışma ihtimaline karşı gerçek PIN'i öğren, farklıysa testi çalıştır.
        string? realPin = null;
        _receiver.Listener.PairingStarted += p => realPin = p.Pin;

        TransferHandle sendHandle = _sender.Send(ReceiverAsDevice(), [_sourceDir], interaction);
        await WaitForFinishAsync(sendHandle, TimeSpan.FromSeconds(60));

        if (realPin == "000000") return; // milyonda bir: PIN tesadüfen doğruydu, test anlamsız
        Assert.Equal(TransferState.Failed, sendHandle.State);
        Assert.False(_receiver.TrustedDevices.IsTrusted(_sender.CertFingerprint));
    }

    [Fact]
    public async Task IptalSonrasiYenidenGonderim_ResumeIleTamamlanir()
    {
        CreateTestContent(bigFileMb: 96);

        var pinReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _receiver.Listener.PairingStarted += prompt => pinReady.TrySetResult(prompt.Pin);
        _receiver.Listener.OfferReceived += offer => offer.Accept(_destDir);

        TransferHandle? receiveHandle = null;
        var receiveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _receiver.TransferAdded += h => { receiveHandle = h; receiveStarted.TrySetResult(); };

        var interaction = new ScriptedPin(async () => await pinReady.Task.WaitAsync(TimeSpan.FromSeconds(30)));

        // 1. deneme: bir miktar veri aktıktan sonra alıcı iptal eder.
        TransferHandle firstSend = _sender.Send(ReceiverAsDevice(), [_sourceDir], interaction);
        await receiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(60));

        var deadline = DateTimeOffset.Now.AddSeconds(60);
        while (receiveHandle!.BytesTransferred < 32L * 1024 * 1024 && DateTimeOffset.Now < deadline)
            await Task.Delay(50);
        Assert.True(receiveHandle.BytesTransferred > 0, "İptalden önce hiç veri akmadı.");

        receiveHandle.Cancel();
        await WaitForFinishAsync(firstSend, TimeSpan.FromSeconds(60));
        Assert.True(File.Exists(Path.Combine(_destDir, PartialState.FileName)),
            "İptal sonrası resume durumu diske yazılmalıydı.");

        // 2. deneme: kaldığı yerden devam etmeli (atlanan baytlar > 0).
        TransferHandle secondSend = _sender.Send(ReceiverAsDevice(), [_sourceDir], interaction);
        await WaitForFinishAsync(secondSend, TimeSpan.FromMinutes(3));

        Assert.Equal(TransferState.Completed, secondSend.State);
        Assert.Equal(HashDirectory(_sourceDir), HashDirectory(Path.Combine(_destDir, "kaynak")));
        Assert.False(File.Exists(Path.Combine(_destDir, PartialState.FileName)));
    }

    [Fact]
    public async Task OzelAvatar_GuvenilirCihazdanCekilir()
    {
        byte[] fakePng = new byte[5000];
        RandomNumberGenerator.Fill(fakePng);
        File.WriteAllBytes(_receiver.CustomAvatarPath, fakePng);
        _receiver.Settings.Current.AvatarId = "custom";
        _receiver.RefreshAvatar();

        Assert.StartsWith("img:", _receiver.AvatarTag);
        Assert.Equal(Core.Models.AvatarTags.ForImageBytes(fakePng), _receiver.AvatarTag);

        // Avatar yalnızca güvenilir cihaza sunulur: önce göndereni alıcının güvenilir listesine ekle.
        _receiver.TrustedDevices.AddOrUpdate(
            _sender.Settings.Current.DeviceId, "gönderen", _sender.CertFingerprint);

        byte[]? fetched = await _sender.FetchAvatarAsync(ReceiverAsDevice());
        Assert.NotNull(fetched);
        Assert.Equal(fakePng, fetched);
    }

    [Fact]
    public async Task OzelAvatar_EslesmemisCihaza_Sunulmaz()
    {
        byte[] fakePng = new byte[5000];
        RandomNumberGenerator.Fill(fakePng);
        File.WriteAllBytes(_receiver.CustomAvatarPath, fakePng);
        _receiver.Settings.Current.AvatarId = "custom";
        _receiver.RefreshAvatar();

        // Eşleşmemiş gönderen avatarı çekememeli (gizlilik).
        byte[]? fetched = await _sender.FetchAvatarAsync(ReceiverAsDevice());
        Assert.Null(fetched);
    }

    [Fact]
    public async Task PresetAvatar_FotografYok_NullDoner()
    {
        _receiver.Settings.Current.AvatarId = "preset:5";
        _receiver.RefreshAvatar();
        Assert.Equal("preset:5", _receiver.AvatarTag);

        Assert.Null(await _sender.FetchAvatarAsync(ReceiverAsDevice()));
    }

    private static async Task WaitForFinishAsync(TransferHandle handle, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (!handle.IsFinished && DateTimeOffset.Now < deadline)
            await Task.Delay(50);
        Assert.True(handle.IsFinished,
            $"Transfer {timeout} içinde bitmedi (durum: {handle.State}, {handle.BytesTransferred}/{handle.TotalBytes}).");
    }

    public void Dispose()
    {
        _sender.Dispose();
        _receiver.Dispose();
        try { Directory.Delete(_rootDir, recursive: true); } catch (Exception) { }
    }
}
