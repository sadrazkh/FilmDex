namespace FilmDex.Core;

/// <summary>
/// A file or folder name after the release junk has been taken off it.
/// </summary>
public sealed class ParsedName
{
    public ParsedName(
        string raw,
        string rawNormalized,
        string cleanTitle,
        string[] titleTokens,
        int? year,
        int? season,
        int? episode)
    {
        Raw = raw;
        RawNormalized = rawNormalized;
        CleanTitle = cleanTitle;
        TitleTokens = titleTokens;
        Year = year;
        Season = season;
        Episode = episode;

        // Precomputed once per name, because scoring touches these on every keystroke.
        DistinctTokens = titleTokens.Distinct(StringComparer.Ordinal).ToArray();
        TokenSet = new HashSet<string>(DistinctTokens, StringComparer.Ordinal);
        SortedTitle = string.Join(' ', DistinctTokens.OrderBy(t => t, StringComparer.Ordinal));
    }

    /// <summary>
    /// Rebuilds a parsed name from the columns the database already holds, so loading an archive
    /// does not mean re-running the parser over every row.
    /// </summary>
    public static ParsedName FromStored(
        string raw,
        string? extension,
        string cleanTitle,
        int? year,
        int? season,
        int? episode)
    {
        var body = extension is { Length: > 0 } && raw.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? raw[..^extension.Length]
            : raw;

        return new ParsedName(
            raw,
            body.Trim().ToLowerInvariant(),
            cleanTitle,
            cleanTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            year,
            season,
            episode);
    }

    /// <summary>The name exactly as it was on disk, or exactly as it was typed.</summary>
    public string Raw { get; }

    /// <summary>The raw name lowercased with its extension removed — what the raw score compares.</summary>
    public string RawNormalized { get; }

    /// <summary>The title with tags, year, season and punctuation removed.</summary>
    public string CleanTitle { get; }

    /// <summary>The words of <see cref="CleanTitle"/>, in order.</summary>
    public string[] TitleTokens { get; }

    /// <summary>Those words with repeats removed.</summary>
    public string[] DistinctTokens { get; }

    /// <summary>The same words as a set, for intersecting two titles.</summary>
    public IReadOnlySet<string> TokenSet { get; }

    /// <summary>Distinct words sorted and joined, so word order cannot affect a comparison.</summary>
    public string SortedTitle { get; }

    public int? Year { get; }

    public int? Season { get; }

    public int? Episode { get; }

    public override string ToString() => Year is null ? CleanTitle : $"{CleanTitle} ({Year})";
}
