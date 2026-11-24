using System.IO;
using System.Diagnostics;
using System.Buffers;
using System.Security.Cryptography;

namespace ImgFind;

public sealed record ScanResult(IReadOnlyList<string> Images, IReadOnlyList<string> Errors);
public sealed record ScanProgress(string CurrentDirectory, int DirectoriesVisited, long FilesExamined,
    int ImagesFound, int Errors);
public sealed record PrepareProgress(string Phase, string CurrentPath, int Completed, int? Total,
    long BytesRead = 0, long CurrentSize = 0);
public sealed record FileFingerprint(string Path, long Size, string Hash);
public sealed record DuplicateGroup(IReadOnlyList<string> Sources, IReadOnlyList<string> ExistingFiles);
public sealed record MovePlan(string Destination, IReadOnlyList<FileFingerprint> Sources,
    IReadOnlyList<DuplicateGroup> Duplicates);
public sealed record MovedFile(string Source, string Destination);
public sealed record MoveResult(IReadOnlyList<MovedFile> Moved, IReadOnlyList<string> Skipped,
    IReadOnlyList<string> Errors);

public static class ImageOperations
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".ico"
    };

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public static ScanResult Scan(string root, Action<ScanProgress>? reportProgress = null)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"A pasta não existe: {root}");

        var images = new List<string>();
        var errors = new List<string>();
        var pending = new Stack<string>();
        var directoriesVisited = 0;
        long filesExamined = 0;
        var progressClock = Stopwatch.StartNew();
        void Report(string directory)
        {
            reportProgress?.Invoke(new ScanProgress(directory, directoriesVisited, filesExamined, images.Count, errors.Count));
            progressClock.Restart();
        }
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            directoriesVisited++;
            Report(directory);
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                            pending.Push(entry);
                        else
                        {
                            filesExamined++;
                            if (Extensions.Contains(Path.GetExtension(entry)))
                                images.Add(Path.GetFullPath(entry));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{entry}: {ex.Message}");
                    }
                    if (progressClock.ElapsedMilliseconds >= 150)
                        Report(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{directory}: {ex.Message}");
            }
            Report(directory);
        }
        images.Sort(PathComparer);
        return new ScanResult(images, errors);
    }

    private static FileFingerprint Fingerprint(string path, Action<long, long>? onRead = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.SequentialScan);
        var size = stream.Length;
        using var algorithm = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            long bytesRead = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                algorithm.AppendData(buffer, 0, read);
                bytesRead += read;
                onRead?.Invoke(bytesRead, size);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        var hash = Convert.ToHexString(algorithm.GetHashAndReset());
        return new FileFingerprint(path, size, hash);
    }

    public static MovePlan Prepare(IEnumerable<string> paths, string destination, Action<PrepareProgress>? reportProgress = null)
    {
        destination = Path.GetFullPath(destination);
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException($"A pasta de destino não existe: {destination}");

        var sourcePathsList = paths.Select(Path.GetFullPath).Distinct(PathComparer).ToList();
        var sourcePaths = sourcePathsList.ToHashSet(PathComparer);
        var clock = Stopwatch.StartNew();
        void Report(PrepareProgress progress, bool force = false)
        {
            if (reportProgress == null || (!force && clock.ElapsedMilliseconds < 150)) return;
            clock.Restart();
            reportProgress(progress);
        }

        var sources = new List<FileFingerprint>(sourcePathsList.Count);
        Report(new PrepareProgress("origem", "", 0, sourcePathsList.Count), true);
        foreach (var path in sourcePathsList)
        {
            sources.Add(Fingerprint(path, (bytes, size) =>
                Report(new PrepareProgress("origem", path, sources.Count, sourcePathsList.Count, bytes, size))));
            Report(new PrepareProgress("origem", path, sources.Count, sourcePathsList.Count));
        }
        Report(new PrepareProgress("origem", "", sources.Count, sourcePathsList.Count), true);
        var sizes = sources.Select(x => x.Size).ToHashSet();
        var candidates = new List<string>();
        var examined = 0;
        Report(new PrepareProgress("listar destino", destination, 0, null), true);
        foreach (var path in Directory.EnumerateFiles(destination))
        {
            examined++;
            var fullPath = Path.GetFullPath(path);
            if (sourcePaths.Contains(fullPath))
                continue;
            var info = new FileInfo(fullPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0 && sizes.Contains(info.Length))
                candidates.Add(fullPath);
            Report(new PrepareProgress("listar destino", fullPath, examined, null));
        }

        var existing = new List<FileFingerprint>(candidates.Count);
        Report(new PrepareProgress("destino", "", 0, candidates.Count), true);
        foreach (var path in candidates)
        {
            existing.Add(Fingerprint(path, (bytes, size) =>
                Report(new PrepareProgress("destino", path, existing.Count, candidates.Count, bytes, size))));
            Report(new PrepareProgress("destino", path, existing.Count, candidates.Count));
        }
        Report(new PrepareProgress("destino", "", existing.Count, candidates.Count), true);
        Report(new PrepareProgress("agrupar", "", 0, null), true);
        var existingByContent = existing.ToLookup(x => (x.Size, x.Hash));
        var duplicates = sources.GroupBy(x => (x.Size, x.Hash))
            .Select(group => new DuplicateGroup(
                group.Select(x => x.Path).ToList(),
                existingByContent[group.Key].Select(x => x.Path).ToList()))
            .Where(group => group.Sources.Count > 1 || group.ExistingFiles.Count > 0)
            .ToList();
        return new MovePlan(destination, sources, duplicates);
    }

    public static MoveResult Move(MovePlan plan, bool excludeDuplicates)
    {
        var moved = new List<MovedFile>();
        var skipped = new List<string>();
        var errors = new List<string>();
        var excluded = new HashSet<string>(PathComparer);
        if (excludeDuplicates)
        {
            foreach (var group in plan.Duplicates)
            {
                // A file already in the destination is the retained copy.
                var retainedAtDestination = group.ExistingFiles.Count > 0 ||
                    group.Sources.Any(x => IsInDestination(x, plan.Destination));
                foreach (var path in group.Sources.Skip(retainedAtDestination ? 0 : 1))
                    excluded.Add(path);
            }
        }

        foreach (var source in plan.Sources)
        {
            if (excluded.Contains(source.Path) || IsInDestination(source.Path, plan.Destination))
            {
                skipped.Add(source.Path);
                continue;
            }
            try
            {
                var current = Fingerprint(source.Path);
                if (current.Size != source.Size || current.Hash != source.Hash)
                    throw new IOException("O arquivo foi alterado após a conferência; não foi movido.");

                var name = Path.GetFileNameWithoutExtension(source.Path);
                var extension = Path.GetExtension(source.Path);
                for (var suffix = 0; ; suffix++)
                {
                    var fileName = suffix == 0 ? name + extension : $"{name} ({suffix}){extension}";
                    var target = Path.Combine(plan.Destination, fileName);
                    try
                    {
                        File.Move(source.Path, target, false);
                        moved.Add(new MovedFile(source.Path, target));
                        break;
                    }
                    catch (IOException) when (File.Exists(target) || Directory.Exists(target))
                    {
                        // Another file may have appeared since the previous attempt.
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or ArgumentException or NotSupportedException)
            {
                errors.Add($"{source.Path}: {ex.Message}");
            }
        }
        return new MoveResult(moved, skipped, errors);
    }

    private static bool IsInDestination(string path, string destination) =>
        PathComparer.Equals(Path.GetDirectoryName(Path.GetFullPath(path)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)));
}
