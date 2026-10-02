using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.SubtitleFormats;

namespace Nikse.SubtitleEdit.UiLogic.Project;

/// <summary>
/// One project entry: a single episode or an episode range (e.g. S01E01-04) mapped to a video and an .ass file.
/// </summary>
public partial class ProjectEpisode : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Tag), nameof(Display))] private int _season = 1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Tag), nameof(Display), nameof(EpisodeText))] private int _episode = 1;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Tag), nameof(Display), nameof(EpisodeText))] private int? _episodeEnd;
    [ObservableProperty] private int? _absolute;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Display))] private string _title = string.Empty;
    [ObservableProperty] private string _airDate = string.Empty;
    [ObservableProperty] private string _video = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;

    public Dictionary<string, string> Tokens { get; set; } = new();
    public int SelectedLine { get; set; }
    public int AudioTrack { get; set; } = -1;
    public string Notes { get; set; } = string.Empty;

    [JsonIgnore] public int LastEpisode => EpisodeEnd is { } end && end > Episode ? end : Episode;

    [JsonIgnore]
    public string Tag => LastEpisode > Episode
        ? $"S{Season:00}E{Episode:00}-{LastEpisode:00}"
        : $"S{Season:00}E{Episode:00}";

    [JsonIgnore] public string Display => string.IsNullOrWhiteSpace(Title) ? Tag : $"{Tag} - {Title}";

    /// <summary>
    /// The episode or range as one editable value: "5" or "1-4" (the editor's Episode column). Stored as
    /// <see cref="Episode"/> + <see cref="EpisodeEnd"/>; text that doesn't parse is ignored and the cell shows the old value.
    /// </summary>
    [JsonIgnore]
    public string EpisodeText
    {
        get => LastEpisode > Episode ? $"{Episode}-{LastEpisode}" : Episode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (TryParseEpisodeRange(value, out var first, out var last))
            {
                Episode = first;
                EpisodeEnd = last;
            }

            OnPropertyChanged(); // always: the grid re-reads the (normalized or unchanged) value
        }
    }

    /// <summary>
    /// Parses what someone types into the Episode cell. <paramref name="last"/> is null for a single episode.
    /// Returns false when the text isn't a usable episode or range (the edit is then dropped).
    /// </summary>
    public static bool TryParseEpisodeRange(string? text, out int first, out int? last)
    {
        first = 0;
        last = null;

        // "5", "1-4", "1 – 4", "E01-E04" (episode 0 = specials); "4-4" is just episode 4, "4-1" is refused
        var parts = (text ?? string.Empty).Split(['-', '–', '—', '~'], StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2 || !TryParseEpisode(parts[0], out first))
        {
            return false;
        }

        if (parts.Length == 1)
        {
            return true;
        }

        if (!TryParseEpisode(parts[1], out var end) || end < first)
        {
            return false;
        }

        last = end > first ? end : null;
        return true;

        static bool TryParseEpisode(string s, out int value) =>
            int.TryParse(s.TrimStart('E', 'e'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    public bool Covers(int season, int episode) => Season == season && episode >= Episode && episode <= LastEpisode;
}

/// <summary>
/// Project sidecar (".seproject.json" in the project folder). Local only, kept out of git via .git/info/exclude.
/// </summary>
public partial class SeProject : ObservableObject
{
    public const string FileName = ".seproject.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public int Version { get; set; } = 1;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _seriesTitle = string.Empty;
    [ObservableProperty] private int? _seriesYear;
    [ObservableProperty] private string _tvdbId = string.Empty;
    [ObservableProperty] private string _imdbId = string.Empty;
    public int TvmazeId { get; set; }
    public int AnilistId { get; set; }
    [ObservableProperty] private string _namingTemplate = SonarrNaming.Presets[0].Template;
    [ObservableProperty] private string _multiEpisodeStyle = SonarrNaming.MultiEpisodeStyles[0];

    /// <summary>
    /// What spaces and the " + " between joined range titles become when titles are imported ("Meow.Escape.Hey!").
    /// Empty = keep titles as they come.
    /// </summary>
    [ObservableProperty] private string _titleSeparator = ".";

    /// <summary>Optional link to the GitHub panel (Project menu entry, opens beside the Project panel). Off = independent.</summary>
    [ObservableProperty] private bool _useGitHub;
    public Dictionary<string, string> Tokens { get; set; } = new();
    public Dictionary<int, Dictionary<string, string>> SeasonTokens { get; set; } = new();
    public string? StyleHeader { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int CurrentEpisode { get; set; }
    public ObservableCollection<ProjectEpisode> Episodes { get; set; } = new();

    [JsonIgnore] public string Folder { get; set; } = string.Empty;
    [JsonIgnore] public string FilePath => Path.Combine(Folder, FileName);

    public static bool Exists(string folder) => File.Exists(Path.Combine(folder, FileName));

    public static SeProject? Load(string folder)
    {
        var path = Path.Combine(folder, FileName);
        return File.Exists(path) ? FromJson(File.ReadAllText(path), folder) : null;
    }

    public static SeProject FromJson(string json, string folder)
    {
        var project = JsonSerializer.Deserialize<SeProject>(json, JsonOptions) ?? new SeProject();
        project.Folder = folder;
        return project;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public SeProject Clone() => FromJson(ToJson(), Folder);

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, ToJson(), new UTF8Encoding(false));
        File.Move(tmp, FilePath, true);
    }

    public string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Folder, path));
    }

    /// <summary>
    /// Relative to the project folder when inside it (subtitles in the repo), otherwise absolute (videos).
    /// </summary>
    public string MakeRelative(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return string.Empty;
        }

        var rel = Path.GetRelativePath(Folder, fullPath);
        return rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel) ? fullPath : rel.Replace('\\', '/');
    }

    private static readonly System.Text.RegularExpressions.Regex TitleGapRegex = new(@"\s*\+\s*|\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The separator the stored titles are currently formatted with (set by import and <see cref="ReformatTitles"/>);
    /// null = plain titles. Makes the formatting reversible: <see cref="PlainTitle"/> turns it back into spaces.
    /// </summary>
    public string? TitlesFormattedWith { get; set; }

    /// <summary>
    /// Spaces and "+" joins → <see cref="TitleSeparator"/> ("Meow + Escape" → "Meow.Escape"); empty separator = unchanged.
    /// A gap right after an existing separator is dropped ("Dr. Stone" → "Dr.Stone"); the separator is used literally.
    /// </summary>
    public string FormatTitle(string title)
    {
        var trimmed = title.Trim();
        var separator = TitleSeparator;
        if (string.IsNullOrEmpty(separator))
        {
            return trimmed;
        }

        return TitleGapRegex.Replace(trimmed, m => trimmed.AsSpan(0, m.Index).EndsWith(separator, StringComparison.Ordinal) ? string.Empty : separator);
    }

    /// <summary>A stored title as plain words: the separator it was formatted with becomes a space again.</summary>
    public string PlainTitle(string title) =>
        string.IsNullOrEmpty(TitlesFormattedWith) ? title : title.Replace(TitlesFormattedWith, " ", StringComparison.Ordinal);

    /// <summary>Formats every stored title with the current separator; repeatable, and switches from a previous one.</summary>
    public void ReformatTitles()
    {
        foreach (var ep in Episodes)
        {
            ep.Title = FormatTitle(PlainTitle(ep.Title));
        }

        TitlesFormattedWith = TitleSeparator;
    }

    /// <summary>The entry starting at (or covering) this episode, or a new one inserted in season/episode order.</summary>
    public ProjectEpisode GetOrAddEntry(EpisodeNumber n)
    {
        var entry = Episodes.FirstOrDefault(e => e.Season == n.Season && e.Episode == n.Episode) ??
                    Episodes.FirstOrDefault(e => e.Covers(n.Season, n.Episode));
        if (entry == null)
        {
            entry = new ProjectEpisode { Season = n.Season, Episode = n.Episode, EpisodeEnd = n.EpisodeEnd, Absolute = n.Absolute };
            InsertSorted(entry);
        }
        else if (n.EpisodeEnd != null && entry.EpisodeEnd == null && entry.Episode == n.Episode)
        {
            entry.EpisodeEnd = n.EpisodeEnd;
        }

        return entry;
    }

    public void InsertSorted(ProjectEpisode ep)
    {
        var index = 0;
        while (index < Episodes.Count &&
               (Episodes[index].Season < ep.Season || Episodes[index].Season == ep.Season && Episodes[index].Episode <= ep.Episode))
        {
            index++;
        }

        Episodes.Insert(index, ep);
    }

    /// <summary>Maps every numbered .ass file below <paramref name="folder"/> to its entry (adding entries). Returns the count.</summary>
    public int MapSubtitles(string folder)
    {
        var files = ProjectRepo.EpisodeFiles(folder);
        foreach (var (path, number) in files)
        {
            GetOrAddEntry(number).Subtitle = MakeRelative(path);
        }

        return files.Count;
    }

    public ProjectEpisode? FindBySubtitle(string? fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return null;
        }

        var target = Path.GetFullPath(fullPath);
        return Episodes.FirstOrDefault(e => !string.IsNullOrEmpty(e.Subtitle) &&
                                            string.Equals(ResolvePath(e.Subtitle), target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Token value with episode → season → project precedence. Names compare case-insensitively, ignoring separators.
    /// </summary>
    public string? GetToken(ProjectEpisode? ep, string name)
    {
        return Find(ep?.Tokens, name) ??
               (ep != null && SeasonTokens.TryGetValue(ep.Season, out var season) ? Find(season, name) : null) ??
               Find(Tokens, name);

        static string? Find(Dictionary<string, string>? dict, string key)
        {
            if (dict == null)
            {
                return null;
            }

            var normalized = SonarrNaming.NormalizeTokenName(key);
            foreach (var kv in dict)
            {
                if (!string.IsNullOrEmpty(kv.Value) && SonarrNaming.NormalizeTokenName(kv.Key) == normalized)
                {
                    return kv.Value;
                }
            }

            return null;
        }
    }

    public static void EnsureExcludeLine(string excludeFilePath)
    {
        if (File.Exists(excludeFilePath) &&
            File.ReadAllLines(excludeFilePath).Any(l => l.Trim() == FileName))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(excludeFilePath)!);
        var existing = File.Exists(excludeFilePath) ? File.ReadAllText(excludeFilePath) : string.Empty;
        var prefix = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : string.Empty;
        File.AppendAllText(excludeFilePath, prefix + FileName + "\n");
    }

    /// <summary>
    /// Replaces/adds the master styles in an .ass file's [V4+ Styles] section, editing only those "Style:" lines
    /// (in the file's own Format order) so everything else stays byte-identical for clean git diffs.
    /// </summary>
    public static string ApplyStylesToFileText(string assText, string masterHeader)
    {
        var masterStyles = AdvancedSubStationAlpha.GetSsaStylesFromHeader(masterHeader);
        if (masterStyles.Count == 0)
        {
            return assText;
        }

        var newLine = assText.Contains("\r\n") ? "\r\n" : "\n";
        var lines = assText.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var sectionStart = lines.FindIndex(l => l.Trim().Equals("[V4+ Styles]", StringComparison.OrdinalIgnoreCase));
        if (sectionStart < 0)
        {
            var eventsIndex = lines.FindIndex(l => l.Trim().Equals("[Events]", StringComparison.OrdinalIgnoreCase));
            var insertAt = eventsIndex < 0 ? lines.Count : eventsIndex;
            var block = new List<string> { "[V4+ Styles]", SsaStyle.DefaultAssStyleFormat };
            block.AddRange(masterStyles.Select(s => s.ToRawAss()));
            block.Add(string.Empty);
            lines.InsertRange(insertAt, block);
            return string.Join(newLine, lines);
        }

        var sectionEnd = lines.FindIndex(sectionStart + 1, l => l.TrimStart().StartsWith('['));
        if (sectionEnd < 0)
        {
            sectionEnd = lines.Count;
        }

        var format = SsaStyle.DefaultAssStyleFormat;
        var lastStyleLine = sectionStart;
        for (var i = sectionStart + 1; i < sectionEnd; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                format = "Format: " + t.Substring(7).Trim();
                lastStyleLine = Math.Max(lastStyleLine, i);
            }
            else if (t.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
            {
                lastStyleLine = i;
            }
        }

        foreach (var style in masterStyles)
        {
            var raw = style.ToRawAss(format);
            var index = -1;
            for (var i = sectionStart + 1; i < sectionEnd; i++)
            {
                if (StyleName(lines[i]) == style.Name)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                lines[index] = raw;
            }
            else
            {
                lastStyleLine++;
                lines.Insert(lastStyleLine, raw);
                sectionEnd++;
            }
        }

        return string.Join(newLine, lines);

        static string? StyleName(string line)
        {
            var t = line.TrimStart();
            if (!t.StartsWith("Style:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var comma = t.IndexOf(',');
            return comma < 0 ? null : t.Substring(6, comma - 6).Trim();
        }
    }
}
