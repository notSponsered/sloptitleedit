using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Main.Layout;
using Nikse.SubtitleEdit.Features.Project;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Layout;
using Nikse.SubtitleEdit.UiLogic.Project;
using System.IO;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>
/// Hooks for the optional GitHub panel. It works in the open project's folder, or else the open subtitle file's
/// folder; the project side knows nothing about GitHub.
/// </summary>
public partial class MainViewModel
{
    /// <summary>The open project opted in to GitHub ("Use GitHub with this project"): shows Project → GitHub…</summary>
    public bool IsProjectUsingGitHub => Project?.UseGitHub == true;

    /// <summary>A project is open and not linked yet: shows Project → Connect to GitHub…</summary>
    public bool CanConnectProjectToGitHub => Project != null && !Project.UseGitHub;

    partial void OnProjectChanged(SeProject? value)
    {
        OnPropertyChanged(nameof(IsProjectUsingGitHub));
        OnPropertyChanged(nameof(CanConnectProjectToGitHub));
    }

    /// <summary>Project → Import from GitHub…: clone (or reuse a copy), pick a series folder, open it as a project.</summary>
    [RelayCommand]
    private async Task ProjectImportFromGitHub()
    {
        if (Window == null)
        {
            return;
        }

        var result = await ShowDialogAsync<GitHubLinkWindow, GitHubLinkViewModel>(vm => vm.Initialize(null));
        if (result.OkPressed && result.Result != null)
        {
            await OpenProject(result.Result, true);
        }
    }

    /// <summary>
    /// Project → Connect to GitHub…: a project already inside a repository just switches the link on; otherwise
    /// the project is moved onto a copy of the repository (episodes matched to its files).
    /// </summary>
    [RelayCommand]
    private async Task ProjectConnectGitHub()
    {
        if (Project == null || Window == null)
        {
            return;
        }

        var repo = await ProcessRunner.GetRepoName(Project.Folder);
        if (repo != null)
        {
            Project.UseGitHub = true;
            SaveProject();
            OnProjectChanged(Project);
            await MessageBox.Show(Window, Se.Language.Workspace.GitHub, string.Format(Se.Language.Project.ConnectedX, repo), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!await HasChangesContinue())
        {
            return;
        }

        StoreEpisodeState(SelectedProjectEpisode);
        var result = await ShowDialogAsync<GitHubLinkWindow, GitHubLinkViewModel>(vm => vm.Initialize(Project.Clone()));
        if (!result.OkPressed || result.Result == null || result.Connected == null)
        {
            return;
        }

        var c = result.Connected;
        await OpenProject(result.Result, true);
        await MessageBox.Show(Window, Se.Language.Workspace.GitHub, string.Format(Se.Language.Project.ConnectResultXYZ, c.Matched, c.Copied, c.Added),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Project → GitHub…: bring the GitHub tab to front, else add it beside the Project panel, else float it.</summary>
    [RelayCommand]
    private void ShowProjectGitHub()
    {
        var ws = AreaHost.Active;
        var leaf = AreaTree.FindLeafWith(ws, PanelIds.GitHub) ?? AreaTree.FindLeafWith(ws, PanelIds.Project);
        if (leaf != null)
        {
            AreaHost.AddTab(leaf, PanelIds.GitHub);
        }
        else
        {
            AreaHost.DetachPanel(PanelIds.GitHub);
        }
    }

    internal string? GitHubFolder => Project?.Folder ??
                                     (string.IsNullOrEmpty(_subtitleFileName) ? null : Path.GetDirectoryName(Path.GetFullPath(_subtitleFileName)));

    /// <summary>Changes when the panel should start over: another folder, file or episode.</summary>
    internal string GitHubContextKey => $"{GitHubFolder}|{_subtitleFileName}|{SelectedProjectEpisode?.Tag}";

    internal GitHubViewModel CreateGitHubViewModel() => new() { Window = Window };

    internal void InitializeGitHubViewModel(GitHubViewModel gh)
    {
        var episode = Project == null ? null : SelectedProjectEpisode;
        var name = string.IsNullOrEmpty(_subtitleFileName) ? string.Empty : Path.GetFileNameWithoutExtension(_subtitleFileName);
        gh.Window = Window;
        gh.Initialize(GitHubFolder, episode?.Tag ?? name, episode?.Display ?? name, HasChangesContinue, ReloadSubtitleFromDisk);
    }

    /// <summary>Re-reads the open subtitle after git changed it on disk (changes were saved or discarded first).</summary>
    private async Task ReloadSubtitleFromDisk()
    {
        if (!string.IsNullOrEmpty(_subtitleFileName) && File.Exists(_subtitleFileName))
        {
            await SubtitleOpen(_subtitleFileName, skipLoadVideo: true, selectedSubtitleIndex: SelectedSubtitleIndex);
        }
    }
}
