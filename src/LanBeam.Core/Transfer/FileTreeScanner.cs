using LanBeam.Core.Protocol;

namespace LanBeam.Core.Transfer;

/// <summary>Seçilen dosya/klasörlerden transfer metadata'sı ve yerel yol eşlemesi üretir.</summary>
public sealed class ScannedTree
{
    public required string DisplayName { get; init; }
    public required List<FileEntry> Files { get; init; }
    public required List<string> EmptyDirectories { get; init; }
    public required Dictionary<int, string> LocalPathsByFileId { get; init; }
    public long TotalBytes => Files.Sum(f => f.Size);
}

public static class FileTreeScanner
{
    /// <summary>
    /// Yollar dosya ya da klasör olabilir. Klasörler içeriğiyle birlikte, klasör adı kök olacak
    /// şekilde eklenir. Göreli yollar '/' ayraçlıdır (platformdan bağımsız).
    /// </summary>
    public static ScannedTree Scan(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new ArgumentException("En az bir yol gerekli.", nameof(paths));

        var files = new List<FileEntry>();
        var emptyDirs = new List<string>();
        var localPaths = new Dictionary<int, string>();
        int nextId = 0;

        foreach (string raw in paths)
        {
            string path = Path.GetFullPath(raw);

            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                files.Add(new FileEntry(nextId, info.Name, info.Length));
                localPaths[nextId] = path;
                nextId++;
            }
            else if (Directory.Exists(path))
            {
                string rootName = new DirectoryInfo(path).Name;
                ScanDirectory(path, rootName, files, emptyDirs, localPaths, ref nextId);
            }
            else
            {
                throw new FileNotFoundException("Yol bulunamadı.", path);
            }
        }

        string displayName = Path.GetFileName(Path.TrimEndingDirectorySeparator(paths[0]));
        if (paths.Count > 1)
            displayName += $" (+{paths.Count - 1})";

        return new ScannedTree
        {
            DisplayName = displayName,
            Files = files,
            EmptyDirectories = emptyDirs,
            LocalPathsByFileId = localPaths,
        };
    }

    private static void ScanDirectory(string dir, string relativePrefix, List<FileEntry> files,
        List<string> emptyDirs, Dictionary<int, string> localPaths, ref int nextId)
    {
        bool any = false;

        foreach (string file in Directory.EnumerateFiles(dir))
        {
            any = true;
            var info = new FileInfo(file);
            files.Add(new FileEntry(nextId, $"{relativePrefix}/{info.Name}", info.Length));
            localPaths[nextId] = file;
            nextId++;
        }

        foreach (string sub in Directory.EnumerateDirectories(dir))
        {
            any = true;
            ScanDirectory(sub, $"{relativePrefix}/{new DirectoryInfo(sub).Name}", files, emptyDirs,
                localPaths, ref nextId);
        }

        if (!any)
            emptyDirs.Add(relativePrefix);
    }

    /// <summary>
    /// Göreli yolu hedef klasör altına güvenle çevirir. Yol kaçışlarını (.., mutlak yol, sürücü)
    /// reddeder — kötü niyetli gönderenin hedef klasör dışına yazmasını engeller.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string ResolveDestinationPath(string destinationRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidDataException("Boş göreli yol.");
        if (relativePath.Contains("..") || Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
            throw new InvalidDataException($"Güvensiz göreli yol reddedildi: {relativePath}");

        // Her yol bileşenini ayrı ayrı doğrula: ayrılmış Windows aygıt adları (CON, PRN…),
        // sondaki nokta/boşluk ve geçersiz karakterler yazımı beklenmedik yere yönlendirebilir.
        foreach (string segment in relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = segment.TrimEnd('.', ' ');
            if (trimmed.Length == 0 || trimmed != segment)
                throw new InvalidDataException($"Güvensiz yol bileşeni reddedildi: {segment}");

            string baseName = Path.GetFileNameWithoutExtension(segment);
            if (ReservedNames.Contains(baseName) || ReservedNames.Contains(segment))
                throw new InvalidDataException($"Ayrılmış aygıt adı reddedildi: {segment}");

            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException($"Geçersiz karakter içeren yol bileşeni: {segment}");
        }

        string full = Path.GetFullPath(Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string rootFull = Path.GetFullPath(destinationRoot + Path.DirectorySeparatorChar);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Hedef klasör dışına çıkan yol reddedildi: {relativePath}");

        return full;
    }
}
