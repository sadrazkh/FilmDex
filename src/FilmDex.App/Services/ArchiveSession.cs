using FilmDex.Core;

namespace FilmDex.App.Services;

/// <summary>
/// Owns the database and the in-memory index, and is the only thing in the app that talks to
/// either. Every database call is serialised: SQLite here is one connection, and a scan finishing
/// must not collide with a search reloading.
/// </summary>
public sealed class ArchiveSession : IDisposable
{
    private readonly LibraryStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ArchiveSession(string databasePath)
    {
        DatabasePath = databasePath;
        _store = new LibraryStore(databasePath);
    }

    public string DatabasePath { get; }

    /// <summary>The searchable snapshot. Replaced wholesale after a scan.</summary>
    public FileIndex Index { get; private set; } = new([]);

    public async Task<IReadOnlyList<LibraryRecord>> GetLibrariesAsync() =>
        await WithStoreAsync(store => store.GetLibraries());

    public async Task<LibraryRecord> AddLibraryAsync(string label, string rootPath) =>
        await WithStoreAsync(store => store.AddLibrary(label, rootPath));

    public async Task RemoveLibraryAsync(long libraryId)
    {
        await WithStoreAsync<object?>(store =>
        {
            store.RemoveLibrary(libraryId);
            return null;
        });

        await ReloadIndexAsync();
    }

    /// <summary>
    /// Reads the folder, writes the result over that library's previous snapshot, and rebuilds the
    /// index. A cancelled scan writes nothing, so the previous names survive.
    /// </summary>
    public async Task<ScanResult> ScanAsync(
        long libraryId,
        string rootPath,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        var scan = await new FolderScanner().ScanAsync(rootPath, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await WithStoreAsync<object?>(store =>
        {
            store.ReplaceEntries(libraryId, scan.Entries, DateTime.UtcNow);
            return null;
        });

        await ReloadIndexAsync();
        return scan;
    }

    /// <summary>
    /// Recomputes every stored title from the names already in the database — what to run after the
    /// parser learns a new rule, instead of asking for every drive back.
    /// </summary>
    public async Task<int> ReparseAllAsync()
    {
        var count = await WithStoreAsync(store => store.ReparseAll());
        await ReloadIndexAsync();
        return count;
    }

    public async Task ReloadIndexAsync()
    {
        var entries = await WithStoreAsync(store => store.LoadAllEntries());
        Index = await Task.Run(() => new FileIndex(entries));
    }

    public void Dispose()
    {
        _store.Dispose();
        _gate.Dispose();
    }

    private async Task<T> WithStoreAsync<T>(Func<LibraryStore, T> work)
    {
        await _gate.WaitAsync();

        try
        {
            return await Task.Run(() => work(_store));
        }
        finally
        {
            _gate.Release();
        }
    }
}
