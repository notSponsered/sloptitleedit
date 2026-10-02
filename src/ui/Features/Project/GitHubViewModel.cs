using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.UiLogic.Project;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Project;

/// <summary>What the GitHub panel can show; everything but Ready is a guided empty state.</summary>
public enum GitHubState
{
    NothingOpen,
    NeedsGh,
    NeedsLogin,
    NotRepo,
    Ready,
}

public partial class ChangedFileRow : ObservableObject
{
    [ObservableProperty] private bool _isChecked = true;
    public string Episode { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string Change { get; init; } = string.Empty;

    /// <summary>Repo-root relative paths to stage (a rename includes its old path).</summary>
    public string[] Paths { get; init; } = [];

    public string Key => string.Join('|', Paths);
}

public record PrRow(int Number, string Title, string Author, string Branch, string Updated)
{
    public string Heading => $"#{Number} {Title}";
    public string Byline => string.Format(Se.Language.Project.ByXFromY, Author, Branch);
}

/// <summary>
/// git + GitHub CLI (gh) for one folder: the open project's folder or the open subtitle file's folder.
/// Changes → commit → new pull request / push to the checked-out one; others' pull requests → check out / merge.
/// All git commands run in the repo root; changed files are limited to the folder. Knows nothing about projects.
/// </summary>
public partial class GitHubViewModel : ObservableObject
{
    public Window? Window { get; set; }

    private string _folder = string.Empty;
    private string _repoRoot = string.Empty;
    private Func<Task<bool>> _beforeWorkingTreeChange = () => Task.FromResult(true);
    private Func<Task> _afterWorkingTreeChange = () => Task.CompletedTask;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsReady), nameof(IsNothingOpen), nameof(IsNotRepo), nameof(NeedsSetup), nameof(SetupTitle), nameof(SetupBody), nameof(SetupCommands))]
    private GitHubState _state = GitHubState.NothingOpen;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOnBase), nameof(BranchNote), nameof(SwitchToBaseText), nameof(ShowNewBranch))]
    private string _branch = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOnBase), nameof(BranchNote), nameof(SwitchToBaseText), nameof(ShowNewBranch))]
    private string _baseBranch = "main";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPr), nameof(BranchNote), nameof(PrimaryText), nameof(ShowNewPrFields), nameof(ShowNewBranch))]
    private PrRow? _currentPr;

    [ObservableProperty] private string _commitMessage = string.Empty;
    [ObservableProperty] private string _newBranch = string.Empty;
    [ObservableProperty] private string _prTitle = string.Empty;
    [ObservableProperty] private string _prBody = string.Empty;
    [ObservableProperty] private string _log = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _changesSummary = string.Empty;
    [ObservableProperty] private string _prsSummary = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedPr))] private PrRow? _selectedPr;
    [ObservableProperty] private string _cloneRepo = string.Empty;
    [ObservableProperty] private string _cloneFolder = string.Empty;

    public bool IsReady => State == GitHubState.Ready;
    public bool IsNothingOpen => State == GitHubState.NothingOpen;
    public bool IsNotRepo => State == GitHubState.NotRepo;
    public bool NeedsSetup => State is GitHubState.NeedsGh or GitHubState.NeedsLogin;
    public bool HasPr => CurrentPr != null;
    public bool HasSelectedPr => SelectedPr != null;
    public bool IsOnBase => Branch == BaseBranch;
    public bool ShowNewPrFields => !HasPr;
    public bool ShowNewBranch => !HasPr && IsOnBase;

    public string BranchNote => HasPr
        ? string.Format(Se.Language.Project.PullRequestXY, CurrentPr!.Number, CurrentPr.Title)
        : IsOnBase ? Se.Language.Project.BaseBranchNote : Se.Language.Project.NoPullRequestYet;

    public string SwitchToBaseText => string.Format(Se.Language.Project.SwitchToX, BaseBranch);
    public string PrimaryText => HasPr ? string.Format(Se.Language.Project.PushToPullRequestX, CurrentPr!.Number) : Se.Language.Project.CreatePullRequest;
    public string SetupTitle => State == GitHubState.NeedsGh ? Se.Language.Project.ConnectTitle : Se.Language.Project.SignInTitle;
    public string SetupBody => State == GitHubState.NeedsGh ? Se.Language.Project.GhMissingBody : Se.Language.Project.SignInBody;
    public string SetupCommands => State == GitHubState.NeedsGh ? "winget install --id GitHub.cli -e" + Environment.NewLine + "gh auth login" : "gh auth login";
    public string NotRepoBody => string.Format(Se.Language.Project.NotRepoBodyX, _folder);

    public ObservableCollection<ChangedFileRow> ChangedFiles { get; } = [];
    public ObservableCollection<PrRow> OpenPrs { get; } = [];

    /// <param name="folder">The folder to work in (project folder or the open file's folder); null = nothing open.</param>
    public void Initialize(string? folder, string defaultMessage, string defaultTitle, Func<Task<bool>> beforeWorkingTreeChange, Func<Task> afterWorkingTreeChange)
    {
        _folder = folder ?? string.Empty;
        _beforeWorkingTreeChange = beforeWorkingTreeChange;
        _afterWorkingTreeChange = afterWorkingTreeChange;
        CommitMessage = defaultMessage;
        PrTitle = defaultTitle;
        var slug = new string(defaultMessage.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        NewBranch = $"se/{(slug.Length == 0 ? "update" : slug)}-{DateTime.Now:yyyyMMdd-HHmm}";
        CloneFolder = Directory.Exists(_folder) && !Directory.EnumerateFileSystemEntries(_folder).Any() ? _folder : string.Empty;
        OnPropertyChanged(nameof(NotRepoBody));
        Dispatcher.UIThread.Post(async void () => await Refresh());
    }

    private async Task<ProcessResult> Git(params string[] args) => await RunLogged(false, args);

    private async Task<ProcessResult> Gh(params string[] args) => await RunLogged(true, args);

    private async Task<ProcessResult> RunLogged(bool gh, string[] args)
    {
        var dir = _repoRoot.Length > 0 ? _repoRoot : _folder;
        var result = gh
            ? await ProcessRunner.Gh(dir, CancellationToken.None, args)
            : await ProcessRunner.Git(dir, CancellationToken.None, args);
        var output = (result.StdOut + result.StdErr).Trim();
        Log += $"> {(gh ? "gh" : "git")} {string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}" + Environment.NewLine +
               (output.Length > 0 ? output + Environment.NewLine : string.Empty);
        return result;
    }

    private async Task<bool> Check(ProcessResult result)
    {
        if (!result.Ok && Window != null)
        {
            await MessageBox.Show(Window, Se.Language.Project.GitError, result.Message, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        return result.Ok;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        IsBusy = true;
        try
        {
            if (_folder.Length == 0 || !Directory.Exists(_folder))
            {
                State = GitHubState.NothingOpen;
                return;
            }

            var version = await ProcessRunner.Gh(_folder, CancellationToken.None, "--version");
            if (version.NotFound)
            {
                State = GitHubState.NeedsGh;
                return;
            }

            if (!(await ProcessRunner.Gh(_folder, CancellationToken.None, "auth", "status")).Ok)
            {
                State = GitHubState.NeedsLogin;
                return;
            }

            _repoRoot = await ProcessRunner.GetRepoRoot(_folder) ?? string.Empty;
            if (_repoRoot.Length == 0)
            {
                State = GitHubState.NotRepo;
                return;
            }

            State = GitHubState.Ready;
            Branch = (await Git("branch", "--show-current")).StdOut.Trim();
            BaseBranch = await DetectBaseBranch();
            await LoadChangedFiles();

            var pr = await Gh("pr", "view", "--json", "number,title,author,headRefName,updatedAt");
            using (var doc = pr.Ok ? JsonDocument.Parse(pr.StdOut) : null)
            {
                CurrentPr = doc == null ? null : ParsePr(doc.RootElement);
            }

            var list = await Gh("pr", "list", "--state", "open", "--limit", "50", "--json", "number,title,author,headRefName,updatedAt");
            var selected = SelectedPr?.Number;
            OpenPrs.Clear();
            if (list.Ok)
            {
                foreach (var row in ParsePrList(list.StdOut))
                {
                    OpenPrs.Add(row);
                }
            }

            SelectedPr = OpenPrs.FirstOrDefault(p => p.Number == selected);
            PrsSummary = OpenPrs.Count == 0 ? string.Empty : string.Format(Se.Language.Project.XOpen, OpenPrs.Count);
        }
        catch (Exception e)
        {
            Log += e.Message + Environment.NewLine;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<string> DetectBaseBranch()
    {
        var gh = await Gh("repo", "view", "--json", "defaultBranchRef", "--jq", ".defaultBranchRef.name");
        if (gh.Ok && gh.StdOut.Trim().Length > 0)
        {
            return gh.StdOut.Trim();
        }

        var head = await Git("symbolic-ref", "--short", "refs/remotes/origin/HEAD");
        return head.Ok && head.StdOut.Trim().StartsWith("origin/", StringComparison.Ordinal) ? head.StdOut.Trim().Substring(7) : "main";
    }

    internal static List<PrRow> ParsePrList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select(ParsePr).ToList();
    }

    private static PrRow ParsePr(JsonElement e)
    {
        var author = e.TryGetProperty("author", out var a) && a.TryGetProperty("login", out var login) ? login.GetString() ?? string.Empty : string.Empty;
        var updated = e.TryGetProperty("updatedAt", out var u) && DateTime.TryParse(u.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : string.Empty;
        return new PrRow(e.GetProperty("number").GetInt32(), e.GetProperty("title").GetString() ?? string.Empty, author,
            e.GetProperty("headRefName").GetString() ?? string.Empty, updated);
    }

    private async Task LoadChangedFiles()
    {
        var unticked = ChangedFiles.Where(f => !f.IsChecked).Select(f => f.Key).ToHashSet();
        ChangedFiles.Clear();
        var scope = Path.GetRelativePath(_repoRoot, _folder);
        var status = await Git("-c", "core.quotepath=false", "status", "--porcelain", "--untracked-files=all", "--", scope);
        if (status.Ok)
        {
            foreach (var line in status.StdOut.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 3))
            {
                var paths = line.Substring(3).Split(" -> ").Select(p => p.Trim('"')).ToArray();
                if (paths.Any(p => p.EndsWith(SeProject.FileName, StringComparison.Ordinal)))
                {
                    continue;
                }

                var path = paths[^1];
                var episode = SonarrNaming.ParseEpisodeNumber(path);
                var row = new ChangedFileRow
                {
                    Paths = paths,
                    FileName = Path.GetFileName(path),
                    Episode = episode == null || episode.Absolute != null
                        ? string.Empty
                        : new ProjectEpisode { Season = episode.Season, Episode = episode.Episode, EpisodeEnd = episode.EpisodeEnd }.Tag,
                    Change = ChangeName(line.Substring(0, 2)),
                };
                row.IsChecked = !unticked.Contains(row.Key);
                ChangedFiles.Add(row);
            }
        }

        ChangesSummary = ChangedFiles.Count switch
        {
            0 => string.Empty,
            1 => Se.Language.Project.OneFile,
            var n => string.Format(Se.Language.Project.XFiles, n),
        };
    }

    private static string ChangeName(string xy)
    {
        var l = Se.Language.Project;
        return xy switch
        {
            "??" => l.ChangeNew,
            _ when xy.Contains('R') => l.ChangeRenamed,
            _ when xy.Contains('D') => l.ChangeDeleted,
            _ when xy.Contains('A') => l.ChangeNew,
            _ => l.ChangeEdited,
        };
    }

    /// <summary>Stage + commit the ticked files. True when committed or when there was nothing to commit.</summary>
    private async Task<bool> CommitChecked()
    {
        // the list may be stale (the episode was just saved by the before-change prompt)
        await LoadChangedFiles();
        var paths = ChangedFiles.Where(f => f.IsChecked).SelectMany(f => f.Paths).Distinct().ToArray();
        if (paths.Length == 0)
        {
            return true;
        }

        await EnsureCommitIdentity();
        var message = string.IsNullOrWhiteSpace(CommitMessage) ? "Update subtitles" : CommitMessage.Trim();
        return await Check(await Git(["add", "-A", "--", .. paths])) &&
               await Check(await Git(["commit", "-m", message, "--", .. paths]));
    }

    /// <summary>
    /// git refuses to commit without user.name/user.email. When none is configured, set them for this repo only
    /// from the gh account, with GitHub's private noreply address.
    /// </summary>
    private async Task EnsureCommitIdentity()
    {
        var hasName = (await ProcessRunner.Git(_repoRoot, CancellationToken.None, "config", "user.name")).Ok;
        var hasEmail = (await ProcessRunner.Git(_repoRoot, CancellationToken.None, "config", "user.email")).Ok;
        if (hasName && hasEmail)
        {
            return;
        }

        var user = await Gh("api", "user", "--jq", ".id,.login");
        var parts = user.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!user.Ok || parts.Length < 2)
        {
            return; // commit will fail with git's own explanation
        }

        if (!hasName)
        {
            await Git("config", "user.name", parts[1]);
        }

        if (!hasEmail)
        {
            await Git("config", "user.email", $"{parts[0]}+{parts[1]}@users.noreply.github.com");
        }
    }

    private async Task<bool> RunWorkingTreeChange(Func<Task<bool>> action)
    {
        if (IsBusy || !await _beforeWorkingTreeChange())
        {
            return false;
        }

        IsBusy = true;
        bool ok;
        try
        {
            ok = await action();
        }
        finally
        {
            IsBusy = false;
        }

        await _afterWorkingTreeChange();
        await Refresh();
        return ok;
    }

    private async Task Inform(string title, string message)
    {
        if (Window != null)
        {
            await MessageBox.Show(Window, title, message, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    [RelayCommand]
    private async Task PullLatest()
    {
        await RunWorkingTreeChange(async () => await Check(await Git("pull", "--ff-only")));
    }

    [RelayCommand]
    private async Task SwitchToBase()
    {
        await RunWorkingTreeChange(async () =>
            await Check(await Git("switch", BaseBranch)) && await Check(await Git("pull", "--ff-only")));
    }

    /// <summary>The one main action: push to the checked-out pull request, or create one.</summary>
    [RelayCommand]
    private async Task Primary()
    {
        if (HasPr)
        {
            await PushToPr();
        }
        else
        {
            await SendNewPr();
        }
    }

    [RelayCommand]
    private async Task SendNewPr()
    {
        string? url = null;
        await RunWorkingTreeChange(async () =>
        {
            var branch = Branch;
            if (string.IsNullOrEmpty(branch) || branch == BaseBranch)
            {
                branch = NewBranch.Trim();
                if (branch.Length == 0 || !await Check(await Git("switch", "-c", branch)))
                {
                    return false;
                }
            }

            if (!await CommitChecked() || !await Check(await Git("push", "-u", "origin", branch)))
            {
                return false;
            }

            var title = string.IsNullOrWhiteSpace(PrTitle) ? CommitMessage : PrTitle.Trim();
            var create = await Gh("pr", "create", "--base", BaseBranch, "--head", branch, "--title", title, "--body", PrBody ?? string.Empty);
            url = create.StdOut.Trim();
            return await Check(create);
        });

        if (!string.IsNullOrEmpty(url))
        {
            await Inform(Se.Language.Project.CreatePullRequest, string.Format(Se.Language.Project.PrCreatedX, url));
        }
    }

    [RelayCommand]
    private async Task PushToPr()
    {
        var pr = CurrentPr;
        // commits the ticked files (if any) and pushes; also pushes commits made outside SE
        if (await RunWorkingTreeChange(async () => await CommitChecked() && await Check(await Git("push"))) && pr != null)
        {
            await Inform(Se.Language.Project.PullRequests, string.Format(Se.Language.Project.PushedToX, pr.Heading));
        }
    }

    [RelayCommand]
    private async Task CheckOut()
    {
        var pr = SelectedPr;
        if (pr != null)
        {
            await RunWorkingTreeChange(async () => await Check(await Gh("pr", "checkout", pr.Number.ToString(CultureInfo.InvariantCulture))));
        }
    }

    [RelayCommand]
    private async Task OpenOnGitHub(PrRow? pr)
    {
        pr ??= SelectedPr;
        if (pr != null)
        {
            await Gh("pr", "view", pr.Number.ToString(CultureInfo.InvariantCulture), "--web");
        }
    }

    /// <param name="method">gh merge flag: squash, merge or rebase.</param>
    [RelayCommand]
    private async Task Merge(string method)
    {
        var pr = SelectedPr;
        if (pr == null)
        {
            return;
        }

        if (Window != null && // no window = scripted test
            await MessageBox.Show(Window, Se.Language.Project.Merge, string.Format(Se.Language.Project.ConfirmMergeXYZ, pr.Number, pr.Title, BaseBranch),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunWorkingTreeChange(async () =>
            await Check(await Gh("pr", "merge", pr.Number.ToString(CultureInfo.InvariantCulture), "--" + method, "--delete-branch")) &&
            await Check(await Git("switch", BaseBranch)) &&
            await Check(await Git("pull", "--ff-only")));
    }

    [RelayCommand]
    private async Task BrowseCloneFolder()
    {
        if (Window != null)
        {
            var folder = await new FolderHelper().PickFolderAsync(Window, Se.Language.Project.CloneInto);
            if (!string.IsNullOrEmpty(folder))
            {
                CloneFolder = folder;
            }
        }
    }

    [RelayCommand]
    private async Task Clone()
    {
        var repo = CloneRepo.Trim();
        var folder = CloneFolder.Trim();
        if (repo.Length == 0 || folder.Length == 0 || IsBusy)
        {
            return;
        }

        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            await Inform(Se.Language.Project.Clone, Se.Language.Project.CloneFolderMustBeEmpty);
            return;
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(folder))!;
        Directory.CreateDirectory(parent);
        IsBusy = true;
        var result = await ProcessRunner.Gh(parent, CancellationToken.None, "repo", "clone", repo, folder);
        Log += $"> gh repo clone {repo} \"{folder}\"" + Environment.NewLine + (result.StdOut + result.StdErr).Trim() + Environment.NewLine;
        IsBusy = false;
        if (await Check(result))
        {
            _folder = folder;
            await Refresh();
            await Inform(Se.Language.Project.Clone, string.Format(Se.Language.Project.ClonedX, folder));
        }
    }
}
