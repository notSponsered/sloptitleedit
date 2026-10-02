using Nikse.SubtitleEdit.UiLogic.Project;

namespace LibUiLogicTests.Project;

public class EpisodeMetadataTests
{
    private const string TvMazeSearch = """
        [{"score":0.9,"show":{"id":123,"name":"The Legend of Luo Xiaohei","premiered":"2011-03-11",
          "externals":{"tvrage":null,"thetvdb":334455,"imdb":"tt1234567"}}}]
        """;

    private const string TvMazeEpisodes = """
        [{"id":1,"name":"Pilot","season":1,"number":1,"airdate":"2011-03-11"},
         {"id":2,"name":"Two","season":1,"number":2,"airdate":"2011-03-18"},
         {"id":3,"name":"Special","season":1,"number":null,"airdate":"2011-03-20"},
         {"id":4,"name":"Three","season":1,"number":3,"airdate":"2011-03-25"},
         {"id":5,"name":"Four","season":1,"number":4,"airdate":"2011-04-01"},
         {"id":6,"name":"Next","season":2,"number":1,"airdate":"2012-01-01"}]
        """;

    private const string AniListSearch = """
        {"data":{"Page":{"media":[{"id":99,"title":{"romaji":"Luo Xiaohei Zhanji","english":null},"startDate":{"year":2019},
          "episodes":3,"streamingEpisodes":[{"title":"Episode 2 - Second"},{"title":"Episode 1 - First"}]}]}}}
        """;

    [Fact]
    public void ParseTvMaze_SearchAndEpisodes()
    {
        var show = Assert.Single(EpisodeMetadata.ParseTvMazeSearch(TvMazeSearch));
        Assert.Equal((123, "The Legend of Luo Xiaohei", 2011, "334455", "tt1234567"), (show.Id, show.Name, show.Year, show.TvdbId, show.ImdbId));

        var episodes = EpisodeMetadata.ParseTvMazeEpisodes(TvMazeEpisodes);
        Assert.Equal(5, episodes.Count);
        Assert.Equal(new EpisodeInfo(2, 1, 5, "Next", "2012-01-01"), episodes[^1]);
    }

    [Fact]
    public void ParseAniList_UsesStreamingTitles()
    {
        var show = Assert.Single(EpisodeMetadata.ParseAniListSearch(AniListSearch));
        Assert.Equal(("Luo Xiaohei Zhanji", 2019), (show.Name, show.Year));
        Assert.Equal(["First", "Second", ""], show.Episodes!.Select(e => e.Title));
        Assert.Equal(3, show.Episodes![2].Absolute);
    }

    [Fact]
    public void MergeInto_RangeTitles_KeepsMappings_AddsMissing()
    {
        var project = new SeProject();
        var entry = new ProjectEpisode { Season = 1, Episode = 1, EpisodeEnd = 3, Video = "v.mkv", Subtitle = "s.ass" };
        entry.Tokens["Release Group"] = "VS";
        project.Episodes.Add(entry);

        var show = EpisodeMetadata.ParseTvMazeSearch(TvMazeSearch)[0];
        EpisodeMetadata.MergeInto(project, show, EpisodeMetadata.ParseTvMazeEpisodes(TvMazeEpisodes));

        Assert.Equal("The Legend of Luo Xiaohei", project.SeriesTitle);
        Assert.Equal(123, project.TvmazeId);
        Assert.Equal("Pilot.Two.Three", entry.Title); // default title word separator "."
        Assert.Equal(".", project.TitlesFormattedWith);
        Assert.Equal("2011-03-11", entry.AirDate);
        Assert.Equal(("v.mkv", "s.ass", "VS"), (entry.Video, entry.Subtitle, entry.Tokens["Release Group"]));
        Assert.Equal(["S01E01-03", "S01E04", "S02E01"], project.Episodes.Select(e => e.Tag));
    }

    [Theory]
    [InlineData(".", "Meow + Escape + Hey! + Flower Elf", "Meow.Escape.Hey!.Flower.Elf")]
    [InlineData("_", "  Deep Mountain +Listen ", "Deep_Mountain_Listen")]
    [InlineData("", "Meow + Escape", "Meow + Escape")]
    [InlineData(".", "Dr. Stone + Wait... What?", "Dr.Stone.Wait...What?")] // no doubled separator
    [InlineData("$$", "Meow Escape", "Meow$$Escape")] // used literally, not as a regex substitution
    public void FormatTitle_ReplacesSpacesAndPlusJoins(string separator, string title, string expected)
    {
        Assert.Equal(expected, new SeProject { TitleSeparator = separator }.FormatTitle(title));
    }

    [Fact]
    public void ReformatTitles_SwitchesSeparators_AndIsRepeatable()
    {
        var project = new SeProject();
        project.Episodes.Add(new ProjectEpisode { Title = "Meow + Escape" });
        var ep = project.Episodes[0];

        project.ReformatTitles();
        Assert.Equal("Meow.Escape", ep.Title);

        project.TitleSeparator = "_";
        project.ReformatTitles();
        project.ReformatTitles();
        Assert.Equal("Meow_Escape", ep.Title);

        project.TitleSeparator = " - ";
        project.ReformatTitles();
        project.ReformatTitles();
        Assert.Equal("Meow - Escape", ep.Title);

        project.TitleSeparator = string.Empty;
        project.ReformatTitles();
        Assert.Equal("Meow Escape", ep.Title);
    }
}
