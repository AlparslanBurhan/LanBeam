using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace LanBeam.App.Services;

/// <summary>
/// Tek örnek koruması: ikinci kopya açılırsa argümanlarını named pipe üzerinden
/// çalışan örneğe iletip kapanır.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;

    public bool IsFirstInstance { get; }

    /// <summary>İkinci kopyadan gelen "gönder" istekleri (dosya yolları).</summary>
    public event Action<string[]>? SendRequested;

    public SingleInstance(string dataDirectory)
    {
        // Veri dizinine bağlı isim: --datadir ile açılan test kopyaları çakışmaz.
        string suffix = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(dataDirectory.ToLowerInvariant())))[..16];
        _pipeName = $"LanBeam_Pipe_{suffix}";
        _mutex = new Mutex(initiallyOwned: true, $"LanBeam_Mutex_{suffix}", out bool createdNew);
        IsFirstInstance = createdNew;
    }

    /// <summary>İlk örnekte pipe sunucusunu başlatır.</summary>
    public void StartServer()
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    using NamedPipeServerStream server = CreateSecuredServer();
                    await server.WaitForConnectionAsync(_cts.Token);

                    // Sınırlı okuma: kötü niyetli/yanlış bir istemcinin belleği doldurmasını engelle.
                    string? json = await ReadBoundedAsync(server, MaxRequestBytes, _cts.Token);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        // Boş dizi de geçerli: "beni öne getir" sinyali.
                        string[]? paths = JsonSerializer.Deserialize<string[]>(json);
                        if (paths is not null)
                            SendRequested?.Invoke(paths);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { }
            }
        });
    }

    private const int MaxRequestBytes = 64 * 1024;

    /// <summary>Yalnızca mevcut kullanıcıya erişim veren ACL ile pipe sunucusu oluşturur.</summary>
    private NamedPipeServerStream CreateSecuredServer()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
        }
        catch (Exception)
        {
            // ACL kurulamazsa varsayılan güvenlikle devam et (varsayılan DACL zaten
            // yalnızca oluşturan kullanıcı + yöneticilere izin verir).
            return new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }
    }

    private static async Task<string?> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        byte[] buffer = new byte[maxBytes];
        int total = 0;
        while (total < maxBytes)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        string text = Encoding.UTF8.GetString(buffer, 0, total);
        // İstemci UTF-8 BOM ekleyebilir; JSON ayrıştırıcı baştaki BOM'u kabul etmez, temizle.
        return text.TrimStart('﻿');
    }

    /// <summary>İkinci örnekten: yolları çalışan örneğe iletir. Başarısızsa false.</summary>
    public bool ForwardToRunningInstance(string[] sendPaths)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(3000);
            // BOM'suz UTF-8: sunucu tarafında JSON ayrıştırma sorununu önler.
            using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(JsonSerializer.Serialize(sendPaths));
            writer.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (Exception) { }
        }
        _mutex.Dispose();
    }
}
