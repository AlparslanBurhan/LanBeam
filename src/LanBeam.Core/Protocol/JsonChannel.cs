using System.Buffers.Binary;
using System.Text.Json;

namespace LanBeam.Core.Protocol;

/// <summary>
/// Uzunluk ön ekli (4 bayt little-endian) JSON mesaj kanalı.
/// Her mesaj bir zarftır: {"type":"...","body":{...}}.
/// Gönderim çok iş parçacığından güvenlidir; okuma tek okuyucu varsayar.
/// </summary>
public sealed class JsonChannel : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Stream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    /// <summary>
    /// Gelen tek bir çerçevenin izin verilen üst sınırı. Varsayılan olarak küçük
    /// (kimlik doğrulaması öncesi bellek-DoS koruması). Yalnızca eşleştirme sonrası,
    /// büyük Offer beklenirken <see cref="ProtocolConstants.MaxOfferFrame"/>'e yükseltilir.
    /// </summary>
    public int MaxFrameBytes { get; set; } = ProtocolConstants.MaxControlFrame;

    public JsonChannel(Stream stream) => _stream = stream;

    public async Task SendAsync<T>(string type, T body, CancellationToken ct = default)
    {
        var envelope = new Envelope<T>(type, body);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);

        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, ct).ConfigureAwait(false);
            await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public Task SendAsync(string type, CancellationToken ct = default) =>
        SendAsync<object?>(type, null, ct);

    /// <summary>Bir mesaj okur. Bağlantı kapandıysa null döner.</summary>
    public async Task<ReceivedMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        byte[] header = new byte[4];
        if (!await ReadExactOrEofAsync(header, ct).ConfigureAwait(false))
            return null;

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes)
            throw new InvalidDataException($"Geçersiz/aşırı büyük çerçeve boyutu: {length} (sınır {MaxFrameBytes}).");

        byte[] payload = new byte[length];
        await _stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(payload);
        string type = doc.RootElement.GetProperty("type").GetString()
                      ?? throw new InvalidDataException("Mesajda 'type' yok.");
        JsonElement body = doc.RootElement.TryGetProperty("body", out var b)
            ? b.Clone()
            : default;

        return new ReceivedMessage(type, body);
    }

    private async Task<bool> ReadExactOrEofAsync(Memory<byte> buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await _stream.ReadAsync(buffer[total..], ct).ConfigureAwait(false);
            if (read == 0)
                return total == 0 ? false : throw new EndOfStreamException();
            total += read;
        }
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    private sealed record Envelope<T>(string Type, T Body);
}

public sealed record ReceivedMessage(string Type, JsonElement Body)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public T As<T>() => Body.Deserialize<T>(JsonOptions)
        ?? throw new InvalidDataException($"'{Type}' mesaj gövdesi çözülemedi.");
}
