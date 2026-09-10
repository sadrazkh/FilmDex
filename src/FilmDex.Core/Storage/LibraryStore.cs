using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FilmDex.Core;

/// <summary>
/// Where the names live between runs. One SQLite file, opened once and kept open.
/// </summary>
public sealed class LibraryStore : IDisposable
{
    private const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;

    public LibraryStore(string databasePath)
    {
        var folder = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        _connection.Open();
        Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;");
        Migrate();
    }

    /// <summary>`%LOCALAPPDATA%\FilmDex\filmdex.db`.</summary>
    public static string DefaultDatabasePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FilmDex",
        "filmdex.db");

    public IReadOnlyList<LibraryRecord> GetLibraries()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Label, RootPath, LastScanUtc, EntryCount FROM Library ORDER BY Label COLLATE NOCASE";

        using var reader = command.ExecuteReader();
        var libraries = new List<LibraryRecord>();

        while (reader.Read()) libraries.Add(ReadLibrary(reader));

        return libraries;
    }

    public LibraryRecord? FindByRoot(string rootPath)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Label, RootPath, LastScanUtc, EntryCount FROM Library WHERE RootPath = $root COLLATE NOCASE";
        command.Parameters.AddWithValue("$root", rootPath.TrimEnd('\\', '/'));

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadLibrary(reader) : null;
    }

    public LibraryRecord AddLibrary(string label, string rootPath)
    {
        var root = rootPath.TrimEnd('\\', '/');

        var existing = FindByRoot(root);
        if (existing is not null) return existing;

        using var command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO Library (Label, RootPath, EntryCount) VALUES ($label, $root, 0); SELECT last_insert_rowid();";
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$root", root);

        var id = (long)command.ExecuteScalar()!;
        return new LibraryRecord(id, label, root, null, 0);
    }

    public void RemoveLibrary(long libraryId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM Library WHERE Id = $id";
        command.Parameters.AddWithValue("$id", libraryId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes a scan over the top of whatever that library held before. A file deleted on disk
    /// disappears from the index — the index mirrors reality rather than remembering it.
    /// </summary>
    public void ReplaceEntries(long libraryId, IReadOnlyList<ScannedEntry> entries, DateTime scanUtc)
    {
        using var transaction = _connection.BeginTransaction();

        using (var delete = _connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM Entry WHERE LibraryId = $id";
            delete.Parameters.AddWithValue("$id", libraryId);
            delete.ExecuteNonQuery();
        }

        using (var insert = _connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO Entry
                    (LibraryId, Kind, RawName, Extension, RelativePath, SizeBytes, ModifiedUtc,
                     CleanTitle, Year, Season, Episode)
                VALUES
                    ($library, $kind, $raw, $extension, $path, $size, $modified,
                     $title, $year, $season, $episode)
                """;

            var library = insert.Parameters.Add("$library", SqliteType.Integer);
            var kind = insert.Parameters.Add("$kind", SqliteType.Integer);
            var raw = insert.Parameters.Add("$raw", SqliteType.Text);
            var extension = insert.Parameters.Add("$extension", SqliteType.Text);
            var path = insert.Parameters.Add("$path", SqliteType.Text);
            var size = insert.Parameters.Add("$size", SqliteType.Integer);
            var modified = insert.Parameters.Add("$modified", SqliteType.Text);
            var title = insert.Parameters.Add("$title", SqliteType.Text);
            var year = insert.Parameters.Add("$year", SqliteType.Integer);
            var season = insert.Parameters.Add("$season", SqliteType.Integer);
            var episode = insert.Parameters.Add("$episode", SqliteType.Integer);

            library.Value = libraryId;

            foreach (var entry in entries)
            {
                var parsed = NameNormalizer.Parse(entry.RawName, entry.Kind == EntryKind.Folder);

                kind.Value = (int)entry.Kind;
                raw.Value = entry.RawName;
                extension.Value = (object?)entry.Extension ?? DBNull.Value;
                path.Value = entry.RelativePath;
                size.Value = (object?)entry.SizeBytes ?? DBNull.Value;
                modified.Value = entry.ModifiedUtc is { } m ? ToText(m) : DBNull.Value;
                title.Value = parsed.CleanTitle;
                year.Value = (object?)parsed.Year ?? DBNull.Value;
                season.Value = (object?)parsed.Season ?? DBNull.Value;
                episode.Value = (object?)parsed.Episode ?? DBNull.Value;

                insert.ExecuteNonQuery();
            }
        }

        using (var update = _connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE Library SET LastScanUtc = $scan, EntryCount = $count WHERE Id = $id";
            update.Parameters.AddWithValue("$scan", ToText(scanUtc));
            update.Parameters.AddWithValue("$count", entries.Count);
            update.Parameters.AddWithValue("$id", libraryId);
            update.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<IndexEntry> LoadAllEntries()
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            SELECT e.Id, e.LibraryId, l.Label, e.Kind, e.RawName, e.Extension, e.RelativePath,
                   e.SizeBytes, e.ModifiedUtc, e.CleanTitle, e.Year, e.Season, e.Episode
            FROM Entry e
            JOIN Library l ON l.Id = e.LibraryId
            """;

        using var reader = command.ExecuteReader();
        var entries = new List<IndexEntry>();

        while (reader.Read())
        {
            var rawName = reader.GetString(4);
            var extension = reader.IsDBNull(5) ? null : reader.GetString(5);

            entries.Add(new IndexEntry(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                (EntryKind)reader.GetInt32(3),
                rawName,
                extension,
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : FromText(reader.GetString(8)),
                ParsedName.FromStored(
                    rawName,
                    extension,
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.IsDBNull(11) ? null : reader.GetInt32(11),
                    reader.IsDBNull(12) ? null : reader.GetInt32(12))));
        }

        return entries;
    }

    /// <summary>
    /// Recomputes every stored title from the name alone. This is what makes a change to the
    /// parser cheap: no drive has to be plugged back in for the archive to benefit from it.
    /// </summary>
    public int ReparseAll()
    {
        var rows = new List<(long Id, string RawName, EntryKind Kind)>();

        using (var select = _connection.CreateCommand())
        {
            select.CommandText = "SELECT Id, RawName, Kind FROM Entry";
            using var reader = select.ExecuteReader();
            while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1), (EntryKind)reader.GetInt32(2)));
        }

        using var transaction = _connection.BeginTransaction();

        using (var update = _connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE Entry SET CleanTitle = $title, Year = $year, Season = $season, Episode = $episode WHERE Id = $id";

            var title = update.Parameters.Add("$title", SqliteType.Text);
            var year = update.Parameters.Add("$year", SqliteType.Integer);
            var season = update.Parameters.Add("$season", SqliteType.Integer);
            var episode = update.Parameters.Add("$episode", SqliteType.Integer);
            var id = update.Parameters.Add("$id", SqliteType.Integer);

            foreach (var row in rows)
            {
                var parsed = NameNormalizer.Parse(row.RawName, row.Kind == EntryKind.Folder);

                title.Value = parsed.CleanTitle;
                year.Value = (object?)parsed.Year ?? DBNull.Value;
                season.Value = (object?)parsed.Season ?? DBNull.Value;
                episode.Value = (object?)parsed.Episode ?? DBNull.Value;
                id.Value = row.Id;

                update.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return rows.Count;
    }

    public void Dispose() => _connection.Dispose();

    private void Migrate()
    {
        var version = Convert.ToInt32(Scalar("PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version >= SchemaVersion) return;

        if (version < 1)
        {
            Execute(
                """
                CREATE TABLE IF NOT EXISTS Library (
                    Id          INTEGER PRIMARY KEY,
                    Label       TEXT    NOT NULL,
                    RootPath    TEXT    NOT NULL,
                    LastScanUtc TEXT    NULL,
                    EntryCount  INTEGER NOT NULL DEFAULT 0
                );

                CREATE UNIQUE INDEX IF NOT EXISTS UX_Library_RootPath ON Library(RootPath COLLATE NOCASE);

                CREATE TABLE IF NOT EXISTS Entry (
                    Id           INTEGER PRIMARY KEY,
                    LibraryId    INTEGER NOT NULL REFERENCES Library(Id) ON DELETE CASCADE,
                    Kind         INTEGER NOT NULL,
                    RawName      TEXT    NOT NULL,
                    Extension    TEXT    NULL,
                    RelativePath TEXT    NOT NULL,
                    SizeBytes    INTEGER NULL,
                    ModifiedUtc  TEXT    NULL,
                    CleanTitle   TEXT    NOT NULL,
                    Year         INTEGER NULL,
                    Season       INTEGER NULL,
                    Episode      INTEGER NULL,
                    CategoryId   INTEGER NULL
                );

                CREATE INDEX IF NOT EXISTS IX_Entry_LibraryId ON Entry(LibraryId);
                """);
        }

        Execute($"PRAGMA user_version = {SchemaVersion}");
    }

    private static LibraryRecord ReadLibrary(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : FromText(reader.GetString(3)),
        reader.GetInt32(4));

    private static string ToText(DateTime value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTime FromText(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
