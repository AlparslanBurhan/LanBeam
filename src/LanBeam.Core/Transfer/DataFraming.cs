using System.Buffers.Binary;
using System.IO.Hashing;

namespace LanBeam.Core.Transfer;

/// <summary>
/// Veri kanalı ikili çerçevesi:
/// [i32 fileId][i64 offset][i32 length][u64 xxHash3] + payload.
/// fileId = -1 → akış sonu işareti (payload yok).
/// </summary>
public static class DataFraming
{
    public const int HeaderSize = 4 + 8 + 4 + 8;

    public static void WriteHeader(Span<byte> header, int fileId, long offset, int length, ulong hash)
    {
        BinaryPrimitives.WriteInt32LittleEndian(header, fileId);
        BinaryPrimitives.WriteInt64LittleEndian(header[4..], offset);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], length);
        BinaryPrimitives.WriteUInt64LittleEndian(header[16..], hash);
    }

    public static (int FileId, long Offset, int Length, ulong Hash) ReadHeader(ReadOnlySpan<byte> header) =>
        (BinaryPrimitives.ReadInt32LittleEndian(header),
         BinaryPrimitives.ReadInt64LittleEndian(header[4..]),
         BinaryPrimitives.ReadInt32LittleEndian(header[12..]),
         BinaryPrimitives.ReadUInt64LittleEndian(header[16..]));

    public static ulong Hash(ReadOnlySpan<byte> payload) => XxHash3.HashToUInt64(payload);

    public static async Task SendChunkAsync(Stream stream, int fileId, long offset,
        ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        byte[] header = new byte[HeaderSize];
        WriteHeader(header, fileId, offset, payload.Length, Hash(payload.Span));
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
    }

    public static async Task SendEndOfStreamAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[HeaderSize];
        WriteHeader(header, Protocol.ProtocolConstants.EndOfStreamFileId, 0, 0, 0);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
