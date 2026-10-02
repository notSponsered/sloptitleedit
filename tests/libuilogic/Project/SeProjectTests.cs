using Nikse.SubtitleEdit.UiLogic.Project;

namespace LibUiLogicTests.Project;

public class SeProjectTests
{
    private const string Ass =
        "[Script Info]\r\nScriptType: v4.00+\r\nPlayResX: 1920\r\nPlayResY: 1080\r\n\r\n" +
        "[V4+ Styles]\r\n" +
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n" +
        "Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,2,10,10,10,0\r\n" +
        "Style: Sign,Arial,30,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,8,10,10,10,0\r\n" +
        "\r\n[Events]\r\n" +
        "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\r\n" +
        "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,Hello\r\n";

    private const string Master =
        "[Script Info]\r\nScriptType: v4.00+\r\n\r\n[V4+ Styles]\r\n" +
        "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\r\n" +
        "Style: Default,Gandhi Sans,60,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,-1,0,-1,0,100,100,0,0,1,3,1,2,40,40,40,1\r\n" +
        "Style: Title,Gandhi Sans,80,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,3,1,8,40,40,40,1\r\n" +
        "\r\n[Events]";

    [Fact]
    public void ApplyStylesToFileText_ReplacesAndAdds_KeepsEventsAndOtherStyles()
    {
        var result = SeProject.ApplyStylesToFileText(Ass, Master);
        var lines = result.Split("\r\n");

        Assert.Contains(lines, l => l.StartsWith("Style: Default,Gandhi Sans,60,", StringComparison.Ordinal) && l.Contains(",-1,0,-1,0,"));
        Assert.Contains(lines, l => l.StartsWith("Style: Title,Gandhi Sans,80,", StringComparison.Ordinal));
        Assert.Contains("Style: Sign,Arial,30,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,1,8,10,10,10,0", lines);
        Assert.Equal(1, lines.Count(l => l.StartsWith("Style: Default,", StringComparison.Ordinal)));
        Assert.True(Array.IndexOf(lines, lines.First(l => l.StartsWith("Style: Title,", StringComparison.Ordinal))) <
                    Array.IndexOf(lines, "[Events]"));

        var eventsAt = Ass.IndexOf("[Events]", StringComparison.Ordinal);
        Assert.EndsWith(Ass.Substring(eventsAt), result);
        Assert.StartsWith(Ass.Substring(0, Ass.IndexOf("Style: Default", StringComparison.Ordinal)), result);
    }

    [Fact]
    public void ApplyStylesToFileText_KeepsLfLineEndings()
    {
        var result = SeProject.ApplyStylesToFileText(Ass.Replace("\r\n", "\n"), Master);
        Assert.DoesNotContain("\r", result);
    }

    [Fact]
    public void SaveLoad_RoundTrip_KeepsEntriesTokensAndRelativePaths()
    {
        var folder = Path.Combine(Path.GetTempPath(), "seproject-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = new SeProject { Folder = folder, Name = "Luo Xiaohei", SeriesYear = 2019 };
            project.SeasonTokens[1] = new() { ["Release Group"] = "VS" };
            var ep = new ProjectEpisode { Season = 1, Episode = 1, EpisodeEnd = 4, Video = @"D:\Video\a.mkv" };
            ep.Subtitle = project.MakeRelative(Path.Combine(folder, "Season 1", "a.ass"));
            ep.Tokens["Language"] = "English";
            project.Episodes.Add(ep);
            project.Save();

            var loaded = SeProject.Load(folder)!;
            Assert.Equal("Luo Xiaohei", loaded.Name);
            Assert.Equal(2019, loaded.SeriesYear);
            Assert.Equal("VS", loaded.SeasonTokens[1]["Release Group"]);
            var e = Assert.Single(loaded.Episodes);
            Assert.Equal("S01E01-04", e.Tag);
            Assert.Equal("Season 1/a.ass", e.Subtitle);
            Assert.Equal("English", e.Tokens["Language"]);
            Assert.Equal(Path.Combine(folder, "Season 1", "a.ass"), loaded.ResolvePath(e.Subtitle));
            Assert.Same(e, loaded.FindBySubtitle(Path.Combine(folder, "Season 1", "a.ass")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void EnsureExcludeLine_IsIdempotent()
    {
        var file = Path.Combine(Path.GetTempPath(), "seproject-exclude-" + Guid.NewGuid().ToString("N"), "info", "exclude");
        try
        {
            File.WriteAllText(PrepareDir(file), "# git ls-files --others --exclude-from=.git/info/exclude");
            SeProject.EnsureExcludeLine(file);
            SeProject.EnsureExcludeLine(file);
            Assert.Equal(["# git ls-files --others --exclude-from=.git/info/exclude", SeProject.FileName], File.ReadAllLines(file));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(file))!, true);
        }

        static string PrepareDir(string f)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            return f;
        }
    }
}
