using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FilmDex.Core;

/// <summary>
/// Turns a file or folder name into a comparable title. Scene names put the title first and the
/// junk after, so the year (or the season marker) is where the title ends.
/// </summary>
public static partial class NameNormalizer
{
    public static ParsedName Parse(string rawName, bool isFolder = false)
    {
        var raw = rawName ?? string.Empty;

        var body = raw;
        if (!isFolder)
        {
            var extension = Path.GetExtension(raw);
            if (extension.Length > 1 && MediaExtensions.IsKnown(extension))
            {
                body = raw[..^extension.Length];
            }
        }

        var rawNormalized = body.Trim().ToLowerInvariant();
        var tokens = Fold(body).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        tokens = DropSitePrefix(tokens);

        var (season, episode, seasonIndex) = ReadSeasonAndEpisode(tokens);
        var (year, yearIndex) = ReadYear(tokens);

        var cut = tokens.Length;
        if (yearIndex > 0) cut = Math.Min(cut, yearIndex);
        if (seasonIndex > 0) cut = Math.Min(cut, seasonIndex);

        var head = tokens.Take(cut).ToArray();
        var kept = head.Where(t => !ReleaseTags.Contains(t)).Select(RomanToDigits).ToArray();

        if (kept.Length == 0) kept = head;
        if (kept.Length == 0) kept = tokens;

        var cleanTitle = string.Join(' ', kept);
        if (cleanTitle.Length == 0) cleanTitle = rawNormalized;

        return new ParsedName(raw, rawNormalized, cleanTitle, kept, year, season, episode);
    }

    /// <summary>Folds a query or a name to the same alphabet, digits and spacing the index uses.</summary>
    public static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '‌': // zero-width non-joiner: "قصه‌های" is two words
                case '‍':
                case '‎':
                case '‏':
                    builder.Append(' ');
                    continue;
                case '&':
                    builder.Append(" and ");
                    continue;
                case '\'':
                case '’':
                case '`':
                    continue; // "don't" folds to "dont", not to two tokens
            }

            if (ch is >= '٠' and <= '٩') { builder.Append((char)('0' + (ch - '٠'))); continue; }
            if (ch is >= '۰' and <= '۹') { builder.Append((char)('0' + (ch - '۰'))); continue; }
            if (ch is >= 'ً' and <= 'ْ' or 'ٰ') continue; // Arabic diacritics

            var mapped = ch switch
            {
                'ي' or 'ى' or 'ئ' => 'ی', // yeh forms  → Farsi yeh
                'ك' => 'ک',                          // Arabic kaf → keheh
                'ة' => 'ه',                          // teh marbuta → heh
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ؤ' => 'و',
                _ => ch
            };

            builder.Append(char.IsLetterOrDigit(mapped) ? char.ToLowerInvariant(mapped) : ' ');
        }

        return CollapseSpaces(builder.ToString());
    }

    private static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;

        foreach (var ch in value)
        {
            if (ch == ' ')
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string[] DropSitePrefix(string[] tokens)
    {
        // "www.Film2Movie.Us.The.Matrix…" — the site, not the film.
        if (tokens.Length >= 3 && tokens[0] == "www" && TopLevelDomains.Contains(tokens[2]))
            return tokens[3..];
        if (tokens.Length >= 2 && tokens[0] == "www" && TopLevelDomains.Contains(tokens[1]))
            return tokens[2..];
        return tokens;
    }

    private static (int? Season, int? Episode, int Index) ReadSeasonAndEpisode(string[] tokens)
    {
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            var seasonEpisode = SeasonEpisodePattern().Match(token);
            if (seasonEpisode.Success)
            {
                return (int.Parse(seasonEpisode.Groups[1].Value, CultureInfo.InvariantCulture),
                        int.Parse(seasonEpisode.Groups[2].Value, CultureInfo.InvariantCulture),
                        i);
            }

            var crossForm = CrossFormPattern().Match(token);
            if (crossForm.Success)
            {
                return (int.Parse(crossForm.Groups[1].Value, CultureInfo.InvariantCulture),
                        int.Parse(crossForm.Groups[2].Value, CultureInfo.InvariantCulture),
                        i);
            }

            if (token == "season" && i + 1 < tokens.Length &&
                int.TryParse(tokens[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var spelled))
            {
                return (spelled, null, i);
            }

            var shortSeason = ShortSeasonPattern().Match(token);
            if (shortSeason.Success)
            {
                return (int.Parse(shortSeason.Groups[1].Value, CultureInfo.InvariantCulture), null, i);
            }
        }

        return (null, null, -1);
    }

    private static (int? Year, int Index) ReadYear(string[] tokens)
    {
        var index = -1;

        for (var i = 0; i < tokens.Length; i++)
        {
            if (YearPattern().IsMatch(tokens[i])) index = i;
        }

        // A year in first position is the title — "2012", "1917" — not a release year.
        if (index <= 0) return (null, index);

        return (int.Parse(tokens[index], CultureInfo.InvariantCulture), index);
    }

    private static string RomanToDigits(string token) =>
        RomanNumerals.TryGetValue(token, out var digits) ? digits : token;

    private static readonly Dictionary<string, string> RomanNumerals = new(StringComparer.Ordinal)
    {
        // "i", "v" and "x" are left alone: "V for Vendetta", "X-Men", "I Am Legend".
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["vi"] = "6",
        ["vii"] = "7", ["viii"] = "8", ["ix"] = "9"
    };

    private static readonly HashSet<string> TopLevelDomains = new(StringComparer.Ordinal)
    {
        "com", "net", "org", "ir", "us", "info", "co", "me", "tv", "biz", "ws", "io", "la",
        "site", "xyz", "top", "in", "cc", "download", "click"
    };

    private static readonly HashSet<string> ReleaseTags = BuildReleaseTags();

    private static HashSet<string> BuildReleaseTags()
    {
        string[] tags =
        [
            // resolution and range
            "480p", "576p", "720p", "1080p", "1440p", "2160p", "4k", "8k", "uhd", "fhd", "hd", "sd",
            "hdr", "hdr10", "sdr", "dv",
            // source
            "bluray", "blueray", "bdrip", "brrip", "bdremux", "remux", "webrip", "webdl", "web", "dl",
            "hdtv", "pdtv", "dvdrip", "dvdscr", "dvd", "hdrip", "camrip", "cam", "telesync", "r5",
            "vhsrip", "hdcam", "scr", "amzn", "nf", "hulu", "dsnp", "atvp", "itunes", "ts", "tc",
            // codec
            "x264", "x265", "h264", "h265", "hevc", "avc", "xvid", "divx", "av1",
            "10bit", "8bit", "hi10p", "hi10",
            // audio
            "aac", "ac3", "eac3", "ddp", "dts", "dtshd", "truehd", "atmos", "mp3", "flac", "opus",
            // packaging and groups
            "dubbed", "dubfa", "softsub", "hardsub", "subbed", "sample", "trailer",
            "yify", "yts", "rarbg", "evo", "eztv", "ettv", "psa", "anoxmous", "ganool", "d3g",
            // the Persian words these files carry
            "دوبله", "فارسی", "زیرنویس", "سانسور", "بدون", "دانلود", "نسخه", "کیفیت",
            "هاردساب", "سافتساب", "اختصاصی"
        ];

        // Fold the tags themselves, so an Arabic yeh in this list can never miss a folded token.
        return new HashSet<string>(tags.Select(Fold), StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^s(\d{1,2})e(\d{1,3})$")]
    private static partial Regex SeasonEpisodePattern();

    [GeneratedRegex(@"^(\d{1,2})x(\d{1,3})$")]
    private static partial Regex CrossFormPattern();

    [GeneratedRegex(@"^s(\d{2})$")]
    private static partial Regex ShortSeasonPattern();

    [GeneratedRegex(@"^(19|20)\d{2}$")]
    private static partial Regex YearPattern();
}
