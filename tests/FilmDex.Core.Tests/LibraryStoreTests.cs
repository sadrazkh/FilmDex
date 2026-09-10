using FilmDex.Core;
using Microsoft.Data.Sqlite;

namespace FilmDex.Core.Tests;

public class LibraryStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"filmdex-tests-{Guid.NewGuid():N}", "filmdex.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var folder = Path.GetDirectoryName(_databasePath)!;
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private LibraryStore OpenStore() => new(_databasePath);

    private static ScannedEntry FileEntry(string name) =>
        new(EntryKind.File, name, Path.GetExtension(name), $@"sub\{name}", 1_500_000_000, DateTime.UtcNow);

    [Fact]
    public void Adds_a_library_and_reads_it_back()
    {
        using var store = OpenStore();

        var added = store.AddLibrary("Movies", @"E:\Movies");

        var libraries = store.GetLibraries();
        Assert.Single(libraries);
        Assert.Equal("Movies", libraries[0].Label);
        Assert.Equal(@"E:\Movies", libraries[0].RootPath);
        Assert.Equal(added.Id, libraries[0].Id);
        Assert.Null(libraries[0].LastScanUtc);
    }

    [Fact]
    public void Finds_an_existing_library_by_its_root_path()
    {
        using var store = OpenStore();
        store.AddLibrary("Movies", @"E:\Movies");

        Assert.NotNull(store.FindByRoot(@"e:\movies"));
        Assert.Null(store.FindByRoot(@"E:\Series"));
    }

    [Fact]
    public void Stores_entries_with_their_parsed_title()
    {
        using var store = OpenStore();
        var library = store.AddLibrary("Movies", @"E:\Movies");

        store.ReplaceEntries(library.Id, [FileEntry("The.Matrix.1999.1080p.BluRay.mkv")], DateTime.UtcNow);

        var entries = store.LoadAllEntries();
        Assert.Single(entries);
        Assert.Equal("the matrix", entries[0].Parsed.CleanTitle);
        Assert.Equal(1999, entries[0].Parsed.Year);
        Assert.Equal("Movies", entries[0].LibraryLabel);
        Assert.Equal(EntryKind.File, entries[0].Kind);
    }

    [Fact]
    public void A_rescan_replaces_the_previous_snapshot()
    {
        using var store = OpenStore();
        var library = store.AddLibrary("Movies", @"E:\Movies");
        store.ReplaceEntries(library.Id, [FileEntry("Old.Film.2001.mkv")], DateTime.UtcNow);

        store.ReplaceEntries(library.Id, [FileEntry("New.Film.2002.mkv")], DateTime.UtcNow);

        var entries = store.LoadAllEntries();
        Assert.Single(entries);
        Assert.Equal("New.Film.2002.mkv", entries[0].RawName);
        Assert.Equal(1, store.GetLibraries()[0].EntryCount);
        Assert.NotNull(store.GetLibraries()[0].LastScanUtc);
    }

    [Fact]
    public void Removing_a_library_removes_its_entries()
    {
        using var store = OpenStore();
        var library = store.AddLibrary("Movies", @"E:\Movies");
        store.ReplaceEntries(library.Id, [FileEntry("The.Matrix.1999.mkv")], DateTime.UtcNow);

        store.RemoveLibrary(library.Id);

        Assert.Empty(store.GetLibraries());
        Assert.Empty(store.LoadAllEntries());
    }

    [Fact]
    public void Reparsing_recomputes_titles_without_reading_the_disk()
    {
        using (var store = OpenStore())
        {
            var library = store.AddLibrary("Movies", @"E:\Movies");
            store.ReplaceEntries(library.Id, [FileEntry("The.Matrix.1999.1080p.mkv")], DateTime.UtcNow);
        }

        // Stand in for a normaliser whose rules have since improved.
        using (var connection = new SqliteConnection($"Data Source={_databasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Entry SET CleanTitle = 'stale', Year = NULL";
            command.ExecuteNonQuery();
        }

        using var reopened = OpenStore();
        var updated = reparse(reopened);

        Assert.Equal(1, updated);
        Assert.Equal("the matrix", reopened.LoadAllEntries()[0].Parsed.CleanTitle);
        Assert.Equal(1999, reopened.LoadAllEntries()[0].Parsed.Year);

        static int reparse(LibraryStore store) => store.ReparseAll();
    }

    [Fact]
    public void Reopening_an_existing_database_keeps_everything()
    {
        long libraryId;

        using (var store = OpenStore())
        {
            libraryId = store.AddLibrary("Movies", @"E:\Movies").Id;
            store.ReplaceEntries(libraryId, [FileEntry("The.Matrix.1999.mkv")], DateTime.UtcNow);
        }

        using var reopened = OpenStore();

        Assert.Single(reopened.GetLibraries());
        Assert.Single(reopened.LoadAllEntries());
        Assert.Equal(libraryId, reopened.GetLibraries()[0].Id);
    }

    [Fact]
    public void Entries_loaded_from_the_database_are_searchable()
    {
        using var store = OpenStore();
        var library = store.AddLibrary("Movies", @"E:\Movies");
        store.ReplaceEntries(
            library.Id,
            [FileEntry("The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv"), FileEntry("Finding.Nemo.2003.mkv")],
            DateTime.UtcNow);

        var index = new FileIndex(store.LoadAllEntries());
        var hits = index.Search("matrix");

        Assert.Single(hits);
        Assert.Equal("The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv", hits[0].Entry.RawName);
    }

    [Fact]
    public void Writes_a_large_scan_quickly()
    {
        using var store = OpenStore();
        var library = store.AddLibrary("Movies", @"E:\Movies");

        var entries = Enumerable.Range(0, 20_000)
            .Select(i => FileEntry($"Film.Number.{i}.20{i % 20 + 1:00}.1080p.BluRay.x264-GROUP.mkv"))
            .ToArray();

        var started = DateTime.UtcNow;
        store.ReplaceEntries(library.Id, entries, DateTime.UtcNow);
        var elapsed = DateTime.UtcNow - started;

        Assert.Equal(20_000, store.LoadAllEntries().Count);
        Assert.True(elapsed < TimeSpan.FromSeconds(20), $"writing took {elapsed}");
    }
}
