using FilmDex.Core;

namespace FilmDex.Core.Tests;

public class SimilarityScorerTests
{
    private static MatchScore Score(string query, string candidate) =>
        SimilarityScorer.Score(NameNormalizer.Parse(query), NameNormalizer.Parse(candidate));

    [Fact]
    public void An_identical_name_scores_full_marks_on_both_measures()
    {
        var score = Score("The.Matrix.1999.1080p.mkv", "The.Matrix.1999.1080p.mkv");

        Assert.Equal(100, score.TitlePercent);
        Assert.Equal(100, score.RawPercent);
    }

    [Fact]
    public void The_same_film_in_a_different_release_matches_on_title_but_not_on_the_raw_name()
    {
        var score = Score("the matrix 1999", "The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv");

        Assert.Equal(100, score.TitlePercent);
        Assert.True(score.RawPercent < 60, $"raw score was {score.RawPercent}");
    }

    [Fact]
    public void A_short_query_still_matches_a_longer_title_that_contains_it()
    {
        var score = Score("matrix", "The.Matrix.Reloaded.2003.1080p.mkv");

        Assert.True(score.TitlePercent >= 85, $"title score was {score.TitlePercent}");
    }

    [Fact]
    public void An_exact_title_outranks_a_title_that_merely_contains_the_query()
    {
        var exact = Score("the matrix", "The.Matrix.1999.1080p.mkv");
        var contains = Score("the matrix", "The.Matrix.Reloaded.2003.1080p.mkv");

        Assert.True(exact.TitlePercent > contains.TitlePercent,
            $"exact {exact.TitlePercent} should beat contains {contains.TitlePercent}");
    }

    [Fact]
    public void Unrelated_films_score_low()
    {
        var score = Score("the matrix", "Finding.Nemo.2003.1080p.mkv");

        Assert.True(score.TitlePercent < 50, $"title score was {score.TitlePercent}");
    }

    [Fact]
    public void The_same_title_in_a_different_year_is_penalised_and_flagged()
    {
        var score = Score("The Matrix 1999", "The.Matrix.2003.1080p.mkv");

        Assert.True(score.YearMismatch);
        Assert.True(score.TitlePercent < 100, $"title score was {score.TitlePercent}");
        Assert.True(score.TitlePercent >= 70, $"title score was {score.TitlePercent}");
    }

    [Fact]
    public void A_missing_year_on_one_side_is_not_a_mismatch()
    {
        var score = Score("the matrix", "The.Matrix.1999.1080p.mkv");

        Assert.False(score.YearMismatch);
    }

    [Fact]
    public void Scoring_is_symmetric()
    {
        var forward = Score("the matrix reloaded", "The.Matrix.1999.1080p.mkv");
        var backward = Score("The.Matrix.1999.1080p.mkv", "the matrix reloaded");

        Assert.Equal(forward.TitlePercent, backward.TitlePercent);
        Assert.Equal(forward.RawPercent, backward.RawPercent);
    }

    [Fact]
    public void Word_order_does_not_matter()
    {
        var score = Score("Furious Fast", "Fast.Furious.2009.mkv");

        Assert.True(score.TitlePercent >= 90, $"title score was {score.TitlePercent}");
    }

    [Fact]
    public void A_persian_query_matches_the_same_name_written_with_arabic_letters()
    {
        // Query uses the Arabic yeh; the file on disk uses the Farsi yeh and a ZWNJ.
        var score = Score("قصه های ماشين ها", "قصه‌های ماشین‌ها ۲۰۰۶.mkv");

        Assert.Equal(100, score.TitlePercent);
    }

    [Fact]
    public void A_typo_still_finds_the_film()
    {
        var score = Score("intersteller", "Interstellar.2014.2160p.mkv");

        Assert.True(score.TitlePercent >= 80, $"title score was {score.TitlePercent}");
    }

    [Fact]
    public void The_best_percent_is_the_higher_of_the_two()
    {
        var score = new MatchScore(40, 70, false);

        Assert.Equal(70, score.BestPercent);
    }

    [Theory]
    [InlineData("", "", 100)]
    [InlineData("abc", "", 0)]
    [InlineData("abc", "abc", 100)]
    public void The_ratio_handles_the_degenerate_cases(string left, string right, int expected)
    {
        Assert.Equal(expected, SimilarityScorer.Ratio(left, right));
    }
}
