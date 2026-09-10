using System.IO;
using FilmDex.Core;
using MaterialDesignThemes.Wpf;

namespace FilmDex.App.ViewModels;

/// <summary>One scanned folder, as the Libraries screen shows it.</summary>
public sealed record LibraryRow(long Id, string Label, string RootPath, int EntryCount, DateTime? LastScanUtc)
{
    public static LibraryRow From(LibraryRecord record) =>
        new(record.Id, record.Label, record.RootPath, record.EntryCount, record.LastScanUtc);

    /// <summary>Read at display time: an external drive can be unplugged between two glances.</summary>
    public bool IsAvailable => Directory.Exists(RootPath);

    public string AvailabilityText => IsAvailable ? "در دسترس" : "وصل نیست";

    public PackIconKind AvailabilityIcon => IsAvailable ? PackIconKind.CheckCircleOutline : PackIconKind.AlertCircleOutline;

    public string CountText => $"{EntryCount:N0} نام";

    public string LastScanText => LastScanUtc is { } scanned
        ? $"آخرین اسکن: {scanned.ToLocalTime():yyyy/MM/dd HH:mm}"
        : "هنوز اسکن نشده";
}

/// <summary>One search result, as the Search screen shows it.</summary>
public sealed record SearchResultRow(
    string Name,
    EntryKind Kind,
    int TitlePercent,
    int RawPercent,
    string YearText,
    bool YearMismatch,
    string LibraryLabel,
    string FullPath,
    string SizeText)
{
    public PackIconKind Icon => Kind == EntryKind.Folder ? PackIconKind.FolderOutline : PackIconKind.Movie;

    public string YearTooltip => YearMismatch ? "سال این فایل با سالی که زدی فرق دارد" : string.Empty;

    public static SearchResultRow From(SearchHit hit, string? rootPath)
    {
        var entry = hit.Entry;
        var fullPath = rootPath is null ? entry.RelativePath : Path.Combine(rootPath, entry.RelativePath);

        return new SearchResultRow(
            entry.RawName,
            entry.Kind,
            hit.Score.TitlePercent,
            hit.Score.RawPercent,
            entry.Parsed.Year?.ToString() ?? "—",
            hit.Score.YearMismatch,
            entry.LibraryLabel,
            fullPath,
            Describe(entry.SizeBytes));
    }

    private static string Describe(long? sizeBytes)
    {
        if (sizeBytes is not { } size) return "—";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = size;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
