using System.Buffers;
using System.Collections.Concurrent;

namespace FilmDex.Core;

/// <summary>
/// Every remembered name, in memory, searchable without touching the disk.
///
/// Scoring all of them on every keystroke is too slow once an archive gets large, so a character
/// bigram index shortlists the plausible ones first. The shortlist is a speed trick only: it must
/// never hide a name that full scoring would have returned, which is what
/// <c>The_prefilter_never_hides_a_match…</c> pins down.
/// </summary>
public sealed class FileIndex
{
    /// <summary>A candidate must share at least this share of the smaller bigram set.</summary>
    private const int ContainmentFloorPercent = 40;

    /// <summary>Above this many candidates, score them in parallel.</summary>
    private const int ParallelThreshold = 2_000;

    private readonly IndexEntry[] _entries;
    private readonly int[] _bigramCounts;
    private readonly Dictionary<int, int[]> _postings;

    public FileIndex(IEnumerable<IndexEntry> entries)
    {
        _entries = entries as IndexEntry[] ?? entries.ToArray();
        _bigramCounts = new int[_entries.Length];

        var postings = new Dictionary<int, List<int>>();
        var bigrams = new HashSet<int>();
        var titleBigrams = new HashSet<int>();

        for (var i = 0; i < _entries.Length; i++)
        {
            bigrams.Clear();
            titleBigrams.Clear();

            CollectBigrams(_entries[i].Parsed.CleanTitle, titleBigrams);
            bigrams.UnionWith(titleBigrams);
            CollectBigrams(_entries[i].Parsed.RawNormalized, bigrams);

            // Reachable through every bigram it has; measured against its title alone. Counting the
            // release junk here would bury a short title such as "Rings" under its own file name.
            _bigramCounts[i] = titleBigrams.Count;

            foreach (var bigram in bigrams)
            {
                if (!postings.TryGetValue(bigram, out var list))
                {
                    list = [];
                    postings[bigram] = list;
                }

                list.Add(i);
            }
        }

        _postings = postings.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public int Count => _entries.Length;

    public IReadOnlyList<SearchHit> Search(
        string query,
        int minPercent = 60,
        int maxResults = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var parsed = NameNormalizer.Parse(query);
        var candidates = Shortlist(parsed, cancellationToken);
        var hits = ScoreCandidates(parsed, candidates, minPercent, cancellationToken);

        hits.Sort(static (left, right) =>
        {
            var byTitle = right.Score.TitlePercent.CompareTo(left.Score.TitlePercent);
            if (byTitle != 0) return byTitle;

            var byRaw = right.Score.RawPercent.CompareTo(left.Score.RawPercent);
            return byRaw != 0 ? byRaw : string.CompareOrdinal(left.Entry.RawName, right.Entry.RawName);
        });

        return maxResults < hits.Count ? hits.GetRange(0, maxResults) : hits;
    }

    private List<int> Shortlist(ParsedName query, CancellationToken cancellationToken)
    {
        var titleBigrams = new HashSet<int>();
        CollectBigrams(query.CleanTitle, titleBigrams);

        var wanted = new HashSet<int>(titleBigrams);
        CollectBigrams(query.RawNormalized, wanted);

        // A one-character query has no bigrams; there is nothing to shortlist with.
        if (wanted.Count == 0) return Enumerable.Range(0, _entries.Length).ToList();

        var shared = ArrayPool<int>.Shared.Rent(_entries.Length);

        try
        {
            Array.Clear(shared, 0, _entries.Length);

            foreach (var bigram in wanted)
            {
                if (!_postings.TryGetValue(bigram, out var entryIndexes)) continue;

                foreach (var index in entryIndexes) shared[index]++;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var candidates = new List<int>();

            for (var i = 0; i < _entries.Length; i++)
            {
                if (shared[i] == 0) continue;

                var smaller = Math.Min(Math.Max(titleBigrams.Count, 1), _bigramCounts[i]);
                if (smaller == 0)
                {
                    candidates.Add(i); // a title too short to have a bigram of its own
                    continue;
                }

                if (shared[i] * 100 / smaller >= ContainmentFloorPercent) candidates.Add(i);
            }

            return candidates;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(shared);
        }
    }

    private List<SearchHit> ScoreCandidates(
        ParsedName query,
        List<int> candidates,
        int minPercent,
        CancellationToken cancellationToken)
    {
        if (candidates.Count < ParallelThreshold)
        {
            var hits = new List<SearchHit>();

            foreach (var index in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = _entries[index];
                var score = SimilarityScorer.Score(query, entry.Parsed);
                if (score.BestPercent >= minPercent) hits.Add(new SearchHit(entry, score));
            }

            return hits;
        }

        var found = new ConcurrentBag<SearchHit>();

        Parallel.ForEach(
            candidates,
            new ParallelOptions { CancellationToken = cancellationToken },
            index =>
            {
                var entry = _entries[index];
                var score = SimilarityScorer.Score(query, entry.Parsed);
                if (score.BestPercent >= minPercent) found.Add(new SearchHit(entry, score));
            });

        return found.ToList();
    }

    private static void CollectBigrams(string value, HashSet<int> into)
    {
        for (var i = 0; i + 1 < value.Length; i++)
        {
            into.Add((value[i] << 16) | value[i + 1]);
        }
    }
}
