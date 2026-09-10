using FilmDex.Core;

namespace FilmDex.Core.Tests;

public class FileIndexTests
{
    private static IndexEntry File(long id, string name) => new(
        id, LibraryId: 1, LibraryLabel: "Movies", Kind: EntryKind.File,
        RawName: name, Extension: Path.GetExtension(name), RelativePath: name,
        SizeBytes: 1_000_000_000, ModifiedUtc: null, Parsed: NameNormalizer.Parse(name));

    private static IndexEntry Folder(long id, string name) => new(
        id, LibraryId: 1, LibraryLabel: "Movies", Kind: EntryKind.Folder,
        RawName: name, Extension: null, RelativePath: name,
        SizeBytes: null, ModifiedUtc: null, Parsed: NameNormalizer.Parse(name, isFolder: true));

    private static FileIndex SmallIndex() => new(
    [
        File(1, "The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv"),
        File(2, "The.Matrix.Reloaded.2003.1080p.mkv"),
        File(3, "Finding.Nemo.2003.720p.mkv"),
        File(4, "Interstellar.2014.2160p.HDR.mkv"),
        Folder(5, "Inception (2010)")
    ]);

    [Fact]
    public void Finds_a_scene_named_file_from_a_plain_query()
    {
        var hits = SmallIndex().Search("the matrix");

        Assert.Equal(1, hits[0].Entry.Id);
        Assert.Equal(100, hits[0].Score.TitlePercent);
    }

    [Fact]
    public void Finds_a_folder_entry_too()
    {
        var hits = SmallIndex().Search("inception");

        Assert.Contains(hits, h => h.Entry.Kind == EntryKind.Folder && h.Entry.Id == 5);
    }

    [Fact]
    public void Returns_nothing_below_the_threshold()
    {
        var hits = SmallIndex().Search("the matrix", minPercent: 60);

        Assert.All(hits, h => Assert.True(h.Score.BestPercent >= 60, $"{h.Entry.RawName} scored {h.Score.BestPercent}"));
        Assert.DoesNotContain(hits, h => h.Entry.Id == 3);
    }

    [Fact]
    public void Orders_the_best_match_first()
    {
        var hits = SmallIndex().Search("matrix", minPercent: 40);

        var scores = hits.Select(h => h.Score.TitlePercent).ToArray();
        Assert.Equal(scores.OrderByDescending(s => s).ToArray(), scores);
    }

    [Fact]
    public void Caps_the_number_of_results()
    {
        var hits = SmallIndex().Search("the", minPercent: 0, maxResults: 2);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public void An_empty_query_returns_nothing()
    {
        Assert.Empty(SmallIndex().Search("   "));
    }

    [Fact]
    public void A_single_character_query_does_not_throw()
    {
        var hits = SmallIndex().Search("m", minPercent: 0);

        Assert.NotNull(hits);
    }

    [Fact]
    public void Counts_what_it_was_given()
    {
        Assert.Equal(5, SmallIndex().Count);
    }

    [Theory]
    [InlineData("the matrix")]
    [InlineData("matrix")]
    [InlineData("interstellar 2014")]
    [InlineData("lord of the rings")]
    [InlineData("قصه های ماشین ها")]
    public void The_prefilter_never_hides_a_match_that_scoring_would_have_found(string query)
    {
        var entries = BuildCorpus();
        var index = new FileIndex(entries);
        var parsedQuery = NameNormalizer.Parse(query);

        var expected = entries
            .Where(e => SimilarityScorer.Score(parsedQuery, e.Parsed).BestPercent >= 60)
            .Select(e => e.Id)
            .ToHashSet();

        var actual = index.Search(query, minPercent: 60, maxResults: int.MaxValue)
            .Select(h => h.Entry.Id)
            .ToHashSet();

        Assert.Equal(expected.OrderBy(id => id), actual.OrderBy(id => id));
    }

    private static List<IndexEntry> BuildCorpus()
    {
        string[] titles =
        [
            "The Matrix", "The Matrix Reloaded", "The Matrix Revolutions", "Matrix of Lies",
            "Interstellar", "Inception", "Finding Nemo", "Finding Dory", "The Lord of the Rings",
            "The Lord of the Rings The Two Towers", "Rings", "The Ring", "Blade Runner 2049",
            "قصه‌های ماشین‌ها", "ماتریکس", "Fast and Furious", "Fast Five", "Interstelar"
        ];

        var entries = new List<IndexEntry>();
        var id = 1L;

        foreach (var title in titles)
        {
            var dotted = title.Replace(' ', '.');
            entries.Add(File(id++, $"{dotted}.2014.1080p.BluRay.x264-GROUP.mkv"));
            entries.Add(Folder(id++, title));
        }

        // Filler, so the prefilter has something to reject.
        for (var i = 0; i < 500; i++)
        {
            entries.Add(File(id++, $"Random.Show.S0{i % 9 + 1}E{i % 20 + 1:00}.720p.WEB-DL-{i}.mkv"));
        }

        return entries;
    }
}
