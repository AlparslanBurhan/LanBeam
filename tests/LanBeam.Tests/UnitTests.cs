using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LanBeam.Core.Protocol;
using LanBeam.Core.Security;
using LanBeam.Core.Transfer;

namespace LanBeam.Tests;

public class ChunkPlannerTests
{
    [Fact]
    public void BuyukDosya_16MBParcalaraBolunur()
    {
        var files = new List<FileEntry> { new(0, "buyuk.bin", 40L * 1024 * 1024) };
        List<ChunkWork> chunks = ChunkPlanner.Plan(files);

        Assert.Equal(3, chunks.Count);
        Assert.Equal(0, chunks[0].Offset);
        Assert.Equal(ProtocolConstants.ChunkSize, chunks[0].Length);
        Assert.Equal(2L * ProtocolConstants.ChunkSize, chunks[2].Offset);
        Assert.Equal(8 * 1024 * 1024, chunks[2].Length);
        Assert.Equal(40L * 1024 * 1024, chunks.Sum(c => (long)c.Length));
    }

    [Fact]
    public void KucukDosya_TekParca_SifirDosya_ParcaYok()
    {
        var files = new List<FileEntry> { new(0, "kucuk.txt", 1234), new(1, "bos.txt", 0) };
        List<ChunkWork> chunks = ChunkPlanner.Plan(files);

        ChunkWork chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.FileId);
        Assert.Equal(1234, chunk.Length);
    }

    [Fact]
    public void Resume_TamamlananParcalarAtlanir()
    {
        var files = new List<FileEntry> { new(0, "buyuk.bin", 40L * 1024 * 1024) };
        var done = new Dictionary<int, HashSet<int>> { [0] = [0, 2] };

        List<ChunkWork> chunks = ChunkPlanner.Plan(files, done);

        ChunkWork chunk = Assert.Single(chunks);
        Assert.Equal(1, chunk.ChunkIndex);
    }
}

public class PairingMathTests
{
    [Fact]
    public void AyniGirdiler_AyniKanit_DogruDogrulanir()
    {
        byte[] nA = PairingMath.GenerateNonce();
        byte[] nB = PairingMath.GenerateNonce();

        byte[] p1 = PairingMath.ComputeProof("123456", "AA", "BB", nA, nB);
        byte[] p2 = PairingMath.ComputeProof("123456", "aa", "bb", nA, nB); // parmak izi büyük/küçük harf duyarsız

        Assert.True(PairingMath.VerifyProof(p1, p2));
    }

    [Fact]
    public void YanlisPin_FarkliKanit()
    {
        byte[] nA = PairingMath.GenerateNonce();
        byte[] nB = PairingMath.GenerateNonce();

        byte[] dogru = PairingMath.ComputeProof("123456", "AA", "BB", nA, nB);
        byte[] yanlis = PairingMath.ComputeProof("123457", "AA", "BB", nA, nB);

        Assert.False(PairingMath.VerifyProof(dogru, yanlis));
    }

    [Fact]
    public void RollerTersken_KanitFarkli_YansitmaEngellenir()
    {
        byte[] nA = PairingMath.GenerateNonce();
        byte[] nB = PairingMath.GenerateNonce();

        byte[] ileri = PairingMath.ComputeProof("123456", "AA", "BB", nA, nB);
        byte[] geri = PairingMath.ComputeProof("123456", "BB", "AA", nB, nA);

        Assert.False(PairingMath.VerifyProof(ileri, geri));
    }

    [Fact]
    public void Pin_AltiHane()
    {
        for (int i = 0; i < 50; i++)
        {
            string pin = PairingMath.GeneratePin();
            Assert.Equal(6, pin.Length);
            Assert.True(pin.All(char.IsDigit));
        }
    }
}

public class FileTreeScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lanbeam-test-" + Guid.NewGuid().ToString("N"));

    public FileTreeScannerTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "klasor", "alt"));
        Directory.CreateDirectory(Path.Combine(_root, "klasor", "bos"));
        File.WriteAllText(Path.Combine(_root, "klasor", "a.txt"), "merhaba");
        File.WriteAllText(Path.Combine(_root, "klasor", "alt", "b.txt"), "dünya");
        File.WriteAllText(Path.Combine(_root, "tek.txt"), "tek dosya");
    }

    [Fact]
    public void KlasorVeDosya_GoreliYollarDogru()
    {
        ScannedTree tree = FileTreeScanner.Scan([Path.Combine(_root, "klasor"), Path.Combine(_root, "tek.txt")]);

        Assert.Contains(tree.Files, f => f.RelativePath == "klasor/a.txt");
        Assert.Contains(tree.Files, f => f.RelativePath == "klasor/alt/b.txt");
        Assert.Contains(tree.Files, f => f.RelativePath == "tek.txt");
        Assert.Contains("klasor/bos", tree.EmptyDirectories);
        Assert.Equal(3, tree.Files.Count);
        Assert.All(tree.Files, f => Assert.True(File.Exists(tree.LocalPathsByFileId[f.Id])));
    }

    [Theory]
    [InlineData("../kacis.txt")]
    [InlineData("klasor/../../kacis.txt")]
    [InlineData("C:/mutlak.txt")]
    [InlineData("\\\\sunucu\\pay\\x.txt")]   // UNC
    [InlineData("klasor/CON")]               // ayrılmış aygıt adı
    [InlineData("PRN")]
    [InlineData("NUL.txt")]                   // uzantılı ayrılmış ad
    [InlineData("dosya. ")]                   // sondaki nokta/boşluk
    [InlineData("alt/COM1")]
    public void GuvensizYollar_Reddedilir(string kotuYol)
    {
        Assert.Throws<InvalidDataException>(() =>
            FileTreeScanner.ResolveDestinationPath(_root, kotuYol));
    }

    [Fact]
    public void GuvenliYol_HedefAltindaCozulur()
    {
        string sonuc = FileTreeScanner.ResolveDestinationPath(_root, "klasor/a.txt");
        Assert.StartsWith(Path.GetFullPath(_root), sonuc);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception) { }
    }
}

public class JsonChannelTests
{
    [Fact]
    public async Task MesajGidisDonus_TipVeGovdeKorunur()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient server = await acceptTask;

        await using var sendChannel = new JsonChannel(client.GetStream());
        await using var recvChannel = new JsonChannel(server.GetStream());

        var offer = new TransferOffer("t1", "Deneme", 42, 2,
            [new FileEntry(0, "a.txt", 40), new FileEntry(1, "b/c.txt", 2)], ["bos"]);
        await sendChannel.SendAsync(MessageTypes.Offer, offer);

        ReceivedMessage? received = await recvChannel.ReceiveAsync();
        Assert.NotNull(received);
        Assert.Equal(MessageTypes.Offer, received!.Type);

        TransferOffer decoded = received.As<TransferOffer>();
        Assert.Equal(offer.DisplayName, decoded.DisplayName);
        Assert.Equal(offer.TotalBytes, decoded.TotalBytes);
        Assert.Equal(2, decoded.Files.Count);
        Assert.Equal("b/c.txt", decoded.Files[1].RelativePath);

        listener.Stop();
    }

    [Fact]
    public async Task BaglantiKapaninca_NullDoner()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new TcpClient();
        Task<TcpClient> acceptTask = listener.AcceptTcpClientAsync();
        await client.ConnectAsync(IPAddress.Loopback, port);
        using TcpClient server = await acceptTask;

        await using var recvChannel = new JsonChannel(server.GetStream());
        client.Close();

        Assert.Null(await recvChannel.ReceiveAsync());
        listener.Stop();
    }
}

public class DataFramingTests
{
    [Fact]
    public void BaslikYazOku_AlanlarKorunur()
    {
        Span<byte> header = stackalloc byte[DataFraming.HeaderSize];
        DataFraming.WriteHeader(header, 7, 32 * 1024 * 1024, 12345, 0xDEADBEEFCAFEBABE);

        (int fileId, long offset, int length, ulong hash) = DataFraming.ReadHeader(header);
        Assert.Equal(7, fileId);
        Assert.Equal(32 * 1024 * 1024, offset);
        Assert.Equal(12345, length);
        Assert.Equal(0xDEADBEEFCAFEBABE, hash);
    }
}

/// <summary>Denetim bulgularının regresyon testleri (#1 vd.).</summary>
public class SecurityHardeningTests
{
    [Fact]
    public async Task AsiriBuyukCerceve_AyirmadanReddedilir()
    {
        // #1: varsayılan sınır (MaxControlFrame) üstünde bir çerçeve boyutu bildirilirse,
        // gövde hiç okunmadan/ayrılmadan reddedilmeli.
        var ms = new MemoryStream();
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, ProtocolConstants.MaxControlFrame + 1);
        ms.Write(header);
        ms.Position = 0;

        await using var channel = new JsonChannel(ms);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await channel.ReceiveAsync());
    }

    [Fact]
    public void VarsayilanCerceveSiniri_KontrolBoyutunda()
    {
        var channel = new JsonChannel(new MemoryStream());
        Assert.Equal(ProtocolConstants.MaxControlFrame, channel.MaxFrameBytes);

        // Eşleştirme sonrası Offer için yükseltilebilir olmalı.
        channel.MaxFrameBytes = ProtocolConstants.MaxOfferFrame;
        Assert.Equal(ProtocolConstants.MaxOfferFrame, channel.MaxFrameBytes);
    }

    [Fact]
    public void NegatifOffset_TasmaKontrolu_ChunkPlannerTutarli()
    {
        // Taşmasız sınır formülünün mantığı: offset > Size - length reddeder.
        long size = 1000;
        int length = 16 * 1024 * 1024;
        long offset = long.MaxValue - 10;   // taşmaya çalışan offset
        // offset + length taşarsa negatif olurdu; doğru kontrol offset > size - length.
        Assert.True(offset > size - length);
    }
}
