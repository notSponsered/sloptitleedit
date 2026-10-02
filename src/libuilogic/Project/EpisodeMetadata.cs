using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nikse.SubtitleEdit.UiLogic.Project;

public record EpisodeInfo(int Season, int Episode, int? Absolute, string Title, string AirDate);

public record ShowSearchResult(string Source, int Id, string Name, int? Year, string TvdbId, string ImdbId, List<EpisodeInfo>? Episodes)
{
    public string Display => Year is { } y ? $"{Name} ({y})" : Name;
}

/// <summary>
/// Episode metadata from TVmaze and AniList (both free, no API key). HTTP is done by the caller.
/// </summary>
public static class EpisodeMetadata
{
    public const string TvMaze = "TVmaze";
    public const string AniList = "AniList";
    public const string AniListUrl = "https://graphql.anilist.co";

    public static string TvMazeSearchUrl(string query) => "https://api.tvmaze.com/search/shows?q=" + Uri.EscapeDataString(query);

    public static string TvMazeEpisodesUrl(int showId) => $"https://api.tvmaze.com/shows/{showId}/episodes";

    public static string AniListSearchBody(string query)
    {
        const string gql = "query($q:String){Page(perPage:15){media(search:$q,type:ANIME){id title{romaji english} startDate{year} episodes streamingEpisodes{title}}}}";
        return JsonSerializer.Serialize(new { query = gql, variables = new { q = query } });
    }

    public static List<ShowSearchResult> ParseTvMazeSearch(string json)
    {
        var list = new List<ShowSearchResult>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("show", out var show))
            {
                continue;
            }

            var externals = show.TryGetProperty("externals", out var e) ? e : default;
            list.Add(new ShowSearchResult(
                TvMaze,
                show.GetProperty("id").GetInt32(),
                Str(show, "name"),
                Year(Str(show, "premiered")),
                externals.ValueKind == JsonValueKind.Object ? Str(externals, "thetvdb") : string.Empty,
                externals.ValueKind == JsonValueKind.Object ? Str(externals, "imdb") : string.Empty,
                null));
        }

        return list;
    }

    /// <summary>Regular episodes (specials without a number are skipped); absolute = running number over seasons ≥ 1.</summary>
    public static List<EpisodeInfo> ParseTvMazeEpisodes(string json)
    {
        var list = new List<EpisodeInfo>();
        using var doc = JsonDocument.Parse(json);
        var absolute = 0;
        foreach (var ep in doc.RootElement.EnumerateArray())
        {
            if (!ep.TryGetProperty("number", out var number) || number.ValueKind != JsonValueKind.Number ||
                !ep.TryGetProperty("season", out var season) || season.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var s = season.GetInt32();
            int? abs = s >= 1 ? ++absolute : null;
            list.Add(new EpisodeInfo(s, number.GetInt32(), abs, Str(ep, "name"), Str(ep, "airdate")));
        }

        return list;
    }

    public static List<ShowSearchResult> ParseAniListSearch(string json)
    {
        var list = new List<ShowSearchResult>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("Page", out var page) ||
            !page.TryGetProperty("media", out var media))
        {
            return list;
        }

        var titleRegex = new Regex(@"^Episode\s+(?<n>\d+)\s*[-–:]\s*(?<t>.+)$", RegexOptions.IgnoreCase);
        foreach (var m in media.EnumerateArray())
        {
            var titles = m.GetProperty("title");
            var name = Str(titles, "english");
            if (name.Length == 0)
            {
                name = Str(titles, "romaji");
            }

            int? year = m.TryGetProperty("startDate", out var sd) && sd.TryGetProperty("year", out var y) && y.ValueKind == JsonValueKind.Number
                ? y.GetInt32()
                : null;

            var streamTitles = new Dictionary<int, string>();
            if (m.TryGetProperty("streamingEpisodes", out var se) && se.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in se.EnumerateArray())
                {
                    var match = titleRegex.Match(Str(s, "title"));
                    if (match.Success)
                    {
                        streamTitles.TryAdd(int.Parse(match.Groups["n"].Value), match.Groups["t"].Value.Trim());
                    }
                }
            }

            var count = m.TryGetProperty("episodes", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetInt32()
                : streamTitles.Keys.DefaultIfEmpty(0).Max();
            var episodes = Enumerable.Range(1, count)
                .Select(n => new EpisodeInfo(1, n, n, streamTitles.GetValueOrDefault(n, string.Empty), string.Empty))
                .ToList();

            list.Add(new ShowSearchResult(AniList, m.GetProperty("id").GetInt32(), name, year, string.Empty, string.Empty, episodes));
        }

        return list;
    }

    /// <summary>
    /// Sets series info and fills entry titles/air dates/absolute numbers. Range entries get every covered title
    /// joined with " + " and then formatted with <see cref="SeProject.FormatTitle"/>. Episodes no entry covers are added.
    /// File mappings and tokens are kept.
    /// </summary>
    public static void MergeInto(SeProject project, ShowSearchResult show, List<EpisodeInfo> episodes)
    {
        project.SeriesTitle = show.Name;
        project.SeriesYear = show.Year ?? project.SeriesYear;
        if (show.TvdbId.Length > 0)
        {
            project.TvdbId = show.TvdbId;
        }

        if (show.ImdbId.Length > 0)
        {
            project.ImdbId = show.ImdbId;
        }

        if (show.Source == TvMaze)
        {
            project.TvmazeId = show.Id;
        }
        else
        {
            project.AnilistId = show.Id;
        }

        project.ReformatTitles(); // titles not covered by this import follow the current separator too
        foreach (var entry in project.Episodes)
        {
            var covered = episodes.Where(e => entry.Covers(e.Season, e.Episode)).OrderBy(e => e.Episode).ToList();
            if (covered.Count == 0)
            {
                continue;
            }

            var titles = covered.Select(e => e.Title).Where(t => t.Length > 0).ToList();
            if (titles.Count > 0)
            {
                entry.Title = project.FormatTitle(string.Join(" + ", titles));
            }

            entry.AirDate = covered[0].AirDate.Length > 0 ? covered[0].AirDate : entry.AirDate;
            entry.Absolute = covered[0].Absolute ?? entry.Absolute;
        }

        var added = episodes
            .Where(e => !project.Episodes.Any(p => p.Covers(e.Season, e.Episode)))
            .Select(e => new ProjectEpisode { Season = e.Season, Episode = e.Episode, Absolute = e.Absolute, Title = project.FormatTitle(e.Title), AirDate = e.AirDate })
            .ToList();
        if (added.Count == 0)
        {
            return;
        }

        var sorted = project.Episodes.Concat(added).OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();
        project.Episodes.Clear();
        foreach (var e in sorted)
        {
            project.Episodes.Add(e);
        }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? string.Empty,
                JsonValueKind.Number => v.GetRawText(),
                _ => string.Empty,
            }
            : string.Empty;

    private static int? Year(string date) => date.Length >= 4 && int.TryParse(date.AsSpan(0, 4), out var y) ? y : null;
}
