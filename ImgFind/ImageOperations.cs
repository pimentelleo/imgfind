using System.IO;
using System.Security.Cryptography;

namespace ImgFind;

public sealed record ScanResult(IReadOnlyList<string> Images, IReadOnlyList<string> Errors);
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

    public static ScanResult Scan(string root)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"A pasta não existe: {root}");

        var images = new List<string>();
        var errors = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
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
                        else if (Extensions.Contains(Path.GetExtension(entry)))
                            images.Add(Path.GetFullPath(entry));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        errors.Add($"{entry}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{directory}: {ex.Message}");
            }
        }
        images.Sort(PathComparer);
        return new ScanResult(images, errors);
    }

    private static FileFingerprint Fingerprint(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var size = stream.Length;
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return new FileFingerprint(path, size, hash);
    }

    public static MovePlan Prepare(IEnumerable<string> paths, string destination)
    {
        destination = Path.GetFullPath(destination);
        if (!Directory.Exists(destination))
            throw new DirectoryNotFoundException($"A pasta de destino não existe: {destination}");

        var sources = paths.Select(Path.GetFullPath).Distinct(PathComparer)
            .Select(Fingerprint).ToList();
        var sourcePaths = sources.Select(x => x.Path).ToHashSet(PathComparer);
        var sizes = sources.Select(x => x.Size).ToHashSet();
        var existing = new List<FileFingerprint>();
        foreach (var path in Directory.EnumerateFiles(destination))
        {
            var fullPath = Path.GetFullPath(path);
            if (sourcePaths.Contains(fullPath))
                continue;
            var info = new FileInfo(fullPath);
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0 && sizes.Contains(info.Length))
                existing.Add(Fingerprint(fullPath));
        }

        var duplicates = sources.GroupBy(x => (x.Size, x.Hash))
            .Select(group => new DuplicateGroup(
                group.Select(x => x.Path).ToList(),
                existing.Where(x => x.Size == group.Key.Size && x.Hash == group.Key.Hash)
                    .Select(x => x.Path).ToList()))
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
