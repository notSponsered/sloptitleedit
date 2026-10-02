using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;
using Nikse.SubtitleEdit.Features.Assa;
using Nikse.SubtitleEdit.Features.Project;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.UiLogic.Project;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Main;

/// <summary>
/// Project mode: a folder with a ".seproject.json" sidecar listing episodes (video + .ass), shared styles,
/// Sonarr-style naming. (GitHub lives in MainViewModel.GitHub.cs and is optional.)
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private SeProject? _project;
    [ObservableProperty] private bool _isProjectOpen;
    [ObservableProperty] private ObservableCollection<ProjectEpisode> _projectEpisodes = new();
    [ObservableProperty] private ProjectEpisode? _selectedProjectEpisode;
    private bool _projectSilentSelect;

    partial void OnSelectedProjectEpisodeChanged(ProjectEpisode? oldValue, ProjectEpisode? newValue)
    {
        if (_projectSilentSelect || newValue == null || Project == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                await SwitchToEpisode(newValue, oldValue);
            }
            catch (Exception e)
            {
                Se.LogError(e);
            }
        });
    }

    private void SelectEpisodeSilently(ProjectEpisode? ep)
    {
        _projectSilentSelect = true;
        SelectedProjectEpisode = ep;
        _projectSilentSelect = false;
    }

    private async Task SwitchToEpisode(ProjectEpisode ep, ProjectEpisode? previous)
    {
        if (Project == null || Window == null)
        {
            return;
        }

        if (!await HasChangesContinue())
        {
            SelectEpisodeSilently(previous);
            return;
        }

        StoreEpisodeState(previous);

        var subtitle = Project.ResolvePath(ep.Subtitle);
        if (string.IsNullOrEmpty(subtitle) || !File.Exists(subtitle))
        {
            subtitle = await CreateEpisodeSubtitle(ep);
            if (subtitle == null)
            {
                SelectEpisodeSilently(previous);
                return;
            }
        }

        VideoCloseFile();
        await SubtitleOpen(subtitle, skipLoadVideo: true, selectedSubtitleIndex: ep.SelectedLine);
        var video = Project.ResolvePath(ep.Video);
        if (File.Exists(video))
        {
            await VideoOpenFile(video, ep.AudioTrack);
        }

        Project.CurrentEpisode = Project.Episodes.IndexOf(ep);
        SaveProject();
    }

    private async Task<string?> CreateEpisodeSubtitle(ProjectEpisode ep)
    {
        var project = Project!;
        var relative = ep.Subtitle;
        if (string.IsNullOrWhiteSpace(relative))
        {
            var name = SonarrNaming.Format(project.NamingTemplate, ep, project);
            relative = (string.IsNullOrEmpty(name) ? ep.Tag : name) + ".ass";
        }

        var path = project.ResolvePath(relative);
        var answer = await MessageBox.Show(Window!, Se.Language.Project.Episode, string.Format(Se.Language.Project.CreateSubtitleX, path),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return null;
        }

        var subtitle = new Subtitle { Header = project.StyleHeader ?? AdvancedSubStationAlpha.DefaultHeader };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, new AdvancedSubStationAlpha().ToText(subtitle, string.Empty), new UTF8Encoding(true));
        ep.Subtitle = project.MakeRelative(path);
        SaveProject();
        return path;
    }

    private void StoreEpisodeState(ProjectEpisode? ep)
    {
        if (Project == null || ep == null || string.IsNullOrEmpty(_subtitleFileName) ||
            !string.Equals(Project.ResolvePath(ep.Subtitle), Path.GetFullPath(_subtitleFileName), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ep.SelectedLine = SelectedSubtitleIndex ?? 0;
        ep.AudioTrack = _audioTrack?.Id ?? -1;
    }

    private void SaveProject()
    {
        try
        {
            Project?.Save();
        }
        catch (Exception e)
        {
            Se.LogError(e);
        }
    }

    private async Task OpenProject(SeProject project, bool openCurrentEpisode)
    {
        Project = project;
        IsProjectOpen = true;
        ProjectEpisodes = project.Episodes;
        Se.Settings.File.LastProjectFolder = project.Folder;
        Se.SaveSettings();
        await ProcessRunner.EnsureGitExclude(project.Folder);

        var current = project.FindBySubtitle(_subtitleFileName) ??
                      project.Episodes.ElementAtOrDefault(project.CurrentEpisode) ??
                      project.Episodes.FirstOrDefault();
        if (openCurrentEpisode && current != null && current != project.FindBySubtitle(_subtitleFileName))
        {
            SelectedProjectEpisode = current; // switches episode
        }
        else
        {
            SelectEpisodeSilently(current);
        }
    }

    private async Task<SeProject?> ShowProjectEditor(SeProject project)
    {
        var result = await ShowDialogAsync<ProjectWindow, ProjectViewModel>(vm => vm.Initialize(project));
        return result.OkPressed ? result.Project : null;
    }

    [RelayCommand]
    private async Task ProjectNew()
    {
        if (Window == null)
        {
            return;
        }

        var folder = await _folderHelper.PickFolderAsync(Window, Se.Language.Project.PickProjectFolder);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var existing = SeProject.Load(folder);
        if (existing != null)
        {
            await OpenProject(existing, true);
            return;
        }

        var project = new SeProject { Folder = folder, Name = Path.GetFileName(folder), SeriesTitle = Path.GetFileName(folder) };
        var edited = await ShowProjectEditor(project);
        if (edited != null)
        {
            await OpenProject(edited, true);
        }
    }

    [RelayCommand]
    private async Task ProjectOpen()
    {
        if (Window == null)
        {
            return;
        }

        var folder = await _folderHelper.PickFolderAsync(Window, Se.Language.Project.PickProjectFolder);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        var project = SeProject.Load(folder);
        if (project == null)
        {
            var answer = await MessageBox.Show(Window, Se.Language.Project.OpenProject.Replace("_", string.Empty),
                string.Format(Se.Language.Project.NoProjectInFolderX, folder), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            project = await ShowProjectEditor(new SeProject { Folder = folder, Name = Path.GetFileName(folder), SeriesTitle = Path.GetFileName(folder) });
            if (project == null)
            {
                return;
            }
        }

        await OpenProject(project, true);
    }

    [RelayCommand]
    private async Task ProjectEdit()
    {
        if (Project == null)
        {
            return;
        }

        // the editor can rename files on disk, so unsaved edits must be written first
        if (!await HasChangesContinue())
        {
            return;
        }

        StoreEpisodeState(SelectedProjectEpisode);

        // follow the open episode by identity, not by its S01E01 tag: the editor can renumber it (and rename its file).
        // The editor edits this clone's episode objects in place and hands the same clone back.
        var clone = Project.Clone();
        var openInClone = SelectedProjectEpisode == null ? null : clone.Episodes.ElementAtOrDefault(Project.Episodes.IndexOf(SelectedProjectEpisode));
        var edited = await ShowProjectEditor(clone);
        if (edited == null)
        {
            return;
        }

        var current = openInClone != null && edited.Episodes.Contains(openInClone) ? openInClone : null;
        if (current != null)
        {
            edited.CurrentEpisode = edited.Episodes.IndexOf(current);
        }

        await OpenProject(edited, false);

        // follow a renamed subtitle file
        var path = current == null ? string.Empty : edited.ResolvePath(current.Subtitle);
        if (File.Exists(path) && !string.Equals(path, _subtitleFileName, StringComparison.OrdinalIgnoreCase))
        {
            await SubtitleOpen(path, skipLoadVideo: true, selectedSubtitleIndex: SelectedSubtitleIndex);
            SelectEpisodeSilently(current);
        }
    }

    [RelayCommand]
    private void ProjectClose()
    {
        if (Project == null)
        {
            return;
        }

        StoreEpisodeState(SelectedProjectEpisode);
        SaveProject();
        SelectEpisodeSilently(null);
        Project = null;
        IsProjectOpen = false;
        ProjectEpisodes = new();
        Se.Settings.File.LastProjectFolder = string.Empty;
        Se.SaveSettings();
    }

    [RelayCommand]
    private void ProjectNextEpisode() => MoveEpisode(1);

    [RelayCommand]
    private void ProjectPreviousEpisode() => MoveEpisode(-1);

    private void MoveEpisode(int delta)
    {
        if (Project == null || ProjectEpisodes.Count == 0)
        {
            return;
        }

        var index = SelectedProjectEpisode == null ? 0 : ProjectEpisodes.IndexOf(SelectedProjectEpisode) + delta;
        if (index >= 0 && index < ProjectEpisodes.Count)
        {
            SelectedProjectEpisode = ProjectEpisodes[index];
        }
    }

    [RelayCommand]
    private async Task ProjectEditStyles()
    {
        if (Project == null || Window == null)
        {
            return;
        }

        var header = Project.StyleHeader;
        if (string.IsNullOrEmpty(header))
        {
            header = IsFormatAssa && !string.IsNullOrEmpty(_subtitle.Header) ? _subtitle.Header : AdvancedSubStationAlpha.DefaultHeader;
        }

        var result = await ShowDialogAsync<AssaStylesWindow, AssaStylesViewModel>(vm =>
        {
            vm.Initialize(new Subtitle { Header = header }, new AdvancedSubStationAlpha(),
                $"{Se.Language.Project.ProjectStylesTitle} - {Project.Name}", string.Empty, null);
        });

        if (!result.OkPressed)
        {
            return;
        }

        Project.StyleHeader = result.Header;
        SaveProject();

        var answer = await MessageBox.Show(Window, Se.Language.Project.ProjectStylesTitle, Se.Language.Project.ApplyStylesNow,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == MessageBoxResult.Yes)
        {
            await ProjectApplyStyles();
        }
    }

    [RelayCommand]
    private async Task ProjectApplyStyles()
    {
        if (Project == null || Window == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(Project.StyleHeader))
        {
            await MessageBox.Show(Window, Se.Language.Project.ProjectStylesTitle, Se.Language.Project.NoProjectStyles,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!await HasChangesContinue())
        {
            return;
        }

        var count = 0;
        foreach (var ep in Project.Episodes)
        {
            var path = Project.ResolvePath(ep.Subtitle);
            if (!File.Exists(path))
            {
                continue;
            }

            string text;
            Encoding encoding;
            using (var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
            {
                text = await reader.ReadToEndAsync();
                encoding = reader.CurrentEncoding;
            }

            var newText = SeProject.ApplyStylesToFileText(text, Project.StyleHeader);
            if (newText != text)
            {
                await File.WriteAllTextAsync(path, newText, encoding);
                count++;
            }
        }

        await ProjectReloadCurrentEpisode();
        await MessageBox.Show(Window, Se.Language.Project.ProjectStylesTitle, string.Format(Se.Language.Project.StylesAppliedToX, count),
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Re-reads the current episode's subtitle after git changed it on disk (changes were saved/discarded first).</summary>
    private async Task ProjectReloadCurrentEpisode()
    {
        if (Project == null)
        {
            return;
        }

        var ep = SelectedProjectEpisode ?? Project.FindBySubtitle(_subtitleFileName);
        var path = ep == null ? _subtitleFileName : Project.ResolvePath(ep.Subtitle);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            await SubtitleOpen(path, skipLoadVideo: true, selectedSubtitleIndex: SelectedSubtitleIndex);
        }
    }

    /// <summary>Startup: re-attach the last project to the file that was just reopened.</summary>
    private async Task ProjectRestore()
    {
        var folder = Se.Settings.File.LastProjectFolder;
        if (string.IsNullOrEmpty(folder) || !SeProject.Exists(folder))
        {
            return;
        }

        var project = SeProject.Load(folder);
        if (project != null)
        {
            await OpenProject(project, false);
        }
    }

    private void ProjectSaveState()
    {
        StoreEpisodeState(SelectedProjectEpisode);
        SaveProject();
    }
}
