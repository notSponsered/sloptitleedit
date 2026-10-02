using Nikse.SubtitleEdit.UiLogic.Project;

namespace LibUiLogicTests.Project;

public class ProjectRepoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "projectrepo-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private string Write(string relative, string text = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Theory]
    [InlineData("https://github.com/giosann/Fansubs.git", "giosann/Fansubs")]
    [InlineData("https://github.com/giosann/Fansubs", "giosann/Fansubs")]
    [InlineData("git@github.com:notSponsered/se-project-test.git\n", "notSponsered/se-project-test")]
    [InlineData("https://gitlab.com/a/b.git", null)]
    public void ParseRepoName(string url, string? expected)
    {
        Assert.Equal(expected, ProjectRepo.ParseRepoName(url));
    }

    [Fact]
    public void FindSeriesFolders_UsesParentOfSeasonFolders()
    {
        Write(@"repo/Luo Xiaohei/Season 1/The.Legend.of.Luo.Xiao.Hei.S01E01-04.1080p English [VS].ass");
        Write(@"repo/Luo Xiaohei/Season 1/The.Legend.of.Luo.Xiao.Hei.S01E05-07.1080p English [VS].ass");
        Write(@"repo/Luo Xiaohei/Season 2/The.Legend.of.Luo.Xiao.Hei.S02E01-03.1080p English [CC].ass");
        Write(@"repo/Other Show/Other.S01E01.ass");
        Write(@"repo/README.md");
        Write(@"repo/.git/x/Show.S01E09.ass"); // never

        var found = ProjectRepo.FindSeriesFolders(Path.Combine(_root, "repo"));

        Assert.Equal(["Luo Xiaohei", "Other Show"], found.Select(f => f.Name));
        Assert.Equal((3, 2), (found[0].Files, found[0].Seasons));
        Assert.Equal(Path.Combine(_root, "repo", "Luo Xiaohei"), found[0].Path);
    }

    [Fact]
    public void CreateFromFolder_MapsRangesAndTurnsGitHubOn()
    {
        Write(@"repo/Show/Season 1/Show.S01E01-04.ass");
        Write(@"repo/Show/Season 1/Show.S01E05-07.ass");

        var project = ProjectRepo.CreateFromFolder(Path.Combine(_root, "repo", "Show"));

        Assert.True(project.UseGitHub);
        Assert.Equal("Show", project.Name);
        Assert.Equal(["S01E01-04", "S01E05-07"], project.Episodes.Select(e => e.Tag));
        Assert.Equal("Season 1/Show.S01E05-07.ass", project.Episodes[1].Subtitle);
    }

    [Fact]
    public void ConnectTo_MatchesCopiesAndKeepsLocalUntouched()
    {
        // local project: E01-04 edited locally, E08-10 only local; repo: E01-04 (different) and E05-07
        var localFolder = Path.Combine(_root, "local");
        var localE1 = Write(@"local/Show S01E01-04 work.ass", "local edit");
        var localE8 = Write(@"local/Show S01E08-10 work.ass", "only local");
        var local = new SeProject { Folder = localFolder, Name = "Show", Notes = "my notes" };
        local.Episodes.Add(new ProjectEpisode { Season = 1, Episode = 1, EpisodeEnd = 4, Subtitle = local.MakeRelative(localE1), Video = @"D:\v1.mkv" });
        local.Episodes.Add(new ProjectEpisode { Season = 1, Episode = 8, EpisodeEnd = 10, Subtitle = local.MakeRelative(localE8) });
        local.Episodes[0].Tokens["Release Group"] = "VS";
        var repoE1 = Write(@"repo/Show/Season 1/Show.S01E01-04.ass", "repo version");
        Write(@"repo/Show/Season 1/Show.S01E05-07.ass", "repo 5-7");
        var series = Path.Combine(_root, "repo", "Show");

        var result = ProjectRepo.ConnectTo(local, series, useLocalFiles: true);
        var p = result.Project;

        Assert.Equal((1, 1, 1), (result.Matched, result.Copied, result.Added));
        Assert.Equal(series, p.Folder);
        Assert.True(p.UseGitHub);
        Assert.Equal("my notes", p.Notes);
        Assert.Equal(["S01E01-04", "S01E05-07", "S01E08-10"], p.Episodes.Select(e => e.Tag));
        Assert.Equal("Season 1/Show.S01E01-04.ass", p.Episodes[0].Subtitle);
        Assert.Equal(@"D:\v1.mkv", p.Episodes[0].Video);
        Assert.Equal("VS", p.Episodes[0].Tokens["Release Group"]);
        Assert.Equal("local edit", File.ReadAllText(repoE1));
        Assert.Equal("only local", File.ReadAllText(p.ResolvePath(p.Episodes[2].Subtitle)));
        Assert.Equal(localFolder, local.Folder); // original project object and files untouched
        Assert.Equal("local edit", File.ReadAllText(localE1));
    }

    [Fact]
    public void ConnectTo_WithoutLocalFiles_KeepsRepoVersions()
    {
        var localE1 = Write(@"local/a S01E01.ass", "local edit");
        var local = new SeProject { Folder = Path.Combine(_root, "local") };
        local.Episodes.Add(new ProjectEpisode { Season = 1, Episode = 1, Subtitle = local.MakeRelative(localE1) });
        var repoE1 = Write(@"repo/S/Season 1/S.S01E01.ass", "repo version");

        var result = ProjectRepo.ConnectTo(local, Path.Combine(_root, "repo", "S"), useLocalFiles: false);

        Assert.Equal((1, 0, 0), (result.Matched, result.Copied, result.Added));
        Assert.Equal("repo version", File.ReadAllText(repoE1));
    }
}
