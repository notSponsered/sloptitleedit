using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.UiLogic.Project;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Project;

public record SeriesFolderRow(SeriesFolder Folder)
{
    public string Name => Folder.Name;

    public string Summary => string.Format(Se.Language.Project.FilesXInSeasonsY,
        Folder.Files == 1 ? Se.Language.Project.OneFile : string.Format(Se.Language.Project.XFiles, Folder.Files),
        Folder.Seasons == 1 ? Se.Language.Project.OneSeason : string.Format(Se.Language.Project.XSeasons, Folder.Seasons));
}

/// <summary>
/// The optional project ↔ GitHub link: import a project from a repository, or connect an existing local project to one.
/// Clones (or reuses an existing copy), finds the series folders, then builds/moves the project via <see cref="ProjectRepo"/>.
/// </summary>
public partial class GitHubLinkViewModel : ObservableObject
{
    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }
    public SeProject? Result { get; private set; }
    public ConnectResult? Connected { get; private set; }

    private SeProject? _local;
    private bool _localCopyTouched;

    [ObservableProperty] private string _repository = string.Empty;
    [ObservableProperty] private string _localCopy = string.Empty;
    [ObservableProperty] private string _localNote = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasRepository;
    [ObservableProperty] private bool _useLocalFiles;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanFinish))] private SeriesFolderRow? _selectedSeries;

    public ObservableCollection<SeriesFolderRow> SeriesFolders { get; } = [];
    public bool IsConnect => _local != null;
    public bool CanFinish => SelectedSeries != null;
    public string Heading => IsConnect ? string.Format(Se.Language.Project.ConnectTitleX, _local!.Name) : Se.Language.Project.ImportTitle;
    public string Intro => IsConnect ? Se.Language.Project.ConnectIntro : Se.Language.Project.ImportIntro;
    public string FinishText => IsConnect ? Se.Language.Project.Connect : Se.Language.Project.CreateProject;

    /// <param name="local">The project to connect; null = import a new one.</param>
    public void Initialize(SeProject? local)
    {
        _local = local;
        _baseFolder = local == null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GitHub")
            : Path.GetDirectoryName(Path.GetFullPath(local.Folder)) ?? string.Empty;
        SetLocalCopyAutomatically(_baseFolder);
        OnPropertyChanged(nameof(Heading));
        OnPropertyChanged(nameof(Intro));
        OnPropertyChanged(nameof(FinishText));
        OnPropertyChanged(nameof(IsConnect));
    }

    private string _baseFolder = string.Empty;
    private bool _settingAuto;

    private void SetLocalCopyAutomatically(string folder)
    {
        _settingAuto = true;
        LocalCopy = folder;
        _settingAuto = false;
    }

    // until the user types or picks a folder, the copy goes to <base folder>\<repo name>
    partial void OnRepositoryChanged(string value)
    {
        if (!_localCopyTouched)
        {
            var name = value.Trim().TrimEnd('/').Split('/').LastOrDefault()?.Replace(".git", string.Empty) ?? string.Empty;
            SetLocalCopyAutomatically(name.Length == 0 ? _baseFolder : Path.Combine(_baseFolder, name));
        }
    }

    partial void OnLocalCopyChanged(string value)
    {
        if (!_settingAuto)
        {
            _localCopyTouched = true;
        }

        HasRepository = false;
        SeriesFolders.Clear();
        _ = UpdateLocalNote(value);
    }

    private async Task UpdateLocalNote(string folder)
    {
        var note = string.Empty;
        if (Directory.Exists(folder))
        {
            var repo = await ProcessRunner.GetRepoName(folder);
            note = repo != null
                ? string.Format(Se.Language.Project.ExistingCopyX, repo)
                : Directory.EnumerateFileSystemEntries(folder).Any() ? Se.Language.Project.FolderNotEmptyNotRepo : Se.Language.Project.WillBeClonedHere;
        }
        else if (folder.Trim().Length > 0)
        {
            note = Se.Language.Project.WillBeClonedHere;
        }

        if (folder == LocalCopy)
        {
            LocalNote = note;
        }
    }

    [RelayCommand]
    private async Task BrowseLocalCopy()
    {
        if (Window != null)
        {
            var folder = await new FolderHelper().PickFolderAsync(Window, Se.Language.Project.LocalCopy);
            if (!string.IsNullOrEmpty(folder))
            {
                _localCopyTouched = true;
                LocalCopy = folder;
            }
        }
    }

    /// <summary>Clone into an empty/new folder, or use the folder as is when it already is a copy; then list series folders.</summary>
    [RelayCommand]
    private async Task GetRepository()
    {
        var folder = LocalCopy.Trim();
        if (folder.Length == 0 || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var root = Directory.Exists(folder) ? await ProcessRunner.GetRepoRoot(folder) : null;
            if (root == null)
            {
                if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    await Inform(Se.Language.Project.FolderNotEmptyNotRepo);
                    return;
                }

                if (Repository.Trim().Length == 0)
                {
                    await Inform(Se.Language.Project.CloneRepository);
                    return;
                }

                var parent = Path.GetDirectoryName(Path.GetFullPath(folder))!;
                Directory.CreateDirectory(parent);
                var clone = await ProcessRunner.Gh(parent, CancellationToken.None, "repo", "clone", Repository.Trim(), folder);
                if (!clone.Ok)
                {
                    await Inform(clone.NotFound ? Se.Language.Project.GhMissingBody + Environment.NewLine + "winget install --id GitHub.cli -e" : clone.Message);
                    return;
                }

                root = folder;
                await UpdateLocalNote(folder);
            }

            SeriesFolders.Clear();
            foreach (var series in ProjectRepo.FindSeriesFolders(folder))
            {
                SeriesFolders.Add(new SeriesFolderRow(series));
            }

            var preferred = _local == null ? null : SeriesFolders.FirstOrDefault(s =>
                string.Equals(s.Name, _local.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, Path.GetFileName(_local.Folder.TrimEnd(Path.DirectorySeparatorChar)), StringComparison.OrdinalIgnoreCase));
            SelectedSeries = preferred ?? SeriesFolders.FirstOrDefault();
            HasRepository = true;
            if (SeriesFolders.Count == 0)
            {
                await Inform(Se.Language.Project.NoSeriesFound);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task Finish()
    {
        var series = SelectedSeries?.Folder;
        if (series == null || IsBusy)
        {
            return;
        }

        try
        {
            if (_local == null)
            {
                // an existing project there keeps its titles/tokens/notes but still picks up every repo file
                var existing = SeProject.Load(series.Path);
                existing?.MapSubtitles(series.Path);
                Result = existing ?? ProjectRepo.CreateFromFolder(series.Path);
                Result.UseGitHub = true;
            }
            else
            {
                Connected = ProjectRepo.ConnectTo(_local, series.Path, UseLocalFiles);
                Result = Connected.Project;
            }

            Result.Save();
            await ProcessRunner.EnsureGitExclude(series.Path);
        }
        catch (Exception e)
        {
            await Inform(e.Message);
            return;
        }

        OkPressed = true;
        Close();
    }

    [RelayCommand]
    private void Cancel() => Close();

    private void Close() => Dispatcher.UIThread.Post(() => Window?.Close());

    private async Task Inform(string message)
    {
        if (Window != null)
        {
            await MessageBox.Show(Window, Heading, message, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
