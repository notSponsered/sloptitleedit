using Nikse.SubtitleEdit.UiLogic.Project;

namespace LibUiLogicTests.Project;

public class SonarrNamingTests
{
    private const string RepoTemplate = "Season {season}/{Series.CleanTitle}.S{season:00}E{episode:00}.{Release} {Language} {[Release Group]}";

    private static SeProject LuoXiaohei()
    {
        var project = new SeProject { SeriesTitle = "The Legend of Luo Xiao Hei", SeriesYear = 2019, NamingTemplate = RepoTemplate };
        project.Tokens["Language"] = "English";
        project.SeasonTokens[1] = new() { ["Release Group"] = "VS", ["Release"] = "1080p.BluRay.Remux.FLAC2.0.AVC" };
        project.SeasonTokens[2] = new() { ["Release Group"] = "Cinema Centuries", ["Release"] = "1080p.BluRay.Remux.FLAC2.0.AVC" };
        return project;
    }

    [Fact]
    public void Format_ReferenceRepoNames()
    {
        var project = LuoXiaohei();
        Assert.Equal("Season 1/The.Legend.of.Luo.Xiao.Hei.S01E01-04.1080p.BluRay.Remux.FLAC2.0.AVC English [VS]",
            SonarrNaming.Format(RepoTemplate, new ProjectEpisode { Season = 1, Episode = 1, EpisodeEnd = 4 }, project));
        Assert.Equal("Season 2/The.Legend.of.Luo.Xiao.Hei.S02E10-12.1080p.BluRay.Remux.FLAC2.0.AVC English [Cinema Centuries]",
            SonarrNaming.Format(RepoTemplate, new ProjectEpisode { Season = 2, Episode = 10, EpisodeEnd = 12 }, project));
    }

    [Fact]
    public void GetToken_EpisodeOverridesSeasonOverridesProject()
    {
        var project = LuoXiaohei();
        project.Tokens["Release Group"] = "Project";
        var ep = new ProjectEpisode { Season = 1, Episode = 5 };
        Assert.Equal("VS", project.GetToken(ep, "release.group"));
        ep.Tokens["Release Group"] = "Mine";
        Assert.Equal("Mine", project.GetToken(ep, "Release Group"));
        Assert.Equal("Project", project.GetToken(new ProjectEpisode { Season = 3 }, "Release Group"));
    }

    [Fact]
    public void Format_TrashStandard_DropsEmptyBlocks()
    {
        var project = new SeProject { SeriesTitle = "Show", SeriesYear = 2024 };
        var ep = new ProjectEpisode { Season = 1, Episode = 3, Title = "Who's There?: Part 1" };
        Assert.Equal("Show (2024) - S01E03 - Whos There Part 1", SonarrNaming.Format(SonarrNaming.Presets[0].Template, ep, project));

        project.Tokens["Quality Full"] = "WEBDL-1080p";
        project.Tokens["Release Group"] = "GRP";
        project.Tokens["MediaInfo AudioCodec"] = "AAC";
        project.Tokens["MediaInfo AudioChannels"] = "2.0";
        Assert.Equal("Show (2024) - S01E03 - Whos There Part 1 [WEBDL-1080p][AAC 2.0]-GRP", SonarrNaming.Format(SonarrNaming.Presets[0].Template, ep, project));
    }

    [Fact]
    public void Format_P2P_UsesDots()
    {
        var project = new SeProject { SeriesTitle = "My Show", SeriesYear = 2020 };
        project.Tokens["Quality Full"] = "Bluray-1080p Remux";
        var ep = new ProjectEpisode { Season = 2, Episode = 5, Title = "The End" };
        Assert.Equal("My.Show.2020.S02E05.The.End.Bluray-1080p.Remux", SonarrNaming.Format(SonarrNaming.Presets[3].Template, ep, project));
    }

    [Theory]
    [InlineData("Range", "S01E01-04")]
    [InlineData("Prefixed Range", "S01E01-E04")]
    [InlineData("Extend", "S01E01-02-03-04")]
    [InlineData("Repeat", "S01E01E02E03E04")]
    [InlineData("Scene", "S01E01-E02-E03-E04")]
    public void Format_MultiEpisodeStyles(string style, string expected)
    {
        var project = new SeProject { MultiEpisodeStyle = style };
        Assert.Equal(expected, SonarrNaming.Format("S{season:00}E{episode:00}", new ProjectEpisode { Season = 1, Episode = 1, EpisodeEnd = 4 }, project));
    }

    [Fact]
    public void Format_AnimeAbsoluteRange_AndTruncation()
    {
        var project = new SeProject { SeriesTitle = "Anime" };
        var ep = new ProjectEpisode { Season = 1, Episode = 1, EpisodeEnd = 4, Absolute = 1, Title = new string('x', 100) };
        var name = SonarrNaming.Format(SonarrNaming.Presets[1].Template, ep, project);
        Assert.StartsWith("Anime - S01E01-04 - 001-004 - " + new string('x', 90) + "...", name);
        Assert.Equal("[bit]", name.Substring(name.Length - 5));
    }

    [Theory]
    [InlineData(@"Luo Xiaohei\Season 1\The.Legend.of.Luo.Xiao.Hei.S01E01-04.1080p.BluRay.Remux.FLAC2.0.AVC English [VS].ass", 1, 1, 4, null)]
    [InlineData("Show.S01E01-E04.mkv", 1, 1, 4, null)]
    [InlineData("Show.S01E01E02E03.mkv", 1, 1, 3, null)]
    [InlineData("Show.S02E05.1080p.mkv", 2, 5, null, null)]
    [InlineData("Show.S02E05-1080p.mkv", 2, 5, null, null)]
    [InlineData("Show 1x03.ass", 1, 3, null, null)]
    [InlineData("[Group] Show - 07 [1080p].mkv", 1, 7, null, 7)]
    [InlineData(@"Show\Season 2\Show - 03.ass", 2, 3, null, null)]
    public void ParseEpisodeNumber(string path, int season, int episode, int? end, int? absolute)
    {
        Assert.Equal(new EpisodeNumber(season, episode, end, absolute), SonarrNaming.ParseEpisodeNumber(path.Replace('\\', Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void ParseEpisodeNumber_NoNumber_ReturnsNull()
    {
        Assert.Null(SonarrNaming.ParseEpisodeNumber("Movie.1080p.BluRay.mkv"));
    }

    [Fact]
    public void TokensFromVideo_ReadsStreamsAndFileName()
    {
        const string log = """
            Input #0, matroska,webm, from 'Show.S01E01.1080p.BluRay.Remux-GRP.mkv':
              Duration: 00:23:40.02, start: 0.000000, bitrate: 30000 kb/s
              Stream #0:0(jpn): Video: h264 (High), yuv420p(tv, bt709, progressive), 1920x1080 [SAR 1:1 DAR 16:9], 23.98 fps, 23.98 tbr, 1k tbn (default)
              Stream #0:1(jpn): Audio: flac, 48000 Hz, stereo, s16 (default)
              Stream #0:2(eng): Audio: aac (LC), 48000 Hz, 5.1(side), fltp
              Stream #0:3: Video: mjpeg (Baseline), yuvj420p(pc), 600x800, 90k tbr (attached pic)
            """;
        var tokens = SonarrNaming.TokensFromVideo(log, @"D:\Video\Show.S01E01.1080p.BluRay.Remux-GRP.mkv");
        Assert.Equal("Bluray-1080p Remux", tokens["Quality Full"]);
        Assert.Equal("AVC", tokens["MediaInfo VideoCodec"]);
        Assert.Equal("8", tokens["MediaInfo VideoBitDepth"]);
        Assert.Equal("FLAC", tokens["MediaInfo AudioCodec"]);
        Assert.Equal("2.0", tokens["MediaInfo AudioChannels"]);
        Assert.Equal("JPN+ENG", tokens["MediaInfo AudioLanguages"]);
        Assert.Equal("GRP", tokens["Release Group"]);
    }

    [Fact]
    public void TokensFromVideo_HevcTenBitWebDl()
    {
        const string log = "  Stream #0:0[0x1011]: Video: hevc (Main 10), yuv420p10le(tv, bt2020nc/bt2020/smpte2084), 3840x2160, 23.98 fps\n" +
                           "  Stream #0:1[0x1100](eng): Audio: eac3, 48000 Hz, 5.1(side), fltp, 640 kb/s";
        var tokens = SonarrNaming.TokensFromVideo(log, "Show.S01E02.2160p.WEB-DL.x265-ABC.mkv");
        Assert.Equal("WEBDL-2160p", tokens["Quality Full"]);
        Assert.Equal("x265", tokens["MediaInfo VideoCodec"]);
        Assert.Equal("10", tokens["MediaInfo VideoBitDepth"]);
        Assert.Equal("HDR10", tokens["MediaInfo VideoDynamicRangeType"]);
        Assert.Equal("EAC3", tokens["MediaInfo AudioCodec"]);
        Assert.Equal("5.1", tokens["MediaInfo AudioChannels"]);
        Assert.Equal("ABC", tokens["Release Group"]);
    }

    [Fact]
    public void Format_TitleTokens_UsePlainWords_NotTheDisplaySeparator()
    {
        // "Meow - Escape" stored with the "." title separator; naming must still clean it like Sonarr does
        var project = new SeProject { TitlesFormattedWith = "." };
        var ep = new ProjectEpisode { Title = "Meow.-.Escape" };
        Assert.Equal("Meow Escape", SonarrNaming.Format("{Episode CleanTitle}", ep, project));
        Assert.Equal("Meow.Escape", SonarrNaming.Format("{Episode.CleanTitle}", ep, project));
        Assert.Equal("Meow - Escape", SonarrNaming.Format("{Episode Title}", ep, project));
    }
}
