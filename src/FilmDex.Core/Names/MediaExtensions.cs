namespace FilmDex.Core;

/// <summary>The file extensions FilmDex considers to be a film.</summary>
public static class MediaExtensions
{
    private static readonly HashSet<string> VideoSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv", ".flv", ".ts", ".m2ts", ".mts",
        ".mpg", ".mpeg", ".webm", ".rmvb", ".rm", ".divx", ".vob", ".iso", ".ogm", ".asf", ".3gp"
    };

    private static readonly HashSet<string> CompanionSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".sub", ".idx", ".ass", ".ssa", ".nfo", ".txt", ".jpg", ".png", ".part", ".torrent"
    };

    public static IReadOnlyCollection<string> Video => VideoSet;

    public static bool IsVideo(string extension) => VideoSet.Contains(extension);

    /// <summary>True for an extension worth stripping before a name is parsed.</summary>
    public static bool IsKnown(string extension) =>
        VideoSet.Contains(extension) || CompanionSet.Contains(extension);
}
