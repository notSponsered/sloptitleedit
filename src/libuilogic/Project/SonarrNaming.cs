using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.UiLogic.Project;

public record NamingPreset(string Name, string Template);

public record EpisodeNumber(int Season, int Episode, int? EpisodeEnd, int? Absolute);

/// <summary>
/// Sonarr-style file naming (https://trash-guides.info/Sonarr/Sonarr-recommended-naming-scheme/),
/// episode-number parsing from file names and naming tokens read from a video file.
/// </summary>
public static class SonarrNaming
{
    // TRaSH-Guides docs/json/sonarr/naming/sonarr-naming.json
    public static readonly NamingPreset[] Presets =
    [
        new("TRaSH Standard", "{Series CleanTitleWithoutYear} {(Series Year)} - S{season:00}E{episode:00} - {Episode CleanTitle:90} {[Custom Formats]}{[Quality Full]}{[Mediainfo AudioCodec}{ Mediainfo AudioChannels]}{[MediaInfo VideoDynamicRangeType]}{[Mediainfo VideoCodec]}{-Release Group}"),
        new("TRaSH Anime", "{Series CleanTitleWithoutYear} {(Series Year)} - S{season:00}E{episode:00} - {absolute:000} - {Episode CleanTitle:90} {[Custom Formats]}{[Quality Full]}{[Mediainfo AudioCodec}{ Mediainfo AudioChannels]}{MediaInfo AudioLanguages}{[MediaInfo VideoDynamicRangeType]}[{Mediainfo VideoCodec }{MediaInfo VideoBitDepth}bit]{-Release Group}"),
        new("TRaSH Daily", "{Series CleanTitleWithoutYear} {(Series Year)} - {Air-Date} - {Episode CleanTitle:90} {[Custom Formats]}{[Quality Full]}{[Mediainfo AudioCodec}{ Mediainfo AudioChannels]}{[MediaInfo VideoDynamicRangeType]}{[Mediainfo VideoCodec]}{-Release Group}"),
        new("TRaSH P2P/Scene", "{Series.CleanTitleYear}.S{season:00}E{episode:00}{.Episode.CleanTitle}{.Custom.Formats}{.Quality.Full}{.Mediainfo.AudioCodec}{.Mediainfo.AudioChannels}{.MediaInfo.VideoDynamicRangeType}{.Mediainfo.VideoCodec}{-Release Group}"),
    ];

    public static readonly string[] MultiEpisodeStyles = ["Range", "Prefixed Range", "Extend", "Repeat", "Scene"];

    public static readonly string[] QualityOptions =
    [
        "SDTV", "DVD", "WEBDL-480p", "WEBRip-480p", "Bluray-480p", "Bluray-576p",
        "HDTV-720p", "WEBDL-720p", "WEBRip-720p", "Bluray-720p",
        "HDTV-1080p", "WEBDL-1080p", "WEBRip-1080p", "Bluray-1080p", "Bluray-1080p Remux",
        "HDTV-2160p", "WEBDL-2160p", "WEBRip-2160p", "Bluray-2160p", "Bluray-2160p Remux", "Raw-HD",
    ];

    public static readonly (string Name, string Code)[] Languages =
    [
        ("English", "en"), ("Japanese", "ja"), ("Chinese", "zh"), ("Korean", "ko"), ("Spanish", "es"), ("Portuguese", "pt"),
        ("French", "fr"), ("German", "de"), ("Italian", "it"), ("Russian", "ru"), ("Arabic", "ar"), ("Indonesian", "id"),
        ("Vietnamese", "vi"), ("Thai", "th"), ("Polish", "pl"), ("Dutch", "nl"), ("Swedish", "sv"), ("Turkish", "tr"),
    ];

    /// <summary>Tokens offered in the editor's token grid (any other name can be added freely).</summary>
    public static readonly string[] CommonTokens =
    [
        "Quality Full", "Language", "Language Code", "Release Group", "Custom Formats",
        "MediaInfo VideoCodec", "MediaInfo VideoBitDepth", "MediaInfo VideoDynamicRangeType",
        "MediaInfo AudioCodec", "MediaInfo AudioChannels", "MediaInfo AudioLanguages",
    ];

    private static readonly Regex TokenRegex = new(
        @"(?<escaped>\{\{|\}\})|\{(?<prefix>[- ._\[(]*)(?<token>[a-z0-9]+(?:[- ._]+[a-z0-9]+)*)(?::(?<format>[a-z0-9+-]+))?(?<suffix>[- ._)\]]*)\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SeparatorRegex = new(@"[- ._]+", RegexOptions.Compiled);
    private static readonly Regex YearSuffixRegex = new(@"\s*\(\d{4}\)\s*$", RegexOptions.Compiled);
    private static readonly Regex ScenifyRemoveChars = new(@"(?<=\s)(,|<|>|\/|\\|;|:|'|""|\||`|’|~|!|\?|@|\$|%|\^|\*|-|_|=){1}(?=\s)|('|`|’|\?|:|,|""|\(|\))", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s{2,}", RegexOptions.Compiled);

    public static string NormalizeTokenName(string name) => SeparatorRegex.Replace(name.Trim(), " ").ToLowerInvariant();

    /// <summary>
    /// Builds a relative path (segments separated by '/') without extension, e.g. "Season 1/Show.S01E01-04 English [VS]".
    /// </summary>
    public static string Format(string template, ProjectEpisode ep, SeProject project)
    {
        var result = TokenRegex.Replace(template, m =>
        {
            if (m.Groups["escaped"].Success)
            {
                return m.Value.Substring(0, 1);
            }

            var token = m.Groups["token"].Value;
            var sepMatch = SeparatorRegex.Match(token);
            var separator = sepMatch.Success ? sepMatch.Value.Substring(0, 1) : " ";
            var value = GetValue(NormalizeTokenName(token), m.Groups["format"].Value, separator, ep, project);
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (separator != " ")
            {
                value = value.Replace(" ", separator);
            }

            return m.Groups["prefix"].Value + value + m.Groups["suffix"].Value;
        });

        var segments = result.Replace('\\', '/').Split('/')
            .Select(s => MultiSpaceRegex.Replace(s, " ").Trim(' ', '.', '-', '_'))
            .Where(s => s.Length > 0);
        return string.Join("/", segments);
    }

    private static string GetValue(string key, string format, string separator, ProjectEpisode ep, SeProject project)
    {
        var title = string.IsNullOrWhiteSpace(project.SeriesTitle) ? project.Name : project.SeriesTitle;
        var titleWithoutYear = YearSuffixRegex.Replace(title, string.Empty);
        var year = project.SeriesYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        var titleYear = year.Length > 0 && titleWithoutYear == title ? $"{title} ({year})" : title;

        var value = key switch
        {
            "series title" => title,
            "series titleyear" => titleYear,
            "series titlewithoutyear" => titleWithoutYear,
            "series cleantitle" => CleanTitle(title),
            "series cleantitleyear" => CleanTitle(titleYear),
            "series cleantitlewithoutyear" => CleanTitle(titleWithoutYear),
            "series year" => year,
            "season" => Number(ep.Season, format),
            "episode" => EpisodeRange(ep.Episode, ep.LastEpisode, format, project.MultiEpisodeStyle),
            "absolute" => ep.Absolute is { } abs ? AbsoluteRange(abs, abs + ep.LastEpisode - ep.Episode, format) : string.Empty,
            "episode title" => Truncate(project.PlainTitle(ep.Title), format), // naming works on plain words: the template decides separators
            "episode cleantitle" => Truncate(CleanTitle(project.PlainTitle(ep.Title)), format),
            "air date" => separator == "-" ? ep.AirDate : ep.AirDate.Replace("-", separator),
            "imdbid" => project.ImdbId,
            "tvdbid" => project.TvdbId,
            _ => project.GetToken(ep, key) ?? string.Empty,
        };

        return SanitizeValue(value);
    }

    private static string Number(int n, string format) =>
        format.Length > 0 && format.All(c => c == '0') ? n.ToString(format, CultureInfo.InvariantCulture) : n.ToString(CultureInfo.InvariantCulture);

    private static string EpisodeRange(int first, int last, string format, string style)
    {
        var a = Number(first, format);
        if (last <= first)
        {
            return a;
        }

        var all = Enumerable.Range(first, last - first + 1).Select(n => Number(n, format)).ToList();
        return style switch
        {
            "Prefixed Range" => $"{a}-E{Number(last, format)}",
            "Extend" => string.Join("-", all),
            "Repeat" => string.Join("E", all),
            "Scene" => string.Join("-E", all),
            _ => $"{a}-{Number(last, format)}",
        };
    }

    private static string AbsoluteRange(int first, int last, string format) =>
        last > first ? $"{Number(first, format)}-{Number(last, format)}" : Number(first, format);

    private static string Truncate(string s, string format)
    {
        if (!int.TryParse(format, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) || max <= 0 || s.Length <= max)
        {
            return s;
        }

        return s.Substring(0, max).TrimEnd() + "...";
    }

    /// <summary>Sonarr's CleanTitle: '&amp;' → and, drops quotes/question marks/colons/commas/parentheses and lone punctuation.</summary>
    public static string CleanTitle(string title)
    {
        var s = title.Replace("&", "and").Replace('/', ' ');
        s = ScenifyRemoveChars.Replace(s, string.Empty);
        s = RemoveDiacritics(s);
        return MultiSpaceRegex.Replace(s, " ").Trim();
    }

    private static string RemoveDiacritics(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string SanitizeValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var s = value.Replace(": ", " - ").Replace(':', '-');
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c >= 32 && "\\/*?\"<>|".IndexOf(c) < 0)
            {
                sb.Append(c);
            }
        }

        return MultiSpaceRegex.Replace(sb.ToString(), " ").Trim();
    }

    private static readonly Regex SxxExxRegex = new(@"(?<![a-z0-9])S(?<s>\d{1,2})E(?<e>\d{1,4})(?:-?E(?<x>\d{1,4})|-(?<x>\d{1,4})(?![\dpi]))*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NxNNRegex = new(@"(?<![a-z0-9])(?<s>\d{1,2})x(?<e>\d{2,4})(?:-(?<x>\d{2,4}))?(?![\dp])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex DashNumberRegex = new(@"\s-\s(?<e>\d{1,4})(?:-(?<x>\d{1,4}))?(?=$|[\s.\[(])", RegexOptions.Compiled);
    private static readonly Regex BracketNumberRegex = new(@"\[(?<e>\d{1,4})\]", RegexOptions.Compiled);
    private static readonly Regex EpNumberRegex = new(@"(?<![a-z0-9])(?:E|EP|Episode\s?)(?<e>\d{1,4})(?![\dp])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SeasonFolderRegex = new(@"^(?:Season\s*|S)(?<s>\d{1,2})$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Season/episode(range)/absolute number from a file path: S01E01-04, S01E01-E04, S01E01E02, 1x01-04, anime " - 07" / "[07]".
    /// A parent "Season N" folder supplies the season when the name has none.
    /// </summary>
    public static EpisodeNumber? ParseEpisodeNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        var m = SxxExxRegex.Match(name);
        if (!m.Success)
        {
            m = NxNNRegex.Match(name);
        }

        if (m.Success)
        {
            var e = int.Parse(m.Groups["e"].Value, CultureInfo.InvariantCulture);
            int? end = m.Groups["x"].Captures.Count > 0
                ? int.Parse(m.Groups["x"].Captures[^1].Value, CultureInfo.InvariantCulture)
                : null;
            return new EpisodeNumber(int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture), e, end > e ? end : null, null);
        }

        m = DashNumberRegex.Match(name);
        if (!m.Success)
        {
            m = BracketNumberRegex.Match(name);
        }

        if (!m.Success)
        {
            m = EpNumberRegex.Match(name);
        }

        if (!m.Success)
        {
            return null;
        }

        var number = int.Parse(m.Groups["e"].Value, CultureInfo.InvariantCulture);
        int? last = m.Groups["x"].Success ? int.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture) : null;
        last = last > number ? last : null;

        var folder = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        var seasonMatch = SeasonFolderRegex.Match(folder ?? string.Empty);
        return seasonMatch.Success
            ? new EpisodeNumber(int.Parse(seasonMatch.Groups["s"].Value, CultureInfo.InvariantCulture), number, last, null)
            : new EpisodeNumber(1, number, last, number);
    }

    private static readonly Regex StreamRegex = new(@"Stream #\d+:\d+(?:\[[^\]]*\])?(?:\((?<lang>[a-z]{2,3})\))?(?:\[[^\]]*\])?: (?<type>Video|Audio): (?<codec>\w+)(?<rest>.*)", RegexOptions.Compiled);
    private static readonly Regex ResolutionRegex = new(@"\b(?<w>\d{3,4})x(?<h>\d{3,4})\b", RegexOptions.Compiled);
    private static readonly Regex ChannelsRegex = new(@", (?<c>mono|stereo|\d\.\d)(?:\(\w+\))?(?=,|\s|$)", RegexOptions.Compiled);

    /// <summary>
    /// Naming tokens from an "ffmpeg -i" log plus the video file name (source + release group).
    /// </summary>
    public static Dictionary<string, string> TokensFromVideo(string ffmpegLog, string videoFileName)
    {
        var tokens = new Dictionary<string, string>();
        var name = Path.GetFileNameWithoutExtension(videoFileName);
        var resolution = string.Empty;
        var audioLanguages = new List<string>();
        var hasVideo = false;
        var hasAudio = false;

        foreach (var line in ffmpegLog.Split('\n'))
        {
            var m = StreamRegex.Match(line);
            if (!m.Success)
            {
                continue;
            }

            var codec = m.Groups["codec"].Value.ToLowerInvariant();
            var rest = m.Groups["rest"].Value;
            if (m.Groups["type"].Value == "Video" && !hasVideo && !rest.Contains("attached pic", StringComparison.Ordinal))
            {
                hasVideo = true;
                var r = ResolutionRegex.Match(rest);
                if (r.Success)
                {
                    resolution = ResolutionName(int.Parse(r.Groups["w"].Value, CultureInfo.InvariantCulture), int.Parse(r.Groups["h"].Value, CultureInfo.InvariantCulture));
                }

                tokens["MediaInfo VideoCodec"] = VideoCodecName(codec, name);
                tokens["MediaInfo VideoBitDepth"] = rest.Contains("p12", StringComparison.Ordinal) ? "12" : rest.Contains("p10", StringComparison.Ordinal) ? "10" : "8";
                if (rest.Contains("smpte2084", StringComparison.Ordinal))
                {
                    tokens["MediaInfo VideoDynamicRangeType"] = "HDR10";
                }
                else if (rest.Contains("arib-std-b67", StringComparison.Ordinal))
                {
                    tokens["MediaInfo VideoDynamicRangeType"] = "HLG";
                }
            }
            else if (m.Groups["type"].Value == "Audio")
            {
                if (!hasAudio)
                {
                    hasAudio = true;
                    tokens["MediaInfo AudioCodec"] = AudioCodecName(codec, rest);
                    var c = ChannelsRegex.Match(rest);
                    if (c.Success)
                    {
                        tokens["MediaInfo AudioChannels"] = c.Groups["c"].Value switch { "mono" => "1.0", "stereo" => "2.0", var x => x };
                    }
                }

                var lang = m.Groups["lang"].Value.ToUpperInvariant();
                if (lang.Length > 0 && lang != "UND" && !audioLanguages.Contains(lang))
                {
                    audioLanguages.Add(lang);
                }
            }
        }

        if (audioLanguages.Count > 0)
        {
            tokens["MediaInfo AudioLanguages"] = string.Join("+", audioLanguages);
        }

        var source = SourceName(name);
        if (source.Length > 0 && resolution.Length > 0)
        {
            tokens["Quality Full"] = source == "Remux" ? $"Bluray-{resolution} Remux" : $"{source}-{resolution}";
        }

        var group = Regex.Match(name, @"-(?<g>[A-Za-z][A-Za-z0-9]*)$");
        if (!group.Success)
        {
            group = Regex.Match(name, @"^\[(?<g>[^\]]+)\]");
        }

        if (group.Success)
        {
            tokens["Release Group"] = group.Groups["g"].Value;
        }

        return tokens;
    }

    private static string ResolutionName(int w, int h) =>
        w >= 3200 || h >= 1800 ? "2160p" :
        w >= 1800 || h >= 1000 ? "1080p" :
        w >= 1200 || h >= 700 ? "720p" :
        h >= 560 ? "576p" : "480p";

    private static string VideoCodecName(string codec, string fileName)
    {
        if (Regex.IsMatch(fileName, @"\bx264\b", RegexOptions.IgnoreCase))
        {
            return "x264";
        }

        if (Regex.IsMatch(fileName, @"\bx265\b", RegexOptions.IgnoreCase))
        {
            return "x265";
        }

        return codec switch
        {
            "h264" => "AVC",
            "hevc" => "HEVC",
            "av1" => "AV1",
            "vp9" => "VP9",
            "mpeg2video" => "MPEG2",
            "vc1" => "VC1",
            _ => codec.ToUpperInvariant(),
        };
    }

    private static string AudioCodecName(string codec, string rest) => codec switch
    {
        "flac" => "FLAC",
        "aac" => "AAC",
        "ac3" => "AC3",
        "eac3" => rest.Contains("Atmos", StringComparison.OrdinalIgnoreCase) ? "EAC3 Atmos" : "EAC3",
        "truehd" => rest.Contains("Atmos", StringComparison.OrdinalIgnoreCase) ? "TrueHD Atmos" : "TrueHD",
        "dts" => rest.Contains("DTS-HD MA", StringComparison.Ordinal) ? "DTS-HD MA" : "DTS",
        "opus" => "Opus",
        "mp3" => "MP3",
        "vorbis" => "Vorbis",
        _ when codec.StartsWith("pcm", StringComparison.Ordinal) => "PCM",
        _ => codec.ToUpperInvariant(),
    };

    private static string SourceName(string fileName)
    {
        bool Has(string pattern) => Regex.IsMatch(fileName, pattern, RegexOptions.IgnoreCase);

        if (Has(@"remux"))
        {
            return "Remux";
        }

        if (Has(@"blu-?ray|bdrip|brrip|(?<![a-z])bd(?![a-z])"))
        {
            return "Bluray";
        }

        if (Has(@"webrip"))
        {
            return "WEBRip";
        }

        if (Has(@"web-?dl|(?<![a-z])web(?![a-z])"))
        {
            return "WEBDL";
        }

        if (Has(@"hdtv"))
        {
            return "HDTV";
        }

        return string.Empty;
    }
}
