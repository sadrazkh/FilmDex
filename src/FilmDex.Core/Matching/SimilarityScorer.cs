namespace FilmDex.Core;

/// <summary>How alike two names are, on the two measures the UI shows side by side.</summary>
/// <param name="TitlePercent">Similarity of the cleaned titles, 0–100.</param>
/// <param name="RawPercent">Similarity of the names exactly as they are written, 0–100.</param>
/// <param name="YearMismatch">True when both names carry a year and the years differ.</param>
public sealed record MatchScore(int TitlePercent, int RawPercent, bool YearMismatch)
{
    public int BestPercent => Math.Max(TitlePercent, RawPercent);
}

public static class SimilarityScorer
{
    /// <summary>Added when both names agree on the year.</summary>
    public const int YearBonus = 5;

    /// <summary>Taken off when both names carry a year and the years disagree.</summary>
    public const int YearPenalty = 15;

    /// <summary>A containment match is worth slightly less than an outright match.</summary>
    private const int ContainmentWeight = 95;

    public static MatchScore Score(ParsedName query, ParsedName candidate)
    {
        var title = TitlePercent(query, candidate);
        var yearMismatch = false;

        if (query.Year is { } queryYear && candidate.Year is { } candidateYear)
        {
            if (queryYear == candidateYear)
            {
                title = Math.Min(100, title + YearBonus);
            }
            else
            {
                title = Math.Max(0, title - YearPenalty);
                yearMismatch = true;
            }
        }

        return new MatchScore(title, Ratio(query.RawNormalized, candidate.RawNormalized), yearMismatch);
    }

    /// <summary>Title similarity before the year is taken into account.</summary>
    public static int TitlePercent(ParsedName left, ParsedName right)
    {
        var tokenSort = Ratio(left.SortedTitle, right.SortedTitle);

        var shared = left.DistinctTokens.Where(right.TokenSet.Contains)
                                        .OrderBy(t => t, StringComparer.Ordinal)
                                        .ToArray();
        if (shared.Length == 0) return tokenSort;

        var common = string.Join(' ', shared);
        var leftRest = Append(common, left.DistinctTokens.Where(t => !right.TokenSet.Contains(t)));
        var rightRest = Append(common, right.DistinctTokens.Where(t => !left.TokenSet.Contains(t)));

        var tokenSet = Math.Max(
            Ratio(common, leftRest),
            Math.Max(Ratio(common, rightRest), Ratio(leftRest, rightRest)));

        return Math.Max(tokenSort, tokenSet * ContainmentWeight / 100);
    }

    private static string Append(string common, IEnumerable<string> rest)
    {
        var tail = rest.OrderBy(t => t, StringComparer.Ordinal).ToArray();
        return tail.Length == 0 ? common : common + ' ' + string.Join(' ', tail);
    }

    /// <summary>Levenshtein similarity as a percentage: 100 is identical, 0 shares nothing.</summary>
    public static int Ratio(string left, string right)
    {
        if (left.Length == 0 && right.Length == 0) return 100;
        if (left.Length == 0 || right.Length == 0) return 0;
        if (string.Equals(left, right, StringComparison.Ordinal)) return 100;

        var longest = Math.Max(left.Length, right.Length);
        var distance = Levenshtein(left, right);

        return (int)Math.Round((1.0 - (double)distance / longest) * 100);
    }

    /// <summary>
    /// Edit distance, two rows at a time. <paramref name="maxDistance"/> lets a caller stop early:
    /// once every cell in a row is past the ceiling, the answer can only get worse.
    /// </summary>
    public static int Levenshtein(string left, string right, int maxDistance = int.MaxValue)
    {
        if (left.Length == 0) return right.Length;
        if (right.Length == 0) return left.Length;

        // Keep the shorter string as the row, so the arrays stay small.
        if (left.Length > right.Length) (left, right) = (right, left);

        var width = left.Length + 1;
        var previous = new int[width];
        var current = new int[width];

        for (var i = 0; i < width; i++) previous[i] = i;

        for (var row = 1; row <= right.Length; row++)
        {
            current[0] = row;
            var rowBest = current[0];
            var rightChar = right[row - 1];

            for (var column = 1; column < width; column++)
            {
                var cost = left[column - 1] == rightChar ? 0 : 1;
                var value = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);

                current[column] = value;
                if (value < rowBest) rowBest = value;
            }

            if (rowBest > maxDistance) return maxDistance + 1;

            (previous, current) = (current, previous);
        }

        return previous[left.Length];
    }
}
