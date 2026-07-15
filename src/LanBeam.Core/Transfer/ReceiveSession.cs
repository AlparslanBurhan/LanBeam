using System.Buffers;
using System.Collections.Concurrent;
using LanBeam.Core.Protocol;
using Microsoft.Win32.SafeHandles;

namespace LanBeam.Core.Transfer;

/// <summary>
/// Alıcı tarafında aktif bir transfer oturumu. Paralel veri akışlarından gelen parçaları
/// doğrular, dosyalara offset bazlı yazar, tamamlanma ve resume durumunu izler.
/// </summary>
public sealed class ReceiveSession : IDisposable
{
    private sealed class FileSlot
    {
        public required FileEntry Entry { get; init; }
        public required string LocalPath { get; init; }
        public required int TotalChunks { get; init; }
        /// <summary>Tamamlanmış parça indeksi → doğrulanmış xxHash3.</summary>
        public readonly Dictionary<int, ulong> Done = [];
        public readonly object Gate = new();
        public SafeFileHandle? Handle;
        public bool Counted;
    }

    private readonly ConcurrentDictionary<int, FileSlot> _slots = new();
    private readonly TransferOffer _offer;
    private readonly List<ChunkRef> _pendingResend = [];
    private readonly object _resendGate = new();
    private readonly TaskCompletionSource<string?> _outcome =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _remainingFiles;
    private int _sentinelsSeen;
    private int _expectedSentinels;
    private int _finalized;

    public string SessionId { get; } = Guid.NewGuid().ToString("N");
    public string DataToken { get; } = Guid.NewGuid().ToString("N");
    public string DestinationRoot { get; }
    public string SenderFingerprint { get; }
    public int StreamCount { get; }
    public TransferHandle Handle { get; }

    /// <summary>null = başarı, aksi halde hata nedeni.</summary>
    public Task<string?> Completion => _outcome.Task;

    /// <summary>Eksik/bozuk parçaların gönderenden yeniden istenmesi gerekiyor.</summary>
    public event Action<List<ChunkRef>>? ResendNeeded;

    public ReceiveSession(TransferOffer offer, string destinationRoot, string senderFingerprint,
        int streamCount, TransferHandle handle)
    {
        _offer = offer;
        DestinationRoot = destinationRoot;
        SenderFingerprint = senderFingerprint;
        StreamCount = streamCount;
        Handle = handle;
        _expectedSentinels = streamCount;

        Directory.CreateDirectory(destinationRoot);
        foreach (string dir in offer.EmptyDirectories)
            Directory.CreateDirectory(FileTreeScanner.ResolveDestinationPath(destinationRoot, dir));

        PartialState? resume = PartialState.TryLoad(destinationRoot, PartialState.ComputeSignature(offer.Files));
        long resumedBytes = 0;

        foreach (FileEntry file in offer.Files)
        {
            var slot = new FileSlot
            {
                Entry = file,
                LocalPath = FileTreeScanner.ResolveDestinationPath(destinationRoot, file.RelativePath),
                TotalChunks = ChunkPlanner.TotalChunkCount(file.Size),
            };

            if (resume is not null &&
                resume.CompletedChunkHashesByPath.TryGetValue(file.RelativePath, out var doneHashes) &&
                File.Exists(slot.LocalPath))
            {
                // Diskteki her "tamamlanmış" parçayı hash'iyle yeniden doğrula; bozuk/kurcalanmış
                // olanı atla (yeniden indirilir) — sessiz bozulmayı engeller.
                foreach ((int index, ulong expectedHash) in doneHashes)
                {
                    if (index < 0 || index >= slot.TotalChunks) continue;
                    if (VerifyChunkOnDisk(slot, index, expectedHash) && slot.Done.TryAdd(index, expectedHash))
                        resumedBytes += ChunkLength(file.Size, index);
                }
            }

            if (slot.TotalChunks > 0 && slot.Done.Count == slot.TotalChunks)
                slot.Counted = true;

            _slots[file.Id] = slot;
        }

        _remainingFiles = _slots.Values.Count(s => s.TotalChunks > 0 && !s.Counted);
        Handle.SetBytes(resumedBytes);
        Handle.TotalBytes = offer.TotalBytes;
        Handle.FileCount = offer.FileCount;
    }

    private static long ChunkLength(long fileSize, int chunkIndex)
    {
        long offset = (long)chunkIndex * ProtocolConstants.ChunkSize;
        return Math.Min(ProtocolConstants.ChunkSize, fileSize - offset);
    }

    /// <summary>Diskteki bir parçayı okuyup xxHash3'ünü beklenen değerle karşılaştırır.</summary>
    private static bool VerifyChunkOnDisk(FileSlot slot, int index, ulong expectedHash)
    {
        try
        {
            long offset = (long)index * ProtocolConstants.ChunkSize;
            int len = (int)ChunkLength(slot.Entry.Size, index);
            using SafeFileHandle h = File.OpenHandle(slot.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (RandomAccess.GetLength(h) < offset + len)
                return false;

            byte[] buf = ArrayPool<byte>.Shared.Rent(len);
            try
            {
                int total = 0;
                while (total < len)
                {
                    int r = RandomAccess.Read(h, buf.AsSpan(total, len - total), offset + total);
                    if (r == 0) return false;
                    total += r;
                }
                return DataFraming.Hash(buf.AsSpan(0, len)) == expectedHash;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buf);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Accept mesajına konacak resume bilgisi: fileId → tamamlanmış chunk indeksleri.</summary>
    public Dictionary<int, int[]> GetResumeCompletedChunks()
    {
        var result = new Dictionary<int, int[]>();
        foreach ((int id, FileSlot slot) in _slots)
        {
            lock (slot.Gate)
            {
                if (slot.Done.Count > 0)
                    result[id] = slot.Done.Keys.ToArray();
            }
        }
        return result;
    }

    /// <summary>Bir veri bağlantısını sonuna kadar tüketir. Her akış için ayrı çağrılır.</summary>
    public async Task RunDataStreamAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[DataFraming.HeaderSize];

        while (!ct.IsCancellationRequested && !_outcome.Task.IsCompleted)
        {
            try
            {
                await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                return; // gönderen akışı kapattı
            }

            (int fileId, long offset, int length, ulong hash) = DataFraming.ReadHeader(header);

            if (fileId == ProtocolConstants.EndOfStreamFileId)
            {
                OnSentinel();
                continue;
            }

            // Taşmasız sınır kontrolü: offset + length yerine offset > Size - length kullan.
            if (!_slots.TryGetValue(fileId, out FileSlot? slot) ||
                length <= 0 || length > ProtocolConstants.ChunkSize ||
                offset < 0 || offset % ProtocolConstants.ChunkSize != 0 ||
                offset > slot!.Entry.Size - length)
            {
                throw new InvalidDataException($"Geçersiz parça başlığı (fileId={fileId}, offset={offset}, len={length}).");
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                await stream.ReadExactlyAsync(buffer.AsMemory(0, length), ct).ConfigureAwait(false);

                if (DataFraming.Hash(buffer.AsSpan(0, length)) != hash)
                {
                    // TLS altında pratikte olmaz; disk/bellek hatasına karşı emniyet.
                    QueueResend(new ChunkRef(fileId, offset, length));
                    continue;
                }

                WriteChunk(slot, offset, buffer.AsSpan(0, length), hash);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private void WriteChunk(FileSlot slot, long offset, ReadOnlySpan<byte> payload, ulong hash)
    {
        int chunkIndex = (int)(offset / ProtocolConstants.ChunkSize);

        SafeFileHandle handle = EnsureHandle(slot);
        RandomAccess.Write(handle, payload, offset);

        bool fileCompleted = false;
        lock (slot.Gate)
        {
            if (!slot.Done.TryAdd(chunkIndex, hash))
                return; // aynı parça iki kez geldi; sayma
            if (!slot.Counted && slot.Done.Count == slot.TotalChunks)
            {
                slot.Counted = true;
                fileCompleted = true;
            }
        }

        Handle.AddBytes(payload.Length);

        if (fileCompleted)
        {
            CloseSlotHandle(slot);
            if (Interlocked.Decrement(ref _remainingFiles) == 0)
                FinalizeSuccess();
        }
    }

    private SafeFileHandle EnsureHandle(FileSlot slot)
    {
        if (slot.Handle is { } existing) return existing;
        lock (slot.Gate)
        {
            if (slot.Handle is { } h) return h;
            Directory.CreateDirectory(Path.GetDirectoryName(slot.LocalPath)!);
            // OpenOrCreate: resume'da mevcut kısmî dosya korunur. Ön tahsis parçalanmayı azaltır.
            SafeFileHandle handle = File.OpenHandle(slot.LocalPath, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, FileOptions.None);
            if (RandomAccess.GetLength(handle) != slot.Entry.Size)
                RandomAccess.SetLength(handle, slot.Entry.Size);
            slot.Handle = handle;
            return handle;
        }
    }

    private void CloseSlotHandle(FileSlot slot)
    {
        lock (slot.Gate)
        {
            slot.Handle?.Dispose();
            slot.Handle = null;
        }
    }

    private void OnSentinel()
    {
        if (Interlocked.Increment(ref _sentinelsSeen) < Volatile.Read(ref _expectedSentinels))
            return;

        // Dalga bitti: gönderen elindekileri yolladı. Eksik kaldıysa yeniden iste.
        if (_outcome.Task.IsCompleted)
            return;

        List<ChunkRef> missing = ComputeMissingChunks();
        if (missing.Count == 0)
        {
            // Tamamlanma normalde son parçayla tetiklenir; sıfır dosyalı/boş transferlerde buradan.
            FinalizeSuccess();
            return;
        }

        // Sonraki dalga tek akıştan gelir ve tek sentinel ile biter.
        Volatile.Write(ref _expectedSentinels, 1);
        Interlocked.Exchange(ref _sentinelsSeen, 0);

        lock (_resendGate)
        {
            missing.AddRange(_pendingResend);
            _pendingResend.Clear();
        }
        ResendNeeded?.Invoke(missing.Distinct().ToList());
    }

    private void QueueResend(ChunkRef chunk)
    {
        lock (_resendGate) _pendingResend.Add(chunk);
    }

    private List<ChunkRef> ComputeMissingChunks()
    {
        var missing = new List<ChunkRef>();
        foreach (FileSlot slot in _slots.Values)
        {
            if (slot.TotalChunks == 0) continue;
            lock (slot.Gate)
            {
                if (slot.Done.Count == slot.TotalChunks) continue;
                for (int i = 0; i < slot.TotalChunks; i++)
                {
                    if (!slot.Done.ContainsKey(i))
                        missing.Add(new ChunkRef(slot.Entry.Id,
                            (long)i * ProtocolConstants.ChunkSize,
                            (int)ChunkLength(slot.Entry.Size, i)));
                }
            }
        }
        return missing;
    }

    private void FinalizeSuccess()
    {
        // Tek girişli: son parça ile OnSentinel aynı anda tetikleyebilir; yalnızca ilki çalışsın.
        if (Interlocked.Exchange(ref _finalized, 1) != 0)
            return;

        try
        {
            // Sıfır boyutlu dosyaları oluştur.
            foreach (FileSlot slot in _slots.Values.Where(s => s.Entry.Size == 0))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(slot.LocalPath)!);
                if (!File.Exists(slot.LocalPath))
                    File.WriteAllBytes(slot.LocalPath, []);
            }

            PartialState.Delete(DestinationRoot);
            _outcome.TrySetResult(null);
        }
        catch (Exception ex)
        {
            _outcome.TrySetResult($"Tamamlama hatası: {ex.Message}");
        }
    }

    public void Fail(string reason)
    {
        SavePartial();
        _outcome.TrySetResult(reason);
    }

    /// <summary>Resume için mevcut durumu diske yazar (periyodik ve iptal/hata anında).</summary>
    public void SavePartial()
    {
        // Başarıyla biten oturum partial dosyayı silmiştir; yarış sonucu yeniden yazma.
        if (_outcome.Task is { IsCompleted: true, Result: null })
            return;

        try
        {
            var state = new PartialState { Signature = PartialState.ComputeSignature(_offer.Files) };
            foreach (FileSlot slot in _slots.Values)
            {
                lock (slot.Gate)
                {
                    if (slot.Done.Count > 0)
                        state.CompletedChunkHashesByPath[slot.Entry.RelativePath] =
                            new Dictionary<int, ulong>(slot.Done);
                }
            }
            if (state.CompletedChunkHashesByPath.Count > 0)
                state.Save(DestinationRoot);
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        foreach (FileSlot slot in _slots.Values)
            CloseSlotHandle(slot);
    }
}
