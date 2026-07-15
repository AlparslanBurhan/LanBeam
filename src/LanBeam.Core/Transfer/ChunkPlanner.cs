using LanBeam.Core.Protocol;

namespace LanBeam.Core.Transfer;

/// <summary>Gönderilecek tek bir parça (dosyanın bir bölümü ya da tamamı).</summary>
public readonly record struct ChunkWork(int FileId, long Offset, int Length)
{
    public int ChunkIndex => (int)(Offset / ProtocolConstants.ChunkSize);
}

public static class ChunkPlanner
{
    /// <summary>
    /// Dosyaları 16 MB'lık parçalara böler; tamamlanmış parçalar (resume) atlanır.
    /// Sıfır boyutlu dosyalar parça üretmez (alıcı tarafında finalize sırasında oluşturulur).
    /// </summary>
    public static List<ChunkWork> Plan(IEnumerable<FileEntry> files,
        IReadOnlyDictionary<int, HashSet<int>>? completedChunks = null)
    {
        var work = new List<ChunkWork>();
        foreach (FileEntry file in files)
        {
            if (file.Size == 0) continue;
            HashSet<int>? done = null;
            completedChunks?.TryGetValue(file.Id, out done);

            for (long offset = 0; offset < file.Size; offset += ProtocolConstants.ChunkSize)
            {
                int length = (int)Math.Min(ProtocolConstants.ChunkSize, file.Size - offset);
                int index = (int)(offset / ProtocolConstants.ChunkSize);
                if (done is not null && done.Contains(index)) continue;
                work.Add(new ChunkWork(file.Id, offset, length));
            }
        }
        return work;
    }

    public static int TotalChunkCount(long fileSize) =>
        fileSize == 0 ? 0 : (int)((fileSize + ProtocolConstants.ChunkSize - 1) / ProtocolConstants.ChunkSize);
}
