using FilmDex.Core;

namespace FilmDex.Core.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"filmdex-scan-{Guid.NewGuid():N}");

    public FolderScannerTests()
    {
        Directory.CreateDirectory(_root);

        Write(@"The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv");
        Write(@"notes.txt");
        Write(@"Interstellar (2014)\Interstellar.2014.2160p.mkv");
        Write(@"Interstellar (2014)\Interstellar.srt");
        Write(@"Interstellar (2014)\Sample\sample.mkv");
        Write(@"Interstellar (2014)\Sample\sample.big.mkv", size: 2_000);
        Write(@"Inception\BDMV\STREAM\00000.m2ts");
        Write(@"Empty Folder\readme.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Write(string relativePath, int size = 10)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[size]);
    }

    private static FolderScanner Scanner() => new(sampleSizeLimitBytes: 1_024);

    private static string[] Names(ScanResult result, EntryKind kind) =>
        result.Entries.Where(e => e.Kind == kind).Select(e => e.RawName).OrderBy(n => n).ToArray();

    [Fact]
    public async Task Keeps_video_files_and_ignores_everything_else()
    {
        var result = await Scanner().ScanAsync(_root);

        var files = Names(result, EntryKind.File);
        Assert.Contains("The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv", files);
        Assert.Contains("Interstellar.2014.2160p.mkv", files);
        Assert.Contains("00000.m2ts", files);
        Assert.DoesNotContain("notes.txt", files);
        Assert.DoesNotContain("Interstellar.srt", files);
    }

    [Fact]
    public async Task Ignores_a_small_file_that_calls_itself_a_sample()
    {
        var result = await Scanner().ScanAsync(_root);

        Assert.DoesNotContain("sample.mkv", Names(result, EntryKind.File));
    }

    [Fact]
    public async Task Keeps_a_full_length_film_whose_name_happens_to_say_sample()
    {
        var result = await Scanner().ScanAsync(_root);

        Assert.Contains("sample.big.mkv", Names(result, EntryKind.File));
    }

    [Fact]
    public async Task Records_the_folders_that_hold_films()
    {
        var result = await Scanner().ScanAsync(_root);

        var folders = Names(result, EntryKind.Folder);
        Assert.Contains("Interstellar (2014)", folders);
        Assert.Contains("Inception", folders);
    }

    [Fact]
    public async Task Leaves_out_disc_structure_and_extras_folders()
    {
        var result = await Scanner().ScanAsync(_root);

        var folders = Names(result, EntryKind.Folder);
        Assert.DoesNotContain("BDMV", folders);
        Assert.DoesNotContain("STREAM", folders);
        Assert.DoesNotContain("Sample", folders);
    }

    [Fact]
    public async Task Leaves_out_a_folder_with_no_film_in_it()
    {
        var result = await Scanner().ScanAsync(_root);

        Assert.DoesNotContain("Empty Folder", Names(result, EntryKind.Folder));
    }

    [Fact]
    public async Task Leaves_out_the_root_itself()
    {
        var result = await Scanner().ScanAsync(_root);

        Assert.DoesNotContain(Path.GetFileName(_root), Names(result, EntryKind.Folder));
    }

    [Fact]
    public async Task Records_a_path_relative_to_the_root_and_a_file_size()
    {
        var result = await Scanner().ScanAsync(_root);

        var entry = result.Entries.Single(e => e.RawName == "Interstellar.2014.2160p.mkv");
        Assert.Equal(Path.Combine("Interstellar (2014)", "Interstellar.2014.2160p.mkv"), entry.RelativePath);
        Assert.Equal(".mkv", entry.Extension);
        Assert.Equal(10, entry.SizeBytes);
        Assert.NotNull(entry.ModifiedUtc);
    }

    [Fact]
    public async Task Counts_the_files_it_looked_at()
    {
        var result = await Scanner().ScanAsync(_root);

        Assert.Equal(8, result.FilesSeen);
    }

    [Fact]
    public async Task Reports_progress_while_it_works()
    {
        var reports = new List<ScanProgress>();
        var progress = new Progress<ScanProgress>(reports.Add);

        await Scanner().ScanAsync(_root, progress);

        // Progress<T> posts asynchronously; the final report is the one that must arrive.
        await Task.Delay(200);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task Refuses_a_root_that_is_not_there()
    {
        var missing = Path.Combine(_root, "no-such-folder");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => Scanner().ScanAsync(missing));
    }

    [Fact]
    public async Task Stops_when_it_is_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Scanner().ScanAsync(_root, progress: null, cancellation.Token));
    }
}
