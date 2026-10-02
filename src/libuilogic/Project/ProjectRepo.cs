using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.UiLogic.Project;

/// <param name="Path">Full path of the series folder (its "Season N" folders or episode files are inside).</param>
public record SeriesFolder(string Path, string Name, int Files, int Seasons);

public record ConnectResult(SeProject Project, int Matched, int Copied, int Added);

/// <summary>
/// Projects and repositories: build a project from a cloned repo ("import"), or move an existing local project onto a
/// clone, matching its episodes to the repo's files ("connect"). Pure file work; cloning is the caller's job.
/// </summary>
public static class ProjectRepo
{
    private static readonly Regex RemoteRegex = new(@"github\.com[/:](?<owner>[^/\s]+)/(?<name>[^/\s]+?)(?:\.git)?/?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>"owner/name" from an https or ssh GitHub remote URL, else null.</summary>
    public static string? ParseRepoName(string remoteUrl)
    {
        var m = RemoteRegex.Match(remoteUrl.Trim());
        return m.Success ? $"{m.Groups["owner"].Value}/{m.Groups["name"].Value}" : null;
    }

    /// <summary>Numbered .ass files below <paramref name="folder"/> (recursive, .git skipped).</summary>
    public static List<(string Path, EpisodeNumber Number)> EpisodeFiles(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        var gitDir = System.IO.Path.DirectorySeparatorChar + ".git" + System.IO.Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(folder, "*.ass", options)
            .Where(f => !f.Contains(gitDir, StringComparison.Ordinal))
            .Select(f => (Path: f, Number: SonarrNaming.ParseEpisodeNumber(System.IO.Path.GetRelativePath(folder, f))))
            .Where(x => x.Number != null)
            .Select(x => (x.Path, x.Number!))
            .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Folders in a repo that hold a series: the parent of "Season N" folders, or a folder with numbered episode
    /// files directly in it. Most files first.
    /// </summary>
    public static List<SeriesFolder> FindSeriesFolders(string root)
    {
        return EpisodeFiles(root)
            .GroupBy(f => SeriesRoot(System.IO.Path.GetDirectoryName(f.Path)!), StringComparer.OrdinalIgnoreCase)
            .Select(g => new SeriesFolder(g.Key, System.IO.Path.GetFileName(g.Key), g.Count(), g.Select(f => f.Number.Season).Distinct().Count()))
            .OrderByDescending(s => s.Files)
            .ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        static string SeriesRoot(string dir) =>
            Regex.IsMatch(System.IO.Path.GetFileName(dir), @"^(Season\s*\d+|S\d{1,2}|Specials)$", RegexOptions.IgnoreCase)
                ? System.IO.Path.GetDirectoryName(dir)!
                : dir;
    }

    /// <summary>New project on a series folder of a repo, every numbered file mapped; GitHub switched on.</summary>
    public static SeProject CreateFromFolder(string seriesFolder)
    {
        var name = System.IO.Path.GetFileName(seriesFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar));
        var project = new SeProject { Folder = seriesFolder, Name = name, SeriesTitle = name, UseGitHub = true };
        project.MapSubtitles(seriesFolder);
        return project;
    }

    /// <summary>
    /// Moves <paramref name="local"/> onto <paramref name="seriesFolder"/> (inside a clone): each episode points at the
    /// repo file with the same season/episode. With <paramref name="useLocalFiles"/> a differing local file is copied
    /// over the repo version (it then shows as a change); local files the repo lacks are copied in. Repo files no
    /// episode matched become new entries. The local folder itself is left untouched.
    /// </summary>
    public static ConnectResult ConnectTo(SeProject local, string seriesFolder, bool useLocalFiles)
    {
        var project = local.Clone();
        project.Folder = seriesFolder;
        project.UseGitHub = true;
        var repoFiles = EpisodeFiles(seriesFolder);
        int matched = 0, copied = 0, added = 0;

        foreach (var ep in project.Episodes)
        {
            var localPath = local.ResolvePath(ep.Subtitle);
            var hasLocal = localPath.Length > 0 && File.Exists(localPath);
            var match = repoFiles.FirstOrDefault(f => f.Number.Season == ep.Season && f.Number.Episode == ep.Episode).Path;
            if (match != null)
            {
                if (useLocalFiles && hasLocal && !SameContent(localPath, match))
                {
                    File.Copy(localPath, match, true);
                    copied++;
                }

                ep.Subtitle = project.MakeRelative(match);
                matched++;
            }
            else if (hasLocal)
            {
                var relative = System.IO.Path.GetRelativePath(local.Folder, localPath);
                var target = System.IO.Path.Combine(seriesFolder, relative.StartsWith("..", StringComparison.Ordinal) ? System.IO.Path.GetFileName(localPath) : relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                if (!File.Exists(target))
                {
                    File.Copy(localPath, target);
                    added++;
                }

                ep.Subtitle = project.MakeRelative(target);
            }
            else
            {
                ep.Subtitle = string.Empty;
            }
        }

        foreach (var (path, number) in repoFiles)
        {
            var entry = project.GetOrAddEntry(number);
            if (string.IsNullOrEmpty(entry.Subtitle))
            {
                entry.Subtitle = project.MakeRelative(path);
            }
        }

        return new ConnectResult(project, matched, copied, added);
    }

    private static bool SameContent(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
}
