namespace Nikse.SubtitleEdit.UiLogic.Assa;

/// <summary>
/// Read/write "Key: value" lines in an ASS header's [Script Info] section. Unlike the libse header
/// helpers, keys may contain spaces ("YCbCr Matrix").
/// </summary>
public static class AssaScriptInfo
{
    private const string Section = "[Script Info]";

    public static string? Get(string? header, string key)
    {
        var inSection = false;
        foreach (var line in (header ?? string.Empty).Split('\n'))
        {
            var s = line.Trim();
            if (s.StartsWith('['))
            {
                inSection = s.Equals(Section, StringComparison.OrdinalIgnoreCase);
            }
            else if (inSection && KeyOf(s) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return s[(s.IndexOf(':') + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>Replaces the key's line, or adds it at the end of [Script Info] (creating the section if needed).</summary>
    public static string Set(string? header, string key, string value)
    {
        header ??= string.Empty;
        var newLine = header.Contains("\r\n") ? "\r\n" : "\n";
        var entry = $"{key}: {value}";
        var lines = header.Replace("\r\n", "\n").Split('\n').ToList();

        var start = lines.FindIndex(l => l.Trim().Equals(Section, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return Section + newLine + entry + newLine + (header.Length > 0 ? newLine + header : string.Empty);
        }

        var end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0)
        {
            end = lines.Count;
        }

        var lastContent = start;
        for (var i = start + 1; i < end; i++)
        {
            var s = lines[i].Trim();
            if (KeyOf(s) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = entry;
                return string.Join(newLine, lines);
            }

            if (s.Length > 0)
            {
                lastContent = i;
            }
        }

        lines.Insert(lastContent + 1, entry);
        return string.Join(newLine, lines);
    }

    private static string? KeyOf(string line)
    {
        if (line.StartsWith(';'))
        {
            return null; // comment
        }

        var colon = line.IndexOf(':');
        return colon > 0 ? line[..colon].Trim() : null;
    }
}
