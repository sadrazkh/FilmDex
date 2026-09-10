using System.Diagnostics;

namespace FilmDex.Core;

/// <summary>How far a scan has got. Reported at most a few times a second.</summary>
public sealed record ScanProgress(int FilesSeen, int EntriesAccepted, string CurrentDirectory);

/// <summary>What a scan found.</summary>
/// <param name="SkippedDirectories">
/// Folders Windows would not let us read. Shown to the owner, because a silent partial scan is
/// exactly how a duplicate slips through.
/// </param>
public sealed record ScanResult(
    IReadOnlyList<ScannedEntry> Entries,
    int FilesSeen,
    int SkippedDirectories);

/// <summary>
/// Reads a folder tree once and hands back the names worth remembering.
/// </summary>
public sealed class FolderScanner
{
    /// <summary>A file smaller than this whose name says "sample" or "trailer" is not a film.</summary>
    public const long DefaultSampleSizeLimitBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> IgnoredFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bdmv", "stream", "video_ts", "audio_ts", "certificate", "playlist", "clipinf", "backup",
        "subs", "subtitle", "subtitles", "sample", "samples", "extras", "featurettes", "trailer",
        "trailers", "screens", "proof", "$recycle.bin", "system volume information"
    };

    private static readonly string[] SampleMarkers = ["sample", "trailer"];

    private readonly long _sampleSizeLimitBytes;

    public FolderScanner(long sampleSizeLimitBytes = DefaultSampleSizeLimitBytes) =>
        _sampleSizeLimitBytes = sampleSizeLimitBytes;

    public Task<ScanResult> ScanAsync(
        string root,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Folder not found: {root}");

        return Task.Run(() => Scan(root, progress, cancellationToken), cancellationToken);
    }

    private ScanResult Scan(string root, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var files = new List<ScannedEntry>();
        var folderPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        var filesSeen = 0;
        var skippedDirectories = 0;
        var clock = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        pending.Push(rootFull);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directory = pending.Pop();

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    // Junctions and symlinks can point back up the tree; following them never ends.
                    if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint)) continue;

                    pending.Push(child);
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                skippedDirectories++;
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    filesSeen++;

                    var info = new FileInfo(file);
                    if (!IsFilm(info)) continue;

                    files.Add(new ScannedEntry(
                        EntryKind.File,
                        info.Name,
                        info.Extension,
                        Path.GetRelativePath(rootFull, info.FullName),
                        info.Length,
                        info.LastWriteTimeUtc));

                    RecordAncestors(rootFull, info.DirectoryName, folderPaths);
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                skippedDirectories++;
            }

            if (progress is not null && clock.Elapsed - lastReport > TimeSpan.FromMilliseconds(250))
            {
                lastReport = clock.Elapsed;
                progress.Report(new ScanProgress(filesSeen, files.Count, directory));
            }
        }

        var entries = new List<ScannedEntry>(files);

        foreach (var folder in folderPaths)
        {
            var info = new DirectoryInfo(folder);

            entries.Add(new ScannedEntry(
                EntryKind.Folder,
                info.Name,
                null,
                Path.GetRelativePath(rootFull, info.FullName),
                null,
                SafeLastWrite(info)));
        }

        progress?.Report(new ScanProgress(filesSeen, entries.Count, rootFull));

        return new ScanResult(entries, filesSeen, skippedDirectories);
    }

    private bool IsFilm(FileInfo file)
    {
        if (!MediaExtensions.IsVideo(file.Extension)) return false;

        if (file.Length < _sampleSizeLimitBytes &&
            SampleMarkers.Any(marker => file.Name.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Every folder between the film and the root is worth remembering, because a film is as often
    /// named by its folder as by its file. Disc structure and extras folders are not.
    /// </summary>
    private static void RecordAncestors(string rootFull, string? directory, HashSet<string> into)
    {
        var current = directory;

        while (!string.IsNullOrEmpty(current) &&
               !current.TrimEnd(Path.DirectorySeparatorChar).Equals(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar));
            if (name.Length == 0) break;

            if (!IgnoredFolderNames.Contains(name)) into.Add(current);

            current = Path.GetDirectoryName(current);
        }
    }

    private static DateTime? SafeLastWrite(DirectoryInfo info)
    {
        try
        {
            return info.LastWriteTimeUtc;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
