namespace FilmDex.Core;

public enum EntryKind
{
    File = 0,
    Folder = 1
}

/// <summary>One remembered name, ready to be searched.</summary>
public sealed record IndexEntry(
    long Id,
    long LibraryId,
    string LibraryLabel,
    EntryKind Kind,
    string RawName,
    string? Extension,
    string RelativePath,
    long? SizeBytes,
    DateTime? ModifiedUtc,
    ParsedName Parsed);

/// <summary>An entry and how well it matched what was typed.</summary>
public sealed record SearchHit(IndexEntry Entry, MatchScore Score);

/// <summary>What a scan found, before it is given an identity in the database.</summary>
public sealed record ScannedEntry(
    EntryKind Kind,
    string RawName,
    string? Extension,
    string RelativePath,
    long? SizeBytes,
    DateTime? ModifiedUtc);

/// <summary>A folder the owner asked FilmDex to remember.</summary>
public sealed record LibraryRecord(
    long Id,
    string Label,
    string RootPath,
    DateTime? LastScanUtc,
    int EntryCount);
