using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Features.Shared;
using Nikse.SubtitleEdit.Logic.Config;
using Nikse.SubtitleEdit.Logic.Media;
using Nikse.SubtitleEdit.UiLogic.Project;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nikse.SubtitleEdit.Features.Project;

public partial class TokenRow : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    public string[] Options { get; init; } = [];
}

/// <summary>
/// Project editor: folder/repo, episode list (+ auto-map and naming tokens), metadata import and naming/renaming.
/// Edits a clone of the project; the caller takes <see cref="Project"/> when <see cref="OkPressed"/>.
/// </summary>
public partial class ProjectViewModel : ObservableObject
{
    private readonly HttpClient _httpClient;
    private readonly IFolderHelper _folderHelper;
    private readonly IFileHelper _fileHelper;

    public Window? Window { get; set; }
    public bool OkPressed { get; private set; }

    [ObservableProperty] private SeProject _project = new();
    [ObservableProperty] private string _folder = string.Empty;
    [ObservableProperty] private string _seriesYearText = string.Empty;
    [ObservableProperty] private bool _isInRepo;
    [ObservableProperty] private string _gitHubNote = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private ProjectEpisode? _selectedEpisode;
    public List<ProjectEpisode> SelectedEpisodes { get; private set; } = [];
    [ObservableProperty] private int _addSeason = 1;
    [ObservableProperty] private int _addCount = 12;
    [ObservableProperty] private int _addPerFile = 1;

    public string[] TokenScopes { get; } = [Se.Language.Project.ScopeSelected, Se.Language.Project.ScopeSeason, Se.Language.Project.ScopeProject];
    [ObservableProperty] private string _selectedTokenScope;
    public ObservableCollection<TokenRow> TokenRows { get; } = [];

    public string[] MetaSources { get; } = [EpisodeMetadata.TvMaze, EpisodeMetadata.AniList];
    [ObservableProperty] private string _selectedMetaSource = EpisodeMetadata.TvMaze;
    [ObservableProperty] private string _metaQuery = string.Empty;
    public ObservableCollection<ShowSearchResult> MetaResults { get; } = [];
    [ObservableProperty] private ShowSearchResult? _selectedMetaResult;

    public NamingPreset[] Presets { get; } = SonarrNaming.Presets;
    public string[] MultiEpisodeStyles { get; } = SonarrNaming.MultiEpisodeStyles;
    [ObservableProperty] private NamingPreset? _selectedPreset;
    [ObservableProperty] private string _namingPreview = string.Empty;
    [ObservableProperty] private bool _alsoRenameVideos;

    public string TokenHelp { get; } =
        "{Series Title} {Series TitleYear} {Series CleanTitle} {Series CleanTitleWithoutYear} {Series Year} {season:00} {episode:00} " +
        "{absolute:000} {Episode Title} {Episode CleanTitle:90} {Air-Date} {ImdbId} {TvdbId} · " +
        string.Join(" ", SonarrNaming.CommonTokens.Select(t => "{" + t + "}")) +
        " · any custom token, e.g. {Release} · prefix/suffix inside braces: {[Release Group]} {-Release Group} · '/' = sub-folder · Separator in name: {Series.CleanTitle}";

    public ProjectViewModel(HttpClient httpClient, IFolderHelper folderHelper, IFileHelper fileHelper)
    {
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        _folderHelper = folderHelper;
        _fileHelper = fileHelper;
        _selectedTokenScope = TokenScopes[0];
    }

    public void Initialize(SeProject project)
    {
        Project = project;
        Folder = project.Folder;
        SeriesYearText = project.SeriesYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        MetaQuery = string.IsNullOrWhiteSpace(project.SeriesTitle) ? project.Name : project.SeriesTitle;
        SelectedPreset = Presets.FirstOrDefault(p => p.Template == project.NamingTemplate);
        project.PropertyChanged += (_, _) => UpdatePreview();
        SelectedEpisode = project.Episodes.ElementAtOrDefault(project.CurrentEpisode) ?? project.Episodes.FirstOrDefault();
        LoadTokenRows();
        _ = UpdateGitHubNote();
    }

    partial void OnFolderChanged(string value)
    {
        Project.Folder = value;
        _ = UpdateGitHubNote();
    }

    /// <summary>The optional GitHub link can only be switched on for a folder inside a repository.</summary>
    private async Task UpdateGitHubNote()
    {
        var folder = Folder;
        var repo = string.IsNullOrWhiteSpace(folder) ? null : await ProcessRunner.GetRepoName(folder);
        if (folder == Folder)
        {
            IsInRepo = repo != null;
            GitHubNote = repo == null ? Se.Language.Project.NotInRepoUseConnect : string.Format(Se.Language.Project.RepositoryX, repo);
        }
    }

    // the preview follows edits to the selected row too (Episode range, title, tokens), not only selection changes
    partial void OnSelectedEpisodeChanged(ProjectEpisode? oldValue, ProjectEpisode? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSelectedEpisodeEdited;
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += OnSelectedEpisodeEdited;
        }

        UpdatePreview();
    }

    private void OnSelectedEpisodeEdited(object? sender, PropertyChangedEventArgs e) => UpdatePreview();

    partial void OnSelectedTokenScopeChanged(string value) => LoadTokenRows();

    partial void OnSelectedPresetChanged(NamingPreset? value)
    {
        if (value != null)
        {
            Project.NamingTemplate = value.Template;
        }
    }

    public void SetSelectedEpisodes(IEnumerable<ProjectEpisode> episodes)
    {
        SelectedEpisodes = episodes.ToList();
        LoadTokenRows();
    }

    private void UpdatePreview()
    {
        var ep = SelectedEpisode ?? Project.Episodes.FirstOrDefault() ?? new ProjectEpisode();
        var name = SonarrNaming.Format(Project.NamingTemplate, ep, Project);
        NamingPreview = name.Length == 0 ? string.Empty : name + ".ass";
    }

    // ── Series ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseFolder()
    {
        if (Window == null)
        {
            return;
        }

        var folder = await _folderHelper.PickFolderAsync(Window, Se.Language.Project.PickProjectFolder);
        if (!string.IsNullOrEmpty(folder))
        {
            Folder = folder;
        }
    }

    // ── Episodes ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddEpisodes()
    {
        var perFile = Math.Max(1, AddPerFile);
        var start = Project.Episodes.Where(e => e.Season == AddSeason).Select(e => e.LastEpisode).DefaultIfEmpty(0).Max() + 1;
        for (var i = 0; i < AddCount; i++)
        {
            var first = start + i * perFile;
            Project.InsertSorted(new ProjectEpisode { Season = AddSeason, Episode = first, EpisodeEnd = perFile > 1 ? first + perFile - 1 : null });
        }
    }

    [RelayCommand]
    private void RemoveEpisodes()
    {
        foreach (var ep in SelectedEpisodes.ToList())
        {
            Project.Episodes.Remove(ep);
        }
    }

    [RelayCommand]
    private async Task AutoMap()
    {
        if (Window == null || string.IsNullOrWhiteSpace(Folder) || !Directory.Exists(Folder))
        {
            await ShowMessage(Se.Language.Project.FolderRequired);
            return;
        }

        var subtitles = Project.MapSubtitles(Folder);
        var videos = 0;
        var videoFolder = await _folderHelper.PickFolderAsync(Window, Se.Language.Project.PickVideoFolder);
        if (!string.IsNullOrEmpty(videoFolder))
        {
            foreach (var file in EnumerateFiles(videoFolder, Utilities.VideoFileExtensions))
            {
                var n = SonarrNaming.ParseEpisodeNumber(file);
                if (n != null)
                {
                    Project.GetOrAddEntry(n).Video = file;
                    videos++;
                }
            }
        }

        UpdatePreview();
        await ShowMessage(string.Format(Se.Language.Project.AutoMapResultXY, subtitles, videos));
    }

    private static IEnumerable<string> EnumerateFiles(string folder, IEnumerable<string> extensions)
    {
        var set = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
        return Directory.EnumerateFiles(folder, "*", options)
            .Where(f => set.Contains(Path.GetExtension(f)) && !f.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private async Task BrowseVideo(ProjectEpisode? ep)
    {
        if (Window == null || ep == null)
        {
            return;
        }

        var file = await _fileHelper.PickOpenVideoFile(Window, Se.Language.Project.Video);
        if (!string.IsNullOrEmpty(file))
        {
            ep.Video = file;
        }
    }

    [RelayCommand]
    private async Task BrowseSubtitle(ProjectEpisode? ep)
    {
        if (Window == null || ep == null)
        {
            return;
        }

        var file = await _fileHelper.PickOpenSubtitleFile(Window, Se.Language.Project.Subtitle, false, Project.ResolvePath(ep.Subtitle));
        if (!string.IsNullOrEmpty(file))
        {
            ep.Subtitle = Project.MakeRelative(file);
            UpdatePreview();
        }
    }

    // ── Tokens ───────────────────────────────────────────────────────────────

    private int ScopeIndex => Array.IndexOf(TokenScopes, SelectedTokenScope);

    private ProjectEpisode? FirstSelected => SelectedEpisodes.FirstOrDefault() ?? SelectedEpisode;

    private void LoadTokenRows()
    {
        var first = FirstSelected;
        var source = ScopeIndex switch
        {
            0 => first?.Tokens,
            1 => first != null && Project.SeasonTokens.TryGetValue(first.Season, out var s) ? s : null,
            _ => Project.Tokens,
        } ?? new Dictionary<string, string>();

        TokenRows.Clear();
        foreach (var name in SonarrNaming.CommonTokens.Concat(source.Keys)
                     .DistinctBy(SonarrNaming.NormalizeTokenName, StringComparer.Ordinal))
        {
            var value = source.FirstOrDefault(kv => SonarrNaming.NormalizeTokenName(kv.Key) == SonarrNaming.NormalizeTokenName(name)).Value ?? string.Empty;
            TokenRows.Add(new TokenRow { Name = name, Value = value, Options = OptionsFor(name) });
        }
    }

    private static string[] OptionsFor(string token) => SonarrNaming.NormalizeTokenName(token) switch
    {
        "quality full" => SonarrNaming.QualityOptions,
        "language" => SonarrNaming.Languages.Select(l => l.Name).ToArray(),
        "language code" => SonarrNaming.Languages.Select(l => l.Code).ToArray(),
        _ => [],
    };

    [RelayCommand]
    private void AddToken()
    {
        TokenRows.Add(new TokenRow());
    }

    [RelayCommand]
    private void ApplyTokens()
    {
        var targets = new List<Dictionary<string, string>>();
        var selected = SelectedEpisodes.Count > 0 ? SelectedEpisodes : SelectedEpisode == null ? [] : [SelectedEpisode];
        switch (ScopeIndex)
        {
            case 0:
                targets.AddRange(selected.Select(e => e.Tokens));
                break;
            case 1:
                foreach (var season in selected.Select(e => e.Season).Distinct())
                {
                    if (!Project.SeasonTokens.TryGetValue(season, out var dict))
                    {
                        dict = new Dictionary<string, string>();
                        Project.SeasonTokens[season] = dict;
                    }

                    targets.Add(dict);
                }

                break;
            default:
                targets.Add(Project.Tokens);
                break;
        }

        foreach (var dict in targets)
        {
            foreach (var row in TokenRows.Where(r => !string.IsNullOrWhiteSpace(r.Name)))
            {
                var key = SonarrNaming.NormalizeTokenName(row.Name);
                foreach (var existing in dict.Keys.Where(k => SonarrNaming.NormalizeTokenName(k) == key).ToList())
                {
                    dict.Remove(existing);
                }

                if (!string.IsNullOrWhiteSpace(row.Value))
                {
                    dict[row.Name.Trim()] = row.Value.Trim();
                }
            }
        }

        StatusText = targets.Count == 0 ? Se.Language.Project.ScopeSelected + ": 0" : string.Empty;
        UpdatePreview();
    }

    [RelayCommand]
    private async Task ImportFromVideo()
    {
        var ep = FirstSelected;
        var video = ep == null ? string.Empty : Project.ResolvePath(ep.Video);
        if (!File.Exists(video))
        {
            await ShowMessage(Se.Language.Project.NoVideoForSelection);
            return;
        }

        var ffmpeg = Configuration.Settings.General.FFmpegLocation;
        if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg))
        {
            ffmpeg = "ffmpeg";
        }

        IsBusy = true;
        var result = await ProcessRunner.Run(ffmpeg, Path.GetDirectoryName(video)!, CancellationToken.None, "-hide_banner", "-i", video);
        IsBusy = false;
        if (result.NotFound)
        {
            await ShowMessage(Se.Language.Project.FfmpegNotFound);
            return;
        }

        foreach (var (name, value) in SonarrNaming.TokensFromVideo(result.StdErr, video))
        {
            var row = TokenRows.FirstOrDefault(r => SonarrNaming.NormalizeTokenName(r.Name) == SonarrNaming.NormalizeTokenName(name));
            if (row == null)
            {
                TokenRows.Add(new TokenRow { Name = name, Value = value, Options = OptionsFor(name) });
            }
            else
            {
                row.Value = value;
            }
        }

        ApplyTokens();
    }

    // ── Metadata ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task MetaSearch()
    {
        if (string.IsNullOrWhiteSpace(MetaQuery) || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            List<ShowSearchResult> results;
            if (SelectedMetaSource == EpisodeMetadata.AniList)
            {
                using var content = new StringContent(EpisodeMetadata.AniListSearchBody(MetaQuery.Trim()), Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, EpisodeMetadata.AniListUrl) { Content = content };
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                results = EpisodeMetadata.ParseAniListSearch(await response.Content.ReadAsStringAsync());
            }
            else
            {
                results = EpisodeMetadata.ParseTvMazeSearch(await _httpClient.GetStringAsync(EpisodeMetadata.TvMazeSearchUrl(MetaQuery.Trim())));
            }

            MetaResults.Clear();
            foreach (var r in results)
            {
                MetaResults.Add(r);
            }

            SelectedMetaResult = MetaResults.FirstOrDefault();
        }
        catch (Exception e)
        {
            await ShowMessage(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task MetaImport()
    {
        var show = SelectedMetaResult;
        if (show == null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var episodes = show.Episodes ??
                           EpisodeMetadata.ParseTvMazeEpisodes(await _httpClient.GetStringAsync(EpisodeMetadata.TvMazeEpisodesUrl(show.Id)));
            EpisodeMetadata.MergeInto(Project, show, episodes);
            SeriesYearText = Project.SeriesYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            UpdatePreview();
            StatusText = string.Format(Se.Language.Project.ImportedX, episodes.Count);
        }
        catch (Exception e)
        {
            await ShowMessage(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Re-formats every title already in the project with the current title word separator.</summary>
    [RelayCommand]
    private void ApplyTitleSeparator()
    {
        Project.ReformatTitles();
        UpdatePreview();
    }

    // ── Naming ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RenameFiles()
    {
        if (Window == null)
        {
            return;
        }

        var moves = new List<(ProjectEpisode Ep, bool IsVideo, string From, string To)>();
        foreach (var ep in Project.Episodes)
        {
            var name = SonarrNaming.Format(Project.NamingTemplate, ep, Project);
            if (name.Length == 0)
            {
                continue;
            }

            var subtitle = Project.ResolvePath(ep.Subtitle);
            if (File.Exists(subtitle))
            {
                var target = Project.ResolvePath(name + ".ass");
                if (!string.Equals(subtitle, target, StringComparison.Ordinal))
                {
                    moves.Add((ep, false, subtitle, target));
                }
            }

            var video = Project.ResolvePath(ep.Video);
            if (AlsoRenameVideos && File.Exists(video))
            {
                var target = Path.Combine(Path.GetDirectoryName(video)!, Path.GetFileName(name) + Path.GetExtension(video));
                if (!string.Equals(video, target, StringComparison.Ordinal))
                {
                    moves.Add((ep, true, video, target));
                }
            }
        }

        if (moves.Count == 0)
        {
            await ShowMessage(Se.Language.Project.NothingToRename);
            return;
        }

        var list = string.Join(Environment.NewLine, moves.Take(25).Select(m => $"{Path.GetFileName(m.From)}  →  {Project.MakeRelative(m.To)}"));
        if (moves.Count > 25)
        {
            list += Environment.NewLine + "...";
        }

        var answer = await MessageBox.Show(Window, Se.Language.Project.RenameFiles, string.Format(Se.Language.Project.RenameConfirmX, list),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var renamed = 0;
        var errors = new StringBuilder();
        foreach (var m in moves)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(m.To)!);
                if (File.Exists(m.To) && !string.Equals(m.From, m.To, StringComparison.OrdinalIgnoreCase))
                {
                    errors.AppendLine($"{m.To}: already exists");
                    continue;
                }

                File.Move(m.From, m.To);
                if (m.IsVideo)
                {
                    m.Ep.Video = m.To;
                }
                else
                {
                    m.Ep.Subtitle = Project.MakeRelative(m.To);
                }

                renamed++;
            }
            catch (Exception e)
            {
                errors.AppendLine($"{Path.GetFileName(m.From)}: {e.Message}");
            }
        }

        // files moved on disk: keep the mapping in sync even if the editor is cancelled
        Project.Save();
        OkPressed = true;
        UpdatePreview();
        await ShowMessage(string.Format(Se.Language.Project.RenamedX, renamed) +
                          (errors.Length > 0 ? Environment.NewLine + Environment.NewLine + errors : string.Empty));
    }

    // ── OK / Cancel ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task Ok()
    {
        if (string.IsNullOrWhiteSpace(Folder))
        {
            await ShowMessage(Se.Language.Project.FolderRequired);
            return;
        }

        Project.Folder = Folder;
        Project.SeriesYear = int.TryParse(SeriesYearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ? year : null;
        if (string.IsNullOrWhiteSpace(Project.Name))
        {
            Project.Name = Path.GetFileName(Folder);
        }

        try
        {
            Project.Save();
        }
        catch (Exception e)
        {
            await ShowMessage(e.Message);
            return;
        }

        await ProcessRunner.EnsureGitExclude(Folder);
        OkPressed = true;
        Close();
    }

    [RelayCommand]
    private void Cancel()
    {
        Close();
    }

    private void Close()
    {
        Dispatcher.UIThread.Post(() => Window?.Close());
    }

    private async Task ShowMessage(string message)
    {
        if (Window != null)
        {
            await MessageBox.Show(Window, Se.Language.Project.Project.Replace("_", string.Empty), message, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
