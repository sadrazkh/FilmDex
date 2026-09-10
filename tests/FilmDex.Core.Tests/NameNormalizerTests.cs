using FilmDex.Core;

namespace FilmDex.Core.Tests;

public class NameNormalizerTests
{
    [Fact]
    public void Strips_release_tags_and_group_from_a_scene_name()
    {
        var parsed = NameNormalizer.Parse("The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv");

        Assert.Equal("the matrix", parsed.CleanTitle);
        Assert.Equal(1999, parsed.Year);
    }

    [Fact]
    public void Reads_a_year_written_in_brackets()
    {
        var parsed = NameNormalizer.Parse("Interstellar (2014) [2160p] [HDR].mkv");

        Assert.Equal("interstellar", parsed.CleanTitle);
        Assert.Equal(2014, parsed.Year);
    }

    [Fact]
    public void Keeps_a_year_that_belongs_to_the_title_when_a_release_year_follows_it()
    {
        var parsed = NameNormalizer.Parse("Blade.Runner.2049.2017.2160p.WEB-DL.mkv");

        Assert.Equal("blade runner 2049", parsed.CleanTitle);
        Assert.Equal(2017, parsed.Year);
    }

    [Fact]
    public void Reads_season_and_episode_from_the_scene_form()
    {
        var parsed = NameNormalizer.Parse("Breaking.Bad.S01E02.Cat.in.the.Bag.720p.WEB-DL.mkv");

        Assert.Equal("breaking bad", parsed.CleanTitle);
        Assert.Equal(1, parsed.Season);
        Assert.Equal(2, parsed.Episode);
    }

    [Fact]
    public void Reads_season_and_episode_from_the_x_form()
    {
        var parsed = NameNormalizer.Parse("Friends 3x07 The One With The Race Car Bed.avi");

        Assert.Equal("friends", parsed.CleanTitle);
        Assert.Equal(3, parsed.Season);
        Assert.Equal(7, parsed.Episode);
    }

    [Fact]
    public void Reads_a_season_folder()
    {
        var parsed = NameNormalizer.Parse("The Office Season 1", isFolder: true);

        Assert.Equal("the office", parsed.CleanTitle);
        Assert.Equal(1, parsed.Season);
        Assert.Null(parsed.Episode);
    }

    [Fact]
    public void Treats_a_folder_name_ending_in_a_dotted_word_as_part_of_the_title()
    {
        // A folder has no extension: ".Rings" must not be stripped the way ".mkv" would be.
        var parsed = NameNormalizer.Parse("The.Lord.of.the.Rings", isFolder: true);

        Assert.Equal("the lord of the rings", parsed.CleanTitle);
    }

    [Fact]
    public void Folds_arabic_letters_persian_digits_and_zero_width_joiners()
    {
        // "قصه‌های ماشين‌ها ۲۰۰۶ دوبله فارسی.mkv" — with the Arabic yeh and a ZWNJ, as such files
        // are usually named. Escapes keep the intent unambiguous in source.
        const string raw = "قصه‌های ماشين‌ها ۲۰۰۶ دوبله فارسی.mkv";

        var parsed = NameNormalizer.Parse(raw);

        // "قصه های ماشین ها"
        Assert.Equal("قصه های ماشین ها", parsed.CleanTitle);
        Assert.Equal(2006, parsed.Year);
    }

    [Fact]
    public void Drops_persian_release_words()
    {
        // "ماتریکس دوبله فارسی بدون سانسور.mkv"
        const string raw = "ماتریکس دوبله فارسی بدون سانسور.mkv";

        var parsed = NameNormalizer.Parse(raw);

        Assert.Equal("ماتریکس", parsed.CleanTitle);
    }

    [Fact]
    public void Drops_a_download_site_prefix()
    {
        var parsed = NameNormalizer.Parse("www.Film2Movie.Us.The.Matrix.1999.1080p.BluRay.mkv");

        Assert.Equal("the matrix", parsed.CleanTitle);
        Assert.Equal(1999, parsed.Year);
    }

    [Fact]
    public void Converts_unambiguous_roman_numerals_to_digits()
    {
        Assert.Equal("rocky 2", NameNormalizer.Parse("Rocky.II.1979.1080p.mkv").CleanTitle);
    }

    [Fact]
    public void Leaves_roman_numerals_that_are_ordinary_words_alone()
    {
        Assert.Equal("v for vendetta", NameNormalizer.Parse("V.for.Vendetta.2005.1080p.BluRay.mkv").CleanTitle);
    }

    [Fact]
    public void Writes_an_ampersand_as_a_word()
    {
        Assert.Equal("fast and furious", NameNormalizer.Parse("Fast & Furious (2009).mp4").CleanTitle);
    }

    [Fact]
    public void Falls_back_to_the_whole_name_when_every_token_is_a_release_tag()
    {
        var parsed = NameNormalizer.Parse("1080p.x264-GROUP.mkv");

        Assert.NotEqual(string.Empty, parsed.CleanTitle);
    }

    [Fact]
    public void Parsing_is_stable_across_the_punctuation_people_actually_use()
    {
        var dotted = NameNormalizer.Parse("Mission.Impossible.Fallout.2018.1080p.mkv");
        var spaced = NameNormalizer.Parse("Mission Impossible - Fallout (2018) 1080p.mkv");
        var underscored = NameNormalizer.Parse("Mission_Impossible_Fallout_2018_1080p.mkv");

        Assert.Equal(dotted.CleanTitle, spaced.CleanTitle);
        Assert.Equal(dotted.CleanTitle, underscored.CleanTitle);
        Assert.Equal(2018, spaced.Year);
    }

    [Fact]
    public void Keeps_the_raw_name_it_was_given()
    {
        var parsed = NameNormalizer.Parse("The.Matrix.1999.mkv");

        Assert.Equal("The.Matrix.1999.mkv", parsed.Raw);
    }
}
